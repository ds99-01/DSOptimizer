namespace DSOptimizer.Core.Apps;

/// <summary>
/// Zakres usunięcia pakietu AppX (Ruling R8): <see cref="CurrentUser"/>, gdy SID procesu = SID zalogowanego
/// (<c>-User &lt;SID&gt;</c>); <see cref="AllUsers"/>, gdy to inne konto (<c>-AllUsers</c>: wszystkie konta i obraz systemu).
/// <c>-User &lt;inny SID&gt;</c> nie jest używane nigdy (zostawia „pending removal”, pomiar VM).
/// </summary>
public enum StoreRemovalScope
{
    CurrentUser,
    AllUsers,
}

/// <summary>Co sprawdza odczyt obecności aplikacji. Dla programów Win32 zakres nie ma znaczenia (klucz Uninstall).</summary>
public enum AppPresenceScope
{
    /// <summary>AppX: pakiet zainstalowany dla konta zalogowanego (<c>Get-AppxPackage -User</c>).</summary>
    User,

    /// <summary>AppX: pakiet dla konta zalogowanego albo w obrazie systemu (<c>Get-AppxProvisionedPackage -Online</c>).</summary>
    UserAndImage,

    /// <summary>AppX: pakiet dla dowolnego konta (<c>Get-AppxPackage -AllUsers</c>) albo w obrazie systemu.</summary>
    AllUsersAndImage,
}

/// <summary>
/// Wynik świeżego odczytu obecności aplikacji (Ruling R19). Tylko <see cref="Absent"/> jest dowodem zniknięcia; błąd odczytu to
/// <see cref="Unknown"/>, nigdy „zniknęła”.
/// </summary>
public enum AppPresence
{
    /// <summary>Jest: pakiet AppX dla konta (kont) albo klucz Uninstall programu.</summary>
    Present,

    /// <summary>Nie ma jej nigdzie, gdzie sprawdzał odczyt (dowód zniknięcia).</summary>
    Absent,

    /// <summary>AppX: nie ma jej dla konta (kont), ale została w obrazie systemu (wróci na nowym koncie).</summary>
    ImageOnly,

    /// <summary>Odczytu nie dało się wykonać – nie wiadomo, czy jest.</summary>
    Unknown,
}
