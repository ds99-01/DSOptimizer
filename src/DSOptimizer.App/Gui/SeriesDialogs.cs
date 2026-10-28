using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using DSOptimizer.App.ViewModels;
using DSOptimizer.App.Views;
using DSOptimizer.Core.Abstractions;
using DSOptimizer.Core.Apps;
using DSOptimizer.Core.Engine;
using DSOptimizer.Core.Gui;
using DSOptimizer.Core.Localization;

namespace DSOptimizer.App.Gui;

/// <summary>
/// Okna serii w skórce aplikacji (<see cref="SeriesWindow"/>), modalne nad aktywnym oknem programu. Wołać na wątku UI.
/// „Kopiuj wynik” idzie przez <see cref="ClipboardCopy"/> i zostawia linię w dzienniku jak „Kopiuj” w Specyfikacji.
/// </summary>
public sealed class SeriesDialogs : ISeriesDialogs
{
    public static string Component => L.T("Seria");

    public static string CopiedMessage => L.T("Skopiowano wynik do schowka.");

    private readonly ILog _log;
    private readonly Action<GuiLogLevel, string, string?> _panel;
    private readonly IClock _clock;
    private SeriesWindowViewModel? _running;

    /// <param name="log">Plik logu (TeeLog): Info tylko do pliku, Warn także do panelu.</param>
    /// <param name="panel">Widoczny dziennik okna (UiLogSink.Add) – dla linii Info.</param>
    public SeriesDialogs(ILog log, Action<GuiLogLevel, string, string?> panel, IClock clock)
    {
        _log = log ?? throw new ArgumentNullException(nameof(log));
        _panel = panel ?? throw new ArgumentNullException(nameof(panel));
        _clock = clock ?? throw new ArgumentNullException(nameof(clock));
    }

    public static string CopyFailedMessage(string reason) => L.F("Nie skopiowano wyniku do schowka: {0}", reason);

    /// <summary>Linia pliku logu po odpowiedzi na pytanie o kontynuację bez punktu przywracania.</summary>
    public static string RestorePointAnsweredMessage(string reason, string answer)
        => L.F("Punkt przywracania nie powstał ({0}). Odpowiedź: {1}.", reason, answer);

    /// <summary>Linia dziennika okna po odpowiedzi na pytanie o kontynuację bez punktu przywracania.</summary>
    public static string ContinueAnsweredMessage(string answer) => L.F("Kontynuować bez punktu przywracania? Odpowiedź: {0}.", answer);

    public Task<bool> ConfirmAsync(SeriesConfirmation confirmation)
    {
        if (confirmation is null)
        {
            throw new ArgumentNullException(nameof(confirmation));
        }

        var viewModel = new SeriesWindowViewModel(confirmation, null, null, null, _clock);
        SeriesWindow.ShowFor(Owner(), viewModel);
        return Task.FromResult(viewModel.Confirmed);
    }

    public Task<SeriesDialogResult> RunSeriesAsync(
        SeriesConfirmation confirmation,
        Func<IProgress<BatchStepEvent>, BatchStop, Task<GuiOperationReport>> run)
    {
        if (confirmation is null)
        {
            throw new ArgumentNullException(nameof(confirmation));
        }

        if (run is null)
        {
            throw new ArgumentNullException(nameof(run));
        }

        // „Uruchom ponownie teraz” w wyniku (tylko przy BatchRun.RebootCount > 0): potwierdzenie nad oknem wyniku, zgoda zamyka
        // okno z RestartRequested = true, a restart wykonuje MainViewModel tą samą ścieżką co baner.
        var viewModel = new SeriesWindowViewModel(confirmation, run, CopyResult, ConfirmRestartAsync, _clock);
        _running = viewModel;
        try
        {
            SeriesWindow.ShowFor(Owner(), viewModel);
        }
        finally
        {
            _running = null;
        }

        return Task.FromResult(new SeriesDialogResult(viewModel.Confirmed ? viewModel.Report : null, viewModel.RestartRequested));
    }

    public Task<SeriesDialogResult> RunActionAsync(
        SeriesConfirmation confirmation,
        Func<IProgress<ActionProgress>, CancellationToken, Task<GuiOperationReport>> run)
    {
        // Wyjątki argumentów z ForAction (null, nie Execute, nie jedna pozycja) – przed pokazaniem okna.
        var viewModel = SeriesWindowViewModel.ForAction(confirmation, run, CopyResult, ConfirmRestartAsync, _clock);
        SeriesWindow.ShowFor(Owner(), viewModel);
        return Task.FromResult(new SeriesDialogResult(viewModel.Confirmed ? viewModel.Report : null, viewModel.RestartRequested));
    }

    public Task<bool> ConfirmRestartAsync()
    {
        var viewModel = SeriesWindowViewModel.ForRestart(_clock);
        SeriesWindow.ShowFor(Owner(), viewModel);
        return Task.FromResult(viewModel.Confirmed);
    }

    public async Task<bool> ConfirmContinueWithoutRestorePointAsync(string reason)
    {
        var running = _running;
        if (running is null || !running.IsRunning)
        {
            _log.Warn(Component, L.F("Punkt przywracania nie powstał ({0}), a okno serii nie trwa – przyjęto „Nie”.", reason));
            return false;
        }

        var proceed = await running.AskContinueWithoutRestorePointAsync().ConfigureAwait(true);
        var answer = proceed ? AppsPageText.ContinueButton : AppsPageText.DeclineButton;
        _log.Info(Component, RestorePointAnsweredMessage(reason, answer));
        _panel(GuiLogLevel.Info, ContinueAnsweredMessage(answer), null);
        return proceed;
    }

    /// <summary>Właściciel: aktywne okno programu (np. okno wyniku przy potwierdzeniu restartu), inaczej okno główne.</summary>
    private static Window? Owner()
    {
        var app = Application.Current;
        if (app is null)
        {
            return null;
        }

        return app.Windows.OfType<Window>().FirstOrDefault(window => window.IsActive) ?? app.MainWindow;
    }

    /// <summary>Schowek bywa zajęty przez inny proces (RustDesk): ClipboardCopy rozstrzyga odczytem zwrotnym.</summary>
    private void CopyResult(string text)
    {
        var result = ClipboardCopy.Copy(WpfClipboardText.Instance, text);
        if (result.Copied)
        {
            if (result.Error is not null)
            {
                _log.Info(Component, ClipboardCopy.RecoveredMessage(result.Error));
            }

            _log.Info(Component, CopiedMessage);
            _panel(GuiLogLevel.Info, CopiedMessage, null);
        }
        else
        {
            _log.Warn(Component, CopyFailedMessage(result.Error?.Message ?? string.Empty));
        }
    }
}
