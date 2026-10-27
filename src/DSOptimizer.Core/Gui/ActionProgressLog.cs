using System;
using System.Collections.Generic;
using DSOptimizer.Core.Engine;

namespace DSOptimizer.Core.Gui;

/// <summary>
/// Linie dziennika z postępu długiej naprawy (spec 6, bez zalewania dziennika): „&lt;tytuł&gt;: &lt;etap&gt;” przy zmianie etapu
/// i „&lt;tytuł&gt;: &lt;etap&gt; &lt;N&gt;%” po przekroczeniu kolejnej dziesiątki (10…100) w bieżącym etapie. Skok o kilka dziesiątek
/// daje jedną linię z najwyższą, spadek procentu nie daje żadnej, a ta sama linia nigdy nie pojawia się dwa razy.
/// Klasa nie jest bezpieczna wątkowo: wołać na wątku UI (z <see cref="Progress{T}"/>).
/// </summary>
public sealed class ActionProgressLog
{
    private readonly string _title;
    private readonly HashSet<string> _emitted = new(StringComparer.Ordinal);
    private string? _stage;
    private int _tens;

    public ActionProgressLog(string title)
    {
        _title = title ?? throw new ArgumentNullException(nameof(title));
    }

    /// <summary>Nowe linie dla tego zdarzenia (zwykle zero albo jedna).</summary>
    public IReadOnlyList<string> OnProgress(ActionProgress p)
    {
        if (p is null)
        {
            throw new ArgumentNullException(nameof(p));
        }

        var lines = new List<string>();
        if (!string.Equals(p.Stage, _stage, StringComparison.Ordinal))
        {
            _stage = p.Stage;
            _tens = 0;
            Emit(lines, $"{_title}: {p.Stage}");
        }

        if (p.Percent is { } percent)
        {
            var tens = Math.Max(0, Math.Min(100, percent)) / 10 * 10;
            if (tens > _tens)
            {
                _tens = tens;
                Emit(lines, $"{_title}: {p.Stage} {tens}%");
            }
        }

        return lines;
    }

    private void Emit(List<string> lines, string line)
    {
        if (_emitted.Add(line))
        {
            lines.Add(line);
        }
    }
}
