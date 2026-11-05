BeforeDiscovery {
    $script:HasHyperV = [bool](Get-Module -ListAvailable -Name Hyper-V)
}

BeforeAll {
    # Wyłącznik: żaden test na mockach nie może dotknąć prawdziwego Hyper-V (patrz Support\NoLiveVm.ps1).
    . (Join-Path $PSScriptRoot 'Support\NoLiveVm.ps1')

    Import-Module (Join-Path $PSScriptRoot '..\modules\VmCommon.psm1') -Force
    Import-Module (Join-Path $PSScriptRoot '..\modules\VmHost.psm1') -Force
    Import-Module (Join-Path $PSScriptRoot '..\modules\VmSmoke.psm1') -Force
    Import-Module (Join-Path $PSScriptRoot '..\modules\VmSmokeUi.psm1')
    $script:ScriptPath = (Resolve-Path (Join-Path $PSScriptRoot '..\Invoke-GuiSmoke.ps1')).Path
    # Nieinicjalizowany PSSession: przechodzi wiązanie parametrów w mockach, nie otwiera połączenia.
    $script:FakeSession = [System.Runtime.Serialization.FormatterServices]::GetUninitializedObject([System.Management.Automation.Runspaces.PSSession])
    $script:Credential = [pscredential]::new('Administrator', (ConvertTo-SecureString 'Test-Haslo-1!' -AsPlainText -Force))
    $script:GoodProbe = [pscustomobject]@{
        Ok = $true; Error = $null; ProcessAlive = $true; ProcessId = 4242; SessionId = 1; Responding = $true
        MainWindowHandle = 123456; MainWindowTitle = 'DSOptimizer'; Events = @(); ErrorLines = @(); LogFiles = @('dsoptimizer-20261019.log')
    }
    # Sonda UI z kompletem: menu i strona historii, wiersz „Aktywna”, HTCAPTION w pasku i HTCLIENT pod nim (po maksymalizacji i normalnie).
    $script:GoodUiProbe = [pscustomobject]@{
        Ok = $true; Error = $null; HistoryMenuFound = $true; HistoryPageOpened = $true; ApplyError = $null; HistoryError = $null
        EntryNames = @('Szybsze otwieranie podmenu, Eksplorator i wygoda, Aktywna, 20.10.2026 21:00'); HitTestError = $null
        FilterRiskOpened = $true; FilterRiskSelected = $true; FilterHistoryOpened = $true; FilterHistorySelected = $true; FilterError = $null
        RowClickPreview = $true; RowDoubleClickToggles = $true; RunIconRuns = $true; RunIconDoubleClickOnce = $true; RowError = $null
        PrivacyButtonFound = $true; PrivacyApplyCountOnEntry = 0; PrivacyApplyCount = 2; PrivacyApplyText = 'Zastosuj (2)'; PrivacyError = $null
        AppsListRead = $true; AppsButtonFound = $true; AppsRemoveCountOnEntry = 0; AppsRemoveCount = 1; AppsRemoveText = 'Usuń (1)'; AppsError = $null
        PrepMenuFound = $true; PrepLoaded = $true; PrepWindowsUpdateButton = $true; PrepWindowsUpdateNote = $false; PrepError = $null
        PrepTitles = @('Komputer', 'Windows Update', 'Opcjonalne aktualizacje sterowników', 'Sterowniki chipsetu', 'Grafika', 'Wsparcie producenta', 'Urządzenia bez sterownika')
        PrepGraphicsLines = @('Zasoby wideo funkcji Microsoft Hyper-V', 'sterownik 10.0.26100.1150, 21.06.2006, Microsoft')
        RepairMenuFound = $true; RepairListFound = $true; RepairError = $null
        RepairTexts = @('Wyczyść pamięć DNS', 'Opis DNS', 'Ochrona systemu', 'Opis ochrony')
        InstallMenuFound = $true; InstallLoaded = $true; InstallButtonText = 'Zainstaluj (0)'; InstallButtonEnabled = $false; InstallError = $null
        InstallSubtitle = 'Źródła: winget (Microsoft) i GitHub (RustDesk) · 35 aplikacji · zainstalowane: 0'
        InstallCategories = @('Przeglądarki', 'Narzędzia'); InstallTexts = @('Mozilla Firefox', 'Mozilla.Firefox', '7-Zip', '7zip.7zip')
        AutostartMenuFound = $true; AutostartPageScoped = $true; AutostartLoaded = $true; AutostartReadFailed = $false; AutostartError = $null
        AutostartGroupNames = @('Włączone, 2', 'Wyłączone, 1', 'Bez pliku, 1')
        AutostartDisableText = 'Wyłącz (0)'; AutostartDisableEnabled = $false; AutostartEnableText = 'Włącz (0)'; AutostartEnableEnabled = $false
        HitTest = @([pscustomobject]@{
                State = 'Maximized'; BarTop = 0; BarBottom = 44; ScanX = 8
                Points = @([pscustomobject]@{ Name = 'mid'; XDip = 8; X = 8; Y = 22; Ht = 2 }, [pscustomobject]@{ Name = 'low'; XDip = 8; X = 8; Y = 42; Ht = 2 },
                    [pscustomobject]@{ Name = 'below'; XDip = 8; X = 8; Y = 46; Ht = 1 }, [pscustomobject]@{ Name = 'mid'; XDip = 'srodek'; X = 500; Y = 22; Ht = 2 },
                    [pscustomobject]@{ Name = 'low'; XDip = 'srodek'; X = 500; Y = 42; Ht = 2 }, [pscustomobject]@{ Name = 'below'; XDip = 'srodek'; X = 500; Y = 46; Ht = 1 })
                Scan = @([pscustomobject]@{ Y = 43; Ht = 2 }, [pscustomobject]@{ Y = 44; Ht = 1 })
            }, [pscustomobject]@{
                State = 'Normal'; BarTop = 36; BarBottom = 80; ScanX = 59
                Points = @([pscustomobject]@{ Name = 'mid'; XDip = 8; X = 59; Y = 58; Ht = 2 }, [pscustomobject]@{ Name = 'low'; XDip = 8; X = 59; Y = 78; Ht = 2 },
                    [pscustomobject]@{ Name = 'below'; XDip = 8; X = 59; Y = 82; Ht = 1 }, [pscustomobject]@{ Name = 'mid'; XDip = 'srodek'; X = 500; Y = 58; Ht = 2 },
                    [pscustomobject]@{ Name = 'low'; XDip = 'srodek'; X = 500; Y = 78; Ht = 2 }, [pscustomobject]@{ Name = 'below'; XDip = 'srodek'; X = 500; Y = 82; Ht = 1 })
                Scan = @([pscustomobject]@{ Y = 79; Ht = 2 }, [pscustomobject]@{ Y = 80; Ht = 1 })
            })
    }
    # Detekcja sugerowanych pozycji „Prywatność i AI” przez CLI: dwie niezastosowane = „Zastosuj (2)”.
    $script:GoodDetection = @(
        [pscustomobject]@{ Id = 'privacy.a'; ExitCode = 0; State = 'NotApplied'; UserSid = 'S-1-5-21-1'; IsCertain = $true; Error = $null }
        [pscustomobject]@{ Id = 'privacy.b'; ExitCode = 0; State = 'Applied'; UserSid = 'S-1-5-21-1'; IsCertain = $true; Error = $null }
        [pscustomobject]@{ Id = 'privacy.c'; ExitCode = 0; State = 'Partial'; UserSid = 'S-1-5-21-1'; IsCertain = $true; Error = $null }
    )
    # --list-apps (wynik Invoke-DsoCli): jedna sugerowana usuwalna aplikacja = „Usuń (1)”.
    $script:GoodListing = [pscustomobject]@{
        ExitCode = 0; TimedOut = $false; Json = 'x'; ReportError = $null
        Report = [pscustomobject]@{
            exitCode = 0; user = [pscustomobject]@{ sid = 'S-1-5-21-1'; isCertain = $true }
            apps = @(
                [pscustomobject]@{ id = 'appx:Microsoft.GamingApp_8wekyb3d8bbwe'; kind = 'Store'; name = 'Xbox'; group = 'Suggested'; removability = 'Removable' }
                [pscustomobject]@{ id = 'appx:Microsoft.WindowsCalculator_8wekyb3d8bbwe'; kind = 'Store'; name = 'Kalkulator'; group = 'Protected'; removability = 'Protected' }
            )
        }
    }
    # --list-autostart (wynik Invoke-DsoCli): 2 włączone (w tym stan nieznany), 1 wyłączona, 1 bez pliku (wyłączona, bez pliku wygrywa ze stanem).
    $script:GoodAutostartListing = [pscustomobject]@{
        ExitCode = 0; TimedOut = $false; Json = 'x'; ReportError = $null
        Report = [pscustomobject]@{
            exitCode = 0; user = [pscustomobject]@{ sid = 'S-1-5-21-1'; isCertain = $true }
            autostartRows = @(
                [pscustomobject]@{ id = 'run-user:a'; source = 'RunUser'; name = 'A'; state = 'Enabled'; missingFile = $false }
                [pscustomobject]@{ id = 'task:b'; source = 'StartupTask'; name = 'B'; state = 'Unknown'; missingFile = $false }
                [pscustomobject]@{ id = 'run-machine:c'; source = 'RunMachine'; name = 'C'; state = 'Disabled'; missingFile = $false }
                [pscustomobject]@{ id = 'run-user:d'; source = 'RunUser'; name = 'D'; state = 'Disabled'; missingFile = $true }
            )
        }
    }
    function script:New-Probe([hashtable]$Override) {
        $Copy = [ordered]@{}
        foreach ($P in $script:GoodProbe.PSObject.Properties) { $Copy[$P.Name] = $P.Value }
        foreach ($K in $Override.Keys) { $Copy[$K] = $Override[$K] }
        [pscustomobject]$Copy
    }
}

Describe 'Invoke-GuiSmoke.ps1 (parametry skryptu)' {
    BeforeAll { $script:Command = Get-Command $script:ScriptPath }
    It 'odrzuca obcą VM jeszcze przy wiązaniu parametrów (nic się nie uruchamia)' {
        { & $script:ScriptPath -VmName 'ObcaVM' -OutDirectory $TestDrive } | Should -Throw '*ObcaVM*'
    }
    It 'dopuszcza tylko DSO-EN-Home i DSO-EN-Pro' {
        $Set = $script:Command.Parameters['VmName'].Attributes | Where-Object { $_ -is [System.Management.Automation.ValidateSetAttribute] }
        @($Set.ValidValues) | Should -Be @('DSO-EN-Home', 'DSO-EN-Pro', 'DSO-PL-Pro', 'DSO-PL-Home')
    }
    It 'ma -KeepRunning jako przełącznik i obowiązkowy -OutDirectory' {
        $script:Command.Parameters['KeepRunning'].SwitchParameter | Should -BeTrue
        $script:Command.Parameters['OutDirectory'].Attributes.Where({ $_ -is [System.Management.Automation.ParameterAttribute] }).Mandatory | Should -BeTrue
    }
    It 'domyślna VM to DSO-EN-Pro' {
        $Ast = [System.Management.Automation.Language.Parser]::ParseFile($script:ScriptPath, [ref]$null, [ref]$null)
        $Param = $Ast.ParamBlock.Parameters | Where-Object { $_.Name.VariablePath.UserPath -eq 'VmName' }
        $Param.DefaultValue.Value | Should -Be 'DSO-EN-Pro'
    }
    It '-Language bez wartości domyślnej, tylko pl|en' {
        $Ast = [System.Management.Automation.Language.Parser]::ParseFile($script:ScriptPath, [ref]$null, [ref]$null)
        $Param = $Ast.ParamBlock.Parameters | Where-Object { $_.Name.VariablePath.UserPath -eq 'Language' }
        $Param | Should -Not -BeNullOrEmpty
        $Param.DefaultValue | Should -BeNullOrEmpty
        $Set = $script:Command.Parameters['Language'].Attributes | Where-Object { $_ -is [System.Management.Automation.ValidateSetAttribute] }
        @($Set.ValidValues) | Should -Be @('pl', 'en')
    }
    It 'brak -Language bierze język z definicji VM' {
        $Source = Get-Content -LiteralPath $script:ScriptPath -Raw
        $Source | Should -Match "PSBoundParameters\.ContainsKey\('Language'\)"
        $Source | Should -Match 'Get-DsoVmDefinition -VmName \$VmName\)\.Language'
    }
    It 'plik jest w UTF-8 z BOM' {
        $Bytes = [System.IO.File]::ReadAllBytes($script:ScriptPath)
        $Bytes[0..2] | Should -Be @(0xEF, 0xBB, 0xBF)
    }
}

