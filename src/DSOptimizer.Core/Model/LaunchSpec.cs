namespace DSOptimizer.Core.Model;

/// <summary>
/// Deklaratywne uruchomienie programu systemowego (panel, launcher) przez CreateProcess poza Job Objectem skryptów.
/// Dozwolone pliki opisuje <see cref="LaunchTarget"/> (spec, sekcja 4, A6).
/// </summary>
public sealed record LaunchSpec
{
    /// <summary>Sama nazwa pliku *.exe|*.cpl|*.msc z System32 albo <c>shell:::{GUID}</c>.</summary>
    public required string File { get; init; }

    /// <summary>Argumenty jednym łańcuchem (przekazywane do procesu bez zmian).</summary>
    public string Arguments { get; init; } = string.Empty;
}
