<#
Instrukcja uruchomienia:
    VM (sesja PowerShell jako administrator, host z Hyper-V, VM utworzone przez New-TestVm.ps1, checkpoint 'czysta-26H2'):
        .\tests\vm\explore\Invoke-E3AExploration.ps1 -OutDirectory C:\ścieżka\do\wyników
    Jedna VM:
        .\tests\vm\explore\Invoke-E3AExploration.ps1 -VmName DSO-26H2-Pro -OutDirectory C:\ścieżka\do\wyników
    Host, tylko odczyt (bez Hyper-V, bez revertu; sesja admina daje pełniejsze dane, bez niej Win32_Tpm i Win32_EncryptableVolume mogą zwrócić odmowę dostępu):
        .\tests\vm\explore\Invoke-E3AExploration.ps1 -LocalReadOnly -OutDirectory C:\ścieżka\do\wyników
    Bez -OutDirectory wynik trafia do tests\vm\out\e3a-<data> (katalog out\ jest w .gitignore).
Opis działania:
    Sonda źródeł danych dla ekranu „Specyfikacja” (Etap 3, partia A). Zbiera FAKTY (surowe wartości) z prawdziwego Windows 11 26H2
    (VM Home i Pro) oraz z hosta, zanim powstanie czytnik w C#. Nic nie jest interpretowane.
    Sonda jest ściśle tylko do odczytu, w VM i na hoście: Get-CimInstance, Get-CimClass, Get-CimAssociatedInstance, odczyt rejestru
    (RegistryKey otwierany bez prawa zapisu), $env:firmware_type, Confirm-SecureBootUEFI. Brak zapisów, Invoke-CimMethod, Set-*,
    uruchamiania aplikacji. Ten sam blok sondy (Invoke-E3ACollection) działa w VM (Invoke-Command -Session) i lokalnie (-LocalReadOnly).
    Każda klasa CIM / grupa wartości rejestru to osobny krok: namespace, klasa, żądane właściwości, surowe wartości wszystkich
    żądanych właściwości, lista nazw właściwości klasy (CimClass: co istnieje), czas w ms, tekst błędu. Błąd i pusty wynik to dane.
    Rejestr: {Exists, Kind, Value, Hex} oraz dla REG_BINARY szesnastkowy zapis bajtów (BinaryHex) i odczyt jako uint64 little-endian.
    Podklucze klasy ekranu {4d36e968-e325-11ce-bfc1-08002be10318}\NNNN: tylko 4-cyfrowe; odmowa dostępu (np. podklucz Properties) jest zapisana i pomijana.
    Przebieg VM (moduł ExploreCommon): przywrócenie checkpointu -> start -> Wait-VmReady -> PowerShell Direct -> zbieranie, a na końcu ZAWSZE
    (finally) zatrzymanie i powrót do checkpointu. Pierwszy błąd uwierzytelnienia przerywa cały skrypt (blokada konta).
    Wynik: <OutDirectory>\<VmName>\e3a-exploration.json (VM) albo <OutDirectory>\host\e3a-exploration.json (-LocalReadOnly), UTF-8.
    Skrypt dotyka wyłącznie VM DSO-26H2-Home i DSO-26H2-Pro (ValidateSet), nigdy obcej VM.
    Kod wyjścia: 0 gdy zebrano wszystko bez błędu przebiegu, 1 w przeciwnym razie.
#>
[CmdletBinding()]
param(
    [ValidateSet('DSO-26H2-Home', 'DSO-26H2-Pro')][string[]]$VmName = @('DSO-26H2-Home', 'DSO-26H2-Pro'),
    [string]$OutDirectory,
    [string]$CheckpointName = 'czysta-26H2',
    [ValidateRange(60, 7200)][int]$ReadyTimeoutSeconds = 1200,
    [switch]$LocalReadOnly
)

# ---------------------------------------------------------------------------------------------------------------------
# Funkcje sondy (wysyłane do VM jako tekst, więc samowystarczalne; wołają tylko siebie nawzajem i cmdlety odczytowe).
# Uwaga: nie zależą od ExploreCommon, żeby tryb -LocalReadOnly nie ładował żadnych modułów Hyper-V.
# ---------------------------------------------------------------------------------------------------------------------

