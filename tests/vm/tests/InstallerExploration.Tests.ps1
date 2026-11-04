BeforeAll {
    # Wyłącznik: żaden test na mockach nie może dotknąć prawdziwego Hyper-V (patrz Support\NoLiveVm.ps1).
    . (Join-Path $PSScriptRoot 'Support\NoLiveVm.ps1')
    Import-Module (Join-Path $PSScriptRoot '..\modules\VmCommon.psm1') -Force
    Import-Module (Join-Path $PSScriptRoot '..\modules\VmHost.psm1') -Force
    # Skrypt jest dot-source'owany: przy '.' nie uruchamia przebiegu na VM, tylko definiuje funkcje.
    . (Join-Path $PSScriptRoot '..\explore\Invoke-InstallerExploration.ps1')
    $script:InstallerScriptPath = Join-Path $PSScriptRoot '..\explore\Invoke-InstallerExploration.ps1'

    function script:New-FakeRelease {
        # Katalog wydania jak z Build-Release.ps1: zip, instalator i SHA256SUMS.txt z sumami obu plików.
        param(
            [Parameter(Mandatory)][string]$Dir,
            [string]$Version = '4.0.4',
            [ValidateSet('', 'zip', 'setup', 'sums')][string]$Omit = '',
            [ValidateSet('', 'zip', 'setup')][string]$BadSum = ''
        )
        $null = New-Item -ItemType Directory -Path $Dir -Force
        $Source = Join-Path $Dir "src-$Version\DSOptimizer"
        $null = New-Item -ItemType Directory -Path $Source -Force
        [System.IO.File]::WriteAllBytes((Join-Path $Source 'DSOptimizer.exe'), [byte[]](77, 90, 4, 0, 4))
        $ZipName = "DSOptimizer-$Version.zip"
        $SetupName = "DSOptimizer-$Version-setup.exe"
        $Zip = Join-Path $Dir $ZipName
        $Setup = Join-Path $Dir $SetupName
        Compress-Archive -Path $Source -DestinationPath $Zip
        [System.IO.File]::WriteAllBytes($Setup, [byte[]](77, 90, 9, 9, 4))
        Remove-Item -LiteralPath (Join-Path $Dir "src-$Version") -Recurse -Force
        $Lines = @()
        foreach ($Item in @(@{ Name = $ZipName; Path = $Zip; Kind = 'zip' }, @{ Name = $SetupName; Path = $Setup; Kind = 'setup' })) {
            $Sha = (Get-FileHash -LiteralPath $Item.Path -Algorithm SHA256).Hash.ToLowerInvariant()
            if ($BadSum -eq $Item.Kind) { $Sha = '0' * 64 }
            $Lines += "$Sha  $($Item.Name)"
        }
        $SumsPath = Join-Path $Dir 'SHA256SUMS.txt'
        if (Test-Path -LiteralPath $SumsPath) { $Lines = @([System.IO.File]::ReadAllLines($SumsPath)) + $Lines }
        [System.IO.File]::WriteAllText($SumsPath, (($Lines -join "`n") + "`n"))
        if ($Omit -eq 'zip') { Remove-Item -LiteralPath $Zip -Force }
        if ($Omit -eq 'setup') { Remove-Item -LiteralPath $Setup -Force }
        if ($Omit -eq 'sums') { Remove-Item -LiteralPath $SumsPath -Force }
        return [pscustomobject]@{ Dir = $Dir; Zip = $Zip; Setup = $Setup }
    }

    function script:New-SmokeData {
        return [ordered]@{
            exitCode = 0
            result   = [pscustomobject]@{ schema = 1; version = '4.0.4'; passed = $true; checks = @([pscustomobject]@{ name = 'install: kod 0'; passed = $true; detail = '' }) }
        }
    }

    function script:New-RunningData {
        return [ordered]@{
            install          = [ordered]@{ exitCode = 0 }
            windowA          = [ordered]@{ processFound = $true; mutex = $true; pid = 1200; seconds = 4 }
            portable         = [ordered]@{ message = 'DSOptimizer już działa (okno albo polecenie w trybie wiersza poleceń). Przełącz się na otwarte okno.'; clickedOk = $true; taskResult = 1; aAlive = $true }
            cliApply         = [ordered]@{ exitCode = 1; firstError = 'Działa już inna kopia DSOptimizera (okno albo polecenie, które zmienia system). Nic nie zostało zmienione.' }
            listApps         = [ordered]@{ exitCode = 0; group = 'Protected' }
            reinstall        = [ordered]@{ exitCode = 7; versionBefore = '4.0.4'; versionAfter = '4.0.4'; filesDiff = @() }
            uninstallRunning = [ordered]@{ exitCode = 1; filesDiff = @(); keyPresent = $true }
            relaunch         = [ordered]@{ aAliveBeforeC = $true; mutexBeforeC = $true; cDetected = $true; holdSeconds = 9; aAliveBeforeStop = $true; mutexBeforeStop = $true
                aStopped = $true; cAlive = $true; mutex = $true; refusalSeen = $false; probeError = $null }
            programData      = [ordered]@{ exists = $true; files = @('logs\dsoptimizer.log', 'settings.json'); owner = 'S-1-5-32-544' }
            final            = [ordered]@{ mutexReleased = $true; exitCode = 0; appDirGone = $true; keyGone = $true; startMenuGone = $true; desktopGone = $true
                dataBefore = @('logs\dsoptimizer.log', 'settings.json'); dataAfter = @('logs\dsoptimizer.log', 'settings.json'); ownerAfter = 'S-1-5-32-544' }
        }
    }

    function script:New-UpgradeData {
        return [ordered]@{
            releaseVersion = '4.0.4'
            upgradeVersion = '4.0.5'
            install        = [ordered]@{ exitCode = 0 }
            desktopRemoved = [ordered]@{ existed = $true; gone = $true }
            windowA        = [ordered]@{ processFound = $true; mutex = $true; pid = 1300; seconds = 3 }
            upgradeRunning = [ordered]@{ exitCode = 7; versionBefore = '4.0.4'; versionAfter = '4.0.4'; filesDiff = @() }
            mutexReleased  = $true
            upgrade        = [ordered]@{ exitCode = 0; versionAfter = '4.0.5'; desktopLink = $false; startMenuLink = $true; filesDiff = @(); unins001 = $false }
        }
    }

    function script:New-WizardPage {
        param([int]$Index, [string[]]$Labels)
        return [pscustomobject]@{ index = $Index; labels = $Labels; buttons = @(); screenshot = "wizard-pl-$Index.png" }
    }

    function script:New-WizardData {
        param([ValidateSet('pl', 'en')][string]$Language = 'pl')
        if ($Language -eq 'en') {
            $Titles = @('Welcome to the DSOptimizer Setup Wizard', 'Select Additional Tasks', 'Ready to Install', 'Completing the DSOptimizer Setup Wizard')
            $Welcome = 'DSOptimizer is free software under the Mozilla Public License 2.0: https://github.com/ds99-01/DSOptimizer/blob/main/LICENSE'
        }
        else {
            $Titles = @('Witamy w instalatorze aplikacji DSOptimizer', 'Zadania dodatkowe', 'Gotowy do rozpoczęcia instalacji', 'Zakończono instalację aplikacji DSOptimizer')
            $Welcome = 'DSOptimizer to darmowy program na licencji Mozilla Public License 2.0: https://github.com/ds99-01/DSOptimizer/blob/main/LICENSE'
        }
        $Pages = @(
            (New-WizardPage -Index 1 -Labels @($Titles[0], $Welcome))
            (New-WizardPage -Index 2 -Labels @($Titles[1], 'opis'))
            (New-WizardPage -Index 3 -Labels @($Titles[2], 'opis'))
            (New-WizardPage -Index 4 -Labels @($Titles[3], 'opis'))
        )
        return [ordered]@{
            language           = $Language
            pages              = $Pages
            desktopTaskChecked = $true
            desktopTaskSource  = 'msaa'
            launchChecked      = $true
            launchSource       = 'msaa'
            programStarted     = $true
            screenshots        = @('wizard-pl-1.png', 'wizard-pl-2.png', 'wizard-pl-3.png', 'wizard-pl-4.png')
        }
    }

    function script:New-MotwData {
        return [ordered]@{ zoneId = 3; titles = @('Program Manager', 'System Windows ochronił ten komputer'); screenshot = $true }
    }

    function script:Get-Failed {
        param([Parameter(Mandatory)][string]$Phase, [Parameter(Mandatory)]$Data)
        return @(Get-InstallerVerdict -Phase $Phase -Data $Data | Where-Object { -not $_.Passed })
    }
}