Describe 'Get-GuiSmokeCheck (agregacja sprawdzeń)' {
    BeforeAll {
        $script:Shot = Join-Path $TestDrive 'shot.png'
        Set-Content -LiteralPath $script:Shot -Value 'x'
    }
    It 'wszystko zielone dla dobrej sondy i istniejącego zrzutu' {
        $Checks = Get-GuiSmokeCheck -Probe $script:GoodProbe -ScreenshotPath $script:Shot
        @($Checks.Name) | Should -Be @('ProcessAlive', 'Responding', 'NoCrashEvents', 'NoLogErrors', 'Screenshot')
        @($Checks | Where-Object { -not $_.Passed }).Count | Should -Be 0
    }
    It '<Nazwa> nie przechodzi, gdy <Powod>' -ForEach @(
        @{ Nazwa = 'ProcessAlive'; Powod = 'proces nie żyje'; Override = @{ ProcessAlive = $false; Responding = $null; MainWindowHandle = 0 } }
        @{ Nazwa = 'Responding'; Powod = 'Responding=False'; Override = @{ Responding = $false } }
        @{ Nazwa = 'Responding'; Powod = 'brak okna głównego (Responding bez okna to fałszywy sukces)'; Override = @{ MainWindowHandle = 0 } }
        @{ Nazwa = 'NoCrashEvents'; Powod = 'jest zdarzenie Application Error'; Override = @{ Events = @([pscustomobject]@{ Provider = 'Application Error'; Id = 1000; Time = 't'; Message = 'm' }) } }
        @{ Nazwa = 'NoCrashEvents'; Powod = 'odczyt zdarzeń się nie udał (EventsError)'; Override = @{ EventsError = 'Access is denied' } }
        @{ Nazwa = 'NoLogErrors'; Powod = 'log ma linię ERROR'; Override = @{ ErrorLines = @('[2026-10-19 10:00:00.000] [ERROR] [x] boom') } }
        @{ Nazwa = 'NoLogErrors'; Powod = 'sonda zakończyła się błędem'; Override = @{ Ok = $false; Error = 'odmowa dostępu' } }
    ) {
        $Checks = Get-GuiSmokeCheck -Probe (New-Probe $Override) -ScreenshotPath $script:Shot
        ($Checks | Where-Object Name -eq $Nazwa).Passed | Should -BeFalse
    }
    It 'NoLogErrors bez plików logu opisuje, że aplikacja nic nie zapisała' {
        $All = Get-GuiSmokeCheck -Probe (New-Probe @{ LogFiles = @() }) -ScreenshotPath $script:Shot; $C = $All | Where-Object Name -eq 'NoLogErrors'
        $C.Passed | Should -BeTrue
        $C.Detail | Should -Be 'brak plików logu (aplikacja nic nie zapisała)'
    }
    It 'NoLogErrors z plikami logu podaje ich liczbę' {
        $All = Get-GuiSmokeCheck -Probe $script:GoodProbe -ScreenshotPath $script:Shot; $C = $All | Where-Object Name -eq 'NoLogErrors'
        $C.Detail | Should -Be 'brak linii ERROR (plików logu: 1)'
    }
    It 'Screenshot nie przechodzi bez pliku zrzutu' {
        $Checks = Get-GuiSmokeCheck -Probe $script:GoodProbe -ScreenshotPath (Join-Path $TestDrive 'brak.png')
        ($Checks | Where-Object Name -eq 'Screenshot').Passed | Should -BeFalse
    }
    It 'brak sondy ($null) oblicza wszystkie sprawdzenia jako niezaliczone' {
        $Checks = Get-GuiSmokeCheck -Probe $null -ScreenshotPath $null
        @($Checks | Where-Object Passed).Count | Should -Be 0
        $Checks.Count | Should -Be 5
    }
}

Describe 'ConvertTo-DsoRgb565Png' {
    It 'zapisuje poprawny PNG o zadanym rozmiarze z właściwymi kolorami' {
        # 2x1: piksel czerwony (0xF800) i zielony (0x07E0), little-endian.
        $Path = Join-Path $TestDrive 'rgb.png'
        ConvertTo-DsoRgb565Png -Data ([byte[]](0x00, 0xF8, 0xE0, 0x07)) -Width 2 -Height 1 -Path $Path | Should -Be $Path
        $Bytes = [System.IO.File]::ReadAllBytes($Path)
        $Bytes[0..7] | Should -Be @(0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A)
        Add-Type -AssemblyName System.Drawing
        $Image = [System.Drawing.Bitmap]::FromFile($Path)
        try {
            $Image.Width | Should -Be 2
            $Image.Height | Should -Be 1
            $Image.GetPixel(0, 0).R | Should -BeGreaterThan 240
            $Image.GetPixel(0, 0).G | Should -BeLessThan 16
            $Image.GetPixel(1, 0).G | Should -BeGreaterThan 240
            $Image.GetPixel(1, 0).R | Should -BeLessThan 16
        }
        finally { $Image.Dispose() }
    }
    It 'dekoduje bufor z 4-bajtowym prefiksem długości (big-endian), piksele od offsetu 4' {
        # 2x1 = 4 B pikseli + 4 B prefiksu = 8; prefiks 00 00 00 08; piksel (0,0) czerwony, (1,0) zielony.
        $Path = Join-Path $TestDrive 'pref.png'
        ConvertTo-DsoRgb565Png -Data ([byte[]](0x00, 0x00, 0x00, 0x08, 0x00, 0xF8, 0xE0, 0x07)) -Width 2 -Height 1 -Path $Path | Out-Null
        Add-Type -AssemblyName System.Drawing
        $Image = [System.Drawing.Bitmap]::FromFile($Path)
        try {
            $Image.GetPixel(0, 0).R | Should -BeGreaterThan 240
            $Image.GetPixel(1, 0).G | Should -BeGreaterThan 240
        }
        finally { $Image.Dispose() }
    }
    It 'dekoduje prefiks wielobajtowy (1024x768 -> 00 18 00 04)' {
        $Data = New-Object byte[] 1572868
        $Data[1] = 0x18; $Data[3] = 0x04
        ConvertTo-DsoRgb565Png -Data $Data -Width 1024 -Height 768 -Path (Join-Path $TestDrive 'big.png') | Should -Exist
    }
    It 'rzuca przy złym prefiksie, podając długości i pierwsze bajty' {
        { ConvertTo-DsoRgb565Png -Data ([byte[]](0x00, 0x00, 0x00, 0x09, 0x00, 0xF8, 0xE0, 0x07)) -Width 2 -Height 1 -Path (Join-Path $TestDrive 'bad.png') } | Should -Throw '*00 00 00 09*'
    }
    It 'rzuca przy złej długości, podając długość i pierwsze bajty' {
        { ConvertTo-DsoRgb565Png -Data ([byte[]](1, 2, 3, 4, 5, 6, 7)) -Width 2 -Height 1 -Path (Join-Path $TestDrive 'len.png') } | Should -Throw '*7 B (pierwsze 4 B: 01 02 03 04)*'
    }
    It 'rzuca, gdy liczba bajtów nie zgadza się z rozmiarem' {
        { ConvertTo-DsoRgb565Png -Data ([byte[]](1, 2, 3)) -Width 2 -Height 1 -Path (Join-Path $TestDrive 'x.png') } | Should -Throw '*RGB565*'
    }
}

Describe 'Save-DsoVmThumbnail' {
    BeforeEach {
        Mock -ModuleName VmSmoke Assert-DsoVmLiveAllowed {}
        Mock -ModuleName VmSmoke Get-CimInstance { New-CimInstance -ClientOnly -Namespace 'root/virtualization/v2' -ClassName 'Msvm_Fake' -Property @{ ElementName = 'DSO-EN-Pro' } }
        Mock -ModuleName VmSmoke Get-CimAssociatedInstance { New-CimInstance -ClientOnly -Namespace 'root/virtualization/v2' -ClassName 'Msvm_FakeSetting' -Property @{ InstanceID = 'settings' } }
        Mock -ModuleName VmSmoke Invoke-CimMethod { [pscustomobject]@{ ReturnValue = 0; ImageData = [byte[]](0x00, 0xF8, 0xE0, 0x07) } }
    }
    It 'woła GetVirtualSystemThumbnailImage w root\virtualization\v2 i zapisuje PNG' {
        Mock -ModuleName VmSmoke Invoke-CimMethod { [pscustomobject]@{ ReturnValue = 0; ImageData = (New-Object byte[] (64 * 64 * 2)) } }
        $Path = Join-Path $TestDrive 'thumb.png'
        Save-DsoVmThumbnail -VmName 'DSO-EN-Pro' -Path $Path -Width 64 -Height 64 | Should -Be $Path
        $Path | Should -Exist
        Should -Invoke -ModuleName VmSmoke Invoke-CimMethod -Times 1 -Exactly -ParameterFilter { $MethodName -eq 'GetVirtualSystemThumbnailImage' -and $Arguments.WidthPixels -eq 64 -and $Arguments.HeightPixels -eq 64 }
        Should -Invoke -ModuleName VmSmoke Get-CimInstance -ParameterFilter { $Namespace -eq 'root\virtualization\v2' }
    }
    It 'rzuca przy niezerowym kodzie zwrotnym' {
        Mock -ModuleName VmSmoke Invoke-CimMethod { [pscustomobject]@{ ReturnValue = 32768; ImageData = $null } }
        { Save-DsoVmThumbnail -VmName 'DSO-EN-Pro' -Path (Join-Path $TestDrive 'e.png') } | Should -Throw '*32768*'
    }
    It 'odrzuca obcą VM' {
        { Save-DsoVmThumbnail -VmName 'ObcaVM' -Path (Join-Path $TestDrive 'e.png') } | Should -Throw
        Should -Invoke -ModuleName VmSmoke Get-CimInstance -Times 0 -Exactly
    }
}

Describe 'Sonda: odczyt zdarzeń' {
    It 'nie wycisza błędów Get-WinEvent i rozróżnia brak zdarzeń od błędu odczytu' {
        $Text = Get-GuiSmokeProbeScript
        $Text | Should -Not -Match 'Get-WinEvent[^\r\n]*SilentlyContinue'
        $Text | Should -Match 'Get-WinEvent[^\r\n]*-ErrorAction Stop'
        $Text | Should -Match 'NoMatchingEventsFound'
        $Text | Should -Match 'EventsError'
    }
}

Describe 'Get-DsoVmResolution' {
    BeforeEach {
        Mock -ModuleName VmSmoke Assert-DsoVmLiveAllowed {}
        Mock -ModuleName VmSmoke Get-CimInstance { New-CimInstance -ClientOnly -Namespace 'root/virtualization/v2' -ClassName 'Msvm_Fake' -Property @{ ElementName = 'DSO-EN-Pro' } }
    }
    It 'czyta natywną rozdzielczość z Msvm_VideoHead' {
        Mock -ModuleName VmSmoke Get-CimAssociatedInstance { [pscustomobject]@{ CurrentHorizontalResolution = 1920; CurrentVerticalResolution = 1080 } }
        $R = Get-DsoVmResolution -VmName 'DSO-EN-Pro'
        $R.Width | Should -Be 1920
        $R.Height | Should -Be 1080
        $R.Source | Should -Be 'guest'
        Should -Invoke -ModuleName VmSmoke Get-CimAssociatedInstance -Times 1 -Exactly -ParameterFilter { $ResultClassName -eq 'Msvm_VideoHead' }
    }
    It 'przy błędzie odczytu wraca do 1024x768 i zapisuje powód' {
        Mock -ModuleName VmSmoke Get-CimAssociatedInstance { throw 'brak Msvm_VideoHead' }
        $R = Get-DsoVmResolution -VmName 'DSO-EN-Pro'
        $R.Width | Should -Be 1024
        $R.Height | Should -Be 768
        $R.Source | Should -Be 'fallback'
        $R.Error | Should -BeLike '*brak Msvm_VideoHead*'
    }
    It 'zerowa rozdzielczość (gość wyłączony) daje fallback' {
        Mock -ModuleName VmSmoke Get-CimAssociatedInstance { [pscustomobject]@{ CurrentHorizontalResolution = 0; CurrentVerticalResolution = 0 } }
        (Get-DsoVmResolution -VmName 'DSO-EN-Pro').Source | Should -Be 'fallback'
    }
}

