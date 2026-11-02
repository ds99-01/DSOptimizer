using System.Globalization;
using CommunityToolkit.Mvvm.ComponentModel;
using DSOptimizer.Core.Autostart;

namespace DSOptimizer.App.ViewModels;

/// <summary>Pozycja przełącznika grup nad listą autostartu: nazwa, licznik, czy jest wybrana i nazwa dla czytnika ekranu.</summary>
public sealed class AutostartGroupOption : ObservableObject
{
    private int _count;
    private bool _isSelected;

    public AutostartGroupOption(AutostartGroup value)
    {
        Value = value;
        Name = AutostartText.GroupName(value);
    }

    public AutostartGroup Value { get; }

    public string Name { get; }

    /// <summary>Liczba pozycji w grupie (przy wyszukiwaniu – pasujących do frazy).</summary>
    public int Count
    {
        get => _count;
        set
        {
            if (SetProperty(ref _count, value))
            {
                OnPropertyChanged(nameof(AutomationName));
            }
        }
    }

    public bool IsSelected
    {
        get => _isSelected;
        set => SetProperty(ref _isSelected, value);
    }

    /// <summary>„Włączone, 11” – nazwa z licznikiem dla czytnika ekranu.</summary>
    public string AutomationName => Name + ", " + Count.ToString(CultureInfo.InvariantCulture);

    public override string ToString() => Name;
}
