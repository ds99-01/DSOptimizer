using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using DSOptimizer.Core.Abstractions;
using DSOptimizer.Core.Apps;
using DSOptimizer.Core.Engine;
using DSOptimizer.Core.Gui;
using DSOptimizer.Core.Install;
using DSOptimizer.Core.Localization;
using DSOptimizer.Core.Model;

namespace DSOptimizer.App.ViewModels;

/// <summary>Stan okna serii: potwierdzenie → postęp → wynik (zawsze w tej kolejności, bez powrotu).</summary>
public enum SeriesWindowPhase
{
    Confirm,
    Running,
    Result,
}

/// <summary>
/// Okno serii (<c>Views/SeriesWindow</c>): potwierdzenie z <see cref="SeriesConfirmation"/>, po zgodzie postęp i wynik
/// z <see cref="BatchRun"/>. Każdy tekst kroku i nagłówka wyniku pochodzi z <see cref="BatchRun"/> (wynik silnika).
/// Wołać wyłącznie na wątku UI: postęp silnika przychodzi przez <see cref="Progress{T}"/> utworzony na wątku UI.
/// W trakcie postępu okna nie da się zamknąć (<see cref="CanClose"/>), „Zatrzymaj” tylko ustawia sygnał między krokami.
/// Tryb długiej naprawy (<see cref="ForAction"/>): zamiast kroków serii blok postępu z <see cref="ActionRun"/> (procent, etap,
/// „Krok N z M”), „Zatrzymaj po tym kroku” = anulowanie tokenu handlera, a wynik z <see cref="TweakResult"/>.
/// </summary>
public sealed class SeriesWindowViewModel : ObservableObject
{
    public static string StopText => L.T("Zatrzymaj");

    public static string StoppingText => L.T("Zatrzymywanie…");

    public static string StopNote => L.T("Zatrzymanie kończy bieżący krok i nie zaczyna następnych.");

    private readonly SeriesConfirmation? _confirmation;
    private readonly Func<IProgress<BatchStepEvent>, BatchStop, Task<GuiOperationReport>>? _run;
    private readonly Action<string>? _copyResult;
    private readonly Func<Task<bool>>? _confirmRestart;
    private readonly IClock _clock;
    private readonly BatchStop _stop = new();
    private Func<IProgress<ActionProgress>, CancellationToken, Task<GuiOperationReport>>? _runAction;
    private ActionRun? _action;
    private CancellationTokenSource? _actionStop;
    private SeriesWindowPhase _phase = SeriesWindowPhase.Confirm;
    private TaskCompletionSource<bool>? _question;
    private BatchRun? _batch;
    private IReadOnlyList<SeriesStepItem> _steps = Array.Empty<SeriesStepItem>();
    private bool _isStopping;

    /// <param name="confirmation">Treść potwierdzenia; po zgodzie także kroki postępu.</param>
    /// <param name="run">Seria do wykonania po zgodzie; null = samo potwierdzenie (okno zamyka się po zgodzie).</param>
    /// <param name="copyResult">„Kopiuj wynik”: tekst wyniku do schowka i linia w dzienniku.</param>
    /// <param name="confirmRestart">„Uruchom ponownie teraz” w wyniku; null = przycisku nie ma.</param>
    public SeriesWindowViewModel(
        SeriesConfirmation confirmation,
        Func<IProgress<BatchStepEvent>, BatchStop, Task<GuiOperationReport>>? run,
        Action<string>? copyResult,
        Func<Task<bool>>? confirmRestart,
        IClock clock)
        : this(
            confirmation?.Title ?? throw new ArgumentNullException(nameof(confirmation)),
            confirmation.Subtitle,
            null,
            confirmation.Facts,
            confirmation.Items.Select(item => new SeriesItemViewModel(item, confirmation.Operation is not (GuiOperation.RemoveApps or GuiOperation.InstallApps))).ToList(),
            confirmation.PrimaryText,
            clock)
    {
        _confirmation = confirmation;
        ConfirmWarnings = confirmation.Warnings;
        _run = run;
        _copyResult = copyResult;
        _confirmRestart = confirmRestart;
    }

