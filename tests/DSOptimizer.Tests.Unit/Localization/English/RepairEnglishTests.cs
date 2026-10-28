using System;
using System.IO;
using System.Linq;
using System.Threading;
using DSOptimizer.Core.Abstractions;
using DSOptimizer.Core.Engine;
using DSOptimizer.Core.Errors;
using DSOptimizer.Core.Gui;
using DSOptimizer.Core.Localization;
using DSOptimizer.Core.Model;
using DSOptimizer.Core.Repair;
using DSOptimizer.Core.Tools;
using DSOptimizer.Tests.Unit.Engine;
using DSOptimizer.Tests.Unit.Fakes;
using DSOptimizer.Tests.Unit.Support;
using Newtonsoft.Json.Linq;
using Xunit;

namespace DSOptimizer.Tests.Unit.Localization.English;

/// <summary>Teksty napraw (Repair) i narzędzi (Tools) po angielsku: całe zdania, rozmiary z kropką dziesiętną, nazwy kroków.</summary>
public class RepairEnglishTests
{
    private const string Win = @"C:\Windows";
    private const string PolicyKey = @"HKLM\SOFTWARE\Policies\Microsoft\Windows\WindowsUpdate";
    private const long Mb = 1024L * 1024;
    private const long Gb = 1024L * Mb;

    /// <summary>Zależności napraw na fake'ach (nic nie dotyka hosta).</summary>
    private sealed class Rig
    {
        public FakeServiceManager Services { get; } = new();

        public FakeRegistry Registry { get; } = new();

        public FakeToolRunner Tools { get; } = new();

        public FakeRepairFileSystem Files { get; } = new();

        public FakeDomainInfo Domain { get; } = new();

        public FakeServicingToolRunner LongTools { get; } = new();

        public FakeDiskSpace Disk { get; } = new();

        public FakeServicingActivity Servicing { get; } = new();

        public FakeScriptRunner Scripts { get; } = new();

        public FakeUserContext User { get; } = new() { Sid = "S-1-5-21-1000", IsCertain = true };

        public FakeClock Clock { get; } = new() { UtcNow = new DateTimeOffset(2026, 10, 22, 14, 30, 5, TimeSpan.Zero) };

        public ListLog Log { get; } = new();

        public ListProgress Output { get; } = new();

        public ListProgress<ActionProgress> Progress { get; } = new();

        public RepairServices Build() => new()
        {
            Services = Services,
            Registry = Registry,
            Tools = Tools,
            Files = Files,
            Domain = Domain,
            Clock = Clock,
            Log = Log,
            LongTools = LongTools,
            DiskSpace = Disk,
            Servicing = Servicing,
            Scripts = Scripts,
            User = User,
            TimeZone = TimeZoneInfo.Utc,
            Paths = new RepairPaths { SystemRoot = Win, EnsureBackupDirectory = () => @"C:\ProgramData\DSOptimizer\backups" },
        };

        public ActionHandlerResult Run(IActionHandler handler, string name, CancellationToken cancellationToken = default)
            => handler.Run(TestTweaks.Action() with { Script = null, Handler = name }, Output, Progress, cancellationToken);
    }

    private static string[] Lines(ActionHandlerResult result) => (result.Message ?? string.Empty).Split(new[] { Environment.NewLine }, StringSplitOptions.None);

    // ---- reset Windows Update ----

    private static Rig WuRig()
    {
        var rig = new Rig();
        rig.Services.Add("wuauserv", ServiceStartType.Manual, running: true);
        rig.Services.Add("bits", ServiceStartType.AutomaticDelayed, running: true);
        rig.Services.Add("cryptsvc", ServiceStartType.Automatic, running: true);
        rig.Services.Add("UsoSvc", ServiceStartType.AutomaticDelayed, running: true);
        rig.Services.Add("AppIDSvc", ServiceStartType.Manual, running: true);
        rig.Services.Add("msiserver", ServiceStartType.Manual, running: false);
        rig.Services.Add("WaaSMedicSvc", ServiceStartType.Manual, running: false);
        rig.Services.Add("DoSvc", ServiceStartType.Manual, running: false);
        rig.Services.SetDependents("cryptsvc", "AppIDSvc");
        rig.Files.AddDirectory(Win + @"\SoftwareDistribution");
        rig.Files.AddDirectory(Win + @"\System32\catroot2");
        return rig;
    }

