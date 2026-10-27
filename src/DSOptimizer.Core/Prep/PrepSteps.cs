using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using DSOptimizer.Core.Hardware;

namespace DSOptimizer.Core.Prep;

public enum PrepStepKind
{
    Computer,
    WindowsUpdate,
    OptionalUpdates,
    Chipset,
    Graphics,
    Support,
    Devices,
}

/// <summary>Wygląd wartości: zwykła, wymaga uwagi (kolor ostrzeżenia), problem (kolor błędu).</summary>
public enum PrepTone
{
    Normal,
    Attention,
    Problem,
}

/// <summary>Link tekstowy; <see cref="Url"/> pochodzi wyłącznie z <see cref="PrepLinks"/>.</summary>
public sealed record PrepAction(string Label, string Url);

/// <summary>
/// Wiersz kroku. <see cref="Detail"/> = druga linia wartości tekstem drugorzędnym (urządzenia w kroku 7), <see cref="Action"/> = link
/// w wierszu (urządzenie w kroku 7 i „Szukaj sterownika” karty w kroku 5).
/// </summary>
public sealed record PrepLine(string Label, string Value, PrepTone Tone, string? Detail = null, PrepAction? Action = null);

/// <summary>Krok strony; <see cref="Actions"/> to linki przy nagłówku kroku.</summary>
public sealed record PrepStep(PrepStepKind Kind, int Number, string Title, IReadOnlyList<PrepLine> Lines, IReadOnlyList<PrepAction> Actions, string? Note);

/// <summary>
/// Składa 7 kroków strony „Przygotowanie” z <see cref="PrepSnapshot"/>. Część w toku = <see cref="PrepText.Loading"/>,
/// błąd = <see cref="PrepText.Failed"/>; linki zależne od części pojawiają się dopiero po jej odczycie. Linki grafiki i wsparcia
/// zależą też od płyty (maszyna wirtualna nie dostaje żadnego). Adresy wyłącznie z <see cref="PrepLinks"/>.
/// </summary>
public static class PrepSteps
{
    private static readonly CultureInfo Invariant = CultureInfo.InvariantCulture;

    private static readonly PrepAction[] NoActions = Array.Empty<PrepAction>();

    public static IReadOnlyList<PrepStep> Build(PrepSnapshot snapshot, string systemDrive)
        => Build(snapshot, systemDrive, PrepLinks.SettingsOpenable);

    /// <summary>Wariant z jawną flagą Ustawień, żeby testy obejmowały obie gałęzie.</summary>
    public static IReadOnlyList<PrepStep> Build(PrepSnapshot snapshot, string systemDrive, bool settingsOpenable)
    {
        if (snapshot is null)
        {
            throw new ArgumentNullException(nameof(snapshot));
        }

        // null = płyta jeszcze nieodczytana; błąd odczytu płyty = traktuj jak prawdziwy komputer.
        bool? isVm = snapshot.Board.State switch
        {
            ReadingState.Pending => null,
            ReadingState.Done => PrepRules.IsVirtualMachine(snapshot.Board.Value!),
            _ => false,
        };

        return new[]
        {
            Computer(snapshot, systemDrive),
            Settings(PrepStepKind.WindowsUpdate, 2, PrepText.WindowsUpdateTitle, settingsOpenable,
                PrepText.OpenWindowsUpdate, PrepLinks.WindowsUpdate, PrepText.WindowsUpdateNote),
            Settings(PrepStepKind.OptionalUpdates, 3, PrepText.OptionalUpdatesTitle, settingsOpenable,
                PrepText.OpenOptionalUpdates, PrepLinks.OptionalUpdates, PrepText.OptionalUpdatesNote),
            Chipset(snapshot.Product),
            Graphics(snapshot.Graphics, isVm),
            Support(snapshot.Board, snapshot.Product, isVm),
            Devices(snapshot.Devices),
        };
    }

    // ---------- krok 1 ----------

    private static PrepStep Computer(PrepSnapshot snapshot, string systemDrive)
    {
        var board = snapshot.Board;
        var lines = new List<PrepLine>
        {
            FromReading(PrepText.ModelLabel, board, b => new PrepLine(PrepText.ModelLabel, ModelText(b, snapshot.Product), PrepTone.Normal)),
            FromReading(PrepText.BiosLabel, board, b => new PrepLine(PrepText.BiosLabel, BiosText(b), PrepTone.Normal)),
        };

        var bitLocker = snapshot.BitLocker.State == ReadingState.Done
            ? PrepRules.SystemDrive(snapshot.BitLocker.Value!, systemDrive)
            : (BitLockerState?)null;
        lines.Add(FromReading(PrepText.BitLockerLabel, snapshot.BitLocker, _ => BitLockerLine(bitLocker!.Value)));
        lines.Add(FromReading(PrepText.ActivationLabel, snapshot.Activation, a => ActivationLine(PrepRules.Activation(a))));

        var note = bitLocker == BitLockerState.On ? PrepText.BitLockerOnNote : null;
        return new PrepStep(PrepStepKind.Computer, 1, PrepText.ComputerTitle, lines, NoActions, note);
    }

