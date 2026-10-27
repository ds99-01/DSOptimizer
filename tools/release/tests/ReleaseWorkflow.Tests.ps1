#Requires -Version 5.1
<#
    Testy tekstowe .github\workflows\release.yml (Pester 5+, Windows PowerShell 5.1).

    Instrukcja uruchomienia (z katalogu repo):
        Invoke-Pester -Path tools/release/tests/ReleaseWorkflow.Tests.ps1

    Opis działania:
        Workflow nie jest uruchamiany – test czyta jego tekst i pilnuje reguł wydania: wydanie zawsze robocze (draft),
        nigdy „najnowsze”, wariant (SingleExe/Folder) tylko z tools\release\variant.txt, testy jednostkowe przed
        Build-Release.ps1, brak --no-restore (restore z /p:DsoSingleExe=true zostawia Fody w obj\project.assets.json)
        i smoke wydanego exe testem Windows (DSO_SINGLE_EXE_PATH); draft tylko przy push tagu (nie przy workflow_dispatch),
        GITHUB_OUTPUT i notatki zapisane bez BOM, nazwa tagu przekazana przez env:.
#>

BeforeAll {
    $script:RepoRoot = (Resolve-Path -Path (Join-Path -Path $PSScriptRoot -ChildPath '..\..\..')).Path
    $script:WorkflowPath = Join-Path -Path $script:RepoRoot -ChildPath '.github\workflows\release.yml'
    $script:VariantPath = Join-Path -Path $script:RepoRoot -ChildPath 'tools\release\variant.txt'
    $script:Workflow = [System.IO.File]::ReadAllText($script:WorkflowPath, [System.Text.Encoding]::UTF8)
}

Describe 'release.yml' {
    It 'wydanie jest zawsze robocze (--draft)' {
        $script:Workflow | Should -Match 'gh release create\b[^\r\n]*--draft'
    }

    It 'nie oznacza wydania jako najnowszego' {
        $script:Workflow | Should -Not -Match '--latest'
        $script:Workflow | Should -Not -Match 'make_latest'
    }

    It 'poziom pliku ma tylko contents: read, a zapis (contents: write) ma wylacznie job release' {
        $script:Workflow | Should -Match '(?m)^permissions:\s*\r?\n\s+contents: read\s*\r?\n'
        $script:Workflow | Should -Not -Match '(?m)^permissions:\s*\r?\n\s+contents: write'
        $script:Workflow | Should -Match '(?s)jobs:\s*\r?\n\s+release:\s*\r?\n(?:\s+[a-z-]+: [^\r\n]*\r?\n)*?\s+permissions:\s*\r?\n\s+contents: write'
        ([regex]::Matches($script:Workflow, 'contents: write')).Count | Should -Be 1
        $script:Workflow | Should -Match 'GITHUB_TOKEN'
    }

    It 'startuje z tagu v* i z workflow_dispatch' {
        $script:Workflow | Should -Match "tags:\s*\[\s*'v\*'\s*\]"
        $script:Workflow | Should -Match 'workflow_dispatch'
    }

    It 'publikuje wydanie tylko przy push tagu (ręczne uruchomienie na tagu = sam artefakt)' {
        $Step = [regex]::Match($script:Workflow, '- name: Wydanie robocze na GitHubie\s*\r?\n\s+if: ([^\r\n]+)')
        $Step.Success | Should -BeTrue
        $Step.Groups[1].Value.Trim() | Should -Be "`${{ github.event_name == 'push' && startsWith(github.ref, 'refs/tags/v') }}"
        ([regex]::Matches($script:Workflow, '(?m)^\s+gh release create')).Count | Should -Be 1
    }

    It 'zapisuje GITHUB_OUTPUT i notatki wydania bez BOM (bez Out-File)' {
        $script:Workflow | Should -Not -Match 'Out-File'
        $script:Workflow | Should -Match '\[IO\.File\]::AppendAllText\(\$env:GITHUB_OUTPUT'
        $script:Workflow | Should -Match '\[IO\.File\]::WriteAllText\(\$Notes'
        $script:Workflow | Should -Match 'New-Object System\.Text\.UTF8Encoding \$false'
    }

    It 'nazwa tagu trafia do skryptów tylko przez env:, nie wklejona w treść' {
        $Lines = @($script:Workflow -split '\r?\n' | Where-Object { $_ -match '\$\{\{\s*github\.ref_name\s*\}\}' })
        $Lines.Count | Should -BeGreaterThan 0
        foreach ($Line in $Lines) {
            $Line | Should -Match '^\s+[A-Z_]+:\s*\$\{\{\s*github\.ref_name\s*\}\}\s*$'
        }
    }

    It 'wariant jest tylko w tools/release/variant.txt i ma poprawną wartość' {
        $Variant = ([System.IO.File]::ReadAllText($script:VariantPath)).Trim()
        $Variant | Should -BeIn @('SingleExe', 'Folder')
        $script:Workflow | Should -Match 'tools[\/]release[\/]variant\.txt'
        $script:Workflow | Should -Not -Match '-Variant\s+(SingleExe|Folder)\b'
    }

    It 'testy jednostkowe idą przed Build-Release.ps1' {
        $Tests = $script:Workflow.IndexOf('DSOptimizer.Tests.Unit')
        $Build = $script:Workflow.IndexOf('-Variant $Variant')
        $Tests | Should -BeGreaterThan -1
        $Build | Should -BeGreaterThan $Tests
    }

    It 'nie używa --no-restore (restore wydania zostawia Fody w obj)' {
        $script:Workflow | Should -Not -Match '--no-restore'
    }

    It 'przekazuje tag do Build-Release.ps1 i używa OutRoot out' {
        $script:Workflow | Should -Match 'Build-Release\.ps1[^\r\n]*-Tag'
        $script:Workflow | Should -Match 'Build-Release\.ps1[^\r\n]*-OutRoot out'
    }

    It 'sprawdza wydany exe testem Windows (DSO_SINGLE_EXE_PATH)' {
        $script:Workflow | Should -Match 'DSO_SINGLE_EXE_PATH'
        $script:Workflow | Should -Match 'SingleExeResourcesTests'
    }
}