    [Fact]
    public void WuReset_StepNames_English()
    {
        using (L.Use(UiLanguage.English))
        {
            var rig = WuRig();

            var result = rig.Run(new WindowsUpdateResetHandler(false, rig.Build()), ActionHandlerNames.WuReset);

            Assert.Equal(TweakOutcome.Executed, result.Outcome);
            Assert.Equal(
                new[]
                {
                    "Windows Update components were reset.",
                    "Stopped service wuauserv.",
                    "Stopped service bits.",
                    "Stopped service cryptsvc together with its dependent services: AppIDSvc.",
                    @"Moved C:\Windows\SoftwareDistribution → C:\Windows\SoftwareDistribution.dso-20261022-143005.",
                    @"Moved C:\Windows\System32\catroot2 → C:\Windows\System32\catroot2.dso-20261022-143005.",
                    "Restarted: cryptsvc, AppIDSvc, bits, wuauserv.",
                    "Verification: services are running as before the reset, folder copies exist.",
                    "Update history in Settings will be cleared (only Microsoft Defender definition entries may stay); updates will download again.",
                },
                Lines(result));
        }
    }

    [Fact]
    public void WuReset_DomainRefused_English()
    {
        using (L.Use(UiLanguage.English))
        {
            var rig = WuRig();
            rig.Domain.Value = true;
            var joined = rig.Run(new WindowsUpdateResetHandler(true, rig.Build()), ActionHandlerNames.WuResetFull);
            rig.Domain.Value = null;
            var unknown = rig.Run(new WindowsUpdateResetHandler(true, rig.Build()), ActionHandlerNames.WuResetFull);

            Assert.Equal(TweakOutcome.Refused, joined.Outcome);
            Assert.Equal("The computer is joined to a domain: Windows Update policies are set by the domain administrator.", joined.Message);
            Assert.Equal("Couldn't check whether the computer is joined to a domain.", unknown.Message);
        }
    }

    [Fact]
    public void WuReset_StopFailed_WholeSentenceEnglish()
    {
        using (L.Use(UiLanguage.English))
        {
            var rig = WuRig();
            rig.Services.FailStop("bits");

            var result = rig.Run(new WindowsUpdateResetHandler(false, rig.Build()), ActionHandlerNames.WuReset);

            Assert.Equal(TweakOutcome.Failed, result.Outcome);
            // Środek zdania to komunikat fake'a (FakeServiceManager.StopService) – deterministyczny.
            Assert.Equal("Couldn't stop service bits: Zatrzymanie bits nie powiodło się (fake). Nothing was changed.", Lines(result)[0]);
        }
    }

    [Fact]
    public void WuReset_CanceledBeforeChanges_WholeSentenceEnglish()
    {
        using (L.Use(UiLanguage.English))
        {
            var rig = WuRig();
            using var cancel = new CancellationTokenSource();
            cancel.Cancel();

            var result = rig.Run(new WindowsUpdateResetHandler(false, rig.Build()), ActionHandlerNames.WuReset, cancel.Token);

            Assert.Equal(TweakOutcome.Failed, result.Outcome);
            Assert.Equal("Canceled before any changes. Nothing was changed.", result.Message);
        }
    }

