using System;
using DSOptimizer.Core.Model;

namespace DSOptimizer.Core.Operations;

/// <summary>Czy bieżący stan (actual) odpowiada zapisanemu (expected) — weryfikacja po undo.</summary>
public static class SnapshotComparer
{
    public static bool SameState(SnapshotRecord expected, SnapshotRecord actual)
    {
        if (expected is null)
        {
            throw new ArgumentNullException(nameof(expected));
        }

        if (actual is null)
        {
            throw new ArgumentNullException(nameof(actual));
        }

        if (expected.Op != actual.Op)
        {
            return false;
        }

        return expected.Op switch
        {
            OperationKind.Registry =>
                string.Equals(expected.Path, actual.Path, StringComparison.OrdinalIgnoreCase)
                && string.Equals(expected.Name, actual.Name, StringComparison.OrdinalIgnoreCase)
                && (expected.Value ?? RegValue.Absent).Equals(actual.Value ?? RegValue.Absent),
            OperationKind.Service => !expected.ServiceExisted || (actual.ServiceExisted && expected.StartType == actual.StartType),
            _ => expected.Capture is null || string.Equals(expected.Capture, actual.Capture, StringComparison.Ordinal),
        };
    }
}
