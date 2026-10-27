using System;
using CommunityToolkit.Mvvm.ComponentModel;
using DSOptimizer.Core.Install;

namespace DSOptimizer.App.ViewModels;

/// <summary>
/// Wiersz strony „Instalacja”: pole wyboru (tylko <see cref="InstallRow.CanSelect"/>), tytuł, źródło drobnym drukiem (identyfikator
/// winget albo „GitHub: &lt;repo&gt;”),
/// opis z listy (<c>summary</c>), ostrzeżenie pozycji, notatka (Id skrócone w wynikach wyszukiwania) i status „Zainstalowana”.
/// Wiersze z pustym Id mają wspólne <c>Target.Id</c> („winget:”), więc widok nie używa go jako klucza – wiersz to ten obiekt.
/// </summary>
public sealed class InstallRowViewModel : ObservableObject
{
    private readonly Action _selectionChanged;
    private bool _isSelected;

    /// <param name="summary">Opis pozycji listy (<c>AppsInstallCatalog.Find(Id)?.Summary</c>); null = wynik wyszukiwania.</param>
    /// <param name="group">Grupa wiersza na stronie: kategoria albo grupa wyników winget (licznik grup w pasku serii).</param>
    public InstallRowViewModel(InstallRow row, string? summary, InstallCategoryOption group, Action selectionChanged)
    {
        Row = row ?? throw new ArgumentNullException(nameof(row));
        Group = group ?? throw new ArgumentNullException(nameof(group));
        _selectionChanged = selectionChanged ?? throw new ArgumentNullException(nameof(selectionChanged));
        Summary = string.IsNullOrWhiteSpace(summary) ? null : summary;
    }

    public InstallRow Row { get; }

    public InstallTarget Target => Row.Target;

    public InstallCategoryOption Group { get; }

    /// <summary>Nagłówek kategorii wiersza na listach z nagłówkami („Wszystkie”, „Zaznaczone”, „Z listy”; <see cref="InstallPage.CategoryHeader"/>).</summary>
    public string CategoryHeader => InstallPage.CategoryHeader(Row);

    public string Title => Row.Target.Title;

    /// <summary>Identyfikator winget (w wynikach wyszukiwania także skrócony „…” – wtedy wiersz nie ma pola wyboru); pusty dla GitHuba.</summary>
    public string WingetId => Row.Target.WingetId ?? string.Empty;

    /// <summary>Źródło drobnym drukiem: identyfikator winget albo <see cref="InstallText.GitHubLabel"/> pozycji z GitHuba.</summary>
    public string SourceLabel => Row.Target.Origin == InstallOrigin.GitHub && Row.Target.GitHub is { } repository
        ? InstallText.GitHubLabel(repository)
        : WingetId;

    /// <summary>Źródło widoczne; skrócone albo puste Id (wiersz z notatką) nie jest pokazywane (render wariantu A).</summary>
    public bool HasSourceLabel => !string.IsNullOrEmpty(SourceLabel) && !HasNote;

    public string? Summary { get; }

    public bool HasSummary => Summary is not null;

    public string? Warning => Row.Target.Warning;

    public bool HasWarning => !string.IsNullOrWhiteSpace(Row.Target.Warning);

    public string? Note => Row.Note;

    public bool HasNote => !string.IsNullOrWhiteSpace(Row.Note);

    public bool IsInstalled => Row.IsInstalled;

    /// <summary>Pole wyboru: ta sama reguła co seria w Core (<see cref="InstallRow.CanSelect"/>).</summary>
    public bool IsSelectable => Row.CanSelect;

    public bool IsSelected
    {
        get => _isSelected;
        set
        {
            if (value && !IsSelectable)
            {
                return;
            }

            if (SetProperty(ref _isSelected, value))
            {
                _selectionChanged();
            }
        }
    }

    /// <summary>Nazwa wiersza dla czytnika ekranu (styl kontenera listy wiąże tę właściwość).</summary>
    public string AutomationName => Title;

    public string AutomationHelpText => IsInstalled ? InstallText.InstalledStatus : Note ?? Summary ?? SourceLabel;
}
