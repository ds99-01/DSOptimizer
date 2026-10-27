BeforeAll {
    # Wyłącznik: żaden test na mockach nie może dotknąć prawdziwego Hyper-V (patrz Support\NoLiveVm.ps1).
    . (Join-Path $PSScriptRoot 'Support\NoLiveVm.ps1')

    Import-Module (Join-Path $PSScriptRoot '..\modules\VmCommon.psm1') -Force
    Import-Module (Join-Path $PSScriptRoot '..\modules\VmRun.psm1') -Force
    Import-Module (Join-Path $PSScriptRoot '..\modules\VmHost.psm1') -Force
    # Skrypt jest dot-source'owany: przy '.' nie uruchamia przebiegu na VM, tylko definiuje funkcje.
    . (Join-Path $PSScriptRoot '..\explore\Invoke-C5Exploration.ps1')

    $script:C5ScriptPath = Join-Path $PSScriptRoot '..\explore\Invoke-C5Exploration.ps1'
    $script:ResearchPath = Join-Path $PSScriptRoot '..\..\..\docs\zrodla\2026-10-19-partia-c5-zrodla.md'
    $script:ResearchText = [System.IO.File]::ReadAllText((Resolve-Path $script:ResearchPath), [System.Text.Encoding]::UTF8)

    function script:New-FakeProc {
        # Wpis migawki procesów w kształcie Get-C5ProcessSnapshot (bez dotykania WMI).
        param([int]$Id, [string]$Name, [int]$Parent = 4, [int]$Session = 1, [string]$Created = '2026-10-19T10:00:00.0000000')
        [pscustomobject]@{ Key = "$Id|$Created"; Name = $Name; ProcessId = $Id; ParentProcessId = $Parent; SessionId = $Session; CommandLine = "$Name --fake"; CreationDate = $Created }
    }

    function script:New-FakeWindow {
        param([long]$Handle, [int]$ProcessId, [string]$ClassName = 'CabinetWClass', [bool]$Visible = $true, [string]$Title = 'okno')
        [pscustomobject]@{ Handle = $Handle; Title = $Title; ClassName = $ClassName; ProcessId = $ProcessId; Visible = $Visible }
    }

    function script:Set-FakeSnapshots {
        # Kolejne wywołania Get-C5ProcessSnapshot zwracają kolejne migawki; ostatnia zostaje na stałe.
        param([object[]]$List)
        $script:SnapQueue = [System.Collections.Generic.Queue[object]]::new()
        foreach ($Item in $List) { $script:SnapQueue.Enqueue(@($Item)) }
    }

    function script:New-FakeStartedProcess {
        param([int]$Id)
        $Fake = [pscustomobject]@{ Id = $Id; HasExited = $false; ExitCode = $null }
        $Fake | Add-Member -MemberType ScriptMethod -Name Refresh -Value { }
        $Fake
    }

    function script:New-FakeTaskCim {
        # Obiekt CimInstance w pamięci (bez WMI i bez Harmonogramu): parametry *-ScheduledTask wymagają PSTypeName klasy MSFT_Task*.
        param([string]$ClassName)
        New-Object Microsoft.Management.Infrastructure.CimInstance $ClassName, 'Root/Microsoft/Windows/TaskScheduler'
    }

    function script:Get-CommandOwner {
        # Nazwa funkcji, w której leży węzeł AST (albo '(poza funkcją)').
        param($Node)
        $Parent = $Node.Parent
        while ($null -ne $Parent -and $Parent -isnot [System.Management.Automation.Language.FunctionDefinitionAst]) { $Parent = $Parent.Parent }
        if ($null -ne $Parent) { $Parent.Name } else { '(poza funkcją)' }
    }
}

