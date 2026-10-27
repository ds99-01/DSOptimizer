BeforeDiscovery {
    $script:HasHyperV = [bool](Get-Module -ListAvailable -Name Hyper-V)
}

BeforeAll {
    # Wyłącznik: żaden test na mockach nie może dotknąć prawdziwego Hyper-V (patrz Support\NoLiveVm.ps1).
    . (Join-Path $PSScriptRoot 'Support\NoLiveVm.ps1')
    Import-Module (Join-Path $PSScriptRoot '..\modules\VmCommon.psm1') -Force
    Import-Module (Join-Path $PSScriptRoot '..\modules\VmHost.psm1') -Force
    # Skrypt jest dot-source'owany: przy '.' nie uruchamia przebiegu na VM, tylko definiuje funkcje.
    . (Join-Path $PSScriptRoot '..\explore\Invoke-E4CExploration.ps1')
    $script:E4CScriptPath = Join-Path $PSScriptRoot '..\explore\Invoke-E4CExploration.ps1'

    function script:Get-CommandOwner {
        param($Node)
        $Parent = $Node.Parent
        while ($null -ne $Parent -and $Parent -isnot [System.Management.Automation.Language.FunctionDefinitionAst]) { $Parent = $Parent.Parent }
        if ($null -ne $Parent) { $Parent.Name } else { '(poza funkcją)' }
    }

    function script:New-TestPlan {
        # Plan z katalogami w TestDrive (zapis żądań i wyników runnera bez dotykania C:\dso).
        $Plan = Get-E4CPlan
        $Plan.WorkDirectory = Join-Path $TestDrive 'e4c'
        $Plan.RawDirectory = Join-Path $TestDrive 'e4c\raw'
        $Plan.TaskPollSeconds = 1
        return ($Plan | ConvertTo-Json -Depth 6 | ConvertFrom-Json)
    }

    # Kody winget jako int ze znakiem (tak je zwraca Process.ExitCode).
    $script:NoApplicableInstaller = [BitConverter]::ToInt32([BitConverter]::GetBytes([uint32]2316632080), 0)   # 0x8A150010
    $script:SourceDataMissing = [BitConverter]::ToInt32([BitConverter]::GetBytes([uint32]2316632079), 0)       # 0x8A15000F
}

Describe 'Plan pomiaru (plan Tasku 2 Etapu 4c, spec pkt 4 i 8)' {
    It 'lista startowa = spec pkt 4: 33 pozycje w 8 kategoriach, identyfikatory dosłownie' {
        $Plan = Get-E4CPlan
        $Ids = @($Plan.Apps | ForEach-Object { $_.WingetId })
        $Ids.Count | Should -Be 33
        $Ids | Should -Be @(
            'Google.Chrome', 'Mozilla.Firefox', 'Brave.Brave', 'Opera.Opera',
            'Discord.Discord', 'Telegram.TelegramDesktop', 'OpenWhisperSystems.Signal', 'Zoom.Zoom', 'Microsoft.Teams',
            'VideoLAN.VLC', 'CodecGuide.K-LiteCodecPack.Standard', 'PeterPawlowski.foobar2000',
            'TheDocumentFoundation.LibreOffice', 'Adobe.Acrobat.Reader.64-bit', 'SumatraPDF.SumatraPDF',
            '7zip.7zip', 'Notepad++.Notepad++', 'voidtools.Everything', 'Microsoft.PowerToys', 'RARLab.WinRAR', 'ShareX.ShareX', 'CPUID.CPU-Z', 'CPUID.HWMonitor', 'TechPowerUp.GPU-Z',
            'AnyDesk.AnyDesk', 'TeamViewer.TeamViewer',
            'Valve.Steam', 'EpicGames.EpicGamesLauncher', 'GOG.Galaxy',
            'Microsoft.VCRedist.2015+.x64', 'Microsoft.VCRedist.2015+.x86', 'Microsoft.DotNet.DesktopRuntime.8', 'Microsoft.DotNet.DesktopRuntime.9')
        @($Plan.Apps | ForEach-Object { $_.Category } | Select-Object -Unique) | Should -Be @('browsers', 'messengers', 'multimedia', 'documents', 'tools', 'remote-help', 'games', 'runtimes')
        foreach ($Id in $Ids) { Test-E4CWingetId -Id $Id | Should -BeTrue -Because $Id }
    }
    It 'odrzucone ze spec dostają tylko show (dowód powodu), lokalizacja pl-PL, Firefox i LibreOffice z --locale, 7-Zip dla --no-upgrade' {
        $Plan = Get-E4CPlan
        @($Plan.RejectedShow) | Should -Be @('Spotify.Spotify', 'RustDesk.RustDesk', 'Microsoft.DirectX')
        $Plan.Locale | Should -Be 'pl-PL'
        @($Plan.LocaleProbe) | Should -Be @('Mozilla.Firefox', 'TheDocumentFoundation.LibreOffice')
        $Plan.NoUpgradeProbe | Should -Be '7zip.7zip'
        @($Plan.LongSearches) | Should -Be @('Microsoft Visual C++ Redistributable', 'codec pack', 'runtime desktop')
        $Plan.OfflineSearch | Should -Be 'vlc'
        $Plan.OfflineFreshInstall | Should -Be '7zip.7zip'
    }
    It 'limit instalacji 30 min (spec 5.4), kandydat spoza listy z wyszukiwania, konto dso, katalogi w C:\dso\e4c' {
        $Plan = Get-E4CPlan
        $Plan.InstallTimeoutSeconds | Should -Be 1800
        $Plan.CommandTimeoutSeconds | Should -BeGreaterOrEqual 120
        $Plan.SearchCandidate.Query | Should -Not -BeNullOrEmpty
        Test-E4CWingetId -Id $Plan.SearchCandidate.WingetId | Should -BeTrue
        @($Plan.Apps | ForEach-Object { $_.WingetId }) | Should -Not -Contain $Plan.SearchCandidate.WingetId
        @($Plan.Apps | ForEach-Object { $_.WingetId }) | Should -Not -Contain $Plan.FinalInstall
        $Plan.UserName | Should -Be 'dso'
        $Plan.WorkDirectory | Should -Be 'C:\dso\e4c'
        $Plan.RawDirectory | Should -Be 'C:\dso\e4c\raw'
    }
    It 'Get-E4CInstallIds: Pro = cała lista (środowiska uruchomieniowe najpierw), Home = po jednej z każdej kategorii, w tym Discord (per user)' {
        $Plan = Get-E4CPlan
        $Pro = @(Get-E4CInstallIds -VmName 'DSO-26H2-Pro' -Plan $Plan)
        $Pro.Count | Should -Be 33
        $Pro[0..3] | Should -Be @('Microsoft.VCRedist.2015+.x64', 'Microsoft.VCRedist.2015+.x86', 'Microsoft.DotNet.DesktopRuntime.8', 'Microsoft.DotNet.DesktopRuntime.9')
        $Pro[4] | Should -Be 'Google.Chrome'
        $Home1 = @(Get-E4CInstallIds -VmName 'DSO-26H2-Home' -Plan $Plan)
        $Home1 | Should -Contain 'Discord.Discord'
        $Categories = @($Home1 | ForEach-Object { $Id = $_; @($Plan.Apps | Where-Object { $_.WingetId -eq $Id })[0].Category } | Select-Object -Unique)
        foreach ($Category in 'browsers', 'messengers', 'multimedia', 'documents', 'tools', 'remote-help', 'games', 'runtimes') { $Categories | Should -Contain $Category }
        $Home1[0] | Should -Be 'Microsoft.VCRedist.2015+.x64'
    }
    It 'Get-E4CPhaseOrder: stała kolejność, baseline zawsze, adminprotection tylko Pro i przed final, bez sieci bez network/install-all' {
        $All = @('final', 'adminprotection', 'install-all', 'network', 'offline-fresh', 'baseline')
        @(Get-E4CPhaseOrder -Phase $All -VmName 'DSO-26H2-Pro') | Should -Be @('baseline', 'offline-fresh', 'network', 'install-all', 'adminprotection', 'final')
        @(Get-E4CPhaseOrder -Phase $All -VmName 'DSO-26H2-Home') | Should -Be @('baseline', 'offline-fresh', 'network', 'install-all', 'final')
        @(Get-E4CPhaseOrder -Phase @('final') -VmName 'DSO-26H2-Pro') | Should -Be @('baseline', 'final')
        @(Get-E4CPhaseOrder -Phase $All -VmName 'DSO-26H2-Pro' -Network $false) | Should -Be @('baseline', 'offline-fresh', 'adminprotection', 'final')
        @(Get-E4CNetworkPhases) | Should -Be @('network', 'install-all', 'cli-install')
    }
}

