<#
Instrukcja uruchomienia:
    Sesja PowerShell uruchomiona jako administrator, na hoście z Hyper-V, z modułami testów VM (tests\vm\modules) i tests\vm\explore\ExploreCommon.psm1.
    VM utworzone wcześniej przez New-TestVm.ps1 (checkpoint 'czysta-26H2', poświadczenia w %LOCALAPPDATA%\DSOptimizer-vm, autologon 'dso').
    Jedna VM na proces (Home i Pro w dwóch osobnych sesjach), np. (Task 1 Etapu 5):
        .\tests\vm\explore\Invoke-E5Exploration.ps1 -VmName DSO-26H2-Pro -Phase defender,winutil -OutDirectory $env:TEMP\dso-explore\winutil\pro
        .\tests\vm\explore\Invoke-E5Exploration.ps1 -VmName DSO-26H2-Home -Phase defender,winutil -OutDirectory $env:TEMP\dso-explore\winutil\home
    Pomiar pełny (Task 6 Etapu 5) – osobno dla każdej pary VM × wariant wydania (cztery wywołania), np.:
        .\tools\release\Build-Release.ps1 -Variant SingleExe -OutRoot $env:TEMP\dso-explore\wydania
        .\tests\vm\explore\Invoke-E5Exploration.ps1 -VmName DSO-26H2-Pro -Phase defender,av,motw,startup,dpi -Variant SingleExe `
            -ArtifactPath C:\...\wydania\v4.0.0-SingleExe\DSOptimizer-4.0.0.exe -OutDirectory $env:TEMP\dso-explore\pomiar\pro-singleexe
        (Folder: -Variant Folder -ArtifactPath C:\...\wydania\v4.0.0-Folder\DSOptimizer-4.0.0.zip.)
    Bez -Phase: defender i winutil. Faza defender wchodzi zawsze jako pierwsza, gdy przebieg ma jakąkolwiek inną fazę (K1); kolejność
    faz jest stała: defender, winutil, startup, av, motw, dpi (startup przed av – pierwszy start = pierwsze uruchomienie po starcie VM).
    -Variant SingleExe|Folder i -ArtifactPath (plik wydania z tools\release\Build-Release.ps1: DSOptimizer-<wersja>.exe albo .zip, obok
    SHA256SUMS.txt z tą samą sumą) są wymagane przez fazy startup, av, motw i dpi; jedno bez drugiego albo zły plik = błąd przed VM.
    -ShowPlan: wypisuje rozstrzygnięte argumenty i plan (JSON) i kończy, nic nie uruchamia.
    -OutDirectory jest wymagany: ścieżka bezwzględna poza repo; w repo tylko katalog ignorowany przez git.
    -BuildDirectory (domyślnie src\DSOptimizer.App\bin\Release\net481): tylko w planie; fazy mierzą wydanie z -ArtifactPath, nie build.
    Czas: defender + winutil ok. 15–25 min; pełny pomiar ok. 60–120 min na VM (av: naprawa dysku i instalacje z sieci, dpi: restart VM).
Opis działania:
    Pomiar Etapu 5 (spec 3.2, 3.3 pkt 1 i 4, K1) na VM 26H2 z siecią (Default Switch tylko na czas faz defender, winutil, startup,
    av i motw; dpi i reszta przebiegu bez sieci).
    Faza defender: odczyt stanu Defendera (wersja i data sygnatur, usługa, ochrona w czasie rzeczywistym, ochrona w chmurze,
    przesyłanie próbek, ochrona przed naruszeniami – wyłącznie odczyt), Update-MpSignature (jedyna dozwolona akcja wobec Defendera)
    i stan po aktualizacji. Ustawień Defendera skrypt nigdy nie zmienia (bez wyjątków, bez wyłączania ochrony). Nieudana aktualizacja
    = signaturesUpdated = false, dalsze fazy pominięte i zapisane jako pominięte, przebieg kończy się błędem (pomiar się nie liczy).
    Faza winutil: trzy warianty uruchomienia WinUtil, każdy w osobnym widocznym oknie PowerShell startowanym z zadania Harmonogramu
    jako 'dso' (Interactive + Highest, jak DSOptimizer po UAC):
      A – dzisiejsze polecenie katalogu: powershell.exe -NoExit -Command "irm https://christitus.com/win | iex";
      B – winutil.ps1 z najnowszego wydania ChrisTitusTech/winutil (API releases/latest, asset winutil.ps1) pobrany w VM przez
          HttpWebRequest (bez Mark of the Web) do C:\ProgramData\DSOptimizer\downloads\<guid>.ps1 (folder tworzony, gdy go nie ma),
          SHA256 porównany z polem digest API, start: powershell.exe -NoExit -NoProfile -ExecutionPolicy RemoteSigned -File "<plik>";
      C – jak B, ale -ExecutionPolicy Bypass.
    Dla każdego wariantu: znacznik czasu startu (przed pobraniem), proces korzenia po linii poleceń, po 60 s: czy żyje, drzewo procesów,
    okna najwyższego poziomu pulpitu dso (UI Automation, sonda z zadania Harmonogramu) z dokładnymi tytułami, zrzut ekranu VM z hosta
    (GetVirtualSystemThumbnailImage), potem detekcje od startu wariantu (Get-MpThreatDetection + dziennik Defender/Operational,
    zdarzenia 1006, 1007, 1015, 1116, 1117, 1118, 1119, 1121, 1122, 1126) i zamknięcie drzewa procesu.
    Blokada pobranego pliku przez Defendera albo SHA256 ≠ digest to wynik pomiaru (Blocked, okno nie startuje). Wariant jest ważny (Valid)
    tylko bez awarii: sieć/API, start, zrzut, stop, odczyt detekcji i sonda okien jako dso; nieważny wariant albo błąd fazy = kod 1.
    Nośnik (fazy startup/av/motw/dpi): host tworzy VHDX (<OutDirectory>\<VmName>\usb-<wariant>-<czas>.vhdx, MBR + exFAT, etykieta DSO-E5;
    przed formatowaniem bezpiecznik: dysk to świeżo podłączony VHDX z tej ścieżki), kopiuje program do katalogu 'Narzędzia DSO' (spacja
    i polski znak; SingleExe pod nazwą z wydania, Folder wypakowany: DSOptimizer\DSOptimizer.exe) i plik wydania do 'wydanie', odłącza go
    od hosta i podłącza do VM (wolumin z literą). Program startuje z tej ścieżki przez zadanie Harmonogramu jako dso (Interactive + Highest,
    w akcji zadania ścieżka exe W CUDZYSŁOWIE, argumenty dosłownie; kod wyjścia = LastTaskResult).
    Faza startup (z siecią): sonda w sesji dso uruchamia GUI z nośnika 3 razy, czas do okna głównego (UI Automation), pierwszy i mediana.
    Faza av (z siecią): polecenia z planu (apply/undo prywatności i usługi, ochrona systemu + punkt przywracania, panel, skrypty akcji,
    restart powłoki, wu-reset, disk-cleanup, --list-apps i --remove-app pierwszej sugerowanej AppX oraz programu testowego DsoTestQuiet,
    --install-apps install.7zip,install.rustdesk, tools.winutil z oknem po 60 s i Zone.Identifier pobranego winutil.ps1, start GUI bez
    argumentów), każde z raportem --out i kodem; detekcje od startu fazy i kopie logów (także logs\<PC>\ obok exe na VHDX).
    Faza motw (z siecią): kopia wydania z ZoneId=3 na pulpit dso (Folder: zip wypakowany przez powłokę), start przez explorer.exe jako dso
    (Limited, jak dwuklik), zrzut po 5 s (motw.png), co się pokazało (SmartScreen / UAC / Aplikacja), zamknięcie.
    Faza dpi (bez sieci): LogPixels = 144 dla dso, restart VM, GUI z nośnika, zrzut okna (dpi-150.png); skala, która się nie przyjęła =
    'niezweryfikowane' z powodem (K7).
    Przebieg jednej VM: [VHDX] -> revert do 'czysta-26H2' -> karta odłączona -> [VHDX podłączony] -> start (bez sieci) -> gotowość
    (zalogowany dso) -> fazy (sieć tylko na ich czas) -> [surowe pliki z C:\dso\e5\raw do <OutDirectory>\<VmName>\raw] -> ZAWSZE (finally)
    revert, odłączenie VHDX i karty oraz sprawdzenie stanu końcowego (VM Off, karta bez przełącznika, VHDX odłączony) przed zapisem JSON.
    Ostatni bezpiecznik po stronie VM: Assert-E5InsideVm (nazwa komputera DSO-26H2-HOME/PRO i brak DSO_VM_TESTS_MOCK_ONLY) na
    początku każdej funkcji ze skutkiem. Pierwszy błąd uwierzytelnienia przerywa cały skrypt (blokada konta). Skrypt dotyka wyłącznie
    VM DSO-26H2-Home i DSO-26H2-Pro (ValidateSet); na hoście nie łączy się z internetem i zmienia wyłącznie pliki wyników oraz własny,
    nowo utworzony VHDX nośnika.
    RISK: przy fazach startup/av/motw/dpi host tworzy VHDX w katalogu wyników, podłącza go do hosta (Mount-VHD, chwilowa litera dysku),
    inicjalizuje i formatuje (Initialize-Disk, New-Partition, Format-Volume) – WYŁĄCZNIE ten dysk: numer z Mount-VHD, obiekt dysku
    sprawdzony przez Assert-E5UsbHostDisk (BusType File Backed Virtual, Location = ścieżka tego VHDX, RAW, nie rozruchowy/systemowy,
    rozmiar z planu) i dopiero ten obiekt trafia do Initialize-Disk/New-Partition; potem Dismount-VHD i podłączenie do VM.
    Wynik: <OutDirectory>\<VmName>\e5.json (UTF-8), zrzuty <OutDirectory>\<VmName>\winutil-<wariant>.png, motw.png, dpi-150.png, surowe pliki
    (raporty --out, logi, journal) w <OutDirectory>\<VmName>\raw i VHDX nośnika.
    Kod wyjścia: 0 gdy wszystkie wybrane VM zebrane bez błędu przebiegu (sygnatury zaktualizowane, bez błędów faz, wszystkie warianty i fazy
    ważne – m.in. każdy krok av z kodem oczekiwanym i raportem, okna w czasie, detekcje odczytane), 1 w przeciwnym razie (wynik i tak w e5.json).
#>
[CmdletBinding()]
param(
    [ValidateSet('DSO-26H2-Home', 'DSO-26H2-Pro')][string[]]$VmName = @('DSO-26H2-Pro', 'DSO-26H2-Home'),
    [ValidateSet('defender', 'winutil', 'startup', 'av', 'motw', 'dpi')][string[]]$Phase = @('defender', 'winutil'),
    [string]$OutDirectory,
    [string]$CheckpointName = 'czysta-26H2',
    [ValidateRange(60, 7200)][int]$ReadyTimeoutSeconds = 1200,
    [switch]$ShowPlan,
    # Katalog builda Release (domyślnie src\DSOptimizer.App\bin\Release\net481); tylko w planie – fazy mierzą wydanie z -ArtifactPath.
    [string]$BuildDirectory,
    # Wariant wydania i jego plik z tools\release\Build-Release.ps1 (fazy startup, av, motw, dpi).
    [ValidateSet('SingleExe', 'Folder')][string]$Variant,
    [string]$ArtifactPath
)

Import-Module (Join-Path $PSScriptRoot 'ExploreCommon.psm1') -Force
Import-Module (Join-Path (Split-Path -Parent $PSScriptRoot) 'modules\VmSmoke.psm1')

# ---------------------------------------------------------------------------------------------------------------------
# Plan (dane z planu Tasku 1 Etapu 5 i spec 3.3; źródło WinUtil sprawdzone 2026-10-26).
# ---------------------------------------------------------------------------------------------------------------------

function Get-E5Plan {
    [CmdletBinding()]
    param()
    return [ordered]@{
        # Wariant A = dzisiejsze polecenie pozycji tools.winutil (pole launch.arguments katalogu), dosłownie.
        WinUtilCommand        = '-NoExit -Command "irm https://christitus.com/win | iex"'
        WinUtilProcessMarker  = 'christitus.com/win'
        WinUtilApiUrl         = 'https://api.github.com/repos/ChrisTitusTech/winutil/releases/latest'
        WinUtilAssetName      = 'winutil.ps1'
        WinUtilMaxBytes       = 20971520
        ApiMaxBytes           = 1048576
        HttpTimeoutSeconds    = 60
        # Okno WinUtil = tytuł okna z drzewa procesu wariantu zawierający ten wzorzec (bez rozróżniania wielkości liter).
        WinUtilTitlePattern   = 'winutil'
        Variants              = @('A', 'B', 'C')
        # Liść pobrań programu (SecureRoot.DownloadsDirectory); na czystej VM go nie ma – skrypt go tworzy (ruling T1).
        DownloadsDirectory    = 'C:\ProgramData\DSOptimizer\downloads'
        DefenderEventLog      = 'Microsoft-Windows-Windows Defender/Operational'
        DefenderEventIds      = @(1006, 1007, 1015, 1116, 1117, 1118, 1119, 1121, 1122, 1126)
        DefenderNetworkName   = 'go.microsoft.com'
        WinUtilNetworkName    = 'api.github.com'
        NetworkWaitSeconds    = 300
        VariantWaitSeconds    = 60
        ProcessFindSeconds    = 30
        ProbeTimeoutSeconds   = 90
        CloseWaitSeconds      = 3
        WorkDirectory         = 'C:\dso\e5'
        UserName              = 'dso'
        # --- Task 6: wydanie z nośnika (VHDX), funkcje z siecią, SmartScreen, start, 150% ---
        # VHDX tworzony na hoście: MBR + exFAT jak pendrive, program w katalogu ze spacją i polskim znakiem (Review Focus 4),
        # oryginalny plik wydania osobno w 'wydanie' (źródło fazy motw).
        UsbFolderName         = 'Narzędzia DSO'
        UsbReleaseFolder      = 'wydanie'
        UsbVolumeLabel        = 'DSO-E5'
        UsbFileSystem         = 'exFAT'
        UsbSizeBytes          = 1073741824
        UsbWaitSeconds        = 120
        RawDirectory          = 'C:\dso\e5\raw'
        FixturesDirectory     = 'C:\dso\fixtures'
        FixtureKeyName        = 'DsoTestQuiet'
        DsoJournalPath        = 'C:\ProgramData\DSOptimizer\journal\journal.json'
        DsoLogDirectory       = 'C:\ProgramData\DSOptimizer\logs'
        # Okno główne = okno najwyższego poziomu procesu z pliku exe z tytułem od tego przedrostka; program ustawia tytuł w trakcie startu
        # na „<nazwa> <wersja>” (MainWindow.xaml.cs), więc wersja w tytule jest dowolna, a faktyczny tytuł trafia do wyniku.
        MainWindowTitlePrefix = 'DSOptimizer'
        # Tytuły okna SmartScreen (PL/EN) – drugi dowód obok okna procesu smartscreen.exe.
        SmartScreenTitles     = @('System Windows ochronił ten komputer', 'Windows protected your PC')
        TaskPollSeconds       = 2
        CliTimeoutSeconds     = 600
        LaunchWaitSeconds     = 5
        ShellWaitSeconds      = 90
        GuiWindowSeconds      = 60
        StartupRuns           = 3
        StartupWindowTimeoutSeconds = 120
        StartupCloseTimeoutSeconds  = 20
        StartupPauseSeconds   = 10
        MotwWaitSeconds       = 5
        MotwZoneId            = 3
        ExtractTimeoutSeconds = 120
        DpiLogPixels          = 144
        DpiScalePercent       = 150
        # Faza av – kolejność i polecenia z planu Tasku 6 (dosłownie); --out dokłada skrypt. Kod oczekiwany 0 (CliExitCodes: sukces).
        AvSteps               = @(
            [ordered]@{ Name = 'apply-advertising-id'; Kind = 'cli'; Arguments = '--apply privacy.advertising-id' }
            [ordered]@{ Name = 'undo-advertising-id'; Kind = 'cli'; Arguments = '--undo privacy.advertising-id' }
            [ordered]@{ Name = 'apply-diagtrack'; Kind = 'cli'; Arguments = '--apply privacy.diagtrack' }
            [ordered]@{ Name = 'undo-diagtrack'; Kind = 'cli'; Arguments = '--undo privacy.diagtrack' }
            [ordered]@{ Name = 'apply-system-protection'; Kind = 'cli'; Arguments = '--apply repair.system-protection' }
            [ordered]@{ Name = 'restore-point'; Kind = 'cli'; Arguments = '--execute repair.restore-point' }
            [ordered]@{ Name = 'undo-system-protection'; Kind = 'cli'; Arguments = '--undo repair.system-protection' }
            [ordered]@{ Name = 'computer-management'; Kind = 'cli'; Arguments = '--execute panels.computer-management'; CloseProcess = 'mmc.exe' }
            [ordered]@{ Name = 'dns-flush'; Kind = 'cli'; Arguments = '--execute repair.dns-flush' }
            [ordered]@{ Name = 'restart-explorer'; Kind = 'cli'; Arguments = '--execute tools.restart-explorer'; WaitShell = $true }
            [ordered]@{ Name = 'wu-reset'; Kind = 'cli'; Arguments = '--execute repair.wu-reset' }
            [ordered]@{ Name = 'disk-cleanup'; Kind = 'cli'; Arguments = '--execute repair.disk-cleanup'; TimeoutSeconds = 3600 }
            [ordered]@{ Name = 'list-apps'; Kind = 'cli'; Arguments = '--list-apps' }
            [ordered]@{ Name = 'remove-appx'; Kind = 'remove-appx'; ListFrom = 'list-apps'; RestorePoint = 'skip' }
            [ordered]@{ Name = 'remove-win32'; Kind = 'remove-win32'; AppId = 'win32:HKLM\SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall\DsoTestQuiet'; RestorePoint = 'ifavailable' }
            [ordered]@{ Name = 'install-apps'; Kind = 'cli'; Arguments = '--install-apps install.7zip,install.rustdesk'; TimeoutSeconds = 3600 }
            [ordered]@{ Name = 'winutil'; Kind = 'winutil'; Arguments = '--execute tools.winutil' }
            [ordered]@{ Name = 'gui'; Kind = 'gui' }
        )
    }
}

# ---------------------------------------------------------------------------------------------------------------------
# Funkcje wysyłane do VM (samowystarczalne: wołają siebie nawzajem, funkcje Explore z modułu i cmdlety systemowe).
# ---------------------------------------------------------------------------------------------------------------------

function Test-E5VmComputerName {
    # Nazwy komputerów VM nadawane przez New-TestVm.ps1 (VmBuild: nazwa VM wielkimi literami).
    [CmdletBinding()]
    [OutputType([bool])]
    param([AllowEmptyString()][string]$ComputerName)
    return (@('DSO-26H2-HOME', 'DSO-26H2-PRO') -contains ([string]$ComputerName).ToUpperInvariant())
}

function Assert-E5InsideVm {
    # Ostatni bezpiecznik: sondy E5 aktualizują sygnatury Defendera, łączą się z GitHubem, rejestrują zadania Harmonogramu, uruchamiają
    # WinUtil i zamykają procesy, więc działają WYŁĄCZNIE w VM DSO-26H2-Home/Pro i nigdy w testach na mockach (DSO_VM_TESTS_MOCK_ONLY).
    [CmdletBinding()]
    param([string]$ComputerName = $env:COMPUTERNAME)
    if ($env:DSO_VM_TESTS_MOCK_ONLY) {
        throw 'Tryb DSO_VM_TESTS_MOCK_ONLY: sondy E5 nie mogą działać w testach na mockach (nic nie zostało uruchomione).'
    }
    if (-not (Test-E5VmComputerName -ComputerName $ComputerName)) {
        throw "Sondy E5 działają tylko w VM DSO-26H2-HOME/DSO-26H2-PRO (ten komputer: '$ComputerName'); nic nie zostało uruchomione."
    }
}

function Format-E5Error {
    [CmdletBinding()]
    [OutputType([string])]
    param([Parameter(Mandatory)]$ErrorRecord)
    $Exception = if ($ErrorRecord -is [System.Management.Automation.ErrorRecord]) { $ErrorRecord.Exception } else { $ErrorRecord }
    $Base = $Exception.GetBaseException()
    return ('0x{0:X8} {1}' -f $Base.HResult, $Base.Message)
}

function Format-E5Hex {
    # Kod wyjścia (int ze znakiem albo uint32 z LastTaskResult) -> '0x00000001'; brak kodu -> $null.
    [CmdletBinding()]
    param([AllowNull()]$Code)
    if ($null -eq $Code -or [string]$Code -eq '') { return $null }
    $Long = [int64]$Code
    if ($Long -gt [int]::MaxValue) { $Long -= 4294967296 }
    return ('0x{0:X8}' -f [int]$Long)
}

function Get-E5Property {
    # Wartość pola obiektu albo słownika; brak obiektu albo pola = $null (odporne na StrictMode i dokumenty z ConvertFrom-Json).
    [CmdletBinding()]
    param(
        [AllowNull()]$Object,
        [Parameter(Mandatory)][string]$Name
    )
    if ($null -eq $Object) { return $null }
    if ($Object -is [System.Collections.IDictionary]) {
        if ($Object.Contains($Name)) { return $Object[$Name] }
        return $null
    }
    $Property = $Object.PSObject.Properties[$Name]
    if ($null -eq $Property) { return $null }
    return $Property.Value
}

function Assert-E5PlainPath {
    # Ścieżka w linii argumentów zadania bez cytowania: bez spacji, tabulatorów i cudzysłowów.
    [CmdletBinding()]
    param([Parameter(Mandatory)][AllowEmptyString()][string]$Path)
    if ([string]::IsNullOrWhiteSpace($Path) -or $Path -match '[\s"]') { throw "Ścieżka '$Path' musi być niepusta, bez spacji i cudzysłowów." }
}

function Test-E5Network {
    # Rozwiązanie nazwy (tylko odczyt); WaitSeconds = 0 -> jedna próba.
    [CmdletBinding()]
    param(
        [Parameter(Mandatory)][string]$Name,
        [int]$WaitSeconds = 0
    )
    $Watch = [System.Diagnostics.Stopwatch]::StartNew()
    $Addresses = @()
    while ($true) {
        try { $Addresses = @([System.Net.Dns]::GetHostAddresses($Name) | ForEach-Object { $_.IPAddressToString }) } catch { $Addresses = @() }
        if ($Addresses.Count -gt 0 -or $Watch.Elapsed.TotalSeconds -ge $WaitSeconds) { break }
        Start-Sleep -Seconds 5
    }
    return [pscustomobject]@{ Name = $Name; Resolved = ($Addresses.Count -gt 0); Addresses = $Addresses; Seconds = [math]::Round($Watch.Elapsed.TotalSeconds, 1) }
}

# --- Defender: odczyt, aktualizacja sygnatur, detekcje ---

function Get-E5DefenderState {
    # Stan Defendera – WYŁĄCZNIE odczyt (Get-MpComputerStatus, Get-MpPreference). Błąd odczytu trafia do Errors, nie przerywa.
    [CmdletBinding()]
    param()
    $StatusFields = @('AMServiceEnabled', 'RealTimeProtectionEnabled', 'IsTamperProtected', 'AntivirusEnabled', 'AMProductVersion', 'AMEngineVersion',
        'AntivirusSignatureVersion', 'AntivirusSignatureLastUpdated', 'NISSignatureVersion')
    $Out = [ordered]@{}
    foreach ($Field in $StatusFields) { $Out[$Field] = $null }
    $Out.MAPSReporting = $null
    $Out.SubmitSamplesConsent = $null
    $Out.ReadAt = (Get-Date).ToString('o')
    $Errors = @()
    try {
        $Status = Get-MpComputerStatus -ErrorAction Stop
        foreach ($Field in $StatusFields) {
            $Value = Get-E5Property -Object $Status -Name $Field
            if ($Value -is [datetime]) { $Value = $Value.ToString('o') }
            $Out[$Field] = $Value
        }
    }
    catch { $Errors += "Get-MpComputerStatus: $(Format-E5Error $_)" }
    try {
        $Preference = Get-MpPreference -ErrorAction Stop
        $Out.MAPSReporting = Get-E5Property -Object $Preference -Name 'MAPSReporting'
        $Out.SubmitSamplesConsent = Get-E5Property -Object $Preference -Name 'SubmitSamplesConsent'
    }
    catch { $Errors += "Get-MpPreference: $(Format-E5Error $_)" }
    $Out.Errors = $Errors
    return [pscustomobject]$Out
}

function Update-E5Signatures {
    # K1: Update-MpSignature (jedyna akcja wobec Defendera w całym skrypcie), stan przed i po. Sukces = brak wyjątku.
    [CmdletBinding()]
    param()
    Assert-E5InsideVm
    $Out = [ordered]@{ Before = (Get-E5DefenderState); Succeeded = $false; Error = $null; Seconds = $null; After = $null; SignatureChanged = $null }
    $Watch = [System.Diagnostics.Stopwatch]::StartNew()
    try {
        Update-MpSignature -ErrorAction Stop
        $Out.Succeeded = $true
    }
    catch { $Out.Error = Format-E5Error $_ }
    $Out.Seconds = [math]::Round($Watch.Elapsed.TotalSeconds, 1)
    $Out.After = Get-E5DefenderState
    $Out.SignatureChanged = ([string](Get-E5Property -Object $Out.Before -Name 'AntivirusSignatureVersion') -ne [string](Get-E5Property -Object $Out.After -Name 'AntivirusSignatureVersion'))
    return [pscustomobject]$Out
}

function Get-E5Detections {
    # Detekcje od znacznika czasu (tylko odczyt): Get-MpThreatDetection (z nazwą z Get-MpThreat) i zdarzenia dziennika Defendera
    # o podanych Id. Rekord wykrycia liczy się, gdy InitialDetectionTime ALBO LastThreatStatusChangeTime jest od znacznika (Defender
    # ponownie używa rekordu tego samego zagrożenia i zasobu – wtedy Reused = true). ThreatCount = rekordy wykryć, EventCount = zdarzenia
    # dziennika (to zwykle te same wykrycia widziane drugą drogą, więc ich nie sumujemy), Any = cokolwiek z dwóch.
    # Brak zdarzeń (NoMatchingEventsFound) to nie błąd; inne błędy trafiają do Errors.
    [CmdletBinding()]
    param(
        [Parameter(Mandatory)][datetime]$Since,
        [Parameter(Mandatory)][string]$LogName,
        [Parameter(Mandatory)][int[]]$EventIds
    )
    $Threats = @()
    $Events = @()
    $Errors = @()
    $Names = @{}
    try {
        foreach ($Threat in @(Get-MpThreat -ErrorAction Stop)) {
            if ($null -ne $Threat) { $Names[[string](Get-E5Property -Object $Threat -Name 'ThreatID')] = [string](Get-E5Property -Object $Threat -Name 'ThreatName') }
        }
    }
    catch { $Errors += "Get-MpThreat: $(Format-E5Error $_)" }
    try {
        foreach ($Detection in @(Get-MpThreatDetection -ErrorAction Stop)) {
            if ($null -eq $Detection) { continue }
            $At = Get-E5Property -Object $Detection -Name 'InitialDetectionTime'
            $Changed = Get-E5Property -Object $Detection -Name 'LastThreatStatusChangeTime'
            $Fresh = ($null -ne $At -and [datetime]$At -ge $Since)
            $Touched = ($null -ne $Changed -and [datetime]$Changed -ge $Since)
            if (-not $Fresh -and -not $Touched) { continue }
            $Id = [string](Get-E5Property -Object $Detection -Name 'ThreatID')
            $Threats += [pscustomobject]@{
                ThreatID = $Id; ThreatName = $Names[$Id]; InitialDetectionTime = $(if ($null -ne $At) { ([datetime]$At).ToString('o') } else { $null })
                LastThreatStatusChangeTime = $(if ($null -ne $Changed) { ([datetime]$Changed).ToString('o') } else { $null }); Reused = (-not $Fresh)
                ProcessName = [string](Get-E5Property -Object $Detection -Name 'ProcessName'); Resources = @(Get-E5Property -Object $Detection -Name 'Resources')
                ActionSuccess = Get-E5Property -Object $Detection -Name 'ActionSuccess'; ThreatStatusID = Get-E5Property -Object $Detection -Name 'ThreatStatusID'
                DetectionSourceTypeID = Get-E5Property -Object $Detection -Name 'DetectionSourceTypeID'; DomainUser = [string](Get-E5Property -Object $Detection -Name 'DomainUser')
            }
        }
    }
    catch { $Errors += "Get-MpThreatDetection: $(Format-E5Error $_)" }
    try {
        $Filter = @{ LogName = $LogName; Id = $EventIds; StartTime = $Since }
        foreach ($LogEvent in @(Get-WinEvent -FilterHashtable $Filter -ErrorAction Stop)) {
            if ($null -eq $LogEvent) { continue }
            $Message = [string]$LogEvent.Message
            if ($Message.Length -gt 4000) { $Message = $Message.Substring(0, 4000) }
            $Events += [pscustomobject]@{ Id = [int]$LogEvent.Id; TimeCreated = ([datetime]$LogEvent.TimeCreated).ToString('o'); Level = [string]$LogEvent.LevelDisplayName; Message = $Message }
        }
    }
    catch {
        if ([string]$_.FullyQualifiedErrorId -notlike 'NoMatchingEventsFound*') { $Errors += "Get-WinEvent ${LogName}: $(Format-E5Error $_)" }
    }
    return [pscustomobject]@{ Since = $Since.ToString('o'); Threats = $Threats; Events = $Events; ThreatCount = $Threats.Count; EventCount = $Events.Count
        Any = (($Threats.Count + $Events.Count) -gt 0); Errors = $Errors }
}

function Invoke-E5DefenderPhase {
    # Faza defender (z siecią): sieć -> Update-MpSignature ze stanem przed i po -> detekcje od startu fazy. Bez sieci aktualizacja nie startuje.
    [CmdletBinding()]
    param([Parameter(Mandatory)]$Plan)
    Assert-E5InsideVm
    $Since = Get-Date
    $Out = [ordered]@{ Since = $Since.ToString('o'); Network = $null; Update = $null; SignaturesUpdated = $false; Detections = $null; Error = $null }
    $Out.Network = Test-E5Network -Name ([string]$Plan.DefenderNetworkName) -WaitSeconds ([int]$Plan.NetworkWaitSeconds)
    if (-not $Out.Network.Resolved) {
        $Out.Error = "Brak sieci w VM: '$($Plan.DefenderNetworkName)' nie rozwiązuje się po $($Out.Network.Seconds) s; Update-MpSignature nie został uruchomiony."
    }
    else {
        $Out.Update = Update-E5Signatures
        $Out.SignaturesUpdated = [bool]$Out.Update.Succeeded
        if (-not $Out.SignaturesUpdated) { $Out.Error = "Update-MpSignature: $($Out.Update.Error)" }
    }
    $Out.Detections = Get-E5Detections -Since $Since -LogName ([string]$Plan.DefenderEventLog) -EventIds @($Plan.DefenderEventIds | ForEach-Object { [int]$_ })
    return [pscustomobject]$Out
}

# --- WinUtil: wydanie z GitHuba, pobranie bez MotW, warianty uruchomienia ---

function Select-E5WinUtilAsset {
    # Odpowiedź API releases/latest -> jedyny asset o dokładnej nazwie z digest sha256. Wydanie prerelease/draft, brak albo dwa assety,
    # brak albo zły digest = wyjątek (nic nie jest pobierane).
    [CmdletBinding()]
    param(
        [Parameter(Mandatory)]$Release,
        [Parameter(Mandatory)][string]$AssetName
    )
    if ((Get-E5Property -Object $Release -Name 'prerelease') -eq $true) { throw 'Wydanie WinUtil jest oznaczone jako prerelease.' }
    if ((Get-E5Property -Object $Release -Name 'draft') -eq $true) { throw 'Wydanie WinUtil jest oznaczone jako draft.' }
    $Tag = [string](Get-E5Property -Object $Release -Name 'tag_name')
    if ([string]::IsNullOrWhiteSpace($Tag)) { throw 'Odpowiedź API bez tag_name.' }
    $Assets = @(@(Get-E5Property -Object $Release -Name 'assets') | Where-Object { $null -ne $_ -and [string](Get-E5Property -Object $_ -Name 'name') -ceq $AssetName })
    if ($Assets.Count -ne 1) { throw "Wydanie ${Tag}: oczekiwano dokładnie jednego assetu '$AssetName', jest $($Assets.Count)." }
    $Digest = [string](Get-E5Property -Object $Assets[0] -Name 'digest')
    if ($Digest -cnotmatch '^sha256:[0-9A-Fa-f]{64}\z') { throw "Wydanie ${Tag}: asset '$AssetName' bez poprawnego pola digest sha256 ('$Digest')." }
    $Url = [string](Get-E5Property -Object $Assets[0] -Name 'browser_download_url')
    if ($Url -notmatch '^https://') { throw "Wydanie ${Tag}: adres pobrania '$Url' nie jest https." }
    return [pscustomobject]@{ Tag = $Tag; Name = $AssetName; Size = Get-E5Property -Object $Assets[0] -Name 'size'; Sha256 = $Digest.Substring(7).ToLowerInvariant(); Url = $Url }
}

function Invoke-E5HttpText {
    # GET tekstu (UTF-8) przez HttpWebRequest (User-Agent wymagany przez API GitHuba). TLS jak WebDownloader (R4): SystemDefault bez zmian,
    # inaczej dopisane Tls12.
    [CmdletBinding()]
    [OutputType([string])]
    param(
        [Parameter(Mandatory)][string]$Url,
        [Parameter(Mandatory)][int]$TimeoutSeconds,
        [Parameter(Mandatory)][int]$MaxBytes
    )
    Assert-E5InsideVm
    if ([System.Net.ServicePointManager]::SecurityProtocol -ne [System.Net.SecurityProtocolType]::SystemDefault) {
        [System.Net.ServicePointManager]::SecurityProtocol = [System.Net.ServicePointManager]::SecurityProtocol -bor [System.Net.SecurityProtocolType]::Tls12
    }
    $Request = [System.Net.HttpWebRequest][System.Net.WebRequest]::Create($Url)
    $Request.Method = 'GET'
    $Request.UserAgent = 'DSOptimizer'
    $Request.Accept = 'application/vnd.github+json'
    $Request.Timeout = $TimeoutSeconds * 1000
    $Request.ReadWriteTimeout = $TimeoutSeconds * 1000
    $Response = $Request.GetResponse()
    try {
        $Stream = $Response.GetResponseStream()
        $Buffer = New-Object System.IO.MemoryStream
        $Chunk = New-Object byte[] 65536
        while (($Read = $Stream.Read($Chunk, 0, $Chunk.Length)) -gt 0) {
            $Buffer.Write($Chunk, 0, $Read)
            if ($Buffer.Length -gt $MaxBytes) { throw "Odpowiedź '$Url' przekracza $MaxBytes B." }
        }
        return [System.Text.Encoding]::UTF8.GetString($Buffer.ToArray())
    }
    finally { $Response.Dispose() }
}

function Save-E5HttpFile {
    # GET pliku przez HttpWebRequest prosto do nowego pliku (bez Zone.Identifier – nie przez Invoke-WebRequest); limit bajtów.
    [CmdletBinding()]
    param(
        [Parameter(Mandatory)][string]$Url,
        [Parameter(Mandatory)][string]$Path,
        [Parameter(Mandatory)][int]$TimeoutSeconds,
        [Parameter(Mandatory)][int]$MaxBytes
    )
    Assert-E5InsideVm
    if ([System.Net.ServicePointManager]::SecurityProtocol -ne [System.Net.SecurityProtocolType]::SystemDefault) {
        [System.Net.ServicePointManager]::SecurityProtocol = [System.Net.ServicePointManager]::SecurityProtocol -bor [System.Net.SecurityProtocolType]::Tls12
    }
    $Request = [System.Net.HttpWebRequest][System.Net.WebRequest]::Create($Url)
    $Request.Method = 'GET'
    $Request.UserAgent = 'DSOptimizer'
    $Request.Timeout = $TimeoutSeconds * 1000
    $Request.ReadWriteTimeout = $TimeoutSeconds * 1000
    $Response = $Request.GetResponse()
    $Total = 0L
    $FinalUrl = [string]$Response.ResponseUri
    try {
        $Stream = $Response.GetResponseStream()
        $File = [System.IO.File]::Open($Path, [System.IO.FileMode]::CreateNew, [System.IO.FileAccess]::Write, [System.IO.FileShare]::None)
        try {
            $Chunk = New-Object byte[] 65536
            while (($Read = $Stream.Read($Chunk, 0, $Chunk.Length)) -gt 0) {
                $Total += $Read
                if ($Total -gt $MaxBytes) { throw "Plik '$Url' przekracza $MaxBytes B." }
                $File.Write($Chunk, 0, $Read)
            }
        }
        finally { $File.Dispose() }
    }
    finally { $Response.Dispose() }
    return [pscustomobject]@{ Bytes = $Total; FinalUrl = $FinalUrl }
}

function Save-E5WinUtilDownload {
    # Warianty B/C: folder pobrań (tworzony, gdy go nie ma) -> API releases/latest -> winutil.ps1 do <guid>.ps1 -> SHA256 z pliku na dysku
    # porównany z digest -> Zone.Identifier (oczekiwany brak) i podpis Authenticode (tylko odczyt). Błąd = pole Error i etap (Stage);
    # blokada pliku przez Defendera (BlockedByDefender) to wynik pomiaru, nie awaria – patrz Test-E5DefenderBlock.
    [CmdletBinding()]
    param([Parameter(Mandatory)]$Plan)
    Assert-E5InsideVm
    $Directory = [string]$Plan.DownloadsDirectory
    $Out = [ordered]@{ DownloadsDirectory = $Directory; DirectoryCreated = $false; Release = $null; Path = $null; Bytes = $null; FinalUrl = $null; Sha256 = $null; ExpectedSha256 = $null
        HashMatches = $false; ZoneIdentifier = $null; Signature = $null; SignatureSubject = $null; Stage = $null; BlockedByDefender = $false; Error = $null }
    $Stage = 'folder'
    try {
        if (-not [System.IO.Directory]::Exists($Directory)) {
            $null = [System.IO.Directory]::CreateDirectory($Directory)
            $Out.DirectoryCreated = $true
        }
        $Stage = 'api'
        $Text = Invoke-E5HttpText -Url ([string]$Plan.WinUtilApiUrl) -TimeoutSeconds ([int]$Plan.HttpTimeoutSeconds) -MaxBytes ([int]$Plan.ApiMaxBytes)
        $Stage = 'release'
        $Asset = Select-E5WinUtilAsset -Release ($Text | ConvertFrom-Json) -AssetName ([string]$Plan.WinUtilAssetName)
        $Out.Release = $Asset
        $Out.ExpectedSha256 = $Asset.Sha256
        $Path = Join-Path $Directory ('{0}.ps1' -f [guid]::NewGuid().ToString('N'))
        $Out.Path = $Path
        $Stage = 'download'
        $Download = Save-E5HttpFile -Url $Asset.Url -Path $Path -TimeoutSeconds ([int]$Plan.HttpTimeoutSeconds) -MaxBytes ([int]$Plan.WinUtilMaxBytes)
        $Out.Bytes = $Download.Bytes
        $Out.FinalUrl = $Download.FinalUrl
        $Stage = 'hash'
        $Out.Sha256 = (Get-FileHash -LiteralPath $Path -Algorithm SHA256 -ErrorAction Stop).Hash.ToLowerInvariant()
        $Out.HashMatches = ($Out.Sha256 -ceq $Out.ExpectedSha256)
        $Stage = 'signature'
        $Out.ZoneIdentifier = ($null -ne (Get-Item -LiteralPath $Path -Stream 'Zone.Identifier' -ErrorAction SilentlyContinue))
        $Signature = Get-AuthenticodeSignature -LiteralPath $Path -ErrorAction Stop
        $Out.Signature = [string]$Signature.Status
        if ($null -ne $Signature.SignerCertificate) { $Out.SignatureSubject = [string]$Signature.SignerCertificate.Subject }
        $Stage = 'done'
    }
    catch {
        $Out.Error = Format-E5Error $_
        $Out.BlockedByDefender = Test-E5DefenderBlock -ErrorRecord $_ -Stage $Stage -Path ([string]$Out.Path)
    }
    $Out.Stage = $Stage
    return [pscustomobject]$Out
}

function Test-E5DefenderBlock {
    # Czy błąd przy pliku WinUtil to blokada antywirusa (wynik pomiaru), a nie awaria sieci/API: kod ERROR_VIRUS_INFECTED (0x800700E1)
    # albo ERROR_VIRUS_DELETED (0x800700E2) od etapu zapisu pliku, albo plik zapisany i zniknięty (kwarantanna) przy odczycie po zapisie.
    [CmdletBinding()]
    [OutputType([bool])]
    param(
        [Parameter(Mandatory)]$ErrorRecord,
        [Parameter(Mandatory)][string]$Stage,
        [AllowEmptyString()][string]$Path
    )
    if (@('download', 'hash', 'signature') -notcontains $Stage) { return $false }
    $Exception = if ($ErrorRecord -is [System.Management.Automation.ErrorRecord]) { $ErrorRecord.Exception } else { $ErrorRecord }
    $Code = $Exception.GetBaseException().HResult
    if (@(-2147024671, -2147024670) -contains $Code) { return $true }
    return ($Stage -ne 'download' -and -not [string]::IsNullOrEmpty($Path) -and -not [System.IO.File]::Exists($Path))
}

function Get-E5VariantArguments {
    # Argumenty powershell.exe wariantu: A = polecenie katalogu dosłownie; B/C = -File "<plik>" z folderu pobrań (<guid>.ps1), B RemoteSigned, C Bypass.
    [CmdletBinding()]
    [OutputType([string])]
    param(
        [Parameter(Mandatory)]$Plan,
        [Parameter(Mandatory)][ValidateSet('A', 'B', 'C')][string]$Variant,
        [string]$Path
    )
    if ($Variant -eq 'A') { return [string]$Plan.WinUtilCommand }
    $Directory = ([string]$Plan.DownloadsDirectory).TrimEnd('\')
    $Pattern = '^' + [regex]::Escape($Directory) + '\\[0-9a-f]{32}\.ps1\z'
    if ([string]$Path -cnotmatch $Pattern) { throw "Plik wariantu $Variant musi leżeć bezpośrednio w '$Directory' jako <guid>.ps1 (podano '$Path')." }
    $Policy = if ($Variant -eq 'B') { 'RemoteSigned' } else { 'Bypass' }
    return "-NoExit -NoProfile -ExecutionPolicy $Policy -File `"$Path`""
}

