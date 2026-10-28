using System;
using System.IO;
using System.Linq;
using System.Security.Principal;
using System.Text;
using System.Threading;
using System.Windows;
using System.Windows.Threading;
using DSOptimizer.App.Gui;
using DSOptimizer.App.ViewModels;
using DSOptimizer.App.Views;
using DSOptimizer.Catalog;
using DSOptimizer.Core.Abstractions;
using DSOptimizer.Core.Apps;
using DSOptimizer.Core.Cli;
using DSOptimizer.Core.Gui;
using DSOptimizer.Core.Hardware;
using DSOptimizer.Core.Install;
using DSOptimizer.Core.Localization;
using DSOptimizer.Core.Prep;
using DSOptimizer.Core.Settings;
using DSOptimizer.Windows;
using DSOptimizer.Windows.Apps;
using DSOptimizer.Windows.Hardware;
using DSOptimizer.Windows.Prep;

namespace DSOptimizer.App;

internal static class Program
{
    private const string Component = "Gui";
    private static int _windowErrorShown;
    private static readonly WindowExceptionGate WindowGate = new();

    [STAThread]
    public static int Main(string[] args)
    {
        if (args.Length > 0)
        {
            return CliHost.Run(args, SecureRoot.ProgramData, FileLog.DefaultMirrorDirectory);
        }

        // Komunikaty sprzed odczytu ustawień (katalog, korzeń) w języku systemu; zapisany albo wybrany język ustawia się niżej.
        var detected = LanguageDetector.DetectCurrent();
        L.SetDefault(detected);

        var sink = new UiLogSink(Dispatcher.CurrentDispatcher, new SystemClock());
        var catalog = CliCatalog.Load(null, path => File.ReadAllText(path, Encoding.UTF8), BuiltInCatalog.Sources);
        if (catalog.Catalog is null)
        {
            MessageBox.Show($"Wbudowany katalog jest nieprawidłowy:{Environment.NewLine}{string.Join(Environment.NewLine, catalog.Errors)}", "DSOptimizer", MessageBoxButton.OK, MessageBoxImage.Error);
            return (int)CliExitCode.CatalogError;
        }

        AppComposition composition;
        try
        {
            composition = AppComposition.Create(SecureRoot.ProgramData, FileLog.DefaultMirrorDirectory, sink);
        }
        catch (Exception ex)
        {
            MessageBox.Show($"Nie można przygotować katalogu roboczego DSOptimizer: {ex.Message}", "DSOptimizer", MessageBoxButton.OK, MessageBoxImage.Error);
            return (int)CliExitCode.InternalError;
        }

        var log = composition.Log;
        AppDomain.CurrentDomain.UnhandledException += (_, e) => OnFatalException(log, e.ExceptionObject as Exception, e.IsTerminating);

        if (composition.StartupWarnings.Count > 0)
        {
            MessageBox.Show(string.Join(Environment.NewLine + Environment.NewLine, composition.StartupWarnings), "DSOptimizer – uwaga", MessageBoxButton.OK, MessageBoxImage.Warning);
        }

        // Ustawienia po AppComposition.Create (korzeń już zabezpieczony). Load z założenia nie rzuca, ale niespodziewany wyjątek też daje domyślne; ostrzeżenie tylko do dziennika.
        var settingsStore = new JsonFileSettingsStore(SecureRoot.ProgramData, composition.Services.Clock);
        SettingsLoadResult settings;
        try
        {
            settings = settingsStore.Load();
        }
        catch (Exception ex)
        {
            settings = new SettingsLoadResult(AppSettings.Default, $"Nie można odczytać ustawień: {ex.Message} Używam ustawień domyślnych.");
        }

        if (settings.Warning is { } settingsWarning)
        {
            log.Warn("Ustawienia", settingsWarning);
        }

        var session = new GuiSession(catalog.Catalog, composition.Services) { CreateRestorePoint = settings.Settings.CreateRestorePoint };
        var accountNames = new CachedAccountNames(new WindowsAccountNames());
        AppsSession apps;
        try
        {
            apps = CreateApps(composition, session, accountNames);
        }
        catch (AppsCatalogException ex)
        {
            MessageBox.Show($"Wbudowana lista aplikacji jest nieprawidłowa:{Environment.NewLine}{ex.Message}", "DSOptimizer", MessageBoxButton.OK, MessageBoxImage.Error);
            return (int)CliExitCode.CatalogError;
        }

        InstallSession install;
        try
        {
            install = CreateInstall(composition, session, accountNames);
        }
        catch (InstallCatalogException ex)
        {
            MessageBox.Show($"Wbudowana lista instalacji jest nieprawidłowa:{Environment.NewLine}{ex.Message}", "DSOptimizer", MessageBoxButton.OK, MessageBoxImage.Error);
            return (int)CliExitCode.CatalogError;
        }

        var app = new App();
        app.DispatcherUnhandledException += (_, e) =>
        {
            OnWindowException(composition, sink, e.Exception);
            e.Handled = true;
        };
        app.InitializeComponent();
        var theme = new ThemeController(log);
        theme.ApplyAtStartup(settings.Settings.Theme);
        // Język: zapisany = bez pytania; brak = okno wyboru (motyw już zastosowany), zamknięte bez wyboru = język wykryty.
        var language = StartupLanguage.Choose(settings.Settings, detected, proposed => AskLanguage(log, proposed), settingsStore);
        L.SetDefault(language.Language);
        if (language.Warning is { } languageWarning)
        {
            log.Warn("Ustawienia", languageWarning);
        }

        if (L.LoadWarning is { } translationsWarning)
        {
            log.Warn("Ustawienia", translationsWarning);
        }

        var machine = MachineSummary.Format(composition.Services.SystemInfo);
        var user = composition.Services.User;
        // Link w dzienniku: Eksplorator za strażnikiem powłoki; ścieżek z katalogu programu nie otwiera (tylko kopiowanie).
        var protectedRoot = SecureRoot.ProgramData.FullPath;
        var revealer = new ShellFileRevealer(new WindowsExplorerProcessQuery(), protectedRoot);
        var shell = new ShellActions(revealer, new ShellUrlOpener(new WindowsExplorerProcessQuery()), log);
        var specification = CreateSpecification(composition, sink, shell);
        var prep = CreatePrep(composition, sink, shell);
        // „Uruchom ponownie teraz” (zmiana języka): najpierw nowa instancja, zamknięcie tej dopiero po udanym starcie.
        var settingsPage = new SettingsViewModel(
            new SettingsLoadResult(language.Settings, settings.Warning),
            settingsStore,
            session,
            theme,
            shell,
            log,
            language.Language,
            new ProgramRelauncher(log),
            () => Application.Current.Shutdown());
        var seriesDialogs = new SeriesDialogs(log, sink.Add, composition.Services.Clock);
        // Baner restartu: czas startu systemu z CIM (tylko odczyt); restart przez shutdown.exe – wyłącznie po potwierdzeniu w oknie,
        // ze śladem w pliku logu (zlecenie i przyczyna błędu).
        var lastBootTime = new WindowsLastBootTime(log);
        var restarter = new ShutdownRestarter(log);
        var viewModel = new MainViewModel(
            session,
            sink,
            seriesDialogs,
            composition.Services.Clock,
            lastBootTime,
            restarter,
            machine,
            () => MachineSummary.ResolveUserName(user),
            accountNames,
            apps,
            install,
            specification,
            prep,
            settingsPage);
        return app.Run(new MainWindow(viewModel, log, shell, theme, protectedRoot));
    }

