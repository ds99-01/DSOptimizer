namespace DSOptimizer.Core.Engine;

/// <summary>
/// Postęp długiej akcji (okno postępu). <paramref name="Stage"/> to krótka nazwa etapu („DISM”, „SFC”, „Pliki tymczasowe”),
/// <paramref name="Percent"/> to 0–100 albo <c>null</c> (nieokreślony), <paramref name="StepIndex"/> liczony od 0.
/// <paramref name="NextStage"/> to nazwa następnego kroku („Dalej: …” w oknie); <c>null</c> na ostatnim kroku albo gdy nieznana.
/// <paramref name="Stoppable"/> = <c>false</c>: przebiegu nie da się przerwać po potwierdzeniu (okno nie pokazuje aktywnego
/// „Zatrzymaj” mimo kolejnego kroku, np. <c>windows-old</c> – Ruling R30); domyślnie <c>true</c> = „Zatrzymaj” między krokami.
/// </summary>
public sealed record ActionProgress(string Stage, int? Percent, int StepIndex, int StepCount, string? NextStage = null, bool Stoppable = true);
