BeforeDiscovery {
    $script:HasHyperV = [bool](Get-Module -ListAvailable -Name Hyper-V)
}

BeforeAll {
    # Wyłącznik: żaden test na mockach nie może dotknąć prawdziwego Hyper-V (patrz Support\NoLiveVm.ps1).
    . (Join-Path $PSScriptRoot 'Support\NoLiveVm.ps1')

    Import-Module (Join-Path $PSScriptRoot '..\modules\VmCommon.psm1') -Force
    Import-Module (Join-Path $PSScriptRoot '..\modules\VmRun.psm1') -Force
    Import-Module (Join-Path $PSScriptRoot '..\modules\VmHost.psm1') -Force
    $script:VmRoot = Join-Path $PSScriptRoot '..'
    # Nieinicjalizowany PSSession: przechodzi wiązanie parametrów Invoke-Command/Remove-PSSession w mockach, nie otwiera połączenia.
    $script:FakeSession = [System.Runtime.Serialization.FormatterServices]::GetUninitializedObject([System.Management.Automation.Runspaces.PSSession])
    $script:Credential = [pscredential]::new('Administrator', (ConvertTo-SecureString 'Test-Haslo-1!' -AsPlainText -Force))
}

Describe 'Restore-VmCleanState' -Skip:(-not $script:HasHyperV) {
    BeforeEach { Set-NoLiveVmDefaultMock -ModuleName VmHost; Mock -ModuleName VmHost Assert-DsoVmLiveAllowed {} }
    It 'wyłącza działającą VM przed przywróceniem' {
        Mock -ModuleName VmHost Get-VM { [pscustomobject]@{ Name = 'v'; State = 'Running' } }
        Mock -ModuleName VmHost Stop-VM {}
        Mock -ModuleName VmHost Restore-VMCheckpoint {}
        Restore-VmCleanState -VmName 'DSO-26H2-Test' -CheckpointName 'czysta-26H2'
        Should -Invoke -ModuleName VmHost Stop-VM -Times 1 -ParameterFilter { $TurnOff }
        Should -Invoke -ModuleName VmHost Restore-VMCheckpoint -Times 1 -ParameterFilter { $Name -eq 'czysta-26H2' }
    }
    It 'nie wyłącza VM, która już jest wyłączona' {
        Mock -ModuleName VmHost Get-VM { [pscustomobject]@{ Name = 'v'; State = 'Off' } }
        Mock -ModuleName VmHost Stop-VM {}
        Mock -ModuleName VmHost Restore-VMCheckpoint {}
        Restore-VmCleanState -VmName 'DSO-26H2-Test' -CheckpointName 'czysta-26H2'
        Should -Invoke -ModuleName VmHost Stop-VM -Times 0
        Should -Invoke -ModuleName VmHost Restore-VMCheckpoint -Times 1
    }
}

Describe 'Wait-VmReady' -Skip:(-not $script:HasHyperV) {
    BeforeEach { Set-NoLiveVmDefaultMock -ModuleName VmHost; Mock -ModuleName VmHost Assert-DsoVmLiveAllowed {}; Mock -ModuleName VmHost Get-VM { [pscustomobject]@{ Name = 'v'; State = 'Running'; Heartbeat = 'OkApplicationsHealthy' } } }
    It 'rzuca po przekroczeniu czasu' {
        Mock -ModuleName VmHost New-PSSession { throw 'brak' }
        Mock -ModuleName VmHost Start-Sleep {}
        { Wait-VmReady -VmName 'DSO-26H2-Test' -Credential $script:Credential -TimeoutSeconds 0 } | Should -Throw -ExpectedMessage '*v*'
    }
    It 'zwraca sesję, gdy explorer.exe ma właściciela dso' {
        Mock -ModuleName VmHost New-PSSession { $script:FakeSession }
        Mock -ModuleName VmHost Start-Sleep {}
        Mock -ModuleName VmHost Invoke-Command -RemoveParameterValidation 'Session' -MockWith { $true }
        $Session = Wait-VmReady -VmName 'DSO-26H2-Test' -Credential $script:Credential -TimeoutSeconds 60
        [object]::ReferenceEquals($Session, $script:FakeSession) | Should -BeTrue
    }
    It '-SkipInteractiveUserCheck: zwraca sesję bez wymagania explorer.exe użytkownika dso (próba gotowości to tylko PowerShell Direct)' {
        Mock -ModuleName VmHost New-PSSession { $script:FakeSession }
        Mock -ModuleName VmHost Start-Sleep {}
        # Wykonuje faktyczny blok próby lokalnie: z przełącznikiem to tylko `return $true` (bez Get-CimInstance explorer.exe).
        Mock -ModuleName VmHost Invoke-Command -RemoveParameterValidation 'Session' -MockWith { & $ScriptBlock }
        $Session = Wait-VmReady -VmName 'DSO-26H2-Test' -Credential $script:Credential -TimeoutSeconds 60 -SkipInteractiveUserCheck
        [object]::ReferenceEquals($Session, $script:FakeSession) | Should -BeTrue
    }
    It 'rzuca z nazwą VM, gdy explorer.exe nie ma właściciela dso, i zamyka sesję' {
        Mock -ModuleName VmHost New-PSSession { $script:FakeSession }
        Mock -ModuleName VmHost Start-Sleep {}
        Mock -ModuleName VmHost Invoke-Command -RemoveParameterValidation 'Session' -MockWith { $false }
        Mock -ModuleName VmHost Remove-PSSession -RemoveParameterValidation 'Session' -MockWith {}
        { Wait-VmReady -VmName 'DSO-26H2-Test' -Credential $script:Credential -TimeoutSeconds 1 } | Should -Throw -ExpectedMessage '*v*'
        Should -Invoke -ModuleName VmHost Remove-PSSession -Times 1 -ParameterFilter { $true }
    }
}

Describe 'Get-VmBootObservation' {
    It 'zwraca usługi w stanie Pending jako tablicę' {
        Mock -ModuleName VmHost Invoke-Command -RemoveParameterValidation 'Session' -MockWith {
            [pscustomobject]@{ Name = 'Svc'; State = 'Start Pending'; StartMode = 'Auto' }
        }
        $Result = Get-VmBootObservation -Session $null
        @($Result).Count | Should -Be 1
        $Result[0].Name | Should -Be 'Svc'
    }
    It 'brak usług Pending daje pustą tablicę' {
        Mock -ModuleName VmHost Invoke-Command -RemoveParameterValidation 'Session' -MockWith { $null }
        $Result = Get-VmBootObservation -Session $null
        @($Result).Count | Should -Be 0
    }
}

Describe 'Copy-DsoBuildToVm' {
    BeforeEach { Set-NoLiveVmDefaultMock -ModuleName VmHost; Mock -ModuleName VmHost Assert-DsoVmLiveAllowed {} }
    It 'czyści C:\dso w VM i kopiuje build oraz katalog do sesji' {
        Mock -ModuleName VmHost Invoke-Command -RemoveParameterValidation 'Session' -MockWith {}
        Mock -ModuleName VmHost Copy-Item -RemoveParameterValidation 'ToSession' -MockWith {}
        Copy-DsoBuildToVm -Session $null -BuildDirectory 'C:\b' -CatalogPath 'C:\k.json' -Layout (Get-DsoVmLayout)
        Should -Invoke -ModuleName VmHost Invoke-Command -Times 1
        Should -Invoke -ModuleName VmHost Copy-Item -Times 1 -ParameterFilter { $Path -eq 'C:\b\*' -and $Destination -eq 'C:\dso\app' -and $Recurse }
        Should -Invoke -ModuleName VmHost Copy-Item -Times 1 -ParameterFilter { $Path -eq 'C:\k.json' -and $Destination -eq 'C:\dso\vm-test-catalog.json' }
    }
}

