using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using DSOptimizer.Core.Apps;
using DSOptimizer.Core.Autostart;
using DSOptimizer.Core.Engine;
using DSOptimizer.Core.Errors;
using DSOptimizer.Core.Gui;
using DSOptimizer.Core.Journal;
using DSOptimizer.Core.Model;
using DSOptimizer.Core.Operations;
using DSOptimizer.Tests.Unit.Fakes;
using Xunit;

namespace DSOptimizer.Tests.Unit.Autostart;

/// <summary>Cofanie wpisu autostartu z Historii zmian i przywracanie stanu po częściowym zapisie zadania startowego.</summary>
public class AutostartEngineUndoTests
{
    private const string Sid = "S-1-5-21-1-2-3-1001";
    private const string OtherSid = "S-1-5-21-1-2-3-1002";
    private const string Family = "Microsoft.WindowsTerminal_8wekyb3d8bbwe";
    private const string TaskId = "StartTerminalOnLoginTask";
    private const string Name = "OneDrive";

    private static readonly AutostartLocations Locations = AutostartLocations.ForUser(Sid);

    private static readonly AutostartSource[] AllApproved =
    {
        AutostartSource.RunUser, AutostartSource.RunMachine, AutostartSource.Run32, AutostartSource.FolderUser, AutostartSource.FolderCommon,
    };

    private static readonly byte[] Zeros = new byte[8];

    private readonly FakeRegistry _registry = new();
    private readonly FakeJournalStore _journal = new();
    private readonly FakeClock _clock = new() { UtcNow = new DateTimeOffset(2026, 11, 2, 8, 37, 53, 977, TimeSpan.Zero) };
    private readonly ListLog _log = new();

    private static string ApprovedKey => Locations.StartupApprovedKey(AutostartSource.RunUser);

    private static string MachineApprovedKey => Locations.StartupApprovedKey(AutostartSource.RunMachine);

    private static string TaskKey => Locations.StartupTaskKey(Family, TaskId);

    private static string FamilyKey => TaskKey.Substring(0, TaskKey.LastIndexOf('\\'));

    [Fact]
    public void Undo_RestoresAbsent_Undone_StartupApprovedKeysStay()
    {
        var entry = Disable(ApprovedRow());
        Assert.True(_registry.Get(ApprovedKey, Name).Exists);

        var result = Engine().Undo(entry.EntryId, Context());

        Assert.Equal(TweakOutcome.Undone, result.Outcome);
        Assert.Equal(AutostartText.UndoneFromHistory, result.Message);
        Assert.Equal("run:user:OneDrive", result.TweakId);
        Assert.Empty(result.NotRestored);
        Assert.Equal(RegValue.Absent, _registry.Get(ApprovedKey, Name));
        Assert.True(_registry.HasKey(ApprovedKey));
        Assert.Empty(_registry.DeletedKeys);
        var saved = Assert.Single(_journal.Current.Entries);
        Assert.Equal(JournalEntryStatus.Undone, saved.Status);
        Assert.Equal(
            new[] { ("disable-autostart", "Started"), ("disable-autostart", "Applied"), ("undo", "Started"), ("undo", "Undone") },
            Events(saved));
        Assert.Equal(AutostartText.UndoneFromHistory, saved.Events.Last().Message);
    }

    [Fact]
    public void Undo_RestoresBytes()
    {
        var original = RegValue.FromBinary(Approved(0x06, 0x0A, 0x0B, 0x0C, Zeros));
        _registry.Set(ApprovedKey, Name, original);
        var entry = Disable(ApprovedRow());
        Assert.NotEqual(original, _registry.Get(ApprovedKey, Name));

        var result = Engine().Undo(entry.EntryId, Context());

        Assert.Equal(TweakOutcome.Undone, result.Outcome);
        Assert.Equal(original, _registry.Get(ApprovedKey, Name));
    }

    [Fact]
    public void Undo_StartupTaskCreatedKey_DeletesEmptyKey()
    {
        _registry.CreateKey(FamilyKey);
        var entry = Enable(TaskRow());
        Assert.True(_registry.HasKey(TaskKey));

        var result = Engine().Undo(entry.EntryId, Context());

        Assert.Equal(TweakOutcome.Undone, result.Outcome);
        Assert.False(_registry.HasKey(TaskKey));
        Assert.True(_registry.HasKey(FamilyKey));
        Assert.Equal(JournalEntryStatus.Undone, Assert.Single(_journal.Current.Entries).Status);
    }

