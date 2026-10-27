<#
Instrukcja uruchomienia:
    Sesja PowerShell uruchomiona jako administrator, na hoście z Hyper-V, z modułami testów VM (tests\vm\modules).
    VM utworzone wcześniej przez New-TestVm.ps1 (checkpoint 'czysta-26H2', poświadczenia w %LOCALAPPDATA%\DSOptimizer-vm).
    Obie VM, po kolei (Home, potem Pro):
        .\tests\vm\explore\Invoke-C1Exploration.ps1 -OutDirectory C:\ścieżka\do\wyników
    Jedna VM:
        .\tests\vm\explore\Invoke-C1Exploration.ps1 -VmName DSO-26H2-Pro -OutDirectory C:\ścieżka\do\wyników
    Bez -OutDirectory wynik trafia do tests\vm\out\c1-<data> (katalog out\ jest w .gitignore).
Opis działania:
    Skrypt zbiera dowody z prawdziwej VM 26H2 dla partii C1 (tweaki ux.* i disk.*). Wartości rejestru i stanu nie pochodzą
    z pamięci modelu: każdą zmianę wykonuje oficjalne narzędzie (powercfg, fsutil, cmdlety DISM), a skutek jest odczytany
    z rejestru po zmianie. Dla każdej sondy: odczyt -> zmiana -> odczyt -> przywrócenie -> odczyt.
    Sondy: hibernacja (powercfg /a, /h off, /h on, hiberfil.sys, HibernateEnabled, HibernateEnabledDefault, HiberbootEnabled),
    NTFS (disablelastaccess, 8dot3name, surowe wyjście 'fsutil ... query'), reserved storage (dostępność cmdletów w PS 5.1,
    obiekt Get-WindowsReservedStorageState, Set Disabled/Enabled, klucz ReserveManager), domyślne ux (MenuShowDelay,
    GameDVR_Enabled, AppCaptureEnabled pod HKU\<SID dso>, AllowGameDVR w HKLM Policies).
    Każdy krok zapisuje: komendę, kod wyjścia, surowe stdout/stderr, wartości rejestru jako {Exists, Kind, Value, Hex} i czas.
    Brak wartości (Exists=false) to nie to samo co wartość 0.
    Przebieg na każdej VM: przywrócenie checkpointu -> start -> Wait-VmReady -> sesja PowerShell Direct -> zbieranie, a na końcu
    ZAWSZE (finally) zatrzymanie i powrót do checkpointu. Pierwszy błąd uwierzytelnienia przerywa cały skrypt (bez prób na
    drugiej VM, blokada konta). Inny błąd jednej VM jest zapisany i nie przerywa drugiej.
    Wynik: <OutDirectory>\<VmName>\c1-exploration.json (UTF-8) z metadanymi (build, UBR, EditionID, kultura, czas).
    Hive użytkownika 'dso' musi być załadowany (Wait-VmReady czeka na jego explorer.exe); jeśli nie jest, JSON zapisuje
    HiveLoaded=false i pomija odczyty HKU (skrypt niczego nie ładuje przez 'reg load').
    Skrypt dotyka wyłącznie VM DSO-26H2-Home i DSO-26H2-Pro, na hoście niczego nie zmienia.
    Kod wyjścia: 0 gdy obie (wybrane) VM zebrane bez błędu, 1 w przeciwnym razie.
#>
[CmdletBinding()]
param(
    [ValidateSet('DSO-26H2-Home', 'DSO-26H2-Pro')][string[]]$VmName = @('DSO-26H2-Home', 'DSO-26H2-Pro'),
    [string]$OutDirectory,
    [string]$CheckpointName = 'czysta-26H2',
    [ValidateRange(60, 7200)][int]$ReadyTimeoutSeconds = 1200
)

# Wspólna orkiestracja i narzędzia odczytu (rejestr, program natywny, rekord kroku) są w ExploreCommon.psm1.
Import-Module (Join-Path $PSScriptRoot 'ExploreCommon.psm1') -Force

