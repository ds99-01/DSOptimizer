using System;
using System.Collections.Generic;
using System.Globalization;
using System.Management;
using DSOptimizer.Core.Abstractions;

namespace DSOptimizer.Windows;

/// <summary>
/// Procesy <c>explorer.exe</c> z sesją i SID właściciela przez WMI (<c>Win32_Process</c>, metoda <c>GetOwnerSid</c>) – ten sam mechanizm,
/// którego używa <see cref="InteractiveUserContext"/>, więc bez nowych pakietów i bez P/Invoke tokenów (odczyt SID z tokenu
/// cudzego procesu wymagałby OpenProcess/OpenProcessToken, a przy procesach innych kont także uprawnień, które WMI ma w usłudze).
/// Proces, którego właściciela WMI nie zwróci (np. już zakończony), jest pomijany.
/// </summary>
public sealed class WindowsExplorerProcessQuery : IExplorerProcessQuery
{
    // Nazwa parametru wyjściowego GetOwnerSid (WMI nie rozróżnia wielkości liter); wielkie litery omijają CA1507 dla właściwości Sid.
    private const string OwnerSidParameter = "SID";

    public IReadOnlyList<ExplorerProcessInfo> GetExplorerProcesses()
    {
        var found = new List<ExplorerProcessInfo>();
        using var searcher = new ManagementObjectSearcher("SELECT Handle, SessionId, CommandLine FROM Win32_Process WHERE Name = 'explorer.exe'");
        using var processes = searcher.Get();
        foreach (ManagementObject process in processes)
        {
            using (process)
            {
                try
                {
                    using var result = process.InvokeMethod("GetOwnerSid", null, null);
                    if (result?[OwnerSidParameter] is string sid && Convert.ToUInt32(result["ReturnValue"], CultureInfo.InvariantCulture) == 0)
                    {
                        found.Add(new ExplorerProcessInfo(Convert.ToInt32(process["SessionId"], CultureInfo.InvariantCulture), sid, process["CommandLine"] as string));
                    }
                }
                catch (ManagementException)
                {
                    // Proces zniknął między wyliczeniem a pytaniem o właściciela – nie liczy się jako powłoka.
                }
            }
        }

        return found;
    }
}
