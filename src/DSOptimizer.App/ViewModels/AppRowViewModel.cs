using System;
using System.Collections.Generic;
using CommunityToolkit.Mvvm.ComponentModel;
using DSOptimizer.Core.Apps;
using TextFolding = DSOptimizer.Core.Gui.SearchText;

namespace DSOptimizer.App.ViewModels;

/// <summary>
/// Wiersz strony „Aplikacje” i jej panel szczegółów (teksty z <see cref="AppsPageText"/>). Pole wyboru tylko dla wiersza
/// <see cref="AppRemovability.Removable"/> spoza grupy chronionej; zaznaczenie innego wiersza jest ignorowane.
/// </summary>
public sealed class AppRowViewModel : ObservableObject
{
    private readonly Action _selectionChanged;
    private bool _isSelected;

    /// <param name="accountName">Nazwa konta wiersza per użytkownik (właściciel programu albo zalogowany dla Sklepu); null = nieznana.</param>
    /// <param name="userName">Nazwa zalogowanego użytkownika do opisu usunięcia aplikacji ze Sklepu; null = nieznana.</param>
    /// <param name="storeScope">Zakres usuwania aplikacji ze Sklepu (Ruling R8).</param>
    public AppRowViewModel(AppRow row, string? accountName, string? userName, StoreRemovalScope storeScope, Action selectionChanged)
    {
        Row = row ?? throw new ArgumentNullException(nameof(row));
        _selectionChanged = selectionChanged ?? throw new ArgumentNullException(nameof(selectionChanged));
        var app = row.App;
        Publisher = PublisherName.Readable(app.Publisher);
        SearchKey = TextFolding.Fold(app.Name + " " + Publisher);
        AccountText = app.Scope == AppScope.Machine ? AppsPageText.AllAccounts : accountName ?? AppsPageText.UnknownAccount;
        // Wiersz do zaznaczenia ma zawsze dane do usunięcia (AppRow.CanRemove), więc plan programu jest tu pewny.
        RemovalText = !IsSelectable
            ? BlockedText
            : row.Plan is { } plan
                ? AppsPageText.ProgramRemovalBrief(plan.Mode)
                : AppsPageText.StoreRemoval(userName, string.Equals(row.BloatGroup, "microsoft", StringComparison.Ordinal), storeScope == StoreRemovalScope.AllUsers);
        TechnicalText = Technical(row);
    }

    public AppRow Row { get; }

    public string Id => Row.App.Id;

    public string Name => Row.App.Name;

    /// <summary>Czytelny wydawca (<see cref="PublisherName.Readable"/>); null = brak.</summary>
    public string? Publisher { get; }

    public string PublisherText => Publisher ?? string.Empty;

    public AppGroup Group => Row.Group;

    public string KindText => AppsPageText.KindShort(Row.App.Kind);

    /// <summary>Nazwa i wydawca bez polskich znaków (wyszukiwarka strony).</summary>
    public string SearchKey { get; }

    /// <summary>Pole wyboru: ta sama reguła co seria w Core (<see cref="AppRow.CanRemove"/>).</summary>
    public bool IsSelectable => Row.CanRemove;

    public bool IsProtected => Row.Group == AppGroup.Protected;

    /// <summary>Druga linia wiersza bez pola wyboru: powód blokady (inne konto, niepewny użytkownik, brak bezpiecznego polecenia).</summary>
    public string? RowNote => !IsSelectable && !IsProtected ? BlockedText : null;

    /// <summary>Powód blokady; wiersz „usuwalny” bez danych do usunięcia (brak planu) = brak bezpiecznego polecenia.</summary>
    private string BlockedText => Row.BlockedReason ?? AppsText.NoSafeCommand;

    public bool HasRowNote => !string.IsNullOrEmpty(RowNote);

    /// <summary>Ostrzeżenie z listy śmieci (trójkąt w wierszu i w panelu).</summary>
    public string? Warning => Row.Warning;

    public bool HasWarning => !string.IsNullOrWhiteSpace(Row.Warning);

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

    public string AutomationName => Name;

    public string AutomationHelpText => IsProtected ? AppsPageText.ProtectedTooltip : RowNote ?? PublisherText;

    // Panel szczegółów (render B: tytuł, opis reguły, linia serii, tabela, szczegóły techniczne).
    public string? Summary => Row.Summary;

    public bool HasSummary => !string.IsNullOrWhiteSpace(Row.Summary);

    /// <summary>„Zaznaczona do usunięcia” albo zachęta; null, gdy wiersza nie da się zaznaczyć (powód jest w „Usuwanie”).</summary>
    public string? SelectionLineText => !IsSelectable ? null : IsSelected ? AppsPageText.SelectedLine : AppsPageText.NotSelectedLine;

    public string GroupText => AppsPageText.GroupDetail(Row.Group, Row.BloatGroup);

    public string KindLongText => AppsPageText.KindLong(Row.App.Kind);

    public string PublisherDetail => Publisher ?? AppsPageText.UnknownPublisher;

    public string VersionText => string.IsNullOrWhiteSpace(Row.App.Version) ? AppsPageText.UnknownValue : Row.App.Version!;

    public string AccountText { get; }

    /// <summary>Jak zostanie usunięta, a dla wiersza bez pola wyboru – dlaczego nie.</summary>
    public string RemovalText { get; }

    public string UndoText { get; } = AppsPageText.Irreversible;

    /// <summary>Tryb i polecenie deinstalacji z planu (program) albo pełna nazwa pakietu (Sklep); tylko do odczytu.</summary>
    public string TechnicalText { get; }

    private static string Technical(AppRow row)
    {
        var lines = new List<string>();
        var app = row.App;
        if (app.Appx is { } appx)
        {
            lines.Add("Pakiet: " + appx.PackageFullName);
        }

        if (app.Win32 is { } win32)
        {
            lines.Add("Wpis odinstalowania: " + win32.KeyPath);
        }

        if (row.Plan is { } plan)
        {
            lines.Add("Tryb: " + AppsPageText.ProgramRemovalBrief(plan.Mode));
            lines.Add("Polecenie: " + AppsPageText.CommandLine(plan));
        }

        if (!string.IsNullOrWhiteSpace(row.CleanupUrl))
        {
            lines.Add("Narzędzie producenta do resztek: " + row.CleanupUrl);
        }

        return string.Join(Environment.NewLine, lines);
    }
}