Describe 'Start-DsoGuiInUserSession i Get-DsoGuiProbe' {
    BeforeEach {
        Mock -ModuleName VmSmoke Assert-DsoVmLiveAllowed {}
    }
    It 'zwraca czas startu z gościa' {
        Mock -ModuleName VmSmoke Invoke-Command -RemoveParameterValidation 'Session' { '2026-10-19T10:00:00.0000000Z' }
        Start-DsoGuiInUserSession -Session $script:FakeSession -Layout (Get-DsoVmLayout) | Should -Be '2026-10-19T10:00:00.0000000Z'
    }
    It 'przekazuje do VM ścieżkę exe i katalog roboczy z układu VM' {
        Mock -ModuleName VmSmoke Invoke-Command -RemoveParameterValidation 'Session' { 'ts' }
        Start-DsoGuiInUserSession -Session $script:FakeSession -Layout (Get-DsoVmLayout) | Out-Null
        Should -Invoke -ModuleName VmSmoke Invoke-Command -Times 1 -Exactly -ParameterFilter { $ArgumentList[0] -eq 'C:\dso\app\DSOptimizer.exe' -and $ArgumentList[1] -eq 'C:\dso\app' -and $ArgumentList[2] -eq 'dso' }
    }
    It 'zadanie startowe i zadanie sondy używają principala Interactive + Highest (K2)' {
        foreach ($Name in 'Start-DsoGuiInUserSession', 'Invoke-DsoUserSessionScript') {
            (Get-Command $Name).ScriptBlock.ToString() | Should -Match 'New-ScheduledTaskPrincipal -UserId \$User -LogonType Interactive -RunLevel Highest'
        }
    }
    It 'sonda stanu idzie przez wspólne zadanie w sesji dso z czasem startu w argumentach' {
        Mock -ModuleName VmSmoke Invoke-Command -RemoveParameterValidation 'Session' { $script:GoodProbe | ConvertTo-Json -Depth 5 }
        Get-DsoGuiProbe -Session $script:FakeSession -Layout (Get-DsoVmLayout) -Since '2026-10-19T10:00:00.0000000Z' | Out-Null
        Should -Invoke -ModuleName VmSmoke Invoke-Command -Times 1 -Exactly -ParameterFilter {
            $ArgumentList[0] -eq 'C:\dso\out' -and $ArgumentList[2] -eq 'GuiSmoke-Probe' -and $ArgumentList[3] -eq '-Since 2026-10-19T10:00:00.0000000Z' -and $ArgumentList[4] -eq 'dso'
        }
    }
    It 'sonda stanu odrzuca czas startu z niedozwolonymi znakami (argumenty zadania)' {
        Mock -ModuleName VmSmoke Invoke-Command -RemoveParameterValidation 'Session' { 'x' }
        { Get-DsoGuiProbe -Session $script:FakeSession -Layout (Get-DsoVmLayout) -Since '2026 -x; calc' } | Should -Throw '*format*'
        Should -Invoke -ModuleName VmSmoke Invoke-Command -Times 0 -Exactly
    }
    It 'sonda UI zapisuje słownik tekstów do pliku w VM (UTF-8 JSON) i przekazuje -TextsFile, ze skryptem z VmSmokeUi i dłuższym limitem czasu' {
        Mock -ModuleName VmSmoke Invoke-Command -RemoveParameterValidation 'Session' { $script:GoodUiProbe | ConvertTo-Json -Depth 8 }
        $Texts = Get-GuiProbeTexts -Language en
        $Ui = Get-DsoGuiUiProbe -Session $script:FakeSession -Layout (Get-DsoVmLayout) -Texts $Texts
        $Ui.HistoryMenuFound | Should -BeTrue
        Should -Invoke -ModuleName VmSmoke Invoke-Command -Times 1 -Exactly -ParameterFilter {
            $Data = $ArgumentList[6]
            $Sent = if ($null -ne $Data -and $Data.ContainsKey('GuiSmoke-Ui-texts.json')) { $Data['GuiSmoke-Ui-texts.json'] | ConvertFrom-Json } else { $null }
            $ArgumentList[1] -eq (Get-GuiUiProbeScript) -and $ArgumentList[2] -eq 'GuiSmoke-Ui' -and
            $ArgumentList[3] -eq '-TextsFile C:\dso\out\GuiSmoke-Ui-texts.json' -and $ArgumentList[5] -eq 720 -and
            $null -ne $Sent -and $Sent.HistoryPage -eq 'Change history' -and @($Sent.PrepTitles).Count -eq 7 -and $Sent.TweakTitle -eq $Texts.TweakTitle
        }
    }
    It 'wspólne zadanie zapisuje pliki danych w katalogu wyników VM jako UTF-8 przed startem zadania' {
        $Body = (Get-Command Invoke-DsoUserSessionScript).ScriptBlock.ToString()
        $Body | Should -Match ([regex]::Escape('[System.IO.File]::WriteAllText((Join-Path $OutDir $DataName), [string]$Data[$DataName], (New-Object System.Text.UTF8Encoding($false)))'))
        $Body.IndexOf('WriteAllText((Join-Path $OutDir $DataName)') | Should -BeLessThan $Body.IndexOf('Start-ScheduledTask')
    }
    It 'sonda UI rzuca, gdy VM nie zwróci wyniku' {
        Mock -ModuleName VmSmoke Invoke-Command -RemoveParameterValidation 'Session' { $null }
        { Get-DsoGuiUiProbe -Session $script:FakeSession -Layout (Get-DsoVmLayout) -Texts (Get-GuiProbeTexts -Language pl) } | Should -Throw '*Sonda UI nie zwróciła*'
    }
    It 'wspólne zadanie odrzuca argumenty spoza ASCII, nazwy z niedozwolonymi znakami i złe nazwy plików danych' {
        Mock -ModuleName VmSmoke Invoke-Command -RemoveParameterValidation 'Session' { 'x' }
        { Invoke-DsoUserSessionScript -Session $script:FakeSession -Layout (Get-DsoVmLayout) -Name 'X' -ScriptText 'x' -ArgumentText "-T $([char]0x0119)" } | Should -Throw
        { Invoke-DsoUserSessionScript -Session $script:FakeSession -Layout (Get-DsoVmLayout) -Name 'a b' -ScriptText 'x' } | Should -Throw
        foreach ($Bad in '..\x.json', 'a b.json', 'x.ps1', 'C:\x.json') {
            { Invoke-DsoUserSessionScript -Session $script:FakeSession -Layout (Get-DsoVmLayout) -Name 'X' -ScriptText 'x' -DataFiles @{ $Bad = '{}' } } | Should -Throw -Because $Bad
        }
        Should -Invoke -ModuleName VmSmoke Invoke-Command -Times 0 -Exactly
    }
    It 'sonda parsuje JSON z VM (także z BOM)' {
        $Json = [char]0xFEFF + ($script:GoodProbe | ConvertTo-Json -Depth 5)
        Mock -ModuleName VmSmoke Invoke-Command -RemoveParameterValidation 'Session' { $Json } -ParameterFilter { $true }
        $Probe = Get-DsoGuiProbe -Session $script:FakeSession -Layout (Get-DsoVmLayout) -Since 'ts'
        $Probe.ProcessAlive | Should -BeTrue
        $Probe.ProcessId | Should -Be 4242
    }
    It 'sonda rzuca, gdy VM nie zwróci wyniku' {
        Mock -ModuleName VmSmoke Invoke-Command -RemoveParameterValidation 'Session' { $null }
        { Get-DsoGuiProbe -Session $script:FakeSession -Layout (Get-DsoVmLayout) -Since 'ts' } | Should -Throw '*nie zwróciła*'
    }
    It 'skrypt sondy to czyste ASCII (PowerShell 5.1 bez BOM w zadaniu)' {
        [regex]::IsMatch((Get-GuiSmokeProbeScript), '[^\x00-\x7F]') | Should -BeFalse
    }
}

Describe 'Get-DsoPrivacyDetection (CLI --detect w VM)' {
    BeforeEach { Mock -ModuleName VmSmoke Assert-DsoVmLiveAllowed {} }
    It 'woła CLI z wbudowanym katalogiem (bez --catalog) dla każdej pozycji i parsuje stan oraz użytkownika' {
        Mock -ModuleName VmSmoke Invoke-Command -RemoveParameterValidation 'Session' {
            @(
                @{ Id = 'privacy.a'; ExitCode = 0; TimedOut = $false; Json = ([char]0xFEFF + '{"tweakId":"privacy.a","state":"NotApplied","user":{"sid":"S-1-5-21-9","isCertain":true}}') }
                @{ Id = 'privacy.b'; ExitCode = 0; TimedOut = $false; Json = '{"tweakId":"privacy.b","state":"Applied","user":{"sid":"S-1-5-21-9","isCertain":true}}' }
            )
        }
        $D = @(Get-DsoPrivacyDetection -Session $script:FakeSession -Layout (Get-DsoVmLayout) -TweakId @('privacy.a', 'privacy.b'))
        @($D.Id) | Should -Be @('privacy.a', 'privacy.b')
        @($D.State) | Should -Be @('NotApplied', 'Applied')
        $D[0].UserSid | Should -Be 'S-1-5-21-9'
        $D[0].IsCertain | Should -BeTrue
        Should -Invoke -ModuleName VmSmoke Invoke-Command -Times 1 -Exactly -ParameterFilter {
            $ArgumentList[0] -eq 'C:\dso\app\DSOptimizer.exe' -and $ArgumentList[1] -eq 'C:\dso\out' -and (@($ArgumentList[2]) -join ',') -eq 'privacy.a,privacy.b'
        }
    }
    It 'brak raportu albo zły JSON daje State=$null z opisem błędu' {
        Mock -ModuleName VmSmoke Invoke-Command -RemoveParameterValidation 'Session' {
            @(
                @{ Id = 'privacy.a'; ExitCode = -1; TimedOut = $true; Json = $null }
                @{ Id = 'privacy.b'; ExitCode = 0; TimedOut = $false; Json = '{zly' }
            )
        }
        $D = @(Get-DsoPrivacyDetection -Session $script:FakeSession -Layout (Get-DsoVmLayout) -TweakId @('privacy.a', 'privacy.b'))
        $D[0].State | Should -BeNullOrEmpty
        $D[0].Error | Should -BeLike '*brak raportu*'
        $D[1].State | Should -BeNullOrEmpty
        $D[1].Error | Should -Not -BeNullOrEmpty
    }
    It 'odrzuca identyfikator z niedozwolonymi znakami, zanim cokolwiek uruchomi' {
        Mock -ModuleName VmSmoke Invoke-Command -RemoveParameterValidation 'Session' {}
        { Get-DsoPrivacyDetection -Session $script:FakeSession -Layout (Get-DsoVmLayout) -TweakId @('privacy.a b') } | Should -Throw
        Should -Invoke -ModuleName VmSmoke Invoke-Command -Times 0 -Exactly
    }
}

