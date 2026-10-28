using System;
using System.Linq;
using DSOptimizer.Core.Engine;
using DSOptimizer.Core.Localization;

namespace DSOptimizer.Core.Gui;

/// <summary>Teksty ekranu „Historia zmian”.</summary>
public static class HistoryText
{
    public static string PageTitle => L.T("Historia zmian");
    public static string PageSubtitle => L.T("Zmiany zrobione programem na tym komputerze, od najnowszej.");
    public static string Empty => L.T("Program nie zmienił jeszcze niczego na tym komputerze.");
    public static string EmptyFiltered => L.T("Żaden wpis nie pasuje do filtra.");

    public static string DateLabel => L.T("Data");
    public static string ItemLabel => L.T("Pozycja");
    public static string FilterLabel => L.T("Filtr");
    public static string StateLabel => L.T("Stan");
    public static string AccountLabel => L.T("Konto");
    public static string CategoryLabel => L.T("Kategoria");
    public static string AppliedLabel => L.T("Zastosowano");
    public static string EventsLabel => L.T("Przebieg");
    public static string UndoButton => L.T("Cofnij");

    public static string WholeComputer => L.T("cały komputer");
    public static string UnknownAccount => L.T("nieznane");
    public static string AppsCategory => L.T("Aplikacje");
    public static string InstallCategory => L.T("Instalacja");
    public static string OutOfCatalog => L.T("pozycja spoza tego katalogu");

    public static string UndoOutOfCatalog => L.T("Tej pozycji nie ma w katalogu tej wersji programu, więc nie da się jej cofnąć.");
    public static string UndoNotNewest => L.T("Nowszy wpis tej pozycji jest aktywny – cofnięcie dotyczy tamtego wpisu.");

    /// <summary>Wpis instalacji zamiast „Cofnij”: instalacji nie cofa się z programu, aplikację usuwa się na stronie „Aplikacje”.</summary>
    public static string InstallUndoBlocked => L.T("Instalacji nie cofa się z programu – aplikację usuniesz na stronie „Aplikacje”.");

    /// <summary>Link w wpisie instalacji/usunięcia, który prowadzi na stronę „Aplikacje”.</summary>
    public static string GoToAppsLink => L.T("Przejdź do: Aplikacje");

    public static string UndoOtherAccount(string name)
        => L.F("Ta zmiana dotyczy konta {0}. Żeby ją cofnąć, uruchom program na tym koncie.", name);

    public static string LoadFailed(string message)
        => L.F("Nie udało się odczytać historii zmian: {0}", message);

    public static string State(HistoryState state) => state switch
    {
        HistoryState.Active => L.T("Aktywna"),
        HistoryState.PendingReboot => L.T("Czeka na restart"),
        HistoryState.UndonePendingReboot => L.T("Cofnięta, czeka na restart"),
        HistoryState.Undone => L.T("Cofnięta"),
        HistoryState.Aborted => L.T("Przerwana"),
        HistoryState.Removed => L.T("Usunięta"),
        HistoryState.RemovalNotConfirmed => L.T("Nie potwierdzono usunięcia"),
        HistoryState.RemovalFailed => L.T("Usuwanie nie powiodło się"),
        HistoryState.Installed => L.T("Zainstalowana"),
        HistoryState.InstallNotConfirmed => L.T("Nie potwierdzono instalacji"),
        HistoryState.InstallNotDone => L.T("Nie zainstalowano"),
        _ => throw new ArgumentOutOfRangeException(nameof(state), state, null),
    };

    /// <summary>
    /// Powód bez przedrostka „Nie wykonano – ”/„Nie potwierdzono – ”; null/biały tekst = null. Przedrostek rozpoznawany w bieżącym
    /// języku i po polsku: journal trzyma tekst z chwili zapisu, więc wpis sprzed przełączenia na angielski nadal jest polski.
    /// </summary>
    public static string? ShortReason(string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return null;
        }

        foreach (var prefix in CurrentAndPolish(() => L.T("Nie wykonano – ")).Concat(CurrentAndPolish(() => L.T("Nie potwierdzono – "))))
        {
            if (text!.StartsWith(prefix, StringComparison.Ordinal))
            {
                return text.Substring(prefix.Length);
            }
        }

