#Requires -Version 5.1
<#
    Testy tools\i18n\Set-DocsTranslationStamp.ps1 (Pester 5+, Windows PowerShell 5.1).

    Instrukcja uruchomienia (z katalogu repo):
        Invoke-Pester -Path tools/i18n/tests

    Opis działania:
        Każdy test buduje w TestDrive małe repo (polski docs\tweaks\*.md i angielski docs\en\tweaks\*.md) i uruchamia skrypt
        w osobnym procesie powershell.exe (kod wyjścia z $LASTEXITCODE). Oczekiwany hash liczy test sam: SHA-256 bajtów
        UTF-8 polskiego pliku bez BOM, z końcami linii LF.
#>

BeforeAll {
    $script:ScriptPath = [System.IO.Path]::GetFullPath((Join-Path -Path $PSScriptRoot -ChildPath '..\Set-DocsTranslationStamp.ps1'))
    $script:Utf8NoBom = New-Object -TypeName System.Text.UTF8Encoding -ArgumentList $false
    # Złożone, żeby test nie zawierał literalnych odnośników do docs (pilnuje ich RepoDocsLinksTests).
    $script:Docs = 'docs' + '/'

    function script:New-DocsRepo {
        param([hashtable]$Files = @{})

        $Root = Join-Path -Path $TestDrive -ChildPath ([guid]::NewGuid().ToString('N'))
        $null = New-Item -ItemType Directory -Path $Root -Force
        foreach ($Name in $Files.Keys) {
            $Path = Join-Path -Path $Root -ChildPath $Name
            $null = New-Item -ItemType Directory -Path (Split-Path -Path $Path -Parent) -Force
            [System.IO.File]::WriteAllText($Path, $Files[$Name], $script:Utf8NoBom)
        }
        return $Root
    }

    function script:Get-ExpectedHash {
        param([string]$Text)
        $Bytes = $script:Utf8NoBom.GetBytes($Text.Replace("`r`n", "`n"))
        $Sha = [System.Security.Cryptography.SHA256]::Create()
        try { $Hash = $Sha.ComputeHash($Bytes) } finally { $Sha.Dispose() }
        return (($Hash | ForEach-Object { $_.ToString('x2') }) -join '')
    }

    function script:Invoke-Stamp {
        param([string]$Root, [string[]]$Path)

        $Quoted = ($Path | ForEach-Object { "'$_'" }) -join ','
        $Command = "[Console]::OutputEncoding = [System.Text.Encoding]::UTF8; & '$script:ScriptPath' -Root '$Root' -Path $Quoted; exit `$LASTEXITCODE"
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

    function script:Read-Bytes {
        param([string]$Root, [string]$Name)
        return [System.IO.File]::ReadAllBytes((Join-Path -Path $Root -ChildPath $Name))
    }
}

Describe 'Set-DocsTranslationStamp.ps1' {
    It 'wpisuje odcisk z hashem źródła i pustą linią' {
        $Polish = "# x.y – Tytuł`n`n## Werdykt 26H2`nTreść.`n"
        $Root = New-DocsRepo -Files @{
            'docs\tweaks\x.y.md'    = $Polish
            'docs\en\tweaks\x.y.md' = "# x.y – Title`n`n## Verdict for 26H2`nText.`n"
        }
        $Result = Invoke-Stamp -Root $Root -Path 'docs\en\tweaks\x.y.md'
        $Result.Code | Should -Be 0
        $Bytes = Read-Bytes -Root $Root -Name 'docs\en\tweaks\x.y.md'
        $Bytes[0] | Should -Not -Be 0xEF
        $Text = $script:Utf8NoBom.GetString($Bytes)
        $Expected = "<!-- source: $($script:Docs)tweaks/x.y.md sha256: $(Get-ExpectedHash -Text $Polish) -->`n`n# x.y – Title`n`n## Verdict for 26H2`nText.`n"
        $Text | Should -BeExactly $Expected
    }

    It 'zastępuje stary odcisk zamiast dopisywać drugi' {
        $Polish = "# a – Tytuł`r`nNowa treść.`r`n"
        $Old = '<!-- source: ' + $script:Docs + 'apps/README.md sha256: ' + ('0' * 64) + ' -->'
        $Root = New-DocsRepo -Files @{
            'docs\apps\README.md'    = $Polish
            'docs\en\apps\README.md' = "$Old`r`n`r`n# a – Title`r`nNew text.`r`n"
        }
        $Result = Invoke-Stamp -Root $Root -Path 'docs\en\apps\README.md'
        $Result.Code | Should -Be 0
        $Text = $script:Utf8NoBom.GetString((Read-Bytes -Root $Root -Name 'docs\en\apps\README.md'))
        $Text | Should -BeExactly "<!-- source: $($script:Docs)apps/README.md sha256: $(Get-ExpectedHash -Text $Polish) -->`r`n`r`n# a – Title`r`nNew text.`r`n"
        ([regex]::Matches($Text, '<!-- source:')).Count | Should -Be 1
    }

    It 'hash nie zależy od CRLF/LF ani BOM źródła' {
        $Lf = "# p – Przygotowanie`n`nŻółć.`n"
        $Root = New-DocsRepo -Files @{
            'docs\prep\README.md'    = $Lf
            'docs\en\prep\README.md' = "# p – Preparation`n"
        }
        $null = Invoke-Stamp -Root $Root -Path 'docs\en\prep\README.md'
        $FirstLf = ($script:Utf8NoBom.GetString((Read-Bytes -Root $Root -Name 'docs\en\prep\README.md')) -split "`n")[0]

        $WithBom = New-Object -TypeName System.Text.UTF8Encoding -ArgumentList $true
        [System.IO.File]::WriteAllText((Join-Path -Path $Root -ChildPath 'docs\prep\README.md'), $Lf.Replace("`n", "`r`n"), $WithBom)
        $Result = Invoke-Stamp -Root $Root -Path 'docs\en\prep\README.md'
        $Result.Code | Should -Be 0
        $FirstCrlf = ($script:Utf8NoBom.GetString((Read-Bytes -Root $Root -Name 'docs\en\prep\README.md')) -split "`n")[0]

        $FirstCrlf | Should -BeExactly $FirstLf
        $FirstLf | Should -BeExactly "<!-- source: $($script:Docs)prep/README.md sha256: $(Get-ExpectedHash -Text $Lf) -->"
    }

    It 'ścieżka spoza docs/en daje kod 2' {
        $Root = New-DocsRepo -Files @{
            'docs\tweaks\x.md'    = "# x – Tytuł`n"
            'docs\en\zrodla\x.md' = "# x`n"
        }
        $Before = [Convert]::ToBase64String((Read-Bytes -Root $Root -Name 'docs\tweaks\x.md'))
        $Result = Invoke-Stamp -Root $Root -Path 'docs\tweaks\x.md'
        $Result.Code | Should -Be 2
        $Result.Text | Should -Match 'docs/tweaks/x\.md'
        [Convert]::ToBase64String((Read-Bytes -Root $Root -Name 'docs\tweaks\x.md')) | Should -BeExactly $Before
        (Invoke-Stamp -Root $Root -Path 'docs\en\zrodla\x.md').Code | Should -Be 2
        (Invoke-Stamp -Root $Root -Path 'docs\en\tweaks\nie-ma.md').Code | Should -Be 2
    }

    It 'ścieżka z inną wielkością liter daje odcisk z prawdziwą nazwą pliku' {
        $Polish = "# perf.sysmain – Tytuł`n"
        $Root = New-DocsRepo -Files @{
            'docs\tweaks\perf.sysmain.md'    = $Polish
            'docs\en\tweaks\perf.sysmain.md' = "# perf.sysmain – Title`n"
        }
        $Expected = "<!-- source: $($script:Docs)tweaks/perf.sysmain.md sha256: $(Get-ExpectedHash -Text $Polish) -->"

        $Result = Invoke-Stamp -Root $Root -Path 'docs\en\tweaks\PERF.Sysmain.md'
        $Result.Code | Should -Be 0
        ($script:Utf8NoBom.GetString((Read-Bytes -Root $Root -Name 'docs\en\tweaks\perf.sysmain.md')) -split "`n")[0] | Should -BeExactly $Expected
        $Result.Text | Should -Match ([regex]::Escape("$($script:Docs)en/tweaks/perf.sysmain.md"))

        $Result = Invoke-Stamp -Root $Root -Path 'Docs\EN\Tweaks\perf.SYSMAIN.md'
        $Result.Code | Should -Be 0
        ($script:Utf8NoBom.GetString((Read-Bytes -Root $Root -Name 'docs\en\tweaks\perf.sysmain.md')) -split "`n")[0] | Should -BeExactly $Expected
    }

    It 'błędna druga ścieżka daje kod 2 i nie zmienia pierwszego pliku' {
        $Root = New-DocsRepo -Files @{
            'docs\tweaks\a.md'    = "# a – Tytuł`n"
            'docs\en\tweaks\a.md' = "# a – Title`n"
        }
        $Before = [Convert]::ToBase64String((Read-Bytes -Root $Root -Name 'docs\en\tweaks\a.md'))
        $Result = Invoke-Stamp -Root $Root -Path 'docs\en\tweaks\a.md', 'docs\en\tweaks\nie-ma.md'
        $Result.Code | Should -Be 2
        $Result.Text | Should -Match 'nie-ma\.md'
        [Convert]::ToBase64String((Read-Bytes -Root $Root -Name 'docs\en\tweaks\a.md')) | Should -BeExactly $Before
    }

    It 'przyjmuje ścieżkę bezwzględną' {
        $Polish = "# b – Tytuł`n"
        $Root = New-DocsRepo -Files @{
            'docs\apps\README.md'    = $Polish
            'docs\en\apps\README.md' = "# b – Title`n"
        }
        $Absolute = Join-Path -Path $Root -ChildPath 'docs\en\apps\README.md'
        $Result = Invoke-Stamp -Root $Root -Path $Absolute
        $Result.Code | Should -Be 0
        $Text = $script:Utf8NoBom.GetString((Read-Bytes -Root $Root -Name 'docs\en\apps\README.md'))
        $Text | Should -BeExactly "<!-- source: $($script:Docs)apps/README.md sha256: $(Get-ExpectedHash -Text $Polish) -->`n`n# b – Title`n"
    }

    It 'brak polskiego źródła daje kod 2 i nie zmienia pliku' {
        $Root = New-DocsRepo -Files @{ 'docs\en\tweaks\sierota.md' = "# sierota – Orphan`n" }
        $Before = [Convert]::ToBase64String((Read-Bytes -Root $Root -Name 'docs\en\tweaks\sierota.md'))
        $Result = Invoke-Stamp -Root $Root -Path 'docs\en\tweaks\sierota.md'
        $Result.Code | Should -Be 2
        $Result.Text | Should -Match 'docs/tweaks/sierota\.md'
        [Convert]::ToBase64String((Read-Bytes -Root $Root -Name 'docs\en\tweaks\sierota.md')) | Should -BeExactly $Before
    }
}
