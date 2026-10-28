using System;
using DSOptimizer.Core.Cli;
using DSOptimizer.Core.Engine;
using DSOptimizer.Core.Localization;
using Xunit;

namespace DSOptimizer.Tests.Unit.Cli;

public class CliParserTests
{
    [Fact]
    public void Parse_DetectWithOut_Succeeds()
    {
        var result = CliParser.Parse(new[] { "--detect", "vmtest.hklm-value", "--out", @"C:\dso\out\a.json" });
        Assert.True(result.IsSuccess);
        Assert.Equal(CliCommand.Detect, result.Options!.Command);
        Assert.Equal("vmtest.hklm-value", result.Options.TweakId);
        Assert.Equal(@"C:\dso\out\a.json", result.Options.OutPath);
        Assert.Null(result.Options.CatalogPath);
    }

    [Fact]
    public void Parse_ApplyWithCatalogAndRestorePoint_Succeeds()
    {
        var result = CliParser.Parse(new[] { "--APPLY", "x.y", "--catalog", @"C:\k.json", "--restore-point", "Skip", "--out", "o.json" });
        Assert.Equal(CliCommand.Apply, result.Options!.Command);
        Assert.Equal(@"C:\k.json", result.Options.CatalogPath);
        Assert.Equal(RestorePointPolicy.Skip, result.Options.RestorePointPolicy);
    }

    [Fact]
    public void Parse_Execute_Succeeds()
    {
        var result = CliParser.Parse(new[] { "--execute", "x.y", "--catalog", @"C:\k.json", "--out", "o.json" });
        Assert.True(result.IsSuccess);
        Assert.Equal(CliCommand.Execute, result.Options!.Command);
        Assert.Equal("x.y", result.Options.TweakId);
    }

    [Fact]
    public void Parse_ExecuteWithRestorePoint_Fails()
    {
        var result = CliParser.Parse(new[] { "--execute", "x.y", "--restore-point", "skip", "--out", "o.json" });
        Assert.False(result.IsSuccess);
        Assert.Contains("dotyczy tylko --apply", result.Error, StringComparison.Ordinal);
    }

    [Fact]
    public void Parse_ExecuteAndApply_Fails()
    {
        var result = CliParser.Parse(new[] { "--execute", "x.y", "--apply", "x.z", "--out", "o.json" });
        Assert.False(result.IsSuccess);
        Assert.Contains("--execute", result.Error, StringComparison.Ordinal);
    }

    [Fact]
    public void Parse_ApplyWithoutRestorePoint_DefaultsToIfAvailable()
        => Assert.Equal(RestorePointPolicy.IfAvailable, CliParser.Parse(new[] { "--apply", "x.y", "--out", "o.json" }).Options!.RestorePointPolicy);

    [Theory]
    [InlineData("required", RestorePointPolicy.Required)]
    [InlineData("IfAvailable", RestorePointPolicy.IfAvailable)]
    [InlineData("SKIP", RestorePointPolicy.Skip)]
    public void Parse_RestorePointValues_AreCaseInsensitive(string value, RestorePointPolicy expected)
        => Assert.Equal(expected, CliParser.Parse(new[] { "--apply", "x.y", "--restore-point", value, "--out", "o.json" }).Options!.RestorePointPolicy);

    [Fact]
    public void Parse_Undo_Succeeds()
        => Assert.Equal(CliCommand.Undo, CliParser.Parse(new[] { "--undo", "x.y", "--out", "o.json" }).Options!.Command);

    [Theory]
    [InlineData(new string[0], "Podaj dokładnie jedno z")]
    [InlineData(new[] { "--out", "o.json" }, "Podaj dokładnie jedno z")]
    [InlineData(new[] { "--detect", "a.b", "--undo", "a.b", "--out", "o.json" }, "Podaj dokładnie jedno z")]
    [InlineData(new[] { "--detect", "a.b" }, "Brak wymaganej opcji --out")]
    [InlineData(new[] { "--detect", "a.b", "--out" }, "Opcja --out wymaga wartości")]
    [InlineData(new[] { "--detect", "--out", "o.json" }, "Opcja --detect wymaga wartości")]
    [InlineData(new[] { "--detect", "a.b", "--out", "o.json", "--out", "p.json" }, "Opcja --out podana więcej niż raz")]
    [InlineData(new[] { "--detect", "a.b", "--catalog", "a.json", "--catalog", "b.json", "--out", "o.json" }, "Opcja --catalog podana więcej niż raz")]
    [InlineData(new[] { "--apply", "a.b", "--restore-point", "skip", "--restore-point", "skip", "--out", "o.json" }, "Opcja --restore-point podana więcej niż raz")]
    [InlineData(new[] { "--detect", "a.b", "--foo", "x", "--out", "o.json" }, "Nieznana opcja '--foo'")]
    [InlineData(new[] { "luzem", "--detect", "a.b", "--out", "o.json" }, "Nieoczekiwany argument 'luzem'")]
    [InlineData(new[] { "--apply", "a.b", "--restore-point", "zawsze", "--out", "o.json" }, "Nieznana wartość --restore-point")]
    [InlineData(new[] { "--detect", "a.b", "--restore-point", "skip", "--out", "o.json" }, "--restore-point dotyczy tylko --apply")]
    public void Parse_Invalid_ReturnsError(string[] args, string fragment)
    {
        var result = CliParser.Parse(args);
        Assert.False(result.IsSuccess);
        Assert.Contains(fragment, result.Error, StringComparison.Ordinal);
    }

