namespace DSOptimizer.Core.Abstractions;

/// <summary>Wolne miejsce na woluminie (do tekstu „zwolniono”).</summary>
public interface IDiskSpace
{
    /// <summary>Wolne bajty woluminu <paramref name="root"/> (np. <c>C:\</c>); <c>null</c> = odczyt nieudany.</summary>
    long? FreeBytes(string root);
}
