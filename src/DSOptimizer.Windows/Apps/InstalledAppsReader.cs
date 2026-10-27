using System;
using System.Collections.Generic;
using System.IO;
using System.Security;
using DSOptimizer.Core.Abstractions;
using DSOptimizer.Core.Apps;
using DSOptimizer.Core.Errors;

namespace DSOptimizer.Windows.Apps;

/// <summary>Łączy odczyt AppX (<see cref="AppxReader"/>) i rejestru Uninstall (<see cref="UninstallRegistryReader"/>); błąd jednego źródła to ostrzeżenie.</summary>
public sealed class InstalledAppsReader : IInstalledAppsReader
{
    private readonly AppxReader _appx;
    private readonly Func<string, UninstallRegistryReader> _registryFor;

    public InstalledAppsReader(IScriptRunner scriptRunner)
        : this(new AppxReader(scriptRunner), UninstallRegistryReader.ForUser)
    {
    }

    public InstalledAppsReader(AppxReader appx, Func<string, UninstallRegistryReader> registryFor)
    {
        _appx = appx ?? throw new ArgumentNullException(nameof(appx));
        _registryFor = registryFor ?? throw new ArgumentNullException(nameof(registryFor));
    }

    public InstalledAppsReading Read(string userSid)
    {
        var apps = new List<InstalledApp>();
        var warnings = new List<string>();

        try
        {
            apps.AddRange(_appx.Read(userSid));
        }
        catch (Exception ex) when (ex is OperationException or InvalidOperationException or FormatException)
        {
            warnings.Add(AppsText.AppxReadFailed(ex.Message));
        }

        try
        {
            var registry = _registryFor(userSid).Read();
            apps.AddRange(registry.Apps);
            warnings.AddRange(registry.Warnings);
        }
        catch (Exception ex) when (ex is InvalidOperationException or ArgumentException or SecurityException or UnauthorizedAccessException or IOException)
        {
            warnings.Add(AppsText.RegistryReadFailed(ex.Message));
        }

        return new InstalledAppsReading(apps, warnings);
    }

    public AppPresence ReadPresence(InstalledApp app, string userSid, AppPresenceScope scope)
    {
        if (app is null)
        {
            throw new ArgumentNullException(nameof(app));
        }

        if (app.Appx is { } appx)
        {
            return _appx.ReadPresence(appx.PackageName, userSid, scope);
        }

        if (app.Win32 is { } win32)
        {
            try
            {
                return UninstallRegistryReader.KeyExists(win32.KeyPath) switch
                {
                    true => AppPresence.Present,
                    false => AppPresence.Absent,
                    null => AppPresence.Unknown,
                };
            }
            catch (ArgumentException)
            {
                return AppPresence.Unknown;
            }
        }

        return AppPresence.Unknown;
    }
}
