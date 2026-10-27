using System;
using DSOptimizer.Core.Catalog;
using DSOptimizer.Core.Model;
using Xunit;

namespace DSOptimizer.Tests.Unit.Catalog;

public class CatalogLoaderTests
{
    private const string FullTweak = """
        [
          {
            "id": "ux.menu-delay",
            "aliases": ["ux_menu"],
            "category": "explorer",
            "kind": "tweak",
            "title": "Szybsze podmenu",
            "summary": "Krótki opis.",
            "benefit": "Menu otwiera się od razu.",
            "description": "Opis",
            "risk": "safe",
            "scope": "user",
            "requiresReboot": true,
            "minBuild": 22000,
            "maxBuild": 26999,
            "timeoutSeconds": 30,
            "reversible": true,
            "warnings": ["Uwaga"],
            "conflicts": ["wsl2"],
            "registry": [
              { "path": "HKCU\\Control Panel\\Desktop", "name": "MenuShowDelay", "type": "String", "value": "10", "original": "400" },
              { "path": "HKCU\\Control Panel\\Desktop", "name": "Flag", "type": "DWord", "value": 1, "original": "<RemoveEntry>", "detectOnly": true }
            ],
            "services": [ { "name": "SysMain", "startType": "Disabled", "original": "Automatic", "stop": true } ],
            "script": { "apply": ["Write-Output 1", "Write-Output 2"], "undo": "Write-Output u", "detect": "Write-Output Applied", "capture": "Write-Output c" }
          }
        ]
        """;

    private const string Minimal = """
        [ { "id": "a.b", "category": "c", "kind": "action", "title": "t", "summary": "s", "risk": "caution", "scope": "machine", "script": { "apply": "x" } } ]
        """;

    private static LoadedCatalog LoadOne(string json, string name = "core.json")
        => CatalogLoader.Load(new[] { new CatalogSource(name, json) });

    [Fact]
    public void Load_Summary_ParsedIntoDefinition()
    {
        Assert.Equal("Krótki opis.", Assert.Single(LoadOne(FullTweak).Definitions).Summary);
        Assert.Equal(string.Empty, Assert.Single(LoadOne(Minimal.Replace("\"summary\": \"s\", ", string.Empty)).Definitions).Summary);
    }

    [Fact]
    public void Parse_Benefit_Read()
    {
        Assert.Equal("Menu otwiera się od razu.", Assert.Single(LoadOne(FullTweak).Definitions).Benefit);
        Assert.Null(Assert.Single(LoadOne(Minimal).Definitions).Benefit);
        Assert.Throws<CatalogLoadException>(() => LoadOne(Minimal.Replace("\"summary\": \"s\"", "\"summary\": \"s\", \"benefit\": 5")));
    }

