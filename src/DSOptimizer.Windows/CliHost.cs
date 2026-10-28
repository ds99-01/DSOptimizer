using System;
using System.Collections.Generic;
using System.IO;
using System.Security;
using System.Security.Principal;
using System.Text;
using DSOptimizer.Catalog;
using DSOptimizer.Core.Abstractions;
using DSOptimizer.Core.Apps;
using DSOptimizer.Core.Catalog;
using DSOptimizer.Core.Cli;
using DSOptimizer.Core.Install;
using DSOptimizer.Core.Localization;
using DSOptimizer.Windows.Apps;

namespace DSOptimizer.Windows;

/// <summary>
/// Host trybu CLI: parsowanie argumentów, katalog, composition root, wykonanie i zapis raportu do <c>--out</c>.
/// Błędy argumentów i katalogu nie dotykają korzenia. Raport trafia do pliku UTF-8 bez BOM (patrz <see cref="WriteReport"/>).
/// </summary>
public static class CliHost
{
    public static int Run(IReadOnlyList<string> args, SecureRoot root, string? logMirrorDirectory)
        => Run(args, root, logMirrorDirectory, p => File.ReadAllText(p, Encoding.UTF8), () => BuiltInCatalog.Sources);

    /// <summary>
    /// Wbudowane źródła katalogu przychodzą jako delegat, żeby ich odczyt (zasoby osadzone) był wykonany w bloku <c>try</c>
    /// i tylko wtedy, gdy nie podano <c>--catalog</c>. <paramref name="serialize"/> to szew testowy (domyślnie serializer raportu).
    /// <paramref name="installServices"/> to szew testowy usług instalacji (domyślnie <see cref="CreateInstallServices"/>: prawdziwy
    /// winget przez alias konta); testy podają fałszywy runner, więc żaden winget nie startuje.
    /// </summary>
    internal static int Run(IReadOnlyList<string> args, SecureRoot root, string? logMirrorDirectory,
                            Func<string, string> readAllText, Func<IReadOnlyList<CatalogSource>> builtInSources,
                            Func<CliReport, string>? serialize = null,
                            Func<AppComposition, AppsInstallCatalog, CliInstallServices>? installServices = null)
    {
        if (args is null)
        {
            throw new ArgumentNullException(nameof(args));
        }

        if (root is null)
        {
            throw new ArgumentNullException(nameof(root));
        }

        serialize ??= CliReportSerializer.Serialize;
        installServices ??= CreateInstallServices;
        var startedAt = DateTimeOffset.UtcNow;

        // Cały przebieg, od parsowania po zapis raportu, idzie w tym języku; domyślny język procesu ustawia Program.Main.
        using (L.Use(ResolveLanguage(args, root)))
        {
            return RunInLanguage(args, root, logMirrorDirectory, readAllText, builtInSources, serialize, installServices, startedAt);
        }
    }

    /// <summary>
    /// Język trybu CLI: <c>--lang</c> &gt; zapamiętany w ustawieniach (tylko odczyt, korzeń nie powstaje) &gt; język interfejsu Windows.
    /// <c>Program.Main</c> ustawia go jako domyślny język procesu, bo wątki bez przepływu <see cref="System.Threading.AsyncLocal{T}"/>
    /// nie widzą <see cref="L.Use"/> z <see cref="Run(IReadOnlyList{string}, SecureRoot, string?)"/>.
    /// </summary>
    public static UiLanguage ResolveLanguage(IReadOnlyList<string> args, SecureRoot root)
    {
        if (args is null)
        {
            throw new ArgumentNullException(nameof(args));
        }

        if (root is null)
        {
            throw new ArgumentNullException(nameof(root));
        }

        return CliParser.FindLanguage(args)
            ?? new JsonFileSettingsStore(root, new SystemClock()).PeekLanguage()
            ?? LanguageDetector.DetectCurrent();
    }

