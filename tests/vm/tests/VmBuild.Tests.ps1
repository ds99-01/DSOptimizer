BeforeDiscovery {
    $script:HasHyperV = [bool](Get-Module -ListAvailable -Name Hyper-V)
}

BeforeAll {
    # Wyłącznik: żaden test na mockach nie może dotknąć prawdziwego Hyper-V (patrz Support\NoLiveVm.ps1).
    . (Join-Path $PSScriptRoot 'Support\NoLiveVm.ps1')

    $script:StubBody = {
        [CmdletBinding()] param([Parameter(ValueFromRemainingArguments)]$Rest, $VMName, $Name, $SnapshotName)
        $PSCmdlet.WriteError([System.Management.Automation.ErrorRecord]::new([System.Exception]::new('x'), 'x', 'NotSpecified', $null))
    }
    Import-Module (Join-Path $PSScriptRoot '..\modules\VmCommon.psm1') -Force
    Import-Module (Join-Path $PSScriptRoot '..\modules\VmBuild.psm1') -Force
}

Describe 'Get-TestVmTargets' {
    It 'domyślnie Home(1) i Pro(5)' {
        $Targets = Get-TestVmTargets
        $Targets.VmName | Should -Be @('DSO-26H2-Home', 'DSO-26H2-Pro')
        $Targets.EditionIndex | Should -Be @(1, 5)
    }
    It 'pojedyncza VM' { (Get-TestVmTargets -VmName 'DSO-X' -EditionIndex 5).ComputerName | Should -Be 'DSO-X' }
    It 'ComputerName to VmName wielkimi literami' { (Get-TestVmTargets -VmName 'dso-x' -EditionIndex 1).ComputerName | Should -Be 'DSO-X' }
    It 'za długa nazwa' { { Get-TestVmTargets -VmName 'DSO-26H2-ZaDlugaNazwa' -EditionIndex 1 } | Should -Throw -ExpectedMessage '*15*' }
    It 'tylko jeden parametr' { { Get-TestVmTargets -VmName 'DSO-X' } | Should -Throw }
    It 'tylko EditionIndex' { { Get-TestVmTargets -EditionIndex 5 } | Should -Throw }
}

Describe 'New-UnattendXml' {
    It 'jest poprawnym XML z nazwą komputera, autologonem dso i pominiętym OOBE' {
        [xml]$Xml = New-UnattendXml -ComputerName 'DSO-26H2-PRO' -Password 'a&b<c'
        $Ns = @{ u = 'urn:schemas-microsoft-com:unattend' }
        (Select-Xml -Xml $Xml -Namespace $Ns -XPath '//u:ComputerName').Node.InnerText | Should -Be 'DSO-26H2-PRO'
        (Select-Xml -Xml $Xml -Namespace $Ns -XPath '//u:AutoLogon/u:Username').Node.InnerText | Should -Be 'dso'
        (Select-Xml -Xml $Xml -Namespace $Ns -XPath '//u:AutoLogon/u:Password/u:Value').Node.InnerText | Should -Be 'a&b<c'
        (Select-Xml -Xml $Xml -Namespace $Ns -XPath '//u:HideOnlineAccountScreens').Node.InnerText | Should -Be 'true'
        $Xml.OuterXml | Should -Match 'Specialize\.ps1'
        $Xml.OuterXml | Should -Not -Match '__[A-Z_]+__'
    }
    It 'hasło z podwójnymi podkreśleniami nie wywołuje fałszywego alarmu o tokenie' {
        { New-UnattendXml -ComputerName 'DSO-X' -Password 'A__B__c' } | Should -Not -Throw
    }
    It 'szablon z nieznanym tokenem daje wyjątek' {
        $Path = Join-Path $TestDrive 'bad.xml'
        Set-Content -LiteralPath $Path -Value '<a>__COMPUTER_NAME__ __PASSWORD__ __INNY__</a>'
        { New-UnattendXml -ComputerName 'DSO-X' -Password 'x' -TemplatePath $Path } | Should -Throw -ExpectedMessage '*token*'
    }
    It 'szablon bez tokenu hasła daje wyjątek' {
        $Path = Join-Path $TestDrive 'nopass.xml'
        Set-Content -LiteralPath $Path -Value '<a>__COMPUTER_NAME__</a>'
        { New-UnattendXml -ComputerName 'DSO-X' -Password 'x' -TemplatePath $Path } | Should -Throw
    }
}

