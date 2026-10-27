using System;
using System.Collections.Generic;
using System.Linq;
using DSOptimizer.Core.Apps;
using Xunit;

namespace DSOptimizer.Tests.Unit.Apps;

public class UninstallCommandTests
{
    private const string System32 = @"C:\Windows\System32";
    private const string Guid = "{AC76BA86-7AD7-1033-7B44-AC0F074E4100}";

    private static Win32Info Info(
        string? uninstall = null,
        string? quiet = null,
        bool msi = false,
        string keyName = "Prog")
        => new(@"HKLM\SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall\" + keyName, uninstall, quiet, msi, null, null, false);

    private static (UninstallPlan? Plan, string? Reason) Plan(Win32Info info, params string[] existing)
        => UninstallCommand.Plan(info, path => existing.Contains(path, StringComparer.OrdinalIgnoreCase), System32);

    [Fact]
    public void QuotedPathWithArguments_IsParsed()
    {
        var (plan, reason) = Plan(
            Info(uninstall: "\"C:\\Program Files\\X\\unins000.exe\" /SILENT /NORESTART"),
            @"C:\Program Files\X\unins000.exe");

        Assert.Null(reason);
        Assert.Equal(@"C:\Program Files\X\unins000.exe", plan!.FileName);
        Assert.Equal("/SILENT /NORESTART", plan.Arguments);
        Assert.Equal(UninstallMode.Interactive, plan.Mode);
    }

    [Fact]
    public void UnquotedPathWithSpacesAndArguments_IsParsedByExistingFile()
    {
        var (plan, reason) = Plan(
            Info(uninstall: @"C:\Program Files\X\unins000.exe /SILENT"),
            @"C:\Program Files\X\unins000.exe");

        Assert.Null(reason);
        Assert.Equal(@"C:\Program Files\X\unins000.exe", plan!.FileName);
        Assert.Equal("/SILENT", plan.Arguments);
    }

    [Fact]
    public void UnquotedPathWithoutArguments_IsParsed()
    {
        var (plan, _) = Plan(Info(uninstall: @"C:\Program Files\X\uninstall.exe"), @"C:\Program Files\X\uninstall.exe");

        Assert.Equal(@"C:\Program Files\X\uninstall.exe", plan!.FileName);
        Assert.Equal(string.Empty, plan.Arguments);
    }

    [Fact]
    public void ExpandedEnvironmentPath_IsAccepted()
    {
        // Czytnik rejestru rozwija %ProgramFiles% (REG_EXPAND_SZ), parser dostaje gotową ścieżkę.
        var (plan, _) = Plan(Info(uninstall: "\"C:\\Program Files (x86)\\Y\\un.exe\" -q"), @"C:\Program Files (x86)\Y\un.exe");

        Assert.Equal(@"C:\Program Files (x86)\Y\un.exe", plan!.FileName);
    }

    [Fact]
    public void UnexpandedEnvironmentVariable_IsRefused()
    {
        var (plan, reason) = Plan(Info(uninstall: @"%ProgramFiles%\X\un.exe"), @"%ProgramFiles%\X\un.exe");

        Assert.Null(plan);
        Assert.Equal(AppsText.NoSafeCommand, reason);
    }

    [Theory]
    [InlineData("MsiExec.exe /I" + Guid)]
    [InlineData("MsiExec.exe /X" + Guid)]
    public void MsiUninstallString_BecomesQuietMsiexecWithProductCode(string uninstall)
    {
        var (plan, reason) = Plan(Info(uninstall: uninstall, msi: true, keyName: Guid), @"C:\Windows\System32\msiexec.exe");

        Assert.Null(reason);
        Assert.Equal(@"C:\Windows\System32\msiexec.exe", plan!.FileName);
        Assert.Equal("/x " + Guid + " /qn /norestart", plan.Arguments);
        Assert.Equal(UninstallMode.Msi, plan.Mode);
    }

    [Fact]
    public void MsiUninstallStringWithoutGuidKey_TakesProductCodeFromString()
    {
        var (plan, _) = Plan(Info(uninstall: "MsiExec.exe /I" + Guid, keyName: "Prog"), @"C:\Windows\System32\msiexec.exe");

        Assert.Equal("/x " + Guid + " /qn /norestart", plan!.Arguments);
        Assert.Equal(UninstallMode.Msi, plan.Mode);
    }

    [Fact]
    public void MsiWithoutMsiexecOnDisk_IsRefused()
    {
        var (plan, reason) = Plan(Info(uninstall: "MsiExec.exe /I" + Guid, msi: true, keyName: Guid));

        Assert.Null(plan);
        Assert.Equal(AppsText.NoSafeCommand, reason);
    }

    [Fact]
    public void BareMsiexecWithoutProductCode_IsRefused()
    {
        var (plan, _) = Plan(Info(uninstall: "msiexec.exe /?"), @"C:\Windows\System32\msiexec.exe");

        Assert.Null(plan);
    }

    [Fact]
    public void QuietUninstallString_WinsOverMsiAndUninstallString()
    {
        var (plan, _) = Plan(
            Info(uninstall: "\"C:\\X\\un.exe\"", quiet: "\"C:\\X\\un.exe\" /S", msi: true, keyName: Guid),
            @"C:\X\un.exe",
            @"C:\Windows\System32\msiexec.exe");

        Assert.Equal(@"C:\X\un.exe", plan!.FileName);
        Assert.Equal("/S", plan.Arguments);
        Assert.Equal(UninstallMode.Quiet, plan.Mode);
    }

    [Fact]
    public void BrokenQuietString_FallsBackToUninstallString()
    {
        var (plan, _) = Plan(
            Info(uninstall: "\"C:\\X\\un.exe\" /U", quiet: @"\\server\share\un.exe /S"),
            @"C:\X\un.exe");

        Assert.Equal(UninstallMode.Interactive, plan!.Mode);
        Assert.Equal("/U", plan.Arguments);
    }

    [Theory]
    [InlineData("msiexec /x " + Guid + " /qn")]
    [InlineData("msiexec.exe /i " + Guid + " /quiet")]
    [InlineData("msiexec.exe /I" + Guid)]
    [InlineData(@"""C:\Windows\System32\msiexec.exe"" /X" + Guid + " /qn")]
    [InlineData(@"""C:\Windows\SysWOW64\msiexec.exe"" /x " + Guid)]
    public void QuietMsiexec_IsNormalizedToUninstallOfProductCode(string quiet)
    {
        var (plan, _) = Plan(Info(quiet: quiet), @"C:\Windows\System32\msiexec.exe");

        Assert.Equal(@"C:\Windows\System32\msiexec.exe", plan!.FileName);
        Assert.Equal("/x " + Guid + " /qn /norestart", plan.Arguments);
        Assert.Equal(UninstallMode.Msi, plan.Mode);
    }

    [Fact]
    public void MsiexecWithoutProductCode_InQuietString_IsRefused()
    {
        var (plan, reason) = Plan(Info(quiet: @"msiexec /i C:\x.msi /qn"), @"C:\Windows\System32\msiexec.exe");

        Assert.Null(plan);
        Assert.Equal(AppsText.NoSafeCommand, reason);
    }

    [Fact]
    public void SysWow64Msiexec_InUninstallString_ResolvesToSystem32()
    {
        var (plan, _) = Plan(Info(uninstall: @"""C:\Windows\SysWOW64\msiexec.exe"" /I" + Guid), @"C:\Windows\System32\msiexec.exe");

        Assert.Equal(@"C:\Windows\System32\msiexec.exe", plan!.FileName);
        Assert.Equal("/x " + Guid + " /qn /norestart", plan.Arguments);
    }

    [Theory]
    [InlineData(@"C:\Windows\System32\cmd.exe /c del x")]
    [InlineData(@"""C:\Windows\System32\WindowsPowerShell\v1.0\powershell.exe"" -File x.ps1")]
    [InlineData(@"C:\Windows\SysWOW64\rundll32.exe x.dll,Y")]
    [InlineData(@"""C:\Windows\System32\WScript.EXE"" x.vbs")]
    [InlineData(@"""C:\Windows\System32\mshta.exe"" x.hta")]
    [InlineData(@"""C:\Tools\pwsh.exe"" -c x")]
    [InlineData(@"""C:\Windows\System32\reg.exe"" delete x")]
    [InlineData(@"""C:\Windows\explorer.exe"" x")]
    public void InterpretersAndShells_AreRefusedInEverySource(string command)
    {
        var existing = new[]
        {
            @"C:\Windows\System32\cmd.exe", @"C:\Windows\System32\WindowsPowerShell\v1.0\powershell.exe",
            @"C:\Windows\SysWOW64\rundll32.exe", @"C:\Windows\System32\WScript.EXE", @"C:\Windows\System32\mshta.exe",
            @"C:\Tools\pwsh.exe", @"C:\Windows\System32\reg.exe", @"C:\Windows\explorer.exe",
        };

        var viaUninstall = Plan(Info(uninstall: command), existing);
        var viaQuiet = Plan(Info(quiet: command), existing);

        Assert.Null(viaUninstall.Plan);
        Assert.Equal(AppsText.NoSafeCommand, viaUninstall.Reason);
        Assert.Null(viaQuiet.Plan);
    }

    [Fact]
    public void OnlyUninstallString_IsInteractive()
    {
        var (plan, _) = Plan(Info(uninstall: "\"C:\\X\\un.exe\""), @"C:\X\un.exe");

        Assert.Equal(UninstallMode.Interactive, plan!.Mode);
    }

    [Theory]
    [InlineData(@"\\server\share\un.exe /S")]
    [InlineData(@"un.exe /S")]
    [InlineData(@"..\un.exe")]
    [InlineData(@"C:\X\..\Y\un.exe")]
    [InlineData(@"C:\Nie\ma\un.exe /S")]
    [InlineData(@"rundll32.exe x.dll,Y")]
    [InlineData(@"C:\X\un.bat")]
    [InlineData("\"C:\\X\\un.exe")]
    [InlineData("")]
    [InlineData("   ")]
    public void UnsafeOrMissingCommand_HasNoPlanAndReason(string uninstall)
    {
        var (plan, reason) = Plan(Info(uninstall: uninstall), @"C:\X\un.exe", @"C:\X\un.bat", @"C:\X\..\Y\un.exe");

        Assert.Null(plan);
        Assert.Equal(AppsText.NoSafeCommand, reason);
    }

    [Fact]
    public void NoCommands_HasNoPlan()
    {
        var (plan, reason) = Plan(Info());

        Assert.Null(plan);
        Assert.Equal(AppsText.NoSafeCommand, reason);
    }
}