    private SeriesWindowViewModel(
        string title,
        string subtitle,
        string? message,
        IReadOnlyList<SeriesFact> facts,
        IReadOnlyList<SeriesItemViewModel> items,
        string primaryText,
        IClock clock)
    {
        _clock = clock ?? throw new ArgumentNullException(nameof(clock));
        ConfirmTitle = title;
        ConfirmSubtitle = subtitle;
        Message = message;
        Facts = facts;
        Items = items;
        PrimaryText = primaryText;

        PrimaryCommand = new AsyncRelayCommand(ConfirmAsync, () => Phase == SeriesWindowPhase.Confirm);
        CancelCommand = new RelayCommand(Cancel, () => Phase == SeriesWindowPhase.Confirm);
        StopCommand = new RelayCommand(Stop, () => Phase == SeriesWindowPhase.Running && !IsStopping && (_action is null || _action.CanStop));
        CopyResultCommand = new RelayCommand(CopyResult, () => Phase == SeriesWindowPhase.Result);
        CloseCommand = new RelayCommand(RequestClose, () => Phase == SeriesWindowPhase.Result);
        RestartCommand = new AsyncRelayCommand(RestartAsync, () => ShowRestartButton);
        ContinueCommand = new RelayCommand(() => Answer(true), () => IsAsking);
        DeclineCommand = new RelayCommand(() => Answer(false), () => IsAsking);
    }

    /// <summary>
    /// Okno długiej naprawy (<see cref="GuiRules.UsesProgressWindow"/>): to samo potwierdzenie, potem postęp w procentach z
    /// <see cref="ActionRun"/> (wariant B: duży procent obok etapu, pasek, „Krok N z M”) i wynik z <see cref="TweakResult"/>.
    /// <paramref name="run"/> dostaje postęp utworzony na wątku UI i token „Zatrzymaj po tym kroku”.
    /// </summary>
    public static SeriesWindowViewModel ForAction(
        SeriesConfirmation confirmation,
        Func<IProgress<ActionProgress>, CancellationToken, Task<GuiOperationReport>> run,
        Action<string>? copyResult,
        Func<Task<bool>>? confirmRestart,
        IClock clock)
    {
        if (confirmation is null)
        {
            throw new ArgumentNullException(nameof(confirmation));
        }

        // Ten sam warunek co ActionRun – błędne potwierdzenie wychodzi przy otwarciu okna, nie po zgodzie.
        if (confirmation.Operation != GuiOperation.Execute || confirmation.Items.Count != 1)
        {
            throw new ArgumentException("Okno postępu akcji dotyczy dokładnie jednej pozycji uruchamianej przez Execute.", nameof(confirmation));
        }

        return new SeriesWindowViewModel(confirmation, null, copyResult, confirmRestart, clock)
        {
            _runAction = run ?? throw new ArgumentNullException(nameof(run)),
        };
    }

    /// <summary>Prośba o zamknięcie okna (zgoda bez serii, anulowanie, „Zamknij”, restart).</summary>
    public event EventHandler? CloseRequested;

    /// <summary>Potwierdzenie ponownego uruchomienia: tytuł „Uruchom ponownie”, treść <see cref="GuiText.RestartConfirmText"/>.</summary>
    public static SeriesWindowViewModel ForRestart(IClock clock)
        => new(L.T("Uruchom ponownie"), string.Empty, GuiText.RestartConfirmText, Array.Empty<SeriesFact>(), Array.Empty<SeriesItemViewModel>(), L.T("Uruchom ponownie"), clock);