Describe 'Sondy C5 (mocki; procesy, Explorer, Appx, zadania i okna hosta nietknięte)' {
    BeforeEach {
        $script:Calls = [System.Collections.Generic.List[string]]::new()
        $script:SnapQueue = [System.Collections.Generic.Queue[object]]::new()
        # Bezpieczniki: wszystko, co uruchamia, zatrzymuje albo czyta system, rzuca, dopóki test nie nadpisze tego zachowaniem.
        Mock Start-Process { throw 'HOST: Start-Process niedozwolone w testach' }
        Mock Stop-Process { throw 'HOST: Stop-Process niedozwolone w testach' }
        Mock Get-Process { throw 'HOST: Get-Process niedozwolone w testach' }
        Mock Get-CimInstance { throw "HOST: Get-CimInstance niedozwolone w testach ($ClassName)" }
        Mock Invoke-CimMethod { throw "HOST: Invoke-CimMethod niedozwolone w testach ($MethodName)" }
        Mock Get-AppxPackage { throw 'HOST: Get-AppxPackage niedozwolone w testach' }
        Mock Add-AppxPackage { throw 'HOST: Add-AppxPackage niedozwolone w testach' }
        Mock Remove-AppxPackage { throw 'HOST: Remove-AppxPackage niedozwolone w testach' }
        Mock Register-ScheduledTask { throw 'HOST: Register-ScheduledTask niedozwolone w testach' }
        Mock Start-ScheduledTask { throw 'HOST: Start-ScheduledTask niedozwolone w testach' }
        Mock Stop-ScheduledTask { throw 'HOST: Stop-ScheduledTask niedozwolone w testach' }
        Mock Unregister-ScheduledTask { throw 'HOST: Unregister-ScheduledTask niedozwolone w testach' }
        Mock Get-ScheduledTask { throw 'HOST: Get-ScheduledTask niedozwolone w testach' }
        Mock Get-ScheduledTaskInfo { throw 'HOST: Get-ScheduledTaskInfo niedozwolone w testach' }
        Mock New-ScheduledTaskAction { throw 'HOST: New-ScheduledTaskAction niedozwolone w testach' }
        Mock New-ScheduledTaskPrincipal { throw 'HOST: New-ScheduledTaskPrincipal niedozwolone w testach' }
        Mock New-ScheduledTaskSettingsSet { throw 'HOST: New-ScheduledTaskSettingsSet niedozwolone w testach' }
        Mock Get-NetConnectionProfile { throw 'HOST: Get-NetConnectionProfile niedozwolone w testach' }
        Mock Add-Type { throw 'HOST: Add-Type niedozwolone w testach' }
        Mock Start-Job { throw 'HOST: Start-Job niedozwolone w testach' }
        Mock Wait-Job { throw 'HOST: Wait-Job niedozwolone w testach' }
        Mock Receive-Job { throw 'HOST: Receive-Job niedozwolone w testach' }
        Mock Stop-Job { throw 'HOST: Stop-Job niedozwolone w testach' }
        Mock Remove-Job { throw 'HOST: Remove-Job niedozwolone w testach' }
        Mock Get-ComputerRestorePoint { throw 'HOST: Get-ComputerRestorePoint niedozwolone w testach' }
        Mock Invoke-ExploreNative { throw 'HOST: Invoke-ExploreNative niedozwolone w testach' }
        Mock -ModuleName ExploreCommon Invoke-ExploreNative { throw 'HOST: Invoke-ExploreNative niedozwolone w testach' }
        Mock New-Item { throw 'HOST: New-Item niedozwolone w testach' }
        Mock Set-Content { throw 'HOST: Set-Content niedozwolone w testach' }
        Mock Get-Content { throw 'HOST: Get-Content niedozwolone w testach' }
        Mock Remove-Item { throw 'HOST: Remove-Item niedozwolone w testach' }
        Mock Move-Item { throw 'HOST: Move-Item niedozwolone w testach' }
        Mock Test-Path { throw 'HOST: Test-Path niedozwolone w testach' }
        Mock Get-Item { throw 'HOST: Get-Item niedozwolone w testach' }
        Mock Start-Sleep { if ($PesterBoundParameters.ContainsKey('Milliseconds')) { $script:Calls.Add("sleep-ms $Milliseconds") } else { $script:Calls.Add("sleep $Seconds") } }
        # Natywne P/Invoke (okna, token) i wyłącznik VM: w testach nigdy prawdziwe.
        Mock Initialize-C5Native { throw 'HOST: Initialize-C5Native niedozwolone w testach' }
        Mock Get-C5WindowList { throw 'HOST: Get-C5WindowList niedozwolone w testach' }
        Mock Close-C5Window { throw 'HOST: Close-C5Window niedozwolone w testach' }
        # Natywne zapytanie o token: (TokenIsElevated, TokenElevationType, błąd Win32); domyślnie token ograniczony (Limited).
        Mock Invoke-C5ElevationQuery { , @(0, 3, 0) }
        Mock Get-C5CurrentSessionId { 1 }
        Mock Assert-C5InsideVm { $script:Calls.Add('assert-vm') }
        Mock Get-C5ProcessSnapshot { if ($script:SnapQueue.Count -gt 1) { $script:SnapQueue.Dequeue() } elseif ($script:SnapQueue.Count -eq 1) { $script:SnapQueue.Peek() } else { @() } }
        Mock Get-C5ProcessOwner { [pscustomobject]@{ User = 'dso'; Domain = 'DSO-26H2-PRO'; Owner = 'DSO-26H2-PRO\dso'; OwnerSid = 'S-1-5-21-1-1001'; Error = $null } }
        $script:RegBlock = {
            [pscustomobject]@{ Key = "$Path\$Name"; KeyExists = $true; Exists = $false; Kind = $null; Value = $null; Hex = $null }
        }
        Mock Read-ExploreRegistryValue $script:RegBlock
        Mock -ModuleName ExploreCommon Read-ExploreRegistryValue $script:RegBlock
        Mock Read-ExploreRegistryKey { [pscustomobject]@{ Path = $Path; KeyExists = $true; Values = @(); SubKeys = @() } }
        Mock -ModuleName ExploreCommon Read-ExploreRegistryKey { [pscustomobject]@{ Path = $Path; KeyExists = $true; Values = @(); SubKeys = @() } }
    }

    Context 'Plan paneli (D1: 14 pozycji WinUtil + warianty MS z research)' {
        It '14 ID z Global Constraints; polecenie WinUtil każdej pozycji dosłownie z tabeli research (sekcja 1), z kluczem WPFPanel*' {
            $Plan = @(Get-C5PanelPlan)
            $ExpectedIds = @('panels.visual-effects', 'panels.power-options', 'panels.network-connections', 'panels.computer-management', 'panels.control-panel',
                'panels.mouse', 'panels.devices-printers', 'panels.programs-features', 'panels.region', 'panels.security-maintenance', 'panels.sound',
                'panels.system-properties', 'panels.date-time', 'panels.firewall', 'panels.system-restore')
            # 15 ID: visual-effects nie ma odpowiednika w WinUtil (research 1, wnioski), pozostałe 14 to lista WinUtil.
            @($Plan | ForEach-Object { $_.Id } | Sort-Object -Unique) | Should -Be @($ExpectedIds | Sort-Object)
            $Rows = [regex]::Matches($script:ResearchText, '(?m)^\| (WPFPanel\w+) \| [^|]+ \| `([^`]+)` \|')
            $Rows.Count | Should -Be 14
            $WinUtil = @($Plan | Where-Object { $_.Source -eq 'WinUtil' })
            $WinUtil.Count | Should -Be 14
            foreach ($Row in $Rows) {
                $Entry = @($WinUtil | Where-Object { $_.WinUtilKey -eq $Row.Groups[1].Value })
                $Entry.Count | Should -Be 1
                $Entry[0].Literal | Should -BeExactly $Row.Groups[2].Value
            }
        }
        It 'warianty MS (control.exe /name, netconnections, SystemPropertiesPerformance.exe) i v3.2 (sysdm.cpl,,3, control powercfg.cpl); nazwy kanoniczne są w research' {
            $Plan = @(Get-C5PanelPlan)
            $Plan.Count | Should -Be 30
            $Names = @($Plan | Where-Object { $_.Arguments -like '/name *' } | ForEach-Object { ($_.Arguments -split ' ')[1] })
            $Names | Should -Be @('Microsoft.PowerOptions', 'Microsoft.Mouse', 'Microsoft.DevicesAndPrinters', 'Microsoft.ProgramsAndFeatures', 'Microsoft.RegionAndLanguage',
                'Microsoft.Sound', 'Microsoft.System', 'Microsoft.DateAndTime', 'Microsoft.WindowsFirewall')
            foreach ($Name in $Names) { $script:ResearchText | Should -Match ([regex]::Escape("``$Name``")) }
            foreach ($Entry in @($Plan | Where-Object { $_.Arguments -like '/name *' })) { $Entry.File | Should -Be 'control.exe'; $Entry.Source | Should -Be 'MS' }
            $Visual = @($Plan | Where-Object { $_.Id -eq 'panels.visual-effects' })
            ($Visual | ForEach-Object { "$($_.File) $($_.Arguments)".Trim() }) | Should -Be @('SystemPropertiesPerformance.exe', 'control.exe sysdm.cpl,,3')
            $Network = @($Plan | Where-Object { $_.Id -eq 'panels.network-connections' })
            ($Network | ForEach-Object { "$($_.File) $($_.Arguments)".Trim() }) | Should -Be @('ncpa.cpl', 'control.exe netconnections')
            $Power = @($Plan | Where-Object { $_.Id -eq 'panels.power-options' })
            ($Power | ForEach-Object { "$($_.File) $($_.Arguments)".Trim() }) | Should -Be @('powercfg.cpl', 'control.exe powercfg.cpl', 'control.exe /name Microsoft.PowerOptions')
            foreach ($Text in 'SystemPropertiesPerformance.exe', 'control.exe sysdm.cpl,,3', 'control.exe netconnections', 'control powercfg.cpl') { $script:ResearchText | Should -Match ([regex]::Escape($Text)) }
            # Pary (Id, Variant) są unikalne.
            @($Plan | ForEach-Object { "$($_.Id)/$($_.Variant)" } | Sort-Object -Unique).Count | Should -Be 30
            @($Plan | ForEach-Object { $_.Name } | Sort-Object -Unique).Count | Should -Be 30
            foreach ($Entry in $Plan) { $Entry.Name | Should -Be "$($Entry.Id -replace '^panels\.', '')-$($Entry.Variant)" }
        }
        It 'sonda uzupełniająca (R20/R21): trzy warianty CreateProcess - control.exe wscui.cpl, control.exe sysdm.cpl (z zamknięciem SystemPropertiesAdvanced.exe), %SystemRoot%\explorer.exe shell:::{Urządzenia i drukarki}' {
            $Plan = @(Get-C5PanelPlan)
            $Extra = @($Plan | Where-Object { $_.Launcher -eq 'CreateProcess' })
            @($Extra | ForEach-Object { $_.Name }) | Should -Be @('devices-printers-explorer', 'security-maintenance-control', 'system-properties-control')
            $Security = $Extra | Where-Object { $_.Name -eq 'security-maintenance-control' }
            "$($Security.File) $($Security.Arguments)" | Should -Be 'control.exe wscui.cpl'
            $Security.Root | Should -Be 'System32'
            $System = $Extra | Where-Object { $_.Name -eq 'system-properties-control' }
            "$($System.File) $($System.Arguments)" | Should -Be 'control.exe sysdm.cpl'
            $System.PreCloseNames | Should -Be @('SystemPropertiesAdvanced.exe')
            $Printers = $Extra | Where-Object { $_.Name -eq 'devices-printers-explorer' }
            "$($Printers.File) $($Printers.Arguments)" | Should -Be 'explorer.exe shell:::{A8A91A66-3A7D-4424-8D24-04E180695C7A}'
            $Printers.Root | Should -Be 'SystemRoot'
            $Printers.ExplorerDetail | Should -BeTrue
            Resolve-C5SystemFile -Name 'explorer.exe' -Root 'SystemRoot' | Should -Be (Join-Path $env:SystemRoot 'explorer.exe')
            # Pozostałe warianty bez zmian: ShellExecute, System32, bez zamykania wstępnego i bez szczegółów Explorera.
            foreach ($Entry in @($Plan | Where-Object { $_.Launcher -ne 'CreateProcess' })) { $Entry.Launcher | Should -Be 'ShellExecute'; $Entry.Root | Should -Be 'System32'; @($Entry.PreCloseNames).Count | Should -Be 0; $Entry.ExplorerDetail | Should -BeFalse }
            # Nazwy -Variant w skrypcie i w Invoke-C5Exploration = nazwy planu.
            foreach ($Command in (Get-Command $script:C5ScriptPath), (Get-Command Invoke-C5Exploration)) {
                ($Command.Parameters['Variant'].Attributes | Where-Object { $_ -is [System.Management.Automation.ValidateSetAttribute] }).ValidValues | Should -Be @($Plan | ForEach-Object { $_.Name })
            }
        }
        It 'pliki to tylko nazwy rozwiązywane w System32 albo shell:::{CLSID}; Resolve-C5SystemFile odrzuca ścieżki, spacje i separatory' {
            foreach ($Entry in Get-C5PanelPlan) { $Entry.File | Should -Match '^([A-Za-z0-9]+\.(exe|cpl|msc)|shell:::\{[0-9A-F-]{36}\})\z' }
            Resolve-C5SystemFile -Name 'ncpa.cpl' | Should -Be (Join-Path $env:SystemRoot 'System32\ncpa.cpl')
            Resolve-C5SystemFile -Name 'shell:::{A8A91A66-3A7D-4424-8D24-04E180695C7A}' | Should -Be 'shell:::{A8A91A66-3A7D-4424-8D24-04E180695C7A}'
            foreach ($Bad in 'C:\Windows\x.exe', '..\x.exe', 'a b.exe', 'x.exe;calc', 'x\y.cpl', 'shell:::{zly}', 'ms-settings:about') {
                { Resolve-C5SystemFile -Name $Bad } | Should -Throw
            }
        }
    }

    Context 'Procesy: różnica przed/po i sprzątanie tylko nowych' {
        It 'Get-C5NewProcesses: nowy PID i ponownie użyty PID (inny CreationDate) są nowe; stare nie' {
            $Before = @((New-FakeProc 1 'a.exe'), (New-FakeProc 2 'b.exe'))
            $After = @((New-FakeProc 1 'a.exe'), (New-FakeProc 2 'c.exe' -Created '2026-10-19T11:00:00.0000000'), (New-FakeProc 3 'd.exe'))
            $New = @(Get-C5NewProcesses -Before $Before -After $After)
            @($New | ForEach-Object { $_.ProcessId }) | Should -Be @(2, 3)
            @(Get-C5NewProcesses -Before $After -After $After).Count | Should -Be 0
        }
        It 'Select-C5CleanupTargets: potomkowie uruchomionego i hosty paneli z tej samej sesji; nigdy explorer.exe, inne sesje, procesy chronione ani obce' {
            $New = @(
                (New-FakeProc 100 'control.exe' -Parent 50), (New-FakeProc 101 'rundll32.exe' -Parent 100), (New-FakeProc 102 'explorer.exe' -Parent 100),
                (New-FakeProc 107 'colorcpl.exe' -Parent 102), (New-FakeProc 103 'svchost.exe' -Parent 4 -Session 0), (New-FakeProc 104 'rundll32.exe' -Parent 4 -Session 0),
                (New-FakeProc 105 'mmc.exe' -Parent 4), (New-FakeProc 106 'WmiPrvSE.exe' -Parent 4 -Session 0), (New-FakeProc 108 'SystemSettings.exe' -Parent 9),
                (New-FakeProc 109 'ApplicationFrameHost.exe' -Parent 9)
            )
            $Targets = @(Select-C5CleanupTargets -NewProcesses $New -LaunchedPid 100 -AllowNames (Get-C5PanelAllowNames) -SessionId 1 -ProtectedPids @(105))
            @($Targets | ForEach-Object { $_.ProcessId }) | Should -Be @(100, 101, 107, 108)
            @(Select-C5CleanupTargets -NewProcesses @() -LaunchedPid 0 -AllowNames (Get-C5PanelAllowNames) -SessionId 1).Count | Should -Be 0
            # Bez uruchomionego PID (np. shell:::) zostają tylko hosty paneli z tej sesji.
            @(Select-C5CleanupTargets -NewProcesses $New -LaunchedPid 0 -AllowNames (Get-C5PanelAllowNames) -SessionId 1 | ForEach-Object { $_.ProcessId }) | Should -Be @(100, 101, 105, 108)
            (Get-C5PanelAllowNames) | Should -Not -Contain 'explorer.exe'
        }
        It 'Select-C5WindowsToClose: tylko NOWE, widoczne okna CabinetWClass należące do explorer.exe (żadnych okien powłoki ani obcych)' {
            $Before = @((New-FakeWindow 1 500))
            $Now = @((New-FakeWindow 1 500), (New-FakeWindow 2 500), (New-FakeWindow 3 500 -Visible $false), (New-FakeWindow 4 500 -ClassName 'Shell_TrayWnd'),
                (New-FakeWindow 5 777), (New-FakeWindow 6 500 -ClassName '#32770'))
            @(Select-C5WindowsToClose -Before $Before -Now $Now -ExplorerPids @(500) | ForEach-Object { $_.Handle }) | Should -Be @(2)
        }
        It 'Wait-C5WindowsClosed: czeka (co 250 ms, limit 5 s), aż uchwyt zniknie; okno znikające przy 3. odczycie => pusto po 2 przerwach; nie znika => zwrócone po 20 odczytach' {
            $script:WinCall = 0
            Mock Get-C5WindowList { $script:WinCall++; if ($script:WinCall -lt 3) { @((New-FakeWindow 2 500), (New-FakeWindow 7 500)) } else { @((New-FakeWindow 7 500)) } }
            @(Wait-C5WindowsClosed -Handles @(2)).Count | Should -Be 0
            $script:WinCall | Should -Be 3
            @($script:Calls | Where-Object { $_ -eq 'sleep-ms 250' }).Count | Should -Be 2
            $script:Calls.Clear()
            Mock Get-C5WindowList { @((New-FakeWindow 2 500)) }
            @(Wait-C5WindowsClosed -Handles @(2)) | Should -Be @(2)
            # Licznik Should -Invoke obejmuje oba mocki w tym It: 3 (pierwszy przypadek) + 20 (limit 5 s / 250 ms).
            Should -Invoke Get-C5WindowList -Times 23 -Exactly
            @($script:Calls | Where-Object { $_ -eq 'sleep-ms 250' }).Count | Should -Be 19
            @(Wait-C5WindowsClosed -Handles @()).Count | Should -Be 0
        }
        It 'Invoke-C5Cleanup (K2): okno, które nie zniknęło po WM_CLOSE, trafia do WindowsStillOpen' {
            Set-FakeSnapshots -List @(, @((New-FakeProc 500 'explorer.exe')))
            Mock Get-C5WindowList { @((New-FakeWindow 1 500), (New-FakeWindow 2 500 -Title 'Opcje zasilania')) }
            Mock Close-C5Window { $true }
            $Cleanup = Invoke-C5Cleanup -Before @((New-FakeProc 500 'explorer.exe')) -BeforeWindows @((New-FakeWindow 1 500)) -AllowNames (Get-C5PanelAllowNames) -WithWindows
            $Cleanup.ClosedWindows[0] | Should -Match 'Opcje zasilania'
            $Cleanup.WindowsStillOpen | Should -Be @('uchwyt 2')
        }
        It 'Select-C5ShellWindows: wszystkie widoczne CabinetWClass i ApplicationFrameWindow (nowe i stare), bez ukrytych i innych klas' {
            $Now = @((New-FakeWindow 1 500), (New-FakeWindow 2 900 -ClassName 'ApplicationFrameWindow'), (New-FakeWindow 3 500 -Visible $false), (New-FakeWindow 4 500 -ClassName 'Shell_TrayWnd'), (New-FakeWindow 5 101 -ClassName '#32770'))
            @(Select-C5ShellWindows -Now $Now | ForEach-Object { $_.Handle }) | Should -Be @(1, 2)
        }
        It 'Get-C5ParentName: nazwa rodzica z tej samej migawki; ponownie użyty PID (rodzic młodszy) i brak rodzica => $null' {
            $Child = New-FakeProc 600 'explorer.exe' -Parent 77 -Created '2026-10-19T10:10:00.0000000'
            Get-C5ParentName -Entry $Child -Snapshot @((New-FakeProc 77 'winlogon.exe' -Created '2026-10-19T09:00:00.0000000'), $Child) | Should -Be 'winlogon.exe'
            Get-C5ParentName -Entry $Child -Snapshot @((New-FakeProc 77 'notepad.exe' -Created '2026-10-19T11:00:00.0000000'), $Child) | Should -BeNullOrEmpty
            Get-C5ParentName -Entry $Child -Snapshot @($Child) | Should -BeNullOrEmpty
        }
        It 'Select-C5NewExplorerTargets: nigdy powłoka ani obcy nowy explorer.exe dso w sesji powłoki; tak: uruchomiony, jego potomkowie, inna sesja, inny właściciel' {
            $New = @(
                (New-FakeProc 500 'explorer.exe'), (New-FakeProc 700 'explorer.exe' -Parent 1), (New-FakeProc 701 'explorer.exe' -Parent 700),
                (New-FakeProc 800 'explorer.exe' -Parent 77), (New-FakeProc 801 'explorer.exe' -Parent 3 -Session 0), (New-FakeProc 802 'explorer.exe' -Parent 3), (New-FakeProc 703 'conhost.exe' -Parent 700)
            )
            $Owners = @{ 500 = 'dso'; 700 = 'dso'; 701 = 'dso'; 800 = 'dso'; 801 = 'Administrator'; 802 = 'Administrator' }
            @(Select-C5NewExplorerTargets -NewProcesses $New -ShellPid 500 -ShellSessionId 1 -LaunchedPid 700 -OwnerUserByPid $Owners | ForEach-Object { $_.ProcessId }) | Should -Be @(700, 701, 801, 802)
            # Bez uruchomionego PID (przekazanie do powłoki) nowy explorer.exe dso w sesji powłoki nie jest celem.
            @(Select-C5NewExplorerTargets -NewProcesses $New -ShellPid 500 -ShellSessionId 1 -OwnerUserByPid $Owners | ForEach-Object { $_.ProcessId }) | Should -Be @(801, 802)
        }
        It 'Invoke-C5LaunchProbe (K2): start -> 3 s -> odczyt -> 7 s -> odczyt -> sprzątanie; Stop-Process tylko dla nowych celów, okno Explorera przez WM_CLOSE, Explorer nietknięty' {
            $Base = @((New-FakeProc 500 'explorer.exe' -Parent 400), (New-FakeProc 600 'svchost.exe' -Session 0))
            $Spawned = $Base + @((New-FakeProc 100 'control.exe' -Parent 900), (New-FakeProc 101 'rundll32.exe' -Parent 100), (New-FakeProc 103 'WmiPrvSE.exe' -Session 0),
                (New-FakeProc 108 'SystemSettings.exe' -Parent 9))
            Set-FakeSnapshots -List @(, $Base)
            $script:SnapQueue.Enqueue(@($Spawned)); $script:SnapQueue.Enqueue(@($Spawned)); $script:SnapQueue.Enqueue(@($Spawned)); $script:SnapQueue.Enqueue(@($Base))
            $script:WinCall = 0
            $script:AfterClose = -1
            Mock Get-C5WindowList {
                $script:WinCall++
                if ($script:AfterClose -ge 0) { $script:AfterClose++ }
                if ($script:WinCall -eq 1) { return @((New-FakeWindow 1 500)) }
                # Okno 2 znika dopiero przy 3. odczycie po WM_CLOSE (zamknięcie jest asynchroniczne).
                if ($script:AfterClose -ge 3) { return @((New-FakeWindow 1 500), (New-FakeWindow 3 101 -ClassName '#32770')) }
                @((New-FakeWindow 1 500), (New-FakeWindow 2 500 -Title 'Panel sterowania'), (New-FakeWindow 3 101 -ClassName '#32770'), (New-FakeWindow 4 900 -ClassName 'ApplicationFrameWindow' -Title 'Ustawienia'), (New-FakeWindow 5 500 -Visible $false))
            }
            Mock Close-C5Window { $script:Calls.Add("close $Handle"); $script:AfterClose = 0; $true }
            Mock Start-Process { $script:Calls.Add("start $FilePath|$ArgumentList"); New-FakeStartedProcess -Id 100 }
            Mock Stop-Process { $script:Calls.Add("stop $Id") }
            $Record = Invoke-C5LaunchProbe -FilePath 'C:\Windows\System32\control.exe' -Arguments '/name Microsoft.Sound' -AllowNames (Get-C5PanelAllowNames) -WithWindows
            $Record.Launch.ProcessId | Should -Be 100
            @($Record.At3s.NewProcesses | ForEach-Object { $_.ProcessId }) | Should -Be @(100, 101, 103, 108)
            $Record.At3s.NewProcesses[0].Owner | Should -Be 'DSO-26H2-PRO\dso'
            @($Record.At10s.NewWindows | ForEach-Object { $_.Handle }) | Should -Be @(2, 3, 4)
            # Wszystkie widoczne okna Eksploratora i Ustawień (także stare: ponowne użycie okna nie daje nowego uchwytu).
            @($Record.At3s.ShellWindows | ForEach-Object { $_.Handle }) | Should -Be @(1, 2, 4)
            $Record.At10s.ShellWindows[1].Title | Should -Be 'Panel sterowania'
            $Record.Launch.LaunchedAt | Should -Not -BeNullOrEmpty
            $Record.At3s.ElapsedSinceLaunchMs | Should -Not -BeNullOrEmpty
            $Record.At10s.ElapsedSinceLaunchMs | Should -BeGreaterOrEqual $Record.At3s.ElapsedSinceLaunchMs
            ($Record.At3s.NewProcesses | Where-Object { $_.ProcessId -eq 101 }).ParentName | Should -Be 'control.exe'
            @($Record.Cleanup.WindowsStillOpen).Count | Should -Be 0
            @($script:Calls | Where-Object { $_ -eq 'sleep-ms 250' }).Count | Should -Be 2
            @($Record.At10s.SettingsHosts | Where-Object { $_.IsNew } | ForEach-Object { $_.Name }) | Should -Be @('SystemSettings.exe')
            $Order = @($script:Calls | Where-Object { $_ -match '^(start|sleep|stop|close)' })
            $Order[0] | Should -Be 'start C:\Windows\System32\control.exe|/name Microsoft.Sound'
            $Order[1] | Should -Be 'sleep 3'
            $Order[2] | Should -Be 'sleep 7'
            @($script:Calls | Where-Object { $_ -like 'stop *' }) | Should -Be @('stop 100', 'stop 101', 'stop 108')
            @($script:Calls | Where-Object { $_ -like 'close *' }) | Should -Be @('close 2')
            $Record.Cleanup.LeftProcesses | Should -Be @('WmiPrvSE.exe (103)')
            @($Record.Cleanup.RemainingAfterStop).Count | Should -Be 0
            Should -Invoke Stop-Process -Times 0 -Exactly -ParameterFilter { $Id -eq 500 -or $Id -eq 600 -or $Id -eq 103 }
        }
        It 'Invoke-C5LaunchProbe (K1): bez okien (Get-C5WindowList nie jest wołane); błąd uruchomienia to dana, bez czekania, sprzątanie nadal jest' {
            Set-FakeSnapshots -List @(, @((New-FakeProc 500 'explorer.exe')))
            Mock Start-Process { throw 'nie znaleziono pliku' }
            $Record = Invoke-C5LaunchProbe -FilePath 'C:\Windows\System32\brak.cpl' -Arguments '' -AllowNames (Get-C5PanelAllowNames)
            $Record.Launch.Error | Should -Match 'nie znaleziono pliku'
            $Record.At3s | Should -BeNullOrEmpty
            @($script:Calls | Where-Object { $_ -like 'sleep*' }).Count | Should -Be 0
            $Record.Cleanup | Should -Not -BeNullOrEmpty
            Should -Invoke Get-C5WindowList -Times 0 -Exactly
            Should -Invoke Stop-Process -Times 0 -Exactly
        }
        It 'Start-C5Process: bez argumentów nie podaje -ArgumentList (Start-Process odrzuca pusty), z argumentami podaje je jednym łańcuchem' {
            Mock Start-Process { $script:Calls.Add("start $FilePath|$($PesterBoundParameters.ContainsKey('ArgumentList'))|$ArgumentList"); New-FakeStartedProcess -Id 5 }
            (Start-C5Process -FilePath 'C:\Windows\System32\ncpa.cpl' -Arguments '').ProcessId | Should -Be 5
            $null = Start-C5Process -FilePath 'C:\Windows\System32\control.exe' -Arguments 'sysdm.cpl,,3'
            @($script:Calls | Where-Object { $_ -like 'start *' }) | Should -Be @('start C:\Windows\System32\ncpa.cpl|False|', 'start C:\Windows\System32\control.exe|True|sysdm.cpl,,3')
            Should -Invoke Start-Process -Times 2 -Exactly -ParameterFilter { $PassThru.IsPresent }
        }
    }

    Context 'Panele (sondy 1-4)' {
        It 'Invoke-C5PanelVariant: rekord z ID, wariantem, dosłownym poleceniem i plikiem z System32 (FileExists z Test-Path); shell::: bez Test-Path' {
            Mock Test-Path { $true }
            Mock Invoke-C5LaunchProbe { [pscustomobject]@{ FilePath = $FilePath; Arguments = $Arguments; Launch = [pscustomobject]@{ ProcessId = 1; Error = $null }; At3s = $null; At10s = $null; Cleanup = $null; Error = $null } }
            $Plan = @(Get-C5PanelPlan)
            $Sound = $Plan | Where-Object { $_.Id -eq 'panels.sound' -and $_.Source -eq 'WinUtil' }
            $Record = Invoke-C5PanelVariant -Variant $Sound -WithWindows
            $Record.Id | Should -Be 'panels.sound'
            $Record.Literal | Should -Be 'mmsys.cpl'
            $Record.FilePath | Should -Be (Join-Path $env:SystemRoot 'System32\mmsys.cpl')
            $Record.FileExists | Should -BeTrue
            Should -Invoke Invoke-C5LaunchProbe -Times 1 -Exactly -ParameterFilter { $WithWindows.IsPresent -and $AllowNames -contains 'rundll32.exe' }
            $Printer = $Plan | Where-Object { $_.Id -eq 'panels.devices-printers' -and $_.Source -eq 'WinUtil' }
            $Record2 = Invoke-C5PanelVariant -Variant $Printer
            $Record2.FilePath | Should -Be 'shell:::{A8A91A66-3A7D-4424-8D24-04E180695C7A}'
            $Record2.FileExists | Should -BeNullOrEmpty
            Should -Invoke Test-Path -Times 1 -Exactly
        }
        It 'Invoke-C5PanelRun: błąd jednego wariantu nie przerywa pozostałych (30 rekordów, błąd w polu Error); okna tylko w K2' {
            Mock Invoke-C5PanelVariant { if ($Variant.Id -eq 'panels.mouse') { throw 'zawieszony panel' }; [pscustomobject]@{ Id = $Variant.Id; Variant = $Variant.Variant; Windows = $WithWindows.IsPresent; Error = $null } }
            $K2 = @(Invoke-C5PanelRun -Context 'K2')
            $K2.Count | Should -Be 30
            @($K2 | Where-Object { $_.Error -match 'zawieszony panel' }).Count | Should -Be 2
            @($K2 | Where-Object { $_.Error -match 'zawieszony panel' } | ForEach-Object { $_.Id } | Sort-Object -Unique) | Should -Be @('panels.mouse')
            @($K2 | Where-Object { -not $_.Error } | ForEach-Object { $_.Windows } | Sort-Object -Unique) | Should -Be @($true)
            $K1 = @(Invoke-C5PanelRun -Context 'K1')
            @($K1 | Where-Object { -not $_.Error } | ForEach-Object { $_.Windows } | Sort-Object -Unique) | Should -Be @($false)
            $script:Calls | Should -Contain 'assert-vm'
        }
        It 'Get-C5SystemProtectionState: liczba punktów przywracania i zrzut klucza SystemRestore (tylko odczyt); błąd cmdletu to dana' {
            Mock Get-ComputerRestorePoint { }
            $State = Get-C5SystemProtectionState
            $State.RestorePointCount | Should -Be 0
            $State.SystemRestoreKey.Path | Should -Be 'HKLM\SOFTWARE\Microsoft\Windows NT\CurrentVersion\SystemRestore'
            Mock Get-ComputerRestorePoint { throw 'usługa wyłączona' }
            (Get-C5SystemProtectionState).RestorePointError | Should -Match 'usługa wyłączona'
        }
        It 'sonda panels: ochrona systemu (K1), panele K1, panele K2 przez zadanie Harmonogramu z limitem zależnym od liczby wariantów' {
            Mock Get-C5SystemProtectionState { [pscustomobject]@{ RestorePointCount = 0 } }
            Mock Invoke-C5PanelRun { [pscustomobject]@{ Id = 'x'; Context = $Context } }
            Mock Invoke-C5UserTask { [pscustomobject]@{ Name = $Name; Context = 'K2'; Available = $true; Body = $Body; Timeout = $TimeoutSeconds; Data = @() } }
            $Records = @(Invoke-C5PanelsProbe)
            @($Records | ForEach-Object { $_.Name }) | Should -Be @('ochrona-systemu', 'panele-k1', 'panele-k2')
            $Records[1].Data[0].Context | Should -Be 'K1'
            $Records[2].Body | Should -Be "Invoke-C5PanelRun -Context 'K2'"
            $Records[2].Timeout | Should -BeGreaterOrEqual (27 * 20)
            $script:Calls[0] | Should -Be 'assert-vm'
        }
        It 'sonda panels: błąd części K1 jest daną i nie blokuje K2' {
            Mock Get-C5SystemProtectionState { throw 'WMI padło' }
            Mock Invoke-C5PanelRun { throw 'K1 padło' }
            Mock Invoke-C5UserTask { [pscustomobject]@{ Name = $Name; Context = 'K2'; Available = $true; Data = @() } }
            $Records = @(Invoke-C5PanelsProbe)
            $Records[0].Error | Should -Match 'WMI padło'
            $Records[1].Error | Should -Match 'K1 padło'
            $Records[2].Name | Should -Be 'panele-k2'
        }
    }

    Context 'Sonda uzupełniająca paneli (R20/R21): CreateProcess, zamknięcie wstępne, explorer.exe shell:::, filtr -Variant' {
        BeforeEach {
            Mock Get-C5ProcessOwner {
                if ($ProcessId -eq 700) { [pscustomobject]@{ User = 'Administrator'; Domain = 'X'; Owner = 'X\Administrator'; OwnerSid = 'S-1-5-21-1-500'; Error = $null } }
                else { [pscustomobject]@{ User = 'dso'; Domain = 'X'; Owner = 'X\dso'; OwnerSid = 'S-1-5-21-1-1001'; Error = $null } }
            }
            $script:Shell = [pscustomobject]@{ Key = '500|2026-10-19T09:00:00.0000000'; Name = 'explorer.exe'; ProcessId = 500; ParentProcessId = 4; SessionId = 1; CommandLine = 'C:\Windows\Explorer.EXE'; CreationDate = '2026-10-19T09:00:00.0000000' }
        }
        It 'Start-C5Process -UseCreateProcess: Start-C5CreateProcess zamiast Start-Process (ShellExecute), metoda zapisana' {
            Mock Start-C5CreateProcess { $script:Calls.Add("create $FilePath|$Arguments"); New-FakeStartedProcess -Id 41 }
            $Started = Start-C5Process -FilePath 'C:\Windows\System32\control.exe' -Arguments 'wscui.cpl' -UseCreateProcess
            $Started.ProcessId | Should -Be 41
            $Started.Method | Should -Be 'CreateProcess'
            $script:Calls | Should -Contain 'create C:\Windows\System32\control.exe|wscui.cpl'
            Should -Invoke Start-Process -Times 0 -Exactly
            (Start-C5Process -FilePath 'x' -Arguments '' -UseCreateProcess:$false).Method | Should -Be 'ShellExecute'
        }
        It 'Invoke-C5PanelVariant: system-properties-control - zamknięcie wstępne SystemPropertiesAdvanced.exe PRZED uruchomieniem, CreateProcess, migawka startowa przebiegu' {
            Mock Test-Path { $true }
            Mock Invoke-C5PreClean { $script:Calls.Add("preclean $($Names -join ',') base=$(@($Baseline).Count)"); [pscustomobject]@{ Stopped = @('SystemPropertiesAdvanced.exe (90), sesja 1') } }
            Mock Invoke-C5LaunchProbe { $script:Calls.Add("launch $FilePath|$Arguments|create=$($UseCreateProcess.IsPresent)|explorer=$($ExplorerDetail.IsPresent)"); [pscustomobject]@{ Launch = $null; At3s = $null; At10s = $null; Cleanup = $null; Error = $null } }
            $Variant = Get-C5PanelPlan | Where-Object { $_.Name -eq 'system-properties-control' }
            $Record = Invoke-C5PanelVariant -Variant $Variant -WithWindows -RunBaseline @((New-FakeProc 1 'a.exe'), (New-FakeProc 2 'b.exe'))
            @($script:Calls | Where-Object { $_ -match '^(preclean|launch)' }) | Should -Be @('preclean SystemPropertiesAdvanced.exe base=2', ("launch $(Join-Path $env:SystemRoot 'System32\control.exe')|sysdm.cpl|create=True|explorer=False"))
            $Record.Name | Should -Be 'system-properties-control'
            $Record.Launcher | Should -Be 'CreateProcess'
            $Record.PreClean.Stopped[0] | Should -Match 'SystemPropertiesAdvanced'
            $Printers = Get-C5PanelPlan | Where-Object { $_.Name -eq 'devices-printers-explorer' }
            $Record2 = Invoke-C5PanelVariant -Variant $Printers
            $Record2.FilePath | Should -Be (Join-Path $env:SystemRoot 'explorer.exe')
            $script:Calls | Should -Contain ("launch $(Join-Path $env:SystemRoot 'explorer.exe')|shell:::{A8A91A66-3A7D-4424-8D24-04E180695C7A}|create=True|explorer=True")
            $Record2.PreClean | Should -BeNullOrEmpty
            Should -Invoke Invoke-C5PreClean -Times 1 -Exactly
        }
        It 'Invoke-C5PreClean: zamyka tylko procesy o tej nazwie powstałe w tym przebiegu w tej sesji; starsze i z innej sesji zapisuje' {
            $Baseline = @((New-FakeProc 80 'SystemPropertiesAdvanced.exe'))
            Set-FakeSnapshots -List @(, @((New-FakeProc 80 'SystemPropertiesAdvanced.exe'), (New-FakeProc 90 'SystemPropertiesAdvanced.exe'), (New-FakeProc 91 'SystemPropertiesAdvanced.exe' -Session 0), (New-FakeProc 92 'notepad.exe')))
            Mock Stop-Process { $script:Calls.Add("stop $Id") }
            $Out = Invoke-C5PreClean -Names 'SystemPropertiesAdvanced.exe' -Baseline $Baseline
            @($script:Calls | Where-Object { $_ -like 'stop *' }) | Should -Be @('stop 90')
            $Out.PreExisting | Should -Be @('SystemPropertiesAdvanced.exe (80), sesja 1')
            $Out.OtherSession | Should -Be @('SystemPropertiesAdvanced.exe (91), sesja 0')
            $script:Calls | Should -Contain 'sleep-ms 1000'
            $script:Calls.Clear()
            Set-FakeSnapshots -List @(, $Baseline)
            $Quiet = Invoke-C5PreClean -Names 'SystemPropertiesAdvanced.exe' -Baseline $Baseline
            @($Quiet.Stopped).Count | Should -Be 0
            $script:Calls | Should -Not -Contain 'sleep-ms 1000'
        }
        It 'Select-C5PanelVariants: pusto = wszystkie 30; nazwy jako tablica albo "a,b" (z zadania K2) w kolejności planu; nieznana nazwa => wyjątek' {
            @(Select-C5PanelVariants -Names $null).Count | Should -Be 30
            @(Select-C5PanelVariants -Names @()).Count | Should -Be 30
            @(Select-C5PanelVariants -Names 'system-properties-control', 'devices-printers-explorer' | ForEach-Object { $_.Name }) | Should -Be @('devices-printers-explorer', 'system-properties-control')
            @(Select-C5PanelVariants -Names 'security-maintenance-control,system-properties-control' | ForEach-Object { $_.Name }) | Should -Be @('security-maintenance-control', 'system-properties-control')
            { Select-C5PanelVariants -Names 'nie-ma-takiego' } | Should -Throw '*Nieznane warianty*'
        }
        It 'Invoke-C5PanelRun -Variant: tylko wybrane warianty, z migawką startową przebiegu przekazaną do każdego' {
            Set-FakeSnapshots -List @(, @((New-FakeProc 1 'a.exe')))
            Mock Invoke-C5PanelVariant { [pscustomobject]@{ Name = $Variant.Name; BaseCount = @($RunBaseline).Count } }
            $Records = @(Invoke-C5PanelRun -Context 'K2' -Variant 'security-maintenance-control,devices-printers-explorer')
            @($Records | ForEach-Object { $_.Name }) | Should -Be @('devices-printers-explorer', 'security-maintenance-control')
            @($Records | ForEach-Object { $_.BaseCount } | Sort-Object -Unique) | Should -Be @(1)
        }
        It 'sonda panels z -Variant: K1 dostaje te same nazwy, K2 treść "-Variant ''a,b,c''" i limit wg liczby wybranych' {
            Mock Get-C5SystemProtectionState { [pscustomobject]@{ RestorePointCount = 0 } }
            Mock Invoke-C5PanelRun { [pscustomobject]@{ Context = $Context; Variant = @($Variant) } }
            Mock Invoke-C5UserTask { [pscustomobject]@{ Name = $Name; Body = $Body; Timeout = $TimeoutSeconds; Data = @() } }
            $Names = @('security-maintenance-control', 'system-properties-control', 'devices-printers-explorer')
            $Records = @(Invoke-C5PanelsProbe -Variant $Names)
            $Records[1].Data[0].Variant | Should -Be $Names
            $Records[2].Body | Should -Be "Invoke-C5PanelRun -Context 'K2' -Variant 'security-maintenance-control,system-properties-control,devices-printers-explorer'"
            $Records[2].Timeout | Should -Be (3 * 25 + 60)
        }
        It 'Invoke-C5Collection -Variant: sonda panels dostaje filtr, a dokument ma pole panelVariants' {
            Mock Get-C5SelfTokenInfo { [pscustomobject]@{ UserName = 'X\Administrator' } }
            Mock Get-C5ShellProcess { $script:Shell }
            Mock -ModuleName ExploreCommon Get-ExploreMeta { [ordered]@{ EditionID = 'Professional' } }
            Mock Invoke-C5PanelsProbe { [pscustomobject]@{ Name = 'p'; Got = @($Variant) } }
            $Doc = Invoke-C5Collection -UserSid 'S-1' -Probe 'panels' -Variant 'system-properties-control', 'devices-printers-explorer' | ConvertFrom-Json
            @($Doc.probes.panels[0].Got) | Should -Be @('system-properties-control', 'devices-printers-explorer')
            @($Doc.panelVariants) | Should -Be @('system-properties-control', 'devices-printers-explorer')
            $Doc2 = Invoke-C5Collection -UserSid 'S-1' -Probe 'panels' | ConvertFrom-Json
            @($Doc2.probes.panels[0].Got).Count | Should -Be 0
        }
        It 'Select-C5ShellPid: pierwsza powłoka bez /factory; same /factory => pierwsza; pusto => $null' {
            $Factory = [pscustomobject]@{ ProcessId = 600; CommandLine = 'C:\Windows\explorer.exe /factory,{5BD95610-9434-43C2-886C-57852CC8A120} -Embedding' }
            (Select-C5ShellPid -Shells @($Factory, $script:Shell)).ProcessId | Should -Be 500
            (Select-C5ShellPid -Shells @($Factory)).ProcessId | Should -Be 600
            Select-C5ShellPid -Shells @() | Should -BeNullOrEmpty
        }
        It 'Select-C5NewExplorerTargets bez znanej powłoki (ShellSessionId -1): tylko uruchomiony i jego potomkowie' {
            $New = @((New-FakeProc 700 'explorer.exe' -Session 0), (New-FakeProc 800 'explorer.exe' -Parent 77 -Session 1))
            @(Select-C5NewExplorerTargets -NewProcesses $New -ShellPid 0 -ShellSessionId -1 -LaunchedPid 700 -OwnerUserByPid @{ 800 = 'Administrator' } | ForEach-Object { $_.ProcessId }) | Should -Be @(700)
        }
        It 'devices-printers-explorer w K1: nowy podniesiony explorer.exe Administratora (sesja 0) - token i właściciel zapisane, zatrzymany TYLKO on, powłoka dso nietknięta' {
            $Spawned = @($script:Shell, (New-FakeProc 700 'explorer.exe' -Parent 900 -Session 0))
            Set-FakeSnapshots -List @(@($script:Shell), $Spawned)
            Mock Start-C5CreateProcess { New-FakeStartedProcess -Id 700 }
            Mock Invoke-C5ElevationQuery { if ($ProcessId -eq 700) { , @(1, 1, 0) } else { , @(0, 3, 0) } }
            Mock Stop-Process { $script:Calls.Add("stop $Id") }
            $Record = Invoke-C5LaunchProbe -FilePath (Join-Path $env:SystemRoot 'explorer.exe') -Arguments 'shell:::{A8A91A66-3A7D-4424-8D24-04E180695C7A}' -UseCreateProcess -ExplorerDetail
            $Record.Launch.Method | Should -Be 'CreateProcess'
            @($Record.At3s.NewExplorers | ForEach-Object { $_.ProcessId }) | Should -Be @(700)
            $Record.At10s.NewExplorers[0].Owner | Should -Be 'X\Administrator'
            $Record.At10s.NewExplorers[0].SessionId | Should -Be 0
            $Record.At10s.NewExplorers[0].Token.Elevated | Should -BeTrue
            $Record.At10s.UserShellPid | Should -Be 500
            $Record.At10s.LaunchedHasExited | Should -BeFalse
            @($script:Calls | Where-Object { $_ -like 'stop *' }) | Should -Be @('stop 700')
            $Record.Cleanup.ExplorerShellPid | Should -Be 500
            Should -Invoke Get-C5WindowList -Times 0 -Exactly
        }
        It 'devices-printers-explorer w K2: uruchomiony proces się kończy (przekazanie), okno w powłoce - właściciel okna = powłoka dso z tokenem; okno zamknięte WM_CLOSE, nic nie zatrzymane' {
            Set-FakeSnapshots -List @(, @($script:Shell))
            Mock Start-C5CreateProcess { $Fake = New-FakeStartedProcess -Id 700; $Fake.HasExited = $true; $Fake.ExitCode = 1; $Fake }
            $script:WinCall = 0
            $script:Closed = $false
            Mock Get-C5WindowList {
                $script:WinCall++
                if ($script:WinCall -eq 1 -or $script:Closed) { return @((New-FakeWindow 1 500 -ClassName 'Progman')) }
                @((New-FakeWindow 1 500 -ClassName 'Progman'), (New-FakeWindow 2 500 -Title 'Urządzenia i drukarki — Eksplorator plików'))
            }
            Mock Close-C5Window { $script:Calls.Add("close $Handle"); $script:Closed = $true; $true }
            Mock Stop-Process { $script:Calls.Add("stop $Id") }
            $Record = Invoke-C5LaunchProbe -FilePath (Join-Path $env:SystemRoot 'explorer.exe') -Arguments 'shell:::{A8A91A66-3A7D-4424-8D24-04E180695C7A}' -WithWindows -UseCreateProcess -ExplorerDetail
            $Record.At3s.LaunchedHasExited | Should -BeTrue
            $Record.At3s.LaunchedExitCode | Should -Be 1
            @($Record.At3s.NewExplorers).Count | Should -Be 0
            $Record.At10s.ShellWindows[0].Title | Should -Be 'Urządzenia i drukarki — Eksplorator plików'
            $Owner = $Record.At10s.WindowOwners | Where-Object { $_.ProcessId -eq 500 }
            $Owner.IsUserShell | Should -BeTrue
            $Owner.IsNew | Should -BeFalse
            $Owner.Token.ElevationTypeName | Should -Be 'Limited'
            $script:Calls | Should -Contain 'close 2'
            @($script:Calls | Where-Object { $_ -like 'stop *' }).Count | Should -Be 0
            @($Record.Cleanup.WindowsStillOpen).Count | Should -Be 0
        }
    }
    Context 'K2: zadanie Harmonogramu w sesji dso (Interactive, Highest)' {
        BeforeEach {
            $script:Written = $null
            $script:Seen = @{}
            $script:ResultPolls = 0
            $script:ResultAfter = 3
            Mock New-Item { $script:Calls.Add("mkdir $Path") }
            Mock Set-Content { $script:Written = @{ Path = $LiteralPath; Value = $Value; Encoding = [string]$Encoding }; $script:Calls.Add("write $LiteralPath") }
            Mock Remove-Item { $script:Calls.Add("remove $LiteralPath") }
            Mock Test-Path { if ($LiteralPath -like '*.json') { $script:ResultPolls++; return ($script:ResultPolls -gt $script:ResultAfter) }; $false }
            Mock Get-Content { '{"Ok":true,"Error":null,"ProcessId":4242,"SessionId":1,"Token":{"UserName":"DSO-26H2-PRO\\dso","ElevationTypeName":"Full"},"Data":[{"Id":"panels.sound"}]}' }
            Mock New-ScheduledTaskAction { $script:Seen.Execute = $Execute; $script:Seen.Argument = $Argument; New-FakeTaskCim 'MSFT_TaskAction' }
            Mock New-ScheduledTaskPrincipal { $script:Seen.UserId = $UserId; $script:Seen.LogonType = [string]$LogonType; $script:Seen.RunLevel = [string]$RunLevel; New-FakeTaskCim 'MSFT_TaskPrincipal' }
            Mock New-ScheduledTaskSettingsSet { $script:Seen.Limit = $ExecutionTimeLimit; New-FakeTaskCim 'MSFT_TaskSettings' }
            Mock Register-ScheduledTask { $script:Calls.Add("register $TaskName"); $script:Seen.Principal = $Principal }
            Mock Start-ScheduledTask { $script:Calls.Add("start-task $TaskName") }
            Mock Stop-ScheduledTask { $script:Calls.Add("stop-task $TaskName") }
            Mock Unregister-ScheduledTask { $script:Calls.Add("unregister $TaskName") }
            Mock Get-ScheduledTaskInfo { [pscustomobject]@{ LastTaskResult = 267009 } }
            Mock Get-ScheduledTask { [pscustomobject]@{ State = 'Running' } }
            Mock Get-C5ShellProcess { [pscustomobject]@{ ProcessId = 500; SessionId = 1 } }
            Mock New-C5ChildScript { "param([string]`$ResultFile)`n# $Body" }
        }
        It 'principal dso / Interactive / Highest, akcja powershell.exe -File ze ścieżkami C:\dso\c5 bez spacji; wynik dziecka odczytany; zadanie ZAWSZE wyrejestrowane' {
            $Out = Invoke-C5UserTask -Name 'panele-k2' -Body "Invoke-C5PanelRun -Context 'K2'" -TimeoutSeconds 600
            $Seen.UserId | Should -Be 'dso'
            $Seen.LogonType | Should -Be 'Interactive'
            $Seen.RunLevel | Should -Be 'Highest'
            $Seen.Execute | Should -Be (Join-Path $env:SystemRoot 'System32\WindowsPowerShell\v1.0\powershell.exe')
            $Seen.Argument | Should -Be '-NoProfile -NonInteractive -ExecutionPolicy Bypass -WindowStyle Hidden -File C:\dso\c5\panele-k2.ps1 -ResultFile C:\dso\c5\panele-k2.json'
            $Seen.Argument | Should -Not -Match 'EncodedCommand'
            $Seen.Limit.TotalSeconds | Should -Be 660
            $script:Written.Path | Should -Be 'C:\dso\c5\panele-k2.ps1'
            $script:Written.Encoding | Should -Be 'UTF8'
            @($script:Calls | Where-Object { $_ -match '^(register|start-task|unregister)' }) | Should -Be @('register DSO-C5-panele-k2', 'start-task DSO-C5-panele-k2', 'unregister DSO-C5-panele-k2')
            $Out.Name | Should -Be 'panele-k2'
            $Out.Context | Should -Be 'K2'
            $Out.Available | Should -BeTrue
            $Out.ChildProcessId | Should -Be 4242
            $Out.ChildToken.ElevationTypeName | Should -Be 'Full'
            $Out.Data[0].Id | Should -Be 'panels.sound'
            @($script:Calls | Where-Object { $_ -like 'sleep-ms 500' }).Count | Should -Be 2
        }
        It 'błąd rejestracji zadania => "K2 niedostępne: …", bez startu, wyrejestrowanie próbowane; nic nie rzuca' {
            Mock Register-ScheduledTask { throw 'Użytkownik nie jest zalogowany' }
            $Out = Invoke-C5UserTask -Name 'panele-k2' -Body "Invoke-C5PanelRun -Context 'K2'"
            $Out.Available | Should -BeFalse
            $Out.Reason | Should -Match '^K2 niedostępne: .*nie jest zalogowany'
            Should -Invoke Start-ScheduledTask -Times 0 -Exactly
            Should -Invoke Unregister-ScheduledTask -Times 1 -Exactly
        }
        It 'brak wyniku w limicie => "K2 niedostępne: zadanie nie zapisało wyniku", stan i LastTaskResult zapisane, zadanie zatrzymane i wyrejestrowane; pętla ma twardy limit prób' {
            $script:ResultAfter = 100000
            $Out = Invoke-C5UserTask -Name 'store-pakiety-k2' -Body 'Get-C5StorePackages' -TimeoutSeconds 10
            $Out.Available | Should -BeFalse
            $Out.Reason | Should -Match 'K2 niedostępne: zadanie nie zapisało wyniku w 10 s'
            $Out.Reason | Should -Match '267009'
            $Out.TaskState | Should -Be 'Running'
            @($script:Calls | Where-Object { $_ -like 'sleep-ms 500' }).Count | Should -Be 20
            @($script:Calls | Where-Object { $_ -match '^(stop-task|unregister)' }) | Should -Be @('stop-task DSO-C5-store-pakiety-k2', 'unregister DSO-C5-store-pakiety-k2')
        }
        It 'zadanie zakończone (Ready) bez wyniku => koniec czekania po 5 s zamiast pełnego limitu, powód "zakończyło się bez wyniku"' {
            $script:ResultAfter = 100000
            Mock Get-ScheduledTask { [pscustomobject]@{ State = 'Ready' } }
            $Out = Invoke-C5UserTask -Name 'panele-k2' -Body "Invoke-C5PanelRun -Context 'K2'" -TimeoutSeconds 600
            $Out.Available | Should -BeFalse
            $Out.Reason | Should -Match '^K2 niedostępne: zadanie zakończyło się bez wyniku'
            @($script:Calls | Where-Object { $_ -like 'sleep-ms 500' }).Count | Should -Be 9
            $script:Calls | Should -Contain 'unregister DSO-C5-panele-k2'
        }
        It 'błąd w skrypcie dziecka (Ok=false) to dana: Available=true, ChildError z komunikatem' {
            Mock Get-Content { '{"Ok":false,"Error":"Add-Type: brak kompilatora","ProcessId":1,"SessionId":1,"Token":null,"Data":[]}' }
            $Out = Invoke-C5UserTask -Name 'explorer-taskbar-k2' -Body 'Get-C5TaskbarInfo'
            $Out.Available | Should -BeTrue
            $Out.ChildError | Should -Be 'Add-Type: brak kompilatora'
        }
        It 'nazwa i treść wywołania są walidowane (bez wstrzyknięcia średnika, pipe, $(...), ścieżek)' {
            foreach ($Bad in "Invoke-C5PanelRun -Context 'K2'; calc", 'Invoke-C5PanelRun | Out-Null', 'Invoke-C5PanelRun -Context $(calc)', 'calc', "Invoke-C5PanelRun -Context 'K 2'", 'Remove-Item C:\x') {
                { Invoke-C5UserTask -Name 'x' -Body $Bad } | Should -Throw
                { New-C5ChildScript -Body $Bad } | Should -Throw
            }
            foreach ($BadName in 'a b', '..\x', 'X;Y', 'a\b') { { Invoke-C5UserTask -Name $BadName -Body 'Get-C5TaskbarInfo' } | Should -Throw }
            Should -Invoke Register-ScheduledTask -Times 0 -Exactly
        }
    }

    Context 'Skrypt dziecka K2 (definicje funkcji + wywołanie, wynik JSON przez plik tymczasowy)' {
        It 'New-C5ChildScript: poprawna składnia, param(ResultFile) na początku, definicje wszystkich funkcji dziecka, wywołanie, zapis UTF8 i Move-Item' {
            $Text = New-C5ChildScript -Body "Invoke-C5PanelRun -Context 'K2'"
            $Errors = $null
            $Ast = [System.Management.Automation.Language.Parser]::ParseInput($Text, [ref]$null, [ref]$Errors)
            @($Errors).Count | Should -Be 0
            $Ast.ParamBlock.Parameters[0].Name.VariablePath.UserPath | Should -Be 'ResultFile'
            foreach ($Name in Get-C5ChildFunctionNames) { $Text | Should -Match "function $([regex]::Escape($Name)) \{" }
            $Text | Should -Match ([regex]::Escape("@(Invoke-C5PanelRun -Context 'K2')"))
            $Text | Should -Match 'Set-Content -LiteralPath \$Temporary -Encoding UTF8'
            $Text | Should -Match 'Move-Item -LiteralPath \$Temporary -Destination \$ResultFile -Force'
            $Text | Should -Not -Match 'EncodedCommand'
        }
        It 'lista funkcji dziecka jest domknięta: wszystko, co wołają (C5/Explore), też jest w dziecku i jest wysyłane do VM' {
            $Child = @(Get-C5ChildFunctionNames)
            foreach ($Name in $Child) { (@(Get-C5RemoteFunctionNames) + @(Get-ExploreCommonRemoteFunctionNames)) | Should -Contain $Name }
            $ScriptAst = [System.Management.Automation.Language.Parser]::ParseFile((Resolve-Path $script:C5ScriptPath), [ref]$null, [ref]$null)
            $Definitions = @{}
            foreach ($Function in $ScriptAst.FindAll({ param($n) $n -is [System.Management.Automation.Language.FunctionDefinitionAst] }, $true)) { $Definitions[$Function.Name] = $Function.Body.Extent.Text }
            foreach ($Command in (Get-Module ExploreCommon).ExportedFunctions.Values) { $Definitions[$Command.Name] = $Command.Definition }
            foreach ($Name in $Child) {
                $Definitions.ContainsKey($Name) | Should -BeTrue -Because $Name
                $Ast = [System.Management.Automation.Language.Parser]::ParseInput($Definitions[$Name], [ref]$null, [ref]$null)
                $Called = $Ast.FindAll({ param($n) $n -is [System.Management.Automation.Language.CommandAst] }, $true) | ForEach-Object { $_.GetCommandName() } |
                    Where-Object { $_ -like '*-C5*' -or $_ -like '*-Explore*' } | Sort-Object -Unique
                foreach ($Callee in $Called) { $Child | Should -Contain $Callee }
            }
            foreach ($Body in 'Invoke-C5PanelRun', 'Invoke-C5WinUtilRun', 'Get-C5StorePackages', 'Invoke-C5StoreRegistration', 'Invoke-C5WsReset', 'Invoke-C5ShellKill', 'Invoke-C5ShellStartV32', 'Get-C5TaskbarInfo', 'Get-C5SelfTokenInfo') {
                $Child | Should -Contain $Body
            }
            foreach ($NotInChild in 'Invoke-C5UserTask', 'New-C5ChildScript', 'Invoke-C5Collection', 'Invoke-C5PanelsProbe', 'Invoke-C5ExplorerProbe') { $Child | Should -Not -Contain $NotInChild }
        }
    }

    Context 'WinUtil (sonda 12): tylko bez sieci w VM' {
        It 'polecenie jest dosłownie z README w research (gałąź stabilna), bez windev i presetu; dwa warianty argumentów -NoExit -Command, bez -EncodedCommand' {
            $script:ResearchText | Should -Match ([regex]::Escape('dosłownie `' + (Get-C5WinUtilCommand) + '`'))
            Get-C5WinUtilCommand | Should -BeExactly 'irm https://christitus.com/win | iex'
            $Variants = @(Get-C5WinUtilVariants)
            @($Variants | ForEach-Object { $_.Arguments }) | Should -Be @('-NoExit -Command "irm https://christitus.com/win | iex"', '-NoExit -Command irm https://christitus.com/win | iex')
            foreach ($Variant in $Variants) { $Variant.Arguments | Should -Not -Match 'EncodedCommand|windev|Preset' }
        }
        It 'Test-C5NoNetwork: profil sieci => pominięte "VM ma sieć"; błąd cmdletu => pominięte (brak potwierdzenia); zero profili => wolno' {
            Mock Get-NetConnectionProfile { [pscustomobject]@{ Name = 'Sieć'; InterfaceAlias = 'Ethernet'; NetworkCategory = 'Public'; IPv4Connectivity = 'Internet'; IPv6Connectivity = 'NoTraffic' } }
            $With = Test-C5NoNetwork
            $With.NoNetwork | Should -BeFalse
            $With.Reason | Should -Match '^pominięte: VM ma sieć'
            $With.Profiles[0] | Should -Match 'Ethernet'
            Mock Get-NetConnectionProfile { throw 'CIM niedostępne' }
            $Err = Test-C5NoNetwork
            $Err.NoNetwork | Should -BeFalse
            $Err.Reason | Should -Match '^pominięte: nie potwierdzono braku sieci.*CIM niedostępne'
            Mock Get-NetConnectionProfile { }
            (Test-C5NoNetwork).NoNetwork | Should -BeTrue
        }
        It 'Invoke-C5WinUtilRun z siecią: nic nie jest uruchamiane, rekord "pominięte: VM ma sieć"' {
            Mock Get-NetConnectionProfile { [pscustomobject]@{ Name = 'Sieć'; InterfaceAlias = 'Ethernet'; NetworkCategory = 'Private'; IPv4Connectivity = 'LocalNetwork'; IPv6Connectivity = 'NoTraffic' } }
            Mock Invoke-C5LaunchProbe { throw 'nie wolno uruchamiać' }
            $Records = @(Invoke-C5WinUtilRun -Context 'K2')
            $Records.Count | Should -Be 1
            $Records[0].Skipped | Should -Match '^pominięte: VM ma sieć'
            Should -Invoke Invoke-C5LaunchProbe -Times 0 -Exactly
            Should -Invoke Start-Process -Times 0 -Exactly
        }
        It 'Invoke-C5WinUtilRun bez sieci: powershell.exe z System32 dla obu wariantów, sprzątanie potomków + host konsoli; okna tylko w K2' {
            Mock Get-NetConnectionProfile { }
            Mock Invoke-C5LaunchProbe { [pscustomobject]@{ FilePath = $FilePath; Arguments = $Arguments; Windows = $WithWindows.IsPresent; Allow = $AllowNames } }
            $Records = @(Invoke-C5WinUtilRun -Context 'K2')
            $Records.Count | Should -Be 2
            $Records[0].Probe.FilePath | Should -Be (Join-Path $env:SystemRoot 'System32\WindowsPowerShell\v1.0\powershell.exe')
            $Records[0].Probe.Windows | Should -BeTrue
            $Records[0].Probe.Allow | Should -Not -Contain 'powershell.exe'
            $Records[0].Probe.Allow | Should -Not -Contain 'explorer.exe'
            Should -Invoke Invoke-C5LaunchProbe -Times 2 -Exactly -ParameterFilter { $SecondSeconds -eq 10 }
            (@(Invoke-C5WinUtilRun -Context 'K1') | ForEach-Object { $_.Probe.Windows } | Sort-Object -Unique) | Should -Be @($false)
        }
        It 'sonda winutil: sieć sprawdzona w K1 => bez zadania K2 i bez uruchomień; bez sieci K1 i K2' {
            Mock Get-NetConnectionProfile { [pscustomobject]@{ Name = 'Sieć'; InterfaceAlias = 'Ethernet'; NetworkCategory = 'Private'; IPv4Connectivity = 'Internet'; IPv6Connectivity = 'NoTraffic' } }
            Mock Invoke-C5UserTask { throw 'zadanie K2 nie powinno ruszyć' }
            Mock Invoke-C5WinUtilRun { throw 'nie powinno ruszyć' }
            $Records = @(Invoke-C5WinUtilProbe)
            $Records[0].Name | Should -Be 'siec'
            $Records[0].Data[0].NoNetwork | Should -BeFalse
            $Records[1].Skipped | Should -Match '^pominięte: VM ma sieć'
            Should -Invoke Invoke-C5UserTask -Times 0 -Exactly
            Mock Get-NetConnectionProfile { }
            Mock Invoke-C5WinUtilRun { [pscustomobject]@{ Context = $Context } }
            Mock Invoke-C5UserTask { [pscustomobject]@{ Name = $Name; Body = $Body; Context = 'K2'; Data = @() } }
            $Records = @(Invoke-C5WinUtilProbe)
            @($Records | ForEach-Object { $_.Name }) | Should -Be @('siec', 'winutil-k1', 'winutil-k2')
            $Records[2].Body | Should -Be "Invoke-C5WinUtilRun -Context 'K2'"
        }
    }

    Context 'Store (sondy 8-11)' {
        BeforeEach {
            $script:Pkg = {
                param([string]$Name, [string]$Location, [string]$Version = '22510.1401.6.0')
                [pscustomobject]@{
                    Name = $Name; PackageFullName = "${Name}_${Version}_x64__8wekyb3d8bbwe"; Version = $Version; Architecture = 'X64'; InstallLocation = $Location; Status = 'Ok'
                    PackageUserInformation = @('S-1-5-21-1-1001 [dso]: Installed')
                }
            }
        }
        It 'Get-C5StorePackages -AllUsers: filtr *Store* (Where-Object), manifest, Status i PackageUserInformation jako tekst; błąd to dana' {
            Mock Get-AppxPackage { & $script:Pkg 'Microsoft.WindowsStore' 'C:\Program Files\WindowsApps\Store'; & $script:Pkg 'Microsoft.WindowsCalculator' 'C:\x'; & $script:Pkg 'Microsoft.StorePurchaseApp' 'C:\y' }
            Mock Test-Path { $LiteralPath -eq 'C:\Program Files\WindowsApps\Store\AppxManifest.xml' }
            $All = Get-C5StorePackages -AllUsers
            $All.Ok | Should -BeTrue
            @($All.Items | ForEach-Object { $_.Name }) | Should -Be @('Microsoft.WindowsStore', 'Microsoft.StorePurchaseApp')
            $All.Items[0].ManifestPath | Should -Be 'C:\Program Files\WindowsApps\Store\AppxManifest.xml'
            $All.Items[0].ManifestExists | Should -BeTrue
            $All.Items[0].Status | Should -Be 'Ok'
            $All.Items[0].Users | Should -Be @('S-1-5-21-1-1001 [dso]: Installed')
            Should -Invoke Get-AppxPackage -Times 1 -Exactly -ParameterFilter { $AllUsers.IsPresent }
            Mock Get-AppxPackage { throw 'Appx niedostępne' }
            $Fail = Get-C5StorePackages
            $Fail.Ok | Should -BeFalse
            $Fail.Error | Should -Match 'Appx niedostępne'
            Should -Invoke Get-AppxPackage -Times 1 -Exactly -ParameterFilter { -not $AllUsers.IsPresent }
        }
        It 'Invoke-C5StoreRegistration: brak Microsoft.WindowsStore z manifestem => pominięte, Register-C5StorePackage nie jest wołany' {
            Mock Get-C5StorePackages { [pscustomobject]@{ AllUsers = $AllUsers.IsPresent; Ok = $true; Error = $null; Count = 1; Items = @([pscustomobject]@{ Name = 'Microsoft.StorePurchaseApp'; ManifestExists = $true; ManifestPath = 'C:\y\AppxManifest.xml'; Version = '1.0.0.0' }) } }
            Mock Register-C5StorePackage { throw 'nie wolno' }
            $Record = Invoke-C5StoreRegistration -Context 'K1'
            $Record.Skipped | Should -Match 'brak pakietu Microsoft.WindowsStore na liście -AllUsers'
            $Record.NameMatchCount | Should -Be 0
            Should -Invoke Register-C5StorePackage -Times 0 -Exactly
        }
        It 'Invoke-C5StoreRegistration: pakiet jest po nazwie, ale manifest nieczytelny (np. ACL WindowsApps) => osobny komunikat z liczbami i powodem' {
            Mock Get-C5StorePackages { [pscustomobject]@{ AllUsers = $AllUsers.IsPresent; Ok = $true; Error = $null; Count = 1; Items = @([pscustomobject]@{ Name = 'Microsoft.WindowsStore'; ManifestExists = $false; ManifestError = 'Access to the path is denied.'; ManifestPath = 'C:\Program Files\WindowsApps\S\AppxManifest.xml'; Version = '1.0.0.0' }) } }
            Mock Register-C5StorePackage { throw 'nie wolno' }
            $Record = Invoke-C5StoreRegistration -Context 'K1'
            $Record.NameMatchCount | Should -Be 1
            $Record.CandidateCount | Should -Be 0
            $Record.Skipped | Should -Match 'po nazwie: 1, z czytelnym AppxManifest.xml: 0 \(powód: Access to the path is denied\.\)'
            Should -Invoke Register-C5StorePackage -Times 0 -Exactly
        }
        It 'Get-C5StorePackageInfo: Test-Path=false => powód z Get-Item jako dana (ManifestError); manifest istnieje => bez Get-Item' {
            Mock Test-Path { $false }
            Mock Get-Item { throw 'Access to the path is denied.' }
            $Info = Get-C5StorePackageInfo -Package ([pscustomobject]@{ Name = 'Microsoft.WindowsStore'; InstallLocation = 'C:\Program Files\WindowsApps\S'; Status = 'Ok' })
            $Info.ManifestExists | Should -BeFalse
            $Info.ManifestError | Should -Be 'Access to the path is denied.'
            Mock Test-Path { $true }
            (Get-C5StorePackageInfo -Package ([pscustomobject]@{ Name = 'Microsoft.WindowsStore'; InstallLocation = 'C:\s'; Status = 'Ok' })).ManifestError | Should -BeNullOrEmpty
            Should -Invoke Get-Item -Times 1 -Exactly
        }
        It 'Invoke-C5StoreRegistration: odczyt przed (bieżące konto), rejestracja z manifestu najnowszej wersji, odczyt po (bieżące i -AllUsers)' {
            $script:Reads = [System.Collections.Generic.List[string]]::new()
            Mock Get-C5StorePackages {
                $script:Reads.Add("read $($AllUsers.IsPresent)")
                [pscustomobject]@{ AllUsers = $AllUsers.IsPresent; Ok = $true; Error = $null; Count = 2; Items = @(
                        [pscustomobject]@{ Name = 'Microsoft.WindowsStore'; ManifestExists = $true; ManifestPath = 'C:\old\AppxManifest.xml'; Version = '9.0.0.0' },
                        [pscustomobject]@{ Name = 'Microsoft.WindowsStore'; ManifestExists = $true; ManifestPath = 'C:\new\AppxManifest.xml'; Version = '22510.1401.6.0' }) }
            }
            Mock Register-C5StorePackage { $script:Reads.Add("register $ManifestPath"); [pscustomobject]@{ ManifestPath = $ManifestPath; Ok = $true; Error = $null; TimedOut = $false } }
            $Record = Invoke-C5StoreRegistration -Context 'K2'
            $script:Reads | Should -Be @('read True', 'read False', 'register C:\new\AppxManifest.xml', 'read False', 'read True')
            $Record.Context | Should -Be 'K2'
            $Record.Register.Ok | Should -BeTrue
            $Record.CandidateCount | Should -Be 2
        }
        It 'Register-C5StorePackage: Add-AppxPackage -Path (manifest) -Register -DisableDevelopmentMode w zadaniu w tle z limitem; przekroczenie limitu => Stop-Job; Remove-Job zawsze' {
            $script:JobText = $null
            Mock Start-Job { $script:JobText = $ScriptBlock.ToString(); $script:JobArgs = $ArgumentList; [pscustomobject]@{ Id = 7; State = 'Completed' } }
            Mock Wait-Job { [pscustomobject]@{ Id = $Id } }
            Mock Receive-Job { 'ok' }
            Mock Remove-Job { $script:Calls.Add("remove-job $Id") }
            Mock Stop-Job { $script:Calls.Add("stop-job $Id") }
            $Ok = Register-C5StorePackage -ManifestPath 'C:\Program Files\WindowsApps\Store\AppxManifest.xml' -TimeoutSeconds 300
            $Ok.Ok | Should -BeTrue
            $script:JobText | Should -Match 'Add-AppxPackage -Path \$Path -Register -DisableDevelopmentMode -ErrorAction Stop'
            $script:JobArgs | Should -Be @('C:\Program Files\WindowsApps\Store\AppxManifest.xml')
            Should -Invoke Wait-Job -Times 1 -Exactly -ParameterFilter { $Timeout -eq 300 }
            Mock Wait-Job { $null }
            $Late = Register-C5StorePackage -ManifestPath 'C:\a\AppxManifest.xml' -TimeoutSeconds 10
            $Late.TimedOut | Should -BeTrue
            $Late.Ok | Should -BeFalse
            @($script:Calls | Where-Object { $_ -eq 'remove-job 7' }).Count | Should -Be 2
            @($script:Calls | Where-Object { $_ -eq 'stop-job 7' }).Count | Should -Be 1
            Mock Wait-Job { [pscustomobject]@{ Id = $Id } }
            Mock Receive-Job { throw 'Deployment failed with HRESULT: 0x80073CF9' }
            $Bad = Register-C5StorePackage -ManifestPath 'C:\a\AppxManifest.xml'
            $Bad.Ok | Should -BeFalse
            $Bad.Error | Should -Match '0x80073CF9'
        }
        It 'Invoke-C5WsReset: WSReset.exe -i z System32 (Invoke-ExploreNative z limitem), potem sprzątanie tylko nowych procesów Store' {
            Mock Test-Path { $true }
            Mock Invoke-ExploreNative { $script:Calls.Add("native $FilePath $($Arguments -join ' ') $TimeoutSeconds"); [pscustomobject]@{ ExitCode = 0; Stdout = ''; Stderr = ''; Error = $null; TimedOut = $false } }
            Mock Invoke-C5Cleanup { $script:Calls.Add("cleanup $($AllowNames -join ',') $LaunchedPid"); [pscustomobject]@{ Stopped = @() } }
            Set-FakeSnapshots -List @(, @((New-FakeProc 1 'a.exe')))
            $Record = Invoke-C5WsReset -Context 'K1'
            $Record.Native.ExitCode | Should -Be 0
            $script:Calls | Should -Contain ("native $(Join-Path $env:SystemRoot 'System32\WSReset.exe') -i 60")
            $script:Calls | Should -Contain 'cleanup WSReset.exe,WinStore.App.exe 0'
        }
        It 'sonda store: kolejność odczyty -> rejestracja K1 -> rejestracja K2 -> wsreset K1 -> wsreset K2; K2 przez zadania' {
            Mock Get-C5StorePackages { [pscustomobject]@{ AllUsers = $AllUsers.IsPresent } }
            Mock Invoke-C5StoreRegistration { [pscustomobject]@{ Context = $Context } }
            Mock Invoke-C5WsReset { [pscustomobject]@{ Context = $Context } }
            Mock Invoke-C5UserTask { [pscustomobject]@{ Name = $Name; Body = $Body; Context = 'K2'; Data = @() } }
            $Records = @(Invoke-C5StoreProbe)
            @($Records | ForEach-Object { $_.Name }) | Should -Be @('store-pakiety-wszyscy-k1', 'store-pakiety-biezace-k1', 'store-pakiety-k2', 'store-rejestracja-k1', 'store-rejestracja-k2', 'wsreset-k1', 'wsreset-k2')
            $Records[0].Data[0].AllUsers | Should -BeTrue
            $Records[1].Data[0].AllUsers | Should -BeFalse
            $Records[2].Body | Should -Be 'Get-C5StorePackages'
            $Records[4].Body | Should -Be "Invoke-C5StoreRegistration -Context 'K2'"
            $Records[6].Body | Should -Be "Invoke-C5WsReset -Context 'K2'"
        }
    }

    Context 'Explorer (sondy 5-7)' {
        BeforeEach {
            Mock Get-C5ProcessOwner { if ($ProcessId -eq 999) { [pscustomobject]@{ User = 'Administrator'; Domain = 'X'; Owner = 'X\Administrator'; OwnerSid = 'S-1-5-21-1-500'; Error = $null } } else { [pscustomobject]@{ User = 'dso'; Domain = 'X'; Owner = 'X\dso'; OwnerSid = 'S-1-5-21-1-1001'; Error = $null } } }
        }
        It 'Get-C5ShellProcess: tylko explorer.exe użytkownika dso (właściciel z GetOwner), opcjonalnie z jego sesji, od najstarszego' {
            Set-FakeSnapshots -List @(, @((New-FakeProc 501 'explorer.exe' -Created '2026-10-19T10:05:00.0000000'), (New-FakeProc 500 'explorer.exe'), (New-FakeProc 999 'explorer.exe'), (New-FakeProc 502 'explorer.exe' -Session 2), (New-FakeProc 7 'notepad.exe')))
            @(Get-C5ShellProcess -UserName 'dso' | ForEach-Object { $_.ProcessId }) | Should -Be @(500, 502, 501)
            @(Get-C5ShellProcess -UserName 'dso' -SessionId 1 | ForEach-Object { $_.ProcessId }) | Should -Be @(500, 501)
            Set-FakeSnapshots -List @(, @((New-FakeProc 77 'winlogon.exe' -Created '2026-10-19T09:00:00.0000000'), (New-FakeProc 600 'explorer.exe' -Parent 77)))
            (Get-C5ShellProcess -UserName 'dso').ParentName | Should -Be 'winlogon.exe'
        }
        It 'Invoke-C5ShellKill StopProcess (K1): Stop-Process -Force tylko na PID powłoki dso; nowy PID w sesji po 2 próbach; czas, token, właściciel; bez okien' {
            $Old = @((New-FakeProc 500 'explorer.exe'), (New-FakeProc 999 'explorer.exe'))
            $New = @((New-FakeProc 600 'explorer.exe' -Created '2026-10-19T10:10:00.0000000'), (New-FakeProc 999 'explorer.exe'))
            Set-FakeSnapshots -List @($Old, @((New-FakeProc 999 'explorer.exe')), $New)
            Mock Stop-Process { $script:Calls.Add("stop $Id force=$($Force.IsPresent)"); [pscustomobject]@{ Id = $Id } }
            $Record = Invoke-C5ShellKill -Method 'StopProcess' -Context 'K1'
            $script:Calls | Should -Contain 'stop 500 force=True'
            Should -Invoke Stop-Process -Times 1 -Exactly
            $Record.Command | Should -Be 'Stop-Process -Id 500 -Force -PassThru'
            $Record.Found | Should -BeTrue
            $Record.NewPid | Should -Be 600
            $Record.Polls | Should -Be 2
            $Record.New.Token.ElevationTypeName | Should -Be 'Limited'
            $Record.New.Owner | Should -Be 'X\dso'
            $Record.Old.ProcessId | Should -Be 500
            $Record.ElapsedMs | Should -Not -BeNullOrEmpty
            $Record.TaskbarFound | Should -BeNullOrEmpty
            Should -Invoke Get-C5WindowList -Times 0 -Exactly
        }
        It 'Invoke-C5ShellKill: brak powrotu w 30 s => Found=false po 60 próbach co 500 ms (twardy limit)' {
            Set-FakeSnapshots -List @(@((New-FakeProc 500 'explorer.exe')), @((New-FakeProc 500 'explorer.exe')), @())
            Mock Stop-Process { }
            $Record = Invoke-C5ShellKill -Method 'StopProcess' -Context 'K1'
            $Record.Found | Should -BeFalse
            $Record.Polls | Should -Be 60
            @($script:Calls | Where-Object { $_ -eq 'sleep-ms 500' }).Count | Should -BeLessOrEqual 60
            @($script:Calls | Where-Object { $_ -eq 'sleep-ms 500' }).Count | Should -BeGreaterOrEqual 59
        }
        It 'Invoke-C5ShellKill Taskkill: taskkill.exe /f /pid (PID powłoki) z System32 (nie /im), Stop-Process nie jest wołany' {
            Set-FakeSnapshots -List @(@((New-FakeProc 500 'explorer.exe')), @((New-FakeProc 500 'explorer.exe')), @((New-FakeProc 601 'explorer.exe' -Created '2026-10-19T11:00:00.0000000')))
            Mock Invoke-ExploreNative { $script:Calls.Add("native $FilePath $($Arguments -join ' ')"); [pscustomobject]@{ ExitCode = 0; Stdout = 'OK'; Stderr = ''; Error = $null } }
            $Record = Invoke-C5ShellKill -Method 'Taskkill' -Context 'K1'
            $script:Calls | Should -Contain ("native $(Join-Path $env:SystemRoot 'System32\taskkill.exe') /f /pid 500")
            $Record.KillResult.ExitCode | Should -Be 0
            $Record.NewPid | Should -Be 601
            Should -Invoke Stop-Process -Times 0 -Exactly
        }
        It 'Invoke-C5ShellKill bez powłoki dso: pominięte, nic nie jest zatrzymywane' {
            Set-FakeSnapshots -List @(, @((New-FakeProc 999 'explorer.exe')))
            $Record = Invoke-C5ShellKill -Method 'StopProcess' -Context 'K1'
            $Record.Skipped | Should -Match 'brak explorer.exe użytkownika dso'
            Should -Invoke Stop-Process -Times 0 -Exactly
            Should -Invoke Invoke-ExploreNative -Times 0 -Exactly
        }
        It 'Invoke-C5ShellKill (K2): powłoka z sesji zadania; po nowym PID czeka na Shell_TrayWnd nowego procesu' {
            Set-FakeSnapshots -List @(@((New-FakeProc 500 'explorer.exe'), (New-FakeProc 502 'explorer.exe' -Session 2)), @((New-FakeProc 500 'explorer.exe'), (New-FakeProc 502 'explorer.exe' -Session 2)), @((New-FakeProc 600 'explorer.exe' -Created '2026-10-19T11:00:00.0000000')))
            Mock Stop-Process { $script:Calls.Add("stop $Id") }
            $script:WinCall = 0
            Mock Get-C5WindowList { $script:WinCall++; if ($script:WinCall -lt 3) { @((New-FakeWindow 1 600 -ClassName 'Progman')) } else { @((New-FakeWindow 2 600 -ClassName 'Shell_TrayWnd')) } }
            $Record = Invoke-C5ShellKill -Method 'StopProcess' -Context 'K2'
            $script:Calls | Should -Contain 'stop 500'
            $Record.TaskbarFound | Should -BeTrue
            $Record.TaskbarPolls | Should -Be 3
        }
        It 'Invoke-C5ShellStartV32: Start-Process explorer.exe (jak v3.2), token nowych explorer.exe, zatrzymanie TYLKO nowych (nigdy powłoki)' {
            $Shell = New-FakeProc 500 'explorer.exe'
            Set-FakeSnapshots -List @(@($Shell), @($Shell), @($Shell, (New-FakeProc 700 'explorer.exe' -Parent 1), (New-FakeProc 701 'conhost.exe'), (New-FakeProc 800 'explorer.exe' -Parent 77)))
            Mock Start-Process { $script:Calls.Add("start $FilePath"); New-FakeStartedProcess -Id 700 }
            Mock Stop-Process { $script:Calls.Add("stop $Id") }
            $Record = Invoke-C5ShellStartV32 -Context 'K1'
            $script:Calls | Should -Contain ("start $(Join-Path $env:SystemRoot 'explorer.exe')")
            @($Record.NewExplorers | ForEach-Object { $_.ProcessId }) | Should -Be @(700, 800)
            $Record.NewExplorers[0].Token.ElevationTypeName | Should -Be 'Limited'
            # 800: nowy explorer.exe dso w sesji powłoki, nie od nas (może być nowa powłoka) => nie zatrzymany.
            @($script:Calls | Where-Object { $_ -like 'stop *' }) | Should -Be @('stop 700')
            $Record.ShellPid | Should -Be 500
            $Record.ShellSessionId | Should -Be 1
            $Record.LaunchedHasExited | Should -BeFalse
            $Record.LaunchedAt | Should -Not -BeNullOrEmpty
            $Record.ObservedAfterMs | Should -Not -BeNullOrEmpty
        }
        It 'Invoke-C5ShellStartV32: przekazanie do istniejącej powłoki (uruchomiony proces zakończony, brak nowych explorer.exe) jest odróżnialne; nic nie jest zatrzymywane' {
            $Shell = New-FakeProc 500 'explorer.exe'
            Set-FakeSnapshots -List @(@($Shell), @($Shell), @($Shell))
            Mock Start-Process { $Fake = New-FakeStartedProcess -Id 700; $Fake.HasExited = $true; $Fake.ExitCode = 1; $Fake }
            Mock Stop-Process { $script:Calls.Add("stop $Id") }
            $Record = Invoke-C5ShellStartV32 -Context 'K1'
            @($Record.NewExplorers).Count | Should -Be 0
            $Record.LaunchedPid | Should -Be 700
            $Record.LaunchedHasExited | Should -BeTrue
            $Record.LaunchedExitCode | Should -Be 1
            Should -Invoke Stop-Process -Times 0 -Exactly
        }
        It 'Invoke-C5ShellStartV32 bez powłoki dso: pominięte (nie uruchamiamy powłoki od zera)' {
            Set-FakeSnapshots -List @(, @())
            $Record = Invoke-C5ShellStartV32 -Context 'K1'
            $Record.Skipped | Should -Match 'brak explorer.exe użytkownika dso'
            Should -Invoke Start-Process -Times 0 -Exactly
        }
        It 'sonda explorer: AutoRestartShell (tylko odczyt Winlogon), po logowaniu, Stop-Process K1, taskkill K1, Stop-Process K2, v3.2 K1, v3.2 K2, stan końcowy' {
            $script:RegRead = $null
            Mock Read-ExploreRegistryValue { $script:RegRead = "$Path|$Name"; [pscustomobject]@{ Key = "$Path\$Name"; KeyExists = $true; Exists = $false; Kind = $null; Value = $null; Hex = $null } }
            Mock Get-C5ShellProcess { New-FakeProc 500 'explorer.exe' }
            Mock Get-C5ShellDetail { [pscustomobject]@{ ProcessId = $Entry.ProcessId } }
            Mock Invoke-C5ShellKill { [pscustomobject]@{ Method = $Method; Context = $Context; Found = $true } }
            Mock Invoke-C5ShellStartV32 { [pscustomobject]@{ Context = $Context } }
            Mock Invoke-C5UserTask { [pscustomobject]@{ Name = $Name; Body = $Body; Context = 'K2'; Data = @() } }
            $Records = @(Invoke-C5ExplorerProbe)
            @($Records | ForEach-Object { $_.Name }) | Should -Be @('autorestartshell', 'explorer-po-logowaniu', 'explorer-stop-process-k1', 'explorer-taskkill-k1', 'explorer-stop-process-k2', 'explorer-v32-k1', 'explorer-v32-k2', 'explorer-taskbar-k2', 'explorer-koniec-k1')
            $script:RegRead | Should -Be 'HKLM\SOFTWARE\Microsoft\Windows NT\CurrentVersion\Winlogon|AutoRestartShell'
            $Records[2].Data[0].Method | Should -Be 'StopProcess'
            $Records[3].Data[0].Method | Should -Be 'Taskkill'
            $Records[4].Body | Should -Be "Invoke-C5ShellKill -Method 'StopProcess' -Context 'K2'"
            $Records[6].Body | Should -Be "Invoke-C5ShellStartV32 -Context 'K2'"
            $Records[7].Body | Should -Be 'Get-C5TaskbarInfo'
            $script:Calls[0] | Should -Be 'assert-vm'
        }
        It 'sonda explorer: błąd jednego kroku (wyjątek) jest daną, kolejne kroki idą dalej' {
            Mock Get-C5ShellProcess { New-FakeProc 500 'explorer.exe' }
            Mock Get-C5ShellDetail { [pscustomobject]@{ ProcessId = $Entry.ProcessId } }
            Mock Invoke-C5ShellKill { if ($Method -eq 'StopProcess') { throw 'odmowa dostępu' }; [pscustomobject]@{ Method = $Method } }
            Mock Invoke-C5ShellStartV32 { [pscustomobject]@{ Context = $Context } }
            Mock Invoke-C5UserTask { [pscustomobject]@{ Name = $Name; Context = 'K2'; Data = @() } }
            $Records = @(Invoke-C5ExplorerProbe)
            ($Records | Where-Object { $_.Name -eq 'explorer-stop-process-k1' }).Error | Should -Match 'odmowa dostępu'
            ($Records | Where-Object { $_.Name -eq 'explorer-taskkill-k1' }).Data[0].Method | Should -Be 'Taskkill'
        }
        It 'Get-C5TaskbarInfo: okna Shell_TrayWnd i PID powłok dso z sesji zadania' {
            Mock Get-C5ShellProcess { New-FakeProc 600 'explorer.exe' }
            Mock Get-C5WindowList { @((New-FakeWindow 2 600 -ClassName 'Shell_TrayWnd'), (New-FakeWindow 3 600 -ClassName 'Progman')) }
            $Info = Get-C5TaskbarInfo
            $Info.ShellPids | Should -Be @(600)
            @($Info.TrayWindows).Count | Should -Be 1
            $Info.TrayWindows[0] | Should -Match '600'
        }
    }

    Context 'Token procesu (P/Invoke tylko w VM) i wyłącznik "tylko VM"' {
        It 'źródło C# kompiluje się kompilatorem Add-Type z PS 5.1 (CodeDom, do pliku w TestDrive, bez ładowania) i używa stałych z MS Learn' {
            $Source = Get-C5NativeSource
            $Provider = New-Object Microsoft.CSharp.CSharpCodeProvider
            $Parameters = New-Object System.CodeDom.Compiler.CompilerParameters
            $Parameters.GenerateInMemory = $false
            $Parameters.GenerateExecutable = $false
            $Parameters.OutputAssembly = Join-Path $TestDrive 'c5native-check.dll'
            [void]$Parameters.ReferencedAssemblies.Add('System.dll')
            $Compiled = $Provider.CompileAssemblyFromSource($Parameters, $Source)
            @($Compiled.Errors | Where-Object { -not $_.IsWarning } | ForEach-Object { $_.ToString() }) | Should -BeNullOrEmpty
            $Source | Should -Match 'OpenProcess\(0x1000'
            $Source | Should -Match 'OpenProcessToken\(process, 0x0008'
            $Source | Should -Match 'GetTokenInformation\(token, 20,'
            $Source | Should -Match 'GetTokenInformation\(token, 18,'
            $Source | Should -Match 'PostMessage\(new IntPtr\(handle\), 0x0010'
            foreach ($Api in 'EnumWindows', 'GetWindowText', 'GetClassName', 'GetWindowThreadProcessId', 'IsWindowVisible', 'CloseHandle') { $Source | Should -Match "extern \w+ $Api\(" }
        }
        It 'Get-C5ProcessTokenInfo mapuje TokenElevationType 1/2/3 na Default/Full/Limited (kolejność enuma z MS Learn); błąd Win32 to dana' {
            Mock Invoke-C5ElevationQuery { switch ($ProcessId) { 1 { , @(0, 1, 0) } 2 { , @(1, 2, 0) } 3 { , @(0, 3, 0) } default { , @(-1, -1, 5) } } }
            (Get-C5ProcessTokenInfo -ProcessId 1).ElevationTypeName | Should -Be 'Default'
            (Get-C5ProcessTokenInfo -ProcessId 1).Elevated | Should -BeFalse
            (Get-C5ProcessTokenInfo -ProcessId 2).Elevated | Should -BeTrue
            (Get-C5ProcessTokenInfo -ProcessId 2).ElevationTypeName | Should -Be 'Full'
            (Get-C5ProcessTokenInfo -ProcessId 3).ElevationTypeName | Should -Be 'Limited'
            $Err = Get-C5ProcessTokenInfo -ProcessId 4
            $Err.Elevated | Should -BeNullOrEmpty
            $Err.ElevationType | Should -BeNullOrEmpty
            $Err.Win32Error | Should -Be 5
            Mock Invoke-C5ElevationQuery { throw 'Add-Type: błąd kompilacji' }
            (Get-C5ProcessTokenInfo -ProcessId 1).Error | Should -Match 'błąd kompilacji'
        }
        It 'Test-C5VmComputerName: tylko DSO-26H2-HOME i DSO-26H2-PRO (nazwy z New-TestVm), host Dawida nie przechodzi' {
            Test-C5VmComputerName -ComputerName 'DSO-26H2-PRO' | Should -BeTrue
            Test-C5VmComputerName -ComputerName 'dso-26h2-home' | Should -BeTrue
            foreach ($Bad in 'DAWID-PC', 'BRUTEFORCE_LTSC', 'DSO-26H2-TEST', '', $env:COMPUTERNAME) { Test-C5VmComputerName -ComputerName $Bad | Should -BeFalse }
        }
        It 'każda sonda i każda funkcja ze skutkiem w systemie zaczyna się od Assert-C5InsideVm (AST)' {
            foreach ($Name in 'Invoke-C5PanelsProbe', 'Invoke-C5WinUtilProbe', 'Invoke-C5StoreProbe', 'Invoke-C5ExplorerProbe', 'Invoke-C5PanelRun', 'Invoke-C5WinUtilRun',
                'Invoke-C5StoreRegistration', 'Invoke-C5WsReset', 'Invoke-C5ShellKill', 'Invoke-C5ShellStartV32', 'Invoke-C5UserTask', 'Invoke-C5Collection', 'Start-C5Process', 'Invoke-C5Cleanup', 'Register-C5StorePackage', 'Close-C5Window', 'Initialize-C5Native', 'Get-C5StorePackages', 'Start-C5CreateProcess', 'Invoke-C5PreClean') {
                $Body = (Get-Command $Name -CommandType Function).ScriptBlock.Ast.Body
                $First = $Body.EndBlock.Statements[0]
                $First.Extent.Text | Should -Be 'Assert-C5InsideVm' -Because $Name
            }
        }
    }

    Context 'Kolejność sond i izolacja błędów (Invoke-C5Collection)' {
        BeforeEach {
            Mock Get-C5SelfTokenInfo { [pscustomobject]@{ UserName = 'X\Administrator' } }
            Mock Get-C5ShellProcess { New-FakeProc 500 'explorer.exe' }
            Mock -ModuleName ExploreCommon Get-ExploreMeta { [ordered]@{ EditionID = 'Professional' } }
        }
        It 'bez -Probe: panels -> winutil -> store -> explorer (Explorer na końcu); z -Probe tylko wybrane w tej samej kolejności' {
            Mock Invoke-C5PanelsProbe { $script:Calls.Add('panels'); [pscustomobject]@{ Name = 'p' } }
            Mock Invoke-C5WinUtilProbe { $script:Calls.Add('winutil'); [pscustomobject]@{ Name = 'w' } }
            Mock Invoke-C5StoreProbe { $script:Calls.Add('store'); [pscustomobject]@{ Name = 's' } }
            Mock Invoke-C5ExplorerProbe { $script:Calls.Add('explorer'); [pscustomobject]@{ Name = 'e' } }
            $All = Invoke-C5Collection -UserSid 'S-1-5-21-1-1001' | ConvertFrom-Json
            @($All.probes.PSObject.Properties.Name) | Should -Be @('panels', 'winutil', 'store', 'explorer')
            @($script:Calls | Where-Object { $_ -ne 'assert-vm' }) | Should -Be @('panels', 'winutil', 'store', 'explorer')
            $script:Calls[0] | Should -Be 'assert-vm'
            $All.userSid | Should -Be 'S-1-5-21-1-1001'
            $All.userName | Should -Be 'dso'
            $script:Calls.Clear()
            $Some = Invoke-C5Collection -UserSid 'S-1' -Probe 'explorer', 'panels' | ConvertFrom-Json
            @($Some.probes.PSObject.Properties.Name) | Should -Be @('panels', 'explorer')
            @($script:Calls | Where-Object { $_ -ne 'assert-vm' }) | Should -Be @('panels', 'explorer')
            { Invoke-C5Collection -Probe 'nieznana' } | Should -Throw
        }
        It 'wyjątek jednej sondy => BLAD-SONDY w jej wyniku, pozostałe zebrane (także Explorer po błędzie Store)' {
            Mock Invoke-C5PanelsProbe { [pscustomobject]@{ Name = 'p' } }
            Mock Invoke-C5WinUtilProbe { throw 'winutil padł' }
            Mock Invoke-C5StoreProbe { throw 'store padł' }
            Mock Invoke-C5ExplorerProbe { [pscustomobject]@{ Name = 'e' } }
            $Doc = Invoke-C5Collection -UserSid 'S-1' | ConvertFrom-Json
            $Doc.probes.winutil[0].Name | Should -Be 'BLAD-SONDY'
            $Doc.probes.winutil[0].Error | Should -Match 'winutil padł'
            $Doc.probes.store[0].Error | Should -Match 'store padł'
            $Doc.probes.explorer[0].Name | Should -Be 'e'
            $Doc.probes.panels[0].Name | Should -Be 'p'
        }
    }
}

