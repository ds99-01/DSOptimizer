using System;
using System.Collections.Generic;
using System.IO;
using DSOptimizer.Core.Localization;

namespace DSOptimizer.Core;

/// <summary>
/// Noty licencyjne komponentów stron trzecich, osadzone w <c>DSOptimizer.Core.dll</c> (program działa z pendrive'a bez plików obok exe).
/// Źródłem jest <c>THIRD_PARTY_NOTICES.md</c> z korzenia repozytorium.
/// </summary>
public static class ThirdPartyNotices
{
    private const string ResourceName = "DSOptimizer.Core.THIRD_PARTY_NOTICES.md";

    private const string EnglishResourceName = "DSOptimizer.Core.THIRD_PARTY_NOTICES_EN.md";

    private static readonly Lazy<string> Loaded = new(() => Load(ResourceName));

    private static readonly Lazy<string> LoadedEnglish = new(() => Load(EnglishResourceName));

    /// <summary>Pełny tekst not licencyjnych (Markdown, UTF-8), wersja polska.</summary>
    public static string Text => Loaded.Value;

    /// <summary>Pełny tekst not licencyjnych po angielsku (<c>THIRD_PARTY_NOTICES_EN.md</c>): własne zdania przetłumaczone, teksty licencji i adresy bez zmian.</summary>
    public static string EnglishText => LoadedEnglish.Value;

    /// <summary>Tekst not w bieżącym języku programu (<see cref="L.Current"/>): angielski dla interfejsu EN, inaczej polski.</summary>
    public static string Localized => L.Current == UiLanguage.English ? EnglishText : Text;

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

    private static string Load(string resourceName)
    {
        using var stream = typeof(ThirdPartyNotices).Assembly.GetManifestResourceStream(resourceName)
            ?? throw new InvalidOperationException("Brak osadzonego zasobu " + resourceName + " w " + typeof(ThirdPartyNotices).Assembly.GetName().Name + ".");
        using var reader = new StreamReader(stream, new System.Text.UTF8Encoding(false));
        return reader.ReadToEnd();
    }
}
