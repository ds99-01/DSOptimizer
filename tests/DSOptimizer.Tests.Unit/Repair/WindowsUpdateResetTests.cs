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
/// Reset Windows Update (repair.wu-reset / repair.wu-reset-full) wyłącznie na fake'ach: usługi, foldery, rejestr, reg.exe i domena
/// są w pamięci, nic nie dotyka hosta. Stan startowy = świeża 26H2 z pomiaru (Task 2, sekcja 3).
/// </summary>
public class WindowsUpdateResetTests
{
    private const string Win = @"C:\Windows";
    private const string Sd = Win + @"\SoftwareDistribution";
    private const string Cr = Win + @"\System32\catroot2";
    private const string Ts = "20261022-143005";
    private const string SdCopy = Sd + ".dso-" + Ts;
    private const string CrCopy = Cr + ".dso-" + Ts;
    private const string Backups = @"C:\ProgramData\DSOptimizer\backups";
    private const string RegFile = Backups + @"\wu-policies-" + Ts + ".reg";
    private const string ServicesFile = Backups + @"\wu-services-" + Ts + ".txt";
    private const string PolicyKey = @"HKLM\SOFTWARE\Policies\Microsoft\Windows\WindowsUpdate";
    private const string HistoryLine = "Historia aktualizacji w Ustawieniach zostanie wyczyszczona (zostać mogą tylko wpisy definicji Microsoft Defender); aktualizacje pobiorą się od nowa.";
    private const string MdmWarning = "Na komputerze zarządzanym przez Intune/MDM usunięte polityki mogą wrócić przy następnej synchronizacji.";

    private sealed class Fixture
    {
        public Fixture()
        {
            Services.Add("wuauserv", ServiceStartType.Manual, running: true);
            Services.Add("bits", ServiceStartType.AutomaticDelayed, running: true);
            Services.Add("cryptsvc", ServiceStartType.Automatic, running: true);
            Services.Add("UsoSvc", ServiceStartType.AutomaticDelayed, running: true);
            Services.Add("AppIDSvc", ServiceStartType.Manual, running: true);
            Services.Add("applockerfltr", ServiceStartType.Manual, running: true);
            Services.Add("IsolationSession", ServiceStartType.Manual, running: false);
            Services.Add("msiserver", ServiceStartType.Manual, running: false);
            Services.Add("WaaSMedicSvc", ServiceStartType.Manual, running: false);
            Services.Add("DoSvc", ServiceStartType.Manual, running: false);
            Services.SetDependents("cryptsvc", "applockerfltr", "AppIDSvc", "IsolationSession");
            Services.StartsAlong("AppIDSvc", "applockerfltr");

            Files.AddDirectory(Sd);
            Files.AddDirectory(Cr);

            Tools.OnRun = request =>
            {
                if (request.FileName == "reg.exe" && request.Arguments.StartsWith("export ", StringComparison.Ordinal))
                {
                    var file = request.Arguments.Split('"')[3];
                    Files.AddFile(file, "Windows Registry Editor Version 5.00\r\n[HKEY_LOCAL_MACHINE\\SOFTWARE\\Policies\\Microsoft\\Windows\\WindowsUpdate]\r\n\"WUServer\"=\"x\"\r\n");
                }

                return new ToolResult { ExitCode = 0, Output = new[] { "Operacja zakończona pomyślnie." } };
            };

            Services.Observer = Events.Add;
            Files.Observer = Events.Add;
            Tools.Observer = Events.Add;
            Registry.Observer = Events.Add;
        }

        public FakeServiceManager Services { get; } = new();

        public FakeRegistry Registry { get; } = new();

        public FakeToolRunner Tools { get; } = new();

        public FakeRepairFileSystem Files { get; } = new();

        public FakeDomainInfo Domain { get; } = new();

        public FakeServicingToolRunner LongTools { get; } = new();

        public FakeDiskSpace DiskSpace { get; } = new();

        public FakeServicingActivity Servicing { get; } = new();

        public FakeScriptRunner Scripts { get; } = new();

        public FakeUserContext User { get; } = new();

        public FakeClock Clock { get; } = new() { UtcNow = new DateTimeOffset(2026, 10, 22, 14, 30, 5, TimeSpan.Zero) };

        public ListLog Log { get; } = new();

        public ListProgress Progress { get; } = new();

        public List<string> Events { get; } = new();

        public int EnsureCalls { get; private set; }

        public Func<string>? EnsureOverride { get; set; }

        public void AddTestPolicies()
        {
            Registry.Set(PolicyKey, "WUServer", RegValue.FromString("http://dso-wsus.invalid:8530"));
            Registry.Set(PolicyKey, "WUStatusServer", RegValue.FromString("http://dso-wsus.invalid:8530"));
            Registry.Set(PolicyKey, "DisableWindowsUpdateAccess", RegValue.FromDWord(1));
            Registry.Set(PolicyKey, "SetDisableUXWUAccess", RegValue.FromDWord(1));
            Registry.Set(PolicyKey + @"\AU", "NoAutoUpdate", RegValue.FromDWord(1));
            Registry.Set(PolicyKey + @"\AU", "UseWUServer", RegValue.FromDWord(1));
        }

        public WindowsUpdateResetHandler Handler(bool full) => new(full, new RepairServices
        {
            Services = Services,
            Registry = Registry,
            Tools = Tools,
            Files = Files,
            Domain = Domain,
            Clock = Clock,
            Log = Log,
            LongTools = LongTools,
            DiskSpace = DiskSpace,
            Servicing = Servicing,
            Scripts = Scripts,
            User = User,
            TimeZone = TimeZoneInfo.Utc,
            Paths = new RepairPaths
            {
                SystemRoot = Win,
                EnsureBackupDirectory = () =>
                {
                    EnsureCalls++;
                    return EnsureOverride?.Invoke() ?? Backups;
                },
            },
        });

        public ActionHandlerResult Run(bool full, CancellationToken cancellationToken = default)
            => Handler(full).Run(TestTweaks.Action() with { Script = null, Handler = full ? ActionHandlerNames.WuResetFull : ActionHandlerNames.WuReset }, Progress, null, cancellationToken);

        public bool AllRunning(params string[] names) => names.All(Services.IsRunning);
    }

    private static string[] Lines(ActionHandlerResult result) => (result.Message ?? string.Empty).Split(new[] { Environment.NewLine }, StringSplitOptions.None);

    // ---- nazwy ----

    [Fact]
    public void Handler_Names_MatchCatalogHandlers()
    {
        var fixture = new Fixture();
        Assert.Equal(ActionHandlerNames.WuReset, fixture.Handler(false).Name);
        Assert.Equal(ActionHandlerNames.WuResetFull, fixture.Handler(true).Name);
    }

    // ---- ścieżka szczęśliwa ----

