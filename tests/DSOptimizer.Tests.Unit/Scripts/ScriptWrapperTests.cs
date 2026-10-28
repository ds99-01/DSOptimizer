using System;
using System.Linq;
using DSOptimizer.Core.Localization;
using DSOptimizer.Core.Scripts;
using DSOptimizer.Tests.Unit.Catalog;
using DSOptimizer.Tests.Unit.Support;
using Xunit;

namespace DSOptimizer.Tests.Unit.Scripts;

public class ScriptWrapperTests
{
    private const string Param = "param([string]$UserSid = $env:DSO_USER_SID, [string]$Snapshot = $env:DSO_SNAPSHOT)";

    private static string[] Lines(string body) => ScriptWrapper.Wrap(body).Split(new[] { "\r\n" }, StringSplitOptions.None);

    private static string[] DictionaryLines(string[] lines) =>
        lines.Where(l => l.StartsWith("$DsoTexts[", StringComparison.Ordinal)).ToArray();

    [Fact]
    public void Wrap_DeclaresParametersFromEnvironment()
        => Assert.Equal(Param, Lines("x")[0]);

    [Fact]
    public void Wrap_MakesErrorsTerminatingAndOutputUtf8()
    {
        var lines = Lines("x");
        Assert.Contains("$ErrorActionPreference = 'Stop'", lines);
        Assert.Contains("[Console]::OutputEncoding = [System.Text.Encoding]::UTF8", lines);
        Assert.Contains("trap { [Console]::Error.WriteLine($_.ToString()); exit 1 }", lines);
    }

    [Fact]
    public void Wrap_PutsBodyAfterTrapAndBeforeExit()
    {
        var lines = Lines("Write-Output 'x'");
        var trap = Array.FindIndex(lines, line => line.StartsWith("trap", StringComparison.Ordinal));
        var body = Array.IndexOf(lines, "Write-Output 'x'");
        Assert.True(body > trap);
        Assert.Equal("if ($LASTEXITCODE -and $LASTEXITCODE -ne 0) { exit $LASTEXITCODE }", lines[lines.Length - 2]);
        Assert.Equal("exit 0", lines[lines.Length - 1]);
    }

    [Fact]
    public void Wrap_NullBody_Throws()
        => Assert.Throws<ArgumentNullException>(() => ScriptWrapper.Wrap(null!));

    [Fact]
    public void Wrap_ParamStaysFirstLine()
    {
        using (L.Use(UiLanguage.English))
        using (L.UseTable(CatalogTranslationTests.DataTable(("Gotowe.", "Done."))))
        {
            var lines = Lines("Get-DsoText 'Gotowe.'");
            Assert.Equal(Param, lines[0]);
            Assert.Equal("$DsoTexts['Gotowe.'] = 'Done.'", Assert.Single(DictionaryLines(lines)));
            var function = Array.FindIndex(lines, l => l.StartsWith("function Get-DsoText", StringComparison.Ordinal));
            var trap = Array.FindIndex(lines, l => l.StartsWith("trap", StringComparison.Ordinal));
            Assert.True(function > 0 && function < trap);
        }
    }

    [Fact]
    public void Wrap_Polish_EmptyDictionary()
    {
        using (L.Use(UiLanguage.Polish))
        using (L.UseTable(CatalogTranslationTests.DataTable(("Gotowe.", "Done."))))
        {
            var lines = Lines("Get-DsoText 'Gotowe.'");
            Assert.Empty(DictionaryLines(lines));
            Assert.Contains("$DsoTextCulture = [System.Globalization.CultureInfo]::GetCultureInfo('pl-PL')", lines);
            Assert.Contains(lines, l => l.StartsWith("function Get-DsoText", StringComparison.Ordinal));
        }
    }

    [Fact]
    public void Wrap_English_ContainsOnlyTranslatedTexts()
    {
        using (L.Use(UiLanguage.English))
        using (L.UseTable(CatalogTranslationTests.DataTable(("Gotowe.", "Done."), ("OK", "OK"), ("Nieużyty", "Unused"))))
        {
            var lines = Lines("Get-DsoText 'Gotowe.'\nGet-DsoText 'OK'\nGet-DsoText 'Brak tłumaczenia'");
            Assert.Equal(new[] { "$DsoTexts['Gotowe.'] = 'Done.'" }, DictionaryLines(lines));
            Assert.Contains("$DsoTextCulture = [System.Globalization.CultureInfo]::GetCultureInfo('en-GB')", lines);
        }
    }

    [Fact]
    public void Wrap_EscapesApostrophes()
    {
        const string Body = "Write-Output (Get-DsoText 'Plik ''{0}'' – nie ma.' 'a.txt')";
        using (L.Use(UiLanguage.English))
        using (L.UseTable(CatalogTranslationTests.DataTable(("Plik '{0}' – nie ma.", "File '{0}' doesn’t exist ‚x‛ ‘y’."))))
        {
            Assert.Equal(
                "$DsoTexts['Plik ''{0}'' – nie ma.'] = 'File ''{0}'' doesn’’t exist ‚‚x‛‛ ‘‘y’’.'",
                Assert.Single(DictionaryLines(Lines(Body))));
            var run = PowerShellHarness.Run(Body, string.Empty, string.Empty);
            Assert.Equal(0, run.ExitCode);
            Assert.Equal("File 'a.txt' doesn’t exist ‚x‛ ‘y’.", run.LastLine);
        }
    }

