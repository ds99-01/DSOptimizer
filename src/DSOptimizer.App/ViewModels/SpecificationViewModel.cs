using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using DSOptimizer.App.Gui;
using DSOptimizer.Core.Abstractions;
using DSOptimizer.Core.Gui;
using DSOptimizer.Core.Hardware;
using DSOptimizer.Windows;

namespace DSOptimizer.App.ViewModels;

/// <summary>
/// Strona „Specyfikacja”: odczyt sprzętu w tle (grupy wypełniają się po każdej części), zapis raportu i kopiowanie do schowka.
/// Aktualizacje loadera trafiają na wątek UI przez <see cref="Progress{T}"/> utworzony na wątku UI (asynchroniczny Post –
/// loader woła je pod swoją blokadą); odczyt zastąpiony odświeżeniem jest odrzucany przez <see cref="SpecificationPage"/>.
/// </summary>
public sealed class SpecificationViewModel : ObservableObject
{
    /// <summary>Grupy lewej kolumny („Procesor i pamięć”, „Grafika, ekran i dźwięk”); reszta w prawej.</summary>
    private const int LeftColumnGroupCount = 2;

    private readonly SpecificationLoader _loader;
    private readonly ReportFileWriter _writer;
    private readonly ILog _log;
    private readonly IClock _clock;
    private readonly Action<GuiLogLevel, string, string?> _panel;
    private readonly ShellActions _shell;
    private readonly string _machineName;
    private readonly SpecificationPage _page = new();
    private IReadOnlyList<SpecGroup> _groups;

    public SpecificationViewModel(SpecificationLoader loader, ReportFileWriter writer, ILog log, IClock clock, string machineName, Action<GuiLogLevel, string, string?> panel, ShellActions shell)
    {
        _loader = loader ?? throw new ArgumentNullException(nameof(loader));
        _writer = writer ?? throw new ArgumentNullException(nameof(writer));
        _log = log ?? throw new ArgumentNullException(nameof(log));
        _clock = clock ?? throw new ArgumentNullException(nameof(clock));
        _panel = panel ?? throw new ArgumentNullException(nameof(panel));
        _shell = shell ?? throw new ArgumentNullException(nameof(shell));
        _machineName = machineName ?? string.Empty;
        _groups = SpecificationFormatter.Groups(_page.Snapshot);

        RefreshCommand = new AsyncRelayCommand(LoadAsync);
        SaveReportCommand = new AsyncRelayCommand(SaveReportAsync, () => IsComplete);
        CopyCommand = new RelayCommand(Copy, () => IsComplete);
        OpenRepositoryPageCommand = new AsyncRelayCommand(OpenRepositoryPageAsync);
    }

    /// <summary>Grupy ekranu, podmieniane w całości po każdej przyjętej aktualizacji.</summary>
    public IReadOnlyList<SpecGroup> Groups
    {
        get => _groups;
        private set
        {
            if (SetProperty(ref _groups, value))
            {
                OnPropertyChanged(nameof(LeftGroups));
                OnPropertyChanged(nameof(RightGroups));
            }
        }
    }

    public IReadOnlyList<SpecGroup> LeftGroups => Groups.Take(LeftColumnGroupCount).ToList();

    public IReadOnlyList<SpecGroup> RightGroups => Groups.Skip(LeftColumnGroupCount).ToList();

    public bool IsComplete => _page.IsComplete;

    /// <summary>Nazwa pozycji w menu (etykieta, podpowiedź w zwiniętym menu, nazwa dla czytnika ekranu).</summary>
    public string Name { get; } = "Specyfikacja";

    /// <summary>„Odśwież stan” na tej stronie: ponowny odczyt.</summary>
    public IAsyncRelayCommand RefreshCommand { get; }

    public IAsyncRelayCommand SaveReportCommand { get; }

    public IRelayCommand CopyCommand { get; }

    /// <summary>Link do repozytorium w wierszu przycisków (ta sama ścieżka co w „O programie”).</summary>
    public IAsyncRelayCommand OpenRepositoryPageCommand { get; }

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

    /// <summary>
    /// Odczyt (start przy Loaded okna i „Odśwież stan”). Wołać z wątku UI. Loader nigdy nie rzuca; wynik odczytu zastąpionego
    /// nowszym nie zmienia ekranu.
    /// </summary>
    public async Task LoadAsync()
    {
        var load = _page.BeginLoad();
        Show();
        IProgress<HardwareSnapshot> progress = new Progress<HardwareSnapshot>(snapshot => Accept(load, snapshot));
        var final = await _loader.LoadAsync(progress.Report).ConfigureAwait(true);
        Accept(load, final);
    }

    private void Accept(int load, HardwareSnapshot snapshot)
    {
        if (_page.Accept(load, snapshot))
        {
            Show();
        }
    }

    private void Show()
    {
        Groups = SpecificationFormatter.Groups(_page.Snapshot);
        OnPropertyChanged(nameof(IsComplete));
        SaveReportCommand.NotifyCanExecuteChanged();
        CopyCommand.NotifyCanExecuteChanged();
    }

    /// <summary>„Zapisz raport”: zapis poza wątkiem UI, każda kopia (komputer, pendrive) osobną linią dziennika.</summary>
    private async Task SaveReportAsync()
    {
        var snapshot = _page.Snapshot;
        var now = _clock.UtcNow.ToLocalTime().DateTime;
        try
        {
            var results = await Task.Run(() => _writer.Save(
                SpecificationFormatter.FileName(now),
                SpecificationFormatter.Report(snapshot, _machineName, now))).ConfigureAwait(true);
            foreach (var result in results)
            {
                SpecificationText.LogSaveResult(_log, result.Path, result.Saved, result.Error, _panel);
            }
        }
        catch (Exception ex)
        {
            // Writer zwraca błędy zapisu jako wyniki; tu zostaje np. odmowa zabezpieczenia katalogu – okno działa dalej.
            _log.Warn(SpecificationText.Component, $"Nie zapisano raportu: {ex.Message}");
        }
    }

    /// <summary>„Kopiuj”: ten sam tekst co plik raportu.</summary>
    private void Copy()
    {
        var text = SpecificationFormatter.Report(_page.Snapshot, _machineName, _clock.UtcNow.ToLocalTime().DateTime);
        // Schowek bywa zajęty przez inny proces (CLIPBRD_E_CANT_OPEN, np. RustDesk): ClipboardCopy sprawdza
        // odczytem zwrotnym, czy tekst i tak trafił do schowka.
        var result = ClipboardCopy.Copy(WpfClipboardText.Instance, text);
        if (result.Copied)
        {
            if (result.Error is not null)
            {
                _log.Info(SpecificationText.Component, ClipboardCopy.RecoveredMessage(result.Error));
            }

            SpecificationText.LogCopied(_log, _panel);
        }
        else
        {
            _log.Warn(SpecificationText.Component, SpecificationText.CopyFailedMessage(result.Error?.Message ?? string.Empty));
        }
    }
}