function ConvertTo-E3AValue {
    # Wartość do postaci przyjaznej JSON: data -> ISO 8601, bajty -> hex, tablice rekurencyjnie, obiekty złożone -> tekst.
    [CmdletBinding()]
    param($Value)
    if ($null -eq $Value) { return $null }
    if ($Value -is [datetime]) { return $Value.ToString('o') }
    if ($Value -is [byte[]]) { return [BitConverter]::ToString($Value) }
    if ($Value -is [string] -or $Value -is [bool] -or $Value -is [char]) { return $Value }
    if ($Value -is [System.Collections.IEnumerable]) { return @(, @($Value | ForEach-Object { ConvertTo-E3AValue $_ })) }
    if ($Value.GetType().IsPrimitive -or $Value -is [decimal]) { return $Value }
    return [string]$Value
}

function Get-E3ARegistryValue {
    # Odczyt jednej wartości HKLM (ścieżka względna, widok 64-bit, klucz otwarty tylko do odczytu). Błąd (np. odmowa dostępu) jako dane.
    [CmdletBinding()]
    param(
        [Parameter(Mandatory)][string]$SubKeyPath,
        [Parameter(Mandatory)][string]$Name
    )
    $Result = [ordered]@{
        Key = "HKLM\$SubKeyPath"; Name = $Name; KeyExists = $false; Exists = $false; Kind = $null; Value = $null; Hex = $null
        BinaryHex = $null; BinaryLength = $null; DecodedUInt64LE = $null; Error = $null
    }
    $Base = $null
    $Key = $null
    try {
        $Base = [Microsoft.Win32.RegistryKey]::OpenBaseKey([Microsoft.Win32.RegistryHive]::LocalMachine, [Microsoft.Win32.RegistryView]::Registry64)
        $Key = $Base.OpenSubKey($SubKeyPath, $false)
        if ($null -ne $Key) {
            $Result.KeyExists = $true
            $Raw = $Key.GetValue($Name, $null, [Microsoft.Win32.RegistryValueOptions]::DoNotExpandEnvironmentNames)
            if ($null -ne $Raw) {
                $Result.Exists = $true
                $Result.Kind = [string]$Key.GetValueKind($Name)
                if ($Raw -is [byte[]]) {
                    $Result.BinaryHex = [BitConverter]::ToString($Raw)
                    $Result.BinaryLength = $Raw.Length
                    $Result.Value = $Result.BinaryHex
                    if ($Raw.Length -eq 8) { $Result.DecodedUInt64LE = [BitConverter]::ToUInt64($Raw, 0) }
                }
                elseif ($Raw -is [string[]]) { $Result.Value = @($Raw) }
                else {
                    $Result.Value = $Raw
                    if ($Result.Kind -eq 'DWord') { $Result.Hex = ('0x{0:X8}' -f [int]$Raw) }
                    elseif ($Result.Kind -eq 'QWord') { $Result.Hex = ('0x{0:X16}' -f [long]$Raw) }
                }
            }
        }
    }
    catch { $Result.Error = $_.Exception.Message }
    finally {
        if ($null -ne $Key) { $Key.Dispose() }
        if ($null -ne $Base) { $Base.Dispose() }
    }
    return [pscustomobject]$Result
}

function Invoke-E3ARegistryStep {
    # Krok: lista wartości rejestru (każda niezależnie; błąd jednej nie psuje pozostałych).
    [CmdletBinding()]
    param(
        [Parameter(Mandatory)][string]$Name,
        [Parameter(Mandatory)][string]$SubKeyPath,
        [Parameter(Mandatory)][string[]]$ValueName
    )
    $Stopwatch = [System.Diagnostics.Stopwatch]::StartNew()
    $Record = [ordered]@{ Step = $Name; Kind = 'registry'; Key = "HKLM\$SubKeyPath"; ValuesRequested = @($ValueName); Registry = @(); Error = $null; DurationMs = 0 }
    try { $Record.Registry = @($ValueName | ForEach-Object { Get-E3ARegistryValue -SubKeyPath $SubKeyPath -Name $_ }) }
    catch { $Record.Error = $_.Exception.Message }
    $Stopwatch.Stop()
    $Record.DurationMs = [int]$Stopwatch.ElapsedMilliseconds
    return [pscustomobject]$Record
}