Describe 'Resolve-InstallerArtifact' {
    It 'katalog z zipem, instalatorem i zgodnymi sumami przechodzi' {
        $Release = New-FakeRelease -Dir (Join-Path $TestDrive 'ok')
        $Artifact = Resolve-InstallerArtifact -ArtifactDirectory $Release.Dir -Phase @('smoke', 'running')
        $Artifact.Version | Should -BeExactly '4.0.4'
        $Artifact.Zip.Name | Should -BeExactly 'DSOptimizer-4.0.4.zip'
        $Artifact.Zip.Path | Should -BeExactly $Release.Zip
        $Artifact.Setup.Name | Should -BeExactly 'DSOptimizer-4.0.4-setup.exe'
        $Artifact.Setup.Sha256 | Should -BeExactly ((Get-FileHash -LiteralPath $Release.Setup -Algorithm SHA256).Hash.ToLowerInvariant())
        $Artifact.Upgrade | Should -BeNullOrEmpty
    }
    It 'brak pliku (<Omit>) jest odrzucony' -ForEach @(
        @{ Omit = 'zip'; Pattern = '*zip*' }
        @{ Omit = 'setup'; Pattern = '*setup.exe*' }
        @{ Omit = 'sums'; Pattern = '*SHA256SUMS.txt*' }
    ) {
        $Release = New-FakeRelease -Dir (Join-Path $TestDrive "brak-$Omit") -Omit $Omit
        { Resolve-InstallerArtifact -ArtifactDirectory $Release.Dir -Phase @('smoke') } | Should -Throw $Pattern
    }
    It 'zła suma (<BadSum>) jest odrzucona' -ForEach @(@{ BadSum = 'zip' }, @{ BadSum = 'setup' }) {
        $Release = New-FakeRelease -Dir (Join-Path $TestDrive "suma-$BadSum") -BadSum $BadSum
        { Resolve-InstallerArtifact -ArtifactDirectory $Release.Dir -Phase @('smoke') } | Should -Throw '*różni się*'
    }
    It 'ścieżka względna i nieistniejący katalog są odrzucone' {
        { Resolve-InstallerArtifact -ArtifactDirectory 'wydanie' -Phase @('smoke') } | Should -Throw '*bezwzględn*'
        { Resolve-InstallerArtifact -ArtifactDirectory (Join-Path $TestDrive 'nie-ma') -Phase @('smoke') } | Should -Throw '*Brak katalogu*'
        { Resolve-InstallerArtifact -ArtifactDirectory '' -Phase @('smoke') } | Should -Throw '*-ArtifactDirectory*'
    }
    It 'zip i instalator w różnych wersjach są odrzucone' {
        $Dir = Join-Path $TestDrive 'mieszane'
        $null = New-FakeRelease -Dir $Dir -Version '4.0.4' -Omit 'setup'
        $null = New-FakeRelease -Dir $Dir -Version '4.0.3' -Omit 'zip'
        { Resolve-InstallerArtifact -ArtifactDirectory $Dir -Phase @('smoke') } | Should -Throw '*wersj*'
    }
    It '-Phase upgrade bez -UpgradeInstallerPath jest odrzucony' {
        $Release = New-FakeRelease -Dir (Join-Path $TestDrive 'bez-upgrade')
        { Resolve-InstallerArtifact -ArtifactDirectory $Release.Dir -Phase @('upgrade') } | Should -Throw '*-UpgradeInstallerPath*'
    }
    It '-UpgradeInstallerPath z wersją <Version> (nie wyższą niż wydanie 4.0.4) jest odrzucony' -ForEach @(@{ Version = '4.0.4' }, @{ Version = '4.0.3' }, @{ Version = '3.9.9' }) {
        $Release = New-FakeRelease -Dir (Join-Path $TestDrive "wydanie-$Version")
        $Upgrade = New-FakeRelease -Dir (Join-Path $TestDrive "nowsza-$Version") -Version $Version
        { Resolve-InstallerArtifact -ArtifactDirectory $Release.Dir -Phase @('upgrade') -UpgradeInstallerPath $Upgrade.Setup } | Should -Throw '*wyższ*'
    }
    It '-UpgradeInstallerPath z wyższą wersją przechodzi (zip tej wersji obok, sumy zgodne)' {
        $Release = New-FakeRelease -Dir (Join-Path $TestDrive 'wydanie-ok')
        $Upgrade = New-FakeRelease -Dir (Join-Path $TestDrive 'nowsza-ok') -Version '4.0.10'
        $Artifact = Resolve-InstallerArtifact -ArtifactDirectory $Release.Dir -Phase @('upgrade') -UpgradeInstallerPath $Upgrade.Setup
        $Artifact.Upgrade.Version | Should -BeExactly '4.0.10'
        $Artifact.Upgrade.Setup.Path | Should -BeExactly $Upgrade.Setup
        $Artifact.Upgrade.Zip.Name | Should -BeExactly 'DSOptimizer-4.0.10.zip'
    }
    It '-UpgradeInstallerPath ze złą sumą albo bez zipu tej wersji jest odrzucony' {
        $Release = New-FakeRelease -Dir (Join-Path $TestDrive 'wydanie-zle')
        $Bad = New-FakeRelease -Dir (Join-Path $TestDrive 'nowsza-zla-suma') -Version '4.0.5' -BadSum 'setup'
        { Resolve-InstallerArtifact -ArtifactDirectory $Release.Dir -Phase @('upgrade') -UpgradeInstallerPath $Bad.Setup } | Should -Throw '*różni się*'
        $NoZip = New-FakeRelease -Dir (Join-Path $TestDrive 'nowsza-bez-zipu') -Version '4.0.5' -Omit 'zip'
        { Resolve-InstallerArtifact -ArtifactDirectory $Release.Dir -Phase @('upgrade') -UpgradeInstallerPath $NoZip.Setup } | Should -Throw '*zip*'
    }
}

