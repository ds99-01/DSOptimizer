<#
Instrukcja uruchomienia:
    Sesja PowerShell uruchomiona jako administrator, na hoście z Hyper-V, z modułami testów VM (tests\vm\modules) i tests\vm\explore\ExploreCommon.psm1.
    VM utworzone wcześniej przez New-TestVm.ps1 (checkpoint 'czysta-26H2', poświadczenia w %LOCALAPPDATA%\DSOptimizer-vm).
    Obie VM, po kolei (Home, potem Pro):
        .\tests\vm\explore\Invoke-C3Exploration.ps1 -OutDirectory C:\ścieżka\do\wyników
    Jedna VM:
        .\tests\vm\explore\Invoke-C3Exploration.ps1 -VmName DSO-26H2-Pro -OutDirectory C:\ścieżka\do\wyników
    Bez -OutDirectory wynik trafia do tests\vm\out\c3-<data> (katalog out\ jest w .gitignore).
Opis działania:
    Skrypt zbiera dowody z prawdziwej VM 26H2 dla partii C3 (pozycje net.*). Zapisuje FAKTY (surowe wartości), bez interpretacji.
    Zmianę w VM wykonuje wyłącznie oficjalny cmdlet MS (Set-SmbClientConfiguration), a skutek jest odczytany po zmianie.
    Sondy (numery = otwarte pytania w docs\zrodla\2026-10-18-partia-c3-zrodla.md):
      systemProfile  (1) - tylko odczyt: zrzut HKLM\SOFTWARE\Microsoft\Windows NT\CurrentVersion\Multimedia\SystemProfile
                           (jawnie NetworkThrottlingIndex, SystemResponsiveness).
      ipv6           (2) - tylko odczyt: zrzut HKLM\SYSTEM\CurrentControlSet\Services\Tcpip6\Parameters (jawnie DisabledComponents),
                           Get-NetPrefixPolicy, Get-NetAdapterBinding -ComponentID ms_tcpip6. Bez zapisu.
      smbClient    (3,4) - Get-SmbClientConfiguration (EnableInsecureGuestLogons, RequireSecuritySignature, EnableSecuritySignature,
                           AuditInsecureGuestLogon) + zrzut HKLM\SYSTEM\CurrentControlSet\Services\LanmanWorkstation\Parameters
                           i HKLM\SOFTWARE\Policies\Microsoft\Windows\LanmanWorkstation (jawnie AllowInsecureGuestAuth i RequireSecuritySignature).
                           Sekwencja: odczyt -> Set-SmbClientConfiguration -EnableInsecureGuestLogons <przeciwna do startowej> -Force -> odczyt ->
                           -RequireSecuritySignature <przeciwna do startowej> -Force -> odczyt -> przywrócenie wartości z odczytu STARTOWEGO (cmdletem) -> odczyt
                           + porównanie z odczytem startowym (MatchesInitial, Differences). Gdy odczytu startowego nie ma albo wartości
                           nie są logiczne, nic nie jest zmieniane. Nieudane przywrócenie jest zapisane w kroku (Error), nie połykane.
      services       (5) - tylko odczyt: Win32_Service (StartMode, State, DelayedAutoStart) dla fdPHost, FDResPub, SSDPSRV, upnphost,
                           lmhosts, LanmanWorkstation, LanmanServer.
      networkProfile (6) - tylko odczyt: Get-NetConnectionProfile (liczba, błąd albo pusto jako dane; NetworkCategory i connectivity
                           jako [string] i [int]), Get-NetAdapter, CIM root\StandardCimv2 MSFT_NetConnectionProfile.
    Metadane: CurrentBuild, UBR, EditionID, DisplayVersion, ProductName, Culture/UICulture, PSVersion.
    Każdy krok zapisuje: komendę, kod wyjścia, surowe stdout/stderr, wartości rejestru jako {Exists, Kind, Value, Hex} i czas.
    Brak wartości (Exists=false) to nie to samo co wartość 0.
    Przebieg na każdej VM (moduł ExploreCommon): przywrócenie checkpointu -> start -> Wait-VmReady -> sesja PowerShell Direct ->
    zbieranie, a na końcu ZAWSZE (finally) zatrzymanie i powrót do checkpointu. Pierwszy błąd uwierzytelnienia przerywa cały skrypt
    (bez prób na drugiej VM, blokada konta). Inny błąd jednej VM jest zapisany i nie przerywa drugiej.
    Wynik: <OutDirectory>\<VmName>\c3-exploration.json (UTF-8).
    Skrypt dotyka wyłącznie VM DSO-26H2-Home i DSO-26H2-Pro (ValidateSet); wszystkie zmiany są w skryptach wykonywanych WEWNĄTRZ VM,
    na hoście niczego nie zmienia.
    Kod wyjścia: 0 gdy wszystkie wybrane VM zebrane bez błędu, 1 w przeciwnym razie.
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
# Lista w Get-C3RemoteFunctionNames (część wspólna: Get-ExploreCommonRemoteFunctionNames).
# Uwaga: bloki -Action/-After kroków widzą zmienne funkcji sondy przez zasięg dynamiczny; nie nazywaj w nich zmiennych jak
# zmienne Invoke-ExploreStep (Name, Command, Record, Errors, Values, Keys, Output, Last, Spec).
# ---------------------------------------------------------------------------------------------------------------------

