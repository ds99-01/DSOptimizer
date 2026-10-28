using System;
using System.Globalization;
using System.Linq;
using DSOptimizer.Core.Localization;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace DSOptimizer.Core.Model;

/// <summary>Konwersja <see cref="RegValue"/> ↔ JSON, wspólna dla katalogu i journala.</summary>
public static class RegValueJson
{
    /// <summary>Marker „wartość ma nie istnieć” (konwencja z WinUtil).</summary>
    public const string RemoveEntryMarker = "<RemoveEntry>";

    public static RegValue FromToken(RegistryValueType type, JToken token)
    {
        if (token is null)
        {
            throw new ArgumentNullException(nameof(token));
        }

        if (token.Type == JTokenType.String && string.Equals((string?)token, RemoveEntryMarker, StringComparison.Ordinal))
        {
            return RegValue.Absent;
        }

        return type switch
        {
            RegistryValueType.String => RegValue.FromString(RequireString(type, token)),
            RegistryValueType.ExpandString => RegValue.FromExpandString(RequireString(type, token)),
            RegistryValueType.DWord => RegValue.FromDWord((uint)RequireInteger(type, token, uint.MaxValue)),
            RegistryValueType.QWord => RegValue.FromQWord(RequireInteger(type, token, ulong.MaxValue)),
            RegistryValueType.MultiString => RegValue.FromMultiString(RequireStringArray(type, token)),
            RegistryValueType.Binary => RegValue.FromBinary(ParseHex(type, token)),
            _ => throw Invalid(type, token),
        };
    }

    public static JToken ToToken(RegValue value)
    {
        if (value is null)
        {
            throw new ArgumentNullException(nameof(value));
        }

        if (!value.Exists)
        {
            return new JValue(RemoveEntryMarker);
        }

        return value.Data switch
        {
            uint dword => new JValue((long)dword),
            ulong qword => new JValue(qword),
            string[] multi => new JArray(multi.Cast<object>().ToArray()),
            byte[] binary => new JValue(string.Concat(binary.Select(b => b.ToString("x2", CultureInfo.InvariantCulture)))),
            string text => new JValue(text),
            _ => throw new FormatException(L.T("Nieobsługiwane dane wartości rejestru.")),
        };
    }

    private static string RequireString(RegistryValueType type, JToken token)
        => token.Type == JTokenType.String ? (string)token! : throw Invalid(type, token);

    private static ulong RequireInteger(RegistryValueType type, JToken token, ulong max)
    {
        if (token.Type != JTokenType.Integer
            || !ulong.TryParse(token.ToString(Formatting.None), NumberStyles.None, CultureInfo.InvariantCulture, out var value)
            || value > max)
        {
            throw Invalid(type, token);
        }

        return value;
    }

    private static string[] RequireStringArray(RegistryValueType type, JToken token)
    {
        if (token is not JArray array || array.Any(item => item.Type != JTokenType.String))
        {
            throw Invalid(type, token);
        }

        return array.Select(item => (string)item!).ToArray();
    }

    private static byte[] ParseHex(RegistryValueType type, JToken token)
    {
        var hex = RequireString(type, token);
        if (hex.Length % 2 != 0)
        {
            throw Invalid(type, token);
        }

        var bytes = new byte[hex.Length / 2];
        for (var i = 0; i < bytes.Length; i++)
        {
            if (!byte.TryParse(hex.Substring(i * 2, 2), NumberStyles.AllowHexSpecifier, CultureInfo.InvariantCulture, out bytes[i]))
            {
                throw Invalid(type, token);
            }
        }

        return bytes;
    }

    private static FormatException Invalid(RegistryValueType type, JToken token)
        => new(L.F("Wartość {0} nie pasuje do typu {1}.", token.ToString(Formatting.None), type));
}
