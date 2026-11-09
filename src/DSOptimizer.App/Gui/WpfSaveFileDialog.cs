using System;
using System.IO;
using System.Windows;
using DSOptimizer.Core.Localization;
using Microsoft.Win32;

namespace DSOptimizer.App.Gui;

/// <summary>
/// Systemowe okno „Zapisz jako” (tytuł ustawia Windows). Właściciel: okno główne programu, gdy jest. Filtr według rozszerzenia
/// nazwy domyślnej (<c>.log</c>, inne = <c>.txt</c>).
/// </summary>
public sealed class WpfSaveFileDialog : ISaveFileDialog
{
    public string? AskPath(string fileName)
    {
        if (fileName is null)
        {
            throw new ArgumentNullException(nameof(fileName));
        }

        var dialog = new SaveFileDialog
        {
            InitialDirectory = Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory),
            FileName = fileName,
            DefaultExt = Path.GetExtension(fileName),
            AddExtension = true,
            OverwritePrompt = true,
            Filter = FilterFor(fileName),
            // Program działa jako administrator: podrzucony skrót .lnk (np. na Pulpicie) nie może przekierować zapisu do swojego celu.
            DereferenceLinks = false,
        };

        var owner = Application.Current?.MainWindow;
        var chosen = owner is null ? dialog.ShowDialog() : dialog.ShowDialog(owner);
        return chosen == true ? dialog.FileName : null;
    }

    /// <summary>Filtr okna zapisu: pliki logu dla <c>.log</c> (bez rozróżniania wielkości liter), inaczej pliki tekstowe.</summary>
    internal static string FilterFor(string fileName)
        => string.Equals(Path.GetExtension(fileName), ".log", StringComparison.OrdinalIgnoreCase)
            ? L.T("Pliki logu (*.log)|*.log")
            : L.T("Pliki tekstowe (*.txt)|*.txt");
}
