using System;
using System.Globalization;
using CommunityToolkit.Mvvm.ComponentModel;
using DSOptimizer.Core.Install;

namespace DSOptimizer.App.ViewModels;

/// <summary>
/// Pozycja listy kategorii strony „Instalacja” (wariant A): „Wszystkie” na górze (D1), pod nią „Zaznaczone” (licznik = liczba
/// zaznaczonych, także 0), potem kategorie listy <c>apps.json</c>. Które wiersze kategorii pokazuje – <see cref="InstallGroup.Contains"/>
/// w Core; „Zaznaczone” składa ViewModel (zaznaczenie zna tylko on). Licznik = liczba wierszy grupy; przed pierwszym odczytem pusty.
/// Grupa wyników winget (<see cref="InstallGroup.Search"/>) nie jest na liście kategorii – służy tylko jako grupa wiersza winget.
/// </summary>
public sealed class InstallCategoryOption : ObservableObject
{
    private int? _count;

    public InstallCategoryOption(InstallGroup group)
    {
        Group = group ?? throw new ArgumentNullException(nameof(group));
    }

    /// <summary>Model pozycji w Core (rodzaj, kategoria, nazwa, reguła wierszy).</summary>
    public InstallGroup Group { get; }

    /// <summary>Kategoria listy; null = „Wszystkie”, „Zaznaczone” albo grupa wyników winget.</summary>
    public InstallCategory? Category => Group.Category;

    public bool IsAll => Group.Kind == InstallGroupKind.All;

    public bool IsSelection => Group.Kind == InstallGroupKind.Selected;

    public string Name => Group.Name;

    /// <summary>Liczba wierszy grupy; null = jeszcze nie ma czego liczyć (licznik pusty).</summary>
    public int? Count
    {
        get => _count;
        set
        {
            if (SetProperty(ref _count, value))
            {
                OnPropertyChanged(nameof(CountText));
            }
        }
    }

    public string CountText => _count is { } count ? count.ToString(CultureInfo.InvariantCulture) : string.Empty;

    public override string ToString() => Name;
}
