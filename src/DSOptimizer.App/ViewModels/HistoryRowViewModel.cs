using System;
using System.Collections.Generic;
using System.Linq;
using DSOptimizer.Core.Gui;

namespace DSOptimizer.App.ViewModels;

/// <summary>
/// Wiersz listy „Historia zmian” i treść panelu szczegółów: wpis z <see cref="ChangeHistory.Build"/> z gotowymi tekstami
/// z <see cref="HistoryText"/>. Niezmienny – po odświeżeniu historii powstają nowe wiersze (zaznaczenie wraca po <see cref="EntryId"/>).
/// </summary>
public sealed class HistoryRowViewModel
{
    public HistoryRowViewModel(HistoryEntry entry)
    {
        Entry = entry ?? throw new ArgumentNullException(nameof(entry));
        DateText = HistoryText.Date(entry.AppliedAt);
        StateName = HistoryText.State(entry.State);
        StateText = HistoryText.StateWithReason(entry.State, entry.Reason);
        ReasonText = string.IsNullOrEmpty(entry.Reason) ? null : entry.Reason;
        KindText = HistoryText.KindLine(entry.Kind, entry.CategoryName);
        AutomationName = HistoryText.RowName(entry.Title, KindText, StateText, entry.AppliedAt);
        Events = entry.Events.Select(item => new HistoryEventRow(HistoryText.Time(item.At), item.Text, item.IsFailure)).ToList();
    }

    public HistoryEntry Entry { get; }

    public Guid EntryId => Entry.EntryId;

    public string TweakId => Entry.TweakId;

    public string Title => Entry.Title;

    /// <summary>Druga linia w kolumnie „Pozycja” i w szczegółach: pozycji nie ma w katalogu tej wersji programu.</summary>
    public bool IsOutOfCatalog => !Entry.InCatalog;

    /// <summary>Znacznik „spoza katalogu” w wierszu tylko, gdy <see cref="KindText"/> go jeszcze nie mówi (bez powtórzenia).</summary>
    public bool ShowOutOfCatalogMarker => IsOutOfCatalog && KindText != HistoryText.OutOfCatalog;

    public string AccountText => Entry.AccountText;

    /// <summary>„cały komputer” i „nieznane” kolorem drugorzędnym; nazwa konta (albo SID) kolorem podstawowym.</summary>
    public bool IsAccountGeneric => Entry.AccountText == HistoryText.WholeComputer || Entry.AccountText == HistoryText.UnknownAccount;

    /// <summary>Kategoria GUI; dla pozycji spoza katalogu <see cref="HistoryText.OutOfCatalog"/>.</summary>
    public string CategoryText => Entry.CategoryName ?? HistoryText.OutOfCatalog;

    public HistoryState State => Entry.State;

    public string DateText { get; }

    /// <summary>Stan z krótkim powodem porażki („Nie zainstalowano – brak połączenia z GitHubem”) – panel szczegółów i podpowiedź wiersza.</summary>
    public string StateText { get; }

    /// <summary>Kolumna „Stan” w wierszu: sam stan („Nie zainstalowano”), zawijany jak przed falą poprawek (nigdy ucinany).</summary>
    public string StateName { get; }

    /// <summary>Krótki powód porażki w drugiej linii pod stanem (jedna linia, za długi ucięty wielokropkiem); null = brak.</summary>
    public string? ReasonText { get; }

    public bool HasReason => ReasonText is not null;

    /// <summary>Podpowiedź kolumny stanu: pełny tekst stanu z powodem (powód w wierszu bywa ucięty).</summary>
    public string StateToolTip => StateText;

    /// <summary>Druga linia pod tytułem: „Instalacja aplikacji”, „Usunięcie aplikacji” albo nazwa kategorii.</summary>
    public string KindText { get; }

    /// <summary>Klucz ikony przed tytułem (<c>Themes/Icons.xaml</c>).</summary>
    public string IconKind => Entry.IconKind;

    /// <summary>Wpis instalacji aplikacji: w szczegółach podpowiedź z linkiem do „Aplikacje” tuż pod stanem.</summary>
    public bool IsInstall => Entry.Kind == HistoryKind.AppInstall;

    /// <summary>
    /// Nazwa wiersza listy dla czytnika ekranu: tytuł, rodzaj wpisu, stan z powodem i data (kontener wiersza; bez niej czytnik
    /// przeczytałby nazwę typu).
    /// </summary>
    public string AutomationName { get; }

    /// <summary>Wiersz historii nie ma osobnego opisu (wspólny styl kontenera listy wiąże tę właściwość).</summary>
    public string AutomationHelpText { get; } = string.Empty;

    public IReadOnlyList<HistoryEventRow> Events { get; }

    /// <summary>„Cofnij” widoczne (wpis aktywny); dostępne dodatkowo tylko bez <see cref="UndoBlockedReason"/>.</summary>
    public bool IsActive => Entry.IsActive;

    public string? UndoBlockedReason => Entry.UndoBlockedReason;

    /// <summary>
    /// Powód blokady nad miejscem „Cofnij”. Ruling R11: także dla wpisu, który nie jest aktywny – wpis instalacji aplikacji niesie
    /// stałą podpowiedź <see cref="HistoryText.InstallUndoBlocked"/> bez przycisku. Wpis tweaka ma powód tylko, gdy jest aktywny.
    /// </summary>
    public bool HasUndoBlockedReason => Entry.UndoBlockedReason is not null;

    /// <summary>Przycisk „Cofnij” w panelu szczegółów (tylko wpis aktywny).</summary>
    public bool ShowUndoButton => Entry.IsActive;

    /// <summary>Dolny pas panelu szczegółów (linia, powód blokady, przycisk): przycisk albo sam powód.</summary>
    public bool ShowUndoArea => ShowUndoButton || (HasUndoBlockedReason && !IsInstall);
}

/// <summary>Zdarzenie w sekcji „Przebieg”: czas z sekundami, tekst i znacznik porażki (jak linia błędu w dzienniku).</summary>
public sealed record HistoryEventRow(string TimeText, string Text, bool IsFailure);
