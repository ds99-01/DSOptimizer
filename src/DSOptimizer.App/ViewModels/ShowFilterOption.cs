using DSOptimizer.Core.Gui;

namespace DSOptimizer.App.ViewModels;

/// <summary>Pozycja listy „Pokaż”: wartość filtra i jej nazwa (ToString = nazwa – pole wyboru i czytnik ekranu).</summary>
public sealed record ShowFilterOption(ShowFilter Value, string Name)
{
    public override string ToString() => Name;
}
