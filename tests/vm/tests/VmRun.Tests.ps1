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
    $script:ValidExpectFull = "Before = 'NotApplied'; ApplyExit = 0; ApplyOutcome = 'Applied'; AfterApply = 'Applied'; UndoExit = 0; UndoOutcome = 'Undone'; AfterUndo = 'NotApplied'"
}

Describe 'Import-VmCases' {
    It 'wczytuje prawdziwe cases.psd1 zgodne z katalogiem' {
        $Cases = Import-VmCases -CasesPath "$script:VmRoot\cases.psd1" -CatalogPath $script:CatalogPath
        $Expected = @((Import-PowerShellDataFile "$script:VmRoot\cases.psd1").Cases).Count
        $Expected | Should -BeGreaterThan 0
        $Cases.Count | Should -Be $Expected
        ($Cases.Name | Select-Object -Unique).Count | Should -Be $Expected
    }
    It 'nieznany TweakId' {
        Write-CasesFile -Path "$TestDrive\bad1.psd1" -CaseBody "Name = 'n'; TweakId = 'vmtest.brak'; Flow = 'Full'; RestorePoint = 'skip'; Expect = @{ $script:ValidExpectFull }"
        { Import-VmCases -CasesPath "$TestDrive\bad1.psd1" -CatalogPath $script:CatalogPath } | Should -Throw -ExpectedMessage '*vmtest.brak*'
    }
    It 'nieznany klucz w Expect' {
        Write-CasesFile -Path "$TestDrive\bad2.psd1" -CaseBody "Name = 'n'; TweakId = 'vmtest.hklm-value'; Flow = 'Full'; RestorePoint = 'skip'; Expect = @{ $script:ValidExpectFull; Cokolwiek = 1 }"
        { Import-VmCases -CasesPath "$TestDrive\bad2.psd1" -CatalogPath $script:CatalogPath } | Should -Throw -ExpectedMessage '*Cokolwiek*'
    }
    It 'Flow ApplyOnly z UndoOutcome' {
        Write-CasesFile -Path "$TestDrive\bad3.psd1" -CaseBody "Name = 'n'; TweakId = 'vmtest.hklm-value'; Flow = 'ApplyOnly'; RestorePoint = 'skip'; Expect = @{ Before = 'NotApplied'; ApplyExit = 0; ApplyOutcome = 'Applied'; AfterApply = 'Applied'; UndoOutcome = 'Undone' }"
        { Import-VmCases -CasesPath "$TestDrive\bad3.psd1" -CatalogPath $script:CatalogPath } | Should -Throw -ExpectedMessage '*UndoOutcome*'
    }
    It 'Flow Full bez AfterUndo' {
        Write-CasesFile -Path "$TestDrive\bad4.psd1" -CaseBody "Name = 'n'; TweakId = 'vmtest.hklm-value'; Flow = 'Full'; RestorePoint = 'skip'; Expect = @{ Before = 'NotApplied'; ApplyExit = 0; ApplyOutcome = 'Applied'; AfterApply = 'Applied'; UndoExit = 0; UndoOutcome = 'Undone' }"
        { Import-VmCases -CasesPath "$TestDrive\bad4.psd1" -CatalogPath $script:CatalogPath } | Should -Throw -ExpectedMessage '*AfterUndo*'
    }
    It 'brak wymaganego klucza Expect (ApplyExit)' {
        Write-CasesFile -Path "$TestDrive\bad5.psd1" -CaseBody "Name = 'n'; TweakId = 'vmtest.hklm-value'; Flow = 'ApplyOnly'; RestorePoint = 'skip'; Expect = @{ Before = 'NotApplied'; ApplyOutcome = 'Applied'; AfterApply = 'Applied' }"
        { Import-VmCases -CasesPath "$TestDrive\bad5.psd1" -CatalogPath $script:CatalogPath } | Should -Throw -ExpectedMessage '*ApplyExit*'
    }
    It 'nieznany klucz przypadku' {
        Write-CasesFile -Path "$TestDrive\bad6.psd1" -CaseBody "Name = 'n6'; TweakId = 'vmtest.hklm-value'; Flow = 'Full'; RestorePoint = 'skip'; Obce = 1; Expect = @{ $script:ValidExpectFull }"
        { Import-VmCases -CasesPath "$TestDrive\bad6.psd1" -CatalogPath $script:CatalogPath } | Should -Throw -ExpectedMessage '*Obce*'
    }
    It 'zły Flow' {
        Write-CasesFile -Path "$TestDrive\bad7.psd1" -CaseBody "Name = 'n7'; TweakId = 'vmtest.hklm-value'; Flow = 'Pol'; RestorePoint = 'skip'; Expect = @{ $script:ValidExpectFull }"
        { Import-VmCases -CasesPath "$TestDrive\bad7.psd1" -CatalogPath $script:CatalogPath } | Should -Throw -ExpectedMessage '*Flow*'
    }
    It 'zły RestorePoint' {
        Write-CasesFile -Path "$TestDrive\bad8.psd1" -CaseBody "Name = 'n8'; TweakId = 'vmtest.hklm-value'; Flow = 'Full'; RestorePoint = 'maybe'; Expect = @{ $script:ValidExpectFull }"
        { Import-VmCases -CasesPath "$TestDrive\bad8.psd1" -CatalogPath $script:CatalogPath } | Should -Throw -ExpectedMessage '*RestorePoint*'
    }
    It 'zduplikowana nazwa' {
        $One = "@{ Name = 'dup'; TweakId = 'vmtest.hklm-value'; Flow = 'Full'; RestorePoint = 'skip'; Expect = @{ $script:ValidExpectFull } }"
        Set-Content -LiteralPath "$TestDrive\bad9.psd1" -Value "@{ Cases = @( $One; $One ) }" -Encoding UTF8
        { Import-VmCases -CasesPath "$TestDrive\bad9.psd1" -CatalogPath $script:CatalogPath } | Should -Throw -ExpectedMessage '*Name*'
    }
}

Describe 'Expand-VmCasePlaceholders' {
    It 'podmienia {UserSid} w zagnieżdżonych strukturach' {
        $Case = @{ Name = 'x'; Setup = @('a {UserSid}'); Snapshot = @{ Registry = @(@{ Path = 'HKU\{UserSid}\S'; Name = 'V' }) }; Expect = @{ UserSid = '{UserSid}' } }
        $Out = Expand-VmCasePlaceholders -Case $Case -UserSid 'S-1-5-21-9'
        $Out.Setup[0] | Should -Be 'a S-1-5-21-9'
        $Out.Snapshot.Registry[0].Path | Should -Be 'HKU\S-1-5-21-9\S'
        $Out.Expect.UserSid | Should -Be 'S-1-5-21-9'
        $Case.Expect.UserSid | Should -Be '{UserSid}'
    }
}

Describe 'ConvertTo-DsoCliArguments' {
    It 'apply z restore point' {
        ConvertTo-DsoCliArguments -Command apply -TweakId 'a.b' -CatalogPath 'C:\dso\k.json' -OutPath 'C:\dso\out\x.json' -RestorePoint skip |
            Should -Be @('--apply', 'a.b', '--catalog', 'C:\dso\k.json', '--out', 'C:\dso\out\x.json', '--restore-point', 'skip')
    }
    It 'detect bez restore point' { (ConvertTo-DsoCliArguments -Command detect -TweakId 'a.b' -CatalogPath 'C:\k.json' -OutPath 'C:\o.json') -contains '--restore-point' | Should -BeFalse }
    It 'RestorePoint ignorowany poza apply' { (ConvertTo-DsoCliArguments -Command undo -TweakId 'a.b' -CatalogPath 'C:\k.json' -OutPath 'C:\o.json' -RestorePoint skip) -contains '--restore-point' | Should -BeFalse }
    It 'odrzuca ścieżkę ze spacją' { { ConvertTo-DsoCliArguments -Command detect -TweakId 'a.b' -CatalogPath 'C:\a b\k.json' -OutPath 'C:\o.json' } | Should -Throw -ExpectedMessage '*spacj*' }
}

Describe 'Invoke-DsoCli' {
    It 'parsuje raport' {
        Mock -ModuleName VmRun Invoke-Command -RemoveParameterValidation 'Session' -MockWith { @{ ExitCode = 0; TimedOut = $false; Json = '{"exitCode":0,"state":"Applied"}' } }
        $Result = Invoke-DsoCli -Session $null -ExePath 'C:\dso\app\DSOptimizer.exe' -Arguments @('--detect', 'a.b') -OutPath 'C:\dso\out\a.json'
        $Result.Report.state | Should -Be 'Applied'
    }
    It 'dokleja --lang pl na końcu argumentów (PS Direct)' {
        Mock -ModuleName VmRun Invoke-Command -RemoveParameterValidation 'Session' -MockWith { $global:DsoLangArgs = $ArgumentList[1]; @{ ExitCode = 0; TimedOut = $false; Json = $null } }
        $null = Invoke-DsoCli -Session $null -ExePath 'x' -Arguments @('--detect', 'a.b', '--out', 'C:\o.json') -OutPath 'C:\o.json'
        @($global:DsoLangArgs) | Should -Be @('--detect', 'a.b', '--out', 'C:\o.json', '--lang', 'pl')
        Remove-Variable -Name DsoLangArgs -Scope Global -ErrorAction SilentlyContinue
    }
    It 'bez argumentów też dostaje samo --lang pl' {
        Mock -ModuleName VmRun Invoke-Command -RemoveParameterValidation 'Session' -MockWith { $global:DsoLangArgs = $ArgumentList[1]; @{ ExitCode = 0; TimedOut = $false; Json = $null } }
        $null = Invoke-DsoCli -Session $null -ExePath 'x' -Arguments @() -OutPath 'C:\o.json'
        @($global:DsoLangArgs) | Should -Be @('--lang', 'pl')
        Remove-Variable -Name DsoLangArgs -Scope Global -ErrorAction SilentlyContinue
    }
    It 'dokleja --lang pl także w gałęzi -RunAsUser (zadanie Harmonogramu)' {
        Mock -ModuleName VmRun Invoke-Command -RemoveParameterValidation 'Session' -MockWith { $global:DsoLangArgs = $ArgumentList[1]; @{ ExitCode = 0; TimedOut = $false; Json = $null } }
        $null = Invoke-DsoCli -Session $null -ExePath 'x' -Arguments @('--list-apps') -OutPath 'C:\o.json' -RunAsUser 'dso'
        @($global:DsoLangArgs) | Should -Be @('--list-apps', '--lang', 'pl')
        Remove-Variable -Name DsoLangArgs -Scope Global -ErrorAction SilentlyContinue
    }
    It 'nie dubluje --lang, gdy wywołujący już go podał' {
        Mock -ModuleName VmRun Invoke-Command -RemoveParameterValidation 'Session' -MockWith { $global:DsoLangArgs = $ArgumentList[1]; @{ ExitCode = 0; TimedOut = $false; Json = $null } }
        $null = Invoke-DsoCli -Session $null -ExePath 'x' -Arguments @('--list-apps', '--lang', 'en') -OutPath 'C:\o.json'
        @($global:DsoLangArgs) | Should -Be @('--list-apps', '--lang', 'en')
        Remove-Variable -Name DsoLangArgs -Scope Global -ErrorAction SilentlyContinue
    }
    It 'bez pliku JSON zwraca Report = null i zachowuje ExitCode' {
        Mock -ModuleName VmRun Invoke-Command -RemoveParameterValidation 'Session' -MockWith { @{ ExitCode = 740; TimedOut = $false; Json = $null } }
        $Result = Invoke-DsoCli -Session $null -ExePath 'x' -Arguments @() -OutPath 'C:\o.json'
        $Result.Report | Should -BeNullOrEmpty
        $Result.ExitCode | Should -Be 740
    }
}

Describe 'Get-DsoStateSnapshot (lokalnie, tylko odczyt)' {
    It 'czyta wartość z TestRegistry i usługę EventLog, brakujące oznacza Exists = false' {
        New-Item -Path 'TestRegistry:\S' -Force | Out-Null
        New-ItemProperty -Path 'TestRegistry:\S' -Name 'V' -Value 7 -PropertyType DWord | Out-Null
        $Root = (Get-PSDrive TestRegistry).Root -replace '^HKEY_CURRENT_USER', 'HKCU'
        $Snap = Get-DsoStateSnapshot -Registry @(@{ Path = "$Root\S"; Name = 'V' }, @{ Path = "$Root\S"; Name = 'Brak' }) -Services @('EventLog', 'DsoNoSuchService') -AllowCurrentUser $true
        ($Snap.Registry | Where-Object Key -like '*\V').Data | Should -Be '7'
        ($Snap.Registry | Where-Object Key -like '*\V').Kind | Should -Be 'DWord'
        ($Snap.Registry | Where-Object Key -like '*\Brak').Exists | Should -BeFalse
        ($Snap.Services | Where-Object Name -eq 'EventLog').Exists | Should -BeTrue
        ($Snap.Services | Where-Object Name -eq 'DsoNoSuchService').Exists | Should -BeFalse
    }
}

Describe 'Compare-VmSnapshot / Test-VmSnapshotMatches' {
    BeforeAll {
        $script:A = [pscustomobject]@{ Registry = @([pscustomobject]@{ Key = 'HKLM\S\V'; Exists = $true; Kind = 'DWord'; Data = '1' }); Services = @([pscustomobject]@{ Name = 'X'; Exists = $true; StartMode = 'Manual'; DelayedAutoStart = $false; State = 'Stopped' }) }
    }
    It 'równe = brak różnic' { Compare-VmSnapshot -Before $script:A -After $script:A | Should -BeNullOrEmpty }
    It 'zmiana danych i typu startu' {
        $B = $script:A | ConvertTo-Json -Depth 5 | ConvertFrom-Json
        $B.Registry[0].Data = '2'; $B.Services[0].StartMode = 'Disabled'
        $Diff = Compare-VmSnapshot -Before $script:A -After $B
        $Diff.Count | Should -Be 2
        ($Diff -join ' ') | Should -Match 'HKLM\\S\\V'
    }
    It 'oczekiwanie częściowe' {
        Test-VmSnapshotMatches -Snapshot $script:A -Expected @{ Services = @(@{ Name = 'X'; StartMode = 'Manual' }) } | Should -BeNullOrEmpty
        Test-VmSnapshotMatches -Snapshot $script:A -Expected @{ Services = @(@{ Name = 'X'; StartMode = 'Auto' }) } | Should -Not -BeNullOrEmpty
    }
    It 'oczekiwanie rejestru po Path i Name' {
        Test-VmSnapshotMatches -Snapshot $script:A -Expected @{ Registry = @(@{ Path = 'HKLM\S'; Name = 'V'; Kind = 'DWord'; Data = '1' }) } | Should -BeNullOrEmpty
        Test-VmSnapshotMatches -Snapshot $script:A -Expected @{ Registry = @(@{ Path = 'HKLM\S'; Name = 'V'; Data = '9' }) } | Should -Not -BeNullOrEmpty
        Test-VmSnapshotMatches -Snapshot $script:A -Expected @{ Registry = @(@{ Path = 'HKLM\S'; Name = 'Brak'; Exists = $true }) } | Should -Not -BeNullOrEmpty
    }
}