Describe 'Get-InstallerVerdict: komplet danych' {
    It 'faza <Phase>: wszystkie sprawdzenia zaliczone, nazwy unikalne' -ForEach @(
        @{ Phase = 'smoke'; Builder = 'New-SmokeData'; Count = 1 }
        @{ Phase = 'running'; Builder = 'New-RunningData'; Count = 18 }
        @{ Phase = 'upgrade'; Builder = 'New-UpgradeData'; Count = 13 }
        @{ Phase = 'wizard'; Builder = 'New-WizardData'; Count = 6 }
        @{ Phase = 'motw'; Builder = 'New-MotwData'; Count = 3 }
    ) {
        $Checks = @(Get-InstallerVerdict -Phase $Phase -Data (& $Builder))
        $Checks | Should -HaveCount $Count
        @($Checks | Where-Object { -not $_.Passed } | ForEach-Object { "$($_.Name): $($_.Detail)" }) | Should -BeNullOrEmpty
        @($Checks.Name | Sort-Object -Unique) | Should -HaveCount $Count
        foreach ($Check in $Checks) { $Check.Detail | Should -Not -BeNullOrEmpty -Because $Check.Name }
    }
    It 'kreator po angielsku: komplet danych przechodzi' {
        @(Get-Failed -Phase 'wizard' -Data (New-WizardData -Language 'en')) | Should -BeNullOrEmpty
    }
    It 'brak danych (<Phase>) = żadne sprawdzenie nie jest zaliczone' -ForEach @(@{ Phase = 'smoke' }, @{ Phase = 'running' }, @{ Phase = 'upgrade' }, @{ Phase = 'wizard' }, @{ Phase = 'motw' }) {
        $Checks = @(Get-InstallerVerdict -Phase $Phase -Data $null)
        $Checks.Count | Should -BeGreaterThan 0
        @($Checks | Where-Object { $_.Passed }) | Should -BeNullOrEmpty
    }
}

