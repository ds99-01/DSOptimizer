<#
Instrukcja uruchomienia:
    Sesja PowerShell uruchomiona jako administrator, na hoście z Hyper-V, z modułami testów VM (tests\vm\modules) i tests\vm\explore\ExploreCommon.psm1.
    VM utworzone wcześniej przez New-TestVm.ps1 (checkpoint 'czysta-26H2', poświadczenia w %LOCALAPPDATA%\DSOptimizer-vm, autologon 'dso').
    Jedna VM na proces. Wydanie: katalog z tools\release\Build-Release.ps1 -Variant Folder (DSOptimizer-<wersja>.zip,
    DSOptimizer-<wersja>-setup.exe i SHA256SUMS.txt z sumami obu plików), np.:
        .\tests\vm\explore\Invoke-InstallerExploration.ps1 -VmName DSO-26H2-Pro -ArtifactDirectory <katalog wydania> `
            -OutDirectory $env:TEMP\dso-explore\instalator\vm
        .\tests\vm\explore\Invoke-InstallerExploration.ps1 -VmName DSO-26H2-Home -ArtifactDirectory <katalog wydania> -Phase upgrade `
            -UpgradeInstallerPath <katalog wyższej wersji>\DSOptimizer-<wyższa>-setup.exe -OutDirectory $env:TEMP\dso-explore\instalator\vm
    -Phase: smoke, running, upgrade, wizard, motw (kolejność stała; domyślnie smoke, running, wizard, motw). upgrade wymaga
    -UpgradeInstallerPath: instalator wersji wyższej niż wydanie, obok niego SHA256SUMS.txt z jego sumą i DSOptimizer-<wyższa>.zip
    z sumą (pliki po aktualizacji są porównywane z tym zipem).
    -Language: języki kreatora (pl, en; domyślnie oba), każdy na osobnym reverrcie.
    -ShowPlan: wypisuje rozstrzygnięte argumenty i plan (JSON) i kończy, nic nie uruchamia.
    -OutDirectory jest wymagany: ścieżka bezwzględna poza repo; w repo tylko katalog ignorowany przez git.
    Instalator uruchamia się wyłącznie w VM, nigdy na hoście.
Opis działania:
    Sprawdzenie instalatora Inno w prawdziwej sesji użytkownika 'dso' na VM 26H2. Każda faza: revert do checkpointu, start VM bez
    sieci (karta odłączona), gotowość (zalogowany dso), pliki w VM w C:\dso\instalator (bez spacji), kopia przenośna rozpakowana do
    C:\dso\app, wyniki sond w C:\dso\out. Okna i CLI startują jako 'dso' przez zadania Harmonogramu (Interactive, Highest), czyli
    bez UAC; instalator i deinstalator po cichu uruchamia Administrator PowerShell Direct. Ścieżka zainstalowanego programu
    (C:\Program Files\DSOptimizer, ze spacją) trafia tylko do pól, których nikt nie dzieli na słowa (-Execute zadania,
    Start-Process -FilePath, -LiteralPath). Na końcu fazy zawsze Complete-ExploreVmCleanup (revert, karta odłączona, VM Off).
    Fazy:
      smoke   - Test-InstallerSmoke.ps1 z tools\release w sesji PowerShell Direct (-LogDirectory C:\dso\out\smoke), wynik
                smoke-result.json; sprawdzenie: passed.
      running - cicha instalacja; okno A z Program Files (do 60 s na proces i mutex Global\DSOptimizer.SingleInstance, wybór języka pl);
                kopia przenośna B jako zadanie: sonda UI Automation w sesji dso w 30 s znajduje okno „DSOptimizer” z tekstem od
                „DSOptimizer już działa”, klika „OK”; B kończy się kodem 1, A żyje; CLI --apply z kopii przenośnej jako dso = kod 1
                i errors[0] od „Działa już inna kopia DSOptimizera”; --list-apps jako dso: wiersz DSOptimizer w grupie Protected;
                instalator ponownie: kod 7, DisplayVersion i sumy plików bez zmian; deinstalator: pliki i klucz zostają; relaunch:
                A z mutexem żyje przed startem C i tuż przed zamknięciem (odczyt zapisany, zamknięcie liczy się tylko dla żywego A);
                zadanie C z --relaunched, najpierw wykrycie procesu C, potem A trzyma mutex jeszcze co najmniej 8 s (dłużej niż
                zwykłe czekanie 2 s) przy działającej sondzie okna odmowy, dopiero potem Stop-Process A; po 15 s C żyje, mutex jest,
                okna „już działa” brak (ani przy żywym A, ani po jego zamknięciu); błąd sondy B albo C nie przerywa fazy;
                %ProgramData%\DSOptimizer (pliki, właściciel); Stop-Process C, mutex znika w 10 s, cicha deinstalacja kod 0,
                folder programu znika w 60 s, klucz i skróty też, %ProgramData%\DSOptimizer z tymi samymi plikami i właścicielem
                S-1-5-32-544.
      upgrade - instalacja wydania, usunięcie skrótu z pulpitu, okno A, instalator wyższej wersji: kod 7, DisplayVersion i pliki bez zmian;
                Stop-Process A, mutex znika, instalator wyższej wersji: kod 0, nowa DisplayVersion, skrót na pulpicie nie wrócił,
                skrót w Menu Start jest, pliki równe zipowi wyższej wersji, brak unins001.dat.
      wizard  - (osobno dla każdego języka) zadanie dso uruchamia instalator z /LANG=<pl|en> /LOG=C:\dso\out\logs\wizard-<lang>.log
                (log w raw-wizard-<lang>); sonda w sesji prowadzi kreator
                (okno klasy TWizardForm, przyciski po nazwie UI Automation), przed każdym kliknięciem zrzut okna
                C:\dso\out\wizard-<lang>-<n>.png i teksty strony; stan pól „ikona na pulpicie” i „Uruchom…” przez MSAA
                (oleacc), zapasowo UI Automation (TogglePattern), pole pulpitu też z podsumowania strony „gotowe”. Po „Zakończ”
                w 30 s działa DSOptimizer.exe z Program Files. Sprawdzenia: strony powitanie -> zadania -> gotowe -> koniec,
                powitanie z „Mozilla Public License 2.0”, oba pola zaznaczone, program wystartował, zrzut każdej strony.
      motw    - instalator na pulpicie dso ze strumieniem Zone.Identifier (ZoneId=3), start explorer.exe "<plik>" z zadania dso
                z RunLevel Limited (jak dwuklik); po 10 s sonda zbiera tytuły okien najwyższego poziomu (UI Automation i Win32)
                i robi zrzut motw.png. Sprawdzenie: okno „System Windows ochronił ten komputer” albo „Windows protected your PC”.
                Niczego nie klika. Faza działa bez sieci jak reszta.
    Werdykt liczy czysta funkcja Get-InstallerVerdict -Phase <nazwa> -Data <obiekt> -> lista { Name; Passed; Detail }.
    Nazwy przycisków kreatora wzięte z plików językowych Inno Setup 6.7 (Default.isl, Polish.isl): Dalej >/Next >,
    Instaluj/Install (dopuszczone też Zainstaluj), Zakończ/Finish; potwierdzić na pierwszym prawdziwym przebiegu.
    RISK: skrypt przywraca VM do checkpointu 'czysta-26H2' przed każdą fazą i po niej (zmiany w VM przepadają); w VM instaluje,
    aktualizuje i odinstalowuje DSOptimizera i zamyka jego procesy (Stop-Process) – wyłącznie w VM. Nie usuwa VM, nie podłącza sieci.
    Wynik: <OutDirectory>\<VmName>\installer.json (UTF-8), zrzuty PNG w <OutDirectory>\<VmName>, logi Inno i smoke
    w <OutDirectory>\<VmName>\raw-<faza>.
    Kod wyjścia: 0 gdy wszystkie wybrane fazy zaliczone, 1 w przeciwnym razie (wynik i tak w installer.json).
#>
[CmdletBinding()]
param(
    [ValidateSet('DSO-26H2-Home', 'DSO-26H2-Pro')][string]$VmName = 'DSO-26H2-Pro',
    # Katalog wydania: DSOptimizer-<wersja>.zip, DSOptimizer-<wersja>-setup.exe i SHA256SUMS.txt.
    [string]$ArtifactDirectory,
    [string]$OutDirectory,
    [ValidateSet('smoke', 'running', 'upgrade', 'wizard', 'motw')][string[]]$Phase = @('smoke', 'running', 'wizard', 'motw'),
    # Instalator wyższej wersji (faza upgrade), obok SHA256SUMS.txt i zip tej wersji.
    [string]$UpgradeInstallerPath,
    [ValidateSet('pl', 'en')][string[]]$Language = @('pl', 'en'),
    [string]$CheckpointName = 'czysta-26H2',
    [ValidateRange(60, 7200)][int]$ReadyTimeoutSeconds = 1200,
    [switch]$ShowPlan
)

$script:InstallerScriptRoot = $PSScriptRoot
Import-Module (Join-Path $PSScriptRoot 'ExploreCommon.psm1') -Force
Import-Module (Join-Path (Split-Path -Parent $PSScriptRoot) 'modules\VmCommon.psm1')
Import-Module (Join-Path (Split-Path -Parent $PSScriptRoot) 'modules\VmSmoke.psm1')
Import-Module (Join-Path (Split-Path -Parent $PSScriptRoot) 'modules\VmRun.psm1')

# ---------------------------------------------------------------------------------------------------------------------
# Plan i werdykt (czyste funkcje, bez dotykania VM).
# ---------------------------------------------------------------------------------------------------------------------

function Get-InstallerPlan {
    [CmdletBinding()]
    param()
    return [ordered]@{
        UserName                 = 'dso'
        # Katalogi w VM bez spacji; jedynym wyjątkiem jest zainstalowany program w Program Files.
        WorkDirectory            = 'C:\dso\instalator'
        PortableDirectory        = 'C:\dso\app'
        PortableExe              = 'C:\dso\app\DSOptimizer.exe'
        OutDirectory             = 'C:\dso\out'
        LogDirectory             = 'C:\dso\out\logs'
        SmokeLogDirectory        = 'C:\dso\out\smoke'
        UserDesktop              = 'C:\Users\dso\Desktop'
        InstallDirectory         = 'C:\Program Files\DSOptimizer'
        InstalledExe             = 'C:\Program Files\DSOptimizer\DSOptimizer.exe'
        ProgramDataDirectory     = 'C:\ProgramData\DSOptimizer'
        SmokeScript              = 'Test-InstallerSmoke.ps1'
        MutexName                = 'Global\DSOptimizer.SingleInstance'
        UninstallSubKey          = 'SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall\{82DBAF33-36AA-4F12-A327-EEC64159D8E8}_is1'
        AdministratorsSid        = 'S-1-5-32-544'
        SilentArguments          = @('/VERYSILENT', '/SUPPRESSMSGBOXES', '/NORESTART', '/SP-')
        UninstallArguments       = @('/VERYSILENT', '/SUPPRESSMSGBOXES', '/NORESTART')
        RefusedExitCode          = 7
        SettleAfterLogonSeconds  = 45
        AppStartSeconds          = 60
        RefusalProbeSeconds      = 30
        TaskFinishSeconds        = 30
        # Relaunch: czekanie na proces C, potem A trzyma mutex jeszcze RelaunchHoldSeconds od wykrycia C (wyraźnie dłużej niż zwykłe
        # czekanie programu 2 s, krócej niż 30 s po --relaunched). Sonda okna odmowy działa w tym czasie (RelaunchProbeSeconds = start C
        # + 8 s + zapas), więc C, który czekałby tylko 2 s, pokaże okno „już działa” przy żywym A i sonda je złapie.
        RelaunchDetectSeconds    = 30
        RelaunchHoldSeconds      = 8
        RelaunchProbeSeconds     = 12
        RelaunchCheckAfterSeconds = 15
        RelaunchAfterProbeSeconds = 5
        MutexReleaseSeconds      = 10
        UninstallSettleSeconds   = 5
        UninstallWaitSeconds     = 60
        ProcessStopSeconds       = 15
        CliTimeoutSeconds        = 300
        WizardProbeSeconds       = 600
        WizardProgramSeconds     = 30
        MotwWaitSeconds          = 10
        MotwZoneId               = 3
        TaskNames                = [ordered]@{ A = 'DSO-Inst-A'; B = 'DSO-Inst-B'; C = 'DSO-Inst-C'; Wizard = 'DSO-Inst-Wizard'; Motw = 'DSO-Inst-Motw' }
        WindowTitle              = 'DSOptimizer'
        OkButton                 = 'OK'
        RefusalPrefix            = 'DSOptimizer już działa'
        CliRefusalPrefix         = 'Działa już inna kopia DSOptimizera'
        CliApplyArguments        = @('--apply', 'privacy.widgets', '--restore-point', 'skip')
        AppListName              = 'DSOptimizer'
        ProtectedGroup           = 'Protected'
        LicenseText              = 'Mozilla Public License 2.0'
        WizardClassName          = 'TWizardForm'
        WizardPageOrder          = @('powitanie', 'zadania', 'gotowe', 'koniec')
        # Teksty kreatora z plików językowych Inno Setup 6.7 (Default.isl / Polish.isl, [name] = DSOptimizer) bez znaku '&'.
        # Polish.isl ma ButtonInstall=&Instaluj; „Zainstaluj” dopuszczone na wypadek innej wersji. Do potwierdzenia na pierwszym przebiegu.
        WizardTexts              = [ordered]@{
            pl = [ordered]@{
                Next        = @('Dalej >')
                Install     = @('Instaluj', 'Zainstaluj')
                Finish      = @('Zakończ')
                Pages       = [ordered]@{ powitanie = 'Witamy w instalatorze aplikacji DSOptimizer'; zadania = 'Zadania dodatkowe'; gotowe = 'Gotowy do rozpoczęcia instalacji'; koniec = 'Zakończono instalację aplikacji DSOptimizer' }
                DesktopTask = 'Utwórz skrót na pulpicie'
                LaunchTask  = 'Uruchom aplikację DSOptimizer'
            }
            en = [ordered]@{
                Next        = @('Next >')
                Install     = @('Install')
                Finish      = @('Finish')
                Pages       = [ordered]@{ powitanie = 'Welcome to the DSOptimizer Setup Wizard'; zadania = 'Select Additional Tasks'; gotowe = 'Ready to Install'; koniec = 'Completing the DSOptimizer Setup Wizard' }
                DesktopTask = 'Create a desktop shortcut'
                LaunchTask  = 'Launch DSOptimizer'
            }
        }
        # Tytuły okna SmartScreen (PL/EN), te same co w Invoke-E5Exploration.ps1.
        SmartScreenTitles        = @('System Windows ochronił ten komputer', 'Windows protected your PC')
    }
}

function Get-InstallerProbeTexts {
    # Teksty dla sond w VM jako JSON (UTF-8 przez -DataFiles): sondy są w ASCII, a teksty mają polskie znaki.
    [CmdletBinding()]
    [OutputType([string])]
    param()
    $Plan = Get-InstallerPlan
    $Wizard = [ordered]@{}
    foreach ($Code in $Plan.WizardTexts.Keys) {
        $Item = $Plan.WizardTexts[$Code]
        $Wizard[$Code] = [ordered]@{ next = @($Item.Next); install = @($Item.Install); finish = @($Item.Finish); desktopTask = $Item.DesktopTask; launchTask = $Item.LaunchTask }
    }
    $Texts = [ordered]@{ installedExe = $Plan.InstalledExe; refusalPrefix = $Plan.RefusalPrefix; windowTitle = $Plan.WindowTitle; okButton = $Plan.OkButton
        wizardClass = $Plan.WizardClassName; wizard = $Wizard }
    return ($Texts | ConvertTo-Json -Depth 6 -Compress)
}

function Format-InstallerUiText {
    # Tekst kontrolki do porównań: bez '&' (klawisz skrótu), białe znaki zwinięte do jednej spacji, bez spacji na brzegach.
    [CmdletBinding()]
    [OutputType([string])]
    param([AllowNull()]$Value)
    return ((([string]$Value) -replace '&', '') -replace '\s+', ' ').Trim()
}

function Get-InstallerWizardPageKind {
    # Rodzaj strony kreatora po dokładnym tytule (Plan.WizardTexts.<język>.Pages): powitanie, zadania, gotowe, koniec;
    # żaden tytuł = 'nieznana', więcej niż jeden = 'niejednoznaczna'.
    [CmdletBinding()]
    [OutputType([string])]
    param([AllowNull()][AllowEmptyCollection()][object[]]$Labels, [AllowNull()][AllowEmptyString()][string]$Language)
    $Plan = Get-InstallerPlan
    if (-not $Language -or -not $Plan.WizardTexts.Contains($Language)) { return 'nieznana' }
    $Normalized = @(@($Labels) | ForEach-Object { Format-InstallerUiText $_ })
    $Titles = $Plan.WizardTexts[$Language].Pages
    $Found = @(foreach ($Kind in $Titles.Keys) { if ($Normalized -ccontains (Format-InstallerUiText $Titles[$Kind])) { $Kind } })
    if ($Found.Count -eq 0) { return 'nieznana' }
    if ($Found.Count -gt 1) { return 'niejednoznaczna' }
    return [string]$Found[0]
}

function Get-InstallerPhaseRuns {
    # Fazy w stałej kolejności, bez duplikatów; kreator osobno dla każdego języka (osobny revert).
    [CmdletBinding()]
    param([Parameter(Mandatory)][AllowEmptyCollection()][string[]]$Phase, [Parameter(Mandatory)][AllowEmptyCollection()][string[]]$Language)
    $Ordered = @(@('smoke', 'running', 'upgrade', 'wizard', 'motw') | Where-Object { $Phase -contains $_ })
    if ($Ordered.Count -eq 0) { throw 'Podaj co najmniej jedną fazę: smoke, running, upgrade, wizard, motw.' }
    $Languages = @(@('pl', 'en') | Where-Object { $Language -contains $_ })
    if ($Ordered -contains 'wizard' -and $Languages.Count -eq 0) { throw 'Faza wizard wymaga -Language pl i/lub en.' }
    $Runs = @()
    foreach ($Name in $Ordered) {
        if ($Name -eq 'wizard') {
            foreach ($Code in $Languages) { $Runs += [ordered]@{ Key = "wizard-$Code"; Phase = 'wizard'; Language = $Code } }
        }
        else { $Runs += [ordered]@{ Key = $Name; Phase = $Name; Language = $null } }
    }
    return $Runs
}

