using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using DSOptimizer.App.Gui;
using DSOptimizer.Core.Gui;
using DSOptimizer.Core.Prep;

namespace DSOptimizer.App.ViewModels;

/// <summary>
/// Strona „Przygotowanie”: odczyt 6 części w tle (kroki wypełniają się po każdej), linki do Ustawień i stron producentów.
/// Odczyt przy każdym wejściu na stronę, na F5 i na „Odśwież stan”. Aktualizacje loadera trafiają na wątek UI przez
/// <see cref="Progress{T}"/> utworzony na wątku UI; odczyt zastąpiony nowszym jest odrzucany przez <see cref="PrepPage"/>.
/// Linki idą przez <see cref="ShellActions.OpenUrlAsync"/> (lista dozwolonych adresów, odmowa = ostrzeżenie i kopia adresu).
/// </summary>
public sealed class PrepViewModel : ObservableObject
{
    private readonly PrepLoader _loader;
    private readonly ShellActions _shell;
    private readonly string _systemDrive;
    private readonly PrepPage _page = new();
    private IReadOnlyList<PrepStep> _steps;
    private readonly Action<GuiLogLevel, string> _log;

    /// <param name="systemDrive">Dysk systemowy dla wiersza BitLockera („C:” albo „C:\”).</param>
    /// <param name="log">Linia do dziennika w oknie (błąd odczytu).</param>
    public PrepViewModel(PrepLoader loader, ShellActions shell, string systemDrive, Action<GuiLogLevel, string> log)
    {
        _log = log ?? throw new ArgumentNullException(nameof(log));
        _loader = loader ?? throw new ArgumentNullException(nameof(loader));
        _shell = shell ?? throw new ArgumentNullException(nameof(shell));
        _systemDrive = systemDrive ?? throw new ArgumentNullException(nameof(systemDrive));
        _steps = PrepSteps.Build(_page.Snapshot, _systemDrive);

        // „Odśwież stan” zawsze aktywne (makieta scenariusza 3): nowy odczyt zastępuje trwający, starszy wynik odrzuca PrepPage.
        RefreshCommand = new AsyncRelayCommand(RefreshAsync, AsyncRelayCommandOptions.AllowConcurrentExecutions);
        // Domyślnie bez równoległych wykonań: linki są nieaktywne tylko w czasie trwającego otwierania.
        OpenCommand = new AsyncRelayCommand<PrepAction>(OpenAsync);
    }

    /// <summary>Nazwa pozycji w menu (etykieta, podpowiedź w zwiniętym menu, nazwa dla czytnika ekranu).</summary>
    public string Name { get; } = PrepText.PageTitle;

    public string IconKind { get; } = GuiIcons.Prep;

    /// <summary>Kroki strony, podmieniane w całości po każdej przyjętej aktualizacji.</summary>
    public IReadOnlyList<PrepStep> Steps
    {
        get => _steps;
        private set => SetProperty(ref _steps, value);
    }

    /// <summary>„Odśwież stan”: ponowny odczyt, także w trakcie poprzedniego (ten zostaje odrzucony).</summary>
    public IAsyncRelayCommand RefreshCommand { get; }

    /// <summary>Link kroku albo wiersza; parametr = <see cref="PrepAction"/>.</summary>
    public IAsyncRelayCommand<PrepAction> OpenCommand { get; }

    /// <summary>
    /// Nowy odczyt (wejście na stronę, F5, „Odśwież stan”). Wołać z wątku UI. Nigdy nie rzuca: błąd = jedna linia Error w dzienniku, ekran zostaje w
    /// poprzednim stanie; wynik odczytu zastąpionego nowszym nie zmienia ekranu.
    /// </summary>
    public async Task RefreshAsync()
    {
        try
        {
            var load = _page.BeginLoad();
            Show();
            IProgress<PrepSnapshot> progress = new Progress<PrepSnapshot>(snapshot => Accept(load, snapshot));
            var final = await _loader.LoadAsync(progress.Report).ConfigureAwait(true);
            Accept(load, final);
        }
        catch (Exception ex)
        {
            Report(ex);
        }
    }

    private void Report(Exception ex)
    {
        try
        {
            _log(GuiLogLevel.Error, $"Nieoczekiwany błąd: {ex.GetType().Name}: {ex.Message}");
        }
        catch (Exception)
        {
            // Dziennik niedostępny – RefreshAsync bywa wołane bez await i nie może rzucać.
        }
    }

    private void Accept(int load, PrepSnapshot snapshot)
    {
        if (_page.Accept(load, snapshot))
        {
            Show();
        }
    }

    private void Show() => Steps = PrepSteps.Build(_page.Snapshot, _systemDrive);

    // ShellActions.OpenUrlAsync nie rzuca (odmowa = ostrzeżenie w dzienniku i kopia adresu do schowka).
    private Task OpenAsync(PrepAction? action) => action is null ? Task.CompletedTask : _shell.OpenUrlAsync(action.Url);
}
