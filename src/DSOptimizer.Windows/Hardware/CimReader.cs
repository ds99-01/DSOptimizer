using System;
using System.Collections.Generic;
using System.Globalization;
using System.Management;
using System.Text;

namespace DSOptimizer.Windows.Hardware;

/// <summary>
/// Zapytania CIM tylko do odczytu (<see cref="ManagementObjectSearcher"/>, bez wywołań metod) i konwersje wartości.
/// Każdy obiekt, kolekcja i wyszukiwarka są zwalniane po odczycie; błąd zapytania leci dalej jako wyjątek.
/// </summary>
internal static class CimReader
{
    internal const string Cimv2 = @"root\cimv2";
    internal const string Wmi = @"root\wmi";
    internal const string Storage = @"root\Microsoft\Windows\Storage";
    internal const string StandardCimv2 = @"root\StandardCimv2";
    internal const string Tpm = @"root\CIMV2\Security\MicrosoftTpm";
    internal const string DeviceGuard = @"root\Microsoft\Windows\DeviceGuard";
    internal const string VolumeEncryption = @"root\CIMV2\Security\MicrosoftVolumeEncryption";

    private static readonly CultureInfo Invariant = CultureInfo.InvariantCulture;

    /// <summary>Wykonuje zapytanie WQL i zamienia każdą instancję przez <paramref name="map"/> (przed zwolnieniem obiektu).</summary>
    internal static List<T> Query<T>(string scope, string wql, Func<ManagementBaseObject, T> map)
        => Query(scope, new ObjectQuery(wql), map);

    /// <summary>
    /// Instancje klasy <paramref name="resultClass"/> powiązane z <paramref name="item"/> (<c>ASSOCIATORS OF {ścieżka} WHERE ResultClass = …</c>);
    /// <paramref name="item"/> musi mieć ścieżkę (zapytanie z kluczem klasy).
    /// </summary>
    internal static List<T> Related<T>(string scope, ManagementBaseObject item, string resultClass, Func<ManagementBaseObject, T> map)
    {
        var path = item.SystemProperties["__RELPATH"].Value as string;
        if (string.IsNullOrEmpty(path))
        {
            throw new InvalidOperationException($"Obiekt {item.ClassPath.ClassName} nie ma ścieżki – zapytanie bez klucza klasy.");
        }

        return Query(scope, new RelatedObjectQuery(path, resultClass), map);
    }

    private static List<T> Query<T>(string scope, ObjectQuery query, Func<ManagementBaseObject, T> map)
    {
        var results = new List<T>();
        using var searcher = new ManagementObjectSearcher(new ManagementScope(scope), query);
        using var collection = searcher.Get();
        foreach (var item in collection)
        {
            using (item)
            {
                results.Add(map(item));
            }
        }

        return results;
    }

    /// <summary>Napis bez skrajnych spacji, ze znakami sterującymi (CR, LF, TAB…) zamienionymi na spację; pusty = null.</summary>
    internal static string? Clean(string? value)
    {
        if (value is null)
        {
            return null;
        }

        var text = new StringBuilder(value.Length);
        foreach (var c in value)
        {
            text.Append(char.IsControl(c) ? ' ' : c);
        }

        var cleaned = text.ToString().Trim();
        return cleaned.Length == 0 ? null : cleaned;
    }

    internal static string? Text(ManagementBaseObject item, string name) => Clean(item[name] as string);

    internal static uint? UInt(ManagementBaseObject item, string name)
        => item[name] is { } value ? Convert.ToUInt32(value, Invariant) : null;

    internal static ulong? ULong(ManagementBaseObject item, string name)
        => item[name] is { } value ? Convert.ToUInt64(value, Invariant) : null;

    internal static bool? Bool(ManagementBaseObject item, string name)
        => item[name] is { } value ? Convert.ToBoolean(value, Invariant) : null;

    /// <summary>Liczba bez znaku do <c>int</c>; wartość spoza zakresu = null.</summary>
    internal static int? Int(ManagementBaseObject item, string name)
        => UInt(item, name) is { } value && value <= int.MaxValue ? (int)value : null;

    /// <summary>Surowy napis daty CIM (<c>yyyyMMddHHmmss.ffffff±UUU</c>), bez przeliczania strefy.</summary>
    internal static string? RawDateTime(ManagementBaseObject item, string name) => item[name] as string;
}
