using System;
using System.Globalization;
using DSOptimizer.Core.Engine;
using DSOptimizer.Core.Localization;

namespace DSOptimizer.Core.Apps;

/// <summary>Teksty strony „Aplikacje”, jej paska serii, panelu szczegółów i okna potwierdzenia usuwania (w bieżącym języku, <see cref="L"/>).</summary>
public static class AppsPageText
{
    public static string PageTitle => L.T("Aplikacje");
    public static string SearchPlaceholder => L.T("Szukaj po nazwie albo wydawcy");
    public static string RefreshButton => L.T("Odśwież listę");
    public static string NotRead => L.T("Lista nie została jeszcze odczytana");
    public static string Reading => L.T("Odczyt listy w toku");
    public static string ProtectedTooltip => L.T("Chroniona – DSOptimizer jej nie usuwa");
    public static string SelectedLine => L.T("Zaznaczona do usunięcia");
    public static string NotSelectedLine => L.T("Nie zaznaczona – kliknij dwukrotnie wiersz, żeby dodać");
    public static string TechnicalHeader => L.T("Szczegóły techniczne");
    public static string EmptyGroup => L.T("Brak aplikacji w tej grupie.");
    public static string EmptySearch => L.T("Żadna aplikacja w tej grupie nie pasuje do wyszukiwania.");

    public static string AllAccounts => L.T("Wszystkie konta");
    public static string UnknownAccount => L.T("nieznane");
    public static string UnknownValue => L.T("nieznana");
    public static string UnknownPublisher => L.T("nieznany");
    public static string Irreversible => L.T("Niemożliwe");

    public static string ConfirmTitle => L.T("Usuń zaznaczone");
    public static string RestorePointFact => L.T("Punkt przywracania");
    public static string RestartFact => L.T("Restart");
    public static string UndoFact => L.T("Cofanie");

    public static string RestorePointRequired => L.T("Tak (wymagany przy programach)");
    public static string RestorePointBySetting => L.T("Tak (według Ustawień)");
    public static string RestorePointSettingOff => L.T("Nie – wyłączony w Ustawieniach");
    public static string RestorePointProtectionOff => L.T("Nie – ochrona systemu wyłączona");
    public static string RestorePointUnknown => L.T("Tak, jeśli się uda – nie udało się sprawdzić ochrony systemu");
    public static string RestartByUninstaller => L.T("Tylko jeśli zgłosi go deinstalator");
    public static string RestartNotNeeded => L.T("Niepotrzebny");

    public static string ProtectionOffWarning => L.T("Ochrona systemu jest wyłączona, więc punkt przywracania nie powstanie.");
    public static string ProtectionOffWithQuestionWarning =>
        L.T("Ochrona systemu jest wyłączona, więc punkt przywracania nie powstanie. Przed pierwszym usunięciem program zapyta, czy kontynuować bez niego.");
    public static string ProtectionUnknownWarning =>
        L.T("Nie udało się sprawdzić, czy ochrona systemu jest włączona. Jeśli punkt przywracania nie powstanie, przed pierwszym usunięciem program zapyta, czy kontynuować bez niego.");

    public static string ContinueWithoutRestorePointBody =>
        L.T("Nic jeszcze nie zostało usunięte. Bez punktu przywracania usuniętego programu nie przywrócisz także przywracaniem systemu Windows.");
    public static string ContinueWithoutRestorePointNote => L.T("„Nie” kończy serię bez usuwania.");
    public static string ContinueButton => L.T("Tak, kontynuuj");
    public static string DeclineButton => L.T("Nie");

    public static string RebootLine => L.T("Wymagany restart, żeby deinstalator dokończył usuwanie.");

    public static string BlockedRefusal => L.T("Chronionej ani zablokowanej aplikacji nie da się usunąć – nic nie zmieniono.");

    /// <summary>Tytuł paska serii strony: zachęta przy 0, inaczej liczba zaznaczonych aplikacji.</summary>
    public static string SelectionBarTitle(int selected)
    {
        if (selected < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(selected), selected, null);
        }

