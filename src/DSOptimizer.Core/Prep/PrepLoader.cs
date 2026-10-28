using System;
using System.Threading.Tasks;
using DSOptimizer.Core.Abstractions;
using DSOptimizer.Core.Hardware;
using DSOptimizer.Core.Localization;

namespace DSOptimizer.Core.Prep;

/// <summary>
/// Czyta 6 części strony „Przygotowanie” równolegle, każdą z własnym limitem czasu (jak <see cref="SpecificationLoader"/>).
/// Płyta i BitLocker idą przez <see cref="IHardwareReader"/>, reszta przez <see cref="IPrepReader"/>.
/// </summary>
public sealed class PrepLoader
{
    /// <summary>Nazwa komponentu w logu (tłumaczona, jak nazwa strony).</summary>
    private static string Component => L.T("Przygotowanie");

    public static readonly TimeSpan DefaultTimeout = TimeSpan.FromSeconds(10);

    private readonly IHardwareReader _hardware;
    private readonly IPrepReader _prep;
    private readonly ILog _log;
    private readonly TimeSpan _timeout;

    public PrepLoader(IHardwareReader hardware, IPrepReader prep, ILog log, TimeSpan timeout)
    {
        _hardware = hardware ?? throw new ArgumentNullException(nameof(hardware));
        _prep = prep ?? throw new ArgumentNullException(nameof(prep));
        _log = log ?? throw new ArgumentNullException(nameof(log));
        _timeout = timeout;
    }

    /// <summary>
    /// Uruchamia wszystkie odczyty. <paramref name="onUpdate"/> dostaje skumulowany snapshot po każdej zakończonej części
    /// (wywoływany pod blokadą, więc ma być tani). Nigdy nie rzuca.
    /// </summary>
    public async Task<PrepSnapshot> LoadAsync(Action<PrepSnapshot> onUpdate)
    {
        var run = new LoadRun(onUpdate ?? (static _ => { }), _log);

        await Task.WhenAll(
            RunPartAsync(run, L.T("Komputer"), _hardware.ReadBoard, static (s, r) => s with { Board = r }),
            RunPartAsync(run, L.T("Produkt"), _prep.ReadProduct, static (s, r) => s with { Product = r }),
            RunPartAsync(run, "BitLocker", _hardware.ReadBitLocker, static (s, r) => s with { BitLocker = r }),
            RunPartAsync(run, L.T("Aktywacja"), _prep.ReadActivation, static (s, r) => s with { Activation = r }),
            RunPartAsync(run, L.T("Grafika"), _prep.ReadGraphics, static (s, r) => s with { Graphics = r }),
            RunPartAsync(run, L.T("Urządzenia"), _prep.ReadProblemDevices, static (s, r) => s with { Devices = r })).ConfigureAwait(false);

        return run.Current;
    }

    private async Task RunPartAsync<T>(
        LoadRun run,
        string partName,
        Func<T> read,
        Func<PrepSnapshot, Reading<T>, PrepSnapshot> assign)
    {
        var reading = await TimedRead.RunAsync(read, _timeout, _log, partName, Component).ConfigureAwait(false);
        run.Publish(assign, reading);
    }

    /// <summary>Stan jednego wywołania <see cref="LoadAsync"/>; nakładające się odświeżenia nie dzielą niczego.</summary>
    private sealed class LoadRun
    {
        private readonly object _gate = new();
        private readonly Action<PrepSnapshot> _onUpdate;
        private readonly ILog _log;
        private PrepSnapshot _snapshot = new();
        private bool _onUpdateFailed;

        public LoadRun(Action<PrepSnapshot> onUpdate, ILog log)
        {
            _onUpdate = onUpdate;
            _log = log;
        }

        public PrepSnapshot Current
        {
            get
            {
                lock (_gate)
                {
                    return _snapshot;
                }
            }
        }

        public void Publish<T>(Func<PrepSnapshot, Reading<T>, PrepSnapshot> assign, Reading<T> reading)
        {
            lock (_gate)
            {
                _snapshot = assign(_snapshot, reading);
                try
                {
                    _onUpdate(_snapshot);
                }
                catch (Exception ex)
                {
                    if (!_onUpdateFailed)
                    {
                        _onUpdateFailed = true;
                        _log.Warn(Component, L.F("Odświeżenie ekranu przygotowania nie powiodło się: {0}", ex.Message));
                    }
                }
            }
        }
    }
}
