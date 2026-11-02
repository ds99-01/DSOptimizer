using System;
using System.Collections.Generic;
using System.Xml;
using DSOptimizer.Core.Abstractions;
using DSOptimizer.Core.Errors;
using DSOptimizer.Core.Model;

namespace DSOptimizer.Core.Autostart;

/// <summary>
/// Odczyt autostartu zalogowanego konta przez abstrakcje: Run (konto, maszyna, 32-bit), foldery Autostart (konto, wspólny), zadania startowe
/// aplikacji ze Sklepu i pomiar Windows. Niczego nie zapisuje: z rejestru tylko <see cref="IRegistry.Read"/>, <see cref="IRegistry.ValueNames"/>
/// i <see cref="IRegistry.KeyStatus"/>, pliki tylko przez <see cref="IAutostartFiles"/>. Błąd jednego źródła = ostrzeżenie (także w dzienniku)
/// i pozostałe źródła czytane dalej.
/// </summary>
public sealed class AutostartScanner : IAutostartReader
{
    private const string Component = "Autostart";
    private const string DesktopIni = "desktop.ini";
    private const string ShortcutExtension = ".lnk";

    private static readonly AutostartSource[] RunSources = { AutostartSource.RunUser, AutostartSource.RunMachine, AutostartSource.Run32 };

    private readonly IRegistry _registry;
    private readonly IAutostartFiles _files;
    private readonly IStartupTaskReader _tasks;
    private readonly IStartupInfoSource _startupInfo;
    private readonly IReadOnlyDictionary<string, string> _machineEnvironment;
    private readonly ILog _log;

    public AutostartScanner(
        IRegistry registry,
        IAutostartFiles files,
        IStartupTaskReader tasks,
        IStartupInfoSource startupInfo,
        IReadOnlyDictionary<string, string> machineEnvironment,
        ILog log)
    {
        _registry = registry ?? throw new ArgumentNullException(nameof(registry));
        _files = files ?? throw new ArgumentNullException(nameof(files));
        _tasks = tasks ?? throw new ArgumentNullException(nameof(tasks));
        _startupInfo = startupInfo ?? throw new ArgumentNullException(nameof(startupInfo));
        _machineEnvironment = machineEnvironment ?? throw new ArgumentNullException(nameof(machineEnvironment));
        _log = log ?? throw new ArgumentNullException(nameof(log));
    }

    public AutostartReading Read(AutostartContext context)
    {
        if (context is null)
        {
            throw new ArgumentNullException(nameof(context));
        }

        var warnings = new List<string>();
        var environment = UserEnvironment.Load(_registry, context.Locations, _machineEnvironment, warnings);
        var entries = new List<AutostartEntry>();

        foreach (var source in RunSources)
        {
            if (source != AutostartSource.RunUser || context.UserHiveLoaded)
            {
                ReadRun(source, context.Locations, environment, entries, warnings);
            }
        }

        if (context.UserHiveLoaded)
        {
            ReadFolder(AutostartSource.FolderUser, context.Locations.UserStartupFolder, context.Locations, environment, entries, warnings);
        }

        ReadFolder(AutostartSource.FolderCommon, context.Locations.CommonStartupFolder, context.Locations, environment, entries, warnings);

        if (context.ClassesHiveLoaded)
        {
            ReadStore(context, entries, warnings);
        }

        var measuredAt = Measure(context.UserSid, environment, entries, warnings);
        foreach (var warning in warnings)
        {
            _log.Warn(Component, warning);
        }

        return new AutostartReading(entries.ToArray(), environment, measuredAt, warnings.ToArray());
    }

