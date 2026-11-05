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

    # Fikstury list obrazów z ISO (Index, Name, EditionId, Languages, Version).
    $NewImage = { param($Index, $Name, $EditionId, $Language) [pscustomobject]@{ Index = [int]$Index; Name = $Name; EditionId = $EditionId; Languages = [string[]]@($Language); Version = '10.0.26200' } }
    $script:EnImages = @(
        (& $NewImage 1 'Windows 11 Home' 'Core' 'en-US')
        (& $NewImage 2 'Windows 11 Home N' 'CoreN' 'en-US')
        (& $NewImage 3 'Windows 11 Home Single Language' 'CoreSingleLanguage' 'en-US')
        (& $NewImage 4 'Windows 11 Education' 'Education' 'en-US')
        (& $NewImage 5 'Windows 11 Education N' 'EducationN' 'en-US')
        (& $NewImage 6 'Windows 11 Pro' 'Professional' 'en-US')
        (& $NewImage 7 'Windows 11 Pro N' 'ProfessionalN' 'en-US')
    )
    $script:PlImages = @(
        (& $NewImage 1 'Windows 11 Home' 'Core' 'pl-PL')
        (& $NewImage 2 'Windows 11 Home N' 'CoreN' 'pl-PL')
        (& $NewImage 3 'Windows 11 Education' 'Education' 'pl-PL')
        (& $NewImage 4 'Windows 11 Education N' 'EducationN' 'pl-PL')
        (& $NewImage 5 'Windows 11 Pro' 'Professional' 'pl-PL')
        (& $NewImage 6 'Windows 11 Pro N' 'ProfessionalN' 'pl-PL')
    )
}

Describe 'Get-TestVmTargets' {
    It 'cel z jednego źródła: DSO-EN-Pro' {
        $T = Get-TestVmTargets -VmName 'DSO-EN-Pro'
        $T.ComputerName | Should -BeExactly 'DSO-EN-PRO'
        $T.EditionId | Should -BeExactly 'Professional'
        $T.Culture | Should -Be 'en-US'
        $T.InputLocale | Should -Be '0409:00000409'
    }
    It 'DSO-PL-Home: Core, pl-PL, 0415:00000415' {
        $T = Get-TestVmTargets -VmName 'DSO-PL-Home'
        $T.EditionId | Should -BeExactly 'Core'
        $T.Culture | Should -Be 'pl-PL'
        $T.InputLocale | Should -Be '0415:00000415'
    }
    It 'kilka nazw w podanej kolejności' { (Get-TestVmTargets -VmName 'DSO-PL-Home', 'DSO-PL-Pro').VmName | Should -Be @('DSO-PL-Home', 'DSO-PL-Pro') }
    It 'Get-TestVmTargets: nazwa kanoniczna' { (Get-TestVmTargets -VmName 'dso-pl-pro').VmName | Should -BeExactly 'DSO-PL-Pro' }
    It 'stara nazwa = wyjątek' { { Get-TestVmTargets -VmName 'DSO-26H2-Pro' } | Should -Throw '*Nieznana VM*' }
    It 'powtórzona nazwa = wyjątek' { { Get-TestVmTargets -VmName 'DSO-EN-Pro', 'dso-en-pro' } | Should -Throw }
    It 'bez parametru EditionIndex' { (Get-Command Get-TestVmTargets).Parameters.ContainsKey('EditionIndex') | Should -BeFalse }
    It 'VmName jest obowiązkowy' { (Get-Command Get-TestVmTargets).Parameters['VmName'].Attributes.Where({ $_ -is [System.Management.Automation.ParameterAttribute] }).Mandatory | Should -BeTrue }
}

