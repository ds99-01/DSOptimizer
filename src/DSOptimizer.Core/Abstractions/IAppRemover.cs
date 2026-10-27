using System;
using System.Threading;
using DSOptimizer.Core.Apps;

namespace DSOptimizer.Core.Abstractions;

/// <summary>Wynik czekania na deinstalator: kod wyjścia (null, gdy proces jeszcze działa) i czy minął limit czasu.</summary>
public sealed record UninstallerRun(int? ExitCode, bool TimedOut);

/// <summary>Wykonawca usunięcia aplikacji. Sam niczego nie sprawdza: o wyniku decyduje <see cref="AppRemovalEngine"/> świeżym odczytem.</summary>
public interface IAppRemover
{
    /// <summary>
    /// Usuwa pakiet AppX. <see cref="StoreRemovalScope.CurrentUser"/>: <c>Remove-AppxPackage -User &lt;SID&gt;</c> (tylko gdy SID procesu =
    /// <paramref name="userSid"/>); <see cref="StoreRemovalScope.AllUsers"/>: <c>-AllUsers</c> (wszystkie konta i obraz systemu).
    /// <paramref name="deprovision"/> = także <c>Remove-AppxProvisionedPackage -Online</c>, gdy pakiet jest w obrazie.
    /// Limit czasu / anulowanie: <c>OperationException</c> (jak <see cref="IScriptRunner"/>).
    /// </summary>
    ScriptResult RemoveStoreApp(AppxInfo appx, string userSid, StoreRemovalScope scope, bool deprovision, CancellationToken ct);

    /// <summary>
    /// Uruchamia deinstalator przez <c>CreateProcessW</c> (bez Job Objectu) i czeka najwyżej <paramref name="timeout"/>.
    /// Po limicie albo anulowaniu przestaje czekać (<c>TimedOut=true</c>), a proces działa dalej – nigdy nie jest zabijany.
    /// </summary>
    UninstallerRun RunUninstaller(UninstallPlan plan, TimeSpan timeout, CancellationToken ct);
}
