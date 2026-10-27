using System;
using System.Collections.Generic;
using System.Linq;
using DSOptimizer.Core.Abstractions;
using DSOptimizer.Core.Apps;

namespace DSOptimizer.Tests.Unit.Fakes;

/// <summary>
/// Odczyt aplikacji z listy w pamięci: <see cref="Read"/> zwraca obecne aplikacje i ostrzeżenia, <see cref="ReadPresence"/> sprawdza
/// zbiór obecnych Id (usunięcie przez <see cref="FakeAppRemover"/> zdejmuje Id z listy).
/// </summary>
public sealed class FakeInstalledAppsReader : IInstalledAppsReader
{
    public List<InstalledApp> Apps { get; } = new();

    public List<string> Warnings { get; } = new();

    public List<string> ReadSids { get; } = new();

    public Exception? ReadThrows { get; set; }

    public InstalledAppsReading Read(string userSid)
    {
        ReadSids.Add(userSid);
        if (ReadThrows is not null)
        {
            throw ReadThrows;
        }

        return new InstalledAppsReading(Apps.ToList(), Warnings.ToList());
    }

    public AppPresence ReadPresence(InstalledApp app, string userSid, AppPresenceScope scope)
        => Apps.Any(candidate => string.Equals(candidate.Id, app.Id, StringComparison.Ordinal)) ? AppPresence.Present : AppPresence.Absent;

    public void Remove(string id) => Apps.RemoveAll(app => string.Equals(app.Id, id, StringComparison.Ordinal));
}
