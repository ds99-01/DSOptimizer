using System;

namespace DSOptimizer.Core.Install;

/// <summary>Limity czekania na winget. Po limicie program przestaje czekać, a proces winget (i instalator) żyje dalej.</summary>
public static class InstallTimeouts
{
    /// <summary>Instalacja jednej aplikacji (spec 5.4, do korekty po pomiarze instalacji na VM).</summary>
    public static readonly TimeSpan PerApp = TimeSpan.FromMinutes(30);

    /// <summary><c>winget --version</c> w sprawdzeniu dostępności.</summary>
    public static readonly TimeSpan Version = TimeSpan.FromSeconds(30);

    /// <summary><c>winget search</c> (może pobierać źródło).</summary>
    public static readonly TimeSpan Search = TimeSpan.FromMinutes(2);

    /// <summary>Jak długo po sukcesie winget czekamy, aż program z regułą <c>match</c> pojawi się na liście zainstalowanych (Ruling R16, np. Discord).</summary>
    public static readonly TimeSpan Readback = TimeSpan.FromSeconds(60);

    /// <summary>Odstęp między odczytami listy zainstalowanych w oknie <see cref="Readback"/>.</summary>
    public static readonly TimeSpan ReadbackInterval = TimeSpan.FromSeconds(1);

    /// <summary>
    /// Najwięcej odczytów w oknie <see cref="Readback"/> (60 s / 1 s + 1 = 61). Pętla kończy się po tej liczbie także przy
    /// zamrożonym albo cofniętym zegarze systemowym, więc seria nie wisi pod blokadą <c>GuiSession</c>.
    /// </summary>
    public static readonly int ReadbackAttempts = (int)(Readback.Ticks / ReadbackInterval.Ticks) + 1;

    /// <summary><c>winget list --id … --exact</c> w odczycie zwrotnym pozycji z wyszukiwania.</summary>
    public static readonly TimeSpan List = TimeSpan.FromMinutes(2);
}