Describe 'Okno wyboru języka w smoke GUI' {
    It 'Get-GuiLanguageCheck: pokazane i wybrane = zielone z tekstem po polsku (pl)' {
        $C = Get-GuiLanguageCheck -Choice ([pscustomobject]@{ Shown = $true; Chosen = $true }) -Language pl -SavedLanguage pl
        $C.Name | Should -Be 'LanguageChoice'
        $C.Passed | Should -BeTrue
        $C.Detail | Should -Be 'Okno wyboru języka pokazane i wybrano polski'
    }
    It 'Get-GuiLanguageCheck: tekst dla en' {
        (Get-GuiLanguageCheck -Choice ([pscustomobject]@{ Shown = $true; Chosen = $true }) -Language en -SavedLanguage en).Detail | Should -Be 'Okno wyboru języka pokazane i wybrano angielski'
    }
    It 'Get-GuiLanguageCheck: okno główne bez okna wyboru (Shown=false) = czerwone' {
        $C = Get-GuiLanguageCheck -Choice ([pscustomobject]@{ Shown = $false; Chosen = $false }) -Language pl
        $C.Passed | Should -BeFalse
        $C.Detail | Should -BeLike '*nie pokazało*'
    }
    It 'Get-GuiLanguageCheck: pokazane, ale bez okna głównego po wyborze = czerwone' {
        (Get-GuiLanguageCheck -Choice ([pscustomobject]@{ Shown = $true; Chosen = $false }) -Language pl).Passed | Should -BeFalse
    }
    It 'Get-GuiLanguageCheck: brak odczytu = czerwone' {
        (Get-GuiLanguageCheck -Choice $null -Language pl).Passed | Should -BeFalse
    }
    It 'Get-GuiLanguageCheck: wybór kafla, zapisany język inny niż wybrany = czerwone' {
        $C = Get-GuiLanguageCheck -Choice ([pscustomobject]@{ Shown = $true; Chosen = $true }) -Language en -SavedLanguage pl
        $C.Passed | Should -BeFalse
        $C.Detail | Should -BeLike '*settings.json: pl*'
    }
    It 'Get-GuiLanguageCheck: wybór kafla bez zapisanego języka = czerwone' {
        $C = Get-GuiLanguageCheck -Choice ([pscustomobject]@{ Shown = $true; Chosen = $true }) -Language pl
        $C.Passed | Should -BeFalse
        $C.Detail | Should -BeLike '*settings.json: brak*'
    }
    It 'Get-GuiLanguageCheck: wykrycie, zapisany = oczekiwany = zielone z opisem wykrycia' -ForEach @(@{ L = 'pl'; W = 'polski' }, @{ L = 'en'; W = 'angielski' }) {
        $C = Get-GuiLanguageCheck -Choice ([pscustomobject]@{ Shown = $true; Chosen = $true; Closed = $true }) -Language $L -Detected -SavedLanguage $L
        $C.Passed | Should -BeTrue
        $C.Detail | Should -Be "Okno wyboru języka zamknięte bez wyboru; program wybrał $W (wykryty z Windows, settings.json: $L)"
    }
    It 'Get-GuiLanguageCheck: wykrycie, zapisany język inny niż oczekiwany = czerwone' {
        (Get-GuiLanguageCheck -Choice ([pscustomobject]@{ Shown = $true; Chosen = $true }) -Language en -Detected -SavedLanguage pl).Passed | Should -BeFalse
    }
    It 'Get-GuiLanguageCheck: wykrycie bez settings.json (null) = czerwone' {
        $C = Get-GuiLanguageCheck -Choice ([pscustomobject]@{ Shown = $true; Chosen = $true }) -Language en -Detected -SavedLanguage $null
        $C.Passed | Should -BeFalse
        $C.Detail | Should -BeLike '*settings.json: brak*'
    }
    It 'Get-GuiLanguageCheck: wykrycie, okno nie zniknęło (Chosen=false) = czerwone' {
        (Get-GuiLanguageCheck -Choice ([pscustomobject]@{ Shown = $true; Chosen = $false }) -Language en -Detected -SavedLanguage en).Passed | Should -BeFalse
    }
    It 'sonda: tryb auto zamyka okno przez WindowPattern i nie szuka kafla' {
        $S = Get-LanguageChoiceProbeScript
        $S | Should -Match 'WindowPattern'
        $S | Should -Match "'auto'"
        $S | Should -Match 'Closed'
        [regex]::IsMatch($S, '[^\x00-\x7F]') | Should -BeFalse
        $Auto = $S.Substring($S.IndexOf("if (`$Language -eq 'auto') {"))
        $Auto.Substring(0, $Auto.IndexOf('} else {')) | Should -Not -Match 'LanguageChoicePolish|FindFirst|InvokePattern'
    }
    It 'Confirm-DsoLanguageChoice przyjmuje auto' {
        Mock -ModuleName VmSmoke Assert-DsoVmLiveAllowed {}
        Mock -ModuleName VmSmoke Invoke-Command -RemoveParameterValidation 'Session' { '{"Ok":true,"Error":null,"Shown":true,"Chosen":true,"Closed":true}' }
        $R = Confirm-DsoLanguageChoice -Session $script:FakeSession -Layout (Get-DsoVmLayout) -Language auto
        $R.Closed | Should -BeTrue
        Should -Invoke -ModuleName VmSmoke Invoke-Command -Times 1 -Exactly -ParameterFilter { $ArgumentList[3] -eq '-Language auto' }
    }
    It 'skrypt sondy: tylko ASCII, AutomationId okna i kafelków, InvokePattern, limit 30 s' {
        $Script = Get-LanguageChoiceProbeScript
        $Script | Should -Match '^[\x09\x0A\x0D\x20-\x7E]+$'
        $Script | Should -Match 'LanguageChoiceWindow'
        $Script | Should -Match 'LanguageChoicePolish'
        $Script | Should -Match 'LanguageChoiceEnglish'
        $Script | Should -Match 'InvokePattern'
        $Script | Should -Match 'AddSeconds\(30\)'
    }
    It 'skrypt sondy: odczyt okna w try/catch (okno znika w trakcie) i poprawna skladnia' {
        $Script = Get-LanguageChoiceProbeScript
        $Script | Should -Match 'catch \{ \$Window = \$null'
        $Errors = $null
        [void][System.Management.Automation.Language.Parser]::ParseInput($Script, [ref]$null, [ref]$Errors)
        @($Errors).Count | Should -Be 0
    }
    It 'Confirm-DsoLanguageChoice idzie przez sesję dso z argumentem -Language i zwraca wynik sondy' {
        Mock -ModuleName VmSmoke Assert-DsoVmLiveAllowed {}
        Mock -ModuleName VmSmoke Invoke-Command -RemoveParameterValidation 'Session' { '{"Ok":true,"Error":null,"Shown":true,"Chosen":true}' }
        $R = Confirm-DsoLanguageChoice -Session $script:FakeSession -Layout (Get-DsoVmLayout) -Language en
        $R.Shown | Should -BeTrue
        Should -Invoke -ModuleName VmSmoke Invoke-Command -Times 1 -Exactly -ParameterFilter {
            $ArgumentList[2] -eq 'GuiSmoke-Language' -and $ArgumentList[3] -eq '-Language en' -and $ArgumentList[4] -eq 'dso'
        }
    }
    It 'Confirm-DsoLanguageChoice: błąd sondy rzuca wyjątek' {
        Mock -ModuleName VmSmoke Assert-DsoVmLiveAllowed {}
        Mock -ModuleName VmSmoke Invoke-Command -RemoveParameterValidation 'Session' { '{"Ok":false,"Error":"Brak kafelka","Shown":true,"Chosen":false}' }
        { Confirm-DsoLanguageChoice -Session $script:FakeSession -Layout (Get-DsoVmLayout) } | Should -Throw '*Brak kafelka*'
    }
}

Describe 'Get-DsoSavedLanguage (settings.json w VM)' {
    BeforeEach { Mock -ModuleName VmSmoke Assert-DsoVmLiveAllowed {} }
    It 'czyta pole language: <Json> -> <Expected>' -ForEach @(
        @{ Json = '{"language":"pl","theme":"dark"}'; Expected = 'pl' }
        @{ Json = '{"language":"en"}'; Expected = 'en' }
        @{ Json = '{"theme":"dark"}'; Expected = $null }
        @{ Json = '{"language":"de"}'; Expected = $null }
        @{ Json = '{niepoprawny'; Expected = $null }
        @{ Json = $null; Expected = $null }
    ) {
        $script:RawSettings = $Json
        Mock -ModuleName VmSmoke Invoke-Command -RemoveParameterValidation 'Session' { $script:RawSettings }
        Get-DsoSavedLanguage -Session $script:FakeSession | Should -Be $Expected
    }
    It 'czyta plik settings.json z ProgramData przez PowerShell Direct (UTF-8, BOM dopuszczalny)' {
        $Source = Get-Content -LiteralPath (Join-Path $PSScriptRoot '..\modules\VmSmoke.psm1') -Raw
        $Source | Should -Match 'C:\\ProgramData\\DSOptimizer\\settings\.json'
        $Source | Should -Match '0xEF'
    }
}