Describe 'New-UnattendXml' {
    BeforeAll { $script:Ns = @{ u = 'urn:schemas-microsoft-com:unattend' } }
    It 'jest poprawnym XML z nazwą komputera, autologonem dso i pominiętym OOBE' {
        [xml]$Xml = New-UnattendXml -ComputerName 'DSO-EN-PRO' -Password 'a&b<c' -Culture 'en-US' -InputLocale '0409:00000409'
        (Select-Xml -Xml $Xml -Namespace $script:Ns -XPath '//u:ComputerName').Node.InnerText | Should -Be 'DSO-EN-PRO'
        (Select-Xml -Xml $Xml -Namespace $script:Ns -XPath '//u:AutoLogon/u:Username').Node.InnerText | Should -Be 'dso'
        (Select-Xml -Xml $Xml -Namespace $script:Ns -XPath '//u:AutoLogon/u:Password/u:Value').Node.InnerText | Should -Be 'a&b<c'
        (Select-Xml -Xml $Xml -Namespace $script:Ns -XPath '//u:HideOnlineAccountScreens').Node.InnerText | Should -Be 'true'
        $Xml.OuterXml | Should -Match 'Specialize\.ps1'
        $Xml.OuterXml | Should -Not -Match '__[A-Z_]+__'
    }
    It 'EN: InputLocale 0409:00000409, SystemLocale/UILanguage/UserLocale en-US' {
        [xml]$Xml = New-UnattendXml -ComputerName 'DSO-EN-PRO' -Password 'admin' -Culture 'en-US' -InputLocale '0409:00000409'
        (Select-Xml -Xml $Xml -Namespace $script:Ns -XPath '//u:InputLocale').Node.InnerText | Should -Be '0409:00000409'
        foreach ($Name in 'SystemLocale', 'UILanguage', 'UserLocale') {
            (Select-Xml -Xml $Xml -Namespace $script:Ns -XPath "//u:$Name").Node.InnerText | Should -Be 'en-US'
        }
    }
    It 'PL: 0415:00000415 i pl-PL' {
        [xml]$Xml = New-UnattendXml -ComputerName 'DSO-PL-PRO' -Password 'admin' -Culture 'pl-PL' -InputLocale '0415:00000415'
        (Select-Xml -Xml $Xml -Namespace $script:Ns -XPath '//u:InputLocale').Node.InnerText | Should -Be '0415:00000415'
        foreach ($Name in 'SystemLocale', 'UILanguage', 'UserLocale') {
            (Select-Xml -Xml $Xml -Namespace $script:Ns -XPath "//u:$Name").Node.InnerText | Should -Be 'pl-PL'
        }
    }
    It 'strefa Central European Standard Time i brak ProductKey w obu' {
        foreach ($Pair in @(@('en-US', '0409:00000409'), @('pl-PL', '0415:00000415'))) {
            [xml]$Xml = New-UnattendXml -ComputerName 'DSO-X' -Password 'admin' -Culture $Pair[0] -InputLocale $Pair[1]
            (Select-Xml -Xml $Xml -Namespace $script:Ns -XPath '//u:TimeZone').Node.InnerText | Should -Be 'Central European Standard Time'
            $Xml.OuterXml | Should -Not -Match 'ProductKey'
        }
    }
    It 'zły format Culture albo InputLocale = wyjątek' {
        { New-UnattendXml -ComputerName 'DSO-X' -Password 'x' -Culture 'en' -InputLocale '0409:00000409' } | Should -Throw
        { New-UnattendXml -ComputerName 'DSO-X' -Password 'x' -Culture 'en-US' -InputLocale '0409' } | Should -Throw
    }
    It 'hasło z podwójnymi podkreśleniami nie wywołuje fałszywego alarmu o tokenie' {
        { New-UnattendXml -ComputerName 'DSO-X' -Password 'A__B__c' -Culture 'en-US' -InputLocale '0409:00000409' } | Should -Not -Throw
    }
    It 'szablon z nieznanym tokenem daje wyjątek' {
        $Path = Join-Path $TestDrive 'bad.xml'
        Set-Content -LiteralPath $Path -Value '<a>__COMPUTER_NAME__ __PASSWORD__ __INNY__</a>'
        { New-UnattendXml -ComputerName 'DSO-X' -Password 'x' -Culture 'en-US' -InputLocale '0409:00000409' -TemplatePath $Path } | Should -Throw -ExpectedMessage '*token*'
    }
    It 'szablon bez nowych tokenów, ale z niepodstawionym __CULTURE__ w innym miejscu, nie przechodzi' {
        $Path = Join-Path $TestDrive 'badc.xml'
        Set-Content -LiteralPath $Path -Value '<a>__COMPUTER_NAME__ __PASSWORD__ __INPUT_LOCALE__ __CULTURE__ __INNY__</a>'
        { New-UnattendXml -ComputerName 'DSO-X' -Password 'x' -Culture 'en-US' -InputLocale '0409:00000409' -TemplatePath $Path } | Should -Throw -ExpectedMessage '*__INNY__*'
    }
    It 'szablon bez tokenu hasła daje wyjątek' {
        $Path = Join-Path $TestDrive 'nopass.xml'
        Set-Content -LiteralPath $Path -Value '<a>__COMPUTER_NAME__</a>'
        { New-UnattendXml -ComputerName 'DSO-X' -Password 'x' -Culture 'en-US' -InputLocale '0409:00000409' -TemplatePath $Path } | Should -Throw
    }
}