    /// <summary>Lenovo: „Lenovo &lt;Version&gt; (&lt;SystemModel&gt;)”; zastępcze nazwy systemu = płyta główna.</summary>
    private static string ModelText(BoardInfo board, Reading<PrepProduct> product)
    {
        var (manufacturer, model, fromBoard) = MachineNames(board);
        var oem = fromBoard ? null : PrepLinks.Oem(manufacturer);
        var version = product.State == ReadingState.Done ? Clean(product.Value!.Version) : null;
        if (oem is { Vendor: "Lenovo" } && version is not null && model is not null)
        {
            var name = version.StartsWith(oem.Vendor, StringComparison.OrdinalIgnoreCase) ? version : oem.Vendor + " " + version;
            return name + " (" + model + ")";
        }

        return OrUnknown(Words(manufacturer, model));
    }

    private static string BiosText(BoardInfo board)
        => OrUnknown(Comma(Clean(board.BiosVersion), board.BiosDate?.ToString("dd.MM.yyyy", Invariant)));

    private static PrepLine BitLockerLine(BitLockerState state)
    {
        var (value, tone) = state switch
        {
            BitLockerState.On => (PrepText.BitLockerOn, PrepTone.Attention),
            BitLockerState.Encrypting => (PrepText.BitLockerEncrypting, PrepTone.Attention),
            BitLockerState.EncryptedProtectionOff => (PrepText.BitLockerEncryptedProtectionOff, PrepTone.Attention),
            BitLockerState.Decrypting => (PrepText.BitLockerDecrypting, PrepTone.Normal),
            BitLockerState.Off => (PrepText.BitLockerOff, PrepTone.Normal),
            _ => (PrepText.Unknown, PrepTone.Normal),
        };
        return new PrepLine(PrepText.BitLockerLabel, value, tone);
    }

    private static PrepLine ActivationLine(ActivationStatus status)
    {
        var (value, tone) = status switch
        {
            ActivationStatus.Licensed => (PrepText.ActivationLicensed, PrepTone.Normal),
            ActivationStatus.Unlicensed => (PrepText.ActivationUnlicensed, PrepTone.Problem),
            ActivationStatus.Grace => (PrepText.ActivationGrace, PrepTone.Attention),
            ActivationStatus.Notification => (PrepText.ActivationNotification, PrepTone.Problem),
            ActivationStatus.NoKey => (PrepText.ActivationNoKey, PrepTone.Problem),
            _ => (PrepText.Unknown, PrepTone.Normal),
        };
        return new PrepLine(PrepText.ActivationLabel, value, tone);
    }

    // ---------- kroki 2–3 ----------

    private static PrepStep Settings(PrepStepKind kind, int number, string title, bool settingsOpenable, string label, string url, string note)
        => settingsOpenable
            ? new PrepStep(kind, number, title, Array.Empty<PrepLine>(), new[] { new PrepAction(label, url) }, null)
            : new PrepStep(kind, number, title, Array.Empty<PrepLine>(), NoActions, note);

    // ---------- krok 4 ----------

    private static PrepStep Chipset(Reading<PrepProduct> product)
    {
        if (product.State != ReadingState.Done)
        {
            return new PrepStep(PrepStepKind.Chipset, 4, PrepText.ChipsetTitle, new[] { StateLine(PrepText.ProcessorLabel, product.State) }, NoActions, null);
        }

        var raw = product.Value!.CpuManufacturer;
        var link = PrepLinks.Chipset(PrepRules.CpuVendorOf(raw));
        if (link is null)
        {
            var line = new PrepLine(PrepText.ProcessorLabel, OrUnknown(Clean(raw)), PrepTone.Normal);
            return new PrepStep(PrepStepKind.Chipset, 4, PrepText.ChipsetTitle, new[] { line }, NoActions, PrepText.ChipsetUnknownNote);
        }

        return new PrepStep(
            PrepStepKind.Chipset,
            4,
            PrepText.ChipsetTitle,
            new[] { new PrepLine(PrepText.ProcessorLabel, link.Vendor, PrepTone.Normal) },
            new[] { new PrepAction(PrepText.DriversPage(link.Vendor), link.Url) },
            null);
    }

