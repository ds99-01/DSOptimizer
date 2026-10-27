using System.Threading;

namespace DSOptimizer.Core.Abstractions;

public interface IToolRunner
{
    /// <summary>
    /// Uruchamia narzędzie z System32 i zbiera jego wyjście (stdout + stderr w kolejności, strona kodowa OEM konsoli).
    /// Timeout/anulowanie: <see cref="Errors.OperationException"/> (<c>Timeout</c>/<c>Cancelled</c>) i zabicie całego drzewa procesów.
    /// Niezerowy kod wyjścia to NIE wyjątek. Nie można uruchomić: <c>OperationException</c> (<c>NotFound</c>/<c>Unexpected</c>).
    /// </summary>
    ToolResult Run(ToolRequest request, CancellationToken cancellationToken);
}