Describe 'New-SpecializeScript' {
    It 'podstawia hasło' { New-SpecializeScript -Password 'Abc-1' | Should -Match "'Abc-1'" }
    It 'odrzuca apostrof' { { New-SpecializeScript -Password "a'b" } | Should -Throw }
    It 'dodaje do grupy przez SID, nie nazwę' { New-SpecializeScript -Password 'A1-b' | Should -Match 'S-1-5-32-544' }
    It 'hasło z $ jest wstawiane dosłownie' { New-SpecializeScript -Password 'A$1-b' | Should -Match ([regex]::Escape("'A`$1-b'")) }
}

Describe 'Resolve-InstallImage' {
    It 'woli wim' { New-Item "$TestDrive\s1\install.wim" -Force | Out-Null; New-Item "$TestDrive\s1\install.esd" -Force | Out-Null; Resolve-InstallImage -SourcesPath "$TestDrive\s1" | Should -BeLike '*install.wim' }
    It 'bierze esd, gdy brak wim' { New-Item "$TestDrive\s2\install.esd" -Force | Out-Null; Resolve-InstallImage -SourcesPath "$TestDrive\s2" | Should -BeLike '*install.esd' }
    It 'brak obrazu' { New-Item "$TestDrive\s3" -ItemType Directory -Force | Out-Null; { Resolve-InstallImage -SourcesPath "$TestDrive\s3" } | Should -Throw }
}

Describe 'Assert-TestVmHost' {
    BeforeEach {
        $script:Iso = Join-Path $TestDrive 'win.iso'
        Set-Content -LiteralPath $script:Iso -Value 'x'
        Mock -ModuleName VmBuild Test-TestVmElevated { $true }
        Mock -ModuleName VmBuild Get-Command { [pscustomobject]@{ Name = 'New-VM' } } -ParameterFilter { $Name -eq 'New-VM' }
    }
    It 'przechodzi, gdy wszystko jest' { { Assert-TestVmHost -IsoPath $script:Iso } | Should -Not -Throw }
    It 'wyjątek bez podniesienia' {
        Mock -ModuleName VmBuild Test-TestVmElevated { $false }
        { Assert-TestVmHost -IsoPath $script:Iso } | Should -Throw -ExpectedMessage '*administrator*'
    }
    It 'wyjątek bez Hyper-V' {
        Mock -ModuleName VmBuild Get-Command { $null } -ParameterFilter { $Name -eq 'New-VM' }
        { Assert-TestVmHost -IsoPath $script:Iso } | Should -Throw -ExpectedMessage '*Hyper-V*'
    }
    It 'wyjątek, gdy ISO nie istnieje' {
        { Assert-TestVmHost -IsoPath (Join-Path $TestDrive 'brak.iso') } | Should -Throw -ExpectedMessage '*ISO*'
    }
}

