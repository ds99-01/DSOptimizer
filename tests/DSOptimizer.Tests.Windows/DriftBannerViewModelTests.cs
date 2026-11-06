using System;
using System.Collections.Generic;
using DSOptimizer.App.ViewModels;
using Xunit;

namespace DSOptimizer.Tests.Windows;

/// <summary>Model banera strażnika ustawień: sam ViewModel, bez okna WPF.</summary>
[Trait("Category", "Windows")]
public class DriftBannerViewModelTests
{
    private static readonly string[] Three = { "a", "b", "c" };

    [Fact]
    public void Start_HiddenWithoutIds()
    {
        var vm = new DriftBannerViewModel(_ => { }, () => true);

        Assert.Empty(vm.Ids);
        Assert.False(vm.IsVisible);
    }

    [Fact]
    public void Ctor_Null_Throws()
    {
        Assert.Throws<ArgumentNullException>(() => new DriftBannerViewModel(null!, () => true));
        Assert.Throws<ArgumentNullException>(() => new DriftBannerViewModel(_ => { }, null!));
    }

    [Fact]
    public void Update_Three_VisibleWithText()
    {
        var vm = new DriftBannerViewModel(_ => { }, () => true);
        var changed = new List<string?>();
        vm.PropertyChanged += (_, e) => changed.Add(e.PropertyName);

        vm.Update(Three);

        Assert.True(vm.IsVisible);
        Assert.Equal("3 pozycje nie są już w pełni zastosowane.", vm.Text);
        Assert.Equal(Three, vm.Ids);
        Assert.Contains(nameof(DriftBannerViewModel.IsVisible), changed);
        Assert.Contains(nameof(DriftBannerViewModel.Text), changed);
        Assert.Contains(nameof(DriftBannerViewModel.Ids), changed);
    }

    [Fact]
    public void Update_Empty_HidesAgain()
    {
        var vm = new DriftBannerViewModel(_ => { }, () => true);
        vm.Update(Three);

        vm.Update(Array.Empty<string>());

        Assert.False(vm.IsVisible);
    }

    [Fact]
    public void Hide_ThenUpdate_StaysHidden()
    {
        var vm = new DriftBannerViewModel(_ => { }, () => true);
        vm.Update(Three);

        vm.HideCommand.Execute(null);
        Assert.False(vm.IsVisible);

        vm.Update(new[] { "x" });
        Assert.False(vm.IsVisible);
        Assert.Equal(new[] { "x" }, vm.Ids);
    }

    [Fact]
    public void SelectAndShow_PassesCurrentIds()
    {
        IReadOnlyList<string>? received = null;
        var vm = new DriftBannerViewModel(ids => received = ids, () => true);
        vm.Update(Three);

        Assert.True(vm.SelectAndShowCommand.CanExecute(null));
        vm.SelectAndShowCommand.Execute(null);

        Assert.Equal(Three, received);
    }

    [Fact]
    public void SelectAndShow_Disabled_WhenCannotSelectOrNoIds()
    {
        var canSelect = false;
        var calls = 0;
        var vm = new DriftBannerViewModel(_ => calls++, () => canSelect);

        vm.Update(Three);
        Assert.False(vm.SelectAndShowCommand.CanExecute(null));
        vm.SelectAndShowCommand.Execute(null);
        Assert.Equal(0, calls);

        canSelect = true;
        vm.RefreshCanSelect();
        Assert.True(vm.SelectAndShowCommand.CanExecute(null));

        vm.Update(Array.Empty<string>());
        Assert.False(vm.SelectAndShowCommand.CanExecute(null));
        vm.SelectAndShowCommand.Execute(null);
        Assert.Equal(0, calls);
    }
}
