using System;
using DSOptimizer.Core.Abstractions;
using DSOptimizer.Core.Engine;
using DSOptimizer.Core.Localization;

namespace DSOptimizer.Core.Repair;

/// <summary>Strażnik drugiej naprawy w trakcie: handlery długich napraw wołają go przed pierwszym krokiem.</summary>
public static class ServicingGuard
{
    public static string RefusalMessage => L.T("Poprzednia operacja serwisowania jeszcze trwa – spróbuj za chwilę.");

    /// <summary><c>null</c> = można startować; inaczej <see cref="TweakOutcome.Refused"/> z nazwą procesu w <c>Warnings</c>.</summary>
    public static ActionHandlerResult? Check(IServicingActivity activity)
    {
        if (activity is null)
        {
            throw new ArgumentNullException(nameof(activity));
        }

        var active = activity.ActiveOperation();
        if (active is null)
        {
            return null;
        }

        return new ActionHandlerResult
        {
            Outcome = TweakOutcome.Refused,
            Message = RefusalMessage,
            Warnings = new[] { L.F("Działa jeszcze proces: {0}.", active) },
        };
    }
}
