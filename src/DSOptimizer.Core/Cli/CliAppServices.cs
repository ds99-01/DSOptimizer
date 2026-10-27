using System;
using System.Collections.Generic;
using DSOptimizer.Core.Abstractions;
using DSOptimizer.Core.Apps;

namespace DSOptimizer.Core.Cli;

/// <summary>Usługi poleceń <c>--list-apps</c>/<c>--remove-app</c> (składa je host; testy podają fałszywe).</summary>
public sealed class CliAppServices
{
    public required IInstalledAppsReader Reader { get; init; }

    public required IAppRemover Remover { get; init; }

    /// <summary>Klasyfikacja aplikacji (<c>apps-bloat.json</c>): sugerowane śmieci i lista chroniona.</summary>
    public required AppsBloatCatalog Catalog { get; init; }

    /// <summary>SID konta procesu DSOptimizera (porównywany z zalogowanym: zakres AppX i programy innego konta).</summary>
    public required string ProcessSid { get; init; }

    /// <summary>SID → nazwa konta (do powodu blokady); null = nieznana.</summary>
    public required Func<string, string?> AccountName { get; init; }

    public required Func<string, bool> FileExists { get; init; }

    /// <summary>Katalog System32 (rozwiązanie <c>msiexec.exe</c>).</summary>
    public required string System32 { get; init; }

    /// <summary>Uśpienie między odczytami weryfikacji; null = <c>Thread.Sleep</c>.</summary>
    public Action<TimeSpan>? Sleep { get; init; }

    /// <summary>
    /// Klasyfikacja odczytu na wiersze; null = <see cref="AppClassifier.Classify"/> z <see cref="Catalog"/>. Szew testowy dla obrony
    /// w głąb strażnika CLI (wiersz, którego klasyfikator nigdy nie zwraca, np. grupa Protected z Removability=Removable).
    /// </summary>
    public Func<InstalledAppsReading, IReadOnlyList<AppRow>>? Classify { get; init; }
}
