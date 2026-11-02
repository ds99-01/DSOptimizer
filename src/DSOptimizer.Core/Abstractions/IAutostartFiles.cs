using System.Collections.Generic;
using DSOptimizer.Core.Autostart;

namespace DSOptimizer.Core.Abstractions;

/// <summary>
/// Cel skrótu <c>.lnk</c> tak, jak jest zapisany w skrócie: ścieżka surowa (nierozwinięta, np. <c>%APPDATA%\…</c>), argumenty i znacznik
/// skrótu MSI „advertised” (bez jawnej ścieżki celu).
/// </summary>
public sealed record ShortcutInfo(string? TargetPath, string? Arguments, bool Advertised);

/// <summary>Pola wersji pliku (zasób <c>VERSIONINFO</c>); brak pola = null.</summary>
public sealed record FileDetails(string? CompanyName, string? FileDescription, string? ProductName);

/// <summary>
/// Pliki autostartu – wyłącznie odczyt (pliki otwierane do odczytu z <c>FileShare.ReadWrite | FileShare.Delete</c>, nic nie jest
/// zapisywane ani blokowane).
/// </summary>
public interface IAutostartFiles
{
    /// <summary>
    /// Nazwy plików folderu (same nazwy z rozszerzeniem, bez podfolderów), w kolejności systemu. Brak folderu = pusta lista; brak dostępu
    /// albo błąd wejścia/wyjścia = <see cref="Errors.OperationException"/>.
    /// </summary>
    IReadOnlyList<string> ListFolder(string folder);

    /// <summary>Cel skrótu; <c>null</c> dla pliku, który nie jest skrótem. Błąd odczytu = <see cref="Errors.OperationException"/>.</summary>
    ShortcutInfo? ReadShortcut(string path);

    /// <summary>
    /// Sprawdzenie pliku, nigdy nie rzuca. <see cref="FileProbe.Exists"/> tylko dla istniejącego PLIKU – katalog o tej ścieżce nigdy nie jest
    /// <see cref="FileProbe.Exists"/> (prefiks polecenia bez cudzysłowu, np. <c>C:\Program Files</c>, nie może wygrać jako plik).
    /// <see cref="FileProbe.Missing"/> tylko, gdy system odpowiedział „nie istnieje” dla lokalnego dysku stałego; brak dostępu, dysk sieciowy
    /// albo wymienny i każdy inny błąd = <see cref="FileProbe.Unknown"/>.
    /// </summary>
    FileProbe Probe(string fullPath);

    /// <summary>Pola wersji istniejącego pliku; brak zasobu wersji albo błąd = <c>null</c>. Nigdy nie rzuca.</summary>
    FileDetails? Details(string fullPath);
}
