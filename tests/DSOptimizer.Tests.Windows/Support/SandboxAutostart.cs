using DSOptimizer.Core.Autostart;

namespace DSOptimizer.Tests.Windows.Support;

/// <summary>
/// Miejsca autostartu w piaskownicy rejestru: gałąź konta <c>U</c>, maszyny <c>M</c> i klas <c>C</c> pod kluczem testu, foldery nieużywane.
/// Silnik zapisuje i cofa tylko w miejscach pozycji z kontekstu, więc testy z przełącznikiem w piaskownicy podają je jawnie.
/// </summary>
internal static class SandboxAutostart
{
    public static AutostartLocations Locations(RegistrySandbox sandbox)
        => new(sandbox.HkcuPath + @"\U", sandbox.HkcuPath + @"\M", sandbox.HkcuPath + @"\C", @"C:\unused", @"C:\unused");
}
