using System;
using System.ComponentModel;
using System.IO;
using DSOptimizer.Windows;
using Xunit;

namespace DSOptimizer.Tests.Windows;

/// <summary>
/// Linia poleceń dla CreateProcessW i błąd startu nieistniejącego pliku. Nic się nie uruchamia: jedyny prawdziwy start
/// celuje w ścieżkę w <c>%TEMP%\dso-tests</c>, której nie ma, więc CreateProcessW kończy się błędem.
/// </summary>
[Trait("Category", "Windows")]
public class NativeProcessStarterTests
{
    [Fact]
    public void CommandLine_QuotesFile_AppendsArgs()
    {
        Assert.Equal(
            "\"C:\\Windows\\System32\\control.exe\" \"C:\\Windows\\System32\\main.cpl\" @1",
            NativeProcessStarter.CommandLine(@"C:\Windows\System32\control.exe", "\"C:\\Windows\\System32\\main.cpl\" @1"));
    }

    [Fact]
    public void CommandLine_NoArgs_OnlyQuotedFile()
    {
        Assert.Equal("\"C:\\Windows\\System32\\mmc.exe\"", NativeProcessStarter.CommandLine(@"C:\Windows\System32\mmc.exe", string.Empty));
    }

    [Fact]
    public void Start_MissingFile_ThrowsWin32Exception()
    {
        var missing = Path.Combine(Path.GetTempPath(), "dso-tests", "brak-" + Guid.NewGuid().ToString("N"), "brak.exe");

        var ex = Assert.Throws<Win32Exception>(() => NativeProcessStarter.Start(missing, "/x", Environment.SystemDirectory));

        Assert.True(ex.NativeErrorCode is 2 or 3, $"Oczekiwano ERROR_FILE_NOT_FOUND albo ERROR_PATH_NOT_FOUND, jest {ex.NativeErrorCode}.");
        Assert.False(File.Exists(missing));
    }
}