    [Fact]
    public void WuReset_VerificationFailed_WholeSentenceEnglish()
    {
        using (L.Use(UiLanguage.English))
        {
            var rig = WuRig();
            const string catrootCopy = Win + @"\System32\catroot2.dso-20261022-143005";
            rig.Files.AfterMove = (_, destination) =>
            {
                if (destination == catrootCopy)
                {
                    rig.Files.RemoveDirectory(destination);
                }
            };

            var result = rig.Run(new WindowsUpdateResetHandler(false, rig.Build()), ActionHandlerNames.WuReset);

            Assert.Equal(TweakOutcome.Failed, result.Outcome);
            Assert.Equal($"Verification didn't confirm the reset: copy {catrootCopy} is missing.", Lines(result)[0]);
        }
    }

    [Fact]
    public void WuReset_ServiceReadFailed_WholeSentenceEnglish()
    {
        using (L.Use(UiLanguage.English))
        {
            var rig = WuRig();
            rig.Services.DenyQuery("bits");

            var result = rig.Run(new WindowsUpdateResetHandler(false, rig.Build()), ActionHandlerNames.WuReset);

            Assert.Equal(TweakOutcome.Failed, result.Outcome);
            // „Odmowa dostępu (fake).” to błąd z FakeServiceManager.Query.
            Assert.Equal("Couldn't read service bits: Odmowa dostępu (fake). Nothing was changed.", result.Message);
        }
    }

    [Fact]
    public void WuResetFull_PolicyCheckFailed_WholeSentenceEnglish()
    {
        using (L.Use(UiLanguage.English))
        {
            var rig = WuRig();
            rig.Registry.DenyKey(PolicyKey);

            var result = rig.Run(new WindowsUpdateResetHandler(true, rig.Build()), ActionHandlerNames.WuResetFull);

            Assert.Equal(TweakOutcome.Failed, result.Outcome);
            Assert.Equal(
                $"Couldn't check the Windows Update policies ({PolicyKey}: AccessDenied); the policies stay unchanged. Nothing was changed.",
                result.Message);
        }
    }

    [Fact]
    public void WuResetFull_PolicyBackupFailed_WholeSentenceEnglish()
    {
        using (L.Use(UiLanguage.English))
        {
            var rig = WuRig();
            rig.Registry.Set(PolicyKey, "WUServer", RegValue.FromString("http://dso-wsus.invalid:8530"));
            rig.Tools.OnRun = _ => new ToolResult { ExitCode = 1, Output = new[] { "ERROR" } };

            var result = rig.Run(new WindowsUpdateResetHandler(true, rig.Build()), ActionHandlerNames.WuResetFull);

            Assert.Equal(TweakOutcome.Failed, result.Outcome);
            Assert.Equal(
                "Couldn't save a backup of the Windows Update policies (reg export ended with code 1: ERROR); the policies stay unchanged. Nothing was changed.",
                result.Message);
        }
    }

    // ---- czyszczenie dysku ----

    private static Rig CleanupRig()
    {
        const string profile = @"C:\Users\dso";
        var rig = new Rig();
        var sysTemp = Win + @"\Temp";
        var userTemp = profile + @"\AppData\Local\Temp";
        var download = Win + @"\SoftwareDistribution\Download";
        var explorer = profile + @"\AppData\Local\Microsoft\Windows\Explorer";
        foreach (var dir in new[] { sysTemp, userTemp, download, explorer })
        {
            rig.Files.AddDirectory(dir);
        }

        rig.Files.OldFiles[sysTemp] = _ => new OldFilesCleanup(2, 1, 1, 3 * Mb);
        rig.Files.OldFiles[userTemp] = _ => new OldFilesCleanup(4, 0, 0, 20 * Mb);
        rig.Files.OldFiles[download] = _ => new OldFilesCleanup(8, 0, 0, 255 * Mb);
        rig.Files.NamedFiles[explorer] = () => new OldFilesCleanup(15, 0, 0, 512 * 1024);
        rig.Registry.Set(DiskCleanupHandler.ProfileListKey + @"\S-1-5-21-1000", "ProfileImagePath", RegValue.FromExpandString(profile));
        rig.Services.Add("wuauserv", ServiceStartType.Manual, running: true);
        rig.Services.Add("bits", ServiceStartType.AutomaticDelayed, running: true);
        rig.Scripts.On(DiskCleanupScripts.DeliveryOptimization, 0, DiskCleanupScripts.DoneMarker);
        return rig;
    }

