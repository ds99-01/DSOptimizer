using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.InteropServices;
using DSOptimizer.Core;
using DSOptimizer.Core.Abstractions;
using DSOptimizer.Core.Catalog;
using DSOptimizer.Core.Cli;
using DSOptimizer.Core.Engine;
using DSOptimizer.Core.Localization;
using DSOptimizer.Core.Model;
using DSOptimizer.Core.Operations;
using DSOptimizer.Core.RestorePoints;
using DSOptimizer.Core.Settings;
using DSOptimizer.Tests.Unit.Engine;
using DSOptimizer.Tests.Unit.Fakes;
using Xunit;

#pragma warning disable CA2201 // COMException jest tu celowo: tak zgłasza wyłączoną ochronę systemu prawdziwe API

namespace DSOptimizer.Tests.Unit.Localization.English;

/// <summary>Teksty silnika, katalogu, CLI, ustawień i punktów przywracania (Core) po angielsku: całe zdania, bez sklejania.</summary>
public class CoreEnglishTests
{
    private static CliCatalogResult Catalog(string source, params TweakDefinition[] tweaks)
        => new(new LoadedCatalog(tweaks, "sha256:abc"), source, Array.Empty<string>());

    private static CliOptions Options(CliCommand command, string id)
        => new() { Command = command, TweakId = id, OutPath = @"C:\o.json", RestorePointPolicy = RestorePointPolicy.Skip };

    /// <summary>Log, którego Warn rzuca: wynik z odmową wywraca pozycję w trakcie serii, więc silnik łapie wyjątek w pętli serii.</summary>
    private sealed class ThrowingWarnLog : ILog
    {
        public List<string> Failures { get; } = new();

        public void Info(string component, string message)
        {
        }

        public void Warn(string component, string message) => throw new InvalidOperationException("boom");

        public void Fail(string component, string message, Exception? exception = null) => Failures.Add(message);
    }

    private static TweakEngine EngineWithLog(EngineFixture f, ILog log)
        => new(
            new EngineServices
            {
                Registry = f.Registry,
                Services = f.Services,
                Scripts = f.Scripts,
                Launcher = f.Launcher,
                Journal = f.Journal,
                RestorePoints = f.RestorePoints,
                SystemInfo = f.SystemInfo,
                User = f.User,
                Log = log,
                Clock = f.Clock,
            },
            "sha256:test");

    [Fact]
    public void Engine_ApplyResult_English()
    {
        using (L.Use(UiLanguage.English))
        {
            var pending = new EngineFixture();
            new ToggleScript { ApplyWithoutEffect = true }.Register(pending.Scripts);
            var reboot = pending.CreateEngine().Apply(TestTweaks.Scripted() with { RequiresReboot = true });
            Assert.Equal(TweakOutcome.PendingReboot, reboot.Outcome);
            Assert.Equal("The change takes effect after a restart.", reboot.Message);

            var mismatch = new EngineFixture();
            new ToggleScript { ApplyWithoutEffect = true }.Register(mismatch.Scripts);
            var failed = mismatch.CreateEngine().Apply(TestTweaks.Scripted());
            Assert.Equal(TweakOutcome.FailedRolledBack, failed.Outcome);
            Assert.Equal("Status check after the change: NotApplied", failed.Message);

            Assert.Equal("Apply is only for kind=tweak – use Execute for actions.", new EngineFixture().CreateEngine().Apply(TestTweaks.Action()).Message);
        }
    }

    [Fact]
    public void Engine_ExecuteResult_English()
    {
        using (L.Use(UiLanguage.English))
        {
            var f = new EngineFixture();
            f.Scripts.On("RUN", _ => new ScriptResult { ExitCode = 1, Output = Array.Empty<string>(), Errors = Array.Empty<string>() });
            var script = f.CreateEngine().Execute(TestTweaks.Action());
            Assert.Equal(TweakOutcome.Failed, script.Outcome);
            Assert.Equal("The script ended with code 1: (no stderr)", script.Message);

            var panel = new EngineFixture().CreateEngine().Execute(TestTweaks.Panel());
            Assert.Equal(TweakOutcome.Executed, panel.Outcome);
            Assert.Equal("Started compmgmt.msc", panel.Message);
        }
    }

    [Fact]
    public void Undo_NoSnapshot_English()
    {
        using (L.Use(UiLanguage.English))
        {
            var withoutDefaults = TestTweaks.TwoValues() with
            {
                Registry = new[] { new RegistryEntry { Path = TestTweaks.Path, Name = "A", Value = RegValue.FromDWord(1) } },
            };
            var refused = new EngineFixture().CreateEngine().Undo(withoutDefaults);
            Assert.Equal(TweakOutcome.Refused, refused.Outcome);
            Assert.Equal("No journal entry and no default values in the catalog – there's no data to undo the change.", refused.Message);

            var f = new EngineFixture();
            f.Registry.Set(TestTweaks.Path, "A", RegValue.FromDWord(1));
            f.Registry.Set(TestTweaks.Path, "B", RegValue.FromDWord(1));
            var defaults = f.CreateEngine().Undo(TestTweaks.TwoValues());
            Assert.Equal(TweakOutcome.UndoneFromDefaults, defaults.Outcome);
            Assert.Equal(
                "No journal entry – the default values from the catalog were restored (the state before the change on this computer is unknown).",
                Assert.Single(defaults.Warnings));
        }
    }

