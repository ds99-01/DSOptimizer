namespace DSOptimizer.Core.Gui;

/// <summary>Co czeka na restart: zastosowana zmiana albo cofnięcie (rodzaj ostatniego istotnego zdarzenia z journala).</summary>
public enum RebootPendingKind
{
    Applied,
    Undone,
}
