<#
.SYNOPSIS
    Teksty programu w języku testu VM, wyłącznie z tabel tłumaczeń repo.

.DESCRIPTION
    Instrukcja uruchomienia:
        Import-Module .\tests\vm\modules\VmText.psm1
        Get-DsoUiText -Polish 'Historia zmian' -Language en        # 'Change history'
        Test-DsoTextKey -Polish 'Historia zmian'                   # $true
        ConvertTo-DsoTextPattern -Polish 'Zastosuj ({0})' -Language en   # '*Apply (*)*' dla operatora -like
        Testy: Invoke-Pester -Path tests/vm/tests/VmText.Tests.ps1

    Opis działania:
        Polski tekst jest kluczem tłumaczenia w C# (L.T/L.F, {l:T} w XAML). Moduł wczytuje raz na korzeń repo wszystkie tabele
        src\DSOptimizer.Core\Localization\Translations\*.json (słownik polski -> angielski, porównanie Ordinal) i zwraca:
          - Get-DsoUiText: tekst w języku testu (pl = klucz bez zmian, en = wpis z tabeli; brak = wyjątek, nigdy polski zastępczo),
          - Test-DsoTextKey: czy polski tekst jest dokładnie kluczem w tabelach,
          - ConvertTo-DsoTextPattern: wzorzec dla -like (znaki wieloznaczne z tekstu dosłowne, {n} jako *, całość w *…*).
        Ten sam polski tekst z dwoma różnymi tłumaczeniami = wyjątek. Dotyka tylko plików repo; VM i sieci nie rusza.
#>
Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

$script:RepoRoot = Split-Path -Parent (Split-Path -Parent (Split-Path -Parent $PSScriptRoot))
# Tabele tłumaczeń wczytane raz na korzeń repo (słownik z rozróżnieniem wielkości liter).
$script:TranslationCache = @{}

function Get-DsoTranslationTable {
    # Wszystkie tabele src\DSOptimizer.Core\Localization\Translations\*.json repo -> słownik polski -> angielski (Ordinal).
    # Ten sam polski tekst z dwoma różnymi tłumaczeniami = błąd (sonda nie zgadywałaby, które pokazuje okno).
    [CmdletBinding()]
    param([Parameter(Mandatory)][string]$Root)
    $Full = [System.IO.Path]::GetFullPath($Root)
    if ($script:TranslationCache.ContainsKey($Full)) { return , $script:TranslationCache[$Full] }
    $Directory = Join-Path $Full 'src\DSOptimizer.Core\Localization\Translations'
    if (-not (Test-Path -LiteralPath $Directory -PathType Container)) { throw "Brak folderu tabel tłumaczeń '$Directory'." }
    $Table = New-Object 'System.Collections.Generic.Dictionary[string,string]' ([System.StringComparer]::Ordinal)
    foreach ($File in @(Get-ChildItem -LiteralPath $Directory -Filter '*.json' -File | Sort-Object Name)) {
        $Json = [System.IO.File]::ReadAllText($File.FullName, [System.Text.Encoding]::UTF8) | ConvertFrom-Json
        foreach ($Entry in @($Json.entries)) {
            if ($null -eq $Entry) { continue }
            $Pl = [string]$Entry.pl
            $En = [string]$Entry.en
            if ($Table.ContainsKey($Pl)) {
                if ($Table[$Pl] -cne $En) { throw "Tekst '$Pl' ma w tabelach dwa różne tłumaczenia: '$($Table[$Pl])' i '$En' ($($File.Name))." }
                continue
            }
            $Table[$Pl] = $En
        }
    }
    $script:TranslationCache[$Full] = $Table
    return , $Table
}

function Get-DsoUiText {
    # Tekst okna w języku sondy: pl = polski tekst (klucz tłumaczenia) bez zmian; en = wpis z tabel tłumaczeń repo. Brak wpisu (albo pusty
    # angielski) = wyjątek „brak tłumaczenia” – sonda nigdy nie spada po cichu na polski.
    [CmdletBinding()]
    [OutputType([string])]
    param(
        [Parameter(Mandatory)][string]$Polish,
        [Parameter(Mandatory)][ValidateSet('pl', 'en')][string]$Language,
        [string]$Root = $script:RepoRoot
    )
    if ($Language -eq 'pl') { return $Polish }
    $Table = Get-DsoTranslationTable -Root $Root
    $English = $null
    if (-not $Table.TryGetValue($Polish, [ref]$English) -or [string]::IsNullOrEmpty($English)) { throw "brak tłumaczenia: '$Polish' (tabele w '$Root')." }
    return $English
}

function Test-DsoTextKey {
    # Czy polski tekst jest dokładnie (Ordinal) kluczem w tabelach tłumaczeń.
    [CmdletBinding()]
    [OutputType([bool])]
    param(
        [Parameter(Mandatory)][string]$Polish,
        [string]$Root = $script:RepoRoot
    )
    $Table = Get-DsoTranslationTable -Root $Root
    return [bool]$Table.ContainsKey($Polish)
}

function ConvertTo-DsoTextPattern {
    # Wzorzec dla -like: tekst w języku testu (pl = klucz, en = tabela), znaki wieloznaczne dosłowne, każde {n} jako *, całość w *…*.
    # Klucz spoza tabel = wyjątek „brak tłumaczenia” w obu językach (literówka w oczekiwaniu nie przejdzie po cichu).
    [CmdletBinding()]
    [OutputType([string])]
    param(
        [Parameter(Mandatory)][string]$Polish,
        [Parameter(Mandatory)][ValidateSet('pl', 'en')][string]$Language,
        [string]$Root = $script:RepoRoot
    )
    if (-not (Test-DsoTextKey -Polish $Polish -Root $Root)) { throw "brak tłumaczenia: '$Polish' (tabele w '$Root')." }
    $Text = Get-DsoUiText -Polish $Polish -Language $Language -Root $Root
    $Escaped = [System.Management.Automation.WildcardPattern]::Escape($Text)
    return '*' + ([regex]::Replace($Escaped, '\{\d+\}', '*')) + '*'
}

Export-ModuleMember -Function Get-DsoUiText, Test-DsoTextKey, ConvertTo-DsoTextPattern
