using System;
using System.Collections.Generic;
using System.Linq;
using DSOptimizer.Core.Engine;
using DSOptimizer.Core.Model;

namespace DSOptimizer.Core.Gui;

/// <summary>Reguły aktywności przycisków i bramki potwierdzenia.</summary>
public static class GuiRules
{
    public static bool CanApply(TweakDefinition tweak, DetectionState? state)
    {
        Check(tweak);
        return tweak.Kind == TweakKind.Tweak
            && state is DetectionState.NotApplied or DetectionState.Partial or DetectionState.Unknown;
    }

    /// <summary>Cofnięcie niedostępne, gdy pozycja jest już cofnięta i czeka tylko na restart (<see cref="RebootPendingKind.Undone"/>).</summary>
    public static bool CanUndo(TweakDefinition tweak, DetectionState? state, RebootPendingKind? pending)
    {
        Check(tweak);
        return pending != RebootPendingKind.Undone
            && tweak.Kind == TweakKind.Tweak
            && state is DetectionState.Applied or DetectionState.Partial or DetectionState.Unknown;
    }

    public static bool CanExecute(TweakDefinition tweak)
    {
        Check(tweak);
        return tweak.Kind is TweakKind.Action or TweakKind.Panel;
    }

    /// <summary>
    /// Długa naprawa z oknem postępu (potwierdzenie → postęp w procentach → wynik): akcja z handlerem z
    /// <see cref="ActionHandlerNames.LongRunning"/>. Pozostałe akcje idą zwykłym „Uruchom”.
    /// </summary>
    public static bool UsesProgressWindow(TweakDefinition tweak)
    {
        Check(tweak);
        return tweak.Kind == TweakKind.Action && tweak.Handler is { } handler && ActionHandlerNames.LongRunning.Contains(handler);
    }

    public static bool RequiresConfirmation(TweakDefinition tweak)
    {
        Check(tweak);
        return tweak.Risk != RiskLevel.Safe || tweak.Kind == TweakKind.Action || tweak.Suggested;
    }

    /// <summary>Seria zawsze wymaga potwierdzenia, gdy ma więcej niż jedną pozycję albo którakolwiek wymaga go sama.</summary>
    public static bool SeriesRequiresConfirmation(IReadOnlyList<TweakDefinition> tweaks)
    {
        if (tweaks is null)
        {
            throw new ArgumentNullException(nameof(tweaks));
        }

        return tweaks.Count > 1 || tweaks.Any(RequiresConfirmation);
    }

    /// <summary>
    /// Co robi klik w wiersz. Pojedynczy klik tylko pokazuje pozycję (robi to ListBox). Dwuklik wiersza przełącza serię (tweak)
    /// albo uruchamia (akcja/panel); ▶ uruchamia już pierwszym klikiem; pole wyboru przełącza się samo.
    /// Wiersz reaguje tylko na dokładnie drugi klik: trzeci i dalsze nic nie robią (akcja nie ruszy drugi raz po zamknięciu potwierdzenia).
    /// <paramref name="firstTarget"/> to cel pierwszego kliku tej serii w tym samym wierszu (null = nieznany albo inny wiersz):
    /// gdy pierwszy klik trafił w ▶ i już uruchomił akcję, drugi (na granicy ▶ i wiersza) nie uruchamia jej ponownie.
    /// </summary>
    public static RowClickAction RowClick(bool isTweak, RowClickTarget target, int clickCount, RowClickTarget? firstTarget = null)
    {
        return target switch
        {
            RowClickTarget.Row when clickCount == 2 && firstTarget == RowClickTarget.RunIcon => RowClickAction.None,
            RowClickTarget.Row when clickCount == 2 => isTweak ? RowClickAction.ToggleSeries : RowClickAction.Run,
            RowClickTarget.RunIcon when !isTweak && clickCount == 1 => RowClickAction.Run,
            _ => RowClickAction.None,
        };
    }

    private static void Check(TweakDefinition tweak)
    {
        if (tweak is null)
        {
            throw new ArgumentNullException(nameof(tweak));
        }
    }
}