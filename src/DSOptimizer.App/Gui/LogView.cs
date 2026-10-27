using System;
using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.Globalization;
using System.IO;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using DSOptimizer.App.Views;
using DSOptimizer.Core.Abstractions;
using DSOptimizer.Core.Gui;

namespace DSOptimizer.App.Gui;

/// <summary>
/// Dziennik okna w <see cref="RichTextBox"/> tylko do odczytu: jeden akapit na linię <see cref="UiLogSink.Lines"/> (źródło prawdy;
/// widok dopisuje akapit, usuwa pierwszy przy limicie i przebudowuje się przy innej zmianie). Tekst można zaznaczyć myszą i skopiować.
/// Ścieżka linii (<see cref="GuiLogLine.Path"/>) poza katalogiem programu jest linkiem „Otwórz w Eksploratorze”, w katalogu
/// programu zwykłym tekstem z ikoną „Kopiuj ścieżkę” (<see cref="LogLineText.PathMode"/>). Każde kopiowanie idzie przez
/// <see cref="ClipboardCopy"/> (schowek pod RustDesk bywa zajęty).
/// </summary>
internal sealed class LogView
{
    private const string Component = "Gui";

    /// <summary>Szerokość pasa znacznika poziomu na lewo od godziny.</summary>
    private const double MarkColumn = 16;

    /// <summary>Odstęp między godziną a tekstem; w tekście kopiowanym z zaznaczenia to jedna spacja (element w tekście).</summary>
    private const double TimeGap = 12;

    // Segoe Fluent Icons (zgodne z Segoe MDL2 Assets): FolderOpen U+E838, Copy U+E8C8.
    private const string CopyGlyph = "";

    private readonly RichTextBox _box;
    private readonly ObservableCollection<GuiLogLine> _lines;
    private readonly ShellActions _shell;
    private readonly string _protectedRoot;
    private readonly ILog _log;
    private readonly MenuItem _copyPathItem;
    private readonly MenuItem _revealItem;

    private string? _menuPath;
    private Hyperlink? _menuLink;

    public LogView(RichTextBox box, ObservableCollection<GuiLogLine> lines, ShellActions shell, string protectedRoot, ILog log)
    {
        _box = box ?? throw new ArgumentNullException(nameof(box));
        _lines = lines ?? throw new ArgumentNullException(nameof(lines));
        _shell = shell ?? throw new ArgumentNullException(nameof(shell));
        _protectedRoot = string.IsNullOrWhiteSpace(protectedRoot)
            ? throw new ArgumentException("Katalog chroniony nie może być pusty.", nameof(protectedRoot))
            : protectedRoot;
        _log = log ?? throw new ArgumentNullException(nameof(log));

        var document = new FlowDocument { PagePadding = new Thickness(0) };
        _box.Document = document;

        // Ctrl+C i „Kopiuj” z menu: zaznaczenie przez ClipboardCopy zamiast wbudowanego kopiowania RichTextBox.
        CommandManager.AddPreviewCanExecuteHandler(_box, OnPreviewCanExecute);
        CommandManager.AddPreviewExecutedHandler(_box, OnPreviewExecuted);

        _copyPathItem = new MenuItem { Header = "Kopiuj ścieżkę" };
        _copyPathItem.Click += (_, _) => CopyPath(_menuPath);
        _revealItem = new MenuItem { Header = "Otwórz lokalizację pliku", Icon = CreateMenuIcon("OpenLocation") };
        _revealItem.Click += (_, _) => RevealFromMenu();
        _box.ContextMenu = CreateContextMenu();
        // handledEventsToo: wbudowana obsługa menu w RichTextBox może oznaczyć zdarzenie jako obsłużone (np. prawy klik w zaznaczenie),
        // a pozycje menu muszą odpowiadać linii pod kursorem przy każdym otwarciu.
        _box.AddHandler(FrameworkElement.ContextMenuOpeningEvent, new ContextMenuEventHandler(OnContextMenuOpening), true);

        _lines.CollectionChanged += OnLinesChanged;
        Rebuild();
    }

    /// <summary>„Kopiuj cały dziennik” / „Kopiuj wszystko”: wszystkie linie (godzina, poziom, tekst).</summary>
    public void CopyAll() => Copy(LogLineText.ForClipboard(_lines), "Nie udało się skopiować dziennika do schowka.");

    private ContextMenu CreateContextMenu()
    {
        var menu = new ContextMenu();
        menu.SetResourceReference(Control.FontFamilyProperty, "BrandFont");
        menu.Items.Add(new MenuItem
        {
            Header = "Kopiuj",
            Command = ApplicationCommands.Copy,
            CommandTarget = _box,
            Icon = CreateMenuIcon("Copy"),
        });
        var copyAll = new MenuItem { Header = "Kopiuj cały dziennik" };
        copyAll.Click += (_, _) => CopyAll();
        menu.Items.Add(copyAll);
        menu.Items.Add(_copyPathItem);
        menu.Items.Add(_revealItem);
        return menu;
    }

