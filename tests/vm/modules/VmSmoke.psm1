<#
Instrukcja uruchomienia:
    Moduł ładuje skrypt tests\vm\Invoke-GuiSmoke.ps1; nie uruchamia się go osobno.
    Testy (na mockach, bez VM): Invoke-Pester -Path tests/vm/tests/VmSmoke.Tests.ps1
Opis działania:
    Smoke GUI na VM Hyper-V 26H2: kopiuje build, startuje DSOptimizer.exe w sesji interaktywnej użytkownika 'dso'
    (zadanie Harmonogramu, principal Interactive + Highest, jak K2 w tests\vm\explore\Invoke-C5Exploration.ps1), czeka na koniec
    detekcji startowej, odczytuje sondą (też zadanie w sesji dso, bo Process.Responding działa tylko w sesji okna) stan procesu,
    zdarzenia Application (.NET Runtime / Application Error / Windows Error Reporting) od startu i linie ERROR w logach,
    robi zrzut ekranu przez Msvm_VirtualSystemManagementService.GetVirtualSystemThumbnailImage (PNG) i agreguje wynik.
    Po starcie sonda UI (VmSmokeUi, też w sesji dso) sprawdza stronę „Historia zmian” po zastosowaniu jednej bezpiecznej pozycji
    przez UI Automation (ux.menu-delay: „Zastosuj (1)” bez okna potwierdzenia) i mierzy WM_NCHITTEST paska marki (stan normalny i po maksymalizacji);
    drugi zrzut pokazuje okno po tej sondzie. Sonda klika też prawdziwą myszą: filtry, wiersze listy i znacznik uruchomienia panelu (patrz VmSmokeUi).
    Na końcu sonda wchodzi na „Prywatność i AI” i czyta N z „Zastosuj (N)”; oczekiwane N liczy host z raportów CLI --detect sugerowanych
    pozycji tego przebiegu (NotApplied/Partial, Get-DsoPrivacyDetection + Get-GuiPrivacyExpectedCount), uruchomionych przed sondą UI. Funkcje przywracania, gotowości i kopiowania pochodzą z VmHost.
    Potem sonda wchodzi na „Aplikacje” i czyta N z „Usuń (N)”; oczekiwane N = sugerowane usuwalne wiersze z CLI --list-apps tego
    przebiegu (Get-DsoAppsListing + Get-GuiAppsExpectedCount), uruchomionego przed sondą UI jako 'dso' - to samo konto co okno.
    Na czystym checkpoincie (brak settings.json) pierwsze okno to wybór języka: Confirm-DsoLanguageChoice (UIA w sesji dso) czeka
    do 30 s na okno LanguageChoiceWindow i klika kafelek wybranego języka (-Language pl|en); sprawdzenie LanguageChoice jest zielone
    tylko, gdy okno się pokazało i po wyborze pojawiło się okno główne. CLI w runnerze dostaje --lang pl (VmRun\Invoke-DsoCli).
    Potem sonda wchodzi na „Przygotowanie” (7 tytułów kroków po odczycie, przycisk „Otwórz Windows Update”, linia karty w kroku
    „Grafika” – PrepPage; bez klikania linków), potem na „Naprawy” (lista ma wszystkie tytuły kategorii repair z katalogu –
    RepairPage, Get-GuiRepairTitle; niczego nie uruchamia), a jako ostatnie na „Instalacja” (Etap 4c: wszystkie kategorie i tytuły
    z apps.json, „Zainstaluj (0)” nieaktywne – InstallPage, Get-GuiInstallTitle; bez zaznaczania, instalowania i wyszukiwania),
    więc drugi zrzut pokazuje stronę „Instalacja”.
    Dotyka wyłącznie VM DSO-26H2-Home i DSO-26H2-Pro; na hoście niczego nie zmienia (poza plikami w OutDirectory).
#>
Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

$script:ModulesDirectory = $PSScriptRoot
# Bez -Force: gdy moduły są już załadowane (skrypt, testy z mockami), nie przeładowujemy ich.
Import-Module (Join-Path $script:ModulesDirectory 'VmCommon.psm1')
Import-Module (Join-Path $script:ModulesDirectory 'VmHost.psm1')
Import-Module (Join-Path $script:ModulesDirectory 'VmSmokeUi.psm1')
Import-Module (Join-Path $script:ModulesDirectory 'VmRun.psm1')
Import-Module (Join-Path (Split-Path -Parent $script:ModulesDirectory) 'explore\ExploreCommon.psm1')

function Get-GuiSmokeProbeScript {
    # Skrypt sondy uruchamiany W VM w sesji użytkownika 'dso' (tylko ASCII, PowerShell 5.1). Zapisuje JSON do -ResultFile.
    [CmdletBinding()]
    [OutputType([string])]
    param()
    return @'
param([Parameter(Mandatory)][string]$ResultFile, [Parameter(Mandatory)][string]$Since)
$ErrorActionPreference = 'Stop'
$Out = [ordered]@{
    Ok = $false; Error = $null; ProcessAlive = $false; ProcessId = $null; SessionId = $null; Responding = $null
    MainWindowHandle = 0; MainWindowTitle = ''; Events = @(); EventsError = $null; ErrorLines = @(); LogFiles = @()
}
try {
    $Start = [datetime]::Parse($Since, [System.Globalization.CultureInfo]::InvariantCulture, [System.Globalization.DateTimeStyles]::RoundtripKind)
    $Processes = @(Get-Process -Name 'DSOptimizer' -ErrorAction SilentlyContinue)
    if ($Processes.Count -gt 0) {
        $Process = $Processes[0]
        $Process.Refresh()
        $Out.ProcessAlive = (-not $Process.HasExited)
        $Out.ProcessId = $Process.Id
        $Out.SessionId = $Process.SessionId
        if ($Out.ProcessAlive) {
            $Out.Responding = [bool]$Process.Responding
            $Out.MainWindowHandle = [int64]$Process.MainWindowHandle
            $Out.MainWindowTitle = [string]$Process.MainWindowTitle
        }
    }
    $Found = @()
    try { $Found = @(Get-WinEvent -FilterHashtable @{ LogName = 'Application'; StartTime = $Start; ProviderName = @('.NET Runtime', 'Application Error', 'Windows Error Reporting') } -ErrorAction Stop) }
    catch {
        # Brak pasujacych zdarzen to zero zdarzen; kazdy inny blad odczytu musi byc widoczny (EventsError), nie udawac sukcesu.
        if ($_.FullyQualifiedErrorId -notlike 'NoMatchingEventsFound*') { $Out.EventsError = $_.Exception.Message }
    }
    $Out.Events = @($Found | ForEach-Object {
            $Text = [string]$_.Message
            if ($Text.Length -gt 400) { $Text = $Text.Substring(0, 400) }
            [ordered]@{ Provider = [string]$_.ProviderName; Id = [int]$_.Id; Time = $_.TimeCreated.ToString('o'); Message = $Text }
        })
    $LogDirectory = Join-Path $env:ProgramData 'DSOptimizer\logs'
    if (Test-Path -LiteralPath $LogDirectory) {
        $Files = @(Get-ChildItem -LiteralPath $LogDirectory -Filter '*.log' -File)
        $Out.LogFiles = @($Files | ForEach-Object { $_.Name })
        $Lines = foreach ($File in $Files) { Select-String -LiteralPath $File.FullName -Pattern '^\[[^\]]+\] \[ERROR\]' -Encoding UTF8 }
        $Out.ErrorLines = @($Lines | ForEach-Object { $_.Line } | ForEach-Object { if ($_.Length -gt 400) { $_.Substring(0, 400) } else { $_ } })
    }
    $Out.Ok = $true
}
catch { $Out.Error = $_.Exception.Message }
$Json = $Out | ConvertTo-Json -Depth 6
Set-Content -LiteralPath $ResultFile -Value $Json -Encoding UTF8
'@
}

