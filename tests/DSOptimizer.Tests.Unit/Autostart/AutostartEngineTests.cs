using System;
using System.Linq;
using System.Threading;
using DSOptimizer.Core.Autostart;
using DSOptimizer.Core.Engine;
using DSOptimizer.Core.Errors;
using DSOptimizer.Core.Journal;
using DSOptimizer.Core.Model;
using DSOptimizer.Core.Operations;
using DSOptimizer.Tests.Unit.Fakes;
using Xunit;

namespace DSOptimizer.Tests.Unit.Autostart;

public class AutostartEngineTests
{
    private const string Sid = "S-1-5-21-1-2-3-1001";
    private const string Family = "Microsoft.WindowsTerminal_8wekyb3d8bbwe";
    private const string TaskId = "StartTerminalOnLoginTask";
    private const string Name = "OneDrive";

    private static readonly AutostartLocations Locations = AutostartLocations.ForUser(Sid);

    private static readonly AutostartSource[] AllApproved =
    {
        AutostartSource.RunUser, AutostartSource.RunMachine, AutostartSource.Run32, AutostartSource.FolderUser, AutostartSource.FolderCommon,
    };

    /// <summary>2026-11-02 08:37:53.977 UTC jako FILETIME (little-endian).</summary>
    private static readonly byte[] NowFiletime = { 0x90, 0x5A, 0x07, 0x74, 0xA5, 0x6A, 0xDD, 0x01 };

    /// <summary>FILETIME zapisany wcześniej przez Menedżera zadań.</summary>
    private static readonly byte[] TaskManagerFiletime = { 0x00, 0x6C, 0x1B, 0x4F, 0x9F, 0x6A, 0xDD, 0x01 };

    private static readonly byte[] Zeros = new byte[8];

    private readonly FakeRegistry _registry = new();
    private readonly FakeJournalStore _journal = new();
    private readonly FakeClock _clock = new() { UtcNow = new DateTimeOffset(2026, 11, 2, 8, 37, 53, 977, TimeSpan.Zero) };
    private readonly ListLog _log = new();

    private static string ApprovedKey => Locations.StartupApprovedKey(AutostartSource.RunUser);

    private static string TaskKey => Locations.StartupTaskKey(Family, TaskId);

    [Fact]
    public void Disable_ValueAbsent_Writes03Filetime_EntryActive()
    {
        var result = Single(Set(AutostartState.Disabled, ApprovedRow()));

        Assert.Equal(TweakOutcome.Applied, result.Outcome);
        Assert.Equal(AutostartText.Disabled, result.Message);
        Assert.Equal(RegValue.FromBinary(Approved(0x03, 0, 0, 0, NowFiletime)), _registry.Get(ApprovedKey, Name));
        var entry = Assert.Single(_journal.Current.Entries);
        Assert.Equal(JournalEntry.AutostartKind, entry.Kind);
        Assert.Equal("run:user:OneDrive", entry.TweakId);
        Assert.Equal("OneDrive app", entry.Title);
        Assert.Equal(Sid, entry.UserSid);
        Assert.Equal(JournalEntryStatus.Active, entry.Status);
        Assert.Equal(_clock.UtcNow, entry.AppliedAt);
        var record = Assert.Single(entry.Baseline);
        Assert.Equal(ApprovedKey, record.Path);
        Assert.Equal(Name, record.Name);
        Assert.Equal(RegValue.Absent, record.Value);
        Assert.Null(record.CreatedKeyRoot);
        Assert.Equal(new[] { ("disable-autostart", "Started"), ("disable-autostart", "Applied") }, Events(entry));
        Assert.Equal(AutostartText.Disabled, entry.Events.Last().Message);
    }

    [Fact]
    public void Disable_Existing02_Writes03()
    {
        _registry.Set(ApprovedKey, Name, RegValue.FromBinary(Approved(0x02, 0, 0, 0, Zeros)));

        var result = Single(Set(AutostartState.Disabled, ApprovedRow()));

        Assert.Equal(TweakOutcome.Applied, result.Outcome);
        Assert.Equal(RegValue.FromBinary(Approved(0x03, 0, 0, 0, NowFiletime)), _registry.Get(ApprovedKey, Name));
        Assert.Equal(RegValue.FromBinary(Approved(0x02, 0, 0, 0, Zeros)), Assert.Single(Assert.Single(_journal.Current.Entries).Baseline).Value);
    }

