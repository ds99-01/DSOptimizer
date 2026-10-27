<#
Instrukcja uruchomienia:
    Sesja PowerShell uruchomiona jako administrator, na hoście z Hyper-V, z modułami testów VM (tests\vm\modules) i tests\vm\explore\ExploreCommon.psm1.
    VM utworzone wcześniej przez New-TestVm.ps1 (checkpoint 'czysta-26H2', poświadczenia w %LOCALAPPDATA%\DSOptimizer-vm, autologon 'dso').
    Build DSOptimizera potrzebny tylko w fazie cli-install (pozostałe fazy wołają wyłącznie winget i whoami w VM).
    Przykład (jedna VM na proces; Home i Pro można uruchomić równolegle w dwóch sesjach, każda z własną VM):
        .\tests\vm\explore\Invoke-E4CExploration.ps1 -VmName DSO-26H2-Pro -OutDirectory $env:TEMP\dso-explore\exploration
    Bez -Phase: wszystkie fazy (baseline zawsze pierwsza; adminprotection tylko na Pro, tuż przed final).
    -NoNetwork: pomija fazy network i install-all; karta VM nie jest podłączana do 'Default Switch'.
    -ShowPlan: wypisuje rozstrzygnięte argumenty i plan (JSON) i kończy, nic nie uruchamia.
    Faza cli-install (Task 11) tylko jawnie i na czystej VM, po `dotnet build DSOptimizer.sln -c Release`:
        .\tests\vm\explore\Invoke-E4CExploration.ps1 -VmName DSO-26H2-Pro -Phase cli-install -OutDirectory $env:TEMP\dso-explore\cli-install
        Build kopiowany do C:\dso\app (Copy-DsoBuildToVm, -BuildDirectory domyślnie src\DSOptimizer.App\bin\Release\net481); Pro 1–2 godziny.
    -CliIds <id>[,<id>…] (plan RustDesk): własna lista serii cli-install zamiast planu (Pro = cała apps.json, Home = CliHomeIds), każde id
    install.<nazwa> albo winget:<pełny id>, bez spacji i przecinków; -CliRepeat: po --list-install drugie --install-apps z tymi samymi id
    (raport cli-install-again.json, np. „Była już zainstalowana” bez pobierania). Przykład (Task 7, po jednej VM na proces):
        .\tests\vm\explore\Invoke-E4CExploration.ps1 -VmName DSO-26H2-Pro -Phase cli-install -CliIds install.7zip,install.rustdesk -CliRepeat -OutDirectory $env:TEMP\dso-explore\rustdesk-cli\pro
        .\tests\vm\explore\Invoke-E4CExploration.ps1 -VmName DSO-26H2-Home -Phase cli-install -CliIds install.rustdesk -CliRepeat -OutDirectory $env:TEMP\dso-explore\rustdesk-cli\home
    Przykłady działają w sesji PowerShell; przez powershell.exe -File lista „a,b” przychodzi jako jeden napis z przecinkiem (odrzucony walidacją,
    VM nietknięta) – wtedy: powershell.exe -Command "& .\tests\vm\explore\Invoke-E4CExploration.ps1 -CliIds @('install.7zip','install.rustdesk') …".
    -OutDirectory jest wymagany: ścieżka bezwzględna poza repo; w repo tylko katalog ignorowany przez git.
    Czas: Home ok. 30 min, Pro 1–2 godziny (instalacja całej listy, dwa restarty VM w fazie adminprotection).
Opis działania:
    Pomiar Etapu 4c (instalacja aplikacji przez winget) na VM 26H2 z siecią (Default Switch tylko w fazach network i install-all).
    Winget startuje WYŁĄCZNIE w VM, z zadania Harmonogramu jako 'dso' (Interactive + Highest, czyli jak DSOptimizer po UAC),
    z aliasu %LOCALAPPDATA%\Microsoft\WindowsApps\winget.exe profilu konta procesu; runner (C:\dso\e4c\runner.ps1) zapisuje
    stdout i stderr BAJTOWO do C:\dso\e4c\raw\<nazwa>.out.bin/.err.bin oraz kod wyjścia, czas i SID procesu do <nazwa>.result.json.
    Limit czekania nie zabija procesu: runner przestaje czekać, zapisuje wynik z TimedOut, a późniejszy kod dopisuje do <nazwa>.late.json.
    Instalacja zawsze pełnym poleceniem programu (spec 5.1): install --id <id> --exact --source winget --silent --no-upgrade
    [--locale pl-PL] --accept-package-agreements --accept-source-agreements --disable-interactivity --log <plik>; przy 0x8A150010
    jedna ponowna próba bez --locale. Przed i po każdej instalacji odczyt Uninstall (HKLM 64/32, HKU\<SID dso>) i AppX konta,
    a po niej list --id <id> --exact (z --source winget i bez).
    Fazy: baseline (bez sieci: --version, --info, TypeOfAdminApprovalMode, język, Uninstall) -> offline-fresh (bez sieci, świeże źródło:
    install, search, list) -> network (sieć: show każdej pozycji listy i odrzuconych, długie wyszukiwania, --locale Firefox i
    LibreOffice, --no-upgrade 7-Zip dwa razy, kandydat spoza listy z wyszukiwania) -> install-all (sieć: Pro = cała lista, Home = po
    jednej z kategorii) -> cli-install (tylko jawnie, sieć: DSOptimizer.exe --install-apps <CliIds> --out ... jako dso z zadania
    Harmonogramu – Pro cała lista apps.json, Home po jednej pozycji z kategorii + winget:PuTTY.PuTTY, albo -CliIds; potem --list-install,
    z -CliRepeat drugie --install-apps, wpisy journala app-install, diff Uninstall, odczyt RustDesk (pliki w C:\ProgramData\DSOptimizer\downloads,
    logi logs\winget\rustdesk-msi-*.log, usługa RustDesk, reguły zapory RustDesk* – tylko odczyt), winget --version przed i po, kopie journala
    i logów) -> adminprotection (tylko Pro: TypeOfAdminApprovalMode = 2 + restart, whoami i winget --version z zadania
    Highest i Limited, przywrócenie wartości + restart) -> final (bez sieci, źródło już pobrane: install, search, show, list, procesy).
    Przebieg jednej VM: revert do 'czysta-26H2' -> start (bez sieci) -> gotowość (zalogowany dso) -> fazy -> kopia surowych plików
    -> ZAWSZE (finally) revert, odłączenie karty i sprawdzenie stanu końcowego (VM Off, karta bez przełącznika) przed zapisem JSON.
    Ostatni bezpiecznik po stronie VM: Assert-E4CInsideVm (nazwa komputera DSO-26H2-HOME/PRO i brak DSO_VM_TESTS_MOCK_ONLY) na
    początku każdej funkcji ze skutkiem. Pierwszy błąd uwierzytelnienia przerywa cały skrypt (blokada konta). Skrypt dotyka wyłącznie
    VM DSO-26H2-Home i DSO-26H2-Pro (ValidateSet), nigdy innej VM; na hoście nie uruchamia winget i niczego nie zmienia.
    Wynik: <OutDirectory>\<VmName>\e4c.json (UTF-8) i <OutDirectory>\<VmName>\raw\*.
    Kod wyjścia: 0 gdy wszystkie wybrane VM zebrane bez błędu przebiegu, 1 w przeciwnym razie.
#>
[CmdletBinding()]
param(
    [ValidateSet('DSO-26H2-Home', 'DSO-26H2-Pro')][string[]]$VmName = @('DSO-26H2-Pro', 'DSO-26H2-Home'),
    [ValidateSet('baseline', 'offline-fresh', 'network', 'install-all', 'cli-install', 'adminprotection', 'final')][string[]]$Phase = @('baseline', 'offline-fresh', 'network', 'install-all', 'adminprotection', 'final'),
    [string]$OutDirectory,
    [string]$CheckpointName = 'czysta-26H2',
    [ValidateRange(60, 7200)][int]$ReadyTimeoutSeconds = 1200,
    [switch]$NoNetwork,
    [switch]$ShowPlan,
    # Faza cli-install: katalog builda Release (domyślnie src\DSOptimizer.App\bin\Release\net481).
    [string]$BuildDirectory,
    # Faza cli-install (plan RustDesk): własna lista id serii (nadpisuje CliIds/CliHomeIds planu) i drugie --install-apps po liście.
    [string[]]$CliIds,
    [switch]$CliRepeat
)

Import-Module (Join-Path $PSScriptRoot 'ExploreCommon.psm1') -Force

# ---------------------------------------------------------------------------------------------------------------------
# Plan (dane z planu Tasku 2 Etapu 4c i spec pkt 4).
# ---------------------------------------------------------------------------------------------------------------------

function Get-E4CPlan {
    [CmdletBinding()]
    param()
    $Apps = @(
        @('browsers', 'Google.Chrome'), @('browsers', 'Mozilla.Firefox'), @('browsers', 'Brave.Brave'), @('browsers', 'Opera.Opera'),
        @('messengers', 'Discord.Discord'), @('messengers', 'Telegram.TelegramDesktop'), @('messengers', 'OpenWhisperSystems.Signal'), @('messengers', 'Zoom.Zoom'), @('messengers', 'Microsoft.Teams'),
        @('multimedia', 'VideoLAN.VLC'), @('multimedia', 'CodecGuide.K-LiteCodecPack.Standard'), @('multimedia', 'PeterPawlowski.foobar2000'),
        @('documents', 'TheDocumentFoundation.LibreOffice'), @('documents', 'Adobe.Acrobat.Reader.64-bit'), @('documents', 'SumatraPDF.SumatraPDF'),
        @('tools', '7zip.7zip'), @('tools', 'Notepad++.Notepad++'), @('tools', 'voidtools.Everything'), @('tools', 'Microsoft.PowerToys'), @('tools', 'RARLab.WinRAR'), @('tools', 'ShareX.ShareX'),
        @('tools', 'CPUID.CPU-Z'), @('tools', 'CPUID.HWMonitor'), @('tools', 'TechPowerUp.GPU-Z'),
        @('remote-help', 'AnyDesk.AnyDesk'), @('remote-help', 'TeamViewer.TeamViewer'),
        @('games', 'Valve.Steam'), @('games', 'EpicGames.EpicGamesLauncher'), @('games', 'GOG.Galaxy'),
        @('runtimes', 'Microsoft.VCRedist.2015+.x64'), @('runtimes', 'Microsoft.VCRedist.2015+.x86'), @('runtimes', 'Microsoft.DotNet.DesktopRuntime.8'), @('runtimes', 'Microsoft.DotNet.DesktopRuntime.9')
    ) | ForEach-Object { [ordered]@{ WingetId = $_[1]; Category = $_[0] } }
    return [ordered]@{
        Apps                  = @($Apps)
        # Odrzucone w spec (Spotify 0x8A150056, RustDesk „brak w winget-pkgs”, DirectX bez wpisu Uninstall): tylko show jako dowód powodu.
        RejectedShow          = @('Spotify.Spotify', 'RustDesk.RustDesk', 'Microsoft.DirectX')
        # Home: po jednej pozycji z każdej kategorii, w tym jedna per user (Discord); środowiska uruchomieniowe najpierw (spec 5.4).
        HomeInstall           = @('Microsoft.VCRedist.2015+.x64', 'Google.Chrome', 'Discord.Discord', 'VideoLAN.VLC', 'SumatraPDF.SumatraPDF', 'Notepad++.Notepad++', 'AnyDesk.AnyDesk', 'Valve.Steam')
        Locale                = 'pl-PL'
        LocaleProbe           = @('Mozilla.Firefox', 'TheDocumentFoundation.LibreOffice')
        NoUpgradeProbe        = '7zip.7zip'
        LongSearches          = @('Microsoft Visual C++ Redistributable', 'codec pack', 'runtime desktop')
        # Dodatkowe zapytania z długimi nazwami w wynikach (pierwszy przebieg Home: trzy z planu nie dały obcięcia).
        ExtraSearches         = @('redistributable', 'visual c++', 'windows desktop runtime', 'microsoft')
        # Kandydat winget:<id> spoza listy dla przebiegu Home w T11: mały instalator Win32 (MSI) znaleziony w search.
        SearchCandidate       = [ordered]@{ Query = 'putty'; WingetId = 'PuTTY.PuTTY' }
        OfflineFreshInstall   = '7zip.7zip'
        OfflineSearch         = 'vlc'
        # Pakiet spoza listy i spoza pomiaru: instalacja offline po pobraniu źródła (nigdy wcześniej instalowany w tym przebiegu).
        FinalInstall          = 'WinMerge.WinMerge'
        FinalShow             = '7zip.7zip'
        FinalListIds          = @('7zip.7zip', 'Discord.Discord')
        # Faza cli-install (Task 11): seria DSOptimizer.exe --install-apps jako dso. Pro = cała lista apps.json (CliIds ustawia host),
        # Home = po jednej pozycji z każdej kategorii (inne niż w pomiarze Tasku 2, Discord zostaje dla R16) + kandydat z wyszukiwania.
        CliHomeIds               = @('install.firefox', 'install.discord', 'install.vlc', 'install.sumatrapdf', 'install.7zip', 'install.teamviewer', 'install.gog-galaxy', 'install.dotnet-desktop-8', 'winget:PuTTY.PuTTY')
        CliIds                   = @()
        # -CliRepeat (plan RustDesk): po --list-install drugie --install-apps z tymi samymi id (cli-install-again.json).
        CliRepeat                = $false
        CliInstallTimeoutSeconds = 7200
        # Folder pobrań programu (RustDesk z GitHuba): po każdym wyniku serii ma być pusty albo nieobecny.
        DownloadsDirectory       = 'C:\ProgramData\DSOptimizer\downloads'
        JournalPath              = 'C:\ProgramData\DSOptimizer\journal\journal.json'
        DsoLogDirectory          = 'C:\ProgramData\DSOptimizer\logs'
        InstallTimeoutSeconds = 1800
        CommandTimeoutSeconds = 600
        TaskGraceSeconds      = 120
        TaskPollSeconds       = 2
        NetworkName           = 'cdn.winget.microsoft.com'
        NetworkWaitSeconds    = 300
        AdminApprovalKey      = 'HKLM\SOFTWARE\Microsoft\Windows\CurrentVersion\Policies\System'
        AdminProtectionValue  = 2
        WorkDirectory         = 'C:\dso\e4c'
        RawDirectory          = 'C:\dso\e4c\raw'
        UserName              = 'dso'
        InstallIds            = @()
    }
}

# ---------------------------------------------------------------------------------------------------------------------
# Funkcje wysyłane do VM (samowystarczalne: wołają siebie nawzajem, funkcje Explore z modułu i cmdlety systemowe).
# ---------------------------------------------------------------------------------------------------------------------

