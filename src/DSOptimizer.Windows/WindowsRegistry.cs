using System;
using System.Collections.Generic;
using System.IO;
using System.Security;
using DSOptimizer.Core.Abstractions;
using DSOptimizer.Core.Errors;
using DSOptimizer.Core.Localization;
using DSOptimizer.Core.Model;
using Microsoft.Win32;

namespace DSOptimizer.Windows;

/// <summary>Rejestr Windows, zawsze widok 64-bit (bez przekierowania do WOW6432Node).</summary>
public sealed class WindowsRegistry : IRegistry
{
    private static readonly (string Prefix, RegistryHive Hive)[] Roots =
    {
        (@"HKLM\", RegistryHive.LocalMachine),
        (@"HKU\", RegistryHive.Users),
        (@"HKCR\", RegistryHive.ClassesRoot),
        (@"HKCU\", RegistryHive.CurrentUser),
    };

    public RegistryReadResult Read(string path, string name)
    {
        if (name is null)
        {
            throw new ArgumentNullException(nameof(name));
        }

        var (hive, subKey) = Split(path);
        try
        {
            using var root = RegistryKey.OpenBaseKey(hive, RegistryView.Registry64);
            using var key = root.OpenSubKey(subKey, writable: false);
            if (key is null)
            {
                return RegistryReadResult.NotFound();
            }

            var raw = key.GetValue(name, null, RegistryValueOptions.DoNotExpandEnvironmentNames);
            return raw is null ? RegistryReadResult.NotFound() : ToResult(key.GetValueKind(name), raw, path, name);
        }
        catch (SecurityException ex)
        {
            return RegistryReadResult.AccessDenied(ex.Message);
        }
        catch (UnauthorizedAccessException ex)
        {
            return RegistryReadResult.AccessDenied(ex.Message);
        }
        catch (IOException ex)
        {
            return RegistryReadResult.Failed(ex.Message);
        }
    }

    public void Write(string path, string name, RegValue value)
    {
        if (name is null)
        {
            throw new ArgumentNullException(nameof(name));
        }

        if (value is null)
        {
            throw new ArgumentNullException(nameof(value));
        }

        var (hive, subKey) = Split(path);
        try
        {
            using var root = RegistryKey.OpenBaseKey(hive, RegistryView.Registry64);
            if (!value.Exists)
            {
                using var existing = root.OpenSubKey(subKey, writable: true);
                existing?.DeleteValue(name, throwOnMissingValue: false);
                return;
            }

            using var key = root.CreateSubKey(subKey, writable: true)
                ?? throw new OperationException(OperationErrorKind.Unexpected, L.F("Nie można utworzyć klucza {0}.", path));
            key.SetValue(name, ToRaw(value), ToKind(value.Type));
        }
        catch (SecurityException ex)
        {
            throw new OperationException(OperationErrorKind.AccessDenied, L.F("Brak uprawnień do zapisu {0}\\{1}: {2}", path, name, ex.Message), ex);
        }
        catch (UnauthorizedAccessException ex)
        {
            throw new OperationException(OperationErrorKind.AccessDenied, L.F("Brak uprawnień do zapisu {0}\\{1}: {2}", path, name, ex.Message), ex);
        }
        catch (IOException ex)
        {
            throw new OperationException(OperationErrorKind.Unexpected, L.F("Błąd zapisu {0}\\{1}: {2}", path, name, ex.Message), ex);
        }
    }

    public ReadStatus KeyStatus(string path)
    {
        var (hive, subKey) = Split(path);
        try
        {
            using var root = RegistryKey.OpenBaseKey(hive, RegistryView.Registry64);
            using var key = root.OpenSubKey(subKey, writable: false);
            return key is null ? ReadStatus.NotFound : ReadStatus.Found;
        }
        catch (Exception ex) when (ex is SecurityException or UnauthorizedAccessException)
        {
            return ReadStatus.AccessDenied;
        }
        catch (IOException)
        {
            return ReadStatus.Error;
        }
    }

    public IReadOnlyList<string> SubKeyNames(string path)
    {
        var (hive, subKey) = Split(path);
        try
        {
            using var root = RegistryKey.OpenBaseKey(hive, RegistryView.Registry64);
            using var key = root.OpenSubKey(subKey, writable: false);
            return key is null ? Array.Empty<string>() : key.GetSubKeyNames();
        }
        catch (Exception ex) when (ex is SecurityException or UnauthorizedAccessException)
        {
            throw new OperationException(OperationErrorKind.AccessDenied, L.F("Brak uprawnień do odczytu podkluczy {0}: {1}", path, ex.Message), ex);
        }
        catch (IOException ex)
        {
            throw new OperationException(OperationErrorKind.Unexpected, L.F("Błąd odczytu podkluczy {0}: {1}", path, ex.Message), ex);
        }
    }

    public bool DeleteKeyIfEmpty(string path)
    {
        var (hive, subKey) = Split(path);
        var slash = subKey.LastIndexOf('\\');
        if (slash < 0)
        {
            throw new ArgumentException($"Ścieżka nie wskazuje podklucza: {path}", nameof(path));
        }

        var parentPath = subKey.Substring(0, slash);
        var leaf = subKey.Substring(slash + 1);
        try
        {
            using var root = RegistryKey.OpenBaseKey(hive, RegistryView.Registry64);
            using (var key = root.OpenSubKey(subKey, writable: false))
            {
                if (key is null)
                {
                    return true;
                }

                if (key.ValueCount != 0 || key.SubKeyCount != 0)
                {
                    return false;
                }
            }

            // TOCTOU: między sprawdzeniem pustki a usunięciem ktoś może dopisać wartość (RegDeleteKeyEx skasuje ją razem z kluczem)
            // albo podklucz (wtedy DeleteSubKey rzuca InvalidOperationException, obsłużone niżej jako "niepusty"). Win32 nie ma
            // atomowego "usuń, jeśli pusty"; okno trwa mikrosekundy i dotyczy tylko klucza utworzonego przez DSOptimizer.
            using var parent = root.OpenSubKey(parentPath, writable: true);
            parent?.DeleteSubKey(leaf, throwOnMissingSubKey: false);
            return true;
        }
        catch (InvalidOperationException)
        {
            // Wyścig: w międzyczasie pojawił się podklucz, więc klucz nie jest pusty. Nie błąd - nic nie usunięto.
            return false;
        }
        catch (Exception ex) when (ex is SecurityException or UnauthorizedAccessException)
        {
            throw new OperationException(OperationErrorKind.AccessDenied, L.F("Brak uprawnień do usunięcia klucza {0}: {1}", path, ex.Message), ex);
        }
        catch (IOException ex)
        {
            throw new OperationException(OperationErrorKind.Unexpected, L.F("Błąd usuwania klucza {0}: {1}", path, ex.Message), ex);
        }
    }

    public void DeleteKeyTree(string path)
    {
        var (hive, subKey) = Split(path);

        // Bezpiecznik: nigdy korzeń ani bezpośrednie dziecko korzenia (HKLM\SOFTWARE, HKU\<SID>).
        if (subKey.IndexOf('\\') < 0)
        {
            throw new ArgumentException($"Ścieżka musi wskazywać podklucz drugiego poziomu lub głębszy: {path}", nameof(path));
        }

        try
        {
            using var root = RegistryKey.OpenBaseKey(hive, RegistryView.Registry64);
            root.DeleteSubKeyTree(subKey, throwOnMissingSubKey: false);
        }
        catch (Exception ex) when (ex is SecurityException or UnauthorizedAccessException)
        {
            throw new OperationException(OperationErrorKind.AccessDenied, L.F("Brak uprawnień do usunięcia drzewa kluczy {0}: {1}", path, ex.Message), ex);
        }
        catch (Exception ex) when (ex is IOException or ArgumentException)
        {
            throw new OperationException(OperationErrorKind.Unexpected, L.F("Błąd usuwania drzewa kluczy {0}: {1}", path, ex.Message), ex);
        }
    }

    internal static (RegistryHive Hive, string SubKey) Split(string path)
    {
        if (path is null)
        {
            throw new ArgumentNullException(nameof(path));
        }

        foreach (var (prefix, hive) in Roots)
        {
            if (path.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
            {
                return (hive, path.Substring(prefix.Length).TrimEnd('\\'));
            }
        }

        throw new ArgumentException($"Nieobsługiwany korzeń rejestru: {path}", nameof(path));
    }

    private static RegistryReadResult ToResult(RegistryValueKind kind, object raw, string path, string name) => kind switch
    {
        RegistryValueKind.String => RegistryReadResult.Found(RegValue.FromString((string)raw)),
        RegistryValueKind.ExpandString => RegistryReadResult.Found(RegValue.FromExpandString((string)raw)),
        RegistryValueKind.DWord when raw is int dword => RegistryReadResult.Found(RegValue.FromDWord(unchecked((uint)dword))),
        RegistryValueKind.QWord when raw is long qword => RegistryReadResult.Found(RegValue.FromQWord(unchecked((ulong)qword))),
        RegistryValueKind.MultiString => RegistryReadResult.Found(RegValue.FromMultiString((string[])raw)),
        RegistryValueKind.Binary => RegistryReadResult.Found(RegValue.FromBinary((byte[])raw)),
        _ => RegistryReadResult.Failed(L.F("Nieobsługiwany typ wartości {0} w {1}\\{2}.", kind, path, name)),
    };

    private static object ToRaw(RegValue value) => value.Data switch
    {
        uint dword => unchecked((int)dword),
        ulong qword => unchecked((long)qword),
        _ => value.Data!,
    };

    private static RegistryValueKind ToKind(RegistryValueType type) => type switch
    {
        RegistryValueType.String => RegistryValueKind.String,
        RegistryValueType.ExpandString => RegistryValueKind.ExpandString,
        RegistryValueType.DWord => RegistryValueKind.DWord,
        RegistryValueType.QWord => RegistryValueKind.QWord,
        RegistryValueType.MultiString => RegistryValueKind.MultiString,
        _ => RegistryValueKind.Binary,
    };
}
