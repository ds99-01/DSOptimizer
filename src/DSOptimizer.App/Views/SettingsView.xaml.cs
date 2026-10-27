using System.Windows;
using DSOptimizer.Core.Gui;

namespace DSOptimizer.App.Views;

public partial class SettingsView
{
    public SettingsView()
    {
        InitializeComponent();
    }

    /// <summary>Kolumna „O programie” ma szerokość panelu szczegółów: 1/3 treści w granicach z <see cref="GuiLayout"/>.</summary>
    private void OnSizeChanged(object sender, SizeChangedEventArgs e)
    {
        if (e.NewSize.Width > 0 && !double.IsNaN(e.NewSize.Width))
        {
            AboutColumn.Width = new GridLength(GuiLayout.DetailColumnWidth(e.NewSize.Width));
        }
    }
}