function Test-E4CVmComputerName {
    # Nazwy komputerów VM nadawane przez New-TestVm.ps1 (VmBuild: nazwa VM wielkimi literami).
    [CmdletBinding()]
    [OutputType([bool])]
    param([AllowEmptyString()][string]$ComputerName)
    return (@('DSO-26H2-HOME', 'DSO-26H2-PRO') -contains ([string]$ComputerName).ToUpperInvariant())
}

function Assert-E4CInsideVm {
    # Ostatni bezpiecznik: sondy E4C rejestrują zadania Harmonogramu, uruchamiają winget (instalacje), piszą do rejestru i restartują
    # komputer, więc działają WYŁĄCZNIE w VM DSO-26H2-Home/Pro i nigdy w testach na mockach (DSO_VM_TESTS_MOCK_ONLY).
    [CmdletBinding()]
    param([string]$ComputerName = $env:COMPUTERNAME)
    if ($env:DSO_VM_TESTS_MOCK_ONLY) {
        throw 'Tryb DSO_VM_TESTS_MOCK_ONLY: sondy E4C nie mogą działać w testach na mockach (nic nie zostało uruchomione).'
    }
    if (-not (Test-E4CVmComputerName -ComputerName $ComputerName)) {
        throw "Sondy E4C działają tylko w VM DSO-26H2-HOME/DSO-26H2-PRO (ten komputer: '$ComputerName'); nic nie zostało uruchomione."
    }
}

function Format-E4CError {
    [CmdletBinding()]
    [OutputType([string])]
    param([Parameter(Mandatory)]$ErrorRecord)
    $Exception = if ($ErrorRecord -is [System.Management.Automation.ErrorRecord]) { $ErrorRecord.Exception } else { $ErrorRecord }
    $Base = $Exception.GetBaseException()
    return ('0x{0:X8} {1}' -f $Base.HResult, $Base.Message)
}

function Format-E4CHex {
    # Kod wyjścia (int ze znakiem, jak Process.ExitCode) -> '0x8A150010'; brak kodu -> $null.
    [CmdletBinding()]
    param([AllowNull()]$Code)
    if ($null -eq $Code -or [string]$Code -eq '') { return $null }
    # LastTaskResult zadania Harmonogramu bywa uint32 (> int.MaxValue): sprowadzenie do int ze znakiem bez przepełnienia.
    $Long = [int64]$Code
    if ($Long -gt [int]::MaxValue) { $Long -= 4294967296 }
    return ('0x{0:X8}' -f [int]$Long)
}

function Get-E4CSafeName {
    # Nazwa pliku/zadania: tylko [A-Za-z0-9.-], reszta na '_'.
    [CmdletBinding()]
    [OutputType([string])]
    param([Parameter(Mandatory)][string]$Text)
    return ($Text -replace '[^A-Za-z0-9.-]', '_')
}

function Get-E4CElapsedSeconds {
    [CmdletBinding()]
    [OutputType([int])]
    param([Parameter(Mandatory)]$Watch)
    return [int]$Watch.Elapsed.TotalSeconds
}

function Get-E4CFreeBytes {
    [CmdletBinding()]
    param()
    try { return (New-Object System.IO.DriveInfo 'C').AvailableFreeSpace } catch { return $null }
}

function Test-E4CWingetId {
    # Ten sam wzorzec co WingetArguments.IsValidWingetId (Task 3): pusty albo z „…” = nie do instalacji.
    [CmdletBinding()]
    [OutputType([bool])]
    param([AllowEmptyString()][AllowNull()][string]$Id)
    return ([string]$Id -cmatch '^[A-Za-z0-9][A-Za-z0-9.+_-]{0,127}\z')
}

function Assert-E4CPlainPath {
    # Ścieżka w linii argumentów bez cytowania: bez spacji, tabulatorów i cudzysłowów.
    [CmdletBinding()]
    param([Parameter(Mandatory)][AllowEmptyString()][string]$Path)
    if ([string]::IsNullOrWhiteSpace($Path) -or $Path -match '[\s"]') { throw "Ścieżka '$Path' musi być niepusta, bez spacji i cudzysłowów." }
}

function Get-E4CInstallArguments {
    # Polecenie instalacji programu (Global Constraints, spec 5.1), kolejność argumentów stała.
    [CmdletBinding()]
    [OutputType([string])]
    param(
        [Parameter(Mandatory)][AllowEmptyString()][string]$WingetId,
        [Parameter(Mandatory)][AllowEmptyString()][string]$Locale,
        [Parameter(Mandatory)][string]$LogPath
    )
    if (-not (Test-E4CWingetId -Id $WingetId)) { throw "Nieprawidłowy identyfikator winget '$WingetId'." }
    if ($Locale -and $Locale -cnotmatch '^[a-z]{2,3}(-[A-Za-z0-9]{2,8})*\z') { throw "Nieprawidłowa lokalizacja '$Locale'." }
    Assert-E4CPlainPath -Path $LogPath
    $Parts = @('install', '--id', $WingetId, '--exact', '--source', 'winget', '--silent', '--no-upgrade')
    if ($Locale) { $Parts += @('--locale', $Locale) }
    $Parts += @('--accept-package-agreements', '--accept-source-agreements', '--disable-interactivity', '--log', $LogPath)
    return ($Parts -join ' ')
}

function Get-E4CSearchArguments {
    # Wyszukiwanie (spec 7): zapytanie ze spacją w cudzysłowie; cudzysłów i znaki sterujące odrzucane.
    [CmdletBinding()]
    [OutputType([string])]
    param([Parameter(Mandatory)][AllowEmptyString()][string]$Query)
    if ([string]::IsNullOrWhiteSpace($Query) -or $Query.Length -gt 100 -or $Query -match '["\x00-\x1F\x7F]') { throw "Nieprawidłowe zapytanie '$Query'." }
    $Quoted = if ($Query -match '\s') { '"' + $Query + '"' } else { $Query }
    return "search --query $Quoted --source winget --count 50 --accept-source-agreements --disable-interactivity"
}

function Get-E4CListArguments {
    # list --id <id> --exact: z --source winget (forma programu, WingetArguments.ListExact) albo bez (forma spike'a).
    [CmdletBinding()]
    [OutputType([string])]
    param(
        [Parameter(Mandatory)][string]$WingetId,
        [Parameter(Mandatory)][bool]$WithSource
    )
    if (-not (Test-E4CWingetId -Id $WingetId)) { throw "Nieprawidłowy identyfikator winget '$WingetId'." }
    $Source = if ($WithSource) { ' --source winget' } else { '' }
    return "list --id $WingetId --exact$Source --accept-source-agreements --disable-interactivity"
}

function Get-E4CShowArguments {
    [CmdletBinding()]
    [OutputType([string])]
    param([Parameter(Mandatory)][string]$WingetId)
    if (-not (Test-E4CWingetId -Id $WingetId)) { throw "Nieprawidłowy identyfikator winget '$WingetId'." }
    return "show --id $WingetId --exact --source winget --accept-source-agreements --disable-interactivity"
}

function Get-E4CCliArguments {
    # Argumenty CLI DSOptimizera (spec 5.5): --install-apps <id,id> --out <plik> albo --list-install --out <plik>.
    # Id = install.<nazwa> albo winget:<pełny id>; bez spacji i przecinków (runner nie cytuje argumentów).
    [CmdletBinding()]
    [OutputType([string])]
    param(
        [Parameter(Mandatory)][ValidateSet('install-apps', 'list-install')][string]$Command,
        [AllowNull()][AllowEmptyCollection()][string[]]$AppIds,
        [Parameter(Mandatory)][string]$OutPath
    )
    Assert-E4CPlainPath -Path $OutPath
    $HasIds = $null -ne $AppIds -and @($AppIds).Count -gt 0
    if ($Command -eq 'list-install') {
        if ($HasIds) { throw 'Polecenie --list-install nie przyjmuje id.' }
        return "--list-install --out $OutPath"
    }
    if (-not $HasIds) { throw 'Polecenie --install-apps wymaga co najmniej jednego id.' }
    foreach ($Id in $AppIds) {
        if (-not (Test-E4CCliId -Id $Id)) { throw "Nieprawidłowe id pozycji '$Id' (install.<nazwa> albo winget:<pełny id>)." }
    }
    return "--install-apps $($AppIds -join ',') --out $OutPath"
}

function Test-E4CCliId {
    # Id pozycji serii CLI: install.<nazwa> (reguła AppsInstallCatalog.IsValidId) albo winget:<pełny id> (Test-E4CWingetId);
    # bez spacji i przecinków, bo runner nie cytuje argumentów, a CLI dzieli listę po przecinku.
    [CmdletBinding()]
    [OutputType([bool])]
    param([AllowNull()][AllowEmptyString()][string]$Id)
    if ([string]::IsNullOrEmpty($Id)) { return $false }
    if ($Id -cmatch '^install\.[a-z0-9-]+\z') { return $true }
    return ($Id.StartsWith('winget:', [System.StringComparison]::Ordinal) -and (Test-E4CWingetId -Id $Id.Substring(7)))
}

function Get-E4CRunnerScript {
    # Treść runnera uruchamianego w VM przez zadanie Harmonogramu (konto dso). Czyta żądanie JSON, startuje JEDNO narzędzie
    # (winget z aliasu profilu konta procesu, whoami z System32 albo DSOptimizer.exe z C:\dso\app – faza cli-install) bez ShellExecute
    # i bez okna, stdout/stderr bajtowo do plików.
    # Po limicie czekania zapisuje wynik z TimedOut i czeka dalej (proces nie jest zabijany), a kod końcowy dopisuje do LateFile.
    [CmdletBinding()]
    [OutputType([string])]
    param()
    return @'
param([Parameter(Mandatory)][string]$Request)
# Runner pomiaru E4C (DSOptimizer, tests\vm\explore\Invoke-E4CExploration.ps1). Tylko w VM DSO-26H2-HOME/PRO.
$ErrorActionPreference = 'Stop'
$Req = Get-Content -LiteralPath $Request -Raw -Encoding UTF8 | ConvertFrom-Json
$Result = [ordered]@{
    Name = $Req.Name; Tool = $Req.Tool; Arguments = $Req.Arguments; TimeoutSeconds = $Req.TimeoutSeconds; FilePath = $null; FileExists = $null
    Sid = $null; Account = $null; Elevated = $null; LocalAppData = $env:LOCALAPPDATA; RunnerPid = $PID; ChildPid = $null
    ExitCode = $null; TimedOut = $false; Seconds = $null; StartedAt = (Get-Date).ToString('o'); Error = $null
}
$Watch = [System.Diagnostics.Stopwatch]::StartNew()
$OutStream = $null
$ErrStream = $null
$Process = $null
function Save-RunnerJson([string]$Path, $Data) {
    $Tmp = "$Path.tmp"
    [System.IO.File]::WriteAllText($Tmp, ($Data | ConvertTo-Json -Depth 4), (New-Object System.Text.UTF8Encoding($false)))
    Move-Item -LiteralPath $Tmp -Destination $Path -Force
}
try {
    if ($env:DSO_VM_TESTS_MOCK_ONLY) { throw 'DSO_VM_TESTS_MOCK_ONLY: runner nie uruchamia niczego w testach.' }
    if (@('DSO-26H2-HOME', 'DSO-26H2-PRO') -notcontains ([string]$env:COMPUTERNAME).ToUpperInvariant()) { throw "Runner dziala tylko w VM DSO-26H2-HOME/PRO (komputer: '$env:COMPUTERNAME')." }
    $Identity = [System.Security.Principal.WindowsIdentity]::GetCurrent()
    $Result.Sid = $Identity.User.Value
    $Result.Account = $Identity.Name
    $Result.Elevated = (New-Object System.Security.Principal.WindowsPrincipal($Identity)).IsInRole([System.Security.Principal.WindowsBuiltInRole]::Administrator)
    switch ([string]$Req.Tool) {
        'winget' { $Path = Join-Path $env:LOCALAPPDATA 'Microsoft\WindowsApps\winget.exe' }
        'whoami' { $Path = Join-Path $env:SystemRoot 'System32\whoami.exe' }
        'dso' { $Path = 'C:\dso\app\DSOptimizer.exe' }
        default { throw "Narzedzie '$($Req.Tool)' nie jest dozwolone." }
    }
    $Result.FilePath = $Path
    $Result.FileExists = [System.IO.File]::Exists($Path)
    $Psi = New-Object System.Diagnostics.ProcessStartInfo
    $Psi.FileName = $Path
    $Psi.Arguments = [string]$Req.Arguments
    $Psi.WorkingDirectory = $env:SystemRoot
    $Psi.UseShellExecute = $false
    $Psi.RedirectStandardOutput = $true
    $Psi.RedirectStandardError = $true
    $Psi.CreateNoWindow = $true
    $OutStream = New-Object System.IO.FileStream([string]$Req.OutFile, [System.IO.FileMode]::Create, [System.IO.FileAccess]::Write, [System.IO.FileShare]::Read, 1)
    $ErrStream = New-Object System.IO.FileStream([string]$Req.ErrFile, [System.IO.FileMode]::Create, [System.IO.FileAccess]::Write, [System.IO.FileShare]::Read, 1)
    $Process = [System.Diagnostics.Process]::Start($Psi)
    $Result.ChildPid = $Process.Id
    $OutCopy = $Process.StandardOutput.BaseStream.CopyToAsync($OutStream, 4096)
    $ErrCopy = $Process.StandardError.BaseStream.CopyToAsync($ErrStream, 4096)
    if ($Process.WaitForExit([int]$Req.TimeoutSeconds * 1000)) {
        $Process.WaitForExit()
        try { $null = $OutCopy.Wait(15000) } catch { $null = $_ }
        try { $null = $ErrCopy.Wait(15000) } catch { $null = $_ }
        $Result.ExitCode = $Process.ExitCode
    }
    else { $Result.TimedOut = $true }
}
catch { $Result.Error = [string]$_.Exception.GetBaseException().Message }
finally {
    $Result.Seconds = [math]::Round($Watch.Elapsed.TotalSeconds, 1)
    Save-RunnerJson -Path ([string]$Req.ResultFile) -Data $Result
}
if ($Result.TimedOut -and $null -ne $Process) {
    # Limit minal: wynik juz zapisany, proces pracuje dalej (bez zabijania); kod koncowy do LateFile.
    $Process.WaitForExit()
    try { $null = $OutCopy.Wait(15000) } catch { $null = $_ }
    try { $null = $ErrCopy.Wait(15000) } catch { $null = $_ }
    Save-RunnerJson -Path ([string]$Req.LateFile) -Data ([ordered]@{ Name = $Req.Name; ExitCode = $Process.ExitCode; Seconds = [math]::Round($Watch.Elapsed.TotalSeconds, 1) })
}
if ($null -ne $OutStream) { $OutStream.Dispose() }
if ($null -ne $ErrStream) { $ErrStream.Dispose() }
'@
}