Describe 'New-TestVmMachine' -Skip:(-not $script:HasHyperV) {
    BeforeEach {
        foreach ($Name in 'New-VM', 'Set-VMMemory', 'Set-VMProcessor', 'Set-VMKeyProtector', 'Enable-VMTPM', 'Set-VM', 'Connect-VMNetworkAdapter') {
            Mock -ModuleName VmBuild -CommandName $Name -MockWith { [pscustomobject]@{ Name = 'vm' } }
        }
        Mock -ModuleName VmBuild -CommandName Set-VMFirmware -MockWith { } -RemoveParameterType FirstBootDevice
        Mock -ModuleName VmBuild -CommandName Get-VMHardDiskDrive -MockWith { [pscustomobject]@{ Path = 'x.vhdx' } }
    }
    It 'Gen2, Secure Boot (MicrosoftWindows), vTPM, bez sieci' {
        New-TestVmMachine -VmName 'DSO-X' -VhdPath 'x.vhdx' -MemoryBytes 4GB -ProcessorCount 4
        Should -Invoke -ModuleName VmBuild New-VM -Times 1 -ParameterFilter { $Generation -eq 2 }
        Should -Invoke -ModuleName VmBuild Set-VMFirmware -ParameterFilter { $EnableSecureBoot -eq 'On' -and $SecureBootTemplate -eq 'MicrosoftWindows' }
        Should -Invoke -ModuleName VmBuild Enable-VMTPM -Times 1 -ParameterFilter { $VMName -eq 'DSO-X' }
        Should -Invoke -ModuleName VmBuild Connect-VMNetworkAdapter -Times 0
    }
    It 'ustawia dysk jako pierwsze urządzenie rozruchu przez nazwę VM' {
        New-TestVmMachine -VmName 'DSO-X' -VhdPath 'x.vhdx' -MemoryBytes 4GB -ProcessorCount 4
        Should -Invoke -ModuleName VmBuild Get-VMHardDiskDrive -Times 1 -ParameterFilter { $VMName -eq 'DSO-X' }
        Should -Invoke -ModuleName VmBuild Set-VMFirmware -Times 1 -ParameterFilter { $VMName -eq 'DSO-X' -and $null -ne $FirstBootDevice }
    }
    It '-WithNetwork podłącza Default Switch' {
        New-TestVmMachine -VmName 'DSO-X' -VhdPath 'x.vhdx' -MemoryBytes 4GB -ProcessorCount 4 -WithNetwork
        Should -Invoke -ModuleName VmBuild Connect-VMNetworkAdapter -Times 1 -ParameterFilter { $SwitchName -eq 'Default Switch' -and $VMName -eq 'DSO-X' }
    }
}

Describe 'Wait-TestVmFirstBoot' -Skip:(-not $script:HasHyperV) {
    BeforeEach {
        $script:Cred = [pscredential]::new('Administrator', (ConvertTo-SecureString 'x' -AsPlainText -Force))
        Mock -ModuleName VmBuild Start-Sleep {}
        Mock -ModuleName VmBuild Invoke-Command { [pscustomobject]@{ Marker = $true; Leftover = @() } }
        Mock -ModuleName VmBuild Stop-VM {}
        Mock -ModuleName VmBuild Get-VM { [pscustomobject]@{ State = 'Off' } }
    }
    It 'po znaczniku gotowości wyłącza VM i czeka na Off' {
        Wait-TestVmFirstBoot -VmName 'DSO-X' -Credential $script:Cred
        Should -Invoke -ModuleName VmBuild Invoke-Command -ParameterFilter { $VMName -eq 'DSO-X' }
        Should -Invoke -ModuleName VmBuild Stop-VM -Times 1 -ParameterFilter { $Name -eq 'DSO-X' -and $Force }
    }
    It 'timeout bez znacznika nie wyłącza VM' {
        Mock -ModuleName VmBuild Invoke-Command { [pscustomobject]@{ Marker = $false; Leftover = @() } }
        { Wait-TestVmFirstBoot -VmName 'DSO-X' -Credential $script:Cred -TimeoutMinutes 0.001 } | Should -Throw -ExpectedMessage '*Przekroczono*'
        Should -Invoke -ModuleName VmBuild Stop-VM -Times 0
    }
    It 'błąd PowerShell Direct podczas startu jest ponawiany' {
        $script:Calls = 0
        Mock -ModuleName VmBuild Invoke-Command { $script:Calls++; if ($script:Calls -lt 3) { throw 'jeszcze nie' } else { [pscustomobject]@{ Marker = $true; Leftover = @() } } }
        { Wait-TestVmFirstBoot -VmName 'DSO-X' -Credential $script:Cred } | Should -Not -Throw
        Should -Invoke -ModuleName VmBuild Stop-VM -Times 1
    }
    It 'timeout dołącza ostatni błąd PowerShell Direct' {
        Mock -ModuleName VmBuild Invoke-Command { throw 'sesja jeszcze niegotowa' }
        { Wait-TestVmFirstBoot -VmName 'DSO-X' -Credential $script:Cred -TimeoutMinutes 0.001 } | Should -Throw -ExpectedMessage '*sesja jeszcze niegotowa*'
    }
    It 'timeout pokazuje treść błędu FirstLogon z VM' {
        Mock -ModuleName VmBuild Invoke-Command { [pscustomobject]@{ Marker = $false; Leftover = @(); FirstLogonError = 'Nie usunięto plików z hasłem' } }
        { Wait-TestVmFirstBoot -VmName 'DSO-X' -Credential $script:Cred -TimeoutMinutes 0.001 } | Should -Throw -ExpectedMessage '*FirstLogon*Nie usunięto*'
    }
    It 'pozostałe pliki z hasłem są usuwane, potem ponowne sprawdzenie' {
        $script:Calls = 0
        Mock -ModuleName VmBuild Invoke-Command {
            $script:Calls++
            if ($script:Calls -eq 1) { [pscustomobject]@{ Marker = $true; Leftover = @('C:\Windows\Panther\unattend.xml') } }
            else { [pscustomobject]@{ Marker = $true; Leftover = @() } }
        }
        Wait-TestVmFirstBoot -VmName 'DSO-X' -Credential $script:Cred
        Should -Invoke -ModuleName VmBuild Invoke-Command -Times 1 -ParameterFilter { $ArgumentList -contains $true }
        Should -Invoke -ModuleName VmBuild Stop-VM -Times 1
    }
    It 'nieusuwalne pliki z hasłem dają wyjątek i VM nie jest wyłączana' {
        Mock -ModuleName VmBuild Invoke-Command { [pscustomobject]@{ Marker = $true; Leftover = @('C:\Windows\Panther\unattend.xml') } }
        { Wait-TestVmFirstBoot -VmName 'DSO-X' -Credential $script:Cred } | Should -Throw -ExpectedMessage '*plików z hasłem*'
        Should -Invoke -ModuleName VmBuild Stop-VM -Times 0
    }
}