    [Fact]
    public void Disable_Byte06_Writes07()
    {
        _registry.Set(ApprovedKey, Name, RegValue.FromBinary(Approved(0x06, 0x0A, 0x0B, 0x0C, Zeros)));

        var result = Single(Set(AutostartState.Disabled, ApprovedRow()));

        Assert.Equal(TweakOutcome.Applied, result.Outcome);
        Assert.Equal(RegValue.FromBinary(Approved(0x07, 0x0A, 0x0B, 0x0C, NowFiletime)), _registry.Get(ApprovedKey, Name));
    }

    [Fact]
    public void Enable_Existing03_Writes02Zeros()
    {
        _registry.Set(ApprovedKey, Name, RegValue.FromBinary(Approved(0x03, 0, 0, 0, TaskManagerFiletime)));

        var result = Single(Set(AutostartState.Enabled, ApprovedRow()));

        Assert.Equal(TweakOutcome.Applied, result.Outcome);
        Assert.Equal(AutostartText.Enabled, result.Message);
        Assert.Equal(RegValue.FromBinary(Approved(0x02, 0, 0, 0, Zeros)), _registry.Get(ApprovedKey, Name));
    }

    [Fact]
    public void Enable_ActiveDisableEntry_UndoesIt_NoNewEntry()
    {
        Single(Set(AutostartState.Disabled, ApprovedRow()));

        var result = Single(Set(AutostartState.Enabled, ApprovedRow()));

        Assert.Equal(TweakOutcome.Undone, result.Outcome);
        Assert.Equal(AutostartText.UndoneByToggle, result.Message);
        Assert.Equal(RegValue.Absent, _registry.Get(ApprovedKey, Name));
        var entry = Assert.Single(_journal.Current.Entries);
        Assert.Equal(JournalEntryStatus.Undone, entry.Status);
        Assert.Equal(new[] { ("disable-autostart", "Started"), ("disable-autostart", "Applied"), ("undo", "Undone") }, Events(entry));
    }

    [Fact]
    public void Disable_ActiveEnableEntry_UndoesIt()
    {
        var original = RegValue.FromBinary(Approved(0x03, 0, 0, 0, TaskManagerFiletime));
        _registry.Set(ApprovedKey, Name, original);
        Single(Set(AutostartState.Enabled, ApprovedRow()));

        var result = Single(Set(AutostartState.Disabled, ApprovedRow()));

        Assert.Equal(TweakOutcome.Undone, result.Outcome);
        Assert.Equal(original, _registry.Get(ApprovedKey, Name));
        var entry = Assert.Single(_journal.Current.Entries);
        Assert.Equal(JournalEntryStatus.Undone, entry.Status);
        Assert.Equal(new[] { ("enable-autostart", "Started"), ("enable-autostart", "Applied"), ("undo", "Undone") }, Events(entry));
    }

    [Fact]
    public void Enable_ActiveDisableEntry_RestoreFails_UndoIncomplete_EntryStaysActive()
    {
        Single(Set(AutostartState.Disabled, ApprovedRow()));
        _registry.FailWrite(ApprovedKey, Name);

        var result = Single(Set(AutostartState.Enabled, ApprovedRow()));

        Assert.Equal(TweakOutcome.UndoIncomplete, result.Outcome);
        Assert.Equal(new[] { ApprovedKey + "\\" + Name + " = " + RegValue.Absent.ToDisplayString() }, result.NotRestored);
        var entry = Assert.Single(_journal.Current.Entries);
        Assert.Equal(JournalEntryStatus.Active, entry.Status);
        Assert.Equal(("undo", "UndoIncomplete"), Events(entry).Last());
    }

    [Fact]
    public void Enable_NoActiveEntry_NewEntry()
    {
        var disabledByTaskManager = RegValue.FromBinary(Approved(0x03, 0, 0, 0, TaskManagerFiletime));
        _registry.Set(ApprovedKey, Name, disabledByTaskManager);

        var result = Single(Set(AutostartState.Enabled, ApprovedRow()));

        Assert.Equal(TweakOutcome.Applied, result.Outcome);
        var entry = Assert.Single(_journal.Current.Entries);
        Assert.Equal(JournalEntryStatus.Active, entry.Status);
        Assert.Equal(disabledByTaskManager, Assert.Single(entry.Baseline).Value);
        Assert.Equal(new[] { ("enable-autostart", "Started"), ("enable-autostart", "Applied") }, Events(entry));
    }

