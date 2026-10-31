using System;
using System.IO;

namespace DSOptimizer.Windows;

/// <summary>
/// Podniesiony PowerShell 5.1 ładuje moduły też z Dokumentów użytkownika, zapisywalnych bez admina. Ta wartość <c>PSModulePath</c> ma tylko
/// foldery chronione. Kolejność jest celowa (System32 pierwszy, jak nie w wartości maszyny): PowerShell dokleja Dokumenty, gdy zmienna procesu
/// jest równa wartości maszyny, a inną przyjmuje bez zmian (pomiar 2026-10-31, Windows 11, PowerShell 5.1).
/// </summary>
internal static class PowerShellEnvironment
{
    public const string ModulePathVariable = "PSModulePath";

    public static string ModulePath => string.Join(
        ";",
        Path.Combine(Environment.SystemDirectory, @"WindowsPowerShell\v1.0\Modules"),
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), @"WindowsPowerShell\Modules"));
}