    [Fact]
    public void Load_FullTweak_MapsAllFields()
    {
        var tweak = Assert.Single(LoadOne(FullTweak).Definitions);

        Assert.Equal("ux.menu-delay", tweak.Id);
        Assert.Equal(new[] { "ux_menu" }, tweak.Aliases);
        Assert.Equal("explorer", tweak.Category);
        Assert.Equal(TweakKind.Tweak, tweak.Kind);
        Assert.Equal("Szybsze podmenu", tweak.Title);
        Assert.Equal("Krótki opis.", tweak.Summary);
        Assert.Equal("Menu otwiera się od razu.", tweak.Benefit);
        Assert.Equal("Opis", tweak.Description);
        Assert.Equal(RiskLevel.Safe, tweak.Risk);
        Assert.Equal(TweakScope.User, tweak.Scope);
        Assert.True(tweak.RequiresReboot);
        Assert.Equal(22000, tweak.MinBuild);
        Assert.Equal(26999, tweak.MaxBuild);
        Assert.Equal(TimeSpan.FromSeconds(30), tweak.Timeout);
        Assert.True(tweak.Reversible);
        Assert.Equal(new[] { "Uwaga" }, tweak.Warnings);
        Assert.Equal(new[] { "wsl2" }, tweak.Conflicts);

        Assert.Equal(2, tweak.Registry.Count);
        Assert.Equal(@"HKCU\Control Panel\Desktop", tweak.Registry[0].Path);
        Assert.Equal("MenuShowDelay", tweak.Registry[0].Name);
        Assert.Equal(RegValue.FromString("10"), tweak.Registry[0].Value);
        Assert.Equal(RegValue.FromString("400"), tweak.Registry[0].Original);
        Assert.False(tweak.Registry[0].DetectOnly);
        Assert.Equal(RegValue.FromDWord(1), tweak.Registry[1].Value);
        Assert.Equal(RegValue.Absent, tweak.Registry[1].Original);
        Assert.True(tweak.Registry[1].DetectOnly);

        var service = Assert.Single(tweak.Services);
        Assert.Equal("SysMain", service.Name);
        Assert.Equal(ServiceStartType.Disabled, service.StartType);
        Assert.Equal(ServiceStartType.Automatic, service.Original);
        Assert.True(service.Stop);

        Assert.NotNull(tweak.Script);
        Assert.Equal("Write-Output 1\nWrite-Output 2", tweak.Script!.Apply);
        Assert.Equal("Write-Output u", tweak.Script.Undo);
        Assert.Equal("Write-Output Applied", tweak.Script.Detect);
        Assert.Equal("Write-Output c", tweak.Script.Capture);
    }

    [Fact]
    public void Load_MinimalTweak_UsesDefaults()
    {
        var tweak = Assert.Single(LoadOne(Minimal).Definitions);

        Assert.Empty(tweak.Aliases);
        Assert.Equal(string.Empty, tweak.Description);
        Assert.False(tweak.RequiresReboot);
        Assert.Null(tweak.MinBuild);
        Assert.Null(tweak.MaxBuild);
        Assert.Equal(TweakDefinition.DefaultTimeout, tweak.Timeout);
        Assert.True(tweak.Reversible);
        Assert.Empty(tweak.Registry);
        Assert.Empty(tweak.Services);
        Assert.Equal("x", tweak.Script!.Apply);
        Assert.Null(tweak.Script.Undo);
    }

    [Fact]
    public void Load_TimeoutNull_MeansNoLimit()
    {
        var json = Minimal.Replace("\"scope\": \"machine\"", "\"scope\": \"machine\", \"timeoutSeconds\": null");
        Assert.Null(Assert.Single(LoadOne(json).Definitions).Timeout);
    }

