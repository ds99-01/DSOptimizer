using System.Collections.Generic;
using DSOptimizer.Core.Apps;

namespace DSOptimizer.Core.Abstractions;

/// <summary>Wynik odczytu zainstalowanych aplikacji. Błąd jednego źródła (AppX albo rejestr) trafia do <see cref="Warnings"/>, a reszta się wyświetla.</summary>
public sealed record InstalledAppsReading(IReadOnlyList<InstalledApp> Apps, IReadOnlyList<string> Warnings);

/// <summary>Odczyt zainstalowanych aplikacji z prawdziwego systemu (pakiety AppX i wpisy Uninstall).</summary>
public interface IInstalledAppsReader
{
    /// <summary>Czyta wszystkie widoczne źródła. Pakiety AppX dotyczą konta <paramref name="userSid"/>, nie konta procesu.</summary>
    InstalledAppsReading Read(string userSid);

    /// <summary>
    /// Niezależny, świeży odczyt: czy aplikacja nadal jest w systemie. AppX według <paramref name="scope"/>: pakiet o tej nazwie dla
    /// <paramref name="userSid"/> albo dla dowolnego konta (<see cref="AppPresence.Present"/>), a przy zakresach „…AndImage” także
    /// wpis w obrazie systemu (sam obraz = <see cref="AppPresence.ImageOnly"/>).
    /// Win32: istnieje klucz <c>KeyPath</c> (zakres bez znaczenia; niezaładowany <c>HKU\&lt;SID&gt;</c> innego konta daje
    /// <see cref="AppPresence.Absent"/>, więc to nie jest dowód zniknięcia – takie wiersze są zablokowane do usuwania).
    /// Gdy odczytu nie da się wykonać: <see cref="AppPresence.Unknown"/> – brak dowodu zniknięcia to nie „usunięte” (R19).
    /// </summary>
    AppPresence ReadPresence(InstalledApp app, string userSid, AppPresenceScope scope);
}
