BeforeAll {
    # Wyłącznik: żaden test na mockach nie może dotknąć prawdziwego Hyper-V (patrz Support\NoLiveVm.ps1).
    . (Join-Path $PSScriptRoot 'Support\NoLiveVm.ps1')

    Import-Module (Join-Path $PSScriptRoot '..\modules\VmCommon.psm1') -Force
    Import-Module (Join-Path $PSScriptRoot '..\modules\VmRun.psm1') -Force
    $script:VmRoot = Join-Path $PSScriptRoot '..'
    $script:CatalogPath = Join-Path $script:VmRoot 'catalog\vm-test-catalog.json'
    $script:RealCasesPath = Join-Path $script:VmRoot 'cases.psd1'

    function Write-CasesFile {
        param([string]$Path, [string]$CaseBody)
        Set-Content -LiteralPath $Path -Value "@{ Cases = @( @{ $CaseBody } ) }" -Encoding UTF8
    }
    function Import-TextCase {
        # Jeden przypadek Execute z podanym Expect.MessageContains (tekst psd1) i dodatkami ciała przypadku.
        param([string]$Message, [string]$Extra = '', [string]$Check = "'`$true'")
        $Path = Join-Path $TestDrive ('c-' + [guid]::NewGuid().ToString('N') + '.psd1')
        $Body = "Name = 'tekst'; TweakId = 'repair.windows-old'; Flow = 'Execute'; Catalog = 'BuiltIn'; RestorePoint = 'skip'; Check = $Check$Extra; Expect = @{ ExecuteExit = 1; ExecuteOutcome = 'NotSupported'; MessageContains = $Message }"
        Write-CasesFile -Path $Path -CaseBody $Body
        return Import-VmCases -CasesPath $Path -CatalogPath $script:CatalogPath
    }
    function New-ResolvedCase {
        # Przypadek po Resolve-VmCaseLanguage (TextLanguage) przepuszczony przez Resolve-VmCaseTexts.
        param([hashtable]$Case, [string]$TextLanguage)
        $Copy = @{} + $Case
        $Copy.TextLanguage = $TextLanguage
        return @(Resolve-VmCaseTexts -Cases @($Copy))[0]
    }
    $script:WindowsOld = 'Nie ma poprzedniej instalacji Windows – nie ma czego usuwać.'
    # Bez Check: testy dokładają go same (dodawanie hashtable z powtórzonym kluczem rzuca).
    $script:Execute = @{ Name = 'tekst'; TweakId = 'repair.windows-old'; Flow = 'Execute'; Catalog = 'BuiltIn'; RestorePoint = 'skip' }
}

AfterAll { Clear-NoLiveVm }

