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

    // Tekst trafia tu z XAML {l:T '…'}: literały w XAML sprawdzają strażnik wywołań i raport braków,
    // a ten plik jest z obu wyłączony (jak definicja L.cs).
    public override object ProvideValue(IServiceProvider serviceProvider) => L.T(Text);
}
