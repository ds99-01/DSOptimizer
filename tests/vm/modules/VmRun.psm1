<#
Instrukcja uruchomienia:
    Import-Module .\tests\vm\modules\VmRun.psm1 -Force
    (używane przez runner VM; testy: Invoke-Pester -Path tests/vm/tests/VmRun.Tests.ps1)
Opis działania:
    Wczytuje i waliduje przypadki testowe (cases.psd1) względem katalogu VM, wywołuje CLI DSOptimizer.exe
    w VM przez PowerShell Direct, robi snapshoty rejestru i usług przed i po oraz niezależnie od aplikacji
    odczytuje stan po apply. Cykl: detect -> apply -> detect -> undo -> detect, a stan sprzed apply jest
    porównywany ze stanem po undo, co wykrywa fałszywe "wykonano". Flow Reboot dodaje prawdziwy restart VM (potwierdzony zmianą LastBootUpTime) po apply i po undo; takie
    przypadki dostają osobny przebieg od czystego checkpointu (Split-VmCaseRuns).
    Flow AppRemove (Etap 4a): pliki z tests\vm\fixtures do VM -> Setup -> --list-apps -> --remove-app <AppId> -> --list-apps
    -> Check (niezależny odczyt w VM) -> Cleanup; RunAs = konto, jako które CLI startuje przez zadanie Harmonogramu
    (Interactive, najwyższe uprawnienia), domyślnie wbudowany Administrator sesji PS Direct.
    Flow AppInstall (Etap 4c): pliki z tests\vm\fixtures do VM -> Setup -> jedno polecenie CLI (--install-apps <AppIds> albo
    --list-install, opcjonalnie --apps-catalog) -> Check -> Cleanup; VM bez sieci, więc instalacja kończy się uczciwym "Nie wykonano".
    Klucz przypadku Language ('pl' albo 'en', poza AppInstall) = język CLI wszystkich kroków przypadku (--lang <Language>);
    bez niego Invoke-DsoCli dokleja --lang pl.
    Flow Autostart: pliki z tests\vm\fixtures do VM -> Setup (sondy Install-DsoAutostartProbe.ps1) -> --list-autostart (odczyt
    istnienia kluczy Expect.ListDoesNotWrite przed i po) -> bez Set: Check -> Cleanup. Z Set: --set-autostart <id> <Set> dla
    każdej pozycji Items -> restart -> Wait-VmAutostartStart (sondy Controls w probe.log po rozruchu, 20 s zapasu, ocena startu
    pozycji) -> Wait-VmStartupInfo -> Confirm-VmAutostartStart (ponowna ocena, późny start = Late) -> przywrócenie (--undo-autostart <id>
    albo --set-autostart <id> enabled|disabled) -> CheckAfterUndo (przed restartem) -> restart -> Wait-VmAutostartStart -> Wait-VmStartupInfo
    -> Confirm-VmAutostartStart -> --list-autostart -> Check -> Cleanup. Sonda = linia w probe.log zgodna z procesem konta (rozbieżność = błąd). Przypadek z Set ma osobny przebieg od czystego checkpointu.
    Hasło VM nigdy nie trafia do wyjścia.
#>
Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

$script:CaseKeys = @('Name', 'TweakId', 'AppId', 'RunAs', 'Flow', 'RestorePoint', 'Setup', 'Snapshot', 'Expect', 'Hypothesis', 'Catalog', 'Skip', 'Check', 'Cleanup', 'CheckAfterApply', 'CheckAfterUndo', 'CliTimeoutSeconds', 'Command', 'AppIds', 'AppsCatalog', 'Language', 'Items', 'Controls', 'Initial', 'Set', 'Restore')
# Flow Autostart: klucze przypadku, Expect i wiersza Expect.Listed. Items = @{ Id; Probe } albo @{ Id; Process } (id bez spacji),
# Controls = sondy, które muszą wystartować w każdym rozruchu; bez Set przypadek tylko czyta listę (przebieg main, bez restartu).
$script:AutostartOnlyKeys = @('Items', 'Controls', 'Initial', 'Set', 'Restore')
$script:AutostartKeys = @('Name', 'Flow', 'RunAs', 'Setup', 'Items', 'Controls', 'Initial', 'Set', 'Restore', 'Check', 'CheckAfterUndo', 'Cleanup', 'Expect', 'Hypothesis', 'Skip')
$script:AutostartExpectKeys = @('Listed', 'ListDoesNotWrite', 'SetExit', 'SetOutcome', 'RestoreExit', 'RestoreOutcome', 'MeasuredAfter')
$script:AutostartSetExpectKeys = @('SetExit', 'SetOutcome', 'RestoreExit', 'RestoreOutcome')
$script:AutostartListedKeys = @('State', 'CanToggle', 'MissingFile', 'Verdict')
$script:AutostartStates = @('Enabled', 'Disabled', 'Unknown')
$script:AutostartVerdicts = @('Protected', 'CanDisable')
$script:AutostartProbePattern = '^Dso[A-Za-z0-9]+\z'
$script:AutostartProcessPattern = '^[A-Za-z0-9._-]+\z'
# Czekanie na start po restarcie: sondy kontrolne do 240 s, potem 20 s zapasu na pozycje; plik StartupInfo do 240 s (pomiar: ok. 95 s po rozruchu).
$script:AutostartStartTimeoutSeconds = 240
$script:AutostartSettleSeconds = 20
$script:AutostartPollSeconds = 5
$script:AutostartUserName = 'dso'
# CliTimeoutSeconds (Etap 4b partia 2): limit czekania na CLI kroku 'execute' dla długich akcji (DISM/SFC, cleanmgr); tylko Flow 'Execute'.
# Language: język CLI przypadku ('pl' albo 'en'), każde wywołanie CLI dostaje --lang <Language>; bez klucza Invoke-DsoCli dokleja --lang pl.
$script:CaseLanguages = @('pl', 'en')
$script:CliTimeoutRange = @(60, 7200)
$script:FlowsSupported = @('Full', 'ApplyOnly', 'DetectOnly', 'Execute', 'Reboot', 'AppRemove', 'AppInstall', 'Autostart')
# Flow AppInstall (Etap 4c): klucze przypadku i Expect; Installed tylko przy list-install, Outcomes i WingetCodes tylko przy
# install-apps. WingetCodes (M2 final review): id -> dozwolone wartości pola wingetCode raportu (0x + 8 cyfr szesnastkowych,
# wielkie litery) albo $null = pozycja bez kodu (winget nie startował).
$script:AppInstallKeys = @('Name', 'Flow', 'Command', 'AppIds', 'AppsCatalog', 'RunAs', 'Setup', 'Check', 'Cleanup', 'Expect', 'Hypothesis', 'Skip')
$script:AppInstallCommands = @('install-apps', 'list-install')
$script:AppInstallExpectKeys = @('Exit', 'Outcomes', 'WingetCodes', 'Installed', 'WingetState', 'MessageContains')
# Flow AppRemove: Expect ma własny zestaw kluczy (lista przed/po, wynik remove-app); Group/Removability dotyczą wiersza z listy przed.
$script:AppRemoveExpectKeys = @('ListedBefore', 'Group', 'Removability', 'RemoveExit', 'RemoveOutcome', 'RequiresReboot', 'ListedAfter',
    'RestorePointStatus', 'Aborted', 'MessageContains', 'WarningContains', 'WarningAbsent')
$script:AppRemoveExpectRequired = @('ListedBefore', 'RemoveExit', 'RemoveOutcome', 'ListedAfter')
# Restart w Flow Reboot: najwyżej 6 wywołań Reconnect i 180 s na jeden restart, 5 s przerwy między próbami.
$script:RebootMaxReconnects = 6
$script:RebootTimeoutSeconds = 180
$script:RebootRetryDelaySeconds = 5
$script:ExecuteExpectKeys = @('ExecuteExit', 'ExecuteOutcome')
$script:ApplyUndoExpectKeys = @('ApplyExit', 'ApplyOutcome', 'AfterApply', 'UndoExit', 'UndoOutcome', 'AfterUndo',
    'AfterApplySnapshot', 'NotWrittenTo', 'UserSid', 'UserIsCertain', 'Aborted', 'RestorePointStatus',
    'RestorePointCreated', 'MessageContains', 'AfterReboot', 'KeyAbsentAfterUndo', 'KeyPresentAfterUndo')
$script:ExpectKeys = @('Before', 'ApplyExit', 'ApplyOutcome', 'AfterApply', 'UndoExit', 'UndoOutcome', 'AfterUndo',
    'AfterApplySnapshot', 'NotWrittenTo', 'UserSid', 'UserIsCertain', 'Aborted', 'RestorePointStatus',
    'RestorePointCreated', 'MessageContains', 'ExecuteExit', 'ExecuteOutcome', 'RequiresReboot', 'AfterReboot', 'KeyAbsentAfterUndo', 'KeyPresentAfterUndo')
# Klucze Expect z listą ścieżek kluczy rejestru (tylko Full i Reboot; ścieżki HKLM\ albo HKU\).
$script:KeyListExpectKeys = @('KeyAbsentAfterUndo', 'KeyPresentAfterUndo')
# Ulotny znacznik restartu (Restart-VmAndReconnect): klucz volatile znika przy każdym prawdziwym restarcie.
$script:RestartMarkerSubKey = 'SOFTWARE\DSOptimizer.RestartMarker'
$script:ExpectRequired = @('Before', 'ApplyExit', 'ApplyOutcome', 'AfterApply')
$script:ExpectUndoKeys = @('UndoExit', 'UndoOutcome', 'AfterUndo')

function Assert-VmSnapshotShape {
    param([string]$Label, [string]$KeyName, $Value, [ValidateSet('Names', 'Entries')][string]$ServiceForm)
    if ($Value -isnot [hashtable]) { throw "Przypadek '$Label': klucz '$KeyName' musi być hashtable (Registry/Services)." }
    foreach ($Key in $Value.Keys) {
        if ($Key -notin @('Registry', 'Services')) { throw "Przypadek '$Label': niedozwolony klucz '$Key' w '$KeyName' (dozwolone: Registry, Services)." }
    }
    if ($Value.ContainsKey('Registry')) { Assert-VmRegistryEntries -Label $Label -KeyName "$KeyName.Registry" -Entries $Value.Registry }
    if ($Value.ContainsKey('Services')) {
        foreach ($Item in @($Value.Services)) {
            if ($ServiceForm -eq 'Names') {
                if ($Item -isnot [string] -or [string]::IsNullOrWhiteSpace($Item)) { throw "Przypadek '$Label': '$KeyName.Services' musi zawierać nazwy usług (string)." }
            }
            elseif ($Item -isnot [hashtable] -or -not $Item.ContainsKey('Name')) {
                throw "Przypadek '$Label': wpis '$KeyName.Services' musi być hashtable z kluczem 'Name'."
            }
        }
    }
}

function Assert-VmRegistryEntries {
    # Path musi zaczynać się od HKLM\ albo HKU\ (HKU\{UserSid}\... dla użytkownika 'dso'); HKCU\ tylko z -AllowCurrentUser
    # (Expect.NotWrittenTo celowo czyta hive Administratora sesji PS Direct). Nieznany prefiks wpadłby do systemu plików
    # i dał Exists = false, czyli zielony test na pusto.
    param([string]$Label, [string]$KeyName, $Entries, [switch]$AllowCurrentUser)
    foreach ($Entry in @($Entries)) {
        if ($Entry -isnot [hashtable]) { throw "Przypadek '$Label': wpis '$KeyName' musi być hashtable z kluczami Path i Name." }
        foreach ($Key in @('Path', 'Name')) {
            if (-not $Entry.ContainsKey($Key)) { throw "Przypadek '$Label': wpis '$KeyName' bez klucza '$Key'." }
        }
        $Pattern = if ($AllowCurrentUser) { '^(HKLM|HKU|HKCU)\\' } else { '^(HKLM|HKU)\\' }
        $Path = [string]$Entry.Path
        if ($Path -notmatch $Pattern) {
            $Allowed = if ($AllowCurrentUser) { "'HKLM\', 'HKU\' lub 'HKCU\'" } else { "'HKLM\' lub 'HKU\'" }
            $Hint = if (-not $AllowCurrentUser -and $Path -match '^HKCU\\') { " Dla HKCU użyj 'HKU\{UserSid}\...' (PowerShell Direct działa jako Administrator, więc HKCU wskazuje inny profil niż użytkownik dso)." } else { '' }
            throw "Przypadek '$Label': '$KeyName' dopuszcza tylko ścieżki zaczynające się od $Allowed; niedozwolona ścieżka: '$Path'.$Hint"
        }
    }
}

function Assert-VmKeyList {
    # Lista ścieżek kluczy (Expect.KeyAbsentAfterUndo / KeyPresentAfterUndo): niepusta, teksty, tylko HKLM\ i HKU\.
    param([string]$Label, [string]$KeyName, $Value)
    $KeyList = @($Value)
    if ($KeyList.Count -eq 0 -or @($KeyList | Where-Object { $_ -isnot [string] -or [string]::IsNullOrWhiteSpace($_) }).Count -gt 0) {
        throw "Przypadek '$Label': 'Expect.$KeyName' musi być niepustą listą ścieżek kluczy (tekst)."
    }
    foreach ($KeyPath in $KeyList) {
        if ($KeyPath -notmatch '^(HKLM|HKU)\\') {
            $Hint = if ($KeyPath -match '^HKCU\\') { " Dla HKCU użyj 'HKU\{UserSid}\...' (PowerShell Direct działa jako Administrator, więc HKCU wskazuje inny profil niż użytkownik dso)." } else { '' }
            throw "Przypadek '$Label': 'Expect.$KeyName' dopuszcza tylko ścieżki zaczynające się od 'HKLM\' lub 'HKU\'; niedozwolona ścieżka: '$KeyPath'.$Hint"
        }
    }
}

function Assert-VmAppIdList {
    # AppIds Flow AppInstall: niepusta lista niepustych id bez spacji i przecinków (CLI łączy je przecinkiem, Start-Process nie cytuje).
    param([string]$Label, $Value)
    $Ids = @($Value)
    if ($null -eq $Value -or $Ids.Count -eq 0) { throw "Przypadek '$Label': 'AppIds' musi być niepustą listą id (install.<nazwa> albo winget:<id>)." }
    foreach ($Id in $Ids) {
        if ($Id -isnot [string] -or [string]::IsNullOrWhiteSpace($Id) -or $Id -match '[\s,]') {
            throw "Przypadek '$Label': każdy element 'AppIds' musi być niepustym id bez spacji i przecinków (jest: '$Id')."
        }
    }
}