Describe 'Invoke-VmCase' {
    BeforeEach {
        Mock -ModuleName VmRun Invoke-VmSetup {}
        Mock -ModuleName VmRun Get-VmStateSnapshot { [pscustomobject]@{ Registry = @(); Services = @() } }
        Mock -ModuleName VmRun Get-VmRestorePointCount { 1 }
        Mock -ModuleName VmRun Start-Sleep {}
        Mock -ModuleName VmRun Invoke-DsoCli { [pscustomobject]@{ ExitCode = 0; TimedOut = $false; Report = [pscustomobject]@{ exitCode = 0 }; Json = '{}'; ReportError = $null } }
        $script:Layout = Get-DsoVmLayout
    }
    It 'Full: 5 wywołań CLI w kolejności detect, apply, detect, undo, detect' {
        $Case = @{ Name = 'c'; TweakId = 'a.b'; Flow = 'Full'; RestorePoint = 'skip'; Setup = @('x'); Snapshot = @{ Registry = @(); Services = @() }; Expect = @{} }
        $Record = Invoke-VmCase -Session $null -Case $Case -Layout $script:Layout
        $Record.Steps.Step | Should -Be @('detect-before', 'apply', 'detect-after-apply', 'undo', 'detect-after-undo')
        Should -Invoke -ModuleName VmRun Invoke-VmSetup -Times 1
    }
    It 'ApplyOnly: 3 wywołania' {
        $Case = @{ Name = 'c'; TweakId = 'a.b'; Flow = 'ApplyOnly'; RestorePoint = 'skip'; Setup = @(); Snapshot = @{ Registry = @(); Services = @() }; Expect = @{} }
        $Record = Invoke-VmCase -Session $null -Case $Case -Layout $script:Layout
        @($Record.Steps).Count | Should -Be 3
        $Record.Steps.Step | Should -Be @('detect-before', 'apply', 'detect-after-apply')
    }
    It 'Invoke-VmCase używa innej ścieżki --out dla każdego wywołania' {
        $Case = @{ Name = 'c'; TweakId = 'a.b'; Flow = 'Full'; RestorePoint = 'skip'; Setup = @(); Snapshot = @{ Registry = @(); Services = @() }; Expect = @{} }
        $global:DsoOutPaths = [System.Collections.Generic.List[string]]::new()
        Mock -ModuleName VmRun Invoke-DsoCli { $global:DsoOutPaths.Add($OutPath); [pscustomobject]@{ ExitCode = 0; TimedOut = $false; Report = [pscustomobject]@{ exitCode = 0 }; Json = '{}'; ReportError = $null } }
        Invoke-VmCase -Session $null -Case $Case -Layout $script:Layout | Out-Null
        $global:DsoOutPaths.Count | Should -Be 5
        ($global:DsoOutPaths | Select-Object -Unique).Count | Should -Be 5
        $global:DsoOutPaths[0] | Should -Be 'C:\dso\out\c-detect-before.json'
        Remove-Variable -Name DsoOutPaths -Scope Global
    }
    It 'NotWrittenTo trafia do Get-VmStateSnapshot opakowane w Registry i Services' {
        $Case = @{ Name = 'c'; TweakId = 'a.b'; Flow = 'ApplyOnly'; RestorePoint = 'skip'; Setup = @(); Snapshot = @{ Registry = @(); Services = @() }
            Expect = @{ NotWrittenTo = @(@{ Path = 'HKCU\S'; Name = 'V' }) } }
        $global:DsoSnapArgs = [System.Collections.Generic.List[object]]::new()
        Mock -ModuleName VmRun Get-VmStateSnapshot { $global:DsoSnapArgs.Add($Snapshot); [pscustomobject]@{ Registry = @(); Services = @() } }
        $Record = Invoke-VmCase -Session $null -Case $Case -Layout $script:Layout
        $Wrapped = @($global:DsoSnapArgs | Where-Object { @($_.Registry).Count -eq 1 })
        $Wrapped.Count | Should -Be 2 -Because 'odczyt NotWrittenTo przed apply i po apply (ApplyOnly bez undo)'
        @($Wrapped[0].Services).Count | Should -Be 0
        $Record.NotWrittenTo | Should -Not -BeNullOrEmpty
        Remove-Variable -Name DsoSnapArgs -Scope Global
    }
    It 'liczy punkty przywracania tylko gdy Expect.RestorePointCreated; brak oczekiwanego wzrostu = jeden odczyt po apply' {
        $Case = @{ Name = 'c'; TweakId = 'a.b'; Flow = 'ApplyOnly'; RestorePoint = 'required'; Setup = @(); Snapshot = @{ Registry = @(); Services = @() }; Expect = @{ RestorePointCreated = $false } }
        $Record = Invoke-VmCase -Session $null -Case $Case -Layout $script:Layout
        $Record.RestorePointCountBefore | Should -Be 1
        $Record.RestorePointCountAfter | Should -Be 1
        Should -Invoke -ModuleName VmRun Get-VmRestorePointCount -Times 2 -Exactly
        Should -Invoke -ModuleName VmRun Start-Sleep -Times 0 -Exactly
        $NoExpect = @{ Name = 'c'; TweakId = 'a.b'; Flow = 'ApplyOnly'; RestorePoint = 'skip'; Setup = @(); Snapshot = @{ Registry = @(); Services = @() }; Expect = @{} }
        $Rec2 = Invoke-VmCase -Session $null -Case $NoExpect -Layout $script:Layout
        $Rec2.RestorePointCountBefore | Should -BeNullOrEmpty
        $Rec2.RestorePointCountAfter | Should -BeNullOrEmpty
    }
    It 'RestorePointCreated = $true: licznik po apply odpytywany do wzrostu (wyścig pomiaru)' {
        $global:DsoCounts = [System.Collections.Generic.Queue[int]]::new([int[]]@(1, 1, 1, 2))
        Mock -ModuleName VmRun Get-VmRestorePointCount { $global:DsoCounts.Dequeue() }
        $Case = @{ Name = 'c'; TweakId = 'a.b'; Flow = 'ApplyOnly'; RestorePoint = 'required'; Setup = @(); Snapshot = @{ Registry = @(); Services = @() }; Expect = @{ RestorePointCreated = $true } }
        $Record = Invoke-VmCase -Session $null -Case $Case -Layout $script:Layout
        $Record.RestorePointCountBefore | Should -Be 1
        $Record.RestorePointCountAfter | Should -Be 2
        Should -Invoke -ModuleName VmRun Get-VmRestorePointCount -Times 4 -Exactly
        Should -Invoke -ModuleName VmRun Start-Sleep -Times 2 -Exactly -ParameterFilter { $Milliseconds -eq 250 }
        Remove-Variable -Name DsoCounts -Scope Global
    }
    It 'RestorePointCreated = $true: licznik nie rośnie = odpytywanie kończy się po 10 s (41 odczytów po apply)' {
        $Case = @{ Name = 'c'; TweakId = 'a.b'; Flow = 'ApplyOnly'; RestorePoint = 'required'; Setup = @(); Snapshot = @{ Registry = @(); Services = @() }; Expect = @{ RestorePointCreated = $true } }
        $Record = Invoke-VmCase -Session $null -Case $Case -Layout $script:Layout
        $Record.RestorePointCountBefore | Should -Be 1
        $Record.RestorePointCountAfter | Should -Be 1
        Should -Invoke -ModuleName VmRun Get-VmRestorePointCount -Times 42 -Exactly
        Should -Invoke -ModuleName VmRun Start-Sleep -Times 40 -Exactly -ParameterFilter { $Milliseconds -eq 250 }
    }
    It 'RestorePointCreated = $true: od razu wzrost = brak czekania' {
        $global:DsoCounts = [System.Collections.Generic.Queue[int]]::new([int[]]@(3, 4))
        Mock -ModuleName VmRun Get-VmRestorePointCount { $global:DsoCounts.Dequeue() }
        $Case = @{ Name = 'c'; TweakId = 'a.b'; Flow = 'ApplyOnly'; RestorePoint = 'required'; Setup = @(); Snapshot = @{ Registry = @(); Services = @() }; Expect = @{ RestorePointCreated = $true } }
        $Record = Invoke-VmCase -Session $null -Case $Case -Layout $script:Layout
        $Record.RestorePointCountAfter | Should -Be 4
        Should -Invoke -ModuleName VmRun Start-Sleep -Times 0 -Exactly
        Remove-Variable -Name DsoCounts -Scope Global
    }
}

Describe 'Get-VmRestorePointCount' {
    BeforeEach {
        Mock -ModuleName VmRun Invoke-Command { & $ScriptBlock } -RemoveParameterValidation Session
    }
    It 'błąd zapytania nie jest maskowany jako 0 (ErrorAction Stop)' {
        Mock -ModuleName VmRun Get-ComputerRestorePoint { throw 'RPC niedostępny' } -ParameterFilter { $ErrorAction -eq 'Stop' }
        { Get-VmRestorePointCount -Session $null } | Should -Throw -ExpectedMessage '*RPC niedostępny*'
    }
    It 'brak punktów (pusta lista, np. wyłączona ochrona) = 0, bez błędu' {
        Mock -ModuleName VmRun Get-ComputerRestorePoint { }
        Get-VmRestorePointCount -Session $null | Should -Be 0
    }
    It 'zwraca liczbę punktów' {
        Mock -ModuleName VmRun Get-ComputerRestorePoint { [pscustomobject]@{ SequenceNumber = 1 }; [pscustomobject]@{ SequenceNumber = 2 } }
        Get-VmRestorePointCount -Session $null | Should -Be 2
    }
}

Describe 'Poprawki rundy 1' {
    It 'Compare-VmSnapshot widzi zmianę samej wielkości liter w Data' {
        $A = [pscustomobject]@{ Registry = @([pscustomobject]@{ Key = 'HKLM\S\V'; Exists = $true; Kind = 'String'; Data = 'zażółć' }); Services = @() }
        $B = [pscustomobject]@{ Registry = @([pscustomobject]@{ Key = 'HKLM\S\V'; Exists = $true; Kind = 'String'; Data = 'ZAŻÓŁĆ' }); Services = @() }
        $Diff = Compare-VmSnapshot -Before $A -After $B
        @($Diff).Count | Should -Be 1
    }
    It 'Test-VmSnapshotMatches rozróżnia wielkość liter' {
        $A = [pscustomobject]@{ Registry = @([pscustomobject]@{ Key = 'HKU\S\V'; Exists = $true; Kind = 'String'; Data = 'vm-1b' }); Services = @() }
        $Problems = Test-VmSnapshotMatches -Snapshot $A -Expected @{ Registry = @(@{ Path = 'HKU\S'; Name = 'V'; Data = 'VM-1B' }) }
        @($Problems).Count | Should -Be 1
    }
    It 'zero różnic i zero niezgodności to pusta tablica, jedna to tablica jednoelementowa' {
        $A = [pscustomobject]@{ Registry = @([pscustomobject]@{ Key = 'K\V'; Exists = $true; Kind = 'DWord'; Data = '1' }); Services = @() }
        $Diff = Compare-VmSnapshot -Before $A -After $A
        $Diff -is [System.Array] | Should -BeTrue
        $Diff.Count | Should -Be 0
        $Problems = Test-VmSnapshotMatches -Snapshot $A -Expected @{ Registry = @(@{ Path = 'K'; Name = 'V'; Data = '1' }) }
        $Problems -is [System.Array] | Should -BeTrue
        $Problems.Count | Should -Be 0
        $B = [pscustomobject]@{ Registry = @([pscustomobject]@{ Key = 'K\V'; Exists = $true; Kind = 'DWord'; Data = '2' }); Services = @() }
        $One = Compare-VmSnapshot -Before $A -After $B
        $One -is [System.Array] | Should -BeTrue
        $One.Count | Should -Be 1
    }

    Context 'zdalny blok Invoke-DsoCli (lokalnie)' {
        BeforeEach {
            $script:Dir = Join-Path $env:TEMP ("dso-run-" + [guid]::NewGuid().ToString('N'))
            New-Item -ItemType Directory -Path $script:Dir | Out-Null
            Mock -ModuleName VmRun Invoke-Command -RemoveParameterValidation 'Session' -MockWith { & $ScriptBlock @ArgumentList }
        }
        AfterEach { Remove-Item -LiteralPath $script:Dir -Recurse -Force -ErrorAction SilentlyContinue }
        It 'kasuje stary raport, zwraca ExitCode 740 i Report = null' {
            $Out = Join-Path $script:Dir 'o.json'
            Set-Content -LiteralPath $Out -Value '{"state":"Applied"}' -Encoding UTF8
            $Result = Invoke-DsoCli -Session $null -ExePath "$env:SystemRoot\System32\cmd.exe" -Arguments '/c', 'exit', '740' -OutPath $Out
            $Result.ExitCode | Should -Be 740
            $Result.TimedOut | Should -BeFalse
            $Result.Report | Should -BeNullOrEmpty
            Test-Path -LiteralPath $Out | Should -BeFalse
        }
        It 'przekroczenie czasu: ExitCode -1, TimedOut, proces zabity' {
            $Out = Join-Path $script:Dir 'o2.json'
            $Result = Invoke-DsoCli -Session $null -ExePath "$env:SystemRoot\System32\WindowsPowerShell\v1.0\powershell.exe" -Arguments '-NoProfile', '-Command', 'Start-Sleep', '37;#' -OutPath $Out -TimeoutSeconds 1
            $Result.ExitCode | Should -Be -1
            $Result.TimedOut | Should -BeTrue
            Start-Sleep -Milliseconds 500
            @(Get-CimInstance -ClassName Win32_Process -Filter "Name = 'powershell.exe'" | Where-Object { $_.CommandLine -like '*Start-Sleep 37;#*' }).Count | Should -Be 0
        }
        It 'uszkodzony JSON: Report null, ReportError wypełniony, Json zachowany' {
            $Out = Join-Path $script:Dir 'o3.json'
            Mock -ModuleName VmRun Invoke-Command -RemoveParameterValidation 'Session' -MockWith { @{ ExitCode = 0; TimedOut = $false; Json = '{zepsuty' } }
            $Result = Invoke-DsoCli -Session $null -ExePath 'x' -Arguments @() -OutPath $Out
            $Result.Report | Should -BeNullOrEmpty
            $Result.ReportError | Should -Not -BeNullOrEmpty
            $Result.Json | Should -Be '{zepsuty'
        }
    }

    Context 'walidacja struktury snapshotów w Import-VmCases' {
        BeforeAll {
            $script:Base = "Name = 'n'; TweakId = 'vmtest.hklm-value'; Flow = 'ApplyOnly'; RestorePoint = 'skip'"
            $script:MinExpect = "Before = 'NotApplied'; ApplyExit = 0; ApplyOutcome = 'Applied'; AfterApply = 'Applied'"
        }
        It 'literówka Service w Snapshot' {
            Write-CasesFile -Path "$TestDrive\s1.psd1" -CaseBody "$script:Base; Snapshot = @{ Service = @('X') }; Expect = @{ $script:MinExpect }"
            { Import-VmCases -CasesPath "$TestDrive\s1.psd1" -CatalogPath $script:CatalogPath } | Should -Throw -ExpectedMessage "*'n'*Service*"
        }
        It 'wpis rejestru bez Name w Snapshot' {
            Write-CasesFile -Path "$TestDrive\s2.psd1" -CaseBody "$script:Base; Snapshot = @{ Registry = @(@{ Path = 'HKLM\S' }) }; Expect = @{ $script:MinExpect }"
            { Import-VmCases -CasesPath "$TestDrive\s2.psd1" -CatalogPath $script:CatalogPath } | Should -Throw -ExpectedMessage "*Snapshot.Registry*Name*"
        }
        It 'literówka w Expect.AfterApplySnapshot' {
            Write-CasesFile -Path "$TestDrive\s3.psd1" -CaseBody "$script:Base; Expect = @{ $script:MinExpect; AfterApplySnapshot = @{ Servicess = @(@{ Name = 'X' }) } }"
            { Import-VmCases -CasesPath "$TestDrive\s3.psd1" -CatalogPath $script:CatalogPath } | Should -Throw -ExpectedMessage "*Servicess*AfterApplySnapshot*"
        }
        It 'Expect.NotWrittenTo musi być listą wpisów Path i Name' {
            Write-CasesFile -Path "$TestDrive\s4.psd1" -CaseBody "$script:Base; Expect = @{ $script:MinExpect; NotWrittenTo = @(@{ Path = 'HKCU\S' }) }"
            { Import-VmCases -CasesPath "$TestDrive\s4.psd1" -CatalogPath $script:CatalogPath } | Should -Throw -ExpectedMessage "*NotWrittenTo*Name*"
        }
    }

    Context 'Get-DsoStateSnapshot: błąd zapytania o usługę' {
        It 'brak usługi bez błędu = Exists false' {
            Mock -ModuleName VmRun Get-CimInstance { $null }
            $Snap = Get-DsoStateSnapshot -Registry @() -Services @('DsoNoSuchService')
            $Snap.Services[0].Exists | Should -BeFalse
        }
        It 'błąd zapytania Win32_Service jest rzucany, nie udaje braku usługi' {
            # Mock nie dostaje ErrorAction, więc błąd symulujemy tylko dla wywołania z -ErrorAction Stop (SilentlyContinue = brak błędu = fałszywe 'brak usługi').
            Mock -ModuleName VmRun Get-CimInstance { $null }
            Mock -ModuleName VmRun Get-CimInstance { throw 'RPC niedostępny' } -ParameterFilter { $ClassName -eq 'Win32_Service' -and $ErrorAction -eq 'Stop' }
            { Get-DsoStateSnapshot -Registry @() -Services @('EventLog') } | Should -Throw -ExpectedMessage '*RPC niedostępny*'
        }
        It 'błąd zapytania Win32_SystemDriver jest rzucany' {
            Mock -ModuleName VmRun Get-CimInstance { $null }
            Mock -ModuleName VmRun Get-CimInstance { throw 'sterowniki niedostępne' } -ParameterFilter { $ClassName -eq 'Win32_SystemDriver' -and $ErrorAction -eq 'Stop' }
            { Get-DsoStateSnapshot -Registry @() -Services @('Brak') } | Should -Throw -ExpectedMessage '*sterowniki niedostępne*'
        }
    }

    Context 'Get-DsoStateSnapshot: błędy dostępu' {
        It 'brak klucza = Exists false, inny błąd Get-Item jest rzucany' {
            $Snap = Get-DsoStateSnapshot -Registry @(@{ Path = 'HKCU\Software\DsoNoSuchKey-zzz'; Name = 'V' }) -Services @() -AllowCurrentUser $true
            $Snap.Registry[0].Exists | Should -BeFalse
            Mock -ModuleName VmRun Get-Item { throw [System.UnauthorizedAccessException]::new('odmowa') }
            { Get-DsoStateSnapshot -Registry @(@{ Path = 'HKCU\Software\X'; Name = 'V' }) -Services @() -AllowCurrentUser $true } | Should -Throw -ExpectedMessage '*odmowa*'
        }
    }

    It 'Invoke-VmCase zapisuje Json i ReportError w krokach' {
        Mock -ModuleName VmRun Invoke-VmSetup {}
        Mock -ModuleName VmRun Get-VmStateSnapshot { [pscustomobject]@{ Registry = @(); Services = @() } }
        Mock -ModuleName VmRun Invoke-DsoCli { [pscustomobject]@{ ExitCode = 0; TimedOut = $false; Report = $null; Json = '{zepsuty'; ReportError = 'zly json' } }
        $Case = @{ Name = 'c'; TweakId = 'a.b'; Flow = 'ApplyOnly'; RestorePoint = 'skip'; Setup = @(); Snapshot = @{ Registry = @(); Services = @() }; Expect = @{} }
        $Record = Invoke-VmCase -Session $null -Case $Case -Layout (Get-DsoVmLayout)
        $Record.Steps[0].Json | Should -Be '{zepsuty'
        $Record.Steps[0].ReportError | Should -Be 'zly json'
    }
}

