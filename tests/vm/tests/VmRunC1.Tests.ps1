BeforeAll {
    # Wyłącznik: żaden test na mockach nie może dotknąć prawdziwego Hyper-V (patrz Support\NoLiveVm.ps1).
    . (Join-Path $PSScriptRoot 'Support\NoLiveVm.ps1')

    Import-Module (Join-Path $PSScriptRoot '..\modules\VmCommon.psm1') -Force
    Import-Module (Join-Path $PSScriptRoot '..\modules\VmRun.psm1') -Force
    $script:VmRoot = Join-Path $PSScriptRoot '..'
    $script:CatalogPath = Join-Path $script:VmRoot 'catalog\vm-test-catalog.json'

    function Write-CasesFile {
        param([string]$Path, [string]$CaseBody)
        $Content = "@{ Cases = @( @{ $CaseBody } ) }"
        Set-Content -LiteralPath $Path -Value $Content -Encoding UTF8
    }
    $script:FullBody = "Name = 'przyp'; TweakId = 'vmtest.hklm-value'; Flow = 'Full'; RestorePoint = 'skip'"
    $script:FullExpect = "Before = 'NotApplied'; ApplyExit = 0; ApplyOutcome = 'Applied'; AfterApply = 'Applied'; UndoExit = 0; UndoOutcome = 'Undone'; AfterUndo = 'NotApplied'"
}

Describe 'C1 Task 1: prefiks ścieżek rejestru w Snapshot (Import-VmCases)' {
    It 'Snapshot.Registry odrzuca <Path>' -ForEach @(
        @{ Path = 'HKCU\Software\X' }
        @{ Path = 'HKCR\X' }
        @{ Path = 'HKEY_LOCAL_MACHINE\SOFTWARE\X' }
        @{ Path = 'C:\Windows' }
    ) {
        Write-CasesFile -Path "$TestDrive\p1.psd1" -CaseBody "$script:FullBody; Snapshot = @{ Registry = @(@{ Path = '$Path'; Name = 'V' }) }; Expect = @{ $script:FullExpect }"
        { Import-VmCases -CasesPath "$TestDrive\p1.psd1" -CatalogPath $script:CatalogPath } | Should -Throw -ExpectedMessage '*przyp*Snapshot.Registry*'
    }
    It 'Snapshot.Registry z HKCU podpowiada HKU\{UserSid}' {
        Write-CasesFile -Path "$TestDrive\p2.psd1" -CaseBody "$script:FullBody; Snapshot = @{ Registry = @(@{ Path = 'HKCU\X'; Name = 'V' }) }; Expect = @{ $script:FullExpect }"
        { Import-VmCases -CasesPath "$TestDrive\p2.psd1" -CatalogPath $script:CatalogPath } | Should -Throw -ExpectedMessage '*HKU\{UserSid}*'
    }
    It 'Snapshot.Registry akceptuje <Path>' -ForEach @(
        @{ Path = 'HKLM\SOFTWARE\X' }
        @{ Path = 'HKU\{UserSid}\Software\X' }
    ) {
        Write-CasesFile -Path "$TestDrive\p3.psd1" -CaseBody "$script:FullBody; Snapshot = @{ Registry = @(@{ Path = '$Path'; Name = 'V' }) }; Expect = @{ $script:FullExpect }"
        { Import-VmCases -CasesPath "$TestDrive\p3.psd1" -CatalogPath $script:CatalogPath } | Should -Not -Throw
    }
    It 'Expect.AfterApplySnapshot.Registry odrzuca HKCU' {
        Write-CasesFile -Path "$TestDrive\p4.psd1" -CaseBody "$script:FullBody; Expect = @{ $script:FullExpect; AfterApplySnapshot = @{ Registry = @(@{ Path = 'HKCU\X'; Name = 'V' }) } }"
        { Import-VmCases -CasesPath "$TestDrive\p4.psd1" -CatalogPath $script:CatalogPath } | Should -Throw -ExpectedMessage '*AfterApplySnapshot.Registry*'
    }
    It 'Expect.NotWrittenTo dopuszcza HKCU (celowo hive Administratora sesji PS Direct), odrzuca HKCR' {
        Write-CasesFile -Path "$TestDrive\p5.psd1" -CaseBody "$script:FullBody; Expect = @{ $script:FullExpect; UserSid = '{UserSid}'; NotWrittenTo = @(@{ Path = 'HKCU\Software\X'; Name = 'V' }) }"
        { Import-VmCases -CasesPath "$TestDrive\p5.psd1" -CatalogPath $script:CatalogPath } | Should -Not -Throw
        Write-CasesFile -Path "$TestDrive\p6.psd1" -CaseBody "$script:FullBody; Expect = @{ $script:FullExpect; NotWrittenTo = @(@{ Path = 'HKCR\X'; Name = 'V' }) }"
        { Import-VmCases -CasesPath "$TestDrive\p6.psd1" -CatalogPath $script:CatalogPath } | Should -Throw -ExpectedMessage '*NotWrittenTo*'
    }
}