    [Fact]
    public void AlreadyInTargetOnFreshRead_AlreadyApplied_NoJournal()
    {
        _registry.Set(ApprovedKey, Name, RegValue.FromBinary(Approved(0x02, 0, 0, 0, Zeros)));
        var row = ApprovedRow();
        Assert.Equal(AutostartState.Enabled, row.State);
        _registry.Set(ApprovedKey, Name, RegValue.FromBinary(Approved(0x03, 0, 0, 0, TaskManagerFiletime)));

        var result = Single(Set(AutostartState.Disabled, row));

        Assert.Equal(TweakOutcome.AlreadyApplied, result.Outcome);
        Assert.Equal(AutostartText.AlreadyDisabled, result.Message);
        Assert.Equal(0, _journal.LoadCount);
        Assert.Equal(0, _journal.SaveCount);
        Assert.Empty(_registry.WriteLog);
    }

    [Fact]
    public void AlreadyEnabledOnFreshRead_AlreadyApplied_NoJournal()
    {
        _registry.Set(ApprovedKey, Name, RegValue.FromBinary(Approved(0x03, 0, 0, 0, TaskManagerFiletime)));
        var row = ApprovedRow();
        _registry.Set(ApprovedKey, Name, RegValue.Absent);

        var result = Single(Set(AutostartState.Enabled, row));

        Assert.Equal(TweakOutcome.AlreadyApplied, result.Outcome);
        Assert.Equal(AutostartText.AlreadyEnabled, result.Message);
        Assert.Equal(0, _journal.SaveCount);
        Assert.Empty(_registry.WriteLog);
    }

    [Fact]
    public void ExternallyReverted_ActiveSameDirection_OldUndoneExternal_NewActive()
    {
        Single(Set(AutostartState.Disabled, ApprovedRow()));
        var enabledByTaskManager = RegValue.FromBinary(Approved(0x02, 0, 0, 0, Zeros));
        _registry.Set(ApprovedKey, Name, enabledByTaskManager);

        var result = Single(Set(AutostartState.Disabled, ApprovedRow()));

        Assert.Equal(TweakOutcome.Applied, result.Outcome);
        Assert.Equal(RegValue.FromBinary(Approved(0x03, 0, 0, 0, NowFiletime)), _registry.Get(ApprovedKey, Name));
        var entries = _journal.Current.Entries;
        Assert.Equal(2, entries.Count);
        Assert.Equal(JournalEntryStatus.Undone, entries[0].Status);
        Assert.Equal(("external", "Undone"), Events(entries[0]).Last());
        Assert.Equal(AutostartText.ChangedOutside, entries[0].Events.Last().Message);
        Assert.Equal(JournalEntryStatus.Active, entries[1].Status);
        Assert.Equal(enabledByTaskManager, Assert.Single(entries[1].Baseline).Value);
        Assert.Single(entries, entry => entry.IsActive);
    }

    [Fact]
    public void FreshValueUnknownFormat_Refused_NoJournal()
    {
        _registry.Set(ApprovedKey, Name, RegValue.FromBinary(Approved(0x02, 0, 0, 0, Zeros)));
        var row = ApprovedRow();
        _registry.Set(ApprovedKey, Name, RegValue.FromBinary(new byte[] { 0x02, 0, 0, 0, 0, 0, 0, 0 }));

        var result = Single(Set(AutostartState.Disabled, row));

        Assert.Equal(TweakOutcome.Refused, result.Outcome);
        Assert.Equal(AutostartText.FormatChanged, result.Message);
        Assert.Equal(0, _journal.LoadCount);
        Assert.Equal(0, _journal.SaveCount);
        Assert.Empty(_registry.WriteLog);
    }

