namespace DSOptimizer.Core.Repair;

/// <summary>
/// Treść skryptów PowerShell kroków czyszczenia dysku (uruchamiane przez <c>IScriptRunner</c>, jak skrypty katalogu i
/// <c>AppxScript</c>). Mechanizm z pomiaru na VM 26H2 (Task 2, pkt 4b): <c>Delete-DeliveryOptimizationCache -Force</c> (funkcja
/// modułu <c>DeliveryOptimization</c> 1.0.3.0) usuwa pamięć podręczną Optymalizacji dostarczania; pliki zadań, które Windows
/// Update właśnie pobiera, zostają. Komunikaty cmdletu idą strumieniem hosta, więc wynik niesie własny znacznik w ostatniej linii.
/// </summary>
public static class DiskCleanupScripts
{
    /// <summary>Ostatnia linia wyjścia po udanym wywołaniu cmdletu.</summary>
    public const string DoneMarker = "DSO:DO-WYKONANO";

    /// <summary>Ostatnia linia wyjścia, gdy w systemie nie ma polecenia <c>Delete-DeliveryOptimizationCache</c> (krok pominięty).</summary>
    public const string MissingMarker = "DSO:DO-BRAK-POLECENIA";

    /// <summary>
    /// Sprawdzenie polecenia przez helper <c>Test-DsoDeliveryOptimizationCmdlet</c> (testy definiują go wcześniej jako mock; skrypt
    /// pomija wtedy własną definicję), potem <c>Delete-DeliveryOptimizationCache -Force</c>. Błąd cmdletu = wyjątek (kod 1, stderr).
    /// </summary>
    public const string DeliveryOptimization = """
        if (-not (Get-Command Test-DsoDeliveryOptimizationCmdlet -ErrorAction SilentlyContinue)) {
            # Jedyne miejsce sprawdzenia, czy system ma polecenie (moduł DeliveryOptimization ładuje się sam przy Get-Command).
            function Test-DsoDeliveryOptimizationCmdlet {
                return [bool](Get-Command -Name Delete-DeliveryOptimizationCache -ErrorAction SilentlyContinue)
            }
        }
        if (Test-DsoDeliveryOptimizationCmdlet) {
            Delete-DeliveryOptimizationCache -Force | Out-Null
            'DSO:DO-WYKONANO'
        }
        else {
            'DSO:DO-BRAK-POLECENIA'
        }
        """;
}