    // ---------- krok 5 ----------

    private static PrepStep Graphics(Reading<IReadOnlyList<PrepGpu>> graphics, bool? isVm)
    {
        if (graphics.State != ReadingState.Done)
        {
            return new PrepStep(PrepStepKind.Graphics, 5, PrepText.GraphicsTitle, new[] { StateLine(PrepText.GraphicsCardsLabel, graphics.State) }, NoActions, null);
        }

        var gpus = graphics.Value!.Where(g => g is not null).ToList();
        if (gpus.Count == 0)
        {
            var none = new PrepLine(PrepText.GraphicsCardsLabel, PrepText.NoGraphicsCards, PrepTone.Normal);
            return new PrepStep(PrepStepKind.Graphics, 5, PrepText.GraphicsTitle, new[] { none }, NoActions, null);
        }

        // Maszyna wirtualna (albo płyta jeszcze nieznana): bez linków.
        var linksAllowed = isVm == false;
        var lines = gpus.Select(g => GpuLine(g, linksAllowed)).ToList();
        var actions = linksAllowed ? VendorActions(gpus) : new List<PrepAction>();
        return new PrepStep(PrepStepKind.Graphics, 5, PrepText.GraphicsTitle, lines, actions, null);
    }

    private static PrepLine GpuLine(PrepGpu gpu, bool linksAllowed)
    {
        var action = linksAllowed ? SearchDriverAction(gpu) : null;
        if (PrepRules.IsBasicDisplay(gpu))
        {
            var vendor = PrepLinks.Graphics(PrepRules.GpuVendorOf(gpu.PnpDeviceId))?.Vendor;
            var label = vendor is null ? PrepText.GraphicsCardLabel : PrepText.CardOf(vendor);
            return new PrepLine(label, PrepText.BasicDisplayValue, PrepTone.Problem, Action: action);
        }

        var version = Clean(gpu.DriverVersion);
        var value = Comma(
            version is null ? null : "sterownik " + version,
            gpu.DriverDate?.ToString("dd.MM.yyyy", Invariant),
            Clean(gpu.DriverProvider));
        return new PrepLine(Clean(gpu.Name) ?? PrepText.GraphicsCardLabel, OrUnknown(value), PrepTone.Normal, Action: action);
    }

