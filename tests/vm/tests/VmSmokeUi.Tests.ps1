BeforeAll {
    # Wyłącznik: żaden test na mockach nie może dotknąć prawdziwego Hyper-V (patrz Support\NoLiveVm.ps1).
    . (Join-Path $PSScriptRoot 'Support\NoLiveVm.ps1')

    Import-Module (Join-Path $PSScriptRoot '..\modules\VmText.psm1') -Force
    Import-Module (Join-Path $PSScriptRoot '..\modules\VmSmokeUi.psm1') -Force
    $script:ModulePath = (Resolve-Path (Join-Path $PSScriptRoot '..\modules\VmSmokeUi.psm1')).Path
    # Polskie teksty programu czytane ze źródeł C#. Są owinięte w L.T("…") (dawniej `const string X = "…"`), a kluczem tłumaczenia
    # jest polski literał, więc sonda na polskiej VM szuka dokładnie tego, co tu wyciągnięto. Brak tekstu = błąd, nigdy test „na pusto”.
    function script:ConvertFrom-CsLiteral([string]$Body) {
        return [regex]::Replace($Body, '\\(.)', {
            param($M)
            switch ($M.Groups[1].Value) { 'n' { "`n" } 't' { "`t" } 'r' { "`r" } default { $M.Groups[1].Value } }
        })
    }
    # $Lead = wyrażenie regularne tuż przed literałem; zwraca wszystkie dopasowane literały (z odcytowanymi \" i \\).
    function script:Get-CsLiteral([string]$Source, [string]$Lead) {
        return @([regex]::Matches($Source, $Lead + '"((?:[^"\\]|\\.)*)"') | ForEach-Object { ConvertFrom-CsLiteral $_.Groups[1].Value })
    }
    # Właściwość/stała tekstowa: `static string X => L.T("…")`, `X = L.T("…")` albo stare `const string X = "…"`.
    function script:Get-CsText([string]$Source, [string]$Name) {
        $Found = @(Get-CsLiteral $Source ('\b' + [regex]::Escape($Name) + '\s*(?:\{\s*get;\s*\}\s*)?(?:=>|=)\s*(?:L\.T\(\s*)?'))
        if ($Found.Count -eq 0) { throw "Nie znaleziono tekstu '$Name' w źródle C# (kształt inny niż L.T(`"…`") albo const string?)." }
        return $Found[0]
    }
    function script:Read-CoreSource([string]$RelativePath) {
        [System.IO.File]::ReadAllText((Join-Path $PSScriptRoot ('..\..\..\src\DSOptimizer.Core\' + $RelativePath)), [System.Text.Encoding]::UTF8)
    }
    function script:New-HitPoint([string]$Name, [int]$Ht, $XDip = 8) { [pscustomobject]@{ Name = $Name; XDip = $XDip; X = $XDip; Y = 0; Ht = $Ht } }
    function script:New-UiProbe([hashtable]$Override) {
        $Probe = [ordered]@{
            Ok = $true; Error = $null; HistoryMenuFound = $true; HistoryPageOpened = $true; ApplyError = $null; HistoryError = $null
            EntryNames = @('Szybsze otwieranie podmenu, Eksplorator i wygoda, Aktywna, 20.10.2026 21:00'); HitTestError = $null
            FilterRiskOpened = $true; FilterRiskSelected = $true; FilterShowOpened = $true; FilterShowSelected = $true; FilterHistoryOpened = $true; FilterHistorySelected = $true; FilterError = $null
            RowClickPreview = $true; RowDoubleClickToggles = $true; RunIconRuns = $true; RunIconDoubleClickOnce = $true; RowError = $null
            PrivacyButtonFound = $true; PrivacyApplyCountOnEntry = 0; PrivacyApplyCount = 2; PrivacyApplyText = 'Zastosuj (2)'; PrivacyError = $null
            AppsListRead = $true; AppsButtonFound = $true; AppsRemoveCountOnEntry = 0; AppsRemoveCount = 8; AppsRemoveText = 'Usuń (8)'; AppsError = $null
            PrepMenuFound = $true; PrepLoaded = $true; PrepWindowsUpdateButton = $true; PrepWindowsUpdateNote = $false; PrepError = $null
            PrepTitles = @('Komputer', 'Windows Update', 'Opcjonalne aktualizacje sterowników', 'Sterowniki chipsetu', 'Grafika', 'Wsparcie producenta', 'Urządzenia bez sterownika')
            PrepGraphicsLines = @('Microsoft Hyper-V Video', '10.0.26100.1')
            RepairMenuFound = $true; RepairListFound = $true; RepairError = $null
            RepairTexts = @('Naprawa A', 'Opis naprawy A', 'Naprawa B', 'Opis naprawy B')
            InstallMenuFound = $true; InstallLoaded = $true; InstallButtonText = 'Zainstaluj (0)'; InstallButtonEnabled = $false; InstallError = $null
            InstallCategories = @('Wszystkie', 'Zaznaczone', 'Przeglądarki', 'Narzędzia'); InstallSubtitle = 'Źródła: winget (Microsoft) i GitHub (RustDesk) · 34 aplikacje · zainstalowane: 3'
            InstallTexts = @('Google Chrome', 'Google.Chrome', 'Przeglądarka internetowa Google.', '7-Zip', '7zip.7zip', 'Zainstalowana')
            AutostartMenuFound = $true; AutostartPageScoped = $true; AutostartLoaded = $true; AutostartReadFailed = $false; AutostartError = $null
            AutostartGroupNames = @('Włączone, 3', 'Wyłączone, 1', 'Bez pliku, 0')
            AutostartDisableText = 'Wyłącz (0)'; AutostartDisableEnabled = $false; AutostartEnableText = 'Włącz (0)'; AutostartEnableEnabled = $false
            HitTest = @(
                [pscustomobject]@{
                    State = 'Normal'; BarTop = 36; BarBottom = 80; ScanX = 59
                    Points = @((New-HitPoint 'mid' 2), (New-HitPoint 'low' 2), (New-HitPoint 'below' 1), (New-HitPoint 'mid' 2 'srodek'), (New-HitPoint 'low' 2 'srodek'), (New-HitPoint 'below' 1 'srodek'))
                    Scan = @([pscustomobject]@{ Y = 79; Ht = 2 }, [pscustomobject]@{ Y = 80; Ht = 1 })
                }
                [pscustomobject]@{
                    State = 'Maximized'; BarTop = 0; BarBottom = 44; ScanX = 8
                    Points = @((New-HitPoint 'mid' 2), (New-HitPoint 'low' 2), (New-HitPoint 'below' 1), (New-HitPoint 'mid' 2 80), (New-HitPoint 'low' 2 80), (New-HitPoint 'below' 1 80), (New-HitPoint 'mid' 2 'srodek'), (New-HitPoint 'low' 2 'srodek'), (New-HitPoint 'below' 1 'srodek'))
                    Scan = @([pscustomobject]@{ Y = 42; Ht = 2 }, [pscustomobject]@{ Y = 43; Ht = 2 }, [pscustomobject]@{ Y = 44; Ht = 1 })
                })
        }
        foreach ($K in $Override.Keys) { $Probe[$K] = $Override[$K] }
        [pscustomobject]$Probe
    }
    function script:Get-Check($Checks, [string]$Name) { $Checks | Where-Object Name -eq $Name }
    # Tytuł pierwszej pozycji danej kategorii z pliku <kategoria>.json katalogu (do sprawdzenia kolejności tytułów Napraw i narzędzi).
    function script:Get-CatalogTitleByCategory([string]$Dir, [string]$Category) {
        $Items = [System.IO.File]::ReadAllText((Join-Path $Dir "$Category.json"), [System.Text.Encoding]::UTF8) | ConvertFrom-Json
        return [string](@($Items) | Where-Object { [string]$_.category -eq $Category } | Select-Object -First 1).title
    }
    # Oczekiwane N obu stron z kompletem zgodnym z New-UiProbe (2 sugerowane privacy, 8 sugerowanych aplikacji).
    $script:PrivacyOk = [pscustomobject]@{ Count = 2; Detail = 'det' }
    $script:PlTexts = Get-GuiProbeTexts -Language pl
    $script:AppsOk = [pscustomobject]@{ Count = 8; Detail = 'lista' }
    # Oczekiwane tytuły strony „Naprawy i narzędzia” zgodne z New-UiProbe (RepairTexts).
    $script:RepairOk = @('Naprawa A', 'Naprawa B')
    # Oczekiwania strony „Instalacja” (Get-GuiInstallTitle) zgodne z New-UiProbe (InstallCategories, InstallTexts).
    $script:InstallOk = [pscustomobject]@{ Titles = @('Google Chrome', '7-Zip'); Categories = @('Wszystkie', 'Zaznaczone', 'Przeglądarki', 'Narzędzia') }
    # Oczekiwane liczby grup strony „Autostart” (Get-GuiAutostartExpectedCount) zgodne z New-UiProbe (AutostartGroupNames).
    $script:AutostartOk = [pscustomobject]@{ Enabled = 3; Disabled = 1; MissingFile = 0; Detail = 'cli' }
}

Describe 'VmSmokeUi (plik i skrypt sondy)' {
    It 'moduł jest w UTF-8 z BOM' {
        [System.IO.File]::ReadAllBytes($script:ModulePath)[0..2] | Should -Be @(0xEF, 0xBB, 0xBF)
    }
    It 'skrypt sondy to czyste ASCII i parsuje się bez błędów' {
        $Text = Get-GuiUiProbeScript
        [regex]::IsMatch($Text, '[^\x00-\x7F]') | Should -BeFalse
        $Errors = $null
        [void][System.Management.Automation.Language.Parser]::ParseInput($Text, [ref]$null, [ref]$Errors)
        @($Errors).Count | Should -Be 0
    }
    It 'sonda wysyła WM_NCHITTEST (0x0084) przez SendMessageTimeout z SMTO_ABORTIFHUNG' {
        $Text = Get-GuiUiProbeScript
        $Text | Should -Match 'SendMessageTimeout\(hWnd, 0x0084'
        $Text | Should -Match '0x0002, 2000'
    }
    It 'sonda klika filtry prawdziwą myszą (SetCursorPos + mouse_event), nie wzorcami UIA' {
        $Text = Get-GuiUiProbeScript
        $Text | Should -Match 'SetCursorPos\(x, y\)'
        $Text | Should -Match 'mouse_event\(0x0002'
        $Text | Should -Match 'mouse_event\(0x0004'
        $Text | Should -Match ([regex]::Escape('Test-FilterCombo $T.ProbeCategory $T.RiskCombo'))
        $Text | Should -Match ([regex]::Escape('Test-FilterCombo $T.ProbeCategory $T.ShowCombo $ShowOptionNames $R3'))
        $Text | Should -Match ([regex]::Escape('Test-FilterCombo $T.HistoryPage $T.HistoryFilter'))
        $Text | Should -Match 'ExpandCollapseState'
        $Text | Should -Not -Match '\.Expand\(\)|SendInput'
        foreach ($Field in 'FilterRiskOpened', 'FilterRiskSelected', 'FilterShowOpened', 'FilterShowSelected', 'FilterHistoryOpened', 'FilterHistorySelected', 'FilterError') { $Text | Should -Match $Field }
    }
    It 'sonda klika wiersze listy prawdziwą myszą: klik, dwuklik, znacznik uruchomienia panelu, okna mmc.exe liczone przed i po' {
        $Text = Get-GuiUiProbeScript
        foreach ($Field in 'RowClickPreview', 'RowDoubleClickToggles', 'RunIconRuns', 'RunIconDoubleClickOnce', 'RowError') { $Text | Should -Match $Field }
        $Text | Should -Match 'public static void DoubleClick\(int x, int y\)'
        $Text | Should -Match ([regex]::Escape('[DsoUser32]::DoubleClick('))
        $Text | Should -Match ([regex]::Escape("Get-Process -Name 'mmc'"))
        $Text | Should -Match ([regex]::Escape('Get-ListRow $T.ServicePage $T.PanelTitle'))
        $Text | Should -Match ([regex]::Escape('$Out.RunIconDoubleClickOnce = ($New.Count -eq 1)'))
        $Text | Should -Match ([regex]::Escape('$Out.RunIconRuns = ($New.Count -eq 1)'))
    }
    It 'dwuklik w znacznik uruchomienia czeka, aż aplikacja skończy być zajęta po pierwszym uruchomieniu panelu (IsBusy ignoruje kolejny klik)' {
        $Text = Get-GuiUiProbeScript
        $Text | Should -Match 'function Wait-AppIdle'
        $Before = $Text.Substring(0, $Text.IndexOf('[DsoUser32]::DoubleClick($IconX'))
        $Before.LastIndexOf('Wait-AppIdle 90') | Should -BeGreaterThan $Before.LastIndexOf('Close-NewMmc $BaseMmc')
    }
    It 'każde pole filtra ma osobny try/catch, a błąd niesie nazwę pola' {
        $Text = Get-GuiUiProbeScript
        $Text | Should -Match ([regex]::Escape('try { Test-FilterCombo $T.ProbeCategory $T.RiskCombo $RiskOptionNames $R1 }'))
        $Text | Should -Match ([regex]::Escape('try { Test-FilterCombo $T.ProbeCategory $T.ShowCombo $ShowOptionNames $R3 }'))
        $Text | Should -Match ([regex]::Escape('try { Test-FilterCombo $T.HistoryPage $T.HistoryFilter $HistoryOptionNames $R2 }'))
        # Przedrostek błędu to nazwa pola okna z słownika (po polsku „Pokaż:”), więc skrypt zostaje czystym ASCII.
        $Text | Should -Match ([regex]::Escape('$FilterErrors += "$($T.ShowCombo): $($_.Exception.Message)"'))
        $Text | Should -Match ([regex]::Escape('$FilterErrors += "$($T.ShowCombo): $($R3.Note)"'))
        $Text | Should -Match ([regex]::Escape('$FilterErrors += "Ryzyko: $($_.Exception.Message)"'))
        $Text | Should -Match ([regex]::Escape('$FilterErrors += "Filtr: $($_.Exception.Message)"'))
        $Text | Should -Match ([regex]::Escape('$Out.FilterError = $FilterErrors -join ''; '''))
    }
    It 'sprzątanie w finally: Esc przez keybd_event, Collapse tylko jako awaryjne sprzątanie, powrót do pierwszej opcji' {
        $Text = Get-GuiUiProbeScript
        $Text | Should -Match 'keybd_event\(vk, 0, 0, UIntPtr\.Zero\)'
        $Text | Should -Match ([regex]::Escape('[DsoUser32]::PressKey(0x1B)'))
        $Finally = $Text.Substring($Text.IndexOf('finally {'))
        $Finally | Should -Match ([regex]::Escape('Reset-FilterCombo $Combo $ComboName $Before $ExpectedNames'))
        $Reset = $Text.Substring($Text.IndexOf('function Reset-FilterCombo'), $Text.IndexOf('function Test-FilterCombo') - $Text.IndexOf('function Reset-FilterCombo'))
        $Reset | Should -Match ([regex]::Escape('$Ec.Collapse()'))
        $Reset | Should -Match 'Get-ComboOption \$Combo 0 \$ExpectedNames'
        ([regex]::Matches($Text, [regex]::Escape('.Collapse()'))).Count | Should -Be 1
    }
    It 'lista niezamknięta po wyborze opcji daje Selected=false z opisem' {
        $Text = Get-GuiUiProbeScript
        $Text | Should -Match ([regex]::Escape('$Closed = (Wait-Expanded $Combo $false)'))
        $Text | Should -Match ([regex]::Escape('if (-not $Closed) { $Result.Note = ''lista nie zamknela sie po wyborze opcji''; return }'))
        $Text | Should -Match ([regex]::Escape('$FilterErrors += "Ryzyko: $($R1.Note)"'))
    }
    It 'kandydat opcji musi mieć nazwę z oczekiwanej listy pola (Ryzyko: GuiText.RiskFilterName, Filtr: HistoryText.FilterName), skrypt ASCII' {
        $Text = Get-GuiUiProbeScript
        ([regex]::Matches($Text, [regex]::Escape('$ExpectedNames -contains [string]$_.Current.Name'))).Count | Should -Be 2
        $Text | Should -Match ([regex]::Escape('$RiskOptionNames = @($T.RiskOptions)'))
        $Text | Should -Match ([regex]::Escape('$HistoryOptionNames = @($T.HistoryOptions)'))
        $Text | Should -Match '^[\x00-\x7F]+\z'
    }
    It 'sonda szuka elementów po nazwach z okna (z pliku tekstów): menu, przycisk serii, lista historii, filtr, pasek' {
        $Text = Get-GuiUiProbeScript
        foreach ($Name in '$T.HistoryPage', '$T.ApplyOne', '$T.HistoryFilter', '$T.Minimize', '$T.Menu', '$T.TweakTitle', '$T.ProbeCategory') { $Text | Should -Match ([regex]::Escape($Name)) }
    }
    It 'cel sondy to pozycja bezpieczna bez potwierdzenia: ux.menu-delay w kategorii „Eksplorator i wygoda”' {
        $Pl = Get-GuiProbeTexts -Language pl
        $Pl.ProbeCategory | Should -Be 'Eksplorator i wygoda'
        $Pl.TweakTitle | Should -Be 'Szybsze otwieranie podmenu'
    }
}

Describe 'ConvertTo-DsoHitTestRange' {
    It 'łączy kolejne Y z tym samym kodem w zakresy' {
        $Scan = @(-2, -1, 0, 1, 2) | ForEach-Object { [pscustomobject]@{ Y = $_; Ht = $(if ($_ -lt 0) { 12 } elseif ($_ -lt 2) { 2 } else { 1 }) } }
        ConvertTo-DsoHitTestRange -Scan $Scan | Should -Be @('-2..-1: 12', '0..1: 2', '2..2: 1')
    }
    It 'przerwa w Y zaczyna nowy zakres nawet przy tym samym kodzie' {
        $Scan = @([pscustomobject]@{ Y = 1; Ht = 2 }, [pscustomobject]@{ Y = 3; Ht = 2 })
        ConvertTo-DsoHitTestRange -Scan $Scan | Should -Be @('1..1: 2', '3..3: 2')
    }
    It 'pusty i $null przekrój dają pustą listę' {
        @(ConvertTo-DsoHitTestRange -Scan @()).Count | Should -Be 0
        @(ConvertTo-DsoHitTestRange -Scan $null).Count | Should -Be 0
    }
}

Describe 'Sonda: sugestie „Prywatność i AI” (krok 7)' {
    BeforeAll {
        $script:ProbeText = Get-GuiUiProbeScript
        $script:PrivacyName = '$T.PrivacyCategory'
    }
    It 'nazwa kategorii z pliku tekstów występuje w skrypcie dokładnie raz' {
        ([regex]::Matches($script:ProbeText, [regex]::Escape($script:PrivacyName))).Count | Should -Be 1
    }
    It 'sonda wchodzi do „Prywatność i AI” po ścieżce ux.menu-delay („Zastosuj (1)”) i po sprzątaniu kroku 6' {
        $Privacy = $script:ProbeText.IndexOf($script:PrivacyName)
        $Privacy | Should -BeGreaterThan $script:ProbeText.IndexOf('$T.ApplyOne')
        $Privacy | Should -BeGreaterThan $script:ProbeText.IndexOf('$Out.RowDoubleClickToggles =')
        $Privacy | Should -BeGreaterThan $script:ProbeText.IndexOf('finally { try { Close-NewMmc $BaseMmc; Wait-AppIdle 90 }')
        $Privacy | Should -BeGreaterThan $script:ProbeText.IndexOf('# 7.')
        $script:ProbeText.IndexOf('# 7.') | Should -BeGreaterThan $script:ProbeText.IndexOf('# 6.')
    }
    It 'sonda czyta N z przycisku „Zastosuj (N)” i czeka na stałą wartość przy bezczynnej aplikacji' {
        $script:ProbeText | Should -Match ([regex]::Escape('$ApplyPattern = $T.ApplyPattern'))
        foreach ($Field in 'PrivacyApplyCount', 'PrivacyApplyText', 'PrivacyButtonFound', 'PrivacyError') { $script:ProbeText | Should -Match $Field }
        $Step = $script:ProbeText.Substring($script:ProbeText.IndexOf('# 7.'), $script:ProbeText.IndexOf('# 8.') - $script:ProbeText.IndexOf('# 7.'))
        $Step | Should -Match 'Wait-AppIdle'
        $Step | Should -Not -Match 'Toggle\(\)'
    }
    It 'krok 7 czyta N przy wejściu, potem klika tylko „Zaznacz sugerowane” (InvokePattern, jedno Invoke) i czyta N końcowe' {
        $Step = $script:ProbeText.Substring($script:ProbeText.IndexOf('# 7.'), $script:ProbeText.IndexOf('# 8.') - $script:ProbeText.IndexOf('# 7.'))
        $Step | Should -Match 'PrivacyApplyCountOnEntry'
        $Step | Should -Match ([regex]::Escape('$T.SelectSuggested'))
        $Step | Should -Match 'InvokePattern'
        ([regex]::Matches($Step, 'Invoke\(\)')).Count | Should -Be 1
        $Step.IndexOf('PrivacyApplyCountOnEntry') | Should -BeLessThan $Step.IndexOf('InvokePattern')
        $Step.IndexOf('InvokePattern') | Should -BeLessThan $Step.IndexOf('$Out.PrivacyApplyCount = ')
    }
    It 'sonda i moduł nie odwołują się do usuniętego samego zaznaczania (SuggestedSelection, AppSuggestionTracker)' {
        $script:ProbeText | Should -Not -Match 'SuggestedSelection|AppSuggestionTracker|pierwsze wejscie|pierwsze wejście'
        [System.IO.File]::ReadAllText($script:ModulePath, [System.Text.Encoding]::UTF8) | Should -Not -Match 'SuggestedSelection|AppSuggestionTracker'
    }
    It 'krok 7 ma własny try/catch (błąd nie gubi wyniku wcześniejszych kroków)' {
        $script:ProbeText | Should -Match ([regex]::Escape('catch { $Out.PrivacyError = $_.Exception.Message'))
    }
}

Describe 'Get-GuiPrivacySuggestedId' {
    It 'z katalogu bierze tylko tweaki kategorii privacy z suggested=true' {
        $Dir = Join-Path $TestDrive 'catalog'
        $null = New-Item -ItemType Directory -Path $Dir -Force
        $Items = @(
            [ordered]@{ id = 'privacy.a'; category = 'privacy'; kind = 'tweak'; suggested = $true }
            [ordered]@{ id = 'privacy.b'; category = 'privacy'; kind = 'tweak'; suggested = $false }
            [ordered]@{ id = 'privacy.c'; category = 'privacy'; kind = 'tweak' }
            [ordered]@{ id = 'privacy.d'; category = 'privacy'; kind = 'tweak'; suggested = $true }
        )
        $Other = @([ordered]@{ id = 'ux.e'; category = 'ux'; kind = 'tweak'; suggested = $true })
        [System.IO.File]::WriteAllText((Join-Path $Dir 'privacy.json'), (ConvertTo-Json -InputObject $Items -Depth 4), (New-Object System.Text.UTF8Encoding($true)))
        [System.IO.File]::WriteAllText((Join-Path $Dir 'ux.json'), (ConvertTo-Json -InputObject $Other -Depth 4), (New-Object System.Text.UTF8Encoding($true)))
        Get-GuiPrivacySuggestedId -CatalogDirectory $Dir | Should -Be @('privacy.a', 'privacy.d')
    }
    It 'pomija apps-bloat.json (klasyfikacja aplikacji, inny format niż katalog tweaków)' {
        $Dir = Join-Path $TestDrive 'catalog-apps'
        $null = New-Item -ItemType Directory -Path $Dir -Force
        $Items = @([ordered]@{ id = 'privacy.a'; category = 'privacy'; kind = 'tweak'; suggested = $true })
        $Apps = [ordered]@{
            entries   = @([ordered]@{ id = 'ms.news'; match = @{ packageName = 'Microsoft.BingNews' }; group = 'microsoft'; suggested = $true; summary = 'x'; source = 'y' })
            protected = @([ordered]@{ id = 'prot.calc'; match = @{ packageName = 'Microsoft.WindowsCalculator' }; reason = 'z' })
        }
        [System.IO.File]::WriteAllText((Join-Path $Dir 'privacy.json'), (ConvertTo-Json -InputObject $Items -Depth 4), (New-Object System.Text.UTF8Encoding($true)))
        [System.IO.File]::WriteAllText((Join-Path $Dir 'apps-bloat.json'), (ConvertTo-Json -InputObject $Apps -Depth 6), (New-Object System.Text.UTF8Encoding($true)))
        Get-GuiPrivacySuggestedId -CatalogDirectory $Dir | Should -Be @('privacy.a')
    }
    It 'pomija apps.json po nazwie pliku (lista instalacji, inny format niż katalog tweaków)' {
        $Dir = Join-Path $TestDrive 'catalog-apps-install'
        $null = New-Item -ItemType Directory -Path $Dir -Force
        $Items = @([ordered]@{ id = 'privacy.a'; category = 'privacy'; kind = 'tweak'; suggested = $true })
        # Treść celowo wygląda jak pozycja katalogu: filtr ma działać po nazwie pliku, nie po kształcie treści.
        $Install = @([ordered]@{ id = 'install.x'; category = 'privacy'; kind = 'tweak'; suggested = $true })
        [System.IO.File]::WriteAllText((Join-Path $Dir 'privacy.json'), (ConvertTo-Json -InputObject $Items -Depth 4), (New-Object System.Text.UTF8Encoding($true)))
        [System.IO.File]::WriteAllText((Join-Path $Dir 'apps.json'), (ConvertTo-Json -InputObject $Install -Depth 4), (New-Object System.Text.UTF8Encoding($true)))
        Get-GuiPrivacySuggestedId -CatalogDirectory $Dir | Should -Be @('privacy.a')
    }
    It 'pomija autostart-known.json po nazwie pliku (baza znanych pozycji autostartu, inny format niż katalog tweaków)' {
        $Dir = Join-Path $TestDrive 'catalog-autostart'
        $null = New-Item -ItemType Directory -Path $Dir -Force
        $Items = @([ordered]@{ id = 'privacy.a'; category = 'privacy'; kind = 'tweak'; suggested = $true })
        # Treść celowo wygląda jak pozycja katalogu: filtr ma działać po nazwie pliku, nie po kształcie treści.
        $Known = @([ordered]@{ id = 'steam'; category = 'privacy'; kind = 'tweak'; suggested = $true })
        [System.IO.File]::WriteAllText((Join-Path $Dir 'privacy.json'), (ConvertTo-Json -InputObject $Items -Depth 4), (New-Object System.Text.UTF8Encoding($true)))
        [System.IO.File]::WriteAllText((Join-Path $Dir 'autostart-known.json'), (ConvertTo-Json -InputObject $Known -Depth 4), (New-Object System.Text.UTF8Encoding($true)))
        Get-GuiPrivacySuggestedId -CatalogDirectory $Dir | Should -Be @('privacy.a')
    }
    It 'wbudowany katalog: 14 sugerowanych pozycji privacy, bez „Proponowane” w Start' {
        $Dir = Join-Path $PSScriptRoot '..\..\..\src\DSOptimizer.Catalog\catalog'
        $Ids = @(Get-GuiPrivacySuggestedId -CatalogDirectory $Dir)
        $Ids.Count | Should -Be 14
        $Ids | Should -Contain 'privacy.advertising-id'
        $Ids | Should -Contain 'privacy.tailored-experiences'
        $Ids | Should -Contain 'privacy.diagtrack'
        $Ids | Should -Not -Contain 'privacy.start-recommended-section'
        $Ids | Should -Not -Contain 'privacy.inking-typing-improve'
        $Ids | Should -Not -Contain 'privacy.start-track-progs'
    }
    It 'pusty wynik to błąd (sprawdzenie nie miałoby czego liczyć)' {
        $Dir = Join-Path $TestDrive 'pusty'
        $null = New-Item -ItemType Directory -Path $Dir -Force
        { Get-GuiPrivacySuggestedId -CatalogDirectory $Dir } | Should -Throw '*sugerowanych*'
    }
}

Describe 'Get-GuiPrivacyExpectedCount (N z raportów detekcji)' {
    BeforeAll {
        function script:New-Detection([string]$Id, [AllowNull()][string]$State, [bool]$Certain = $true, [string]$Err = $null) {
            [pscustomobject]@{ Id = $Id; ExitCode = 0; State = $State; UserSid = 'S-1-5-21-1'; IsCertain = $Certain; Error = $Err }
        }
    }
    It 'liczy NotApplied i Partial, pomija Applied' {
        $Detections = @((New-Detection 'p.a' 'NotApplied'), (New-Detection 'p.b' 'Applied'), (New-Detection 'p.c' 'Partial'))
        $Expected = Get-GuiPrivacyExpectedCount -SuggestedId @('p.a', 'p.b', 'p.c') -Detection $Detections
        $Expected.Count | Should -Be 2
        $Expected.Detail | Should -BeLike '*p.a=NotApplied*p.b=Applied*p.c=Partial*'
    }
    It 'wszystkie Applied daje 0' {
        (Get-GuiPrivacyExpectedCount -SuggestedId @('p.a') -Detection @(New-Detection 'p.a' 'Applied')).Count | Should -Be 0
    }
    It 'brak N (<Powod>)' -ForEach @(
        @{ Powod = 'brak stanu w raporcie'; Detections = { @((New-Detection 'p.a' 'NotApplied'), (New-Detection 'p.b' $null -Err 'brak raportu')) }; Like = '*p.b*brak raportu*' }
        @{ Powod = 'brak raportu dla pozycji'; Detections = { @(New-Detection 'p.a' 'NotApplied') }; Like = '*p.b*' }
        @{ Powod = 'użytkownik niepewny (detekcja HKCU na koncie Administratora)'; Detections = { @((New-Detection 'p.a' 'NotApplied'), (New-Detection 'p.b' 'Applied' $false)) }; Like = '*p.b*niepewny*' }
        @{ Powod = 'stan inny niż Applied/NotApplied/Partial'; Detections = { @((New-Detection 'p.a' 'NotApplied'), (New-Detection 'p.b' 'Unknown')) }; Like = '*p.b=Unknown*' }
    ) {
        $Expected = Get-GuiPrivacyExpectedCount -SuggestedId @('p.a', 'p.b') -Detection (& $Detections)
        $Expected.Count | Should -BeNullOrEmpty
        $Expected.Detail | Should -BeLike $Like
    }
}

Describe 'Get-GuiUiCheck: PrivacySuggestedApply' {
    It 'przechodzi, gdy przycisk pokazuje N z detekcji' {
        $Check = Get-Check (Get-GuiUiCheck -Texts $script:PlTexts -Probe (New-UiProbe @{}) -PrivacyExpected ([pscustomobject]@{ Count = 2; Detail = 'det' })) 'PrivacySuggestedApply'
        $Check.Passed | Should -BeTrue
        $Check.Detail | Should -BeLike "*Zastosuj (2)*oczekiwano 2*det*"
    }
    It 'nie przechodzi, gdy N z przycisku różni się od detekcji (opis niesie obie liczby)' {
        $Check = Get-Check (Get-GuiUiCheck -Texts $script:PlTexts -Probe (New-UiProbe @{ PrivacyApplyCount = 3; PrivacyApplyText = 'Zastosuj (3)' }) -PrivacyExpected ([pscustomobject]@{ Count = 2; Detail = 'det' })) 'PrivacySuggestedApply'
        $Check.Passed | Should -BeFalse
        $Check.Detail | Should -BeLike '*Zastosuj (3)*oczekiwano 2*'
    }
    It 'nie przechodzi bez oczekiwanego N (brak lub błąd detekcji)' {
        $Check = Get-Check (Get-GuiUiCheck -Texts $script:PlTexts -Probe (New-UiProbe @{}) -PrivacyExpected ([pscustomobject]@{ Count = $null; Detail = 'p.b: brak raportu' })) 'PrivacySuggestedApply'
        $Check.Passed | Should -BeFalse
        $Check.Detail | Should -BeLike '*p.b: brak raportu*'
        (Get-Check (Get-GuiUiCheck -Texts $script:PlTexts -Probe (New-UiProbe @{})) 'PrivacySuggestedApply').Passed | Should -BeFalse
    }
    It 'nie przechodzi, gdy sonda nie ma pola wyniku (stara sonda) albo nie znalazła przycisku przy N > 0' {
        $Probe = New-UiProbe @{}
        $Without = [pscustomobject]@{}
        foreach ($P in $Probe.PSObject.Properties | Where-Object { $_.Name -notlike 'Privacy*' }) { $Without | Add-Member -NotePropertyName $P.Name -NotePropertyValue $P.Value }
        $Expected = [pscustomobject]@{ Count = 2; Detail = 'det' }
        (Get-Check (Get-GuiUiCheck -Texts $script:PlTexts -Probe $Without -PrivacyExpected $Expected) 'PrivacySuggestedApply').Passed | Should -BeFalse
        $NoButton = New-UiProbe @{ PrivacyButtonFound = $false; PrivacyApplyCount = $null; PrivacyApplyText = $null; PrivacyError = 'brak przycisku' }
        $Check = Get-Check (Get-GuiUiCheck -Texts $script:PlTexts -Probe $NoButton -PrivacyExpected $Expected) 'PrivacySuggestedApply'
        $Check.Passed | Should -BeFalse
        $Check.Detail | Should -BeLike '*brak przycisku*'
    }
    It 'PrivacySuggestedApply: N przy wejściu większe od 0 = niezaliczone' {
        $Check = Get-Check (Get-GuiUiCheck -Texts $script:PlTexts -Probe (New-UiProbe @{ PrivacyApplyCountOnEntry = 1 }) -PrivacyExpected $script:PrivacyOk) 'PrivacySuggestedApply'
        $Check.Passed | Should -BeFalse
        $Check.Detail | Should -BeLike '*przy wejściu 1*'
    }
    It 'PrivacySuggestedApply: brak pola N przy wejściu (stara sonda) = niezaliczone' {
        $Probe = New-UiProbe @{}
        $Without = [pscustomobject]@{}
        foreach ($P in $Probe.PSObject.Properties | Where-Object { $_.Name -ne 'PrivacyApplyCountOnEntry' }) { $Without | Add-Member -NotePropertyName $P.Name -NotePropertyValue $P.Value }
        (Get-Check (Get-GuiUiCheck -Texts $script:PlTexts -Probe $Without -PrivacyExpected $script:PrivacyOk) 'PrivacySuggestedApply').Passed | Should -BeFalse
    }
    It 'oczekiwane N = 0 przechodzi bez kliknięcia przycisku (brak przycisku „Zaznacz sugerowane”, N przy wejściu 0)' {
        $Zero = [pscustomobject]@{ Count = 0; Detail = 'det' }
        $Probe = New-UiProbe @{ PrivacyButtonFound = $false; PrivacyApplyCountOnEntry = 0; PrivacyApplyCount = $null; PrivacyApplyText = $null }
        (Get-Check (Get-GuiUiCheck -Texts $script:PlTexts -Probe $Probe -PrivacyExpected $Zero) 'PrivacySuggestedApply').Passed | Should -BeTrue
    }
    It 'N = 0: przechodzi przy „Zastosuj (0)” albo przy braku przycisku bez błędu (pasek serii ukryty)' {
        $Zero = [pscustomobject]@{ Count = 0; Detail = 'det' }
        (Get-Check (Get-GuiUiCheck -Texts $script:PlTexts -Probe (New-UiProbe @{ PrivacyApplyCount = 0; PrivacyApplyText = 'Zastosuj (0)' }) -PrivacyExpected $Zero) 'PrivacySuggestedApply').Passed | Should -BeTrue
        (Get-Check (Get-GuiUiCheck -Texts $script:PlTexts -Probe (New-UiProbe @{ PrivacyButtonFound = $false; PrivacyApplyCount = $null; PrivacyApplyText = $null }) -PrivacyExpected $Zero) 'PrivacySuggestedApply').Passed | Should -BeTrue
    }
}

Describe 'Sonda: „Aplikacje” i „Usuń (N)” (krok 8)' {
    BeforeAll {
        $script:ProbeText = Get-GuiUiProbeScript
        $Start = $script:ProbeText.IndexOf('# 8.')
        $script:Step8 = $script:ProbeText.Substring($Start, $script:ProbeText.IndexOf('# 9.') - $Start)
    }
    It 'krok 8 jest po kroku 7 („Prywatność i AI”) i jego odczycie N' {
        $script:ProbeText.IndexOf('# 8.') | Should -BeGreaterThan $script:ProbeText.IndexOf('# 7.')
        $script:ProbeText.IndexOf('# 8.') | Should -BeGreaterThan $script:ProbeText.IndexOf('$Out.PrivacyApplyCount = [int]$Matches[1]')
    }
    It 'wchodzi na pozycję menu „Aplikacje” i czyta N z „Usuń (N)” (teksty z pliku, skrypt ASCII)' {
        $script:Step8 | Should -Match ([regex]::Escape('Find-Element $Root $T.AppsPage $ListItem'))
        $script:Step8 | Should -Match ([regex]::Escape('$RemovePattern = $T.RemovePattern'))
        foreach ($Field in 'AppsListRead', 'AppsButtonFound', 'AppsRemoveCount', 'AppsRemoveText', 'AppsError') { $script:ProbeText | Should -Match $Field }
    }
    It 'czeka na koniec odczytu listy („Odśwież listę” aktywne, bez „Odczyt listy w toku”/„jeszcze nie odczytana”) i na stałą wartość' {
        $script:Step8 | Should -Match ([regex]::Escape('$T.AppsRefresh'))
        $script:Step8 | Should -Match ([regex]::Escape('$T.AppsReading'))
        $script:Step8 | Should -Match ([regex]::Escape('$T.AppsNotRead'))
        $script:Step8 | Should -Match 'IsEnabled'
        $script:Step8 | Should -Match 'TotalSeconds -ge 3'
    }
    It 'czyta N przy wejściu i klika tylko „Zaznacz sugerowane” (jedno Invoke, bez „Usuń”, bez pól wyboru, bez myszy)' {
        $script:Step8 | Should -Match 'AppsRemoveCountOnEntry'
        $script:Step8 | Should -Match ([regex]::Escape('$T.SelectSuggested'))
        $script:Step8 | Should -Match 'InvokePattern'
        ([regex]::Matches($script:Step8, 'Invoke\(\)')).Count | Should -Be 1
        $script:Step8.IndexOf('AppsRemoveCountOnEntry') | Should -BeLessThan $script:Step8.IndexOf('InvokePattern')
        $script:Step8 | Should -Not -Match 'Toggle\(\)'
        $script:Step8 | Should -Not -Match 'DsoUser32\]::(Click|DoubleClick|PressKey)'
    }
    It 'krok 8 ma własny try/catch (błąd nie gubi wyniku wcześniejszych kroków)' {
        $script:ProbeText | Should -Match ([regex]::Escape('catch { $Out.AppsError = $_.Exception.Message'))
    }
}

Describe 'Get-GuiAppsExpectedCount (N z --list-apps)' {
    BeforeAll {
        function script:New-Listing([object[]]$Apps, [int]$ExitCode = 0, [bool]$TimedOut = $false, [bool]$WithApps = $true) {
            $Report = [pscustomobject]@{ exitCode = $ExitCode; user = [pscustomobject]@{ sid = 'S-1-5-21-7'; isCertain = $true } }
            if ($WithApps) { $Report | Add-Member -NotePropertyName apps -NotePropertyValue @($Apps) }
            [pscustomobject]@{ ExitCode = $ExitCode; TimedOut = $TimedOut; Report = $Report; Json = 'x'; ReportError = $null }
        }
        function script:New-AppInfo([string]$Name, [string]$Group, [string]$Removability) {
            [pscustomobject]@{ id = "appx:$Name"; kind = 'Store'; name = $Name; publisher = 'P'; group = $Group; removability = $Removability }
        }
    }
    It 'liczy tylko wiersze grupy Suggested z Removability=Removable (AppRow.CanRemove)' {
        $Apps = @(
            (New-AppInfo 'Xbox' 'Suggested' 'Removable')
            (New-AppInfo 'Copilot' 'Suggested' 'Removable')
            (New-AppInfo 'Inny' 'Suggested' 'OtherAccount')
            (New-AppInfo 'Niepewny' 'Suggested' 'UserUncertain')
            (New-AppInfo 'Notatnik' 'Other' 'Removable')
            (New-AppInfo 'Kalkulator' 'Protected' 'Protected')
        )
        $Expected = Get-GuiAppsExpectedCount -Listing (New-Listing $Apps)
        $Expected.Count | Should -Be 2
        $Expected.Detail | Should -BeLike '*Xbox*Copilot*'
        $Expected.Detail | Should -Not -BeLike '*Notatnik*'
        $Expected.Detail | Should -BeLike '*S-1-5-21-7*'
    }
    It 'lista bez sugerowanych usuwalnych daje 0' {
        (Get-GuiAppsExpectedCount -Listing (New-Listing @((New-AppInfo 'Notatnik' 'Other' 'Removable')))).Count | Should -Be 0
    }
    It 'brak N (<Powod>)' -ForEach @(
        @{ Powod = 'brak wyniku CLI'; Listing = { $null }; Like = '*brak wyniku*' }
        @{ Powod = 'limit czasu CLI'; Listing = { New-Listing @() -ExitCode -1 -TimedOut $true }; Like = '*limit czasu*' }
        @{ Powod = 'kod wyjścia CLI różny od 0'; Listing = { New-Listing @() -ExitCode 4 }; Like = '*kod 4*' }
        @{ Powod = 'brak raportu (zły JSON)'; Listing = { [pscustomobject]@{ ExitCode = 0; TimedOut = $false; Report = $null; Json = '{zly'; ReportError = 'zły JSON' } }; Like = '*brak raportu*zły JSON*' }
        @{ Powod = 'raport bez pola apps'; Listing = { New-Listing @() -WithApps $false }; Like = '*bez listy aplikacji*' }
    ) {
        $Expected = Get-GuiAppsExpectedCount -Listing (& $Listing)
        $Expected.Count | Should -BeNullOrEmpty
        $Expected.Detail | Should -BeLike $Like
    }
}

Describe 'Get-GuiUiCheck: AppsSuggestedRemove' {
    It 'przechodzi, gdy „Usuń (N)” pokazuje N z --list-apps' {
        $Check = Get-Check (Get-GuiUiCheck -Texts $script:PlTexts -Probe (New-UiProbe @{}) -PrivacyExpected $script:PrivacyOk -AppsExpected $script:AppsOk) 'AppsSuggestedRemove'
        $Check.Passed | Should -BeTrue
        $Check.Detail | Should -BeLike '*Usuń (8)*oczekiwano 8*lista*'
    }
    It 'nie przechodzi, gdy N z przycisku różni się od listy (opis niesie obie liczby)' {
        $Check = Get-Check (Get-GuiUiCheck -Texts $script:PlTexts -Probe (New-UiProbe @{ AppsRemoveCount = 7; AppsRemoveText = 'Usuń (7)' }) -AppsExpected $script:AppsOk) 'AppsSuggestedRemove'
        $Check.Passed | Should -BeFalse
        $Check.Detail | Should -BeLike '*Usuń (7)*oczekiwano 8*'
    }
    It 'nie przechodzi bez oczekiwanego N (brak lub błąd --list-apps)' {
        $Check = Get-Check (Get-GuiUiCheck -Texts $script:PlTexts -Probe (New-UiProbe @{}) -AppsExpected ([pscustomobject]@{ Count = $null; Detail = 'kod 4' })) 'AppsSuggestedRemove'
        $Check.Passed | Should -BeFalse
        $Check.Detail | Should -BeLike '*kod 4*'
        (Get-Check (Get-GuiUiCheck -Texts $script:PlTexts -Probe (New-UiProbe @{})) 'AppsSuggestedRemove').Passed | Should -BeFalse
    }
    It 'nie przechodzi, gdy lista nie została odczytana do końca (N sprzed odczytu)' {
        $Check = Get-Check (Get-GuiUiCheck -Texts $script:PlTexts -Probe (New-UiProbe @{ AppsListRead = $false; AppsError = 'odczyt listy nie skonczyl sie w 180 s' }) -AppsExpected $script:AppsOk) 'AppsSuggestedRemove'
        $Check.Passed | Should -BeFalse
        $Check.Detail | Should -BeLike '*sonda: odczyt listy*'
    }
    It 'nie przechodzi, gdy sonda nie ma pola wyniku (stara sonda) albo nie znalazła przycisku – także przy N = 0 (pasek serii Aplikacji jest zawsze widoczny)' {
        $Probe = New-UiProbe @{}
        $Without = [pscustomobject]@{}
        foreach ($P in $Probe.PSObject.Properties | Where-Object { $_.Name -notlike 'Apps*' }) { $Without | Add-Member -NotePropertyName $P.Name -NotePropertyValue $P.Value }
        (Get-Check (Get-GuiUiCheck -Texts $script:PlTexts -Probe $Without -AppsExpected $script:AppsOk) 'AppsSuggestedRemove').Passed | Should -BeFalse
        $NoButton = New-UiProbe @{ AppsButtonFound = $false; AppsRemoveCount = $null; AppsRemoveText = $null }
        $Zero = [pscustomobject]@{ Count = 0; Detail = 'lista' }
        $Check = Get-Check (Get-GuiUiCheck -Texts $script:PlTexts -Probe $NoButton -AppsExpected $Zero) 'AppsSuggestedRemove'
        $Check.Passed | Should -BeFalse
        $Check.Detail | Should -BeLike '*brak przycisku*'
    }
    It 'AppsSuggestedRemove: N przy wejściu większe od 0 = niezaliczone' {
        $Check = Get-Check (Get-GuiUiCheck -Texts $script:PlTexts -Probe (New-UiProbe @{ AppsRemoveCountOnEntry = 2 }) -AppsExpected $script:AppsOk) 'AppsSuggestedRemove'
        $Check.Passed | Should -BeFalse
        $Check.Detail | Should -BeLike '*przy wejściu 2*'
    }
    It 'AppsSuggestedRemove: brak N przy wejściu (null) = niezaliczone' {
        (Get-Check (Get-GuiUiCheck -Texts $script:PlTexts -Probe (New-UiProbe @{ AppsRemoveCountOnEntry = $null }) -AppsExpected $script:AppsOk) 'AppsSuggestedRemove').Passed | Should -BeFalse
    }
    It 'N = 0: przechodzi przy „Usuń (0)”' {
        $Zero = [pscustomobject]@{ Count = 0; Detail = 'lista' }
        (Get-Check (Get-GuiUiCheck -Texts $script:PlTexts -Probe (New-UiProbe @{ AppsRemoveCount = 0; AppsRemoveText = 'Usuń (0)' }) -AppsExpected $Zero) 'AppsSuggestedRemove').Passed | Should -BeTrue
    }
}

Describe 'Sonda: „Przygotowanie” (krok 9)' {
    BeforeAll {
        $script:ProbeText = Get-GuiUiProbeScript
        $script:Step9 = $script:ProbeText.Substring($script:ProbeText.IndexOf('# 9.'))
        if ($script:ProbeText.IndexOf('# 10.') -gt $script:ProbeText.IndexOf('# 9.')) {
            $script:Step9 = $script:ProbeText.Substring($script:ProbeText.IndexOf('# 9.'), $script:ProbeText.IndexOf('# 10.') - $script:ProbeText.IndexOf('# 9.'))
        }
        function script:Get-PrepConstant([string]$Pattern) {
            return @(Get-CsLiteral (Read-CoreSource 'Prep\PrepText.cs') $Pattern)
        }
    }
    It 'krok 9 jest po kroku 8 („Aplikacje”) i jego odczycie N, a przed krokiem 10 („Naprawy i narzędzia”)' {
        $script:ProbeText.IndexOf('# 9.') | Should -BeGreaterThan $script:ProbeText.IndexOf('$Out.AppsRemoveCount = [int]$Matches[1]')
        $script:Step9 | Should -Not -Match '# 10\.'
        $script:ProbeText.IndexOf('# 10.') | Should -BeGreaterThan $script:ProbeText.IndexOf('catch { $Out.PrepError = $_.Exception.Message')
    }
    It 'wchodzi na pozycję menu „Przygotowanie” wzorcem SelectionItemPattern' {
        $script:Step9 | Should -Match ([regex]::Escape('Find-Element $Root $T.PrepPage $ListItem'))
        $script:Step9 | Should -Match ([regex]::Escape('[System.Windows.Automation.SelectionItemPattern]::Pattern).Select()'))
    }
    It 'tytuły kroków hosta = tytuły z PrepText.cs (7, w kolejności), a sonda bierze je z pliku tekstów' {
        $Expected = @(Get-PrepConstant 'public (?:static|const) string (?!Page)\w+Title\s*(?:=>|=)\s*(?:L\.T\(\s*)?')
        $Expected.Count | Should -Be 7
        (Get-GuiPrepExpected).Titles | Should -Be $Expected
        @((Get-GuiProbeTexts -Language pl).PrepTitles) | Should -Be $Expected
        (Get-GuiProbeTexts -Language pl).PrepGraphicsTitle | Should -Be (Get-CsText (Read-CoreSource 'Prep\PrepText.cs') 'GraphicsTitle')
        $script:Step9 | Should -Match ([regex]::Escape('$PrepTitleNames = @($T.PrepTitles)'))
        $script:Step9 | Should -Match ([regex]::Escape('[array]::IndexOf($PrepTexts, [string]$T.PrepGraphicsTitle)'))
    }
    It 'Windows Update to jedyny tytuł kroku bez tłumaczenia: stała const w PrepText.cs' {
        (Read-CoreSource 'Prep\PrepText.cs') | Should -Match ([regex]::Escape('public const string WindowsUpdateTitle = "Windows Update";'))
        @((Get-GuiProbeTexts -Language en).PrepTitles)[1] | Should -Be 'Windows Update'
    }
    It 'czeka najwyżej 60 s, aż zniknie „Wczytywanie…” (PrepText.Loading) i pojawi się 7 tytułów' {
        $script:Step9 | Should -Match ([regex]::Escape('$T.PrepLoading'))
        $script:Step9 | Should -Match ([regex]::Escape('AddSeconds(60)'))
        foreach ($Field in 'PrepMenuFound', 'PrepLoaded', 'PrepTitles') { $script:Step9 | Should -Match $Field }
    }
    It 'szuka przycisku „Otwórz Windows Update” i notatki zastępczej (teksty z PrepText.cs)' {
        $PrepSource = Read-CoreSource 'Prep\PrepText.cs'
        $Pl = Get-GuiProbeTexts -Language pl
        $Pl.PrepOpenWindowsUpdate | Should -Be (Get-CsText $PrepSource 'OpenWindowsUpdate')
        $Pl.PrepWindowsUpdateNote | Should -Be (Get-CsText $PrepSource 'WindowsUpdateNote')
        $script:Step9 | Should -Match ([regex]::Escape('$T.PrepOpenWindowsUpdate'))
        $script:Step9 | Should -Match ([regex]::Escape('$T.PrepWindowsUpdateNote'))
        foreach ($Field in 'PrepWindowsUpdateButton', 'PrepWindowsUpdateNote') { $script:Step9 | Should -Match $Field }
    }
    It 'zbiera teksty kroku „Grafika” do następnego tytułu, bez numerów kroków i bez napisów przycisków' {
        $script:Step9 | Should -Match 'PrepGraphicsLines'
        $script:Step9 | Should -Match ([regex]::Escape('ControlType]::Button'))
        $script:Step9 | Should -Match ([regex]::Escape("-match '^\d+$'"))
    }
    It 'niczego nie klika ani nie przełącza (bez linków, bez myszy)' {
        $script:Step9 | Should -Not -Match 'Invoke\(\)'
        $script:Step9 | Should -Not -Match 'Toggle\(\)'
        $script:Step9 | Should -Not -Match 'DsoUser32\]::(Click|DoubleClick|PressKey)'
    }
    It 'krok 9 ma własny try/catch (błąd nie gubi wyniku wcześniejszych kroków)' {
        $script:ProbeText | Should -Match ([regex]::Escape('catch { $Out.PrepError = $_.Exception.Message'))
    }
    It 'SettingsOpenable hosta = PrepLinks.SettingsOpenable w Core' {
        $Path = Join-Path $PSScriptRoot '..\..\..\src\DSOptimizer.Core\Prep\PrepLinks.cs'
        $Source = [System.IO.File]::ReadAllText($Path, [System.Text.Encoding]::UTF8)
        $Match = [regex]::Match($Source, 'public static bool SettingsOpenable \{ get; \} = (true|false);')
        $Match.Success | Should -BeTrue
        (Get-GuiPrepExpected).SettingsOpenable | Should -Be ($Match.Groups[1].Value -eq 'true')
    }
}

Describe 'Get-GuiUiCheck: PrepPage' {
    BeforeAll {
        function script:Get-PrepCheck($Probe, [bool]$SettingsOpenable = $true) {
            Get-Check (Get-GuiUiCheck -Texts $script:PlTexts -Probe $Probe -PrivacyExpected $script:PrivacyOk -AppsExpected $script:AppsOk -SettingsOpenable $SettingsOpenable) 'PrepPage'
        }
    }
    It 'przechodzi: odczyt skończony, 7 tytułów, przycisk Windows Update, linia karty w kroku „Grafika”' {
        $Check = Get-PrepCheck (New-UiProbe @{})
        $Check.Passed | Should -BeTrue
        $Check.Detail | Should -BeLike '*tytuły 7/7*Otwórz Windows Update*Microsoft Hyper-V Video*'
    }
    It 'nie przechodzi, gdy <Powod>' -ForEach @(
        @{ Powod = 'brak pozycji menu'; Override = @{ PrepMenuFound = $false }; Like = '*menu*' }
        @{ Powod = 'odczyt nie skończył się w 60 s'; Override = @{ PrepLoaded = $false }; Like = '*Wczytywanie*' }
        @{ Powod = 'brakuje tytułu kroku'; Override = @{ PrepTitles = @('Komputer', 'Windows Update') }; Like = '*tytuły 2/7*brak: Opcjonalne*' }
        @{ Powod = 'brak przycisku Windows Update (SettingsOpenable)'; Override = @{ PrepWindowsUpdateButton = $false }; Like = '*NIE: przycisk*' }
        @{ Powod = 'krok „Grafika” bez linii'; Override = @{ PrepGraphicsLines = @() }; Like = '*Grafika: brak linii karty*' }
        @{ Powod = 'krok „Grafika” pokazuje tylko „Karty graficzne” (brak karty albo błąd odczytu)'; Override = @{ PrepGraphicsLines = @('Karty graficzne', 'Nie znaleziono karty graficznej') }; Like = '*Grafika: brak linii karty*' }
        @{ Powod = 'sonda zgłosiła błąd kroku'; Override = @{ PrepError = 'Brak pozycji menu' }; Like = '*sonda: Brak pozycji menu*' }
    ) {
        $Check = Get-PrepCheck (New-UiProbe $Override)
        $Check.Passed | Should -BeFalse
        $Check.Detail | Should -BeLike $Like
    }
    It 'SettingsOpenable = false: wymagana notatka zastępcza zamiast przycisku' {
        (Get-PrepCheck (New-UiProbe @{ PrepWindowsUpdateButton = $false; PrepWindowsUpdateNote = $true }) $false).Passed | Should -BeTrue
        $Check = Get-PrepCheck (New-UiProbe @{ PrepWindowsUpdateButton = $true; PrepWindowsUpdateNote = $false }) $false
        $Check.Passed | Should -BeFalse
        $Check.Detail | Should -BeLike '*NIE: notatka*'
    }
    It 'nie przechodzi, gdy sonda nie ma pól kroku 9 (stara sonda)' {
        $Probe = New-UiProbe @{}
        $Without = [pscustomobject]@{}
        foreach ($P in $Probe.PSObject.Properties | Where-Object { $_.Name -notlike 'Prep*' }) { $Without | Add-Member -NotePropertyName $P.Name -NotePropertyValue $P.Value }
        $Check = Get-PrepCheck $Without
        $Check.Passed | Should -BeFalse
        $Check.Detail | Should -BeLike 'brak pola PrepMenuFound*'
    }
    It 'brak pól kroku 9 nie pomija sprawdzenia kroku 10 („Naprawy i narzędzia”)' {
        $Probe = New-UiProbe @{}
        $Without = [pscustomobject]@{}
        foreach ($P in $Probe.PSObject.Properties | Where-Object { $_.Name -notlike 'Prep*' }) { $Without | Add-Member -NotePropertyName $P.Name -NotePropertyValue $P.Value }
        $Checks = Get-GuiUiCheck -Texts $script:PlTexts -Probe $Without -PrivacyExpected $script:PrivacyOk -AppsExpected $script:AppsOk -RepairTitles $script:RepairOk -InstallExpected $script:InstallOk -AutostartExpected $script:AutostartOk
        (Get-Check $Checks 'RepairPage').Passed | Should -BeTrue
    }
}

Describe 'Sonda: „Naprawy i narzędzia” (krok 10)' {
    BeforeAll {
        $script:ProbeText = Get-GuiUiProbeScript
        $script:Step10 = $script:ProbeText.Substring($script:ProbeText.IndexOf('# 10.'))
        if ($script:ProbeText.IndexOf('# 11.') -gt $script:ProbeText.IndexOf('# 10.')) {
            $script:Step10 = $script:ProbeText.Substring($script:ProbeText.IndexOf('# 10.'), $script:ProbeText.IndexOf('# 11.') - $script:ProbeText.IndexOf('# 10.'))
        }
    }
    It 'krok 10 jest po kroku 9 („Przygotowanie”) i jego obsłudze błędu, a przed krokiem 11 („Instalacja”)' {
        $script:ProbeText.IndexOf('# 10.') | Should -BeGreaterThan $script:ProbeText.IndexOf('catch { $Out.PrepError = $_.Exception.Message')
        $script:Step10 | Should -Not -Match '# 11\.'
        $script:ProbeText.IndexOf('# 11.') | Should -BeGreaterThan $script:ProbeText.IndexOf('catch { $Out.RepairError = $_.Exception.Message')
    }
    It 'wchodzi na pozycję menu „Naprawy i narzędzia” (nazwa = GuiText.ServicePageName) wzorcem SelectionItemPattern' {
        (Get-CsText (Read-CoreSource 'Gui\GuiText.cs') 'ServicePageName') | Should -Be 'Naprawy i narzędzia'
        (Get-GuiProbeTexts -Language pl).ServicePage | Should -Be 'Naprawy i narzędzia'
        $script:Step10 | Should -Match ([regex]::Escape('Find-Element $Root $T.ServicePage $ListItem'))
        $script:Step10 | Should -Match ([regex]::Escape('[System.Windows.Automation.SelectionItemPattern]::Pattern).Select()'))
    }
    It 'czyta teksty listy „Pozycje” (z przewinięciem do końca) do pól RepairMenuFound, RepairListFound, RepairTexts' {
        $script:Step10 | Should -Match ([regex]::Escape('Wait-Element $Root $T.Items ([System.Windows.Automation.ControlType]::List)'))
        $script:Step10 | Should -Match ([regex]::Escape('ScrollPattern'))
        foreach ($Field in 'RepairMenuFound', 'RepairListFound', 'RepairTexts') { $script:Step10 | Should -Match $Field }
    }
    It 'niczego nie klika, nie zaznacza ani nie uruchamia (bez Invoke, Toggle, wyboru wiersza, myszy i klawiatury)' {
        $Body = $script:Step10.Substring($script:Step10.IndexOf('try {'))
        $Body = $Body.Substring($Body.IndexOf('.Select()') + '.Select()'.Length)
        $Body | Should -Not -Match 'Invoke\(\)'
        $Body | Should -Not -Match 'Toggle\(\)'
        $Body | Should -Not -Match 'Select\(\)'
        $Body | Should -Not -Match 'AddToSelection|ScrollIntoView'
        $Body | Should -Not -Match 'DsoUser32\]::(Click|DoubleClick|PressKey)'
    }
    It 'krok 10 ma własny try/catch, a pola wyniku są w początkowym obiekcie sondy' {
        $script:ProbeText | Should -Match ([regex]::Escape('catch { $Out.RepairError = $_.Exception.Message'))
        $Head = $script:ProbeText.Substring(0, $script:ProbeText.IndexOf('function Add-Step'))
        foreach ($Field in 'RepairMenuFound', 'RepairListFound', 'RepairTexts', 'RepairError') { $Head | Should -Match $Field }
    }
}

Describe 'Sonda: przewijanie wirtualizowanej listy (Find-ListText)' {
    BeforeAll {
        Add-Type -AssemblyName UIAutomationClient
        $Text = Get-GuiUiProbeScript
        $Start = $Text.IndexOf('    function Find-ListText')
        $End = $Text.IndexOf('    function Get-ListRow')
        $script:FindListTextSource = $Text.Substring($Start, $End - $Start)
        $script:ProbeScriptText = $Text
        # Atrapa listy: wiersz "Wiersz" jest w drzewie dopiero od zadanego procentu przewiniecia.
        function script:New-FakeList([int]$VisibleFrom, [bool]$Scrollable = $true) {
            $List = [pscustomobject]@{ Percent = -1; VisibleFrom = $VisibleFrom; Calls = 0 }
            $Pattern = [pscustomobject]@{ Owner = $List }
            $Pattern | Add-Member -MemberType ScriptMethod -Name SetScrollPercent -Value { param($H, $V) $this.Owner.Calls++; $this.Owner.Percent = $V }
            $List | Add-Member -MemberType ScriptMethod -Name GetCurrentPattern -Value { param($P) if (-not $Scrollable) { throw 'brak wzorca' }; $Pattern }.GetNewClosure()
            return $List
        }
        function script:Invoke-FindListText($List, [int]$FirstWait = 0) {
            $Script = [scriptblock]::Create(@"
param(`$List, `$FirstWait)
`$script:Steps = @()
function Add-Step([string]`$T) { `$script:Steps += `$T }
function Wait-Element(`$Parent, [string]`$Name, `$Type, [int]`$Seconds) { if (`$Parent.Percent -ge `$Parent.VisibleFrom -and `$Parent.VisibleFrom -ge 0) { return 'ELEMENT' } return `$null }
$($script:FindListTextSource)
`$Found = Find-ListText `$List 'Wiersz' `$FirstWait
[pscustomobject]@{ Found = `$Found; Steps = `$script:Steps }
"@)
            & $Script $List $FirstWait
        }
    }
    It 'wiersz widoczny od razu: bez przewijania' {
        $List = New-FakeList 0
        $List.Percent = 0
        $R = Invoke-FindListText $List
        $R.Found | Should -Be 'ELEMENT'
        $List.Calls | Should -Be 0
    }
    It 'wiersz pojawia się dopiero przy 60 proc.: sonda przewija stopniami co 10 proc. i go znajduje' {
        $List = New-FakeList 60
        $R = Invoke-FindListText $List
        $R.Found | Should -Be 'ELEMENT'
        $List.Percent | Should -Be 60
        $List.Calls | Should -Be 7
        ($R.Steps -join ' ') | Should -BeLike '*60 proc.*'
    }
    It 'brak wiersza: kończy się po skończonej liczbie kroków (11) i zwraca $null z wpisem diagnostycznym' {
        $List = New-FakeList -1
        $R = Invoke-FindListText $List
        $R.Found | Should -BeNullOrEmpty
        $List.Calls | Should -Be 11
        ($R.Steps -join ' ') | Should -BeLike '*nie znaleziono*'
    }
    It 'brak ScrollPattern: wpis diagnostyczny zamiast cichego catch' {
        $List = New-FakeList -1 $false
        $R = Invoke-FindListText $List
        $R.Found | Should -BeNullOrEmpty
        ($R.Steps -join ' ') | Should -BeLike '*brak ScrollPattern*'
    }
    It 'Get-ListRow szuka przez Find-ListText z pierwszym czekaniem 15 s, bez pustego catch przy przewijaniu' {
        $script:ProbeScriptText | Should -Match ([regex]::Escape('$Text = Find-ListText $List $Title 15'))
        $script:FindListTextSource | Should -Not -Match 'catch\s*\{\s*\}'
    }
}