    [Fact]
    public void DiskCleanup_FreedSpace_English()
    {
        using (L.Use(UiLanguage.English))
        {
            var rig = CleanupRig();
            rig.Disk.Readings.Enqueue(10 * Gb);
            rig.Disk.Readings.Enqueue((11 * Gb) + (Gb / 2));

            var result = rig.Run(new DiskCleanupHandler(rig.Build()), ActionHandlerNames.DiskCleanup);

            Assert.Equal(TweakOutcome.Executed, result.Outcome);
            Assert.Equal(
                new[]
                {
                    "Freed: 1.5 GB.",
                    "System temporary files: done (files: 2, 3 MB; kept, newer than 24 h: 1; kept, in use: 1).",
                    "Account temporary files: done (files: 4, 20 MB).",
                    "Delivery Optimization: done – the command finished without an error (files being downloaded right now stay).",
                    "Downloaded updates: done (files: 8, 255 MB); services were restarted: bits, wuauserv.",
                    "Component store: done (DISM /StartComponentCleanup, code 0).",
                    "Account thumbnails: done (files: 15, less than 1 MB).",
                },
                Lines(result));
        }
    }

    [Fact]
    public void DiskCleanup_SpaceDropped_English()
    {
        using (L.Use(UiLanguage.English))
        {
            var rig = CleanupRig();
            rig.Disk.Readings.Enqueue(10 * Gb);
            rig.Disk.Readings.Enqueue(9 * Gb);

            var result = rig.Run(new DiskCleanupHandler(rig.Build()), ActionHandlerNames.DiskCleanup);

            Assert.Equal("Freed: the freed space can't be reported honestly (other programs were writing to the disk).", Lines(result)[0]);
            Assert.Equal("couldn't read the free space", DiskSpaceText.Freed(null, 9 * Gb));
        }
    }

    [Fact]
    public void DiskCleanup_Sizes_DecimalPointInEnglish_CommaInPolish()
    {
        using (L.Use(UiLanguage.English))
        {
            Assert.Equal("1.5 GB", DiskSpaceText.Size((Gb * 3) / 2));
            Assert.Equal("less than 1 MB", DiskSpaceText.Size(1000));
        }

        using (L.Use(UiLanguage.Polish))
        {
            Assert.Equal("1,5 GB", DiskSpaceText.Size((Gb * 3) / 2));
        }
    }

    [Fact]
    public void DiskCleanup_RestartFailed_WholeSentenceEnglish()
    {
        using (L.Use(UiLanguage.English))
        {
            var rig = CleanupRig();
            rig.Services.FailStart("bits");

            var result = rig.Run(new DiskCleanupHandler(rig.Build()), ActionHandlerNames.DiskCleanup);

            Assert.Equal(TweakOutcome.Failed, result.Outcome);
            Assert.Contains(
                "Downloaded updates: error – deleted files (files: 8, 255 MB), but couldn't restart services: bits; restarted: wuauserv.",
                Lines(result));
        }
    }

    [Fact]
    public void DiskCleanup_SkippedThenRestartFailed_WholeSentenceEnglish()
    {
        using (L.Use(UiLanguage.English))
        {
            var rig = CleanupRig();
            rig.Files.OldFiles[Win + @"\SoftwareDistribution\Download"] = _ => new OldFilesCleanup(0, 0, 0, 0, "link");
            rig.Services.FailStart("bits");

            var result = rig.Run(new DiskCleanupHandler(rig.Build()), ActionHandlerNames.DiskCleanup);

            Assert.Contains(
                "Downloaded updates: error – skipped – link, and then couldn't restart services: bits; restarted: wuauserv.",
                Lines(result));
        }
    }

