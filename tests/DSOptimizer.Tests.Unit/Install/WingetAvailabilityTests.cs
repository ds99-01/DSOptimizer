using System;
using System.Collections.Generic;
using DSOptimizer.Core.Abstractions;
using DSOptimizer.Core.Apps;
using DSOptimizer.Core.Install;
using DSOptimizer.Tests.Unit.Fakes;
using Xunit;

namespace DSOptimizer.Tests.Unit.Install;

public class WingetAvailabilityTests
{
    internal const string UserSid = "S-1-5-21-1000";
    internal const string OtherSid = "S-1-5-21-500";

    internal static InstalledApp AppInstaller()
        => new(
            "appx:Microsoft.DesktopAppInstaller_8wekyb3d8bbwe",
            AppKind.Store,
            "Instalator aplikacji",
            "Microsoft Corporation",
            "1.26.510.0",
            AppScope.User,
            UserSid,
            new AppxInfo(
                "Microsoft.DesktopAppInstaller_1.26.510.0_x64__8wekyb3d8bbwe",
                "Microsoft.DesktopAppInstaller_8wekyb3d8bbwe",
                "Microsoft.DesktopAppInstaller",
                false,
                false,
                false,
                "Store",
                true),
            null);

    private static WingetStatus Check(
        FakeWingetRunner runner,
        IReadOnlyList<InstalledApp>? raw = null,
        string processSid = UserSid,
        bool certain = true,
        Func<string, string?>? accountName = null)
        => WingetAvailability.Check(
            new FakeUserContext { Sid = UserSid, IsCertain = certain },
            processSid,
            raw ?? new[] { AppInstaller() },
            runner,
            accountName ?? (sid => sid == UserSid ? "dso" : null));

    [Fact]
    public void Ready_VersionFromOutput()
    {
        var runner = new FakeWingetRunner().Enqueue(FakeWingetRunner.Exit(0, InstallSamples.Lines("spike-net-version.bin").ToArray()));

        var status = Check(runner);

        Assert.Equal(WingetState.Ready, status.State);
        Assert.Equal("v1.11.510", status.Version);
        var call = Assert.Single(runner.Calls);
        Assert.Equal(WingetArguments.Version(), call.Arguments);
        Assert.Equal(InstallTimeouts.Version, call.WaitLimit);
        Assert.Contains("v1.11.510", status.Text, StringComparison.Ordinal);
    }

    [Fact]
    public void OtherAccount_NoWingetCall()
    {
        var runner = new FakeWingetRunner();

        var status = Check(runner, processSid: OtherSid);

        Assert.Equal(WingetState.OtherAccount, status.State);
        Assert.Equal("Instalacja działa tylko na koncie zalogowanego użytkownika: uruchom DSOptimizer na koncie dso.", status.Text);
        Assert.Null(status.Version);
        Assert.Empty(runner.Calls);
    }

    [Fact]
    public void OtherAccount_UnknownName_UsesSid()
    {
        var runner = new FakeWingetRunner();

        var status = Check(runner, processSid: OtherSid, accountName: _ => throw new InvalidOperationException("brak"));

        Assert.Equal(
            "Instalacja działa tylko na koncie zalogowanego użytkownika: uruchom DSOptimizer na koncie " + UserSid + ".",
            status.Text);
        Assert.Equal(status.Text, Check(runner, processSid: OtherSid, accountName: _ => " ").Text);
        Assert.Empty(runner.Calls);
    }

    /// <summary>Ruling R17 (pomiar VM 4c): Administrator Protection = proces na ukrytym koncie ADMIN_&lt;nazwa&gt; z innym SID.</summary>
    [Fact]
    public void OtherAccount_AdministratorProtection_HiddenAccountNamed()
    {
        var runner = new FakeWingetRunner();

        var status = Check(runner, processSid: OtherSid, accountName: sid => sid == UserSid ? "dso" : sid == OtherSid ? "ADMIN_dso" : null);

        Assert.Equal(WingetState.OtherAccount, status.State);
        Assert.Equal(
            "Instalacja działa tylko na koncie zalogowanego użytkownika: uruchom DSOptimizer na koncie dso"
                + " (Administrator Protection: program podniesiony działa na ukrytym koncie ADMIN_dso).",
            status.Text);
        Assert.Empty(runner.Calls);
    }