Describe 'Get-GuiRepairTitle' {
    It 'z katalogu bierze tytuły pozycji kategorii repair, w kolejności plików i wpisów, bez apps-bloat.json' {
        $Dir = Join-Path $TestDrive 'catalog-repair'
        $null = New-Item -ItemType Directory -Path $Dir -Force
        $Items = @(
            [ordered]@{ id = 'repair.a'; category = 'repair'; kind = 'action'; title = 'Napraw A' }
            [ordered]@{ id = 'repair.b'; category = 'repair'; kind = 'tweak'; title = 'Ochrona B' }
        )
        $Other = @([ordered]@{ id = 'tools.c'; category = 'tools'; kind = 'action'; title = 'Narzędzie C' })
        $Apps = [ordered]@{ entries = @(); protected = @() }
        [System.IO.File]::WriteAllText((Join-Path $Dir 'repair.json'), (ConvertTo-Json -InputObject $Items -Depth 4), (New-Object System.Text.UTF8Encoding($true)))
        [System.IO.File]::WriteAllText((Join-Path $Dir 'tools.json'), (ConvertTo-Json -InputObject $Other -Depth 4), (New-Object System.Text.UTF8Encoding($true)))
        [System.IO.File]::WriteAllText((Join-Path $Dir 'apps-bloat.json'), (ConvertTo-Json -InputObject $Apps -Depth 4), (New-Object System.Text.UTF8Encoding($true)))
        $Panels = @([ordered]@{ id = 'panels.d'; category = 'panels'; kind = 'action'; title = 'Panel D' })
        [System.IO.File]::WriteAllText((Join-Path $Dir 'panels.json'), (ConvertTo-Json -InputObject $Panels -Depth 4), (New-Object System.Text.UTF8Encoding($true)))
        Get-GuiRepairTitle -CatalogDirectory $Dir | Should -Be @('Napraw A', 'Ochrona B', 'Narzędzie C', 'Panel D')
    }
    It 'pomija apps.json po nazwie pliku' {
        $Dir = Join-Path $TestDrive 'catalog-repair-install'
        $null = New-Item -ItemType Directory -Path $Dir -Force
        $Items = @([ordered]@{ id = 'repair.a'; category = 'repair'; kind = 'action'; title = 'Napraw A' })
        $Install = @([ordered]@{ id = 'install.x'; category = 'repair'; title = 'Nie z katalogu' })
        [System.IO.File]::WriteAllText((Join-Path $Dir 'repair.json'), (ConvertTo-Json -InputObject $Items -Depth 4), (New-Object System.Text.UTF8Encoding($true)))
        [System.IO.File]::WriteAllText((Join-Path $Dir 'apps.json'), (ConvertTo-Json -InputObject $Install -Depth 4), (New-Object System.Text.UTF8Encoding($true)))
        Get-GuiRepairTitle -CatalogDirectory $Dir | Should -Be @('Napraw A')
    }
    It 'pomija autostart-known.json po nazwie pliku' {
        $Dir = Join-Path $TestDrive 'catalog-repair-autostart'
        $null = New-Item -ItemType Directory -Path $Dir -Force
        $Items = @([ordered]@{ id = 'repair.a'; category = 'repair'; kind = 'action'; title = 'Napraw A' })
        $Known = @([ordered]@{ id = 'steam'; category = 'repair'; title = 'Nie z katalogu' })
        [System.IO.File]::WriteAllText((Join-Path $Dir 'repair.json'), (ConvertTo-Json -InputObject $Items -Depth 4), (New-Object System.Text.UTF8Encoding($true)))
        [System.IO.File]::WriteAllText((Join-Path $Dir 'autostart-known.json'), (ConvertTo-Json -InputObject $Known -Depth 4), (New-Object System.Text.UTF8Encoding($true)))
        Get-GuiRepairTitle -CatalogDirectory $Dir | Should -Be @('Napraw A')
    }
    It 'wbudowany katalog: tytuły kategorii repair, tools i panels (w tej kolejności) = tytuły z repair.json, tools.json i panels.json' {
        $Dir = Join-Path $PSScriptRoot '..\..\..\src\DSOptimizer.Catalog\catalog'
        $ByFile = foreach ($Name in 'repair', 'tools', 'panels') {
            $Items = [System.IO.File]::ReadAllText((Join-Path $Dir "$Name.json"), [System.Text.Encoding]::UTF8) | ConvertFrom-Json
            , @(@($Items) | Where-Object { [string]$_.category -eq $Name } | ForEach-Object { [string]$_.title })
        }
        $Expected = @($ByFile | ForEach-Object { $_ })
        $Titles = @(Get-GuiRepairTitle -CatalogDirectory $Dir)
        # Liczby z katalogu repo, nie stałe: 11 napraw, 2 narzędzia, 15 paneli = 28 (stan paczki UX).
        $Titles.Count | Should -Be $Expected.Count
        $Titles.Count | Should -Be ($ByFile[0].Count + $ByFile[1].Count + $ByFile[2].Count)
        $Titles | Should -Be $Expected
        $Titles | Should -Contain 'Ochrona systemu'
        $Titles | Should -Contain (Get-CatalogTitleByCategory $Dir 'tools')
        $Titles | Should -Contain (Get-CatalogTitleByCategory $Dir 'panels')
        $Titles.IndexOf((Get-CatalogTitleByCategory $Dir 'tools')) | Should -BeGreaterThan ($ByFile[0].Count - 1)
        $Titles.IndexOf((Get-CatalogTitleByCategory $Dir 'panels')) | Should -BeGreaterThan ($ByFile[0].Count + $ByFile[1].Count - 1)
    }
    It 'wbudowany katalog: 28 tytułów (11 napraw, 2 narzędzia, 15 paneli)' {
        $Dir = Join-Path $PSScriptRoot '..\..\..\src\DSOptimizer.Catalog\catalog'
        @(Get-GuiRepairTitle -CatalogDirectory $Dir).Count | Should -Be 28
    }
    It '-Language en: te same pozycje, tytuły z tabel tłumaczeń (każdy inny niż polski poza „Region”)' {
        $Dir = Join-Path $PSScriptRoot '..\..\..\src\DSOptimizer.Catalog\catalog'
        $Pl = @(Get-GuiRepairTitle -CatalogDirectory $Dir)
        $En = @(Get-GuiRepairTitle -CatalogDirectory $Dir -Language en)
        $En.Count | Should -Be $Pl.Count
        for ($I = 0; $I -lt $Pl.Count; $I++) {
            $En[$I] | Should -Be (Get-DsoUiText -Polish $Pl[$I] -Language en)
            # „Region” (panel) brzmi tak samo w obu językach.
            if ($Pl[$I] -cne 'Region') { $En[$I] | Should -Not -Be $Pl[$I] }
        }
    }
    It 'pusty wynik to błąd (sprawdzenie nie miałoby czego porównać)' {
        $Dir = Join-Path $TestDrive 'pusty-repair'
        $null = New-Item -ItemType Directory -Path $Dir -Force
        { Get-GuiRepairTitle -CatalogDirectory $Dir } | Should -Throw '*repair*'
    }
}