    [Fact]
    public void DiskCleanup_StageNames_English()
    {
        using (L.Use(UiLanguage.English))
        {
            var rig = CleanupRig();

            rig.Run(new DiskCleanupHandler(rig.Build()), ActionHandlerNames.DiskCleanup);

            var first = rig.Progress.Items[0];
            Assert.Equal("System temporary files", first.Stage);
            Assert.Equal("Account temporary files", first.NextStage);
            Assert.Equal(
                new[] { "System temporary files", "Account temporary files", "Delivery Optimization", "Downloaded updates", "Component store", "Account thumbnails" },
                rig.Progress.Items.Select(p => p.Stage).Distinct().ToArray());
        }
    }

    // ---- Windows.old ----

    [Fact]
    public void WindowsOld_NotSupported_English()
    {
        using (L.Use(UiLanguage.English))
        {
            var rig = new Rig();

            var result = rig.Run(new WindowsOldHandler(rig.Build()), ActionHandlerNames.WindowsOld);

            Assert.Equal(TweakOutcome.NotSupported, result.Outcome);
            Assert.Equal("There's no previous Windows installation – nothing to remove.", result.Message);
        }
    }

    [Fact]
    public void WindowsOld_StageNamesAndResult_English()
    {
        using (L.Use(UiLanguage.English))
        {
            var rig = new Rig();
            const string caches = WindowsOldHandler.VolumeCachesKey;
            rig.Registry.CreateKey(caches + @"\Previous Installations");
            rig.Files.AddDirectory(@"C:\Windows.old");
            rig.Disk.Readings.Enqueue(10 * Gb);
            rig.Disk.Readings.Enqueue(12 * Gb);
            rig.LongTools.Handler = _ =>
            {
                rig.Files.RemoveDirectory(@"C:\Windows.old");
                return new ServicingToolRun(0, false, Array.Empty<string>());
            };

            var result = rig.Run(new WindowsOldHandler(rig.Build()), ActionHandlerNames.WindowsOld);

            Assert.Equal(TweakOutcome.Executed, result.Outcome);
            Assert.Equal("Removed the previous Windows installation. Freed: 2.0 GB.", result.Message);
            Assert.Equal(new[] { "Disk Cleanup", "Check" }, rig.Progress.Items.Select(p => p.Stage).ToArray());
            Assert.Equal("Check", rig.Progress.Items[0].NextStage);
        }
    }

    [Fact]
    public void WindowsOld_UnknownExitCode_WholeSentenceEnglish()
    {
        using (L.Use(UiLanguage.English))
        {
            var rig = new Rig();
            rig.Registry.CreateKey(WindowsOldHandler.VolumeCachesKey + @"\Previous Installations");
            rig.Files.AddDirectory(@"C:\Windows.old");
            rig.LongTools.Handler = _ => new ServicingToolRun(null, false, Array.Empty<string>());

            var result = rig.Run(new WindowsOldHandler(rig.Build()), ActionHandlerNames.WindowsOld);

            Assert.Contains("Disk Cleanup ended with an unknown code.", Lines(result));
        }
    }

    [Fact]
    public void WindowsOld_FlagAlreadySet_WholeSentenceEnglish()
    {
        using (L.Use(UiLanguage.English))
        {
            var rig = new Rig();
            rig.Registry.CreateKey(WindowsOldHandler.VolumeCachesKey + @"\Previous Installations");
            rig.Registry.Set(WindowsOldHandler.PreviousInstallationsKey, WindowsOldHandler.StateFlagsName, RegValue.FromDWord(2));
            rig.Files.AddDirectory(@"C:\Windows.old");

            var result = rig.Run(new WindowsOldHandler(rig.Build()), ActionHandlerNames.WindowsOld);

            Assert.Equal(TweakOutcome.Failed, result.Outcome);
            Assert.Equal(
                @"Value StateFlags7351 is already in the Disk Cleanup settings (VolumeCaches\Previous Installations) – another program or an interrupted earlier run set it; nothing was changed.",
                result.Message);
        }
    }

