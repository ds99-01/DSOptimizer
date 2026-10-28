using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using DSOptimizer.Core.Engine;
using DSOptimizer.Core.Hardware;
using DSOptimizer.Core.Localization;
using DSOptimizer.Core.Model;

namespace DSOptimizer.Core.Gui;

/// <summary>Teksty okna (polskie źródło, tłumaczenie przez <see cref="L"/>).</summary>
public static class GuiText
{
    public static IReadOnlyList<string> CategoryOrder { get; } = new[] { "privacy", "perf", "disk", "net", "sec", "ux", "panels", "repair", "tools" };

    public static string CategoryName(string category) => category switch
    {
        "privacy" => L.T("Prywatność i AI"),
        "perf" => L.T("Wydajność"),
        "disk" => L.T("Dysk i system plików"),
        "net" => L.T("Sieć i LAN"),
        "sec" => L.T("Bezpieczeństwo i szyfrowanie"),
        "ux" => L.T("Explorer i wygoda"),
        "panels" => L.T("Panele systemowe"),
        "repair" => L.T("Naprawy"),
        "tools" => L.T("Narzędzia"),
        _ => category,
    };

    /// <summary>Podpowiedź przy żarówce na pozycji sugerowanej (wariant C, wybór Dawida 2026-10-21).</summary>
    public static string SuggestedTooltip => L.T("Sugerowane. Przy pierwszym wejściu na stronę program sam zaznacza tę pozycję, jeśli nie jest jeszcze zastosowana. Możesz ją odznaczyć.");

    /// <summary>Wiersz „Działa w edycjach: …” dla pozycji z polem editions.</summary>
    public static string EditionsLine(IReadOnlyList<EditionFamily> editions)
    {
        var names = editions.Distinct().OrderBy(family => family).Select(family => family switch
        {
            EditionFamily.Home => "Home",
            EditionFamily.Pro => "Pro",
            _ => L.T("Enterprise i Education"),
        }).ToList();
        // Business to jedna jednostka „Enterprise i Education” - przed nią zawsze przecinek, żeby nie było dwóch „i” z rzędu.
        var list = names.Count < 2 || editions.Contains(EditionFamily.Business)
            ? string.Join(", ", names)
            : L.F("{0} i {1}", string.Join(", ", names.Take(names.Count - 1)), names[names.Count - 1]);
        return L.F("Działa w edycjach: {0}.", list);
    }

    public static string RiskName(RiskLevel risk) => risk switch
    {
        RiskLevel.Safe => L.T("bezpieczne"),
        RiskLevel.Caution => L.T("ostrożnie"),
        RiskLevel.Experimental => L.T("eksperymentalne"),
        _ => throw new ArgumentOutOfRangeException(nameof(risk), risk, null),
    };

    /// <summary>Podpowiedź i nazwa dla czytnika ekranu wskaźnika ryzyka (trzy kreski): „Ryzyko: bezpieczne”.</summary>
    public static string RiskToolTip(RiskLevel risk) => L.F("Ryzyko: {0}", RiskName(risk));

    public static string StateName(DetectionState? state) => state switch
    {
        null => "—",
        DetectionState.Applied => L.T("Zastosowane"),
        DetectionState.NotApplied => L.T("Niezastosowane"),
        DetectionState.Partial => L.T("Częściowo"),
        DetectionState.NotSupported => L.T("Nieobsługiwane w tym systemie"),
        DetectionState.Unknown => L.T("Nieznany (błąd detekcji)"),
        _ => throw new ArgumentOutOfRangeException(nameof(state), state, null),
    };