    [Fact]
    public void Load_UnknownField_ReportsFieldAndLine()
    {
        const string json = "[\n  {\n    \"id\": \"a.b\",\n    \"titel\": \"x\"\n  }\n]";
        var ex = Assert.Throws<CatalogLoadException>(() => LoadOne(json));
        Assert.Contains("titel", ex.Message, StringComparison.Ordinal);
        Assert.Contains("core.json (linia 4)", ex.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Load_InvalidJson_ReportsSourceAndLine()
    {
        var ex = Assert.Throws<CatalogLoadException>(() => LoadOne("[\n  { \"id\": }\n]"));
        Assert.Contains("core.json (linia 2", ex.Message, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("\"risk\": \"caution\"", "\"risk\": \"dangerous\"")]
    [InlineData("\"risk\": \"caution\"", "\"risk\": \"1\"")]
    [InlineData("\"title\": \"t\", ", "")]
    [InlineData("\"title\": \"t\"", "\"title\": 5")]
    [InlineData("\"script\": { \"apply\": \"x\" }", "\"script\": \"x\"")]
    [InlineData("\"script\": { \"apply\": \"x\" }", "\"script\": { \"apply\": 1 }")]
    public void Load_InvalidField_Throws(string original, string replacement)
        => Assert.Throws<CatalogLoadException>(() => LoadOne(Minimal.Replace(original, replacement)));

    private const string Panel = """
        [ { "id": "panels.x", "category": "panels", "kind": "panel", "title": "t", "summary": "s", "risk": "safe", "scope": "machine", "launch": { "file": "control.exe", "arguments": "/name Microsoft.PowerOptions" } } ]
        """;

    [Fact]
    public void Load_Handler_IsParsed()
    {
        var json = Minimal.Replace("\"script\": { \"apply\": \"x\" }", "\"handler\": \"wu-reset\"");

        Assert.Equal("wu-reset", Assert.Single(LoadOne(json).Definitions).Handler);
    }

    [Fact]
    public void Load_WithoutHandler_HasNullHandler()
        => Assert.Null(Assert.Single(LoadOne(Minimal).Definitions).Handler);

    [Fact]
    public void Load_HandlerNotString_Throws()
        => Assert.Throws<CatalogLoadException>(() => LoadOne(Minimal.Replace("\"scope\": \"machine\"", "\"scope\": \"machine\", \"handler\": 5")));

    [Fact]
    public void Load_MisspelledHandlerField_StillRejected()
        => Assert.Throws<CatalogLoadException>(() => LoadOne(Minimal.Replace("\"scope\": \"machine\"", "\"scope\": \"machine\", \"handlr\": \"x\"")));

    [Fact]
    public void Load_Launch_MapsFileAndArguments()
    {
        var panel = Assert.Single(LoadOne(Panel).Definitions);

        Assert.Equal(TweakKind.Panel, panel.Kind);
        Assert.Null(panel.Script);
        Assert.Equal("control.exe", panel.Launch!.File);
        Assert.Equal("/name Microsoft.PowerOptions", panel.Launch.Arguments);
    }

    [Fact]
    public void Load_LaunchWithoutArguments_HasEmptyArguments()
    {
        var json = Panel.Replace(", \"arguments\": \"/name Microsoft.PowerOptions\"", string.Empty);
        Assert.Equal(string.Empty, Assert.Single(LoadOne(json).Definitions).Launch!.Arguments);
    }

    [Fact]
    public void Load_WithoutLaunch_HasNullLaunch()
        => Assert.Null(Assert.Single(LoadOne(Minimal).Definitions).Launch);

    [Theory]
    [InlineData("\"arguments\": \"/name Microsoft.PowerOptions\"", "\"arguments\": [\"/name\", \"Microsoft.PowerOptions\"]")]
    [InlineData("\"arguments\": \"/name Microsoft.PowerOptions\"", "\"arguments\": 1")]
    [InlineData("\"arguments\": \"/name Microsoft.PowerOptions\"", "\"args\": \"x\"")]
    [InlineData("\"file\": \"control.exe\", ", "")]
    [InlineData("\"file\": \"control.exe\"", "\"file\": [\"control.exe\"]")]
    [InlineData("\"launch\": { \"file\": \"control.exe\", \"arguments\": \"/name Microsoft.PowerOptions\" }", "\"launch\": \"control.exe\"")]
    public void Load_InvalidLaunch_Throws(string original, string replacement)
    {
        var json = Panel.Replace(original, replacement);
        Assert.NotEqual(Panel, json);
        Assert.Throws<CatalogLoadException>(() => LoadOne(json));
    }

    [Fact]
    public void Load_InvalidRegistryValue_ReportsEntry()
    {
        var json = FullTweak.Replace("\"value\": 1,", "\"value\": \"abc\",");
        var ex = Assert.Throws<CatalogLoadException>(() => LoadOne(json));
        Assert.Contains("Flag", ex.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Load_ElementNotObject_Throws()
        => Assert.Throws<CatalogLoadException>(() => LoadOne("[1]"));

    [Fact]
    public void Load_RootNotArray_Throws()
        => Assert.Throws<CatalogLoadException>(() => LoadOne("{}"));

    [Fact]
    public void Load_Hash_IsStableOrderIndependentAndContentSensitive()
    {
        var a = new CatalogSource("a.json", Minimal);
        var b = new CatalogSource("b.json", Minimal.Replace("a.b", "a.c"));

        var forward = CatalogLoader.Load(new[] { a, b });
        var backward = CatalogLoader.Load(new[] { b, a });
        var changed = CatalogLoader.Load(new[] { a, new CatalogSource("b.json", Minimal.Replace("a.b", "a.d")) });

        Assert.StartsWith("sha256:", forward.Hash, StringComparison.Ordinal);
        Assert.Equal(71, forward.Hash.Length);
        Assert.Equal(forward.Hash, backward.Hash);
        Assert.NotEqual(forward.Hash, changed.Hash);
        Assert.Equal(new[] { "a.b", "a.c" }, new[] { backward.Definitions[0].Id, backward.Definitions[1].Id });
    }

    [Fact]
    public void Parse_SuggestedAndEditions_Read()
    {
        var json = Minimal.Replace("\"summary\": \"s\"", "\"summary\": \"s\", \"suggested\": true, \"editions\": [\"pro\", \"business\"]");
        var definition = Assert.Single(LoadOne(json).Definitions);
        Assert.True(definition.Suggested);
        Assert.Equal(new[] { EditionFamily.Pro, EditionFamily.Business }, definition.Editions);

        var plain = Assert.Single(LoadOne(Minimal).Definitions);
        Assert.False(plain.Suggested);
        Assert.Null(plain.Editions);
    }

    [Theory]
    [InlineData("home,business")]
    [InlineData(" pro")]
    [InlineData("pro ")]
    [InlineData("1")]
    [InlineData("")]
    public void Parse_EditionsLenientForms_Rejected(string value)
    {
        var ex = Assert.Throws<CatalogLoadException>(() => LoadOne(Minimal.Replace("\"summary\": \"s\"", "\"summary\": \"s\", \"editions\": [\"" + value + "\"]")));
        Assert.Contains("nieznana wartość", ex.Message, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("safe,caution", "risk")]
    [InlineData("machine,user", "scope")]
    [InlineData(" safe", "risk")]
    [InlineData("1", "risk")]
    public void Parse_EnumLenientForms_Rejected(string value, string field)
    {
        var original = field == "risk" ? "\"risk\": \"caution\"" : "\"scope\": \"machine\"";
        var json = Minimal.Replace(original, "\"" + field + "\": \"" + value + "\"");
        var ex = Assert.Throws<CatalogLoadException>(() => LoadOne(json));
        Assert.Contains("nieznana wartość", ex.Message, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("Home")]
    [InlineData("home")]
    public void Parse_EditionsExactName_AcceptedCaseInsensitive(string value)
    {
        var definition = Assert.Single(LoadOne(Minimal.Replace("\"summary\": \"s\"", "\"summary\": \"s\", \"editions\": [\"" + value + "\"]")).Definitions);
        Assert.Equal(new[] { EditionFamily.Home }, definition.Editions);
    }

    [Fact]
    public void Parse_EditionsInvalid_ThrowsWithFieldName()
    {
        var unknown = Assert.Throws<CatalogLoadException>(() => LoadOne(Minimal.Replace("\"summary\": \"s\"", "\"summary\": \"s\", \"editions\": [\"ultimate\"]")));
        Assert.Contains("editions", unknown.Message);
        var notArray = Assert.Throws<CatalogLoadException>(() => LoadOne(Minimal.Replace("\"summary\": \"s\"", "\"summary\": \"s\", \"editions\": \"pro\"")));
        Assert.Contains("editions", notArray.Message);
        var notBool = Assert.Throws<CatalogLoadException>(() => LoadOne(Minimal.Replace("\"summary\": \"s\"", "\"summary\": \"s\", \"suggested\": 1")));
        Assert.Contains("suggested", notBool.Message);
    }
}
