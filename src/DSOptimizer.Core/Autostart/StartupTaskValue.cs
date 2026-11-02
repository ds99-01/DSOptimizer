using System;
using DSOptimizer.Core.Abstractions;
using DSOptimizer.Core.Model;

namespace DSOptimizer.Core.Autostart;

public sealed record StartupTaskRead(AutostartSwitchFormat Format, AutostartState State, DateTimeOffset? DisabledAt, uint? TaskState);

/// <summary>
/// Stan zadania startowego aplikacji ze Sklepu (klucz <see cref="AutostartLocations.StartupTaskKey"/>): <c>State</c> DWORD 1 = wyłączone
/// (obok <c>LastDisabledTime</c> QWORD, sekundy Unix UTC), 2 = włączone. Brak klucza albo <c>State</c> 0 (klucz utworzony przez system,
/// jeszcze nie przełączany) = wartość <c>Enabled</c> z manifestu. Inne numery (3, 4 – zasady) = stan nieznany, bez zgadywania znaczenia.
/// </summary>
public static class StartupTaskValue
{
    public const string State = "State";
    public const string LastDisabledTime = "LastDisabledTime";
    public const string UserEnabledStartupOnce = "UserEnabledStartupOnce";
    public const string ShowNotification = "ShowNotification";

    public static StartupTaskRead Interpret(ReadStatus keyStatus, RegistryReadResult state, RegistryReadResult lastDisabled, bool enabledInManifest)
    {
        if (state is null)
        {
            throw new ArgumentNullException(nameof(state));
        }

        if (lastDisabled is null)
        {
            throw new ArgumentNullException(nameof(lastDisabled));
        }

        var manifestState = enabledInManifest ? AutostartState.Enabled : AutostartState.Disabled;
        if (keyStatus == ReadStatus.NotFound || (keyStatus == ReadStatus.Found && state.Status == ReadStatus.NotFound))
        {
            return new StartupTaskRead(AutostartSwitchFormat.Absent, manifestState, null, null);
        }

        if (keyStatus != ReadStatus.Found || state.Status != ReadStatus.Found)
        {
            return new StartupTaskRead(AutostartSwitchFormat.Unreadable, AutostartState.Unknown, null, null);
        }

        if (state.Value.Data is not uint value || state.Value.Type != RegistryValueType.DWord)
        {
            return new StartupTaskRead(AutostartSwitchFormat.UnknownFormat, AutostartState.Unknown, null, null);
        }

        return value switch
        {
            0 => new StartupTaskRead(AutostartSwitchFormat.Absent, manifestState, null, value),
            1 => new StartupTaskRead(AutostartSwitchFormat.Valid, AutostartState.Disabled, DisabledAtOf(lastDisabled), value),
            2 => new StartupTaskRead(AutostartSwitchFormat.Valid, AutostartState.Enabled, null, value),
            _ => new StartupTaskRead(AutostartSwitchFormat.Policy, AutostartState.Unknown, null, value),
        };
    }

    private static DateTimeOffset? DisabledAtOf(RegistryReadResult lastDisabled)
    {
        // Zakres DateTimeOffset.FromUnixTimeSeconds: do 9999-12-31 23:59:59 UTC.
        const ulong MaxSeconds = 253_402_300_799;
        if (lastDisabled.Status != ReadStatus.Found || lastDisabled.Value.Data is not ulong seconds || seconds == 0 || seconds > MaxSeconds)
        {
            return null;
        }

        return DateTimeOffset.FromUnixTimeSeconds((long)seconds);
    }
}