    [Fact]
    public void WindowsOld_FlagNotRemoved_WholeSentenceEnglish()
    {
        using (L.Use(UiLanguage.English))
        {
            var rig = new Rig();
            rig.Registry.CreateKey(WindowsOldHandler.VolumeCachesKey + @"\Previous Installations");
            rig.Files.AddDirectory(@"C:\Windows.old");
            rig.Registry.WriteInterceptor = (_, _, value) => value.Exists ? null : new OperationException(OperationErrorKind.AccessDenied, "denied");

            var result = rig.Run(new WindowsOldHandler(rig.Build()), ActionHandlerNames.WindowsOld);

            Assert.Equal(TweakOutcome.Failed, result.Outcome);
            Assert.Contains(
                @"Couldn't remove value StateFlags7351 from VolumeCaches\Previous Installations (denied) – remove it manually, otherwise the next run will refuse.",
                Lines(result));
        }
    }

    // ---- pliki systemowe ----

    [Fact]
    public void SystemFiles_StageNames_English()
    {
        using (L.Use(UiLanguage.English))
        {
            var rig = new Rig();

            var result = rig.Run(new SystemFilesHandler(rig.Build()), ActionHandlerNames.SystemFiles);

            var dism = rig.Progress.Items[0];
            Assert.Equal("DISM", dism.Stage);
            Assert.Equal(
                "Step 1 of 2 · Next: SFC",
                GuiText.ActionStepText(dism.StepIndex, dism.StepCount) + " · " + GuiText.ActionNextStageText(dism.NextStage!));
            var sfc = rig.Progress.Items.Last();
            Assert.Equal("Step 2 of 2", GuiText.ActionStepText(sfc.StepIndex, sfc.StepCount));
            Assert.Equal(
                new[]
                {
                    "DISM: finished without an error (code 0); CBS.log didn't show whether anything was repaired.",
                    @"SFC: ended with code 0, but the result can't be read clearly – check the [SR] entries in %SystemRoot%\Logs\CBS\CBS.log.",
                    @"Logs: %SystemRoot%\Logs\DISM\dism.log, %SystemRoot%\Logs\CBS\CBS.log.",
                },
                Lines(result));
        }
    }

    [Fact]
    public void ServicingGuard_Busy_English()
    {
        using (L.Use(UiLanguage.English))
        {
            var result = ServicingGuard.Check(new FakeServicingActivity { Active = "TiWorker.exe" });

            Assert.NotNull(result);
            Assert.Equal(TweakOutcome.Refused, result!.Outcome);
            Assert.Equal("The previous servicing operation is still in progress – try again in a moment.", result.Message);
            Assert.Equal(new[] { "A process is still running: TiWorker.exe." }, result.Warnings);
        }
    }

    // ---- punkt przywracania ----

    [Fact]
    public void RestorePoint_CreatedAndFailed_English()
    {
        using (L.Use(UiLanguage.English))
        {
            var clock = new FakeClock { UtcNow = new DateTimeOffset(2026, 10, 22, 12, 30, 5, TimeSpan.Zero) };
            var created = new RestorePointHandler(new FakeRestorePointService { Next = new RestorePointResult(RestorePointStatus.Created, null, 0) }, clock, TimeZoneInfo.Utc);
            var disabled = new RestorePointHandler(new FakeRestorePointService { Next = new RestorePointResult(RestorePointStatus.ProtectionDisabled, null, 1058) }, clock, TimeZoneInfo.Utc);
            var failed = new RestorePointHandler(new FakeRestorePointService { Next = new RestorePointResult(RestorePointStatus.Failed, null, 5) }, clock, TimeZoneInfo.Utc);
            var tweak = TestTweaks.Action() with { Script = null, Handler = ActionHandlerNames.RestorePoint };

            Assert.Equal(
                "Created restore point \"DSOptimizer – manual restore point\" (2026-10-22 12:30:05).",
                created.Run(tweak, null, null, CancellationToken.None).Message);
            Assert.Equal(
                "System Protection is turned off for drive C: – turn it on with the \"System Protection\" item in this category.",
                disabled.Run(tweak, null, null, CancellationToken.None).Message);
            Assert.Equal("Couldn't create a restore point. (code 5)", failed.Run(tweak, null, null, CancellationToken.None).Message);
        }
    }

