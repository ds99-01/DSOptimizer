using System;
using System.Collections.Generic;
using System.IO;

namespace DSOptimizer.Core;

/// <summary>
/// Noty licencyjne komponentów stron trzecich, osadzone w <c>DSOptimizer.Core.dll</c> (program działa z pendrive'a bez plików obok exe).
/// Źródłem jest <c>THIRD_PARTY_NOTICES.md</c> z korzenia repozytorium.
/// </summary>
public static class ThirdPartyNotices
{
    private const string ResourceName = "DSOptimizer.Core.THIRD_PARTY_NOTICES.md";

    private static readonly Lazy<string> Loaded = new(Load);

    /// <summary>Pełny tekst not licencyjnych (Markdown, UTF-8).</summary>
    public static string Text => Loaded.Value;

    /// <summary>
    /// Zamienia Markdown tego pliku na czytelny tekst: nagłówki bez <c>#</c>, bez linii płotków, bez <c>**</c> i backticków, punktory „•”.
    /// Treść wewnątrz płotków (pełne teksty licencji) zostaje bez zmian; linki zostają jako tekst.
    /// </summary>
    public static string ToPlainText(string markdown)
    {
        var output = new List<string>();
        var inFence = false;
        foreach (var line in (markdown ?? string.Empty).Replace("\r\n", "\n").Split('\n'))
        {
            if (line.TrimStart().StartsWith("```", StringComparison.Ordinal))
            {
                inFence = !inFence;
                continue;
            }

            if (inFence)
            {
                output.Add(line);
            }
            else if (line.StartsWith("## ", StringComparison.Ordinal))
            {
                if (output.Count > 0 && output[output.Count - 1].Length > 0)
                {
                    output.Add(string.Empty);
                }

                output.Add(StripInline(line.Substring(3)));
            }
            else if (line.StartsWith("# ", StringComparison.Ordinal))
            {
                output.Add(StripInline(line.Substring(2)));
            }
            else if (line.StartsWith("- ", StringComparison.Ordinal))
            {
                output.Add("• " + StripInline(line.Substring(2)));
            }
            else
            {
                output.Add(StripInline(line));
            }
        }

        return string.Join("\n", output);
    }

    private static string StripInline(string text) => text.Replace("**", string.Empty).Replace("`", string.Empty);

    private static string Load()
    {
        using var stream = typeof(ThirdPartyNotices).Assembly.GetManifestResourceStream(ResourceName)
            ?? throw new InvalidOperationException("Brak osadzonego zasobu " + ResourceName + " w " + typeof(ThirdPartyNotices).Assembly.GetName().Name + ".");
        using var reader = new StreamReader(stream, new System.Text.UTF8Encoding(false));
        return reader.ReadToEnd();
    }
}
