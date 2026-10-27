using System;
using DSOptimizer.Core.Install;
using Xunit;

namespace DSOptimizer.Tests.Unit.Install;

/// <summary>Teksty strony „Instalacja” i odmowy konta (Ruling R17).</summary>
public class InstallTextTests
{
    private const string Plain = "Instalacja działa tylko na koncie zalogowanego użytkownika: uruchom DSOptimizer na koncie dso.";

    [Fact]
    public void OtherAccount_WithoutProcessName_Plain()
    {
        Assert.Equal(Plain, InstallText.OtherAccount("dso"));
        Assert.Equal(Plain, InstallText.OtherAccount("dso", null));
        Assert.Equal(Plain, InstallText.OtherAccount("dso", " "));
    }

    [Theory]
    [InlineData("ADMIN_dso")]
    [InlineData("admin_Dawid")]
    public void OtherAccount_HiddenAdministratorProtectionAccount_Suffix(string processName)
    {
        Assert.Equal(
            "Instalacja działa tylko na koncie zalogowanego użytkownika: uruchom DSOptimizer na koncie dso"
                + " (Administrator Protection: program podniesiony działa na ukrytym koncie " + processName + ").",
            InstallText.OtherAccount("dso", processName));
    }

    [Theory]
    [InlineData("Administrator")]
    [InlineData("ADMIN")]
    [InlineData("dso_ADMIN_")]
    public void OtherAccount_OtherProcessAccount_NoSuffix(string processName)
    {
        Assert.Equal(Plain, InstallText.OtherAccount("dso", processName));
    }

    [Theory]
    [InlineData(33, 3, false, "Źródło: winget (Microsoft) · 33 aplikacje · zainstalowane: 3")]
    [InlineData(1, 0, false, "Źródło: winget (Microsoft) · 1 aplikacja · zainstalowane: 0")]
    [InlineData(25, 25, false, "Źródło: winget (Microsoft) · 25 aplikacji · zainstalowane: 25")]
    [InlineData(34, 0, true, "Źródła: winget (Microsoft) i GitHub (RustDesk) · 34 aplikacje · zainstalowane: 0")]
    [InlineData(1, 1, true, "Źródła: winget (Microsoft) i GitHub (RustDesk) · 1 aplikacja · zainstalowane: 1")]
    public void PageSummary_CountAndInstalled(int total, int installed, bool withGitHub, string expected)
    {
        Assert.Equal(expected, InstallText.PageSummary(total, installed, withGitHub));
    }