Describe 'Get-GuiUiCheck: RepairPage' {
    BeforeAll {
        function script:Get-RepairCheck($Probe, $Titles = $script:RepairOk) {
            Get-Check (Get-GuiUiCheck -Texts $script:PlTexts -Probe $Probe -PrivacyExpected $script:PrivacyOk -AppsExpected $script:AppsOk -RepairTitles $Titles -InstallExpected $script:InstallOk) 'RepairPage'
        }
    }
    It 'przechodzi: pozycja menu, lista i wszystkie tytuły z katalogu' {
        $Check = Get-RepairCheck (New-UiProbe @{})
        $Check.Passed | Should -BeTrue
        $Check.Detail | Should -BeLike '*pozycja menu*tytuły 2/2*'
    }
    It 'nie przechodzi, gdy <Powod>' -ForEach @(
        @{ Powod = 'brak pozycji menu'; Override = @{ RepairMenuFound = $false }; Like = '*NIE: pozycja menu*' }
        @{ Powod = 'brak listy'; Override = @{ RepairListFound = $false }; Like = '*NIE: lista*' }
        @{ Powod = 'brakuje tytułu'; Override = @{ RepairTexts = @('Naprawa A', 'Opis naprawy A') }; Like = '*tytuły 1/2 (brak: Naprawa B)*' }
        @{ Powod = 'lista jest pusta'; Override = @{ RepairTexts = @() }; Like = '*tytuły 0/2*' }
        @{ Powod = 'sonda zgłosiła błąd kroku'; Override = @{ RepairError = 'Brak listy Pozycje' }; Like = '*sonda: Brak listy Pozycje*' }
    ) {
        $Check = Get-RepairCheck (New-UiProbe $Override)
        $Check.Passed | Should -BeFalse
        $Check.Detail | Should -BeLike $Like
    }
    It 'nie przechodzi bez oczekiwanych tytułów (brak albo pusta lista z katalogu)' {
        foreach ($Titles in @($null, @())) {
            $Check = Get-RepairCheck (New-UiProbe @{}) $Titles
            $Check.Passed | Should -BeFalse
            $Check.Detail | Should -BeLike '*brak oczekiwanych tytułów*'
        }
    }
    It 'nie przechodzi, gdy sonda nie ma pól kroku 10 (stara sonda)' {
        $Probe = New-UiProbe @{}
        $Without = [pscustomobject]@{}
        foreach ($P in $Probe.PSObject.Properties | Where-Object { $_.Name -notlike 'Repair*' }) { $Without | Add-Member -NotePropertyName $P.Name -NotePropertyValue $P.Value }
        $Check = Get-RepairCheck $Without
        $Check.Passed | Should -BeFalse
        $Check.Detail | Should -BeLike 'brak pola RepairMenuFound*'
    }
}

