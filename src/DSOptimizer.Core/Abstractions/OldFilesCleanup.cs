namespace DSOptimizer.Core.Abstractions;

/// <summary>
/// Wynik <see cref="IRepairFileSystem.DeleteOldFiles"/>: usunięte pliki i linki, pominięte w użyciu (albo bez dostępu),
/// pominięte młodsze niż próg oraz zwolnione bajty (suma rozmiarów usuniętych plików; linki i foldery liczą się jako 0).
/// <paramref name="SkipReason"/> różny od <c>null</c> = folderu w ogóle nie czyszczono (np. sam jest linkiem albo ścieżka prowadzi
/// przez link do innego folderu); tekst powodu bez „pominięto – ”.
/// </summary>
public sealed record OldFilesCleanup(int Deleted, int SkippedInUse, int SkippedYoung, long FreedBytes, string? SkipReason = null)
{
    /// <summary>Nic nie zrobiono (np. brak folderu).</summary>
    public static OldFilesCleanup None { get; } = new(0, 0, 0, 0);

    /// <summary>Folder pominięty z powodem (nic nie usunięto).</summary>
    public static OldFilesCleanup Skipped(string reason) => new(0, 0, 0, 0, reason ?? throw new System.ArgumentNullException(nameof(reason)));
}