Describe 'C1 Task 1: Get-DsoStateSnapshot - prefiks (obrona w głąb)' {
    It 'rzuca na <Path>, zamiast pytać system plików' -ForEach @(
        @{ Path = 'HKCR\X' }
        @{ Path = 'C:\Windows' }
        @{ Path = 'HKEY_LOCAL_MACHINE\SOFTWARE' }
    ) {
        { Get-DsoStateSnapshot -Registry @(@{ Path = $Path; Name = 'V' }) -Services @() } | Should -Throw -ExpectedMessage '*prefiks*'
    }
    It 'HKCU bez AllowCurrentUser jest odrzucone z podpowiedzią HKU' {
        { Get-DsoStateSnapshot -Registry @(@{ Path = 'HKCU\Software\X'; Name = 'V' }) -Services @() } | Should -Throw -ExpectedMessage '*HKU*'
    }
    It 'HKCU z AllowCurrentUser jest czytane (NotWrittenTo)' {
        $Snap = Get-DsoStateSnapshot -Registry @(@{ Path = 'HKCU\Software\DsoNoSuchKey-zzz'; Name = 'V' }) -Services @() -AllowCurrentUser $true
        $Snap.Registry[0].Exists | Should -BeFalse
    }
    It 'HKLM\ i HKU\ przechodzą bez AllowCurrentUser' {
        $Snap = Get-DsoStateSnapshot -Registry @(@{ Path = 'HKLM\SOFTWARE\DsoNoSuchKey-zzz'; Name = 'V' }, @{ Path = 'HKU\S-1-5-18\DsoNoSuchKey-zzz'; Name = 'V' }) -Services @()
        @($Snap.Registry).Count | Should -Be 2
    }
}

Describe 'C1 Task 1: Invoke-VmCase - NotWrittenTo z AllowCurrentUser, Snapshot bez' {
    BeforeEach {
        Mock -ModuleName VmRun Invoke-VmSetup {}
        Mock -ModuleName VmRun Get-VmRestorePointCount { 1 }
        Mock -ModuleName VmRun Invoke-DsoCli { [pscustomobject]@{ ExitCode = 0; TimedOut = $false; Report = [pscustomobject]@{ exitCode = 0 }; Json = '{}'; ReportError = $null } }
        $global:Dso9Snap = [System.Collections.Generic.List[object]]::new()
        Mock -ModuleName VmRun Get-VmStateSnapshot { $global:Dso9Snap.Add(@{ AllowCurrentUser = [bool]$AllowCurrentUser }); [pscustomobject]@{ Registry = @(); Services = @() } }
    }
    AfterAll { Remove-Variable -Name Dso9Snap -Scope Global -ErrorAction SilentlyContinue }
    It 'tylko odczyt NotWrittenTo dostaje -AllowCurrentUser' {
        $Case = @{ Name = 'c'; TweakId = 'a.b'; Flow = 'ApplyOnly'; RestorePoint = 'skip'; Setup = @(); Snapshot = @{ Registry = @(); Services = @() }
            Expect = @{ NotWrittenTo = @(@{ Path = 'HKCU\S'; Name = 'V' }) } }
        Invoke-VmCase -Session $null -Case $Case -Layout (Get-DsoVmLayout) | Out-Null
        @($global:Dso9Snap | Where-Object { $_.AllowCurrentUser }).Count | Should -Be 2
        @($global:Dso9Snap | Where-Object { -not $_.AllowCurrentUser }).Count | Should -BeGreaterThan 0
    }
}

Describe 'C1 Task 3: NotWrittenTo = stan bez zmian (przed/po apply/po undo)' {
    BeforeEach {
        Mock -ModuleName VmRun Invoke-VmSetup {}
        Mock -ModuleName VmRun Get-VmRestorePointCount { 1 }
        Mock -ModuleName VmRun Invoke-DsoCli { [pscustomobject]@{ ExitCode = 0; TimedOut = $false; Report = [pscustomobject]@{ exitCode = 0 }; Json = '{}'; ReportError = $null } }
        $global:Dso3Reads = [System.Collections.Generic.List[object]]::new()
        $global:Dso3Values = @('400', '400', '400')
        Mock -ModuleName VmRun Get-VmStateSnapshot {
            if (-not $AllowCurrentUser) { return [pscustomobject]@{ Registry = @(); Services = @() } }
            $Index = $global:Dso3Reads.Count
            $global:Dso3Reads.Add($Index)
            [pscustomobject]@{ Registry = @([pscustomobject]@{ Key = 'HKCU\Control Panel\Desktop\MenuShowDelay'; Exists = $true; Kind = 'String'; Data = $global:Dso3Values[$Index] }); Services = @() }
        }
        $script:Case3 = @{ Name = 'c'; TweakId = 'a.b'; Flow = 'Full'; RestorePoint = 'skip'; Setup = @(); Snapshot = @{ Registry = @(); Services = @() }
            Expect = @{ NotWrittenTo = @(@{ Path = 'HKCU\Control Panel\Desktop'; Name = 'MenuShowDelay' }) } }
    }
    AfterAll { Remove-Variable -Name Dso3Reads, Dso3Values -Scope Global -ErrorAction SilentlyContinue }
    It 'wartość istniejąca w HKCU admina i niezmieniona: brak różnic (nie fałszywa czerwień)' {
        $Record = Invoke-VmCase -Session $null -Case $script:Case3 -Layout (Get-DsoVmLayout)
        $global:Dso3Reads.Count | Should -Be 3 -Because 'przed, po apply, po undo'
        @($Record.NotWrittenToDiff).Count | Should -Be 0
    }
    It 'zmiana wartości w HKCU admina po apply jest wykrywana' {
        $global:Dso3Values = @('400', '10', '400')
        $Record = Invoke-VmCase -Session $null -Case $script:Case3 -Layout (Get-DsoVmLayout)
        @($Record.NotWrittenToDiff).Count | Should -Be 1
        $Record.NotWrittenToDiff[0] | Should -BeLike 'po apply:*Data*400*10*'
    }
    It 'zmiana wartości w HKCU admina dopiero po undo jest wykrywana' {
        $global:Dso3Values = @('400', '400', '250')
        $Record = Invoke-VmCase -Session $null -Case $script:Case3 -Layout (Get-DsoVmLayout)
        @($Record.NotWrittenToDiff).Count | Should -Be 1
        $Record.NotWrittenToDiff[0] | Should -BeLike 'po undo:*'
    }
}

