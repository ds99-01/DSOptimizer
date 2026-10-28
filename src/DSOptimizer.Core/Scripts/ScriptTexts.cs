using System;
using System.Collections.Generic;
using System.Text.RegularExpressions;

namespace DSOptimizer.Core.Scripts;

/// <summary>
/// Komunikaty skryptów katalogu do tłumaczenia: pierwszy argument wywołania <c>Get-DsoText '…'</c> (literał w apostrofach,
/// podwojony apostrof = apostrof). Jedno miejsce wzorca dla <see cref="ScriptWrapper"/> i testów; raport braków
/// (<c>tools/i18n/Get-MissingTranslations.ps1</c>) używa tego samego wyrażenia.
/// </summary>
public static class ScriptTexts
{
    private static readonly Regex TextCall = new(@"Get-DsoText\s+'((?:[^']|'')*)'", RegexOptions.CultureInvariant | RegexOptions.Compiled);

    /// <summary>Polskie teksty z wywołań <c>Get-DsoText</c> w kolejności wystąpienia, bez powtórzeń (porównanie dokładne).</summary>
    public static IReadOnlyList<string> Find(string script)
    {
        if (script is null)
        {
            throw new ArgumentNullException(nameof(script));
        }

        var result = new List<string>();
        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (Match match in TextCall.Matches(script))
        {
            var text = match.Groups[1].Value.Replace("''", "'");
            if (seen.Add(text))
            {
                result.Add(text);
            }
        }

        return result;
    }
}