Describe 'Task 6: Catalog, Skip i DetectOnly w Import-VmCases' {
    BeforeAll {
        $script:DetectBody = "Name = 'd'; TweakId = 'vmtest.hklm-value'; Flow = 'DetectOnly'; RestorePoint = 'skip'"
    }
    It 'przyjmuje DetectOnly z samym Expect.Before' {
        Write-CasesFile -Path "$TestDrive\t6a.psd1" -CaseBody "$script:DetectBody; Expect = @{ Before = 'NotApplied' }"
        $Cases = Import-VmCases -CasesPath "$TestDrive\t6a.psd1" -CatalogPath $script:CatalogPath
        $Cases[0].Flow | Should -Be 'DetectOnly'
    }
    It 'DetectOnly bez Before jest odrzucony' {
        Write-CasesFile -Path "$TestDrive\t6b.psd1" -CaseBody "$script:DetectBody; Expect = @{ }"
        { Import-VmCases -CasesPath "$TestDrive\t6b.psd1" -CatalogPath $script:CatalogPath } | Should -Throw -ExpectedMessage '*Before*'
    }
    It 'DetectOnly z ApplyExit jest odrzucony' {
        Write-CasesFile -Path "$TestDrive\t6c.psd1" -CaseBody "$script:DetectBody; Expect = @{ Before = 'NotApplied'; ApplyExit = 0 }"
        { Import-VmCases -CasesPath "$TestDrive\t6c.psd1" -CatalogPath $script:CatalogPath } | Should -Throw -ExpectedMessage '*ApplyExit*'
    }
    It 'DetectOnly z UndoOutcome jest odrzucony' {
        Write-CasesFile -Path "$TestDrive\t6c2.psd1" -CaseBody "$script:DetectBody; Expect = @{ Before = 'NotApplied'; UndoOutcome = 'Undone' }"
        { Import-VmCases -CasesPath "$TestDrive\t6c2.psd1" -CatalogPath $script:CatalogPath } | Should -Throw -ExpectedMessage '*UndoOutcome*'
    }
    It 'Catalog = BuiltIn nie wymaga TweakId w katalogu testowym' {
        Write-CasesFile -Path "$TestDrive\t6d.psd1" -CaseBody "Name = 'b'; TweakId = 'wbudowany.nieistniejacy-w-testowym'; Flow = 'DetectOnly'; RestorePoint = 'skip'; Catalog = 'BuiltIn'; Expect = @{ Before = 'NotApplied' }"
        $Cases = Import-VmCases -CasesPath "$TestDrive\t6d.psd1" -CatalogPath $script:CatalogPath
        $Cases[0].Catalog | Should -Be 'BuiltIn'
    }
    It 'Catalog = Test nadal sprawdza TweakId w katalogu testowym' {
        Write-CasesFile -Path "$TestDrive\t6d2.psd1" -CaseBody "Name = 'b'; TweakId = 'vmtest.brak'; Flow = 'DetectOnly'; RestorePoint = 'skip'; Catalog = 'Test'; Expect = @{ Before = 'NotApplied' }"
        { Import-VmCases -CasesPath "$TestDrive\t6d2.psd1" -CatalogPath $script:CatalogPath } | Should -Throw -ExpectedMessage '*vmtest.brak*'
    }
    It 'odrzuca Catalog = Inny' {
        Write-CasesFile -Path "$TestDrive\t6e.psd1" -CaseBody "$script:DetectBody; Catalog = 'Inny'; Expect = @{ Before = 'NotApplied' }"
        { Import-VmCases -CasesPath "$TestDrive\t6e.psd1" -CatalogPath $script:CatalogPath } | Should -Throw -ExpectedMessage '*Catalog*Inny*'
    }
    It 'przyjmuje Skip z powodem' {
        Write-CasesFile -Path "$TestDrive\t6f.psd1" -CaseBody "$script:DetectBody; Skip = 'Nie da się sprawdzić w VM.'; Expect = @{ Before = 'NotApplied' }"
        $Cases = Import-VmCases -CasesPath "$TestDrive\t6f.psd1" -CatalogPath $script:CatalogPath
        $Cases[0].Skip | Should -Be 'Nie da się sprawdzić w VM.'
    }
    It 'odrzuca pusty Skip' {
        Write-CasesFile -Path "$TestDrive\t6g.psd1" -CaseBody "$script:DetectBody; Skip = ' '; Expect = @{ Before = 'NotApplied' }"
        { Import-VmCases -CasesPath "$TestDrive\t6g.psd1" -CatalogPath $script:CatalogPath } | Should -Throw -ExpectedMessage '*Skip*'
    }
    It 'odrzuca Skip niebędący stringiem' {
        Write-CasesFile -Path "$TestDrive\t6h.psd1" -CaseBody "$script:DetectBody; Skip = `$true; Expect = @{ Before = 'NotApplied' }"
        { Import-VmCases -CasesPath "$TestDrive\t6h.psd1" -CatalogPath $script:CatalogPath } | Should -Throw -ExpectedMessage '*Skip*'
    }
}

Describe 'Task 6: ConvertTo-DsoCliArguments bez katalogu' {
    It 'pusty lub $null CatalogPath pomija --catalog' {
        $A = ConvertTo-DsoCliArguments -Command detect -TweakId 'a.b' -CatalogPath $null -OutPath 'C:\o.json'
        $A | Should -Be @('--detect', 'a.b', '--out', 'C:\o.json')
        $B = ConvertTo-DsoCliArguments -Command detect -TweakId 'a.b' -CatalogPath '' -OutPath 'C:\o.json'
        $B -contains '--catalog' | Should -BeFalse
    }
    It 'z katalogiem nadal go zawiera' {
        (ConvertTo-DsoCliArguments -Command detect -TweakId 'a.b' -CatalogPath 'C:\k.json' -OutPath 'C:\o.json') -contains '--catalog' | Should -BeTrue
    }
}

Describe 'Task 6: Invoke-VmCase DetectOnly i katalog wbudowany' {
    BeforeEach {
        Mock -ModuleName VmRun Invoke-VmSetup {}
        Mock -ModuleName VmRun Get-VmStateSnapshot { [pscustomobject]@{ Registry = @(); Services = @() } }
        Mock -ModuleName VmRun Invoke-DsoCli { [pscustomobject]@{ ExitCode = 0; TimedOut = $false; Report = [pscustomobject]@{ state = 'NotApplied' }; Json = '{}'; ReportError = $null } }
        $script:Layout = Get-DsoVmLayout
    }
    It 'DetectOnly woła CLI raz (detect) i zwraca SnapshotBefore' {
        $Case = @{ Name = 'c'; TweakId = 'a.b'; Flow = 'DetectOnly'; RestorePoint = 'skip'; Setup = @('x'); Snapshot = @{ Registry = @(); Services = @() }; Expect = @{ Before = 'NotApplied' } }
        $Record = Invoke-VmCase -Session $null -Case $Case -Layout $script:Layout
        $Record.Steps.Step | Should -Be @('detect-before')
        Should -Invoke -ModuleName VmRun Invoke-DsoCli -Times 1 -Exactly -ParameterFilter { $Arguments[0] -eq '--detect' }
        Should -Invoke -ModuleName VmRun Invoke-VmSetup -Times 1 -Exactly
        $Record.SnapshotBefore | Should -Not -BeNullOrEmpty
    }
    It 'Catalog = BuiltIn: argumenty CLI bez --catalog' {
        $Case = @{ Name = 'c'; TweakId = 'a.b'; Flow = 'DetectOnly'; RestorePoint = 'skip'; Catalog = 'BuiltIn'; Snapshot = @{ Registry = @(); Services = @() }; Expect = @{ Before = 'NotApplied' } }
        Invoke-VmCase -Session $null -Case $Case -Layout $script:Layout | Out-Null
        Should -Invoke -ModuleName VmRun Invoke-DsoCli -Times 1 -Exactly -ParameterFilter { $Arguments -notcontains '--catalog' }
    }
    It 'Catalog domyślny (Test): argumenty zawierają --catalog z katalogiem VM' {
        $Case = @{ Name = 'c'; TweakId = 'a.b'; Flow = 'DetectOnly'; RestorePoint = 'skip'; Snapshot = @{ Registry = @(); Services = @() }; Expect = @{ Before = 'NotApplied' } }
        Invoke-VmCase -Session $null -Case $Case -Layout $script:Layout | Out-Null
        Should -Invoke -ModuleName VmRun Invoke-DsoCli -Times 1 -Exactly -ParameterFilter { $Arguments -contains '--catalog' -and @($Arguments | Where-Object { $_ -like '*vm-test-catalog.json' }).Count -eq 1 }
    }
}

Describe 'Task 7: Execute w Import-VmCases i ConvertTo-DsoCliArguments' {
    BeforeAll {
        $script:ExecBody = "Name = 'e'; TweakId = 'vmtest.action-marker'; Flow = 'Execute'; RestorePoint = 'skip'"
        $script:ExecExpect = "ExecuteExit = 0; ExecuteOutcome = 'Executed'"
    }
    It 'przyjmuje Execute z Check i Cleanup' {
        Write-CasesFile -Path "$TestDrive\t7a.psd1" -CaseBody "$script:ExecBody; Check = '`$true'; Cleanup = 'Write-Output 1'; Expect = @{ $script:ExecExpect }"
        $Cases = Import-VmCases -CasesPath "$TestDrive\t7a.psd1" -CatalogPath $script:CatalogPath
        $Cases[0].Flow | Should -Be 'Execute'
    }
    It 'Execute bez ExecuteOutcome jest odrzucony' {
        Write-CasesFile -Path "$TestDrive\t7b.psd1" -CaseBody "$script:ExecBody; Expect = @{ ExecuteExit = 0 }"
        { Import-VmCases -CasesPath "$TestDrive\t7b.psd1" -CatalogPath $script:CatalogPath } | Should -Throw -ExpectedMessage '*ExecuteOutcome*'
    }
    It 'Execute z kluczem apply jest odrzucony' {
        Write-CasesFile -Path "$TestDrive\t7c.psd1" -CaseBody "$script:ExecBody; Expect = @{ $script:ExecExpect; ApplyExit = 0 }"
        { Import-VmCases -CasesPath "$TestDrive\t7c.psd1" -CatalogPath $script:CatalogPath } | Should -Throw -ExpectedMessage '*ApplyExit*'
    }
    It 'Execute z Before jest odrzucony (przebieg nie robi detect)' {
        Write-CasesFile -Path "$TestDrive\t7c2.psd1" -CaseBody "$script:ExecBody; Expect = @{ $script:ExecExpect; Before = 'NotApplied' }"
        { Import-VmCases -CasesPath "$TestDrive\t7c2.psd1" -CatalogPath $script:CatalogPath } | Should -Throw -ExpectedMessage '*Before*'
    }
    It 'Check i Cleanup są dozwolone tylko w Flow Execute: <Key>' -ForEach @(@{ Key = 'Check' }, @{ Key = 'Cleanup' }) {
        Write-CasesFile -Path "$TestDrive\t7d.psd1" -CaseBody "Name = 'x'; TweakId = 'vmtest.hklm-value'; Flow = 'DetectOnly'; RestorePoint = 'skip'; $Key = 'Write-Output 1'; Expect = @{ Before = 'NotApplied' }"
        { Import-VmCases -CasesPath "$TestDrive\t7d.psd1" -CatalogPath $script:CatalogPath } | Should -Throw -ExpectedMessage "*$Key*Execute*"
    }
    It 'pusty Check jest odrzucony' {
        Write-CasesFile -Path "$TestDrive\t7e.psd1" -CaseBody "$script:ExecBody; Check = ' '; Expect = @{ $script:ExecExpect }"
        { Import-VmCases -CasesPath "$TestDrive\t7e.psd1" -CatalogPath $script:CatalogPath } | Should -Throw -ExpectedMessage '*Check*'
    }
    It 'Execute przyjmuje MessageContains razem z oczekiwanym Failed (akcja kończy się błędem z komunikatem)' {
        Write-CasesFile -Path "$TestDrive\t7m.psd1" -CaseBody "$script:ExecBody; Check = '`$true'; Expect = @{ ExecuteExit = 1; ExecuteOutcome = 'Failed'; MessageContains = 'brak sieci' }"
        $Cases = Import-VmCases -CasesPath "$TestDrive\t7m.psd1" -CatalogPath $script:CatalogPath
        $Cases[0].Expect.MessageContains | Should -Be 'brak sieci'
        $Cases[0].Expect.ExecuteOutcome | Should -Be 'Failed'
    }
    It 'MessageContains w DetectOnly nadal jest odrzucony (brak komunikatu do sprawdzenia)' {
        Write-CasesFile -Path "$TestDrive\t7n.psd1" -CaseBody "Name = 'x'; TweakId = 'vmtest.hklm-value'; Flow = 'DetectOnly'; RestorePoint = 'skip'; Expect = @{ Before = 'NotApplied'; MessageContains = 'x' }"
        { Import-VmCases -CasesPath "$TestDrive\t7n.psd1" -CatalogPath $script:CatalogPath } | Should -Throw -ExpectedMessage '*MessageContains*'
    }
    It 'Execute bez Check jest odrzucony także z MessageContains (komunikat błędu nie zastępuje sprawdzenia stanu)' {
        Write-CasesFile -Path "$TestDrive\t7o.psd1" -CaseBody "$script:ExecBody; Expect = @{ ExecuteExit = 1; ExecuteOutcome = 'Failed'; MessageContains = 'x' }"
        { Import-VmCases -CasesPath "$TestDrive\t7o.psd1" -CatalogPath $script:CatalogPath } | Should -Throw -ExpectedMessage '*Check*'
    }
    It 'Execute przyjmuje Expect.RequiresReboot = $true (akcja z requiresReboot w katalogu)' {
        Write-CasesFile -Path "$TestDrive\t7r1.psd1" -CaseBody "$script:ExecBody; Check = '`$true'; Expect = @{ $script:ExecExpect; RequiresReboot = `$true }"
        $Cases = Import-VmCases -CasesPath "$TestDrive\t7r1.psd1" -CatalogPath $script:CatalogPath
        $Cases[0].Expect.RequiresReboot | Should -BeTrue
    }
    It 'Expect.RequiresReboot musi być wartością logiczną' {
        Write-CasesFile -Path "$TestDrive\t7r2.psd1" -CaseBody "$script:ExecBody; Check = '`$true'; Expect = @{ $script:ExecExpect; RequiresReboot = 'tak' }"
        { Import-VmCases -CasesPath "$TestDrive\t7r2.psd1" -CatalogPath $script:CatalogPath } | Should -Throw -ExpectedMessage '*RequiresReboot*logiczn*'
    }
    It 'Expect.RequiresReboot poza Flow Execute jest odrzucony: <Flow>' -ForEach @(@{ Flow = 'Full' }, @{ Flow = 'DetectOnly' }) {
        $Expect = if ($Flow -eq 'Full') { $script:ValidExpectFull } else { "Before = 'NotApplied'" }
        Write-CasesFile -Path "$TestDrive\t7r3.psd1" -CaseBody "Name = 'n'; TweakId = 'vmtest.hklm-value'; Flow = '$Flow'; RestorePoint = 'skip'; Expect = @{ $Expect; RequiresReboot = `$true }"
        { Import-VmCases -CasesPath "$TestDrive\t7r3.psd1" -CatalogPath $script:CatalogPath } | Should -Throw -ExpectedMessage "*RequiresReboot*Execute*"
    }
    It 'ExecuteExit w Flow Full jest odrzucony' {
        Write-CasesFile -Path "$TestDrive\t7f.psd1" -CaseBody "Name = 'n'; TweakId = 'vmtest.hklm-value'; Flow = 'Full'; RestorePoint = 'skip'; Expect = @{ $script:ValidExpectFull; ExecuteExit = 0 }"
        { Import-VmCases -CasesPath "$TestDrive\t7f.psd1" -CatalogPath $script:CatalogPath } | Should -Throw -ExpectedMessage '*ExecuteExit*'
    }
    It 'prawdziwy przypadek action-marker jest wczytany jako Execute' {
        $Cases = Import-VmCases -CasesPath "$script:VmRoot\cases.psd1" -CatalogPath $script:CatalogPath
        $Marker = @($Cases | Where-Object { $_.Name -eq 'action-marker' })
        $Marker.Count | Should -Be 1
        $Marker[0].Flow | Should -Be 'Execute'
        $Marker[0].Expect.ExecuteOutcome | Should -Be 'Executed'
    }
    It 'prawdziwe przypadki akcji net.*: smb-harden z Check, Cleanup i Snapshot; network-private: Failed z komunikatem oraz Skip dla realnej zmiany' {
        $Cases = Import-VmCases -CasesPath "$script:VmRoot\cases.psd1" -CatalogPath $script:CatalogPath
        $Harden = @($Cases | Where-Object { $_.Name -eq 'net-smb-harden' })
        $Harden.Count | Should -Be 1
        $Harden[0].Flow | Should -Be 'Execute'
        $Harden[0].Catalog | Should -Be 'BuiltIn'
        $Harden[0].Expect.ExecuteOutcome | Should -Be 'Executed'
        $Harden[0].Check | Should -Match 'Get-SmbClientConfiguration'
        $Harden[0].Cleanup | Should -Match 'Set-SmbClientConfiguration'
        @($Harden[0].Snapshot.Registry).Count | Should -Be 2

        $NoNetwork = @($Cases | Where-Object { $_.Name -eq 'net-network-private-no-network' })
        $NoNetwork.Count | Should -Be 1
        $NoNetwork[0].Expect.ExecuteOutcome | Should -Be 'Failed'
        $NoNetwork[0].Expect.ExecuteExit | Should -Be 1
        $NoNetwork[0].Expect.MessageContains | Should -Not -BeNullOrEmpty
        $NoNetwork[0].Check | Should -Match 'Get-NetConnectionProfile'
        $NoNetwork[0].ContainsKey('Skip') | Should -BeFalse

        $Change = @($Cases | Where-Object { $_.Name -eq 'net-network-private-change' })
        $Change.Count | Should -Be 1
        $Change[0].Skip | Should -Be 'brak sieci na VM'
    }
    It 'ConvertTo-DsoCliArguments execute: --execute, bez --restore-point' {
        $A = ConvertTo-DsoCliArguments -Command execute -TweakId 'a.b' -CatalogPath 'C:\k.json' -OutPath 'C:\o.json' -RestorePoint skip
        $A | Should -Be @('--execute', 'a.b', '--catalog', 'C:\k.json', '--out', 'C:\o.json')
    }
}