Describe 'Sonda: „Instalacja” (krok 11)' {
    BeforeAll {
        $script:ProbeText = Get-GuiUiProbeScript
        $script:Step11 = $script:ProbeText.Substring($script:ProbeText.IndexOf('# 11.'))
        $script:Step11 = $script:Step11.Substring(0, $script:Step11.IndexOf('# 12.'))
    }
    It 'krok 11 jest po kroku 10 („Naprawy i narzędzia”) i jego obsłudze błędu, a przed krokiem 12 („Autostart”)' {
        $script:ProbeText.IndexOf('# 11.') | Should -BeGreaterThan $script:ProbeText.IndexOf('catch { $Out.RepairError = $_.Exception.Message')
        $script:Step11 | Should -Match ([regex]::Escape('catch { $Out.InstallError = $_.Exception.Message'))
    }
    It 'wchodzi na pozycję menu „Instalacja” (nazwa = InstallText.PageTitle) wzorcem SelectionItemPattern' {
        (Get-CsText (Read-CoreSource 'Install\InstallText.cs') 'PageTitle') | Should -Be (Get-GuiProbeTexts -Language pl).InstallPage
        $script:Step11 | Should -Match ([regex]::Escape('Find-Element $Root $T.InstallPage $ListItem'))
        $script:Step11 | Should -Match ([regex]::Escape('[System.Windows.Automation.SelectionItemPattern]::Pattern).Select()'))
    }
    It 'czyta listę „Kategorie”, teksty listy „Aplikacje do instalacji” w każdej kategorii (z przewijaniem) i przycisk „Zainstaluj (N)”' {
        $script:Step11 | Should -Match ([regex]::Escape('Wait-Element $Root $T.InstallCategoryList ([System.Windows.Automation.ControlType]::List)'))
        $script:Step11 | Should -Match ([regex]::Escape('$T.InstallAppList'))
        $script:Step11 | Should -Match ([regex]::Escape('ScrollPattern'))
        $script:Step11 | Should -Match ([regex]::Escape('$InstallPattern = $T.InstallPattern'))
        $script:Step11 | Should -Match ([regex]::Escape('IsEnabled'))
        foreach ($Field in 'InstallMenuFound', 'InstallLoaded', 'InstallCategories', 'InstallTexts', 'InstallButtonText', 'InstallButtonEnabled') { $script:Step11 | Should -Match $Field }
    }
    It 'niczego nie zaznacza, nie instaluje i nie wyszukuje (bez Invoke, Toggle, wpisywania, myszy i klawiatury)' {
        $script:Step11 | Should -Not -Match 'Invoke\(\)'
        $script:Step11 | Should -Not -Match 'Toggle\(\)'
        $script:Step11 | Should -Not -Match 'ValuePattern|SetValue'
        $script:Step11 | Should -Not -Match 'Szukaj'
        $script:Step11 | Should -Not -Match 'DsoUser32\]::(Click|DoubleClick|PressKey)'
    }
    It 'krok 11 ma własny try/catch, a pola wyniku są w początkowym obiekcie sondy' {
        $script:ProbeText | Should -Match ([regex]::Escape('catch { $Out.InstallError = $_.Exception.Message'))
        $Head = $script:ProbeText.Substring(0, $script:ProbeText.IndexOf('function Add-Step'))
        foreach ($Field in 'InstallMenuFound', 'InstallLoaded', 'InstallCategories', 'InstallTexts', 'InstallButtonText', 'InstallButtonEnabled', 'InstallError') { $Head | Should -Match $Field }
    }
}