Describe 'C1 Task 1: KeyPresentAfterUndo w Import-VmCases' {
    It 'odrzuca <Path>' -ForEach @(
        @{ Path = 'HKCU\X' }
        @{ Path = 'HKCR\X' }
        @{ Path = 'C:\Windows' }
    ) {
        Write-CasesFile -Path "$TestDrive\k1.psd1" -CaseBody "$script:FullBody; Expect = @{ $script:FullExpect; KeyPresentAfterUndo = @('$Path') }"
        { Import-VmCases -CasesPath "$TestDrive\k1.psd1" -CatalogPath $script:CatalogPath } | Should -Throw -ExpectedMessage "*przyp*KeyPresentAfterUndo*$Path*"
    }
    It 'HKCU podpowiada HKU\{UserSid}' {
        Write-CasesFile -Path "$TestDrive\k2.psd1" -CaseBody "$script:FullBody; Expect = @{ $script:FullExpect; KeyPresentAfterUndo = @('HKCU\X') }"
        { Import-VmCases -CasesPath "$TestDrive\k2.psd1" -CatalogPath $script:CatalogPath } | Should -Throw -ExpectedMessage '*HKU\{UserSid}*'
    }
    It 'akceptuje <Path> w Full' -ForEach @(
        @{ Path = 'HKLM\SOFTWARE\X' }
        @{ Path = 'HKU\{UserSid}\Software\X' }
    ) {
        Write-CasesFile -Path "$TestDrive\k3.psd1" -CaseBody "$script:FullBody; Expect = @{ $script:FullExpect; KeyPresentAfterUndo = @('$Path') }"
        { Import-VmCases -CasesPath "$TestDrive\k3.psd1" -CatalogPath $script:CatalogPath } | Should -Not -Throw
    }
    It 'pusta lista jest odrzucona' {
        Write-CasesFile -Path "$TestDrive\k4.psd1" -CaseBody "$script:FullBody; Expect = @{ $script:FullExpect; KeyPresentAfterUndo = @() }"
        { Import-VmCases -CasesPath "$TestDrive\k4.psd1" -CatalogPath $script:CatalogPath } | Should -Throw -ExpectedMessage '*KeyPresentAfterUndo*'
    }
    It 'ApplyOnly, DetectOnly i Execute zabraniają klucza' {
        Write-CasesFile -Path "$TestDrive\k5.psd1" -CaseBody "Name = 'n'; TweakId = 'vmtest.hklm-value'; Flow = 'ApplyOnly'; RestorePoint = 'skip'; Expect = @{ Before = 'NotApplied'; ApplyExit = 0; ApplyOutcome = 'Applied'; AfterApply = 'Applied'; KeyPresentAfterUndo = @('HKLM\X') }"
        { Import-VmCases -CasesPath "$TestDrive\k5.psd1" -CatalogPath $script:CatalogPath } | Should -Throw -ExpectedMessage '*KeyPresentAfterUndo*'
        Write-CasesFile -Path "$TestDrive\k6.psd1" -CaseBody "Name = 'n'; TweakId = 'vmtest.hklm-value'; Flow = 'DetectOnly'; RestorePoint = 'skip'; Expect = @{ Before = 'NotApplied'; KeyPresentAfterUndo = @('HKLM\X') }"
        { Import-VmCases -CasesPath "$TestDrive\k6.psd1" -CatalogPath $script:CatalogPath } | Should -Throw -ExpectedMessage '*KeyPresentAfterUndo*'
        Write-CasesFile -Path "$TestDrive\k7.psd1" -CaseBody "Name = 'n'; TweakId = 'vmtest.action-marker'; Flow = 'Execute'; RestorePoint = 'skip'; Check = '`$true'; Expect = @{ ExecuteExit = 0; ExecuteOutcome = 'Executed'; KeyPresentAfterUndo = @('HKLM\X') }"
        { Import-VmCases -CasesPath "$TestDrive\k7.psd1" -CatalogPath $script:CatalogPath } | Should -Throw -ExpectedMessage '*KeyPresentAfterUndo*'
    }
    It 'ten sam klucz w KeyAbsentAfterUndo i KeyPresentAfterUndo to sprzeczność' {
        Write-CasesFile -Path "$TestDrive\k8.psd1" -CaseBody "$script:FullBody; Expect = @{ $script:FullExpect; KeyAbsentAfterUndo = @('HKLM\SOFTWARE\X'); KeyPresentAfterUndo = @('hklm\software\x') }"
        { Import-VmCases -CasesPath "$TestDrive\k8.psd1" -CatalogPath $script:CatalogPath } | Should -Throw -ExpectedMessage '*sprzeczn*'
    }
    It 'prawdziwy reboot-hklm sprawdza, że klucz sprzed apply przetrwał undo' {
        $Cases = Import-VmCases -CasesPath "$script:VmRoot\cases.psd1" -CatalogPath $script:CatalogPath
        $Reboot = @($Cases | Where-Object { $_.Name -eq 'reboot-hklm' })[0]
        @($Reboot.Expect.KeyPresentAfterUndo) | Should -Be @('HKLM\SOFTWARE\DSOptimizer.VmTests')
    }
}