# ---------------------------------------------------------------------------------------------------------------------
# Funkcje wysyłane do VM (samowystarczalne: wołają tylko siebie nawzajem, funkcje Explore z modułu i cmdlety systemowe).
# Lista w Get-C1RemoteFunctionNames (część wspólna: Get-ExploreCommonRemoteFunctionNames).
# ---------------------------------------------------------------------------------------------------------------------

function Get-C1HiberfilInfo {
    [CmdletBinding()]
    param()
    $Path = 'C:\hiberfil.sys'
    $Info = [ordered]@{ Path = $Path; Exists = $false; SizeBytes = $null; Attributes = $null }
    $Item = $null
    try { $Item = Get-Item -LiteralPath $Path -Force -ErrorAction Stop }
    catch { if (-not ($_.Exception -is [System.Management.Automation.ItemNotFoundException])) { throw } }
    if ($null -ne $Item) {
        $Info.Exists = $true
        $Info.SizeBytes = [long]$Item.Length
        $Info.Attributes = [string]$Item.Attributes
    }
    return $Info
}

function Invoke-C1HibernationProbe {
    [CmdletBinding()]
    param()
    $PowerKey = 'HKLM\SYSTEM\CurrentControlSet\Control\Power'
    $SessionPowerKey = 'HKLM\SYSTEM\CurrentControlSet\Control\Session Manager\Power'
    $Regs = @(
        @{ Path = $PowerKey; Name = 'HibernateEnabled' }
        @{ Path = $PowerKey; Name = 'HibernateEnabledDefault' }
        @{ Path = $PowerKey; Name = 'HiberFileSizePercent' }
        @{ Path = $SessionPowerKey; Name = 'HiberbootEnabled' }
        @{ Path = $SessionPowerKey; Name = 'HibernateEnabled' }
    )
    # Stan ze startu (przed jakąkolwiek zmianą): do niego wracamy i z nim porównujemy na końcu.
    $BaseRegs = @($Regs | ForEach-Object { Read-ExploreRegistryValue -Path $_.Path -Name $_.Name })
    $BaseFile = Get-C1HiberfilInfo
    $WholeKeys = @($PowerKey, $SessionPowerKey)
    $WithFile = { @{ Hiberfil = Get-C1HiberfilInfo } }
    Invoke-ExploreStep -Name 'baseline-powercfg-a' -Command 'powercfg.exe /a' -Registry $Regs -RegistryKeys $WholeKeys -After $WithFile -Action { Invoke-ExploreNative -FilePath 'powercfg.exe' -Arguments @('/a') }
    Invoke-ExploreStep -Name 'powercfg-h-off' -Command 'powercfg.exe /h off' -Registry $Regs -RegistryKeys $WholeKeys -After $WithFile -Action { Invoke-ExploreNative -FilePath 'powercfg.exe' -Arguments @('/h', 'off') }
    Invoke-ExploreStep -Name 'po-off-powercfg-a' -Command 'powercfg.exe /a' -Registry $Regs -RegistryKeys $WholeKeys -After $WithFile -Action { Invoke-ExploreNative -FilePath 'powercfg.exe' -Arguments @('/a') }
    Invoke-ExploreStep -Name 'powercfg-h-on' -Command 'powercfg.exe /h on' -Registry $Regs -RegistryKeys $WholeKeys -After $WithFile -Action { Invoke-ExploreNative -FilePath 'powercfg.exe' -Arguments @('/h', 'on') }
    Invoke-ExploreStep -Name 'po-on-powercfg-a' -Command 'powercfg.exe /a' -Registry $Regs -RegistryKeys $WholeKeys -After $WithFile -Action { Invoke-ExploreNative -FilePath 'powercfg.exe' -Arguments @('/a') }
    # Przywrócenie stanu startowego: jeśli hibernacja była startowo wyłączona (HibernateEnabled = 0 odczytane na starcie),
    # '/h on' było zmianą, nie przywróceniem, więc wracamy '/h off'. Brak wartości lub inna wartość = nie zgadujemy.
    if ($BaseRegs[0].Exists -and $BaseRegs[0].Value -eq 0) {
        Invoke-ExploreStep -Name 'przywroc-powercfg-h-off' -Command 'powercfg.exe /h off' -Registry $Regs -RegistryKeys $WholeKeys -After $WithFile -Action { Invoke-ExploreNative -FilePath 'powercfg.exe' -Arguments @('/h', 'off') }
    }
    Invoke-ExploreStep -Name 'porownanie-z-baseline' -Command '(odczyt i porównanie ze stanem startowym)' -Registry $Regs -After {
        $NowRegs = @($Regs | ForEach-Object { Read-ExploreRegistryValue -Path $_.Path -Name $_.Name })
        $Diff = @(Compare-ExploreRegistry -Baseline $BaseRegs -Current $NowRegs)
        $NowFile = Get-C1HiberfilInfo
        if ($NowFile.Exists -ne $BaseFile.Exists) { $Diff += "Hiberfil.Exists: $($BaseFile.Exists) -> $($NowFile.Exists)" }
        elseif ($NowFile.Exists -and $NowFile.SizeBytes -ne $BaseFile.SizeBytes) { $Diff += "Hiberfil.SizeBytes: $($BaseFile.SizeBytes) -> $($NowFile.SizeBytes)" }
        @{ MatchesBaseline = ($Diff.Count -eq 0); Differences = @($Diff); BaselineHiberfil = $BaseFile; Hiberfil = $NowFile }
    }
}

