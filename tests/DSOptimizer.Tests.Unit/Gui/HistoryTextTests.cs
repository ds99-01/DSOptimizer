using System;
using DSOptimizer.Core.Gui;
using Xunit;

namespace DSOptimizer.Tests.Unit.Gui;

public class HistoryTextTests
{
    [Theory]
    [InlineData(HistoryState.Removed, "Usunięta")]
    [InlineData(HistoryState.RemovalNotConfirmed, "Nie potwierdzono usunięcia")]
    [InlineData(HistoryState.RemovalFailed, "Usuwanie nie powiodło się")]
    public void State_RemovalStates(HistoryState state, string expected)
        => Assert.Equal(expected, HistoryText.State(state));

    [Theory]
    [InlineData("Started", "Usuwanie rozpoczęte")]
    [InlineData("Removed", "Usunięto i sprawdzono")]
    [InlineData("NotConfirmed", "Nie potwierdzono usunięcia")]
    [InlineData("Failed", "Usuwanie nie powiodło się")]
    [InlineData("Cokolwiek", "remove-app: Cokolwiek")]
    public void Event_RemoveApp(string outcome, string expected)
        => Assert.Equal(expected, HistoryText.Event("remove-app", outcome, null));

    [Fact]
    public void Event_RemoveApp_AppendsMessage()
        => Assert.Equal("Nie potwierdzono usunięcia – powód", HistoryText.Event("remove-app", "NotConfirmed", "powód"));

    [Fact]
    public void AppsCategory_IsAplikacje()
        => Assert.Equal("Aplikacje", HistoryText.AppsCategory);

    [Theory]
    [InlineData(HistoryState.Installed, "Zainstalowana")]
    [InlineData(HistoryState.InstallNotConfirmed, "Nie potwierdzono instalacji")]
    [InlineData(HistoryState.InstallNotDone, "Nie zainstalowano")]
    public void State_InstallStates(HistoryState state, string expected)
        => Assert.Equal(expected, HistoryText.State(state));

    [Theory]
    [InlineData("Started", "Instalacja rozpoczęta")]
    [InlineData("Installed", "Zainstalowano i sprawdzono")]
    [InlineData("InstalledUnverified", "Zainstalowano (zgłoszone przez winget)")]
    [InlineData("AlreadyInstalled", "Była już zainstalowana")]
    [InlineData("NotConfirmed", "Nie potwierdzono instalacji")]
    [InlineData("Failed", "Nie wykonano")]
    [InlineData("Cokolwiek", "install-app: Cokolwiek")]
    public void InstallEvents(string outcome, string expected)
        => Assert.Equal(expected, HistoryText.Event("install-app", outcome, null));

    [Fact]
    public void InstallEvents_AppendsMessage()
        => Assert.Equal(
            "Zainstalowano i sprawdzono – winget: 7zip.7zip, wersja 26.05",
            HistoryText.Event("install-app", "Installed", "winget: 7zip.7zip, wersja 26.05"));

    /// <summary>Fala poprawek F3: wynik instalacji z powodem pokazuje powód zamiast źródła z <c>Message</c>.</summary>
    [Theory]
    [InlineData("Failed", "Nie wykonano – brak połączenia z GitHubem", "Nie wykonano – brak połączenia z GitHubem")]
    [InlineData("NotConfirmed", "Nie potwierdzono – instalacja nadal trwa w tle", "Nie potwierdzono instalacji – instalacja nadal trwa w tle")]
    [InlineData("Failed", "instalator został przerwany", "Nie wykonano – instalator został przerwany")]
    public void Event_InstallResultWithReason_ShowsShortReason(string outcome, string reason, string expected)
        => Assert.Equal(expected, HistoryText.Event("install-app", outcome, "github: rustdesk/rustdesk", reason));

    [Theory]
    [InlineData("install-app", "Failed", null, "Nie wykonano – github: rustdesk/rustdesk")]
    [InlineData("install-app", "Failed", "  ", "Nie wykonano – github: rustdesk/rustdesk")]
    [InlineData("install-app", "Started", "Nie wykonano – brak sieci", "Instalacja rozpoczęta – github: rustdesk/rustdesk")]
    [InlineData("remove-app", "Failed", "Nie wykonano – brak sieci", "Usuwanie nie powiodło się – github: rustdesk/rustdesk")]
    public void Event_WithoutReasonOrNotInstallResult_UsesMessage(string action, string outcome, string? reason, string expected)
        => Assert.Equal(expected, HistoryText.Event(action, outcome, "github: rustdesk/rustdesk", reason));

    [Fact]
    public void InstallTexts()
    {
        Assert.Equal("Instalacja", HistoryText.InstallCategory);
        Assert.Equal("Instalacji nie cofa się z programu – aplikację usuniesz na stronie „Aplikacje”.", HistoryText.InstallUndoBlocked);
    }

    [Theory]
    [InlineData("Nie wykonano – brak sieci", "brak sieci")]
    [InlineData("Nie potwierdzono – limit czasu", "limit czasu")]
    [InlineData("Coś innego", "Coś innego")]
    [InlineData(null, null)]
    [InlineData("  ", null)]
    public void ShortReason_CutsPrefix(string? text, string? expected)
        => Assert.Equal(expected, HistoryText.ShortReason(text));

    [Fact]
    public void StateWithReason_AppendsReasonOrNot()
    {
        Assert.Equal("Nie zainstalowano", HistoryText.StateWithReason(HistoryState.InstallNotDone, null));
        Assert.Equal("Nie zainstalowano – brak sieci", HistoryText.StateWithReason(HistoryState.InstallNotDone, "brak sieci"));
    }

    [Fact]
    public void KindLine_ThreeCases()
    {
        Assert.Equal("Instalacja aplikacji", HistoryText.KindLine(HistoryKind.AppInstall, "x"));
        Assert.Equal("Usunięcie aplikacji", HistoryText.KindLine(HistoryKind.AppRemoval, "x"));
        Assert.Equal("Prywatność i AI", HistoryText.KindLine(HistoryKind.Change, "Prywatność i AI"));
        Assert.Equal(HistoryText.OutOfCatalog, HistoryText.KindLine(HistoryKind.Change, null));
        Assert.Equal("Przejdź do: Aplikacje", HistoryText.GoToAppsLink);
    }
}