function Test-E4CProgressSegment {
    # Fragment wyjścia między \r/\n, który jest spinnerem, czyszczeniem linii albo paskiem postępu (nie trafia do Lines).
    [CmdletBinding()]
    [OutputType([bool])]
    param([Parameter(Mandatory)][AllowEmptyString()][string]$Text)
    $Trimmed = $Text.Trim()
    if ($Trimmed -eq '') { return $true }
    if ($Trimmed -match '^[-\\|/]\z') { return $true }
    if ($Text -match '[\u2588\u2592]') { return $true }
    if ($Trimmed -match '^\d{1,3}\s?%\z') { return $true }
    if ($Trimmed -match '^\d+(\.\d+)?\s*(B|KB|MB|GB)\s*/\s*\d+(\.\d+)?\s*(B|KB|MB|GB)\z') { return $true }
    return $false
}

function ConvertFrom-E4COutput {
    # Surowe bajty -> linie tekstu bez spinnera i paska, liczniki \r\n / samych \r i fragmentów postępu.
    [CmdletBinding()]
    param(
        [AllowNull()][AllowEmptyCollection()][byte[]]$Bytes,
        [int]$CodePage = 65001
    )
    $Length = if ($null -eq $Bytes) { 0 } else { $Bytes.Length }
    $Text = ''
    if ($Length -gt 0) { $Text = [System.Text.Encoding]::GetEncoding($CodePage).GetString($Bytes) }
    $Text = $Text.TrimStart([char]0xFEFF)
    $Lines = New-Object System.Collections.Generic.List[string]
    $Progress = 0
    foreach ($Segment in ($Text -split "`r`n|`r|`n")) {
        if (Test-E4CProgressSegment -Text $Segment) { if ($Segment.Length -gt 0) { $Progress++ }; continue }
        $Lines.Add($Segment.TrimEnd())
    }
    return [pscustomobject]@{
        Length = $Length; CodePage = $CodePage; CrLf = ([regex]::Matches($Text, "`r`n")).Count; CrOnly = ([regex]::Matches($Text, "`r(?!`n)")).Count
        ProgressSegments = $Progress; Lines = $Lines.ToArray()
    }
}

function Get-E4CShowFields {
    # Pola z wyjścia `winget show` (angielskie znaczniki z pomiaru 1.11).
    [CmdletBinding()]
    param([Parameter(Mandatory)][AllowEmptyCollection()][string[]]$Lines)
    $Fields = [ordered]@{ Name = $null; Id = $null; Version = $null; Publisher = $null; InstallerType = $null; InstallerLocale = $null; InstallerScope = $null; InstallerUrl = $null; ReleaseDate = $null }
    $Patterns = [ordered]@{
        Version = '^Version:\s*(.+)$'; Publisher = '^Publisher:\s*(.+)$'; InstallerType = '^\s*Installer Type:\s*(.+)$'; InstallerLocale = '^\s*Installer Locale:\s*(.+)$'
        InstallerScope = '^\s*Installer Scope:\s*(.+)$'; InstallerUrl = '^\s*Installer Url:\s*(.+)$'; ReleaseDate = '^\s*Release Date:\s*(.+)$'
    }
    foreach ($Line in $Lines) {
        if ($null -eq $Fields.Id -and $Line -match '^Found (.+) \[([^\]]+)\]') { $Fields.Name = $Matches[1]; $Fields.Id = $Matches[2]; continue }
        foreach ($Key in $Patterns.Keys) {
            if ($null -eq $Fields[$Key] -and $Line -match $Patterns[$Key]) { $Fields[$Key] = $Matches[1].Trim() }
        }
    }
    return [pscustomobject]$Fields
}

function Get-E4CSearchSummary {
    # Tabela `winget search`: nagłówek nad linią kresek, początki kolumn z nagłówka, wiersze, obcięcia „…” w kolumnie Name i Id.
    [CmdletBinding()]
    param([Parameter(Mandatory)][AllowEmptyCollection()][string[]]$Lines)
    $Ellipsis = [string][char]0x2026
    $Out = [ordered]@{ Header = $null; ColumnStarts = @(); RowCount = 0; TruncatedNameRows = 0; TruncatedIdRows = 0; Rows = @(); MaxLineLength = 0 }
    $Dash = -1
    for ($I = 0; $I -lt $Lines.Count; $I++) { if ($Lines[$I] -match '^-{10,}\s*$') { $Dash = $I; break } }
    if ($Dash -lt 0) { return [pscustomobject]$Out }
    for ($J = $Dash - 1; $J -ge 0; $J--) { if ($Lines[$J].Trim()) { $Out.Header = $Lines[$J]; break } }
    if ($null -ne $Out.Header) {
        $Starts = @()
        for ($K = 0; $K -lt $Out.Header.Length; $K++) {
            if ($Out.Header[$K] -ne ' ' -and ($K -eq 0 -or $Out.Header[$K - 1] -eq ' ')) { $Starts += $K }
        }
        $Out.ColumnStarts = @($Starts)
    }
    $Rows = @($Lines[($Dash + 1)..($Lines.Count - 1)] | Where-Object { $_ -and $_.Trim() })
    if ($Dash -eq $Lines.Count - 1) { $Rows = @() }
    $Out.RowCount = $Rows.Count
    $Out.Rows = @($Rows | Select-Object -First 25)
    foreach ($Row in $Rows) {
        if ($Row.Length -gt $Out.MaxLineLength) { $Out.MaxLineLength = $Row.Length }
        if (@($Out.ColumnStarts).Count -ge 3) {
            $IdStart = [int]$Out.ColumnStarts[1]
            $IdEnd = [int]$Out.ColumnStarts[2]
            $NamePart = if ($Row.Length -gt $IdStart) { $Row.Substring(0, $IdStart) } else { $Row }
            $IdPart = if ($Row.Length -gt $IdStart) { $Row.Substring($IdStart, [math]::Min($IdEnd, $Row.Length) - $IdStart) } else { '' }
            if ($NamePart.Contains($Ellipsis)) { $Out.TruncatedNameRows++ }
            if ($IdPart.Contains($Ellipsis)) { $Out.TruncatedIdRows++ }
        }
    }
    return [pscustomobject]$Out
}

function Get-E4CUninstallSnapshot {
    # Wpisy Uninstall (tylko odczyt, API .NET): HKLM widok 64, HKLM widok 32 (WOW6432Node) i HKU\<SID konta>.
    [CmdletBinding()]
    param([Parameter(Mandatory)][string]$UserSid)
    $Sources = @(
        @{ Branch = 'HKLM'; Hive = 'LocalMachine'; View = 'Registry64'; Path = 'SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall'; Prefix = 'HKLM\SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall' }
        @{ Branch = 'HKLM32'; Hive = 'LocalMachine'; View = 'Registry32'; Path = 'SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall'; Prefix = 'HKLM\SOFTWARE\WOW6432Node\Microsoft\Windows\CurrentVersion\Uninstall' }
        @{ Branch = 'HKU'; Hive = 'Users'; View = 'Default'; Path = "$UserSid\Software\Microsoft\Windows\CurrentVersion\Uninstall"; Prefix = "HKU\$UserSid\Software\Microsoft\Windows\CurrentVersion\Uninstall" }
    )
    $Entries = New-Object System.Collections.Generic.List[object]
    foreach ($Source in $Sources) {
        $Base = [Microsoft.Win32.RegistryKey]::OpenBaseKey([Microsoft.Win32.RegistryHive]$Source.Hive, [Microsoft.Win32.RegistryView]$Source.View)
        try {
            $Root = $Base.OpenSubKey($Source.Path, $false)
            if ($null -ne $Root) {
                try {
                    foreach ($Name in $Root.GetSubKeyNames()) {
                        $Key = $Root.OpenSubKey($Name, $false)
                        if ($null -eq $Key) { continue }
                        try {
                            $Entries.Add([pscustomobject]@{
                                    Branch = $Source.Branch; KeyPath = "$($Source.Prefix)\$Name"; Key = $Name
                                    DisplayName = $Key.GetValue('DisplayName'); Publisher = $Key.GetValue('Publisher'); DisplayVersion = $Key.GetValue('DisplayVersion')
                                    SystemComponent = $Key.GetValue('SystemComponent'); ParentKeyName = $Key.GetValue('ParentKeyName'); WindowsInstaller = $Key.GetValue('WindowsInstaller')
                                    ReleaseType = $Key.GetValue('ReleaseType'); InstallLocation = $Key.GetValue('InstallLocation')
                                    UninstallString = $Key.GetValue('UninstallString'); QuietUninstallString = $Key.GetValue('QuietUninstallString')
                                })
                        }
                        finally { $Key.Dispose() }
                    }
                }
                finally { $Root.Dispose() }
            }
        }
        finally { $Base.Dispose() }
    }
    return $Entries.ToArray()
}

function Get-E4CAppxSnapshot {
    # Pakiety AppX konta i obrazu systemu (tylko odczyt); błąd odczytu = wpis Kind=Error zamiast wyjątku.
    [CmdletBinding()]
    param([Parameter(Mandatory)][string]$UserSid)
    $Out = New-Object System.Collections.Generic.List[object]
    try {
        foreach ($Package in @(Get-AppxPackage -User $UserSid -ErrorAction Stop)) {
            $Out.Add([pscustomobject]@{ Kind = 'User'; Key = "User|$($Package.PackageFullName)"; Name = $Package.Name; PackageFamilyName = $Package.PackageFamilyName; Version = [string]$Package.Version; Publisher = $Package.Publisher; IsFramework = [bool]$Package.IsFramework })
        }
    }
    catch { $Out.Add([pscustomobject]@{ Kind = 'Error'; Key = 'Error|User'; Name = (Format-E4CError $_); PackageFamilyName = $null; Version = $null; Publisher = $null; IsFramework = $null }) }
    try {
        foreach ($Package in @(Get-AppxProvisionedPackage -Online -ErrorAction Stop)) {
            $Out.Add([pscustomobject]@{ Kind = 'Provisioned'; Key = "Provisioned|$($Package.PackageName)"; Name = $Package.DisplayName; PackageFamilyName = $null; Version = [string]$Package.Version; Publisher = $Package.PublisherId; IsFramework = $null })
        }
    }
    catch { $Out.Add([pscustomobject]@{ Kind = 'Error'; Key = 'Error|Provisioned'; Name = (Format-E4CError $_); PackageFamilyName = $null; Version = $null; Publisher = $null; IsFramework = $null }) }
    return $Out.ToArray()
}

function Compare-E4CSnapshot {
    # Różnice po kluczu: Added (nowe), Removed (zniknęły), Changed (inne wartości wskazanych pól; obiekt z odczytu po).
    [CmdletBinding()]
    param(
        [Parameter(Mandatory)][AllowEmptyCollection()][object[]]$Before,
        [Parameter(Mandatory)][AllowEmptyCollection()][object[]]$After,
        [Parameter(Mandatory)][string]$KeyProperty,
        [Parameter(Mandatory)][string[]]$Properties
    )
    $BeforeMap = @{}
    foreach ($Item in $Before) { $BeforeMap[[string]$Item.$KeyProperty] = $Item }
    $AfterMap = @{}
    foreach ($Item in $After) { $AfterMap[[string]$Item.$KeyProperty] = $Item }
    $Added = @($After | Where-Object { -not $BeforeMap.ContainsKey([string]$_.$KeyProperty) })
    $Removed = @($Before | Where-Object { -not $AfterMap.ContainsKey([string]$_.$KeyProperty) })
    $Changed = @($After | Where-Object {
            $Old = $BeforeMap[[string]$_.$KeyProperty]
            if ($null -eq $Old) { return $false }
            $New = $_
            @($Properties | Where-Object { [string]$Old.$_ -ne [string]$New.$_ }).Count -gt 0
        })
    return [pscustomobject]@{ Added = $Added; Removed = $Removed; Changed = $Changed }
}

