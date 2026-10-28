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
