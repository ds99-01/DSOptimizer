using System;
using System.Collections.Generic;
using System.Text;
using DSOptimizer.Core.Localization;

namespace DSOptimizer.Core.Scripts;

/// <summary>
/// Owija treść tweaka: parametry z env (bez problemów z cytowaniem argumentów), błędy jako wyjątki,
/// wyjście UTF-8, trap → exit 1, propagacja kodu wyjścia ostatniego programu natywnego.
/// Zaraz po <c>param(...)</c> dokleja funkcję <c>Get-DsoText '&lt;polski format&gt;' [arg…]</c>: słownik tłumaczeń
/// (<c>Dictionary[string,string]</c> z porównaniem dokładnym – hashtable PowerShella nie rozróżnia wielkości liter) zawiera tylko
/// teksty z <see cref="ScriptTexts.Find"/>, które w bieżącym języku mają tłumaczenie inne niż polski oryginał, więc po polsku jest pusty.
/// Z argumentami tekst jest formatowany <c>[string]::Format</c> w kulturze <see cref="L.Culture"/>, bez argumentów zwracany bez zmian.
/// </summary>
public static class ScriptWrapper
{
    public const string UserSidVariable = "DSO_USER_SID";

    public const string SnapshotVariable = "DSO_SNAPSHOT";

    /// <summary>Znaki, które PowerShell uznaje za apostrof w napisie w apostrofach; w literale każdy trzeba podwoić.</summary>
    private const string SingleQuotes = "'‘’‚‛";

    public static string Wrap(string body)
    {
        if (body is null)
        {
            throw new ArgumentNullException(nameof(body));
        }

        var lines = new List<string>
        {
            "param([string]$UserSid = $env:DSO_USER_SID, [string]$Snapshot = $env:DSO_SNAPSHOT)",
            "$DsoTextCulture = [System.Globalization.CultureInfo]::GetCultureInfo(" + Quote(L.Culture.Name) + ")",
            "$DsoTexts = New-Object -TypeName 'System.Collections.Generic.Dictionary[string,string]' -ArgumentList ([System.StringComparer]::Ordinal)",
        };
        foreach (var polish in ScriptTexts.Find(body))
        {
            var translated = L.Data(polish);
            if (!string.Equals(translated, polish, StringComparison.Ordinal))
            {
                lines.Add("$DsoTexts[" + Quote(polish) + "] = " + Quote(translated));
            }
        }

        lines.AddRange(new[]
        {
            "function Get-DsoText([string]$Text) {",
            "    $Translated = $null",
            "    if ($script:DsoTexts.TryGetValue($Text, [ref]$Translated)) { $Text = $Translated }",
            "    if ($args.Count -eq 0) { return $Text }",
            "    return [string]::Format($script:DsoTextCulture, $Text, [object[]]$args)",
            "}",
            "$ErrorActionPreference = 'Stop'",
            "$ProgressPreference = 'SilentlyContinue'",
            "[Console]::OutputEncoding = [System.Text.Encoding]::UTF8",
            "$OutputEncoding = [System.Text.Encoding]::UTF8",
            "trap { [Console]::Error.WriteLine($_.ToString()); exit 1 }",
            "# --- treść tweaka ---",
            body,
            "# --- koniec treści ---",
            "if ($LASTEXITCODE -and $LASTEXITCODE -ne 0) { exit $LASTEXITCODE }",
            "exit 0",
        });
        return string.Join("\r\n", lines);
    }

    /// <summary>Literał PowerShella w apostrofach: każdy znak apostrofu podwojony (podwojony daje w napisie ten sam znak).</summary>
    private static string Quote(string text)
    {
        var builder = new StringBuilder(text.Length + 2).Append('\'');
        foreach (var c in text)
        {
            builder.Append(c);
            if (SingleQuotes.IndexOf(c) >= 0)
            {
                builder.Append(c);
            }
        }

        return builder.Append('\'').ToString();
    }
}
