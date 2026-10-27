using System;
using System.Runtime.CompilerServices;
using DSOptimizer.Windows;

namespace DSOptimizer.Tests.Windows.Support;

/// <summary>
/// Bezpieczniki całego procesu testów, ustawiane przy ładowaniu modułu (przed pierwszym testem): <c>DSO_BLOCK_REAL_WINGET=1</c>
/// sprawia, że <c>WingetRunner</c> odmawia startu pliku <c>winget.exe</c>, więc żadna ścieżka testów (CliHost, AppComposition)
/// nie dojdzie do prawdziwego winget na hoście. <c>DSO_BLOCK_REAL_NETWORK=1</c>: <c>WebDownloader</c> przyjmuje tylko adresy pętli
/// zwrotnej (także na skoku przekierowania), więc testy nigdy nie łączą się z internetem. <c>DSO_BLOCK_REAL_MSIEXEC=1</c>:
/// <c>MsiexecInstaller</c> odmawia startu pliku <c>msiexec.exe</c>. Zmienne przechodzą też do procesów potomnych testów.
/// </summary>
internal static class TestGuards
{
    public const string BlockRealWinget = WingetRunner.BlockRealWingetVariable;
    public const string BlockRealNetwork = WebDownloader.BlockRealNetworkVariable;
    public const string BlockRealMsiexec = MsiexecInstaller.BlockRealMsiexecVariable;

#pragma warning disable CA2255 // Inicjalizator modułu w zestawie testów jest tu celowy: bezpiecznik ma działać przed każdym testem.
    [ModuleInitializer]
#pragma warning restore CA2255
    internal static void Install()
    {
        Environment.SetEnvironmentVariable(BlockRealWinget, "1");
        Environment.SetEnvironmentVariable(BlockRealNetwork, "1");
        Environment.SetEnvironmentVariable(BlockRealMsiexec, "1");
    }
}
