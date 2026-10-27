using System;

namespace DSOptimizer.Core.Abstractions;

/// <summary>Ścieżki plików logu winget (<c>--log</c>) w folderze logów programu.</summary>
public interface IWingetLogs
{
    /// <summary>Nowa ścieżka logu dla instalacji pakietu <paramref name="wingetId"/> rozpoczętej w chwili <paramref name="at"/>.</summary>
    string NewLogPath(string wingetId, DateTimeOffset at);
}
