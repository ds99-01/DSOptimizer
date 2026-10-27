using System;
using System.Collections.Generic;
using DSOptimizer.Core.Abstractions;
using DSOptimizer.Core.Errors;
using DSOptimizer.Core.Model;

namespace DSOptimizer.Tests.Unit.Fakes;

/// <summary>Rejestr w pamięci (klucze bez rozróżniania wielkości liter) ze wstrzykiwanymi błędami.</summary>
public sealed class FakeRegistry : IRegistry
{
    private readonly Dictionary<string, RegValue> _values = new(StringComparer.OrdinalIgnoreCase);
    private readonly HashSet<string> _deniedReads = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, OperationErrorKind> _failingWrites = new(StringComparer.OrdinalIgnoreCase);

    private readonly HashSet<string> _keys = new(StringComparer.OrdinalIgnoreCase);
    private readonly HashSet<string> _deniedKeys = new(StringComparer.OrdinalIgnoreCase);

    public List<string> DeletedKeys { get; } = new();

    /// <summary>Dostaje wywołania DeleteKeyTree (wspólny dziennik kolejności z innymi fake'ami).</summary>
    public Action<string>? Observer { get; set; }

    public List<string> WriteLog { get; } = new();

    public int ReadCount { get; private set; }

    /// <summary>Wywoływany przed każdym zapisem; zwrócony wyjątek zostanie rzucony.</summary>
    public Func<string, string, RegValue, Exception?>? WriteInterceptor { get; set; }

    public void Set(string path, string name, RegValue value)
    {
        if (value.Exists)
        {
            CreateKey(path);
            _values[Key(path, name)] = value;
        }
        else
        {
            _values.Remove(Key(path, name));
        }
    }

    /// <summary>Tworzy klucz wraz ze wszystkimi przodkami.</summary>
    public void CreateKey(string path)
    {
        var current = NormalizeKey(path);
        while (current.Length > 0)
        {
            _keys.Add(current);
            var slash = current.LastIndexOf('\\');
            current = slash < 0 ? string.Empty : current.Substring(0, slash);
        }
    }

    public bool HasKey(string path) => _keys.Contains(NormalizeKey(path));

    public void DenyKey(string path) => _deniedKeys.Add(NormalizeKey(path));

    public ReadStatus KeyStatus(string path)
    {
        var key = NormalizeKey(path);
        if (_deniedKeys.Contains(key))
        {
            return ReadStatus.AccessDenied;
        }

        return _keys.Contains(key) ? ReadStatus.Found : ReadStatus.NotFound;
    }

    public IReadOnlyList<string> SubKeyNames(string path)
    {
        var key = NormalizeKey(path);
        if (_deniedKeys.Contains(key))
        {
            throw new OperationException(OperationErrorKind.AccessDenied, $"Lista podkluczy {path} odrzucona (fake).");
        }

        var childPrefix = key + "\\";
        var names = new List<string>();
        foreach (var other in _keys)
        {
            if (other.StartsWith(childPrefix, StringComparison.OrdinalIgnoreCase) && other.IndexOf('\\', childPrefix.Length) < 0)
            {
                names.Add(other.Substring(childPrefix.Length));
            }
        }

        return names;
    }

    public bool DeleteKeyIfEmpty(string path)
    {
        var key = NormalizeKey(path);
        if (_deniedKeys.Contains(key))
        {
            throw new OperationException(OperationErrorKind.AccessDenied, $"Usunięcie klucza {path} odrzucone (fake).");
        }

        if (!_keys.Contains(key))
        {
            return true;
        }

        var hasValue = false;
        foreach (var valueKey in _values.Keys)
        {
            if (valueKey.StartsWith(key + "|", StringComparison.OrdinalIgnoreCase))
            {
                hasValue = true;
                break;
            }
        }

        var childPrefix = key + "\\";
        var hasSubKey = false;
        foreach (var other in _keys)
        {
            if (other.StartsWith(childPrefix, StringComparison.OrdinalIgnoreCase))
            {
                hasSubKey = true;
                break;
            }
        }

        if (hasValue || hasSubKey)
        {
            return false;
        }

        _keys.Remove(key);
        DeletedKeys.Add(key);
        return true;
    }

    public void DeleteKeyTree(string path)
    {
        var key = NormalizeKey(path);

        // Ten sam bezpiecznik co WindowsRegistry.DeleteKeyTree: nigdy korzeń ani jego bezpośrednie dziecko.
        var firstSlash = key.IndexOf('\\');
        if (firstSlash < 0 || key.IndexOf('\\', firstSlash + 1) < 0)
        {
            throw new ArgumentException($"Ścieżka musi wskazywać podklucz drugiego poziomu lub głębszy: {path}", nameof(path));
        }

        Observer?.Invoke($"DeleteKeyTree:{key}");
        if (_deniedKeys.Contains(key))
        {
            throw new OperationException(OperationErrorKind.AccessDenied, $"Usunięcie drzewa {path} odrzucone (fake).");
        }

        var childPrefix = key + "\\";
        foreach (var existing in new List<string>(_keys))
        {
            if (string.Equals(existing, key, StringComparison.OrdinalIgnoreCase)
                || existing.StartsWith(childPrefix, StringComparison.OrdinalIgnoreCase))
            {
                _keys.Remove(existing);
                DeletedKeys.Add(existing);
            }
        }

        foreach (var valueKey in new List<string>(_values.Keys))
        {
            var owner = valueKey.Substring(0, valueKey.IndexOf('|'));
            if (string.Equals(owner, key, StringComparison.OrdinalIgnoreCase)
                || owner.StartsWith(childPrefix, StringComparison.OrdinalIgnoreCase))
            {
                _values.Remove(valueKey);
            }
        }
    }

    public RegValue Get(string path, string name)
        => _values.TryGetValue(Key(path, name), out var value) ? value : RegValue.Absent;

    public void DenyRead(string path, string name) => _deniedReads.Add(Key(path, name));

    public void FailWrite(string path, string name, OperationErrorKind kind = OperationErrorKind.AccessDenied)
        => _failingWrites[Key(path, name)] = kind;

    /// <summary>Wyjątek rzucany przez <see cref="Read"/> dla ścieżki i nazwy (null = brak błędu; symulacja awarii adaptera).</summary>
    public Func<string, string, Exception?>? ReadFailure { get; set; }

    public RegistryReadResult Read(string path, string name)
    {
        ReadCount++;
        if (ReadFailure?.Invoke(path, name) is { } failure)
        {
            throw failure;
        }

        if (_deniedReads.Contains(Key(path, name)))
        {
            return RegistryReadResult.AccessDenied("Odmowa dostępu (fake).");
        }

        return _values.TryGetValue(Key(path, name), out var value)
            ? RegistryReadResult.Found(value)
            : RegistryReadResult.NotFound();
    }

    public void Write(string path, string name, RegValue value)
    {
        if (_failingWrites.TryGetValue(Key(path, name), out var kind))
        {
            throw new OperationException(kind, $"Zapis {path}\\{name} odrzucony (fake).");
        }

        if (WriteInterceptor?.Invoke(path, name, value) is { } exception)
        {
            throw exception;
        }

        WriteLog.Add($"{path}\\{name}={value}");
        Set(path, name, value);
    }

    private static string NormalizeKey(string path) => path.TrimEnd('\\');

    private static string Key(string path, string name) => path + "|" + name;
}
