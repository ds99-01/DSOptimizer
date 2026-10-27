using System;
using DSOptimizer.App.ViewModels;
using DSOptimizer.Core.Gui;
using Xunit;

namespace DSOptimizer.Tests.Windows;

/// <summary>
/// Ruling R11: panel szczegółów historii pokazuje powód blokady „Cofnij” także dla wpisu instalacji, który nie jest aktywny
/// (bez przycisku „Cofnij”); wpis tweaka pokazuje powód tylko razem z przyciskiem.
/// </summary>
[Trait("Category", "Windows")]
public class HistoryRowViewModelTests
{
    private static HistoryEntry Entry(HistoryState state, bool active, string? blocked)
        => new(Guid.NewGuid(), "install.7zip", "7-Zip", true, "Instalacja", DateTimeOffset.UtcNow, "dso", state, Array.Empty<HistoryEvent>(), active, blocked);

    [Theory]
    [InlineData(HistoryState.Installed)]
    [InlineData(HistoryState.InstallNotConfirmed)]
    [InlineData(HistoryState.InstallNotDone)]
    public void InstallEntry_NotActive_ReasonShownWithoutUndoButton(HistoryState state)
    {
        var row = new HistoryRowViewModel(Entry(state, false, HistoryText.InstallUndoBlocked));

        Assert.True(row.HasUndoBlockedReason);
        Assert.True(row.ShowUndoArea);
        Assert.False(row.ShowUndoButton);
        Assert.Equal(HistoryText.InstallUndoBlocked, row.UndoBlockedReason);
    }

    [Fact]
    public void ActiveTweak_BlockedReason_ShownWithButton()
    {
        var row = new HistoryRowViewModel(Entry(HistoryState.Active, true, HistoryText.UndoNotNewest));

        Assert.True(row.HasUndoBlockedReason);
        Assert.True(row.ShowUndoArea);
        Assert.True(row.ShowUndoButton);
    }

    [Fact]
    public void RemovalEntry_NoReason_NoUndoArea()
    {
        var row = new HistoryRowViewModel(Entry(HistoryState.Removed, false, null));

        Assert.False(row.HasUndoBlockedReason);
        Assert.False(row.ShowUndoArea);
        Assert.False(row.ShowUndoButton);
    }

    [Fact]
    public void ActiveTweak_WithoutReason_ButtonOnly()
    {
        var row = new HistoryRowViewModel(Entry(HistoryState.Active, true, null));

        Assert.False(row.HasUndoBlockedReason);
        Assert.True(row.ShowUndoArea);
        Assert.True(row.ShowUndoButton);
    }

    [Fact]
    public void InstallNotDone_WithReason_ShowsReasonKindAndIcon()
    {
        var entry = Entry(HistoryState.InstallNotDone, false, HistoryText.InstallUndoBlocked)
            with { Kind = HistoryKind.AppInstall, IconKind = "Install", Reason = "brak połączenia z GitHubem" };

        var row = new HistoryRowViewModel(entry);

        Assert.Equal("Nie zainstalowano – brak połączenia z GitHubem", row.StateText);
        Assert.Equal(row.StateText, row.StateToolTip);
        Assert.Equal("Instalacja aplikacji", row.KindText);
        Assert.Equal("Install", row.IconKind);
        Assert.True(row.IsInstall);
    }

    /// <summary>Fala poprawek F1 + F5: wiersz – stan osobno (zawija się), powód w drugiej linii; nazwa UIA z rodzajem i powodem.</summary>
    [Fact]
    public void InstallNotDone_WithReason_RowSplitsStateAndReason_AutomationNameHasKindAndReason()
    {
        var entry = Entry(HistoryState.InstallNotDone, false, HistoryText.InstallUndoBlocked)
            with { Title = "RustDesk", Kind = HistoryKind.AppInstall, Reason = "brak połączenia z GitHubem" };

        var row = new HistoryRowViewModel(entry);

        Assert.Equal("Nie zainstalowano", row.StateName);
        Assert.Equal("brak połączenia z GitHubem", row.ReasonText);
        Assert.True(row.HasReason);
        Assert.Equal(
            $"RustDesk, Instalacja aplikacji, Nie zainstalowano – brak połączenia z GitHubem, {HistoryText.Date(entry.AppliedAt)}",
            row.AutomationName);
    }

    [Fact]
    public void Tweak_NoReason_RowHasStateOnly_AutomationNameHasCategory()
    {
        var entry = Entry(HistoryState.UndonePendingReboot, false, null) with { Title = "Wyłącz Widżety", CategoryName = "Prywatność i AI" };

        var row = new HistoryRowViewModel(entry);

        Assert.Equal("Cofnięta, czeka na restart", row.StateName);
        Assert.Null(row.ReasonText);
        Assert.False(row.HasReason);
        Assert.Equal($"Wyłącz Widżety, Prywatność i AI, Cofnięta, czeka na restart, {HistoryText.Date(entry.AppliedAt)}", row.AutomationName);
    }

    [Fact]
    public void Tweak_KindTextIsCategoryName_NotInstall()
    {
        var row = new HistoryRowViewModel(Entry(HistoryState.Active, true, null));

        Assert.Equal("Instalacja", row.KindText);
        Assert.False(row.IsInstall);
    }

    [Fact]
    public void ShowAppsCommand_InvokesAction()
    {
        var called = 0;
        var vm = new HistoryViewModel(
            () => System.Threading.Tasks.Task.FromResult(new HistorySnapshot(Array.Empty<HistoryEntry>(), null)),
            _ => System.Threading.Tasks.Task.CompletedTask,
            () => called++,
            (_, _) => { });

        vm.ShowAppsCommand.Execute(null);

        Assert.Equal(1, called);
    }

    [Fact]
    public void InstallEntry_WithUndoBlockedReason_HasNoUndoArea()
    {
        var entry = Entry(HistoryState.Installed, false, HistoryText.InstallUndoBlocked) with { Kind = HistoryKind.AppInstall };

        var row = new HistoryRowViewModel(entry);

        Assert.True(row.IsInstall);
        Assert.False(row.ShowUndoArea);
    }

    [Fact]
    public void OutOfCatalogChange_KindTextSaysIt_MarkerNotRepeated()
    {
        var entry = Entry(HistoryState.Active, true, null) with { InCatalog = false, CategoryName = null };

        var row = new HistoryRowViewModel(entry);

        Assert.True(row.IsOutOfCatalog);
        Assert.Equal(HistoryText.OutOfCatalog, row.KindText);
        Assert.False(row.ShowOutOfCatalogMarker);
    }

    [Fact]
    public void OutOfCatalogInstall_KindTextDiffers_MarkerShown()
    {
        var entry = Entry(HistoryState.Installed, false, null) with { InCatalog = false, Kind = HistoryKind.AppInstall };

        Assert.True(new HistoryRowViewModel(entry).ShowOutOfCatalogMarker);
    }
}
