using System;
using System.Threading;
using DSOptimizer.Core.Abstractions;
using DSOptimizer.Core.Errors;
using DSOptimizer.Core.Model;

namespace DSOptimizer.Core.Operations;

public sealed class ScriptOperation : IOperation
{
    private readonly ScriptSet _script;
    private readonly IScriptRunner _runner;
    private readonly string _userSid;
    private readonly TimeSpan? _timeout;
    private readonly IProgress<string>? _output;

    public ScriptOperation(ScriptSet script, IScriptRunner runner, string userSid, TimeSpan? timeout, IProgress<string>? output)
    {
        _script = script ?? throw new ArgumentNullException(nameof(script));
        _runner = runner ?? throw new ArgumentNullException(nameof(runner));
        _userSid = userSid ?? throw new ArgumentNullException(nameof(userSid));
        _timeout = timeout;
        _output = output;
    }

    public OperationKind Kind => OperationKind.Script;

    public bool IsDetectOnly => _script.Apply is null && _script.Undo is null;

    public bool ParticipatesInDetection => _script.Detect is not null;

    public string Describe() => "Skrypt PowerShell";

    public OperationState Detect(CancellationToken cancellationToken)
    {
        if (_script.Detect is null)
        {
            return OperationState.NotApplicable;
        }

        ScriptResult result;
        try
        {
            result = Run(_script.Detect, string.Empty, cancellationToken);
        }
        catch (OperationException)
        {
            return OperationState.Unknown;
        }

        if (result.ExitCode != 0)
        {
            return OperationState.Unknown;
        }

        return result.LastLine switch
        {
            "Applied" => OperationState.Match,
            "NotApplied" => OperationState.Mismatch,
            "NotSupported" => OperationState.NotSupported,
            _ => OperationState.Unknown,
        };
    }

    public SnapshotRecord Capture(CancellationToken cancellationToken)
    {
        if (_script.Capture is null)
        {
            return SnapshotRecord.ForScript(null);
        }

        var result = Run(_script.Capture, string.Empty, cancellationToken);
        EnsureSuccess(result, "capture");
        return SnapshotRecord.ForScript(result.LastLine ?? string.Empty);
    }

    public void Apply(CancellationToken cancellationToken)
    {
        if (_script.Apply is not null)
        {
            EnsureSuccess(Run(_script.Apply, string.Empty, cancellationToken), "apply");
        }
    }

    public void Restore(SnapshotRecord snapshot, CancellationToken cancellationToken)
    {
        if (snapshot is null || !Owns(snapshot))
        {
            throw new ArgumentException("Snapshot nie należy do operacji skryptu.", nameof(snapshot));
        }

        if (_script.Undo is null)
        {
            throw new OperationException(OperationErrorKind.Unexpected, "Skrypt tweaka nie ma kroku undo – zmiany nie da się cofnąć.");
        }

        EnsureSuccess(Run(_script.Undo, snapshot.Capture ?? string.Empty, cancellationToken), "undo");
    }

    public SnapshotRecord? DefaultsSnapshot() => _script.Undo is null ? null : SnapshotRecord.ForScript(null);

    public bool Owns(SnapshotRecord snapshot) => snapshot is not null && snapshot.Op == OperationKind.Script;

    public PlannedChange Plan(CancellationToken cancellationToken)
        => new() { Description = Describe(), Current = "(nieznana przed wykonaniem)", Target = "wykonanie skryptu apply" };

    private ScriptResult Run(string body, string snapshot, CancellationToken cancellationToken)
        => _runner.Run(new ScriptRequest { Body = body, UserSid = _userSid, Snapshot = snapshot, Timeout = _timeout }, _output, cancellationToken);

    private static void EnsureSuccess(ScriptResult result, string phase)
    {
        if (result.ExitCode != 0)
        {
            var errors = result.Errors.Count == 0 ? "(brak stderr)" : string.Join(" | ", result.Errors);
            throw new OperationException(OperationErrorKind.ScriptFailed, $"Skrypt {phase} zakończył się kodem {result.ExitCode}: {errors}");
        }
    }
}
