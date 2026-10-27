using System.Collections.Generic;
using System.Globalization;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using DSOptimizer.Core.Gui;
using DSOptimizer.Core.Model;

namespace DSOptimizer.App.ViewModels;

/// <summary>
/// Jedna pozycja katalogu: teksty wiersza i panelu szczegółów, stan z detekcji, zaznaczenie do operacji zbiorczej
/// (<see cref="IsSelected"/> – to NIE jest stan) i polecenia aktywne wg <see cref="GuiRules"/> i braku operacji w toku.
/// </summary>
public sealed class ItemViewModel : ObservableObject
{
    public const string CheckingText = "Sprawdzanie…";
    public const string SettingsSection = "Ustawienia";
    public const string ActionsSection = "Akcje";

    private readonly MainViewModel _owner;
    private DetectionState? _state;
    private DetectionState? _knownState;
    private bool _isChecking;
    private RebootPendingKind? _pendingReboot;
    private bool _isSelected;

    public ItemViewModel(TweakDefinition definition, MainViewModel owner)
    {
        Definition = definition;
        _owner = owner;
        _isChecking = definition.Kind == TweakKind.Tweak;
        ExecuteCommand = new AsyncRelayCommand(() => _owner.RunAsync(this, GuiOperation.Execute), () => !_owner.IsBusy && GuiRules.CanExecute(Definition));
    }

    public TweakDefinition Definition { get; }

    public string Id => Definition.Id;

    public string Title => Definition.Title;

    public string Description => Definition.Description;

    /// <summary>Krótki ludzki opis: podpowiedź tytułu wiersza, opis wiersza dla czytnika ekranu i akapit pod sekcją „Co zyskasz” w panelu szczegółów.</summary>
    public string Summary => Definition.Summary;

    /// <summary>Sekcja „Co zyskasz” w panelu szczegółów (tylko tweak i akcja; panel jej nie ma).</summary>
    public string Benefit => Definition.Benefit ?? string.Empty;

    public bool HasBenefit => !string.IsNullOrWhiteSpace(Definition.Benefit);

    /// <summary>Nagłówek grupy w wynikach wyszukiwania.</summary>
    public string CategoryName => GuiText.CategoryName(Definition.Category);

    public IReadOnlyList<string> Warnings => Definition.Warnings;

    public RiskLevel Risk => Definition.Risk;

    public string RiskName => GuiText.RiskName(Definition.Risk);

    /// <summary>Podpowiedź i nazwa dla czytnika ekranu wskaźnika ryzyka w wierszu i w panelu: „Ryzyko: bezpieczne”.</summary>
    public string RiskToolTip => GuiText.RiskToolTip(Definition.Risk);

    /// <summary>Pozycja z <c>suggested</c> w katalogu: zaznacza się sama przy pierwszym wejściu na kategorię.</summary>
    public bool IsSuggested => Definition.Suggested;

    public bool HasEditions => EditionsText is not null;

    /// <summary>Linia „Działa w edycjach: …” w panelu szczegółów; null, gdy pozycja nie ma ograniczenia <c>editions</c>.</summary>
    public string? EditionsText => Definition.Editions is { } editions ? GuiText.EditionsLine(editions) : null;

    public bool IsTweak => Definition.Kind == TweakKind.Tweak;

    public bool IsExecutable => Definition.Kind is TweakKind.Action or TweakKind.Panel;

    /// <summary>Grupa w środkowej liście: tweaki w „Ustawieniach”, akcje i panele w „Akcjach”.</summary>
    public string SectionName => IsTweak ? SettingsSection : ActionsSection;

    public DetectionState? State => _state;

    /// <summary>
    /// Ostatni stan z detekcji; w trakcie ponownego sprawdzania („Sprawdzanie…”, <see cref="State"/> = null) zostaje
    /// poprzedni. Na nim liczą się widoczność przy filtrze ryzyka, „Ukryte przez filtr” i licznik w menu – inaczej
    /// „Odśwież stan” chowałby na chwilę pozycje zastosowane na tym komputerze i przestawiał bieżącą pozycję.
    /// </summary>
    public DetectionState? KnownState => _knownState;

    public string StateText => _isChecking ? CheckingText : GuiText.StateName(_state);

    /// <summary>Drugi wiersz pozycji na liście.</summary>
    public string RowSubtitle
    {
        get
        {
            if (!IsTweak)
            {
                return Definition.Kind == TweakKind.Panel ? "Otwiera okno systemowe" : "Tej operacji nie można cofnąć";
            }

            return _isChecking ? StateText : GuiText.ItemRowSubtitle(StateText, _pendingReboot);
        }
    }

