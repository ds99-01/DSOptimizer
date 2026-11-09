using System;
using DSOptimizer.Core.Autostart;
using DSOptimizer.Core.Journal;
using DSOptimizer.Core.Model;
using DSOptimizer.Core.Operations;
using DSOptimizer.Tests.Unit.Fakes;
using Xunit;

namespace DSOptimizer.Tests.Unit.Autostart;

public class AutostartJournalTests
{
    private const string Sid = "S-1-5-21-1-2-3-1001";
    private const string OtherSid = "S-1-5-21-1-2-3-1002";
    private const string TaskKey = @"HKU\" + Sid + @"_Classes\Local Settings\Software\Microsoft\Windows\CurrentVersion\AppModel\SystemAppData\Microsoft.WindowsTerminal_8wekyb3d8bbwe\StartTerminalOnLoginTask";
    private const string FamilyKey = @"HKU\" + Sid + @"_Classes\Local Settings\Software\Microsoft\Windows\CurrentVersion\AppModel\SystemAppData\Microsoft.WindowsTerminal_8wekyb3d8bbwe";
    private const string PolicyKey = @"HKLM\SOFTWARE\Microsoft\Windows\CurrentVersion\Policies\System";

    private static readonly AutostartLocations Locations = AutostartLocations.ForUser(Sid);

    private readonly FakeRegistry _registry = new();

    [Fact]
    public void FindActive_MachineScope_AnySid()
    {
        var document = Document(Entry("run:machine:SecurityHealth", OtherSid));

        Assert.Same(document.Entries[0], AutostartJournal.FindActive(document, "RUN:MACHINE:SecurityHealth", Sid));
    }

    [Fact]
    public void FindActive_UserScope_SidMustMatch()
    {
        var other = Entry("run:user:OneDrive", OtherSid);
        var mine = Entry("run:user:OneDrive", Sid.ToLowerInvariant());
        var document = Document(mine, other);

        Assert.Same(mine, AutostartJournal.FindActive(document, "run:user:onedrive", Sid));
        Assert.Null(AutostartJournal.FindActive(Document(other), "run:user:OneDrive", Sid));
    }

    [Fact]
    public void FindActive_IgnoresOtherKinds()
    {
        var tweak = Entry("run:user:OneDrive", Sid, kind: null);
        var removal = Entry("run:user:OneDrive", Sid, kind: JournalEntry.AppRemovalKind);
        var undone = Entry("run:user:OneDrive", Sid, status: JournalEntryStatus.Undone);
        var older = Entry("run:user:OneDrive", Sid);
        var newer = Entry("run:user:OneDrive", Sid, status: JournalEntryStatus.PendingReboot);

        Assert.Null(AutostartJournal.FindActive(Document(tweak, removal, undone), "run:user:OneDrive", Sid));
        Assert.Same(newer, AutostartJournal.FindActive(Document(older, newer, tweak), "run:user:OneDrive", Sid));
    }

    [Fact]
    public void TargetOf_FromFirstEvent()
    {
        var disable = Entry("run:user:A", Sid, AutostartEngine.DisableAction);
        disable.Events.Add(new JournalEvent { Action = AutostartEngine.UndoAction, Outcome = "UndoIncomplete" });
        var enable = Entry("run:user:A", Sid, AutostartEngine.EnableAction);
        var unknown = Entry("run:user:A", Sid, AutostartEngine.ExternalAction);
        var empty = Entry("run:user:A", Sid);
        empty.Events.Clear();

        Assert.Equal(AutostartState.Disabled, AutostartJournal.TargetOf(disable));
        Assert.Equal(AutostartState.Enabled, AutostartJournal.TargetOf(enable));
        Assert.Equal(AutostartState.Unknown, AutostartJournal.TargetOf(unknown));
        Assert.Equal(AutostartState.Unknown, AutostartJournal.TargetOf(empty));
    }

