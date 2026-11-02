using System;
using System.Collections.Generic;
using DSOptimizer.Core.Autostart;

namespace DSOptimizer.Windows.Autostart;

/// <summary>
/// Zmienne maszyny ze środowiska procesu – wyłącznie <see cref="UserEnvironment.MachineVariableNames"/> (te same dla każdego konta).
/// Zmienne konta (<c>APPDATA</c>, <c>TEMP</c> …) nigdy nie są brane z procesu: program bywa uruchomiony innym kontem niż zalogowane.
/// </summary>
public static class MachineEnvironment
{
    public static IReadOnlyDictionary<string, string> FromProcess()
    {
        var variables = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var name in UserEnvironment.MachineVariableNames)
        {
            var value = Environment.GetEnvironmentVariable(name);
            if (!string.IsNullOrEmpty(value))
            {
                variables[name] = value!;
            }
        }

        return variables;
    }
}
