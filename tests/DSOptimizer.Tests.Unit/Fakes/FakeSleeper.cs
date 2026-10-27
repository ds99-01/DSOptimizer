using System;
using System.Collections.Generic;

namespace DSOptimizer.Tests.Unit.Fakes;

/// <summary>Udaje usypianie: przesuwa FakeClock zamiast czekać naprawdę.</summary>
public sealed class FakeSleeper
{
    private readonly FakeClock _clock;

    public FakeSleeper(FakeClock clock) => _clock = clock;

    public List<TimeSpan> Sleeps { get; } = new();

    /// <summary>Wołane po każdym „uśpieniu” (np. zmiana odpowiedzi fałszywego odczytu między kolejnymi odczytami).</summary>
    public Action? OnSleep { get; set; }

    public void Sleep(TimeSpan duration)
    {
        Sleeps.Add(duration);
        _clock.UtcNow += duration;
        OnSleep?.Invoke();
    }
}