Describe 'Invoke-DsoVmRun' -Skip:(-not $script:HasHyperV) {
    BeforeEach {
        Mock -ModuleName VmHost Restore-VmCleanState {}
        Mock -ModuleName VmHost Start-VM {}
        Mock -ModuleName VmHost Wait-VmReady { $null }
        Mock -ModuleName VmHost Get-VmBootObservation { @() }
        Mock -ModuleName VmHost Copy-DsoBuildToVm {}
        Mock -ModuleName VmHost Invoke-Command -RemoveParameterValidation 'Session' -MockWith { 'S-1-5-21-9' }
        Mock -ModuleName VmHost Copy-Item -RemoveParameterValidation 'FromSession' -MockWith {}
        Mock -ModuleName VmHost Remove-PSSession -RemoveParameterValidation 'Session' -MockWith {}
        Mock -ModuleName VmHost Invoke-Pester { [pscustomobject]@{ PassedCount = 3; FailedCount = 0; SkippedCount = 0; Failed = @() } }
    }
    It 'przywraca checkpoint na początku i na końcu' {
        Invoke-DsoVmRun -VmName 'DSO-26H2-Test' -Credential $script:Credential -BuildDirectory $TestDrive -CatalogPath "$TestDrive\k.json" -Cases @() -OutDirectory $TestDrive | Out-Null
        Should -Invoke -ModuleName VmHost Restore-VmCleanState -Times 2
    }
    It 'przywraca checkpoint także po błędzie Pestera' {
        Mock -ModuleName VmHost Invoke-Pester { throw 'boom' }
        { Invoke-DsoVmRun -VmName 'DSO-26H2-Test' -Credential $script:Credential -BuildDirectory $TestDrive -CatalogPath "$TestDrive\k.json" -Cases @() -OutDirectory $TestDrive } | Should -Throw
        Should -Invoke -ModuleName VmHost Restore-VmCleanState -Times 2
    }
    It 'nie woła Remove-PSSession dla pustej sesji' {
        Invoke-DsoVmRun -VmName 'DSO-26H2-Test' -Credential $script:Credential -BuildDirectory $TestDrive -CatalogPath "$TestDrive\k.json" -Cases @() -OutDirectory $TestDrive | Out-Null
        Should -Invoke -ModuleName VmHost Remove-PSSession -Times 0
    }
    It 'zamyka niepustą sesję' {
        Mock -ModuleName VmHost Wait-VmReady { $script:FakeSession }
        Invoke-DsoVmRun -VmName 'DSO-26H2-Test' -Credential $script:Credential -BuildDirectory $TestDrive -CatalogPath "$TestDrive\k.json" -Cases @() -OutDirectory $TestDrive | Out-Null
        Should -Invoke -ModuleName VmHost Remove-PSSession -Times 1
    }
    It 'zapisuje report.json z wynikiem Pestera' {
        $Run = Invoke-DsoVmRun -VmName 'DSO-26H2-Test' -Credential $script:Credential -BuildDirectory $TestDrive -CatalogPath "$TestDrive\k.json" -Cases @() -OutDirectory $TestDrive
        $Run.Passed | Should -Be 3
        $Run.VmName | Should -Be 'DSO-26H2-Test'
        (Get-Content $Run.ReportPath -Raw | ConvertFrom-Json).pester.passed | Should -Be 3
    }
    It 'w report.json zapisuje nazwy i błędy nieudanych testów' {
        Mock -ModuleName VmHost Invoke-Pester {
            [pscustomobject]@{
                PassedCount = 0; FailedCount = 1; SkippedCount = 0
                Failed      = @([pscustomobject]@{ ExpandedPath = 'przypadek.apply'; ErrorRecord = @([System.Management.Automation.ErrorRecord]::new([System.Exception]::new('zły stan'), 'e', 'NotSpecified', $null)) })
            }
        }
        $Run = Invoke-DsoVmRun -VmName 'DSO-26H2-Test' -Credential $script:Credential -BuildDirectory $TestDrive -CatalogPath "$TestDrive\k.json" -Cases @() -OutDirectory $TestDrive
        $Report = Get-Content $Run.ReportPath -Raw | ConvertFrom-Json
        $Run.Failed | Should -Be 1
        @($Report.pester.failedTests).Count | Should -Be 1
        $Report.pester.failedTests[0].name | Should -Be 'przypadek.apply'
        $Report.pester.failedTests[0].error | Should -BeLike '*zły stan*'
    }
    It 'nie zapisuje hasła w report.json' {
        $Run = Invoke-DsoVmRun -VmName 'DSO-26H2-Test' -Credential $script:Credential -BuildDirectory $TestDrive -CatalogPath "$TestDrive\k.json" -Cases @() -OutDirectory $TestDrive
        (Get-Content $Run.ReportPath -Raw) | Should -Not -BeLike '*Test-Haslo-1!*'
    }
}

Describe 'Format-VmRunSummary' {
    It 'jedna linia na VM' {
        Format-VmRunSummary -Runs @([pscustomobject]@{ VmName = 'A'; Passed = 5; Failed = 1; Skipped = 0; ReportPath = 'r.json' }) |
            Should -Be @('A: zaliczone 5, błędy 1, pominięte 0 – r.json')
    }
}

Describe 'Walidacja nazwy VM' -Skip:(-not $script:HasHyperV) {
    BeforeEach {
        Mock -ModuleName VmHost Get-VM { [pscustomobject]@{ Name = 'x'; State = 'Off' } }
        Mock -ModuleName VmHost Stop-VM {}
        Mock -ModuleName VmHost Start-VM {}
        Mock -ModuleName VmHost Restore-VMCheckpoint {}
        Mock -ModuleName VmHost New-PSSession { throw 'nie powinno być wołane' }
    }
    It 'Restore-VmCleanState odmawia dla <Nazwa> i nie woła Hyper-V' -ForEach @(
        @{ Nazwa = 'ObcaVM' }
        @{ Nazwa = 'DSO-*' }
    ) {
        { Restore-VmCleanState -VmName $Nazwa -CheckpointName 'czysta-26H2' } | Should -Throw
        Should -Invoke -ModuleName VmHost Get-VM -Times 0
        Should -Invoke -ModuleName VmHost Stop-VM -Times 0
        Should -Invoke -ModuleName VmHost Restore-VMCheckpoint -Times 0
    }
    It 'Wait-VmReady odmawia dla obcej VM' {
        { Wait-VmReady -VmName 'ObcaVM' -Credential $script:Credential -TimeoutSeconds 1 } | Should -Throw
        Should -Invoke -ModuleName VmHost New-PSSession -Times 0
    }
    It 'Invoke-DsoVmRun odmawia dla <Nazwa> i nie woła Hyper-V' -ForEach @(
        @{ Nazwa = 'ObcaVM' }
        @{ Nazwa = 'DSO-*' }
    ) {
        { Invoke-DsoVmRun -VmName $Nazwa -Credential $script:Credential -BuildDirectory $TestDrive -CatalogPath "$TestDrive\k.json" -Cases @() -OutDirectory $TestDrive } | Should -Throw
        Should -Invoke -ModuleName VmHost Restore-VMCheckpoint -Times 0
        Should -Invoke -ModuleName VmHost Start-VM -Times 0
    }
    It 'Invoke-VmTests.ps1 odrzuca obcą VM przy wiązaniu parametrów (exit 1, bez Hyper-V)' {
        $Output = & powershell.exe -NoProfile -File (Join-Path $script:VmRoot 'Invoke-VmTests.ps1') -VmName 'ObcaVM' 2>&1 | Out-String
        $LASTEXITCODE | Should -Be 1
        $Output | Should -BeLike '*VmName*'
    }
}

