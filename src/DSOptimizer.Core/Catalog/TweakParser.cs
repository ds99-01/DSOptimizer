using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using DSOptimizer.Core.Localization;
using DSOptimizer.Core.Model;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace DSOptimizer.Core.Catalog;

/// <summary>Zamienia obiekt JSON tweaka na <see cref="TweakDefinition"/>. Błędy zawierają źródło i numer linii.</summary>
internal static class TweakParser
{
    private static readonly HashSet<string> TweakFields = new(StringComparer.Ordinal)
    {
        "id", "aliases", "category", "kind", "title", "summary", "benefit", "description", "risk", "scope", "requiresReboot", "suggested", "editions",
        "minBuild", "maxBuild", "timeoutSeconds", "reversible", "warnings", "conflicts", "registry", "services", "script", "launch", "handler",
    };

    private static readonly HashSet<string> RegistryFields = new(StringComparer.Ordinal)
    {
        "path", "name", "type", "value", "original", "detectOnly", "localized",
    };

    private static readonly HashSet<string> ServiceFields = new(StringComparer.Ordinal)
    {
        "name", "startType", "original", "stop",
    };

    private static readonly HashSet<string> ScriptFields = new(StringComparer.Ordinal)
    {
        "apply", "undo", "detect", "capture",
    };

    private static readonly HashSet<string> LaunchFields = new(StringComparer.Ordinal)
    {
        "file", "arguments",
    };

    public static TweakDefinition Parse(JObject obj, string source)
    {
        CheckFields(obj, TweakFields, source);
        return new TweakDefinition
        {
            Id = RequiredString(obj, "id", source),
            Aliases = StringList(obj, "aliases", source),
            Category = RequiredString(obj, "category", source),
            Kind = RequiredEnum<TweakKind>(obj, "kind", source),
            Title = RequiredString(obj, "title", source),
            Summary = OptionalString(obj, "summary", source) ?? string.Empty,
            Benefit = OptionalString(obj, "benefit", source),
            Description = OptionalString(obj, "description", source) ?? string.Empty,
            Risk = RequiredEnum<RiskLevel>(obj, "risk", source),
            Scope = RequiredEnum<TweakScope>(obj, "scope", source),
            RequiresReboot = OptionalBool(obj, "requiresReboot", source) ?? false,
            Suggested = OptionalBool(obj, "suggested", source) ?? false,
            Editions = EditionList(obj, "editions", source),
            MinBuild = OptionalInt(obj, "minBuild", source),
            MaxBuild = OptionalInt(obj, "maxBuild", source),
            Timeout = ParseTimeout(obj, source),
            Reversible = OptionalBool(obj, "reversible", source) ?? true,
            Warnings = StringList(obj, "warnings", source),
            Conflicts = StringList(obj, "conflicts", source),
            Registry = ObjectList(obj, "registry", source).Select(item => ParseRegistry(item, source)).ToList(),
            Services = ObjectList(obj, "services", source).Select(item => ParseService(item, source)).ToList(),
            Script = OptionalObject(obj, "script", source) is { } script ? ParseScript(script, source) : null,
            Launch = OptionalObject(obj, "launch", source) is { } launch ? ParseLaunch(launch, source) : null,
            Handler = OptionalString(obj, "handler", source),
        };
    }

    internal static CatalogLoadException Error(JToken at, string source, string message)
    {
        var info = (IJsonLineInfo)at;
        var line = info.HasLineInfo() ? info.LineNumber : 0;
        return new CatalogLoadException(L.F("{0} (linia {1}): {2}", source, line, message));
    }

    private static RegistryEntry ParseRegistry(JObject obj, string source)
    {
        CheckFields(obj, RegistryFields, source);
        var path = RequiredString(obj, "path", source);
        var name = RequiredString(obj, "name", source);
        var type = RequiredEnum<RegistryValueType>(obj, "type", source);
        var value = Field(obj, "value") ?? throw Error(obj, source, L.F("brak wymaganego pola '{0}'", "value"));
        var original = Field(obj, "original");
        return new RegistryEntry
        {
            Path = path,
            Name = name,
            Value = ToRegValue(type, value, path, name, source),
            Original = original is null ? null : ToRegValue(type, original, path, name, source),
            DetectOnly = OptionalBool(obj, "detectOnly", source) ?? false,
            Localized = OptionalBool(obj, "localized", source) ?? false,
        };
    }

    private static ServiceEntry ParseService(JObject obj, string source)
    {
        CheckFields(obj, ServiceFields, source);
        return new ServiceEntry
        {
            Name = RequiredString(obj, "name", source),
            StartType = RequiredEnum<ServiceStartType>(obj, "startType", source),
            Original = OptionalEnum<ServiceStartType>(obj, "original", source),
            Stop = OptionalBool(obj, "stop", source) ?? false,
        };
    }

    private static ScriptSet ParseScript(JObject obj, string source)
    {
        CheckFields(obj, ScriptFields, source);
        return new ScriptSet
        {
            Apply = ScriptText(obj, "apply", source),
            Undo = ScriptText(obj, "undo", source),
            Detect = ScriptText(obj, "detect", source),
            Capture = ScriptText(obj, "capture", source),
        };
    }

    private static LaunchSpec ParseLaunch(JObject obj, string source)
    {
        CheckFields(obj, LaunchFields, source);
        return new LaunchSpec
        {
            File = RequiredString(obj, "file", source),
            Arguments = OptionalString(obj, "arguments", source) ?? string.Empty,
        };
    }

    private static RegValue ToRegValue(RegistryValueType type, JToken token, string path, string name, string source)
    {
        try
        {
            return RegValueJson.FromToken(type, token);
        }
        catch (FormatException ex)
        {
            throw Error(token, source, L.F("wpis rejestru {0}\\{1}: {2}", path, name, ex.Message));
        }
    }

