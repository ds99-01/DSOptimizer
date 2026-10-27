using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using DSOptimizer.Core.Abstractions;
using DSOptimizer.Core.Engine;
using DSOptimizer.Core.Errors;
using DSOptimizer.Core.Gui;
using DSOptimizer.Core.Model;
using DSOptimizer.Core.Repair;
using DSOptimizer.Tests.Unit.Engine;
using DSOptimizer.Tests.Unit.Fakes;
using DSOptimizer.Tests.Unit.Support;
using Xunit;

namespace DSOptimizer.Tests.Unit.Repair;

/// <summary>
/// Handler <c>windows-old</c> wyłącznie na fake'ach: rejestr w pamięci (31 programów obsługi <c>VolumeCaches</c> jak na świeżej VM
/// 26H2, pomiar 5f), folder <c>C:\Windows.old</c> w pamięci i fałszywy cleanmgr, który „usuwa” folder. Nic nie uruchamia
/// prawdziwego cleanmgr ani nie dotyka rejestru hosta.
/// </summary>
public class WindowsOldHandlerTests
{
    private const string Win = @"C:\Windows";
    private const string Old = @"C:\Windows.old";
    private const string Caches = @"HKLM\SOFTWARE\Microsoft\Windows\CurrentVersion\Explorer\VolumeCaches";
    private const string Previous = Caches + @"\Previous Installations";
    private const string Flag = "StateFlags7351";
    private const long Mb = 1024L * 1024;

    // Programy obsługi z baseline VM 26H2 Home (pomiar 5f, e4b2.json: 31 kluczy, żadnej wartości StateFlags*).
    private static readonly string[] Handlers =
    {
        "Active Setup Temp Folders", "Content Indexer Cleaner", "D3D Shader Cache", "Delivery Optimization Files",
        "Device Driver Packages", "Diagnostic Data Viewer database files", "Downloaded Program Files", "DownloadsFolder",
        "Feedback Hub Archive log files", "Internet Cache Files", "Language Pack", "Offline Pages Files", "Old ChkDsk Files",
        "Previous Installations", "Recycle Bin", "RetailDemo Offline Content", "Setup Log Files", "System error memory dump files",
        "System error minidump files", "Temporary Files", "Temporary Setup Files", "Temporary Sync Files", "Thumbnail Cache",
        "Update Cleanup", "Upgrade Discarded Files", "User file versions", "Windows Defender", "Windows Error Reporting Files",
        "Windows ESD installation files", "Windows Reset Log Files", "Windows Upgrade Log Files",
    };