Describe 'ci.yml' {
    It 'ma uprawnienia tylko contents: read na poziomie pliku' {
        $ci = [System.IO.File]::ReadAllText((Join-Path -Path $script:RepoRoot -ChildPath '.github\workflows\ci.yml'), [System.Text.Encoding]::UTF8)
        $ci | Should -Match '(?m)^permissions:\s*\r?\n\s+contents: read\s*\r?\n'
        $ci | Should -Not -Match 'write'
    }
}

Describe 'bloki run: w workflow' {
    # PowerShell 5.1 czyta tymczasowy .ps1 kroku bez BOM jako ANSI - znaki spoza ASCII psuja parsowanie.
    It 'skrypty run: w <Name> zawieraja wylacznie ASCII' -ForEach @(
        @{ Name = 'release.yml' }
        @{ Name = 'ci.yml' }
    ) {
        $path = Join-Path -Path $script:RepoRoot -ChildPath ('.github\workflows\' + $Name)
        $lines = [System.IO.File]::ReadAllText($path, [System.Text.Encoding]::UTF8) -split '\r?\n'
        $bad = New-Object System.Collections.Generic.List[string]
        $runIndent = -1
        for ($i = 0; $i -lt $lines.Count; $i++) {
            $line = $lines[$i]
            if ($runIndent -ge 0) {
                $isBlank = $line.Trim().Length -eq 0
                $indent = $line.Length - $line.TrimStart().Length
                if (-not $isBlank -and $indent -le $runIndent) { $runIndent = -1 }
                elseif ($line -match '[^\x00-\x7F]') { $bad.Add(('{0}: {1}' -f ($i + 1), $line.Trim())) }
            }
            if ($runIndent -lt 0 -and $line -match '^(\s*)(-\s+)?run:\s*[|>][-+]?\s*$') {
                $runIndent = $Matches[1].Length + $(if ($Matches[2]) { $Matches[2].Length } else { 0 })
            }
        }
        $bad | Should -BeNullOrEmpty -Because 'run: musi byc czystym ASCII'
    }
}