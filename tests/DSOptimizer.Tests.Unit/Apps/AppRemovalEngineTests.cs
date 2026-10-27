using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using DSOptimizer.Core.Abstractions;
using DSOptimizer.Core.Apps;
using DSOptimizer.Core.Engine;
using DSOptimizer.Core.Errors;
using DSOptimizer.Core.Journal;
using DSOptimizer.Tests.Unit.Fakes;
using DSOptimizer.Tests.Unit.Support;
using Xunit;

namespace DSOptimizer.Tests.Unit.Apps;

public class AppRemovalEngineTests
{
    private const string UserSid = "S-1-5-21-1000";
    private const string OtherSid = "S-1-5-21-2000";
    private const string UninstallRoot = @"HKLM\SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall\";

    /// <summary>Obecność aplikacji: zbiór obecnych Id + opóźnienie zniknięcia (ile kolejnych odczytów po usunięciu nadal widzi aplikację).</summary>
    private sealed class FakeAppsReader : IInstalledAppsReader
    {
        private readonly Dictionary<string, int> _lag = new(StringComparer.Ordinal);

        public HashSet<string> Present { get; } = new(StringComparer.Ordinal);

        public List<(string Id, string Sid, AppPresenceScope Scope)> Calls { get; } = new();

        public Exception? Throw { get; set; }

        /// <summary>Gdy ustawione, każdy odczyt zwraca tę wartość (np. <see cref="AppPresence.Unknown"/> po próbie).</summary>
        public AppPresence? Answer { get; set; }

        public void MarkRemoved(string id, int lag) => _lag[id] = lag;

        public InstalledAppsReading Read(string userSid) => throw new InvalidOperationException("Silnik nie powinien czytać całej listy.");

        public AppPresence ReadPresence(InstalledApp app, string userSid, AppPresenceScope scope)
        {
            Calls.Add((app.Id, userSid, scope));
            if (Throw is not null)
            {
                throw Throw;
            }

            if (Answer is { } answer)
            {
                return answer;
            }

            if (!Present.Contains(app.Id))
            {
                return AppPresence.Absent;
            }

            if (_lag.TryGetValue(app.Id, out var left))
            {
                if (left <= 0)
                {
                    Present.Remove(app.Id);
                    return AppPresence.Absent;
                }

                _lag[app.Id] = left - 1;
            }

            return AppPresence.Present;
        }
    }

    private sealed class FakeAppRemover : IAppRemover
    {
        private readonly FakeAppsReader _reader;

        public FakeAppRemover(FakeAppsReader reader) => _reader = reader;

        public List<(AppxInfo Appx, string Sid, StoreRemovalScope Scope, bool Deprovision)> StoreCalls { get; } = new();

        public List<(UninstallPlan Plan, TimeSpan Timeout)> UninstallerCalls { get; } = new();

        /// <summary>Wynik skryptu AppX; null = sukces z kodem 0.</summary>
        public Func<AppxInfo, ScriptResult>? StoreResult { get; set; }

        public UninstallerRun Run { get; set; } = new(0, false);

        public Exception? UninstallerThrows { get; set; }

        /// <summary>Czy próba naprawdę usuwa (dla aplikacji „zostaje” = false).</summary>
        public bool Removes { get; set; } = true;

        /// <summary>Ile odczytów po próbie aplikacja jeszcze jest (deinstalator dokańcza w tle).</summary>
        public int Lag { get; set; }

        public Action? OnAttempt { get; set; }

        public int Attempts => StoreCalls.Count + UninstallerCalls.Count;

        public ScriptResult RemoveStoreApp(AppxInfo appx, string userSid, StoreRemovalScope scope, bool deprovision, CancellationToken ct)
        {
            StoreCalls.Add((appx, userSid, scope, deprovision));
            OnAttempt?.Invoke();
            var result = StoreResult?.Invoke(appx) ?? new ScriptResult { ExitCode = 0 };
            Effect("appx:" + appx.PackageFamilyName);
            return result;
        }

        public UninstallerRun RunUninstaller(UninstallPlan plan, TimeSpan timeout, CancellationToken ct)
        {
            UninstallerCalls.Add((plan, timeout));
            OnAttempt?.Invoke();
            if (UninstallerThrows is not null)
            {
                throw UninstallerThrows;
            }

            Effect("win32:" + UninstallRoot + System.IO.Path.GetFileNameWithoutExtension(plan.FileName));
            return Run;
        }

        private void Effect(string id)
        {
            if (Removes)
            {
                _reader.MarkRemoved(id, Lag);
            }
        }
    }

    private sealed class Fixture
    {
        public Fixture(string? processSid = null)
        {
            Remover = new FakeAppRemover(Reader);
            Sleeper = new FakeSleeper(Clock);
            Engine = new AppRemovalEngine(Reader, Remover, Journal, RestorePoints, User, processSid ?? UserSid, Log, Clock, Sleeper.Sleep);
        }

        public FakeAppsReader Reader { get; } = new();

        public FakeAppRemover Remover { get; }

        public FakeJournalStore Journal { get; } = new();

        public FakeRestorePointService RestorePoints { get; } = new();

        public FakeUserContext User { get; } = new() { Sid = UserSid };

        public ListLog Log { get; } = new();

        public FakeClock Clock { get; } = new();

        public FakeSleeper Sleeper { get; }

        public AppRemovalEngine Engine { get; }

        public BatchResult Remove(AppRemovalOptions? options = null, params AppRow[] rows)
        {
            foreach (var row in rows)
            {
                Reader.Present.Add(row.App.Id);
            }

            return Engine.Remove(rows, options ?? new AppRemovalOptions(), CancellationToken.None);
        }
    }

