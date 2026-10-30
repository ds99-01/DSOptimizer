using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using DSOptimizer.Catalog;
using DSOptimizer.Core.Catalog;
using DSOptimizer.Core.Engine;
using DSOptimizer.Core.Localization;
using DSOptimizer.Core.Model;
using DSOptimizer.Tests.Unit.Engine;
using DSOptimizer.Tests.Unit.Support;
using Xunit;

namespace DSOptimizer.Tests.Unit.Catalog;

/// <summary>
/// sec.take-ownership-menu (partia C4, Task 5): menu kontekstowe w całości w registry[]. Wartości z VM 26H2:
/// docs/zrodla/2026-10-18-partia-c4-zrodla.md (przebieg 2 i „Test ręczny UAC”), linie komend z tests/vm/explore/Invoke-C4Exploration.ps1.
/// </summary>
public class SecTakeOwnershipTests
{
    private const string Id = "sec.take-ownership-menu";
    private const string ClassesRoot = @"HKLM\SOFTWARE\Classes";

    // Przetestowane na VM (wariant B AQS: wyklucza C:\Windows, wszystko pod nim i korzeń C:\).
    private const string AppliesToVariantB = "NOT (System.ItemPathDisplay:=\"C:\\Windows\" OR System.ItemPathDisplay:~<\"C:\\Windows\\\" OR System.ItemPathDisplay:=\"C:\\\")";

    private const string FileCommand = "cmd.exe /c takeown /f \"%1\" /a && icacls \"%1\" /grant *S-1-5-32-544:F /c /l /q";
    private const string FolderCommand = "cmd.exe /c takeown /f \"%1\" /a /r /d Y && icacls \"%1\" /grant *S-1-5-32-544:F /t /c /l /q";

    private static TweakDefinition Get()
        => CatalogLoader.Load(BuiltInCatalog.Sources).Definitions.Single(d => d.Id == Id);

    private static string Doc()
        => File.ReadAllText(Path.Combine(RepoRoot.Combine("docs", "tweaks"), Id + ".md"), Encoding.UTF8);

    private static string Root(string cls) => $@"{ClassesRoot}\{cls}\shell\DSOptimizer.TakeOwnership";

    private static string Sub(string cls) => Root(cls) + @"\ExtendedSubCommandsKey\Shell\runas";

    private static (string Path, string Name, RegValue Value)[] Expected(string cls)
    {
        var list = new List<(string, string, RegValue)>
        {
            (Root(cls), "MUIVerb", RegValue.FromString("Przejmij na własność")),
            (Root(cls), "Extended", RegValue.FromString(string.Empty)),
        };
        if (cls == "Directory")
        {
            list.Add((Root(cls), "AppliesTo", RegValue.FromString(AppliesToVariantB)));
        }

        list.Add((Root(cls), "ExtendedSubCommandsKey", RegValue.FromString($@"{cls}\shell\DSOptimizer.TakeOwnership\ExtendedSubCommandsKey")));
        list.Add((Sub(cls), "MUIVerb", RegValue.FromString("Przejmij na własność (administrator)")));
        list.Add((Sub(cls), "HasLUAShield", RegValue.FromString(string.Empty)));
        list.Add((Sub(cls) + @"\command", string.Empty, RegValue.FromString(cls == "*" ? FileCommand : FolderCommand)));
        return list.ToArray();
    }

    // ---- kształt pozycji ----

    [Fact]
    public void Entry_IsInSecCategory_WithBothLegacyAliases()
    {
        var owners = CatalogLoader.Load(BuiltInCatalog.Sources).Definitions.Where(d => d.Aliases.Contains("sec_take_ownership")).Select(d => d.Id).ToList();
        Assert.Equal(new[] { Id }, owners);
        owners = CatalogLoader.Load(BuiltInCatalog.Sources).Definitions.Where(d => d.Aliases.Contains("sec_take_ownership_remove")).Select(d => d.Id).ToList();
        Assert.Equal(new[] { Id }, owners);
        Assert.Equal("sec", Get().Category);
    }

    [Fact]
    public void Entry_IsCautionMachineTweak_WithoutScriptServicesOrReboot()
    {
        var def = Get();
        Assert.Equal(TweakKind.Tweak, def.Kind);
        Assert.Equal(RiskLevel.Caution, def.Risk);
        Assert.Equal(TweakScope.Machine, def.Scope);
        Assert.False(def.RequiresReboot);
        Assert.Empty(def.Services);
        Assert.Null(def.Script);
    }

