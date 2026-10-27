using System;
using System.Linq;
using System.Runtime.InteropServices;
using DSOptimizer.Core.Abstractions;
using DSOptimizer.Core.Model;
using DSOptimizer.Core.RestorePoints;
using DSOptimizer.Tests.Unit.Fakes;
using Xunit;

namespace DSOptimizer.Tests.Unit.RestorePoints;

#pragma warning disable CA2201 // COMException jest tu celowo: tak zgłasza błąd WMI/COM prawdziwe API
public class RestorePointServiceTests
{
    private const string Key = RestorePointService.FrequencyKeyPath;
    private const string Value = RestorePointService.FrequencyValueName;
    private readonly FakeRegistry _registry = new();
    private readonly FakeRestorePointApi _api;
    private readonly RestorePointService _service;
    private readonly FakeClock _clock = new();
    private readonly FakeSleeper _sleeper;
    private readonly ListLog _log = new();

    public RestorePointServiceTests()
    {
        _api = new FakeRestorePointApi(_registry);
        _sleeper = new FakeSleeper(_clock);
        _service = new RestorePointService(_registry, _api, _log, _clock, _sleeper.Sleep);
    }

    [Fact]
    public void Create_ZeroesFrequencyDuringCallAndRestoresPrevious()
    {
        _registry.Set(Key, Value, RegValue.FromDWord(1440));

        var result = _service.Create("test");

        Assert.Equal(RestorePointStatus.Created, result.Status);
        Assert.Equal(RegValue.FromDWord(0), Assert.Single(_api.FrequencyAtCall));
        Assert.Equal(RegValue.FromDWord(1440), _registry.Get(Key, Value));
    }

    [Fact]
    public void Create_PreviouslyAbsent_DeletesValueAfterwards()
    {
        _service.Create("test");
        Assert.Equal(RegValue.Absent, _registry.Get(Key, Value));
    }

    [Fact]
    public void Create_ApiErrorCode_FailsWithCode()
    {
        _api.ReturnCode = 2;

        var result = _service.Create("test");

        Assert.Equal(RestorePointStatus.Failed, result.Status);
        Assert.Contains("2", result.Message, StringComparison.Ordinal);
        Assert.Equal(2u, result.Code);
        Assert.Equal(RegValue.Absent, _registry.Get(Key, Value));
    }

    [Fact]
    public void Create_Success_CodeIsZero()
    {
        _api.ReturnCode = 0;

        var result = _service.Create("test");

        Assert.Equal(RestorePointStatus.Created, result.Status);
        Assert.Equal(0u, result.Code);
    }

    [Fact]
    public void Create_ApiThrows_CodeIsNull()
    {
        _api.ThrowOnCall = new InvalidOperationException();

        var result = _service.Create("test");

        Assert.Equal(RestorePointStatus.Failed, result.Status);
        Assert.Null(result.Code);
    }

    [Fact]
    public void Create_ApiThrows_FailsAndRestoresFrequency()
    {
        _registry.Set(Key, Value, RegValue.FromDWord(5));
        _api.ThrowOnCall = new InvalidOperationException("WMI padło");

        var result = _service.Create("test");

        Assert.Equal(RestorePointStatus.Failed, result.Status);
        Assert.Contains("WMI padło", result.Message, StringComparison.Ordinal);
        Assert.Equal(RegValue.FromDWord(5), _registry.Get(Key, Value));
    }

    [Fact]
    public void Create_FrequencyUnreadable_FailsWithoutCallingApi()
    {
        _registry.DenyRead(Key, Value);

        Assert.Equal(RestorePointStatus.Failed, _service.Create("test").Status);
        Assert.Empty(_api.FrequencyAtCall);
    }

    [Fact]
    public void Create_PointVisibleImmediately_CreatedWithoutSleeping()
    {
        var result = _service.Create("test");

        Assert.Equal(RestorePointStatus.Created, result.Status);
        Assert.Equal(0u, result.Code);
        Assert.Empty(_sleeper.Sleeps);
        Assert.Equal(2, _api.ReadCount); // przed + jeden po
    }

    [Fact]
    public void Create_PointAppearsAfterSeveralReads_Created()
    {
        _api.AppearsOnReadNumber = 4;

        var result = _service.Create("test");

        Assert.Equal(RestorePointStatus.Created, result.Status);
        Assert.Equal(3, _sleeper.Sleeps.Count);
        Assert.All(_sleeper.Sleeps, s => Assert.Equal(RestorePointService.PollInterval, s));
        Assert.Contains(_log.Lines, e => e.StartsWith("INFO", StringComparison.Ordinal) && e.IndexOf("numer sekwencji 100", StringComparison.Ordinal) >= 0);
    }

