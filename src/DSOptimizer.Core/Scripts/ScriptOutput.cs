using System;
using System.Collections.Generic;

namespace DSOptimizer.Core.Scripts;

public static class ScriptOutput
{
    /// <summary>Ostatnia niepusta linia wyjścia (przycięta) albo null.</summary>
    public static string? LastNonEmptyLine(IReadOnlyList<string> lines)
    {
        if (lines is null)
        {
            throw new ArgumentNullException(nameof(lines));
        }

        for (var i = lines.Count - 1; i >= 0; i--)
        {
            var trimmed = lines[i]?.Trim();
            if (!string.IsNullOrEmpty(trimmed))
            {
                return trimmed;
            }
        }

        return null;
    }
}