Describe 'Task 7: Invoke-VmCase Execute' {
    BeforeEach {
        Mock -ModuleName VmRun Invoke-VmSetup {}
        Mock -ModuleName VmRun Get-VmStateSnapshot { [pscustomobject]@{ Registry = @(); Services = @() } }
        Mock -ModuleName VmRun Invoke-DsoCli { [pscustomobject]@{ ExitCode = 0; TimedOut = $false; Report = [pscustomobject]@{ outcome = 'Executed'; exitCode = 0 }; Json = '{}'; ReportError = $null } }
        $global:DsoScriptCalls = [System.Collections.Generic.List[string]]::new()
        $global:DsoCheckBehavior = 'true'
        $global:DsoCleanupThrows = $false
        Mock -ModuleName VmRun Invoke-Command -RemoveParameterValidation 'Session' -MockWith {
            $Text = $ScriptBlock.ToString()
            $global:DsoScriptCalls.Add($Text)
            if ($Text -match 'MARKER-CHECK') {
                switch ($global:DsoCheckBehavior) {
                    'true' { return $true }
                    'false' { return $false }
                    'text' { return 'plik ma inną treść' }
                    'throw' { throw 'nie można odczytać pliku' }
                    'none' { return }
                }
            }
            if ($Text -match 'MARKER-CLEANUP' -and $global:DsoCleanupThrows) { throw 'sprzątanie nie powiodło się' }
        }
        $script:Layout = Get-DsoVmLayout
        $script:Case = @{ Name = 'a'; TweakId = 'vmtest.action-marker'; Flow = 'Execute'; RestorePoint = 'skip'; Setup = @(); Check = '# MARKER-CHECK'; Cleanup = '# MARKER-CLEANUP'
            Expect = @{ ExecuteExit = 0; ExecuteOutcome = 'Executed' } }
    }
    AfterEach {
        Remove-Variable -Name DsoScriptCalls, DsoCheckBehavior, DsoCleanupThrows -Scope Global -ErrorAction SilentlyContinue
    }
    It 'woła CLI jeden raz z --execute, uruchamia Check i Cleanup (w tej kolejności)' {
        $Record = Invoke-VmCase -Session $null -Case $script:Case -Layout $script:Layout
        $Record.Steps.Step | Should -Be @('execute')
        Should -Invoke -ModuleName VmRun Invoke-DsoCli -Times 1 -Exactly -ParameterFilter { $Arguments[0] -eq '--execute' -and $Arguments[1] -eq 'vmtest.action-marker' }
        $Record.CheckResult | Should -Be 'Passed'
        $Record.CleanupError | Should -BeNullOrEmpty
        $global:DsoScriptCalls.Count | Should -Be 2
        $global:DsoScriptCalls[0] | Should -Match 'MARKER-CHECK'
        $global:DsoScriptCalls[1] | Should -Match 'MARKER-CLEANUP'
    }
    It 'Check zwracający <Behavior> daje opis porażki, a nie Passed' -ForEach @(
        @{ Behavior = 'false'; Fragment = 'False' }
        @{ Behavior = 'text'; Fragment = 'plik ma inną treść' }
        @{ Behavior = 'none'; Fragment = 'nie zwrócił' }
    ) {
        $global:DsoCheckBehavior = $Behavior
        $Record = Invoke-VmCase -Session $null -Case $script:Case -Layout $script:Layout
        $Record.CheckResult | Should -Not -Be 'Passed'
        $Record.CheckResult | Should -BeLike "*$Fragment*"
    }
    It 'Cleanup działa także, gdy Check rzuca, a rzucony błąd trafia do CheckResult' {
        $global:DsoCheckBehavior = 'throw'
        $Record = Invoke-VmCase -Session $null -Case $script:Case -Layout $script:Layout
        $Record.CheckResult | Should -BeLike '*nie można odczytać pliku*'
        @($global:DsoScriptCalls | Where-Object { $_ -match 'MARKER-CLEANUP' }).Count | Should -Be 1
    }
    It 'Cleanup działa także, gdy krok execute rzuca, a wyjątek nie jest połykany' {
        Mock -ModuleName VmRun Invoke-DsoCli { throw 'sesja zerwana' }
        { Invoke-VmCase -Session $null -Case $script:Case -Layout $script:Layout } | Should -Throw -ExpectedMessage '*sesja zerwana*'
        @($global:DsoScriptCalls | Where-Object { $_ -match 'MARKER-CLEANUP' }).Count | Should -Be 1
        @($global:DsoScriptCalls | Where-Object { $_ -match 'MARKER-CHECK' }).Count | Should -Be 0
    }
    It 'błąd Cleanup trafia do CleanupError i nie ukrywa wyniku Check' {
        $global:DsoCleanupThrows = $true
        $Record = Invoke-VmCase -Session $null -Case $script:Case -Layout $script:Layout
        $Record.CheckResult | Should -Be 'Passed'
        $Record.CleanupError | Should -BeLike '*sprzątanie nie powiodło się*'
    }
    It 'bez Check i Cleanup: CheckResult pusty, Invoke-Command nie jest wołany' {
        $Plain = @{ Name = 'a'; TweakId = 'x.y'; Flow = 'Execute'; RestorePoint = 'skip'; Expect = @{ ExecuteExit = 0; ExecuteOutcome = 'Executed' } }
        $Record = Invoke-VmCase -Session $null -Case $Plain -Layout $script:Layout
        $Record.CheckResult | Should -BeNullOrEmpty
        $global:DsoScriptCalls.Count | Should -Be 0
    }
    It 'snapshot przed i po (po sprzątaniu) tylko gdy podano Snapshot' {
        $WithSnap = @{} + $script:Case
        $WithSnap.Snapshot = @{ Registry = @(); Services = @() }
        $Record = Invoke-VmCase -Session $null -Case $WithSnap -Layout $script:Layout
        $Record.SnapshotBefore | Should -Not -BeNullOrEmpty
        $Record.SnapshotAfter | Should -Not -BeNullOrEmpty
        @($Record.SnapshotDiff).Count | Should -Be 0
    }
}

Describe 'Task 8: Reboot w Import-VmCases' {
    BeforeAll {
        $script:RebootBody = "Name = 'r'; TweakId = 'vmtest.hklm-value'; Flow = 'Reboot'; RestorePoint = 'skip'"
        $script:RebootExpect = "Before = 'NotApplied'; ApplyExit = 0; ApplyOutcome = 'Applied'; AfterApply = 'Applied'; AfterReboot = 'Applied'; UndoExit = 0; UndoOutcome = 'Undone'; AfterUndo = 'NotApplied'"
    }
    It 'przyjmuje pełny przypadek Reboot' {
        Write-CasesFile -Path "$TestDrive\t8a.psd1" -CaseBody "$script:RebootBody; Expect = @{ $script:RebootExpect; KeyAbsentAfterUndo = @('HKLM\SOFTWARE\X') }"
        (Import-VmCases -CasesPath "$TestDrive\t8a.psd1" -CatalogPath $script:CatalogPath)[0].Flow | Should -Be 'Reboot'
    }
    It 'Reboot bez AfterReboot jest odrzucony' {
        $Expect = $script:RebootExpect.Replace("AfterReboot = 'Applied'; ", '')
        Write-CasesFile -Path "$TestDrive\t8b.psd1" -CaseBody "$script:RebootBody; Expect = @{ $Expect }"
        { Import-VmCases -CasesPath "$TestDrive\t8b.psd1" -CatalogPath $script:CatalogPath } | Should -Throw -ExpectedMessage '*AfterReboot*'
    }
    It 'Reboot bez AfterUndo jest odrzucony' {
        $Expect = $script:RebootExpect.Replace("; AfterUndo = 'NotApplied'", '')
        Write-CasesFile -Path "$TestDrive\t8c.psd1" -CaseBody "$script:RebootBody; Expect = @{ $Expect }"
        { Import-VmCases -CasesPath "$TestDrive\t8c.psd1" -CatalogPath $script:CatalogPath } | Should -Throw -ExpectedMessage '*AfterUndo*'
    }
    It 'Reboot ze Skip jest przyjęty, ale nadal walidowany' {
        Write-CasesFile -Path "$TestDrive\t8d.psd1" -CaseBody "$script:RebootBody; Skip = 'Powód.'; Expect = @{ $script:RebootExpect }"
        (Import-VmCases -CasesPath "$TestDrive\t8d.psd1" -CatalogPath $script:CatalogPath)[0].Skip | Should -Be 'Powód.'
        Write-CasesFile -Path "$TestDrive\t8e.psd1" -CaseBody "$script:RebootBody; Skip = 'Powód.'; Expect = @{ Before = 'NotApplied' }"
        { Import-VmCases -CasesPath "$TestDrive\t8e.psd1" -CatalogPath $script:CatalogPath } | Should -Throw -ExpectedMessage '*ApplyExit*'
    }
    It 'AfterReboot we Flow Full jest odrzucony' {
        Write-CasesFile -Path "$TestDrive\t8f.psd1" -CaseBody "Name = 'n'; TweakId = 'vmtest.hklm-value'; Flow = 'Full'; RestorePoint = 'skip'; Expect = @{ $script:ValidExpectFull; AfterReboot = 'Applied' }"
        { Import-VmCases -CasesPath "$TestDrive\t8f.psd1" -CatalogPath $script:CatalogPath } | Should -Throw -ExpectedMessage '*AfterReboot*'
    }
    It 'KeyAbsentAfterUndo w ApplyOnly i DetectOnly jest odrzucone, w Full dozwolone' {
        Write-CasesFile -Path "$TestDrive\t8g.psd1" -CaseBody "Name = 'n'; TweakId = 'vmtest.hklm-value'; Flow = 'DetectOnly'; RestorePoint = 'skip'; Expect = @{ Before = 'NotApplied'; KeyAbsentAfterUndo = @('HKLM\X') }"
        { Import-VmCases -CasesPath "$TestDrive\t8g.psd1" -CatalogPath $script:CatalogPath } | Should -Throw -ExpectedMessage '*KeyAbsentAfterUndo*'
        Write-CasesFile -Path "$TestDrive\t8h.psd1" -CaseBody "Name = 'n'; TweakId = 'vmtest.hklm-value'; Flow = 'ApplyOnly'; RestorePoint = 'skip'; Expect = @{ Before = 'NotApplied'; ApplyExit = 0; ApplyOutcome = 'Applied'; AfterApply = 'Applied'; KeyAbsentAfterUndo = @('HKLM\X') }"
        { Import-VmCases -CasesPath "$TestDrive\t8h.psd1" -CatalogPath $script:CatalogPath } | Should -Throw -ExpectedMessage '*KeyAbsentAfterUndo*'
        Write-CasesFile -Path "$TestDrive\t8i.psd1" -CaseBody "Name = 'n'; TweakId = 'vmtest.hklm-value'; Flow = 'Full'; RestorePoint = 'skip'; Expect = @{ $script:ValidExpectFull; KeyAbsentAfterUndo = @('HKLM\X') }"
        { Import-VmCases -CasesPath "$TestDrive\t8i.psd1" -CatalogPath $script:CatalogPath } | Should -Not -Throw
    }
    It 'KeyAbsentAfterUndo musi być niepustą listą tekstów' {
        Write-CasesFile -Path "$TestDrive\t8j.psd1" -CaseBody "Name = 'n'; TweakId = 'vmtest.hklm-value'; Flow = 'Full'; RestorePoint = 'skip'; Expect = @{ $script:ValidExpectFull; KeyAbsentAfterUndo = @() }"
        { Import-VmCases -CasesPath "$TestDrive\t8j.psd1" -CatalogPath $script:CatalogPath } | Should -Throw -ExpectedMessage '*KeyAbsentAfterUndo*'
    }
}

Describe 'Task 8: Split-VmCaseRuns' {
    It 'zwykłe i pominięte Reboot w main, każdy aktywny Reboot osobno' {
        $Cases = @(
            @{ Name = 'a'; Flow = 'Full' }
            @{ Name = 'r 1'; Flow = 'Reboot' }
            @{ Name = 'b'; Flow = 'DetectOnly' }
            @{ Name = 'r2'; Flow = 'Reboot'; Skip = 'powód' }
            @{ Name = 'r3'; Flow = 'Reboot' }
        )
        $Plan = @(Split-VmCaseRuns -Cases $Cases)
        $Plan.Count | Should -Be 3
        $Plan[0].Label | Should -Be 'main'
        @($Plan[0].Cases.Name) | Should -Be @('a', 'b', 'r2')
        $Plan[1].Label | Should -Be 'reboot-r_1'
        @($Plan[1].Cases.Name) | Should -Be @('r 1')
        $Plan[2].Label | Should -Be 'reboot-r3'
    }
    It 'bez zwykłych przypadków nie tworzy pustego main' {
        $Plan = @(Split-VmCaseRuns -Cases @(@{ Name = 'r'; Flow = 'Reboot' }))
        $Plan.Count | Should -Be 1
        $Plan[0].Label | Should -Be 'reboot-r'
    }
    It 'kolizja etykiet po sanityzacji to błąd' {
        { Split-VmCaseRuns -Cases @(@{ Name = 'r 1'; Flow = 'Reboot' }, @{ Name = 'r_1'; Flow = 'Reboot' }) } | Should -Throw -ExpectedMessage '*zduplikowana*'
    }
}

