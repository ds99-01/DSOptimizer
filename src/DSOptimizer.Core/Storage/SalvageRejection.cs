namespace DSOptimizer.Core.Storage;

/// <summary>Powód, dla którego plik historii albo ustawień z odłożonego folderu nie został przeniesiony do korzenia.</summary>
public enum SalvageRejection
{
    /// <summary>Nie udało się otworzyć ani przeczytać pliku (np. otwarty do zapisu przez inny proces).</summary>
    OpenFailed,

    /// <summary>Element nie jest zwykłym plikiem: link symboliczny, junction, folder albo urządzenie.</summary>
    NotRegularFile,

    /// <summary>Plik ma więcej niż jedno dowiązanie twarde.</summary>
    MultipleLinks,

    /// <summary>Właściciel albo uprawnienia pliku nie dowodzą, że zapisał go administrator.</summary>
    UntrustedAcl,

    /// <summary>Plik przekracza limit rozmiaru.</summary>
    TooLarge,

    /// <summary>Treść pliku jest uszkodzona.</summary>
    Corrupted,

    /// <summary>Historia pochodzi z innego komputera.</summary>
    OtherComputer,

    /// <summary>Nie udało się zapisać nowego pliku w korzeniu.</summary>
    WriteFailed,
}
