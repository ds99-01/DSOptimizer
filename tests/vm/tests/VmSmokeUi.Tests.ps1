BeforeAll {
    # Wyłącznik: żaden test na mockach nie może dotknąć prawdziwego Hyper-V (patrz Support\NoLiveVm.ps1).
    . (Join-Path $PSScriptRoot 'Support\NoLiveVm.ps1')

    Import-Module (Join-Path $PSScriptRoot '..\modules\VmSmokeUi.psm1') -Force
    $script:ModulePath = (Resolve-Path (Join-Path $PSScriptRoot '..\modules\VmSmokeUi.psm1')).Path
    function script:New-HitPoint([string]$Name, [int]$Ht, $XDip = 8) { [pscustomobject]@{ Name = $Name; XDip = $XDip; X = $XDip; Y = 0; Ht = $Ht } }
    function script:New-UiProbe([hashtable]$Override) {
        $Probe = [ordered]@{
            Ok = $true; Error = $null; HistoryMenuFound = $true; HistoryPageOpened = $true; ApplyError = $null; HistoryError = $null
            EntryNames = @('Szybsze otwieranie podmenu, Explorer i wygoda, Aktywna, 20.10.2026 21:00'); HitTestError = $null
            FilterRiskOpened = $true; FilterRiskSelected = $true; FilterHistoryOpened = $true; FilterHistorySelected = $true; FilterError = $null
            RowClickPreview = $true; RowDoubleClickToggles = $true; RunIconRuns = $true; RunIconDoubleClickOnce = $true; RowError = $null
            PrivacyButtonFound = $true; PrivacyApplyCount = 2; PrivacyApplyText = 'Zastosuj (2)'; PrivacyError = $null
            AppsListRead = $true; AppsButtonFound = $true; AppsRemoveCount = 8; AppsRemoveText = 'Usuń (8)'; AppsError = $null
            PrepMenuFound = $true; PrepLoaded = $true; PrepWindowsUpdateButton = $true; PrepWindowsUpdateNote = $false; PrepError = $null
            PrepTitles = @('Komputer', 'Windows Update', 'Opcjonalne aktualizacje sterowników', 'Sterowniki chipsetu', 'Grafika', 'Wsparcie producenta', 'Urządzenia bez sterownika')
            PrepGraphicsLines = @('Microsoft Hyper-V Video', '10.0.26100.1')
            RepairMenuFound = $true; RepairListFound = $true; RepairError = $null
            RepairTexts = @('Naprawa A', 'Opis naprawy A', 'Naprawa B', 'Opis naprawy B')
            InstallMenuFound = $true; InstallLoaded = $true; InstallButtonText = 'Zainstaluj (0)'; InstallButtonEnabled = $false; InstallError = $null
            InstallCategories = @('Wszystkie', 'Zaznaczone', 'Przeglądarki', 'Narzędzia'); InstallSubtitle = 'Źródła: winget (Microsoft) i GitHub (RustDesk) · 34 aplikacje · zainstalowane: 3'
            InstallTexts = @('Google Chrome', 'Google.Chrome', 'Przeglądarka internetowa Google.', '7-Zip', '7zip.7zip', 'Zainstalowana')
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
    # Oczekiwane N obu stron z kompletem zgodnym z New-UiProbe (2 sugerowane privacy, 8 sugerowanych aplikacji).
    $script:PrivacyOk = [pscustomobject]@{ Count = 2; Detail = 'det' }
    $script:AppsOk = [pscustomobject]@{ Count = 8; Detail = 'lista' }
    # Oczekiwane tytuły strony „Naprawy” zgodne z New-UiProbe (RepairTexts).
    $script:RepairOk = @('Naprawa A', 'Naprawa B')
    # Oczekiwania strony „Instalacja” (Get-GuiInstallTitle) zgodne z New-UiProbe (InstallCategories, InstallTexts).
    $script:InstallOk = [pscustomobject]@{ Titles = @('Google Chrome', '7-Zip'); Categories = @('Wszystkie', 'Zaznaczone', 'Przeglądarki', 'Narzędzia') }
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
        $Text | Should -Match ([regex]::Escape('Test-FilterCombo $Category ''Ryzyko'''))
        $Text | Should -Match ([regex]::Escape('Test-FilterCombo ''Historia zmian'' ''Filtr'''))
        $Text | Should -Match 'ExpandCollapseState'
        $Text | Should -Not -Match '\.Expand\(\)|SendInput'
        foreach ($Field in 'FilterRiskOpened', 'FilterRiskSelected', 'FilterHistoryOpened', 'FilterHistorySelected', 'FilterError') { $Text | Should -Match $Field }
    }
    It 'sonda klika wiersze listy prawdziwą myszą: klik, dwuklik, znacznik uruchomienia panelu, okna mmc.exe liczone przed i po' {
        $Text = Get-GuiUiProbeScript
        foreach ($Field in 'RowClickPreview', 'RowDoubleClickToggles', 'RunIconRuns', 'RunIconDoubleClickOnce', 'RowError') { $Text | Should -Match $Field }
        $Text | Should -Match 'public static void DoubleClick\(int x, int y\)'
        $Text | Should -Match ([regex]::Escape('[DsoUser32]::DoubleClick('))
        $Text | Should -Match ([regex]::Escape("Get-Process -Name 'mmc'"))
        $Text | Should -Match ([regex]::Escape("Get-ListRow 'Panele systemowe'"))
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
        $Text | Should -Match ([regex]::Escape('try { Test-FilterCombo $Category ''Ryzyko'' $RiskOptionNames $R1 }'))
        $Text | Should -Match ([regex]::Escape('try { Test-FilterCombo ''Historia zmian'' ''Filtr'' $HistoryOptionNames $R2 }'))
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
        $Text | Should -Match ([regex]::Escape("@('Wszystkie', 'Bez eksperymentalnych', 'Tylko bezpieczne')"))
        $Text | Should -Match ([regex]::Escape("'Wszystkie wpisy', 'Aktywne', ('Czekaj' + [char]0x0105 + 'ce na restart'), ('Cofni' + [char]0x0119 + 'te i przerwane')"))
        $Text | Should -Match '^[\x00-\x7F]+\z'
    }
    It 'sonda szuka elementów po nazwach z okna: menu, przycisk serii, lista historii, filtr, pasek' {
        $Text = Get-GuiUiProbeScript
        foreach ($Name in "'Historia zmian'", "'Zastosuj (1)'", "'Filtr'", "'Minimalizuj'", "'Menu'") { $Text | Should -Match ([regex]::Escape($Name)) }
    }
    It 'cel sondy to pozycja bezpieczna bez potwierdzenia: ASCII bez cudzysłowów (argumenty zadania)' {
        $Target = Get-GuiUiProbeTarget
        $Target.Category | Should -Be 'Explorer i wygoda'
        $Target.TweakTitle | Should -Be 'Szybsze otwieranie podmenu'
        foreach ($V in $Target.Values) { $V | Should -Match '^[ -~]+\z'; $V | Should -Not -Match '"' }
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
        $script:PrivacyName = "'Prywatno' + [char]0x015B + [char]0x0107 + ' i AI'"
    }
    It 'nazwa kategorii składana z kodów znaków występuje w skrypcie dokładnie raz' {
        ([regex]::Matches($script:ProbeText, [regex]::Escape($script:PrivacyName))).Count | Should -Be 1
    }
    It 'sonda wchodzi do „Prywatność i AI” dopiero po ścieżce ux.menu-delay („Zastosuj (1)”) i po sprzątaniu kroku 6 (Review Focus 1)' {
        $Privacy = $script:ProbeText.IndexOf($script:PrivacyName)
        $Privacy | Should -BeGreaterThan $script:ProbeText.IndexOf("'Zastosuj (1)'")
        $Privacy | Should -BeGreaterThan $script:ProbeText.IndexOf('$Out.RowDoubleClickToggles =')
        $Privacy | Should -BeGreaterThan $script:ProbeText.IndexOf('finally { try { Close-NewMmc $BaseMmc; Wait-AppIdle 90 }')
        $Privacy | Should -BeGreaterThan $script:ProbeText.IndexOf('# 7.')
        $script:ProbeText.IndexOf('# 7.') | Should -BeGreaterThan $script:ProbeText.IndexOf('# 6.')
    }
    It 'sonda czyta N z przycisku „Zastosuj (N)” i czeka na stałą wartość przy bezczynnej aplikacji' {
        $script:ProbeText | Should -Match ([regex]::Escape("'^Zastosuj \((\d+)\)$'"))
        foreach ($Field in 'PrivacyApplyCount', 'PrivacyApplyText', 'PrivacyButtonFound', 'PrivacyError') { $script:ProbeText | Should -Match $Field }
        $Step = $script:ProbeText.Substring($script:ProbeText.IndexOf('# 7.'))
        $Step | Should -Match 'Wait-AppIdle'
        $Step | Should -Not -Match 'Invoke\(\)'
        $Step | Should -Not -Match 'Toggle\(\)'
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
    It 'wbudowany katalog: 11 sugerowanych pozycji privacy, bez „Proponowane” w Start' {
        $Dir = Join-Path $PSScriptRoot '..\..\..\src\DSOptimizer.Catalog\catalog'
        $Ids = @(Get-GuiPrivacySuggestedId -CatalogDirectory $Dir)
        $Ids.Count | Should -Be 11
        $Ids | Should -Contain 'privacy.advertising-id'
        $Ids | Should -Contain 'privacy.tailored-experiences'
        $Ids | Should -Contain 'privacy.diagtrack'
        $Ids | Should -Not -Contain 'privacy.start-recommended-section'
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
        $Check = Get-Check (Get-GuiUiCheck -Probe (New-UiProbe @{}) -PrivacyExpected ([pscustomobject]@{ Count = 2; Detail = 'det' })) 'PrivacySuggestedApply'
        $Check.Passed | Should -BeTrue
        $Check.Detail | Should -BeLike "*Zastosuj (2)*oczekiwano 2*det*"
    }
    It 'nie przechodzi, gdy N z przycisku różni się od detekcji (opis niesie obie liczby)' {
        $Check = Get-Check (Get-GuiUiCheck -Probe (New-UiProbe @{ PrivacyApplyCount = 3; PrivacyApplyText = 'Zastosuj (3)' }) -PrivacyExpected ([pscustomobject]@{ Count = 2; Detail = 'det' })) 'PrivacySuggestedApply'
        $Check.Passed | Should -BeFalse
        $Check.Detail | Should -BeLike '*Zastosuj (3)*oczekiwano 2*'
    }
    It 'nie przechodzi bez oczekiwanego N (brak lub błąd detekcji)' {
        $Check = Get-Check (Get-GuiUiCheck -Probe (New-UiProbe @{}) -PrivacyExpected ([pscustomobject]@{ Count = $null; Detail = 'p.b: brak raportu' })) 'PrivacySuggestedApply'
        $Check.Passed | Should -BeFalse
        $Check.Detail | Should -BeLike '*p.b: brak raportu*'
        (Get-Check (Get-GuiUiCheck -Probe (New-UiProbe @{})) 'PrivacySuggestedApply').Passed | Should -BeFalse
    }
    It 'nie przechodzi, gdy sonda nie ma pola wyniku (stara sonda) albo nie znalazła przycisku przy N > 0' {
        $Probe = New-UiProbe @{}
        $Without = [pscustomobject]@{}
        foreach ($P in $Probe.PSObject.Properties | Where-Object { $_.Name -notlike 'Privacy*' }) { $Without | Add-Member -NotePropertyName $P.Name -NotePropertyValue $P.Value }
        $Expected = [pscustomobject]@{ Count = 2; Detail = 'det' }
        (Get-Check (Get-GuiUiCheck -Probe $Without -PrivacyExpected $Expected) 'PrivacySuggestedApply').Passed | Should -BeFalse
        $NoButton = New-UiProbe @{ PrivacyButtonFound = $false; PrivacyApplyCount = $null; PrivacyApplyText = $null; PrivacyError = 'brak przycisku' }
        $Check = Get-Check (Get-GuiUiCheck -Probe $NoButton -PrivacyExpected $Expected) 'PrivacySuggestedApply'
        $Check.Passed | Should -BeFalse
        $Check.Detail | Should -BeLike '*brak przycisku*'
    }
    It 'N = 0: przechodzi przy „Zastosuj (0)” albo przy braku przycisku bez błędu (pasek serii ukryty)' {
        $Zero = [pscustomobject]@{ Count = 0; Detail = 'det' }
        (Get-Check (Get-GuiUiCheck -Probe (New-UiProbe @{ PrivacyApplyCount = 0; PrivacyApplyText = 'Zastosuj (0)' }) -PrivacyExpected $Zero) 'PrivacySuggestedApply').Passed | Should -BeTrue
        (Get-Check (Get-GuiUiCheck -Probe (New-UiProbe @{ PrivacyButtonFound = $false; PrivacyApplyCount = $null; PrivacyApplyText = $null }) -PrivacyExpected $Zero) 'PrivacySuggestedApply').Passed | Should -BeTrue
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
    It 'wchodzi na pozycję menu „Aplikacje” i czyta N z „Usuń (N)” (nazwa z kodów znaków, skrypt ASCII)' {
        $script:Step8 | Should -Match ([regex]::Escape("Find-Element `$Root 'Aplikacje' `$ListItem"))
        $script:Step8 | Should -Match ([regex]::Escape("'^Usu' + [char]0x0144 + ' \((\d+)\)$'"))
        foreach ($Field in 'AppsListRead', 'AppsButtonFound', 'AppsRemoveCount', 'AppsRemoveText', 'AppsError') { $script:ProbeText | Should -Match $Field }
    }
    It 'czeka na koniec odczytu listy („Odśwież listę” aktywne, bez „Odczyt listy w toku”/„jeszcze nie odczytana”) i na stałą wartość' {
        $script:Step8 | Should -Match ([regex]::Escape("'Od' + [char]0x015B + 'wie' + [char]0x017C + ' list' + [char]0x0119"))
        $script:Step8 | Should -Match ([regex]::Escape("'Odczyt listy w toku'"))
        $script:Step8 | Should -Match ([regex]::Escape("'Lista nie zosta' + [char]0x0142 + 'a jeszcze odczytana'"))
        $script:Step8 | Should -Match 'IsEnabled'
        $script:Step8 | Should -Match 'TotalSeconds -ge 3'
    }
    It 'niczego nie klika ani nie przełącza (bez „Usuń”, bez pól wyboru, bez myszy)' {
        $script:Step8 | Should -Not -Match 'Invoke\(\)'
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
        $Check = Get-Check (Get-GuiUiCheck -Probe (New-UiProbe @{}) -PrivacyExpected $script:PrivacyOk -AppsExpected $script:AppsOk) 'AppsSuggestedRemove'
        $Check.Passed | Should -BeTrue
        $Check.Detail | Should -BeLike '*Usuń (8)*oczekiwano 8*lista*'
    }
    It 'nie przechodzi, gdy N z przycisku różni się od listy (opis niesie obie liczby)' {
        $Check = Get-Check (Get-GuiUiCheck -Probe (New-UiProbe @{ AppsRemoveCount = 7; AppsRemoveText = 'Usuń (7)' }) -AppsExpected $script:AppsOk) 'AppsSuggestedRemove'
        $Check.Passed | Should -BeFalse
        $Check.Detail | Should -BeLike '*Usuń (7)*oczekiwano 8*'
    }
    It 'nie przechodzi bez oczekiwanego N (brak lub błąd --list-apps)' {
        $Check = Get-Check (Get-GuiUiCheck -Probe (New-UiProbe @{}) -AppsExpected ([pscustomobject]@{ Count = $null; Detail = 'kod 4' })) 'AppsSuggestedRemove'
        $Check.Passed | Should -BeFalse
        $Check.Detail | Should -BeLike '*kod 4*'
        (Get-Check (Get-GuiUiCheck -Probe (New-UiProbe @{})) 'AppsSuggestedRemove').Passed | Should -BeFalse
    }
    It 'nie przechodzi, gdy lista nie została odczytana do końca (N sprzed odczytu)' {
        $Check = Get-Check (Get-GuiUiCheck -Probe (New-UiProbe @{ AppsListRead = $false; AppsError = 'odczyt listy nie skonczyl sie w 180 s' }) -AppsExpected $script:AppsOk) 'AppsSuggestedRemove'
        $Check.Passed | Should -BeFalse
        $Check.Detail | Should -BeLike '*sonda: odczyt listy*'
    }
    It 'nie przechodzi, gdy sonda nie ma pola wyniku (stara sonda) albo nie znalazła przycisku – także przy N = 0 (pasek serii Aplikacji jest zawsze widoczny)' {
        $Probe = New-UiProbe @{}
        $Without = [pscustomobject]@{}
        foreach ($P in $Probe.PSObject.Properties | Where-Object { $_.Name -notlike 'Apps*' }) { $Without | Add-Member -NotePropertyName $P.Name -NotePropertyValue $P.Value }
        (Get-Check (Get-GuiUiCheck -Probe $Without -AppsExpected $script:AppsOk) 'AppsSuggestedRemove').Passed | Should -BeFalse
        $NoButton = New-UiProbe @{ AppsButtonFound = $false; AppsRemoveCount = $null; AppsRemoveText = $null }
        $Zero = [pscustomobject]@{ Count = 0; Detail = 'lista' }
        $Check = Get-Check (Get-GuiUiCheck -Probe $NoButton -AppsExpected $Zero) 'AppsSuggestedRemove'
        $Check.Passed | Should -BeFalse
        $Check.Detail | Should -BeLike '*brak przycisku*'
    }
    It 'N = 0: przechodzi przy „Usuń (0)”' {
        $Zero = [pscustomobject]@{ Count = 0; Detail = 'lista' }
        (Get-Check (Get-GuiUiCheck -Probe (New-UiProbe @{ AppsRemoveCount = 0; AppsRemoveText = 'Usuń (0)' }) -AppsExpected $Zero) 'AppsSuggestedRemove').Passed | Should -BeTrue
    }
}

Describe 'Sonda: „Przygotowanie” (krok 9)' {
    BeforeAll {
        $script:ProbeText = Get-GuiUiProbeScript
        $script:Step9 = $script:ProbeText.Substring($script:ProbeText.IndexOf('# 9.'))
        if ($script:ProbeText.IndexOf('# 10.') -gt $script:ProbeText.IndexOf('# 9.')) {
            $script:Step9 = $script:ProbeText.Substring($script:ProbeText.IndexOf('# 9.'), $script:ProbeText.IndexOf('# 10.') - $script:ProbeText.IndexOf('# 9.'))
        }
        # Napis -> literał skryptu ASCII w stylu sondy: 'Urz' + [char]0x0105 + 'dzenia bez sterownika'.
        function script:ConvertTo-AsciiLiteral([string]$Text) {
            $Parts = @()
            $Run = ''
            foreach ($C in $Text.ToCharArray()) {
                if ([int]$C -lt 128) { $Run += $C; continue }
                if ($Run) { $Parts += "'$Run'"; $Run = '' }
                $Parts += ('[char]0x{0:X4}' -f [int]$C)
            }
            if ($Run) { $Parts += "'$Run'" }
            return ($Parts -join ' + ')
        }
        function script:Get-PrepConstant([string]$Pattern) {
            $Path = Join-Path $PSScriptRoot '..\..\..\src\DSOptimizer.Core\Prep\PrepText.cs'
            $Source = [System.IO.File]::ReadAllText($Path, [System.Text.Encoding]::UTF8)
            return @([regex]::Matches($Source, $Pattern) | ForEach-Object { $_.Groups[1].Value })
        }
    }
    It 'krok 9 jest po kroku 8 („Aplikacje”) i jego odczycie N, a przed krokiem 10 („Naprawy”)' {
        $script:ProbeText.IndexOf('# 9.') | Should -BeGreaterThan $script:ProbeText.IndexOf('$Out.AppsRemoveCount = [int]$Matches[1]')
        $script:Step9 | Should -Not -Match '# 10\.'
        $script:ProbeText.IndexOf('# 10.') | Should -BeGreaterThan $script:ProbeText.IndexOf('catch { $Out.PrepError = $_.Exception.Message')
    }
    It 'wchodzi na pozycję menu „Przygotowanie” wzorcem SelectionItemPattern' {
        $script:Step9 | Should -Match ([regex]::Escape("Find-Element `$Root 'Przygotowanie' `$ListItem"))
        $script:Step9 | Should -Match ([regex]::Escape('[System.Windows.Automation.SelectionItemPattern]::Pattern).Select()'))
    }
    It 'tytuły kroków hosta = tytuły z PrepText.cs (7, w kolejności), a sonda ma każdy jako literał ASCII' {
        $Expected = @(Get-PrepConstant 'public const string (?!Page)\w+Title = "([^"]+)";')
        $Expected.Count | Should -Be 7
        (Get-GuiPrepExpected).Titles | Should -Be $Expected
        foreach ($Title in $Expected) { $script:Step9 | Should -Match ([regex]::Escape((ConvertTo-AsciiLiteral $Title))) }
    }
    It 'czeka najwyżej 60 s, aż zniknie „Wczytywanie…” (PrepText.Loading) i pojawi się 7 tytułów' {
        $script:Step9 | Should -Match ([regex]::Escape("'Wczytywanie' + [char]0x2026"))
        $script:Step9 | Should -Match ([regex]::Escape('AddSeconds(60)'))
        foreach ($Field in 'PrepMenuFound', 'PrepLoaded', 'PrepTitles') { $script:Step9 | Should -Match $Field }
    }
    It 'szuka przycisku „Otwórz Windows Update” i notatki zastępczej (teksty z PrepText.cs)' {
        $Button = @(Get-PrepConstant 'OpenWindowsUpdate = "([^"]+)";')[0]
        $Note = @(Get-PrepConstant 'WindowsUpdateNote = "([^"]+)";')[0]
        $script:Step9 | Should -Match ([regex]::Escape((ConvertTo-AsciiLiteral $Button)))
        $script:Step9 | Should -Match ([regex]::Escape((ConvertTo-AsciiLiteral $Note)))
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
            Get-Check (Get-GuiUiCheck -Probe $Probe -PrivacyExpected $script:PrivacyOk -AppsExpected $script:AppsOk -SettingsOpenable $SettingsOpenable) 'PrepPage'
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
    It 'brak pól kroku 9 nie pomija sprawdzenia kroku 10 („Naprawy”)' {
        $Probe = New-UiProbe @{}
        $Without = [pscustomobject]@{}
        foreach ($P in $Probe.PSObject.Properties | Where-Object { $_.Name -notlike 'Prep*' }) { $Without | Add-Member -NotePropertyName $P.Name -NotePropertyValue $P.Value }
        $Checks = Get-GuiUiCheck -Probe $Without -PrivacyExpected $script:PrivacyOk -AppsExpected $script:AppsOk -RepairTitles $script:RepairOk -InstallExpected $script:InstallOk
        (Get-Check $Checks 'RepairPage').Passed | Should -BeTrue
    }
}

Describe 'Sonda: „Naprawy” (krok 10)' {
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
    It 'wchodzi na pozycję menu „Naprawy” (nazwa = GuiText dla kategorii repair) wzorcem SelectionItemPattern' {
        $Path = Join-Path $PSScriptRoot '..\..\..\src\DSOptimizer.Core\Gui\GuiText.cs'
        $Source = [System.IO.File]::ReadAllText($Path, [System.Text.Encoding]::UTF8)
        $Source | Should -Match ([regex]::Escape('"repair" => "Naprawy"'))
        $script:Step10 | Should -Match ([regex]::Escape("Find-Element `$Root 'Naprawy' `$ListItem"))
        $script:Step10 | Should -Match ([regex]::Escape('[System.Windows.Automation.SelectionItemPattern]::Pattern).Select()'))
    }
    It 'czyta teksty listy „Pozycje” (z przewinięciem do końca) do pól RepairMenuFound, RepairListFound, RepairTexts' {
        $script:Step10 | Should -Match ([regex]::Escape("Wait-Element `$Root 'Pozycje' ([System.Windows.Automation.ControlType]::List)"))
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
        Get-GuiRepairTitle -CatalogDirectory $Dir | Should -Be @('Napraw A', 'Ochrona B')
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
    It 'wbudowany katalog: 11 tytułów = tytuły z repair.json' {
        $Dir = Join-Path $PSScriptRoot '..\..\..\src\DSOptimizer.Catalog\catalog'
        $Items = [System.IO.File]::ReadAllText((Join-Path $Dir 'repair.json'), [System.Text.Encoding]::UTF8) | ConvertFrom-Json
        $Expected = @(@($Items) | ForEach-Object { [string]$_.title })
        $Titles = @(Get-GuiRepairTitle -CatalogDirectory $Dir)
        $Titles.Count | Should -Be 11
        $Titles | Should -Be $Expected
        $Titles | Should -Contain 'Ochrona systemu'
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
            Get-Check (Get-GuiUiCheck -Probe $Probe -PrivacyExpected $script:PrivacyOk -AppsExpected $script:AppsOk -RepairTitles $Titles -InstallExpected $script:InstallOk) 'RepairPage'
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
        $script:Step11 = $script:Step11.Substring(0, $script:Step11.IndexOf('$Out.Ok = $true'))
    }
    It 'krok 11 jest ostatni: po kroku 10 („Naprawy”) i jego obsłudze błędu' {
        $script:ProbeText.IndexOf('# 11.') | Should -BeGreaterThan $script:ProbeText.IndexOf('catch { $Out.RepairError = $_.Exception.Message')
        $script:Step11 | Should -Not -Match '# 12\.'
    }
    It 'wchodzi na pozycję menu „Instalacja” (nazwa = InstallText.PageTitle) wzorcem SelectionItemPattern' {
        $Path = Join-Path $PSScriptRoot '..\..\..\src\DSOptimizer.Core\Install\InstallText.cs'
        $Source = [System.IO.File]::ReadAllText($Path, [System.Text.Encoding]::UTF8)
        $Source | Should -Match ([regex]::Escape('public const string PageTitle = "Instalacja";'))
        $script:Step11 | Should -Match ([regex]::Escape("Find-Element `$Root 'Instalacja' `$ListItem"))
        $script:Step11 | Should -Match ([regex]::Escape('[System.Windows.Automation.SelectionItemPattern]::Pattern).Select()'))
    }
    It 'czyta listę „Kategorie”, teksty listy „Aplikacje do instalacji” w każdej kategorii (z przewijaniem) i przycisk „Zainstaluj (N)”' {
        $script:Step11 | Should -Match ([regex]::Escape("Wait-Element `$Root 'Kategorie' ([System.Windows.Automation.ControlType]::List)"))
        $script:Step11 | Should -Match ([regex]::Escape("'Aplikacje do instalacji'"))
        $script:Step11 | Should -Match ([regex]::Escape('ScrollPattern'))
        $script:Step11 | Should -Match ([regex]::Escape("'^Zainstaluj \((\d+)\)$'"))
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
        $Text = [System.IO.File]::ReadAllText((Join-Path $Core 'InstallText.cs'), [System.Text.Encoding]::UTF8)
        $Catalog = [System.IO.File]::ReadAllText((Join-Path $Core 'AppsInstallCatalog.cs'), [System.Text.Encoding]::UTF8)
        $Names = @{}
        foreach ($M in [regex]::Matches($Text, 'InstallCategory\.(\w+) => "([^"]+)"')) { $Names[$M.Groups[1].Value] = $M.Groups[2].Value }
        $Expected = foreach ($M in [regex]::Matches($Catalog, '\["([a-z-]+)"\] = InstallCategory\.(\w+)')) {
            if (@($Entries | Where-Object { [string]$_.category -eq $M.Groups[1].Value }).Count -gt 0) { $Names[$M.Groups[2].Value] }
        }
        $Names.Count | Should -Be 8
        # „Wszystkie” na górze (D1) – ta sama nazwa co InstallText.AllCategoriesName w Core.
        $All = [regex]::Match($Text, 'AllCategoriesName = "([^"]+)"')
        $All.Success | Should -BeTrue
        $Sel = [regex]::Match($Text, 'SelectedGroupName = "([^"]+)"')
        $Sel.Success | Should -BeTrue
        @($R.Categories) | Should -Be @(@($All.Groups[1].Value, $Sel.Groups[1].Value) + @($Expected))
    }
}

Describe 'Get-GuiUiCheck: InstallPage' {
    BeforeAll {
        function script:Get-InstallCheck($Probe, $Expected = $script:InstallOk) {
            Get-Check (Get-GuiUiCheck -Probe $Probe -PrivacyExpected $script:PrivacyOk -AppsExpected $script:AppsOk -RepairTitles $script:RepairOk -InstallExpected $Expected) 'InstallPage'
        }
    }
    It 'prefiks podtytułu: sonda w VM łapie tekst, który sprawdza host, a host = InstallText.PageSummary z GitHubem (M6)' {
        $ModuleText = [System.IO.File]::ReadAllText($script:ModulePath, [System.Text.Encoding]::UTF8)
        $Line = [regex]::Match($ModuleText, '(?m)^\s*\$SourcePrefix = (\[char\]0x[0-9A-F]{4}(?: \+ (?:\[char\]0x[0-9A-F]{4}|''[^'']*''))*)\s*$')
        $Line.Success | Should -BeTrue
        $ProbePrefix = Invoke-Expression $Line.Groups[1].Value
        $HostPrefix = & (Get-Module VmSmokeUi) { $script:InstallSourcePrefix }
        $HostPrefix.StartsWith($ProbePrefix, [System.StringComparison]::Ordinal) | Should -BeTrue
        $Core = [System.IO.File]::ReadAllText((Join-Path $PSScriptRoot '..\..\..\src\DSOptimizer.Core\Install\InstallText.cs'), [System.Text.Encoding]::UTF8)
        $Core.Contains('"' + $HostPrefix + '"') | Should -BeTrue
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

Describe 'Get-GuiUiCheck (agregacja sondy UI)' {
    It 'komplet: szesnaście zaliczonych sprawdzeń w stałej kolejności' {
        $Checks = Get-GuiUiCheck -Probe (New-UiProbe @{}) -PrivacyExpected $script:PrivacyOk -AppsExpected $script:AppsOk -RepairTitles $script:RepairOk -InstallExpected $script:InstallOk
        @($Checks.Name) | Should -Be @('HistoryMenu', 'HistoryEntryActive', 'CaptionHitTest', 'FilterRiskOpened', 'FilterRiskSelected', 'FilterHistoryOpened', 'FilterHistorySelected', 'RowClickPreview', 'RowDoubleClickToggles', 'RunIconRuns', 'RunIconDoubleClickOnce', 'PrivacySuggestedApply', 'AppsSuggestedRemove', 'PrepPage', 'RepairPage', 'InstallPage')
        @($Checks | Where-Object { -not $_.Passed }).Count | Should -Be 0
        (Get-Check $Checks 'HistoryEntryActive').Detail | Should -Be 'wiersz: Szybsze otwieranie podmenu, Explorer i wygoda, Aktywna, 20.10.2026 21:00'
        (Get-Check $Checks 'CaptionHitTest').Detail | Should -BeLike '*przekrój x=8: 42..43: 2; 44..44: 1*'
    }
    It 'brak sondy ($null): wszystkie szesnaście niezaliczone' {
        $Checks = Get-GuiUiCheck -Probe $null -PrivacyExpected $script:PrivacyOk -AppsExpected $script:AppsOk -RepairTitles $script:RepairOk -InstallExpected $script:InstallOk
        @($Checks).Count | Should -Be 16
        @($Checks | Where-Object Passed).Count | Should -Be 0
    }
    It '<Nazwa> nie przechodzi, gdy <Powod>' -ForEach @(
        @{ Nazwa = 'HistoryMenu'; Powod = 'nie ma pozycji menu'; Override = @{ HistoryMenuFound = $false } }
        @{ Nazwa = 'HistoryMenu'; Powod = 'strona się nie otworzyła (brak listy albo filtra)'; Override = @{ HistoryPageOpened = $false } }
        @{ Nazwa = 'HistoryEntryActive'; Powod = 'wiersz ma inny stan'; Override = @{ EntryNames = @('Szybsze otwieranie podmenu, Explorer i wygoda, Cofnięta, 20.10.2026 21:00') } }
        @{ Nazwa = 'HistoryEntryActive'; Powod = 'aktywny jest inny wpis'; Override = @{ EntryNames = @('Inna pozycja, Explorer i wygoda, Aktywna, 20.10.2026 21:00') } }
        @{ Nazwa = 'HistoryEntryActive'; Powod = 'lista jest pusta'; Override = @{ EntryNames = @() } }
        @{ Nazwa = 'HistoryEntryActive'; Powod = 'nazwa ma stary kształt bez rodzaju wpisu'; Override = @{ EntryNames = @('Szybsze otwieranie podmenu, Aktywna, 20.10.2026 21:00') } }
        @{ Nazwa = 'CaptionHitTest'; Powod = 'brak pomiaru'; Override = @{ HitTest = @(); HitTestError = 'UIA' } }
    ) {
        (Get-Check (Get-GuiUiCheck -Probe (New-UiProbe $Override)) $Nazwa).Passed | Should -BeFalse
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
        $Check = Get-Check (Get-GuiUiCheck -Probe $Probe) 'CaptionHitTest'
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
        $Check = Get-Check (Get-GuiUiCheck -Probe $Probe) 'CaptionHitTest'
        $Check.Passed | Should -BeFalse
        $Check.Detail | Should -BeLike "*$Stan`: pasek*krawędź*y=$Y=$Kod*"
    }
    It 'przekrój bez wierszy na granicy paska nie przechodzi' {
        $Probe = New-UiProbe @{}
        $Probe.HitTest[1].Scan = @([pscustomobject]@{ Y = 10; Ht = 2 })
        $Check = Get-Check (Get-GuiUiCheck -Probe $Probe) 'CaptionHitTest'
        $Check.Passed | Should -BeFalse
        $Check.Detail | Should -BeLike '*y=43=brak, y=44=brak*'
    }
    It 'HTCLIENT na środku paska (tekst komputera, stan sprzed poprawki 2026-10-26) nie przechodzi (<Stan>)' -ForEach @(
        @{ Stan = 'Normal'; Indeks = 0 }
        @{ Stan = 'Maximized'; Indeks = 1 }
    ) {
        $Probe = New-UiProbe @{}
        ($Probe.HitTest[$Indeks].Points | Where-Object { $_.XDip -eq 'srodek' -and $_.Name -eq 'mid' }).Ht = 1
        $Check = Get-Check (Get-GuiUiCheck -Probe $Probe) 'CaptionHitTest'
        $Check.Passed | Should -BeFalse
        $Check.Detail | Should -BeLike "*$Stan`: pasek*mid@srodekDIP(y=0)=1*"
    }
    It 'pomiar bez punktów na środku paska (stara sonda) nie przechodzi' {
        $Probe = New-UiProbe @{}
        $Probe.HitTest[0].Points = @($Probe.HitTest[0].Points | Where-Object { $_.XDip -ne 'srodek' })
        (Get-Check (Get-GuiUiCheck -Probe $Probe) 'CaptionHitTest').Passed | Should -BeFalse
    }
    It 'stan z pustą listą punktów nie przechodzi' {
        $Probe = New-UiProbe @{}
        $Probe.HitTest[0].Points = @()
        (Get-Check (Get-GuiUiCheck -Probe $Probe) 'CaptionHitTest').Passed | Should -BeFalse
    }
    It 'Ht = -1000 (SendMessageTimeout nie zdążył) nie przechodzi' {
        $Probe = New-UiProbe @{}
        $Probe.HitTest[1].Points[0].Ht = -1000
        $Check = Get-Check (Get-GuiUiCheck -Probe $Probe) 'CaptionHitTest'
        $Check.Passed | Should -BeFalse
        $Check.Detail | Should -BeLike '*mid@8DIP(y=0)=-1000*'
    }
    It 'brak pomiaru jednego ze stanów nie przechodzi' {
        $Probe = New-UiProbe @{}
        $Probe.HitTest = @($Probe.HitTest[1])
        $Check = Get-Check (Get-GuiUiCheck -Probe $Probe) 'CaptionHitTest'
        $Check.Passed | Should -BeFalse
        $Check.Detail | Should -BeLike '*Normal: brak pomiaru*'
    }
    It '<Nazwa> nie przechodzi, gdy pole wyniku to false' -ForEach @(
        @{ Nazwa = 'FilterRiskOpened' }
        @{ Nazwa = 'FilterRiskSelected' }
        @{ Nazwa = 'FilterHistoryOpened' }
        @{ Nazwa = 'FilterHistorySelected' }
    ) {
        $Checks = Get-GuiUiCheck -Probe (New-UiProbe @{ $Nazwa = $false }) -PrivacyExpected $script:PrivacyOk -AppsExpected $script:AppsOk -RepairTitles $script:RepairOk -InstallExpected $script:InstallOk
        $Check = Get-Check $Checks $Nazwa
        $Check.Passed | Should -BeFalse
        $Check.Detail | Should -BeLike 'NIE: *'
        @($Checks | Where-Object { -not $_.Passed }).Count | Should -Be 1
    }
    It '<Nazwa> nie przechodzi, gdy sonda nie ma pola wyniku (stara sonda)' -ForEach @(
        @{ Nazwa = 'FilterRiskOpened' }
        @{ Nazwa = 'FilterRiskSelected' }
        @{ Nazwa = 'FilterHistoryOpened' }
        @{ Nazwa = 'FilterHistorySelected' }
    ) {
        $Probe = New-UiProbe @{}
        $Without = [pscustomobject]@{}
        foreach ($P in $Probe.PSObject.Properties | Where-Object { $_.Name -ne $Nazwa }) { $Without | Add-Member -NotePropertyName $P.Name -NotePropertyValue $P.Value }
        $Check = Get-Check (Get-GuiUiCheck -Probe $Without) $Nazwa
        $Check.Passed | Should -BeFalse
        $Check.Detail | Should -BeLike "brak pola $Nazwa*"
    }
    It '<Nazwa> (wiersze listy) nie przechodzi, gdy pole wyniku to false' -ForEach @(
        @{ Nazwa = 'RowClickPreview' }
        @{ Nazwa = 'RowDoubleClickToggles' }
        @{ Nazwa = 'RunIconRuns' }
        @{ Nazwa = 'RunIconDoubleClickOnce' }
    ) {
        $Checks = Get-GuiUiCheck -Probe (New-UiProbe @{ $Nazwa = $false }) -PrivacyExpected $script:PrivacyOk -AppsExpected $script:AppsOk -RepairTitles $script:RepairOk -InstallExpected $script:InstallOk
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
        $Check = Get-Check (Get-GuiUiCheck -Probe $Without) $Nazwa
        $Check.Passed | Should -BeFalse
        $Check.Detail | Should -BeLike "brak pola $Nazwa*"
    }
    It 'błąd klikania wierszy trafia do opisu sprawdzeń wierszy, a nie do filtrów' {
        $Checks = Get-GuiUiCheck -Probe (New-UiProbe @{ RunIconRuns = $false; RowError = 'panel: Brak listy Pozycje.' })
        (Get-Check $Checks 'RunIconRuns').Detail | Should -BeLike '*wiersze: panel: Brak listy Pozycje.*'
        (Get-Check $Checks 'RowClickPreview').Passed | Should -BeTrue
        (Get-Check $Checks 'FilterRiskOpened').Detail | Should -Not -BeLike '*wiersze:*'
    }
    It 'błąd klikania filtrów trafia do opisu sprawdzeń filtrów' {
        $Check = Get-Check (Get-GuiUiCheck -Probe (New-UiProbe @{ FilterRiskOpened = $false; FilterError = 'Brak ComboBox' })) 'FilterRiskOpened'
        $Check.Passed | Should -BeFalse
        $Check.Detail | Should -BeLike '*filtry: Brak ComboBox*'
    }
    It 'błąd jednego pola filtra (z nazwą pola) nie unieważnia wyniku drugiego, a opis niesie oba błędy' {
        $Probe = New-UiProbe @{ FilterRiskOpened = $true; FilterRiskSelected = $false; FilterError = 'Ryzyko: lista nie zamknela sie po wyborze opcji; Filtr: Brak ComboBox' }
        $Checks = Get-GuiUiCheck -Probe $Probe
        (Get-Check $Checks 'FilterRiskOpened').Passed | Should -BeTrue
        (Get-Check $Checks 'FilterHistoryOpened').Passed | Should -BeTrue
        $Selected = Get-Check $Checks 'FilterRiskSelected'
        $Selected.Passed | Should -BeFalse
        $Selected.Detail | Should -BeLike '*Ryzyko: lista nie zamknela sie*Filtr: Brak ComboBox*'
    }
    It 'błędy zastosowania i odczytu historii trafiają do opisu wiersza historii' {
        $Check = Get-Check (Get-GuiUiCheck -Probe (New-UiProbe @{ EntryNames = @(); ApplyError = 'brak przycisku'; HistoryError = 'brak listy' })) 'HistoryEntryActive'
        $Check.Detail | Should -BeLike '*zastosowanie: brak przycisku*'
        $Check.Detail | Should -BeLike '*historia: brak listy*'
    }
    It 'błąd sondy jest w opisie HistoryMenu' {
        (Get-Check (Get-GuiUiCheck -Probe (New-UiProbe @{ Ok = $false; Error = 'brak okna'; HistoryMenuFound = $false })) 'HistoryMenu').Detail | Should -BeLike '*sonda: brak okna*'
    }
}

AfterAll { Clear-NoLiveVm }
