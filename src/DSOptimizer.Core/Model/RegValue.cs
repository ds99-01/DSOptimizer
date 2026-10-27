using System;
using System.Globalization;
using System.Linq;

namespace DSOptimizer.Core.Model;

/// <summary>
/// Wartość rejestru razem z typem albo marker „wartość nie istnieje” (<see cref="Absent"/>). Niezmienna.
/// Dane: string (String/ExpandString), uint (DWord), ulong (QWord), string[] (MultiString), byte[] (Binary).
/// </summary>
public sealed class RegValue : IEquatable<RegValue>
{
    public static readonly RegValue Absent = new(exists: false, RegistryValueType.String, data: null);

    private RegValue(bool exists, RegistryValueType type, object? data)
    {
        Exists = exists;
        Type = type;
        Data = data;
    }

    public bool Exists { get; }

    public RegistryValueType Type { get; }

    public object? Data { get; }

    public static RegValue FromString(string value) => Create(RegistryValueType.String, value);

    public static RegValue FromExpandString(string value) => Create(RegistryValueType.ExpandString, value);

    public static RegValue FromDWord(uint value) => new(exists: true, RegistryValueType.DWord, value);

    public static RegValue FromQWord(ulong value) => new(exists: true, RegistryValueType.QWord, value);

    public static RegValue FromMultiString(params string[] values)
        => Create(RegistryValueType.MultiString, values is null ? null : (string[])values.Clone());

    public static RegValue FromBinary(byte[] value)
        => Create(RegistryValueType.Binary, value is null ? null : (byte[])value.Clone());

    private static RegValue Create(RegistryValueType type, object? data)
        => data is null ? throw new ArgumentNullException(nameof(data)) : new RegValue(exists: true, type, data);

    public bool Equals(RegValue? other)
    {
        if (other is null)
        {
            return false;
        }

        if (!Exists || !other.Exists)
        {
            return Exists == other.Exists;
        }

        if (Type != other.Type)
        {
            return false;
        }

        return (Data, other.Data) switch
        {
            (string[] left, string[] right) => left.SequenceEqual(right, StringComparer.Ordinal),
            (byte[] left, byte[] right) => left.SequenceEqual(right),
            (string left, string right) => string.Equals(left, right, StringComparison.Ordinal),
            var (left, right) => Equals(left, right),
        };
    }

    public override bool Equals(object? obj) => Equals(obj as RegValue);

    public override int GetHashCode()
        => Exists ? StringComparer.Ordinal.GetHashCode(ToDisplayString()) : 0;

    public string ToDisplayString()
    {
        if (!Exists)
        {
            return "(brak)";
        }

        return Data switch
        {
            uint dword => string.Format(CultureInfo.InvariantCulture, "{0} (0x{0:X8}) [DWord]", dword),
            ulong qword => string.Format(CultureInfo.InvariantCulture, "{0} (0x{0:X16}) [QWord]", qword),
            string[] multi => "[" + string.Join(", ", multi) + "] [MultiString]",
            byte[] binary => BitConverter.ToString(binary).Replace("-", string.Empty) + " [Binary]",
            string text => "\"" + text + "\" [" + Type + "]",
            _ => "(?)",
        };
    }

    public override string ToString() => ToDisplayString();
}