Describe 'Faza cli-install (Task 11: seria --install-apps przez CLI z siecią)' {
    BeforeAll {
        $script:AppsCatalog = Join-Path (Get-DsoRepoRoot) 'src\DSOptimizer.Catalog\catalog\apps.json'
        $script:AppsEntries = @((Get-Content -LiteralPath $script:AppsCatalog -Raw -Encoding UTF8 | ConvertFrom-Json).entries)
    }
    It 'plan: Pro = cała lista apps.json w kolejności pliku, Home = po jednej pozycji z każdej kategorii (w tym Discord) + winget:PuTTY.PuTTY (kandydat Tasku 2)' {
        $Plan = Get-E4CPlan
        $Pro = @(Get-E4CCliInstallIds -VmName 'DSO-26H2-Pro' -Plan $Plan -AppsCatalogPath $script:AppsCatalog)
        $Pro | Should -Be @($script:AppsEntries | ForEach-Object { [string]$_.id })
        $Pro.Count | Should -Be 35
        $Pro | Should -Contain 'install.rustdesk'
        $HomeIds = @(Get-E4CCliInstallIds -VmName 'DSO-26H2-Home' -Plan $Plan -AppsCatalogPath $script:AppsCatalog)
        $HomeIds[-1] | Should -BeExactly "winget:$($Plan.SearchCandidate.WingetId)"
        $HomeIds[-1] | Should -BeExactly 'winget:PuTTY.PuTTY'
        $HomeIds | Should -Contain 'install.discord'
        $ListIds = @($HomeIds | Where-Object { $_ -like 'install.*' })
        foreach ($Id in $ListIds) { @($script:AppsEntries | ForEach-Object { [string]$_.id }) | Should -Contain $Id }
        $Categories = @($ListIds | ForEach-Object { $Id = $_; [string]@($script:AppsEntries | Where-Object { $_.id -eq $Id })[0].category })
        @($Categories | Select-Object -Unique).Count | Should -Be 8
        $Categories.Count | Should -Be 8
        $Plan.CliInstallTimeoutSeconds | Should -Be 7200
        # Ścieżka exe jest tylko w liście dozwolonych narzędzi runnera ('dso' -> C:\dso\app\DSOptimizer.exe), nie w planie (jedno źródło prawdy).
        $Plan.Contains('CliExe') | Should -BeFalse
        $Plan.JournalPath | Should -Be 'C:\ProgramData\DSOptimizer\journal\journal.json'
        $Plan.DsoLogDirectory | Should -Be 'C:\ProgramData\DSOptimizer\logs'
    }
    It 'Get-E4CCliInstallIds: brak pliku listy albo pusta lista = wyjątek' {
        { Get-E4CCliInstallIds -VmName 'DSO-26H2-Pro' -Plan (Get-E4CPlan) -AppsCatalogPath (Join-Path $TestDrive 'brak.json') } | Should -Throw
        $Empty = Join-Path $TestDrive 'empty.json'
        Set-Content -LiteralPath $Empty -Value '{ "entries": [] }' -Encoding UTF8
        { Get-E4CCliInstallIds -VmName 'DSO-26H2-Pro' -Plan (Get-E4CPlan) -AppsCatalogPath $Empty } | Should -Throw '*pust*'
    }
    It 'Get-E4CCliArguments: --install-apps z id po przecinku i --out; --list-install z --out; złe id albo ścieżka = wyjątek' {
        Get-E4CCliArguments -Command 'install-apps' -AppIds @('install.vlc', 'winget:PuTTY.PuTTY') -OutPath 'C:\dso\e4c\raw\cli-install.json' |
            Should -BeExactly '--install-apps install.vlc,winget:PuTTY.PuTTY --out C:\dso\e4c\raw\cli-install.json'
        Get-E4CCliArguments -Command 'list-install' -OutPath 'C:\dso\e4c\raw\cli-list.json' | Should -BeExactly '--list-install --out C:\dso\e4c\raw\cli-list.json'
        { Get-E4CCliArguments -Command 'install-apps' -AppIds @() -OutPath 'C:\x\a.json' } | Should -Throw
        foreach ($Bad in 'install.a b', 'install.a,b', '', 'winget:Foo…', 'cmd.exe') { { Get-E4CCliArguments -Command 'install-apps' -AppIds @($Bad) -OutPath 'C:\x\a.json' } | Should -Throw -Because "'$Bad'" }
        { Get-E4CCliArguments -Command 'list-install' -AppIds @('install.vlc') -OutPath 'C:\x\a.json' } | Should -Throw
        { Get-E4CCliArguments -Command 'list-install' -OutPath 'C:\x y\a.json' } | Should -Throw
        { Get-E4CCliArguments -Command 'remove-app' -OutPath 'C:\x\a.json' } | Should -Throw
    }
    It 'Get-E4CPhaseOrder: cli-install tylko jawnie, po install-all i przed adminprotection; bez sieci wypada' {
        @(Get-E4CPhaseOrder -Phase @('cli-install') -VmName 'DSO-26H2-Pro') | Should -Be @('baseline', 'cli-install')
        @(Get-E4CPhaseOrder -Phase @('final', 'cli-install', 'install-all', 'adminprotection') -VmName 'DSO-26H2-Pro') | Should -Be @('baseline', 'install-all', 'cli-install', 'adminprotection', 'final')
        @(Get-E4CPhaseOrder -Phase @('cli-install') -VmName 'DSO-26H2-Home' -Network $false) | Should -Be @('baseline')
        $Default = (Get-Command $script:E4CScriptPath).ScriptBlock.Ast.ParamBlock.Parameters | Where-Object { $_.Name.VariablePath.UserPath -eq 'Phase' }
        $Default.DefaultValue.Extent.Text | Should -Not -Match 'cli-install'
        @($Default.Attributes | Where-Object { $_.TypeName.Name -eq 'ValidateSet' } | ForEach-Object { $_.PositionalArguments.Value }) | Should -Contain 'cli-install'
    }
    It 'Read-E4CJournalInstallEntries: tylko wpisy app-install (id, status, zdarzenia Started i wynik); brak pliku = pusta lista' {
        $Path = Join-Path $TestDrive 'journal.json'
        $Json = @'
{ "schemaVersion": 1, "machine": "DSO-26H2-PRO", "entries": [
  { "entryId": "1", "kind": "app-install", "title": "VLC", "tweakId": "install.vlc", "userSid": "S-1-5-21-1", "status": "Active", "appliedAt": "2026-10-24T20:00:00+00:00",
    "events": [ { "at": "2026-10-24T20:00:00+00:00", "action": "install-app", "outcome": "Started", "message": "a" }, { "at": "2026-10-24T20:01:00+00:00", "action": "install-app", "outcome": "Installed", "message": "b" } ] },
  { "entryId": "2", "kind": "app-removal", "tweakId": "win32:X", "status": "Active", "events": [] },
  { "entryId": "3", "kind": null, "tweakId": "ux.menu-delay", "status": "Active", "events": [] }
] }
'@
        [System.IO.File]::WriteAllText($Path, $Json, (New-Object System.Text.UTF8Encoding($false)))
        $R = @(Read-E4CJournalInstallEntries -Path $Path)
        $R.Count | Should -Be 1
        $R[0].TweakId | Should -Be 'install.vlc'
        $R[0].Status | Should -Be 'Active'
        @($R[0].Events | ForEach-Object { $_.Outcome }) | Should -Be @('Started', 'Installed')
        @(Read-E4CJournalInstallEntries -Path (Join-Path $TestDrive 'brak.json')).Count | Should -Be 0
    }
    It 'Read-E4CCliReport: raport JSON albo null, gdy pliku nie ma' {
        $Path = Join-Path $TestDrive 'r.json'
        [System.IO.File]::WriteAllText($Path, '{ "exitCode": 0, "installItems": [ { "id": "install.vlc", "outcome": "Installed" } ] }', (New-Object System.Text.UTF8Encoding($false)))
        (Read-E4CCliReport -Path $Path).installItems[0].outcome | Should -Be 'Installed'
        Read-E4CCliReport -Path (Join-Path $TestDrive 'brak.json') | Should -BeNullOrEmpty
    }
    It 'runner: narzędzie dso = pełna ścieżka C:\dso\app\DSOptimizer.exe; Invoke-E4CTool przyjmuje Tool dso' {
        Get-E4CRunnerScript | Should -Match ([regex]::Escape("'dso' { `$Path = 'C:\dso\app\DSOptimizer.exe' }"))
        $Attr = (Get-Command Invoke-E4CTool).Parameters['Tool'].Attributes | Where-Object { $_ -is [System.Management.Automation.ValidateSetAttribute] }
        $Attr.ValidValues | Should -Be @('winget', 'whoami', 'dso')
    }
    Context 'Invoke-E4CCliInstall (mocki: nic nie jest uruchamiane naprawdę)' {
        BeforeEach {
            Mock Assert-E4CInsideVm { }
            Mock Get-E4CFreeBytes { 1000 }
            Mock Test-E4CNetwork { [pscustomobject]@{ Name = 'x'; Resolved = $true; Addresses = @('1.2.3.4'); Seconds = 0 } }
            Mock Get-E4CInstallerProcesses { @() }
            $script:Calls = [System.Collections.Generic.List[string]]::new()
            $script:Tools = [System.Collections.Generic.List[object]]::new()
            Mock Invoke-E4CTool {
                $script:Calls.Add("$Tool $(($Arguments -split ' ')[0])")
                $script:Tools.Add([pscustomobject]@{ Tool = $Tool; Name = $Name; Arguments = $Arguments; TimeoutSeconds = $TimeoutSeconds; RunLevel = $RunLevel })
                [pscustomobject]@{ Name = $Name; Tool = $Tool; Arguments = $Arguments; ExitCode = 0; Hex = '0x00000000'; Seconds = 1; Output = [pscustomobject]@{ Lines = @('v1.11.510') } }
            }
            Mock Read-E4CCliReport { $script:Calls.Add("report $(Split-Path -Leaf $Path)"); [pscustomobject]@{ exitCode = 0; path = $Path } }
            Mock Read-E4CJournalInstallEntries { $script:Calls.Add('journal'); @([pscustomobject]@{ TweakId = 'install.vlc'; Status = 'Active'; Events = @() }) }
            $script:Snap = 0
            Mock Get-E4CUninstallSnapshot {
                $script:Snap++
                $script:Calls.Add("uninstall $script:Snap")
                if ($script:Snap -eq 1) { return @() }
                @([pscustomobject]@{ Branch = 'HKLM'; KeyPath = 'K1'; DisplayName = 'VLC media player'; Publisher = 'VideoLAN'; DisplayVersion = '3'; SystemComponent = $null; ParentKeyName = $null })
            }
            Mock Copy-E4CDsoLogs { $script:Calls.Add('logs'); @('journal.json') }
            Mock Get-E4CRustDeskState { $script:Calls.Add('rustdesk'); [pscustomobject]@{ DownloadsExists = $false; Downloads = @(); MsiLogs = @(); Service = $null; Firewall = @() } }
        }
        It 'kolejność: winget --version, Uninstall, --install-apps jako dso (zadanie Harmonogramu, Highest), raport, --list-install, raport, journal, Uninstall, odczyt RustDesk, winget --version, logi' {
            $Plan = New-TestPlan
            $Plan | Add-Member -NotePropertyName CliIds -NotePropertyValue @('install.vlc', 'winget:PuTTY.PuTTY') -Force
            $R = Invoke-E4CCliInstall -Plan $Plan -UserSid 'S-1-5-21-1'
            @($script:Calls) | Should -Be @('winget --version', 'uninstall 1', 'dso --install-apps', 'report cli-install.json', 'dso --list-install', 'report cli-list.json', 'journal', 'uninstall 2', 'rustdesk', 'winget --version', 'logs')
            # Bez -CliRepeat jedno --install-apps (plan domyślny: CliRepeat = false).
            $Plan.CliRepeat | Should -BeFalse
            $R.Contains('InstallAgain') | Should -BeFalse
            $R.RustDesk.DownloadsExists | Should -BeFalse
            $Install = @($script:Tools | Where-Object { $_.Arguments -like '--install-apps*' })[0]
            $Install.Tool | Should -Be 'dso'
            $Install.Arguments | Should -BeExactly ('--install-apps install.vlc,winget:PuTTY.PuTTY --out ' + (Join-Path $Plan.RawDirectory 'cli-install.json'))
            $Install.TimeoutSeconds | Should -Be 7200
            # Minor T11: RunLevel przekazany jawnie (nie domyślny parametr Invoke-E4CTool) – każde wywołanie jako dso z Highest.
            @($script:Tools | ForEach-Object { $_.RunLevel }) | Should -Be @('Highest', 'Highest', 'Highest', 'Highest')
            @($script:Tools | ForEach-Object { $_.Name }) | Should -Be @('cli-version-before', 'cli-install', 'cli-list', 'cli-version-after')
            @($R.Visible | ForEach-Object { $_.DisplayName }) | Should -Be @('VLC media player')
            $R.Journal[0].TweakId | Should -Be 'install.vlc'
            $R.InstallReport.path | Should -Match 'cli-install\.json$'
            $R.ListReport.path | Should -Match 'cli-list\.json$'
            $R.Connectivity.Resolved | Should -BeTrue
        }
        It '-CliRepeat: po --list-install drugie --install-apps z tymi samymi id do cli-install-again.json (jako dso, Highest, ten sam limit)' {
            $Plan = New-TestPlan
            $Plan | Add-Member -NotePropertyName CliIds -NotePropertyValue @('install.7zip', 'install.rustdesk') -Force
            $Plan.CliRepeat = $true
            $R = Invoke-E4CCliInstall -Plan $Plan -UserSid 'S-1-5-21-1'
            @($script:Calls) | Should -Be @('winget --version', 'uninstall 1', 'dso --install-apps', 'report cli-install.json', 'dso --list-install', 'report cli-list.json',
                'dso --install-apps', 'report cli-install-again.json', 'journal', 'uninstall 2', 'rustdesk', 'winget --version', 'logs')
            @($script:Tools | ForEach-Object { $_.Name }) | Should -Be @('cli-version-before', 'cli-install', 'cli-list', 'cli-install-again', 'cli-version-after')
            $Again = @($script:Tools | Where-Object { $_.Name -eq 'cli-install-again' })[0]
            $Again.Tool | Should -Be 'dso'
            $Again.Arguments | Should -BeExactly ('--install-apps install.7zip,install.rustdesk --out ' + (Join-Path $Plan.RawDirectory 'cli-install-again.json'))
            $Again.TimeoutSeconds | Should -Be 7200
            $Again.RunLevel | Should -Be 'Highest'
            $R.InstallAgainReport.path | Should -Match 'cli-install-again\.json$'
            $R.InstallAgain.Name | Should -Be 'cli-install-again'
        }
        It 'brak sieci (DNS) = nic nie jest uruchamiane, wyjątek' {
            Mock Test-E4CNetwork { [pscustomobject]@{ Resolved = $false; Addresses = @(); Seconds = 300 } }
            $Plan = New-TestPlan
            $Plan | Add-Member -NotePropertyName CliIds -NotePropertyValue @('install.vlc') -Force
            { Invoke-E4CCliInstall -Plan $Plan -UserSid 'S-1-5-21-1' } | Should -Throw '*sieci*'
            Should -Invoke Invoke-E4CTool -Times 0 -Exactly
        }
        It 'pusta lista id = wyjątek przed uruchomieniem czegokolwiek' {
            { Invoke-E4CCliInstall -Plan (New-TestPlan) -UserSid 'S-1-5-21-1' } | Should -Throw '*CliIds*'
            Should -Invoke Invoke-E4CTool -Times 0 -Exactly
        }
    }
}

