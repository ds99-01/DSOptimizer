using DSOptimizer.Tests.Unit.Fakes;

namespace DSOptimizer.Tests.Unit.Engine;

/// <summary>
/// Stanowe skrypty dla TestTweaks.Scripted(): DETECT zwraca Applied po APPLY, NotApplied po UNDO;
/// CAPTURE zwraca CaptureValue; UNDO zapamiętuje otrzymany snapshot.
/// </summary>
internal sealed class ToggleScript
{
    public bool Applied { get; set; }

    public string CaptureValue { get; set; } = "2";

    public bool ApplyFails { get; set; }

    /// <summary>APPLY kończy się sukcesem, ale nic nie zmienia (symulacja „wymaga restartu” / nieskutecznego tweaka).</summary>
    public bool ApplyWithoutEffect { get; set; }

    public string? LastUndoSnapshot { get; private set; }

    public void Register(FakeScriptRunner runner)
    {
        runner.On("DETECT", _ => FakeScriptRunner.Result(0, Applied ? "Applied" : "NotApplied"));
        runner.On("CAPTURE", _ => FakeScriptRunner.Result(0, CaptureValue));
        runner.On("APPLY", _ =>
        {
            if (ApplyFails)
            {
                return FakeScriptRunner.Result(1);
            }

            if (!ApplyWithoutEffect)
            {
                Applied = true;
            }

            return FakeScriptRunner.Result(0);
        });
        runner.On("UNDO", request =>
        {
            Applied = false;
            LastUndoSnapshot = request.Snapshot;
            return FakeScriptRunner.Result(0);
        });
    }
}