function Get-C3EnumPair {
    # Wartość enuma/liczby jako [string] i [int] (Int=$null, gdy nie da się rzutować). Nic nie jest interpretowane.
    [CmdletBinding()]
    param($Value)
    $Pair = [ordered]@{ String = $null; Int = $null }
    if ($null -eq $Value) { return [pscustomobject]$Pair }
    $Pair.String = [string]$Value
    try { $Pair.Int = [int]$Value } catch { $Pair.Int = $null }
    return [pscustomobject]$Pair
}

function Invoke-C3Read {
    # Odczyt przez cmdlet: wynik albo błąd jako DANE (Ok, Error, Count, Items). Pusty wynik to Ok=true, Count=0.
    [CmdletBinding()]
    param([Parameter(Mandatory)][scriptblock]$Script)
    $Result = [ordered]@{ Ok = $false; Error = $null; Count = 0; Items = @() }
    try {
        $Found = @(& $Script)
        $Result.Items = @($Found)
        $Result.Count = $Found.Count
        $Result.Ok = $true
    }
    catch { $Result.Error = $_.Exception.Message }
    return $Result
}

function Get-C3SmbConfig {
    # Cztery właściwości Get-SmbClientConfiguration jako wartości surowe (brak właściwości = $null). Błąd odczytu = Ok=false.
    [CmdletBinding()]
    param()
    $Result = [ordered]@{ Ok = $false; Error = $null; Values = $null }
    try {
        $Config = Get-SmbClientConfiguration -ErrorAction Stop
        $Found = [ordered]@{}
        foreach ($Property in 'EnableInsecureGuestLogons', 'RequireSecuritySignature', 'EnableSecuritySignature', 'AuditInsecureGuestLogon') {
            $Entry = $Config.PSObject.Properties[$Property]
            $Found[$Property] = if ($null -ne $Entry) { $Entry.Value } else { $null }
        }
        $Result.Values = [pscustomobject]$Found
        $Result.Ok = $true
    }
    catch { $Result.Error = $_.Exception.Message }
    return $Result
}

function Compare-C3SmbConfig {
    # Różnice między dwoma wynikami Get-C3SmbConfig (wartości jako tekst).
    [CmdletBinding()]
    param(
        [Parameter(Mandatory)]$Baseline,
        [Parameter(Mandatory)]$Current
    )
    if (-not $Baseline.Ok -or -not $Current.Ok) { return @("odczyt SMB: Ok $($Baseline.Ok) -> $($Current.Ok)") }
    $Differences = @()
    foreach ($Property in $Baseline.Values.PSObject.Properties.Name) {
        $Before = $Baseline.Values.$Property
        $After = $Current.Values.$Property
        if ([string]$Before -ne [string]$After) { $Differences += "${Property}: $Before -> $After" }
    }
    return $Differences
}

function Invoke-C3SystemProfileProbe {
    # (1) Tylko odczyt: klucz SystemProfile (zrzut) i jawnie dwie wartości.
    [CmdletBinding()]
    param()
    $Key = 'HKLM\SOFTWARE\Microsoft\Windows NT\CurrentVersion\Multimedia\SystemProfile'
    $Regs = @(
        @{ Path = $Key; Name = 'NetworkThrottlingIndex' }
        @{ Path = $Key; Name = 'SystemResponsiveness' }
    )
    Invoke-ExploreStep -Name 'systemprofile-zrzut' -Command '(odczyt rejestru, bez zmian)' -Registry $Regs -RegistryKeys @($Key)
}

