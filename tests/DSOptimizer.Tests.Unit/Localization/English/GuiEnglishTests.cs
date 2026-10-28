using System;
using System.Linq;
using System.Runtime.InteropServices;
using DSOptimizer.Core.Catalog;
using DSOptimizer.Core.Engine;
using DSOptimizer.Core.Gui;
using DSOptimizer.Core.Localization;
using DSOptimizer.Core.Model;
using DSOptimizer.Core.Operations;
using DSOptimizer.Tests.Unit.Engine;
using DSOptimizer.Tests.Unit.Fakes;
using Xunit;

#pragma warning disable CA2201 // COMException jest tu celowo: tak zgłasza błąd schowka WPF (CLIPBRD_E_CANT_OPEN)

namespace DSOptimizer.Tests.Unit.Localization.English;

/// <summary>Teksty interfejsu (Core/Gui) po angielsku: całe zdania, liczebniki 1 i 2, rozpoznawanie własnych komunikatów.</summary>
public class GuiEnglishTests
{
    private static readonly DateTimeOffset T0 = new(2026, 10, 28, 10, 0, 0, TimeSpan.Zero);

    private static SeriesItem Item(string id, string title)
        => new(id, title, RiskLevel.Safe, "", Array.Empty<string>(), Array.Empty<string>(), Array.Empty<SeriesFact>());

    private static SeriesConfirmation Confirmation(GuiOperation operation, bool restorePoint, int items)
        => new(
            operation,
            "t",
            "s",
            Array.Empty<SeriesFact>(),
            Enumerable.Range(1, items).Select(i => Item("id" + i, "Item " + i)).ToList(),
            "p",
            restorePoint);

    private static TweakResult Result(string id, TweakOutcome outcome, string? message = null, bool reboot = false, string[]? notRestored = null)
        => new() { TweakId = id, Outcome = outcome, Message = message, RequiresReboot = reboot, NotRestored = notRestored ?? Array.Empty<string>() };

    private static GuiOperationReport Report(params TweakResult[] results)
        => new() { TweakId = "seria", Operation = GuiOperation.Apply, Batch = new BatchResult { Results = results } };

    private static GuiSession Session(EngineFixture f, params TweakDefinition[] definitions)
        => new(new LoadedCatalog(definitions, "sha256:test"), f.CreateServices());

    private static SeriesConfirmation Build(GuiOperation operation, bool isSeries, bool restorePoint, string? userName, params TweakDefinition[] tweaks)
        => SeriesConfirmationBuilder.Build(operation, tweaks, isSeries, restorePoint, userName, _ => Array.Empty<PlannedChange>(), new ListLog());

    [Fact]
    public void CategoryNames_English()
    {
        using (L.Use(UiLanguage.English))
        {
            Assert.Equal(
                new[]
                {
                    "Privacy and AI", "Performance", "Disk and file system", "Network and LAN", "Security and encryption",
                    "Explorer and convenience", "System panels", "Repairs", "Tools",
                },
                GuiText.CategoryOrder.Select(GuiText.CategoryName));
            Assert.Equal("custom", GuiText.CategoryName("custom"));
        }
    }

    [Fact]
    public void ApplyButton_English()
    {
        using (L.Use(UiLanguage.English))
        {
            var session = Session(new EngineFixture(), TestTweaks.TwoValues("a"), TestTweaks.TwoValues("b"), TestTweaks.TwoValues("c"));

            var window = session.ConfirmationForSeries(new[] { "a", "b", "c" }, GuiOperation.Apply, "jan")!;

            Assert.Equal("Apply (3)", window.PrimaryText);
            Assert.Equal("Apply selected", window.Title);
            Assert.Equal("3 items", window.Subtitle);
        }
    }