    [Fact]
    public void FreshAbsent_SourceNotConfirmed_Refused_NoJournal()
    {
        var key = Locations.StartupApprovedKey(AutostartSource.RunMachine);
        _registry.Set(key, Name, RegValue.FromBinary(Approved(0x02, 0, 0, 0, Zeros)));
        var row = ApprovedRow(AutostartSource.RunMachine);
        Assert.True(row.CanToggle);
        _registry.Set(key, Name, RegValue.Absent);
        var context = Context(AllApproved.Where(source => source != AutostartSource.RunMachine).ToArray());

        var result = Single(Engine().Set(new[] { row }, AutostartState.Disabled, context, new AutostartRunOptions(), CancellationToken.None));

        Assert.Equal(TweakOutcome.Refused, result.Outcome);
        Assert.Equal(AutostartText.FormatChanged, result.Message);
        Assert.Equal(0, _journal.LoadCount);
        Assert.Equal(0, _journal.SaveCount);
        Assert.Empty(_registry.WriteLog);
    }

    [Fact]
    public void FreshAbsent_SourceNotConfirmed_AlreadyInTarget_AlreadyApplied_NoJournal()
    {
        var key = Locations.StartupApprovedKey(AutostartSource.RunMachine);
        _registry.Set(key, Name, RegValue.FromBinary(Approved(0x03, 0, 0, 0, TaskManagerFiletime)));
        var row = ApprovedRow(AutostartSource.RunMachine);
        _registry.Set(key, Name, RegValue.Absent);
        var context = Context(AllApproved.Where(source => source != AutostartSource.RunMachine).ToArray());

        var result = Single(Engine().Set(new[] { row }, AutostartState.Enabled, context, new AutostartRunOptions(), CancellationToken.None));

        Assert.Equal(TweakOutcome.AlreadyApplied, result.Outcome);
        Assert.Equal(AutostartText.AlreadyEnabled, result.Message);
        Assert.Equal(0, _journal.LoadCount);
        Assert.Equal(0, _journal.SaveCount);
        Assert.Empty(_registry.WriteLog);
    }

    [Fact]
    public void FreshAbsent_StartupTaskManifestEnabled_AlreadyInTarget_AlreadyApplied_NoJournal()
    {
        _registry.Set(TaskKey, "State", RegValue.FromDWord(1));
        var row = TaskRow(manifestEnabled: true);
        _registry.Set(TaskKey, "State", RegValue.FromDWord(0));

        var result = Single(Set(AutostartState.Enabled, row));

        Assert.Equal(TweakOutcome.AlreadyApplied, result.Outcome);
        Assert.Equal(AutostartText.AlreadyEnabled, result.Message);
        Assert.Equal(0, _journal.LoadCount);
        Assert.Equal(0, _journal.SaveCount);
        Assert.Empty(_registry.WriteLog);
    }

    [Fact]
    public void FreshAbsent_StartupTaskManifestEnabled_Refused_NoJournal()
    {
        _registry.Set(TaskKey, "State", RegValue.FromDWord(2));
        var row = TaskRow(manifestEnabled: true);
        Assert.True(row.CanToggle);
        _registry.Set(TaskKey, "State", RegValue.FromDWord(0));

        var result = Single(Set(AutostartState.Disabled, row));

        Assert.Equal(TweakOutcome.Refused, result.Outcome);
        Assert.Equal(AutostartText.FormatChanged, result.Message);
        Assert.Equal(0, _journal.LoadCount);
        Assert.Equal(0, _journal.SaveCount);
        Assert.Empty(_registry.WriteLog);
    }

    [Fact]
    public void VerifyMismatch_RollsBack_FailedRolledBack_EntryAborted()
    {
        var baseline = RegValue.FromBinary(Approved(0x02, 0, 0, 0, Zeros));
        _registry.Set(ApprovedKey, Name, baseline);
        var overwritten = false;
        _registry.AfterWrite = (path, name, _) =>
        {
            if (!overwritten)
            {
                overwritten = true;
                _registry.Set(path, name, RegValue.FromBinary(Approved(0x06, 0, 0, 0, Zeros)));
            }
        };

        var result = Single(Set(AutostartState.Disabled, ApprovedRow()));

        Assert.Equal(TweakOutcome.FailedRolledBack, result.Outcome);
        Assert.Equal(AutostartText.NotConfirmed, result.Message);
        Assert.Equal(baseline, _registry.Get(ApprovedKey, Name));
        var entry = Assert.Single(_journal.Current.Entries);
        Assert.Equal(JournalEntryStatus.Aborted, entry.Status);
        Assert.Equal(new[] { ("disable-autostart", "Started"), ("disable-autostart", "FailedRolledBack") }, Events(entry));
    }