function Get-InstallerSumsTable {
    # SHA256SUMS.txt -> nazwa pliku -> suma (małe litery).
    [CmdletBinding()]
    param([Parameter(Mandatory)][string]$Path)
    $Table = @{}
    foreach ($Line in [System.IO.File]::ReadAllLines($Path)) {
        if ($Line -match '^([0-9a-fA-F]{64}) [ *](.+)$') { $Table[$Matches[2].Trim()] = $Matches[1].ToLowerInvariant() }
    }
    return $Table
}

function Get-InstallerCheckedFile {
    # Plik wydania z sumą zgodną z SHA256SUMS.txt -> { Name; Path; Sha256; Bytes }. Każda niezgodność = wyjątek.
    [CmdletBinding()]
    param([Parameter(Mandatory)][string]$Path, [Parameter(Mandatory)][hashtable]$Sums)
    $Name = [System.IO.Path]::GetFileName($Path)
    if (-not $Sums.ContainsKey($Name)) { throw "SHA256SUMS.txt nie ma sumy pliku '$Name'." }
    $Sha = (Get-FileHash -LiteralPath $Path -Algorithm SHA256 -ErrorAction Stop).Hash.ToLowerInvariant()
    if ($Sha -cne $Sums[$Name]) { throw "SHA256 pliku '$Name' ($Sha) różni się od SHA256SUMS.txt ($($Sums[$Name]))." }
    return [ordered]@{ Name = $Name; Path = $Path; Sha256 = $Sha; Bytes = (New-Object System.IO.FileInfo($Path)).Length }
}

function Test-InstallerAbsolutePath {
    [CmdletBinding()]
    [OutputType([bool])]
    param([AllowEmptyString()][string]$Path)
    return ($Path -match '^[A-Za-z]:\\' -or $Path -match '^\\\\[^\\]+\\[^\\]+')
}