function Invoke-E3ACimStep {
    # Krok: jedna klasa CIM. Zapisuje namespace, klasę, żądane właściwości, surowe wartości (każdej żądanej właściwości z osobna,
    # brakujące w MissingProperties), listę nazw właściwości i metod klasy (Get-CimClass), liczbę instancji, czas i błąd.
    # ItemExtra: opcjonalny blok dostający instancję, zwraca słownik dodatkowych pól (np. powiązane instancje); jego błąd jest w polu ExtraError.
    [CmdletBinding()]
    param(
        [Parameter(Mandatory)][string]$Name,
        [Parameter(Mandatory)][string]$Namespace,
        [Parameter(Mandatory)][string]$ClassName,
        [Parameter(Mandatory)][string[]]$Property,
        [scriptblock]$ItemExtra
    )
    $Stopwatch = [System.Diagnostics.Stopwatch]::StartNew()
    $Record = [ordered]@{
        Step = $Name; Kind = 'cim'; Namespace = $Namespace; Class = $ClassName; PropertiesRequested = @($Property)
        Count = $null; Items = @(); ClassProperties = $null; ClassMethods = $null; ClassError = $null; Error = $null; DurationMs = 0
    }
    try {
        $Instances = @(Get-CimInstance -Namespace $Namespace -ClassName $ClassName -ErrorAction Stop)
        $Record.Count = $Instances.Count
        $Items = @()
        foreach ($Instance in $Instances) {
            $Values = [ordered]@{}
            $Types = [ordered]@{}
            $Missing = @()
            foreach ($PropertyName in $Property) {
                $Found = $Instance.CimInstanceProperties[$PropertyName]
                if ($null -eq $Found) { $Missing += $PropertyName; continue }
                $Values[$PropertyName] = ConvertTo-E3AValue $Found.Value
                $Types[$PropertyName] = [string]$Found.CimType
            }
            $Item = [ordered]@{ Values = $Values; CimTypes = $Types; MissingProperties = @($Missing) }
            if ($null -ne $ItemExtra) {
                try {
                    $ExtraFields = & $ItemExtra $Instance
                    foreach ($ExtraKey in $ExtraFields.Keys) { $Item[$ExtraKey] = $ExtraFields[$ExtraKey] }
                }
                catch { $Item['ExtraError'] = $_.Exception.Message }
            }
            $Items += [pscustomobject]$Item
        }
        $Record.Items = @($Items)
    }
    catch { $Record.Error = $_.Exception.Message }
    try {
        $Class = Get-CimClass -Namespace $Namespace -ClassName $ClassName -ErrorAction Stop
        $Record.ClassProperties = @($Class.CimClassProperties | ForEach-Object { $_.Name })
        $Record.ClassMethods = @($Class.CimClassMethods | ForEach-Object { $_.Name })
    }
    catch { $Record.ClassError = $_.Exception.Message }
    $Stopwatch.Stop()
    $Record.DurationMs = [int]$Stopwatch.ElapsedMilliseconds
    return [pscustomobject]$Record
}