    public SeriesWindowPhase Phase
    {
        get => _phase;
        private set
        {
            if (SetProperty(ref _phase, value))
            {
                OnPropertyChanged(nameof(IsConfirm));
                OnPropertyChanged(nameof(IsRunning));
                OnPropertyChanged(nameof(IsResult));
                OnPropertyChanged(nameof(ShowStopFooter));
                OnPropertyChanged(nameof(CanClose));
                OnPropertyChanged(nameof(ShowSeriesProgressLine));
                OnPropertyChanged(nameof(ShowActionPanel));
                OnHeaderChanged();
                OnActionChanged();
                OnResultChanged();
                PrimaryCommand.NotifyCanExecuteChanged();
                CancelCommand.NotifyCanExecuteChanged();
                StopCommand.NotifyCanExecuteChanged();
                CopyResultCommand.NotifyCanExecuteChanged();
                CloseCommand.NotifyCanExecuteChanged();
            }
        }
    }

    public bool IsConfirm => Phase == SeriesWindowPhase.Confirm;

    public bool IsRunning => Phase == SeriesWindowPhase.Running;

    public bool IsResult => Phase == SeriesWindowPhase.Result;

    /// <summary>X, Esc i Alt+F4 działają poza postępem; w trakcie serii okna nie da się zamknąć.</summary>
    public bool CanClose => Phase != SeriesWindowPhase.Running;

    public string ConfirmTitle { get; }

    public string ConfirmSubtitle { get; }

    /// <summary>Pasek tytułu: w potwierdzeniu tytuł okna, potem tytuł postępu albo wyniku z <see cref="ActionRun"/>/<see cref="BatchRun"/>.</summary>
    public string HeaderTitle => _action?.Title ?? _batch?.Title ?? ConfirmTitle;

    public string HeaderSubtitle => _action?.Subtitle ?? _batch?.Subtitle ?? ConfirmSubtitle;

    /// <summary>Okno długiej naprawy (<see cref="ForAction"/>) zamiast serii kroków.</summary>
    public bool IsActionMode => _runAction is not null;

    /// <summary>Blok postępu akcji (procent, etap, pasek) w postępie i wyniku.</summary>
    public bool ShowActionPanel => IsActionMode && !IsConfirm;

    /// <summary>Cienka linia 3 px pod paskiem tytułu – tylko seria; okno akcji ma pasek w treści (Ruling R6).</summary>
    public bool ShowSeriesProgressLine => IsRunning && !IsActionMode;

    /// <summary>Procent jest znany; przy nieokreślonym okno pokazuje sam etap, bez liczby i bez paska.</summary>
    public bool HasActionPercent => _action?.Percent is not null;

    public string? ActionPercentText
        => _action?.Percent is { } percent ? percent.ToString(CultureInfo.InvariantCulture) + "%" : null;

    /// <summary>Etap w trakcie, tytuł pozycji w wyniku.</summary>
    public string? ActionStage => _action?.Stage;

    /// <summary>„Krok 1 z 2” w trakcie; pusty przed pierwszym zdarzeniem i w wyniku.</summary>
    public string? ActionStepText => _action?.StepText;

    public bool HasActionStepText => !string.IsNullOrEmpty(ActionStepText);

    /// <summary>„Dalej: …” obok kroku (wariant B); null na ostatnim kroku i w wyniku.</summary>
    public string? ActionNextStageText => _action?.NextStageText;

    public bool HasActionNextStage => ActionNextStageText is not null;

    /// <summary>Podpowiedź pod paskiem – tylko w trakcie.</summary>
    public string? ActionHint => IsActionMode && IsRunning ? GuiText.ActionProgressHint : null;

    /// <summary>Pasek postępu akcji (6 px): procent jako proporcje kolumn.</summary>
    public GridLength ActionBarDone => new((_action?.Percent ?? 0) / 100.0, GridUnitType.Star);

    public GridLength ActionBarRest => new(1 - ((_action?.Percent ?? 0) / 100.0), GridUnitType.Star);

    /// <summary>Treść wyniku akcji (komunikat <see cref="TweakResult.Message"/> linia po linii); w trakcie pusta.</summary>
    public IReadOnlyList<string> ActionResultLines => IsResult && _action is not null ? _action.ResultLines : Array.Empty<string>();

    public bool HasActionResultLines => ActionResultLines.Count > 0;

    /// <summary>Treść zwykłego potwierdzenia (restart); null w potwierdzeniu serii.</summary>
    public string? Message { get; }

