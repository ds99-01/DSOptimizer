<#
Instrukcja uruchomienia:
    Import-Module .\tests\vm\modules\VmBuild.psm1 -Force
    (używane przez tests\vm\New-TestVm.ps1; wymaga sesji admina z Hyper-V tylko przy faktycznym tworzeniu VM)
Opis działania:
    Skryptowane budowanie VM Hyper-V Gen2 (Secure Boot, vTPM) z Windows 11 26H2: obraz z install.wim/esd
    jest nakładany offline (Expand-WindowsImage + bcdboot), potem dokładane są unattend.xml i skrypty
    Specialize/FirstLogon. Instalator nie wymaga klikania. Hasło VM nigdy nie trafia do wyjścia ani logów.
#>
Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

Import-Module (Join-Path $PSScriptRoot 'VmCommon.psm1')

$script:TemplateDirectory = Join-Path (Split-Path -Parent $PSScriptRoot) 'templates'

function Get-TestVmTargets {
    [CmdletBinding()]
    [OutputType([hashtable[]])]
    param(
        [string]$VmName,
        [int]$EditionIndex
    )
    $HasName = $PSBoundParameters.ContainsKey('VmName')
    $HasIndex = $PSBoundParameters.ContainsKey('EditionIndex')
    if ($HasName -ne $HasIndex) {
        throw 'Podaj oba parametry (-VmName i -EditionIndex) albo żaden.'
    }
    if ($HasName) {
        $Pairs = @(, @($VmName, $EditionIndex))
    }
    else {
        $Pairs = @(@('DSO-26H2-Home', 1), @('DSO-26H2-Pro', 5))
    }
    $Targets = foreach ($Pair in $Pairs) {
        if ($Pair[0] -notmatch '^[A-Za-z0-9-]{1,15}$') {
            throw "Nazwa VM '$($Pair[0])' jest niepoprawna: max 15 znaków, tylko litery, cyfry i myślnik (nazwa komputera NetBIOS)."
        }
        @{ VmName = $Pair[0]; EditionIndex = [int]$Pair[1]; ComputerName = $Pair[0].ToUpperInvariant() }
    }
    return , @($Targets)
}

function Read-TestVmTemplate {
    param([Parameter(Mandatory)][string]$Path)
    return [System.IO.File]::ReadAllText($Path, [System.Text.Encoding]::UTF8)
}

function New-UnattendXml {
    [CmdletBinding()]
    [OutputType([string])]
    param(
        [Parameter(Mandatory)][ValidatePattern('^[A-Za-z0-9-]{1,15}\z')][string]$ComputerName,
        [Parameter(Mandatory)][ValidateNotNullOrEmpty()][string]$Password,
        [string]$TemplatePath = (Join-Path $script:TemplateDirectory 'unattend.xml')
    )
    $Text = (Read-TestVmTemplate -Path $TemplatePath).Replace('__COMPUTER_NAME__', $ComputerName)
    if (-not $Text.Contains('__PASSWORD__')) {
        throw 'Szablon unattend nie zawiera tokenu hasła.'
    }
    # Hasło wstawiamy dopiero po kontroli tokenów, żeby jego treść nie dała fałszywego alarmu.
    if ($Text.Replace('__PASSWORD__', '') -match '__[A-Z_]+__') {
        throw "Szablon unattend zawiera niepodstawiony token: $($Matches[0])"
    }
    $Escaped = [System.Security.SecurityElement]::Escape($Password)
    $Result = $Text.Replace('__PASSWORD__', $Escaped)
    $null = [xml]$Result
    return $Result
}

function New-SpecializeScript {
    [CmdletBinding()]
    [OutputType([string])]
    param(
        [Parameter(Mandatory)][ValidateNotNullOrEmpty()][string]$Password,
        [string]$TemplatePath = (Join-Path $script:TemplateDirectory 'Specialize.ps1')
    )
    if ($Password.Contains("'")) {
        throw "Hasło z apostrofem nie może trafić do skryptu Specialize (literał w apostrofach)."
    }
    $Text = Read-TestVmTemplate -Path $TemplatePath
    if (-not $Text.Contains('__PASSWORD__')) {
        throw 'Szablon Specialize nie zawiera tokenu hasła.'
    }
    return $Text.Replace('__PASSWORD__', $Password)
}

