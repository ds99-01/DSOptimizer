using System;
using System.Windows.Markup;
using DSOptimizer.Core.Localization;

namespace DSOptimizer.App.Gui;

/// <summary>
/// Rozszerzenie XAML <c>{l:T 'Polski tekst'}</c> (<c>xmlns:l="clr-namespace:DSOptimizer.App.Gui"</c>):
/// zwraca tłumaczenie z <see cref="L.T"/> dla bieżącego języka, a bez tłumaczenia polski tekst źródłowy.
/// </summary>
[MarkupExtensionReturnType(typeof(string))]
public sealed class TExtension : MarkupExtension
{
    public TExtension(string text) => Text = text ?? throw new ArgumentNullException(nameof(text));

    [ConstructorArgument("text")]
    public string Text { get; set; }

    /// <summary>
    /// Tekst jest znany dopiero w XAML (skrypt raportu braków czyta go stamtąd), więc wywołanie idzie przez grupę metod,
    /// a nie przez literał <c>L.T("…")</c>, którego pilnuje strażnik wywołań.
    /// </summary>
    public override object ProvideValue(IServiceProvider serviceProvider)
    {
        Func<string, string> translate = L.T;
        return translate(Text);
    }
}