    /// <summary>
    /// Link w wierszu karty: tylko karta PCI o nieznanym producencie z <c>VEN_</c> (wyszukiwanie po identyfikatorze).
    /// Karty spoza PCI (Remote Display, IDD, DisplayLink, VMBUS) mają własny sterownik, a ich identyfikator instancji nic nie znajdzie.
    /// </summary>
    private static PrepAction? SearchDriverAction(PrepGpu gpu)
    {
        var id = gpu.PnpDeviceId?.Trim();
        if (id is null
            || !id.StartsWith(@"PCI\", StringComparison.OrdinalIgnoreCase)
            || PrepLinks.Graphics(PrepRules.GpuVendorOf(id)) is not null)
        {
            return null;
        }

        var query = HardwareIdQuery.For(new[] { id });
        return query is null ? null : new PrepAction(PrepText.SearchDriver, PrepLinks.Search(query));
    }

    /// <summary>Linki przy nagłówku kroku: jeden na znanego producenta karty, bez duplikatów.</summary>
    private static List<PrepAction> VendorActions(IEnumerable<PrepGpu> gpus)
    {
        var actions = new List<PrepAction>();
        foreach (var gpu in gpus)
        {
            var link = PrepLinks.Graphics(PrepRules.GpuVendorOf(gpu.PnpDeviceId));
            if (link is not null && !actions.Any(a => string.Equals(a.Url, link.Url, StringComparison.Ordinal)))
            {
                actions.Add(new PrepAction(PrepText.DriversPage(link.Vendor), link.Url));
            }
        }

        return actions;
    }

    // ---------- krok 6 ----------

    private static PrepStep Support(Reading<BoardInfo> board, Reading<PrepProduct> product, bool? isVm)
    {
        var lines = new List<PrepLine>();
        var actions = new List<PrepAction>();
        string? note = null;

        if (board.State != ReadingState.Done)
        {
            lines.Add(StateLine(PrepText.ManufacturerLabel, board.State));
        }
        else
        {
            // Składak (zastępcze nazwy systemu) szuka po płycie, nawet gdy płyta jest np. ASUS; VM przed Surface (ten sam producent).
            var (manufacturer, model, fromBoard) = MachineNames(board.Value!);
            var oem = isVm == true || fromBoard ? null : PrepLinks.Oem(manufacturer);
            lines.Add(new PrepLine(PrepText.ManufacturerLabel, oem?.Vendor ?? OrUnknown(manufacturer), PrepTone.Normal));

            if (isVm == true)
            {
                note = PrepText.VirtualMachineNote;
            }
            else if (oem is not null)
            {
                actions.Add(new PrepAction(PrepText.SupportPage(oem.Vendor), oem.Url));
            }
            else if (Words(manufacturer, model) is { } names)
            {
                actions.Add(new PrepAction(PrepText.SearchSupport, PrepLinks.Search(names + " support")));
            }
        }

        if (product.State != ReadingState.Done)
        {
            lines.Add(StateLine(PrepText.SerialLabel, product.State));
        }
        else if (Clean(product.Value!.SerialNumber) is { } serial)
        {
            lines.Add(new PrepLine(PrepText.SerialLabel, serial, PrepTone.Normal));
        }

        return new PrepStep(PrepStepKind.Support, 6, PrepText.SupportTitle, lines, actions, note);
    }

    // ---------- krok 7 ----------

    private static PrepStep Devices(Reading<IReadOnlyList<ProblemDevice>> devices)
    {
        if (devices.State != ReadingState.Done)
        {
            return new PrepStep(PrepStepKind.Devices, 7, PrepText.DevicesTitle, new[] { StateLine(PrepText.DevicesLabel, devices.State) }, NoActions, null);
        }

        var lines = devices.Value!.Where(d => d is not null).Select(DeviceLine).ToList();
        if (lines.Count == 0)
        {
            lines.Add(new PrepLine(string.Empty, PrepText.AllDevicesOk, PrepTone.Normal));
        }

        return new PrepStep(PrepStepKind.Devices, 7, PrepText.DevicesTitle, lines, NoActions, null);
    }

    private static PrepLine DeviceLine(ProblemDevice device)
    {
        var reason = device.ErrorCode switch
        {
            28 => PrepText.NoDriver,
            22 => PrepText.Disabled,
            _ => PrepText.ProblemCode(device.ErrorCode),
        };
        var query = HardwareIdQuery.For(device.HardwareIds ?? Array.Empty<string>());
        // Wyłączone urządzenie (kod 22) ma sterownik – szukanie w Google nic mu nie da, zostaje sama linia z identyfikatorem.
        var action = query is null || device.ErrorCode == 22 ? null : new PrepAction(PrepText.SearchGoogle, PrepLinks.Search(query));
        return new PrepLine(Clean(device.Name) ?? PrepText.UnknownDevice, reason, PrepTone.Problem, query, action);
    }

    // ---------- pomocnicze ----------

    private static PrepLine FromReading<T>(string label, Reading<T> reading, Func<T, PrepLine> done)
        => reading.State == ReadingState.Done ? done(reading.Value!) : StateLine(label, reading.State);

    private static PrepLine StateLine(string label, ReadingState state)
        => new(label, state == ReadingState.Pending ? PrepText.Loading : PrepText.Failed, PrepTone.Normal);

    /// <summary>Producent i model komputera; gdy któraś nazwa systemu jest zastępcza albo pusta – producent i model płyty (<c>FromBoard</c>).</summary>
    private static (string? Manufacturer, string? Model, bool FromBoard) MachineNames(BoardInfo board)
    {
        var manufacturer = Clean(board.SystemManufacturer);
        var model = Clean(board.SystemModel);
        if (manufacturer is not null && model is not null)
        {
            return (manufacturer, model, false);
        }

        return (Clean(board.Manufacturer), Clean(board.Product), true);
    }

    /// <summary>Napis po <c>Trim</c>; pusty albo zastępczy (<see cref="SpecificationFormatter.IsPlaceholder"/>) = null.</summary>
    private static string? Clean(string? value)
    {
        var text = value?.Trim();
        return string.IsNullOrEmpty(text) || SpecificationFormatter.IsPlaceholder(text) ? null : text;
    }

    private static string? Words(params string?[] parts) => Join(" ", parts);

    private static string? Comma(params string?[] parts) => Join(", ", parts);

    private static string? Join(string separator, string?[] parts)
    {
        var text = string.Join(separator, parts.Where(p => !string.IsNullOrEmpty(p)));
        return text.Length == 0 ? null : text;
    }

    private static string OrUnknown(string? value) => value ?? PrepText.Unknown;
}
