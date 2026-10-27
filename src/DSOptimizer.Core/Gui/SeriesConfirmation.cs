using System.Collections.Generic;
using DSOptimizer.Core.Model;

namespace DSOptimizer.Core.Gui;

/// <summary>Jeden wiersz „nazwa – wartość” okna potwierdzenia (fakty serii albo szczegóły techniczne pozycji).</summary>
public sealed record SeriesFact(string Name, string Value);

/// <summary>Pozycja okna potwierdzenia: opis dla człowieka, uwagi, ostrzeżenia z katalogu i szczegóły techniczne z podglądu zmian.</summary>
public sealed record SeriesItem(
    string Id,
    string Title,
    RiskLevel Risk,
    string Summary,
    IReadOnlyList<string> Notes,
    IReadOnlyList<string> Warnings,
    IReadOnlyList<SeriesFact> Technical);

/// <summary>
/// Model okna potwierdzenia serii albo pojedynczej pozycji (bez WPF): nagłówek, fakty wspólne dla całej operacji
/// i lista pozycji. <see cref="CreatesRestorePoint"/> = seria zaczyna się krokiem „Punkt przywracania” (Apply przy włączonym
/// punkcie, usuwanie wg reguły <c>AppRemovalEngine</c>).
/// </summary>
public sealed record SeriesConfirmation(
    GuiOperation Operation,
    string Title,
    string Subtitle,
    IReadOnlyList<SeriesFact> Facts,
    IReadOnlyList<SeriesItem> Items,
    string PrimaryText,
    bool CreatesRestorePoint)
{
    /// <summary>Ostrzeżenia całej serii pod faktami (trójkąt), np. brak cofania usunięcia; puste dla tweaków.</summary>
    public IReadOnlyList<string> Warnings { get; init; } = System.Array.Empty<string>();

    /// <summary>
    /// Punkt przywracania jest wymagany: bez niego seria pyta „Kontynuować bez punktu przywracania?” (usuwanie z programem Win32).
    /// Wtedy krok „Nie powstał” jest porażką (czerwony); w serii samych aplikacji ze Sklepu – ostrzeżeniem (R20).
    /// </summary>
    public bool RestorePointRequired { get; init; }
}
