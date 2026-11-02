using System;
using System.Collections.Generic;
using System.Text;
using DSOptimizer.Core.Abstractions;
using DSOptimizer.Core.Errors;
using DSOptimizer.Core.Model;

namespace DSOptimizer.Core.Autostart;

/// <summary>
/// Zmienne środowiskowe zalogowanego konta do rozwijania ścieżek autostartu. Program działa jako administrator, często innym kontem,
/// więc zmienne procesu nie nadają się: z procesu bierze się tylko zmienne maszyny (<see cref="MachineVariableNames"/>, te same dla
/// każdego konta), a zmienne konta wyłącznie z <c>HKU\&lt;SID&gt;\Environment</c> i <c>HKU\&lt;SID&gt;\Volatile Environment</c>.
/// Później wygrywa: maszyna → <c>Environment</c> → <c>Volatile Environment</c>. Nazwy bez rozróżniania wielkości liter.
/// </summary>
public sealed class UserEnvironment
{
    private static readonly string[] MachineNames =
    {
        "SystemRoot", "windir", "SystemDrive", "ProgramFiles", "ProgramFiles(x86)", "ProgramW6432", "ProgramData",
        "ALLUSERSPROFILE", "CommonProgramFiles", "CommonProgramFiles(x86)", "CommonProgramW6432", "PUBLIC",
    };

    private readonly Dictionary<string, string> _variables;

    private UserEnvironment(Dictionary<string, string> variables) => _variables = variables;

    /// <summary>Zmienne brane ze środowiska procesu; każda inna nazwa ze słownika maszyny jest pomijana.</summary>
    public static IReadOnlyList<string> MachineVariableNames => MachineNames;

    /// <summary>
    /// Składa zmienne konta. Wartości <c>REG_EXPAND_SZ</c> z <c>Environment</c> są rozwijane raz zmiennymi już znanymi (maszyna,
    /// <c>REG_SZ</c> z <c>Environment</c>, <c>Volatile Environment</c>); wartość z nieznaną zmienną jest pomijana. Błąd odczytu klucza
    /// albo wartości konta = linia w <paramref name="warnings"/>, nie wyjątek. Odczyt niczego nie zapisuje.
    /// </summary>
    public static UserEnvironment Load(
        IRegistry registry,
        AutostartLocations locations,
        IReadOnlyDictionary<string, string> machine,
        ICollection<string>? warnings = null)
    {
        if (registry is null)
        {
            throw new ArgumentNullException(nameof(registry));
        }

        if (locations is null)
        {
            throw new ArgumentNullException(nameof(locations));
        }

        if (machine is null)
        {
            throw new ArgumentNullException(nameof(machine));
        }

        var machineOnly = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var pair in machine)
        {
            if (pair.Value is not null && Array.Exists(MachineNames, n => string.Equals(n, pair.Key, StringComparison.OrdinalIgnoreCase)))
            {
                machineOnly[pair.Key] = pair.Value;
            }
        }

        var environment = ReadStrings(registry, locations.UserEnvironmentKey, warnings);
        var volatileValues = ReadStrings(registry, locations.VolatileEnvironmentKey, warnings);

        var known = new Dictionary<string, string>(machineOnly, StringComparer.OrdinalIgnoreCase);
        foreach (var (name, value) in environment)
        {
            if (value.Type == RegistryValueType.String)
            {
                known[name] = (string)value.Data!;
            }
        }

        foreach (var (name, value) in volatileValues)
        {
            known[name] = (string)value.Data!;
        }

        var result = new Dictionary<string, string>(machineOnly, StringComparer.OrdinalIgnoreCase);
        foreach (var (name, value) in environment)
        {
            var text = (string)value.Data!;
            if (value.Type == RegistryValueType.String)
            {
                result[name] = text;
            }
            else if (TryExpand(known, text, out var expanded))
            {
                result[name] = expanded;
            }
            else
            {
                result.Remove(name);
            }
        }

        foreach (var (name, value) in volatileValues)
        {
            result[name] = (string)value.Data!;
        }

        return new UserEnvironment(result);
    }

    public static UserEnvironment FromVariables(IReadOnlyDictionary<string, string> variables)
    {
        if (variables is null)
        {
            throw new ArgumentNullException(nameof(variables));
        }

        var copy = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var pair in variables)
        {
            if (pair.Value is not null)
            {
                copy[pair.Key] = pair.Value;
            }
        }

        return new UserEnvironment(copy);
    }

    /// <summary>
    /// Rozwija <c>%NAZWA%</c> (bez rozróżniania wielkości liter). False, gdy któraś nazwa jest nieznana – wtedy <paramref name="expanded"/>
    /// ma znane zmienne rozwinięte, a nieznane bez zmian. Pojedynczy <c>%</c> bez pary zostaje dosłownie.
    /// </summary>
    public bool TryExpand(string text, out string expanded)
    {
        if (text is null)
        {
            throw new ArgumentNullException(nameof(text));
        }

        return TryExpand(_variables, text, out expanded);
    }

    public string? Get(string name) => _variables.TryGetValue(name, out var value) ? value : null;

    private static bool TryExpand(Dictionary<string, string> variables, string text, out string expanded)
    {
        var builder = new StringBuilder(text.Length);
        var complete = true;
        var index = 0;
        while (index < text.Length)
        {
            var open = text.IndexOf('%', index);
            var close = open < 0 ? -1 : text.IndexOf('%', open + 1);
            if (close < 0)
            {
                builder.Append(text, index, text.Length - index);
                break;
            }

            builder.Append(text, index, open - index);
            var name = text.Substring(open + 1, close - open - 1);
            if (name.Length > 0 && variables.TryGetValue(name, out var value))
            {
                builder.Append(value);
                index = close + 1;
            }
            else
            {
                // Jak ExpandEnvironmentStrings: nieznana nazwa zostaje, a zamykający % może otwierać następną zmienną.
                complete = false;
                builder.Append('%').Append(name);
                index = close;
            }
        }

        expanded = builder.ToString();
        return complete;
    }

    private static List<(string Name, RegValue Value)> ReadStrings(IRegistry registry, string key, ICollection<string>? warnings)
    {
        var values = new List<(string, RegValue)>();
        IReadOnlyList<string> names;
        try
        {
            names = registry.ValueNames(key);
        }
        catch (OperationException ex)
        {
            warnings?.Add(AutostartText.EnvironmentUnreadable(key, ex.Message));
            return values;
        }

        foreach (var name in names)
        {
            if (name.Length == 0)
            {
                continue;
            }

            var read = registry.Read(key, name);
            switch (read.Status)
            {
                case ReadStatus.Found when read.Value.Type is RegistryValueType.String or RegistryValueType.ExpandString:
                    values.Add((name, read.Value));
                    break;
                case ReadStatus.AccessDenied or ReadStatus.Error:
                    warnings?.Add(AutostartText.EnvironmentUnreadable(key + @"\" + name, read.Error ?? read.Status.ToString()));
                    break;
            }
        }

        return values;
    }
}