Describe 'Plan RustDesk (Task 6): -CliIds, -CliRepeat i odczyty RustDesk w fazie cli-install' {
    It 'parametry skryptu: -CliIds [string[]] i -CliRepeat [switch]; plan: CliRepeat = false, folder pobrań programu' {
        $Parameters = (Get-Command $script:E4CScriptPath).Parameters
        $Parameters['CliIds'].ParameterType | Should -Be ([string[]])
        $Parameters['CliRepeat'].ParameterType | Should -Be ([switch])
        $Plan = Get-E4CPlan
        $Plan.CliRepeat | Should -BeFalse
        $Plan.DownloadsDirectory | Should -Be 'C:\ProgramData\DSOptimizer\downloads'
    }
    It 'Test-E4CCliId: install.nazwa albo winget:pełny-id; spacja, przecinek, wielkie litery, puste = odmowa' {
        foreach ($Good in 'install.rustdesk', 'install.7zip', 'install.k-lite', 'winget:PuTTY.PuTTY') { Test-E4CCliId -Id $Good | Should -BeTrue -Because $Good }
        foreach ($Bad in 'install.a b', 'install.7zip,install.vlc', 'install.RustDesk', 'Install.rustdesk', 'install.', '', 'winget:', 'winget:Foo…', 'winget:a b', 'cmd.exe') {
            Test-E4CCliId -Id $Bad | Should -BeFalse -Because "'$Bad'"
        }
        Test-E4CCliId -Id ("install.rustdesk" + [char]10) | Should -BeFalse
        Test-E4CCliId -Id $null | Should -BeFalse
    }
    It 'Get-E4CRunArguments: -CliIds i -CliRepeat trafiają do argumentów przebiegu; złe id (np. ze spacją) = wyjątek' {
        $Arguments = Get-E4CRunArguments -VmName @('DSO-26H2-Pro') -Phase @('cli-install') -OutDirectory 'C:\w' -CheckpointName 'c' -ReadyTimeoutSeconds 60 -CliIds @('install.7zip', 'install.rustdesk') -CliRepeat $true
        @($Arguments.CliIds) | Should -Be @('install.7zip', 'install.rustdesk')
        $Arguments.CliRepeat | Should -BeTrue
        $Default = Get-E4CRunArguments -VmName @('DSO-26H2-Home') -Phase @('cli-install') -OutDirectory 'C:\w' -CheckpointName 'c' -ReadyTimeoutSeconds 60
        @($Default.CliIds).Count | Should -Be 0
        $Default.CliRepeat | Should -BeFalse
        { Get-E4CRunArguments -VmName @('DSO-26H2-Home') -Phase @('cli-install') -OutDirectory 'C:\w' -CheckpointName 'c' -ReadyTimeoutSeconds 60 -CliIds @('install.rustdesk', 'install.a b') } |
            Should -Throw '*install.a b*'
        { Get-E4CRunArguments -VmName @('DSO-26H2-Home') -Phase @('cli-install') -OutDirectory 'C:\w' -CheckpointName 'c' -ReadyTimeoutSeconds 60 -CliIds @('') } | Should -Throw
    }
    Context 'Get-E4CRustDeskState (tylko odczyt; mocki usługi i zapory)' {
        BeforeEach {
            # TestDrive żyje przez cały Context, więc każdy test dostaje własne foldery.
            $script:Plan = New-TestPlan
            $Own = Join-Path $TestDrive ([guid]::NewGuid().ToString('N'))
            $script:Plan.DownloadsDirectory = Join-Path $Own 'downloads'
            $script:Plan.DsoLogDirectory = Join-Path $Own 'logs'
            $null = New-Item -ItemType Directory -Path (Join-Path $script:Plan.DsoLogDirectory 'winget') -Force
            Set-Content -LiteralPath (Join-Path $script:Plan.DsoLogDirectory 'winget\rustdesk-msi-20261024-101500.log') -Value 'msi' -Encoding UTF8
            Set-Content -LiteralPath (Join-Path $script:Plan.DsoLogDirectory 'winget\7zip.7zip-20261024-101400.log') -Value 'winget' -Encoding UTF8
        }
        It 'zainstalowany RustDesk: pliki w folderze pobrań, logi MSI RustDesk (bez logów winget), usługa i reguły zapory' {
            $null = New-Item -ItemType Directory -Path $script:Plan.DownloadsDirectory -Force
            Set-Content -LiteralPath (Join-Path $script:Plan.DownloadsDirectory 'a.msi') -Value 'x' -Encoding UTF8
            Mock Get-Service { [pscustomobject]@{ Name = 'RustDesk'; DisplayName = 'RustDesk Service'; Status = 'Running'; StartType = 'Automatic' } } -ParameterFilter { $Name -eq 'RustDesk' }
            Mock Get-NetFirewallRule { @([pscustomobject]@{ DisplayName = 'RustDesk Service'; Direction = 'Inbound'; Action = 'Allow'; Enabled = 'True' }, [pscustomobject]@{ DisplayName = 'RustDesk Service'; Direction = 'Outbound'; Action = 'Allow'; Enabled = 'True' }) } -ParameterFilter { $DisplayName -eq 'RustDesk*' }
            $R = Get-E4CRustDeskState -Plan $script:Plan
            $R.DownloadsExists | Should -BeTrue
            @($R.Downloads | ForEach-Object { $_.Name }) | Should -Be @('a.msi')
            @($R.MsiLogs | ForEach-Object { $_.Name }) | Should -Be @('rustdesk-msi-20261024-101500.log')
            $R.Service.Status | Should -Be 'Running'
            $R.Service.StartType | Should -Be 'Automatic'
            @($R.Firewall | ForEach-Object { "$($_.DisplayName)|$($_.Direction)|$($_.Action)" }) | Should -Be @('RustDesk Service|Inbound|Allow', 'RustDesk Service|Outbound|Allow')
            Should -Invoke Get-NetFirewallRule -Times 1 -Exactly -ParameterFilter { $DisplayName -eq 'RustDesk*' }
        }
        It 'bez RustDesk: folder pobrań nieobecny, brak usługi i reguł = puste wyniki bez błędu' {
            Mock Get-Service { } -ParameterFilter { $Name -eq 'RustDesk' }
            Mock Get-NetFirewallRule { }
            Remove-Item -LiteralPath (Join-Path $script:Plan.DsoLogDirectory 'winget\rustdesk-msi-20261024-101500.log')
            $R = Get-E4CRustDeskState -Plan $script:Plan
            $R.DownloadsExists | Should -BeFalse
            @($R.Downloads).Count | Should -Be 0
            @($R.MsiLogs).Count | Should -Be 0
            $R.Service | Should -BeNullOrEmpty
            @($R.Firewall).Count | Should -Be 0
            @($R.Errors).Count | Should -Be 0
        }
        It 'błąd odczytu zapory nie przerywa: zapisany w Errors, reszta odczytana' {
            Mock Get-Service { } -ParameterFilter { $Name -eq 'RustDesk' }
            Mock Get-NetFirewallRule { throw 'CIM padł' }
            $R = Get-E4CRustDeskState -Plan $script:Plan
            @($R.MsiLogs).Count | Should -Be 1
            ($R.Errors -join ' ') | Should -Match 'CIM padł'
        }
    }
}

Describe 'Polecenia winget (Global Constraints, spec 5.1 i 7)' {
    It 'install: pełne polecenie w stałej kolejności, z --locale i bez' {
        Get-E4CInstallArguments -WingetId '7zip.7zip' -Locale 'pl-PL' -LogPath 'C:\dso\e4c\raw\a.log' |
            Should -BeExactly 'install --id 7zip.7zip --exact --source winget --silent --no-upgrade --locale pl-PL --accept-package-agreements --accept-source-agreements --disable-interactivity --log C:\dso\e4c\raw\a.log'
        Get-E4CInstallArguments -WingetId 'Notepad++.Notepad++' -Locale '' -LogPath 'C:\dso\e4c\raw\b.log' |
            Should -BeExactly 'install --id Notepad++.Notepad++ --exact --source winget --silent --no-upgrade --accept-package-agreements --accept-source-agreements --disable-interactivity --log C:\dso\e4c\raw\b.log'
    }
    It 'install: nigdy --force, --ignore-security-hash, --scope ani --no-progress; zawsze --no-upgrade i --source winget' {
        foreach ($Locale in 'pl-PL', '') {
            $Text = Get-E4CInstallArguments -WingetId 'Microsoft.VCRedist.2015+.x64' -Locale $Locale -LogPath 'C:\x\a.log'
            foreach ($Bad in '--force', '--ignore-security-hash', '--scope', '--no-progress', 'msstore') { $Text | Should -Not -Match ([regex]::Escape($Bad)) }
            $Text | Should -Match '--no-upgrade'
            $Text | Should -Match '--source winget'
        }
    }
    It 'install: zły identyfikator, zła lokalizacja albo ścieżka logu ze spacją lub cudzysłowem = wyjątek' {
        foreach ($Bad in 'Foo.Bar…', '', 'a b', '-x', 'Foo"Bar') { { Get-E4CInstallArguments -WingetId $Bad -Locale '' -LogPath 'C:\x\a.log' } | Should -Throw }
        { Get-E4CInstallArguments -WingetId '7zip.7zip' -Locale 'pl PL' -LogPath 'C:\x\a.log' } | Should -Throw
        { Get-E4CInstallArguments -WingetId '7zip.7zip' -Locale '' -LogPath 'C:\x y\a.log' } | Should -Throw
        { Get-E4CInstallArguments -WingetId '7zip.7zip' -Locale '' -LogPath 'C:\x"\a.log' } | Should -Throw
    }
    It 'search: cytowanie zapytania ze spacją, --count 50; cudzysłów i znaki sterujące odrzucane' {
        Get-E4CSearchArguments -Query 'codec pack' | Should -BeExactly 'search --query "codec pack" --source winget --count 50 --accept-source-agreements --disable-interactivity'
        Get-E4CSearchArguments -Query 'vlc' | Should -BeExactly 'search --query vlc --source winget --count 50 --accept-source-agreements --disable-interactivity'
        foreach ($Bad in 'a"b', "a$([char]7)b", '', '   ') { { Get-E4CSearchArguments -Query $Bad } | Should -Throw }
    }
    It 'list/show: --exact, list w dwóch wariantach (z --source winget jak program i bez), show z --source winget' {
        Get-E4CListArguments -WingetId '7zip.7zip' -WithSource $true | Should -BeExactly 'list --id 7zip.7zip --exact --source winget --accept-source-agreements --disable-interactivity'
        Get-E4CListArguments -WingetId '7zip.7zip' -WithSource $false | Should -BeExactly 'list --id 7zip.7zip --exact --accept-source-agreements --disable-interactivity'
        Get-E4CShowArguments -WingetId 'VideoLAN.VLC' | Should -BeExactly 'show --id VideoLAN.VLC --exact --source winget --accept-source-agreements --disable-interactivity'
    }
    It 'Test-E4CWingetId i Get-E4CSafeName' {
        Test-E4CWingetId -Id 'CodecGuide.K-LiteCodecPack.Standard' | Should -BeTrue
        Test-E4CWingetId -Id 'Foo.Bar…' | Should -BeFalse
        Test-E4CWingetId -Id '' | Should -BeFalse
        Get-E4CSafeName -Text 'net-install-Notepad++.Notepad++' | Should -Be 'net-install-Notepad__.Notepad__'
        Get-E4CSafeName -Text 'a b/c' | Should -Be 'a_b_c'
    }
}

