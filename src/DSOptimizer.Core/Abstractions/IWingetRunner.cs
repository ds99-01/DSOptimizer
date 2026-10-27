using System;
using System.Collections.Generic;

namespace DSOptimizer.Core.Abstractions;

/// <summary>
/// Uruchamia winget (alias <c>%LOCALAPPDATA%\Microsoft\WindowsApps\winget.exe</c> z profilu konta procesu) bez zabijania go i poza
/// Job Objectem. Po <c>waitLimit</c> przestaje czekać: <see cref="WingetRun.TimedOut"/> = true, <see cref="WingetRun.ExitCode"/> = null,
/// a proces (i instalator) żyje dalej. Brak <c>CancellationToken</c> jest celowy (jak <see cref="IServicingToolRunner"/>):
/// „Zatrzymaj” działa tylko między aplikacjami. Błąd startu procesu = <c>OperationException</c>.
/// </summary>
public interface IWingetRunner
{
    /// <param name="arguments">Linia argumentów z <c>WingetArguments</c>.</param>
    /// <param name="waitLimit">Jak długo najwyżej czekać na koniec procesu.</param>
    /// <param name="onLine">Wywoływane dla każdej linii wyjścia (postęp), może być <c>null</c>.</param>
    WingetRun Run(string arguments, TimeSpan waitLimit, Action<string>? onLine);
}

/// <summary>Wynik uruchomienia winget: kod wyjścia (<c>null</c> po limicie czasu), flaga limitu i zebrane linie.</summary>
public sealed record WingetRun(int? ExitCode, bool TimedOut, IReadOnlyList<string> Output);
