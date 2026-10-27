using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using DSOptimizer.Core.Abstractions;
using DSOptimizer.Core.Apps;

namespace DSOptimizer.Tests.Unit.Fakes;

/// <summary>
/// Usuwanie na niby: AppX po nazwie rodziny pakietu, program po pliku deinstalatora z <c>UninstallString</c>/<c>QuietUninstallString</c>.
/// <see cref="Keep"/> = Id aplikacji, które „zostają” po próbie (wynik „Nie udało się”). Nic nie uruchamia.
/// </summary>
public sealed class FakeAppRemover : IAppRemover
{
    private readonly FakeInstalledAppsReader _reader;

    public FakeAppRemover(FakeInstalledAppsReader reader) => _reader = reader ?? throw new ArgumentNullException(nameof(reader));

    public List<(AppxInfo Appx, string Sid, StoreRemovalScope Scope, bool Deprovision)> StoreCalls { get; } = new();

    public List<UninstallPlan> UninstallerCalls { get; } = new();

    public HashSet<string> Keep { get; } = new(StringComparer.Ordinal);

    /// <summary>Kod wyjścia każdego deinstalatora (domyślnie 0).</summary>
    public int ExitCode { get; set; }

    /// <summary>Wołane przy każdej próbie, przed skutkiem (np. blokada w teście współbieżności).</summary>
    public Action? OnAttempt { get; set; }

    public int Attempts => StoreCalls.Count + UninstallerCalls.Count;

    public ScriptResult RemoveStoreApp(AppxInfo appx, string userSid, StoreRemovalScope scope, bool deprovision, CancellationToken ct)
    {
        StoreCalls.Add((appx, userSid, scope, deprovision));
        OnAttempt?.Invoke();
        Effect(_reader.Apps.FirstOrDefault(app => app.Appx?.PackageFamilyName == appx.PackageFamilyName));
        return new ScriptResult { ExitCode = 0 };
    }

    public UninstallerRun RunUninstaller(UninstallPlan plan, TimeSpan timeout, CancellationToken ct)
    {
        UninstallerCalls.Add(plan);
        OnAttempt?.Invoke();
        Effect(_reader.Apps.FirstOrDefault(app => app.Win32 is { } win32
            && ((win32.UninstallString?.IndexOf(plan.FileName, StringComparison.OrdinalIgnoreCase) ?? -1) >= 0
                || (win32.QuietUninstallString?.IndexOf(plan.FileName, StringComparison.OrdinalIgnoreCase) ?? -1) >= 0)));
        return new UninstallerRun(ExitCode, false);
    }

    private void Effect(InstalledApp? app)
    {
        if (app is not null && !Keep.Contains(app.Id))
        {
            _reader.Remove(app.Id);
        }
    }
}