Describe 'Import-VmCases: oczekiwane teksty z tabel tłumaczeń' {
    It 'Import-VmCases: fragment zamiast pełnego klucza = wyjątek z nazwą przypadku i tekstem' {
        { Import-TextCase -Message "'zależne'" } | Should -Throw "*Przypadek 'tekst'*'zależne'*tabelach tłumaczeń*"
    }
    It 'Import-VmCases: pełny klucz z tabel jest przyjęty i zostaje bez zmian' {
        $Cases = Import-TextCase -Message "'$script:WindowsOld'"
        $Cases[0].Expect.MessageContains | Should -BeExactly $script:WindowsOld
    }
    It 'Import-VmCases: {T:…} spoza tabel = wyjątek' {
        { Import-TextCase -Message "'$script:WindowsOld'" -Extra "; Setup = @('Write-Output ''{T:Tego tekstu nie ma w tabelach}''')" } |
            Should -Throw "*Przypadek 'tekst'*'Tego tekstu nie ma w tabelach'*tabelach tłumaczeń*"
    }
    It 'Import-VmCases: niedomknięty token {T: = wyjątek' {
        { Import-TextCase -Message "'$script:WindowsOld'" -Extra "; Hypothesis = 'tekst {T:bez końca'" } | Should -Throw "*Przypadek 'tekst'*{T:*"
    }
    It 'Import-VmCases: {T:…} z tabel w Check jest przyjęty' {
        $Cases = Import-TextCase -Message "'$script:WindowsOld'" -Check "'''{T:DSOptimizer – punkt ręczny}'' -ne '''''"
        $Cases[0].Check | Should -BeLike '*{T:DSOptimizer – punkt ręczny}*'
    }
    It 'Import-VmCases: AnyOf i TestCatalog przyjęte, inny kształt = wyjątek' {
        $AnyOf = "@{ AnyOf = @('Tej aplikacji nie wolno usunąć ({0}, grupa {1}).', 'Tej aplikacji nie wolno usunąć ({0}): {1}') }"
        @(Import-TextCase -Message $AnyOf).Count | Should -Be 1
        @(Import-TextCase -Message "@{ TestCatalog = 'Komunikat skryptu katalogu testowego' }").Count | Should -Be 1
        @(Import-TextCase -Message "@('Magazyn składników', @{ AnyOf = @('$script:WindowsOld') }, @{ TestCatalog = 'x' })").Count | Should -Be 1
        { Import-TextCase -Message "@{ Coś = 'x' }" } | Should -Throw "*Przypadek 'tekst'*MessageContains*"
        { Import-TextCase -Message "@{ AnyOf = @('$script:WindowsOld'); TestCatalog = 'x' }" } | Should -Throw '*MessageContains*'
        { Import-TextCase -Message '@{ AnyOf = @() }' } | Should -Throw '*AnyOf*'
        { Import-TextCase -Message "@{ TestCatalog = '' }" } | Should -Throw '*TestCatalog*'
        { Import-TextCase -Message '@()' } | Should -Throw '*MessageContains*'
        { Import-TextCase -Message '1' } | Should -Throw '*MessageContains*'
        { Import-TextCase -Message "@{ AnyOf = @('$script:WindowsOld', 'nie ma czego') }" } | Should -Throw "*'nie ma czego'*tabelach tłumaczeń*"
    }
    It 'Import-VmCases: WarningContains i WarningAbsent też muszą być kluczami tabel' {
        $Path = Join-Path $TestDrive 'warn.psd1'
        Write-CasesFile -Path $Path -CaseBody "Name = 'app'; AppId = 'appx:Microsoft.BingNews_8wekyb3d8bbwe'; Flow = 'AppRemove'; RestorePoint = 'skip'; Check = '`$true'; Expect = @{ ListedBefore = `$true; RemoveExit = 0; RemoveOutcome = 'Removed'; ListedAfter = `$false; WarningAbsent = 'Usunięta dla wszystkich kont' }"
        { Import-VmCases -CasesPath $Path -CatalogPath $script:CatalogPath } | Should -Throw "*'Usunięta dla wszystkich kont'*tabelach tłumaczeń*"
    }
}

