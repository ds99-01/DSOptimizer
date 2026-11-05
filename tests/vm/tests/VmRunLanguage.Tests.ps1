BeforeAll {
    # Wyłącznik: żaden test na mockach nie może dotknąć prawdziwego Hyper-V (patrz Support\NoLiveVm.ps1).
    . (Join-Path $PSScriptRoot 'Support\NoLiveVm.ps1')

    Import-Module (Join-Path $PSScriptRoot '..\modules\VmCommon.psm1') -Force
    Import-Module (Join-Path $PSScriptRoot '..\modules\VmRun.psm1') -Force
    $script:CatalogPath = Join-Path $PSScriptRoot '..\catalog\vm-test-catalog.json'

    function Write-CasesFile {
        param([string]$Path, [string]$CaseBody)
        Set-Content -LiteralPath $Path -Value "@{ Cases = @( @{ $CaseBody } ) }" -Encoding UTF8
    }
    $script:FullBody = "Name = 'x'; TweakId = 'vmtest.hklm-value'; Flow = 'Full'; RestorePoint = 'skip'; Expect = @{ Before = 'NotApplied'; ApplyExit = 0; ApplyOutcome = 'Applied'; AfterApply = 'Applied'; UndoExit = 0; UndoOutcome = 'Undone'; AfterUndo = 'NotApplied' }"
}

AfterAll { Clear-NoLiveVm }

Describe 'Klucz przypadku Language (język CLI przypadku)' {
    BeforeEach {
        $script:Dir = Join-Path $env:TEMP ('dso-case-language-' + [guid]::NewGuid().ToString('N'))
        New-Item -ItemType Directory -Path $script:Dir | Out-Null
        $script:CasesPath = Join-Path $script:Dir 'cases.psd1'
    }
    AfterEach { Remove-Item -LiteralPath $script:Dir -Recurse -Force -ErrorAction SilentlyContinue }

    It 'Language <Value> jest przyjmowany' -ForEach @(@{ Value = 'pl' }, @{ Value = 'en' }, @{ Value = 'system' }) {
        Write-CasesFile -Path $script:CasesPath -CaseBody "$script:FullBody; Language = '$Value'"
        $Cases = Import-VmCases -CasesPath $script:CasesPath -CatalogPath $script:CatalogPath
        $Cases[0].Language | Should -Be $Value
    }

    It 'Language spoza pl|en|system odrzuca import (<Value>)' -ForEach @(
        @{ Value = "'de'" }, @{ Value = "'EN'" }, @{ Value = "'System'" }, @{ Value = "''" }, @{ Value = '1' }, @{ Value = "@('en')" }
    ) {
        Write-CasesFile -Path $script:CasesPath -CaseBody "$script:FullBody; Language = $Value"
        { Import-VmCases -CasesPath $script:CasesPath -CatalogPath $script:CatalogPath } | Should -Throw -ExpectedMessage "*'Language'*pl*en*system*"
    }
}

Describe 'Resolve-VmCaseLanguage' {
    BeforeAll {
        $script:Plain = @{ Name = 'a'; TweakId = 'x.y'; Flow = 'Full' }
    }
    It 'bez Language: język VM w CliLanguage i TextLanguage (<Vm>)' -ForEach @(@{ Vm = 'en' }, @{ Vm = 'pl' }) {
        $R = @(Resolve-VmCaseLanguage -Cases @($script:Plain) -VmLanguage $Vm)
        $R[0].CliLanguage | Should -Be $Vm
        $R[0].TextLanguage | Should -Be $Vm
    }
    It 'Language en na VM pl: en i en' {
        $C = @{ Name = 'a'; Language = 'en' }
        $R = @(Resolve-VmCaseLanguage -Cases @($C) -VmLanguage pl)
        $R[0].CliLanguage | Should -Be 'en'
        $R[0].TextLanguage | Should -Be 'en'
    }
    It 'Language pl na VM en: pl i pl' {
        $C = @{ Name = 'a'; Language = 'pl' }
        $R = @(Resolve-VmCaseLanguage -Cases @($C) -VmLanguage en)
        $R[0].CliLanguage | Should -Be 'pl'
        $R[0].TextLanguage | Should -Be 'pl'
    }
    It 'Language system na VM en: CliLanguage system, TextLanguage en' {
        $C = @{ Name = 'a'; Language = 'system' }
        $R = @(Resolve-VmCaseLanguage -Cases @($C) -VmLanguage en)
        $R[0].CliLanguage | Should -Be 'system'
        $R[0].TextLanguage | Should -Be 'en'
    }
    It 'nie zmienia przypadków wejściowych i zwraca nowe kopie' {
        $C = @{ Name = 'a'; Language = 'en' }
        $R = @(Resolve-VmCaseLanguage -Cases @($C) -VmLanguage pl)
        $C.ContainsKey('CliLanguage') | Should -BeFalse
        $C.ContainsKey('TextLanguage') | Should -BeFalse
        [object]::ReferenceEquals($R[0], $C) | Should -BeFalse
        $R[0].Name | Should -Be 'a'
    }
    It 'zachowuje kolejność i liczbę przypadków' {
        $R = @(Resolve-VmCaseLanguage -Cases @(@{ Name = '1' }, @{ Name = '2'; Language = 'system' }, @{ Name = '3' }) -VmLanguage en)
        @($R.Name) | Should -Be @('1', '2', '3')
    }
    It 'VmLanguage spoza pl|en jest odrzucany' {
        { Resolve-VmCaseLanguage -Cases @($script:Plain) -VmLanguage system } | Should -Throw
    }
}

