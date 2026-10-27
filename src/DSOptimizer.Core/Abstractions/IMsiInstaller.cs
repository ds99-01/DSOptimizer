using System;

namespace DSOptimizer.Core.Abstractions;

/// <summary>
/// Cicha instalacja pakietu MSI (<c>msiexec /i … /qn /norestart /l*v …</c>, argumenty z <c>MsiArguments</c>) bez zabijania i poza
/// Job Objectem. Po <paramref name="waitLimit"/> przestaje czekać: <see cref="MsiRun.TimedOut"/> = true, kod <c>null</c>, proces żyje.
/// Błąd startu procesu = wyjątek.
/// </summary>
public interface IMsiInstaller
{
    MsiRun Install(string msiPath, string logPath, TimeSpan waitLimit);
}

/// <summary>Wynik <c>msiexec</c>: kod wyjścia (<c>null</c> po limicie czasu) i flaga limitu.</summary>
public sealed record MsiRun(int? ExitCode, bool TimedOut);