    [Fact]
    public void Undo_StartupTaskExistingKey_RestoresFourValues()
    {
        _registry.Set(TaskKey, "State", RegValue.FromDWord(1));
        _registry.Set(TaskKey, "LastDisabledTime", RegValue.FromQWord(1_793_000_000));
        _registry.Set(TaskKey, "ShowNotification", RegValue.FromDWord(1));
        var entry = Enable(TaskRow());
        Assert.Equal(RegValue.FromDWord(2), _registry.Get(TaskKey, "State"));

        var result = Engine().Undo(entry.EntryId, Context());

        Assert.Equal(TweakOutcome.Undone, result.Outcome);
        Assert.Equal(RegValue.FromDWord(1), _registry.Get(TaskKey, "State"));
        Assert.Equal(RegValue.FromQWord(1_793_000_000), _registry.Get(TaskKey, "LastDisabledTime"));
        Assert.Equal(RegValue.Absent, _registry.Get(TaskKey, "UserEnabledStartupOnce"));
        Assert.Equal(RegValue.FromDWord(1), _registry.Get(TaskKey, "ShowNotification"));
        Assert.True(_registry.HasKey(TaskKey));
    }

    [Fact]
    public void Undo_VerifyMismatch_UndoIncomplete_EntryStaysActive()
    {
        var entry = Disable(ApprovedRow());
        // Inny program zapisuje wartość tuż po przywróceniu: świeży odczyt nie zgadza się z baseline.
        _registry.AfterWrite = (path, name, _) => _registry.Set(path, name, RegValue.FromBinary(Approved(0x06, 0, 0, 0, Zeros)));

        var result = Engine().Undo(entry.EntryId, Context());

        Assert.Equal(TweakOutcome.UndoIncomplete, result.Outcome);
        Assert.Null(result.Message);
        Assert.Equal(new[] { SnapshotRecord.ForRegistry(ApprovedKey, Name, RegValue.Absent).Describe() }, result.NotRestored);
        var saved = Assert.Single(_journal.Current.Entries);
        Assert.Equal(JournalEntryStatus.Active, saved.Status);
        Assert.Equal(("undo", "UndoIncomplete"), Events(saved).Last());
        Assert.Equal(result.NotRestored.Single(), saved.Events.Last().Message);
    }

    [Fact]
    public void Undo_NotActive_Refused()
    {
        var entry = Disable(ApprovedRow());
        Assert.Equal(TweakOutcome.Undone, Engine().Undo(entry.EntryId, Context()).Outcome);
        var writes = _registry.WriteLog.Count;
        var saves = _journal.SaveCount;

        var result = Engine().Undo(entry.EntryId, Context());

        Assert.Equal(TweakOutcome.Refused, result.Outcome);
        Assert.Equal(AutostartText.NoActiveEntry, result.Message);
        Assert.Equal(writes, _registry.WriteLog.Count);
        Assert.Equal(saves, _journal.SaveCount);
    }

    [Fact]
    public void Undo_NotNewestActive_Refused()
    {
        var older = Disable(ApprovedRow(AutostartSource.RunMachine));
        var document = _journal.Load().Document;
        var newer = Copy(older);
        newer.TweakId = "RUN:MACHINE:onedrive";
        document.Entries.Add(newer);
        _journal.Save(document);
        var value = _registry.Get(MachineApprovedKey, Name);

        var result = Engine().Undo(older.EntryId, Context());

        Assert.Equal(TweakOutcome.Refused, result.Outcome);
        Assert.Equal(HistoryText.UndoNotNewest, result.Message);
        Assert.Equal(value, _registry.Get(MachineApprovedKey, Name));
        Assert.All(_journal.Current.Entries, saved => Assert.Equal(JournalEntryStatus.Active, saved.Status));
    }