Describe 'Wait-VmReady: błędy logowania i ponowienia' -Skip:(-not $script:HasHyperV) {
    BeforeEach { Set-NoLiveVmDefaultMock -ModuleName VmHost; Mock -ModuleName VmHost Assert-DsoVmLiveAllowed {}; Mock -ModuleName VmHost Start-Sleep {}; Mock -ModuleName VmHost Get-VM { [pscustomobject]@{ Name = 'v'; State = 'Running'; Heartbeat = 'OkApplicationsHealthy' } } }
    It 'błąd uwierzytelnienia przerywa od razu, bez ponawiania (lockout VM)' {
        Mock -ModuleName VmHost New-PSSession { throw 'The credential is invalid.' }
        { Wait-VmReady -VmName 'DSO-26H2-Test' -Credential $script:Credential -TimeoutSeconds 600 } | Should -Throw -ExpectedMessage '*credential is invalid*'
        Should -Invoke -ModuleName VmHost New-PSSession -Times 1 -Exactly
    }
    It 'błąd uwierzytelnienia niesie flagę VmAuthFailure rozpoznawaną przez Test-VmAuthFailure' {
        Mock -ModuleName VmHost New-PSSession { throw 'The credential is invalid.' }
        $Caught = $null
        try { Wait-VmReady -VmName 'DSO-26H2-Test' -Credential $script:Credential -TimeoutSeconds 600 } catch { $Caught = $_.Exception }
        $Caught | Should -Not -BeNullOrEmpty
        $Caught.Data['VmAuthFailure'] | Should -BeTrue
        Test-VmAuthFailure -Exception $Caught | Should -BeTrue
    }
    It 'wyczerpanie prób, gdy ostatni błąd to odmowa dostępu (sonda), też niesie flagę VmAuthFailure' {
        # Sesja otwiera się, ale sonda Invoke-Command za każdym razem rzuca "Access is denied": 8 prób, potem wyjątek z dopiskiem o uwierzytelnieniu.
        Mock -ModuleName VmHost New-PSSession { $script:FakeSession }
        Mock -ModuleName VmHost Remove-PSSession -RemoveParameterValidation 'Session' -MockWith {}
        Mock -ModuleName VmHost Invoke-Command -RemoveParameterValidation 'Session' -MockWith { throw 'Access is denied.' }
        $Caught = $null
        try { Wait-VmReady -VmName 'DSO-26H2-Test' -Credential $script:Credential -TimeoutSeconds 600 } catch { $Caught = $_.Exception }
        Should -Invoke -ModuleName VmHost New-PSSession -Times 8 -Exactly
        $Caught | Should -Not -BeNullOrEmpty
        $Caught.Message | Should -BeLike '*wygląda na błąd uwierzytelnienia*'
        Test-VmAuthFailure -Exception $Caught | Should -BeTrue
    }
    It 'wyczerpanie prób bez wzorca uwierzytelnienia NIE niesie flagi VmAuthFailure' {
        Mock -ModuleName VmHost New-PSSession { throw 'dziwny błąd xyz' }
        $Caught = $null
        try { Wait-VmReady -VmName 'DSO-26H2-Test' -Credential $script:Credential -TimeoutSeconds 600 } catch { $Caught = $_.Exception }
        $Caught | Should -Not -BeNullOrEmpty
        Test-VmAuthFailure -Exception $Caught | Should -BeFalse
    }
    It 'nieznany błąd jest ponawiany ograniczoną liczbę razy (8)' {
        Mock -ModuleName VmHost New-PSSession { throw 'dziwny błąd xyz' }
        { Wait-VmReady -VmName 'DSO-26H2-Test' -Credential $script:Credential -TimeoutSeconds 600 } | Should -Throw -ExpectedMessage '*dziwny błąd xyz*'
        Should -Invoke -ModuleName VmHost New-PSSession -Times 8 -Exactly
    }
    It 'timeout zawiera ostatni błąd New-PSSession' {
        Mock -ModuleName VmHost New-PSSession { throw 'The virtual machine is not ready yet' }
        { Wait-VmReady -VmName 'DSO-26H2-Test' -Credential $script:Credential -TimeoutSeconds 1 } | Should -Throw -ExpectedMessage '*not ready yet*'
    }
}