function Start-E5UserProcess {
    # Widoczne okno powershell.exe (System32) z argumentami wariantu jako dso z zadania Harmonogramu (Interactive + Highest), bez limitu czasu.
    # Zadanie zostaje zarejestrowane (wyrejestrowanie w Stop-E5Variant po zamknięciu drzewa; revert VM i tak je usuwa).
    [CmdletBinding()]
    param(
        [Parameter(Mandatory)]$Plan,
        [Parameter(Mandatory)][ValidatePattern('^DSO-E5-[A-Za-z0-9-]+\z')][string]$TaskName,
        [Parameter(Mandatory)][string]$Arguments
    )
    Assert-E5InsideVm
    $Executable = Join-Path $env:SystemRoot 'System32\WindowsPowerShell\v1.0\powershell.exe'
    $Action = New-ScheduledTaskAction -Execute $Executable -Argument $Arguments
    $Principal = New-ScheduledTaskPrincipal -UserId ([string]$Plan.UserName) -LogonType Interactive -RunLevel Highest
    $Settings = New-ScheduledTaskSettingsSet -AllowStartIfOnBatteries -DontStopIfGoingOnBatteries -ExecutionTimeLimit ([TimeSpan]::Zero)
    $null = Register-ScheduledTask -TaskName $TaskName -Action $Action -Principal $Principal -Settings $Settings -Force -ErrorAction Stop
    Start-ScheduledTask -TaskName $TaskName -ErrorAction Stop
}

function Find-E5RootProcess {
    # PID procesu powershell.exe ze znacznikiem w linii poleceń, utworzonego po starcie wariantu (najwcześniejszy); brak w limicie = $null.
    [CmdletBinding()]
    param(
        [Parameter(Mandatory)][string]$Marker,
        [Parameter(Mandatory)][datetime]$Since,
        [ValidateRange(0, 600)][int]$TimeoutSeconds = 30
    )
    $Watch = [System.Diagnostics.Stopwatch]::StartNew()
    $Floor = $Since.AddSeconds(-5)
    while ($true) {
        $Found = @(Get-CimInstance -ClassName Win32_Process -Filter "Name='powershell.exe'" -ErrorAction SilentlyContinue | Where-Object {
                $null -ne $_ -and $null -ne $_.CommandLine -and ([string]$_.CommandLine).IndexOf($Marker, [System.StringComparison]::OrdinalIgnoreCase) -ge 0 -and
                ($null -eq $_.CreationDate -or [datetime]$_.CreationDate -ge $Floor)
            } | Sort-Object -Property CreationDate)
        if ($Found.Count -gt 0) { return [int]$Found[0].ProcessId }
        if ($Watch.Elapsed.TotalSeconds -ge $TimeoutSeconds) { return $null }
        Start-Sleep -Seconds 1
    }
}

function Get-E5ProcessTree {
    # Korzeń i potomkowie (BFS po ParentProcessId; potomek starszy od rodzica = ponownie użyty PID, pomijany). Tylko odczyt.
    [CmdletBinding()]
    param([Parameter(Mandatory)][int]$RootPid)
    $All = @(Get-CimInstance -ClassName Win32_Process -ErrorAction Stop | Where-Object { $null -ne $_ })
    $Root = @($All | Where-Object { [int]$_.ProcessId -eq $RootPid }) | Select-Object -First 1
    if ($null -eq $Root) { return @() }
    $Result = New-Object System.Collections.Generic.List[object]
    $Queue = New-Object System.Collections.Generic.Queue[object]
    $Queue.Enqueue($Root)
    while ($Queue.Count -gt 0) {
        $Node = $Queue.Dequeue()
        $Line = [string]$Node.CommandLine
        if ($Line.Length -gt 1000) { $Line = $Line.Substring(0, 1000) }
        $Created = if ($null -ne $Node.CreationDate) { ([datetime]$Node.CreationDate).ToString('o') } else { $null }
        $Result.Add([pscustomobject]@{ ProcessId = [int]$Node.ProcessId; ParentProcessId = [int]$Node.ParentProcessId; Name = [string]$Node.Name; CommandLine = $Line; CreationDate = $Created })
        foreach ($Child in @($All | Where-Object {
                    [int]$_.ParentProcessId -eq [int]$Node.ProcessId -and [int]$_.ProcessId -ne [int]$Node.ProcessId -and
                    ($null -eq $_.CreationDate -or $null -eq $Node.CreationDate -or [datetime]$_.CreationDate -ge [datetime]$Node.CreationDate)
                })) {
            $Queue.Enqueue($Child)
        }
    }
    return $Result.ToArray()
}

function Get-E5WindowProbeScript {
    # Treść sondy uruchamianej w sesji dso (zadanie Harmonogramu): okna najwyższego poziomu pulpitu – UI Automation (PID, tytuł, klasa)
    # i niezależnie Win32 EnumWindows (widoczne okna: PID, nazwa procesu, tytuł, klasa; UIA nie zawsze widzi np. okno SmartScreen).
    # Tylko odczyt; poza VM i w trybie mock-only odmawia przed odczytem okien i zapisuje wynik z błędem.
    [CmdletBinding()]
    [OutputType([string])]
    param()
    $Body = @'
# Sonda okien pomiaru E5 (DSOptimizer, tests\vm\explore\Invoke-E5Exploration.ps1). Tylko w VM DSO-26H2-HOME/PRO, tylko odczyt.
$ErrorActionPreference = 'Stop'
$Out = [ordered]@{ Sid = $null; Elevated = $null; Windows = @(); Win32Windows = @(); Win32Error = $null; Error = $null }
try {
    if ($env:DSO_VM_TESTS_MOCK_ONLY) { throw 'DSO_VM_TESTS_MOCK_ONLY: sonda nie czyta okien w testach.' }
    if (@('DSO-26H2-HOME', 'DSO-26H2-PRO') -notcontains ([string]$env:COMPUTERNAME).ToUpperInvariant()) { throw "Sonda dziala tylko w VM DSO-26H2-HOME/PRO (komputer: '$env:COMPUTERNAME')." }
    $Identity = [System.Security.Principal.WindowsIdentity]::GetCurrent()
    $Out.Sid = $Identity.User.Value
    $Out.Elevated = (New-Object System.Security.Principal.WindowsPrincipal($Identity)).IsInRole([System.Security.Principal.WindowsBuiltInRole]::Administrator)
    try { $Out.Win32Windows = @(Get-ProbeWin32Windows) } catch { $Out.Win32Error = [string]$_.Exception.GetBaseException().Message }
    Add-Type -AssemblyName UIAutomationClient, UIAutomationTypes
    $Root = [System.Windows.Automation.AutomationElement]::RootElement
    $Items = $Root.FindAll([System.Windows.Automation.TreeScope]::Children, [System.Windows.Automation.Condition]::TrueCondition)
    $List = New-Object System.Collections.Generic.List[object]
    foreach ($Item in $Items) {
        try {
            $Current = $Item.Current
            $List.Add([pscustomobject]@{ ProcessId = [int]$Current.ProcessId; Name = [string]$Current.Name; ClassName = [string]$Current.ClassName })
        }
        catch { $null = $_ }
    }
    $Out.Windows = $List.ToArray()
}
catch { $Out.Error = [string]$_.Exception.GetBaseException().Message }
$Tmp = "$ResultFile.tmp"
[System.IO.File]::WriteAllText($Tmp, ($Out | ConvertTo-Json -Depth 5), (New-Object System.Text.UTF8Encoding($false)))
Move-Item -LiteralPath $Tmp -Destination $ResultFile -Force
'@
    return ("param([Parameter(Mandatory)][string]`$ResultFile)`n" + (Get-E5ProbeHelpersText) + "`n" + $Body)
}

function Select-E5MainWindow {
    # Okno główne programu wśród okien najwyższego poziomu (czysta logika, wysyłana też do sond): okno procesu z listy PID (procesy
    # uruchomione z pliku exe), z niepustym tytułem zaczynającym się od TitlePrefix i spoza klasy okien dialogowych (#32770 – np. komunikat
    # o błędzie). Tytuł NIE jest porównywany dokładnie: program ustawia go w trakcie startu na „<nazwa> <wersja>” (np. „DSOptimizer 4.0.0”).
    [CmdletBinding()]
    param(
        [AllowEmptyCollection()][AllowNull()][object[]]$Windows = @(),
        [AllowEmptyCollection()][int[]]$ProcessIds = @(),
        [Parameter(Mandatory)][string]$TitlePrefix
    )
    foreach ($Window in @($Windows)) {
        if ($null -eq $Window) { continue }
        if ($ProcessIds -notcontains [int]$Window.ProcessId) { continue }
        $Name = [string]$Window.Name
        if ([string]::IsNullOrWhiteSpace($Name) -or [string]$Window.ClassName -eq '#32770') { continue }
        if (-not $Name.StartsWith($TitlePrefix, [System.StringComparison]::OrdinalIgnoreCase)) { continue }
        return $Window
    }
    return $null
}

