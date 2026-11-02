using System;

namespace DSOptimizer.Core.Model;

/// <summary>
/// Dopasowanie wzorca z <c>*</c> (bez regex, bez rozróżniania wielkości liter): kolejne fragmenty wzorca muszą wystąpić w tekście w tej
/// kolejności, pierwszy na początku, ostatni na końcu; <c>*</c> obejmuje dowolny ciąg, także z <c>\</c>. Wzorzec bez gwiazdki = równość.
/// </summary>
public static class WildcardText
{
    public static bool Matches(string pattern, string text)
    {
        if (pattern is null)
        {
            throw new ArgumentNullException(nameof(pattern));
        }

        if (text is null)
        {
            throw new ArgumentNullException(nameof(text));
        }

        var parts = pattern.Split('*');
        var position = 0;
        for (var i = 0; i < parts.Length; i++)
        {
            var part = parts[i];
            if (i == 0)
            {
                if (!text.StartsWith(part, StringComparison.OrdinalIgnoreCase))
                {
                    return false;
                }

                position = part.Length;
                continue;
            }

            if (i == parts.Length - 1)
            {
                return text.Length - position >= part.Length && text.EndsWith(part, StringComparison.OrdinalIgnoreCase);
            }

            var found = text.IndexOf(part, position, StringComparison.OrdinalIgnoreCase);
            if (found < 0)
            {
                return false;
            }

            position = found + part.Length;
        }

        return position == text.Length;
    }
}
