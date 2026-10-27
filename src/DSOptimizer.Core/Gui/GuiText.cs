using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using DSOptimizer.Core.Engine;
using DSOptimizer.Core.Hardware;
using DSOptimizer.Core.Model;

namespace DSOptimizer.Core.Gui;

/// <summary>Teksty okna (PL).</summary>
public static class GuiText
{
    public static IReadOnlyList<string> CategoryOrder { get; } = new[] { "privacy", "perf", "disk", "net", "sec", "ux", "panels", "repair", "tools" };

    public static string CategoryName(string category) => category switch
    {
        "privacy" => "Prywatność i AI",
        "perf" => "Wydajność",
        "disk" => "Dysk i system plików",
        "net" => "Sieć i LAN",
        "sec" => "Bezpieczeństwo i szyfrowanie",
        "ux" => "Explorer i wygoda",
        "panels" => "Panele systemowe",
        "repair" => "Naprawy",
        "tools" => "Narzędzia",
        _ => category,
    };

    /// <summary>Podpowiedź przy żarówce na pozycji sugerowanej (wariant C, wybór Dawida 2026-10-21).</summary>
    public const string SuggestedTooltip = "Sugerowane. Przy pierwszym wejściu na stronę program sam zaznacza tę pozycję, jeśli nie jest jeszcze zastosowana. Możesz ją odznaczyć.";

    /// <summary>Wiersz „Działa w edycjach: …” dla pozycji z polem editions.</summary>
    public static string EditionsLine(IReadOnlyList<EditionFamily> editions)
    {
        var names = editions.Distinct().OrderBy(family => family).Select(family => family switch
        {
            EditionFamily.Home => "Home",
            EditionFamily.Pro => "Pro",
            _ => "Enterprise i Education",
        }).ToList();
        // Business to jedna jednostka „Enterprise i Education” - przed nią zawsze przecinek, żeby nie było dwóch „i” z rzędu.
        var list = names.Count < 2 || editions.Contains(EditionFamily.Business)
            ? string.Join(", ", names)
            : string.Join(", ", names.Take(names.Count - 1)) + " i " + names[names.Count - 1];
        return $"Działa w edycjach: {list}.";
    }

    public static string RiskName(RiskLevel risk) => risk switch
    {
        RiskLevel.Safe => "bezpieczne",
        RiskLevel.Caution => "ostrożnie",
        RiskLevel.Experimental => "eksperymentalne",
        _ => throw new ArgumentOutOfRangeException(nameof(risk), risk, null),
    };

    /// <summary>Podpowiedź i nazwa dla czytnika ekranu wskaźnika ryzyka (trzy kreski): „Ryzyko: bezpieczne”.</summary>
    public static string RiskToolTip(RiskLevel risk) => "Ryzyko: " + RiskName(risk);

    public static string StateName(DetectionState? state) => state switch
    {
        null => "—",
        DetectionState.Applied => "Zastosowane",
        DetectionState.NotApplied => "Niezastosowane",
        DetectionState.Partial => "Częściowo",
        DetectionState.NotSupported => "Nieobsługiwane w tym systemie",
        DetectionState.Unknown => "Nieznany (błąd detekcji)",
        _ => throw new ArgumentOutOfRangeException(nameof(state), state, null),
    };

    public static string OutcomeName(TweakOutcome outcome) => outcome switch
    {
        TweakOutcome.Applied => "Zastosowano",
        TweakOutcome.AlreadyApplied => "Już zastosowane",
        TweakOutcome.PendingReboot => "Zastosowano – wymaga restartu",
        TweakOutcome.NotSupported => "Nieobsługiwane w tym systemie",
        TweakOutcome.Failed => "Błąd",
        TweakOutcome.FailedRolledBack => "Błąd – zmiany wycofane",
        TweakOutcome.FailedRollbackIncomplete => "Błąd – wycofanie niepełne",
        TweakOutcome.Undone => "Cofnięto",
        TweakOutcome.UndoIncomplete => "Cofnięcie niepełne",
        TweakOutcome.UndoneFromDefaults => "Cofnięto do wartości domyślnych",
        TweakOutcome.Refused => "Odmowa",
        TweakOutcome.Executed => "Wykonano",
        TweakOutcome.Skipped => "Pominięto",
        TweakOutcome.Removed => "Usunięto",
        TweakOutcome.NotConfirmed => "Nie potwierdzono",
        TweakOutcome.Installed => "Zainstalowano",
        TweakOutcome.AlreadyInstalled => "Już zainstalowane",
        TweakOutcome.InstalledUnverified => "Zainstalowano (zgłoszone przez winget)",
        _ => throw new ArgumentOutOfRangeException(nameof(outcome), outcome, null),
    };