    [Fact]
    public void Undo_UnknownEntry_Refused()
    {
        var entry = Disable(ApprovedRow());
        var document = _journal.Load().Document;
        var tweak = Copy(entry);
        tweak.Kind = null;
        document.Entries.Add(tweak);
        _journal.Save(document);
        var writes = _registry.WriteLog.Count;

        var unknown = Engine().Undo(Guid.NewGuid(), Context());
        var otherKind = Engine().Undo(tweak.EntryId, Context());

        Assert.Equal(TweakOutcome.Refused, unknown.Outcome);
        Assert.Equal(AutostartText.NoActiveEntry, unknown.Message);
        Assert.Equal(TweakOutcome.Refused, otherKind.Outcome);
        Assert.Equal(AutostartText.NoActiveEntry, otherKind.Message);
        Assert.Equal(writes, _registry.WriteLog.Count);
    }

    [Fact]
    public void Undo_UnparsableItemId_Refused_NothingWritten()
    {
        var entry = Disable(ApprovedRow());
        var document = _journal.Load().Document;
        document.Entries.Single().TweakId = "nieznane:OneDrive";
        _journal.Save(document);
        var writes = _registry.WriteLog.Count;

        var result = Engine().Undo(entry.EntryId, Context());

        Assert.Equal(TweakOutcome.Refused, result.Outcome);
        Assert.Equal(AutostartText.NoActiveEntry, result.Message);
        Assert.Equal(writes, _registry.WriteLog.Count);
    }

    [Fact]
    public void Undo_UserScopeOtherSid_Refused()
    {
        var entry = Disable(ApprovedRow());
        var value = _registry.Get(ApprovedKey, Name);

        var result = Engine().Undo(entry.EntryId, Context(sid: OtherSid));

        Assert.Equal(TweakOutcome.Refused, result.Outcome);
        Assert.Equal(HistoryText.UndoOtherAccount(Sid), result.Message);
        Assert.Equal(value, _registry.Get(ApprovedKey, Name));
        Assert.Equal(JournalEntryStatus.Active, Assert.Single(_journal.Current.Entries).Status);
    }

    [Fact]
    public void Undo_UserUncertain_Refused()
    {
        var entry = Disable(ApprovedRow());
        var saves = _journal.SaveCount;

        var result = Engine().Undo(entry.EntryId, Context(certain: false));

        Assert.Equal(TweakOutcome.Refused, result.Outcome);
        Assert.Equal(AppsText.UserUncertain, result.Message);
        Assert.Equal(saves, _journal.SaveCount);
    }

    [Theory]
    [InlineData(false, false)]
    [InlineData(true, true)]
    public void Undo_UserHiveMissing_OtherProcessAccount_Refused(bool storeEntry, bool userHiveLoaded)
    {
        if (storeEntry)
        {
            _registry.Set(TaskKey, "State", RegValue.FromDWord(2));
        }

        var entry = storeEntry ? Disable(TaskRow()) : Disable(ApprovedRow());
        var context = Context(processSid: OtherSid, userHive: userHiveLoaded, classesHive: !userHiveLoaded);

        var result = Engine().Undo(entry.EntryId, context);

        Assert.Equal(TweakOutcome.Refused, result.Outcome);
        Assert.Equal(AutostartText.Blocked(AutostartBlocker.UserHiveMissing), result.Message);
        Assert.Equal(JournalEntryStatus.Active, Assert.Single(_journal.Current.Entries).Status);
    }

    [Fact]
    public void Undo_UserHiveMissing_SameProcessAccount_Undone()
    {
        var entry = Disable(ApprovedRow());

        var result = Engine().Undo(entry.EntryId, Context(userHive: false, classesHive: false));

        Assert.Equal(TweakOutcome.Undone, result.Outcome);
    }

    [Fact]
    public void Undo_MachineScope_AnyAccount_Undone()
    {
        var entry = Disable(ApprovedRow(AutostartSource.RunMachine));

        var result = Engine().Undo(entry.EntryId, Context(sid: OtherSid, certain: false, processSid: "S-1-5-18", userHive: false, classesHive: false));

        Assert.Equal(TweakOutcome.Undone, result.Outcome);
        Assert.Equal(RegValue.Absent, _registry.Get(MachineApprovedKey, Name));
        Assert.Equal(JournalEntryStatus.Undone, Assert.Single(_journal.Current.Entries).Status);
    }

