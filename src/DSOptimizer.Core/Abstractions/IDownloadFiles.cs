namespace DSOptimizer.Core.Abstractions;

/// <summary>Pliki pobrane z internetu: folder pobrań programu z zaufanym ACL, skrót i usuwanie po instalacji.</summary>
public interface IDownloadFiles
{
    /// <summary>Nowa, nieistniejąca ścieżka pliku o rozszerzeniu <paramref name="extension"/> w folderze pobrań; dozwolone dokładnie <c>.msi</c> (RustDesk) i <c>.ps1</c> (skrypt WinUtil – zostaje po starcie, bo PowerShell czyta go po uruchomieniu, i jest sprzątany jako osierocony przy kolejnym pobraniu – WinUtil albo RustDesk – gdy ma ponad 24 h).</summary>
    string NewDownloadPath(string extension);

    /// <summary>SHA256 pliku: 64 małe znaki szesnastkowe.</summary>
    string Sha256(string path);

    /// <summary>Usuwa plik z folderu pobrań; brak pliku = <c>true</c>, błąd usuwania = <c>false</c> (z wpisem w logu).</summary>
    bool TryDelete(string path);
}