function Assert-VmAppInstallCase {
    # Flow AppInstall: jedno polecenie CLI instalacji, wymagany Check (niezależny odczyt w VM) i Expect.Exit.
    param([string]$Label, [hashtable]$Case)
    foreach ($Key in $Case.Keys) {
        if ($Key -notin $script:AppInstallKeys) { throw "Przypadek '$Label': klucz '$Key' nie dotyczy Flow 'AppInstall' (dozwolone: $($script:AppInstallKeys -join ', '))." }
    }
    foreach ($Key in @('Name', 'Command', 'Expect')) {
        if (-not $Case.ContainsKey($Key)) { throw "Przypadek '$Label': brak wymaganego klucza '$Key' (Flow 'AppInstall')." }
    }
    if ($Case.Command -isnot [string] -or $Case.Command -cnotin $script:AppInstallCommands) {
        throw "Przypadek '$Label': 'Command' = '$($Case.Command)' (dozwolone: $($script:AppInstallCommands -join ', '))."
    }
    if ($Case.Command -eq 'install-apps') {
        if (-not $Case.ContainsKey('AppIds')) { throw "Przypadek '$Label': Command 'install-apps' wymaga klucza 'AppIds'." }
        Assert-VmAppIdList -Label $Label -Value $Case.AppIds
    }
    elseif ($Case.ContainsKey('AppIds')) {
        throw "Przypadek '$Label': klucz 'AppIds' jest dozwolony tylko przy Command 'install-apps'."
    }
    if ($Case.ContainsKey('AppsCatalog') -and ($Case.AppsCatalog -isnot [string] -or $Case.AppsCatalog -notmatch '^[A-Za-z]:\\\S+\z')) {
        throw "Przypadek '$Label': 'AppsCatalog' musi być bezwzględną ścieżką w VM bez spacji (np. C:\dso\fixtures\vm-test-apps.json)."
    }
    if ($Case.ContainsKey('RunAs') -and ($Case.RunAs -isnot [string] -or $Case.RunAs -notmatch '^[A-Za-z0-9_-]+\z')) {
        throw "Przypadek '$Label': 'RunAs' musi być nazwą konta lokalnego (litery, cyfry, _ i -), np. 'dso'."
    }
    if ($Case.ContainsKey('Skip') -and ($Case.Skip -isnot [string] -or [string]::IsNullOrWhiteSpace($Case.Skip))) {
        throw "Przypadek '$Label': klucz 'Skip' musi być niepustym tekstem z powodem pominięcia."
    }
    if (-not $Case.ContainsKey('Check')) {
        throw "Przypadek '$Label': Flow 'AppInstall' wymaga klucza 'Check' (niezależny odczyt w VM: wpisy Uninstall, logi winget, MSI); raport CLI go nie zastępuje."
    }
    foreach ($Key in @('Check', 'Cleanup')) {
        if ($Case.ContainsKey($Key) -and ($Case.$Key -isnot [string] -or [string]::IsNullOrWhiteSpace($Case.$Key))) { throw "Przypadek '$Label': klucz '$Key' musi być niepustym skryptem PowerShell (tekst)." }
    }
    if ($Case.Expect -isnot [hashtable]) { throw "Przypadek '$Label': klucz 'Expect' musi być hashtable." }
    foreach ($Key in $Case.Expect.Keys) {
        if ($Key -notin $script:AppInstallExpectKeys) { throw "Przypadek '$Label': niedozwolony klucz Expect '$Key' (Flow 'AppInstall': $($script:AppInstallExpectKeys -join ', '))." }
    }
    if (-not $Case.Expect.ContainsKey('Exit')) { throw "Przypadek '$Label': brak wymaganego klucza Expect 'Exit'." }
    if ($Case.Expect.ContainsKey('Installed')) {
        if ($Case.Command -ne 'list-install') { throw "Przypadek '$Label': klucz Expect 'Installed' dotyczy tylko Command 'list-install'." }
        if ($Case.Expect.Installed -isnot [hashtable] -or $Case.Expect.Installed.Count -eq 0) { throw "Przypadek '$Label': 'Expect.Installed' musi być niepustą hashtable id -> wartość logiczna." }
        foreach ($Key in $Case.Expect.Installed.Keys) {
            if ($Case.Expect.Installed[$Key] -isnot [bool]) { throw "Przypadek '$Label': 'Expect.Installed' dla '$Key' musi być wartością logiczną (`$true albo `$false)." }
        }
    }
    if ($Case.Expect.ContainsKey('Outcomes')) {
        if ($Case.Command -ne 'install-apps') { throw "Przypadek '$Label': klucz Expect 'Outcomes' dotyczy tylko Command 'install-apps'." }
        if ($Case.Expect.Outcomes -isnot [hashtable] -or $Case.Expect.Outcomes.Count -eq 0) { throw "Przypadek '$Label': 'Expect.Outcomes' musi być niepustą hashtable id -> wynik." }
        foreach ($Key in $Case.Expect.Outcomes.Keys) {
            if (@($Case.AppIds) -notcontains $Key) { throw "Przypadek '$Label': 'Expect.Outcomes' ma id '$Key', którego nie ma w AppIds." }
        }
    }
    if ($Case.Expect.ContainsKey('WingetCodes')) {
        if ($Case.Command -ne 'install-apps') { throw "Przypadek '$Label': klucz Expect 'WingetCodes' dotyczy tylko Command 'install-apps'." }
        if ($Case.Expect.WingetCodes -isnot [hashtable] -or $Case.Expect.WingetCodes.Count -eq 0) {
            throw "Przypadek '$Label': 'Expect.WingetCodes' musi być niepustą hashtable id -> dozwolone kody (albo `$null = brak kodu)."
        }
        foreach ($Key in $Case.Expect.WingetCodes.Keys) {
            if (@($Case.AppIds) -notcontains $Key) { throw "Przypadek '$Label': 'Expect.WingetCodes' ma id '$Key', którego nie ma w AppIds." }
            $Codes = $Case.Expect.WingetCodes[$Key]
            if ($null -eq $Codes) { continue }
            $List = @($Codes)
            if ($List.Count -eq 0) { throw "Przypadek '$Label': 'Expect.WingetCodes' dla '$Key' to pusta lista (brak kodu zapisuje się jako `$null)." }
            foreach ($Code in $List) {
                if ($Code -isnot [string] -or $Code -cnotmatch '^0x[0-9A-F]{8}$') {
                    throw "Przypadek '$Label': 'Expect.WingetCodes' dla '$Key' ma wartość '$Code' (dozwolone: 0x + 8 cyfr szesnastkowych wielkimi literami, jak wingetCode w raporcie)."
                }
            }
        }
    }
}

function Assert-VmAutostartId {
    # Id pozycji autostartu w argumencie CLI: niepusty tekst bez spacji (Start-Process w VM nie cytuje argumentów).
    param([string]$Label, [string]$What, $Id)
    if ($Id -isnot [string] -or [string]::IsNullOrWhiteSpace($Id) -or $Id -match '\s') {
        throw "Przypadek '$Label': $What musi być niepustym id pozycji autostartu bez spacji (jest: '$Id')."
    }
}

function Assert-VmAutostartCase {
    # Flow Autostart: lista (--list-autostart) i opcjonalnie Set z restartem; wymagany Check (niezależny odczyt w VM).
    param([string]$Label, [hashtable]$Case)
    foreach ($Key in $Case.Keys) {
        if ($Key -notin $script:AutostartKeys) { throw "Przypadek '$Label': klucz '$Key' nie dotyczy Flow 'Autostart' (dozwolone: $($script:AutostartKeys -join ', '))." }
    }
    foreach ($Key in @('Name', 'Check', 'Expect')) {
        if (-not $Case.ContainsKey($Key)) { throw "Przypadek '$Label': brak wymaganego klucza '$Key' (Flow 'Autostart')." }
    }
    if ($Case.ContainsKey('RunAs') -and ($Case.RunAs -isnot [string] -or $Case.RunAs -notmatch '^[A-Za-z0-9_-]+\z')) {
        throw "Przypadek '$Label': 'RunAs' musi być nazwą konta lokalnego (litery, cyfry, _ i -), np. 'dso'."
    }
    if ($Case.ContainsKey('Skip') -and ($Case.Skip -isnot [string] -or [string]::IsNullOrWhiteSpace($Case.Skip))) {
        throw "Przypadek '$Label': klucz 'Skip' musi być niepustym tekstem z powodem pominięcia."
    }
    foreach ($Key in @('Check', 'CheckAfterUndo', 'Cleanup')) {
        if ($Case.ContainsKey($Key) -and ($Case.$Key -isnot [string] -or [string]::IsNullOrWhiteSpace($Case.$Key))) { throw "Przypadek '$Label': klucz '$Key' musi być niepustym skryptem PowerShell (tekst)." }
    }
    if ($Case.ContainsKey('Setup')) {
        foreach ($Command in @($Case.Setup)) {
            if ($Command -isnot [string] -or [string]::IsNullOrWhiteSpace($Command)) { throw "Przypadek '$Label': 'Setup' musi być listą niepustych skryptów PowerShell." }
        }
    }
    if ($Case.Expect -isnot [hashtable]) { throw "Przypadek '$Label': klucz 'Expect' musi być hashtable." }
    foreach ($Key in $Case.Expect.Keys) {
        if ($Key -notin $script:AutostartExpectKeys) { throw "Przypadek '$Label': niedozwolony klucz Expect '$Key' (Flow 'Autostart': $($script:AutostartExpectKeys -join ', '))." }
    }
    $HasSet = $Case.ContainsKey('Set')
    if ($HasSet) {
        if ($Case.Set -isnot [string] -or $Case.Set -cnotin @('disabled', 'enabled')) { throw "Przypadek '$Label': 'Set' = '$($Case.Set)' (dozwolone: disabled, enabled)." }
        if (-not $Case.ContainsKey('Restore')) { throw "Przypadek '$Label': 'Set' wymaga klucza 'Restore' (undo, enable albo disable) - stan po teście musi wrócić." }
        if ($Case.Restore -isnot [string] -or $Case.Restore -cnotin @('undo', 'enable', 'disable')) { throw "Przypadek '$Label': 'Restore' = '$($Case.Restore)' (dozwolone: undo, enable, disable)." }
        if ($Case.ContainsKey('Initial') -and ($Case.Initial -isnot [string] -or $Case.Initial -cnotin @('enabled', 'disabled'))) { throw "Przypadek '$Label': 'Initial' = '$($Case.Initial)' (dozwolone: enabled, disabled)." }
        $Items = @(if ($Case.ContainsKey('Items')) { $Case.Items })
        if ($Items.Count -eq 0) { throw "Przypadek '$Label': 'Set' wymaga niepustej listy 'Items' (@{ Id; Probe } albo @{ Id; Process })." }
        $Ids = [System.Collections.Generic.HashSet[string]]::new([System.StringComparer]::OrdinalIgnoreCase)
        foreach ($Item in $Items) {
            if ($Item -isnot [hashtable]) { throw "Przypadek '$Label': każdy element 'Items' musi być hashtable @{ Id; Probe } albo @{ Id; Process }." }
            foreach ($Key in $Item.Keys) {
                if ($Key -notin @('Id', 'Probe', 'Process')) { throw "Przypadek '$Label': niedozwolony klucz '$Key' w 'Items' (dozwolone: Id, Probe, Process)." }
            }
            if (-not $Item.ContainsKey('Id')) { throw "Przypadek '$Label': element 'Items' bez klucza 'Id'." }
            Assert-VmAutostartId -Label $Label -What "'Items.Id'" -Id $Item.Id
            if (-not $Ids.Add([string]$Item.Id)) { throw "Przypadek '$Label': id '$($Item.Id)' powtarza się w 'Items'." }
            if ($Item.ContainsKey('Probe') -eq $Item.ContainsKey('Process')) {
                throw "Przypadek '$Label': pozycja '$($Item.Id)' musi mieć dokładnie jedno z: Probe (sonda w probe.log) albo Process (proces konta)."
            }
            if ($Item.ContainsKey('Probe') -and ($Item.Probe -isnot [string] -or $Item.Probe -notmatch $script:AutostartProbePattern)) { throw "Przypadek '$Label': 'Probe' pozycji '$($Item.Id)' musi być nazwą sondy Dso<litery i cyfry>." }
            if ($Item.ContainsKey('Process') -and ($Item.Process -isnot [string] -or $Item.Process -notmatch $script:AutostartProcessPattern)) { throw "Przypadek '$Label': 'Process' pozycji '$($Item.Id)' musi być nazwą procesu bez spacji i bez .exe." }
        }
        $Controls = @(if ($Case.ContainsKey('Controls')) { $Case.Controls })
        if ($Controls.Count -eq 0) { throw "Przypadek '$Label': 'Set' wymaga niepustej listy 'Controls' (sondy, które muszą wystartować w każdym rozruchu)." }
        foreach ($Control in $Controls) {
            if ($Control -isnot [string] -or $Control -notmatch $script:AutostartProbePattern) { throw "Przypadek '$Label': element 'Controls' musi być nazwą sondy Dso<litery i cyfry> (jest: '$Control')." }
        }
        foreach ($Key in $script:AutostartSetExpectKeys) {
            if (-not $Case.Expect.ContainsKey($Key)) { throw "Przypadek '$Label': 'Set' wymaga klucza Expect '$Key'." }
        }
    }
    else {
        foreach ($Key in @('Restore', 'Items', 'Controls', 'Initial', 'CheckAfterUndo')) {
            if ($Case.ContainsKey($Key)) { throw "Przypadek '$Label': klucz '$Key' wymaga klucza 'Set' (bez Set przypadek tylko czyta listę)." }
        }
        foreach ($Key in $script:AutostartSetExpectKeys + @('MeasuredAfter')) {
            if ($Case.Expect.ContainsKey($Key)) { throw "Przypadek '$Label': klucz Expect '$Key' wymaga klucza 'Set'." }
        }
    }
    if ($Case.Expect.ContainsKey('Listed')) {
        $Listed = $Case.Expect.Listed
        if ($Listed -isnot [hashtable] -or $Listed.Count -eq 0) { throw "Przypadek '$Label': 'Expect.Listed' musi być niepustą hashtable id -> @{ State; CanToggle; MissingFile?; Verdict? }." }
        foreach ($Id in $Listed.Keys) {
            Assert-VmAutostartId -Label $Label -What "klucz 'Expect.Listed'" -Id $Id
            $Want = $Listed[$Id]
            if ($Want -isnot [hashtable]) { throw "Przypadek '$Label': 'Expect.Listed' dla '$Id' musi być hashtable." }
            foreach ($Key in $Want.Keys) {
                if ($Key -notin $script:AutostartListedKeys) { throw "Przypadek '$Label': niedozwolone pole '$Key' w 'Expect.Listed' dla '$Id' (dozwolone: $($script:AutostartListedKeys -join ', '))." }
            }
            foreach ($Key in @('State', 'CanToggle')) {
                if (-not $Want.ContainsKey($Key)) { throw "Przypadek '$Label': 'Expect.Listed' dla '$Id' bez wymaganego pola '$Key'." }
            }
            if ($Want.State -isnot [string] -or $Want.State -cnotin $script:AutostartStates) { throw "Przypadek '$Label': 'Expect.Listed.State' dla '$Id' = '$($Want.State)' (dozwolone: $($script:AutostartStates -join ', '))." }
            foreach ($Key in @('CanToggle', 'MissingFile')) {
                if ($Want.ContainsKey($Key) -and $Want[$Key] -isnot [bool]) { throw "Przypadek '$Label': 'Expect.Listed.$Key' dla '$Id' musi być wartością logiczną." }
            }
            if ($Want.ContainsKey('Verdict') -and ($Want.Verdict -isnot [string] -or $Want.Verdict -cnotin $script:AutostartVerdicts)) { throw "Przypadek '$Label': 'Expect.Listed.Verdict' dla '$Id' = '$($Want.Verdict)' (dozwolone: $($script:AutostartVerdicts -join ', '))." }
        }
    }
    if ($Case.Expect.ContainsKey('ListDoesNotWrite')) { Assert-VmKeyList -Label $Label -KeyName 'ListDoesNotWrite' -Value $Case.Expect.ListDoesNotWrite }
    if ($Case.Expect.ContainsKey('MeasuredAfter')) {
        $Measured = @($Case.Expect.MeasuredAfter)
        if ($Measured.Count -eq 0) { throw "Przypadek '$Label': 'Expect.MeasuredAfter' musi być niepustą listą id." }
        foreach ($Id in $Measured) { Assert-VmAutostartId -Label $Label -What "element 'Expect.MeasuredAfter'" -Id $Id }
    }
}

