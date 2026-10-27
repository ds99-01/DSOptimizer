using DSOptimizer.Core.Settings;

namespace DSOptimizer.Core.Abstractions;

public interface ISettingsStore
{
    /// <summary>Wczytuje ustawienia. Nigdy nie rzuca: przy braku lub uszkodzeniu pliku zwraca wartości domyślne (z ostrzeżeniem).</summary>
    SettingsLoadResult Load();

    /// <summary>Zapisuje ustawienia. Rzuca wyłącznie <see cref="SettingsStoreException"/>.</summary>
    void Save(AppSettings settings);
}