    // ---- WinUtil ----

    private static ActionHandlerResult RunWinUtil(FakeWebDownloader web)
        => new WinUtilHandler(new WinUtilServices { Web = web, Files = new FakeDownloadFiles(), Launcher = new FakeProcessLauncher(), Log = new ListLog() })
            .Run(TestTweaks.Action() with { Script = null, Handler = ActionHandlerNames.WinUtil }, null, null, CancellationToken.None);

    [Fact]
    public void WinUtil_NoNetwork_English()
    {
        using (L.Use(UiLanguage.English))
        {
            var web = new FakeWebDownloader();
            web.EnqueueText(FakeWebDownloader.Failure(WebFailure.Network));

            var result = RunWinUtil(web);

            Assert.Equal(TweakOutcome.Failed, result.Outcome);
            Assert.Equal("Not done – no connection to GitHub.", result.Message);
        }
    }

    [Fact]
    public void WinUtil_PrereleaseRefused_WholeSentenceEnglish()
    {
        using (L.Use(UiLanguage.English))
        {
            var root = JObject.Parse(File.ReadAllText(RepoRoot.Combine("tests", "DSOptimizer.Tests.Unit", "Tools", "Samples", "winutil-release-26.10.07.json")));
            root["prerelease"] = true;
            var web = new FakeWebDownloader();
            web.EnqueueText(FakeWebDownloader.Ok(root.ToString()));

            var result = RunWinUtil(web);

            Assert.Equal("Not done – test release (prerelease).", result.Message);
            Assert.Equal("Opened the WinUtil 26.10.07 window (file from the GitHub release, SHA256 matches).", WinUtilHandler.Executed("26.10.07"));
            Assert.Equal("Downloading WinUtil 26.10.07 from GitHub…", WinUtilHandler.Downloading("26.10.07"));
        }
    }

    [Fact]
    public void WinUtil_LaunchFailed_WholeSentenceEnglish()
    {
        using (L.Use(UiLanguage.English))
        {
            var root = JObject.Parse(File.ReadAllText(RepoRoot.Combine("tests", "DSOptimizer.Tests.Unit", "Tools", "Samples", "winutil-release-26.10.07.json")));
            var web = new FakeWebDownloader();
            web.EnqueueText(FakeWebDownloader.Ok(root.ToString()));
            web.ByteSteps.Add(927798);
            var files = new FakeDownloadFiles { Hash = "eeae69922fbe6354ea4e2a37f705eaa17bdaf45cc7889618a313178a513b13ee" };
            var launcher = new FakeProcessLauncher { Failure = new OperationException(OperationErrorKind.Unexpected, "access denied.") };

            var result = new WinUtilHandler(new WinUtilServices { Web = web, Files = files, Launcher = launcher, Log = new ListLog() })
                .Run(TestTweaks.Action() with { Script = null, Handler = ActionHandlerNames.WinUtil }, null, null, CancellationToken.None);

            Assert.Equal(TweakOutcome.Failed, result.Outcome);
            Assert.Equal("Not done – couldn't open the PowerShell window: access denied.", result.Message);
        }
    }

    /// <summary>
    /// Ścieżka handlera niedostępna na fake'ach (<see cref="FakeDownloadFiles.NewDownloadPath"/> nigdy nie rzuca), więc test sprawdza
    /// metodę, która buduje zdanie, a handler zwraca dokładnie jej wynik.
    /// </summary>
    [Fact]
    public void WinUtil_DownloadFolderFailed_WholeSentenceEnglish()
    {
        using (L.Use(UiLanguage.English))
        {
            Assert.Equal("Not done – couldn't prepare the downloads folder (Access is denied).", WinUtilHandler.DownloadFolderFailed("Access is denied."));
        }
    }
}