Describe 'Task 8: Invoke-VmCase Flow Reboot' {
    BeforeEach {
        $global:Dso8Events = [System.Collections.Generic.List[string]]::new()
        $global:Dso8Boot = [System.Collections.Queue]::new()
        $global:Dso8Reconnects = 0
        $global:Dso8Sessions = @(1..8 | ForEach-Object { [System.Runtime.Serialization.FormatterServices]::GetUninitializedObject([System.Management.Automation.Runspaces.PSSession]) })
        Mock -ModuleName VmRun Invoke-VmSetup {}
        Mock -ModuleName VmRun Get-VmStateSnapshot { [pscustomobject]@{ Registry = @(); Services = @() } }
        Mock -ModuleName VmRun Start-Sleep {}
        Mock -ModuleName VmRun Remove-PSSession -RemoveParameterValidation 'Session' -MockWith { $global:Dso8Events.Add('remove-session') }
        Mock -ModuleName VmRun Invoke-DsoCli {
            $global:Dso8Events.Add("cli:$($Arguments[0])")
            [pscustomobject]@{ ExitCode = 0; TimedOut = $false; Report = [pscustomobject]@{ state = 'Applied'; outcome = 'Applied' }; Json = '{}'; ReportError = $null }
        }
        Mock -ModuleName VmRun Get-VmLastBootTime { $global:Dso8Events.Add('boot-read'); [datetime]$global:Dso8Boot.Dequeue() }
        Mock -ModuleName VmRun Send-VmRestart { $global:Dso8Events.Add('restart') }
        Mock -ModuleName VmRun Get-VmKeyExistence { @($Keys | ForEach-Object { [pscustomobject]@{ Key = $_; Exists = $false } }) }
        Mock -ModuleName VmRun New-VmRestartMarker { $global:Dso8Events.Add('marker-created') }
        Mock -ModuleName VmRun Test-VmRestartMarker { $false }
        $script:Layout = Get-DsoVmLayout
        $script:Reconnect = { $global:Dso8Events.Add('reconnect'); $global:Dso8Reconnects++; $global:Dso8Sessions[$global:Dso8Reconnects] }
        $script:RebootCase = @{ Name = 'rb'; TweakId = 'a.b'; Flow = 'Reboot'; RestorePoint = 'skip'; Setup = @(); Snapshot = @{ Registry = @(); Services = @() }; Expect = @{} }
        $T = [datetime]'2026-10-17T10:00:00'
        $script:T0 = $T; $script:T1 = $T.AddMinutes(5); $script:T2 = $T.AddMinutes(10)
    }
    AfterAll { Remove-Variable -Name Dso8Events, Dso8Boot, Dso8Reconnects, Dso8Sessions -Scope Global -ErrorAction SilentlyContinue }
    It 'kolejność kroków: detect, apply, detect, restart, detect, undo, restart, detect' {
        foreach ($Time in @($script:T0, $script:T1, $script:T1, $script:T2)) { $global:Dso8Boot.Enqueue($Time) }
        $Record = Invoke-VmCase -Session $global:Dso8Sessions[0] -Case $script:RebootCase -Layout $script:Layout -Reconnect $script:Reconnect
        $Record.Steps.Step | Should -Be @('detect-before', 'apply', 'detect-after-apply', 'detect-after-reboot', 'undo', 'detect-after-undo')
        $Order = @($global:Dso8Events | Where-Object { $_ -in @('cli:--detect', 'cli:--apply', 'cli:--undo', 'restart', 'reconnect') })
        $Order | Should -Be @('cli:--detect', 'cli:--apply', 'cli:--detect', 'restart', 'reconnect', 'cli:--detect', 'cli:--undo', 'restart', 'reconnect', 'cli:--detect')
        @($Record.Reboots).Count | Should -Be 2
        $Record.Reboots[0].Step | Should -Be 'reboot-after-apply'
        $Record.Reboots[1].Step | Should -Be 'reboot-after-undo'
    }
    It 'Reconnect wołany dwa razy, gdy nowa sesja od razu ma późniejszy LastBootUpTime' {
        foreach ($Time in @($script:T0, $script:T1, $script:T1, $script:T2)) { $global:Dso8Boot.Enqueue($Time) }
        Invoke-VmCase -Session $global:Dso8Sessions[0] -Case $script:RebootCase -Layout $script:Layout -Reconnect $script:Reconnect | Out-Null
        $global:Dso8Reconnects | Should -Be 2
        Should -Invoke -ModuleName VmRun Send-VmRestart -Times 2 -Exactly
    }
    It 'starą sesję zamyka przed Reconnect, a ostatnią zostawia otwartą dla wywołującego' {
        foreach ($Time in @($script:T0, $script:T1, $script:T1, $script:T2)) { $global:Dso8Boot.Enqueue($Time) }
        Invoke-VmCase -Session $global:Dso8Sessions[0] -Case $script:RebootCase -Layout $script:Layout -Reconnect $script:Reconnect | Out-Null
        $Index = $global:Dso8Events.IndexOf('restart')
        $global:Dso8Events.IndexOf('remove-session') | Should -BeGreaterThan $Index
        $global:Dso8Events.IndexOf('remove-session') | Should -BeLessThan $global:Dso8Events.IndexOf('reconnect')
        Should -Invoke -ModuleName VmRun Remove-PSSession -Times 2 -Exactly
    }
    It 'pierwsze połączenie ze starym LastBootUpTime, drugie z nowym: przypadek idzie dalej' {
        foreach ($Time in @($script:T0, $script:T0, $script:T1, $script:T1, $script:T2)) { $global:Dso8Boot.Enqueue($Time) }
        $Record = Invoke-VmCase -Session $global:Dso8Sessions[0] -Case $script:RebootCase -Layout $script:Layout -Reconnect $script:Reconnect
        $global:Dso8Reconnects | Should -Be 3
        $Record.Reboots[0].Attempts | Should -Be 2
        $Record.Reboots[1].Attempts | Should -Be 1
        $Record.Steps.Step | Should -Contain 'detect-after-undo'
    }
    It 'stale ten sam LastBootUpTime: błąd "restart nie nastąpił" po 6 wywołaniach Reconnect, bez dalszych kroków' {
        foreach ($Time in @($script:T0) * 7) { $global:Dso8Boot.Enqueue($Time) }
        { Invoke-VmCase -Session $global:Dso8Sessions[0] -Case $script:RebootCase -Layout $script:Layout -Reconnect $script:Reconnect } | Should -Throw -ExpectedMessage '*restart nie nastąpił*'
        $global:Dso8Reconnects | Should -Be 6
        $global:Dso8Events | Should -Not -Contain 'cli:--undo'
        Should -Invoke -ModuleName VmRun Invoke-DsoCli -Times 0 -ParameterFilter { $Arguments[0] -eq '--undo' }
    }
    It 'brak restartu nie przechodzi do detect-after-reboot' {
        foreach ($Time in @($script:T0) * 7) { $global:Dso8Boot.Enqueue($Time) }
        try { Invoke-VmCase -Session $global:Dso8Sessions[0] -Case $script:RebootCase -Layout $script:Layout -Reconnect $script:Reconnect } catch { Write-Verbose $_ }
        Should -Invoke -ModuleName VmRun Invoke-DsoCli -Times 3 -Exactly
    }
    It 'błąd Reconnect (np. uwierzytelnienia) przerywa bez ponowienia' {
        foreach ($Time in @($script:T0, $script:T1)) { $global:Dso8Boot.Enqueue($Time) }
        $Failing = { $global:Dso8Reconnects++; throw 'błąd uwierzytelnienia PowerShell Direct' }
        { Invoke-VmCase -Session $global:Dso8Sessions[0] -Case $script:RebootCase -Layout $script:Layout -Reconnect $Failing } | Should -Throw -ExpectedMessage '*uwierzytelnienia*'
        $global:Dso8Reconnects | Should -Be 1
    }
    It 'Reconnect zwracający $null to błąd, nie sukces' {
        $global:Dso8Boot.Enqueue($script:T0)
        { Invoke-VmCase -Session $global:Dso8Sessions[0] -Case $script:RebootCase -Layout $script:Layout -Reconnect { $null } } | Should -Throw -ExpectedMessage '*Reconnect*'
    }
    It 'brak Reconnect przy Reboot: błąd przed jakimkolwiek krokiem' {
        { Invoke-VmCase -Session $global:Dso8Sessions[0] -Case $script:RebootCase -Layout $script:Layout } | Should -Throw -ExpectedMessage '*Reconnect*'
        Should -Invoke -ModuleName VmRun Invoke-VmSetup -Times 0
        Should -Invoke -ModuleName VmRun Invoke-DsoCli -Times 0
    }
    It 'KeyAbsentAfterUndo: rekord zawiera odczyt kluczy po undo' {
        foreach ($Time in @($script:T0, $script:T1, $script:T1, $script:T2)) { $global:Dso8Boot.Enqueue($Time) }
        $Case = $script:RebootCase.Clone()
        $Case.Expect = @{ KeyAbsentAfterUndo = @('HKLM\SOFTWARE\X\Y') }
        $Record = Invoke-VmCase -Session $global:Dso8Sessions[0] -Case $Case -Layout $script:Layout -Reconnect $script:Reconnect
        @($Record.KeysAfterUndo).Count | Should -Be 1
        $Record.KeysAfterUndo[0].Key | Should -Be 'HKLM\SOFTWARE\X\Y'
    }
}

Describe 'KeyAbsentAfterUndo: dozwolone tylko HKLM\ i HKU\' {
    It 'Import odrzuca <Path> i podaje nazwę przypadku oraz ścieżkę' -ForEach @(
        @{ Path = 'HKCU\X' }
        @{ Path = 'HKCR\X' }
        @{ Path = 'C:\Windows' }
    ) {
        Write-CasesFile -Path "$TestDrive\k1.psd1" -CaseBody "Name = 'przyp-k'; TweakId = 'vmtest.hklm-value'; Flow = 'Full'; RestorePoint = 'skip'; Expect = @{ $script:ValidExpectFull; KeyAbsentAfterUndo = @('$Path') }"
        { Import-VmCases -CasesPath "$TestDrive\k1.psd1" -CatalogPath $script:CatalogPath } | Should -Throw -ExpectedMessage "*przyp-k*$Path*"
    }
    It 'Import dla HKCU podpowiada HKU\{UserSid}' {
        Write-CasesFile -Path "$TestDrive\k2.psd1" -CaseBody "Name = 'przyp-k'; TweakId = 'vmtest.hklm-value'; Flow = 'Full'; RestorePoint = 'skip'; Expect = @{ $script:ValidExpectFull; KeyAbsentAfterUndo = @('HKCU\X') }"
        { Import-VmCases -CasesPath "$TestDrive\k2.psd1" -CatalogPath $script:CatalogPath } | Should -Throw -ExpectedMessage '*HKU\{UserSid}*'
    }
    It 'Import akceptuje <Path>' -ForEach @(
        @{ Path = 'HKLM\X' }
        @{ Path = 'HKU\{UserSid}\X' }
    ) {
        Write-CasesFile -Path "$TestDrive\k3.psd1" -CaseBody "Name = 'n'; TweakId = 'vmtest.hklm-value'; Flow = 'Full'; RestorePoint = 'skip'; Expect = @{ $script:ValidExpectFull; KeyAbsentAfterUndo = @('$Path') }"
        { Import-VmCases -CasesPath "$TestDrive\k3.psd1" -CatalogPath $script:CatalogPath } | Should -Not -Throw
    }
    It 'Get-VmKeyExistence rzuca na nieznanym prefiksie (bez Test-Path na plikach)' -ForEach @(
        @{ Key = 'HKCU\X' }
        @{ Key = 'HKCR\X' }
        @{ Key = 'C:\Windows' }
    ) {
        { InModuleScope VmRun -Parameters @{ K = $Key } { param($K) Get-VmKeyExistence -Session $null -Keys @($K) } } | Should -Throw -ExpectedMessage '*HKLM*HKU*'
    }
}

Describe 'Etap 4a T7: AppRemove w Import-VmCases' {
    BeforeAll {
        $script:AppBody = "Name = 'app'; AppId = 'appx:Microsoft.BingNews_8wekyb3d8bbwe'; Flow = 'AppRemove'; RestorePoint = 'skip'; Check = '`$true'"
        $script:AppExpect = "ListedBefore = `$true; RemoveExit = 0; RemoveOutcome = 'Removed'; ListedAfter = `$false"
    }
    It 'przyjmuje AppRemove z AppId, Check, Cleanup, RunAs i pełnym Expect' {
        Write-CasesFile -Path "$TestDrive\a1.psd1" -CaseBody "$script:AppBody; Cleanup = 'Write-Output 1'; RunAs = 'dso'; Setup = @('Write-Output 1'); Expect = @{ $script:AppExpect; Group = 'Suggested'; Removability = 'Removable'; RequiresReboot = `$false; RestorePointStatus = 'ProtectionDisabled'; Aborted = `$false; MessageContains = 'x'; WarningContains = 'Usunięta'; WarningAbsent = 'y' }"
        $Cases = Import-VmCases -CasesPath "$TestDrive\a1.psd1" -CatalogPath $script:CatalogPath
        $Cases[0].Flow | Should -Be 'AppRemove'
        $Cases[0].AppId | Should -Be 'appx:Microsoft.BingNews_8wekyb3d8bbwe'
        $Cases[0].RunAs | Should -Be 'dso'
    }
    It 'AppRemove bez AppId jest odrzucony' {
        Write-CasesFile -Path "$TestDrive\a2.psd1" -CaseBody "Name = 'app'; Flow = 'AppRemove'; RestorePoint = 'skip'; Check = '`$true'; Expect = @{ $script:AppExpect }"
        { Import-VmCases -CasesPath "$TestDrive\a2.psd1" -CatalogPath $script:CatalogPath } | Should -Throw -ExpectedMessage "*AppId*"
    }
    It 'AppRemove z TweakId jest odrzucony (klucz AppId zamiast TweakId)' {
        Write-CasesFile -Path "$TestDrive\a3.psd1" -CaseBody "$script:AppBody; TweakId = 'vmtest.hklm-value'; Expect = @{ $script:AppExpect }"
        { Import-VmCases -CasesPath "$TestDrive\a3.psd1" -CatalogPath $script:CatalogPath } | Should -Throw -ExpectedMessage "*TweakId*AppRemove*"
    }
    It 'AppId poza AppRemove jest odrzucony' {
        Write-CasesFile -Path "$TestDrive\a4.psd1" -CaseBody "Name = 'x'; TweakId = 'vmtest.hklm-value'; AppId = 'appx:x'; Flow = 'DetectOnly'; RestorePoint = 'skip'; Expect = @{ Before = 'NotApplied' }"
        { Import-VmCases -CasesPath "$TestDrive\a4.psd1" -CatalogPath $script:CatalogPath } | Should -Throw -ExpectedMessage "*AppId*AppRemove*"
    }
    It 'AppId ze spacją jest odrzucony (Start-Process nie cytuje argumentów)' {
        Write-CasesFile -Path "$TestDrive\a5.psd1" -CaseBody "Name = 'app'; AppId = 'win32:HKLM\SOFTWARE\X\Uninstall\Dso Test'; Flow = 'AppRemove'; RestorePoint = 'skip'; Check = '`$true'; Expect = @{ $script:AppExpect }"
        { Import-VmCases -CasesPath "$TestDrive\a5.psd1" -CatalogPath $script:CatalogPath } | Should -Throw -ExpectedMessage "*AppId*spacj*"
    }
    It 'AppRemove bez Check jest odrzucony (wynik CLI nie zastępuje niezależnego odczytu)' {
        Write-CasesFile -Path "$TestDrive\a6.psd1" -CaseBody "Name = 'app'; AppId = 'appx:x'; Flow = 'AppRemove'; RestorePoint = 'skip'; Expect = @{ $script:AppExpect }"
        { Import-VmCases -CasesPath "$TestDrive\a6.psd1" -CatalogPath $script:CatalogPath } | Should -Throw -ExpectedMessage "*AppRemove*Check*"
    }
    It 'AppRemove bez wymaganego klucza Expect <Key> jest odrzucony' -ForEach @(
        @{ Key = 'ListedBefore' }, @{ Key = 'RemoveExit' }, @{ Key = 'RemoveOutcome' }, @{ Key = 'ListedAfter' }
    ) {
        $Parts = @("ListedBefore = `$true", "RemoveExit = 0", "RemoveOutcome = 'Removed'", "ListedAfter = `$false") | Where-Object { $_ -notlike "$Key *" }
        Write-CasesFile -Path "$TestDrive\a7.psd1" -CaseBody "$script:AppBody; Expect = @{ $($Parts -join '; ') }"
        { Import-VmCases -CasesPath "$TestDrive\a7.psd1" -CatalogPath $script:CatalogPath } | Should -Throw -ExpectedMessage "*$Key*"
    }
    It 'AppRemove z kluczem Expect innego przebiegu (<Key>) jest odrzucony' -ForEach @(@{ Key = 'ApplyExit' }, @{ Key = 'Before' }, @{ Key = 'ExecuteOutcome' }, @{ Key = 'RestorePointCreated' }) {
        Write-CasesFile -Path "$TestDrive\a8.psd1" -CaseBody "$script:AppBody; Expect = @{ $script:AppExpect; $Key = 0 }"
        { Import-VmCases -CasesPath "$TestDrive\a8.psd1" -CatalogPath $script:CatalogPath } | Should -Throw -ExpectedMessage "*$Key*"
    }
    It 'klucze Expect AppRemove (<Key>) są odrzucone w Flow Full' -ForEach @(@{ Key = 'ListedBefore' }, @{ Key = 'RemoveExit' }, @{ Key = 'WarningContains' }) {
        Write-CasesFile -Path "$TestDrive\a9.psd1" -CaseBody "Name = 'n'; TweakId = 'vmtest.hklm-value'; Flow = 'Full'; RestorePoint = 'skip'; Expect = @{ $script:ValidExpectFull; $Key = 0 }"
        { Import-VmCases -CasesPath "$TestDrive\a9.psd1" -CatalogPath $script:CatalogPath } | Should -Throw -ExpectedMessage "*$Key*"
    }
    It 'Group/Removability wymagają ListedBefore = $true' {
        Write-CasesFile -Path "$TestDrive\a10.psd1" -CaseBody "$script:AppBody; Expect = @{ ListedBefore = `$false; RemoveExit = 1; RemoveOutcome = 'Refused'; ListedAfter = `$false; Group = 'Other' }"
        { Import-VmCases -CasesPath "$TestDrive\a10.psd1" -CatalogPath $script:CatalogPath } | Should -Throw -ExpectedMessage "*Group*ListedBefore*"
    }
    It 'RunAs poza AppRemove i Execute jest odrzucony: <Flow>' -ForEach @(@{ Flow = 'DetectOnly' }, @{ Flow = 'Full' }) {
        Write-CasesFile -Path "$TestDrive\a11.psd1" -CaseBody "Name = 'x'; TweakId = 'vmtest.hklm-value'; Flow = '$Flow'; RestorePoint = 'skip'; RunAs = 'dso'; Expect = @{ $script:ValidExpectFull }"
        { Import-VmCases -CasesPath "$TestDrive\a11.psd1" -CatalogPath $script:CatalogPath } | Should -Throw -ExpectedMessage "*RunAs*AppRemove*Execute*"
    }
    It 'RunAs we Flow Execute jest dozwolony (Etap 4b: naprawa winget jako dso)' {
        Write-CasesFile -Path "$TestDrive\a11b.psd1" -CaseBody "Name = 'x'; TweakId = 'vmtest.action-marker'; Flow = 'Execute'; RestorePoint = 'skip'; RunAs = 'dso'; Check = '`$true'; Expect = @{ ExecuteExit = 0; ExecuteOutcome = 'Executed' }"
        $Cases = Import-VmCases -CasesPath "$TestDrive\a11b.psd1" -CatalogPath $script:CatalogPath
        $Cases[0].RunAs | Should -Be 'dso'
    }
    It 'RunAs w Execute z niedozwoloną nazwą konta jest odrzucony' {
        Write-CasesFile -Path "$TestDrive\a11c.psd1" -CaseBody "Name = 'x'; TweakId = 'vmtest.action-marker'; Flow = 'Execute'; RestorePoint = 'skip'; RunAs = 'dso x'; Check = '`$true'; Expect = @{ ExecuteExit = 0; ExecuteOutcome = 'Executed' }"
        { Import-VmCases -CasesPath "$TestDrive\a11c.psd1" -CatalogPath $script:CatalogPath } | Should -Throw -ExpectedMessage "*RunAs*"
    }
    It 'RunAs z niedozwoloną nazwą konta jest odrzucony' {
        Write-CasesFile -Path "$TestDrive\a12.psd1" -CaseBody "$script:AppBody; RunAs = 'dso x'; Expect = @{ $script:AppExpect }"
        { Import-VmCases -CasesPath "$TestDrive\a12.psd1" -CatalogPath $script:CatalogPath } | Should -Throw -ExpectedMessage "*RunAs*"
    }
    It 'AppRemove z kluczem <Key> jest odrzucony (CLI aplikacji nie używa katalogu ani snapshotu)' -ForEach @(@{ Key = 'Catalog'; Value = "'BuiltIn'" }, @{ Key = 'Snapshot'; Value = "@{ Registry = @(); Services = @() }" }) {
        Write-CasesFile -Path "$TestDrive\a13.psd1" -CaseBody "$script:AppBody; $Key = $Value; Expect = @{ $script:AppExpect }"
        { Import-VmCases -CasesPath "$TestDrive\a13.psd1" -CatalogPath $script:CatalogPath } | Should -Throw -ExpectedMessage "*$Key*AppRemove*"
    }
    It 'prawdziwe cases.psd1: przypadki AppRemove z planu (1, 1b, 1c, 2, 2b, 3, 4, 5, 6, 7), każdy z Check i bez spacji w AppId' {
        $Cases = Import-VmCases -CasesPath "$script:VmRoot\cases.psd1" -CatalogPath $script:CatalogPath
        $Apps = @($Cases | Where-Object { $_.Flow -eq 'AppRemove' })
        @($Apps | ForEach-Object { $_.Name }) | Should -Be @('app-store-allusers', 'app-store-user', 'app-store-required-aborted', 'app-win32-quiet', 'app-win32-required-aborted', 'app-win32-deferred', 'app-win32-exit3010', 'app-win32-msi', 'app-protected-calculator', 'app-win32-other-account')
        foreach ($App in $Apps) {
            $App.Check | Should -Not -BeNullOrEmpty
            $App.AppId | Should -Not -Match '\s'
        }
        ($Apps | Where-Object Name -eq 'app-store-allusers').Expect.WarningContains | Should -Be 'Usunięta dla wszystkich kont na tym komputerze.'
        ($Apps | Where-Object Name -eq 'app-store-user').RunAs | Should -Be 'dso'
        ($Apps | Where-Object Name -eq 'app-win32-required-aborted').RestorePoint | Should -Be 'required'
        # R20: `required` przy samych AppX = bez punktu nic nie usuwamy; na VM bez ochrony systemu pozostałe przypadki AppX podają skip/ifavailable jawnie.
        $StoreRequired = $Apps | Where-Object Name -eq 'app-store-required-aborted'
        $StoreRequired.RestorePoint | Should -Be 'required'
        $StoreRequired.AppId | Should -BeLike 'appx:*'
        $StoreRequired.Expect.Aborted | Should -BeTrue
        $StoreRequired.Expect.ListedAfter | Should -BeTrue
        foreach ($Store in @($Apps | Where-Object { $_.AppId -like 'appx:*' -and $_.Name -ne 'app-store-required-aborted' })) {
            $Store.RestorePoint | Should -BeIn @('skip', 'ifavailable') -Because "$($Store.Name): AppX na VM bez ochrony systemu z domyślnym required zostałby przerwany (R20)"
        }
        ($Apps | Where-Object Name -eq 'app-win32-exit3010').Expect.RequiresReboot | Should -BeTrue
        ($Apps | Where-Object Name -eq 'app-protected-calculator').Expect.Removability | Should -Be 'Protected'
        ($Apps | Where-Object Name -eq 'app-win32-other-account').Expect.Removability | Should -Be 'OtherAccount'
        foreach ($Name in @('app-win32-quiet', 'app-win32-deferred', 'app-win32-exit3010', 'app-win32-msi')) {
            ($Apps | Where-Object Name -eq $Name).RestorePoint | Should -Be 'ifavailable'
        }
    }
}

