using System;
using System.Collections.Generic;

namespace DSOptimizer.Core.Gui;

/// <summary>Stan wpisu historii zmian widoczny dla użytkownika.</summary>
public enum HistoryState
{
    Active,
    PendingReboot,
    UndonePendingReboot,
    Undone,
    Aborted,

    /// <summary>Usunięta aplikacja (wpis <c>app-removal</c>): zniknięcie sprawdzone, bez „Cofnij”.</summary>
    Removed,

    /// <summary>Usunięcie aplikacji bez potwierdzenia zniknięcia (też proces przerwany w trakcie próby).</summary>
    RemovalNotConfirmed,

    /// <summary>Usunięcie aplikacji nie powiodło się.</summary>
    RemovalFailed,

    /// <summary>Zainstalowana aplikacja (wpis <c>app-install</c>): sprawdzona, zgłoszona przez winget albo była już zainstalowana; bez „Cofnij”.</summary>
    Installed,

    /// <summary>Instalacja bez potwierdzenia (limit czasu, program niewidoczny po sukcesie winget, proces przerwany w trakcie).</summary>
    InstallNotConfirmed,

    /// <summary>Instalacja nie została wykonana (kod winget, błąd uruchomienia).</summary>
    InstallNotDone,
}

/// <summary>Rodzaj wpisu historii: zmiana z katalogu (tweak, akcja, panel), instalacja albo usunięcie aplikacji.</summary>
public enum HistoryKind
{
    Change,
    AppInstall,
    AppRemoval,
}

/// <summary>Filtr listy historii zmian.</summary>
public enum HistoryFilter
{
    All,
    Active,
    PendingReboot,
    Inactive,
}

/// <summary>Jedno zdarzenie wpisu: czas, gotowy tekst i znacznik porażki.</summary>
public sealed record HistoryEvent(DateTimeOffset At, string Text, bool IsFailure);

/// <summary>
/// Wpis historii zmian. <c>IsActive</c> = przycisk „Cofnij” widoczny (nigdy dla usunięć ani instalacji aplikacji); <c>UndoBlockedReason == null</c> = „Cofnij” dostępne
/// (wpis instalacji niesie tu stałą podpowiedź <see cref="HistoryText.InstallUndoBlocked"/>).
/// <c>CategoryName</c> = nazwa kategorii GUI, null dla pozycji spoza katalogu.
/// <c>Kind</c> = rodzaj wpisu, <c>IconKind</c> = klucz ikony (<see cref="GuiIcons"/>), <c>Reason</c> = krótki powód porażki
/// (tylko stany „nie wykonano”/„nie potwierdzono” instalacji i usunięcia; null dla starych journali i sukcesów).
/// </summary>
public sealed record HistoryEntry(
    Guid EntryId,
    string TweakId,
    string Title,
    bool InCatalog,
    string? CategoryName,
    DateTimeOffset AppliedAt,
    string AccountText,
    HistoryState State,
    IReadOnlyList<HistoryEvent> Events,
    bool IsActive,
    string? UndoBlockedReason,
    HistoryKind Kind = HistoryKind.Change,
    string IconKind = "Tools",
    string? Reason = null);