function Get-E5ProbeHelpersText {
    # Wspólne funkcje sond (tekst wstawiany po bloku param): Get-ProbeWin32Windows (Win32 EnumWindows + IsWindowVisible +
    # GetWindowThreadProcessId + GetWindowText/GetClassName/GetWindowRect – tylko odczyt) i Select-E5MainWindow (ta sama funkcja co w skrypcie).
    [CmdletBinding()]
    [OutputType([string])]
    param()
    $Win32 = @'
function Get-ProbeWin32Windows {
    # Widoczne okna najwyzszego poziomu (Win32 EnumWindows), opcjonalnie tylko procesow z listy PID. Tylko odczyt.
    param([int[]]$ProcessIds)
    if (-not ('DsoE5.Win32Windows' -as [type])) {
        Add-Type -TypeDefinition @"
using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Text;
namespace DsoE5 {
    public static class Win32Windows {
        private delegate bool EnumProc(IntPtr hWnd, IntPtr lParam);
        [DllImport("user32.dll")] private static extern bool EnumWindows(EnumProc callback, IntPtr lParam);
        [DllImport("user32.dll")] private static extern bool IsWindowVisible(IntPtr hWnd);
        [DllImport("user32.dll")] private static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint processId);
        [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern int GetWindowText(IntPtr hWnd, StringBuilder text, int maxCount);
        [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern int GetClassName(IntPtr hWnd, StringBuilder text, int maxCount);
        [DllImport("user32.dll")] private static extern bool GetWindowRect(IntPtr hWnd, out Rect rect);
        [StructLayout(LayoutKind.Sequential)] public struct Rect { public int Left; public int Top; public int Right; public int Bottom; }
        public sealed class Info { public long Handle; public int ProcessId; public string Title; public string ClassName; public int X; public int Y; public int Width; public int Height; }
        public static List<Info> Visible() {
            List<Info> list = new List<Info>();
            EnumProc callback = delegate (IntPtr hWnd, IntPtr lParam) {
                if (!IsWindowVisible(hWnd)) { return true; }
                uint processId;
                GetWindowThreadProcessId(hWnd, out processId);
                StringBuilder title = new StringBuilder(512);
                GetWindowText(hWnd, title, title.Capacity);
                StringBuilder className = new StringBuilder(256);
                GetClassName(hWnd, className, className.Capacity);
                Rect rect;
                GetWindowRect(hWnd, out rect);
                Info info = new Info();
                info.Handle = hWnd.ToInt64(); info.ProcessId = (int)processId; info.Title = title.ToString(); info.ClassName = className.ToString();
                info.X = rect.Left; info.Y = rect.Top; info.Width = rect.Right - rect.Left; info.Height = rect.Bottom - rect.Top;
                list.Add(info);
                return true;
            };
            EnumWindows(callback, IntPtr.Zero);
            GC.KeepAlive(callback);
            return list;
        }
    }
}
"@
    }
    $Names = @{}
    foreach ($Window in [DsoE5.Win32Windows]::Visible()) {
        if ($ProcessIds -and $ProcessIds -notcontains $Window.ProcessId) { continue }
        if (-not $Names.ContainsKey($Window.ProcessId)) {
            $ProcessName = $null
            try { $ProcessName = (Get-Process -Id $Window.ProcessId -ErrorAction Stop).ProcessName + '.exe' } catch { $ProcessName = $null }
            $Names[$Window.ProcessId] = $ProcessName
        }
        [pscustomobject]@{ Source = 'win32'; ProcessId = $Window.ProcessId; ProcessName = $Names[$Window.ProcessId]; Name = $Window.Title; ClassName = $Window.ClassName
            Bounds = [ordered]@{ X = $Window.X; Y = $Window.Y; Width = $Window.Width; Height = $Window.Height } }
    }
}
'@
    $Select = "function Select-E5MainWindow {`n$((Get-Command -Name 'Select-E5MainWindow' -CommandType Function).Definition)`n}"
    return ($Win32 + "`n" + $Select + "`n")
}

function Invoke-E5UserProbe {
    # Skrypt sondy (UTF-8 z BOM) uruchamiany w sesji dso z ukrytego zadania Harmonogramu (Interactive + Highest); czeka na plik wyniku
    # najwyżej TimeoutSeconds, zwraca sparsowany JSON; zadanie zawsze wyrejestrowane.
    [CmdletBinding()]
    param(
        [Parameter(Mandatory)]$Plan,
        [Parameter(Mandatory)][ValidatePattern('^[A-Za-z0-9-]+\z')][string]$Name,
        [Parameter(Mandatory)][string]$ScriptText,
        [ValidateRange(1, 900)][int]$TimeoutSeconds = 90,
        # Żądanie JSON dla sondy (np. ścieżka exe ze spacją i polskim znakiem – nie w linii argumentów zadania): zapis do <Name>.req.json
        # i argument -Request <plik>; bez niego sonda dostaje tylko -ResultFile.
        [string]$RequestJson
    )
    Assert-E5InsideVm
    $Work = [string]$Plan.WorkDirectory
    $null = [System.IO.Directory]::CreateDirectory($Work)
    $ScriptPath = Join-Path $Work "$Name.ps1"
    $ResultPath = Join-Path $Work "$Name.json"
    Assert-E5PlainPath -Path $ScriptPath
    Assert-E5PlainPath -Path $ResultPath
    [System.IO.File]::WriteAllText($ScriptPath, $ScriptText, (New-Object System.Text.UTF8Encoding($true)))
    if ([System.IO.File]::Exists($ResultPath)) { [System.IO.File]::Delete($ResultPath) }
    $RequestArgument = ''
    if ($PSBoundParameters.ContainsKey('RequestJson')) {
        $RequestPath = Join-Path $Work "$Name.req.json"
        Assert-E5PlainPath -Path $RequestPath
        [System.IO.File]::WriteAllText($RequestPath, $RequestJson, (New-Object System.Text.UTF8Encoding($false)))
        $RequestArgument = " -Request $RequestPath"
    }
    $TaskName = "DSO-E5-$Name"
    $Registered = $false
    try {
        $Executable = Join-Path $env:SystemRoot 'System32\WindowsPowerShell\v1.0\powershell.exe'
        $Action = New-ScheduledTaskAction -Execute $Executable -Argument "-NoProfile -NonInteractive -ExecutionPolicy Bypass -WindowStyle Hidden -File $ScriptPath -ResultFile $ResultPath$RequestArgument"
        $Principal = New-ScheduledTaskPrincipal -UserId ([string]$Plan.UserName) -LogonType Interactive -RunLevel Highest
        $Settings = New-ScheduledTaskSettingsSet -AllowStartIfOnBatteries -DontStopIfGoingOnBatteries -ExecutionTimeLimit (New-TimeSpan -Seconds ($TimeoutSeconds + 60))
        $Registered = $true
        $null = Register-ScheduledTask -TaskName $TaskName -Action $Action -Principal $Principal -Settings $Settings -Force -ErrorAction Stop
        Start-ScheduledTask -TaskName $TaskName -ErrorAction Stop
        $Watch = [System.Diagnostics.Stopwatch]::StartNew()
        while (-not [System.IO.File]::Exists($ResultPath)) {
            if ($Watch.Elapsed.TotalSeconds -ge $TimeoutSeconds) { throw "Sonda $Name w sesji dso nie zapisała wyniku w $TimeoutSeconds s (zalogowany dso?)." }
            Start-Sleep -Seconds 1
        }
        return ([System.IO.File]::ReadAllText($ResultPath, [System.Text.Encoding]::UTF8).TrimStart([char]0xFEFF) | ConvertFrom-Json)
    }
    finally {
        if ($Registered) { Unregister-ScheduledTask -TaskName $TaskName -Confirm:$false -ErrorAction SilentlyContinue }
    }
}

function Start-E5Variant {
    # Jeden wariant (VM): znacznik czasu startu (przed pobraniem) -> sieć -> [B/C: pobranie i SHA256] -> start jako dso -> PID korzenia ->
    # 60 s od startu -> drzewo procesów (żyje?) -> stan zadania -> okna pulpitu dso (sonda) -> okno WinUtil w drzewie. Zrzut robi host.
    # Blocked = wynik pomiaru bez startu okna (Defender zablokował pobrany plik albo SHA256 ≠ digest); Error = awaria (sieć, API, start);
    # ProbeError = awaria sondy okien. Ważność wariantu ocenia host (Get-E5VariantSummary).
    [CmdletBinding()]
    param(
        [Parameter(Mandatory)]$Plan,
        [Parameter(Mandatory)][ValidateSet('A', 'B', 'C')][string]$Variant,
        [Parameter(Mandatory)][string]$UserSid
    )
    Assert-E5InsideVm
    $Since = Get-Date
    $Out = [ordered]@{
        Variant = $Variant; Since = $Since.ToString('o'); Network = $null; Download = $null; Blocked = $null; Command = $null; TaskName = "DSO-E5-WinUtil-$Variant"
        Started = $false; RootPid = $null; WaitSeconds = [int]$Plan.VariantWaitSeconds; AliveAfterWait = $false; TaskState = $null; TaskLastResult = $null
        Tree = @(); Windows = @(); TreeWindows = @(); MatchingWindows = @(); ProbeSid = $null; ProbeAsDso = $null; ProbeError = $null
        WinUtilVisible = $false; WinUtilWindowTitle = $null; Error = $null
    }
    try {
        $Out.Network = Test-E5Network -Name ([string]$Plan.WinUtilNetworkName) -WaitSeconds ([int]$Plan.NetworkWaitSeconds)
        if (-not $Out.Network.Resolved) { throw "Brak sieci w VM: '$($Plan.WinUtilNetworkName)' nie rozwiązuje się po $($Out.Network.Seconds) s; nic nie uruchomiono." }
        $Marker = $null
        if ($Variant -eq 'A') {
            $Out.Command = Get-E5VariantArguments -Plan $Plan -Variant 'A'
            $Marker = [string]$Plan.WinUtilProcessMarker
        }
        else {
            $Out.Download = Save-E5WinUtilDownload -Plan $Plan
            if ($Out.Download.BlockedByDefender) {
                $Out.Blocked = "Defender zablokował plik $($Plan.WinUtilAssetName) (etap $($Out.Download.Stage)): $($Out.Download.Error); okno nie zostało uruchomione."
            }
            elseif ($Out.Download.Error) { throw "Pobranie $($Plan.WinUtilAssetName) nie powiodło się (etap $($Out.Download.Stage)): $($Out.Download.Error); okno nie zostało uruchomione." }
            elseif (-not $Out.Download.HashMatches) {
                $Out.Blocked = "SHA256 pliku ($($Out.Download.Sha256)) różni się od digest z API ($($Out.Download.ExpectedSha256)); okno nie zostało uruchomione."
            }
            else {
                $Out.Command = Get-E5VariantArguments -Plan $Plan -Variant $Variant -Path ([string]$Out.Download.Path)
                $Marker = [string]$Out.Download.Path
            }
        }
        if ($null -eq $Out.Blocked) {
            $Launched = Get-Date
            Start-E5UserProcess -Plan $Plan -TaskName $Out.TaskName -Arguments $Out.Command
            $Out.Started = $true
            $Out.RootPid = Find-E5RootProcess -Marker $Marker -Since $Launched -TimeoutSeconds ([int]$Plan.ProcessFindSeconds)
            $Remaining = [int]$Plan.VariantWaitSeconds - [int]((Get-Date) - $Launched).TotalSeconds
            if ($Remaining -gt 0) { Start-Sleep -Seconds $Remaining }
            if ($null -ne $Out.RootPid) {
                $Out.Tree = @(Get-E5ProcessTree -RootPid ([int]$Out.RootPid))
                $Out.AliveAfterWait = (@($Out.Tree | Where-Object { [int]$_.ProcessId -eq [int]$Out.RootPid }).Count -gt 0)
            }
            try {
                $Out.TaskState = [string](Get-ScheduledTask -TaskName $Out.TaskName -ErrorAction Stop).State
                $Out.TaskLastResult = Format-E5Hex -Code ([int64](Get-ScheduledTaskInfo -TaskName $Out.TaskName -ErrorAction Stop).LastTaskResult)
            }
            catch { $Out.TaskState = "odczyt: $(Format-E5Error $_)" }
            $Probe = $null
            $ProbeFailure = $null
            try { $Probe = Invoke-E5UserProbe -Plan $Plan -Name "windows-$Variant" -ScriptText (Get-E5WindowProbeScript) -TimeoutSeconds ([int]$Plan.ProbeTimeoutSeconds) }
            catch { $ProbeFailure = Format-E5Error $_ }
            $Summary = Get-E5ProbeWindowSummary -Probe $Probe -ProbeFailure $ProbeFailure -UserSid $UserSid -TreeIds @($Out.Tree | ForEach-Object { [int]$_.ProcessId }) -Pattern ([string]$Plan.WinUtilTitlePattern)
            foreach ($Field in 'ProbeSid', 'ProbeAsDso', 'ProbeError', 'Windows', 'TreeWindows', 'MatchingWindows') { $Out[$Field] = $Summary[$Field] }
            $Out.WinUtilVisible = $Summary.Visible
            $Out.WinUtilWindowTitle = $Summary.Title
        }
    }
    catch { $Out.Error = Format-E5Error $_ }
    return [pscustomobject]$Out
}

function Stop-E5Variant {
    # Koniec wariantu (VM): detekcje od startu wariantu (PRZED zamknięciem), zamknięcie drzewa procesu (dzieci przed rodzicem),
    # sprawdzenie, czy coś przeżyło, wyrejestrowanie zadania wariantu (tylko DSO-E5-*). Błędy zbierane, nie przerywają.
    [CmdletBinding()]
    param(
        [Parameter(Mandatory)]$Plan,
        [Parameter(Mandatory)]$State
    )
    Assert-E5InsideVm
    $Out = [ordered]@{ Variant = [string](Get-E5Property -Object $State -Name 'Variant'); SinceFallback = $false; Detections = $null; Closed = @(); StillRunning = @(); TaskUnregistered = $false; Error = $null }
    $Errors = New-Object System.Collections.Generic.List[string]
    try {
        $SinceText = [string](Get-E5Property -Object $State -Name 'Since')
        if ($SinceText) { $Since = [datetime]::Parse($SinceText, [System.Globalization.CultureInfo]::InvariantCulture, [System.Globalization.DateTimeStyles]::RoundtripKind) }
        else {
            # Start padł przed znacznikiem czasu: detekcje z ostatnich 15 minut, oznaczone.
            $Since = (Get-Date).AddMinutes(-15)
            $Out.SinceFallback = $true
        }
        $Out.Detections = Get-E5Detections -Since $Since -LogName ([string]$Plan.DefenderEventLog) -EventIds @($Plan.DefenderEventIds | ForEach-Object { [int]$_ })
    }
    catch { $Errors.Add("detekcje: $(Format-E5Error $_)") }
    $RootPid = Get-E5Property -Object $State -Name 'RootPid'
    if ($null -ne $RootPid -and [string]$RootPid -ne '') {
        try {
            $Tree = @(Get-E5ProcessTree -RootPid ([int]$RootPid))
            $Closed = @()
            for ($Index = $Tree.Count - 1; $Index -ge 0; $Index--) {
                $Id = [int]$Tree[$Index].ProcessId
                try {
                    Stop-Process -Id $Id -Force -ErrorAction Stop
                    $Closed += $Id
                }
                catch { $Errors.Add("zamknięcie PID ${Id}: $(Format-E5Error $_)") }
            }
            $Out.Closed = $Closed
            Start-Sleep -Seconds ([int]$Plan.CloseWaitSeconds)
            $Out.StillRunning = @($Tree | Where-Object { $null -ne (Get-Process -Id ([int]$_.ProcessId) -ErrorAction SilentlyContinue) } | ForEach-Object { [int]$_.ProcessId })
        }
        catch { $Errors.Add("drzewo procesu: $(Format-E5Error $_)") }
    }
    $TaskName = [string](Get-E5Property -Object $State -Name 'TaskName')
    if ($TaskName) {
        if ($TaskName -cnotmatch '^DSO-E5-[A-Za-z0-9-]+\z') { $Errors.Add("nazwa zadania '$TaskName' spoza pomiaru E5 – nie wyrejestrowano") }
        else {
            try {
                Unregister-ScheduledTask -TaskName $TaskName -Confirm:$false -ErrorAction Stop
                $Out.TaskUnregistered = $true
            }
            catch { $Errors.Add("wyrejestrowanie ${TaskName}: $(Format-E5Error $_)") }
        }
    }
    if ($Errors.Count -gt 0) { $Out.Error = $Errors -join ' | ' }
    return [pscustomobject]$Out
}

function Get-E5ProbeWindowSummary {
    # Wynik sondy okien (Get-E5WindowProbeScript) względem drzewa procesu: okna w drzewie, okna o tytule pasującym do wzorca, okno
    # z drzewa pasujące do wzorca (Visible/Title). ProbeFailure = błąd wywołania sondy (wtedy Probe = $null). Czysta logika.
    [CmdletBinding()]
    param(
        [AllowNull()]$Probe,
        [AllowNull()][string]$ProbeFailure,
        [Parameter(Mandatory)][AllowEmptyString()][string]$UserSid,
        [AllowEmptyCollection()][int[]]$TreeIds = @(),
        [Parameter(Mandatory)][string]$Pattern
    )
    $Out = [ordered]@{ ProbeSid = $null; ProbeAsDso = $null; ProbeError = $ProbeFailure; Windows = @(); TreeWindows = @(); MatchingWindows = @(); Visible = $false; Title = $null }
    if ($null -eq $Probe) { return $Out }
    $Out.ProbeSid = [string](Get-E5Property -Object $Probe -Name 'Sid')
    $Out.ProbeAsDso = ($Out.ProbeSid -eq $UserSid)
    $Out.ProbeError = Get-E5Property -Object $Probe -Name 'Error'
    $Out.Windows = @(@(Get-E5Property -Object $Probe -Name 'Windows') | Where-Object { $null -ne $_ } | ForEach-Object {
            [pscustomobject]@{ ProcessId = [int]$_.ProcessId; Name = [string]$_.Name; ClassName = [string]$_.ClassName; InTree = ($TreeIds -contains [int]$_.ProcessId) }
        })
    $Out.TreeWindows = @($Out.Windows | Where-Object { $_.InTree })
    $Out.MatchingWindows = @($Out.Windows | Where-Object { $_.Name -match $Pattern })
    $Match = @($Out.TreeWindows | Where-Object { $_.Name -match $Pattern })
    if ($Match.Count -gt 0) {
        $Out.Visible = $true
        $Out.Title = [string]$Match[0].Name
    }
    return $Out
}

# --- Task 6: wydanie z VHDX (ścieżka ze spacją i polskim znakiem), CLI jako dso, GUI, SmartScreen, start, 150% ---

function Get-E5TaskExecute {
    # Pole Execute akcji zadania Harmonogramu dla pliku z nośnika: pełna ścieżka W CUDZYSŁOWIE (spacja i polski znak w 'Narzędzia DSO').
    # Ścieżka musi być bezwzględna (X:\...) i bez cudzysłowu.
    [CmdletBinding()]
    [OutputType([string])]
    param([Parameter(Mandatory)][AllowEmptyString()][string]$Path)
    if ($Path -notmatch '^[A-Za-z]:\\[^"]+\z' -or $Path -match '[\r\n\t]') { throw "Ścieżka programu '$Path' musi być bezwzględna (X:\...) i bez cudzysłowu." }
    return ('"{0}"' -f $Path)
}

function ConvertTo-E5ExitCode {
    # LastTaskResult (uint32 z Harmonogramu) -> kod wyjścia jako int ze znakiem; brak = $null.
    [CmdletBinding()]
    param([AllowNull()]$Code)
    if ($null -eq $Code -or [string]$Code -eq '') { return $null }
    $Long = [int64]$Code
    if ($Long -gt [int]::MaxValue) { $Long -= 4294967296 }
    return [int]$Long
}

function Read-E5JsonFile {
    # Plik JSON (UTF-8, z BOM albo bez) -> obiekt; brak pliku = $null. Tylko odczyt.
    [CmdletBinding()]
    param([Parameter(Mandatory)][string]$Path)
    if (-not [System.IO.File]::Exists($Path)) { return $null }
    return ([System.IO.File]::ReadAllText($Path, [System.Text.Encoding]::UTF8).TrimStart([char]0xFEFF) | ConvertFrom-Json)
}

function Test-E5ZoneIdentifier {
    # Mark of the Web pliku (strumień Zone.Identifier): Exists, ZoneId (liczba z linii ZoneId=) i tekst. Tylko odczyt.
    # Exists = false tylko wtedy, gdy lista strumieni pliku została odczytana i nie ma w niej Zone.Identifier; brak pliku albo błąd
    # odczytu (np. odmowa dostępu) = WYJĄTEK, nigdy „brak MotW”.
    [CmdletBinding()]
    param([Parameter(Mandatory)][string]$Path)
    $Out = [ordered]@{ Path = $Path; FileExists = [System.IO.File]::Exists($Path); Exists = $false; ZoneId = $null; Text = $null }
    if (-not $Out.FileExists) { throw "Brak pliku '$Path' – nie da się sprawdzić Zone.Identifier." }
    $Streams = @(Get-Item -LiteralPath $Path -Stream '*' -ErrorAction Stop | ForEach-Object { [string]$_.Stream })
    if ($Streams -notcontains 'Zone.Identifier') { return [pscustomobject]$Out }
    $Out.Exists = $true
    $Out.Text = (@(Get-Content -LiteralPath $Path -Stream 'Zone.Identifier' -ErrorAction Stop) -join "`n")
    if ($Out.Text -match '(?m)^\s*ZoneId\s*=\s*(\d+)') { $Out.ZoneId = [int]$Matches[1] }
    return [pscustomobject]$Out
}

function Get-E5UserProfile {
    # Katalog profilu konta po SID (ProfileList, tylko odczyt).
    [CmdletBinding()]
    [OutputType([string])]
    param([Parameter(Mandatory)][ValidatePattern('^S-1-5-21-[0-9-]+\z')][string]$UserSid)
    $Entry = Read-ExploreRegistryValue -Path "HKLM\SOFTWARE\Microsoft\Windows NT\CurrentVersion\ProfileList\$UserSid" -Name 'ProfileImagePath'
    if (-not $Entry.Exists -or [string]::IsNullOrWhiteSpace([string]$Entry.Value)) { throw "Brak ProfileImagePath konta $UserSid." }
    return [Environment]::ExpandEnvironmentVariables([string]$Entry.Value)
}

function Get-E5ExeProcesses {
    # Procesy uruchomione z dokładnie tego pliku (ExecutablePath bez rozróżniania wielkości liter). Tylko odczyt.
    [CmdletBinding()]
    param([Parameter(Mandatory)][string]$Path)
    return @(Get-CimInstance -ClassName Win32_Process -ErrorAction Stop | Where-Object { $null -ne $_ -and [string]$_.ExecutablePath -ieq $Path } | ForEach-Object {
            [pscustomobject]@{ ProcessId = [int]$_.ProcessId; Name = [string]$_.Name; ExecutablePath = [string]$_.ExecutablePath; SessionId = $_.SessionId }
        })
}

function Get-E5NewProcesses {
    # Procesy o podanych nazwach utworzone od znacznika czasu (np. smartscreen.exe, consent.exe, mmc.exe). Tylko odczyt.
    [CmdletBinding()]
    param(
        [Parameter(Mandatory)][datetime]$Since,
        [Parameter(Mandatory)][string[]]$Names
    )
    $Floor = $Since.AddSeconds(-2)
    return @(Get-CimInstance -ClassName Win32_Process -ErrorAction Stop | Where-Object {
            $null -ne $_ -and $Names -contains [string]$_.Name -and ($null -eq $_.CreationDate -or [datetime]$_.CreationDate -ge $Floor)
        } | ForEach-Object {
            $Line = [string]$_.CommandLine
            if ($Line.Length -gt 1000) { $Line = $Line.Substring(0, 1000) }
            [pscustomobject]@{ ProcessId = [int]$_.ProcessId; Name = [string]$_.Name; ExecutablePath = [string]$_.ExecutablePath; CommandLine = $Line; SessionId = $_.SessionId }
        })
}

function Get-E5NamedProcesses {
    # WSZYSTKIE procesy o podanych nazwach (bez filtra czasu – np. smartscreen.exe żyje długo i istnieje przed startem fazy), z czasem
    # utworzenia i sesją. Tylko odczyt.
    [CmdletBinding()]
    param([Parameter(Mandatory)][string[]]$Names)
    return @(Get-CimInstance -ClassName Win32_Process -ErrorAction Stop | Where-Object { $null -ne $_ -and $Names -contains [string]$_.Name } | ForEach-Object {
            [pscustomobject]@{ ProcessId = [int]$_.ProcessId; Name = [string]$_.Name; ExecutablePath = [string]$_.ExecutablePath; SessionId = $_.SessionId
                CreationDate = $(if ($null -ne $_.CreationDate) { ([datetime]$_.CreationDate).ToString('o') } else { $null }) }
        })
}

function Stop-E5Processes {
    # Zamknięcie procesów pomiaru po PID (dzieci podaje wołający), sprawdzenie, czy coś przeżyło. Błąd jednego nie przerywa reszty.
    [CmdletBinding()]
    param(
        [Parameter(Mandatory)]$Plan,
        [AllowEmptyCollection()][int[]]$Ids = @()
    )
    Assert-E5InsideVm
    $Out = [ordered]@{ Closed = @(); StillRunning = @(); Errors = @() }
    foreach ($Id in @($Ids | Select-Object -Unique)) {
        try {
            Stop-Process -Id $Id -Force -ErrorAction Stop
            $Out.Closed += $Id
        }
        catch { $Out.Errors += "zamknięcie PID ${Id}: $(Format-E5Error $_)" }
    }
    if (@($Ids).Count -gt 0) {
        Start-Sleep -Seconds ([int]$Plan.CloseWaitSeconds)
        $Out.StillRunning = @($Ids | Where-Object { $null -ne (Get-Process -Id $_ -ErrorAction SilentlyContinue) })
    }
    return [pscustomobject]$Out
}

function Unregister-E5Task {
    # Wyrejestrowanie zadania pomiaru (tylko DSO-E5-*); błąd = tekst, nie wyjątek.
    [CmdletBinding()]
    param([Parameter(Mandatory)][string]$TaskName)
    Assert-E5InsideVm
    if ($TaskName -cnotmatch '^DSO-E5-[A-Za-z0-9-]+\z') { return "nazwa zadania '$TaskName' spoza pomiaru E5 – nie wyrejestrowano" }
    try { Unregister-ScheduledTask -TaskName $TaskName -Confirm:$false -ErrorAction Stop; return $null }
    catch { return "wyrejestrowanie ${TaskName}: $(Format-E5Error $_)" }
}

function Register-E5Task {
    # Zadanie Harmonogramu jako dso (Interactive; Highest = jak DSOptimizer po UAC, Limited = jak powłoka użytkownika), bez limitu czasu.
    # Execute podaje wołający (plik z nośnika: Get-E5TaskExecute – ścieżka w cudzysłowie); Argument trafia do akcji dosłownie.
    [CmdletBinding()]
    param(
        [Parameter(Mandatory)]$Plan,
        [Parameter(Mandatory)][ValidatePattern('^DSO-E5-[A-Za-z0-9-]+\z')][string]$TaskName,
        [Parameter(Mandatory)][string]$Execute,
        [AllowEmptyString()][string]$Argument = '',
        [ValidateSet('Highest', 'Limited')][string]$RunLevel = 'Highest'
    )
    Assert-E5InsideVm
    $Action = if ($Argument) { New-ScheduledTaskAction -Execute $Execute -Argument $Argument } else { New-ScheduledTaskAction -Execute $Execute }
    $Principal = New-ScheduledTaskPrincipal -UserId ([string]$Plan.UserName) -LogonType Interactive -RunLevel $RunLevel
    $Settings = New-ScheduledTaskSettingsSet -AllowStartIfOnBatteries -DontStopIfGoingOnBatteries -ExecutionTimeLimit ([TimeSpan]::Zero)
    $null = Register-ScheduledTask -TaskName $TaskName -Action $Action -Principal $Principal -Settings $Settings -Force -ErrorAction Stop
    Start-ScheduledTask -TaskName $TaskName -ErrorAction Stop
}

function Wait-E5Task {
    # Czeka, aż zadanie skończy (stan inny niż Running/Queued i LastTaskResult różny od 0x41301 „działa” i 0x41303 „jeszcze nie działało”);
    # kod wyjścia programu = LastTaskResult. Po limicie TimedOut = true (zadanie i program NIE są przerywane).
    [CmdletBinding()]
    param(
        [Parameter(Mandatory)][string]$TaskName,
        [Parameter(Mandatory)][ValidateRange(1, 7200)][int]$TimeoutSeconds,
        [ValidateRange(1, 60)][int]$PollSeconds = 2
    )
    $Watch = [System.Diagnostics.Stopwatch]::StartNew()
    $Out = [ordered]@{ Finished = $false; TimedOut = $false; ExitCode = $null; Hex = $null; State = $null; Seconds = $null }
    while ($true) {
        Start-Sleep -Seconds $PollSeconds
        $Out.State = [string](Get-ScheduledTask -TaskName $TaskName -ErrorAction Stop).State
        $Code = [int64](Get-ScheduledTaskInfo -TaskName $TaskName -ErrorAction Stop).LastTaskResult
        if (@('Running', 'Queued') -notcontains $Out.State -and @(267009, 267011) -notcontains $Code) {
            $Out.Finished = $true
            $Out.ExitCode = ConvertTo-E5ExitCode -Code $Code
            $Out.Hex = Format-E5Hex -Code $Code
            break
        }
        if ($Watch.Elapsed.TotalSeconds -ge $TimeoutSeconds) { $Out.TimedOut = $true; break }
    }
    $Out.Seconds = [math]::Round($Watch.Elapsed.TotalSeconds, 1)
    return [pscustomobject]$Out
}

function Invoke-E5Cli {
    # Jedno polecenie CLI DSOptimizera z nośnika jako dso: akcja zadania = "<ścieżka exe>" (w cudzysłowie) + argumenty dosłownie, czekanie
    # na koniec (Wait-E5Task), kod z LastTaskResult, raport --out. Zadanie, które nie skończyło się w limicie, zostaje (revert je usunie).
    [CmdletBinding()]
    param(
        [Parameter(Mandatory)]$Plan,
        [Parameter(Mandatory)][string]$Exe,
        [Parameter(Mandatory)][ValidatePattern('^[A-Za-z0-9-]+\z')][string]$Name,
        [Parameter(Mandatory)][AllowEmptyString()][string]$Arguments,
        [string]$OutFile,
        [ValidateRange(1, 7200)][int]$TimeoutSeconds = 600
    )
    Assert-E5InsideVm
    $TaskName = "DSO-E5-cli-$Name"
    $Out = [ordered]@{ Name = $Name; TaskName = $TaskName; Execute = $null; Arguments = $Arguments; OutFile = $OutFile; StartedAt = (Get-Date).ToString('o'); Finished = $false; TimedOut = $false
        ExitCode = $null; Hex = $null; Seconds = $null; Report = $null; ReportError = $null; Error = $null }
    try {
        $Out.Execute = Get-E5TaskExecute -Path $Exe
        if ($OutFile -and [System.IO.File]::Exists($OutFile)) { [System.IO.File]::Delete($OutFile) }
        Register-E5Task -Plan $Plan -TaskName $TaskName -Execute $Out.Execute -Argument $Arguments -RunLevel 'Highest'
        $Wait = Wait-E5Task -TaskName $TaskName -TimeoutSeconds $TimeoutSeconds -PollSeconds ([int]$Plan.TaskPollSeconds)
        foreach ($Field in 'Finished', 'TimedOut', 'ExitCode', 'Hex', 'Seconds') { $Out[$Field] = $Wait.$Field }
        if ($Out.Finished) { $Message = Unregister-E5Task -TaskName $TaskName; if ($Message) { $Out.Error = $Message } }
    }
    catch { $Out.Error = Format-E5Error $_ }
    if ($OutFile) {
        try {
            $Out.Report = Read-E5JsonFile -Path $OutFile
            if ($null -eq $Out.Report -and $Out.Finished) { $Out.ReportError = "brak raportu --out ($OutFile)" }
        }
        catch { $Out.ReportError = "odczyt raportu: $(Format-E5Error $_)" }
    }
    return [pscustomobject]$Out
}

function Resolve-E5UsbVolume {
    # Wolumin VHDX z wydaniem w VM po etykiecie (Plan.UsbVolumeLabel): dysk spoza systemowego offline/tylko do odczytu -> online i zapis,
    # wolumin bez litery -> litera; czeka najwyżej UsbWaitSeconds. Zwraca literę i ścieżki exe (Narzędzia DSO\...) oraz pliku wydania.
    [CmdletBinding()]
    param([Parameter(Mandatory)]$Plan)
    Assert-E5InsideVm
    $Label = [string]$Plan.UsbVolumeLabel
    $Artifact = Get-E5Property -Object $Plan -Name 'Artifact'
    if ($null -eq $Artifact) { throw 'Plan bez artefaktu (-Variant/-ArtifactPath): nie ma czego szukać na VHDX.' }
    $Out = [ordered]@{ Label = $Label; DriveLetter = $null; Root = $null; ExePath = $null; ExeExists = $false; ReleasePath = $null; ReleaseExists = $false; Onlined = @(); Seconds = $null }
    $Watch = [System.Diagnostics.Stopwatch]::StartNew()
    while ($true) {
        foreach ($Disk in @(Get-Disk -ErrorAction Stop | Where-Object { $null -ne $_ -and -not $_.IsBoot -and -not $_.IsSystem })) {
            if ($Disk.IsOffline) { Set-Disk -Number ([int]$Disk.Number) -IsOffline $false -ErrorAction Stop; $Out.Onlined += [int]$Disk.Number }
            if ($Disk.IsReadOnly) { Set-Disk -Number ([int]$Disk.Number) -IsReadOnly $false -ErrorAction Stop }
        }
        $Volumes = @(Get-Volume -ErrorAction Stop | Where-Object { $null -ne $_ -and [string]$_.FileSystemLabel -ceq $Label })
        if ($Volumes.Count -gt 1) { throw "W VM jest $($Volumes.Count) woluminów z etykietą '$Label'." }
        if ($Volumes.Count -eq 1) {
            $Letter = [string]$Volumes[0].DriveLetter
            if ($Letter -match '^[A-Za-z]\z') { $Out.DriveLetter = $Letter.ToUpperInvariant(); break }
            $Partition = @(Get-Partition -Volume $Volumes[0] -ErrorAction Stop)[0]
            Add-PartitionAccessPath -InputObject $Partition -AssignDriveLetter -ErrorAction Stop
            continue
        }
        if ($Watch.Elapsed.TotalSeconds -ge [int]$Plan.UsbWaitSeconds) { throw "Wolumin '$Label' (VHDX z wydaniem) nie pojawił się w VM w $($Plan.UsbWaitSeconds) s." }
        Start-Sleep -Seconds 3
    }
    $Out.Seconds = [math]::Round($Watch.Elapsed.TotalSeconds, 1)
    $Out.Root = "$($Out.DriveLetter):\"
    $Out.ExePath = Join-Path (Join-Path $Out.Root ([string]$Plan.UsbFolderName)) ([string]$Artifact.ExeRelativePath)
    $Out.ReleasePath = Join-Path (Join-Path $Out.Root ([string]$Plan.UsbReleaseFolder)) ([string]$Artifact.Name)
    $Out.ExeExists = [System.IO.File]::Exists($Out.ExePath)
    $Out.ReleaseExists = [System.IO.File]::Exists($Out.ReleasePath)
    if (-not $Out.ExeExists) { throw "Na woluminie '$Label' nie ma programu '$($Out.ExePath)'." }
    return [pscustomobject]$Out
}

function Wait-E5UserShell {
    # Czeka na explorer.exe konta (po tools.restart-explorer powłoka wraca sama). Tylko odczyt; zwraca {Ready, Seconds}.
    [CmdletBinding()]
    param(
        [Parameter(Mandatory)][string]$UserSid,
        [ValidateRange(0, 600)][int]$TimeoutSeconds = 90
    )
    $Watch = [System.Diagnostics.Stopwatch]::StartNew()
    while ($true) {
        $Owners = @(Get-CimInstance -ClassName Win32_Process -Filter "Name='explorer.exe'" -ErrorAction SilentlyContinue | Where-Object { $null -ne $_ } |
                ForEach-Object { [string](Invoke-CimMethod -InputObject $_ -MethodName GetOwnerSid -ErrorAction SilentlyContinue).Sid })
        if ($Owners -contains $UserSid) { return [pscustomobject]@{ Ready = $true; Seconds = [math]::Round($Watch.Elapsed.TotalSeconds, 1) } }
        if ($Watch.Elapsed.TotalSeconds -ge $TimeoutSeconds) { return [pscustomobject]@{ Ready = $false; Seconds = [math]::Round($Watch.Elapsed.TotalSeconds, 1) } }
        Start-Sleep -Seconds 2
    }
}

function Install-E5Fixture {
    # Program testowy Win32 DsoTestQuiet (tests\vm\fixtures, skopiowane przez hosta do C:\dso\fixtures): powershell.exe -ExecutionPolicy Bypass
    # -File Install-DsoTestProgram.ps1 -Mode quiet -KeyName DsoTestQuiet (jak Setup przypadku app-win32-quiet), potem obecność wpisu Uninstall.
    [CmdletBinding()]
    param([Parameter(Mandatory)]$Plan)
    Assert-E5InsideVm
    $Script = Join-Path ([string]$Plan.FixturesDirectory) 'Install-DsoTestProgram.ps1'
    if (-not [System.IO.File]::Exists($Script)) { throw "Brak '$Script' (fixtures nie zostały skopiowane do VM)." }
    $PowerShell = Join-Path $env:SystemRoot 'System32\WindowsPowerShell\v1.0\powershell.exe'
    $Run = Invoke-ExploreNative -FilePath $PowerShell -TimeoutSeconds 300 -Arguments @('-NoProfile', '-NonInteractive', '-ExecutionPolicy', 'Bypass', '-File', $Script,
        '-Mode', 'quiet', '-KeyName', [string]$Plan.FixtureKeyName, '-FixturesDirectory', [string]$Plan.FixturesDirectory)
    $Entry = Read-ExploreRegistryKey -Path "HKLM\SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall\$($Plan.FixtureKeyName)"
    return [pscustomobject]@{ ExitCode = $Run.ExitCode; TimedOut = $Run.TimedOut; Stdout = $Run.Stdout; Stderr = $Run.Stderr; Error = $Run.Error; Present = [bool]$Entry.KeyExists }
}

function Copy-E5DsoLogs {
    # Kopie dowodów do katalogu surowych plików (host je pobiera): journal, logi DSOptimizera (też winget) z C:\ProgramData\DSOptimizer
    # i kopia logu obok exe na nośniku (<katalog exe>\logs\<KOMPUTER>\). Tylko kopie; oryginały zostają.
    [CmdletBinding()]
    param(
        [Parameter(Mandatory)]$Plan,
        [Parameter(Mandatory)][string]$ExeDirectory
    )
    Assert-E5InsideVm
    $Raw = [string]$Plan.RawDirectory
    $null = [System.IO.Directory]::CreateDirectory($Raw)
    $UsbLogs = Join-Path (Join-Path $ExeDirectory 'logs') $env:COMPUTERNAME
    $Out = [ordered]@{ Copied = @(); UsbLogDirectory = $UsbLogs; UsbLogExists = [System.IO.Directory]::Exists($UsbLogs); UsbLogFiles = @() }
    if ([System.IO.File]::Exists([string]$Plan.DsoJournalPath)) {
        Copy-Item -LiteralPath ([string]$Plan.DsoJournalPath) -Destination (Join-Path $Raw 'journal.json') -Force
        $Out.Copied += 'journal.json'
    }
    $Logs = [string]$Plan.DsoLogDirectory
    foreach ($Source in @(@{ Directory = $Logs; Prefix = 'dsolog-' }, @{ Directory = (Join-Path $Logs 'winget'); Prefix = 'wingetlog-' }, @{ Directory = $UsbLogs; Prefix = 'usblog-' })) {
        if (-not [System.IO.Directory]::Exists($Source.Directory)) { continue }
        foreach ($File in @(Get-ChildItem -LiteralPath $Source.Directory -File -ErrorAction SilentlyContinue)) {
            $Name = $Source.Prefix + ($File.Name -replace '[^A-Za-z0-9._-]', '_')
            Copy-Item -LiteralPath $File.FullName -Destination (Join-Path $Raw $Name) -Force
            $Out.Copied += $Name
            if ($Source.Prefix -eq 'usblog-') { $Out.UsbLogFiles += [pscustomobject]@{ Name = $File.Name; Length = $File.Length } }
        }
    }
    return [pscustomobject]$Out
}

function Get-E5WaitWindowProbeScript {
    # Sonda w sesji dso: czeka na okno główne programu z pliku Exe – najpierw procesy o ścieżce = Exe, potem ich okna najwyższego poziomu
    # (UI Automation z warunkiem ProcessIdProperty, jak VmSmokeUi, oraz Win32 EnumWindows), wybór Select-E5MainWindow (tytuł od TitlePrefix,
    # wersja w tytule dowolna); zwraca czas, PID, faktyczny tytuł, klasę, źródło, prostokąt okna, okna procesu oraz skuteczne DPI ekranu
    # (proces sondy DPI-aware, GetDeviceCaps LOGPIXELSX) i LogPixels konta. Tylko odczyt.
    [CmdletBinding()]
    [OutputType([string])]
    param()
    $Body = @'
# Sonda okna programu (DSOptimizer, tests\vm\explore\Invoke-E5Exploration.ps1). Tylko w VM DSO-26H2-HOME/PRO, tylko odczyt.
$ErrorActionPreference = 'Stop'
$Out = [ordered]@{ Sid = $null; Elevated = $null; Found = $false; Seconds = $null; ProcessId = $null; ProcessIds = @(); Title = $null; ClassName = $null; Source = $null; Bounds = $null
    ProcessWindows = @(); Dpi = $null; ScreenWidth = $null; ScreenHeight = $null; LogPixels = $null; Error = $null }
try {
    if ($env:DSO_VM_TESTS_MOCK_ONLY) { throw 'DSO_VM_TESTS_MOCK_ONLY: sonda nie czyta okien w testach.' }
    if (@('DSO-26H2-HOME', 'DSO-26H2-PRO') -notcontains ([string]$env:COMPUTERNAME).ToUpperInvariant()) { throw "Sonda dziala tylko w VM DSO-26H2-HOME/PRO (komputer: '$env:COMPUTERNAME')." }
    $Req = [System.IO.File]::ReadAllText($Request, [System.Text.Encoding]::UTF8).TrimStart([char]0xFEFF) | ConvertFrom-Json
    $Identity = [System.Security.Principal.WindowsIdentity]::GetCurrent()
    $Out.Sid = $Identity.User.Value
    $Out.Elevated = (New-Object System.Security.Principal.WindowsPrincipal($Identity)).IsInRole([System.Security.Principal.WindowsBuiltInRole]::Administrator)
    Add-Type -Namespace DsoE5 -Name Dpi -MemberDefinition @"
[DllImport("user32.dll")] public static extern bool SetProcessDPIAware();
[DllImport("user32.dll")] public static extern System.IntPtr GetDC(System.IntPtr hWnd);
[DllImport("user32.dll")] public static extern int ReleaseDC(System.IntPtr hWnd, System.IntPtr hDC);
[DllImport("gdi32.dll")] public static extern int GetDeviceCaps(System.IntPtr hdc, int nIndex);
[DllImport("user32.dll")] public static extern int GetSystemMetrics(int nIndex);
"@
    $null = [DsoE5.Dpi]::SetProcessDPIAware()
    $Dc = [DsoE5.Dpi]::GetDC([System.IntPtr]::Zero)
    try { $Out.Dpi = [DsoE5.Dpi]::GetDeviceCaps($Dc, 88) } finally { $null = [DsoE5.Dpi]::ReleaseDC([System.IntPtr]::Zero, $Dc) }
    $Out.ScreenWidth = [DsoE5.Dpi]::GetSystemMetrics(0)
    $Out.ScreenHeight = [DsoE5.Dpi]::GetSystemMetrics(1)
    $Desktop = [Microsoft.Win32.Registry]::CurrentUser.OpenSubKey('Control Panel\Desktop')
    if ($null -ne $Desktop) { try { $Out.LogPixels = $Desktop.GetValue('LogPixels') } finally { $Desktop.Dispose() } }
    Add-Type -AssemblyName UIAutomationClient, UIAutomationTypes
    $Target = [System.IO.Path]::GetFullPath([string]$Req.Exe)
    $Root = [System.Windows.Automation.AutomationElement]::RootElement
    $Watch = [System.Diagnostics.Stopwatch]::StartNew()
    while (-not $Out.Found -and $Watch.Elapsed.TotalSeconds -lt [int]$Req.TimeoutSeconds) {
        $Ids = @(Get-Process -ErrorAction SilentlyContinue | Where-Object { $_.Path -and [string]::Equals([System.IO.Path]::GetFullPath($_.Path), $Target, [System.StringComparison]::OrdinalIgnoreCase) } | ForEach-Object { [int]$_.Id })
        $Out.ProcessIds = $Ids
        if ($Ids.Count -gt 0) {
            $Candidates = New-Object System.Collections.Generic.List[object]
            foreach ($ProcId in $Ids) {
                $ByProcess = New-Object System.Windows.Automation.PropertyCondition([System.Windows.Automation.AutomationElement]::ProcessIdProperty, [int]$ProcId)
                foreach ($Element in $Root.FindAll([System.Windows.Automation.TreeScope]::Children, $ByProcess)) {
                    try {
                        $Rect = $Element.Current.BoundingRectangle
                        $Candidates.Add([pscustomobject]@{ Source = 'uia'; ProcessId = $ProcId; Name = [string]$Element.Current.Name; ClassName = [string]$Element.Current.ClassName
                                Bounds = [ordered]@{ X = [int]$Rect.X; Y = [int]$Rect.Y; Width = [int]$Rect.Width; Height = [int]$Rect.Height } })
                    }
                    catch { $null = $_ }
                }
            }
            try { foreach ($Window in @(Get-ProbeWin32Windows -ProcessIds $Ids)) { $Candidates.Add($Window) } } catch { $null = $_ }
            $Out.ProcessWindows = @($Candidates | ForEach-Object { [pscustomobject]@{ Source = $_.Source; ProcessId = $_.ProcessId; Name = $_.Name; ClassName = $_.ClassName } })
            $Main = Select-E5MainWindow -Windows $Candidates.ToArray() -ProcessIds $Ids -TitlePrefix ([string]$Req.TitlePrefix)
            if ($null -ne $Main) {
                $Out.Found = $true
                $Out.Seconds = [math]::Round($Watch.Elapsed.TotalSeconds, 2)
                $Out.ProcessId = [int]$Main.ProcessId
                $Out.Title = [string]$Main.Name
                $Out.ClassName = [string]$Main.ClassName
                $Out.Source = [string]$Main.Source
                $Out.Bounds = $Main.Bounds
            }
        }
        if (-not $Out.Found) { Start-Sleep -Milliseconds 250 }
    }
}
catch { $Out.Error = [string]$_.Exception.GetBaseException().Message }
$Tmp = "$ResultFile.tmp"
[System.IO.File]::WriteAllText($Tmp, ($Out | ConvertTo-Json -Depth 5), (New-Object System.Text.UTF8Encoding($false)))
Move-Item -LiteralPath $Tmp -Destination $ResultFile -Force
'@
    return ("param([Parameter(Mandatory)][string]`$ResultFile, [Parameter(Mandatory)][string]`$Request)`n" + (Get-E5ProbeHelpersText) + "`n" + $Body)
}

function Get-E5StartupProbeScript {
    # Sonda w sesji dso (zadanie Highest = jak po UAC): Runs razy uruchamia Exe (CreateProcess, bez powłoki), mierzy Stopwatch od startu
    # do okna głównego TEGO PID (UI Automation z warunkiem ProcessIdProperty i Win32 EnumWindows co 100 ms, wybór Select-E5MainWindow –
    # tytuł od TitlePrefix, wersja dowolna; zapisuje faktyczny tytuł), zamyka okno (CloseMainWindow, po limicie Stop-Process).
    [CmdletBinding()]
    [OutputType([string])]
    param()
    $Body = @'
# Sonda czasu startu (DSOptimizer, tests\vm\explore\Invoke-E5Exploration.ps1). Tylko w VM DSO-26H2-HOME/PRO.
$ErrorActionPreference = 'Stop'
$Out = [ordered]@{ Sid = $null; Elevated = $null; Runs = @(); Error = $null }
try {
    if ($env:DSO_VM_TESTS_MOCK_ONLY) { throw 'DSO_VM_TESTS_MOCK_ONLY: sonda nie uruchamia niczego w testach.' }
    if (@('DSO-26H2-HOME', 'DSO-26H2-PRO') -notcontains ([string]$env:COMPUTERNAME).ToUpperInvariant()) { throw "Sonda dziala tylko w VM DSO-26H2-HOME/PRO (komputer: '$env:COMPUTERNAME')." }
    $Req = [System.IO.File]::ReadAllText($Request, [System.Text.Encoding]::UTF8).TrimStart([char]0xFEFF) | ConvertFrom-Json
    $Identity = [System.Security.Principal.WindowsIdentity]::GetCurrent()
    $Out.Sid = $Identity.User.Value
    $Out.Elevated = (New-Object System.Security.Principal.WindowsPrincipal($Identity)).IsInRole([System.Security.Principal.WindowsBuiltInRole]::Administrator)
    Add-Type -AssemblyName UIAutomationClient, UIAutomationTypes
    $Target = [System.IO.Path]::GetFullPath([string]$Req.Exe)
    $Root = [System.Windows.Automation.AutomationElement]::RootElement
    $Runs = New-Object System.Collections.Generic.List[object]
    for ($Index = 1; $Index -le [int]$Req.Runs; $Index++) {
        $Run = [ordered]@{ Index = $Index; StartedAt = (Get-Date).ToString('o'); ProcessId = $null; WindowMs = $null; Title = $null; ClassName = $null; Source = $null; ProcessWindows = @()
            TimedOut = $false; Closed = $false; Killed = $false; ExitCode = $null; Error = $null }
        try {
            $Running = @(Get-Process -ErrorAction SilentlyContinue | Where-Object { $_.Path -and [string]::Equals([System.IO.Path]::GetFullPath($_.Path), $Target, [System.StringComparison]::OrdinalIgnoreCase) })
            if ($Running.Count -gt 0) { throw "poprzednia instancja nadal dziala (PID $($Running[0].Id))" }
            $Psi = New-Object System.Diagnostics.ProcessStartInfo
            $Psi.FileName = $Target
            $Psi.WorkingDirectory = [System.IO.Path]::GetDirectoryName($Target)
            $Psi.UseShellExecute = $false
            $Watch = [System.Diagnostics.Stopwatch]::StartNew()
            $Process = [System.Diagnostics.Process]::Start($Psi)
            $Run.ProcessId = $Process.Id
            $ByProcess = New-Object System.Windows.Automation.PropertyCondition([System.Windows.Automation.AutomationElement]::ProcessIdProperty, [int]$Process.Id)
            while ($true) {
                $Candidates = New-Object System.Collections.Generic.List[object]
                foreach ($Element in $Root.FindAll([System.Windows.Automation.TreeScope]::Children, $ByProcess)) {
                    try { $Candidates.Add([pscustomobject]@{ Source = 'uia'; ProcessId = [int]$Process.Id; Name = [string]$Element.Current.Name; ClassName = [string]$Element.Current.ClassName }) } catch { $null = $_ }
                }
                try { foreach ($Window in @(Get-ProbeWin32Windows -ProcessIds @([int]$Process.Id))) { $Candidates.Add($Window) } } catch { $null = $_ }
                $Main = Select-E5MainWindow -Windows $Candidates.ToArray() -ProcessIds @([int]$Process.Id) -TitlePrefix ([string]$Req.TitlePrefix)
                if ($null -ne $Main) {
                    $Run.WindowMs = [int]$Watch.ElapsedMilliseconds
                    $Run.Title = [string]$Main.Name
                    $Run.ClassName = [string]$Main.ClassName
                    $Run.Source = [string]$Main.Source
                    break
                }
                if ($Process.HasExited) { throw "proces zakonczyl sie przed oknem (kod $($Process.ExitCode))" }
                if ($Watch.Elapsed.TotalSeconds -ge [int]$Req.WindowTimeoutSeconds) {
                    $Run.TimedOut = $true
                    $Run.ProcessWindows = @($Candidates | ForEach-Object { [pscustomobject]@{ Source = $_.Source; Name = $_.Name; ClassName = $_.ClassName } })
                    break
                }
                Start-Sleep -Milliseconds 100
            }
            $null = $Process.CloseMainWindow()
            if ($Process.WaitForExit([int]$Req.CloseTimeoutSeconds * 1000)) { $Run.Closed = $true }
            else {
                Stop-Process -Id $Process.Id -Force -ErrorAction SilentlyContinue
                $Run.Killed = $true
                $null = $Process.WaitForExit(10000)
            }
            if ($Process.HasExited) { $Run.ExitCode = $Process.ExitCode }
        }
        catch { $Run.Error = [string]$_.Exception.GetBaseException().Message }
        $Runs.Add([pscustomobject]$Run)
        if ($Run.Error) { break }
        Start-Sleep -Seconds ([int]$Req.PauseSeconds)
    }
    $Out.Runs = $Runs.ToArray()
}
catch { $Out.Error = [string]$_.Exception.GetBaseException().Message }
$Tmp = "$ResultFile.tmp"
[System.IO.File]::WriteAllText($Tmp, ($Out | ConvertTo-Json -Depth 5), (New-Object System.Text.UTF8Encoding($false)))
Move-Item -LiteralPath $Tmp -Destination $ResultFile -Force
'@
    return ("param([Parameter(Mandatory)][string]`$ResultFile, [Parameter(Mandatory)][string]`$Request)`n" + (Get-E5ProbeHelpersText) + "`n" + $Body)
}

function Get-E5ExtractProbeScript {
    # Sonda w sesji dso (faza motw, wariant Folder): wypakowanie zipu z Mark of the Web „przez Eksplorator” – obsługa folderów
    # skompresowanych powłoki (Shell.Application NameSpace(zip).Items() -> CopyHere, bez okna postępu), czekanie na exe o stałym rozmiarze.
    [CmdletBinding()]
    [OutputType([string])]
    param()
    return @'
param([Parameter(Mandatory)][string]$ResultFile, [Parameter(Mandatory)][string]$Request)
# Sonda wypakowania zipu przez powloke (DSOptimizer, tests\vm\explore\Invoke-E5Exploration.ps1). Tylko w VM DSO-26H2-HOME/PRO.
$ErrorActionPreference = 'Stop'
$Out = [ordered]@{ Sid = $null; Extracted = $false; Seconds = $null; ExeLength = $null; FileCount = $null; Error = $null }
try {
    if ($env:DSO_VM_TESTS_MOCK_ONLY) { throw 'DSO_VM_TESTS_MOCK_ONLY: sonda nie wypakowuje niczego w testach.' }
    if (@('DSO-26H2-HOME', 'DSO-26H2-PRO') -notcontains ([string]$env:COMPUTERNAME).ToUpperInvariant()) { throw "Sonda dziala tylko w VM DSO-26H2-HOME/PRO (komputer: '$env:COMPUTERNAME')." }
    $Req = [System.IO.File]::ReadAllText($Request, [System.Text.Encoding]::UTF8).TrimStart([char]0xFEFF) | ConvertFrom-Json
    $Out.Sid = [System.Security.Principal.WindowsIdentity]::GetCurrent().User.Value
    $Shell = New-Object -ComObject Shell.Application
    $Zip = $Shell.NameSpace([string]$Req.Zip)
    $Destination = $Shell.NameSpace([string]$Req.Destination)
    if ($null -eq $Zip -or $null -eq $Destination) { throw 'Powloka nie otworzyla zipu albo folderu docelowego.' }
    $Watch = [System.Diagnostics.Stopwatch]::StartNew()
    $Destination.CopyHere($Zip.Items(), 20)
    $Last = -1
    while ($Watch.Elapsed.TotalSeconds -lt [int]$Req.TimeoutSeconds) {
        Start-Sleep -Seconds 2
        if (-not [System.IO.File]::Exists([string]$Req.ExpectedExe)) { continue }
        $Length = (New-Object System.IO.FileInfo([string]$Req.ExpectedExe)).Length
        if ($Length -gt 0 -and $Length -eq $Last) { $Out.Extracted = $true; $Out.ExeLength = $Length; break }
        $Last = $Length
    }
    $Out.Seconds = [math]::Round($Watch.Elapsed.TotalSeconds, 1)
    $Out.FileCount = @(Get-ChildItem -LiteralPath ([string]$Req.Destination) -Recurse -File -ErrorAction SilentlyContinue).Count
    if (-not $Out.Extracted) { throw "Nie wypakowano '$($Req.ExpectedExe)' w $($Req.TimeoutSeconds) s." }
}
catch { $Out.Error = [string]$_.Exception.GetBaseException().Message }
$Tmp = "$ResultFile.tmp"
[System.IO.File]::WriteAllText($Tmp, ($Out | ConvertTo-Json -Depth 4), (New-Object System.Text.UTF8Encoding($false)))
Move-Item -LiteralPath $Tmp -Destination $ResultFile -Force
'@
}

function Start-E5Gui {
    # GUI z nośnika bez argumentów jako dso (zadanie: "<exe>" w cudzysłowie, Highest), potem sonda czekająca na okno główne (najwyżej
    # WaitSeconds). Okno zostaje otwarte (zamyka Stop-E5Gui), żeby host mógł zrobić zrzut.
    [CmdletBinding()]
    param(
        [Parameter(Mandatory)]$Plan,
        [Parameter(Mandatory)][string]$UserSid,
        [Parameter(Mandatory)][string]$Exe,
        [Parameter(Mandatory)][ValidatePattern('^[A-Za-z0-9-]+\z')][string]$Name,
        [ValidateRange(5, 600)][int]$WaitSeconds = 60
    )
    Assert-E5InsideVm
    $Out = [ordered]@{ Name = $Name; Exe = $Exe; Since = (Get-Date).ToString('o'); TaskName = "DSO-E5-gui-$Name"; Execute = $null; Started = $false; Found = $false; Seconds = $null
        ProcessId = $null; ProcessIds = @(); Title = $null; ClassName = $null; Source = $null; Bounds = $null; ProcessWindows = @(); Dpi = $null; ScreenWidth = $null; ScreenHeight = $null; LogPixels = $null
        ProbeSid = $null; ProbeAsDso = $null; ProbeError = $null; Error = $null }
    try {
        $Out.Execute = Get-E5TaskExecute -Path $Exe
        Register-E5Task -Plan $Plan -TaskName $Out.TaskName -Execute $Out.Execute -RunLevel 'Highest'
        $Out.Started = $true
        $Request = [ordered]@{ Exe = $Exe; TitlePrefix = [string]$Plan.MainWindowTitlePrefix; TimeoutSeconds = $WaitSeconds }
        $Probe = $null
        try { $Probe = Invoke-E5UserProbe -Plan $Plan -Name "window-$Name" -ScriptText (Get-E5WaitWindowProbeScript) -RequestJson (ConvertTo-Json -InputObject $Request) -TimeoutSeconds ($WaitSeconds + 60) }
        catch { $Out.ProbeError = Format-E5Error $_ }
        if ($null -ne $Probe) {
            foreach ($Field in 'Found', 'Seconds', 'ProcessId', 'ProcessIds', 'Title', 'ClassName', 'Source', 'Bounds', 'ProcessWindows', 'Dpi', 'ScreenWidth', 'ScreenHeight', 'LogPixels') { $Out[$Field] = Get-E5Property -Object $Probe -Name $Field }
            $Out.ProbeSid = [string](Get-E5Property -Object $Probe -Name 'Sid')
            $Out.ProbeAsDso = ($Out.ProbeSid -eq $UserSid)
            $Out.ProbeError = Get-E5Property -Object $Probe -Name 'Error'
        }
    }
    catch { $Out.Error = Format-E5Error $_ }
    return [pscustomobject]$Out
}

function Stop-E5Gui {
    # Zamknięcie GUI uruchomionego przez Start-E5Gui: procesy z pliku exe, wyrejestrowanie zadania. Błędy w Error, bez wyjątku.
    [CmdletBinding()]
    param(
        [Parameter(Mandatory)]$Plan,
        [Parameter(Mandatory)]$State
    )
    Assert-E5InsideVm
    $Out = [ordered]@{ Closed = @(); StillRunning = @(); TaskUnregistered = $false; Error = $null }
    $Errors = @()
    try {
        $Exe = [string](Get-E5Property -Object $State -Name 'Exe')
        if ($Exe) {
            $Stop = Stop-E5Processes -Plan $Plan -Ids @(Get-E5ExeProcesses -Path $Exe | ForEach-Object { [int]$_.ProcessId })
            $Out.Closed = $Stop.Closed
            $Out.StillRunning = $Stop.StillRunning
            $Errors += @($Stop.Errors)
        }
    }
    catch { $Errors += "zamknięcie: $(Format-E5Error $_)" }
    $TaskName = [string](Get-E5Property -Object $State -Name 'TaskName')
    if ($TaskName) {
        $Message = Unregister-E5Task -TaskName $TaskName
        if ($Message) { $Errors += $Message } else { $Out.TaskUnregistered = $true }
    }
    if ($Errors.Count -gt 0) { $Out.Error = $Errors -join ' | ' }
    return [pscustomobject]$Out
}

function Invoke-E5WinUtilCliStep {
    # Krok av: --execute tools.winutil z nośnika (handler pobiera winutil.ps1 do folderu pobrań i otwiera okno PowerShell), potem jak
    # wariant WinUtil: korzeń po linii poleceń (plik z folderu pobrań; awaryjnie polecenie irm), 60 s od startu, drzewo, okna (sonda jako
    # dso), Zone.Identifier pobranego pliku (RemoteSigned działa tylko bez MotW), na końcu Stop-E5Variant (detekcje, zamknięcie drzewa).
    [CmdletBinding()]
    param(
        [Parameter(Mandatory)]$Plan,
        [Parameter(Mandatory)][string]$UserSid,
        [Parameter(Mandatory)][string]$Exe,
        [Parameter(Mandatory)][ValidatePattern('^[A-Za-z0-9-]+\z')][string]$Name,
        [Parameter(Mandatory)][string]$Arguments
    )
    Assert-E5InsideVm
    $Since = Get-Date
    $Out = [ordered]@{ Since = $Since.ToString('o'); Cli = $null; RootPid = $null; RootMarker = $null; WaitSeconds = [int]$Plan.VariantWaitSeconds; AliveAfterWait = $false; Tree = @()
        RootFromDownloads = $false; DownloadedFile = $null; DownloadedZone = $null; DownloadedZoneError = $null; DownloadsFiles = @(); ProbeSid = $null; ProbeAsDso = $null; ProbeError = $null; Windows = @(); TreeWindows = @()
        WinUtilVisible = $false; WinUtilWindowTitle = $null; Stop = $null; Error = $null }
    try {
        $Out.Cli = Invoke-E5Cli -Plan $Plan -Exe $Exe -Name $Name -Arguments $Arguments -OutFile (Join-Path ([string]$Plan.RawDirectory) "av-$Name.json") -TimeoutSeconds ([int]$Plan.CliTimeoutSeconds)
        foreach ($Marker in @([string]$Plan.DownloadsDirectory, [string]$Plan.WinUtilProcessMarker)) {
            $Timeout = if ($Marker -eq [string]$Plan.DownloadsDirectory) { [int]$Plan.ProcessFindSeconds } else { 0 }
            $Out.RootPid = Find-E5RootProcess -Marker $Marker -Since $Since -TimeoutSeconds $Timeout
            if ($null -ne $Out.RootPid) { $Out.RootMarker = $Marker; $Out.RootFromDownloads = ($Marker -eq [string]$Plan.DownloadsDirectory); break }
        }
        $Remaining = [int]$Plan.VariantWaitSeconds - [int]((Get-Date) - $Since).TotalSeconds
        if ($Remaining -gt 0) { Start-Sleep -Seconds $Remaining }
        if ($null -ne $Out.RootPid) {
            $Out.Tree = @(Get-E5ProcessTree -RootPid ([int]$Out.RootPid))
            $Out.AliveAfterWait = (@($Out.Tree | Where-Object { [int]$_.ProcessId -eq [int]$Out.RootPid }).Count -gt 0)
            $RootLine = [string](@($Out.Tree | Where-Object { [int]$_.ProcessId -eq [int]$Out.RootPid } | ForEach-Object { $_.CommandLine }) | Select-Object -First 1)
            if ($RootLine -match '-File\s+"([^"]+)"') { $Out.DownloadedFile = $Matches[1] }
        }
        if ($Out.DownloadedFile) {
            try { $Out.DownloadedZone = Test-E5ZoneIdentifier -Path $Out.DownloadedFile }
            catch { $Out.DownloadedZoneError = Format-E5Error $_ }
        }
        if ([System.IO.Directory]::Exists([string]$Plan.DownloadsDirectory)) {
            $Out.DownloadsFiles = @(Get-ChildItem -LiteralPath ([string]$Plan.DownloadsDirectory) -File -ErrorAction Stop | ForEach-Object {
                    $Zone = $null
                    $ZoneError = $null
                    try { $Zone = (Test-E5ZoneIdentifier -Path $_.FullName).Exists } catch { $ZoneError = Format-E5Error $_ }
                    [pscustomobject]@{ Name = $_.Name; Length = $_.Length; ZoneIdentifier = $Zone; ZoneError = $ZoneError }
                })
        }
        $Probe = $null
        $ProbeFailure = $null
        try { $Probe = Invoke-E5UserProbe -Plan $Plan -Name "windows-$Name" -ScriptText (Get-E5WindowProbeScript) -TimeoutSeconds ([int]$Plan.ProbeTimeoutSeconds) }
        catch { $ProbeFailure = Format-E5Error $_ }
        $Summary = Get-E5ProbeWindowSummary -Probe $Probe -ProbeFailure $ProbeFailure -UserSid $UserSid -TreeIds @($Out.Tree | ForEach-Object { [int]$_.ProcessId }) -Pattern ([string]$Plan.WinUtilTitlePattern)
        foreach ($Field in 'ProbeSid', 'ProbeAsDso', 'ProbeError', 'Windows', 'TreeWindows') { $Out[$Field] = $Summary[$Field] }
        $Out.WinUtilVisible = $Summary.Visible
        $Out.WinUtilWindowTitle = $Summary.Title
    }
    catch { $Out.Error = Format-E5Error $_ }
    $Out.Stop = Stop-E5Variant -Plan $Plan -State ([pscustomobject]@{ Variant = $Name; Since = $Out.Since; RootPid = $Out.RootPid })
    return [pscustomobject]$Out
}

function Select-E5AppxCandidate {
    # Pierwsza pozycja AppX (id appx:...) z grupy Suggested i Removability = Removable z raportu --list-apps (kolejność raportu). Brak = $null.
    [CmdletBinding()]
    param([AllowNull()]$Report)
    foreach ($App in @(Get-E5Property -Object $Report -Name 'apps')) {
        if ($null -eq $App) { continue }
        if ([string]$App.id -like 'appx:*' -and [string]$App.group -eq 'Suggested' -and [string]$App.removability -eq 'Removable') {
            return [pscustomobject]@{ Id = [string]$App.id; Name = [string]$App.name; Publisher = [string]$App.publisher }
        }
    }
    return $null
}

function Invoke-E5AvPhase {
    # Faza av (z siecią, po defender): lista kroków z planu (AvSteps) na programie z nośnika jako dso, każde polecenie CLI z raportem --out
    # i kodem wyjścia; usunięcie pierwszej sugerowanej AppX i programu testowego Win32 DsoTestQuiet, instalacja 7-Zip i RustDesk, WinUtil
    # przez program (okno po 60 s, zamknięcie drzewa), start GUI bez argumentów (okno w 60 s, zamknięcie). Na końcu detekcje od startu
    # fazy i kopie logów (także logs\<PC>\ obok exe na VHDX). Błąd kroku trafia do kroku i nie przerywa kolejnych.
    [CmdletBinding()]
    param(
        [Parameter(Mandatory)]$Plan,
        [Parameter(Mandatory)][string]$UserSid
    )
    Assert-E5InsideVm
    $Since = Get-Date
    $Raw = [string]$Plan.RawDirectory
    $null = [System.IO.Directory]::CreateDirectory($Raw)
    $Out = [ordered]@{ Since = $Since.ToString('o'); Usb = $null; Steps = @(); Candidate = $null; Fixture = $null; Detections = $null; Logs = $null; Error = $null }
    $Out.Usb = Resolve-E5UsbVolume -Plan $Plan
    $Exe = [string]$Out.Usb.ExePath
    $Reports = @{}
    $Index = 0
    foreach ($Step in @($Plan.AvSteps)) {
        $Index++
        $Name = '{0:D2}-{1}' -f $Index, [string]$Step.Name
        $OutFile = Join-Path $Raw "av-$Name.json"
        $Expected = if ($null -ne (Get-E5Property -Object $Step -Name 'ExpectedExit')) { [int]$Step.ExpectedExit } else { 0 }
        $Record = [ordered]@{ Name = $Name; Kind = [string]$Step.Kind; Arguments = $null; ExpectedExit = $Expected; StartedAt = (Get-Date).ToString('o'); FinishedAt = $null; Result = $null; Extra = $null; Error = $null }
        try {
            switch ([string]$Step.Kind) {
                'cli' {
                    $Record.Arguments = "$($Step.Arguments) --out $OutFile"
                    $StepSince = Get-Date
                    $Timeout = if ($null -ne (Get-E5Property -Object $Step -Name 'TimeoutSeconds')) { [int]$Step.TimeoutSeconds } else { [int]$Plan.CliTimeoutSeconds }
                    $Record.Result = Invoke-E5Cli -Plan $Plan -Exe $Exe -Name $Name -Arguments $Record.Arguments -OutFile $OutFile -TimeoutSeconds $Timeout
                    $Reports[[string]$Step.Name] = $Record.Result.Report
                    $Close = [string](Get-E5Property -Object $Step -Name 'CloseProcess')
                    if ($Close) {
                        # Panel (launch) zostawia okno, np. mmc.exe Zarządzania komputerem: czekamy chwilę i zamykamy procesy utworzone w tym kroku.
                        Start-Sleep -Seconds ([int]$Plan.LaunchWaitSeconds)
                        $Found = @(Get-E5NewProcesses -Since $StepSince -Names @($Close))
                        $Record.Extra = [pscustomobject]@{ Launched = $Found; Stop = (Stop-E5Processes -Plan $Plan -Ids @($Found | ForEach-Object { [int]$_.ProcessId })) }
                    }
                    if ((Get-E5Property -Object $Step -Name 'WaitShell') -eq $true) { $Record.Extra = [pscustomobject]@{ Shell = (Wait-E5UserShell -UserSid $UserSid -TimeoutSeconds ([int]$Plan.ShellWaitSeconds)) } }
                }
                'remove-appx' {
                    $Out.Candidate = Select-E5AppxCandidate -Report $Reports[[string]$Step.ListFrom]
                    if ($null -eq $Out.Candidate) { throw "Brak pozycji AppX z grupy Suggested i Removability = Removable w raporcie kroku '$($Step.ListFrom)' – nic nie usunięto." }
                    $Record.Arguments = "--remove-app $($Out.Candidate.Id) --restore-point $($Step.RestorePoint) --out $OutFile"
                    $Record.Result = Invoke-E5Cli -Plan $Plan -Exe $Exe -Name $Name -Arguments $Record.Arguments -OutFile $OutFile -TimeoutSeconds ([int]$Plan.CliTimeoutSeconds)
                }
                'remove-win32' {
                    $Out.Fixture = Install-E5Fixture -Plan $Plan
                    if (-not $Out.Fixture.Present) { throw "Program testowy $($Plan.FixtureKeyName) nie został zainstalowany (kod $($Out.Fixture.ExitCode)): $($Out.Fixture.Error) $($Out.Fixture.Stderr)" }
                    $Record.Arguments = "--remove-app $($Step.AppId) --restore-point $($Step.RestorePoint) --out $OutFile"
                    $Record.Result = Invoke-E5Cli -Plan $Plan -Exe $Exe -Name $Name -Arguments $Record.Arguments -OutFile $OutFile -TimeoutSeconds ([int]$Plan.CliTimeoutSeconds)
                }
                'winutil' {
                    $Record.Arguments = "$($Step.Arguments) --out $OutFile"
                    $Record.Extra = Invoke-E5WinUtilCliStep -Plan $Plan -UserSid $UserSid -Exe $Exe -Name $Name -Arguments $Record.Arguments
                    $Record.Result = $Record.Extra.Cli
                }
                'gui' {
                    $Gui = Start-E5Gui -Plan $Plan -UserSid $UserSid -Exe $Exe -Name $Name -WaitSeconds ([int]$Plan.GuiWindowSeconds)
                    $Record.Extra = [pscustomobject]@{ Start = $Gui; Stop = (Stop-E5Gui -Plan $Plan -State $Gui) }
                }
                default { throw "Nieznany rodzaj kroku '$($Step.Kind)'." }
            }
        }
        catch { $Record.Error = Format-E5Error $_ }
        $Record.FinishedAt = (Get-Date).ToString('o')
        $Out.Steps += [pscustomobject]$Record
    }
    $Out.Detections = Get-E5Detections -Since $Since -LogName ([string]$Plan.DefenderEventLog) -EventIds @($Plan.DefenderEventIds | ForEach-Object { [int]$_ })
    try { $Out.Logs = Copy-E5DsoLogs -Plan $Plan -ExeDirectory ([System.IO.Path]::GetDirectoryName($Exe)) }
    catch { $Out.Error = "kopie logów: $(Format-E5Error $_)" }
    return [pscustomobject]$Out
}

function Invoke-E5StartupPhase {
    # Faza startup: sonda w sesji dso uruchamia GUI z nośnika StartupRuns razy (pierwszy start = pierwszy po starcie VM, faza idzie przed av),
    # czas do okna głównego (UI Automation) i zamknięcie; potem procesy, które przeżyły, są zamykane, a detekcje odczytane od startu fazy.
    [CmdletBinding()]
    param(
        [Parameter(Mandatory)]$Plan,
        [Parameter(Mandatory)][string]$UserSid
    )
    Assert-E5InsideVm
    $Since = Get-Date
    $Out = [ordered]@{ Since = $Since.ToString('o'); Usb = $null; ProbeSid = $null; ProbeAsDso = $null; ProbeError = $null; Elevated = $null; Runs = @(); Leftover = $null; Detections = $null; Error = $null }
    try {
        $Out.Usb = Resolve-E5UsbVolume -Plan $Plan
        $Request = [ordered]@{ Exe = [string]$Out.Usb.ExePath; TitlePrefix = [string]$Plan.MainWindowTitlePrefix; Runs = [int]$Plan.StartupRuns; WindowTimeoutSeconds = [int]$Plan.StartupWindowTimeoutSeconds
            CloseTimeoutSeconds = [int]$Plan.StartupCloseTimeoutSeconds; PauseSeconds = [int]$Plan.StartupPauseSeconds }
        $Limit = [int]$Plan.StartupRuns * ([int]$Plan.StartupWindowTimeoutSeconds + [int]$Plan.StartupCloseTimeoutSeconds + [int]$Plan.StartupPauseSeconds + 15) + 60
        $Probe = $null
        try { $Probe = Invoke-E5UserProbe -Plan $Plan -Name 'startup' -ScriptText (Get-E5StartupProbeScript) -RequestJson (ConvertTo-Json -InputObject $Request) -TimeoutSeconds ([math]::Min(900, $Limit)) }
        catch { $Out.ProbeError = Format-E5Error $_ }
        if ($null -ne $Probe) {
            $Out.ProbeSid = [string](Get-E5Property -Object $Probe -Name 'Sid')
            $Out.ProbeAsDso = ($Out.ProbeSid -eq $UserSid)
            $Out.ProbeError = Get-E5Property -Object $Probe -Name 'Error'
            $Out.Elevated = Get-E5Property -Object $Probe -Name 'Elevated'
            $Out.Runs = @(@(Get-E5Property -Object $Probe -Name 'Runs') | Where-Object { $null -ne $_ })
        }
        $Out.Leftover = Stop-E5Processes -Plan $Plan -Ids @(Get-E5ExeProcesses -Path ([string]$Out.Usb.ExePath) | ForEach-Object { [int]$_.ProcessId })
    }
    catch { $Out.Error = Format-E5Error $_ }
    $Out.Detections = Get-E5Detections -Since $Since -LogName ([string]$Plan.DefenderEventLog) -EventIds @($Plan.DefenderEventIds | ForEach-Object { [int]$_ })
    return [pscustomobject]$Out
}

function Start-E5Motw {
    # Faza motw (z siecią), część 1: plik wydania z nośnika na pulpit dso z Zone.Identifier ZoneId=3 (jak z przeglądarki), dla Folder
    # wypakowanie zipu przez powłokę dso (MotW przechodzi na pliki), potem start przez powłokę: zadanie jako dso z RunLevel Limited
    # (jak Eksplorator) uruchamia explorer.exe "<exe>" – SmartScreen i UAC jak przy dwukliku. Zrzut po MotwWaitSeconds robi host.
    [CmdletBinding()]
    param(
        [Parameter(Mandatory)]$Plan,
        [Parameter(Mandatory)][string]$UserSid
    )
    Assert-E5InsideVm
    $Since = Get-Date
    $Artifact = Get-E5Property -Object $Plan -Name 'Artifact'
    $Out = [ordered]@{ Since = $Since.ToString('o'); Variant = [string](Get-E5Property -Object $Artifact -Name 'Variant'); Usb = $null; Desktop = $null; Copied = $null; CopiedZone = $null
        Extract = $null; LaunchPath = $null; LaunchZone = $null; TaskName = 'DSO-E5-motw'; Launched = $false; LaunchedAt = $null; Error = $null }
    try {
        $Out.Usb = Resolve-E5UsbVolume -Plan $Plan
        if (-not $Out.Usb.ReleaseExists) { throw "Na nośniku nie ma pliku wydania '$($Out.Usb.ReleasePath)'." }
        $Out.Desktop = Join-Path (Get-E5UserProfile -UserSid $UserSid) 'Desktop'
        if (-not [System.IO.Directory]::Exists($Out.Desktop)) { throw "Brak pulpitu konta dso '$($Out.Desktop)'." }
        $Out.Copied = Join-Path $Out.Desktop ([string]$Artifact.Name)
        Copy-Item -LiteralPath ([string]$Out.Usb.ReleasePath) -Destination $Out.Copied -Force
        Set-Content -LiteralPath $Out.Copied -Stream 'Zone.Identifier' -Value ("[ZoneTransfer]`r`nZoneId=$([int]$Plan.MotwZoneId)") -Encoding Ascii
        $Out.CopiedZone = Test-E5ZoneIdentifier -Path $Out.Copied
        if ($Out.CopiedZone.ZoneId -ne [int]$Plan.MotwZoneId) { throw "Zone.Identifier pliku '$($Out.Copied)' nie ma ZoneId=$($Plan.MotwZoneId)." }
        if ($Out.Variant -eq 'Folder') {
            $Destination = Join-Path $Out.Desktop ([System.IO.Path]::GetFileNameWithoutExtension([string]$Artifact.Name))
            $null = [System.IO.Directory]::CreateDirectory($Destination)
            $Expected = Join-Path $Destination ([string]$Artifact.ExeRelativePath)
            $Request = [ordered]@{ Zip = $Out.Copied; Destination = $Destination; ExpectedExe = $Expected; TimeoutSeconds = [int]$Plan.ExtractTimeoutSeconds }
            $Out.Extract = Invoke-E5UserProbe -Plan $Plan -Name 'motw-extract' -ScriptText (Get-E5ExtractProbeScript) -RequestJson (ConvertTo-Json -InputObject $Request) -TimeoutSeconds ([int]$Plan.ExtractTimeoutSeconds + 60)
            if ((Get-E5Property -Object $Out.Extract -Name 'Extracted') -ne $true) { throw "Wypakowanie przez powłokę nie powiodło się: $(Get-E5Property -Object $Out.Extract -Name 'Error')" }
            $Out.LaunchPath = $Expected
        }
        else { $Out.LaunchPath = $Out.Copied }
        $Out.LaunchZone = Test-E5ZoneIdentifier -Path $Out.LaunchPath
        Register-E5Task -Plan $Plan -TaskName $Out.TaskName -Execute (Join-Path $env:SystemRoot 'explorer.exe') -Argument (Get-E5TaskExecute -Path $Out.LaunchPath) -RunLevel 'Limited'
        $Out.Launched = $true
        $Out.LaunchedAt = (Get-Date).ToString('o')
    }
    catch { $Out.Error = Format-E5Error $_ }
    return [pscustomobject]$Out
}

function Get-E5SmartScreenEvidence {
    # Dowody okna SmartScreen (czysta logika): widoczne okno Win32 albo okno UIA należące do procesu smartscreen.exe (po PID z listy
    # procesów albo nazwie procesu okna Win32) albo okno o tytule SmartScreen (np. PL „System Windows ochronił ten komputer”,
    # EN „Windows protected your PC”). Zwraca listę opisów (pusta = brak dowodu).
    [CmdletBinding()]
    [OutputType([string[]])]
    param(
        [AllowEmptyCollection()][AllowNull()][object[]]$Windows = @(),
        [AllowEmptyCollection()][AllowNull()][object[]]$Win32Windows = @(),
        [AllowEmptyCollection()][AllowNull()][object[]]$Processes = @(),
        [AllowEmptyCollection()][string[]]$Titles = @()
    )
    $Evidence = @()
    $Smart = @(@($Processes) | Where-Object { $null -ne $_ -and [string]$_.Name -ieq 'smartscreen.exe' } | ForEach-Object { [int]$_.ProcessId })
    foreach ($Window in @($Win32Windows | Where-Object { $null -ne $_ })) {
        $Owner = ([string]$Window.ProcessName -ieq 'smartscreen.exe') -or ($Smart -contains [int]$Window.ProcessId)
        $Titled = @($Titles | Where-Object { $_ -and ([string]$Window.Name).IndexOf($_, [System.StringComparison]::OrdinalIgnoreCase) -ge 0 }).Count -gt 0
        if ($Owner) { $Evidence += "okno Win32 procesu smartscreen.exe (PID $($Window.ProcessId), klasa '$($Window.ClassName)', tytuł '$($Window.Name)')" }
        elseif ($Titled) { $Evidence += "okno Win32 o tytule SmartScreen '$($Window.Name)' (PID $($Window.ProcessId), proces '$($Window.ProcessName)')" }
    }
    foreach ($Window in @($Windows | Where-Object { $null -ne $_ })) {
        $Titled = @($Titles | Where-Object { $_ -and ([string]$Window.Name).IndexOf($_, [System.StringComparison]::OrdinalIgnoreCase) -ge 0 }).Count -gt 0
        if ($Smart -contains [int]$Window.ProcessId) { $Evidence += "okno UIA procesu smartscreen.exe (PID $($Window.ProcessId), tytuł '$($Window.Name)')" }
        elseif ($Titled) { $Evidence += "okno UIA o tytule SmartScreen '$($Window.Name)' (PID $($Window.ProcessId))" }
    }
    return $Evidence
}

function Get-E5MotwShown {
    # Co pokazał start z MotW (czysta logika): dowód okna SmartScreen (Get-E5SmartScreenEvidence) = SmartScreen, działający consent.exe = UAC
    # (bezpieczny pulpit, widoczny tylko na zrzucie), proces programu = Aplikacja; nic z tego = 'nic' (problem ważności w podsumowaniu).
    [CmdletBinding()]
    [OutputType([string[]])]
    param(
        [AllowEmptyCollection()][AllowNull()][object[]]$Windows = @(),
        [AllowEmptyCollection()][AllowNull()][object[]]$Win32Windows = @(),
        [AllowEmptyCollection()][AllowNull()][object[]]$Processes = @(),
        [AllowEmptyCollection()][string[]]$Titles = @()
    )
    $Shown = @()
    if (@(Get-E5SmartScreenEvidence -Windows $Windows -Win32Windows $Win32Windows -Processes $Processes -Titles $Titles).Count -gt 0) { $Shown += 'SmartScreen' }
    if (@($Processes | Where-Object { $null -ne $_ -and [string]$_.Name -ieq 'consent.exe' }).Count -gt 0) { $Shown += 'UAC' }
    if (@($Processes | Where-Object { $null -ne $_ -and [string]$_.Name -like 'DSOptimizer*' }).Count -gt 0) { $Shown += 'Aplikacja' }
    if ($Shown.Count -eq 0) { $Shown += 'nic' }
    return $Shown
}

function Stop-E5Motw {
    # Faza motw, część 2 (po zrzucie): okna pulpitu dso (sonda: UIA i Win32), WSZYSTKIE procesy smartscreen.exe (bez filtra czasu),
    # consent.exe od startu fazy i proces programu, klasyfikacja (Get-E5MotwShown) z dowodami SmartScreen, zamknięcie procesów
    # smartscreen.exe z widocznym oknem i programu, wyrejestrowanie zadania, detekcje od startu fazy.
    # consent.exe (UAC na bezpiecznym pulpicie, proces SYSTEM) NIE jest zabijany: zostaje do restartu fazy dpi albo revertu VM.
    [CmdletBinding()]
    param(
        [Parameter(Mandatory)]$Plan,
        [Parameter(Mandatory)][string]$UserSid,
        [Parameter(Mandatory)]$State
    )
    Assert-E5InsideVm
    $Out = [ordered]@{ Windows = @(); Win32Windows = @(); Win32Error = $null; Processes = @(); SmartScreenProcesses = @(); SmartScreenEvidence = @(); Shown = @()
        ProbeSid = $null; ProbeAsDso = $null; ProbeError = $null; Stop = $null; TaskUnregistered = $false; Detections = $null; Error = $null }
    $Errors = @()
    $SinceText = [string](Get-E5Property -Object $State -Name 'Since')
    $Since = if ($SinceText) { [datetime]::Parse($SinceText, [System.Globalization.CultureInfo]::InvariantCulture, [System.Globalization.DateTimeStyles]::RoundtripKind) } else { (Get-Date).AddMinutes(-15) }
    try {
        $Probe = $null
        $ProbeFailure = $null
        try { $Probe = Invoke-E5UserProbe -Plan $Plan -Name 'windows-motw' -ScriptText (Get-E5WindowProbeScript) -TimeoutSeconds ([int]$Plan.ProbeTimeoutSeconds) }
        catch { $ProbeFailure = Format-E5Error $_ }
        $Summary = Get-E5ProbeWindowSummary -Probe $Probe -ProbeFailure $ProbeFailure -UserSid $UserSid -Pattern '^$'
        foreach ($Field in 'ProbeSid', 'ProbeAsDso', 'ProbeError', 'Windows') { $Out[$Field] = $Summary[$Field] }
        $Out.Win32Windows = @(@(Get-E5Property -Object $Probe -Name 'Win32Windows') | Where-Object { $null -ne $_ })
        $Out.Win32Error = Get-E5Property -Object $Probe -Name 'Win32Error'
        $Out.SmartScreenProcesses = @(Get-E5NamedProcesses -Names @('smartscreen.exe'))
        $Processes = @($Out.SmartScreenProcesses) + @(Get-E5NewProcesses -Since $Since -Names @('consent.exe'))
        $LaunchPath = [string](Get-E5Property -Object $State -Name 'LaunchPath')
        if ($LaunchPath) { $Processes += @(Get-E5ExeProcesses -Path $LaunchPath) }
        $Out.Processes = $Processes
        $Titles = @($Plan.SmartScreenTitles | ForEach-Object { [string]$_ })
        $Out.SmartScreenEvidence = @(Get-E5SmartScreenEvidence -Windows @($Out.Windows) -Win32Windows @($Out.Win32Windows) -Processes $Processes -Titles $Titles)
        $Out.Shown = @(Get-E5MotwShown -Windows @($Out.Windows) -Win32Windows @($Out.Win32Windows) -Processes $Processes -Titles $Titles)
        # Zamykane: smartscreen.exe z widocznym oknem (dialog) i program; consent.exe zostaje.
        $WindowOwners = @(@($Out.Win32Windows) + @($Out.Windows) | ForEach-Object { [int]$_.ProcessId })
        $SmartWithWindow = @($Out.SmartScreenProcesses | Where-Object { $WindowOwners -contains [int]$_.ProcessId } | ForEach-Object { [int]$_.ProcessId })
        $AppIds = @($Processes | Where-Object { [string]$_.Name -like 'DSOptimizer*' } | ForEach-Object { [int]$_.ProcessId })
        $Out.Stop = Stop-E5Processes -Plan $Plan -Ids @($SmartWithWindow + $AppIds)
        $Errors += @($Out.Stop.Errors)
    }
    catch { $Errors += Format-E5Error $_ }
    $TaskName = [string](Get-E5Property -Object $State -Name 'TaskName')
    if ($TaskName) {
        $Message = Unregister-E5Task -TaskName $TaskName
        if ($Message) { $Errors += $Message } else { $Out.TaskUnregistered = $true }
    }
    $Out.Detections = Get-E5Detections -Since $Since -LogName ([string]$Plan.DefenderEventLog) -EventIds @($Plan.DefenderEventIds | ForEach-Object { [int]$_ })
    if ($Errors.Count -gt 0) { $Out.Error = $Errors -join ' | ' }
    return [pscustomobject]$Out
}

function Set-E5DpiScale {
    # Faza dpi, część 1: skala dla konta dso przez LogPixels (DWORD, 144 = 150%) i Win8DpiScaling = 1 w HKU\<SID>\Control Panel\Desktop
    # (jak „Skalowanie niestandardowe” Ustawień); działa po ponownym zalogowaniu (host restartuje VM, autologon dso). Stan przed i po.
    [CmdletBinding()]
    param(
        [Parameter(Mandatory)]$Plan,
        [Parameter(Mandatory)][ValidatePattern('^S-1-5-21-[0-9-]+\z')][string]$UserSid
    )
    Assert-E5InsideVm
    $Path = "HKU\$UserSid\Control Panel\Desktop"
    $Out = [ordered]@{ Key = $Path; Before = @(); After = @(); Error = $null }
    $Out.Before = @(foreach ($Name in 'LogPixels', 'Win8DpiScaling') { Read-ExploreRegistryValue -Path $Path -Name $Name })
    $Base = [Microsoft.Win32.RegistryKey]::OpenBaseKey([Microsoft.Win32.RegistryHive]::Users, [Microsoft.Win32.RegistryView]::Registry64)
    try {
        $Key = $Base.OpenSubKey("$UserSid\Control Panel\Desktop", $true)
        if ($null -eq $Key) { throw "Brak klucza $Path (gałąź konta dso niezaładowana?)." }
        try {
            $Key.SetValue('LogPixels', [int]$Plan.DpiLogPixels, [Microsoft.Win32.RegistryValueKind]::DWord)
            $Key.SetValue('Win8DpiScaling', 1, [Microsoft.Win32.RegistryValueKind]::DWord)
        }
        finally { $Key.Dispose() }
    }
    finally { $Base.Dispose() }
    $Out.After = @(foreach ($Name in 'LogPixels', 'Win8DpiScaling') { Read-ExploreRegistryValue -Path $Path -Name $Name })
    return [pscustomobject]$Out
}

function Restart-E5Vm {
    # Restart VM (faza dpi: LogPixels działa po ponownym zalogowaniu; autologon dso).
    [CmdletBinding()]
    param()
    Assert-E5InsideVm
    Restart-Computer -Force
}

function Start-E5DpiProbe {
    # Faza dpi, część 2 (po restarcie): GUI z nośnika jako dso, okno główne, skuteczne DPI ekranu i prostokąt okna (sonda). Zrzut robi host.
    [CmdletBinding()]
    param(
        [Parameter(Mandatory)]$Plan,
        [Parameter(Mandatory)][string]$UserSid
    )
    Assert-E5InsideVm
    $Since = Get-Date
    $Out = [ordered]@{ Since = $Since.ToString('o'); Usb = $null; Gui = $null; Error = $null }
    try {
        $Out.Usb = Resolve-E5UsbVolume -Plan $Plan
        $Out.Gui = Start-E5Gui -Plan $Plan -UserSid $UserSid -Exe ([string]$Out.Usb.ExePath) -Name 'dpi' -WaitSeconds ([int]$Plan.GuiWindowSeconds)
    }
    catch { $Out.Error = Format-E5Error $_ }
    return [pscustomobject]$Out
}

function Stop-E5DpiProbe {
    # Faza dpi, część 3 (po zrzucie): zamknięcie GUI i detekcje od startu części 2.
    [CmdletBinding()]
    param(
        [Parameter(Mandatory)]$Plan,
        [Parameter(Mandatory)]$State
    )
    Assert-E5InsideVm
    $SinceText = [string](Get-E5Property -Object $State -Name 'Since')
    $Since = if ($SinceText) { [datetime]::Parse($SinceText, [System.Globalization.CultureInfo]::InvariantCulture, [System.Globalization.DateTimeStyles]::RoundtripKind) } else { (Get-Date).AddMinutes(-15) }
    $Out = [ordered]@{ Stop = $null; Detections = $null; Error = $null }
    $Gui = Get-E5Property -Object $State -Name 'Gui'
    if ($null -ne $Gui) { $Out.Stop = Stop-E5Gui -Plan $Plan -State $Gui; $Out.Error = $Out.Stop.Error }
    $Out.Detections = Get-E5Detections -Since $Since -LogName ([string]$Plan.DefenderEventLog) -EventIds @($Plan.DefenderEventIds | ForEach-Object { [int]$_ })
    return [pscustomobject]$Out
}

function Invoke-E5Collection {
    # Punkt wejścia w VM: zwraca JSON {meta, phase, error, data}. ArgumentJson: wariant (winutil-start) albo stan części startowej
    # (winutil-stop, motw-stop, dpi-stop).
    [CmdletBinding()]
    param(
        [Parameter(Mandatory)][string]$PlanJson,
        [Parameter(Mandatory)][string]$Phase,
        [Parameter(Mandatory)][string]$UserSid,
        [string]$ArgumentJson = '{}'
    )
    Assert-E5InsideVm
    $Document = [ordered]@{ meta = $null; phase = $Phase; error = $null; data = $null }
    try { $Document.meta = Get-ExploreMeta } catch { $Document.meta = [ordered]@{ Error = $_.Exception.Message } }
    try {
        $Plan = $PlanJson | ConvertFrom-Json
        $Arguments = $ArgumentJson | ConvertFrom-Json
        switch ($Phase) {
            'defender' { $Document.data = Invoke-E5DefenderPhase -Plan $Plan }
            'winutil-start' { $Document.data = Start-E5Variant -Plan $Plan -Variant ([string](Get-E5Property -Object $Arguments -Name 'Variant')) -UserSid $UserSid }
            'winutil-stop' { $Document.data = Stop-E5Variant -Plan $Plan -State $Arguments }
            'startup' { $Document.data = Invoke-E5StartupPhase -Plan $Plan -UserSid $UserSid }
            'av' { $Document.data = Invoke-E5AvPhase -Plan $Plan -UserSid $UserSid }
            'motw-start' { $Document.data = Start-E5Motw -Plan $Plan -UserSid $UserSid }
            'motw-stop' { $Document.data = Stop-E5Motw -Plan $Plan -UserSid $UserSid -State $Arguments }
            'dpi-set' { $Document.data = Set-E5DpiScale -Plan $Plan -UserSid $UserSid }
            'dpi-start' { $Document.data = Start-E5DpiProbe -Plan $Plan -UserSid $UserSid }
            'dpi-stop' { $Document.data = Stop-E5DpiProbe -Plan $Plan -State $Arguments }
            default { throw "Nieznana faza '$Phase'." }
        }
    }
    catch { $Document.error = "$(Format-E5Error $_) | $($_.ScriptStackTrace)" }
    try { return ($Document | ConvertTo-Json -Depth 14) }
    catch { return ([ordered]@{ meta = $Document.meta; phase = $Phase; error = "ConvertTo-Json: $(Format-E5Error $_)"; data = $null } | ConvertTo-Json -Depth 6) }
}

# ---------------------------------------------------------------------------------------------------------------------
# Strona hosta (nic z tego nie jest wysyłane do VM).
# ---------------------------------------------------------------------------------------------------------------------

function Get-E5RemoteFunctionNames {
    return @(
        'Test-E5VmComputerName', 'Assert-E5InsideVm', 'Format-E5Error', 'Format-E5Hex', 'Get-E5Property', 'Assert-E5PlainPath', 'Test-E5Network',
        'Get-E5DefenderState', 'Update-E5Signatures', 'Get-E5Detections', 'Invoke-E5DefenderPhase',
        'Select-E5WinUtilAsset', 'Invoke-E5HttpText', 'Save-E5HttpFile', 'Save-E5WinUtilDownload', 'Test-E5DefenderBlock', 'Get-E5VariantArguments', 'Start-E5UserProcess',
        'Find-E5RootProcess', 'Get-E5ProcessTree', 'Get-E5WindowProbeScript', 'Invoke-E5UserProbe', 'Start-E5Variant', 'Stop-E5Variant', 'Get-E5ProbeWindowSummary', 'Select-E5MainWindow', 'Get-E5ProbeHelpersText', 'Get-E5NamedProcesses', 'Get-E5SmartScreenEvidence',
        'Get-E5TaskExecute', 'ConvertTo-E5ExitCode', 'Read-E5JsonFile', 'Test-E5ZoneIdentifier', 'Get-E5UserProfile', 'Get-E5ExeProcesses', 'Get-E5NewProcesses',
        'Stop-E5Processes', 'Unregister-E5Task', 'Register-E5Task', 'Wait-E5Task', 'Invoke-E5Cli', 'Resolve-E5UsbVolume', 'Wait-E5UserShell', 'Install-E5Fixture',
        'Copy-E5DsoLogs', 'Get-E5WaitWindowProbeScript', 'Get-E5StartupProbeScript', 'Get-E5ExtractProbeScript', 'Start-E5Gui', 'Stop-E5Gui', 'Invoke-E5WinUtilCliStep',
        'Select-E5AppxCandidate', 'Invoke-E5AvPhase', 'Invoke-E5StartupPhase', 'Start-E5Motw', 'Get-E5MotwShown', 'Stop-E5Motw', 'Set-E5DpiScale', 'Restart-E5Vm',
        'Start-E5DpiProbe', 'Stop-E5DpiProbe', 'Invoke-E5Collection'
    )
}

function Get-E5HostOnlyFunctionNames {
    return @('Get-E5Plan', 'Get-E5RemoteFunctionNames', 'Get-E5HostOnlyFunctionNames', 'Get-E5RemoteScript', 'Get-E5NetworkPhases', 'Get-E5ArtifactPhases', 'Get-E5PhaseOrder',
        'Resolve-E5Artifact', 'Get-E5RunArguments', 'Get-E5PhaseError', 'Test-E5SignaturesUpdated', 'Get-E5VariantSummary', 'Get-E5DetectionProblems', 'Get-E5AvSummary',
        'Get-E5StartupSummary', 'Get-E5MotwSummary', 'Get-E5DpiSummary', 'Assert-E5UsbHostDisk', 'Copy-E5ArtifactToUsb', 'New-E5UsbDisk', 'Copy-E5FixturesToVm',
        'Restart-E5VmAndWait', 'Invoke-E5VmPhase', 'Invoke-E5WinUtilPhase', 'Save-E5Screenshot', 'Invoke-E5MotwPhase', 'Invoke-E5DpiPhase', 'Invoke-E5HostPhase',
        'Invoke-E5VmRun', 'Invoke-E5Exploration')
}

function Get-E5RemoteScript {
    # Tekst definicji funkcji zdalnych: część wspólna z modułu + funkcje E5 (FunctionInfo pobrane tu, w zasięgu skryptu).
    [CmdletBinding()]
    [OutputType([string])]
    param()
    $Names = @(Get-ExploreCommonRemoteFunctionNames) + @(Get-E5RemoteFunctionNames)
    return (ConvertTo-ExploreRemoteScript -Command @($Names | ForEach-Object { Get-Command -Name $_ -CommandType Function -ErrorAction Stop }))
}

function Get-E5NetworkPhases {
    # Fazy z siecią: karta VM podłączona do 'Default Switch' tylko na czas tych faz (SmartScreen i ochrona w chmurze potrzebują sieci,
    # av instaluje z winget i GitHuba i pobiera WinUtil). dpi idzie bez sieci.
    [CmdletBinding()]
    [OutputType([string[]])]
    param()
    return @('defender', 'winutil', 'startup', 'av', 'motw')
}

function Get-E5ArtifactPhases {
    # Fazy na wydaniu z nośnika (VHDX z artefaktem Build-Release.ps1): wymagają -Variant i -ArtifactPath.
    [CmdletBinding()]
    [OutputType([string[]])]
    param()
    return @('startup', 'av', 'motw', 'dpi')
}

function Get-E5PhaseOrder {
    # Wybrane fazy w stałej kolejności; defender dokładany i zawsze pierwszy, gdy wybrano jakąkolwiek inną fazę (K1: pomiar tylko na
    # świeżych sygnaturach). startup przed av, bo pierwszy start ma być pierwszym uruchomieniem programu po starcie VM; dpi na końcu (restart).
    [CmdletBinding()]
    [OutputType([string[]])]
    param([Parameter(Mandatory)][string[]]$Phase)
    $Order = @('defender', 'winutil', 'startup', 'av', 'motw', 'dpi')
    $Wanted = @($Phase)
    if (@($Wanted | Where-Object { $_ -ne 'defender' }).Count -gt 0) { $Wanted += 'defender' }
    return @($Order | Where-Object { $Wanted -contains $_ })
}

function Resolve-E5Artifact {
    # -Variant + -ArtifactPath -> opis artefaktu z tools\release\Build-Release.ps1 (plik DSOptimizer-<wersja>.exe dla SingleExe albo
    # DSOptimizer-<wersja>.zip dla Folder, obok SHA256SUMS.txt z tą samą sumą; zip musi mieć DSOptimizer\DSOptimizer.exe). Oba puste = $null.
    # Każdy błąd = wyjątek, zanim cokolwiek dotknie VM.
    [CmdletBinding()]
    param(
        [AllowEmptyString()][AllowNull()][string]$Variant,
        [AllowEmptyString()][AllowNull()][string]$ArtifactPath
    )
    if ([string]::IsNullOrWhiteSpace($Variant) -and [string]::IsNullOrWhiteSpace($ArtifactPath)) { return $null }
    if ([string]::IsNullOrWhiteSpace($Variant)) { throw '-ArtifactPath wymaga -Variant SingleExe albo Folder.' }
    if (@('SingleExe', 'Folder') -cnotcontains $Variant) { throw "-Variant '$Variant': dozwolone SingleExe albo Folder." }
    if ([string]::IsNullOrWhiteSpace($ArtifactPath)) { throw "-Variant $Variant wymaga -ArtifactPath (plik wydania z tools\release\Build-Release.ps1 -Variant $Variant)." }
    if ($ArtifactPath -notmatch '^[A-Za-z]:\\' -and $ArtifactPath -notmatch '^\\\\[^\\]+\\[^\\]+') { throw "-ArtifactPath musi być ścieżką bezwzględną: '$ArtifactPath'." }
    $Full = [System.IO.Path]::GetFullPath($ArtifactPath)
    if (-not [System.IO.File]::Exists($Full)) { throw "Brak pliku wydania '$Full'." }
    $Name = [System.IO.Path]::GetFileName($Full)
    $Extension = if ($Variant -eq 'SingleExe') { 'exe' } else { 'zip' }
    if ($Name -notmatch "^DSOptimizer-(\d+\.\d+\.\d+)\.$Extension\z") { throw "Plik '$Name' nie jest wydaniem $Variant (oczekiwano DSOptimizer-<wersja>.$Extension)." }
    $Version = $Matches[1]
    $Sums = Join-Path ([System.IO.Path]::GetDirectoryName($Full)) 'SHA256SUMS.txt'
    if (-not [System.IO.File]::Exists($Sums)) { throw "Brak '$Sums' obok pliku wydania." }
    $Expected = $null
    foreach ($Line in [System.IO.File]::ReadAllLines($Sums)) {
        if ($Line -match '^([0-9a-fA-F]{64}) [ *](.+)$' -and $Matches[2] -ceq $Name) { $Expected = $Matches[1].ToLowerInvariant() }
    }
    if ($null -eq $Expected) { throw "SHA256SUMS.txt nie ma sumy pliku '$Name'." }
    $Sha = (Get-FileHash -LiteralPath $Full -Algorithm SHA256 -ErrorAction Stop).Hash.ToLowerInvariant()
    if ($Sha -cne $Expected) { throw "SHA256 pliku '$Name' ($Sha) różni się od SHA256SUMS.txt ($Expected)." }
    $ExeRelative = $Name
    if ($Variant -eq 'Folder') {
        $ExeRelative = 'DSOptimizer\DSOptimizer.exe'
        Add-Type -AssemblyName System.IO.Compression.FileSystem
        $Zip = [System.IO.Compression.ZipFile]::OpenRead($Full)
        try { $HasExe = @($Zip.Entries | Where-Object { ($_.FullName -replace '\\', '/') -ceq 'DSOptimizer/DSOptimizer.exe' }).Count -eq 1 }
        finally { $Zip.Dispose() }
        if (-not $HasExe) { throw "Zip '$Name' nie ma DSOptimizer\DSOptimizer.exe." }
    }
    return [ordered]@{ Variant = $Variant; Path = $Full; Name = $Name; Version = $Version; Sha256 = $Sha; Bytes = (New-Object System.IO.FileInfo($Full)).Length; ExeRelativePath = $ExeRelative }
}

function Get-E5RunArguments {
    [CmdletBinding()]
    param(
        [Parameter(Mandatory)][string[]]$VmName,
        [Parameter(Mandatory)][string[]]$Phase,
        [Parameter(Mandatory)][string]$OutDirectory,
        [Parameter(Mandatory)][string]$CheckpointName,
        [Parameter(Mandatory)][int]$ReadyTimeoutSeconds,
        [string]$BuildDirectory,
        [string]$Variant,
        [string]$ArtifactPath
    )
    $Artifact = Resolve-E5Artifact -Variant $Variant -ArtifactPath $ArtifactPath
    $Phases = [ordered]@{}
    foreach ($Vm in $VmName) { $Phases[$Vm] = @(Get-E5PhaseOrder -Phase $Phase) }
    $NeedsArtifact = @(@(Get-E5PhaseOrder -Phase $Phase) | Where-Object { @(Get-E5ArtifactPhases) -contains $_ })
    if ($NeedsArtifact.Count -gt 0 -and $null -eq $Artifact) { throw "Fazy $($NeedsArtifact -join ', ') wymagają -Variant SingleExe|Folder i -ArtifactPath (plik wydania z tools\release\Build-Release.ps1)." }
    return [ordered]@{ VmName = @($VmName); Phases = $Phases; OutDirectory = $OutDirectory; CheckpointName = $CheckpointName; ReadyTimeoutSeconds = $ReadyTimeoutSeconds
        BuildDirectory = $BuildDirectory; Artifact = $Artifact; Plan = (Get-E5Plan) }
}

function Get-E5PhaseError {
    # Pole error dokumentu fazy (odporne na brak pola w trybie StrictMode).
    [CmdletBinding()]
    param([AllowNull()]$Document)
    return (Get-E5Property -Object $Document -Name 'error')
}

function Test-E5SignaturesUpdated {
    # K1: sygnatury zaktualizowane tylko przy rekordzie fazy defender bez błędu i z data.SignaturesUpdated = true.
    [CmdletBinding()]
    [OutputType([bool])]
    param([AllowNull()]$Record)
    if ($null -eq $Record) { return $false }
    if (Get-E5Property -Object $Record -Name 'error') { return $false }
    return ((Get-E5Property -Object (Get-E5Property -Object $Record -Name 'data') -Name 'SignaturesUpdated') -eq $true)
}

function Get-E5DetectionProblems {
    # Odczyt detekcji jako warunek ważności: brak odczytu albo błędy odczytu = problemy (nigdy ciche „0 detekcji”). Czysta logika.
    [CmdletBinding()]
    [OutputType([string[]])]
    param(
        [AllowNull()]$Detections,
        [Parameter(Mandatory)][string]$Label
    )
    if ($null -eq $Detections) { return @("${Label}: brak odczytu detekcji") }
    return @(@(Get-E5Property -Object $Detections -Name 'Errors') | Where-Object { $_ } | ForEach-Object { "${Label}: odczyt detekcji: $_" })
}

function Get-E5AvSummary {
    # Podsumowanie fazy av (czysta logika): krok po kroku kod wyjścia i wynik z raportu, detekcje. Problemy (= kod 1 przebiegu): błąd kroku,
    # limit czasu, brak raportu --out, kod inny niż oczekiwany (np. funkcja zablokowana przez antywirus), brak kandydata AppX, okno WinUtil
    # nieżywe po 60 s albo bez okna WinUtil, GUI bez okna w 60 s, sonda nie jako dso, błąd odczytu detekcji albo kopii logów.
    [CmdletBinding()]
    param([AllowNull()]$Data)
    $Problems = New-Object System.Collections.Generic.List[string]
    $Steps = @()
    if ($null -eq $Data) { $Problems.Add('brak danych fazy av') }
    else {
        $Error0 = Get-E5Property -Object $Data -Name 'Error'
        if ($Error0) { $Problems.Add([string]$Error0) }
        foreach ($Step in @(Get-E5Property -Object $Data -Name 'Steps')) {
            if ($null -eq $Step) { continue }
            $Name = [string](Get-E5Property -Object $Step -Name 'Name')
            $Kind = [string](Get-E5Property -Object $Step -Name 'Kind')
            $Result = Get-E5Property -Object $Step -Name 'Result'
            $Extra = Get-E5Property -Object $Step -Name 'Extra'
            $Report = Get-E5Property -Object $Result -Name 'Report'
            $Outcome = Get-E5Property -Object $Report -Name 'outcome'
            if ($null -eq $Outcome) { $Outcome = Get-E5Property -Object $Report -Name 'result' }
            $Exit = Get-E5Property -Object $Result -Name 'ExitCode'
            $Expected = Get-E5Property -Object $Step -Name 'ExpectedExit'
            if ($null -eq $Expected) { $Expected = 0 }
            $StepError = Get-E5Property -Object $Step -Name 'Error'
            if ($StepError) { $Problems.Add("krok ${Name}: $StepError") }
            if ($Kind -ne 'gui' -and -not $StepError) {
                if ($null -eq $Result) { $Problems.Add("krok ${Name}: brak wyniku polecenia CLI") }
                else {
                    if (Get-E5Property -Object $Result -Name 'Error') { $Problems.Add("krok ${Name}: $(Get-E5Property -Object $Result -Name 'Error')") }
                    if ((Get-E5Property -Object $Result -Name 'TimedOut') -eq $true) { $Problems.Add("krok ${Name}: limit czasu CLI (program pracuje dalej)") }
                    elseif ($null -eq $Exit) { $Problems.Add("krok ${Name}: brak kodu wyjścia") }
                    elseif ([int]$Exit -ne [int]$Expected) { $Problems.Add("krok ${Name}: kod $Exit zamiast $Expected (wynik: $Outcome)") }
                    if (Get-E5Property -Object $Result -Name 'ReportError') { $Problems.Add("krok ${Name}: $(Get-E5Property -Object $Result -Name 'ReportError')") }
                }
            }
            $Shell = Get-E5Property -Object $Extra -Name 'Shell'
            if ($null -ne $Shell -and (Get-E5Property -Object $Shell -Name 'Ready') -ne $true) { $Problems.Add("krok ${Name}: powłoka dso nie wróciła po $(Get-E5Property -Object $Shell -Name 'Seconds') s") }
            $Launched = Get-E5Property -Object $Extra -Name 'Stop'
            if ($Kind -eq 'cli' -and $null -ne $Launched) { foreach ($Message in @(Get-E5Property -Object $Launched -Name 'Errors')) { if ($Message) { $Problems.Add("krok ${Name}: zamknięcie okna: $Message") } } }
            if ($Kind -eq 'winutil' -and $null -ne $Extra) {
                if (Get-E5Property -Object $Extra -Name 'Error') { $Problems.Add("krok ${Name}: $(Get-E5Property -Object $Extra -Name 'Error')") }
                if ((Get-E5Property -Object $Extra -Name 'AliveAfterWait') -ne $true) { $Problems.Add("krok ${Name}: okno WinUtil nie żyje po $(Get-E5Property -Object $Extra -Name 'WaitSeconds') s") }
                elseif ((Get-E5Property -Object $Extra -Name 'WinUtilVisible') -ne $true) { $Problems.Add("krok ${Name}: brak okna WinUtil w drzewie procesu") }
                if ($null -ne (Get-E5Property -Object $Extra -Name 'RootPid') -and (Get-E5Property -Object $Extra -Name 'RootFromDownloads') -ne $true) {
                    $Problems.Add("krok ${Name}: korzeń WinUtil znaleziony tylko po znaczniku awaryjnym '$(Get-E5Property -Object $Extra -Name 'RootMarker')' (nie plik z folderu pobrań) – brak pomiaru MotW pobranego winutil.ps1")
                }
                $ZoneError = Get-E5Property -Object $Extra -Name 'DownloadedZoneError'
                if ($ZoneError) { $Problems.Add("krok ${Name}: odczyt Zone.Identifier pobranego winutil.ps1: $ZoneError") }
                elseif ($null -eq (Get-E5Property -Object $Extra -Name 'DownloadedZone')) { $Problems.Add("krok ${Name}: nie ustalono pobranego winutil.ps1 – Zone.Identifier niezmierzony") }
                foreach ($File in @(Get-E5Property -Object $Extra -Name 'DownloadsFiles')) {
                    $FileError = Get-E5Property -Object $File -Name 'ZoneError'
                    if ($FileError) { $Problems.Add("krok ${Name}: odczyt Zone.Identifier pliku pobrań $(Get-E5Property -Object $File -Name 'Name'): $FileError") }
                }
                if (Get-E5Property -Object $Extra -Name 'ProbeError') { $Problems.Add("krok ${Name}: sonda okien: $(Get-E5Property -Object $Extra -Name 'ProbeError')") }
                elseif ((Get-E5Property -Object $Extra -Name 'ProbeAsDso') -ne $true) { $Problems.Add("krok ${Name}: sonda okien nie działała jako dso") }
                $Stop = Get-E5Property -Object $Extra -Name 'Stop'
                if (Get-E5Property -Object $Stop -Name 'Error') { $Problems.Add("krok ${Name}: zamknięcie: $(Get-E5Property -Object $Stop -Name 'Error')") }
                foreach ($Message in @(Get-E5DetectionProblems -Detections (Get-E5Property -Object $Stop -Name 'Detections') -Label "krok $Name")) { $Problems.Add($Message) }
            }
            if ($Kind -eq 'gui' -and -not $StepError) {
                $Start = Get-E5Property -Object $Extra -Name 'Start'
                if ($null -eq $Start) { $Problems.Add("krok ${Name}: brak danych startu GUI") }
                else {
                    if (Get-E5Property -Object $Start -Name 'Error') { $Problems.Add("krok ${Name}: $(Get-E5Property -Object $Start -Name 'Error')") }
                    if (Get-E5Property -Object $Start -Name 'ProbeError') { $Problems.Add("krok ${Name}: sonda okna: $(Get-E5Property -Object $Start -Name 'ProbeError')") }
                    elseif ((Get-E5Property -Object $Start -Name 'ProbeAsDso') -ne $true) { $Problems.Add("krok ${Name}: sonda okna nie działała jako dso") }
                    elseif ((Get-E5Property -Object $Start -Name 'Found') -ne $true) { $Problems.Add("krok ${Name}: okno główne nie pojawiło się w czasie") }
                }
                $Stop = Get-E5Property -Object $Extra -Name 'Stop'
                if (Get-E5Property -Object $Stop -Name 'Error') { $Problems.Add("krok ${Name}: zamknięcie: $(Get-E5Property -Object $Stop -Name 'Error')") }
            }
            $Title = if ($Kind -eq 'gui') { Get-E5Property -Object (Get-E5Property -Object $Extra -Name 'Start') -Name 'Title' } else { $null }
            $Steps += [pscustomobject]@{ Name = $Name; Kind = $Kind; ExitCode = $Exit; Outcome = $Outcome; Seconds = Get-E5Property -Object $Result -Name 'Seconds'; WindowTitle = $Title }
        }
        foreach ($Message in @(Get-E5DetectionProblems -Detections (Get-E5Property -Object $Data -Name 'Detections') -Label 'av')) { $Problems.Add($Message) }
    }
    $Detections = Get-E5Property -Object $Data -Name 'Detections'
    $Logs = Get-E5Property -Object $Data -Name 'Logs'
    return [pscustomobject]@{
        Valid        = ($Problems.Count -eq 0)
        Steps        = $Steps
        Candidate    = Get-E5Property -Object $Data -Name 'Candidate'
        ThreatCount  = Get-E5Property -Object $Detections -Name 'ThreatCount'
        EventCount   = Get-E5Property -Object $Detections -Name 'EventCount'
        Detected     = Get-E5Property -Object $Detections -Name 'Any'
        UsbLogExists = Get-E5Property -Object $Logs -Name 'UsbLogExists'
        Problems     = $Problems.ToArray()
    }
}

function Get-E5StartupSummary {
    # Podsumowanie fazy startup (czysta logika): czasy do okna głównego, pierwszy (zimny – pierwszy po starcie VM) i mediana.
    # Problemy: błąd fazy albo sondy, sonda nie jako dso, mniej przebiegów niż w planie, przebieg z błędem albo bez okna w limicie, detekcje.
    [CmdletBinding()]
    param(
        [AllowNull()]$Data,
        [Parameter(Mandatory)][int]$ExpectedRuns
    )
    $Problems = New-Object System.Collections.Generic.List[string]
    $Runs = @(@(Get-E5Property -Object $Data -Name 'Runs') | Where-Object { $null -ne $_ })
    if ($null -eq $Data) { $Problems.Add('brak danych fazy startup') }
    else {
        foreach ($Field in 'Error', 'ProbeError') { $Message = Get-E5Property -Object $Data -Name $Field; if ($Message) { $Problems.Add([string]$Message) } }
        if (-not (Get-E5Property -Object $Data -Name 'ProbeError') -and (Get-E5Property -Object $Data -Name 'ProbeAsDso') -ne $true) { $Problems.Add('sonda startu nie działała jako dso') }
        if ($Runs.Count -ne $ExpectedRuns) { $Problems.Add("przebiegów $($Runs.Count) zamiast $ExpectedRuns") }
        foreach ($Run in $Runs) {
            $Index = Get-E5Property -Object $Run -Name 'Index'
            if (Get-E5Property -Object $Run -Name 'Error') { $Problems.Add("start ${Index}: $(Get-E5Property -Object $Run -Name 'Error')") }
            elseif ((Get-E5Property -Object $Run -Name 'TimedOut') -eq $true -or $null -eq (Get-E5Property -Object $Run -Name 'WindowMs')) { $Problems.Add("start ${Index}: okno główne nie pojawiło się w limicie") }
        }
        $Leftover = Get-E5Property -Object $Data -Name 'Leftover'
        foreach ($Message in @(Get-E5Property -Object $Leftover -Name 'Errors')) { if ($Message) { $Problems.Add("sprzątanie: $Message") } }
        foreach ($Message in @(Get-E5DetectionProblems -Detections (Get-E5Property -Object $Data -Name 'Detections') -Label 'startup')) { $Problems.Add($Message) }
    }
    $Times = @($Runs | ForEach-Object { Get-E5Property -Object $_ -Name 'WindowMs' } | Where-Object { $null -ne $_ } | ForEach-Object { [int]$_ })
    $Median = $null
    if ($Times.Count -gt 0) {
        $Sorted = @($Times | Sort-Object)
        $Middle = [int][math]::Floor($Sorted.Count / 2)
        $Median = if ($Sorted.Count % 2 -eq 1) { [double]$Sorted[$Middle] } else { ([double]$Sorted[$Middle - 1] + [double]$Sorted[$Middle]) / 2 }
    }
    $First = @($Runs | Where-Object { [int](Get-E5Property -Object $_ -Name 'Index') -eq 1 } | ForEach-Object { Get-E5Property -Object $_ -Name 'WindowMs' }) | Select-Object -First 1
    $Detections = Get-E5Property -Object $Data -Name 'Detections'
    return [pscustomobject]@{
        Valid         = ($Problems.Count -eq 0)
        WindowMs      = $Times
        FirstMs       = $First
        MedianMs      = $Median
        WindowTitles  = @($Runs | ForEach-Object { Get-E5Property -Object $_ -Name 'Title' } | Where-Object { $_ } | Select-Object -Unique)
        FirstSeconds  = $(if ($null -ne $First) { [math]::Round([double]$First / 1000, 2) } else { $null })
        MedianSeconds = $(if ($null -ne $Median) { [math]::Round($Median / 1000, 2) } else { $null })
        ThreatCount   = Get-E5Property -Object $Detections -Name 'ThreatCount'
        EventCount    = Get-E5Property -Object $Detections -Name 'EventCount'
        Detected      = Get-E5Property -Object $Detections -Name 'Any'
        Problems      = $Problems.ToArray()
    }
}

function Get-E5MotwSummary {
    # Podsumowanie fazy motw z wpisu {start, screenshot, screenshotError, stop} (czysta logika): co się pokazało (SmartScreen/UAC/Aplikacja),
    # MotW pliku na pulpicie i pliku uruchomionego (po wypakowaniu), zrzut. Problemy: błędy części, brak startu, ZoneId inne niż w planie,
    # brak zrzutu, sonda nie jako dso, detekcje.
    [CmdletBinding()]
    param(
        [Parameter(Mandatory)]$Entry,
        [Parameter(Mandatory)][int]$ZoneId
    )
    $Problems = New-Object System.Collections.Generic.List[string]
    $StartDocument = Get-E5Property -Object $Entry -Name 'start'
    $StopDocument = Get-E5Property -Object $Entry -Name 'stop'
    $Start = Get-E5Property -Object $StartDocument -Name 'data'
    $Stop = Get-E5Property -Object $StopDocument -Name 'data'
    foreach ($Message in @((Get-E5Property -Object $StartDocument -Name 'error'), (Get-E5Property -Object $Start -Name 'Error'), (Get-E5Property -Object $Entry -Name 'screenshotError'),
            (Get-E5Property -Object $StopDocument -Name 'error'), (Get-E5Property -Object $Stop -Name 'Error')) | Where-Object { $_ }) { $Problems.Add([string]$Message) }
    if ($null -eq $Start) { $Problems.Add('brak danych startu motw') }
    elseif ((Get-E5Property -Object $Start -Name 'Launched') -ne $true -and -not (Get-E5Property -Object $Start -Name 'Error')) { $Problems.Add('program nie został uruchomiony przez powłokę') }
    $CopiedZone = Get-E5Property -Object (Get-E5Property -Object $Start -Name 'CopiedZone') -Name 'ZoneId'
    if ($null -ne $Start -and $null -ne $CopiedZone -and [int]$CopiedZone -ne $ZoneId) { $Problems.Add("Zone.Identifier pliku na pulpicie: ZoneId=$CopiedZone zamiast $ZoneId") }
    if ($null -eq $Stop) { $Problems.Add('brak danych zamknięcia motw') }
    else {
        if (Get-E5Property -Object $Stop -Name 'ProbeError') { $Problems.Add("sonda okien: $(Get-E5Property -Object $Stop -Name 'ProbeError')") }
        elseif ((Get-E5Property -Object $Stop -Name 'ProbeAsDso') -ne $true) { $Problems.Add('sonda okien nie działała jako dso') }
        foreach ($Message in @(Get-E5DetectionProblems -Detections (Get-E5Property -Object $Stop -Name 'Detections') -Label 'motw')) { $Problems.Add($Message) }
        # Program wymaga administratora: udany start przez powłokę daje co najmniej UAC, SmartScreen albo proces programu.
        if (@(Get-E5Property -Object $Stop -Name 'Shown') -contains 'nic') { $Problems.Add('start przez powłokę nie pokazał ani SmartScreen, ani UAC, ani programu (start albo odczyt nie zadziałał)') }
    }
    $Detections = Get-E5Property -Object $Stop -Name 'Detections'
    $LaunchZone = Get-E5Property -Object $Start -Name 'LaunchZone'
    if ((Get-E5Property -Object $Start -Name 'Launched') -eq $true -and $null -eq $LaunchZone) { $Problems.Add('brak odczytu Zone.Identifier pliku uruchomionego') }
    return [pscustomobject]@{
        Valid             = ($Problems.Count -eq 0)
        Variant           = Get-E5Property -Object $Start -Name 'Variant'
        Shown             = @(Get-E5Property -Object $Stop -Name 'Shown')
        SmartScreenEvidence = @(Get-E5Property -Object $Stop -Name 'SmartScreenEvidence')
        LaunchPath        = Get-E5Property -Object $Start -Name 'LaunchPath'
        LaunchZoneId      = Get-E5Property -Object $LaunchZone -Name 'ZoneId'
        LaunchHasMotw     = Get-E5Property -Object $LaunchZone -Name 'Exists'
        Screenshot        = Get-E5Property -Object $Entry -Name 'screenshot'
        ThreatCount       = Get-E5Property -Object $Detections -Name 'ThreatCount'
        EventCount        = Get-E5Property -Object $Detections -Name 'EventCount'
        Detected          = Get-E5Property -Object $Detections -Name 'Any'
        Problems          = $Problems.ToArray()
    }
}

function Get-E5DpiSummary {
    # Podsumowanie fazy dpi z rekordu {set, restartError, start, screenshot, screenshotError, stop} (czysta logika). Status 'zweryfikowane',
    # gdy okno główne pojawiło się przy skutecznym DPI = Plan.DpiLogPixels; inaczej 'niezweryfikowane' z powodem (K7). Skala, która się nie
    # przyjęła (DPI inne niż w planie), to wynik, nie awaria; awarie (zapis, restart, sonda, zrzut, brak okna, detekcje) to problemy.
    [CmdletBinding()]
    param(
        [Parameter(Mandatory)][System.Collections.IDictionary]$Record,
        [Parameter(Mandatory)][int]$LogPixels
    )
    $Problems = New-Object System.Collections.Generic.List[string]
    $Reasons = New-Object System.Collections.Generic.List[string]
    $SetDocument = Get-E5Property -Object $Record -Name 'set'
    $StartDocument = Get-E5Property -Object $Record -Name 'start'
    $StopDocument = Get-E5Property -Object $Record -Name 'stop'
    $Start = Get-E5Property -Object $StartDocument -Name 'data'
    $Gui = Get-E5Property -Object $Start -Name 'Gui'
    $Stop = Get-E5Property -Object $StopDocument -Name 'data'
    foreach ($Message in @((Get-E5Property -Object $SetDocument -Name 'error'), (Get-E5Property -Object (Get-E5Property -Object $SetDocument -Name 'data') -Name 'Error'),
            (Get-E5Property -Object $Record -Name 'restartError'), (Get-E5Property -Object $StartDocument -Name 'error'), (Get-E5Property -Object $Start -Name 'Error'),
            (Get-E5Property -Object $Gui -Name 'Error'), (Get-E5Property -Object $Gui -Name 'ProbeError'), (Get-E5Property -Object $Record -Name 'screenshotError'),
            (Get-E5Property -Object $StopDocument -Name 'error'), (Get-E5Property -Object $Stop -Name 'Error')) | Where-Object { $_ }) {
        $Problems.Add([string]$Message)
        $Reasons.Add([string]$Message)
    }
    $Dpi = Get-E5Property -Object $Gui -Name 'Dpi'
    if ($null -eq $SetDocument) { $Problems.Add('brak zapisu skali'); $Reasons.Add('skala nie została ustawiona') }
    elseif ($null -eq $Gui) { if ($Reasons.Count -eq 0) { $Problems.Add('brak danych okna po restarcie'); $Reasons.Add('brak danych okna po restarcie') } }
    else {
        if (-not (Get-E5Property -Object $Gui -Name 'ProbeError') -and (Get-E5Property -Object $Gui -Name 'ProbeAsDso') -ne $true) { $Problems.Add('sonda okna nie działała jako dso'); $Reasons.Add('sonda okna nie działała jako dso') }
        if ($null -ne $Dpi -and [int]$Dpi -ne $LogPixels) { $Reasons.Add("skala nie przyjęła się po ponownym zalogowaniu (DPI ekranu $Dpi zamiast $LogPixels, LogPixels konta: $(Get-E5Property -Object $Gui -Name 'LogPixels'))") }
        elseif ($null -eq $Dpi -and -not (Get-E5Property -Object $Gui -Name 'ProbeError')) { $Reasons.Add('sonda nie odczytała DPI ekranu') }
        if ((Get-E5Property -Object $Gui -Name 'Found') -ne $true) { $Problems.Add('okno główne nie pojawiło się przy 150%'); $Reasons.Add('okno główne nie pojawiło się') }
    }
    if ($null -ne $Stop) { foreach ($Message in @(Get-E5DetectionProblems -Detections (Get-E5Property -Object $Stop -Name 'Detections') -Label 'dpi')) { $Problems.Add($Message) } }
    elseif ($null -ne $Gui) { $Problems.Add('brak danych zamknięcia dpi') }
    $Detections = Get-E5Property -Object $Stop -Name 'Detections'
    return [pscustomobject]@{
        Valid       = ($Problems.Count -eq 0)
        Status      = $(if ($Reasons.Count -eq 0) { 'zweryfikowane' } else { 'niezweryfikowane' })
        Reason      = $(if ($Reasons.Count -gt 0) { $Reasons -join '; ' } else { $null })
        Dpi         = $Dpi
        WindowTitle = Get-E5Property -Object $Gui -Name 'Title'
        Bounds      = Get-E5Property -Object $Gui -Name 'Bounds'
        Screen      = $(if ($null -ne $Gui) { '{0}x{1}' -f (Get-E5Property -Object $Gui -Name 'ScreenWidth'), (Get-E5Property -Object $Gui -Name 'ScreenHeight') } else { $null })
        Screenshot  = Get-E5Property -Object $Record -Name 'screenshot'
        ThreatCount = Get-E5Property -Object $Detections -Name 'ThreatCount'
        EventCount  = Get-E5Property -Object $Detections -Name 'EventCount'
        Detected    = Get-E5Property -Object $Detections -Name 'Any'
        Problems    = $Problems.ToArray()
    }
}

function Get-E5VariantSummary {
    # Podsumowanie jednego wariantu WinUtil z wpisu {start, screenshot, screenshotError, stop} (czysta logika).
    # Valid = pomiar się liczy: bez błędów startu, zrzutu i stopu, detekcje odczytane bez błędu, a przy starcie okna sonda okien
    # działała jako dso bez błędu. Blokada pliku przez Defendera albo SHA256 ≠ digest (Blocked) to WYNIK pomiaru, nie awaria.
    # Errors = powody nieważności; „0 detekcji” albo „brak okna” bez błędów odczytu = prawdziwy wynik.
    [CmdletBinding()]
    param(
        [Parameter(Mandatory)][string]$Variant,
        [Parameter(Mandatory)]$Entry
    )
    $StartDocument = Get-E5Property -Object $Entry -Name 'start'
    $StopDocument = Get-E5Property -Object $Entry -Name 'stop'
    $Start = Get-E5Property -Object $StartDocument -Name 'data'
    $Stop = Get-E5Property -Object $StopDocument -Name 'data'
    $Started = ((Get-E5Property -Object $Start -Name 'Started') -eq $true)
    $Blocked = Get-E5Property -Object $Start -Name 'Blocked'
    $Detections = Get-E5Property -Object $Stop -Name 'Detections'
    $DetectionErrors = @(@(Get-E5Property -Object $Detections -Name 'Errors') | Where-Object { $_ })
    $Errors = New-Object System.Collections.Generic.List[string]
    foreach ($Message in @(
            (Get-E5Property -Object $StartDocument -Name 'error'), (Get-E5Property -Object $Start -Name 'Error'),
            (Get-E5Property -Object $Entry -Name 'screenshotError'),
            (Get-E5Property -Object $StopDocument -Name 'error'), (Get-E5Property -Object $Stop -Name 'Error')
        ) | Where-Object { $_ }) { $Errors.Add([string]$Message) }
    if ($null -eq $Start) { $Errors.Add('brak danych startu wariantu') }
    elseif (-not $Started -and -not $Blocked -and -not (Get-E5Property -Object $Start -Name 'Error')) { $Errors.Add('okno nie wystartowało, a brak blokady i błędu') }
    if ($null -eq $Detections) { $Errors.Add('brak odczytu detekcji') }
    foreach ($Message in $DetectionErrors) { $Errors.Add("odczyt detekcji: $Message") }
    if ($Started) {
        $ProbeError = Get-E5Property -Object $Start -Name 'ProbeError'
        if ($ProbeError) { $Errors.Add("sonda okien: $ProbeError") }
        if ((Get-E5Property -Object $Start -Name 'ProbeAsDso') -ne $true) { $Errors.Add("sonda okien nie działała jako dso (SID sondy: '$(Get-E5Property -Object $Start -Name 'ProbeSid')')") }
    }
    return [pscustomobject]@{
        Variant            = $Variant
        Valid              = ($Errors.Count -eq 0)
        Started            = $Started
        Blocked            = $Blocked
        AliveAfterWait     = ((Get-E5Property -Object $Start -Name 'AliveAfterWait') -eq $true)
        WinUtilVisible     = ((Get-E5Property -Object $Start -Name 'WinUtilVisible') -eq $true)
        WinUtilWindowTitle = Get-E5Property -Object $Start -Name 'WinUtilWindowTitle'
        ProbeAsDso         = Get-E5Property -Object $Start -Name 'ProbeAsDso'
        ThreatCount        = Get-E5Property -Object $Detections -Name 'ThreatCount'
        EventCount         = Get-E5Property -Object $Detections -Name 'EventCount'
        Detected           = Get-E5Property -Object $Detections -Name 'Any'
        Screenshot         = Get-E5Property -Object $Entry -Name 'screenshot'
        Errors             = $Errors.ToArray()
    }
}

function Invoke-E5VmPhase {
    # Jedna faza (krok) w VM przez otwartą sesję: definicje + Invoke-E5Collection; zwraca sparsowany dokument.
    [CmdletBinding()]
    param(
        [Parameter(Mandatory)][AllowNull()][System.Management.Automation.Runspaces.PSSession]$Session,
        [Parameter(Mandatory)][string]$RemoteScript,
        [Parameter(Mandatory)][string]$PlanJson,
        [Parameter(Mandatory)][string]$Phase,
        [Parameter(Mandatory)][string]$UserSid,
        [string]$ArgumentJson = '{}'
    )
    Assert-DsoVmLiveAllowed
    $Remote = {
        param([string]$Definitions, [string]$Json, [string]$Which, [string]$Sid, [string]$ArgumentsJson)
        . ([scriptblock]::Create($Definitions))
        Invoke-E5Collection -PlanJson $Json -Phase $Which -UserSid $Sid -ArgumentJson $ArgumentsJson
    }
    $Raw = @(Invoke-Command -Session $Session -ScriptBlock $Remote -ArgumentList @($RemoteScript, $PlanJson, $Phase, $UserSid, $ArgumentJson))
    $Text = [string]($Raw | Where-Object { $_ -is [string] } | Select-Object -Last 1)
    if ([string]::IsNullOrWhiteSpace($Text)) { throw "Faza '$Phase' nie zwróciła JSON." }
    return ($Text | ConvertFrom-Json)
}

function Invoke-E5WinUtilPhase {
    # Faza winutil (host): dla każdego wariantu z planu start w VM -> zrzut VM z hosta -> stop w VM (detekcje, zamknięcie drzewa).
    # Rekord wypełniany na bieżąco, więc przy wyjątku (np. zerwana sesja) wynik częściowy zostaje. Błąd zrzutu nie przerywa wariantu.
    [CmdletBinding()]
    param(
        [Parameter(Mandatory)][System.Collections.IDictionary]$Context,
        [Parameter(Mandatory)][System.Collections.IDictionary]$Record
    )
    $Plan = $Context.PlanJson | ConvertFrom-Json
    $Variants = [ordered]@{}
    $Record.variants = $Variants
    foreach ($Variant in @($Plan.Variants)) {
        $Entry = [ordered]@{ start = $null; screenshot = $null; screenshotError = $null; stop = $null }
        $Variants[[string]$Variant] = $Entry
        Write-Host "$(Get-Date -Format 'HH:mm:ss') $($Context.VmName) winutil $Variant"
        $Entry.start = Invoke-E5VmPhase -Session $Context.Session -RemoteScript $Context.RemoteScript -PlanJson $Context.PlanJson -Phase 'winutil-start' -UserSid $Context.UserSid `
            -ArgumentJson (ConvertTo-Json -InputObject ([ordered]@{ Variant = [string]$Variant }) -Compress)
        try {
            $null = New-Item -ItemType Directory -Path $Context.VmDirectory -Force
            $Entry.screenshot = Save-DsoVmThumbnail -VmName $Context.VmName -Path (Join-Path $Context.VmDirectory "winutil-$Variant.png")
        }
        catch { $Entry.screenshotError = $_.Exception.Message }
        $Started = Get-E5Property -Object $Entry.start -Name 'data'
        $State = [ordered]@{
            Variant = [string]$Variant; Since = Get-E5Property -Object $Started -Name 'Since'; RootPid = Get-E5Property -Object $Started -Name 'RootPid'
            TaskName = Get-E5Property -Object $Started -Name 'TaskName'
        }
        $Entry.stop = Invoke-E5VmPhase -Session $Context.Session -RemoteScript $Context.RemoteScript -PlanJson $Context.PlanJson -Phase 'winutil-stop' -UserSid $Context.UserSid `
            -ArgumentJson (ConvertTo-Json -InputObject $State -Compress)
    }
    $Record.summary = @(foreach ($Key in $Variants.Keys) { Get-E5VariantSummary -Variant $Key -Entry $Variants[$Key] })
    $DocumentErrors = @(foreach ($Key in $Variants.Keys) {
            foreach ($Part in 'start', 'stop') { $Message = Get-E5PhaseError -Document $Variants[$Key][$Part]; if ($Message) { "$Key ${Part}: $Message" } }
        })
    $Record.error = if ($DocumentErrors.Count -gt 0) { $DocumentErrors -join ' | ' } else { $null }
    # Nieważny wariant (awaria sieci, sondy, odczytu detekcji…) = problem fazy, więc przebieg kończy się kodem 1; wynik zostaje w rekordzie.
    $Invalid = @($Record.summary | Where-Object { -not $_.Valid })
    $Record.invalidVariants = @($Invalid | ForEach-Object { [string]$_.Variant })
    $Record.problems = @($Invalid | ForEach-Object { "wariant $($_.Variant) nieważny: $(@($_.Errors) -join '; ')" })
}

function Save-E5Screenshot {
    # Zrzut ekranu VM z hosta (GetVirtualSystemThumbnailImage) do <VmDirectory>\<FileName>; zwraca {Path, Error} – błąd nie przerywa fazy.
    [CmdletBinding()]
    param(
        [Parameter(Mandatory)][System.Collections.IDictionary]$Context,
        [Parameter(Mandatory)][ValidatePattern('^[A-Za-z0-9-]+\.png\z')][string]$FileName
    )
    try {
        $null = New-Item -ItemType Directory -Path $Context.VmDirectory -Force
        return [pscustomobject]@{ Path = (Save-DsoVmThumbnail -VmName $Context.VmName -Path (Join-Path $Context.VmDirectory $FileName)); Error = $null }
    }
    catch { return [pscustomobject]@{ Path = $null; Error = $_.Exception.Message } }
}

function Invoke-E5MotwPhase {
    # Faza motw (host): część startowa w VM (kopia z ZoneId=3, [wypakowanie], start przez powłokę) -> MotwWaitSeconds -> zrzut VM
    # (okno SmartScreen albo UAC/aplikacji) -> część końcowa w VM (okna, procesy, klasyfikacja, zamknięcie, detekcje).
    [CmdletBinding()]
    param(
        [Parameter(Mandatory)][System.Collections.IDictionary]$Context,
        [Parameter(Mandatory)][System.Collections.IDictionary]$Record
    )
    $Plan = $Context.PlanJson | ConvertFrom-Json
    $Record.start = $null; $Record.screenshot = $null; $Record.screenshotError = $null; $Record.stop = $null
    $Record.start = Invoke-E5VmPhase -Session $Context.Session -RemoteScript $Context.RemoteScript -PlanJson $Context.PlanJson -Phase 'motw-start' -UserSid $Context.UserSid
    Start-Sleep -Seconds ([int]$Plan.MotwWaitSeconds)
    $Shot = Save-E5Screenshot -Context $Context -FileName 'motw.png'
    $Record.screenshot = $Shot.Path
    $Record.screenshotError = $Shot.Error
    $Started = Get-E5Property -Object $Record.start -Name 'data'
    $State = [ordered]@{ Since = Get-E5Property -Object $Started -Name 'Since'; LaunchPath = Get-E5Property -Object $Started -Name 'LaunchPath'; TaskName = Get-E5Property -Object $Started -Name 'TaskName' }
    $Record.stop = Invoke-E5VmPhase -Session $Context.Session -RemoteScript $Context.RemoteScript -PlanJson $Context.PlanJson -Phase 'motw-stop' -UserSid $Context.UserSid `
        -ArgumentJson (ConvertTo-Json -InputObject $State -Compress)
    $Record.summary = Get-E5MotwSummary -Entry $Record -ZoneId ([int]$Plan.MotwZoneId)
    $Errors = @(foreach ($Part in 'start', 'stop') { $Message = Get-E5PhaseError -Document $Record[$Part]; if ($Message) { "${Part}: $Message" } })
    $Record.error = if ($Errors.Count -gt 0) { $Errors -join ' | ' } else { $null }
    $Record.problems = @($Record.summary.Problems)
}

function Restart-E5VmAndWait {
    # Restart VM z jej wnętrza (Restart-E5Vm za bezpiecznikiem), zgaśnięcie heartbeatu, nowa sesja po gotowości (autologon dso).
    [CmdletBinding()]
    param(
        [Parameter(Mandatory)][AllowNull()][System.Management.Automation.Runspaces.PSSession]$Session,
        [Parameter(Mandatory)][ValidateSet('DSO-26H2-Home', 'DSO-26H2-Pro')][string]$VmName,
        [Parameter(Mandatory)][pscredential]$Credential,
        [Parameter(Mandatory)][string]$RemoteScript,
        [int]$ReadyTimeoutSeconds = 1200
    )
    Assert-DsoVmLiveAllowed
    $Remote = {
        param([string]$Definitions)
        . ([scriptblock]::Create($Definitions))
        Restart-E5Vm
    }
    # Restart zrywa sesję: błąd wywołania jest spodziewany (zgaśnięcie heartbeatu i tak sprawdzamy).
    try { $null = Invoke-Command -Session $Session -ScriptBlock $Remote -ArgumentList @($RemoteScript) } catch { $null = $_ }
    if ($null -ne $Session) { Remove-PSSession -Session $Session -ErrorAction SilentlyContinue }
    $null = Wait-VmHeartbeatDrop -VmName $VmName
    return (Wait-VmReady -VmName $VmName -Credential $Credential -TimeoutSeconds $ReadyTimeoutSeconds)
}

function Invoke-E5DpiPhase {
    # Faza dpi (host, bez sieci): LogPixels = 144 dla dso w VM -> restart VM (nowe logowanie dso) -> GUI z nośnika i okno główne -> zrzut
    # -> zamknięcie i detekcje. Sesja po restarcie trafia do Context.Session. Wynik: Status 'zweryfikowane'/'niezweryfikowane' z powodem (K7).
    [CmdletBinding()]
    param(
        [Parameter(Mandatory)][System.Collections.IDictionary]$Context,
        [Parameter(Mandatory)][System.Collections.IDictionary]$Record
    )
    $Plan = $Context.PlanJson | ConvertFrom-Json
    $Record.set = $null; $Record.restartError = $null; $Record.start = $null; $Record.screenshot = $null; $Record.screenshotError = $null; $Record.stop = $null
    $Record.set = Invoke-E5VmPhase -Session $Context.Session -RemoteScript $Context.RemoteScript -PlanJson $Context.PlanJson -Phase 'dpi-set' -UserSid $Context.UserSid
    $SetOk = -not (Get-E5PhaseError -Document $Record.set) -and -not (Get-E5Property -Object (Get-E5Property -Object $Record.set -Name 'data') -Name 'Error')
    if ($SetOk) {
        $Restarted = $false
        try {
            $Context.Session = Restart-E5VmAndWait -Session $Context.Session -VmName $Context.VmName -Credential $Context.Credential -RemoteScript $Context.RemoteScript -ReadyTimeoutSeconds $Context.ReadyTimeoutSeconds
            # Chwila na dokończenie logowania dso po restarcie (jak po pierwszym starcie).
            Start-Sleep -Seconds 45
            $Restarted = $true
        }
        catch {
            if (Test-VmAuthFailure -Exception $_.Exception) { throw }
            $Record.restartError = "restart VM: $($_.Exception.Message)"
        }
        if ($Restarted) {
            $Record.start = Invoke-E5VmPhase -Session $Context.Session -RemoteScript $Context.RemoteScript -PlanJson $Context.PlanJson -Phase 'dpi-start' -UserSid $Context.UserSid
            $Shot = Save-E5Screenshot -Context $Context -FileName "dpi-$([int]$Plan.DpiScalePercent).png"
            $Record.screenshot = $Shot.Path
            $Record.screenshotError = $Shot.Error
            $Started = Get-E5Property -Object $Record.start -Name 'data'
            $State = [ordered]@{ Since = Get-E5Property -Object $Started -Name 'Since'; Gui = Get-E5Property -Object $Started -Name 'Gui' }
            $Record.stop = Invoke-E5VmPhase -Session $Context.Session -RemoteScript $Context.RemoteScript -PlanJson $Context.PlanJson -Phase 'dpi-stop' -UserSid $Context.UserSid `
                -ArgumentJson (ConvertTo-Json -InputObject $State -Depth 6 -Compress)
        }
    }
    $Record.summary = Get-E5DpiSummary -Record $Record -LogPixels ([int]$Plan.DpiLogPixels)
    $Errors = @(foreach ($Part in 'set', 'start', 'stop') { $Message = Get-E5PhaseError -Document $Record[$Part]; if ($Message) { "${Part}: $Message" } })
    $Record.error = if ($Errors.Count -gt 0) { $Errors -join ' | ' } else { $null }
    $Record.problems = @($Record.summary.Problems)
}