Describe 'Get-InstallerVerdict: jedno odstępstwo = jedno niezaliczone sprawdzenie' {
    It 'smoke: <Why> -> <Name>' -ForEach @(
        @{ Why = 'passed false'; Name = 'Smoke'; Mutate = { param($D) $D.result.passed = $false } }
        @{ Why = 'kod wyjścia 1'; Name = 'Smoke'; Mutate = { param($D) $D.exitCode = 1 } }
        @{ Why = 'brak smoke-result.json'; Name = 'Smoke'; Mutate = { param($D) $D.result = $null } }
    ) {
        $Data = New-SmokeData
        & $Mutate $Data
        $Failed = @(Get-Failed -Phase 'smoke' -Data $Data)
        $Failed | Should -HaveCount 1
        $Failed[0].Name | Should -BeExactly $Name
    }
    It 'running: <Why> -> <Name>' -ForEach @(
        @{ Why = 'cicha instalacja kod 3'; Name = 'InstallExit'; Mutate = { param($D) $D.install.exitCode = 3 } }
        @{ Why = 'okno A bez procesu'; Name = 'WindowA'; Mutate = { param($D) $D.windowA.processFound = $false } }
        @{ Why = 'okno A bez mutexa'; Name = 'WindowA'; Mutate = { param($D) $D.windowA.mutex = $false } }
        @{ Why = 'kopia B: inny tekst'; Name = 'PortableMessage'; Mutate = { param($D) $D.portable.message = 'Inny komunikat' } }
        @{ Why = 'kopia B: brak okna'; Name = 'PortableMessage'; Mutate = { param($D) $D.portable.message = $null } }
        @{ Why = 'kopia B: OK nie kliknięte'; Name = 'PortableMessage'; Mutate = { param($D) $D.portable.clickedOk = $false } }
        @{ Why = 'kopia B: LastTaskResult 0'; Name = 'PortableExit'; Mutate = { param($D) $D.portable.taskResult = 0 } }
        @{ Why = 'kopia B: zadanie nie skończyło się'; Name = 'PortableExit'; Mutate = { param($D) $D.portable.taskResult = $null } }
        @{ Why = 'okno A zamknięte po kopii B'; Name = 'WindowAAlive'; Mutate = { param($D) $D.portable.aAlive = $false } }
        @{ Why = 'CLI --apply kod 0'; Name = 'CliRefused'; Mutate = { param($D) $D.cliApply.exitCode = 0 } }
        @{ Why = 'CLI --apply inny błąd'; Name = 'CliRefused'; Mutate = { param($D) $D.cliApply.firstError = 'Inny błąd' } }
        @{ Why = '--list-apps grupa Suggested'; Name = 'ListAppsProtected'; Mutate = { param($D) $D.listApps.group = 'Suggested' } }
        @{ Why = '--list-apps bez wiersza'; Name = 'ListAppsProtected'; Mutate = { param($D) $D.listApps.group = $null } }
        @{ Why = '--list-apps kod 1'; Name = 'ListAppsProtected'; Mutate = { param($D) $D.listApps.exitCode = 1 } }
        @{ Why = 'instalator przy oknie kod 0'; Name = 'ReinstallRefused'; Mutate = { param($D) $D.reinstall.exitCode = 0 } }
        @{ Why = 'instalator przy oknie zmienił wersję'; Name = 'ReinstallUnchanged'; Mutate = { param($D) $D.reinstall.versionAfter = '4.0.5' } }
        @{ Why = 'instalator przy oknie zmienił pliki'; Name = 'ReinstallUnchanged'; Mutate = { param($D) $D.reinstall.filesDiff = @('inna suma SHA256: DSOptimizer.exe') } }
        @{ Why = 'deinstalator przy oknie usunął pliki'; Name = 'UninstallRefusedKeeps'; Mutate = { param($D) $D.uninstallRunning.filesDiff = @('brak pliku: DSOptimizer.exe') } }
        @{ Why = 'deinstalator przy oknie usunął klucz'; Name = 'UninstallRefusedKeeps'; Mutate = { param($D) $D.uninstallRunning.keyPresent = $false } }
        @{ Why = 'relaunch: C nie żyje'; Name = 'RelaunchAlive'; Mutate = { param($D) $D.relaunch.cAlive = $false } }
        @{ Why = 'relaunch: A nie zamknięte'; Name = 'RelaunchAlive'; Mutate = { param($D) $D.relaunch.aStopped = $false } }
        @{ Why = 'relaunch: brak odczytu zamknięcia A'; Name = 'RelaunchAlive'; Mutate = { param($D) $D.relaunch.aStopped = $null } }
        @{ Why = 'relaunch: A martwe przed startem C'; Name = 'RelaunchAlive'; Mutate = { param($D) $D.relaunch.aAliveBeforeC = $false } }
        @{ Why = 'relaunch: brak mutexa przed startem C'; Name = 'RelaunchAlive'; Mutate = { param($D) $D.relaunch.mutexBeforeC = $false } }
        @{ Why = 'relaunch: C niewykryte'; Name = 'RelaunchAlive'; Mutate = { param($D) $D.relaunch.cDetected = $false } }
        @{ Why = 'relaunch: A martwe tuż przed zamknięciem'; Name = 'RelaunchAlive'; Mutate = { param($D) $D.relaunch.aAliveBeforeStop = $false } }
        @{ Why = 'relaunch: brak mutexa tuż przed zamknięciem A'; Name = 'RelaunchAlive'; Mutate = { param($D) $D.relaunch.mutexBeforeStop = $false } }
        @{ Why = 'relaunch: trzymanie krótsze niż 8 s'; Name = 'RelaunchAlive'; Mutate = { param($D) $D.relaunch.holdSeconds = 7 } }
        @{ Why = 'relaunch: brak odczytu trzymania'; Name = 'RelaunchAlive'; Mutate = { param($D) $D.relaunch.holdSeconds = $null } }
        @{ Why = 'relaunch: błąd sondy bez odczytu'; Name = 'RelaunchNoRefusal'; Mutate = { param($D) $D.relaunch.refusalSeen = $null; $D.relaunch.probeError = 'sonda przy żywym A: brak wyniku' } }
        @{ Why = 'relaunch: błąd jednej z sond'; Name = 'RelaunchNoRefusal'; Mutate = { param($D) $D.relaunch.probeError = 'sonda po zamknięciu A: brak wyniku' } }
        @{ Why = 'kopia B: błąd sondy'; Name = 'PortableMessage'; Mutate = { param($D) $D.portable.probeError = 'Sonda Inst-Refusal-B nie zwróciła wyniku.' } }
        @{ Why = 'relaunch: brak mutexa'; Name = 'RelaunchMutex'; Mutate = { param($D) $D.relaunch.mutex = $false } }
        @{ Why = 'relaunch: okno już działa'; Name = 'RelaunchNoRefusal'; Mutate = { param($D) $D.relaunch.refusalSeen = $true } }
        @{ Why = 'mutex nie zniknął po zamknięciu C'; Name = 'MutexReleased'; Mutate = { param($D) $D.final.mutexReleased = $false } }
        @{ Why = 'deinstalacja kod 1'; Name = 'UninstallExit'; Mutate = { param($D) $D.final.exitCode = 1 } }
        @{ Why = 'folder programu został'; Name = 'UninstallRemoved'; Mutate = { param($D) $D.final.appDirGone = $false } }
        @{ Why = 'klucz Uninstall został'; Name = 'UninstallRemoved'; Mutate = { param($D) $D.final.keyGone = $false } }
        @{ Why = 'skrót w Menu Start został'; Name = 'UninstallRemoved'; Mutate = { param($D) $D.final.startMenuGone = $false } }
        @{ Why = 'skrót na pulpicie został'; Name = 'UninstallRemoved'; Mutate = { param($D) $D.final.desktopGone = $false } }
        @{ Why = 'ProgramData bez pliku'; Name = 'ProgramDataKept'; Mutate = { param($D) $D.final.dataAfter = @('settings.json') } }
        @{ Why = 'ProgramData usunięty'; Name = 'ProgramDataKept'; Mutate = { param($D) $D.final.dataAfter = $null } }
        @{ Why = 'ProgramData pusty przed deinstalacją'; Name = 'ProgramDataKept'; Mutate = { param($D) $D.final.dataBefore = @(); $D.final.dataAfter = @() } }
        @{ Why = 'ProgramData inny właściciel'; Name = 'ProgramDataOwner'; Mutate = { param($D) $D.final.ownerAfter = 'S-1-5-21-1-2-3-1001' } }
    ) {
        $Data = New-RunningData
        & $Mutate $Data
        $Failed = @(Get-Failed -Phase 'running' -Data $Data)
        @($Failed | ForEach-Object { $_.Name }) | Should -Be @($Name)
    }
    It 'upgrade: <Why> -> <Name>' -ForEach @(
        @{ Why = 'instalacja wydania kod 1'; Name = 'InstallExit'; Mutate = { param($D) $D.install.exitCode = 1 } }
        @{ Why = 'skrót z pulpitu nie usunięty'; Name = 'DesktopIconRemoved'; Mutate = { param($D) $D.desktopRemoved.gone = $false } }
        @{ Why = 'skrótu na pulpicie nie było'; Name = 'DesktopIconRemoved'; Mutate = { param($D) $D.desktopRemoved.existed = $false } }
        @{ Why = 'okno A bez mutexa'; Name = 'WindowA'; Mutate = { param($D) $D.windowA.mutex = $false } }
        @{ Why = 'nowsza wersja przy oknie kod 0'; Name = 'UpgradeRefused'; Mutate = { param($D) $D.upgradeRunning.exitCode = 0 } }
        @{ Why = 'nowsza wersja przy oknie zmieniła DisplayVersion'; Name = 'UpgradeRefusedVersion'; Mutate = { param($D) $D.upgradeRunning.versionAfter = '4.0.5' } }
        @{ Why = 'nowsza wersja przy oknie zmieniła pliki'; Name = 'UpgradeRefusedUnchanged'; Mutate = { param($D) $D.upgradeRunning.filesDiff = @('inna suma SHA256: DSOptimizer.exe') } }
        @{ Why = 'brak odczytu plików przy odmowie'; Name = 'UpgradeRefusedUnchanged'; Mutate = { param($D) $D.upgradeRunning.filesDiff = $null } }
        @{ Why = 'mutex nie zniknął'; Name = 'MutexReleased'; Mutate = { param($D) $D.mutexReleased = $false } }
        @{ Why = 'aktualizacja kod 7'; Name = 'UpgradeExit'; Mutate = { param($D) $D.upgrade.exitCode = 7 } }
        @{ Why = 'DisplayVersion stara'; Name = 'UpgradeVersion'; Mutate = { param($D) $D.upgrade.versionAfter = '4.0.4' } }
        @{ Why = 'skrót na pulpicie wrócił'; Name = 'DesktopIconNotRestored'; Mutate = { param($D) $D.upgrade.desktopLink = $true } }
        @{ Why = 'brak skrótu w Menu Start'; Name = 'StartMenuLink'; Mutate = { param($D) $D.upgrade.startMenuLink = $false } }
        @{ Why = 'pliki różne od zipu'; Name = 'UpgradeFiles'; Mutate = { param($D) $D.upgrade.filesDiff = @('inna suma SHA256: DSOptimizer.exe') } }
        @{ Why = 'brak odczytu plików'; Name = 'UpgradeFiles'; Mutate = { param($D) $D.upgrade.filesDiff = $null } }
        @{ Why = 'drugi deinstalator'; Name = 'SingleUninstaller'; Mutate = { param($D) $D.upgrade.unins001 = $true } }
    ) {
        $Data = New-UpgradeData
        & $Mutate $Data
        $Failed = @(Get-Failed -Phase 'upgrade' -Data $Data)
        @($Failed | ForEach-Object { $_.Name }) | Should -Be @($Name)
    }
    It 'wizard: <Why> -> <Name>' -ForEach @(
        @{ Why = 'zadania przed powitaniem'; Name = 'WizardPages'; Mutate = { param($D) $D.pages = @($D.pages[1], $D.pages[0], $D.pages[2], $D.pages[3]) } }
        @{ Why = 'brak strony gotowe'; Name = 'WizardPages'; Mutate = { param($D) $D.pages = @($D.pages[0], $D.pages[1], $D.pages[3]) } }
        @{ Why = 'nieznana strona w środku'; Name = 'WizardPages'; Mutate = { param($D) $D.pages = @($D.pages[0], $D.pages[1], (New-WizardPage -Index 9 -Labels @('Instalacja')), $D.pages[2], $D.pages[3]); $D.screenshots += 'wizard-pl-9.png' } }
        @{ Why = 'powitanie bez licencji'; Name = 'WelcomeLicense'; Mutate = { param($D) $D.pages[0].labels = @('Witamy w instalatorze aplikacji DSOptimizer', 'DSOptimizer to darmowy program.') } }
        @{ Why = 'pole pulpitu niezaznaczone'; Name = 'DesktopTaskChecked'; Mutate = { param($D) $D.desktopTaskChecked = $false } }
        @{ Why = 'pole pulpitu nieczytelne'; Name = 'DesktopTaskChecked'; Mutate = { param($D) $D.desktopTaskChecked = $null } }
        @{ Why = 'pole Uruchom niezaznaczone'; Name = 'LaunchChecked'; Mutate = { param($D) $D.launchChecked = $false } }
        @{ Why = 'program nie wystartował'; Name = 'ProgramStarted'; Mutate = { param($D) $D.programStarted = $false } }
        @{ Why = 'brak zrzutu strony'; Name = 'Screenshots'; Mutate = { param($D) $D.screenshots = @('wizard-pl-1.png', 'wizard-pl-2.png', 'wizard-pl-4.png') } }
    ) {
        $Data = New-WizardData
        & $Mutate $Data
        $Failed = @(Get-Failed -Phase 'wizard' -Data $Data)
        @($Failed | ForEach-Object { $_.Name }) | Should -Be @($Name)
    }
    It 'motw: <Why> -> <Name>' -ForEach @(
        @{ Why = 'brak okna SmartScreen'; Name = 'SmartScreenWindow'; Mutate = { param($D) $D.titles = @('Program Manager', 'Instalacja - DSOptimizer') } }
        @{ Why = 'ZoneId 0'; Name = 'MotwZone'; Mutate = { param($D) $D.zoneId = 0 } }
        @{ Why = 'brak zrzutu'; Name = 'Screenshot'; Mutate = { param($D) $D.screenshot = $false } }
    ) {
        $Data = New-MotwData
        & $Mutate $Data
        $Failed = @(Get-Failed -Phase 'motw' -Data $Data)
        @($Failed | ForEach-Object { $_.Name }) | Should -Be @($Name)
    }
    It 'motw: angielski tytuł okna SmartScreen też przechodzi' {
        $Data = New-MotwData
        $Data.titles = @('Windows protected your PC')
        @(Get-Failed -Phase 'motw' -Data $Data) | Should -BeNullOrEmpty
    }
}