Describe 'Select-TestVmImage' {
    It 'ISO en-US daje Pro z indeksu 6, nie 5 (Education N)' { (Select-TestVmImage -Images $script:EnImages -EditionId 'Professional' -Culture 'en-US').Index | Should -Be 6 }
    It 'Home = Core, nie CoreSingleLanguage' { (Select-TestVmImage -Images $script:EnImages -EditionId 'Core' -Culture 'en-US').Index | Should -Be 1 }
    It 'ISO pl-PL daje Pro z indeksu 5' { (Select-TestVmImage -Images $script:PlImages -EditionId 'Professional' -Culture 'pl-PL').Index | Should -Be 5 }
    It 'brak obrazu albo dwa = wyjątek' {
        { Select-TestVmImage -Images $script:EnImages -EditionId 'Enterprise' -Culture 'en-US' } | Should -Throw '*dokładnie jeden*'
        { Select-TestVmImage -Images @($script:EnImages[5], $script:EnImages[5]) -EditionId 'Professional' -Culture 'en-US' } | Should -Throw "*znaleziono 2*dokładnie jeden*Nic nie zmieniono.*"
    }
    It 'same ProfessionalN = wyjątek (porównanie dokładne)' {
        { Select-TestVmImage -Images @($script:EnImages[6]) -EditionId 'Professional' -Culture 'en-US' } | Should -Throw "*'Professional'*znaleziono 0*"
    }
    It 'wielkość liter EditionId ma znaczenie' {
        { Select-TestVmImage -Images $script:EnImages -EditionId 'professional' -Culture 'en-US' } | Should -Throw '*dokładnie jeden*'
    }
    It 'obraz en-GB nie jest en-US' {
        $Gb = $script:EnImages | ForEach-Object { [pscustomobject]@{ Index = $_.Index; Name = $_.Name; EditionId = $_.EditionId; Languages = [string[]]@('en-GB'); Version = $_.Version } }
        { Select-TestVmImage -Images $Gb -EditionId 'Professional' -Culture 'en-US' } | Should -Throw "*'en-US'*en-GB*Nic nie zmieniono.*"
    }
    It 'ISO pl-PL dla VM EN = wyjątek' {
        { Select-TestVmImage -Images $script:PlImages -EditionId 'Professional' -Culture 'en-US' } | Should -Throw "*nie zawiera języka 'en-US'*pl-PL*"
    }
}

Describe 'Get-TestVmImageInfo' {
    It 'czyta listę, potem każdy indeks osobno' {
        Mock -ModuleName VmBuild Get-WindowsImage {
            if ($Index) {
                [pscustomobject]@{ ImageIndex = $Index; ImageName = "Obraz $Index"; EditionId = 'Professional'; Languages = @('en-US'); Version = '10.0.26200' }
            }
            else {
                @([pscustomobject]@{ ImageIndex = 1; ImageName = 'Obraz 1' }, [pscustomobject]@{ ImageIndex = 2; ImageName = 'Obraz 2' })
            }
        }
        $Result = @(Get-TestVmImageInfo -ImagePath 'Z:\sources\install.wim')
        $Result.Count | Should -Be 2
        $Result[1].Index | Should -Be 2
        $Result[1].Name | Should -Be 'Obraz 2'
        $Result[1].EditionId | Should -Be 'Professional'
        @($Result[1].Languages) | Should -Be @('en-US')
        $Result[1].Version | Should -Be '10.0.26200'
        Should -Invoke -ModuleName VmBuild Get-WindowsImage -Times 1 -ParameterFilter { -not $Index }
        Should -Invoke -ModuleName VmBuild Get-WindowsImage -Times 2 -ParameterFilter { $Index -gt 0 }
    }
}

