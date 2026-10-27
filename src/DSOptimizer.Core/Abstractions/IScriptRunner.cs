using System;
using System.Threading;

namespace DSOptimizer.Core.Abstractions;

public interface IScriptRunner
{
    /// <summary>Uruchamia skrypt. Timeout/anulowanie: OperationException(Timeout|Cancelled); niezerowy kod wyjścia to NIE wyjątek.</summary>
    ScriptResult Run(ScriptRequest request, IProgress<string>? output, CancellationToken cancellationToken);
}