Describe 'Invoke-DsoVmRun: poprawki rundy 1' -Skip:(-not $script:HasHyperV) {
    BeforeEach {
        Mock -ModuleName VmHost Restore-VmCleanState {}
        Mock -ModuleName VmHost Start-VM {}
        Mock -ModuleName VmHost Wait-VmReady { $null }
        Mock -ModuleName VmHost Get-VmBootObservation { @() }
        Mock -ModuleName VmHost Copy-DsoBuildToVm {}
        Mock -ModuleName VmHost Invoke-Command -RemoveParameterValidation 'Session' -MockWith { 'S-1-5-21-9' }
        Mock -ModuleName VmHost Copy-Item -RemoveParameterValidation 'FromSession' -MockWith {}
        Mock -ModuleName VmHost Remove-PSSession -RemoveParameterValidation 'Session' -MockWith {}
        Mock -ModuleName VmHost Invoke-Pester { [pscustomobject]@{ PassedCount = 3; FailedCount = 0; SkippedCount = 0; Failed = @() } }
        $RunArgs = @{ VmName = 'DSO-26H2-Test'; Credential = $script:Credential; BuildDirectory = $TestDrive; CatalogPath = "$TestDrive\k.json"; OutDirectory = $TestDrive }
    }
    It 'błąd przywracania w finally nie zastępuje pierwotnego błędu' {
        $global:DsoRestoreCalls = 0
        Mock -ModuleName VmHost Restore-VmCleanState { $global:DsoRestoreCalls++; if ($global:DsoRestoreCalls -ge 2) { throw 'checkpoint zepsuty' } }
        Mock -ModuleName VmHost Invoke-Pester { throw 'boom pestera' }
        $Message = try { Invoke-DsoVmRun @RunArgs -Cases @(); '' } catch { $_.Exception.Message }
        $Message | Should -BeLike '*boom pestera*'
        $Message | Should -BeLike '*checkpoint zepsuty*'
        $Message | Should -BeLike '*czystym stanie*'
    }
    It 'błąd przywracania bez wcześniejszego błędu też jest zgłaszany' {
        $global:DsoRestoreCalls = 0
        Mock -ModuleName VmHost Restore-VmCleanState { $global:DsoRestoreCalls++; if ($global:DsoRestoreCalls -ge 2) { throw 'checkpoint zepsuty' } }
        { Invoke-DsoVmRun @RunArgs -Cases @() } | Should -Throw -ExpectedMessage '*czystym stanie*'
    }
    It 'stare rekordy z poprzedniego przebiegu nie trafiają do report.json' {
        $Records = Join-Path $TestDrive 'DSO-26H2-Test\main\records'
        New-Item -ItemType Directory -Path $Records -Force | Out-Null
        Set-Content -LiteralPath "$Records\stary.json" -Value '{"Name":"stary"}'
        $Run = Invoke-DsoVmRun @RunArgs -Cases @()
        Test-Path "$Records\stary.json" | Should -BeFalse
        @((Get-Content $Run.ReportPath -Raw | ConvertFrom-Json).cases).Count | Should -Be 0
    }
    It 'wynik Pestera 0/0 daje wyjątek także bez przypadków' {
        Mock -ModuleName VmHost Invoke-Pester { [pscustomobject]@{ PassedCount = 0; FailedCount = 0; SkippedCount = 0; Failed = @() } }
        { Invoke-DsoVmRun @RunArgs -Cases @() } | Should -Throw -ExpectedMessage '*żadnego testu*'
    }
    It 'wynik Pestera 0/0 daje wyjątek przy niepustych przypadkach' {
        Mock -ModuleName VmHost Invoke-Pester { [pscustomobject]@{ PassedCount = 0; FailedCount = 0; SkippedCount = 0; Failed = @() } }
        { Invoke-DsoVmRun @RunArgs -Cases @(@{ Name = 'n'; TweakId = 't' }) } | Should -Throw -ExpectedMessage '*żadnego testu*'
    }
    It 'FailedContainersCount = 1 daje Failed >= 1, a kontener trafia do failedTests' {
        Mock -ModuleName VmHost Invoke-Pester {
            [pscustomobject]@{
                PassedCount = 3; FailedCount = 0; SkippedCount = 0; Failed = @()
                FailedContainersCount = 1
                FailedContainers = @([pscustomobject]@{ Path = 'VmScenarios.Tests.ps1'; ErrorRecord = @([System.Management.Automation.ErrorRecord]::new([System.Exception]::new('kontener padł'), 'e', 'NotSpecified', $null)) })
            }
        }
        $Run = Invoke-DsoVmRun @RunArgs -Cases @()
        $Run.Failed | Should -BeGreaterOrEqual 1
        $Report = Get-Content $Run.ReportPath -Raw | ConvertFrom-Json
        @($Report.pester.failedTests).Count | Should -Be 1
        $Report.pester.failedTests[0].name | Should -BeLike '*VmScenarios*'
        $Report.pester.failedTests[0].error | Should -BeLike '*kontener padł*'
    }
}

Describe 'Invoke-VmTests.ps1: zero przypadków' {
    It 'kończy się błędem (exit 1) i komunikatem, zanim dotknie VM' {
        # Skrypt idzie w osobnym procesie (mocki nie działają), więc nazwa spełnia wzorzec, ale nie należy do żadnej VM:
        # nawet po przestawieniu kontroli pustych przypadków nic nie zrobi revertu ani startu prawdziwej VM.
        $VmName = 'DSO-26H2-Brak'
        if (Get-Command -Name Get-VM -ErrorAction SilentlyContinue) { Get-VM -Name $VmName -ErrorAction SilentlyContinue | Should -BeNullOrEmpty -Because "VM $VmName nie może istnieć" }
        Set-Content -LiteralPath "$TestDrive\puste.psd1" -Value '@{ Cases = @() }' -Encoding UTF8
        $Output = & powershell.exe -NoProfile -File (Join-Path $script:VmRoot 'Invoke-VmTests.ps1') -VmName $VmName -CasesPath "$TestDrive\puste.psd1" -OutDirectory "$TestDrive\out" 2>&1 | Out-String
        $LASTEXITCODE | Should -Be 1
        $Output | Should -BeLike '*Brak przypadków*'
    }
}