function Invoke-C3Ipv6Probe {
    # (2) Tylko odczyt: DisabledComponents + zrzut klucza, polityka prefiksów, wiązanie ms_tcpip6. Bez zapisu.
    [CmdletBinding()]
    param()
    $Key = 'HKLM\SYSTEM\CurrentControlSet\Services\Tcpip6\Parameters'
    $Regs = @(@{ Path = $Key; Name = 'DisabledComponents' })
    Invoke-ExploreStep -Name 'tcpip6-rejestr' -Command "(odczyt rejestru: $Key)" -Registry $Regs -RegistryKeys @($Key)
    Invoke-ExploreStep -Name 'get-netprefixpolicy' -Command 'Get-NetPrefixPolicy' -After {
        @{ NetPrefixPolicy = (Invoke-C3Read -Script {
                    Get-NetPrefixPolicy -ErrorAction Stop | ForEach-Object { [pscustomobject]@{ Prefix = [string]$_.Prefix; Precedence = $_.Precedence; Label = $_.Label; PolicyStore = [string]$_.PolicyStore } }
                }) }
    }
    Invoke-ExploreStep -Name 'get-netadapterbinding-ms-tcpip6' -Command 'Get-NetAdapterBinding -ComponentID ms_tcpip6' -After {
        @{ NetAdapterBinding = (Invoke-C3Read -Script {
                    Get-NetAdapterBinding -ComponentID ms_tcpip6 -ErrorAction Stop | ForEach-Object { [pscustomobject]@{ Name = [string]$_.Name; DisplayName = [string]$_.DisplayName; ComponentID = [string]$_.ComponentID; Enabled = $_.Enabled } }
                }) }
    }
}

function Invoke-C3SmbProbe {
    # (3)+(4) Jedyna sonda ze zmianą (oficjalny cmdlet MS), wyłącznie wewnątrz VM. Przywrócenie z odczytu STARTOWEGO.
    [CmdletBinding()]
    param()
    $ParametersKey = 'HKLM\SYSTEM\CurrentControlSet\Services\LanmanWorkstation\Parameters'
    $PolicyKey = 'HKLM\SOFTWARE\Policies\Microsoft\Windows\LanmanWorkstation'
    $RegKeys = @($ParametersKey, $PolicyKey)
    $Regs = @(
        @{ Path = $ParametersKey; Name = 'AllowInsecureGuestAuth' }
        @{ Path = $ParametersKey; Name = 'RequireSecuritySignature' }
        @{ Path = $PolicyKey; Name = 'AllowInsecureGuestAuth' }
        @{ Path = $PolicyKey; Name = 'RequireSecuritySignature' }
    )
    $Initial = Get-C3SmbConfig
    Invoke-ExploreStep -Name 'baseline' -Command "Get-SmbClientConfiguration; rejestr $ParametersKey i $PolicyKey" -Registry $Regs -RegistryKeys $RegKeys -After { @{ Smb = $Initial } }
    if (-not $Initial.Ok) { return }
    $GuestInitial = $Initial.Values.EnableInsecureGuestLogons
    $SignInitial = $Initial.Values.RequireSecuritySignature
    if ($GuestInitial -isnot [bool] -or $SignInitial -isnot [bool]) {
        # Nie zgadujemy wartości do przywrócenia: bez zmian.
        Invoke-ExploreStep -Name 'zmiany-pominiete' -Command '(pominięte: odczyt startowy nie ma wartości logicznych)' -Registry $Regs -RegistryKeys $RegKeys -After {
            @{ RestoreSkipped = $true; Reason = 'EnableInsecureGuestLogons lub RequireSecuritySignature nie jest wartością logiczną w odczycie startowym'; Smb = $Initial }
        }
        return
    }
    # Wartość PRZECIWNA do startowej: zmiana jest zawsze widoczna w odczycie i w rejestrze (niezależnie od edycji).
    # Przywrócenie z odczytu startowego zapisuje potem wartość startową jawnie, więc obie wartości logiczne są zaobserwowane.
    $GuestTarget = -not $GuestInitial
    $SignTarget = -not $SignInitial
    Invoke-ExploreStep -Name 'set-guest' -Command "Set-SmbClientConfiguration -EnableInsecureGuestLogons `$$GuestTarget -Force (przeciwna do startowej: $GuestInitial)" -Registry $Regs -RegistryKeys $RegKeys -After { @{ Smb = (Get-C3SmbConfig); Target = $GuestTarget } } -Action {
        Set-SmbClientConfiguration -EnableInsecureGuestLogons $GuestTarget -Force -ErrorAction Stop
        [pscustomobject]@{ ExitCode = $null; Stdout = "Set-SmbClientConfiguration -EnableInsecureGuestLogons $GuestTarget wykonane bez wyjątku"; Stderr = $null; Error = $null }
    }
    Invoke-ExploreStep -Name 'set-signing' -Command "Set-SmbClientConfiguration -RequireSecuritySignature `$$SignTarget -Force (przeciwna do startowej: $SignInitial)" -Registry $Regs -RegistryKeys $RegKeys -After { @{ Smb = (Get-C3SmbConfig); Target = $SignTarget } } -Action {
        Set-SmbClientConfiguration -RequireSecuritySignature $SignTarget -Force -ErrorAction Stop
        [pscustomobject]@{ ExitCode = $null; Stdout = "Set-SmbClientConfiguration -RequireSecuritySignature $SignTarget wykonane bez wyjątku"; Stderr = $null; Error = $null }
    }
    Invoke-ExploreStep -Name 'przywroc-z-odczytu-startowego' -Command 'Set-SmbClientConfiguration -EnableInsecureGuestLogons <start> -RequireSecuritySignature <start> -Force' -Registry $Regs -RegistryKeys $RegKeys -After {
        $SmbNow = Get-C3SmbConfig
        $SmbDifferences = @(Compare-C3SmbConfig -Baseline $Initial -Current $SmbNow)
        @{ Smb = $SmbNow; InitialSmb = $Initial; MatchesInitial = ($SmbDifferences.Count -eq 0); Differences = @($SmbDifferences) }
    } -Action {
        Set-SmbClientConfiguration -EnableInsecureGuestLogons $GuestInitial -RequireSecuritySignature $SignInitial -Force -ErrorAction Stop
        [pscustomobject]@{ ExitCode = $null; Stdout = "Przywrócono EnableInsecureGuestLogons=$GuestInitial RequireSecuritySignature=$SignInitial"; Stderr = $null; Error = $null }
    }
}

