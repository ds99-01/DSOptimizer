using System;
using DSOptimizer.Core.Abstractions;
using DSOptimizer.Core.Model;

namespace DSOptimizer.Core.Autostart;

/// <summary>Format odczytanego przełącznika pozycji; zapisywać wolno tylko <see cref="Valid"/> i <see cref="Absent"/>.</summary>
public enum AutostartSwitchFormat
{
    /// <summary>Zmierzony format z wartością.</summary>
    Valid,

    /// <summary>Brak wartości (klucza): stan domyślny.</summary>
    Absent,

    /// <summary>Inny typ albo długość niż zmierzone – tylko do odczytu.</summary>
    UnknownFormat,

    /// <summary>Stan ustawiony przez zasady albo nieznany numer stanu – tylko do odczytu.</summary>
    Policy,

    /// <summary>Brak dostępu albo błąd odczytu.</summary>
    Unreadable,
}

public sealed record StartupApprovedRead(AutostartSwitchFormat Format, AutostartState State, DateTimeOffset? DisabledAt, byte[]? Raw);

/// <summary>
/// Wartość <c>StartupApproved</c> (<c>REG_BINARY</c>, 12 B): bit 0 bajtu 0 = wyłączone, pozostałe bity bajtu 0 i bajty 1–3 zachowywane,
/// bajty 4–11 = FILETIME UTC chwili wyłączenia (włączenie je zeruje). Brak wartości = włączone.
/// </summary>
public static class StartupApprovedValue
{
    public const int Length = 12;

    private const int FiletimeOffset = 4;

    public static StartupApprovedRead Interpret(RegistryReadResult read)
    {
        if (read is null)
        {
            throw new ArgumentNullException(nameof(read));
        }

        switch (read.Status)
        {
            case ReadStatus.NotFound:
                return new StartupApprovedRead(AutostartSwitchFormat.Absent, AutostartState.Enabled, null, null);
            case ReadStatus.Found:
                break;
            default:
                return new StartupApprovedRead(AutostartSwitchFormat.Unreadable, AutostartState.Unknown, null, null);
        }

        if (read.Value.Type != RegistryValueType.Binary || read.Value.Data is not byte[] bytes)
        {
            return new StartupApprovedRead(AutostartSwitchFormat.UnknownFormat, AutostartState.Unknown, null, null);
        }

        var copy = (byte[])bytes.Clone();
        if (copy.Length != Length)
        {
            var state = copy.Length >= 1 ? StateOf(copy[0]) : AutostartState.Unknown;
            return new StartupApprovedRead(AutostartSwitchFormat.UnknownFormat, state, null, copy);
        }

        return new StartupApprovedRead(AutostartSwitchFormat.Valid, StateOf(copy[0]), DisabledAtOf(copy), copy);
    }

    /// <summary>Wartość po wyłączeniu: istniejące 12 B → bit 0 ustawiony + FILETIME teraz; brak wartości → <c>03 00 00 00</c> + FILETIME.</summary>
    public static byte[] Disabled(byte[]? current, DateTimeOffset now)
    {
        byte[] result;
        if (current is null)
        {
            result = new byte[Length];
            result[0] = 0x03;
        }
        else
        {
            result = CopyOfValid(current, nameof(current));
            result[0] |= 0x01;
        }

        var filetime = now.UtcDateTime.ToFileTimeUtc();
        for (var i = 0; i < 8; i++)
        {
            result[FiletimeOffset + i] = (byte)(filetime >> (8 * i));
        }

        return result;
    }

    /// <summary>Wartość po włączeniu: bit 0 wyczyszczony, bajty 4–11 = 0 (jak Menedżer zadań).</summary>
    public static byte[] Enabled(byte[] current)
    {
        if (current is null)
        {
            throw new ArgumentNullException(nameof(current));
        }

        var result = CopyOfValid(current, nameof(current));
        result[0] &= unchecked((byte)~0x01);
        Array.Clear(result, FiletimeOffset, Length - FiletimeOffset);
        return result;
    }

    private static byte[] CopyOfValid(byte[] current, string name)
        => current.Length == Length
            ? (byte[])current.Clone()
            : throw new ArgumentException("Wartość StartupApproved musi mieć 12 bajtów, ma " + current.Length + ".", name);

    private static AutostartState StateOf(byte first) => (first & 0x01) != 0 ? AutostartState.Disabled : AutostartState.Enabled;

    private static DateTimeOffset? DisabledAtOf(byte[] bytes)
    {
        long filetime = 0;
        for (var i = 7; i >= 0; i--)
        {
            filetime = (filetime << 8) | bytes[FiletimeOffset + i];
        }

        if (filetime <= 0 || filetime > DateTime.MaxValue.ToFileTimeUtc())
        {
            return null;
        }

        return new DateTimeOffset(DateTime.FromFileTimeUtc(filetime), TimeSpan.Zero);
    }
}
