BeforeAll {
    # Wyłącznik: żaden test na mockach nie może dotknąć prawdziwego Hyper-V (patrz Support\NoLiveVm.ps1).
    . (Join-Path $PSScriptRoot 'Support\NoLiveVm.ps1')

    Import-Module (Join-Path $PSScriptRoot '..\modules\VmCommon.psm1') -Force
    Import-Module (Join-Path $PSScriptRoot '..\modules\VmRun.psm1') -Force
    Import-Module (Join-Path $PSScriptRoot '..\modules\VmHost.psm1') -Force
    $script:VmRoot = Join-Path $PSScriptRoot '..'
    $script:CatalogPath = Join-Path $script:VmRoot 'catalog\vm-test-catalog.json'

    function Write-CasesFile {
        param([string]$Path, [string]$CaseBody)
        Set-Content -LiteralPath $Path -Value "@{ Cases = @( @{ $CaseBody } ) }" -Encoding UTF8
    }
    $script:FullExpect = "Before = 'NotApplied'; ApplyExit = 0; ApplyOutcome = 'Applied'; AfterApply = 'Applied'; UndoExit = 0; UndoOutcome = 'Undone'; AfterUndo = 'NotApplied'"
    $script:RebootExpect = "$script:FullExpect; AfterReboot = 'Applied'"
    $script:FullBuiltIn = "Name = 'przyp'; TweakId = 'ux.menu-delay'; Flow = 'Full'; Catalog = 'BuiltIn'; RestorePoint = 'skip'"
}

Describe 'C2 Task 1 (M1b): Snapshot wymagany dla Full/Reboot z katalogu BuiltIn' {
    It 'Full + BuiltIn bez Snapshot: błąd importu' {
        Write-CasesFile -Path "$TestDrive\a1.psd1" -CaseBody "$script:FullBuiltIn; Expect = @{ $script:FullExpect }"
        { Import-VmCases -CasesPath "$TestDrive\a1.psd1" -CatalogPath $script:CatalogPath } | Should -Throw -ExpectedMessage '*przyp*Snapshot*'
    }
    It 'Full + BuiltIn z pustym Snapshot: błąd importu' {
        Write-CasesFile -Path "$TestDrive\a2.psd1" -CaseBody "$script:FullBuiltIn; Snapshot = @{ Registry = @(); Services = @() }; Expect = @{ $script:FullExpect }"
        { Import-VmCases -CasesPath "$TestDrive\a2.psd1" -CatalogPath $script:CatalogPath } | Should -Throw -ExpectedMessage '*przyp*Snapshot*'
    }
    It 'Reboot + BuiltIn bez Snapshot: błąd importu' {
        Write-CasesFile -Path "$TestDrive\a3.psd1" -CaseBody "Name = 'przyp'; TweakId = 'disk.ntfs-last-access'; Flow = 'Reboot'; Catalog = 'BuiltIn'; RestorePoint = 'skip'; Expect = @{ $script:RebootExpect }"
        { Import-VmCases -CasesPath "$TestDrive\a3.psd1" -CatalogPath $script:CatalogPath } | Should -Throw -ExpectedMessage '*przyp*Snapshot*'
    }
    It 'Full + BuiltIn z niepustym Snapshot (rejestr albo usługi): import przechodzi' -ForEach @(
        @{ Snapshot = "@{ Registry = @(@{ Path = 'HKLM\SOFTWARE\X'; Name = 'V' }); Services = @() }" }
        @{ Snapshot = "@{ Registry = @(); Services = @('Spooler') }" }
    ) {
        Write-CasesFile -Path "$TestDrive\a4.psd1" -CaseBody "$script:FullBuiltIn; Snapshot = $Snapshot; Expect = @{ $script:FullExpect }"
        { Import-VmCases -CasesPath "$TestDrive\a4.psd1" -CatalogPath $script:CatalogPath } | Should -Not -Throw
    }
    It 'Full + BuiltIn ze Skip bez Snapshot: import przechodzi (przypadek nie rusza VM)' {
        Write-CasesFile -Path "$TestDrive\a5.psd1" -CaseBody "$script:FullBuiltIn; Skip = 'powód'; Expect = @{ $script:FullExpect }"
        { Import-VmCases -CasesPath "$TestDrive\a5.psd1" -CatalogPath $script:CatalogPath } | Should -Not -Throw
    }
    It 'DetectOnly + BuiltIn bez Snapshot: import przechodzi' {
        Write-CasesFile -Path "$TestDrive\a6.psd1" -CaseBody "Name = 'przyp'; TweakId = 'disk.hibernation'; Flow = 'DetectOnly'; Catalog = 'BuiltIn'; RestorePoint = 'skip'; Expect = @{ Before = 'NotApplied' }"
        { Import-VmCases -CasesPath "$TestDrive\a6.psd1" -CatalogPath $script:CatalogPath } | Should -Not -Throw
    }
    It 'Full + Test (katalog testowy) bez Snapshot: import przechodzi (wymóg dotyczy tylko BuiltIn)' {
        Write-CasesFile -Path "$TestDrive\a7.psd1" -CaseBody "Name = 'przyp'; TweakId = 'vmtest.hklm-value'; Flow = 'Full'; RestorePoint = 'skip'; Expect = @{ $script:FullExpect }"
        { Import-VmCases -CasesPath "$TestDrive\a7.psd1" -CatalogPath $script:CatalogPath } | Should -Not -Throw
    }
    It 'prawdziwy cases.psd1 nadal się importuje' {
        { Import-VmCases -CasesPath (Join-Path $script:VmRoot 'cases.psd1') -CatalogPath $script:CatalogPath } | Should -Not -Throw
    }
}