Describe 'Analiza wyjścia (czysta logika)' {
    It 'ConvertFrom-E4COutput: spinner, czyszczenie linii i pasek odfiltrowane, linie tekstu zostają, liczniki \r' {
        $Text = "`r   - `r   \ `r" + (' ' * 120) + "`rFound VLC media player [VideoLAN.VLC] Version 3.0.24`r`nDownloading https://x/vlc.exe`r`n  " + [string][char]0x2588 + [string][char]0x2592 + "  1.00 MB / 44.1 MB`r  45%`r`nSuccessfully installed`r`n"
        $Out = ConvertFrom-E4COutput -Bytes ([System.Text.Encoding]::UTF8.GetBytes($Text)) -CodePage 65001
        @($Out.Lines) | Should -Be @('Found VLC media player [VideoLAN.VLC] Version 3.0.24', 'Downloading https://x/vlc.exe', 'Successfully installed')
        $Out.ProgressSegments | Should -BeGreaterOrEqual 4
        $Out.CrOnly | Should -BeGreaterThan 0
        $Out.Length | Should -BeGreaterThan 0
        (ConvertFrom-E4COutput -Bytes ([byte[]]@()) -CodePage 65001).Lines.Count | Should -Be 0
    }
    It 'Test-E4CProgressSegment' {
        foreach ($Yes in '   - ', ' \ ', '|', '/', '', ("  " + [char]0x2588 + "  1 MB / 2 MB"), '  45%', '512 KB / 1.00 MB') { Test-E4CProgressSegment -Text $Yes | Should -BeTrue -Because "'$Yes'" }
        foreach ($No in 'Successfully installed', 'Found 7-Zip [7zip.7zip] Version 26.05', '-----', 'Name  Id  Version') { Test-E4CProgressSegment -Text $No | Should -BeFalse -Because "'$No'" }
    }
    It 'Get-E4CShowFields: nazwa, Id, wersja, wydawca, typ i lokalizacja instalatora (wcięte linie sekcji Installer)' {
        $Lines = @('Found VLC media player [VideoLAN.VLC]', 'Version: 3.0.24', 'Publisher: VideoLAN', 'Installer:', '  Installer Type: nullsoft', '  Installer Locale: pl-PL', '  Installer Url: https://download.videolan.org/x.exe', '  Installer SHA256: d7')
        $F = Get-E4CShowFields -Lines $Lines
        $F.Name | Should -Be 'VLC media player'
        $F.Id | Should -Be 'VideoLAN.VLC'
        $F.Version | Should -Be '3.0.24'
        $F.Publisher | Should -Be 'VideoLAN'
        $F.InstallerType | Should -Be 'nullsoft'
        $F.InstallerLocale | Should -Be 'pl-PL'
        $F.InstallerUrl | Should -Be 'https://download.videolan.org/x.exe'
        (Get-E4CShowFields -Lines @('No package found matching input criteria.')).Id | Should -BeNullOrEmpty
    }
    It 'Get-E4CSearchSummary: nagłówek nad linią kresek, wiersze, wiersze z „…” policzone osobno' {
        $Ell = [string][char]0x2026
        $Lines = @('Name                                   Id                                  Version  Source', ('-' * 90),
            "Microsoft Visual C++ 2015-2022 Redist$Ell Microsoft.VCRedist.2015+.x64        14.44    winget",
            "Microsoft Visual C++ Redistributable$Ell  Microsoft.VCRedist.2015+.x86        14.44    winget",
            "Very long name that pushes columns    Vendor.VeryLongPackageIdentifier$Ell 1.0      winget")
        $S = Get-E4CSearchSummary -Lines $Lines
        $S.Header | Should -Match '^Name'
        $S.RowCount | Should -Be 3
        $S.TruncatedNameRows | Should -Be 2
        $S.TruncatedIdRows | Should -Be 1
        @($S.Rows).Count | Should -Be 3
        $S.ColumnStarts | Should -Be @(0, 39, 75, 84)
        (Get-E4CSearchSummary -Lines @('An unexpected error occurred')).RowCount | Should -Be 0
    }
    It 'Compare-E4CSnapshot: dodane, usunięte i zmienione po kluczu' {
        $Before = @([pscustomobject]@{ KeyPath = 'A'; DisplayName = 'a'; DisplayVersion = '1' }, [pscustomobject]@{ KeyPath = 'B'; DisplayName = 'b'; DisplayVersion = '1' })
        $After = @([pscustomobject]@{ KeyPath = 'B'; DisplayName = 'b'; DisplayVersion = '2' }, [pscustomobject]@{ KeyPath = 'C'; DisplayName = 'c'; DisplayVersion = '1' })
        $D = Compare-E4CSnapshot -Before $Before -After $After -KeyProperty 'KeyPath' -Properties @('DisplayName', 'DisplayVersion')
        @($D.Added | ForEach-Object { $_.KeyPath }) | Should -Be @('C')
        @($D.Removed | ForEach-Object { $_.KeyPath }) | Should -Be @('A')
        @($D.Changed | ForEach-Object { $_.KeyPath }) | Should -Be @('B')
        $E = Compare-E4CSnapshot -Before @() -After @() -KeyProperty 'KeyPath' -Properties @('DisplayName')
        @($E.Added).Count | Should -Be 0
    }
    It 'Format-E4CHex: int ze znakiem -> 0x8A150010; null -> null' {
        Format-E4CHex -Code $script:NoApplicableInstaller | Should -Be '0x8A150010'
        Format-E4CHex -Code 0 | Should -Be '0x00000000'
        Format-E4CHex -Code $null | Should -BeNullOrEmpty
    }
}

Describe 'Bezpieczniki (VM-only, mock-only)' {
    It 'Test-E4CVmComputerName: tylko DSO-26H2-HOME/PRO' {
        Test-E4CVmComputerName -ComputerName 'dso-26h2-pro' | Should -BeTrue
        Test-E4CVmComputerName -ComputerName 'DSO-26H2-HOME' | Should -BeTrue
        Test-E4CVmComputerName -ComputerName 'BRUTEFORCE_LTSC' | Should -BeFalse
        Test-E4CVmComputerName -ComputerName '' | Should -BeFalse
    }
    It 'Assert-E4CInsideVm rzuca w trybie mock-only, nawet z nazwą VM; poza trybem rzuca na hoście' {
        { Assert-E4CInsideVm -ComputerName 'DSO-26H2-PRO' } | Should -Throw '*DSO_VM_TESTS_MOCK_ONLY*'
        $Saved = $env:DSO_VM_TESTS_MOCK_ONLY
        try {
            [Environment]::SetEnvironmentVariable('DSO_VM_TESTS_MOCK_ONLY', $null)
            { Assert-E4CInsideVm -ComputerName 'HOST-PC' } | Should -Throw '*tylko w VM*'
            { Assert-E4CInsideVm -ComputerName 'DSO-26H2-HOME' } | Should -Not -Throw
        }
        finally { $env:DSO_VM_TESTS_MOCK_ONLY = $Saved }
    }
    It '<Name> rzuca przed jakimkolwiek skutkiem (zadanie Harmonogramu, winget, rejestr, restart)' -ForEach @(
        @{ Name = 'Install-E4CRunner'; Code = { Install-E4CRunner -Plan (New-TestPlan) } }
        @{ Name = 'Invoke-E4CTool'; Code = { Invoke-E4CTool -Plan (New-TestPlan) -Name 'x' -Tool 'winget' -Arguments '--version' -TimeoutSeconds 10 } }
        @{ Name = 'Invoke-E4CInstallItem'; Code = { Invoke-E4CInstallItem -Plan (New-TestPlan) -UserSid 'S-1-5-21-1' -WingetId '7zip.7zip' -Label 'x' } }
        @{ Name = 'Set-E4CAdminApprovalMode'; Code = { Set-E4CAdminApprovalMode -Value 2 } }
        @{ Name = 'Restart-E4CVm'; Code = { Restart-E4CVm } }
        @{ Name = 'Invoke-E4CBaseline'; Code = { Invoke-E4CBaseline -Plan (New-TestPlan) -UserSid 'S-1-5-21-1' } }
        @{ Name = 'Invoke-E4COfflineFresh'; Code = { Invoke-E4COfflineFresh -Plan (New-TestPlan) -UserSid 'S-1-5-21-1' } }
        @{ Name = 'Invoke-E4CNetworkProbe'; Code = { Invoke-E4CNetworkProbe -Plan (New-TestPlan) -UserSid 'S-1-5-21-1' } }
        @{ Name = 'Invoke-E4CInstallAll'; Code = { Invoke-E4CInstallAll -Plan (New-TestPlan) -UserSid 'S-1-5-21-1' } }
        @{ Name = 'Invoke-E4CAdminProtectionSet'; Code = { Invoke-E4CAdminProtectionSet -Plan (New-TestPlan) } }
        @{ Name = 'Invoke-E4CAdminProtectionProbe'; Code = { Invoke-E4CAdminProtectionProbe -Plan (New-TestPlan) -UserSid 'S-1-5-21-1' } }
        @{ Name = 'Invoke-E4CAdminProtectionRestore'; Code = { Invoke-E4CAdminProtectionRestore -Plan (New-TestPlan) } }
        @{ Name = 'Invoke-E4CFinal'; Code = { Invoke-E4CFinal -Plan (New-TestPlan) -UserSid 'S-1-5-21-1' } }
        @{ Name = 'Invoke-E4CCliInstall'; Code = { Invoke-E4CCliInstall -Plan (New-TestPlan) -UserSid 'S-1-5-21-1' } }
        @{ Name = 'Copy-E4CDsoLogs'; Code = { Copy-E4CDsoLogs -Plan (New-TestPlan) } }
        @{ Name = 'Invoke-E4CCollection'; Code = { Invoke-E4CCollection -PlanJson '{}' -Phase 'baseline' -UserSid 'S-1-5-21-1' } }
    ) {
        Mock Register-ScheduledTask { throw 'nie wolno' }
        Mock Start-ScheduledTask { throw 'nie wolno' }
        Mock Restart-Computer { throw 'nie wolno' }
        if ($Name -ne 'Invoke-E4CTool') { Mock Invoke-E4CTool { throw 'nie wolno' } }
        { & $Code } | Should -Throw '*DSO_VM_TESTS_MOCK_ONLY*'
        foreach ($Command in 'Register-ScheduledTask', 'Start-ScheduledTask', 'Restart-Computer') { Should -Invoke $Command -Times 0 -Exactly }
        if ($Name -ne 'Invoke-E4CTool') { Should -Invoke Invoke-E4CTool -Times 0 -Exactly }
        Test-Path -LiteralPath (Join-Path $TestDrive 'e4c') | Should -BeFalse
    }
    It 'każda funkcja wysyłana do VM ze skutkiem (wprost albo przez inną taką funkcję) zaczyna się od Assert-E4CInsideVm' {
        $EffectCommands = @('Set-Content', 'New-Item', 'Remove-Item', 'Move-Item', 'Copy-Item', 'Register-ScheduledTask', 'Start-ScheduledTask', 'Stop-ScheduledTask', 'Unregister-ScheduledTask', 'Restart-Computer')
        $EffectMembers = @('Kill', 'Start', 'Delete', 'WriteAllBytes', 'WriteAllText', 'Create', 'CreateDirectory', 'SetValue', 'DeleteValue', 'Write', 'CopyToAsync')
        $Ast = [System.Management.Automation.Language.Parser]::ParseFile((Resolve-Path $script:E4CScriptPath), [ref]$null, [ref]$null)
        $Remote = @(Get-E4CRemoteFunctionNames)
        $Functions = @{}
        foreach ($Fn in $Ast.FindAll({ param($n) $n -is [System.Management.Automation.Language.FunctionDefinitionAst] }, $true)) { if ($Remote -contains $Fn.Name) { $Functions[$Fn.Name] = $Fn } }
        $Calls = @{}
        $Effect = @{}
        foreach ($Name in $Functions.Keys) {
            $Body = $Functions[$Name].Body
            $Commands = @($Body.FindAll({ param($n) $n -is [System.Management.Automation.Language.CommandAst] }, $true) | ForEach-Object { $_.GetCommandName() } | Where-Object { $_ })
            $Members = @($Body.FindAll({ param($n) $n -is [System.Management.Automation.Language.InvokeMemberExpressionAst] }, $true) | ForEach-Object { $_.Member.Extent.Text })
            $Calls[$Name] = @($Commands | Where-Object { $Functions.ContainsKey($_) -and $_ -ne $Name })
            $Effect[$Name] = (@($Commands | Where-Object { $EffectCommands -contains $_ }).Count + @($Members | Where-Object { $EffectMembers -contains $_ }).Count) -gt 0
        }
        do {
            $Changed = $false
            foreach ($Name in $Functions.Keys) {
                if (-not $Effect[$Name] -and @($Calls[$Name] | Where-Object { $Effect[$_] -and $_ -ne 'Assert-E4CInsideVm' }).Count -gt 0) { $Effect[$Name] = $true; $Changed = $true }
            }
        } while ($Changed)
        $Guarded = @($Functions.Keys | Where-Object { $Effect[$_] } | Sort-Object)
        foreach ($Name in 'Install-E4CRunner', 'Invoke-E4CTool', 'Invoke-E4CInstallItem', 'Set-E4CAdminApprovalMode', 'Restart-E4CVm', 'Invoke-E4CBaseline', 'Invoke-E4COfflineFresh', 'Invoke-E4CNetworkProbe',
            'Invoke-E4CInstallAll', 'Invoke-E4CAdminProtectionSet', 'Invoke-E4CAdminProtectionProbe', 'Invoke-E4CAdminProtectionRestore', 'Invoke-E4CFinal', 'Invoke-E4CCliInstall', 'Copy-E4CDsoLogs', 'Invoke-E4CCollection') {
            $Guarded | Should -Contain $Name
        }
        foreach ($Name in $Guarded) {
            $First = @($Functions[$Name].Body.EndBlock.Statements)[0]
            $First.Extent.Text | Should -Match '^Assert-E4CInsideVm\b' -Because "$Name ma skutek, więc pierwsza instrukcja to bezpiecznik"
        }
    }
    It 'runner zadania Harmonogramu: poza VM (i w trybie mock-only) odmawia przed startem procesu i zapisuje wynik z błędem' {
        $Dir = Join-Path $TestDrive 'runner'
        $null = New-Item -ItemType Directory -Path $Dir -Force
        $Runner = Join-Path $Dir 'runner.ps1'
        [System.IO.File]::WriteAllText($Runner, (Get-E4CRunnerScript), (New-Object System.Text.UTF8Encoding($true)))
        $Request = Join-Path $Dir 'req.json'
        $ResultFile = Join-Path $Dir 'x.result.json'
        [ordered]@{ Name = 'x'; Tool = 'winget'; Arguments = '--version'; TimeoutSeconds = 5; OutFile = (Join-Path $Dir 'x.out.bin'); ErrFile = (Join-Path $Dir 'x.err.bin'); ResultFile = $ResultFile } |
            ConvertTo-Json | Set-Content -LiteralPath $Request -Encoding UTF8
        & $Runner -Request $Request
        $Result = Get-Content -LiteralPath $ResultFile -Raw -Encoding UTF8 | ConvertFrom-Json
        $Result.Error | Should -Match 'tylko w VM|DSO_VM_TESTS_MOCK_ONLY'
        $Result.ExitCode | Should -BeNullOrEmpty
        $Result.FilePath | Should -BeNullOrEmpty
        Test-Path -LiteralPath (Join-Path $Dir 'x.out.bin') | Should -BeFalse
    }
    It 'runner: winget z aliasu profilu konta procesu (LOCALAPPDATA), whoami z System32; bez Kill, bez PATH, bez ShellExecute' {
        $Text = Get-E4CRunnerScript
        $Errors = $null
        $null = [System.Management.Automation.Language.Parser]::ParseInput($Text, [ref]$null, [ref]$Errors)
        @($Errors).Count | Should -Be 0
        $Text | Should -Match ([regex]::Escape("Join-Path `$env:LOCALAPPDATA 'Microsoft\WindowsApps\winget.exe'"))
        $Text | Should -Match ([regex]::Escape("Join-Path `$env:SystemRoot 'System32\whoami.exe'"))
        $Text | Should -Match 'UseShellExecute = \$false'
        $Text | Should -Not -Match '\.Kill\(|Stop-Process|taskkill|Start-Process|\$env:PATH'
    }
}