    [Fact]
    public void Wrap_English_TranslatesFormattedText()
    {
        using (L.Use(UiLanguage.English))
        using (L.UseTable(CatalogTranslationTests.DataTable(("Usunięto {0} plików.", "Removed {0} files."))))
        {
            var run = PowerShellHarness.Run("Write-Output (Get-DsoText 'Usunięto {0} plików.' 5)", string.Empty, string.Empty);
            Assert.Equal(0, run.ExitCode);
            Assert.Equal("Removed 5 files.", run.LastLine);
        }
    }

    [Fact]
    public void Wrap_Polish_SameAsConcatenation()
    {
        using (L.Use(UiLanguage.Polish))
        using (L.UseTable(CatalogTranslationTests.DataTable(("Usunięto {0} plików.", "Removed {0} files."))))
        {
            var run = PowerShellHarness.Run(
                "$n = 5\nWrite-Output (Get-DsoText 'Usunięto {0} plików.' $n)\nWrite-Output ('Usunięto ' + $n + ' plików.')",
                string.Empty,
                string.Empty);
            Assert.Equal(0, run.ExitCode);
            Assert.Equal(new[] { "Usunięto 5 plików.", "Usunięto 5 plików." }, run.Output);
        }
    }

    [Fact]
    public void Wrap_HexFormat()
    {
        using (L.Use(UiLanguage.English))
        using (L.UseTable(CatalogTranslationTests.DataTable(("Kod 0x{0:X8}, wynik {1}.", "Code 0x{0:X8}, result {1}."))))
        {
            var run = PowerShellHarness.Run(
                "$code = [int]-2147024891\nWrite-Output (Get-DsoText 'Kod 0x{0:X8}, wynik {1}.' $code 255)\nWrite-Output (Get-DsoText 'Kod 0x{0:X8}, wynik {1}.' ([uint32]2147942405) 'x')",
                string.Empty,
                string.Empty);
            Assert.Equal(0, run.ExitCode);
            Assert.Equal(new[] { "Code 0x80070005, result 255.", "Code 0x80070005, result x." }, run.Output);
        }
    }

    [Fact]
    public void Wrap_BracesWithoutArgs_Unchanged()
    {
        const string Body = "Write-Output (Get-DsoText 'Klucz {abc} i {0}')";
        using (L.UseTable(CatalogTranslationTests.DataTable(("Klucz {abc} i {0}", "Key {abc} and {0}"))))
        {
            using (L.Use(UiLanguage.Polish))
            {
                Assert.Equal("Klucz {abc} i {0}", PowerShellHarness.Run(Body, string.Empty, string.Empty).LastLine);
            }

            using (L.Use(UiLanguage.English))
            {
                Assert.Equal("Key {abc} and {0}", PowerShellHarness.Run(Body, string.Empty, string.Empty).LastLine);
            }
        }
    }

    [Fact]
    public void Wrap_KeysDifferOnlyByCase_BothTranslated()
    {
        using (L.Use(UiLanguage.English))
        using (L.UseTable(CatalogTranslationTests.DataTable(("Gotowe", "Done"), ("GOTOWE", "DONE"))))
        {
            var run = PowerShellHarness.Run("Write-Output (Get-DsoText 'Gotowe')\nWrite-Output (Get-DsoText 'GOTOWE')", string.Empty, string.Empty);
            Assert.Equal(0, run.ExitCode);
            Assert.Equal(new[] { "Done", "DONE" }, run.Output);
        }
    }

    [Fact]
    public void Wrap_Missing_ReturnsPolish()
    {
        using (L.Use(UiLanguage.English))
        using (L.UseTable(CatalogTranslationTests.DataTable(("Inny tekst", "Other text"))))
        {
            var run = PowerShellHarness.Run("Write-Output (Get-DsoText 'Zostało {0} z {1}.' 2 3)\nWrite-Output (Get-DsoText 'Bez tłumaczenia')", string.Empty, string.Empty);
            Assert.Equal(0, run.ExitCode);
            Assert.Equal(new[] { "Zostało 2 z 3.", "Bez tłumaczenia" }, run.Output);
        }
    }

    [Fact]
    public void Wrap_ThrowWithText_EnglishMessageOnStderr()
    {
        using (L.Use(UiLanguage.English))
        using (L.UseTable(CatalogTranslationTests.DataTable(("Nie udało się zatrzymać usługi {0}.", "Could not stop the {0} service."))))
        {
            var run = PowerShellHarness.Run("throw (Get-DsoText 'Nie udało się zatrzymać usługi {0}.' 'wuauserv')", string.Empty, string.Empty);
            Assert.Equal(1, run.ExitCode);
            Assert.Contains("Could not stop the wuauserv service.", run.Error, StringComparison.Ordinal);
            Assert.DoesNotContain("usługi", run.Error, StringComparison.Ordinal);
        }
    }
}