Describe 'Okno wyboru języka w Invoke-DsoGuiSmoke (orkiestracja na mockach)' -Skip:(-not $script:HasHyperV) {
    BeforeEach {
        $script:Build = Join-Path $TestDrive 'build'
        $null = New-Item -ItemType Directory -Path $script:Build -Force
        Set-Content -LiteralPath (Join-Path $script:Build 'DSOptimizer.exe') -Value 'x'
        $script:Out = Join-Path $TestDrive ('out-' + [guid]::NewGuid().ToString('N'))
        Set-NoLiveVmDefaultMock -ModuleName VmSmoke
        Mock -ModuleName VmSmoke Assert-DsoVmLiveAllowed {}
        Mock -ModuleName VmSmoke Get-DsoVmCredential { $script:Credential }
        Mock -ModuleName VmSmoke Restore-VmCleanState {}
        Mock -ModuleName VmSmoke Start-VM {}
        Mock -ModuleName VmSmoke Wait-VmReady { $script:FakeSession }
        Mock -ModuleName VmSmoke Copy-DsoBuildToVm {}
        Mock -ModuleName VmSmoke Remove-PSSession {}
        Mock -ModuleName VmSmoke Start-Sleep {}
        Mock -ModuleName VmSmoke Start-DsoGuiInUserSession { '2026-10-19T10:00:00.0000000Z' }
        Mock -ModuleName VmSmoke Confirm-DsoLanguageChoice { [pscustomobject]@{ Shown = $true; Chosen = $true } }
        $script:Saved = 'pl'
        Mock -ModuleName VmSmoke Get-DsoSavedLanguage { $script:Saved }
        Mock -ModuleName VmSmoke Get-DsoGuiProbe { $script:GoodProbe }
        Mock -ModuleName VmSmoke Get-DsoGuiUiProbe { $script:GoodUiProbe }
        Mock -ModuleName VmSmoke Get-GuiPrivacySuggestedId { @('privacy.a', 'privacy.b', 'privacy.c') }
        Mock -ModuleName VmSmoke Get-GuiRepairTitle { @('Wyczyść pamięć DNS', 'Ochrona systemu') }
        Mock -ModuleName VmSmoke Get-GuiInstallTitle { [pscustomobject]@{ Titles = @('Mozilla Firefox', '7-Zip'); Categories = @('Przeglądarki', 'Narzędzia') } }
        Mock -ModuleName VmSmoke Get-DsoPrivacyDetection { $script:GoodDetection }
        Mock -ModuleName VmSmoke Get-DsoAppsListing { $script:GoodListing }
        Mock -ModuleName VmSmoke Get-DsoAutostartListing { $script:GoodAutostartListing }
        Mock -ModuleName VmSmoke Save-DsoVmThumbnail { Set-Content -LiteralPath $Path -Value 'png'; $Path }
        Mock -ModuleName VmSmoke Get-DsoVmResolution { [pscustomobject]@{ Width = 1280; Height = 800; Source = 'guest'; Error = $null } }
    }
    It 'domyślnie wybiera polski, wybór idzie zaraz po starcie okna, przed sondą stanu' {
        $script:Order = New-Object System.Collections.Generic.List[string]
        Mock -ModuleName VmSmoke Start-DsoGuiInUserSession { $script:Order.Add('start'); '2026-10-19T10:00:00.0000000Z' }
        Mock -ModuleName VmSmoke Confirm-DsoLanguageChoice { $script:Order.Add("wybor:$Language"); [pscustomobject]@{ Shown = $true; Chosen = $true } }
        Mock -ModuleName VmSmoke Get-DsoGuiProbe { $script:Order.Add('stan'); $script:GoodProbe }
        $Result = Invoke-DsoGuiSmoke -VmName 'DSO-EN-Pro' -OutDirectory $script:Out -BuildDirectory $script:Build -CatalogPath 'C:\k.json'
        @($script:Order)[0..2] | Should -Be @('start', 'wybor:pl', 'stan')
        $Check = $Result.checks | Where-Object Name -eq 'LanguageChoice'
        $Check.Passed | Should -BeTrue
        $Check.Detail | Should -Be 'Okno wyboru języka pokazane i wybrano polski'
        $Result.passed | Should -BeTrue
    }
    It '-Language en przekazuje angielski do wyboru i do tekstu sprawdzenia' {
        $script:Saved = 'en'
        $Result = Invoke-DsoGuiSmoke -VmName 'DSO-PL-Pro' -OutDirectory $script:Out -BuildDirectory $script:Build -CatalogPath 'C:\k.json' -Language en
        Should -Invoke -ModuleName VmSmoke Confirm-DsoLanguageChoice -Times 1 -Exactly -ParameterFilter { $Language -eq 'en' }
        ($Result.checks | Where-Object Name -eq 'LanguageChoice').Detail | Should -Be 'Okno wyboru języka pokazane i wybrano angielski'
    }
    It '-Language en: sonda UI dostaje angielskie teksty, tytuły Napraw i kategorie Instalacji po angielsku, słownik w wyniku' {
        $Result = Invoke-DsoGuiSmoke -VmName 'DSO-EN-Pro' -OutDirectory $script:Out -BuildDirectory $script:Build -CatalogPath 'C:\k.json' -Language en
        Should -Invoke -ModuleName VmSmoke Get-DsoGuiUiProbe -Times 1 -Exactly -ParameterFilter { $Texts.HistoryPage -eq 'Change history' -and $Texts.ApplyOne -eq 'Apply (1)' }
        Should -Invoke -ModuleName VmSmoke Get-GuiRepairTitle -Times 1 -Exactly -ParameterFilter { $Language -eq 'en' }
        Should -Invoke -ModuleName VmSmoke Get-GuiInstallTitle -Times 1 -Exactly -ParameterFilter { $Language -eq 'en' }
        $Result.probeTexts.HistoryPage | Should -Be 'Change history'
        ($Result.checks | Where-Object Name -eq 'HistoryEntryActive').Passed | Should -BeFalse
    }
    It 'domyślnie (pl) sonda UI dostaje polskie teksty' {
        Invoke-DsoGuiSmoke -VmName 'DSO-EN-Pro' -OutDirectory $script:Out -BuildDirectory $script:Build -CatalogPath 'C:\k.json' | Out-Null
        Should -Invoke -ModuleName VmSmoke Get-DsoGuiUiProbe -Times 1 -Exactly -ParameterFilter { $Texts.HistoryPage -eq 'Historia zmian' -and $Texts.ApplyOne -eq 'Zastosuj (1)' }
        Should -Invoke -ModuleName VmSmoke Get-GuiRepairTitle -Times 1 -Exactly -ParameterFilter { $Language -eq 'pl' }
    }
    It 'brak tłumaczenia tekstu sondy przerywa przebieg, zanim dotknie VM' {
        Mock -ModuleName VmSmoke Get-GuiProbeTexts { throw 'brak tłumaczenia: X' }
        { Invoke-DsoGuiSmoke -VmName 'DSO-EN-Pro' -OutDirectory $script:Out -BuildDirectory $script:Build -CatalogPath 'C:\k.json' -Language en } | Should -Throw '*brak tłumaczenia*'
        Should -Invoke -ModuleName VmSmoke Restore-VmCleanState -Times 0 -Exactly
        Should -Invoke -ModuleName VmSmoke Start-VM -Times 0 -Exactly
    }
    It 'VM EN + en = wykrycie (auto), bez klikania kafla' {
        $script:Saved = 'en'
        $Result = Invoke-DsoGuiSmoke -VmName 'DSO-EN-Pro' -OutDirectory $script:Out -BuildDirectory $script:Build -CatalogPath 'C:\k.json' -Language en
        Should -Invoke -ModuleName VmSmoke Confirm-DsoLanguageChoice -Times 1 -Exactly -ParameterFilter { $Language -eq 'auto' }
        $Result.languageMode | Should -Be 'wykrycie'
        $Result.savedLanguage | Should -Be 'en'
        ($Result.checks | Where-Object Name -eq 'LanguageChoice').Detail | Should -Be 'Okno wyboru języka zamknięte bez wyboru; program wybrał angielski (wykryty z Windows, settings.json: en)'
        ($Result.checks | Where-Object Name -eq 'LanguageChoice').Passed | Should -BeTrue
    }
    It 'VM EN + pl = kafel pl (tryb wybór)' {
        $Result = Invoke-DsoGuiSmoke -VmName 'DSO-EN-Pro' -OutDirectory $script:Out -BuildDirectory $script:Build -CatalogPath 'C:\k.json' -Language pl
        Should -Invoke -ModuleName VmSmoke Confirm-DsoLanguageChoice -Times 1 -Exactly -ParameterFilter { $Language -eq 'pl' }
        $Result.languageMode | Should -Be 'wybór'
        $Result.savedLanguage | Should -Be 'pl'
    }
    It 'VM PL + pl = wykrycie (auto)' {
        $Result = Invoke-DsoGuiSmoke -VmName 'DSO-PL-Pro' -OutDirectory $script:Out -BuildDirectory $script:Build -CatalogPath 'C:\k.json' -Language pl
        Should -Invoke -ModuleName VmSmoke Confirm-DsoLanguageChoice -Times 1 -Exactly -ParameterFilter { $Language -eq 'auto' }
        $Result.languageMode | Should -Be 'wykrycie'
    }
    It 'VM PL + en = kafel en' {
        $script:Saved = 'en'
        Invoke-DsoGuiSmoke -VmName 'DSO-PL-Pro' -OutDirectory $script:Out -BuildDirectory $script:Build -CatalogPath 'C:\k.json' -Language en | Out-Null
        Should -Invoke -ModuleName VmSmoke Confirm-DsoLanguageChoice -Times 1 -Exactly -ParameterFilter { $Language -eq 'en' }
    }
    It 'wykrycie: zapisany język inny niż język VM = LanguageChoice czerwone' {
        $script:Saved = 'pl'
        $Result = Invoke-DsoGuiSmoke -VmName 'DSO-EN-Home' -OutDirectory $script:Out -BuildDirectory $script:Build -CatalogPath 'C:\k.json' -Language en
        ($Result.checks | Where-Object Name -eq 'LanguageChoice').Passed | Should -BeFalse
        $Result.passed | Should -BeFalse
    }
    It 'wykrycie: brak settings.json = czerwone' {
        $script:Saved = $null
        $Result = Invoke-DsoGuiSmoke -VmName 'DSO-EN-Home' -OutDirectory $script:Out -BuildDirectory $script:Build -CatalogPath 'C:\k.json' -Language en
        ($Result.checks | Where-Object Name -eq 'LanguageChoice').Passed | Should -BeFalse
        $Result.savedLanguage | Should -BeNullOrEmpty
    }
    It 'wybór kafla: zapisany język inny niż wybrany = czerwone' {
        $script:Saved = 'en'
        $Result = Invoke-DsoGuiSmoke -VmName 'DSO-EN-Pro' -OutDirectory $script:Out -BuildDirectory $script:Build -CatalogPath 'C:\k.json' -Language pl
        ($Result.checks | Where-Object Name -eq 'LanguageChoice').Passed | Should -BeFalse
    }
    It 'zapisany język czytany po StartupSeconds, po wyborze okna' {
        $script:Order = New-Object System.Collections.Generic.List[string]
        Mock -ModuleName VmSmoke Confirm-DsoLanguageChoice { $script:Order.Add('wybor'); [pscustomobject]@{ Shown = $true; Chosen = $true } }
        Mock -ModuleName VmSmoke Start-Sleep { $script:Order.Add('sleep') }
        Mock -ModuleName VmSmoke Get-DsoSavedLanguage { $script:Order.Add('zapis'); 'pl' }
        Invoke-DsoGuiSmoke -VmName 'DSO-EN-Pro' -OutDirectory $script:Out -BuildDirectory $script:Build -CatalogPath 'C:\k.json' | Out-Null
        $List = @($script:Order)
        $List.IndexOf('wybor') | Should -BeLessThan $List.IndexOf('sleep')
        $List.IndexOf('sleep') | Should -BeLessThan $List.IndexOf('zapis')
    }
    It 'błąd odczytu settings.json = błąd w wyniku i czerwone sprawdzenie' {
        Mock -ModuleName VmSmoke Get-DsoSavedLanguage { throw 'brak sesji' }
        $Result = Invoke-DsoGuiSmoke -VmName 'DSO-EN-Pro' -OutDirectory $script:Out -BuildDirectory $script:Build -CatalogPath 'C:\k.json'
        ($Result.errors -join ' ') | Should -BeLike '*odczyt settings.json: brak sesji*'
        ($Result.checks | Where-Object Name -eq 'LanguageChoice').Passed | Should -BeFalse
    }
    It 'nadal 23 sprawdzenia' {
        $Result = Invoke-DsoGuiSmoke -VmName 'DSO-EN-Pro' -OutDirectory $script:Out -BuildDirectory $script:Build -CatalogPath 'C:\k.json'
        @($Result.checks).Count | Should -Be 23
    }
    It 'brak okna wyboru (Shown=false) = sprawdzenie czerwone i passed=false' {
        Mock -ModuleName VmSmoke Confirm-DsoLanguageChoice { [pscustomobject]@{ Shown = $false; Chosen = $false } }
        $Result = Invoke-DsoGuiSmoke -VmName 'DSO-EN-Pro' -OutDirectory $script:Out -BuildDirectory $script:Build -CatalogPath 'C:\k.json'
        $Result.passed | Should -BeFalse
        ($Result.checks | Where-Object Name -eq 'LanguageChoice').Passed | Should -BeFalse
        ($Result.checks | Where-Object Name -eq 'PrepPage').Passed | Should -BeTrue
    }
    It 'wyjątek sondy wyboru nie gubi reszty przebiegu, ale daje błąd i czerwone sprawdzenie' {
        Mock -ModuleName VmSmoke Confirm-DsoLanguageChoice { throw 'zadanie nie wystartowało' }
        $Result = Invoke-DsoGuiSmoke -VmName 'DSO-EN-Pro' -OutDirectory $script:Out -BuildDirectory $script:Build -CatalogPath 'C:\k.json'
        $Result.passed | Should -BeFalse
        ($Result.errors -join ' ') | Should -BeLike '*okno wyboru języka: zadanie nie wystartowało*'
        ($Result.checks | Where-Object Name -eq 'LanguageChoice').Passed | Should -BeFalse
        Should -Invoke -ModuleName VmSmoke Get-DsoGuiUiProbe -Times 1 -Exactly
    }
}

Describe 'Get-DsoAppsListing (CLI --list-apps w VM)' {
    BeforeEach { Mock -ModuleName VmSmoke Assert-DsoVmLiveAllowed {} }
    It 'woła CLI --list-apps z --out w katalogu wyników VM, jako konto dso (te same warunki co okno: konto procesu = zalogowane)' {
        Mock -ModuleName VmSmoke Invoke-DsoCli { $script:GoodListing }
        $L = Get-DsoAppsListing -Session $script:FakeSession -Layout (Get-DsoVmLayout) -Language pl
        $L.Report.apps.Count | Should -Be 2
        Should -Invoke -ModuleName VmSmoke Invoke-DsoCli -Times 1 -Exactly -ParameterFilter {
            $ExePath -eq 'C:\dso\app\DSOptimizer.exe' -and $OutPath -eq 'C:\dso\out\smoke-list-apps.json' -and $RunAsUser -eq 'dso' -and
            (@($Arguments) -join ' ') -eq '--list-apps --out C:\dso\out\smoke-list-apps.json'
        }
    }
}

Describe 'Przekazywanie -Language do Invoke-DsoCli przez listingi' {
    BeforeEach { Mock -ModuleName VmSmoke Assert-DsoVmLiveAllowed {} }
    It 'Get-DsoAppsListing przekazuje -Language <Lang> do Invoke-DsoCli' -ForEach @(@{ Lang = 'pl' }, @{ Lang = 'en' }) {
        Mock -ModuleName VmSmoke Invoke-DsoCli { $script:GoodListing }
        $null = Get-DsoAppsListing -Session $script:FakeSession -Layout (Get-DsoVmLayout) -Language $Lang
        Should -Invoke -ModuleName VmSmoke Invoke-DsoCli -Times 1 -Exactly -ParameterFilter { $Language -eq $Lang }
    }
    It 'Get-DsoAutostartListing przekazuje -Language <Lang> do Invoke-DsoCli' -ForEach @(@{ Lang = 'pl' }, @{ Lang = 'en' }) {
        Mock -ModuleName VmSmoke Invoke-DsoCli { $script:GoodAutostartListing }
        $null = Get-DsoAutostartListing -Session $script:FakeSession -Layout (Get-DsoVmLayout) -Language $Lang
        Should -Invoke -ModuleName VmSmoke Invoke-DsoCli -Times 1 -Exactly -ParameterFilter { $Language -eq $Lang }
    }
    It '<Fn> wymaga -Language pl|en' -ForEach @(@{ Fn = 'Get-DsoAppsListing' }, @{ Fn = 'Get-DsoAutostartListing' }) {
        $Param = (Get-Command $Fn).Parameters['Language']
        $Param.Attributes.Mandatory | Should -Contain $true
        ($Param.Attributes | Where-Object { $_ -is [System.Management.Automation.ValidateSetAttribute] }).ValidValues | Should -Be @('pl', 'en')
    }
    It 'Invoke-DsoGuiSmoke podaje swoje -Language obu listingom' {
        $Source = Get-Content -LiteralPath (Join-Path $PSScriptRoot '..\modules\VmSmoke.psm1') -Raw
        $Source | Should -Match 'Get-DsoAppsListing -Session \$Session -Layout \$Layout -Language \$Language'
        $Source | Should -Match 'Get-DsoAutostartListing -Session \$Session -Layout \$Layout -Language \$Language'
    }
}

