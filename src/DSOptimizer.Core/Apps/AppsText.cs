using DSOptimizer.Core.Localization;

namespace DSOptimizer.Core.Apps;

/// <summary>Teksty strony „Aplikacje” i wyników usuwania (w bieżącym języku, <see cref="L"/>).</summary>
public static class AppsText
{
    public static string UserUncertain => L.T("Nie udało się ustalić zalogowanego użytkownika.");

    public static string NoSafeCommand => L.T("Nie da się bezpiecznie uruchomić deinstalatora.");

    public static string IrreversibleWarning =>
        L.T("Aplikacje ze Sklepu zainstalujesz z powrotem ze Sklepu; programów nie da się przywrócić z poziomu DSOptimizera.");

    public static string NotConfirmed => L.T("Nie potwierdzono – deinstalator nadal działa.");

    public static string AlreadyGone => L.T("Nie było jej już na komputerze.");

    public static string ContinueWithoutRestorePoint => L.T("Kontynuować bez punktu przywracania?");

    /// <summary>Ostrzeżenie usunięcia w trybie <see cref="StoreRemovalScope.AllUsers"/> (Ruling R8).</summary>
    public static string RemovedForAllUsers => L.T("Usunięta dla wszystkich kont na tym komputerze.");

    /// <summary>Wynik „Nie udało się”: po próbie aplikacja nadal jest na komputerze.</summary>
    public static string StillPresent => L.T("Nie udało się – aplikacja nadal jest na komputerze.");

    /// <summary>Wynik „Nie potwierdzono”: deinstalator zwrócił 3010/1641, a wpis jeszcze jest (Ruling R12).</summary>
    public static string NotConfirmedUntilReboot => L.T("Deinstalator dokończy usuwanie po ponownym uruchomieniu komputera.");

    /// <summary>Wynik „Nie potwierdzono”: interaktywny deinstalator skończył, a wpis został (użytkownik mógł nie dokończyć).</summary>
    public static string InteractiveMayNotHaveFinished => L.T("Deinstalator mógł nie dokończyć – sprawdź listę po odświeżeniu.");

    /// <summary>
    /// Wynik „Nie potwierdzono”: cichy deinstalator zakończył się kodem 0, a wpis został po oknie weryfikacji (R18; Inno Setup
    /// usuwa wpis na końcu, często po więcej niż 60 s).
    /// </summary>
    public static string QuietMayFinishInBackground => L.T("Deinstalator mógł dokończyć w tle – sprawdź listę po odświeżeniu.");

    /// <summary>Wynik „Nie potwierdzono”: po próbie nie udało się odczytać, czy aplikacja jest (np. limit odczytu obrazu, R19).</summary>
    public static string PresenceUnknown => L.T("Nie udało się sprawdzić, czy aplikacja zniknęła.");

    /// <summary>Wynik „Nie udało się”: aplikacja ze Sklepu zniknęła z konta (kont), ale została w obrazie systemu (R19).</summary>
    public static string LeftInImage => L.T("Usunięta z konta, ale została w obrazie systemu (wróci na nowym koncie).");

    /// <summary>Wynik „Nie potwierdzono”: sprawdzanie przerwane („Zatrzymaj” albo anulowanie), zanim aplikacja zniknęła.</summary>
    public static string VerificationInterrupted => L.T("Nie potwierdzono – sprawdzanie przerwane, sprawdź listę po odświeżeniu.");

    /// <summary>Powód przerwania serii, gdy punkt przywracania nie powstał, a użytkownik nie zgodził się kontynuować.</summary>
    public static string AbortedWithoutRestorePoint => L.T("Seria przerwana przed pierwszym usunięciem – bez punktu przywracania.");

    /// <summary>Wynik „Nie potwierdzono” dla aplikacji ze Sklepu, gdy skrypt usuwania przerwał limit czasu albo anulowanie.</summary>
    public static string StoreRemovalInterrupted(string? reason)
        => string.IsNullOrWhiteSpace(reason)
            ? L.T("Nie potwierdzono – usuwanie aplikacji ze Sklepu przerwane.")
            : L.F("Nie potwierdzono – usuwanie aplikacji ze Sklepu przerwane: {0}.", reason!.Trim().TrimEnd('.'));

    /// <summary>Ostrzeżenie z adresem oficjalnego narzędzia producenta (tylko tekst, program niczego nie pobiera).</summary>
    public static string CleanupHint(string url) => L.F("Jeśli zostały resztki, użyj narzędzia producenta: {0}", url);

    /// <summary>Kod wyjścia deinstalatora inny niż 0/3010/1641 przy aplikacji, która zniknęła (liczba bez grupowania, jak dawniej).</summary>
    public static string UninstallerExitCode(int code)
        => L.F("Deinstalator zakończył się kodem {0}.", code.ToString(System.Globalization.CultureInfo.InvariantCulture));

    /// <summary>Błąd zgłoszony przez próbę usunięcia (skrypt, start deinstalatora), dopisywany do wyniku.</summary>
    public static string RemovalError(string reason) => L.F("Błąd usuwania: {0}", reason);

    /// <summary>Powód blokady programu zainstalowanego na innym koncie niż konto procesu.</summary>
    public static string OtherAccount(string? name)
        => string.IsNullOrWhiteSpace(name)
            ? L.T("Uruchom DSOptimizer na koncie, które to zainstalowało, żeby to usunąć.")
            : L.F("Uruchom DSOptimizer na koncie {0}, żeby to usunąć.", name);

    /// <summary>Ostrzeżenie: nie udało się odczytać aplikacji ze Sklepu (reszta listy się wyświetla).</summary>
    public static string AppxReadFailed(string reason)
        => L.F("Nie udało się odczytać aplikacji ze Sklepu. {0}", reason);

    /// <summary>Ostrzeżenie: nie udało się odczytać programów z rejestru (reszta listy się wyświetla).</summary>
    public static string RegistryReadFailed(string reason)
        => L.F("Nie udało się odczytać programów z rejestru. {0}", reason);

    /// <summary>Błąd odczytu obecności pakietu: wynik skryptu (<see cref="AppxScript.PresenceBody"/>) nie jest poprawnym JSON-em.</summary>
    public static string PresenceResultInvalid(string reason) => L.F("Zły wynik odczytu obecności AppX: {0}", reason);

    /// <summary>Błąd odczytu obecności pakietu: w wyniku skryptu brakuje pola logicznego.</summary>
    public static string PresenceFieldMissing(string field) => L.F("Brak pola {0} w wyniku odczytu obecności AppX.", field);

    /// <summary>Ostrzeżenie: część wpisów rejestru była nieczytelna i została pominięta.</summary>
    public static string RegistryKeysSkipped(int count)
        => L.F("Pominięto nieczytelne wpisy programów w rejestrze: {0}.", count.ToString(System.Globalization.CultureInfo.InvariantCulture));
}