    public bool HasMessage => !string.IsNullOrEmpty(Message);

    public IReadOnlyList<SeriesFact> Facts { get; }

    public bool HasFacts => Facts.Count > 0;

    public IReadOnlyList<SeriesItemViewModel> Items { get; }

    public bool HasItems => Items.Count > 0;

    /// <summary>Ostrzeżenia całej serii pod faktami (np. brak cofania usunięcia, inne konto procesu).</summary>
    public IReadOnlyList<string> ConfirmWarnings { get; } = Array.Empty<string>();

    public bool HasConfirmWarnings => ConfirmWarnings.Count > 0;

    /// <summary>
    /// Pytanie „Kontynuować bez punktu przywracania?” w trakcie postępu (seria usuwania): pod krokami treść pytania, w stopce
    /// „Tak, kontynuuj” i domyślne „Nie”; „Zatrzymaj” schowane do odpowiedzi.
    /// </summary>
    public bool IsAsking => _question is not null;

    /// <summary>Stopka postępu („Zatrzymaj”) – poza pytaniem.</summary>
    public bool ShowStopFooter => IsRunning && !IsAsking && (!IsActionMode || (_action?.IsStoppable ?? true));

    public string AskTitle { get; } = AppsText.ContinueWithoutRestorePoint;

    public string AskBody { get; } = AppsPageText.ContinueWithoutRestorePointBody;

    public string AskNote { get; } = AppsPageText.ContinueWithoutRestorePointNote;

    public string ContinueText { get; } = AppsPageText.ContinueButton;

    public string DeclineText { get; } = AppsPageText.DeclineButton;

    public IRelayCommand ContinueCommand { get; }

    public IRelayCommand DeclineCommand { get; }

    public string PrimaryText { get; }

    /// <summary>Kroki postępu i wyniku (podmieniane w całości po każdej zmianie <see cref="BatchRun"/>).</summary>
    public IReadOnlyList<SeriesStepItem> Steps
    {
        get => _steps;
        private set => SetProperty(ref _steps, value);
    }

    /// <summary>Linia postępu: część ukończona i reszta jako proporcje kolumn.</summary>
    public GridLength ProgressDone => new(Math.Max(0, _batch?.Progress ?? 0), GridUnitType.Star);

    public GridLength ProgressRest => new(Math.Max(0, 1 - (_batch?.Progress ?? 0)), GridUnitType.Star);

    public bool IsStopping
    {
        get => _isStopping;
        private set
        {
            if (SetProperty(ref _isStopping, value))
            {
                OnPropertyChanged(nameof(StopButtonText));
                StopCommand.NotifyCanExecuteChanged();
            }
        }
    }

    /// <summary>
    /// Seria: „Zatrzymaj”; akcja: „Zatrzymaj po tym kroku”, po kliknięciu „Zatrzymam po bieżącym kroku…”; instalacja (spec 5.2):
    /// „Zatrzymaj po tej aplikacji”, po kliknięciu „Zatrzymam po bieżącej aplikacji…”.
    /// </summary>
    public string StopButtonText => IsActionMode
        ? (IsStopping ? GuiText.ActionStoppingText : GuiText.ActionStopText)
        : IsInstallMode
            ? (IsStopping ? InstallText.StoppingText : InstallText.StopText)
            : (IsStopping ? StoppingText : StopText);

    /// <summary>Seria instalacji aplikacji (<see cref="GuiOperation.InstallApps"/>): kroki to aplikacje, bez miernika ryzyka.</summary>
    public bool IsInstallMode => _confirmation?.Operation == GuiOperation.InstallApps;

    /// <summary>Notatka obok „Zatrzymaj” w stopce postępu (instalacja: bieżąca zawsze się kończy).</summary>
    public string StopNoteText => IsInstallMode ? InstallText.StopNote : StopNote;

    public IReadOnlyList<string> Warnings => !IsResult ? Array.Empty<string>() : _action?.Warnings ?? _batch?.Warnings ?? Array.Empty<string>();

