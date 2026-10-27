using CommunityToolkit.Mvvm.ComponentModel;

namespace DSOptimizer.App.ViewModels;

/// <summary>Rodzaj linii listy strony „Instalacja” między wierszami aplikacji.</summary>
public enum InstallListLineKind
{
    /// <summary>Nagłówek z linią pod spodem: sekcja wyszukiwania („Z listy (N)”, „W winget (N)”) albo kategoria w „Wszystkie”/„Zaznaczone”.</summary>
    Header,

    /// <summary>Nagłówek kategorii wewnątrz sekcji „Z listy (N)” – drobniejszy, bez linii.</summary>
    SubHeader,

    /// <summary>Notatka drobnym drukiem (zdanie nad wynikami winget, „… jest na liście – wyżej”, „Szukanie w winget”).</summary>
    Note,

    /// <summary>Komunikat wyszukiwania winget w jego sekcji (brak sieci, nic nie znaleziono, błąd); ostrzeżenie z trójkątem.</summary>
    Message,
}

/// <summary>
/// Linia listy strony „Instalacja”, która nie jest aplikacją: nagłówek albo notatka. Lista jest płaska (linie i wiersze
/// <see cref="InstallRowViewModel"/> w jednej kolejności), a widok daje linii kontener bez fokusu i bez zaznaczenia,
/// więc strzałki przeskakują ją, a czytnik ekranu czyta jej tekst. Licznik nagłówka w „Zaznaczone” zmienia się na miejscu
/// (<see cref="SetText"/>) – odznaczony wiersz zostaje na liście, a lista się nie przebudowuje.
/// </summary>
public sealed class InstallListLine : ObservableObject
{
    private string _text;
    private string _countText;

    public InstallListLine(InstallListLineKind kind, string text, string? countText = null, bool isWarning = false, bool isFirst = false)
    {
        Kind = kind;
        _text = text ?? string.Empty;
        _countText = countText ?? string.Empty;
        IsWarning = isWarning;
        IsFirst = isFirst;
    }

    public InstallListLineKind Kind { get; }

    public string Text => _text;

    /// <summary>Licznik obok nagłówka kategorii (jak dotąd w „Wszystkie”); pusty, gdy liczba jest już w tekście („Z listy (N)”).</summary>
    public string CountText => _countText;

    public bool HasCount => _countText.Length > 0;

    /// <summary>Komunikat z trójkątem ostrzeżenia (wszystko poza „nic nie znaleziono”).</summary>
    public bool IsWarning { get; }

    /// <summary>Pierwsza linia listy – bez odstępu nad nagłówkiem.</summary>
    public bool IsFirst { get; }

    /// <summary>Nazwa linii dla czytnika ekranu (kontener listy).</summary>
    public string AutomationName => HasCount ? _text + " " + _countText : _text;

    /// <summary>Nowy tekst i licznik nagłówka (liczniki „Zaznaczone” po odznaczeniu wiersza).</summary>
    public void SetText(string text, string? countText)
    {
        var textChanged = SetProperty(ref _text, text ?? string.Empty, nameof(Text));
        var countChanged = SetProperty(ref _countText, countText ?? string.Empty, nameof(CountText));
        if (countChanged)
        {
            OnPropertyChanged(nameof(HasCount));
        }

        if (textChanged || countChanged)
        {
            OnPropertyChanged(nameof(AutomationName));
        }
    }

    public override string ToString() => _text;
}
