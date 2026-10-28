using System;
using System.Globalization;
using DSOptimizer.Core.Localization;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace DSOptimizer.Core.Settings;

public static class AppSettingsSerializer
{
    public static string Serialize(AppSettings settings)
    {
        if (settings is null)
        {
            throw new ArgumentNullException(nameof(settings));
        }

        var document = new JObject
        {
            ["schemaVersion"] = AppSettings.SchemaVersion,
            ["createRestorePoint"] = settings.CreateRestorePoint,
            ["theme"] = ThemeToText(settings.Theme),
        };
        if (settings.Language is { } language)
        {
            document["language"] = UiLanguageCodes.ToCode(language);
        }

        return document.ToString(Formatting.None);
    }

    public static AppSettings Deserialize(string json)
    {
        if (json is null)
        {
            throw new ArgumentNullException(nameof(json));
        }

        if (string.IsNullOrWhiteSpace(json))
        {
            throw new SettingsFormatException(L.T("Pusty plik ustawień."));
        }

        // Surowy napis z BOM jest odrzucany tutaj; plik z BOM (np. z Notatnika) i tak się wczyta, bo File.ReadAllText
        // w JsonFileSettingsStore wykrywa BOM i usuwa go, zanim napis trafi do serializera.
        if (json[0] == '﻿')
        {
            throw new SettingsFormatException(L.T("Plik ustawień zaczyna się od BOM (oczekiwano UTF-8 bez BOM)."));
        }

        JToken root;
        try
        {
            using var reader = new JsonTextReader(new System.IO.StringReader(json))
            {
                DateParseHandling = DateParseHandling.None,
                Culture = CultureInfo.InvariantCulture,
            };
            root = JToken.ReadFrom(reader);
            if (reader.Read())
            {
                throw new SettingsFormatException(L.T("Plik ustawień zawiera dane po zamknięciu dokumentu JSON."));
            }
        }
        catch (JsonException ex)
        {
            throw new SettingsFormatException(L.F("Uszkodzony plik ustawień: {0}", ex.Message), ex);
        }

        if (root is not JObject document)
        {
            throw new SettingsFormatException(L.T("Plik ustawień musi być obiektem JSON."));
        }

        CheckSchemaVersion(document["schemaVersion"]);

        return new AppSettings
        {
            CreateRestorePoint = ReadBoolean(document["createRestorePoint"], AppSettings.Default.CreateRestorePoint),
            Theme = ReadTheme(document["theme"], AppSettings.Default.Theme),
            Language = ReadLanguage(document["language"]),
        };
    }

    private static void CheckSchemaVersion(JToken? token)
    {
        if (token is null)
        {
            throw new SettingsFormatException(L.T("Brak pola schemaVersion w pliku ustawień."));
        }

        if (token.Type != JTokenType.Integer)
        {
            throw new SettingsFormatException(L.T("Pole schemaVersion musi być liczbą całkowitą."));
        }

        // Newtonsoft zwraca long tylko dla liczb mieszczących się w zakresie; większe to BigInteger.
        if (token is not JValue { Value: long version })
        {
            throw new SettingsFormatException(L.T("Pole schemaVersion jest poza dozwolonym zakresem liczb."));
        }

        if (version > AppSettings.SchemaVersion)
        {
            throw new SettingsFormatException(L.F("Plik ustawień ma nowszą wersję formatu (schemaVersion {0} – nowsza wersja programu); ta wersja obsługuje {1}.", version, AppSettings.SchemaVersion));
        }

        if (version != AppSettings.SchemaVersion)
        {
            throw new SettingsFormatException(L.F("Nieobsługiwana wersja pliku ustawień: {0}.", version));
        }
    }

    private static bool ReadBoolean(JToken? token, bool fallback)
    {
        if (token is null)
        {
            return fallback;
        }

        if (token.Type != JTokenType.Boolean)
        {
            throw new SettingsFormatException(L.T("Pole createRestorePoint musi być wartością true lub false."));
        }

        return token.Value<bool>();
    }

    private static ThemePreference ReadTheme(JToken? token, ThemePreference fallback)
    {
        if (token is null)
        {
            return fallback;
        }

        if (token.Type != JTokenType.String)
        {
            throw new SettingsFormatException(L.T("Pole theme musi być tekstem: system, light albo dark."));
        }

        return (token.Value<string>() ?? string.Empty).ToLowerInvariant() switch
        {
            "system" => ThemePreference.System,
            "light" => ThemePreference.Light,
            "dark" => ThemePreference.Dark,
            _ => throw new SettingsFormatException(L.T("Nieznana wartość pola theme (dozwolone: system, light, dark).")),
        };
    }

    private static UiLanguage? ReadLanguage(JToken? token)
    {
        if (token is null || token.Type == JTokenType.Null)
        {
            return null;
        }

        if (token.Type != JTokenType.String)
        {
            throw new SettingsFormatException(L.T("Pole language musi być tekstem: pl albo en."));
        }

        return UiLanguageCodes.TryParse(token.Value<string>(), out var language) ? language : null;
    }

    private static string ThemeToText(ThemePreference theme) => theme switch
    {
        ThemePreference.System => "system",
        ThemePreference.Light => "light",
        ThemePreference.Dark => "dark",
        _ => throw new ArgumentOutOfRangeException(nameof(theme), theme, "Nieznany motyw."),
    };
}