    [Fact]
    public void Undo_SavesStartedEventBeforeRestore()
    {
        var entry = Disable(ApprovedRow());
        var onDiskAtRestore = new List<(string Action, string Outcome)>();
        _registry.WriteInterceptor = (_, _, _) =>
        {
            onDiskAtRestore.AddRange(Events(Assert.Single(_journal.Current.Entries)));
            return null;
        };

        Assert.Equal(TweakOutcome.Undone, Engine().Undo(entry.EntryId, Context()).Outcome);

        Assert.Equal(("undo", "Started"), onDiskAtRestore.Last());
    }

    [Fact]
    public void Undo_JournalSaveFailsBeforeRestore_Failed_RegistryUntouched()
    {
        var entry = Disable(ApprovedRow());
        var value = _registry.Get(ApprovedKey, Name);
        var writes = _registry.WriteLog.Count;
        _journal.FailSave = true;

        var result = Engine().Undo(entry.EntryId, Context());

        Assert.Equal(TweakOutcome.Failed, result.Outcome);
        Assert.Equal(AutostartText.JournalFailed("Zapis journala nie powiódł się (fake)."), result.Message);
        Assert.Equal(writes, _registry.WriteLog.Count);
        Assert.Equal(value, _registry.Get(ApprovedKey, Name));
        var saved = Assert.Single(_journal.Current.Entries);
        Assert.Equal(JournalEntryStatus.Active, saved.Status);
        Assert.Equal(("disable-autostart", "Applied"), Events(saved).Last());
    }

    [Fact]
    public void Undo_JournalLoadFails_Failed()
    {
        var entry = Disable(ApprovedRow());
        _journal.FailLoad = true;

        var result = Engine().Undo(entry.EntryId, Context());

        Assert.Equal(TweakOutcome.Failed, result.Outcome);
        Assert.Equal(AutostartText.JournalFailed("Odczyt journala nie powiódł się (fake)."), result.Message);
        Assert.Equal(entry.EntryId.ToString(), result.TweakId);
    }

    [Fact]
    public void Undo_JournalSaveAfterRestoreFails_UndoneWithWarning()
    {
        var entry = Disable(ApprovedRow());
        _journal.SaveException = new JournalStoreException("dysk pełny (test)");
        _journal.ThrowOnSaveFrom = _journal.SaveCount + 1;

        var result = Engine().Undo(entry.EntryId, Context());

        Assert.Equal(TweakOutcome.Undone, result.Outcome);
        Assert.Contains(result.Warnings, warning => warning.Contains("dysk pełny (test)"));
    }

    [Fact]
    public void Undo_LoadWarning_InResultWarnings()
    {
        var entry = Disable(ApprovedRow());
        _journal.LoadWarning = "journal był uszkodzony (test)";

        var result = Engine().Undo(entry.EntryId, Context());

        Assert.Equal(TweakOutcome.Undone, result.Outcome);
        Assert.Contains("journal był uszkodzony (test)", result.Warnings);
    }

    [Fact]
    public void Toggle_ActiveOppositeEntry_JournalSaveFailsBeforeRestore_Failed_RegistryUntouched()
    {
        Disable(ApprovedRow());
        var value = _registry.Get(ApprovedKey, Name);
        var writes = _registry.WriteLog.Count;
        _journal.FailSave = true;

        var result = Assert.Single(Engine().Set(new[] { ApprovedRow() }, AutostartState.Enabled, Context(), new AutostartRunOptions(), CancellationToken.None).Results);

        Assert.Equal(TweakOutcome.Failed, result.Outcome);
        Assert.Equal(AutostartText.JournalFailed("Zapis journala nie powiódł się (fake)."), result.Message);
        Assert.Equal(writes, _registry.WriteLog.Count);
        Assert.Equal(value, _registry.Get(ApprovedKey, Name));
        Assert.Equal(JournalEntryStatus.Active, Assert.Single(_journal.Current.Entries).Status);
    }

    [Fact]
    public void Toggle_ActiveOppositeEntry_SavesStartedEventBeforeRestore()
    {
        Disable(ApprovedRow());
        var onDiskAtRestore = new List<(string Action, string Outcome)>();
        _registry.WriteInterceptor = (_, _, _) =>
        {
            onDiskAtRestore.AddRange(Events(Assert.Single(_journal.Current.Entries)));
            return null;
        };

        var result = Assert.Single(Engine().Set(new[] { ApprovedRow() }, AutostartState.Enabled, Context(), new AutostartRunOptions(), CancellationToken.None).Results);

        Assert.Equal(TweakOutcome.Undone, result.Outcome);
        Assert.Equal(AutostartText.UndoneByToggle, result.Message);
        Assert.Equal(("undo", "Started"), onDiskAtRestore.Last());
        Assert.Equal(("undo", "Undone"), Events(Assert.Single(_journal.Current.Entries)).Last());
    }

