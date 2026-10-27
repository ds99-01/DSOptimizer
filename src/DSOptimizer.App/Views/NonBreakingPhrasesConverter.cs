using System;
using System.Globalization;
using System.Windows.Data;
using DSOptimizer.Core.Gui;

namespace DSOptimizer.App.Views;

/// <summary>Wartość specyfikacji na ekran: łamanie wiersza tylko po przecinku (<see cref="SpecificationText.BreakOnlyAfterCommas"/>).</summary>
public sealed class NonBreakingPhrasesConverter : IValueConverter
{
    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
        => value is string text ? SpecificationText.BreakOnlyAfterCommas(text) : value;

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => throw new NotSupportedException();
}