Describe 'Assert-TestVmDiskIdentity' {
    BeforeEach {
        $script:Vhd = Join-Path $TestDrive 'a.vhdx'
        $script:Good = [pscustomobject]@{ Number = 7; BusType = 'File Backed Virtual'; Location = $script:Vhd.ToUpperInvariant(); PartitionStyle = 'RAW' }
    }
    It 'akceptuje właściwy dysk (wielkość liter ścieżki bez znaczenia)' {
        InModuleScope VmBuild -Parameters @{ D = $script:Good; V = $script:Vhd } { param($D, $V) { Assert-TestVmDiskIdentity -Disk $D -VhdPath $V } | Should -Not -Throw }
    }
    It 'odrzuca zły BusType' {
        $script:Good.BusType = 'SAS'
        InModuleScope VmBuild -Parameters @{ D = $script:Good; V = $script:Vhd } { param($D, $V) { Assert-TestVmDiskIdentity -Disk $D -VhdPath $V } | Should -Throw -ExpectedMessage '*BusType*' }
    }
    It 'odrzuca inną lokalizację' {
        $script:Good.Location = 'C:\inny.vhdx'
        InModuleScope VmBuild -Parameters @{ D = $script:Good; V = $script:Vhd } { param($D, $V) { Assert-TestVmDiskIdentity -Disk $D -VhdPath $V } | Should -Throw -ExpectedMessage '*wskazuje*' }
    }
    It 'odrzuca dysk niepusty' {
        $script:Good.PartitionStyle = 'GPT'
        InModuleScope VmBuild -Parameters @{ D = $script:Good; V = $script:Vhd } { param($D, $V) { Assert-TestVmDiskIdentity -Disk $D -VhdPath $V } | Should -Throw -ExpectedMessage '*nie jest pusty*' }
    }
    It 'odrzuca wiele dysków i brak dysku' {
        InModuleScope VmBuild -Parameters @{ D = $script:Good; V = $script:Vhd } { param($D, $V) { Assert-TestVmDiskIdentity -Disk @($D, $D) -VhdPath $V } | Should -Throw -ExpectedMessage '*jednego dysku*' }
        InModuleScope VmBuild -Parameters @{ V = $script:Vhd } { param($V) { Assert-TestVmDiskIdentity -Disk $null -VhdPath $V } | Should -Throw -ExpectedMessage '*jednego dysku*' }
    }
}