Describe 'Assert-TestVmBuildFacts' {
    BeforeAll {
        $script:Target = @{ VmName = 'DSO-EN-Pro'; EditionId = 'Professional'; Culture = 'en-US' }
        $script:Facts = [pscustomobject]@{ EditionId = 'Professional'; CurrentBuild = '26200'; Ubr = 1; UiLanguage = 'en-US'; SystemLocale = 'en-US' }
    }
    It 'zgodne fakty przechodzą' { { Assert-TestVmBuildFacts -Facts $script:Facts -Target $script:Target } | Should -Not -Throw }
    It 'EducationN zamiast Professional = wyjątek z zakończeniem o checkpoincie' {
        $F = [pscustomobject]@{ EditionId = 'EducationN'; CurrentBuild = '26200'; Ubr = 1; UiLanguage = 'en-US'; SystemLocale = 'en-US' }
        { Assert-TestVmBuildFacts -Facts $F -Target $script:Target } | Should -Throw '*EducationN*VM zostaje bez checkpointu; nic nie usunięto.'
    }
    It 'ProfessionalN ≠ Professional' {
        $F = [pscustomobject]@{ EditionId = 'ProfessionalN'; CurrentBuild = '26200'; Ubr = 1; UiLanguage = 'en-US'; SystemLocale = 'en-US' }
        { Assert-TestVmBuildFacts -Facts $F -Target $script:Target } | Should -Throw
    }
    It 'zły język interfejsu = wyjątek' {
        $F = [pscustomobject]@{ EditionId = 'Professional'; CurrentBuild = '26200'; Ubr = 1; UiLanguage = 'pl-PL'; SystemLocale = 'en-US' }
        { Assert-TestVmBuildFacts -Facts $F -Target $script:Target } | Should -Throw '*pl-PL*VM zostaje bez checkpointu; nic nie usunięto.'
    }
}