function Compare-C1ReservedState {
    # Różnice (nazwa i wartość właściwości) między dwoma obiektami z Get-C1ReservedStorageObject.
    [CmdletBinding()]
    param(
        [Parameter(Mandatory)]$Baseline,
        [Parameter(Mandatory)]$Current
    )
    $Differences = @()
    if (-not $Baseline.Ok -or -not $Current.Ok) { return @("odczyt stanu nie powiódł się (baseline Ok=$($Baseline.Ok), bieżący Ok=$($Current.Ok))") }
    $BaseProps = @($Baseline.Properties)
    $NowProps = @($Current.Properties)
    foreach ($Prop in $BaseProps) {
        $Match = @($NowProps | Where-Object { $_.Name -eq $Prop.Name })[0]
        if ($null -eq $Match) { $Differences += "$($Prop.Name): brak właściwości w odczycie końcowym" }
        elseif ([string]$Match.Value -ne [string]$Prop.Value) { $Differences += "$($Prop.Name): $($Prop.Value) -> $($Match.Value)" }
    }
    foreach ($Prop in $NowProps) {
        if (-not (@($BaseProps | Where-Object { $_.Name -eq $Prop.Name }))) { $Differences += "$($Prop.Name): nowa właściwość $($Prop.Value)" }
    }
    return $Differences
}

function Get-C1ReservedRestoreValue {
    # Wartość do przywrócenia pochodzi z odczytu startowego: dokładnie jedna właściwość-enum, której wartość jest w EnumNames.
    # Zwraca $null, gdy nie da się tego ustalić bez zgadywania.
    [CmdletBinding()]
    param([Parameter(Mandatory)]$Baseline)
    if (-not $Baseline.Ok) { return $null }
    $Candidates = @(@($Baseline.Properties) | Where-Object { $_.IsEnum -and $null -ne $_.Value -and (@($_.EnumNames) -contains $_.Value) })
    if ($Candidates.Count -eq 1) { return [string]$Candidates[0].Value }
    return $null
}