    /// <summary>Okno wyboru języka; jego wyjątek nie blokuje startu: wpis w dzienniku i język wykryty (<c>null</c>).</summary>
    private static UiLanguage? AskLanguage(ILog log, UiLanguage detected)
    {
        try
        {
            return LanguageChoiceWindow.Ask(detected);
        }
        catch (Exception ex)
        {
            TryLog(log, L.T("Nie udało się pokazać okna wyboru języka. Program używa języka systemu."), ex);
            if (Application.Current is { } app)
            {
                // Pozostawione okno wyboru (np. wyjątek w trakcie pokazywania) nie może trzymać procesu przy OnLastWindowClose.
                foreach (var chooser in app.Windows.OfType<LanguageChoiceWindow>().ToList())
                {
                    try
                    {
                        chooser.Close();
                    }
                    catch (Exception closeEx)
                    {
                        TryLog(log, L.T("Nie udało się zamknąć okna wyboru języka."), closeEx);
                    }
                }

                // Okno wyboru nie może zostać MainWindow aplikacji – nim ma być okno główne programu.
                if (app.MainWindow is LanguageChoiceWindow)
                {
                    app.MainWindow = null;
                }
            }

            return null;
        }
    }

    /// <summary>
    /// Strona „Aplikacje”: odczyt (AppX przez runner skryptów, rejestr Uninstall), usuwanie (AppX przez skrypt, deinstalator przez
    /// CreateProcessW z pełnej ścieżki) i silnik serii z punktem przywracania, journalem i weryfikacją. Zakres AppX wg SID procesu
    /// i zalogowanego (Ruling R8). Konstruktory niczego nie uruchamiają; lista śmieci i chronionych z zasobu Catalog.
    /// </summary>
    private static AppsSession CreateApps(AppComposition composition, GuiSession session, IAccountNames accountNames)
    {
        var services = composition.Services;
        var processSid = WindowsIdentity.GetCurrent().User?.Value ?? services.User.Sid;
        var reader = new InstalledAppsReader(services.Scripts);
        var engine = new AppRemovalEngine(
            reader, new AppxRemover(services.Scripts), services.Journal, services.RestorePoints, services.User, processSid, composition.Log, services.Clock);
        return new AppsSession(
            session,
            reader,
            engine,
            AppsBloatCatalog.Load(BuiltInCatalog.AppsBloatSource.Json),
            new SystemProtectionReader(),
            accountNames,
            services.User,
            processSid,
            File.Exists,
            Environment.SystemDirectory,
            services.Clock);
    }