    private void ReadRun(AutostartSource source, AutostartLocations locations, UserEnvironment environment, List<AutostartEntry> entries, List<string> warnings)
    {
        var key = locations.RunKey(source);
        IReadOnlyList<string> names;
        try
        {
            names = _registry.ValueNames(key);
        }
        catch (OperationException ex)
        {
            warnings.Add(AutostartText.SourceUnreadable(key, ex.Message));
            return;
        }

        var approvedKey = locations.StartupApprovedKey(source);
        foreach (var name in names)
        {
            var read = _registry.Read(key, name);
            if (read.Status == ReadStatus.NotFound)
            {
                continue;
            }

            if (read.Status != ReadStatus.Found)
            {
                warnings.Add(AutostartText.SourceUnreadable(key + @"\" + name, read.Error ?? read.Status.ToString()));
                continue;
            }

            if (read.Value.Type is not (RegistryValueType.String or RegistryValueType.ExpandString))
            {
                warnings.Add(AutostartText.ValueSkipped(key, name));
                continue;
            }

            var command = (string)read.Value.Data!;
            var target = AutostartCommand.Parse(command, environment, _files.Probe);
            var details = DetailsOf(target);
            entries.Add(new AutostartEntry
            {
                Id = AutostartIds.For(source, name),
                Source = source,
                EntryName = name,
                Name = NameOf(details, name),
                Location = key,
                FileStatus = StatusOf(target),
                Switch = AutostartSwitchReader.Read(_registry, AutostartSwitchKind.StartupApproved, approvedKey, name, enabledInManifest: false),
                Publisher = Clean(details?.CompanyName),
                Command = command,
                FilePath = target.Definite ? target.FilePath : null,
                IsInterpreter = target.IsInterpreter,
            });
        }
    }

    private void ReadFolder(
        AutostartSource source,
        string rawFolder,
        AutostartLocations locations,
        UserEnvironment environment,
        List<AutostartEntry> entries,
        List<string> warnings)
    {
        if (!environment.TryExpand(rawFolder, out var folder))
        {
            return;
        }

        IReadOnlyList<string> names;
        try
        {
            names = _files.ListFolder(folder);
        }
        catch (OperationException ex)
        {
            warnings.Add(AutostartText.SourceUnreadable(folder, ex.Message));
            return;
        }

        var approvedKey = locations.StartupApprovedKey(source);
        foreach (var name in names)
        {
            if (string.Equals(name, DesktopIni, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            var path = folder.TrimEnd('\\') + @"\" + name;
            var target = name.EndsWith(ShortcutExtension, StringComparison.OrdinalIgnoreCase)
                ? FromShortcut(path, name, environment)
                : FromFile(path, name);
            entries.Add(new AutostartEntry
            {
                Id = AutostartIds.For(source, name),
                Source = source,
                EntryName = name,
                Name = target.Name,
                Location = folder,
                FileStatus = target.Status,
                Switch = AutostartSwitchReader.Read(_registry, AutostartSwitchKind.StartupApproved, approvedKey, name, enabledInManifest: false),
                Publisher = target.Publisher,
                Command = target.Command,
                FilePath = target.FilePath,
                IsInterpreter = target.IsInterpreter,
            });
        }
    }

    /// <summary>Skrót: cel przez <see cref="AutostartCommand.FromShortcut"/>; plik, który nie jest skrótem, albo błąd odczytu = plik nieustalony.</summary>
    private FolderTarget FromShortcut(string path, string name, UserEnvironment environment)
    {
        ShortcutInfo? shortcut = null;
        try
        {
            shortcut = _files.ReadShortcut(path);
        }
        catch (OperationException ex)
        {
            _log.Warn(Component, AutostartText.SourceUnreadable(path, ex.Message));
        }

        var fallbackName = name.Substring(0, name.Length - ShortcutExtension.Length);
        if (shortcut is null)
        {
            return new FolderTarget(NameOf(null, fallbackName), AutostartFileStatus.NotDetermined, null, null, null, false);
        }

        var target = AutostartCommand.FromShortcut(shortcut.TargetPath, shortcut.Arguments, shortcut.Advertised, environment, _files.Probe);
        var details = DetailsOf(target);
        var command = string.IsNullOrWhiteSpace(shortcut.TargetPath)
            ? null
            : "\"" + shortcut.TargetPath!.Trim() + "\"" + (target.Arguments is { } arguments ? " " + arguments : string.Empty);
        return new FolderTarget(
            NameOf(details, fallbackName),
            StatusOf(target),
            Clean(details?.CompanyName),
            command,
            target.Definite ? target.FilePath : null,
            target.IsInterpreter);
    }

    /// <summary>Zwykły plik w folderze Autostart: plik istnieje (jest na liście folderu), uruchamiany jest on sam.</summary>
    private FolderTarget FromFile(string path, string name)
    {
        var details = _files.Details(path);
        return new FolderTarget(NameOf(details, name), AutostartFileStatus.Exists, Clean(details?.CompanyName), null, path, false);
    }

    private void ReadStore(AutostartContext context, List<AutostartEntry> entries, List<string> warnings)
    {
        StartupTaskReading reading;
        try
        {
            reading = _tasks.Read(context.UserSid);
        }
        catch (OperationException ex)
        {
            warnings.Add(AutostartText.StoreUnreadable(ex.Message));
            return;
        }

        warnings.AddRange(reading.Warnings);
        foreach (var task in reading.Tasks)
        {
            string keyPath;
            try
            {
                keyPath = context.Locations.StartupTaskKey(task.PackageFamilyName, task.TaskId);
            }
            catch (ArgumentException ex)
            {
                _log.Warn(Component, AutostartText.SourceUnreadable(task.PackageFamilyName + "!" + task.TaskId, ex.Message));
                continue;
            }

            var filePath = string.IsNullOrWhiteSpace(task.InstallLocation) || string.IsNullOrWhiteSpace(task.Executable)
                ? null
                : task.InstallLocation.TrimEnd('\\') + @"\" + task.Executable!.TrimStart('\\');
            entries.Add(new AutostartEntry
            {
                Id = AutostartIds.For(AutostartSource.StartupTask, task.TaskId, task.PackageFamilyName),
                Source = AutostartSource.StartupTask,
                EntryName = task.TaskId,
                Name = Clean(task.DisplayName) ?? Clean(task.PackageName) ?? AutostartText.NoName,
                Location = task.PackageFamilyName,
                FileStatus = filePath is not null && _files.Probe(filePath) == FileProbe.Exists ? AutostartFileStatus.Exists : AutostartFileStatus.NotDetermined,
                Switch = AutostartSwitchReader.Read(_registry, AutostartSwitchKind.StartupTask, keyPath, StartupTaskValue.State, task.EnabledInManifest),
                Publisher = Clean(task.PublisherDisplayName),
                FilePath = filePath,
                PackageFamily = task.PackageFamilyName,
            });
        }
    }

    /// <summary>
    /// Pomiar Windows: pozycja włączona (albo stan nieznany) – najnowszy czytelny plik; wyłączona – pliki od najnowszego, pierwszy, w którym
    /// jest jej proces („ostatni start”). Pliki czytane leniwie, każdy najwyżej raz; plik nieczytelny albo w złym formacie jest pomijany z jednym
    /// ostrzeżeniem. Zwraca czas zapisu najnowszego czytelnego pliku.
    /// </summary>
    private DateTimeOffset? Measure(string userSid, UserEnvironment environment, List<AutostartEntry> entries, List<string> warnings)
    {
        IReadOnlyList<StartupInfoFile> files;
        try
        {
            files = _startupInfo.Files(userSid);
        }
        catch (OperationException ex)
        {
            _log.Warn(Component, AutostartText.SourceUnreadable("StartupInfo", ex.Message));
            files = Array.Empty<StartupInfoFile>();
        }

        if (files.Count == 0)
        {
            warnings.Add(AutostartText.NoMeasurement);
            return null;
        }

        var ordered = new List<StartupInfoFile>(files);
        ordered.Sort((a, b) => b.Number != a.Number ? b.Number.CompareTo(a.Number) : b.WrittenAt.CompareTo(a.WrittenAt));
        var cache = new Dictionary<StartupInfoFile, IReadOnlyList<StartupProcess>?>();

        StartupInfoFile? newest = null;
        IReadOnlyList<StartupProcess>? newestProcesses = null;
        foreach (var file in ordered)
        {
            newestProcesses = ProcessesOf(file, cache, warnings);
            if (newestProcesses is not null)
            {
                newest = file;
                break;
            }
        }

        if (newest is null || newestProcesses is null)
        {
            return null;
        }

        for (var i = 0; i < entries.Count; i++)
        {
            var entry = entries[i];
            if (entry.FilePath is null)
            {
                continue;
            }

            StartupMeasurement? measurement = null;
            if (entry.Switch.State == AutostartState.Disabled)
            {
                foreach (var file in ordered)
                {
                    var processes = ProcessesOf(file, cache, warnings);
                    measurement = processes is null ? null : StartupMeasurements.Find(entry, processes, file, fromEarlierStart: true, environment);
                    if (measurement is not null)
                    {
                        break;
                    }
                }
            }
            else
            {
                measurement = StartupMeasurements.Find(entry, newestProcesses, newest, fromEarlierStart: false, environment);
            }

            if (measurement is not null)
            {
                entries[i] = entry with { Measurement = measurement };
            }
        }

        return newest.WrittenAt;
    }

    private IReadOnlyList<StartupProcess>? ProcessesOf(StartupInfoFile file, Dictionary<StartupInfoFile, IReadOnlyList<StartupProcess>?> cache, List<string> warnings)
    {
        if (cache.TryGetValue(file, out var cached))
        {
            return cached;
        }

        IReadOnlyList<StartupProcess>? processes = null;
        try
        {
            processes = StartupInfoParser.Parse(_startupInfo.Read(file));
        }
        catch (OperationException ex)
        {
            warnings.Add(AutostartText.SourceUnreadable(file.Path, ex.Message));
        }
        catch (XmlException)
        {
            warnings.Add(AutostartText.MeasurementFileSkipped(file.Path));
        }

        cache[file] = processes;
        return processes;
    }

    /// <summary>Wersja pliku tylko dla istniejącego pliku, który nie jest interpreterem (opis <c>rundll32.exe</c> nie mówi nic o pozycji).</summary>
    private FileDetails? DetailsOf(CommandTarget target)
        => target.Probe == FileProbe.Exists && !target.IsInterpreter && target.FilePath is not null ? _files.Details(target.FilePath) : null;

    private static AutostartFileStatus StatusOf(CommandTarget target)
    {
        if (target.Probe == FileProbe.Exists)
        {
            return AutostartFileStatus.Exists;
        }

        return target.Definite && target.Probe == FileProbe.Missing ? AutostartFileStatus.Missing : AutostartFileStatus.NotDetermined;
    }

    private static string NameOf(FileDetails? details, string entryName)
        => Clean(details?.FileDescription) ?? Clean(details?.ProductName) ?? (entryName.Length == 0 ? AutostartText.NoName : entryName);

    private static string? Clean(string? text) => string.IsNullOrWhiteSpace(text) ? null : text!.Trim();

    /// <summary>Plik pozycji z folderu Autostart: nazwa, stan pliku, wydawca, polecenie skrótu, ścieżka i znacznik interpretera.</summary>
    private sealed record FolderTarget(string Name, AutostartFileStatus Status, string? Publisher, string? Command, string? FilePath, bool IsInterpreter);
}