Describe 'Wyłącznik "tylko VM" bez mocka (prawdziwy Assert-C5InsideVm): na hoście nic nie rusza' {
    BeforeEach {
        Mock Start-Process { throw 'HOST: Start-Process niedozwolone w testach' }
        Mock Stop-Process { throw 'HOST: Stop-Process niedozwolone w testach' }
        Mock Register-ScheduledTask { throw 'HOST: Register-ScheduledTask niedozwolone w testach' }
        Mock Start-Job { throw 'HOST: Start-Job niedozwolone w testach' }
        Mock Add-Type { throw 'HOST: Add-Type niedozwolone w testach' }
        Mock Get-CimInstance { throw 'HOST: Get-CimInstance niedozwolone w testach' }
        Mock Get-NetConnectionProfile { throw 'HOST: Get-NetConnectionProfile niedozwolone w testach' }
        Mock Invoke-ExploreNative { throw 'HOST: Invoke-ExploreNative niedozwolone w testach' }
        Mock New-Item { throw 'HOST: New-Item niedozwolone w testach' }
        Mock Invoke-C5PanelsProbe { throw 'sonda nie może ruszyć' }
        Mock Invoke-C5WinUtilProbe { throw 'sonda nie może ruszyć' }
        Mock Invoke-C5StoreProbe { throw 'sonda nie może ruszyć' }
        Mock Invoke-C5ExplorerProbe { throw 'sonda nie może ruszyć' }
    }
    It 'Assert-C5InsideVm rzuca w testach (DSO_VM_TESTS_MOCK_ONLY), nawet z nazwą komputera VM; bez nazwy VM też rzuca' {
        { Assert-C5InsideVm -ComputerName 'DSO-26H2-PRO' } | Should -Throw '*DSO_VM_TESTS_MOCK_ONLY*'
        { Assert-C5InsideVm } | Should -Throw
    }
    It 'Invoke-C5Collection: wyjątek przed pierwszą sondą (żadna sonda nie jest wołana)' {
        { Invoke-C5Collection -UserSid 'S-1' } | Should -Throw '*DSO_VM_TESTS_MOCK_ONLY*'
        Should -Invoke Invoke-C5PanelsProbe -Times 0 -Exactly
        Should -Invoke Invoke-C5ExplorerProbe -Times 0 -Exactly
    }
    It 'funkcje ze skutkiem wywołane bezpośrednio rzucają, zanim cokolwiek uruchomią (proces, zadanie, Appx, Explorer, sieć)' {
        { Start-C5Process -FilePath 'C:\Windows\System32\control.exe' -Arguments '' } | Should -Throw '*DSO_VM_TESTS_MOCK_ONLY*'
        { Invoke-C5ShellKill -Method 'StopProcess' -Context 'K1' } | Should -Throw '*DSO_VM_TESTS_MOCK_ONLY*'
        { Invoke-C5ShellStartV32 -Context 'K1' } | Should -Throw '*DSO_VM_TESTS_MOCK_ONLY*'
        { Register-C5StorePackage -ManifestPath 'C:\x\AppxManifest.xml' } | Should -Throw '*DSO_VM_TESTS_MOCK_ONLY*'
        { Invoke-C5UserTask -Name 'x' -Body 'Get-C5TaskbarInfo' } | Should -Throw '*DSO_VM_TESTS_MOCK_ONLY*'
        { Invoke-C5WinUtilRun -Context 'K1' } | Should -Throw '*DSO_VM_TESTS_MOCK_ONLY*'
        { Invoke-C5PanelRun -Context 'K1' } | Should -Throw '*DSO_VM_TESTS_MOCK_ONLY*'
        { Close-C5Window -Handle 1 } | Should -Throw '*DSO_VM_TESTS_MOCK_ONLY*'
        { Initialize-C5Native } | Should -Throw '*DSO_VM_TESTS_MOCK_ONLY*'
        { Get-C5StorePackages -AllUsers } | Should -Throw '*DSO_VM_TESTS_MOCK_ONLY*'
        { Get-C5WindowList } | Should -Throw '*DSO_VM_TESTS_MOCK_ONLY*'
        { Start-C5CreateProcess -FilePath 'C:\Windows\System32\control.exe' -Arguments 'wscui.cpl' } | Should -Throw '*DSO_VM_TESTS_MOCK_ONLY*'
        { Invoke-C5PreClean -Names 'SystemPropertiesAdvanced.exe' } | Should -Throw '*DSO_VM_TESTS_MOCK_ONLY*'
        { Start-C5Process -FilePath 'C:\Windows\explorer.exe' -Arguments 'shell:::{A8A91A66-3A7D-4424-8D24-04E180695C7A}' -UseCreateProcess } | Should -Throw '*DSO_VM_TESTS_MOCK_ONLY*'
        foreach ($Command in 'Start-Process', 'Stop-Process', 'Register-ScheduledTask', 'Start-Job', 'Add-Type', 'Get-CimInstance', 'Get-NetConnectionProfile', 'Invoke-ExploreNative', 'New-Item') {
            Should -Invoke $Command -Times 0 -Exactly
        }
    }
}