    /// <summary>
    /// Strona „Instalacja” (Etap 4c): lista <c>apps.json</c> z zasobu Catalog, odczyt zainstalowanych tym samym czytnikiem co
    /// „Aplikacje”, winget przez <see cref="AppComposition.Winget"/> (alias z profilu konta procesu, bez zabijania) i logi winget
    /// w <see cref="AppComposition.WingetLogs"/>. RustDesk z GitHuba przez <see cref="AppComposition.GitHub"/> (pobieranie, podpis,
    /// msiexec – jak w CLI). Konstruktory niczego nie uruchamiają – winget startuje dopiero przy odczycie strony, GitHub dopiero w serii.
    /// </summary>
    private static InstallSession CreateInstall(AppComposition composition, GuiSession session, IAccountNames accountNames)
    {
        var services = composition.Services;
        var processSid = WindowsIdentity.GetCurrent().User?.Value ?? services.User.Sid;
        var reader = new InstalledAppsReader(services.Scripts);
        var engine = new AppInstallEngine(
            composition.Winget,
            composition.WingetLogs,
            reader,
            services.Journal,
            services.User,
            processSid,
            accountNames.NameOf,
            composition.Log,
            services.Clock,
            gitHub: composition.GitHub);
        return new InstallSession(
            session,
            engine,
            AppsInstallCatalog.Load(BuiltInCatalog.AppsInstallSource.Json),
            reader,
            composition.Winget,
            services.User,
            processSid,
            accountNames,
            services.Clock);
    }

    /// <summary>
    /// Strona „Specyfikacja”: czytnik sprzętu (tylko odczyt), loader z limitem na część, zapis raportu na komputerze
    /// (<see cref="SecureRoot.ReportsDirectory"/>) i obok exe tylko poza dyskiem systemowym. Składana po
    /// <see cref="AppComposition.Create"/> (korzeń już zabezpieczony); konstruktory niczego nie tworzą na dysku.
    /// </summary>
    private static SpecificationViewModel CreateSpecification(AppComposition composition, UiLogSink sink, ShellActions shell)
    {
        var services = composition.Services;
        var loader = new SpecificationLoader(new WindowsHardwareReader(), composition.Log, SpecificationLoader.DefaultTimeout);
        var writer = new ReportFileWriter(SecureRoot.ProgramData, FileLog.DefaultMirrorDirectory);
        return new SpecificationViewModel(loader, writer, composition.Log, services.Clock, services.SystemInfo.MachineName, sink.Add, shell);
    }

