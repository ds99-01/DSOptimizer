using System;
using System.Collections.Generic;
using DSOptimizer.Core.Engine;

namespace DSOptimizer.Core.Cli;

public sealed class CliOptions
{
    public required CliCommand Command { get; init; }

    /// <summary>Id celu polecenia: tweaka albo aplikacji (<c>--remove-app</c>, <c>appx:…</c>/<c>win32:…</c>); pusty dla <c>--list-apps</c>.</summary>
    public required string TweakId { get; init; }

    public string? CatalogPath { get; init; }

    public required string OutPath { get; init; }

    public RestorePointPolicy RestorePointPolicy { get; init; } = RestorePointPolicy.IfAvailable;

    /// <summary>Lista aplikacji do instalacji z pliku (<c>--apps-catalog</c>, tylko <c>--list-install</c>/<c>--install-apps</c>); null = wbudowana.</summary>
    public string? AppsCatalogPath { get; init; }

    /// <summary>Id pozycji <c>--install-apps</c> w kolejności podania: <c>install.&lt;nazwa&gt;</c> albo <c>winget:&lt;wingetId&gt;</c>.</summary>
    public IReadOnlyList<string> InstallIds { get; init; } = Array.Empty<string>();

    /// <summary>Zapytanie <c>--search-apps</c>.</summary>
    public string? SearchQuery { get; init; }
}