    private static int RunInLanguage(IReadOnlyList<string> args, SecureRoot root, string? logMirrorDirectory,
                                     Func<string, string> readAllText, Func<IReadOnlyList<CatalogSource>> builtInSources,
                                     Func<CliReport, string> serialize, Func<AppComposition, AppsInstallCatalog, CliInstallServices> installServices,
                                     DateTimeOffset startedAt)
    {
        string? outPath = null;
        CliOptions? options = null;
        try
        {
            var parsed = CliParser.Parse(args);
            outPath = parsed.OutPath;
            if (!parsed.IsSuccess)
            {
                var now = DateTimeOffset.UtcNow;
                var failure = CliRunner.Failure(
                    CliExitCode.UsageError, null, new[] { parsed.Error ?? CliParser.Usage }, Array.Empty<string>(), now, now);
                return parsed.OutPath is null ? (int)CliExitCode.UsageError : Finish(failure, parsed.OutPath, serialize);
            }

            options = parsed.Options!;
            return RunParsed(options, root, logMirrorDirectory, readAllText, builtInSources, startedAt, serialize, installServices);
        }
        catch (Exception ex)
        {
            var report = CliRunner.Failure(
                CliExitCode.InternalError, options, new[] { L.F("Nieoczekiwany błąd: {0}: {1}", ex.GetType().Name, ex.Message) },
                Array.Empty<string>(), startedAt, DateTimeOffset.UtcNow);
            return outPath is null ? (int)CliExitCode.InternalError : FinishSafely(report, outPath, serialize);
        }
    }

    private static int RunParsed(CliOptions options, SecureRoot root, string? logMirrorDirectory,
                                 Func<string, string> readAllText, Func<IReadOnlyList<CatalogSource>> builtInSources, DateTimeOffset startedAt,
                                 Func<CliReport, string> serialize, Func<AppComposition, AppsInstallCatalog, CliInstallServices> installServices)
    {
        var sources = options.CatalogPath is null ? builtInSources() : Array.Empty<CatalogSource>();
        var catalog = CliCatalog.Load(options.CatalogPath, readAllText, sources);
        if (catalog.Errors.Count > 0 || catalog.Catalog is null)
        {
            var errors = catalog.Errors.Count > 0 ? catalog.Errors : new[] { L.F("Nie można załadować katalogu {0}.", catalog.Source) };
            return Finish(
                CliRunner.Failure(CliExitCode.CatalogError, options, errors, Array.Empty<string>(), startedAt, DateTimeOffset.UtcNow),
                options.OutPath, serialize);
        }

        var warnings = ExternalCatalogWarnings(catalog);

        // Lista apps.json (wbudowana albo --apps-catalog) przed składaniem korzenia: błąd formatu = kod 3 bez dotykania dysku programu.
        AppsInstallCatalog? appsCatalog = null;
        if (CliParser.IsInstallCommand(options.Command))
        {
            appsCatalog = LoadAppsCatalog(options.AppsCatalogPath, readAllText, out var appsError);
            if (appsCatalog is null)
            {
                return Finish(
                    CliRunner.Failure(CliExitCode.CatalogError, options, new[] { appsError! }, warnings, startedAt, DateTimeOffset.UtcNow),
                    options.OutPath, serialize);
            }
        }

        AppComposition composition;
        try
        {
            composition = AppComposition.Create(root, logMirrorDirectory);
        }
        catch (Exception ex)
        {
            return Finish(
                CliRunner.Failure(
                    CliExitCode.InternalError, options, new[] { L.F("Nie można przygotować środowiska ({0}): {1}: {2}", root.FullPath, ex.GetType().Name, ex.Message) },
                    warnings, startedAt, DateTimeOffset.UtcNow),
                options.OutPath, serialize);
        }

        CliReport report;
        try
        {
            var apps = options.Command is CliCommand.ListApps or CliCommand.RemoveApp ? CreateAppServices(composition) : null;
            var install = appsCatalog is null ? null : installServices(composition, appsCatalog);
            report = CliRunner.Run(options, catalog, composition.Services, composition.StartupWarnings, apps, install);
        }
        catch (Exception ex)
        {
            composition.Log.Fail("CLI", L.F("Nieoczekiwany błąd wykonania ({0} {1}).", options.Command, options.TweakId), ex);
            var all = new List<string>(composition.StartupWarnings);
            all.AddRange(warnings);
            report = CliRunner.Failure(
                CliExitCode.InternalError, options, new[] { L.F("Nieoczekiwany błąd: {0}: {1}", ex.GetType().Name, ex.Message) }, all, startedAt, DateTimeOffset.UtcNow);
        }

        return Finish(report, options.OutPath, serialize);
    }

    /// <summary>
    /// Usługi poleceń aplikacji na prawdziwym systemie: odczyt AppX i rejestru Uninstall, usuwanie (skrypt AppX, deinstalator
    /// przez <c>CreateProcessW</c> bez Job Objectu), wbudowana klasyfikacja <c>apps-bloat.json</c> i SID konta procesu.
    /// </summary>
    private static CliAppServices CreateAppServices(AppComposition composition)
        => new()
        {
            Reader = new InstalledAppsReader(composition.Services.Scripts),
            Remover = new AppxRemover(composition.Services.Scripts),
            Catalog = AppsBloatCatalog.Load(BuiltInCatalog.AppsBloatSource.Json),
            ProcessSid = WindowsIdentity.GetCurrent().User?.Value ?? throw new InvalidOperationException(L.T("Brak SID bieżącego procesu.")),
            AccountName = new WindowsAccountNames().NameOf,
            FileExists = File.Exists,
            System32 = Environment.SystemDirectory,
        };