function Invoke-E5HostPhase {
    # Jedna faza przebiegu (host) – po jednej gałęzi na fazę. Wynik w rekordzie {phase, error, problems, ...}:
    # error = błąd dokumentu z VM, problems = awarie infrastruktury w danych (np. odczyt detekcji, nieważny wariant) – obie dają kod 1.
    [CmdletBinding()]
    param(
        [Parameter(Mandatory)][string]$Phase,
        [Parameter(Mandatory)][System.Collections.IDictionary]$Context,
        [Parameter(Mandatory)][System.Collections.IDictionary]$Record
    )
    switch ($Phase) {
        'defender' {
            $Document = Invoke-E5VmPhase -Session $Context.Session -RemoteScript $Context.RemoteScript -PlanJson $Context.PlanJson -Phase 'defender' -UserSid $Context.UserSid
            $Record.error = Get-E5PhaseError -Document $Document
            $Record.meta = Get-E5Property -Object $Document -Name 'meta'
            $Record.data = Get-E5Property -Object $Document -Name 'data'
            $DetectionErrors = @(@(Get-E5Property -Object (Get-E5Property -Object $Record.data -Name 'Detections') -Name 'Errors') | Where-Object { $_ })
            $Record.problems = @($DetectionErrors | ForEach-Object { "odczyt detekcji: $_" })
        }
        'winutil' { Invoke-E5WinUtilPhase -Context $Context -Record $Record }
        { $_ -in 'startup', 'av' } {
            $Document = Invoke-E5VmPhase -Session $Context.Session -RemoteScript $Context.RemoteScript -PlanJson $Context.PlanJson -Phase $Phase -UserSid $Context.UserSid
            $Record.error = Get-E5PhaseError -Document $Document
            $Record.meta = Get-E5Property -Object $Document -Name 'meta'
            $Record.data = Get-E5Property -Object $Document -Name 'data'
            $Plan = $Context.PlanJson | ConvertFrom-Json
            $Record.summary = if ($Phase -eq 'startup') { Get-E5StartupSummary -Data $Record.data -ExpectedRuns ([int]$Plan.StartupRuns) } else { Get-E5AvSummary -Data $Record.data }
            $Record.problems = @($Record.summary.Problems)
        }
        'motw' { Invoke-E5MotwPhase -Context $Context -Record $Record }
        'dpi' { Invoke-E5DpiPhase -Context $Context -Record $Record }
        default { throw "Nieznana faza '$Phase'." }
    }
}

