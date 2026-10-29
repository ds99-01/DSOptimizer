namespace DSOptimizer.Core.Model;

/// <summary>Wpis rejestru tweaka. <see cref="Value"/>/<see cref="Original"/> = <see cref="RegValue.Absent"/> oznacza „ma nie istnieć”.</summary>
public sealed record RegistryEntry
{
    /// <summary>Ścieżka z korzeniem HKLM\, HKCU\, HKCR\ lub HKU\.</summary>
    public required string Path { get; init; }

    /// <summary>Nazwa wartości; pusty tekst = wartość domyślna klucza.</summary>
    public required string Name { get; init; }

    public required RegValue Value { get; init; }

    /// <summary>Wartość domyślna Windows (fallback undo, gdy brak journala). null = nieznana.</summary>
    public RegValue? Original { get; init; }

    /// <summary>Wpis tylko weryfikuje stan — nie jest zapisywany ani przywracany.</summary>
    public bool DetectOnly { get; init; }

    /// <summary>
    /// Tekst wartości (<c>String</c>, polski w katalogu) jest zapisywany w języku programu z chwili zastosowania, a detekcja uznaje
    /// go w każdym języku interfejsu. Cofnięcie i tak przywraca snapshot. Pole JSON <c>localized</c>.
    /// </summary>
    public bool Localized { get; init; }
}