Describe 'Etap 4a T7: ConvertTo-DsoCliArguments dla aplikacji' {
    It 'list-apps: samo --list-apps i --out' {
        ConvertTo-DsoCliArguments -Command list-apps -OutPath 'C:\dso\out\l.json' | Should -Be @('--list-apps', '--out', 'C:\dso\out\l.json')
    }
    It 'remove-app: id, --out i --restore-point' {
        ConvertTo-DsoCliArguments -Command remove-app -TweakId 'appx:Microsoft.BingNews_8wekyb3d8bbwe' -OutPath 'C:\o.json' -RestorePoint required |
            Should -Be @('--remove-app', 'appx:Microsoft.BingNews_8wekyb3d8bbwe', '--out', 'C:\o.json', '--restore-point', 'required')
    }
    It 'remove-app bez id jest odrzucone' { { ConvertTo-DsoCliArguments -Command remove-app -OutPath 'C:\o.json' } | Should -Throw -ExpectedMessage '*remove-app*wymaga id*' }
    It 'list-apps z id jest odrzucone' { { ConvertTo-DsoCliArguments -Command list-apps -TweakId 'appx:x' -OutPath 'C:\o.json' } | Should -Throw -ExpectedMessage '*list-apps*bez id*' }
    It 'remove-app z id ze spacją jest odrzucone' { { ConvertTo-DsoCliArguments -Command remove-app -TweakId 'win32:HKLM\X\Dso Test' -OutPath 'C:\o.json' } | Should -Throw -ExpectedMessage '*spacj*' }
    It '<Command> z katalogiem jest odrzucone' -ForEach @(@{ Command = 'list-apps'; Id = '' }, @{ Command = 'remove-app'; Id = 'appx:x' }) {
        { ConvertTo-DsoCliArguments -Command $Command -TweakId $Id -CatalogPath 'C:\k.json' -OutPath 'C:\o.json' } | Should -Throw -ExpectedMessage '*katalog*'
    }
    It 'detect bez id nadal jest odrzucone' { { ConvertTo-DsoCliArguments -Command detect -OutPath 'C:\o.json' } | Should -Throw -ExpectedMessage '*detect*wymaga id*' }
}

Describe 'Etap 4a T7: Invoke-DsoCli -RunAsUser (zadanie Harmonogramu)' {
    It 'przekazuje konto do zdalnego bloku zadania Harmonogramu (dso / Interactive / Highest) i parsuje raport' {
        Mock -ModuleName VmRun Invoke-Command -RemoveParameterValidation 'Session' -MockWith {
            $global:DsoRunAsBlock = $ScriptBlock.ToString()
            $global:DsoRunAsArgs = $ArgumentList
            @{ ExitCode = 0; TimedOut = $false; Json = '{"exitCode":0,"outcome":"Removed"}' }
        }
        $Result = Invoke-DsoCli -Session $null -ExePath 'C:\dso\app\DSOptimizer.exe' -Arguments @('--remove-app', 'appx:x', '--out', 'C:\dso\out\r.json') -OutPath 'C:\dso\out\r.json' -RunAsUser 'dso'
        $Result.Report.outcome | Should -Be 'Removed'
        $global:DsoRunAsBlock | Should -Match 'Register-ScheduledTask'
        $global:DsoRunAsBlock | Should -Match 'LogonType Interactive'
        $global:DsoRunAsBlock | Should -Match 'RunLevel Highest'
        $global:DsoRunAsBlock | Should -Match 'Unregister-ScheduledTask'
        @($global:DsoRunAsArgs) | Should -Contain 'dso'
        Remove-Variable -Name DsoRunAsBlock, DsoRunAsArgs -Scope Global -ErrorAction SilentlyContinue
    }
    It 'bez -RunAsUser używa Start-Process (konto sesji PS Direct)' {
        Mock -ModuleName VmRun Invoke-Command -RemoveParameterValidation 'Session' -MockWith { $global:DsoPlainBlock = $ScriptBlock.ToString(); @{ ExitCode = 0; TimedOut = $false; Json = $null } }
        $null = Invoke-DsoCli -Session $null -ExePath 'x' -Arguments @() -OutPath 'C:\o.json'
        $global:DsoPlainBlock | Should -Match 'Start-Process'
        $global:DsoPlainBlock | Should -Not -Match 'Register-ScheduledTask'
        Remove-Variable -Name DsoPlainBlock -Scope Global -ErrorAction SilentlyContinue
    }
    Context 'zdalny blok zadania (lokalnie, cmdlety Harmonogramu podmienione)' {
        BeforeEach {
            $script:Dir = Join-Path $env:TEMP ("dso-runas-" + [guid]::NewGuid().ToString('N'))
            New-Item -ItemType Directory -Path $script:Dir | Out-Null
            $global:DsoTaskEvents = [System.Collections.Generic.List[string]]::new()
            $global:DsoTaskInfo = [System.Collections.Queue]::new()
            Mock -ModuleName VmRun Invoke-Command -RemoveParameterValidation 'Session' -MockWith { & $ScriptBlock @ArgumentList }
            Mock -ModuleName VmRun New-ScheduledTaskAction { $global:DsoTaskEvents.Add("action:$Execute|$Argument"); 'A' }
            Mock -ModuleName VmRun New-ScheduledTaskPrincipal { $global:DsoTaskEvents.Add("principal:$UserId|$LogonType|$RunLevel"); 'P' }
            Mock -ModuleName VmRun New-ScheduledTaskSettingsSet { 'S' }
            Mock -ModuleName VmRun Register-ScheduledTask -RemoveParameterType Action, Principal, Settings -MockWith { $global:DsoTaskEvents.Add("register:$TaskName") }
            Mock -ModuleName VmRun Start-ScheduledTask { $global:DsoTaskEvents.Add('start') }
            Mock -ModuleName VmRun Stop-ScheduledTask { $global:DsoTaskEvents.Add('stop') }
            Mock -ModuleName VmRun Unregister-ScheduledTask { $global:DsoTaskEvents.Add('unregister') }
            Mock -ModuleName VmRun Start-Sleep {}
            Mock -ModuleName VmRun Get-ScheduledTask { [pscustomobject]@{ State = $global:DsoTaskInfo.Peek().State } }
            Mock -ModuleName VmRun Get-ScheduledTaskInfo { $Item = $global:DsoTaskInfo.Dequeue(); if ($global:DsoTaskInfo.Count -eq 0) { $global:DsoTaskInfo.Enqueue($Item) }; [pscustomobject]@{ LastTaskResult = $Item.Result } }
        }
        AfterEach {
            Remove-Item -LiteralPath $script:Dir -Recurse -Force -ErrorAction SilentlyContinue
            Remove-Variable -Name DsoTaskEvents, DsoTaskInfo -Scope Global -ErrorAction SilentlyContinue
        }
        It 'czeka na koniec zadania (nie bierze 267011/267009 za wynik), zwraca LastTaskResult i raport, zawsze wyrejestrowuje' {
            $Out = Join-Path $script:Dir 'r.json'
            $global:DsoTaskOut = $Out
            foreach ($Step in @(@{ State = 'Ready'; Result = 267011 }, @{ State = 'Queued'; Result = 267011 }, @{ State = 'Running'; Result = 267009 }, @{ State = 'Ready'; Result = 1 })) { $global:DsoTaskInfo.Enqueue($Step) }
            Mock -ModuleName VmRun Start-ScheduledTask { $global:DsoTaskEvents.Add('start'); Set-Content -LiteralPath $global:DsoTaskOut -Value '{"exitCode":1,"outcome":"Refused"}' -Encoding UTF8 }
            $Result = Invoke-DsoCli -Session $null -ExePath 'C:\dso\app\DSOptimizer.exe' -Arguments @('--remove-app', 'appx:x', '--out', $Out) -OutPath $Out -RunAsUser 'dso'
            $Result.ExitCode | Should -Be 1
            $Result.TimedOut | Should -BeFalse
            $Result.Report.outcome | Should -Be 'Refused'
            $global:DsoTaskEvents | Should -Contain "action:C:\dso\app\DSOptimizer.exe|--remove-app appx:x --out $Out --lang pl"
            $global:DsoTaskEvents | Should -Contain 'principal:dso|Interactive|Highest'
            $global:DsoTaskEvents[$global:DsoTaskEvents.Count - 1] | Should -Be 'unregister'
            Remove-Variable -Name DsoTaskOut -Scope Global -ErrorAction SilentlyContinue
        }
        It 'przekroczenie czasu: ExitCode -1, TimedOut, zadanie zatrzymane i wyrejestrowane' {
            $global:DsoTaskInfo.Enqueue(@{ State = 'Running'; Result = 267009 })
            $Result = Invoke-DsoCli -Session $null -ExePath 'C:\dso\app\DSOptimizer.exe' -Arguments @('--list-apps') -OutPath (Join-Path $script:Dir 'x.json') -RunAsUser 'dso' -TimeoutSeconds 0
            $Result.ExitCode | Should -Be -1
            $Result.TimedOut | Should -BeTrue
            $global:DsoTaskEvents | Should -Contain 'stop'
            $global:DsoTaskEvents[$global:DsoTaskEvents.Count - 1] | Should -Be 'unregister'
        }
    }
}

Describe 'Etap 4a T7: Invoke-VmCase AppRemove' {
    BeforeEach {
        $global:DsoAppEvents = [System.Collections.Generic.List[string]]::new()
        Mock -ModuleName VmRun Invoke-VmSetup { $global:DsoAppEvents.Add('setup') }
        Mock -ModuleName VmRun Copy-VmFixtures { $global:DsoAppEvents.Add("fixtures:$Destination") }
        Mock -ModuleName VmRun Invoke-DsoCli {
            $global:DsoAppEvents.Add('cli:' + ($Arguments -join ' ') + "|as:$RunAsUser")
            [pscustomobject]@{ ExitCode = 0; TimedOut = $false; Report = [pscustomobject]@{ exitCode = 0 }; Json = '{}'; ReportError = $null }
        }
        Mock -ModuleName VmRun Invoke-Command -RemoveParameterValidation 'Session' -MockWith {
            $Text = $ScriptBlock.ToString()
            if ($Text -match 'MARKER-CHECK') { $global:DsoAppEvents.Add('check'); return $true }
            if ($Text -match 'MARKER-CLEANUP') { $global:DsoAppEvents.Add('cleanup') }
        }
        $script:Layout = Get-DsoVmLayout
        $script:AppCase = @{ Name = 'app'; AppId = 'appx:Microsoft.BingNews_8wekyb3d8bbwe'; Flow = 'AppRemove'; RestorePoint = 'ifavailable'; Setup = @('x'); Check = '# MARKER-CHECK'; Cleanup = '# MARKER-CLEANUP'
            Expect = @{ ListedBefore = $true; RemoveExit = 0; RemoveOutcome = 'Removed'; ListedAfter = $false } }
    }
    AfterEach { Remove-Variable -Name DsoAppEvents -Scope Global -ErrorAction SilentlyContinue }
    It 'kolejność: fixtures -> Setup -> list-apps -> remove-app -> list-apps -> Check -> Cleanup; bez katalogu' {
        $Record = Invoke-VmCase -Session $null -Case $script:AppCase -Layout $script:Layout
        $Record.Steps.Step | Should -Be @('list-before', 'remove-app', 'list-after')
        $Record.AppId | Should -Be 'appx:Microsoft.BingNews_8wekyb3d8bbwe'
        $Record.CheckResult | Should -Be 'Passed'
        $Record.CleanupError | Should -BeNullOrEmpty
        @($global:DsoAppEvents) | Should -Be @(
            'fixtures:C:\dso\fixtures'
            'setup'
            'cli:--list-apps --out C:\dso\out\app-list-before.json|as:'
            'cli:--remove-app appx:Microsoft.BingNews_8wekyb3d8bbwe --out C:\dso\out\app-remove-app.json --restore-point ifavailable|as:'
            'cli:--list-apps --out C:\dso\out\app-list-after.json|as:'
            'check'
            'cleanup'
        )
    }
    It 'RunAs: wszystkie trzy wywołania CLI idą jako to konto' {
        $script:AppCase.RunAs = 'dso'
        $null = Invoke-VmCase -Session $null -Case $script:AppCase -Layout $script:Layout
        @($global:DsoAppEvents | Where-Object { $_ -like 'cli:*' -and $_ -like '*|as:dso' }).Count | Should -Be 3
    }
    It 'Cleanup działa, gdy krok CLI rzuca; wyjątek nie jest połykany, Check nie startuje' {
        Mock -ModuleName VmRun Invoke-DsoCli { throw 'sesja zerwana' }
        { Invoke-VmCase -Session $null -Case $script:AppCase -Layout $script:Layout } | Should -Throw -ExpectedMessage '*sesja zerwana*'
        $global:DsoAppEvents | Should -Contain 'cleanup'
        $global:DsoAppEvents | Should -Not -Contain 'check'
    }
}

