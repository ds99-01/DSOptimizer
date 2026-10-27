using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using DSOptimizer.Core.Abstractions;
using DSOptimizer.Core.Engine;
using DSOptimizer.Core.Errors;
using DSOptimizer.Core.Model;
using DSOptimizer.Core.Repair;
using DSOptimizer.Tests.Unit.Engine;
using DSOptimizer.Tests.Unit.Fakes;
using Xunit;

namespace DSOptimizer.Tests.Unit.Repair;

/// <summary>
/// Handler <c>disk-cleanup</c> wyłącznie na fake'ach: system plików, usługi, rejestr, skrypt DO i DISM są w pamięci, DISM odtwarza
/// próbkę <c>/StartComponentCleanup</c> z VM (Samples/). Nic nie usuwa plików hosta ani nie uruchamia DISM.
/// </summary>
public class DiskCleanupHandlerTests
{
    private const string Win = @"C:\Windows";
    private const string SysTemp = Win + @"\Temp";
    private const string Sid = "S-1-5-21-1000";
    private const string Profile = @"C:\Users\dso";
    private const string UserTemp = Profile + @"\AppData\Local\Temp";
    private const string Explorer = Profile + @"\AppData\Local\Microsoft\Windows\Explorer";
    private const string Download = Win + @"\SoftwareDistribution\Download";
    private const string ProfileKey = @"HKLM\SOFTWARE\Microsoft\Windows NT\CurrentVersion\ProfileList\" + Sid;
    private const long Mb = 1024L * 1024;
    private const long Gb = 1024L * Mb;

    private static readonly string[] Stages =
    {
        "Pliki tymczasowe systemu", "Pliki tymczasowe konta", "Optymalizacja dostarczania", "Pobrane aktualizacje", "Magazyn składników", "Miniatury konta",
    };

    private static readonly IReadOnlyList<string> DismLines = RepairSamples.Lines("dism-startcomponentcleanup-home.bin");

    private sealed class Fixture
    {
        public Fixture()
        {
            foreach (var dir in new[] { SysTemp, UserTemp, Download, Explorer })
            {
                Files.AddDirectory(dir);
            }

            Files.OldFiles[SysTemp] = _ => new OldFilesCleanup(2, 1, 1, 3 * Mb);
            Files.OldFiles[UserTemp] = _ => new OldFilesCleanup(4, 1, 5, 20 * Mb);
            Files.OldFiles[Download] = _ => new OldFilesCleanup(8, 0, 0, 255 * Mb);
            Files.NamedFiles[Explorer] = () => new OldFilesCleanup(15, 0, 0, 2 * Mb);
            Registry.Set(ProfileKey, "ProfileImagePath", RegValue.FromExpandString(Profile));
            Services.Add("wuauserv", ServiceStartType.Manual, running: true);
            Services.Add("bits", ServiceStartType.AutomaticDelayed, running: true);
            Disk.Readings.Enqueue(10 * Gb);
            Disk.Readings.Enqueue((11 * Gb) + (Gb / 2));
            Scripts.On(DiskCleanupScripts.DeliveryOptimization, request =>
            {
                Events.Add("script:DO");
                return DoResult(request);
            });
            LongTools.Handler = request =>
            {
                Events.Add("run:" + request.FileName + " " + request.Arguments);
                return Dism(request);
            };
            Files.Observer = call => Events.Add(call);
            Services.Observer = call => Events.Add(call);
        }

        public FakeServiceManager Services { get; } = new();

        public FakeRegistry Registry { get; } = new();

        public FakeRepairFileSystem Files { get; } = new();

        public FakeServicingToolRunner LongTools { get; } = new();

        public FakeServicingActivity Servicing { get; } = new();

        public FakeScriptRunner Scripts { get; } = new();

        public FakeDiskSpace Disk { get; } = new();

        public FakeUserContext User { get; } = new() { Sid = Sid, IsCertain = true };

        public FakeClock Clock { get; } = new();

        public ListLog Log { get; } = new();

        public ListProgress Output { get; } = new();

        public ListProgress<ActionProgress> Progress { get; } = new();

        public List<string> Events { get; } = new();

        public Func<ScriptRequest, ScriptResult> DoResult { get; set; } = _ => FakeScriptRunner.Result(0, "Deleting...", DiskCleanupScripts.DoneMarker);

        public Func<ToolRequest, ServicingToolRun> Dism { get; set; } = _ => new ServicingToolRun(0, false, DismLines.ToArray());

        public TweakDefinition Tweak { get; set; } = TestTweaks.Action() with { Id = "repair.disk-cleanup", Script = null, Handler = ActionHandlerNames.DiskCleanup, Timeout = TimeSpan.FromSeconds(1200) };

