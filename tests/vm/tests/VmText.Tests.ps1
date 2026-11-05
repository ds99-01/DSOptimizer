BeforeAll {
    # Wyłącznik: żaden test na mockach nie może dotknąć prawdziwego Hyper-V (patrz Support\NoLiveVm.ps1).
    . (Join-Path $PSScriptRoot 'Support\NoLiveVm.ps1')

    Import-Module (Join-Path $PSScriptRoot '..\modules\VmText.psm1') -Force
    # Udawane repo: tylko folder tabel tłumaczeń.
    function script:New-FakeRepo([string]$Name, [object[]]$Tables) {
        $Root = Join-Path $TestDrive $Name
        $Dir = Join-Path $Root 'src\DSOptimizer.Core\Localization\Translations'
        $null = New-Item -ItemType Directory -Path $Dir -Force
        $I = 0
        foreach ($Entries in $Tables) {
            $I++
            $Json = ConvertTo-Json -InputObject ([ordered]@{ language = 'en'; entries = @($Entries) }) -Depth 5
            [System.IO.File]::WriteAllText((Join-Path $Dir "t$I.json"), $Json, (New-Object System.Text.UTF8Encoding($true)))
        }
        return $Root
    }
    $script:R = New-FakeRepo 'repo-main' @(, @(
            [ordered]@{ pl = 'Tak'; en = 'Yes' },
            [ordered]@{ pl = 'tak'; en = 'yes' },
            [ordered]@{ pl = 'Plik [a] {0}?'; en = 'File [a] {0} isn''t *ready*?' },
            [ordered]@{ pl = 'Usługa {0} ma {1} zależnych.'; en = 'Service {0} has {1} dependents.' }))
}

Describe 'Get-DsoUiText (tekst okna w języku sondy)' {
    It 'pl zwraca polski tekst bez zmian (także bez wpisu w tabeli)' {
        Get-DsoUiText -Polish 'Coś, czego nie ma w żadnej tabeli' -Language pl | Should -Be 'Coś, czego nie ma w żadnej tabeli'
    }
    It 'en bierze wpis z tabel tłumaczeń repo' {
        Get-DsoUiText -Polish 'Historia zmian' -Language en | Should -Be 'Change history'
        Get-DsoUiText -Polish 'Zastosuj ({0})' -Language en | Should -Be 'Apply ({0})'
    }
    It 'en bez wpisu to wyjątek „brak tłumaczenia”, nigdy polski tekst' {
        { Get-DsoUiText -Polish 'Coś, czego nie ma w żadnej tabeli' -Language en } | Should -Throw '*brak tłumaczenia*Coś, czego nie ma*'
    }
    It 'czyta wszystkie pliki tabel podanego repo, z rozróżnieniem wielkości liter' {
        $Root = New-FakeRepo 'repo-a' @(@([ordered]@{ pl = 'Tak'; en = 'Yes' }), @([ordered]@{ pl = 'tak'; en = 'yes' }))
        Get-DsoUiText -Polish 'Tak' -Language en -Root $Root | Should -Be 'Yes'
        Get-DsoUiText -Polish 'tak' -Language en -Root $Root | Should -Be 'yes'
        { Get-DsoUiText -Polish 'TAK' -Language en -Root $Root } | Should -Throw '*brak tłumaczenia*'
    }
    It 'dwa różne tłumaczenia tego samego polskiego tekstu to błąd' {
        $Root = New-FakeRepo 'repo-b' @(@([ordered]@{ pl = 'Tak'; en = 'Yes' }), @([ordered]@{ pl = 'Tak'; en = 'Sure' }))
        { Get-DsoUiText -Polish 'Tak' -Language en -Root $Root } | Should -Throw '*Tak*'
    }
    It 'pusty angielski wpis to brak tłumaczenia' {
        $Root = New-FakeRepo 'repo-c' @(, @([ordered]@{ pl = 'Tak'; en = '' }))
        { Get-DsoUiText -Polish 'Tak' -Language en -Root $Root } | Should -Throw '*brak tłumaczenia*'
    }
}

Describe 'Test-DsoTextKey i ConvertTo-DsoTextPattern' {
    It 'Test-DsoTextKey: pełny klucz tak, fragment nie' {
        Test-DsoTextKey -Polish 'Tak' -Root $R | Should -BeTrue
        Test-DsoTextKey -Polish 'Ta' -Root $R | Should -BeFalse
    }
    It 'Test-DsoTextKey: wielkość liter ma znaczenie' {
        Test-DsoTextKey -Polish 'TAK' -Root $R | Should -BeFalse
    }
    It 'ConvertTo-DsoTextPattern pl: {n} jako *' {
        ConvertTo-DsoTextPattern -Polish 'Usługa {0} ma {1} zależnych.' -Language pl -Root $R | Should -BeExactly '*Usługa * ma * zależnych.*'
    }
    It 'ConvertTo-DsoTextPattern en: {n} jako *' {
        ConvertTo-DsoTextPattern -Polish 'Usługa {0} ma {1} zależnych.' -Language en -Root $R | Should -BeExactly '*Service * has * dependents.*'
    }
    It 'ConvertTo-DsoTextPattern en: znaki wieloznaczne i nawiasy z tekstu są dosłowne' {
        $P = ConvertTo-DsoTextPattern -Polish 'Plik [a] {0}?' -Language en -Root $R
        ('Błąd: File [a] C:\x isn''t *ready*? koniec' -like $P) | Should -BeTrue
        ('File a C:\x isn''t XreadyX!' -like $P) | Should -BeFalse
    }
    It 'ConvertTo-DsoTextPattern: klucz spoza tabel = wyjątek także dla pl' {
        { ConvertTo-DsoTextPattern -Polish 'Coś, czego nie ma' -Language pl -Root $R } | Should -Throw '*brak tłumaczenia*'
        { ConvertTo-DsoTextPattern -Polish 'Coś, czego nie ma' -Language en -Root $R } | Should -Throw '*brak tłumaczenia*'
    }
    It 'prawdziwe tabele repo' {
        ConvertTo-DsoTextPattern -Polish 'Ochrona systemu na dysku C: jest wyłączona – włącz ją pozycją „Ochrona systemu” w tej kategorii.' -Language en | Should -BeExactly '*System Protection is turned off for the C: drive – turn it on with the "System Protection" item in this category.*'
    }
}

Describe 'VmSmokeUi a VmText' {
    It 'VmSmokeUi nie eksportuje już Get-DsoUiText' {
        Import-Module (Join-Path $PSScriptRoot '..\modules\VmSmokeUi.psm1') -Force
        (Get-Command -Module VmSmokeUi).Name | Should -Not -Contain 'Get-DsoUiText'
    }
}

AfterAll { Clear-NoLiveVm }