    [Fact]
    public void UndoActive_NoActiveEntry_RefusedNoActiveEntryText()
    {
        var result = Engine().UndoActive("run:user:OneDrive", Context());

        Assert.Equal(TweakOutcome.Refused, result.Outcome);
        Assert.Equal(AutostartText.NoActiveEntry, result.Message);
        Assert.Equal("run:user:OneDrive", result.TweakId);
        Assert.Equal(0, _journal.SaveCount);
    }

    [Fact]
    public void UndoActive_FindsByIdAndSid()
    {
        var mine = Disable(ApprovedRow());
        var document = _journal.Load().Document;
        var theirs = Copy(mine);
        theirs.UserSid = OtherSid;
        document.Entries.Add(theirs);
        _journal.Save(document);

        var result = Engine().UndoActive("RUN:USER:onedrive", Context());

        Assert.Equal(TweakOutcome.Undone, result.Outcome);
        var saved = _journal.Current.Entries;
        Assert.Equal(JournalEntryStatus.Undone, saved.Single(entry => entry.EntryId == mine.EntryId).Status);
        Assert.Equal(JournalEntryStatus.Active, saved.Single(entry => entry.EntryId == theirs.EntryId).Status);
        Assert.Equal(RegValue.Absent, _registry.Get(ApprovedKey, Name));
    }

    [Fact]
    public void UndoActive_ReadsJournalOnce()
    {
        Disable(ApprovedRow());
        var loads = _journal.LoadCount;

        var result = Engine().UndoActive("run:user:OneDrive", Context());

        Assert.Equal(TweakOutcome.Undone, result.Outcome);
        Assert.Equal(loads + 1, _journal.LoadCount);
    }

    [Fact]
    public void UndoActive_JournalLoadFails_Failed()
    {
        _journal.FailLoad = true;

        var result = Engine().UndoActive("run:user:OneDrive", Context());

        Assert.Equal(TweakOutcome.Failed, result.Outcome);
        Assert.Equal(AutostartText.JournalFailed("Odczyt journala nie powiódł się (fake)."), result.Message);
    }

    [Fact]
    public void Undo_NullArguments_Throw()
    {
        var engine = Engine();
        Assert.Throws<ArgumentNullException>(() => engine.Undo(Guid.NewGuid(), null!));
        Assert.Throws<ArgumentNullException>(() => engine.UndoActive(null!, Context()));
        Assert.Throws<ArgumentNullException>(() => engine.UndoActive("run:user:OneDrive", null!));
    }

    // --- przywrócenie po częściowym zapisie zadania startowego (ta sama ścieżka RestoreBaseline) ---

    [Fact]
    public void StartupTask_EnableFromZero_StateWriteThrows_FailedRolledBack_TaskKeyDeleted()
    {
        _registry.CreateKey(FamilyKey);
        var stateWrites = 0;
        _registry.WriteInterceptor = (_, name, _) =>
            name == "State" && ++stateWrites == 1 ? new OperationException(OperationErrorKind.AccessDenied, "odmowa (test)") : null;

        var result = Assert.Single(Engine().Set(new[] { TaskRow() }, AutostartState.Enabled, Context(), new AutostartRunOptions(), CancellationToken.None).Results);

        Assert.Equal(TweakOutcome.FailedRolledBack, result.Outcome);
        Assert.Equal(AutostartText.NotConfirmed, result.Message);
        Assert.Contains(@"\UserEnabledStartupOnce=", string.Join("\n", _registry.WriteLog));
        Assert.False(_registry.HasKey(TaskKey));
        Assert.True(_registry.HasKey(FamilyKey));
        var entry = Assert.Single(_journal.Current.Entries);
        Assert.Equal(JournalEntryStatus.Aborted, entry.Status);
        Assert.Equal(("enable-autostart", "FailedRolledBack"), Events(entry).Last());
        Assert.Contains("odmowa (test)", entry.Events.Last().Reason);
    }

