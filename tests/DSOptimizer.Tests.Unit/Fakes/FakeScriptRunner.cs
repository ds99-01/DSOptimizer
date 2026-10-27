using System;
using System.Collections.Generic;
using System.Threading;
using DSOptimizer.Core.Abstractions;
using DSOptimizer.Core.Errors;

namespace DSOptimizer.Tests.Unit.Fakes;

/// <summary>Odpowiada na skrypty po dokładnej treści (Body). Nieznana treść = błąd testu.</summary>
public sealed class FakeScriptRunner : IScriptRunner
{
    private readonly Dictionary<string, Func<ScriptRequest, ScriptResult>> _handlers = new(StringComparer.Ordinal);

    /// <summary>Wywoływane na początku Run (przed handlerem), np. by wysłać linie na <c>output</c> i dopiero potem rzucić wyjątek.</summary>
    public Action<IProgress<string>?>? BeforeThrow { get; set; }

    public List<ScriptRequest> Calls { get; } = new();

    public static ScriptResult Result(int exitCode, params string[] output) => new()
    {
        ExitCode = exitCode,
        Output = output,
        Errors = exitCode == 0 ? Array.Empty<string>() : new[] { "fake stderr" },
    };

    public void On(string body, Func<ScriptRequest, ScriptResult> handler) => _handlers[body] = handler;

    public void On(string body, int exitCode, params string[] output) => On(body, _ => Result(exitCode, output));

    public void Throw(string body, OperationErrorKind kind)
        => On(body, _ => throw new OperationException(kind, $"Skrypt {body}: {kind} (fake)."));

    public ScriptResult Run(ScriptRequest request, IProgress<string>? output, CancellationToken cancellationToken)
    {
        Calls.Add(request);
        if (cancellationToken.IsCancellationRequested)
        {
            throw new OperationException(OperationErrorKind.Cancelled, "Anulowano (fake).");
        }

        if (!_handlers.TryGetValue(request.Body, out var handler))
        {
            throw new InvalidOperationException($"Brak handlera dla skryptu: {request.Body}");
        }

        BeforeThrow?.Invoke(output);
        var result = handler(request);
        foreach (var line in result.Output)
        {
            output?.Report(line);
        }

        return result;
    }
}
