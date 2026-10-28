using System;
using System.Collections.Generic;
using System.Threading;
using DSOptimizer.Core.Journal;
using DSOptimizer.Core.Localization;
using DSOptimizer.Core.Model;

namespace DSOptimizer.Core.Engine;

public sealed partial class TweakEngine
{
    private TweakResult Finish(
        TweakDefinition tweak,
        TweakOutcome outcome,
        DetectionState? state,
        string? message,
        IReadOnlyList<string>? warnings = null,
        IReadOnlyList<string>? notRestored = null,
        bool requiresReboot = false)
    {
        var result = new TweakResult
        {
            TweakId = tweak.Id,
            Outcome = outcome,
            FinalState = state,
            Message = message,
            Warnings = warnings ?? Array.Empty<string>(),
            NotRestored = notRestored ?? Array.Empty<string>(),
            RequiresReboot = requiresReboot,
        };

        var line = message is null ? $"{tweak.Id}: {outcome}" : $"{tweak.Id}: {outcome} – {message}";
        if (result.IsSuccess)
        {
            _s.Log.Info(Component, line);
        }
        else
        {
            _s.Log.Warn(Component, line);
        }

        return result;
    }

    private List<string> UserWarnings(TweakDefinition tweak)
    {
        var warnings = new List<string>();
        if (tweak.Scope == TweakScope.User && !_s.User.IsCertain)
        {
            warnings.Add(L.F("Nie udało się pewnie ustalić zalogowanego użytkownika – zmiany trafiają do profilu {0}.", _s.User.Sid));
        }

        return warnings;
    }

    private DetectionState SafeDetect(TweakDefinition tweak)
    {
        try
        {
            return DetectSafely(tweak, _factory.Create(tweak), CancellationToken.None);
        }
        catch (Exception ex)
        {
            _s.Log.Fail(Component, L.F("Detekcja {0} przerwana nieoczekiwanym błędem.", tweak.Id), ex);
            return DetectionState.Unknown;
        }
    }

    private void TrySave(JournalDocument document, List<string> warnings)
    {
        try
        {
            _s.Journal.Save(document);
        }
        catch (Exception ex)
        {
            if (ex is JournalStoreException)
            {
                _s.Log.Warn(Component, L.F("Nie udało się zapisać journala: {0}", ex.Message));
            }
            else
            {
                _s.Log.Fail(Component, L.T("Nieoczekiwany błąd zapisu journala."), ex);
            }

            warnings.Add(L.F("Nie udało się zapisać journala: {0}", ex.Message));
        }
    }

    /// <summary>Ostrzeżenie z wczytania journala trafia do wyniku i do logu.</summary>
    private void AddLoadWarning(string warning, List<string> warnings)
    {
        _s.Log.Warn(Component, warning);
        warnings.Add(warning);
    }
}
