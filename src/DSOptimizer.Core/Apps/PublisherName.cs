using System;
using System.Collections.Generic;
using System.Text;

namespace DSOptimizer.Core.Apps;

/// <summary>
/// Czytelna nazwa wydawcy. Pakiety AppX podają wydawcę jako nazwę wyróżniającą certyfikatu (<c>CN=…, O=…, L=…</c>); na liście i
/// w panelu pokazujemy wartość <c>O=</c>, a gdy jej brak – <c>CN=</c>. Tekst, który nie jest nazwą wyróżniającą (np. „McAfee, LLC”
/// z wpisu Uninstall), zostaje bez zmian.
/// </summary>
public static class PublisherName
{
    /// <summary>Wartość <c>O=</c>, potem <c>CN=</c>; inny tekst bez zmian (przycięty); pusty = null.</summary>
    public static string? Readable(string? publisher)
    {
        if (string.IsNullOrWhiteSpace(publisher))
        {
            return null;
        }

        var text = publisher!.Trim();
        var parts = Split(text);
        if (parts is null)
        {
            return text;
        }

        return Find(parts, "O") ?? Find(parts, "CN") ?? text;
    }

    private static string? Find(List<KeyValuePair<string, string>> parts, string key)
    {
        foreach (var part in parts)
        {
            if (string.Equals(part.Key, key, StringComparison.OrdinalIgnoreCase) && part.Value.Length > 0)
            {
                return part.Value;
            }
        }

        return null;
    }

    /// <summary>Składniki <c>KLUCZ=wartość</c> rozdzielone przecinkami poza cudzysłowem; null, gdy któryś nie ma postaci <c>KLUCZ=</c>.</summary>
    private static List<KeyValuePair<string, string>>? Split(string text)
    {
        var parts = new List<KeyValuePair<string, string>>();
        var index = 0;
        while (index <= text.Length)
        {
            var equals = text.IndexOf('=', index);
            var comma = text.IndexOf(',', index);
            if (equals < 0 || (comma >= 0 && comma < equals))
            {
                return null;
            }

            var key = text.Substring(index, equals - index).Trim();
            if (key.Length == 0)
            {
                return null;
            }

            var (value, next) = ReadValue(text, equals + 1);
            parts.Add(new KeyValuePair<string, string>(key, value));
            if (next >= text.Length)
            {
                break;
            }

            index = next + 1;
        }

        return parts;
    }

    /// <summary>Wartość od <paramref name="start"/> do przecinka poza cudzysłowem; w cudzysłowie <c>""</c> = jeden cudzysłów.</summary>
    private static (string Value, int End) ReadValue(string text, int start)
    {
        var position = start;
        while (position < text.Length && text[position] == ' ')
        {
            position++;
        }

        if (position < text.Length && text[position] == '"')
        {
            var builder = new StringBuilder();
            position++;
            while (position < text.Length)
            {
                if (text[position] == '"')
                {
                    if (position + 1 < text.Length && text[position + 1] == '"')
                    {
                        builder.Append('"');
                        position += 2;
                        continue;
                    }

                    position++;
                    break;
                }

                builder.Append(text[position]);
                position++;
            }

            var comma = text.IndexOf(',', position);
            return (builder.ToString(), comma < 0 ? text.Length : comma);
        }

        var end = text.IndexOf(',', position);
        end = end < 0 ? text.Length : end;
        return (text.Substring(position, end - position).Trim(), end);
    }
}