    [Fact]
    public void OtherAccount_PlainAdministrator_NoAdministratorProtectionNote()
    {
        var runner = new FakeWingetRunner();

        var status = Check(runner, processSid: OtherSid, accountName: sid => sid == UserSid ? "dso" : "Administrator");

        Assert.Equal("Instalacja działa tylko na koncie zalogowanego użytkownika: uruchom DSOptimizer na koncie dso.", status.Text);
        Assert.Empty(runner.Calls);
    }

    [Fact]
    public void ProcessSid_ComparedCaseInsensitive()
    {
        var runner = new FakeWingetRunner().Enqueue(FakeWingetRunner.Exit(0, "v1.11.510"));

        Assert.Equal(WingetState.Ready, Check(runner, processSid: UserSid.ToLowerInvariant()).State);
    }

    [Fact]
    public void UserUncertain_Refused_BeforeAccountCheck()
    {
        var runner = new FakeWingetRunner();

        var status = Check(runner, processSid: OtherSid, certain: false);

        Assert.Equal(WingetState.UserUncertain, status.State);
        Assert.Equal("Nie udało się ustalić zalogowanego użytkownika.", status.Text);
        Assert.Empty(runner.Calls);
    }

    [Fact]
    public void NotRegistered_NoVersionCall()
    {
        var runner = new FakeWingetRunner();
        var other = AppInstaller() with
        {
            Id = "appx:Microsoft.WindowsCalculator_8wekyb3d8bbwe",
            Appx = AppInstaller().Appx! with { PackageName = "Microsoft.WindowsCalculator" },
        };
        var program = AppInstaller() with { Kind = AppKind.Program, Appx = null, Name = "Microsoft.DesktopAppInstaller" };

        var status = Check(runner, raw: new[] { other, program });

        Assert.Equal(WingetState.NotRegistered, status.State);
        Assert.Contains("Napraw winget", status.Text, StringComparison.Ordinal);
        Assert.Empty(runner.Calls);
    }

    [Theory]
    [InlineData(1, "v1.11.510", "0x00000001")]
    [InlineData(0, "brak wersji", "brak numeru wersji")]
    public void VersionFails_NotWorking(int code, string output, string expected)
    {
        var runner = new FakeWingetRunner().Enqueue(FakeWingetRunner.Exit(code, output));

        var status = Check(runner);

        Assert.Equal(WingetState.NotWorking, status.State);
        Assert.Null(status.Version);
        Assert.Contains(expected, status.Text, StringComparison.Ordinal);
        Assert.Contains("Napraw winget", status.Text, StringComparison.Ordinal);
    }

    [Fact]
    public void VersionTimedOut_NotWorking()
    {
        var runner = new FakeWingetRunner().Enqueue(FakeWingetRunner.TimedOut());

        var status = Check(runner);

        Assert.Equal(WingetState.NotWorking, status.State);
        Assert.Contains("nie odpowiedział", status.Text, StringComparison.Ordinal);
    }

    [Fact]
    public void VersionThrows_NotWorking()
    {
        var runner = new FakeWingetRunner { Default = _ => throw new InvalidOperationException("brak aliasu") };

        var status = Check(runner);

        Assert.Equal(WingetState.NotWorking, status.State);
        Assert.Contains("brak aliasu", status.Text, StringComparison.Ordinal);
    }

    [Fact]
    public void NullArguments_Throw()
    {
        var runner = new FakeWingetRunner();
        var user = new FakeUserContext();
        var raw = Array.Empty<InstalledApp>();
        Func<string, string?> names = _ => null;

        Assert.Throws<ArgumentNullException>(() => WingetAvailability.Check(null!, UserSid, raw, runner, names));
        Assert.Throws<ArgumentNullException>(() => WingetAvailability.Check(user, null!, raw, runner, names));
        Assert.Throws<ArgumentNullException>(() => WingetAvailability.Check(user, UserSid, null!, runner, names));
        Assert.Throws<ArgumentNullException>(() => WingetAvailability.Check(user, UserSid, raw, null!, names));
        Assert.Throws<ArgumentNullException>(() => WingetAvailability.Check(user, UserSid, raw, runner, null!));
    }
}