    /// <summary>Co z journala czeka na restart dla tej pozycji (null: nic); ustawia <see cref="MainViewModel"/> po każdym odczycie oczekiwania.</summary>
    public RebootPendingKind? PendingReboot
    {
        get => _pendingReboot;
        set
        {
            if (SetProperty(ref _pendingReboot, value))
            {
                OnPropertyChanged(nameof(RowSubtitle));
                OnPropertyChanged(nameof(AutomationName));
                OnPropertyChanged(nameof(SeriesLineText));
                OnPropertyChanged(nameof(SelectHint));
                if (_isSelected)
                {
                    _owner.RefreshSelection();
                }
            }
        }
    }

    /// <summary>Nazwa wiersza listy dla czytnika ekranu (kontener wiersza; bez niej czytnik przeczytałby nazwę typu).</summary>
    public string AutomationName => GuiText.ItemRowName(Title, RowSubtitle);

    /// <summary>Opis wiersza dla czytnika ekranu (<c>AutomationProperties.HelpText</c> kontenera): to samo co podpowiedź tytułu; dla sugerowanej z dopiskiem „Sugerowane.” (żarówka jest tylko wizualna).</summary>
    public string AutomationHelpText => GuiText.ItemRowHelp(Summary, IsSuggested && State != DetectionState.NotSupported);

    // Panel szczegółów: tabela klucz–wartość.
    public string DetailStateText => IsTweak ? StateText : GuiText.StateName(null);

    /// <summary>Nazwa ryzyka z <see cref="GuiText.RiskName"/>, w tabeli zaczęta wielką literą jak pozostałe wartości.</summary>
    public string DetailRiskText => CultureInfo.GetCultureInfo("pl-PL").TextInfo.ToUpper(RiskName[0]) + RiskName.Substring(1);

    public string DetailRebootText => Definition.RequiresReboot ? "Wymagany" : "Niewymagany";

    public string DetailUndoText => !IsTweak ? "Nie dotyczy" : Definition.Reversible ? "Dostępne" : "Niedostępne";

    /// <summary>Zaznaczenie do serii (dwuklik wiersza tweaka, Spacja albo pole wyboru; krawędź w kolorze akcentu). Przeżywa zmianę kategorii.</summary>
    public bool IsSelected
    {
        get => _isSelected;
        set
        {
            if (SetProperty(ref _isSelected, value && IsTweak))
            {
                OnPropertyChanged(nameof(SeriesLineText));
                _owner.RefreshSelection();
            }
        }
    }

    /// <summary>Linia panelu szczegółów: czy tweak jest w serii (<see cref="IsSelected"/>) i do którego przycisku trafi; widoczna tylko dla tweaków.</summary>
    public string SeriesLineText => GuiText.SeriesLine(IsSelected, CanApplyNow, CanUndoNow);

    /// <summary>Podpowiedź pola wyboru: co da zaznaczenie przy bieżącym stanie.</summary>
    public string SelectHint => GuiText.SelectHint(CanApplyNow, CanUndoNow);

    private bool CanApplyNow => IsTweak && GuiRules.CanApply(Definition, State);

    private bool CanUndoNow => IsTweak && GuiRules.CanUndo(Definition, State, PendingReboot);

    public IAsyncRelayCommand ExecuteCommand { get; }

    public void SetChecking()
    {
        if (!IsTweak)
        {
            return;
        }

        _isChecking = true;
        _state = null;
        Changed();
    }

    public void SetState(DetectionState state)
    {
        _isChecking = false;
        _state = state;
        _knownState = state;
        Changed();
    }

    /// <summary>Pozycja, której stan nie dotarł (np. błąd detekcji) – nie zostaje na zawsze w „Sprawdzanie…”.</summary>
    public void SettleIfChecking()
    {
        if (_isChecking)
        {
            SetState(DetectionState.Unknown);
        }
    }

    public void RefreshCommands()
    {
        ExecuteCommand.NotifyCanExecuteChanged();
    }

    private void Changed()
    {
        OnPropertyChanged(nameof(State));
        OnPropertyChanged(nameof(StateText));
        OnPropertyChanged(nameof(RowSubtitle));
        OnPropertyChanged(nameof(AutomationName));
        OnPropertyChanged(nameof(DetailStateText));
        OnPropertyChanged(nameof(SeriesLineText));
        OnPropertyChanged(nameof(SelectHint));
        RefreshCommands();
        if (_isSelected)
        {
            _owner.RefreshSelection();
        }

        // „Sprawdzanie…” nie zmienia KnownState, więc widoczność i liczniki zostają do nadejścia prawdziwego stanu.
        if (_state is not null)
        {
            _owner.OnItemStateChanged();
        }
    }
}