function Assert-E5UsbHostDisk {
    # Bezpiecznik przed Initialize-Disk/Format-Volume na hoście: dysk MUSI być świeżo podłączonym VHDX z tej ścieżki (BusType
    # 'File Backed Virtual', Location = ścieżka VHDX, RAW, nie rozruchowy i nie systemowy, rozmiar jak przy tworzeniu). Inaczej wyjątek.
    [CmdletBinding()]
    param(
        [Parameter(Mandatory)]$Disk,
        [Parameter(Mandatory)][string]$VhdPath,
        [Parameter(Mandatory)][int64]$SizeBytes
    )
    $Problems = @()
    if ([string]$Disk.BusType -ne 'File Backed Virtual') { $Problems += "BusType '$($Disk.BusType)'" }
    if ([string]$Disk.Location -ine $VhdPath) { $Problems += "Location '$($Disk.Location)'" }
    if ([string]$Disk.PartitionStyle -ne 'RAW') { $Problems += "PartitionStyle '$($Disk.PartitionStyle)'" }
    if ($Disk.IsBoot -or $Disk.IsSystem) { $Problems += 'dysk rozruchowy albo systemowy' }
    if ([int64]$Disk.Size -ne $SizeBytes) { $Problems += "rozmiar $($Disk.Size) zamiast $SizeBytes" }
    if ($Problems.Count -gt 0) { throw "Dysk $($Disk.Number) to nie świeży VHDX '$VhdPath' ($($Problems -join '; ')); nic nie zostało sformatowane." }
}