    [Fact]
    public void StartupTask_Disable_StateWriteThrows_FailedRolledBack_ValuesRestored()
    {
        _registry.Set(TaskKey, "State", RegValue.FromDWord(2));
        _registry.Set(TaskKey, "UserEnabledStartupOnce", RegValue.FromDWord(1));
        var stateWrites = 0;
        _registry.WriteInterceptor = (_, name, _) =>
            name == "State" && ++stateWrites == 1 ? new OperationException(OperationErrorKind.AccessDenied, "odmowa (test)") : null;

        var result = Assert.Single(Engine().Set(new[] { TaskRow() }, AutostartState.Disabled, Context(), new AutostartRunOptions(), CancellationToken.None).Results);

        Assert.Equal(TweakOutcome.FailedRolledBack, result.Outcome);
        Assert.Contains(@"\LastDisabledTime=", string.Join("\n", _registry.WriteLog));
        Assert.Equal(RegValue.FromDWord(2), _registry.Get(TaskKey, "State"));
        Assert.Equal(RegValue.Absent, _registry.Get(TaskKey, "LastDisabledTime"));
        Assert.Equal(RegValue.FromDWord(1), _registry.Get(TaskKey, "UserEnabledStartupOnce"));
        Assert.Equal(RegValue.Absent, _registry.Get(TaskKey, "ShowNotification"));
        Assert.True(_registry.HasKey(TaskKey));
        Assert.Empty(_registry.DeletedKeys);
        Assert.Equal(JournalEntryStatus.Aborted, Assert.Single(_journal.Current.Entries).Status);
    }

    private JournalEntry Disable(AutostartRow row) => Apply(row, AutostartState.Disabled);

    private JournalEntry Enable(AutostartRow row) => Apply(row, AutostartState.Enabled);

    private JournalEntry Apply(AutostartRow row, AutostartState target)
    {
        var result = Assert.Single(Engine().Set(new[] { row }, target, Context(), new AutostartRunOptions(), CancellationToken.None).Results);
        Assert.Equal(TweakOutcome.Applied, result.Outcome);
        return _journal.Current.Entries.Last();
    }

    /// <summary>Drugi wpis z tym samym baseline i zdarzeniami, nowy identyfikator.</summary>
    private static JournalEntry Copy(JournalEntry entry)
    {
        var copy = new JournalEntry
        {
            EntryId = Guid.NewGuid(),
            Kind = entry.Kind,
            TweakId = entry.TweakId,
            Title = entry.Title,
            UserSid = entry.UserSid,
            Status = entry.Status,
            AppliedAt = entry.AppliedAt,
        };
        copy.Baseline.AddRange(entry.Baseline);
        copy.Events.AddRange(entry.Events);
        return copy;
    }

    private static byte[] Approved(byte b0, byte b1, byte b2, byte b3, byte[] filetime)
        => new[] { b0, b1, b2, b3 }.Concat(filetime).ToArray();

    private static (string Action, string Outcome)[] Events(JournalEntry entry)
        => entry.Events.Select(e => (e.Action, e.Outcome)).ToArray();

    private static AutostartContext Context(
        string sid = Sid,
        bool certain = true,
        string? processSid = null,
        bool userHive = true,
        bool classesHive = true)
        => new(sid, certain, processSid ?? sid, userHive, classesHive, AutostartLocations.ForUser(sid), AllApproved, true, true);

    private AutostartEngine Engine() => new(_registry, _journal, _log, _clock);

    private AutostartRow ApprovedRow(AutostartSource source = AutostartSource.RunUser)
    {
        var current = AutostartSwitchReader.Read(_registry, AutostartSwitchKind.StartupApproved, Locations.StartupApprovedKey(source), Name, enabledInManifest: false);
        return new AutostartRow(
            new AutostartEntry
            {
                Id = AutostartIds.For(source, Name),
                Source = source,
                EntryName = Name,
                Name = Name + " app",
                Location = "loc",
                FileStatus = AutostartFileStatus.Exists,
                Switch = current,
            },
            null,
            AutostartBlocker.None);
    }

    private AutostartRow TaskRow()
    {
        var current = AutostartSwitchReader.Read(_registry, AutostartSwitchKind.StartupTask, TaskKey, "State", enabledInManifest: false);
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