function Resolve-InstallImage {
    [CmdletBinding()]
    [OutputType([string])]
    param(
        [Parameter(Mandatory)][string]$SourcesPath
    )
    foreach ($Name in 'install.wim', 'install.esd') {
        $Candidate = Join-Path $SourcesPath $Name
        if (Test-Path -LiteralPath $Candidate -PathType Leaf) {
            return $Candidate
        }
    }
    throw "Brak install.wim ani install.esd w '$SourcesPath'."
}

function Test-TestVmElevated {
    [OutputType([bool])]
    param()
    $Principal = New-Object System.Security.Principal.WindowsPrincipal([System.Security.Principal.WindowsIdentity]::GetCurrent())
    return $Principal.IsInRole([System.Security.Principal.WindowsBuiltInRole]::Administrator)
}

function Assert-TestVmHost {
    [CmdletBinding()]
    param(
        [Parameter(Mandatory)][string]$IsoPath
    )
    if (-not (Test-TestVmElevated)) {
        throw 'Uruchom skrypt w sesji PowerShell jako administrator.'
    }
    if (-not (Get-Command -Name 'New-VM' -ErrorAction SilentlyContinue)) {
        throw 'Brak modułu Hyper-V (New-VM). Włącz funkcję Hyper-V i zrestartuj komputer.'
    }
    if (-not (Test-Path -LiteralPath $IsoPath -PathType Leaf)) {
        throw "Plik ISO nie istnieje: '$IsoPath'."
    }
}