Describe 'Sondy w VM (mocki: nic nie jest uruchamiane naprawdę)' {
    BeforeEach {
        Mock Assert-E4CInsideVm { }
        Mock Start-Sleep { }
        Mock Get-E4CUninstallSnapshot { @() }
        Mock Get-E4CAppxSnapshot { @() }
        Mock Get-E4CFreeBytes { 1000 }
        Mock Test-E4CNetwork { [pscustomobject]@{ Name = 'x'; Resolved = $true; Addresses = @('1.2.3.4'); Seconds = 0 } }
    }
    It 'Invoke-E4CTool: żądanie w pliku, zadanie jako dso (Interactive, Highest), bez limitu czasu zadania, wynik z runnera, zadanie usunięte' {
        $Plan = New-TestPlan
        $script:Principal = $null
        $script:SettingsLimit = $null
        Mock New-ScheduledTaskAction { [pscustomobject]@{ Execute = $Execute; Argument = $Argument } }
        Mock New-ScheduledTaskPrincipal { $script:Principal = [pscustomobject]@{ UserId = $UserId; LogonType = [string]$LogonType; RunLevel = [string]$RunLevel }; $script:Principal }
        Mock New-ScheduledTaskSettingsSet { $script:SettingsLimit = $ExecutionTimeLimit; [pscustomobject]@{} }
        Mock Register-ScheduledTask -RemoveParameterType 'Action', 'Principal', 'Settings' { [pscustomobject]@{ TaskName = $TaskName } }
        Mock Start-ScheduledTask {
            $Req = Get-Content -LiteralPath (Join-Path $Plan.WorkDirectory 'req\net-version.json') -Raw -Encoding UTF8 | ConvertFrom-Json
            [System.IO.File]::WriteAllBytes($Req.OutFile, [System.Text.Encoding]::UTF8.GetBytes("v1.11.510`r`n"))
            [System.IO.File]::WriteAllBytes($Req.ErrFile, [byte[]]@())
            [ordered]@{ Name = $Req.Name; ExitCode = 0; TimedOut = $false; Seconds = 0.5; Sid = 'S-1-5-21-1'; Account = 'VM\dso'; Elevated = $true; FilePath = 'C:\Users\dso\AppData\Local\Microsoft\WindowsApps\winget.exe'; Error = $null } |
                ConvertTo-Json | Set-Content -LiteralPath $Req.ResultFile -Encoding UTF8
        }
        Mock Get-ScheduledTask { [pscustomobject]@{ State = 'Ready' } }
        Mock Get-ScheduledTaskInfo { [pscustomobject]@{ LastTaskResult = 0 } }
        Mock Unregister-ScheduledTask { }
        $R = Invoke-E4CTool -Plan $Plan -Name 'net-version' -Tool 'winget' -Arguments '--version' -TimeoutSeconds 60
        $R.ExitCode | Should -Be 0
        $R.Hex | Should -Be '0x00000000'
        @($R.Output.Lines) | Should -Be @('v1.11.510')
        $R.Sid | Should -Be 'S-1-5-21-1'
        $script:Principal.UserId | Should -Be 'dso'
        $script:Principal.LogonType | Should -Be 'Interactive'
        $script:Principal.RunLevel | Should -Be 'Highest'
        $script:SettingsLimit | Should -Be ([TimeSpan]::Zero)
        Should -Invoke New-ScheduledTaskAction -Times 1 -Exactly -ParameterFilter { $Execute -eq (Join-Path $env:SystemRoot 'System32\WindowsPowerShell\v1.0\powershell.exe') -and $Argument -match ('-File ' + [regex]::Escape((Join-Path $Plan.WorkDirectory 'runner.ps1')) + ' -Request ') }
        Should -Invoke Unregister-ScheduledTask -Times 1 -Exactly
        $Req = Get-Content -LiteralPath (Join-Path $Plan.WorkDirectory 'req\net-version.json') -Raw -Encoding UTF8 | ConvertFrom-Json
        $Req.Tool | Should -Be 'winget'
        $Req.Arguments | Should -Be '--version'
        $Req.TimeoutSeconds | Should -Be 60
    }
    It 'Invoke-E4CTool: runner nie odpisał (limit + zapas) = WaitExpired, zadanie trwające NIE jest wyrejestrowane ani zatrzymane (bez zabijania)' {
        $Plan = New-TestPlan
        $Plan.TaskGraceSeconds = 0
        Mock New-ScheduledTaskAction { [pscustomobject]@{} }
        Mock New-ScheduledTaskPrincipal { [pscustomobject]@{} }
        Mock New-ScheduledTaskSettingsSet { [pscustomobject]@{} }
        Mock Register-ScheduledTask -RemoveParameterType 'Action', 'Principal', 'Settings' { }
        Mock Start-ScheduledTask { }
        Mock Get-ScheduledTask { [pscustomobject]@{ State = 'Running' } }
        Mock Get-ScheduledTaskInfo { [pscustomobject]@{ LastTaskResult = 267009 } }
        Mock Unregister-ScheduledTask { }
        Mock Stop-ScheduledTask { }
        $script:Clock = 0
        Mock Get-E4CElapsedSeconds { $script:Clock += 30; $script:Clock }
        $R = Invoke-E4CTool -Plan $Plan -Name 'slow' -Tool 'winget' -Arguments '--version' -TimeoutSeconds 60
        $R.WaitExpired | Should -BeTrue
        $R.ExitCode | Should -BeNullOrEmpty
        $R.TaskLeftRunning | Should -BeTrue
        Should -Invoke Unregister-ScheduledTask -Times 0 -Exactly
        Should -Invoke Stop-ScheduledTask -Times 0 -Exactly
    }
    It 'Invoke-E4CTool: błąd startu zadania = wynik z błędem, zadanie wyrejestrowane w finally' {
        $Plan = New-TestPlan
        Mock New-ScheduledTaskAction { [pscustomobject]@{} }
        Mock New-ScheduledTaskPrincipal { [pscustomobject]@{} }
        Mock New-ScheduledTaskSettingsSet { [pscustomobject]@{} }
        Mock Register-ScheduledTask -RemoveParameterType 'Action', 'Principal', 'Settings' { }
        Mock Start-ScheduledTask { throw 'zadanie nie wystartowało' }
        Mock Get-ScheduledTask { [pscustomobject]@{ State = 'Ready' } }
        Mock Get-ScheduledTaskInfo { $null }
        Mock Unregister-ScheduledTask { }
        $R = Invoke-E4CTool -Plan $Plan -Name 'broken' -Tool 'winget' -Arguments '--version' -TimeoutSeconds 60
        $R.Error | Should -Match 'nie wystartowało'
        Should -Invoke Unregister-ScheduledTask -Times 1 -Exactly
    }
    It 'Invoke-E4CTool: tylko narzędzia winget i whoami, RunLevel tylko Highest/Limited' {
        $Plan = New-TestPlan
        { Invoke-E4CTool -Plan $Plan -Name 'x' -Tool 'cmd' -Arguments '/c' -TimeoutSeconds 5 } | Should -Throw
        { Invoke-E4CTool -Plan $Plan -Name 'x' -Tool 'winget' -Arguments '--version' -TimeoutSeconds 5 -RunLevel 'System' } | Should -Throw
    }
    It 'Invoke-E4CInstallItem: pełne polecenie z --locale; przy 0x8A150010 jedna ponowna próba bez --locale; diff Uninstall i dwa warianty list' {
        $script:Commands = [System.Collections.Generic.List[string]]::new()
        Mock Invoke-E4CTool { $script:Commands.Add($Arguments); $Code = if ($Arguments -match '^install' -and $Arguments -match '--locale') { $script:NoApplicableInstaller } else { 0 }; [pscustomobject]@{ Name = $Name; Arguments = $Arguments; ExitCode = $Code; Hex = (Format-E4CHex -Code $Code); Seconds = 1; Output = [pscustomobject]@{ Lines = @() } } }
        $script:Snap = 0
        Mock Get-E4CUninstallSnapshot { $script:Snap++; if ($script:Snap -eq 1) { @() } else { @([pscustomobject]@{ Branch = 'HKLM'; KeyPath = 'HKLM\X\Mozilla Firefox'; DisplayName = 'Mozilla Firefox (x64 en-US)'; Publisher = 'Mozilla'; DisplayVersion = '1'; SystemComponent = $null; ParentKeyName = $null }) } }
        $R = Invoke-E4CInstallItem -Plan (New-TestPlan) -UserSid 'S-1-5-21-1' -WingetId 'Mozilla.Firefox' -Label 'net-locale'
        @($R.Attempts).Count | Should -Be 2
        $script:Commands[0] | Should -Match '^install --id Mozilla\.Firefox --exact --source winget --silent --no-upgrade --locale pl-PL '
        $script:Commands[1] | Should -Match '^install --id Mozilla\.Firefox --exact --source winget --silent --no-upgrade --accept-package-agreements'
        $R.LocaleFallback | Should -BeTrue
        $R.FinalHex | Should -Be '0x00000000'
        @($R.Uninstall.Added | ForEach-Object { $_.DisplayName }) | Should -Be @('Mozilla Firefox (x64 en-US)')
        @($R.Visible | ForEach-Object { $_.DisplayName }) | Should -Be @('Mozilla Firefox (x64 en-US)')
        @($R.Branches) | Should -Be @('HKLM')
        ($script:Commands | Where-Object { $_ -like 'list *' }).Count | Should -Be 2
        $script:Commands | Where-Object { $_ -like 'list *--source winget*' } | Should -Not -BeNullOrEmpty
    }
    It 'Invoke-E4CInstallItem: inny kod niż 0x8A150010 = bez ponownej próby; -NoLocale = bez --locale' {
        $script:Commands = [System.Collections.Generic.List[string]]::new()
        Mock Invoke-E4CTool { $script:Commands.Add($Arguments); [pscustomobject]@{ Name = $Name; Arguments = $Arguments; ExitCode = $script:SourceDataMissing; Hex = '0x8A15000F'; Seconds = 1; Output = [pscustomobject]@{ Lines = @() } } }
        $R = Invoke-E4CInstallItem -Plan (New-TestPlan) -UserSid 'S-1-5-21-1' -WingetId '7zip.7zip' -Label 'off' -NoList
        @($R.Attempts).Count | Should -Be 1
        $R.LocaleFallback | Should -BeFalse
        $R.FinalHex | Should -Be '0x8A15000F'
        @($script:Commands | Where-Object { $_ -like 'list *' }).Count | Should -Be 0
        $script:Commands = [System.Collections.Generic.List[string]]::new()
        $null = Invoke-E4CInstallItem -Plan (New-TestPlan) -UserSid 'S-1-5-21-1' -WingetId '7zip.7zip' -Label 'off' -NoList -NoLocale
        $script:Commands[0] | Should -Not -Match '--locale'
    }
    It 'Invoke-E4CInstallItem: wpisy SystemComponent i ParentKeyName dodane, ale nie widoczne; gałąź HKU = per user' {
        Mock Invoke-E4CTool { [pscustomobject]@{ Name = $Name; Arguments = $Arguments; ExitCode = 0; Hex = '0x00000000'; Seconds = 1; Output = [pscustomobject]@{ Lines = @() } } }
        $script:Snap = 0
        Mock Get-E4CUninstallSnapshot {
            $script:Snap++
            if ($script:Snap -eq 1) { return @() }
            @([pscustomobject]@{ Branch = 'HKLM32'; KeyPath = 'K1'; DisplayName = 'Pakiet'; Publisher = 'P'; DisplayVersion = '1'; SystemComponent = $null; ParentKeyName = $null }
                [pscustomobject]@{ Branch = 'HKLM'; KeyPath = 'K2'; DisplayName = 'Minimum Runtime'; Publisher = 'P'; DisplayVersion = '1'; SystemComponent = 1; ParentKeyName = $null }
                [pscustomobject]@{ Branch = 'HKU'; KeyPath = 'K3'; DisplayName = 'Aktualizacja'; Publisher = 'P'; DisplayVersion = '1'; SystemComponent = $null; ParentKeyName = 'Pakiet' })
        }
        $R = Invoke-E4CInstallItem -Plan (New-TestPlan) -UserSid 'S-1-5-21-1' -WingetId 'Microsoft.VCRedist.2015+.x64' -Label 'all' -NoList
        @($R.Visible | ForEach-Object { $_.KeyPath }) | Should -Be @('K1')
        @($R.Branches | Sort-Object) | Should -Be @('HKLM', 'HKLM32', 'HKU')
    }
    It 'Invoke-E4CInstallAll: każda pozycja z planu po kolei, jedno wywołanie instalacji na aplikację, kategoria w wyniku' {
        $Plan = New-TestPlan
        $Plan | Add-Member -NotePropertyName InstallIds -NotePropertyValue @('Microsoft.VCRedist.2015+.x64', 'Discord.Discord') -Force
        $script:Installed = [System.Collections.Generic.List[string]]::new()
        Mock Invoke-E4CInstallItem { $script:Installed.Add($WingetId); [pscustomobject]@{ WingetId = $WingetId; FinalHex = '0x00000000' } }
        $R = Invoke-E4CInstallAll -Plan $Plan -UserSid 'S-1-5-21-1'
        @($script:Installed) | Should -Be @('Microsoft.VCRedist.2015+.x64', 'Discord.Discord')
        @($R.Items | ForEach-Object { $_.Category }) | Should -Be @('runtimes', 'messengers')
    }
    It 'Invoke-E4CNetworkProbe: show dla każdej pozycji listy i odrzuconych, długie wyszukiwania, --locale Firefox i LibreOffice, --no-upgrade 7-Zip dwa razy, kandydat tylko gdy jest w wynikach' {
        $Plan = New-TestPlan
        $script:Shows = [System.Collections.Generic.List[string]]::new()
        $script:Searches = [System.Collections.Generic.List[string]]::new()
        $script:Installs = [System.Collections.Generic.List[string]]::new()
        Mock Test-E4CNetwork { [pscustomobject]@{ Resolved = $true; Addresses = @('1.2.3.4'); Seconds = 1 } }
        Mock Invoke-E4CTool {
            if ($Arguments -like 'show *') { $script:Shows.Add(($Arguments -split ' ')[2]) }
            if ($Arguments -like 'search *') { $script:Searches.Add($Arguments) }
            $Lines = if ($Arguments -like "search --query $($Plan.SearchCandidate.Query) *") { @("PuTTY  $($Plan.SearchCandidate.WingetId)  0.83  winget") } else { @() }
            [pscustomobject]@{ Name = $Name; Arguments = $Arguments; ExitCode = 0; Hex = '0x00000000'; Seconds = 1; Output = [pscustomobject]@{ Lines = $Lines } }
        }
        Mock Invoke-E4CInstallItem { $script:Installs.Add("$WingetId|$Label|$NoLocale"); [pscustomobject]@{ WingetId = $WingetId; FinalHex = '0x00000000' } }
        $R = Invoke-E4CNetworkProbe -Plan $Plan -UserSid 'S-1-5-21-1'
        @($script:Shows) | Should -Be (@($Plan.Apps | ForEach-Object { $_.WingetId }) + @($Plan.RejectedShow))
        @($script:Searches | Where-Object { $_ -match 'Microsoft Visual C\+\+ Redistributable' }).Count | Should -Be 1
        @($Plan.ExtraSearches).Count | Should -BeGreaterThan 0
        @($script:Searches).Count | Should -Be (@($Plan.LongSearches).Count + @($Plan.ExtraSearches).Count + 1)
        @($script:Installs) | Should -Contain 'Mozilla.Firefox|net-locale|False'
        @($script:Installs) | Should -Contain 'TheDocumentFoundation.LibreOffice|net-locale|False'
        @($script:Installs | Where-Object { $_ -like '7zip.7zip|net-noupgrade*' }).Count | Should -Be 2
        @($script:Installs) | Should -Contain "$($Plan.SearchCandidate.WingetId)|net-candidate|False"
        $R.Connectivity.Resolved | Should -BeTrue
        Mock Invoke-E4CTool { [pscustomobject]@{ Name = $Name; Arguments = $Arguments; ExitCode = 0; Hex = '0x00000000'; Seconds = 1; Output = [pscustomobject]@{ Lines = @() } } }
        $script:Installs.Clear()
        $R2 = Invoke-E4CNetworkProbe -Plan $Plan -UserSid 'S-1-5-21-1'
        @($script:Installs | Where-Object { $_ -like '*net-candidate*' }).Count | Should -Be 0
        $R2.Candidate.Skipped | Should -Match 'brak'
    }
    It 'Invoke-E4CNetworkProbe: brak sieci (DNS) = nic nie jest uruchamiane, błąd w wyniku' {
        Mock Test-E4CNetwork { [pscustomobject]@{ Resolved = $false; Addresses = @(); Seconds = 300 } }
        Mock Invoke-E4CTool { throw 'nie wolno' }
        Mock Invoke-E4CInstallItem { throw 'nie wolno' }
        { Invoke-E4CNetworkProbe -Plan (New-TestPlan) -UserSid 'S-1-5-21-1' } | Should -Throw '*sieci*'
        Should -Invoke Invoke-E4CTool -Times 0 -Exactly
    }
    It 'Invoke-E4CAdminProtectionProbe: whoami i winget --version z zadania Highest i Limited, SID procesu porównany z SID dso' {
        $script:Runs = [System.Collections.Generic.List[string]]::new()
        Mock Get-E4CAdminApprovalState { [pscustomobject]@{ TypeOfAdminApprovalMode = [pscustomobject]@{ Exists = $true; Value = 2 } } }
        Mock Get-E4CLocalAccounts { @() }
        Mock Invoke-E4CTool { $script:Runs.Add("$Tool|$RunLevel|$Arguments"); [pscustomobject]@{ Name = $Name; Tool = $Tool; Arguments = $Arguments; ExitCode = 0; Hex = '0x00000000'; Sid = $(if ($RunLevel -eq 'Highest') { 'S-1-5-21-9-500' } else { 'S-1-5-21-1' }); Output = [pscustomobject]@{ Lines = @() } } }
        $R = Invoke-E4CAdminProtectionProbe -Plan (New-TestPlan) -UserSid 'S-1-5-21-1'
        @($script:Runs) | Should -Be @('whoami|Highest|/user /groups', 'winget|Highest|--version', 'whoami|Limited|/user /groups', 'winget|Limited|--version')
        $R.HighestSidDiffers | Should -BeTrue
        $R.LimitedSidDiffers | Should -BeFalse
    }
}

