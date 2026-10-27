using System;
using System.Windows;
using DSOptimizer.Core;

namespace DSOptimizer.App.Views;

/// <summary>
/// Okno „Licencje komponentów”: treść <c>THIRD_PARTY_NOTICES.md</c> (jako zwykły tekst, bez znaczników Markdown) osadzona w exe (tylko do odczytu, zaznaczanie, przewijanie).
/// Modalne nad oknem głównym; wysokość najwyżej <see cref="WorkAreaHeightShare"/> obszaru roboczego.
/// </summary>
public partial class LicensesWindow
{
    /// <summary>Część wysokości obszaru roboczego, której okno nie przekracza.</summary>
    internal const double WorkAreaHeightShare = 0.8;

    public LicensesWindow(string notices, double workAreaWidth, double workAreaHeight)
    {
        InitializeComponent();
        NoticesBox.Text = ThirdPartyNotices.ToPlainText(notices);
        if (workAreaHeight > 0)
        {
            MaxHeight = workAreaHeight * WorkAreaHeightShare;
            Height = Math.Min(Height, MaxHeight);
            MinHeight = Math.Min(MinHeight, MaxHeight);
        }

        if (workAreaWidth > 0)
        {
            Width = Math.Min(Width, workAreaWidth * 0.9);
            MinWidth = Math.Min(MinWidth, Width);
        }
    }

    /// <summary>Pokazuje okno modalnie nad <paramref name="owner"/> (gdy jest załadowane; inaczej na środku ekranu).</summary>
    public static void ShowFor(Window? owner, string notices)
    {
        var area = SystemParameters.WorkArea;
        var window = new LicensesWindow(notices, area.Width, area.Height);
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

    private void OnCloseClick(object sender, RoutedEventArgs e) => Close();
}
