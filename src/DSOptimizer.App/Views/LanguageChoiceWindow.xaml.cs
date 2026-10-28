using System;
using System.Windows;
using System.Windows.Input;
using System.Windows.Shell;
using DSOptimizer.App.ViewModels;
using DSOptimizer.Core.Localization;

namespace DSOptimizer.App.Views;

/// <summary>
/// Okno wyboru języka przy pierwszym uruchomieniu (przed oknem głównym, jedyne okno programu w tej chwili – stąd środek ekranu
/// i pasek zadań). Klik kafla = wybór i zamknięcie; Esc i krzyżyk = zamknięcie bez wyboru (program bierze język wykryty).
/// Kafel wykrytego języka ma IsDefault (Enter) i początkowy fokus klawiatury; Tab przechodzi między kaflami.
/// </summary>
public partial class LanguageChoiceWindow
{
    /// <summary>Wysokość paska tytułu (obszar podpisu WindowChrome).</summary>
    private const double HeadHeight = 44;

    public LanguageChoiceWindow(LanguageChoiceViewModel viewModel)
    {
        if (viewModel is null)
        {
            throw new ArgumentNullException(nameof(viewModel));
        }

        InitializeComponent();
        DataContext = viewModel;
        viewModel.CloseRequested += (_, _) => Close();
        Loaded += (_, _) => (viewModel.IsEnglishDetected ? EnglishTile : PolishTile).Focus();
    }

    /// <summary>
    /// Pokazuje okno modalnie i zwraca wybrany język; <c>null</c> = zamknięte bez wyboru. Wołane przed <see cref="Application.Run()"/>:
    /// na czas okna <see cref="Application.ShutdownMode"/> = <see cref="ShutdownMode.OnExplicitShutdown"/>, bo zamknięcie jedynego okna
    /// zakończyłoby aplikację, zanim powstanie okno główne; potem poprzednia wartość wraca.
    /// </summary>
    public static UiLanguage? Ask(UiLanguage detected)
    {
        var viewModel = new LanguageChoiceViewModel(detected);
        var app = Application.Current;
        var previousMode = app?.ShutdownMode ?? ShutdownMode.OnLastWindowClose;
        if (app is not null)
        {
            app.ShutdownMode = ShutdownMode.OnExplicitShutdown;
        }

        try
        {
            var window = new LanguageChoiceWindow(viewModel);
            window.ShowDialog();
            // Pierwsze okno aplikacji zostaje jej MainWindow; okno główne programu ma nim być od app.Run.
            if (app is not null && ReferenceEquals(app.MainWindow, window))
            {
                app.MainWindow = null;
            }
        }
        finally
        {
            if (app is not null)
            {
                app.ShutdownMode = previousMode;
            }
        }

        return viewModel.Answer;
    }

    /// <summary>
    /// FluentWindow ustawia WindowChrome w OnSourceInitialized (CaptionHeight = 0, bo zwykle podpis robi ui:TitleBar).
    /// Własny pasek tytułu staje się obszarem podpisu (jak w oknie serii); krzyżyk ma IsHitTestVisibleInChrome.
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

    /// <summary>Esc: zamknięcie bez wyboru.</summary>
    private void OnPreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Escape)
        {
            e.Handled = true;
            Close();
        }
    }

    /// <summary>Krzyżyk: zamknięcie bez wyboru.</summary>
    private void OnCloseClick(object sender, RoutedEventArgs e) => Close();
}
