#Requires -Version 5.1
<#
    Testy tools\i18n\Get-MissingTranslations.ps1 (Pester 5+, Windows PowerShell 5.1).

    Instrukcja uruchomienia (z katalogu repo):
        Invoke-Pester -Path tools/i18n/tests

    Opis działania:
        Każdy test buduje w TestDrive małe drzewo (src\*.cs, src\*.xaml, plik tłumaczeń) i uruchamia skrypt w osobnym
        procesie powershell.exe (kod wyjścia z $LASTEXITCODE). Dziecko ustawia wyjście konsoli na UTF-8, a rodzic czyta
        je tak samo, więc polskie znaki nie zależą od strony kodowej.
#>

BeforeAll {
    $script:ScriptPath = (Resolve-Path -Path (Join-Path -Path $PSScriptRoot -ChildPath '..\Get-MissingTranslations.ps1')).Path
    $script:Utf8NoBom = New-Object -TypeName System.Text.UTF8Encoding -ArgumentList $false

    function script:New-TestTree {
        param(
            [hashtable]$Files = @{},
            [string]$Translations = '{"language":"en","entries":[]}'
        )

        $Root = Join-Path -Path $TestDrive -ChildPath ([guid]::NewGuid().ToString('N'))
        $TranslationDirectory = Join-Path -Path $Root -ChildPath 'src\DSOptimizer.Core\Localization\Translations'
        $null = New-Item -ItemType Directory -Path $TranslationDirectory -Force
        [System.IO.File]::WriteAllText((Join-Path -Path $TranslationDirectory -ChildPath 'x.json'), $Translations, $script:Utf8NoBom)
        [System.IO.File]::WriteAllText((Join-Path -Path $Root -ChildPath 'src\Empty.cs'), 'class Empty { }', $script:Utf8NoBom)
        foreach ($Name in $Files.Keys) {
            $Path = Join-Path -Path $Root -ChildPath $Name
            $null = New-Item -ItemType Directory -Path (Split-Path -Path $Path -Parent) -Force
            [System.IO.File]::WriteAllText($Path, $Files[$Name], $script:Utf8NoBom)
        }
        return $Root
    }

    function script:Invoke-Report {
        param(
            [string]$Root,
            [switch]$FailOnMissing,
            [string]$ScriptPath = $script:ScriptPath,
            [string]$WorkDirectory = $TestDrive
        )

        $Arguments = ''
        if ($Root) { $Arguments += " -Root '$Root'" }
        if ($FailOnMissing) { $Arguments += ' -FailOnMissing' }
        $Command = "[Console]::OutputEncoding = [System.Text.Encoding]::UTF8; Set-Location -LiteralPath '$WorkDirectory'; & '$ScriptPath'$Arguments; exit `$LASTEXITCODE"
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
Describe 'Get-MissingTranslations.ps1' {
    It 'tekst z tłumaczeniem nie jest brakiem' {
        $Root = New-TestTree -Files @{ 'src\A.cs' = 'var s = L.T("Zapisz");' } -Translations '{"language":"en","entries":[{"pl":"Zapisz","en":"Save"}]}'
        $Result = Invoke-Report -Root $Root
        $Result.Code | Should -Be 0
        $Result.Text | Should -Match 'Braki: 0, nieużywane: 0'
    }

    It 'brakujący tekst jest raportowany z plikiem i linią' {
        $Root = New-TestTree -Files @{ 'src\A.cs' = "// start`r`nvar s = L.T(`"Zapisz`");`r`n" }
        $Result = Invoke-Report -Root $Root
        $Result.Code | Should -Be 0
        $Result.Text | Should -Match 'Brak tłumaczenia \(1\)'
        $Result.Text | Should -Match 'src/A\.cs:2  Zapisz'
        $Result.Text | Should -Match 'Braki: 1, nieużywane: 0'
    }

    It 'L.F daje klucz z symbolem zastępczym' {
        $Root = New-TestTree -Files @{ 'src\A.cs' = 'var s = L.F("Plik {0}", p);' }
        $Result = Invoke-Report -Root $Root
        $Result.Text | Should -Match 'src/A\.cs:1  Plik \{0\}'
    }

    It 'rozkodowuje sekwencje ucieczki C#' {
        $Root = New-TestTree -Files @{ 'src\A.cs' = 'var s = L.T("a \"b\"");' }
        $Result = Invoke-Report -Root $Root
        $Result.Text | Should -Match 'A\.cs:1  a "b"'
    }

    It 'rozkodowuje \uXXXX, \t, \0, \'' i \\ w literale C#' {
        $Root = New-TestTree -Files @{ 'src\A.cs' = 'var s = L.T("\u0141\t\0\'' \\ \a\b\f\v");' }
        $Result = Invoke-Report -Root $Root
        $Result.Text | Should -Match ([regex]::Escape('A.cs:1  Ł\t' + [char]0 + "' \ " + [char]7 + [char]8 + [char]12 + [char]11))
    }

    It 'klucze są porównywane z uwzględnieniem wielkości liter' {
        $Root = New-TestTree -Files @{ 'src\A.cs' = 'var a = L.T("Wyślij"); var b = L.T("stary");' } -Translations '{"language":"en","entries":[{"pl":"wyślij","en":"send"},{"pl":"Stary","en":"Old"},{"pl":"Stary","en":"Old2"},{"pl":"STARY","en":"OLD"}]}'
        $Result = Invoke-Report -Root $Root
        $Result.Text | Should -Match 'Braki: 2, nieużywane: 3'
        $Result.Text | Should -Match 'A\.cs:1  Wyślij'
        $Result.Text | Should -Match 'A\.cs:1  stary'
        $Result.Text | Should -Match '(?m)^  STARY$'
    }

    It 'ignoruje wywołania w komentarzach C# i zachowuje numery linii' {
        $Source = "/// <summary>np. L.T(`"Z komentarza`")</summary>`n// L.T(`"Liniowy`")`n/* L.T(`"Blokowy`")`n   L.T(`"Blokowy2`") */`nvar s = L.T(`"Prawdziwy`"); var u = `"http://x`"; var c = '`"'; var t = L.T(`"Po url`");"
        $Root = New-TestTree -Files @{ 'src\A.cs' = $Source }
        $Result = Invoke-Report -Root $Root
        $Result.Text | Should -Match 'Braki: 2, nieużywane: 0'
        $Result.Text | Should -Match 'A\.cs:5  Prawdziwy'
        $Result.Text | Should -Match 'A\.cs:5  Po url'
        $Result.Text | Should -Not -Match 'komentarza|Liniowy|Blokowy'
    }

    It 'ignoruje {l:T} w komentarzach XAML' {
        $Root = New-TestTree -Files @{ 'src\V.xaml' = "<!-- {l:T 'Zakomentowany'}`n -->`n<Button Content=`"{l:T 'Żywy'}`" />" }
        $Result = Invoke-Report -Root $Root
        $Result.Text | Should -Match 'Braki: 1, nieużywane: 0'
        $Result.Text | Should -Match 'V\.xaml:3  Żywy'
    }

    It 'czyta plik źródłowy zapisany z BOM' {
        $Root = New-TestTree
        $Path = Join-Path -Path $Root -ChildPath 'src\Bom.cs'
        [System.IO.File]::WriteAllText($Path, 'var s = L.T("Z BOM");', (New-Object -TypeName System.Text.UTF8Encoding -ArgumentList $true))
        (Invoke-Report -Root $Root).Text | Should -Match 'Bom\.cs:1  Z BOM'
    }

    It 'nieobsługiwane sekwencje \x i \U dają kod 2 z miejscem' {
        $Root = New-TestTree -Files @{ 'src\A.cs' = "var a = 1;`nvar s = L.T(`"\U0001F600`");" }
        $Result = Invoke-Report -Root $Root
        $Result.Code | Should -Be 2
        $Result.Text | Should -Match 'src/A\.cs:2'
        $Root2 = New-TestTree -Files @{ 'src\A.cs' = 'var s = L.T("a \x41");' }
        (Invoke-Report -Root $Root2).Code | Should -Be 2
    }

    It 'zły -Root i korzeń bez src dają kod 2, także z -FailOnMissing' {
        $Result = Invoke-Report -Root (Join-Path -Path $TestDrive -ChildPath 'nie-ma-takiego')
        $Result.Code | Should -Be 2
        $Empty = Join-Path -Path $TestDrive -ChildPath ([guid]::NewGuid().ToString('N'))
        $null = New-Item -ItemType Directory -Path $Empty
        (Invoke-Report -Root $Empty -FailOnMissing).Code | Should -Be 2
    }

    It 'bez -Root szuka repo obok skryptu, niezależnie od katalogu roboczego' {
        $Root = New-TestTree -Files @{ 'src\A.cs' = 'var s = L.T("Zapisz");' } -Translations '{"language":"en","entries":[{"pl":"Zapisz","en":"Save"}]}'
        $null = New-Item -ItemType Directory -Path (Join-Path -Path $Root -ChildPath 'tools\i18n') -Force
        $Copy = Join-Path -Path $Root -ChildPath 'tools\i18n\Get-MissingTranslations.ps1'
        Copy-Item -LiteralPath $script:ScriptPath -Destination $Copy
        $Other = Join-Path -Path $TestDrive -ChildPath ([guid]::NewGuid().ToString('N'))
        $null = New-Item -ItemType Directory -Path $Other
        $Result = Invoke-Report -ScriptPath $Copy -WorkDirectory $Other
        $Result.Code | Should -Be 0
        $Result.Text | Should -Match 'Braki: 0, nieużywane: 0'
    }
    It 'rozkodowuje encje i apostrof w XAML' {
        $Root = New-TestTree -Files @{ 'src\V.xaml' = "<Button Content=`"{l:T 'Zapisz &amp; zamknij'}`" />`r`n<TextBlock Text=`"{l:T 'Nie \'tak\''}`" />" }
        $Result = Invoke-Report -Root $Root
        $Result.Text | Should -Match 'src/V\.xaml:1  Zapisz & zamknij'
        $Result.Text | Should -Match "src/V\.xaml:2  Nie 'tak'"
    }

    It 'rozkodowuje &lt;, &#N; i \\ w XAML' {
        $Root = New-TestTree -Files @{ 'src\V.xaml' = "<Button Content=`"{l:T 'a &lt;b&gt; &#65;&#x42; \\ c'}`" />" }
        $Result = Invoke-Report -Root $Root
        $Result.Text | Should -Match ([regex]::Escape('V.xaml:1  a <b> AB \ c'))
    }

    It 'wpis bez źródła jest nieużywany' {
        $Root = New-TestTree -Files @{ 'src\A.cs' = 'var s = L.T("Zapisz");' } -Translations '{"language":"en","entries":[{"pl":"Zapisz","en":"Save"},{"pl":"Stary","en":"Old"}]}'
        $Result = Invoke-Report -Root $Root
        $Result.Text | Should -Match 'Nieużywane tłumaczenia \(1\)'
        $Result.Text | Should -Match '(?m)^  Stary$'
        $Result.Text | Should -Match 'Braki: 0, nieużywane: 1'
    }

    It 'ten sam brak w dwóch plikach jest liczony raz, z pierwszym miejscem' {
        $Root = New-TestTree -Files @{
            'src\B.cs'   = 'var s = L.T("Wspólny");'
            'src\A.cs'   = 'var s = L.T("Wspólny");'
            'src\V.xaml' = "{l:T 'Wspólny'}"
        }
        $Result = Invoke-Report -Root $Root
        $Result.Text | Should -Match 'Braki: 1, nieużywane: 0'
        $Result.Text | Should -Match 'src/A\.cs:1  Wspólny'
        $Result.Text | Should -Not -Match 'B\.cs'
    }

    It 'pomija obj, bin i definicję L.cs' {
        $Root = New-TestTree -Files @{
            'src\P\obj\G.cs'                              = 'var s = L.T("Obj");'
            'src\P\bin\G.cs'                              = 'var s = L.T("Bin");'
            'src\DSOptimizer.Core\Localization\L.cs'      = 'var s = L.T("Definicja");'
            'src\DSOptimizer.App\Gui\TExtension.cs'       = 'return L.T(Text);'
            'src\P\X.cs'                                  = 'var s = xL.T("Obce"); var t = a.L.T("Obce2");'
        }
        $Result = Invoke-Report -Root $Root
        $Result.Text | Should -Match 'Braki: 0, nieużywane: 0'
    }

    It '-FailOnMissing daje kod 1 przy braku, a 0 bez braków' {
        $Missing = New-TestTree -Files @{ 'src\A.cs' = 'var s = L.T("Zapisz");' }
        (Invoke-Report -Root $Missing -FailOnMissing).Code | Should -Be 1
        (Invoke-Report -Root $Missing).Code | Should -Be 0

        $Clean = New-TestTree -Files @{ 'src\A.cs' = 'var s = L.T("Zapisz");' } -Translations '{"language":"en","entries":[{"pl":"Zapisz","en":"Save"}]}'
        (Invoke-Report -Root $Clean -FailOnMissing).Code | Should -Be 0
    }

    It 'nieprawidłowy JSON tłumaczeń daje kod 2' {
        $Root = New-TestTree -Files @{ 'src\A.cs' = 'var s = L.T("Zapisz");' } -Translations '{ to nie jest json'
        $Result = Invoke-Report -Root $Root
        $Result.Code | Should -Be 2
    }
}