    public bool HasWarnings => Warnings.Count > 0;

    public string? RebootText => IsResult ? _action?.RebootText ?? _batch?.RebootText : null;

    public bool HasReboot => RebootText is not null;

    /// <summary>Linia oddzielająca kroki od ostrzeżeń i bloku restartu.</summary>
    public bool HasResultNotes => HasWarnings || HasReboot;

    /// <summary>„Uruchom ponownie teraz”: tylko w wyniku z restartem i tylko gdy okno dostało potwierdzenie restartu.</summary>
    public bool ShowRestartButton => IsResult && _confirmRestart is not null && ((_batch?.RebootCount ?? 0) > 0 || _action?.RequiresReboot == true);

    public IAsyncRelayCommand PrimaryCommand { get; }

    public IRelayCommand CancelCommand { get; }

    public IRelayCommand StopCommand { get; }

    public IRelayCommand CopyResultCommand { get; }

    public IRelayCommand CloseCommand { get; }

    public IAsyncRelayCommand RestartCommand { get; }

    /// <summary>True po kliknięciu przycisku głównego potwierdzenia.</summary>
    public bool Confirmed { get; private set; }

    /// <summary>Raport serii (także syntetyczny po wyjątku); null, dopóki seria nie ruszyła.</summary>
    public GuiOperationReport? Report { get; private set; }

    public bool RestartRequested { get; private set; }

    /// <summary>
    /// Pyta w tym oknie, czy kontynuować serię bez punktu przywracania (wołać na wątku UI w trakcie postępu). Wynik: true =
    /// „Tak, kontynuuj”, false = „Nie” (domyślny przycisk, także Esc). Poza postępem albo przy pytaniu już otwartym = false.
    /// </summary>
    public Task<bool> AskContinueWithoutRestorePointAsync()
    {
        if (Phase != SeriesWindowPhase.Running || _question is not null)
        {
            return Task.FromResult(false);
        }

        _question = new TaskCompletionSource<bool>();
        OnQuestionChanged();
        return _question.Task;
    }

    /// <summary>Esc: w potwierdzeniu Anuluj, przy pytaniu „Nie”, w wyniku Zamknij, w trakcie postępu nic.</summary>
    public void HandleEscape()
    {
        if (IsAsking)
        {
            Answer(false);
        }
        else if (Phase == SeriesWindowPhase.Confirm)
        {
            Cancel();
        }
        else if (Phase == SeriesWindowPhase.Result)
        {
            RequestClose();
        }
    }

    private async Task ConfirmAsync()
    {
        if (Phase != SeriesWindowPhase.Confirm)
        {
            return;
        }

        Confirmed = true;
        if (_runAction is not null && _confirmation is not null)
        {
            await RunActionAsync(_runAction, _confirmation).ConfigureAwait(true);
            return;
        }

        if (_run is null || _confirmation is null)
        {
            RequestClose();
            return;
        }

        var batch = new BatchRun(_confirmation);
        _batch = batch;
        batch.Changed += (_, _) => OnBatchChanged();

        // Progress utworzony tutaj, na wątku UI: zdarzenia silnika z wątku roboczego wracają przez asynchroniczny Post.
        var progress = new Progress<BatchStepEvent>(batch.OnStep);
        try
        {
            // W try: wyjątek przy przejściu w postęp też kończy w wyniku (inaczej okno zostałoby w postępie z nieaktywnym X).
            Phase = SeriesWindowPhase.Running;
            OnBatchChanged();
            var report = await _run(progress, _stop).ConfigureAwait(true);
            Report = report;
            batch.Complete(report);
        }
        catch (Exception ex)
        {
            var message = L.F("Nieoczekiwany błąd: {0}: {1}", ex.GetType().Name, ex.Message);
            batch.Fail(message);
            Report = FailureReport(message);
        }

        // Pytanie bez odpowiedzi (silnik przestał czekać – limit albo zamykanie) nie zostaje w wyniku.
        Answer(false);
        Phase = SeriesWindowPhase.Result;
        OnBatchChanged();
    }