    [Fact]
    public void Basic_HappyPath_StopsMovesAndRestartsInOrder()
    {
        var fixture = new Fixture();

        var result = fixture.Run(full: false);

        Assert.Equal(TweakOutcome.Executed, result.Outcome);
        Assert.Equal(
            new[]
            {
                "StopService:wuauserv",
                "StopService:bits",
                "StopServiceWithDependents:cryptsvc",
                $"Move:{Sd}->{SdCopy}",
                $"Move:{Cr}->{CrCopy}",
                "StartService:cryptsvc",
                "StartService:AppIDSvc",
                "StartService:applockerfltr",
                "StartService:bits",
                "StartService:wuauserv",
            },
            fixture.Events);
        Assert.True(fixture.AllRunning("wuauserv", "bits", "cryptsvc", "AppIDSvc", "applockerfltr", "UsoSvc"));
        Assert.False(fixture.Services.IsRunning("IsolationSession"));
        Assert.Contains(SdCopy, fixture.Files.Directories);
        Assert.Contains(CrCopy, fixture.Files.Directories);
        Assert.Empty(fixture.Tools.Calls);
        Assert.Equal(0, fixture.EnsureCalls);
        Assert.Equal(0, fixture.Domain.Calls);
        Assert.Empty(result.NotRestored);
        Assert.Empty(result.Warnings);
        Assert.False(result.RequiresReboot);
    }

    [Fact]
    public void Basic_HappyPath_MessageListsStepsCopiesAndHistoryWarning()
    {
        var fixture = new Fixture();

        var result = fixture.Run(full: false);

        Assert.Equal(
            new[]
            {
                "Zresetowano składniki Windows Update.",
                "Zatrzymano usługę wuauserv.",
                "Zatrzymano usługę bits.",
                "Zatrzymano usługę cryptsvc razem z zależnymi: applockerfltr, AppIDSvc.",
                $"Przeniesiono {Sd} → {SdCopy}.",
                $"Przeniesiono {Cr} → {CrCopy}.",
                "Uruchomiono ponownie: cryptsvc, AppIDSvc, applockerfltr, bits, wuauserv.",
                "Odczyt zwrotny: usługi działają jak przed resetem, kopie folderów istnieją.",
                HistoryLine,
            },
            Lines(result));
        Assert.Equal(Lines(result).Skip(1).ToArray(), fixture.Progress.Items.ToArray());
    }

    [Fact]
    public void Full_HappyPath_ExportsThenDeletesPolicies_RestoresServices_ThenSetsStartTypes()
    {
        var fixture = new Fixture();
        fixture.AddTestPolicies();
        fixture.Services.Add("wuauserv", ServiceStartType.Automatic, running: true);
        fixture.Services.Add("UsoSvc", ServiceStartType.Disabled, running: false);

        var result = fixture.Run(full: true);

        Assert.Equal(TweakOutcome.Executed, result.Outcome);
        Assert.Equal(
            new[]
            {
                $"Tool:reg.exe export \"{PolicyKey}\" \"{RegFile}\" /y",
                "StopService:wuauserv",
                "StopService:bits",
                "StopServiceWithDependents:cryptsvc",
                $"Move:{Sd}->{SdCopy}",
                $"Move:{Cr}->{CrCopy}",
                $"DeleteKeyTree:{PolicyKey}",
                "StartService:cryptsvc",
                "StartService:AppIDSvc",
                "StartService:applockerfltr",
                "StartService:bits",
                "StartService:wuauserv",
                $"Write:{ServicesFile}",
                "SetStartType:wuauserv:Manual",
                "SetStartType:UsoSvc:AutomaticDelayed",
            },
            fixture.Events);
        Assert.Equal(ReadStatus.NotFound, fixture.Registry.KeyStatus(PolicyKey));
        Assert.Equal(ReadStatus.NotFound, fixture.Registry.KeyStatus(PolicyKey + @"\AU"));
        Assert.Equal(ServiceStartType.Manual, fixture.Services.StartTypeOf("wuauserv"));
        Assert.Equal(ServiceStartType.AutomaticDelayed, fixture.Services.StartTypeOf("UsoSvc"));
        Assert.False(fixture.Services.IsRunning("UsoSvc"));
        Assert.True(fixture.AllRunning("wuauserv", "bits", "cryptsvc", "AppIDSvc", "applockerfltr"));
        Assert.Equal(1, fixture.Domain.Calls);

        var request = fixture.Tools.Calls.Single();
        Assert.Equal("reg.exe", request.FileName);

        var saved = fixture.Files.FileText(ServicesFile)!;
        Assert.Contains("wuauserv: Automatic", saved, StringComparison.Ordinal);
        Assert.Contains("bits: AutomaticDelayed", saved, StringComparison.Ordinal);
        Assert.Contains("cryptsvc: Automatic", saved, StringComparison.Ordinal);
        Assert.Contains("UsoSvc: Disabled", saved, StringComparison.Ordinal);

        var lines = Lines(result);
        Assert.Equal("Zresetowano składniki Windows Update i usunięto blokady aktualizacji, jeśli były.", lines[0]);
        Assert.Contains($"Polityki Windows Update usunięte (z podkluczem AU); kopia: {RegFile}. Przywrócenie: reg import \"{RegFile}\".", lines);
        Assert.Contains($"Typy startu sprzed zapisane w {ServicesFile}.", lines);
        Assert.Contains("Typ startu wuauserv: Automatic → Manual.", lines);
        Assert.Contains("Typ startu UsoSvc: Disabled → AutomaticDelayed.", lines);
        Assert.Contains("Typ startu bez zmian: bits, cryptsvc.", lines);
        Assert.Equal(HistoryLine, lines[lines.Length - 1]);
        Assert.Equal(new[] { MdmWarning }, result.Warnings);
    }

    [Fact]
    public void Full_StartTypeTable_IsSameForEveryEdition_AndProtectedServicesNeverTouched()
    {
        var fixture = new Fixture();
        foreach (var name in new[] { "wuauserv", "bits", "cryptsvc", "UsoSvc" })
        {
            fixture.Services.Add(name, ServiceStartType.Disabled, running: false);
        }

        fixture.Services.Add("msiserver", ServiceStartType.Disabled, running: false);
        fixture.Services.Add("WaaSMedicSvc", ServiceStartType.Disabled, running: false);
        fixture.Services.Add("DoSvc", ServiceStartType.Disabled, running: false);

        var result = fixture.Run(full: true);

        Assert.Equal(TweakOutcome.Executed, result.Outcome);
        Assert.Equal(ServiceStartType.Manual, fixture.Services.StartTypeOf("wuauserv"));
        Assert.Equal(ServiceStartType.AutomaticDelayed, fixture.Services.StartTypeOf("bits"));
        Assert.Equal(ServiceStartType.Automatic, fixture.Services.StartTypeOf("cryptsvc"));
        Assert.Equal(ServiceStartType.AutomaticDelayed, fixture.Services.StartTypeOf("UsoSvc"));
        Assert.DoesNotContain(fixture.Services.MutationCalls, c => c.Contains("msiserver") || c.Contains("WaaSMedicSvc") || c.Contains("DoSvc"));
        Assert.Contains(result.Warnings, w => w.StartsWith("Usługa msiserver jest wyłączona (Disabled)", StringComparison.Ordinal));
        Assert.Contains(result.Warnings, w => w.StartsWith("Usługa WaaSMedicSvc jest wyłączona (Disabled)", StringComparison.Ordinal));
        Assert.Contains(result.Warnings, w => w.StartsWith("Usługa DoSvc jest wyłączona (Disabled)", StringComparison.Ordinal));
    }