    [Fact]
    public void WriteThrows_FailedRolledBack()
    {
        var writes = 0;
        _registry.WriteInterceptor = (_, _, _) => ++writes == 1 ? new OperationException(OperationErrorKind.AccessDenied, "odmowa (test)") : null;

        var result = Single(Set(AutostartState.Disabled, ApprovedRow()));

        Assert.Equal(TweakOutcome.FailedRolledBack, result.Outcome);
        Assert.Equal(AutostartText.NotConfirmed, result.Message);
        Assert.Equal(RegValue.Absent, _registry.Get(ApprovedKey, Name));
        Assert.Contains("odmowa (test)", Assert.Single(_journal.Current.Entries).Events.Last().Reason);
        Assert.Equal(JournalEntryStatus.Aborted, Assert.Single(_journal.Current.Entries).Status);
    }

    [Fact]
    public void RollbackFails_FailedRollbackIncomplete_NotRestoredListed()
    {
        var baseline = RegValue.FromBinary(Approved(0x02, 0, 0, 0, Zeros));
        _registry.Set(ApprovedKey, Name, baseline);
        var writes = 0;
        _registry.WriteInterceptor = (_, _, _) => ++writes > 1 ? new OperationException(OperationErrorKind.AccessDenied, "odmowa (test)") : null;
        _registry.AfterWrite = (path, name, _) => _registry.Set(path, name, RegValue.FromBinary(Approved(0x06, 0, 0, 0, Zeros)));

        var result = Single(Set(AutostartState.Disabled, ApprovedRow()));

        Assert.Equal(TweakOutcome.FailedRollbackIncomplete, result.Outcome);
        Assert.Equal(AutostartText.RollbackIncomplete, result.Message);
        Assert.Equal(new[] { SnapshotRecord.ForRegistry(ApprovedKey, Name, baseline).Describe() }, result.NotRestored);
        var entry = Assert.Single(_journal.Current.Entries);
        Assert.Equal(JournalEntryStatus.Aborted, entry.Status);
        Assert.Equal(("disable-autostart", "FailedRollbackIncomplete"), Events(entry).Last());
    }

    [Fact]
    public void JournalSaveFails_Failed_NothingWritten()
    {
        _journal.FailSave = true;

        var result = Single(Set(AutostartState.Disabled, ApprovedRow()));

        Assert.Equal(TweakOutcome.Failed, result.Outcome);
        Assert.Equal(AutostartText.JournalFailed("Zapis journala nie powiódł się (fake)."), result.Message);
        Assert.Empty(_registry.WriteLog);
        Assert.Equal(RegValue.Absent, _registry.Get(ApprovedKey, Name));
    }

    [Fact]
    public void JournalLoadFails_Failed_NothingWritten()
    {
        _journal.FailLoad = true;

        var result = Single(Set(AutostartState.Disabled, ApprovedRow()));

        Assert.Equal(TweakOutcome.Failed, result.Outcome);
        Assert.Equal(AutostartText.JournalFailed("Odczyt journala nie powiódł się (fake)."), result.Message);
        Assert.Empty(_registry.WriteLog);
        Assert.Equal(0, _journal.SaveCount);
    }

    [Fact]
    public void JournalLoadWarning_InResultWarnings()
    {
        _journal.LoadWarning = "journal był uszkodzony (test)";

        var result = Single(Set(AutostartState.Disabled, ApprovedRow()));

        Assert.Equal(TweakOutcome.Applied, result.Outcome);
        Assert.Contains("journal był uszkodzony (test)", result.Warnings);
    }

    [Fact]
    public void JournalSaveAfterResultFails_AppliedWithWarning()
    {
        _journal.SaveException = new JournalStoreException("dysk pełny (test)");
        _journal.ThrowOnSaveFrom = 1;

        var result = Single(Set(AutostartState.Disabled, ApprovedRow()));

        Assert.Equal(TweakOutcome.Applied, result.Outcome);
        Assert.Contains(result.Warnings, warning => warning.Contains("dysk pełny (test)"));
    }