Describe 'Wysyłka funkcji do VM i treść skryptu' {
    It 'Get-E4CRemoteScript: poprawny, definiuje wszystkie funkcje zdalne; nie woła niczego spoza listy; funkcje hosta nie są wysyłane' {
        $Text = Get-E4CRemoteScript
        $Errors = $null
        $Ast = [System.Management.Automation.Language.Parser]::ParseInput($Text, [ref]$null, [ref]$Errors)
        @($Errors).Count | Should -Be 0
        $Sent = @(Get-ExploreCommonRemoteFunctionNames) + @(Get-E4CRemoteFunctionNames)
        foreach ($Name in $Sent) { $Text | Should -Match "function $([regex]::Escape($Name)) \{" }
        $Called = $Ast.FindAll({ param($n) $n -is [System.Management.Automation.Language.CommandAst] }, $true) | ForEach-Object { $_.GetCommandName() } | Where-Object { $_ -like '*-E4C*' -or $_ -like '*-Explore*' } | Sort-Object -Unique
        foreach ($Name in $Called) { $Sent | Should -Contain $Name }
        foreach ($HostOnly in (Get-E4CHostOnlyFunctionNames)) { $Text | Should -Not -Match "function $([regex]::Escape($HostOnly)) \{" }
        $Text | Should -Not -Match 'Restore-VmCleanState|Wait-VmReady|Get-DsoVmCredential|Start-VM|Connect-VMNetworkAdapter'
    }
    It 'każda funkcja skryptu jest wysyłana albo na liście hosta; polecenia ze skutkiem tylko w swoich funkcjach; winget nigdy po nazwie na hoście' {
        $Ast = [System.Management.Automation.Language.Parser]::ParseFile((Resolve-Path $script:E4CScriptPath), [ref]$null, [ref]$null)
        $Defined = @($Ast.FindAll({ param($n) $n -is [System.Management.Automation.Language.FunctionDefinitionAst] }, $true) | ForEach-Object { $_.Name })
        foreach ($Name in $Defined) { ((Get-E4CRemoteFunctionNames) + (Get-E4CHostOnlyFunctionNames)) | Should -Contain $Name }
        $Commands = @($Ast.FindAll({ param($n) $n -is [System.Management.Automation.Language.CommandAst] }, $true))
        $Allowed = @{
            'Start-VM'                    = @('Invoke-E4CVmRun')
            'Restore-VmCleanState'        = @('Invoke-E4CVmRun', 'Complete-E4CVmCleanup')
            'Connect-VMNetworkAdapter'    = @('Invoke-E4CVmRun')
            'Disconnect-VMNetworkAdapter' = @('Invoke-E4CVmRun', 'Complete-E4CVmCleanup')
            'Register-ScheduledTask'      = @('Invoke-E4CTool')
            'Start-ScheduledTask'         = @('Invoke-E4CTool')
            'Unregister-ScheduledTask'    = @('Invoke-E4CTool')
            'Restart-Computer'            = @('Restart-E4CVm')
            'Wait-VmReady'                = @('Invoke-E4CVmRun', 'Restart-E4CVmAndWait')
            'Copy-DsoBuildToVm'           = @('Invoke-E4CVmRun')
            'Copy-Item'                   = @('Copy-E4CDsoLogs')
        }
        foreach ($Name in $Allowed.Keys) {
            foreach ($Command in ($Commands | Where-Object { $_.GetCommandName() -eq $Name })) { $Allowed[$Name] | Should -Contain (Get-CommandOwner $Command) -Because $Name }
        }
        foreach ($Command in ($Commands | Where-Object { $_.GetCommandName() -eq 'Connect-VMNetworkAdapter' })) { $Command.Extent.Text | Should -Match "-SwitchName 'Default Switch'" }
        $Names = @($Commands | ForEach-Object { $_.GetCommandName() } | Where-Object { $_ })
        foreach ($Forbidden in 'winget', 'winget.exe', 'Set-ItemProperty', 'New-ItemProperty', 'Remove-ItemProperty', 'reg', 'reg.exe', 'Stop-Process', 'taskkill', 'taskkill.exe', 'Stop-ScheduledTask', 'Invoke-Expression', 'iex',
            'Invoke-RestMethod', 'Invoke-WebRequest', 'Stop-VM', 'Remove-VM', 'Restore-VMCheckpoint', 'Set-VMNetworkAdapter', 'cmd', 'cmd.exe', 'Start-Process', 'Set-Acl', 'Add-Type', 'Add-AppxPackage') {
            $Names | Should -Not -Contain $Forbidden
        }
        $Text = [System.IO.File]::ReadAllText((Resolve-Path $script:E4CScriptPath), [System.Text.Encoding]::UTF8)
        $Text | Should -Not -Match 'HKCU:|HKEY_CURRENT_USER|EncodedCommand|Bruteforce_LTSC\s*[''"]|--ignore-security-hash|--force|--scope|msstore|\.Kill\('
    }
    It 'plik ma BOM UTF-8 oraz sekcje Instrukcja uruchomienia i Opis działania; VmName tylko Home/Pro' {
        $Bytes = [System.IO.File]::ReadAllBytes((Resolve-Path $script:E4CScriptPath))
        ($Bytes[0..2] -join ',') | Should -Be '239,187,191'
        $Text = [System.IO.File]::ReadAllText((Resolve-Path $script:E4CScriptPath), [System.Text.Encoding]::UTF8)
        $Text.Substring(0, 400) | Should -Match 'Instrukcja uruchomienia'
        $Text | Should -Match 'Opis działania'
        foreach ($Command in (Get-Command $script:E4CScriptPath), (Get-Command Invoke-E4CVmRun), (Get-Command Complete-E4CVmCleanup)) {
            $Attr = $Command.Parameters['VmName'].Attributes | Where-Object { $_ -is [System.Management.Automation.ValidateSetAttribute] }
            $Attr.ValidValues | Should -Be @('DSO-26H2-Home', 'DSO-26H2-Pro')
        }
    }
    It 'plik testów też ma BOM UTF-8' {
        $Bytes = [System.IO.File]::ReadAllBytes((Join-Path $PSScriptRoot 'E4CExploration.Tests.ps1'))
        ($Bytes[0..2] -join ',') | Should -Be '239,187,191'
    }
}

