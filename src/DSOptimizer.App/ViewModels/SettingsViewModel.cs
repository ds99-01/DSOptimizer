using System;
using System.Threading.Tasks;
using System.Windows;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using DSOptimizer.App.Gui;
using DSOptimizer.App.Views;
using DSOptimizer.Core;
using DSOptimizer.Core.Abstractions;
using DSOptimizer.Core.Errors;
using DSOptimizer.Core.Gui;
using DSOptimizer.Core.Localization;
using DSOptimizer.Core.Settings;

namespace DSOptimizer.App.ViewModels;

/// <summary>
/// Strona „Ustawienia” i kolumna „O programie”. Każda zmiana od razu: nowy <see cref="AppSettings"/> → zapis → zastosowanie.
/// Nieudany zapis = ostrzeżenie w dzienniku, a zmiana i tak obowiązuje do zamknięcia programu.
/// W trakcie operacji (<see cref="IsEditable"/> = false) formularz jest nieaktywny, więc seria nie zmienia polityki w połowie.
/// Język zapisuje się od razu, ale obowiązuje po ponownym uruchomieniu programu („Uruchom ponownie teraz”).
/// </summary>
public sealed class SettingsViewModel : ObservableObject
{
    private const string Component = "Ustawienia";

    private readonly ISettingsStore _store;
    private readonly GuiSession _session;
    private readonly ThemeController _theme;
    private readonly ShellActions _shell;
    private readonly ILog _log;
    private readonly UiLanguage _running;
    private readonly IProgramRelauncher _relauncher;
    private readonly Action _shutdown;
    private AppSettings _settings;
    private UiLanguage _selectedLanguage;
    private bool _isEditable = true;
    private bool _relaunching;

    /// <param name="running">Język, w którym działa bieżąca instancja programu.</param>
    /// <param name="shutdown">Zamknięcie programu po udanym starcie nowej instancji.</param>
    public SettingsViewModel(
        SettingsLoadResult loaded,
        ISettingsStore store,
        GuiSession session,
        ThemeController theme,
        ShellActions shell,
        ILog log,
        UiLanguage running,
        IProgramRelauncher relauncher,
        Action shutdown)
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
        _relauncher = relauncher ?? throw new ArgumentNullException(nameof(relauncher));
        _shutdown = shutdown ?? throw new ArgumentNullException(nameof(shutdown));
        _running = running;
        _selectedLanguage = running;
        _settings = loaded.Settings ?? AppSettings.Default;
        // Program ustawia to samo przy tworzeniu sesji; tu gwarancja, że strona i sesja startują zgodne.
        _session.CreateRestorePoint = _settings.CreateRestorePoint;

        // Kilka klików w link w trakcie sprawdzania powłoki ignoruje ShellActions; przycisk nie przygasa na 1–3 s.
        OpenRepositoryPageCommand = new AsyncRelayCommand(OpenRepositoryPageAsync, AsyncRelayCommandOptions.AllowConcurrentExecutions);
        ShowLicensesCommand = new RelayCommand(ShowLicenses);
        RelaunchCommand = new RelayCommand(Relaunch, CanRelaunch);
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

    /// <summary>Język wybrany na liście (na starcie = bieżący); zmiana zapisuje się od razu, jak motyw.</summary>
    public UiLanguage SelectedLanguage
    {
        get => _selectedLanguage;
        set
        {
            if (value == _selectedLanguage)
            {
                return;
            }

            _selectedLanguage = value;
            Save(_settings with { Language = value });
            OnPropertyChanged();
            OnPropertyChanged(nameof(RelaunchNeeded));
            RelaunchCommand.NotifyCanExecuteChanged();
        }
    }

    /// <summary>Wybrany język różni się od tego, w którym działa program: podpowiedź i „Uruchom ponownie teraz”.</summary>
    public bool RelaunchNeeded => _selectedLanguage != _running;

    /// <summary>Fałsz w trakcie operacji (ustawia <see cref="MainViewModel"/> razem z IsBusy).</summary>
    public bool IsEditable
    {
        get => _isEditable;
        set
        {
            if (SetProperty(ref _isEditable, value))
            {
                RelaunchCommand.NotifyCanExecuteChanged();
            }
        }
    }

    public string VersionText { get; } = "Wersja " + ProductInfo.Version;

    /// <summary>Nazwa pozycji w menu (etykieta, podpowiedź w zwiniętym menu, nazwa dla czytnika ekranu).</summary>
    public string Name { get; } = "Ustawienia";

    public IAsyncRelayCommand OpenRepositoryPageCommand { get; }

    public IRelayCommand ShowLicensesCommand { get; }

    /// <summary>„Uruchom ponownie teraz”: aktywne tylko poza operacją, gdy wybrany język ≠ bieżący i start nie trwa.</summary>
    public IRelayCommand RelaunchCommand { get; }

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

    private bool CanRelaunch() => IsEditable && RelaunchNeeded && !_relaunching;

    /// <summary>
    /// Najpierw ponowny zapis ustawień (nowa instancja czyta język z pliku), potem nowa instancja, zamknięcie dopiero po udanym starcie.
    /// Błąd zapisu albo startu = linia w dzienniku i program zostaje otwarty.
    /// </summary>
    private void Relaunch()
    {
        // RelayCommand.Execute nie sprawdza CanExecute: w trakcie operacji i po udanym starcie nic się nie dzieje.
        if (!CanRelaunch())
        {
            return;
        }

        try
        {
            _store.Save(_settings);
        }
        catch (SettingsStoreException ex)
        {
            _log.Fail(Component, L.F("Nie udało się zapisać wybranego języka: {0} Program nie został uruchomiony ponownie.", ex.Message));
            return;
        }

        SetRelaunching(true);
        try
        {
            _relauncher.Relaunch();
        }
        catch (OperationException ex)
        {
            SetRelaunching(false);
            _log.Fail(Component, ex.Message);
            return;
        }

        _shutdown();
    }

    private void SetRelaunching(bool value)
    {
        _relaunching = value;
        RelaunchCommand.NotifyCanExecuteChanged();
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
