using System.Collections.Generic;

namespace DSOptimizer.Core.Abstractions;

/// <summary>Zapytanie o działające procesy <c>explorer.exe</c> (strażnik powłoki dla <c>shell:::{GUID}</c>).</summary>
public interface IExplorerProcessQuery
{
    /// <summary>
    /// Wszystkie <c>explorer.exe</c> w systemie z sesją i SID właściciela; procesu, którego właściciela nie da się ustalić, nie zwraca.
    /// Błąd zapytania: wyjątek (launcher traktuje go jako odmowę).
    /// </summary>
    IReadOnlyList<ExplorerProcessInfo> GetExplorerProcesses();
}
