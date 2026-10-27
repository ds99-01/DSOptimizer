using System;
using System.IO;

namespace DSOptimizer.Core.Storage;

/// <summary>
/// Reguła kopii lustrzanej logów i raportów: kopia obok exe (<c>logs\NAZWA_KOMPUTERA</c>) powstaje tylko wtedy,
/// gdy exe nie leży na dysku systemowym (np. pendrive albo udział UNC). Exe skopiowany na pulpit klienta nic tam nie tworzy.
/// </summary>
public static class LogMirrorPolicy
{
    /// <summary>Katalog kopii lustrzanej albo <c>null</c>, gdy korzeń ścieżki exe jest korzeniem katalogu Windows.</summary>
    public static string? MirrorDirectory(string exeDirectory, string windowsDirectory, string machineName)
    {
        if (exeDirectory is null)
        {
            throw new ArgumentNullException(nameof(exeDirectory));
        }

        if (windowsDirectory is null)
        {
            throw new ArgumentNullException(nameof(windowsDirectory));
        }

        if (machineName is null)
        {
            throw new ArgumentNullException(nameof(machineName));
        }

        var exeRoot = Path.GetPathRoot(exeDirectory);
        var systemRoot = Path.GetPathRoot(windowsDirectory);
        if (!string.IsNullOrEmpty(exeRoot)
            && string.Equals(exeRoot, systemRoot, StringComparison.OrdinalIgnoreCase))
        {
            return null;
        }

        return Path.Combine(exeDirectory, "logs", machineName);
    }
}
