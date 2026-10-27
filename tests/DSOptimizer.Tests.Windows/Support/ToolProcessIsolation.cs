using Xunit;

namespace DSOptimizer.Tests.Windows.Support;

/// <summary>
/// Testy uruchamiające <c>ping.exe</c> przez runnery (limit czasu) idą po kolei: <c>System32ToolRunnerTests</c> sprawdza, że po limicie
/// nie żyje żaden świeży <c>ping</c> procesu testów, a <c>ServicingToolRunnerTests</c> i <c>WingetRunnerTests</c> celowo zostawiają swój
/// działający do sprzątnięcia w <c>finally</c>. Wszystkie liczą tylko dzieci procesu testów (<see cref="ChildPings"/>), więc <c>ping</c>
/// startowany przez skrypty <c>PowerShellScriptRunnerTests</c> (dziecko <c>powershell.exe</c>) ani inne suity im nie przeszkadza.
/// </summary>
[CollectionDefinition(Name)]
public sealed class ToolProcessIsolation
{
    public const string Name = "Procesy narzędzi (ping)";
}
