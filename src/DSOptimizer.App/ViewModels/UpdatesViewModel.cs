using System;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using DSOptimizer.App.Gui;
using DSOptimizer.Core;
using DSOptimizer.Core.Localization;
using DSOptimizer.Core.Updates;

namespace DSOptimizer.App.ViewModels;

/// <summary>
/// Komunikat o nowej wersji (baner w oknie) i ręczne „Sprawdź aktualizacje” w Ustawieniach. Program niczego nie pobiera: wynik to
/// numer wersji i link do strony wydań. Sprawdzenie zawsze idzie przez <c>Task.Run</c>, więc wątek okna nie czeka na sieć;
/// start nigdy nie rzuca i nie pokazuje błędu. Ukrycie baneru obowiązuje do końca uruchomienia: spóźniony wynik go nie przywraca.
/// </summary>
public sealed class UpdatesViewModel : ObservableObject
{
    private readonly UpdateChecker _checker;
    private readonly ShellActions _shell;
    private bool _noticeHidden;
    private bool _isNoticeVisible;
    private string _noticeText = string.Empty;
    private string? _checkResultText;
    private bool _hasNewerFromCheck;

    public UpdatesViewModel(UpdateChecker checker, ShellActions shell)
    {
        _checker = checker ?? throw new ArgumentNullException(nameof(checker));
        _shell = shell ?? throw new ArgumentNullException(nameof(shell));
        CheckNowCommand = new AsyncRelayCommand(CheckNowAsync);
        // Kilka klików w link w trakcie sprawdzania powłoki ignoruje ShellActions; przycisk nie przygasa na 1–3 s.
        OpenReleasesCommand = new AsyncRelayCommand(OpenReleasesAsync, AsyncRelayCommandOptions.AllowConcurrentExecutions);
        HideNoticeCommand = new RelayCommand(HideNotice);
    }

    public bool IsNoticeVisible
    {
        get => _isNoticeVisible;
        private set => SetProperty(ref _isNoticeVisible, value);
    }

    public string NoticeText
    {
        get => _noticeText;
        private set => SetProperty(ref _noticeText, value);
    }

    /// <summary>Wynik ostatniego „Sprawdź aktualizacje”; <c>null</c> = jeszcze nie sprawdzano.</summary>
    public string? CheckResultText
    {
        get => _checkResultText;
        private set => SetProperty(ref _checkResultText, value);
    }

    /// <summary>Ręczne sprawdzenie znalazło nowszą wersję (link do strony wydań w Ustawieniach).</summary>
    public bool HasNewerFromCheck
    {
        get => _hasNewerFromCheck;
        private set => SetProperty(ref _hasNewerFromCheck, value);
    }

    public IAsyncRelayCommand CheckNowCommand { get; }

    public IAsyncRelayCommand OpenReleasesCommand { get; }

    public IRelayCommand HideNoticeCommand { get; }

    /// <summary>Sprawdzenie przy starcie okna: cicho przy braku nowej wersji i przy każdym błędzie.</summary>
    public async Task CheckAtStartAsync()
    {
        try
        {
            var result = await Task.Run(_checker.Check).ConfigureAwait(true);
            if (result.Outcome == UpdateCheckOutcome.NewerAvailable && result.LatestVersion is { } version && !_noticeHidden)
            {
                NoticeText = L.F("Dostępna jest nowa wersja: {0}.", version);
                IsNoticeVisible = true;
            }
        }
        catch (Exception)
        {
            // Start programu nigdy nie zależy od sprawdzenia wersji.
        }
    }

    private async Task CheckNowAsync()
    {
        CheckResultText = ItemViewModel.CheckingText;
        HasNewerFromCheck = false;
        UpdateCheckResult result;
        try
        {
            result = await Task.Run(_checker.Check).ConfigureAwait(true);
        }
        catch (Exception)
        {
            result = new UpdateCheckResult(UpdateCheckOutcome.Failed, null);
        }

        switch (result.Outcome)
        {
            case UpdateCheckOutcome.UpToDate:
                CheckResultText = L.T("Masz najnowszą wersję");
                break;
            case UpdateCheckOutcome.NewerAvailable when result.LatestVersion is { } version:
                CheckResultText = L.F("Dostępna wersja {0}", version);
                HasNewerFromCheck = true;
                break;
            default:
                CheckResultText = L.T("Nie udało się sprawdzić");
                break;
        }
    }

    private void HideNotice()
    {
        _noticeHidden = true;
        IsNoticeVisible = false;
    }

    // ShellActions.OpenUrlAsync nie rzuca (odmowa = ostrzeżenie i kopia adresu); try/catch, by nic nie wyciekło na wątek UI.
    private async Task OpenReleasesAsync()
    {
        try
        {
            await _shell.OpenUrlAsync(ProductInfo.ReleasesUrl).ConfigureAwait(true);
        }
        catch (Exception)
        {
            // Odmowa i błędy powłoki są już w dzienniku ShellActions.
        }
    }
}
