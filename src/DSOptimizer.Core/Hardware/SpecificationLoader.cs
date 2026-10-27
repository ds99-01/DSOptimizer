using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using DSOptimizer.Core.Abstractions;

namespace DSOptimizer.Core.Hardware;

/// <summary>
/// Czyta 13 części sprzętu równolegle, każdą z własnym limitem czasu. Zawieszony odczyt (np. uszkodzone WMI)
/// kończy się jako <see cref="ReadingState.Failed"/> i jednym ostrzeżeniem w logu; pozostałe części wypełniają się dalej.
/// </summary>
public sealed class SpecificationLoader
{
    private const string Component = "Specyfikacja";

    public static readonly TimeSpan DefaultTimeout = TimeSpan.FromSeconds(10);

    private readonly IHardwareReader _reader;
    private readonly ILog _log;
    private readonly TimeSpan _timeout;

    public SpecificationLoader(IHardwareReader reader, ILog log, TimeSpan timeout)
    {
        _reader = reader ?? throw new ArgumentNullException(nameof(reader));
        _log = log ?? throw new ArgumentNullException(nameof(log));
        _timeout = timeout;
    }

    public static string PartName(HardwarePart part) => part switch
    {
        HardwarePart.Processor => "Procesor",
        HardwarePart.Memory => "Pamięć",
        HardwarePart.Board => "Płyta główna",
        HardwarePart.Graphics => "Karta graficzna",
        HardwarePart.Displays => "Monitory",
        HardwarePart.Sound => "Dźwięk",
        HardwarePart.Disks => "Dyski",
        HardwarePart.Network => "Sieć",
        HardwarePart.Windows => "Windows",
        HardwarePart.Tpm => "TPM",
        HardwarePart.SecureBoot => "Secure Boot",
        HardwarePart.Vbs => "VBS i HVCI",
        HardwarePart.BitLocker => "BitLocker",
        _ => part.ToString(),
    };

    /// <summary>
    /// Uruchamia wszystkie odczyty. <paramref name="onUpdate"/> dostaje skumulowany snapshot po każdej zakończonej części
    /// (wywoływany pod blokadą, więc ma być tani). Nigdy nie rzuca.
    /// </summary>
    public async Task<HardwareSnapshot> LoadAsync(Action<HardwareSnapshot> onUpdate)
    {
        var run = new LoadRun(onUpdate ?? (static _ => { }), _log);

        await Task.WhenAll(
            RunPartAsync(run, HardwarePart.Processor, _reader.ReadProcessor, static (s, r) => s with { Processor = r }),
            RunPartAsync(run, HardwarePart.Memory, _reader.ReadMemory, static (s, r) => s with { Memory = r }),
            RunPartAsync(run, HardwarePart.Board, _reader.ReadBoard, static (s, r) => s with { Board = r }),
            RunPartAsync(run, HardwarePart.Graphics, _reader.ReadGraphics, static (s, r) => s with { Graphics = r }),
            RunPartAsync(run, HardwarePart.Displays, _reader.ReadDisplays, static (s, r) => s with { Displays = r }),
            RunPartAsync(run, HardwarePart.Sound, _reader.ReadSound, static (s, r) => s with { Sound = r }),
            RunPartAsync(run, HardwarePart.Disks, _reader.ReadDisks, static (s, r) => s with { Disks = r }),
            RunPartAsync(run, HardwarePart.Network, _reader.ReadNetwork, static (s, r) => s with { Network = r }),
            RunPartAsync(run, HardwarePart.Windows, _reader.ReadWindows, static (s, r) => s with { Windows = r }),
            RunPartAsync(run, HardwarePart.Tpm, _reader.ReadTpm, static (s, r) => s with { Tpm = r }),
            RunPartAsync(run, HardwarePart.SecureBoot, _reader.ReadSecureBoot, static (s, r) => s with { SecureBoot = r }),
            RunPartAsync(run, HardwarePart.Vbs, _reader.ReadVbs, static (s, r) => s with { Vbs = r }),
            RunPartAsync(run, HardwarePart.BitLocker, _reader.ReadBitLocker, static (s, r) => s with { BitLocker = r })).ConfigureAwait(false);

        return run.Current;
    }

    private async Task RunPartAsync<T>(
        LoadRun run,
        HardwarePart part,
        Func<T> read,
        Func<HardwareSnapshot, Reading<T>, HardwareSnapshot> assign)
    {
        var reading = await TimedRead.RunAsync(read, _timeout, _log, PartName(part), Component).ConfigureAwait(false);
        run.Publish(assign, reading);
    }

    /// <summary>Stan jednego wywołania <see cref="LoadAsync"/>; nakładające się odświeżenia nie dzielą niczego.</summary>
    private sealed class LoadRun
    {
        private readonly object _gate = new();
        private readonly Action<HardwareSnapshot> _onUpdate;
        private readonly ILog _log;
        private HardwareSnapshot _snapshot = new();
        private bool _onUpdateFailed;

        public LoadRun(Action<HardwareSnapshot> onUpdate, ILog log)
        {
            _onUpdate = onUpdate;
            _log = log;
        }

        public HardwareSnapshot Current
        {
            get
            {
                lock (_gate)
                {
                    return _snapshot;
                }
            }
        }

        public void Publish<T>(Func<HardwareSnapshot, Reading<T>, HardwareSnapshot> assign, Reading<T> reading)
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
                        _log.Warn(Component, $"Odświeżenie ekranu specyfikacji nie powiodło się: {ex.Message}");
                    }
                }
            }
        }
    }
}