    private static BrandIcon CreateMenuIcon(string kind)
        => new() { Kind = kind, Size = 16, VerticalAlignment = VerticalAlignment.Center };

    /// <summary>Linia i link pod kursorem (z klawiatury: pod karetką) decydują, które pozycje menu są widoczne.</summary>
    private void OnContextMenuOpening(object sender, ContextMenuEventArgs e)
    {
        TextPointer? exact;
        TextPointer? near;
        if (e.CursorLeft < 0)
        {
            exact = near = _box.CaretPosition;
        }
        else
        {
            var point = Mouse.GetPosition(_box);
            exact = _box.GetPositionFromPoint(point, false);
            near = exact ?? _box.GetPositionFromPoint(point, true);
        }

        _menuPath = (near?.Paragraph?.Tag as GuiLogLine)?.Path;
        _menuLink = FindLink(exact);
        _copyPathItem.Visibility = string.IsNullOrEmpty(_menuPath) ? Visibility.Collapsed : Visibility.Visible;
        _revealItem.Visibility = _menuLink is null ? Visibility.Collapsed : Visibility.Visible;
        _revealItem.IsEnabled = _menuLink?.IsEnabled == true;
    }

    private static Hyperlink? FindLink(TextPointer? pointer)
    {
        for (var element = pointer?.Parent as TextElement; element is not null; element = element.Parent as TextElement)
        {
            if (element is Hyperlink link)
            {
                return link;
            }
        }

        return null;
    }

    private void RevealFromMenu()
    {
        if (_menuLink?.Tag is string path)
        {
            Reveal(path);
        }
    }

    private void OnPreviewCanExecute(object sender, CanExecuteRoutedEventArgs e)
    {
        if (e.Command == ApplicationCommands.Copy)
        {
            e.CanExecute = !_box.Selection.IsEmpty;
            e.Handled = true;
        }
    }

    private void OnPreviewExecuted(object sender, ExecutedRoutedEventArgs e)
    {
        if (e.Command != ApplicationCommands.Copy)
        {
            return;
        }

        e.Handled = true;
        if (!_box.Selection.IsEmpty)
        {
            Copy(_box.Selection.Text, "Nie udało się skopiować zaznaczonego fragmentu dziennika do schowka.");
        }
    }