    /// <summary>M6 (review Tasku 5): winget nie działa, ale lista ma RustDesk z GitHuba – podtytuł nie mówi „niedostępne” o całej stronie.</summary>
    [Theory]
    [InlineData(34, 0, "Źródła: GitHub (RustDesk) · winget (Microsoft) niedostępny · 34 aplikacje · zainstalowane: 0")]
    [InlineData(4, 1, "Źródła: GitHub (RustDesk) · winget (Microsoft) niedostępny · 4 aplikacje · zainstalowane: 1")]
    public void PageWingetUnavailable_GitHubFirst(int total, int installed, string expected)
    {
        Assert.Equal(expected, InstallText.PageWingetUnavailable(total, installed));
        Assert.DoesNotContain("· niedostępne", InstallText.PageWingetUnavailable(total, installed), StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(0, "Kliknij dwukrotnie wiersz albo pole wyboru, żeby dodać do instalacji.")]
    [InlineData(1, "Zaznaczono 1 aplikację")]
    [InlineData(3, "Zaznaczono 3 aplikacje")]
    [InlineData(8, "Zaznaczono 8 aplikacji")]
    public void SelectionBarTitle_ByCount(int selected, string expected)
    {
        Assert.Equal(expected, InstallText.SelectionBarTitle(selected));
    }

    [Theory]
    [InlineData(0, false, false, "")]
    [InlineData(1, true, false, "Z 1 kategorii, pobierane z winget (Microsoft). Wymaga internetu.")]
    [InlineData(5, true, false, "Z 5 kategorii, pobierane z winget (Microsoft). Wymaga internetu.")]
    [InlineData(1, false, true, "Z 1 kategorii, pobierane z GitHuba RustDesk. Wymaga internetu.")]
    [InlineData(2, true, true, "Z 2 kategorii, pobierane z winget (Microsoft) i z GitHuba RustDesk. Wymaga internetu.")]
    public void SelectionBarDetails_ThreeVariants(int groups, bool winget, bool gitHub, string expected)
    {
        Assert.Equal(expected, InstallText.SelectionBarDetails(groups, winget, gitHub));
    }

    [Fact]
    public void SelectionBarDetails_GroupsWithoutSource_Throws()
        => Assert.Throws<ArgumentException>(() => InstallText.SelectionBarDetails(1, false, false));

    [Fact]
    public void GitHubTexts_ExactValues()
    {
        Assert.Equal("GitHub: rustdesk/rustdesk", InstallText.GitHubLabel("rustdesk/rustdesk"));
        // M4: GitHub i PURSLANE mówi już fakt „Źródło” i ostrzeżenie pozycji RustDesk (spec 4), a „Wymaga internetu.”
        // pada raz – w ostrzeżeniu winget przy serii mieszanej albo w wariancie samego RustDeska.
        Assert.Equal(
            "RustDesk zostanie pobrany i zainstalowany bez okna instalatora dopiero po sprawdzeniu skrótu SHA256 i podpisu wydawcy.",
            InstallText.SeriesWarningGitHub);
        Assert.Equal(InstallText.SeriesWarningGitHub + " Wymaga internetu.", InstallText.SeriesWarningGitHubOnly);
        Assert.Equal("Pokaż RustDesk", InstallText.ShowRustDeskLink);
        Assert.Equal("GitHub RustDesk (rustdesk/rustdesk), podpis PURSLANE", InstallText.GitHubSourceValue);
        Assert.Equal("Repozytorium GitHub", InstallText.GitHubRepositoryFact);
        Assert.Equal("RustDesk (z GitHuba) można zainstalować także bez winget.", InstallText.GitHubStillAvailable);
        Assert.Equal("Wszystkie", InstallText.AllCategoriesName);
    }

    [Fact]
    public void Counts_Negative_Throw()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => InstallText.SelectionBarTitle(-1));
        Assert.Throws<ArgumentOutOfRangeException>(() => InstallText.SelectionBarDetails(-1, true, false));
        Assert.Throws<ArgumentOutOfRangeException>(() => InstallText.PageSummary(-1, 0, false));
        Assert.Throws<ArgumentOutOfRangeException>(() => InstallText.PageSummary(1, -1, true));
        Assert.Throws<ArgumentOutOfRangeException>(() => InstallText.PageWingetUnavailable(-1, 0));
        Assert.Throws<ArgumentOutOfRangeException>(() => InstallText.PageWingetUnavailable(1, -1));
    }

    /// <summary>
    /// Okno serii: szczegół w osobnej linii, z wielkiej litery. Dziennik (M8): szczegół po „ – ” w środku zdania, więc bez
    /// podnoszenia litery (tekst zostaje jak w komunikacie). Komunikat bez prefiksu statusu przechodzi bez zmian w obu trybach.
    /// </summary>
    [Fact]
    public void ResultDetail_CapitalizeOnlyForSeparateLine()
    {
        var failed = new DSOptimizer.Core.Engine.TweakResult
        {
            TweakId = "install.vlc",
            Outcome = DSOptimizer.Core.Engine.TweakOutcome.Failed,
            Message = InstallText.SkippedNoNetwork,
        };
        var already = new DSOptimizer.Core.Engine.TweakResult
        {
            TweakId = "install.vlc",
            Outcome = DSOptimizer.Core.Engine.TweakOutcome.AlreadyInstalled,
            Message = InstallText.AlreadyInstalledBeforeRun,
        };

        Assert.Equal("Brak połączenia z internetem albo ze źródłem winget", InstallText.ResultDetail(failed));
        Assert.Equal("brak połączenia z internetem albo ze źródłem winget", InstallText.ResultDetail(failed, capitalize: false));
        Assert.Equal(InstallText.AlreadyInstalledBeforeRun, InstallText.ResultDetail(already, capitalize: false));
    }

    [Fact]
    public void InstallButton_CountInText()
    {
        Assert.Equal("Zainstaluj (0)", InstallText.InstallButton(0));
        Assert.Equal("Zainstaluj (12)", InstallText.InstallButton(12));
    }

    [Fact]
    public void SearchTexts_Verbatim()
    {
        Assert.Equal("Szukaj aplikacji, np. VLC", InstallText.SearchPlaceholder);
        Assert.Equal("Szukaj aplikacji na liście, np. VLC", InstallText.SearchPlaceholderListOnly);
        Assert.Equal("Szukaj też w winget", InstallText.SearchButton);
        Assert.Equal("Zaznaczone", InstallText.SelectedGroupName);
        Assert.Equal("Nic nie jest zaznaczone. Zaznacz aplikacje na liście albo w wynikach winget.", InstallText.SelectedEmpty);
        Assert.Equal("Z listy (3)", InstallText.CatalogSection(3));
        Assert.Equal("W winget (12)", InstallText.WingetSection(12));
        Assert.Equal("Pakiety spoza listy DSOptimizera – pobierane z oficjalnego źródła winget (Microsoft), bez wcześniejszego sprawdzenia przez autora programu.", InstallText.WingetSectionNote);
        Assert.Equal("Brak na liście. Naciśnij Enter, żeby poszukać w winget.", InstallText.NoCatalogMatch);
        Assert.Equal("Brak na liście. Winget nie działa – wyszukiwanie w winget niedostępne.", InstallText.NoCatalogMatchWingetDown);
        Assert.Equal("Brak na liście.", InstallText.NoCatalogMatchPlain);
        Assert.Equal("RustDesk instaluje się z listy: użyj install.rustdesk.", InstallText.CliGitHubAlias("RustDesk", "install.rustdesk"));
        Assert.Equal("VLC jest na liście – wyżej", InstallText.OnListNote("VLC"));
        Assert.Equal("RustDesk jest na liście – wyżej (instalowany z GitHuba)", InstallText.OnListNoteGitHub("RustDesk"));
        Assert.Equal("Znalezione aplikacje są na liście – wyżej.", InstallText.SearchAllOnList);
    }
}