Describe 'Faza running na mockach (kolejność relaunchu, błędy sond)' {
    BeforeEach {
        $script:Calls = [System.Collections.Generic.List[string]]::new()
        $script:ProbeBehavior = 'ok'
        $script:CliErrors = @('Działa już inna kopia DSOptimizera (okno albo polecenie, które zmienia system).')
        Mock Initialize-InstallerVm { @{ 'DSOptimizer-4.0.4.zip' = 'C:\dso\instalator\DSOptimizer-4.0.4.zip'; 'DSOptimizer-4.0.4-setup.exe' = 'C:\dso\instalator\DSOptimizer-4.0.4-setup.exe' } }
        Mock Invoke-InstallerSetup { $script:Calls.Add("setup:$Step"); if ($Step -like '*running') { if ($Uninstall) { 1 } else { 7 } } else { 0 } }
        Mock Start-InstallerWindowA { $script:Calls.Add('windowA'); [ordered]@{ processFound = $true; mutex = $true; pid = 7; seconds = 2 } }
        Mock Confirm-DsoLanguageChoice -RemoveParameterValidation 'Session' { [pscustomobject]@{ Shown = $true; Chosen = $true } }
        Mock Start-DsoGuiInUserSession -RemoveParameterValidation 'Session' { $script:Calls.Add("gui:$TaskName"); '2026-11-04T10:00:00Z' }
        Mock Invoke-InstallerRefusalProbe {
            $script:Calls.Add("probe:$Name")
            if ($script:ProbeBehavior -eq 'throw') { throw "Sonda $Name nie zwróciła wyniku." }
            if ($Name -eq 'Inst-Refusal-B') { return [pscustomobject]@{ Found = $true; Text = 'DSOptimizer już działa (okno).'; Clicked = $true; ProcessId = 9 } }
            [pscustomobject]@{ Found = $false; Text = $null; Clicked = $false; ProcessId = $null }
        }
        Mock Invoke-InstallerRemote {
            $script:Calls.Add("remote:$(@($ArgumentList) -join ',')")
            [pscustomobject]@{ Finished = $true; LastTaskResult = 1; State = 'Ready'; ProcessFound = $true; Pid = 42; Mutex = $true; Seconds = 1; Released = $true
                Exists = $true; Files = @('settings.json'); Owner = 'S-1-5-32-544'; Alive = $true; WasAlive = $true; Gone = $true }
        }
        Mock Invoke-DsoCli -RemoveParameterValidation 'Session' {
            [pscustomobject]@{ ExitCode = 1; TimedOut = $false; ReportError = $null
                Report = [pscustomobject]@{ errors = $script:CliErrors; apps = @([pscustomobject]@{ name = 'DSOptimizer'; group = 'Protected' }) } }
        }
        Mock Get-InstallerState { [pscustomobject]@{ AppExists = $true; Files = @{ 'DSOptimizer.exe' = 'AA' }; KeyPresent = $true; DisplayVersion = '4.0.4'; StartMenuLink = $true; DesktopLink = $true; Unins001 = $false } }
        Mock Start-Sleep { $script:Calls.Add("sleep:$Seconds") }
        $script:Context = [ordered]@{ Session = $null; Plan = (Get-InstallerPlan); Artifact = [ordered]@{ Version = '4.0.4'; Zip = @{ Name = 'DSOptimizer-4.0.4.zip' }; Setup = @{ Name = 'DSOptimizer-4.0.4-setup.exe' } }
            VmName = 'DSO-26H2-Pro'; VmDirectory = $TestDrive; Run = [ordered]@{ Key = 'running'; Phase = 'running'; Language = $null }; Timeline = [System.Collections.Generic.List[string]]::new() }
        $script:Record = [ordered]@{ data = $null; problems = @() }
    }
    It 'relaunch: wykrycie C, sonda przy żywym A, czekanie do 8 s od wykrycia C, dopiero potem zamknięcie A' {
        Invoke-InstallerRunningPhase -Context $script:Context -Record $script:Record
        $Calls = @($script:Calls)
        $DetectC = [array]::IndexOf($Calls, "remote:C:\Program Files\DSOptimizer\DSOptimizer.exe,Global\DSOptimizer.SingleInstance,30,--relaunched")
        $HoldProbe = [array]::IndexOf($Calls, 'probe:Inst-Refusal-Hold')
        $StopA = [array]::IndexOf($Calls, 'remote:7,15')
        $AfterProbe = [array]::IndexOf($Calls, 'probe:Inst-Refusal-C')
        # Odczyt „A żyje + mutex” (PID 7) przed startem C i tuż przed zamknięciem A.
        $AliveChecks = @(for ($I = 0; $I -lt $Calls.Count; $I++) { if ($Calls[$I] -ceq 'remote:7,Global\DSOptimizer.SingleInstance') { $I } })
        $AliveChecks | Should -HaveCount 2
        $StartC = [array]::IndexOf($Calls, 'remote:C:\Program Files\DSOptimizer\DSOptimizer.exe,--relaunched,C:\Program Files\DSOptimizer,dso,DSO-Inst-C,Highest')
        $AliveChecks[0] | Should -BeLessThan $StartC
        $DetectC | Should -BeGreaterThan $StartC
        $HoldProbe | Should -BeGreaterThan $DetectC
        $AliveChecks[1] | Should -BeGreaterThan $HoldProbe
        $AliveChecks[1] | Should -BeLessThan $StopA
        $StopA | Should -BeGreaterThan $HoldProbe
        $AfterProbe | Should -BeGreaterThan $StopA
        # Mock sondy wraca od razu, więc skrypt dosypia resztę z 8 s przed zamknięciem A.
        $Sleeps = @($Calls[$HoldProbe..$StopA] | Where-Object { $_ -like 'sleep:*' } | ForEach-Object { [int]($_ -replace 'sleep:', '') })
        ($Sleeps | Measure-Object -Sum).Sum | Should -BeGreaterOrEqual 7
        $script:Record.data.relaunch.aStopped | Should -BeTrue
        $script:Record.data.relaunch.aAliveBeforeC | Should -BeTrue
        $script:Record.data.relaunch.aAliveBeforeStop | Should -BeTrue
        $script:Record.data.relaunch.refusalSeen | Should -BeFalse
        $script:Record.data.cliApply.firstError | Should -Match '^Działa już inna kopia'
    }
    It 'A martwe już przed relaunchem (wolny mutex): zamknięcie „już nieżywego” A nie liczy się, RelaunchAlive niezaliczone' {
        Mock Invoke-InstallerRemote {
            $script:Calls.Add("remote:$(@($ArgumentList) -join ',')")
            [pscustomobject]@{ Finished = $true; LastTaskResult = 1; State = 'Ready'; ProcessFound = $true; Pid = 42; Mutex = $true; Seconds = 1; Released = $true
                Exists = $true; Files = @('settings.json'); Owner = 'S-1-5-32-544'; Alive = $false; WasAlive = $false; Gone = $true }
        }
        Invoke-InstallerRunningPhase -Context $script:Context -Record $script:Record
        $script:Record.data.relaunch.aAliveBeforeC | Should -BeFalse
        $script:Record.data.relaunch.aAliveBeforeStop | Should -BeFalse
        $script:Record.data.relaunch.aStopped | Should -BeFalse
        $Check = @(Get-InstallerVerdict -Phase 'running' -Data $script:Record.data) | Where-Object Name -eq 'RelaunchAlive'
        $Check.Passed | Should -BeFalse
        $Check.Detail | Should -Match 'A żyło przed startem C'
        $Check.Detail | Should -Match 'zamknięto żywe A'
    }
    It 'błąd sond B i C nie przerywa fazy: dalsze kroki wykonane, niezaliczone PortableMessage i RelaunchNoRefusal z opisem błędu' {
        $script:ProbeBehavior = 'throw'
        { Invoke-InstallerRunningPhase -Context $script:Context -Record $script:Record } | Should -Not -Throw
        $script:Calls | Should -Contain 'setup:uninstall'
        $script:Record.data.portable.probeError | Should -Match 'Inst-Refusal-B'
        $script:Record.data.relaunch.refusalSeen | Should -BeNullOrEmpty
        $script:Record.data.relaunch.probeError | Should -Match 'Inst-Refusal-Hold'
        $script:Record.data.relaunch.probeError | Should -Match 'Inst-Refusal-C'
        $script:Record.data.final | Should -Not -BeNullOrEmpty
        $Checks = @(Get-InstallerVerdict -Phase 'running' -Data $script:Record.data)
        ($Checks | Where-Object Name -eq 'PortableMessage').Passed | Should -BeFalse
        ($Checks | Where-Object Name -eq 'PortableMessage').Detail | Should -Match 'błąd sondy'
        ($Checks | Where-Object Name -eq 'RelaunchNoRefusal').Passed | Should -BeFalse
        ($Checks | Where-Object Name -eq 'RelaunchNoRefusal').Detail | Should -Match 'błąd sondy'
    }
    It 'CLI bez listy errors (np. nie odmówiło) pod StrictMode: brak wyjątku, firstError pusty' {
        $script:CliErrors = @()
        Set-StrictMode -Version Latest
        try { { Invoke-InstallerRunningPhase -Context $script:Context -Record $script:Record } | Should -Not -Throw }
        finally { Set-StrictMode -Off }
        $script:Record.data.cliApply.firstError | Should -BeNullOrEmpty
    }
}

