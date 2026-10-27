using System;
using System.ComponentModel;
using System.Windows;
using System.Windows.Input;
using System.Windows.Shell;
using System.Windows.Threading;
using DSOptimizer.App.ViewModels;
using DSOptimizer.Core.Gui;

namespace DSOptimizer.App.Views;

/// <summary>
/// Okno serii (makiety 1–3): potwierdzenie, postęp i wynik w jednym oknie. Szerokość <see cref="DialogWidth"/>, wysokość
/// wg treści, najwyżej <see cref="WorkAreaHeightShare"/> obszaru roboczego (treść przewijana, stopka zawsze widoczna).
/// W trakcie postępu X, Esc i Alt+F4 nic nie robią (<see cref="SeriesWindowViewModel.CanClose"/>).
/// </summary>
public partial class SeriesWindow
{
    /// <summary>Szerokość okna (DIP).</summary>
    internal const double DialogWidth = 600;

    /// <summary>Część wysokości obszaru roboczego, której okno nie przekracza.</summary>
    internal const double WorkAreaHeightShare = 0.85;

    /// <summary>Wysokość paska tytułu (obszar podpisu WindowChrome).</summary>
    private const double HeadHeight = 44;

    private readonly SeriesWindowViewModel _viewModel;

    public SeriesWindow(SeriesWindowViewModel viewModel, double workAreaWidth, double workAreaHeight)
    {
        _viewModel = viewModel ?? throw new ArgumentNullException(nameof(viewModel));
        InitializeComponent();
        DataContext = viewModel;
        if (workAreaHeight > 0)
        {
            MaxHeight = workAreaHeight * WorkAreaHeightShare;
        }

        if (workAreaWidth > 0)
        {
            Width = Math.Min(DialogWidth, workAreaWidth * 0.95);
        }

        viewModel.CloseRequested += (_, _) => Close();
        viewModel.PropertyChanged += OnViewModelPropertyChanged;
        SizeChanged += OnSizeChanged;
    }

    /// <summary>Pokazuje okno modalnie nad <paramref name="owner"/> (gdy jest załadowane; inaczej na środku ekranu).</summary>
    public static void ShowFor(Window? owner, SeriesWindowViewModel viewModel)
    {
        var area = SystemParameters.WorkArea;
        var window = new SeriesWindow(viewModel, area.Width, area.Height);
        if (owner is { IsLoaded: true })
        {
            window.Owner = owner;
        }
        else
        {
            window.WindowStartupLocation = WindowStartupLocation.CenterScreen;
        }

        window.ShowDialog();
    }

    /// <summary>
    /// FluentWindow ustawia WindowChrome w OnSourceInitialized (CaptionHeight = 0, bo zwykle podpis robi ui:TitleBar).
    /// Własny pasek tytułu staje się obszarem podpisu (jak pasek marki okna głównego); krzyżyk ma IsHitTestVisibleInChrome.
    /// </summary>
    protected override void OnSourceInitialized(EventArgs e)
    {
        base.OnSourceInitialized(e);
        var chrome = WindowChrome.GetWindowChrome(this);
        if (chrome is null)
        {
            return;
        }

        if (chrome.IsFrozen)
        {
            chrome = (WindowChrome)chrome.Clone();
        }

        chrome.CaptionHeight = HeadHeight;
        WindowChrome.SetWindowChrome(this, chrome);
    }

    /// <summary>Po zmianie stanu wysokość się zmienia (SizeToContent): środek okna zostaje w miejscu.</summary>
    private void OnSizeChanged(object sender, SizeChangedEventArgs e)
    {
        if (!e.HeightChanged || e.PreviousSize.Height <= 0 || !IsLoaded)
        {
            return;
        }

        Top = Math.Max(SystemParameters.VirtualScreenTop, Top + ((e.PreviousSize.Height - e.NewSize.Height) / 2));
    }

    /// <summary>
    /// W postępie krok „W toku” zawsze w widoku: przy długiej serii (dziesiątki kroków) treść się przewija. Po nowej liście
    /// kroków czeka na ułożenie (priorytet Loaded), potem BringIntoView kontenera kroku.
    /// </summary>
    private void OnViewModelPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(SeriesWindowViewModel.Steps) && _viewModel.IsRunning)
        {
            Dispatcher.BeginInvoke(DispatcherPriority.Loaded, new Action(BringRunningStepIntoView));
        }

        // Pytanie o kontynuację bez punktu przywracania: fokus na domyślnym „Nie” (Enter = „Nie”), pytanie w widoku.
        if (e.PropertyName == nameof(SeriesWindowViewModel.IsAsking) && _viewModel.IsAsking)
        {
            Dispatcher.BeginInvoke(DispatcherPriority.Loaded, new Action(() =>
            {
                Activate();
                DeclineButton.Focus();
                QuestionPanel.BringIntoView();
            }));
        }
    }

    private void BringRunningStepIntoView()
    {
        if (!_viewModel.IsRunning)
        {
            return;
        }

        var steps = _viewModel.Steps;
        for (var i = 0; i < steps.Count; i++)
        {
            if (steps[i].State == BatchStepState.Running)
            {
                (StepList.ItemContainerGenerator.ContainerFromIndex(i) as FrameworkElement)?.BringIntoView();
                return;
            }
        }
    }

    private void OnClosing(object? sender, CancelEventArgs e)
    {
        if (!_viewModel.CanClose)
        {
            e.Cancel = true;
        }
    }

    /// <summary>Esc: Anuluj w potwierdzeniu, „Nie” przy pytaniu o punkt przywracania, Zamknij w wyniku, w trakcie postępu ignorowany.</summary>
    private void OnPreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Escape)
        {
            e.Handled = true;
            _viewModel.HandleEscape();
        }
    }

    /// <summary>Krzyżyk: zamknięcie przez Closing (w potwierdzeniu = Anuluj, bo zgoda jest tylko po przycisku głównym).</summary>
    private void OnCloseClick(object sender, RoutedEventArgs e) => SystemCommands.CloseWindow(this);
}