Describe 'Scenariusze VM: brak fałszywej zieleni (przez Invoke-DsoVmRun, kontekst produkcyjny)' {
    BeforeAll {
        function script:Invoke-DryRun {
            param([string]$Variant)
            $Script = Join-Path $PSScriptRoot 'helpers\Invoke-ScenarioDryRun.ps1'
            $Lines = @(& powershell.exe -NoProfile -File $Script -Variant $Variant 2>&1 | ForEach-Object { "$_" })
            $Line = $Lines | Where-Object { $_ -like 'DSORESULT:*' } | Select-Object -Last 1
            if (-not $Line) { throw "Brak wyniku dry-runu '$Variant': $($Lines -join ' | ')" }
            return ($Line.Substring(10) | ConvertFrom-Json)
        }
    }
    It '<Variant>: komplet zielonych testów' -ForEach @(
        @{ Variant = 'Good' }
        @{ Variant = 'ArrayOk' }
        @{ Variant = 'RealCases' }
        @{ Variant = 'FullChecks' }
    ) {
        $Result = Invoke-DryRun -Variant $Variant
        $Result.threw | Should -BeNullOrEmpty -Because "$($Result.threw)"
        $Result.failed | Should -Be 0 -Because (@($Result.messages) -join '; ')
        $Result.passed | Should -BeGreaterThan 8
    }
    It 'RealCases: każdy przypadek z cases.psd1 zapisał rekord' {
        $Expected = @((Import-PowerShellDataFile (Join-Path $script:VmRoot 'cases.psd1')).Cases).Count
        $Expected | Should -BeGreaterThan 0
        (Invoke-DryRun -Variant 'RealCases').cases | Should -Be $Expected
    }
    It 'Skip: blok ze Skip i żywym It nie woła Invoke-VmCase ani nie nadpisuje rekordu pominięcia' {
        $Result = Invoke-DryRun -Variant 'SkippedLiveIt'
        $Result.skipInvoked | Should -BeFalse
        $Result.recordSkipped | Should -BeTrue
        $Result.recordReason | Should -Be 'Powod pominiecia.'
    }
    It 'Execute: przebieg, kod, wynik, Check i Cleanup są zielone (5 testów)' {
        $Result = Invoke-DryRun -Variant 'Execute'
        $Result.threw | Should -BeNullOrEmpty -Because "$($Result.threw)"
        $Result.failed | Should -Be 0 -Because (@($Result.messages) -join '; ')
        $Result.passed | Should -Be 5
    }
    It 'ExecuteRequiresReboot: przebieg, kod, wynik, requiresReboot raportu kroku execute i Check są zielone (5 testów)' {
        $Result = Invoke-DryRun -Variant 'ExecuteRequiresReboot'
        $Result.threw | Should -BeNullOrEmpty -Because "$($Result.threw)"
        $Result.failed | Should -Be 0 -Because (@($Result.messages) -join '; ')
        $Result.passed | Should -Be 5
    }
    It 'ExecuteFailedMessage: oczekiwane Failed, kod wyjścia i komunikat są zielone (5 testów: przebieg, kod, wynik, komunikat, Check)' {
        $Result = Invoke-DryRun -Variant 'ExecuteFailedMessage'
        $Result.threw | Should -BeNullOrEmpty -Because "$($Result.threw)"
        $Result.failed | Should -Be 0 -Because (@($Result.messages) -join '; ')
        $Result.passed | Should -Be 5
    }
    It 'AppRemove: lista przed/po, kod, wynik, restart, ostrzeżenia, komunikat, przerwanie, punkt, Check i Cleanup są zielone (19 testów)' {
        $Result = Invoke-DryRun -Variant 'AppRemove'
        $Result.threw | Should -BeNullOrEmpty -Because "$($Result.threw)"
        $Result.failed | Should -Be 0 -Because (@($Result.messages) -join '; ')
        $Result.passed | Should -Be 19
        $Result.cases | Should -Be 2
    }
    It 'AppInstall: kod, wyniki i kody winget pozycji, komplet pozycji, status zainstalowana, stan winget, komunikat, Check i Cleanup są zielone (15 testów)' {
        $Result = Invoke-DryRun -Variant 'AppInstall'
        $Result.threw | Should -BeNullOrEmpty -Because "$($Result.threw)"
        $Result.failed | Should -Be 0 -Because (@($Result.messages) -join '; ')
        $Result.passed | Should -Be 15
        $Result.cases | Should -Be 2
    }
    It 'Reboot: przebieg, restarty, klucz i stany są zielone (przez scenariusz)' {
        $Result = Invoke-DryRun -Variant 'Reboot'
        $Result.threw | Should -BeNullOrEmpty -Because "$($Result.threw)"
        $Result.failed | Should -Be 0 -Because (@($Result.messages) -join '; ')
        $Result.passed | Should -BeGreaterThan 8
    }
    It 'ReconnectReal: prawdziwy blok Reconnect pod Invoke-Pester z wnętrza modułu, StrictMode Latest, bez błędów $script:' {
        $Result = Invoke-DryRun -Variant 'ReconnectReal'
        $Result.threw | Should -BeNullOrEmpty -Because "$($Result.threw)"
        $Result.failed | Should -Be 0 -Because (@($Result.messages) -join '; ')
        $Result.reconnected | Should -BeTrue -Because 'Reconnect nie zwrócił nowej sesji'
    }
    It 'NoSnapshot: test "stan końcowy = stan przed" nie powstaje bez Snapshot (dokładnie jeden test mniej niż w Good)' {
        $Good = Invoke-DryRun -Variant 'Good'
        $Result = Invoke-DryRun -Variant 'NoSnapshot'
        $Result.threw | Should -BeNullOrEmpty -Because "$($Result.threw)"
        $Result.failed | Should -Be 0 -Because (@($Result.messages) -join '; ')
        $Result.passed | Should -Be ($Good.passed - 1)
    }
    It 'Skip: przypadek pominięty nie jest zaliczony, nie woła Invoke-VmCase i zostawia rekord z powodem' {
        $Good = Invoke-DryRun -Variant 'Good'
        $Result = Invoke-DryRun -Variant 'Skipped'
        $Result.threw | Should -BeNullOrEmpty -Because "$($Result.threw)"
        $Result.failed | Should -Be 0 -Because (@($Result.messages) -join '; ')
        $Result.skipped | Should -BeGreaterOrEqual 1
        $Result.passed | Should -Be $Good.passed -Because 'przypadek ze Skip nie może zwiększyć liczby zaliczonych'
        $Result.skipInvoked | Should -BeFalse
        $Result.skippedRecords | Should -Be 1
    }
    It '<Variant>: czerwony test z komunikatem zawierającym "<Fragment>"' -ForEach @(
        @{ Variant = 'NoReport'; Fragment = 'exit 740' }
        @{ Variant = 'HiveMissing'; Fragment = 'nie jest załadowany' }
        @{ Variant = 'WrittenToHkcu'; Fragment = 'HKCU procesu' }
        @{ Variant = 'EmptyNotWrittenTo'; Fragment = 'nic nie zostało sprawdzone' }
        @{ Variant = 'StateDiff'; Fragment = 'Exists' }
        @{ Variant = 'RunThrows'; Fragment = 'sztuczny wyjątek' }
        @{ Variant = 'WrongState'; Fragment = 'detect-after-apply' }
        @{ Variant = 'ArrayBad'; Fragment = 'Refused' }
        @{ Variant = 'EmptyAllowed'; Fragment = 'Pusta lista dozwolonych' }
        @{ Variant = 'EmptyActual'; Fragment = 'pusta wartość' }
        @{ Variant = 'ExecuteCheckFails'; Fragment = 'Check zwrócił: False' }
        @{ Variant = 'ExecuteCleanupFails'; Fragment = 'sprzątanie nie powiodło się' }
        @{ Variant = 'ExecuteFailedMessageWrong'; Fragment = 'brak aktywnego połączenia' }
        @{ Variant = 'ExecuteNoRequiresReboot'; Fragment = "krok 'execute': requiresReboot" }
        @{ Variant = 'ExecuteNoRequiresRebootField'; Fragment = "nie ma pola 'requiresReboot'" }
        @{ Variant = 'RebootKeyPresent'; Fragment = 'nadal istnieje' }
        @{ Variant = 'RebootNoRestart'; Fragment = 'restart' }
        @{ Variant = 'RebootNoKeyCheck'; Fragment = 'nic nie zostało sprawdzone' }
        @{ Variant = 'RebootWrongAfterReboot'; Fragment = 'detect-after-reboot' }
        @{ Variant = 'FullCheckAfterApplyFails'; Fragment = 'Check zwrócił: False' }
        @{ Variant = 'FullCheckAfterUndoEmpty'; Fragment = 'nic nie zostało sprawdzone' }
        @{ Variant = 'RebootKeyPresentMissing'; Fragment = 'sprzed apply' }
        @{ Variant = 'RebootMarkerNotGone'; Fragment = 'znacznik' }
        @{ Variant = 'RebootNoRequiresReboot'; Fragment = 'requiresReboot' }
        @{ Variant = 'AppRemoveStillListed'; Fragment = "krok 'list-after'" }
        @{ Variant = 'AppRemoveWrongOutcome'; Fragment = "wynik kroku 'remove-app'" }
        @{ Variant = 'AppRemoveNoWarning'; Fragment = 'brak ostrzeżenia' }
        @{ Variant = 'AppRemoveWrongRemovability'; Fragment = "removability wiersza w kroku 'list-before'" }
        @{ Variant = 'AppRemoveCheckFails'; Fragment = 'Check zwrócił: False' }
        @{ Variant = 'AppRemoveNoApps'; Fragment = "nie ma pola 'apps'" }
        @{ Variant = 'AppRemoveNoRequiresReboot'; Fragment = "nie ma pola 'requiresReboot'" }
        @{ Variant = 'AppInstallWrongOutcome'; Fragment = "wynik pozycji 'install.7zip'" }
        @{ Variant = 'AppInstallMissingItem'; Fragment = 'a AppIds ma 2' }
        @{ Variant = 'AppInstallWrongInstalled'; Fragment = "krok 'list-install': 'install.dso-test-msi' installed" }
        @{ Variant = 'AppInstallNoInstallRows'; Fragment = "nie ma pola 'installRows'" }
        @{ Variant = 'AppInstallWrongWingetState'; Fragment = 'stan winget' }
        @{ Variant = 'AppInstallNoMessage'; Fragment = 'brak komunikatu' }
        @{ Variant = 'AppInstallWrongExit'; Fragment = 'exit 5' }
        @{ Variant = 'AppInstallCheckFails'; Fragment = 'Check zwrócił: False' }
        @{ Variant = 'AppInstallWrongWingetCode'; Fragment = "kod winget pozycji 'install.7zip'" }
        @{ Variant = 'AppInstallUnexpectedWingetCode'; Fragment = "'install.vlc' nie powinna mieć kodu winget" }
    ) {
        $Result = Invoke-DryRun -Variant $Variant
        $Result.threw | Should -BeNullOrEmpty -Because "$($Result.threw)"
        $Result.failed | Should -BeGreaterThan 0
        (@($Result.messages) -join ' | ') | Should -BeLike "*$Fragment*"
    }
}
Describe 'Składnia skryptów VM' {
    It '<Plik> parsuje się bez błędów' -ForEach @(
        @{ Plik = 'scenarios\VmScenarios.Tests.ps1' }
        @{ Plik = 'Invoke-VmTests.ps1' }
    ) {
        $Errors = $null
        $null = [System.Management.Automation.Language.Parser]::ParseFile((Join-Path $script:VmRoot $Plik), [ref]$null, [ref]$Errors)
        @($Errors).Count | Should -Be 0 -Because (@($Errors | ForEach-Object { $_.Message }) -join '; ')
    }
}

