namespace DSOptimizer.Core.Apps;

/// <summary>Teksty strony „Aplikacje” i wyników usuwania (PL).</summary>
public static class AppsText
{
    public const string UserUncertain = "Nie udało się ustalić zalogowanego użytkownika.";

    public const string NoSafeCommand = "Nie da się bezpiecznie uruchomić deinstalatora.";

    public const string IrreversibleWarning =
        "Aplikacje ze Sklepu zainstalujesz z powrotem ze Sklepu; programów nie da się przywrócić z poziomu DSOptimizera.";

    public const string NotConfirmed = "Nie potwierdzono – deinstalator nadal działa.";

    public const string AlreadyGone = "Nie było jej już na komputerze.";

    public const string ContinueWithoutRestorePoint = "Kontynuować bez punktu przywracania?";

    /// <summary>Ostrzeżenie usunięcia w trybie <see cref="StoreRemovalScope.AllUsers"/> (Ruling R8).</summary>
    public const string RemovedForAllUsers = "Usunięta dla wszystkich kont na tym komputerze.";

    /// <summary>Wynik „Nie udało się”: po próbie aplikacja nadal jest na komputerze.</summary>
    public const string StillPresent = "Nie udało się – aplikacja nadal jest na komputerze.";

    /// <summary>Wynik „Nie potwierdzono”: deinstalator zwrócił 3010/1641, a wpis jeszcze jest (Ruling R12).</summary>
    public const string NotConfirmedUntilReboot = "Deinstalator dokończy usuwanie po ponownym uruchomieniu komputera.";

    /// <summary>Wynik „Nie potwierdzono”: interaktywny deinstalator skończył, a wpis został (użytkownik mógł nie dokończyć).</summary>
    public const string InteractiveMayNotHaveFinished = "Deinstalator mógł nie dokończyć – sprawdź listę po odświeżeniu.";

    /// <summary>
    /// Wynik „Nie potwierdzono”: cichy deinstalator zakończył się kodem 0, a wpis został po oknie weryfikacji (R18; Inno Setup
    /// usuwa wpis na końcu, często po więcej niż 60 s).
    /// </summary>
    public const string QuietMayFinishInBackground = "Deinstalator mógł dokończyć w tle – sprawdź listę po odświeżeniu.";

    /// <summary>Wynik „Nie potwierdzono”: po próbie nie udało się odczytać, czy aplikacja jest (np. limit odczytu obrazu, R19).</summary>
    public const string PresenceUnknown = "Nie udało się sprawdzić, czy aplikacja zniknęła.";

    /// <summary>Wynik „Nie udało się”: aplikacja ze Sklepu zniknęła z konta (kont), ale została w obrazie systemu (R19).</summary>
    public const string LeftInImage = "Usunięta z konta, ale została w obrazie systemu (wróci na nowym koncie).";

    /// <summary>Wynik „Nie potwierdzono”: sprawdzanie przerwane („Zatrzymaj” albo anulowanie), zanim aplikacja zniknęła.</summary>
    public const string VerificationInterrupted = "Nie potwierdzono – sprawdzanie przerwane, sprawdź listę po odświeżeniu.";

    /// <summary>Powód przerwania serii, gdy punkt przywracania nie powstał, a użytkownik nie zgodził się kontynuować.</summary>
    public const string AbortedWithoutRestorePoint = "Seria przerwana przed pierwszym usunięciem – bez punktu przywracania.";

    /// <summary>Wynik „Nie potwierdzono” dla aplikacji ze Sklepu, gdy skrypt usuwania przerwał limit czasu albo anulowanie.</summary>
    public static string StoreRemovalInterrupted(string? reason)
        => string.IsNullOrWhiteSpace(reason)
            ? "Nie potwierdzono – usuwanie aplikacji ze Sklepu przerwane."
            : "Nie potwierdzono – usuwanie aplikacji ze Sklepu przerwane: " + reason!.Trim().TrimEnd('.') + ".";

    /// <summary>Ostrzeżenie z adresem oficjalnego narzędzia producenta (tylko tekst, program niczego nie pobiera).</summary>
    public static string CleanupHint(string url) => "Jeśli zostały resztki, użyj narzędzia producenta: " + url;

    /// <summary>Kod wyjścia deinstalatora inny niż 0/3010/1641 przy aplikacji, która zniknęła.</summary>
    public static string UninstallerExitCode(int code)
        => "Deinstalator zakończył się kodem " + code.ToString(System.Globalization.CultureInfo.InvariantCulture) + ".";

    /// <summary>Błąd zgłoszony przez próbę usunięcia (skrypt, start deinstalatora), dopisywany do wyniku.</summary>
    public static string RemovalError(string reason) => "Błąd usuwania: " + reason;

    /// <summary>Powód blokady programu zainstalowanego na innym koncie niż konto procesu.</summary>
    public static string OtherAccount(string? name)
        => string.IsNullOrWhiteSpace(name)
            ? "Uruchom DSOptimizer na koncie, które to zainstalowało, żeby to usunąć."
            : "Uruchom DSOptimizer na koncie " + name + ", żeby to usunąć.";

    /// <summary>Ostrzeżenie: nie udało się odczytać aplikacji ze Sklepu (reszta listy się wyświetla).</summary>
    public static string AppxReadFailed(string reason)
        => "Nie udało się odczytać aplikacji ze Sklepu. " + reason;

    /// <summary>Ostrzeżenie: nie udało się odczytać programów z rejestru (reszta listy się wyświetla).</summary>
    public static string RegistryReadFailed(string reason)
        => "Nie udało się odczytać programów z rejestru. " + reason;

    /// <summary>Ostrzeżenie: część wpisów rejestru była nieczytelna i została pominięta.</summary>
    public static string RegistryKeysSkipped(int count)
        => "Pominięto nieczytelne wpisy programów w rejestrze: " + count.ToString(System.Globalization.CultureInfo.InvariantCulture) + ".";
}