    private sealed class Fixture
    {
        public Fixture()
        {
            foreach (var handler in Handlers)
            {
                Registry.CreateKey(Caches + @"\" + handler);
            }

            Files.AddDirectory(Old);
            DiskSpace.Readings.Enqueue(10_000 * Mb);
            DiskSpace.Readings.Enqueue(10_200 * Mb);
            Registry.WriteInterceptor = (path, name, value) =>
            {
                Events.Add($"{(value.Exists ? "set" : "remove")}:{path}|{name}={(value.Exists ? value.Data : null)}");
                return WriteFailure?.Invoke(path, name, value);
            };
            LongTools.Handler = request =>
            {
                Events.Add("run:" + request.FileName + " " + request.Arguments);
                FlagDuringRun = Registry.Get(Previous, Flag);
                return Cleanmgr(this);
            };
        }

        public FakeRegistry Registry { get; } = new();

        public FakeRepairFileSystem Files { get; } = new();

        public FakeServicingToolRunner LongTools { get; } = new();

        public FakeServicingActivity Servicing { get; } = new();

        public FakeDiskSpace DiskSpace { get; } = new();

        public FakeToolRunner Tools { get; } = new();

        public ListLog Log { get; } = new();

        public ListProgress Output { get; } = new();

        public ListProgress<ActionProgress> Progress { get; } = new();

        public List<string> Events { get; } = new();

        public RegValue? FlagDuringRun { get; private set; }

        public Func<string, string, RegValue, Exception?>? WriteFailure { get; set; }

        /// <summary>Domyślnie: cleanmgr usuwa folder i kończy się kodem 0.</summary>
        public Func<Fixture, ServicingToolRun> Cleanmgr { get; set; } = f =>
        {
            f.Files.RemoveDirectory(Old);
            return new ServicingToolRun(0, false, Array.Empty<string>());
        };

        public string SystemRoot { get; set; } = Win;

        public WindowsOldHandler Handler() => new(new RepairServices
        {
            Services = new FakeServiceManager(),
            Registry = Registry,
            Tools = Tools,
            Files = Files,
            Domain = new FakeDomainInfo(),
            Clock = new FakeClock(),
            Log = Log,
            LongTools = LongTools,
            DiskSpace = DiskSpace,
            Servicing = Servicing,
            Scripts = new FakeScriptRunner(),
            User = new FakeUserContext(),
            TimeZone = TimeZoneInfo.Utc,
            Paths = new RepairPaths { SystemRoot = SystemRoot, EnsureBackupDirectory = () => throw new InvalidOperationException("windows-old nie robi kopii") },
        });

        public TweakDefinition Tweak { get; set; } = TestTweaks.Action() with { Id = "repair.windows-old", Script = null, Handler = ActionHandlerNames.WindowsOld, Timeout = TimeSpan.FromSeconds(2700) };

        public ActionHandlerResult Run(CancellationToken cancellationToken = default)
            => Handler().Run(Tweak, Output, Progress, cancellationToken);

        /// <summary>Wszystkie wartości StateFlags7351 pod VolumeCaches po przebiegu.</summary>
        public List<string> FlagsLeft()
            => Handlers.Where(h => Registry.Get(Caches + @"\" + h, Flag).Exists).ToList();
    }

    private static void AssertNothingChanged(Fixture f)
    {
        Assert.Empty(f.LongTools.Calls);
        Assert.Empty(f.Tools.Calls);
        Assert.Empty(f.Registry.WriteLog);
        Assert.DoesNotContain(f.Events, e => e.StartsWith("set:", StringComparison.Ordinal));
        Assert.Empty(f.Files.Calls);
        Assert.Contains(Old, f.Files.Directories);
    }

    [Fact]
    public void Name_IsWindowsOld()
        => Assert.Equal("windows-old", new Fixture().Handler().Name);

    [Fact]
    public void SageRunId_IsMeasuredValue()
    {
        Assert.Equal(7351, WindowsOldHandler.SageRunId);
        Assert.Equal(Flag, WindowsOldHandler.StateFlagsName);
        Assert.Equal(Previous, WindowsOldHandler.PreviousInstallationsKey);
    }

    [Fact]
    public void NoWindowsOld_NotSupported_NothingRun()
    {
        var f = new Fixture();
        f.Files.RemoveDirectory(Old);

        var result = f.Run();

        Assert.Equal(TweakOutcome.NotSupported, result.Outcome);
        Assert.Equal("Nie ma poprzedniej instalacji Windows – nie ma czego usuwać.", result.Message);
        Assert.Equal(1, f.Servicing.Calls);
        Assert.Empty(f.LongTools.Calls);
        Assert.Empty(f.Registry.WriteLog);
        Assert.Equal(0, f.Registry.ReadCount);
        Assert.Empty(f.Progress.Items);
        Assert.Equal(new[] { Old }, f.Files.ExistenceQueries);
    }

    [Fact]
    public void GuardActive_Refused_BeforeAnyStep()
    {
        var f = new Fixture();
        f.Servicing.Active = "cleanmgr";

        var result = f.Run();

        Assert.Equal(TweakOutcome.Refused, result.Outcome);
        Assert.Equal(ServicingGuard.RefusalMessage, result.Message);
        Assert.Empty(f.Files.ExistenceQueries);
        Assert.Empty(f.DiskSpace.Calls);
        Assert.Equal(0, f.Registry.ReadCount);
        AssertNothingChanged(f);
    }

    [Fact]
    public void Success_FolderGone_Executed()
    {
        var f = new Fixture();

        var result = f.Run();

        Assert.Equal(TweakOutcome.Executed, result.Outcome);
        Assert.False(result.RequiresReboot);
        Assert.Equal("Usunięto poprzednią instalację Windows. Zwolniono: 200 MB.", result.Message);
        Assert.Empty(result.NotRestored);
        Assert.DoesNotContain(Old, f.Files.Directories);
        Assert.Empty(f.FlagsLeft());
        Assert.Equal(new[] { @"C:\", @"C:\" }, f.DiskSpace.Calls);
    }

    [Fact]
    public void Success_SetsFlagOnlyUnderPreviousInstallations_RunsCleanmgrThenRemovesFlag()
    {
        var f = new Fixture();

        f.Run();

        Assert.Equal(
            new[]
            {
                $"set:{Previous}|{Flag}=2",
                "run:cleanmgr.exe /sagerun:7351",
                $"remove:{Previous}|{Flag}=",
            },
            f.Events);
        Assert.Equal(RegValue.FromDWord(2), f.FlagDuringRun);
        var call = Assert.Single(f.LongTools.Calls);
        Assert.Equal(TimeSpan.FromSeconds(2700), call.WaitLimit);
        Assert.Equal(TimeSpan.FromSeconds(2700), call.Request.Timeout);
    }

    [Fact]
    public void ChecksEveryVolumeCachesHandlerBeforeWriting()
    {
        var f = new Fixture();

        f.Run();

        // 31 odczytów StateFlags7351 (po jednym na program obsługi), wszystkie przed pierwszym zapisem.
        Assert.Equal(Handlers.Length, f.Registry.ReadCount);
    }

    [Fact]
    public void NoTimeoutInCatalog_UsesMeasuredDefault()
    {
        var f = new Fixture();
        f.Tweak = f.Tweak with { Timeout = null };

        f.Run();

        Assert.Equal(WindowsOldHandler.DefaultWaitLimit, Assert.Single(f.LongTools.Calls).WaitLimit);
        Assert.Equal(TimeSpan.FromSeconds(2700), WindowsOldHandler.DefaultWaitLimit);
    }

    [Fact]
    public void SystemDriveFromSystemRoot()
    {
        var f = new Fixture { SystemRoot = @"D:\Windows" };
        f.Files.AddDirectory(@"D:\Windows.old");
        f.Cleanmgr = fx =>
        {
            fx.Files.RemoveDirectory(@"D:\Windows.old");
            return new ServicingToolRun(0, false, Array.Empty<string>());
        };

        var result = f.Run();

        Assert.Equal(TweakOutcome.Executed, result.Outcome);
        Assert.Equal(new[] { @"D:\Windows.old", @"D:\Windows.old" }, f.Files.ExistenceQueries);
        Assert.Equal(new[] { @"D:\", @"D:\" }, f.DiskSpace.Calls);
        Assert.Contains(Old, f.Files.Directories);
    }

    [Fact]
    public void FolderRemains_Failed_FlagRemoved()
    {
        var f = new Fixture();
        f.DiskSpace.Readings.Clear();
        f.DiskSpace.Readings.Enqueue(10_000 * Mb);
        f.DiskSpace.Readings.Enqueue(10_000 * Mb);
        f.Cleanmgr = _ => new ServicingToolRun(0, false, Array.Empty<string>());

        var result = f.Run();

        Assert.Equal(TweakOutcome.Failed, result.Outcome);
        Assert.Equal("Folder Windows.old nadal istnieje (zwolniono: mniej niż 1 MB).", result.Message);
        Assert.Empty(f.FlagsLeft());
    }

    [Fact]
    public void FreeSpaceUnreadable_NoFakeNumber()
    {
        var f = new Fixture();
        f.DiskSpace.Readings.Clear();
        f.DiskSpace.Readings.Enqueue(null);

        var result = f.Run();

        Assert.Equal(TweakOutcome.Executed, result.Outcome);
        Assert.Equal("Usunięto poprzednią instalację Windows. Zwolniono: nie udało się odczytać wolnego miejsca.", result.Message);
    }

    [Fact]
    public void NonZeroExitCode_FolderGone_ExecutedWithCodeNote()
    {
        var f = new Fixture();
        f.Cleanmgr = fx =>
        {
            fx.Files.RemoveDirectory(Old);
            return new ServicingToolRun(1, false, Array.Empty<string>());
        };

        var result = f.Run();

        Assert.Equal(TweakOutcome.Executed, result.Outcome);
        Assert.Contains("Usunięto poprzednią instalację Windows. Zwolniono: 200 MB.", result.Message, StringComparison.Ordinal);
        Assert.Contains("kodem 0x00000001", result.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Timeout_NotConfirmed_FlagRemoved()
    {
        var f = new Fixture();
        f.Cleanmgr = _ => new ServicingToolRun(null, true, Array.Empty<string>());

        var result = f.Run();

        Assert.Equal(TweakOutcome.NotConfirmed, result.Outcome);
        Assert.StartsWith("Czyszczenie nadal działa w tle", result.Message, StringComparison.Ordinal);
        Assert.Contains("45 min", result.Message, StringComparison.Ordinal);
        Assert.Contains(@"C:\Windows.old jeszcze istnieje", result.Message, StringComparison.Ordinal);
        Assert.Empty(f.FlagsLeft());
        Assert.Equal($"remove:{Previous}|{Flag}=", f.Events.Last());
    }

    [Fact]
    public void Timeout_FolderAlreadyGone_NotConfirmed_SaysFolderIsGone()
    {
        var f = new Fixture();
        f.Cleanmgr = fx =>
        {
            fx.Files.RemoveDirectory(Old);
            return new ServicingToolRun(null, true, Array.Empty<string>());
        };

        var result = f.Run();

        Assert.Equal(TweakOutcome.NotConfirmed, result.Outcome);
        Assert.StartsWith("Czyszczenie nadal działa w tle", result.Message, StringComparison.Ordinal);
        Assert.Contains(@"C:\Windows.old już nie istnieje (zwolniono: 200 MB)", result.Message, StringComparison.Ordinal);
        Assert.Empty(f.FlagsLeft());
    }

    [Theory]
    [InlineData("Temporary Files")]
    [InlineData("Previous Installations")]
    [InlineData("Windows Upgrade Log Files")]
    public void ForeignStateFlagsPresent_FailedNothingChanged(string handler)
    {
        var f = new Fixture();
        f.Registry.Set(Caches + @"\" + handler, Flag, RegValue.FromDWord(0));

        var result = f.Run();

        Assert.Equal(TweakOutcome.Failed, result.Outcome);
        Assert.Contains("nic nie zmieniono", result.Message, StringComparison.Ordinal);
        Assert.Contains(@"VolumeCaches\" + handler, result.Message, StringComparison.Ordinal);
        AssertNothingChanged(f);
        Assert.Equal(RegValue.FromDWord(0), f.Registry.Get(Caches + @"\" + handler, Flag));
        Assert.Empty(f.Progress.Items);
    }

    [Fact]
    public void ForeignStateFlagsUnderUnknownHandler_FailedNothingChanged()
    {
        var f = new Fixture();
        f.Registry.Set(Caches + @"\Cudzy program obsługi", Flag, RegValue.FromDWord(2));

        var result = f.Run();

        Assert.Equal(TweakOutcome.Failed, result.Outcome);
        Assert.Contains("nic nie zmieniono", result.Message, StringComparison.Ordinal);
        AssertNothingChanged(f);
    }

    [Fact]
    public void StateFlagsReadDenied_FailedNothingChanged()
    {
        var f = new Fixture();
        f.Registry.DenyRead(Caches + @"\Update Cleanup", Flag);

        var result = f.Run();

        Assert.Equal(TweakOutcome.Failed, result.Outcome);
        Assert.Contains("nic nie zmieniono", result.Message, StringComparison.Ordinal);
        Assert.Contains("Update Cleanup", result.Message, StringComparison.Ordinal);
        AssertNothingChanged(f);
    }

    [Fact]
    public void VolumeCachesListDenied_FailedNothingChanged()
    {
        var f = new Fixture();
        f.Registry.DenyKey(Caches);

        var result = f.Run();

        Assert.Equal(TweakOutcome.Failed, result.Outcome);
        Assert.Contains("nic nie zmieniono", result.Message, StringComparison.Ordinal);
        AssertNothingChanged(f);
    }

    [Fact]
    public void PreviousInstallationsHandlerMissing_FailedNothingChanged()
    {
        var f = new Fixture();
        f.Registry.DeleteKeyTree(Previous);

        var result = f.Run();

        Assert.Equal(TweakOutcome.Failed, result.Outcome);
        Assert.Contains("Previous Installations", result.Message, StringComparison.Ordinal);
        Assert.Contains("nic nie zmieniono", result.Message, StringComparison.Ordinal);
        AssertNothingChanged(f);
    }

    [Fact]
    public void FlagWriteFails_FailedNothingStarted()
    {
        var f = new Fixture();
        f.WriteFailure = (_, _, value) => value.Exists ? new OperationException(OperationErrorKind.AccessDenied, "odmowa zapisu (fake)") : null;

        var result = f.Run();

        Assert.Equal(TweakOutcome.Failed, result.Outcome);
        Assert.Contains("odmowa zapisu (fake)", result.Message, StringComparison.Ordinal);
        Assert.Contains("nie zostało uruchomione", result.Message, StringComparison.Ordinal);
        Assert.Empty(f.LongTools.Calls);
        Assert.Contains(Old, f.Files.Directories);
        Assert.Empty(f.FlagsLeft());
    }

    [Fact]
    public void StateFlagsRemovedEvenOnFailure_StartError()
    {
        var f = new Fixture();
        f.Cleanmgr = _ => throw new OperationException(OperationErrorKind.NotFound, @"Nie znaleziono pliku C:\Windows\System32\cleanmgr.exe.");

        var result = f.Run();

        Assert.Equal(TweakOutcome.Failed, result.Outcome);
        Assert.Contains("Nie udało się uruchomić Oczyszczania dysku", result.Message, StringComparison.Ordinal);
        Assert.Contains("cleanmgr.exe", result.Message, StringComparison.Ordinal);
        Assert.Empty(f.FlagsLeft());
        Assert.Equal($"remove:{Previous}|{Flag}=", f.Events.Last());
    }

    [Fact]
    public void StateFlagsRemovedEvenOnFailure_UnexpectedException()
    {
        var f = new Fixture();
        f.Cleanmgr = _ => throw new InvalidOperationException("awaria runnera (fake)");

        Assert.Throws<InvalidOperationException>(() => f.Run());

        Assert.Empty(f.FlagsLeft());
        Assert.Equal($"remove:{Previous}|{Flag}=", f.Events.Last());
    }

    [Fact]
    public void FlagRemovalFails_Failed_WithNotRestored()
    {
        var f = new Fixture();
        f.WriteFailure = (_, _, value) => value.Exists ? null : new OperationException(OperationErrorKind.AccessDenied, "odmowa usunięcia (fake)");

        var result = f.Run();

        Assert.Equal(TweakOutcome.Failed, result.Outcome);
        Assert.Contains("Usunięto poprzednią instalację Windows.", result.Message, StringComparison.Ordinal);
        Assert.Contains("StateFlags7351", result.Message, StringComparison.Ordinal);
        Assert.Contains("odmowa usunięcia (fake)", result.Message, StringComparison.Ordinal);
        var left = Assert.Single(result.NotRestored);
        Assert.Contains(@"VolumeCaches\Previous Installations", left, StringComparison.Ordinal);
    }

    [Fact]
    public void CancelledTokenDuringCleanmgr_Ignored_VerifiedRemovalIsExecuted()
    {
        // Ruling R30: krok nieprzerywalny – okno nie oferuje „Zatrzymaj”, a token (gdyby jednak przyszedł) niczego nie zmienia.
        var f = new Fixture();
        using var cts = new CancellationTokenSource();
        f.Cleanmgr = fx =>
        {
            cts.Cancel();
            fx.Files.RemoveDirectory(Old);
            return new ServicingToolRun(0, false, Array.Empty<string>());
        };

        var result = f.Run(cts.Token);

        Assert.Equal(TweakOutcome.Executed, result.Outcome);
        Assert.Equal("Usunięto poprzednią instalację Windows. Zwolniono: 200 MB.", result.Message);
        Assert.Empty(f.FlagsLeft());
    }

    [Fact]
    public void Progress_TwoSteps_CleanupThenCheck_NeitherStoppable()
    {
        var f = new Fixture();

        f.Run();

        Assert.Equal(
            new[]
            {
                new ActionProgress(WindowsOldHandler.CleanupStage, null, 0, 2, WindowsOldHandler.CheckStage, Stoppable: false),
                new ActionProgress(WindowsOldHandler.CheckStage, null, 1, 2, null, Stoppable: false),
            },
            f.Progress.Items);
    }

    [Fact]
    public void GuiModel_CanStopFalseThroughoutRun()
    {
        var f = new Fixture();
        var item = new SeriesItem("repair.windows-old", "Usuń poprzednią instalację Windows", RiskLevel.Caution, "", Array.Empty<string>(),
            Array.Empty<string>(), Array.Empty<SeriesFact>());
        var run = new ActionRun(new SeriesConfirmation(GuiOperation.Execute, "Uruchom", item.Title, Array.Empty<SeriesFact>(), new[] { item }, "Uruchom", false));
        var seen = new List<bool> { run.CanStop };
        var progress = new SyncProgress<ActionProgress>(p =>
        {
            run.OnProgress(p);
            seen.Add(run.CanStop);
        });
        f.Cleanmgr = fx =>
        {
            seen.Add(run.CanStop);
            Assert.False(run.RequestStop());
            fx.Files.RemoveDirectory(Old);
            return new ServicingToolRun(0, false, Array.Empty<string>());
        };

        var result = f.Handler().Run(f.Tweak, f.Output, progress, CancellationToken.None);

        Assert.Equal(TweakOutcome.Executed, result.Outcome);
        Assert.Equal(4, seen.Count);
        Assert.All(seen, Assert.False);
        Assert.False(run.StopRequested);
    }

    [Fact]
    public void StartProbeError_FailedNothingChanged()
    {
        var f = new Fixture();
        f.Files.ProbeFailure = _ => new OperationException(OperationErrorKind.AccessDenied, "odmowa dostępu do folderu (fake)");

        var result = f.Run();

        Assert.Equal(TweakOutcome.Failed, result.Outcome);
        Assert.Contains("odmowa dostępu do folderu (fake)", result.Message, StringComparison.Ordinal);
        Assert.Contains("nic nie zmieniono", result.Message, StringComparison.Ordinal);
        Assert.DoesNotContain("nie ma czego usuwać", result.Message, StringComparison.Ordinal);
        AssertNothingChanged(f);
        Assert.Equal(0, f.Registry.ReadCount);
        Assert.Empty(f.DiskSpace.Calls);
        Assert.Empty(f.Progress.Items);
    }

    [Fact]
    public void ReadBackProbeError_NotConfirmed_KeepsCleanmgrNote()
    {
        var f = new Fixture();
        f.Cleanmgr = fx =>
        {
            fx.Files.RemoveDirectory(Old);
            fx.Files.ProbeFailure = _ => new OperationException(OperationErrorKind.Unexpected, "dysk niegotowy (fake)");
            return new ServicingToolRun(1, false, Array.Empty<string>());
        };

        var result = f.Run();

        Assert.Equal(TweakOutcome.NotConfirmed, result.Outcome);
        Assert.Contains("dysk niegotowy (fake)", result.Message, StringComparison.Ordinal);
        Assert.Contains(@"C:\Windows.old", result.Message, StringComparison.Ordinal);
        Assert.DoesNotContain("Usunięto", result.Message, StringComparison.Ordinal);
        Assert.Contains("kodem 0x00000001", result.Message, StringComparison.Ordinal);
        Assert.Empty(result.NotRestored);
        Assert.Empty(f.FlagsLeft());
    }

    [Fact]
    public void ReadBackProbeError_FlagRemovalFails_FailedWithNotRestored()
    {
        var f = new Fixture();
        f.WriteFailure = (_, _, value) => value.Exists ? null : new OperationException(OperationErrorKind.AccessDenied, "odmowa usunięcia (fake)");
        f.Cleanmgr = fx =>
        {
            fx.Files.ProbeFailure = _ => new OperationException(OperationErrorKind.Unexpected, "dysk niegotowy (fake)");
            return new ServicingToolRun(0, false, Array.Empty<string>());
        };

        var result = f.Run();

        Assert.Equal(TweakOutcome.Failed, result.Outcome);
        Assert.Contains("dysk niegotowy (fake)", result.Message, StringComparison.Ordinal);
        Assert.Contains("odmowa usunięcia (fake)", result.Message, StringComparison.Ordinal);
        var left = Assert.Single(result.NotRestored);
        Assert.Contains(@"VolumeCaches\Previous Installations", left, StringComparison.Ordinal);
    }

    [Fact]
    public void Timeout_ReadBackProbeError_NotConfirmed_StateUnknown()
    {
        var f = new Fixture();
        f.Cleanmgr = fx =>
        {
            fx.Files.ProbeFailure = _ => new OperationException(OperationErrorKind.AccessDenied, "odmowa (fake)");
            return new ServicingToolRun(null, true, Array.Empty<string>());
        };

        var result = f.Run();

        Assert.Equal(TweakOutcome.NotConfirmed, result.Outcome);
        Assert.StartsWith("Czyszczenie nadal działa w tle", result.Message, StringComparison.Ordinal);
        Assert.Contains("odmowa (fake)", result.Message, StringComparison.Ordinal);
        Assert.DoesNotContain("już nie istnieje", result.Message, StringComparison.Ordinal);
        Assert.DoesNotContain("jeszcze istnieje", result.Message, StringComparison.Ordinal);
        Assert.Empty(f.FlagsLeft());
    }

    [Fact]
    public void CleanmgrNotStarted_NoFolderStateClaimInLog()
    {
        var f = new Fixture();
        f.Cleanmgr = _ => throw new OperationException(OperationErrorKind.NotFound, "brak cleanmgr.exe (fake)");

        f.Run();

        Assert.DoesNotContain(f.Log.Lines, l => l.Contains("nie istnieje", StringComparison.Ordinal));
        Assert.Equal(new[] { Old }, f.Files.ExistenceQueries);
    }

    [Fact]
    public void CleanmgrOutputLines_GoToOutputAndLog()
    {
        var f = new Fixture();
        f.Cleanmgr = fx =>
        {
            fx.Files.RemoveDirectory(Old);
            return new ServicingToolRun(0, false, new[] { "linia cleanmgr" });
        };

        f.Run();

        Assert.Equal(new[] { "linia cleanmgr" }, f.Output.Items);
        Assert.Contains(f.Log.Lines, l => l.Contains("linia cleanmgr", StringComparison.Ordinal));
    }

    [Fact]
    public void LogsRawFreeSpaceAndResult()
    {
        var f = new Fixture();

        f.Run();

        Assert.Contains(f.Log.Lines, l => l.Contains("cleanmgr.exe /sagerun:7351", StringComparison.Ordinal));
        Assert.Contains(f.Log.Lines, l => l.Contains((10_000 * Mb).ToString(System.Globalization.CultureInfo.InvariantCulture), StringComparison.Ordinal));
    }

    [Fact]
    public void NeverDeletesFilesItself()
    {
        var f = new Fixture();
        f.Cleanmgr = _ => new ServicingToolRun(0, false, Array.Empty<string>());

        f.Run();

        // Mechanizm A (R8/R18): folder usuwa wyłącznie cleanmgr; handler niczego nie usuwa ani nie przejmuje.
        Assert.Empty(f.Files.Calls);
        Assert.Empty(f.Tools.Calls);
    }

    private sealed class SyncProgress<T> : IProgress<T>
    {
        private readonly Action<T> _onReport;

        public SyncProgress(Action<T> onReport) => _onReport = onReport;

        public void Report(T value) => _onReport(value);
    }
}