        public DiskCleanupHandler Handler() => new(new RepairServices
        {
            Services = Services,
            Registry = Registry,
            Tools = new FakeToolRunner(),
            Files = Files,
            Domain = new FakeDomainInfo(),
            Clock = Clock,
            Log = Log,
            LongTools = LongTools,
            DiskSpace = Disk,
            Servicing = Servicing,
            Scripts = Scripts,
            User = User,
            TimeZone = TimeZoneInfo.Utc,
            Paths = new RepairPaths { SystemRoot = Win, EnsureBackupDirectory = () => throw new InvalidOperationException("disk-cleanup nie robi kopii") },
        });

        public ActionHandlerResult Run(CancellationToken cancellationToken = default)
            => Handler().Run(Tweak, Output, Progress, cancellationToken);
    }

    private static string[] Lines(ActionHandlerResult result) => (result.Message ?? string.Empty).Split(new[] { Environment.NewLine }, StringSplitOptions.None);

    private static string Line(ActionHandlerResult result, string stage) => Lines(result).Single(l => l.StartsWith(stage + ":", StringComparison.Ordinal));

    // ---- nazwa, strażnik ----

    [Fact]
    public void Name_IsDiskCleanup() => Assert.Equal(ActionHandlerNames.DiskCleanup, new Fixture().Handler().Name);

    [Fact]
    public void GuardActive_NothingStarted()
    {
        var f = new Fixture();
        f.Servicing.Active = "cleanmgr";

        var result = f.Run();

        Assert.Equal(TweakOutcome.Refused, result.Outcome);
        Assert.Equal(ServicingGuard.RefusalMessage, result.Message);
        Assert.Empty(f.Events);
        Assert.Empty(f.Disk.Calls);
        Assert.Empty(f.Scripts.Calls);
        Assert.Empty(f.LongTools.Calls);
        Assert.Empty(f.Progress.Items);
    }

    // ---- ścieżka szczęśliwa ----