Describe 'Resolve-VmCaseTexts' {
    It 'Resolve-VmCaseTexts en: wzorzec z angielskiej tabeli' {
        $R = New-ResolvedCase -Case ($script:Execute + @{ Expect = @{ ExecuteExit = 1; ExecuteOutcome = 'NotSupported'; MessageContains = $script:WindowsOld } }) -TextLanguage en
        $R.Expect.MessagePatterns[0].Any | Should -Be @("*There's no previous Windows installation – nothing to remove.*")
        $R.Expect.MessagePatterns[0].Keys | Should -Be @($script:WindowsOld)
        $R.Expect.MessageContains | Should -BeExactly $script:WindowsOld
    }
    It 'Resolve-VmCaseTexts pl: wzorzec z polskiego klucza, {n} jako *' {
        $R = New-ResolvedCase -Case ($script:Execute + @{ Expect = @{ ExecuteExit = 0; ExecuteOutcome = 'Executed'; MessageContains = 'Usunięto poprzednią instalację Windows. Zwolniono: {0}.' } }) -TextLanguage pl
        $R.Expect.MessagePatterns[0].Any | Should -Be @('*Usunięto poprzednią instalację Windows. Zwolniono: *.*')
    }
    It 'lista = każda grupa musi trafić' {
        $R = New-ResolvedCase -Case ($script:Execute + @{ Expect = @{ ExecuteExit = 0; ExecuteOutcome = 'Executed'; MessageContains = @('Magazyn składników', 'wykonano (DISM /StartComponentCleanup, kod 0).') } }) -TextLanguage en
        $G = @($R.Expect.MessagePatterns)
        $G.Count | Should -Be 2
        (('Component store: done (DISM /StartComponentCleanup, code 0).' -like $G[0].Any[0]) -and ('Component store: done (DISM /StartComponentCleanup, code 0).' -like $G[1].Any[0])) | Should -BeTrue
        ('Component store: failed' -like $G[1].Any[0]) | Should -BeFalse
    }
    It 'AnyOf = jedna grupa z kilkoma wzorcami' {
        $AnyOf = @{ AnyOf = @('Wyczyszczono pamięć DNS (przed: {0} wpisów, po: 0).', 'Wyczyszczono pamięć DNS (przed: {0} wpisów, po: {1} – tylko wpisy z pliku hosts).') }
        $R = New-ResolvedCase -Case ($script:Execute + @{ Expect = @{ ExecuteExit = 0; ExecuteOutcome = 'Executed'; MessageContains = $AnyOf } }) -TextLanguage en
        $R.Expect.MessagePatterns.Count | Should -Be 1
        $R.Expect.MessagePatterns[0].Any.Count | Should -Be 2
        $R.Expect.MessagePatterns[0].Keys.Count | Should -Be 2
        ('Cleared the DNS cache (entries before: 3, after: 1 – only entries from the hosts file).' -like $R.Expect.MessagePatterns[0].Any[1]) | Should -BeTrue
    }
    It 'TestCatalog: polski tekst dosłownie w obu językach' {
        $Text = 'Komunikat [katalogu] testowego*'
        foreach ($Language in 'pl', 'en') {
            $R = New-ResolvedCase -Case ($script:Execute + @{ Expect = @{ ExecuteExit = 1; ExecuteOutcome = 'Failed'; MessageContains = @{ TestCatalog = $Text } } }) -TextLanguage $Language
            $R.Expect.MessagePatterns[0].Any | Should -Be @('*' + [System.Management.Automation.WildcardPattern]::Escape($Text) + '*')
            ("błąd: $Text koniec" -like $R.Expect.MessagePatterns[0].Any[0]) | Should -BeTrue
            ('Komunikat katalogu testowego' -like $R.Expect.MessagePatterns[0].Any[0]) | Should -BeFalse
        }
    }
    It 'WarningContains i WarningAbsent dają WarningPatterns i WarningAbsentPatterns' {
        $Case = @{ Name = 'app'; AppId = 'appx:X'; Flow = 'AppRemove'; RestorePoint = 'skip'; Check = '$true'
            Expect = @{ ListedBefore = $true; RemoveExit = 0; RemoveOutcome = 'Removed'; ListedAfter = $false; WarningContains = 'Usunięta dla wszystkich kont na tym komputerze.'; WarningAbsent = 'Usunięta dla wszystkich kont na tym komputerze.' } }
        $R = New-ResolvedCase -Case $Case -TextLanguage en
        $R.Expect.WarningPatterns[0].Any | Should -Be @('*Removed for all accounts on this computer.*')
        $R.Expect.WarningAbsentPatterns[0].Any | Should -Be @('*Removed for all accounts on this computer.*')
    }
    It '{T:} w Check: apostrof podwojony' {
        $R = New-ResolvedCase -Case ($script:Execute + @{ Check = "'{T:$script:WindowsOld}'"; Expect = @{ ExecuteExit = 1; ExecuteOutcome = 'NotSupported' } }) -TextLanguage en
        $R.Check | Should -BeExactly "'There''s no previous Windows installation – nothing to remove.'"
    }
    It '{T:} w Setup (lista skryptów): apostrof podwojony, pl bez zmian treści' {
        $Case = $script:Execute + @{ Setup = @("Write-Output '{T:$script:WindowsOld}'", 'Write-Output 1'); Expect = @{ ExecuteExit = 1; ExecuteOutcome = 'NotSupported' } }
        (New-ResolvedCase -Case $Case -TextLanguage en).Setup | Should -Be @("Write-Output 'There''s no previous Windows installation – nothing to remove.'", 'Write-Output 1')
        (New-ResolvedCase -Case $Case -TextLanguage pl).Setup | Should -Be @("Write-Output '$script:WindowsOld'", 'Write-Output 1')
    }
    It '{T:} w danych Snapshot: bez podwajania' {
        $Case = @{ Name = 'menu'; TweakId = 'sec.take-ownership-menu'; Flow = 'Full'; Catalog = 'BuiltIn'; RestorePoint = 'skip'
            Snapshot = @{ Registry = @(@{ Path = 'HKLM\SOFTWARE\X'; Name = 'MUIVerb' }); Services = @() }
            Expect = @{ Before = 'NotApplied'; ApplyExit = 0; ApplyOutcome = 'Applied'; AfterApply = 'Applied'; UndoExit = 0; UndoOutcome = 'Undone'; AfterUndo = 'NotApplied'
                AfterApplySnapshot = @{ Registry = @(@{ Path = 'HKLM\SOFTWARE\X'; Name = 'MUIVerb'; Exists = $true; Kind = 'String'; Data = '{T:Przejmij na własność}' }, @{ Path = 'HKLM\SOFTWARE\X'; Name = 'Opis'; Exists = $true; Kind = 'String'; Data = "{T:$script:WindowsOld}" }) } } }
        $R = New-ResolvedCase -Case $Case -TextLanguage en
        $R.Expect.AfterApplySnapshot.Registry[0].Data | Should -BeExactly 'Take ownership'
        $R.Expect.AfterApplySnapshot.Registry[1].Data | Should -BeExactly "There's no previous Windows installation – nothing to remove."
        $R.Expect.AfterApplySnapshot.Registry[0].Exists | Should -BeTrue
        $Case.Expect.AfterApplySnapshot.Registry[0].Data | Should -BeExactly '{T:Przejmij na własność}' -Because 'wejście zostaje bez zmian'
    }
    It 'przypadek z Language en na VM pl: MUIVerb po angielsku, a na VM en z Language pl po polsku' {
        $Base = @{ Name = 'menu'; TweakId = 'sec.take-ownership-menu'; Flow = 'Full'; Catalog = 'BuiltIn'; RestorePoint = 'skip'
            CheckAfterApply = '$r.GetValue(''MUIVerb'') -ceq ''{T:Przejmij na własność}'''
            Expect = @{ AfterApplySnapshot = @{ Registry = @(@{ Path = 'HKLM\SOFTWARE\X'; Name = 'MUIVerb'; Exists = $true; Kind = 'String'; Data = '{T:Przejmij na własność (administrator)}' }) } } }
        $En = @(Resolve-VmCaseTexts -Cases (Resolve-VmCaseLanguage -Cases @($Base + @{ Language = 'en' }) -VmLanguage pl))[0]
        $En.Expect.AfterApplySnapshot.Registry[0].Data | Should -BeExactly 'Take ownership (administrator)'
        $En.CheckAfterApply | Should -BeExactly '$r.GetValue(''MUIVerb'') -ceq ''Take ownership'''
        $Pl = @(Resolve-VmCaseTexts -Cases (Resolve-VmCaseLanguage -Cases @($Base + @{ Language = 'pl' }) -VmLanguage en))[0]
        $Pl.Expect.AfterApplySnapshot.Registry[0].Data | Should -BeExactly 'Przejmij na własność (administrator)'
        $Pl.CheckAfterApply | Should -BeExactly '$r.GetValue(''MUIVerb'') -ceq ''Przejmij na własność'''
    }
    It 'Language system na VM en: oczekiwania po angielsku' {
        $Case = $script:Execute + @{ Language = 'system'; Expect = @{ ExecuteExit = 1; ExecuteOutcome = 'NotSupported'; MessageContains = $script:WindowsOld } }
        $R = @(Resolve-VmCaseTexts -Cases (Resolve-VmCaseLanguage -Cases @($Case) -VmLanguage en))[0]
        $R.CliLanguage | Should -Be 'system'
        $R.TextLanguage | Should -Be 'en'
        $R.Expect.MessagePatterns[0].Any | Should -Be @("*There's no previous Windows installation – nothing to remove.*")
    }
    It 'Expand-VmCasePlaceholders (przebieg w VmHost) zachowuje Keys i Any grup wzorców' {
        # Grupa ma wpis 'Keys': kopia przez .Keys hashtable zwróciłaby wartość wpisu zamiast listy kluczy.
        $R = New-ResolvedCase -Case ($script:Execute + @{ Expect = @{ ExecuteExit = 1; ExecuteOutcome = 'NotSupported'; MessageContains = $script:WindowsOld } }) -TextLanguage en
        $E = Expand-VmCasePlaceholders -Case $R -UserSid 'S-1-5-21-1-2-3-1000'
        @($E.Expect.MessagePatterns[0]['Keys']) | Should -Be @($script:WindowsOld)
        @($E.Expect.MessagePatterns[0]['Any']) | Should -Be @("*There's no previous Windows installation – nothing to remove.*")
    }
    It 'Resolve-VmCaseTexts bez TextLanguage = wyjątek' {
        { Resolve-VmCaseTexts -Cases @($script:Execute + @{ Expect = @{ ExecuteExit = 0; ExecuteOutcome = 'Executed' } }) } | Should -Throw "*Przypadek 'tekst'*TextLanguage*Resolve-VmCaseLanguage*"
    }
    It 'Resolve-VmCaseTexts: przypadek bez tekstów zostaje bez zmian, kolejność i liczba zachowane' {
        $R = @(Resolve-VmCaseTexts -Cases @(@{ Name = '1'; TextLanguage = 'pl'; Expect = @{ Before = 'NotApplied' } }, @{ Name = '2'; TextLanguage = 'en'; Flow = 'Autostart' }))
        @($R.Name) | Should -Be @('1', '2')
        $R[0].Expect.ContainsKey('MessagePatterns') | Should -BeFalse
        $R[0].Expect.Before | Should -Be 'NotApplied'
    }
}

