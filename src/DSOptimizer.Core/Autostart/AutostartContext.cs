using System;
using System.Collections.Generic;
using DSOptimizer.Core.Abstractions;

namespace DSOptimizer.Core.Autostart;

/// <summary>
/// Konto, którego autostart jest czytany i zmieniany: SID zalogowanego użytkownika (pewny albo nie), SID procesu programu, czy gałęzie
/// konta (<c>HKU\&lt;SID&gt;</c>, <c>HKU\&lt;SID&gt;_Classes</c>) są załadowane, miejsca autostartu i dowód zapisu od zera: źródła
/// <c>StartupApproved</c> (<see cref="WriteFromZeroConfirmed"/>) i dwa osobne stany zadania startowego Sklepu – klucz nie istnieje
/// (<see cref="StartupTaskKeyAbsentEnable"/>) i klucz systemowy ze <c>State</c> 0 (<see cref="StartupTaskState0Enable"/>), oba tylko dla
/// manifestu <c>Enabled="false"</c> (<see cref="AutostartEvidence"/>).
/// </summary>
public sealed record AutostartContext(
    string UserSid,
    bool UserCertain,
    string ProcessSid,
    bool UserHiveLoaded,
    bool ClassesHiveLoaded,
    AutostartLocations Locations,
    IReadOnlyCollection<AutostartSource> WriteFromZeroConfirmed,
    bool StartupTaskKeyAbsentEnable,
    bool StartupTaskState0Enable)
{
    /// <summary>Kontekst zalogowanego konta; gałąź jest załadowana, gdy <see cref="IRegistry.KeyStatus"/> zwraca <see cref="ReadStatus.Found"/>.</summary>
    public static AutostartContext Create(IUserContext user, string processSid, IRegistry registry)
    {
        if (user is null)
        {
            throw new ArgumentNullException(nameof(user));
        }

        if (processSid is null)
        {
            throw new ArgumentNullException(nameof(processSid));
        }

        if (registry is null)
        {
            throw new ArgumentNullException(nameof(registry));
        }

        var locations = AutostartLocations.ForUser(user.Sid);
        return new AutostartContext(
            user.Sid,
            user.IsCertain,
            processSid,
            registry.KeyStatus(locations.UserRoot) == ReadStatus.Found,
            registry.KeyStatus(locations.UserClassesRoot) == ReadStatus.Found,
            locations,
            AutostartEvidence.WriteFromZeroConfirmed,
            AutostartEvidence.StartupTaskKeyAbsentEnable,
            AutostartEvidence.StartupTaskState0Enable);
    }
}