    [Fact]
    public void CannotToggleRow_ArgumentException_NothingRead()
    {
        var good = ApprovedRow();
        _registry.Set(ApprovedKey, "Other", RegValue.FromDWord(2));
        var bad = ApprovedRow(name: "Other");
        Assert.False(bad.CanToggle);
        var reads = _registry.ReadCount;

        Assert.Throws<ArgumentException>(() => Set(AutostartState.Disabled, good, bad));

        Assert.Equal(reads, _registry.ReadCount);
        Assert.Equal(0, _journal.LoadCount);
        Assert.Empty(_registry.WriteLog);
    }

    [Fact]
    public void TargetUnknown_ArgumentException()
        => Assert.Throws<ArgumentOutOfRangeException>(() => Set(AutostartState.Unknown, ApprovedRow()));

    [Fact]
    public void StartupTask_Disable_State1_LastDisabledUnix()
    {
        _clock.UtcNow = new DateTimeOffset(2026, 11, 2, 8, 47, 29, TimeSpan.Zero);
        _registry.Set(TaskKey, "State", RegValue.FromDWord(2));
        _registry.Set(TaskKey, "UserEnabledStartupOnce", RegValue.FromDWord(1));

        var result = Single(Set(AutostartState.Disabled, TaskRow(manifestEnabled: false)));

        Assert.Equal(TweakOutcome.Applied, result.Outcome);
        Assert.Equal(RegValue.FromDWord(1), _registry.Get(TaskKey, "State"));
        Assert.Equal(RegValue.FromQWord(1_793_609_249), _registry.Get(TaskKey, "LastDisabledTime"));
        Assert.Equal(RegValue.FromDWord(1), _registry.Get(TaskKey, "UserEnabledStartupOnce"));
        var entry = Assert.Single(_journal.Current.Entries);
        Assert.Equal("appx:" + Family + "!" + TaskId, entry.TweakId);
        Assert.Equal(
            new[] { ("State", RegValue.FromDWord(2)), ("LastDisabledTime", RegValue.Absent), ("UserEnabledStartupOnce", RegValue.FromDWord(1)), ("ShowNotification", RegValue.Absent) },
            entry.Baseline.Select(record => (record.Name!, record.Value!)));
        Assert.All(entry.Baseline, record => Assert.Equal(TaskKey, record.Path));
        Assert.All(entry.Baseline, record => Assert.Null(record.CreatedKeyRoot));
    }

    [Fact]
    public void StartupTask_Enable_State2_Once_ShowNotificationRemoved()
    {
        _registry.Set(TaskKey, "State", RegValue.FromDWord(1));
        _registry.Set(TaskKey, "LastDisabledTime", RegValue.FromQWord(1_793_000_000));
        _registry.Set(TaskKey, "ShowNotification", RegValue.FromDWord(1));

        var result = Single(Set(AutostartState.Enabled, TaskRow(manifestEnabled: false)));

        Assert.Equal(TweakOutcome.Applied, result.Outcome);
        Assert.Equal(AutostartText.Enabled, result.Message);
        Assert.Equal(RegValue.FromDWord(2), _registry.Get(TaskKey, "State"));
        Assert.Equal(RegValue.FromDWord(1), _registry.Get(TaskKey, "UserEnabledStartupOnce"));
        Assert.Equal(RegValue.Absent, _registry.Get(TaskKey, "ShowNotification"));
        Assert.Equal(RegValue.FromQWord(1_793_000_000), _registry.Get(TaskKey, "LastDisabledTime"));
    }

    [Fact]
    public void StartupTask_KeyAbsent_BaselineCreatedKeyRoot()
    {
        var familyKey = TaskKey.Substring(0, TaskKey.LastIndexOf('\\'));
        _registry.CreateKey(familyKey);

        var result = Single(Set(AutostartState.Enabled, TaskRow(manifestEnabled: false)));

        Assert.Equal(TweakOutcome.Applied, result.Outcome);
        var entry = Assert.Single(_journal.Current.Entries);
        Assert.Equal(new[] { "State", "LastDisabledTime", "UserEnabledStartupOnce", "ShowNotification" }, entry.Baseline.Select(record => record.Name));
        Assert.All(entry.Baseline, record => Assert.Equal(RegValue.Absent, record.Value));
        Assert.Equal(TaskKey, entry.Baseline[0].CreatedKeyRoot);
        Assert.All(entry.Baseline.Skip(1), record => Assert.Null(record.CreatedKeyRoot));

        var back = Single(Set(AutostartState.Disabled, TaskRow(manifestEnabled: false)));

        Assert.Equal(TweakOutcome.Undone, back.Outcome);
        Assert.False(_registry.HasKey(TaskKey));
        Assert.True(_registry.HasKey(familyKey));
    }