    [Fact]
    public void HappyPath_StepsInOrder_MessageWithFreedSpaceAndStepLines()
    {
        var f = new Fixture();

        var result = f.Run();

        Assert.Equal(TweakOutcome.Executed, result.Outcome);
        Assert.False(result.RequiresReboot);
        Assert.Equal(
            new[]
            {
                "DeleteOld:" + SysTemp,
                "DeleteOld:" + UserTemp,
                "script:DO",
                "StopService:wuauserv",
                "StopService:bits",
                "DeleteOld:" + Download,
                "StartService:bits",
                "StartService:wuauserv",
                "run:dism.exe /Online /Cleanup-Image /StartComponentCleanup",
                "DeleteNamed:" + Explorer + "|thumbcache_*.db",
            },
            f.Events);
        Assert.Equal(
            new[]
            {
                "Zwolniono: 1,5 GB.",
                "Pliki tymczasowe systemu: wykonano (plików: 2, 3 MB; młodszych niż 24 h zostało: 1; w użyciu zostało: 1).",
                "Pliki tymczasowe konta: wykonano (plików: 4, 20 MB; młodszych niż 24 h zostało: 5; w użyciu zostało: 1).",
                "Optymalizacja dostarczania: wykonano – polecenie zakończyło się bez błędu (pliki pobierane w tej chwili zostają).",
                "Pobrane aktualizacje: wykonano (plików: 8, 255 MB); usługi uruchomiono ponownie: bits, wuauserv.",
                "Magazyn składników: wykonano (DISM /StartComponentCleanup, kod 0).",
                "Miniatury konta: wykonano (plików: 15, 2 MB).",
            },
            Lines(result));
        Assert.Equal(new[] { @"C:\", @"C:\" }, f.Disk.Calls);
        Assert.Contains(f.Log.Lines, l => l.Contains(@"Wolne C:\ przed: 10737418240 B, po: 12348030976 B. Wynik: Executed."));
        Assert.True(f.Services.IsRunning("wuauserv"));
        Assert.True(f.Services.IsRunning("bits"));
    }

    [Fact]
    public void TempThreshold_Is24HoursBeforeNow_DownloadUsesMaxValue()
    {
        var f = new Fixture();
        var thresholds = new Dictionary<string, DateTime>();
        foreach (var dir in new[] { SysTemp, UserTemp, Download })
        {
            var key = dir;
            f.Files.OldFiles[key] = threshold =>
            {
                thresholds[key] = threshold;
                return OldFilesCleanup.None;
            };
        }

        f.Run();

        var expected = f.Clock.UtcNow.UtcDateTime.AddHours(-24);
        Assert.Equal(expected, thresholds[SysTemp]);
        Assert.Equal(expected, thresholds[UserTemp]);
        Assert.Equal(DateTime.MaxValue, thresholds[Download]);
    }

    [Fact]
    public void Progress_SixStepsWithNextStage_DismPercentsInComponentStep()
    {
        var f = new Fixture();

        f.Run();

        var starts = f.Progress.Items.Where(p => p.Percent is null).ToList();
        Assert.Equal(Stages, starts.Select(p => p.Stage));
        for (var i = 0; i < Stages.Length; i++)
        {
            Assert.Equal((i, 6, i + 1 < Stages.Length ? Stages[i + 1] : null), (starts[i].StepIndex, starts[i].StepCount, starts[i].NextStage));
        }

        var percents = f.Progress.Items.Where(p => p.Percent is not null).ToList();
        Assert.NotEmpty(percents);
        Assert.All(percents, p => Assert.Equal(("Magazyn składników", 4, "Miniatury konta"), (p.Stage, p.StepIndex, p.NextStage)));
        Assert.Equal(100, percents[percents.Count - 1].Percent);
        Assert.Equal(percents.Count, percents.Select(p => p.Percent).Distinct().Count());
    }

    [Fact]
    public void Output_GetsStepLinesAndDismLines_LogGetsStepLines()
    {
        var f = new Fixture();

        var result = f.Run();

        foreach (var line in DismLines)
        {
            Assert.Contains(line, f.Output.Items);
        }

        foreach (var line in Lines(result).Skip(1))
        {
            Assert.Contains(line, f.Output.Items);
            Assert.Contains(f.Log.Lines, l => l.EndsWith(line, StringComparison.Ordinal));
        }
    }

    [Fact]
    public void DoScript_GetsCatalogTimeoutAndNoCancellationOfRunningStep()
    {
        var f = new Fixture();

        f.Run();

        var call = Assert.Single(f.Scripts.Calls);
        Assert.Equal(DiskCleanupScripts.DeliveryOptimization, call.Body);
        Assert.Equal(TimeSpan.FromSeconds(1200), call.Timeout);
    }

    [Fact]
    public void NullTimeout_UsesMeasuredDefaultLimit()
    {
        var f = new Fixture();
        f.Tweak = f.Tweak with { Timeout = null };

        f.Run();

        Assert.Equal(TimeSpan.FromSeconds(1200), DiskCleanupHandler.DefaultWaitLimit);
        Assert.Equal(DiskCleanupHandler.DefaultWaitLimit, Assert.Single(f.LongTools.Calls).WaitLimit);
        Assert.Equal(DiskCleanupHandler.DefaultWaitLimit, Assert.Single(f.Scripts.Calls).Timeout);
    }

    // ---- wolne miejsce (Review Focus 1) ----

    [Fact]
    public void FreeSpaceShrank_NoFakeNumber_StepsStillListed()
    {
        var f = new Fixture();
        f.Disk.Readings.Clear();
        f.Disk.Readings.Enqueue(10 * Gb);
        f.Disk.Readings.Enqueue(9 * Gb);

        var result = f.Run();

        Assert.Equal(TweakOutcome.Executed, result.Outcome);
        Assert.Equal("Zwolniono: nie da się uczciwie podać zwolnionego miejsca (inne programy zapisywały na dysku).", Lines(result)[0]);
        Assert.Equal(7, Lines(result).Length);
    }

    [Fact]
    public void FreeSpaceUnreadable_SaysSo()
    {
        var f = new Fixture();
        f.Disk.Readings.Clear();
        f.Disk.Readings.Enqueue(null);

        var result = f.Run();

        Assert.Equal("Zwolniono: nie udało się odczytać wolnego miejsca.", Lines(result)[0]);
        Assert.Contains(f.Log.Lines, l => l.Contains("przed: brak odczytu, po: brak odczytu."));
        Assert.Equal(TweakOutcome.Executed, result.Outcome);
    }

    // ---- konto (Review Focus 5) ----

    [Fact]
    public void UserUncertain_UserStepsSkippedWithNote()
    {
        var f = new Fixture();
        f.User.IsCertain = false;

        var result = f.Run();

        Assert.Equal(TweakOutcome.Executed, result.Outcome);
        const string Note = "pominięto – nie udało się ustalić zalogowanego konta (brak sesji interaktywnej); program nie czyści zamiast niego własnego konta.";
        Assert.Equal("Pliki tymczasowe konta: " + Note, Line(result, "Pliki tymczasowe konta"));
        Assert.Equal("Miniatury konta: " + Note, Line(result, "Miniatury konta"));
        Assert.DoesNotContain(f.Events, e => e.Contains(Profile));
        Assert.DoesNotContain(f.Files.ExistenceQueries, q => q.Contains(Profile));
        Assert.Contains("DeleteOld:" + SysTemp, f.Events);
        Assert.Contains("DeleteOld:" + Download, f.Events);
        Assert.Contains("run:dism.exe /Online /Cleanup-Image /StartComponentCleanup", f.Events);
        Assert.Equal(0, f.Registry.ReadCount);
    }

    [Fact]
    public void NoProfilePath_UserStepsSkipped()
    {
        var f = new Fixture();
        f.Registry.Set(ProfileKey, "ProfileImagePath", RegValue.Absent);

        var result = f.Run();

        Assert.Equal(TweakOutcome.Executed, result.Outcome);
        var note = $"pominięto – brak ścieżki profilu konta {Sid} w rejestrze (ProfileList).";
        Assert.Equal("Pliki tymczasowe konta: " + note, Line(result, "Pliki tymczasowe konta"));
        Assert.Equal("Miniatury konta: " + note, Line(result, "Miniatury konta"));
        Assert.DoesNotContain(f.Events, e => e.Contains(Profile));
        Assert.Contains("DeleteOld:" + SysTemp, f.Events);
    }

    [Fact]
    public void ProfileReadDenied_UserStepsSkippedWithReason_NotFailed()
    {
        var f = new Fixture();
        f.Registry.DenyRead(ProfileKey, "ProfileImagePath");

        var result = f.Run();

        Assert.Equal(TweakOutcome.Executed, result.Outcome);
        Assert.StartsWith($"Pliki tymczasowe konta: pominięto – nie udało się odczytać ścieżki profilu konta {Sid} (", Line(result, "Pliki tymczasowe konta"), StringComparison.Ordinal);
        Assert.DoesNotContain(f.Events, e => e.Contains(Profile));
    }

    [Theory]
    [InlineData(@"\\serwer\profile\dso")]
    [InlineData(@"Users\dso")]
    [InlineData(@"C:\Users\..\Windows")]
    [InlineData("   ")]
    public void ProfilePathNotLocalAbsolute_UserStepsSkipped(string path)
    {
        var f = new Fixture();
        f.Registry.Set(ProfileKey, "ProfileImagePath", RegValue.FromExpandString(path));

        var result = f.Run();

        Assert.Equal(TweakOutcome.Executed, result.Outcome);
        Assert.StartsWith("Pliki tymczasowe konta: pominięto – ", Line(result, "Pliki tymczasowe konta"), StringComparison.Ordinal);
        Assert.StartsWith("Miniatury konta: pominięto – ", Line(result, "Miniatury konta"), StringComparison.Ordinal);
        Assert.Equal(new[] { "DeleteOld:" + SysTemp, "DeleteOld:" + Download }, f.Events.Where(e => e.StartsWith("DeleteOld:", StringComparison.Ordinal)));
        Assert.DoesNotContain(f.Events, e => e.StartsWith("DeleteNamed:", StringComparison.Ordinal));
    }

    [Theory]
    [InlineData("")]
    [InlineData(@"S-1-5-21-1000\..\x")]
    public void SidEmptyOrMalformed_UserStepsSkipped_RegistryNotRead(string sid)
    {
        var f = new Fixture();
        f.User.Sid = sid;

        var result = f.Run();

        Assert.Equal(TweakOutcome.Executed, result.Outcome);
        Assert.StartsWith("Pliki tymczasowe konta: pominięto – ", Line(result, "Pliki tymczasowe konta"), StringComparison.Ordinal);
        Assert.Equal(0, f.Registry.ReadCount);
    }

    [Fact]
    public void ProfileWithTrailingBackslash_PathsWithoutDoubleSeparator()
    {
        var f = new Fixture();
        f.Registry.Set(ProfileKey, "ProfileImagePath", RegValue.FromExpandString(Profile + @"\"));

        f.Run();

        Assert.Contains("DeleteOld:" + UserTemp, f.Events);
        Assert.Contains("DeleteNamed:" + Explorer + "|thumbcache_*.db", f.Events);
    }

    [Fact]
    public void ProfileResolutionThrows_ThumbnailsSkippedWithDefaultReason()
    {
        var f = new Fixture();
        f.Registry.ReadFailure = (path, _) => path == ProfileKey ? new InvalidOperationException("awaria odczytu rejestru (fake)") : null;

        var result = f.Run();

        Assert.Equal(TweakOutcome.Failed, result.Outcome);
        Assert.Equal("Pliki tymczasowe konta: błąd – awaria odczytu rejestru (fake)", Line(result, "Pliki tymczasowe konta").TrimEnd('.'));
        Assert.Equal("Miniatury konta: pominięto – nie udało się ustalić profilu konta.", Line(result, "Miniatury konta"));
        Assert.DoesNotContain(f.Events, e => e.Contains(Profile));
    }

    [Fact]
    public void CleanupSkippedByFileSystem_StepSkippedWithReason_NotDoneWithZero()
    {
        var f = new Fixture();
        f.Files.OldFiles[SysTemp] = _ => OldFilesCleanup.Skipped("ścieżka prowadzi przez link (fake)");
        f.Files.OldFiles[Download] = _ => OldFilesCleanup.Skipped("folder jest linkiem (fake)");
        f.Files.NamedFiles[Explorer] = () => OldFilesCleanup.Skipped("folder jest linkiem (fake)");

        var result = f.Run();

        Assert.Equal(TweakOutcome.Executed, result.Outcome);
        Assert.Equal("Pliki tymczasowe systemu: pominięto – ścieżka prowadzi przez link (fake).", Line(result, "Pliki tymczasowe systemu"));
        Assert.Equal("Pobrane aktualizacje: pominięto – folder jest linkiem (fake); usługi uruchomiono ponownie: bits, wuauserv.", Line(result, "Pobrane aktualizacje"));
        Assert.Equal("Miniatury konta: pominięto – folder jest linkiem (fake).", Line(result, "Miniatury konta"));
        Assert.DoesNotContain("plików: 0", result.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void MissingFolders_StepsSkippedWithPath_NotErrors()
    {
        var f = new Fixture();
        f.Files.RemoveDirectory(SysTemp);
        f.Files.RemoveDirectory(UserTemp);
        f.Files.RemoveDirectory(Explorer);

        var result = f.Run();

        Assert.Equal(TweakOutcome.Executed, result.Outcome);
        Assert.Equal($"Pliki tymczasowe systemu: pominięto – brak folderu {SysTemp}.", Line(result, "Pliki tymczasowe systemu"));
        Assert.Equal($"Pliki tymczasowe konta: pominięto – brak folderu {UserTemp}.", Line(result, "Pliki tymczasowe konta"));
        Assert.Equal($"Miniatury konta: pominięto – brak folderu {Explorer}.", Line(result, "Miniatury konta"));
        Assert.DoesNotContain("DeleteOld:" + SysTemp, f.Events);
    }

    [Fact]
    public void ThumbnailsLocked_CountedInLine()
    {
        var f = new Fixture();
        f.Files.NamedFiles[Explorer] = () => new OldFilesCleanup(13, 2, 0, 1 * Mb);

        var result = f.Run();

        Assert.Equal("Miniatury konta: wykonano (plików: 13, 1 MB; w użyciu zostało: 2).", Line(result, "Miniatury konta"));
        Assert.Equal(TweakOutcome.Executed, result.Outcome);
    }

    // ---- optymalizacja dostarczania ----

    [Fact]
    public void DoCmdletMissing_Skipped()
    {
        var f = new Fixture();
        f.DoResult = _ => FakeScriptRunner.Result(0, DiskCleanupScripts.MissingMarker);

        var result = f.Run();

        Assert.Equal(TweakOutcome.Executed, result.Outcome);
        Assert.Equal("Optymalizacja dostarczania: pominięto – w tym systemie nie ma polecenia Delete-DeliveryOptimizationCache.", Line(result, "Optymalizacja dostarczania"));
    }

    [Fact]
    public void DoScriptFails_Failed_WithStderr_OthersStillRun()
    {
        var f = new Fixture();
        f.DoResult = _ => new ScriptResult { ExitCode = 1, Errors = new[] { "Odmowa dostępu do pamięci DO" } };

        var result = f.Run();

        Assert.Equal(TweakOutcome.Failed, result.Outcome);
        Assert.Equal("Optymalizacja dostarczania: błąd – Odmowa dostępu do pamięci DO.", Line(result, "Optymalizacja dostarczania"));
        Assert.Contains("DeleteNamed:" + Explorer + "|thumbcache_*.db", f.Events);
    }

    [Fact]
    public void DoScriptWithoutMarker_NotTreatedAsDone()
    {
        var f = new Fixture();
        f.DoResult = _ => FakeScriptRunner.Result(0, "coś innego");

        var result = f.Run();

        Assert.Equal(TweakOutcome.Failed, result.Outcome);
        Assert.StartsWith("Optymalizacja dostarczania: błąd – ", Line(result, "Optymalizacja dostarczania"), StringComparison.Ordinal);
    }

    [Fact]
    public void DoScriptTimeout_Failed()
    {
        var f = new Fixture();
        f.DoResult = _ => throw new OperationException(OperationErrorKind.Timeout, "Skrypt przekroczył limit czasu.");

        var result = f.Run();

        Assert.Equal(TweakOutcome.Failed, result.Outcome);
        Assert.Equal("Optymalizacja dostarczania: błąd – Skrypt przekroczył limit czasu.", Line(result, "Optymalizacja dostarczania"));
    }

    // ---- pobrane aktualizacje ----

    [Fact]
    public void WuauservWontStop_DownloadSkipped_ServicesRestored()
    {
        var f = new Fixture();
        f.Services.FailStop("wuauserv");

        var result = f.Run();

        Assert.Equal(TweakOutcome.Executed, result.Outcome);
        Assert.DoesNotContain("DeleteOld:" + Download, f.Events);
        Assert.Equal(
            "Pobrane aktualizacje: pominięto – usługa wuauserv nie zatrzymała się (Zatrzymanie wuauserv nie powiodło się (fake)); usługi przywrócono.",
            Line(result, "Pobrane aktualizacje"));
        Assert.True(f.Services.IsRunning("wuauserv"));
        Assert.True(f.Services.IsRunning("bits"));
        Assert.DoesNotContain("StopService:bits", f.Events);
    }

    [Fact]
    public void BitsWontStop_WuauservRestarted_DownloadSkipped()
    {
        var f = new Fixture();
        f.Services.FailStop("bits");

        var result = f.Run();

        Assert.Equal(TweakOutcome.Executed, result.Outcome);
        Assert.DoesNotContain("DeleteOld:" + Download, f.Events);
        Assert.Equal(new[] { "StopService:wuauserv", "StopService:bits", "StartService:bits", "StartService:wuauserv" }, f.Services.MutationCalls);
        Assert.True(f.Services.IsRunning("wuauserv"));
        Assert.True(f.Services.IsRunning("bits"));
    }

    [Fact]
    public void ServicesNotRunningBefore_NotStoppedNorStarted()
    {
        var f = new Fixture();
        f.Services.Add("wuauserv", ServiceStartType.Manual, running: false);
        f.Services.Add("bits", ServiceStartType.AutomaticDelayed, running: false);

        var result = f.Run();

        Assert.Empty(f.Services.MutationCalls);
        Assert.Contains("DeleteOld:" + Download, f.Events);
        Assert.Equal("Pobrane aktualizacje: wykonano (plików: 8, 255 MB).", Line(result, "Pobrane aktualizacje"));
        Assert.False(f.Services.IsRunning("wuauserv"));
        Assert.False(f.Services.IsRunning("bits"));
    }

    [Fact]
    public void ServiceQueryDenied_DownloadSkipped_NothingStopped()
    {
        var f = new Fixture();
        f.Services.DenyQuery("bits");

        var result = f.Run();

        Assert.Equal(TweakOutcome.Executed, result.Outcome);
        Assert.Empty(f.Services.MutationCalls);
        Assert.DoesNotContain("DeleteOld:" + Download, f.Events);
        Assert.Equal("Pobrane aktualizacje: pominięto – nie udało się odczytać usługi bits (Odmowa dostępu (fake)); nic nie zatrzymano.", Line(result, "Pobrane aktualizacje"));
    }

    [Fact]
    public void DownloadFolderMissing_SkippedWithoutTouchingServices()
    {
        var f = new Fixture();
        f.Files.RemoveDirectory(Download);

        var result = f.Run();

        Assert.Equal(TweakOutcome.Executed, result.Outcome);
        Assert.Empty(f.Services.MutationCalls);
        Assert.Equal($"Pobrane aktualizacje: pominięto – brak folderu {Download}.", Line(result, "Pobrane aktualizacje"));
    }

    [Fact]
    public void ServiceDoesNotComeBack_Failed_NotRestoredListed()
    {
        var f = new Fixture();
        f.Services.FailStart("bits");

        var result = f.Run();

        Assert.Equal(TweakOutcome.Failed, result.Outcome);
        Assert.Equal(
            "Pobrane aktualizacje: błąd – usunięto pliki (plików: 8, 255 MB), ale nie udało się ponownie uruchomić usług: bits; uruchomiono ponownie: wuauserv.",
            Line(result, "Pobrane aktualizacje"));
        Assert.Equal(new[] { "Usługa bits nie działa po przywróceniu: Uruchomienie bits nie powiodło się (fake)." }, result.NotRestored);
        Assert.True(f.Services.IsRunning("wuauserv"));
    }

    [Fact]
    public void DownloadFolderSkippedByLink_ServicesNotRestarted_FailedWithBothFacts()
    {
        var f = new Fixture();
        f.Files.OldFiles[Download] = _ => new OldFilesCleanup(0, 0, 0, 0, "folder jest dowiązaniem");
        f.Services.FailStart("bits");

        var result = f.Run();

        Assert.Equal(TweakOutcome.Failed, result.Outcome);
        Assert.Equal(
            "Pobrane aktualizacje: błąd – pominięto – folder jest dowiązaniem, a potem nie udało się ponownie uruchomić usług: bits; uruchomiono ponownie: wuauserv.",
            Line(result, "Pobrane aktualizacje"));
    }

    [Fact]
    public void StopFailsAndRestoreFails_Failed()
    {
        var f = new Fixture();
        f.Services.FailStop("bits");
        f.Services.FailStart("wuauserv");

        var result = f.Run();

        Assert.Equal(TweakOutcome.Failed, result.Outcome);
        Assert.StartsWith("Pobrane aktualizacje: błąd – usługa bits nie zatrzymała się", Line(result, "Pobrane aktualizacje"), StringComparison.Ordinal);
        Assert.Contains("wuauserv", Line(result, "Pobrane aktualizacje"), StringComparison.Ordinal);
        Assert.Single(result.NotRestored);
    }

    [Fact]
    public void DownloadDeleteThrowsUnexpected_ServicesStillRestored_Failed_OthersRun()
    {
        var f = new Fixture();
        f.Files.FailingCleanups[Download] = new InvalidOperationException("nieoczekiwany");

        var result = f.Run();

        Assert.Equal(TweakOutcome.Failed, result.Outcome);
        Assert.True(f.Services.IsRunning("wuauserv"));
        Assert.True(f.Services.IsRunning("bits"));
        Assert.StartsWith("Pobrane aktualizacje: błąd – nieoczekiwany", Line(result, "Pobrane aktualizacje"), StringComparison.Ordinal);
        Assert.Contains("usługi uruchomiono ponownie: bits, wuauserv", Line(result, "Pobrane aktualizacje"), StringComparison.Ordinal);
        Assert.Contains("run:dism.exe /Online /Cleanup-Image /StartComponentCleanup", f.Events);
    }

    // ---- magazyn składników ----

    [Fact]
    public void NeverPassesResetBase()
    {
        var f = new Fixture();

        f.Run();

        var call = Assert.Single(f.LongTools.Calls);
        Assert.Equal("dism.exe", call.Request.FileName);
        Assert.Equal("/Online /Cleanup-Image /StartComponentCleanup", call.Request.Arguments);
        Assert.DoesNotContain("ResetBase", call.Request.Arguments, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(TimeSpan.FromSeconds(1200), call.WaitLimit);
    }

    [Fact]
    public void ComponentCleanupTimeout_NotConfirmed()
    {
        var f = new Fixture();
        f.Dism = _ => new ServicingToolRun(null, true, DismLines.Take(3).ToArray());

        var result = f.Run();

        Assert.Equal(TweakOutcome.NotConfirmed, result.Outcome);
        Assert.False(result.RequiresReboot);
        Assert.Equal(
            @"Magazyn składników: nadal działa w tle po 20 min – program przestał czekać, a DISM pracuje dalej. Wynik sprawdzisz w %SystemRoot%\Logs\DISM\dism.log.",
            Line(result, "Magazyn składników"));
        Assert.Contains("DeleteNamed:" + Explorer + "|thumbcache_*.db", f.Events);
    }

    [Fact]
    public void ComponentCleanupTimeout_AfterEarlierError_Failed()
    {
        var f = new Fixture();
        f.Files.FailingCleanups[SysTemp] = new OperationException(OperationErrorKind.AccessDenied, "Brak dostępu do C:\\Windows\\Temp.");
        f.Dism = _ => new ServicingToolRun(null, true, Array.Empty<string>());

        var result = f.Run();

        Assert.Equal(TweakOutcome.Failed, result.Outcome);
        Assert.Contains("nadal działa w tle", Line(result, "Magazyn składników"), StringComparison.Ordinal);
    }

    [Fact]
    public void ComponentCleanupErrorCode_Failed_WithHexAndLog()
    {
        var f = new Fixture();
        f.Dism = _ => new ServicingToolRun(unchecked((int)0x800F0806), false, new[] { "Error: 0x800f0806" });

        var result = f.Run();

        Assert.Equal(TweakOutcome.Failed, result.Outcome);
        Assert.Equal(@"Magazyn składników: błąd 0x800F0806 – szczegóły: %SystemRoot%\Logs\DISM\dism.log.", Line(result, "Magazyn składników"));
        Assert.Contains("DeleteNamed:" + Explorer + "|thumbcache_*.db", f.Events);
    }

    [Fact]
    public void ComponentCleanup3010_ExecutedWithReboot()
    {
        var f = new Fixture();
        f.Dism = _ => new ServicingToolRun(3010, false, DismLines.ToArray());

        var result = f.Run();

        Assert.Equal(TweakOutcome.Executed, result.Outcome);
        Assert.True(result.RequiresReboot);
        Assert.Equal("Magazyn składników: wykonano – dokończy się po ponownym uruchomieniu komputera (kod 3010).", Line(result, "Magazyn składników"));
    }

    [Fact]
    public void DismDoesNotStart_Failed()
    {
        var f = new Fixture();
        f.Dism = _ => throw new OperationException(OperationErrorKind.NotFound, "Brak pliku dism.exe.");

        var result = f.Run();

        Assert.Equal(TweakOutcome.Failed, result.Outcome);
        Assert.Equal("Magazyn składników: błąd – nie udało się uruchomić DISM: Brak pliku dism.exe.", Line(result, "Magazyn składników"));
    }

    [Fact]
    public void DismNoExitCodeWithoutTimeout_Failed_NotReportedAsBackground()
    {
        var f = new Fixture();
        f.Dism = _ => new ServicingToolRun(null, false, Array.Empty<string>());

        var result = f.Run();

        Assert.Equal(TweakOutcome.Failed, result.Outcome);
        Assert.DoesNotContain("w tle", Line(result, "Magazyn składników"), StringComparison.Ordinal);
    }

    // ---- błąd kroku, zatrzymanie ----

    [Fact]
    public void StepError_OthersStillRun_Failed()
    {
        var f = new Fixture();
        f.Files.FailingCleanups[SysTemp] = new OperationException(OperationErrorKind.AccessDenied, "Brak dostępu do C:\\Windows\\Temp.");

        var result = f.Run();

        Assert.Equal(TweakOutcome.Failed, result.Outcome);
        Assert.False(result.RequiresReboot);
        Assert.Equal(@"Pliki tymczasowe systemu: błąd – Brak dostępu do C:\Windows\Temp.", Line(result, "Pliki tymczasowe systemu"));
        Assert.Contains("DeleteOld:" + UserTemp, f.Events);
        Assert.Contains("script:DO", f.Events);
        Assert.Contains("DeleteOld:" + Download, f.Events);
        Assert.Contains("run:dism.exe /Online /Cleanup-Image /StartComponentCleanup", f.Events);
        Assert.Contains("DeleteNamed:" + Explorer + "|thumbcache_*.db", f.Events);
        Assert.Equal(7, Lines(result).Length);
    }

    [Fact]
    public void UnexpectedExceptionInStep_Failed_OthersStillRun()
    {
        var f = new Fixture();
        f.Files.FailingCleanups[UserTemp] = new InvalidOperationException("coś pękło");

        var result = f.Run();

        Assert.Equal(TweakOutcome.Failed, result.Outcome);
        Assert.Equal("Pliki tymczasowe konta: błąd – coś pękło.", Line(result, "Pliki tymczasowe konta"));
        Assert.Contains("DeleteNamed:" + Explorer + "|thumbcache_*.db", f.Events);
        Assert.Contains(f.Log.Lines, l => l.StartsWith("ERROR", StringComparison.Ordinal));
    }

    [Fact]
    public void StopBetweenSteps()
    {
        var f = new Fixture();
        using var cts = new CancellationTokenSource();
        f.Progress.OnReport = p =>
        {
            if (p.StepIndex == 2 && p.Percent is null)
            {
                cts.Cancel();
            }
        };

        var result = f.Run(cts.Token);

        Assert.Equal(TweakOutcome.NotConfirmed, result.Outcome);
        Assert.Contains("script:DO", f.Events);
        Assert.Empty(f.Services.MutationCalls);
        Assert.Empty(f.LongTools.Calls);
        Assert.DoesNotContain(f.Events, e => e.StartsWith("DeleteNamed:", StringComparison.Ordinal));
        foreach (var stage in Stages.Skip(3))
        {
            Assert.Equal(stage + ": pominięto – zatrzymano.", Line(result, stage));
        }

        Assert.StartsWith("Optymalizacja dostarczania: wykonano", Line(result, "Optymalizacja dostarczania"), StringComparison.Ordinal);
        Assert.Equal(7, Lines(result).Length);
        Assert.Equal(2, f.Disk.Calls.Count);
    }

    [Fact]
    public void StopBeforeFirstStep_NothingDone_NotConfirmed()
    {
        var f = new Fixture();
        using var cts = new CancellationTokenSource();
        cts.Cancel();

        var result = f.Run(cts.Token);

        Assert.Equal(TweakOutcome.NotConfirmed, result.Outcome);
        Assert.Empty(f.Events);
        Assert.All(Stages, stage => Assert.Equal(stage + ": pominięto – zatrzymano.", Line(result, stage)));
    }

    [Fact]
    public void StopBetweenSteps_AfterStepError_Failed()
    {
        var f = new Fixture();
        f.Files.FailingCleanups[SysTemp] = new OperationException(OperationErrorKind.AccessDenied, "Brak dostępu.");
        using var cts = new CancellationTokenSource();
        f.Progress.OnReport = p =>
        {
            if (p.StepIndex == 1 && p.Percent is null)
            {
                cts.Cancel();
            }
        };

        var result = f.Run(cts.Token);

        // R20: odczytana porażka wcześniejszego kroku wygrywa z „zatrzymano”.
        Assert.Equal(TweakOutcome.Failed, result.Outcome);
        Assert.Equal("Pobrane aktualizacje: pominięto – zatrzymano.", Line(result, "Pobrane aktualizacje"));
    }

    // ---- zakres (spec 5.2) ----

    [Fact]
    public void NeverTouchesRecycleBinDownloadsDumps()
    {
        var f = new Fixture();

        f.Run();

        var touched = f.Files.Calls
            .Concat(f.Files.ExistenceQueries)
            .Concat(f.LongTools.Calls.Select(c => c.Request.FileName + " " + c.Request.Arguments))
            .Concat(f.Scripts.Calls.Select(c => c.Body))
            .ToList();
        Assert.NotEmpty(touched);
        foreach (var forbidden in new[] { "$Recycle.Bin", @"\Downloads", ".dmp", "Minidump", "Windows.old", "ResetBase", "cleanmgr" })
        {
            Assert.DoesNotContain(touched, t => t.IndexOf(forbidden, StringComparison.OrdinalIgnoreCase) >= 0);
        }

        Assert.Equal(new[] { SysTemp, UserTemp, Download }, f.Files.Calls.Where(c => c.StartsWith("DeleteOld:", StringComparison.Ordinal)).Select(c => c.Substring("DeleteOld:".Length)));
    }
}