Describe 'Odczyt wyników sond bez pól (StrictMode)' {
    BeforeEach {
        Mock Initialize-InstallerVm { @{ 'DSOptimizer-4.0.4-setup.exe' = 'C:\dso\instalator\DSOptimizer-4.0.4-setup.exe' } }
        Mock Invoke-InstallerRemote { $script:RemoteArgs.Add((@($ArgumentList) -join '|')); [pscustomobject]@{ Path = 'C:\Users\dso\Desktop\DSOptimizer-4.0.4-setup.exe'; ZoneId = 3 } }
        Mock Invoke-DsoUserSessionScript -RemoveParameterValidation 'Session' { '{"Ok":false,"Error":"Sonda dziala tylko w VM"}' }
        Mock Copy-InstallerScreenshot { $null }
        Mock Start-Sleep { }
        $script:RemoteArgs = [System.Collections.Generic.List[string]]::new()
        $script:Context = [ordered]@{ Session = $null; Plan = (Get-InstallerPlan); Artifact = [ordered]@{ Version = '4.0.4'; Setup = @{ Name = 'DSOptimizer-4.0.4-setup.exe' } }
            VmName = 'DSO-26H2-Pro'; VmDirectory = $TestDrive; Run = [ordered]@{ Key = 'wizard-pl'; Phase = 'wizard'; Language = 'pl' }; Timeline = [System.Collections.Generic.List[string]]::new() }
        $script:Record = [ordered]@{ data = $null; problems = @(); screenshots = @() }
    }
    It 'kreator: wynik z samym Ok/Error daje problem z prawdziwym błędem sondy; start z /LOG w C:\dso\out\logs' {
        Set-StrictMode -Version Latest
        try { { Invoke-InstallerWizardPhase -Context $script:Context -Record $script:Record } | Should -Not -Throw }
        finally { Set-StrictMode -Off }
        @($script:Record.problems) | Should -Contain 'sonda kreatora: Sonda dziala tylko w VM'
        @($script:Record.data.pages) | Should -HaveCount 0
        @($script:RemoteArgs | Where-Object { $_ -like '*/LANG=pl /LOG=C:\dso\out\logs\wizard-pl.log*' }) | Should -HaveCount 1
        $script:Record.data.log | Should -BeExactly 'C:\dso\out\logs\wizard-pl.log'
    }
    It 'motw: wynik z samym Ok/Error daje problem z prawdziwym błędem sondy' {
        $script:Context.Run = [ordered]@{ Key = 'motw'; Phase = 'motw'; Language = $null }
        Set-StrictMode -Version Latest
        try { { Invoke-InstallerMotwPhase -Context $script:Context -Record $script:Record } | Should -Not -Throw }
        finally { Set-StrictMode -Off }
        @($script:Record.problems) | Should -Contain 'sonda okien: Sonda dziala tylko w VM'
        $script:Record.data.screenshot | Should -BeFalse
    }
}