    private static void CheckFields(JObject obj, HashSet<string> known, string source)
    {
        foreach (var property in obj.Properties())
        {
            if (!known.Contains(property.Name))
            {
                throw Error(property, source, L.F("nieznane pole '{0}'", property.Name));
            }
        }
    }

    private static JToken? Field(JObject obj, string name)
        => obj.TryGetValue(name, StringComparison.Ordinal, out var token) && token is not null && token.Type != JTokenType.Null
            ? token
            : null;

    private static string RequiredString(JObject obj, string name, string source)
        => OptionalString(obj, name, source) ?? throw Error(obj, source, L.F("brak wymaganego pola '{0}'", name));

    private static string? OptionalString(JObject obj, string name, string source)
    {
        var token = Field(obj, name);
        if (token is null)
        {
            return null;
        }

        return token.Type == JTokenType.String ? (string)token! : throw Error(token, source, L.F("pole '{0}' musi być tekstem", name));
    }

    /// <summary>Dokładnie jedna nazwa z <see cref="Enum.GetNames(Type)"/> (bez wielkości liter); bez przecinków, przycinania spacji i liczb.</summary>
    private static bool TryParseExactEnum<T>(string text, out T value)
        where T : struct, Enum
    {
        foreach (var name in Enum.GetNames(typeof(T)))
        {
            if (string.Equals(name, text, StringComparison.OrdinalIgnoreCase))
            {
                value = (T)Enum.Parse(typeof(T), name);
                return true;
            }
        }

        value = default;
        return false;
    }

    private static T RequiredEnum<T>(JObject obj, string name, string source)
        where T : struct, Enum
        => OptionalEnum<T>(obj, name, source) ?? throw Error(obj, source, L.F("brak wymaganego pola '{0}'", name));

    private static T? OptionalEnum<T>(JObject obj, string name, string source)
        where T : struct, Enum
    {
        var text = OptionalString(obj, name, source);
        if (text is null)
        {
            return null;
        }

        if (TryParseExactEnum<T>(text, out var value))
        {
            return value;
        }

        throw Error(Field(obj, name)!, source, L.F("pole '{0}': nieznana wartość '{1}'", name, text));
    }

    private static bool? OptionalBool(JObject obj, string name, string source)
    {
        var token = Field(obj, name);
        if (token is null)
        {
            return null;
        }

        return token.Type == JTokenType.Boolean ? (bool)token : throw Error(token, source, L.F("pole '{0}' musi być true/false", name));
    }

    private static int? OptionalInt(JObject obj, string name, string source)
    {
        var token = Field(obj, name);
        if (token is null)
        {
            return null;
        }

        if (token.Type == JTokenType.Integer
            && int.TryParse(token.ToString(Formatting.None), NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out var value))
        {
            return value;
        }

        throw Error(token, source, L.F("pole '{0}' musi być liczbą całkowitą", name));
    }

    private static TimeSpan? ParseTimeout(JObject obj, string source)
    {
        if (!obj.TryGetValue("timeoutSeconds", StringComparison.Ordinal, out var token) || token is null)
        {
            return TweakDefinition.DefaultTimeout;
        }

        if (token.Type == JTokenType.Null)
        {
            return null;
        }

        return TimeSpan.FromSeconds(OptionalInt(obj, "timeoutSeconds", source)!.Value);
    }

    private static IReadOnlyList<string> StringList(JObject obj, string name, string source)
    {
        var token = Field(obj, name);
        if (token is null)
        {
            return Array.Empty<string>();
        }

        if (token is JArray array && array.All(item => item.Type == JTokenType.String))
        {
            return array.Select(item => (string)item!).ToList();
        }

        throw Error(token, source, L.F("pole '{0}' musi być tablicą tekstów", name));
    }

    private static List<EditionFamily>? EditionList(JObject obj, string name, string source)
    {
        var token = Field(obj, name);
        if (token is null)
        {
            return null;
        }

        if (token is not JArray array || !array.All(item => item.Type == JTokenType.String))
        {
            throw Error(token, source, L.F("pole '{0}' musi być tablicą tekstów", name));
        }

        var result = new List<EditionFamily>();
        foreach (var item in array)
        {
            var text = (string)item!;
            if (!TryParseExactEnum<EditionFamily>(text, out var family))
            {
                throw Error(item, source, L.F("pole '{0}': nieznana wartość '{1}'", name, text));
            }

            result.Add(family);
        }

        return result;
    }

    private static IReadOnlyList<JObject> ObjectList(JObject obj, string name, string source)
    {
        var token = Field(obj, name);
        if (token is null)
        {
            return Array.Empty<JObject>();
        }

        if (token is JArray array && array.All(item => item is JObject))
        {
            return array.Cast<JObject>().ToList();
        }

        throw Error(token, source, L.F("pole '{0}' musi być tablicą obiektów", name));
    }

    private static JObject? OptionalObject(JObject obj, string name, string source)
    {
        var token = Field(obj, name);
        return token switch
        {
            null => null,
            JObject value => value,
            _ => throw Error(token, source, L.F("pole '{0}' musi być obiektem", name)),
        };
    }

    private static string? ScriptText(JObject obj, string name, string source)
    {
        var token = Field(obj, name);
        return token switch
        {
            null => null,
            { Type: JTokenType.String } => (string)token!,
            JArray array when array.All(item => item.Type == JTokenType.String) => string.Join("\n", array.Select(item => (string)item!)),
            _ => throw Error(token, source, L.F("pole '{0}' musi być tekstem lub tablicą linii", name)),
        };
    }
}
