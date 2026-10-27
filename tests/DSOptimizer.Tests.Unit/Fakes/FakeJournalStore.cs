using System;
using DSOptimizer.Core.Journal;

namespace DSOptimizer.Tests.Unit.Fakes;

/// <summary>Journal w pamięci, ale zawsze przez JSON — jak prawdziwy plik (brak współdzielenia obiektów).</summary>
public sealed class FakeJournalStore : IJournalStore
{
    private string _json = JournalSerializer.Serialize(new JournalDocument { Machine = "TEST-PC" });

    public bool FailLoad { get; set; }

    public bool FailSave { get; set; }

    /// <summary>Wyjątek (spoza kontraktu JournalStoreException) rzucany z Load.</summary>
    public Exception? LoadException { get; set; }

    /// <summary>Wyjątek (spoza kontraktu) rzucany z Save, gdy liczba udanych zapisów >= ThrowOnSaveFrom.</summary>
    public Exception? SaveException { get; set; }

    public int ThrowOnSaveFrom { get; set; }

    public string? LoadWarning { get; set; }

    /// <summary>Modeluje prawdziwy store: ostrzeżenie (odłożony uszkodzony plik) tylko przy pierwszym Load.</summary>
    public bool WarnOnlyOnFirstLoad { get; set; }

    public int LoadCount { get; private set; }

    public int SaveCount { get; private set; }

    public JournalDocument Current => JournalSerializer.Deserialize(_json);

    public JournalLoadResult Load()
    {
        if (LoadException is not null)
        {
            throw LoadException;
        }

        if (FailLoad)
        {
            throw new JournalStoreException("Odczyt journala nie powiódł się (fake).");
        }

        LoadCount++;
        return new JournalLoadResult(JournalSerializer.Deserialize(_json), WarnOnlyOnFirstLoad && LoadCount > 1 ? null : LoadWarning);
    }

    public void Save(JournalDocument document)
    {
        if (SaveException is not null && SaveCount >= ThrowOnSaveFrom)
        {
            throw SaveException;
        }

        if (FailSave)
        {
            throw new JournalStoreException("Zapis journala nie powiódł się (fake).");
        }

        _json = JournalSerializer.Serialize(document);
        SaveCount++;
    }
}