function Test-E4CNetwork {
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

function Get-E4CAdminApprovalState {
    # Wartości UAC / Administrator Protection (tylko odczyt).
    [CmdletBinding()]
    param([string]$Path = 'HKLM\SOFTWARE\Microsoft\Windows\CurrentVersion\Policies\System')
    $Out = [ordered]@{}
    foreach ($Name in 'TypeOfAdminApprovalMode', 'EnableLUA', 'ConsentPromptBehaviorAdmin', 'FilterAdministratorToken', 'PromptOnSecureDesktop') {
        $Out[$Name] = Read-ExploreRegistryValue -Path $Path -Name $Name
    }
    return [pscustomobject]$Out
}

function Get-E4CLocalAccounts {
    # Konta lokalne i profile (ProfileList) – tylko odczyt; ukryte konto Administrator Protection powinno się tu pokazać.
    [CmdletBinding()]
    param()
    $Users = @()
    try { $Users = @(Get-LocalUser -ErrorAction Stop | ForEach-Object { [pscustomobject]@{ Name = $_.Name; Sid = $_.SID.Value; Enabled = $_.Enabled; Description = $_.Description } }) }
    catch { $Users = @([pscustomobject]@{ Name = 'BLAD'; Sid = $null; Enabled = $null; Description = (Format-E4CError $_) }) }
    $Profiles = @()
    $List = Open-ExploreRegistryKey -Path 'HKLM\SOFTWARE\Microsoft\Windows NT\CurrentVersion\ProfileList'
    if ($null -ne $List) {
        try {
            foreach ($Sid in $List.GetSubKeyNames()) {
                $Profiles += [pscustomobject]@{ Sid = $Sid; ProfileImagePath = (Read-ExploreRegistryValue -Path "HKLM\SOFTWARE\Microsoft\Windows NT\CurrentVersion\ProfileList\$Sid" -Name 'ProfileImagePath').Value }
            }
        }
        finally { $List.Dispose() }
    }
    return [pscustomobject]@{ Users = $Users; Profiles = $Profiles }
}

function Get-E4CInstallerProcesses {
    # Procesy instalatorów i winget (tylko odczyt): czy coś nadal pracuje w tle.
    [CmdletBinding()]
    param()
    return @(Get-Process -ErrorAction SilentlyContinue | Where-Object { $_.ProcessName -match 'winget|AppInstaller|msiexec|setup|install|unins|bootstrapper' } |
            ForEach-Object { [pscustomobject]@{ Name = $_.ProcessName; Id = $_.Id; SessionId = $_.SessionId } })
}

function Read-E4CCliReport {
    # Raport JSON CLI (--out) – tylko odczyt; brak pliku = $null.
    [CmdletBinding()]
    param([Parameter(Mandatory)][string]$Path)
    if (-not [System.IO.File]::Exists($Path)) { return $null }
    return ([System.IO.File]::ReadAllText($Path, [System.Text.Encoding]::UTF8).TrimStart([char]0xFEFF) | ConvertFrom-Json)
}

function Read-E4CJournalInstallEntries {
    # Wpisy journala DSOptimizera z Kind = app-install (tylko odczyt): id pozycji, status, czas i zdarzenia (Started + wynik).
    [CmdletBinding()]
    param([Parameter(Mandatory)][string]$Path)
    if (-not [System.IO.File]::Exists($Path)) { return @() }
    $Document = [System.IO.File]::ReadAllText($Path, [System.Text.Encoding]::UTF8).TrimStart([char]0xFEFF) | ConvertFrom-Json
    return @(@($Document.entries) | Where-Object { $null -ne $_ -and [string]$_.kind -eq 'app-install' } | ForEach-Object {
            [pscustomobject]@{
                TweakId = [string]$_.tweakId; Title = [string]$_.title; Status = [string]$_.status; UserSid = [string]$_.userSid; AppliedAt = [string]$_.appliedAt
                Events  = @(@($_.events) | Where-Object { $null -ne $_ } | ForEach-Object { [pscustomobject]@{ At = [string]$_.at; Action = [string]$_.action; Outcome = [string]$_.outcome; Message = [string]$_.message } })
            }
        })
}

function Get-E4CRustDeskState {
    # Plan RustDesk: stan po serii CLI, wyłącznie odczyt – pliki w folderze pobrań programu (po każdym wyniku ma być pusty; poza
    # limitem msiexec MSI jest usuwany), logi MSI RustDesk (logs\winget\rustdesk-msi-*.log), usługa RustDesk i reguły zapory RustDesk*.
    # Błąd jednego odczytu trafia do Errors i nie przerywa pozostałych.
    [CmdletBinding()]
    param([Parameter(Mandatory)]$Plan)
    $Downloads = [string]$Plan.DownloadsDirectory
    $Out = [ordered]@{ DownloadsDirectory = $Downloads; DownloadsExists = $false; Downloads = @(); MsiLogs = @(); Service = $null; Firewall = @(); Errors = @() }
    try {
        $Out.DownloadsExists = [System.IO.Directory]::Exists($Downloads)
        if ($Out.DownloadsExists) {
            $Out.Downloads = @(Get-ChildItem -LiteralPath $Downloads -Recurse -Force -File -ErrorAction Stop | ForEach-Object { [pscustomobject]@{ Name = $_.FullName.Substring($Downloads.Length).TrimStart('\'); Length = $_.Length } })
        }
    }
    catch { $Out.Errors += "pobrania: $(Format-E4CError $_)" }
    try {
        $Logs = Join-Path ([string]$Plan.DsoLogDirectory) 'winget'
        if ([System.IO.Directory]::Exists($Logs)) {
            $Out.MsiLogs = @(Get-ChildItem -LiteralPath $Logs -Filter 'rustdesk-msi-*.log' -File -Force -ErrorAction Stop | ForEach-Object { [pscustomobject]@{ Name = $_.Name; Length = $_.Length } })
        }
    }
    catch { $Out.Errors += "logi MSI: $(Format-E4CError $_)" }
    try {
        $Service = Get-Service -Name 'RustDesk' -ErrorAction SilentlyContinue
        if ($null -ne $Service) { $Out.Service = [pscustomobject]@{ Name = [string]$Service.Name; DisplayName = [string]$Service.DisplayName; Status = [string]$Service.Status; StartType = [string]$Service.StartType } }
    }
    catch { $Out.Errors += "usługa: $(Format-E4CError $_)" }
    try {
        $Out.Firewall = @(Get-NetFirewallRule -DisplayName 'RustDesk*' -ErrorAction SilentlyContinue | Where-Object { $null -ne $_ } |
                ForEach-Object { [pscustomobject]@{ DisplayName = [string]$_.DisplayName; Direction = [string]$_.Direction; Action = [string]$_.Action; Enabled = [string]$_.Enabled } })
    }
    catch { $Out.Errors += "zapora: $(Format-E4CError $_)" }
    return [pscustomobject]$Out
}

# --- skutki (tylko w VM) ---

function Install-E4CRunner {
    # Katalogi pomiaru i runner zadania Harmonogramu (UTF-8 z BOM) w VM.
    [CmdletBinding()]
    param([Parameter(Mandatory)]$Plan)
    Assert-E4CInsideVm
    foreach ($Directory in @([string]$Plan.WorkDirectory, [string]$Plan.RawDirectory, (Join-Path ([string]$Plan.WorkDirectory) 'req'))) { $null = [System.IO.Directory]::CreateDirectory($Directory) }
    $Path = Join-Path ([string]$Plan.WorkDirectory) 'runner.ps1'
    [System.IO.File]::WriteAllText($Path, (Get-E4CRunnerScript), (New-Object System.Text.UTF8Encoding($true)))
    return $Path
}

function Invoke-E4CTool {
    # Jedno narzędzie (winget albo whoami) jako konto dso z zadania Harmonogramu (Interactive, RunLevel Highest/Limited), przez runner.
    # Zadanie bez limitu czasu (nic go nie przerywa); czekamy na plik wyniku najwyżej TimeoutSeconds + TaskGraceSeconds.
    # Zadanie, które nadal pracuje, NIE jest zatrzymywane ani wyrejestrowywane (revert VM je usunie).
    [CmdletBinding()]
    param(
        [Parameter(Mandatory)]$Plan,
        [Parameter(Mandatory)][ValidatePattern('^[A-Za-z0-9._-]+\z')][string]$Name,
        [Parameter(Mandatory)][ValidateSet('winget', 'whoami', 'dso')][string]$Tool,
        [Parameter(Mandatory)][AllowEmptyString()][string]$Arguments,
        [Parameter(Mandatory)][ValidateRange(1, 7200)][int]$TimeoutSeconds,
        [ValidateSet('Highest', 'Limited')][string]$RunLevel = 'Highest'
    )
    Assert-E4CInsideVm
    $Work = [string]$Plan.WorkDirectory
    $Raw = [string]$Plan.RawDirectory
    $RequestDirectory = Join-Path $Work 'req'
    foreach ($Directory in @($Work, $Raw, $RequestDirectory)) { $null = [System.IO.Directory]::CreateDirectory($Directory) }
    $RequestFile = Join-Path $RequestDirectory "$Name.json"
    $Runner = Join-Path $Work 'runner.ps1'
    Assert-E4CPlainPath -Path $RequestFile
    Assert-E4CPlainPath -Path $Runner
    $Request = [ordered]@{
        Name = $Name; Tool = $Tool; Arguments = $Arguments; TimeoutSeconds = $TimeoutSeconds
        OutFile = (Join-Path $Raw "$Name.out.bin"); ErrFile = (Join-Path $Raw "$Name.err.bin"); ResultFile = (Join-Path $Raw "$Name.result.json"); LateFile = (Join-Path $Raw "$Name.late.json")
    }
    [System.IO.File]::WriteAllText($RequestFile, ($Request | ConvertTo-Json), (New-Object System.Text.UTF8Encoding($false)))
    $TaskName = "DSO-E4C-$Name"
    $Out = [ordered]@{
        Name = $Name; Tool = $Tool; Arguments = $Arguments; RunLevel = $RunLevel; TaskName = $TaskName; TimeoutSeconds = $TimeoutSeconds
        ExitCode = $null; Hex = $null; TimedOut = $false; WaitExpired = $false; TaskLeftRunning = $false; Seconds = $null
        Sid = $null; Account = $null; Elevated = $null; FilePath = $null; FileExists = $null; LocalAppData = $null; RunnerError = $null; Error = $null
        TaskLastResult = $null; OutFile = "$Name.out.bin"; ErrFile = "$Name.err.bin"; Output = $null; Stderr = $null
    }
    $Registered = $false
    $Watch = [System.Diagnostics.Stopwatch]::StartNew()
    try {
        $Executable = Join-Path $env:SystemRoot 'System32\WindowsPowerShell\v1.0\powershell.exe'
        $Action = New-ScheduledTaskAction -Execute $Executable -Argument "-NoProfile -ExecutionPolicy Bypass -WindowStyle Hidden -File $Runner -Request $RequestFile"
        $Principal = New-ScheduledTaskPrincipal -UserId ([string]$Plan.UserName) -LogonType Interactive -RunLevel $RunLevel
        $Settings = New-ScheduledTaskSettingsSet -AllowStartIfOnBatteries -DontStopIfGoingOnBatteries -ExecutionTimeLimit ([TimeSpan]::Zero)
        $Registered = $true
        $null = Register-ScheduledTask -TaskName $TaskName -Action $Action -Principal $Principal -Settings $Settings -Force -ErrorAction Stop
        Start-ScheduledTask -TaskName $TaskName -ErrorAction Stop
        $Limit = $TimeoutSeconds + [int]$Plan.TaskGraceSeconds
        while ($true) {
            if ([System.IO.File]::Exists([string]$Request.ResultFile)) { break }
            Start-Sleep -Seconds ([int]$Plan.TaskPollSeconds)
            if ([System.IO.File]::Exists([string]$Request.ResultFile)) { break }
            $Elapsed = Get-E4CElapsedSeconds -Watch $Watch
            $State = [string](Get-ScheduledTask -TaskName $TaskName -ErrorAction Stop).State
            if ($State -ne 'Running' -and $State -ne 'Queued' -and $Elapsed -ge 30) { $Out.Error = "zadanie zakończone bez pliku wyniku (stan $State)"; break }
            if ($Elapsed -ge $Limit) { $Out.WaitExpired = $true; break }
        }
    }
    catch { $Out.Error = Format-E4CError $_ }
    finally {
        if ($Registered) {
            try {
                $Info = Get-ScheduledTaskInfo -TaskName $TaskName -ErrorAction SilentlyContinue
                if ($null -ne $Info) { $Out.TaskLastResult = Format-E4CHex -Code ([int64]$Info.LastTaskResult) }
                $Task = Get-ScheduledTask -TaskName $TaskName -ErrorAction SilentlyContinue
                if ($null -ne $Task -and [string]$Task.State -eq 'Running') { $Out.TaskLeftRunning = $true }
                else { Unregister-ScheduledTask -TaskName $TaskName -Confirm:$false -ErrorAction Stop }
            }
            catch { $Out.Error = (@($Out.Error, "wyrejestrowanie: $(Format-E4CError $_)") | Where-Object { $_ }) -join ' | ' }
        }
    }
    if ([System.IO.File]::Exists([string]$Request.ResultFile)) {
        $Result = Get-Content -LiteralPath ([string]$Request.ResultFile) -Raw -Encoding UTF8 | ConvertFrom-Json
        foreach ($Field in 'ExitCode', 'TimedOut', 'Seconds', 'Sid', 'Account', 'Elevated', 'FilePath', 'FileExists', 'LocalAppData') {
            if ($Result.PSObject.Properties[$Field]) { $Out[$Field] = $Result.$Field }
        }
        if ($Result.PSObject.Properties['Error']) { $Out.RunnerError = $Result.Error }
    }
    $Out.Hex = Format-E4CHex -Code $Out.ExitCode
    $CodePage = if ($Tool -eq 'whoami') { [Globalization.CultureInfo]::CurrentCulture.TextInfo.OEMCodePage } else { 65001 }
    if ([System.IO.File]::Exists([string]$Request.OutFile)) { $Out.Output = ConvertFrom-E4COutput -Bytes ([System.IO.File]::ReadAllBytes([string]$Request.OutFile)) -CodePage $CodePage }
    else { $Out.Output = ConvertFrom-E4COutput -Bytes ([byte[]]@()) -CodePage $CodePage }
    if ([System.IO.File]::Exists([string]$Request.ErrFile)) { $Out.Stderr = ConvertFrom-E4COutput -Bytes ([System.IO.File]::ReadAllBytes([string]$Request.ErrFile)) -CodePage $CodePage }
    return [pscustomobject]$Out
}

function Invoke-E4CInstallItem {
    # Jedna aplikacja pełnym poleceniem programu: Uninstall + AppX przed, install (z --locale; przy 0x8A150010 jedna próba bez),
    # Uninstall + AppX po (diff, gałąź, wpisy widoczne), list --id --exact w dwóch wariantach.
    [CmdletBinding()]
    param(
        [Parameter(Mandatory)]$Plan,
        [Parameter(Mandatory)][string]$UserSid,
        [Parameter(Mandatory)][string]$WingetId,
        [Parameter(Mandatory)][ValidatePattern('^[a-z0-9-]+\z')][string]$Label,
        [switch]$NoLocale,
        [switch]$NoList
    )
    Assert-E4CInsideVm
    if (-not (Test-E4CWingetId -Id $WingetId)) { throw "Nieprawidłowy identyfikator winget '$WingetId'." }
    $Safe = Get-E4CSafeName -Text $WingetId
    $UninstallBefore = @(Get-E4CUninstallSnapshot -UserSid $UserSid)
    $AppxBefore = @(Get-E4CAppxSnapshot -UserSid $UserSid)
    $FreeBefore = Get-E4CFreeBytes
    $Locale = if ($NoLocale) { '' } else { [string]$Plan.Locale }
    $Base = "$Label-install-$Safe"
    $Attempts = @()
    $Attempts += Invoke-E4CTool -Plan $Plan -Name $Base -Tool 'winget' -TimeoutSeconds ([int]$Plan.InstallTimeoutSeconds) `
        -Arguments (Get-E4CInstallArguments -WingetId $WingetId -Locale $Locale -LogPath (Join-Path ([string]$Plan.RawDirectory) "$Base.winget.log"))
    $Fallback = $false
    if ($Locale -and [string]$Attempts[0].Hex -eq '0x8A150010') {
        $Fallback = $true
        $Retry = "$Base-nolocale"
        $Attempts += Invoke-E4CTool -Plan $Plan -Name $Retry -Tool 'winget' -TimeoutSeconds ([int]$Plan.InstallTimeoutSeconds) `
            -Arguments (Get-E4CInstallArguments -WingetId $WingetId -Locale '' -LogPath (Join-Path ([string]$Plan.RawDirectory) "$Retry.winget.log"))
    }
    $Last = $Attempts[-1]
    $UninstallAfter = @(Get-E4CUninstallSnapshot -UserSid $UserSid)
    $AppxAfter = @(Get-E4CAppxSnapshot -UserSid $UserSid)
    $Uninstall = Compare-E4CSnapshot -Before $UninstallBefore -After $UninstallAfter -KeyProperty 'KeyPath' -Properties @('DisplayName', 'DisplayVersion', 'Publisher', 'SystemComponent', 'ParentKeyName')
    $Appx = Compare-E4CSnapshot -Before $AppxBefore -After $AppxAfter -KeyProperty 'Key' -Properties @('Version')
    $Visible = @($Uninstall.Added | Where-Object { [string]$_.SystemComponent -ne '1' -and -not $_.ParentKeyName })
    $Branches = @($Uninstall.Added | ForEach-Object { $_.Branch } | Select-Object -Unique)
    $Lists = @()
    if (-not $NoList) {
        $Lists += Invoke-E4CTool -Plan $Plan -Name "$Label-list-$Safe-source" -Tool 'winget' -Arguments (Get-E4CListArguments -WingetId $WingetId -WithSource $true) -TimeoutSeconds ([int]$Plan.CommandTimeoutSeconds)
        $Lists += Invoke-E4CTool -Plan $Plan -Name "$Label-list-$Safe-plain" -Tool 'winget' -Arguments (Get-E4CListArguments -WingetId $WingetId -WithSource $false) -TimeoutSeconds ([int]$Plan.CommandTimeoutSeconds)
    }
    $Seconds = 0
    foreach ($Attempt in $Attempts) { if ($null -ne $Attempt.Seconds) { $Seconds += [double]$Attempt.Seconds } }
    return [pscustomobject]@{
        WingetId = $WingetId; Label = $Label; Attempts = $Attempts; LocaleFallback = $Fallback; FinalExitCode = $Last.ExitCode; FinalHex = $Last.Hex; TimedOut = $Last.TimedOut
        Seconds = [math]::Round($Seconds, 1); Uninstall = $Uninstall; Appx = $Appx; Visible = $Visible; Branches = $Branches; Lists = $Lists
        FreeBefore = $FreeBefore; FreeAfter = (Get-E4CFreeBytes)
    }
}

function Set-E4CAdminApprovalMode {
    # TypeOfAdminApprovalMode (HKLM 64-bit, API .NET): ustawienie wartości albo usunięcie (gdy jej nie było przed pomiarem).
    [CmdletBinding(DefaultParameterSetName = 'Set')]
    param(
        [Parameter(Mandatory, ParameterSetName = 'Set')][ValidateRange(0, 2)][int]$Value,
        [Parameter(Mandatory, ParameterSetName = 'Remove')][switch]$Remove
    )
    Assert-E4CInsideVm
    $Base = [Microsoft.Win32.RegistryKey]::OpenBaseKey([Microsoft.Win32.RegistryHive]::LocalMachine, [Microsoft.Win32.RegistryView]::Registry64)
    try {
        $Key = $Base.OpenSubKey('SOFTWARE\Microsoft\Windows\CurrentVersion\Policies\System', $true)
        if ($null -eq $Key) { throw 'Brak klucza Policies\System.' }
        try {
            if ($Remove) { $Key.DeleteValue('TypeOfAdminApprovalMode', $false) }
            else { $Key.SetValue('TypeOfAdminApprovalMode', $Value, [Microsoft.Win32.RegistryValueKind]::DWord) }
        }
        finally { $Key.Dispose() }
    }
    finally { $Base.Dispose() }
}

function Restart-E4CVm {
    # Restart VM (faza adminprotection: nowa wartość TypeOfAdminApprovalMode działa po restarcie).
    [CmdletBinding()]
    param()
    Assert-E4CInsideVm
    Restart-Computer -Force
}

function Invoke-E4CBaseline {
    # Stan świeżej VM bez sieci: wersja i --info winget (jako dso), UAC / Administrator Protection, język, App Installer, Uninstall, konta.
    [CmdletBinding()]
    param(
        [Parameter(Mandatory)]$Plan,
        [Parameter(Mandatory)][string]$UserSid
    )
    Assert-E4CInsideVm
    $AppInstaller = $null
    try { $AppInstaller = @(Get-AppxPackage -User $UserSid -Name 'Microsoft.DesktopAppInstaller' -ErrorAction Stop | ForEach-Object { [string]$_.Version }) -join ',' } catch { $AppInstaller = Format-E4CError $_ }
    $SystemLocale = $null
    try { $SystemLocale = (Get-WinSystemLocale).Name } catch { $SystemLocale = Format-E4CError $_ }
    $Uninstall = @(Get-E4CUninstallSnapshot -UserSid $UserSid)
    return [ordered]@{
        Connectivity       = Test-E4CNetwork -Name ([string]$Plan.NetworkName) -WaitSeconds 0
        Version            = Invoke-E4CTool -Plan $Plan -Name 'base-version' -Tool 'winget' -Arguments '--version' -TimeoutSeconds ([int]$Plan.CommandTimeoutSeconds)
        Info               = Invoke-E4CTool -Plan $Plan -Name 'base-info' -Tool 'winget' -Arguments '--info' -TimeoutSeconds ([int]$Plan.CommandTimeoutSeconds)
        AdminApproval      = Get-E4CAdminApprovalState -Path ([string]$Plan.AdminApprovalKey)
        InstalledUICulture = [Globalization.CultureInfo]::InstalledUICulture.Name
        SystemLocale       = $SystemLocale
        AppInstaller       = $AppInstaller
        UninstallCount     = $Uninstall.Count
        Uninstall          = $Uninstall
        AppxCount          = @(Get-E4CAppxSnapshot -UserSid $UserSid).Count
        Accounts           = Get-E4CLocalAccounts
        FreeBytes          = Get-E4CFreeBytes
    }
}

function Invoke-E4COfflineFresh {
    # Bez sieci, źródło nigdy niepobrane: install (pełne polecenie), search, list --id --exact (z --source winget i bez).
    [CmdletBinding()]
    param(
        [Parameter(Mandatory)]$Plan,
        [Parameter(Mandatory)][string]$UserSid
    )
    Assert-E4CInsideVm
    $Id = [string]$Plan.OfflineFreshInstall
    $Safe = Get-E4CSafeName -Text $Id
    return [ordered]@{
        Connectivity = Test-E4CNetwork -Name ([string]$Plan.NetworkName) -WaitSeconds 0
        Install      = Invoke-E4CInstallItem -Plan $Plan -UserSid $UserSid -WingetId $Id -Label 'off' -NoList
        Search       = Invoke-E4CTool -Plan $Plan -Name "off-search-$(Get-E4CSafeName -Text ([string]$Plan.OfflineSearch))" -Tool 'winget' -Arguments (Get-E4CSearchArguments -Query ([string]$Plan.OfflineSearch)) -TimeoutSeconds ([int]$Plan.CommandTimeoutSeconds)
        ListSource   = Invoke-E4CTool -Plan $Plan -Name "off-list-$Safe-source" -Tool 'winget' -Arguments (Get-E4CListArguments -WingetId $Id -WithSource $true) -TimeoutSeconds ([int]$Plan.CommandTimeoutSeconds)
        ListPlain    = Invoke-E4CTool -Plan $Plan -Name "off-list-$Safe-plain" -Tool 'winget' -Arguments (Get-E4CListArguments -WingetId $Id -WithSource $false) -TimeoutSeconds ([int]$Plan.CommandTimeoutSeconds)
    }
}

function Invoke-E4CNetworkProbe {
    # Z siecią: show każdej pozycji listy i odrzuconych, długie wyszukiwania (obcinanie „…”), --locale (Firefox, LibreOffice),
    # --no-upgrade (7-Zip dwa razy), kandydat spoza listy (instalacja tylko, gdy jest w wynikach wyszukiwania).
    [CmdletBinding()]
    param(
        [Parameter(Mandatory)]$Plan,
        [Parameter(Mandatory)][string]$UserSid
    )
    Assert-E4CInsideVm
    $Connectivity = Test-E4CNetwork -Name ([string]$Plan.NetworkName) -WaitSeconds ([int]$Plan.NetworkWaitSeconds)
    if (-not $Connectivity.Resolved) { throw "Brak sieci w VM: '$($Plan.NetworkName)' nie rozwiązuje się po $($Connectivity.Seconds) s; nic nie uruchomiono." }
    $Command = [int]$Plan.CommandTimeoutSeconds
    $Out = [ordered]@{ Connectivity = $Connectivity; Version = $null; Shows = @(); Searches = @(); Locale = @(); NoUpgrade = @(); Candidate = $null; Errors = @() }
    try { $Out.Version = Invoke-E4CTool -Plan $Plan -Name 'net-version' -Tool 'winget' -Arguments '--version' -TimeoutSeconds $Command } catch { $Out.Errors += "version: $(Format-E4CError $_)" }
    foreach ($Id in (@($Plan.Apps | ForEach-Object { [string]$_.WingetId }) + @($Plan.RejectedShow))) {
        try {
            $Show = Invoke-E4CTool -Plan $Plan -Name "net-show-$(Get-E4CSafeName -Text $Id)" -Tool 'winget' -Arguments (Get-E4CShowArguments -WingetId $Id) -TimeoutSeconds $Command
            $Show | Add-Member -NotePropertyName WingetId -NotePropertyValue $Id -Force
            $Show | Add-Member -NotePropertyName Fields -NotePropertyValue (Get-E4CShowFields -Lines @($Show.Output.Lines)) -Force
            $Out.Shows += $Show
        }
        catch { $Out.Errors += "show ${Id}: $(Format-E4CError $_)" }
    }
    $CandidateSearch = $null
    foreach ($Query in (@($Plan.LongSearches) + @($Plan.ExtraSearches) + @([string]$Plan.SearchCandidate.Query))) {
        try {
            $Search = Invoke-E4CTool -Plan $Plan -Name "net-search-$(Get-E4CSafeName -Text $Query)" -Tool 'winget' -Arguments (Get-E4CSearchArguments -Query $Query) -TimeoutSeconds $Command
            $Search | Add-Member -NotePropertyName Query -NotePropertyValue $Query -Force
            $Search | Add-Member -NotePropertyName Summary -NotePropertyValue (Get-E4CSearchSummary -Lines @($Search.Output.Lines)) -Force
            $Out.Searches += $Search
            if ($Query -eq [string]$Plan.SearchCandidate.Query) { $CandidateSearch = $Search }
        }
        catch { $Out.Errors += "search ${Query}: $(Format-E4CError $_)" }
    }
    foreach ($Id in @($Plan.LocaleProbe)) {
        try { $Out.Locale += Invoke-E4CInstallItem -Plan $Plan -UserSid $UserSid -WingetId ([string]$Id) -Label 'net-locale' } catch { $Out.Errors += "locale ${Id}: $(Format-E4CError $_)" }
    }
    foreach ($Round in 1, 2) {
        try { $Out.NoUpgrade += Invoke-E4CInstallItem -Plan $Plan -UserSid $UserSid -WingetId ([string]$Plan.NoUpgradeProbe) -Label "net-noupgrade-$Round" } catch { $Out.Errors += "no-upgrade ${Round}: $(Format-E4CError $_)" }
    }
    $CandidateId = [string]$Plan.SearchCandidate.WingetId
    $Pattern = "(^|\s)$([regex]::Escape($CandidateId))(\s|$)"
    $Found = ($null -ne $CandidateSearch) -and (@($CandidateSearch.Output.Lines | Where-Object { $_ -match $Pattern }).Count -gt 0)
    if ($Found) {
        try { $Out.Candidate = Invoke-E4CInstallItem -Plan $Plan -UserSid $UserSid -WingetId $CandidateId -Label 'net-candidate' } catch { $Out.Errors += "candidate: $(Format-E4CError $_)" }
    }
    else { $Out.Candidate = [pscustomobject]@{ WingetId = $CandidateId; Skipped = "brak $CandidateId w wynikach wyszukiwania '$($Plan.SearchCandidate.Query)'" } }
    return $Out
}

function Invoke-E4CInstallAll {
    # Z siecią: każda pozycja z Plan.InstallIds po kolei, jedno wywołanie instalacji na aplikację (limit 30 min, bez zabijania).
    [CmdletBinding()]
    param(
        [Parameter(Mandatory)]$Plan,
        [Parameter(Mandatory)][string]$UserSid
    )
    Assert-E4CInsideVm
    $Connectivity = Test-E4CNetwork -Name ([string]$Plan.NetworkName) -WaitSeconds ([int]$Plan.NetworkWaitSeconds)
    $Items = @()
    foreach ($Id in @($Plan.InstallIds)) {
        $Category = @($Plan.Apps | Where-Object { [string]$_.WingetId -eq [string]$Id } | ForEach-Object { [string]$_.Category })
        $CategoryName = if ($Category.Count -gt 0) { $Category[0] } else { $null }
        try { $Item = Invoke-E4CInstallItem -Plan $Plan -UserSid $UserSid -WingetId ([string]$Id) -Label 'all' }
        catch { $Item = [pscustomobject]@{ WingetId = [string]$Id; Error = (Format-E4CError $_) } }
        $Item | Add-Member -NotePropertyName Category -NotePropertyValue $CategoryName -Force
        $Items += $Item
    }
    return [pscustomobject]@{ Connectivity = $Connectivity; Items = $Items; FreeBytes = (Get-E4CFreeBytes) }
}

function Invoke-E4CAdminProtectionSet {
    # Pro: zapis wartości sprzed zmiany (plik w VM, przetrwa restart), TypeOfAdminApprovalMode = 2. Restart robi host.
    [CmdletBinding()]
    param([Parameter(Mandatory)]$Plan)
    Assert-E4CInsideVm
    $Key = [string]$Plan.AdminApprovalKey
    $Before = Read-ExploreRegistryValue -Path $Key -Name 'TypeOfAdminApprovalMode'
    $null = [System.IO.Directory]::CreateDirectory([string]$Plan.WorkDirectory)
    [System.IO.File]::WriteAllText((Join-Path ([string]$Plan.WorkDirectory) 'ap-before.json'), ($Before | ConvertTo-Json), (New-Object System.Text.UTF8Encoding($false)))
    Set-E4CAdminApprovalMode -Value ([int]$Plan.AdminProtectionValue)
    return [pscustomobject]@{ Before = $Before; After = (Read-ExploreRegistryValue -Path $Key -Name 'TypeOfAdminApprovalMode'); Accounts = (Get-E4CLocalAccounts) }
}

function Invoke-E4CAdminProtectionProbe {
    # Pro po restarcie z TypeOfAdminApprovalMode = 2: whoami /user /groups i winget --version z zadania Highest i Limited jako dso.
    # SID procesu (runner) porównany z SID dso; konta lokalne i ProfileList (ukryte konto), wartości rejestru.
    [CmdletBinding()]
    param(
        [Parameter(Mandatory)]$Plan,
        [Parameter(Mandatory)][string]$UserSid
    )
    Assert-E4CInsideVm
    $Runs = [ordered]@{}
    foreach ($Level in 'Highest', 'Limited') {
        $Lower = $Level.ToLowerInvariant()
        $Runs["Whoami$Level"] = Invoke-E4CTool -Plan $Plan -Name "ap-whoami-$Lower" -Tool 'whoami' -Arguments '/user /groups' -TimeoutSeconds 120 -RunLevel $Level
        $Runs["Version$Level"] = Invoke-E4CTool -Plan $Plan -Name "ap-version-$Lower" -Tool 'winget' -Arguments '--version' -TimeoutSeconds ([int]$Plan.CommandTimeoutSeconds) -RunLevel $Level
    }
    $HighestSid = $Runs['WhoamiHighest'].Sid
    $LimitedSid = $Runs['WhoamiLimited'].Sid
    return [pscustomobject]@{
        UserSid = $UserSid; AdminApproval = (Get-E4CAdminApprovalState -Path ([string]$Plan.AdminApprovalKey)); Accounts = (Get-E4CLocalAccounts)
        HighestSid = $HighestSid; LimitedSid = $LimitedSid
        HighestSidDiffers = [bool]($HighestSid -and $HighestSid -ne $UserSid); LimitedSidDiffers = [bool]($LimitedSid -and $LimitedSid -ne $UserSid)
        Runs = [pscustomobject]$Runs
    }
}

function Invoke-E4CAdminProtectionRestore {
    # Pro: przywrócenie TypeOfAdminApprovalMode sprzed pomiaru (z pliku ap-before.json). Restart robi host; revert i tak przywraca checkpoint.
    [CmdletBinding()]
    param([Parameter(Mandatory)]$Plan)
    Assert-E4CInsideVm
    $File = Join-Path ([string]$Plan.WorkDirectory) 'ap-before.json'
    if (-not [System.IO.File]::Exists($File)) { throw "Brak ${File}: nie wiadomo, do czego przywrócić (revert VM przywróci checkpoint)." }
    $Before = Get-Content -LiteralPath $File -Raw -Encoding UTF8 | ConvertFrom-Json
    if ($Before.Exists) { Set-E4CAdminApprovalMode -Value ([int]$Before.Value) } else { Set-E4CAdminApprovalMode -Remove }
    return [pscustomobject]@{ Before = $Before; After = (Read-ExploreRegistryValue -Path ([string]$Plan.AdminApprovalKey) -Name 'TypeOfAdminApprovalMode') }
}

function Invoke-E4CFinal {
    # Bez sieci, źródło już pobrane: install (pakiet spoza pomiaru), search, show, list (także per user), procesy, wyniki spóźnione.
    [CmdletBinding()]
    param(
        [Parameter(Mandatory)]$Plan,
        [Parameter(Mandatory)][string]$UserSid
    )
    Assert-E4CInsideVm
    $Command = [int]$Plan.CommandTimeoutSeconds
    $Lists = @()
    foreach ($Id in @($Plan.FinalListIds)) {
        $Safe = Get-E4CSafeName -Text ([string]$Id)
        $Lists += Invoke-E4CTool -Plan $Plan -Name "final-list-$Safe-source" -Tool 'winget' -Arguments (Get-E4CListArguments -WingetId ([string]$Id) -WithSource $true) -TimeoutSeconds $Command
        $Lists += Invoke-E4CTool -Plan $Plan -Name "final-list-$Safe-plain" -Tool 'winget' -Arguments (Get-E4CListArguments -WingetId ([string]$Id) -WithSource $false) -TimeoutSeconds $Command
    }
    $Late = @(Get-ChildItem -LiteralPath ([string]$Plan.RawDirectory) -Filter '*.late.json' -ErrorAction SilentlyContinue | ForEach-Object { Get-Content -LiteralPath $_.FullName -Raw -Encoding UTF8 | ConvertFrom-Json })
    return [ordered]@{
        Connectivity = Test-E4CNetwork -Name ([string]$Plan.NetworkName) -WaitSeconds 0
        Install      = Invoke-E4CInstallItem -Plan $Plan -UserSid $UserSid -WingetId ([string]$Plan.FinalInstall) -Label 'final' -NoList
        Search       = Invoke-E4CTool -Plan $Plan -Name "final-search-$(Get-E4CSafeName -Text ([string]$Plan.OfflineSearch))" -Tool 'winget' -Arguments (Get-E4CSearchArguments -Query ([string]$Plan.OfflineSearch)) -TimeoutSeconds $Command
        Show         = Invoke-E4CTool -Plan $Plan -Name "final-show-$(Get-E4CSafeName -Text ([string]$Plan.FinalShow))" -Tool 'winget' -Arguments (Get-E4CShowArguments -WingetId ([string]$Plan.FinalShow)) -TimeoutSeconds $Command
        Lists        = $Lists
        Processes    = Get-E4CInstallerProcesses
        Late         = $Late
        FreeBytes    = Get-E4CFreeBytes
    }
}

function Copy-E4CDsoLogs {
    # Journal, log DSOptimizera i logi winget (--log) z C:\ProgramData\DSOptimizer do katalogu surowych plików (płasko, z przedrostkiem),
    # żeby host skopiował je razem z raportami CLI. Tylko kopie; oryginały zostają.
    [CmdletBinding()]
    param([Parameter(Mandatory)]$Plan)
    Assert-E4CInsideVm
    $Raw = [string]$Plan.RawDirectory
    $null = [System.IO.Directory]::CreateDirectory($Raw)
    $Copied = @()
    if ([System.IO.File]::Exists([string]$Plan.JournalPath)) {
        Copy-Item -LiteralPath ([string]$Plan.JournalPath) -Destination (Join-Path $Raw 'journal.json') -Force
        $Copied += 'journal.json'
    }
    $Logs = [string]$Plan.DsoLogDirectory
    foreach ($Source in @(@{ Directory = $Logs; Prefix = 'dsolog-' }, @{ Directory = (Join-Path $Logs 'winget'); Prefix = 'wingetlog-' })) {
        if (-not [System.IO.Directory]::Exists($Source.Directory)) { continue }
        foreach ($File in @(Get-ChildItem -LiteralPath $Source.Directory -File -ErrorAction SilentlyContinue)) {
            $Name = $Source.Prefix + (Get-E4CSafeName -Text $File.Name)
            Copy-Item -LiteralPath $File.FullName -Destination (Join-Path $Raw $Name) -Force
            $Copied += $Name
        }
    }
    return $Copied
}

function Invoke-E4CCliInstall {
    # Task 11: seria instalacji przez CLI z siecią. winget --version (przed), Uninstall przed, DSOptimizer.exe --install-apps <CliIds>
    # jako dso z zadania Harmonogramu (Interactive + Highest, runner bez zabijania), raport, --list-install (jako dso), raport, wpisy
    # journala app-install, Uninstall po (diff), winget --version (po; App Installer bywa aktualizowany w trakcie), kopie logów.
    [CmdletBinding()]
    param(
        [Parameter(Mandatory)]$Plan,
        [Parameter(Mandatory)][string]$UserSid
    )
    Assert-E4CInsideVm
    $Ids = @($Plan.CliIds | Where-Object { $_ })
    if ($Ids.Count -eq 0) { throw 'Plan bez CliIds: nie ma czego instalować (nic nie uruchomiono).' }
    $Connectivity = Test-E4CNetwork -Name ([string]$Plan.NetworkName) -WaitSeconds ([int]$Plan.NetworkWaitSeconds)
    if (-not $Connectivity.Resolved) { throw "Brak sieci w VM: '$($Plan.NetworkName)' nie rozwiązuje się po $($Connectivity.Seconds) s; nic nie uruchomiono." }
    $Raw = [string]$Plan.RawDirectory
    $Command = [int]$Plan.CommandTimeoutSeconds
    $InstallOut = Join-Path $Raw 'cli-install.json'
    $ListOut = Join-Path $Raw 'cli-list.json'
    $Out = [ordered]@{ Connectivity = $Connectivity; CliIds = $Ids; FreeBefore = (Get-E4CFreeBytes) }
    # RunLevel jawnie (minor T11): CLI i winget jako dso z zadania Harmonogramu Interactive + Highest, jak właściciel laptopa po UAC.
    $Out.VersionBefore = Invoke-E4CTool -Plan $Plan -Name 'cli-version-before' -Tool 'winget' -Arguments '--version' -TimeoutSeconds $Command -RunLevel 'Highest'
    $UninstallBefore = @(Get-E4CUninstallSnapshot -UserSid $UserSid)
    $Out.Install = Invoke-E4CTool -Plan $Plan -Name 'cli-install' -Tool 'dso' -Arguments (Get-E4CCliArguments -Command 'install-apps' -AppIds $Ids -OutPath $InstallOut) -TimeoutSeconds ([int]$Plan.CliInstallTimeoutSeconds) -RunLevel 'Highest'
    $Out.InstallReport = Read-E4CCliReport -Path $InstallOut
    $Out.List = Invoke-E4CTool -Plan $Plan -Name 'cli-list' -Tool 'dso' -Arguments (Get-E4CCliArguments -Command 'list-install' -OutPath $ListOut) -TimeoutSeconds $Command -RunLevel 'Highest'
    $Out.ListReport = Read-E4CCliReport -Path $ListOut
    if ([bool]$Plan.CliRepeat) {
        # -CliRepeat (plan RustDesk): druga seria z tymi samymi id po liście – pozycja już zainstalowana nie startuje ani nie pobiera.
        $AgainOut = Join-Path $Raw 'cli-install-again.json'
        $Out.InstallAgain = Invoke-E4CTool -Plan $Plan -Name 'cli-install-again' -Tool 'dso' -Arguments (Get-E4CCliArguments -Command 'install-apps' -AppIds $Ids -OutPath $AgainOut) -TimeoutSeconds ([int]$Plan.CliInstallTimeoutSeconds) -RunLevel 'Highest'
        $Out.InstallAgainReport = Read-E4CCliReport -Path $AgainOut
    }
    $Out.Journal = @(Read-E4CJournalInstallEntries -Path ([string]$Plan.JournalPath))
    $UninstallAfter = @(Get-E4CUninstallSnapshot -UserSid $UserSid)
    $Uninstall = Compare-E4CSnapshot -Before $UninstallBefore -After $UninstallAfter -KeyProperty 'KeyPath' -Properties @('DisplayName', 'DisplayVersion', 'Publisher', 'SystemComponent', 'ParentKeyName')
    $Out.Uninstall = $Uninstall
    $Out.Visible = @($Uninstall.Added | Where-Object { [string]$_.SystemComponent -ne '1' -and -not $_.ParentKeyName })
    $Out.RustDesk = Get-E4CRustDeskState -Plan $Plan
    $Out.VersionAfter = Invoke-E4CTool -Plan $Plan -Name 'cli-version-after' -Tool 'winget' -Arguments '--version' -TimeoutSeconds $Command -RunLevel 'Highest'
    $Out.Processes = Get-E4CInstallerProcesses
    $Out.FreeAfter = Get-E4CFreeBytes
    $Out.Logs = @(Copy-E4CDsoLogs -Plan $Plan)
    return $Out
}

function Invoke-E4CCollection {
    # Punkt wejścia jednej fazy w VM: zwraca JSON {meta, phase, error, data}.
    [CmdletBinding()]
    param(
        [Parameter(Mandatory)][string]$PlanJson,
        [Parameter(Mandatory)][string]$Phase,
        [Parameter(Mandatory)][string]$UserSid
    )
    Assert-E4CInsideVm
    $Plan = $PlanJson | ConvertFrom-Json
    $Document = [ordered]@{ meta = $null; phase = $Phase; error = $null; data = $null }
    try { $Document.meta = Get-ExploreMeta } catch { $Document.meta = [ordered]@{ Error = $_.Exception.Message } }
    try {
        $null = Install-E4CRunner -Plan $Plan
        switch ($Phase) {
            'baseline' { $Document.data = Invoke-E4CBaseline -Plan $Plan -UserSid $UserSid }
            'offline-fresh' { $Document.data = Invoke-E4COfflineFresh -Plan $Plan -UserSid $UserSid }
            'network' { $Document.data = Invoke-E4CNetworkProbe -Plan $Plan -UserSid $UserSid }
            'install-all' { $Document.data = Invoke-E4CInstallAll -Plan $Plan -UserSid $UserSid }
            'ap-set' { $Document.data = Invoke-E4CAdminProtectionSet -Plan $Plan }
            'ap-probe' { $Document.data = Invoke-E4CAdminProtectionProbe -Plan $Plan -UserSid $UserSid }
            'ap-restore' { $Document.data = Invoke-E4CAdminProtectionRestore -Plan $Plan }
            'final' { $Document.data = Invoke-E4CFinal -Plan $Plan -UserSid $UserSid }
            'cli-install' { $Document.data = Invoke-E4CCliInstall -Plan $Plan -UserSid $UserSid }
            default { throw "Nieznana faza '$Phase'." }
        }
    }
    catch { $Document.error = "$(Format-E4CError $_) | $($_.ScriptStackTrace)" }
    try { return ($Document | ConvertTo-Json -Depth 16) }
    catch { return ([ordered]@{ meta = $Document.meta; phase = $Phase; error = "ConvertTo-Json: $(Format-E4CError $_)"; data = $null } | ConvertTo-Json -Depth 6) }
}

# ---------------------------------------------------------------------------------------------------------------------
# Strona hosta (nic z tego nie jest wysyłane do VM).
# ---------------------------------------------------------------------------------------------------------------------

function Get-E4CRemoteFunctionNames {
    return @(
        'Test-E4CVmComputerName', 'Assert-E4CInsideVm', 'Format-E4CError', 'Format-E4CHex', 'Get-E4CSafeName', 'Get-E4CElapsedSeconds', 'Get-E4CFreeBytes', 'Test-E4CWingetId', 'Assert-E4CPlainPath',
        'Get-E4CInstallArguments', 'Get-E4CSearchArguments', 'Get-E4CListArguments', 'Get-E4CShowArguments', 'Get-E4CRunnerScript', 'Test-E4CProgressSegment', 'ConvertFrom-E4COutput',
        'Get-E4CShowFields', 'Get-E4CSearchSummary', 'Get-E4CUninstallSnapshot', 'Get-E4CAppxSnapshot', 'Compare-E4CSnapshot', 'Test-E4CNetwork', 'Get-E4CAdminApprovalState', 'Get-E4CLocalAccounts',
        'Get-E4CInstallerProcesses', 'Install-E4CRunner', 'Invoke-E4CTool', 'Invoke-E4CInstallItem', 'Set-E4CAdminApprovalMode', 'Restart-E4CVm', 'Invoke-E4CBaseline', 'Invoke-E4COfflineFresh',
        'Invoke-E4CNetworkProbe', 'Invoke-E4CInstallAll', 'Invoke-E4CAdminProtectionSet', 'Invoke-E4CAdminProtectionProbe', 'Invoke-E4CAdminProtectionRestore', 'Invoke-E4CFinal',
        'Get-E4CCliArguments', 'Test-E4CCliId', 'Read-E4CCliReport', 'Read-E4CJournalInstallEntries', 'Get-E4CRustDeskState', 'Copy-E4CDsoLogs', 'Invoke-E4CCliInstall', 'Invoke-E4CCollection'
    )
}

function Get-E4CHostOnlyFunctionNames {
    return @('Get-E4CPlan', 'Get-E4CInstallIds', 'Get-E4CCliInstallIds', 'Assert-E4CCliIds', 'Get-E4CRemoteFunctionNames', 'Get-E4CHostOnlyFunctionNames', 'Get-E4CRemoteScript', 'Assert-E4COutDirectory', 'Get-E4CPhaseOrder', 'Get-E4CNetworkPhases',
        'Get-E4CRunArguments', 'Get-E4CPhaseError', 'Invoke-E4CVmPhase', 'Restart-E4CVmAndWait', 'Copy-E4CRawFiles', 'Complete-E4CVmCleanup', 'Invoke-E4CVmRun', 'Invoke-E4CExploration')
}

function Get-E4CInstallIds {
    # Pro: cała lista, środowiska uruchomieniowe najpierw (kolejność serii ze spec 5.4); Home: HomeInstall z planu.
    [CmdletBinding()]
    [OutputType([string[]])]
    param(
        [Parameter(Mandatory)][ValidateSet('DSO-26H2-Home', 'DSO-26H2-Pro')][string]$VmName,
        [Parameter(Mandatory)]$Plan
    )
    if ($VmName -eq 'DSO-26H2-Home') { return @($Plan.HomeInstall | ForEach-Object { [string]$_ }) }
    $Runtimes = @($Plan.Apps | Where-Object { $_.Category -eq 'runtimes' } | ForEach-Object { [string]$_.WingetId })
    $Rest = @($Plan.Apps | Where-Object { $_.Category -ne 'runtimes' } | ForEach-Object { [string]$_.WingetId })
    return @($Runtimes + $Rest)
}

function Get-E4CCliInstallIds {
    # Faza cli-install: Pro = wszystkie id z apps.json (kolejność pliku; CLI i tak stawia środowiska uruchomieniowe pierwsze),
    # Home = CliHomeIds z planu (po jednej pozycji z kategorii + winget:<kandydat z wyszukiwania>).
    [CmdletBinding()]
    [OutputType([string[]])]
    param(
        [Parameter(Mandatory)][ValidateSet('DSO-26H2-Home', 'DSO-26H2-Pro')][string]$VmName,
        [Parameter(Mandatory)]$Plan,
        [string]$AppsCatalogPath = (Join-Path (Get-DsoRepoRoot) 'src\DSOptimizer.Catalog\catalog\apps.json')
    )
    if ($VmName -eq 'DSO-26H2-Home') { return @($Plan.CliHomeIds | ForEach-Object { [string]$_ }) }
    if (-not [System.IO.File]::Exists($AppsCatalogPath)) { throw "Brak listy aplikacji '$AppsCatalogPath'." }
    $Entries = @((Get-Content -LiteralPath $AppsCatalogPath -Raw -Encoding UTF8 | ConvertFrom-Json).entries | Where-Object { $null -ne $_ })
    if ($Entries.Count -eq 0) { throw "Lista aplikacji '$AppsCatalogPath' jest pusta." }
    return @($Entries | ForEach-Object { [string]$_.id })
}

function Assert-E4CCliIds {
    # -CliIds (plan RustDesk): niepusta lista, każde id wg Test-E4CCliId; zwraca listę bez zmian. Błąd = wyjątek z id, przed VM.
    [CmdletBinding()]
    [OutputType([string[]])]
    param([AllowNull()][AllowEmptyCollection()][string[]]$Ids)
    if ($null -eq $Ids -or @($Ids).Count -eq 0) { throw '-CliIds: pusta lista id.' }
    foreach ($Id in $Ids) {
        if (-not (Test-E4CCliId -Id $Id)) { throw "-CliIds: nieprawidłowe id '$Id' (install.<nazwa> albo winget:<pełny id>, bez spacji i przecinków)." }
    }
    return @($Ids)
}

function Get-E4CRemoteScript {
    # Tekst definicji funkcji zdalnych: część wspólna z modułu + funkcje E4C (FunctionInfo pobrane tu, w zasięgu skryptu).
    [CmdletBinding()]
    [OutputType([string])]
    param()
    $Names = @(Get-ExploreCommonRemoteFunctionNames) + @(Get-E4CRemoteFunctionNames)
    return (ConvertTo-ExploreRemoteScript -Command @($Names | ForEach-Object { Get-Command -Name $_ -CommandType Function -ErrorAction Stop }))
}

function Assert-E4COutDirectory {
    # -OutDirectory: wymagany, bezwzględny (C:\... albo \\serwer\udział\...), poza repo; w repo tylko katalog ignorowany przez git.
    [CmdletBinding()]
    [OutputType([string])]
    param(
        [AllowEmptyString()][string]$Path,
        [Parameter(Mandatory)][string]$RepoRoot
    )
    if ([string]::IsNullOrWhiteSpace($Path)) { throw '-OutDirectory jest wymagany (ścieżka bezwzględna poza repo, np. $env:TEMP\dso-explore\exploration).' }
    if ($Path -notmatch '^[A-Za-z]:\\' -and $Path -notmatch '^\\\\[^\\]+\\[^\\]+') { throw "-OutDirectory musi być ścieżką bezwzględną (C:\... albo \\serwer\udział\...): '$Path'." }
    $Full = [System.IO.Path]::GetFullPath($Path).TrimEnd('\')
    $Root = [System.IO.Path]::GetFullPath($RepoRoot).TrimEnd('\')
    $InRepo = ($Full -ieq $Root) -or $Full.StartsWith("$Root\", [System.StringComparison]::OrdinalIgnoreCase)
    if ($InRepo -and -not (Test-ExploreGitIgnored -Path $Full -RepoRoot $Root)) { throw "-OutDirectory '$Full' leży w repo ('$Root') poza katalogiem ignorowanym przez git; wyniki eksploracji trzymaj poza repo albo w katalogu ignorowanym przez git." }
    return $Full
}

function Get-E4CNetworkPhases {
    # Fazy z siecią: karta VM podłączona do 'Default Switch' tylko na czas tych faz.
    [CmdletBinding()]
    [OutputType([string[]])]
    param()
    return @('network', 'install-all', 'cli-install')
}

function Get-E4CPhaseOrder {
    # Wybrane fazy w stałej kolejności; baseline zawsze; bez sieci bez network/install-all; adminprotection tylko Pro, tuż przed final.
    [CmdletBinding()]
    [OutputType([string[]])]
    param(
        [Parameter(Mandatory)][string[]]$Phase,
        [Parameter(Mandatory)][ValidateSet('DSO-26H2-Home', 'DSO-26H2-Pro')][string]$VmName,
        [bool]$Network = $true
    )
    $Order = @('baseline', 'offline-fresh', 'network', 'install-all', 'cli-install', 'adminprotection', 'final')
    $Wanted = @($Phase) + @('baseline')
    if (-not $Network) { $Wanted = @($Wanted | Where-Object { @(Get-E4CNetworkPhases) -notcontains $_ }) }
    if ($VmName -ne 'DSO-26H2-Pro') { $Wanted = @($Wanted | Where-Object { $_ -ne 'adminprotection' }) }
    return @($Order | Where-Object { $Wanted -contains $_ })
}

function Get-E4CRunArguments {
    [CmdletBinding()]
    param(
        [Parameter(Mandatory)][string[]]$VmName,
        [Parameter(Mandatory)][string[]]$Phase,
        [Parameter(Mandatory)][string]$OutDirectory,
        [Parameter(Mandatory)][string]$CheckpointName,
        [Parameter(Mandatory)][int]$ReadyTimeoutSeconds,
        [bool]$Network = $true,
        [string]$BuildDirectory,
        [string]$CatalogPath,
        # Plan RustDesk: -CliIds (puste/nieobecne = lista z planu dla VM) i -CliRepeat.
        [AllowNull()][AllowEmptyCollection()][string[]]$CliIds,
        [bool]$CliRepeat = $false
    )
    $Ids = @()
    if ($null -ne $CliIds -and @($CliIds).Count -gt 0) { $Ids = @(Assert-E4CCliIds -Ids $CliIds) }
    $Phases = [ordered]@{}
    foreach ($Vm in $VmName) { $Phases[$Vm] = @(Get-E4CPhaseOrder -Phase $Phase -VmName $Vm -Network $Network) }
    return [ordered]@{ VmName = @($VmName); Phases = $Phases; Network = $Network; OutDirectory = $OutDirectory; CheckpointName = $CheckpointName; ReadyTimeoutSeconds = $ReadyTimeoutSeconds; BuildDirectory = $BuildDirectory; CatalogPath = $CatalogPath
        CliIds = $Ids; CliRepeat = $CliRepeat; Plan = (Get-E4CPlan) }
}

function Get-E4CPhaseError {
    # Pole error dokumentu fazy (odporne na brak pola w trybie StrictMode).
    [CmdletBinding()]
    param([AllowNull()]$Document)
    if ($null -eq $Document -or $null -eq $Document.PSObject.Properties['error']) { return $null }
    return $Document.error
}

function Invoke-E4CVmPhase {
    # Jedna faza w VM przez otwartą sesję: definicje + Invoke-E4CCollection; zwraca sparsowany dokument.
    [CmdletBinding()]
    param(
        [Parameter(Mandatory)][AllowNull()][System.Management.Automation.Runspaces.PSSession]$Session,
        [Parameter(Mandatory)][string]$RemoteScript,
        [Parameter(Mandatory)][string]$PlanJson,
        [Parameter(Mandatory)][string]$Phase,
        [Parameter(Mandatory)][string]$UserSid
    )
    Assert-DsoVmLiveAllowed
    $Remote = {
        param([string]$Definitions, [string]$Json, [string]$Which, [string]$Sid)
        . ([scriptblock]::Create($Definitions))
        Invoke-E4CCollection -PlanJson $Json -Phase $Which -UserSid $Sid
    }
    $Raw = @(Invoke-Command -Session $Session -ScriptBlock $Remote -ArgumentList @($RemoteScript, $PlanJson, $Phase, $UserSid))
    $Text = [string]($Raw | Where-Object { $_ -is [string] } | Select-Object -Last 1)
    if ([string]::IsNullOrWhiteSpace($Text)) { throw "Faza '$Phase' nie zwróciła JSON." }
    return ($Text | ConvertFrom-Json)
}

function Restart-E4CVmAndWait {
    # Restart VM z jej wnętrza (Restart-E4CVm za bezpiecznikiem), zgaśnięcie heartbeatu, nowa sesja po gotowości (zalogowany dso).
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
        Restart-E4CVm
    }
    # Restart zrywa sesję: błąd wywołania jest spodziewany (zgaśnięcie heartbeatu i tak sprawdzamy).
    try { $null = Invoke-Command -Session $Session -ScriptBlock $Remote -ArgumentList @($RemoteScript) } catch { $null = $_ }
    if ($null -ne $Session) { Remove-PSSession -Session $Session -ErrorAction SilentlyContinue }
    $null = Wait-VmHeartbeatDrop -VmName $VmName
    return (Wait-VmReady -VmName $VmName -Credential $Credential -TimeoutSeconds $ReadyTimeoutSeconds)
}

function Copy-E4CRawFiles {
    # Surowe pliki z C:\dso\e4c\raw w VM do <OutDirectory>\<VM>\raw, kawałkami po 4 MB jako Base64 (Copy-Item -FromSession zgłaszał OOM na PS Direct).
    [CmdletBinding()]
    param(
        [Parameter(Mandatory)][AllowNull()][System.Management.Automation.Runspaces.PSSession]$Session,
        [Parameter(Mandatory)][string]$Destination,
        [string]$Source = 'C:\dso\e4c\raw'
    )
    Assert-DsoVmLiveAllowed
    $null = New-Item -ItemType Directory -Path $Destination -Force
    $Files = @(Invoke-Command -Session $Session -ArgumentList @($Source) -ScriptBlock { param([string]$From) @(Get-ChildItem -LiteralPath $From -File -ErrorAction SilentlyContinue | ForEach-Object { [pscustomobject]@{ Name = $_.Name; Length = $_.Length } }) })
    $Copied = @()
    foreach ($File in $Files) {
        $Target = Join-Path $Destination $File.Name
        $Stream = [System.IO.File]::Create($Target)
        try {
            for ($Offset = 0L; $Offset -lt [long]$File.Length; $Offset += 4MB) {
                $Base64 = [string](Invoke-Command -Session $Session -ArgumentList @($Source, $File.Name, $Offset) -ScriptBlock {
                        param([string]$From, [string]$Name, [long]$At)
                        $S = [System.IO.File]::Open((Join-Path $From $Name), 'Open', 'Read', 'ReadWrite')
                        try { $null = $S.Seek($At, 'Begin'); $B = New-Object byte[] ([math]::Min(4MB, $S.Length - $At)); $N = $S.Read($B, 0, $B.Length); [Convert]::ToBase64String($B, 0, $N) } finally { $S.Dispose() }
                    })
                $Bytes = [Convert]::FromBase64String($Base64)
                $Stream.Write($Bytes, 0, $Bytes.Length)
            }
        }
        finally { $Stream.Dispose() }
        $Copied += $Target
    }
    return $Copied
}

function Complete-E4CVmCleanup {
    # Koniec przebiegu: revert do checkpointu, odłączenie karty i sprawdzenie kontraktu stanu końcowego: VM Off i karta bez przełącznika.
    [CmdletBinding()]
    param(
        [Parameter(Mandatory)][ValidateSet('DSO-26H2-Home', 'DSO-26H2-Pro')][string]$VmName,
        [Parameter(Mandatory)][string]$CheckpointName
    )
    $Out = [ordered]@{ Ok = $false; RevertError = $null; DisconnectError = $null; CheckError = $null; FinalState = $null; FinalSwitch = $null; Violations = @(); Problems = @() }
    try { Restore-VmCleanState -VmName $VmName -CheckpointName $CheckpointName }
    catch { $Out.RevertError = $_.Exception.Message; $Out.Problems += "revert do '$CheckpointName': $($_.Exception.Message)" }
    try { Disconnect-VMNetworkAdapter -VMName $VmName -ErrorAction Stop }
    catch { $Out.DisconnectError = $_.Exception.Message; $Out.Problems += "odłączenie sieci: $($_.Exception.Message)" }
    try {
        $Out.FinalState = [string](Get-VM -Name $VmName -ErrorAction Stop).State
        $Out.FinalSwitch = (@(Get-VMNetworkAdapter -VMName $VmName -ErrorAction Stop | ForEach-Object { $_.SwitchName } | Where-Object { $_ }) -join ', ')
        if ($Out.FinalState -ne 'Off') { $Out.Violations += "VM w stanie '$($Out.FinalState)' zamiast 'Off'" }
        if ($Out.FinalSwitch) { $Out.Violations += "karta podłączona do '$($Out.FinalSwitch)'" }
    }
    catch { $Out.CheckError = $_.Exception.Message; $Out.Problems += "odczyt stanu końcowego: $($_.Exception.Message)" }
    if (@($Out.Violations).Count -gt 0) { $Out.Problems += "stan końcowy: $(@($Out.Violations) -join '; ')" }
    $Out.Ok = (@($Out.Problems).Count -eq 0)
    return [pscustomobject]$Out
}

function Invoke-E4CVmRun {
    # Jedna VM: revert -> start (bez sieci) -> gotowość -> fazy (sieć tylko na network/install-all; adminprotection z dwoma restartami)
    # -> kopia surowych plików -> ZAWSZE revert, odłączenie karty i sprawdzenie stanu końcowego -> zapis JSON.
    [CmdletBinding()]
    [OutputType([string])]
    param(
        [Parameter(Mandatory)][ValidateSet('DSO-26H2-Home', 'DSO-26H2-Pro')][string]$VmName,
        [Parameter(Mandatory)][string]$OutDirectory,
        [Parameter(Mandatory)][string[]]$Phases,
        [string]$CheckpointName = 'czysta-26H2',
        [int]$ReadyTimeoutSeconds = 1200,
        # Faza cli-install: katalog builda Release (DSOptimizer.exe) i katalog testowy VM dla Copy-DsoBuildToVm.
        [string]$BuildDirectory,
        [string]$CatalogPath,
        # Plan RustDesk: własna lista serii cli-install (nadpisuje CliIds/CliHomeIds planu) i drugie --install-apps po liście.
        [AllowNull()][AllowEmptyCollection()][string[]]$CliIds,
        [switch]$CliRepeat
    )
    Assert-DsoVmLiveAllowed
    # Złe id = wyjątek, zanim VM zostanie ruszona.
    $OwnCliIds = @()
    if ($null -ne $CliIds -and @($CliIds).Count -gt 0) { $OwnCliIds = @(Assert-E4CCliIds -Ids $CliIds) }
    $NeedsBuild = (@($Phases) -contains 'cli-install')
    if ($NeedsBuild -and ([string]::IsNullOrWhiteSpace($BuildDirectory) -or [string]::IsNullOrWhiteSpace($CatalogPath))) {
        throw "Faza cli-install wymaga -BuildDirectory i -CatalogPath (build DSOptimizera kopiowany do VM); VM '$VmName' nie została ruszona."
    }
    $Credential = Get-DsoVmCredential -VmName $VmName
    $Session = $null
    $StartedAt = Get-Date
    $Results = [ordered]@{}
    $Timeline = @()
    $PhaseErrors = @()
    $UserSid = $null
    $PrimaryError = $null
    $RawFiles = @()
    $Ready = $false
    $VmDirectory = Join-Path $OutDirectory $VmName
    try {
        Restore-VmCleanState -VmName $VmName -CheckpointName $CheckpointName
        Start-VM -Name $VmName
        $Session = Wait-VmReady -VmName $VmName -Credential $Credential -TimeoutSeconds $ReadyTimeoutSeconds
        $Ready = $true
        $UserSid = [string](Invoke-Command -Session $Session -ScriptBlock { (Get-LocalUser -Name 'dso').SID.Value })
        if ([string]::IsNullOrWhiteSpace($UserSid)) { throw "Nie udało się odczytać SID użytkownika 'dso' w VM '$VmName'." }
        # Chwila na dokończenie logowania dso (rejestracja pakietów, w tym App Installera) przed pierwszym wywołaniem winget.
        Start-Sleep -Seconds 45
        if ($NeedsBuild) {
            # Copy-DsoBuildToVm czyści C:\dso, więc build trafia do VM przed pierwszą fazą (runner i surowe pliki powstają później).
            Copy-DsoBuildToVm -Session $Session -BuildDirectory $BuildDirectory -CatalogPath $CatalogPath -Layout (Get-DsoVmLayout)
            $Timeline += "$(Get-Date -Format 'HH:mm:ss') build DSOptimizera w VM"
        }
        $RemoteScript = Get-E4CRemoteScript
        $Plan = Get-E4CPlan
        $Plan.InstallIds = @(Get-E4CInstallIds -VmName $VmName -Plan $Plan)
        if ($OwnCliIds.Count -gt 0) { $Plan.CliIds = @($OwnCliIds) }
        else { $Plan.CliIds = @(Get-E4CCliInstallIds -VmName $VmName -Plan $Plan) }
        $Plan.CliRepeat = [bool]$CliRepeat
        $PlanJson = ConvertTo-Json -InputObject $Plan -Depth 6
        $NetworkPhases = @(Get-E4CNetworkPhases)
        foreach ($Phase in $Phases) {
            $Network = ($NetworkPhases -contains $Phase)
            if ($Network) {
                Connect-VMNetworkAdapter -VMName $VmName -SwitchName 'Default Switch' -ErrorAction Stop
                $Timeline += "$(Get-Date -Format 'HH:mm:ss') sieć podłączona"
            }
            $Timeline += "$(Get-Date -Format 'HH:mm:ss') $Phase"
            Write-Host "$(Get-Date -Format 'HH:mm:ss') $VmName $Phase"
            try {
                if ($Phase -eq 'adminprotection') {
                    $Ap = [ordered]@{ set = $null; probe = $null; restore = $null }
                    $Results[$Phase] = $Ap
                    $Ap.set = Invoke-E4CVmPhase -Session $Session -RemoteScript $RemoteScript -PlanJson $PlanJson -Phase 'ap-set' -UserSid $UserSid
                    try {
                        $Session = Restart-E4CVmAndWait -Session $Session -VmName $VmName -Credential $Credential -RemoteScript $RemoteScript -ReadyTimeoutSeconds $ReadyTimeoutSeconds
                        $Timeline += "$(Get-Date -Format 'HH:mm:ss') restart (TypeOfAdminApprovalMode = 2)"
                        $Ap.probe = Invoke-E4CVmPhase -Session $Session -RemoteScript $RemoteScript -PlanJson $PlanJson -Phase 'ap-probe' -UserSid $UserSid
                    }
                    finally {
                        # Przywrócenie wartości i restart także po błędzie sondy (revert i tak przywróci checkpoint).
                        $Ap.restore = Invoke-E4CVmPhase -Session $Session -RemoteScript $RemoteScript -PlanJson $PlanJson -Phase 'ap-restore' -UserSid $UserSid
                        $Session = Restart-E4CVmAndWait -Session $Session -VmName $VmName -Credential $Credential -RemoteScript $RemoteScript -ReadyTimeoutSeconds $ReadyTimeoutSeconds
                        $Timeline += "$(Get-Date -Format 'HH:mm:ss') restart (wartość przywrócona)"
                    }
                    foreach ($Part in 'set', 'probe', 'restore') { $Message = Get-E4CPhaseError -Document $Ap[$Part]; if ($Message) { $PhaseErrors += "ap-${Part}: $Message" } }
                }
                else {
                    $Results[$Phase] = Invoke-E4CVmPhase -Session $Session -RemoteScript $RemoteScript -PlanJson $PlanJson -Phase $Phase -UserSid $UserSid
                    $Message = Get-E4CPhaseError -Document $Results[$Phase]
                    if ($Message) { $PhaseErrors += "${Phase}: $Message" }
                }
            }
            catch { $PrimaryError = (@($PrimaryError, "faza ${Phase}: $($_.Exception.Message)") | Where-Object { $_ }) -join ' | '; if (Test-VmAuthFailure -Exception $_.Exception) { throw } }
            finally {
                if ($Network) {
                    try { Disconnect-VMNetworkAdapter -VMName $VmName -ErrorAction Stop; $Timeline += "$(Get-Date -Format 'HH:mm:ss') sieć odłączona" }
                    catch { $PrimaryError = (@($PrimaryError, "odłączenie sieci: $($_.Exception.Message)") | Where-Object { $_ }) -join ' | ' }
                }
            }
        }
    }
    catch {
        $PrimaryError = (@($PrimaryError, $_.Exception.Message) | Where-Object { $_ }) -join ' | '
        if (Test-VmAuthFailure -Exception $_.Exception) { throw }
    }
    finally {
        if ($Ready) {
            try { $RawFiles = @(Copy-E4CRawFiles -Session $Session -Destination (Join-Path $VmDirectory 'raw')) }
            catch { $PrimaryError = (@($PrimaryError, "kopia surowych plików: $($_.Exception.Message)") | Where-Object { $_ }) -join ' | ' }
        }
        if ($null -ne $Session) { Remove-PSSession -Session $Session -ErrorAction SilentlyContinue }
        # Sprzątanie PRZED zapisem, żeby jego wynik (także błędy) trafił do JSON; błąd sprzątania nigdy nie jest ukrywany.
        $Cleanup = Complete-E4CVmCleanup -VmName $VmName -CheckpointName $CheckpointName
        if (-not $Cleanup.Ok) {
            $CleanupMessage = "sprzątanie VM: $(@($Cleanup.Problems) -join ' | ')"
            Write-Warning "VM '$VmName': $CleanupMessage"
            $PrimaryError = (@($PrimaryError, $CleanupMessage) | Where-Object { $_ }) -join ' | '
        }
        $Result = [ordered]@{ schema = 1; vmName = $VmName; checkpoint = $CheckpointName; userSid = $UserSid; startedAt = $StartedAt.ToString('o'); finishedAt = (Get-Date).ToString('o')
            error = $PrimaryError; phaseErrors = $PhaseErrors; cleanup = $Cleanup; timeline = $Timeline; rawFiles = @($RawFiles | ForEach-Object { Split-Path -Leaf $_ }); phases = $Results }
        $Saved = Save-ExploreResult -Result $Result -OutDirectory $OutDirectory -VmName $VmName -FileName 'e4c.json'
    }
    if ($PrimaryError) { throw "VM '$VmName': $PrimaryError (wynik częściowy: $Saved)" }
    return $Saved
}

function Invoke-E4CExploration {
    # Wszystkie wybrane VM po kolei. Błąd uwierzytelnienia = stop całości; inny błąd = zapis i dalej.
    [CmdletBinding()]
    param([Parameter(Mandatory)][System.Collections.IDictionary]$Arguments)
    $Paths = New-Object System.Collections.Generic.List[string]
    $Errors = New-Object System.Collections.Generic.List[string]
    foreach ($Vm in $Arguments.VmName) {
        try {
            Write-Host "$(Get-Date -Format 'HH:mm:ss') $Vm"
            $Paths.Add((Invoke-E4CVmRun -VmName $Vm -OutDirectory $Arguments.OutDirectory -Phases @($Arguments.Phases[$Vm]) -CheckpointName $Arguments.CheckpointName -ReadyTimeoutSeconds $Arguments.ReadyTimeoutSeconds `
                -BuildDirectory ([string]$Arguments.BuildDirectory) -CatalogPath ([string]$Arguments.CatalogPath) -CliIds @($Arguments.CliIds | Where-Object { $_ }) -CliRepeat:([bool]$Arguments.CliRepeat)))
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
        $OutDirectory = Assert-E4COutDirectory -Path $OutDirectory -RepoRoot $RepoRoot
        $OutDirectory = Resolve-ExploreOutDirectory -Path $OutDirectory
        if (-not $BuildDirectory) { $BuildDirectory = Join-Path $RepoRoot 'src\DSOptimizer.App\bin\Release\net481' }
        $CatalogPath = Join-Path $RepoRoot 'tests\vm\catalog\vm-test-catalog.json'
        $RunArguments = Get-E4CRunArguments -VmName $VmName -Phase $Phase -OutDirectory $OutDirectory -CheckpointName $CheckpointName -ReadyTimeoutSeconds $ReadyTimeoutSeconds -Network (-not $NoNetwork) `
            -BuildDirectory $BuildDirectory -CatalogPath $CatalogPath -CliIds $CliIds -CliRepeat ([bool]$CliRepeat)
        if ($ShowPlan) {
            [pscustomobject]$RunArguments | ConvertTo-Json -Depth 6
            exit 0
        }
        $NeedsBuild = @($RunArguments.VmName | Where-Object { @($RunArguments.Phases[$_]) -contains 'cli-install' }).Count -gt 0
        if ($NeedsBuild -and -not (Test-Path -LiteralPath (Join-Path $BuildDirectory 'DSOptimizer.exe') -PathType Leaf)) {
            throw "Brak DSOptimizer.exe w '$BuildDirectory'. Zbuduj aplikację: dotnet build DSOptimizer.sln -c Release"
        }
        Import-Module (Join-Path $VmRoot 'modules\VmCommon.psm1') -Force
        Import-Module (Join-Path $VmRoot 'modules\VmHost.psm1') -Force
        # ExploreCommon (zaimportowany wyżej) trzyma referencję do starej instancji VmHost; ponowny import po nim wiąże go z bieżącą.
        Import-Module (Join-Path $PSScriptRoot 'ExploreCommon.psm1') -Force
        $Principal = New-Object System.Security.Principal.WindowsPrincipal([System.Security.Principal.WindowsIdentity]::GetCurrent())
        if (-not $Principal.IsInRole([System.Security.Principal.WindowsBuiltInRole]::Administrator)) {
            throw 'Uruchom skrypt w sesji PowerShell jako administrator (Hyper-V i PowerShell Direct tego wymagają).'
        }
        Write-Host "Wyniki: $OutDirectory"
        foreach ($Vm in $RunArguments.VmName) { Write-Host "VM: $Vm; fazy: $(@($RunArguments.Phases[$Vm]) -join ', ')" }
        $Outcome = Invoke-E4CExploration -Arguments $RunArguments
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
