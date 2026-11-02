using System;
using CommunityToolkit.Mvvm.ComponentModel;
using DSOptimizer.Core.Autostart;
using DSOptimizer.Core.Repair;
using TextFolding = DSOptimizer.Core.Gui.SearchText;

namespace DSOptimizer.App.ViewModels;

/// <summary>
/// Wiersz strony „Autostart” i jej panel szczegółów (teksty z <see cref="AutostartText"/>). Pole wyboru tylko dla wiersza
/// <see cref="AutostartRow.CanToggle"/>; zaznaczenie innego wiersza jest ignorowane. Wskaźnik w wierszu: liczba (czas procesora), pod nią
/// pasek – najcięższa pozycja całej listy = 72 px, najkrótszy pasek 4 px, bez pomiaru albo bez pliku = sam tekst.
/// </summary>
public sealed class AutostartRowViewModel : ObservableObject
{
    /// <summary>Szerokość paska najcięższej pozycji listy.</summary>
    public const double MaxBarWidth = 72;

    /// <summary>Najkrótszy pasek zmierzonej pozycji.</summary>
    public const double MinBarWidth = 4;

    private readonly Action _selectionChanged;
    private bool _isSelected;

    /// <param name="maxCpuMicroseconds">Największy czas procesora wśród zmierzonych pozycji całej listy (nie tylko widocznej grupy).</param>
    /// <param name="accountName">Nazwa zalogowanego konta (pozycje konta i Sklepu); null = nieznana.</param>
    public AutostartRowViewModel(AutostartRow row, long maxCpuMicroseconds, string? accountName, Action selectionChanged)
    {
        Row = row ?? throw new ArgumentNullException(nameof(row));
        _selectionChanged = selectionChanged ?? throw new ArgumentNullException(nameof(selectionChanged));
        var entry = row.Entry;
        var publisher = string.IsNullOrWhiteSpace(entry.Publisher) ? null : entry.Publisher!.Trim();
        var scope = AutostartText.Scope(entry.Scope);
        PublisherText = publisher is null ? scope : publisher + " · " + scope;
        PublisherDetail = publisher ?? AutostartText.UnknownPublisher;
        SearchKey = TextFolding.Fold(entry.Name + " " + publisher + " " + entry.Command);
        RowNote = AutostartText.RowNote(row);
        AccountText = AutostartText.Account(entry.Scope, accountName ?? AutostartText.UnknownAccount);
        LocationText = AutostartText.Location(entry);
        Summary = row.Known?.Summary ?? (row.MissingFile ? AutostartText.MissingFileSummary : AutostartText.NotAssessed);
        SourceText = AutostartText.SourceLine(row.Known?.Sources ?? Array.Empty<string>());
        StateText = AutostartText.State(row);
        VerdictText = AutostartText.Verdict(row);
        CommandText = entry.Command is { } command ? AutostartText.CommandLine(command) : null;
        FileText = AutostartText.FileLine(entry.FilePath);

        if (entry.Measurement is { } measurement && !row.MissingFile)
        {
            IsMeasured = true;
            var cpu = measurement.CpuMicroseconds;
            BarWidth = maxCpuMicroseconds > 0 ? Math.Max(MinBarWidth, Math.Round(MaxBarWidth * cpu / maxCpuMicroseconds)) : MinBarWidth;
            IndicatorText = AutostartText.CpuSeconds(cpu);
            CpuText = IndicatorText;
            DiskText = DiskSpaceText.Size(measurement.DiskBytes);
            ImpactText = AutostartText.Impact(StartupImpact.Of(cpu, measurement.DiskBytes));
            MeasuredAtText = AutostartText.MeasuredAt(measurement);
            MeasureNote = AutostartText.CpuNote;
        }
        else
        {
            IndicatorText = row.MissingFile ? AutostartText.FileMissing : AutostartText.NotMeasured;
            CpuText = AutostartText.NotMeasured;
            MeasureNote = row.MissingFile ? AutostartText.FileMissingNote : AutostartText.NotInMeasurement;
        }
    }

    public AutostartRow Row { get; }

    public string Id => Row.Id;

    public string Name => Row.Entry.Name;

    public AutostartGroup Group => Row.Group;

    /// <summary>Druga linia wiersza: „wydawca · zakres” (bez wydawcy – sam zakres).</summary>
    public string PublisherText { get; }

    /// <summary>Pole wyboru: ta sama reguła co seria w Core (<see cref="AutostartRow.CanToggle"/>).</summary>
    public bool IsSelectable => Row.CanToggle;

    /// <summary>Kłódka z podpowiedzią zamiast pola wyboru.</summary>
    public bool IsProtected => Row.Verdict == AutostartVerdict.Protected;

    /// <summary>Żarówka przy pozycji sugerowanej; zaznacza ją dopiero „Zaznacz sugerowane”.</summary>
    public bool IsSuggested => Row.Suggested;

    public bool IsSelected
    {
        get => _isSelected;
        set
        {
            if (value && !IsSelectable)
            {
                return;
            }

            if (SetProperty(ref _isSelected, value))
            {
                OnPropertyChanged(nameof(SelectionLineText));
                _selectionChanged();
            }
        }
    }

    /// <summary>Trzecia linia wiersza (stan pozycji wyłączonej, brak pliku, powód blokady); null = bez linii.</summary>
    public string? RowNote { get; }

    public bool HasRowNote => !string.IsNullOrEmpty(RowNote);

    /// <summary>Liczba w kolumnie wskaźnika: czas procesora, „nie zmierzono” albo „plik nie istnieje”.</summary>
    public string IndicatorText { get; }

    /// <summary>Pomiar Windows jest i plik istnieje – wtedy wiersz ma pasek, a panel liczby pomiaru.</summary>
    public bool IsMeasured { get; }

    /// <summary>Szerokość paska wskaźnika w px (0 bez pomiaru).</summary>
    public double BarWidth { get; }

    /// <summary>Nazwa, wydawca i polecenie bez polskich znaków (wyszukiwarka strony).</summary>
    public string SearchKey { get; }

    // Panel szczegółów: tytuł, opis, źródło, „Kopiuj nazwę”, linia zaznaczenia, pomiar, tabela, szczegóły techniczne.
    public string Summary { get; }

    public string? SourceText { get; }

    public bool HasSource => SourceText is not null;

    /// <summary>„Zaznaczona” albo zachęta; null, gdy wiersza nie da się zaznaczyć (powód jest w wierszu albo w kłódce).</summary>
    public string? SelectionLineText => !IsSelectable ? null : IsSelected ? AutostartText.SelectedLine : AutostartText.NotSelectedLine;

    public string CpuText { get; }

    public string DiskText { get; } = string.Empty;

    public string ImpactText { get; } = string.Empty;

    public string MeasuredAtText { get; } = string.Empty;

    /// <summary>Dopisek pod pomiarem: czas procesora (nie czas rozruchu), brak w pomiarze albo brak pliku.</summary>
    public string MeasureNote { get; }

    public string StateText { get; }

    public string VerdictText { get; }

    public string PublisherDetail { get; }

    public string AccountText { get; }

    public string LocationText { get; }

    public string UndoText { get; } = AutostartText.UndoInHistory;

    /// <summary>„Polecenie: …”; null dla pliku w folderze Autostart i aplikacji ze Sklepu (bez polecenia).</summary>
    public string? CommandText { get; }

    public bool HasCommand => CommandText is not null;

    public string FileText { get; }

    public string AutomationName => Name;

    public string AutomationHelpText => IsProtected ? AutostartText.ProtectedTooltip : RowNote ?? PublisherText;
}