    public static string OutcomeName(TweakOutcome outcome) => outcome switch
    {
        TweakOutcome.Applied => L.T("Zastosowano"),
        TweakOutcome.AlreadyApplied => L.T("Już zastosowane"),
        TweakOutcome.PendingReboot => L.T("Zastosowano – wymaga restartu"),
        TweakOutcome.NotSupported => L.T("Nieobsługiwane w tym systemie"),
        TweakOutcome.Failed => L.T("Błąd"),
        TweakOutcome.FailedRolledBack => L.T("Błąd – zmiany wycofane"),
        TweakOutcome.FailedRollbackIncomplete => L.T("Błąd – wycofanie niepełne"),
        TweakOutcome.Undone => L.T("Cofnięto"),
        TweakOutcome.UndoIncomplete => L.T("Cofnięcie niepełne"),
        TweakOutcome.UndoneFromDefaults => L.T("Cofnięto do wartości domyślnych"),
        TweakOutcome.Refused => L.T("Odmowa"),
        TweakOutcome.Executed => L.T("Wykonano"),
        TweakOutcome.Skipped => L.T("Pominięto"),
        TweakOutcome.Removed => L.T("Usunięto"),
        TweakOutcome.NotConfirmed => L.T("Nie potwierdzono"),
        TweakOutcome.Installed => L.T("Zainstalowano"),
        TweakOutcome.AlreadyInstalled => L.T("Już zainstalowane"),
        TweakOutcome.InstalledUnverified => L.T("Zainstalowano (zgłoszone przez winget)"),
        _ => throw new ArgumentOutOfRangeException(nameof(outcome), outcome, null),
    };

    /// <summary>Licznik nad dziennikiem, np. „1 błąd, 3 ostrzeżenia” (odmiana liczebników przez <see cref="L.P"/>).</summary>
    public static string LogSummary(int errors, int warnings)
    {
        if (errors < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(errors), errors, null);
        }

