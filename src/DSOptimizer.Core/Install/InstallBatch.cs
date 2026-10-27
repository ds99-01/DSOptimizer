using System.Collections.Generic;
using DSOptimizer.Core.Engine;

namespace DSOptimizer.Core.Install;

/// <summary>
/// Wynik jednej pozycji serii instalacji (raport CLI i okno serii): kod winget (<c>null</c>, gdy winget nie zakończył się albo nie
/// ruszył), komunikat wyniku, ścieżka logu winget (<c>--log</c>; przy GitHubie log MSI), restart i wersja z linii „Found … Version …”
/// (przy GitHubie tag wydania). Pozycja z GitHuba: <see cref="Origin"/>, repozytorium i kod <c>msiexec</c> (<c>null</c>, gdy nie ruszył).
/// </summary>
public sealed record InstallItemReport(
    string Id,
    string? WingetId,
    string Title,
    TweakOutcome Outcome,
    int? WingetExitCode,
    string Message,
    string? LogPath,
    bool RequiresReboot,
    string? Version,
    InstallOrigin Origin = InstallOrigin.Winget,
    string? GitHub = null,
    int? MsiExitCode = null);

/// <summary>Wynik serii instalacji: wspólny <see cref="BatchResult"/>, raport pozycji w tej samej kolejności i stan winget z początku serii.</summary>
public sealed record InstallBatch(BatchResult Batch, IReadOnlyList<InstallItemReport> Items, WingetStatus Winget);