    [Fact]
    public void Parse_Invalid_ErrorEndsWithUsage()
        => Assert.EndsWith(" Składnia: " + CliParser.Usage, CliParser.Parse(Array.Empty<string>()).Error, StringComparison.Ordinal);

    [Fact]
    public void Parse_InvalidButOutGiven_KeepsOutPath()
        => Assert.Equal("o.json", CliParser.Parse(new[] { "--foo", "--out", "o.json" }).OutPath);

    [Fact]
    public void Parse_PostPassErrorWithOut_KeepsOutPath()
        => Assert.Equal("o.json", CliParser.Parse(new[] { "--out", "o.json" }).OutPath);

    [Fact]
    public void Parse_OutWithoutValue_OutPathIsNull()
        => Assert.Null(CliParser.Parse(new[] { "--detect", "a.b", "--out" }).OutPath);

    [Fact]
    public void Parse_TwoErrors_FirstOneWins()
    {
        var result = CliParser.Parse(new[] { "--foo", "--bar", "--out", "o.json" });
        Assert.Contains("Nieznana opcja '--foo'", result.Error, StringComparison.Ordinal);
        Assert.DoesNotContain("--bar", result.Error, StringComparison.Ordinal);
    }

    [Fact]
    public void Parse_ErrorBeforeOut_StillFindsOut()
        => Assert.Equal("o.json", CliParser.Parse(new[] { "luzem", "--detect", "a.b", "--out", "o.json" }).OutPath);

    [Fact]
    public void Parse_UnknownOption_DoesNotConsumeNextArgument()
    {
        var result = CliParser.Parse(new[] { "--foo", "--out", "o.json" });
        Assert.Contains("Nieznana opcja '--foo'", result.Error, StringComparison.Ordinal);
        Assert.Equal("o.json", result.OutPath);
    }

    [Fact]
    public void Parse_DuplicateOut_KeepsFirstValue()
        => Assert.Equal("o.json", CliParser.Parse(new[] { "--detect", "a.b", "--out", "o.json", "--out", "p.json" }).OutPath);

    [Fact]
    public void Parse_PassErrorSuppressesPostChecks()
    {
        // Brak polecenia i --restore-point przy braku --apply: raportowany ma być błąd z przebiegu.
        var result = CliParser.Parse(new[] { "--restore-point", "skip", "--foo", "--out", "o.json" });
        Assert.Contains("Nieznana opcja '--foo'", result.Error, StringComparison.Ordinal);
    }

    [Fact]
    public void Parse_PostChecks_CommandCountBeforeOut()
        => Assert.Contains("Podaj dokładnie jedno z", CliParser.Parse(new[] { "--restore-point", "skip" }).Error, StringComparison.Ordinal);

    [Fact]
    public void Parse_PostChecks_OutBeforeRestorePoint()
        => Assert.Contains("Brak wymaganej opcji --out", CliParser.Parse(new[] { "--detect", "a.b", "--restore-point", "skip" }).Error, StringComparison.Ordinal);

    [Fact]
    public void Parse_ListApps_TakesNoValue()
    {
        var result = CliParser.Parse(new[] { "--list-apps", "--out", @"C:\dso\out\l.json" });
        Assert.True(result.IsSuccess, result.Error);
        Assert.Equal(CliCommand.ListApps, result.Options!.Command);
        Assert.Equal(string.Empty, result.Options.TweakId);
        Assert.Equal(@"C:\dso\out\l.json", result.Options.OutPath);
    }

    [Fact]
    public void Parse_ListAppsAsLastArgument_Succeeds()
        => Assert.Equal(CliCommand.ListApps, CliParser.Parse(new[] { "--out", "o.json", "--LIST-APPS" }).Options!.Command);

