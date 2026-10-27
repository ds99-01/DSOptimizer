using System;
using System.Globalization;
using System.Windows.Data;

namespace DSOptimizer.App.Views;

/// <summary>Szerokość (double) mniejsza niż próg z parametru (np. „640”) → true. Układ jednej kolumny przy wąskim oknie.</summary>
public sealed class WidthBelowConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
        => value is double width
            && width > 0
            && double.TryParse(parameter as string, NumberStyles.Float, CultureInfo.InvariantCulture, out var threshold)
            && width < threshold;

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => throw new NotSupportedException();
}