function Invoke-C1NtfsProbe {
    [CmdletBinding()]
    param()
    $FsKey = 'HKLM\SYSTEM\CurrentControlSet\Control\FileSystem'
    $Regs = @(
        @{ Path = $FsKey; Name = 'NtfsDisableLastAccessUpdate' }
        @{ Path = $FsKey; Name = 'NtfsDisable8dot3NameCreation' }
    )
    # Pierwotne wartości odczytane na starcie: tylko do nich wracamy (nic nie zgadujemy).
    $BaseLast = Read-ExploreRegistryValue -Path $FsKey -Name 'NtfsDisableLastAccessUpdate'
    $Base8dot3 = Read-ExploreRegistryValue -Path $FsKey -Name 'NtfsDisable8dot3NameCreation'

    Invoke-ExploreStep -Name 'baseline-rejestr' -Command '(odczyt rejestru, bez zmian)' -Registry $Regs
    Invoke-ExploreStep -Name 'query-disablelastaccess' -Command 'fsutil.exe behavior query disablelastaccess' -Registry $Regs -Action { Invoke-ExploreNative -FilePath 'fsutil.exe' -Arguments @('behavior', 'query', 'disablelastaccess') }
    Invoke-ExploreStep -Name 'query-disable8dot3' -Command 'fsutil.exe behavior query disable8dot3' -Registry $Regs -Action { Invoke-ExploreNative -FilePath 'fsutil.exe' -Arguments @('behavior', 'query', 'disable8dot3') }
    Invoke-ExploreStep -Name 'query-8dot3name-C' -Command 'fsutil.exe 8dot3name query C:' -Registry $Regs -Action { Invoke-ExploreNative -FilePath 'fsutil.exe' -Arguments @('8dot3name', 'query', 'C:') }

    Invoke-ExploreStep -Name 'set-disablelastaccess-1' -Command 'fsutil.exe behavior set disablelastaccess 1' -Registry $Regs -Action { Invoke-ExploreNative -FilePath 'fsutil.exe' -Arguments @('behavior', 'set', 'disablelastaccess', '1') }
    Invoke-ExploreStep -Name 'set-disablelastaccess-0' -Command 'fsutil.exe behavior set disablelastaccess 0' -Registry $Regs -Action { Invoke-ExploreNative -FilePath 'fsutil.exe' -Arguments @('behavior', 'set', 'disablelastaccess', '0') }

    # Argument przywrócenia wyprowadzony z wartości rejestru odczytanej na starcie: DWORD z flagą 0x80000000 -> dolne 2 bity
    # (wg źródeł hint: tryb system managed), zwykłe 0..3 -> bez zmian. Zgodność z pierwotnym stanem sprawdza pole MatchesBaseline.
    $LastArgument = $null
    $LastDerivation = $null
    if ($BaseLast.Exists -and $BaseLast.Kind -eq 'DWord') {
        $Raw = [int]$BaseLast.Value
        if ($Raw -ge 0 -and $Raw -le 3) { $LastArgument = $Raw; $LastDerivation = 'wartość rejestru 0..3 użyta wprost' }
        elseif ($Raw -lt 0 -and (($Raw -band 0x7FFFFFFC) -eq 0)) { $LastArgument = ($Raw -band 3); $LastDerivation = 'flaga 0x80000000 odjęta, dolne 2 bity (wnioskowanie ze źródeł hint, weryfikuje MatchesBaseline)' }
    }
    $LastCompare = { $Now = Read-ExploreRegistryValue -Path 'HKLM\SYSTEM\CurrentControlSet\Control\FileSystem' -Name 'NtfsDisableLastAccessUpdate'; ($Now.Exists -eq $BaseLast.Exists) -and ($Now.Value -eq $BaseLast.Value) }
    if ($null -ne $LastArgument) {
        $Argument = [string]$LastArgument
        Invoke-ExploreStep -Name 'przywroc-disablelastaccess' -Command "fsutil.exe behavior set disablelastaccess $Argument" -Registry $Regs `
            -Action { Invoke-ExploreNative -FilePath 'fsutil.exe' -Arguments @('behavior', 'set', 'disablelastaccess', $Argument) } `
            -After { @{ RestoreArgument = $LastArgument; Derivation = $LastDerivation; MatchesBaseline = [bool](& $LastCompare) } }
    }
    else {
        Invoke-ExploreStep -Name 'przywroc-disablelastaccess' -Command '(pominięte: brak użytecznej wartości pierwotnej)' -Registry $Regs `
            -After { @{ RestoreSkipped = $true; Reason = 'NtfsDisableLastAccessUpdate nie istniała na starcie albo ma nieoczekiwany typ/wartość; nie zgaduję przywrócenia.' } }
    }

    Invoke-ExploreStep -Name 'set-8dot3name-1' -Command 'fsutil.exe 8dot3name set 1' -Registry $Regs -Action { Invoke-ExploreNative -FilePath 'fsutil.exe' -Arguments @('8dot3name', 'set', '1') }

    $Argument8 = $null
    if ($Base8dot3.Exists -and $Base8dot3.Kind -eq 'DWord' -and [int]$Base8dot3.Value -ge 0 -and [int]$Base8dot3.Value -le 3) { $Argument8 = [string][int]$Base8dot3.Value }
    $Compare8 = { $Now = Read-ExploreRegistryValue -Path 'HKLM\SYSTEM\CurrentControlSet\Control\FileSystem' -Name 'NtfsDisable8dot3NameCreation'; ($Now.Exists -eq $Base8dot3.Exists) -and ($Now.Value -eq $Base8dot3.Value) }
    if ($null -ne $Argument8) {
        Invoke-ExploreStep -Name 'przywroc-8dot3name' -Command "fsutil.exe 8dot3name set $Argument8" -Registry $Regs `
            -Action { Invoke-ExploreNative -FilePath 'fsutil.exe' -Arguments @('8dot3name', 'set', $Argument8) } `
            -After { @{ RestoreArgument = [int]$Argument8; MatchesBaseline = [bool](& $Compare8) } }
    }
    else {
        Invoke-ExploreStep -Name 'przywroc-8dot3name' -Command '(pominięte: brak użytecznej wartości pierwotnej)' -Registry $Regs `
            -After { @{ RestoreSkipped = $true; Reason = 'NtfsDisable8dot3NameCreation nie istniała na starcie albo ma nieoczekiwany typ/wartość; nie zgaduję przywrócenia.' } }
    }
}