function Get-C3ServiceInfo {
    # Win32_Service jednej usługi: wartości surowe; brak = Found=false; błąd CIM zapisany przy usłudze.
    [CmdletBinding()]
    param([Parameter(Mandatory)][string]$ServiceName)
    $Info = [ordered]@{ Name = $ServiceName; Found = $false; Error = $null; StartMode = $null; State = $null; DelayedAutoStart = $null }
    try {
        $Cim = Get-CimInstance -ClassName Win32_Service -Filter "Name='$ServiceName'" -ErrorAction Stop
        if ($null -ne $Cim) {
            $Info.Found = $true
            $Info.StartMode = [string]$Cim.StartMode
            $Info.State = [string]$Cim.State
            $Info.DelayedAutoStart = $Cim.DelayedAutoStart
        }
    }
    catch { $Info.Error = $_.Exception.Message }
    return [pscustomobject]$Info
}

function Invoke-C3ServicesProbe {
    # (5) Tylko odczyt.
    [CmdletBinding()]
    param()
    $ServiceNames = @('fdPHost', 'FDResPub', 'SSDPSRV', 'upnphost', 'lmhosts', 'LanmanWorkstation', 'LanmanServer')
    Invoke-ExploreStep -Name 'win32-service' -Command ('Get-CimInstance Win32_Service: ' + ($ServiceNames -join ', ')) -After {
        @{ Services = @($ServiceNames | ForEach-Object { Get-C3ServiceInfo -ServiceName $_ }) }
    }
}