    /// <summary>
    /// Długa naprawa po zgodzie: postęp z <see cref="ActionRun"/> (Progress utworzony na wątku UI), „Zatrzymaj po tym kroku” =
    /// anulowanie tokenu, a po końcu wynik w tym samym oknie. Wyjątek z <paramref name="run"/> kończy w wyniku z błędem.
    /// </summary>
    private async Task RunActionAsync(
        Func<IProgress<ActionProgress>, CancellationToken, Task<GuiOperationReport>> run,
        SeriesConfirmation confirmation)
    {
        var action = new ActionRun(confirmation);
        _action = action;
        action.Changed += (_, _) => OnActionChanged();
        var progress = new Progress<ActionProgress>(action.OnProgress);
        using (var stop = new CancellationTokenSource())
        {
            _actionStop = stop;
            try
            {
                Phase = SeriesWindowPhase.Running;
                var report = await run(progress, stop.Token).ConfigureAwait(true);
                Report = report;
                action.Complete(report);
            }
            catch (Exception ex)
            {
                var message = L.F("Nieoczekiwany błąd: {0}: {1}", ex.GetType().Name, ex.Message);
                action.Fail(message);
                Report = FailureReport(message);
            }
            finally
            {
                _actionStop = null;
            }
        }

        Phase = SeriesWindowPhase.Result;
    }

    /// <summary>Raport zastępczy po wyjątku: jedna linia Error, bez odświeżonych stanów (MainViewModel dopisuje ją do dziennika).</summary>
    private GuiOperationReport FailureReport(string message) => new()
    {
        TweakId = _action is not null ? _confirmation!.Items[0].Id : "seria",
        Operation = _confirmation!.Operation,
        Lines = new[] { new GuiLogLine(_clock.UtcNow, GuiLogLevel.Error, message) },
    };

    private void Cancel()
    {
        if (Phase == SeriesWindowPhase.Confirm)
        {
            Confirmed = false;
            RequestClose();
        }
    }

    private void Stop()
    {
        if (Phase != SeriesWindowPhase.Running || IsStopping)
        {
            return;
        }

        if (_action is not null)
        {
            // Tylko przed ostatnim krokiem (ActionRun.CanStop); handler sprawdza token wyłącznie między krokami.
            if (!_action.RequestStop())
            {
                return;
            }

            _actionStop?.Cancel();
        }
        else
        {
            _stop.Request();
        }

        IsStopping = true;
    }

    private void CopyResult()
    {
        if (Phase != SeriesWindowPhase.Result)
        {
            return;
        }

        var text = _action?.ResultText() ?? _batch?.ResultText();
        if (text is not null)
        {
            _copyResult?.Invoke(text);
        }
    }

    private async Task RestartAsync()
    {
        if (!ShowRestartButton || _confirmRestart is null)
        {
            return;
        }

        if (await _confirmRestart().ConfigureAwait(true))
        {
            RestartRequested = true;
            RequestClose();
        }
    }

    private void RequestClose() => CloseRequested?.Invoke(this, EventArgs.Empty);

    private void Answer(bool proceed)
    {
        var question = _question;
        if (question is null)
        {
            return;
        }

        _question = null;
        OnQuestionChanged();
        question.TrySetResult(proceed);
    }

    private void OnQuestionChanged()
    {
        OnPropertyChanged(nameof(IsAsking));
        OnPropertyChanged(nameof(ShowStopFooter));
        ContinueCommand.NotifyCanExecuteChanged();
        DeclineCommand.NotifyCanExecuteChanged();
        OnBatchChanged();
    }

    private void OnBatchChanged()
    {
        if (_batch is null)
        {
            return;
        }

        // Szczegóły kroków w wyniku i przy pytaniu o punkt przywracania (powód, dla którego punkt nie powstał).
        var details = Phase == SeriesWindowPhase.Result || IsAsking;
        var steps = _batch.Steps;
        Steps = steps
            .Select((step, index) => new SeriesStepItem(step.Title, step.State, step.StatusText, step.Details, details && step.Details.Count > 0, index == steps.Count - 1))
            .ToList();
        OnPropertyChanged(nameof(ProgressDone));
        OnPropertyChanged(nameof(ProgressRest));
        OnHeaderChanged();
        OnResultChanged();
    }

