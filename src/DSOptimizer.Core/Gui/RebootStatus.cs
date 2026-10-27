using System.Collections.Generic;

namespace DSOptimizer.Core.Gui;

/// <summary>Wynik reguły banera restartu: id i tytuły pozycji czekających na restart, podzbiór id cofniętych (<paramref name="UndoneIds"/>) oraz ostrzeżenie (np. błąd odczytu journala).</summary>
public sealed record RebootStatus(IReadOnlyList<string> Ids, IReadOnlyList<string> Titles, IReadOnlyList<string> UndoneIds, string? Warning);
