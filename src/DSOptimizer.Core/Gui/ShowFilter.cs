namespace DSOptimizer.Core.Gui;

/// <summary>
/// Filtr listy „Pokaż” po stanie pozycji (start „Wszystkie”, niezapamiętywany między uruchomieniami). „Zaznaczone” to widok
/// zaznaczonych ze wszystkich kategorii, nie filtr stanu: dla pojedynczej pozycji działa jak „Wszystkie”.
/// </summary>
public enum ShowFilter
{
    All,
    NotApplied,
    Applied,
    Selected,
}