    private void OnLinesChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        var blocks = _box.Document.Blocks;
        if (e.Action == NotifyCollectionChangedAction.Add && e.NewItems is not null && e.NewStartingIndex == blocks.Count)
        {
            foreach (GuiLogLine line in e.NewItems)
            {
                blocks.Add(CreateParagraph(line));
            }

            // Zaznaczony fragment zostaje na miejscu (przewinięcie zgubiłoby go z widoku) [decyzja projektowa].
            if (_box.Selection.IsEmpty)
            {
                _box.ScrollToEnd();
            }
        }
        else if (e.Action == NotifyCollectionChangedAction.Remove && e.OldStartingIndex == 0 && e.OldItems?.Count == 1 && blocks.FirstBlock is not null)
        {
            blocks.Remove(blocks.FirstBlock);
        }
        else
        {
            Rebuild();
        }
    }

    private void Rebuild()
    {
        var blocks = _box.Document.Blocks;
        blocks.Clear();
        foreach (var line in _lines)
        {
            blocks.Add(CreateParagraph(line));
        }

        _box.ScrollToEnd();
    }

    /// <summary>
    /// Akapit: lewa krawędź = znacznik poziomu (Info 2 px szary, ostrzeżenie 2 px Caution, błąd 6 px Critical), godzina, tekst.
    /// Wcięcie wiszące o szerokość godziny z odstępem: zawinięte wiersze zaczynają się pod tekstem, nie pod godziną.
    /// </summary>
    private Paragraph CreateParagraph(GuiLogLine line)
    {
        var mark = line.Level == GuiLogLevel.Error ? 6 : 2;
        var time = LogLineText.Time(line.At);
        var hang = MeasureWidth(time) + TimeGap;
        var paragraph = new Paragraph
        {
            Tag = line,
            Margin = new Thickness(0, 2, 0, 2),
            BorderThickness = new Thickness(mark, 0, 0, 0),
            Padding = new Thickness(MarkColumn - mark + hang, 0, 0, 0),
            TextIndent = -hang,
        };
        paragraph.SetResourceReference(Block.BorderBrushProperty, line.Level switch
        {
            GuiLogLevel.Error => "SystemFillColorCriticalBrush",
            GuiLogLevel.Warning => "SystemFillColorCautionBrush",
            _ => "ControlStrongStrokeColorDisabledBrush",
        });

        var timeRun = new Run(time);
        timeRun.SetResourceReference(TextElement.ForegroundProperty, "TextFillColorSecondaryBrush");
        paragraph.Inlines.Add(timeRun);
        paragraph.Inlines.Add(new InlineUIContainer(new FrameworkElement { Width = TimeGap }));

        var text = new Span();
        text.SetResourceReference(TextElement.ForegroundProperty, line.Level == GuiLogLevel.Info ? "TextFillColorSecondaryBrush" : "TextFillColorPrimaryBrush");
        if (line.Level == GuiLogLevel.Error)
        {
            text.FontWeight = FontWeights.SemiBold;
        }

        AddText(text.Inlines, line);
        paragraph.Inlines.Add(text);
        return paragraph;
    }

    private void AddText(InlineCollection inlines, GuiLogLine line)
    {
        var parts = LogLineText.Split(line);
        var mode = LogLineText.PathMode(parts.Path, _protectedRoot, File.Exists);
        switch (mode)
        {
            case LogPathMode.Reveal:
            case LogPathMode.RevealMissing:
                // Ikona folderu w tekście kopiowanym z zaznaczenia liczy się jako spacja, więc zastępuje odstęp przed ścieżką.
                inlines.Add(new Run(parts.Before.TrimEnd(' ')));
                inlines.Add(CreateLink(parts.Path!, mode == LogPathMode.Reveal));
                break;
            case LogPathMode.CopyOnly:
                inlines.Add(new Run(parts.Before + parts.Path));
                inlines.Add(new InlineUIContainer(CreateCopyButton(parts.Path!)) { BaselineAlignment = BaselineAlignment.Center });
                break;
            default:
                inlines.Add(new Run(parts.Before));
                break;
        }

        if (parts.After.Length > 0)
        {
            inlines.Add(new Run(parts.After));
        }
    }

    private Hyperlink CreateLink(string path, bool exists)
    {
        var icon = new BrandIcon { Kind = "OpenLocation", Size = 14, Margin = new Thickness(4, 0, 4, 0) };
        icon.SetResourceReference(Control.ForegroundProperty, exists ? "TextFillColorPrimaryBrush" : "TextFillColorDisabledBrush");
        var link = new Hyperlink(new InlineUIContainer(icon) { BaselineAlignment = BaselineAlignment.Center })
        {
            Tag = path,
            IsEnabled = exists,
            ToolTip = exists ? "Otwórz w Eksploratorze" : "Plik nie istnieje",
        };
        link.Inlines.Add(new Run(path));
        link.SetResourceReference(TextElement.ForegroundProperty, exists ? "TextFillColorPrimaryBrush" : "TextFillColorDisabledBrush");
        ToolTipService.SetShowOnDisabled(link, true);
        if (exists)
        {
            link.Click += (_, _) => Reveal(path);
        }

        return link;
    }

    private Button CreateCopyButton(string path)
    {
        var button = new Button { Content = CopyGlyph, ToolTip = "Kopiuj ścieżkę", Margin = new Thickness(4, 0, 0, 0) };
        button.SetResourceReference(FrameworkElement.StyleProperty, "InlineIconButton");
        AutomationProperties.SetName(button, "Kopiuj ścieżkę");
        button.Click += (_, _) => CopyPath(path);
        return button;
    }

    /// <summary>
    /// Eksplorator z zaznaczonym plikiem poza wątkiem UI (<see cref="ShellActions"/>). async void tylko dla zdarzenia kliknięcia:
    /// wyjątek nie może wyjść na wątek UI, więc idzie do dziennika.
    /// </summary>
    private async void Reveal(string path)
    {
        try
        {
            await _shell.RevealAsync(path).ConfigureAwait(true);
        }
        catch (Exception ex)
        {
            _log.Fail(Component, "Nie udało się otworzyć lokalizacji pliku.", ex);
        }
    }

    private void CopyPath(string? path)
    {
        if (!string.IsNullOrEmpty(path))
        {
            Copy(path!, "Nie udało się skopiować ścieżki do schowka.");
        }
    }

    /// <summary>Schowek bywa zajęty przez inny proces (CLIPBRD_E_CANT_OPEN): ClipboardCopy sprawdza odczytem zwrotnym.</summary>
    private bool Copy(string text, string failure)
    {
        var result = ClipboardCopy.Copy(WpfClipboardText.Instance, text);
        if (!result.Copied)
        {
            _log.Fail(Component, failure, result.Error);
        }
        else if (result.Error is not null)
        {
            _log.Info(Component, ClipboardCopy.RecoveredMessage(result.Error));
        }

        return result.Copied;
    }

    private double MeasureWidth(string text)
    {
        var typeface = new Typeface(_box.FontFamily, FontStyles.Normal, FontWeights.Normal, FontStretches.Normal);
        var formatted = new FormattedText(
            text,
            CultureInfo.CurrentCulture,
            FlowDirection.LeftToRight,
            typeface,
            _box.FontSize,
            Brushes.Black,
            null,
            TextOptions.GetTextFormattingMode(_box),
            VisualTreeHelper.GetDpi(_box).PixelsPerDip);
        return formatted.WidthIncludingTrailingWhitespace;
    }
}