Describe 'Etap 4a T7: Copy-VmFixtures' {
    It 'tworzy katalog w VM i kopiuje pliki fixtures z repo przez sesję' {
        $global:DsoFixtureCalls = [System.Collections.Generic.List[string]]::new()
        Mock -ModuleName VmRun Invoke-Command -RemoveParameterValidation 'Session' -MockWith { $global:DsoFixtureCalls.Add('mkdir:' + ($ArgumentList -join ',')) }
        Mock -ModuleName VmRun Copy-Item -RemoveParameterValidation 'ToSession' -MockWith { $global:DsoFixtureCalls.Add("copy:$Path>$Destination") }
        InModuleScope VmRun { Copy-VmFixtures -Session $null -Destination 'C:\dso\fixtures' }
        $global:DsoFixtureCalls[0] | Should -Be 'mkdir:C:\dso\fixtures'
        $global:DsoFixtureCalls[1] | Should -BeLike 'copy:*\tests\vm\fixtures\*>C:\dso\fixtures'
        Remove-Variable -Name DsoFixtureCalls -Scope Global -ErrorAction SilentlyContinue
    }
}

Describe 'Etap 4a T7: fixtures pakietów testowych (na hoście tylko pliki, bez instalacji)' {
    BeforeAll { $script:Fixtures = Join-Path $script:VmRoot 'fixtures' }
    It 'Install-DsoTestProgram odmawia poza VM i niczego nie zmienia' {
        $Before = Test-Path -LiteralPath 'C:\DsoTest'
        { & (Join-Path $script:Fixtures 'Install-DsoTestProgram.ps1') -Mode quiet -KeyName DsoTestHost -FixturesDirectory $TestDrive } | Should -Throw -ExpectedMessage '*tylko w VM*'
        Test-Path -LiteralPath 'Registry::HKEY_LOCAL_MACHINE\SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall\DsoTestHost' | Should -BeFalse
        Test-Path -LiteralPath 'C:\DsoTest' | Should -Be $Before
    }
    It 'New-DsoTestMsi buduje bazę MSI x64 z ProductCode, rejestrem 64-bit i sekwencją instalacji (bez instalowania)' {
        $Path = Join-Path $TestDrive 'DsoTestMsi.msi'
        $Result = & (Join-Path $script:Fixtures 'New-DsoTestMsi.ps1') -Path $Path
        $Result.ProductCode | Should -Be '{4E3B2A10-7C5D-4F8E-9A1B-2C3D4E5F6A70}'
        # Każdy obiekt COM (widok, rekord, SummaryInformation) trzyma uchwyt pliku MSI do GC – bez zwolnienia
        # Remove-TestDrive nie usunie pliku i kontener Pester kończy się porażką (Result=Failed przy 0 porażek testów).
        $ComObjects = [System.Collections.Generic.List[object]]::new()
        $Installer = New-Object -ComObject WindowsInstaller.Installer
        $Database = $null
        $Summary = $null
        try {
            $Database = $Installer.OpenDatabase($Path, 0)
            $Read = {
                param([string]$Sql)
                $View = $Database.OpenView($Sql); $ComObjects.Add($View); $null = $View.Execute()
                $Rows = @(while ($Record = $View.Fetch()) { $ComObjects.Add($Record); $Record.StringData(1) + '=' + $Record.StringData(2) })
                $null = $View.Close()
                return $Rows
            }
            & $Read 'SELECT `Property`, `Value` FROM `Property`' | Should -Contain 'ProductCode={4E3B2A10-7C5D-4F8E-9A1B-2C3D4E5F6A70}'
            & $Read 'SELECT `Component`, `Attributes` FROM `Component`' | Should -Be @('DsoTestMsiRegistry=260')
            & $Read 'SELECT `Key`, `Name` FROM `Registry`' | Should -Be @('SOFTWARE\DSOptimizer.VmTests\Msi=Installed')
            @(& $Read 'SELECT `Action`, `Sequence` FROM `InstallExecuteSequence`') | Should -Contain 'RegisterProduct=6100'
            # Bez tabeli Media RegisterProduct kończy instalację błędem 2228 (msiexec 1603; pomiar VM).
            & $Read 'SELECT `DiskId`, `LastSequence` FROM `Media`' | Should -Be @('1=0')
            $Summary = $Database.GetType().InvokeMember('SummaryInformation', 'GetProperty', $null, $Database, @(0))
            $Summary.GetType().InvokeMember('Property', 'GetProperty', $null, $Summary, @(7)) | Should -Be 'x64;1033'
            $Summary.GetType().InvokeMember('Property', 'GetProperty', $null, $Summary, @(14)) | Should -Be 200
        }
        finally {
            foreach ($Com in $ComObjects) { $null = [System.Runtime.InteropServices.Marshal]::ReleaseComObject($Com) }
            if ($null -ne $Summary) { $null = [System.Runtime.InteropServices.Marshal]::ReleaseComObject($Summary) }
            if ($null -ne $Database) { $null = [System.Runtime.InteropServices.Marshal]::ReleaseComObject($Database) }
            $null = [System.Runtime.InteropServices.Marshal]::ReleaseComObject($Installer)
            [GC]::Collect()
            [GC]::WaitForPendingFinalizers()
        }
    }
    It 'DsoTestUninstaller.cs kompiluje się csc.exe z .NET Framework 4 (C# 5), bez uruchamiania' {
        $Csc = Join-Path $env:WINDIR 'Microsoft.NET\Framework64\v4.0.30319\csc.exe'
        $Exe = Join-Path $TestDrive 'DsoTestUninstaller.exe'
        $Output = & $Csc /nologo /target:exe "/out:$Exe" (Join-Path $script:Fixtures 'DsoTestUninstaller.cs') 2>&1
        $LASTEXITCODE | Should -Be 0 -Because ($Output -join ' ')
        Test-Path -LiteralPath $Exe | Should -BeTrue
    }
}

Describe 'Etap 4c T7: AppInstall w Import-VmCases' {
    BeforeAll {
        $script:InstBody = "Name = 'inst'; Flow = 'AppInstall'; Command = 'install-apps'; AppIds = @('install.7zip', 'install.vlc'); Check = '`$true'"
        $script:ListBody = "Name = 'list'; Flow = 'AppInstall'; Command = 'list-install'; Check = '`$true'"
    }
    It 'przyjmuje install-apps z AppIds, RunAs, Setup, Cleanup i pełnym Expect' {
        Write-CasesFile -Path "$TestDrive\i1.psd1" -CaseBody "$script:InstBody; RunAs = 'dso'; Setup = @('Write-Output 1'); Cleanup = 'Write-Output 1'; Expect = @{ Exit = 1; Outcomes = @{ 'install.7zip' = @('Failed', 'Skipped'); 'install.vlc' = 'Skipped' }; WingetState = 'Ready'; MessageContains = 'brak połączenia' }"
        $Cases = Import-VmCases -CasesPath "$TestDrive\i1.psd1" -CatalogPath $script:CatalogPath
        $Cases[0].Flow | Should -Be 'AppInstall'
        @($Cases[0].AppIds) | Should -Be @('install.7zip', 'install.vlc')
        $Cases[0].RunAs | Should -Be 'dso'
    }
    It 'przyjmuje list-install z AppsCatalog i Installed' {
        Write-CasesFile -Path "$TestDrive\i2.psd1" -CaseBody "$script:ListBody; AppsCatalog = 'C:\dso\fixtures\vm-test-apps.json'; Expect = @{ Exit = 0; WingetState = @('Ready', 'OtherAccount'); Installed = @{ 'install.dso-test-msi' = `$true; 'install.dso-test-absent' = `$false } }"
        $Cases = Import-VmCases -CasesPath "$TestDrive\i2.psd1" -CatalogPath $script:CatalogPath
        $Cases[0].Command | Should -Be 'list-install'
        $Cases[0].AppsCatalog | Should -Be 'C:\dso\fixtures\vm-test-apps.json'
    }
    It 'AppInstall bez Check jest odrzucony (raport CLI nie zastępuje niezależnego odczytu)' {
        Write-CasesFile -Path "$TestDrive\i3.psd1" -CaseBody "Name = 'inst'; Flow = 'AppInstall'; Command = 'install-apps'; AppIds = @('install.7zip'); Expect = @{ Exit = 1 }"
        { Import-VmCases -CasesPath "$TestDrive\i3.psd1" -CatalogPath $script:CatalogPath } | Should -Throw -ExpectedMessage "*AppInstall*Check*"
    }
    It 'AppInstall bez Expect.Exit jest odrzucony' {
        Write-CasesFile -Path "$TestDrive\i4.psd1" -CaseBody "$script:InstBody; Expect = @{ WingetState = 'Ready' }"
        { Import-VmCases -CasesPath "$TestDrive\i4.psd1" -CatalogPath $script:CatalogPath } | Should -Throw -ExpectedMessage "*Exit*"
    }
    It 'AppInstall bez Command albo z nieznanym Command jest odrzucony: <Body>' -ForEach @(
        @{ Body = "Name = 'x'; Flow = 'AppInstall'; Check = '`$true'; Expect = @{ Exit = 0 }" }
        @{ Body = "Name = 'x'; Flow = 'AppInstall'; Command = 'search-apps'; Check = '`$true'; Expect = @{ Exit = 0 }" }
    ) {
        Write-CasesFile -Path "$TestDrive\i5.psd1" -CaseBody $Body
        { Import-VmCases -CasesPath "$TestDrive\i5.psd1" -CatalogPath $script:CatalogPath } | Should -Throw -ExpectedMessage "*Command*"
    }
    It 'Installed tylko przy list-install' {
        Write-CasesFile -Path "$TestDrive\i6.psd1" -CaseBody "$script:InstBody; Expect = @{ Exit = 1; Installed = @{ 'install.7zip' = `$false } }"
        { Import-VmCases -CasesPath "$TestDrive\i6.psd1" -CatalogPath $script:CatalogPath } | Should -Throw -ExpectedMessage "*Installed*list-install*"
    }
    It 'Installed musi mapować id na wartość logiczną' {
        Write-CasesFile -Path "$TestDrive\i6b.psd1" -CaseBody "$script:ListBody; Expect = @{ Exit = 0; Installed = @{ 'install.7zip' = 'tak' } }"
        { Import-VmCases -CasesPath "$TestDrive\i6b.psd1" -CatalogPath $script:CatalogPath } | Should -Throw -ExpectedMessage "*Installed*logiczn*"
    }
    It 'Outcomes tylko przy install-apps i tylko dla id z AppIds' -ForEach @(
        @{ Body = "Name = 'list'; Flow = 'AppInstall'; Command = 'list-install'; Check = '`$true'; Expect = @{ Exit = 0; Outcomes = @{ 'install.7zip' = 'Failed' } }"; Fragment = '*Outcomes*install-apps*' }
        @{ Body = "Name = 'inst'; Flow = 'AppInstall'; Command = 'install-apps'; AppIds = @('install.7zip'); Check = '`$true'; Expect = @{ Exit = 1; Outcomes = @{ 'install.vlc' = 'Failed' } }"; Fragment = '*install.vlc*AppIds*' }
    ) {
        Write-CasesFile -Path "$TestDrive\i7.psd1" -CaseBody $Body
        { Import-VmCases -CasesPath "$TestDrive\i7.psd1" -CatalogPath $script:CatalogPath } | Should -Throw -ExpectedMessage $Fragment
    }
    It 'przyjmuje Expect.WingetCodes przy install-apps (id -> dozwolone kody albo $null = brak kodu)' {
        Write-CasesFile -Path "$TestDrive\i15.psd1" -CaseBody "$script:InstBody; Expect = @{ Exit = 1; WingetCodes = @{ 'install.vlc' = @('0x8A15000F', '0x80072EE7'); 'install.7zip' = `$null } }"
        $Cases = Import-VmCases -CasesPath "$TestDrive\i15.psd1" -CatalogPath $script:CatalogPath
        @($Cases[0].Expect.WingetCodes['install.vlc']) | Should -Be @('0x8A15000F', '0x80072EE7')
        $Cases[0].Expect.WingetCodes.ContainsKey('install.7zip') | Should -BeTrue
        $Cases[0].Expect.WingetCodes['install.7zip'] | Should -BeNullOrEmpty
    }
    It 'WingetCodes jest odrzucone: <Why>' -ForEach @(
        @{ Why = 'list-install'; Body = "Name = 'list'; Flow = 'AppInstall'; Command = 'list-install'; Check = '`$true'; Expect = @{ Exit = 0; WingetCodes = @{ 'install.7zip' = `$null } }"; Fragment = '*WingetCodes*install-apps*' }
        @{ Why = 'id spoza AppIds'; Body = "Name = 'inst'; Flow = 'AppInstall'; Command = 'install-apps'; AppIds = @('install.7zip'); Check = '`$true'; Expect = @{ Exit = 1; WingetCodes = @{ 'install.vlc' = '0x8A15000F' } }"; Fragment = '*WingetCodes*install.vlc*AppIds*' }
        @{ Why = 'pusta hashtable'; Body = "Name = 'inst'; Flow = 'AppInstall'; Command = 'install-apps'; AppIds = @('install.7zip', 'install.vlc'); Check = '`$true'; Expect = @{ Exit = 1; WingetCodes = @{} }"; Fragment = '*WingetCodes*niepust*' }
        @{ Why = 'nie hashtable'; Body = "Name = 'inst'; Flow = 'AppInstall'; Command = 'install-apps'; AppIds = @('install.7zip', 'install.vlc'); Check = '`$true'; Expect = @{ Exit = 1; WingetCodes = '0x8A15000F' }"; Fragment = '*WingetCodes*niepust*' }
        @{ Why = 'kod bez formatu 0x + 8 cyfr szesnastkowych'; Body = "Name = 'inst'; Flow = 'AppInstall'; Command = 'install-apps'; AppIds = @('install.7zip', 'install.vlc'); Check = '`$true'; Expect = @{ Exit = 1; WingetCodes = @{ 'install.vlc' = @('0x8A15000F', '8A150107') } }"; Fragment = '*WingetCodes*install.vlc*0x*' }
        @{ Why = 'małe litery w kodzie (raport ma wielkie)'; Body = "Name = 'inst'; Flow = 'AppInstall'; Command = 'install-apps'; AppIds = @('install.7zip', 'install.vlc'); Check = '`$true'; Expect = @{ Exit = 1; WingetCodes = @{ 'install.vlc' = '0x8a15000f' } }"; Fragment = '*WingetCodes*install.vlc*0x*' }
        @{ Why = 'pusta lista kodów'; Body = "Name = 'inst'; Flow = 'AppInstall'; Command = 'install-apps'; AppIds = @('install.7zip', 'install.vlc'); Check = '`$true'; Expect = @{ Exit = 1; WingetCodes = @{ 'install.vlc' = @() } }"; Fragment = '*WingetCodes*install.vlc*' }
    ) {
        Write-CasesFile -Path "$TestDrive\i16.psd1" -CaseBody $Body
        { Import-VmCases -CasesPath "$TestDrive\i16.psd1" -CatalogPath $script:CatalogPath } | Should -Throw -ExpectedMessage $Fragment
    }
    It 'install-apps wymaga niepustych AppIds' -ForEach @(
        @{ Ids = '' }
        @{ Ids = "AppIds = @(); " }
    ) {
        Write-CasesFile -Path "$TestDrive\i8.psd1" -CaseBody "Name = 'inst'; Flow = 'AppInstall'; Command = 'install-apps'; $($Ids)Check = '`$true'; Expect = @{ Exit = 1 }"
        { Import-VmCases -CasesPath "$TestDrive\i8.psd1" -CatalogPath $script:CatalogPath } | Should -Throw -ExpectedMessage "*AppIds*"
    }
    It 'AppIds ze spacją, przecinkiem albo pustym elementem jest odrzucone: <Id>' -ForEach @(
        @{ Id = 'install.7 zip' }, @{ Id = 'install.7zip,install.vlc' }, @{ Id = '' }
    ) {
        Write-CasesFile -Path "$TestDrive\i9.psd1" -CaseBody "Name = 'inst'; Flow = 'AppInstall'; Command = 'install-apps'; AppIds = @('install.vlc', '$Id'); Check = '`$true'; Expect = @{ Exit = 1 }"
        { Import-VmCases -CasesPath "$TestDrive\i9.psd1" -CatalogPath $script:CatalogPath } | Should -Throw -ExpectedMessage "*AppIds*"
    }
    It 'list-install z AppIds jest odrzucone' {
        Write-CasesFile -Path "$TestDrive\i10.psd1" -CaseBody "$script:ListBody; AppIds = @('install.7zip'); Expect = @{ Exit = 0 }"
        { Import-VmCases -CasesPath "$TestDrive\i10.psd1" -CatalogPath $script:CatalogPath } | Should -Throw -ExpectedMessage "*AppIds*install-apps*"
    }
    It 'AppInstall z kluczem <Key> jest odrzucony' -ForEach @(
        @{ Key = 'TweakId'; Value = "'vmtest.hklm-value'" }
        @{ Key = 'AppId'; Value = "'appx:x'" }
        @{ Key = 'Catalog'; Value = "'BuiltIn'" }
        @{ Key = 'Snapshot'; Value = "@{ Registry = @(); Services = @() }" }
        @{ Key = 'RestorePoint'; Value = "'skip'" }
    ) {
        Write-CasesFile -Path "$TestDrive\i11.psd1" -CaseBody "$script:InstBody; $Key = $Value; Expect = @{ Exit = 1 }"
        { Import-VmCases -CasesPath "$TestDrive\i11.psd1" -CatalogPath $script:CatalogPath } | Should -Throw -ExpectedMessage "*$Key*AppInstall*"
    }
    It 'AppInstall z kluczem Expect innego przebiegu (<Key>) jest odrzucony' -ForEach @(@{ Key = 'RemoveExit' }, @{ Key = 'ApplyExit' }, @{ Key = 'ExecuteOutcome' }) {
        Write-CasesFile -Path "$TestDrive\i12.psd1" -CaseBody "$script:InstBody; Expect = @{ Exit = 1; $Key = 0 }"
        { Import-VmCases -CasesPath "$TestDrive\i12.psd1" -CatalogPath $script:CatalogPath } | Should -Throw -ExpectedMessage "*$Key*"
    }
    It 'AppsCatalog ze spacją albo bez ścieżki bezwzględnej jest odrzucony: <Path>' -ForEach @(@{ Path = 'C:\dso\fix tures\a.json' }, @{ Path = 'a.json' }) {
        Write-CasesFile -Path "$TestDrive\i13.psd1" -CaseBody "$script:ListBody; AppsCatalog = '$Path'; Expect = @{ Exit = 0 }"
        { Import-VmCases -CasesPath "$TestDrive\i13.psd1" -CatalogPath $script:CatalogPath } | Should -Throw -ExpectedMessage "*AppsCatalog*"
    }
    It 'klucze Command/AppIds/AppsCatalog poza AppInstall są odrzucone: <Key>' -ForEach @(@{ Key = 'Command'; Value = "'list-install'" }, @{ Key = 'AppIds'; Value = "@('install.7zip')" }, @{ Key = 'AppsCatalog'; Value = "'C:\dso\a.json'" }) {
        Write-CasesFile -Path "$TestDrive\i14.psd1" -CaseBody "Name = 'x'; TweakId = 'vmtest.hklm-value'; Flow = 'DetectOnly'; RestorePoint = 'skip'; $Key = $Value; Expect = @{ Before = 'NotApplied' }"
        { Import-VmCases -CasesPath "$TestDrive\i14.psd1" -CatalogPath $script:CatalogPath } | Should -Throw -ExpectedMessage "*$Key*AppInstall*"
    }
    It 'prawdziwe cases.psd1: przypadki AppInstall z planu (offline, inne konto, MSI z listą testową, lista na innym koncie)' {
        $Cases = Import-VmCases -CasesPath "$script:VmRoot\cases.psd1" -CatalogPath $script:CatalogPath
        $Inst = @($Cases | Where-Object { $_.Flow -eq 'AppInstall' })
        @($Inst | ForEach-Object { $_.Name }) | Should -Be @('install-other-account', 'list-install-other-account', 'install-offline-dso', 'list-install-msi-dso', 'install-rustdesk-offline-dso', 'install-rustdesk-steam-offline-dso')
        foreach ($Case in $Inst) { $Case.Check | Should -Not -BeNullOrEmpty }
        $Offline = $Inst | Where-Object Name -eq 'install-offline-dso'
        $Offline.RunAs | Should -Be 'dso'
        @($Offline.AppIds) | Should -Be @('install.7zip', 'install.vlc')
        $Offline.Expect.Exit | Should -Be 1
        # M2 (R19): kolejność serii = kolejność apps.json (VLC, multimedia, przed 7-Zip, narzędzia) -> dokładne wyniki i kody.
        $Offline.Expect.Outcomes['install.vlc'] | Should -Be 'Failed'
        $Offline.Expect.Outcomes['install.7zip'] | Should -Be 'Skipped'
        @($Offline.Expect.WingetCodes['install.vlc']) | Should -Be @('0x8A15000F', '0x8A150107', '0x8A150008', '0x80072EE7')
        $Offline.Expect.WingetCodes.ContainsKey('install.7zip') | Should -BeTrue
        $Offline.Expect.WingetCodes['install.7zip'] | Should -BeNullOrEmpty
        $Offline.Check | Should -BeLike '*7-Zip*'
        $Offline.Check | Should -BeLike '*VLC*'
        $Offline.Check | Should -BeLike '*HKEY_USERS\{UserSid}*'
        $Other = $Inst | Where-Object Name -eq 'install-other-account'
        $Other.ContainsKey('RunAs') | Should -BeFalse
        $Other.Expect.Exit | Should -Be 1
        $Other.Expect.Outcomes['install.7zip'] | Should -Be 'Refused'
        $Other.Expect.Outcomes['install.vlc'] | Should -Be 'Refused'
        $Other.Expect.MessageContains | Should -Be 'uruchom DSOptimizer na koncie'
        $Other.Check | Should -BeLike '*C:\ProgramData\DSOptimizer\logs\winget*'
        $Msi = $Inst | Where-Object Name -eq 'list-install-msi-dso'
        $Msi.AppsCatalog | Should -Be 'C:\dso\fixtures\vm-test-apps.json'
        $Msi.RunAs | Should -Be 'dso'
        $Msi.Expect.WingetState | Should -Be 'Ready'
        $Msi.Expect.Installed['install.dso-test-msi'] | Should -BeTrue
        $Msi.Expect.Installed['install.dso-test-absent'] | Should -BeFalse
        $Msi.Cleanup | Should -BeLike '*/x*'
        ($Inst | Where-Object Name -eq 'list-install-other-account').Expect.WingetState | Should -Be 'OtherAccount'
    }
    It 'prawdziwe cases.psd1: RustDesk bez sieci (plan RustDesk, Task 6) – sam RustDesk i RustDesk + Steam jako dso, nic nie pobrane ani zainstalowane' {
        $Cases = Import-VmCases -CasesPath "$script:VmRoot\cases.psd1" -CatalogPath $script:CatalogPath
        $Alone = $Cases | Where-Object Name -eq 'install-rustdesk-offline-dso'
        $Mixed = $Cases | Where-Object Name -eq 'install-rustdesk-steam-offline-dso'
        foreach ($Case in $Alone, $Mixed) {
            $Case.Flow | Should -Be 'AppInstall'
            $Case.Command | Should -Be 'install-apps'
            $Case.RunAs | Should -Be 'dso'
            $Case.ContainsKey('Skip') | Should -BeFalse
            $Case.Expect.Exit | Should -Be 1
            $Case.Expect.WingetState | Should -Be 'Ready'
            $Case.Expect.MessageContains | Should -BeExactly 'brak połączenia z GitHubem'
            $Case.Expect.Outcomes['install.rustdesk'] | Should -Be 'Failed'
            $Case.Expect.WingetCodes.ContainsKey('install.rustdesk') | Should -BeTrue
            $Case.Expect.WingetCodes['install.rustdesk'] | Should -BeNullOrEmpty
            # Check: brak wpisu Uninstall RustDesk (HKLM 64 i 32), brak usługi RustDesk, folder pobrań nieobecny albo bez plików.
            $Case.Check | Should -BeLike '*SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall*'
            $Case.Check | Should -BeLike '*SOFTWARE\WOW6432Node\Microsoft\Windows\CurrentVersion\Uninstall*'
            $Case.Check | Should -BeLike "*-eq 'RustDesk'*"
            $Case.Check | Should -BeLike "*Get-Service -Name 'RustDesk'*"
            $Case.Check | Should -BeLike '*C:\ProgramData\DSOptimizer\downloads*'
            $Errors = $null
            $null = [System.Management.Automation.Language.Parser]::ParseInput($Case.Check, [ref]$null, [ref]$Errors)
            @($Errors).Count | Should -Be 0 -Because $Case.Name
        }
        @($Alone.AppIds) | Should -Be @('install.rustdesk')
        @($Mixed.AppIds) | Should -Be @('install.rustdesk', 'install.steam')
        # Kolejność serii = kolejność apps.json: RustDesk (Zdalna pomoc) przed Steam (Gry), więc brak sieci kończy serię na RustDesku.
        $Mixed.Expect.Outcomes['install.steam'] | Should -Be 'Skipped'
        $Mixed.Expect.WingetCodes.ContainsKey('install.steam') | Should -BeTrue
        $Mixed.Expect.WingetCodes['install.steam'] | Should -BeNullOrEmpty
        $Mixed.Check | Should -BeLike '*C:\ProgramData\DSOptimizer\logs\winget*Valve.Steam-*.log*'
        $Alone.Check | Should -Not -BeLike '*Valve.Steam*'
        $Order = @((Get-Content -LiteralPath (Join-Path (Get-DsoRepoRoot) 'src\DSOptimizer.Catalog\catalog\apps.json') -Raw -Encoding UTF8 | ConvertFrom-Json).entries | ForEach-Object { [string]$_.id })
        $Order.IndexOf('install.rustdesk') | Should -BeLessThan $Order.IndexOf('install.steam')
    }
    It 'fixtures/vm-test-apps.json: dwa wpisy listy testowej zgodne z formatem apps.json' {
        $Json = Get-Content -LiteralPath (Join-Path $script:VmRoot 'fixtures\vm-test-apps.json') -Raw -Encoding UTF8 | ConvertFrom-Json
        $Entries = @($Json.entries)
        @($Entries | ForEach-Object { $_.id }) | Should -Be @('install.dso-test-msi', 'install.dso-test-absent')
        @($Entries | ForEach-Object { $_.wingetId }) | Should -Be @('Dso.TestMsi', 'Dso.TestAbsent')
        @($Entries | ForEach-Object { $_.match.displayName }) | Should -Be @('DSO Test MSI', 'DSO Test Absent')
        foreach ($Entry in $Entries) {
            $Entry.category | Should -Be 'tools'
            $Entry.source | Should -Be 'https://github.com/ds99-01'
            $Entry.title | Should -Not -BeNullOrEmpty
            $Entry.summary | Should -Not -BeNullOrEmpty
        }
    }
}