    [Fact]
    public void Registry_HasExactlyTheTestedEntriesForBothClasses_AllWithRemoveEntryOriginal()
    {
        var def = Get();
        var expected = Expected("*").Concat(Expected("Directory")).ToList();

        Assert.Equal(expected.Count, def.Registry.Count);
        foreach (var (path, name, value) in expected)
        {
            var entry = Assert.Single(def.Registry, e => e.Path == path && e.Name == name);
            Assert.Equal(value, entry.Value);
            Assert.Equal(RegValue.Absent, entry.Original);
            Assert.False(entry.DetectOnly);
        }
    }

    [Fact]
    public void Registry_OnlyMenuLabelsAreLocalized()
    {
        // Nazwy menu (MUIVerb) idą w języku programu z chwili zastosowania; ścieżki, komendy i AppliesTo zostają dosłowne.
        var localized = Get().Registry.Where(e => e.Localized).Select(e => (e.Path, e.Name)).ToList();
        var expected = new[] { (Root("*"), "MUIVerb"), (Sub("*"), "MUIVerb"), (Root("Directory"), "MUIVerb"), (Sub("Directory"), "MUIVerb") };
        Assert.Equal(expected.OrderBy(e => e.Item1, StringComparer.Ordinal), localized.OrderBy(e => e.Path, StringComparer.Ordinal));
    }

    [Fact]
    public void MenuLabels_HaveEnglishTranslations()
    {
        using (L.Use(UiLanguage.English))
        {
            Assert.Equal("Take ownership", L.Data("Przejmij na własność"));
            Assert.Equal("Take ownership (administrator)", L.Data("Przejmij na własność (administrator)"));
        }
    }