Describe 'Invoke-TestVmBcdboot' {
    It 'przekazuje /nofirmwaresync, /f UEFI i właściwe ścieżki' {
        Mock -ModuleName VmBuild Invoke-TestVmNative { [pscustomobject]@{ ExitCode = 0; Output = 'ok' } }
        InModuleScope VmBuild { Invoke-TestVmBcdboot -WindowsRoot 'E:\' -SystemRoot 'F:\' }
        Should -Invoke -ModuleName VmBuild Invoke-TestVmNative -Times 1 -ParameterFilter {
            $FilePath -like '*bcdboot.exe' -and $ArgumentList -contains '/nofirmwaresync' -and $ArgumentList -contains 'UEFI' -and
            $ArgumentList[0] -eq 'E:\Windows' -and $ArgumentList[2] -eq 'F:'
        }
    }
    It 'kod różny od 0 daje wyjątek z treścią wyjścia' {
        Mock -ModuleName VmBuild Invoke-TestVmNative { [pscustomobject]@{ ExitCode = 5; Output = 'Failure when attempting to copy boot files.' } }
        InModuleScope VmBuild { { Invoke-TestVmBcdboot -WindowsRoot 'E:\' -SystemRoot 'F:\' } | Should -Throw -ExpectedMessage '*kodem 5*Failure when attempting*' }
    }
}
Describe 'Błędy niekończące przerywają budowę' -Skip:(-not $script:HasHyperV) {
    It 'New-TestVmMachine: Write-Error z Enable-VMTPM kończy się wyjątkiem' {
        foreach ($Name in 'New-VM', 'Set-VMMemory', 'Set-VMProcessor', 'Set-VMKeyProtector', 'Set-VM', 'Connect-VMNetworkAdapter') {
            Mock -ModuleName VmBuild -CommandName $Name -MockWith { }
        }
        Mock -ModuleName VmBuild -CommandName Set-VMFirmware -MockWith { } -RemoveParameterType FirstBootDevice
        Mock -ModuleName VmBuild -CommandName Get-VMHardDiskDrive -MockWith { [pscustomobject]@{ Path = 'x.vhdx' } }
        # Prawdziwy cmdlet zgłasza błąd niekończący; atrapa w zakresie modułu przesłania go i honoruje -ErrorAction.
        InModuleScope VmBuild -Parameters @{ Body = $script:StubBody } {
            param($Body)
            Set-Item -Path 'Function:\Enable-VMTPM' -Value $Body
            try { { New-TestVmMachine -VmName 'DSO-X' -VhdPath 'x.vhdx' -MemoryBytes 4GB -ProcessorCount 4 } | Should -Throw -ExpectedMessage '*x*' }
            finally { Remove-Item -Path 'Function:\Enable-VMTPM' }
        }
    }
}

Describe 'New-DsoTestVm' -Skip:(-not $script:HasHyperV) {
    BeforeEach {
        Mock -ModuleName VmBuild Assert-TestVmHost {}
        Mock -ModuleName VmBuild Mount-DiskImage { [pscustomobject]@{ ImagePath = 'x.iso' } }
        Mock -ModuleName VmBuild Get-Volume { [pscustomobject]@{ DriveLetter = 'Z' } } -RemoveParameterType DiskImage
        Mock -ModuleName VmBuild Dismount-DiskImage {}
        Mock -ModuleName VmBuild Resolve-InstallImage { 'Z:\sources\install.wim' }
        Mock -ModuleName VmBuild Get-WindowsImage { [pscustomobject]@{ ImageName = 'Windows 11 Pro' } }
        Mock -ModuleName VmBuild Save-DsoVmCredential { 'cred.xml' }
        Mock -ModuleName VmBuild Get-DsoVmCredential { [pscredential]::new('Administrator', (ConvertTo-SecureString 'x' -AsPlainText -Force)) }
        Mock -ModuleName VmBuild New-TestVmDisk {}
        Mock -ModuleName VmBuild New-TestVmMachine {}
        Mock -ModuleName VmBuild Start-VM {}
        Mock -ModuleName VmBuild Wait-TestVmFirstBoot {}
        Mock -ModuleName VmBuild Checkpoint-VM {}
        Mock -ModuleName VmBuild Get-VM { $null }
        Mock -ModuleName VmBuild Write-Host {}
        Mock -ModuleName VmBuild Get-DiskImage { [pscustomobject]@{ Attached = $false } }
        $script:Dir = Join-Path $TestDrive ([guid]::NewGuid().ToString())
        New-Item -ItemType Directory -Path $script:Dir | Out-Null
    }
    It 'tworzy checkpoint czysta-26H2 po pierwszym starcie' {
        New-DsoTestVm -IsoPath 'x.iso' -Targets (Get-TestVmTargets -VmName 'DSO-X' -EditionIndex 5) -VhdDirectory $script:Dir
        Should -Invoke -ModuleName VmBuild Checkpoint-VM -Times 1 -ParameterFilter { $SnapshotName -eq 'czysta-26H2' -and $Name -eq 'DSO-X' }
        Should -Invoke -ModuleName VmBuild Dismount-DiskImage -Times 1
    }
    It 'odmawia, gdy VM już istnieje (nic nie tworzy, nie zapisuje poświadczeń)' {
        Mock -ModuleName VmBuild Get-VM { [pscustomobject]@{ Name = 'DSO-X' } }
        { New-DsoTestVm -IsoPath 'x.iso' -Targets (Get-TestVmTargets -VmName 'DSO-X' -EditionIndex 5) -VhdDirectory $script:Dir } | Should -Throw -ExpectedMessage '*istnieje*'
        Should -Invoke -ModuleName VmBuild New-TestVmDisk -Times 0
        Should -Invoke -ModuleName VmBuild Save-DsoVmCredential -Times 0
    }
    It 'nie odmontowuje ISO, które było już podłączone' {
        Mock -ModuleName VmBuild Get-DiskImage { [pscustomobject]@{ Attached = $true } }
        New-DsoTestVm -IsoPath 'x.iso' -Targets (Get-TestVmTargets -VmName 'DSO-X' -EditionIndex 5) -VhdDirectory $script:Dir
        Should -Invoke -ModuleName VmBuild Dismount-DiskImage -Times 0
    }
    It 'odmawia, gdy VHDX już istnieje (nie nadpisuje poświadczeń)' {
        New-Item -ItemType File -Path (Join-Path $script:Dir 'DSO-X.vhdx') -Force | Out-Null
        { New-DsoTestVm -IsoPath 'x.iso' -Targets (Get-TestVmTargets -VmName 'DSO-X' -EditionIndex 5) -VhdDirectory $script:Dir } | Should -Throw -ExpectedMessage '*istnieje*'
        Should -Invoke -ModuleName VmBuild New-TestVmDisk -Times 0
        Should -Invoke -ModuleName VmBuild Save-DsoVmCredential -Times 0
        Should -Invoke -ModuleName VmBuild Mount-DiskImage -Times 0
    }
    It 'odmontowuje ISO także po błędzie dysku' {
        Mock -ModuleName VmBuild New-TestVmDisk { throw 'boom' }
        { New-DsoTestVm -IsoPath 'x.iso' -Targets (Get-TestVmTargets -VmName 'DSO-X' -EditionIndex 5) -VhdDirectory $script:Dir } | Should -Throw
        Should -Invoke -ModuleName VmBuild Dismount-DiskImage -Times 1
    }
    It 'nieistniejący index edycji daje błąd przed tworzeniem dysku i zapisem poświadczeń' {
        Mock -ModuleName VmBuild Get-WindowsImage { throw 'brak indeksu' }
        { New-DsoTestVm -IsoPath 'x.iso' -Targets (Get-TestVmTargets -VmName 'DSO-X' -EditionIndex 9) -VhdDirectory $script:Dir } | Should -Throw
        Should -Invoke -ModuleName VmBuild New-TestVmDisk -Times 0
        Should -Invoke -ModuleName VmBuild Save-DsoVmCredential -Times 0
    }
    It 'hasło nie trafia na żaden strumień wyjścia' {
        Mock -ModuleName VmBuild New-DsoVmPassword { 'SeKr3t-Pass-123' }
        $Written = New-Object System.Collections.ArrayList
        Mock -ModuleName VmBuild Write-Host { $null = $Written.Add([string]$Object) }
        $All = & {
            New-DsoTestVm -IsoPath 'x.iso' -Targets (Get-TestVmTargets -VmName 'DSO-X' -EditionIndex 5) -VhdDirectory $script:Dir -Verbose
        } *>&1 | Out-String
        $Written.Count | Should -BeGreaterThan 0
        ($All + ($Written -join ' ')) | Should -Not -Match 'SeKr3t-Pass-123'
        Should -Invoke -ModuleName VmBuild Save-DsoVmCredential -Times 1 -ParameterFilter { $Password -eq 'SeKr3t-Pass-123' }
    }
    It 'przekazuje do dysku unattend z nazwą komputera i skrypty' {
        New-DsoTestVm -IsoPath 'x.iso' -Targets (Get-TestVmTargets -VmName 'DSO-X' -EditionIndex 5) -VhdDirectory $script:Dir
        Should -Invoke -ModuleName VmBuild New-TestVmDisk -Times 1 -ParameterFilter {
            $EditionIndex -eq 5 -and $UnattendXml -match '<ComputerName>DSO-X</ComputerName>' -and $SpecializeScript -match 'S-1-5-32-544' -and $FirstLogonScript -match 'dso-vm-ready'
        }
    }
}

Describe 'Błąd niekończący Checkpoint-VM przerywa New-DsoTestVm' -Skip:(-not $script:HasHyperV) {
    It 'wyjątek zamiast fałszywego sukcesu' {
        foreach ($Name in 'Assert-TestVmHost', 'Dismount-DiskImage', 'Save-DsoVmCredential', 'New-TestVmDisk', 'New-TestVmMachine', 'Start-VM', 'Wait-TestVmFirstBoot', 'Write-Host') {
            Mock -ModuleName VmBuild -CommandName $Name -MockWith { }
        }
        Mock -ModuleName VmBuild Get-DiskImage { [pscustomobject]@{ Attached = $false } }
        Mock -ModuleName VmBuild Mount-DiskImage { [pscustomobject]@{ ImagePath = 'x.iso' } }
        Mock -ModuleName VmBuild Get-Volume { [pscustomobject]@{ DriveLetter = 'Z' } } -RemoveParameterType DiskImage
        Mock -ModuleName VmBuild Resolve-InstallImage { 'Z:\sources\install.wim' }
        Mock -ModuleName VmBuild Get-WindowsImage { [pscustomobject]@{ ImageName = 'Windows 11 Pro' } }
        Mock -ModuleName VmBuild Get-DsoVmCredential { [pscredential]::new('Administrator', (ConvertTo-SecureString 'x' -AsPlainText -Force)) }
        Mock -ModuleName VmBuild Get-VM { $null }
        $Dir = Join-Path $TestDrive ([guid]::NewGuid().ToString())
        New-Item -ItemType Directory -Path $Dir | Out-Null
        # Brak mocka Checkpoint-VM: atrapa w zakresie modułu zgłasza błąd niekończący i honoruje -ErrorAction.
        InModuleScope VmBuild -Parameters @{ Body = $script:StubBody; Dir = $Dir } {
            param($Body, $Dir)
            Set-Item -Path 'Function:\Checkpoint-VM' -Value $Body
            try { { New-DsoTestVm -IsoPath 'x.iso' -Targets (Get-TestVmTargets -VmName 'DSO-X' -EditionIndex 5) -VhdDirectory $Dir } | Should -Throw -ExpectedMessage '*x*' }
            finally { Remove-Item -Path 'Function:\Checkpoint-VM' }
        }
    }
}
Describe 'Pliki taska 12 (kodowanie)' {
    It 'ps1/psm1 mają BOM i CRLF, unattend.xml ma CRLF' -ForEach @(
        @{ Rel = 'modules\VmBuild.psm1'; Bom = $true }
        @{ Rel = 'templates\Specialize.ps1'; Bom = $true }
        @{ Rel = 'templates\FirstLogon.ps1'; Bom = $true }
        @{ Rel = 'templates\unattend.xml'; Bom = $false }
        @{ Rel = 'New-TestVm.ps1'; Bom = $true }
    ) {
        $Bytes = [System.IO.File]::ReadAllBytes((Join-Path (Join-Path $PSScriptRoot '..') $Rel))
        if ($Bom) { $Bytes[0..2] | Should -Be @(0xEF, 0xBB, 0xBF) }
        $Text = [System.Text.Encoding]::UTF8.GetString($Bytes)
        $Text | Should -Not -Match "(?<!\r)\n"
        $Text | Should -Match "\r\n"
    }
    It 'New-TestVm.ps1 parsuje się bez błędów' {
        $Errors = $null
        [void][System.Management.Automation.Language.Parser]::ParseFile((Join-Path $PSScriptRoot '..\New-TestVm.ps1'), [ref]$null, [ref]$Errors)
        $Errors | Should -BeNullOrEmpty
    }
}

AfterAll { Clear-NoLiveVm }