    [Fact]
    public void Batch_UnexpectedError_English()
    {
        using (L.Use(UiLanguage.English))
        {
            var undoLog = new ThrowingWarnLog();
            var undo = EngineWithLog(new EngineFixture(), undoLog).UndoBatch(new[] { TestTweaks.TwoValues() with { Reversible = false } });
            Assert.Equal("Unexpected error: boom", Assert.Single(undo.Results).Message);
            Assert.Equal("Unexpected error in the Undo batch for test.two-values.", Assert.Single(undoLog.Failures));

            var applyLog = new ThrowingWarnLog();
            var apply = EngineWithLog(new EngineFixture(), applyLog).ApplyBatch(
                new[] { TestTweaks.Panel() with { Handler = "x" } },
                new BatchOptions { RestorePointPolicy = RestorePointPolicy.Skip });
            Assert.Equal("Unexpected error: boom", Assert.Single(apply.Results).Message);
            Assert.Equal("Unexpected error in the batch for test.panel.", Assert.Single(applyLog.Failures));
        }
    }

    [Fact]
    public void Batch_StoppedMessage_English()
    {
        using (L.Use(UiLanguage.English))
        {
            Assert.Equal("Skipped – stopped.", TweakEngine.StoppedMessage);
            Assert.Equal("DSOptimizer – before changes", new BatchOptions().RestorePointDescription);
        }
    }

    [Fact]
    public void RestorePoint_Created_English()
    {
        using (L.Use(UiLanguage.English))
        {
            var registry = new FakeRegistry();
            var api = new FakeRestorePointApi(registry);
            var clock = new FakeClock();
            var log = new ListLog();
            var service = new RestorePointService(registry, api, log, clock, new FakeSleeper(clock).Sleep);

            var result = service.Create("DSOptimizer – before changes (2026-10-28 10:00:00)");

            Assert.Equal(RestorePointStatus.Created, result.Status);
            Assert.Contains("INFO [RestorePoint] Restore point created: DSOptimizer – before changes (2026-10-28 10:00:00) (sequence number 100)", log.Lines);
        }
    }

    [Fact]
    public void RestorePoint_Failures_English()
    {
        using (L.Use(UiLanguage.English))
        {
            var registry = new FakeRegistry();
            var clock = new FakeClock();
            var disabledApi = new FakeRestorePointApi(registry) { ThrowOnCall = new COMException("x", unchecked((int)0x80070422)) };
            var disabled = new RestorePointService(registry, disabledApi, new ListLog(), clock, new FakeSleeper(clock).Sleep).Create("d");
            Assert.Equal(RestorePointStatus.ProtectionDisabled, disabled.Status);
            Assert.Equal("System Protection is turned off (HRESULT 0x80070422, error 1058).", disabled.Message);

            var neverApi = new FakeRestorePointApi(registry) { AppearsOnReadNumber = null };
            var never = new RestorePointService(registry, neverApi, new ListLog(), clock, new FakeSleeper(clock).Sleep).Create("d");
            Assert.Equal(
                "SystemRestore.CreateRestorePoint returned 0, but the restore point didn't appear in the list within 10 s (possibly the 24-hour limit, or the service skipped it).",
                never.Message);
        }
    }

    [Fact]
    public void RestorePoint_ErrorMessages_English_ReadOnceWithoutRepeatedPhrase()
    {
        using (L.Use(UiLanguage.English))
        {
            var registry = new FakeRegistry();
            var clock = new FakeClock();

            var before = new FakeRestorePointApi(registry) { ThrowOnRead = new InvalidOperationException("boom") };
            var beforeResult = new RestorePointService(registry, before, new ListLog(), clock, new FakeSleeper(clock).Sleep).Create("d");
            Assert.Equal("Can't read the list of restore points, so creating one wasn't attempted: boom", beforeResult.Message);

            var failing = new FakeRestorePointApi(registry) { ThrowOnCall = new InvalidOperationException("boom") };
            var failed = new RestorePointService(registry, failing, new ListLog(), clock, new FakeSleeper(clock).Sleep).Create("d");
            Assert.Equal("The restore point service reported an error: boom", failed.Message);

            var polling = new FakeRestorePointApi(registry) { ThrowOnRead = new InvalidOperationException("boom"), ThrowOnReadNumber = 2 };
            var pollResult = new RestorePointService(registry, polling, new ListLog(), clock, new FakeSleeper(clock).Sleep).Create("d");
            Assert.Equal("Can't confirm the result (couldn't read the list of restore points): boom", pollResult.Message);
        }
    }