Describe 'Get-DsoAutostartListing (CLI --list-autostart w VM)' {
    BeforeEach { Mock -ModuleName VmSmoke Assert-DsoVmLiveAllowed {} }
    It 'woła CLI --list-autostart z --out w katalogu wyników VM, jako konto dso (to samo konto co okno)' {
        Mock -ModuleName VmSmoke Invoke-DsoCli { $script:GoodAutostartListing }
        $L = Get-DsoAutostartListing -Session $script:FakeSession -Layout (Get-DsoVmLayout) -Language pl
        @($L.Report.autostartRows).Count | Should -Be 4
        Should -Invoke -ModuleName VmSmoke Invoke-DsoCli -Times 1 -Exactly -ParameterFilter {
            $ExePath -eq 'C:\dso\app\DSOptimizer.exe' -and $OutPath -eq 'C:\dso\out\smoke-list-autostart.json' -and $RunAsUser -eq 'dso' -and
            (@($Arguments) -join ' ') -eq '--list-autostart --out C:\dso\out\smoke-list-autostart.json'
        }
    }
    It '-FileName zmienia plik raportu (drugi odczyt po sondzie nie nadpisuje pierwszego)' {
        Mock -ModuleName VmSmoke Invoke-DsoCli { $script:GoodAutostartListing }
        $null = Get-DsoAutostartListing -Session $script:FakeSession -Layout (Get-DsoVmLayout) -Language en -FileName 'smoke-list-autostart-po.json'
        Should -Invoke -ModuleName VmSmoke Invoke-DsoCli -Times 1 -Exactly -ParameterFilter {
            $OutPath -eq 'C:\dso\out\smoke-list-autostart-po.json' -and (@($Arguments) -join ' ') -eq '--list-autostart --out C:\dso\out\smoke-list-autostart-po.json'
        }
        { Get-DsoAutostartListing -Session $script:FakeSession -Layout (Get-DsoVmLayout) -Language en -FileName '..\x.json' } | Should -Throw
    }
}

Describe 'Get-GuiAutostartExpectedCount (liczby grup z --list-autostart)' {
    BeforeAll {
        function script:New-AutostartListing([object[]]$Rows) {
            [pscustomobject]@{
                ExitCode = 0; TimedOut = $false; Json = 'x'; ReportError = $null
                Report = [pscustomobject]@{ exitCode = 0; user = [pscustomobject]@{ sid = 'S-1-5-21-1'; isCertain = $true }; autostartRows = @($Rows) }
            }
        }
        function script:New-Row([string]$State, [bool]$MissingFile) { [pscustomobject]@{ id = [guid]::NewGuid().ToString('N'); name = 'X'; state = $State; missingFile = $MissingFile } }
    }
    It 'liczy grupy jak AutostartRow.Group: bez pliku wygrywa ze stanem, stan nieznany = Włączone' {
        $R = Get-GuiAutostartExpectedCount -Listing $script:GoodAutostartListing
        $R.Enabled | Should -Be 2
        $R.Disabled | Should -Be 1
        $R.MissingFile | Should -Be 1
        $R.Detail | Should -BeLike '*--list-autostart*S-1-5-21-1*wierszy 4*'
    }
    It 'pozycja włączona bez pliku liczy się tylko do „Bez pliku”' {
        $R = Get-GuiAutostartExpectedCount -Listing (New-AutostartListing @((New-Row 'Enabled' $true), (New-Row 'Enabled' $false), (New-Row 'Disabled' $false)))
        @($R.Enabled, $R.Disabled, $R.MissingFile) | Should -Be @(1, 1, 1)
    }
    It 'pusta lista daje same zera' {
        $R = Get-GuiAutostartExpectedCount -Listing (New-AutostartListing @())
        @($R.Enabled, $R.Disabled, $R.MissingFile) | Should -Be @(0, 0, 0)
    }
    It 'reguła grup w module = AutostartRow.Group w Core' {
        $Source = [System.IO.File]::ReadAllText((Join-Path $PSScriptRoot '..\..\..\src\DSOptimizer.Core\Autostart\AutostartRow.cs'), [System.Text.Encoding]::UTF8)
        $Source | Should -Match ([regex]::Escape('public AutostartGroup Group => MissingFile'))
        $Source | Should -Match ([regex]::Escape('? AutostartGroup.MissingFile'))
        $Source | Should -Match ([regex]::Escape(': State == AutostartState.Disabled ? AutostartGroup.Disabled : AutostartGroup.Enabled;'))
    }
    It 'brak liczb (<Powod>)' -ForEach @(
        @{ Powod = 'brak wyniku'; Listing = $null; Like = '*brak wyniku*' }
        @{ Powod = 'limit czasu'; Listing = [pscustomobject]@{ ExitCode = -1; TimedOut = $true; Report = $null; ReportError = $null }; Like = '*limit czasu*' }
        @{ Powod = 'kod różny od 0'; Listing = [pscustomobject]@{ ExitCode = 1; TimedOut = $false; Report = [pscustomobject]@{ exitCode = 1 }; ReportError = $null }; Like = '*kod 1*' }
        @{ Powod = 'brak raportu'; Listing = [pscustomobject]@{ ExitCode = 0; TimedOut = $false; Report = $null; ReportError = 'zły JSON' }; Like = '*brak raportu (zły JSON)*' }
        @{ Powod = 'raport bez listy'; Listing = [pscustomobject]@{ ExitCode = 0; TimedOut = $false; Report = [pscustomobject]@{ exitCode = 0 }; ReportError = $null }; Like = '*bez listy autostartu*' }
    ) {
        $R = Get-GuiAutostartExpectedCount -Listing $Listing
        $R.Enabled | Should -BeNullOrEmpty
        $R.Disabled | Should -BeNullOrEmpty
        $R.MissingFile | Should -BeNullOrEmpty
        $R.Detail | Should -BeLike $Like
    }
}

