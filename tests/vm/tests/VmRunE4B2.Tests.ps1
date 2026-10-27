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
    $script:ExecuteBody = "Name = 'dlugi'; TweakId = 'repair.system-files'; Flow = 'Execute'; Catalog = 'BuiltIn'; RestorePoint = 'skip'; Check = '`$true'; Expect = @{ ExecuteExit = 0; ExecuteOutcome = 'Executed' }"
}

Describe 'Etap 4b partia 2 T6: klucz CliTimeoutSeconds (limit CLI dla długich akcji)' {
    BeforeEach {
        $script:Dir = Join-Path $env:TEMP ('dso-cli-timeout-' + [guid]::NewGuid().ToString('N'))
        New-Item -ItemType Directory -Path $script:Dir | Out-Null
        $script:CasesPath = Join-Path $script:Dir 'cases.psd1'
    }
    AfterEach { Remove-Item -LiteralPath $script:Dir -Recurse -Force -ErrorAction SilentlyContinue }

    It 'Execute z CliTimeoutSeconds jest przyjmowany' {
        Write-CasesFile -Path $script:CasesPath -CaseBody "$script:ExecuteBody; CliTimeoutSeconds = 1200"
        $Cases = Import-VmCases -CasesPath $script:CasesPath -CatalogPath $script:CatalogPath
        $Cases[0].CliTimeoutSeconds | Should -Be 1200
    }

    It 'Flow <Flow> z CliTimeoutSeconds jest odrzucany' -ForEach @(
        @{ Flow = 'Full'; Body = "Name = 'x'; TweakId = 'vmtest.hklm-value'; Flow = 'Full'; RestorePoint = 'skip'; CliTimeoutSeconds = 1200; Expect = @{ Before = 'NotApplied'; ApplyExit = 0; ApplyOutcome = 'Applied'; AfterApply = 'Applied'; UndoExit = 0; UndoOutcome = 'Undone'; AfterUndo = 'NotApplied' }" }
        @{ Flow = 'DetectOnly'; Body = "Name = 'x'; TweakId = 'vmtest.hklm-value'; Flow = 'DetectOnly'; RestorePoint = 'skip'; CliTimeoutSeconds = 1200; Expect = @{ Before = 'NotApplied' }" }
        @{ Flow = 'AppRemove'; Body = "Name = 'x'; AppId = 'appx:x'; Flow = 'AppRemove'; RestorePoint = 'skip'; CliTimeoutSeconds = 1200; Check = '`$true'; Expect = @{ ListedBefore = `$true; RemoveExit = 0; RemoveOutcome = 'Removed'; ListedAfter = `$false }" }
    ) {
        Write-CasesFile -Path $script:CasesPath -CaseBody $Body
        { Import-VmCases -CasesPath $script:CasesPath -CatalogPath $script:CatalogPath } | Should -Throw -ExpectedMessage "*CliTimeoutSeconds*Execute*"
    }

    It 'wartość <Value> jest odrzucana (liczba całkowita 60–7200)' -ForEach @(
        @{ Value = '0' }, @{ Value = '59' }, @{ Value = '7201' }, @{ Value = "'1200'" }, @{ Value = '12.5' }
    ) {
        Write-CasesFile -Path $script:CasesPath -CaseBody "$script:ExecuteBody; CliTimeoutSeconds = $Value"
        { Import-VmCases -CasesPath $script:CasesPath -CatalogPath $script:CatalogPath } | Should -Throw -ExpectedMessage "*CliTimeoutSeconds*60*7200*"
    }
}

Describe 'Etap 4b partia 2 T6: Invoke-VmCase przekazuje CliTimeoutSeconds do Invoke-DsoCli' {
    BeforeEach {
        $global:DsoCliTimeouts = [System.Collections.Generic.List[string]]::new()
        Mock -ModuleName VmRun Invoke-VmSetup {}
        Mock -ModuleName VmRun Get-VmStateSnapshot { @{ Registry = @(); Services = @() } }
        Mock -ModuleName VmRun Invoke-VmCheck { 'Passed' }
        Mock -ModuleName VmRun Invoke-DsoCli {
            $Bound = if ($TimeoutSeconds) { [string]$TimeoutSeconds } else { 'domyślny' }
            $global:DsoCliTimeouts.Add(($Arguments[0]) + '|' + $Bound + '|as:' + $RunAsUser)
            [pscustomobject]@{ ExitCode = 0; TimedOut = $false; Report = [pscustomobject]@{ exitCode = 0 }; Json = '{}'; ReportError = $null }
        }
        $script:Layout = Get-DsoVmLayout
    }
    AfterEach { Remove-Variable -Name DsoCliTimeouts -Scope Global -ErrorAction SilentlyContinue }

    It 'Execute z kluczem: krok execute dostaje -TimeoutSeconds z przypadku (także z RunAs)' {
        $Case = @{ Name = 'dlugi'; TweakId = 'repair.system-files'; Flow = 'Execute'; Catalog = 'BuiltIn'; RestorePoint = 'skip'; RunAs = 'dso'; CliTimeoutSeconds = 2700; Check = '$true'
            Expect = @{ ExecuteExit = 0; ExecuteOutcome = 'Executed' } }
        $Record = Invoke-VmCase -Session $null -Case $Case -Layout $script:Layout
        $Record.CheckResult | Should -Be 'Passed'
        @($global:DsoCliTimeouts) | Should -Be @('--execute|2700|as:dso')
    }

    It 'Execute bez klucza: Invoke-DsoCli z domyślnym limitem (600 s)' {
        $Case = @{ Name = 'krotki'; TweakId = 'repair.dns-flush'; Flow = 'Execute'; Catalog = 'BuiltIn'; RestorePoint = 'skip'; Check = '$true'
            Expect = @{ ExecuteExit = 0; ExecuteOutcome = 'Executed' } }
        $null = Invoke-VmCase -Session $null -Case $Case -Layout $script:Layout
        @($global:DsoCliTimeouts) | Should -Be @('--execute|domyślny|as:')
    }

    It 'domyślny limit Invoke-DsoCli to nadal 600 s' {
        (Get-Command Invoke-DsoCli).Parameters['TimeoutSeconds'].Attributes | Out-Null
        $Default = (Get-Command Invoke-DsoCli).ScriptBlock.Ast.Body.ParamBlock.Parameters | Where-Object { $_.Name.VariablePath.UserPath -eq 'TimeoutSeconds' }
        $Default.DefaultValue.Extent.Text | Should -Be '600'
    }
}