Describe 'C1 Task 1: Execute wymaga Check (Snapshot go nie zastępuje)' {
    BeforeAll {
        $script:ExecBody = "Name = 'e'; TweakId = 'vmtest.action-marker'; Flow = 'Execute'; RestorePoint = 'skip'"
        $script:ExecExpect = "ExecuteExit = 0; ExecuteOutcome = 'Executed'"
    }
    It 'bez Check i bez Snapshot: błąd importu z nazwą przypadku' {
        Write-CasesFile -Path "$TestDrive\m1.psd1" -CaseBody "$script:ExecBody; Expect = @{ $script:ExecExpect }"
        { Import-VmCases -CasesPath "$TestDrive\m1.psd1" -CatalogPath $script:CatalogPath } | Should -Throw -ExpectedMessage "*'e'*Check*"
    }
    It 'tylko Cleanup nie wystarcza' {
        Write-CasesFile -Path "$TestDrive\m2.psd1" -CaseBody "$script:ExecBody; Cleanup = 'Write-Output 1'; Expect = @{ $script:ExecExpect }"
        { Import-VmCases -CasesPath "$TestDrive\m2.psd1" -CatalogPath $script:CatalogPath } | Should -Throw -ExpectedMessage '*Check*Snapshot*'
    }
    It 'z Check przechodzi' {
        Write-CasesFile -Path "$TestDrive\m3.psd1" -CaseBody "$script:ExecBody; Check = '`$true'; Expect = @{ $script:ExecExpect }"
        { Import-VmCases -CasesPath "$TestDrive\m3.psd1" -CatalogPath $script:CatalogPath } | Should -Not -Throw
    }
    It 'niepusty Snapshot bez Check jest odrzucony (Snapshot nie zastępuje Check)' {
        Write-CasesFile -Path "$TestDrive\m4.psd1" -CaseBody "$script:ExecBody; Snapshot = @{ Registry = @(@{ Path = 'HKLM\SOFTWARE\X'; Name = 'V' }); Services = @() }; Expect = @{ $script:ExecExpect }"
        { Import-VmCases -CasesPath "$TestDrive\m4.psd1" -CatalogPath $script:CatalogPath } | Should -Throw -ExpectedMessage "*'e'*Check*Snapshot*opcjonalny*"
    }
    It 'pusty Snapshot bez Check jest odrzucony' {
        Write-CasesFile -Path "$TestDrive\m5.psd1" -CaseBody "$script:ExecBody; Snapshot = @{}; Expect = @{ $script:ExecExpect }"
        { Import-VmCases -CasesPath "$TestDrive\m5.psd1" -CatalogPath $script:CatalogPath } | Should -Throw -ExpectedMessage "*'e'*Check*"
    }
    It 'Check ze Snapshot przechodzi' {
        Write-CasesFile -Path "$TestDrive\m6.psd1" -CaseBody "$script:ExecBody; Check = '`$true'; Snapshot = @{ Registry = @(@{ Path = 'HKLM\SOFTWARE\X'; Name = 'V' }); Services = @() }; Expect = @{ $script:ExecExpect }"
        { Import-VmCases -CasesPath "$TestDrive\m6.psd1" -CatalogPath $script:CatalogPath } | Should -Not -Throw
    }
}