    [Fact]
    public void StartupTask_State0_Enable_KeyStaysOnUndo()
    {
        _registry.Set(TaskKey, "State", RegValue.FromDWord(0));
        _registry.Set(TaskKey, "ShowNotification", RegValue.FromDWord(1));

        Single(Set(AutostartState.Enabled, TaskRow(manifestEnabled: false)));
        Assert.All(Assert.Single(_journal.Current.Entries).Baseline, record => Assert.Null(record.CreatedKeyRoot));

        var back = Single(Set(AutostartState.Disabled, TaskRow(manifestEnabled: false)));

        Assert.Equal(TweakOutcome.Undone, back.Outcome);
        Assert.Equal(RegValue.FromDWord(0), _registry.Get(TaskKey, "State"));
        Assert.Equal(RegValue.FromDWord(1), _registry.Get(TaskKey, "ShowNotification"));
        Assert.Equal(RegValue.Absent, _registry.Get(TaskKey, "UserEnabledStartupOnce"));
        Assert.True(_registry.HasKey(TaskKey));
    }

    [Fact]
    public void StopRequested_RestSkipped()
    {
        var stop = new BatchStop();
        var steps = new ListProgress<BatchStepEvent>
        {
            OnReport = step =>
            {
                if (step.Phase == BatchStepPhase.Finished)
                {
                    stop.Request();
                }
            },
        };

        var batch = Engine().Set(
            new[] { ApprovedRow(), ApprovedRow(name: "Teams") },
            AutostartState.Disabled,
            Context(),
            new AutostartRunOptions { Steps = steps, Stop = stop },
            CancellationToken.None);

        Assert.True(batch.Stopped);
        Assert.Equal(TweakOutcome.Applied, batch.Results[0].Outcome);
        Assert.Equal(TweakOutcome.Skipped, batch.Results[1].Outcome);
        Assert.Equal(TweakEngine.StoppedMessage, batch.Results[1].Message);
        Assert.Equal(RegValue.Absent, _registry.Get(ApprovedKey, "Teams"));
    }

    [Fact]
    public void Cancelled_AllSkipped()
    {
        using var cts = new CancellationTokenSource();
        cts.Cancel();

        var batch = Engine().Set(new[] { ApprovedRow() }, AutostartState.Disabled, Context(), new AutostartRunOptions(), cts.Token);

        Assert.Equal(TweakOutcome.Skipped, Single(batch).Outcome);
        Assert.Empty(_registry.WriteLog);
    }

    [Fact]
    public void Steps_StartedFinishedPerItem()
    {
        var steps = new ListProgress<BatchStepEvent>();
        var rows = new[] { ApprovedRow(), ApprovedRow(AutostartSource.RunMachine, "Teams") };

        var batch = Engine().Set(rows, AutostartState.Disabled, Context(), new AutostartRunOptions { Steps = steps }, CancellationToken.None);

        Assert.Equal(
            new[]
            {
                (0, "run:user:OneDrive", BatchStepPhase.Started),
                (0, "run:user:OneDrive", BatchStepPhase.Finished),
                (1, "run:machine:Teams", BatchStepPhase.Started),
                (1, "run:machine:Teams", BatchStepPhase.Finished),
            },
            steps.Items.Select(step => (step.Index, step.TweakId!, step.Phase)));
        Assert.All(steps.Items, step => Assert.Equal(2, step.Count));
        Assert.All(steps.Items, step => Assert.Equal(BatchStepKind.Item, step.Kind));
        Assert.Same(batch.Results[1], steps.Items[3].Result);
    }

    [Fact]
    public void StepsReceiverThrows_SeriesContinues()
    {
        var steps = new ListProgress<BatchStepEvent> { OnReport = _ => throw new InvalidOperationException("odbiornik (test)") };

        var batch = Engine().Set(new[] { ApprovedRow() }, AutostartState.Disabled, Context(), new AutostartRunOptions { Steps = steps }, CancellationToken.None);

        Assert.Equal(TweakOutcome.Applied, Single(batch).Outcome);
    }

