using System;
using System.Collections.Generic;

namespace DSOptimizer.Core.Autostart;

/// <summary>
/// Plik wykonywalny pozycji: <see cref="Missing"/> tylko przy pewnym braku (ścieżka w pełni rozwinięta zmiennymi konta, odpowiedź
/// „nie istnieje”), każdy inny przypadek bez potwierdzonego pliku = <see cref="NotDetermined"/> („nie ustalono pliku”).
/// </summary>
public enum AutostartFileStatus
{
    Exists,
    Missing,
    NotDetermined,
}

/// <summary>Rodzaj przełącznika pozycji: wartość <c>StartupApproved</c> (Run, foldery) albo stan zadania startowego Sklepu.</summary>
public enum AutostartSwitchKind
{
    StartupApproved,
    StartupTask,
}

/// <summary>
/// Odczytany przełącznik pozycji. Dla <see cref="AutostartSwitchKind.StartupApproved"/>: klucz <c>StartupApproved\…</c> i nazwa wartości
/// (nazwa wartości Run albo nazwa pliku z folderu z rozszerzeniem). Dla <see cref="AutostartSwitchKind.StartupTask"/>: <see cref="KeyPath"/> =
/// klucz zadania, <see cref="ValueName"/> = <c>State</c>, <see cref="TaskState"/> = odczytany numer stanu, <see cref="KeyExists"/> = klucz
/// zadania istnieje (bez znaczenia, gdy <see cref="Format"/> = <see cref="AutostartSwitchFormat.Unreadable"/>), <see cref="EnabledInManifest"/>
/// = stan domyślny z manifestu (potrzebny do świeżego odczytu, <see cref="AutostartSwitchReader.Reread"/>). Dla <c>StartupApproved</c> oba
/// ostatnie pola = false. W programie tworzy go <see cref="AutostartSwitchReader"/>.
/// </summary>
public sealed record AutostartSwitch(
    AutostartSwitchKind Kind,
    string KeyPath,
    string ValueName,
    AutostartSwitchFormat Format,
    AutostartState State,
    DateTimeOffset? DisabledAt,
    uint? TaskState,
    bool KeyExists = false,
    bool EnabledInManifest = false);

/// <summary>
/// Pomiar Windows procesu pozycji: czas procesora samego procesu (µs, bez potomnych), odczyt z dysku (B), czas zapisu pliku pomiaru.
/// <see cref="FromEarlierStart"/> = pozycja jest wyłączona, a liczby pochodzą z ostatniego rozruchu, w którym startowała.
/// </summary>
public sealed record StartupMeasurement(long CpuMicroseconds, long DiskBytes, DateTimeOffset MeasuredAt, bool FromEarlierStart);

/// <summary>Jedna pozycja autostartu zalogowanego konta (wiersz listy przed oceną).</summary>
public sealed record AutostartEntry
{
    /// <summary>Id pozycji (<see cref="AutostartIds"/>).</summary>
    public required string Id { get; init; }

    public required AutostartSource Source { get; init; }

    /// <summary>Nazwa wartości Run, nazwa pliku w folderze (z rozszerzeniem) albo TaskId zadania startowego; może być pusta (wartość bez nazwy).</summary>
    public required string EntryName { get; init; }

    /// <summary>Nazwa do wyświetlenia: opis pliku → nazwa produktu → nazwa wpisu (bez <c>.lnk</c>); Sklep: nazwa zadania → nazwa pakietu.</summary>
    public required string Name { get; init; }

    /// <summary>Klucz Run, rozwinięty folder Autostart albo rodzina pakietu.</summary>
    public required string Location { get; init; }

    public required AutostartFileStatus FileStatus { get; init; }

    public required AutostartSwitch Switch { get; init; }

    /// <summary><c>CompanyName</c> z wersji pliku; Sklep: wydawca pakietu.</summary>
    public string? Publisher { get; init; }

    /// <summary>Polecenie z Run (dosłownie) albo <c>"&lt;cel&gt;" &lt;argumenty&gt;</c> skrótu (cel surowy); plik w folderze i Sklep = null.</summary>
    public string? Command { get; init; }

    /// <summary>Pełna, rozwinięta ścieżka pliku wykonywalnego; null, gdy pliku nie da się ustalić.</summary>
    public string? FilePath { get; init; }

    /// <summary>Plik to interpreter albo host (<see cref="AutostartCommand.Interpreters"/>): pomiar tylko przy zgodnej linii poleceń.</summary>
    public bool IsInterpreter { get; init; }

    public string? PackageFamily { get; init; }

    public StartupMeasurement? Measurement { get; init; }

    public AutostartScope Scope => AutostartSources.ScopeOf(Source);
}

/// <summary>
/// Wynik odczytu autostartu: pozycje w kolejności źródeł (Run konta, Run maszyny, Run 32-bit, folder konta, folder wspólny, Sklep; w źródle
/// kolejność systemu), zmienne konta użyte do rozwinięcia ścieżek, czas zapisu najnowszego pliku pomiaru Windows i ostrzeżenia odczytu.
/// </summary>
public sealed record AutostartReading(
    IReadOnlyList<AutostartEntry> Entries,
    UserEnvironment Environment,
    DateTimeOffset? MeasuredAt,
    IReadOnlyList<string> Warnings);