function Invoke-C3NetworkProfileProbe {
    # (6) Tylko odczyt: profil sieci, karty, CIM. Błąd albo pusty wynik to dane.
    [CmdletBinding()]
    param()
    Invoke-ExploreStep -Name 'get-netconnectionprofile' -Command 'Get-NetConnectionProfile' -After {
        @{ NetConnectionProfile = (Invoke-C3Read -Script {
                    Get-NetConnectionProfile -ErrorAction Stop | ForEach-Object {
                        [pscustomobject]@{
                            Name = [string]$_.Name; InterfaceAlias = [string]$_.InterfaceAlias; InterfaceIndex = $_.InterfaceIndex
                            NetworkCategory = (Get-C3EnumPair $_.NetworkCategory); IPv4Connectivity = (Get-C3EnumPair $_.IPv4Connectivity); IPv6Connectivity = (Get-C3EnumPair $_.IPv6Connectivity)
                        }
                    }
                }) }
    }
    Invoke-ExploreStep -Name 'get-netadapter' -Command 'Get-NetAdapter' -After {
        @{ NetAdapter = (Invoke-C3Read -Script {
                    Get-NetAdapter -ErrorAction Stop | ForEach-Object { [pscustomobject]@{ Name = [string]$_.Name; Status = [string]$_.Status; InterfaceDescription = [string]$_.InterfaceDescription; ifIndex = $_.ifIndex } }
                }) }
    }
    Invoke-ExploreStep -Name 'cim-msft-netconnectionprofile' -Command 'Get-CimInstance -Namespace root\StandardCimv2 -ClassName MSFT_NetConnectionProfile' -After {
        @{ CimProfile = (Invoke-C3Read -Script {
                    Get-CimInstance -Namespace 'root\StandardCimv2' -ClassName MSFT_NetConnectionProfile -ErrorAction Stop | ForEach-Object {
                        [pscustomobject]@{
                            Name = [string]$_.Name; InterfaceAlias = [string]$_.InterfaceAlias
                            NetworkCategory = (Get-C3EnumPair $_.NetworkCategory); IPv4Connectivity = (Get-C3EnumPair $_.IPv4Connectivity); IPv6Connectivity = (Get-C3EnumPair $_.IPv6Connectivity)
                        }
                    }
                }) }
    }
}

function Invoke-C3Collection {
    # Punkt wejścia po stronie VM: zwraca JEDEN string JSON (meta + wszystkie sondy). Sonda ze zmianą (SMB) po odczytowych.
    [CmdletBinding()]
    param()
    $Probes = [ordered]@{
        systemProfile  = { Invoke-C3SystemProfileProbe }
        ipv6           = { Invoke-C3Ipv6Probe }
        smbClient      = { Invoke-C3SmbProbe }
        services       = { Invoke-C3ServicesProbe }
        networkProfile = { Invoke-C3NetworkProfileProbe }
    }
    return (Invoke-ExploreCollection -Probe $Probes)
}

# ---------------------------------------------------------------------------------------------------------------------
# Strona hosta.
# ---------------------------------------------------------------------------------------------------------------------

function Get-C3RemoteFunctionNames {
    return @(
        'Get-C3EnumPair', 'Invoke-C3Read', 'Get-C3SmbConfig', 'Compare-C3SmbConfig', 'Invoke-C3SystemProfileProbe', 'Invoke-C3Ipv6Probe',
        'Invoke-C3SmbProbe', 'Get-C3ServiceInfo', 'Invoke-C3ServicesProbe', 'Invoke-C3NetworkProfileProbe', 'Invoke-C3Collection'
    )
}

function Get-C3RemoteScript {
    # Tekst definicji funkcji zdalnych: część wspólna z modułu + funkcje C3 (FunctionInfo pobrane tu, w zasięgu skryptu).
    [CmdletBinding()]
    [OutputType([string])]
    param()
    $Names = @(Get-ExploreCommonRemoteFunctionNames) + @(Get-C3RemoteFunctionNames)
    return (ConvertTo-ExploreRemoteScript -Command @($Names | ForEach-Object { Get-Command -Name $_ -CommandType Function -ErrorAction Stop }))
}

function Invoke-C3Exploration {
    # Wszystkie wybrane VM po kolei. Błąd uwierzytelnienia = stop całości; inny błąd = zapis i dalej (logika we wspólnym module).
    [CmdletBinding()]
    param(
        [Parameter(Mandatory)][ValidateSet('DSO-26H2-Home', 'DSO-26H2-Pro')][string[]]$VmName,
        [Parameter(Mandatory)][string]$OutDirectory,
        [string]$CheckpointName = 'czysta-26H2',
        [int]$ReadyTimeoutSeconds = 1200
    )
    return (Invoke-ExploreAll -VmName $VmName -OutDirectory $OutDirectory -FileName 'c3-exploration.json' -RemoteScript (Get-C3RemoteScript) `
            -EntryCommand 'Invoke-C3Collection' -CheckpointName $CheckpointName -ReadyTimeoutSeconds $ReadyTimeoutSeconds)
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
        if (-not $OutDirectory) { $OutDirectory = Join-Path $VmRoot ('out\c3-' + (Get-Date -Format 'yyyyMMdd-HHmmss')) }
        $null = New-Item -ItemType Directory -Path $OutDirectory -Force
        Write-Host "Wyniki: $OutDirectory"
        $Outcome = Invoke-C3Exploration -VmName $VmName -OutDirectory $OutDirectory -CheckpointName $CheckpointName -ReadyTimeoutSeconds $ReadyTimeoutSeconds
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
