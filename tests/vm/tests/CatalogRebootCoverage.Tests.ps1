BeforeDiscovery {
    $RepoRoot = Split-Path -Parent (Split-Path -Parent (Split-Path -Parent $PSScriptRoot))
    $script:RebootItems = @(Get-ChildItem -LiteralPath (Join-Path $RepoRoot 'src\DSOptimizer.Catalog\catalog') -Filter '*.json' -File |
            ForEach-Object { Get-Content -LiteralPath $_.FullName -Raw -Encoding UTF8 | ConvertFrom-Json } |
            ForEach-Object { $_ } |
            Where-Object { $_.kind -eq 'tweak' -and $_.requiresReboot -eq $true } |
            ForEach-Object { @{ TweakId = [string]$_.id } })
}

BeforeAll {
    # Wyłącznik: żaden test na mockach nie może dotknąć prawdziwego Hyper-V (patrz Support\NoLiveVm.ps1).
    . (Join-Path $PSScriptRoot 'Support\NoLiveVm.ps1')

    $RepoRoot = Split-Path -Parent (Split-Path -Parent (Split-Path -Parent $PSScriptRoot))
    $script:Cases = @((Import-PowerShellDataFile -LiteralPath (Join-Path $RepoRoot 'tests\vm\cases.psd1')).Cases)

    function Test-RebootCovered {
        # Pokrycie daje wyłącznie przypadek Flow='Reboot' (Catalog='BuiltIn') dla tego TweakId: aktywny albo ze Skip z niepustym powodem.
        # Skip na innym Flow (np. DetectOnly) się nie liczy - restart nadal nie byłby testowany.
        param([string]$TweakId, [object[]]$Cases)
        $Covered = @($Cases | Where-Object {
                $_.TweakId -ceq $TweakId -and $_.Catalog -ceq 'BuiltIn' -and $_.Flow -ceq 'Reboot' -and
                (-not $_.ContainsKey('Skip') -or -not [string]::IsNullOrWhiteSpace([string]$_.Skip))
            })
        return ($Covered.Count -gt 0)
    }
}

Describe 'Katalog: każdy tweak z requiresReboot=true ma przypadek Flow=Reboot (aktywny albo ze Skip z powodem)' {
    It 'katalog zawiera przynajmniej jeden tweak z requiresReboot (strażnik nie jest pusty): <Count>' -ForEach @(@{ Count = $script:RebootItems.Count }) {
        $Count | Should -BeGreaterThan 0
    }
    It '<TweakId>' -ForEach $script:RebootItems {
        Test-RebootCovered -TweakId $TweakId -Cases $script:Cases | Should -BeTrue -Because "tweak '$TweakId' ma requiresReboot=true, więc cases.psd1 musi mieć dla niego przypadek Flow='Reboot' (Catalog='BuiltIn'), aktywny albo ze Skip z powodem; inny Flow ani Skip na innym Flow się nie liczy"
    }
}

Describe 'Strażnik restartów: dane syntetyczne' {
    It '<Opis>: pokrycie = <Oczekiwane>' -ForEach @(
        @{ Opis = 'tylko Full'; Oczekiwane = $false; Cases = @(@{ TweakId = 'a.b'; Catalog = 'BuiltIn'; Flow = 'Full' }) }
        @{ Opis = 'Full + DetectOnly ze Skip'; Oczekiwane = $false; Cases = @(@{ TweakId = 'a.b'; Catalog = 'BuiltIn'; Flow = 'Full' }, @{ TweakId = 'a.b'; Catalog = 'BuiltIn'; Flow = 'DetectOnly'; Skip = 'powód' }) }
        @{ Opis = 'Reboot ze Skip'; Oczekiwane = $true; Cases = @(@{ TweakId = 'a.b'; Catalog = 'BuiltIn'; Flow = 'Reboot'; Skip = 'powód' }) }
        @{ Opis = 'Reboot aktywny'; Oczekiwane = $true; Cases = @(@{ TweakId = 'a.b'; Catalog = 'BuiltIn'; Flow = 'Reboot' }) }
        @{ Opis = 'Reboot dla innego TweakId'; Oczekiwane = $false; Cases = @(@{ TweakId = 'x.y'; Catalog = 'BuiltIn'; Flow = 'Reboot' }) }
    ) {
        Test-RebootCovered -TweakId 'a.b' -Cases $Cases | Should -Be $Oczekiwane
    }
}

AfterAll { Clear-NoLiveVm }
