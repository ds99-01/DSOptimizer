namespace DSOptimizer.Core.Journal;

public interface IJournalStore
{
    /// <summary>Uszkodzony plik → nowy dokument + Warning. Błąd I/O → JournalStoreException.</summary>
    JournalLoadResult Load();

    /// <summary>Zapis atomowy. Błąd → JournalStoreException.</summary>
    void Save(JournalDocument document);
}