function Get-C1CommandInfo {
    [CmdletBinding()]
    param([Parameter(Mandatory)][string]$Name)
    $Command = @(Get-Command -Name $Name -ErrorAction SilentlyContinue) | Select-Object -First 1
    $Info = [ordered]@{ Name = $Name; Available = ($null -ne $Command); CommandType = $null; Source = $null; Version = $null; Parameters = @() }
    if ($null -ne $Command) {
        $Info.CommandType = [string]$Command.CommandType
        $Info.Source = [string]$Command.Source
        $Info.Version = [string]$Command.Version
        $Info.Parameters = @($Command.Parameters.Keys)
    }
    return $Info
}

function Get-C1ReservedStorageObject {
    # Obiekt z Get-WindowsReservedStorageState: nazwy, typy i wartości właściwości (w tym typ enuma i jego nazwy).
    [CmdletBinding()]
    param()
    $Result = [ordered]@{ Ok = $false; Error = $null; ObjectType = $null; IsEnum = $false; EnumNames = @(); Text = $null; Properties = @() }
    try {
        $Object = Get-WindowsReservedStorageState -ErrorAction Stop
        $Result.Ok = $true
        $Result.Text = (($Object | Out-String).Trim())
        if ($null -ne $Object) {
            $First = @($Object)[0]
            $Result.ObjectType = $First.GetType().FullName
            if ($First -is [enum]) {
                $Result.IsEnum = $true
                $Result.EnumNames = @([enum]::GetNames($First.GetType()))
            }
            $Properties = @()
            foreach ($Property in $First.PSObject.Properties) {
                $PropertyValue = $Property.Value
                $TypeName = if ($null -ne $PropertyValue) { $PropertyValue.GetType().FullName } else { $null }
                $EnumNames = @()
                $IsEnum = ($null -ne $PropertyValue) -and ($PropertyValue -is [enum])
                if ($IsEnum) { $EnumNames = @([enum]::GetNames($PropertyValue.GetType())) }
                $Properties += [pscustomobject]@{ Name = [string]$Property.Name; TypeName = $TypeName; Value = if ($null -ne $PropertyValue) { [string]$PropertyValue } else { $null }; IsEnum = $IsEnum; EnumNames = $EnumNames }
            }
            $Result.Properties = @($Properties)
        }
    }
    catch { $Result.Error = $_.Exception.Message }
    return $Result
}

