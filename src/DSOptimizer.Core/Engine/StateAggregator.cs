using System;
using System.Collections.Generic;
using System.Linq;
using DSOptimizer.Core.Model;
using DSOptimizer.Core.Operations;

namespace DSOptimizer.Core.Engine;

/// <summary>Składa stany operacji w stan tweaka (spec, sekcja 5).</summary>
public static class StateAggregator
{
    public static DetectionState Aggregate(IReadOnlyList<OperationState> states)
    {
        if (states is null)
        {
            throw new ArgumentNullException(nameof(states));
        }

        if (states.Contains(OperationState.NotSupported))
        {
            return DetectionState.NotSupported;
        }

        if (states.Contains(OperationState.Unknown))
        {
            return DetectionState.Unknown;
        }

        var considered = states.Where(state => state != OperationState.NotApplicable).ToList();
        if (considered.Count == 0)
        {
            return DetectionState.NotSupported;
        }

        var matches = considered.Count(state => state == OperationState.Match);
        if (matches == considered.Count)
        {
            return DetectionState.Applied;
        }

        return matches == 0 ? DetectionState.NotApplied : DetectionState.Partial;
    }
}
