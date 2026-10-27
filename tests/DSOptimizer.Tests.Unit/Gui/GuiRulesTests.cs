using System;
using DSOptimizer.Core.Gui;
using DSOptimizer.Core.Model;
using DSOptimizer.Tests.Unit.Engine;
using Xunit;

namespace DSOptimizer.Tests.Unit.Gui;

public class GuiRulesTests
{
    [Theory]
    [InlineData(null, false, false)]
    [InlineData(DetectionState.Applied, false, true)]
    [InlineData(DetectionState.NotApplied, true, false)]
    [InlineData(DetectionState.Partial, true, true)]
    [InlineData(DetectionState.Unknown, true, true)]
    [InlineData(DetectionState.NotSupported, false, false)]
    public void CanApplyAndUndo_ForTweak_FollowStateTable(DetectionState? state, bool canApply, bool canUndo)
    {
        var tweak = TestTweaks.TwoValues();
        Assert.Equal(canApply, GuiRules.CanApply(tweak, state));
        Assert.Equal(canUndo, GuiRules.CanUndo(tweak, state, null));
        Assert.False(GuiRules.CanExecute(tweak));
    }

    [Theory]
    [InlineData(DetectionState.Applied)]
    [InlineData(DetectionState.Partial)]
    [InlineData(DetectionState.Unknown)]
    public void CanUndo_PendingUndone_False(DetectionState state)
        => Assert.False(GuiRules.CanUndo(TestTweaks.TwoValues(), state, RebootPendingKind.Undone));

    [Theory]
    [InlineData(DetectionState.Applied, true)]
    [InlineData(DetectionState.NotApplied, false)]
    [InlineData(DetectionState.Partial, true)]
    public void CanUndo_PendingApplied_AsBefore(DetectionState state, bool expected)
        => Assert.Equal(expected, GuiRules.CanUndo(TestTweaks.TwoValues(), state, RebootPendingKind.Applied));

    [Theory]
    [InlineData(DetectionState.NotApplied)]
    [InlineData(DetectionState.Applied)]
    [InlineData(DetectionState.Partial)]
    [InlineData(DetectionState.Unknown)]
    [InlineData(null)]
    public void ActionAndPanel_CanOnlyExecute(DetectionState? state)
    {
        foreach (var item in new[] { TestTweaks.Action(), TestTweaks.Panel() })
        {
            Assert.False(GuiRules.CanApply(item, state));
            Assert.False(GuiRules.CanUndo(item, state, null));
            Assert.True(GuiRules.CanExecute(item));
        }
    }

    [Fact]
    public void RequiresConfirmation_Suggested_AlwaysTrueEvenWhenSafe()
    {
        Assert.True(GuiRules.RequiresConfirmation(TestTweaks.TwoValues() with { Suggested = true }));
        Assert.False(GuiRules.RequiresConfirmation(TestTweaks.TwoValues() with { Suggested = false }));
    }

    [Fact]
    public void RequiresConfirmation_Table()
    {
        Assert.True(GuiRules.RequiresConfirmation(TestTweaks.TwoValues() with { Risk = RiskLevel.Caution }));
        Assert.True(GuiRules.RequiresConfirmation(TestTweaks.TwoValues() with { Risk = RiskLevel.Experimental }));
        Assert.True(GuiRules.RequiresConfirmation(TestTweaks.Action()));
        Assert.False(GuiRules.RequiresConfirmation(TestTweaks.TwoValues()));
        Assert.False(GuiRules.RequiresConfirmation(TestTweaks.Panel()));
    }

    [Fact]
    public void NullTweak_Throws()
    {
        Assert.Throws<ArgumentNullException>(() => GuiRules.CanApply(null!, null));
        Assert.Throws<ArgumentNullException>(() => GuiRules.CanUndo(null!, null, null));
        Assert.Throws<ArgumentNullException>(() => GuiRules.CanExecute(null!));
        Assert.Throws<ArgumentNullException>(() => GuiRules.RequiresConfirmation(null!));
    }

    [Theory]
    [InlineData(true, RowClickTarget.Row, 1, RowClickAction.None)]
    [InlineData(true, RowClickTarget.Row, 2, RowClickAction.ToggleSeries)]
    [InlineData(true, RowClickTarget.CheckBox, 1, RowClickAction.None)]
    [InlineData(true, RowClickTarget.CheckBox, 2, RowClickAction.None)]
    [InlineData(false, RowClickTarget.Row, 1, RowClickAction.None)]
    [InlineData(false, RowClickTarget.Row, 2, RowClickAction.Run)]
    [InlineData(false, RowClickTarget.RunIcon, 1, RowClickAction.Run)]
    [InlineData(false, RowClickTarget.RunIcon, 2, RowClickAction.None)]
    [InlineData(true, RowClickTarget.RunIcon, 1, RowClickAction.None)]
    [InlineData(true, RowClickTarget.RunIcon, 2, RowClickAction.None)]
    public void RowClick_Table(bool isTweak, RowClickTarget target, int clickCount, RowClickAction expected)
        => Assert.Equal(expected, GuiRules.RowClick(isTweak, target, clickCount));

    [Fact]
    public void RowClick_RowDoubleAfterRunIcon_None()
    {
        Assert.Equal(RowClickAction.None, GuiRules.RowClick(false, RowClickTarget.Row, 2, RowClickTarget.RunIcon));
        Assert.Equal(RowClickAction.Run, GuiRules.RowClick(false, RowClickTarget.Row, 2, RowClickTarget.Row));
        Assert.Equal(RowClickAction.Run, GuiRules.RowClick(false, RowClickTarget.Row, 2, null));
        Assert.Equal(RowClickAction.ToggleSeries, GuiRules.RowClick(true, RowClickTarget.Row, 2, null));
    }

    [Fact]
    public void RowClick_RunIconDoubleClick_SecondClickNone()
        => Assert.Equal(RowClickAction.None, GuiRules.RowClick(false, RowClickTarget.RunIcon, 2));

    [Fact]
    public void RowClick_ActionRowSingleClick_None()
        => Assert.Equal(RowClickAction.None, GuiRules.RowClick(false, RowClickTarget.Row, 1));

    [Fact]
    public void RowClick_TripleClickRow_None()
    {
        Assert.Equal(RowClickAction.None, GuiRules.RowClick(true, RowClickTarget.Row, 3));
        Assert.Equal(RowClickAction.None, GuiRules.RowClick(false, RowClickTarget.Row, 3));
        Assert.Equal(RowClickAction.None, GuiRules.RowClick(false, RowClickTarget.RunIcon, 3));
    }
    [Theory]
    [InlineData("system-files", true)]
    [InlineData("disk-cleanup", true)]
    [InlineData("windows-old", true)]
    [InlineData("wu-reset", false)]
    [InlineData("restore-point", false)]
    public void UsesProgressWindow_OnlyLongRunningHandlers(string handler, bool expected)
        => Assert.Equal(expected, GuiRules.UsesProgressWindow(TestTweaks.Action() with { Script = null, Handler = handler }));

    [Fact]
    public void UsesProgressWindow_ScriptActionTweakAndPanel_False()
    {
        Assert.False(GuiRules.UsesProgressWindow(TestTweaks.Action()));
        Assert.False(GuiRules.UsesProgressWindow(TestTweaks.TwoValues()));
        Assert.False(GuiRules.UsesProgressWindow(TestTweaks.Panel()));
        Assert.False(GuiRules.UsesProgressWindow(TestTweaks.TwoValues() with { Handler = "system-files" }));
        Assert.Throws<ArgumentNullException>(() => GuiRules.UsesProgressWindow(null!));
    }
}