Describe 'C2 Task 1 (M5): Expect.NotWrittenTo wymaga Expect.UserSid' {
    It 'NotWrittenTo bez UserSid: błąd importu' {
        Write-CasesFile -Path "$TestDrive\b1.psd1" -CaseBody "Name = 'przyp'; TweakId = 'vmtest.hkcu-value'; Flow = 'Full'; RestorePoint = 'skip'; Expect = @{ $script:FullExpect; NotWrittenTo = @(@{ Path = 'HKCU\Software\X'; Name = 'V' }) }"
        { Import-VmCases -CasesPath "$TestDrive\b1.psd1" -CatalogPath $script:CatalogPath } | Should -Throw -ExpectedMessage '*przyp*NotWrittenTo*UserSid*'
    }
    It 'NotWrittenTo z UserSid: import przechodzi' {
        Write-CasesFile -Path "$TestDrive\b2.psd1" -CaseBody "Name = 'przyp'; TweakId = 'vmtest.hkcu-value'; Flow = 'Full'; RestorePoint = 'skip'; Expect = @{ $script:FullExpect; UserSid = '{UserSid}'; NotWrittenTo = @(@{ Path = 'HKCU\Software\X'; Name = 'V' }) }"
        { Import-VmCases -CasesPath "$TestDrive\b2.psd1" -CatalogPath $script:CatalogPath } | Should -Not -Throw
    }
}

Describe 'C2 Task 1: podpowiedź HKU w Get-DsoStateSnapshot' {
    It 'HKCU bez AllowCurrentUser podpowiada tę samą formę co import: HKU\{UserSid}' {
        { Get-DsoStateSnapshot -Registry @(@{ Path = 'HKCU\Software\X'; Name = 'V' }) -Services @() } | Should -Throw -ExpectedMessage '*HKU\{UserSid}*'
    }
}

Describe 'C2 Task 1: New-VmSkipRecord i New-VmSkipReport (jeden kształt rekordu Skip)' {
    It 'rekord ma dokładnie pola Name, TweakId, Skipped, SkipReason' {
        $Record = New-VmSkipRecord -Case @{ Name = 'n'; TweakId = 'a.b'; Skip = 'powód' }
        ($Record.PSObject.Properties.Name -join ',') | Should -Be 'Name,TweakId,Skipped,SkipReason'
        $Record.Skipped | Should -BeTrue
        $Record.SkipReason | Should -Be 'powód'
    }
    It 'przypadek bez Skip: wyjątek' {
        { New-VmSkipRecord -Case @{ Name = 'n'; TweakId = 'a.b' } } | Should -Throw -ExpectedMessage '*Skip*'
    }
    It 'Write-VmSkippedRun zapisuje rekord o kształcie New-VmSkipRecord i szkielet report.json' {
        $Case = @{ Name = 'n'; TweakId = 'a.b'; Flow = 'DetectOnly'; RestorePoint = 'skip'; Skip = 'powód'; Expect = @{ Before = 'NotApplied' } }
        $Run = InModuleScope VmHost -Parameters @{ Case = $Case; Out = "$TestDrive\out" } { Write-VmSkippedRun -VmName 'DSO-26H2-Test' -OutDirectory $Out -Cases @($Case) }
        $Report = Get-Content -LiteralPath $Run.ReportPath -Raw -Encoding UTF8 | ConvertFrom-Json
        ($Report.PSObject.Properties.Name -join ',') | Should -Be 'vmName,runLabel,startedAt,finishedAt,bootObservation,pester,cases'
        $Expected = New-VmSkipRecord -Case $Case
        (@($Report.cases)[0].PSObject.Properties.Name -join ',') | Should -Be ($Expected.PSObject.Properties.Name -join ',')
        $Report.pester.skipped | Should -Be 1
    }
    It 'New-VmSkipReport buduje szkielet z podanych rekordów' {
        $Record = New-VmSkipRecord -Case @{ Name = 'n'; TweakId = 'a.b'; Skip = 'powód' }
        $Report = New-VmSkipReport -VmName 'V' -RunLabel 'skipped' -StartedAt ([datetime]'2026-10-18') -Records @($Record)
        $Report.pester.skipped | Should -Be 1
        $Report.pester.passed | Should -Be 0
        @($Report.cases).Count | Should -Be 1
    }
}

AfterAll { Clear-NoLiveVm }
