using System;
using DSOptimizer.Core.Apps;
using DSOptimizer.Core.Engine;
using Xunit;

namespace DSOptimizer.Tests.Unit.Apps;

public class AppsPageTextTests
{
    [Theory]
    [InlineData(0, "Kliknij dwukrotnie wiersz albo pole wyboru, żeby dodać do usunięcia.")]
    [InlineData(1, "Zaznaczono 1 aplikację do usunięcia")]
    [InlineData(3, "Zaznaczono 3 aplikacje do usunięcia")]
    [InlineData(6, "Zaznaczono 6 aplikacji do usunięcia")]
    [InlineData(22, "Zaznaczono 22 aplikacje do usunięcia")]
    public void SelectionBarTitle_CountsApps(int selected, string expected)
        => Assert.Equal(expected, AppsPageText.SelectionBarTitle(selected));

    [Fact]
    public void SelectionBarTitle_Negative_Throws()
        => Assert.Throws<ArgumentOutOfRangeException>(() => AppsPageText.SelectionBarTitle(-1));

    [Theory]
    [InlineData(true, false, "Przed usunięciem programów program utworzy punkt przywracania. Usunięcia nie da się cofnąć.")]
    [InlineData(true, true, "Przed usunięciem programów program utworzy punkt przywracania. Usunięcia nie da się cofnąć.")]
    [InlineData(false, true, "Przed usunięciem program spróbuje utworzyć punkt przywracania. Usunięcia nie da się cofnąć.")]
    [InlineData(false, false, "Bez punktu przywracania. Usunięcia nie da się cofnąć.")]
    public void SelectionBarDetails_RestorePointRule(bool hasProgram, bool setting, string expected)
        => Assert.Equal(expected, AppsPageText.SelectionBarDetails(hasProgram, setting));

    [Theory]
    [InlineData(1, "Odczyt listy: 14:05, 1 aplikacja")]
    [InlineData(4, "Odczyt listy: 14:05, 4 aplikacje")]
    [InlineData(21, "Odczyt listy: 14:05, 21 aplikacji")]
    public void ReadSummary_TimeAndCount(int count, string expected)
        => Assert.Equal(expected, AppsPageText.ReadSummary(new DateTime(2026, 10, 21, 14, 5, 30), count));

    [Theory]
    [InlineData(AppGroup.Suggested, "Sugerowane do usunięcia")]
    [InlineData(AppGroup.Other, "Pozostałe")]
    [InlineData(AppGroup.Protected, "Chronione")]
    public void GroupName_Polish(AppGroup group, string expected)
        => Assert.Equal(expected, AppsPageText.GroupName(group));

    [Theory]
    [InlineData(AppGroup.Suggested, "trial", "Sugerowane do usunięcia (wersja próbna)")]
    [InlineData(AppGroup.Suggested, "microsoft", "Sugerowane do usunięcia (Microsoft)")]
    [InlineData(AppGroup.Other, "producent", "Pozostałe (producent komputera)")]
    [InlineData(AppGroup.Other, null, "Pozostałe")]
    [InlineData(AppGroup.Other, "inna", "Pozostałe")]
    [InlineData(AppGroup.Protected, null, "Chronione")]
    public void GroupDetail_AddsBloatGroup(AppGroup group, string? bloatGroup, string expected)
        => Assert.Equal(expected, AppsPageText.GroupDetail(group, bloatGroup));

    [Theory]
    [InlineData(AppKind.Store, "Microsoft Corporation", "Aplikacja ze Sklepu, Microsoft Corporation")]
    [InlineData(AppKind.Program, "McAfee, LLC", "Program, McAfee, LLC")]
    [InlineData(AppKind.Program, null, "Program")]
    [InlineData(AppKind.Store, " ", "Aplikacja ze Sklepu")]
    public void Summary_KindAndPublisher(AppKind kind, string? publisher, string expected)
        => Assert.Equal(expected, AppsPageText.Summary(kind, publisher));

    [Theory]
    [InlineData(AppKind.Store, "Sklep", "Aplikacja ze Sklepu")]
    [InlineData(AppKind.Program, "Program", "Program")]
    public void KindNames(AppKind kind, string shortName, string longName)
    {
        Assert.Equal(shortName, AppsPageText.KindShort(kind));
        Assert.Equal(longName, AppsPageText.KindLong(kind));
    }