    [Theory]
    [InlineData(ServiceStartType.Manual)]
    [InlineData(ServiceStartType.AutomaticDelayed)]
    [InlineData(ServiceStartType.Automatic)]
    public void ProtectedServices_NotDisabled_AreNotMentioned(ServiceStartType startType)
    {
        var fixture = new Fixture();
        fixture.Services.Add("DoSvc", startType, running: false);

        var result = fixture.Run(full: true);

        Assert.DoesNotContain(result.Warnings, w => w.Contains("DoSvc"));
        Assert.DoesNotContain("DoSvc", result.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Basic_ProtectedServiceDisabled_IsReportedAsWarning()
    {
        var fixture = new Fixture();
        fixture.Services.Add("msiserver", ServiceStartType.Disabled, running: false);

        var result = fixture.Run(full: false);

        Assert.Equal(TweakOutcome.Executed, result.Outcome);
        Assert.Contains(result.Warnings, w => w.StartsWith("Usługa msiserver jest wyłączona (Disabled)", StringComparison.Ordinal));
        Assert.DoesNotContain(fixture.Services.MutationCalls, c => c.Contains("msiserver"));
    }

    // ---- Review Focus 1: zatrzymanie ----

    [Fact]
    public void StopFails_NoFolderTouched_ServicesRestored()
    {
        var fixture = new Fixture();
        fixture.Services.FailStop("bits");

        var result = fixture.Run(full: false);

        Assert.Equal(TweakOutcome.Failed, result.Outcome);
        Assert.StartsWith("Nie udało się zatrzymać usługi bits: ", result.Message, StringComparison.Ordinal);
        Assert.Contains("Nic nie zmieniono.", Lines(result)[0], StringComparison.Ordinal);
        Assert.Empty(fixture.Files.Calls);
        Assert.Equal(new[] { "StopService:wuauserv", "StopService:bits", "StartService:bits", "StartService:wuauserv" }, fixture.Services.MutationCalls);
        Assert.True(fixture.AllRunning("wuauserv", "bits", "cryptsvc", "AppIDSvc", "applockerfltr"));
        Assert.Empty(result.NotRestored);
    }

    [Fact]
    public void DependentStopFails_CatrootBlocked_NothingMoved_AllRestarted()
    {
        var fixture = new Fixture();
        fixture.Services.FailStop("AppIDSvc");

        var result = fixture.Run(full: false);

        Assert.Equal(TweakOutcome.Failed, result.Outcome);
        Assert.StartsWith("Nie udało się zatrzymać usługi cryptsvc: ", result.Message, StringComparison.Ordinal);
        Assert.Contains("Nic nie zmieniono.", Lines(result)[0], StringComparison.Ordinal);
        Assert.Empty(fixture.Files.Calls);
        Assert.True(fixture.AllRunning("wuauserv", "bits", "cryptsvc", "AppIDSvc", "applockerfltr"));
        Assert.Empty(result.NotRestored);
    }

    [Fact]
    public void DependentsQueryFails_NothingMoved_ServicesRestored()
    {
        var fixture = new Fixture();
        fixture.Services.FailDependentsQuery("cryptsvc");

        var result = fixture.Run(full: false);

        Assert.Equal(TweakOutcome.Failed, result.Outcome);
        Assert.StartsWith("Nie udało się zatrzymać usługi cryptsvc: ", result.Message, StringComparison.Ordinal);
        Assert.Empty(fixture.Files.Calls);
        Assert.DoesNotContain("StopServiceWithDependents:cryptsvc", fixture.Services.MutationCalls);
        Assert.True(fixture.AllRunning("wuauserv", "bits", "cryptsvc"));
    }

    [Fact]
    public void StopFails_AndRestartFails_ReportedInNotRestored()
    {
        var fixture = new Fixture();
        fixture.Services.FailStop("bits");
        fixture.Services.FailStart("wuauserv");

        var result = fixture.Run(full: false);

        Assert.Equal(TweakOutcome.Failed, result.Outcome);
        Assert.DoesNotContain("Nic nie zmieniono.", result.Message, StringComparison.Ordinal);
        Assert.Contains("wuauserv", Assert.Single(result.NotRestored), StringComparison.Ordinal);
        Assert.Empty(fixture.Files.Calls);
    }

    [Fact]
    public void ServiceStoppedBefore_StaysStopped()
    {
        var fixture = new Fixture();
        fixture.Services.Add("wuauserv", ServiceStartType.Manual, running: false);

        var result = fixture.Run(full: false);

        Assert.Equal(TweakOutcome.Executed, result.Outcome);
        Assert.DoesNotContain(fixture.Services.MutationCalls, c => c.EndsWith(":wuauserv", StringComparison.Ordinal));
        Assert.False(fixture.Services.IsRunning("wuauserv"));
        Assert.Contains("Usługa wuauserv nie działała – bez zmian.", Lines(result));
        Assert.Contains("Uruchomiono ponownie: cryptsvc, AppIDSvc, applockerfltr, bits.", Lines(result));
    }

    [Fact]
    public void CryptsvcWithoutRunningDependents_StoppedWithoutDependentList()
    {
        var fixture = new Fixture();
        fixture.Services.Add("AppIDSvc", ServiceStartType.Manual, running: false);
        fixture.Services.Add("applockerfltr", ServiceStartType.Manual, running: false);

        var result = fixture.Run(full: false);

        Assert.Equal(TweakOutcome.Executed, result.Outcome);
        Assert.Contains("Zatrzymano usługę cryptsvc.", Lines(result));
        Assert.False(fixture.Services.IsRunning("AppIDSvc"));
        Assert.DoesNotContain("StartService:AppIDSvc", fixture.Services.MutationCalls);
    }

    [Fact]
    public void ServiceQueryDenied_FailedBeforeAnyChange()
    {
        var fixture = new Fixture();
        fixture.Services.DenyQuery("cryptsvc");

        var result = fixture.Run(full: false);

        Assert.Equal(TweakOutcome.Failed, result.Outcome);
        Assert.StartsWith("Nie udało się odczytać usługi cryptsvc: ", result.Message, StringComparison.Ordinal);
        Assert.EndsWith("Nic nie zmieniono.", result.Message, StringComparison.Ordinal);
        Assert.Empty(fixture.Events);
    }

    [Fact]
    public void ServiceMissing_SkippedWithWarning()
    {
        var fixture = new Fixture();
        var services = new FakeServiceManager();
        foreach (var name in new[] { "wuauserv", "cryptsvc", "UsoSvc", "AppIDSvc", "applockerfltr" })
        {
            services.Add(name, fixture.Services.StartTypeOf(name), running: true);
        }

        services.SetDependents("cryptsvc", "applockerfltr", "AppIDSvc");
        var handler = new WindowsUpdateResetHandler(false, new RepairServices
        {
            Services = services,
            Registry = fixture.Registry,
            Tools = fixture.Tools,
            Files = fixture.Files,
            Domain = fixture.Domain,
            Clock = fixture.Clock,
            Log = fixture.Log,
            LongTools = fixture.LongTools,
            DiskSpace = fixture.DiskSpace,
            Servicing = fixture.Servicing,
            Scripts = fixture.Scripts,
            User = fixture.User,
            TimeZone = TimeZoneInfo.Utc,
            Paths = new RepairPaths { SystemRoot = Win, EnsureBackupDirectory = () => Backups },
        });

        var result = handler.Run(TestTweaks.Action(), null, null, CancellationToken.None);

        Assert.Equal(TweakOutcome.Executed, result.Outcome);
        Assert.Contains("Usługa bits nie istnieje – pominięta.", result.Warnings);
        Assert.DoesNotContain(services.MutationCalls, c => c.Contains("bits"));
    }

    [Fact]
    public void CancelledBeforeStart_NothingChanged()
    {
        var fixture = new Fixture();
        using var cts = new CancellationTokenSource();
        cts.Cancel();

        var result = fixture.Run(full: false, cts.Token);

        Assert.Equal(TweakOutcome.Failed, result.Outcome);
        Assert.Equal("Anulowano przed zmianami. Nic nie zmieniono.", result.Message);
        Assert.Empty(fixture.Events);
    }

    // ---- Review Focus 2: foldery, wszystko albo nic ----

    [Fact]
    public void FirstRenameFails_NothingMoved_ServicesRestored()
    {
        var fixture = new Fixture();
        fixture.Files.FailMove(Sd);

        var result = fixture.Run(full: false);

        Assert.Equal(TweakOutcome.Failed, result.Outcome);
        Assert.StartsWith($"Nie udało się przenieść folderu {Sd}: ", result.Message, StringComparison.Ordinal);
        Assert.Contains("Nic nie zmieniono.", Lines(result)[0], StringComparison.Ordinal);
        Assert.Contains(Sd, fixture.Files.Directories);
        Assert.True(fixture.AllRunning("wuauserv", "bits", "cryptsvc", "AppIDSvc", "applockerfltr"));
    }

    [Fact]
    public void SecondRenameFails_FirstRolledBack()
    {
        var fixture = new Fixture();
        fixture.Files.FailMove(Cr);

        var result = fixture.Run(full: false);

        Assert.Equal(TweakOutcome.Failed, result.Outcome);
        Assert.StartsWith($"Nie udało się przenieść folderu {Cr}: ", result.Message, StringComparison.Ordinal);
        Assert.Contains($"Cofnięto przeniesienie {Sd}", Lines(result)[0], StringComparison.Ordinal);
        Assert.Contains("Nic nie zmieniono.", Lines(result)[0], StringComparison.Ordinal);
        Assert.Contains(Sd, fixture.Files.Directories);
        Assert.Contains(Cr, fixture.Files.Directories);
        Assert.DoesNotContain(SdCopy, fixture.Files.Directories);
        Assert.Equal(
            new[] { $"Move:{Sd}->{SdCopy}", $"Move:{Cr}->{CrCopy}", $"Move:{SdCopy}->{Sd}" },
            fixture.Files.Calls);
        var rollback = fixture.Events.IndexOf($"Move:{SdCopy}->{Sd}");
        Assert.True(rollback < fixture.Events.IndexOf("StartService:cryptsvc"), "cofnięcie folderu przed startem usług");
        Assert.True(fixture.AllRunning("wuauserv", "bits", "cryptsvc", "AppIDSvc", "applockerfltr"));
        Assert.Empty(result.NotRestored);
    }

    [Fact]
    public void RollbackFails_ReportedInNotRestored()
    {
        var fixture = new Fixture();
        fixture.Files.FailMove(Cr);
        fixture.Files.FailMove(SdCopy);

        var result = fixture.Run(full: false);

        Assert.Equal(TweakOutcome.Failed, result.Outcome);
        Assert.Equal(
            $"Folder {Sd} jest pod nazwą {SdCopy}. Windows mógł już utworzyć nowy folder {Sd} – najpierw go przenieś albo usuń, potem przywróć nazwę. Kolejny reset w DSOptimizerze usunie kopię {SdCopy} jako starszą.",
            Assert.Single(result.NotRestored));
        Assert.DoesNotContain("Nic nie zmieniono.", result.Message, StringComparison.Ordinal);
        Assert.Contains($"zmień nazwę {SdCopy} z powrotem na {Sd}", result.Message, StringComparison.Ordinal);
        Assert.True(fixture.AllRunning("wuauserv", "bits", "cryptsvc"));
    }

    [Fact]
    public void UnexpectedExceptionDuringMove_StillRolledBackAndRestored()
    {
        var fixture = new Fixture();
        fixture.Files.FailMove(Cr, new InvalidOperationException("nieoczekiwany"));

        var result = fixture.Run(full: false);

        Assert.Equal(TweakOutcome.Failed, result.Outcome);
        Assert.Contains("nieoczekiwany", result.Message, StringComparison.Ordinal);
        Assert.Contains(Sd, fixture.Files.Directories);
        Assert.True(fixture.AllRunning("wuauserv", "bits", "cryptsvc"));
    }

    [Fact]
    public void BackupNameCollision_GetsSuffix()
    {
        var fixture = new Fixture();
        fixture.Files.AddDirectory(SdCopy);
        fixture.Files.AddDirectory(SdCopy + "-2");

        var result = fixture.Run(full: false);

        Assert.Equal(TweakOutcome.Executed, result.Outcome);
        Assert.Contains($"Move:{Sd}->{SdCopy}-3", fixture.Files.Calls);
        Assert.Contains($"Move:{Cr}->{CrCopy}", fixture.Files.Calls);
        Assert.Contains($"Przeniesiono {Sd} → {SdCopy}-3.", Lines(result));
        Assert.Contains(SdCopy + "-3", fixture.Files.Directories);
    }

    [Fact]
    public void SoftwareDistributionMissing_StepSkipped_CatrootStillMoved()
    {
        var fixture = new Fixture();
        fixture.Files.RemoveDirectory(Sd);

        var result = fixture.Run(full: false);

        Assert.Equal(TweakOutcome.Executed, result.Outcome);
        Assert.Contains($"Pominięto {Sd} – folder nie istnieje.", Lines(result));
        Assert.Equal(new[] { $"Move:{Cr}->{CrCopy}" }, fixture.Files.Calls);
    }

    [Fact]
    public void NoFolderToMove_FailedAfterRestore()
    {
        var fixture = new Fixture();
        fixture.Files.RemoveDirectory(Sd);
        fixture.Files.RemoveDirectory(Cr);

        var result = fixture.Run(full: false);

        Assert.Equal(TweakOutcome.Failed, result.Outcome);
        Assert.StartsWith("Nie znaleziono folderów Windows Update", result.Message, StringComparison.Ordinal);
        Assert.True(fixture.AllRunning("wuauserv", "bits", "cryptsvc"));
        Assert.DoesNotContain("Typy startu", result.Message, StringComparison.Ordinal); // podstawowy nie dotyka typów startu ani polityk
        Assert.DoesNotContain("Polityki", result.Message, StringComparison.Ordinal);
        Assert.Empty(result.Warnings);
    }

    private const string PoliciesAlreadyRemoved = "Polityki Windows Update zostały już usunięte (kopia: " + RegFile + "; przywrócenie: reg import \"" + RegFile + "\").";
    private const string StartTypesUnchanged = "Typy startu usług nie zostały zmienione.";

    [Fact]
    public void Full_NoFolderToMove_HeaderSaysPoliciesAlreadyRemoved_StartTypesUnchanged_AndMdmWarning()
    {
        var fixture = new Fixture();
        fixture.AddTestPolicies();
        fixture.Files.RemoveDirectory(Sd);
        fixture.Files.RemoveDirectory(Cr);

        var result = fixture.Run(full: true);

        Assert.Equal(TweakOutcome.Failed, result.Outcome);
        Assert.Equal(ReadStatus.NotFound, fixture.Registry.KeyStatus(PolicyKey));
        var header = Lines(result)[0];
        Assert.StartsWith("Nie znaleziono folderów Windows Update", header, StringComparison.Ordinal);
        Assert.Contains(PoliciesAlreadyRemoved, header, StringComparison.Ordinal);
        Assert.EndsWith(StartTypesUnchanged, header, StringComparison.Ordinal);
        Assert.DoesNotContain(fixture.Events, e => e.StartsWith("SetStartType:", StringComparison.Ordinal));
        Assert.Equal(new[] { MdmWarning }, result.Warnings);
    }

    [Fact]
    public void Full_ServiceNotRestored_HeaderSaysPoliciesAlreadyRemoved_StartTypesUnchanged_AndMdmWarning()
    {
        var fixture = new Fixture();
        fixture.AddTestPolicies();
        fixture.Services.FailStart("bits");

        var result = fixture.Run(full: true);

        Assert.Equal(TweakOutcome.Failed, result.Outcome);
        Assert.Equal(ReadStatus.NotFound, fixture.Registry.KeyStatus(PolicyKey));
        var header = Lines(result)[0];
        Assert.StartsWith("Nie udało się ponownie uruchomić usług: bits.", header, StringComparison.Ordinal);
        Assert.Contains(PoliciesAlreadyRemoved, header, StringComparison.Ordinal);
        Assert.EndsWith(StartTypesUnchanged, header, StringComparison.Ordinal);
        Assert.DoesNotContain(fixture.Events, e => e.StartsWith("SetStartType:", StringComparison.Ordinal));
        Assert.Equal(new[] { MdmWarning }, result.Warnings);
    }

    [Fact]
    public void Full_ServiceNotRestored_PolicyDeleteFailed_HeaderKeepsDeleteFailure_AndDoesNotClaimRemoval()
    {
        var fixture = new Fixture();
        fixture.AddTestPolicies();
        fixture.Services.FailStart("bits");
        var registry = new DelegatingRegistry(fixture.Registry)
        {
            OnDeleteKeyTree = _ => throw new OperationException(OperationErrorKind.AccessDenied, "drzewo zablokowane (fake)"),
        };

        var result = FullWithRegistry(fixture, registry).Run(TestTweaks.Action(), null, null, CancellationToken.None);

        Assert.Equal(TweakOutcome.Failed, result.Outcome);
        var header = Lines(result)[0];
        Assert.Contains("usunięcie klucza nie powiodło się", header, StringComparison.Ordinal);
        Assert.DoesNotContain("zostały już usunięte", header, StringComparison.Ordinal);
        Assert.EndsWith(StartTypesUnchanged, header, StringComparison.Ordinal);
    }

    [Fact]
    public void Full_NoFolderToMove_NoPolicies_SaysStartTypesUnchanged_WithoutClaimingPolicyRemoval()
    {
        var fixture = new Fixture();
        fixture.Files.RemoveDirectory(Sd);
        fixture.Files.RemoveDirectory(Cr);

        var result = fixture.Run(full: true);

        Assert.Equal(TweakOutcome.Failed, result.Outcome);
        var header = Lines(result)[0];
        Assert.DoesNotContain("zostały już usunięte", header, StringComparison.Ordinal);
        Assert.EndsWith(StartTypesUnchanged, header, StringComparison.Ordinal);
    }

    // ---- Review Focus 3: kopie ----

    [Fact]
    public void OldBackupsPruned_OnlyAfterSuccess()
    {
        var fixture = new Fixture();
        fixture.Files.AddDirectory(Sd + ".dso-20000101-000000");
        fixture.Files.AddDirectory(Cr + ".dso-20000101-000000-2");

        var result = fixture.Run(full: false);

        Assert.Equal(TweakOutcome.Executed, result.Outcome);
        Assert.DoesNotContain(Sd + ".dso-20000101-000000", fixture.Files.Directories);
        Assert.DoesNotContain(Cr + ".dso-20000101-000000-2", fixture.Files.Directories);
        Assert.Contains(SdCopy, fixture.Files.Directories);
        Assert.Contains(CrCopy, fixture.Files.Directories);
        Assert.Contains($"Usunięto starszą kopię {Sd}.dso-20000101-000000.", Lines(result));
        var firstDelete = fixture.Events.FindIndex(e => e.StartsWith("Delete:", StringComparison.Ordinal));
        Assert.True(firstDelete > fixture.Events.LastIndexOf("StartService:wuauserv"));
    }

    [Fact]
    public void OldBackupsKept_WhenResetFails()
    {
        var fixture = new Fixture();
        fixture.Files.AddDirectory(Sd + ".dso-20000101-000000");
        fixture.Files.FailMove(Cr);

        var result = fixture.Run(full: false);

        Assert.Equal(TweakOutcome.Failed, result.Outcome);
        Assert.Contains(Sd + ".dso-20000101-000000", fixture.Files.Directories);
        Assert.DoesNotContain(fixture.Files.Calls, c => c.StartsWith("Delete:", StringComparison.Ordinal));
    }

    [Fact]
    public void OldBackupsKept_WhenServiceNotRestored()
    {
        var fixture = new Fixture();
        fixture.Files.AddDirectory(Sd + ".dso-20000101-000000");
        fixture.Services.FailStart("bits");

        var result = fixture.Run(full: false);

        Assert.Equal(TweakOutcome.Failed, result.Outcome);
        Assert.Contains(Sd + ".dso-20000101-000000", fixture.Files.Directories);
        Assert.Contains("bits", Assert.Single(result.NotRestored), StringComparison.Ordinal);
    }

    [Fact]
    public void ForeignFoldersNeverDeleted()
    {
        var fixture = new Fixture();
        var foreign = new[]
        {
            Sd + ".old",
            Sd + ".dso-moje",
            Sd + ".dso-20000101-000000.bak",
            Sd + ".dso-2000-01-01",
            Sd + "2.dso-20000101-000000",
            Cr + ".bak",
            Win + @"\catroot2.dso-20000101-000000",
        };
        foreach (var folder in foreign)
        {
            fixture.Files.AddDirectory(folder);
        }

        var result = fixture.Run(full: false);

        Assert.Equal(TweakOutcome.Executed, result.Outcome);
        Assert.All(foreign, folder => Assert.Contains(folder, fixture.Files.Directories));
        Assert.DoesNotContain(fixture.Files.Calls, c => c.StartsWith("Delete:", StringComparison.Ordinal));
    }

    [Fact]
    public void PruneFailure_IsWarningOnly()
    {
        var fixture = new Fixture();
        fixture.Files.AddDirectory(Sd + ".dso-20000101-000000");
        fixture.Files.FailDelete(Sd + ".dso-20000101-000000");

        var result = fixture.Run(full: false);

        Assert.Equal(TweakOutcome.Executed, result.Outcome);
        Assert.Contains(result.Warnings, w => w.StartsWith($"Nie udało się usunąć starszej kopii {Sd}.dso-20000101-000000", StringComparison.Ordinal));
    }

    [Fact]
    public void PruneListFailure_IsWarningOnly()
    {
        var fixture = new Fixture();
        fixture.Files.ListFails = true;

        var result = fixture.Run(full: false);

        Assert.Equal(TweakOutcome.Executed, result.Outcome);
        Assert.Contains(result.Warnings, w => w.StartsWith("Nie udało się sprawdzić starszych kopii", StringComparison.Ordinal));
    }

    // ---- odczyt zwrotny i przywracanie ----

    [Fact]
    public void DriverStartReportsError_ButRunning_CountsAsRestored()
    {
        var fixture = new Fixture();
        fixture.Services.FailStart("applockerfltr", runsAnyway: true, kind: OperationErrorKind.Unexpected);

        var result = fixture.Run(full: false);

        Assert.Equal(TweakOutcome.Executed, result.Outcome);
        Assert.Empty(result.NotRestored);
        Assert.True(fixture.Services.IsRunning("applockerfltr"));
    }

    [Fact]
    public void ServiceStartFails_NotRestored_Failed()
    {
        var fixture = new Fixture();
        fixture.Services.FailStart("AppIDSvc");

        var result = fixture.Run(full: false);

        Assert.Equal(TweakOutcome.Failed, result.Outcome);
        Assert.Contains(result.NotRestored, n => n.StartsWith("Usługa AppIDSvc", StringComparison.Ordinal));
        Assert.Contains("AppIDSvc", Lines(result)[0], StringComparison.Ordinal);
        Assert.Contains(SdCopy, result.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Readback_CopyMissing_Failed()
    {
        var fixture = new Fixture();
        fixture.Files.AfterMove = (_, destination) =>
        {
            if (destination == CrCopy)
            {
                fixture.Files.RemoveDirectory(destination);
            }
        };

        var result = fixture.Run(full: false);

        Assert.Equal(TweakOutcome.Failed, result.Outcome);
        Assert.StartsWith("Odczyt zwrotny nie potwierdził resetu:", result.Message, StringComparison.Ordinal);
        Assert.Contains(CrCopy, Lines(result)[0], StringComparison.Ordinal);
    }

    // ---- Review Focus 4: wariant rozszerzony ----

    [Fact]
    public void Full_DomainJoined_Refused()
    {
        var fixture = new Fixture();
        fixture.Domain.Value = true;
        fixture.AddTestPolicies();

        var result = fixture.Run(full: true);

        Assert.Equal(TweakOutcome.Refused, result.Outcome);
        Assert.Equal("Komputer jest w domenie: polityki Windows Update ustawia administrator domeny.", result.Message);
        Assert.Empty(fixture.Events);
        Assert.Equal(ReadStatus.Found, fixture.Registry.KeyStatus(PolicyKey));
    }

    [Fact]
    public void Full_DomainUnknown_Refused()
    {
        var fixture = new Fixture();
        fixture.Domain.Value = null;

        var result = fixture.Run(full: true);

        Assert.Equal(TweakOutcome.Refused, result.Outcome);
        Assert.Equal("Nie udało się sprawdzić, czy komputer jest w domenie.", result.Message);
        Assert.Empty(fixture.Events);
    }

    [Fact]
    public void Basic_DomainJoined_NotChecked()
    {
        var fixture = new Fixture();
        fixture.Domain.Value = true;

        Assert.Equal(TweakOutcome.Executed, fixture.Run(full: false).Outcome);
        Assert.Equal(0, fixture.Domain.Calls);
    }

    [Fact]
    public void Full_NoPolicies_NoBackupFile()
    {
        var fixture = new Fixture();

        var result = fixture.Run(full: true);

        Assert.Equal(TweakOutcome.Executed, result.Outcome);
        Assert.Empty(fixture.Tools.Calls);
        Assert.Null(fixture.Files.FileText(RegFile));
        Assert.DoesNotContain(fixture.Events, e => e.StartsWith("DeleteKeyTree:", StringComparison.Ordinal));
        Assert.Contains("Brak polityk Windows Update.", Lines(result));
        Assert.Contains("Typ startu bez zmian: wuauserv, bits, cryptsvc, UsoSvc.", Lines(result));
        Assert.Contains(MdmWarning, result.Warnings);
    }

    [Theory]
    [InlineData(1, true)]
    [InlineData(0, false)]
    public void Full_ExportFails_PoliciesNotDeleted(int exitCode, bool writesFile)
    {
        var fixture = new Fixture();
        fixture.AddTestPolicies();
        fixture.Tools.OnRun = request =>
        {
            if (writesFile)
            {
                fixture.Files.AddFile(request.Arguments.Split('"')[3], "x");
            }

            return new ToolResult { ExitCode = exitCode, Output = new[] { "BŁĄD: odmowa (fake)" } };
        };

        var result = fixture.Run(full: true);

        Assert.Equal(TweakOutcome.Failed, result.Outcome);
        Assert.StartsWith("Nie udało się zapisać kopii polityk Windows Update", result.Message, StringComparison.Ordinal);
        Assert.Contains("polityki zostają bez zmian", result.Message, StringComparison.Ordinal);
        Assert.EndsWith(NothingChanged, result.Message, StringComparison.Ordinal);
        Assert.Single(Lines(result));
        AssertNothingChangedBeyondExport(fixture);
    }

    private const string NothingChanged = "Nic nie zmieniono.";

    /// <summary>R13: błąd na etapie kopii polityk = żadne zatrzymanie, przeniesienie, usunięcie ani zmiana typu startu.</summary>
    private static void AssertNothingChangedBeyondExport(Fixture fixture)
    {
        Assert.All(fixture.Events, e => Assert.StartsWith("Tool:reg.exe export ", e, StringComparison.Ordinal));
        Assert.Empty(fixture.Services.MutationCalls);
        Assert.Empty(fixture.Files.Calls);
        Assert.Equal(ReadStatus.Found, fixture.Registry.KeyStatus(PolicyKey));
        Assert.Equal(RegValue.FromDWord(1), fixture.Registry.Get(PolicyKey + @"\AU", "NoAutoUpdate"));
        Assert.Contains(Sd, fixture.Files.Directories);
        Assert.Contains(Cr, fixture.Files.Directories);
        Assert.True(fixture.AllRunning("wuauserv", "bits", "cryptsvc", "AppIDSvc", "applockerfltr"));
    }

    [Fact]
    public void Full_ExportRunsBeforeAnyServiceOrFolderChange()
    {
        var fixture = new Fixture();
        fixture.AddTestPolicies();

        fixture.Run(full: true);

        Assert.StartsWith("Tool:reg.exe export ", fixture.Events[0], StringComparison.Ordinal);
    }

    [Fact]
    public void Full_StopFailsAfterExport_RegFileKept_PoliciesUntouched()
    {
        var fixture = new Fixture();
        fixture.AddTestPolicies();
        fixture.Services.FailStop("bits");

        var result = fixture.Run(full: true);

        Assert.Equal(TweakOutcome.Failed, result.Outcome);
        Assert.StartsWith("Nie udało się zatrzymać usługi bits: ", result.Message, StringComparison.Ordinal);
        Assert.NotNull(fixture.Files.FileText(RegFile));
        Assert.Contains(RegFile, result.Message, StringComparison.Ordinal);
        Assert.Equal(ReadStatus.Found, fixture.Registry.KeyStatus(PolicyKey));
        Assert.DoesNotContain(fixture.Events, e => e.StartsWith("DeleteKeyTree:", StringComparison.Ordinal));
        Assert.Empty(fixture.Files.Calls);
    }

    [Fact]
    public void Full_SecondRenameFailsAfterExport_RegFileKept_PoliciesUntouched()
    {
        var fixture = new Fixture();
        fixture.AddTestPolicies();
        fixture.Files.FailMove(Cr);

        var result = fixture.Run(full: true);

        Assert.Equal(TweakOutcome.Failed, result.Outcome);
        Assert.NotNull(fixture.Files.FileText(RegFile));
        Assert.Equal(ReadStatus.Found, fixture.Registry.KeyStatus(PolicyKey));
        Assert.DoesNotContain(fixture.Events, e => e.StartsWith("DeleteKeyTree:", StringComparison.Ordinal));
        Assert.Contains(Sd, fixture.Files.Directories);
    }

    [Fact]
    public void Full_ExportThrows_PoliciesNotDeleted()
    {
        var fixture = new Fixture();
        fixture.AddTestPolicies();
        fixture.Tools.OnRun = _ => throw new OperationException(OperationErrorKind.Timeout, "reg.exe przekroczył czas (fake).");

        var result = fixture.Run(full: true);

        Assert.Equal(TweakOutcome.Failed, result.Outcome);
        Assert.Contains("reg.exe przekroczył czas", result.Message, StringComparison.Ordinal);
        Assert.EndsWith(NothingChanged, result.Message, StringComparison.Ordinal);
        AssertNothingChangedBeyondExport(fixture);
    }

    [Fact]
    public void Full_BackupDirectoryUnavailable_PoliciesNotDeleted()
    {
        var fixture = new Fixture();
        fixture.AddTestPolicies();
        fixture.EnsureOverride = () => throw new System.IO.IOException("katalog niezaufany (fake)");

        var result = fixture.Run(full: true);

        Assert.Equal(TweakOutcome.Failed, result.Outcome);
        Assert.Contains("katalog niezaufany", result.Message, StringComparison.Ordinal);
        Assert.EndsWith(NothingChanged, result.Message, StringComparison.Ordinal);
        Assert.Empty(fixture.Tools.Calls);
        AssertNothingChangedBeyondExport(fixture);
    }

    private static WindowsUpdateResetHandler FullWithRegistry(Fixture fixture, IRegistry registry) => new(true, new RepairServices
    {
        Services = fixture.Services,
        Registry = registry,
        Tools = fixture.Tools,
        Files = fixture.Files,
        Domain = fixture.Domain,
        Clock = fixture.Clock,
        Log = fixture.Log,
        LongTools = fixture.LongTools,
        DiskSpace = fixture.DiskSpace,
        Servicing = fixture.Servicing,
        Scripts = fixture.Scripts,
        User = fixture.User,
        TimeZone = TimeZoneInfo.Utc,
        Paths = new RepairPaths { SystemRoot = Win, EnsureBackupDirectory = () => Backups },
    });

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void Full_DeleteFails_FailedWithBackupPathAndImportHint(bool operationException)
    {
        var fixture = new Fixture();
        fixture.AddTestPolicies();
        var registry = new DelegatingRegistry(fixture.Registry)
        {
            OnDeleteKeyTree = _ => throw (operationException
                ? new OperationException(OperationErrorKind.AccessDenied, "drzewo zablokowane (fake)")
                : new UnauthorizedAccessException("drzewo zablokowane (fake)")),
        };

        var result = FullWithRegistry(fixture, registry).Run(TestTweaks.Action(), null, null, CancellationToken.None);

        Assert.Equal(TweakOutcome.Failed, result.Outcome);
        Assert.StartsWith($"Kopia polityk zapisana w {RegFile}, ale usunięcie klucza nie powiodło się", result.Message, StringComparison.Ordinal);
        Assert.Contains("drzewo zablokowane", Lines(result)[0], StringComparison.Ordinal);
        Assert.Contains($"reg import \"{RegFile}\"", Lines(result)[0], StringComparison.Ordinal);
        Assert.True(fixture.AllRunning("wuauserv", "bits", "cryptsvc"));
        Assert.DoesNotContain(fixture.Services.MutationCalls, c => c.StartsWith("SetStartType", StringComparison.Ordinal));
    }

    [Fact]
    public void Full_KeyStillPresentAfterDelete_Failed()
    {
        var fixture = new Fixture();
        fixture.AddTestPolicies();
        var registry = new DelegatingRegistry(fixture.Registry) { OnDeleteKeyTree = _ => { } };

        var result = FullWithRegistry(fixture, registry).Run(TestTweaks.Action(), null, null, CancellationToken.None);

        Assert.Equal(TweakOutcome.Failed, result.Outcome);
        Assert.Contains("klucz nadal istnieje", Lines(result)[0], StringComparison.Ordinal);
        Assert.Contains(RegFile, Lines(result)[0], StringComparison.Ordinal);
    }

    [Fact]
    public void Full_PolicyKeyUnreadable_FailedPoliciesUntouched()
    {
        var fixture = new Fixture();
        fixture.Registry.DenyKey(PolicyKey);

        var result = fixture.Run(full: true);

        Assert.Equal(TweakOutcome.Failed, result.Outcome);
        Assert.StartsWith("Nie udało się sprawdzić polityk Windows Update", result.Message, StringComparison.Ordinal);
        Assert.EndsWith(NothingChanged, result.Message, StringComparison.Ordinal);
        Assert.Single(Lines(result));
        Assert.Empty(fixture.Tools.Calls);
        Assert.Empty(fixture.Events);
        Assert.True(fixture.AllRunning("wuauserv", "bits", "cryptsvc", "AppIDSvc", "applockerfltr"));
    }

    [Fact]
    public void Full_StartTypeWriteDenied_WarningOnly()
    {
        var fixture = new Fixture();
        fixture.Services.Add("UsoSvc", ServiceStartType.Disabled, running: false);
        fixture.Services.FailSetStartType("UsoSvc");

        var result = fixture.Run(full: true);

        Assert.Equal(TweakOutcome.Executed, result.Outcome);
        Assert.Contains(result.Warnings, w => w.StartsWith("Nie udało się ustawić typu startu UsoSvc na AutomaticDelayed:", StringComparison.Ordinal));
        Assert.Equal(ServiceStartType.Disabled, fixture.Services.StartTypeOf("UsoSvc"));
    }

    [Fact]
    public void Full_ServicesBackupWriteFails_StartTypesUntouched_Warning()
    {
        var fixture = new Fixture();
        fixture.Services.Add("UsoSvc", ServiceStartType.Disabled, running: false);
        fixture.Files.FailWrite(ServicesFile);

        var result = fixture.Run(full: true);

        Assert.Equal(TweakOutcome.Executed, result.Outcome);
        Assert.Contains(result.Warnings, w => w.StartsWith("Nie udało się zapisać kopii typów startu", StringComparison.Ordinal) && w.Contains("typy startu nie zostały zmienione"));
        Assert.DoesNotContain(fixture.Services.MutationCalls, c => c.StartsWith("SetStartType", StringComparison.Ordinal));
    }

    [Fact]
    public void Full_StartTypeReadbackMismatch_Warning()
    {
        var fixture = new Fixture();
        fixture.Services.Add("UsoSvc", ServiceStartType.Disabled, running: false);
        var services = new IgnoringSetStartTypeServiceManager(fixture.Services);
        var handler = new WindowsUpdateResetHandler(true, new RepairServices
        {
            Services = services,
            Registry = fixture.Registry,
            Tools = fixture.Tools,
            Files = fixture.Files,
            Domain = fixture.Domain,
            Clock = fixture.Clock,
            Log = fixture.Log,
            LongTools = fixture.LongTools,
            DiskSpace = fixture.DiskSpace,
            Servicing = fixture.Servicing,
            Scripts = fixture.Scripts,
            User = fixture.User,
            TimeZone = TimeZoneInfo.Utc,
            Paths = new RepairPaths { SystemRoot = Win, EnsureBackupDirectory = () => Backups },
        });

        var result = handler.Run(TestTweaks.Action(), null, null, CancellationToken.None);

        Assert.Equal(TweakOutcome.Executed, result.Outcome);
        Assert.Contains(result.Warnings, w => w.StartsWith("Typ startu UsoSvc po zmianie to Disabled zamiast AutomaticDelayed", StringComparison.Ordinal));
    }

    // ---- wariant podstawowy: diagnoza polityk ----

    [Fact]
    public void Basic_PoliciesPresent_DiagnosedNotTouched()
    {
        var fixture = new Fixture();
        fixture.AddTestPolicies();

        var result = fixture.Run(full: false);

        Assert.Equal(TweakOutcome.Executed, result.Outcome);
        var lines = Lines(result);
        Assert.Contains("Polityka: WUServer = http://dso-wsus.invalid:8530", lines);
        Assert.Contains("Polityka: WUStatusServer = http://dso-wsus.invalid:8530", lines);
        Assert.Contains("Polityka: DisableWindowsUpdateAccess = 1", lines);
        Assert.Contains("Polityka: SetDisableUXWUAccess = 1", lines);
        Assert.Contains(@"Polityka: AU\NoAutoUpdate = 1", lines);
        Assert.Contains(@"Polityka: AU\UseWUServer = 1", lines);
        Assert.Contains("Mogą być przyczyną problemu; usuwa je pozycja „Zresetuj Windows Update i usuń blokady” (z kopią).", lines);
        Assert.Empty(fixture.Registry.DeletedKeys);
        Assert.Empty(fixture.Registry.WriteLog);
        Assert.Empty(fixture.Tools.Calls);
        Assert.Equal(ReadStatus.Found, fixture.Registry.KeyStatus(PolicyKey));
        Assert.Equal(HistoryLine, lines[lines.Length - 1]);
    }

    [Fact]
    public void Basic_SomePolicies_OnlyPresentOnesListed_OtherTypesShownAsDisplayString()
    {
        var fixture = new Fixture();
        fixture.Registry.Set(PolicyKey + @"\AU", "NoAutoUpdate", RegValue.FromDWord(1));
        fixture.Registry.Set(PolicyKey, "WUServer", RegValue.FromBinary(new byte[] { 0xAB }));
        fixture.Registry.DenyRead(PolicyKey, "SetDisableUXWUAccess");

        var lines = Lines(fixture.Run(full: false));

        Assert.Contains(@"Polityka: AU\NoAutoUpdate = 1", lines);
        Assert.Contains("Polityka: WUServer = AB [Binary]", lines);
        Assert.Contains(lines, l => l.StartsWith("Polityka: SetDisableUXWUAccess – nie udało się odczytać", StringComparison.Ordinal));
        Assert.DoesNotContain(lines, l => l.StartsWith("Polityka: UseWUServer", StringComparison.Ordinal) || l.StartsWith(@"Polityka: AU\UseWUServer", StringComparison.Ordinal));
    }

    [Fact]
    public void Basic_NoPolicies_NoDiagnosisLines()
    {
        var lines = Lines(new Fixture().Run(full: false));

        Assert.DoesNotContain(lines, l => l.StartsWith("Polityka:", StringComparison.Ordinal) || l.StartsWith("Mogą być", StringComparison.Ordinal));
    }

    [Fact]
    public void Timestamp_UsesLocalTimeOfConfiguredZone()
    {
        var fixture = new Fixture();
        var plus2 = TimeZoneInfo.CreateCustomTimeZone("DSO+2", TimeSpan.FromHours(2), "DSO+2", "DSO+2");
        var handler = new WindowsUpdateResetHandler(false, new RepairServices
        {
            Services = fixture.Services,
            Registry = fixture.Registry,
            Tools = fixture.Tools,
            Files = fixture.Files,
            Domain = fixture.Domain,
            Clock = fixture.Clock,
            Log = fixture.Log,
            LongTools = fixture.LongTools,
            DiskSpace = fixture.DiskSpace,
            Servicing = fixture.Servicing,
            Scripts = fixture.Scripts,
            User = fixture.User,
            TimeZone = plus2,
            Paths = new RepairPaths { SystemRoot = Win, EnsureBackupDirectory = () => Backups },
        });

        handler.Run(TestTweaks.Action(), null, null, CancellationToken.None);

        Assert.Contains($"Move:{Sd}->{Sd}.dso-20261022-163005", fixture.Files.Calls);
    }

    [Fact]
    public void Plan_MatchesMeasurement()
    {
        Assert.Equal(new[] { "wuauserv", "bits", "cryptsvc" }, WindowsUpdateReset.StoppedServices);
        Assert.Equal(
            new[] { ("wuauserv", ServiceStartType.Manual), ("bits", ServiceStartType.AutomaticDelayed), ("cryptsvc", ServiceStartType.Automatic), ("UsoSvc", ServiceStartType.AutomaticDelayed) },
            WindowsUpdateReset.FreshStartTypes.ToArray());
        Assert.Equal(new[] { "msiserver", "WaaSMedicSvc", "DoSvc" }, WindowsUpdateReset.ProtectedServices);
        Assert.Equal(PolicyKey, WindowsUpdatePolicies.KeyPath);
        Assert.Equal(
            new[] { "WUServer", "WUStatusServer", "DisableWindowsUpdateAccess", "SetDisableUXWUAccess", @"AU\NoAutoUpdate", @"AU\UseWUServer" },
            WindowsUpdatePolicies.DiagnosedValues.Select(v => v.Path == PolicyKey ? v.Name : @"AU\" + v.Name).ToArray());
    }

    /// <summary>Rejestr delegujący do fake'a; DeleteKeyTree podmieniany (wyjątek albo „sukces” bez skutku).</summary>
    private sealed class DelegatingRegistry : IRegistry
    {
        private readonly FakeRegistry _inner;

        public DelegatingRegistry(FakeRegistry inner) => _inner = inner;

        public Action<string>? OnDeleteKeyTree { get; init; }

        public RegistryReadResult Read(string path, string name) => _inner.Read(path, name);

        public void Write(string path, string name, RegValue value) => _inner.Write(path, name, value);

        public ReadStatus KeyStatus(string path) => _inner.KeyStatus(path);

        public IReadOnlyList<string> SubKeyNames(string path) => _inner.SubKeyNames(path);

        public bool DeleteKeyIfEmpty(string path) => _inner.DeleteKeyIfEmpty(path);

        public void DeleteKeyTree(string path)
        {
            if (OnDeleteKeyTree is { } replacement)
            {
                replacement(path);
                return;
            }

            _inner.DeleteKeyTree(path);
        }
    }

    /// <summary>Usługi, w których SetStartType „udaje sukces”, ale nic nie zmienia (rozjazd odczytu zwrotnego).</summary>
    private sealed class IgnoringSetStartTypeServiceManager : IServiceManager
    {
        private readonly FakeServiceManager _inner;

        public IgnoringSetStartTypeServiceManager(FakeServiceManager inner) => _inner = inner;

        public ServiceQueryResult Query(string name) => _inner.Query(name);

        public void SetStartType(string name, ServiceStartType startType)
        {
        }

        public void StopService(string name, TimeSpan timeout) => _inner.StopService(name, timeout);

        public void StartService(string name, TimeSpan timeout) => _inner.StartService(name, timeout);

        public IReadOnlyList<string> GetRunningDependents(string name) => _inner.GetRunningDependents(name);

        public void StopServiceWithDependents(string name, TimeSpan timeout) => _inner.StopServiceWithDependents(name, timeout);
    }
}
