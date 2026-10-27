using System;
using System.Collections.Generic;
using DSOptimizer.Core.Operations;
using Newtonsoft.Json;

namespace DSOptimizer.Core.Journal;

/// <summary>Jedna zmiana tweaka na maszynie: baza (stan sprzed pierwszej zmiany) + historia zdarzeń.</summary>
public sealed class JournalEntry
{
    /// <summary>Wartość <see cref="Kind"/> dla usunięcia aplikacji.</summary>
    public const string AppRemovalKind = "app-removal";

    /// <summary>Wartość <see cref="Kind"/> dla instalacji aplikacji przez winget (<see cref="TweakId"/> = <c>install.&lt;nazwa&gt;</c> albo <c>winget:&lt;id&gt;</c>).</summary>
    public const string AppInstallKind = "app-install";

    public Guid EntryId { get; set; }

    /// <summary>
    /// Rodzaj wpisu: null = tweak katalogu, <see cref="AppRemovalKind"/> = usunięcie aplikacji (<see cref="TweakId"/> = Id aplikacji),
    /// <see cref="AppInstallKind"/> = instalacja aplikacji.
    /// </summary>
    public string? Kind { get; set; }

    /// <summary>Nazwa wyświetlana dla wpisów spoza katalogu (usunięta albo zainstalowana aplikacja); null dla tweaków.</summary>
    public string? Title { get; set; }

    public string TweakId { get; set; } = string.Empty;

    public string UserSid { get; set; } = string.Empty;

    public JournalEntryStatus Status { get; set; }

    public DateTimeOffset AppliedAt { get; set; }

    public string CatalogHash { get; set; } = string.Empty;

    public List<SnapshotRecord> Baseline { get; } = new();

    public List<JournalEvent> Events { get; } = new();

    [JsonIgnore]
    public bool IsActive => Status is JournalEntryStatus.Active or JournalEntryStatus.PendingReboot;
}
