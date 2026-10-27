using System;
using System.Collections.Generic;

namespace DSOptimizer.Core.Abstractions;

/// <summary>
/// Uruchamia narzędzie serwisowania (DISM, SFC, cleanmgr) bez zabijania go. Po <c>waitLimit</c> przestaje czekać:
/// <see cref="ServicingToolRun.TimedOut"/> = true, <see cref="ServicingToolRun.ExitCode"/> = null, a proces żyje dalej.
/// Brak <c>CancellationToken</c> jest celowy – „Zatrzymaj” działa tylko między krokami.
/// </summary>
public interface IServicingToolRunner
{
    /// <param name="request">Narzędzie z System32.</param>
    /// <param name="waitLimit">Jak długo najwyżej czekać na koniec procesu.</param>
    /// <param name="onLine">Wywoływane dla każdej linii wyjścia (postęp), może być <c>null</c>.</param>
    ServicingToolRun Run(ToolRequest request, TimeSpan waitLimit, Action<string>? onLine);
}

/// <summary>Wynik uruchomienia: kod wyjścia (<c>null</c> po limicie czasu), flaga limitu i zebrane linie.</summary>
public sealed record ServicingToolRun(int? ExitCode, bool TimedOut, IReadOnlyList<string> Output);