    private static AppRow StoreRow(string package, string? bloatGroup = "microsoft", AppRemovability removability = AppRemovability.Removable)
    {
        var app = new InstalledApp(
            "appx:" + package + "_fam", AppKind.Store, package + " (nazwa)", "Microsoft Corporation", "1.0", AppScope.User, UserSid,
            new AppxInfo(package + "_1.0_x64__fam", package + "_fam", package, false, false, false, "Store", true), null);
        return new AppRow(app, AppGroup.Suggested, bloatGroup, null, null, null, removability, null, null);
    }

    private static AppRow ProgramRow(
        string name,
        UninstallMode mode = UninstallMode.Quiet,
        string? cleanupUrl = null,
        AppRemovability removability = AppRemovability.Removable,
        AppScope scope = AppScope.Machine,
        string? owner = null)
    {
        var key = (owner is null ? UninstallRoot : @"HKU\" + owner + @"\SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall\") + name;
        var app = new InstalledApp(
            "win32:" + key, AppKind.Program, name + " (program)", "Vendor", "1.0", scope, owner,
            null, new Win32Info(key, "\"C:\\Program Files\\V\\" + name + ".exe\"", null, false, null, null, false));
        var plan = new UninstallPlan(@"C:\Program Files\V\" + name + ".exe", "/S", mode);
        return new AppRow(app, AppGroup.Other, "producent", null, null, cleanupUrl, removability, null, plan);
    }

    private static JournalEntry SingleEntry(Fixture f) => Assert.Single(f.Journal.Current.Entries);

    [Theory]
    [InlineData(AppRemovability.Protected)]
    [InlineData(AppRemovability.OtherAccount)]
    [InlineData(AppRemovability.UserUncertain)]
    [InlineData(AppRemovability.NoSafeCommand)]
    public void Remove_ProtectedOrBlockedRow_Throws_NothingRuns(AppRemovability removability)
    {
        var f = new Fixture();

        Assert.Throws<ArgumentException>(() => f.Remove(null, ProgramRow("Ok"), StoreRow("Microsoft.WindowsCalculator", null, removability)));

        Assert.Empty(f.Reader.Calls);
        Assert.Equal(0, f.Remover.Attempts);
        Assert.Empty(f.RestorePoints.Descriptions);
        Assert.Equal(0, f.Journal.SaveCount);
    }

    [Fact]
    public void Remove_ProtectedGroupWithRemovableFlag_Throws_NothingRuns()
    {
        // Obrona w głąb: wiersz chroniony podrobiony przez `with` (np. w CLI) nie przechodzi, nawet z Removability=Removable.
        var f = new Fixture();
        var forged = StoreRow("Microsoft.WindowsCalculator") with { Group = AppGroup.Protected };

        Assert.Throws<ArgumentException>(() => f.Remove(null, forged));

        Assert.Empty(f.Reader.Calls);
        Assert.Equal(0, f.Remover.Attempts);
        Assert.Empty(f.RestorePoints.Descriptions);
    }

    [Fact]
    public void Remove_OtherAccountProgramInUnloadedHive_IsRefusedBeforeAnyPresenceRead()
    {
        // Wpis w niezaładowanym HKU\<inny SID> daje IsPresent=false – silnik nie może tego uznać za „usunięte”, więc wiersz
        // innego konta nie dojdzie nawet do odczytu obecności (reguła 1).
        var f = new Fixture();
        var foreign = ProgramRow("Cudzy", removability: AppRemovability.OtherAccount, scope: AppScope.User, owner: OtherSid);

        var error = Assert.Throws<ArgumentException>(() => f.Engine.Remove(new[] { foreign }, new AppRemovalOptions(), CancellationToken.None));

        Assert.Contains(foreign.App.Id, error.Message, StringComparison.Ordinal);
        Assert.Empty(f.Reader.Calls);
        Assert.Equal(0, f.Remover.Attempts);
    }

    [Fact]
    public void Remove_RowWithoutRemovalData_Throws_NothingRuns()
    {
        var f = new Fixture();
        var programWithoutPlan = ProgramRow("BezPlanu") with { Plan = null };
        var storeWithoutAppx = StoreRow("Microsoft.BingNews") with { App = StoreRow("Microsoft.BingNews").App with { Appx = null } };

        Assert.Throws<ArgumentException>(() => f.Remove(null, programWithoutPlan));
        Assert.Throws<ArgumentException>(() => f.Remove(null, storeWithoutAppx));
        Assert.Throws<ArgumentException>(() => f.Engine.Remove(new AppRow[] { null! }, new AppRemovalOptions(), CancellationToken.None));
        Assert.Equal(0, f.Remover.Attempts);
        Assert.Empty(f.Reader.Calls);
    }

    [Fact]
    public void Remove_NullArguments_Throw()
    {
        var f = new Fixture();

        Assert.Throws<ArgumentNullException>(() => f.Engine.Remove(null!, new AppRemovalOptions(), CancellationToken.None));
        Assert.Throws<ArgumentNullException>(() => f.Engine.Remove(Array.Empty<AppRow>(), null!, CancellationToken.None));
    }

    [Fact]
    public void Remove_Program_ForcesRestorePointEvenWhenSettingOff()
    {
        var f = new Fixture();
        var steps = new ListProgress<BatchStepEvent>();

        var batch = f.Remove(new AppRemovalOptions { RestorePointSetting = false, Steps = steps }, StoreRow("Microsoft.BingNews"), ProgramRow("Bloat"));

        Assert.Single(f.RestorePoints.Descriptions);
        Assert.Equal(RestorePointStatus.Created, batch.RestorePoint!.Status);
        Assert.Equal(2, batch.SucceededCount);
        Assert.Equal(BatchStepKind.RestorePoint, steps.Items[0].Kind);
        Assert.Equal(3, steps.Items[0].Count);
        Assert.Equal(
            new[] { (0, BatchStepKind.RestorePoint), (0, BatchStepKind.RestorePoint), (1, BatchStepKind.Item), (1, BatchStepKind.Item), (2, BatchStepKind.Item), (2, BatchStepKind.Item) },
            steps.Items.Select(s => (s.Index, s.Kind)));
    }

    [Fact]
    public void Remove_RestorePointFailed_AskDeclined_AbortsBeforeFirstRemoval()
    {
        var f = new Fixture();
        f.RestorePoints.Next = new RestorePointResult(RestorePointStatus.ProtectionDisabled, "Ochrona systemu jest wyłączona.");
        var asked = new List<RestorePointResult>();

        var batch = f.Remove(
            new AppRemovalOptions { ContinueWithoutRestorePoint = result => { asked.Add(result); return false; } },
            ProgramRow("Bloat"),
            StoreRow("Microsoft.BingNews"));

        Assert.Same(f.RestorePoints.Next, Assert.Single(asked));
        Assert.True(batch.Aborted);
        Assert.Contains(AppsText.AbortedWithoutRestorePoint, batch.AbortReason, StringComparison.Ordinal);
        Assert.All(batch.Results, r => Assert.Equal(TweakOutcome.Skipped, r.Outcome));
        Assert.Equal(2, batch.SkippedCount);
        Assert.Equal(0, f.Remover.Attempts);
        Assert.Equal(0, f.Journal.SaveCount);
    }

    [Fact]
    public void Remove_RestorePointFailed_DefaultAnswerIsNo()
    {
        var f = new Fixture();
        f.RestorePoints.Next = new RestorePointResult(RestorePointStatus.Failed, "Błąd.");

        var batch = f.Remove(null, ProgramRow("Bloat"));

        Assert.True(batch.Aborted);
        Assert.Equal(0, f.Remover.Attempts);
    }

    [Fact]
    public void Remove_RestorePointServiceThrows_AskAnswerThrows_TreatedAsNo()
    {
        var f = new Fixture();
        f.RestorePoints.ThrowOnCreate = new InvalidOperationException("WMI padło.");

        var batch = f.Remove(
            new AppRemovalOptions { ContinueWithoutRestorePoint = _ => throw new InvalidOperationException("Okno padło.") },
            ProgramRow("Bloat"));

        Assert.Equal(RestorePointStatus.Failed, batch.RestorePoint!.Status);
        Assert.True(batch.Aborted);
        Assert.Equal(0, f.Remover.Attempts);
    }

    [Fact]
    public void Remove_RestorePointFailed_AskAccepted_Continues()
    {
        var f = new Fixture();
        f.RestorePoints.Next = new RestorePointResult(RestorePointStatus.ProtectionDisabled, "Ochrona systemu jest wyłączona.");

        var batch = f.Remove(new AppRemovalOptions { ContinueWithoutRestorePoint = _ => true }, ProgramRow("Bloat"));

        Assert.False(batch.Aborted);
        Assert.Equal(TweakOutcome.Removed, Assert.Single(batch.Results).Outcome);
        Assert.Contains(batch.Warnings, w => w.Contains("Ochrona systemu jest wyłączona.", StringComparison.Ordinal));
    }

    [Fact]
    public void Remove_StoreOnly_SettingOff_NoRestorePoint()
    {
        var f = new Fixture();
        var asked = 0;
        var steps = new ListProgress<BatchStepEvent>();

        var batch = f.Remove(
            new AppRemovalOptions { RestorePointSetting = false, Steps = steps, ContinueWithoutRestorePoint = _ => { asked++; return false; } },
            StoreRow("Microsoft.BingNews"));

        Assert.Empty(f.RestorePoints.Descriptions);
        Assert.Null(batch.RestorePoint);
        Assert.Equal(0, asked);
        Assert.All(steps.Items, s => Assert.Equal(BatchStepKind.Item, s.Kind));
        Assert.Equal(1, steps.Items[0].Count);
        Assert.Equal(TweakOutcome.Removed, Assert.Single(batch.Results).Outcome);
    }

    [Fact]
    public void Remove_StoreOnly_RestorePointRequired_Failed_AskDeclined_AbortsBeforeFirstRemoval()
    {
        // R20 (CLI --restore-point required): seria samych AppX też pyta i bez punktu nic nie usuwa.
        var f = new Fixture();
        f.RestorePoints.Next = new RestorePointResult(RestorePointStatus.ProtectionDisabled, "Ochrona wyłączona.");
        RestorePointResult? asked = null;

        var batch = f.Remove(
            new AppRemovalOptions { RestorePointRequired = true, ContinueWithoutRestorePoint = point => { asked = point; return false; } },
            StoreRow("Microsoft.BingNews"));

        Assert.Equal(RestorePointStatus.ProtectionDisabled, asked!.Status);
        Assert.True(batch.Aborted);
        Assert.Contains(AppsText.AbortedWithoutRestorePoint, batch.AbortReason, StringComparison.Ordinal);
        Assert.Equal(0, f.Remover.Attempts);
        Assert.Empty(f.Journal.Current.Entries);
        Assert.Equal(TweakOutcome.Skipped, Assert.Single(batch.Results).Outcome);
    }

    [Fact]
    public void Remove_StoreOnly_RestorePointRequired_CreatesPointEvenWhenSettingOff()
    {
        var f = new Fixture();

        var batch = f.Remove(new AppRemovalOptions { RestorePointRequired = true }, StoreRow("Microsoft.BingNews"));

        Assert.Single(f.RestorePoints.Descriptions);
        Assert.Equal(RestorePointStatus.Created, batch.RestorePoint!.Status);
        Assert.Equal(1, batch.SucceededCount);
    }

    [Fact]
    public void Remove_StoreOnly_SettingOn_FailedRestorePoint_WarnsAndContinuesWithoutAsking()
    {
        var f = new Fixture();
        f.RestorePoints.Next = new RestorePointResult(RestorePointStatus.Failed, "Błąd WMI.");
        var asked = 0;

        var batch = f.Remove(
            new AppRemovalOptions { RestorePointSetting = true, ContinueWithoutRestorePoint = _ => { asked++; return false; } },
            StoreRow("Microsoft.BingNews"));

        Assert.Single(f.RestorePoints.Descriptions);
        Assert.Equal(0, asked);
        Assert.False(batch.Aborted);
        Assert.Contains(batch.Warnings, w => w.Contains("Błąd WMI.", StringComparison.Ordinal));
        Assert.Equal(TweakOutcome.Removed, Assert.Single(batch.Results).Outcome);
    }

    [Fact]
    public void Remove_UninstallerExitsAtOnce_EntryVanishesWithinWindow_Removed()
    {
        // Review Focus 2: Inno Setup kopiuje się do %TEMP% i wychodzi od razu – wynik decyduje zniknięcie wpisu, nie kod wyjścia.
        var f = new Fixture();
        f.Remover.Lag = 5;

        var batch = f.Remove(null, ProgramRow("Inno"));

        var result = Assert.Single(batch.Results);
        Assert.Equal(TweakOutcome.Removed, result.Outcome);
        Assert.True(result.IsSuccess);
        Assert.Equal(Enumerable.Repeat(AppRemovalTimeouts.VerifyPoll, 5), f.Sleeper.Sleeps);
        Assert.Equal(7, f.Reader.Calls.Count); // przed próbą + 6 odczytów weryfikacji
        Assert.Empty(result.Warnings);
    }

    [Fact]
    public void Remove_TimedOut_StillPresent_NotConfirmed()
    {
        var f = new Fixture();
        f.Remover.Run = new UninstallerRun(null, true);
        f.Remover.Removes = false;

        var batch = f.Remove(null, ProgramRow("Wolny", UninstallMode.Interactive));

        var result = Assert.Single(batch.Results);
        Assert.Equal(TweakOutcome.NotConfirmed, result.Outcome);
        Assert.Equal(AppsText.NotConfirmed, result.Message);
        Assert.False(result.IsSuccess);
        Assert.Equal(1, batch.FailedCount);
        Assert.Equal(AppRemovalTimeouts.Interactive, Assert.Single(f.Remover.UninstallerCalls).Timeout);
        Assert.Equal(AppRemovalTimeouts.VerifyWindow, TimeSpan.FromTicks(f.Sleeper.Sleeps.Sum(s => s.Ticks)));
        var entry = SingleEntry(f);
        Assert.Equal(JournalEntryStatus.Aborted, entry.Status);
        Assert.Equal(nameof(TweakOutcome.NotConfirmed), entry.Events.Last().Outcome);
    }

    [Theory]
    [InlineData(3010)]
    [InlineData(1641)]
    public void Remove_Exit3010_Removed_RequiresReboot(int code)
    {
        var f = new Fixture();
        f.Remover.Run = new UninstallerRun(code, false);

        var batch = f.Remove(null, ProgramRow("Msi", UninstallMode.Msi));

        var result = Assert.Single(batch.Results);
        Assert.Equal(TweakOutcome.Removed, result.Outcome);
        Assert.True(result.RequiresReboot);
        Assert.True(batch.RequiresReboot);
        Assert.Empty(result.Warnings);
        Assert.Equal(AppRemovalTimeouts.Msi, Assert.Single(f.Remover.UninstallerCalls).Timeout);
        Assert.Equal(JournalEntryStatus.PendingReboot, SingleEntry(f).Status);
    }

    [Fact]
    public void Remove_OtherNonZeroExit_Vanished_RemovedWithExitCodeWarning()
    {
        var f = new Fixture();
        f.Remover.Run = new UninstallerRun(1, false);

        var result = Assert.Single(f.Remove(null, ProgramRow("Dziwny")).Results);

        Assert.Equal(TweakOutcome.Removed, result.Outcome);
        Assert.False(result.RequiresReboot);
        Assert.Equal(AppsText.UninstallerExitCode(1), Assert.Single(result.Warnings));
    }

    [Fact]
    public void Remove_StillPresent_Failed_WithExitCode()
    {
        var f = new Fixture();
        f.Remover.Run = new UninstallerRun(1603, false);
        f.Remover.Removes = false;

        var batch = f.Remove(null, ProgramRow("Uparty"));

        var result = Assert.Single(batch.Results);
        Assert.Equal(TweakOutcome.Failed, result.Outcome);
        Assert.Contains(AppsText.StillPresent, result.Message, StringComparison.Ordinal);
        Assert.Contains("1603", result.Message, StringComparison.Ordinal);
        Assert.Equal(1, batch.FailedCount);
        Assert.Equal(AppRemovalTimeouts.Quiet, Assert.Single(f.Remover.UninstallerCalls).Timeout);
        var entry = SingleEntry(f);
        Assert.Equal(JournalEntryStatus.Aborted, entry.Status);
        Assert.Equal(nameof(TweakOutcome.Failed), entry.Events.Last().Outcome);
    }

    [Fact]
    public void Remove_UninstallerCannotStart_Failed_WithReason()
    {
        var f = new Fixture();
        f.Remover.UninstallerThrows = new System.IO.FileNotFoundException("Nie ma pliku deinstalatora.");
        f.Remover.Removes = false;

        var result = Assert.Single(f.Remove(null, ProgramRow("Zniknal")).Results);

        Assert.Equal(TweakOutcome.Failed, result.Outcome);
        Assert.Contains("Nie ma pliku deinstalatora.", result.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Remove_AlreadyGone_RemovedWithoutRunning()
    {
        // Review Focus 5: aplikacja zniknęła między odczytem listy a „Usuń”.
        var f = new Fixture();
        var row = ProgramRow("Odinstalowany");

        var batch = f.Engine.Remove(new[] { row }, new AppRemovalOptions(), CancellationToken.None);

        var result = Assert.Single(batch.Results);
        Assert.Equal(TweakOutcome.Removed, result.Outcome);
        Assert.Equal(AppsText.AlreadyGone, result.Message);
        Assert.Equal(row.App.Id, result.TweakId);
        Assert.Equal(0, f.Remover.Attempts);
        Assert.Single(f.Reader.Calls);
        Assert.Empty(f.Journal.Current.Entries);
    }

    [Fact]
    public void Remove_MicrosoftStoreApp_Deprovisions_ProducentDoesNot()
    {
        var f = new Fixture();
        var microsoft = StoreRow("Microsoft.BingNews", "microsoft");
        var producent = StoreRow("Vendor.Helper", "producent");

        var batch = f.Remove(null, microsoft, producent);

        Assert.Equal(2, batch.SucceededCount);
        Assert.Equal(
            new[] { ("Microsoft.BingNews", UserSid, StoreRemovalScope.CurrentUser, true), ("Vendor.Helper", UserSid, StoreRemovalScope.CurrentUser, false) },
            f.Remover.StoreCalls.Select(c => (c.Appx.PackageName, c.Sid, c.Scope, c.Deprovision)));
        Assert.All(f.Reader.Calls.Where(c => c.Id == microsoft.App.Id), c => Assert.Equal(AppPresenceScope.UserAndImage, c.Scope));
        Assert.All(f.Reader.Calls.Where(c => c.Id == producent.App.Id), c => Assert.Equal(AppPresenceScope.User, c.Scope));
        Assert.All(f.Reader.Calls, c => Assert.Equal(UserSid, c.Sid));
        Assert.All(batch.Results, r => Assert.DoesNotContain(AppsText.RemovedForAllUsers, r.Warnings));
    }

    [Fact]
    public void Remove_ProcessSidDiffersFromLoggedOnUser_UsesAllUsers_ChecksImage_AndWarns()
    {
        // Ruling R8 (b): nigdy -User <inny SID>; -AllUsers usuwa wszystkim kontom i z obrazu, więc weryfikacja patrzy na wszystkie konta i obraz.
        var f = new Fixture(processSid: OtherSid);
        var producent = StoreRow("Vendor.Helper", "producent");

        var batch = f.Remove(null, producent);

        var call = Assert.Single(f.Remover.StoreCalls);
        Assert.Equal(StoreRemovalScope.AllUsers, call.Scope);
        Assert.True(call.Deprovision);
        Assert.All(f.Reader.Calls, c => Assert.Equal(AppPresenceScope.AllUsersAndImage, c.Scope));
        var result = Assert.Single(batch.Results);
        Assert.Equal(TweakOutcome.Removed, result.Outcome);
        Assert.Contains(AppsText.RemovedForAllUsers, result.Warnings);
        Assert.Contains(AppsText.RemovedForAllUsers, SingleEntry(f).Events.Last().Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Remove_ProcessSidSameButDifferentCase_IsCurrentUser()
    {
        var f = new Fixture(processSid: UserSid.ToLowerInvariant());

        f.Remove(null, StoreRow("Microsoft.BingNews"));

        Assert.Equal(StoreRemovalScope.CurrentUser, Assert.Single(f.Remover.StoreCalls).Scope);
    }

    [Fact]
    public void Remove_StoreScriptFails_StillPresent_FailedWithScriptError()
    {
        var f = new Fixture();
        f.Remover.Removes = false;
        f.Remover.StoreResult = _ => new ScriptResult { ExitCode = 1, Errors = new[] { "0x80073CFA: odmowa usunięcia." } };

        var result = Assert.Single(f.Remove(null, StoreRow("Microsoft.BingNews")).Results);

        Assert.Equal(TweakOutcome.Failed, result.Outcome);
        Assert.Contains(AppsText.StillPresent, result.Message, StringComparison.Ordinal);
        Assert.Contains("0x80073CFA", result.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Remove_StoreScriptFailsWithoutErrorText_ButPackageGone_RemovedWithWarning()
    {
        var f = new Fixture();
        f.Remover.StoreResult = _ => new ScriptResult { ExitCode = 3 };

        var result = Assert.Single(f.Remove(null, StoreRow("Microsoft.BingNews")).Results);

        Assert.Equal(TweakOutcome.Removed, result.Outcome);
        Assert.Contains(result.Warnings, w => w.StartsWith(AppsText.RemovalError(string.Empty), StringComparison.Ordinal) && w.Contains("3", StringComparison.Ordinal));
    }

    [Fact]
    public void Remove_StoreScriptTimesOut_StillPresent_NotConfirmed()
    {
        var f = new Fixture();
        f.Remover.Removes = false;
        f.Remover.StoreResult = _ => throw new OperationException(OperationErrorKind.Timeout, "Skrypt przekroczył limit czasu.");

        var result = Assert.Single(f.Remove(null, StoreRow("Microsoft.BingNews")).Results);

        Assert.Equal(TweakOutcome.NotConfirmed, result.Outcome);
        Assert.Equal(AppsText.StoreRemovalInterrupted("Skrypt przekroczył limit czasu."), result.Message);
        Assert.DoesNotContain("deinstalator", result.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("Skrypt przekroczył limit czasu.", result.Message, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(3010)]
    [InlineData(1641)]
    public void Remove_Exit3010_StillPresent_NotConfirmed_RequiresReboot(int code)
    {
        // R12: deinstalator zgłasza „dokończę po restarcie”, a wpis jeszcze jest – nie sukces, ale restart do banera (journal).
        var f = new Fixture();
        f.Remover.Run = new UninstallerRun(code, false);
        f.Remover.Removes = false;

        var batch = f.Remove(null, ProgramRow("Msi", UninstallMode.Msi));

        var result = Assert.Single(batch.Results);
        Assert.Equal(TweakOutcome.NotConfirmed, result.Outcome);
        Assert.True(result.RequiresReboot);
        Assert.Equal(AppsText.NotConfirmedUntilReboot, result.Message);
        Assert.Equal(1, batch.FailedCount);
        var entry = SingleEntry(f);
        Assert.Equal(JournalEntryStatus.PendingReboot, entry.Status);
        Assert.Equal(("remove-app", nameof(TweakOutcome.NotConfirmed)), (entry.Events.Last().Action, entry.Events.Last().Outcome));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1602)]
    public void Remove_InteractiveUninstallerExited_StillPresent_NotConfirmed(int code)
    {
        // Użytkownik mógł nie kliknąć drugiego okna deinstalatora – „Nie udało się” byłoby fałszywe.
        var f = new Fixture();
        f.Remover.Run = new UninstallerRun(code, false);
        f.Remover.Removes = false;

        var result = Assert.Single(f.Remove(null, ProgramRow("Okienkowy", UninstallMode.Interactive)).Results);

        Assert.Equal(TweakOutcome.NotConfirmed, result.Outcome);
        Assert.Equal(AppsText.InteractiveMayNotHaveFinished, result.Message);
        Assert.False(result.RequiresReboot);
        Assert.Equal(JournalEntryStatus.Aborted, SingleEntry(f).Status);
    }

    [Fact]
    public void Remove_QuietUninstallerExitedZero_StillPresentAfterWindow_NotConfirmed_MayFinishInBackground()
    {
        // R18: Inno Setup usuwa wpis Uninstall na końcu, często po więcej niż 60 s – „Nie udało się” byłoby fałszywe.
        var f = new Fixture();
        f.Remover.Run = new UninstallerRun(0, false);
        f.Remover.Removes = false;

        var batch = f.Remove(null, ProgramRow("Cichy", UninstallMode.Quiet));

        var result = Assert.Single(batch.Results);
        Assert.Equal(TweakOutcome.NotConfirmed, result.Outcome);
        Assert.Equal(AppsText.QuietMayFinishInBackground, result.Message);
        Assert.Equal("Deinstalator mógł dokończyć w tle – sprawdź listę po odświeżeniu.", AppsText.QuietMayFinishInBackground);
        Assert.False(result.RequiresReboot);
        Assert.Equal(1, batch.FailedCount);
        var entry = SingleEntry(f);
        Assert.Equal(JournalEntryStatus.Aborted, entry.Status);
        Assert.Equal(nameof(TweakOutcome.NotConfirmed), entry.Events.Last().Outcome);
    }

    [Fact]
    public void Remove_MsiExitedZero_StillPresent_StaysFailed()
    {
        // R18 dotyczy tylko cichego deinstalatora: msiexec /x kończy się po usunięciu, więc kod 0 + wpis = porażka.
        var f = new Fixture();
        f.Remover.Run = new UninstallerRun(0, false);
        f.Remover.Removes = false;

        var result = Assert.Single(f.Remove(null, ProgramRow("Msi", UninstallMode.Msi)).Results);

        Assert.Equal(TweakOutcome.Failed, result.Outcome);
        Assert.Contains(AppsText.StillPresent, result.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Remove_QuietUninstallerNonZeroExit_StillPresent_StaysFailed()
    {
        var f = new Fixture();
        f.Remover.Run = new UninstallerRun(2, false);
        f.Remover.Removes = false;

        var result = Assert.Single(f.Remove(null, ProgramRow("Cichy", UninstallMode.Quiet)).Results);

        Assert.Equal(TweakOutcome.Failed, result.Outcome);
        Assert.Contains(AppsText.UninstallerExitCode(2), result.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Remove_CancelledDuringVerification_UninstallerFinished_NotConfirmed()
    {
        var f = new Fixture();
        using var cts = new CancellationTokenSource();
        f.Remover.Removes = false;
        f.Remover.Run = new UninstallerRun(0, false);
        f.Remover.OnAttempt = cts.Cancel;
        var row = ProgramRow("Cichy");
        f.Reader.Present.Add(row.App.Id);

        var result = Assert.Single(f.Engine.Remove(new[] { row }, new AppRemovalOptions(), cts.Token).Results);

        Assert.Equal(TweakOutcome.NotConfirmed, result.Outcome);
        Assert.Equal(AppsText.VerificationInterrupted, result.Message);
        Assert.Empty(f.Sleeper.Sleeps);
    }

    [Fact]
    public void Remove_StopDuringVerification_NotConfirmed_AndSkipsRemaining()
    {
        var f = new Fixture();
        var stop = new BatchStop();
        f.Remover.Removes = false;
        f.Remover.OnAttempt = stop.Request;

        var batch = f.Remove(new AppRemovalOptions { Stop = stop }, ProgramRow("Cichy"), StoreRow("Microsoft.BingNews"));

        Assert.Equal(TweakOutcome.NotConfirmed, batch.Results[0].Outcome);
        Assert.Equal(AppsText.VerificationInterrupted, batch.Results[0].Message);
        Assert.Equal(TweakOutcome.Skipped, batch.Results[1].Outcome);
        Assert.True(batch.Stopped);
        Assert.Empty(f.Sleeper.Sleeps);
    }

    [Fact]
    public void Remove_StoreRemoverThrowsUnexpected_StillPresent_Failed()
    {
        var f = new Fixture();
        f.Remover.Removes = false;
        f.Remover.StoreResult = _ => throw new InvalidOperationException("Nie da się uruchomić PowerShella.");

        var result = Assert.Single(f.Remove(null, StoreRow("Microsoft.BingNews")).Results);

        Assert.Equal(TweakOutcome.Failed, result.Outcome);
        Assert.Contains("Nie da się uruchomić PowerShella.", result.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Remove_PresenceReadThrows_NeverClaimsRemoved()
    {
        var f = new Fixture();
        f.Reader.Present.Add(ProgramRow("X").App.Id);
        f.Reader.Throw = new InvalidOperationException("Rejestr padł.");

        var result = Assert.Single(f.Engine.Remove(new[] { ProgramRow("X", UninstallMode.Msi) }, new AppRemovalOptions(), CancellationToken.None).Results);

        // R19: błąd odczytu po próbie to „nie wiadomo”, nie „zniknęła” ani „nadal jest”.
        Assert.Equal(TweakOutcome.NotConfirmed, result.Outcome);
        Assert.Equal(AppsText.PresenceUnknown, result.Message);
        Assert.Equal(1, f.Remover.Attempts);
    }

    [Fact]
    public void Remove_PresenceUnknownAfterAttempt_NotConfirmed_NeverRemoved()
    {
        var f = new Fixture();
        f.Remover.Run = new UninstallerRun(0, false);
        f.Remover.OnAttempt = () => f.Reader.Answer = AppPresence.Unknown;

        var batch = f.Remove(null, ProgramRow("Wolny", UninstallMode.Msi), StoreRow("Microsoft.BingNews"));

        Assert.All(batch.Results, result =>
        {
            Assert.Equal(TweakOutcome.NotConfirmed, result.Outcome);
            Assert.Equal(AppsText.PresenceUnknown, result.Message);
        });
        Assert.Equal("Nie udało się sprawdzić, czy aplikacja zniknęła.", AppsText.PresenceUnknown);
        Assert.Equal(2, batch.FailedCount);
        Assert.All(f.Journal.Current.Entries, entry => Assert.Equal(JournalEntryStatus.Aborted, entry.Status));
    }

    [Fact]
    public void Remove_PresenceUnknownBeforeAttempt_StillAttempts_AndGoneLaterIsRemoved()
    {
        // Odczyt przed próbą nieudany: nie wiadomo, czy aplikacja jest – próba idzie; potem świeży odczyt widzi zniknięcie.
        var f = new Fixture();
        var row = ProgramRow("Cichy");
        f.Reader.Present.Add(row.App.Id);
        f.Reader.Answer = AppPresence.Unknown;
        f.Remover.OnAttempt = () => f.Reader.Answer = null;

        var result = Assert.Single(f.Engine.Remove(new[] { row }, new AppRemovalOptions(), CancellationToken.None).Results);

        Assert.Equal(1, f.Remover.Attempts);
        Assert.Equal(TweakOutcome.Removed, result.Outcome);
        Assert.Null(result.Message);
    }

    [Fact]
    public void Remove_UnknownReadsThenGone_WithinWindow_Removed()
    {
        var f = new Fixture();
        var reads = 0;
        f.Remover.OnAttempt = () => f.Reader.Answer = AppPresence.Unknown;
        f.Sleeper.OnSleep = () =>
        {
            if (++reads == 2)
            {
                f.Reader.Answer = null;
            }
        };

        var result = Assert.Single(f.Remove(null, ProgramRow("Cichy")).Results);

        Assert.Equal(TweakOutcome.Removed, result.Outcome);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("other")]
    public void Remove_StoreGoneFromAccountButLeftInImage_Failed_WithImageText(string? processSid)
    {
        // R19: konto bez pakietu, ale deprovisioning nie zadziałał – pakiet wróci na nowym koncie.
        var f = new Fixture(processSid: processSid is null ? null : OtherSid);
        f.Remover.OnAttempt = () => f.Reader.Answer = AppPresence.ImageOnly;

        var batch = f.Remove(null, StoreRow("Microsoft.BingNews"));

        var result = Assert.Single(batch.Results);
        Assert.Equal(TweakOutcome.Failed, result.Outcome);
        Assert.Equal(AppsText.LeftInImage, result.Message);
        Assert.Equal("Usunięta z konta, ale została w obrazie systemu (wróci na nowym koncie).", AppsText.LeftInImage);
        Assert.DoesNotContain(AppsText.StillPresent, result.Message, StringComparison.Ordinal);
        Assert.Equal(1, batch.FailedCount);
        Assert.Equal(JournalEntryStatus.Aborted, SingleEntry(f).Status);
    }

    [Fact]
    public void Remove_StoreLeftInImage_ScriptError_IsAppended()
    {
        var f = new Fixture();
        f.Remover.StoreResult = _ => new ScriptResult { ExitCode = 1, Errors = new[] { "0x80070005: odmowa dostępu do obrazu." } };
        f.Remover.OnAttempt = () => f.Reader.Answer = AppPresence.ImageOnly;

        var result = Assert.Single(f.Remove(null, StoreRow("Microsoft.BingNews")).Results);

        Assert.Equal(TweakOutcome.Failed, result.Outcome);
        Assert.StartsWith(AppsText.LeftInImage, result.Message, StringComparison.Ordinal);
        Assert.Contains("0x80070005", result.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Remove_CleanupUrl_AddsManufacturerToolHint()
    {
        var f = new Fixture();

        var result = Assert.Single(f.Remove(null, ProgramRow("McAfee", cleanupUrl: "https://example.invalid/mcpr")).Results);

        Assert.Equal(TweakOutcome.Removed, result.Outcome);
        Assert.Contains(AppsText.CleanupHint("https://example.invalid/mcpr"), result.Warnings);
    }

    [Fact]
    public void Remove_Stop_SkipsRemaining()
    {
        var f = new Fixture();
        var stop = new BatchStop();
        f.Remover.OnAttempt = stop.Request;

        var batch = f.Remove(new AppRemovalOptions { Stop = stop }, StoreRow("Microsoft.BingNews"), StoreRow("Microsoft.ZuneVideo"));

        Assert.True(batch.Stopped);
        Assert.Equal(TweakOutcome.Removed, batch.Results[0].Outcome);
        Assert.Equal(TweakOutcome.Skipped, batch.Results[1].Outcome);
        Assert.Equal(TweakEngine.StoppedMessage, batch.Results[1].Message);
        Assert.Single(f.Remover.StoreCalls);
    }

    [Fact]
    public void Remove_CancelledBeforeStart_SkipsAll()
    {
        var f = new Fixture();
        using var cts = new CancellationTokenSource();
        cts.Cancel();
        var row = StoreRow("Microsoft.BingNews");
        f.Reader.Present.Add(row.App.Id);

        var batch = f.Engine.Remove(new[] { row }, new AppRemovalOptions(), cts.Token);

        Assert.Equal(TweakOutcome.Skipped, Assert.Single(batch.Results).Outcome);
        Assert.Equal(0, f.Remover.Attempts);
    }

    [Fact]
    public void Remove_CancelledDuringVerification_StopsPolling()
    {
        var f = new Fixture();
        using var cts = new CancellationTokenSource();
        f.Remover.Removes = false;
        f.Remover.Run = new UninstallerRun(null, true);
        f.Remover.OnAttempt = cts.Cancel;
        var row = ProgramRow("Wolny");
        f.Reader.Present.Add(row.App.Id);

        var result = Assert.Single(f.Engine.Remove(new[] { row }, new AppRemovalOptions(), cts.Token).Results);

        Assert.Equal(TweakOutcome.NotConfirmed, result.Outcome);
        Assert.Empty(f.Sleeper.Sleeps);
    }

    [Fact]
    public void Remove_WritesJournalEntryKindAppRemoval()
    {
        var f = new Fixture();
        var row = StoreRow("Microsoft.BingNews");

        f.Remove(null, row);

        var entry = SingleEntry(f);
        Assert.Equal(JournalEntry.AppRemovalKind, entry.Kind);
        Assert.Equal("app-removal", entry.Kind);
        Assert.Equal(row.App.Id, entry.TweakId);
        Assert.Equal(row.App.Name, entry.Title);
        Assert.Equal(UserSid, entry.UserSid);
        Assert.Empty(entry.Baseline);
        Assert.Equal(JournalEntryStatus.Active, entry.Status);
        Assert.Equal(f.Clock.UtcNow, entry.AppliedAt);
        Assert.Equal(
            new[] { ("remove-app", "Started"), ("remove-app", nameof(TweakOutcome.Removed)) },
            entry.Events.Select(e => (e.Action, e.Outcome)));
    }

    [Fact]
    public void Remove_JournalEntryIsSavedBeforeTheAttempt()
    {
        var f = new Fixture();
        var savesBeforeAttempt = -1;
        f.Remover.OnAttempt = () => savesBeforeAttempt = f.Journal.SaveCount;

        f.Remove(null, StoreRow("Microsoft.BingNews"));

        Assert.Equal(1, savesBeforeAttempt);
        Assert.Equal(2, f.Journal.SaveCount);
    }

    [Fact]
    public void Remove_JournalCannotBeWritten_NothingRuns_Failed()
    {
        var f = new Fixture();
        f.Journal.FailSave = true;

        var result = Assert.Single(f.Remove(null, StoreRow("Microsoft.BingNews")).Results);

        Assert.Equal(TweakOutcome.Failed, result.Outcome);
        Assert.Equal(0, f.Remover.Attempts);
    }

    [Fact]
    public void Remove_JournalFinalSaveFails_ResultStaysVerified_WithWarning()
    {
        var f = new Fixture();
        f.Journal.SaveException = new JournalStoreException("Dysk pełny.");
        f.Journal.ThrowOnSaveFrom = 1;

        var result = Assert.Single(f.Remove(null, StoreRow("Microsoft.BingNews")).Results);

        Assert.Equal(TweakOutcome.Removed, result.Outcome);
        Assert.Contains(result.Warnings, w => w.Contains("Dysk pełny.", StringComparison.Ordinal));
    }

    [Fact]
    public void Remove_JournalLoadWarning_GoesToResult()
    {
        var f = new Fixture();
        f.Journal.LoadWarning = "Journal był uszkodzony – odłożono kopię.";

        var result = Assert.Single(f.Remove(null, StoreRow("Microsoft.BingNews")).Results);

        Assert.Contains("Journal był uszkodzony – odłożono kopię.", result.Warnings);
    }

    [Fact]
    public void Remove_StepListenerThrows_SeriesContinues()
    {
        var f = new Fixture();
        var steps = new ListProgress<BatchStepEvent> { OnReport = _ => throw new InvalidOperationException("UI padło.") };

        var batch = f.Remove(new AppRemovalOptions { Steps = steps }, StoreRow("Microsoft.BingNews"));

        Assert.Equal(TweakOutcome.Removed, Assert.Single(batch.Results).Outcome);
        Assert.Contains(f.Log.Lines, line => line.Contains("UI padło.", StringComparison.Ordinal));
    }

    [Fact]
    public void Remove_FinishedEventsCarryResults()
    {
        var f = new Fixture();
        var steps = new ListProgress<BatchStepEvent>();
        var row = StoreRow("Microsoft.BingNews");

        f.Remove(new AppRemovalOptions { Steps = steps }, row);

        Assert.Equal(new[] { BatchStepPhase.Started, BatchStepPhase.Finished }, steps.Items.Select(s => s.Phase));
        Assert.All(steps.Items, s => Assert.Equal(row.App.Id, s.TweakId));
        Assert.Equal(TweakOutcome.Removed, steps.Items[1].Result!.Outcome);
    }
}