    [Fact]
    public void Create_PointNeverAppears_FailsWithCodeZeroAfterTimeout()
    {
        _registry.Set(Key, Value, RegValue.FromDWord(1440));
        _api.AppearsOnReadNumber = null;

        var result = _service.Create("test");

        Assert.Equal(RestorePointStatus.Failed, result.Status);
        Assert.Equal(0u, result.Code);
        Assert.Contains("nie pojawi", result.Message, StringComparison.Ordinal);
        Assert.Contains("10 s", result.Message, StringComparison.Ordinal);
        var expectedPolls = (int)(RestorePointService.PollTimeout.TotalMilliseconds / RestorePointService.PollInterval.TotalMilliseconds) + 1;
        Assert.Equal(1 + expectedPolls, _api.ReadCount);
        Assert.Equal(RestorePointService.PollTimeout, TimeSpan.FromTicks(_sleeper.Sleeps.Sum(s => s.Ticks)));
        Assert.Equal(RegValue.FromDWord(1440), _registry.Get(Key, Value));
    }

    [Fact]
    public void Create_ExistingPointWithSameDescription_IsIgnored()
    {
        // 24-godzinny limit: Create zwraca 0, nowego punktu brak, a starszy punkt DSOptimizera ma identyczny opis.
        _api.Points.Add(new RestorePointInfo(5, "test"));
        _api.AppearsOnReadNumber = null;

        var result = _service.Create("test");

        Assert.Equal(RestorePointStatus.Failed, result.Status);
        Assert.Equal(0u, result.Code);
        Assert.Contains("nie pojawi", result.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Create_NewPointWithDifferentDescription_DoesNotCount()
    {
        _api.ListedDescription = "inny opis";

        var result = _service.Create("test");

        Assert.Equal(RestorePointStatus.Failed, result.Status);
        Assert.Equal(0u, result.Code);
    }

    [Fact]
    public void Create_DescriptionComparedCaseSensitively()
    {
        _api.ListedDescription = "TEST";

        Assert.Equal(RestorePointStatus.Failed, _service.Create("test").Status);
    }

    [Fact]
    public void Create_ListUnreadableBefore_FailsWithoutCallingCreate()
    {
        _registry.Set(Key, Value, RegValue.FromDWord(1440));
        _api.ThrowOnRead = new InvalidOperationException("WMI niedostepne");

        var result = _service.Create("test");

        Assert.Equal(RestorePointStatus.Failed, result.Status);
        Assert.Equal(0, _api.CreateCalls);
        Assert.Contains("WMI niedostepne", result.Message, StringComparison.Ordinal);
        Assert.Contains("listy punkt", result.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(RegValue.FromDWord(1440), _registry.Get(Key, Value));
    }

    [Fact]
    public void Create_ListUnreadableWhilePolling_Fails()
    {
        _api.ThrowOnRead = new InvalidOperationException("RPC padl");
        _api.ThrowOnReadNumber = 2;

        var result = _service.Create("test");

        Assert.Equal(RestorePointStatus.Failed, result.Status);
        Assert.Equal(0u, result.Code);
        Assert.Equal(1, _api.CreateCalls);
        Assert.Contains("RPC padl", result.Message, StringComparison.Ordinal);
        Assert.Equal(RegValue.Absent, _registry.Get(Key, Value));
    }

    [Fact]
    public void Create_ProtectionDisabledHResult_MapsToProtectionDisabled1058()
    {
        _registry.Set(Key, Value, RegValue.FromDWord(7));
        _api.ThrowOnCall = new COMException(string.Empty, unchecked((int)0x80070422));

        var result = _service.Create("test");

        Assert.Equal(RestorePointStatus.ProtectionDisabled, result.Status);
        Assert.Equal(1058u, result.Code);
        Assert.Contains("ochrona systemu", result.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Contains(_log.Lines, e => e.IndexOf("WARN", StringComparison.OrdinalIgnoreCase) >= 0);
        Assert.DoesNotContain(_log.Lines, e => e.IndexOf("ERROR", StringComparison.OrdinalIgnoreCase) >= 0);
        Assert.Equal(RegValue.FromDWord(7), _registry.Get(Key, Value));
    }

    [Fact]
    public void Create_OtherExceptionWithEmptyMessage_MessageHasTypeAndHResult()
    {
        _api.ThrowOnCall = new COMException(string.Empty, unchecked((int)0x80041001));

        var result = _service.Create("test");

        Assert.Equal(RestorePointStatus.Failed, result.Status);
        Assert.Contains("COMException", result.Message, StringComparison.Ordinal);
        Assert.Contains("0x80041001", result.Message, StringComparison.OrdinalIgnoreCase);
    }
}