function Invoke-E3ADisplayRegistryStep {
    # Podklucze klasy ekranu: tylko 4-cyfrowe (NNNN). Odmowa dostępu do podklucza (np. Properties) jest zapisana i pomijana.
    [CmdletBinding()]
    param()
    $Stopwatch = [System.Diagnostics.Stopwatch]::StartNew()
    $ClassPath = 'SYSTEM\CurrentControlSet\Control\Class\{4d36e968-e325-11ce-bfc1-08002be10318}'
    $Record = [ordered]@{
        Step = 'registry-display-class'; Kind = 'registry-display'; Key = "HKLM\$ClassPath"; AllSubKeyNames = @(); Adapters = @(); Error = $null; DurationMs = 0
    }
    $Base = $null
    $Class = $null
    try {
        $Base = [Microsoft.Win32.RegistryKey]::OpenBaseKey([Microsoft.Win32.RegistryHive]::LocalMachine, [Microsoft.Win32.RegistryView]::Registry64)
        $Class = $Base.OpenSubKey($ClassPath, $false)
        if ($null -eq $Class) { $Record.Error = 'klucz klasy ekranu nie istnieje' }
        else {
            $Names = @($Class.GetSubKeyNames())
            $Record.AllSubKeyNames = @($Names)
            $Adapters = @()
            foreach ($SubName in @($Names | Where-Object { $_ -match '^\d{4}$' })) {
                $SubPath = "$ClassPath\$SubName"
                $Entry = [ordered]@{ SubKey = $SubName; Accessible = $true; AccessError = $null; SubKeyNames = @(); Values = @() }
                $Sub = $null
                try {
                    $Sub = $Base.OpenSubKey($SubPath, $false)
                    if ($null -eq $Sub) { $Entry.Accessible = $false; $Entry.AccessError = 'OpenSubKey zwrócił null' }
                    else {
                        try { $Entry.SubKeyNames = @($Sub.GetSubKeyNames()) } catch { $Entry.AccessError = "GetSubKeyNames: $($_.Exception.Message)" }
                    }
                }
                catch { $Entry.Accessible = $false; $Entry.AccessError = $_.Exception.Message }
                finally { if ($null -ne $Sub) { $Sub.Dispose() } }
                if ($Entry.Accessible) {
                    $Entry.Values = @(@('DriverDesc', 'MatchingDeviceId', 'HardwareInformation.qwMemorySize', 'HardwareInformation.MemorySize', 'HardwareInformation.AdapterString') |
                            ForEach-Object { Get-E3ARegistryValue -SubKeyPath $SubPath -Name $_ })
                }
                $Adapters += [pscustomobject]$Entry
            }
            $Record.Adapters = @($Adapters)
        }
    }
    catch { $Record.Error = $_.Exception.Message }
    finally {
        if ($null -ne $Class) { $Class.Dispose() }
        if ($null -ne $Base) { $Base.Dispose() }
    }
    $Stopwatch.Stop()
    $Record.DurationMs = [int]$Stopwatch.ElapsedMilliseconds
    return [pscustomobject]$Record
}

function Invoke-E3AFirmwareStep {
    # $env:firmware_type i Confirm-SecureBootUEFI (oba tylko odczyt); błąd cmdletu (BIOS bez UEFI, brak uprawnień) jest daną.
    [CmdletBinding()]
    param()
    $Stopwatch = [System.Diagnostics.Stopwatch]::StartNew()
    $Record = [ordered]@{
        Step = 'firmware-secureboot-cmdlet'; Kind = 'cmdlet'; EnvFirmwareType = $env:firmware_type; ConfirmSecureBootUEFI = $null; ConfirmError = $null; Error = $null; DurationMs = 0
    }
    try { $Record.ConfirmSecureBootUEFI = [bool](Confirm-SecureBootUEFI -ErrorAction Stop) }
    catch { $Record.ConfirmError = $_.Exception.Message }
    $Stopwatch.Stop()
    $Record.DurationMs = [int]$Stopwatch.ElapsedMilliseconds
    return [pscustomobject]$Record
}

function Get-E3AMeta {
    [CmdletBinding()]
    param([string]$Mode)
    $Version = 'SOFTWARE\Microsoft\Windows NT\CurrentVersion'
    $Meta = [ordered]@{}
    foreach ($Name in @('CurrentBuild', 'UBR', 'EditionID', 'DisplayVersion', 'ProductName')) {
        $Meta[$Name] = (Get-E3ARegistryValue -SubKeyPath $Version -Name $Name).Value
    }
    $Meta.PSVersion = [string]$PSVersionTable.PSVersion
    $Meta.IsAdmin = $null
    try {
        $Principal = New-Object System.Security.Principal.WindowsPrincipal([System.Security.Principal.WindowsIdentity]::GetCurrent())
        $Meta.IsAdmin = [bool]$Principal.IsInRole([System.Security.Principal.WindowsBuiltInRole]::Administrator)
    }
    catch { $Meta.IsAdminError = $_.Exception.Message }
    $Meta.Is64BitProcess = [Environment]::Is64BitProcess
    $Meta.ComputerName = $env:COMPUTERNAME
    $Meta.Mode = $Mode
    $Meta.CollectedAt = (Get-Date).ToString('o')
    return $Meta
}

