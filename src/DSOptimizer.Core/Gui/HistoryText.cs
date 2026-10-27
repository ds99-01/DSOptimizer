using System;
using System.Globalization;
using DSOptimizer.Core.Engine;

namespace DSOptimizer.Core.Gui;

/// <summary>Teksty ekranu „Historia zmian”.</summary>
public static class HistoryText
{
    public const string PageTitle = "Historia zmian";
    public const string PageSubtitle = "Zmiany zrobione programem na tym komputerze, od najnowszej.";
    public const string Empty = "Program nie zmienił jeszcze niczego na tym komputerze.";
    public const string EmptyFiltered = "Żaden wpis nie pasuje do filtra.";

    public const string DateLabel = "Data";
    public const string ItemLabel = "Pozycja";
    public const string FilterLabel = "Filtr";
    public const string StateLabel = "Stan";
    public const string AccountLabel = "Konto";
    public const string CategoryLabel = "Kategoria";
    public const string AppliedLabel = "Zastosowano";
    public const string EventsLabel = "Przebieg";
    public const string UndoButton = "Cofnij";

    public const string WholeComputer = "cały komputer";
    public const string UnknownAccount = "nieznane";
    public const string AppsCategory = "Aplikacje";
    public const string InstallCategory = "Instalacja";
    public const string OutOfCatalog = "pozycja spoza tego katalogu";

    public const string UndoOutOfCatalog = "Tej pozycji nie ma w katalogu tej wersji programu, więc nie da się jej cofnąć.";
    public const string UndoNotNewest = "Nowszy wpis tej pozycji jest aktywny – cofnięcie dotyczy tamtego wpisu.";

    /// <summary>Wpis instalacji zamiast „Cofnij”: instalacji nie cofa się z programu, aplikację usuwa się na stronie „Aplikacje”.</summary>
    public const string InstallUndoBlocked = "Instalacji nie cofa się z programu – aplikację usuniesz na stronie „Aplikacje”.";

    /// <summary>Link w wpisie instalacji/usunięcia, który prowadzi na stronę „Aplikacje”.</summary>
    public const string GoToAppsLink = "Przejdź do: Aplikacje";

    private const string NotDonePrefix = "Nie wykonano – ";
    private const string NotConfirmedPrefix = "Nie potwierdzono – ";

    private const string RebootDetectMessage = "Zmiana aktywna po restarcie.";

    public static string UndoOtherAccount(string name)
        => $"Ta zmiana dotyczy konta {name}. Żeby ją cofnąć, uruchom program na tym koncie.";

    public static string LoadFailed(string message)
        => $"Nie udało się odczytać historii zmian: {message}";

    public static string State(HistoryState state) => state switch
    {
        HistoryState.Active => "Aktywna",
        HistoryState.PendingReboot => "Czeka na restart",
        HistoryState.UndonePendingReboot => "Cofnięta, czeka na restart",
        HistoryState.Undone => "Cofnięta",
        HistoryState.Aborted => "Przerwana",
        HistoryState.Removed => "Usunięta",
        HistoryState.RemovalNotConfirmed => "Nie potwierdzono usunięcia",
        HistoryState.RemovalFailed => "Usuwanie nie powiodło się",
        HistoryState.Installed => "Zainstalowana",
        HistoryState.InstallNotConfirmed => "Nie potwierdzono instalacji",
        HistoryState.InstallNotDone => "Nie zainstalowano",
        _ => throw new ArgumentOutOfRangeException(nameof(state), state, null),
    };

    /// <summary>Powód bez przedrostka „Nie wykonano – ”/„Nie potwierdzono – ”; null/biały tekst = null.</summary>
    public static string? ShortReason(string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return null;
        }

        foreach (var prefix in new[] { NotDonePrefix, NotConfirmedPrefix })
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
        HistoryKind.AppInstall => "Instalacja aplikacji",
        HistoryKind.AppRemoval => "Usunięcie aplikacji",
        _ => categoryName ?? OutOfCatalog,
    };

    public static string FilterName(HistoryFilter filter) => filter switch
    {
        HistoryFilter.All => "Wszystkie wpisy",
        HistoryFilter.Active => "Aktywne",
        HistoryFilter.PendingReboot => "Czekające na restart",
        HistoryFilter.Inactive => "Cofnięte i przerwane",
        _ => throw new ArgumentOutOfRangeException(nameof(filter), filter, null),
    };

    /// <summary>Tekst zdarzenia journala; <paramref name="message"/> dopisywany po „ – ”.</summary>
    public static string Event(string action, string outcome, string? message)
    {
        var text = BaseEventText(action, outcome);
        return string.IsNullOrEmpty(message) || message == RebootDetectMessage ? text : $"{text} – {message}";
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
        => at.ToLocalTime().ToString("dd.MM.yyyy HH:mm", CultureInfo.InvariantCulture);

    public static string Time(DateTimeOffset at)
        => at.ToLocalTime().ToString("dd.MM.yyyy HH:mm:ss", CultureInfo.InvariantCulture);

    internal static bool TryParseOutcome(string outcome, out TweakOutcome parsed)
        => Enum.TryParse(outcome, ignoreCase: false, out parsed) && Enum.IsDefined(typeof(TweakOutcome), parsed);

    private static string BaseEventText(string action, string outcome)
    {
        if (action == "apply" && outcome == "Started")
        {
            return "Zastosowanie rozpoczęte";
        }

        var known = TryParseOutcome(outcome, out var parsed);
        switch (action)
        {
            case "remove-app":
                return outcome switch
                {
                    "Started" => "Usuwanie rozpoczęte",
                    "Removed" => "Usunięto i sprawdzono",
                    "NotConfirmed" => "Nie potwierdzono usunięcia",
                    "Failed" => "Usuwanie nie powiodło się",
                    _ => $"{action}: {outcome}",
                };
            case "install-app":
                return outcome switch
                {
                    "Started" => "Instalacja rozpoczęta",
                    "Installed" => "Zainstalowano i sprawdzono",
                    "InstalledUnverified" => "Zainstalowano (zgłoszone przez winget)",
                    "AlreadyInstalled" => "Była już zainstalowana",
                    "NotConfirmed" => "Nie potwierdzono instalacji",
                    "Failed" => "Nie wykonano",
                    _ => $"{action}: {outcome}",
                };
            case "apply" when known:
                return parsed switch
                {
                    TweakOutcome.Applied => "Zastosowano",
                    TweakOutcome.PendingReboot => "Zastosowano – zadziała po restarcie",
                    _ => $"Zastosowanie: {GuiText.OutcomeName(parsed)}",
                };
            case "undo" when known:
                return parsed == TweakOutcome.Undone ? "Cofnięto" : $"Cofnięcie: {GuiText.OutcomeName(parsed)}";
            case "detect" when known && parsed == TweakOutcome.Applied:
                return "Po restarcie: zmiana aktywna";
            default:
                return $"{action}: {outcome}";
        }
    }
}
