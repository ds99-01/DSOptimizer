using System;
using System.Collections.Generic;
using DSOptimizer.Core.Abstractions;

namespace DSOptimizer.Core.Autostart;

/// <summary>
/// Powiązanie pozycji z procesem z pomiaru Windows: proces o tej samej pełnej ścieżce co plik pozycji, uruchomiony przez program startowy
/// Windows (rodzic <c>explorer.exe</c> – Run i foldery, <c>runonce.exe</c> – Run 32-bit, <c>sihost.exe</c> – Sklep). Procesy potomne o tej samej
/// ścieżce się nie liczą; kilka pasujących = najwcześniejszy start.
/// </summary>
public static class StartupMeasurements
{
    private static readonly string[] Launchers = { "explorer.exe", "runonce.exe", "sihost.exe" };

    private static readonly char[] Separators = { '\\', '/' };

    /// <summary>
    /// Pomiar pozycji z procesów jednego pliku albo null („nie zmierzono”). Wymaga ustalonej ścieżki pliku (<see cref="AutostartEntry.FilePath"/>)
    /// i procesu z czasem procesora i odczytem z dysku. Interpreter (<see cref="AutostartEntry.IsInterpreter"/>) dodatkowo wymaga zgodnej linii
    /// poleceń po <see cref="AutostartCommand.NormalizeCommandLine"/>. Alias aplikacji (<c>…\Microsoft\WindowsApps\…</c>) ma inną ścieżkę niż
    /// proces pakietu, więc zostaje bez pomiaru.
    /// </summary>
    public static StartupMeasurement? Find(
        AutostartEntry entry,
        IReadOnlyList<StartupProcess> processes,
        StartupInfoFile file,
        bool fromEarlierStart,
        UserEnvironment environment)
    {
        if (entry is null)
        {
            throw new ArgumentNullException(nameof(entry));
        }

        if (processes is null)
        {
            throw new ArgumentNullException(nameof(processes));
        }

        if (file is null)
        {
            throw new ArgumentNullException(nameof(file));
        }

        if (environment is null)
        {
            throw new ArgumentNullException(nameof(environment));
        }

        if (entry.FilePath is null)
        {
            return null;
        }

        string? commandLine = null;
        if (entry.IsInterpreter)
        {
            commandLine = AutostartCommand.NormalizeCommandLine(entry.Command, environment);
            if (commandLine.Length == 0)
            {
                return null;
            }
        }

        StartupProcess? best = null;
        foreach (var process in processes)
        {
            if (process.CpuMicroseconds is null
                || process.DiskBytes is null
                || !string.Equals(process.Name, entry.FilePath, StringComparison.OrdinalIgnoreCase)
                || !IsLauncher(process.ParentName)
                || (commandLine is not null
                    && !string.Equals(AutostartCommand.NormalizeCommandLine(process.CommandLine, environment), commandLine, StringComparison.Ordinal)))
            {
                continue;
            }

            if (best is null || StartsEarlier(process, best))
            {
                best = process;
            }
        }

        return best is null ? null : new StartupMeasurement(best.CpuMicroseconds!.Value, best.DiskBytes!.Value, file.WrittenAt, fromEarlierStart);
    }

    private static bool IsLauncher(string? parentName)
    {
        if (string.IsNullOrEmpty(parentName))
        {
            return false;
        }

        var name = parentName!.Substring(parentName.LastIndexOfAny(Separators) + 1);
        return Array.Exists(Launchers, l => string.Equals(l, name, StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>Wcześniejszy start wygrywa; proces bez czasu startu przegrywa z każdym z czasem, a przy remisie zostaje pierwszy z pliku.</summary>
    private static bool StartsEarlier(StartupProcess candidate, StartupProcess best)
        => candidate.StartTime is { } start && (best.StartTime is null || start < best.StartTime.Value);
}
