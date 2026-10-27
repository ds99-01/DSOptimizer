namespace DSOptimizer.Core.Abstractions;

/// <summary>Stan ochrony systemu (punkty przywracania) dysku C:, tylko odczyt.</summary>
public interface ISystemProtectionReader
{
    /// <summary>true = włączona, false = wyłączona, null = nie udało się sprawdzić.</summary>
    bool? IsEnabled();
}