function Invoke-C1ReservedStorageProbe {
    [CmdletBinding()]
    param()
    $ReserveKey = 'HKLM\SOFTWARE\Microsoft\Windows\CurrentVersion\ReserveManager'
    $GetInfo = Get-C1CommandInfo -Name 'Get-WindowsReservedStorageState'
    $SetInfo = Get-C1CommandInfo -Name 'Set-WindowsReservedStorageState'
    Invoke-ExploreStep -Name 'dostepnosc-cmdletow' -Command 'Get-Command Get-WindowsReservedStorageState, Set-WindowsReservedStorageState' -RegistryKeys @($ReserveKey) `
        -After { @{ Get = $GetInfo; Set = $SetInfo; ReserveManager = $ReserveKey; PSVersion = [string]$PSVersionTable.PSVersion } }
    # Tylko odczyt: surowe wyjście DISM przed zmianami (czy jest tłumaczone na język systemu).
    Invoke-ExploreStep -Name 'dism-get-przed' -Command 'DISM.exe /Online /Get-ReservedStorageState' -Action { Invoke-ExploreNative -FilePath 'DISM.exe' -Arguments @('/Online', '/Get-ReservedStorageState') }
    if ($GetInfo.Available -and $SetInfo.Available) {
        $BaseState = Get-C1ReservedStorageObject
        $RestoreValue = Get-C1ReservedRestoreValue -Baseline $BaseState
        $Show = { @{ State = (Get-C1ReservedStorageObject) } }
        Invoke-ExploreStep -Name 'get-baseline' -Command 'Get-WindowsReservedStorageState' -RegistryKeys @($ReserveKey) -After $Show
        foreach ($Target in @('Disabled', 'Enabled')) {
            $StateValue = $Target
            Invoke-ExploreStep -Name "set-$Target" -Command "Set-WindowsReservedStorageState -State $Target" -RegistryKeys @($ReserveKey) -After $Show -Action {
                if ($SetInfo.Parameters -notcontains 'State') { throw "Set-WindowsReservedStorageState nie ma parametru State; parametry: $($SetInfo.Parameters -join ', ')" }
                $Text = (Set-WindowsReservedStorageState -State $StateValue -ErrorAction Stop *>&1 | Out-String)
                [pscustomobject]@{ ExitCode = $null; Stdout = $Text; Stderr = $null; Error = $null }
            }
        }
        # Przywrócenie stanu startowego: -State wartością odczytaną w baseline (tylko gdy bieżący stan się różni).
        $RestoreCommand = if ($RestoreValue) { "Set-WindowsReservedStorageState -State $RestoreValue (gdy stan różni się od startowego)" } else { '(brak jednoznacznej wartości startowej do przywrócenia)' }
        Invoke-ExploreStep -Name 'przywroc-stan-startowy' -Command $RestoreCommand -RegistryKeys @($ReserveKey) -Action {
            $CurrentState = Get-C1ReservedStorageObject
            $Pending = @(Compare-C1ReservedState -Baseline $BaseState -Current $CurrentState)
            $Text = 'bez zmian: stan zgodny ze startowym albo brak wartości do przywrócenia'
            if ($Pending.Count -gt 0 -and $RestoreValue) {
                if ($SetInfo.Parameters -notcontains 'State') { throw "Set-WindowsReservedStorageState nie ma parametru State; parametry: $($SetInfo.Parameters -join ', ')" }
                $Text = (Set-WindowsReservedStorageState -State $RestoreValue -ErrorAction Stop *>&1 | Out-String)
            }
            [pscustomobject]@{ ExitCode = $null; Stdout = $Text; Stderr = $null; Error = $null }
        } -After {
            $FinalState = Get-C1ReservedStorageObject
            $FinalDiff = @(Compare-C1ReservedState -Baseline $BaseState -Current $FinalState)
            @{ RestoreValue = $RestoreValue; MatchesBaseline = ($FinalDiff.Count -eq 0); Differences = @($FinalDiff); State = $FinalState }
        }
    }
    Invoke-ExploreStep -Name 'dism-get-po' -Command 'DISM.exe /Online /Get-ReservedStorageState' -RegistryKeys @($ReserveKey) -Action { Invoke-ExploreNative -FilePath 'DISM.exe' -Arguments @('/Online', '/Get-ReservedStorageState') }
}

function Invoke-C1UxProbe {
    # Domyślne wartości ux (tylko odczyt). Hive użytkownika dso musi być załadowany; inaczej HKU jest pomijane i jest to zapisane.
    [CmdletBinding()]
    param(
        [Parameter(Mandatory)][string]$UserSid,
        [Parameter(Mandatory)][bool]$HiveLoaded
    )
    $Regs = @(@{ Path = 'HKLM\SOFTWARE\Policies\Microsoft\Windows\GameDVR'; Name = 'AllowGameDVR' })
    if ($HiveLoaded) {
        $Regs += @{ Path = "HKU\$UserSid\Control Panel\Desktop"; Name = 'MenuShowDelay' }
        $Regs += @{ Path = "HKU\$UserSid\System\GameConfigStore"; Name = 'GameDVR_Enabled' }
        $Regs += @{ Path = "HKU\$UserSid\Software\Microsoft\Windows\CurrentVersion\GameDVR"; Name = 'AppCaptureEnabled' }
    }
    Invoke-ExploreStep -Name 'odczyt-domyslnych-ux' -Command '(odczyt rejestru, bez zmian)' -Registry $Regs `
        -After { @{ HiveLoaded = $HiveLoaded; UserSid = $UserSid; Note = $(if ($HiveLoaded) { $null } else { 'hive niezaładowany - odczyty HKU pominięte' }) } }
}

