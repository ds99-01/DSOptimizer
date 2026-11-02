using System;

namespace DSOptimizer.Core.Autostart;

/// <summary>
/// Identyfikatory pozycji autostartu (CLI, journal, zaznaczenie): <c>run:user:&lt;nazwa&gt;</c>, <c>run:machine:&lt;nazwa&gt;</c>,
/// <c>run32:&lt;nazwa&gt;</c>, <c>folder:user:&lt;plik&gt;</c>, <c>folder:common:&lt;plik&gt;</c>, <c>appx:&lt;rodzina&gt;!&lt;TaskId&gt;</c>.
/// Przedrostek bez rozróżniania wielkości liter, reszta dosłownie. Id porównuje się w całym module przez
/// <see cref="StringComparer.OrdinalIgnoreCase"/>.
/// </summary>
public static class AutostartIds
{
    private const string AppxPrefix = "appx:";

    private static readonly (string Prefix, AutostartSource Source)[] Prefixes =
    {
        ("run:user:", AutostartSource.RunUser),
        ("run:machine:", AutostartSource.RunMachine),
        ("run32:", AutostartSource.Run32),
        ("folder:user:", AutostartSource.FolderUser),
        ("folder:common:", AutostartSource.FolderCommon),
    };

    /// <summary>
    /// Id pozycji. Dla <see cref="AutostartSource.StartupTask"/> <paramref name="entryName"/> = TaskId i wymagana rodzina pakietu.
    /// Pusta nazwa (wartość Run bez nazwy) daje id, którego <see cref="TryParse"/> nie przyjmuje – taka pozycja jest tylko do odczytu.
    /// </summary>
    public static string For(AutostartSource source, string entryName, string? packageFamily = null)
    {
        if (entryName is null)
        {
            throw new ArgumentNullException(nameof(entryName));
        }

        if (source == AutostartSource.StartupTask)
        {
            if (string.IsNullOrEmpty(packageFamily))
            {
                throw new ArgumentException("Zadanie startowe wymaga rodziny pakietu.", nameof(packageFamily));
            }

            return AppxPrefix + packageFamily + "!" + entryName;
        }

        foreach (var (prefix, candidate) in Prefixes)
        {
            if (candidate == source)
            {
                return prefix + entryName;
            }
        }

        throw new ArgumentOutOfRangeException(nameof(source), source, null);
    }

    public static bool TryParse(string id, out AutostartSource source, out string entryName, out string? packageFamily)
    {
        source = default;
        entryName = string.Empty;
        packageFamily = null;
        if (string.IsNullOrEmpty(id))
        {
            return false;
        }

        if (id.StartsWith(AppxPrefix, StringComparison.OrdinalIgnoreCase))
        {
            var bang = id.IndexOf('!', AppxPrefix.Length);
            if (bang <= AppxPrefix.Length || bang == id.Length - 1)
            {
                return false;
            }

            source = AutostartSource.StartupTask;
            packageFamily = id.Substring(AppxPrefix.Length, bang - AppxPrefix.Length);
            entryName = id.Substring(bang + 1);
            return true;
        }

        foreach (var (prefix, candidate) in Prefixes)
        {
            if (id.StartsWith(prefix, StringComparison.OrdinalIgnoreCase) && id.Length > prefix.Length)
            {
                source = candidate;
                entryName = id.Substring(prefix.Length);
                return true;
            }
        }

        return false;
    }

    /// <summary>Pozycja konta (Run i folder konta, aplikacja ze Sklepu); nieprawidłowe id = false.</summary>
    public static bool IsUserScoped(string id)
        => TryParse(id, out var source, out _, out _) && AutostartSources.ScopeOf(source) != AutostartScope.Machine;
}
