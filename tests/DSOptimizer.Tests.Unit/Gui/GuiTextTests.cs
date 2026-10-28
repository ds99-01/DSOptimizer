using System;
using System.Linq;
using DSOptimizer.Core.Engine;
using DSOptimizer.Core.Gui;
using DSOptimizer.Core.Model;
using Xunit;

namespace DSOptimizer.Tests.Unit.Gui;

public class GuiTextTests
{
    [Fact]
    public void OutcomeName_CoversEveryValue_NonEmptyAndDistinct()
    {
        var names = Enum.GetValues(typeof(TweakOutcome)).Cast<TweakOutcome>().Select(GuiText.OutcomeName).ToList();
        Assert.All(names, name => Assert.False(string.IsNullOrWhiteSpace(name)));
        Assert.Equal(names.Count, names.Distinct().Count());
    }

    [Fact]
    public void OutcomeName_PinnedSamples()
    {
        Assert.Equal("Zastosowano – wymaga restartu", GuiText.OutcomeName(TweakOutcome.PendingReboot));
        Assert.Equal("Cofnięto do wartości domyślnych", GuiText.OutcomeName(TweakOutcome.UndoneFromDefaults));
        Assert.Equal("Usunięto", GuiText.OutcomeName(TweakOutcome.Removed));
        Assert.Equal("Nie potwierdzono", GuiText.OutcomeName(TweakOutcome.NotConfirmed));
    }

    [Fact]
    public void StateName_CoversEveryValue_NonEmptyAndDistinct_NullIsDash()
    {
        var names = Enum.GetValues(typeof(DetectionState)).Cast<DetectionState>().Select(value => GuiText.StateName(value)).ToList();
        Assert.All(names, name => Assert.False(string.IsNullOrWhiteSpace(name)));
        Assert.Equal(names.Count, names.Distinct().Count());
        Assert.Equal("—", GuiText.StateName(null));
    }

    [Fact]
    public void RiskName_PinnedValues()
    {
        Assert.Equal("bezpieczne", GuiText.RiskName(RiskLevel.Safe));
        Assert.Equal("ostrożnie", GuiText.RiskName(RiskLevel.Caution));
        Assert.Equal("eksperymentalne", GuiText.RiskName(RiskLevel.Experimental));
    }

    [Fact]
    public void OperationName_PinnedValues()
    {
        Assert.Equal("Zastosuj", GuiText.OperationName(GuiOperation.Apply));
        Assert.Equal("Cofnij", GuiText.OperationName(GuiOperation.Undo));
        Assert.Equal("Uruchom", GuiText.OperationName(GuiOperation.Execute));
        Assert.Equal("Usuń", GuiText.OperationName(GuiOperation.RemoveApps));
        Assert.Equal("Zainstaluj", GuiText.OperationName(GuiOperation.InstallApps));
    }

    [Fact]
    public void ItemRowName_TitleAndSubtitle()
    {
        Assert.Equal(
            "Wyłącz opóźnienie menu, Zastosowane. Wymaga ponownego uruchomienia.",
            GuiText.ItemRowName("Wyłącz opóźnienie menu", "Zastosowane. Wymaga ponownego uruchomienia."));
    }

    [Fact]
    public void ItemRowHelp_AddsSuggestedMarkerOnlyForSuggested()
    {
        Assert.Equal("Opis.", GuiText.ItemRowHelp("Opis.", false));
        Assert.Equal("Opis. Sugerowane.", GuiText.ItemRowHelp("Opis.", true));
        Assert.Equal("Sugerowane.", GuiText.ItemRowHelp("", true));
    }

    [Fact]
    public void ItemRowSubtitle_NoPending_StateOnly()
        => Assert.Equal("Zastosowane", GuiText.ItemRowSubtitle("Zastosowane", null));

    [Fact]
    public void ItemRowSubtitle_Applied_AddsWaiting()
        => Assert.Equal("Zastosowane. Czeka na ponowne uruchomienie.", GuiText.ItemRowSubtitle("Zastosowane", RebootPendingKind.Applied));

    [Fact]
    public void ItemRowSubtitle_Undone_ReplacesState()
        => Assert.Equal("Cofnięta. Czeka na ponowne uruchomienie.", GuiText.ItemRowSubtitle("Zastosowane", RebootPendingKind.Undone));

