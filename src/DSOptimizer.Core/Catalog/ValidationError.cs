using System;

namespace DSOptimizer.Core.Catalog;

public sealed class ValidationError
{
    public ValidationError(string tweakId, string rule, string message)
    {
        TweakId = tweakId ?? throw new ArgumentNullException(nameof(tweakId));
        Rule = rule ?? throw new ArgumentNullException(nameof(rule));
        Message = message ?? throw new ArgumentNullException(nameof(message));
    }

    public string TweakId { get; }

    public string Rule { get; }

    public string Message { get; }

    public override string ToString() => $"{TweakId} [{Rule}]: {Message}";
}
