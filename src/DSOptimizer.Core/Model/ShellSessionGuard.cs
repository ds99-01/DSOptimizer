using System;
using System.Collections.Generic;
using DSOptimizer.Core.Abstractions;
using DSOptimizer.Core.Localization;

namespace DSOptimizer.Core.Model;

/// <summary>
/// Strażnik <c>shell:::{GUID}</c> (spec, sekcja 4, A6): <c>explorer.exe</c> uruchomiony z procesu podniesionego oddaje otwarcie
/// istniejącej powłoki tylko wtedy, gdy w tej samej sesji działa powłoka (explorer.exe bez /factory) tego samego konta; inaczej powstaje osobny,
/// podniesiony Eksplorer (błąd v3.2, zmierzony w VM jako K1). Launcher nie startuje wtedy niczego.
/// </summary>
public static class ShellSessionGuard
{
    public static string RefusalMessage => L.T("Brak Eksploratora tego konta w bieżącej sesji – nie uruchamiam podniesionego Eksploratora.");

    public static bool HasOwnExplorer(IEnumerable<ExplorerProcessInfo> explorers, int sessionId, string ownerSid)
    {
        if (explorers is null)
        {
            throw new ArgumentNullException(nameof(explorers));
        }

        if (string.IsNullOrEmpty(ownerSid))
        {
            return false;
        }

        foreach (var explorer in explorers)
        {
            if (explorer.SessionId == sessionId
                && string.Equals(explorer.OwnerSid, ownerSid, StringComparison.OrdinalIgnoreCase)
                && IsShell(explorer.CommandLine))
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>
    /// Powłoka = <c>explorer.exe</c> bez <c>/factory</c> w linii poleceń (jak w R18). Procesy <c>explorer.exe /factory,{…} -Embedding</c>
    /// (rodzic svchost.exe) przeżywają śmierć powłoki, więc nie dowodzą jej obecności. Brak linii poleceń = nie powłoka (odmowa):
    /// administrator dostaje <c>CommandLine</c> z WMI dla procesów swojej sesji, a pomyłka w drugą stronę oznaczałaby start podniesionego Eksploratora.
    /// Strażnik nie sprawdza tokena znalezionej powłoki (świadome ograniczenie).
    /// </summary>
    private static bool IsShell(string? commandLine)
        => commandLine is { Length: > 0 } && !string.IsNullOrWhiteSpace(commandLine) && commandLine.IndexOf("/factory", StringComparison.OrdinalIgnoreCase) < 0;
}
