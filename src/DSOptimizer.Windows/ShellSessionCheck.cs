using System;
using System.Diagnostics;
using System.Security.Principal;
using DSOptimizer.Core.Abstractions;
using DSOptimizer.Core.Errors;
using DSOptimizer.Core.Model;

namespace DSOptimizer.Windows;

/// <summary>
/// Wspólny kod strażnika powłoki (<see cref="ShellSessionGuard"/>) dla <see cref="ShellProcessLauncher"/> i <see cref="ShellFileRevealer"/>:
/// przed startem <c>explorer.exe</c> z procesu podniesionego musi działać powłoka tego samego konta w bieżącej sesji.
/// </summary>
internal static class ShellSessionCheck
{
    public static void RequireOwnExplorer(IExplorerProcessQuery explorers, int sessionId, string ownerSid)
    {
        bool found;
        try
        {
            found = ShellSessionGuard.HasOwnExplorer(explorers.GetExplorerProcesses(), sessionId, ownerSid);
        }
        catch (Exception ex) when (ex is not OperationException)
        {
            throw new OperationException(
                OperationErrorKind.Unexpected,
                $"Nie można sprawdzić Eksploratora tego konta w bieżącej sesji – nie uruchamiam Eksploratora: {ex.Message}",
                ex);
        }

        if (!found)
        {
            throw new OperationException(OperationErrorKind.Unexpected, ShellSessionGuard.RefusalMessage);
        }
    }

    public static int CurrentSessionId()
    {
        using var process = Process.GetCurrentProcess();
        return process.SessionId;
    }

    public static string CurrentSid()
    {
        using var identity = WindowsIdentity.GetCurrent();
        return identity.User?.Value ?? throw new InvalidOperationException("Brak SID bieżącego procesu.");
    }
}
