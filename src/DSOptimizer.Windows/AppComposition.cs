using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using DSOptimizer.Core.Abstractions;
using DSOptimizer.Core.Engine;
using DSOptimizer.Core.Install;
using DSOptimizer.Core.Journal;
using DSOptimizer.Core.Localization;
using DSOptimizer.Core.Repair;
using DSOptimizer.Core.RestorePoints;
using DSOptimizer.Core.Storage;
using DSOptimizer.Core.Tools;

namespace DSOptimizer.Windows;

/// <summary>
/// Composition root: składa usługi silnika na prawdziwym systemie. Korzeń jest zabezpieczany jako pierwszy
/// (zanim powstanie log, runner czy journal). Z korzenia odłożonego jako <c>.untrusted-*</c> program przenosi historię zmian
/// i ustawienia, a gdy w korzeniu nie ma historii, odzyskuje ją z dawnych odłożonych folderów; do ostrzeżeń startowych (okno)
/// trafia tylko utrata historii, reszta to linie logu.
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

        // Istniejące podfoldery (np. z wersji 4.0.7) dostają bieżący deskryptor od razu, zanim Eksplorator zapyta o dostęp.
        // Odłożony albo błędny podfolder psuje tylko swoją funkcję, więc wystarcza linia WARN, bez okna.
        var leafAsides = root.EnsureExistingLeaves(error => log.Warn("Start", error));
        foreach (var aside in leafAsides)
        {
            log.Warn("Start", SetAsideText.Folder(aside));
        }

        var registry = new WindowsRegistry();
        var serviceManager = new WindowsServiceManager(registry);
        var systemInfo = new WindowsSystemInfo(registry);
        var runner = new PowerShellScriptRunner(root, log);
        var journal = new JsonFileJournalStore(root, systemInfo.MachineName, clock);
        var user = InteractiveUserContext.Resolve(log);
        var restorePoints = new RestorePointService(registry, new WmiRestorePointApi(), log, clock);

        var warnings = new List<string>();
        var deferred = RecoverHistory(root, setAside, journal, systemInfo.MachineName, log, warnings);

        try
        {
            // Gdy przy tym starcie odłożono korzeń, o historii zmian rozstrzygnął już RecoverHistory; ostrzeżenie z journala
            // powtórzyłoby tę samą ścieżkę. Przy odłożonej decyzji (plik chwilowo zajęty) opis odłożonych journali wskazałby ten
            // sam plik, który kolejny start jeszcze przeniesie.
            var warning = journal.Load().Warning;
            if (warning is not null && setAside.Count == 0 && !deferred)
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

        // Folder z wersji 4.0.7, którego deskryptora nie udało się przepisać (korzeń, podfoldery, journal): jedna linia na folder,
        // żeby trwały błąd był widoczny. Folder działa dalej ze starym, zaufanym deskryptorem.
        foreach (var failure in root.TakeRewriteFailures())
        {
            log.Warn("Start", RewriteFailed(failure.Key, failure.Value));
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

    /// <summary>
    /// Przenosi historię zmian i ustawienia z korzenia odłożonego przy tym starcie, a gdy w korzeniu nie ma historii, odzyskuje ją
    /// z dawnych folderów <c>&lt;korzeń&gt;.untrusted-*</c>. Każdy plik dostaje w logu swoją linię; do <paramref name="warnings"/>
    /// (okno) trafia tylko utrata historii. Po próbie bez przeniesienia zapisuje pusty journal, więc skan i okno się nie powtarzają.
    /// Zwraca <c>true</c>, gdy decyzję o historii odłożono do kolejnego startu (plik historii chwilowo zajęty).
    /// </summary>
    private static bool RecoverHistory(
        SecureRoot root, IReadOnlyList<string> setAside, JsonFileJournalStore journal, string machineName, ILog log, List<string> warnings)
    {
        var journalFile = Path.Combine(root.JournalDirectory, JsonFileJournalStore.FileName);
        var lost = new List<string>();
        var attempted = false;
        var deferred = false;
        foreach (var aside in setAside)
        {
            log.Info("Start", SetAsideText.RootMoved(aside));
            var salvage = SetAsideSalvage.FromAside(aside, root, machineName);
            WarnSetAside(salvage.SetAside, log);
            if (salvage.Journal.Outcome == SalvageOutcome.Moved)
            {
                log.Info("Start", SetAsideText.HistoryMoved);
            }
            else if (salvage.Journal.Outcome == SalvageOutcome.Rejected)
            {
                log.Warn("Start", SetAsideText.HistoryNotMoved(salvage.Journal.Source, ReasonOf(salvage.Journal)));

                // Plik chwilowo zajęty w folderze, który skan dawnych folderów weźmie przy kolejnym starcie: decyzja odłożona, bez okna
                // i bez pustego journala. Folder z obcym właścicielem do skanu nie wejdzie, więc wtedy to utrata.
                if (salvage.Journal.Transient && SecureDirectory.IsAdminOwnedPlainFolder(aside))
                {
                    deferred = true;
                }
                else
                {
                    lost.Add(aside);
                    attempted = true;
                }
            }

            if (salvage.Settings.Outcome == SalvageOutcome.Moved)
            {
                log.Info("Start", SetAsideText.SettingsMoved);
            }
            else if (salvage.Settings.Outcome == SalvageOutcome.Rejected)
            {
                log.Warn("Start", SetAsideText.SettingsNotMoved(salvage.Settings.Source, ReasonOf(salvage.Settings)));
            }
        }

        if (lost.Count == 0 && !File.Exists(journalFile))
        {
            var old = SetAsideSalvage.FromOldAsides(root, machineName);
            WarnSetAside(old.SetAside, log);
            foreach (var rejected in old.Rejected)
            {
                log.Warn("Start", SetAsideText.HistoryNotMoved(rejected.Source, ReasonOf(rejected)));
            }

            if (old.Moved is not null)
            {
                log.Info("Start", SetAsideText.HistoryRestored(old.Moved.Source));
            }
            else if (old.Rejected.Any(file => file.Transient))
            {
                // Plik chwilowo zajęty przez inny proces (np. antywirus) odkłada decyzję do kolejnego startu: bez okna i bez pustego
                // journala, który zakończyłby skanowanie i po cichu przekreślił tę historię. Same linie WARN.
                deferred = true;
            }
            else if (old.Rejected.Count > 0)
            {
                attempted = true;

                // Okno tylko przy odrzuceniu na treści albo zapisie: odrzucenie na samym pliku (link, właściciel, ACL) mogło
                // pochodzić od zwykłego konta, które w C:\ProgramData zakłada foldery.
                foreach (var rejected in old.Rejected.Where(file => file.Rejection is SalvageRejection.Corrupted
                    or SalvageRejection.OtherComputer or SalvageRejection.TooLarge or SalvageRejection.WriteFailed))
                {
                    var folder = Path.GetDirectoryName(Path.GetDirectoryName(rejected.Source));
                    if (!string.IsNullOrEmpty(folder) && !lost.Contains(folder!, StringComparer.OrdinalIgnoreCase))
                    {
                        lost.Add(folder!);
                    }
                }
            }
        }

        if (lost.Count > 0)
        {
            warnings.Add(SetAsideText.HistoryLost(string.Join(", ", lost)));
        }

        if (attempted && !File.Exists(journalFile))
        {
            try
            {
                journal.Save(new JournalDocument { Machine = machineName });
            }
            catch (JournalStoreException ex)
            {
                warnings.Add(ex.Message);
            }
        }

        return deferred;
    }

    /// <summary>Linia WARN: nie udało się przepisać uprawnień istniejącego folderu na bieżące; <paramref name="reason"/> = komunikat błędu.</summary>
    internal static string RewriteFailed(string folder, string reason)
        => L.F("Nie udało się zmienić uprawnień folderu {0}, więc Eksplorator może pytać o dostęp do niego. Program spróbuje ponownie przy następnym starcie. Powód: {1}", folder, reason);

    private static string ReasonOf(SalvageFile file) => SetAsideText.Reason(file.Rejection!.Value, file.Detail);

    /// <summary>Folder odłożony przy zapisie kopii historii (np. niezaufany <c>journal</c>): linia WARN, jak przy innych podfolderach.</summary>
    private static void WarnSetAside(IReadOnlyList<string> asides, ILog log)
    {
        foreach (var aside in asides)
        {
            log.Warn("Start", SetAsideText.Folder(aside));
        }
    }

    /// <summary>Katalog kopii napraw pod korzeniem, zabezpieczany dopiero przy pierwszej kopii (nie przy starcie programu).</summary>
    private static string EnsureBackupDirectory(SecureRoot root, ILog log)
    {
        foreach (var aside in root.EnsureLeaf(root.BackupsDirectory))
        {
            log.Warn(L.T("Naprawy"), SetAsideText.Folder(aside));
        }

        return root.BackupsDirectory;
    }
}