    [Fact]
    public void ItemsInCategories_English()
    {
        using (L.Use(UiLanguage.English))
        {
            Assert.Equal("1 item in 1 category", GuiText.SearchSummary(1, 1));
            Assert.Equal("5 items in 2 categories", GuiText.SearchSummary(5, 2));
            Assert.Equal("No results", GuiText.SearchSummary(0, 0));
        }
    }

    [Fact]
    public void History_StateWithReason_English()
    {
        using (L.Use(UiLanguage.English))
        {
            Assert.Equal("Not installed", HistoryText.StateWithReason(HistoryState.InstallNotDone, null));
            Assert.Equal("Not installed – no network", HistoryText.StateWithReason(HistoryState.InstallNotDone, "no network"));
            Assert.Equal("Undone, waiting for restart", HistoryText.StateWithReason(HistoryState.UndonePendingReboot, null));
        }
    }

    [Fact]
    public void ShortReason_English()
    {
        using (L.Use(UiLanguage.English))
        {
            Assert.Equal("no connection to GitHub", HistoryText.ShortReason("Not done – no connection to GitHub"));
            Assert.Equal("the uninstaller is still running", HistoryText.ShortReason("Not confirmed – the uninstaller is still running"));
            Assert.Equal("Other text", HistoryText.ShortReason("Other text"));
            Assert.Null(HistoryText.ShortReason(" "));
        }
    }

    [Fact]
    public void ShortReason_PolishStoredReason_InEnglishRun()
    {
        using (L.Use(UiLanguage.English))
        {
            Assert.Equal("brak połączenia z GitHubem", HistoryText.ShortReason("Nie wykonano – brak połączenia z GitHubem"));
            Assert.Equal("deinstalator nadal działa", HistoryText.ShortReason("Nie potwierdzono – deinstalator nadal działa"));
            Assert.Equal(
                "Not done – brak sieci",
                HistoryText.Event("install-app", "Failed", "github: rustdesk/rustdesk", "Nie wykonano – brak sieci"));
        }
    }

    [Fact]
    public void ShortReason_PolishRun_Unchanged()
        => Assert.Equal("brak sieci", HistoryText.ShortReason("Nie wykonano – brak sieci"));

    [Fact]
    public void History_Event_English()
    {
        using (L.Use(UiLanguage.English))
        {
            Assert.Equal("Apply started", HistoryText.Event("apply", "Started", null));
            Assert.Equal("Applied – takes effect after restart", HistoryText.Event("apply", "PendingReboot", null));
            Assert.Equal("Apply: Error – rollback incomplete", HistoryText.Event("apply", "FailedRollbackIncomplete", null));
            Assert.Equal("Undo: Undo incomplete – reason", HistoryText.Event("undo", "UndoIncomplete", "reason"));
            Assert.Equal("Removal not confirmed – reason", HistoryText.Event("remove-app", "NotConfirmed", "reason"));
            Assert.Equal("Installed and verified", HistoryText.Event("install-app", "Installed", null));
            Assert.Equal("Not done – no network", HistoryText.Event("install-app", "Failed", "github: rustdesk/rustdesk", "Not done – no network"));
        }
    }

    [Fact]
    public void History_RebootDetectMessage_HiddenInBothLanguages()
    {
        using (L.Use(UiLanguage.English))
        {
            Assert.Equal("After restart: change active", HistoryText.Event("detect", "Applied", "Zmiana aktywna po restarcie."));
            Assert.Equal("After restart: change active", HistoryText.Event("detect", "Applied", "The change is active after the restart."));
        }
    }

    [Fact]
    public void History_Texts_English()
    {
        using (L.Use(UiLanguage.English))
        {
            Assert.Equal("Change history", HistoryText.PageTitle);
            Assert.Equal("Status", HistoryText.StateLabel);
            Assert.Equal("Go to: Apps", HistoryText.GoToAppsLink);
            Assert.Equal("All entries", HistoryText.FilterName(HistoryFilter.All));
            Assert.Equal("App install", HistoryText.KindLine(HistoryKind.AppInstall, null));
            Assert.Equal("item not in this catalog", HistoryText.KindLine(HistoryKind.Change, null));
            Assert.Equal("This change applies to account jan. To undo it, run the program on that account.", HistoryText.UndoOtherAccount("jan"));
            Assert.Equal("Couldn't read the change history: broken", HistoryText.LoadFailed("broken"));
        }
    }