    [Fact]
    public void RestoreBaseline_WritesValuesAndAbsent_EmptyWhenRestored()
    {
        _registry.Set(TaskKey, "State", RegValue.FromDWord(1));
        _registry.Set(TaskKey, "LastDisabledTime", RegValue.FromQWord(1_793_609_249));
        var entry = TaskEntry(
            SnapshotRecord.ForRegistry(TaskKey, "State", RegValue.FromDWord(2)),
            SnapshotRecord.ForRegistry(TaskKey, "LastDisabledTime", RegValue.Absent),
            SnapshotRecord.ForRegistry(TaskKey, "ShowNotification", RegValue.FromDWord(1)));

        Assert.Empty(AutostartJournal.RestoreBaseline(_registry, entry, Locations));

        Assert.Equal(RegValue.FromDWord(2), _registry.Get(TaskKey, "State"));
        Assert.Equal(RegValue.Absent, _registry.Get(TaskKey, "LastDisabledTime"));
        Assert.Equal(RegValue.FromDWord(1), _registry.Get(TaskKey, "ShowNotification"));
    }

    [Fact]
    public void RestoreBaseline_CreatedKey_EmptyAfterRestore_Deleted()
    {
        _registry.Set(TaskKey, "State", RegValue.FromDWord(2));
        _registry.Set(TaskKey, "UserEnabledStartupOnce", RegValue.FromDWord(1));

        Assert.Empty(AutostartJournal.RestoreBaseline(_registry, CreatedTaskEntry(), Locations));

        Assert.False(_registry.HasKey(TaskKey));
    }

    [Fact]
    public void RestoreBaseline_CreatedKeyNotEmpty_KeptNoMismatch()
    {
        _registry.Set(TaskKey, "State", RegValue.FromDWord(2));
        _registry.Set(TaskKey, "UserEnabledStartupOnce", RegValue.FromDWord(1));
        _registry.Set(TaskKey, "AddedByWindows", RegValue.FromDWord(1));

        Assert.Empty(AutostartJournal.RestoreBaseline(_registry, CreatedTaskEntry(), Locations));

        Assert.True(_registry.HasKey(TaskKey));
        Assert.Empty(_registry.DeletedKeys);
        Assert.Equal(RegValue.FromDWord(1), _registry.Get(TaskKey, "AddedByWindows"));
    }

    [Fact]
    public void RestoreBaseline_DeleteKeyThrows_RecordListed()
    {
        _registry.Set(TaskKey, "State", RegValue.FromDWord(2));
        _registry.DenyKey(TaskKey);
        var entry = CreatedTaskEntry();

        Assert.Equal(new[] { entry.Baseline[0].Describe() }, AutostartJournal.RestoreBaseline(_registry, entry, Locations));
    }

    [Fact]
    public void RestoreBaseline_WriteThrows_RecordListedNotThrown()
    {
        var state = SnapshotRecord.ForRegistry(TaskKey, "State", RegValue.FromDWord(2));
        var notification = SnapshotRecord.ForRegistry(TaskKey, "ShowNotification", RegValue.FromDWord(1));
        _registry.FailWrite(TaskKey, "State");

        Assert.Equal(new[] { state.Describe() }, AutostartJournal.RestoreBaseline(_registry, TaskEntry(state, notification), Locations));

        Assert.Equal(RegValue.FromDWord(1), _registry.Get(TaskKey, "ShowNotification"));
    }

    [Fact]
    public void RestoreBaseline_ReadBackDiffers_RecordListed()
    {
        var state = SnapshotRecord.ForRegistry(TaskKey, "State", RegValue.FromDWord(2));
        _registry.AfterWrite = (path, name, _) => _registry.Set(path, name, RegValue.FromDWord(1));

        Assert.Equal(new[] { state.Describe() }, AutostartJournal.RestoreBaseline(_registry, TaskEntry(state), Locations));
    }

    [Fact]
    public void RestoreBaseline_ReadBackDenied_RecordListed()
    {
        var state = SnapshotRecord.ForRegistry(TaskKey, "State", RegValue.FromDWord(2));
        _registry.DenyRead(TaskKey, "State");

        Assert.Equal(new[] { state.Describe() }, AutostartJournal.RestoreBaseline(_registry, TaskEntry(state), Locations));
    }

