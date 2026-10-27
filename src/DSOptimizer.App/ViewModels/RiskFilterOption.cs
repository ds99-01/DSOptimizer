using DSOptimizer.Core.Gui;

namespace DSOptimizer.App.ViewModels;

/// <summary>Pozycja listy „Ryzyko”: wartość filtra i jej nazwa (ToString = nazwa – pole wyboru i czytnik ekranu).</summary>
public sealed record RiskFilterOption(RiskFilter Value, string Name)
{
    public override string ToString() => Name;
}
