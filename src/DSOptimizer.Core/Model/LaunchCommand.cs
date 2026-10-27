namespace DSOptimizer.Core.Model;

/// <summary>
/// Gotowe polecenie startu pozycji <c>launch</c>: zawsze CreateProcess pełnej ścieżki (System32, stała ścieżka PowerShella
/// albo <c>%SystemRoot%\explorer.exe</c> dla <c>shell:::{GUID}</c>), nigdy ShellExecute. Tworzy je <see cref="LaunchTarget.ToCommand"/>.
/// </summary>
public sealed record LaunchCommand
{
    public required string FileName { get; init; }

    public required string Arguments { get; init; }

    /// <summary>True dla <c>shell:::{GUID}</c>: start wolno wykonać tylko za <see cref="ShellSessionGuard"/>.</summary>
    public required bool RequiresUserShell { get; init; }
}
