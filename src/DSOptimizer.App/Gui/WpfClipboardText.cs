using System.Windows;
using DSOptimizer.Core.Gui;

namespace DSOptimizer.App.Gui;

/// <summary>Schowek WPF dla <see cref="ClipboardCopy"/>; wołać tylko z wątku UI (STA).</summary>
internal sealed class WpfClipboardText : IClipboardText
{
    public static WpfClipboardText Instance { get; } = new();

    private WpfClipboardText()
    {
    }

    public void SetText(string text) => Clipboard.SetText(text);

    public string? GetText() => Clipboard.ContainsText() ? Clipboard.GetText() : null;
}