Describe 'Argumenty przebiegu' {
    It 'Assert-E4COutDirectory: wymagany, bezwzględny, poza repo; w repo tylko ignorowane przez git' {
        $Repo = 'C:\Repo\DSOptimizer'
        { Assert-E4COutDirectory -Path '' -RepoRoot $Repo } | Should -Throw '*wymagany*'
        { Assert-E4COutDirectory -Path 'wyniki' -RepoRoot $Repo } | Should -Throw '*bezwzględn*'
        { Assert-E4COutDirectory -Path 'C:\Repo\DSOptimizer\tests\vm\out\x' -RepoRoot $Repo } | Should -Throw '*w repo*'
        { Assert-E4COutDirectory -Path 'C:\Repo\DSOptimizer\wyniki\pomiar\exploration' -RepoRoot $Repo } | Should -Throw '*w repo*'
        $Real = (Resolve-Path (Join-Path $PSScriptRoot '..\..\..')).ProviderPath   # prawdziwe repo: git check-ignore
        Assert-E4COutDirectory -Path "$Real\tests\vm\out\x" -RepoRoot $Real | Should -Be "$Real\tests\vm\out\x"   # ignorowany przez git
        { Assert-E4COutDirectory -Path "$Real\src\x" -RepoRoot $Real } | Should -Throw '*ignorowanym przez git*'   # nieignorowany
        Assert-E4COutDirectory -Path (Join-Path $env:TEMP 'dso-explore') -RepoRoot $Real | Should -Be (Join-Path $env:TEMP 'dso-explore')   # poza repo
    }
    It 'Get-E4CRunArguments: VM w kolejności (Pro najpierw), fazy per VM, plan' {
        $Arguments = Get-E4CRunArguments -VmName @('DSO-26H2-Pro', 'DSO-26H2-Home') -Phase @('adminprotection', 'network') -OutDirectory 'C:\w' -CheckpointName 'c' -ReadyTimeoutSeconds 60
        @($Arguments.VmName) | Should -Be @('DSO-26H2-Pro', 'DSO-26H2-Home')
        @($Arguments.Phases['DSO-26H2-Pro']) | Should -Be @('baseline', 'network', 'adminprotection')
        @($Arguments.Phases['DSO-26H2-Home']) | Should -Be @('baseline', 'network')
        $Arguments.Plan.Locale | Should -Be 'pl-PL'
    }
}

