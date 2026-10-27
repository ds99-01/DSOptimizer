using System;
using System.Threading;
using DSOptimizer.Core.Abstractions;
using DSOptimizer.Core.Engine;
using DSOptimizer.Core.Model;
using DSOptimizer.Core.Repair;
using DSOptimizer.Tests.Unit.Fakes;
using Xunit;

namespace DSOptimizer.Tests.Unit.Repair;

/// <summary>Handler restore-point wyłącznie z fałszywym IRestorePointService: żaden punkt nie powstaje na hoście.</summary>
public class RestorePointHandlerTests
{
    private static readonly TimeZoneInfo Warsaw = TimeZoneInfo.CreateCustomTimeZone("dso-test", TimeSpan.FromHours(2), "dso-test", "dso-test");

    private static (RestorePointHandler Handler, FakeRestorePointService Service) Create(RestorePointResult next)
    {
        var service = new FakeRestorePointService { Next = next };
        var clock = new FakeClock { UtcNow = new DateTimeOffset(2026, 10, 22, 12, 30, 5, TimeSpan.Zero) };
        return (new RestorePointHandler(service, clock, Warsaw), service);
    }

    private static ActionHandlerResult Run(RestorePointHandler handler)
        => handler.Run(new TweakDefinition { Id = "repair.restore-point", Category = "repair", Kind = TweakKind.Action, Title = "t", Risk = RiskLevel.Safe, Scope = TweakScope.Machine, Summary = "s" }, null, null, CancellationToken.None);

    [Fact]
    public void Name_IsRestorePoint() => Assert.Equal(ActionHandlerNames.RestorePoint, Create(new RestorePointResult(RestorePointStatus.Created, null)).Handler.Name);

    [Fact]
    public void Created_IsExecuted_WithDescriptionAndLocalTime()
    {
        var (handler, service) = Create(new RestorePointResult(RestorePointStatus.Created, null, 0));

        var result = Run(handler);

        Assert.Equal(TweakOutcome.Executed, result.Outcome);
        Assert.Equal("Utworzono punkt przywracania „DSOptimizer – punkt ręczny” (2026-10-22 14:30:05).", result.Message);
        Assert.Equal(new[] { "DSOptimizer – punkt ręczny" }, service.Descriptions);
    }

    [Fact]
    public void ProtectionDisabled_IsFailed_PointingToSystemProtectionItem()
    {
        var (handler, _) = Create(new RestorePointResult(RestorePointStatus.ProtectionDisabled, "Ochrona systemu jest wyłączona", 1058));

        var result = Run(handler);

        Assert.Equal(TweakOutcome.Failed, result.Outcome);
        Assert.Equal("Ochrona systemu na dysku C: jest wyłączona – włącz ją pozycją „Ochrona systemu” w tej kategorii.", result.Message);
    }

    [Fact]
    public void Failed_IsFailed_WithServiceMessageAndCode()
    {
        var (handler, _) = Create(new RestorePointResult(RestorePointStatus.Failed, "Punkt nie pojawił się na liście.", 0));

        var result = Run(handler);

        Assert.Equal(TweakOutcome.Failed, result.Outcome);
        Assert.Equal("Punkt nie pojawił się na liście. (kod 0)", result.Message);
    }

    [Fact]
    public void Failed_WithoutCode_HasOnlyMessage()
    {
        var (handler, _) = Create(new RestorePointResult(RestorePointStatus.Failed, "Nie można odczytać listy.", null));

        Assert.Equal("Nie można odczytać listy.", Run(handler).Message);
    }

    [Fact]
    public void Failed_WithoutMessage_StillSaysSomething()
    {
        var (handler, _) = Create(new RestorePointResult(RestorePointStatus.Failed, null, 5));

        var result = Run(handler);

        Assert.Equal(TweakOutcome.Failed, result.Outcome);
        Assert.Equal("Nie udało się utworzyć punktu przywracania. (kod 5)", result.Message);
    }

    [Fact]
    public void ServiceThrows_IsNotSwallowedAsSuccess()
    {
        var service = new FakeRestorePointService { ThrowOnCreate = new InvalidOperationException("boom") };
        var handler = new RestorePointHandler(service, new FakeClock(), Warsaw);

        Assert.Throws<InvalidOperationException>(() => Run(handler));
    }
}
