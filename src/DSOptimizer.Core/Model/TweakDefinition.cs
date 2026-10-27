using System;
using System.Collections.Generic;

namespace DSOptimizer.Core.Model;

/// <summary>Definicja pozycji katalogu (tweak, akcja albo panel).</summary>
public sealed record TweakDefinition
{
    public static readonly TimeSpan DefaultTimeout = TimeSpan.FromSeconds(120);

    public required string Id { get; init; }

    /// <summary>Stare ID z v3.2.</summary>
    public IReadOnlyList<string> Aliases { get; init; } = Array.Empty<string>();

    public required string Category { get; init; }

    public required TweakKind Kind { get; init; }

    public required string Title { get; init; }

    /// <summary>Krótki ludzki opis (UI): co zmiana daje człowiekowi.</summary>
    public string Summary { get; init; } = string.Empty;

    /// <summary>Realny efekt na co dzień (sekcja „Co zyskasz”): wymagany dla tweaka i akcji, zabroniony dla panelu.</summary>
    public string? Benefit { get; init; }

    public string Description { get; init; } = string.Empty;

    public required RiskLevel Risk { get; init; }

    public required TweakScope Scope { get; init; }

    public bool RequiresReboot { get; init; }

    /// <summary>Tylko kind=tweak: pozycja jest wstępnie zaznaczana do serii (nigdy nie wykonuje się sama).</summary>
    public bool Suggested { get; init; }

    /// <summary>Rodziny edycji, na których pozycja działa; null = wszystkie. Nieznana edycja systemu jest traktowana jak obsługiwana.</summary>
    public IReadOnlyList<EditionFamily>? Editions { get; init; }

    public int? MinBuild { get; init; }

    public int? MaxBuild { get; init; }

    /// <summary>Limit czasu skryptów; null = bez limitu (można tylko anulować).</summary>
    public TimeSpan? Timeout { get; init; } = DefaultTimeout;

    public bool Reversible { get; init; } = true;

    public IReadOnlyList<string> Warnings { get; init; } = Array.Empty<string>();

    public IReadOnlyList<string> Conflicts { get; init; } = Array.Empty<string>();

    public IReadOnlyList<RegistryEntry> Registry { get; init; } = Array.Empty<RegistryEntry>();

    public IReadOnlyList<ServiceEntry> Services { get; init; } = Array.Empty<ServiceEntry>();

    public ScriptSet? Script { get; init; }

    /// <summary>Uruchomienie programu systemowego poza Job Objectem (tylko akcja/panel, zamiast script.apply).</summary>
    public LaunchSpec? Launch { get; init; }

    /// <summary>Nazwa handlera akcji napisanego w C# (tylko akcja, zamiast script.apply i launch).</summary>
    public string? Handler { get; init; }
}
