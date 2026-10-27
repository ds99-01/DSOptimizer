using System;

namespace DSOptimizer.Core.Repair;

/// <summary>Ścieżki napraw: katalog Windows i katalog kopii (tworzony leniwie, przy pierwszym wywołaniu, nie przy starcie programu).</summary>
public sealed class RepairPaths
{
    /// <summary><c>%SystemRoot%</c>, np. <c>C:\Windows</c>.</summary>
    public required string SystemRoot { get; init; }

    /// <summary>Zwraca <c>&lt;SecureRoot&gt;\backups</c> (np. <c>%ProgramData%\DSOptimizer\backups</c>), tworząc go z zaufanym ACL; błąd = wyjątek.</summary>
    public required Func<string> EnsureBackupDirectory { get; init; }
}