    [Fact]
    public void Parse_ListAppsWithValue_ValueIsUnexpected()
    {
        var result = CliParser.Parse(new[] { "--list-apps", "x", "--out", "o.json" });
        Assert.False(result.IsSuccess);
        Assert.Contains("Nieoczekiwany argument 'x'", result.Error, StringComparison.Ordinal);
    }

    [Fact]
    public void Parse_RemoveApp_DefaultsToRequiredRestorePoint()
    {
        var result = CliParser.Parse(new[] { "--remove-app", @"win32:HKLM\SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall\DsoTestQuiet", "--out", "o.json" });
        Assert.True(result.IsSuccess, result.Error);
        Assert.Equal(CliCommand.RemoveApp, result.Options!.Command);
        Assert.Equal(@"win32:HKLM\SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall\DsoTestQuiet", result.Options.TweakId);
        Assert.Equal(RestorePointPolicy.Required, result.Options.RestorePointPolicy);
    }

    [Theory]
    [InlineData("required", RestorePointPolicy.Required)]
    [InlineData("ifavailable", RestorePointPolicy.IfAvailable)]
    [InlineData("skip", RestorePointPolicy.Skip)]
    public void Parse_RemoveAppWithRestorePoint_UsesGivenPolicy(string value, RestorePointPolicy expected)
        => Assert.Equal(expected, CliParser.Parse(new[] { "--remove-app", "appx:A_b", "--restore-point", value, "--out", "o.json" }).Options!.RestorePointPolicy);

    [Fact]
    public void Parse_RemoveAppIdWithSpaces_KeptAsOneValue()
        => Assert.Equal(@"win32:HKLM\SOFTWARE\X\Uninstall\Program z spacją",
            CliParser.Parse(new[] { "--remove-app", @"win32:HKLM\SOFTWARE\X\Uninstall\Program z spacją", "--out", "o.json" }).Options!.TweakId);

    [Theory]
    [InlineData(new[] { "--remove-app", "--out", "o.json" }, "Opcja --remove-app wymaga wartości")]
    [InlineData(new[] { "--list-apps", "--remove-app", "appx:A_b", "--out", "o.json" }, "Podaj dokładnie jedno z: --detect, --apply, --undo, --execute, --list-apps, --remove-app, --list-install, --install-apps, --search-apps.")]
    [InlineData(new[] { "--list-apps", "--list-apps", "--out", "o.json" }, "Podaj dokładnie jedno z")]
    [InlineData(new[] { "--list-apps", "--restore-point", "skip", "--out", "o.json" }, "Opcja --restore-point dotyczy tylko --apply i --remove-app.")]
    [InlineData(new[] { "--list-apps", "--catalog", "k.json", "--out", "o.json" }, "Opcja --catalog nie dotyczy --list-apps ani --remove-app.")]
    [InlineData(new[] { "--remove-app", "appx:A_b", "--catalog", "k.json", "--out", "o.json" }, "Opcja --catalog nie dotyczy --list-apps ani --remove-app.")]
    [InlineData(new[] { "--list-apps" }, "Brak wymaganej opcji --out")]
    public void Parse_AppsInvalid_ReturnsError(string[] args, string fragment)
    {
        var result = CliParser.Parse(args);
        Assert.False(result.IsSuccess);
        Assert.Contains(fragment, result.Error, StringComparison.Ordinal);
    }

    [Fact]
    public void Usage_MentionsAppCommands()
    {
        Assert.Contains("--list-apps", CliParser.Usage, StringComparison.Ordinal);
        Assert.Contains("--remove-app <id> [--restore-point required|ifavailable|skip]", CliParser.Usage, StringComparison.Ordinal);
    }
    [Fact]
    public void FindLanguage_En()
        => Assert.Equal(UiLanguage.English, CliParser.FindLanguage(new[] { "--detect", "a.b", "--LANG", "en", "--out", "o.json" }));

    [Fact]
    public void FindLanguage_Missing_Null()
        => Assert.Null(CliParser.FindLanguage(new[] { "--detect", "a.b", "--out", "o.json" }));

    [Theory]
    [InlineData("de")]
    [InlineData("EN")]
    [InlineData("")]
    public void FindLanguage_Invalid_Null(string value)
        => Assert.Null(CliParser.FindLanguage(new[] { "--lang", value, "--detect", "a.b" }));

    [Fact]
    public void FindLanguage_NoValue_Null()
        => Assert.Null(CliParser.FindLanguage(new[] { "--detect", "a.b", "--lang" }));

