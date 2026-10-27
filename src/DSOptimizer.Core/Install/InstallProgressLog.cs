using System;
using System.Collections.Generic;
using DSOptimizer.Core.Engine;

namespace DSOptimizer.Core.Install;

/// <summary>
/// Linie dziennika głównego okna w trakcie serii instalacji (spec 5.2, bez zalewania): jedna linia „&lt;tytuł&gt;: Pobieranie”
/// albo „&lt;tytuł&gt;: Instalowanie” przy zmianie etapu pozycji. Sam procent linii nie daje, a ta sama linia nigdy nie wraca
/// (np. etap wracający do pobierania). Wynik pozycji to linie raportu serii, nie tej klasy.
/// Klasa nie jest bezpieczna wątkowo: wołać na wątku UI.
/// </summary>
public sealed class InstallProgressLog
{
    private readonly IReadOnlyDictionary<string, string> _titles;
    private readonly HashSet<string> _emitted = new(StringComparer.Ordinal);

    /// <param name="titles">Id pozycji serii → tytuł; brak wpisu = samo Id.</param>
    public InstallProgressLog(IReadOnlyDictionary<string, string> titles)
        => _titles = titles ?? throw new ArgumentNullException(nameof(titles));

    /// <summary>Linia do dziennika albo <c>null</c>, gdy zdarzenie nic nowego nie mówi.</summary>
    public string? OnStep(BatchStepEvent e)
    {
        if (e is null)
        {
            throw new ArgumentNullException(nameof(e));
        }

        if (e.Phase != BatchStepPhase.Progress || string.IsNullOrWhiteSpace(e.Stage))
        {
            return null;
        }

        var id = e.TweakId ?? string.Empty;
        if (!_emitted.Add(id + "\n" + e.Stage))
        {
            return null;
        }

        var title = _titles.TryGetValue(id, out var known) ? known : id;
        return title + ": " + e.Stage;
    }
}
