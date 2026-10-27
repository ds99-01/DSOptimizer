using System;
using System.Globalization;
using Newtonsoft.Json;
using Newtonsoft.Json.Converters;
using Newtonsoft.Json.Serialization;

namespace DSOptimizer.Core.Journal;

public static class JournalSerializer
{
    // DateParseHandling.None: stringi wyglądające jak daty (REG_SZ, capture skryptu) muszą zostać stringami;
    // typowane DateTimeOffset (appliedAt, at) i tak są parsowane przy bindowaniu do właściwości.
    private static readonly JsonSerializerSettings Settings = new()
    {
        ContractResolver = new CamelCasePropertyNamesContractResolver(),
        Converters = { new StringEnumConverter(), new RegValueConverter() },
        NullValueHandling = NullValueHandling.Ignore,
        Formatting = Formatting.Indented,
        DateParseHandling = DateParseHandling.None,
        Culture = CultureInfo.InvariantCulture,
    };

    public static string Serialize(JournalDocument document)
    {
        if (document is null)
        {
            throw new ArgumentNullException(nameof(document));
        }

        return JsonConvert.SerializeObject(document, Settings);
    }

    public static JournalDocument Deserialize(string json)
    {
        if (json is null)
        {
            throw new ArgumentNullException(nameof(json));
        }

        JournalDocument? document;
        try
        {
            document = JsonConvert.DeserializeObject<JournalDocument>(json, Settings);
        }
        catch (JsonException ex)
        {
            throw new JournalFormatException($"Uszkodzony journal: {ex.Message}", ex);
        }

        if (document is null)
        {
            throw new JournalFormatException("Pusty dokument journala.");
        }

        if (document.SchemaVersion != JournalDocument.CurrentSchemaVersion)
        {
            throw new JournalFormatException($"Nieobsługiwana wersja journala: {document.SchemaVersion}.");
        }

        return document;
    }
}