    [Fact]
    public void BatchRun_Summary_English()
    {
        using (L.Use(UiLanguage.English))
        {
            var run = new BatchRun(Confirmation(GuiOperation.Apply, false, 2));
            Assert.Equal("Applying changes", run.Title);
            Assert.Equal("step 1 of 2", run.Subtitle);
            Assert.Equal("Waiting", run.Steps[0].StatusText);

            run.Complete(Report(Result("id1", TweakOutcome.Applied), Result("id2", TweakOutcome.Failed, "boom")));

            Assert.Equal("Finished", run.Title);
            Assert.Equal("1 of 2 applied", run.Subtitle);
            Assert.Equal("Applied and verified", run.Steps[0].StatusText);
            Assert.Equal("Error", run.Steps[1].StatusText);

            var undo = new BatchRun(Confirmation(GuiOperation.Undo, false, 1));
            undo.Complete(Report(Result("id1", TweakOutcome.Undone)));
            Assert.Equal("1 of 1 undone", undo.Subtitle);

            var remove = new BatchRun(Confirmation(GuiOperation.RemoveApps, false, 2));
            remove.Complete(Report(Result("id1", TweakOutcome.Removed)));
            Assert.Equal("1 of 2 removed", remove.Subtitle);
        }
    }

    [Fact]
    public void BatchRun_ResultText_English()
    {
        using (L.Use(UiLanguage.English))
        {
            var run = new BatchRun(Confirmation(GuiOperation.Apply, true, 1));
            run.Complete(Report(Result("id1", TweakOutcome.FailedRollbackIncomplete, "boom", reboot: false, notRestored: new[] { "HKLM\\X" })));

            Assert.Equal(
                "Finished: 0 of 1 applied\r\nRestore point – Skipped\r\nItem 1 – Error\r\n  boom\r\n  Couldn't roll back everything:\r\n  HKLM\\X",
                run.ResultText());
        }
    }

    [Fact]
    public void BatchRun_RebootBanner_English()
    {
        using (L.Use(UiLanguage.English))
        {
            var one = new BatchRun(Confirmation(GuiOperation.Apply, false, 1));
            one.Complete(Report(Result("id1", TweakOutcome.PendingReboot, reboot: true)));
            Assert.Equal("1 change takes effect only after the computer restarts.", one.RebootText);
            Assert.EndsWith("Restart required. 1 change takes effect only after the computer restarts.", one.ResultText(), StringComparison.Ordinal);

            Assert.Equal("2 changes take effect only after the computer restarts.", GuiText.RebootBannerText(2));
        }
    }

    [Fact]
    public void BatchRun_InstallProgress_English()
    {
        using (L.Use(UiLanguage.English))
        {
            var run = new BatchRun(Confirmation(GuiOperation.InstallApps, false, 2));
            Assert.Equal("Installing apps", run.Title);
            Assert.Equal("App 1 of 2 · Next: Item 2", run.Subtitle);

            run.OnStep(new BatchStepEvent(1, 2, BatchStepKind.Item, "id2", BatchStepPhase.Started, T0));
            Assert.Equal("App 2 of 2", run.Subtitle);
            Assert.Equal("In progress", run.Steps[1].StatusText);
        }
    }

