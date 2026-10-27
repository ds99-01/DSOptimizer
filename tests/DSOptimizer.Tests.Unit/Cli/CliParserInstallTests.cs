using System;
using DSOptimizer.Core.Cli;
using Xunit;

namespace DSOptimizer.Tests.Unit.Cli;

/// <summary>Składnia <c>--list-install</c>/<c>--install-apps</c>/<c>--search-apps</c> (spec 5.5): każdy błąd = kod 2 bez uruchamiania czegokolwiek.</summary>
public class CliParserInstallTests
{
    private static CliParseResult Parse(params string[] args) => CliParser.Parse(args);

    private static void AssertUsageError(CliParseResult result, string fragment)
    {
        Assert.False(result.IsSuccess);
        Assert.Null(result.Options);
        Assert.Contains(fragment, result.Error, StringComparison.Ordinal);
        Assert.Contains("Składnia:", result.Error, StringComparison.Ordinal);
    }

    [Fact]
    public void ListInstall_WithOut_Succeeds()
    {
        var result = Parse("--list-install", "--out", @"C:\dso\out\l.json");

        Assert.True(result.IsSuccess);
        var options = result.Options!;
        Assert.Equal(CliCommand.ListInstall, options.Command);
        Assert.Equal(string.Empty, options.TweakId);
        Assert.Null(options.AppsCatalogPath);
        Assert.Null(options.CatalogPath);
        Assert.Empty(options.InstallIds);
        Assert.Null(options.SearchQuery);
        Assert.Equal(@"C:\dso\out\l.json", options.OutPath);
    }

    [Fact]
    public void ListInstall_WithAppsCatalog_Succeeds()
    {
        // --list-install nie ma wartości: następny argument to kolejna opcja, nie id.
        var result = Parse("--LIST-INSTALL", "--apps-catalog", @"C:\dso\fixtures\vm-test-apps.json", "--out", "o.json");

        Assert.True(result.IsSuccess);
        Assert.Equal(CliCommand.ListInstall, result.Options!.Command);
        Assert.Equal(@"C:\dso\fixtures\vm-test-apps.json", result.Options.AppsCatalogPath);
    }

    [Fact]
    public void InstallApps_IdList_KeepsInputOrder()
    {
        var result = Parse("--install-apps", "install.vlc,winget:Notepad++.Notepad++,install.7zip", "--apps-catalog", "a.json", "--out", "o.json");

        Assert.True(result.IsSuccess);
        var options = result.Options!;
        Assert.Equal(CliCommand.InstallApps, options.Command);
        Assert.Equal(new[] { "install.vlc", "winget:Notepad++.Notepad++", "install.7zip" }, options.InstallIds);
        Assert.Equal("a.json", options.AppsCatalogPath);
        Assert.Equal(string.Empty, options.TweakId);
    }

    [Fact]
    public void InstallApps_SingleSearchId_Succeeds()
    {
        var result = Parse("--install-apps", "winget:Microsoft.VCRedist.2015+.x64", "--out", "o.json");

        Assert.Equal(new[] { "winget:Microsoft.VCRedist.2015+.x64" }, result.Options!.InstallIds);
    }

    [Fact]
    public void SearchApps_QueryWithSpace_Succeeds()
    {
        var result = Parse("--search-apps", "notepad plus", "--out", "o.json");

        Assert.True(result.IsSuccess);
        Assert.Equal(CliCommand.SearchApps, result.Options!.Command);
        Assert.Equal("notepad plus", result.Options.SearchQuery);
        Assert.Empty(result.Options.InstallIds);
        Assert.Null(result.Options.AppsCatalogPath);
    }

    /// <summary>
    /// Review Focus 1: Id obcięte „…” w tabeli <c>winget search</c> (także puste) nie przechodzi jako <c>winget:&lt;id&gt;</c>, więc
    /// nic się nie uruchamia (kod 2), zamiast instalować pakiet zgadnięty z ucięcia.
    /// </summary>
    [Theory]
    [InlineData("winget:Microsoft.VisualStudioCode.Ins…")]
    [InlineData("install.7zip,winget:Vendor.LongApp…")]
    [InlineData("winget:…")]
    [InlineData("winget:")]
    public void WingetIdWithEllipsis_UsageError(string ids)
    {
        var result = Parse("--install-apps", ids, "--out", "o.json");

        AssertUsageError(result, "winget:");
        Assert.Contains("„…”", result.Error, StringComparison.Ordinal);
        Assert.Equal("o.json", result.OutPath);
    }