Describe 'Orkiestracja hosta (mocki Hyper-V)' -Skip:(-not $script:HasHyperV) {
    BeforeEach {
        $script:Calls = [System.Collections.Generic.List[string]]::new()
        Mock Assert-DsoVmLiveAllowed { }
        Mock Get-DsoVmCredential { [pscredential]::new('Administrator', (ConvertTo-SecureString 'x' -AsPlainText -Force)) }
        Mock Restore-VmCleanState { $script:Calls.Add("revert $VmName") }
        Mock Start-VM { $script:Calls.Add("start $Name") }
        Mock Wait-VmReady { $script:Calls.Add('ready'); $null }
        Mock Invoke-Command -RemoveParameterValidation 'Session' { 'S-1-5-21-1-1001' }
        Mock Start-Sleep { }
        Mock Get-E4CRemoteScript { 'function X {}' }
        Mock Invoke-E4CVmPhase -RemoveParameterValidation 'Session' { $script:Calls.Add("phase $Phase"); [pscustomobject]@{ phase = $Phase; data = [pscustomobject]@{ Before = [pscustomobject]@{ Exists = $true; Value = 1 } } } }
        Mock Restart-E4CVmAndWait -RemoveParameterValidation 'Session' { $script:Calls.Add('restart'); $null }
        Mock Copy-E4CRawFiles -RemoveParameterValidation 'Session' { $script:Calls.Add('copy raw'); @() }
        Mock Connect-VMNetworkAdapter { $script:Calls.Add("connect $VMName $SwitchName") }
        Mock Disconnect-VMNetworkAdapter { $script:Calls.Add("disconnect $VMName") }
        Mock Remove-PSSession -RemoveParameterValidation 'Session' { }
        Mock Save-ExploreResult { $script:Calls.Add("save $FileName"); $script:SavedResult = $Result; "C:\w\$VmName\$FileName" }
        Mock Get-VM { [pscustomobject]@{ Name = $Name; State = 'Off' } }
        Mock Get-VMNetworkAdapter { [pscustomobject]@{ VMName = $VMName; Name = 'Network Adapter'; SwitchName = $null } }
        $script:SavedResult = $null
    }
    It 'przebieg Pro: revert -> start -> gotowość -> fazy, sieć tylko na network/install-all, adminprotection z dwoma restartami -> kopia -> revert -> odłączenie -> zapis' {
        $Phases = Get-E4CPhaseOrder -Phase @('offline-fresh', 'network', 'install-all', 'adminprotection', 'final') -VmName 'DSO-26H2-Pro'
        $Path = Invoke-E4CVmRun -VmName 'DSO-26H2-Pro' -OutDirectory $TestDrive -Phases $Phases 6>$null
        $Path | Should -Be 'C:\w\DSO-26H2-Pro\e4c.json'
        @($script:Calls) | Should -Be @('revert DSO-26H2-Pro', 'start DSO-26H2-Pro', 'ready', 'phase baseline', 'phase offline-fresh',
            'connect DSO-26H2-Pro Default Switch', 'phase network', 'disconnect DSO-26H2-Pro',
            'connect DSO-26H2-Pro Default Switch', 'phase install-all', 'disconnect DSO-26H2-Pro',
            'phase ap-set', 'restart', 'phase ap-probe', 'phase ap-restore', 'restart',
            'phase final', 'copy raw', 'revert DSO-26H2-Pro', 'disconnect DSO-26H2-Pro', 'save e4c.json')
        $script:SavedResult.cleanup.Ok | Should -BeTrue
        $script:SavedResult.userSid | Should -Be 'S-1-5-21-1-1001'
        $script:SavedResult.error | Should -BeNullOrEmpty
        $script:SavedResult.phases.adminprotection.set.phase | Should -Be 'ap-set'
    }
    It 'plan wysłany do VM ma listę instalacji tej VM (Home = podzbiór); bez cli-install build nie jest kopiowany' {
        $script:PlanJsons = [System.Collections.Generic.List[string]]::new()
        Mock Invoke-E4CVmPhase -RemoveParameterValidation 'Session' { $script:PlanJsons.Add($PlanJson); [pscustomobject]@{ phase = $Phase } }
        Mock Copy-DsoBuildToVm -RemoveParameterValidation 'Session' { $script:Calls.Add('copy build') }
        $null = Invoke-E4CVmRun -VmName 'DSO-26H2-Home' -OutDirectory $TestDrive -Phases @('baseline') 6>$null
        $Sent = $script:PlanJsons[0] | ConvertFrom-Json
        @($Sent.InstallIds) | Should -Be @(Get-E4CInstallIds -VmName 'DSO-26H2-Home' -Plan (Get-E4CPlan))
        @($Sent.CliIds) | Should -Be @(Get-E4CCliInstallIds -VmName 'DSO-26H2-Home' -Plan (Get-E4CPlan))
        Should -Invoke Copy-DsoBuildToVm -Times 0 -Exactly
    }
    It 'przebieg cli-install (Pro): build w VM przed fazami, sieć tylko na cli-install, cała lista w planie, potem kopia -> revert -> odłączenie -> zapis' {
        $script:PlanJsons = [System.Collections.Generic.List[string]]::new()
        Mock Invoke-E4CVmPhase -RemoveParameterValidation 'Session' { $script:Calls.Add("phase $Phase"); $script:PlanJsons.Add($PlanJson); [pscustomobject]@{ phase = $Phase } }
        Mock Copy-DsoBuildToVm -RemoveParameterValidation 'Session' { $script:Calls.Add("copy build $BuildDirectory $CatalogPath $($Layout.Exe)") }
        $Path = Invoke-E4CVmRun -VmName 'DSO-26H2-Pro' -OutDirectory $TestDrive -Phases @('baseline', 'cli-install') -BuildDirectory 'C:\b' -CatalogPath 'C:\c.json' 6>$null
        $Path | Should -Be 'C:\w\DSO-26H2-Pro\e4c.json'
        @($script:Calls) | Should -Be @('revert DSO-26H2-Pro', 'start DSO-26H2-Pro', 'ready', 'copy build C:\b C:\c.json C:\dso\app\DSOptimizer.exe', 'phase baseline',
            'connect DSO-26H2-Pro Default Switch', 'phase cli-install', 'disconnect DSO-26H2-Pro',
            'copy raw', 'revert DSO-26H2-Pro', 'disconnect DSO-26H2-Pro', 'save e4c.json')
        @(($script:PlanJsons[0] | ConvertFrom-Json).CliIds).Count | Should -Be 35
        ($script:PlanJsons[0] | ConvertFrom-Json).CliRepeat | Should -BeFalse
        $script:SavedResult.cleanup.Ok | Should -BeTrue
        $script:SavedResult.error | Should -BeNullOrEmpty
    }
    It 'przebieg cli-install z -CliIds i -CliRepeat (RustDesk, Task 7): id nadpisują listę VM, CliRepeat w planie, sieć tylko na cli-install, revert i odłączenie w finally' {
        $script:PlanJsons = [System.Collections.Generic.List[string]]::new()
        Mock Invoke-E4CVmPhase -RemoveParameterValidation 'Session' { $script:Calls.Add("phase $Phase"); $script:PlanJsons.Add($PlanJson); [pscustomobject]@{ phase = $Phase } }
        Mock Copy-DsoBuildToVm -RemoveParameterValidation 'Session' { $script:Calls.Add('copy build') }
        $null = Invoke-E4CVmRun -VmName 'DSO-26H2-Pro' -OutDirectory $TestDrive -Phases @('baseline', 'cli-install') -BuildDirectory 'C:\b' -CatalogPath 'C:\c.json' -CliIds @('install.7zip', 'install.rustdesk') -CliRepeat 6>$null
        @($script:Calls) | Should -Be @('revert DSO-26H2-Pro', 'start DSO-26H2-Pro', 'ready', 'copy build', 'phase baseline',
            'connect DSO-26H2-Pro Default Switch', 'phase cli-install', 'disconnect DSO-26H2-Pro',
            'copy raw', 'revert DSO-26H2-Pro', 'disconnect DSO-26H2-Pro', 'save e4c.json')
        foreach ($Json in $script:PlanJsons) {
            $Sent = $Json | ConvertFrom-Json
            @($Sent.CliIds) | Should -Be @('install.7zip', 'install.rustdesk')
            $Sent.CliRepeat | Should -BeTrue
        }
        $script:SavedResult.cleanup.Ok | Should -BeTrue
        # Home: te same -CliIds nadpisują CliHomeIds; bez -CliRepeat plan ma CliRepeat = false.
        $script:PlanJsons.Clear()
        $null = Invoke-E4CVmRun -VmName 'DSO-26H2-Home' -OutDirectory $TestDrive -Phases @('baseline') -CliIds @('install.rustdesk') 6>$null
        $Sent = $script:PlanJsons[0] | ConvertFrom-Json
        @($Sent.CliIds) | Should -Be @('install.rustdesk')
        $Sent.CliRepeat | Should -BeFalse
    }
    It 'błąd fazy cli-install z -CliRepeat: karta odłączona od razu, revert i odłączenie w finally' {
        Mock Copy-DsoBuildToVm -RemoveParameterValidation 'Session' { $script:Calls.Add('copy build') }
        Mock Invoke-E4CVmPhase -RemoveParameterValidation 'Session' { $script:Calls.Add("phase $Phase"); if ($Phase -eq 'cli-install') { throw 'CLI padło' }; [pscustomobject]@{ phase = $Phase } }
        { Invoke-E4CVmRun -VmName 'DSO-26H2-Home' -OutDirectory $TestDrive -Phases @('baseline', 'cli-install') -BuildDirectory 'C:\b' -CatalogPath 'C:\c.json' -CliIds @('install.rustdesk') -CliRepeat 6>$null } | Should -Throw '*CLI padło*'
        $script:Calls.IndexOf('disconnect DSO-26H2-Home') | Should -Be ($script:Calls.IndexOf('phase cli-install') + 1)
        @($script:Calls)[-3..-1] | Should -Be @('revert DSO-26H2-Home', 'disconnect DSO-26H2-Home', 'save e4c.json')
    }
    It 'złe -CliIds (id ze spacją) = wyjątek przed revertem i startem VM' {
        { Invoke-E4CVmRun -VmName 'DSO-26H2-Pro' -OutDirectory $TestDrive -Phases @('baseline', 'cli-install') -BuildDirectory 'C:\b' -CatalogPath 'C:\c.json' -CliIds @('install.a b') 6>$null } | Should -Throw '*install.a b*'
        Should -Invoke Restore-VmCleanState -Times 0 -Exactly
        Should -Invoke Start-VM -Times 0 -Exactly
    }
    It 'Invoke-E4CExploration przekazuje -CliIds i -CliRepeat z argumentów przebiegu do każdej VM' {
        $script:Runs = [System.Collections.Generic.List[string]]::new()
        Mock Invoke-E4CVmRun { $script:Runs.Add("$VmName|$(@($CliIds) -join ',')|$([bool]$CliRepeat)"); "C:\w\$VmName\e4c.json" }
        $Arguments = Get-E4CRunArguments -VmName @('DSO-26H2-Pro', 'DSO-26H2-Home') -Phase @('cli-install') -OutDirectory 'C:\w' -CheckpointName 'c' -ReadyTimeoutSeconds 60 -CliIds @('install.rustdesk') -CliRepeat $true
        $null = Invoke-E4CExploration -Arguments $Arguments 6>$null
        @($script:Runs) | Should -Be @('DSO-26H2-Pro|install.rustdesk|True', 'DSO-26H2-Home|install.rustdesk|True')
        $script:Runs.Clear()
        $Default = Get-E4CRunArguments -VmName @('DSO-26H2-Pro') -Phase @('cli-install') -OutDirectory 'C:\w' -CheckpointName 'c' -ReadyTimeoutSeconds 60
        $null = Invoke-E4CExploration -Arguments $Default 6>$null
        @($script:Runs) | Should -Be @('DSO-26H2-Pro||False')
    }
    It 'cli-install bez katalogu builda = wyjątek przed revertem i startem VM' {
        { Invoke-E4CVmRun -VmName 'DSO-26H2-Pro' -OutDirectory $TestDrive -Phases @('baseline', 'cli-install') 6>$null } | Should -Throw '*BuildDirectory*'
        Should -Invoke Restore-VmCleanState -Times 0 -Exactly
        Should -Invoke Start-VM -Times 0 -Exactly
    }
    It 'błąd fazy cli-install: karta odłączona od razu, revert i odłączenie w finally, wynik częściowy zapisany' {
        Mock Copy-DsoBuildToVm -RemoveParameterValidation 'Session' { $script:Calls.Add('copy build') }
        Mock Invoke-E4CVmPhase -RemoveParameterValidation 'Session' { $script:Calls.Add("phase $Phase"); if ($Phase -eq 'cli-install') { throw 'CLI padło' }; [pscustomobject]@{ phase = $Phase } }
        { Invoke-E4CVmRun -VmName 'DSO-26H2-Home' -OutDirectory $TestDrive -Phases @('baseline', 'cli-install') -BuildDirectory 'C:\b' -CatalogPath 'C:\c.json' 6>$null } | Should -Throw '*DSO-26H2-Home*CLI padło*'
        $script:Calls.IndexOf('disconnect DSO-26H2-Home') | Should -Be ($script:Calls.IndexOf('phase cli-install') + 1)
        @($script:Calls)[-3..-1] | Should -Be @('revert DSO-26H2-Home', 'disconnect DSO-26H2-Home', 'save e4c.json')
        $script:SavedResult.phases.baseline.phase | Should -Be 'baseline'
    }
    It 'błąd fazy install-all: karta odłączona od razu, dalsze fazy idą, revert i odłączenie w finally, wynik częściowy zapisany, wyjątek z nazwą VM' {
        Mock Invoke-E4CVmPhase -RemoveParameterValidation 'Session' { $script:Calls.Add("phase $Phase"); if ($Phase -eq 'install-all') { throw 'sesja padła' }; [pscustomobject]@{ phase = $Phase } }
        { Invoke-E4CVmRun -VmName 'DSO-26H2-Home' -OutDirectory $TestDrive -Phases @('baseline', 'install-all', 'final') 6>$null } | Should -Throw '*DSO-26H2-Home*sesja padła*'
        $script:Calls.IndexOf('disconnect DSO-26H2-Home') | Should -BeLessThan $script:Calls.IndexOf('phase final')
        $script:Calls | Should -Contain 'revert DSO-26H2-Home'
        $script:Calls[-1] | Should -Be 'save e4c.json'
        $script:Calls[-2] | Should -Be 'disconnect DSO-26H2-Home'
        $script:Calls[-3] | Should -Be 'revert DSO-26H2-Home'
        $script:SavedResult.phases.baseline.phase | Should -Be 'baseline'
        $script:SavedResult.phases.final.phase | Should -Be 'final'
    }
    It 'błąd w adminprotection po ustawieniu wartości: przywrócenie i tak próbowane, potem revert w finally' {
        Mock Invoke-E4CVmPhase -RemoveParameterValidation 'Session' { $script:Calls.Add("phase $Phase"); if ($Phase -eq 'ap-probe') { throw 'sonda padła' }; [pscustomobject]@{ phase = $Phase; data = [pscustomobject]@{ Before = [pscustomobject]@{ Exists = $true; Value = 1 } } } }
        { Invoke-E4CVmRun -VmName 'DSO-26H2-Pro' -OutDirectory $TestDrive -Phases @('baseline', 'adminprotection', 'final') 6>$null } | Should -Throw '*sonda padła*'
        $script:Calls | Should -Contain 'phase ap-restore'
        $script:Calls.IndexOf('phase ap-restore') | Should -BeLessThan $script:Calls.IndexOf('phase final')
        $script:Calls | Should -Contain 'revert DSO-26H2-Pro'
    }
    It 'stan końcowy niezgodny z kontraktem (VM nie Off, karta na przełączniku): zapisany i zgłoszony' {
        Mock Get-VM { [pscustomobject]@{ Name = $Name; State = 'Running' } }
        Mock Get-VMNetworkAdapter { [pscustomobject]@{ VMName = $VMName; Name = 'Network Adapter'; SwitchName = 'Default Switch' } }
        { Invoke-E4CVmRun -VmName 'DSO-26H2-Home' -OutDirectory $TestDrive -Phases @('baseline') 3>$null 6>$null } | Should -Throw '*stan końcowy*'
        ($script:SavedResult.cleanup.Violations -join ' ') | Should -Match 'Running'
        ($script:SavedResult.cleanup.Violations -join ' ') | Should -Match 'Default Switch'
    }
    It 'błąd uwierzytelnienia: przekazany dalej, revert wykonany; Invoke-E4CExploration zatrzymuje całość po pierwszej VM' {
        Mock Wait-VmReady { $E = [System.InvalidOperationException]::new('błąd uwierzytelnienia PowerShell Direct'); $E.Data['VmAuthFailure'] = $true; throw $E }
        { Invoke-E4CVmRun -VmName 'DSO-26H2-Pro' -OutDirectory $TestDrive -Phases @('baseline') } | Should -Throw '*uwierzytelnienia*'
        $script:Calls | Should -Contain 'revert DSO-26H2-Pro'
        $Arguments = Get-E4CRunArguments -VmName @('DSO-26H2-Pro', 'DSO-26H2-Home') -Phase @('baseline') -OutDirectory 'C:\w' -CheckpointName 'c' -ReadyTimeoutSeconds 60
        Mock Invoke-E4CVmRun { $E = [System.InvalidOperationException]::new('x'); $E.Data['VmAuthFailure'] = $true; throw $E }
        { Invoke-E4CExploration -Arguments $Arguments 6>$null } | Should -Throw
        Should -Invoke Invoke-E4CVmRun -Times 1 -Exactly -Scope It
    }
}

Describe 'Blok główny SKRYPTU (-ShowPlan; bez VM)' {
    It '-ShowPlan: kod 0, plan i fazy; bez -OutDirectory kod 1' {
        $env:DSO_VM_TESTS_MOCK_ONLY | Should -Be '1'
        $Output = & $script:E4CScriptPath -ShowPlan -VmName DSO-26H2-Pro -Phase network -NoNetwork -OutDirectory (Join-Path $TestDrive 'w') 6>$null
        $LASTEXITCODE | Should -Be 0
        $Plan = ($Output | Out-String) | ConvertFrom-Json
        @($Plan.VmName) | Should -Be @('DSO-26H2-Pro')
        @($Plan.Phases.'DSO-26H2-Pro') | Should -Be @('baseline')
        $Plan.Network | Should -BeFalse
        Test-Path -LiteralPath (Join-Path $TestDrive 'w') | Should -BeFalse
        & $script:E4CScriptPath -ShowPlan 6>$null | Out-Null
        $LASTEXITCODE | Should -Be 1
    }
    It '-ShowPlan bez -Phase: cli-install nie wchodzi; z -Phase cli-install: baseline + cli-install, z siecią' {
        $All = (& $script:E4CScriptPath -ShowPlan -VmName DSO-26H2-Pro -OutDirectory (Join-Path $TestDrive 'w') 6>$null | Out-String) | ConvertFrom-Json
        @($All.Phases.'DSO-26H2-Pro') | Should -Not -Contain 'cli-install'
        $Cli = (& $script:E4CScriptPath -ShowPlan -VmName DSO-26H2-Home -Phase cli-install -OutDirectory (Join-Path $TestDrive 'w') 6>$null | Out-String) | ConvertFrom-Json
        $LASTEXITCODE | Should -Be 0
        @($Cli.Phases.'DSO-26H2-Home') | Should -Be @('baseline', 'cli-install')
        $Cli.Network | Should -BeTrue
    }
    It '-ShowPlan z -CliIds i -CliRepeat: id i powtórzenie w planie; id ze spacją = kod 1' {
        $Cli = (& $script:E4CScriptPath -ShowPlan -VmName DSO-26H2-Pro -Phase cli-install -CliIds install.7zip, install.rustdesk -CliRepeat -OutDirectory (Join-Path $TestDrive 'w') 6>$null | Out-String) | ConvertFrom-Json
        $LASTEXITCODE | Should -Be 0
        @($Cli.CliIds) | Should -Be @('install.7zip', 'install.rustdesk')
        $Cli.CliRepeat | Should -BeTrue
        & $script:E4CScriptPath -ShowPlan -VmName DSO-26H2-Home -Phase cli-install -CliIds 'install.a b' -OutDirectory (Join-Path $TestDrive 'w') 6>$null | Out-Null
        $LASTEXITCODE | Should -Be 1
    }
}

AfterAll { Clear-NoLiveVm }