function Invoke-E3ACollection {
    # Punkt wejścia sondy (VM i host): zwraca JEDEN string JSON {meta, steps}. Wyjątek jednego kroku nie przerywa pozostałych.
    [CmdletBinding()]
    param([string]$Mode = 'VM')
    $Steps = New-Object System.Collections.Generic.List[object]
    $Plan = [ordered]@{
        'win32-processor' = { Invoke-E3ACimStep -Name 'win32-processor' -Namespace 'root\cimv2' -ClassName 'Win32_Processor' -Property @('Name', 'NumberOfCores', 'NumberOfLogicalProcessors') }
        'win32-physicalmemory' = { Invoke-E3ACimStep -Name 'win32-physicalmemory' -Namespace 'root\cimv2' -ClassName 'Win32_PhysicalMemory' -Property @('BankLabel', 'DeviceLocator', 'Capacity', 'Speed', 'ConfiguredClockSpeed', 'SMBIOSMemoryType', 'Manufacturer', 'PartNumber') }
        'win32-physicalmemoryarray' = { Invoke-E3ACimStep -Name 'win32-physicalmemoryarray' -Namespace 'root\cimv2' -ClassName 'Win32_PhysicalMemoryArray' -Property @('MemoryDevices') }
        'win32-baseboard' = { Invoke-E3ACimStep -Name 'win32-baseboard' -Namespace 'root\cimv2' -ClassName 'Win32_BaseBoard' -Property @('Manufacturer', 'Product') }
        'win32-bios' = { Invoke-E3ACimStep -Name 'win32-bios' -Namespace 'root\cimv2' -ClassName 'Win32_BIOS' -Property @('SMBIOSBIOSVersion', 'ReleaseDate') }
        'win32-computersystem' = { Invoke-E3ACimStep -Name 'win32-computersystem' -Namespace 'root\cimv2' -ClassName 'Win32_ComputerSystem' -Property @('Manufacturer', 'Model') }
        'win32-videocontroller' = { Invoke-E3ACimStep -Name 'win32-videocontroller' -Namespace 'root\cimv2' -ClassName 'Win32_VideoController' -Property @('Name', 'AdapterRAM', 'DriverVersion', 'PNPDeviceID') }
        'registry-display-class' = { Invoke-E3ADisplayRegistryStep }
        'wmi-monitorid' = {
            Invoke-E3ACimStep -Name 'wmi-monitorid' -Namespace 'root\wmi' -ClassName 'WmiMonitorID' -Property @('InstanceName', 'UserFriendlyName') -ItemExtra {
                param($Instance)
                $Raw = $Instance.CimInstanceProperties['UserFriendlyName']
                $Decoded = $null
                if ($null -ne $Raw -and $null -ne $Raw.Value) { $Decoded = -join @($Raw.Value | Where-Object { [int]$_ -ne 0 } | ForEach-Object { [char][int]$_ }) }
                @{ UserFriendlyNameDecoded = $Decoded }
            }
        }
        'wmi-monitorbasicdisplayparams' = { Invoke-E3ACimStep -Name 'wmi-monitorbasicdisplayparams' -Namespace 'root\wmi' -ClassName 'WmiMonitorBasicDisplayParams' -Property @('InstanceName', 'MaxHorizontalImageSize', 'MaxVerticalImageSize') }
        'win32-sounddevice' = { Invoke-E3ACimStep -Name 'win32-sounddevice' -Namespace 'root\cimv2' -ClassName 'Win32_SoundDevice' -Property @('Name') }
        'msft-physicaldisk' = {
            Invoke-E3ACimStep -Name 'msft-physicaldisk' -Namespace 'root\Microsoft\Windows\Storage' -ClassName 'MSFT_PhysicalDisk' -Property @('DeviceId', 'FriendlyName', 'Size', 'MediaType', 'BusType', 'SerialNumber', 'HealthStatus') -ItemExtra {
                param($Instance)
                $Counters = @(Get-CimAssociatedInstance -InputObject $Instance -ResultClassName 'MSFT_StorageReliabilityCounter' -ErrorAction Stop)
                @{
                    ReliabilityCounterCount = $Counters.Count
                    ReliabilityCounters     = @($Counters | ForEach-Object {
                            $Props = [ordered]@{}
                            foreach ($CounterProperty in $_.CimInstanceProperties) { $Props[$CounterProperty.Name] = ConvertTo-E3AValue $CounterProperty.Value }
                            [pscustomobject]@{ Temperature = (ConvertTo-E3AValue $_.CimInstanceProperties['Temperature'].Value); AllProperties = $Props }
                        })
                }
            }
        }
        'msft-netadapter' = { Invoke-E3ACimStep -Name 'msft-netadapter' -Namespace 'root\StandardCimv2' -ClassName 'MSFT_NetAdapter' -Property @('Name', 'ifIndex', 'InterfaceDescription', 'HardwareInterface', 'Virtual', 'ConnectorPresent', 'MediaConnectState', 'Speed', 'NdisPhysicalMedium') }
        'registry-currentversion' = { Invoke-E3ARegistryStep -Name 'registry-currentversion' -SubKeyPath 'SOFTWARE\Microsoft\Windows NT\CurrentVersion' -ValueName @('DisplayVersion', 'EditionID', 'CurrentBuild', 'UBR', 'ProductName') }
        'win32-operatingsystem' = { Invoke-E3ACimStep -Name 'win32-operatingsystem' -Namespace 'root\cimv2' -ClassName 'Win32_OperatingSystem' -Property @('InstallDate') }
        'win32-tpm' = { Invoke-E3ACimStep -Name 'win32-tpm' -Namespace 'root\CIMV2\Security\MicrosoftTpm' -ClassName 'Win32_Tpm' -Property @('IsEnabled_InitialValue', 'IsActivated_InitialValue', 'SpecVersion') }
        'registry-secureboot-state' = { Invoke-E3ARegistryStep -Name 'registry-secureboot-state' -SubKeyPath 'SYSTEM\CurrentControlSet\Control\SecureBoot\State' -ValueName @('UEFISecureBootEnabled') }
        'firmware-secureboot-cmdlet' = { Invoke-E3AFirmwareStep }
        'win32-deviceguard' = { Invoke-E3ACimStep -Name 'win32-deviceguard' -Namespace 'root\Microsoft\Windows\DeviceGuard' -ClassName 'Win32_DeviceGuard' -Property @('VirtualizationBasedSecurityStatus', 'SecurityServicesRunning') }
        'win32-encryptablevolume' = { Invoke-E3ACimStep -Name 'win32-encryptablevolume' -Namespace 'root\CIMV2\Security\MicrosoftVolumeEncryption' -ClassName 'Win32_EncryptableVolume' -Property @('DriveLetter', 'ConversionStatus', 'ProtectionStatus') }
    }
    foreach ($StepName in $Plan.Keys) {
        try { $Steps.Add((& $Plan[$StepName])) }
        catch { $Steps.Add([pscustomobject]@{ Step = $StepName; Kind = 'blad-sondy'; Error = $_.Exception.Message }) }
    }
    $Meta = $null
    try { $Meta = Get-E3AMeta -Mode $Mode } catch { $Meta = [ordered]@{ Error = $_.Exception.Message } }
    $Document = [ordered]@{ meta = $Meta; steps = @($Steps.ToArray()) }
    return ($Document | ConvertTo-Json -Depth 30)
}

