BeforeAll {
    # Wyłącznik: żaden test na mockach nie może dotknąć prawdziwego Hyper-V (patrz Support\NoLiveVm.ps1).
    . (Join-Path $PSScriptRoot 'Support\NoLiveVm.ps1')

    Import-Module (Join-Path $PSScriptRoot '..\modules\VmCommon.psm1') -Force
    Import-Module (Join-Path $PSScriptRoot '..\modules\VmRun.psm1') -Force
    Import-Module (Join-Path $PSScriptRoot '..\modules\VmHost.psm1') -Force
    $script:Credential = [pscredential]::new('Administrator', (ConvertTo-SecureString 'Test-Haslo-1!' -AsPlainText -Force))
}

Describe 'C1 Task 1: Invoke-DsoVmRunSet - częściowe wyniki i przebieg tylko-pominięcia' {
    BeforeEach {
        $global:Dso9Labels = [System.Collections.Generic.List[string]]::new()
        $global:Dso9Fail = $null
        Mock -ModuleName VmHost Invoke-DsoVmRun {
            $global:Dso9Labels.Add($RunLabel)
            if ($global:Dso9Fail -and $RunLabel -eq $global:Dso9Fail) { throw 'sesja zerwana' }
            [pscustomobject]@{ VmName = $VmName; RunLabel = $RunLabel; Passed = 1; Failed = 0; Skipped = 0; ReportPath = "$OutDirectory\$VmName\$RunLabel\report.json" }
        }
        $script:SetArgs = @{ VmName = 'DSO-26H2-Test'; Credential = $script:Credential; BuildDirectory = 'B'; CatalogPath = 'C'; OutDirectory = $TestDrive; CheckpointName = 'cp'; ReadyTimeoutSeconds = 60 }
    }
    AfterAll { Remove-Variable -Name Dso9Labels, Dso9Fail -Scope Global -ErrorAction SilentlyContinue }
    It 'wyjątek drugiego przebiegu niesie ukończony pierwszy przebieg (Data.CompletedRuns) i zachowuje komunikat z etykietą' {
        $global:Dso9Fail = 'reboot-r1'
        $Cases = @(@{ Name = 'a'; Flow = 'Full' }, @{ Name = 'r1'; Flow = 'Reboot' }, @{ Name = 'r2'; Flow = 'Reboot' })
        $Caught = $null
        try { Invoke-DsoVmRunSet @script:SetArgs -Cases $Cases | Out-Null } catch { $Caught = $_.Exception }
        $Caught | Should -Not -BeNullOrEmpty
        $Caught.Message | Should -BeLike "*reboot-r1*sesja zerwana*"
        $Completed = @($Caught.Data['CompletedRuns'])
        $Completed.Count | Should -Be 1
        $Completed[0].RunLabel | Should -Be 'main'
        @($global:Dso9Labels) | Should -Be @('main', 'reboot-r1')
    }
    It 'wyjątek pierwszego przebiegu: CompletedRuns jest puste, ale istnieje' {
        $global:Dso9Fail = 'main'
        $Caught = $null
        try { Invoke-DsoVmRunSet @script:SetArgs -Cases @(@{ Name = 'a'; Flow = 'Full' }) | Out-Null } catch { $Caught = $_.Exception }
        $Caught.Data.Contains('CompletedRuns') | Should -BeTrue
        @($Caught.Data['CompletedRuns']).Count | Should -Be 0
    }
    It 'przebieg tylko-pominięcia nie woła Invoke-DsoVmRun, zapisuje rekordy i report.json z passed 0' {
        $Cases = @(@{ Name = 's1'; TweakId = 'x.y'; Flow = 'Full'; Skip = 'powód pierwszy' }, @{ Name = 'r1'; TweakId = 'x.z'; Flow = 'Reboot' })
        $Runs = @(Invoke-DsoVmRunSet @script:SetArgs -Cases $Cases)
        @($global:Dso9Labels) | Should -Be @('reboot-r1')
        $Runs.Count | Should -Be 2
        $Skipped = @($Runs | Where-Object RunLabel -eq 'skipped')[0]
        $Skipped.Passed | Should -Be 0
        $Skipped.Failed | Should -Be 0
        $Skipped.Skipped | Should -Be 1
        $Report = Get-Content -LiteralPath $Skipped.ReportPath -Raw -Encoding UTF8 | ConvertFrom-Json
        $Report.pester.passed | Should -Be 0
        $Report.pester.skipped | Should -Be 1
        @($Report.cases).Count | Should -Be 1
        $Report.cases[0].Skipped | Should -BeTrue
        $Report.cases[0].SkipReason | Should -Be 'powód pierwszy'
        $Report.cases[0].Name | Should -Be 's1'
        Test-Path -LiteralPath (Join-Path (Split-Path $Skipped.ReportPath) 'records\s1.json') | Should -BeTrue
    }
    It 'same pominięte: jedyny przebieg to tylko-pominięcie, VM nietknięta' {
        $Runs = @(Invoke-DsoVmRunSet @script:SetArgs -Cases @(@{ Name = 's1'; TweakId = 'x.y'; Flow = 'Full'; Skip = 'p' }))
        $Runs.Count | Should -Be 1
        $Runs[0].RunLabel | Should -Be 'skipped'
        Should -Invoke -ModuleName VmHost Invoke-DsoVmRun -Times 0
    }
}