Describe 'Invoke-DsoGuiSmoke (orkiestracja na mockach)' -Skip:(-not $script:HasHyperV) {
    BeforeEach {
        $script:Build = Join-Path $TestDrive 'build'
        $null = New-Item -ItemType Directory -Path $script:Build -Force
        Set-Content -LiteralPath (Join-Path $script:Build 'DSOptimizer.exe') -Value 'x'
        $script:Out = Join-Path $TestDrive ('out-' + [guid]::NewGuid().ToString('N'))
        Set-NoLiveVmDefaultMock -ModuleName VmSmoke
        Mock -ModuleName VmSmoke Assert-DsoVmLiveAllowed {}
        Mock -ModuleName VmSmoke Get-DsoVmCredential { $script:Credential }
        Mock -ModuleName VmSmoke Restore-VmCleanState {}
        Mock -ModuleName VmSmoke Start-VM {}
        Mock -ModuleName VmSmoke Wait-VmReady { $script:FakeSession }
        Mock -ModuleName VmSmoke Copy-DsoBuildToVm {}
        Mock -ModuleName VmSmoke Remove-PSSession {}
        Mock -ModuleName VmSmoke Start-Sleep {}
        Mock -ModuleName VmSmoke Start-DsoGuiInUserSession { '2026-10-19T10:00:00.0000000Z' }
        Mock -ModuleName VmSmoke Confirm-DsoLanguageChoice { [pscustomobject]@{ Shown = $true; Chosen = $true } }
        $script:Saved = 'pl'
        Mock -ModuleName VmSmoke Get-DsoSavedLanguage { $script:Saved }
        Mock -ModuleName VmSmoke Get-DsoGuiProbe { $script:GoodProbe }
        Mock -ModuleName VmSmoke Get-DsoGuiUiProbe { $script:GoodUiProbe }
        Mock -ModuleName VmSmoke Get-GuiPrivacySuggestedId { @('privacy.a', 'privacy.b', 'privacy.c') }
        Mock -ModuleName VmSmoke Get-GuiRepairTitle { @('Wyczyść pamięć DNS', 'Ochrona systemu') }
        Mock -ModuleName VmSmoke Get-GuiInstallTitle { [pscustomobject]@{ Titles = @('Mozilla Firefox', '7-Zip'); Categories = @('Przeglądarki', 'Narzędzia') } }
        Mock -ModuleName VmSmoke Get-DsoPrivacyDetection { $script:GoodDetection }
        Mock -ModuleName VmSmoke Get-DsoAppsListing { $script:GoodListing }
        Mock -ModuleName VmSmoke Get-DsoAutostartListing { $script:GoodAutostartListing }
        Mock -ModuleName VmSmoke Save-DsoVmThumbnail { Set-Content -LiteralPath $Path -Value 'png'; $Path }
        Mock -ModuleName VmSmoke Get-DsoVmResolution { [pscustomobject]@{ Width = 1280; Height = 800; Source = 'guest'; Error = $null } }
    }
    It 'domyślnie przywraca checkpoint na początku i na końcu (revert i VM Off)' {
        $Result = Invoke-DsoGuiSmoke -VmName 'DSO-EN-Pro' -OutDirectory $script:Out -BuildDirectory $script:Build -CatalogPath 'C:\k.json'
        $Result.passed | Should -BeTrue
        Should -Invoke -ModuleName VmSmoke Restore-VmCleanState -Times 2 -Exactly -ParameterFilter { $VmName -eq 'DSO-EN-Pro' -and $CheckpointName -eq 'czysta-26H2' }
    }
    It '-KeepRunning pomija revert na końcu (tylko ten na początku)' {
        $Result = Invoke-DsoGuiSmoke -VmName 'DSO-EN-Pro' -OutDirectory $script:Out -BuildDirectory $script:Build -CatalogPath 'C:\k.json' -KeepRunning
        $Result.passed | Should -BeTrue
        $Result.keepRunning | Should -BeTrue
        Should -Invoke -ModuleName VmSmoke Restore-VmCleanState -Times 1 -Exactly
    }
    It 'zapisuje JSON z wynikiem i ścieżką zrzutu w OutDirectory' {
        $Result = Invoke-DsoGuiSmoke -VmName 'DSO-EN-Home' -OutDirectory $script:Out -BuildDirectory $script:Build -CatalogPath 'C:\k.json'
        $Result.resultPath | Should -Exist
        $Result.screenshotPath | Should -BeLike (Join-Path $script:Out '*.png')
        $Saved = Get-Content -LiteralPath $Result.resultPath -Raw -Encoding UTF8 | ConvertFrom-Json
        $Saved.vmName | Should -Be 'DSO-EN-Home'
        @($Saved.checks.Name) | Should -Be @('ProcessAlive', 'Responding', 'NoCrashEvents', 'NoLogErrors', 'Screenshot', 'LanguageChoice', 'HistoryMenu', 'HistoryEntryActive', 'CaptionHitTest', 'FilterRiskOpened', 'FilterRiskSelected', 'FilterHistoryOpened', 'FilterHistorySelected', 'RowClickPreview', 'RowDoubleClickToggles', 'RunIconRuns', 'RunIconDoubleClickOnce', 'PrivacySuggestedApply', 'AppsSuggestedRemove', 'PrepPage', 'RepairPage', 'InstallPage', 'AutostartPage')
        @($Saved.repairTitles) | Should -Be @('Wyczyść pamięć DNS', 'Ochrona systemu')
        @($Saved.installExpected.Titles) | Should -Be @('Mozilla Firefox', '7-Zip')
        @($Saved.privacyDetection.Id) | Should -Be @('privacy.a', 'privacy.b', 'privacy.c')
        $Saved.privacyExpected.Count | Should -Be 2
        $Saved.appsExpected.Count | Should -Be 1
        $Saved.autostartExpected.Enabled | Should -Be 2
        $Saved.autostartExpected.MissingFile | Should -Be 1
        @($Saved.autostartListing.Report.autostartRows.id) | Should -Contain 'run-user:d'
        @($Saved.appsListing.Report.apps.id) | Should -Contain 'appx:Microsoft.GamingApp_8wekyb3d8bbwe'
        $Saved.uiScreenshotPath | Should -Be (Join-Path $script:Out 'gui-smoke-DSO-EN-Home-historia.png')
        $Saved.uiProbe.EntryNames | Should -Be @('Szybsze otwieranie podmenu, Eksplorator i wygoda, Aktywna, 20.10.2026 21:00')
    }
    It 'sonda UI idzie po pierwszym zrzucie i przed sondą stanu (zdarzenia i log obejmują operację z sondy UI)' {
        $script:Order = New-Object System.Collections.Generic.List[string]
        Mock -ModuleName VmSmoke Save-DsoVmThumbnail { $script:Order.Add("zrzut:$(Split-Path -Leaf $Path)"); Set-Content -LiteralPath $Path -Value 'png'; $Path }
        Mock -ModuleName VmSmoke Get-DsoGuiUiProbe { $script:Order.Add('ui'); $script:GoodUiProbe }
        Mock -ModuleName VmSmoke Get-DsoGuiProbe { $script:Order.Add('stan'); $script:GoodProbe }
        Mock -ModuleName VmSmoke Get-DsoPrivacyDetection { $script:Order.Add('detekcja'); $script:GoodDetection }
        Mock -ModuleName VmSmoke Get-DsoAppsListing { $script:Order.Add('lista'); $script:GoodListing }
        Mock -ModuleName VmSmoke Get-DsoAutostartListing { $script:Order.Add($(if ($FileName) { "autostart:$FileName" } else { 'autostart' })); $script:GoodAutostartListing }
        Invoke-DsoGuiSmoke -VmName 'DSO-EN-Pro' -OutDirectory $script:Out -BuildDirectory $script:Build -CatalogPath 'C:\k.json' | Out-Null
        @($script:Order) | Should -Be @('zrzut:gui-smoke-DSO-EN-Pro.png', 'detekcja', 'lista', 'autostart', 'ui', 'autostart:smoke-list-autostart-po.json', 'zrzut:gui-smoke-DSO-EN-Pro-historia.png', 'stan')
    }
    It '„Autostart”: lista zmieniona w trakcie sondy (pierwsze logowanie) - okno zgodne z odczytem CLI po sondzie przechodzi' {
        # Przed sondą jeszcze jest jednorazowa pozycja pierwszego logowania (OneDriveSetup), po sondzie już jej nie ma.
        $Rows = @($script:GoodAutostartListing.Report.autostartRows) + @([pscustomobject]@{ id = 'run:user:OneDriveSetup'; source = 'RunUser'; name = 'Setup'; state = 'Enabled'; missingFile = $false })
        $script:BeforeListing = [pscustomobject]@{
            ExitCode = 0; TimedOut = $false; Json = 'x'; ReportError = $null
            Report = [pscustomobject]@{ exitCode = 0; user = [pscustomobject]@{ sid = 'S-1-5-21-1'; isCertain = $true }; autostartRows = $Rows }
        }
        Mock -ModuleName VmSmoke Get-DsoAutostartListing { if ($FileName) { $script:GoodAutostartListing } else { $script:BeforeListing } }
        $Result = Invoke-DsoGuiSmoke -VmName 'DSO-EN-Pro' -OutDirectory $script:Out -BuildDirectory $script:Build -CatalogPath 'C:\k.json'
        $Check = $Result.checks | Where-Object Name -eq 'AutostartPage'
        $Check.Passed | Should -BeTrue
        $Check.Detail | Should -BeLike '*(CLI 3 przed, 2 po)*zmieniła się*'
        $Result.passed | Should -BeTrue
        $Result.autostartExpected.Enabled | Should -Be 3
        $Result.autostartExpectedAfter.Enabled | Should -Be 2
        @($Result.autostartListingAfter.Report.autostartRows.id) | Should -Not -Contain 'run:user:OneDriveSetup'
    }
    It 'błąd odczytu --list-autostart po sondzie kończy się niepowodzeniem' {
        Mock -ModuleName VmSmoke Get-DsoAutostartListing { if ($FileName) { throw 'limit zadań' } else { $script:GoodAutostartListing } }
        $Result = Invoke-DsoGuiSmoke -VmName 'DSO-EN-Pro' -OutDirectory $script:Out -BuildDirectory $script:Build -CatalogPath 'C:\k.json'
        $Result.passed | Should -BeFalse
        ($Result.errors -join ' ') | Should -BeLike '*lista autostartu po sondzie: limit zadań*'
        ($Result.checks | Where-Object Name -eq 'AutostartPage').Passed | Should -BeFalse
    }
    It 'N z „Usuń (N)” inne niż z --list-apps daje passed=false' {
        Mock -ModuleName VmSmoke Get-DsoGuiUiProbe { $P = $script:GoodUiProbe | ConvertTo-Json -Depth 8 | ConvertFrom-Json; $P.AppsRemoveCount = 3; $P.AppsRemoveText = 'Usuń (3)'; $P }
        $Result = Invoke-DsoGuiSmoke -VmName 'DSO-EN-Pro' -OutDirectory $script:Out -BuildDirectory $script:Build -CatalogPath 'C:\k.json'
        $Result.passed | Should -BeFalse
        ($Result.checks | Where-Object Name -eq 'AppsSuggestedRemove').Passed | Should -BeFalse
        ($Result.checks | Where-Object Name -eq 'PrivacySuggestedApply').Passed | Should -BeTrue
    }
    It 'błąd --list-apps nie gubi sondy UI, ale kończy się niepowodzeniem' {
        Mock -ModuleName VmSmoke Get-DsoAppsListing { throw 'zadanie nie wystartowało' }
        $Result = Invoke-DsoGuiSmoke -VmName 'DSO-EN-Pro' -OutDirectory $script:Out -BuildDirectory $script:Build -CatalogPath 'C:\k.json'
        $Result.passed | Should -BeFalse
        ($Result.errors -join ' ') | Should -BeLike '*lista aplikacji: zadanie nie wystartowało*'
        ($Result.checks | Where-Object Name -eq 'AppsSuggestedRemove').Passed | Should -BeFalse
        ($Result.checks | Where-Object Name -eq 'PrivacySuggestedApply').Passed | Should -BeTrue
        Should -Invoke -ModuleName VmSmoke Get-DsoGuiUiProbe -Times 1 -Exactly
    }
    It 'liczba grupy „Autostart” inna niż z --list-autostart daje passed=false' {
        Mock -ModuleName VmSmoke Get-DsoGuiUiProbe { $P = $script:GoodUiProbe | ConvertTo-Json -Depth 8 | ConvertFrom-Json; $P.AutostartGroupNames = @('Włączone, 3', 'Wyłączone, 1', 'Bez pliku, 0'); $P }
        $Result = Invoke-DsoGuiSmoke -VmName 'DSO-EN-Pro' -OutDirectory $script:Out -BuildDirectory $script:Build -CatalogPath 'C:\k.json'
        $Result.passed | Should -BeFalse
        ($Result.checks | Where-Object Name -eq 'AutostartPage').Passed | Should -BeFalse
        ($Result.checks | Where-Object Name -eq 'InstallPage').Passed | Should -BeTrue
    }
    It 'błąd --list-autostart nie gubi sondy UI, ale kończy się niepowodzeniem' {
        Mock -ModuleName VmSmoke Get-DsoAutostartListing { throw 'zadanie nie wystartowało' }
        $Result = Invoke-DsoGuiSmoke -VmName 'DSO-EN-Pro' -OutDirectory $script:Out -BuildDirectory $script:Build -CatalogPath 'C:\k.json'
        $Result.passed | Should -BeFalse
        ($Result.errors -join ' ') | Should -BeLike '*lista autostartu: zadanie nie wystartowało*'
        ($Result.checks | Where-Object Name -eq 'AutostartPage').Passed | Should -BeFalse
        ($Result.checks | Where-Object Name -eq 'AppsSuggestedRemove').Passed | Should -BeTrue
        Should -Invoke -ModuleName VmSmoke Get-DsoGuiUiProbe -Times 1 -Exactly
    }
    It 'tytuły „Naprawy” z wbudowanego katalogu repo; brak tytułu na liście daje passed=false' {
        Invoke-DsoGuiSmoke -VmName 'DSO-EN-Pro' -OutDirectory $script:Out -BuildDirectory $script:Build -CatalogPath 'C:\k.json' | Out-Null
        Should -Invoke -ModuleName VmSmoke Get-GuiRepairTitle -Times 1 -Exactly -ParameterFilter { $CatalogDirectory -like '*\src\DSOptimizer.Catalog\catalog' }
        Mock -ModuleName VmSmoke Get-DsoGuiUiProbe { $P = $script:GoodUiProbe | ConvertTo-Json -Depth 8 | ConvertFrom-Json; $P.RepairTexts = @('Wyczyść pamięć DNS'); $P }
        $Result = Invoke-DsoGuiSmoke -VmName 'DSO-EN-Pro' -OutDirectory $script:Out -BuildDirectory $script:Build -CatalogPath 'C:\k.json'
        $Result.passed | Should -BeFalse
        ($Result.checks | Where-Object Name -eq 'RepairPage').Passed | Should -BeFalse
        ($Result.checks | Where-Object Name -eq 'PrepPage').Passed | Should -BeTrue
    }
    It 'błąd odczytu tytułów „Naprawy” nie gubi sondy UI, ale kończy się niepowodzeniem' {
        Mock -ModuleName VmSmoke Get-GuiRepairTitle { throw 'brak repair.json' }
        $Result = Invoke-DsoGuiSmoke -VmName 'DSO-EN-Pro' -OutDirectory $script:Out -BuildDirectory $script:Build -CatalogPath 'C:\k.json'
        $Result.passed | Should -BeFalse
        ($Result.errors -join ' ') | Should -BeLike '*tytuły Napraw: brak repair.json*'
        ($Result.checks | Where-Object Name -eq 'RepairPage').Passed | Should -BeFalse
        Should -Invoke -ModuleName VmSmoke Get-DsoGuiUiProbe -Times 1 -Exactly
    }
    It 'tytuły i kategorie „Instalacja” z apps.json repo; aktywne „Zainstaluj (N)” daje passed=false' {
        Invoke-DsoGuiSmoke -VmName 'DSO-EN-Pro' -OutDirectory $script:Out -BuildDirectory $script:Build -CatalogPath 'C:\k.json' | Out-Null
        Should -Invoke -ModuleName VmSmoke Get-GuiInstallTitle -Times 1 -Exactly -ParameterFilter { $CatalogDirectory -like '*\src\DSOptimizer.Catalog\catalog' }
        Mock -ModuleName VmSmoke Get-DsoGuiUiProbe { $P = $script:GoodUiProbe | ConvertTo-Json -Depth 8 | ConvertFrom-Json; $P.InstallButtonEnabled = $true; $P }
        $Result = Invoke-DsoGuiSmoke -VmName 'DSO-EN-Pro' -OutDirectory $script:Out -BuildDirectory $script:Build -CatalogPath 'C:\k.json'
        $Result.passed | Should -BeFalse
        ($Result.checks | Where-Object Name -eq 'InstallPage').Passed | Should -BeFalse
        ($Result.checks | Where-Object Name -eq 'RepairPage').Passed | Should -BeTrue
    }
    It 'błąd odczytu apps.json nie gubi sondy UI, ale kończy się niepowodzeniem' {
        Mock -ModuleName VmSmoke Get-GuiInstallTitle { throw 'brak apps.json' }
        $Result = Invoke-DsoGuiSmoke -VmName 'DSO-EN-Pro' -OutDirectory $script:Out -BuildDirectory $script:Build -CatalogPath 'C:\k.json'
        $Result.passed | Should -BeFalse
        ($Result.errors -join ' ') | Should -BeLike '*lista instalacji: brak apps.json*'
        ($Result.checks | Where-Object Name -eq 'InstallPage').Passed | Should -BeFalse
        Should -Invoke -ModuleName VmSmoke Get-DsoGuiUiProbe -Times 1 -Exactly
    }
    It 'detekcja dostaje sugerowane pozycje privacy z wbudowanego katalogu repo' {
        Invoke-DsoGuiSmoke -VmName 'DSO-EN-Pro' -OutDirectory $script:Out -BuildDirectory $script:Build -CatalogPath 'C:\k.json' | Out-Null
        Should -Invoke -ModuleName VmSmoke Get-GuiPrivacySuggestedId -Times 1 -Exactly -ParameterFilter { $CatalogDirectory -like '*\src\DSOptimizer.Catalog\catalog' }
        Should -Invoke -ModuleName VmSmoke Get-DsoPrivacyDetection -Times 1 -Exactly -ParameterFilter { (@($TweakId) -join ',') -eq 'privacy.a,privacy.b,privacy.c' }
    }
    It 'N z przycisku inne niż z detekcji daje passed=false' {
        Mock -ModuleName VmSmoke Get-DsoPrivacyDetection { @($script:GoodDetection[0], $script:GoodDetection[1], ([pscustomobject]@{ Id = 'privacy.c'; ExitCode = 0; State = 'Applied'; UserSid = 'S-1-5-21-1'; IsCertain = $true; Error = $null })) }
        $Result = Invoke-DsoGuiSmoke -VmName 'DSO-EN-Pro' -OutDirectory $script:Out -BuildDirectory $script:Build -CatalogPath 'C:\k.json'
        $Result.passed | Should -BeFalse
        ($Result.checks | Where-Object Name -eq 'PrivacySuggestedApply').Passed | Should -BeFalse
    }
    It 'błąd detekcji nie gubi sondy UI, ale kończy się niepowodzeniem' {
        Mock -ModuleName VmSmoke Get-DsoPrivacyDetection { throw 'CLI nie odpowiada' }
        $Result = Invoke-DsoGuiSmoke -VmName 'DSO-EN-Pro' -OutDirectory $script:Out -BuildDirectory $script:Build -CatalogPath 'C:\k.json'
        $Result.passed | Should -BeFalse
        ($Result.errors -join ' ') | Should -BeLike '*detekcja sugerowanych: CLI nie odpowiada*'
        ($Result.checks | Where-Object Name -eq 'HistoryMenu').Passed | Should -BeTrue
        ($Result.checks | Where-Object Name -eq 'PrivacySuggestedApply').Passed | Should -BeFalse
        Should -Invoke -ModuleName VmSmoke Get-DsoGuiUiProbe -Times 1 -Exactly
    }
    It 'błąd sondy UI nie gubi sondy stanu ani drugiego zrzutu, ale kończy się niepowodzeniem' {
        Mock -ModuleName VmSmoke Get-DsoGuiUiProbe { throw 'UIA niedostępne' }
        $Result = Invoke-DsoGuiSmoke -VmName 'DSO-EN-Pro' -OutDirectory $script:Out -BuildDirectory $script:Build -CatalogPath 'C:\k.json'
        $Result.passed | Should -BeFalse
        ($Result.checks | Where-Object Name -eq 'ProcessAlive').Passed | Should -BeTrue
        ($Result.checks | Where-Object Name -eq 'HistoryMenu').Passed | Should -BeFalse
        ($Result.errors -join ' ') | Should -BeLike '*sonda UI: UIA niedostępne*'
        Should -Invoke -ModuleName VmSmoke Save-DsoVmThumbnail -Times 2 -Exactly
        Should -Invoke -ModuleName VmSmoke Get-DsoGuiProbe -Times 1 -Exactly
    }
    It 'HTCAPTION pod paskiem po maksymalizacji (zły offset) daje passed=false' {
        $Bad = $script:GoodUiProbe | ConvertTo-Json -Depth 8 | ConvertFrom-Json
        $Bad.HitTest[0].Points[2].Ht = 2
        Mock -ModuleName VmSmoke Get-DsoGuiUiProbe { $Bad }
        $Result = Invoke-DsoGuiSmoke -VmName 'DSO-EN-Pro' -OutDirectory $script:Out -BuildDirectory $script:Build -CatalogPath 'C:\k.json'
        $Result.passed | Should -BeFalse
        ($Result.checks | Where-Object Name -eq 'CaptionHitTest').Passed | Should -BeFalse
    }
    It 'prosi o zrzut w natywnej rozdzielczości gościa i zapisuje ją w JSON' {
        $Result = Invoke-DsoGuiSmoke -VmName 'DSO-EN-Pro' -OutDirectory $script:Out -BuildDirectory $script:Build -CatalogPath 'C:\k.json'
        Should -Invoke -ModuleName VmSmoke Save-DsoVmThumbnail -Times 2 -Exactly -ParameterFilter { $Width -eq 1280 -and $Height -eq 800 }
        $Saved = Get-Content -LiteralPath $Result.resultPath -Raw -Encoding UTF8 | ConvertFrom-Json
        $Saved.resolution.Width | Should -Be 1280
        $Saved.resolution.Source | Should -Be 'guest'
    }
    It 'zapisuje użycie fallbacku rozdzielczości w JSON' {
        Mock -ModuleName VmSmoke Get-DsoVmResolution { [pscustomobject]@{ Width = 1024; Height = 768; Source = 'fallback'; Error = 'x' } }
        $Result = Invoke-DsoGuiSmoke -VmName 'DSO-EN-Pro' -OutDirectory $script:Out -BuildDirectory $script:Build -CatalogPath 'C:\k.json'
        $Result.resolution.Source | Should -Be 'fallback'
        Should -Invoke -ModuleName VmSmoke Save-DsoVmThumbnail -Times 2 -Exactly -ParameterFilter { $Width -eq 1024 -and $Height -eq 768 }
    }
    It 'niezaliczone sprawdzenie sondy daje passed=false (kod wyjścia skryptu 1), a revert i tak się odbywa' {
        Mock -ModuleName VmSmoke Get-DsoGuiProbe { New-Probe @{ ErrorLines = @('[t] [ERROR] [c] zle') } }
        $Result = Invoke-DsoGuiSmoke -VmName 'DSO-EN-Pro' -OutDirectory $script:Out -BuildDirectory $script:Build -CatalogPath 'C:\k.json'
        $Result.passed | Should -BeFalse
        ($Result.checks | Where-Object Name -eq 'NoLogErrors').Passed | Should -BeFalse
        Should -Invoke -ModuleName VmSmoke Restore-VmCleanState -Times 2 -Exactly
    }
    It 'błąd uwierzytelnienia przerywa od razu: bez kopiowania i startu GUI, ale z rewertem; passed=false' {
        Mock -ModuleName VmSmoke Wait-VmReady {
            $E = [System.InvalidOperationException]::new('błąd uwierzytelnienia PowerShell Direct, przerywam')
            $E.Data['VmAuthFailure'] = $true
            throw $E
        }
        $Result = Invoke-DsoGuiSmoke -VmName 'DSO-EN-Pro' -OutDirectory $script:Out -BuildDirectory $script:Build -CatalogPath 'C:\k.json'
        $Result.passed | Should -BeFalse
        ($Result.errors -join ' ') | Should -BeLike '*uwierzytelnienia*'
        Should -Invoke -ModuleName VmSmoke Wait-VmReady -Times 1 -Exactly
        Should -Invoke -ModuleName VmSmoke Copy-DsoBuildToVm -Times 0 -Exactly
        Should -Invoke -ModuleName VmSmoke Start-DsoGuiInUserSession -Times 0 -Exactly
        Should -Invoke -ModuleName VmSmoke Restore-VmCleanState -Times 2 -Exactly
    }
    It 'błąd zrzutu ekranu nie gubi sondy, ale kończy się niepowodzeniem' {
        Mock -ModuleName VmSmoke Save-DsoVmThumbnail { throw 'WMI niedostępne' }
        $Result = Invoke-DsoGuiSmoke -VmName 'DSO-EN-Pro' -OutDirectory $script:Out -BuildDirectory $script:Build -CatalogPath 'C:\k.json'
        $Result.passed | Should -BeFalse
        ($Result.checks | Where-Object Name -eq 'ProcessAlive').Passed | Should -BeTrue
        ($Result.checks | Where-Object Name -eq 'Screenshot').Passed | Should -BeFalse
        ($Result.errors -join ' ') | Should -BeLike '*WMI niedostępne*'
    }
    It 'nieudany końcowy revert jest błędem w wyniku' {
        Mock -ModuleName VmSmoke Restore-VmCleanState { $script:RestoreCalls++; if ($script:RestoreCalls -ge 2) { throw 'checkpoint zajęty' } }
        InModuleScope VmSmoke { $script:RestoreCalls = 0 }
        $Result = Invoke-DsoGuiSmoke -VmName 'DSO-EN-Pro' -OutDirectory $script:Out -BuildDirectory $script:Build -CatalogPath 'C:\k.json'
        $Result.passed | Should -BeFalse
        ($Result.errors -join ' ') | Should -BeLike '*checkpoint zajęty*'
    }
    It 'odrzuca obcą VM' {
        { Invoke-DsoGuiSmoke -VmName 'ObcaVM' -OutDirectory $script:Out -BuildDirectory $script:Build -CatalogPath 'C:\k.json' } | Should -Throw
        Should -Invoke -ModuleName VmSmoke Restore-VmCleanState -Times 0 -Exactly
    }
    It 'rzuca przed jakąkolwiek operacją, gdy brakuje DSOptimizer.exe w buildzie' {
        { Invoke-DsoGuiSmoke -VmName 'DSO-EN-Pro' -OutDirectory $script:Out -BuildDirectory (Join-Path $TestDrive 'pusty') -CatalogPath 'C:\k.json' } | Should -Throw '*DSOptimizer.exe*'
        Should -Invoke -ModuleName VmSmoke Restore-VmCleanState -Times 0 -Exactly
    }
}