Describe 'C1 Task 1: CheckAfterApply i CheckAfterUndo w Import-VmCases' {
    It '<Key> przechodzi we Flow Full' -ForEach @(@{ Key = 'CheckAfterApply' }, @{ Key = 'CheckAfterUndo' }) {
        Write-CasesFile -Path "$TestDrive\c1.psd1" -CaseBody "$script:FullBody; $Key = '`$true'; Expect = @{ $script:FullExpect }"
        $Cases = Import-VmCases -CasesPath "$TestDrive\c1.psd1" -CatalogPath $script:CatalogPath
        $Cases[0].ContainsKey($Key) | Should -BeTrue
    }
    It '<Key> przechodzi we Flow Reboot' -ForEach @(@{ Key = 'CheckAfterApply' }, @{ Key = 'CheckAfterUndo' }) {
        Write-CasesFile -Path "$TestDrive\c2.psd1" -CaseBody "Name = 'r'; TweakId = 'vmtest.hklm-value'; Flow = 'Reboot'; RestorePoint = 'skip'; $Key = '`$true'; Expect = @{ $script:FullExpect; AfterReboot = 'Applied' }"
        { Import-VmCases -CasesPath "$TestDrive\c2.psd1" -CatalogPath $script:CatalogPath } | Should -Not -Throw
    }
    It '<Key> jest zabronione we Flow <Flow>' -ForEach @(
        @{ Key = 'CheckAfterApply'; Flow = 'ApplyOnly' }
        @{ Key = 'CheckAfterUndo'; Flow = 'ApplyOnly' }
        @{ Key = 'CheckAfterApply'; Flow = 'DetectOnly' }
        @{ Key = 'CheckAfterUndo'; Flow = 'Execute' }
    ) {
        $Body = switch ($Flow) {
            'ApplyOnly' { "Name = 'n'; TweakId = 'vmtest.hklm-value'; Flow = 'ApplyOnly'; RestorePoint = 'skip'; $Key = '`$true'; Expect = @{ Before = 'NotApplied'; ApplyExit = 0; ApplyOutcome = 'Applied'; AfterApply = 'Applied' }" }
            'DetectOnly' { "Name = 'n'; TweakId = 'vmtest.hklm-value'; Flow = 'DetectOnly'; RestorePoint = 'skip'; $Key = '`$true'; Expect = @{ Before = 'NotApplied' }" }
            'Execute' { "Name = 'n'; TweakId = 'vmtest.action-marker'; Flow = 'Execute'; RestorePoint = 'skip'; Check = '`$true'; $Key = '`$true'; Expect = @{ ExecuteExit = 0; ExecuteOutcome = 'Executed' }" }
        }
        Write-CasesFile -Path "$TestDrive\c3.psd1" -CaseBody $Body
        { Import-VmCases -CasesPath "$TestDrive\c3.psd1" -CatalogPath $script:CatalogPath } | Should -Throw -ExpectedMessage "*$Key*Full*Reboot*"
    }
    It '<Key> pusty skrypt jest odrzucony' -ForEach @(@{ Key = 'CheckAfterApply' }, @{ Key = 'CheckAfterUndo' }) {
        Write-CasesFile -Path "$TestDrive\c4.psd1" -CaseBody "$script:FullBody; $Key = ' '; Expect = @{ $script:FullExpect }"
        { Import-VmCases -CasesPath "$TestDrive\c4.psd1" -CatalogPath $script:CatalogPath } | Should -Throw -ExpectedMessage "*$Key*"
    }
    It 'Check i Cleanup nadal tylko dla Execute (komunikat o Execute)' {
        Write-CasesFile -Path "$TestDrive\c5.psd1" -CaseBody "$script:FullBody; Check = '`$true'; Expect = @{ $script:FullExpect }"
        { Import-VmCases -CasesPath "$TestDrive\c5.psd1" -CatalogPath $script:CatalogPath } | Should -Throw -ExpectedMessage '*Check*Execute*'
    }
}

