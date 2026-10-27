using System;
using System.Collections.Generic;
using DSOptimizer.Core.Engine;
using DSOptimizer.Core.Install;

namespace DSOptimizer.Core.Cli;

public static class CliExitCodes
{
    /// <summary>
    /// Kod serii <c>--install-apps</c> (spec 5.5): wszystkie pozycje Zainstalowano/Już zainstalowane = 0; jakakolwiek Nie wykonano
    /// (<c>Failed</c>/<c>Skipped</c>) albo odmowa (<c>Refused</c>) = 1; w przeciwnym razie Nie potwierdzono (także limit czasu
    /// i „zgłoszone przez winget”) = 4. Pusta seria i wynik spoza instalacji = wyjątek (błąd programu, nie wynik).
    /// </summary>
    public static CliExitCode ForInstall(IReadOnlyList<InstallItemReport> items)
    {
        if (items is null)
        {
            throw new ArgumentNullException(nameof(items));
        }

        if (items.Count == 0)
        {
            throw new ArgumentException("Pusta seria instalacji nie ma kodu wyjścia.", nameof(items));
        }

        var failed = false;
        var incomplete = false;
        foreach (var item in items)
        {
            switch (item.Outcome)
            {
                case TweakOutcome.Installed:
                case TweakOutcome.AlreadyInstalled:
                    break;
                case TweakOutcome.Failed:
                case TweakOutcome.Skipped:
                case TweakOutcome.Refused:
                    failed = true;
                    break;
                case TweakOutcome.NotConfirmed:
                case TweakOutcome.InstalledUnverified:
                    incomplete = true;
                    break;
                default:
                    throw new ArgumentOutOfRangeException(nameof(items), item.Outcome, "Wynik spoza serii instalacji.");
            }
        }

        return failed ? CliExitCode.OperationFailed : incomplete ? CliExitCode.Incomplete : CliExitCode.Success;
    }

    public static CliExitCode FromOutcome(TweakOutcome outcome)
    {
        switch (outcome)
        {
            case TweakOutcome.Applied:
            case TweakOutcome.AlreadyApplied:
            case TweakOutcome.PendingReboot:
            case TweakOutcome.Undone:
            case TweakOutcome.UndoneFromDefaults:
            case TweakOutcome.Executed:
            case TweakOutcome.Removed:
            case TweakOutcome.Installed:
            case TweakOutcome.AlreadyInstalled:
                return CliExitCode.Success;
            case TweakOutcome.Failed:
            case TweakOutcome.FailedRolledBack:
            case TweakOutcome.Refused:
            case TweakOutcome.NotSupported:
            case TweakOutcome.Skipped:
                return CliExitCode.OperationFailed;
            case TweakOutcome.FailedRollbackIncomplete:
            case TweakOutcome.UndoIncomplete:
            case TweakOutcome.NotConfirmed:
            case TweakOutcome.InstalledUnverified:
                return CliExitCode.Incomplete;
            default:
                throw new ArgumentOutOfRangeException(nameof(outcome), outcome, "Nieobsłużony wynik operacji.");
        }
    }
}