function Import-VmCases {
    [CmdletBinding()]
    [OutputType([hashtable[]])]
    param(
        [Parameter(Mandatory)][string]$CasesPath,
        [Parameter(Mandatory)][string]$CatalogPath
    )
    $Catalog = Get-Content -LiteralPath $CatalogPath -Raw -Encoding UTF8 | ConvertFrom-Json
    $CatalogIds = @($Catalog | ForEach-Object { $_.id })
    $Data = Import-PowerShellDataFile -LiteralPath $CasesPath
    if (-not $Data.ContainsKey('Cases')) { throw "Plik '$CasesPath' nie zawiera klucza 'Cases'." }

    $Names = [System.Collections.Generic.HashSet[string]]::new([System.StringComparer]::OrdinalIgnoreCase)
    $Result = [System.Collections.Generic.List[hashtable]]::new()
    foreach ($Case in @($Data.Cases)) {
        $Label = if ($Case.ContainsKey('Name')) { [string]$Case.Name } else { '(bez nazwy)' }
        foreach ($Key in $Case.Keys) {
            if ($Key -notin $script:CaseKeys) { throw "Przypadek '$Label': niedozwolony klucz '$Key'." }
        }
        if ($Case.ContainsKey('Flow') -and $Case.Flow -eq 'AppInstall') {
            Assert-VmAppInstallCase -Label $Label -Case $Case
            if (-not $Names.Add($Label)) { throw "Przypadek '$Label': zduplikowana wartość klucza 'Name'." }
            $Result.Add($Case)
            continue
        }
        if ($Case.ContainsKey('Flow') -and $Case.Flow -eq 'Autostart') {
            Assert-VmAutostartCase -Label $Label -Case $Case
            if (-not $Names.Add($Label)) { throw "Przypadek '$Label': zduplikowana wartość klucza 'Name'." }
            $Result.Add($Case)
            continue
        }
        foreach ($Key in $script:AutostartOnlyKeys) {
            if ($Case.ContainsKey($Key)) { throw "Przypadek '$Label': klucz '$Key' jest dozwolony tylko we Flow 'Autostart'." }
        }
        foreach ($Key in @('Command', 'AppIds', 'AppsCatalog')) {
            if ($Case.ContainsKey($Key)) { throw "Przypadek '$Label': klucz '$Key' jest dozwolony tylko we Flow 'AppInstall'." }
        }
        $IsAppRemove = $Case.ContainsKey('Flow') -and $Case.Flow -eq 'AppRemove'
        $IdKey = if ($IsAppRemove) { 'AppId' } else { 'TweakId' }
        foreach ($Key in @('Name', $IdKey, 'Flow', 'RestorePoint', 'Expect')) {
            if (-not $Case.ContainsKey($Key)) { throw "Przypadek '$Label': brak wymaganego klucza '$Key'." }
        }
        if (-not $Names.Add($Label)) { throw "Przypadek '$Label': zduplikowana wartość klucza 'Name'." }
        if ($Case.Flow -notin $script:FlowsSupported) { throw "Przypadek '$Label': klucz 'Flow' = '$($Case.Flow)' (dozwolone: $($script:FlowsSupported -join ', '))." }
        if ($IsAppRemove) {
            if ($Case.ContainsKey('TweakId')) { throw "Przypadek '$Label': klucz 'TweakId' jest niedozwolony we Flow 'AppRemove' (użyj 'AppId')." }
            foreach ($Key in @('Catalog', 'Snapshot')) {
                if ($Case.ContainsKey($Key)) { throw "Przypadek '$Label': klucz '$Key' nie dotyczy Flow 'AppRemove' (CLI aplikacji nie używa katalogu tweaków, stan po usunięciu sprawdza Check)." }
            }
            # Start-Process w VM nie cytuje -ArgumentList, więc id ze spacją rozpadłby się na dwa argumenty (Ruling R6).
            if ($Case.AppId -isnot [string] -or [string]::IsNullOrWhiteSpace($Case.AppId) -or $Case.AppId -match '\s') {
                throw "Przypadek '$Label': 'AppId' musi być niepustym tekstem bez spacji (Start-Process w VM nie cytuje argumentów)."
            }
        }
        else {
            if ($Case.ContainsKey('AppId')) { throw "Przypadek '$Label': klucz 'AppId' jest dozwolony tylko we Flow 'AppRemove'." }
            # RunAs także w Execute (Etap 4b: naprawa winget działa tylko jako konto zalogowane, więc CLI musi wystartować jako 'dso').
            if ($Case.ContainsKey('RunAs') -and $Case.Flow -ne 'Execute') { throw "Przypadek '$Label': klucz 'RunAs' jest dozwolony tylko we Flow 'AppRemove' i 'Execute'." }
        }
        if ($Case.ContainsKey('CliTimeoutSeconds')) {
            # Długa akcja (np. repair.system-files) potrzebuje więcej niż domyślne 600 s Invoke-DsoCli; dotyczy tylko kroku 'execute'.
            if ($Case.Flow -ne 'Execute') { throw "Przypadek '$Label': klucz 'CliTimeoutSeconds' jest dozwolony tylko we Flow 'Execute'." }
            $Limit = $Case.CliTimeoutSeconds
            if ($Limit -isnot [int] -or $Limit -lt $script:CliTimeoutRange[0] -or $Limit -gt $script:CliTimeoutRange[1]) {
                throw "Przypadek '$Label': 'CliTimeoutSeconds' musi być liczbą całkowitą $($script:CliTimeoutRange[0])–$($script:CliTimeoutRange[1]) (sekundy)."
            }
        }
        if ($Case.ContainsKey('RunAs') -and ($Case.RunAs -isnot [string] -or $Case.RunAs -notmatch '^[A-Za-z0-9_-]+\z')) {
            throw "Przypadek '$Label': 'RunAs' musi być nazwą konta lokalnego (litery, cyfry, _ i -), np. 'dso'."
        }
        if ($Case.ContainsKey('Language') -and ($Case.Language -isnot [string] -or $Case.Language -cnotin $script:CaseLanguages)) {
            throw "Przypadek '$Label': klucz 'Language' = '$($Case.Language)' (dozwolone: pl, en)."
        }
        $CatalogKind = 'Test'
        if ($Case.ContainsKey('Catalog')) {
            $CatalogKind = $Case.Catalog
            if ($CatalogKind -isnot [string] -or $CatalogKind -cnotin @('Test', 'BuiltIn')) {
                throw "Przypadek '$Label': klucz 'Catalog' = '$CatalogKind' (dozwolone: Test, BuiltIn)."
            }
        }
        if ($Case.ContainsKey('Skip') -and ($Case.Skip -isnot [string] -or [string]::IsNullOrWhiteSpace($Case.Skip))) {
            throw "Przypadek '$Label': klucz 'Skip' musi być niepustym tekstem z powodem pominięcia."
        }
        if (-not $IsAppRemove -and $CatalogKind -eq 'Test' -and $Case.TweakId -notin $CatalogIds) { throw "Przypadek '$Label': TweakId '$($Case.TweakId)' nie istnieje w katalogu." }
        if ($Case.RestorePoint -notin @('skip', 'ifavailable', 'required')) {
            throw "Przypadek '$Label': klucz 'RestorePoint' = '$($Case.RestorePoint)' (dozwolone: skip, ifavailable, required)."
        }
        if ($Case.Expect -isnot [hashtable]) { throw "Przypadek '$Label': klucz 'Expect' musi być hashtable." }
        $AllowedExpect = if ($IsAppRemove) { $script:AppRemoveExpectKeys } else { $script:ExpectKeys }
        foreach ($Key in $Case.Expect.Keys) {
            if ($Key -notin $AllowedExpect) { throw "Przypadek '$Label': niedozwolony klucz Expect '$Key'." }
        }
        # Expect.RequiresReboot (poza AppRemove, które ma własne znaczenie): raport kroku 'execute' ma requiresReboot równe tej wartości.
        # Tylko Flow 'Execute' - w Full/Reboot restart pokrywa osobna asercja apply.
        if (-not $IsAppRemove -and $Case.Expect.ContainsKey('RequiresReboot')) {
            if ($Case.Flow -ne 'Execute') { throw "Przypadek '$Label': klucz Expect 'RequiresReboot' jest dozwolony tylko we Flow 'Execute' (w Reboot sprawdza to asercja apply)." }
            if ($Case.Expect.RequiresReboot -isnot [bool]) { throw "Przypadek '$Label': 'Expect.RequiresReboot' musi być wartością logiczną (`$true albo `$false)." }
        }
        foreach ($Key in @('Check', 'Cleanup', 'CheckAfterApply', 'CheckAfterUndo')) {
            if (-not $Case.ContainsKey($Key)) { continue }
            if ($Key -in @('Check', 'Cleanup') -and $Case.Flow -notin @('Execute', 'AppRemove')) { throw "Przypadek '$Label': klucz '$Key' jest dozwolony tylko we Flow 'Execute' i 'AppRemove'." }
            if ($Key -in @('CheckAfterApply', 'CheckAfterUndo') -and $Case.Flow -notin @('Full', 'Reboot')) { throw "Przypadek '$Label': klucz '$Key' jest dozwolony tylko we Flow 'Full' lub 'Reboot' (wymaga apply i undo)." }
            if ($Case.$Key -isnot [string] -or [string]::IsNullOrWhiteSpace($Case.$Key)) { throw "Przypadek '$Label': klucz '$Key' musi być niepustym skryptem PowerShell (tekst)." }
        }
        if ($Case.Flow -eq 'AppRemove') {
            foreach ($Key in $script:AppRemoveExpectRequired) {
                if (-not $Case.Expect.ContainsKey($Key)) { throw "Przypadek '$Label': brak wymaganego klucza Expect '$Key'." }
            }
            # Wynik CLI to deklaracja programu; sukces ("zniknęło") potwierdza dopiero niezależny odczyt w VM.
            if (-not $Case.ContainsKey('Check')) {
                throw "Przypadek '$Label': Flow 'AppRemove' wymaga klucza 'Check' (niezależny odczyt w VM: pakiet AppX, obraz systemu, klucz Uninstall); raport CLI go nie zastępuje."
            }
            foreach ($Key in @('Group', 'Removability')) {
                if ($Case.Expect.ContainsKey($Key) -and $Case.Expect.ListedBefore -ne $true) {
                    throw "Przypadek '$Label': klucz Expect '$Key' wymaga 'ListedBefore = `$true' (dotyczy wiersza listy przed usunięciem)."
                }
            }
        }
        elseif ($Case.Flow -eq 'Execute') {
            foreach ($Key in $script:ExecuteExpectKeys) {
                if (-not $Case.Expect.ContainsKey($Key)) { throw "Przypadek '$Label': brak wymaganego klucza Expect '$Key'." }
            }
            # MessageContains jest dozwolone: akcja, która ma się skończyć błędem (Failed), nie jest zielona na samym kodzie wyjścia,
            # tylko na komunikacie z raportu kroku 'execute' (np. brak aktywnego profilu sieci). Check (stan po akcji) jest i tak wymagany.
            foreach ($Key in @('Before') + $script:ApplyUndoExpectKeys) {
                if ($Key -eq 'MessageContains') { continue }
                if ($Case.Expect.ContainsKey($Key)) { throw "Przypadek '$Label': Flow 'Execute' zabrania klucza Expect '$Key'." }
            }
            # Bez Check zostałby tylko kod wyjścia i 'Executed' (zielony na samym "skrypt się wykonał"). Snapshot w Execute
            # jest robiony PO Cleanup, więc sprawdza tylko brak śladów po sprzątaniu, nie efekt akcji - nie zastępuje Check.
            if (-not $Case.ContainsKey('Check')) {
                throw "Przypadek '$Label': Flow 'Execute' wymaga klucza 'Check' (skrypt sprawdzający efekt akcji). 'Snapshot' w Execute jest opcjonalny i oznacza tylko brak śladów po Cleanup, więc go nie zastępuje."
            }
        }
        elseif ($Case.Flow -eq 'DetectOnly') {
            if (-not $Case.Expect.ContainsKey('Before')) { throw "Przypadek '$Label': brak wymaganego klucza Expect 'Before'." }
            foreach ($Key in $script:ApplyUndoExpectKeys + $script:ExecuteExpectKeys) {
                if ($Case.Expect.ContainsKey($Key)) { throw "Przypadek '$Label': Flow 'DetectOnly' zabrania klucza Expect '$Key'." }
            }
        }
        else {
            foreach ($Key in $script:ExecuteExpectKeys) {
                if ($Case.Expect.ContainsKey($Key)) { throw "Przypadek '$Label': Flow '$($Case.Flow)' zabrania klucza Expect '$Key'." }
            }
            foreach ($Key in $script:ExpectRequired) {
                if (-not $Case.Expect.ContainsKey($Key)) { throw "Przypadek '$Label': brak wymaganego klucza Expect '$Key'." }
            }
            foreach ($Key in $script:ExpectUndoKeys) {
                if ($Case.Flow -in @('Full', 'Reboot') -and -not $Case.Expect.ContainsKey($Key)) {
                    throw "Przypadek '$Label': Flow '$($Case.Flow)' wymaga klucza Expect '$Key'."
                }
                if ($Case.Flow -eq 'ApplyOnly' -and $Case.Expect.ContainsKey($Key)) {
                    throw "Przypadek '$Label': Flow 'ApplyOnly' zabrania klucza Expect '$Key'."
                }
            }
            if ($Case.Flow -eq 'Reboot' -and -not $Case.Expect.ContainsKey('AfterReboot')) { throw "Przypadek '$Label': Flow 'Reboot' wymaga klucza Expect 'AfterReboot'." }
            if ($Case.Flow -ne 'Reboot' -and $Case.Expect.ContainsKey('AfterReboot')) { throw "Przypadek '$Label': klucz Expect 'AfterReboot' jest dozwolony tylko we Flow 'Reboot'." }
            foreach ($Key in $script:KeyListExpectKeys) {
                if ($Case.Flow -eq 'ApplyOnly' -and $Case.Expect.ContainsKey($Key)) { throw "Przypadek '$Label': Flow 'ApplyOnly' zabrania klucza Expect '$Key' (brak undo)." }
            }
        }
        foreach ($Key in $script:KeyListExpectKeys) {
            if ($Case.Expect.ContainsKey($Key)) { Assert-VmKeyList -Label $Label -KeyName $Key -Value $Case.Expect[$Key] }
        }
        if ($Case.Expect.ContainsKey('KeyAbsentAfterUndo') -and $Case.Expect.ContainsKey('KeyPresentAfterUndo')) {
            foreach ($KeyPath in @($Case.Expect.KeyAbsentAfterUndo)) {
                if (@($Case.Expect.KeyPresentAfterUndo | Where-Object { $_ -ieq $KeyPath }).Count -gt 0) {
                    throw "Przypadek '$Label': klucz '$KeyPath' jest jednocześnie w 'KeyAbsentAfterUndo' i 'KeyPresentAfterUndo' (sprzeczne oczekiwania)."
                }
            }
        }
        if ($Case.ContainsKey('Snapshot')) { Assert-VmSnapshotShape -Label $Label -KeyName 'Snapshot' -Value $Case.Snapshot -ServiceForm Names }
        # Bez wpisów w Snapshot test "stan końcowy = stan przed" nie powstaje, więc undo z katalogu nie byłoby niczym zweryfikowane.
        if ($CatalogKind -ceq 'BuiltIn' -and $Case.Flow -in @('Full', 'Reboot') -and -not $Case.ContainsKey('Skip')) {
            $SnapshotEntries = 0
            if ($Case.ContainsKey('Snapshot')) {
                foreach ($Section in @('Registry', 'Services')) {
                    if ($Case.Snapshot.ContainsKey($Section)) { $SnapshotEntries += @($Case.Snapshot[$Section]).Count }
                }
            }
            if ($SnapshotEntries -eq 0) {
                throw "Przypadek '$Label': Flow '$($Case.Flow)' z Catalog 'BuiltIn' wymaga niepustego klucza 'Snapshot' (co najmniej jeden wpis Registry albo Services); bez niego test 'stan końcowy = stan przed' byłby pusty."
            }
        }
        if ($Case.Expect.ContainsKey('AfterApplySnapshot')) { Assert-VmSnapshotShape -Label $Label -KeyName 'Expect.AfterApplySnapshot' -Value $Case.Expect.AfterApplySnapshot -ServiceForm Entries }
        if ($Case.Expect.ContainsKey('NotWrittenTo')) {
            Assert-VmRegistryEntries -Label $Label -KeyName 'Expect.NotWrittenTo' -Entries $Case.Expect.NotWrittenTo -AllowCurrentUser
            # Scenariusz asertuje NotWrittenTo tylko razem z Expect.UserSid; bez niego asercja byłaby cicho pominięta.
            if (-not $Case.Expect.ContainsKey('UserSid')) { throw "Przypadek '$Label': 'Expect.NotWrittenTo' wymaga klucza 'Expect.UserSid' (bez niego asercja nie jest sprawdzana)." }
        }
        $Result.Add($Case)
    }
    return , $Result.ToArray()
}

