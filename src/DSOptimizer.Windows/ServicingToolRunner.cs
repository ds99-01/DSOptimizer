using System;
using System.IO;
using DSOptimizer.Core.Abstractions;

namespace DSOptimizer.Windows;

/// <summary>
/// Długie narzędzia serwisowania (DISM, SFC, cleanmgr) z <c>%SystemRoot%\System32</c> przez <c>CreateProcessW</c> pełnej ścieżki
/// (wzorzec <see cref="System32ToolRunner"/>: stdout i stderr jednym potokiem, stdin = NUL, dziedziczone tylko te dwa uchwyty),
/// ale **bez Job Objectu i bez zabijania** (wzorzec <c>UninstallerRunner</c>): po <c>waitLimit</c> runner od razu zwraca
/// <c>TimedOut=true</c> i <c>ExitCode=null</c>, a narzędzie działa dalej (przeżywa też zamknięcie DSOptimizera). Przebieg, potok
/// opróżniany w tle do EOF bez zamykania (Ruling R16) i zbieranie linii są w <see cref="UnkilledProcess"/>; tu zostaje katalog
/// System32 (ścieżka narzędzia i katalog roboczy) oraz dekodowanie stroną OEM (UTF-16LE SFC wykrywane z pierwszych bajtów).
/// Linie idą do <c>onLine</c> (na wątku czytającym) i do <see cref="ServicingToolRun.Output"/> – tylko do powrotu z <see cref="Run"/>.
/// Wyjątek z <c>onLine</c> trafia do wywołującego.
/// </summary>
public sealed class ServicingToolRunner : IServicingToolRunner
{
    public ServicingToolRun Run(ToolRequest request, TimeSpan waitLimit, Action<string>? onLine)
    {
        if (request is null)
        {
            throw new ArgumentNullException(nameof(request));
        }

        var systemDirectory = Environment.SystemDirectory;
        return UnkilledProcess.Run(
            Path.Combine(systemDirectory, request.FileName),
            request,
            systemDirectory,
            RedirectedProcess.OemEncoding(),
            waitLimit,
            onLine);
    }
}