Describe 'Save-TestVmBuildReport' {
    BeforeEach { $script:OldLocal = $env:LOCALAPPDATA; $env:LOCALAPPDATA = $TestDrive }
    AfterEach { $env:LOCALAPPDATA = $script:OldLocal }
    It 'zapisuje JSON poza repo w DSOptimizer-vm\<VmName>.build.json' {
        $Report = [ordered]@{ vmName = 'DSO-EN-Pro'; isoPath = 'x.iso'; imageIndex = 6; imageName = 'Windows 11 Pro'; editionId = 'Professional'; currentBuild = '26200'; ubr = 1; uiLanguage = 'en-US'; systemLocale = 'en-US'; builtAt = '2026-11-05T10:00:00.0000000+01:00' }
        $Path = Save-TestVmBuildReport -VmName 'DSO-EN-Pro' -Report $Report
        $Path | Should -BeLike (Join-Path $TestDrive 'DSOptimizer-vm\DSO-EN-Pro.build.json')
        $Json = Get-Content -LiteralPath $Path -Raw -Encoding UTF8 | ConvertFrom-Json
        $Json.editionId | Should -Be 'Professional'
        $Json.imageIndex | Should -Be 6
        $Json.uiLanguage | Should -Be 'en-US'
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
        Mock -ModuleName VmBuild Invoke-Command { [pscustomobject]@{ Marker = $true; Leftover = @(); EditionId = 'Professional'; CurrentBuild = '26200'; UBR = 1234; UiLanguage = 'en-US'; SystemLocale = 'en-US' } }
        Mock -ModuleName VmBuild Stop-VM {}
        Mock -ModuleName VmBuild Get-VM { [pscustomobject]@{ State = 'Off' } }
    }
    It 'po znaczniku gotowości wyłącza VM i czeka na Off' {
        Wait-TestVmFirstBoot -VmName 'DSO-X' -Credential $script:Cred
        Should -Invoke -ModuleName VmBuild Invoke-Command -ParameterFilter { $VMName -eq 'DSO-X' }
        Should -Invoke -ModuleName VmBuild Stop-VM -Times 1 -ParameterFilter { $Name -eq 'DSO-X' -and $Force }
    }
    It 'Wait-TestVmFirstBoot zwraca EditionId, CurrentBuild, Ubr, UiLanguage, SystemLocale' {
        $Facts = Wait-TestVmFirstBoot -VmName 'DSO-X' -Credential $script:Cred
        $Facts.EditionId | Should -BeExactly 'Professional'
        $Facts.CurrentBuild | Should -Be '26200'
        $Facts.Ubr | Should -Be 1234
        $Facts.UiLanguage | Should -Be 'en-US'
        $Facts.SystemLocale | Should -Be 'en-US'
        Should -Invoke -ModuleName VmBuild Get-VM -ParameterFilter { $Name -eq 'DSO-X' }
    }
    It 'fakty pochodzą z ostatniego odczytu, a nie z pierwszego' {
        $script:Calls = 0
        Mock -ModuleName VmBuild Invoke-Command {
            $script:Calls++
            if ($script:Calls -eq 1) { [pscustomobject]@{ Marker = $true; Leftover = @('C:\Windows\Panther\unattend.xml'); EditionId = 'Pierwszy'; CurrentBuild = '1'; UBR = 1; UiLanguage = 'a'; SystemLocale = 'a' } }
            else { [pscustomobject]@{ Marker = $true; Leftover = @(); EditionId = 'Ostatni'; CurrentBuild = '2'; UBR = 2; UiLanguage = 'b'; SystemLocale = 'b' } }
        }
        (Wait-TestVmFirstBoot -VmName 'DSO-X' -Credential $script:Cred).EditionId | Should -BeExactly 'Ostatni'
    }
    It 'timeout bez znacznika nie wyłącza VM' {
        Mock -ModuleName VmBuild Invoke-Command { [pscustomobject]@{ Marker = $false; Leftover = @() } }
        { Wait-TestVmFirstBoot -VmName 'DSO-X' -Credential $script:Cred -TimeoutMinutes 0.001 } | Should -Throw -ExpectedMessage '*Przekroczono*'
        Should -Invoke -ModuleName VmBuild Stop-VM -Times 0
    }
    It 'błąd PowerShell Direct podczas startu jest ponawiany' {
        $script:Calls = 0
        Mock -ModuleName VmBuild Invoke-Command { $script:Calls++; if ($script:Calls -lt 3) { throw 'jeszcze nie' } else { [pscustomobject]@{ Marker = $true; Leftover = @(); EditionId = 'Professional'; CurrentBuild = '26200'; UBR = 1234; UiLanguage = 'en-US'; SystemLocale = 'en-US' } } }
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
            else { [pscustomobject]@{ Marker = $true; Leftover = @(); EditionId = 'Professional'; CurrentBuild = '26200'; UBR = 1234; UiLanguage = 'en-US'; SystemLocale = 'en-US' } }
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
        $Images = $script:EnImages
        $Facts = [pscustomobject]@{ EditionId = 'Professional'; CurrentBuild = '26200'; Ubr = 1234; UiLanguage = 'en-US'; SystemLocale = 'en-US' }
        Mock -ModuleName VmBuild Assert-TestVmHost {}
        Mock -ModuleName VmBuild Mount-DiskImage { [pscustomobject]@{ ImagePath = 'x.iso' } }
        Mock -ModuleName VmBuild Get-Volume { [pscustomobject]@{ DriveLetter = 'Z' } } -RemoveParameterType DiskImage
        Mock -ModuleName VmBuild Dismount-DiskImage {}
        Mock -ModuleName VmBuild Resolve-InstallImage { 'Z:\sources\install.wim' }
        Mock -ModuleName VmBuild Get-TestVmImageInfo ({ $Images }.GetNewClosure())
        Mock -ModuleName VmBuild Save-DsoVmCredential { 'cred.xml' }
        Mock -ModuleName VmBuild Get-DsoVmCredential { [pscredential]::new('Administrator', (ConvertTo-SecureString 'x' -AsPlainText -Force)) }
        Mock -ModuleName VmBuild New-TestVmDisk {}
        Mock -ModuleName VmBuild New-TestVmMachine {}
        Mock -ModuleName VmBuild Start-VM {}
        Mock -ModuleName VmBuild Wait-TestVmFirstBoot ({ $Facts }.GetNewClosure())
        Mock -ModuleName VmBuild Save-TestVmBuildReport { 'build.json' }
        Mock -ModuleName VmBuild Checkpoint-VM {}
        Mock -ModuleName VmBuild Get-VM { $null }
        Mock -ModuleName VmBuild Write-Host {}
        Mock -ModuleName VmBuild Get-DiskImage { [pscustomobject]@{ Attached = $false } }
        $script:Dir = Join-Path $TestDrive ([guid]::NewGuid().ToString())
        New-Item -ItemType Directory -Path $script:Dir | Out-Null
    }
    It 'tworzy checkpoint czysta-26H2 po pierwszym starcie' {
        New-DsoTestVm -IsoPath 'x.iso' -Targets (Get-TestVmTargets -VmName 'DSO-EN-Pro') -VhdDirectory $script:Dir
        Should -Invoke -ModuleName VmBuild Checkpoint-VM -Times 1 -ParameterFilter { $SnapshotName -eq 'czysta-26H2' -and $Name -eq 'DSO-EN-Pro' }
        Should -Invoke -ModuleName VmBuild Dismount-DiskImage -Times 1
    }
    It 'odmawia, gdy VM już istnieje (nic nie tworzy, nie zapisuje poświadczeń)' {
        Mock -ModuleName VmBuild Get-VM { [pscustomobject]@{ Name = 'DSO-EN-Pro' } }
        { New-DsoTestVm -IsoPath 'x.iso' -Targets (Get-TestVmTargets -VmName 'DSO-EN-Pro') -VhdDirectory $script:Dir } | Should -Throw -ExpectedMessage '*istnieje*'
        Should -Invoke -ModuleName VmBuild New-TestVmDisk -Times 0
        Should -Invoke -ModuleName VmBuild Save-DsoVmCredential -Times 0
    }
    It 'nie odmontowuje ISO, które było już podłączone' {
        Mock -ModuleName VmBuild Get-DiskImage { [pscustomobject]@{ Attached = $true } }
        New-DsoTestVm -IsoPath 'x.iso' -Targets (Get-TestVmTargets -VmName 'DSO-EN-Pro') -VhdDirectory $script:Dir
        Should -Invoke -ModuleName VmBuild Dismount-DiskImage -Times 0
    }
    It 'odmawia, gdy VHDX już istnieje (nie nadpisuje poświadczeń)' {
        New-Item -ItemType File -Path (Join-Path $script:Dir 'DSO-EN-Pro.vhdx') -Force | Out-Null
        { New-DsoTestVm -IsoPath 'x.iso' -Targets (Get-TestVmTargets -VmName 'DSO-EN-Pro') -VhdDirectory $script:Dir } | Should -Throw -ExpectedMessage '*istnieje*'
        Should -Invoke -ModuleName VmBuild New-TestVmDisk -Times 0
        Should -Invoke -ModuleName VmBuild Save-DsoVmCredential -Times 0
        Should -Invoke -ModuleName VmBuild Mount-DiskImage -Times 0
    }
    It 'odmontowuje ISO także po błędzie dysku' {
        Mock -ModuleName VmBuild New-TestVmDisk { throw 'boom' }
        { New-DsoTestVm -IsoPath 'x.iso' -Targets (Get-TestVmTargets -VmName 'DSO-EN-Pro') -VhdDirectory $script:Dir } | Should -Throw
        Should -Invoke -ModuleName VmBuild Dismount-DiskImage -Times 1
    }
    It 'brak obrazu o EditionId daje błąd przed tworzeniem dysku i zapisem poświadczeń' {
        $Images = @($script:EnImages[0..4])
        Mock -ModuleName VmBuild Get-TestVmImageInfo ({ $Images }.GetNewClosure())
        { New-DsoTestVm -IsoPath 'x.iso' -Targets (Get-TestVmTargets -VmName 'DSO-EN-Pro') -VhdDirectory $script:Dir } | Should -Throw '*dokładnie jeden*'
        Should -Invoke -ModuleName VmBuild New-TestVmDisk -Times 0
        Should -Invoke -ModuleName VmBuild Save-DsoVmCredential -Times 0
    }
    It 'hasło domyślne admin trafia do poświadczeń, unattend i Specialize' {
        New-DsoTestVm -IsoPath 'x.iso' -Targets (Get-TestVmTargets -VmName 'DSO-EN-Pro') -VhdDirectory $script:Dir
        Should -Invoke -ModuleName VmBuild Save-DsoVmCredential -Times 1 -ParameterFilter { $Password -ceq 'admin' }
        Should -Invoke -ModuleName VmBuild New-TestVmDisk -Times 1 -ParameterFilter { $UnattendXml -match '<Value>admin</Value>' -and $SpecializeScript -match "'admin'" }
    }
    It 'Password ma domyślnie admin i nie przyjmuje pustego' {
        $P = (Get-Command New-DsoTestVm).Parameters['Password']
        $P | Should -Not -BeNullOrEmpty
        { New-DsoTestVm -IsoPath 'x.iso' -Targets (Get-TestVmTargets -VmName 'DSO-EN-Pro') -Password '' } | Should -Throw
    }
    It 'przekazuje do dysku unattend z nazwą komputera, językiem celu i skrypty' {
        New-DsoTestVm -IsoPath 'x.iso' -Targets (Get-TestVmTargets -VmName 'DSO-EN-Pro') -VhdDirectory $script:Dir
        Should -Invoke -ModuleName VmBuild New-TestVmDisk -Times 1 -ParameterFilter {
            $UnattendXml -match '<ComputerName>DSO-EN-PRO</ComputerName>' -and $UnattendXml -match '<UILanguage>en-US</UILanguage>' -and $UnattendXml -match '<InputLocale>0409:00000409</InputLocale>' -and
            $SpecializeScript -match 'S-1-5-32-544' -and $FirstLogonScript -match 'dso-vm-ready'
        }
    }
    It 'Pro z ISO en-US nakłada indeks 6' {
        New-DsoTestVm -IsoPath 'x.iso' -Targets (Get-TestVmTargets -VmName 'DSO-EN-Pro') -VhdDirectory $script:Dir
        Should -Invoke -ModuleName VmBuild New-TestVmDisk -Times 1 -ParameterFilter { $EditionIndex -eq 6 }
    }
    It 'Home z ISO en-US nakłada indeks 1' {
        $Facts = [pscustomobject]@{ EditionId = 'Core'; CurrentBuild = '26200'; Ubr = 1; UiLanguage = 'en-US'; SystemLocale = 'en-US' }
        Mock -ModuleName VmBuild Wait-TestVmFirstBoot ({ $Facts }.GetNewClosure())
        New-DsoTestVm -IsoPath 'x.iso' -Targets (Get-TestVmTargets -VmName 'DSO-EN-Home') -VhdDirectory $script:Dir
        Should -Invoke -ModuleName VmBuild New-TestVmDisk -Times 1 -ParameterFilter { $EditionIndex -eq 1 }
    }
    It 'wypisuje wybrany obraz przed budową' {
        New-DsoTestVm -IsoPath 'x.iso' -Targets (Get-TestVmTargets -VmName 'DSO-EN-Pro') -VhdDirectory $script:Dir
        Should -Invoke -ModuleName VmBuild Write-Host -ParameterFilter { "$Object" -like "VM 'DSO-EN-Pro': obraz 6 'Windows 11 Pro' (EditionId Professional, języki en-US)*" }
    }
    It 'zły obraz dla drugiej VM z listy: błąd przed pierwszym dyskiem i poświadczeniem' {
        { New-DsoTestVm -IsoPath 'x.iso' -Targets (Get-TestVmTargets -VmName 'DSO-EN-Home', 'DSO-PL-Pro') -VhdDirectory $script:Dir } | Should -Throw
        Should -Invoke -ModuleName VmBuild New-TestVmDisk -Times 0
        Should -Invoke -ModuleName VmBuild Save-DsoVmCredential -Times 0
        Should -Invoke -ModuleName VmBuild New-TestVmMachine -Times 0
    }
    It 'fakty z VM niezgodne (EducationN): wyjątek, raport zapisany, bez Checkpoint-VM' {
        $Facts = [pscustomobject]@{ EditionId = 'EducationN'; CurrentBuild = '26200'; Ubr = 1; UiLanguage = 'en-US'; SystemLocale = 'en-US' }
        Mock -ModuleName VmBuild Wait-TestVmFirstBoot ({ $Facts }.GetNewClosure())
        { New-DsoTestVm -IsoPath 'x.iso' -Targets (Get-TestVmTargets -VmName 'DSO-EN-Pro') -VhdDirectory $script:Dir } | Should -Throw '*EducationN*'
        Should -Invoke -ModuleName VmBuild Save-TestVmBuildReport -Times 1
        Should -Invoke -ModuleName VmBuild Checkpoint-VM -Times 0
    }
    It 'raport budowy poza repo z EditionId, CurrentBuild, UBR i językiem' {
        New-DsoTestVm -IsoPath 'x.iso' -Targets (Get-TestVmTargets -VmName 'DSO-EN-Pro') -VhdDirectory $script:Dir
        Should -Invoke -ModuleName VmBuild Save-TestVmBuildReport -Times 1 -ParameterFilter {
            $VmName -eq 'DSO-EN-Pro' -and $Report.vmName -eq 'DSO-EN-Pro' -and $Report.imageIndex -eq 6 -and $Report.imageName -eq 'Windows 11 Pro' -and
            $Report.editionId -ceq 'Professional' -and $Report.currentBuild -eq '26200' -and $Report.ubr -eq 1234 -and $Report.uiLanguage -eq 'en-US' -and
            $Report.systemLocale -eq 'en-US' -and $Report.isoPath -like '*x.iso' -and $Report.builtAt
        }
    }
}