function Start-DsoGuiInUserSession {
    # W VM: rejestruje i startuje zadanie Harmonogramu (principal 'dso', Interactive, Highest), które uruchamia DSOptimizer.exe
    # w interaktywnej sesji. Zwraca czas startu wg zegara GOŚCIA (ISO 'o', UTC) - granica dla zdarzeń. Zadania nie wyrejestrowuje
    # (wyrejestrowanie mogłoby zabić okno; revert checkpointu je usuwa, przy -KeepRunning zostaje).
    [CmdletBinding()]
    [OutputType([string])]
    param(
        [Parameter(Mandatory)][AllowNull()][System.Management.Automation.Runspaces.PSSession]$Session,
        [Parameter(Mandatory)][hashtable]$Layout,
        [ValidatePattern('^[A-Za-z0-9_-]+\z')][string]$UserName = 'dso',
        [ValidatePattern('^[A-Za-z0-9-]+\z')][string]$TaskName = 'DSO-GuiSmoke-App'
    )
    Assert-DsoVmLiveAllowed
    $Started = Invoke-Command -Session $Session -ArgumentList @($Layout.Exe, $Layout.App, $UserName, $TaskName) -ScriptBlock {
        param([string]$Exe, [string]$WorkDir, [string]$User, [string]$Name)
        $ErrorActionPreference = 'Stop'
        $Since = (Get-Date).ToUniversalTime().ToString('o')
        $Action = New-ScheduledTaskAction -Execute $Exe -WorkingDirectory $WorkDir
        $Principal = New-ScheduledTaskPrincipal -UserId $User -LogonType Interactive -RunLevel Highest
        $Settings = New-ScheduledTaskSettingsSet -AllowStartIfOnBatteries -DontStopIfGoingOnBatteries -ExecutionTimeLimit ([TimeSpan]::Zero)
        $null = Register-ScheduledTask -TaskName $Name -Action $Action -Principal $Principal -Settings $Settings -Force
        Start-ScheduledTask -TaskName $Name
        return $Since
    }
    $Text = [string]($Started | Select-Object -Last 1)
    if ([string]::IsNullOrWhiteSpace($Text)) { throw 'Start aplikacji w sesji dso nie zwrócił czasu startu.' }
    return $Text
}

function Invoke-DsoUserSessionScript {
    # W VM: zadanie Harmonogramu w sesji 'dso' (Interactive + Highest) uruchamia skrypt PowerShell z -ResultFile <json> i dodatkowymi
    # argumentami; host czeka na plik wyniku i zwraca jego tekst (albo $null). Zadanie jest wyrejestrowywane po odczycie.
    [CmdletBinding()]
    param(
        [Parameter(Mandatory)][AllowNull()][System.Management.Automation.Runspaces.PSSession]$Session,
        [Parameter(Mandatory)][hashtable]$Layout,
        [Parameter(Mandatory)][ValidatePattern('^[A-Za-z0-9-]+\z')][string]$Name,
        [Parameter(Mandatory)][string]$ScriptText,
        [ValidatePattern('^[ -~]*\z')][string]$ArgumentText = '',
        [ValidatePattern('^[A-Za-z0-9_-]+\z')][string]$UserName = 'dso',
        [ValidateRange(10, 900)][int]$TimeoutSeconds = 90
    )
    Assert-DsoVmLiveAllowed
    $Json = Invoke-Command -Session $Session -ArgumentList @($Layout.Out, $ScriptText, $Name, $ArgumentText, $UserName, $TimeoutSeconds) -ScriptBlock {
        param([string]$OutDir, [string]$ProbeText, [string]$ProbeName, [string]$ExtraArguments, [string]$User, [int]$Timeout)
        $ErrorActionPreference = 'Stop'
        $ScriptPath = Join-Path $OutDir "$ProbeName.ps1"
        $ResultPath = Join-Path $OutDir "$ProbeName.json"
        $TaskName = "DSO-$ProbeName"
        $null = New-Item -ItemType Directory -Path $OutDir -Force
        Set-Content -LiteralPath $ScriptPath -Value $ProbeText -Encoding UTF8
        if (Test-Path -LiteralPath $ResultPath) { Remove-Item -LiteralPath $ResultPath -Force }
        $PowerShellPath = Join-Path $env:SystemRoot 'System32\WindowsPowerShell\v1.0\powershell.exe'
        $Action = New-ScheduledTaskAction -Execute $PowerShellPath -Argument "-NoProfile -NonInteractive -ExecutionPolicy Bypass -WindowStyle Hidden -File $ScriptPath -ResultFile $ResultPath $ExtraArguments"
        $Principal = New-ScheduledTaskPrincipal -UserId $User -LogonType Interactive -RunLevel Highest
        $Settings = New-ScheduledTaskSettingsSet -AllowStartIfOnBatteries -DontStopIfGoingOnBatteries -ExecutionTimeLimit (New-TimeSpan -Seconds ($Timeout + 60))
        try {
            $null = Register-ScheduledTask -TaskName $TaskName -Action $Action -Principal $Principal -Settings $Settings -Force
            Start-ScheduledTask -TaskName $TaskName
            for ($Poll = 0; $Poll -lt ($Timeout * 2); $Poll++) {
                if (Test-Path -LiteralPath $ResultPath) {
                    # Plik mógł powstać, ale jeszcze nie być domknięty: krótka pauza przed odczytem.
                    Start-Sleep -Milliseconds 300
                    return (Get-Content -LiteralPath $ResultPath -Raw -Encoding UTF8)
                }
                Start-Sleep -Milliseconds 500
            }
            throw "Sonda $ProbeName w sesji dso nie zapisala wyniku w ${Timeout} s (explorer.exe uzytkownika dso dziala?)."
        }
        finally { Unregister-ScheduledTask -TaskName $TaskName -Confirm:$false -ErrorAction SilentlyContinue }
    }
    $Text = [string]($Json | Where-Object { $_ -is [string] } | Select-Object -Last 1)
    if ([string]::IsNullOrWhiteSpace($Text)) { return $null }
    return $Text.TrimStart([char]0xFEFF)
}

