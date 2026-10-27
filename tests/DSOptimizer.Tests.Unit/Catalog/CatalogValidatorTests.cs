using System;
using System.Collections.Generic;
using System.Linq;
using DSOptimizer.Core.Catalog;
using DSOptimizer.Core.Model;
using Xunit;

namespace DSOptimizer.Tests.Unit.Catalog;

public class CatalogValidatorTests
{
    private const string Hklm = @"HKLM\SOFTWARE\DSO\Test";

    private static TweakDefinition Valid(string id = "test.tweak") => new()
    {
        Id = id,
        Category = "test",
        Kind = TweakKind.Tweak,
        Title = "T",
        Summary = "Opis dla człowieka.",
        Benefit = "Co realnie zyskujesz.",
        Risk = RiskLevel.Safe,
        Scope = TweakScope.Machine,
        Registry = new[]
        {
            new RegistryEntry { Path = Hklm, Name = "A", Value = RegValue.FromDWord(1), Original = RegValue.FromDWord(0) },
        },
    };

    private static TweakDefinition ValidAction() => new()
    {
        Id = "test.action",
        Category = "test",
        Kind = TweakKind.Action,
        Title = "A",
        Summary = "Opis dla człowieka.",
        Benefit = "Co realnie zyskujesz.",
        Risk = RiskLevel.Safe,
        Scope = TweakScope.Machine,
        Script = new ScriptSet { Apply = "Write-Output ok" },
    };

    private static List<string> Rules(params TweakDefinition[] definitions)
        => CatalogValidator.Validate(definitions).Select(error => error.Rule).ToList();

    [Fact]
    public void ValidDefinitions_HaveNoErrors()
        => Assert.Empty(CatalogValidator.Validate(new[] { Valid(), ValidAction() }));

    [Theory]
    [InlineData("Test.Tweak")]
    [InlineData("tweak")]
    [InlineData("a..b")]
    [InlineData("a.b_c")]
    public void InvalidId_IsReported(string id)
        => Assert.Contains("id-format", Rules(Valid(id)));

    [Fact]
    public void DuplicateId_IsReported()
        => Assert.Contains("id-unique", Rules(Valid(), Valid()));

    [Fact]
    public void AliasCollidingWithId_IsReported()
        => Assert.Contains("alias-unique", Rules(Valid("a.one"), Valid("a.two") with { Aliases = new[] { "a.one" } }));

    [Fact]
    public void DuplicateAlias_IsReported()
        => Assert.Contains("alias-unique", Rules(Valid("a.one") with { Aliases = new[] { "old_id" } }, Valid("a.two") with { Aliases = new[] { "old_id" } }));

    [Fact]
    public void BlankTitleOrCategory_IsReported()
    {
        Assert.Contains("required-field", Rules(Valid() with { Title = " " }));
        Assert.Contains("required-field", Rules(Valid() with { Category = string.Empty }));
    }

    [Fact]
    public void Summary_Missing_ReportsSummaryRule()
    {
        Assert.Contains("summary", Rules(Valid() with { Summary = string.Empty }));
        Assert.Contains("summary", Rules(Valid() with { Summary = " " }));
    }

