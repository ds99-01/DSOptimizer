using System;

namespace DSOptimizer.Core.Scripts;

/// <summary>
/// Owija treść tweaka: parametry z env (bez problemów z cytowaniem argumentów), błędy jako wyjątki,
/// wyjście UTF-8, trap → exit 1, propagacja kodu wyjścia ostatniego programu natywnego.
/// </summary>
public static class ScriptWrapper
{
    public const string UserSidVariable = "DSO_USER_SID";

    public const string SnapshotVariable = "DSO_SNAPSHOT";

    public static string Wrap(string body)
    {
        if (body is null)
        {
            throw new ArgumentNullException(nameof(body));
        }

        return string.Join(
            "\r\n",
            "param([string]$UserSid = $env:DSO_USER_SID, [string]$Snapshot = $env:DSO_SNAPSHOT)",
            "$ErrorActionPreference = 'Stop'",
            "$ProgressPreference = 'SilentlyContinue'",
            "[Console]::OutputEncoding = [System.Text.Encoding]::UTF8",
            "$OutputEncoding = [System.Text.Encoding]::UTF8",
            "trap { [Console]::Error.WriteLine($_.ToString()); exit 1 }",
            "# --- treść tweaka ---",
            body,
            "# --- koniec treści ---",
            "if ($LASTEXITCODE -and $LASTEXITCODE -ne 0) { exit $LASTEXITCODE }",
            "exit 0");
    }
}