    [Fact]
    public void SeriesConfirmation_Title_English()
    {
        using (L.Use(UiLanguage.English))
        {
            var undo = Build(GuiOperation.Undo, true, true, null, TestTweaks.TwoValues("a"));
            Assert.Equal("Undo selected", undo.Title);
            Assert.Equal("1 item", undo.Subtitle);
            Assert.Equal("Undo (1)", undo.PrimaryText);
            Assert.Equal(new SeriesFact("Restore point", "No – undo doesn't create one"), undo.Facts[0]);

            var single = Build(GuiOperation.Apply, false, false, null, TestTweaks.TwoValues("a"));
            Assert.Equal("Apply", single.Title);
            Assert.Equal("Apply", single.PrimaryText);
        }
    }

    [Fact]
    public void SeriesConfirmation_Facts_English()
    {
        using (L.Use(UiLanguage.English))
        {
            var reboot = TestTweaks.TwoValues("a") with { RequiresReboot = true, Reversible = false, Title = "First" };
            var one = Build(GuiOperation.Apply, true, true, null, reboot, TestTweaks.TwoValues("b"));
            Assert.Equal(
                new[]
                {
                    new SeriesFact("Restore point", "The program will try to create one before the first change"),
                    new SeriesFact("Restart", "Required for 1 item"),
                    new SeriesFact("Undo", "Not possible for: First"),
                },
                one.Facts);
            Assert.Equal(new[] { "Takes effect after a restart." }, one.Items[0].Notes);

            var two = Build(GuiOperation.Apply, true, false, null, reboot, reboot with { Id = "c" });
            Assert.Equal(new SeriesFact("Restart", "Required for 2 items"), two.Facts[1]);
            Assert.Equal(new SeriesFact("Restore point", "No – turned off in Settings"), two.Facts[0]);

            var none = Build(GuiOperation.Apply, true, true, null, TestTweaks.TwoValues("b"));
            Assert.Equal(new SeriesFact("Restart", "Not needed"), none.Facts[1]);
            Assert.Equal(new SeriesFact("Undo", "Possible for all, from the change history"), none.Facts[2]);

            var action = Build(GuiOperation.Execute, false, true, null, TestTweaks.Action());
            Assert.Equal(new[] { new SeriesFact("Undo", "Not possible – actions can't be undone") }, action.Facts);

            var user = Build(GuiOperation.Apply, false, true, "jan", TestTweaks.UserMenuDelay());
            Assert.Equal(new[] { "Applies to account jan." }, user.Items[0].Notes);
            var current = Build(GuiOperation.Apply, false, true, null, TestTweaks.UserMenuDelay());
            Assert.Equal(new[] { "Applies to the current account." }, current.Items[0].Notes);
        }
    }

    [Fact]
    public void SeriesConfirmation_Technical_English()
    {
        using (L.Use(UiLanguage.English))
        {
            var change = new PlannedChange { Description = @"HKLM\X\A", Current = "0", Target = "1" };
            var apply = SeriesConfirmationBuilder.Build(
                GuiOperation.Apply, new[] { TestTweaks.TwoValues("a") }, false, true, null, _ => new[] { change }, new ListLog());
            Assert.Equal(new[] { new SeriesFact("Where", @"HKLM\X\A"), new SeriesFact("Change", "0 to 1") }, apply.Items[0].Technical);

            var log = new ListLog();
            var failed = SeriesConfirmationBuilder.Build(
                GuiOperation.Apply, new[] { TestTweaks.TwoValues("a") }, false, true, null, _ => throw new InvalidOperationException("boom"), log);
            Assert.Equal(new[] { new SeriesFact("Details", "unavailable: boom") }, failed.Items[0].Technical);
            Assert.Equal("WARN [Gui] Change preview for a unavailable: boom", Assert.Single(log.Lines));
        }
    }