Describe 'Get-GuiInstallTitle' {
    BeforeAll {
        function script:Write-AppsJson([string]$Dir, [object[]]$Entries) {
            $null = New-Item -ItemType Directory -Path $Dir -Force
            $Json = ConvertTo-Json -InputObject ([ordered]@{ entries = @($Entries) }) -Depth 5
            [System.IO.File]::WriteAllText((Join-Path $Dir 'apps.json'), $Json, (New-Object System.Text.UTF8Encoding($true)))
        }
    }
    It 'z apps.json bierze tytuły (w kolejności pliku) i nazwy kategorii w UI (kolejność menu strony, bez powtórzeń)' {
        $Dir = Join-Path $TestDrive 'apps-a'
        Write-AppsJson $Dir @(
            [ordered]@{ id = 'install.7zip'; wingetId = '7zip.7zip'; title = '7-Zip'; category = 'tools' }
            [ordered]@{ id = 'install.chrome'; wingetId = 'Google.Chrome'; title = 'Google Chrome'; category = 'browsers' }
            [ordered]@{ id = 'install.npp'; wingetId = 'Notepad++.Notepad++'; title = 'Notepad++'; category = 'tools' }
            [ordered]@{ id = 'install.vc'; wingetId = 'Microsoft.VCRedist.2015+.x64'; title = 'Visual C++'; category = 'runtimes' }
        )
        $R = Get-GuiInstallTitle -CatalogDirectory $Dir
        @($R.Titles) | Should -Be @('7-Zip', 'Google Chrome', 'Notepad++', 'Visual C++')
        @($R.Categories) | Should -Be @('Wszystkie', 'Zaznaczone', 'Przeglądarki', 'Narzędzia', 'Środowiska uruchomieniowe')
    }
    It 'nieznana kategoria to błąd' {
        $Dir = Join-Path $TestDrive 'apps-b'
        Write-AppsJson $Dir @([ordered]@{ id = 'install.x'; wingetId = 'X.X'; title = 'X'; category = 'office' })
        { Get-GuiInstallTitle -CatalogDirectory $Dir } | Should -Throw '*office*'
    }
    It 'pusta lista albo brak pliku to błąd (sprawdzenie nie miałoby czego porównać)' {
        $Dir = Join-Path $TestDrive 'apps-c'
        Write-AppsJson $Dir @()
        { Get-GuiInstallTitle -CatalogDirectory $Dir } | Should -Throw '*apps.json*'
        { Get-GuiInstallTitle -CatalogDirectory (Join-Path $TestDrive 'brak') } | Should -Throw '*apps.json*'
    }
    It 'wbudowana lista: tytuły = wpisy apps.json, nazwy kategorii = InstallText.CategoryName w Core' {
        $Dir = Join-Path $PSScriptRoot '..\..\..\src\DSOptimizer.Catalog\catalog'
        $Entries = @(([System.IO.File]::ReadAllText((Join-Path $Dir 'apps.json'), [System.Text.Encoding]::UTF8) | ConvertFrom-Json).entries)
        $R = Get-GuiInstallTitle -CatalogDirectory $Dir
        @($R.Titles) | Should -Be @($Entries | ForEach-Object { [string]$_.title })
        $Core = Join-Path $PSScriptRoot '..\..\..\src\DSOptimizer.Core\Install'
        $Text = Read-CoreSource 'Install\InstallText.cs'
        $Catalog = [System.IO.File]::ReadAllText((Join-Path $Core 'AppsInstallCatalog.cs'), [System.Text.Encoding]::UTF8)
        $Names = @{}
        foreach ($M in [regex]::Matches($Text, 'InstallCategory\.(\w+)\s*=>\s*(?:L\.T\(\s*)?"((?:[^"\\]|\\.)*)"')) { $Names[$M.Groups[1].Value] = ConvertFrom-CsLiteral $M.Groups[2].Value }
        $Expected = foreach ($M in [regex]::Matches($Catalog, '\["([a-z-]+)"\] = InstallCategory\.(\w+)')) {
            if (@($Entries | Where-Object { [string]$_.category -eq $M.Groups[1].Value }).Count -gt 0) { $Names[$M.Groups[2].Value] }
        }
        $Names.Count | Should -Be 8
        # „Wszystkie” na górze (D1) – ta sama nazwa co InstallText.AllCategoriesName w Core.
        $All = Get-CsText $Text 'AllCategoriesName'
        $Sel = Get-CsText $Text 'SelectedGroupName'
        @($R.Categories) | Should -Be @(@($All, $Sel) + @($Expected))
    }
    It '-Language en: tytuły bez zmian (nazwy produktów), kategorie po angielsku z tabel tłumaczeń' {
        $Dir = Join-Path $PSScriptRoot '..\..\..\src\DSOptimizer.Catalog\catalog'
        $Pl = Get-GuiInstallTitle -CatalogDirectory $Dir
        $En = Get-GuiInstallTitle -CatalogDirectory $Dir -Language en
        @($En.Titles) | Should -Be @($Pl.Titles)
        @($En.Categories) | Should -Be @(@($Pl.Categories) | ForEach-Object { Get-DsoUiText -Polish $_ -Language en })
        @($En.Categories)[0..1] | Should -Be @('All', 'Selected')
    }
}

Describe 'Get-GuiUiCheck: InstallPage' {
    BeforeAll {
        function script:Get-InstallCheck($Probe, $Expected = $script:InstallOk) {
            Get-Check (Get-GuiUiCheck -Texts $script:PlTexts -Probe $Probe -PrivacyExpected $script:PrivacyOk -AppsExpected $script:AppsOk -RepairTitles $script:RepairOk -InstallExpected $Expected) 'InstallPage'
        }
    }
    It 'prefiks podtytułu (<Language>): sonda w VM łapie tekst, który sprawdza host, a host = InstallText.PageSummary z GitHubem (M6)' -ForEach @(
        @{ Language = 'pl' }, @{ Language = 'en' }
    ) {
        (Get-GuiUiProbeScript) | Should -Match ([regex]::Escape('$SourcePrefix = [string]$T.InstallSourceProbePrefix'))
        $Texts = Get-GuiProbeTexts -Language $Language
        $Texts.InstallSourcePrefix.StartsWith($Texts.InstallSourceProbePrefix, [System.StringComparison]::Ordinal) | Should -BeTrue
        # PageSummary: polski klucz L.F("Źródła: winget (Microsoft) i GitHub (RustDesk) · {0} · …") zaczyna się od prefiksu hosta.
        $Pl = Get-GuiProbeTexts -Language pl
        $Core = Read-CoreSource 'Install\InstallText.cs'
        $Core.Contains('L.F("' + $Pl.InstallSourcePrefix + '{0}') | Should -BeTrue
    }
    It 'przechodzi: pozycja menu, odczyt, wszystkie kategorie i tytuły, „Zainstaluj (0)” nieaktywne' {
        $Check = Get-InstallCheck (New-UiProbe @{})
        $Check.Passed | Should -BeTrue
        $Check.Detail | Should -BeLike '*pozycja menu*kategorie 4/4*tytuły 2/2*Zainstaluj (0)*nieaktywny*'
    }
    It 'nie przechodzi, gdy <Powod>' -ForEach @(
        @{ Powod = 'brak pozycji menu'; Override = @{ InstallMenuFound = $false }; Like = '*NIE: pozycja menu*' }
        @{ Powod = 'odczyt się nie skończył'; Override = @{ InstallLoaded = $false }; Like = '*NIE: odczyt*' }
        @{ Powod = 'winget nie działa (podtytuł „niedostępne”, komunikat zamiast listy)'; Override = @{ InstallSubtitle = 'Źródło: winget (Microsoft) · niedostępne' }; Like = '*NIE: podtytuł źródła*' }
        @{ Powod = 'winget nie działa, lista z RustDeskiem (M6)'; Override = @{ InstallSubtitle = 'Źródła: GitHub (RustDesk) · winget (Microsoft) niedostępny · 34 aplikacje · zainstalowane: 0' }; Like = '*NIE: podtytuł źródła*' }
        @{ Powod = 'stary podtytuł bez GitHuba'; Override = @{ InstallSubtitle = 'Źródło: winget (Microsoft) · 34 aplikacje · zainstalowane: 0' }; Like = '*NIE: podtytuł źródła*' }
        @{ Powod = 'brak podtytułu źródła'; Override = @{ InstallSubtitle = $null }; Like = '*NIE: podtytuł źródła*' }
        @{ Powod = 'brakuje kategorii'; Override = @{ InstallCategories = @('Wszystkie', 'Zaznaczone', 'Przeglądarki') }; Like = '*kategorie 3/4 (brak: Narzędzia)*' }
        @{ Powod = 'brakuje tytułu'; Override = @{ InstallTexts = @('Google Chrome') }; Like = '*tytuły 1/2 (brak: 7-Zip)*' }
        @{ Powod = 'przycisk jest aktywny'; Override = @{ InstallButtonEnabled = $true }; Like = '*NIE: „Zainstaluj (0)” nieaktywny*' }
        @{ Powod = 'przycisk ma inne N'; Override = @{ InstallButtonText = 'Zainstaluj (2)' }; Like = "*przycisk 'Zainstaluj (2)'*" }
        @{ Powod = 'brak przycisku'; Override = @{ InstallButtonText = $null }; Like = '*brak przycisku*' }
        @{ Powod = 'sonda zgłosiła błąd kroku'; Override = @{ InstallError = 'Brak listy Kategorie' }; Like = '*sonda: Brak listy Kategorie*' }
    ) {
        $Check = Get-InstallCheck (New-UiProbe $Override)
        $Check.Passed | Should -BeFalse
        $Check.Detail | Should -BeLike $Like
    }
    It 'nie przechodzi bez oczekiwań z apps.json (brak albo puste tytuły)' {
        foreach ($Expected in @($null, [pscustomobject]@{ Titles = @(); Categories = @('Narzędzia') })) {
            $Check = Get-InstallCheck (New-UiProbe @{}) $Expected
            $Check.Passed | Should -BeFalse
            $Check.Detail | Should -BeLike '*brak oczekiwanych tytułów*'
        }
    }
    It 'nie przechodzi, gdy sonda nie ma pól kroku 11 (stara sonda)' {
        $Probe = New-UiProbe @{}
        $Without = [pscustomobject]@{}
        foreach ($P in $Probe.PSObject.Properties | Where-Object { $_.Name -notlike 'Install*' }) { $Without | Add-Member -NotePropertyName $P.Name -NotePropertyValue $P.Value }
        $Check = Get-InstallCheck $Without
        $Check.Passed | Should -BeFalse
        $Check.Detail | Should -BeLike 'brak pola InstallMenuFound*'
    }
    It 'brak pól kroku 10 nie pomija sprawdzenia kroku 11' {
        $Probe = New-UiProbe @{}
        $Without = [pscustomobject]@{}
        foreach ($P in $Probe.PSObject.Properties | Where-Object { $_.Name -notlike 'Repair*' }) { $Without | Add-Member -NotePropertyName $P.Name -NotePropertyValue $P.Value }
        (Get-InstallCheck $Without).Passed | Should -BeTrue
    }
}

Describe 'Sonda: „Autostart” (krok 12)' {
    BeforeAll {
        $script:ProbeText = Get-GuiUiProbeScript
        $script:Step12 = $script:ProbeText.Substring($script:ProbeText.IndexOf('# 12.'))
        $script:Step12 = $script:Step12.Substring(0, $script:Step12.IndexOf('$Out.Ok = $true'))
    }
    It 'krok 12 jest ostatni: po kroku 11 („Instalacja”) i jego obsłudze błędu' {
        $script:ProbeText.IndexOf('# 12.') | Should -BeGreaterThan $script:ProbeText.IndexOf('catch { $Out.InstallError = $_.Exception.Message')
        $script:Step12 | Should -Not -Match '# 13\.'
    }
    It 'wchodzi na stronę przez pozycję menu „Autostart” (nazwa = AutostartText.PageTitle) wzorcem SelectionItemPattern' {
        (Get-CsText (Read-CoreSource 'Autostart\AutostartText.cs') 'PageTitle') | Should -Be (Get-GuiProbeTexts -Language pl).AutostartPage
        $script:Step12 | Should -Match ([regex]::Escape('Find-Element $Root $T.AutostartPage $ListItem'))
        $script:Step12 | Should -Match ([regex]::Escape('[System.Windows.Automation.SelectionItemPattern]::Pattern).Select()'))
        # Jedyny wybór w kroku 12 to pozycja menu (żaden wiersz listy ani grupa nie jest wybierana).
        ([regex]::Matches($script:Step12, [regex]::Escape('.Select()'))).Count | Should -Be 1
    }
    It 'szuka elementów strony w kontenerze strony (AutomationId = x:Name widoku w MainWindow.xaml)' {
        $Main = [System.IO.File]::ReadAllText((Join-Path $PSScriptRoot '..\..\..\src\DSOptimizer.App\MainWindow.xaml'), [System.Text.Encoding]::UTF8)
        $Main | Should -Match ([regex]::Escape('<views:AutostartView x:Name="AutostartPage"'))
        $script:Step12 | Should -Match ([regex]::Escape("`$AE::AutomationIdProperty, 'AutostartPage'"))
        $script:Step12 | Should -Match 'AutostartPageScoped'
    }
    It 'czeka najwyżej 120 s na koniec odczytu („Odśwież listę” aktywne, bez „Odczyt listy w toku” i „jeszcze nie odczytana”) i na stały stan przez 3 s' {
        $script:Step12 | Should -Match ([regex]::Escape('AddSeconds(120)'))
        foreach ($Name in 'AutostartRefresh', 'AutostartReading', 'AutostartNotRead', 'AutostartReadFailed') { $script:Step12 | Should -Match ([regex]::Escape("`$T.$Name")) }
        $script:Step12 | Should -Match ([regex]::Escape('IsEnabled'))
        $script:Step12 | Should -Match ([regex]::Escape('TotalSeconds -ge 3'))
    }
    It 'czyta nazwy pozycji przełącznika grup (lista „Grupy autostartu”) i napis oraz stan przycisków „Wyłącz (N)” i „Włącz (N)”' {
        $script:Step12 | Should -Match ([regex]::Escape('$T.AutostartGroups ([System.Windows.Automation.ControlType]::List)'))
        $script:Step12 | Should -Match ([regex]::Escape('$T.DisablePattern'))
        $script:Step12 | Should -Match ([regex]::Escape('$T.EnablePattern'))
        foreach ($Field in 'AutostartMenuFound', 'AutostartLoaded', 'AutostartReadFailed', 'AutostartGroupNames', 'AutostartDisableText', 'AutostartDisableEnabled', 'AutostartEnableText', 'AutostartEnableEnabled') { $script:Step12 | Should -Match $Field }
    }
    It 'niczego nie zaznacza, nie wyłącza i nie włącza (bez Invoke, Toggle, „Zaznacz sugerowane”, wpisywania, myszy i klawiatury)' {
        $script:Step12 | Should -Not -Match 'Invoke\(\)'
        $script:Step12 | Should -Not -Match 'Toggle\(\)'
        $script:Step12 | Should -Not -Match ([regex]::Escape('$T.SelectSuggested'))
        $script:Step12 | Should -Not -Match 'ValuePattern|SetValue|AddToSelection'
        $script:Step12 | Should -Not -Match 'DsoUser32\]::(Click|DoubleClick|PressKey)'
    }
    It 'krok 12 ma własny try/catch, a pola wyniku są w początkowym obiekcie sondy' {
        $script:ProbeText | Should -Match ([regex]::Escape('catch { $Out.AutostartError = $_.Exception.Message'))
        $Head = $script:ProbeText.Substring(0, $script:ProbeText.IndexOf('function Add-Step'))
        foreach ($Field in 'AutostartMenuFound', 'AutostartPageScoped', 'AutostartLoaded', 'AutostartReadFailed', 'AutostartGroupNames', 'AutostartDisableText', 'AutostartDisableEnabled', 'AutostartEnableText', 'AutostartEnableEnabled', 'AutostartError') { $Head | Should -Match $Field }
    }
    It 'strony wybiera po nazwie pozycji menu, nigdy po pozycji na liście (nowa pozycja „Autostart” przesunęła „Instalację” i dalsze)' {
        foreach ($Key in 'HistoryPage', 'ProbeCategory', 'AppsPage', 'PrepPage', 'ServicePage', 'InstallPage', 'AutostartPage') {
            $script:ProbeText | Should -Match ([regex]::Escape("Find-Element `$Root `$T.$Key `$ListItem")) -Because $Key
        }
        $script:ProbeText | Should -Match ([regex]::Escape('$PrivacyName = [string]$T.PrivacyCategory'))
        $script:ProbeText | Should -Match ([regex]::Escape('Find-Element $Root $PrivacyName $ListItem'))
        # Lista menu służy wyłącznie do pomiaru paska (BoundingRectangle): jej pozycji się nie przegląda ani nie liczy.
        $script:ProbeText | Should -Not -Match '\$Menu\.(FindAll|FindFirst)'
        $script:ProbeText | Should -Not -Match '\$T\.Menu\b[^\r\n]*\.(FindAll|Count)'
    }
}

