namespace DSOptimizer.Core.Engine;

public enum TweakOutcome
{
    Applied,
    AlreadyApplied,
    PendingReboot,
    NotSupported,
    Failed,
    FailedRolledBack,
    FailedRollbackIncomplete,
    Undone,
    UndoIncomplete,
    UndoneFromDefaults,
    Refused,
    Executed,
    Skipped,

    /// <summary>Aplikacja usunięta i sprawdzona niezależnym odczytem (albo nie było jej już na komputerze).</summary>
    Removed,

    /// <summary>
    /// Aplikacja nadal jest, a deinstalator nie skończył w limicie czasu; przy instalacji: winget zgłosił sukces, ale odczyt zwrotny
    /// jej nie widzi, albo minął limit czasu (nie jest sukcesem, liczy się do nieudanych).
    /// </summary>
    NotConfirmed,

    /// <summary>Aplikacja zainstalowana przez winget i widoczna w odczycie zwrotnym (lista zainstalowanych albo <c>winget list</c>).</summary>
    Installed,

    /// <summary>Aplikacja była już zainstalowana (dopasowanie przed próbą albo kod winget „już zainstalowane”); nie porażka.</summary>
    AlreadyInstalled,

    /// <summary>
    /// Pozycja z wyszukiwania: winget zgłosił sukces, a odczytu zwrotnego nie da się potwierdzić (słabszy dowód; ani sukces,
    /// ani porażka, CLI kod 4).
    /// </summary>
    InstalledUnverified,
}