    /// <summary>Licznik nad dziennikiem, np. „1 błąd, 3 ostrzeżenia” (polska odmiana liczebników).</summary>
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

        return $"{Count(errors, "błąd", "błędy", "błędów")}, {Count(warnings, "ostrzeżenie", "ostrzeżenia", "ostrzeżeń")}";
    }

    public static string OperationName(GuiOperation operation) => operation switch
    {
        GuiOperation.Apply => "Zastosuj",
        GuiOperation.Undo => "Cofnij",
        GuiOperation.Execute => "Uruchom",
        GuiOperation.RemoveApps => "Usuń",
        GuiOperation.InstallApps => "Zainstaluj",
        _ => throw new ArgumentOutOfRangeException(nameof(operation), operation, null),
    };

    /// <summary>
    /// Drugi wiersz tweaka na liście: stan, a gdy zmiana czeka na restart – dopisek (cofnięta pozycja pokazuje „Cofnięta”
    /// zamiast stanu z detekcji, bo ta jeszcze widzi stare ustawienie).
    /// </summary>
    public static string ItemRowSubtitle(string stateText, RebootPendingKind? pending) => pending switch
    {
        RebootPendingKind.Applied => $"{stateText}. Czeka na ponowne uruchomienie.",
        RebootPendingKind.Undone => "Cofnięta. Czeka na ponowne uruchomienie.",
        _ => stateText,
    };

    /// <summary>Nazwa wiersza listy pozycji dla czytnika ekranu: tytuł i drugi wiersz (stan albo rodzaj akcji).</summary>
    public static string ItemRowName(string title, string subtitle) => $"{title}, {subtitle}";

    /// <summary>Opis wiersza dla czytnika ekranu: podsumowanie, a dla pozycji sugerowanej dopisek „Sugerowane.” (nazwa wiersza bez zmian).</summary>
    public static string ItemRowHelp(string summary, bool suggested)
        => !suggested ? summary : summary.Length == 0 ? "Sugerowane." : $"{summary} Sugerowane.";

    /// <summary>Linia dziennika, gdy operacja dostała id spoza wczytanego katalogu (np. „Cofnij” z historii).</summary>
    public static string ItemNotFound(GuiOperation operation, string id)
        => $"{OperationName(operation)}: nie ma pozycji „{id}” w katalogu – nic nie zmieniono.";

    public const string SearchTitle = "Wyniki wyszukiwania";

    public const string SearchPlaceholder = "Szukaj we wszystkich kategoriach";

    public static string RiskFilterName(RiskFilter filter) => filter switch
    {
        RiskFilter.All => "Wszystkie",
        RiskFilter.NoExperimental => "Bez eksperymentalnych",
        RiskFilter.SafeOnly => "Tylko bezpieczne",
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
            return string.Format(CultureInfo.InvariantCulture, "{0} z {1} {2} zastosowanych",
                applied, tweaks, tweaks == 1 ? "ustawienia" : "ustawień");
        }

        var total = actions + panels;
        return (actions, panels) switch
        {
            (0, _) => Counted(panels, "panel", "panele", "paneli"),
            (_, 0) => Counted(actions, "akcja", "akcje", "akcji"),
            _ => Counted(total, "pozycja", "pozycje", "pozycji"),
        };
    }

    /// <summary>Opis licznika strony „Aplikacje” w menu: „34 aplikacje”.</summary>
    public static string AppsCounterDescription(int count) => Counted(count, "aplikacja", "aplikacje", "aplikacji");

    /// <summary>Opis licznika strony „Instalacja” w menu (pozycje listy apps.json): „35 aplikacji na liście”.</summary>
    public static string InstallCounterDescription(int count) => AppsCounterDescription(count) + " na liście";

    /// <summary>Opis licznika strony „Historia zmian” w menu (aktywne zmiany): „3 aktywne zmiany”.</summary>
    public static string HistoryCounterDescription(int count) => Counted(count, "aktywna zmiana", "aktywne zmiany", "aktywnych zmian");

    private static string Counted(int count, string one, string few, string many)
        => count.ToString(CultureInfo.InvariantCulture) + " " + PolishPlural.Choose(count, one, few, many);

    /// <summary>Linia pod listą: ile pozycji ukrył filtr i ile zastosowanych jest widocznych mimo niego; null, gdy nie ma czego mówić.</summary>
    public static string? FilterNote(int hidden, int shownDespiteFilter)
    {
        var hiddenPart = string.Format(CultureInfo.InvariantCulture, "Ukryte przez filtr: {0}", hidden);
        var shownPart = string.Format(CultureInfo.InvariantCulture, "Zastosowane pozycje widoczne mimo filtra: {0}", shownDespiteFilter);
        return (hidden > 0, shownDespiteFilter > 0) switch
        {
            (false, false) => null,
            (true, false) => hiddenPart,
            (false, true) => shownPart,
            _ => hiddenPart + ". " + shownPart,
        };
    }

    public static string SearchSummary(int items, int categories)
        => items == 0
            ? "Brak wyników"
            : string.Format(
                CultureInfo.InvariantCulture,
                "{0} {1} w {2} {3}",
                items,
                PolishPlural.Choose(items, "pozycja", "pozycje", "pozycji"),
                categories,
                PolishPlural.Choose(categories, "kategorii", "kategoriach", "kategoriach"));

    public static string NoSearchResults(string query)
        => string.Format(CultureInfo.InvariantCulture, "Nic nie pasuje do „{0}”.", query.Trim());

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
            return "Kliknij dwukrotnie wiersz albo pole wyboru, żeby dodać do zmiany.";
        }

        var title = "Zaznaczono " + Count(selected, "pozycję", "pozycje", "pozycji");
        if (toUndo == 0)
        {
            return title;
        }

        return toApply == 0
            ? $"{title}: {toUndo} do cofnięcia"
            : $"{title}: {toApply} do zastosowania, {toUndo} do cofnięcia";
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
            parts.Add(Count(rebootCount, "wymaga", "wymagają", "wymaga") + " restartu.");
        }

        if (hasApplicable)
        {
            parts.Add(restorePoint
                ? "Przed zastosowaniem program spróbuje utworzyć punkt przywracania."
                : "Bez punktu przywracania.");
        }

        return string.Join(" ", parts);
    }

    /// <summary>Linia okna potwierdzenia serii „Zastosuj”: czy powstanie punkt przywracania.</summary>
    public static string BatchRestorePointNotice(bool createsRestorePoint)
        => createsRestorePoint
            ? "Przed zmianami zostanie utworzony punkt przywracania, jeśli ochrona systemu jest włączona."
            : "Punkt przywracania nie zostanie utworzony (wyłączony w Ustawieniach).";

    /// <summary>Linia panelu szczegółów: czy tweak jest w serii i do którego przycisku trafi (stan już zastosowany → „Cofnij”).</summary>
    public static string SeriesLine(bool inSeries, bool canApply, bool canUndo)
    {
        if (!inSeries)
        {
            return "Nie w serii – kliknij dwukrotnie wiersz, żeby dodać";
        }

        return (canApply, canUndo) switch
        {
            (true, false) => "W serii – trafi do „Zastosuj”",
            (false, true) => "W serii – już zastosowana, trafi do „Cofnij”",
            (true, true) => "W serii – możesz ją zastosować albo cofnąć",
            _ => "W serii – teraz nie da się jej zastosować ani cofnąć",
        };
    }

    /// <summary>Podpowiedź pola wyboru tweaka: co da zaznaczenie przy bieżącym stanie.</summary>
    public static string SelectHint(bool canApply, bool canUndo) => (canApply, canUndo) switch
    {
        (true, false) => "Zaznacz, żeby zastosować w serii (przycisk „Zastosuj”)",
        (false, true) => "Już zastosowana – zaznacz, żeby cofnąć w serii (przycisk „Cofnij”)",
        (true, true) => "Zaznacz, żeby zastosować albo cofnąć w serii",
        _ => "Teraz nie da się jej zastosować ani cofnąć",
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
            list += " i " + Count(titles.Count - shown, "inna", "inne", "innych");
        }

        return string.Format(
            CultureInfo.InvariantCulture,
            "Anulowano serię: {0} – {1} ({2}).",
            OperationName(operation),
            Count(titles.Count, "pozycja", "pozycje", "pozycji"),
            list);
    }

    /// <summary>Pusta lista przy wyszukiwaniu: brak dopasowań albo dopasowania ukryte przez filtr ryzyka.</summary>
    public static string EmptySearchText(string query, int hiddenCount)
        => hiddenCount == 0 ? NoSearchResults(query) : "Pasujące pozycje ukrył filtr ryzyka.";

    /// <summary>Linia Info, gdy program tylko kopiuje adres strony autora (wariant bez otwierania przeglądarki).</summary>
    public static string UrlCopied(string url)
        => "Skopiowano adres: " + (url ?? throw new ArgumentNullException(nameof(url)));

    /// <summary>Linia ostrzeżenia po odmowie otwarcia strony autora: powód i wynik kopiowania adresu do schowka.</summary>
    public static string OpenUrlFailed(string reason, bool copied)
    {
        if (reason is null)
        {
            throw new ArgumentNullException(nameof(reason));
        }

        return copied
            ? $"Nie udało się otworzyć strony: {reason} Skopiowano adres: {ProductInfo.AuthorUrl}"
            : $"Nie udało się otworzyć strony: {reason} Nie udało się też skopiować adresu.";
    }

    public const string WindowErrorsMuted = "Kolejne błędy okna trafiają tylko do pliku logu (więcej niż 5 w ciągu 10 s).";

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

    public const string RebootTitle = "Wymagany restart.";

    public const string RestartConfirmText = "Komputer uruchomi się ponownie. Zapisz pracę w otwartych programach.";

    /// <summary>Baner restartu po serii: ile zmian zadziała dopiero po ponownym uruchomieniu komputera.</summary>
    public static string RebootBannerText(int count)
        => Count(count, "zmiana zadziała", "zmiany zadziałają", "zmian zadziała") + " dopiero po ponownym uruchomieniu komputera.";

    /// <summary>Okno postępu długiej naprawy: podtytuł paska w trakcie.</summary>
    public const string ActionRunningSubtitle = "w toku";

    /// <summary>Etap przed pierwszym zdarzeniem postępu z handlera.</summary>
    public const string ActionStarting = "Przygotowanie…";

    /// <summary>Podpowiedź pod paskiem postępu (bez obietnic czasu – czas trwania jest w ostrzeżeniach pozycji).</summary>
    public const string ActionProgressHint = "Nie wyłączaj komputera, dopóki naprawa trwa.";

    public const string ActionStopText = "Zatrzymaj po tym kroku";

    public const string ActionStoppingText = "Zatrzymam po bieżącym kroku…";

    /// <summary>Blok restartu w wyniku długiej naprawy (handler zgłosił <c>RequiresReboot</c> przy <c>Executed</c>).</summary>
    public const string ActionRebootText = "Naprawa zadziała w pełni dopiero po ponownym uruchomieniu komputera.";

    /// <summary>„Dalej: SFC – …” obok „Krok N z M” (nazwa następnego kroku z handlera).</summary>
    public static string ActionNextStageText(string nextStage) => "Dalej: " + nextStage;

    /// <summary>„Krok 1 z 2” (indeks liczony od 0).</summary>
    public static string ActionStepText(int stepIndex, int stepCount)
        => string.Format(CultureInfo.InvariantCulture, "Krok {0} z {1}", stepIndex + 1, stepCount);

    /// <summary>1 → one; końcówki 2–4 (poza 12–14) → few; reszta (także 0) → many.</summary>
    internal static string Count(int value, string one, string few, string many)
    {
        var lastDigit = value % 10;
        var lastTwo = value % 100;
        var word = value == 1
            ? one
            : (lastDigit is >= 2 and <= 4) && (lastTwo is < 12 or > 14) ? few : many;
        return string.Format(CultureInfo.InvariantCulture, "{0} {1}", value, word);
    }
}