function Invoke-C1Collection {
    # Punkt wejścia po stronie VM: zwraca JEDEN string JSON (meta + sondy).
    [CmdletBinding()]
    param(
        [Parameter(Mandatory)][string]$UserSid
    )
    $HiveLoaded = [bool](Test-Path -LiteralPath "Registry::HKEY_USERS\$UserSid")
    $Probes = [ordered]@{
        hibernation     = { Invoke-C1HibernationProbe }
        ntfs            = { Invoke-C1NtfsProbe }
        reservedStorage = { Invoke-C1ReservedStorageProbe }
        ux              = { Invoke-C1UxProbe -UserSid $UserSid -HiveLoaded $HiveLoaded }
    }
    return (Invoke-ExploreCollection -Probe $Probes -Extra ([ordered]@{ userSid = $UserSid; hiveLoaded = $HiveLoaded }))
}

# ---------------------------------------------------------------------------------------------------------------------
# Strona hosta.
# ---------------------------------------------------------------------------------------------------------------------

function Get-C1RemoteFunctionNames {
    return @(
        'Get-C1HiberfilInfo', 'Invoke-C1HibernationProbe', 'Invoke-C1NtfsProbe', 'Get-C1CommandInfo', 'Get-C1ReservedStorageObject',
        'Invoke-C1ReservedStorageProbe', 'Compare-C1ReservedState', 'Get-C1ReservedRestoreValue', 'Invoke-C1UxProbe', 'Invoke-C1Collection'
    )
}

function Get-C1RemoteScript {
    # Tekst definicji funkcji zdalnych: część wspólna z modułu + funkcje C1 (FunctionInfo pobrane tu, w zasięgu skryptu).
    [CmdletBinding()]
    [OutputType([string])]
    param()
    $Names = @(Get-ExploreCommonRemoteFunctionNames) + @(Get-C1RemoteFunctionNames)
    return (ConvertTo-ExploreRemoteScript -Command @($Names | ForEach-Object { Get-Command -Name $_ -CommandType Function -ErrorAction Stop }))
}

