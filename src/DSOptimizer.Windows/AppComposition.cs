using System;
using System.Collections.Generic;
using DSOptimizer.Core.Abstractions;
using DSOptimizer.Core.Engine;
using DSOptimizer.Core.Install;
using DSOptimizer.Core.Journal;
using DSOptimizer.Core.Localization;
using DSOptimizer.Core.Repair;
using DSOptimizer.Core.RestorePoints;
using DSOptimizer.Core.Tools;

namespace DSOptimizer.Windows;

/// <summary>
/// Composition root: składa usługi silnika na prawdziwym systemie. Korzeń jest zabezpieczany jako pierwszy
/// (zanim powstanie log, runner czy journal), a ścieżki odłożone jako <c>.untrusted-*</c> trafiają do ostrzeżeń startowych.
/// </summary>
public sealed class AppComposition
{
    private AppComposition(
        EngineServices services,
        ILog log,
        ILog fileLog,
        IReadOnlyList<string> startupWarnings,
        IWingetRunner winget,
        IWingetLogs wingetLogs,
        GitHubInstallServices gitHub)
    {
        Services = services;
        Log = log;
        FileLog = fileLog;
        StartupWarnings = startupWarnings;
        Winget = winget;
        WingetLogs = wingetLogs;
        GitHub = gitHub;
    }

    public EngineServices Services { get; }

    public ILog Log { get; }

    /// <summary>Sam log pliku, bez obserwatora (okna): wpisy, które nie mają trafić do panelu.</summary>
    public ILog FileLog { get; }

    public IReadOnlyList<string> StartupWarnings { get; }

    /// <summary>Uruchamiacz winget: alias z profilu konta procesu (<see cref="WingetRunner.AliasPath"/>), bez zabijania.</summary>
    public IWingetRunner Winget { get; }

    /// <summary>Ścieżki logów winget (<c>--log</c>) w <c>&lt;logs&gt;\winget</c>, folder tworzony leniwie.</summary>
    public IWingetLogs WingetLogs { get; }

    /// <summary>
    /// Instalacja z GitHuba (RustDesk): HTTPS, podpis Authenticode, <c>msiexec</c> z System32 i folder pobrań. Tylko składane – nic nie
    /// łączy się z siecią ani nie powstaje na dysku przy starcie; folder <c>downloads</c> powstaje przy pierwszej ścieżce pobrania.
    /// </summary>
    public GitHubInstallServices GitHub { get; }

    /// <summary>
    /// <paramref name="observer"/> (np. okno) dostaje wszystkie wpisy logu, także ostrzeżenia startowe; jego błędy są połykane.
    /// Wyjątki z zabezpieczania korzenia (np. korzeń jest junction) lecą dalej – fail-safe, nic nie powstaje.
    /// </summary>
    public static AppComposition Create(SecureRoot root, string? logMirrorDirectory, ILog? observer = null)
    {
        if (root is null)
        {
            throw new ArgumentNullException(nameof(root));
        }

        var setAside = root.Ensure();

        var clock = new SystemClock();
        var fileLog = new FileLog(root, logMirrorDirectory, clock);
        ILog log = observer is null ? fileLog : new TeeLog(fileLog, observer);
        var registry = new WindowsRegistry();
        var serviceManager = new WindowsServiceManager(registry);
        var systemInfo = new WindowsSystemInfo(registry);
        var runner = new PowerShellScriptRunner(root, log);
        var journal = new JsonFileJournalStore(root, systemInfo.MachineName, clock);
        var user = InteractiveUserContext.Resolve(log);
        var restorePoints = new RestorePointService(registry, new WmiRestorePointApi(), log, clock);

        var warnings = new List<string>();
        foreach (var aside in setAside)
        {
            warnings.Add(L.F("Katalog roboczy {0} miał niezaufanego właściciela lub uprawnienia i został odłożony: {1}. Utworzono nowy.", root.FullPath, aside));
        }

        try
        {
            var warning = journal.Load().Warning;
            if (warning is not null)
            {
                warnings.Add(warning);
            }
        }
        catch (JournalStoreException ex)
        {
            warnings.Add(ex.Message);
        }

        foreach (var warning in warnings)
        {
            log.Warn("Start", warning);
        }

        // Punkt składania handlerów akcji (pole katalogu handler): kolejne partie napraw dopisują tu swoje handlery.
        var repair = new RepairServices
        {
            Services = serviceManager,
            Registry = registry,
            Tools = new System32ToolRunner(),
            LongTools = new ServicingToolRunner(),
            DiskSpace = new WindowsDiskSpace(),
            Servicing = new WindowsServicingActivity(),
            Scripts = runner,
            User = user,
            Files = new WindowsRepairFileSystem(),
            Domain = new WindowsDomainInfo(),
            Clock = clock,
            Log = log,
            Paths = new RepairPaths
            {
                SystemRoot = Environment.GetFolderPath(Environment.SpecialFolder.Windows),
                EnsureBackupDirectory = () => EnsureBackupDirectory(root, log),
            },
        };

        // Plan RustDesk + Etap 5: HTTP i folder pobrań – jedne instancje dla instalacji z GitHuba i handlera WinUtil.
        // Tylko składane: nic nie łączy się z siecią ani nie powstaje na dysku przy starcie.
        var web = new WebDownloader(RustDeskSource.UserAgent);
        var downloads = new DownloadFolder(root, log);
        var launcher = new ShellProcessLauncher(new WindowsExplorerProcessQuery());
        var gitHub = new GitHubInstallServices
        {
            Web = web,
            Signatures = new AuthenticodeVerifier(),
            Msi = new MsiexecInstaller(MsiexecInstaller.SystemPath()),
            Files = downloads,
        };

        var handlers = new List<IActionHandler>
        {
            new WindowsUpdateResetHandler(full: false, repair),
            new WindowsUpdateResetHandler(full: true, repair),
            new RestorePointHandler(restorePoints, clock),
            new SystemFilesHandler(repair),
            new DiskCleanupHandler(repair),
            new WindowsOldHandler(repair),
            new WinUtilHandler(new WinUtilServices { Web = web, Files = downloads, Launcher = launcher, Log = log }),
        };

        var services = new EngineServices
        {
            Registry = registry,
            Services = serviceManager,
            Scripts = runner,
            Launcher = launcher,
            Journal = journal,
            RestorePoints = restorePoints,
            SystemInfo = systemInfo,
            User = user,
            Log = log,
            Clock = clock,
            Handlers = new ActionHandlerSet(handlers),
        };

        // Etap 4c: winget tylko składany; startuje dopiero na żądanie instalacji albo wyszukiwania.
        var winget = new WingetRunner(WingetRunner.AliasPath());
        var wingetLogs = new WingetLogFolder(root, log);

        return new AppComposition(services, log, fileLog, warnings, winget, wingetLogs, gitHub);
    }

    /// <summary>Katalog kopii napraw pod korzeniem, zabezpieczany dopiero przy pierwszej kopii (nie przy starcie programu).</summary>
    private static string EnsureBackupDirectory(SecureRoot root, ILog log)
    {
        foreach (var aside in root.EnsureLeaf(root.BackupsDirectory))
        {
            log.Warn(L.T("Naprawy"), L.F("Katalog {0} miał niezaufanego właściciela lub uprawnienia i został odłożony. Utworzono nowy.", aside));
        }

        return root.BackupsDirectory;
    }
}