Describe 'Strony kreatora' {
    It 'rozpoznaje stronę po tytule (pl i en), także z „&” i podwójnymi spacjami' {
        Get-InstallerWizardPageKind -Labels @('Witamy w  instalatorze aplikacji DSOptimizer', 'x') -Language 'pl' | Should -BeExactly 'powitanie'
        Get-InstallerWizardPageKind -Labels @('Zadania dodatkowe') -Language 'pl' | Should -BeExactly 'zadania'
        Get-InstallerWizardPageKind -Labels @('Ready to Install') -Language 'en' | Should -BeExactly 'gotowe'
        Get-InstallerWizardPageKind -Labels @('Completing the DSOptimizer Setup Wizard') -Language 'en' | Should -BeExactly 'koniec'
        Get-InstallerWizardPageKind -Labels @('Instalacja') -Language 'pl' | Should -BeExactly 'nieznana'
        Get-InstallerWizardPageKind -Labels @('Zadania dodatkowe', 'Gotowy do rozpoczęcia instalacji') -Language 'pl' | Should -BeExactly 'niejednoznaczna'
    }
    It 'teksty sond: oba języki, ścieżka zainstalowanego programu i przedrostki komunikatów' {
        $Texts = Get-InstallerProbeTexts | ConvertFrom-Json
        $Texts.installedExe | Should -BeExactly 'C:\Program Files\DSOptimizer\DSOptimizer.exe'
        $Texts.refusalPrefix | Should -BeExactly 'DSOptimizer już działa'
        $Texts.windowTitle | Should -BeExactly 'DSOptimizer'
        @($Texts.wizard.pl.next) | Should -Contain 'Dalej >'
        @($Texts.wizard.en.finish) | Should -Contain 'Finish'
        $Texts.wizard.pl.desktopTask | Should -BeExactly 'Utwórz skrót na pulpicie'
        $Texts.wizard.en.launchTask | Should -BeExactly 'Launch DSOptimizer'
    }
}

Describe 'Plan i argumenty' {
    It 'plan: ścieżki w VM bez spacji poza zainstalowanym programem, stałe instalatora' {
        $Plan = Get-InstallerPlan
        foreach ($Name in 'WorkDirectory', 'PortableDirectory', 'PortableExe', 'OutDirectory', 'LogDirectory', 'SmokeLogDirectory', 'UserDesktop') {
            [string]$Plan[$Name] | Should -Match '^C:\\[^\s]+\z' -Because $Name
        }
        $Plan.InstalledExe | Should -BeExactly 'C:\Program Files\DSOptimizer\DSOptimizer.exe'
        $Plan.MutexName | Should -BeExactly 'Global\DSOptimizer.SingleInstance'
        $Plan.UninstallSubKey | Should -BeExactly 'SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall\{82DBAF33-36AA-4F12-A327-EEC64159D8E8}_is1'
        $Plan.AdministratorsSid | Should -BeExactly 'S-1-5-32-544'
        $Plan.RefusedExitCode | Should -Be 7
        @($Plan.CliApplyArguments) | Should -Be @('--apply', 'privacy.widgets', '--restore-point', 'skip')
        @($Plan.SmartScreenTitles) | Should -Be @('System Windows ochronił ten komputer', 'Windows protected your PC')
        @($Plan.WizardPageOrder) | Should -Be @('powitanie', 'zadania', 'gotowe', 'koniec')
        $Plan.MotwZoneId | Should -Be 3
    }
    It 'Get-InstallerPhaseRuns: stała kolejność faz, kreator osobno dla każdego języka' {
        $Runs = @(Get-InstallerPhaseRuns -Phase @('motw', 'wizard', 'smoke', 'wizard') -Language @('en', 'pl'))
        @($Runs | ForEach-Object { $_.Key }) | Should -Be @('smoke', 'wizard-pl', 'wizard-en', 'motw')
        $Runs[1].Phase | Should -BeExactly 'wizard'
        $Runs[1].Language | Should -BeExactly 'pl'
        { Get-InstallerPhaseRuns -Phase @() -Language @('pl') } | Should -Throw
        { Get-InstallerPhaseRuns -Phase @('wizard') -Language @() } | Should -Throw '*-Language*'
    }
    It 'parametry skryptu: tylko VM Home/Pro, pięć faz i dwa języki' {
        $Parameters = (Get-Command $script:InstallerScriptPath).ScriptBlock.Ast.ParamBlock.Parameters
        $Set = { param($Name) $P = $Parameters | Where-Object { $_.Name.VariablePath.UserPath -eq $Name }; @($P.Attributes | Where-Object { $_.TypeName.Name -eq 'ValidateSet' } | ForEach-Object { $_.PositionalArguments.Value }) }
        & $Set 'VmName' | Should -Be @('DSO-26H2-Home', 'DSO-26H2-Pro')
        & $Set 'Phase' | Should -Be @('smoke', 'running', 'upgrade', 'wizard', 'motw')
        & $Set 'Language' | Should -Be @('pl', 'en')
    }
    It 'Compare-InstallerFiles: brak, nadmiar i inna suma' {
        $Diff = Compare-InstallerFiles -Expected @{ 'a.exe' = 'AA'; 'b.dll' = 'BB' } -Actual @{ 'a.exe' = 'AA'; 'b.dll' = 'XX'; 'c.txt' = 'CC' }
        $Diff | Should -Be @('inna suma SHA256: b.dll', 'nadmiarowy plik: c.txt')
        $Missing = Compare-InstallerFiles -Expected @{ 'a.exe' = 'AA' } -Actual @{}
        $Missing | Should -Be @('brak pliku: a.exe')
        # Równe = pusta tablica (nie $null): werdykt odróżnia „bez różnic” od „brak odczytu”.
        $Same = Compare-InstallerFiles -Expected @{ 'a.exe' = 'AA' } -Actual @{ 'a.exe' = 'AA' }
        $null -eq $Same | Should -BeFalse
        $Same.Count | Should -Be 0
    }
}

