using System;
using System.IO;
using System.Threading.Tasks;
using DSOptimizer.Core.Abstractions;
using DSOptimizer.Core.Gui;
using DSOptimizer.Windows;

namespace DSOptimizer.App.Gui;

/// <summary>
/// „Zapisz kopię…” w dzienniku: pyta w oknie „Zapisz jako” (nazwa pliku źródła), kopiuje plik przez
/// <see cref="ReportFileWriter.SaveCopy"/> poza wątkiem UI. Sukces = linia w dzienniku z linkiem do kopii, odmowa albo błąd = ostrzeżenie
/// w logu, które przez TeeLog trafia też do dziennika okna. Anulowanie okna = nic. Nigdy nie rzuca wyjątku.
/// </summary>
public sealed class LogCopySaver
{
    private const string Component = "Gui";

    private readonly ISaveFileDialog _dialog;
    private readonly ReportFileWriter _writer;
    private readonly ILog _log;
    private readonly Action<GuiLogLevel, string, string?> _panel;

    /// <param name="panel">Linia w dzienniku okna: poziom, tekst, ścieżka (link).</param>
    public LogCopySaver(ISaveFileDialog dialog, ReportFileWriter writer, ILog log, Action<GuiLogLevel, string, string?> panel)
    {
        _dialog = dialog ?? throw new ArgumentNullException(nameof(dialog));
        _writer = writer ?? throw new ArgumentNullException(nameof(writer));
        _log = log ?? throw new ArgumentNullException(nameof(log));
        _panel = panel ?? throw new ArgumentNullException(nameof(panel));
    }

    /// <summary>Okno zapisu na wątku wywołującym (UI), kopia w tle, wynik z powrotem na wątku UI.</summary>
    public async Task SaveAsync(string sourcePath)
    {
        string? target;
        try
        {
            target = _dialog.AskPath(Path.GetFileName(sourcePath));
        }
        catch (Exception ex)
        {
            _log.Warn(Component, LogLineText.CopyFailed(ex.Message));
            return;
        }

        if (target is null)
        {
            return;
        }

        try
        {
            var result = await Task.Run(() => _writer.SaveCopy(sourcePath, target)).ConfigureAwait(true);
            if (result.Saved)
            {
                var text = LogLineText.CopySaved(result.Path);
                _log.Info(Component, text);
                _panel(GuiLogLevel.Info, text, result.Path);
            }
            else
            {
                _log.Warn(Component, LogLineText.CopyNotSaved(result.Path, result.Error ?? string.Empty));
            }
        }
        catch (Exception ex)
        {
            _log.Warn(Component, LogLineText.CopyFailed(ex.Message));
        }
    }
}