    [Fact]
    public void Registry_TouchesOnlyOurKeysUnderStarAndDirectory_NoTopLevelRunasAndNoHkcr()
    {
        var def = Get();
        var allowedRoots = new[] { Root("*"), Root("Directory") };

        Assert.All(def.Registry, entry => Assert.Contains(allowedRoots, root => entry.Path.Equals(root, StringComparison.Ordinal)
                                                                            || entry.Path.StartsWith(root + @"\", StringComparison.Ordinal)));
        Assert.DoesNotContain(def.Registry, entry => entry.Path.StartsWith(@"HKCR\", StringComparison.OrdinalIgnoreCase));
        // `runas` istnieje wyłącznie w podmenu naszego klucza, nigdy jako bezpośrednie dziecko `shell`.
        foreach (var cls in new[] { "*", "Directory" })
        {
            var topLevelRunas = $@"{ClassesRoot}\{cls}\shell\runas";
            Assert.DoesNotContain(def.Registry, entry => entry.Path.Equals(topLevelRunas, StringComparison.OrdinalIgnoreCase)
                                                         || entry.Path.StartsWith(topLevelRunas + @"\", StringComparison.OrdinalIgnoreCase));
        }

        Assert.Equal(2, def.Registry.Select(e => e.Path.Split('\\')[3]).Distinct().Count()); // `*` i `Directory`
    }

    [Fact]
    public void Commands_UseSidInsteadOfGroupName_AndQuoteTheTarget()
    {
        var commands = Get().Registry.Where(e => e.Name.Length == 0).ToList();
        Assert.Equal(2, commands.Count);
        foreach (var command in commands)
        {
            var text = (string)command.Value.Data!;
            Assert.Contains("*S-1-5-32-544:F", text, StringComparison.Ordinal);
            Assert.DoesNotContain("Administrators", text, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("Administratorzy", text, StringComparison.OrdinalIgnoreCase);
            Assert.Contains("\"%1\"", text, StringComparison.Ordinal);
            Assert.StartsWith("cmd.exe /c takeown ", text, StringComparison.Ordinal);
        }
    }

    [Fact]
    public void Commands_FilesAreNotRecursive_FoldersRecurseWithDy()
    {
        var file = (string)Get().Registry.Single(e => e.Name.Length == 0 && e.Path.Contains(@"\*\shell\", StringComparison.Ordinal)).Value.Data!;
        var folder = (string)Get().Registry.Single(e => e.Name.Length == 0 && e.Path.Contains(@"\Directory\shell\", StringComparison.Ordinal)).Value.Data!;

        Assert.DoesNotContain("/r", file, StringComparison.Ordinal);
        Assert.DoesNotContain("/t", file, StringComparison.Ordinal);
        Assert.Contains("/a /r /d Y", folder, StringComparison.Ordinal);
        Assert.Contains("/t /c /l /q", folder, StringComparison.Ordinal);
    }

    [Fact]
    public void AppliesTo_IsOnlyOnDirectory_NotOnFiles()
    {
        var appliesTo = Get().Registry.Where(e => e.Name == "AppliesTo").ToList();
        var entry = Assert.Single(appliesTo);
        Assert.Equal(Root("Directory"), entry.Path);
        Assert.Equal(RegValue.FromString(AppliesToVariantB), entry.Value);
    }

    [Fact]
    public void Extended_IsEmptyRegSz_OnBothRoots()
    {
        foreach (var cls in new[] { "*", "Directory" })
        {
            var entry = Get().Registry.Single(e => e.Path == Root(cls) && e.Name == "Extended");
            Assert.Equal(RegistryValueType.String, entry.Value.Type);
            Assert.Equal(string.Empty, entry.Value.Data);
        }
    }

    // ---- ostrzeżenia i opis ----

    [Fact]
    public void Warnings_CoverSystemFilesAclReplacementPercentShiftAndUndoLimits()
    {
        // Spec opisów 2026-10-26: w uwagach (widocznych od razu) pliki systemowe/WindowsApps i brak cofania uprawnień,
        // reszta ograniczeń w szczegółach technicznych, a wynik z VM tylko w dokumencie pozycji.
        var def = Get();
        var warnings = string.Join(" ", def.Warnings);
        Assert.Contains("WindowsApps", warnings, StringComparison.Ordinal);
        Assert.Contains("Shift", def.Summary, StringComparison.Ordinal); // wpis ma Extended: bez Shift go nie widać
        Assert.Contains("nie przywraca", warnings, StringComparison.Ordinal);

        var text = def.Description + " " + warnings;
        Assert.Contains("Windows Resource Protection", text, StringComparison.Ordinal);
        Assert.Contains("nie cofa", text, StringComparison.OrdinalIgnoreCase); // undo usuwa tylko menu
        Assert.Contains("Shift", text, StringComparison.Ordinal);
        Assert.Contains("zastępuje całą listę ACL", text, StringComparison.Ordinal); // R11
        Assert.Contains("WindowsApps", text, StringComparison.Ordinal);
        Assert.Contains("%", text, StringComparison.Ordinal); // R6
        Assert.Contains(@"C:\Windows", text, StringComparison.Ordinal);
        Assert.Contains("nie są wykluczone", text, StringComparison.Ordinal); // pliki pod C:\Windows
        Assert.Contains("inną literę", text, StringComparison.OrdinalIgnoreCase); // Windows na innej literze
        Assert.Contains("długo", text, StringComparison.OrdinalIgnoreCase); // rekurencja
        Assert.Contains("UAC", text, StringComparison.Ordinal);
        Assert.Contains("SYSTEM i Users utracili dostęp", Doc(), StringComparison.Ordinal);
        Assert.Contains("prawdopodobna przyczyna", text, StringComparison.Ordinal); // mechanizm % to wniosek, nie odczyt
    }

    [Fact]
    public void Description_NamesTheMenuAndDoesNotPromiseUndoOfPermissions()
    {
        var def = Get();
        Assert.Contains("Przejmij na własność", def.Description, StringComparison.Ordinal);
        Assert.Contains("Pokaż więcej opcji", def.Description, StringComparison.Ordinal);
        Assert.Contains("nie cofa", def.Description, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("God Mode", def.Description, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Doc_HasSourcesVmResultsManualTestsAndWarnings()
    {
        var doc = Doc();

        Assert.Contains("creating shortcut menu handlers", doc, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("context-menu-handlers", doc, StringComparison.Ordinal);
        // niespójność MS i rozstrzygnięcie VM
        Assert.Contains("specifying the same", doc, StringComparison.Ordinal);
        Assert.Contains("add an `ExtendedSubCommandsKey` subkey", doc, StringComparison.Ordinal);
        Assert.Contains("Z tym plikiem nie jest skojarzona aplikacja", doc, StringComparison.Ordinal);
        Assert.Contains("bez strzałki", doc, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("S-1-5-32-544", doc, StringComparison.Ordinal);
        // wyniki sond i testu ręcznego z datami
        Assert.Contains("2026-10-18", doc, StringComparison.Ordinal);
        Assert.Contains("2026-10-19", doc, StringComparison.Ordinal);
        foreach (var probe in new[] { "(3)", "(4a)", "(4b)", "(5)", "(6)" })
        {
            Assert.Contains(probe, doc, StringComparison.Ordinal);
        }

        // ostrzeżenia
        Assert.Contains("PAI(A;OI;FA;;;BA)(A;CI;FA;;;BA)", doc, StringComparison.Ordinal);
        Assert.Contains("WindowsApps", doc, StringComparison.Ordinal);
        Assert.Contains("nie cofa", doc, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("Verbs()", doc, StringComparison.Ordinal); // dlaczego Focus 4 nie przez Verbs()
        Assert.DoesNotContain("Task 6", doc, StringComparison.Ordinal);
        Assert.Contains("vm-run-20261019-002807", doc, StringComparison.Ordinal);
        // zakres testu ręcznego: bez AppliesTo i z etykietami „DSO: …”; docelowa kombinacja czeka na klik (R14, decyzja D2)
        Assert.Contains("bez `AppliesTo`", doc, StringComparison.Ordinal);
        Assert.Contains("R14", doc, StringComparison.Ordinal);
        Assert.Contains("PAI(A;OI;FA;;;BA)(A;CI;FA;;;BA)", doc, StringComparison.Ordinal);
        Assert.Contains("sec_take_ownership_remove", doc, StringComparison.Ordinal);
    }

    // ---- silnik na fake'ach (Review Focus 3) ----

    private static EngineFixture NewFixture()
    {
        var f = new EngineFixture();
        f.Registry.CreateKey($@"{ClassesRoot}\*\shell");
        f.Registry.CreateKey($@"{ClassesRoot}\Directory\shell");
        return f;
    }

    private static IEnumerable<string> AllOurKeys()
        => new[] { "*", "Directory" }.SelectMany(cls => new[]
        {
            Root(cls),
            Root(cls) + @"\ExtendedSubCommandsKey",
            Root(cls) + @"\ExtendedSubCommandsKey\Shell",
            Sub(cls),
            Sub(cls) + @"\command",
        });

    [Fact]
    public void ApplyThenDetect_IsApplied_AndAllNestedKeysExist()
    {
        var f = NewFixture();
        var engine = f.CreateEngine();
        var def = Get();

        Assert.Equal(DetectionState.NotApplied, engine.Detect(def));
        Assert.Equal(TweakOutcome.Applied, engine.Apply(def).Outcome);
        Assert.Equal(DetectionState.Applied, engine.Detect(def));
        Assert.All(AllOurKeys(), key => Assert.True(f.Registry.HasKey(key), key));
    }

    [Theory]
    [InlineData(UiLanguage.Polish, UiLanguage.English, "Przejmij na własność", "Przejmij na własność (administrator)")]
    [InlineData(UiLanguage.English, UiLanguage.Polish, "Take ownership", "Take ownership (administrator)")]
    public void LanguageSwitchAfterApply_StillDetectsApplied_AndUndoRemovesTheEntries(UiLanguage applyLanguage, UiLanguage laterLanguage, string label, string subLabel)
    {
        var f = NewFixture();
        var engine = f.CreateEngine();
        var def = Get();

        using (L.Use(applyLanguage))
        {
            Assert.Equal(TweakOutcome.Applied, engine.Apply(def).Outcome);
        }

        // Etykiety w rejestrze są w języku z chwili zastosowania.
        Assert.Equal(RegValue.FromString(label), f.Registry.Get(Root("*"), "MUIVerb"));
        Assert.Equal(RegValue.FromString(subLabel), f.Registry.Get(Sub("Directory"), "MUIVerb"));

        using (L.Use(laterLanguage))
        {
            Assert.Equal(DetectionState.Applied, engine.Detect(def));

            var result = engine.Undo(def);

            Assert.Equal(TweakOutcome.Undone, result.Outcome);
            Assert.Empty(result.Warnings);
            Assert.All(AllOurKeys(), key => Assert.False(f.Registry.HasKey(key), key));
            Assert.Equal(DetectionState.NotApplied, engine.Detect(def));
        }
    }

    [Fact]
    public void Undo_RemovesEveryNestedKeyWeCreated_AndLeavesTheShellKeysWithoutWarnings()
    {
        var f = NewFixture();
        var engine = f.CreateEngine();
        var def = Get();
        engine.Apply(def);

        var result = engine.Undo(def);

        Assert.Equal(TweakOutcome.Undone, result.Outcome);
        Assert.Empty(result.Warnings);
        Assert.All(AllOurKeys(), key => Assert.False(f.Registry.HasKey(key), key));
        Assert.True(f.Registry.HasKey($@"{ClassesRoot}\*\shell"));
        Assert.True(f.Registry.HasKey($@"{ClassesRoot}\Directory\shell"));
        Assert.All(f.Registry.DeletedKeys, key => Assert.Contains("DSOptimizer.TakeOwnership", key, StringComparison.Ordinal));
        Assert.Equal(DetectionState.NotApplied, engine.Detect(def));
    }

    [Fact]
    public void Undo_LeavesForeignRunasUntouched_AndNeverWritesToIt()
    {
        // Review Focus 3: cudzy `*\shell\runas` (i jego `command`) przed apply = taki sam po undo.
        var f = NewFixture();
        const string foreign = ClassesRoot + @"\*\shell\runas";
        f.Registry.Set(foreign, string.Empty, RegValue.FromString("Obcy verb"));
        f.Registry.Set(foreign + @"\command", string.Empty, RegValue.FromString("obce.exe \"%1\""));
        f.Registry.Set(ClassesRoot + @"\Directory\shell\runas", "Extended", RegValue.FromString(string.Empty));
        var engine = f.CreateEngine();
        var def = Get();

        Assert.Equal(TweakOutcome.Applied, engine.Apply(def).Outcome);
        var undo = engine.Undo(def);

        Assert.Equal(TweakOutcome.Undone, undo.Outcome);
        Assert.Empty(undo.Warnings);
        Assert.Equal(RegValue.FromString("Obcy verb"), f.Registry.Get(foreign, string.Empty));
        Assert.Equal(RegValue.FromString("obce.exe \"%1\""), f.Registry.Get(foreign + @"\command", string.Empty));
        Assert.Equal(RegValue.FromString(string.Empty), f.Registry.Get(ClassesRoot + @"\Directory\shell\runas", "Extended"));
        Assert.True(f.Registry.HasKey(foreign));
        Assert.DoesNotContain(f.Registry.WriteLog, line => line.Contains(@"\shell\runas", StringComparison.OrdinalIgnoreCase)
                                                           && !line.Contains("DSOptimizer.TakeOwnership", StringComparison.Ordinal));
        Assert.DoesNotContain(f.Registry.DeletedKeys, key => key.EndsWith(@"\runas", StringComparison.OrdinalIgnoreCase)
                                                             && !key.Contains("DSOptimizer.TakeOwnership", StringComparison.Ordinal));
    }

    [Fact]
    public void Undo_ValueAddedByOthersToOurKey_KeepsTheKeyAndWarns()
    {
        // Review Focus 3 / A1: ktoś dopisał wartość do naszego klucza -> klucz zostaje, ostrzeżenie; nasze wartości znikają.
        var f = NewFixture();
        var engine = f.CreateEngine();
        var def = Get();
        engine.Apply(def);
        f.Registry.Set(Root("*"), "Obca", RegValue.FromDWord(5));

        var result = engine.Undo(def);

        Assert.Equal(TweakOutcome.Undone, result.Outcome);
        var warning = Assert.Single(result.Warnings, w => w.StartsWith("Klucz ", StringComparison.Ordinal));
        Assert.Contains(Root("*"), warning, StringComparison.Ordinal);
        Assert.True(f.Registry.HasKey(Root("*")));
        Assert.Equal(RegValue.FromDWord(5), f.Registry.Get(Root("*"), "Obca"));
        Assert.Equal(RegValue.Absent, f.Registry.Get(Root("*"), "MUIVerb"));
        Assert.False(f.Registry.HasKey(Sub("*")));
        // klucze klasy Directory nie mają obcych wartości, więc znikają w całości
        Assert.False(f.Registry.HasKey(Root("Directory")));
    }

    [Fact]
    public void Undo_ValueAddedByOthersToNestedCommandKey_KeepsOnlyTheNonEmptyBranch()
    {
        var f = NewFixture();
        var engine = f.CreateEngine();
        var def = Get();
        engine.Apply(def);
        f.Registry.Set(Sub("Directory") + @"\command", "Obca", RegValue.FromString("x"));

        var result = engine.Undo(def);

        Assert.Equal(TweakOutcome.Undone, result.Outcome);
        Assert.Contains(result.Warnings, w => w.StartsWith("Klucz ", StringComparison.Ordinal));
        Assert.True(f.Registry.HasKey(Sub("Directory") + @"\command"));
        Assert.Equal(RegValue.Absent, f.Registry.Get(Sub("Directory") + @"\command", string.Empty));
        Assert.False(f.Registry.HasKey(Root("*")));
    }
}
