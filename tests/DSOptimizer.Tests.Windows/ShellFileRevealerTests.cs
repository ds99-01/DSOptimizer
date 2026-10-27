using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using DSOptimizer.Core.Abstractions;
using DSOptimizer.Core.Errors;
using DSOptimizer.Core.Model;
using DSOptimizer.Windows;
using Xunit;

namespace DSOptimizer.Tests.Windows;

/// <summary>
/// „Pokaż w Eksploratorze” bez uruchamiania czegokolwiek: start procesu podmieniony, sprawdzamy ProcessStartInfo
/// (<c>%SystemRoot%\explorer.exe /select,"ścieżka"</c>) i to, że odmowy (katalog programu, brak pliku, brak Eksploratora konta)
/// nie startują nic.
/// </summary>
[Trait("Category", "Windows")]
public class ShellFileRevealerTests
{
    private const int Session = 1;
    private const string Me = "S-1-5-21-1-2-3-1000";
    private const string Root = @"C:\ProgramData\DSOptimizer";
    private const string Target = @"E:\logs\PC\r.txt";

    private readonly List<ProcessStartInfo> _started = new();
    private readonly FakeExplorerQuery _explorers = new();

    public ShellFileRevealerTests()
    {
        _explorers.Processes.Add(new ExplorerProcessInfo(Session, Me, @"C:\Windows\Explorer.EXE"));
    }

    private ShellFileRevealer Revealer(Func<string, bool>? exists = null, Exception? failure = null, string machine = "MYPC")
        => new(
            info =>
            {
                _started.Add(info);
                if (failure is not null)
                {
                    throw failure;
                }

                return null;
            },
            _explorers,
            Session,
            Me,
            Root,
            exists ?? (_ => true),
            machine);

    private sealed class FakeExplorerQuery : IExplorerProcessQuery
    {
        public List<ExplorerProcessInfo> Processes { get; } = new();

        public Exception? Failure { get; set; }

        public int Calls { get; private set; }

        public IReadOnlyList<ExplorerProcessInfo> GetExplorerProcesses()
        {
            Calls++;
            if (Failure is not null)
            {
                throw Failure;
            }

            return Processes;
        }
    }

    [Fact]
    public void Reveal_FileOutsideRoot_WithOwnExplorer_StartsExplorerSelect()
    {
        Revealer().Reveal(Target);

        var info = Assert.Single(_started);
        Assert.Equal(ShellProcessLauncher.ExplorerPath, info.FileName);
        Assert.Equal("/select,\"" + Target + "\"", info.Arguments);
        Assert.False(info.UseShellExecute);
        Assert.False(info.ErrorDialog);
        Assert.Equal(Environment.SystemDirectory, info.WorkingDirectory);
    }

    [Theory]
    [InlineData(@"C:\ProgramData\DSOptimizer\reports\r.txt")]
    [InlineData(@"C:\ProgramData\DSOptimizer")]
    public void Reveal_PathUnderProtectedRoot_RefusesWithoutStarting(string path)
    {
        var ex = Assert.Throws<OperationException>(() => Revealer().Reveal(path));

        Assert.Equal("Ten plik leży w katalogu programu – skopiuj ścieżkę zamiast otwierać.", ex.Message);
        Assert.Empty(_started);
        Assert.Equal(0, _explorers.Calls);
    }

    [Fact]
    public void Reveal_MissingFile_RefusesWithoutStarting()
    {
        var ex = Assert.Throws<OperationException>(() => Revealer(exists: _ => false).Reveal(Target));

        Assert.Equal($"Plik nie istnieje: {Target}", ex.Message);
        Assert.Empty(_started);
        Assert.Equal(0, _explorers.Calls);
    }

    [Fact]
    public void Reveal_NoOwnExplorer_RefusesWithoutStarting()
    {
        _explorers.Processes.Clear();

        var ex = Assert.Throws<OperationException>(() => Revealer().Reveal(Target));

        Assert.Equal(ShellSessionGuard.RefusalMessage, ex.Message);
        Assert.Empty(_started);
    }

    [Fact]
    public void Reveal_ExplorerQueryFails_RefusesWithoutStarting()
    {
        _explorers.Failure = new InvalidOperationException("brak dostępu");

        var ex = Assert.Throws<OperationException>(() => Revealer().Reveal(Target));

        Assert.Contains("brak dostępu", ex.Message, StringComparison.Ordinal);
        Assert.Empty(_started);
    }

    [Theory]
    [InlineData("r.txt")]
    [InlineData(@"E:\a""b.txt")]
    public void Reveal_RelativeOrQuotedPath_Refuses(string path)
    {
        var ex = Assert.Throws<OperationException>(() => Revealer().Reveal(path));

        Assert.Equal($"Nieprawidłowa ścieżka pliku: {path}", ex.Message);

        Assert.Empty(_started);
        Assert.Equal(0, _explorers.Calls);
    }

    [Theory]
    [InlineData(@"\\MYPC\share\r.txt")]
    [InlineData(@"\\mypc\share\r.txt")]
    [InlineData(@"\\MYPC.local\share\r.txt")]
    public void Reveal_UncToOwnMachine_Refuses(string path)
    {
        Assert.Throws<OperationException>(() => Revealer().Reveal(path));

        Assert.Empty(_started);
        Assert.Equal(0, _explorers.Calls);
    }

    [Fact]
    public void Reveal_ForwardSlashPath_LaunchesNormalizedPath()
    {
        Revealer(exists: p => p == Target).Reveal("E:/logs/PC/r.txt");

        var info = Assert.Single(_started);
        Assert.Equal("/select,\"" + Target + "\"", info.Arguments);
    }

    [Fact]
    public void Reveal_UncToOtherMachine_Starts()
    {
        Revealer().Reveal(@"\\MYPC2\share\r.txt");

        Assert.Single(_started);
    }

    [Fact]
    public void Reveal_Win32Error_BecomesOperationException()
    {
        var ex = Assert.Throws<OperationException>(() => Revealer(failure: new Win32Exception("odmowa")).Reveal(Target));

        Assert.Contains("odmowa", ex.Message, StringComparison.Ordinal);
        Assert.IsType<Win32Exception>(ex.InnerException);
    }
}
