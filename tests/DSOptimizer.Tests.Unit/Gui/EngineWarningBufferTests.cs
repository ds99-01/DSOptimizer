using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.InteropServices;
using DSOptimizer.Core.Abstractions;
using DSOptimizer.Core.Catalog;
using DSOptimizer.Core.Engine;
using DSOptimizer.Core.Errors;
using DSOptimizer.Core.Gui;
using DSOptimizer.Core.Localization;
using DSOptimizer.Core.Model;
using DSOptimizer.Core.RestorePoints;
using DSOptimizer.Tests.Unit.Engine;
using DSOptimizer.Tests.Unit.Fakes;
using Xunit;

namespace DSOptimizer.Tests.Unit.Gui;

#pragma warning disable CA2201 // COMException jest tu celowo: tak zgłasza błąd WMI/COM prawdziwe API
public class EngineWarningBufferTests
{
    private static readonly DateTimeOffset At = new(2026, 10, 19, 12, 0, 0, TimeSpan.Zero);

    private static GuiLogLine Warning(string text) => new(At, GuiLogLevel.Warning, text);

    private static GuiOperationReport Report(params string[] lines) => new()
    {
        TweakId = "t.one",
        Operation = GuiOperation.Apply,
        Lines = lines.Select(Warning).ToList(),
    };

    [Theory]
    [InlineData("t.one: Failed")]
    [InlineData("t.one: FailedRolledBack – Odmowa dostępu.")]
    [InlineData("t.two: Refused – Tweak jest oznaczony jako nieodwracalny.")]
    public void IsRepeatedByReport_FinishLineOfOperationTweak_IsRepeated(string message)
        => Assert.True(EngineWarningBuffer.IsRepeatedByReport(message, Report("Raport"), new[] { "t.one", "t.two" }));

    [Theory]
    [InlineData("t.other: Failed – spoza operacji")]
    [InlineData("t.one: 5")]
    [InlineData("t.one: Coś innego")]
    [InlineData("t.one:Failed")]
    public void IsRepeatedByReport_OtherLines_AreNotRepeated(string message)
        => Assert.False(EngineWarningBuffer.IsRepeatedByReport(message, Report("Raport"), new[] { "t.one" }));

    [Fact]
    public void IsRepeatedByReport_TextContainedInReportLine_IsRepeated()
    {
        var report = Report("Punkt przywracania nie powstał: brak listy.");
        Assert.True(EngineWarningBuffer.IsRepeatedByReport("brak listy.", report, Array.Empty<string>()));
        Assert.False(EngineWarningBuffer.IsRepeatedByReport("Nie udało się przywrócić X: odmowa", report, Array.Empty<string>()));
    }

    [Fact]
    public void Release_KeepsOnlyNotRepeated_InOrder_ThenClosesBuffer()
    {
        var buffer = new EngineWarningBuffer();
        Assert.True(buffer.TryAdd(Warning("t.one: Failed – x")));
        Assert.True(buffer.TryAdd(Warning("Pierwsze")));
        Assert.True(buffer.TryAdd(Warning("Powtórzone")));
        Assert.True(buffer.TryAdd(Warning("Drugie")));

        var shown = buffer.Release(Report("Raport: Powtórzone"), new[] { "t.one" });

        Assert.Equal(new[] { "Pierwsze", "Drugie" }, shown.Select(line => line.Text));
        Assert.False(buffer.TryAdd(Warning("Po operacji")));
        Assert.Empty(buffer.Release(null, Array.Empty<string>()));
    }

    [Fact]
    public void Release_WithoutReport_ReturnsEverything()
    {
        var buffer = new EngineWarningBuffer();
        buffer.TryAdd(Warning("t.one: Failed"));

        Assert.Single(buffer.Release(null, new[] { "t.one" }));
    }

    [Fact]
    public void Release_RejectsNullIds()
        => Assert.Throws<ArgumentNullException>(() => new EngineWarningBuffer().Release(null, null!));

    [Fact]
    public void TryAdd_RejectsNull()
        => Assert.Throws<ArgumentNullException>(() => new EngineWarningBuffer().TryAdd(null!));

