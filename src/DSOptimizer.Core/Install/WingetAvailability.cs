using System;
using System.Collections.Generic;
using System.Linq;
using DSOptimizer.Core.Abstractions;
using DSOptimizer.Core.Apps;

namespace DSOptimizer.Core.Install;

/// <summary>Stan winget dla serii instalacji i strony „Instalacja”.</summary>
public enum WingetState
{
    Ready,
    OtherAccount,
    UserUncertain,
    NotRegistered,
    NotWorking,
}

/// <summary>Stan winget, wersja (tylko przy <see cref="WingetState.Ready"/>) i tekst dla użytkownika.</summary>
public sealed record WingetStatus(WingetState State, string? Version, string Text);

/// <summary>
/// Reguła <c>repair.winget</c> w C# (spec 2): niepewny użytkownik → inne konto → brak pakietu App Installera dla konta →
/// <c>winget --version</c> z kodem 0 i numerem wersji. W pierwszych trzech krokach winget nie jest uruchamiany wcale.
/// </summary>
public static class WingetAvailability
{
    private const string AppInstallerPackage = "Microsoft.DesktopAppInstaller";

    /// <param name="raw">Surowy odczyt aplikacji konta (<c>IInstalledAppsReader.Read(user.Sid).Apps</c>).</param>
    /// <param name="accountName">SID → nazwa konta do tekstu odmowy; <c>null</c>, pusta albo wyjątek = sam SID.</param>
    public static WingetStatus Check(
        IUserContext user,
        string processSid,
        IReadOnlyList<InstalledApp> raw,
        IWingetRunner runner,
        Func<string, string?> accountName)
    {
        if (user is null)
        {
            throw new ArgumentNullException(nameof(user));
        }

        if (processSid is null)
        {
            throw new ArgumentNullException(nameof(processSid));
        }

        if (raw is null)
        {
            throw new ArgumentNullException(nameof(raw));
        }

        if (runner is null)
        {
            throw new ArgumentNullException(nameof(runner));
        }

        if (accountName is null)
        {
            throw new ArgumentNullException(nameof(accountName));
        }

        var account = CheckAccount(user, processSid, accountName);
        if (account is not null)
        {
            return account;
        }

        var registered = raw.Any(app => app.Kind == AppKind.Store
            && app.Appx is not null
            && string.Equals(app.Appx.PackageName, AppInstallerPackage, StringComparison.OrdinalIgnoreCase));
        if (!registered)
        {
            return new WingetStatus(WingetState.NotRegistered, null, InstallText.NotRegistered);
        }

        WingetRun run;
        try
        {
            run = runner.Run(WingetArguments.Version(), InstallTimeouts.Version, null);
        }
        catch (Exception ex)
        {
            return NotWorking(InstallText.VersionStartFailed(ex.Message));
        }

        if (run.TimedOut || run.ExitCode is null)
        {
            return NotWorking(InstallText.VersionTimedOut(InstallTimeouts.Version));
        }

        if (run.ExitCode != 0)
        {
            return NotWorking(InstallText.VersionExitCode(run.ExitCode.Value));
        }

        var version = WingetOutput.ParseVersion(run.Output);
        return version is null
            ? NotWorking(InstallText.VersionWithoutNumber)
            : new WingetStatus(WingetState.Ready, version, InstallText.WingetReady(version));
    }

    /// <summary>
    /// Czy przy tym stanie winget mogą ruszyć pozycje z GitHuba (nie używają winget): tak przy <see cref="WingetState.Ready"/>,
    /// <see cref="WingetState.NotRegistered"/> i <see cref="WingetState.NotWorking"/>; odmowa konta blokuje także je (decyzja planu 2).
    /// </summary>
    public static bool AllowsGitHub(WingetStatus status)
    {
        if (status is null)
        {
            throw new ArgumentNullException(nameof(status));
        }

        return status.State is WingetState.Ready or WingetState.NotRegistered or WingetState.NotWorking;
    }

    /// <summary>Kroki 1–2 (bez winget): niepewny użytkownik albo inne konto; <c>null</c> = konto w porządku.</summary>
    internal static WingetStatus? CheckAccount(IUserContext user, string processSid, Func<string, string?> accountName)
    {
        if (!user.IsCertain)
        {
            return new WingetStatus(WingetState.UserUncertain, null, InstallText.UserUncertain);
        }

        if (!string.Equals(processSid, user.Sid, StringComparison.OrdinalIgnoreCase))
        {
            // Nazwa konta procesu tylko do dopisku Administrator Protection (Ruling R17); nieznana = bez dopisku.
            var text = InstallText.OtherAccount(NameOf(user.Sid, accountName) ?? user.Sid, NameOf(processSid, accountName));
            return new WingetStatus(WingetState.OtherAccount, null, text);
        }

        return null;
    }

    /// <summary>SID → nazwa; <c>null</c>, pusta albo wyjątek = <c>null</c>.</summary>
    private static string? NameOf(string sid, Func<string, string?> accountName)
    {
        try
        {
            var name = accountName(sid);
            return string.IsNullOrWhiteSpace(name) ? null : name;
        }
        catch (Exception)
        {
            return null;
        }
    }

    private static WingetStatus NotWorking(string detail) => new(WingetState.NotWorking, null, InstallText.WingetNotWorking(detail));
}
