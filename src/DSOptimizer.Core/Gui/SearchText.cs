using System.Globalization;
using System.Text;

namespace DSOptimizer.Core.Gui;

/// <summary>Normalizacja tekstu do wyszukiwania: małe litery, bez polskich znaków, zwinięte spacje.</summary>
public static class SearchText
{
    public static string Fold(string? text)
    {
        if (string.IsNullOrEmpty(text))
        {
            return string.Empty;
        }

        var decomposed = text!.ToLowerInvariant().Normalize(NormalizationForm.FormD);
        var builder = new StringBuilder(decomposed.Length);
        var pendingSpace = false;
        foreach (var c in decomposed)
        {
            if (CharUnicodeInfo.GetUnicodeCategory(c) == UnicodeCategory.NonSpacingMark)
            {
                continue;
            }

            if (char.IsWhiteSpace(c))
            {
                pendingSpace = builder.Length > 0;
                continue;
            }

            if (pendingSpace)
            {
                builder.Append(' ');
                pendingSpace = false;
            }

            // "ł" nie rozkłada się w NFD, więc zamieniamy go ręcznie.
            builder.Append(c == 'ł' ? 'l' : c);
        }

        return builder.ToString();
    }
}
