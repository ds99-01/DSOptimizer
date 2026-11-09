using System.IO;
using DSOptimizer.Core.Localization;

namespace DSOptimizer.Windows;

/// <summary>
/// Kopia logu i raportu obok exe leży w miejscu zapisywalnym bez admina (pendrive), a program pisze jako admin. Junction albo dowiązanie
/// w tej ścieżce skierowałoby zapis do dowolnego folderu, więc taka ścieżka to błąd zapisu kopii, nie zapis gdzie indziej.
/// Link rozpoznaje <see cref="ReparseLinks.IsLink"/> (placeholdery OneDrive nie są linkami).
/// </summary>
internal static class MirrorPathGuard
{
    /// <summary>Tworzy katalog kopii; link na ścieżce = <see cref="IOException"/> i nic nie powstaje.</summary>
    public static void EnsureDirectory(string directory)
    {
        for (var current = new DirectoryInfo(directory); current is not null; current = current.Parent)
        {
            RefuseLink(current);
        }

        Directory.CreateDirectory(directory);
    }

    /// <summary>Plik kopii będący dowiązaniem = <see cref="IOException"/>.</summary>
    public static void EnsureFile(string path) => RefuseLink(new FileInfo(path));

    private static void RefuseLink(FileSystemInfo entry)
    {
        if (ReparseLinks.IsLink(entry.FullName))
        {
            throw new IOException(L.F("Ścieżka kopii prowadzi przez junction albo dowiązanie: {0}", entry.FullName));
        }
    }
}