    [Theory]
    [InlineData("install.7zip,,install.vlc")]
    [InlineData("install.7zip,")]
    [InlineData(",install.7zip")]
    public void InstallApps_EmptyElement_UsageError(string ids)
        => AssertUsageError(Parse("--install-apps", ids, "--out", "o.json"), "pusty element");

    [Theory]
    [InlineData("install.7zip,install.vlc,install.7zip")]
    [InlineData("winget:Vendor.App,winget:vendor.app")]
    [InlineData("install.rustdesk,install.rustdesk")]
    public void InstallApps_Duplicate_UsageError(string ids)
        => AssertUsageError(Parse("--install-apps", ids, "--out", "o.json"), "więcej niż raz");

    [Theory]
    [InlineData("7zip")]
    [InlineData("install.")]
    [InlineData("Install.7zip")]
    [InlineData("install.7 zip")]
    [InlineData(" install.7zip")]
    [InlineData("winget:Vendor App")]
    [InlineData("appx:Microsoft.BingNews_8wekyb3d8bbwe")]
    public void InstallApps_InvalidId_UsageError(string ids)
        => AssertUsageError(Parse("--install-apps", ids, "--out", "o.json"), "Nieprawidłowy identyfikator aplikacji");

    [Theory]
    [InlineData("vlc \"player\"")]
    [InlineData("vlc\tplayer")]
    [InlineData("   ")]
    public void SearchApps_InvalidQuery_UsageError(string query)
        => AssertUsageError(Parse("--search-apps", query, "--out", "o.json"), "Nieprawidłowe zapytanie --search-apps");

    [Fact]
    public void SearchApps_QueryTooLong_UsageError()
        => AssertUsageError(Parse("--search-apps", new string('a', 101), "--out", "o.json"), "najwyżej 100");

    [Theory]
    [InlineData("--list-install", null, "--catalog", "k.json", "--catalog nie dotyczy")]
    [InlineData("--install-apps", "install.7zip", "--catalog", "k.json", "--catalog nie dotyczy")]
    [InlineData("--search-apps", "vlc", "--catalog", "k.json", "--catalog nie dotyczy")]
    [InlineData("--install-apps", "install.7zip", "--restore-point", "skip", "--restore-point dotyczy tylko")]
    [InlineData("--list-install", null, "--restore-point", "skip", "--restore-point dotyczy tylko")]
    [InlineData("--search-apps", "vlc", "--apps-catalog", "a.json", "--apps-catalog dotyczy tylko")]
    [InlineData("--detect", "x.y", "--apps-catalog", "a.json", "--apps-catalog dotyczy tylko")]
    [InlineData("--list-apps", null, "--apps-catalog", "a.json", "--apps-catalog dotyczy tylko")]
    [InlineData("--remove-app", "appx:x", "--apps-catalog", "a.json", "--apps-catalog dotyczy tylko")]
    public void OptionNotAllowedForCommand_UsageError(string command, string? value, string option, string optionValue, string fragment)
    {
        var args = value is null
            ? new[] { command, option, optionValue, "--out", "o.json" }
            : new[] { command, value, option, optionValue, "--out", "o.json" };

        AssertUsageError(Parse(args), fragment);
    }

    [Fact]
    public void AppsCatalogTwice_UsageError()
        => AssertUsageError(Parse("--list-install", "--apps-catalog", "a.json", "--apps-catalog", "b.json", "--out", "o.json"), "więcej niż raz");

    [Fact]
    public void TwoInstallCommands_UsageError()
        => AssertUsageError(Parse("--list-install", "--install-apps", "install.7zip", "--out", "o.json"), "Podaj dokładnie jedno z");

    [Fact]
    public void InstallAppsWithoutValue_UsageError()
        => AssertUsageError(Parse("--install-apps", "--out", "o.json"), "--install-apps wymaga wartości");

    [Fact]
    public void ListInstallWithoutOut_UsageError()
        => AssertUsageError(Parse("--list-install"), "Brak wymaganej opcji --out");

    [Fact]
    public void Usage_ListsInstallCommands()
    {
        Assert.Contains("--list-install [--apps-catalog <plik.json>]", CliParser.Usage, StringComparison.Ordinal);
        Assert.Contains("--install-apps <id>[,<id>…] [--apps-catalog <plik.json>]", CliParser.Usage, StringComparison.Ordinal);
        Assert.Contains("--search-apps <tekst>", CliParser.Usage, StringComparison.Ordinal);
    }

    [Fact]
    public void TweakCommands_HaveNoInstallFields()
    {
        var options = Parse("--detect", "x.y", "--out", "o.json").Options!;

        Assert.Empty(options.InstallIds);
        Assert.Null(options.AppsCatalogPath);
        Assert.Null(options.SearchQuery);
    }
}