function Get-DsoGuiProbe {
    # W VM: zadanie w sesji 'dso' uruchamia sondę (Get-GuiSmokeProbeScript), host czeka na plik wyniku i zwraca sparsowany obiekt.
    [CmdletBinding()]
    param(
        [Parameter(Mandatory)][AllowNull()][System.Management.Automation.Runspaces.PSSession]$Session,
        [Parameter(Mandatory)][hashtable]$Layout,
        [Parameter(Mandatory)][string]$Since,
        [ValidatePattern('^[A-Za-z0-9_-]+\z')][string]$UserName = 'dso',
        [ValidateRange(10, 600)][int]$TimeoutSeconds = 90
    )
    Assert-DsoVmLiveAllowed
    if ($Since -notmatch '^[0-9A-Za-z:.+-]+\z') { throw "Nieoczekiwany format czasu startu '$Since'." }
    $Text = Invoke-DsoUserSessionScript -Session $Session -Layout $Layout -Name 'GuiSmoke-Probe' -ScriptText (Get-GuiSmokeProbeScript) `
        -ArgumentText "-Since $Since" -UserName $UserName -TimeoutSeconds $TimeoutSeconds
    if ([string]::IsNullOrWhiteSpace($Text)) { throw 'Sonda GUI nie zwróciła wyniku.' }
    return ($Text | ConvertFrom-Json)
}

function Get-DsoGuiUiProbe {
    # W VM: sonda UI (VmSmokeUi\Get-GuiUiProbeScript) w sesji 'dso': menu „Historia zmian”, zastosowanie jednej bezpiecznej pozycji,
    # wiersze historii i WM_NCHITTEST (stan normalny i po maksymalizacji). Zwraca sparsowany obiekt.
    [CmdletBinding()]
    param(
        [Parameter(Mandatory)][AllowNull()][System.Management.Automation.Runspaces.PSSession]$Session,
        [Parameter(Mandatory)][hashtable]$Layout,
        [ValidatePattern('^[A-Za-z0-9_-]+\z')][string]$UserName = 'dso',
        # 600 s: krok 11 („Instalacja”, Etap 4c) czeka na odczyt strony najwyżej 180 s ponad dotychczasowe kroki.
        [ValidateRange(60, 900)][int]$TimeoutSeconds = 600
    )
    Assert-DsoVmLiveAllowed
    $Target = Get-GuiUiProbeTarget
    $Text = Invoke-DsoUserSessionScript -Session $Session -Layout $Layout -Name 'GuiSmoke-Ui' -ScriptText (Get-GuiUiProbeScript) `
        -ArgumentText "-Category ""$($Target.Category)"" -TweakTitle ""$($Target.TweakTitle)""" -UserName $UserName -TimeoutSeconds $TimeoutSeconds
    if ([string]::IsNullOrWhiteSpace($Text)) { throw 'Sonda UI nie zwróciła wyniku.' }
    return ($Text | ConvertFrom-Json)
}