    [Fact]
    public void LogLine_English()
    {
        using (L.Use(UiLanguage.English))
        {
            var at = new DateTimeOffset(2026, 10, 28, 9, 5, 7, TimeSpan.Zero);
            var time = LogLineText.Time(at);
            Assert.Equal(time + " [Error] boom", LogLineText.ForClipboard(new GuiLogLine(at, GuiLogLevel.Error, "boom")));
            Assert.Equal(time + " [Warning] careful", LogLineText.ForClipboard(new GuiLogLine(at, GuiLogLevel.Warning, "careful")));
            Assert.Equal(time + " [Info] ok", LogLineText.ForClipboard(new GuiLogLine(at, GuiLogLevel.Info, "ok")));
            Assert.Equal("Access denied. Path copied.", LogLineText.RevealFailed("Access denied", true));
            Assert.Equal("1 error, 2 warnings", GuiText.LogSummary(1, 2));
            Assert.Equal("2 errors, 1 warning", GuiText.LogSummary(2, 1));
            Assert.Equal("0 errors, 22 warnings", GuiText.LogSummary(0, 22));
            Assert.Equal("1000 errors, 1001 warnings", GuiText.LogSummary(1000, 1001));
        }
    }

    [Fact]
    public void States_And_Outcomes_English()
    {
        using (L.Use(UiLanguage.English))
        {
            Assert.Equal("Partially applied", GuiText.StateName(DetectionState.Partial));
            Assert.Equal("—", GuiText.StateName(null));
            Assert.Equal("Applied – restart required", GuiText.OutcomeName(TweakOutcome.PendingReboot));
            Assert.Equal("Installed (reported by winget)", GuiText.OutcomeName(TweakOutcome.InstalledUnverified));
            Assert.Equal("Risk: caution", GuiText.RiskToolTip(RiskLevel.Caution));
            Assert.Equal("No experimental", GuiText.RiskFilterName(RiskFilter.NoExperimental));
            Assert.Equal("Install", GuiText.OperationName(GuiOperation.InstallApps));
        }
    }

    [Fact]
    public void EditionsLine_English()
    {
        using (L.Use(UiLanguage.English))
        {
            Assert.Equal("Works in editions: Pro.", GuiText.EditionsLine(new[] { EditionFamily.Pro }));
            Assert.Equal("Works in editions: Home and Pro.", GuiText.EditionsLine(new[] { EditionFamily.Pro, EditionFamily.Home }));
            Assert.Equal(
                "Works in editions: Home, Pro, Enterprise and Education.",
                GuiText.EditionsLine(new[] { EditionFamily.Home, EditionFamily.Pro, EditionFamily.Business }));
        }
    }

    [Fact]
    public void ItemRow_English()
    {
        using (L.Use(UiLanguage.English))
        {
            Assert.Equal("Applied. Waiting for a restart.", GuiText.ItemRowSubtitle("Applied", RebootPendingKind.Applied));
            Assert.Equal("Undone. Waiting for a restart.", GuiText.ItemRowSubtitle("Applied", RebootPendingKind.Undone));
            Assert.Equal("Short. Suggested.", GuiText.ItemRowHelp("Short.", true));
            Assert.Equal("Suggested.", GuiText.ItemRowHelp("", true));
            Assert.Equal("Undo: item \"x.y\" isn't in the catalog – nothing was changed.", GuiText.ItemNotFound(GuiOperation.Undo, "x.y"));
        }
    }

    [Fact]
    public void Counters_English()
    {
        using (L.Use(UiLanguage.English))
        {
            Assert.Equal("1 of 1 setting applied", GuiText.CategoryCounterDescription(1, 1, 0, 0));
            Assert.Equal("0 of 13 settings applied", GuiText.CategoryCounterDescription(0, 13, 0, 0));
            Assert.Equal("1 panel", GuiText.CategoryCounterDescription(0, 0, 0, 1));
            Assert.Equal("2 panels", GuiText.CategoryCounterDescription(0, 0, 0, 2));
            Assert.Equal("1 action", GuiText.CategoryCounterDescription(0, 0, 1, 0));
            Assert.Equal("2 actions", GuiText.CategoryCounterDescription(0, 0, 2, 0));
            Assert.Equal("2 items", GuiText.CategoryCounterDescription(0, 0, 1, 1));
            Assert.Equal("1 app", GuiText.AppsCounterDescription(1));
            Assert.Equal("2 apps", GuiText.AppsCounterDescription(2));
            Assert.Equal("1 app on the list", GuiText.InstallCounterDescription(1));
            Assert.Equal("2 apps on the list", GuiText.InstallCounterDescription(2));
            Assert.Equal("1 active change", GuiText.HistoryCounterDescription(1));
            Assert.Equal("2 active changes", GuiText.HistoryCounterDescription(2));
        }
    }