    [Fact]
    public void RestoreBaseline_RecordOutsideItemLocation_NothingWritten()
    {
        var foreign = SnapshotRecord.ForRegistry(PolicyKey, "EnableLUA", RegValue.FromDWord(0));
        var entry = Entry("run:machine:SecurityHealth", Sid);
        entry.Baseline.Add(foreign);

        Assert.Equal(new[] { foreign.Describe() }, AutostartJournal.RestoreBaseline(_registry, entry, Locations));

        Assert.Equal(RegValue.Absent, _registry.Get(PolicyKey, "EnableLUA"));
        Assert.Empty(_registry.WriteLog);
    }

    [Fact]
    public void RestoreBaseline_OtherValueNameInSameKey_NothingWritten()
    {
        var key = Locations.StartupApprovedKey(AutostartSource.RunMachine);
        var other = SnapshotRecord.ForRegistry(key, "Other", RegValue.FromBinary(new byte[12]));
        var entry = Entry("run:machine:SecurityHealth", Sid);
        entry.Baseline.Add(other);

        Assert.Equal(new[] { other.Describe() }, AutostartJournal.RestoreBaseline(_registry, entry, Locations));

        Assert.Equal(RegValue.Absent, _registry.Get(key, "Other"));
        Assert.Empty(_registry.WriteLog);
    }

    [Fact]
    public void RestoreBaseline_UserItemRecordInOtherAccountHive_NothingWritten()
    {
        var otherKey = AutostartLocations.ForUser(OtherSid).StartupApprovedKey(AutostartSource.RunUser);
        var record = SnapshotRecord.ForRegistry(otherKey, "OneDrive", RegValue.FromBinary(new byte[12]));
        var entry = Entry("run:user:OneDrive", Sid);
        entry.Baseline.Add(record);

        Assert.Equal(new[] { record.Describe() }, AutostartJournal.RestoreBaseline(_registry, entry, Locations));

        Assert.Equal(RegValue.Absent, _registry.Get(otherKey, "OneDrive"));
        Assert.Empty(_registry.WriteLog);
    }

    [Fact]
    public void RestoreBaseline_StartupTaskUnknownValueName_NothingWritten()
    {
        var record = SnapshotRecord.ForRegistry(TaskKey, "Evil", RegValue.FromDWord(1));

        Assert.Equal(new[] { record.Describe() }, AutostartJournal.RestoreBaseline(_registry, TaskEntry(record), Locations));

        Assert.Equal(RegValue.Absent, _registry.Get(TaskKey, "Evil"));
        Assert.Empty(_registry.WriteLog);
    }

    [Fact]
    public void RestoreBaseline_CreatedKeyRootOutsideTaskKey_NothingDeleted()
    {
        _registry.Set(TaskKey, "State", RegValue.FromDWord(2));
        var record = SnapshotRecord.ForRegistry(TaskKey, "State", RegValue.Absent, FamilyKey);
        var writes = _registry.WriteLog.Count;

        Assert.Equal(new[] { record.Describe() }, AutostartJournal.RestoreBaseline(_registry, TaskEntry(record), Locations));

        Assert.Empty(_registry.DeletedKeys);
        Assert.True(_registry.HasKey(FamilyKey));
        Assert.Equal(RegValue.FromDWord(2), _registry.Get(TaskKey, "State"));
        Assert.Equal(writes, _registry.WriteLog.Count);
    }

