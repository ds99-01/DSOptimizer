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

    It 'Language <Value> jest przyjmowany' -ForEach @(@{ Value = 'pl' }, @{ Value = 'en' }) {
        Write-CasesFile -Path $script:CasesPath -CaseBody "$script:FullBody; Language = '$Value'"
        $Cases = Import-VmCases -CasesPath $script:CasesPath -CatalogPath $script:CatalogPath
        $Cases[0].Language | Should -Be $Value
    }

    It 'Language spoza pl|en odrzuca import (<Value>)' -ForEach @(
        @{ Value = "'de'" }, @{ Value = "'EN'" }, @{ Value = "''" }, @{ Value = '1' }, @{ Value = "@('en')" }
    ) {
        Write-CasesFile -Path $script:CasesPath -CaseBody "$script:FullBody; Language = $Value"
        { Import-VmCases -CasesPath $script:CasesPath -CatalogPath $script:CatalogPath } | Should -Throw -ExpectedMessage "*'Language'*pl*en*"
    }
}

Describe 'Invoke-VmCase przekazuje Language do CLI' {
    BeforeEach {
        $global:DsoLanguageArgs = [System.Collections.Generic.List[string]]::new()
        Mock -ModuleName VmRun Invoke-VmSetup {}
        Mock -ModuleName VmRun Get-VmStateSnapshot { [pscustomobject]@{ Registry = @(); Services = @() } }
        Mock -ModuleName VmRun Invoke-VmCheck { 'Passed' }
        Mock -ModuleName VmRun Invoke-DsoCli {
            $global:DsoLanguageArgs.Add((@($Arguments) -join ' '))
            [pscustomobject]@{ ExitCode = 0; TimedOut = $false; Report = [pscustomobject]@{ exitCode = 0 }; Json = '{}'; ReportError = $null }
        }
        $script:Layout = Get-DsoVmLayout
    }
    AfterEach { Remove-Variable -Name DsoLanguageArgs -Scope Global -ErrorAction SilentlyContinue }

    It 'Language en dokleja --lang en do każdego wywołania CLI przebiegu Full' {
        $Case = @{ Name = 'c'; TweakId = 'a.b'; Flow = 'Full'; Catalog = 'BuiltIn'; RestorePoint = 'skip'; Language = 'en'; Snapshot = @{ Registry = @(); Services = @() }; Expect = @{} }
        $null = Invoke-VmCase -Session $null -Case $Case -Layout $script:Layout
        @($global:DsoLanguageArgs).Count | Should -Be 5
        foreach ($Line in $global:DsoLanguageArgs) {
            $Line | Should -Match ' --lang en$'
            ([regex]::Matches($Line, '--lang')).Count | Should -Be 1
        }
    }

    It 'Language pl dokleja --lang pl (także w Execute)' {
        $Case = @{ Name = 'c'; TweakId = 'repair.dns-flush'; Flow = 'Execute'; Catalog = 'BuiltIn'; RestorePoint = 'skip'; Language = 'pl'; Check = '$true'; Expect = @{ ExecuteExit = 0; ExecuteOutcome = 'Executed' } }
        $null = Invoke-VmCase -Session $null -Case $Case -Layout $script:Layout
        @($global:DsoLanguageArgs) | Should -Be @('--execute repair.dns-flush --out C:\dso\out\c-execute.json --lang pl')
    }

    It 'bez Language przypadek nie podaje --lang (Invoke-DsoCli dokleja wtedy --lang pl)' {
        $Case = @{ Name = 'c'; TweakId = 'a.b'; Flow = 'Full'; Catalog = 'BuiltIn'; RestorePoint = 'skip'; Snapshot = @{ Registry = @(); Services = @() }; Expect = @{} }
        $null = Invoke-VmCase -Session $null -Case $Case -Layout $script:Layout
        @($global:DsoLanguageArgs).Count | Should -Be 5
        $global:DsoLanguageArgs | ForEach-Object { $_ | Should -Not -Match '--lang' }
    }
}

Describe 'Invoke-DsoCli z --lang en od przypadku' {
    It 'nie dokleja --lang pl, gdy argumenty mają już --lang en' {
        Mock -ModuleName VmRun Invoke-Command -RemoveParameterValidation 'Session' -MockWith { $global:DsoLangArgs = $ArgumentList[1]; @{ ExitCode = 0; TimedOut = $false; Json = $null } }
        $null = Invoke-DsoCli -Session $null -ExePath 'x' -Arguments @('--apply', 'a.b', '--out', 'C:\o.json', '--lang', 'en') -OutPath 'C:\o.json'
        @($global:DsoLangArgs) | Should -Be @('--apply', 'a.b', '--out', 'C:\o.json', '--lang', 'en')
        Remove-Variable -Name DsoLangArgs -Scope Global -ErrorAction SilentlyContinue
    }
}