    [Fact]
    public void SelectionBar_English()
    {
        using (L.Use(UiLanguage.English))
        {
            Assert.Equal("Double-click a row or checkbox to add it to the change.", GuiText.SelectionBarTitle(0, 0, 0));
            Assert.Equal("1 item selected", GuiText.SelectionBarTitle(1, 1, 0));
            Assert.Equal("2 items selected: 2 to undo", GuiText.SelectionBarTitle(2, 0, 2));
            Assert.Equal("3 items selected: 2 to apply, 1 to undo", GuiText.SelectionBarTitle(3, 2, 1));
            Assert.Equal(
                "1 requires a restart. The program will try to create a restore point before applying.",
                GuiText.SelectionBarDetails(1, true, true));
            Assert.Equal("2 require a restart. No restore point.", GuiText.SelectionBarDetails(2, true, false));
        }
    }

    [Fact]
    public void FilterAndSearch_English()
    {
        using (L.Use(UiLanguage.English))
        {
            Assert.Equal("Hidden by the filter: 3", GuiText.FilterNote(3, 0));
            Assert.Equal("Applied items shown despite the filter: 2", GuiText.FilterNote(0, 2));
            Assert.Equal("Hidden by the filter: 3. Applied items shown despite the filter: 2", GuiText.FilterNote(3, 2));
            Assert.Equal("Nothing matches \"dns\".", GuiText.EmptySearchText(" dns ", 0));
            Assert.Equal("The risk filter hid the matching items.", GuiText.EmptySearchText("dns", 2));
            Assert.Equal("Search results", GuiText.SearchTitle);
        }
    }

    [Fact]
    public void CancelledBatch_English()
    {
        using (L.Use(UiLanguage.English))
        {
            Assert.Equal("Batch canceled: Apply – 1 item (A).", GuiText.CancelledBatch(GuiOperation.Apply, new[] { "A" }));
            Assert.Equal(
                "Batch canceled: Undo – 4 items (A; B; C and 1 other).",
                GuiText.CancelledBatch(GuiOperation.Undo, new[] { "A", "B", "C", "D" }));
            Assert.Equal(
                "Batch canceled: Undo – 5 items (A; B; C and 2 others).",
                GuiText.CancelledBatch(GuiOperation.Undo, new[] { "A", "B", "C", "D", "E" }));
        }
    }

    [Fact]
    public void Links_English()
    {
        using (L.Use(UiLanguage.English))
        {
            Assert.Equal("Address copied: https://x", GuiText.UrlCopied("https://x"));
            Assert.Equal(
                "Couldn't open the page: Blocked. Address copied: " + Core.ProductInfo.RepositoryUrl,
                GuiText.OpenUrlFailed("Blocked.", true));
            Assert.Equal("Couldn't open the page: Blocked. Couldn't copy the address either.", GuiText.OpenUrlFailed("Blocked.", false));
        }
    }