    [Fact]
    public void ItemNotFound_NamesOperationAndId()
    {
        Assert.Equal(
            "Cofnij: nie ma pozycji „net.old-tweak” w katalogu – nic nie zmieniono.",
            GuiText.ItemNotFound(GuiOperation.Undo, "net.old-tweak"));
    }

    [Theory]
    [InlineData("privacy", "Prywatność i AI")]
    [InlineData("perf", "Wydajność")]
    [InlineData("disk", "Dysk i system plików")]
    [InlineData("net", "Sieć i LAN")]
    [InlineData("sec", "Bezpieczeństwo i szyfrowanie")]
    [InlineData("ux", "Explorer i wygoda")]
    [InlineData("panels", "Panele systemowe")]
    [InlineData("repair", "Naprawy")]
    [InlineData("tools", "Narzędzia")]
    [InlineData("xyz", "xyz")]
    public void CategoryName_KnownAndUnknown(string id, string expected)
        => Assert.Equal(expected, GuiText.CategoryName(id));

    [Fact]
    public void SuggestedTooltip_ExplainsAutoSelectionAndUndo()
        => Assert.Equal(
            "Sugerowane. Przy pierwszym wejściu na stronę program sam zaznacza tę pozycję, jeśli nie jest jeszcze zastosowana. Możesz ją odznaczyć.",
            GuiText.SuggestedTooltip);

    [Fact]
    public void CategoryOrder_IsRoadmapOrder()
        => Assert.Equal(new[] { "privacy", "perf", "disk", "net", "sec", "ux", "panels", "repair", "tools" }, GuiText.CategoryOrder);

    [Theory]
    [InlineData(0, 0, "0 błędów, 0 ostrzeżeń")]
    [InlineData(1, 1, "1 błąd, 1 ostrzeżenie")]
    [InlineData(2, 3, "2 błędy, 3 ostrzeżenia")]
    [InlineData(4, 4, "4 błędy, 4 ostrzeżenia")]
    [InlineData(5, 7, "5 błędów, 7 ostrzeżeń")]
    [InlineData(11, 12, "11 błędów, 12 ostrzeżeń")]
    [InlineData(13, 14, "13 błędów, 14 ostrzeżeń")]
    [InlineData(21, 22, "21 błędów, 22 ostrzeżenia")]
    [InlineData(112, 104, "112 błędów, 104 ostrzeżenia")]
    [InlineData(1000, 1001, "1000 błędów, 1001 ostrzeżeń")]
    public void LogSummary_PolishPlurals(int errors, int warnings, string expected)
        => Assert.Equal(expected, GuiText.LogSummary(errors, warnings));