    [Fact]
    public void Summary_LongerThanLimit_ReportsSummaryRule()
    {
        var error = Assert.Single(CatalogValidator.Validate(new[] { Valid() with { Summary = new string('a', 161) } }));

        Assert.Equal("summary", error.Rule);
        Assert.Contains("161", error.Message, StringComparison.Ordinal);
        Assert.Contains("160", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Summary_ExactlyLimit_NoError()
        => Assert.Empty(CatalogValidator.Validate(new[] { Valid() with { Summary = new string('a', CatalogValidator.SummaryMaxLength) } }));

    [Fact]
    public void Validate_TweakWithoutBenefit_Error()
    {
        Assert.Contains("benefit", Rules(Valid() with { Benefit = null }));
        Assert.Contains("benefit", Rules(Valid() with { Benefit = string.Empty }));
    }

    [Fact]
    public void Validate_ActionWhitespaceBenefit_Error()
        => Assert.Contains("benefit", Rules(ValidAction() with { Benefit = "   " }));

    [Fact]
    public void Validate_Benefit221_Error()
    {
        var error = Assert.Single(CatalogValidator.Validate(new[] { Valid() with { Benefit = new string('a', CatalogValidator.BenefitMaxLength + 1) } }));
        Assert.Equal("benefit", error.Rule);
        Assert.Contains("221", error.Message, StringComparison.Ordinal);
        Assert.Contains("220", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Validate_Benefit220_Ok()
    {
        Assert.Equal(220, CatalogValidator.BenefitMaxLength);
        Assert.Empty(CatalogValidator.Validate(new[] { ValidAction() with { Benefit = new string('a', CatalogValidator.BenefitMaxLength) } }));
    }

    [Fact]
    public void Validate_PanelWithBenefit_Error()
    {
        Assert.Contains("benefit", Rules(ValidPanel() with { Benefit = "Zysk." }));
        Assert.Contains("benefit", Rules(ValidPanel() with { Benefit = " " }));
        Assert.Empty(Rules(ValidPanel()));
    }

    [Fact]
    public void Summary_AppliesToActionsAndPanels()
    {
        Assert.Contains("summary", Rules(ValidAction() with { Summary = string.Empty }));
        Assert.Contains("summary", Rules(ValidPanel() with { Summary = string.Empty }));
    }

    [Fact]
    public void TweakWithoutDetection_IsReported()
        => Assert.Contains("tweak-detection", Rules(Valid() with
        {
            Registry = Array.Empty<RegistryEntry>(),
            Script = new ScriptSet { Apply = "a", Undo = "u" },
        }));

    [Fact]
    public void ReversibleTweak_RegistryWithoutOriginal_IsReported()
        => Assert.Contains("tweak-reversible", Rules(Valid() with
        {
            Registry = new[] { new RegistryEntry { Path = Hklm, Name = "A", Value = RegValue.FromDWord(1) } },
        }));

    [Fact]
    public void ReversibleTweak_DetectOnlyWithoutOriginal_IsAccepted()
        => Assert.DoesNotContain("tweak-reversible", Rules(Valid() with
        {
            Registry = new[]
            {
                new RegistryEntry { Path = Hklm, Name = "A", Value = RegValue.FromDWord(1), Original = RegValue.FromDWord(0) },
                new RegistryEntry { Path = Hklm, Name = "B", Value = RegValue.FromDWord(1), DetectOnly = true },
            },
        }));

    [Fact]
    public void ReversibleTweak_ServiceWithoutOriginal_IsReported()
        => Assert.Contains("tweak-reversible", Rules(Valid() with
        {
            Services = new[] { new ServiceEntry { Name = "X", StartType = ServiceStartType.Disabled } },
        }));

    [Fact]
    public void ReversibleTweak_ScriptApplyWithoutUndo_IsReported()
        => Assert.Contains("tweak-reversible", Rules(Valid() with { Script = new ScriptSet { Apply = "a", Detect = "d" } }));

    [Fact]
    public void IrreversibleTweak_NeedsWarningButNoOriginals()
    {
        var withoutOriginal = new[] { new RegistryEntry { Path = Hklm, Name = "A", Value = RegValue.FromDWord(1) } };
        Assert.Contains("tweak-reversible", Rules(Valid() with { Reversible = false, Registry = withoutOriginal }));
        Assert.Empty(Rules(Valid() with { Reversible = false, Registry = withoutOriginal, Warnings = new[] { "Nie da się cofnąć." } }));
    }

    [Fact]
    public void ActionWithoutApply_IsReported()
        => Assert.Contains("action-shape", Rules(ValidAction() with { Script = null }));

    [Fact]
    public void ActionWithRegistry_IsReported()
        => Assert.Contains("action-shape", Rules(ValidAction() with { Registry = Valid().Registry }));

    [Fact]
    public void RegistryPathWithUnknownRoot_IsReported()
        => Assert.Contains("registry-path", Rules(Valid() with
        {
            Registry = new[] { new RegistryEntry { Path = @"HKEY_LOCAL_MACHINE\X", Name = "A", Value = RegValue.FromDWord(1), Original = RegValue.Absent } },
        }));

    [Fact]
    public void UserScope_WithHklmPath_IsReported()
        => Assert.Contains("scope", Rules(Valid() with { Scope = TweakScope.User }));

    [Fact]
    public void UserScope_WithService_IsReported()
        => Assert.Contains("scope", Rules(Valid() with
        {
            Scope = TweakScope.User,
            Registry = new[] { new RegistryEntry { Path = @"HKCU\X", Name = "A", Value = RegValue.FromDWord(1), Original = RegValue.Absent } },
            Services = new[] { new ServiceEntry { Name = "X", StartType = ServiceStartType.Manual, Original = ServiceStartType.Automatic } },
        }));

    [Fact]
    public void MachineScope_WithHkcuPath_IsReported()
        => Assert.Contains("scope", Rules(Valid() with
        {
            Registry = new[] { new RegistryEntry { Path = @"HKCU\X", Name = "A", Value = RegValue.FromDWord(1), Original = RegValue.Absent } },
        }));

    [Fact]
    public void ScriptUsingHkcuDrive_IsReported()
        => Assert.Contains("script-hkcu", Rules(Valid() with { Script = new ScriptSet { Detect = @"Get-ItemProperty hkcu:\X" } }));

    [Fact]
    public void BuildRangeInverted_IsReported()
        => Assert.Contains("build-range", Rules(Valid() with { MinBuild = 26000, MaxBuild = 22000 }));

    [Fact]
    public void NonPositiveTimeout_IsReported()
        => Assert.Contains("timeout", Rules(Valid() with { Timeout = TimeSpan.Zero }));

    private static TweakDefinition ValidHandlerAction(string handler = "wu-reset") => ValidAction() with { Script = null, Handler = handler };

    [Fact]
    public void ActionWithHandlerOnly_HasNoErrors()
        => Assert.Empty(CatalogValidator.Validate(new[] { ValidHandlerAction() }));

    [Fact]
    public void ActionWithHandlerAndApply_IsReported()
        => Assert.Contains("action-shape", Rules(ValidAction() with { Handler = "wu-reset" }));

    [Fact]
    public void ActionWithHandlerAndLaunch_IsReported()
        => Assert.Contains("action-shape", Rules(ValidHandlerAction() with { Launch = new LaunchSpec { File = "control.exe" } }));

    [Fact]
    public void TweakWithHandler_IsReported()
        => Assert.Contains("handler-kind", Rules(Valid() with { Handler = "wu-reset" }));

    [Fact]
    public void PanelWithHandler_IsReported()
        => Assert.Contains("handler-kind", Rules(ValidPanel() with { Launch = null, Handler = "wu-reset" }));

    [Fact]
    public void ActionWithUnknownHandler_IsReportedWithName()
    {
        var error = Assert.Single(CatalogValidator.Validate(new[] { ValidHandlerAction("nieznany") }));
        Assert.Equal("handler-unknown", error.Rule);
        Assert.Contains("nieznany", error.Message, StringComparison.Ordinal);
    }

    private static TweakDefinition ValidPanel(string file = "compmgmt.msc") => new()
    {
        Id = "test.panel",
        Category = "test",
        Kind = TweakKind.Panel,
        Title = "P",
        Summary = "Opis dla człowieka.",
        Risk = RiskLevel.Safe,
        Scope = TweakScope.Machine,
        Launch = new LaunchSpec { File = file },
    };

    [Fact]
    public void PanelAndActionWithLaunch_HaveNoErrors()
        => Assert.Empty(CatalogValidator.Validate(new[]
        {
            ValidPanel(),
            ValidPanel("shell:::{A8A91A66-3A7D-4424-8D24-04E180695C7A}") with { Id = "test.printers" },
            ValidAction() with { Script = null, Launch = new LaunchSpec { File = "powershell.exe", Arguments = "-NoProfile -Command \"Write-Output ok\"" } },
        }));

    [Fact]
    public void TweakWithLaunch_IsReported()
        => Assert.Contains("launch-shape", Rules(Valid() with { Launch = new LaunchSpec { File = "control.exe" } }));

    [Fact]
    public void ActionWithApplyAndLaunch_IsReported()
        => Assert.Contains("action-shape", Rules(ValidAction() with { Launch = new LaunchSpec { File = "control.exe" } }));

    [Fact]
    public void PanelWithoutApplyAndLaunch_IsReported()
        => Assert.Contains("action-shape", Rules(ValidPanel() with { Launch = null }));

    [Fact]
    public void PanelWithLaunchAndEmptyScriptSet_IsAccepted()
        => Assert.Empty(Rules(ValidPanel() with { Script = new ScriptSet() }));

    [Theory]
    [MemberData(nameof(Model.LaunchTargetTests.Forbidden), MemberType = typeof(Model.LaunchTargetTests))]
    public void LaunchFileOutsideAllowList_IsReported(string file)
    {
        var error = Assert.Single(CatalogValidator.Validate(new[] { ValidPanel(file) }));
        Assert.Equal("launch-file", error.Rule);
    }

    [Theory]
    [MemberData(nameof(Model.LaunchTargetTests.Allowed), MemberType = typeof(Model.LaunchTargetTests))]
    public void LaunchFileOnAllowList_IsAccepted(string file)
        => Assert.Empty(Rules(ValidPanel(file)));

    [Fact]
    public void Error_CarriesTweakIdAndReadableText()
    {
        var error = Assert.Single(CatalogValidator.Validate(new[] { Valid() with { MinBuild = 2, MaxBuild = 1 } }));
        Assert.Equal("test.tweak", error.TweakId);
        Assert.StartsWith("test.tweak [build-range]: ", error.ToString(), StringComparison.Ordinal);
    }

    [Fact]
    public void Suggested_OnTweak_IsValid()
        => Assert.Empty(CatalogValidator.Validate(new[] { Valid() with { Suggested = true } }));

    [Theory]
    [InlineData(TweakKind.Action)]
    [InlineData(TweakKind.Panel)]
    public void Suggested_OnActionOrPanel_IsError(TweakKind kind)
    {
        var definition = kind == TweakKind.Action
            ? ValidAction() with { Suggested = true }
            : new TweakDefinition
            {
                Id = "test.panel",
                Category = "test",
                Kind = TweakKind.Panel,
                Title = "P",
                Summary = "Opis dla człowieka.",
                Risk = RiskLevel.Safe,
                Scope = TweakScope.Machine,
                Launch = new LaunchSpec { File = "compmgmt.msc" },
                Suggested = true,
            };

        var error = Assert.Single(CatalogValidator.Validate(new[] { definition }));
        Assert.Equal("suggested", error.Rule);
        Assert.Equal(definition.Id, error.TweakId);
        Assert.Contains("suggested", error.Message);
    }

    [Fact]
    public void Editions_Empty_IsError()
        => Assert.Equal(new[] { "editions" }, Rules(Valid() with { Editions = Array.Empty<EditionFamily>() }));

    [Fact]
    public void Editions_Duplicate_IsError()
        => Assert.Equal(new[] { "editions" }, Rules(Valid() with { Editions = new[] { EditionFamily.Pro, EditionFamily.Pro } }));

    [Fact]
    public void Editions_Valid_IsOk()
        => Assert.Empty(CatalogValidator.Validate(new[] { Valid() with { Editions = new[] { EditionFamily.Pro, EditionFamily.Business } } }));
}