Describe 'cases.psd1 z repo: teksty z tabel tłumaczeń' {
    BeforeAll {
        $script:Cases = Import-VmCases -CasesPath $script:RealCasesPath -CatalogPath $script:CatalogPath
    }
    It 'cases.psd1 z repo: import i rozwiązanie dla pl i en bez błędów' {
        foreach ($L in 'pl', 'en') {
            { Resolve-VmCaseTexts -Cases (Resolve-VmCaseLanguage -Cases $script:Cases -VmLanguage $L) } | Should -Not -Throw
        }
        $En = @(Resolve-VmCaseTexts -Cases (Resolve-VmCaseLanguage -Cases $script:Cases -VmLanguage en))
        foreach ($Case in @($En | Where-Object { $_.ContainsKey('Expect') -and $_.Expect -is [hashtable] -and $_.Expect.ContainsKey('MessageContains') })) {
            @($Case.Expect.MessagePatterns).Count | Should -BeGreaterThan 0 -Because "przypadek '$($Case.Name)'"
        }
        ($En | Where-Object Name -eq 'repair-restore-point-disabled').Check | Should -BeLike "*-eq 'DSOptimizer – manual restore point'*"
    }
    It 'cases.psd1: cli-language-system ma Language system, kontrolę settings.json w Setup i klucz windows-old' {
        $Case = @($script:Cases | Where-Object Name -eq 'cli-language-system')
        $Case.Count | Should -Be 1
        $Case[0].Language | Should -BeExactly 'system'
        $Case[0].TweakId | Should -Be 'repair.windows-old'
        $Case[0].Flow | Should -Be 'Execute'
        $Case[0].Catalog | Should -Be 'BuiltIn'
        @($Case[0].Setup) -join ' ' | Should -BeLike "*Test-Path -LiteralPath 'C:\ProgramData\DSOptimizer\settings.json'*throw*"
        $Case[0].Expect.MessageContains | Should -BeExactly $script:WindowsOld
        $Case[0].Expect.ExecuteOutcome | Should -Be 'NotSupported'
        $Absent = @($script:Cases | Where-Object Name -eq 'repair-windows-old-absent')[0]
        $Case[0].Check | Should -BeExactly $Absent.Check
        $Names = @($script:Cases.Name)
        [array]::IndexOf($Names, 'cli-language-system') | Should -Be ([array]::IndexOf($Names, 'repair-windows-old-absent') + 1)
    }
    It 'cases.psd1: sec-take-ownership-menu ma Language pl' {
        @($script:Cases | Where-Object Name -eq 'sec-take-ownership-menu')[0].Language | Should -BeExactly 'pl'
        @($script:Cases | Where-Object Name -eq 'sec-take-ownership-menu-en')[0].Language | Should -BeExactly 'en'
    }
    It 'cases.psd1: w Check i Snapshot nie ma dosłownych tekstów programu' {
        $Texts = [System.Collections.Generic.List[string]]::new()
        $Collect = {
            param($Value)
            if ($Value -is [string]) { $Texts.Add($Value) }
            elseif ($Value -is [hashtable]) { foreach ($Item in $Value.Values) { & $Collect $Item } }
            elseif ($Value -is [System.Array]) { foreach ($Item in $Value) { & $Collect $Item } }
        }
        foreach ($Case in $script:Cases) {
            foreach ($Key in 'Setup', 'Check', 'Cleanup', 'CheckAfterApply', 'CheckAfterUndo', 'Snapshot') { if ($Case.ContainsKey($Key)) { & $Collect $Case[$Key] } }
            if ($Case.Expect -is [hashtable]) { foreach ($Key in @($Case.Expect.Keys | Where-Object { $_ -like '*Snapshot' })) { & $Collect $Case.Expect[$Key] } }
        }
        $Texts.Count | Should -BeGreaterThan 100
        $Joined = [regex]::Replace(($Texts -join "`n"), '\{T:(?:[^{}]|\{\d+\})+\}', '')
        $Joined | Should -Not -Match 'DSOptimizer – punkt ręczny|Take ownership|Przejmij na własność|manual restore point'
        ($Texts -join "`n") | Should -Match ([regex]::Escape('{T:DSOptimizer – punkt ręczny}'))
    }
}
