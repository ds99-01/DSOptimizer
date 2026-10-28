using System;
using System.Threading.Tasks;
using System.Windows;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using DSOptimizer.App.Gui;
using DSOptimizer.App.Views;
using DSOptimizer.Core;
using DSOptimizer.Core.Abstractions;
using DSOptimizer.Core.Gui;
using DSOptimizer.Core.Settings;

namespace DSOptimizer.App.ViewModels;

/// <summary>
/// Strona „Ustawienia” i kolumna „O programie”. Każda zmiana od razu: nowy <see cref="AppSettings"/> → zapis → zastosowanie.
/// Nieudany zapis = ostrzeżenie w dzienniku, a zmiana i tak obowiązuje do zamknięcia programu.
/// W trakcie operacji (<see cref="IsEditable"/> = false) formularz jest nieaktywny, więc seria nie zmienia polityki w połowie.
/// </summary>
public sealed class SettingsViewModel : ObservableObject
{
    private const string Component = "Ustawienia";

    private readonly ISettingsStore _store;
    private readonly GuiSession _session;
    private readonly ThemeController _theme;
    private readonly ShellActions _shell;
    private readonly ILog _log;
    private AppSettings _settings;
    private bool _isEditable = true;

    public SettingsViewModel(SettingsLoadResult loaded, ISettingsStore store, GuiSession session, ThemeController theme, ShellActions shell, ILog log)
    {
        if (loaded is null)
        {
            throw new ArgumentNullException(nameof(loaded));
        }

        _store = store ?? throw new ArgumentNullException(nameof(store));
        _session = session ?? throw new ArgumentNullException(nameof(session));
        _theme = theme ?? throw new ArgumentNullException(nameof(theme));
        _shell = shell ?? throw new ArgumentNullException(nameof(shell));
        _log = log ?? throw new ArgumentNullException(nameof(log));
        _settings = loaded.Settings ?? AppSettings.Default;
        // Program ustawia to samo przy tworzeniu sesji; tu gwarancja, że strona i sesja startują zgodne.
        _session.CreateRestorePoint = _settings.CreateRestorePoint;

        // Kilka klików w link w trakcie sprawdzania powłoki ignoruje ShellActions; przycisk nie przygasa na 1–3 s.
        OpenRepositoryPageCommand = new AsyncRelayCommand(OpenRepositoryPageAsync, AsyncRelayCommandOptions.AllowConcurrentExecutions);
        ShowLicensesCommand = new RelayCommand(ShowLicenses);
    }

    /// <summary>„Twórz punkt przywracania przed zmianami” (domyślnie tak); ustawia <see cref="GuiSession.CreateRestorePoint"/>.</summary>
    public bool CreateRestorePoint
    {
        get => _settings.CreateRestorePoint;
        set
        {
            if (value == _settings.CreateRestorePoint)
            {
                return;
            }

            _session.CreateRestorePoint = value;
            Save(_settings with { CreateRestorePoint = value });
            OnPropertyChanged();
        }
    }

    public bool IsSystemTheme
    {
        get => _settings.Theme == ThemePreference.System;
        set => SelectTheme(value, ThemePreference.System);
    }

    public bool IsLightTheme
    {
        get => _settings.Theme == ThemePreference.Light;
        set => SelectTheme(value, ThemePreference.Light);
    }

    public bool IsDarkTheme
    {
        get => _settings.Theme == ThemePreference.Dark;
        set => SelectTheme(value, ThemePreference.Dark);
    }

    /// <summary>Fałsz w trakcie operacji (ustawia <see cref="MainViewModel"/> razem z IsBusy).</summary>
    public bool IsEditable
    {
        get => _isEditable;
        internal set => SetProperty(ref _isEditable, value);
    }

    public string VersionText { get; } = "Wersja " + ProductInfo.Version;

    /// <summary>Nazwa pozycji w menu (etykieta, podpowiedź w zwiniętym menu, nazwa dla czytnika ekranu).</summary>
    public string Name { get; } = "Ustawienia";

    public IAsyncRelayCommand OpenRepositoryPageCommand { get; }

    public IRelayCommand ShowLicensesCommand { get; }

    /// <summary>RadioButton ustawia true na wybranym i false na poprzednim; liczy się tylko true.</summary>
    private void SelectTheme(bool selected, ThemePreference theme)
    {
        if (!selected || _settings.Theme == theme)
        {
            return;
        }

        Save(_settings with { Theme = theme });
        _theme.Change(theme);
        OnPropertyChanged(nameof(IsSystemTheme));
        OnPropertyChanged(nameof(IsLightTheme));
        OnPropertyChanged(nameof(IsDarkTheme));
    }

    /// <summary>Nowe ustawienia obowiązują zawsze; nieudany zapis = ostrzeżenie w dzienniku.</summary>
    private void Save(AppSettings settings)
    {
        _settings = settings;
        try
        {
            _store.Save(settings);
        }
        catch (SettingsStoreException ex)
        {
            _log.Warn(Component, $"Nie udało się zapisać ustawień: {ex.Message} Zmiana obowiązuje do zamknięcia programu.");
        }
    }

    // ShellActions.OpenRepositoryPageAsync nie rzuca (odmowa = ostrzeżenie i kopia adresu); try/catch, by nic nie wyciekło na wątek UI.
    private async Task OpenRepositoryPageAsync()
    {
        try
        {
            await _shell.OpenRepositoryPageAsync().ConfigureAwait(true);
        }
        catch (Exception ex)
        {
            _log.Fail("Gui", "Nie udało się otworzyć strony repozytorium.", ex);
        }
    }

    private void ShowLicenses()
    {
        try
        {
            LicensesWindow.ShowFor(Application.Current?.MainWindow, ThirdPartyNotices.Text);
        }
        catch (Exception ex)
        {
            _log.Fail("Gui", "Nie udało się pokazać licencji komponentów.", ex);
        }
    }
}
