using DSOptimizer.Core.Model;

namespace DSOptimizer.Tests.Unit.Engine;

/// <summary>Tweaki-atrapy. Ścieżki są fikcyjne — działają tylko na FakeRegistry.</summary>
internal static class TestTweaks
{
    public const string Path = @"HKLM\SOFTWARE\DSO\Test";

    public static TweakDefinition TwoValues(string id = "test.two-values") => new()
    {
        Id = id,
        Category = "test",
        Kind = TweakKind.Tweak,
        Title = "Dwie wartości",
        Risk = RiskLevel.Safe,
        Scope = TweakScope.Machine,
        Registry = new[]
        {
            new RegistryEntry { Path = Path, Name = "A", Value = RegValue.FromDWord(1), Original = RegValue.FromDWord(0) },
            new RegistryEntry { Path = Path, Name = "B", Value = RegValue.FromDWord(1), Original = RegValue.Absent },
        },
    };

    public const string NewKeyPath = @"HKLM\SOFTWARE\DSO\Nowy\Klucz";

    public static TweakDefinition TwoValuesInNewKey() => new()
    {
        Id = "test.new-key",
        Category = "test",
        Kind = TweakKind.Tweak,
        Title = "Nowy klucz",
        Risk = RiskLevel.Safe,
        Scope = TweakScope.Machine,
        Registry = new[]
        {
            new RegistryEntry { Path = NewKeyPath, Name = "A", Value = RegValue.FromDWord(1), Original = RegValue.Absent },
            new RegistryEntry { Path = NewKeyPath, Name = "B", Value = RegValue.FromDWord(1), Original = RegValue.Absent },
        },
    };

    public static TweakDefinition UserMenuDelay() => new()
    {
        Id = "test.menu-delay",
        Category = "test",
        Kind = TweakKind.Tweak,
        Title = "Opóźnienie menu",
        Risk = RiskLevel.Safe,
        Scope = TweakScope.User,
        Registry = new[]
        {
            new RegistryEntry { Path = @"HKCU\Control Panel\Desktop", Name = "MenuShowDelay", Value = RegValue.FromString("10"), Original = RegValue.FromString("400") },
        },
    };

    public static TweakDefinition DisableService() => new()
    {
        Id = "test.service",
        Category = "test",
        Kind = TweakKind.Tweak,
        Title = "Usługa",
        Risk = RiskLevel.Caution,
        Scope = TweakScope.Machine,
        Services = new[]
        {
            new ServiceEntry { Name = "SysMain", StartType = ServiceStartType.Disabled, Original = ServiceStartType.Automatic, Stop = true },
        },
    };

    public static TweakDefinition Scripted() => new()
    {
        Id = "test.script",
        Category = "test",
        Kind = TweakKind.Tweak,
        Title = "Skrypt",
        Risk = RiskLevel.Safe,
        Scope = TweakScope.Machine,
        Script = new ScriptSet { Apply = "APPLY", Undo = "UNDO", Detect = "DETECT", Capture = "CAPTURE" },
    };

    public static TweakDefinition Action() => new()
    {
        Id = "test.action",
        Category = "test",
        Kind = TweakKind.Action,
        Title = "Akcja",
        Risk = RiskLevel.Safe,
        Scope = TweakScope.Machine,
        Script = new ScriptSet { Apply = "RUN" },
    };
    public static TweakDefinition Panel() => new()
    {
        Id = "test.panel",
        Category = "test",
        Kind = TweakKind.Panel,
        Title = "Panel",
        Risk = RiskLevel.Safe,
        Scope = TweakScope.Machine,
        Launch = new LaunchSpec { File = "compmgmt.msc", Arguments = "/s" },
    };
}