# ---------------------------------------------------------------------------------------------------------------------
# Strona hosta (nic z tego nie jest wysyłane do VM).
# ---------------------------------------------------------------------------------------------------------------------

function Get-E3ARemoteFunctionNames {
    return @(
        'ConvertTo-E3AValue', 'Get-E3ARegistryValue', 'Invoke-E3ARegistryStep', 'Invoke-E3ACimStep', 'Invoke-E3ADisplayRegistryStep',
        'Invoke-E3AFirmwareStep', 'Get-E3AMeta', 'Invoke-E3ACollection'
    )
}

function Get-E3ARemoteScript {
    # Tekst definicji funkcji sondy do dot-sourcingu w VM (FunctionInfo pobrane tu, w zasięgu skryptu; ConvertTo-ExploreRemoteScript z ExploreCommon).
    [CmdletBinding()]
    [OutputType([string])]
    param()
    return (ConvertTo-ExploreRemoteScript -Command @(Get-E3ARemoteFunctionNames | ForEach-Object { Get-Command -Name $_ -CommandType Function -ErrorAction Stop }))
}

function Save-E3AHostResult {
    # Zapis wyniku lokalnego: <OutDirectory>\host\e3a-exploration.json, UTF-8 z BOM (jak Save-ExploreResult).
    [CmdletBinding()]
    [OutputType([string])]
    param(
        [Parameter(Mandatory)][string]$Json,
        [Parameter(Mandatory)][string]$OutDirectory
    )
    $Directory = Join-Path ([System.IO.Path]::GetFullPath($OutDirectory)) 'host'
    $null = New-Item -ItemType Directory -Path $Directory -Force
    $Path = Join-Path $Directory 'e3a-exploration.json'
    $Collected = $Json | ConvertFrom-Json
    $Result = [ordered]@{ schema = 1; vmName = 'host'; mode = 'LocalReadOnly'; finishedAt = (Get-Date).ToString('o') }
    foreach ($Property in $Collected.PSObject.Properties) { $Result[$Property.Name] = $Property.Value }
    [System.IO.File]::WriteAllText($Path, ($Result | ConvertTo-Json -Depth 30), (New-Object System.Text.UTF8Encoding($true)))
    return $Path
}

