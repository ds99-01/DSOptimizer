using System;
using System.Threading;
using System.Threading.Tasks;
using DSOptimizer.Core.Engine;
using DSOptimizer.Core.Gui;

namespace DSOptimizer.App.Gui;

/// <summary>
/// Wynik okna serii. <see cref="Report"/> == null oznacza wyłącznie anulowanie w potwierdzeniu (seria nie ruszyła).
/// <see cref="RestartRequested"/> = użytkownik potwierdził „Uruchom ponownie teraz” w wyniku.
/// </summary>
public sealed record SeriesDialogResult(GuiOperationReport? Report, bool RestartRequested);

/// <summary>Okno serii w skórce aplikacji: potwierdzenie, postęp z „Zatrzymaj” i wynik (jedno okno, trzy stany).</summary>
public interface ISeriesDialogs
{
    /// <summary>Samo potwierdzenie (bez postępu i wyniku); true tylko po kliknięciu przycisku głównego.</summary>
    Task<bool> ConfirmAsync(SeriesConfirmation confirmation);

    /// <summary>
    /// Potwierdzenie, a po zgodzie postęp i wynik w tym samym oknie. <paramref name="run"/> jest wołane na wątku UI
    /// z postępem utworzonym na wątku UI i sygnałem „Zatrzymaj”; wyjątek z <paramref name="run"/> kończy serię błędem
    /// (raport z jedną linią Error, nigdy null).
    /// </summary>
    Task<SeriesDialogResult> RunSeriesAsync(SeriesConfirmation confirmation, Func<IProgress<BatchStepEvent>, BatchStop, Task<GuiOperationReport>> run);

    /// <summary>
    /// Długa naprawa (<see cref="GuiRules.UsesProgressWindow"/>): potwierdzenie, a po zgodzie postęp w procentach i wynik w tym
    /// samym oknie, na tych samych zasadach co <see cref="RunSeriesAsync"/> (<c>Report == null</c> = anulowano w potwierdzeniu,
    /// w postępie okna nie da się zamknąć, „Uruchom ponownie teraz” w wyniku przy restarcie). <paramref name="run"/> jest wołane
    /// na wątku UI z postępem utworzonym na wątku UI i tokenem „Zatrzymaj po tym kroku” (handler sprawdza go tylko między krokami).
    /// </summary>
    Task<SeriesDialogResult> RunActionAsync(
        SeriesConfirmation confirmation,
        Func<IProgress<ActionProgress>, CancellationToken, Task<GuiOperationReport>> run);

    /// <summary>Potwierdzenie ponownego uruchomienia komputera; true tylko po kliknięciu „Uruchom ponownie”.</summary>
    Task<bool> ConfirmRestartAsync();

    /// <summary>
    /// „Kontynuować bez punktu przywracania?” w trwającym oknie serii (wołać na wątku UI, np. przez dispatcher z wątku silnika).
    /// Domyślny przycisk „Nie”; true tylko po „Tak, kontynuuj”. Bez trwającej serii w oknie = false (nic nie jest usuwane).
    /// <paramref name="reason"/> (dlaczego punkt nie powstał) trafia z odpowiedzią do dziennika.
    /// </summary>
    Task<bool> ConfirmContinueWithoutRestorePointAsync(string reason);
}