    /// <summary>
    /// Cała ścieżka na prawdziwym RestorePointService: ostrzeżenie, że nie przywrócono SystemRestorePointCreationFrequency,
    /// nie ma w raporcie, więc po operacji zostaje (wcześniej tłumione bez śladu w oknie).
    /// </summary>
    [Fact]
    public void Apply_FrequencyNotRestored_WarningSurvives()
    {
        var f = new EngineFixture();
        f.Registry.Set(RestorePointService.FrequencyKeyPath, RestorePointService.FrequencyValueName, RegValue.FromDWord(1440));
        f.Registry.WriteInterceptor = (_, name, value) =>
            name == RestorePointService.FrequencyValueName && value.Equals(RegValue.FromDWord(1440))
                ? new OperationException(OperationErrorKind.AccessDenied, "odmowa (fake)")
                : null;
        var (session, buffer, log) = SessionWithRealRestorePoints(f, new FakeRestorePointApi(f.Registry), TestTweaks.TwoValues());

        var report = session.Run("test.two-values", GuiOperation.Apply, confirmed: false);
        var shown = buffer.Release(report, new[] { "test.two-values" });

        Assert.Equal(TweakOutcome.Applied, report.Result!.Outcome);
        var line = Assert.Single(shown);
        Assert.Contains("Nie udało się przywrócić SystemRestorePointCreationFrequency", line.Text, StringComparison.Ordinal);
    }

    /// <summary>Ochrona systemu wyłączona + nieudany tweak: wszystkie ostrzeżenia silnika są powtórzone w raporcie.</summary>
    [Fact]
    public void Apply_ProtectionDisabledAndFailedTweak_NothingExtraShown()
    {
        var f = new EngineFixture();
        new ToggleScript { ApplyFails = true }.Register(f.Scripts);
        var api = new FakeRestorePointApi(f.Registry) { ThrowOnCall = new COMException(string.Empty, unchecked((int)0x80070422)) };
        var (session, buffer, log) = SessionWithRealRestorePoints(f, api, TestTweaks.Scripted());

        var report = session.Run("test.script", GuiOperation.Apply, confirmed: false);
        var shown = buffer.Release(report, new[] { "test.script" });

        Assert.False(report.Result!.IsSuccess);
        Assert.Contains(report.Lines, line => line.Text.StartsWith("Punkt przywracania nie powstał: Ochrona systemu", StringComparison.Ordinal));
        Assert.Empty(shown);
        Assert.Equal(3, log.Warnings.Count);
    }

    /// <summary>U6: ostrzeżenie o braku punktu przy wyłączonej ochronie nie pokazuje nazwy wyliczenia i nie powtarza zdania.</summary>
    [Theory]
    [InlineData(false, "punkt przywracania")]
    [InlineData(true, "restore point wasn't created")]
    public void Apply_ProtectionDisabled_WarningHasNoEnumNameAndNoRepeatedSentence(bool english, string phrase)
    {
        using var scope = L.Use(english ? UiLanguage.English : UiLanguage.Polish);
        var f = new EngineFixture();
        var api = new FakeRestorePointApi(f.Registry) { ThrowOnCall = new COMException(string.Empty, unchecked((int)0x80070422)) };
        var (session, _, _) = SessionWithRealRestorePoints(f, api, TestTweaks.TwoValues());

        var report = session.Run("test.two-values", GuiOperation.Apply, confirmed: false);

        var text = Assert.Single(report.Lines, line => line.Level == GuiLogLevel.Warning).Text;
        Assert.DoesNotContain("ProtectionDisabled", text, StringComparison.Ordinal);
        var count = 0;
        for (var i = text.IndexOf(phrase, StringComparison.OrdinalIgnoreCase); i >= 0; i = text.IndexOf(phrase, i + 1, StringComparison.OrdinalIgnoreCase))
        {
            count++;
        }

        Assert.Equal(1, count);
    }

    private static (GuiSession Session, EngineWarningBuffer Buffer, BufferLog Log) SessionWithRealRestorePoints(
        EngineFixture f, FakeRestorePointApi api, params TweakDefinition[] definitions)
    {
        var buffer = new EngineWarningBuffer();
        var log = new BufferLog(buffer);
        var services = new EngineServices
        {
            Registry = f.Registry,
            Services = f.Services,
            Scripts = f.Scripts,
            Launcher = f.Launcher,
            Journal = f.Journal,
            RestorePoints = new RestorePointService(f.Registry, api, log, f.Clock, new FakeSleeper(f.Clock).Sleep),
            SystemInfo = f.SystemInfo,
            User = f.User,
            Log = log,
            Clock = f.Clock,
        };
        return (new GuiSession(new LoadedCatalog(definitions, "sha256:test"), services), buffer, log);
    }

    /// <summary>Jak UiLogSink w trakcie operacji: Warn silnika idzie do bufora.</summary>
    private sealed class BufferLog : ILog
    {
        private readonly EngineWarningBuffer _buffer;

        public BufferLog(EngineWarningBuffer buffer) => _buffer = buffer;

        public List<string> Warnings { get; } = new();

        public void Info(string component, string message)
        {
        }

        public void Warn(string component, string message)
        {
            Warnings.Add($"[{component}] {message}");
            Assert.True(_buffer.TryAdd(new GuiLogLine(At, GuiLogLevel.Warning, message)));
        }

        public void Fail(string component, string message, Exception? exception = null)
        {
        }
    }
}