    [Fact]
    public void UnexpectedReadError_Failed_RestContinues()
    {
        var first = ApprovedRow();
        var second = ApprovedRow(AutostartSource.RunMachine, "Teams");
        _registry.ReadFailure = (path, _) => path == ApprovedKey ? new InvalidOperationException("adapter (test)") : null;

        var batch = Set(AutostartState.Disabled, first, second);

        Assert.Equal(TweakOutcome.Failed, batch.Results[0].Outcome);
        Assert.Contains("adapter (test)", batch.Results[0].Message);
        Assert.Equal(TweakOutcome.Applied, batch.Results[1].Outcome);
    }

    [Fact]
    public void NullArguments_Throw()
    {
        var engine = Engine();
        var rows = new[] { ApprovedRow() };
        Assert.Throws<ArgumentNullException>(() => new AutostartEngine(null!, _journal, _log, _clock));
        Assert.Throws<ArgumentNullException>(() => new AutostartEngine(_registry, null!, _log, _clock));
        Assert.Throws<ArgumentNullException>(() => new AutostartEngine(_registry, _journal, null!, _clock));
        Assert.Throws<ArgumentNullException>(() => new AutostartEngine(_registry, _journal, _log, null!));
        Assert.Throws<ArgumentNullException>(() => engine.Set(null!, AutostartState.Disabled, Context(), new AutostartRunOptions(), CancellationToken.None));
        Assert.Throws<ArgumentNullException>(() => engine.Set(rows, AutostartState.Disabled, null!, new AutostartRunOptions(), CancellationToken.None));
        Assert.Throws<ArgumentNullException>(() => engine.Set(rows, AutostartState.Disabled, Context(), null!, CancellationToken.None));
        Assert.Throws<ArgumentException>(() => engine.Set(new AutostartRow[] { null! }, AutostartState.Disabled, Context(), new AutostartRunOptions(), CancellationToken.None));
    }

    private static byte[] Approved(byte b0, byte b1, byte b2, byte b3, byte[] filetime)
        => new[] { b0, b1, b2, b3 }.Concat(filetime).ToArray();

    private static (string Action, string Outcome)[] Events(JournalEntry entry)
        => entry.Events.Select(e => (e.Action, e.Outcome)).ToArray();

    private static TweakResult Single(BatchResult batch) => Assert.Single(batch.Results);

    private static AutostartContext Context(params AutostartSource[] confirmed)
        => new(Sid, true, Sid, true, true, Locations, confirmed.Length == 0 ? AllApproved : confirmed, true, true);

    private AutostartEngine Engine() => new(_registry, _journal, _log, _clock);

    private BatchResult Set(AutostartState target, params AutostartRow[] rows)
        => Engine().Set(rows, target, Context(), new AutostartRunOptions(), CancellationToken.None);

    private AutostartRow ApprovedRow(AutostartSource source = AutostartSource.RunUser, string name = Name)
    {
        var current = AutostartSwitchReader.Read(_registry, AutostartSwitchKind.StartupApproved, Locations.StartupApprovedKey(source), name, enabledInManifest: false);
        return new AutostartRow(
            new AutostartEntry
            {
                Id = AutostartIds.For(source, name),
                Source = source,
                EntryName = name,
                Name = name + " app",
                Location = "loc",
                FileStatus = AutostartFileStatus.Exists,
                Switch = current,
            },
            null,
            AutostartBlocker.None);
    }

    private AutostartRow TaskRow(bool manifestEnabled)
    {
        var current = AutostartSwitchReader.Read(_registry, AutostartSwitchKind.StartupTask, TaskKey, "State", manifestEnabled);
        return new AutostartRow(
            new AutostartEntry
            {
                Id = AutostartIds.For(AutostartSource.StartupTask, TaskId, Family),
                Source = AutostartSource.StartupTask,
                EntryName = TaskId,
                Name = "Terminal",
                Location = Family,
                FileStatus = AutostartFileStatus.Exists,
                Switch = current,
                PackageFamily = Family,
            },
            null,
            AutostartBlocker.None);
    }
}