Describe '-ShowPlan' {
    It 'wypisuje plan i nie woła żadnej funkcji VM' {
        Mock Restore-VmCleanState { throw 'nie wolno' }
        Mock Start-VM { throw 'nie wolno' }
        Mock Wait-VmReady { throw 'nie wolno' }
        Mock Get-DsoVmCredential { throw 'nie wolno' }
        Mock Invoke-InstallerVmRun { throw 'nie wolno' }
        $Release = New-FakeRelease -Dir (Join-Path $TestDrive 'plan')
        $Output = Invoke-InstallerExplorationMain -VmName 'DSO-26H2-Pro' -ArtifactDirectory $Release.Dir -OutDirectory (Join-Path $TestDrive 'out') `
            -Phase @('smoke', 'wizard') -Language @('pl') -CheckpointName 'czysta-26H2' -ReadyTimeoutSeconds 600 -ShowPlan 6>$null
        $Code = @($Output)[-1]
        $Code | Should -Be 0
        $Json = (@($Output)[0..(@($Output).Count - 2)] -join "`n") | ConvertFrom-Json
        @($Json.Runs | ForEach-Object { $_.Key }) | Should -Be @('smoke', 'wizard-pl')
        $Json.Artifact.Version | Should -BeExactly '4.0.4'
        Should -Invoke Restore-VmCleanState -Times 0 -Exactly
        Should -Invoke Start-VM -Times 0 -Exactly
        Should -Invoke Wait-VmReady -Times 0 -Exactly
        Should -Invoke Get-DsoVmCredential -Times 0 -Exactly
        Should -Invoke Invoke-InstallerVmRun -Times 0 -Exactly
    }
    It 'błąd argumentów (upgrade bez -UpgradeInstallerPath) = kod 1 przed VM' {
        Mock Restore-VmCleanState { throw 'nie wolno' }
        $Release = New-FakeRelease -Dir (Join-Path $TestDrive 'plan-zly')
        $Output = Invoke-InstallerExplorationMain -VmName 'DSO-26H2-Pro' -ArtifactDirectory $Release.Dir -OutDirectory (Join-Path $TestDrive 'out') `
            -Phase @('upgrade') -Language @('pl') -CheckpointName 'czysta-26H2' -ReadyTimeoutSeconds 600 -ShowPlan 6>$null
        @($Output)[-1] | Should -Be 1
        Should -Invoke Restore-VmCleanState -Times 0 -Exactly
    }
}

Describe 'Invoke-InstallerVmRun: sprzątanie i zaliczenia' {
    BeforeEach {
        $script:Calls = [System.Collections.Generic.List[string]]::new()
        $script:Saved = $null
        $script:PhaseBehavior = 'ok'
        Mock Assert-DsoVmLiveAllowed { }
        Mock Get-DsoVmCredential { New-Object System.Management.Automation.PSCredential('x', (ConvertTo-SecureString 'x' -AsPlainText -Force)) }
        Mock Restore-VmCleanState { $script:Calls.Add('revert') }
        Mock Disconnect-VMNetworkAdapter { $script:Calls.Add('disconnect') }
        Mock Start-VM { $script:Calls.Add('start') }
        Mock Wait-VmReady { $script:Calls.Add('ready'); $null }
        Mock Start-Sleep { }
        Mock Remove-PSSession -RemoveParameterValidation 'Session' { }
        Mock Complete-ExploreVmCleanup { $script:Calls.Add('cleanup'); [pscustomobject]@{ Ok = $true; Problems = @() } }
        Mock Save-ExploreResult { $script:Calls.Add('save'); $script:Saved = $Result; 'C:\w\installer.json' }
        Mock Invoke-InstallerPhase {
            $script:Calls.Add("phase:$($Run.Key)")
            if ($script:PhaseBehavior -eq 'throw') { throw 'boom w fazie' }
            $Record.data = New-MotwData
            if ($script:PhaseBehavior -eq 'problem') { $Record.problems += 'sonda: brak wyniku' }
        }
        $script:Artifact = [ordered]@{ Version = '4.0.4' }
    }
    It 'faza zaliczona: revert przed, sprzątanie po, installer.json zapisany' {
        $Path = Invoke-InstallerVmRun -VmName 'DSO-26H2-Pro' -OutDirectory $TestDrive -Runs @(Get-InstallerPhaseRuns -Phase @('motw') -Language @('pl')) -Artifact $script:Artifact 6>$null
        $Path | Should -BeExactly 'C:\w\installer.json'
        $script:Calls.IndexOf('revert') | Should -BeLessThan $script:Calls.IndexOf('phase:motw')
        $script:Calls.IndexOf('cleanup') | Should -BeGreaterThan $script:Calls.IndexOf('phase:motw')
        Should -Invoke Save-ExploreResult -Times 1 -Exactly -ParameterFilter { $FileName -eq 'installer.json' }
        $script:Saved.allPassed | Should -BeTrue
        $script:Saved.phases['motw'].passed | Should -BeTrue
        @($script:Saved.phases['motw'].checks) | Should -HaveCount 3
    }
    It 'wyjątek w fazie: sprzątanie nadal wywołane, wynik zapisany z błędem, przebieg zgłasza błąd' {
        $script:PhaseBehavior = 'throw'
        { Invoke-InstallerVmRun -VmName 'DSO-26H2-Pro' -OutDirectory $TestDrive -Runs @(Get-InstallerPhaseRuns -Phase @('smoke') -Language @('pl')) -Artifact $script:Artifact 6>$null } | Should -Throw '*boom w fazie*'
        Should -Invoke Complete-ExploreVmCleanup -Times 1 -Exactly
        $script:Saved.phases['smoke'].error | Should -Match 'boom'
        $script:Saved.phases['smoke'].passed | Should -BeFalse
        $script:Saved.allPassed | Should -BeFalse
    }
    It 'problem fazy odbiera zaliczenie mimo zaliczonych sprawdzeń' {
        $script:PhaseBehavior = 'problem'
        { Invoke-InstallerVmRun -VmName 'DSO-26H2-Pro' -OutDirectory $TestDrive -Runs @(Get-InstallerPhaseRuns -Phase @('motw') -Language @('pl')) -Artifact $script:Artifact 6>$null } | Should -Throw '*brak wyniku*'
        $script:Saved.phases['motw'].passed | Should -BeFalse
    }
    It 'każda faza na świeżym reverrcie: dwa języki kreatora = dwa przebiegi i dwa sprzątania' {
        # Werdykt kreatora na danych motw nie przechodzi, więc przebieg zgłasza błąd; tu liczy się tylko liczba revertów i sprzątań.
        try { $null = Invoke-InstallerVmRun -VmName 'DSO-26H2-Pro' -OutDirectory $TestDrive -Runs @(Get-InstallerPhaseRuns -Phase @('motw', 'wizard') -Language @('pl', 'en')) -Artifact $script:Artifact 6>$null 3>$null }
        catch { $null = $_ }
        Should -Invoke Restore-VmCleanState -Times 3 -Exactly
        Should -Invoke Complete-ExploreVmCleanup -Times 3 -Exactly
        Should -Invoke Disconnect-VMNetworkAdapter -Times 3 -Exactly
    }
    It 'odmawia pracy w trybie testów na mockach, zanim dotknie VM' {
        Mock Assert-DsoVmLiveAllowed { throw 'Tryb DSO_VM_TESTS_MOCK_ONLY' }
        { Invoke-InstallerVmRun -VmName 'DSO-26H2-Pro' -OutDirectory $TestDrive -Runs @(Get-InstallerPhaseRuns -Phase @('smoke') -Language @('pl')) -Artifact $script:Artifact } | Should -Throw '*DSO_VM_TESTS_MOCK_ONLY*'
        Should -Invoke Restore-VmCleanState -Times 0 -Exactly
    }
}

Describe 'Plik skryptu i sondy' {
    It 'parsuje się bez błędów, ma BOM UTF-8 i sekcje Instrukcja uruchomienia oraz Opis działania' {
        $Full = (Resolve-Path -LiteralPath $script:InstallerScriptPath).Path
        $Errors = $null
        $null = [System.Management.Automation.Language.Parser]::ParseFile($Full, [ref]$null, [ref]$Errors)
        @($Errors) | Should -HaveCount 0
        $Bytes = [System.IO.File]::ReadAllBytes($Full)
        ($Bytes[0..2] -join ',') | Should -Be '239,187,191'
        $Text = [System.IO.File]::ReadAllText($Full, [System.Text.Encoding]::UTF8)
        $Text | Should -Match 'Instrukcja uruchomienia'
        $Text | Should -Match 'Opis działania'
        $Text | Should -Match 'RISK:'
    }
    It 'sonda <Name>: tylko ASCII, parsuje się, odmawia poza VM' -ForEach @(
        @{ Name = 'Get-InstallerRefusalProbeScript' }
        @{ Name = 'Get-InstallerWizardProbeScript' }
        @{ Name = 'Get-InstallerWindowsProbeScript' }
    ) {
        $Text = & $Name
        $Text | Should -Not -Match '[^\x00-\x7F]'
        $Errors = $null
        $null = [System.Management.Automation.Language.Parser]::ParseInput($Text, [ref]$null, [ref]$Errors)
        @($Errors) | Should -HaveCount 0
        $Text | Should -Match 'DSO-26H2-HOME'
    }
    It 'sonda okna odmowy klika tylko przycisk OK, sonda okien niczego nie klika' {
        (Get-InstallerRefusalProbeScript) | Should -Match 'okButton'
        # UI Automation zawodzące w każdym przebiegu = błąd sondy (Ok=false), nie „brak okna”.
        (Get-InstallerRefusalProbeScript) | Should -Match '\$Failures -eq \$Passes\) \{ throw'
        (Get-InstallerWindowsProbeScript) | Should -Not -Match 'InvokePattern'
    }
}

AfterAll { Clear-NoLiveVm }
