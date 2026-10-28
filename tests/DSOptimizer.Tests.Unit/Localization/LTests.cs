using System;
using System.Threading.Tasks;
using DSOptimizer.Core.Localization;
using Xunit;

namespace DSOptimizer.Tests.Unit.Localization;

public class LTests
{
    private static TranslationTable Table(string pl, string en) =>
        TranslationTable.Load(new[]
        {
            new TranslationSource("t.json", "{\"language\":\"en\",\"entries\":[{\"pl\":\"" + pl + "\",\"en\":\"" + en + "\"}]}"),
        });

    [Fact]
    public void T_DefaultIsPolish_ReturnsSource()
    {
        Assert.Equal(UiLanguage.Polish, L.Current);
        Assert.Equal("Ustawienia", L.T("Ustawienia"));
    }

    [Fact]
    public void T_EnglishWithTranslation_ReturnsEnglish()
    {
        using (L.UseTable(Table("Ustawienia", "Settings")))
        using (L.Use(UiLanguage.English))
        {
            Assert.Equal("Settings", L.T("Ustawienia"));
        }
    }

    [Fact]
    public void T_EnglishWithoutTranslation_ReturnsPolish()
    {
        using (L.UseTable(Table("Ustawienia", "Settings")))
        using (L.Use(UiLanguage.English))
        {
            Assert.Equal("Inny tekst", L.T("Inny tekst"));
        }
    }

    [Fact]
    public void T_PolishWithTable_ReturnsSource()
    {
        using (L.UseTable(Table("Ustawienia", "Settings")))
        using (L.Use(UiLanguage.Polish))
        {
            Assert.Equal("Ustawienia", L.T("Ustawienia"));
        }
    }

    [Fact]
    public void Use_Nested_RestoresPrevious()
    {
        using (L.Use(UiLanguage.English))
        {
            using (L.Use(UiLanguage.Polish))
            {
                Assert.Equal(UiLanguage.Polish, L.Current);
            }

            Assert.Equal(UiLanguage.English, L.Current);
        }

        Assert.Equal(UiLanguage.Polish, L.Current);
    }

    [Fact]
    public async Task Use_DoesNotLeakToOtherFlow()
    {
        var english = Task.Run(async () =>
        {
            using (L.Use(UiLanguage.English))
            {
                await Task.Delay(50);
                return L.Current;
            }
        });
        var polish = Task.Run(async () =>
        {
            using (L.Use(UiLanguage.Polish))
            {
                await Task.Delay(50);
                return L.Current;
            }
        });

        Assert.Equal(UiLanguage.English, await english);
        Assert.Equal(UiLanguage.Polish, await polish);
        Assert.Equal(UiLanguage.Polish, L.Current);
    }

    [Fact]
    public void F_English_UsesEnGbCulture()
    {
        using (L.Use(UiLanguage.English))
        {
            Assert.Equal("en-GB", L.Culture.Name);
            Assert.Equal("1,234.5", L.F("{0:N1}", 1234.5));
        }
    }

    [Fact]
    public void F_Polish_UsesPlPlCulture()
    {
        using (L.Use(UiLanguage.Polish))
        {
            Assert.Equal("pl-PL", L.Culture.Name);
            Assert.Equal("1 234,5", L.F("{0:N1}", 1234.5));
        }
    }

    [Fact]
    public void F_English_FormatsTranslatedText()
    {
        using (L.UseTable(Table("Plik {0}", "File {0}")))
        using (L.Use(UiLanguage.English))
        {
            Assert.Equal("File x", L.F("Plik {0}", "x"));
        }
    }

    [Fact]
    public void Cultures_AreReadOnly()
    {
        using (L.Use(UiLanguage.English))
        {
            Assert.True(L.Culture.IsReadOnly);
        }
    }

    [Fact]
    public void T_Null_Throws()
    {
        Assert.Throws<ArgumentNullException>(() => L.T(null!));
        Assert.Throws<ArgumentNullException>(() => L.F(null!));
    }

    private static TranslationTable PluralTable() =>
        TranslationTable.Load(new[]
        {
            new TranslationSource(
                "t.json",
                "{\"language\":\"en\",\"entries\":["
                + "{\"pl\":\"{0} pozycja\",\"en\":\"{0} item\"},"
                + "{\"pl\":\"{0} pozycje\",\"en\":\"{0} items\"},"
                + "{\"pl\":\"{0} pozycji\",\"en\":\"{0} items\"}]}"),
        });

    private static string Items(int count) => L.P("{0} pozycja", "{0} pozycje", "{0} pozycji", count);

    [Fact]
    public void P_Polish_ChoosesForm()
    {
        using (L.UseTable(PluralTable()))
        using (L.Use(UiLanguage.Polish))
        {
            Assert.Equal("1 pozycja", Items(1));
            Assert.Equal("2 pozycje", Items(2));
            Assert.Equal("5 pozycji", Items(5));
            Assert.Equal("22 pozycje", Items(22));
            Assert.Equal("12 pozycji", Items(12));
            Assert.Equal("0 pozycji", Items(0));
            Assert.Equal("1234 pozycje", Items(1234));
            Assert.Equal("1235 pozycji", Items(1235));
        }
    }

    [Fact]
    public void P_English_OneUsesOne()
    {
        using (L.UseTable(PluralTable()))
        using (L.Use(UiLanguage.English))
        {
            Assert.Equal("1 item", Items(1));
        }
    }

    [Fact]
    public void P_English_OtherUsesMany()
    {
        using (L.UseTable(PluralTable()))
        using (L.Use(UiLanguage.English))
        {
            Assert.Equal("0 items", Items(0));
            Assert.Equal("2 items", Items(2));
            Assert.Equal("5 items", Items(5));
            Assert.Equal("22 items", Items(22));
            Assert.Equal("1234 items", Items(1234));
        }
    }

    [Fact]
    public void P_English_MissingTranslation_FallsBackToPolishForm()
    {
        using (L.UseTable(TranslationTable.Empty))
        using (L.Use(UiLanguage.English))
        {
            Assert.Equal("1 pozycja", Items(1));
            Assert.Equal("2 pozycje", Items(2));
            Assert.Equal("5 pozycji", Items(5));
            Assert.Equal("1235 pozycji", Items(1235));
        }
    }

    [Fact]
    public void P_FormWithoutPlaceholder_Throws()
    {
        Assert.Throws<ArgumentException>(() => L.P("pozycja", "{0} pozycje", "{0} pozycji", 1));
        Assert.Throws<ArgumentException>(() => L.P("{0} pozycja", "pozycje", "{0} pozycji", 5));
        Assert.Throws<ArgumentException>(() => L.P("{0} pozycja", "{0} pozycje", "pozycji", 5));
        Assert.Throws<ArgumentNullException>(() => L.P(null!, "{0} pozycje", "{0} pozycji", 5));
    }

    [Fact]
    public void LoadBuiltIn_ThrowingSource_YieldsEmptyTableAndWarning()
    {
        var table = L.LoadBuiltIn(() => throw new UnauthorizedAccessException("brak dostepu"), out var warning);
        Assert.NotNull(warning);
        Assert.Equal(0, table.Count);
        using (L.UseTable(table))
        {
            Assert.Equal("Ustawienia", L.T("Ustawienia"));
        }
    }
}
