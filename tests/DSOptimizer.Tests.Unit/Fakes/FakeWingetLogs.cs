using System;
using System.Collections.Generic;
using System.Globalization;
using DSOptimizer.Core.Abstractions;

namespace DSOptimizer.Tests.Unit.Fakes;

/// <summary>Ścieżki logu winget bez dysku: <c>C:\dso-logs\winget\&lt;id&gt;-&lt;n&gt;.log</c>, kolejne n od 1.</summary>
public sealed class FakeWingetLogs : IWingetLogs
{
    public List<(string WingetId, DateTimeOffset At)> Calls { get; } = new();

    public Exception? Throws { get; set; }

    public string NewLogPath(string wingetId, DateTimeOffset at)
    {
        if (Throws is not null)
        {
            throw Throws;
        }

        Calls.Add((wingetId, at));
        return @"C:\dso-logs\winget\" + wingetId + "-" + Calls.Count.ToString(CultureInfo.InvariantCulture) + ".log";
    }
}