        return text;
    }

    /// <summary>Stan wpisu z dopiskiem powodu („Nie zainstalowano – brak sieci”).</summary>
    public static string StateWithReason(HistoryState state, string? reason)
        => string.IsNullOrEmpty(reason) ? State(state) : $"{State(state)} – {reason}";

    /// <summary>Wiersz „rodzaj” wpisu: instalacja, usunięcie albo nazwa kategorii (spoza katalogu: <see cref="OutOfCatalog"/>).</summary>
    public static string KindLine(HistoryKind kind, string? categoryName) => kind switch
    {
        HistoryKind.AppInstall => L.T("Instalacja aplikacji"),
        HistoryKind.AppRemoval => L.T("Usunięcie aplikacji"),
        _ => categoryName ?? OutOfCatalog,
    };

    public static string FilterName(HistoryFilter filter) => filter switch
    {
        HistoryFilter.All => L.T("Wszystkie wpisy"),
        HistoryFilter.Active => L.T("Aktywne"),
        HistoryFilter.PendingReboot => L.T("Czekające na restart"),
        HistoryFilter.Inactive => L.T("Cofnięte i przerwane"),
        _ => throw new ArgumentOutOfRangeException(nameof(filter), filter, null),
    };

    /// <summary>
    /// Tekst zdarzenia journala; <paramref name="message"/> dopisywany po „ – ”. Komunikat silnika o zmianie aktywnej po restarcie
    /// (w bieżącym języku albo zapisany po polsku) nie jest dopisywany – mówi to już sam tekst zdarzenia.
    /// </summary>
    public static string Event(string action, string outcome, string? message)
    {
        var text = BaseEventText(action, outcome);
        return string.IsNullOrEmpty(message) || CurrentAndPolish(() => L.T("Zmiana aktywna po restarcie.")).Contains(message!, StringComparer.Ordinal)
            ? text
            : $"{text} – {message}";
    }

    /// <summary>
    /// Tekst zdarzenia journala z powodem: wynik instalacji aplikacji (<c>install-app</c>, wynik ≠ <c>Started</c>) z <paramref name="reason"/>
    /// pokazuje powód zamiast źródła z <paramref name="message"/> („Nie wykonano – brak połączenia z GitHubem”); inaczej jak
    /// <see cref="Event(string, string, string?)"/>.
    /// </summary>
    public static string Event(string action, string outcome, string? message, string? reason)
        => action == "install-app" && outcome != "Started" && ShortReason(reason) is { } shortReason
            ? $"{BaseEventText(action, outcome)} – {shortReason}"
            : Event(action, outcome, message);

    /// <summary>
    /// Nazwa wiersza listy dla czytnika ekranu: tytuł, rodzaj wpisu (druga linia kolumny „Pozycja”), stan z powodem i data
    /// zastosowania (te same teksty co w wierszu).
    /// </summary>
    public static string RowName(string title, string kindText, string stateText, DateTimeOffset appliedAt)
        => $"{title}, {kindText}, {stateText}, {Date(appliedAt)}";

    public static string Date(DateTimeOffset at)
        => at.ToLocalTime().ToString(L.DateTimePattern, L.Culture);

    public static string Time(DateTimeOffset at)
        => at.ToLocalTime().ToString(L.DateTimeSecondsPattern, L.Culture);

    internal static bool TryParseOutcome(string outcome, out TweakOutcome parsed)
        => Enum.TryParse(outcome, ignoreCase: false, out parsed) && Enum.IsDefined(typeof(TweakOutcome), parsed);

    /// <summary>Tekst w bieżącym języku i (gdy inny) po polsku – tak, jak mógł zostać zapisany w journalu przez wcześniejsze uruchomienie.</summary>
    private static string[] CurrentAndPolish(Func<string> text)
    {
        var current = text();
        string polish;
        using (L.Use(UiLanguage.Polish))
        {
            polish = text();
        }

        return string.Equals(current, polish, StringComparison.Ordinal) ? new[] { current } : new[] { current, polish };
    }

    private static string BaseEventText(string action, string outcome)
    {
        if (action == "apply" && outcome == "Started")
        {
            return L.T("Zastosowanie rozpoczęte");
        }

        var known = TryParseOutcome(outcome, out var parsed);
        switch (action)
        {
            case "remove-app":
                return outcome switch
                {
                    "Started" => L.T("Usuwanie rozpoczęte"),
                    "Removed" => L.T("Usunięto i sprawdzono"),
                    "NotConfirmed" => L.T("Nie potwierdzono usunięcia"),
                    "Failed" => L.T("Usuwanie nie powiodło się"),
                    _ => $"{action}: {outcome}",
                };
            case "install-app":
                return outcome switch
                {
                    "Started" => L.T("Instalacja rozpoczęta"),
                    "Installed" => L.T("Zainstalowano i sprawdzono"),
                    "InstalledUnverified" => L.T("Zainstalowano (zgłoszone przez winget)"),
                    "AlreadyInstalled" => L.T("Była już zainstalowana"),
                    "NotConfirmed" => L.T("Nie potwierdzono instalacji"),
                    "Failed" => L.T("Nie wykonano"),
                    _ => $"{action}: {outcome}",
                };
            case "apply" when known:
                return parsed switch
                {
                    TweakOutcome.Applied => L.T("Zastosowano"),
                    TweakOutcome.PendingReboot => L.T("Zastosowano – zadziała po restarcie"),
                    _ => L.F("Zastosowanie: {0}", GuiText.OutcomeName(parsed)),
                };
            case "undo" when known:
                return parsed == TweakOutcome.Undone ? L.T("Cofnięto") : L.F("Cofnięcie: {0}", GuiText.OutcomeName(parsed));
            case "detect" when known && parsed == TweakOutcome.Applied:
                return L.T("Po restarcie: zmiana aktywna");
            default:
                return $"{action}: {outcome}";
        }
    }
}
