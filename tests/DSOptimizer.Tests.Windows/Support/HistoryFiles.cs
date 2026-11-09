using System.IO;
using System.Security.AccessControl;
using DSOptimizer.Core.Journal;
using DSOptimizer.Core.Localization;
using DSOptimizer.Core.Settings;
using DSOptimizer.Windows;

namespace DSOptimizer.Tests.Windows.Support;

/// <summary>Pliki historii i ustawień zapisane prawdziwymi magazynami programu (folder i plik z deskryptorem programu).</summary>
public static class HistoryFiles
{
    /// <summary>Journal z jednym wpisem tweaka w <c>&lt;folder&gt;\journal\journal.json</c>; zwraca ścieżkę pliku.</summary>
    public static string WriteJournal(string folder, string machine)
    {
        var store = new JsonFileJournalStore(new SecureRoot(folder), machine, new FixedClock());
        var document = new JournalDocument { Machine = machine };
        document.Entries.Add(new JournalEntry { TweakId = "a.b", Status = JournalEntryStatus.Active });
        store.Save(document);
        return store.FilePath;
    }

    /// <summary>Ustawienia z wybranym językiem w <c>&lt;folder&gt;\settings.json</c>; zwraca ścieżkę pliku.</summary>
    public static string WriteSettings(string folder, UiLanguage language)
    {
        var store = new JsonFileSettingsStore(new SecureRoot(folder), new FixedClock());
        store.Save(new AppSettings { Language = language });
        return store.FilePath;
    }

    /// <summary>Plik z dowolną treścią i deskryptorem pliku programu (chroniony DACL, tylko Administratorzy i SYSTEM).</summary>
    public static string WriteProtected(string path, byte[] bytes)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        using var stream = new FileStream(
            path, FileMode.CreateNew, FileSystemRights.Write, FileShare.None, 4096, FileOptions.None, SecureDirectory.CreateTrustedFileSecurity());
        stream.Write(bytes, 0, bytes.Length);
        return path;
    }
}
