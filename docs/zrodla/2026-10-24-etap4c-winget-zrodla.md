# Etap 4c – winget: źródła (research 2026-10-23)

Research automatyczny (pobranie stron/GitHub API), stan na 2026-10-23. Najnowszy stabilny winget: 1.29.380
(https://github.com/microsoft/winget-cli/releases). Wszystko oznaczone „niezweryfikowane” wymaga pomiaru na VM (Task 1).

## 1. Konto, podniesienie, ścieżka
- Winget działa z podniesionego procesu; instalatory nie pokazują wtedy monitów UAC:
  https://learn.microsoft.com/en-us/windows/package-manager/winget/#administrator-considerations
- SYSTEM: CLI niewspierane (MSIX nie rejestruje się dla SYSTEM):
  https://learn.microsoft.com/en-us/windows/package-manager/winget/troubleshooting#system-context
- Ścieżka: alias per użytkownik `%LOCALAPPDATA%\Microsoft\WindowsApps\winget.exe`
  (https://github.com/microsoft/winget-cli/tree/master/doc/troubleshooting). Start bezpośrednio z
  `C:\Program Files\WindowsApps\…` – niezweryfikowane.
- **Administrator Protection** (KB5120998, 2026-08): podniesiony proces dostaje token ukrytego konta systemowego z osobnym
  profilem; domyślnie wyłączona, w security baselines włączana od aktualizacji 2026-10:
  https://learn.microsoft.com/en-us/windows/security/application-security/application-control/administrator-protection/
  Zgłoszenie użytkownika (nie potwierdzenie MS): winget „not recognized” w podniesionym terminalu po KB
  (https://github.com/microsoft/winget-cli/issues/6497). Stan na świeżej 26H2 z ISO – niezweryfikowany.
- Scope: domyślnie `user`, gdy brak – `machine` (https://github.com/microsoft/winget-cli/blob/master/doc/Settings.md,
  `installBehavior`); scope dla EXE bywa niedeterministyczny
  (https://learn.microsoft.com/en-us/windows/package-manager/winget/troubleshooting#scope-for-specific-user-vs-machine-wide).
  Wymuszone `--scope machine` bez instalatora maszynowego = `0x8A150010`.

## 2. Flagi
- https://learn.microsoft.com/en-us/windows/package-manager/winget/install : `--id`, `--exact`, `--source`, `--silent`,
  `--accept-package-agreements`, `--accept-source-agreements`, `--disable-interactivity`, `--scope`, `--architecture`,
  `--log`, `--force`, `--skip-dependencies`, `--allow-reboot`, `--override`, `--ignore-security-hash`.
- Wersja startowa `--disable-interactivity` – niezweryfikowane (jest też ustawienie `interactivity.disable`).
- Wiele pakietów w jednym `install` – wspierane („installs each package in sequence”), jeden kod wyjścia; wersja startowa
  niezweryfikowana.
- Domyślne logi: `%LOCALAPPDATA%\Packages\Microsoft.DesktopAppInstaller_8wekyb3d8bbwe\LocalState\DiagOutputDir`.

## 3. Kody wyjścia
Źródło: https://github.com/microsoft/winget-cli/blob/master/doc/windows/package-manager/winget/returnCodes.md (zgodne
z `AppInstallerErrors.h`).
- `0x8A150014` NO_APPLICATIONS_FOUND, `0x8A150016` MULTIPLE_APPLICATIONS_FOUND, `0x8A150010` NO_APPLICABLE_INSTALLER,
  `0x8A150015` NO_SOURCES_DEFINED, `0x8A15000F` SOURCE_DATA_MISSING, `0x8A150045` SOURCE_OPEN_FAILED.
- `0x8A150061` PACKAGE_ALREADY_INSTALLED, `0x8A15002B` UPDATE_NOT_APPLICABLE, `0x8A15010D` INSTALL_ALREADY_INSTALLED,
  `0x8A15010E` INSTALL_DOWNGRADE.
- `0x8A150109` INSTALL_REBOOT_REQUIRED_TO_FINISH, `0x8A15010A` INSTALL_REBOOT_REQUIRED_FOR_INSTALL,
  `0x8A15010B` INSTALL_REBOOT_INITIATED.
- `0x8A150008` DOWNLOAD_FAILED, `0x8A150107` INSTALL_NO_NETWORK, `0x8A150011` INSTALLER_HASH_MISMATCH.
- `0x8A150041` PACKAGE_AGREEMENTS_NOT_ACCEPTED, `0x8A150046` SOURCE_AGREEMENTS_NOT_ACCEPTED.
- `0x8A150102` INSTALL_INSTALL_IN_PROGRESS (msiexec 1618 mapowany tu w `ManifestCommon.cpp`), `0x8A150101` PACKAGE_IN_USE,
  `0x8A150103` FILE_IN_USE, `0x8A150104` MISSING_DEPENDENCY, `0x8A150105` DISK_FULL, `0x8A150108` CONTACT_SUPPORT,
  `0x8A15010C` CANCELLED_BY_USER, `0x8A150113` SYSTEM_NOT_SUPPORTED, `0x8A150115` CUSTOM_ERROR,
  `0x8A150006` SHELLEXEC_INSTALL_FAILED, `0x8A150049` MSI_INSTALL_FAILED, `0x8A150019` COMMAND_REQUIRES_ADMIN,
  `0x8A15003A` BLOCKED_BY_POLICY, `0x8A150005` CTRL_SIGNAL_RECEIVED.
- `Process.ExitCode` to `int` ze znakiem: porównywać z `unchecked((int)0x8A15xxxx)`.

## 4. Wyjście
- Winget przełącza konsolę na UTF-8 (`ConsoleOutputCPRestore utf8CP(CP_UTF8)` w
  https://github.com/microsoft/winget-cli/blob/master/src/AppInstallerCLICore/Core.cpp).
- Pasek postępu i spinner na stdout (`\r`, VT) – dokładny format niezweryfikowany. Wyłączanie: `visual.progressBar` =
  `disabled` (od 1.9), flaga `--no-progress` od 1.29.240 (ukryta w `Argument.cpp`).
- Brak `--output json` dla `search`/`list`/`show`/`install`; JSON tylko w `winget export` (zainstalowane).
- Moduł `Microsoft.WinGet.Client` – z PSGallery, nie jest w Windows (anty-cel: nie pobieramy).

## 5. COM API
- Spec: https://github.com/microsoft/winget-cli/blob/master/doc/specs/%23888%20-%20Com%20Api.md ; projekcja C# celuje
  w `net10.0-windows`; z .NET Framework 4.8.1 – niezweryfikowane. Problemy z SYSTEM (#4944) i mieszanym podniesieniem.
  Wniosek: CLI.

## 6. search
- https://github.com/microsoft/winget-cli/blob/master/doc/windows/package-manager/winget/search.md : `--query`, `--id`,
  `--name`, `--source`, `--count` (1–1000), `--exact`.
- Tabela tekstowa `Name Id Version Match Source`, nagłówek tłumaczony; obcinanie „…” – niezweryfikowane.

## 7. Świeży Windows
- „WinGet will not be available until you have logged into Windows as a user for the first time…”; obejście
  `Add-AppxPackage -RegisterByFamilyName -MainPackage Microsoft.DesktopAppInstaller_8wekyb3d8bbwe`:
  https://learn.microsoft.com/en-us/windows/package-manager/winget/
- Zgłoszenie (26H2 build 26300): długi PATH użytkownika – `winget` znika z cmd/PS
  (https://github.com/microsoft/winget-cli/issues/6606); wołać przez pełną ścieżkę aliasu.
- Pomiar 4b (VM 26H2): App Installer 1.26.509.0, winget `v1.11.510`.

## 8. Identyfikatory (folder w `microsoft/winget-pkgs/manifests` istnieje, sprawdzone 2026-10-23)
Google.Chrome, Mozilla.Firefox, Brave.Brave, Opera.Opera, Microsoft.Edge; Discord.Discord, Telegram.TelegramDesktop,
OpenWhisperSystems.Signal, Zoom.Zoom, Microsoft.Teams; VideoLAN.VLC, Spotify.Spotify, CodecGuide.K-LiteCodecPack.Standard,
PeterPawlowski.foobar2000; 7zip.7zip, Notepad++.Notepad++, voidtools.Everything, Microsoft.PowerToys, RARLab.WinRAR,
ShareX.ShareX, Microsoft.WindowsTerminal, Git.Git, CPUID.CPU-Z, CPUID.HWMonitor, TechPowerUp.GPU-Z;
TheDocumentFoundation.LibreOffice, Adobe.Acrobat.Reader.64-bit, SumatraPDF.SumatraPDF; Microsoft.VCRedist.2015+.x64,
Microsoft.VCRedist.2015+.x86, Microsoft.DotNet.DesktopRuntime.8/.9/.10, Microsoft.DirectX; TeamViewer.TeamViewer,
AnyDesk.AnyDesk; Valve.Steam, EpicGames.EpicGamesLauncher, GOG.Galaxy; Intel.IntelDriverAndSupportAssistant.
Brak: RustDesk, Nvidia.GeForceExperience, Nvidia.NvidiaApp, AMD Adrenalin.