Describe 'Invoke-VmCase przekazuje CliLanguage do CLI' {
    BeforeEach {
        $global:DsoLanguageArgs = [System.Collections.Generic.List[string]]::new()
        $global:DsoLanguageValues = [System.Collections.Generic.List[string]]::new()
        Mock -ModuleName VmRun Invoke-VmSetup {}
        Mock -ModuleName VmRun Get-VmStateSnapshot { [pscustomobject]@{ Registry = @(); Services = @() } }
        Mock -ModuleName VmRun Invoke-VmCheck { 'Passed' }
        Mock -ModuleName VmRun Invoke-DsoCli {
            $global:DsoLanguageArgs.Add((@($Arguments) -join ' '))
            $global:DsoLanguageValues.Add($Language)
            [pscustomobject]@{ ExitCode = 0; TimedOut = $false; Report = [pscustomobject]@{ exitCode = 0 }; Json = '{}'; ReportError = $null }
        }
        $script:Layout = Get-DsoVmLayout
    }
    AfterEach { Remove-Variable -Name DsoLanguageArgs, DsoLanguageValues -Scope Global -ErrorAction SilentlyContinue }

    It 'CliLanguage en trafia do każdego z 5 wywołań CLI przebiegu Full, argumenty bez --lang' {
        $Case = @{ Name = 'c'; TweakId = 'a.b'; Flow = 'Full'; Catalog = 'BuiltIn'; RestorePoint = 'skip'; CliLanguage = 'en'; Snapshot = @{ Registry = @(); Services = @() }; Expect = @{} }
        $null = Invoke-VmCase -Session $null -Case $Case -Layout $script:Layout
        @($global:DsoLanguageValues) | Should -Be @('en', 'en', 'en', 'en', 'en')
        foreach ($Line in $global:DsoLanguageArgs) { $Line | Should -Not -Match '--lang' }
    }

    It 'krok CLI przypadku dostaje -Language system z CliLanguage (Execute)' {
        $Case = @{ Name = 'c'; TweakId = 'repair.dns-flush'; Flow = 'Execute'; Catalog = 'BuiltIn'; RestorePoint = 'skip'; CliLanguage = 'system'; Check = '$true'; Expect = @{ ExecuteExit = 0; ExecuteOutcome = 'Executed' } }
        $null = Invoke-VmCase -Session $null -Case $Case -Layout $script:Layout
        Should -Invoke -ModuleName VmRun Invoke-DsoCli -ParameterFilter { $Language -eq 'system' }
        @($global:DsoLanguageArgs) | Should -Be @('--execute repair.dns-flush --out C:\dso\out\c-execute.json')
    }

    It 'Invoke-VmCase bez CliLanguage = wyjątek' {
        $Case = @{ Name = 'c'; TweakId = 'a.b'; Flow = 'Full'; Catalog = 'BuiltIn'; RestorePoint = 'skip'; Snapshot = @{ Registry = @(); Services = @() }; Expect = @{} }
        { Invoke-VmCase -Session $null -Case $Case -Layout $script:Layout } | Should -Throw "*Przypadek 'c': brak CliLanguage*Resolve-VmCaseLanguage*"
        Should -Invoke -ModuleName VmRun Invoke-DsoCli -Times 0
    }

    It 'Invoke-VmCase Flow Autostart bez CliLanguage = wyjątek' {
        $Case = @{ Name = 'au'; Flow = 'Autostart'; Expect = @{} }
        { Invoke-VmCase -Session $null -Case $Case -Layout $script:Layout } | Should -Throw '*brak CliLanguage*'
    }
}