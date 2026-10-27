using CommunityToolkit.Mvvm.ComponentModel;
using DSOptimizer.Core.Apps;

namespace DSOptimizer.App.ViewModels;

/// <summary>Pozycja przełącznika grup nad listą aplikacji (wariant B): nazwa, licznik i czy jest wybrana.</summary>
public sealed class AppGroupOption : ObservableObject
{
    private int _count;
    private bool _isSelected;

    public AppGroupOption(AppGroup value)
    {
        Value = value;
        Name = AppsPageText.GroupName(value);
    }

    public AppGroup Value { get; }

    public string Name { get; }

    /// <summary>Liczba aplikacji w grupie (przy wyszukiwaniu – pasujących do frazy).</summary>
    public int Count
    {
        get => _count;
        set => SetProperty(ref _count, value);
    }

    public bool IsSelected
    {
        get => _isSelected;
        set => SetProperty(ref _isSelected, value);
    }

    public override string ToString() => Name;
}
