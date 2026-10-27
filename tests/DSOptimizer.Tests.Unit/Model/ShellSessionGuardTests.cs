using System;
using System.Collections.Generic;
using DSOptimizer.Core.Abstractions;
using DSOptimizer.Core.Model;
using Xunit;

namespace DSOptimizer.Tests.Unit.Model;

/// <summary>R23: podniesiony Eksplorer startuje tylko, gdy w sesji procesu działa już Eksplorer tego samego konta.</summary>
public class ShellSessionGuardTests
{
    private const string Me = "S-1-5-21-1-2-3-1000";
    private const string Other = "S-1-5-21-1-2-3-500";

    private const string ShellLine = @"C:\Windows\Explorer.EXE";
    private const string FactoryLine = @"C:\Windows\explorer.exe /factory,{5BD95610-9434-43C2-886C-57852CC8A120} -Embedding";

    private static ExplorerProcessInfo Explorer(int session, string sid, string? commandLine = ShellLine) => new(session, sid, commandLine);

    [Fact]
    public void HasOwnExplorer_SameSessionAndSid_IsTrue()
        => Assert.True(ShellSessionGuard.HasOwnExplorer(new[] { Explorer(1, Me) }, 1, Me));

    [Fact]
    public void HasOwnExplorer_SidComparisonIgnoresCase()
        => Assert.True(ShellSessionGuard.HasOwnExplorer(new[] { Explorer(1, "s-1-5-21-1-2-3-1000") }, 1, Me));

    [Fact]
    public void HasOwnExplorer_OneMatchAmongOthers_IsTrue()
        => Assert.True(ShellSessionGuard.HasOwnExplorer(new[] { Explorer(0, Me), Explorer(1, Other), Explorer(2, Me), Explorer(1, Me) }, 1, Me));

    [Fact]
    public void HasOwnExplorer_NoProcesses_IsFalse()
        => Assert.False(ShellSessionGuard.HasOwnExplorer(Array.Empty<ExplorerProcessInfo>(), 1, Me));

    [Fact]
    public void HasOwnExplorer_OtherSession_IsFalse()
        => Assert.False(ShellSessionGuard.HasOwnExplorer(new[] { Explorer(2, Me) }, 1, Me));

    [Fact]
    public void HasOwnExplorer_OtherAccount_IsFalse()
        => Assert.False(ShellSessionGuard.HasOwnExplorer(new[] { Explorer(1, Other) }, 1, Me));

    [Fact]
    public void HasOwnExplorer_EmptyOwnerSid_IsFalse()
        => Assert.False(ShellSessionGuard.HasOwnExplorer(new[] { Explorer(1, string.Empty) }, 1, string.Empty));

    [Fact]
    public void HasOwnExplorer_OnlyFactoryProcesses_IsFalse()
        => Assert.False(ShellSessionGuard.HasOwnExplorer(new[] { Explorer(1, Me, FactoryLine), Explorer(1, Me, FactoryLine.ToUpperInvariant()) }, 1, Me));

    [Fact]
    public void HasOwnExplorer_ShellPlusFactory_IsTrue()
        => Assert.True(ShellSessionGuard.HasOwnExplorer(new[] { Explorer(1, Me, FactoryLine), Explorer(1, Me, ShellLine) }, 1, Me));

    [Fact]
    public void HasOwnExplorer_FactoryOfOtherAccountDoesNotHelpShellOfOther_IsFalse()
        => Assert.False(ShellSessionGuard.HasOwnExplorer(new[] { Explorer(1, Me, FactoryLine), Explorer(1, Other, ShellLine) }, 1, Me));

    [Fact]
    public void HasOwnExplorer_UnknownCommandLine_IsNotAShell()
    {
        // Zachowawczo: bez linii poleceń nie da się odróżnić powłoki od /factory, więc odmowa (WMI zwraca CommandLine explorera administratorowi).
        Assert.False(ShellSessionGuard.HasOwnExplorer(new[] { Explorer(1, Me, null) }, 1, Me));
        Assert.False(ShellSessionGuard.HasOwnExplorer(new[] { Explorer(1, Me, string.Empty) }, 1, Me));
    }

    [Fact]
    public void HasOwnExplorer_NullProcesses_Throws()
        => Assert.Throws<ArgumentNullException>(() => ShellSessionGuard.HasOwnExplorer((IEnumerable<ExplorerProcessInfo>)null!, 1, Me));

    [Fact]
    public void RefusalMessage_ExplainsWhyNothingStarted()
        => Assert.Equal("Brak Eksploratora tego konta w bieżącej sesji – nie uruchamiam podniesionego Eksploratora.", ShellSessionGuard.RefusalMessage);
}