function Get-LanguageChoiceProbeScript {
    # Skrypt uruchamiany W VM w sesji 'dso' (tylko ASCII, PowerShell 5.1): czeka do 30 s na okno wyboru jezyka (AutomationId
    # LanguageChoiceWindow), klika kafelek wybranego jezyka (InvokePattern) i czeka, az okno wyboru zniknie. Gdy zamiast niego
    # pojawi sie okno glowne (czysty checkpoint zawsze ma pokazac wybor), zwraca Shown=false.
    [CmdletBinding()]
    [OutputType([string])]
    param()
    return @'
param([Parameter(Mandatory)][string]$ResultFile, [Parameter(Mandatory)][ValidateSet('pl', 'en')][string]$Language)
$ErrorActionPreference = 'Stop'
$Out = [ordered]@{ Ok = $false; Error = $null; Shown = $false; Chosen = $false; MainWindowAppeared = $false; Language = $Language }
try {
    Add-Type -AssemblyName UIAutomationClient
    Add-Type -AssemblyName UIAutomationTypes
    $AE = [System.Windows.Automation.AutomationElement]
    $TileId = if ($Language -eq 'en') { 'LanguageChoiceEnglish' } else { 'LanguageChoicePolish' }
    function Get-ProcessWindowElement {
        $Process = @(Get-Process -Name 'DSOptimizer' -ErrorAction SilentlyContinue)[0]
        if ($null -eq $Process) { return $null }
        $Process.Refresh()
        if ($Process.MainWindowHandle -eq [IntPtr]::Zero) { return $null }
        return $AE::FromHandle($Process.MainWindowHandle)
    }
    $Chooser = $null
    $Limit = (Get-Date).AddSeconds(30)
    do {
        $Window = Get-ProcessWindowElement
        if ($null -ne $Window) {
            if ($Window.Current.AutomationId -eq 'LanguageChoiceWindow') { $Chooser = $Window; break }
            # Okno glowne bez okna wyboru: wybor sie nie pokazal.
            $Out.MainWindowAppeared = $true
            break
        }
        Start-Sleep -Milliseconds 250
    } while ((Get-Date) -lt $Limit)
    if ($null -ne $Chooser) {
        $Out.Shown = $true
        $ById = New-Object System.Windows.Automation.PropertyCondition($AE::AutomationIdProperty, $TileId)
        $Tile = $null
        $TileLimit = (Get-Date).AddSeconds(10)
        do {
            $Tile = $Chooser.FindFirst([System.Windows.Automation.TreeScope]::Descendants, $ById)
            if ($null -eq $Tile) { Start-Sleep -Milliseconds 250 }
        } while ($null -eq $Tile -and (Get-Date) -lt $TileLimit)
        if ($null -eq $Tile) { throw "Brak kafelka $TileId w oknie wyboru jezyka." }
        $Tile.GetCurrentPattern([System.Windows.Automation.InvokePattern]::Pattern).Invoke()
        $CloseLimit = (Get-Date).AddSeconds(15)
        do {
            Start-Sleep -Milliseconds 250
            $Window = Get-ProcessWindowElement
            $StillChooser = ($null -ne $Window) -and ($Window.Current.AutomationId -eq 'LanguageChoiceWindow')
            if ($null -ne $Window -and -not $StillChooser) { $Out.MainWindowAppeared = $true }
        } while (($StillChooser -or $null -eq $Window) -and (Get-Date) -lt $CloseLimit)
        $Out.Chosen = $Out.MainWindowAppeared
    }
    $Out.Ok = $true
}
catch { $Out.Error = $_.Exception.Message }
$Json = $Out | ConvertTo-Json -Depth 4
Set-Content -LiteralPath $ResultFile -Value $Json -Encoding UTF8
'@
}

