using System.Windows;
using System.Windows.Controls;

namespace DSOptimizer.App.Views;

/// <summary>
/// Szerokość listy wyboru ze stylem <c>BrandComboBox</c>: przy <see cref="FitWidestOptionProperty"/> = true pole ma szerokość najdłuższej opcji
/// w bieżącym języku, a nie wybranej. Szablon mierzy wtedy wszystkie opcje niewidocznymi napisami w tej samej komórce co wybrana, więc pomiar
/// robi sam WPF (ta sama czcionka i zaokrąglenia). Wybór opcji nie zmienia szerokości pola ani układu wiersza (wiersz szukania).
/// </summary>
public static class ComboBoxSizing
{
    public static readonly DependencyProperty FitWidestOptionProperty = DependencyProperty.RegisterAttached(
        "FitWidestOption", typeof(bool), typeof(ComboBoxSizing), new FrameworkPropertyMetadata(false, FrameworkPropertyMetadataOptions.AffectsMeasure));

    public static bool GetFitWidestOption(ComboBox element) => element is not null && (bool)element.GetValue(FitWidestOptionProperty);

    public static void SetFitWidestOption(ComboBox element, bool value) => element?.SetValue(FitWidestOptionProperty, value);
}