function Copy-E5ArtifactToUsb {
    # Zawartość nośnika (host, katalog główny podłączonego VHDX): '<Root>\Narzędzia DSO\' z programem (SingleExe – plik wydania pod własną
    # nazwą, Folder – zip wypakowany: DSOptimizer\DSOptimizer.exe + DLL) i '<Root>\wydanie\<plik wydania>' (źródło fazy motw).
    # Zwraca listę plików (ścieżka względna, rozmiar).
    [CmdletBinding()]
    param(
        [Parameter(Mandatory)][string]$Root,
        [Parameter(Mandatory)]$Artifact,
        [Parameter(Mandatory)]$Plan
    )
    $Folder = Join-Path $Root ([string]$Plan.UsbFolderName)
    $Release = Join-Path $Root ([string]$Plan.UsbReleaseFolder)
    $null = New-Item -ItemType Directory -Path $Folder -Force
    $null = New-Item -ItemType Directory -Path $Release -Force
    Copy-Item -LiteralPath ([string]$Artifact.Path) -Destination (Join-Path $Release ([string]$Artifact.Name)) -Force
    if ([string]$Artifact.Variant -eq 'SingleExe') { Copy-Item -LiteralPath ([string]$Artifact.Path) -Destination (Join-Path $Folder ([string]$Artifact.Name)) -Force }
    else { Expand-Archive -LiteralPath ([string]$Artifact.Path) -DestinationPath $Folder -Force }
    $Exe = Join-Path $Folder ([string]$Artifact.ExeRelativePath)
    if (-not [System.IO.File]::Exists($Exe)) { throw "Po skopiowaniu na nośnik brak '$Exe'." }
    $Base = $Root.TrimEnd('\').Length + 1
    return @(Get-ChildItem -LiteralPath $Root -Recurse -File -Force -ErrorAction Stop | Where-Object { $_.FullName -notmatch '\\System Volume Information\\' } |
            ForEach-Object { [pscustomobject]@{ Path = $_.FullName.Substring($Base); Length = $_.Length } })
}

function New-E5UsbDisk {
    # Nośnik z wydaniem (host): nowy VHDX (dynamiczny) -> Mount-VHD -> bezpiecznik dysku (Assert-E5UsbHostDisk) -> MBR, jedna partycja,
    # exFAT z etykietą Plan.UsbVolumeLabel -> zawartość (Copy-E5ArtifactToUsb) -> ZAWSZE Dismount-VHD. Plik VHDX zostaje w katalogu wyników
    # (dowód: po przebiegu ma też logs\<PC>\ zapisane przez program obok exe).
    [CmdletBinding()]
    param(
        [Parameter(Mandatory)][ValidatePattern('\.vhdx\z')][string]$VhdPath,
        [Parameter(Mandatory)]$Artifact,
        [Parameter(Mandatory)]$Plan
    )
    Assert-DsoVmLiveAllowed
    if (Test-Path -LiteralPath $VhdPath) { throw "VHDX '$VhdPath' już istnieje; nie nadpisuję." }
    $Out = [ordered]@{ Path = $VhdPath; Variant = [string]$Artifact.Variant; Files = @(); ExeRelativePath = [string]$Artifact.ExeRelativePath; Folder = [string]$Plan.UsbFolderName }
    $null = New-Item -ItemType Directory -Path ([System.IO.Path]::GetDirectoryName($VhdPath)) -Force
    $null = New-VHD -Path $VhdPath -SizeBytes ([int64]$Plan.UsbSizeBytes) -Dynamic -ErrorAction Stop
    $Mounted = $false
    $Primary = $null
    try {
        $Vhd = Mount-VHD -Path $VhdPath -Passthru -ErrorAction Stop
        $Mounted = $true
        # Bez numeru dysku z Mount-VHD nie pytamy Get-Disk wcale ([int]$null dałoby dysk 0 – systemowy).
        if ($null -eq $Vhd -or $null -eq $Vhd.DiskNumber -or [string]$Vhd.DiskNumber -eq '') { throw "Mount-VHD nie zwrócił numeru dysku dla '$VhdPath'; nic nie zostało sformatowane." }
        $Disk = Get-Disk -Number ([int]$Vhd.DiskNumber) -ErrorAction Stop
        Assert-E5UsbHostDisk -Disk $Disk -VhdPath $VhdPath -SizeBytes ([int64]$Plan.UsbSizeBytes)
        # Dalej wyłącznie obiekt dysku, który przeszedł bezpiecznik (potok), a nie ponownie numer.
        $null = $Disk | Initialize-Disk -PartitionStyle MBR -ErrorAction Stop
        $Partition = $Disk | New-Partition -UseMaximumSize -AssignDriveLetter -ErrorAction Stop
        $null = Format-Volume -Partition $Partition -FileSystem ([string]$Plan.UsbFileSystem) -NewFileSystemLabel ([string]$Plan.UsbVolumeLabel) -Confirm:$false -Force -ErrorAction Stop
        $Letter = [string](Get-Partition -DiskNumber ([int]$Disk.Number) -PartitionNumber ([int]$Partition.PartitionNumber) -ErrorAction Stop).DriveLetter
        if ($Letter -notmatch '^[A-Za-z]\z') { throw "Partycja VHDX '$VhdPath' nie dostała litery na hoście." }
        $Out.Files = @(Copy-E5ArtifactToUsb -Root "${Letter}:\" -Artifact $Artifact -Plan $Plan)
    }
    catch {
        $Primary = $_
        throw
    }
    finally {
        if ($Mounted) {
            # Błąd odłączenia nie może przykryć pierwotnego wyjątku (np. bezpiecznika dysku): oba trafiają do jednego komunikatu.
            try { Dismount-VHD -Path $VhdPath -ErrorAction Stop }
            catch {
                $DismountMessage = "Dismount-VHD '$VhdPath' nie powiódł się (VHDX może być nadal podłączony na hoście): $($_.Exception.Message)"
                if ($null -ne $Primary) { throw "$($Primary.Exception.Message) Dodatkowo: $DismountMessage" }
                throw $DismountMessage
            }
        }
    }
    return [pscustomobject]$Out
}

function Copy-E5FixturesToVm {
    # Programy testowe (tests\vm\fixtures) do C:\dso\fixtures w VM – faza av instaluje z nich DsoTestQuiet.
    [CmdletBinding()]
    param(
        [Parameter(Mandatory)][AllowNull()][System.Management.Automation.Runspaces.PSSession]$Session,
        [Parameter(Mandatory)][string]$Source,
        [Parameter(Mandatory)][ValidatePattern('^[A-Za-z]:\\[^\s"]+\z')][string]$Destination
    )
    Assert-DsoVmLiveAllowed
    if (-not [System.IO.Directory]::Exists($Source)) { throw "Brak katalogu fixtures '$Source'." }
    Invoke-Command -Session $Session -ArgumentList @($Destination) -ScriptBlock { param([string]$Path) $null = New-Item -ItemType Directory -Path $Path -Force } | Out-Null
    Copy-Item -Path (Join-Path $Source '*') -Destination $Destination -ToSession $Session -Recurse -Force
}

function Invoke-E5VmRun {
    # Jedna VM: [nośnik VHDX z wydaniem na hoście] -> revert -> karta odłączona -> [VHDX podłączony] -> start (bez sieci) -> gotowość
    # -> [fixtures] -> fazy (sieć tylko na ich czas; po nieudanej aktualizacji sygnatur fazy po defender pominięte) -> [surowe pliki]
    # -> ZAWSZE revert, odłączenie VHDX i karty, sprawdzenie stanu końcowego (Complete-ExploreVmCleanup) -> zapis JSON.
    [CmdletBinding()]
    [OutputType([string])]
    param(
        [Parameter(Mandatory)][ValidateSet('DSO-26H2-Home', 'DSO-26H2-Pro')][string]$VmName,
        [Parameter(Mandatory)][string]$OutDirectory,
        [Parameter(Mandatory)][string[]]$Phases,
        [string]$CheckpointName = 'czysta-26H2',
        [int]$ReadyTimeoutSeconds = 1200,
        # Opis artefaktu z Resolve-E5Artifact; wymagany przez fazy startup/av/motw/dpi.
        [AllowNull()]$Artifact,
        [string]$FixturesDirectory = (Join-Path (Split-Path -Parent $PSScriptRoot) 'fixtures')
    )
    Assert-DsoVmLiveAllowed
    $ArtifactPhases = @($Phases | Where-Object { @(Get-E5ArtifactPhases) -contains $_ })
    if ($ArtifactPhases.Count -gt 0 -and $null -eq $Artifact) {
        throw "Fazy $($ArtifactPhases -join ', ') wymagają artefaktu (-Variant i -ArtifactPath); VM '$VmName' nie została ruszona."
    }
    $Credential = Get-DsoVmCredential -VmName $VmName
    $Session = $null
    $StartedAt = Get-Date
    $Results = [ordered]@{}
    $Timeline = @()
    $PhaseErrors = @()
    $UserSid = $null
    $PrimaryError = $null
    $SignaturesUpdated = $null
    $Ready = $false
    $Usb = $null
    $VhdPath = $null
    $RawFiles = @()
    $VmDirectory = Join-Path $OutDirectory $VmName
    $Plan = Get-E5Plan
    if ($ArtifactPhases.Count -gt 0) { $Plan.Artifact = [ordered]@{ Variant = [string]$Artifact.Variant; Name = [string]$Artifact.Name; ExeRelativePath = [string]$Artifact.ExeRelativePath } }
    $Context = $null
    try {
        if ($ArtifactPhases.Count -gt 0) {
            $VhdPath = Join-Path $VmDirectory ('usb-{0}-{1}.vhdx' -f $Artifact.Variant, (Get-Date -Format 'yyyyMMdd-HHmmss'))
            $Usb = New-E5UsbDisk -VhdPath $VhdPath -Artifact $Artifact -Plan $Plan
            $Timeline += "$(Get-Date -Format 'HH:mm:ss') nośnik VHDX z wydaniem $($Artifact.Name)"
        }
        Restore-VmCleanState -VmName $VmName -CheckpointName $CheckpointName
        # VM nigdy nie startuje z siecią poza mierzonymi fazami (checkpoint mógł mieć kartę na przełączniku).
        Disconnect-VMNetworkAdapter -VMName $VmName -ErrorAction Stop
        if ($null -ne $VhdPath) {
            Add-VMHardDiskDrive -VMName $VmName -Path $VhdPath -ErrorAction Stop
            $Timeline += "$(Get-Date -Format 'HH:mm:ss') VHDX podłączony do VM"
        }
        Start-VM -Name $VmName
        $Session = Wait-VmReady -VmName $VmName -Credential $Credential -TimeoutSeconds $ReadyTimeoutSeconds
        $Ready = $true
        $UserSid = [string](Invoke-Command -Session $Session -ScriptBlock { (Get-LocalUser -Name 'dso').SID.Value })
        if ([string]::IsNullOrWhiteSpace($UserSid)) { throw "Nie udało się odczytać SID użytkownika 'dso' w VM '$VmName'." }
        # Chwila na dokończenie logowania dso (powłoka, pakiety) przed pierwszym zadaniem w jego sesji.
        Start-Sleep -Seconds 45
        if (@($Phases) -contains 'av') {
            Copy-E5FixturesToVm -Session $Session -Source $FixturesDirectory -Destination ([string]$Plan.FixturesDirectory)
            $Timeline += "$(Get-Date -Format 'HH:mm:ss') fixtures w VM"
        }
        $PlanJson = ConvertTo-Json -InputObject $Plan -Depth 6
        $Context = [ordered]@{ Session = $Session; RemoteScript = (Get-E5RemoteScript); PlanJson = $PlanJson; UserSid = $UserSid; VmName = $VmName; VmDirectory = $VmDirectory
            Credential = $Credential; ReadyTimeoutSeconds = $ReadyTimeoutSeconds }
        $NetworkPhases = @(Get-E5NetworkPhases)
        foreach ($Phase in $Phases) {
            $Record = [ordered]@{ phase = $Phase; error = $null }
            $Results[$Phase] = $Record
            $Network = ($NetworkPhases -contains $Phase)
            if ($Phase -ne 'defender' -and $SignaturesUpdated -ne $true) {
                $Record.skipped = 'faza pominięta: sygnatury Defendera nie zostały zaktualizowane w fazie defender (K1)'
                $Timeline += "$(Get-Date -Format 'HH:mm:ss') $Phase pominięta (K1)"
                continue
            }
            if ($Network) {
                Connect-VMNetworkAdapter -VMName $VmName -SwitchName 'Default Switch' -ErrorAction Stop
                $Timeline += "$(Get-Date -Format 'HH:mm:ss') sieć podłączona"
            }
            $Timeline += "$(Get-Date -Format 'HH:mm:ss') $Phase"
            Write-Host "$(Get-Date -Format 'HH:mm:ss') $VmName $Phase"
            try {
                Invoke-E5HostPhase -Phase $Phase -Context $Context -Record $Record
                if ($Record.error) { $PhaseErrors += "${Phase}: $($Record.error)" }
                foreach ($Problem in @(Get-E5Property -Object $Record -Name 'problems')) { if ($Problem) { $PhaseErrors += "${Phase}: $Problem" } }
            }
            catch {
                $PrimaryError = (@($PrimaryError, "faza ${Phase}: $($_.Exception.Message)") | Where-Object { $_ }) -join ' | '
                if (Test-VmAuthFailure -Exception $_.Exception) { throw }
            }
            finally {
                # Faza dpi restartuje VM: dalej (i w sprzątaniu) używamy nowej sesji.
                $Session = $Context.Session
                if ($Phase -eq 'defender') { $SignaturesUpdated = Test-E5SignaturesUpdated -Record $Record }
                if ($Network) {
                    try { Disconnect-VMNetworkAdapter -VMName $VmName -ErrorAction Stop; $Timeline += "$(Get-Date -Format 'HH:mm:ss') sieć odłączona" }
                    catch { $PrimaryError = (@($PrimaryError, "odłączenie sieci: $($_.Exception.Message)") | Where-Object { $_ }) -join ' | ' }
                }
            }
        }
        if (@($PhaseErrors).Count -gt 0) {
            # Błąd dokumentu albo awaria infrastruktury w fazie = pomiar niepełny; wynik zostaje w JSON, ale przebieg nie przechodzi bramki.
            $PrimaryError = (@($PrimaryError, "błędy faz: $(@($PhaseErrors) -join ' | ')") | Where-Object { $_ }) -join ' | '
        }
        if ($SignaturesUpdated -eq $false) {
            $Reason = Get-E5Property -Object (Get-E5Property -Object $Results['defender'] -Name 'data') -Name 'Error'
            $Suffix = if ($Reason) { " ($Reason)" } else { '' }
            $PrimaryError = (@($PrimaryError, "sygnatury Defendera nie zostały zaktualizowane (K1) – pomiar się nie liczy$Suffix") | Where-Object { $_ }) -join ' | '
        }
    }
    catch {
        $PrimaryError = (@($PrimaryError, $_.Exception.Message) | Where-Object { $_ }) -join ' | '
        if (Test-VmAuthFailure -Exception $_.Exception) { throw }
    }
    finally {
        if ($null -ne $Context) { $Session = $Context.Session }
        if ($Ready -and $ArtifactPhases.Count -gt 0) {
            try { $RawFiles = @(Copy-ExploreRawFiles -Session $Session -Destination (Join-Path $VmDirectory 'raw') -Source ([string]$Plan.RawDirectory)) }
            catch { $PrimaryError = (@($PrimaryError, "kopia surowych plików: $($_.Exception.Message)") | Where-Object { $_ }) -join ' | ' }
        }
        if ($null -ne $Session) { Remove-PSSession -Session $Session -ErrorAction SilentlyContinue }
        # Sprzątanie PRZED zapisem, żeby jego wynik (także błędy) trafił do JSON; błąd sprzątania nigdy nie jest ukrywany.
        $Cleanup = Complete-ExploreVmCleanup -VmName $VmName -CheckpointName $CheckpointName -DetachDiskPath @($VhdPath | Where-Object { $_ })
        if (-not $Cleanup.Ok) {
            $CleanupMessage = "sprzątanie VM: $(@($Cleanup.Problems) -join ' | ')"
            Write-Warning "VM '$VmName': $CleanupMessage"
            $PrimaryError = (@($PrimaryError, $CleanupMessage) | Where-Object { $_ }) -join ' | '
        }
        $Result = [ordered]@{ schema = 1; vmName = $VmName; checkpoint = $CheckpointName; userSid = $UserSid; startedAt = $StartedAt.ToString('o'); finishedAt = (Get-Date).ToString('o')
            error = $PrimaryError; phaseErrors = $PhaseErrors; signaturesUpdated = $SignaturesUpdated; artifact = $Artifact; usb = $Usb; cleanup = $Cleanup; timeline = $Timeline
            rawFiles = @($RawFiles | ForEach-Object { Split-Path -Leaf $_ }); phases = $Results }
        $Saved = Save-ExploreResult -Result $Result -OutDirectory $OutDirectory -VmName $VmName -FileName 'e5.json'
    }
    if ($PrimaryError) { throw "VM '$VmName': $PrimaryError (wynik częściowy: $Saved)" }
    return $Saved
}

function Invoke-E5Exploration {
    # Wszystkie wybrane VM po kolei. Błąd uwierzytelnienia = stop całości; inny błąd = zapis i dalej.
    [CmdletBinding()]
    param([Parameter(Mandatory)][System.Collections.IDictionary]$Arguments)
    $Paths = New-Object System.Collections.Generic.List[string]
    $Errors = New-Object System.Collections.Generic.List[string]
    foreach ($Vm in $Arguments.VmName) {
        try {
            Write-Host "$(Get-Date -Format 'HH:mm:ss') $Vm"
            $Paths.Add((Invoke-E5VmRun -VmName $Vm -OutDirectory $Arguments.OutDirectory -Phases @($Arguments.Phases[$Vm]) -CheckpointName $Arguments.CheckpointName -ReadyTimeoutSeconds $Arguments.ReadyTimeoutSeconds `
                        -Artifact (Get-E5Property -Object $Arguments -Name 'Artifact')))
        }
        catch {
            if (Test-VmAuthFailure -Exception $_.Exception) { throw }
            $Errors.Add("VM '$Vm': $($_.Exception.Message)")
        }
    }
    return [pscustomobject]@{ Paths = $Paths.ToArray(); Errors = $Errors.ToArray() }
}

# Przy dot-sourcingu (testy) tylko definicje; uruchomienie przebiegu tylko przy zwykłym wywołaniu skryptu.
if ($MyInvocation.InvocationName -ne '.') {
    $ExitCode = 1
    try {
        Set-StrictMode -Version Latest
        $ErrorActionPreference = 'Stop'
        $VmRoot = Split-Path -Parent $PSScriptRoot
        Import-Module (Join-Path $VmRoot 'modules\VmCommon.psm1')
        $RepoRoot = Get-DsoRepoRoot
        $OutDirectory = Assert-ExploreOutDirectory -Path $OutDirectory -RepoRoot $RepoRoot -Example '$env:TEMP\dso-explore\pomiar\pro-singleexe'
        $OutDirectory = Resolve-ExploreOutDirectory -Path $OutDirectory
        if (-not $BuildDirectory) { $BuildDirectory = Join-Path $RepoRoot 'src\DSOptimizer.App\bin\Release\net481' }
        $RunArguments = Get-E5RunArguments -VmName $VmName -Phase $Phase -OutDirectory $OutDirectory -CheckpointName $CheckpointName -ReadyTimeoutSeconds $ReadyTimeoutSeconds -BuildDirectory $BuildDirectory `
            -Variant $Variant -ArtifactPath $ArtifactPath
        if ($ShowPlan) {
            [pscustomobject]$RunArguments | ConvertTo-Json -Depth 6
            exit 0
        }
        Import-Module (Join-Path $VmRoot 'modules\VmCommon.psm1') -Force
        Import-Module (Join-Path $VmRoot 'modules\VmHost.psm1') -Force
        # ExploreCommon i VmSmoke (zaimportowane wyżej) trzymają referencję do starej instancji VmHost; ponowny import po nim wiąże je z bieżącą.
        Import-Module (Join-Path $PSScriptRoot 'ExploreCommon.psm1') -Force
        Import-Module (Join-Path $VmRoot 'modules\VmSmoke.psm1') -Force
        $Principal = New-Object System.Security.Principal.WindowsPrincipal([System.Security.Principal.WindowsIdentity]::GetCurrent())
        if (-not $Principal.IsInRole([System.Security.Principal.WindowsBuiltInRole]::Administrator)) {
            throw 'Uruchom skrypt w sesji PowerShell jako administrator (Hyper-V i PowerShell Direct tego wymagają).'
        }
        Write-Host "Wyniki: $OutDirectory"
        if ($null -ne $RunArguments.Artifact) { Write-Host "Wydanie: $($RunArguments.Artifact.Variant) $($RunArguments.Artifact.Path) (SHA256 $($RunArguments.Artifact.Sha256))" }
        foreach ($Vm in $RunArguments.VmName) { Write-Host "VM: $Vm; fazy: $(@($RunArguments.Phases[$Vm]) -join ', ')" }
        $Outcome = Invoke-E5Exploration -Arguments $RunArguments
        foreach ($Path in $Outcome.Paths) { Write-Host "Zapisano: $Path" }
        foreach ($Message in $Outcome.Errors) { Write-Host "BŁĄD $Message" -ForegroundColor Red }
        $ExitCode = if (@($Outcome.Errors).Count -eq 0) { 0 } else { 1 }
    }
    catch {
        Write-Host "BŁĄD: $($_.Exception.Message)" -ForegroundColor Red
        Write-Host $_.ScriptStackTrace -ForegroundColor DarkGray
        $ExitCode = 1
    }
    exit $ExitCode
}
