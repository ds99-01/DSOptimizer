using System;
using DSOptimizer.Core.Localization;
using Newtonsoft.Json.Linq;

namespace DSOptimizer.Core.Apps;

/// <summary>
/// Wspólny odczyt reguły <c>match</c> i pól tekstowych dla <c>apps-bloat.json</c> i <c>apps.json</c>: jeden kod dopasowania
/// dla obu list. <c>fail(id, pole, treść)</c> tworzy wyjątek właściwy dla pliku (komunikat z prefiksem pliku).
/// </summary>
internal static class AppMatchJson
{
    /// <summary>Reguła z pola <c>match</c>: <c>packageName</c> (AppX) albo <c>displayName</c>/<c>publisher</c> (Win32), nigdy oba rodzaje.</summary>
    internal static AppMatch Parse(JObject obj, string id, Func<string, string, string, Exception> fail)
    {
        if (obj["match"] is not JObject match)
        {
            throw fail(id, "match", L.T("wymagany obiekt z regułą dopasowania"));
        }

        var package = Text(match, "packageName", id, fail);
        var display = Text(match, "displayName", id, fail);
        var publisher = Text(match, "publisher", id, fail);
        if (package is null && display is null && publisher is null)
        {
            throw fail(id, "match", L.T("podaj packageName (AppX) albo displayName/publisher (Win32)"));
        }

        if (package is not null && (display is not null || publisher is not null))
        {
            throw fail(id, "match", L.T("packageName (AppX) nie łączy się z displayName/publisher (Win32)"));
        }

        return new AppMatch(package, display, publisher);
    }

    /// <summary>Tekst pola po <c>Trim</c>; brak, <c>null</c> albo same białe znaki = <c>null</c>, inny typ niż tekst = błąd.</summary>
    internal static string? Text(JObject obj, string field, string id, Func<string, string, string, Exception> fail)
    {
        var value = obj[field];
        if (value is null || value.Type == JTokenType.Null)
        {
            return null;
        }

        if (value.Type != JTokenType.String)
        {
            throw fail(id, field, L.T("wartość musi być tekstem"));
        }

        var text = value.Value<string>();
        return string.IsNullOrWhiteSpace(text) ? null : text!.Trim();
    }
}