    private void OnActionChanged()
    {
        if (_action is null)
        {
            return;
        }

        OnPropertyChanged(nameof(HasActionPercent));
        OnPropertyChanged(nameof(ActionPercentText));
        OnPropertyChanged(nameof(ActionStage));
        OnPropertyChanged(nameof(ActionStepText));
        OnPropertyChanged(nameof(HasActionStepText));
        OnPropertyChanged(nameof(ActionNextStageText));
        OnPropertyChanged(nameof(HasActionNextStage));
        OnPropertyChanged(nameof(ActionHint));
        OnPropertyChanged(nameof(ActionBarDone));
        OnPropertyChanged(nameof(ActionBarRest));
        OnPropertyChanged(nameof(ActionResultLines));
        OnPropertyChanged(nameof(HasActionResultLines));
        StopCommand.NotifyCanExecuteChanged();
        OnHeaderChanged();
        OnResultChanged();
    }

    private void OnHeaderChanged()
    {
        OnPropertyChanged(nameof(HeaderTitle));
        OnPropertyChanged(nameof(HeaderSubtitle));
    }

    private void OnResultChanged()
    {
        OnPropertyChanged(nameof(Warnings));
        OnPropertyChanged(nameof(HasWarnings));
        OnPropertyChanged(nameof(RebootText));
        OnPropertyChanged(nameof(HasReboot));
        OnPropertyChanged(nameof(HasResultNotes));
        OnPropertyChanged(nameof(ShowRestartButton));
        RestartCommand.NotifyCanExecuteChanged();
    }
}

/// <summary>Pozycja potwierdzenia serii z rozwijanymi „Szczegółami technicznymi”.</summary>
public sealed class SeriesItemViewModel : ObservableObject
{
    private bool _isExpanded;

    /// <param name="showRisk">Miernik ryzyka przed tytułem (tweaki); usuwanie aplikacji go nie ma (render Tasku 1).</param>
    public SeriesItemViewModel(SeriesItem item, bool showRisk = true)
    {
        if (item is null)
        {
            throw new ArgumentNullException(nameof(item));
        }

        Title = item.Title;
        Risk = item.Risk;
        Summary = item.Summary;
        Notes = item.Notes;
        Warnings = item.Warnings;
        Technical = item.Technical;
        RiskColumnWidth = new GridLength(showRisk ? 30 : 0);
        ShowRisk = showRisk;
        ToggleCommand = new RelayCommand(() => IsExpanded = !IsExpanded);
    }

    public string Title { get; }

    public RiskLevel Risk { get; }

    public bool ShowRisk { get; }

    /// <summary>Kolumna miernika ryzyka: 30 px albo 0 (usuwanie aplikacji).</summary>
    public GridLength RiskColumnWidth { get; }

    public string Summary { get; }

    public IReadOnlyList<string> Notes { get; }

    public IReadOnlyList<string> Warnings { get; }

    public IReadOnlyList<SeriesFact> Technical { get; }

    public bool HasTechnical => Technical.Count > 0;

    public bool IsExpanded
    {
        get => _isExpanded;
        set
        {
            if (SetProperty(ref _isExpanded, value))
            {
                OnPropertyChanged(nameof(ToggleText));
            }
        }
    }

    public string ToggleText => IsExpanded ? L.T("Ukryj szczegóły techniczne") : L.T("Szczegóły techniczne");

    public IRelayCommand ToggleCommand { get; }
}

/// <summary>Wiersz kroku w postępie i wyniku (migawka <see cref="BatchRunStep"/>); szczegóły tylko w wyniku.</summary>
public sealed record SeriesStepItem(
    string Title,
    BatchStepState State,
    string StatusText,
    IReadOnlyList<string> Details,
    bool ShowDetails,
    bool IsLast);