function Invoke-C1VmExploration {
    # Jedna VM; zwraca ścieżkę JSON (c1-exploration.json). Orkiestracja we wspólnym module.
    [CmdletBinding()]
    [OutputType([string])]
    param(
        [Parameter(Mandatory)][ValidateSet('DSO-26H2-Home', 'DSO-26H2-Pro')][string]$VmName,
        [Parameter(Mandatory)][string]$OutDirectory,
        [string]$CheckpointName = 'czysta-26H2',
        [int]$ReadyTimeoutSeconds = 1200
    )
    return (Invoke-ExploreVmRun -VmName $VmName -OutDirectory $OutDirectory -FileName 'c1-exploration.json' -RemoteScript (Get-C1RemoteScript) `
            -EntryCommand 'Invoke-C1Collection' -PassUserSid -CheckpointName $CheckpointName -ReadyTimeoutSeconds $ReadyTimeoutSeconds)
}

function Invoke-C1Exploration {
    # Wszystkie wybrane VM po kolei. Błąd uwierzytelnienia = stop całości; inny błąd = zapis i dalej (logika we wspólnym module).
    [CmdletBinding()]
    param(
        [Parameter(Mandatory)][ValidateSet('DSO-26H2-Home', 'DSO-26H2-Pro')][string[]]$VmName,
        [Parameter(Mandatory)][string]$OutDirectory,
        [string]$CheckpointName = 'czysta-26H2',
        [int]$ReadyTimeoutSeconds = 1200
    )
    return (Invoke-ExploreAll -VmName $VmName -OutDirectory $OutDirectory -FileName 'c1-exploration.json' -RemoteScript (Get-C1RemoteScript) `
            -EntryCommand 'Invoke-C1Collection' -PassUserSid -CheckpointName $CheckpointName -ReadyTimeoutSeconds $ReadyTimeoutSeconds)
}

# Przy dot-sourcingu (testy) tylko definicje; uruchomienie przebiegu tylko przy zwykłym wywołaniu skryptu.
if ($MyInvocation.InvocationName -ne '.') {
    $ExitCode = 1
    try {
        Set-StrictMode -Version Latest
        $ErrorActionPreference = 'Stop'
        $VmRoot = Split-Path -Parent $PSScriptRoot
        Import-Module (Join-Path $VmRoot 'modules\VmCommon.psm1') -Force
        Import-Module (Join-Path $VmRoot 'modules\VmRun.psm1') -Force
        Import-Module (Join-Path $VmRoot 'modules\VmHost.psm1') -Force
        # ExploreCommon (zaimportowany wyżej, przed tymi modułami) trzyma referencję do starej instancji VmHost; ponowny import po nich wiąże go z bieżącą.
        Import-Module (Join-Path $PSScriptRoot 'ExploreCommon.psm1') -Force
        $Principal = New-Object System.Security.Principal.WindowsPrincipal([System.Security.Principal.WindowsIdentity]::GetCurrent())
        if (-not $Principal.IsInRole([System.Security.Principal.WindowsBuiltInRole]::Administrator)) {
            throw 'Uruchom skrypt w sesji PowerShell jako administrator (Hyper-V i PowerShell Direct tego wymagają).'
        }
        if (-not $OutDirectory) { $OutDirectory = Join-Path $VmRoot ('out\c1-' + (Get-Date -Format 'yyyyMMdd-HHmmss')) }
        $null = New-Item -ItemType Directory -Path $OutDirectory -Force
        Write-Host "Wyniki: $OutDirectory"
        $Outcome = Invoke-C1Exploration -VmName $VmName -OutDirectory $OutDirectory -CheckpointName $CheckpointName -ReadyTimeoutSeconds $ReadyTimeoutSeconds
        foreach ($Path in $Outcome.Paths) { Write-Host "Zapisano: $Path" }
        foreach ($Message in $Outcome.Errors) { Write-Host "BŁĄD $Message" -ForegroundColor Red }
        $ExitCode = if (@($Outcome.Errors).Count -eq 0 -and @($Outcome.Paths).Count -eq @($VmName).Count) { 0 } else { 1 }
    }
    catch {
        Write-Host "BŁĄD: $($_.Exception.Message)" -ForegroundColor Red
        Write-Host $_.ScriptStackTrace -ForegroundColor DarkGray
        $ExitCode = 1
    }
    exit $ExitCode
}