    [Fact]
    public void LogSummary_NegativeCount_Throws()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => GuiText.LogSummary(-1, 0));
        Assert.Throws<ArgumentOutOfRangeException>(() => GuiText.LogSummary(0, -1));
    }

    [Fact]
    public void UndefinedEnumValues_Throw()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => GuiText.RiskName((RiskLevel)99));
        Assert.Throws<ArgumentOutOfRangeException>(() => GuiText.StateName((DetectionState)99));
        Assert.Throws<ArgumentOutOfRangeException>(() => GuiText.OutcomeName((TweakOutcome)99));
        Assert.Throws<ArgumentOutOfRangeException>(() => GuiText.OperationName((GuiOperation)99));
    }

    [Fact]
    public void CategoryCounter_TweaksShowAppliedOverTweaks_OtherwiseTotal()
    {
        Assert.Equal("1/4", GuiText.CategoryCounter(1, 4, 6));
        Assert.Equal("15", GuiText.CategoryCounter(0, 0, 15));
    }

    [Theory]
    [InlineData(0, 13, 0, 0, "0 z 13 ustawień zastosowanych")]
    [InlineData(1, 4, 2, 0, "1 z 4 ustawień zastosowanych")]
    [InlineData(1, 1, 0, 0, "1 z 1 ustawienia zastosowanych")]
    [InlineData(0, 0, 0, 15, "15 paneli")]
    [InlineData(0, 0, 0, 1, "1 panel")]
    [InlineData(0, 0, 0, 3, "3 panele")]
    [InlineData(0, 0, 5, 0, "5 akcji")]
    [InlineData(0, 0, 2, 0, "2 akcje")]
    [InlineData(0, 0, 1, 1, "2 pozycje")]
    [InlineData(0, 0, 4, 8, "12 pozycji")]
    public void CategoryCounterDescription_FullText(int applied, int tweaks, int actions, int panels, string expected)
    {
        Assert.Equal(expected, GuiText.CategoryCounterDescription(applied, tweaks, actions, panels));
    }

    [Theory]
    [InlineData(1, "1 aplikacja")]
    [InlineData(34, "34 aplikacje")]
    [InlineData(35, "35 aplikacji")]
    [InlineData(12, "12 aplikacji")]
    public void AppsCounterDescription_Plural(int count, string expected)
    {
        Assert.Equal(expected, GuiText.AppsCounterDescription(count));
    }

    [Theory]
    [InlineData(1, "1 aplikacja na liście")]
    [InlineData(35, "35 aplikacji na liście")]
    [InlineData(22, "22 aplikacje na liście")]
    public void InstallCounterDescription_Plural(int count, string expected)
    {
        Assert.Equal(expected, GuiText.InstallCounterDescription(count));
    }

    [Theory]
    [InlineData(0, "0 aktywnych zmian")]
    [InlineData(1, "1 aktywna zmiana")]
    [InlineData(3, "3 aktywne zmiany")]
    [InlineData(14, "14 aktywnych zmian")]
    public void HistoryCounterDescription_Plural(int count, string expected)
    {
        Assert.Equal(expected, GuiText.HistoryCounterDescription(count));
    }

    [Theory]
    [InlineData(RiskLevel.Safe, "Ryzyko: bezpieczne")]
    [InlineData(RiskLevel.Caution, "Ryzyko: ostrożnie")]
    [InlineData(RiskLevel.Experimental, "Ryzyko: eksperymentalne")]
    public void RiskToolTip_PrefixedRiskName(RiskLevel risk, string expected)
    {
        Assert.Equal(expected, GuiText.RiskToolTip(risk));
    }

    [Theory]
    [InlineData("Sieć i LAN", "1/4", "Sieć i LAN – 1/4")]
    [InlineData("Panele systemowe", "15", "Panele systemowe – 15")]
    public void CategoryNavToolTip_NameDashCounter(string name, string counter, string expected)
    {
        Assert.Equal(expected, GuiText.CategoryNavToolTip(name, counter));
    }

    [Theory]
    [InlineData(1, 1, "1 pozycja w 1 kategorii")]
    [InlineData(3, 2, "3 pozycje w 2 kategoriach")]
    [InlineData(5, 3, "5 pozycji w 3 kategoriach")]
    [InlineData(22, 2, "22 pozycje w 2 kategoriach")]
    [InlineData(0, 0, "Brak wyników")]
    public void SearchSummary_PolishPlurals(int items, int categories, string expected)
        => Assert.Equal(expected, GuiText.SearchSummary(items, categories));

    [Fact]
    public void RiskFilterName_CoversEveryValue()
    {
        Assert.Equal("Wszystkie", GuiText.RiskFilterName(RiskFilter.All));
        Assert.Equal("Bez eksperymentalnych", GuiText.RiskFilterName(RiskFilter.NoExperimental));
        Assert.Equal("Tylko bezpieczne", GuiText.RiskFilterName(RiskFilter.SafeOnly));
        Assert.Throws<ArgumentOutOfRangeException>(() => GuiText.RiskFilterName((RiskFilter)99));
    }

    [Fact]
    public void FilterNote_CoversFourCases()
    {
        Assert.Null(GuiText.FilterNote(0, 0));
        Assert.Equal("Ukryte przez filtr: 3", GuiText.FilterNote(3, 0));
        Assert.Equal("Zastosowane pozycje widoczne mimo filtra: 2", GuiText.FilterNote(0, 2));
        Assert.Equal("Ukryte przez filtr: 3. Zastosowane pozycje widoczne mimo filtra: 2", GuiText.FilterNote(3, 2));
    }

    [Fact]
    public void FilterAndSearchTexts()
    {
        Assert.Equal("Nic nie pasuje do „xyz”.", GuiText.NoSearchResults(" xyz "));
        Assert.Equal("Wyniki wyszukiwania", GuiText.SearchTitle);
        Assert.Equal("Szukaj we wszystkich kategoriach", GuiText.SearchPlaceholder);
    }

    [Fact]
    public void SelectionBar_EmptyText_MentionsDoubleClick()
        => Assert.Equal("Kliknij dwukrotnie wiersz albo pole wyboru, żeby dodać do zmiany.", GuiText.SelectionBarTitle(0, 0, 0));

    [Theory]
    [InlineData(1, "Zaznaczono 1 pozycję")]
    [InlineData(3, "Zaznaczono 3 pozycje")]
    [InlineData(5, "Zaznaczono 5 pozycji")]
    [InlineData(22, "Zaznaczono 22 pozycje")]
    public void SelectionBarTitle_Plurals(int selected, string expected)
        => Assert.Equal(expected, GuiText.SelectionBarTitle(selected, selected, 0));

    // Zaznaczenie = „dodaj do serii”; o skutku decyduje przycisk. Pasek mówi wprost, ile trafi do „Zastosuj”, a ile do „Cofnij”.
    [Theory]
    [InlineData(3, 2, 1, "Zaznaczono 3 pozycje: 2 do zastosowania, 1 do cofnięcia")]
    [InlineData(2, 0, 2, "Zaznaczono 2 pozycje: 2 do cofnięcia")]
    [InlineData(1, 1, 1, "Zaznaczono 1 pozycję: 1 do zastosowania, 1 do cofnięcia")]
    [InlineData(2, 0, 0, "Zaznaczono 2 pozycje")]
    public void SelectionBarTitle_SplitsApplyAndUndo(int selected, int toApply, int toUndo, string expected)
        => Assert.Equal(expected, GuiText.SelectionBarTitle(selected, toApply, toUndo));

    [Theory]
    [InlineData(0, true, true, "Przed zastosowaniem program spróbuje utworzyć punkt przywracania.")]
    [InlineData(1, true, true, "1 wymaga restartu. Przed zastosowaniem program spróbuje utworzyć punkt przywracania.")]
    [InlineData(2, false, true, "2 wymagają restartu.")]
    [InlineData(5, true, false, "5 wymaga restartu. Bez punktu przywracania.")]
    [InlineData(0, true, false, "Bez punktu przywracania.")]
    [InlineData(0, false, true, "")]
    public void SelectionBarDetails_Table(int rebootCount, bool hasApplicable, bool restorePoint, string expected)
        => Assert.Equal(expected, GuiText.SelectionBarDetails(rebootCount, hasApplicable, restorePoint));

    [Fact]
    public void BatchRestorePointNotice_Texts()
    {
        Assert.Equal("Przed zmianami zostanie utworzony punkt przywracania, jeśli ochrona systemu jest włączona.", GuiText.BatchRestorePointNotice(true));
        Assert.Equal("Punkt przywracania nie zostanie utworzony (wyłączony w Ustawieniach).", GuiText.BatchRestorePointNotice(false));
    }

    [Theory]
    [InlineData(true, true, false, "W serii – trafi do „Zastosuj”")]
    [InlineData(true, false, true, "W serii – już zastosowana, trafi do „Cofnij”")]
    [InlineData(true, true, true, "W serii – możesz ją zastosować albo cofnąć")]
    [InlineData(true, false, false, "W serii – teraz nie da się jej zastosować ani cofnąć")]
    [InlineData(false, true, false, "Nie w serii – kliknij dwukrotnie wiersz, żeby dodać")]
    [InlineData(false, false, true, "Nie w serii – kliknij dwukrotnie wiersz, żeby dodać")]
    public void SeriesLine_Values(bool inSeries, bool canApply, bool canUndo, string expected)
        => Assert.Equal(expected, GuiText.SeriesLine(inSeries, canApply, canUndo));

    [Theory]
    [InlineData(true, false, "Zaznacz, żeby zastosować w serii (przycisk „Zastosuj”)")]
    [InlineData(false, true, "Już zastosowana – zaznacz, żeby cofnąć w serii (przycisk „Cofnij”)")]
    [InlineData(true, true, "Zaznacz, żeby zastosować albo cofnąć w serii")]
    [InlineData(false, false, "Teraz nie da się jej zastosować ani cofnąć")]
    public void SelectHint_Values(bool canApply, bool canUndo, string expected)
        => Assert.Equal(expected, GuiText.SelectHint(canApply, canUndo));

    [Fact]
    public void CancelledBatch_UpToThreeTitles()
    {
        Assert.Equal("Anulowano serię: Zastosuj – 1 pozycja (A).", GuiText.CancelledBatch(GuiOperation.Apply, new[] { "A" }));
        Assert.Equal("Anulowano serię: Cofnij – 3 pozycje (A; B; C).", GuiText.CancelledBatch(GuiOperation.Undo, new[] { "A", "B", "C" }));
    }

    [Fact]
    public void CancelledBatch_TitleWithSemicolon_KeptVerbatim()
        => Assert.Equal("Anulowano serię: Zastosuj – 2 pozycje (A;B; C).", GuiText.CancelledBatch(GuiOperation.Apply, new[] { "A;B", "C" }));

    [Theory]
    [InlineData(4, "Anulowano serię: Zastosuj – 4 pozycje (A; B; C i 1 inna).")]
    [InlineData(5, "Anulowano serię: Zastosuj – 5 pozycji (A; B; C i 2 inne).")]
    [InlineData(8, "Anulowano serię: Zastosuj – 8 pozycji (A; B; C i 5 innych).")]
    [InlineData(36, "Anulowano serię: Zastosuj – 36 pozycji (A; B; C i 33 inne).")]
    public void CancelledBatch_MoreThanThree(int count, string expected)
    {
        var titles = new[] { "A", "B", "C" }.Concat(Enumerable.Range(0, count - 3).Select(i => "X" + i)).ToList();
        Assert.Equal(expected, GuiText.CancelledBatch(GuiOperation.Apply, titles));
    }

    [Fact]
    public void CancelledBatch_EmptyOrNull_Throws()
    {
        Assert.Throws<ArgumentException>(() => GuiText.CancelledBatch(GuiOperation.Apply, Array.Empty<string>()));
        Assert.Throws<ArgumentNullException>(() => GuiText.CancelledBatch(GuiOperation.Apply, null!));
    }

    [Theory]
    [InlineData(" xyz ", 0, "Nic nie pasuje do „xyz”.")]
    [InlineData("xyz", 2, "Pasujące pozycje ukrył filtr ryzyka.")]
    public void EmptySearchText_Table(string query, int hiddenCount, string expected)
        => Assert.Equal(expected, GuiText.EmptySearchText(query, hiddenCount));

    [Fact]
    public void UrlCopied_Text()
        => Assert.Equal("Skopiowano adres: https://github.com/ds99-01/DSOptimizer", GuiText.UrlCopied("https://github.com/ds99-01/DSOptimizer"));

    [Fact]
    public void OpenUrlFailed_Texts()
    {
        Assert.Equal(
            "Nie udało się otworzyć strony: Brak powłoki konta. Skopiowano adres: https://github.com/ds99-01/DSOptimizer",
            GuiText.OpenUrlFailed("Brak powłoki konta.", true));
        Assert.Equal(
            "Nie udało się otworzyć strony: Brak powłoki konta. Nie udało się też skopiować adresu.",
            GuiText.OpenUrlFailed("Brak powłoki konta.", false));
        Assert.Throws<ArgumentNullException>(() => GuiText.OpenUrlFailed(null!, true));
    }

    [Fact]
    public void OneLine_MultiLine_JoinedWithBar()
        => Assert.Equal("a | b | c", GuiText.OneLine("a\r\nb\n\n  \nc\r", 300));

    [Fact]
    public void OneLine_Long_TruncatedWithEllipsis()
    {
        var result = GuiText.OneLine(new string('x', 500), GuiText.MaxResultMessageLength);
        Assert.Equal(300, result.Length);
        Assert.EndsWith("…", result);
    }

    [Fact]
    public void OneLine_ShortSingle_Unchanged()
        => Assert.Equal("krótki wynik", GuiText.OneLine("krótki wynik", 300));

    [Fact]
    public void EditionsLine_PinnedSamples()
    {
        Assert.Equal("Działa w edycjach: Pro, Enterprise i Education.", GuiText.EditionsLine(new[] { EditionFamily.Pro, EditionFamily.Business }));
        Assert.Equal("Działa w edycjach: Home i Pro.", GuiText.EditionsLine(new[] { EditionFamily.Home, EditionFamily.Pro }));
        Assert.Equal("Działa w edycjach: Enterprise i Education.", GuiText.EditionsLine(new[] { EditionFamily.Business }));
        Assert.Equal("Działa w edycjach: Home, Pro, Enterprise i Education.", GuiText.EditionsLine(new[] { EditionFamily.Business, EditionFamily.Home, EditionFamily.Pro }));
    }
}
