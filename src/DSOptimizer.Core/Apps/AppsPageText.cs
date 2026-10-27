using System;
using System.Globalization;
using DSOptimizer.Core.Engine;
using DSOptimizer.Core.Gui;

namespace DSOptimizer.Core.Apps;

/// <summary>Teksty strony „Aplikacje”, jej paska serii, panelu szczegółów i okna potwierdzenia usuwania (PL, render Tasku 1).</summary>
public static class AppsPageText
{
    public const string PageTitle = "Aplikacje";
    public const string SearchPlaceholder = "Szukaj po nazwie albo wydawcy";
    public const string RefreshButton = "Odśwież listę";
    public const string NotRead = "Lista nie została jeszcze odczytana";
    public const string Reading = "Odczyt listy w toku";
    public const string ProtectedTooltip = "Chroniona – DSOptimizer jej nie usuwa";
    public const string SelectedLine = "Zaznaczona do usunięcia";
    public const string NotSelectedLine = "Nie zaznaczona – kliknij dwukrotnie wiersz, żeby dodać";
    public const string TechnicalHeader = "Szczegóły techniczne";
    public const string EmptyGroup = "Brak aplikacji w tej grupie.";
    public const string EmptySearch = "Żadna aplikacja w tej grupie nie pasuje do wyszukiwania.";

    public const string AllAccounts = "Wszystkie konta";
    public const string UnknownAccount = "nieznane";
    public const string UnknownValue = "nieznana";
    public const string UnknownPublisher = "nieznany";
    public const string Irreversible = "Niemożliwe";

    public const string ConfirmTitle = "Usuń zaznaczone";
    public const string RestorePointFact = "Punkt przywracania";
    public const string RestartFact = "Restart";
    public const string UndoFact = "Cofanie";

    public const string RestorePointRequired = "Tak (wymagany przy programach)";
    public const string RestorePointBySetting = "Tak (według Ustawień)";
    public const string RestorePointSettingOff = "Nie – wyłączony w Ustawieniach";
    public const string RestorePointProtectionOff = "Nie – ochrona systemu wyłączona";
    public const string RestorePointUnknown = "Tak, jeśli się uda – nie udało się sprawdzić ochrony systemu";
    public const string RestartByUninstaller = "Tylko jeśli zgłosi go deinstalator";
    public const string RestartNotNeeded = "Niepotrzebny";

    public const string ProtectionOffWarning = "Ochrona systemu jest wyłączona, więc punkt przywracania nie powstanie.";
    public const string ProtectionOffWithQuestionWarning =
        "Ochrona systemu jest wyłączona, więc punkt przywracania nie powstanie. Przed pierwszym usunięciem program zapyta, czy kontynuować bez niego.";
    public const string ProtectionUnknownWarning =
        "Nie udało się sprawdzić, czy ochrona systemu jest włączona. Jeśli punkt przywracania nie powstanie, przed pierwszym usunięciem program zapyta, czy kontynuować bez niego.";

    public const string ContinueWithoutRestorePointBody =
        "Nic jeszcze nie zostało usunięte. Bez punktu przywracania usuniętego programu nie przywrócisz także przywracaniem systemu Windows.";
    public const string ContinueWithoutRestorePointNote = "„Nie” kończy serię bez usuwania.";
    public const string ContinueButton = "Tak, kontynuuj";
    public const string DeclineButton = "Nie";

    public const string RebootLine = "Wymagany restart, żeby deinstalator dokończył usuwanie.";

    public const string BlockedRefusal = "Chronionej ani zablokowanej aplikacji nie da się usunąć – nic nie zmieniono.";

    /// <summary>Tytuł paska serii strony: zachęta przy 0, inaczej liczba zaznaczonych aplikacji.</summary>
    public static string SelectionBarTitle(int selected)
    {
        if (selected < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(selected), selected, null);
        }

