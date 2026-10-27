BeforeAll {
    # Wyłącznik: żaden test na mockach nie może dotknąć prawdziwego Hyper-V (patrz Support\NoLiveVm.ps1).
    . (Join-Path $PSScriptRoot 'Support\NoLiveVm.ps1')
    Import-Module (Join-Path $PSScriptRoot '..\modules\VmCommon.psm1') -Force }

Describe 'New-DsoVmPassword' {
    It 'ma żądaną długość i wszystkie klasy znaków' {
        $Password = New-DsoVmPassword -Length 24
        $Password.Length | Should -Be 24
        $Password | Should -MatchExactly '[A-Z]'
        $Password | Should -MatchExactly '[a-z]'
        $Password | Should -Match '[0-9]'
        $Password | Should -Match '[-_.!#+=]'
    }
    It 'nie zawiera znaków psujących XML i skrypty' { New-DsoVmPassword | Should -Not -Match "['""<>&% ]" }
    It 'daje różne hasła' { New-DsoVmPassword | Should -Not -Be (New-DsoVmPassword) }
}

Describe 'Poświadczenia VM' {
    BeforeEach { $script:OldLocal = $env:LOCALAPPDATA; $env:LOCALAPPDATA = $TestDrive }
    AfterEach { $env:LOCALAPPDATA = $script:OldLocal }

    It 'ścieżka leży w LOCALAPPDATA\DSOptimizer-vm i poza repo' {
        $Path = Get-DsoVmCredentialPath -VmName 'DSO-26H2-Pro'
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
