using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using DSOptimizer.Core.Journal;
using DSOptimizer.Core.Localization;
using DSOptimizer.Core.Model;
using DSOptimizer.Core.Operations;

namespace DSOptimizer.Core.Engine;

/// <summary>Silnik tweaków. Część wspólna: Detect, Preview i helpery dla pozostałych plików partial.</summary>
public sealed partial class TweakEngine
{
    private const string Component = "Engine";

    private readonly EngineServices _s;
    private readonly OperationFactory _factory;
    private readonly string _catalogHash;

    public TweakEngine(EngineServices services, string catalogHash)
    {
        _s = services ?? throw new ArgumentNullException(nameof(services));
        _catalogHash = catalogHash ?? throw new ArgumentNullException(nameof(catalogHash));
        _factory = new OperationFactory(services.Registry, services.Services, services.Scripts, services.User);
    }

    public DetectionState Detect(TweakDefinition tweak, CancellationToken cancellationToken = default)
    {
        if (tweak is null)
        {
            throw new ArgumentNullException(nameof(tweak));
        }

        if (tweak.Kind != TweakKind.Tweak)
        {
            throw new ArgumentException("Detect dotyczy tylko pozycji kind=tweak.", nameof(tweak));
        }

        var state = DetectSafely(tweak, _factory.Create(tweak), cancellationToken);
        PromotePendingReboot(tweak, state);
        return state;
    }

    public IReadOnlyList<PlannedChange> Preview(TweakDefinition tweak, CancellationToken cancellationToken = default)
    {
        if (tweak is null)
        {
            throw new ArgumentNullException(nameof(tweak));
        }

        return _factory.Create(tweak)
            .Where(operation => !operation.IsDetectOnly)
            .Select(operation => operation.Plan(cancellationToken))
            .Select(change => new PlannedChange
            {
                Description = change.Description,
                Current = change.Current,
                Target = change.Target,
                RequiresReboot = tweak.RequiresReboot,
            })
            .ToList();
    }

    private DetectionState DetectSafely(TweakDefinition tweak, IReadOnlyList<IOperation> operations, CancellationToken cancellationToken)
    {
        if (!IsSupported(tweak))
        {
            return DetectionState.NotSupported;
        }

        try
        {
            var states = operations
                .Where(operation => operation.ParticipatesInDetection)
                .Select(operation => operation.Detect(cancellationToken))
                .ToList();
            return StateAggregator.Aggregate(states);
        }
        catch (Exception ex)
        {
            _s.Log.Fail(Component, L.F("Detekcja {0} przerwana nieoczekiwanym błędem.", tweak.Id), ex);
            return DetectionState.Unknown;
        }
    }

    private bool IsSupported(TweakDefinition tweak)
    {
        var build = _s.SystemInfo.Build;
        if ((tweak.MinBuild is not null && build < tweak.MinBuild) || (tweak.MaxBuild is not null && build > tweak.MaxBuild))
        {
            return false;
        }

        // Nieznana edycja (null) nie blokuje: nie odmawiamy na podstawie braku wiedzy.
        return tweak.Editions is null
            || EditionFamilies.FromEditionId(_s.SystemInfo.Edition) is not { } family
            || tweak.Editions.Contains(family);
    }

    private string? ScopedUserSid(TweakDefinition tweak)
        => tweak.Scope == TweakScope.User ? _s.User.Sid : null;

    private void PromotePendingReboot(TweakDefinition tweak, DetectionState state)
    {
        if (state != DetectionState.Applied)
        {
            return;
        }

        try
        {
            var load = _s.Journal.Load();
            if (load.Warning is not null)
            {
                _s.Log.Warn(Component, load.Warning);
            }

            var document = load.Document;
            var entry = document.FindActive(tweak.Id, ScopedUserSid(tweak));
            if (entry is null || entry.Status != JournalEntryStatus.PendingReboot)
            {
                return;
            }

            entry.Status = JournalEntryStatus.Active;
            AddEvent(entry, "detect", "Applied", L.T("Zmiana aktywna po restarcie."));
            _s.Journal.Save(document);
        }
        catch (JournalStoreException ex)
        {
            _s.Log.Warn(Component, L.F("Nie udało się zaktualizować journala dla {0}: {1}", tweak.Id, ex.Message));
        }
    }

    private void AddEvent(JournalEntry entry, string action, string outcome, string? message)
        => entry.Events.Add(new JournalEvent { At = _s.Clock.UtcNow, Action = action, Outcome = outcome, Message = message });
}

