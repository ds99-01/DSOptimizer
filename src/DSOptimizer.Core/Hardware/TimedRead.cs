using System;
using System.Globalization;
using System.Threading;
using System.Threading.Tasks;
using DSOptimizer.Core.Abstractions;
using DSOptimizer.Core.Localization;

namespace DSOptimizer.Core.Hardware;

/// <summary>Pojedynczy odczyt z limitem czasu: zawieszenie albo wyjątek kończy się <see cref="ReadingState.Failed"/> i jednym ostrzeżeniem w logu. Nigdy nie rzuca.</summary>
public static class TimedRead
{
    /// <param name="read">Odczyt uruchamiany na własnym wątku.</param>
    /// <param name="timeout">Limit czasu odczytu.</param>
    /// <param name="log">Log ostrzeżeń.</param>
    /// <param name="partName">Nazwa części w tekście ostrzeżenia (w bieżącym języku).</param>
    /// <param name="component">Nazwa komponentu w logu (null = „Specyfikacja” w bieżącym języku).</param>
    public static async Task<Reading<T>> RunAsync<T>(Func<T> read, TimeSpan timeout, ILog log, string partName, string? component = null)
    {
        if (read is null) { throw new ArgumentNullException(nameof(read)); }
        if (log is null) { throw new ArgumentNullException(nameof(log)); }
        component ??= L.T("Specyfikacja");
        try
        {
            // Własny wątek na odczyt (LongRunning): blokujące WMI nie zagłodzi puli ani nie zje limitu czasu w kolejce.
            var work = Task.Factory.StartNew(read, CancellationToken.None, TaskCreationOptions.LongRunning, TaskScheduler.Default);
            using var cancelDelay = new CancellationTokenSource();
            var delay = Task.Delay(timeout, cancelDelay.Token);
            var first = await Task.WhenAny(work, delay).ConfigureAwait(false);
            if (ReferenceEquals(first, work))
            {
                cancelDelay.Cancel();
                return Reading<T>.Done(await work.ConfigureAwait(false));
            }

            // Odczytu nie da się przerwać (WMI); porzucamy go, a jego ewentualny wyjątek odbieramy, by nie został niezauważony.
            _ = work.ContinueWith(
                static t => _ = t.Exception,
                CancellationToken.None,
                TaskContinuationOptions.OnlyOnFaulted | TaskContinuationOptions.ExecuteSynchronously,
                TaskScheduler.Default);
            var seconds = timeout.TotalSeconds.ToString("0.##", CultureInfo.InvariantCulture);
            log.Warn(component, L.F("Odczyt „{0}” trwał ponad {1} s – pokazuję „brak danych”.", partName, seconds));
            return Reading<T>.Failed;
        }
        catch (Exception ex)
        {
            log.Warn(component, L.F("Odczyt „{0}” nie powiódł się: {1}", partName, ex.Message));
            return Reading<T>.Failed;
        }
    }
}
