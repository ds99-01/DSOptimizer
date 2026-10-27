using System;
using DSOptimizer.Core.Model;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace DSOptimizer.Core.Journal;

/// <summary>RegValue w journalu jako { "type": "...", "value": ... } — wartość w formacie katalogu.</summary>
internal sealed class RegValueConverter : JsonConverter<RegValue>
{
    public override void WriteJson(JsonWriter writer, RegValue? value, JsonSerializer serializer)
    {
        if (value is null)
        {
            writer.WriteNull();
            return;
        }

        new JObject
        {
            ["type"] = value.Type.ToString(),
            ["value"] = RegValueJson.ToToken(value),
        }.WriteTo(writer);
    }

    public override RegValue? ReadJson(JsonReader reader, Type objectType, RegValue? existingValue, bool hasExistingValue, JsonSerializer serializer)
    {
        if (reader.TokenType == JsonToken.Null)
        {
            return null;
        }

        var obj = JObject.Load(reader);
        var typeText = (string?)obj["type"] ?? throw new JsonSerializationException("Brak pola 'type' wartości rejestru.");
        if (!Enum.TryParse<RegistryValueType>(typeText, ignoreCase: false, out var type))
        {
            throw new JsonSerializationException($"Nieznany typ wartości rejestru: {typeText}");
        }

        var token = obj["value"] ?? throw new JsonSerializationException("Brak pola 'value' wartości rejestru.");
        try
        {
            return RegValueJson.FromToken(type, token);
        }
        catch (FormatException ex)
        {
            throw new JsonSerializationException(ex.Message, ex);
        }
    }
}