Describe 'C1 Task 1: Invoke-VmCase - CheckAfterApply i CheckAfterUndo' {
    BeforeEach {
        $global:Dso9Events = [System.Collections.Generic.List[string]]::new()
        $global:Dso9Boot = [System.Collections.Queue]::new()
        $global:Dso9Reconnects = 0
        $global:Dso9Sessions = @(1..6 | ForEach-Object { [System.Runtime.Serialization.FormatterServices]::GetUninitializedObject([System.Management.Automation.Runspaces.PSSession]) })
        $global:Dso9CheckResult = 'Passed'
        Mock -ModuleName VmRun Invoke-VmSetup {}
        Mock -ModuleName VmRun Get-VmStateSnapshot { [pscustomobject]@{ Registry = @(); Services = @() } }
        Mock -ModuleName VmRun Start-Sleep {}
        Mock -ModuleName VmRun Remove-PSSession -RemoveParameterValidation 'Session' -MockWith {}
        Mock -ModuleName VmRun Invoke-DsoCli {
            $global:Dso9Events.Add("cli:$($Arguments[0])")
            [pscustomobject]@{ ExitCode = 0; TimedOut = $false; Report = [pscustomobject]@{ state = 'Applied'; outcome = 'Applied' }; Json = '{}'; ReportError = $null }
        }
        Mock -ModuleName VmRun Invoke-VmCheck {
            $global:Dso9Events.Add("check:$Script")
            $global:Dso9CheckResult
        }
        Mock -ModuleName VmRun Get-VmLastBootTime { [datetime]$global:Dso9Boot.Dequeue() }
        Mock -ModuleName VmRun Send-VmRestart { $global:Dso9Events.Add('restart') }
        Mock -ModuleName VmRun New-VmRestartMarker {}
        Mock -ModuleName VmRun Test-VmRestartMarker { $false }
        Mock -ModuleName VmRun Get-VmKeyExistence { @($Keys | ForEach-Object { [pscustomobject]@{ Key = $_; Exists = $true } }) }
        $script:Layout = Get-DsoVmLayout
        $script:Reconnect = { $global:Dso9Events.Add('reconnect'); $global:Dso9Reconnects++; $global:Dso9Sessions[$global:Dso9Reconnects] }
        $T = [datetime]'2026-10-17T10:00:00'
        $script:T0 = $T; $script:T1 = $T.AddMinutes(5); $script:T2 = $T.AddMinutes(10)
    }
    AfterAll { Remove-Variable -Name Dso9Events, Dso9Boot, Dso9Reconnects, Dso9Sessions, Dso9CheckResult -Scope Global -ErrorAction SilentlyContinue }
    It 'Full: CheckAfterApply po detect-after-apply (przed undo), CheckAfterUndo po detect-after-undo; wyniki w rekordzie' {
        $Case = @{ Name = 'c'; TweakId = 'a.b'; Flow = 'Full'; RestorePoint = 'skip'; Setup = @(); Snapshot = @{ Registry = @(); Services = @() }; Expect = @{}
            CheckAfterApply = '# po-apply'; CheckAfterUndo = '# po-undo' }
        $Record = Invoke-VmCase -Session $null -Case $Case -Layout $script:Layout
        @($global:Dso9Events) | Should -Be @('cli:--detect', 'cli:--apply', 'cli:--detect', 'check:# po-apply', 'cli:--undo', 'cli:--detect', 'check:# po-undo')
        $Record.CheckAfterApplyResult | Should -Be 'Passed'
        $Record.CheckAfterUndoResult | Should -Be 'Passed'
    }
    It 'Full bez kluczy: wyniki puste, Invoke-VmCheck nie jest wołany' {
        $Case = @{ Name = 'c'; TweakId = 'a.b'; Flow = 'Full'; RestorePoint = 'skip'; Setup = @(); Snapshot = @{ Registry = @(); Services = @() }; Expect = @{} }
        $Record = Invoke-VmCase -Session $null -Case $Case -Layout $script:Layout
        $Record.CheckAfterApplyResult | Should -BeNullOrEmpty
        $Record.CheckAfterUndoResult | Should -BeNullOrEmpty
        Should -Invoke -ModuleName VmRun Invoke-VmCheck -Times 0
    }
    It 'Reboot: CheckAfterApply po restarcie (po detect-after-reboot), CheckAfterUndo po drugim restarcie i detect-after-undo' {
        foreach ($Time in @($script:T0, $script:T1, $script:T1, $script:T2)) { $global:Dso9Boot.Enqueue($Time) }
        $Case = @{ Name = 'rb'; TweakId = 'a.b'; Flow = 'Reboot'; RestorePoint = 'skip'; Setup = @(); Snapshot = @{ Registry = @(); Services = @() }; Expect = @{}
            CheckAfterApply = '# po-apply'; CheckAfterUndo = '# po-undo' }
        $Record = Invoke-VmCase -Session $global:Dso9Sessions[0] -Case $Case -Layout $script:Layout -Reconnect $script:Reconnect
        @($global:Dso9Events | Where-Object { $_ -ne 'reconnect' }) | Should -Be @(
            'cli:--detect', 'cli:--apply', 'cli:--detect', 'restart', 'cli:--detect', 'check:# po-apply', 'cli:--undo', 'restart', 'cli:--detect', 'check:# po-undo')
        $Record.CheckAfterApplyResult | Should -Be 'Passed'
        $Record.CheckAfterUndoResult | Should -Be 'Passed'
    }
    It 'porażka Check trafia do rekordu i nie przerywa przebiegu (undo nadal wykonane)' {
        $global:Dso9CheckResult = 'Check zwrócił: False'
        $Case = @{ Name = 'c'; TweakId = 'a.b'; Flow = 'Full'; RestorePoint = 'skip'; Setup = @(); Snapshot = @{ Registry = @(); Services = @() }; Expect = @{}; CheckAfterApply = '# a'; CheckAfterUndo = '# u' }
        $Record = Invoke-VmCase -Session $null -Case $Case -Layout $script:Layout
        $Record.CheckAfterApplyResult | Should -BeLike '*False*'
        $Record.Steps.Step | Should -Contain 'undo'
        $Record.CheckAfterUndoResult | Should -BeLike '*False*'
    }
    It 'KeyPresentAfterUndo: rekord zawiera odczyt, KeyAbsentAfterUndo zostaje osobno' {
        $Case = @{ Name = 'c'; TweakId = 'a.b'; Flow = 'Full'; RestorePoint = 'skip'; Setup = @(); Snapshot = @{ Registry = @(); Services = @() }
            Expect = @{ KeyAbsentAfterUndo = @('HKLM\SOFTWARE\X\Y'); KeyPresentAfterUndo = @('HKLM\SOFTWARE\X') } }
        $Record = Invoke-VmCase -Session $null -Case $Case -Layout $script:Layout
        @($Record.KeysAfterUndo).Key | Should -Be @('HKLM\SOFTWARE\X\Y')
        @($Record.KeysPresentAfterUndo).Key | Should -Be @('HKLM\SOFTWARE\X')
    }
    It 'bez KeyPresentAfterUndo rekord ma KeysPresentAfterUndo = null' {
        $Case = @{ Name = 'c'; TweakId = 'a.b'; Flow = 'Full'; RestorePoint = 'skip'; Setup = @(); Snapshot = @{ Registry = @(); Services = @() }; Expect = @{} }
        (Invoke-VmCase -Session $null -Case $Case -Layout $script:Layout).KeysPresentAfterUndo | Should -BeNullOrEmpty
    }
}