function Resolve-InstallerArtifact {
    # Katalog wydania (zip, instalator, SHA256SUMS.txt; ta sama wersja, sumy zgodne) i opcjonalnie instalator wyższej wersji
    # (faza upgrade: obok niego SHA256SUMS.txt i zip tej wersji). Pliki są tylko czytane; każdy błąd = wyjątek przed VM.
    [CmdletBinding()]
    param(
        [AllowEmptyString()][AllowNull()][string]$ArtifactDirectory,
        [AllowEmptyCollection()][string[]]$Phase = @(),
        [AllowEmptyString()][AllowNull()][string]$UpgradeInstallerPath
    )
    if ([string]::IsNullOrWhiteSpace($ArtifactDirectory)) { throw '-ArtifactDirectory jest wymagany (katalog z DSOptimizer-<wersja>.zip, DSOptimizer-<wersja>-setup.exe i SHA256SUMS.txt).' }
    if (-not (Test-InstallerAbsolutePath -Path $ArtifactDirectory)) { throw "-ArtifactDirectory musi być ścieżką bezwzględną: '$ArtifactDirectory'." }
    $Full = [System.IO.Path]::GetFullPath($ArtifactDirectory).TrimEnd('\')
    if (-not [System.IO.Directory]::Exists($Full)) { throw "Brak katalogu wydania '$Full'." }
    $SumsPath = Join-Path $Full 'SHA256SUMS.txt'
    if (-not [System.IO.File]::Exists($SumsPath)) { throw "Brak SHA256SUMS.txt w katalogu wydania '$Full'." }
    $Sums = Get-InstallerSumsTable -Path $SumsPath
    $Files = @(Get-ChildItem -LiteralPath $Full -File)
    $Zips = @($Files | Where-Object { $_.Name -cmatch '^DSOptimizer-(\d+\.\d+\.\d+)\.zip\z' })
    $Setups = @($Files | Where-Object { $_.Name -cmatch '^DSOptimizer-(\d+\.\d+\.\d+)-setup\.exe\z' })
    if ($Zips.Count -eq 0) { throw "Brak zipu wydania (DSOptimizer-<wersja>.zip) w '$Full'." }
    if ($Zips.Count -gt 1) { throw "W '$Full' jest więcej niż jeden zip wydania: $(@($Zips.Name) -join ', ')." }
    if ($Setups.Count -eq 0) { throw "Brak instalatora (DSOptimizer-<wersja>-setup.exe) w '$Full'." }
    if ($Setups.Count -gt 1) { throw "W '$Full' jest więcej niż jeden instalator: $(@($Setups.Name) -join ', ')." }
    $null = $Zips[0].Name -cmatch '^DSOptimizer-(\d+\.\d+\.\d+)\.zip\z'
    $Version = $Matches[1]
    $null = $Setups[0].Name -cmatch '^DSOptimizer-(\d+\.\d+\.\d+)-setup\.exe\z'
    if ($Matches[1] -cne $Version) { throw "Zip ($Version) i instalator ($($Matches[1])) mają różne wersje." }
    $Zip = Get-InstallerCheckedFile -Path $Zips[0].FullName -Sums $Sums
    $Setup = Get-InstallerCheckedFile -Path $Setups[0].FullName -Sums $Sums

    $Upgrade = $null
    $HasUpgrade = -not [string]::IsNullOrWhiteSpace($UpgradeInstallerPath)
    if (@($Phase) -contains 'upgrade' -and -not $HasUpgrade) { throw 'Faza upgrade wymaga -UpgradeInstallerPath (DSOptimizer-<wyższa wersja>-setup.exe, obok SHA256SUMS.txt i zip tej wersji).' }
    if ($HasUpgrade) {
        if (-not (Test-InstallerAbsolutePath -Path $UpgradeInstallerPath)) { throw "-UpgradeInstallerPath musi być ścieżką bezwzględną: '$UpgradeInstallerPath'." }
        $UpgradeFull = [System.IO.Path]::GetFullPath($UpgradeInstallerPath)
        if (-not [System.IO.File]::Exists($UpgradeFull)) { throw "Brak pliku -UpgradeInstallerPath '$UpgradeFull'." }
        $UpgradeName = [System.IO.Path]::GetFileName($UpgradeFull)
        if ($UpgradeName -cnotmatch '^DSOptimizer-(\d+\.\d+\.\d+)-setup\.exe\z') { throw "Plik '$UpgradeName' nie jest instalatorem (oczekiwano DSOptimizer-<wersja>-setup.exe)." }
        $UpgradeVersion = $Matches[1]
        if ([version]$UpgradeVersion -le [version]$Version) { throw "Wersja -UpgradeInstallerPath ($UpgradeVersion) musi być wyższa niż wersja wydania ($Version)." }
        $UpgradeDirectory = [System.IO.Path]::GetDirectoryName($UpgradeFull)
        $UpgradeSumsPath = Join-Path $UpgradeDirectory 'SHA256SUMS.txt'
        if (-not [System.IO.File]::Exists($UpgradeSumsPath)) { throw "Brak SHA256SUMS.txt obok -UpgradeInstallerPath ('$UpgradeDirectory')." }
        $UpgradeSums = Get-InstallerSumsTable -Path $UpgradeSumsPath
        $UpgradeSetup = Get-InstallerCheckedFile -Path $UpgradeFull -Sums $UpgradeSums
        $UpgradeZipPath = Join-Path $UpgradeDirectory "DSOptimizer-$UpgradeVersion.zip"
        if (-not [System.IO.File]::Exists($UpgradeZipPath)) { throw "Brak zipu DSOptimizer-$UpgradeVersion.zip obok -UpgradeInstallerPath (pliki po aktualizacji są porównywane z tym zipem)." }
        $UpgradeZip = Get-InstallerCheckedFile -Path $UpgradeZipPath -Sums $UpgradeSums
        $Upgrade = [ordered]@{ Version = $UpgradeVersion; Setup = $UpgradeSetup; Zip = $UpgradeZip }
    }
    return [ordered]@{ Version = $Version; Directory = $Full; Zip = $Zip; Setup = $Setup; Upgrade = $Upgrade }
}

function Get-InstallerRunArguments {
    # Argumenty przebiegu rozstrzygane przed jakimkolwiek dotknięciem VM.
    [CmdletBinding()]
    param(
        [Parameter(Mandatory)][string]$VmName,
        [AllowEmptyString()][AllowNull()][string]$ArtifactDirectory,
        [Parameter(Mandatory)][string]$OutDirectory,
        [Parameter(Mandatory)][AllowEmptyCollection()][string[]]$Phase,
        [AllowEmptyString()][AllowNull()][string]$UpgradeInstallerPath,
        [Parameter(Mandatory)][AllowEmptyCollection()][string[]]$Language,
        [Parameter(Mandatory)][string]$CheckpointName,
        [Parameter(Mandatory)][int]$ReadyTimeoutSeconds
    )
    $Runs = @(Get-InstallerPhaseRuns -Phase $Phase -Language $Language)
    $Phases = @($Runs | ForEach-Object { $_.Phase } | Select-Object -Unique)
    $Artifact = Resolve-InstallerArtifact -ArtifactDirectory $ArtifactDirectory -Phase $Phases -UpgradeInstallerPath $UpgradeInstallerPath
    return [ordered]@{ VmName = $VmName; Runs = $Runs; OutDirectory = $OutDirectory; CheckpointName = $CheckpointName; ReadyTimeoutSeconds = $ReadyTimeoutSeconds
        Artifact = $Artifact; Plan = (Get-InstallerPlan) }
}

function Compare-InstallerFiles {
    # Dwie tablice ścieżka względna -> SHA256: lista różnic (brak pliku, inna suma, nadmiarowy plik), posortowana. Pusta = równe.
    [CmdletBinding()]
    param([Parameter(Mandatory)][hashtable]$Expected, [Parameter(Mandatory)][hashtable]$Actual)
    $Differences = @()
    foreach ($Key in @($Expected.Keys | Sort-Object)) {
        if (-not $Actual.ContainsKey($Key)) { $Differences += "brak pliku: $Key" }
        elseif ([string]$Actual[$Key] -ne [string]$Expected[$Key]) { $Differences += "inna suma SHA256: $Key" }
    }
    foreach ($Key in @($Actual.Keys | Sort-Object)) {
        if (-not $Expected.ContainsKey($Key)) { $Differences += "nadmiarowy plik: $Key" }
    }
    return , $Differences
}

function Get-InstallerZipHashes {
    # Zip wydania (folder DSOptimizer\ w środku) -> ścieżka względna w folderze programu -> SHA256 (wielkie litery, jak Get-FileHash).
    [CmdletBinding()]
    param([Parameter(Mandatory)][string]$Path)
    Add-Type -AssemblyName System.IO.Compression.FileSystem
    $Result = @{}
    $Zip = [System.IO.Compression.ZipFile]::OpenRead($Path)
    try {
        foreach ($Entry in $Zip.Entries) {
            if ($Entry.FullName.EndsWith('/')) { continue }
            $Relative = $Entry.FullName -replace '\\', '/'
            if ($Relative.StartsWith('DSOptimizer/')) { $Relative = $Relative.Substring('DSOptimizer/'.Length) }
            $Stream = $Entry.Open()
            try {
                $Sha = [System.Security.Cryptography.SHA256]::Create()
                try { $Hash = [System.BitConverter]::ToString($Sha.ComputeHash($Stream)).Replace('-', '') } finally { $Sha.Dispose() }
            }
            finally { $Stream.Dispose() }
            $Result[$Relative.Replace('/', '\')] = $Hash
        }
    }
    finally { $Zip.Dispose() }
    return $Result
}

function ConvertTo-InstallerHashtable {
    # Tablica z VM (Hashtable albo obiekt po deserializacji) -> Hashtable; $null = pusta.
    [CmdletBinding()]
    [OutputType([hashtable])]
    param([AllowNull()]$Value)
    $Table = @{}
    if ($null -eq $Value) { return $Table }
    if ($Value -is [System.Collections.IDictionary]) { foreach ($Key in @($Value.Keys)) { $Table[[string]$Key] = $Value[$Key] }; return $Table }
    foreach ($Property in $Value.PSObject.Properties) { $Table[$Property.Name] = $Property.Value }
    return $Table
}

function Remove-InstallerUninstallerFiles {
    # Bez plików deinstalatora (unins000.exe/.dat, unins001.dat...): porównanie z zipem; drugi deinstalator ma osobne sprawdzenie.
    [CmdletBinding()]
    [OutputType([hashtable])]
    param([Parameter(Mandatory)][hashtable]$Files)
    $Table = @{}
    foreach ($Key in @($Files.Keys)) { if ([string]$Key -notmatch '^unins\d{3}\.(exe|dat)\z') { $Table[$Key] = $Files[$Key] } }
    return $Table
}

function Get-InstallerValue {
    # Wartość pola po ścieżce 'a.b' w słowniku albo obiekcie; $null, gdy pola brak. Tablice zwracane w całości.
    [CmdletBinding()]
    param([AllowNull()]$Object, [Parameter(Mandatory)][string]$Path)
    $Current = $Object
    foreach ($Part in $Path.Split('.')) {
        if ($null -eq $Current) { return $null }
        if ($Current -is [System.Collections.IDictionary]) {
            if (-not $Current.Contains($Part)) { return $null }
            $Current = $Current[$Part]
        }
        else {
            $Property = $Current.PSObject.Properties[$Part]
            if ($null -eq $Property) { return $null }
            $Current = $Property.Value
        }
    }
    return , $Current
}

function Get-InstallerList {
    # Elementy listy pod ścieżką (po jednym na wyjściu, do @(...)); brak pola = nic.
    [CmdletBinding()]
    param([AllowNull()]$Object, [Parameter(Mandatory)][string]$Path)
    $Value = Get-InstallerValue $Object $Path
    if ($null -eq $Value) { return }
    foreach ($Item in @($Value)) { $Item }
}

function Test-InstallerCode {
    [CmdletBinding()]
    [OutputType([bool])]
    param([AllowNull()]$Value, [Parameter(Mandatory)][int64]$Expected)
    return ($null -ne $Value) -and ([string]$Value -match '^-?\d+\z') -and ([int64][string]$Value -eq $Expected)
}

function Test-InstallerTrue {
    [CmdletBinding()]
    [OutputType([bool])]
    param([AllowNull()]$Value)
    return ($Value -is [bool]) -and $Value
}

function Test-InstallerFalse {
    [CmdletBinding()]
    [OutputType([bool])]
    param([AllowNull()]$Value)
    return ($Value -is [bool]) -and (-not $Value)
}

function Format-InstallerValue {
    [CmdletBinding()]
    [OutputType([string])]
    param([AllowNull()]$Value)
    if ($null -eq $Value) { return '(brak)' }
    if ($Value -is [System.Array]) { return '[' + (@($Value) -join '; ') + ']' }
    return [string]$Value
}

function New-InstallerCheck {
    [CmdletBinding()]
    param([Parameter(Mandatory)][string]$Name, [Parameter(Mandatory)][bool]$Passed, [Parameter(Mandatory)][string]$Detail)
    return [pscustomobject]@{ Name = $Name; Passed = $Passed; Detail = $Detail }
}

function Test-InstallerStartsWith {
    [CmdletBinding()]
    [OutputType([bool])]
    param([AllowNull()]$Value, [Parameter(Mandatory)][string]$Prefix)
    return ($null -ne $Value) -and ([string]$Value).StartsWith($Prefix, [System.StringComparison]::Ordinal)
}

function Test-InstallerEmptyList {
    # Lista różnic odczytana (nie $null) i pusta.
    [CmdletBinding()]
    [OutputType([bool])]
    param([AllowNull()]$Value)
    return ($null -ne $Value) -and (@($Value | Where-Object { $null -ne $_ }).Count -eq 0)
}

function Get-InstallerSmokeVerdict {
    [CmdletBinding()]
    param([AllowNull()]$Data)
    $Code = Get-InstallerValue $Data 'exitCode'
    $Passed = Get-InstallerValue $Data 'result.passed'
    $Failed = @(Get-InstallerList $Data 'result.checks' | Where-Object { $null -ne $_ -and -not (Test-InstallerTrue (Get-InstallerValue $_ 'passed')) } | ForEach-Object { [string](Get-InstallerValue $_ 'name') })
    $Detail = "Test-InstallerSmoke.ps1: kod $(Format-InstallerValue $Code), passed $(Format-InstallerValue $Passed)"
    if ($Failed.Count -gt 0) { $Detail += "; niezaliczone: $($Failed -join '; ')" }
    return @(New-InstallerCheck -Name 'Smoke' -Passed ((Test-InstallerCode $Code 0) -and (Test-InstallerTrue $Passed)) -Detail $Detail)
}

function Get-InstallerRunningVerdict {
    [CmdletBinding()]
    param([AllowNull()]$Data)
    $Plan = Get-InstallerPlan
    $Checks = @()
    $V = { param([string]$Path) Get-InstallerValue $Data $Path }

    $Code = & $V 'install.exitCode'
    $Checks += New-InstallerCheck -Name 'InstallExit' -Passed (Test-InstallerCode $Code 0) -Detail "cicha instalacja: kod $(Format-InstallerValue $Code) (oczekiwano 0)"

    $Found = & $V 'windowA.processFound'; $Mutex = & $V 'windowA.mutex'
    $Checks += New-InstallerCheck -Name 'WindowA' -Passed ((Test-InstallerTrue $Found) -and (Test-InstallerTrue $Mutex)) `
        -Detail "okno A z $($Plan.InstalledExe): proces $(Format-InstallerValue $Found), mutex $(Format-InstallerValue $Mutex) (w $($Plan.AppStartSeconds) s)"

    $Message = & $V 'portable.message'; $Clicked = & $V 'portable.clickedOk'; $ProbeErrorB = & $V 'portable.probeError'
    $MessageDetail = "kopia przenośna B: okno '$($Plan.WindowTitle)' z tekstem '$(Format-InstallerValue $Message)', OK kliknięte: $(Format-InstallerValue $Clicked) (oczekiwano tekstu od '$($Plan.RefusalPrefix)')"
    if ($ProbeErrorB) { $MessageDetail += "; błąd sondy: $ProbeErrorB" }
    $Checks += New-InstallerCheck -Name 'PortableMessage' -Passed ((Test-InstallerStartsWith $Message $Plan.RefusalPrefix) -and (Test-InstallerTrue $Clicked) -and -not $ProbeErrorB) -Detail $MessageDetail
    $TaskResult = & $V 'portable.taskResult'
    $Checks += New-InstallerCheck -Name 'PortableExit' -Passed (Test-InstallerCode $TaskResult 1) -Detail "zadanie kopii B: LastTaskResult $(Format-InstallerValue $TaskResult) (oczekiwano 1)"
    $AAlive = & $V 'portable.aAlive'
    $Checks += New-InstallerCheck -Name 'WindowAAlive' -Passed (Test-InstallerTrue $AAlive) -Detail "okno A po odmowie kopii B żyje: $(Format-InstallerValue $AAlive)"

    $CliCode = & $V 'cliApply.exitCode'; $CliError = & $V 'cliApply.firstError'
    $Checks += New-InstallerCheck -Name 'CliRefused' -Passed ((Test-InstallerCode $CliCode 1) -and (Test-InstallerStartsWith $CliError $Plan.CliRefusalPrefix)) `
        -Detail "CLI $(@($Plan.CliApplyArguments) -join ' ') jako dso: kod $(Format-InstallerValue $CliCode), errors[0] '$(Format-InstallerValue $CliError)' (oczekiwano 1 i '$($Plan.CliRefusalPrefix)…')"

    $ListCode = & $V 'listApps.exitCode'; $Group = & $V 'listApps.group'
    $Checks += New-InstallerCheck -Name 'ListAppsProtected' -Passed ((Test-InstallerCode $ListCode 0) -and ([string]$Group -ceq $Plan.ProtectedGroup)) `
        -Detail "--list-apps jako dso: kod $(Format-InstallerValue $ListCode), grupa wiersza '$($Plan.AppListName)': $(Format-InstallerValue $Group) (oczekiwano $($Plan.ProtectedGroup))"

    $ReCode = & $V 'reinstall.exitCode'
    $Checks += New-InstallerCheck -Name 'ReinstallRefused' -Passed (Test-InstallerCode $ReCode $Plan.RefusedExitCode) -Detail "instalator przy działającym oknie: kod $(Format-InstallerValue $ReCode) (oczekiwano $($Plan.RefusedExitCode))"
    $Before = & $V 'reinstall.versionBefore'; $After = & $V 'reinstall.versionAfter'; $ReDiff = & $V 'reinstall.filesDiff'
    $Checks += New-InstallerCheck -Name 'ReinstallUnchanged' -Passed ((-not [string]::IsNullOrEmpty([string]$Before)) -and ([string]$Before -ceq [string]$After) -and (Test-InstallerEmptyList $ReDiff)) `
        -Detail "po odmowie: DisplayVersion $(Format-InstallerValue $Before) -> $(Format-InstallerValue $After), różnice plików $(Format-InstallerValue $ReDiff)"

    $UnDiff = & $V 'uninstallRunning.filesDiff'; $KeyPresent = & $V 'uninstallRunning.keyPresent'; $UnCode = & $V 'uninstallRunning.exitCode'
    $Checks += New-InstallerCheck -Name 'UninstallRefusedKeeps' -Passed ((Test-InstallerEmptyList $UnDiff) -and (Test-InstallerTrue $KeyPresent)) `
        -Detail "deinstalator przy działającym oknie (kod $(Format-InstallerValue $UnCode)): różnice plików $(Format-InstallerValue $UnDiff), klucz Uninstall jest: $(Format-InstallerValue $KeyPresent)"

    # Relaunch dowodzi czekania tylko wtedy, gdy A żyło z mutexem przed startem C i tuż przed zamknięciem, C wystartowało, A trzymało
    # mutex co najmniej RelaunchHoldSeconds od wykrycia C (dłużej niż zwykłe czekanie 2 s), zamknięto żywe A, a C potem żyje.
    $HoldValue = & $V 'relaunch.holdSeconds'
    $HoldOk = ($null -ne $HoldValue) -and ([string]$HoldValue -match '^\d+\z') -and ([int64][string]$HoldValue -ge [int64]$Plan.RelaunchHoldSeconds)
    $Conditions = [ordered]@{
        "A żyło przed startem C"                         = (Test-InstallerTrue (& $V 'relaunch.aAliveBeforeC'))
        "mutex był przed startem C"                      = (Test-InstallerTrue (& $V 'relaunch.mutexBeforeC'))
        "C wykryte"                                      = (Test-InstallerTrue (& $V 'relaunch.cDetected'))
        "A trzymało mutex co najmniej $($Plan.RelaunchHoldSeconds) s od wykrycia C" = $HoldOk
        "A żyło tuż przed zamknięciem"                   = (Test-InstallerTrue (& $V 'relaunch.aAliveBeforeStop'))
        "mutex był tuż przed zamknięciem A"              = (Test-InstallerTrue (& $V 'relaunch.mutexBeforeStop'))
        "zamknięto żywe A"                               = (Test-InstallerTrue (& $V 'relaunch.aStopped'))
        "C z --relaunched żyje $($Plan.RelaunchCheckAfterSeconds) s po zamknięciu A" = (Test-InstallerTrue (& $V 'relaunch.cAlive'))
    }
    $Missing = @($Conditions.Keys | Where-Object { -not $Conditions[$_] })
    $AliveDetail = if ($Missing.Count -eq 0) { "relaunch: $(@($Conditions.Keys) -join '; ') (trzymanie $(& $V 'relaunch.holdSeconds') s)" } else { "relaunch: niespełnione: $($Missing -join '; ') (trzymanie $(Format-InstallerValue (& $V 'relaunch.holdSeconds')) s)" }
    $Checks += New-InstallerCheck -Name 'RelaunchAlive' -Passed ($Missing.Count -eq 0) -Detail $AliveDetail
    $CMutex = & $V 'relaunch.mutex'
    $Checks += New-InstallerCheck -Name 'RelaunchMutex' -Passed (Test-InstallerTrue $CMutex) -Detail "relaunch: mutex $($Plan.MutexName) jest: $(Format-InstallerValue $CMutex)"
    $Seen = & $V 'relaunch.refusalSeen'; $ProbeErrorC = & $V 'relaunch.probeError'
    $SeenDetail = "relaunch: okno '$($Plan.RefusalPrefix)…' widoczne (sonda w czasie trzymania mutexa przez A i po jego zamknięciu): $(Format-InstallerValue $Seen) (oczekiwano False)"
    if ($ProbeErrorC) { $SeenDetail += "; błąd sondy: $ProbeErrorC" }
    $Checks += New-InstallerCheck -Name 'RelaunchNoRefusal' -Passed ((Test-InstallerFalse $Seen) -and -not $ProbeErrorC) -Detail $SeenDetail

    $Released = & $V 'final.mutexReleased'
    $Checks += New-InstallerCheck -Name 'MutexReleased' -Passed (Test-InstallerTrue $Released) -Detail "mutex znika w $($Plan.MutexReleaseSeconds) s po zamknięciu C: $(Format-InstallerValue $Released)"
    $FinalCode = & $V 'final.exitCode'
    $Checks += New-InstallerCheck -Name 'UninstallExit' -Passed (Test-InstallerCode $FinalCode 0) -Detail "cicha deinstalacja: kod $(Format-InstallerValue $FinalCode) (oczekiwano 0)"
    $Left = @()
    foreach ($Item in @(@('final.appDirGone', 'folder programu'), @('final.keyGone', 'klucz Uninstall'), @('final.startMenuGone', 'skrót w Menu Start'), @('final.desktopGone', 'skrót na pulpicie'))) {
        if (-not (Test-InstallerTrue (& $V $Item[0]))) { $Left += $Item[1] }
    }
    $RemovedDetail = if ($Left.Count -eq 0) { "po deinstalacji (do $($Plan.UninstallWaitSeconds) s) nie ma folderu programu, klucza Uninstall ani skrótów" } else { "po deinstalacji zostało (albo brak odczytu): $($Left -join ', ')" }
    $Checks += New-InstallerCheck -Name 'UninstallRemoved' -Passed ($Left.Count -eq 0) -Detail $RemovedDetail

    $DataBefore = & $V 'final.dataBefore'; $DataAfter = & $V 'final.dataAfter'
    $BeforeList = @(@($DataBefore) | Where-Object { $null -ne $_ } | ForEach-Object { [string]$_ } | Sort-Object)
    $AfterList = @(@($DataAfter) | Where-Object { $null -ne $_ } | ForEach-Object { [string]$_ } | Sort-Object)
    $Same = ($null -ne $DataBefore) -and ($null -ne $DataAfter) -and ($BeforeList.Count -gt 0) -and (($BeforeList -join "`n") -ceq ($AfterList -join "`n"))
    $KeptDetail = if ($null -eq $DataAfter) { "$($Plan.ProgramDataDirectory) po deinstalacji nie istnieje (albo brak odczytu)" }
    elseif ($BeforeList.Count -eq 0) { "$($Plan.ProgramDataDirectory) był pusty przed deinstalacją: brak dowodu, że zostaje" }
    else { "$($Plan.ProgramDataDirectory): plików przed $($BeforeList.Count), po $($AfterList.Count), te same: $Same" }
    $Checks += New-InstallerCheck -Name 'ProgramDataKept' -Passed $Same -Detail $KeptDetail
    $Owner = & $V 'final.ownerAfter'
    $Checks += New-InstallerCheck -Name 'ProgramDataOwner' -Passed ([string]$Owner -ceq $Plan.AdministratorsSid) -Detail "właściciel $($Plan.ProgramDataDirectory) po deinstalacji: $(Format-InstallerValue $Owner) (oczekiwano $($Plan.AdministratorsSid))"
    return $Checks
}

function Get-InstallerUpgradeVerdict {
    [CmdletBinding()]
    param([AllowNull()]$Data)
    $Plan = Get-InstallerPlan
    $Checks = @()
    $V = { param([string]$Path) Get-InstallerValue $Data $Path }
    $Release = [string](& $V 'releaseVersion'); $Target = [string](& $V 'upgradeVersion')

    $Code = & $V 'install.exitCode'
    $Checks += New-InstallerCheck -Name 'InstallExit' -Passed (Test-InstallerCode $Code 0) -Detail "cicha instalacja wydania $(Format-InstallerValue $Release): kod $(Format-InstallerValue $Code) (oczekiwano 0)"
    $Existed = & $V 'desktopRemoved.existed'; $Gone = & $V 'desktopRemoved.gone'
    $Checks += New-InstallerCheck -Name 'DesktopIconRemoved' -Passed ((Test-InstallerTrue $Existed) -and (Test-InstallerTrue $Gone)) -Detail "skrót na pulpicie po instalacji był: $(Format-InstallerValue $Existed), usunięty: $(Format-InstallerValue $Gone)"
    $Found = & $V 'windowA.processFound'; $Mutex = & $V 'windowA.mutex'
    $Checks += New-InstallerCheck -Name 'WindowA' -Passed ((Test-InstallerTrue $Found) -and (Test-InstallerTrue $Mutex)) -Detail "okno A: proces $(Format-InstallerValue $Found), mutex $(Format-InstallerValue $Mutex)"
    $RunCode = & $V 'upgradeRunning.exitCode'
    $Checks += New-InstallerCheck -Name 'UpgradeRefused' -Passed (Test-InstallerCode $RunCode $Plan.RefusedExitCode) -Detail "instalator $(Format-InstallerValue $Target) przy działającym oknie: kod $(Format-InstallerValue $RunCode) (oczekiwano $($Plan.RefusedExitCode))"
    $VBefore = & $V 'upgradeRunning.versionBefore'; $VAfter = & $V 'upgradeRunning.versionAfter'
    $Checks += New-InstallerCheck -Name 'UpgradeRefusedVersion' -Passed ((-not [string]::IsNullOrEmpty($Release)) -and ([string]$VBefore -ceq $Release) -and ([string]$VAfter -ceq $Release)) `
        -Detail "DisplayVersion przy odmowie: $(Format-InstallerValue $VBefore) -> $(Format-InstallerValue $VAfter) (oczekiwano $(Format-InstallerValue $Release))"
    $RefusedDiff = & $V 'upgradeRunning.filesDiff'
    $Checks += New-InstallerCheck -Name 'UpgradeRefusedUnchanged' -Passed (Test-InstallerEmptyList $RefusedDiff) -Detail "pliki programu po odmowie aktualizacji (przed -> po): różnice $(Format-InstallerValue $RefusedDiff)"
    $Released = & $V 'mutexReleased'
    $Checks += New-InstallerCheck -Name 'MutexReleased' -Passed (Test-InstallerTrue $Released) -Detail "mutex znika w $($Plan.MutexReleaseSeconds) s po zamknięciu A: $(Format-InstallerValue $Released)"
    $UpCode = & $V 'upgrade.exitCode'
    $Checks += New-InstallerCheck -Name 'UpgradeExit' -Passed (Test-InstallerCode $UpCode 0) -Detail "aktualizacja do $(Format-InstallerValue $Target): kod $(Format-InstallerValue $UpCode) (oczekiwano 0)"
    $NewVersion = & $V 'upgrade.versionAfter'
    $Checks += New-InstallerCheck -Name 'UpgradeVersion' -Passed ((-not [string]::IsNullOrEmpty($Target)) -and ([string]$NewVersion -ceq $Target)) -Detail "DisplayVersion po aktualizacji: $(Format-InstallerValue $NewVersion) (oczekiwano $(Format-InstallerValue $Target))"
    $Desktop = & $V 'upgrade.desktopLink'
    $Checks += New-InstallerCheck -Name 'DesktopIconNotRestored' -Passed (Test-InstallerFalse $Desktop) -Detail "skrót na pulpicie po aktualizacji jest: $(Format-InstallerValue $Desktop) (oczekiwano False)"
    $StartMenu = & $V 'upgrade.startMenuLink'
    $Checks += New-InstallerCheck -Name 'StartMenuLink' -Passed (Test-InstallerTrue $StartMenu) -Detail "skrót w Menu Start po aktualizacji jest: $(Format-InstallerValue $StartMenu)"
    $Diff = & $V 'upgrade.filesDiff'
    $Checks += New-InstallerCheck -Name 'UpgradeFiles' -Passed (Test-InstallerEmptyList $Diff) -Detail "pliki programu (bez deinstalatora) kontra zip $(Format-InstallerValue $Target): różnice $(Format-InstallerValue $Diff)"
    $Second = & $V 'upgrade.unins001'
    $Checks += New-InstallerCheck -Name 'SingleUninstaller' -Passed (Test-InstallerFalse $Second) -Detail "unins001.dat jest: $(Format-InstallerValue $Second) (oczekiwano False)"
    return $Checks
}

function Get-InstallerWizardVerdict {
    [CmdletBinding()]
    param([AllowNull()]$Data)
    $Plan = Get-InstallerPlan
    $Checks = @()
    $Language = [string](Get-InstallerValue $Data 'language')
    $Pages = @(Get-InstallerList $Data 'pages' | Where-Object { $null -ne $_ })
    $Kinds = @($Pages | ForEach-Object { Get-InstallerWizardPageKind -Labels @(Get-InstallerList $_ 'labels') -Language $Language })
    $Expected = @($Plan.WizardPageOrder)
    $Checks += New-InstallerCheck -Name 'WizardPages' -Passed ($Kinds.Count -gt 0 -and (($Kinds -join ',') -ceq ($Expected -join ','))) `
        -Detail "strony kreatora ($(Format-InstallerValue $Language)): $(if ($Kinds.Count -gt 0) { $Kinds -join ' -> ' } else { '(brak)' }) (oczekiwano $($Expected -join ' -> '))"

    $Welcome = $null
    for ($Index = 0; $Index -lt $Pages.Count; $Index++) { if ($Kinds[$Index] -eq 'powitanie') { $Welcome = $Pages[$Index]; break } }
    $WelcomeText = if ($null -ne $Welcome) { Format-InstallerUiText (@(Get-InstallerList $Welcome 'labels') -join ' ') } else { '' }
    $HasLicense = $WelcomeText.Contains($Plan.LicenseText)
    $LicenseDetail = if ($null -eq $Welcome) { 'brak strony powitania' } else { "tekst powitania zawiera '$($Plan.LicenseText)': $HasLicense" }
    $Checks += New-InstallerCheck -Name 'WelcomeLicense' -Passed (($null -ne $Welcome) -and $HasLicense) -Detail $LicenseDetail

    $Desktop = Get-InstallerValue $Data 'desktopTaskChecked'
    $Checks += New-InstallerCheck -Name 'DesktopTaskChecked' -Passed (Test-InstallerTrue $Desktop) -Detail "pole zadania pulpitu zaznaczone: $(Format-InstallerValue $Desktop) (odczyt: $(Format-InstallerValue (Get-InstallerValue $Data 'desktopTaskSource')))"
    $Launch = Get-InstallerValue $Data 'launchChecked'
    $Checks += New-InstallerCheck -Name 'LaunchChecked' -Passed (Test-InstallerTrue $Launch) -Detail "pole 'Uruchom…' na ostatniej stronie zaznaczone: $(Format-InstallerValue $Launch) (odczyt: $(Format-InstallerValue (Get-InstallerValue $Data 'launchSource')))"
    $Started = Get-InstallerValue $Data 'programStarted'
    $Checks += New-InstallerCheck -Name 'ProgramStarted' -Passed (Test-InstallerTrue $Started) -Detail "po 'Zakończ' w $($Plan.WizardProgramSeconds) s działa $($Plan.InstalledExe): $(Format-InstallerValue $Started)"

    $Copied = @(Get-InstallerList $Data 'screenshots' | Where-Object { $null -ne $_ } | ForEach-Object { [string]$_ })
    $Missing = @($Pages | ForEach-Object { [string](Get-InstallerValue $_ 'screenshot') } | Where-Object { $Copied -notcontains $_ })
    $ShotsDetail = if ($Pages.Count -eq 0) { 'brak stron, więc brak zrzutów' } elseif ($Missing.Count -eq 0) { "zrzuty wszystkich $($Pages.Count) stron skopiowane" } else { "brak zrzutów: $($Missing -join ', ')" }
    $Checks += New-InstallerCheck -Name 'Screenshots' -Passed ($Pages.Count -gt 0 -and $Missing.Count -eq 0) -Detail $ShotsDetail
    return $Checks
}

function Get-InstallerMotwVerdict {
    [CmdletBinding()]
    param([AllowNull()]$Data)
    $Plan = Get-InstallerPlan
    $Zone = Get-InstallerValue $Data 'zoneId'
    $Titles = @(Get-InstallerList $Data 'titles' | Where-Object { $null -ne $_ } | ForEach-Object { [string]$_ })
    $Matched = @($Titles | Where-Object { $Plan.SmartScreenTitles -ccontains $_ } | Select-Object -Unique)
    $Shot = Get-InstallerValue $Data 'screenshot'
    return @(
        (New-InstallerCheck -Name 'MotwZone' -Passed (Test-InstallerCode $Zone $Plan.MotwZoneId) -Detail "Zone.Identifier instalatora na pulpicie: ZoneId $(Format-InstallerValue $Zone) (oczekiwano $($Plan.MotwZoneId))")
        (New-InstallerCheck -Name 'SmartScreenWindow' -Passed ($Matched.Count -gt 0) -Detail $(if ($Matched.Count -gt 0) { "okno SmartScreen: '$($Matched -join "', '")'" } else { "brak okna o tytule '$($Plan.SmartScreenTitles -join "' / '")'; okna: $(if ($Titles.Count -gt 0) { $Titles -join ' | ' } else { '(brak)' })" }))
        (New-InstallerCheck -Name 'Screenshot' -Passed (Test-InstallerTrue $Shot) -Detail "zrzut motw.png skopiowany: $(Format-InstallerValue $Shot)")
    )
}

function Get-InstallerVerdict {
    # Czysta funkcja werdyktu (fail-closed): faza + dane z przebiegu -> lista { Name; Passed; Detail }. Brak pola = niezaliczone.
    [CmdletBinding()]
    param(
        [Parameter(Mandatory)][ValidateSet('smoke', 'running', 'upgrade', 'wizard', 'motw')][string]$Phase,
        [Parameter(Mandatory)][AllowNull()]$Data
    )
    switch ($Phase) {
        'smoke' { return @(Get-InstallerSmokeVerdict -Data $Data) }
        'running' { return @(Get-InstallerRunningVerdict -Data $Data) }
        'upgrade' { return @(Get-InstallerUpgradeVerdict -Data $Data) }
        'wizard' { return @(Get-InstallerWizardVerdict -Data $Data) }
        'motw' { return @(Get-InstallerMotwVerdict -Data $Data) }
    }
}

# ---------------------------------------------------------------------------------------------------------------------
# Sondy uruchamiane w VM w sesji 'dso' (zadania Harmonogramu, tylko ASCII, PowerShell 5.1).
# ---------------------------------------------------------------------------------------------------------------------

function Get-InstallerProbeHelpersText {
    # Wspólne funkcje sond: strażnik VM i Win32/MSAA (EnumWindows, EnumChildWindows, WM_GETTEXT, GetWindowRect, oleacc). Tylko odczyt.
    [CmdletBinding()]
    [OutputType([string])]
    param()
    return @'
if ($env:DSO_VM_TESTS_MOCK_ONLY) { throw 'DSO_VM_TESTS_MOCK_ONLY: sonda nie dziala w testach.' }
if (@('DSO-26H2-HOME', 'DSO-26H2-PRO') -notcontains ([string]$env:COMPUTERNAME).ToUpperInvariant()) { throw "Sonda dziala tylko w VM DSO-26H2-HOME/PRO (komputer: '$env:COMPUTERNAME')." }
if (-not ('DsoInst.Native' -as [type])) {
    Add-Type -ReferencedAssemblies 'Accessibility' -TypeDefinition @"
using System;
using System.Collections.Generic;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Text;
namespace DsoInst
{
    public static class Native
    {
        public delegate bool EnumProc(IntPtr hwnd, IntPtr lParam);
        [StructLayout(LayoutKind.Sequential)] public struct RECT { public int Left; public int Top; public int Right; public int Bottom; }
        [DllImport("user32.dll")] private static extern bool EnumWindows(EnumProc callback, IntPtr lParam);
        [DllImport("user32.dll")] private static extern bool EnumChildWindows(IntPtr parent, EnumProc callback, IntPtr lParam);
        [DllImport("user32.dll")] public static extern bool IsWindowVisible(IntPtr hwnd);
        [DllImport("user32.dll")] public static extern bool GetWindowRect(IntPtr hwnd, out RECT rect);
        [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern int GetClassName(IntPtr hwnd, StringBuilder text, int max);
        [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern int GetWindowText(IntPtr hwnd, StringBuilder text, int max);
        [DllImport("user32.dll")] private static extern uint GetWindowThreadProcessId(IntPtr hwnd, out uint processId);
        [DllImport("user32.dll", CharSet = CharSet.Unicode, EntryPoint = "SendMessageTimeoutW")] private static extern IntPtr SendText(IntPtr hwnd, uint msg, IntPtr wParam, StringBuilder lParam, uint flags, uint timeout, out IntPtr result);
        [DllImport("user32.dll", EntryPoint = "SendMessageTimeoutW")] private static extern IntPtr SendValue(IntPtr hwnd, uint msg, IntPtr wParam, IntPtr lParam, uint flags, uint timeout, out IntPtr result);
        [DllImport("oleacc.dll")] private static extern int AccessibleObjectFromWindow(IntPtr hwnd, uint objectId, ref Guid iid, [MarshalAs(UnmanagedType.IUnknown)] out object accessible);

        public static string ClassOf(IntPtr hwnd) { var text = new StringBuilder(256); GetClassName(hwnd, text, text.Capacity); return text.ToString(); }
        public static string TitleOf(IntPtr hwnd) { var text = new StringBuilder(1024); GetWindowText(hwnd, text, text.Capacity); return text.ToString(); }
        public static int ProcessOf(IntPtr hwnd) { uint id; GetWindowThreadProcessId(hwnd, out id); return (int)id; }

        public static string TextOf(IntPtr hwnd)
        {
            IntPtr length;
            if (SendValue(hwnd, 0x000E, IntPtr.Zero, IntPtr.Zero, 0x0002, 2000, out length) == IntPtr.Zero) { return null; }
            int size = (int)length;
            if (size <= 0) { return string.Empty; }
            var text = new StringBuilder(size + 1);
            IntPtr copied;
            if (SendText(hwnd, 0x000D, (IntPtr)(size + 1), text, 0x0002, 2000, out copied) == IntPtr.Zero) { return null; }
            return text.ToString();
        }

        public static IntPtr[] TopWindows() { var list = new List<IntPtr>(); EnumWindows((h, l) => { list.Add(h); return true; }, IntPtr.Zero); return list.ToArray(); }
        public static IntPtr[] Children(IntPtr parent) { var list = new List<IntPtr>(); EnumChildWindows(parent, (h, l) => { list.Add(h); return true; }, IntPtr.Zero); return list.ToArray(); }

        // Ostatni blad odczytu MSAA (CheckItems nigdy nie rzuca; sonda zapisuje ten tekst w wyniku).
        public static string LastError;

        // MSAA (oleacc) klienta okna: nazwa i stan kazdego dziecka (STATE_SYSTEM_CHECKED = 0x10); stan -1 = brak odczytu.
        // Najpierw wczesne wiazanie (Accessibility.IAccessible), przy bledzie pozne (IDispatch). Kazdy wyjatek (np. E_NOTIMPL) = brak odczytu.
        public static object[] CheckItems(IntPtr hwnd)
        {
            LastError = null;
            var result = new List<object>();
            try
            {
                var iid = new Guid("618736E0-3C3D-11CF-810C-00AA00389B71");
                object accessible;
                int hr = AccessibleObjectFromWindow(hwnd, 0xFFFFFFFC, ref iid, out accessible);
                if (hr != 0 || accessible == null) { LastError = "AccessibleObjectFromWindow: 0x" + hr.ToString("X8"); return new object[0]; }
                var early = accessible as Accessibility.IAccessible;
                var type = accessible.GetType();
                int count = 0;
                try { count = early != null ? early.accChildCount : Convert.ToInt32(type.InvokeMember("accChildCount", BindingFlags.GetProperty, null, accessible, null)); }
                catch (Exception error) { LastError = "accChildCount: " + error.Message; return new object[0]; }
                for (int child = 1; child <= count; child++)
                {
                    string name = null;
                    int state = -1;
                    try { name = early != null ? early.get_accName(child) : Convert.ToString(type.InvokeMember("accName", BindingFlags.GetProperty, null, accessible, new object[] { child })); }
                    catch (Exception error) { LastError = "accName(" + child + "): " + error.Message; }
                    try { state = Convert.ToInt32(early != null ? early.get_accState(child) : type.InvokeMember("accState", BindingFlags.GetProperty, null, accessible, new object[] { child })); }
                    catch (Exception error) { LastError = "accState(" + child + "): " + error.Message; }
                    result.Add(new object[] { name, state });
                }
            }
            catch (Exception error) { LastError = error.Message; }
            return result.ToArray();
        }
    }
}
"@
}
function Format-UiText([AllowNull()]$Value) { return ((([string]$Value) -replace '&', '') -replace '\s+', ' ').Trim() }
function Write-ProbeResult($Result, [string]$Path) {
    $Tmp = "$Path.tmp"
    [System.IO.File]::WriteAllText($Tmp, ($Result | ConvertTo-Json -Depth 8), (New-Object System.Text.UTF8Encoding($false)))
    Move-Item -LiteralPath $Tmp -Destination $Path -Force
}
'@
}

function Get-InstallerRefusalProbeScript {
    # Sonda okna odmowy: do -WaitSeconds szuka okna najwyższego poziomu „DSOptimizer” (klasa #32770, MessageBox) z tekstem od
    # refusalPrefix; z -Click klika w nim wyłącznie przycisk okButton. Zwraca Found, Text, Clicked, ProcessId i widziane teksty.
    [CmdletBinding()]
    [OutputType([string])]
    param()
    $Body = @'
$Out = [ordered]@{ Ok = $false; Error = $null; Found = $false; Text = $null; Clicked = $false; ProcessId = $null; WaitSeconds = $WaitSeconds; Seen = @()
    UiaPasses = 0; UiaFailures = 0; LastUiaError = $null }
try {
    $Texts = [System.IO.File]::ReadAllText($TextsFile, [System.Text.Encoding]::UTF8) | ConvertFrom-Json
    Add-Type -AssemblyName UIAutomationClient, UIAutomationTypes
    $AE = [System.Windows.Automation.AutomationElement]
    $Scope = [System.Windows.Automation.TreeScope]
    $ByWindow = New-Object System.Windows.Automation.AndCondition(
        (New-Object System.Windows.Automation.PropertyCondition($AE::NameProperty, [string]$Texts.windowTitle)),
        (New-Object System.Windows.Automation.PropertyCondition($AE::ClassNameProperty, '#32770')))
    $ByText = New-Object System.Windows.Automation.PropertyCondition($AE::ControlTypeProperty, [System.Windows.Automation.ControlType]::Text)
    $ByOk = New-Object System.Windows.Automation.AndCondition(
        (New-Object System.Windows.Automation.PropertyCondition($AE::ControlTypeProperty, [System.Windows.Automation.ControlType]::Button)),
        (New-Object System.Windows.Automation.PropertyCondition($AE::NameProperty, [string]$Texts.okButton)))
    $Seen = New-Object System.Collections.Generic.List[string]
    $Match = $null
    $Passes = 0
    $Failures = 0
    $LastUiaError = $null
    $Limit = (Get-Date).AddSeconds($WaitSeconds)
    do {
        $Passes++
        try {
            foreach ($Window in @($AE::RootElement.FindAll($Scope::Children, $ByWindow))) {
                foreach ($Label in @($Window.FindAll($Scope::Descendants, $ByText))) {
                    $Name = [string]$Label.Current.Name
                    if (-not $Seen.Contains($Name)) { $Seen.Add($Name) }
                    if ($Name.StartsWith([string]$Texts.refusalPrefix, [System.StringComparison]::Ordinal)) { $Match = $Window; $Out.Text = $Name; break }
                }
                if ($null -ne $Match) { break }
            }
        }
        catch { $Match = $null; $Failures++; $LastUiaError = [string]$_.Exception.GetBaseException().Message }
        if ($null -ne $Match) { break }
        Start-Sleep -Milliseconds 250
    } while ((Get-Date) -lt $Limit)
    $Out.UiaPasses = $Passes
    $Out.UiaFailures = $Failures
    $Out.LastUiaError = $LastUiaError
    # UI Automation zawiodlo w kazdym przebiegu: to brak odczytu (Ok=false), nie "brak okna".
    if ($null -eq $Match -and $Passes -gt 0 -and $Failures -eq $Passes) { throw "UI Automation zawiodlo w kazdym z $Passes przebiegow: $LastUiaError" }
    if ($null -ne $Match) {
        $Out.Found = $true
        $Out.ProcessId = [int]$Match.Current.ProcessId
        if ($Click) {
            $Button = $Match.FindFirst($Scope::Descendants, $ByOk)
            if ($null -eq $Button) { throw "Okno odmowy bez przycisku '$($Texts.okButton)'." }
            $Button.GetCurrentPattern([System.Windows.Automation.InvokePattern]::Pattern).Invoke()
            $Out.Clicked = $true
        }
    }
    $Out.Seen = $Seen.ToArray()
    $Out.Ok = $true
}
catch { $Out.Error = [string]$_.Exception.GetBaseException().Message }
Write-ProbeResult $Out $ResultFile
'@
    return ("param([Parameter(Mandatory)][string]`$ResultFile, [Parameter(Mandatory)][string]`$TextsFile, [int]`$WaitSeconds = 30, [switch]`$Click)`n`$ErrorActionPreference = 'Stop'`n" +
        "try {`n" + (Get-InstallerProbeHelpersText) + "`n} catch { [System.IO.File]::WriteAllText(`$ResultFile, (@{ Ok = `$false; Error = [string]`$_.Exception.Message } | ConvertTo-Json)); return }`n" + $Body)
}

function Get-InstallerWizardProbeScript {
    # Sonda kreatora: okno klasy TWizardForm, przyciski po nazwie UIA (finish > install > next), przed każdym kliknięciem zrzut okna
    # (CopyFromScreen) i teksty widocznych kontrolek (WM_GETTEXT), stan pól wyboru przez MSAA (zapasowo UIA TogglePattern),
    # pole pulpitu też z podsumowania strony „gotowe”. Po „Zakończ” czeka na DSOptimizer.exe z Program Files.
    [CmdletBinding()]
    [OutputType([string])]
    param()
    $Body = @'
$Pages = New-Object System.Collections.Generic.List[object]
$CheckErrors = New-Object System.Collections.Generic.List[string]
$Out = [ordered]@{ Ok = $false; Error = $null; Language = $Language; WindowFound = $false; Pages = @(); DesktopTaskChecked = $null; DesktopTaskSource = $null
    LaunchChecked = $null; LaunchSource = $null; FinishClicked = $false; ProgramStarted = $false; ProgramSeconds = $null; ButtonNames = @(); CheckErrors = @() }
try {
    $Texts = [System.IO.File]::ReadAllText($TextsFile, [System.Text.Encoding]::UTF8) | ConvertFrom-Json
    $W = $Texts.wizard.$Language
    Add-Type -AssemblyName UIAutomationClient, UIAutomationTypes, System.Drawing
    $AE = [System.Windows.Automation.AutomationElement]
    $Scope = [System.Windows.Automation.TreeScope]
    $ByButton = New-Object System.Windows.Automation.PropertyCondition($AE::ControlTypeProperty, [System.Windows.Automation.ControlType]::Button)
    $ButtonNames = New-Object System.Collections.Generic.List[string]
    function Test-UiName($Value, $Names) { $N = Format-UiText $Value; foreach ($Candidate in @($Names)) { if ($N -ceq (Format-UiText $Candidate)) { return $true } }; return $false }
    function Get-PageTexts([IntPtr]$Handle) {
        $List = New-Object System.Collections.Generic.List[string]
        foreach ($Child in [DsoInst.Native]::Children($Handle)) {
            if (-not [DsoInst.Native]::IsWindowVisible($Child)) { continue }
            $Text = [DsoInst.Native]::TextOf($Child)
            if (-not [string]::IsNullOrWhiteSpace($Text)) { $List.Add([string]$Text) }
        }
        return , $List.ToArray()
    }
    function Find-Target($Window) {
        $Buttons = @($Window.FindAll($Scope::Descendants, $ByButton))
        foreach ($Button in $Buttons) { try { $Name = [string]$Button.Current.Name; if (-not $ButtonNames.Contains($Name)) { $ButtonNames.Add($Name) } } catch { $null = $_ } }
        foreach ($Kind in @('finish', 'install', 'next')) {
            foreach ($Button in $Buttons) {
                try {
                    $Current = $Button.Current
                    if ($Current.IsEnabled -and -not $Current.IsOffscreen -and (Test-UiName $Current.Name $W.$Kind)) { return [pscustomobject]@{ Kind = $Kind; Element = $Button; Name = [string]$Current.Name } }
                }
                catch { $null = $_ }
            }
        }
        return $null
    }
    function Get-CheckItems([IntPtr]$Handle, $Window) {
        # MSAA najpierw; blad odczytu (np. E_NOTIMPL) nie przerywa sondy, trafia do CheckErrors. UIA (TogglePattern) zawsze, gdy MSAA
        # nie dalo zadnego stanu. LegacyIAccessiblePattern nie istnieje w zarzadzanym UIA .NET Framework (System.Windows.Automation),
        # wiec stan MSAA czytany jest wprost przez oleacc (to ta sama wartosc accState).
        $Items = New-Object System.Collections.Generic.List[object]
        foreach ($Child in [DsoInst.Native]::Children($Handle)) {
            try {
                if (-not [DsoInst.Native]::IsWindowVisible($Child)) { continue }
                if ([DsoInst.Native]::ClassOf($Child) -notlike '*CheckListBox*') { continue }
                $Entries = @([DsoInst.Native]::CheckItems($Child))
                if ([DsoInst.Native]::LastError) { $CheckErrors.Add("msaa: $([DsoInst.Native]::LastError)") }
                foreach ($Entry in $Entries) {
                    $State = [int]$Entry[1]
                    $Checked = $null
                    if ($State -ge 0) { $Checked = (($State -band 0x10) -ne 0) }
                    $Items.Add([pscustomobject]@{ Name = [string]$Entry[0]; Checked = $Checked; Source = 'msaa' })
                }
            }
            catch { $CheckErrors.Add("msaa: $($_.Exception.GetBaseException().Message)") }
        }
        if (@($Items | Where-Object { $null -ne $_.Checked }).Count -eq 0) {
            try {
                foreach ($Element in @($Window.FindAll($Scope::Descendants, [System.Windows.Automation.Condition]::TrueCondition))) {
                    try {
                        $Pattern = $null
                        if ($Element.TryGetCurrentPattern([System.Windows.Automation.TogglePattern]::Pattern, [ref]$Pattern)) {
                            $Items.Add([pscustomobject]@{ Name = [string]$Element.Current.Name; Checked = ($Pattern.Current.ToggleState -eq [System.Windows.Automation.ToggleState]::On); Source = 'uia' })
                        }
                    }
                    catch { $null = $_ }
                }
            }
            catch { $CheckErrors.Add("uia: $($_.Exception.GetBaseException().Message)") }
        }
        return , $Items.ToArray()
    }
    function Save-WindowShot([IntPtr]$Handle, [string]$Path) {
        $Rect = New-Object DsoInst.Native+RECT
        if (-not [DsoInst.Native]::GetWindowRect($Handle, [ref]$Rect)) { throw 'GetWindowRect okna kreatora nie powiodlo sie.' }
        $Bitmap = New-Object System.Drawing.Bitmap(($Rect.Right - $Rect.Left), ($Rect.Bottom - $Rect.Top))
        try {
            $Graphics = [System.Drawing.Graphics]::FromImage($Bitmap)
            try { $Graphics.CopyFromScreen($Rect.Left, $Rect.Top, 0, 0, $Bitmap.Size) } finally { $Graphics.Dispose() }
            $Bitmap.Save($Path, [System.Drawing.Imaging.ImageFormat]::Png)
        }
        finally { $Bitmap.Dispose() }
    }

    $ByClass = New-Object System.Windows.Automation.PropertyCondition($AE::ClassNameProperty, [string]$Texts.wizardClass)
    $Window = $null
    $Limit = (Get-Date).AddSeconds($WindowSeconds)
    while ($null -eq $Window -and (Get-Date) -lt $Limit) {
        $Window = $AE::RootElement.FindFirst($Scope::Children, $ByClass)
        if ($null -eq $Window) { Start-Sleep -Milliseconds 500 }
    }
    if ($null -eq $Window) { throw "Brak okna kreatora (klasa $($Texts.wizardClass)) po $WindowSeconds s." }
    $Out.WindowFound = $true
    $Handle = [IntPtr]$Window.Current.NativeWindowHandle
    $Previous = $null
    for ($Index = 1; $Index -le 10; $Index++) {
        $Target = $null
        $Limit = (Get-Date).AddSeconds($PageSeconds)
        do {
            $Target = Find-Target $Window
            if ($null -ne $Target) {
                $Signature = ((Get-PageTexts $Handle) -join "`n") + '|' + $Target.Name
                if ($Signature -ne $Previous) { break }
                $Target = $null
            }
            Start-Sleep -Milliseconds 500
        } while ((Get-Date) -lt $Limit)
        if ($null -eq $Target) { throw "Kreator: brak nowej strony z przyciskiem dalej/instaluj/zakoncz po $PageSeconds s (strona $Index)." }
        Start-Sleep -Milliseconds 800
        $PageTexts = Get-PageTexts $Handle
        $Items = Get-CheckItems $Handle $Window
        $Shot = "$ShotPrefix-$Index.png"
        Save-WindowShot $Handle $Shot
        $Pages.Add([pscustomobject]@{ Index = $Index; Labels = $PageTexts; Target = $Target.Name; TargetKind = $Target.Kind; Items = $Items; Screenshot = [System.IO.Path]::GetFileName($Shot) })
        foreach ($Item in $Items) {
            # Odczyt ze stanem wygrywa z odczytem bez stanu (np. MSAA bez accState, potem UIA z TogglePattern).
            $ItemName = Format-UiText $Item.Name
            if ($ItemName -ceq (Format-UiText $W.desktopTask) -and ($null -ne $Item.Checked -or $null -eq $Out.DesktopTaskChecked)) { $Out.DesktopTaskChecked = $Item.Checked; $Out.DesktopTaskSource = $Item.Source }
            if ($ItemName -ceq (Format-UiText $W.launchTask) -and ($null -ne $Item.Checked -or $null -eq $Out.LaunchChecked)) { $Out.LaunchChecked = $Item.Checked; $Out.LaunchSource = $Item.Source }
        }
        if ($Target.Kind -eq 'install' -and $null -eq $Out.DesktopTaskChecked) {
            # Strona gotowe: podsumowanie wymienia wybrane zadania; zadanie pulpitu na liscie = pole zaznaczone.
            if ((Format-UiText ($PageTexts -join ' ')).Contains((Format-UiText $W.desktopTask))) { $Out.DesktopTaskChecked = $true; $Out.DesktopTaskSource = 'podsumowanie' }
        }
        $Previous = ($PageTexts -join "`n") + '|' + $Target.Name
        $Target.Element.GetCurrentPattern([System.Windows.Automation.InvokePattern]::Pattern).Invoke()
        if ($Target.Kind -eq 'finish') { $Out.FinishClicked = $true; break }
    }
    if ($Out.FinishClicked) {
        $Watch = [System.Diagnostics.Stopwatch]::StartNew()
        while ($Watch.Elapsed.TotalSeconds -lt $StartSeconds) {
            $Found = @(Get-CimInstance -ClassName Win32_Process -Filter "Name='DSOptimizer.exe'" | Where-Object { $_.ExecutablePath -and ([string]$_.ExecutablePath -ieq [string]$Texts.installedExe) })
            if ($Found.Count -gt 0) { $Out.ProgramStarted = $true; $Out.ProgramSeconds = [int]$Watch.Elapsed.TotalSeconds; break }
            Start-Sleep -Milliseconds 500
        }
    }
    $Out.Ok = $true
}
catch { $Out.Error = [string]$_.Exception.GetBaseException().Message }
$Out.Pages = $Pages.ToArray()
if ($null -ne (Get-Variable -Name ButtonNames -ErrorAction SilentlyContinue)) { $Out.ButtonNames = $ButtonNames.ToArray() }
$Out.CheckErrors = @($CheckErrors | Select-Object -Unique)
Write-ProbeResult $Out $ResultFile
'@
    return ("param([Parameter(Mandatory)][string]`$ResultFile, [Parameter(Mandatory)][string]`$TextsFile, [Parameter(Mandatory)][ValidateSet('pl', 'en')][string]`$Language, " +
        "[Parameter(Mandatory)][string]`$ShotPrefix, [int]`$WindowSeconds = 60, [int]`$PageSeconds = 240, [int]`$StartSeconds = 30)`n`$ErrorActionPreference = 'Stop'`n" +
        "try {`n" + (Get-InstallerProbeHelpersText) + "`n} catch { [System.IO.File]::WriteAllText(`$ResultFile, (@{ Ok = `$false; Error = [string]`$_.Exception.Message } | ConvertTo-Json)); return }`n" + $Body)
}

function Get-InstallerWindowsProbeScript {
    # Sonda okien (faza motw): tytuły okien najwyższego poziomu z UI Automation i z Win32 EnumWindows (widoczne; UIA nie zawsze widzi
    # okno SmartScreen) i zrzut całego ekranu. Tylko odczyt, niczego nie klika.
    [CmdletBinding()]
    [OutputType([string])]
    param()
    $Body = @'
$Out = [ordered]@{ Ok = $false; Error = $null; Titles = @(); Win32Windows = @(); Win32Error = $null; Screenshot = $false; ScreenshotError = $null }
try {
    try {
        $List = New-Object System.Collections.Generic.List[object]
        foreach ($Handle in [DsoInst.Native]::TopWindows()) {
            if (-not [DsoInst.Native]::IsWindowVisible($Handle)) { continue }
            $Title = [DsoInst.Native]::TitleOf($Handle)
            if ([string]::IsNullOrWhiteSpace($Title)) { continue }
            $ProcessId = [DsoInst.Native]::ProcessOf($Handle)
            $ProcessName = $null
            try { $ProcessName = (Get-Process -Id $ProcessId -ErrorAction Stop).ProcessName } catch { $ProcessName = $null }
            $List.Add([pscustomobject]@{ Title = $Title; ClassName = [DsoInst.Native]::ClassOf($Handle); ProcessId = $ProcessId; ProcessName = $ProcessName })
        }
        $Out.Win32Windows = $List.ToArray()
    }
    catch { $Out.Win32Error = [string]$_.Exception.GetBaseException().Message }
    Add-Type -AssemblyName UIAutomationClient, UIAutomationTypes
    $Titles = New-Object System.Collections.Generic.List[string]
    foreach ($Item in @([System.Windows.Automation.AutomationElement]::RootElement.FindAll([System.Windows.Automation.TreeScope]::Children, [System.Windows.Automation.Condition]::TrueCondition))) {
        try { $Name = [string]$Item.Current.Name; if ($Name) { $Titles.Add($Name) } } catch { $null = $_ }
    }
    $Out.Titles = $Titles.ToArray()
    try {
        Add-Type -AssemblyName System.Windows.Forms, System.Drawing
        $Bounds = [System.Windows.Forms.SystemInformation]::VirtualScreen
        $Bitmap = New-Object System.Drawing.Bitmap($Bounds.Width, $Bounds.Height)
        try {
            $Graphics = [System.Drawing.Graphics]::FromImage($Bitmap)
            try { $Graphics.CopyFromScreen($Bounds.Left, $Bounds.Top, 0, 0, $Bitmap.Size) } finally { $Graphics.Dispose() }
            $Bitmap.Save($Shot, [System.Drawing.Imaging.ImageFormat]::Png)
        }
        finally { $Bitmap.Dispose() }
        $Out.Screenshot = $true
    }
    catch { $Out.ScreenshotError = [string]$_.Exception.GetBaseException().Message }
    $Out.Ok = $true
}
catch { $Out.Error = [string]$_.Exception.GetBaseException().Message }
Write-ProbeResult $Out $ResultFile
'@
    return ("param([Parameter(Mandatory)][string]`$ResultFile, [Parameter(Mandatory)][string]`$Shot)`n`$ErrorActionPreference = 'Stop'`n" +
        "try {`n" + (Get-InstallerProbeHelpersText) + "`n} catch { [System.IO.File]::WriteAllText(`$ResultFile, (@{ Ok = `$false; Error = [string]`$_.Exception.Message } | ConvertTo-Json)); return }`n" + $Body)
}

# ---------------------------------------------------------------------------------------------------------------------
# Bloki wykonywane w VM (jako Administrator przez PowerShell Direct).
# ---------------------------------------------------------------------------------------------------------------------

$script:InstPrepareBlock = {
    param([string[]]$Directories)
    $ErrorActionPreference = 'Stop'
    foreach ($Directory in $Directories) { $null = New-Item -ItemType Directory -Path $Directory -Force }
}

$script:InstVerifyBlock = {
    param([string]$Path, [string]$ExpectedSha256)
    $ErrorActionPreference = 'Stop'
    $Sha = (Get-FileHash -LiteralPath $Path -Algorithm SHA256).Hash.ToLowerInvariant()
    if ($Sha -ne $ExpectedSha256) { throw "SHA256 pliku '$Path' w VM ($Sha) różni się od oczekiwanego ($ExpectedSha256)." }
}

$script:InstExpandBlock = {
    # Zip wydania -> kopia przenośna w $AppDirectory (bez spacji, jak reszta testów VM).
    param([string]$ZipPath, [string]$AppDirectory)
    $ErrorActionPreference = 'Stop'
    $Staging = Join-Path (Split-Path -Parent $AppDirectory) 'inst-unzip'
    foreach ($Path in @($Staging, $AppDirectory)) { if (Test-Path -LiteralPath $Path) { Remove-Item -LiteralPath $Path -Recurse -Force } }
    Expand-Archive -LiteralPath $ZipPath -DestinationPath $Staging -Force
    Move-Item -LiteralPath (Join-Path $Staging 'DSOptimizer') -Destination $AppDirectory
    Remove-Item -LiteralPath $Staging -Recurse -Force
    if (-not (Test-Path -LiteralPath (Join-Path $AppDirectory 'DSOptimizer.exe'))) { throw "Po rozpakowaniu brak $AppDirectory\DSOptimizer.exe." }
}

$script:InstRunProcessBlock = {
    # Instalator albo deinstalator po cichu; ścieżka (także ze spacją) tylko w -FilePath, argumenty bez spacji.
    param([string]$FilePath, [string[]]$Arguments)
    $ErrorActionPreference = 'Stop'
    $Process = Start-Process -FilePath $FilePath -ArgumentList $Arguments -Wait -PassThru
    return [int]$Process.ExitCode
}

$script:InstStateBlock = {
    # Stan instalacji: sumy plików folderu programu, klucz Uninstall (widok 64-bit), skróty, drugi deinstalator.
    param([string]$AppDirectory, [string]$UninstallSubKey)
    $ErrorActionPreference = 'Stop'
    $Files = @{}
    $Exists = Test-Path -LiteralPath $AppDirectory -PathType Container
    if ($Exists) {
        $Prefix = $AppDirectory.TrimEnd('\') + '\'
        foreach ($File in Get-ChildItem -LiteralPath $AppDirectory -Recurse -Force -File) { $Files[$File.FullName.Substring($Prefix.Length)] = (Get-FileHash -LiteralPath $File.FullName -Algorithm SHA256).Hash }
    }
    $KeyPresent = $false
    $DisplayVersion = $null
    $Base = [Microsoft.Win32.RegistryKey]::OpenBaseKey([Microsoft.Win32.RegistryHive]::LocalMachine, [Microsoft.Win32.RegistryView]::Registry64)
    try {
        $Key = $Base.OpenSubKey($UninstallSubKey)
        if ($null -ne $Key) { try { $KeyPresent = $true; $DisplayVersion = [string]$Key.GetValue('DisplayVersion') } finally { $Key.Dispose() } }
    }
    finally { $Base.Dispose() }
    $StartMenu = Join-Path ([Environment]::GetFolderPath('CommonPrograms')) 'DSOptimizer.lnk'
    $Desktop = Join-Path ([Environment]::GetFolderPath('CommonDesktopDirectory')) 'DSOptimizer.lnk'
    return [pscustomobject]@{ AppExists = $Exists; Files = $Files; KeyPresent = $KeyPresent; DisplayVersion = $DisplayVersion
        StartMenuLink = (Test-Path -LiteralPath $StartMenu -PathType Leaf); DesktopLink = (Test-Path -LiteralPath $Desktop -PathType Leaf)
        Unins001 = (Test-Path -LiteralPath (Join-Path $AppDirectory 'unins001.dat')) }
}

$script:InstWaitProgramBlock = {
    # Czeka do $Seconds na proces DSOptimizer.exe z pliku $ExePath (opcjonalnie z tekstem w linii poleceń) i na mutex programu.
    param([string]$ExePath, [string]$MutexName, [int]$Seconds, [string]$CommandLineContains)
    $ErrorActionPreference = 'Stop'
    $Watch = [System.Diagnostics.Stopwatch]::StartNew()
    $Found = @()
    $HasMutex = $false
    do {
        $Found = @(Get-CimInstance -ClassName Win32_Process -Filter "Name='DSOptimizer.exe'" | Where-Object {
                $_.ExecutablePath -and ([string]$_.ExecutablePath -ieq $ExePath) -and (-not $CommandLineContains -or ([string]$_.CommandLine).Contains($CommandLineContains)) })
        $Mutex = $null
        try { $HasMutex = [System.Threading.Mutex]::TryOpenExisting($MutexName, [ref]$Mutex) } catch [System.UnauthorizedAccessException] { $HasMutex = $true }
        if ($null -ne $Mutex) { $Mutex.Dispose() }
        if ($Found.Count -gt 0 -and $HasMutex) { break }
        Start-Sleep -Milliseconds 500
    } while ($Watch.Elapsed.TotalSeconds -lt $Seconds)
    return [pscustomobject]@{ ProcessFound = ($Found.Count -gt 0); Pid = $(if ($Found.Count -gt 0) { [int]$Found[0].ProcessId } else { 0 }); Mutex = $HasMutex; Seconds = [int]$Watch.Elapsed.TotalSeconds }
}

$script:InstWaitMutexGoneBlock = {
    param([string]$MutexName, [int]$Seconds)
    $ErrorActionPreference = 'Stop'
    $Watch = [System.Diagnostics.Stopwatch]::StartNew()
    do {
        $Mutex = $null
        $HasMutex = $false
        try { $HasMutex = [System.Threading.Mutex]::TryOpenExisting($MutexName, [ref]$Mutex) } catch [System.UnauthorizedAccessException] { $HasMutex = $true }
        if ($null -ne $Mutex) { $Mutex.Dispose() }
        if (-not $HasMutex) { return [pscustomobject]@{ Released = $true; Seconds = [int]$Watch.Elapsed.TotalSeconds } }
        Start-Sleep -Milliseconds 250
    } while ($Watch.Elapsed.TotalSeconds -lt $Seconds)
    return [pscustomobject]@{ Released = $false; Seconds = [int]$Watch.Elapsed.TotalSeconds }
}

$script:InstProcessAliveBlock = {
    param([int]$ProcessId)
    if ($ProcessId -le 0) { return $false }
    $Process = Get-Process -Id $ProcessId -ErrorAction SilentlyContinue
    return ($null -ne $Process -and -not $Process.HasExited)
}

$script:InstStopProcessBlock = {
    # Zamyka proces programu W VM (Stop-Process) i czeka na jego koniec. WasAlive = proces żył przed zamknięciem (zamknięcie żywego
    # procesu, nie „już go nie było”), Gone = po wszystkim procesu nie ma.
    param([int]$ProcessId, [int]$Seconds)
    $ErrorActionPreference = 'Stop'
    if ($ProcessId -le 0) { return [pscustomobject]@{ WasAlive = $false; Gone = $false } }
    $Process = Get-Process -Id $ProcessId -ErrorAction SilentlyContinue
    if ($null -eq $Process -or $Process.HasExited) { return [pscustomobject]@{ WasAlive = $false; Gone = $true } }
    Stop-Process -Id $ProcessId -Force
    return [pscustomobject]@{ WasAlive = $true; Gone = [bool]$Process.WaitForExit($Seconds * 1000) }
}

$script:InstAliveMutexBlock = {
    # Czy proces o PID żyje i czy mutex programu istnieje (tylko odczyt).
    param([int]$ProcessId, [string]$MutexName)
    $ErrorActionPreference = 'Stop'
    $Alive = $false
    if ($ProcessId -gt 0) { $Process = Get-Process -Id $ProcessId -ErrorAction SilentlyContinue; $Alive = ($null -ne $Process -and -not $Process.HasExited) }
    $Mutex = $null
    $HasMutex = $false
    try { $HasMutex = [System.Threading.Mutex]::TryOpenExisting($MutexName, [ref]$Mutex) } catch [System.UnauthorizedAccessException] { $HasMutex = $true }
    if ($null -ne $Mutex) { $Mutex.Dispose() }
    return [pscustomobject]@{ Alive = $Alive; Mutex = $HasMutex }
}

$script:InstStartTaskBlock = {
    # Zadanie Harmonogramu w sesji użytkownika (Interactive, RunLevel z argumentu); $Execute może mieć zmienne środowiskowe VM (%SystemRoot%).
    param([string]$Execute, [string]$Argument, [string]$WorkingDirectory, [string]$User, [string]$TaskName, [string]$RunLevel)
    $ErrorActionPreference = 'Stop'
    $Since = (Get-Date).ToUniversalTime().ToString('o')
    $ActionArguments = @{ Execute = [Environment]::ExpandEnvironmentVariables($Execute) }
    if ($Argument) { $ActionArguments.Argument = $Argument }
    if ($WorkingDirectory) { $ActionArguments.WorkingDirectory = $WorkingDirectory }
    $Action = New-ScheduledTaskAction @ActionArguments
    $Principal = New-ScheduledTaskPrincipal -UserId $User -LogonType Interactive -RunLevel $RunLevel
    $Settings = New-ScheduledTaskSettingsSet -AllowStartIfOnBatteries -DontStopIfGoingOnBatteries -ExecutionTimeLimit ([TimeSpan]::Zero)
    $null = Register-ScheduledTask -TaskName $TaskName -Action $Action -Principal $Principal -Settings $Settings -Force
    Start-ScheduledTask -TaskName $TaskName
    return $Since
}

$script:InstTaskResultBlock = {
    # Czeka do $Seconds na koniec zadania; LastTaskResult = kod wyjścia procesu (267009/267011 = jeszcze bez kodu).
    param([string]$TaskName, [int]$Seconds)
    $ErrorActionPreference = 'Stop'
    $Watch = [System.Diagnostics.Stopwatch]::StartNew()
    $State = $null
    $Last = $null
    do {
        $State = [string](Get-ScheduledTask -TaskName $TaskName).State
        $Last = [int64](Get-ScheduledTaskInfo -TaskName $TaskName).LastTaskResult
        if ($State -ne 'Running' -and $Last -notin @(267009, 267011)) { return [pscustomobject]@{ Finished = $true; State = $State; LastTaskResult = $Last } }
        Start-Sleep -Milliseconds 500
    } while ($Watch.Elapsed.TotalSeconds -lt $Seconds)
    return [pscustomobject]@{ Finished = $false; State = $State; LastTaskResult = $Last }
}

$script:InstStopTaskBlock = {
    # Zatrzymuje działające wystąpienie zadania (proces startowany przez zadanie; tylko w VM).
    param([string]$TaskName)
    Stop-ScheduledTask -TaskName $TaskName -ErrorAction SilentlyContinue
}

$script:InstProgramDataBlock = {
    # Pliki (ścieżki względne) i właściciel (SID) %ProgramData%\DSOptimizer; tylko odczyt.
    param([string]$Directory)
    $ErrorActionPreference = 'Stop'
    if (-not (Test-Path -LiteralPath $Directory -PathType Container)) { return [pscustomobject]@{ Exists = $false; Files = @(); Owner = $null } }
    $Prefix = $Directory.TrimEnd('\') + '\'
    $Files = @(Get-ChildItem -LiteralPath $Directory -Recurse -Force -File | ForEach-Object { $_.FullName.Substring($Prefix.Length) } | Sort-Object)
    $Owner = (Get-Item -LiteralPath $Directory -Force).GetAccessControl('Owner').GetOwner([System.Security.Principal.SecurityIdentifier]).Value
    return [pscustomobject]@{ Exists = $true; Files = $Files; Owner = [string]$Owner }
}

$script:InstRemoveDesktopLinkBlock = {
    $ErrorActionPreference = 'Stop'
    $Desktop = Join-Path ([Environment]::GetFolderPath('CommonDesktopDirectory')) 'DSOptimizer.lnk'
    $Existed = Test-Path -LiteralPath $Desktop -PathType Leaf
    if ($Existed) { Remove-Item -LiteralPath $Desktop -Force }
    return [pscustomobject]@{ Existed = $Existed; Gone = (-not (Test-Path -LiteralPath $Desktop)) }
}

$script:InstWaitGoneBlock = {
    param([string]$Path, [int]$Seconds)
    $Watch = [System.Diagnostics.Stopwatch]::StartNew()
    while ((Test-Path -LiteralPath $Path) -and $Watch.Elapsed.TotalSeconds -lt $Seconds) { Start-Sleep -Milliseconds 500 }
    return (-not (Test-Path -LiteralPath $Path))
}

$script:InstMotwBlock = {
    # Kopia instalatora na pulpit dso ze strumieniem Zone.Identifier (jak z przeglądarki) i odczyt zwrotny ZoneId.
    param([string]$Source, [string]$DesktopDirectory, [int]$ZoneId)
    $ErrorActionPreference = 'Stop'
    if (-not (Test-Path -LiteralPath $DesktopDirectory -PathType Container)) { throw "Brak pulpitu użytkownika '$DesktopDirectory'." }
    $Target = Join-Path $DesktopDirectory (Split-Path -Leaf $Source)
    Copy-Item -LiteralPath $Source -Destination $Target -Force
    Set-Content -LiteralPath $Target -Stream 'Zone.Identifier' -Value "[ZoneTransfer]`r`nZoneId=$ZoneId" -Encoding Ascii
    $Read = @(Get-Content -LiteralPath $Target -Stream 'Zone.Identifier')
    $Zone = $null
    if (($Read -join "`n") -match 'ZoneId=(\d+)') { $Zone = [int]$Matches[1] }
    return [pscustomobject]@{ Path = $Target; ZoneId = $Zone }
}

$script:InstSmokeBlock = {
    # Test-InstallerSmoke.ps1 w tej sesji (Administrator, 64-bit, czysta VM); zwraca kod wyjścia, smoke-result.json i koniec wyjścia.
    param([string]$ScriptPath, [string]$InstallerPath, [string]$ZipPath, [string]$Version, [string]$LogDirectory)
    $ErrorActionPreference = 'Stop'
    Set-ExecutionPolicy -Scope Process -ExecutionPolicy Bypass -Force
    $null = New-Item -ItemType Directory -Path $LogDirectory -Force
    $Output = (& $ScriptPath -InstallerPath $InstallerPath -ZipPath $ZipPath -Version $Version -LogDirectory $LogDirectory *>&1 | Out-String)
    $Code = $LASTEXITCODE
    $ResultPath = Join-Path $LogDirectory 'smoke-result.json'
    $Json = $null
    if (Test-Path -LiteralPath $ResultPath) { $Json = Get-Content -LiteralPath $ResultPath -Raw -Encoding UTF8 }
    if ($Output.Length -gt 6000) { $Output = $Output.Substring($Output.Length - 6000) }
    return [pscustomobject]@{ ExitCode = $Code; Json = $Json; Output = $Output }
}

# ---------------------------------------------------------------------------------------------------------------------
# Kroki faz (strona hosta).
# ---------------------------------------------------------------------------------------------------------------------

function Get-InstallerLayout {
    # Układ dla Start-DsoGuiInUserSession / Invoke-DsoUserSessionScript (Hashtable): program zainstalowany albo przenośny.
    [CmdletBinding()]
    [OutputType([hashtable])]
    param([Parameter(Mandatory)][ValidateSet('Installed', 'Portable')][string]$Kind)
    $Plan = Get-InstallerPlan
    if ($Kind -eq 'Installed') { return @{ Root = 'C:\dso'; App = $Plan.InstallDirectory; Exe = $Plan.InstalledExe; Out = $Plan.OutDirectory } }
    return @{ Root = 'C:\dso'; App = $Plan.PortableDirectory; Exe = $Plan.PortableExe; Out = $Plan.OutDirectory }
}

function Invoke-InstallerRemote {
    # Blok w VM; zwraca ostatni obiekt wyjścia.
    [CmdletBinding()]
    param([Parameter(Mandatory)][System.Collections.IDictionary]$Context, [Parameter(Mandatory)][scriptblock]$ScriptBlock, [object[]]$ArgumentList = @())
    return (Invoke-Command -Session $Context.Session -ScriptBlock $ScriptBlock -ArgumentList $ArgumentList | Select-Object -Last 1)
}

function Add-InstallerTimeline {
    [CmdletBinding()]
    param([Parameter(Mandatory)][System.Collections.IDictionary]$Context, [Parameter(Mandatory)][string]$Text)
    $Context.Timeline.Add("$(Get-Date -Format 'HH:mm:ss') $($Context.Run.Key) $Text")
}

function Initialize-InstallerVm {
    # Katalogi w VM, kopia plików wydania do C:\dso\instalator i sprawdzenie sum w VM; z -Portable też kopia przenośna w C:\dso\app.
    # Zwraca nazwę -> ścieżka w VM.
    [CmdletBinding()]
    param([Parameter(Mandatory)][System.Collections.IDictionary]$Context, [Parameter(Mandatory)][object[]]$Files, [switch]$Portable)
    $Plan = $Context.Plan
    $null = Invoke-InstallerRemote -Context $Context -ScriptBlock $script:InstPrepareBlock -ArgumentList @(, [string[]]@($Plan.WorkDirectory, $Plan.OutDirectory, $Plan.LogDirectory))
    $Remote = @{}
    foreach ($File in $Files) {
        $Target = Join-Path $Plan.WorkDirectory $File.Name
        Copy-Item -LiteralPath $File.Path -Destination $Target -ToSession $Context.Session -Force
        $null = Invoke-InstallerRemote -Context $Context -ScriptBlock $script:InstVerifyBlock -ArgumentList @($Target, $File.Sha256)
        $Remote[$File.Name] = $Target
    }
    if ($Portable) {
        $null = Invoke-InstallerRemote -Context $Context -ScriptBlock $script:InstExpandBlock -ArgumentList @($Remote[$Context.Artifact.Zip.Name], $Plan.PortableDirectory)
    }
    Add-InstallerTimeline -Context $Context -Text 'pliki w VM'
    return $Remote
}

function Invoke-InstallerSetup {
    # Instalator albo deinstalator po cichu (Administrator PowerShell Direct) z logiem Inno w C:\dso\out\logs\<krok>.log; zwraca kod.
    [CmdletBinding()]
    [OutputType([int])]
    param(
        [Parameter(Mandatory)][System.Collections.IDictionary]$Context,
        [Parameter(Mandatory)][string]$FilePath,
        [Parameter(Mandatory)][ValidatePattern('^[A-Za-z0-9-]+\z')][string]$Step,
        [switch]$Uninstall
    )
    $Plan = $Context.Plan
    $Arguments = if ($Uninstall) { @($Plan.UninstallArguments) } else { @($Plan.SilentArguments) }
    $Arguments += "/LOG=$($Plan.LogDirectory)\$Step.log"
    $Code = [int](Invoke-InstallerRemote -Context $Context -ScriptBlock $script:InstRunProcessBlock -ArgumentList @($FilePath, [string[]]$Arguments))
    Add-InstallerTimeline -Context $Context -Text "$Step kod $Code"
    return $Code
}

function Get-InstallerState {
    [CmdletBinding()]
    param([Parameter(Mandatory)][System.Collections.IDictionary]$Context)
    $State = Invoke-InstallerRemote -Context $Context -ScriptBlock $script:InstStateBlock -ArgumentList @($Context.Plan.InstallDirectory, $Context.Plan.UninstallSubKey)
    return [pscustomobject]@{ AppExists = [bool]$State.AppExists; Files = (ConvertTo-InstallerHashtable $State.Files); KeyPresent = [bool]$State.KeyPresent
        DisplayVersion = $State.DisplayVersion; StartMenuLink = [bool]$State.StartMenuLink; DesktopLink = [bool]$State.DesktopLink; Unins001 = [bool]$State.Unins001 }
}

function Start-InstallerWindowA {
    # Okno A z Program Files (Start-DsoGuiInUserSession z własnym układem) i do 60 s na proces oraz mutex.
    [CmdletBinding()]
    param([Parameter(Mandatory)][System.Collections.IDictionary]$Context)
    $Plan = $Context.Plan
    $null = Start-DsoGuiInUserSession -Session $Context.Session -Layout (Get-InstallerLayout -Kind 'Installed') -UserName $Plan.UserName -TaskName $Plan.TaskNames.A
    $Wait = Invoke-InstallerRemote -Context $Context -ScriptBlock $script:InstWaitProgramBlock -ArgumentList @($Plan.InstalledExe, $Plan.MutexName, [int]$Plan.AppStartSeconds, '')
    Add-InstallerTimeline -Context $Context -Text "okno A: proces $($Wait.ProcessFound), mutex $($Wait.Mutex)"
    return [ordered]@{ processFound = [bool]$Wait.ProcessFound; mutex = [bool]$Wait.Mutex; pid = [int]$Wait.Pid; seconds = [int]$Wait.Seconds }
}

function Invoke-InstallerRefusalProbe {
    # Sonda okna „DSOptimizer już działa” w sesji dso; z -Click klika OK. Błąd sondy = wyjątek.
    [CmdletBinding()]
    param([Parameter(Mandatory)][System.Collections.IDictionary]$Context, [Parameter(Mandatory)][string]$Name, [Parameter(Mandatory)][int]$WaitSeconds, [switch]$Click)
    $Plan = $Context.Plan
    $TextsPath = "$($Plan.OutDirectory)\inst-texts.json"
    $Arguments = "-TextsFile $TextsPath -WaitSeconds $WaitSeconds"
    if ($Click) { $Arguments += ' -Click' }
    $Json = Invoke-DsoUserSessionScript -Session $Context.Session -Layout (Get-InstallerLayout -Kind 'Portable') -Name $Name -ScriptText (Get-InstallerRefusalProbeScript) `
        -ArgumentText $Arguments -UserName $Plan.UserName -TimeoutSeconds ($WaitSeconds + 60) -DataFiles @{ 'inst-texts.json' = (Get-InstallerProbeTexts) }
    if ([string]::IsNullOrWhiteSpace($Json)) { throw "Sonda $Name nie zwróciła wyniku." }
    $Result = $Json | ConvertFrom-Json
    if ($Result.Error) { throw "Sonda ${Name}: $($Result.Error)" }
    return $Result
}

function Copy-InstallerScreenshot {
    # Zrzut z C:\dso\out do <OutDirectory>\<VmName> (Copy-Item -FromSession); zwraca nazwę pliku albo $null (problem w rekordzie).
    [CmdletBinding()]
    param([Parameter(Mandatory)][System.Collections.IDictionary]$Context, [Parameter(Mandatory)][System.Collections.IDictionary]$Record, [Parameter(Mandatory)][string]$Name)
    if ($Name -notmatch '^[A-Za-z0-9-]+\.png\z') { $Record.problems += "niedozwolona nazwa zrzutu '$Name'"; return $null }
    try {
        $null = New-Item -ItemType Directory -Path $Context.VmDirectory -Force
        Copy-Item -FromSession $Context.Session -Path "$($Context.Plan.OutDirectory)\$Name" -Destination (Join-Path $Context.VmDirectory $Name) -Force -ErrorAction Stop
        return $Name
    }
    catch { $Record.problems += "kopia zrzutu ${Name}: $($_.Exception.Message)"; return $null }
}

function Invoke-InstallerSmokePhase {
    [CmdletBinding()]
    param([Parameter(Mandatory)][System.Collections.IDictionary]$Context, [Parameter(Mandatory)][System.Collections.IDictionary]$Record)
    $Plan = $Context.Plan
    $Artifact = $Context.Artifact
    $Remote = Initialize-InstallerVm -Context $Context -Files @($Artifact.Zip, $Artifact.Setup)
    # explore -> vm -> tests -> korzeń repo
    $RepoRoot = Split-Path -Parent (Split-Path -Parent (Split-Path -Parent $script:InstallerScriptRoot))
    $ScriptSource = Join-Path $RepoRoot "tools\release\$($Plan.SmokeScript)"
    $ScriptTarget = Join-Path $Plan.WorkDirectory $Plan.SmokeScript
    Copy-Item -LiteralPath $ScriptSource -Destination $ScriptTarget -ToSession $Context.Session -Force
    $Smoke = Invoke-InstallerRemote -Context $Context -ScriptBlock $script:InstSmokeBlock -ArgumentList @($ScriptTarget, $Remote[$Artifact.Setup.Name], $Remote[$Artifact.Zip.Name], $Artifact.Version, $Plan.SmokeLogDirectory)
    $Result = $null
    if ($Smoke.Json) { try { $Result = ([string]$Smoke.Json).TrimStart([char]0xFEFF) | ConvertFrom-Json } catch { $Record.problems += "smoke-result.json nieczytelny: $($_.Exception.Message)" } }
    $Record.data = [ordered]@{ exitCode = $Smoke.ExitCode; result = $Result; output = [string]$Smoke.Output }
    Add-InstallerTimeline -Context $Context -Text "smoke kod $($Smoke.ExitCode)"
}

function Invoke-InstallerRunningPhase {
    [CmdletBinding()]
    param([Parameter(Mandatory)][System.Collections.IDictionary]$Context, [Parameter(Mandatory)][System.Collections.IDictionary]$Record)
    $Plan = $Context.Plan
    $Artifact = $Context.Artifact
    $Data = [ordered]@{}
    $Record.data = $Data
    $Remote = Initialize-InstallerVm -Context $Context -Files @($Artifact.Zip, $Artifact.Setup) -Portable
    $Setup = $Remote[$Artifact.Setup.Name]
    $Uninstaller = Join-Path $Plan.InstallDirectory 'unins000.exe'

    # (1) cicha instalacja
    $Data.install = [ordered]@{ exitCode = (Invoke-InstallerSetup -Context $Context -FilePath $Setup -Step 'install') }
    # (2) okno A z Program Files; wybór języka (czysty checkpoint nie ma settings.json), żeby komunikaty miały stały język
    $Data.windowA = Start-InstallerWindowA -Context $Context
    $PidA = [int]$Data.windowA.pid
    try { $Data.languageChoice = Confirm-DsoLanguageChoice -Session $Context.Session -Layout (Get-InstallerLayout -Kind 'Installed') -Language 'pl' -UserName $Plan.UserName }
    catch { $Record.problems += "wybór języka w oknie A: $($_.Exception.Message)" }
    # (3) kopia przenośna B: okno odmowy, OK, kod 1, A żyje
    $null = Start-DsoGuiInUserSession -Session $Context.Session -Layout (Get-InstallerLayout -Kind 'Portable') -UserName $Plan.UserName -TaskName $Plan.TaskNames.B
    # Błąd sondy nie przerywa fazy: trafia do portable.probeError (niezaliczone PortableMessage), dalsze kroki idą dalej.
    $Refusal = $null
    $ProbeErrorB = $null
    try { $Refusal = Invoke-InstallerRefusalProbe -Context $Context -Name 'Inst-Refusal-B' -WaitSeconds ([int]$Plan.RefusalProbeSeconds) -Click }
    catch { $ProbeErrorB = $_.Exception.Message }
    $TaskB = Invoke-InstallerRemote -Context $Context -ScriptBlock $script:InstTaskResultBlock -ArgumentList @($Plan.TaskNames.B, [int]$Plan.TaskFinishSeconds)
    # B wiszące na niekliknietym oknie odmowy zamykamy, żeby jego okno nie trafiło do sond relaunchu.
    if (-not $TaskB.Finished) { $null = Invoke-InstallerRemote -Context $Context -ScriptBlock $script:InstStopTaskBlock -ArgumentList @($Plan.TaskNames.B) }
    $Data.portable = [ordered]@{ message = (Get-InstallerValue $Refusal 'Text'); clickedOk = (Test-InstallerTrue (Get-InstallerValue $Refusal 'Clicked')); windowProcessId = (Get-InstallerValue $Refusal 'ProcessId')
        probeError = $ProbeErrorB; taskResult = $(if ($TaskB.Finished) { [int64]$TaskB.LastTaskResult } else { $null }); taskState = [string]$TaskB.State
        aAlive = [bool](Invoke-InstallerRemote -Context $Context -ScriptBlock $script:InstProcessAliveBlock -ArgumentList @($PidA)) }
    Add-InstallerTimeline -Context $Context -Text "kopia B: okno $(Get-InstallerValue $Refusal 'Found'), kod $($Data.portable.taskResult)"
    # (4) CLI zmieniające system z kopii przenośnej jako dso
    $ApplyOut = "$($Plan.OutDirectory)\cli-apply.json"
    $Cli = Invoke-DsoCli -Session $Context.Session -ExePath $Plan.PortableExe -Arguments (@($Plan.CliApplyArguments) + @('--out', $ApplyOut)) -OutPath $ApplyOut `
        -RunAsUser $Plan.UserName -TimeoutSeconds ([int]$Plan.CliTimeoutSeconds)
    $FirstError = $null
    if ($null -ne $Cli.Report -and $null -ne $Cli.Report.PSObject.Properties['errors']) { $FirstError = @($Cli.Report.errors) | Select-Object -First 1 }
    $Data.cliApply = [ordered]@{ exitCode = $Cli.ExitCode; timedOut = [bool]$Cli.TimedOut; firstError = $FirstError; reportError = $Cli.ReportError }
    # (5) --list-apps jako dso: wpis DSOptimizer w grupie Protected
    $ListOut = "$($Plan.OutDirectory)\list-apps.json"
    $List = Invoke-DsoCli -Session $Context.Session -ExePath $Plan.PortableExe -Arguments @('--list-apps', '--out', $ListOut) -OutPath $ListOut -RunAsUser $Plan.UserName -TimeoutSeconds ([int]$Plan.CliTimeoutSeconds)
    $Row = $null
    if ($null -ne $List.Report -and $null -ne $List.Report.PSObject.Properties['apps']) { $Row = @($List.Report.apps | Where-Object { $null -ne $_ -and [string]$_.name -ceq $Plan.AppListName }) | Select-Object -First 1 }
    $Data.listApps = [ordered]@{ exitCode = $List.ExitCode; group = $(if ($null -ne $Row) { [string]$Row.group } else { $null }); row = $Row }
    # (6) instalator ponownie przy działającym oknie
    $Before = Get-InstallerState -Context $Context
    $ReCode = Invoke-InstallerSetup -Context $Context -FilePath $Setup -Step 'reinstall-running'
    $After = Get-InstallerState -Context $Context
    $Data.reinstall = [ordered]@{ exitCode = $ReCode; versionBefore = $Before.DisplayVersion; versionAfter = $After.DisplayVersion; filesDiff = (Compare-InstallerFiles -Expected $Before.Files -Actual $After.Files) }
    # (7) deinstalator przy działającym oknie
    $UnCode = Invoke-InstallerSetup -Context $Context -FilePath $Uninstaller -Step 'uninstall-running' -Uninstall
    Start-Sleep -Seconds ([int]$Plan.UninstallSettleSeconds)
    $AfterUninstall = Get-InstallerState -Context $Context
    $Data.uninstallRunning = [ordered]@{ exitCode = $UnCode; filesDiff = (Compare-InstallerFiles -Expected $Before.Files -Actual $AfterUninstall.Files); keyPresent = $AfterUninstall.KeyPresent }
    # (8) relaunch: C z --relaunched; najpierw proces C, potem A trzyma mutex jeszcze co najmniej RelaunchHoldSeconds od wykrycia C
    # (wyraźnie dłużej niż zwykłe czekanie 2 s), a sonda okna odmowy patrzy w tym czasie: C czekający tylko 2 s pokazałby „już działa”
    # przy żywym A. Dopiero potem zamknięcie A, po RelaunchCheckAfterSeconds: C żyje, mutex jest, druga krótka sonda bez okna odmowy.
    # A musi żyć (i trzymać mutex) przed startem C i tuż przed zamknięciem: inaczej wolny mutex wpuściłby C od razu i 30 s czekania
    # po --relaunched w ogóle by się nie sprawdziło.
    $Relaunch = [ordered]@{ aAliveBeforeC = $null; mutexBeforeC = $null; cDetected = $false; cDetectSeconds = $null; holdSeconds = $null; aAliveBeforeStop = $null
        mutexBeforeStop = $null; aStopped = $false; cAlive = $false; cPid = 0; mutex = $false; refusalSeen = $null; refusalText = $null; refusalProcessId = $null; probeError = $null }
    $Data.relaunch = $Relaunch
    $ProbeErrors = @()
    $Seen = $false
    $BeforeC = Invoke-InstallerRemote -Context $Context -ScriptBlock $script:InstAliveMutexBlock -ArgumentList @($PidA, $Plan.MutexName)
    $Relaunch.aAliveBeforeC = (Test-InstallerTrue (Get-InstallerValue $BeforeC 'Alive'))
    $Relaunch.mutexBeforeC = (Test-InstallerTrue (Get-InstallerValue $BeforeC 'Mutex'))
    $null = Invoke-InstallerRemote -Context $Context -ScriptBlock $script:InstStartTaskBlock -ArgumentList @($Plan.InstalledExe, '--relaunched', $Plan.InstallDirectory, $Plan.UserName, $Plan.TaskNames.C, 'Highest')
    $Detect = Invoke-InstallerRemote -Context $Context -ScriptBlock $script:InstWaitProgramBlock -ArgumentList @($Plan.InstalledExe, $Plan.MutexName, [int]$Plan.RelaunchDetectSeconds, '--relaunched')
    $Hold = [System.Diagnostics.Stopwatch]::StartNew()
    $Relaunch.cDetected = [bool]$Detect.ProcessFound
    $Relaunch.cDetectSeconds = [int]$Detect.Seconds
    if ($Relaunch.cDetected) {
        try {
            $HoldProbe = Invoke-InstallerRefusalProbe -Context $Context -Name 'Inst-Refusal-Hold' -WaitSeconds ([int]$Plan.RelaunchProbeSeconds)
            if ($HoldProbe.Found) { $Seen = $true; $Relaunch.refusalText = $HoldProbe.Text; $Relaunch.refusalProcessId = $HoldProbe.ProcessId }
        }
        catch { $ProbeErrors += "sonda przy żywym A: $($_.Exception.Message)" }
        $Remaining = [int][Math]::Ceiling([double]$Plan.RelaunchHoldSeconds - $Hold.Elapsed.TotalSeconds)
        if ($Remaining -gt 0) { Start-Sleep -Seconds $Remaining }
    }
    $Relaunch.holdSeconds = [int][Math]::Floor($Hold.Elapsed.TotalSeconds)
    $BeforeStop = Invoke-InstallerRemote -Context $Context -ScriptBlock $script:InstAliveMutexBlock -ArgumentList @($PidA, $Plan.MutexName)
    $Relaunch.aAliveBeforeStop = (Test-InstallerTrue (Get-InstallerValue $BeforeStop 'Alive'))
    $Relaunch.mutexBeforeStop = (Test-InstallerTrue (Get-InstallerValue $BeforeStop 'Mutex'))
    # A zamykane także bez C: kolejne kroki (deinstalacja) wymagają programu bez okien. aStopped = zamknięty żywy proces.
    $StopA = Invoke-InstallerRemote -Context $Context -ScriptBlock $script:InstStopProcessBlock -ArgumentList @($PidA, [int]$Plan.ProcessStopSeconds)
    $Relaunch.aStopped = (Test-InstallerTrue (Get-InstallerValue $StopA 'WasAlive')) -and (Test-InstallerTrue (Get-InstallerValue $StopA 'Gone'))
    Start-Sleep -Seconds ([int]$Plan.RelaunchCheckAfterSeconds)
    $C = Invoke-InstallerRemote -Context $Context -ScriptBlock $script:InstWaitProgramBlock -ArgumentList @($Plan.InstalledExe, $Plan.MutexName, 1, '--relaunched')
    $Relaunch.cAlive = [bool]$C.ProcessFound
    $Relaunch.cPid = [int]$C.Pid
    $Relaunch.mutex = [bool]$C.Mutex
    try {
        $AfterProbe = Invoke-InstallerRefusalProbe -Context $Context -Name 'Inst-Refusal-C' -WaitSeconds ([int]$Plan.RelaunchAfterProbeSeconds)
        if ($AfterProbe.Found) { $Seen = $true; $Relaunch.refusalText = $AfterProbe.Text; $Relaunch.refusalProcessId = $AfterProbe.ProcessId }
    }
    catch { $ProbeErrors += "sonda po zamknięciu A: $($_.Exception.Message)" }
    # Błąd sondy bez znalezionego okna = brak odczytu ($null), nie „brak okna”.
    $Relaunch.refusalSeen = $(if ($Seen) { $true } elseif ($ProbeErrors.Count -gt 0) { $null } else { $false })
    if ($ProbeErrors.Count -gt 0) { $Relaunch.probeError = $ProbeErrors -join ' | ' }
    Add-InstallerTimeline -Context $Context -Text "relaunch: C wykryty $($Relaunch.cDetected), A zamknięte po $($Relaunch.holdSeconds) s: $($Relaunch.aStopped), C żyje $($Relaunch.cAlive), mutex $($Relaunch.mutex), odmowa $($Relaunch.refusalSeen)"
    # (9) %ProgramData%\DSOptimizer przy działającym C
    $Data.programData = Invoke-InstallerRemote -Context $Context -ScriptBlock $script:InstProgramDataBlock -ArgumentList @($Plan.ProgramDataDirectory)
    # (10) zamknięcie C, mutex znika, cicha deinstalacja, folder/klucz/skróty znikają, ProgramData zostaje
    $StoppedC = Test-InstallerTrue (Get-InstallerValue (Invoke-InstallerRemote -Context $Context -ScriptBlock $script:InstStopProcessBlock -ArgumentList @([int]$Relaunch.cPid, [int]$Plan.ProcessStopSeconds)) 'Gone')
    $Released = Invoke-InstallerRemote -Context $Context -ScriptBlock $script:InstWaitMutexGoneBlock -ArgumentList @($Plan.MutexName, [int]$Plan.MutexReleaseSeconds)
    $DataBefore = Invoke-InstallerRemote -Context $Context -ScriptBlock $script:InstProgramDataBlock -ArgumentList @($Plan.ProgramDataDirectory)
    $FinalCode = Invoke-InstallerSetup -Context $Context -FilePath $Uninstaller -Step 'uninstall' -Uninstall
    $Gone = [bool](Invoke-InstallerRemote -Context $Context -ScriptBlock $script:InstWaitGoneBlock -ArgumentList @($Plan.InstallDirectory, [int]$Plan.UninstallWaitSeconds))
    $Final = Get-InstallerState -Context $Context
    $DataAfter = Invoke-InstallerRemote -Context $Context -ScriptBlock $script:InstProgramDataBlock -ArgumentList @($Plan.ProgramDataDirectory)
    $Data.final = [ordered]@{ cStopped = $StoppedC; mutexReleased = [bool]$Released.Released; exitCode = $FinalCode; appDirGone = $Gone; keyGone = (-not $Final.KeyPresent)
        startMenuGone = (-not $Final.StartMenuLink); desktopGone = (-not $Final.DesktopLink)
        dataBefore = $(if ($DataBefore.Exists) { @($DataBefore.Files) } else { $null }); dataAfter = $(if ($DataAfter.Exists) { @($DataAfter.Files) } else { $null })
        ownerBefore = $DataBefore.Owner; ownerAfter = $DataAfter.Owner }
}

function Invoke-InstallerUpgradePhase {
    [CmdletBinding()]
    param([Parameter(Mandatory)][System.Collections.IDictionary]$Context, [Parameter(Mandatory)][System.Collections.IDictionary]$Record)
    $Plan = $Context.Plan
    $Artifact = $Context.Artifact
    if ($null -eq $Artifact.Upgrade) { throw 'Faza upgrade bez instalatora wyższej wersji (-UpgradeInstallerPath).' }
    $Data = [ordered]@{ releaseVersion = $Artifact.Version; upgradeVersion = $Artifact.Upgrade.Version }
    $Record.data = $Data
    $Remote = Initialize-InstallerVm -Context $Context -Files @($Artifact.Setup, $Artifact.Upgrade.Setup)
    $Setup = $Remote[$Artifact.Setup.Name]
    $UpgradeSetup = $Remote[$Artifact.Upgrade.Setup.Name]
    $Expected = Remove-InstallerUninstallerFiles -Files (Get-InstallerZipHashes -Path $Artifact.Upgrade.Zip.Path)

    $Data.install = [ordered]@{ exitCode = (Invoke-InstallerSetup -Context $Context -FilePath $Setup -Step 'install') }
    $Removed = Invoke-InstallerRemote -Context $Context -ScriptBlock $script:InstRemoveDesktopLinkBlock
    $Data.desktopRemoved = [ordered]@{ existed = [bool]$Removed.Existed; gone = [bool]$Removed.Gone }
    $Data.windowA = Start-InstallerWindowA -Context $Context
    $Before = Get-InstallerState -Context $Context
    $RunCode = Invoke-InstallerSetup -Context $Context -FilePath $UpgradeSetup -Step 'upgrade-running'
    $After = Get-InstallerState -Context $Context
    $Data.upgradeRunning = [ordered]@{ exitCode = $RunCode; versionBefore = $Before.DisplayVersion; versionAfter = $After.DisplayVersion
        filesDiff = (Compare-InstallerFiles -Expected $Before.Files -Actual $After.Files) }
    $null = Invoke-InstallerRemote -Context $Context -ScriptBlock $script:InstStopProcessBlock -ArgumentList @([int]$Data.windowA.pid, [int]$Plan.ProcessStopSeconds)
    $Released = Invoke-InstallerRemote -Context $Context -ScriptBlock $script:InstWaitMutexGoneBlock -ArgumentList @($Plan.MutexName, [int]$Plan.MutexReleaseSeconds)
    $Data.mutexReleased = [bool]$Released.Released
    $UpCode = Invoke-InstallerSetup -Context $Context -FilePath $UpgradeSetup -Step 'upgrade'
    $State = Get-InstallerState -Context $Context
    $Data.upgrade = [ordered]@{ exitCode = $UpCode; versionAfter = $State.DisplayVersion; desktopLink = $State.DesktopLink; startMenuLink = $State.StartMenuLink
        filesDiff = (Compare-InstallerFiles -Expected $Expected -Actual (Remove-InstallerUninstallerFiles -Files $State.Files)); unins001 = $State.Unins001 }
}

function Invoke-InstallerWizardPhase {
    [CmdletBinding()]
    param([Parameter(Mandatory)][System.Collections.IDictionary]$Context, [Parameter(Mandatory)][System.Collections.IDictionary]$Record)
    $Plan = $Context.Plan
    $Artifact = $Context.Artifact
    $Language = [string]$Context.Run.Language
    $Remote = Initialize-InstallerVm -Context $Context -Files @($Artifact.Setup)
    # Log Inno kreatora w C:\dso\out\logs (bez spacji); trafia do raw-wizard-<język> razem z resztą logów fazy.
    $WizardLog = "$($Plan.LogDirectory)\wizard-$Language.log"
    $null = Invoke-InstallerRemote -Context $Context -ScriptBlock $script:InstStartTaskBlock -ArgumentList @($Remote[$Artifact.Setup.Name], "/LANG=$Language /LOG=$WizardLog", $Plan.WorkDirectory, $Plan.UserName, $Plan.TaskNames.Wizard, 'Highest')
    Add-InstallerTimeline -Context $Context -Text "kreator /LANG=$Language uruchomiony (log $WizardLog)"
    $Arguments = "-TextsFile $($Plan.OutDirectory)\inst-texts.json -Language $Language -ShotPrefix $($Plan.OutDirectory)\wizard-$Language -StartSeconds $([int]$Plan.WizardProgramSeconds)"
    $Json = Invoke-DsoUserSessionScript -Session $Context.Session -Layout (Get-InstallerLayout -Kind 'Portable') -Name "Inst-Wizard-$Language" -ScriptText (Get-InstallerWizardProbeScript) `
        -ArgumentText $Arguments -UserName $Plan.UserName -TimeoutSeconds ([int]$Plan.WizardProbeSeconds) -DataFiles @{ 'inst-texts.json' = (Get-InstallerProbeTexts) }
    if ([string]::IsNullOrWhiteSpace($Json)) { throw 'Sonda kreatora nie zwróciła wyniku.' }
    $Probe = $Json | ConvertFrom-Json
    # Pola przez Get-InstallerValue/Get-InstallerList: wynik błędu sondy ma tylko Ok i Error, a StrictMode rzuca przy brakującej właściwości.
    $ProbeError = Get-InstallerValue $Probe 'Error'
    if ($ProbeError) { $Record.problems += "sonda kreatora: $ProbeError" }
    $Pages = @(Get-InstallerList $Probe 'Pages' | Where-Object { $null -ne $_ } | ForEach-Object {
            [pscustomobject]@{ index = (Get-InstallerValue $_ 'Index'); kind = (Get-InstallerWizardPageKind -Labels @(Get-InstallerList $_ 'Labels') -Language $Language)
                labels = @(Get-InstallerList $_ 'Labels'); target = (Get-InstallerValue $_ 'Target'); targetKind = (Get-InstallerValue $_ 'TargetKind')
                items = @(Get-InstallerList $_ 'Items'); screenshot = [string](Get-InstallerValue $_ 'Screenshot') }
        })
    $Copied = @()
    foreach ($Page in $Pages) { $Name = Copy-InstallerScreenshot -Context $Context -Record $Record -Name $Page.screenshot; if ($Name) { $Copied += $Name } }
    $Record.screenshots = $Copied
    $Record.data = [ordered]@{ language = $Language; pages = $Pages; desktopTaskChecked = (Get-InstallerValue $Probe 'DesktopTaskChecked'); desktopTaskSource = (Get-InstallerValue $Probe 'DesktopTaskSource')
        launchChecked = (Get-InstallerValue $Probe 'LaunchChecked'); launchSource = (Get-InstallerValue $Probe 'LaunchSource'); finishClicked = (Test-InstallerTrue (Get-InstallerValue $Probe 'FinishClicked'))
        programStarted = (Test-InstallerTrue (Get-InstallerValue $Probe 'ProgramStarted')); programSeconds = (Get-InstallerValue $Probe 'ProgramSeconds')
        buttonNames = @(Get-InstallerList $Probe 'ButtonNames'); checkErrors = @(Get-InstallerList $Probe 'CheckErrors'); probeError = $ProbeError; log = $WizardLog; screenshots = $Copied }
    Add-InstallerTimeline -Context $Context -Text "kreator: stron $($Pages.Count), program $($Record.data.programStarted)"
}

function Invoke-InstallerMotwPhase {
    [CmdletBinding()]
    param([Parameter(Mandatory)][System.Collections.IDictionary]$Context, [Parameter(Mandatory)][System.Collections.IDictionary]$Record)
    $Plan = $Context.Plan
    $Artifact = $Context.Artifact
    $Remote = Initialize-InstallerVm -Context $Context -Files @($Artifact.Setup)
    $Motw = Invoke-InstallerRemote -Context $Context -ScriptBlock $script:InstMotwBlock -ArgumentList @($Remote[$Artifact.Setup.Name], $Plan.UserDesktop, [int]$Plan.MotwZoneId)
    $null = Invoke-InstallerRemote -Context $Context -ScriptBlock $script:InstStartTaskBlock -ArgumentList @('%SystemRoot%\explorer.exe', "`"$($Motw.Path)`"", '', $Plan.UserName, $Plan.TaskNames.Motw, 'Limited')
    Add-InstallerTimeline -Context $Context -Text 'motw: start przez explorer.exe'
    Start-Sleep -Seconds ([int]$Plan.MotwWaitSeconds)
    $Json = Invoke-DsoUserSessionScript -Session $Context.Session -Layout (Get-InstallerLayout -Kind 'Portable') -Name 'Inst-Motw' -ScriptText (Get-InstallerWindowsProbeScript) `
        -ArgumentText "-Shot $($Plan.OutDirectory)\motw.png" -UserName $Plan.UserName -TimeoutSeconds 120
    if ([string]::IsNullOrWhiteSpace($Json)) { throw 'Sonda okien nie zwróciła wyniku.' }
    $Probe = $Json | ConvertFrom-Json
    # Jak w kreatorze: wynik błędu sondy ma tylko Ok i Error, więc pola czytane bez założenia, że istnieją.
    $ProbeError = Get-InstallerValue $Probe 'Error'
    if ($ProbeError) { $Record.problems += "sonda okien: $ProbeError" }
    $Win32Windows = @(Get-InstallerList $Probe 'Win32Windows' | Where-Object { $null -ne $_ })
    $Titles = @(@(Get-InstallerList $Probe 'Titles') + @($Win32Windows | ForEach-Object { [string](Get-InstallerValue $_ 'Title') }) | Where-Object { $_ } | Select-Object -Unique)
    $Copied = $null
    if (Test-InstallerTrue (Get-InstallerValue $Probe 'Screenshot')) { $Copied = Copy-InstallerScreenshot -Context $Context -Record $Record -Name 'motw.png' }
    $Record.screenshots = @($Copied | Where-Object { $_ })
    $Record.data = [ordered]@{ zoneId = $Motw.ZoneId; path = $Motw.Path; titles = $Titles; win32Windows = $Win32Windows; screenshot = ($null -ne $Copied)
        screenshotError = (Get-InstallerValue $Probe 'ScreenshotError'); probeError = $ProbeError }
}

function Invoke-InstallerPhase {
    # Jedna faza na już uruchomionej VM; logi (Inno, smoke) do raw-<faza> w finally.
    [CmdletBinding()]
    param([Parameter(Mandatory)][System.Collections.IDictionary]$Run, [Parameter(Mandatory)][System.Collections.IDictionary]$Context, [Parameter(Mandatory)][System.Collections.IDictionary]$Record)
    try {
        switch ($Run.Phase) {
            'smoke' { Invoke-InstallerSmokePhase -Context $Context -Record $Record }
            'running' { Invoke-InstallerRunningPhase -Context $Context -Record $Record }
            'upgrade' { Invoke-InstallerUpgradePhase -Context $Context -Record $Record }
            'wizard' { Invoke-InstallerWizardPhase -Context $Context -Record $Record }
            'motw' { Invoke-InstallerMotwPhase -Context $Context -Record $Record }
            default { throw "Nieznana faza '$($Run.Phase)'." }
        }
    }
    finally {
        $Source = if ($Run.Phase -eq 'smoke') { $Context.Plan.SmokeLogDirectory } else { $Context.Plan.LogDirectory }
        try { $Record.rawFiles = @(Copy-ExploreRawFiles -Session $Context.Session -Destination (Join-Path $Context.VmDirectory "raw-$($Run.Key)") -Source $Source | ForEach-Object { Split-Path -Leaf $_ }) }
        catch { $Record.problems += "kopia logów: $($_.Exception.Message)" }
    }
}

function Invoke-InstallerVmRun {
    # Jedna VM, fazy po kolei; KAŻDA: revert -> karta odłączona -> start -> gotowość (zalogowany dso) -> faza -> ZAWSZE
    # Complete-ExploreVmCleanup. Werdykt z Get-InstallerVerdict; problem fazy albo nieudane sprzątanie odbiera zaliczenie. Zapis installer.json.
    [CmdletBinding()]
    [OutputType([string])]
    param(
        [Parameter(Mandatory)][ValidateSet('DSO-26H2-Home', 'DSO-26H2-Pro')][string]$VmName,
        [Parameter(Mandatory)][string]$OutDirectory,
        [Parameter(Mandatory)][object[]]$Runs,
        [Parameter(Mandatory)][System.Collections.IDictionary]$Artifact,
        [string]$CheckpointName = 'czysta-26H2',
        [int]$ReadyTimeoutSeconds = 1200
    )
    Assert-DsoVmLiveAllowed
    $Credential = Get-DsoVmCredential -VmName $VmName
    $Plan = Get-InstallerPlan
    $StartedAt = Get-Date
    $Results = [ordered]@{}
    $Errors = @()
    $Timeline = New-Object System.Collections.Generic.List[string]
    $VmDirectory = Join-Path $OutDirectory $VmName
    $AuthFailure = $null
    foreach ($Run in $Runs) {
        $Record = [ordered]@{ key = $Run.Key; phase = $Run.Phase; language = $Run.Language; passed = $false; checks = @(); data = $null; error = $null; problems = @(); screenshots = @(); rawFiles = @(); cleanup = $null }
        $Results[$Run.Key] = $Record
        $Session = $null
        try {
            Restore-VmCleanState -VmName $VmName -CheckpointName $CheckpointName
            # Bez sieci: karta odłączona przed startem (checkpoint mógł mieć kartę na przełączniku).
            Disconnect-VMNetworkAdapter -VMName $VmName -ErrorAction Stop
            Start-VM -Name $VmName
            $Session = Wait-VmReady -VmName $VmName -Credential $Credential -TimeoutSeconds $ReadyTimeoutSeconds
            $Timeline.Add("$(Get-Date -Format 'HH:mm:ss') $($Run.Key) VM gotowa")
            Write-Host "$(Get-Date -Format 'HH:mm:ss') $VmName $($Run.Key)"
            # Chwila na dokończenie logowania dso (powłoka, pakiety) przed pierwszym zadaniem w jego sesji.
            Start-Sleep -Seconds ([int]$Plan.SettleAfterLogonSeconds)
            $Context = [ordered]@{ Session = $Session; Plan = $Plan; Artifact = $Artifact; VmName = $VmName; VmDirectory = $VmDirectory; Run = $Run; Timeline = $Timeline }
            Invoke-InstallerPhase -Run $Run -Context $Context -Record $Record
        }
        catch {
            $Record.error = $_.Exception.Message
            if (Test-VmAuthFailure -Exception $_.Exception) { $AuthFailure = $_ }
        }
        finally {
            if ($null -ne $Session) { Remove-PSSession -Session $Session -ErrorAction SilentlyContinue }
            $Cleanup = Complete-ExploreVmCleanup -VmName $VmName -CheckpointName $CheckpointName
            $Record.cleanup = $Cleanup
            if (-not $Cleanup.Ok) {
                Write-Warning "VM '$VmName': sprzątanie: $(@($Cleanup.Problems) -join ' | ')"
                $Record.problems += "sprzątanie VM: $(@($Cleanup.Problems) -join ' | ')"
            }
        }
        $Record.checks = @(Get-InstallerVerdict -Phase $Run.Phase -Data $Record.data)
        $Failed = @($Record.checks | Where-Object { -not $_.Passed })
        $Problems = @($Record.problems | Where-Object { $_ })
        $Record.passed = ($Record.checks.Count -gt 0) -and ($Failed.Count -eq 0) -and ($Problems.Count -eq 0) -and (-not $Record.error)
        if ($Record.error) { $Errors += "$($Run.Key): $($Record.error)" }
        foreach ($Problem in $Problems) { $Errors += "$($Run.Key): $Problem" }
        foreach ($Check in $Failed) { $Errors += "$($Run.Key): $($Check.Name) - $($Check.Detail)" }
        Write-Host ("{0}: {1} ({2}/{3} sprawdzeń)" -f $Run.Key, $(if ($Record.passed) { 'PASS' } else { 'FAIL' }), ($Record.checks.Count - $Failed.Count), $Record.checks.Count)
        # Błąd uwierzytelnienia przerywa całość (blokada konta Administrator w VM).
        if ($null -ne $AuthFailure) { break }
    }
    $Result = [ordered]@{ schema = 1; vmName = $VmName; checkpoint = $CheckpointName; startedAt = $StartedAt.ToString('o'); finishedAt = (Get-Date).ToString('o'); artifact = $Artifact
        allPassed = ($Results.Count -eq @($Runs).Count -and @($Results.Values | Where-Object { -not $_.passed }).Count -eq 0); errors = $Errors; timeline = $Timeline.ToArray(); phases = $Results }
    $Saved = Save-ExploreResult -Result $Result -OutDirectory $OutDirectory -VmName $VmName -FileName 'installer.json'
    if ($null -ne $AuthFailure) { throw $AuthFailure }
    if (-not $Result.allPassed) { throw "VM '$VmName': $($Errors -join ' | ') (wynik: $Saved)" }
    return $Saved
}

function Invoke-InstallerExplorationMain {
    # Wejście skryptu (testowalne przez dot-source): argumenty -> -ShowPlan (sam plan, nic nie uruchamia) albo przebieg na VM. Zwraca kod wyjścia.
    [CmdletBinding()]
    [OutputType([int])]
    param(
        [Parameter(Mandatory)][ValidateSet('DSO-26H2-Home', 'DSO-26H2-Pro')][string]$VmName,
        [AllowEmptyString()][AllowNull()][string]$ArtifactDirectory,
        [AllowEmptyString()][AllowNull()][string]$OutDirectory,
        [Parameter(Mandatory)][AllowEmptyCollection()][string[]]$Phase,
        [AllowEmptyString()][AllowNull()][string]$UpgradeInstallerPath,
        [Parameter(Mandatory)][AllowEmptyCollection()][string[]]$Language,
        [Parameter(Mandatory)][string]$CheckpointName,
        [Parameter(Mandatory)][int]$ReadyTimeoutSeconds,
        [switch]$ShowPlan
    )
    try {
        Set-StrictMode -Version Latest
        $ErrorActionPreference = 'Stop'
        $VmRoot = Split-Path -Parent $script:InstallerScriptRoot
        $RepoRoot = Get-DsoRepoRoot
        $Out = Assert-ExploreOutDirectory -Path $OutDirectory -RepoRoot $RepoRoot -Example '$env:TEMP\dso-explore\instalator\vm'
        $Out = Resolve-ExploreOutDirectory -Path $Out
        $RunArguments = Get-InstallerRunArguments -VmName $VmName -ArtifactDirectory $ArtifactDirectory -OutDirectory $Out -Phase $Phase -UpgradeInstallerPath $UpgradeInstallerPath `
            -Language $Language -CheckpointName $CheckpointName -ReadyTimeoutSeconds $ReadyTimeoutSeconds
        if ($ShowPlan) {
            [pscustomobject]$RunArguments | ConvertTo-Json -Depth 8
            return 0
        }
        Import-Module (Join-Path $VmRoot 'modules\VmCommon.psm1') -Force
        Import-Module (Join-Path $VmRoot 'modules\VmHost.psm1') -Force
        # ExploreCommon i VmSmoke trzymają referencję do starej instancji VmHost; ponowny import po nim wiąże je z bieżącą.
        Import-Module (Join-Path $script:InstallerScriptRoot 'ExploreCommon.psm1') -Force
        Import-Module (Join-Path $VmRoot 'modules\VmSmoke.psm1') -Force
        Import-Module (Join-Path $VmRoot 'modules\VmRun.psm1') -Force
        $Principal = New-Object System.Security.Principal.WindowsPrincipal([System.Security.Principal.WindowsIdentity]::GetCurrent())
        if (-not $Principal.IsInRole([System.Security.Principal.WindowsBuiltInRole]::Administrator)) {
            throw 'Uruchom skrypt w sesji PowerShell jako administrator (Hyper-V i PowerShell Direct tego wymagają).'
        }
        Write-Host "Wyniki: $Out"
        Write-Host "Wydanie $($RunArguments.Artifact.Version): $($RunArguments.Artifact.Directory)"
        Write-Host "VM: $VmName; fazy: $(@($RunArguments.Runs | ForEach-Object { $_.Key }) -join ', ')"
        $Saved = Invoke-InstallerVmRun -VmName $VmName -OutDirectory $Out -Runs @($RunArguments.Runs) -Artifact $RunArguments.Artifact -CheckpointName $CheckpointName -ReadyTimeoutSeconds $ReadyTimeoutSeconds
        Write-Host "Zapisano: $Saved"
        return 0
    }
    catch {
        Write-Host "BŁĄD: $($_.Exception.Message)" -ForegroundColor Red
        Write-Host $_.ScriptStackTrace -ForegroundColor DarkGray
        return 1
    }
}

# Przy dot-sourcingu (testy) tylko definicje; uruchomienie przebiegu tylko przy zwykłym wywołaniu skryptu.
if ($MyInvocation.InvocationName -ne '.') {
    # Ostatni obiekt wyjścia = kod; wcześniejsze (plan JSON przy -ShowPlan) idą na wyjście.
    $Output = @(Invoke-InstallerExplorationMain -VmName $VmName -ArtifactDirectory $ArtifactDirectory -OutDirectory $OutDirectory -Phase $Phase -UpgradeInstallerPath $UpgradeInstallerPath `
            -Language $Language -CheckpointName $CheckpointName -ReadyTimeoutSeconds $ReadyTimeoutSeconds -ShowPlan:$ShowPlan)
    if ($Output.Count -gt 1) { $Output[0..($Output.Count - 2)] }
    exit ([int]$Output[-1])
}
