using System.Windows;
using System.Windows.Controls;
using System.Windows.Shapes;

namespace DSOptimizer.App.Views;

/// <summary>
/// Ikona stylu A (kontur 1,5 px, siatka 20×20, kolor z <see cref="Control.Foreground"/>). <see cref="Kind"/> to rodzaj z
/// <c>GuiIcons</c> (np. „Disk”); geometrie to zasoby <c>Icon{Kind}Stroke</c> i <c>Icon{Kind}Fill</c> z <c>Themes/Icons.xaml</c>.
/// Brak klucza = pusta ikona, bez wyjątku. Szablon: <c>Themes/Icons.xaml</c>.
/// </summary>
public sealed class BrandIcon : Control
{
    public static readonly DependencyProperty KindProperty = DependencyProperty.Register(
        nameof(Kind), typeof(string), typeof(BrandIcon), new PropertyMetadata(string.Empty, OnKindChanged));

    public static readonly DependencyProperty SizeProperty = DependencyProperty.Register(
        nameof(Size), typeof(double), typeof(BrandIcon), new PropertyMetadata(16d));

    private Path? _stroke;
    private Path? _fill;

    static BrandIcon()
    {
        DefaultStyleKeyProperty.OverrideMetadata(typeof(BrandIcon), new FrameworkPropertyMetadata(typeof(BrandIcon)));
    }

    /// <summary>Rodzaj ikony, np. „Disk”, „Refresh”.</summary>
    public string Kind
    {
        get => (string)GetValue(KindProperty);
        set => SetValue(KindProperty, value);
    }

    /// <summary>Bok kwadratu ikony w pikselach (domyślnie 16; w menu 20).</summary>
    public double Size
    {
        get => (double)GetValue(SizeProperty);
        set => SetValue(SizeProperty, value);
    }

    public override void OnApplyTemplate()
    {
        base.OnApplyTemplate();
        _stroke = GetTemplateChild("PART_Stroke") as Path;
        _fill = GetTemplateChild("PART_Fill") as Path;
        ApplyGeometry();
    }

    private static void OnKindChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
        => ((BrandIcon)d).ApplyGeometry();

    private void ApplyGeometry()
    {
        if (_stroke is not null)
        {
            _stroke.Data = Find("Stroke");
        }

        if (_fill is not null)
        {
            _fill.Data = Find("Fill");
        }
    }

    private System.Windows.Media.Geometry? Find(string part)
        => string.IsNullOrEmpty(Kind) ? null : TryFindResource($"Icon{Kind}{part}") as System.Windows.Media.Geometry;
}