Describe 'Wysyłka funkcji do VM (C5) i struktura skryptu' {
    It 'Get-C5RemoteScript jest poprawnym skryptem i definiuje wszystkie funkcje zdalne' {
        $Text = Get-C5RemoteScript
        $Errors = $null
        [void][System.Management.Automation.Language.Parser]::ParseInput($Text, [ref]$null, [ref]$Errors)
        @($Errors).Count | Should -Be 0
        foreach ($Name in ((Get-ExploreCommonRemoteFunctionNames) + (Get-C5RemoteFunctionNames))) { $Text | Should -Match "function $([regex]::Escape($Name)) \{" }
        (Get-C5RemoteFunctionNames) | Should -Contain 'Invoke-C5Collection'
    }
    It 'funkcje zdalne nie wołają niczego spoza listy wysyłanej; funkcje hosta nie są wysyłane' {
        $Text = Get-C5RemoteScript
        $Ast = [System.Management.Automation.Language.Parser]::ParseInput($Text, [ref]$null, [ref]$null)
        $Called = $Ast.FindAll({ param($n) $n -is [System.Management.Automation.Language.CommandAst] }, $true) |
            ForEach-Object { $_.GetCommandName() } | Where-Object { $_ -like '*-C5*' -or $_ -like '*-Explore*' } | Sort-Object -Unique
        $Sent = (Get-ExploreCommonRemoteFunctionNames) + (Get-C5RemoteFunctionNames)
        foreach ($Name in $Called) { $Sent | Should -Contain $Name }
        foreach ($HostOnly in (Get-C5HostOnlyFunctionNames)) { $Text | Should -Not -Match "function $([regex]::Escape($HostOnly)) \{"; $Called | Should -Not -Contain $HostOnly }
        $Text | Should -Not -Match 'Restore-VmCleanState|Wait-VmReady|Get-DsoVmCredential|Test-VmAuthFailure|Start-VM|Invoke-ExploreVmRun|Invoke-ExploreAll'
    }
    It 'każda funkcja skryptu jest albo wysyłana do VM, albo na liście funkcji hosta (nic nie ginie)' {
        $Ast = [System.Management.Automation.Language.Parser]::ParseFile((Resolve-Path $script:C5ScriptPath), [ref]$null, [ref]$null)
        $Defined = @($Ast.FindAll({ param($n) $n -is [System.Management.Automation.Language.FunctionDefinitionAst] }, $true) | ForEach-Object { $_.Name })
        foreach ($Name in $Defined) { ((Get-C5RemoteFunctionNames) + (Get-C5HostOnlyFunctionNames)) | Should -Contain $Name }
        foreach ($Name in (Get-C5RemoteFunctionNames)) { $Defined | Should -Contain $Name }
    }
    It 'polecenia ze skutkiem tylko w swoich funkcjach; zakazane (Remove-AppxPackage, zapis rejestru, irm/iex, restart, sieć) nie występują wcale' {
        $Ast = [System.Management.Automation.Language.Parser]::ParseFile((Resolve-Path $script:C5ScriptPath), [ref]$null, [ref]$null)
        $Commands = @($Ast.FindAll({ param($n) $n -is [System.Management.Automation.Language.CommandAst] }, $true))
        $Allowed = @{
            'Stop-Process'             = @('Invoke-C5Cleanup', 'Invoke-C5ShellKill', 'Invoke-C5ShellStartV32', 'Invoke-C5PreClean')
            'Start-Process'            = @('Start-C5Process')
            'Add-Type'                 = @('Initialize-C5Native')
            'Add-AppxPackage'          = @('Register-C5StorePackage')
            'Get-AppxPackage'          = @('Get-C5StorePackages')
            'Register-ScheduledTask'   = @('Invoke-C5UserTask')
            'Start-ScheduledTask'      = @('Invoke-C5UserTask')
            'Stop-ScheduledTask'       = @('Invoke-C5UserTask')
            'Unregister-ScheduledTask' = @('Invoke-C5UserTask')
            'Get-NetConnectionProfile' = @('Test-C5NoNetwork')
            'Start-Job'                = @('Register-C5StorePackage')
            'Invoke-ExploreNative'     = @('Invoke-C5ShellKill', 'Invoke-C5WsReset')
        }
        foreach ($Name in $Allowed.Keys) {
            foreach ($Command in ($Commands | Where-Object { $_.GetCommandName() -eq $Name })) { $Allowed[$Name] | Should -Contain (Get-CommandOwner $Command) -Because $Name }
        }
        # CreateProcess tylko w Start-C5CreateProcess (UseShellExecute=$false).
        $StartCalls = @($Ast.FindAll({ param($n) $n -is [System.Management.Automation.Language.InvokeMemberExpressionAst] -and $n.Member.Extent.Text -eq 'Start' -and $n.Expression.Extent.Text -match 'Diagnostics\.Process' }, $true))
        $StartCalls.Count | Should -Be 1
        (Get-CommandOwner $StartCalls[0]) | Should -Be 'Start-C5CreateProcess'
        (Get-Command Start-C5CreateProcess).Definition | Should -Match '\.UseShellExecute = \$false'
        foreach ($Command in ($Commands | Where-Object { $_.GetCommandName() -eq 'Add-AppxPackage' })) { $Command.Extent.Text | Should -Be 'Add-AppxPackage -Path $Path -Register -DisableDevelopmentMode -ErrorAction Stop' }
        foreach ($Command in ($Commands | Where-Object { $_.GetCommandName() -eq 'Stop-Process' })) { $Command.Extent.Text | Should -Match '^Stop-Process -Id \$\w+(\.ProcessId)? -Force' }
        $Names = @($Commands | ForEach-Object { $_.GetCommandName() } | Where-Object { $_ })
        foreach ($Forbidden in 'Remove-AppxPackage', 'Set-ItemProperty', 'New-ItemProperty', 'Remove-ItemProperty', 'Restart-Computer', 'Stop-Computer', 'Invoke-Expression', 'iex',
            'Invoke-RestMethod', 'irm', 'Invoke-WebRequest', 'iwr', 'Set-NetConnectionProfile', 'reg', 'reg.exe', 'Enable-ComputerRestore', 'Disable-ComputerRestore', 'Checkpoint-Computer',
            'Start-VM', 'Stop-VM', 'Restore-VMCheckpoint', 'Set-Service', 'Stop-Service', 'taskkill', 'taskkill.exe', 'control', 'control.exe', 'explorer', 'explorer.exe', 'wsreset', 'wsreset.exe', 'schtasks', 'schtasks.exe', 'cmd', 'cmd.exe') {
            $Names | Should -Not -Contain $Forbidden
        }
        $Text = [System.IO.File]::ReadAllText($script:C5ScriptPath, [System.Text.Encoding]::UTF8)
        $Text | Should -Not -Match 'HKCU:|HKEY_CURRENT_USER|EncodedCommand|\[Microsoft\.Win32\.Registry\]|windev|-Preset|Bruteforce_LTSC\s*[''"]'
        # Polecenie WinUtil występuje dokładnie raz (jako dana w Get-C5WinUtilCommand).
        ([regex]::Matches($Text, [regex]::Escape('irm https://christitus.com/win | iex'))).Count | Should -Be 1
        $Main = $Ast.FindAll({ param($n) $n -is [System.Management.Automation.Language.IfStatementAst] -and $n.Extent.Text -match '^if \(\$MyInvocation\.InvocationName' }, $false)[0]
        @($Main.FindAll({ param($n) $n -is [System.Management.Automation.Language.CommandAst] -and $n.GetCommandName() -eq 'New-Item' }, $true)).Count | Should -Be 0
    }
    It 'plik ma BOM UTF-8 oraz sekcje Instrukcja uruchomienia i Opis działania' {
        $Bytes = [System.IO.File]::ReadAllBytes($script:C5ScriptPath)
        ($Bytes[0..2] -join ',') | Should -Be '239,187,191'
        $Text = [System.IO.File]::ReadAllText($script:C5ScriptPath, [System.Text.Encoding]::UTF8)
        $Text.Substring(0, 400) | Should -Match 'Instrukcja uruchomienia'
        $Text | Should -Match 'Opis działania'
    }
    It 'parametr VmName przyjmuje tylko DSO-26H2-Home i DSO-26H2-Pro (nigdy Bruteforce_LTSC), w skrypcie i w Invoke-C5Exploration' {
        $Attr = (Get-Command $script:C5ScriptPath).Parameters['VmName'].Attributes | Where-Object { $_ -is [System.Management.Automation.ValidateSetAttribute] }
        $Attr.ValidValues | Should -Be @('DSO-26H2-Home', 'DSO-26H2-Pro')
        $Attr2 = (Get-Command Invoke-C5Exploration).Parameters['VmName'].Attributes | Where-Object { $_ -is [System.Management.Automation.ValidateSetAttribute] }
        $Attr2.ValidValues | Should -Be @('DSO-26H2-Home', 'DSO-26H2-Pro')
        { Invoke-C5Exploration -VmName 'Bruteforce_LTSC' -OutDirectory $TestDrive } | Should -Throw
        (Get-Command $script:C5ScriptPath).Parameters.Keys | Should -Not -Contain 'PrepareManualTest'
    }
    It 'ValidateSet -Probe jest identyczny w skrypcie, Invoke-C5Collection i Invoke-C5Exploration (cztery nazwy w kolejności przebiegu)' {
        $Expected = @('panels', 'winutil', 'store', 'explorer')
        foreach ($Command in (Get-Command $script:C5ScriptPath), (Get-Command Invoke-C5Collection), (Get-Command Invoke-C5Exploration)) {
            ($Command.Parameters['Probe'].Attributes | Where-Object { $_ -is [System.Management.Automation.ValidateSetAttribute] }).ValidValues | Should -Be $Expected
        }
    }
    It 'New-C5EntryPoint: tylko Invoke-C5Collection z opcjonalnym -Probe; przekazuje -UserSid; wstrzyknięcie odrzucone' {
        $Text = New-C5EntryPoint -Body "Invoke-C5Collection -Probe @('panels','explorer')"
        $Text | Should -Match "function Invoke-C5EntryPoint \{\s+param\(\[string\]\`$UserSid\)\s+Invoke-C5Collection -Probe @\('panels','explorer'\) -UserSid \`$UserSid\s+\}"
        { New-C5EntryPoint -Body 'Invoke-C5Collection' } | Should -Not -Throw
        foreach ($Bad in 'Invoke-C5Collection; calc', 'Invoke-C5Collection | Out-Null', 'Invoke-C5Collection $(calc)', "Invoke-C5Collection -Probe @('panels') -Foo", 'calc') {
            { New-C5EntryPoint -Body $Bad } | Should -Throw
        }
    }
    It 'Assert-C5OutDirectory: wymagany, bezwzględny (dysk albo UNC), poza repo; w repo tylko ignorowane przez git' {
        $Repo = 'C:\Repo\DSOptimizer'
        { Assert-C5OutDirectory -Path '' -RepoRoot $Repo } | Should -Throw '*wymagany*'
        foreach ($Relative in 'wyniki', '.\wyniki', '..\wyniki', '\wyniki', 'C:wyniki') { { Assert-C5OutDirectory -Path $Relative -RepoRoot $Repo } | Should -Throw '*bezwzględn*' }
        { Assert-C5OutDirectory -Path 'C:\Repo\DSOptimizer\tests\vm\out\x' -RepoRoot $Repo } | Should -Throw '*w repo*'
        { Assert-C5OutDirectory -Path 'C:\Repo\DSOptimizer' -RepoRoot $Repo } | Should -Throw '*w repo*'
        { Assert-C5OutDirectory -Path 'C:\Repo\DSOptimizer\..\DSOptimizer\docs' -RepoRoot $Repo } | Should -Throw '*w repo*'
        { Assert-C5OutDirectory -Path 'C:\Repo\DSOptimizer\wyniki\pomiar\exploration' -RepoRoot $Repo } | Should -Throw '*w repo*'
        $Real = (Resolve-Path (Join-Path $PSScriptRoot '..\..\..')).ProviderPath   # prawdziwe repo: git check-ignore
        Assert-C5OutDirectory -Path "$Real\tests\vm\out\x" -RepoRoot $Real | Should -Be "$Real\tests\vm\out\x"   # ignorowany przez git
        { Assert-C5OutDirectory -Path "$Real\src\x" -RepoRoot $Real } | Should -Throw '*ignorowanym przez git*'   # nieignorowany
        Assert-C5OutDirectory -Path (Join-Path $env:TEMP 'dso-explore') -RepoRoot $Real | Should -Be (Join-Path $env:TEMP 'dso-explore')   # poza repo
        Assert-C5OutDirectory -Path 'C:\Repo\DSOptimizer-inne\x\' -RepoRoot $Repo | Should -Be 'C:\Repo\DSOptimizer-inne\x'
        Assert-C5OutDirectory -Path '\\nas\udzial\c5' -RepoRoot $Repo | Should -Be '\\nas\udzial\c5'
        Assert-C5OutDirectory -Path 'D:\wyniki c5' -RepoRoot $Repo | Should -Be 'D:\wyniki c5'
    }
    It 'blok główny: Assert-C5OutDirectory i Resolve-ExploreOutDirectory PRZED Invoke-C5Exploration; bez domyślnego katalogu w repo' {
        $Text = [System.IO.File]::ReadAllText($script:C5ScriptPath, [System.Text.Encoding]::UTF8)
        $Main = $Text.Substring($Text.IndexOf('if ($MyInvocation.InvocationName'))
        $Assert = $Main.IndexOf('$OutDirectory = Assert-C5OutDirectory -Path $OutDirectory -RepoRoot')
        $Resolve = $Main.IndexOf('$OutDirectory = Resolve-ExploreOutDirectory -Path $OutDirectory')
        $Run = $Main.IndexOf('Invoke-C5Exploration @RunArguments')
        $Assert | Should -BeGreaterThan 0
        $Resolve | Should -BeGreaterThan $Assert
        $Run | Should -BeGreaterThan $Resolve
        $Main | Should -Not -Match "out\\\\c5-"
        # Regresja z przebiegu 2026-10-19: blok główny nie przekazuje pustego -Probe (ValidateSet odrzuca $null); wszystko idzie przez Get-C5RunArguments.
        @($Main -split "`r?`n" | Where-Object { $_ -match '-Probe \$Probe' -and $_ -notmatch '^\s*\$RunArguments = Get-C5RunArguments ' }).Count | Should -Be 0
        $Main | Should -Not -Match 'Invoke-C5Exploration -VmName'
        $Main.IndexOf('Get-C5RunArguments') | Should -BeLessThan $Run
        # -ShowPlan kończy PRZED ponownym importem modułów, sprawdzeniem uprawnień i jakąkolwiek pracą na VM.
        $Main.IndexOf('if ($ShowPlan)') | Should -BeLessThan $Main.IndexOf('IsInRole')
        # Przed planem wolno tylko zwykły import VmCommon (Get-DsoRepoRoot); ponowne importy z -Force dopiero po -ShowPlan.
        $Main.IndexOf('if ($ShowPlan)') | Should -BeLessThan $Main.IndexOf("modules\VmRun.psm1') -Force")
        $Main.IndexOf("modules\VmCommon.psm1')
") | Should -BeLessThan $Main.IndexOf('Assert-C5OutDirectory -Path')
    }
}

Describe 'Przebieg hosta C5 (orkiestracja na mockach; Hyper-V nietknięty)' {
    BeforeEach {
        Set-NoLiveVmDefaultMock -ModuleName VmHost
        Set-NoLiveVmDefaultMock -ModuleName ExploreCommon
        $script:Order = [System.Collections.Generic.List[string]]::new()
        Mock -ModuleName ExploreCommon Stop-VM { throw 'HOST: Stop-VM niedozwolone w testach' }
        Mock -ModuleName ExploreCommon Get-DsoVmCredential { [pscredential]::new('Administrator', (ConvertTo-SecureString 'x' -AsPlainText -Force)) }
        Mock -ModuleName ExploreCommon Restore-VmCleanState { $script:Order.Add("restore:${VmName}:$CheckpointName") }
        Mock -ModuleName ExploreCommon Start-VM { $script:Order.Add("start:$Name") }
        Mock -ModuleName ExploreCommon Wait-VmReady { $script:Order.Add("ready:$VmName"); [System.Runtime.Serialization.FormatterServices]::GetUninitializedObject([System.Management.Automation.Runspaces.PSSession]) }
        Mock -ModuleName ExploreCommon Remove-PSSession { $script:Order.Add('removesession') }
        Mock -ModuleName ExploreCommon Invoke-Command { throw 'HOST: Invoke-Command niedozwolone w tym teście' }
    }
    It 'bez zmockowanego wyłącznika prawdziwa orkiestracja zatrzymuje się na DSO_VM_TESTS_MOCK_ONLY: błąd zapisany, zero Start-VM i revertu' {
        $Outcome = Invoke-C5Exploration -VmName @('DSO-26H2-Pro') -OutDirectory $TestDrive
        @($Outcome.Paths).Count | Should -Be 0
        $Outcome.Errors[0] | Should -Match 'DSO_VM_TESTS_MOCK_ONLY'
        Should -Invoke -ModuleName ExploreCommon Start-VM -Times 0 -Exactly
        Should -Invoke -ModuleName ExploreCommon Restore-VmCleanState -Times 0 -Exactly
    }
    Context 'z wyłącznikiem zmockowanym tylko w module' {
        BeforeEach {
            Mock -ModuleName ExploreCommon Assert-DsoVmLiveAllowed {}
            Mock -ModuleName ExploreCommon Invoke-Command {
                if ($ArgumentList) { $script:Order.Add("collect:$($ArgumentList[1]):$($ArgumentList[2])"); $script:Defs = $ArgumentList[0]; '{"meta":{"EditionID":"Professional"},"userSid":"S-1-5-21-9-1001","probes":{}}' }
                else { $script:Order.Add('sid'); 'S-1-5-21-9-1001' }
            }
        }
        It 'revert -> start -> ready -> SID dso -> zbieranie (Invoke-C5Collection z -UserSid) -> usunięcie sesji -> revert; JSON c5-exploration.json' {
            $Outcome = Invoke-C5Exploration -VmName @('DSO-26H2-Pro') -OutDirectory $TestDrive
            ($Order -join ',') | Should -Be 'restore:DSO-26H2-Pro:czysta-26H2,start:DSO-26H2-Pro,ready:DSO-26H2-Pro,sid,collect:Invoke-C5Collection:S-1-5-21-9-1001,removesession,restore:DSO-26H2-Pro:czysta-26H2'
            $Outcome.Paths | Should -Be @((Join-Path $TestDrive 'DSO-26H2-Pro\c5-exploration.json'))
            $Doc = Get-Content -LiteralPath $Outcome.Paths[0] -Raw -Encoding UTF8 | ConvertFrom-Json
            $Doc.vmName | Should -Be 'DSO-26H2-Pro'
            $Doc.checkpoint | Should -Be 'czysta-26H2'
        }
        It '-Probe: punkt wejścia Invoke-C5EntryPoint z wybranymi sondami i -UserSid; skrypt zdalny poprawny składniowo' {
            $null = Invoke-C5Exploration -VmName @('DSO-26H2-Home') -OutDirectory $TestDrive -Probe 'explorer', 'panels'
            $script:Order | Should -Contain 'collect:Invoke-C5EntryPoint:S-1-5-21-9-1001'
            $script:Defs | Should -Match ([regex]::Escape("Invoke-C5Collection -Probe @('panels','explorer') -UserSid `$UserSid"))
            $Errors = $null
            [void][System.Management.Automation.Language.Parser]::ParseInput($script:Defs, [ref]$null, [ref]$Errors)
            @($Errors).Count | Should -Be 0
        }
        It 'dwie VM po kolei (Home, potem Pro), każda z własnym revertem przed i po; checkpoint z parametru' {
            $null = Invoke-C5Exploration -VmName @('DSO-26H2-Home', 'DSO-26H2-Pro') -OutDirectory $TestDrive -CheckpointName 'inny'
            @($Order | Where-Object { $_ -like 'restore:*' }) | Should -Be @('restore:DSO-26H2-Home:inny', 'restore:DSO-26H2-Home:inny', 'restore:DSO-26H2-Pro:inny', 'restore:DSO-26H2-Pro:inny')
        }
        It 'błąd zbierania: revert w finally, błąd zapisany, druga VM zebrana' {
            $script:Fail = $true
            Mock -ModuleName ExploreCommon Invoke-Command { if (-not $ArgumentList) { return 'S-1' }; if ($script:Fail) { $script:Fail = $false; throw 'zerwana sesja' }; '{"meta":{},"probes":{}}' }
            $Outcome = Invoke-C5Exploration -VmName @('DSO-26H2-Home', 'DSO-26H2-Pro') -OutDirectory $TestDrive
            @($Outcome.Errors).Count | Should -Be 1
            $Outcome.Errors[0] | Should -Match 'zerwana sesja'
            @($Outcome.Paths).Count | Should -Be 1
            $Order | Should -Contain 'restore:DSO-26H2-Home:czysta-26H2'
        }
        It 'błąd uwierzytelnienia zatrzymuje całość: druga VM nietknięta, revert pierwszej jest' {
            Mock -ModuleName ExploreCommon Wait-VmReady { $F = [System.InvalidOperationException]::new('odmowa logowania'); $F.Data['VmAuthFailure'] = $true; throw $F }
            { Invoke-C5Exploration -VmName @('DSO-26H2-Home', 'DSO-26H2-Pro') -OutDirectory $TestDrive } | Should -Throw '*odmowa logowania*'
            ($Order | Where-Object { $_ -like '*Pro*' }) | Should -BeNullOrEmpty
            $Order[-1] | Should -Be 'restore:DSO-26H2-Home:czysta-26H2'
        }
        It 'skrypt nie dubluje logiki Hyper-V (tylko Invoke-ExploreAll z -PassUserSid)' {
            $Text = [System.IO.File]::ReadAllText($script:C5ScriptPath, [System.Text.Encoding]::UTF8)
            $Text | Should -Match 'Invoke-ExploreAll .*-PassUserSid'
            $Text | Should -Not -Match 'Restore-VMCheckpoint|Start-VM|New-PSSession|Stop-VM|Invoke-Command'
        }
    }
}

Describe 'Blok główny SKRYPTU (wywołanie pliku z -ShowPlan; bez VM, bez Hyper-V)' {
    BeforeAll {
        # Bezpiecznik niezależny od -ShowPlan: gdyby blok główny jednak doszedł do orkiestracji, Assert-DsoVmLiveAllowed rzuca (DSO_VM_TESTS_MOCK_ONLY).
        $env:DSO_VM_TESTS_MOCK_ONLY | Should -Be '1'
        function script:Invoke-C5ScriptPlan {
            param([hashtable]$Arguments)
            $Output = & $script:C5ScriptPath @Arguments 6>$null
            [pscustomobject]@{ ExitCode = $LASTEXITCODE; Plan = (($Output | Out-String) | ConvertFrom-Json) }
        }
    }
    It 'bez -Probe: do Invoke-C5Exploration trafia pełna lista sond w kolejności przebiegu oraz domyślne VM, checkpoint i limit (regresja: pusty -Probe)' {
        $Result = Invoke-C5ScriptPlan -Arguments @{ OutDirectory = (Join-Path $TestDrive 'wyniki'); ShowPlan = $true }
        $Result.ExitCode | Should -Be 0
        @($Result.Plan.Probe) | Should -Be @('panels', 'winutil', 'store', 'explorer')
        @($Result.Plan.VmName) | Should -Be @('DSO-26H2-Home', 'DSO-26H2-Pro')
        $Result.Plan.CheckpointName | Should -Be 'czysta-26H2'
        $Result.Plan.ReadyTimeoutSeconds | Should -Be 1200
        $Result.Plan.OutDirectory | Should -Be (Join-Path $TestDrive 'wyniki')
        @($Result.Plan.Variant).Count | Should -Be 0
        Test-Path -LiteralPath (Join-Path $TestDrive 'wyniki') | Should -BeFalse
    }
    It 'z -Probe explorer,panels i jawnymi parametrami: wybrane sondy w kolejności przebiegu, jedna VM, checkpoint i limit z parametrów' {
        $Result = Invoke-C5ScriptPlan -Arguments @{ OutDirectory = (Join-Path $TestDrive 'w2'); Probe = @('explorer', 'panels'); VmName = @('DSO-26H2-Pro'); CheckpointName = 'inny'; ReadyTimeoutSeconds = 600; ShowPlan = $true }
        $Result.ExitCode | Should -Be 0
        @($Result.Plan.Probe) | Should -Be @('panels', 'explorer')
        @($Result.Plan.VmName) | Should -Be @('DSO-26H2-Pro')
        $Result.Plan.CheckpointName | Should -Be 'inny'
        $Result.Plan.ReadyTimeoutSeconds | Should -Be 600
    }
    It '-Probe panels -Variant (trzy warianty uzupełniające): plan pokazuje warianty; -Variant bez sondy panels => kod 1' {
        $Names = @('security-maintenance-control', 'system-properties-control', 'devices-printers-explorer')
        $Result = Invoke-C5ScriptPlan -Arguments @{ OutDirectory = (Join-Path $TestDrive 'w3'); Probe = @('panels'); Variant = $Names; VmName = @('DSO-26H2-Pro'); ShowPlan = $true }
        $Result.ExitCode | Should -Be 0
        @($Result.Plan.Probe) | Should -Be @('panels')
        @($Result.Plan.Variant) | Should -Be $Names
        & $script:C5ScriptPath -ShowPlan -OutDirectory (Join-Path $TestDrive 'w4') -Probe explorer -Variant 'devices-printers-explorer' 6>$null | Out-Null
        $LASTEXITCODE | Should -Be 1
        { & $script:C5ScriptPath -ShowPlan -OutDirectory (Join-Path $TestDrive 'w5') -Variant 'nieznany-wariant' } | Should -Throw
    }
    It 'świeży runspace (bez modułów załadowanych przez testy): -ShowPlan działa jak przy zwykłym uruchomieniu - kod 0 i plan (regresja: Get-DsoRepoRoot niewidoczny)' {
        $Runspace = [powershell]::Create()
        try {
            $Out = Join-Path $TestDrive 'swiezy'
            $null = $Runspace.AddScript("`$env:DSO_VM_TESTS_MOCK_ONLY = '1'; `$Plan = & '$($script:C5ScriptPath)' -VmName DSO-26H2-Pro -Probe panels -Variant security-maintenance-control,system-properties-control,devices-printers-explorer -OutDirectory '$Out' -ShowPlan 6>`$null; [pscustomobject]@{ Exit = `$LASTEXITCODE; Plan = (`$Plan | Out-String) }")
            $Result = @($Runspace.Invoke())[0]
            @($Runspace.Streams.Error | ForEach-Object { $_.ToString() }) | Should -BeNullOrEmpty
            $Result.Exit | Should -Be 0
            $Plan = $Result.Plan | ConvertFrom-Json
            @($Plan.Variant) | Should -Be @('security-maintenance-control', 'system-properties-control', 'devices-printers-explorer')
            @($Plan.VmName) | Should -Be @('DSO-26H2-Pro')
        }
        finally { $Runspace.Dispose() }
    }
    It 'bez -OutDirectory albo z katalogiem w repo (nieignorowanym przez git): kod 1 jeszcze przed planem (nic nie uruchomione)' {
        & $script:C5ScriptPath -ShowPlan 6>$null | Out-Null
        $LASTEXITCODE | Should -Be 1
        & $script:C5ScriptPath -ShowPlan -OutDirectory (Join-Path (Get-DsoRepoRoot) 'src\x') 6>$null | Out-Null
        $LASTEXITCODE | Should -Be 1
    }
    It 'Get-C5RunArguments: $null, pusta lista i lista z pustym elementem => pełna lista; wybór => kolejność przebiegu' {
        foreach ($Empty in @($null, @(), @(''))) {
            @((Get-C5RunArguments -VmName 'DSO-26H2-Home' -OutDirectory 'C:\w' -CheckpointName 'c' -ReadyTimeoutSeconds 60 -Probe $Empty).Probe) | Should -Be @('panels', 'winutil', 'store', 'explorer')
        }
        @((Get-C5RunArguments -VmName 'DSO-26H2-Home' -OutDirectory 'C:\w' -CheckpointName 'c' -ReadyTimeoutSeconds 60).Probe) | Should -Be @('panels', 'winutil', 'store', 'explorer')
        @((Get-C5RunArguments -VmName 'DSO-26H2-Home' -OutDirectory 'C:\w' -CheckpointName 'c' -ReadyTimeoutSeconds 60 -Probe 'store', 'winutil').Probe) | Should -Be @('winutil', 'store')
    }
    It 'Invoke-C5Exploration -Variant: punkt wejścia z -Probe i -Variant; -Variant bez panels w -Probe => wyjątek; New-C5EntryPoint przepuszcza tylko ten kształt' {
        Mock Invoke-ExploreAll { $script:SeenScript = $RemoteScript; [pscustomobject]@{ Paths = @(); Errors = @() } }
        $Arguments = Get-C5RunArguments -VmName @('DSO-26H2-Pro') -OutDirectory $TestDrive -CheckpointName 'czysta-26H2' -ReadyTimeoutSeconds 1200 -Probe 'panels' -Variant 'system-properties-control', 'devices-printers-explorer'
        $null = Invoke-C5Exploration @Arguments
        $script:SeenScript | Should -Match ([regex]::Escape("Invoke-C5Collection -Probe @('panels') -Variant @('system-properties-control','devices-printers-explorer') -UserSid `$UserSid"))
        { Get-C5RunArguments -VmName 'DSO-26H2-Pro' -OutDirectory 'C:\w' -CheckpointName 'c' -ReadyTimeoutSeconds 60 -Probe 'store' -Variant 'devices-printers-explorer' } | Should -Throw '*tylko sondy panels*'
        { Invoke-C5Exploration -VmName 'DSO-26H2-Pro' -OutDirectory $TestDrive -Probe 'store' -Variant 'devices-printers-explorer' } | Should -Throw '*tylko sondy panels*'
        { Invoke-C5Exploration -VmName 'DSO-26H2-Pro' -OutDirectory $TestDrive -Probe 'panels' -Variant 'nieznany' } | Should -Throw
        { New-C5EntryPoint -Body "Invoke-C5Collection -Probe @('panels') -Variant @('a-b','c')" } | Should -Not -Throw
        foreach ($Bad in "Invoke-C5Collection -Variant @('A;B')", "Invoke-C5Collection -Variant @('a') -Probe @('panels')", "Invoke-C5Collection -Variant @('a b')") { { New-C5EntryPoint -Body $Bad } | Should -Throw }
    }
    It 'pełna lista z Get-C5RunArguments przechodzi walidację Invoke-C5Exploration (splat jak w bloku głównym)' {
        Mock Invoke-ExploreAll { $script:SeenScript = $RemoteScript; $script:SeenEntry = $EntryCommand; [pscustomobject]@{ Paths = @(); Errors = @() } }
        $Arguments = Get-C5RunArguments -VmName @('DSO-26H2-Home') -OutDirectory $TestDrive -CheckpointName 'czysta-26H2' -ReadyTimeoutSeconds 1200 -Probe $null
        { $null = Invoke-C5Exploration @Arguments } | Should -Not -Throw
        $script:SeenEntry | Should -Be 'Invoke-C5EntryPoint'
        $script:SeenScript | Should -Match ([regex]::Escape("Invoke-C5Collection -Probe @('panels','winutil','store','explorer') -UserSid `$UserSid"))
    }
}

AfterAll { Clear-NoLiveVm }
