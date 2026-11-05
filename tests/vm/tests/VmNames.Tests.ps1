BeforeDiscovery {
    $script:HistoricScripts = @('C1', 'C2', 'C3', 'C4', 'C5', 'E3A', 'E4A1', 'E4A2', 'E4A3', 'E4B1', 'E4B2') | ForEach-Object { "Invoke-${_}Exploration.ps1" }
}

BeforeAll {
    . (Join-Path $PSScriptRoot 'Support\NoLiveVm.ps1')
    Import-Module (Join-Path $PSScriptRoot '..\modules\VmCommon.psm1') -Force
    Import-Module (Join-Path $PSScriptRoot '..\modules\VmHost.psm1') -Force

    $script:VmRoot = (Resolve-Path (Join-Path $PSScriptRoot '..')).Path
    $script:RepoRoot = (Resolve-Path (Join-Path $PSScriptRoot '..\..\..')).Path
    $VmTestsDir = Join-Path $script:VmRoot 'tests'
    $ReleaseTestsDir = Join-Path $script:RepoRoot 'tools\release\tests'
    $script:CodeFiles = @(
        @(Get-ChildItem -LiteralPath $script:VmRoot -Recurse -File | Where-Object { '.ps1', '.psm1', '.psd1' -contains $_.Extension }) +
        @(Get-ChildItem -LiteralPath (Join-Path $script:RepoRoot 'tools\release') -Recurse -File | Where-Object { '.ps1', '.psm1', '.psd1' -contains $_.Extension })
    ) | Where-Object { -not $_.FullName.StartsWith($VmTestsDir + '\') -and -not $_.FullName.StartsWith($ReleaseTestsDir + '\') } |
        Select-Object -ExpandProperty FullName

    $script:HistoricNames = @('Invoke-C1Exploration.ps1', 'Invoke-C2Exploration.ps1', 'Invoke-C3Exploration.ps1', 'Invoke-C4Exploration.ps1', 'Invoke-C5Exploration.ps1',
        'Invoke-E3AExploration.ps1', 'Invoke-E4A1Exploration.ps1', 'Invoke-E4A2Exploration.ps1', 'Invoke-E4A3Exploration.ps1', 'Invoke-E4B1Exploration.ps1', 'Invoke-E4B2Exploration.ps1')

    function script:Get-Ast {
        param([string]$Path)
        $Tokens = $null; $Errors = $null
        $Ast = [System.Management.Automation.Language.Parser]::ParseFile($Path, [ref]$Tokens, [ref]$Errors)
        if (@($Errors).Count -gt 0) { throw "Błąd składni w '$Path': $($Errors[0].Message)" }
        return $Ast
    }

    function script:Get-AttributeAsts {
        param([string]$Path, [string]$TypeName)
        $Ast = Get-Ast -Path $Path
        return @($Ast.FindAll({ param($Node) $Node -is [System.Management.Automation.Language.AttributeAst] -and $Node.TypeName.Name -eq $TypeName }, $true))
    }

    function script:Get-ScriptDefault {
        # Wartość domyślna parametru $VmName głównego bloku param() skryptu.
        param([string]$Path)
        $Ast = Get-Ast -Path $Path
        $Parameter = $Ast.ParamBlock.Parameters | Where-Object { $_.Name.VariablePath.UserPath -eq 'VmName' }
        return @($Parameter.DefaultValue.SafeGetValue())
    }
}

Describe 'Nazwy VM w kodzie zgodne ze źródłem (Get-DsoVmDefinitions)' {
    It 'znajduje pliki kodu do sprawdzenia' {
        @($script:CodeFiles).Count | Should -BeGreaterThan 30
    }

    It 'w kodzie nie ma starych nazw DSO-26H2-' {
        @(Select-String -Path $script:CodeFiles -Pattern 'DSO-26H2-' -SimpleMatch).Count | Should -Be 0
    }

    It 'ValidateSet z nazwami VM: historyczne = PL, reszta = cztery nazwy w kolejności źródła' {
        $Total = 0
        foreach ($Path in $script:CodeFiles) {
            $Historic = $script:HistoricNames -contains (Split-Path -Leaf $Path)
            foreach ($Attribute in (Get-AttributeAsts -Path $Path -TypeName 'ValidateSet')) {
                $Values = @($Attribute.PositionalArguments | ForEach-Object { $_.Value })
                if ($Values.Count -eq 0 -or $Values[0] -notlike 'DSO-*') { continue }
                $Total++
                if ($Historic) { $Values | Should -Be @('DSO-PL-Home', 'DSO-PL-Pro') -Because "$Path :: $($Attribute.Extent.StartLineNumber)" }
                else { $Values | Should -Be @(Get-DsoVmNames) -Because "$Path :: $($Attribute.Extent.StartLineNumber)" }
            }
        }
        $Total | Should -BeGreaterOrEqual 53
    }

    It 'ValidatePattern nazw VM = wzorzec ze źródła (6 miejsc), przyjmuje cztery nazwy i odrzuca stare' {
        $Pattern = Get-DsoVmNamePattern
        $Found = 0
        foreach ($Path in $script:CodeFiles) {
            foreach ($Attribute in (Get-AttributeAsts -Path $Path -TypeName 'ValidatePattern')) {
                $Value = [string]$Attribute.PositionalArguments[0].Value
                if ($Value -notlike '^DSO-(*' -and $Value -notlike '^DSO-26H2-*') { continue }   # wzorce nazw VM; pomija np. nazwę zadania '^DSO-E5-'
                $Found++
                $Value | Should -BeExactly $Pattern -Because "$Path :: $($Attribute.Extent.StartLineNumber)"
            }
        }
        $Found | Should -Be 6
        foreach ($Name in (Get-DsoVmDefinitions).Name) { $Name -cmatch $Pattern | Should -BeTrue -Because $Name }
        foreach ($Old in 'DSO-26H2-Pro', 'DSO-26H2-Home', 'DSO-EN-Brak', 'DSO-DE-Pro', 'DSO-EN-Pro-x') { $Old -cmatch $Pattern | Should -BeFalse -Because $Old }
    }

    It 'każdy bezpiecznik w VM zna dokładnie cztery nazwy komputerów (17 list)' {
        $Expected = @((Get-DsoVmDefinitions).ComputerName | Sort-Object)
        $Lists = 0
        foreach ($Path in $script:CodeFiles) {
            $Text = [System.IO.File]::ReadAllText($Path, [System.Text.Encoding]::UTF8)
            foreach ($Match in [regex]::Matches($Text, "@\(\s*'DSO-[A-Z]{2}-[A-Z]+'(\s*,\s*'DSO-[A-Z]{2}-[A-Z]+')*\s*\)")) {
                $Lists++
                $Names = @([regex]::Matches($Match.Value, "'(DSO-[A-Z]{2}-[A-Z]+)'") | ForEach-Object { $_.Groups[1].Value } | Sort-Object)
                $Names | Should -Be $Expected -Because "$Path :: $($Match.Value)"
            }
        }
        $Lists | Should -Be 17
    }

    It 'domyślne -VmName skryptów = role ze źródła' {
        $Explore = Join-Path $script:VmRoot 'explore'
        Get-ScriptDefault -Path (Join-Path $script:VmRoot 'Invoke-VmTests.ps1') | Should -Be @(Get-DsoVmNames -FullRun)
        Get-ScriptDefault -Path (Join-Path $script:VmRoot 'Invoke-GuiSmoke.ps1') | Should -Be @('DSO-EN-Pro')
        Get-ScriptDefault -Path (Join-Path $Explore 'Invoke-NetExploration.ps1') | Should -Be @('DSO-EN-Pro')
        Get-ScriptDefault -Path (Join-Path $Explore 'Invoke-InstallerExploration.ps1') | Should -Be @('DSO-EN-Pro')
        Get-ScriptDefault -Path (Join-Path $Explore 'Invoke-E4CExploration.ps1') | Should -Be @('DSO-EN-Pro', 'DSO-EN-Home')
        Get-ScriptDefault -Path (Join-Path $Explore 'Invoke-E5Exploration.ps1') | Should -Be @('DSO-EN-Pro', 'DSO-EN-Home')
        foreach ($Name in $script:HistoricNames) {
            Get-ScriptDefault -Path (Join-Path $Explore $Name) | Should -Be @('DSO-PL-Home', 'DSO-PL-Pro') -Because $Name
        }
    }

    It 'edycja nie pochodzi z porównania nazwy' {
        @(Select-String -Path $script:CodeFiles -Pattern "-(c?eq|c?ne|like) 'DSO-").Count | Should -Be 0
    }
}

Describe 'E4C: edycja VM z definicji, nie z nazwy' {
    BeforeAll {
        . (Join-Path $PSScriptRoot '..\explore\Invoke-E4CExploration.ps1')
    }

    It 'Home dostaje plan Home (Windows PL i EN), Pro pełną listę' {
        $Plan = Get-E4CPlan
        $Home1 = @($Plan.HomeInstall | ForEach-Object { [string]$_ })
        @(Get-E4CInstallIds -VmName 'DSO-PL-Home' -Plan $Plan) | Should -Be $Home1
        @(Get-E4CInstallIds -VmName 'DSO-EN-Home' -Plan $Plan) | Should -Be $Home1
        foreach ($Name in 'DSO-EN-Pro', 'DSO-PL-Pro') {
            @(Get-E4CInstallIds -VmName $Name -Plan $Plan).Count | Should -Be @($Plan.Apps).Count -Because $Name
        }
    }

    It 'faza adminprotection tylko dla Pro' {
        $Phases = @('network', 'adminprotection', 'final')
        foreach ($Name in 'DSO-EN-Pro', 'DSO-PL-Pro') { @(Get-E4CPhaseOrder -Phase $Phases -VmName $Name) | Should -Contain 'adminprotection' -Because $Name }
        foreach ($Name in 'DSO-EN-Home', 'DSO-PL-Home') { @(Get-E4CPhaseOrder -Phase $Phases -VmName $Name) | Should -Not -Contain 'adminprotection' -Because $Name }
    }
}

Describe 'Invoke-VmTests.ps1: nazwa kanoniczna VM' {
    It 'pętla po -VmName zamienia nazwę na kanoniczną z definicji, zanim cokolwiek jej użyje' {
        # Skrypt kończy się exit i dotyka Hyper-V, więc sprawdzamy kod: pierwsza instrukcja pętli to $Name = (Get-DsoVmDefinition -VmName $Name).Name.
        $Ast = [System.Management.Automation.Language.Parser]::ParseFile((Join-Path $script:VmRoot 'Invoke-VmTests.ps1'), [ref]$null, [ref]$null)
        $Loop = $Ast.Find({ param($Node) $Node -is [System.Management.Automation.Language.ForEachStatementAst] -and $Node.Variable.VariablePath.UserPath -eq 'Name' }, $true)
        $Loop | Should -Not -BeNullOrEmpty
        $First = $Loop.Body.Statements[0]
        $First | Should -BeOfType ([System.Management.Automation.Language.AssignmentStatementAst])
        $First.Left.VariablePath.UserPath | Should -Be 'Name'
        $First.Right.Extent.Text | Should -Be '(Get-DsoVmDefinition -VmName $Name).Name'
    }
    It 'Get-DsoVmDefinition zamienia dso-en-pro na DSO-EN-Pro' {
        (Get-DsoVmDefinition -VmName 'dso-en-pro').Name | Should -Be 'DSO-EN-Pro'
    }
}

AfterAll { Clear-NoLiveVm }