Describe 'Get-GuiUiCheck: AutostartPage' {
    BeforeAll {
        function script:Get-AutostartCheck($Probe, $Expected = $script:AutostartOk, $Texts = $script:PlTexts) {
            Get-Check (Get-GuiUiCheck -Texts $Texts -Probe $Probe -PrivacyExpected $script:PrivacyOk -AppsExpected $script:AppsOk -RepairTitles $script:RepairOk -InstallExpected $script:InstallOk -AutostartExpected $Expected) 'AutostartPage'
        }
        # Dwa odczyty CLI wokół sondy: przed (-AutostartExpected) i po (-AutostartExpectedAfter).
        function script:Get-AutostartWindowCheck($Probe, $Before, $After) {
            Get-Check (Get-GuiUiCheck -Texts $script:PlTexts -Probe $Probe -PrivacyExpected $script:PrivacyOk -AppsExpected $script:AppsOk -RepairTitles $script:RepairOk -InstallExpected $script:InstallOk -AutostartExpected $Before -AutostartExpectedAfter $After) 'AutostartPage'
        }
        # Pierwsze logowanie świeżego konta: OneDriveSetup /thfirstsetup usuwa swoją wartość Run po instalacji OneDrive (zmierzone na DSO-EN-Pro).
        $script:AutostartFirstLogon = [pscustomobject]@{ Enabled = 4; Disabled = 1; MissingFile = 0; Detail = 'cli przed' }
    }
    It 'odczyty CLI przed i po sondzie równe: porównanie jak przy jednym odczycie, bez dopisku o zmianie' {
        $Check = Get-AutostartWindowCheck (New-UiProbe @{}) $script:AutostartOk ([pscustomobject]@{ Enabled = 3; Disabled = 1; MissingFile = 0; Detail = 'cli po' })
        $Check.Passed | Should -BeTrue
        $Check.Detail | Should -BeLike '*Włączone: 3 (CLI 3)*Wyłączone: 1 (CLI 1)*Bez pliku: 0 (CLI 0)*'
        $Check.Detail | Should -Not -BeLike '*zmieniła się*'
        $Check = Get-AutostartWindowCheck (New-UiProbe @{ AutostartGroupNames = @('Włączone, 4', 'Wyłączone, 1', 'Bez pliku, 0') }) $script:AutostartOk $script:AutostartOk
        $Check.Passed | Should -BeFalse
        $Check.Detail | Should -BeLike '*NIE: Włączone: 4 (CLI 3)*'
    }
    It 'lista zmieniła się między odczytami CLI: okno zgodne z odczytem <Ktory> przechodzi, opis podaje oba' -ForEach @(
        @{ Ktory = 'po sondzie'; Names = @('Włączone, 3', 'Wyłączone, 1', 'Bez pliku, 0') }
        @{ Ktory = 'przed sondą'; Names = @('Włączone, 4', 'Wyłączone, 1', 'Bez pliku, 0') }
    ) {
        $Check = Get-AutostartWindowCheck (New-UiProbe @{ AutostartGroupNames = $Names }) $script:AutostartFirstLogon $script:AutostartOk
        $Check.Passed | Should -BeTrue
        $Check.Detail | Should -BeLike '*Włączone: * (CLI 4 przed, 3 po)*Wyłączone: 1 (CLI 1)*'
        $Check.Detail | Should -BeLike '*lista CLI zmieniła się w trakcie sondy (przed: cli przed; po: cli)'
    }
    It 'lista zmieniła się: nie przechodzi, gdy okno nie jest zgodne z żadnym odczytem (<Powod>)' -ForEach @(
        @{ Powod = 'liczba spoza obu odczytów'; Names = @('Włączone, 5', 'Wyłączone, 1', 'Bez pliku, 0'); Like = '*NIE: Włączone: 5 (CLI 4 przed, 3 po)*' }
        @{ Powod = 'grupy z różnych odczytów'; Names = @('Włączone, 3', 'Wyłączone, 2', 'Bez pliku, 0'); Like = '*Wyłączone: 2 (CLI 2 przed, 1 po)*NIE: liczby grup nie pochodzą z jednego odczytu CLI*' }
    ) {
        $Before = [pscustomobject]@{ Enabled = 4; Disabled = 2; MissingFile = 0; Detail = 'cli przed' }
        if ($Powod -eq 'liczba spoza obu odczytów') { $Before = $script:AutostartFirstLogon }
        $Check = Get-AutostartWindowCheck (New-UiProbe @{ AutostartGroupNames = $Names }) $Before $script:AutostartOk
        $Check.Passed | Should -BeFalse
        $Check.Detail | Should -BeLike $Like
    }
    It 'nie przechodzi bez liczb z odczytu CLI po sondzie, a opis niesie powód' {
        $Check = Get-AutostartWindowCheck (New-UiProbe @{}) $script:AutostartOk ([pscustomobject]@{ Enabled = $null; Disabled = $null; MissingFile = $null; Detail = '--list-autostart: limit czasu' })
        $Check.Passed | Should -BeFalse
        $Check.Detail | Should -BeLike '*brak oczekiwanych liczb*--list-autostart: limit czasu*'
    }
    It 'przechodzi: pozycja menu, odczyt skończony, liczby grup = CLI, „Wyłącz (0)” i „Włącz (0)” nieaktywne' {
        $Check = Get-AutostartCheck (New-UiProbe @{})
        $Check.Passed | Should -BeTrue
        $Check.Detail | Should -BeLike '*pozycja menu*odczyt skończony*Włączone: 3 (CLI 3)*Wyłączone: 1 (CLI 1)*Bez pliku: 0 (CLI 0)*„Wyłącz (0)” nieaktywny*„Włącz (0)” nieaktywny*'
    }
    It 'opis podaje, czy sonda znalazła kontener strony (tylko informacja, bez wpływu na wynik)' {
        $Check = Get-AutostartCheck (New-UiProbe @{})
        $Check.Detail | Should -BeLike '*kontener strony: tak*'
        $Check = Get-AutostartCheck (New-UiProbe @{ AutostartPageScoped = $false })
        $Check.Passed | Should -BeTrue
        $Check.Detail | Should -BeLike '*kontener strony: nie*'
    }
    It 'pusta lista po obu stronach: opis ma dopisek „wierszy 0”, sprawdzenie dalej zaliczone' {
        $Zero = [pscustomobject]@{ Enabled = 0; Disabled = 0; MissingFile = 0; Detail = 'cli' }
        $Check = Get-AutostartCheck (New-UiProbe @{ AutostartGroupNames = @('Włączone, 0', 'Wyłączone, 0', 'Bez pliku, 0') }) $Zero
        $Check.Passed | Should -BeTrue
        $Check.Detail | Should -BeLike '*wierszy 0*'
        (Get-AutostartCheck (New-UiProbe @{})).Detail | Should -Not -BeLike '*wierszy 0*'
    }
    It 'kolejność pozycji przełącznika nie ma znaczenia' {
        (Get-AutostartCheck (New-UiProbe @{ AutostartGroupNames = @('Bez pliku, 0', 'Włączone, 3', 'Wyłączone, 1') })).Passed | Should -BeTrue
    }
    It 'nie przechodzi, gdy <Powod>' -ForEach @(
        @{ Powod = 'liczba włączonych różni się od CLI'; Override = @{ AutostartGroupNames = @('Włączone, 4', 'Wyłączone, 1', 'Bez pliku, 0') }; Like = '*NIE: Włączone: 4 (CLI 3)*' }
        @{ Powod = 'liczba wyłączonych różni się od CLI'; Override = @{ AutostartGroupNames = @('Włączone, 3', 'Wyłączone, 0', 'Bez pliku, 0') }; Like = '*NIE: Wyłączone: 0 (CLI 1)*' }
        @{ Powod = 'liczba bez pliku różni się od CLI'; Override = @{ AutostartGroupNames = @('Włączone, 3', 'Wyłączone, 1', 'Bez pliku, 2') }; Like = '*NIE: Bez pliku: 2 (CLI 0)*' }
        @{ Powod = 'brakuje grupy w przełączniku'; Override = @{ AutostartGroupNames = @('Włączone, 3', 'Wyłączone, 1') }; Like = '*NIE: Bez pliku: brak w przełączniku grup (CLI 0)*' }
        @{ Powod = 'nazwa grupy bez licznika (stara strona)'; Override = @{ AutostartGroupNames = @('Włączone', 'Wyłączone, 1', 'Bez pliku, 0') }; Like = '*NIE: Włączone: brak w przełączniku grup*' }
        @{ Powod = 'pusty przełącznik grup'; Override = @{ AutostartGroupNames = @() }; Like = '*NIE: Włączone: brak w przełączniku grup*' }
        @{ Powod = 'brak pozycji menu'; Override = @{ AutostartMenuFound = $false }; Like = '*NIE: pozycja menu*' }
        @{ Powod = 'odczyt się nie skończył w 120 s'; Override = @{ AutostartLoaded = $false }; Like = '*NIE: odczyt skończony w 120 s*' }
        @{ Powod = 'odczyt listy się nie udał'; Override = @{ AutostartReadFailed = $true }; Like = '*NIE: odczyt bez błędu*' }
        @{ Powod = '„Wyłącz (0)” jest aktywny'; Override = @{ AutostartDisableEnabled = $true }; Like = '*NIE: „Wyłącz (0)” nieaktywny*' }
        @{ Powod = '„Włącz (N)” ma N > 0 (coś zaznaczone)'; Override = @{ AutostartEnableText = 'Włącz (2)' }; Like = '*NIE: „Włącz (0)” nieaktywny (przycisk ''Włącz (2)''*' }
        @{ Powod = 'brak przycisku „Wyłącz (N)”'; Override = @{ AutostartDisableText = $null; AutostartDisableEnabled = $null }; Like = '*NIE: „Wyłącz (0)” nieaktywny (brak przycisku)*' }
        @{ Powod = 'sonda zgłosiła błąd kroku'; Override = @{ AutostartError = 'Brak listy Grupy autostartu' }; Like = '*sonda: Brak listy Grupy autostartu*' }
    ) {
        $Check = Get-AutostartCheck (New-UiProbe $Override)
        $Check.Passed | Should -BeFalse
        $Check.Detail | Should -BeLike $Like
    }
    It 'nie przechodzi bez oczekiwanych liczb (brak wyniku CLI albo liczby $null przy błędzie CLI), a opis niesie powód' {
        $Check = Get-AutostartCheck (New-UiProbe @{}) $null
        $Check.Passed | Should -BeFalse
        $Check.Detail | Should -BeLike '*brak oczekiwanych liczb*'
        $Check = Get-AutostartCheck (New-UiProbe @{}) ([pscustomobject]@{ Enabled = $null; Disabled = $null; MissingFile = $null; Detail = '--list-autostart: kod 1' })
        $Check.Passed | Should -BeFalse
        $Check.Detail | Should -BeLike '*--list-autostart: kod 1*'
    }
    It 'nie przechodzi, gdy sonda nie ma pól kroku 12 (stara sonda)' {
        $Probe = New-UiProbe @{}
        $Without = [pscustomobject]@{}
        foreach ($P in $Probe.PSObject.Properties | Where-Object { $_.Name -notlike 'Autostart*' }) { $Without | Add-Member -NotePropertyName $P.Name -NotePropertyValue $P.Value }
        $Check = Get-AutostartCheck $Without
        $Check.Passed | Should -BeFalse
        $Check.Detail | Should -BeLike 'brak pola AutostartMenuFound*'
    }
    It 'brak pól kroku 11 nie pomija sprawdzenia kroku 12' {
        $Probe = New-UiProbe @{}
        $Without = [pscustomobject]@{}
        foreach ($P in $Probe.PSObject.Properties | Where-Object { $_.Name -notlike 'Install*' }) { $Without | Add-Member -NotePropertyName $P.Name -NotePropertyValue $P.Value }
        (Get-AutostartCheck $Without).Passed | Should -BeTrue
    }
    It 'po angielsku: nazwy grup i przycisków z tabel tłumaczeń' {
        $En = Get-GuiProbeTexts -Language en
        $Probe = New-UiProbe @{
            AutostartGroupNames = @("$($En.AutostartEnabled), 3", "$($En.AutostartDisabled), 1", "$($En.AutostartMissingFile), 0")
            AutostartDisableText = $En.DisableIdle; AutostartEnableText = $En.EnableIdle
        }
        (Get-AutostartCheck $Probe $script:AutostartOk $En).Passed | Should -BeTrue
        # Polska sonda oceniana angielskim słownikiem nie przechodzi.
        (Get-AutostartCheck (New-UiProbe @{}) $script:AutostartOk $En).Passed | Should -BeFalse
    }
}