function Confirm-DsoLanguageChoice {
    # W VM: sonda UIA w sesji 'dso' (Get-LanguageChoiceProbeScript) czeka do 30 s na okno wyboru języka, wybiera kafelek języka
    # i czeka na okno główne. Zwraca { Shown, Chosen, ... }: Shown=false, gdy okno główne pojawiło się bez wyboru
    # (na czystym checkpoincie brak settings.json, więc wybór ma się pokazać zawsze).
    [CmdletBinding()]
    param(
        [Parameter(Mandatory)][AllowNull()][System.Management.Automation.Runspaces.PSSession]$Session,
        [Parameter(Mandatory)][hashtable]$Layout,
        [ValidateSet('pl', 'en')][string]$Language = 'pl',
        [ValidatePattern('^[A-Za-z0-9_-]+\z')][string]$UserName = 'dso',
        [ValidateRange(60, 300)][int]$TimeoutSeconds = 90
    )
    Assert-DsoVmLiveAllowed
    $Text = Invoke-DsoUserSessionScript -Session $Session -Layout $Layout -Name 'GuiSmoke-Language' -ScriptText (Get-LanguageChoiceProbeScript) `
        -ArgumentText "-Language $Language" -UserName $UserName -TimeoutSeconds $TimeoutSeconds
    if ([string]::IsNullOrWhiteSpace($Text)) { throw 'Sonda okna wyboru języka nie zwróciła wyniku.' }
    $Result = $Text | ConvertFrom-Json
    if ($Result.PSObject.Properties['Error'] -and $Result.Error) { throw "Sonda okna wyboru języka: $($Result.Error)" }
    return $Result
}

function Get-GuiLanguageCheck {
    # Czysta agregacja wyniku Confirm-DsoLanguageChoice -> sprawdzenie { Name, Passed, Detail }. Wynik $null = niezaliczone.
    [CmdletBinding()]
    param(
        [AllowNull()]$Choice,
        [ValidateSet('pl', 'en')][string]$Language = 'pl'
    )
    $Word = if ($Language -eq 'en') { 'angielski' } else { 'polski' }
    $Name = 'LanguageChoice'
    if ($null -eq $Choice) { return New-GuiSmokeCheck -Name $Name -Passed $false -Detail 'brak odczytu okna wyboru języka' }
    $Shown = [bool]$Choice.Shown
    $Chosen = [bool]$Choice.Chosen
    $Detail = if (-not $Shown) { 'okno wyboru języka się nie pokazało (czysty checkpoint nie ma settings.json)' }
    elseif (-not $Chosen) { "okno wyboru języka pokazane, ale po wyborze nie pojawiło się okno główne ($Word)" }
    else { "Okno wyboru języka pokazane i wybrano $Word" }
    return New-GuiSmokeCheck -Name $Name -Passed ($Shown -and $Chosen) -Detail $Detail
}

function Get-DsoPrivacyDetection {
    # W VM (PowerShell Direct, Administrator): CLI DSOptimizer.exe --detect <id> --out <json> z wbudowanym katalogiem (bez --catalog) dla
    # każdej pozycji, po kolei. Tylko odczyt. Użytkownik docelowy HKCU = zalogowany 'dso' (InteractiveUserContext, raport: user.sid/isCertain).
    # Zwraca [{ Id, ExitCode, State, UserSid, IsCertain, Error }]; brak raportu albo zły JSON = State $null z opisem w Error.
    [CmdletBinding()]
    param(
        [Parameter(Mandatory)][AllowNull()][System.Management.Automation.Runspaces.PSSession]$Session,
        [Parameter(Mandatory)][hashtable]$Layout,
        [Parameter(Mandatory)][ValidatePattern('^[a-z0-9.-]+\z')][string[]]$TweakId,
        [ValidateRange(10, 600)][int]$TimeoutSeconds = 120
    )
    Assert-DsoVmLiveAllowed
    $Raw = Invoke-Command -Session $Session -ArgumentList @($Layout.Exe, $Layout.Out, $TweakId, ($TimeoutSeconds * 1000)) -ScriptBlock {
        param([string]$Exe, [string]$OutDir, [string[]]$Ids, [int]$TimeoutMs)
        $ErrorActionPreference = 'Stop'
        $null = New-Item -ItemType Directory -Path $OutDir -Force
        foreach ($Id in $Ids) {
            $OutPath = Join-Path $OutDir "smoke-detect-$Id.json"
            if (Test-Path -LiteralPath $OutPath) { Remove-Item -LiteralPath $OutPath -Force }
            $Process = Start-Process -FilePath $Exe -ArgumentList @('--detect', $Id, '--out', $OutPath) -PassThru -WindowStyle Hidden
            $null = $Process.Handle
            $TimedOut = -not $Process.WaitForExit($TimeoutMs)
            if ($TimedOut) { try { $Process.Kill() } catch { Write-Verbose "Kill: $($_.Exception.Message)" } }
            $Json = $null
            if (Test-Path -LiteralPath $OutPath) { $Json = Get-Content -LiteralPath $OutPath -Raw -Encoding UTF8 }
            @{ Id = $Id; ExitCode = $(if ($TimedOut) { -1 } else { $Process.ExitCode }); TimedOut = $TimedOut; Json = $Json }
        }
    }
    $Result = foreach ($Entry in @($Raw)) {
        if ($null -eq $Entry) { continue }
        $State = $null; $Sid = $null; $Certain = $null; $ParseError = $null
        $Text = [string]$Entry.Json
        if ([string]::IsNullOrWhiteSpace($Text)) { $ParseError = "brak raportu (kod $($Entry.ExitCode), limit czasu: $($Entry.TimedOut))" }
        else {
            try {
                $Report = $Text.TrimStart([char]0xFEFF) | ConvertFrom-Json
                $State = [string]$Report.state
                if ($null -ne $Report.PSObject.Properties['user'] -and $null -ne $Report.user) { $Sid = [string]$Report.user.sid; $Certain = [bool]$Report.user.isCertain }
                if (-not $State) { $ParseError = 'raport bez stanu'; $State = $null }
            }
            catch { $ParseError = "zły JSON raportu: $($_.Exception.Message)" }
        }
        [pscustomobject]@{ Id = [string]$Entry.Id; ExitCode = $Entry.ExitCode; State = $State; UserSid = $Sid; IsCertain = $Certain; Error = $ParseError }
    }
    return @($Result)
}

function Get-DsoAppsListing {
    # W VM: CLI DSOptimizer.exe --list-apps --out <json> (wbudowana klasyfikacja apps-bloat.json) jako konto 'dso' przez zadanie Harmonogramu
    # (Interactive + Highest, VmRun\Invoke-DsoCli -RunAsUser) - te same warunki co okno z Start-DsoGuiInUserSession: konto procesu = konto
    # zalogowane, więc ten sam zakres AppX i te same programy per użytkownik. CLI jako Administrator PS Direct dałoby inny zakres (R8:
    # tryb AllUsers) i programy konta dso jako „innego konta”. Tylko odczyt. Zwraca wynik Invoke-DsoCli (ExitCode, TimedOut, Report, Json, ReportError).
    [CmdletBinding()]
    param(
        [Parameter(Mandatory)][AllowNull()][System.Management.Automation.Runspaces.PSSession]$Session,
        [Parameter(Mandatory)][hashtable]$Layout,
        [ValidatePattern('^[A-Za-z0-9_-]+\z')][string]$UserName = 'dso',
        [ValidateRange(10, 900)][int]$TimeoutSeconds = 300
    )
    Assert-DsoVmLiveAllowed
    $OutPath = "$($Layout.Out)\smoke-list-apps.json"
    $Arguments = ConvertTo-DsoCliArguments -Command 'list-apps' -OutPath $OutPath
    return Invoke-DsoCli -Session $Session -ExePath $Layout.Exe -Arguments $Arguments -OutPath $OutPath -RunAsUser $UserName -TimeoutSeconds $TimeoutSeconds
}

function ConvertTo-DsoRgb565Png {
    # Bajty obrazu RGB565 (16 bpp, bez dopełnienia wierszy) -> PNG na dysku. Zwraca ścieżkę.
    [CmdletBinding()]
    [OutputType([string])]
    param(
        [Parameter(Mandatory)][byte[]]$Data,
        [Parameter(Mandatory)][ValidateRange(1, 8192)][int]$Width,
        [Parameter(Mandatory)][ValidateRange(1, 8192)][int]$Height,
        [Parameter(Mandatory)][string]$Path
    )
    # Format zmierzony na żywo (Hyper-V 26H2): ImageData = 4-bajtowy prefiks (uint32 BIG-ENDIAN = całkowita długość bufora z prefiksem),
    # potem piksele RGB565 bez dopełnienia wierszy (1024x768 -> 1572868 B, 640x480 -> 614404 B). Starsze hosty: sam bufor W*H*2 (offset 0).
    $Expected = $Width * $Height * 2
    $Offset = 0
    $Head = ($Data | Select-Object -First 4 | ForEach-Object { $_.ToString('X2') }) -join ' '
    if ($Data.Length -eq $Expected) { $Offset = 0 }
    elseif ($Data.Length -eq $Expected + 4) {
        $Prefix = ([int64]$Data[0] -shl 24) -bor ([int64]$Data[1] -shl 16) -bor ([int64]$Data[2] -shl 8) -bor [int64]$Data[3]
        if ($Prefix -ne $Data.Length) { throw "Zrzut ma $($Data.Length) B z prefiksem długości $Prefix (pierwsze 4 B: $Head), oczekiwano prefiksu równego długości bufora." }
        $Offset = 4
    }
    else { throw "Zrzut ma $($Data.Length) B (pierwsze 4 B: $Head), a $Width x $Height w RGB565 to $Expected B albo $($Expected + 4) B z 4-bajtowym prefiksem długości." }
    Add-Type -AssemblyName System.Drawing
    $Bitmap = New-Object System.Drawing.Bitmap($Width, $Height, [System.Drawing.Imaging.PixelFormat]::Format16bppRgb565)
    try {
        $Rect = New-Object System.Drawing.Rectangle(0, 0, $Width, $Height)
        $Locked = $Bitmap.LockBits($Rect, [System.Drawing.Imaging.ImageLockMode]::WriteOnly, [System.Drawing.Imaging.PixelFormat]::Format16bppRgb565)
        try {
            $RowBytes = $Width * 2
            for ($Row = 0; $Row -lt $Height; $Row++) {
                $Target = [IntPtr]($Locked.Scan0.ToInt64() + [int64]$Row * $Locked.Stride)
                [System.Runtime.InteropServices.Marshal]::Copy($Data, $Offset + $Row * $RowBytes, $Target, $RowBytes)
            }
        }
        finally { $Bitmap.UnlockBits($Locked) }
        $Bitmap.Save($Path, [System.Drawing.Imaging.ImageFormat]::Png)
    }
    finally { $Bitmap.Dispose() }
    return $Path
}

function Get-DsoVmResolution {
    # Host: aktualna rozdzielczość ekranu gościa z Msvm_VideoHead (CurrentHorizontalResolution/CurrentVerticalResolution).
    # Błąd odczytu nie przerywa smoke: zwraca 1024x768 z Source='fallback' i przyczyną w Error.
    [CmdletBinding()]
    param(
        [Parameter(Mandatory)][ValidateSet('DSO-26H2-Home', 'DSO-26H2-Pro')][string]$VmName
    )
    Assert-DsoVmLiveAllowed
    $Namespace = 'root\virtualization\v2'
    try {
        $System = Get-CimInstance -Namespace $Namespace -ClassName Msvm_ComputerSystem -Filter "ElementName='$VmName'" -ErrorAction Stop
        if ($null -eq $System) { throw "Nie znaleziono VM '$VmName' w WMI." }
        $Head = @(Get-CimAssociatedInstance -InputObject @($System)[0] -ResultClassName Msvm_VideoHead -ErrorAction Stop) |
            Where-Object { [int]$_.CurrentHorizontalResolution -gt 0 -and [int]$_.CurrentVerticalResolution -gt 0 } | Select-Object -First 1
        if ($null -eq $Head) { throw 'Msvm_VideoHead nie zwróciło rozdzielczości.' }
        $W = [int]$Head.CurrentHorizontalResolution
        $H = [int]$Head.CurrentVerticalResolution
        if ($W -lt 64 -or $W -gt 4096 -or $H -lt 64 -or $H -gt 4096) { throw "Rozdzielczość ${W}x${H} poza zakresem 64-4096." }
        return [pscustomobject]@{ Width = $W; Height = $H; Source = 'guest'; Error = $null }
    }
    catch { return [pscustomobject]@{ Width = 1024; Height = 768; Source = 'fallback'; Error = $_.Exception.Message } }
}

function Save-DsoVmThumbnail {
    # Host: zrzut ekranu VM przez Msvm_VirtualSystemManagementService.GetVirtualSystemThumbnailImage (root\virtualization\v2), PNG.
    [CmdletBinding()]
    [OutputType([string])]
    param(
        [Parameter(Mandatory)][ValidateSet('DSO-26H2-Home', 'DSO-26H2-Pro')][string]$VmName,
        [Parameter(Mandatory)][string]$Path,
        [ValidateRange(64, 4096)][int]$Width = 1024,
        [ValidateRange(64, 4096)][int]$Height = 768
    )
    Assert-DsoVmLiveAllowed
    $Namespace = 'root\virtualization\v2'
    $System = Get-CimInstance -Namespace $Namespace -ClassName Msvm_ComputerSystem -Filter "ElementName='$VmName'" -ErrorAction Stop
    if ($null -eq $System) { throw "Nie znaleziono VM '$VmName' w WMI ($Namespace)." }
    $Setting = Get-CimAssociatedInstance -InputObject @($System)[0] -Association Msvm_SettingsDefineState -ResultClassName Msvm_VirtualSystemSettingData -ErrorAction Stop
    $Service = Get-CimInstance -Namespace $Namespace -ClassName Msvm_VirtualSystemManagementService -ErrorAction Stop
    $Result = Invoke-CimMethod -InputObject @($Service)[0] -MethodName GetVirtualSystemThumbnailImage -ErrorAction Stop -Arguments @{
        TargetSystem = @($Setting)[0]; WidthPixels = [uint16]$Width; HeightPixels = [uint16]$Height
    }
    if ($Result.ReturnValue -ne 0) { throw "GetVirtualSystemThumbnailImage zwróciło kod $($Result.ReturnValue)." }
    return (ConvertTo-DsoRgb565Png -Data ([byte[]]$Result.ImageData) -Width $Width -Height $Height -Path $Path)
}

function New-GuiSmokeCheck {
    [CmdletBinding()]
    param([string]$Name, [bool]$Passed, [string]$Detail)
    return [pscustomobject]@{ Name = $Name; Passed = $Passed; Detail = $Detail }
}

function Get-GuiSmokeCheck {
    # Czysta agregacja: sonda + ścieżka zrzutu -> lista sprawdzeń { Name, Passed, Detail }. Sonda $null = wszystko niezaliczone.
    [CmdletBinding()]
    param(
        [AllowNull()]$Probe,
        [AllowNull()][string]$ScreenshotPath
    )
    $ShotOk = (-not [string]::IsNullOrWhiteSpace($ScreenshotPath)) -and (Test-Path -LiteralPath $ScreenshotPath -PathType Leaf)
    $Checks = @()
    if ($null -eq $Probe) {
        foreach ($Name in 'ProcessAlive', 'Responding', 'NoCrashEvents', 'NoLogErrors') { $Checks += New-GuiSmokeCheck -Name $Name -Passed $false -Detail 'brak odczytu sondy' }
    }
    else {
        $Props = $Probe.PSObject.Properties
        $Get = { param($N, $Default) if ($null -ne $Props[$N]) { $Props[$N].Value } else { $Default } }
        $Alive = [bool](& $Get 'ProcessAlive' $false)
        $AliveDetail = if ($Alive) { "PID $(& $Get 'ProcessId' '?'), sesja $(& $Get 'SessionId' '?')" } else { 'proces DSOptimizer nie działa' }
        $Checks += New-GuiSmokeCheck -Name 'ProcessAlive' -Passed $Alive -Detail $AliveDetail
        # Responding bez okna głównego byłoby zawsze True (brak okna = brak komunikatów do wysłania), więc wymagamy uchwytu okna.
        $Handle = [int64](& $Get 'MainWindowHandle' 0)
        $Resp = ($Alive -and ((& $Get 'Responding' $false) -eq $true) -and $Handle -ne 0)
        $Checks += New-GuiSmokeCheck -Name 'Responding' -Passed $Resp -Detail "Responding=$(& $Get 'Responding' '?'), uchwyt okna=$Handle, tytuł='$(& $Get 'MainWindowTitle' '')'"
        $Events = @(& $Get 'Events' @())
        $EventDetail = if ($Events.Count -eq 0) { 'brak zdarzeń .NET Runtime / Application Error / WER' } else { ($Events | Select-Object -First 3 | ForEach-Object { "$($_.Provider) #$($_.Id)" }) -join '; ' }
        $EventsError = & $Get 'EventsError' $null
        if ($EventsError) { $EventDetail = "błąd odczytu zdarzeń: $EventsError" }
        $Checks += New-GuiSmokeCheck -Name 'NoCrashEvents' -Passed ((-not $EventsError) -and $Events.Count -eq 0) -Detail $EventDetail
        $Lines = @(& $Get 'ErrorLines' @())
        $ProbeOk = [bool](& $Get 'Ok' $false)
        $LogFiles = @(& $Get 'LogFiles' @())
        $LogDetail = if ($Lines.Count -gt 0) { "$($Lines.Count) linii ERROR, np.: $($Lines[0])" } elseif (-not $ProbeOk) { "sonda zakończona błędem: $(& $Get 'Error' '?')" } elseif ($LogFiles.Count -eq 0) { 'brak plików logu (aplikacja nic nie zapisała)' } else { "brak linii ERROR (plików logu: $($LogFiles.Count))" }
        $Checks += New-GuiSmokeCheck -Name 'NoLogErrors' -Passed ($ProbeOk -and $Lines.Count -eq 0) -Detail $LogDetail
    }
    $ShotDetail = if ($ShotOk) { $ScreenshotPath } else { 'brak zrzutu ekranu' }
    $Checks += New-GuiSmokeCheck -Name 'Screenshot' -Passed $ShotOk -Detail $ShotDetail
    return , $Checks
}

function Invoke-DsoGuiSmoke {
    # Jedna VM: revert -> start -> gotowość -> kopia builda -> start GUI w sesji dso -> czekanie -> zrzut -> detekcja CLI sugerowanych
    # pozycji „Prywatność i AI” (oczekiwane N) -> CLI --list-apps jako dso (oczekiwane N „Usuń (N)”) -> sonda UI (historia, WM_NCHITTEST,
    # filtry, wiersze, „Prywatność i AI”, „Aplikacje”, „Przygotowanie”, „Naprawy”, na końcu „Instalacja”) -> drugi zrzut -> sonda stanu (zdarzenia i log od startu, więc obejmują też sondę UI) -> (domyślnie) revert i VM Off.
    # Zwraca wynik (passed, checks, errors, screenshotPath, uiScreenshotPath, resultPath); zapisuje gui-smoke-<VM>.json w OutDirectory.
    [CmdletBinding()]
    param(
        [Parameter(Mandatory)][ValidateSet('DSO-26H2-Home', 'DSO-26H2-Pro')][string]$VmName,
        [Parameter(Mandatory)][string]$OutDirectory,
        [Parameter(Mandatory)][string]$BuildDirectory,
        [Parameter(Mandatory)][string]$CatalogPath,
        [switch]$KeepRunning,
        [string]$CheckpointName = 'czysta-26H2',
        [ValidateRange(60, 7200)][int]$ReadyTimeoutSeconds = 1200,
        [ValidateRange(0, 600)][int]$StartupSeconds = 45,
        [ValidateSet('pl', 'en')][string]$Language = 'pl'
    )
    Assert-DsoVmLiveAllowed
    $OutDirectory = Resolve-ExploreOutDirectory -Path $OutDirectory
    $Layout = Get-DsoVmLayout
    if (-not (Test-Path -LiteralPath (Join-Path $BuildDirectory 'DSOptimizer.exe') -PathType Leaf)) {
        throw "Brak DSOptimizer.exe w '$BuildDirectory'. Zbuduj aplikację: dotnet build DSOptimizer.sln -c Release"
    }
    $null = New-Item -ItemType Directory -Path $OutDirectory -Force
    $ScreenshotPath = Join-Path $OutDirectory "gui-smoke-$VmName.png"
    $UiScreenshotPath = Join-Path $OutDirectory "gui-smoke-$VmName-historia.png"
    $ResultPath = Join-Path $OutDirectory "gui-smoke-$VmName.json"
    foreach ($Old in @($ScreenshotPath, $UiScreenshotPath)) { if (Test-Path -LiteralPath $Old) { Remove-Item -LiteralPath $Old -Force } }
    $Probe = $null
    $UiProbe = $null
    $UiError = $null
    $LanguageChoice = $null
    $LanguageError = $null
    $PrivacyDetection = $null
    $PrivacyExpected = $null
    $DetectionError = $null
    $AppsListing = $null
    $AppsExpected = $null
    $AppsListError = $null
    $RepairTitles = $null
    $RepairTitlesError = $null
    $InstallExpected = $null
    $InstallTitlesError = $null
    $Shot = $null
    $ShotError = $null
    $UiShot = $null
    $UiShotError = $null
    $Resolution = $null
    $PrimaryError = $null
    $RevertError = $null
    $Session = $null
    $StartedAt = Get-Date
    try {
        $Credential = Get-DsoVmCredential -VmName $VmName
        Restore-VmCleanState -VmName $VmName -CheckpointName $CheckpointName
        Start-VM -Name $VmName
        $Session = Wait-VmReady -VmName $VmName -Credential $Credential -TimeoutSeconds $ReadyTimeoutSeconds
        Copy-DsoBuildToVm -Session $Session -BuildDirectory $BuildDirectory -CatalogPath $CatalogPath -Layout $Layout
        $Since = Start-DsoGuiInUserSession -Session $Session -Layout $Layout
        # Na czystym checkpoincie (brak settings.json) pierwsze okno to wybór języka; dopiero po nim startuje okno główne.
        try { $LanguageChoice = Confirm-DsoLanguageChoice -Session $Session -Layout $Layout -Language $Language }
        catch { $LanguageError = "okno wyboru języka: $($_.Exception.Message)" }
        Start-Sleep -Seconds $StartupSeconds
        $Resolution = Get-DsoVmResolution -VmName $VmName
        try { $Shot = Save-DsoVmThumbnail -VmName $VmName -Path $ScreenshotPath -Width $Resolution.Width -Height $Resolution.Height }
        catch { $ShotError = "zrzut ekranu: $($_.Exception.Message)" }
        # Oczekiwane N dla „Prywatność i AI” (krok 7 sondy UI): CLI --detect sugerowanych pozycji tego przebiegu, przed sondą UI (sonda nic z nich nie stosuje).
        try {
            $SuggestedIds = @(Get-GuiPrivacySuggestedId -CatalogDirectory (Join-Path (Get-DsoRepoRoot) 'src\DSOptimizer.Catalog\catalog'))
            $PrivacyDetection = @(Get-DsoPrivacyDetection -Session $Session -Layout $Layout -TweakId $SuggestedIds)
            $PrivacyExpected = Get-GuiPrivacyExpectedCount -SuggestedId $SuggestedIds -Detection $PrivacyDetection
        }
        catch { $DetectionError = "detekcja sugerowanych: $($_.Exception.Message)" }
        # Oczekiwane N dla „Aplikacje” (krok 8 sondy UI): CLI --list-apps jako dso, przed sondą UI (sonda niczego nie usuwa).
        try {
            $AppsListing = Get-DsoAppsListing -Session $Session -Layout $Layout
            $AppsExpected = Get-GuiAppsExpectedCount -Listing $AppsListing
        }
        catch { $AppsListError = "lista aplikacji: $($_.Exception.Message)" }
        # Oczekiwane tytuły strony „Naprawy” (krok 10 sondy UI, na samym końcu): kategoria repair z wbudowanego katalogu repo.
        try { $RepairTitles = @(Get-GuiRepairTitle -CatalogDirectory (Join-Path (Get-DsoRepoRoot) 'src\DSOptimizer.Catalog\catalog')) }
        catch { $RepairTitlesError = "tytuły Napraw: $($_.Exception.Message)" }
        # Oczekiwania strony „Instalacja” (krok 11 sondy UI, na samym końcu): tytuły i kategorie z apps.json repo.
        try { $InstallExpected = Get-GuiInstallTitle -CatalogDirectory (Join-Path (Get-DsoRepoRoot) 'src\DSOptimizer.Catalog\catalog') }
        catch { $InstallTitlesError = "lista instalacji: $($_.Exception.Message)" }
        try { $UiProbe = Get-DsoGuiUiProbe -Session $Session -Layout $Layout }
        catch { $UiError = "sonda UI: $($_.Exception.Message)" }
        try { $UiShot = Save-DsoVmThumbnail -VmName $VmName -Path $UiScreenshotPath -Width $Resolution.Width -Height $Resolution.Height }
        catch { $UiShotError = "zrzut ekranu po sondzie UI: $($_.Exception.Message)" }
        $Probe = Get-DsoGuiProbe -Session $Session -Layout $Layout -Since $Since
    }
    catch { $PrimaryError = $_.Exception.Message }
    finally {
        if ($null -ne $Session) { Remove-PSSession -Session $Session -ErrorAction SilentlyContinue }
        if (-not $KeepRunning) {
            try { Restore-VmCleanState -VmName $VmName -CheckpointName $CheckpointName }
            catch { $RevertError = "Przywracanie checkpointu '$CheckpointName' VM '$VmName' nie powiodło się: $($_.Exception.Message). VM może nie być w czystym stanie." }
        }
    }
    $BaseChecks = Get-GuiSmokeCheck -Probe $Probe -ScreenshotPath $Shot
    $UiChecks = Get-GuiUiCheck -Probe $UiProbe -PrivacyExpected $PrivacyExpected -AppsExpected $AppsExpected -RepairTitles $RepairTitles -InstallExpected $InstallExpected
    $LanguageCheck = Get-GuiLanguageCheck -Choice $LanguageChoice -Language $Language
    $Checks = @($BaseChecks) + @($LanguageCheck) + @($UiChecks)
    $Errors = @($PrimaryError, $LanguageError, $ShotError, $DetectionError, $AppsListError, $RepairTitlesError, $InstallTitlesError, $UiError, $UiShotError, $RevertError) | Where-Object { $_ }
    $Failed = @($Checks | Where-Object { -not $_.Passed }).Count + @($Errors).Count
    $Result = [ordered]@{
        schema         = 1
        vmName         = $VmName
        passed         = ($Failed -eq 0)
        language       = $Language
        languageChoice = $LanguageChoice
        keepRunning    = [bool]$KeepRunning
        startedAt      = $StartedAt.ToString('o')
        finishedAt     = (Get-Date).ToString('o')
        screenshotPath = $Shot
        uiScreenshotPath = $UiShot
        resolution     = $Resolution
        resultPath     = $ResultPath
        checks         = @($Checks)
        errors         = @($Errors)
        probe          = $Probe
        uiProbe        = $UiProbe
        privacyDetection = $PrivacyDetection
        privacyExpected  = $PrivacyExpected
        appsListing      = $AppsListing
        appsExpected     = $AppsExpected
        repairTitles     = @($RepairTitles)
        installExpected  = $InstallExpected
    }
    [System.IO.File]::WriteAllText($ResultPath, ($Result | ConvertTo-Json -Depth 12), (New-Object System.Text.UTF8Encoding($true)))
    return [pscustomobject]$Result
}

Export-ModuleMember -Function Get-GuiSmokeProbeScript, Get-LanguageChoiceProbeScript, Confirm-DsoLanguageChoice, Get-GuiLanguageCheck, Start-DsoGuiInUserSession, Invoke-DsoUserSessionScript, Get-DsoGuiProbe, Get-DsoGuiUiProbe, Get-DsoPrivacyDetection, Get-DsoAppsListing, ConvertTo-DsoRgb565Png, Save-DsoVmThumbnail, Get-DsoVmResolution, Get-GuiSmokeCheck, Invoke-DsoGuiSmoke