        if (warnings < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(warnings), warnings, null);
        }

        var errorsText = L.P("{0} błąd", "{0} błędy", "{0} błędów", errors);
        var warningsText = L.P("{0} ostrzeżenie", "{0} ostrzeżenia", "{0} ostrzeżeń", warnings);
        return $"{errorsText}, {warningsText}";
    }

    public static string OperationName(GuiOperation operation) => operation switch
    {
        GuiOperation.Apply => L.T("Zastosuj"),
        GuiOperation.Undo => L.T("Cofnij"),
        GuiOperation.Execute => L.T("Uruchom"),
        GuiOperation.RemoveApps => L.T("Usuń"),
        GuiOperation.InstallApps => L.T("Zainstaluj"),
        _ => throw new ArgumentOutOfRangeException(nameof(operation), operation, null),
    };

    /// <summary>
    /// Drugi wiersz tweaka na liście: stan, a gdy zmiana czeka na restart – dopisek (cofnięta pozycja pokazuje „Cofnięta”
    /// zamiast stanu z detekcji, bo ta jeszcze widzi stare ustawienie).
    /// </summary>
    public static string ItemRowSubtitle(string stateText, RebootPendingKind? pending) => pending switch
    {
        RebootPendingKind.Applied => L.F("{0}. Czeka na ponowne uruchomienie.", stateText),
        RebootPendingKind.Undone => L.T("Cofnięta. Czeka na ponowne uruchomienie."),
        _ => stateText,
    };

    /// <summary>Nazwa wiersza listy pozycji dla czytnika ekranu: tytuł i drugi wiersz (stan albo rodzaj akcji).</summary>
    public static string ItemRowName(string title, string subtitle) => $"{title}, {subtitle}";

    /// <summary>Opis wiersza dla czytnika ekranu: podsumowanie, a dla pozycji sugerowanej dopisek „Sugerowane.” (nazwa wiersza bez zmian).</summary>
    public static string ItemRowHelp(string summary, bool suggested)
        => !suggested ? summary : summary.Length == 0 ? L.T("Sugerowane.") : L.F("{0} Sugerowane.", summary);

    /// <summary>Linia dziennika, gdy operacja dostała id spoza wczytanego katalogu (np. „Cofnij” z historii).</summary>
    public static string ItemNotFound(GuiOperation operation, string id)
        => L.F("{0}: nie ma pozycji „{1}” w katalogu – nic nie zmieniono.", OperationName(operation), id);

    public static string SearchTitle => L.T("Wyniki wyszukiwania");

    public static string SearchPlaceholder => L.T("Szukaj we wszystkich kategoriach");

    public static string RiskFilterName(RiskFilter filter) => filter switch
    {
        RiskFilter.All => L.T("Wszystkie"),
        RiskFilter.NoExperimental => L.T("Bez eksperymentalnych"),
        RiskFilter.SafeOnly => L.T("Tylko bezpieczne"),
        _ => throw new ArgumentOutOfRangeException(nameof(filter), filter, null),
    };

    /// <summary>Licznik w menu: zastosowane/wszystkie tweaki, a w kategorii bez tweaków liczba pozycji.</summary>
    public static string CategoryCounter(int applied, int tweaks, int total)
        => tweaks > 0
            ? string.Format(CultureInfo.InvariantCulture, "{0}/{1}", applied, tweaks)
            : total.ToString(CultureInfo.InvariantCulture);

    /// <summary>Podpowiedź kategorii w zwiniętym menu: „Nazwa – licznik” (licznik jak <see cref="CategoryCounter"/>).</summary>
    public static string CategoryNavToolTip(string name, string counter) => $"{name} – {counter}";

    /// <summary>
    /// Pełny opis licznika kategorii w menu (podpowiedź licznika i nazwa dla czytnika ekranu): „0 z 13 ustawień zastosowanych”;
    /// w kategorii bez tweaków liczba pozycji z rodzajem – „15 paneli”, „5 akcji”, a przy obu rodzajach „12 pozycji”.
    /// </summary>
    public static string CategoryCounterDescription(int applied, int tweaks, int actions, int panels)
    {
        if (tweaks > 0)
        {
            // Po polsku „ustawienia” tylko przy 1, inaczej „ustawień” (także 2–4: „z 3 ustawień”).
            return L.F("{0} z {1}", applied, L.P("{0} ustawienia zastosowanych", "{0} ustawień zastosowanych", "{0} ustawień zastosowanych", tweaks));
        }

        var total = actions + panels;
        return (actions, panels) switch
        {
            (0, _) => L.P("{0} panel", "{0} panele", "{0} paneli", panels),
            (_, 0) => L.P("{0} akcja", "{0} akcje", "{0} akcji", actions),
            _ => L.P("{0} pozycja", "{0} pozycje", "{0} pozycji", total),
        };
    }

    /// <summary>Opis licznika strony „Aplikacje” w menu: „34 aplikacje”.</summary>
    public static string AppsCounterDescription(int count) => L.P("{0} aplikacja", "{0} aplikacje", "{0} aplikacji", count);

    /// <summary>Opis licznika strony „Instalacja” w menu (pozycje listy apps.json): „35 aplikacji na liście”.</summary>
    public static string InstallCounterDescription(int count)
        => L.P("{0} aplikacja na liście", "{0} aplikacje na liście", "{0} aplikacji na liście", count);

    /// <summary>Opis licznika strony „Historia zmian” w menu (aktywne zmiany): „3 aktywne zmiany”.</summary>
    public static string HistoryCounterDescription(int count) => L.P("{0} aktywna zmiana", "{0} aktywne zmiany", "{0} aktywnych zmian", count);

    /// <summary>Linia pod listą: ile pozycji ukrył filtr i ile zastosowanych jest widocznych mimo niego; null, gdy nie ma czego mówić.</summary>
    public static string? FilterNote(int hidden, int shownDespiteFilter) => (hidden > 0, shownDespiteFilter > 0) switch
    {
        (false, false) => null,
        (true, false) => L.F("Ukryte przez filtr: {0}", hidden),
        (false, true) => L.F("Zastosowane pozycje widoczne mimo filtra: {0}", shownDespiteFilter),
        _ => L.F("Ukryte przez filtr: {0}. Zastosowane pozycje widoczne mimo filtra: {1}", hidden, shownDespiteFilter),
    };

    public static string SearchSummary(int items, int categories)
        => items == 0
            ? L.T("Brak wyników")
            : L.F("{0} w {1}", L.P("{0} pozycja", "{0} pozycje", "{0} pozycji", items), L.P("{0} kategorii", "{0} kategoriach", "{0} kategoriach", categories));

    public static string NoSearchResults(string query) => L.F("Nic nie pasuje do „{0}”.", query.Trim());

    /// <summary>
    /// Tytuł stałego paska akcji: ile pozycji zaznaczono (0 → podpowiedź, jak zacząć). Zaznaczenie znaczy „dodaj do serii”, a skutek
    /// wybiera przycisk – gdy wśród zaznaczonych są pozycje do cofnięcia, tytuł mówi, ile trafi do „Zastosuj”, a ile do „Cofnij”.
    /// </summary>
    public static string SelectionBarTitle(int selected, int toApply, int toUndo)
    {
        if (selected < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(selected), selected, null);
        }

        if (toApply < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(toApply), toApply, null);
        }

        if (toUndo < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(toUndo), toUndo, null);
        }

        if (selected == 0)
        {
            return L.T("Kliknij dwukrotnie wiersz albo pole wyboru, żeby dodać do zmiany.");
        }

        var title = L.P("Zaznaczono {0} pozycję", "Zaznaczono {0} pozycje", "Zaznaczono {0} pozycji", selected);
        if (toUndo == 0)
        {
            return title;
        }

        return toApply == 0
            ? L.F("{0}: {1} do cofnięcia", title, toUndo)
            : L.F("{0}: {1} do zastosowania, {2} do cofnięcia", title, toApply, toUndo);
    }

    /// <summary>Druga linia paska: restarty i punkt przywracania (ten dotyczy tylko „Zastosuj”, więc wymaga <paramref name="hasApplicable"/>).</summary>
    public static string SelectionBarDetails(int rebootCount, bool hasApplicable, bool restorePoint)
    {
        if (rebootCount < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(rebootCount), rebootCount, null);
        }

        var parts = new List<string>();
        if (rebootCount > 0)
        {
            // Polska forma dla 1 i dla 5+ jest ta sama („wymaga”), więc 1 ma własny klucz – po angielsku „requires”, nie „require”.
            parts.Add(rebootCount == 1
                ? L.T("1 wymaga restartu.")
                : L.P("{0} wymaga restartu.", "{0} wymagają restartu.", "{0} wymaga restartu.", rebootCount));
        }

        if (hasApplicable)
        {
            parts.Add(restorePoint
                ? L.T("Przed zastosowaniem program spróbuje utworzyć punkt przywracania.")
                : L.T("Bez punktu przywracania."));
        }

        return string.Join(" ", parts);
    }

    /// <summary>Linia okna potwierdzenia serii „Zastosuj”: czy powstanie punkt przywracania.</summary>
    public static string BatchRestorePointNotice(bool createsRestorePoint)
        => createsRestorePoint
            ? L.T("Przed zmianami zostanie utworzony punkt przywracania, jeśli ochrona systemu jest włączona.")
            : L.T("Punkt przywracania nie zostanie utworzony (wyłączony w Ustawieniach).");

    /// <summary>Linia panelu szczegółów: czy tweak jest w serii i do którego przycisku trafi (stan już zastosowany → „Cofnij”).</summary>
    public static string SeriesLine(bool inSeries, bool canApply, bool canUndo)
    {
        if (!inSeries)
        {
            return L.T("Nie w serii – kliknij dwukrotnie wiersz, żeby dodać");
        }

        return (canApply, canUndo) switch
        {
            (true, false) => L.T("W serii – trafi do „Zastosuj”"),
            (false, true) => L.T("W serii – już zastosowana, trafi do „Cofnij”"),
            (true, true) => L.T("W serii – możesz ją zastosować albo cofnąć"),
            _ => L.T("W serii – teraz nie da się jej zastosować ani cofnąć"),
        };
    }

    /// <summary>Podpowiedź pola wyboru tweaka: co da zaznaczenie przy bieżącym stanie.</summary>
    public static string SelectHint(bool canApply, bool canUndo) => (canApply, canUndo) switch
    {
        (true, false) => L.T("Zaznacz, żeby zastosować w serii (przycisk „Zastosuj”)"),
        (false, true) => L.T("Już zastosowana – zaznacz, żeby cofnąć w serii (przycisk „Cofnij”)"),
        (true, true) => L.T("Zaznacz, żeby zastosować albo cofnąć w serii"),
        _ => L.T("Teraz nie da się jej zastosować ani cofnąć"),
    };

    /// <summary>Komunikat o anulowanej serii: do trzech tytułów dosłownie, reszta jako „i N innych”.</summary>
    public static string CancelledBatch(GuiOperation operation, IReadOnlyList<string> titles)
    {
        if (titles is null)
        {
            throw new ArgumentNullException(nameof(titles));
        }

        if (titles.Count == 0)
        {
            throw new ArgumentException("Seria musi mieć co najmniej jedną pozycję.", nameof(titles));
        }

        const int shown = 3;
        var list = string.Join("; ", titles.Take(shown));
        if (titles.Count > shown)
        {
            list = L.F("{0} i {1}", list, L.P("{0} inna", "{0} inne", "{0} innych", titles.Count - shown));
        }

        var items = L.P("{0} pozycja", "{0} pozycje", "{0} pozycji", titles.Count);
        return L.F("Anulowano serię: {0} – {1} ({2}).", OperationName(operation), items, list);
    }

    /// <summary>Pusta lista przy wyszukiwaniu: brak dopasowań albo dopasowania ukryte przez filtr ryzyka.</summary>
    public static string EmptySearchText(string query, int hiddenCount)
        => hiddenCount == 0 ? NoSearchResults(query) : L.T("Pasujące pozycje ukrył filtr ryzyka.");

    /// <summary>Linia Info, gdy program tylko kopiuje adres strony repozytorium (wariant bez otwierania przeglądarki).</summary>
    public static string UrlCopied(string url)
        => L.F("Skopiowano adres: {0}", url ?? throw new ArgumentNullException(nameof(url)));

    /// <summary>Linia ostrzeżenia po odmowie otwarcia strony repozytorium: powód i wynik kopiowania adresu do schowka.</summary>
    public static string OpenUrlFailed(string reason, bool copied)
    {
        if (reason is null)
        {
            throw new ArgumentNullException(nameof(reason));
        }

        return copied
            ? L.F("Nie udało się otworzyć strony: {0} Skopiowano adres: {1}", reason, ProductInfo.RepositoryUrl)
            : L.F("Nie udało się otworzyć strony: {0} Nie udało się też skopiować adresu.", reason);
    }

    public static string WindowErrorsMuted => L.T("Kolejne błędy okna trafiają tylko do pliku logu (więcej niż 5 w ciągu 10 s).");

    /// <summary>Najdłuższy komunikat wyniku w jednej linii dziennika; reszta tylko w pliku logu.</summary>
    public const int MaxResultMessageLength = 300;

    private static readonly string[] LineBreaks = { "\r\n", "\n", "\r" };

    /// <summary>Tekst w jednej linii: podziały linii na „ | ” (puste linie pomijane), przycięcie do <paramref name="maxLength"/> znaków łącznie z „…”.</summary>
    public static string OneLine(string text, int maxLength)
    {
        if (text is null)
        {
            throw new ArgumentNullException(nameof(text));
        }

        var parts = text.Split(LineBreaks, StringSplitOptions.None)
            .Select(p => p.Trim())
            .Where(p => p.Length > 0);
        var joined = string.Join(" | ", parts);
        return joined.Length <= maxLength ? joined : joined.Substring(0, Math.Max(0, maxLength - 1)) + "…";
    }

    public static string RebootTitle => L.T("Wymagany restart.");

    public static string RestartConfirmText => L.T("Komputer uruchomi się ponownie. Zapisz pracę w otwartych programach.");

    /// <summary>Baner restartu po serii: ile zmian zadziała dopiero po ponownym uruchomieniu komputera.</summary>
    public static string RebootBannerText(int count)
        => L.P("{0} zmiana zadziała dopiero po ponownym uruchomieniu komputera.", "{0} zmiany zadziałają dopiero po ponownym uruchomieniu komputera.", "{0} zmian zadziała dopiero po ponownym uruchomieniu komputera.", count);

    /// <summary>Okno postępu długiej naprawy: podtytuł paska w trakcie.</summary>
    public static string ActionRunningSubtitle => L.T("w toku");

    /// <summary>Etap przed pierwszym zdarzeniem postępu z handlera.</summary>
    public static string ActionStarting => L.T("Przygotowanie…");

    /// <summary>Podpowiedź pod paskiem postępu (bez obietnic czasu – czas trwania jest w ostrzeżeniach pozycji).</summary>
    public static string ActionProgressHint => L.T("Nie wyłączaj komputera, dopóki naprawa trwa.");

    public static string ActionStopText => L.T("Zatrzymaj po tym kroku");

    public static string ActionStoppingText => L.T("Zatrzymam po bieżącym kroku…");

    /// <summary>Blok restartu w wyniku długiej naprawy (handler zgłosił <c>RequiresReboot</c> przy <c>Executed</c>).</summary>
    public static string ActionRebootText => L.T("Naprawa zadziała w pełni dopiero po ponownym uruchomieniu komputera.");

    /// <summary>„Dalej: SFC – …” obok „Krok N z M” (nazwa następnego kroku z handlera).</summary>
    public static string ActionNextStageText(string nextStage) => L.F("Dalej: {0}", nextStage);

    /// <summary>„Krok 1 z 2” (indeks liczony od 0).</summary>
    public static string ActionStepText(int stepIndex, int stepCount) => L.F("Krok {0} z {1}", stepIndex + 1, stepCount);
}