Describe 'Get-GuiUiCheck (agregacja sondy UI)' {
    It 'komplet: dziewiętnaście zaliczonych sprawdzeń w stałej kolejności' {
        $Checks = Get-GuiUiCheck -Texts $script:PlTexts -Probe (New-UiProbe @{}) -PrivacyExpected $script:PrivacyOk -AppsExpected $script:AppsOk -RepairTitles $script:RepairOk -InstallExpected $script:InstallOk -AutostartExpected $script:AutostartOk
        @($Checks.Name) | Should -Be @('HistoryMenu', 'HistoryEntryActive', 'CaptionHitTest', 'FilterRiskOpened', 'FilterRiskSelected', 'FilterShowOpened', 'FilterShowSelected', 'FilterHistoryOpened', 'FilterHistorySelected', 'RowClickPreview', 'RowDoubleClickToggles', 'RunIconRuns', 'RunIconDoubleClickOnce', 'PrivacySuggestedApply', 'AppsSuggestedRemove', 'PrepPage', 'RepairPage', 'InstallPage', 'AutostartPage')
        @($Checks | Where-Object { -not $_.Passed }).Count | Should -Be 0
        (Get-Check $Checks 'HistoryEntryActive').Detail | Should -Be 'wiersz: Szybsze otwieranie podmenu, Eksplorator i wygoda, Aktywna, 20.10.2026 21:00'
        (Get-Check $Checks 'CaptionHitTest').Detail | Should -BeLike '*przekrój x=8: 42..43: 2; 44..44: 1*'
    }
    It 'brak sondy ($null): wszystkie dziewiętnaście niezaliczone' {
        $Checks = Get-GuiUiCheck -Texts $script:PlTexts -Probe $null -PrivacyExpected $script:PrivacyOk -AppsExpected $script:AppsOk -RepairTitles $script:RepairOk -InstallExpected $script:InstallOk -AutostartExpected $script:AutostartOk
        @($Checks).Count | Should -Be 19
        @($Checks | Where-Object Passed).Count | Should -Be 0
    }
    It '<Nazwa> nie przechodzi, gdy <Powod>' -ForEach @(
        @{ Nazwa = 'HistoryMenu'; Powod = 'nie ma pozycji menu'; Override = @{ HistoryMenuFound = $false } }
        @{ Nazwa = 'HistoryMenu'; Powod = 'strona się nie otworzyła (brak listy albo filtra)'; Override = @{ HistoryPageOpened = $false } }
        @{ Nazwa = 'HistoryEntryActive'; Powod = 'wiersz ma inny stan'; Override = @{ EntryNames = @('Szybsze otwieranie podmenu, Eksplorator i wygoda, Cofnięta, 20.10.2026 21:00') } }
        @{ Nazwa = 'HistoryEntryActive'; Powod = 'aktywny jest inny wpis'; Override = @{ EntryNames = @('Inna pozycja, Eksplorator i wygoda, Aktywna, 20.10.2026 21:00') } }
        @{ Nazwa = 'HistoryEntryActive'; Powod = 'lista jest pusta'; Override = @{ EntryNames = @() } }
        @{ Nazwa = 'HistoryEntryActive'; Powod = 'nazwa ma stary kształt bez rodzaju wpisu'; Override = @{ EntryNames = @('Szybsze otwieranie podmenu, Aktywna, 20.10.2026 21:00') } }
        @{ Nazwa = 'CaptionHitTest'; Powod = 'brak pomiaru'; Override = @{ HitTest = @(); HitTestError = 'UIA' } }
    ) {
        (Get-Check (Get-GuiUiCheck -Texts $script:PlTexts -Probe (New-UiProbe $Override)) $Nazwa).Passed | Should -BeFalse
    }
    It 'CaptionHitTest nie przechodzi przy <Opis> (<Stan>)' -ForEach @(
        @{ Opis = 'HTCLIENT w dolnych pikselach paska (stan sprzed poprawki)'; Stan = 'Maximized'; Indeks = 1; Punkt = 1; Kod = 1 }
        @{ Opis = 'HTCLIENT w środku paska'; Stan = 'Maximized'; Indeks = 1; Punkt = 0; Kod = 1 }
        @{ Opis = 'HTCAPTION pod paskiem (za duży offset)'; Stan = 'Maximized'; Indeks = 1; Punkt = 2; Kod = 2 }
        @{ Opis = 'HTCAPTION pod paskiem (ramka zmiany rozmiaru nie odjęta)'; Stan = 'Normal'; Indeks = 0; Punkt = 2; Kod = 2 }
        @{ Opis = 'HTCLIENT w dolnych pikselach paska'; Stan = 'Normal'; Indeks = 0; Punkt = 1; Kod = 1 }
    ) {
        $Probe = New-UiProbe @{}
        $Probe.HitTest[$Indeks].Points[$Punkt].Ht = $Kod
        $Check = Get-Check (Get-GuiUiCheck -Texts $script:PlTexts -Probe $Probe) 'CaptionHitTest'
        $Check.Passed | Should -BeFalse
        $Check.Detail | Should -BeLike '*Normal: pasek 36..79*Maximized: pasek 0..43*'
    }
    It 'granica z przekroju (<Stan>): <Opis> nie przechodzi' -ForEach @(
        @{ Stan = 'Maximized'; Indeks = 1; Opis = 'HTCAPTION o 1 px za dolną krawędzią (y = BarBottom)'; Y = 44; Kod = 2 }
        @{ Stan = 'Maximized'; Indeks = 1; Opis = 'HTCLIENT w ostatnim wierszu paska (y = BarBottom−1)'; Y = 43; Kod = 1 }
        @{ Stan = 'Normal'; Indeks = 0; Opis = 'HTCAPTION o 1 px za dolną krawędzią (y = BarBottom)'; Y = 80; Kod = 2 }
        @{ Stan = 'Normal'; Indeks = 0; Opis = 'HTCLIENT w ostatnim wierszu paska (y = BarBottom−1)'; Y = 79; Kod = 1 }
    ) {
        $Probe = New-UiProbe @{}
        ($Probe.HitTest[$Indeks].Scan | Where-Object Y -eq $Y).Ht = $Kod
        $Check = Get-Check (Get-GuiUiCheck -Texts $script:PlTexts -Probe $Probe) 'CaptionHitTest'
        $Check.Passed | Should -BeFalse
        $Check.Detail | Should -BeLike "*$Stan`: pasek*krawędź*y=$Y=$Kod*"
    }
    It 'przekrój bez wierszy na granicy paska nie przechodzi' {
        $Probe = New-UiProbe @{}
        $Probe.HitTest[1].Scan = @([pscustomobject]@{ Y = 10; Ht = 2 })
        $Check = Get-Check (Get-GuiUiCheck -Texts $script:PlTexts -Probe $Probe) 'CaptionHitTest'
        $Check.Passed | Should -BeFalse
        $Check.Detail | Should -BeLike '*y=43=brak, y=44=brak*'
    }
    It 'HTCLIENT na środku paska (tekst komputera, stan sprzed poprawki 2026-10-26) nie przechodzi (<Stan>)' -ForEach @(
        @{ Stan = 'Normal'; Indeks = 0 }
        @{ Stan = 'Maximized'; Indeks = 1 }
    ) {
        $Probe = New-UiProbe @{}
        ($Probe.HitTest[$Indeks].Points | Where-Object { $_.XDip -eq 'srodek' -and $_.Name -eq 'mid' }).Ht = 1
        $Check = Get-Check (Get-GuiUiCheck -Texts $script:PlTexts -Probe $Probe) 'CaptionHitTest'
        $Check.Passed | Should -BeFalse
        $Check.Detail | Should -BeLike "*$Stan`: pasek*mid@srodekDIP(y=0)=1*"
    }
    It 'pomiar bez punktów na środku paska (stara sonda) nie przechodzi' {
        $Probe = New-UiProbe @{}
        $Probe.HitTest[0].Points = @($Probe.HitTest[0].Points | Where-Object { $_.XDip -ne 'srodek' })
        (Get-Check (Get-GuiUiCheck -Texts $script:PlTexts -Probe $Probe) 'CaptionHitTest').Passed | Should -BeFalse
    }
    It 'stan z pustą listą punktów nie przechodzi' {
        $Probe = New-UiProbe @{}
        $Probe.HitTest[0].Points = @()
        (Get-Check (Get-GuiUiCheck -Texts $script:PlTexts -Probe $Probe) 'CaptionHitTest').Passed | Should -BeFalse
    }
    It 'Ht = -1000 (SendMessageTimeout nie zdążył) nie przechodzi' {
        $Probe = New-UiProbe @{}
        $Probe.HitTest[1].Points[0].Ht = -1000
        $Check = Get-Check (Get-GuiUiCheck -Texts $script:PlTexts -Probe $Probe) 'CaptionHitTest'
        $Check.Passed | Should -BeFalse
        $Check.Detail | Should -BeLike '*mid@8DIP(y=0)=-1000*'
    }
    It 'brak pomiaru jednego ze stanów nie przechodzi' {
        $Probe = New-UiProbe @{}
        $Probe.HitTest = @($Probe.HitTest[1])
        $Check = Get-Check (Get-GuiUiCheck -Texts $script:PlTexts -Probe $Probe) 'CaptionHitTest'
        $Check.Passed | Should -BeFalse
        $Check.Detail | Should -BeLike '*Normal: brak pomiaru*'
    }
    It '<Nazwa> nie przechodzi, gdy pole wyniku to false' -ForEach @(
        @{ Nazwa = 'FilterRiskOpened' }
        @{ Nazwa = 'FilterRiskSelected' }
        @{ Nazwa = 'FilterShowOpened' }
        @{ Nazwa = 'FilterShowSelected' }
        @{ Nazwa = 'FilterHistoryOpened' }
        @{ Nazwa = 'FilterHistorySelected' }
    ) {
        $Checks = Get-GuiUiCheck -Texts $script:PlTexts -Probe (New-UiProbe @{ $Nazwa = $false }) -PrivacyExpected $script:PrivacyOk -AppsExpected $script:AppsOk -RepairTitles $script:RepairOk -InstallExpected $script:InstallOk -AutostartExpected $script:AutostartOk
        $Check = Get-Check $Checks $Nazwa
        $Check.Passed | Should -BeFalse
        $Check.Detail | Should -BeLike 'NIE: *'
        @($Checks | Where-Object { -not $_.Passed }).Count | Should -Be 1
    }
    It '<Nazwa> nie przechodzi, gdy sonda nie ma pola wyniku (stara sonda)' -ForEach @(
        @{ Nazwa = 'FilterRiskOpened' }
        @{ Nazwa = 'FilterRiskSelected' }
        @{ Nazwa = 'FilterShowOpened' }
        @{ Nazwa = 'FilterShowSelected' }
        @{ Nazwa = 'FilterHistoryOpened' }
        @{ Nazwa = 'FilterHistorySelected' }
    ) {
        $Probe = New-UiProbe @{}
        $Without = [pscustomobject]@{}
        foreach ($P in $Probe.PSObject.Properties | Where-Object { $_.Name -ne $Nazwa }) { $Without | Add-Member -NotePropertyName $P.Name -NotePropertyValue $P.Value }
        $Check = Get-Check (Get-GuiUiCheck -Texts $script:PlTexts -Probe $Without) $Nazwa
        $Check.Passed | Should -BeFalse
        $Check.Detail | Should -BeLike "brak pola $Nazwa*"
    }
    It '<Nazwa> (wiersze listy) nie przechodzi, gdy pole wyniku to false' -ForEach @(
        @{ Nazwa = 'RowClickPreview' }
        @{ Nazwa = 'RowDoubleClickToggles' }
        @{ Nazwa = 'RunIconRuns' }
        @{ Nazwa = 'RunIconDoubleClickOnce' }
    ) {
        $Checks = Get-GuiUiCheck -Texts $script:PlTexts -Probe (New-UiProbe @{ $Nazwa = $false }) -PrivacyExpected $script:PrivacyOk -AppsExpected $script:AppsOk -RepairTitles $script:RepairOk -InstallExpected $script:InstallOk -AutostartExpected $script:AutostartOk
        $Check = Get-Check $Checks $Nazwa
        $Check.Passed | Should -BeFalse
        $Check.Detail | Should -BeLike 'NIE: *'
        @($Checks | Where-Object { -not $_.Passed }).Count | Should -Be 1
    }
    It '<Nazwa> (wiersze listy) nie przechodzi, gdy sonda nie ma pola wyniku (stara sonda)' -ForEach @(
        @{ Nazwa = 'RowClickPreview' }
        @{ Nazwa = 'RowDoubleClickToggles' }
        @{ Nazwa = 'RunIconRuns' }
        @{ Nazwa = 'RunIconDoubleClickOnce' }
    ) {
        $Probe = New-UiProbe @{}
        $Without = [pscustomobject]@{}
        foreach ($P in $Probe.PSObject.Properties | Where-Object { $_.Name -ne $Nazwa }) { $Without | Add-Member -NotePropertyName $P.Name -NotePropertyValue $P.Value }
        $Check = Get-Check (Get-GuiUiCheck -Texts $script:PlTexts -Probe $Without) $Nazwa
        $Check.Passed | Should -BeFalse
        $Check.Detail | Should -BeLike "brak pola $Nazwa*"
    }
    It 'błąd klikania wierszy trafia do opisu sprawdzeń wierszy, a nie do filtrów' {
        $Checks = Get-GuiUiCheck -Texts $script:PlTexts -Probe (New-UiProbe @{ RunIconRuns = $false; RowError = 'panel: Brak listy Pozycje.' })
        (Get-Check $Checks 'RunIconRuns').Detail | Should -BeLike '*wiersze: panel: Brak listy Pozycje.*'
        (Get-Check $Checks 'RowClickPreview').Passed | Should -BeTrue
        (Get-Check $Checks 'FilterRiskOpened').Detail | Should -Not -BeLike '*wiersze:*'
    }
    It 'błąd klikania filtrów trafia do opisu sprawdzeń filtrów' {
        $Check = Get-Check (Get-GuiUiCheck -Texts $script:PlTexts -Probe (New-UiProbe @{ FilterRiskOpened = $false; FilterError = 'Brak ComboBox' })) 'FilterRiskOpened'
        $Check.Passed | Should -BeFalse
        $Check.Detail | Should -BeLike '*filtry: Brak ComboBox*'
    }
    It 'błąd jednego pola filtra (z nazwą pola) nie unieważnia wyniku drugiego, a opis niesie oba błędy' {
        $Probe = New-UiProbe @{ FilterRiskOpened = $true; FilterRiskSelected = $false; FilterError = 'Ryzyko: lista nie zamknela sie po wyborze opcji; Filtr: Brak ComboBox' }
        $Checks = Get-GuiUiCheck -Texts $script:PlTexts -Probe $Probe
        (Get-Check $Checks 'FilterRiskOpened').Passed | Should -BeTrue
        (Get-Check $Checks 'FilterHistoryOpened').Passed | Should -BeTrue
        $Selected = Get-Check $Checks 'FilterRiskSelected'
        $Selected.Passed | Should -BeFalse
        $Selected.Detail | Should -BeLike '*Ryzyko: lista nie zamknela sie*Filtr: Brak ComboBox*'
    }
    It 'błędy zastosowania i odczytu historii trafiają do opisu wiersza historii' {
        $Check = Get-Check (Get-GuiUiCheck -Texts $script:PlTexts -Probe (New-UiProbe @{ EntryNames = @(); ApplyError = 'brak przycisku'; HistoryError = 'brak listy' })) 'HistoryEntryActive'
        $Check.Detail | Should -BeLike '*zastosowanie: brak przycisku*'
        $Check.Detail | Should -BeLike '*historia: brak listy*'
    }
    It 'błąd sondy jest w opisie HistoryMenu' {
        (Get-Check (Get-GuiUiCheck -Texts $script:PlTexts -Probe (New-UiProbe @{ Ok = $false; Error = 'brak okna'; HistoryMenuFound = $false })) 'HistoryMenu').Detail | Should -BeLike '*sonda: brak okna*'
    }
}

