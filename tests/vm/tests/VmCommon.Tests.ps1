BeforeAll {
    # Wyłącznik: żaden test na mockach nie może dotknąć prawdziwego Hyper-V (patrz Support\NoLiveVm.ps1).
    . (Join-Path $PSScriptRoot 'Support\NoLiveVm.ps1')
    Import-Module (Join-Path $PSScriptRoot '..\modules\VmCommon.psm1') -Force }

Describe 'Generator haseł' {
    It 'generatora haseł już nie ma' { Get-Command New-DsoVmPassword -ErrorAction SilentlyContinue | Should -BeNullOrEmpty }
}

Describe 'Poświadczenia VM' {
    BeforeEach { $script:OldLocal = $env:LOCALAPPDATA; $env:LOCALAPPDATA = $TestDrive }
    AfterEach { $env:LOCALAPPDATA = $script:OldLocal }

    It 'ścieżka leży w LOCALAPPDATA\DSOptimizer-vm i poza repo' {
        $Path = Get-DsoVmCredentialPath -VmName 'DSO-EN-Pro'
        $Path | Should -BeLike (Join-Path $TestDrive 'DSOptimizer-vm\*')
        $Path | Should -Not -BeLike ((Get-DsoRepoRoot) + '*')
    }
    It 'zapis i odczyt zwracają konto Administrator z tym samym hasłem' {
        Save-DsoVmCredential -VmName 'X' -Password 'Abc-123-xyz' | Out-Null
        $Credential = Get-DsoVmCredential -VmName 'X'
        $Credential.UserName | Should -Be 'Administrator'
        $Credential.GetNetworkCredential().Password | Should -Be 'Abc-123-xyz'
    }
    It 'brak pliku daje czytelny błąd' { { Get-DsoVmCredential -VmName 'Brak' } | Should -Throw -ExpectedMessage '*uruchom New-TestVm.ps1*' }
    It 'odmawia ścieżki wewnątrz repo' {
        $env:LOCALAPPDATA = Get-DsoRepoRoot
        { Get-DsoVmCredentialPath -VmName 'X' } | Should -Throw -ExpectedMessage '*repo*'
    }
}

Describe 'Get-DsoVmLayout' {
    It 'ścieżki w VM nie mają spacji' { (Get-DsoVmLayout).Values | ForEach-Object { $_ | Should -Not -Match ' ' } }
}

AfterAll { Clear-NoLiveVm }

Describe 'Jedno źródło listy VM' {
    It 'cztery VM w stałej kolejności' { (Get-DsoVmDefinitions).Name | Should -Be @('DSO-EN-Home', 'DSO-EN-Pro', 'DSO-PL-Pro', 'DSO-PL-Home') }
    It '<Name>: edycja, język, region i role' -ForEach @(
        @{ Name = 'DSO-EN-Home'; Edition = 'Home'; EditionId = 'Core'; Language = 'en'; Culture = 'en-US'; InputLocale = '0409:00000409'; FullRun = $true; Smoke = @('en'); Explore = $true }
        @{ Name = 'DSO-EN-Pro'; Edition = 'Pro'; EditionId = 'Professional'; Language = 'en'; Culture = 'en-US'; InputLocale = '0409:00000409'; FullRun = $true; Smoke = @('en', 'pl'); Explore = $true }
        @{ Name = 'DSO-PL-Pro'; Edition = 'Pro'; EditionId = 'Professional'; Language = 'pl'; Culture = 'pl-PL'; InputLocale = '0415:00000415'; FullRun = $true; Smoke = @('pl', 'en'); Explore = $true }
        @{ Name = 'DSO-PL-Home'; Edition = 'Home'; EditionId = 'Core'; Language = 'pl'; Culture = 'pl-PL'; InputLocale = '0415:00000415'; FullRun = $false; Smoke = @('pl'); Explore = $false }
    ) {
        $D = Get-DsoVmDefinition -VmName $Name
        $D.Name | Should -BeExactly $Name
        $D.ComputerName | Should -BeExactly $Name.ToUpperInvariant()
        $D.Edition | Should -BeExactly $Edition
        $D.EditionId | Should -BeExactly $EditionId
        $D.Language | Should -BeExactly $Language
        $D.Culture | Should -BeExactly $Culture
        $D.InputLocale | Should -BeExactly $InputLocale
        $D.FullRun | Should -BeExactly $FullRun
        @($D.SmokeLanguages) | Should -Be $Smoke
        $D.ReleaseExplorations | Should -BeExactly $Explore
    }
    It 'nazwa ma najwyżej 15 znaków i pasuje do wzorca' { foreach ($D in Get-DsoVmDefinitions) { $D.Name.Length | Should -BeLessOrEqual 15; $D.Name | Should -MatchExactly (Get-DsoVmNamePattern) } }
    It 'wzorzec nazw' { Get-DsoVmNamePattern | Should -BeExactly '^DSO-(EN|PL)-(Home|Pro)\z' }
    It 'nazwa w innej wielkości liter daje nazwę kanoniczną' { (Get-DsoVmDefinition -VmName 'dso-en-pro').Name | Should -BeExactly 'DSO-EN-Pro' }
    It 'stara nazwa daje wyjątek z listą znanych VM' { { Get-DsoVmDefinition -VmName 'DSO-26H2-Pro' } | Should -Throw -ExpectedMessage "*'DSO-26H2-Pro'*DSO-EN-Home, DSO-EN-Pro, DSO-PL-Pro, DSO-PL-Home*" }
    It 'Get-DsoVmNames -FullRun' { Get-DsoVmNames -FullRun | Should -Be @('DSO-EN-Home', 'DSO-EN-Pro', 'DSO-PL-Pro') }
    It 'Get-DsoVmNames -Language pl' { Get-DsoVmNames -Language pl | Should -Be @('DSO-PL-Pro', 'DSO-PL-Home') }
    It 'każde wywołanie daje nowe obiekty' { $A = Get-DsoVmDefinitions; $A[0].Language = 'xx'; (Get-DsoVmDefinitions)[0].Language | Should -Be 'en' }
}