# Przy dot-sourcingu tylko definicje; uruchomienie przebiegu tylko przy zwykłym wywołaniu skryptu.
if ($MyInvocation.InvocationName -ne '.') {
    $ExitCode = 1
    try {
        Set-StrictMode -Version Latest
        $ErrorActionPreference = 'Stop'
        $VmRoot = Split-Path -Parent $PSScriptRoot
        if (-not $OutDirectory) { $OutDirectory = Join-Path $VmRoot ('out\e3a-' + (Get-Date -Format 'yyyyMMdd-HHmmss')) }
        if ($LocalReadOnly) {
            if ($PSBoundParameters.ContainsKey('VmName')) { throw '-LocalReadOnly nie łączy się z -VmName (host: bez Hyper-V i bez VM).' }
            $OutDirectory = $ExecutionContext.SessionState.Path.GetUnresolvedProviderPathFromPSPath($OutDirectory)
            Write-Host "Wyniki: $OutDirectory"
            $SavedPath = Save-E3AHostResult -Json (Invoke-E3ACollection -Mode 'LocalReadOnly') -OutDirectory $OutDirectory
            Write-Host "Zapisano: $SavedPath"
            $ExitCode = 0
        }
        else {
            Import-Module (Join-Path $VmRoot 'modules\VmCommon.psm1') -Force
            Import-Module (Join-Path $VmRoot 'modules\VmRun.psm1') -Force
            Import-Module (Join-Path $VmRoot 'modules\VmHost.psm1') -Force
            # ExploreCommon trzyma referencję do instancji VmHost; ponowny import po nich wiąże go z bieżącą (jak w Invoke-C4Exploration).
            Import-Module (Join-Path $PSScriptRoot 'ExploreCommon.psm1') -Force
            $Principal = New-Object System.Security.Principal.WindowsPrincipal([System.Security.Principal.WindowsIdentity]::GetCurrent())
            if (-not $Principal.IsInRole([System.Security.Principal.WindowsBuiltInRole]::Administrator)) {
                throw 'Uruchom skrypt w sesji PowerShell jako administrator (Hyper-V i PowerShell Direct tego wymagają).'
            }
            $OutDirectory = Resolve-ExploreOutDirectory -Path $OutDirectory
            Write-Host "Wyniki: $OutDirectory"
            $Outcome = Invoke-ExploreAll -VmName $VmName -OutDirectory $OutDirectory -FileName 'e3a-exploration.json' -RemoteScript (Get-E3ARemoteScript) `
                -EntryCommand 'Invoke-E3ACollection' -CheckpointName $CheckpointName -ReadyTimeoutSeconds $ReadyTimeoutSeconds
            foreach ($Path in $Outcome.Paths) { Write-Host "Zapisano: $Path" }
            foreach ($Message in $Outcome.Errors) { Write-Host "BŁĄD $Message" -ForegroundColor Red }
            $ExitCode = if (@($Outcome.Errors).Count -eq 0 -and @($Outcome.Paths).Count -eq @($VmName).Count) { 0 } else { 1 }
        }
    }
    catch {
        Write-Host "BŁĄD: $($_.Exception.Message)" -ForegroundColor Red
        Write-Host $_.ScriptStackTrace -ForegroundColor DarkGray
        $ExitCode = 1
    }
    exit $ExitCode
}