        return selected == 0
            ? "Kliknij dwukrotnie wiersz albo pole wyboru, żeby dodać do usunięcia."
            : "Zaznaczono " + GuiText.Count(selected, "aplikację", "aplikacje", "aplikacji") + " do usunięcia";
    }

    /// <summary>Druga linia paska: punkt przywracania (wymuszony przy programach, inaczej wg Ustawień) i brak cofania.</summary>
    public static string SelectionBarDetails(bool hasProgram, bool restorePointSetting)
    {
        var restorePoint = hasProgram
            ? "Przed usunięciem programów program utworzy punkt przywracania."
            : restorePointSetting
                ? "Przed usunięciem program spróbuje utworzyć punkt przywracania."
                : "Bez punktu przywracania.";
        return restorePoint + " Usunięcia nie da się cofnąć.";
    }

    /// <summary>Podtytuł strony po odczycie, np. „Odczyt listy: 14:05, 21 aplikacji” (czas lokalny).</summary>
    public static string ReadSummary(DateTime localTime, int count)
        => "Odczyt listy: " + localTime.ToString("HH:mm", CultureInfo.InvariantCulture) + ", "
            + GuiText.Count(count, "aplikacja", "aplikacje", "aplikacji");

    public static string GroupName(AppGroup group) => group switch
    {
        AppGroup.Suggested => "Sugerowane do usunięcia",
        AppGroup.Other => "Pozostałe",
        AppGroup.Protected => "Chronione",
        _ => throw new ArgumentOutOfRangeException(nameof(group), group, null),
    };

    /// <summary>Grupa w panelu szczegółów z rodzajem wpisu listy śmieci, np. „Sugerowane do usunięcia (wersja próbna)”.</summary>
    public static string GroupDetail(AppGroup group, string? bloatGroup)
    {
        var suffix = bloatGroup switch
        {
            "microsoft" => "Microsoft",
            "producent" => "producent komputera",
            "trial" => "wersja próbna",
            _ => null,
        };
        return suffix is null ? GroupName(group) : $"{GroupName(group)} ({suffix})";
    }

    /// <summary>Rodzaj w kolumnie listy.</summary>
    public static string KindShort(AppKind kind) => kind == AppKind.Store ? "Sklep" : "Program";

    /// <summary>Rodzaj w panelu szczegółów i w potwierdzeniu.</summary>
    public static string KindLong(AppKind kind) => kind == AppKind.Store ? "Aplikacja ze Sklepu" : "Program";

    /// <summary>Opis pozycji potwierdzenia: rodzaj i wydawca, np. „Aplikacja ze Sklepu, Microsoft Corporation”.</summary>
    public static string Summary(AppKind kind, string? publisher)
        => string.IsNullOrWhiteSpace(publisher) ? KindLong(kind) : KindLong(kind) + ", " + publisher!.Trim();

    /// <summary>Uwaga pozycji programu w potwierdzeniu: jak zostanie uruchomiony deinstalator.</summary>
    public static string ProgramRemoval(UninstallMode mode) => mode switch
    {
        UninstallMode.Interactive => "Deinstalator producenta; jego okno pojawi się na ekranie, program poczeka na koniec.",
        UninstallMode.Quiet => "Cichy deinstalator producenta, bez okna; program poczeka na koniec.",
        UninstallMode.Msi => "Instalator Windows usunie program bez okna; program poczeka na koniec.",
        _ => throw new ArgumentOutOfRangeException(nameof(mode), mode, null),
    };

    /// <summary>„Usuwanie” w panelu szczegółów programu (krótko).</summary>
    public static string ProgramRemovalBrief(UninstallMode mode) => mode switch
    {
        UninstallMode.Interactive => "Deinstalator producenta; jego okno pojawi się na ekranie",
        UninstallMode.Quiet => "Cichy deinstalator producenta, bez okna",
        UninstallMode.Msi => "Instalator Windows, bez okna",
        _ => throw new ArgumentOutOfRangeException(nameof(mode), mode, null),
    };

    /// <summary>
    /// Zakres usunięcia aplikacji ze Sklepu (Ruling R8): konto zalogowanego (z obrazu systemu dla grupy <c>microsoft</c>) albo,
    /// gdy DSOptimizer działa na innym koncie, wszystkie konta i obraz systemu.
    /// </summary>
    public static string StoreRemoval(string? userName, bool fromImage, bool allUsers)
    {
        if (allUsers)
        {
            return "Dla wszystkich kont na tym komputerze i z obrazu systemu.";
        }

        var account = string.IsNullOrWhiteSpace(userName) ? "Dla zalogowanego konta" : "Dla konta " + userName!.Trim();
        return fromImage ? account + " i z obrazu systemu, żeby nie wróciła na nowym koncie." : account + ".";
    }

    /// <summary>Ostrzeżenie potwierdzenia, gdy SID procesu ≠ SID zalogowanego, a w serii jest aplikacja ze Sklepu (Ruling R8, dosłownie).</summary>
    public static string RunsAsOtherAccount(string processAccount, string loggedInAccount)
        => $"DSOptimizer działa na koncie {processAccount}, innym niż zalogowane ({loggedInAccount}): "
            + "aplikacje ze Sklepu zostaną usunięte dla wszystkich kont na tym komputerze i z obrazu systemu.";

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
        TweakOutcome.Removed => "Usunięto",
        TweakOutcome.NotConfirmed => "Nie potwierdzono",
        TweakOutcome.Skipped => "Pominięto",
        TweakOutcome.Refused => "Odmowa",
        TweakOutcome.Installed => "Zainstalowano",
        TweakOutcome.AlreadyInstalled => "Już zainstalowane",
        TweakOutcome.InstalledUnverified => "Zainstalowano (zgłoszone przez winget)",
        _ => FailedStatus,
    };

    public const string FailedStatus = "Nie udało się";

    /// <summary>Powód porażki bez powtórzonego „Nie udało się – ” (status już to mówi), z wielkiej litery.</summary>
    public static string FailureDetail(string message)
    {
        if (message is null)
        {
            throw new ArgumentNullException(nameof(message));
        }

        const string prefix = FailedStatus + " – ";
        if (!message.StartsWith(prefix, StringComparison.Ordinal) || message.Length == prefix.Length)
        {
            return message;
        }

        var rest = message.Substring(prefix.Length);
        return char.ToUpper(rest[0], CultureInfo.GetCultureInfo("pl-PL")) + rest.Substring(1);
    }

    /// <summary>Blok restartu w wyniku serii usuwania, np. „1 aplikacja dokończy usuwanie po ponownym uruchomieniu komputera.”.</summary>
    public static string RemovalRebootText(int count)
        => GuiText.Count(count, "aplikacja dokończy", "aplikacje dokończą", "aplikacji dokończy") + " usuwanie po ponownym uruchomieniu komputera.";

    /// <summary>Podsumowanie serii w dzienniku, np. „Usuwanie: 4 z 6 usuniętych.”.</summary>
    public static string BatchSummary(int removed, int total)
        => string.Format(CultureInfo.InvariantCulture, "Usuwanie: {0} z {1} usuniętych.", removed, total);
}
