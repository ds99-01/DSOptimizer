using System;
using DSOptimizer.Core.Abstractions;

namespace DSOptimizer.Core.Autostart;

/// <summary>
/// Jedyny odczyt przełącznika pozycji – ten sam przy odczycie listy i przy świeżym odczycie przed zapisem i po nim. Woła tylko
/// <see cref="IRegistry.Read"/> i <see cref="IRegistry.KeyStatus"/>; formaty interpretują <see cref="StartupApprovedValue"/> i
/// <see cref="StartupTaskValue"/>.
/// </summary>
public static class AutostartSwitchReader
{
    /// <summary>
    /// Odczyt przełącznika. <see cref="AutostartSwitchKind.StartupApproved"/>: wartość <paramref name="valueName"/> w kluczu
    /// <paramref name="keyPath"/> (<paramref name="enabledInManifest"/> bez znaczenia). <see cref="AutostartSwitchKind.StartupTask"/>: klucz
    /// zadania, <paramref name="valueName"/> musi być <c>State</c>, a brak klucza albo <c>State</c> 0 = stan z manifestu.
    /// </summary>
    public static AutostartSwitch Read(IRegistry registry, AutostartSwitchKind kind, string keyPath, string valueName, bool enabledInManifest)
    {
        if (registry is null)
        {
            throw new ArgumentNullException(nameof(registry));
        }

        if (keyPath is null)
        {
            throw new ArgumentNullException(nameof(keyPath));
        }

        if (valueName is null)
        {
            throw new ArgumentNullException(nameof(valueName));
        }

        switch (kind)
        {
            case AutostartSwitchKind.StartupApproved:
                var approved = StartupApprovedValue.Interpret(registry.Read(keyPath, valueName));
                return new AutostartSwitch(kind, keyPath, valueName, approved.Format, approved.State, approved.DisabledAt, null);
            case AutostartSwitchKind.StartupTask:
                if (!string.Equals(valueName, StartupTaskValue.State, StringComparison.Ordinal))
                {
                    throw new ArgumentException("Przełącznik zadania startowego to wartość State: " + valueName, nameof(valueName));
                }

                var keyStatus = registry.KeyStatus(keyPath);
                var found = keyStatus == ReadStatus.Found;
                var state = found ? registry.Read(keyPath, StartupTaskValue.State) : RegistryReadResult.NotFound();
                var lastDisabled = found ? registry.Read(keyPath, StartupTaskValue.LastDisabledTime) : RegistryReadResult.NotFound();
                var task = StartupTaskValue.Interpret(keyStatus, state, lastDisabled, enabledInManifest);
                return new AutostartSwitch(kind, keyPath, valueName, task.Format, task.State, task.DisabledAt, task.TaskState, task.KeyExists, enabledInManifest);
            default:
                throw new ArgumentOutOfRangeException(nameof(kind), kind, null);
        }
    }

    /// <summary>Świeży odczyt tego samego przełącznika (te same klucz, nazwa wartości i stan z manifestu), np. przed zapisem i po nim.</summary>
    public static AutostartSwitch Reread(IRegistry registry, AutostartSwitch current)
    {
        if (current is null)
        {
            throw new ArgumentNullException(nameof(current));
        }

        return Read(registry, current.Kind, current.KeyPath, current.ValueName, current.EnabledInManifest);
    }
}
