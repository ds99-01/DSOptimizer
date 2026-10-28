using System;
using System.Collections.Generic;
using DSOptimizer.Core.Localization;
using Xunit;

namespace DSOptimizer.Tests.Unit.Localization;

public class TranslationTableTests
{
    private static string Json(params (string Pl, string En)[] entries)
    {
        var parts = new List<string>();
        foreach (var (pl, en) in entries)
        {
            parts.Add("{\"pl\":\"" + pl + "\",\"en\":\"" + en + "\"}");
        }

        return "{\"language\":\"en\",\"entries\":[" + string.Join(",", parts) + "]}";
    }

    private static TranslationTable LoadOne(string json) =>
        TranslationTable.Load(new[] { new TranslationSource("a.json", json) });

    [Fact]
    public void Load_ValidEntry_FindReturnsEnglish()
    {
        var table = LoadOne(Json(("Ustawienia", "Settings")));
        Assert.Equal("Settings", table.Find(UiLanguage.English, "Ustawienia"));
        Assert.Equal(1, table.Count);
    }

    [Fact]
    public void Load_EnglishWithStrayBrace_Throws()
    {
        Assert.Throws<TranslationFormatException>(() => LoadOne(Json(("Plik {0}", "File {0} {"))));
    }

    [Fact]
    public void Load_EnglishWithEscapedBraces_Passes()
    {
        var table = LoadOne(Json(("Plik {0}", "File {{x}} {0}")));
        Assert.Equal("File {{x}} {0}", table.Find(UiLanguage.English, "Plik {0}"));
    }

    [Fact]
    public void Find_Polish_ReturnsNull()
    {
        var table = LoadOne(Json(("Ustawienia", "Settings")));
        Assert.Null(table.Find(UiLanguage.Polish, "Ustawienia"));
    }

    [Fact]
    public void Find_MissingText_ReturnsNull()
    {
        var table = LoadOne(Json(("Ustawienia", "Settings")));
        Assert.Null(table.Find(UiLanguage.English, "Nic"));
        Assert.Equal(0, TranslationTable.Empty.Count);
    }

    [Fact]
    public void Load_DuplicatePolishAcrossFiles_Throws()
    {
        var ex = Assert.Throws<TranslationFormatException>(() => TranslationTable.Load(new[]
        {
            new TranslationSource("one.json", Json(("Tak", "Yes"))),
            new TranslationSource("two.json", Json(("Tak", "Yep"))),
        }));
        Assert.Contains("one.json", ex.Message, StringComparison.Ordinal);
        Assert.Contains("two.json", ex.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Load_DuplicateJsonKey_Throws()
    {
        var json = "{\"language\":\"en\",\"entries\":[{\"pl\":\"A\",\"pl\":\"B\",\"en\":\"C\"}]}";
        var ex = Assert.Throws<TranslationFormatException>(() => LoadOne(json));
        Assert.Contains("a.json", ex.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Load_WrongLanguage_Throws()
    {
        Assert.Throws<TranslationFormatException>(() => LoadOne("{\"language\":\"de\",\"entries\":[]}"));
    }

    [Fact]
    public void Load_EmptyEnglish_Throws()
    {
        Assert.Throws<TranslationFormatException>(() => LoadOne(Json(("A", ""))));
    }

    [Fact]
    public void Load_ExtraKey_Throws()
    {
        var json = "{\"language\":\"en\",\"entries\":[{\"pl\":\"A\",\"en\":\"B\",\"de\":\"C\"}]}";
        Assert.Throws<TranslationFormatException>(() => LoadOne(json));
    }

    [Fact]
    public void Load_PlaceholderMismatch_Throws()
    {
        Assert.Throws<TranslationFormatException>(() => LoadOne(Json(("Plik {0}", "File"))));
    }

    [Fact]
    public void Load_PlaceholderReordered_Passes()
    {
        var table = LoadOne(Json(("{0} z {1}", "{1} of {0}")));
        Assert.Equal("{1} of {0}", table.Find(UiLanguage.English, "{0} z {1}"));
    }

    [Fact]
    public void Load_FormatSpecifiersAndAlignment_AreSamePlaceholders()
    {
        var table = LoadOne(Json(("{0:N1} {1,5}", "{1,5} {0:N2}")));
        Assert.Equal(1, table.Count);
    }

    [Fact]
    public void Load_EscapedBraces_AreNotPlaceholders()
    {
        var table = LoadOne(Json(("{{x}} {0}", "{0} {{x}}")));
        Assert.Equal(1, table.Count);
    }

    [Fact]
    public void Load_TrailingSpaceMismatch_Throws()
    {
        Assert.Throws<TranslationFormatException>(() => LoadOne(Json(("Tekst ", "Text"))));
    }

    [Fact]
    public void Load_LeadingSpaceMismatch_Throws()
    {
        Assert.Throws<TranslationFormatException>(() => LoadOne(Json((" Tekst", "Text"))));
    }

    [Fact]
    public void Load_ReportsAllErrors()
    {
        var ex = Assert.Throws<TranslationFormatException>(() => LoadOne(Json(("A {0}", "A"), ("B", ""))));
        Assert.Equal(2, ex.Errors.Count);
    }

    [Fact]
    public void Load_NullSources_Throws()
    {
        Assert.Throws<ArgumentNullException>(() => TranslationTable.Load(null!));
    }

    [Fact]
    public void Load_InvalidJson_Throws()
    {
        Assert.Throws<TranslationFormatException>(() => LoadOne("{nie json"));
    }
}
