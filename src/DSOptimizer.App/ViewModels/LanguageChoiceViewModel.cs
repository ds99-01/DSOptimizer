using System;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using DSOptimizer.Core.Localization;

namespace DSOptimizer.App.ViewModels;

/// <summary>
/// Okno wyboru języka przy pierwszym uruchomieniu: kafel wykrytego języka jest propozycją (obrys akcentu, Enter),
/// klik kafla = odpowiedź i zamknięcie okna. Zamknięcie bez kliku zostawia <see cref="Answer"/> = null (program bierze język wykryty).
/// </summary>
public sealed class LanguageChoiceViewModel : ObservableObject
{
    private UiLanguage? _answer;

    public LanguageChoiceViewModel(UiLanguage detected)
    {
        Detected = detected;
        ChoosePolishCommand = new RelayCommand(() => Choose(UiLanguage.Polish));
        ChooseEnglishCommand = new RelayCommand(() => Choose(UiLanguage.English));
    }

    /// <summary>Język wykryty z systemu – propozycja okna.</summary>
    public UiLanguage Detected { get; }

    public bool IsPolishDetected => Detected == UiLanguage.Polish;

    public bool IsEnglishDetected => Detected == UiLanguage.English;

    /// <summary>Wybrany język; null, dopóki nie kliknięto kafla.</summary>
    public UiLanguage? Answer
    {
        get => _answer;
        private set => SetProperty(ref _answer, value);
    }

    public IRelayCommand ChoosePolishCommand { get; }

    public IRelayCommand ChooseEnglishCommand { get; }

    /// <summary>Po wyborze okno ma się zamknąć.</summary>
    public event EventHandler? CloseRequested;

    private void Choose(UiLanguage language)
    {
        Answer = language;
        CloseRequested?.Invoke(this, EventArgs.Empty);
    }
}