    [Fact]
    public void CatalogValidator_Error_English()
    {
        using (L.Use(UiLanguage.English))
        {
            var ex = Assert.Throws<CatalogLoadException>(() => CatalogLoader.Load(new[] { new CatalogSource("a.json", "[{\"id\": 5}]") }));
            Assert.Equal("a.json (line 1): field 'id' must be text", ex.Message);

            var json = Assert.Throws<CatalogLoadException>(() => CatalogLoader.Load(new[] { new CatalogSource("b.json", "[{") }));
            Assert.StartsWith("b.json (line 1, column 2): invalid JSON – ", json.Message, StringComparison.Ordinal);

            var tweak = TestTweaks.TwoValues() with { Summary = new string('x', 161), Benefit = "b" };
            var error = Assert.Single(CatalogValidator.Validate(new[] { tweak }));
            Assert.Equal("test.two-values [summary]: summary has 161 characters (limit 160).", error.ToString());

            var noSummary = CatalogValidator.Validate(new[] { TestTweaks.TwoValues() with { Benefit = "b" } });
            Assert.Equal("Missing short description (summary).", Assert.Single(noSummary).Message);
        }
    }

    [Fact]
    public void Settings_CorruptedWarning_English()
    {
        using (L.Use(UiLanguage.English))
        {
            Assert.Equal("The settings file is empty.", Assert.Throws<SettingsFormatException>(() => AppSettingsSerializer.Deserialize(" ")).Message);

            var damaged = Assert.Throws<SettingsFormatException>(() => AppSettingsSerializer.Deserialize("{"));
            Assert.Equal("The settings file is corrupted: " + damaged.InnerException!.Message, damaged.Message);

            var newer = Assert.Throws<SettingsFormatException>(() => AppSettingsSerializer.Deserialize("{\"schemaVersion\": 99}"));
            Assert.Equal(
                $"The settings file has a newer format version (schemaVersion 99 – from a newer version of the program); this version supports {AppSettings.SchemaVersion}.",
                newer.Message);
        }
    }

    [Fact]
    public void CliRunner_UnknownId_English()
    {
        using (L.Use(UiLanguage.English))
        {
            var report = CliRunner.Run(Options(CliCommand.Detect, "nie.ma"), Catalog(@"C:\dso\k.json", TestTweaks.TwoValues()), new EngineFixture().CreateServices(), Array.Empty<string>());
            Assert.Equal(CliExitCode.CatalogError, report.Result);
            Assert.Equal(@"Unknown tweak 'nie.ma' in catalog C:\dso\k.json.", Assert.Single(report.Errors));
            Assert.Equal(@"An external catalog was used: C:\dso\k.json (sha256:abc).", Assert.Single(report.Warnings));

            var apply = CliRunner.Run(Options(CliCommand.Apply, "test.action"), Catalog(CliCatalog.BuiltInSource, TestTweaks.Action()), new EngineFixture().CreateServices(), Array.Empty<string>());
            Assert.Equal("The apply command applies only to kind=tweak ('test.action' has kind=action) – use --execute.", Assert.Single(apply.Errors));
        }
    }

    [Fact]
    public void ShellSessionGuard_Refusal_English()
    {
        using (L.Use(UiLanguage.English))
        {
            Assert.Equal(
                "File Explorer for this account isn't running in the current session – not starting an elevated File Explorer.",
                ShellSessionGuard.RefusalMessage);
        }
    }

    [Fact]
    public void ServicePreview_WholeSentences_English()
    {
        using (L.Use(UiLanguage.English))
        {
            var f = new EngineFixture();
            f.Services.Add("SysMain", ServiceStartType.Automatic, running: true);
            var running = Assert.Single(f.CreateEngine().Preview(TestTweaks.DisableService()));
            Assert.Equal(("Service SysMain", "Automatic, running", "Disabled, stopped"), (running.Description, running.Current, running.Target));

            var g = new EngineFixture();
            g.Services.Add("SysMain", ServiceStartType.Manual, running: false);
            Assert.Equal("Manual", Assert.Single(g.CreateEngine().Preview(TestTweaks.DisableService())).Current);
        }
    }

    [Fact]
    public void SnapshotDescribe_WholeSentences_English()
    {
        using (L.Use(UiLanguage.English))
        {
            Assert.Equal("Service SysMain: Automatic, was running", SnapshotRecord.ForService("SysMain", true, ServiceStartType.Automatic, true).Describe());
            Assert.Equal("Service SysMain: Manual", SnapshotRecord.ForService("SysMain", true, ServiceStartType.Manual, false).Describe());
            Assert.Equal("Service SysMain: (no such service)", SnapshotRecord.ForService("SysMain", false, null, false).Describe());
            Assert.Equal("Script: (no snapshot)", SnapshotRecord.ForScript(null).Describe());
            Assert.Equal(@"HKLM\X\A = (none)", SnapshotRecord.ForRegistry(@"HKLM\X", "A", RegValue.Absent).Describe());
        }
    }

    [Fact]
    public void ProductInfo_TrademarkNotice_English()
    {
        using (L.Use(UiLanguage.English))
        {
            Assert.Equal(
                "The DSOptimizer name and the DŚO mark aren't covered by the MPL 2.0 license; modified versions must use a different name.",
                ProductInfo.TrademarkNotice);
        }
    }
}