function Get-TestVmPartitionRoot {
    # Zwraca katalog główny partycji (np. 'E:\'); literę przydziela tylko, gdy jej brak.
    [OutputType([string])]
    param(
        [Parameter(Mandatory)][int]$DiskNumber,
        [Parameter(Mandatory)][int]$PartitionNumber
    )
    $Partition = Get-Partition -DiskNumber $DiskNumber -PartitionNumber $PartitionNumber -ErrorAction Stop
    if (-not $Partition.DriveLetter) {
        Add-PartitionAccessPath -DiskNumber $DiskNumber -PartitionNumber $PartitionNumber -AssignDriveLetter -ErrorAction Stop
        $Partition = Get-Partition -DiskNumber $DiskNumber -PartitionNumber $PartitionNumber -ErrorAction Stop
    }
    if (-not $Partition.DriveLetter) {
        throw "Nie udało się przydzielić litery partycji $PartitionNumber dysku $DiskNumber."
    }
    return ('{0}:\' -f $Partition.DriveLetter)
}

function Assert-TestVmDiskIdentity {
    # Zanim ruszymy dysk: to ma być dokładnie jeden, pusty (RAW) dysk wirtualny z naszego VHDX.
    param(
        [Parameter(Mandatory)][AllowNull()]$Disk,
        [Parameter(Mandatory)][string]$VhdPath
    )
    $Expected = [System.IO.Path]::GetFullPath($VhdPath)
    if (@($Disk).Count -ne 1 -or $null -eq $Disk) {
        throw "Oczekiwano dokładnie jednego dysku po zamontowaniu '$Expected'. Nic nie zmieniono."
    }
    if ("$($Disk.BusType)" -ne 'File Backed Virtual') {
        throw "Dysk $($Disk.Number) nie jest plikowym dyskiem wirtualnym (BusType '$($Disk.BusType)'). Nic nie zmieniono."
    }
    if ("$($Disk.Location)" -ine $Expected) {
        throw "Dysk $($Disk.Number) wskazuje na '$($Disk.Location)', a nie na '$Expected'. Nic nie zmieniono."
    }
    if ("$($Disk.PartitionStyle)" -ne 'RAW') {
        throw "Dysk $($Disk.Number) nie jest pusty (PartitionStyle '$($Disk.PartitionStyle)'). Nic nie zmieniono."
    }
}

function Invoke-TestVmNative {
    # Uruchamia program natywny; stderr łączy ze stdout (bez NativeCommandError), zwraca kod wyjścia i tekst.
    param(
        [Parameter(Mandatory)][string]$FilePath,
        [Parameter(Mandatory)][string[]]$ArgumentList
    )
    $ErrorActionPreference = 'Continue'
    $Output = (& $FilePath @ArgumentList 2>&1 | Out-String)
    [pscustomobject]@{ ExitCode = $LASTEXITCODE; Output = $Output }
}

function Invoke-TestVmBcdboot {
    # /nofirmwaresync: bcdboot nie dotyka NVRAM hosta (bez wpisu Windows Boot Manager w firmware).
    param(
        [Parameter(Mandatory)][string]$WindowsRoot,
        [Parameter(Mandatory)][string]$SystemRoot
    )
    $Exe = Join-Path $env:SystemRoot 'System32\bcdboot.exe'
    $Result = Invoke-TestVmNative -FilePath $Exe -ArgumentList @((Join-Path $WindowsRoot 'Windows'), '/s', $SystemRoot.TrimEnd('\'), '/f', 'UEFI', '/nofirmwaresync')
    if ($Result.ExitCode -ne 0) {
        throw "bcdboot zakończył się kodem $($Result.ExitCode): $($Result.Output)"
    }
}

function New-TestVmDisk {
    # Integracyjne (prawdziwy VHDX, Mount-VHD, DISM, bcdboot) - nie jest testowane Pesterem.
    [CmdletBinding()]
    param(
        [Parameter(Mandatory)][string]$VhdPath,
        [Parameter(Mandatory)][uint64]$SizeBytes,
        [Parameter(Mandatory)][string]$ImagePath,
        [Parameter(Mandatory)][int]$EditionIndex,
        [Parameter(Mandatory)][string]$UnattendXml,
        [Parameter(Mandatory)][string]$SpecializeScript,
        [Parameter(Mandatory)][string]$FirstLogonScript
    )
    $ErrorActionPreference = 'Stop'
    $BasicDataGuid = '{ebd0a0a2-b9e5-4433-87c0-68b6b72699c7}'
    $EspGuid = '{c12a7328-f81f-11d2-ba4b-00a0c93ec93b}'
    $MsrGuid = '{e3c9e316-0b5c-4db8-817d-f92df00215ae}'
    New-VHD -Path $VhdPath -SizeBytes $SizeBytes -Dynamic | Out-Null
    try {
        $Disk = Mount-VHD -Path $VhdPath -Passthru | Get-Disk -ErrorAction Stop
        Assert-TestVmDiskIdentity -Disk $Disk -VhdPath $VhdPath
        Initialize-Disk -Number $Disk.Number -PartitionStyle GPT -ErrorAction Stop
        $System = New-Partition -DiskNumber $Disk.Number -Size 260MB -GptType $BasicDataGuid -ErrorAction Stop
        Format-Volume -Partition $System -FileSystem FAT32 -NewFileSystemLabel 'System' -Force -Confirm:$false -ErrorAction Stop | Out-Null
        New-Partition -DiskNumber $Disk.Number -Size 16MB -GptType $MsrGuid -ErrorAction Stop | Out-Null
        $Windows = New-Partition -DiskNumber $Disk.Number -UseMaximumSize -GptType $BasicDataGuid -ErrorAction Stop
        Format-Volume -Partition $Windows -FileSystem NTFS -NewFileSystemLabel 'Windows' -Force -Confirm:$false -ErrorAction Stop | Out-Null
        $SystemRoot = Get-TestVmPartitionRoot -DiskNumber $Disk.Number -PartitionNumber $System.PartitionNumber
        $WindowsRoot = Get-TestVmPartitionRoot -DiskNumber $Disk.Number -PartitionNumber $Windows.PartitionNumber
        Expand-WindowsImage -ImagePath $ImagePath -Index $EditionIndex -ApplyPath $WindowsRoot | Out-Null
        Invoke-TestVmBcdboot -WindowsRoot $WindowsRoot -SystemRoot $SystemRoot

        $Panther = Join-Path $WindowsRoot 'Windows\Panther'
        $Scripts = Join-Path $WindowsRoot 'Windows\Setup\Scripts'
        $null = New-Item -ItemType Directory -Path $Panther, $Scripts -Force
        # Set-Content -Encoding UTF8 daje UTF-8 z BOM w PowerShell 5.1.
        Set-Content -LiteralPath (Join-Path $Panther 'unattend.xml') -Value $UnattendXml -Encoding UTF8
        Set-Content -LiteralPath (Join-Path $Scripts 'Specialize.ps1') -Value $SpecializeScript -Encoding UTF8
        Set-Content -LiteralPath (Join-Path $Scripts 'FirstLogon.ps1') -Value $FirstLogonScript -Encoding UTF8

        Remove-PartitionAccessPath -DiskNumber $Disk.Number -PartitionNumber $System.PartitionNumber -AccessPath $SystemRoot -ErrorAction Stop
        Set-Partition -DiskNumber $Disk.Number -PartitionNumber $System.PartitionNumber -GptType $EspGuid -ErrorAction Stop
    }
    finally {
        Dismount-VHD -Path $VhdPath
    }
}

function New-TestVmMachine {
    [CmdletBinding()]
    param(
        [Parameter(Mandatory)][string]$VmName,
        [Parameter(Mandatory)][string]$VhdPath,
        [Parameter(Mandatory)][uint64]$MemoryBytes,
        [Parameter(Mandatory)][int]$ProcessorCount,
        [switch]$WithNetwork
    )
    $ErrorActionPreference = 'Stop'
    # Wszystkie cmdlety Hyper-V przez -VMName (bez potoku obiektów VM). Karta sieciowa zostaje niepodłączona.
    New-VM -Name $VmName -Generation 2 -MemoryStartupBytes $MemoryBytes -VHDPath $VhdPath -ErrorAction Stop | Out-Null
    Set-VMMemory -VMName $VmName -DynamicMemoryEnabled $false -ErrorAction Stop
    Set-VMProcessor -VMName $VmName -Count $ProcessorCount -ErrorAction Stop
    Set-VMFirmware -VMName $VmName -EnableSecureBoot On -SecureBootTemplate MicrosoftWindows -ErrorAction Stop
    Set-VMKeyProtector -VMName $VmName -NewLocalKeyProtector -ErrorAction Stop
    Enable-VMTPM -VMName $VmName -ErrorAction Stop
    Set-VM -Name $VmName -AutomaticCheckpointsEnabled $false -CheckpointType Standard -ErrorAction Stop
    $Drive = Get-VMHardDiskDrive -VMName $VmName -ErrorAction Stop
    Set-VMFirmware -VMName $VmName -FirstBootDevice $Drive -ErrorAction Stop
    if ($WithNetwork) {
        Connect-VMNetworkAdapter -VMName $VmName -SwitchName 'Default Switch' -ErrorAction Stop
    }
}

function Wait-TestVmFirstBoot {
    [CmdletBinding()]
    param(
        [Parameter(Mandatory)][string]$VmName,
        [Parameter(Mandatory)][pscredential]$Credential,
        [ValidateRange(0.001, 600)][double]$TimeoutMinutes = 45,
        [ValidateRange(0, 300)][int]$PollSeconds = 10
    )
    $ErrorActionPreference = 'Stop'
    $Deadline = (Get-Date).AddMinutes($TimeoutMinutes)
    # Gotowość = znacznik istnieje ORAZ pliki z hasłem zniknęły. Argument $Remove = usuń pozostałości (jako Administrator).
    $Probe = {
        param($Remove)
        $Secrets = @('C:\Windows\Panther\unattend.xml', 'C:\Windows\Setup\Scripts\Specialize.ps1')
        if ($Remove) {
            foreach ($Secret in $Secrets) { Remove-Item -LiteralPath $Secret -Force -ErrorAction SilentlyContinue }
        }
        [pscustomobject]@{
            Marker   = [bool](Test-Path -LiteralPath 'C:\ProgramData\dso-vm-ready.txt')
            Leftover = @($Secrets | Where-Object { Test-Path -LiteralPath $_ })
            FirstLogonError = $(if (Test-Path -LiteralPath 'C:\ProgramData\dso-vm-firstlogon-error.txt') { Get-Content -LiteralPath 'C:\ProgramData\dso-vm-firstlogon-error.txt' -Raw } else { '' })
        }
    }
    $Ready = $false
    $LastError = ''
    while (-not $Ready) {
        try {
            $State = Invoke-Command -VMName $VmName -Credential $Credential -ErrorAction Stop -ScriptBlock $Probe -ArgumentList $false
            if ($State.Marker -and @($State.Leftover).Count -gt 0) {
                $State = Invoke-Command -VMName $VmName -Credential $Credential -ErrorAction Stop -ScriptBlock $Probe -ArgumentList $true
                if (@($State.Leftover).Count -gt 0) {
                    throw "Nie udało się usunąć z VM '$VmName' plików z hasłem: $(@($State.Leftover) -join ', ')."
                }
            }
            $Ready = [bool]$State.Marker
            if (-not $Ready -and $State.FirstLogonError) { $LastError = 'FirstLogon: ' + $State.FirstLogonError }
        }
        catch {
            if ($_.Exception.Message -like '*plików z hasłem*') { throw }
            $LastError = $_.Exception.Message   # PowerShell Direct bywa niedostępny w trakcie instalacji i restartów
            $Ready = $false
        }
        if (-not $Ready) {
            if ((Get-Date) -ge $Deadline) {
                throw "Przekroczono czas oczekiwania ($TimeoutMinutes min) na znacznik gotowości VM '$VmName'. Ostatni błąd PowerShell Direct: $LastError"
            }
            Start-Sleep -Seconds $PollSeconds
        }
    }
    Stop-VM -Name $VmName -Force -ErrorAction Stop
    while ($true) {
        $Vm = Get-VM -Name $VmName -ErrorAction Stop
        if ("$($Vm.State)" -eq 'Off') { break }
        if ((Get-Date) -ge $Deadline.AddMinutes(5)) {
            throw "Przekroczono czas oczekiwania na wyłączenie VM '$VmName'."
        }
        Start-Sleep -Seconds $PollSeconds
    }
}

function New-DsoTestVm {
    [CmdletBinding()]
    param(
        [Parameter(Mandatory)][string]$IsoPath,
        [Parameter(Mandatory)][hashtable[]]$Targets,
        [string]$VhdDirectory,
        [ValidateRange(20, 512)][int]$VhdSizeGB = 64,
        [ValidateRange(2, 64)][int]$MemoryGB = 4,
        [ValidateRange(1, 16)][int]$ProcessorCount = 4,
        [switch]$WithNetwork,
        [string]$CheckpointName = 'czysta-26H2',
        [ValidateRange(0.001, 600)][double]$FirstBootTimeoutMinutes = 45
    )
    $ErrorActionPreference = 'Stop'
    Assert-TestVmHost -IsoPath $IsoPath
    if (-not $VhdDirectory) {
        $VhdDirectory = (Get-VMHost).VirtualHardDiskPath
    }
    $VhdDirectory = $ExecutionContext.SessionState.Path.GetUnresolvedProviderPathFromPSPath($VhdDirectory)
    $IsoFullPath = $ExecutionContext.SessionState.Path.GetUnresolvedProviderPathFromPSPath($IsoPath)

    # Najpierw wszystkie sprawdzenia istnienia; dopiero potem cokolwiek zapisujemy (w tym poświadczenia).
    foreach ($Target in $Targets) {
        if (Get-VM -Name $Target.VmName -ErrorAction SilentlyContinue) {
            throw "VM '$($Target.VmName)' już istnieje – usuń ją ręcznie (nic nie kasuję automatycznie)."
        }
        $Existing = Join-Path $VhdDirectory "$($Target.VmName).vhdx"
        if (Test-Path -LiteralPath $Existing) {
            throw "Dysk '$Existing' już istnieje – usuń go ręcznie. Poświadczenia VM '$($Target.VmName)' zostają nietknięte."
        }
    }

    # Odmontowujemy tylko to, co zamontował skrypt (ISO mógł być już podłączony przez użytkownika).
    $WasAttached = [bool](Get-DiskImage -ImagePath $IsoFullPath -ErrorAction Stop).Attached
    $Image = Mount-DiskImage -ImagePath $IsoFullPath -PassThru -ErrorAction Stop
    try {
        $Letter = (Get-Volume -DiskImage $Image -ErrorAction Stop).DriveLetter
        $ImagePath = Resolve-InstallImage -SourcesPath "${Letter}:\sources"

        foreach ($Target in $Targets) {
            $Info = Get-WindowsImage -ImagePath $ImagePath -Index $Target.EditionIndex
            Write-Host "VM '$($Target.VmName)': edycja '$($Info.ImageName)' (index $($Target.EditionIndex))."
        }

        $FirstLogon = Read-TestVmTemplate -Path (Join-Path $script:TemplateDirectory 'FirstLogon.ps1')
        foreach ($Target in $Targets) {
            $VmName = $Target.VmName
            $VhdPath = Join-Path $VhdDirectory "$VmName.vhdx"
            $Password = New-DsoVmPassword
            try {
                $null = Save-DsoVmCredential -VmName $VmName -Password $Password
                $Unattend = New-UnattendXml -ComputerName $Target.ComputerName -Password $Password
                $Specialize = New-SpecializeScript -Password $Password
            }
            finally {
                $Password = $null
            }
            New-TestVmDisk -VhdPath $VhdPath -SizeBytes ([uint64]$VhdSizeGB * 1GB) -ImagePath $ImagePath `
                -EditionIndex $Target.EditionIndex -UnattendXml $Unattend -SpecializeScript $Specialize -FirstLogonScript $FirstLogon
            $Unattend = $null
            $Specialize = $null
            New-TestVmMachine -VmName $VmName -VhdPath $VhdPath -MemoryBytes ([uint64]$MemoryGB * 1GB) `
                -ProcessorCount $ProcessorCount -WithNetwork:$WithNetwork
            Write-Host "Pierwszy start VM '$VmName' (instalacja bez nadzoru, do $FirstBootTimeoutMinutes min)."
            Start-VM -Name $VmName -ErrorAction Stop
            $Credential = Get-DsoVmCredential -VmName $VmName
            Wait-TestVmFirstBoot -VmName $VmName -Credential $Credential -TimeoutMinutes $FirstBootTimeoutMinutes
            Checkpoint-VM -Name $VmName -SnapshotName $CheckpointName -ErrorAction Stop
            Write-Host "VM '$VmName' gotowa, checkpoint '$CheckpointName' utworzony."
        }
    }
    finally {
        if (-not $WasAttached) {
            try { Dismount-DiskImage -ImagePath $IsoFullPath -ErrorAction Stop | Out-Null }
            catch { Write-Warning "Nie udało się odmontować ISO '$IsoFullPath': $($_.Exception.Message)" }
        }
    }
}

Export-ModuleMember -Function Get-TestVmTargets, New-UnattendXml, New-SpecializeScript, Resolve-InstallImage, Assert-TestVmHost, New-TestVmDisk, New-TestVmMachine, Wait-TestVmFirstBoot, New-DsoTestVm