function Split-VmCaseRuns {
    # Dzieli przypadki na przebiegi: grupa "main" (aktywne zwykłe przypadki; dołączone są do niej też pominięte, żeby były
    # widoczne w raporcie), po jednym przebiegu "reboot-<nazwa>" na każdy aktywny przypadek Reboot i "autostart-<nazwa>" na każdy
    # aktywny Autostart z Set, oraz - tylko gdy nie ma
    # żadnego aktywnego zwykłego przypadku - przebieg "skipped" (SkipOnly): same rekordy pominięcia, bez VM i bez Pestera.
    # Main złożony wyłącznie z pominiętych kończyłby się błędem "Pester nie wykonał żadnego testu" i blokował przebiegi Reboot.
    [CmdletBinding()]
    [OutputType([hashtable[]])]
    param(
        [Parameter(Mandatory)][AllowEmptyCollection()][hashtable[]]$Cases
    )
    $Main = [System.Collections.Generic.List[hashtable]]::new()
    $Separate = [System.Collections.Generic.List[hashtable]]::new()
    $Labels = [System.Collections.Generic.HashSet[string]]::new([System.StringComparer]::OrdinalIgnoreCase)
    $ActiveMain = 0
    foreach ($Case in $Cases) {
        # Autostart z Set restartuje VM jak Reboot, więc dostaje własny przebieg; bez Set (sama lista) idzie do main.
        $SeparatePrefix = if ($Case.Flow -eq 'Reboot') { 'reboot-' } elseif ($Case.Flow -eq 'Autostart' -and $Case.ContainsKey('Set')) { 'autostart-' } else { $null }
        if ($null -ne $SeparatePrefix -and -not $Case.ContainsKey('Skip')) {
            $Label = $SeparatePrefix + ([string]$Case.Name -replace '[^A-Za-z0-9._-]', '_')
            if (-not $Labels.Add($Label)) { throw "Przypadek '$($Case.Name)': zduplikowana etykieta przebiegu '$Label'." }
            $Separate.Add(@{ Label = $Label; Cases = @($Case); SkipOnly = $false })
        }
        else {
            $Main.Add($Case)
            if (-not $Case.ContainsKey('Skip')) { $ActiveMain++ }
        }
    }
    $Plan = [System.Collections.Generic.List[hashtable]]::new()
    if ($ActiveMain -gt 0) { $Plan.Add(@{ Label = 'main'; Cases = $Main.ToArray(); SkipOnly = $false }) }
    elseif ($Main.Count -gt 0) { $Plan.Add(@{ Label = 'skipped'; Cases = $Main.ToArray(); SkipOnly = $true }) }
    foreach ($Item in $Separate) { $Plan.Add($Item) }
    return $Plan.ToArray()
}

function Expand-VmValue {
    param($Value, [string]$UserSid)
    if ($Value -is [string]) { return $Value.Replace('{UserSid}', $UserSid) }
    if ($Value -is [hashtable]) {
        $Copy = @{}
        foreach ($Key in $Value.Keys) { $Copy[$Key] = Expand-VmValue -Value $Value[$Key] -UserSid $UserSid }
        return $Copy
    }
    if ($Value -is [System.Array]) {
        $Items = [System.Collections.Generic.List[object]]::new()
        foreach ($Item in $Value) { $Items.Add((Expand-VmValue -Value $Item -UserSid $UserSid)) }
        return , $Items.ToArray()
    }
    return $Value
}

function Expand-VmCasePlaceholders {
    [CmdletBinding()]
    [OutputType([hashtable])]
    param(
        [Parameter(Mandatory)][hashtable]$Case,
        [Parameter(Mandatory)][string]$UserSid
    )
    return Expand-VmValue -Value $Case -UserSid $UserSid
}

function ConvertTo-DsoCliArguments {
    [CmdletBinding()]
    [OutputType([string[]])]
    param(
        [Parameter(Mandatory)][ValidateSet('detect', 'apply', 'undo', 'execute', 'list-apps', 'remove-app', 'install-apps', 'list-install', 'list-autostart', 'set-autostart', 'undo-autostart')][string]$Command,
        # Id tweaka albo aplikacji (remove-app); list-apps i polecenia instalacji nie mają id.
        [AllowNull()][AllowEmptyString()][string]$TweakId,
        [AllowNull()][AllowEmptyString()][string]$CatalogPath,
        [Parameter(Mandatory)][string]$OutPath,
        [ValidateSet('skip', 'ifavailable', 'required')][string]$RestorePoint,
        # install-apps: id pozycji (install.<nazwa> albo winget:<id>), łączone przecinkiem bez spacji.
        [AllowNull()][AllowEmptyCollection()][string[]]$AppIds,
        # list-install/install-apps: lista apps.json w VM (--apps-catalog).
        [AllowNull()][AllowEmptyString()][string]$AppsCatalog,
        # set-autostart: stan docelowy (druga wartość --set-autostart).
        [ValidateSet('disabled', 'enabled')][string]$AutostartState
    )
    foreach ($Path in @($CatalogPath, $OutPath, $AppsCatalog)) {
        if (-not [string]::IsNullOrEmpty($Path) -and $Path -match '\s') { throw "Ścieżka '$Path' zawiera spację lub inny biały znak (niedozwolone w argumentach CLI)." }
    }
    $IsAutostartCommand = $Command -in @('list-autostart', 'set-autostart', 'undo-autostart')
    if ($IsAutostartCommand) {
        # Autostart: bez katalogu tweaków, punktu przywracania i list aplikacji; stan tylko przy set-autostart.
        if (-not [string]::IsNullOrEmpty($CatalogPath)) { throw "Polecenie $Command nie przyjmuje katalogu tweaków (--catalog)." }
        if ($RestorePoint) { throw "Polecenie $Command nie przyjmuje punktu przywracania (--restore-point)." }
        if (($null -ne $AppIds -and $AppIds.Count -gt 0) -or -not [string]::IsNullOrEmpty($AppsCatalog)) { throw "Polecenie $Command nie przyjmuje AppIds ani AppsCatalog." }
        if ($Command -ne 'set-autostart' -and $AutostartState) { throw "Polecenie $Command jest bez stanu (stan podaje się tylko przy set-autostart)." }
        if ($Command -eq 'list-autostart') {
            if (-not [string]::IsNullOrEmpty($TweakId)) { throw "Polecenie list-autostart jest bez id (podano '$TweakId')." }
            $Arguments = @('--list-autostart')
        }
        else {
            if ([string]::IsNullOrWhiteSpace($TweakId)) { throw "Polecenie $Command wymaga id pozycji autostartu." }
            if ($TweakId -match '\s') { throw "Id '$TweakId' zawiera spację lub inny biały znak (niedozwolone w argumentach CLI)." }
            $Arguments = @("--$Command", $TweakId)
            if ($Command -eq 'set-autostart') {
                if (-not $AutostartState) { throw "Polecenie set-autostart wymaga stanu (disabled albo enabled)." }
                $Arguments += $AutostartState
            }
        }
        $Arguments += @('--out', $OutPath)
        return [string[]]$Arguments
    }
    if ($AutostartState) { throw "Polecenie $Command nie przyjmuje stanu autostartu (tylko set-autostart)." }
    $IsInstallCommand = $Command -in @('install-apps', 'list-install')
    if (-not $IsInstallCommand -and -not [string]::IsNullOrEmpty($AppsCatalog)) { throw "Polecenie $Command nie przyjmuje AppsCatalog (--apps-catalog dotyczy list-install i install-apps)." }
    $HasAppIds = $null -ne $AppIds -and $AppIds.Count -gt 0
    if (-not $IsInstallCommand -and $HasAppIds) { throw "Polecenie $Command nie przyjmuje AppIds (tylko install-apps)." }
    if ($IsInstallCommand) {
        if (-not [string]::IsNullOrEmpty($CatalogPath)) { throw "Polecenie $Command nie przyjmuje katalogu tweaków (--catalog)." }
        if (-not [string]::IsNullOrEmpty($TweakId)) { throw "Polecenie $Command jest bez id tweaka (podano '$TweakId'); pozycje instalacji idą w AppIds." }
        if ($Command -eq 'list-install' -and $HasAppIds) { throw "Polecenie list-install nie przyjmuje AppIds (tylko install-apps)." }
        if ($Command -eq 'list-install') {
            $Arguments = @('--list-install')
        }
        else {
            if ($null -eq $AppIds -or $AppIds.Count -eq 0) { throw "Polecenie install-apps wymaga AppIds." }
            foreach ($Id in $AppIds) {
                if ([string]::IsNullOrWhiteSpace($Id) -or $Id -match '[\s,]') { throw "AppIds: id '$Id' jest puste albo ma spację lub przecinek (niedozwolone w argumentach CLI)." }
            }
            $Arguments = @('--install-apps', ($AppIds -join ','))
        }
        if (-not [string]::IsNullOrEmpty($AppsCatalog)) { $Arguments += @('--apps-catalog', $AppsCatalog) }
        $Arguments += @('--out', $OutPath)
        return [string[]]$Arguments
    }
    $IsAppCommand = $Command -in @('list-apps', 'remove-app')
    if ($IsAppCommand -and -not [string]::IsNullOrEmpty($CatalogPath)) { throw "Polecenie $Command nie przyjmuje katalogu tweaków (--catalog)." }
    if ($Command -eq 'list-apps') {
        if (-not [string]::IsNullOrEmpty($TweakId)) { throw "Polecenie list-apps jest bez id (podano '$TweakId')." }
        $Arguments = @('--list-apps')
    }
    else {
        if ([string]::IsNullOrWhiteSpace($TweakId)) { throw "Polecenie $Command wymaga id." }
        # Start-Process w VM nie cytuje argumentów: id ze spacją rozpadłby się na dwa (Ruling R6).
        if ($TweakId -match '\s') { throw "Id '$TweakId' zawiera spację lub inny biały znak (niedozwolone w argumentach CLI)." }
        $Arguments = @("--$Command", $TweakId)
    }
    if (-not [string]::IsNullOrEmpty($CatalogPath)) { $Arguments += @('--catalog', $CatalogPath) }
    $Arguments += @('--out', $OutPath)
    if ($Command -in @('apply', 'remove-app') -and $RestorePoint) { $Arguments += @('--restore-point', $RestorePoint) }
    return [string[]]$Arguments
}

function Invoke-DsoCli {
    [CmdletBinding()]
    param(
        [Parameter(Mandatory)][AllowNull()][System.Management.Automation.Runspaces.PSSession]$Session,
        [Parameter(Mandatory)][string]$ExePath,
        [Parameter(Mandatory)][AllowEmptyCollection()][string[]]$Arguments,
        [Parameter(Mandatory)][string]$OutPath,
        [int]$TimeoutSeconds = 600,
        # Konto zalogowane w VM (np. 'dso'): CLI startuje przez zadanie Harmonogramu Interactive z najwyższymi uprawnieniami
        # (jak właściciel laptopa, który podnosi DSOptimizer przez UAC; wzorzec K2 eksploracji E4A2). Puste = Administrator PS Direct.
        [ValidatePattern('^([A-Za-z0-9_-]+)?\z')][string]$RunAsUser
    )
    # Język CLI przypięty do polskiego: teksty w raportach (np. MessageContains) mają być stałe niezależnie od kultury VM.
    if (@($Arguments) -notcontains '--lang') { $Arguments = @($Arguments) + @('--lang', 'pl') }
    if ($RunAsUser) {
        $RemoteAsUser = {
            param([string]$ExePath, [string[]]$Arguments, [string]$OutPath, [int]$TimeoutMs, [string]$UserName)
            $ErrorActionPreference = 'Stop'
            if (Test-Path -LiteralPath $OutPath) { Remove-Item -LiteralPath $OutPath -Force }
            $TaskName = 'DSO-VmCli-' + [guid]::NewGuid().ToString('N')
            $Action = New-ScheduledTaskAction -Execute $ExePath -Argument ($Arguments -join ' ')
            $Principal = New-ScheduledTaskPrincipal -UserId $UserName -LogonType Interactive -RunLevel Highest
            $Settings = New-ScheduledTaskSettingsSet -AllowStartIfOnBatteries -DontStopIfGoingOnBatteries -ExecutionTimeLimit ([TimeSpan]::FromMilliseconds($TimeoutMs + 60000))
            $ExitCode = -1
            $TimedOut = $false
            try {
                $null = Register-ScheduledTask -TaskName $TaskName -Action $Action -Principal $Principal -Settings $Settings -Force
                Start-ScheduledTask -TaskName $TaskName
                $Watch = [System.Diagnostics.Stopwatch]::StartNew()
                while ($true) {
                    $State = [string](Get-ScheduledTask -TaskName $TaskName).State
                    $LastResult = [int64](Get-ScheduledTaskInfo -TaskName $TaskName).LastTaskResult
                    # 267009 = SCHED_S_TASK_RUNNING, 267011 = SCHED_S_TASK_HAS_NOT_RUN: to jeszcze nie kod wyjścia CLI.
                    if ($State -ne 'Running' -and $LastResult -notin @(267009, 267011)) { $ExitCode = $LastResult; break }
                    if ($Watch.ElapsedMilliseconds -ge $TimeoutMs) {
                        $TimedOut = $true
                        try { Stop-ScheduledTask -TaskName $TaskName } catch { Write-Verbose "Stop-ScheduledTask nie powiódł się: $($_.Exception.Message)" }
                        break
                    }
                    Start-Sleep -Milliseconds 500
                }
            }
            finally {
                try { Unregister-ScheduledTask -TaskName $TaskName -Confirm:$false } catch { Write-Verbose "Unregister-ScheduledTask nie powiódł się: $($_.Exception.Message)" }
            }
            $Json = $null
            if (Test-Path -LiteralPath $OutPath) { $Json = Get-Content -LiteralPath $OutPath -Raw -Encoding UTF8 }
            return @{ ExitCode = $ExitCode; TimedOut = $TimedOut; Json = $Json }
        }
        $Raw = Invoke-Command -Session $Session -ScriptBlock $RemoteAsUser -ArgumentList @($ExePath, $Arguments, $OutPath, ($TimeoutSeconds * 1000), $RunAsUser)
        return ConvertTo-DsoCliResult -Raw $Raw
    }
    $Remote = {
        param([string]$ExePath, [string[]]$Arguments, [string]$OutPath, [int]$TimeoutMs)
        $ErrorActionPreference = 'Stop'
        if (Test-Path -LiteralPath $OutPath) { Remove-Item -LiteralPath $OutPath -Force }
        $Process = Start-Process -FilePath $ExePath -ArgumentList $Arguments -PassThru -WindowStyle Hidden
        $null = $Process.Handle
        $TimedOut = -not $Process.WaitForExit($TimeoutMs)
        if ($TimedOut) {
            try { $Process.Kill() } catch { Write-Verbose "Kill nie powiódł się: $($_.Exception.Message)" }
            $ExitCode = -1
        }
        else {
            $ExitCode = $Process.ExitCode
        }
        $Json = $null
        if (Test-Path -LiteralPath $OutPath) { $Json = Get-Content -LiteralPath $OutPath -Raw -Encoding UTF8 }
        return @{ ExitCode = $ExitCode; TimedOut = $TimedOut; Json = $Json }
    }
    $Raw = Invoke-Command -Session $Session -ScriptBlock $Remote -ArgumentList @($ExePath, $Arguments, $OutPath, ($TimeoutSeconds * 1000))
    return ConvertTo-DsoCliResult -Raw $Raw
}

function ConvertTo-DsoCliResult {
    # Surowy wynik zdalnego bloku (ExitCode, TimedOut, Json) -> wynik kroku z raportem; uszkodzony JSON = Report null + ReportError.
    param([Parameter(Mandatory)]$Raw)
    $Report = $null
    $ReportError = $null
    if ($Raw.Json) {
        try { $Report = $Raw.Json | ConvertFrom-Json } catch { $Report = $null; $ReportError = $_.Exception.Message }
    }
    return [pscustomobject]@{ ExitCode = $Raw.ExitCode; TimedOut = $Raw.TimedOut; Report = $Report; Json = $Raw.Json; ReportError = $ReportError }
}