    [Theory]
    [InlineData(UninstallMode.Interactive, "Deinstalator producenta; jego okno pojawi się na ekranie, program poczeka na koniec.", "Deinstalator producenta; jego okno pojawi się na ekranie")]
    [InlineData(UninstallMode.Quiet, "Cichy deinstalator producenta, bez okna; program poczeka na koniec.", "Cichy deinstalator producenta, bez okna")]
    [InlineData(UninstallMode.Msi, "Instalator Windows usunie program bez okna; program poczeka na koniec.", "Instalator Windows, bez okna")]
    public void ProgramRemoval_ByMode(UninstallMode mode, string note, string shortNote)
    {
        Assert.Equal(note, AppsPageText.ProgramRemoval(mode));
        Assert.Equal(shortNote, AppsPageText.ProgramRemovalBrief(mode));
    }

    [Theory]
    [InlineData("Dawid", true, false, "Dla konta Dawid i z obrazu systemu, żeby nie wróciła na nowym koncie.")]
    [InlineData("Dawid", false, false, "Dla konta Dawid.")]
    [InlineData(null, true, false, "Dla zalogowanego konta i z obrazu systemu, żeby nie wróciła na nowym koncie.")]
    [InlineData(null, false, false, "Dla zalogowanego konta.")]
    [InlineData("Dawid", false, true, "Dla wszystkich kont na tym komputerze i z obrazu systemu.")]
    public void StoreRemoval_ByScope(string? userName, bool fromImage, bool allUsers, string expected)
        => Assert.Equal(expected, AppsPageText.StoreRemoval(userName, fromImage, allUsers));

    [Fact]
    public void RunsAsOtherAccount_R8Literal()
        => Assert.Equal(
            "DSOptimizer działa na koncie Administrator, innym niż zalogowane (Dawid): aplikacje ze Sklepu zostaną usunięte dla wszystkich kont na tym komputerze i z obrazu systemu.",
            AppsPageText.RunsAsOtherAccount("Administrator", "Dawid"));

    [Theory]
    [InlineData(@"C:\Program Files\McAfee\uninstall.exe", "", "\"C:\\Program Files\\McAfee\\uninstall.exe\"")]
    [InlineData(@"C:\Windows\System32\msiexec.exe", "/x {1} /qn /norestart", "\"C:\\Windows\\System32\\msiexec.exe\" /x {1} /qn /norestart")]
    public void CommandLine_QuotesFile(string file, string arguments, string expected)
        => Assert.Equal(expected, AppsPageText.CommandLine(new UninstallPlan(file, arguments, UninstallMode.Quiet)));

    [Theory]
    [InlineData(1, "1 aplikacja dokończy usuwanie po ponownym uruchomieniu komputera.")]
    [InlineData(2, "2 aplikacje dokończą usuwanie po ponownym uruchomieniu komputera.")]
    [InlineData(12, "12 aplikacji dokończy usuwanie po ponownym uruchomieniu komputera.")]
    [InlineData(22, "22 aplikacje dokończą usuwanie po ponownym uruchomieniu komputera.")]
    public void RemovalRebootText_Plural(int count, string expected)
        => Assert.Equal(expected, AppsPageText.RemovalRebootText(count));

    [Theory]
    [InlineData(TweakOutcome.Removed, "Usunięto")]
    [InlineData(TweakOutcome.NotConfirmed, "Nie potwierdzono")]
    [InlineData(TweakOutcome.Skipped, "Pominięto")]
    [InlineData(TweakOutcome.Refused, "Odmowa")]
    [InlineData(TweakOutcome.Failed, "Nie udało się")]
    public void OutcomeName_Removal(TweakOutcome outcome, string expected)
        => Assert.Equal(expected, AppsPageText.OutcomeName(outcome));

    [Theory]
    [InlineData("Nie udało się – aplikacja nadal jest na komputerze.", "Aplikacja nadal jest na komputerze.")]
    [InlineData("Nie udało się zapisać journala – x", "Nie udało się zapisać journala – x")]
    [InlineData("Nieoczekiwany błąd: x", "Nieoczekiwany błąd: x")]
    public void FailureDetail_DropsRepeatedPrefix(string message, string expected)
        => Assert.Equal(expected, AppsPageText.FailureDetail(message));
}
