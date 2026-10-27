using DSOptimizer.Core.Gui;

namespace DSOptimizer.App.ViewModels;

/// <summary>Pozycja listy filtra historii: wartość i jej nazwa (ToString = nazwa – pole wyboru i czytnik ekranu).</summary>
public sealed record HistoryFilterOption(HistoryFilter Value, string Name)
{
    public override string ToString() => Name;
}