    [Fact]
    public void ActionWindow_English()
    {
        using (L.Use(UiLanguage.English))
        {
            Assert.Equal("Step 1 of 2", GuiText.ActionStepText(0, 2));
            Assert.Equal("Next: SFC", GuiText.ActionNextStageText("SFC"));
            Assert.Equal("Stop after this step", GuiText.ActionStopText);

            var run = new ActionRun(new SeriesConfirmation(
                GuiOperation.Execute, "Run", "Repair", Array.Empty<SeriesFact>(), new[] { Item("r", "Repair") }, "Run", false));
            Assert.Equal("in progress", run.Subtitle);
            Assert.Equal("Preparing…", run.Stage);

            run.Complete(new GuiOperationReport
            {
                TweakId = "r",
                Operation = GuiOperation.Execute,
                Result = new TweakResult { TweakId = "r", Outcome = TweakOutcome.Executed, RequiresReboot = true, NotRestored = new[] { "X" } },
            });

            Assert.Equal(
                "Finished: Done\r\nRepair\r\n  Not restored: X\r\nRestart required. The repair takes full effect only after the computer restarts.",
                run.ResultText());

            var failed = new ActionRun(new SeriesConfirmation(
                GuiOperation.Execute, "Run", "Repair", Array.Empty<SeriesFact>(), new[] { Item("r", "Repair") }, "Run", false));
            failed.Complete(new GuiOperationReport { TweakId = "r", Operation = GuiOperation.Execute });
            Assert.Equal("Finished with an error", failed.Title);
            Assert.Equal(new[] { "No action result." }, failed.ResultLines);
        }
    }

    [Fact]
    public void Session_Lines_English()
    {
        using (L.Use(UiLanguage.English))
        {
            var f = new EngineFixture();
            var session = Session(f, TestTweaks.TwoValues("a"), TestTweaks.TwoValues("b"));

            var report = session.RunBatch(new[] { "a", "b" }, GuiOperation.Apply, confirmed: true);

            Assert.Contains(report.Lines, line => line.Text == "Batch: 2 of 2 succeeded.");
            Assert.Equal("DSOptimizer – before changes (2)", Assert.Single(f.RestorePoints.Descriptions));

            var refused = Session(new EngineFixture(), TestTweaks.TwoValues("a") with { Risk = RiskLevel.Caution })
                .Run("a", GuiOperation.Apply, confirmed: false);
            Assert.Equal("The operation requires confirmation – nothing was changed.", Assert.Single(refused.Lines).Text);
        }
    }

    [Fact]
    public void Specification_English()
    {
        using (L.Use(UiLanguage.English))
        {
            var log = new ListLog();
            SpecificationText.LogSaveResult(log, @"E:\x.txt", true, null);
            SpecificationText.LogSaveResult(log, @"E:\y.txt", false, "not ready");
            SpecificationText.LogCopied(log);

            Assert.Equal(
                new[]
                {
                    @"INFO [Specification] Report saved: E:\x.txt",
                    @"WARN [Specification] Couldn't save the report to E:\y.txt: not ready",
                    "INFO [Specification] Specification copied to the clipboard.",
                },
                log.Lines);
            Assert.Equal("Couldn't copy the specification to the clipboard: busy", SpecificationText.CopyFailedMessage("busy"));
        }
    }

    [Fact]
    public void ClipboardRecovered_English()
    {
        using (L.Use(UiLanguage.English))
        {
            COMException error = new("locked", unchecked((int)0x800401D0));
            Assert.Equal(
                "The clipboard reported error 0x800401D0 (locked), but the text is on the clipboard – treated as copied.",
                ClipboardCopy.RecoveredMessage(error));
        }
    }

    [Fact]
    public void FixedTexts_English()
    {
        using (L.Use(UiLanguage.English))
        {
            Assert.Equal("Restart required.", GuiText.RebootTitle);
            Assert.Equal("The computer will restart. Save your work in open programs.", GuiText.RestartConfirmText);
            Assert.Equal("Search all categories", GuiText.SearchPlaceholder);
            Assert.StartsWith("Suggested. ", GuiText.SuggestedTooltip, StringComparison.Ordinal);
            Assert.Equal("Further window errors go only to the log file (more than 5 within 10 s).", GuiText.WindowErrorsMuted);
        }
    }
}