Describe 'Wait-VmReady: heartbeat i limit prób' -Skip:(-not $script:HasHyperV) {
    BeforeEach {
        Set-NoLiveVmDefaultMock -ModuleName VmHost; Mock -ModuleName VmHost Assert-DsoVmLiveAllowed {}
        Mock -ModuleName VmHost Start-Sleep {}
        Mock -ModuleName VmHost Get-VM { [pscustomobject]@{ Name = 'v'; State = 'Running'; Heartbeat = 'OkApplicationsHealthy' } }
        Mock -ModuleName VmHost Invoke-Command -RemoveParameterValidation 'Session' -MockWith { $true }
        Mock -ModuleName VmHost Remove-PSSession -RemoveParameterValidation 'Session' -MockWith {}
    }
    It 'brak heartbeatu: rzuca z nazwą VM i stanem, New-PSSession nie jest wołane' {
        Mock -ModuleName VmHost Get-VM { [pscustomobject]@{ Name = 'v'; State = 'Running'; Heartbeat = 'NoContact' } }
        Mock -ModuleName VmHost New-PSSession { $script:FakeSession }
        { Wait-VmReady -VmName 'DSO-26H2-Test' -Credential $script:Credential -TimeoutSeconds 1 } |
            Should -Throw -ExpectedMessage "*DSO-26H2-Test*NoContact*"
        Should -Invoke -ModuleName VmHost New-PSSession -Times 0 -Exactly
    }
    It 'heartbeat OkApplicationsUnknown wystarcza' {
        Mock -ModuleName VmHost Get-VM { [pscustomobject]@{ Name = 'v'; State = 'Running'; Heartbeat = 'OkApplicationsUnknown' } }
        Mock -ModuleName VmHost New-PSSession { $script:FakeSession }
        $S = Wait-VmReady -VmName 'DSO-26H2-Test' -Credential $script:Credential -TimeoutSeconds 60
        [object]::ReferenceEquals($S, $script:FakeSession) | Should -BeTrue
    }
    It 'heartbeat pojawia się po kilku odpytaniach, potem sesja' {
        $script:HbCalls = 0
        Mock -ModuleName VmHost Get-VM { $script:HbCalls++; [pscustomobject]@{ Name = 'v'; State = 'Running'; Heartbeat = $(if ($script:HbCalls -lt 3) { 'NoContact' } else { 'OkApplicationsHealthy' }) } }
        Mock -ModuleName VmHost New-PSSession { $script:FakeSession }
        Wait-VmReady -VmName 'DSO-26H2-Test' -Credential $script:Credential -TimeoutSeconds 60 | Out-Null
        Should -Invoke -ModuleName VmHost New-PSSession -Times 1 -Exactly
    }
    It 'przejściowe błędy: dokładnie 8 prób, potem rzuca z ostatnim błędem' {
        Mock -ModuleName VmHost New-PSSession { throw 'The virtual machine is not ready yet' }
        { Wait-VmReady -VmName 'DSO-26H2-Test' -Credential $script:Credential -TimeoutSeconds 600 } | Should -Throw -ExpectedMessage '*not ready yet*'
        Should -Invoke -ModuleName VmHost New-PSSession -Times 8 -Exactly
    }
    It 'sukces w próbie 3 zwraca sesję po 3 wywołaniach' {
        $script:Tries = 0
        Mock -ModuleName VmHost New-PSSession { $script:Tries++; if ($script:Tries -lt 3) { throw 'not ready' }; $script:FakeSession }
        $S = Wait-VmReady -VmName 'DSO-26H2-Test' -Credential $script:Credential -TimeoutSeconds 600
        [object]::ReferenceEquals($S, $script:FakeSession) | Should -BeTrue
        Should -Invoke -ModuleName VmHost New-PSSession -Times 3 -Exactly
    }
    It 'odrzuca nazwę z końcowym znakiem nowej linii' {
        Mock -ModuleName VmHost New-PSSession { $script:FakeSession }
        { Wait-VmReady -VmName "DSO-26H2-Pro`n" -Credential $script:Credential -TimeoutSeconds 1 } | Should -Throw
        { Restore-VmCleanState -VmName "DSO-26H2-Pro`n" -CheckpointName 'c' } | Should -Throw
        { Invoke-DsoVmRun -VmName "DSO-26H2-Pro`n" -Credential $script:Credential -BuildDirectory $TestDrive -CatalogPath "$TestDrive\k.json" -Cases @() -OutDirectory $TestDrive } | Should -Throw
        Should -Invoke -ModuleName VmHost New-PSSession -Times 0 -Exactly
    }
}