Describe 'Błąd niekończący Checkpoint-VM przerywa New-DsoTestVm' -Skip:(-not $script:HasHyperV) {
    It 'wyjątek zamiast fałszywego sukcesu' {
        foreach ($Name in 'Assert-TestVmHost', 'Dismount-DiskImage', 'Save-DsoVmCredential', 'Save-TestVmBuildReport', 'New-TestVmDisk', 'New-TestVmMachine', 'Start-VM', 'Write-Host') {
            Mock -ModuleName VmBuild -CommandName $Name -MockWith { }
        }
        $Images = $script:EnImages
        $Facts = [pscustomobject]@{ EditionId = 'Professional'; CurrentBuild = '26200'; Ubr = 1; UiLanguage = 'en-US'; SystemLocale = 'en-US' }
        Mock -ModuleName VmBuild Get-TestVmImageInfo ({ $Images }.GetNewClosure())
        Mock -ModuleName VmBuild Wait-TestVmFirstBoot ({ $Facts }.GetNewClosure())
        Mock -ModuleName VmBuild Get-DiskImage { [pscustomobject]@{ Attached = $false } }
        Mock -ModuleName VmBuild Mount-DiskImage { [pscustomobject]@{ ImagePath = 'x.iso' } }
        Mock -ModuleName VmBuild Get-Volume { [pscustomobject]@{ DriveLetter = 'Z' } } -RemoveParameterType DiskImage
        Mock -ModuleName VmBuild Resolve-InstallImage { 'Z:\sources\install.wim' }
        Mock -ModuleName VmBuild Get-DsoVmCredential { [pscredential]::new('Administrator', (ConvertTo-SecureString 'x' -AsPlainText -Force)) }
        Mock -ModuleName VmBuild Get-VM { $null }
        $Dir = Join-Path $TestDrive ([guid]::NewGuid().ToString())
        New-Item -ItemType Directory -Path $Dir | Out-Null
        # Brak mocka Checkpoint-VM: atrapa w zakresie modułu zgłasza błąd niekończący i honoruje -ErrorAction.
        InModuleScope VmBuild -Parameters @{ Body = $script:StubBody; Dir = $Dir } {
            param($Body, $Dir)
            Set-Item -Path 'Function:\Checkpoint-VM' -Value $Body
            try { { New-DsoTestVm -IsoPath 'x.iso' -Targets (Get-TestVmTargets -VmName 'DSO-EN-Pro') -VhdDirectory $Dir } | Should -Throw -ExpectedMessage '*x*' }
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