Describe 'Etap 4c T7: ConvertTo-DsoCliArguments dla instalacji' {
    It 'list-install: samo --list-install i --out' {
        ConvertTo-DsoCliArguments -Command list-install -OutPath 'C:\dso\out\l.json' | Should -Be @('--list-install', '--out', 'C:\dso\out\l.json')
    }
    It 'list-install z listą testową: --apps-catalog przed --out' {
        ConvertTo-DsoCliArguments -Command list-install -AppsCatalog 'C:\dso\fixtures\vm-test-apps.json' -OutPath 'C:\o.json' |
            Should -Be @('--list-install', '--apps-catalog', 'C:\dso\fixtures\vm-test-apps.json', '--out', 'C:\o.json')
    }
    It 'install-apps: id połączone przecinkiem bez spacji' {
        ConvertTo-DsoCliArguments -Command install-apps -AppIds @('install.7zip', 'install.vlc') -OutPath 'C:\o.json' |
            Should -Be @('--install-apps', 'install.7zip,install.vlc', '--out', 'C:\o.json')
    }
    It 'install-apps bez AppIds jest odrzucone' { { ConvertTo-DsoCliArguments -Command install-apps -OutPath 'C:\o.json' } | Should -Throw -ExpectedMessage '*install-apps*AppIds*' }
    It 'install-apps z id ze spacją albo przecinkiem jest odrzucone: <Id>' -ForEach @(@{ Id = 'install.7 zip' }, @{ Id = 'a,b' }, @{ Id = '' }) {
        { ConvertTo-DsoCliArguments -Command install-apps -AppIds @('install.vlc', $Id) -OutPath 'C:\o.json' } | Should -Throw -ExpectedMessage '*AppIds*'
    }
    It 'list-install z AppIds jest odrzucone' { { ConvertTo-DsoCliArguments -Command list-install -AppIds @('install.7zip') -OutPath 'C:\o.json' } | Should -Throw -ExpectedMessage '*AppIds*install-apps*' }
    It '<Command> z id tweaka albo katalogiem tweaków jest odrzucone' -ForEach @(@{ Command = 'list-install' }, @{ Command = 'install-apps' }) {
        { ConvertTo-DsoCliArguments -Command $Command -AppIds @('install.7zip') -CatalogPath 'C:\k.json' -OutPath 'C:\o.json' } | Should -Throw -ExpectedMessage '*katalog*'
        { ConvertTo-DsoCliArguments -Command $Command -TweakId 'x.y' -OutPath 'C:\o.json' } | Should -Throw -ExpectedMessage '*id*'
    }
    It '--apps-catalog przy poleceniu spoza instalacji jest odrzucone' {
        { ConvertTo-DsoCliArguments -Command detect -TweakId 'x.y' -AppsCatalog 'C:\a.json' -OutPath 'C:\o.json' } | Should -Throw -ExpectedMessage '*AppsCatalog*'
    }
    It 'AppsCatalog ze spacją jest odrzucony' {
        { ConvertTo-DsoCliArguments -Command list-install -AppsCatalog 'C:\a b.json' -OutPath 'C:\o.json' } | Should -Throw -ExpectedMessage '*spacj*'
    }
}

Describe 'Etap 4c T7: Invoke-VmCase AppInstall' {
    BeforeEach {
        $global:DsoInstEvents = [System.Collections.Generic.List[string]]::new()
        Mock -ModuleName VmRun Invoke-VmSetup { $global:DsoInstEvents.Add('setup') }
        Mock -ModuleName VmRun Copy-VmFixtures { $global:DsoInstEvents.Add("fixtures:$Destination") }
        Mock -ModuleName VmRun Invoke-DsoCli {
            $global:DsoInstEvents.Add('cli:' + ($Arguments -join ' ') + "|as:$RunAsUser")
            [pscustomobject]@{ ExitCode = 1; TimedOut = $false; Report = [pscustomobject]@{ exitCode = 1 }; Json = '{}'; ReportError = $null }
        }
        Mock -ModuleName VmRun Invoke-Command -RemoveParameterValidation 'Session' -MockWith {
            $Text = $ScriptBlock.ToString()
            if ($Text -match 'MARKER-CHECK') { $global:DsoInstEvents.Add('check'); return $true }
            if ($Text -match 'MARKER-CLEANUP') { $global:DsoInstEvents.Add('cleanup') }
        }
        $script:Layout = Get-DsoVmLayout
        $script:InstCase = @{ Name = 'inst'; Flow = 'AppInstall'; Command = 'install-apps'; AppIds = @('install.7zip', 'install.vlc'); RunAs = 'dso'
            Setup = @('x'); Check = '# MARKER-CHECK'; Cleanup = '# MARKER-CLEANUP'; Expect = @{ Exit = 1 } }
    }
    AfterEach { Remove-Variable -Name DsoInstEvents -Scope Global -ErrorAction SilentlyContinue }
    It 'kolejność: fixtures -> Setup -> install-apps -> Check -> Cleanup; jeden krok CLI jako RunAs' {
        $Record = Invoke-VmCase -Session $null -Case $script:InstCase -Layout $script:Layout
        $Record.Steps.Step | Should -Be @('install-apps')
        $Record.Command | Should -Be 'install-apps'
        $Record.CheckResult | Should -Be 'Passed'
        $Record.CleanupError | Should -BeNullOrEmpty
        @($global:DsoInstEvents) | Should -Be @(
            'fixtures:C:\dso\fixtures'
            'setup'
            'cli:--install-apps install.7zip,install.vlc --out C:\dso\out\inst-install-apps.json|as:dso'
            'check'
            'cleanup'
        )
    }
    It 'list-install z AppsCatalog, bez RunAs = konto sesji PS Direct' {
        $Case = @{ Name = 'list'; Flow = 'AppInstall'; Command = 'list-install'; AppsCatalog = 'C:\dso\fixtures\vm-test-apps.json'; Check = '# MARKER-CHECK'; Expect = @{ Exit = 0 } }
        $Record = Invoke-VmCase -Session $null -Case $Case -Layout $script:Layout
        $Record.Steps.Step | Should -Be @('list-install')
        $global:DsoInstEvents | Should -Contain 'cli:--list-install --apps-catalog C:\dso\fixtures\vm-test-apps.json --out C:\dso\out\list-list-install.json|as:'
    }
    It 'Cleanup działa, gdy krok CLI rzuca; wyjątek nie jest połykany, Check nie startuje' {
        Mock -ModuleName VmRun Invoke-DsoCli { throw 'sesja zerwana' }
        { Invoke-VmCase -Session $null -Case $script:InstCase -Layout $script:Layout } | Should -Throw -ExpectedMessage '*sesja zerwana*'
        $global:DsoInstEvents | Should -Contain 'cleanup'
        $global:DsoInstEvents | Should -Not -Contain 'check'
    }
}

AfterAll { Clear-NoLiveVm }
