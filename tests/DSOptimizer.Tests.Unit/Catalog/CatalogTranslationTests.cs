using System.Linq;
using DSOptimizer.Core.Catalog;
using DSOptimizer.Core.Localization;
using DSOptimizer.Core.Model;
using Newtonsoft.Json;
using Xunit;

namespace DSOptimizer.Tests.Unit.Catalog;

public class CatalogTranslationTests
{
    private const string Hash = "sha256:abc";

    internal static TranslationTable DataTable(params (string Pl, string En)[] entries)
    {
        var json = JsonConvert.SerializeObject(new
        {
            language = "en",
            data = true,
            entries = entries.Select(e => new { pl = e.Pl, en = e.En }).ToArray(),
        });
        return TranslationTable.Load(new[] { new TranslationSource("catalog-test.json", json) });
    }

    private static TweakDefinition Tweak() => new()
    {
        Id = "test.item",
        Category = "test",
        Kind = TweakKind.Tweak,
        Title = "Wyłącz coś",
        Summary = "Krótki opis.",
        Benefit = "Zysk na co dzień.",
        Description = "Ustawia HKLM\\SOFTWARE\\X = 1.",
        Risk = RiskLevel.Caution,
        Scope = TweakScope.Machine,
        Warnings = new[] { "Pierwsza uwaga.", "Druga uwaga." },
        Script = new ScriptSet { Apply = "Write-Output 'a'", Detect = "Write-Output 'd'" },
        Registry = new[] { new RegistryEntry { Path = @"HKLM\SOFTWARE\X", Name = "A", Value = RegValue.FromDWord(1) } },
    };

    private static TweakDefinition Panel() => new()
    {
        Id = "test.panel",
        Category = "test",
        Kind = TweakKind.Panel,
        Title = "Panel sterowania",
        Summary = "Otwiera panel.",
        Description = "Otwiera shell:::{21EC2020-3AEA-1069-A2DD-08002B30309D}.",
        Risk = RiskLevel.Safe,
        Scope = TweakScope.Machine,
        Launch = new LaunchSpec { File = "shell:::{21EC2020-3AEA-1069-A2DD-08002B30309D}" },
    };

    private static TweakDefinition Action() => new()
    {
        Id = "test.action",
        Category = "test",
        Kind = TweakKind.Action,
        Title = "Napraw coś",
        Summary = "Naprawia.",
        Benefit = "Działa znowu.",
        Description = "Handler w C#.",
        Risk = RiskLevel.Safe,
        Scope = TweakScope.Machine,
        Handler = "restore-point",
    };

    private static LoadedCatalog Catalog() => new(new[] { Tweak(), Panel(), Action() }, Hash);

    private static TranslationTable FullTable() => DataTable(
        ("Wyłącz coś", "Turn off something"),
        ("Krótki opis.", "Short summary."),
        ("Zysk na co dzień.", "Everyday gain."),
        ("Ustawia HKLM\\SOFTWARE\\X = 1.", "Sets HKLM\\SOFTWARE\\X = 1."),
        ("Pierwsza uwaga.", "First warning."),
        ("Druga uwaga.", "Second warning."),
        ("Panel sterowania", "Control Panel"),
        ("Otwiera panel.", "Opens the panel."),
        ("Otwiera shell:::{21EC2020-3AEA-1069-A2DD-08002B30309D}.", "Opens shell:::{21EC2020-3AEA-1069-A2DD-08002B30309D}."),
        ("Napraw coś", "Repair something"),
        ("Naprawia.", "Repairs."),
        ("Działa znowu.", "Works again."),
        ("Handler w C#.", "C# handler."));

    [Fact]
    public void Translate_English_TranslatesTextFields()
    {
        using (L.UseTable(FullTable()))
        using (L.Use(UiLanguage.English))
        {
            var result = CatalogTranslation.Translate(Catalog());

            var tweak = result.Definitions[0];
            Assert.Equal("Turn off something", tweak.Title);
            Assert.Equal("Short summary.", tweak.Summary);
            Assert.Equal("Everyday gain.", tweak.Benefit);
            Assert.Equal("Sets HKLM\\SOFTWARE\\X = 1.", tweak.Description);
            Assert.Equal(new[] { "First warning.", "Second warning." }, tweak.Warnings);

            var panel = result.Definitions[1];
            Assert.Equal("Control Panel", panel.Title);
            Assert.Equal("Opens shell:::{21EC2020-3AEA-1069-A2DD-08002B30309D}.", panel.Description);
            Assert.Equal("Repair something", result.Definitions[2].Title);
        }
    }

    [Fact]
    public void Translate_KeepsHashIdsAndExecutionFields()
    {
        var source = Catalog();
        using (L.UseTable(FullTable()))
        using (L.Use(UiLanguage.English))
        {
            var result = CatalogTranslation.Translate(source);

            Assert.NotSame(source, result);
            Assert.Equal(Hash, result.Hash);
            Assert.Equal(source.Definitions.Count, result.Definitions.Count);
            for (var i = 0; i < source.Definitions.Count; i++)
            {
                var before = source.Definitions[i];
                var after = result.Definitions[i];
                Assert.Equal(before.Id, after.Id);
                Assert.Equal(before.Category, after.Category);
                Assert.Equal(before.Kind, after.Kind);
                Assert.Equal(before.Risk, after.Risk);
                Assert.Same(before.Script, after.Script);
                Assert.Same(before.Registry, after.Registry);
                Assert.Same(before.Services, after.Services);
                Assert.Same(before.Launch, after.Launch);
                Assert.Equal(before.Handler, after.Handler);
                Assert.Same(before.Conflicts, after.Conflicts);
            }
        }
    }

    [Fact]
    public void Translate_Polish_ReturnsSameInstance()
    {
        var source = Catalog();
        using (L.UseTable(FullTable()))
        using (L.Use(UiLanguage.Polish))
        {
            Assert.Same(source, CatalogTranslation.Translate(source));
        }
    }

    [Fact]
    public void Translate_Missing_KeepsPolish()
    {
        using (L.UseTable(DataTable(("Wyłącz coś", "Turn off something"))))
        using (L.Use(UiLanguage.English))
        {
            var tweak = CatalogTranslation.Translate(Catalog()).Definitions[0];

            Assert.Equal("Turn off something", tweak.Title);
            Assert.Equal("Krótki opis.", tweak.Summary);
            Assert.Equal("Zysk na co dzień.", tweak.Benefit);
            Assert.Equal(new[] { "Pierwsza uwaga.", "Druga uwaga." }, tweak.Warnings);
        }
    }

    [Fact]
    public void Translate_NullBenefit_StaysNull()
    {
        using (L.UseTable(FullTable()))
        using (L.Use(UiLanguage.English))
        {
            var panel = CatalogTranslation.Translate(Catalog()).Definitions[1];

            Assert.Null(panel.Benefit);
            Assert.Empty(panel.Warnings);
        }
    }

    [Fact]
    public void Translate_Null_Throws()
    {
        Assert.Throws<System.ArgumentNullException>(() => CatalogTranslation.Translate(null!));
    }
}