Describe 'Task 8: Invoke-DsoVmRun z RunLabel' -Skip:(-not $script:HasHyperV) {
    BeforeEach {
        Set-NoLiveVmDefaultMock -ModuleName VmHost; Mock -ModuleName VmHost Assert-DsoVmLiveAllowed {}
        Mock -ModuleName VmHost Restore-VmCleanState {}
        Mock -ModuleName VmHost Start-VM {}
        # Prawdziwa Wait-VmReady (przez Reconnect) na podmienionych granicach Hyper-V: heartbeat OK, New-PSSession zwraca sesję.
        Mock -ModuleName VmHost Get-VM { [pscustomobject]@{ Heartbeat = 'OkApplicationsHealthy' } }
        Mock -ModuleName VmHost Start-Sleep {}
        Mock -ModuleName VmHost New-PSSession { $script:FakeSession }
        Mock -ModuleName VmHost Get-VmBootObservation { @() }
        Mock -ModuleName VmHost Copy-DsoBuildToVm {}
        Mock -ModuleName VmHost Invoke-Command -RemoveParameterValidation 'Session' -MockWith { 'S-1-5-21-9' }
        Mock -ModuleName VmHost Copy-Item -RemoveParameterValidation 'FromSession' -MockWith {}
        Mock -ModuleName VmHost Remove-PSSession -RemoveParameterValidation 'Session' -MockWith {}
        Mock -ModuleName VmHost Invoke-Pester {
            $global:Dso8Container = $Configuration.Run.Container.Value
            [pscustomobject]@{ PassedCount = 3; FailedCount = 0; SkippedCount = 0; Failed = @() }
        }
        $script:RunArgs = @{ VmName = 'DSO-26H2-Test'; Credential = $script:Credential; BuildDirectory = $TestDrive; CatalogPath = "$TestDrive\k.json"; OutDirectory = $TestDrive; Cases = @() }
    }
    AfterAll { Remove-Variable -Name Dso8Container -Scope Global -ErrorAction SilentlyContinue }
    It 'domyślna etykieta main: raport w out\<VM>\main\report.json' {
        $Run = Invoke-DsoVmRun @script:RunArgs
        $Run.RunLabel | Should -Be 'main'
        $Run.ReportPath | Should -Be (Join-Path $TestDrive 'DSO-26H2-Test\main\report.json')
        Test-Path $Run.ReportPath | Should -BeTrue
        (Get-Content $Run.ReportPath -Raw | ConvertFrom-Json).runLabel | Should -Be 'main'
    }
    It 'własna etykieta: osobny katalog raportu' {
        $Run = Invoke-DsoVmRun @script:RunArgs -RunLabel 'reboot-x'
        $Run.ReportPath | Should -Be (Join-Path $TestDrive 'DSO-26H2-Test\reboot-x\report.json')
    }
    It 'odrzuca etykietę z separatorem ścieżki' {
        { Invoke-DsoVmRun @script:RunArgs -RunLabel '..\x' } | Should -Throw
        Should -Invoke -ModuleName VmHost Restore-VmCleanState -Times 0
    }
    It 'każdy przebieg zaczyna się i kończy przywróceniem checkpointu' {
        Invoke-DsoVmRun @script:RunArgs -RunLabel 'reboot-x' | Out-Null
        Should -Invoke -ModuleName VmHost Restore-VmCleanState -Times 2 -Exactly
    }
    It 'sesja z Reconnect: logi kopiowane jej przez, a po przebiegu zamykana' {
        $script:Second = [System.Runtime.Serialization.FormatterServices]::GetUninitializedObject([System.Management.Automation.Runspaces.PSSession])
        Mock -ModuleName VmHost Invoke-Pester {
            $Reconnect = @($Configuration.Run.Container.Value)[0].Data.Reconnect
            $global:Dso8Reconnected = & $Reconnect
            [pscustomobject]@{ PassedCount = 3; FailedCount = 0; SkippedCount = 0; Failed = @() }
        }
        Mock -ModuleName VmHost Test-VmSessionOpen { $null -ne $Session }
        Mock -ModuleName VmHost New-PSSession { if ($null -eq $global:Dso8Reconnected -and $script:Calls++ -eq 0) { $script:FakeSession } else { $script:Second } }
        $script:Calls = 0
        Invoke-DsoVmRun @script:RunArgs | Out-Null
        Should -Invoke -ModuleName VmHost Copy-Item -Times 1 -Exactly -ParameterFilter { [object]::ReferenceEquals($FromSession, $script:Second) }
        Should -Invoke -ModuleName VmHost Remove-PSSession -ParameterFilter { $Session -eq $script:Second } -Times 1 -Exactly
        Remove-Variable -Name Dso8Reconnected -Scope Global -ErrorAction SilentlyContinue
    }
    It 'do scenariusza trafia Reconnect, który woła Wait-VmReady' {
        Invoke-DsoVmRun @script:RunArgs | Out-Null
        $Data = @($global:Dso8Container)[0].Data
        $Data.Reconnect | Should -BeOfType [scriptblock]
        $Session = & $Data.Reconnect
        $Session | Should -Not -BeNullOrEmpty
        Should -Invoke -ModuleName VmHost New-PSSession -Times 2 -Exactly -ParameterFilter { $VMName -eq 'DSO-26H2-Test' }
    }
}

Describe 'Task 8: Invoke-DsoVmRunSet' {
    BeforeEach {
        $global:Dso8Labels = [System.Collections.Generic.List[string]]::new()
        Mock -ModuleName VmHost Invoke-DsoVmRun {
            $global:Dso8Labels.Add("$RunLabel|$(@($Cases.Name) -join ',')")
            [pscustomobject]@{ VmName = $VmName; RunLabel = $RunLabel; Passed = 1; Failed = 0; Skipped = 0; ReportPath = "$OutDirectory\$VmName\$RunLabel\report.json" }
        }
        $script:Cases = @(
            @{ Name = 'a'; Flow = 'Full' }
            @{ Name = 'r1'; Flow = 'Reboot' }
            @{ Name = 'b'; Flow = 'Full' }
        )
        $script:SetArgs = @{ VmName = 'DSO-26H2-Test'; Credential = $script:Credential; BuildDirectory = 'B'; CatalogPath = 'C'; OutDirectory = 'O'; CheckpointName = 'cp'; ReadyTimeoutSeconds = 60 }
    }
    AfterAll { Remove-Variable -Name Dso8Labels -Scope Global -ErrorAction SilentlyContinue }
    It '1 przypadek Reboot i 2 zwykłe: dwa wywołania Invoke-DsoVmRun z różnymi RunLabel' {
        $Runs = @(Invoke-DsoVmRunSet @script:SetArgs -Cases $script:Cases)
        Should -Invoke -ModuleName VmHost Invoke-DsoVmRun -Times 2 -Exactly
        @($global:Dso8Labels) | Should -Be @('main|a,b', 'reboot-r1|r1')
        $Runs.Count | Should -Be 2
        $Runs[0].RunLabel | Should -Be 'main'
    }
    It 'przekazuje CheckpointName i ReadyTimeoutSeconds' {
        Invoke-DsoVmRunSet @script:SetArgs -Cases $script:Cases | Out-Null
        Should -Invoke -ModuleName VmHost Invoke-DsoVmRun -Times 2 -Exactly -ParameterFilter { $CheckpointName -eq 'cp' -and $ReadyTimeoutSeconds -eq 60 }
    }
    It 'błąd przebiegu zatrzymuje kolejne (ochrona przed blokadą konta)' {
        Mock -ModuleName VmHost Invoke-DsoVmRun { throw 'błąd uwierzytelnienia' }
        { Invoke-DsoVmRunSet @script:SetArgs -Cases $script:Cases } | Should -Throw -ExpectedMessage '*uwierzytelnienia*'
        Should -Invoke -ModuleName VmHost Invoke-DsoVmRun -Times 1 -Exactly
    }
    It 'błąd przebiegu zawiera etykietę' {
        Mock -ModuleName VmHost Invoke-DsoVmRun { throw 'zepsute' }
        { Invoke-DsoVmRunSet @script:SetArgs -Cases $script:Cases } | Should -Throw -ExpectedMessage '*main*zepsute*'
    }
}

Describe 'Task 8: Format-VmRunSummary z przebiegami' {
    It 'etykiety i suma dla VM z kilkoma przebiegami' {
        $Runs = @(
            [pscustomobject]@{ VmName = 'A'; RunLabel = 'main'; Passed = 5; Failed = 1; Skipped = 1; ReportPath = 'a\main.json' }
            [pscustomobject]@{ VmName = 'A'; RunLabel = 'reboot-x'; Passed = 6; Failed = 0; Skipped = 0; ReportPath = 'a\rb.json' }
        )
        $Lines = Format-VmRunSummary -Runs $Runs
        $Lines | Should -Contain 'A [main]: zaliczone 5, błędy 1, pominięte 1 – a\main.json'
        $Lines | Should -Contain 'A [reboot-x]: zaliczone 6, błędy 0, pominięte 0 – a\rb.json'
        $Lines | Should -Contain 'A razem: zaliczone 11, błędy 1, pominięte 1'
    }
}