    /// <summary>
    /// Strona „Przygotowanie”: odczyt płyty i BitLockera (<see cref="WindowsHardwareReader"/>) oraz produktu, aktywacji, grafiki
    /// i urządzeń z problemem (<see cref="WindowsPrepReader"/>) – wyłącznie odczyt, z limitem na część. Linki przez ten sam
    /// <see cref="ShellActions"/> co reszta okna. Dysk systemowy z <c>%SystemDrive%</c> (brak = „C:”).
    /// </summary>
    private static PrepViewModel CreatePrep(AppComposition composition, UiLogSink sink, ShellActions shell)
    {
        var loader = new PrepLoader(new WindowsHardwareReader(), new WindowsPrepReader(), composition.Log, PrepLoader.DefaultTimeout);
        var systemDrive = Environment.GetEnvironmentVariable("SystemDrive");
        return new PrepViewModel(loader, shell, string.IsNullOrEmpty(systemDrive) ? "C:" : systemDrive, (level, text) => sink.Add(level, text));
    }

    /// <summary>
    /// Nieobsłużony wyjątek wątku UI (np. z wiązania albo zdarzenia okna). Proces NIE jest zamykany: w tle może trwać operacja
    /// silnika, której przerwanie byłoby gorsze. Wyjątek idzie do pliku logu i (przez TeeLog) jako błąd do panelu, a za
    /// pierwszym razem komunikat zaleca zamknięcie programu po zakończeniu operacji – stan okna może być niepełny.
    /// </summary>
    private static void OnWindowException(AppComposition composition, UiLogSink sink, Exception exception)
    {
        // Lawina błędów okna nie zalewa panelu: po limicie tylko plik logu i jeden komunikat (bramka WindowExceptionGate).
        var route = WindowExceptionRoute.Panel;
        try
        {
            route = WindowGate.Next(composition.Services.Clock.UtcNow);
        }
        catch (Exception)
        {
            // Zegar niedostępny: zostaje domyślna ścieżka (panel).
        }

        TryLog(route == WindowExceptionRoute.Panel ? composition.Log : composition.FileLog, "Nieobsłużony wyjątek w oknie.", exception);
        if (route == WindowExceptionRoute.FileOnlyWithNotice)
        {
            try
            {
                sink.Add(GuiLogLevel.Warning, GuiText.WindowErrorsMuted);
            }
            catch (Exception)
            {
                // Panel niedostępny: wyjątek i tak jest w pliku logu.
            }
        }

        if (Interlocked.Exchange(ref _windowErrorShown, 1) != 0)
        {
            return;
        }

        TryShow(
            $"Wystąpił nieoczekiwany błąd okna:{Environment.NewLine}{exception.Message}{Environment.NewLine}{Environment.NewLine}" +
            "Szczegóły zapisano w pliku logu. Okno może działać niepoprawnie – zamknij program po zakończeniu bieżącej operacji i uruchom go ponownie.",
            MessageBoxImage.Error);
    }

    /// <summary>Wyjątek spoza wątku UI: .NET Framework i tak zamyka proces, więc tylko ślad w logu i (gdy się da) komunikat.</summary>
    private static void OnFatalException(ILog log, Exception? exception, bool isTerminating)
    {
        TryLog(log, isTerminating ? "Nieobsłużony wyjątek – program zostanie zamknięty." : "Nieobsłużony wyjątek w tle.", exception);
        if (isTerminating)
        {
            TryShow(
                $"Nieoczekiwany błąd – program zostanie zamknięty:{Environment.NewLine}{exception?.Message}{Environment.NewLine}{Environment.NewLine}" +
                "Szczegóły zapisano w pliku logu. Sprawdź stan zmienionych pozycji po ponownym uruchomieniu.",
                MessageBoxImage.Error);
        }
    }

    private static void TryLog(ILog log, string message, Exception? exception)
    {
        try
        {
            log.Fail(Component, message, exception);
        }
        catch (Exception)
        {
            // Log niedostępny (np. dysk): nic więcej nie da się zapisać; komunikat dla użytkownika nadal się pokaże.
        }
    }

    private static void TryShow(string text, MessageBoxImage image)
    {
        try
        {
            MessageBox.Show(text, "DSOptimizer – błąd", MessageBoxButton.OK, image);
        }
        catch (Exception)
        {
            // Bez okna komunikatu (np. dispatcher już zamknięty) zostaje ślad w pliku logu.
        }
    }
}
