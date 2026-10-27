using System;
using DSOptimizer.Core.Model;

namespace DSOptimizer.Core.Abstractions;

public sealed class ScriptRequest
{
    /// <summary>Treść PowerShell tweaka (bez param()) — wrapper dodaje $UserSid i $Snapshot.</summary>
    public required string Body { get; init; }

    public string UserSid { get; init; } = string.Empty;

    public string Snapshot { get; init; } = string.Empty;

    /// <summary>null = bez limitu.</summary>
    public TimeSpan? Timeout { get; init; } = TweakDefinition.DefaultTimeout;
}
