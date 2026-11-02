using System.Collections.Generic;

namespace DSOptimizer.Core.Autostart;

/// <summary>
/// Ścieżki zapisu „od zera” (brak wartości, brak kluczy) potwierdzone pomiarem na VM 26H2 Pro i Home: realny skutek po restarcie,
/// bez wcześniejszego otwarcia Menedżera zadań i Ustawień. Dowód: <c>docs/zrodla/2026-11-02-autostart-pomiar.md</c>, sekcja 8
/// („Zapis od zera”). Ścieżka spoza tej listy ma przełączanie wyłączone, dopóki pomiar jej nie potwierdzi.
/// </summary>
public static class AutostartEvidence
{
    private static readonly AutostartSource[] Confirmed =
    {
        AutostartSource.RunUser,
        AutostartSource.RunMachine,
        AutostartSource.Run32,
        AutostartSource.FolderUser,
        AutostartSource.FolderCommon,
    };

    /// <summary>
    /// Źródła <c>StartupApproved</c> z potwierdzonym zapisem od zera (wyłączenie: utworzenie brakujących kluczy i wartości
    /// <c>03 00 00 00</c> + FILETIME). Zadanie startowe Sklepu nie należy do tej listy – ma dwie osobne flagi niżej.
    /// </summary>
    public static IReadOnlyCollection<AutostartSource> WriteFromZeroConfirmed => Confirmed;

    /// <summary>
    /// Zadanie startowe, klucz nie istnieje (<see cref="StartupTaskRead.KeyExists"/> = false, manifest <c>Enabled="false"</c>): utworzenie
    /// klucza od zera ze stanem włączenia działa. Wyłączenie od zera (manifest <c>Enabled="true"</c>) i klucz bez <c>State</c> nie były mierzone.
    /// </summary>
    public static bool StartupTaskKeyAbsentEnable { get; } = true;

    /// <summary>
    /// Zadanie startowe, klucz utworzony przez system ze <c>State</c> 0 (manifest <c>Enabled="false"</c>): włączenie, wyłączenie i powrót
    /// do stanu systemu działają. Ten sam stan przy manifeście <c>Enabled="true"</c> nie był mierzony.
    /// </summary>
    public static bool StartupTaskState0Enable { get; } = true;
}