Describe 'Fix 1: Wait-VmHeartbeatDrop i Reconnect' -Skip:(-not $script:HasHyperV) {
    BeforeEach {
        Set-NoLiveVmDefaultMock -ModuleName VmHost; Mock -ModuleName VmHost Assert-DsoVmLiveAllowed {}
        $global:Dso8Hb = [System.Collections.Queue]::new()
        $global:Dso8Order = [System.Collections.Generic.List[string]]::new()
        Mock -ModuleName VmHost Start-Sleep {}
        Mock -ModuleName VmHost Get-VM {
            $global:Dso8Order.Add('get-vm')
            $State = if ($global:Dso8Hb.Count -gt 0) { $global:Dso8Hb.Dequeue() } else { 'OkApplicationsHealthy' }
            [pscustomobject]@{ Heartbeat = $State }
        }
        Mock -ModuleName VmHost New-PSSession { $global:Dso8Order.Add('new-pssession'); $script:FakeSession }
        Mock -ModuleName VmHost Invoke-Command -RemoveParameterValidation 'Session' -MockWith { $true }
        Mock -ModuleName VmHost Wait-VmReady { $global:Dso8Order.Add('wait-vmready'); $script:FakeSession }
    }
    AfterAll { Remove-Variable -Name Dso8Hb, Dso8Order -Scope Global -ErrorAction SilentlyContinue }
    It 'OK -> NoContact -> OK: czekanie kończy się na NoContact, bez New-PSSession' {
        foreach ($S in 'OkApplicationsHealthy', 'OkApplicationsHealthy', 'LostCommunication', 'OkApplicationsHealthy') { $global:Dso8Hb.Enqueue($S) }
        Wait-VmHeartbeatDrop -VmName 'DSO-26H2-Test' | Should -BeTrue
        Should -Invoke -ModuleName VmHost Get-VM -Times 3 -Exactly -ParameterFilter { $VMName -eq 'DSO-26H2-Test' }
        Should -Invoke -ModuleName VmHost New-PSSession -Times 0 -Exactly
    }
    It 'heartbeat stale OK: jedno ostrzeżenie i powrót po limicie (60 odczytów)' {
        $Warnings = @()
        Wait-VmHeartbeatDrop -VmName 'DSO-26H2-Test' -WarningVariable Warnings -WarningAction SilentlyContinue | Should -BeFalse
        @($Warnings).Count | Should -Be 1
        Should -Invoke -ModuleName VmHost Get-VM -Times 60 -Exactly
    }
    It 'Reconnect z Invoke-DsoVmRun: najpierw czekanie na heartbeat, potem Wait-VmReady (raz), tylko przy pierwszej próbie' {
        foreach ($S in 'OkApplicationsHealthy', 'LostCommunication') { $global:Dso8Hb.Enqueue($S) }
        Mock -ModuleName VmHost Restore-VmCleanState {}
        Mock -ModuleName VmHost Start-VM {}
        Mock -ModuleName VmHost Get-VmBootObservation { @() }
        Mock -ModuleName VmHost Copy-DsoBuildToVm {}
        Mock -ModuleName VmHost Copy-Item -RemoveParameterValidation 'FromSession' -MockWith {}
        Mock -ModuleName VmHost Remove-PSSession -RemoveParameterValidation 'Session' -MockWith {}
        Mock -ModuleName VmHost Invoke-Pester {
            $Reconnect = @($Configuration.Run.Container.Value)[0].Data.Reconnect
            $null = & $Reconnect 1
            $null = & $Reconnect 2
            [pscustomobject]@{ PassedCount = 1; FailedCount = 0; SkippedCount = 0; Failed = @() }
        }
        Invoke-DsoVmRun -VmName 'DSO-26H2-Test' -Credential $script:Credential -BuildDirectory $TestDrive -CatalogPath "$TestDrive\k.json" -Cases @() -OutDirectory $TestDrive | Out-Null
        # Start VM (zamockowana Wait-VmReady), potem Reconnect próba 1: 2 odczyty heartbeatu (OK, LostCommunication) bez New-PSSession,
        # dopiero potem prawdziwa Wait-VmReady (Get-VM + New-PSSession; domknięcie widzi wyeksportowaną funkcję); próba 2 bez czekania na heartbeat.
        @($global:Dso8Order) | Should -Be @('wait-vmready', 'get-vm', 'get-vm', 'get-vm', 'new-pssession', 'get-vm', 'new-pssession')
    }
}

Describe 'Fix 2: wybór sesji do kopiowania logów' -Skip:(-not $script:HasHyperV) {
    BeforeEach {
        Mock -ModuleName VmHost Restore-VmCleanState {}
        Mock -ModuleName VmHost Start-VM {}
        Mock -ModuleName VmHost Wait-VmReady { $script:FakeSession }
        Mock -ModuleName VmHost Get-VmBootObservation { @() }
        Mock -ModuleName VmHost Copy-DsoBuildToVm {}
        Mock -ModuleName VmHost Invoke-Command -RemoveParameterValidation 'Session' -MockWith { 'S-1-5-21-9' }
        Mock -ModuleName VmHost Copy-Item -RemoveParameterValidation 'FromSession' -MockWith {}
        Mock -ModuleName VmHost Remove-PSSession -RemoveParameterValidation 'Session' -MockWith {}
        Mock -ModuleName VmHost Invoke-Pester { [pscustomobject]@{ PassedCount = 1; FailedCount = 0; SkippedCount = 0; Failed = @() } }
        $script:RunArgs2 = @{ VmName = 'DSO-26H2-Test'; Credential = $script:Credential; BuildDirectory = $TestDrive; CatalogPath = "$TestDrive\k.json"; OutDirectory = $TestDrive; Cases = @() }
    }
    It 'otwarta sesja oryginalna: logi kopiowane przez nią' {
        Mock -ModuleName VmHost Test-VmSessionOpen { $null -ne $Session }
        Invoke-DsoVmRun @script:RunArgs2 | Out-Null
        Should -Invoke -ModuleName VmHost Copy-Item -Times 1 -Exactly
    }
    It 'brak otwartej sesji: kopiowanie pominięte z jednym ostrzeżeniem' {
        Mock -ModuleName VmHost Test-VmSessionOpen { $false }
        $Warnings = @()
        Invoke-DsoVmRun @script:RunArgs2 -WarningVariable Warnings -WarningAction SilentlyContinue | Out-Null
        Should -Invoke -ModuleName VmHost Copy-Item -Times 0 -Exactly
        @($Warnings | Where-Object { "$_" -like '*brak otwartej sesji*' }).Count | Should -Be 1
    }
    It 'Test-VmSessionOpen: null i sesja bez stanu Opened to nie' {
        InModuleScope VmHost { Test-VmSessionOpen -Session $null } | Should -BeFalse
        InModuleScope VmHost -Parameters @{ S = [pscustomobject]@{ State = 'Broken' } } { Test-VmSessionOpen -Session $S } | Should -BeFalse
        InModuleScope VmHost -Parameters @{ S = [pscustomobject]@{ State = 'Opened' } } { Test-VmSessionOpen -Session $S } | Should -BeTrue
    }
}

AfterAll { Clear-NoLiveVm }