Describe 'C1 Task 1: Execute - Setup w try/finally' {
    BeforeEach {
        $global:Dso9ExecCalls = [System.Collections.Generic.List[string]]::new()
        Mock -ModuleName VmRun Invoke-VmSetup { throw 'setup nie powiodło się' }
        Mock -ModuleName VmRun Get-VmStateSnapshot { [pscustomobject]@{ Registry = @(); Services = @() } }
        Mock -ModuleName VmRun Invoke-DsoCli { [pscustomobject]@{ ExitCode = 0; TimedOut = $false; Report = [pscustomobject]@{ outcome = 'Executed' }; Json = '{}'; ReportError = $null } }
        Mock -ModuleName VmRun Invoke-Command -RemoveParameterValidation 'Session' -MockWith { $global:Dso9ExecCalls.Add($ScriptBlock.ToString()) }
    }
    AfterAll { Remove-Variable -Name Dso9ExecCalls -Scope Global -ErrorAction SilentlyContinue }
    It 'wyjątek Setup nie omija Cleanup, a wyjątek nie jest połykany; CLI nie startuje' {
        $Case = @{ Name = 'a'; TweakId = 'x.y'; Flow = 'Execute'; RestorePoint = 'skip'; Setup = @('x'); Check = '# CHK'; Cleanup = '# MARKER-CLEANUP'
            Expect = @{ ExecuteExit = 0; ExecuteOutcome = 'Executed' } }
        { Invoke-VmCase -Session $null -Case $Case -Layout (Get-DsoVmLayout) } | Should -Throw -ExpectedMessage '*setup nie powiodło się*'
        @($global:Dso9ExecCalls | Where-Object { $_ -match 'MARKER-CLEANUP' }).Count | Should -Be 1
        Should -Invoke -ModuleName VmRun Invoke-DsoCli -Times 0
    }
}

Describe 'C1 Task 1: ulotny znacznik restartu (Restart-VmAndReconnect)' {
    BeforeEach {
        $global:Dso9Events = [System.Collections.Generic.List[string]]::new()
        $global:Dso9Boot = [System.Collections.Queue]::new()
        $global:Dso9Marker = [System.Collections.Queue]::new()
        $global:Dso9Reconnects = 0
        $global:Dso9Sessions = @(1..8 | ForEach-Object { [System.Runtime.Serialization.FormatterServices]::GetUninitializedObject([System.Management.Automation.Runspaces.PSSession]) })
        Mock -ModuleName VmRun Start-Sleep {}
        Mock -ModuleName VmRun Remove-PSSession -RemoveParameterValidation 'Session' -MockWith {}
        Mock -ModuleName VmRun Get-VmLastBootTime { [datetime]$global:Dso9Boot.Dequeue() }
        Mock -ModuleName VmRun Send-VmRestart { $global:Dso9Events.Add('restart') }
        Mock -ModuleName VmRun New-VmRestartMarker { $global:Dso9Events.Add('marker-created') }
        Mock -ModuleName VmRun Test-VmRestartMarker { [bool]$global:Dso9Marker.Dequeue() }
        $script:Reconnect = { $global:Dso9Events.Add('reconnect'); $global:Dso9Reconnects++; $global:Dso9Sessions[$global:Dso9Reconnects] }
        $T = [datetime]'2026-10-17T10:00:00'
        $script:T0 = $T; $script:T1 = $T.AddMinutes(5)
    }
    AfterAll { Remove-Variable -Name Dso9Events, Dso9Boot, Dso9Marker, Dso9Reconnects, Dso9Sessions -Scope Global -ErrorAction SilentlyContinue }
    It 'znacznik powstaje przed restartem; po restarcie jego brak + późniejszy LastBootUpTime = sukces z MarkerGone' {
        foreach ($Time in @($script:T0, $script:T1)) { $global:Dso9Boot.Enqueue($Time) }
        $global:Dso9Marker.Enqueue($false)
        $Result = Restart-VmAndReconnect -Session $global:Dso9Sessions[0] -Reconnect $script:Reconnect -Step 'reboot-after-apply'
        $global:Dso9Events.IndexOf('marker-created') | Should -BeLessThan $global:Dso9Events.IndexOf('restart')
        $Result.Record.MarkerGone | Should -BeTrue
        $Result.Record.Step | Should -Be 'reboot-after-apply'
    }
    It 'późniejszy LastBootUpTime, ale znacznik nadal jest (skok zegara bez restartu): to nie jest restart' {
        foreach ($Time in @($script:T0, $script:T1, $script:T1)) { $global:Dso9Boot.Enqueue($Time) }
        foreach ($Present in @($true, $false)) { $global:Dso9Marker.Enqueue($Present) }
        $Result = Restart-VmAndReconnect -Session $global:Dso9Sessions[0] -Reconnect $script:Reconnect -Step 's'
        $Result.Record.Attempts | Should -Be 2
        $global:Dso9Reconnects | Should -Be 2
    }
    It 'znacznik wciąż obecny po wszystkich próbach: błąd "restart nie nastąpił"' {
        foreach ($Time in @($script:T0) + @($script:T1) * 7) { $global:Dso9Boot.Enqueue($Time) }
        foreach ($Present in 1..7) { $global:Dso9Marker.Enqueue($true) }
        { Restart-VmAndReconnect -Session $global:Dso9Sessions[0] -Reconnect $script:Reconnect -Step 's' } | Should -Throw -ExpectedMessage '*restart nie nastąpił*'
        $global:Dso9Reconnects | Should -Be 6
    }
    It 'błąd utworzenia znacznika przerywa przed restartem (nie ma weryfikacji = nie ma restartu)' {
        $global:Dso9Boot.Enqueue($script:T0)
        Mock -ModuleName VmRun New-VmRestartMarker { throw 'nie można utworzyć znacznika' }
        { Restart-VmAndReconnect -Session $global:Dso9Sessions[0] -Reconnect $script:Reconnect -Step 's' } | Should -Throw -ExpectedMessage '*znacznika*'
        Should -Invoke -ModuleName VmRun Send-VmRestart -Times 0
    }
}

