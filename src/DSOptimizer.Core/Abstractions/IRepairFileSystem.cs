using System;
using System.Collections.Generic;

namespace DSOptimizer.Core.Abstractions;

/// <summary>Operacje na plikach i folderach potrzebne naprawom. Błędy systemu plików = <see cref="Errors.OperationException"/>.</summary>
public interface IRepairFileSystem
{
    bool DirectoryExists(string path);

    /// <summary>
    /// Czy folder istnieje – z odróżnieniem braku od błędu (w przeciwieństwie do <see cref="DirectoryExists"/>, które przy każdym
    /// błędzie zwraca <c>false</c>). <c>false</c> tylko wtedy, gdy elementu nie ma (Win32 <c>ERROR_FILE_NOT_FOUND</c> albo
    /// <c>ERROR_PATH_NOT_FOUND</c>) albo pod ścieżką leży plik. Niektóre błędy dają <see cref="Errors.OperationException"/>, ale nie
    /// wszystkie (na net481 <c>File.GetAttributes</c> idzie przez <c>FindFirstFile</c> na folderze nadrzędnym):
    /// <list type="bullet">
    /// <item>odmowa <c>ListDirectory</c>/<c>ReadAttributes</c> na folderze <b>nadrzędnym</b> = <see cref="Errors.OperationException"/>
    /// z <see cref="Errors.OperationErrorKind.AccessDenied"/> (nie fałszywe „nie ma”; test
    /// <c>ProbeDirectory_AccessDenied_ThrowsInsteadOfFalse</c>), podobnie np. dysk niegotowy;</item>
    /// <item>brak dostępu do <b>samego</b> folderu i folder w trakcie usuwania zwykle = „istnieje” (<c>true</c>), bez wyjątku.</item>
    /// </list>
    /// Dokument nie obiecuje wyjątku przy każdym błędzie. Służy odczytowi zwrotnemu <c>windows-old</c>.
    /// </summary>
    bool ProbeDirectory(string path);

    /// <summary>Przenosi (zmienia nazwę) folder; istniejący cel albo brak źródła to błąd.</summary>
    void MoveDirectory(string source, string destination);

    /// <summary>
    /// Usuwa folder wraz z zawartością. Nie podąża za junction ani dowiązaniem symbolicznym: usuwa sam link, a cel zostaje.
    /// Brak folderu = nic do zrobienia.
    /// </summary>
    void DeleteDirectoryTree(string path);

    /// <summary>
    /// Pełne ścieżki folderów leżących obok <paramref name="path"/> (w tym samym folderze nadrzędnym), których nazwa zaczyna się
    /// od <paramref name="prefix"/> (bez rozróżniania wielkości liter), posortowane; sam <paramref name="path"/> pomijany.
    /// Brak folderu nadrzędnego = pusta lista.
    /// </summary>
    IReadOnlyList<string> ListSiblingDirectories(string path, string prefix);

    /// <summary>Zapisuje tekst jako UTF-8 z BOM do nowego pliku z zaufanym ACL (Administratorzy/SYSTEM); istniejący plik jest zastępowany.</summary>
    void WriteAllText(string path, string content);

    /// <summary>Rozmiar pliku w bajtach; <c>null</c>, gdy pliku nie ma.</summary>
    long? FileLength(string path);

    /// <summary>
    /// Usuwa rekurencyjnie pliki z <paramref name="root"/>, których <c>LastWriteTimeUtc</c> jest starszy niż
    /// <paramref name="olderThanUtc"/>. Przy każdym elemencie sprawdza <c>ReparsePoint</c>: junction i dowiązanie są usuwane jako
    /// sam link (też według wieku linku), bez wchodzenia do celu. Plik w użyciu (nie da się go otworzyć bez współdzielenia, także
    /// gdy inny proces trzyma go z <c>FILE_SHARE_DELETE</c>) albo bez dostępu zostaje i jest liczony w
    /// <see cref="OldFilesCleanup.SkippedInUse"/>; sprawdzenie i usunięcie to jedno otwarcie pliku. Puste podfoldery starsze niż
    /// próg (czas sprzed czyszczenia) też są usuwane, a sam <paramref name="root"/> zostaje. Element znikający w trakcie = pominięcie.
    /// Brak <paramref name="root"/> albo <paramref name="root"/> będący linkiem = <see cref="OldFilesCleanup.None"/>.
    /// </summary>
    OldFilesCleanup DeleteOldFiles(string root, DateTime olderThanUtc);

    /// <summary>
    /// Usuwa pliki leżące bezpośrednio w <paramref name="directory"/> (bez podfolderów), których nazwa zaczyna się od
    /// <paramref name="prefix"/> i kończy na <paramref name="extension"/> (bez rozróżniania wielkości liter, bez wzorców z
    /// <c>*</c>), tak samo jak <see cref="DeleteOldFiles"/>: przez uchwyt, link usuwany jako link, plik w użyciu zostaje i jest
    /// liczony w <see cref="OldFilesCleanup.SkippedInUse"/>. Wiek nie ma znaczenia (<see cref="OldFilesCleanup.SkippedYoung"/> = 0).
    /// Brak folderu albo folder będący linkiem = <see cref="OldFilesCleanup.None"/>. Służy do miniatur konta (<c>thumbcache_*.db</c>).
    /// </summary>
    OldFilesCleanup DeleteFilesByName(string directory, string prefix, string extension);

    /// <summary>
    /// Tekst pliku od bajtu <paramref name="offset"/> (UTF-8, BOM niewymagany), czytany ze współdzieleniem
    /// <c>ReadWrite | Delete</c>, najwyżej ostatnie 16 MB (przy obcięciu od początku pełnej linii). Offset za końcem pliku (plik
    /// zastąpiony nowym) = tekst od początku pliku. Brak pliku = <c>null</c>. Służy do odczytu segmentu <c>CBS.log</c>.
    /// </summary>
    string? ReadSharedFrom(string path, long offset);
}