        return selected == 0
            ? L.T("Kliknij dwukrotnie wiersz albo pole wyboru, żeby dodać do usunięcia.")
            : L.P("Zaznaczono {0} aplikację do usunięcia", "Zaznaczono {0} aplikacje do usunięcia", "Zaznaczono {0} aplikacji do usunięcia", selected);
    }

    /// <summary>Druga linia paska: punkt przywracania (wymuszony przy programach, inaczej wg Ustawień) i brak cofania.</summary>
    public static string SelectionBarDetails(bool hasProgram, bool restorePointSetting)
        => hasProgram
            ? L.T("Przed usunięciem programów program utworzy punkt przywracania. Usunięcia nie da się cofnąć.")
            : restorePointSetting
                ? L.T("Przed usunięciem program spróbuje utworzyć punkt przywracania. Usunięcia nie da się cofnąć.")
                : L.T("Bez punktu przywracania. Usunięcia nie da się cofnąć.");

    /// <summary>Podtytuł strony po odczycie, np. „Odczyt listy: 14:05, 21 aplikacji” (czas lokalny).</summary>
    public static string ReadSummary(DateTime localTime, int count)
        => L.F("Odczyt listy: {0}, {1}",
            localTime.ToString("HH:mm", CultureInfo.InvariantCulture),
            L.P("{0} aplikacja", "{0} aplikacje", "{0} aplikacji", count));

    public static string GroupName(AppGroup group) => group switch
    {
        AppGroup.Suggested => L.T("Sugerowane do usunięcia"),
        AppGroup.Other => L.T("Pozostałe"),
        AppGroup.Protected => L.T("Chronione"),
        _ => throw new ArgumentOutOfRangeException(nameof(group), group, null),
    };

    /// <summary>Grupa w panelu szczegółów z rodzajem wpisu listy śmieci, np. „Sugerowane do usunięcia (wersja próbna)”.</summary>
    public static string GroupDetail(AppGroup group, string? bloatGroup)
    {
        var suffix = bloatGroup switch
        {
            "microsoft" => "Microsoft",
            "producent" => L.T("producent komputera"),
            "trial" => L.T("wersja próbna"),
            _ => null,
        };
        return suffix is null ? GroupName(group) : $"{GroupName(group)} ({suffix})";
    }

    /// <summary>Rodzaj w kolumnie listy.</summary>
    public static string KindShort(AppKind kind) => kind == AppKind.Store ? L.T("Sklep") : L.T("Program");

    /// <summary>Rodzaj w panelu szczegółów i w potwierdzeniu.</summary>
    public static string KindLong(AppKind kind) => kind == AppKind.Store ? L.T("Aplikacja ze Sklepu") : L.T("Program");

    /// <summary>Opis pozycji potwierdzenia: rodzaj i wydawca, np. „Aplikacja ze Sklepu, Microsoft Corporation”.</summary>
    public static string Summary(AppKind kind, string? publisher)
        => string.IsNullOrWhiteSpace(publisher) ? KindLong(kind) : KindLong(kind) + ", " + publisher!.Trim();

    /// <summary>Uwaga pozycji programu w potwierdzeniu: jak zostanie uruchomiony deinstalator.</summary>
    public static string ProgramRemoval(UninstallMode mode) => mode switch
    {
        UninstallMode.Interactive => L.T("Deinstalator producenta; jego okno pojawi się na ekranie, program poczeka na koniec."),
        UninstallMode.Quiet => L.T("Cichy deinstalator producenta, bez okna; program poczeka na koniec."),
        UninstallMode.Msi => L.T("Instalator Windows usunie program bez okna; program poczeka na koniec."),
        _ => throw new ArgumentOutOfRangeException(nameof(mode), mode, null),
    };

    /// <summary>„Usuwanie” w panelu szczegółów programu (krótko).</summary>
    public static string ProgramRemovalBrief(UninstallMode mode) => mode switch
    {
        UninstallMode.Interactive => L.T("Deinstalator producenta; jego okno pojawi się na ekranie"),
        UninstallMode.Quiet => L.T("Cichy deinstalator producenta, bez okna"),
        UninstallMode.Msi => L.T("Instalator Windows, bez okna"),
        _ => throw new ArgumentOutOfRangeException(nameof(mode), mode, null),
    };

    /// <summary>
    /// Zakres usunięcia aplikacji ze Sklepu (Ruling R8): konto zalogowanego (z obrazu systemu dla grupy <c>microsoft</c>) albo,
    /// gdy DSOptimizer działa na innym koncie, wszystkie konta i obraz systemu. Każdy wariant to osobne pełne zdanie.
    /// </summary>
    public static string StoreRemoval(string? userName, bool fromImage, bool allUsers)
    {
        if (allUsers)
        {
            return L.T("Dla wszystkich kont na tym komputerze i z obrazu systemu.");
        }

        if (string.IsNullOrWhiteSpace(userName))
        {
            return fromImage
                ? L.T("Dla zalogowanego konta i z obrazu systemu, żeby nie wróciła na nowym koncie.")
                : L.T("Dla zalogowanego konta.");
        }

        return fromImage
            ? L.F("Dla konta {0} i z obrazu systemu, żeby nie wróciła na nowym koncie.", userName!.Trim())
            : L.F("Dla konta {0}.", userName!.Trim());
    }

    /// <summary>Ostrzeżenie potwierdzenia, gdy SID procesu ≠ SID zalogowanego, a w serii jest aplikacja ze Sklepu (Ruling R8, dosłownie).</summary>
    public static string RunsAsOtherAccount(string processAccount, string loggedInAccount)
        => L.F("DSOptimizer działa na koncie {0}, innym niż zalogowane ({1}): aplikacje ze Sklepu zostaną usunięte dla wszystkich kont na tym komputerze i z obrazu systemu.",
            processAccount,
            loggedInAccount);

    /// <summary>Polecenie deinstalacji z planu: pełna ścieżka w cudzysłowie i argumenty (tylko do wyświetlenia).</summary>
    public static string CommandLine(UninstallPlan plan)
    {
        if (plan is null)
        {
            throw new ArgumentNullException(nameof(plan));
        }

        return string.IsNullOrWhiteSpace(plan.Arguments) ? $"\"{plan.FileName}\"" : $"\"{plan.FileName}\" {plan.Arguments}";
    }

    /// <summary>Wynik pozycji serii usuwania w dzienniku: porażka to „Nie udało się” (nie „Błąd”), Ruling R17.</summary>
    public static string OutcomeName(TweakOutcome outcome) => outcome switch
    {
        TweakOutcome.Removed => L.T("Usunięto"),
        TweakOutcome.NotConfirmed => L.T("Nie potwierdzono"),
        TweakOutcome.Skipped => L.T("Pominięto"),
        TweakOutcome.Refused => L.T("Odmowa"),
        TweakOutcome.Installed => L.T("Zainstalowano"),
        TweakOutcome.AlreadyInstalled => L.T("Już zainstalowane"),
        TweakOutcome.InstalledUnverified => L.T("Zainstalowano (zgłoszone przez winget)"),
        _ => FailedStatus,
    };

    public static string FailedStatus => L.T("Nie udało się");

    /// <summary>
    /// Powód porażki bez powtórzonego „Nie udało się – ” (status już to mówi), z wielkiej litery. Przedrostek w bieżącym języku
    /// (wynik powstaje w tym samym przebiegu co ta funkcja); po angielsku „Failed – the app…” → „The app…”.
    /// </summary>
    public static string FailureDetail(string message)
    {
        if (message is null)
        {
            throw new ArgumentNullException(nameof(message));
        }

        var prefix = FailedStatus + " – ";
        if (!message.StartsWith(prefix, StringComparison.Ordinal) || message.Length == prefix.Length)
        {
            return message;
        }

        var rest = message.Substring(prefix.Length);
        return char.ToUpper(rest[0], L.Culture) + rest.Substring(1);
    }

    /// <summary>Blok restartu w wyniku serii usuwania, np. „1 aplikacja dokończy usuwanie po ponownym uruchomieniu komputera.”.</summary>
    public static string RemovalRebootText(int count)
        => L.P("{0} aplikacja dokończy usuwanie po ponownym uruchomieniu komputera.", "{0} aplikacje dokończą usuwanie po ponownym uruchomieniu komputera.", "{0} aplikacji dokończy usuwanie po ponownym uruchomieniu komputera.", count);

    /// <summary>Podsumowanie serii w dzienniku, np. „Usuwanie: 4 z 6 usuniętych.”.</summary>
    public static string BatchSummary(int removed, int total)
        => L.F("Usuwanie: {0} z {1} usuniętych.", removed, total);
}