Describe 'C1 Task 1: New-VmRestartMarker i Test-VmRestartMarker' {
    It 'New-VmRestartMarker tworzy klucz volatile w HKLM (RegistryOptions.Volatile), nie przez New-Item' {
        $global:Dso9Script = $null
        Mock -ModuleName VmRun Invoke-Command -RemoveParameterValidation 'Session' -MockWith { $global:Dso9Script = $ScriptBlock.ToString(); $true }
        New-VmRestartMarker -Session $null
        $global:Dso9Script | Should -Match 'Volatile'
        $global:Dso9Script | Should -Match 'LocalMachine'
        $global:Dso9Script | Should -Not -Match 'New-Item'
        Remove-Variable -Name Dso9Script -Scope Global
    }
    It 'New-VmRestartMarker rzuca, gdy VM nie potwierdzi utworzenia ($true)' {
        Mock -ModuleName VmRun Invoke-Command -RemoveParameterValidation 'Session' -MockWith { $false }
        { New-VmRestartMarker -Session $null } | Should -Throw -ExpectedMessage '*znacznik*'
        Mock -ModuleName VmRun Invoke-Command -RemoveParameterValidation 'Session' -MockWith { }
        { New-VmRestartMarker -Session $null } | Should -Throw -ExpectedMessage '*znacznik*'
    }
    It 'Test-VmRestartMarker zwraca bool z VM; brak odpowiedzi to błąd, nie "brak znacznika"' {
        Mock -ModuleName VmRun Invoke-Command -RemoveParameterValidation 'Session' -MockWith { $true }
        Test-VmRestartMarker -Session $null | Should -BeTrue
        Mock -ModuleName VmRun Invoke-Command -RemoveParameterValidation 'Session' -MockWith { $false }
        Test-VmRestartMarker -Session $null | Should -BeFalse
        Mock -ModuleName VmRun Invoke-Command -RemoveParameterValidation 'Session' -MockWith { }
        { Test-VmRestartMarker -Session $null } | Should -Throw
    }
    It 'API CreateSubKey(string, bool, RegistryOptions) istnieje w tym środowisku' {
        [Microsoft.Win32.RegistryKey].GetMethod('CreateSubKey', [type[]]@([string], [bool], [Microsoft.Win32.RegistryOptions])) | Should -Not -BeNullOrEmpty
    }
}

Describe 'C1 Task 1: Split-VmCaseRuns - pominięte przypadki bez aktywnego main' {
    It 'pominięte Reboot i Full bez aktywnych zwykłych: osobny przebieg tylko-pominięcia, bez main; aktywny Reboot osobno' {
        $Cases = @(
            @{ Name = 's1'; Flow = 'Full'; Skip = 'powód' }
            @{ Name = 'r2'; Flow = 'Reboot'; Skip = 'powód' }
            @{ Name = 'r3'; Flow = 'Reboot' }
        )
        $Plan = @(Split-VmCaseRuns -Cases $Cases)
        $Plan.Count | Should -Be 2
        $Plan[0].Label | Should -Be 'skipped'
        $Plan[0].SkipOnly | Should -BeTrue
        @($Plan[0].Cases.Name) | Should -Be @('s1', 'r2')
        $Plan[1].Label | Should -Be 'reboot-r3'
        $Plan[1].SkipOnly | Should -BeFalse
    }
    It 'wszystko pominięte: tylko przebieg tylko-pominięcia (nie main)' {
        $Plan = @(Split-VmCaseRuns -Cases @(@{ Name = 'a'; Flow = 'Full'; Skip = 'p' }, @{ Name = 'b'; Flow = 'Reboot'; Skip = 'p' }))
        $Plan.Count | Should -Be 1
        $Plan[0].Label | Should -Be 'skipped'
        $Plan[0].SkipOnly | Should -BeTrue
    }
    It 'main z aktywnym przypadkiem zachowuje pominięte w main (kolejność wejściowa), bez przebiegu tylko-pominięcia' {
        $Plan = @(Split-VmCaseRuns -Cases @(@{ Name = 'a'; Flow = 'Full' }, @{ Name = 'r'; Flow = 'Reboot'; Skip = 'p' }, @{ Name = 'b'; Flow = 'Full'; Skip = 'p' }))
        $Plan.Count | Should -Be 1
        $Plan[0].Label | Should -Be 'main'
        $Plan[0].SkipOnly | Should -BeFalse
        @($Plan[0].Cases.Name) | Should -Be @('a', 'r', 'b')
    }
    It 'pusta lista przypadków to pusty plan' {
        @(Split-VmCaseRuns -Cases @()).Count | Should -Be 0
    }
}

AfterAll { Clear-NoLiveVm }