function Copy-VmFixtures {
    # Pliki pakietów testowych (tests\vm\fixtures: źródło deinstalatora, skrypty instalacji i MSI) do VM; budowane są dopiero w VM (Setup).
    [CmdletBinding()]
    param(
        [Parameter(Mandatory)][AllowNull()][System.Management.Automation.Runspaces.PSSession]$Session,
        [Parameter(Mandatory)][string]$Destination
    )
    if ($Destination -notmatch '^[A-Za-z]:\\[^\s]+\z') { throw "Copy-VmFixtures: niedozwolony katalog docelowy '$Destination'." }
    $Source = [System.IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..\fixtures'))
    Invoke-Command -Session $Session -ArgumentList $Destination -ScriptBlock {
        param([string]$Directory)
        $ErrorActionPreference = 'Stop'
        $null = New-Item -ItemType Directory -Path $Directory -Force
    } | Out-Null
    Copy-Item -Path (Join-Path $Source '*') -Destination $Destination -ToSession $Session -Force
}

function Get-DsoStateSnapshot {
    # Samowystarczalna: wysyłana do VM jako ${function:Get-DsoStateSnapshot}, nie woła innych funkcji modułu.
    [CmdletBinding()]
    param(
        [Parameter(Mandatory)][AllowEmptyCollection()][object[]]$Registry,
        [Parameter(Mandatory)][AllowEmptyCollection()][string[]]$Services,
        # HKCU\ wskazuje hive Administratora sesji PS Direct, nie użytkownika 'dso': dozwolone tylko dla Expect.NotWrittenTo.
        [bool]$AllowCurrentUser = $false
    )
    $RegistryResult = @()
    foreach ($Entry in $Registry) {
        $Path = [string]$Entry.Path
        $Name = [string]$Entry.Name
        # Nieznany prefiks trafiłby do systemu plików (Get-Item -> ItemNotFound -> Exists = false): zielone na pusto.
        if ($Path -notmatch '^(HKLM|HKU)\\' -and -not ($AllowCurrentUser -and $Path -match '^HKCU\\')) {
            $Hint = if ($Path -match '^HKCU\\') { " HKCU\ wskazuje hive Administratora sesji PS Direct; dla użytkownika użyj 'HKU\{UserSid}\...' (jak w cases.psd1)." } else { '' }
            throw "Get-DsoStateSnapshot: nieznany prefiks ścieżki '$Path'; dozwolone tylko 'HKLM\' i 'HKU\' (HKCU\ wyłącznie dla NotWrittenTo).$Hint"
        }
        $Native = $Path -replace '^HKLM\\', 'Registry::HKEY_LOCAL_MACHINE\' -replace '^HKU\\', 'Registry::HKEY_USERS\' -replace '^HKCU\\', 'Registry::HKEY_CURRENT_USER\'
        $Exists = $false; $Kind = $null; $Data = $null
        $Key = $null
        try { $Key = Get-Item -LiteralPath $Native -ErrorAction Stop }
        catch {
            $NotFound = ($_.Exception -is [System.Management.Automation.ItemNotFoundException]) -or ($_.Exception -is [System.Management.Automation.DriveNotFoundException])
            if (-not $NotFound) { throw }
        }
        if ($null -ne $Key) {
            try {
                $Value = $Key.GetValue($Name, $null, 'DoNotExpandEnvironmentNames')
                if ($null -ne $Value) {
                    $Exists = $true
                    $Kind = [string]$Key.GetValueKind($Name)
                    if ($Value -is [byte[]]) { $Data = [BitConverter]::ToString($Value) }
                    elseif ($Value -is [string[]]) { $Data = $Value -join "`n" }
                    else { $Data = [string]$Value }
                }
            }
            finally { $Key.Dispose() }
        }
        $RegistryResult += [pscustomobject]@{ Key = "$Path\$Name"; Exists = $Exists; Kind = $Kind; Data = $Data }
    }
    $ServiceResult = @()
    foreach ($ServiceName in $Services) {
        $Escaped = $ServiceName.Replace("'", "''")
        # -ErrorAction Stop: brak usługi = pusty wynik bez błędu (Exists=$false), błąd zapytania = wyjątek (nie fałszywe "brak").
        $Item = Get-CimInstance -ClassName Win32_Service -Filter "Name='$Escaped'" -ErrorAction Stop
        $Delayed = $null
        if ($null -ne $Item) {
            $Delayed = [bool]$Item.DelayedAutoStart
        }
        else {
            $Item = Get-CimInstance -ClassName Win32_SystemDriver -Filter "Name='$Escaped'" -ErrorAction Stop
        }
        if ($null -eq $Item) {
            $ServiceResult += [pscustomobject]@{ Name = $ServiceName; Exists = $false; StartMode = $null; DelayedAutoStart = $null; State = $null }
        }
        else {
            $ServiceResult += [pscustomobject]@{ Name = $ServiceName; Exists = $true; StartMode = [string]$Item.StartMode; DelayedAutoStart = $Delayed; State = [string]$Item.State }
        }
    }
    return [pscustomobject]@{ Registry = $RegistryResult; Services = $ServiceResult }
}

function Get-VmStateSnapshot {
    [CmdletBinding()]
    param(
        [Parameter(Mandatory)][AllowNull()][System.Management.Automation.Runspaces.PSSession]$Session,
        [Parameter(Mandatory)][hashtable]$Snapshot,
        [switch]$AllowCurrentUser
    )
    $Registry = @()
    if ($Snapshot.ContainsKey('Registry')) { $Registry = @($Snapshot.Registry) }
    [string[]]$Services = @()
    if ($Snapshot.ContainsKey('Services')) { $Services = [string[]]@($Snapshot.Services) }
    return Invoke-Command -Session $Session -ScriptBlock ${function:Get-DsoStateSnapshot} -ArgumentList @($Registry, $Services, [bool]$AllowCurrentUser)
}

function Compare-VmSnapshot {
    [CmdletBinding()]
    [OutputType([string[]])]
    param(
        [Parameter(Mandatory)]$Before,
        [Parameter(Mandatory)]$After
    )
    $Diff = [System.Collections.Generic.List[string]]::new()
    $Sections = @(
        @{ Property = 'Registry'; Id = 'Key'; Fields = @('Exists', 'Kind', 'Data'); Label = 'Rejestr' }
        @{ Property = 'Services'; Id = 'Name'; Fields = @('Exists', 'StartMode', 'DelayedAutoStart', 'State'); Label = 'Usługa' }
    )
    foreach ($Section in $Sections) {
        $Id = $Section.Id
        $AfterMap = @{}
        foreach ($Item in @($After.($Section.Property))) { $AfterMap[[string]$Item.$Id] = $Item }
        foreach ($Old in @($Before.($Section.Property))) {
            $Identity = [string]$Old.$Id
            if (-not $AfterMap.ContainsKey($Identity)) { $Diff.Add("$($Section.Label) '$Identity': brak wpisu w stanie po"); continue }
            $New = $AfterMap[$Identity]
            foreach ($Field in $Section.Fields) {
                if ([string]$Old.$Field -cne [string]$New.$Field) {
                    $Diff.Add("$($Section.Label) '$Identity': $Field '$($Old.$Field)' -> '$($New.$Field)'")
                }
            }
        }
    }
    return , $Diff.ToArray()
}

function Test-VmSnapshotMatches {
    [CmdletBinding()]
    [OutputType([string[]])]
    param(
        [Parameter(Mandatory)]$Snapshot,
        [Parameter(Mandatory)][hashtable]$Expected
    )
    $Problems = [System.Collections.Generic.List[string]]::new()
    $Sections = @(
        @{ Property = 'Registry'; Label = 'Rejestr' }
        @{ Property = 'Services'; Label = 'Usługa' }
    )
    foreach ($Section in $Sections) {
        if (-not $Expected.ContainsKey($Section.Property)) { continue }
        $ActualMap = @{}
        foreach ($Item in @($Snapshot.($Section.Property))) {
            $Identity = if ($Section.Property -eq 'Registry') { [string]$Item.Key } else { [string]$Item.Name }
            $ActualMap[$Identity] = $Item
        }
        foreach ($Want in @($Expected[$Section.Property])) {
            $Identity = if ($Section.Property -eq 'Registry') { "$($Want.Path)\$($Want.Name)" } else { [string]$Want.Name }
            if (-not $ActualMap.ContainsKey($Identity)) { $Problems.Add("$($Section.Label) '$Identity': brak wpisu w snapshocie"); continue }
            $Actual = $ActualMap[$Identity]
            foreach ($Field in $Want.Keys) {
                if ($Field -in @('Path', 'Name')) { continue }
                if ([string]$Actual.$Field -cne [string]$Want[$Field]) {
                    $Problems.Add("$($Section.Label) '$Identity': $Field oczekiwano '$($Want[$Field])', jest '$($Actual.$Field)'")
                }
            }
        }
    }
    return , $Problems.ToArray()
}

function Invoke-VmSetup {
    [CmdletBinding()]
    param(
        [Parameter(Mandatory)][AllowNull()][System.Management.Automation.Runspaces.PSSession]$Session,
        [Parameter(Mandatory)][AllowEmptyCollection()][string[]]$Setup,
        # Wyjście skryptów Setup jako tekst (Flow Autostart zapisuje je w rekordzie, np. wartości klucza zadania Terminala).
        [switch]$PassThru
    )
    $Output = [System.Collections.Generic.List[string]]::new()
    foreach ($Command in $Setup) {
        $Block = [scriptblock]::Create("`$ErrorActionPreference = 'Stop'; $Command")
        foreach ($Line in @(Invoke-Command -Session $Session -ScriptBlock $Block)) { if ($null -ne $Line) { $Output.Add([string]$Line) } }
    }
    if ($PassThru) { return , $Output.ToArray() }
}

function Invoke-VmCheck {
    # Uruchamia skrypt Check w VM. Zaliczony tylko wtedy, gdy wynik to dokładnie jedno $true;
    # wszystko inne (w tym wyjątek i brak wyniku) jest opisem porażki - nigdy "Passed" z braku danych.
    [CmdletBinding()]
    [OutputType([string])]
    param(
        [Parameter(Mandatory)][AllowNull()][System.Management.Automation.Runspaces.PSSession]$Session,
        [Parameter(Mandatory)][string]$Script
    )
    try {
        $Block = [scriptblock]::Create("`$ErrorActionPreference = 'Stop'; $Script")
        $Output = @(Invoke-Command -Session $Session -ScriptBlock $Block)
    }
    catch {
        return "Check rzucił wyjątek: $($_.Exception.Message)"
    }
    if ($Output.Count -eq 0) { return 'Check nie zwrócił żadnego wyniku (oczekiwano $true).' }
    if ($Output.Count -eq 1 -and $Output[0] -is [bool] -and $Output[0]) { return 'Passed' }
    return "Check zwrócił: $(@($Output | ForEach-Object { [string]$_ }) -join '; ')"
}

function Get-VmRestorePointCount {
    [CmdletBinding()]
    [OutputType([int])]
    param(
        [Parameter(Mandatory)][AllowNull()][System.Management.Automation.Runspaces.PSSession]$Session
    )
    return [int](Invoke-Command -Session $Session -ScriptBlock { @(Get-ComputerRestorePoint -ErrorAction Stop).Count })
}

# CreateRestorePoint zwraca 0 także wtedy, gdy punkt jeszcze nie jest widoczny (pojawia się do ok. 1,2 s później),
# więc po apply odpytujemy licznik co 250 ms, co najmniej 10 s (40 oczekiwań = 41 odczytów; realny czas wlicza czas odczytów), aż przekroczy wartość sprzed.
$script:RestorePointPollIntervalMs = 250
$script:RestorePointPollMaxWaits = 40

function Wait-VmRestorePointCount {
    [CmdletBinding()]
    [OutputType([int])]
    param(
        [Parameter(Mandatory)][AllowNull()][System.Management.Automation.Runspaces.PSSession]$Session,
        [Parameter(Mandatory)][int]$CountBefore
    )
    $Count = Get-VmRestorePointCount -Session $Session
    $Waits = 0
    while ($Count -le $CountBefore -and $Waits -lt $script:RestorePointPollMaxWaits) {
        Start-Sleep -Milliseconds $script:RestorePointPollIntervalMs
        $Waits++
        $Count = Get-VmRestorePointCount -Session $Session
    }
    return $Count
}

function Get-VmLastBootTime {
    [CmdletBinding()]
    [OutputType([datetime])]
    param(
        [Parameter(Mandatory)][AllowNull()][System.Management.Automation.Runspaces.PSSession]$Session
    )
    return [datetime](Invoke-Command -Session $Session -ScriptBlock { (Get-CimInstance -ClassName Win32_OperatingSystem -ErrorAction Stop).LastBootUpTime })
}

function Send-VmRestart {
    # Zerwanie połączenia w trakcie restartu jest oczekiwane; o tym, czy restart się odbył, decyduje wyłącznie LastBootUpTime.
    [CmdletBinding()]
    param(
        [Parameter(Mandatory)][AllowNull()][System.Management.Automation.Runspaces.PSSession]$Session
    )
    try { Invoke-Command -Session $Session -ScriptBlock { Restart-Computer -Force -ErrorAction Stop } -ErrorAction Stop | Out-Null }
    catch { Write-Verbose "Restart-Computer: połączenie zerwane lub błąd (oczekiwane przy restarcie): $($_.Exception.Message)" }
}

function New-VmRestartMarker {
    # Tworzy w VM klucz volatile (RegistryOptions.Volatile) HKLM\SOFTWARE\DSOptimizer.RestartMarker. Takie klucze znikają przy
    # każdym restarcie, więc jego brak po restarcie potwierdza prawdziwy restart niezależnie od zegara (LastBootUpTime
    # przesuwa się też przy korekcie czasu). New-Item w PS 5.1 nie umie tworzyć kluczy volatile, stąd API .NET.
    [CmdletBinding()]
    param(
        [Parameter(Mandatory)][AllowNull()][System.Management.Automation.Runspaces.PSSession]$Session
    )
    $Created = Invoke-Command -Session $Session -ArgumentList $script:RestartMarkerSubKey -ScriptBlock {
        param([string]$SubKey)
        $ErrorActionPreference = 'Stop'
        $Base = [Microsoft.Win32.RegistryKey]::OpenBaseKey([Microsoft.Win32.RegistryHive]::LocalMachine, [Microsoft.Win32.RegistryView]::Registry64)
        try {
            # Pozostałość nieulotnego klucza o tej nazwie przetrwałaby restart i dała fałszywy brak restartu.
            $Base.DeleteSubKeyTree($SubKey, $false)
            $Key = $Base.CreateSubKey($SubKey, $true, [Microsoft.Win32.RegistryOptions]::Volatile)
            try { $Key.SetValue('Utworzono', [DateTime]::UtcNow.ToString('o')) } finally { $Key.Dispose() }
            $Check = $Base.OpenSubKey($SubKey)
            $Exists = $null -ne $Check
            if ($null -ne $Check) { $Check.Dispose() }
            return $Exists
        }
        finally { $Base.Dispose() }
    }
    if (@($Created).Count -ne 1 -or $Created[0] -isnot [bool] -or -not $Created[0]) {
        throw 'Nie udało się utworzyć ulotnego znacznika restartu w VM (brak potwierdzenia); bez niego restart nie byłby zweryfikowany.'
    }
}

function Test-VmRestartMarker {
    # True = znacznik nadal istnieje (VM nie zrestartowała się od jego utworzenia). Brak odpowiedzi to błąd, nie "znacznik zniknął".
    [CmdletBinding()]
    [OutputType([bool])]
    param(
        [Parameter(Mandatory)][AllowNull()][System.Management.Automation.Runspaces.PSSession]$Session
    )
    $Exists = @(Invoke-Command -Session $Session -ArgumentList $script:RestartMarkerSubKey -ScriptBlock {
            param([string]$SubKey)
            $Base = [Microsoft.Win32.RegistryKey]::OpenBaseKey([Microsoft.Win32.RegistryHive]::LocalMachine, [Microsoft.Win32.RegistryView]::Registry64)
            try {
                $Key = $Base.OpenSubKey($SubKey)
                $Found = $null -ne $Key
                if ($null -ne $Key) { $Key.Dispose() }
                return $Found
            }
            finally { $Base.Dispose() }
        })
    if ($Exists.Count -ne 1 -or $Exists[0] -isnot [bool]) { throw 'Nie udało się odczytać ulotnego znacznika restartu z VM (brak jednoznacznej odpowiedzi).' }
    return [bool]$Exists[0]
}

function Get-VmKeyExistence {
    [CmdletBinding()]
    param(
        [Parameter(Mandatory)][AllowNull()][System.Management.Automation.Runspaces.PSSession]$Session,
        [Parameter(Mandatory)][string[]]$Keys
    )
    foreach ($Key in $Keys) {
        if ($Key -notmatch '^(HKLM|HKU)\\') { throw "Get-VmKeyExistence: nieznany prefiks ścieżki '$Key'; dozwolone tylko 'HKLM\' i 'HKU\'." }
    }
    $Raw = Invoke-Command -Session $Session -ArgumentList @(, $Keys) -ScriptBlock {
        param([string[]]$Keys)
        foreach ($Key in $Keys) {
            $Native = $Key -replace '^HKLM\\', 'Registry::HKEY_LOCAL_MACHINE\' -replace '^HKU\\', 'Registry::HKEY_USERS\'
            [pscustomobject]@{ Key = $Key; Exists = [bool](Test-Path -LiteralPath $Native) }
        }
    }
    return @($Raw | ForEach-Object { [pscustomobject]@{ Key = [string]$_.Key; Exists = [bool]$_.Exists } })
}

function Restart-VmAndReconnect {
    # Prawdziwy restart: odczyt LastBootUpTime, Restart-Computer, zamknięcie starej sesji, Reconnect i odczyt LastBootUpTime
    # na nowej sesji. Sukces tylko gdy nowy czas jest późniejszy; inaczej (stara instancja) ponawia, najwyżej
    # $script:RebootMaxReconnects wywołań Reconnect i $script:RebootTimeoutSeconds s. Dodatkowo przed restartem powstaje
    # ulotny znacznik (klucz volatile), który musi zniknąć: sam LastBootUpTime przesuwa się też przy korekcie zegara. Błąd Reconnect (w tym uwierzytelnienia)
    # NIGDY nie jest ponawiany - wychodzi od razu (ochrona przed blokadą konta).
    [CmdletBinding()]
    param(
        [Parameter(Mandatory)][AllowNull()][System.Management.Automation.Runspaces.PSSession]$Session,
        [Parameter(Mandatory)][scriptblock]$Reconnect,
        [Parameter(Mandatory)][string]$Step
    )
    $BootBefore = Get-VmLastBootTime -Session $Session
    New-VmRestartMarker -Session $Session
    Send-VmRestart -Session $Session
    if ($null -ne $Session) { Remove-PSSession -Session $Session -ErrorAction SilentlyContinue }
    $Deadline = (Get-Date).AddSeconds($script:RebootTimeoutSeconds)
    $Attempts = 0
    while ($true) {
        $Attempts++
        # Numer próby trafia do Reconnect: oczekiwanie po stronie hosta na zgaśnięcie starej instancji dotyczy tylko pierwszej.
        $Output = @(& $Reconnect $Attempts)
        $NewSession = if ($Output.Count -gt 0) { $Output[$Output.Count - 1] } else { $null }
        if ($NewSession -isnot [System.Management.Automation.Runspaces.PSSession]) {
            throw "Krok '$Step': Reconnect nie zwrócił sesji PowerShell Direct."
        }
        $BootAfter = $null
        $MarkerPresent = $true
        $ReadError = $null
        try {
            $BootAfter = Get-VmLastBootTime -Session $NewSession
            # Restart = późniejszy LastBootUpTime ORAZ zniknięcie ulotnego znacznika (sam zegar mógł się przesunąć).
            if ($null -ne $BootAfter -and $BootAfter -gt $BootBefore) { $MarkerPresent = Test-VmRestartMarker -Session $NewSession }
        }
        catch { $ReadError = $_.Exception.Message }
        if ($null -ne $BootAfter -and $BootAfter -gt $BootBefore -and -not $MarkerPresent) {
            return [pscustomobject]@{
                Session = $NewSession
                Record  = [pscustomobject]@{ Step = $Step; BootBefore = $BootBefore.ToString('o'); BootAfter = $BootAfter.ToString('o'); Attempts = $Attempts; MarkerGone = $true }
            }
        }
        Remove-PSSession -Session $NewSession -ErrorAction SilentlyContinue
        if ($Attempts -ge $script:RebootMaxReconnects -or (Get-Date) -ge $Deadline) {
            $Detail = if ($ReadError) { " Ostatni błąd odczytu: $ReadError" } elseif ($MarkerPresent -and $null -ne $BootAfter -and $BootAfter -gt $BootBefore) { " LastBootUpTime po: $BootAfter, ale ulotny znacznik restartu nadal istnieje (zegar przesunięty bez restartu)." } else { " LastBootUpTime po: $BootAfter." }
            throw "Krok '$Step': restart nie nastąpił (LastBootUpTime przed: $BootBefore) po $Attempts wywołaniach Reconnect.$Detail"
        }
        Start-Sleep -Seconds $script:RebootRetryDelaySeconds
    }
}

function Get-VmAutostartExpectedStart {
    # Czy pozycja ma wystartować po restarcie fazy: Set (enabled = start, disabled = brak) albo Restore (undo = stan Initial,
    # domyślnie enabled; enable = start; disable = brak).
    [CmdletBinding()]
    [OutputType([bool])]
    param(
        [Parameter(Mandatory)][hashtable]$Case,
        [Parameter(Mandatory)][ValidateSet('Set', 'Restore')][string]$Phase
    )
    if ($Phase -eq 'Set') { return ([string]$Case.Set -eq 'enabled') }
    switch ([string]$Case.Restore) {
        'enable' { return $true }
        'disable' { return $false }
        'undo' {
            $Initial = if ($Case.ContainsKey('Initial')) { [string]$Case.Initial } else { 'enabled' }
            return ($Initial -eq 'enabled')
        }
        default { throw "Przypadek '$($Case.Name)': nieznane 'Restore' = '$($Case.Restore)'." }
    }
}

function ConvertFrom-VmUtcText {
    # Czas ISO 8601 z VM (probe.log, BootUtc, StartUtc) -> DateTime UTC; tekst nie do odczytania = $null (linia nie liczy się jako start).
    param([AllowNull()][AllowEmptyString()][string]$Text)
    $Parsed = [datetime]::MinValue
    if ([string]::IsNullOrWhiteSpace($Text)) { return $null }
    if (-not [datetime]::TryParse($Text, [System.Globalization.CultureInfo]::InvariantCulture, [System.Globalization.DateTimeStyles]::RoundtripKind, [ref]$Parsed)) { return $null }
    return $Parsed.ToUniversalTime()
}

function Get-VmAutostartObservation {
    # Jeden odczyt w VM (tylko odczyt): czas rozruchu (UTC), linie C:\dsotest\probe.log (otwarcie z FileShare.ReadWrite, bo sondy
    # dopisują) i procesy o podanych nazwach z kontem i czasem startu (UTC). Błąd odczytu probe.log = LogError, nie wyjątek.
    [CmdletBinding()]
    param(
        [Parameter(Mandatory)][AllowNull()][System.Management.Automation.Runspaces.PSSession]$Session,
        [AllowEmptyCollection()][string[]]$ProcessNames = @()
    )
    return Invoke-Command -Session $Session -ArgumentList @(, [string[]]@($ProcessNames)) -ScriptBlock {
        param([string[]]$ProcessNames)
        $ErrorActionPreference = 'Stop'
        $Boot = (Get-CimInstance -ClassName Win32_OperatingSystem).LastBootUpTime.ToUniversalTime().ToString('o')
        $Lines = @()
        $LogError = $null
        $Path = 'C:\dsotest\probe.log'
        if ([System.IO.File]::Exists($Path)) {
            # Sonda dopisuje z wyłącznym dostępem (kilka startuje naraz), więc zajęty plik to chwila - ponowienie zamiast fałszywego "brak startu".
            for ($Attempt = 1; $Attempt -le 50; $Attempt++) {
                try {
                    $Stream = [System.IO.File]::Open($Path, [System.IO.FileMode]::Open, [System.IO.FileAccess]::Read, [System.IO.FileShare]'ReadWrite, Delete')
                    try {
                        $Reader = New-Object System.IO.StreamReader($Stream)
                        $Lines = @($Reader.ReadToEnd() -split "`r?`n" | Where-Object { $_ })
                    }
                    finally { $Stream.Dispose() }
                    $LogError = $null
                    break
                }
                catch {
                    $LogError = $_.Exception.Message
                    Start-Sleep -Milliseconds 100
                }
            }
        }
        $Processes = @()
        foreach ($Name in @($ProcessNames)) {
            foreach ($Process in @(Get-Process -Name $Name -IncludeUserName -ErrorAction SilentlyContinue)) {
                $Start = $null
                try { $Start = $Process.StartTime.ToUniversalTime().ToString('o') } catch { $Start = $null }
                $Processes += [pscustomobject]@{ Name = [string]$Process.ProcessName; UserName = [string]$Process.UserName; StartUtc = $Start; Id = [int]$Process.Id }
            }
        }
        [pscustomobject]@{ BootUtc = $Boot; Lines = $Lines; LogError = $LogError; Processes = $Processes }
    }
}

function Get-VmProbeStarts {
    # Sondy z probe.log (linia: <nazwa>|<czas UTC ISO>|<konto>|<sesja>) zapisane PO rozruchu z obserwacji: nazwa -> pierwsza taka linia.
    param([Parameter(Mandatory)]$Observation)
    $Boot = ConvertFrom-VmUtcText -Text ([string]$Observation.BootUtc)
    if ($null -eq $Boot) { throw "Odczyt VM bez czasu rozruchu (BootUtc '$($Observation.BootUtc)') - start sond nie może być oceniony." }
    $Starts = @{}
    foreach ($Line in @($Observation.Lines)) {
        $Parts = ([string]$Line).Split('|')
        if ($Parts.Count -lt 2) { continue }
        $At = ConvertFrom-VmUtcText -Text $Parts[1]
        if ($null -eq $At -or $At -le $Boot) { continue }
        $Name = $Parts[0].Trim()
        if (-not $Starts.ContainsKey($Name)) { $Starts[$Name] = [string]$Line }
    }
    return $Starts
}

function Get-VmAutostartProcessNames {
    # Nazwy procesów do odczytu: pozycje Process oraz każda sonda (Probe pozycji i Controls) - sonda śpi godzinę, więc jej proces musi być widoczny.
    param([AllowEmptyCollection()][object[]]$Items = @(), [AllowEmptyCollection()][string[]]$Controls = @())
    $Names = [System.Collections.Generic.List[string]]::new()
    foreach ($Item in @($Items)) {
        if ($Item.ContainsKey('Process')) { $Names.Add([string]$Item.Process) }
        if ($Item.ContainsKey('Probe')) { $Names.Add([string]$Item.Probe) }
    }
    foreach ($Control in @($Controls)) { $Names.Add([string]$Control) }
    return [string[]]@($Names | Select-Object -Unique)
}

function Measure-VmAutostartObservation {
    # Ocena jednego odczytu (bez VM): start sond (linia probe.log po rozruchu), start procesów konta UserName po rozruchu i rozbieżności:
    # sonda z linią po rozruchu bez procesu albo z procesem bez linii (np. sonda nie zdołała dopisać linii) to błąd, nie "nie wystartowała".
    param([Parameter(Mandatory)]$Observation, [AllowEmptyCollection()][object[]]$Items = @(), [AllowEmptyCollection()][string[]]$Controls = @(), [string]$UserName = 'dso')
    $Starts = Get-VmProbeStarts -Observation $Observation
    $Boot = ConvertFrom-VmUtcText -Text ([string]$Observation.BootUtc)
    $FindProcesses = {
        param([string]$Name)
        @($Observation.Processes | Where-Object {
                $null -ne $_ -and [string]$_.Name -ieq $Name -and [string]$_.UserName -like "*\$UserName" -and
                $null -ne (ConvertFrom-VmUtcText -Text ([string]$_.StartUtc)) -and (ConvertFrom-VmUtcText -Text ([string]$_.StartUtc)) -gt $Boot
            })
    }
    $Disagreements = [System.Collections.Generic.List[string]]::new()
    $ProbeNames = @(@($Items | Where-Object { $_.ContainsKey('Probe') } | ForEach-Object { [string]$_.Probe }) + @($Controls) | Select-Object -Unique)
    foreach ($Name in $ProbeNames) {
        $Logged = $Starts.ContainsKey($Name)
        $Running = @(& $FindProcesses $Name).Count -gt 0
        if ($Logged -and -not $Running) { $Disagreements.Add("sonda '$Name' ma linię w probe.log po rozruchu ($($Starts[$Name])), ale nie ma jej procesu konta $UserName") }
        if ($Running -and -not $Logged) { $Disagreements.Add("sonda '$Name' działa jako proces konta $UserName po rozruchu, ale nie ma jej linii w probe.log") }
    }
    $Results = foreach ($Item in @($Items)) {
        if ($Item.ContainsKey('Probe')) {
            $Started = $Starts.ContainsKey([string]$Item.Probe)
            $Evidence = if ($Started) { $Starts[[string]$Item.Probe] } else { "brak linii '$($Item.Probe)' w probe.log i procesu po rozruchu" }
        }
        else {
            $Matching = @(& $FindProcesses ([string]$Item.Process))
            $Started = $Matching.Count -gt 0
            $All = @($Observation.Processes | Where-Object { $null -ne $_ -and [string]$_.Name -ieq [string]$Item.Process } | ForEach-Object { "$($_.Name) $($_.UserName) $($_.StartUtc)" })
            $Evidence = if ($Started) { "proces $($Matching[0].Name) (PID $($Matching[0].Id)) konta $($Matching[0].UserName) od $($Matching[0].StartUtc)" } else { "brak procesu '$($Item.Process)' konta $UserName po rozruchu (procesy o tej nazwie: $(if ($All) { $All -join '; ' } else { 'brak' }))" }
        }
        [pscustomobject]@{ Id = [string]$Item.Id; Probe = $(if ($Item.ContainsKey('Probe')) { [string]$Item.Probe } else { $null }); Process = $(if ($Item.ContainsKey('Process')) { [string]$Item.Process } else { $null }); Started = [bool]$Started; Evidence = $Evidence }
    }
    return [pscustomobject]@{
        BootUtc         = [string]$Observation.BootUtc
        Starts          = $Starts
        ControlsStarted = @($Controls | Where-Object { $Starts.ContainsKey($_) })
        ProbeLines      = @($Starts.Values | Sort-Object)
        Items           = @($Results)
        Disagreements   = $Disagreements.ToArray()
    }
}

function Read-VmAutostartState {
    # Odczyt do oceny startu: błąd odczytu probe.log albo rozbieżność probe.log z listą procesów -> jedno ponowienie po RetrySeconds
    # (sonda mogła być w trakcie dopisywania), potem wyjątek. Nigdy nie zamienia błędu odczytu w "pozycja nie wystartowała".
    param($Session, [object[]]$Items, [string[]]$Controls, [string]$UserName, [int]$RetrySeconds, [string]$When)
    $ProcessNames = Get-VmAutostartProcessNames -Items $Items -Controls $Controls
    $Problem = $null
    for ($Attempt = 1; $Attempt -le 2; $Attempt++) {
        if ($Attempt -gt 1) { Start-Sleep -Seconds $RetrySeconds }
        $Observation = Get-VmAutostartObservation -Session $Session -ProcessNames $ProcessNames
        if ($Observation.LogError) { $Problem = "nie udało się odczytać C:\dsotest\probe.log ($When): $($Observation.LogError)"; continue }
        $Measured = Measure-VmAutostartObservation -Observation $Observation -Items $Items -Controls $Controls -UserName $UserName
        if (@($Measured.Disagreements).Count -eq 0) { return $Measured }
        $Problem = "rozbieżność probe.log i listy procesów ($When): $(@($Measured.Disagreements) -join '; ')"
    }
    throw "Ocena startu niemożliwa - $Problem."
}

function Wait-VmAutostartStart {
    # Po restarcie: czeka (najwyżej TimeoutSeconds, co PollSeconds), aż każda sonda z Controls zapisze w probe.log linię z czasem po
    # rozruchu, potem SettleSeconds zapasu i świeży odczyt; ocenia start każdej pozycji Items (Probe = linia po rozruchu zgodna z procesem,
    # Process = proces konta UserName ze StartTime po rozruchu). Kontrola bez startu po limicie = wyjątek (bez niej brak startu pozycji nic
    # nie znaczy). Ponowna ocena po pomiarze Windows: Confirm-VmAutostartStart.
    [CmdletBinding()]
    param(
        [Parameter(Mandatory)][AllowNull()][System.Management.Automation.Runspaces.PSSession]$Session,
        [Parameter(Mandatory)][AllowEmptyCollection()][string[]]$Controls,
        [Parameter(Mandatory)][AllowEmptyCollection()][object[]]$Items,
        [ValidateRange(1, 3600)][int]$TimeoutSeconds = $script:AutostartStartTimeoutSeconds,
        [ValidateRange(0, 600)][int]$SettleSeconds = $script:AutostartSettleSeconds,
        [ValidateRange(1, 60)][int]$PollSeconds = $script:AutostartPollSeconds,
        [ValidatePattern('^[A-Za-z0-9_-]+\z')][string]$UserName = $script:AutostartUserName
    )
    $ProcessNames = Get-VmAutostartProcessNames -Items $Items -Controls $Controls
    $MaxPolls = [int][Math]::Ceiling($TimeoutSeconds / $PollSeconds)
    $Poll = 0
    while ($true) {
        $Observation = Get-VmAutostartObservation -Session $Session -ProcessNames $ProcessNames
        $Starts = Get-VmProbeStarts -Observation $Observation
        $Missing = @($Controls | Where-Object { -not $Starts.ContainsKey($_) })
        if ($Missing.Count -eq 0) { break }
        if ($Poll -ge $MaxPolls) {
            $Seen = @($Starts.Keys | Sort-Object) -join ', '
            $LogNote = if ($Observation.LogError) { " Błąd odczytu probe.log: $($Observation.LogError)." } else { '' }
            $Running = @($Observation.Processes | Where-Object { $null -ne $_ -and $Missing -contains [string]$_.Name } | ForEach-Object { "$($_.Name) $($_.UserName) $($_.StartUtc)" })
            $ProcessNote = if ($Running) { " Procesy brakujących sond: $($Running -join '; ')." } else { '' }
            throw "Sondy kontrolne nie wystartowały w ciągu $TimeoutSeconds s po rozruchu $($Observation.BootUtc): $($Missing -join ', ') (sondy po rozruchu: $(if ($Seen) { $Seen } else { 'brak' })).$LogNote$ProcessNote"
        }
        $Poll++
        Start-Sleep -Seconds $PollSeconds
    }
    if ($SettleSeconds -gt 0) { Start-Sleep -Seconds $SettleSeconds }
    $Measured = Read-VmAutostartState -Session $Session -Items $Items -Controls $Controls -UserName $UserName -RetrySeconds $PollSeconds -When "po zapasie $SettleSeconds s"
    return [pscustomobject]@{
        BootUtc         = $Measured.BootUtc
        ControlsStarted = @($Measured.ControlsStarted)
        ProbeLines      = @($Measured.ProbeLines)
        Items           = @($Measured.Items)
        Polls           = $Poll
        RecheckedAtUtc  = $null
    }
}

function Confirm-VmAutostartStart {
    # Drugi odczyt tego samego rozruchu (po Wait-VmStartupInfo, ok. 96 s po rozruchu): pozycja, która nie wystartowała w pierwszym odczycie,
    # a wystartowała później, ma Late = $true (późny start = porażka scenariusza). Started = start w którymkolwiek odczycie. Inny rozruch = wyjątek.
    [CmdletBinding()]
    param(
        [Parameter(Mandatory)][AllowNull()][System.Management.Automation.Runspaces.PSSession]$Session,
        [Parameter(Mandatory)]$Observation,
        [Parameter(Mandatory)][AllowEmptyCollection()][string[]]$Controls,
        [Parameter(Mandatory)][AllowEmptyCollection()][object[]]$Items,
        [ValidateRange(1, 60)][int]$PollSeconds = $script:AutostartPollSeconds,
        [ValidatePattern('^[A-Za-z0-9_-]+\z')][string]$UserName = $script:AutostartUserName
    )
    $Measured = Read-VmAutostartState -Session $Session -Items $Items -Controls $Controls -UserName $UserName -RetrySeconds $PollSeconds -When 'po pomiarze Windows'
    if ([string]$Measured.BootUtc -ne [string]$Observation.BootUtc) {
        throw "Ponowny odczyt startu dotyczy innego rozruchu ($($Measured.BootUtc) zamiast $($Observation.BootUtc)) - VM zrestartowała się bez polecenia."
    }
    $Merged = foreach ($Item in @($Items)) {
        $First = @($Observation.Items | Where-Object { [string]$_.Id -ceq [string]$Item.Id })
        $Again = @($Measured.Items | Where-Object { [string]$_.Id -ceq [string]$Item.Id })
        if ($First.Count -ne 1 -or $Again.Count -ne 1) { throw "Brak oceny startu pozycji '$($Item.Id)' w jednym z odczytów." }
        $AtSettle = [bool]$First[0].Started
        $AtRecheck = [bool]$Again[0].Started
        [pscustomobject]@{
            Id = [string]$Item.Id; Probe = $First[0].Probe; Process = $First[0].Process
            Started = ($AtSettle -or $AtRecheck); Late = ((-not $AtSettle) -and $AtRecheck)
            StartedAtSettle = $AtSettle; StartedAtRecheck = $AtRecheck
            Evidence = "$($First[0].Evidence) | po pomiarze Windows: $($Again[0].Evidence)"
        }
    }
    return [pscustomobject]@{
        BootUtc         = [string]$Observation.BootUtc
        ControlsStarted = @($Observation.ControlsStarted)
        ProbeLines      = @($Measured.ProbeLines)
        Items           = @($Merged)
        Polls           = $Observation.Polls
        RecheckedAtUtc  = [DateTime]::UtcNow.ToString('o')
    }
}

function Get-VmStartupInfoState {
    # Odczyt w VM (tylko odczyt): SID konta, czas rozruchu (UTC) i pliki <SID>_StartupInfo*.xml z czasem zapisu (UTC).
    [CmdletBinding()]
    param(
        [Parameter(Mandatory)][AllowNull()][System.Management.Automation.Runspaces.PSSession]$Session,
        [ValidatePattern('^[A-Za-z0-9_-]+\z')][string]$UserName = $script:AutostartUserName
    )
    return Invoke-Command -Session $Session -ArgumentList $UserName -ScriptBlock {
        param([string]$UserName)
        $ErrorActionPreference = 'Stop'
        $Sid = (Get-LocalUser -Name $UserName).SID.Value
        $Boot = (Get-CimInstance -ClassName Win32_OperatingSystem).LastBootUpTime.ToUniversalTime().ToString('o')
        $Directory = Join-Path $env:SystemRoot 'System32\wdi\LogFiles\StartupInfo'
        $Files = @()
        if ([System.IO.Directory]::Exists($Directory)) {
            $Files = @(Get-ChildItem -LiteralPath $Directory -Filter "$($Sid)_StartupInfo*.xml" -File | ForEach-Object { [pscustomobject]@{ Name = $_.Name; WriteUtc = $_.LastWriteTimeUtc.ToString('o') } })
        }
        [pscustomobject]@{ Sid = $Sid; BootUtc = $Boot; Files = $Files }
    }
}

function Wait-VmStartupInfo {
    # Czeka (najwyżej TimeoutSeconds), aż Windows zapisze pomiar startu konta: plik <SID>_StartupInfo*.xml z czasem zapisu po rozruchu.
    # Bez niego lista po restarcie nie ma pomiaru tego rozruchu (Expect.MeasuredAfter). Brak po limicie = wyjątek.
    [CmdletBinding()]
    param(
        [Parameter(Mandatory)][AllowNull()][System.Management.Automation.Runspaces.PSSession]$Session,
        [ValidateRange(1, 3600)][int]$TimeoutSeconds = $script:AutostartStartTimeoutSeconds,
        [ValidateRange(1, 60)][int]$PollSeconds = $script:AutostartPollSeconds,
        [ValidatePattern('^[A-Za-z0-9_-]+\z')][string]$UserName = $script:AutostartUserName
    )
    $MaxPolls = [int][Math]::Ceiling($TimeoutSeconds / $PollSeconds)
    $Poll = 0
    while ($true) {
        $State = Get-VmStartupInfoState -Session $Session -UserName $UserName
        $Boot = ConvertFrom-VmUtcText -Text ([string]$State.BootUtc)
        if ($null -eq $Boot) { throw "Odczyt VM bez czasu rozruchu (BootUtc '$($State.BootUtc)') - pliku StartupInfo nie da się ocenić." }
        $Fresh = @($State.Files | Where-Object { $null -ne $_ -and $null -ne (ConvertFrom-VmUtcText -Text ([string]$_.WriteUtc)) -and (ConvertFrom-VmUtcText -Text ([string]$_.WriteUtc)) -gt $Boot } |
                Sort-Object { ConvertFrom-VmUtcText -Text ([string]$_.WriteUtc) })
        if ($Fresh.Count -gt 0) {
            $Newest = $Fresh[$Fresh.Count - 1]
            return [pscustomobject]@{ Sid = [string]$State.Sid; BootUtc = [string]$State.BootUtc; File = [string]$Newest.Name; WriteUtc = [string]$Newest.WriteUtc; Polls = $Poll }
        }
        if ($Poll -ge $MaxPolls) {
            $Names = @($State.Files | Where-Object { $null -ne $_ } | ForEach-Object { "$($_.Name) $($_.WriteUtc)" }) -join '; '
            throw "Brak pliku $($State.Sid)_StartupInfo*.xml zapisanego po rozruchu $($State.BootUtc) w ciągu $TimeoutSeconds s (pliki: $(if ($Names) { $Names } else { 'brak' }))."
        }
        $Poll++
        Start-Sleep -Seconds $PollSeconds
    }
}

function Invoke-VmAutostartCase {
    # Przebieg Flow Autostart (opis w nagłówku modułu). Sesja po restarcie (ostatnia) zostaje otwarta: należy do wywołującego.
    [CmdletBinding()]
    param(
        [Parameter(Mandatory)][AllowNull()][System.Management.Automation.Runspaces.PSSession]$Session,
        [Parameter(Mandatory)][hashtable]$Case,
        [Parameter(Mandatory)][hashtable]$Layout,
        [scriptblock]$Reconnect
    )
    $Steps = [System.Collections.Generic.List[object]]::new()
    $Reboots = [System.Collections.Generic.List[object]]::new()
    $RunAsUser = if ($Case.ContainsKey('RunAs')) { [string]$Case.RunAs } else { '' }
    $Expect = $Case.Expect
    $HasSet = $Case.ContainsKey('Set')
    $Items = @(if ($HasSet) { $Case.Items | Where-Object { $null -ne $_ } })
    [string[]]$Controls = @(if ($HasSet) { $Case.Controls | Where-Object { $null -ne $_ } })
    [string[]]$Setup = @()
    if ($Case.ContainsKey('Setup')) { $Setup = [string[]]@($Case.Setup) }
    [string[]]$WatchedKeys = @()
    if ($Expect.ContainsKey('ListDoesNotWrite')) { $WatchedKeys = [string[]]@($Expect.ListDoesNotWrite) }
    $Run = {
        param([string]$Step, [string]$Command, [string]$ItemId, [string]$State)
        $OutPath = '{0}\{1}-{2}.json' -f $Layout.Out, $Case.Name, $Step
        $CliArgs = @{ Command = $Command; OutPath = $OutPath }
        if ($ItemId) { $CliArgs.TweakId = $ItemId }
        if ($State) { $CliArgs.AutostartState = $State }
        $Arguments = ConvertTo-DsoCliArguments @CliArgs
        $Result = Invoke-DsoCli -Session $Session -ExePath $Layout.Exe -Arguments $Arguments -OutPath $OutPath -RunAsUser $RunAsUser
        $Steps.Add([pscustomobject]@{ Step = $Step; ItemId = $(if ($ItemId) { $ItemId } else { $null }); ExitCode = $Result.ExitCode; TimedOut = $Result.TimedOut; Report = $Result.Report; Json = $Result.Json; ReportError = $Result.ReportError })
    }
    $SetupOutput = @()
    $KeysBefore = $null
    $KeysAfter = $null
    $StartAfterSet = $null
    $StartupInfoAfterSet = $null
    $CheckAfterUndoResult = $null
    $StartAfterRestore = $null
    $StartupInfo = $null
    $CheckResult = $null
    $CleanupError = $null
    try {
        # Sondy budują się w VM (csc.exe), więc do VM trafia tylko ich źródło i skrypt instalacji.
        Copy-VmFixtures -Session $Session -Destination (Join-Path $Layout.Root 'fixtures')
        # Invoke-VmSetup -PassThru zwraca jedną tablicę (return ,): bez @() wokół, inaczej rekord ma tablicę w tablicy.
        $SetupOutput = Invoke-VmSetup -Session $Session -Setup $Setup -PassThru
        if ($WatchedKeys.Count -gt 0) { $KeysBefore = @(Get-VmKeyExistence -Session $Session -Keys $WatchedKeys) }
        & $Run 'list-before' 'list-autostart'
        if ($WatchedKeys.Count -gt 0) { $KeysAfter = @(Get-VmKeyExistence -Session $Session -Keys $WatchedKeys) }
        if ($HasSet) {
            $Index = 0
            foreach ($Item in $Items) { $Index++; & $Run "set-$Index" 'set-autostart' ([string]$Item.Id) ([string]$Case.Set) }
            $Restart = Restart-VmAndReconnect -Session $Session -Reconnect $Reconnect -Step 'reboot-after-set'
            $Session = $Restart.Session
            $Reboots.Add($Restart.Record)
            # Pierwsza ocena ok. 20 s po sondach kontrolnych, druga po pomiarze Windows (ok. 96 s po rozruchu): późny start = porażka.
            $FirstLook = Wait-VmAutostartStart -Session $Session -Controls $Controls -Items $Items
            $StartupInfoAfterSet = Wait-VmStartupInfo -Session $Session
            $StartAfterSet = Confirm-VmAutostartStart -Session $Session -Observation $FirstLook -Controls $Controls -Items $Items
            $Index = 0
            foreach ($Item in $Items) {
                $Index++
                switch ([string]$Case.Restore) {
                    'undo' { & $Run "restore-$Index" 'undo-autostart' ([string]$Item.Id) }
                    'enable' { & $Run "restore-$Index" 'set-autostart' ([string]$Item.Id) 'enabled' }
                    'disable' { & $Run "restore-$Index" 'set-autostart' ([string]$Item.Id) 'disabled' }
                }
            }
            # CheckAfterUndo: stan zaraz po przywróceniu, przed restartem (np. klucz, który Windows po logowaniu odtworzyłby sam).
            if ($Case.ContainsKey('CheckAfterUndo')) { $CheckAfterUndoResult = Invoke-VmCheck -Session $Session -Script $Case.CheckAfterUndo }
            $Restart = Restart-VmAndReconnect -Session $Session -Reconnect $Reconnect -Step 'reboot-after-restore'
            $Session = $Restart.Session
            $Reboots.Add($Restart.Record)
            $FirstLook = Wait-VmAutostartStart -Session $Session -Controls $Controls -Items $Items
            $StartupInfo = Wait-VmStartupInfo -Session $Session
            $StartAfterRestore = Confirm-VmAutostartStart -Session $Session -Observation $FirstLook -Controls $Controls -Items $Items
            & $Run 'list-after' 'list-autostart'
        }
        $CheckResult = Invoke-VmCheck -Session $Session -Script $Case.Check
    }
    finally {
        # Sprzątanie zawsze (także po błędzie restartu albo czekania); własny błąd sprzątania nie zasłania pierwotnego wyjątku.
        if ($Case.ContainsKey('Cleanup')) {
            try {
                $CleanupBlock = [scriptblock]::Create("`$ErrorActionPreference = 'Stop'; $($Case.Cleanup)")
                Invoke-Command -Session $Session -ScriptBlock $CleanupBlock | Out-Null
            }
            catch {
                $CleanupError = $_.Exception.Message
                Write-Warning "Sprzątanie przypadku '$($Case.Name)' nie powiodło się: $CleanupError"
            }
        }
    }
    return [pscustomobject]@{
        Name              = $Case.Name
        Flow              = 'Autostart'
        RunAs             = $RunAsUser
        Hypothesis        = if ($Case.ContainsKey('Hypothesis')) { $Case.Hypothesis } else { $null }
        SetupOutput       = [string[]]@($SetupOutput | Where-Object { $null -ne $_ })
        Steps             = $Steps.ToArray()
        KeysBeforeList    = $KeysBefore
        KeysAfterList     = $KeysAfter
        Reboots           = $Reboots.ToArray()
        StartAfterSet     = $StartAfterSet
        StartupInfoAfterSet = $StartupInfoAfterSet
        CheckAfterUndoResult = $CheckAfterUndoResult
        StartAfterRestore = $StartAfterRestore
        StartupInfo       = $StartupInfo
        CheckResult       = $CheckResult
        CleanupError      = $CleanupError
    }
}

function Invoke-VmCase {
    [CmdletBinding()]
    param(
        [Parameter(Mandatory)][AllowNull()][System.Management.Automation.Runspaces.PSSession]$Session,
        [Parameter(Mandatory)][hashtable]$Case,
        [Parameter(Mandatory)][hashtable]$Layout,
        [scriptblock]$Reconnect
    )
    if ($Case.Flow -eq 'Reboot' -and $null -eq $Reconnect) {
        throw "Przypadek '$($Case.Name)': Flow 'Reboot' wymaga parametru -Reconnect (scriptblock zwracający nową sesję po restarcie)."
    }
    if ($Case.Flow -eq 'Autostart') {
        if ($Case.ContainsKey('Set') -and $null -eq $Reconnect) {
            throw "Przypadek '$($Case.Name)': Flow 'Autostart' z 'Set' wymaga parametru -Reconnect (scriptblock zwracający nową sesję po restarcie)."
        }
        return Invoke-VmAutostartCase -Session $Session -Case $Case -Layout $Layout -Reconnect $Reconnect
    }
    $Steps = [System.Collections.Generic.List[object]]::new()
    $UseBuiltInCatalog = $Case.ContainsKey('Catalog') -and $Case.Catalog -eq 'BuiltIn'
    $RunAsUser = if ($Case.ContainsKey('RunAs')) { [string]$Case.RunAs } else { '' }
    $Run = {
        param([string]$Step, [string]$Command)
        $OutPath = '{0}\{1}-{2}.json' -f $Layout.Out, $Case.Name, $Step
        if ($Command -in @('list-apps', 'remove-app')) {
            # Polecenia aplikacji: bez katalogu tweaków; id tylko przy remove-app.
            $CliArgs = @{ Command = $Command; OutPath = $OutPath }
            if ($Command -eq 'remove-app') { $CliArgs.TweakId = $Case.AppId; $CliArgs.RestorePoint = $Case.RestorePoint }
        }
        elseif ($Command -in @('install-apps', 'list-install')) {
            # Polecenia instalacji (Etap 4c): bez katalogu tweaków; AppIds tylko przy install-apps, lista testowa przez --apps-catalog.
            $CliArgs = @{ Command = $Command; OutPath = $OutPath }
            if ($Command -eq 'install-apps') { $CliArgs.AppIds = [string[]]@($Case.AppIds) }
            if ($Case.ContainsKey('AppsCatalog')) { $CliArgs.AppsCatalog = [string]$Case.AppsCatalog }
        }
        else {
            $CatalogForCli = if ($UseBuiltInCatalog) { $null } else { $Layout.Catalog }
            $CliArgs = @{ Command = $Command; TweakId = $Case.TweakId; CatalogPath = $CatalogForCli; OutPath = $OutPath }
            if ($Command -eq 'apply') { $CliArgs.RestorePoint = $Case.RestorePoint }
        }
        $Arguments = ConvertTo-DsoCliArguments @CliArgs
        if ($Case.ContainsKey('Language')) { $Arguments = @($Arguments) + @('--lang', [string]$Case.Language) }
        $CliParams = @{ Session = $Session; ExePath = $Layout.Exe; Arguments = $Arguments; OutPath = $OutPath; RunAsUser = $RunAsUser }
        if ($Command -eq 'execute' -and $Case.ContainsKey('CliTimeoutSeconds')) { $CliParams.TimeoutSeconds = [int]$Case.CliTimeoutSeconds }
        $Result = Invoke-DsoCli @CliParams
        $Steps.Add([pscustomobject]@{ Step = $Step; ExitCode = $Result.ExitCode; TimedOut = $Result.TimedOut; Report = $Result.Report; Json = $Result.Json; ReportError = $Result.ReportError })
    }

    [string[]]$Setup = @()
    if ($Case.ContainsKey('Setup')) { $Setup = [string[]]@($Case.Setup) }
    $Snapshot = if ($Case.ContainsKey('Snapshot')) { $Case.Snapshot } else { @{ Registry = @(); Services = @() } }
    $Expect = if ($Case.ContainsKey('Expect')) { $Case.Expect } else { @{} }
    $CountRestorePoints = $Expect.ContainsKey('RestorePointCreated')

    if ($Case.Flow -eq 'AppInstall') {
        $CheckResult = $null
        $CleanupError = $null
        try {
            # Lista testowa (vm-test-apps.json) i źródła MSI trafiają do VM razem z innymi fixtures.
            Copy-VmFixtures -Session $Session -Destination (Join-Path $Layout.Root 'fixtures')
            Invoke-VmSetup -Session $Session -Setup $Setup
            & $Run $Case.Command $Case.Command
            $CheckResult = Invoke-VmCheck -Session $Session -Script $Case.Check
        }
        finally {
            if ($Case.ContainsKey('Cleanup')) {
                try {
                    $CleanupBlock = [scriptblock]::Create("`$ErrorActionPreference = 'Stop'; $($Case.Cleanup)")
                    Invoke-Command -Session $Session -ScriptBlock $CleanupBlock | Out-Null
                }
                catch {
                    $CleanupError = $_.Exception.Message
                    Write-Warning "Sprzątanie przypadku '$($Case.Name)' nie powiodło się: $CleanupError"
                }
            }
        }
        return [pscustomobject]@{
            Name         = $Case.Name
            Command      = $Case.Command
            AppIds       = if ($Case.ContainsKey('AppIds')) { @($Case.AppIds) } else { @() }
            RunAs        = $RunAsUser
            Hypothesis   = if ($Case.ContainsKey('Hypothesis')) { $Case.Hypothesis } else { $null }
            Steps        = $Steps.ToArray()
            CheckResult  = $CheckResult
            CleanupError = $CleanupError
        }
    }

    if ($Case.Flow -eq 'AppRemove') {
        $CheckResult = $null
        $CleanupError = $null
        try {
            # Pakiety testowe budują się w VM (csc.exe, COM WindowsInstaller), więc do VM trafia tylko ich źródło.
            Copy-VmFixtures -Session $Session -Destination (Join-Path $Layout.Root 'fixtures')
            Invoke-VmSetup -Session $Session -Setup $Setup
            & $Run 'list-before' 'list-apps'
            & $Run 'remove-app' 'remove-app'
            & $Run 'list-after' 'list-apps'
            $CheckResult = Invoke-VmCheck -Session $Session -Script $Case.Check
        }
        finally {
            if ($Case.ContainsKey('Cleanup')) {
                try {
                    $CleanupBlock = [scriptblock]::Create("`$ErrorActionPreference = 'Stop'; $($Case.Cleanup)")
                    Invoke-Command -Session $Session -ScriptBlock $CleanupBlock | Out-Null
                }
                catch {
                    $CleanupError = $_.Exception.Message
                    Write-Warning "Sprzątanie przypadku '$($Case.Name)' nie powiodło się: $CleanupError"
                }
            }
        }
        return [pscustomobject]@{
            Name         = $Case.Name
            AppId        = $Case.AppId
            RunAs        = $RunAsUser
            Hypothesis   = if ($Case.ContainsKey('Hypothesis')) { $Case.Hypothesis } else { $null }
            Steps        = $Steps.ToArray()
            CheckResult  = $CheckResult
            CleanupError = $CleanupError
        }
    }

    if ($Case.Flow -eq 'Execute') {
        $CheckResult = $null
        $CleanupError = $null
        $SnapshotBefore = $null
        try {
            # Setup w try/finally: błąd w połowie Setup też uruchamia Cleanup (inaczej stan zostałby dla kolejnych przypadków).
            Invoke-VmSetup -Session $Session -Setup $Setup
            $SnapshotBefore = Get-VmStateSnapshot -Session $Session -Snapshot $Snapshot
            & $Run 'execute' 'execute'
            if ($Case.ContainsKey('Check')) { $CheckResult = Invoke-VmCheck -Session $Session -Script $Case.Check }
        }
        finally {
            # Sprzątanie zawsze, także po błędzie kroku lub Check; własny błąd sprzątania nie zasłania pierwotnego wyjątku.
            if ($Case.ContainsKey('Cleanup')) {
                try {
                    $CleanupBlock = [scriptblock]::Create("`$ErrorActionPreference = 'Stop'; $($Case.Cleanup)")
                    Invoke-Command -Session $Session -ScriptBlock $CleanupBlock | Out-Null
                }
                catch {
                    $CleanupError = $_.Exception.Message
                    Write-Warning "Sprzątanie przypadku '$($Case.Name)' nie powiodło się: $CleanupError"
                }
            }
        }
        $SnapshotAfterExecute = $null
        $ExecuteDiff = @()
        if ($Case.ContainsKey('Snapshot')) {
            $SnapshotAfterExecute = Get-VmStateSnapshot -Session $Session -Snapshot $Snapshot
            $ExecuteDiff = Compare-VmSnapshot -Before $SnapshotBefore -After $SnapshotAfterExecute
        }
        return [pscustomobject]@{
            Name           = $Case.Name
            TweakId        = $Case.TweakId
            Hypothesis     = if ($Case.ContainsKey('Hypothesis')) { $Case.Hypothesis } else { $null }
            Steps          = $Steps.ToArray()
            SnapshotBefore = $SnapshotBefore
            SnapshotAfter  = $SnapshotAfterExecute
            SnapshotDiff   = $ExecuteDiff
            CheckResult    = $CheckResult
            CleanupError   = $CleanupError
        }
    }

    Invoke-VmSetup -Session $Session -Setup $Setup
    $SnapshotBefore = Get-VmStateSnapshot -Session $Session -Snapshot $Snapshot
    # NotWrittenTo = "stan bez zmian": odczyt przed apply, po apply i po undo (HKCU Administratora sesji PS Direct; wartości systemowe typu
    # MenuShowDelay istnieją tam domyślnie, więc samo Exists=false byłoby fałszywą czerwienią).
    $NotWrittenToBefore = $null
    if ($Expect.ContainsKey('NotWrittenTo')) {
        $NotWrittenToBefore = Get-VmStateSnapshot -Session $Session -Snapshot @{ Registry = @($Expect.NotWrittenTo); Services = @() } -AllowCurrentUser
    }
    $CountBefore = $null
    if ($CountRestorePoints) { $CountBefore = Get-VmRestorePointCount -Session $Session }

    & $Run 'detect-before' 'detect'
    if ($Case.Flow -eq 'DetectOnly') {
        # Tylko odczyt: stan sprzed (z niego powstają wartości domyślne do docs) i kontrola, że detect niczego nie zmienił.
        $SnapshotAfterDetect = Get-VmStateSnapshot -Session $Session -Snapshot $Snapshot
        return [pscustomobject]@{
            Name           = $Case.Name
            TweakId        = $Case.TweakId
            Hypothesis     = if ($Case.ContainsKey('Hypothesis')) { $Case.Hypothesis } else { $null }
            Steps          = $Steps.ToArray()
            SnapshotBefore = $SnapshotBefore
            SnapshotAfter  = $SnapshotAfterDetect
            SnapshotDiff   = Compare-VmSnapshot -Before $SnapshotBefore -After $SnapshotAfterDetect
        }
    }
    # Sesja po restarcie (ostatnia) zostaje otwarta: należy do wywołującego (Invoke-DsoVmRun zamyka ją i kopiuje przez nią logi).
    $Reboots = [System.Collections.Generic.List[object]]::new()
    & $Run 'apply' 'apply'
    $CountAfter = $null
    if ($CountRestorePoints) {
        $CountAfter = if ($Expect.RestorePointCreated) { Wait-VmRestorePointCount -Session $Session -CountBefore $CountBefore } else { Get-VmRestorePointCount -Session $Session }
    }
    $SnapshotAfterApply = Get-VmStateSnapshot -Session $Session -Snapshot $Snapshot

    $NotWrittenTo = $null
    if ($Expect.ContainsKey('NotWrittenTo')) {
        $NotWrittenTo = Get-VmStateSnapshot -Session $Session -Snapshot @{ Registry = @($Expect.NotWrittenTo); Services = @() } -AllowCurrentUser
    }

    & $Run 'detect-after-apply' 'detect'
    $CheckAfterApplyResult = $null
    $CheckAfterUndoResult = $null
    if ($Case.Flow -eq 'Reboot') {
        $Restart = Restart-VmAndReconnect -Session $Session -Reconnect $Reconnect -Step 'reboot-after-apply'
        $Session = $Restart.Session
        $Reboots.Add($Restart.Record)
        & $Run 'detect-after-reboot' 'detect'
    }
    # CheckAfterApply: po detekcji AfterApply (w Reboot po restarcie, razem z AfterReboot); wynik jest opisem, nie wyjątkiem.
    if ($Case.ContainsKey('CheckAfterApply')) { $CheckAfterApplyResult = Invoke-VmCheck -Session $Session -Script $Case.CheckAfterApply }
    if ($Case.Flow -in @('Full', 'Reboot')) {
        & $Run 'undo' 'undo'
        if ($Case.Flow -eq 'Reboot') {
            $Restart = Restart-VmAndReconnect -Session $Session -Reconnect $Reconnect -Step 'reboot-after-undo'
            $Session = $Restart.Session
            $Reboots.Add($Restart.Record)
        }
        & $Run 'detect-after-undo' 'detect'
        if ($Case.ContainsKey('CheckAfterUndo')) { $CheckAfterUndoResult = Invoke-VmCheck -Session $Session -Script $Case.CheckAfterUndo }
    }

    $NotWrittenToDiff = @()
    if ($Expect.ContainsKey('NotWrittenTo')) {
        $DiffApply = Compare-VmSnapshot -Before $NotWrittenToBefore -After $NotWrittenTo
        $NotWrittenToDiff = @($DiffApply | ForEach-Object { "po apply: $_" })
        if ($Case.Flow -in @('Full', 'Reboot')) {
            $NotWrittenToAfterUndo = Get-VmStateSnapshot -Session $Session -Snapshot @{ Registry = @($Expect.NotWrittenTo); Services = @() } -AllowCurrentUser
            $DiffUndo = Compare-VmSnapshot -Before $NotWrittenToBefore -After $NotWrittenToAfterUndo
            $NotWrittenToDiff += @($DiffUndo | ForEach-Object { "po undo: $_" })
        }
    }
    $KeysAfterUndo = $null
    if ($Expect.ContainsKey('KeyAbsentAfterUndo')) {
        $KeysAfterUndo = @(Get-VmKeyExistence -Session $Session -Keys ([string[]]@($Expect.KeyAbsentAfterUndo)))
    }
    $KeysPresentAfterUndo = $null
    if ($Expect.ContainsKey('KeyPresentAfterUndo')) {
        $KeysPresentAfterUndo = @(Get-VmKeyExistence -Session $Session -Keys ([string[]]@($Expect.KeyPresentAfterUndo)))
    }
    $SnapshotAfter = Get-VmStateSnapshot -Session $Session -Snapshot $Snapshot
    $SnapshotDiff = Compare-VmSnapshot -Before $SnapshotBefore -After $SnapshotAfter

    return [pscustomobject]@{
        Name                    = $Case.Name
        TweakId                 = $Case.TweakId
        Hypothesis              = if ($Case.ContainsKey('Hypothesis')) { $Case.Hypothesis } else { $null }
        Steps                   = $Steps.ToArray()
        SnapshotBefore          = $SnapshotBefore
        SnapshotAfterApply      = $SnapshotAfterApply
        SnapshotAfter           = $SnapshotAfter
        SnapshotDiff            = $SnapshotDiff
        NotWrittenTo            = $NotWrittenTo
        NotWrittenToDiff        = $NotWrittenToDiff
        RestorePointCountBefore = $CountBefore
        RestorePointCountAfter  = $CountAfter
        Reboots                 = $Reboots.ToArray()
        KeysAfterUndo           = $KeysAfterUndo
        KeysPresentAfterUndo    = $KeysPresentAfterUndo
        CheckAfterApplyResult   = $CheckAfterApplyResult
        CheckAfterUndoResult    = $CheckAfterUndoResult
    }
}

Export-ModuleMember -Function Import-VmCases, Split-VmCaseRuns, Expand-VmCasePlaceholders, ConvertTo-DsoCliArguments, Invoke-DsoCli, Get-DsoStateSnapshot, Get-VmStateSnapshot, Compare-VmSnapshot, Test-VmSnapshotMatches, Invoke-VmSetup, Get-VmRestorePointCount, Invoke-VmCase, Restart-VmAndReconnect, New-VmRestartMarker, Test-VmRestartMarker, Get-VmAutostartExpectedStart, Wait-VmAutostartStart, Confirm-VmAutostartStart, Wait-VmStartupInfo