    [Fact]
    public void Parse_LangWithDetect_Succeeds()
    {
        var result = CliParser.Parse(new[] { "--detect", "a.b", "--lang", "en", "--out", "o.json" });
        Assert.True(result.IsSuccess);
        Assert.Equal(CliCommand.Detect, result.Options!.Command);
    }

    [Fact]
    public void Parse_UnknownLang_Error()
    {
        var result = CliParser.Parse(new[] { "--detect", "a.b", "--lang", "de", "--out", "o.json" });
        Assert.False(result.IsSuccess);
        Assert.Contains("Nieznany język 'de' w --lang (dozwolone: pl, en).", result.Error, StringComparison.Ordinal);
    }

    [Fact]
    public void Parse_LangTwice_Error()
    {
        var result = CliParser.Parse(new[] { "--detect", "a.b", "--lang", "en", "--lang", "pl", "--out", "o.json" });
        Assert.False(result.IsSuccess);
        Assert.Contains("Opcja --lang podana więcej niż raz.", result.Error, StringComparison.Ordinal);
    }

    [Fact]
    public void Parse_LangWithoutValue_Error()
        => Assert.Contains("Opcja --lang wymaga wartości.", CliParser.Parse(new[] { "--detect", "a.b", "--out", "o.json", "--lang" }).Error, StringComparison.Ordinal);

    [Fact]
    public void Parse_UnknownOption_English()
    {
        using (L.Use(UiLanguage.English))
        {
            var result = CliParser.Parse(new[] { "--foo", "--out", "o.json" });
            Assert.StartsWith("Unknown option '--foo'.", result.Error, StringComparison.Ordinal);
        }
    }

    [Fact]
    public void Usage_EndsWithLang()
    {
        Assert.EndsWith(" [--lang pl|en]", CliParser.Usage, StringComparison.Ordinal);
        using (L.Use(UiLanguage.English))
        {
            Assert.EndsWith(" [--lang pl|en]", CliParser.Usage, StringComparison.Ordinal);
            Assert.Contains("<file.json>", CliParser.Usage, StringComparison.Ordinal);
            Assert.Contains("<text>", CliParser.Usage, StringComparison.Ordinal);
        }
    }

    public static TheoryData<string[]> ErrorCases => new()
    {
        { new[] { "stray", "--out", "o.json" } },
        { new[] { "--foo", "--out", "o.json" } },
        { new[] { "--detect", "--out", "o.json" } },
        { new[] { "--detect", "a.b", "--out", "o.json", "--out", "p.json" } },
        { new[] { "--detect", "a.b", "--catalog", "k.json", "--catalog", "l.json", "--out", "o.json" } },
        { new[] { "--list-install", "--apps-catalog", "k.json", "--apps-catalog", "l.json", "--out", "o.json" } },
        { new[] { "--apply", "a.b", "--restore-point", "skip", "--restore-point", "skip", "--out", "o.json" } },
        { new[] { "--apply", "a.b", "--restore-point", "maybe", "--out", "o.json" } },
        { new[] { "--detect", "a.b", "--lang", "de", "--out", "o.json" } },
        { new[] { "--detect", "a.b", "--lang", "en", "--lang", "en", "--out", "o.json" } },
        { new[] { "--detect", "a.b", "--out", "o.json", "--lang" } },
        { new[] { "--restore-point", "skip", "--out", "o.json" } },
        { new[] { "--detect", "a.b", "--restore-point", "skip" } },
        { new[] { "--detect", "a.b", "--restore-point", "skip", "--out", "o.json" } },
        { new[] { "--list-apps", "--catalog", "k.json", "--out", "o.json" } },
        { new[] { "--list-install", "--catalog", "k.json", "--out", "o.json" } },
        { new[] { "--detect", "a.b", "--apps-catalog", "k.json", "--out", "o.json" } },
        { new[] { "--install-apps", "install.a,,install.b", "--out", "o.json" } },
        { new[] { "--install-apps", "winget:Abc…", "--out", "o.json" } },
        { new[] { "--install-apps", "zle", "--out", "o.json" } },
        { new[] { "--install-apps", "install.a,install.a", "--out", "o.json" } },
    };

    [Theory]
    [MemberData(nameof(ErrorCases))]
    public void ParserErrors_English_HaveNoPolishLetters(string[] args)
    {
        using (L.Use(UiLanguage.English))
        {
            var result = CliParser.Parse(args);
            Assert.False(result.IsSuccess);
            Assert.DoesNotContain(result.Error!, c => "ąćęłńóśźżĄĆĘŁŃÓŚŹŻ".IndexOf(c) >= 0);
        }
    }
}
