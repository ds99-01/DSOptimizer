namespace DSOptimizer.App.Gui;

/// <summary>Okno „Zapisz jako” dla raportu; wołać z wątku UI.</summary>
public interface ISaveFileDialog
{
    /// <summary>
    /// Pyta o miejsce zapisu: start na Pulpicie konta procesu, nazwa domyślna <paramref name="fileName"/>, pytanie o nadpisanie
    /// istniejącego pliku. Wybrana pełna ścieżka albo <c>null</c>, gdy anulowano.
    /// </summary>
    string? AskPath(string fileName);
}
