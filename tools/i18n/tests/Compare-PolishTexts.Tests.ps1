#Requires -Version 5.1
<#
    Testy tools\i18n\Compare-PolishTexts.ps1 (Pester 5+, Windows PowerShell 5.1).

    Instrukcja uruchomienia (z katalogu repo):
        Invoke-Pester -Path tools/i18n/tests

    Opis działania:
        Każdy test zakłada w TestDrive repo git (git init) z dwoma commitami: pierwszy oznaczony tagiem „base”, drugi
        z tekstami po zmianie. Skrypt startuje w osobnym procesie powershell.exe z -Base base, wyjście czytane jako UTF-8.
#>

BeforeAll {
    $script:ScriptPath = (Resolve-Path -Path (Join-Path -Path $PSScriptRoot -ChildPath '..\Compare-PolishTexts.ps1')).Path
    $script:Utf8NoBom = New-Object -TypeName System.Text.UTF8Encoding -ArgumentList $false

    function script:Invoke-Git {
        param([string]$Root, [string[]]$Arguments)
        $Output = & git -C $Root -c user.name=Test -c user.email=test@example.invalid -c core.autocrlf=false @Arguments 2>&1
        if ($LASTEXITCODE -ne 0) { throw ('git {0}: {1}' -f ($Arguments -join ' '), ($Output -join ' ')) }
    }

    function script:Write-Files {
        param([string]$Root, [hashtable]$Files)
        foreach ($Name in $Files.Keys) {
            $Path = Join-Path -Path $Root -ChildPath $Name
            $null = New-Item -ItemType Directory -Path (Split-Path -Path $Path -Parent) -Force
            [System.IO.File]::WriteAllText($Path, $Files[$Name], $script:Utf8NoBom)
        }
    }

    function script:New-TestRepo {
        param([hashtable]$Before, [hashtable]$After)

        $Root = Join-Path -Path $TestDrive -ChildPath ([guid]::NewGuid().ToString('N'))
        $null = New-Item -ItemType Directory -Path $Root
        Invoke-Git -Root $Root -Arguments @('init', '-q')
        Write-Files -Root $Root -Files $Before
        Invoke-Git -Root $Root -Arguments @('add', '-A')
        Invoke-Git -Root $Root -Arguments @('commit', '-q', '-m', 'przed')
        Invoke-Git -Root $Root -Arguments @('tag', 'base')
        Write-Files -Root $Root -Files $After
        Invoke-Git -Root $Root -Arguments @('add', '-A')
        Invoke-Git -Root $Root -Arguments @('commit', '-q', '-m', 'po')
        return $Root
    }

    function script:Invoke-Compare {
        param([string]$Root, [string]$Base = 'base')

        $Command = "[Console]::OutputEncoding = [System.Text.Encoding]::UTF8; & '$script:ScriptPath' -Root '$Root' -Base '$Base'; exit `$LASTEXITCODE"
        $Previous = [Console]::OutputEncoding
        [Console]::OutputEncoding = [System.Text.Encoding]::UTF8
        try {
            $Lines = @(& powershell.exe -NoProfile -ExecutionPolicy Bypass -Command $Command)
            $Code = $LASTEXITCODE
        }
        finally {
            [Console]::OutputEncoding = $Previous
        }
        return [pscustomobject]@{ Lines = $Lines; Text = ($Lines -join "`n"); Code = $Code }
    }
}

Describe 'Compare-PolishTexts.ps1' {
    It 'tekst bez zmian nie jest w „Zniknęły”' {
        $Root = New-TestRepo -Before @{ 'src\A.cs' = 'var s = "Usunięto plik";' } -After @{ 'src\A.cs' = "// zmiana`r`nvar s = `"Usunięto plik`";" }
        $Result = Invoke-Compare -Root $Root
        $Result.Code | Should -Be 0
        $Result.Text | Should -Match 'Zniknęły \(0\)'
        $Result.Text | Should -Match 'Nowe \(0\)'
    }

    It 'interpolacja zamieniona na L.F z tym samym tekstem nie jest w „Zniknęły”' {
        $Root = New-TestRepo -Before @{ 'src\A.cs' = 'var s = $"Plik {name} usunięty";' } -After @{ 'src\A.cs' = 'var s = L.F("Plik {0} usunięty", name);' }
        $Result = Invoke-Compare -Root $Root
        $Result.Code | Should -Be 0
        $Result.Text | Should -Match 'Zniknęły \(0\)'
    }

    It 'zmieniony polski tekst jest w „Zniknęły” z miejscem w bazie, a nowy w „Nowe”' {
        $Root = New-TestRepo -Before @{ 'src\A.cs' = "var a = 1;`r`nvar s = `"Usunięto plik`";" } -After @{ 'src\A.cs' = "var a = 1;`r`nvar s = `"Usunięto pliki`";" }
        $Result = Invoke-Compare -Root $Root
        $Result.Code | Should -Be 0
        $Result.Text | Should -Match 'Zniknęły \(1\)'
        $Result.Text | Should -Match '(?m)^  src/A\.cs:2  Usunięto plik$'
        $Result.Text | Should -Match 'Nowe \(1\)'
        $Result.Text | Should -Match '(?m)^  src/A\.cs:2  Usunięto pliki$'
    }

    It 'tekst przeniesiony do innego pliku albo do XAML {l:T} nie znika' {
        $Before = @{
            'src\A.cs'   = 'var s = "Zastosuj"; var t = "Odśwież stan";'
            'src\V.xaml' = '<Grid xmlns:l="x"><Button Content="Szukaj &amp; filtruj" /></Grid>'
        }
        $After = @{
            'src\A.cs'   = 'var x = 1;'
            'src\B.cs'   = 'var s = L.T("Zastosuj");'
            'src\V.xaml' = "<Grid xmlns:l=`"x`"><Button Content=`"{l:T 'Szukaj &amp; filtruj'}`" ToolTip=`"{l:T 'Odśwież stan'}`" /></Grid>"
        }
        $Result = Invoke-Compare -Root (New-TestRepo -Before $Before -After $After)
        $Result.Code | Should -Be 0
        $Result.Text | Should -Match 'Zniknęły \(0\)'
    }

    It 'wielowierszowy literał z LF w bazie i CRLF w drzewie to ten sam tekst' {
        $Root = New-TestRepo -Before @{ 'src\A.cs' = "var s = @`"Ścieżka`nmówi`";`n" } -After @{ 'src\A.cs' = "var s = @`"Ścieżka`r`nmówi`";`r`n" }
        $Result = Invoke-Compare -Root $Root
        $Result.Text | Should -Match 'Zniknęły \(0\)'
        $Result.Text | Should -Match 'Nowe \(0\)'
    }

    It 'pomija komentarze i teksty bez polskich znaków i słów' {
        $Root = New-TestRepo -Before @{ 'src\A.cs' = "// Usunięto`r`nvar s = `"Applied`"; /* Zamknij */" } -After @{ 'src\A.cs' = 'var x = 1;' }
        $Result = Invoke-Compare -Root $Root
        $Result.Text | Should -Match 'Zniknęły \(0\)'
    }

    It 'nieznana baza daje kod 2' {
        $Root = New-TestRepo -Before @{ 'src\A.cs' = 'var s = "Tak";' } -After @{ 'src\A.cs' = 'var s = L.T("Tak");' }
        (Invoke-Compare -Root $Root -Base 'nie-ma-takiej-galezi').Code | Should -Be 2
    }
}
