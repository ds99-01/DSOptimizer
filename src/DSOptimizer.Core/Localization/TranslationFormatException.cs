using System;
using System.Collections.Generic;
using System.Linq;

namespace DSOptimizer.Core.Localization;

/// <summary>Błędy w plikach tłumaczeń; wszystkie wykryte błędy są w jednym wyjątku, każdy z nazwą pliku.</summary>
public sealed class TranslationFormatException : Exception
{
    public TranslationFormatException(IReadOnlyList<string> errors)
        : base(BuildMessage(errors))
    {
        Errors = errors;
    }

    public IReadOnlyList<string> Errors { get; }

    private static string BuildMessage(IReadOnlyList<string> errors)
    {
        if (errors is null)
        {
            throw new ArgumentNullException(nameof(errors));
        }

        return "Błędy w plikach tłumaczeń (" + errors.Count + "):" + Environment.NewLine + string.Join(Environment.NewLine, errors.Select(e => "- " + e));
    }
}
