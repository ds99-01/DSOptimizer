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

    private const string Guid = "{21EC2020-3AEA-1069-A2DD-08002B30309D}";

    private static string DataJson(string data, params (string Pl, string En)[] entries) =>
        Json(entries).Replace("{\"language\":\"en\",", "{\"language\":\"en\",\"data\":" + data + ",");

    [Fact]
    public void Data_GuidInText_Loads()
    {
        var table = LoadOne(DataJson("true", ("Otwiera shell:::" + Guid + ".", "Opens shell:::" + Guid + ".")));
        Assert.Equal("Opens shell:::" + Guid + ".", table.Find(UiLanguage.English, "Otwiera shell:::" + Guid + "."));
    }

    [Fact]
    public void Data_BraceGroupsDiffer_Rejected()
    {
        Assert.Throws<TranslationFormatException>(() => LoadOne(DataJson(
            "true",
            ("Otwiera shell:::" + Guid + ".", "Opens shell:::{21EC2020-3AEA-1069-A2DD-08002B30309E}."))));
    }

    [Fact]
    public void Data_FormatGroupKept_Loads()
    {
        var table = LoadOne(DataJson("true", ("Kod {0:X8}.", "Code {0:X8}.")));
        Assert.Equal("Code {0:X8}.", table.Find(UiLanguage.English, "Kod {0:X8}."));
    }

    [Fact]
    public void Data_DroppedPlaceholder_Rejected()
    {
        Assert.Throws<TranslationFormatException>(() => LoadOne(DataJson("true", ("Plik {0} usunięty.", "File removed."))));
    }

    [Fact]
    public void Data_ExtraBrace_Rejected()
    {
        Assert.Throws<TranslationFormatException>(() => LoadOne(DataJson("true", ("Plik {0}.", "File {0} {."))));
    }

    [Fact]
    public void Data_NotBoolean_Rejected()
    {
        var ex = Assert.Throws<TranslationFormatException>(() => LoadOne(DataJson("\"true\"", ("Tak", "Yes"))));
        Assert.Contains("a.json", ex.Message, StringComparison.Ordinal);
        Assert.Throws<TranslationFormatException>(() => LoadOne(DataJson("1", ("Tak", "Yes"))));
    }

    [Fact]
    public void Data_False_KeepsFormatRules()
    {
        Assert.Throws<TranslationFormatException>(() => LoadOne(DataJson("false", ("Plik " + Guid, "File " + Guid))));
        Assert.Equal(1, LoadOne(DataJson("false", ("Tak", "Yes"))).Count);
    }

    [Fact]
    public void NonData_GuidStillRejected()
    {
        Assert.Throws<TranslationFormatException>(() => LoadOne(Json(("Otwiera shell:::" + Guid + ".", "Opens shell:::" + Guid + "."))));
    }

    [Fact]
    public void Data_WhitespaceRulesStillApply()
    {
        Assert.Throws<TranslationFormatException>(() => LoadOne(DataJson("true", ("Tekst ", "Text"))));
    }

    [Fact]
    public void Data_DuplicateWithCodeTable_Rejected()
    {
        var ex = Assert.Throws<TranslationFormatException>(() => TranslationTable.Load(new[]
        {
            new TranslationSource("gui.json", Json(("Zapisz", "Save"))),
            new TranslationSource("catalog-x.json", DataJson("true", ("Zapisz", "Save it"))),
        }));
        Assert.Contains("gui.json", ex.Message, StringComparison.Ordinal);
        Assert.Contains("catalog-x.json", ex.Message, StringComparison.Ordinal);
    }
}
