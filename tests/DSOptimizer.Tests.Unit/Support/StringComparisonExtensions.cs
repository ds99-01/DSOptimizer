using System;

namespace DSOptimizer.Tests.Unit.Support;

/// <summary>net481 nie ma string.Contains(string, StringComparison).</summary>
public static class StringComparisonExtensions
{
    public static bool Contains(this string text, string value, StringComparison comparison)
        => text.IndexOf(value, comparison) >= 0;
}
