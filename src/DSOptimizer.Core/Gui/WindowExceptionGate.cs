using System;
using System.Collections.Generic;

namespace DSOptimizer.Core.Gui;

/// <summary>Dokąd trafia wyjątek okna: do panelu dziennika (i pliku), tylko do pliku z jednym komunikatem, albo tylko do pliku.</summary>
public enum WindowExceptionRoute
{
    Panel,
    FileOnlyWithNotice,
    FileOnly,
}

/// <summary>
/// Bramka lawiny wyjątków okna: do <see cref="Limit"/> w oknie przesuwnym <see cref="Window"/> idzie do panelu; kolejny
/// daje jeden komunikat, następne tylko plik. Wyciszenie kończy się po <see cref="Window"/> bez wyjątku (licznik od zera).
/// Bezpieczna między wątkami.
/// </summary>
public sealed class WindowExceptionGate
{
    public const int Limit = 5;

    public static readonly TimeSpan Window = TimeSpan.FromSeconds(10);

    private readonly object _sync = new();
    private readonly Queue<DateTimeOffset> _recent = new();
    private bool _muted;
    private DateTimeOffset _last;

    public WindowExceptionRoute Next(DateTimeOffset now)
    {
        lock (_sync)
        {
            if (_muted && now - _last >= Window)
            {
                _muted = false;
                _recent.Clear();
            }

            _last = now;
            if (_muted)
            {
                return WindowExceptionRoute.FileOnly;
            }

            while (_recent.Count > 0 && now - _recent.Peek() >= Window)
            {
                _recent.Dequeue();
            }

            _recent.Enqueue(now);
            if (_recent.Count <= Limit)
            {
                return WindowExceptionRoute.Panel;
            }

            _muted = true;
            return WindowExceptionRoute.FileOnlyWithNotice;
        }
    }
}