Describe 'Wyłącznik DSO_VM_TESTS_MOCK_ONLY w smoke GUI (bez mocka Assert-DsoVmLiveAllowed)' -Skip:(-not $script:HasHyperV) {
    BeforeEach {
        Set-NoLiveVmDefaultMock -ModuleName VmSmoke
        Set-NoLiveVmDefaultMock -ModuleName VmHost
    }
    It 'Invoke-DsoGuiSmoke rzuca przed Get-DsoVmCredential, Restore-VMCheckpoint i Start-VM' {
        $Build = Join-Path $TestDrive 'b'
        $null = New-Item -ItemType Directory -Path $Build -Force
        Set-Content -LiteralPath (Join-Path $Build 'DSOptimizer.exe') -Value 'x'
        { Invoke-DsoGuiSmoke -VmName 'DSO-EN-Pro' -OutDirectory (Join-Path $TestDrive 'o') -BuildDirectory $Build -CatalogPath 'C:\k.json' } | Should -Throw '*DSO_VM_TESTS_MOCK_ONLY*'
        Should -Invoke -ModuleName VmSmoke Get-DsoVmCredential -Times 0 -Exactly
        Should -Invoke -ModuleName VmSmoke Start-VM -Times 0 -Exactly
        Should -Invoke -ModuleName VmHost Restore-VMCheckpoint -Times 0 -Exactly
    }
    It '<Funkcja> rzuca przed jakimkolwiek wywołaniem WMI / Invoke-Command' -ForEach @(
        @{ Funkcja = 'Save-DsoVmThumbnail'; Kod = { Save-DsoVmThumbnail -VmName 'DSO-EN-Pro' -Path (Join-Path $TestDrive 't.png') } }
        @{ Funkcja = 'Start-DsoGuiInUserSession'; Kod = { Start-DsoGuiInUserSession -Session $null -Layout (Get-DsoVmLayout) } }
        @{ Funkcja = 'Get-DsoGuiProbe'; Kod = { Get-DsoGuiProbe -Session $null -Layout (Get-DsoVmLayout) -Since 'ts' } }
        @{ Funkcja = 'Get-DsoGuiUiProbe'; Kod = { Get-DsoGuiUiProbe -Session $null -Layout (Get-DsoVmLayout) -Texts ([ordered]@{ HistoryPage = 'x' }) } }
        @{ Funkcja = 'Invoke-DsoUserSessionScript'; Kod = { Invoke-DsoUserSessionScript -Session $null -Layout (Get-DsoVmLayout) -Name 'X' -ScriptText 'x' } }
        @{ Funkcja = 'Get-DsoPrivacyDetection'; Kod = { Get-DsoPrivacyDetection -Session $null -Layout (Get-DsoVmLayout) -TweakId @('privacy.a') } }
        @{ Funkcja = 'Get-DsoAppsListing'; Kod = { Get-DsoAppsListing -Session $null -Layout (Get-DsoVmLayout) -Language pl } }
        @{ Funkcja = 'Get-DsoAutostartListing'; Kod = { Get-DsoAutostartListing -Session $null -Layout (Get-DsoVmLayout) -Language pl } }
    ) {
        Mock -ModuleName VmSmoke Get-CimInstance {}
        Mock -ModuleName VmSmoke Invoke-Command -RemoveParameterValidation 'Session' {}
        Mock -ModuleName VmSmoke Invoke-DsoCli {}
        { & $Kod } | Should -Throw '*DSO_VM_TESTS_MOCK_ONLY*'
        Should -Invoke -ModuleName VmSmoke Get-CimInstance -Times 0 -Exactly
        Should -Invoke -ModuleName VmSmoke Invoke-Command -Times 0 -Exactly
        Should -Invoke -ModuleName VmSmoke Invoke-DsoCli -Times 0 -Exactly
    }
}

AfterAll { Clear-NoLiveVm }
