using System;
using System.Collections.Generic;

namespace DSOptimizer.Core.Engine;

/// <summary>Rejestr handlerów akcji po nazwie. Duplikat nazwy to błąd programisty.</summary>
public sealed class ActionHandlerSet
{
    private readonly Dictionary<string, IActionHandler> _handlers = new(StringComparer.Ordinal);

    public ActionHandlerSet(IEnumerable<IActionHandler> handlers)
    {
        if (handlers is null)
        {
            throw new ArgumentNullException(nameof(handlers));
        }

        foreach (var handler in handlers)
        {
            if (_handlers.ContainsKey(handler.Name))
            {
                throw new ArgumentException($"Duplikat handlera '{handler.Name}'.", nameof(handlers));
            }

            _handlers.Add(handler.Name, handler);
        }
    }

    public static ActionHandlerSet Empty { get; } = new(Array.Empty<IActionHandler>());

    public IReadOnlyCollection<string> Names => _handlers.Keys;

    public bool TryGet(string name, out IActionHandler handler)
    {
        if (_handlers.TryGetValue(name, out var found))
        {
            handler = found;
            return true;
        }

        handler = null!;
        return false;
    }
}