    [Fact]
    public void RestoreBaseline_OneForeignRecordAmongValid_NothingWrittenAllListed()
    {
        _registry.Set(TaskKey, "State", RegValue.FromDWord(1));
        var state = SnapshotRecord.ForRegistry(TaskKey, "State", RegValue.FromDWord(2));
        var foreign = SnapshotRecord.ForRegistry(PolicyKey, "EnableLUA", RegValue.FromDWord(0));
        var notification = SnapshotRecord.ForRegistry(TaskKey, "ShowNotification", RegValue.FromDWord(1));
        var writes = _registry.WriteLog.Count;

        Assert.Equal(
            new[] { state.Describe(), foreign.Describe(), notification.Describe() },
            AutostartJournal.RestoreBaseline(_registry, TaskEntry(state, foreign, notification), Locations));

        Assert.Equal(RegValue.FromDWord(1), _registry.Get(TaskKey, "State"));
        Assert.Equal(RegValue.Absent, _registry.Get(TaskKey, "ShowNotification"));
        Assert.Equal(RegValue.Absent, _registry.Get(PolicyKey, "EnableLUA"));
        Assert.Equal(writes, _registry.WriteLog.Count);
    }

    [Fact]
    public void RestoreBaseline_UnparsableItemId_NothingWritten()
    {
        var key = Locations.StartupApprovedKey(AutostartSource.RunUser);
        var record = SnapshotRecord.ForRegistry(key, "OneDrive", RegValue.FromBinary(new byte[12]));
        var entry = Entry("nieznane:OneDrive", Sid);
        entry.Baseline.Add(record);

        Assert.Equal(new[] { record.Describe() }, AutostartJournal.RestoreBaseline(_registry, entry, Locations));

        Assert.Equal(RegValue.Absent, _registry.Get(key, "OneDrive"));
        Assert.Empty(_registry.WriteLog);
    }

    [Fact]
    public void RestoreBaseline_StartupApprovedNameDifferentCase_Restored()
    {
        var key = Locations.StartupApprovedKey(AutostartSource.RunUser);
        var original = RegValue.FromBinary(new byte[] { 2, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0 });
        _registry.Set(key, "OneDrive", RegValue.FromBinary(new byte[] { 3, 0, 0, 0, 1, 2, 3, 4, 5, 6, 7, 8 }));
        var entry = Entry("RUN:USER:onedrive", Sid);
        entry.Baseline.Add(SnapshotRecord.ForRegistry(key.ToUpperInvariant(), "OneDrive", original));

        Assert.Empty(AutostartJournal.RestoreBaseline(_registry, entry, Locations));

        Assert.Equal(original, _registry.Get(key, "OneDrive"));
    }

    private static JournalEntry CreatedTaskEntry() => TaskEntry(
        SnapshotRecord.ForRegistry(TaskKey, "State", RegValue.Absent, TaskKey),
        SnapshotRecord.ForRegistry(TaskKey, "LastDisabledTime", RegValue.Absent),
        SnapshotRecord.ForRegistry(TaskKey, "UserEnabledStartupOnce", RegValue.Absent),
        SnapshotRecord.ForRegistry(TaskKey, "ShowNotification", RegValue.Absent));

    private static JournalEntry TaskEntry(params SnapshotRecord[] baseline)
    {
        var entry = Entry("appx:Microsoft.WindowsTerminal_8wekyb3d8bbwe!StartTerminalOnLoginTask", Sid, AutostartEngine.EnableAction);
        entry.Baseline.AddRange(baseline);
        return entry;
    }

    private static JournalDocument Document(params JournalEntry[] entries)
    {
        var document = new JournalDocument { Machine = "TEST-PC" };
        document.Entries.AddRange(entries);
        return document;
    }

    private static JournalEntry Entry(
        string id,
        string sid,
        string action = AutostartEngine.DisableAction,
        string? kind = JournalEntry.AutostartKind,
        JournalEntryStatus status = JournalEntryStatus.Active)
    {
        var entry = new JournalEntry
        {
            EntryId = Guid.NewGuid(),
            Kind = kind,
            TweakId = id,
            UserSid = sid,
            Status = status,
            AppliedAt = new DateTimeOffset(2026, 11, 2, 8, 37, 53, TimeSpan.Zero),
        };
        entry.Events.Add(new JournalEvent { Action = action, Outcome = "Started" });
        return entry;
    }
}
