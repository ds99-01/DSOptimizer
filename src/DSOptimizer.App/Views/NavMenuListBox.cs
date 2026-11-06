using System.Collections.Generic;
using System.Linq;
using System.Windows.Automation.Peers;
using System.Windows.Controls;
using DSOptimizer.App.ViewModels;

namespace DSOptimizer.App.Views;

/// <summary>
/// Górna lista menu z liniami grup (<see cref="NavSeparatorViewModel"/>). Linia to tylko ozdoba: czytnik ekranu jej nie widzi,
/// a „pozycja N z M” liczy same strony (<see cref="NavMenuListBoxAutomationPeer"/>).
/// </summary>
public sealed class NavMenuListBox : ListBox
{
    protected override AutomationPeer OnCreateAutomationPeer() => new NavMenuListBoxAutomationPeer(this);
}

/// <summary>Peer listy menu: dzieci = strony, bez linii grup.</summary>
public sealed class NavMenuListBoxAutomationPeer : ListBoxAutomationPeer
{
    public NavMenuListBoxAutomationPeer(NavMenuListBox owner)
        : base(owner)
    {
    }

    /// <summary>Strony menu w kolejności listy (bez linii grup) – podstawa „pozycji N z M”.</summary>
    internal List<object> Pages => ((ListBox)Owner).Items.Cast<object>().Where(item => item is not NavSeparatorViewModel).ToList();

    protected override List<AutomationPeer>? GetChildrenCore()
        => base.GetChildrenCore()?.Where(peer => peer is not ItemAutomationPeer { Item: NavSeparatorViewModel }).ToList();

    protected override ItemAutomationPeer CreateItemAutomationPeer(object item) => new NavMenuItemAutomationPeer(item, this);
}

/// <summary>Peer strony menu: pozycja i liczność zestawu liczone bez linii grup.</summary>
public sealed class NavMenuItemAutomationPeer : ListBoxItemAutomationPeer
{
    private readonly NavMenuListBoxAutomationPeer _list;

    public NavMenuItemAutomationPeer(object item, NavMenuListBoxAutomationPeer list)
        : base(item, list)
    {
        _list = list;
    }

    protected override int GetPositionInSetCore() => _list.Pages.IndexOf(Item) + 1;

    protected override int GetSizeOfSetCore() => _list.Pages.Count;
}