    /// <summary>
    /// Usługi poleceń instalacji na prawdziwym systemie: winget przez alias z profilu konta procesu (<see cref="AppComposition.Winget"/>),
    /// logi winget w korzeniu programu i odczyt listy zainstalowanych jak na stronie „Aplikacje”. Bez <c>--locale</c> (Ruling R14).
    /// RustDesk z GitHuba: te same usługi co reszta programu (<see cref="AppComposition.GitHub"/>) – tylko składane, startują dopiero
    /// w serii <c>--install-apps</c> z pozycją z GitHuba.
    /// </summary>
    internal static CliInstallServices CreateInstallServices(AppComposition composition, AppsInstallCatalog catalog)
        => new()
        {
            Runner = composition.Winget,
            Logs = composition.WingetLogs,
            Reader = new InstalledAppsReader(composition.Services.Scripts),
            Catalog = catalog,
            ProcessSid = WindowsIdentity.GetCurrent().User?.Value ?? throw new InvalidOperationException(L.T("Brak SID bieżącego procesu.")),
            AccountName = new WindowsAccountNames().NameOf,
            GitHub = composition.GitHub,
        };

    /// <summary>Wbudowana lista <c>apps.json</c> albo plik <c>--apps-catalog</c>; błąd odczytu lub formatu = <c>null</c> z komunikatem.</summary>
    private static AppsInstallCatalog? LoadAppsCatalog(string? path, Func<string, string> readAllText, out string? error)
    {
        string json;
        if (path is null)
        {
            json = BuiltInCatalog.AppsInstallSource.Json;
        }
        else
        {
            try
            {
                json = readAllText(path);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException or SecurityException)
            {
                error = L.F("Nie można odczytać listy aplikacji {0}: {1}", path, ex.Message);
                return null;
            }
        }

        try
        {
            error = null;
            return AppsInstallCatalog.Load(json);
        }
        catch (InstallCatalogException ex)
        {
            error = path is null
                ? L.F("Błędna lista aplikacji wbudowana lista {0}: {1}", BuiltInCatalog.AppsInstallFileName, ex.Message)
                : L.F("Błędna lista aplikacji {0}: {1}", path, ex.Message);
            return null;
        }
    }

    private static string[] ExternalCatalogWarnings(CliCatalogResult catalog)
        => string.Equals(catalog.Source, CliCatalog.BuiltInSource, StringComparison.Ordinal)
            ? Array.Empty<string>()
            : new[] { L.F("Użyto zewnętrznego katalogu: {0} ({1}).", catalog.Source, catalog.Catalog!.Hash) };

    /// <summary>Zapisuje raport i zwraca kod wyjścia raportu albo 6, gdy zapis się nie udał.</summary>
    private static int Finish(CliReport report, string outPath, Func<CliReport, string> serialize)
        => WriteReport(report, outPath, serialize) ? report.ExitCode : (int)CliExitCode.OutputError;

    /// <summary>
    /// Zapis raportu w <c>catch</c> ostatniej szansy: gdy zawiedzie także on (np. serializer), nie wolno wypuścić wyjątku z procesu,
    /// więc zwraca 5 (kontrakt: nieoczekiwany wyjątek = 5), choć raportu nie ma.
    /// </summary>
    private static int FinishSafely(CliReport report, string outPath, Func<CliReport, string> serialize)
    {
        try
        {
            return Finish(report, outPath, serialize);
        }
        catch (Exception)
        {
            return (int)CliExitCode.InternalError;
        }
    }

    /// <summary>
    /// UTF-8 bez BOM. Znaki spoza ASCII w raporcie (np. polskie komunikaty) są poprawnie czytane przez
    /// Get-Content tylko z jawnym <c>-Encoding UTF8</c>: PS 5.1 bez BOM i bez -Encoding zakłada stronę kodową ANSI.
    /// Katalogu --out nie tworzy.
    /// </summary>
    private static bool WriteReport(CliReport report, string outPath, Func<CliReport, string> serialize)
    {
        try
        {
            File.WriteAllText(outPath, serialize(report), new UTF8Encoding(false));
            return true;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException or SecurityException)
        {
            return false;
        }
    }
}
