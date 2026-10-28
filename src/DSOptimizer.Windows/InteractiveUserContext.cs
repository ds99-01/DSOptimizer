using System;
using System.Globalization;
using System.Management;
using System.Runtime.InteropServices;
using System.Security.Principal;
using DSOptimizer.Core.Abstractions;
using DSOptimizer.Core.Localization;
using DSOptimizer.Windows.Native;

namespace DSOptimizer.Windows;

/// <summary>
/// Zalogowany użytkownik = właściciel explorer.exe w aktywnej sesji konsoli. Dzięki temu zmiany HKCU trafiają
/// do właściwego profilu nawet wtedy, gdy aplikację podniesiono innym kontem administratora.
/// </summary>
public sealed class InteractiveUserContext : IUserContext
{
    private const string Component = "User";

    // Nazwa parametru wyjściowego GetOwnerSid (WMI nie rozróżnia wielkości liter); wielkie litery omijają CA1507 dla właściwości Sid.
    private const string OwnerSidParameter = "SID";

    private InteractiveUserContext(string sid, bool isCertain)
    {
        Sid = sid;
        IsCertain = isCertain;
    }

    public string Sid { get; }

    public bool IsCertain { get; }

    public static InteractiveUserContext Resolve(ILog log)
    {
        if (log is null)
        {
            throw new ArgumentNullException(nameof(log));
        }

        try
        {
            var session = NativeMethods.WTSGetActiveConsoleSessionId();
            using var searcher = new ManagementObjectSearcher("SELECT Handle, SessionId FROM Win32_Process WHERE Name = 'explorer.exe'");
            using var processes = searcher.Get();
            foreach (ManagementObject process in processes)
            {
                using (process)
                {
                    if (Convert.ToUInt32(process["SessionId"], CultureInfo.InvariantCulture) != session)
                    {
                        continue;
                    }

                    using var result = process.InvokeMethod("GetOwnerSid", null, null);
                    if (result?[OwnerSidParameter] is string sid && Convert.ToUInt32(result["ReturnValue"], CultureInfo.InvariantCulture) == 0)
                    {
                        return new InteractiveUserContext(sid, isCertain: true);
                    }
                }
            }
        }
        catch (Exception ex) when (ex is ManagementException or COMException)
        {
            log.Warn(Component, L.F("Nie udało się ustalić właściciela explorer.exe: {0}", ex.Message));
        }

        var current = WindowsIdentity.GetCurrent().User?.Value
            ?? throw new InvalidOperationException(L.T("Brak SID bieżącego procesu."));
        log.Warn(Component, L.T("Nie znaleziono explorer.exe w sesji konsoli – zmiany HKCU trafią do konta bieżącego procesu."));
        return new InteractiveUserContext(current, isCertain: false);
    }
}