Describe 'C1 Task 1: Get-VmRunsExitCode - nigdy 0 bez zaliczonych testów' {
    It '<Opis> -> <Expected>' -ForEach @(
        @{ Opis = 'zaliczone, bez błędów'; Runs = @([pscustomobject]@{ Passed = 3; Failed = 0; Skipped = 0 }); Errors = 0; Expected = 0 }
        @{ Opis = 'zaliczone + przebieg tylko-pominięcia'; Runs = @([pscustomobject]@{ Passed = 0; Failed = 0; Skipped = 2 }, [pscustomobject]@{ Passed = 4; Failed = 0; Skipped = 0 }); Errors = 0; Expected = 0 }
        @{ Opis = 'same pominięte (0 zaliczonych)'; Runs = @([pscustomobject]@{ Passed = 0; Failed = 0; Skipped = 2 }); Errors = 0; Expected = 1 }
        @{ Opis = 'brak przebiegów'; Runs = @(); Errors = 0; Expected = 1 }
        @{ Opis = 'błąd testu'; Runs = @([pscustomobject]@{ Passed = 3; Failed = 1; Skipped = 0 }); Errors = 0; Expected = 1 }
        @{ Opis = 'błąd VM mimo zaliczonych'; Runs = @([pscustomobject]@{ Passed = 3; Failed = 0; Skipped = 0 }); Errors = 1; Expected = 1 }
    ) {
        Get-VmRunsExitCode -Runs $Runs -ErrorCount $Errors | Should -Be $Expected
    }
}

Describe 'Test-VmAuthFailure (wspólne rozpoznanie błędu uwierzytelnienia)' {
    It 'rozpoznaje wyjątek z flagą Data niesiony przez Wait-VmReady, także zawinięty w inny wyjątek' {
        $Inner = [System.InvalidOperationException]::new('cokolwiek')
        $Inner.Data['VmAuthFailure'] = $true
        $Outer = [System.Exception]::new('Przebieg x: opakowane', $Inner)
        Test-VmAuthFailure -Exception $Outer | Should -BeTrue
    }
    It 'rozpoznaje komunikat ze wspólnym znacznikiem (wyjątek spłaszczony do tekstu)' {
        $Ex = [System.Exception]::new("VM 'x': $(Get-VmAuthFailureMarker), przerywam: coś")
        Test-VmAuthFailure -Exception $Ex | Should -BeTrue
    }
    It 'zwykły błąd nie jest błędem uwierzytelnienia' {
        Test-VmAuthFailure -Exception ([System.Exception]::new('zerwana sesja')) | Should -BeFalse
    }
}

AfterAll { Clear-NoLiveVm }
