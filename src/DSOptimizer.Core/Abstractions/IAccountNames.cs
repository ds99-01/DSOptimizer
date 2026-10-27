namespace DSOptimizer.Core.Abstractions;

/// <summary>Tłumaczenie SID na nazwę konta. Zwraca null przy braku nazwy; implementacja nie rzuca przy złym albo nieprzetłumaczalnym SID.</summary>
public interface IAccountNames
{
    string? NameOf(string sid);
}