Describe 'Get-GuiProbeTexts (słownik tekstów sondy)' {
    BeforeAll {
        $script:Pl = Get-GuiProbeTexts -Language pl
        $script:En = Get-GuiProbeTexts -Language en
        # Nazwy własne: ten sam tekst w obu językach (PrepText.WindowsUpdateTitle to const bez L.T; „Menu” i „Multimedia” mają w tabeli
        # wpis równy polskiemu).
        $script:SameInBothLanguages = @('Menu', 'Windows Update', 'Multimedia')
        function script:Get-FlatText($Texts) {
            $Flat = [ordered]@{}
            foreach ($Key in $Texts.Keys) {
                $Value = $Texts[$Key]
                if ($Value -is [System.Collections.IDictionary]) { foreach ($K in $Value.Keys) { $Flat["$Key.$K"] = $Value[$K] } }
                elseif ($Value -is [array]) { for ($I = 0; $I -lt $Value.Count; $I++) { $Flat["$Key[$I]"] = $Value[$I] } }
                else { $Flat[$Key] = $Value }
            }
            return $Flat
        }
        function script:Read-RepoSource([string]$RelativePath) {
            [System.IO.File]::ReadAllText((Join-Path $PSScriptRoot ('..\..\..\' + $RelativePath)), [System.Text.Encoding]::UTF8)
        }
        function script:Get-CatalogTitle([string]$File, [string]$Id) {
            $Items = Read-RepoSource "src\DSOptimizer.Catalog\catalog\$File" | ConvertFrom-Json
            return [string](@($Items) | Where-Object { $_.id -eq $Id }).title
        }
    }
    It 'pl i en mają te same nazwy, tablice tej samej długości' {
        @($script:En.Keys) | Should -Be @($script:Pl.Keys)
        foreach ($Key in 'RiskOptions', 'ShowOptions', 'HistoryOptions', 'PrepTitles') { @($script:En[$Key]).Count | Should -Be @($script:Pl[$Key]).Count }
        @($script:En.InstallCategoryNames.Keys) | Should -Be @($script:Pl.InstallCategoryNames.Keys)
    }
    It 'en: każda nazwa ma niepusty tekst' {
        foreach ($Entry in (Get-FlatText $script:En).GetEnumerator()) { [string]$Entry.Value | Should -Not -BeNullOrEmpty -Because $Entry.Key }
    }
    It 'en: żaden tekst nie jest równy polskiemu poza nazwami własnymi z jawnej listy' {
        $Pl = Get-FlatText $script:Pl
        $Same = foreach ($Entry in (Get-FlatText $script:En).GetEnumerator()) { if ([string]$Entry.Value -ceq [string]$Pl[$Entry.Key]) { [string]$Entry.Value } }
        @($Same | Sort-Object -Unique) | Should -Be @($script:SameInBothLanguages | Sort-Object)
    }
    It 'tytuły pozycji z JSON katalogu: ux.menu-delay (bezpieczna, bez restartu) i panel „Zarządzanie komputerem”' {
        $script:Pl.TweakTitle | Should -Be (Get-CatalogTitle 'ux.json' 'ux.menu-delay')
        $script:Pl.PanelTitle | Should -Be (Get-CatalogTitle 'panels.json' 'panels.computer-management')
        $Ux = Read-RepoSource 'src\DSOptimizer.Catalog\catalog\ux.json' | ConvertFrom-Json
        $Tweak = @($Ux) | Where-Object { $_.id -eq 'ux.menu-delay' }
        @($Tweak).Count | Should -Be 1
        $Tweak.risk | Should -Be 'safe'
        [bool]$Tweak.requiresReboot | Should -BeFalse
        $script:En.TweakTitle | Should -Be (Get-DsoUiText -Polish $script:Pl.TweakTitle -Language en)
        $script:En.PanelTitle | Should -Be (Get-DsoUiText -Polish $script:Pl.PanelTitle -Language en)
    }
    It 'polski tekst <Key> jest kluczem tłumaczenia w <File>' -ForEach @(
        @{ Key = 'HistoryPage'; File = 'src\DSOptimizer.Core\Gui\HistoryText.cs' }
        @{ Key = 'HistoryFilter'; File = 'src\DSOptimizer.Core\Gui\HistoryText.cs' }
        @{ Key = 'HistoryActive'; File = 'src\DSOptimizer.Core\Gui\HistoryText.cs' }
        @{ Key = 'ProbeCategory'; File = 'src\DSOptimizer.Core\Gui\GuiText.cs' }
        @{ Key = 'ServicePage'; File = 'src\DSOptimizer.Core\Gui\GuiText.cs' }
        @{ Key = 'ShowCombo'; File = 'src\DSOptimizer.Core\Gui\GuiText.cs' }
        @{ Key = 'PrivacyCategory'; File = 'src\DSOptimizer.Core\Gui\GuiText.cs' }
        @{ Key = 'RefreshState'; File = 'src\DSOptimizer.App\MainWindow.xaml' }
        @{ Key = 'ApplyFormat'; File = 'src\DSOptimizer.App\ViewModels\MainViewModel.cs' }
        @{ Key = 'RiskCombo'; File = 'src\DSOptimizer.App\MainWindow.xaml' }
        @{ Key = 'Minimize'; File = 'src\DSOptimizer.App\MainWindow.xaml' }
        @{ Key = 'Menu'; File = 'src\DSOptimizer.App\MainWindow.xaml' }
        @{ Key = 'Items'; File = 'src\DSOptimizer.App\MainWindow.xaml' }
        @{ Key = 'SelectSuggested'; File = 'src\DSOptimizer.App\MainWindow.xaml' }
        @{ Key = 'SelectSuggested'; File = 'src\DSOptimizer.App\Views\AppsView.xaml' }
        @{ Key = 'AppsPage'; File = 'src\DSOptimizer.Core\Apps\AppsPageText.cs' }
        @{ Key = 'AppsRefresh'; File = 'src\DSOptimizer.Core\Apps\AppsPageText.cs' }
        @{ Key = 'AppsReading'; File = 'src\DSOptimizer.Core\Apps\AppsPageText.cs' }
        @{ Key = 'AppsNotRead'; File = 'src\DSOptimizer.Core\Apps\AppsPageText.cs' }
        @{ Key = 'RemoveFormat'; File = 'src\DSOptimizer.App\ViewModels\AppsViewModel.cs' }
        @{ Key = 'PrepPage'; File = 'src\DSOptimizer.Core\Prep\PrepText.cs' }
        @{ Key = 'PrepLoading'; File = 'src\DSOptimizer.Core\Hardware\SpecificationFormatter.cs' }
        @{ Key = 'PrepOpenWindowsUpdate'; File = 'src\DSOptimizer.Core\Prep\PrepText.cs' }
        @{ Key = 'PrepWindowsUpdateNote'; File = 'src\DSOptimizer.Core\Prep\PrepText.cs' }
        @{ Key = 'PrepCardsStateLabel'; File = 'src\DSOptimizer.Core\Prep\PrepText.cs' }
        @{ Key = 'PrepGraphicsTitle'; File = 'src\DSOptimizer.Core\Prep\PrepText.cs' }
        @{ Key = 'InstallPage'; File = 'src\DSOptimizer.Core\Install\InstallText.cs' }
        @{ Key = 'InstallRefresh'; File = 'src\DSOptimizer.Core\Install\InstallText.cs' }
        @{ Key = 'InstallFormat'; File = 'src\DSOptimizer.Core\Install\InstallText.cs' }
        @{ Key = 'InstallCategoryList'; File = 'src\DSOptimizer.App\Views\InstallView.xaml' }
        @{ Key = 'InstallAppList'; File = 'src\DSOptimizer.App\Views\InstallView.xaml' }
        @{ Key = 'InstallAll'; File = 'src\DSOptimizer.Core\Install\InstallText.cs' }
        @{ Key = 'InstallSelected'; File = 'src\DSOptimizer.Core\Install\InstallText.cs' }
        @{ Key = 'InstallSummaryGitHub'; File = 'src\DSOptimizer.Core\Install\InstallText.cs' }
        @{ Key = 'InstallSummaryWinget'; File = 'src\DSOptimizer.Core\Install\InstallText.cs' }
        @{ Key = 'InstallSummaryWingetDown'; File = 'src\DSOptimizer.Core\Install\InstallText.cs' }
        @{ Key = 'InstallUnavailable'; File = 'src\DSOptimizer.Core\Install\InstallText.cs' }
        @{ Key = 'AutostartPage'; File = 'src\DSOptimizer.Core\Autostart\AutostartText.cs' }
        @{ Key = 'AutostartGroups'; File = 'src\DSOptimizer.Core\Autostart\AutostartText.cs' }
        @{ Key = 'AutostartEnabled'; File = 'src\DSOptimizer.Core\Autostart\AutostartText.cs' }
        @{ Key = 'AutostartDisabled'; File = 'src\DSOptimizer.Core\Autostart\AutostartText.cs' }
        @{ Key = 'AutostartMissingFile'; File = 'src\DSOptimizer.Core\Autostart\AutostartText.cs' }
        @{ Key = 'AutostartRefresh'; File = 'src\DSOptimizer.App\Views\AutostartView.xaml' }
        @{ Key = 'AutostartReading'; File = 'src\DSOptimizer.Core\Autostart\AutostartText.cs' }
        @{ Key = 'AutostartNotRead'; File = 'src\DSOptimizer.Core\Autostart\AutostartText.cs' }
        @{ Key = 'AutostartReadFailed'; File = 'src\DSOptimizer.Core\Autostart\AutostartText.cs' }
        @{ Key = 'DisableFormat'; File = 'src\DSOptimizer.Core\Autostart\AutostartText.cs' }
        @{ Key = 'EnableFormat'; File = 'src\DSOptimizer.Core\Autostart\AutostartText.cs' }
    ) {
        $Text = [string]$script:Pl[$Key]
        $Text | Should -Not -BeNullOrEmpty
        $Source = Read-RepoSource $File
        $Wrapped = $Source.Contains('L.T("' + $Text + '")') -or $Source.Contains('L.F("' + $Text + '"') -or $Source.Contains("{l:T '" + $Text + "'}")
        # Cudzysłowy „ ” to w PowerShellu ograniczniki napisu – tylko w napisach w apostrofach.
        $Wrapped | Should -BeTrue -Because ('„' + $Text + '” ma być owinięte w L.T/L.F/{l:T} w ' + $File)
    }
    It 'strona serwisowa i lista „Pokaż”: en ma „Repairs and tools” i „Show”, każdy tekst inny niż polski; nazw starych stron brak' {
        $script:Pl.ServicePage | Should -Be 'Naprawy i narzędzia'
        $script:Pl.ShowCombo | Should -Be 'Pokaż'
        $script:En.ServicePage | Should -Be 'Repairs and tools'
        $script:En.ShowCombo | Should -Be 'Show'
        $script:En.ServicePage | Should -Not -Be $script:Pl.ServicePage
        $script:En.ShowCombo | Should -Not -Be $script:Pl.ShowCombo
        foreach ($Dictionary in $script:Pl, $script:En) {
            $Dictionary.Contains('PanelsCategory') | Should -BeFalse
            $Dictionary.Contains('RepairCategory') | Should -BeFalse
        }
    }
    It 'opcje „Pokaż” = GuiText.ShowFilterName w kolejności listy: Wszystkie, Niezastosowane, Zastosowane, Zaznaczone (en z tabel tłumaczeń)' {
        @($script:Pl.ShowOptions) | Should -Be @('Wszystkie', 'Niezastosowane', 'Zastosowane', 'Zaznaczone')
        @($script:Pl.ShowOptions) | Should -Be @(Get-CsLiteral (Read-CoreSource 'Gui\GuiText.cs') 'ShowFilter\.\w+\s*=>\s*(?:L\.T\(\s*)?')
        @($script:En.ShowOptions).Count | Should -Be 4
        for ($I = 0; $I -lt 4; $I++) { $script:En.ShowOptions[$I] | Should -Be (Get-DsoUiText -Polish $script:Pl.ShowOptions[$I] -Language en) }
    }
    It 'opcje filtrów: Ryzyko = GuiText.RiskFilterName, Filtr = HistoryText.FilterName (w kolejności listy)' {
        @($script:Pl.RiskOptions) | Should -Be @(Get-CsLiteral (Read-CoreSource 'Gui\GuiText.cs') 'RiskFilter\.\w+\s*=>\s*(?:L\.T\(\s*)?')
        @($script:Pl.HistoryOptions) | Should -Be @(Get-CsLiteral (Read-CoreSource 'Gui\HistoryText.cs') 'HistoryFilter\.\w+\s*=>\s*(?:L\.T\(\s*)?')
    }
    It 'pl: teksty pochodne – przycisk z liczbą, wzorce N, prefiks podtytułu' {
        $script:Pl.ApplyOne | Should -Be 'Zastosuj (1)'
        $script:Pl.InstallIdle | Should -Be 'Zainstaluj (0)'
        $script:Pl.InstallSourcePrefix | Should -Be 'Źródła: winget (Microsoft) i GitHub (RustDesk) · '
        $script:Pl.InstallSourceProbePrefix | Should -Be 'Źródł'
        $script:Pl.InstallUnavailableSuffix | Should -Be 'niedostępne'
        'Zastosuj (12)' -match $script:Pl.ApplyPattern | Should -BeTrue
        $Matches[1] | Should -Be '12'
        'Usuń (3)' -match $script:Pl.RemovePattern | Should -BeTrue
        'Zainstaluj (0)' -match $script:Pl.InstallPattern | Should -BeTrue
        'Zastosuj (1) x' -match $script:Pl.ApplyPattern | Should -BeFalse
    }
    It 'en: teksty pochodne z angielskich formatów' {
        $script:En.ApplyOne | Should -Be ((Get-DsoUiText -Polish 'Zastosuj ({0})' -Language en) -f 1)
        $script:En.InstallIdle | Should -Be ((Get-DsoUiText -Polish 'Zainstaluj ({0})' -Language en) -f 0)
        $Summary = Get-DsoUiText -Polish $script:Pl.InstallSummaryGitHub -Language en
        $script:En.InstallSourcePrefix | Should -Be $Summary.Substring(0, $Summary.IndexOf('{0}'))
        $script:En.InstallSourcePrefix.StartsWith($script:En.InstallSourceProbePrefix, [System.StringComparison]::Ordinal) | Should -BeTrue
        foreach ($Key in 'InstallSummaryWinget', 'InstallSummaryWingetDown', 'InstallUnavailable') {
            ([string]$script:En[$Key]).StartsWith($script:En.InstallSourceProbePrefix, [System.StringComparison]::Ordinal) | Should -BeTrue -Because $Key
        }
        ([string]$script:En.InstallUnavailable).EndsWith($script:En.InstallUnavailableSuffix, [System.StringComparison]::Ordinal) | Should -BeTrue
        ($script:En.ApplyOne -replace '1', '27') -match $script:En.ApplyPattern | Should -BeTrue
        $Matches[1] | Should -Be '27'
        (((Get-DsoUiText -Polish 'Usuń ({0})' -Language en) -f 5) -match $script:En.RemovePattern) | Should -BeTrue
        ($script:En.InstallIdle -match $script:En.InstallPattern) | Should -BeTrue
        $script:En.ApplyOne -match $script:Pl.ApplyPattern | Should -BeFalse
    }
    It 'teksty strony Autostart (<Language>): przyciski bez zaznaczenia, wzorce N przycisków i pozycji przełącznika grup' -ForEach @(
        @{ Language = 'pl' }, @{ Language = 'en' }
    ) {
        $Texts = Get-GuiProbeTexts -Language $Language
        $Texts.DisableIdle | Should -Be ($Texts.DisableFormat -f 0)
        $Texts.EnableIdle | Should -Be ($Texts.EnableFormat -f 0)
        (($Texts.DisableFormat -f 12) -match $Texts.DisablePattern) | Should -BeTrue
        $Matches[1] | Should -Be '12'
        (($Texts.EnableFormat -f 3) -match $Texts.EnablePattern) | Should -BeTrue
        ($Texts.DisableIdle -match $Texts.EnablePattern) | Should -BeFalse
        ($Texts.EnableIdle -match $Texts.DisablePattern) | Should -BeFalse
        foreach ($Group in 'AutostartEnabled', 'AutostartDisabled', 'AutostartMissingFile') {
            $Pattern = [string]$Texts["$($Group)Pattern"]
            ("$($Texts[$Group]), 11" -match $Pattern) | Should -BeTrue -Because $Group
            $Matches[1] | Should -Be '11'
            ([string]$Texts[$Group] -match $Pattern) | Should -BeFalse -Because "$Group bez licznika"
        }
        ("$($Texts.AutostartDisabled), 2" -match $Texts.AutostartEnabledPattern) | Should -BeFalse
        ("$($Texts.AutostartEnabled), 2" -match $Texts.AutostartDisabledPattern) | Should -BeFalse
    }
    It 'wzorzec pozycji przełącznika grup = AutostartGroupOption.AutomationName („Nazwa, N”, liczba w kulturze niezmiennej)' {
        $Source = Read-RepoSource 'src\DSOptimizer.App\ViewModels\AutostartGroupOption.cs'
        $Source | Should -Match ([regex]::Escape('AutomationName => Name + ", " + Count.ToString(CultureInfo.InvariantCulture);'))
        $Source | Should -Match ([regex]::Escape('Name = AutostartText.GroupName(value);'))
        $View = Read-RepoSource 'src\DSOptimizer.App\Views\AutostartView.xaml'
        $View | Should -Match ([regex]::Escape('<Setter Property="AutomationProperties.Name" Value="{Binding AutomationName}" />'))
        $View | Should -Match ([regex]::Escape('AutomationProperties.Name="{x:Static autostart:AutostartText.GroupsName}"'))
        $View | Should -Match ([regex]::Escape('Content="{Binding EnableText}"'))
        $View | Should -Match ([regex]::Escape('Content="{Binding DisableText}"'))
    }
    It 'en: brak tłumaczenia któregokolwiek tekstu sondy to wyjątek (repo bez tabel)' {
        $Root = Join-Path $TestDrive 'repo-bez-tabel'
        $null = New-Item -ItemType Directory -Path (Join-Path $Root 'src\DSOptimizer.Core\Localization\Translations') -Force
        $null = New-Item -ItemType Directory -Path (Join-Path $Root 'src\DSOptimizer.Catalog') -Force
        Copy-Item -Path (Join-Path $PSScriptRoot '..\..\..\src\DSOptimizer.Catalog\catalog') -Destination (Join-Path $Root 'src\DSOptimizer.Catalog') -Recurse
        { Get-GuiProbeTexts -Language en -Root $Root } | Should -Throw '*brak tłumaczenia*'
        (Get-GuiProbeTexts -Language pl -Root $Root).HistoryPage | Should -Be 'Historia zmian'
    }
}

Describe 'Sonda: teksty z pliku (-TextsFile)' {
    BeforeAll { $script:ProbeText = Get-GuiUiProbeScript }
    It 'lista wymaganych tekstów ma ServicePage, ShowCombo, ShowOptions i nie ma nazw starych stron' {
        $Line = [regex]::Match($script:ProbeText, '(?m)^\$RequiredTexts = (.+)$').Groups[1].Value
        $Required = @([regex]::Matches($Line, "'(\w+)'") | ForEach-Object { $_.Groups[1].Value })
        foreach ($Name in 'ServicePage', 'ShowCombo', 'ShowOptions') { $Required | Should -Contain $Name }
        foreach ($Name in 'PanelsCategory', 'RepairCategory') { $Required | Should -Not -Contain $Name }
        $script:ProbeText | Should -Not -Match 'PanelsCategory|RepairCategory'
    }
    It 'krok 5 sprawdza listę „Pokaż” na kategorii z widocznym polem (ProbeCategory), błąd ma przedrostek z nazwy pola' {
        $script:ProbeText | Should -Match ([regex]::Escape('$ShowOptionNames = @($T.ShowOptions)'))
        $script:ProbeText | Should -Match ([regex]::Escape('$Out.FilterShowOpened = [bool]$R3.Opened'))
        $script:ProbeText | Should -Match ([regex]::Escape('$Out.FilterShowSelected = [bool]$R3.Selected'))
        $Head = $script:ProbeText.Substring(0, $script:ProbeText.IndexOf('function Add-Step'))
        foreach ($Field in 'FilterShowOpened', 'FilterShowSelected') { $Head | Should -Match $Field }
    }
    It 'skrypt nie ma kodów znaków [char]0x ani polskich liter' {
        $script:ProbeText | Should -Not -Match '\[char\]0x'
        $script:ProbeText | Should -Not -Match '[ąćęłńóśźżĄĆĘŁŃÓŚŹŻ]'
    }
    It 'skrypt czyta teksty z -TextsFile jako UTF-8 i sprawdza, czy są wszystkie' {
        $script:ProbeText | Should -Match ([regex]::Escape('[Parameter(Mandatory)][string]$TextsFile'))
        $script:ProbeText | Should -Match ([regex]::Escape('[System.IO.File]::ReadAllText($TextsFile, [System.Text.Encoding]::UTF8)'))
        $script:ProbeText | Should -Not -Match '\$Category\b|\$TweakTitle\b'
    }
    It 'słownik $T jest przypisany w skrypcie tylko raz (żadna zmienna pomocnicza go nie nadpisuje)' {
        ([regex]::Matches($script:ProbeText, '\$T\s*=(?!=)')).Count | Should -Be 1
        ([regex]::Matches($script:ProbeText, '(?i)foreach\s*\(\s*\$T\s+in\b')).Count | Should -Be 0
    }
    It 'każdy tekst $T.Nazwa w skrypcie jest w słowniku hosta i na liście wymaganych w skrypcie' {
        $Used = @([regex]::Matches($script:ProbeText, '\$T\.(?!PSObject\b)(\w+)') | ForEach-Object { $_.Groups[1].Value } | Sort-Object -Unique)
        $Used.Count | Should -BeGreaterThan 20
        $Keys = @((Get-GuiProbeTexts -Language pl).Keys)
        foreach ($Name in $Used) { $Keys | Should -Contain $Name }
        $Line = [regex]::Match($script:ProbeText, '(?m)^\$RequiredTexts = (.+)$')
        $Line.Success | Should -BeTrue
        $Required = @([regex]::Matches($Line.Groups[1].Value, "'(\w+)'") | ForEach-Object { $_.Groups[1].Value })
        @($Required | Sort-Object) | Should -Be $Used
    }
    It 'skrypt nie ma polskich nazw elementów okna jako literałów (wszystkie z pliku tekstów)' {
        $Pl = Get-GuiProbeTexts -Language pl
        $Flat = foreach ($Key in $Pl.Keys) { $V = $Pl[$Key]; if ($V -is [System.Collections.IDictionary]) { $V.Values } else { $V } }
        # Lista nazw wymaganych tekstów to nazwy kluczy, nie teksty okna (klucz 'Menu' = tekst 'Menu').
        $Body = [regex]::Replace($script:ProbeText, '(?m)^\$RequiredTexts = .+$', '')
        foreach ($Text in @($Flat | ForEach-Object { [string]$_ } | Where-Object { $_ -match '^[ -~]+$' } | Sort-Object -Unique)) {
            $Body.Contains("'$Text'") | Should -BeFalse -Because "literał '$Text'"
        }
    }
}

Describe 'Get-GuiUiCheck po angielsku (-Texts en)' {
    It 'angielska sonda z angielskimi tekstami: dziewiętnaście zaliczonych' {
        $En = Get-GuiProbeTexts -Language en
        $Probe = New-UiProbe @{
            EntryNames = @("$($En.TweakTitle), $($En.ProbeCategory), $($En.HistoryActive), 10/20/2026 9:00 PM")
            PrepTitles = @($En.PrepTitles); PrepGraphicsLines = @('Microsoft Hyper-V Video', '10.0.26100.1')
            InstallSubtitle = ($En.InstallSourcePrefix + '35 apps · installed: 0'); InstallButtonText = $En.InstallIdle
            InstallCategories = @($En.InstallAll, $En.InstallSelected, 'Browsers')
            AutostartGroupNames = @("$($En.AutostartEnabled), 3", "$($En.AutostartDisabled), 1", "$($En.AutostartMissingFile), 0")
            AutostartDisableText = $En.DisableIdle; AutostartEnableText = $En.EnableIdle
        }
        $Install = [pscustomobject]@{ Titles = @('Google Chrome', '7-Zip'); Categories = @($En.InstallAll, $En.InstallSelected, 'Browsers') }
        $Checks = Get-GuiUiCheck -Probe $Probe -Texts $En -PrivacyExpected $script:PrivacyOk -AppsExpected $script:AppsOk -RepairTitles $script:RepairOk -InstallExpected $Install -AutostartExpected $script:AutostartOk
        @($Checks | Where-Object { -not $_.Passed } | ForEach-Object { "$($_.Name): $($_.Detail)" }) | Should -Be @()
        (Get-Check $Checks 'PrepPage').Detail | Should -BeLike "*$($En.PrepOpenWindowsUpdate)*"
    }
    It 'polska sonda z angielskimi tekstami nie przechodzi (wiersz historii, Przygotowanie, Instalacja, Autostart)' {
        $En = Get-GuiProbeTexts -Language en
        $Checks = Get-GuiUiCheck -Probe (New-UiProbe @{}) -Texts $En -PrivacyExpected $script:PrivacyOk -AppsExpected $script:AppsOk -RepairTitles $script:RepairOk -InstallExpected $script:InstallOk -AutostartExpected $script:AutostartOk
        foreach ($Name in 'HistoryEntryActive', 'PrepPage', 'InstallPage', 'AutostartPage') { (Get-Check $Checks $Name).Passed | Should -BeFalse -Because $Name }
    }
    It 'krok „Grafika” z samą angielską etykietą stanu („Graphics cards”) nie przechodzi' {
        $En = Get-GuiProbeTexts -Language en
        $Probe = New-UiProbe @{ PrepTitles = @($En.PrepTitles); PrepGraphicsLines = @($En.PrepCardsStateLabel, 'No graphics card found') }
        (Get-Check (Get-GuiUiCheck -Probe $Probe -Texts $En) 'PrepPage').Passed | Should -BeFalse
    }
}

Describe 'Get-CsText / Get-CsLiteral (czytanie owiniętych tekstów ze źródeł C#)' {
    It 'rozpoznaje L.T we właściwości, w switchu i stare const string oraz odcytowuje cudzysłowy i ukośniki' {
        $Src = @'
public static string A => L.T("Pierwszy \"cytat\" i \\ ukośnik");
public static string B { get; } = L.T("Drugi");
public const string C = "Trzeci";
InstallCategory.Tools => L.T("Narzędzia"),
'@
        Get-CsText $Src 'A' | Should -Be 'Pierwszy "cytat" i \ ukośnik'
        Get-CsText $Src 'B' | Should -Be 'Drugi'
        Get-CsText $Src 'C' | Should -Be 'Trzeci'
        @(Get-CsLiteral $Src 'InstallCategory\.Tools\s*=>\s*(?:L\.T\(\s*)?') | Should -Be @('Narzędzia')
    }
    It 'brak tekstu to błąd, nie pusty wynik' {
        { Get-CsText 'public static string A => Foo();' 'A' } | Should -Throw '*Nie znaleziono*'
    }
}

AfterAll { Clear-NoLiveVm }
