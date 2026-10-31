BeforeDiscovery {
    $script:HasHyperV = [bool](Get-Module -ListAvailable -Name Hyper-V)
}

BeforeAll {
    # Wyłącznik: żaden test na mockach nie może dotknąć prawdziwego Hyper-V (patrz Support\NoLiveVm.ps1).
    . (Join-Path $PSScriptRoot 'Support\NoLiveVm.ps1')
    Import-Module (Join-Path $PSScriptRoot '..\modules\VmCommon.psm1') -Force
    Import-Module (Join-Path $PSScriptRoot '..\modules\VmHost.psm1') -Force
    # Skrypt jest dot-source'owany: przy '.' nie uruchamia przebiegu na VM, tylko definiuje funkcje.
    . (Join-Path $PSScriptRoot '..\explore\Invoke-E4B2Exploration.ps1')
    $script:E4B2ScriptPath = Join-Path $PSScriptRoot '..\explore\Invoke-E4B2Exploration.ps1'

    function script:Get-CommandOwner {
        param($Node)
        $Parent = $Node.Parent
        while ($null -ne $Parent -and $Parent -isnot [System.Management.Automation.Language.FunctionDefinitionAst]) { $Parent = $Parent.Parent }
        if ($null -ne $Parent) { $Parent.Name } else { '(poza funkcją)' }
    }

    function script:New-FakeWatch {
        # Stoper testowy: Elapsed liczone z $script:FakeSeconds (przesuwa je mock Wait-E4B2ToolExit).
        $Watch = [pscustomobject]@{}
        $Watch | Add-Member -MemberType ScriptProperty -Name Elapsed -Value { [timespan]::FromSeconds($script:FakeSeconds) }
        return $Watch
    }
}

Describe 'Plan pomiaru (plan Tasku 2 partii 2, zawężony decyzją R8)' {
    It 'lista procesów serwisowania z planu, czasy próbkowania i limity narzędzi' {
        $Plan = Get-E4B2Plan
        @($Plan.WatchNames) | Should -Be @('dism', 'DismHost', 'sfc', 'TiWorker', 'TrustedInstaller', 'cleanmgr')
        $Plan.IdleSampleSeconds | Should -BeGreaterOrEqual 120
        $Plan.FinalIdleSeconds | Should -BeGreaterOrEqual 60
        $Plan.SampleSeconds | Should -BeGreaterThan 0
        $Plan.ConcurrentDelaySeconds | Should -BeGreaterThan 0
        $Plan.DismTimeoutSeconds | Should -BeGreaterOrEqual 2700
        $Plan.SfcTimeoutSeconds | Should -BeGreaterOrEqual 2700
        $Plan.CleanmgrTimeoutSeconds | Should -BeGreaterOrEqual 1200
    }
    It 'brak źródła DISM, numer sagerun, klucze VolumeCaches, wiek plików i Windows.old z planu' {
        $Plan = Get-E4B2Plan
        $Plan.NoSourcePath | Should -Be 'C:\brak'
        $Plan.SageRunId | Should -BeOfType [int]
        $Plan.SageRunId | Should -BeGreaterThan 0
        $Plan.SageRunId | Should -BeLessThan 10000
        $Plan.VolumeCachesKey | Should -Be 'HKLM\SOFTWARE\Microsoft\Windows\CurrentVersion\Explorer\VolumeCaches'
        $Plan.TempHandler | Should -Be 'Temporary Files'
        $Plan.PreviousHandler | Should -Be 'Previous Installations'
        $Plan.WindowsOldPath | Should -Be 'C:\Windows.old'
        $Plan.WindowsOldMegabytes | Should -Be 200
        $Plan.OldAgeDays | Should -Be 2
        $Plan.VeryOldAgeDays | Should -BeGreaterThan 7
    }
    It 'zakres R8: plan nie ma pliku chronionego do uszkodzenia ani właściciela/ACL dla Windows.old' {
        $Plan = Get-E4B2Plan
        foreach ($Key in 'ProtectedFile', 'TrustedInstallerSid', 'OwnerSid') { $Plan.Contains($Key) | Should -BeFalse -Because $Key }
    }
}

Describe 'Czysta logika (bez VM)' {
    It 'Get-E4B2TimeoutSeconds: max(4 × czas, 45 min) dla DISM/SFC i max(4 × czas, 20 min) dla czyszczenia' {
        Get-E4B2TimeoutSeconds -MeasuredSeconds 300 -Kind Servicing | Should -Be 2700
        Get-E4B2TimeoutSeconds -MeasuredSeconds 900 -Kind Servicing | Should -Be 3600
        Get-E4B2TimeoutSeconds -MeasuredSeconds 100 -Kind Cleanup | Should -Be 1200
        Get-E4B2TimeoutSeconds -MeasuredSeconds 400.4 -Kind Cleanup | Should -Be 1602
    }
    It 'Get-E4B2StateFlagsName: StateFlags + cztery cyfry' {
        Get-E4B2StateFlagsName -Id 7351 | Should -Be 'StateFlags7351'
        Get-E4B2StateFlagsName -Id 5 | Should -Be 'StateFlags0005'
    }
    It 'Get-E4B2OutputAnalysis: UTF-16LE z BOM, postęp z samym \r, procent z przecinkiem, ostatnie linie, pierwszy znak' {
        $Text = "`r`nRozpoczynanie skanowania.`r`nWeryfikacja 12% ukończona.`rWeryfikacja 45,5% ukończona.`rWeryfikacja 100% ukończona.`r`n`r`nOchrona zasobów nie znalazła naruszeń.`r`n"
        $Bytes = [byte[]](@(0xFF, 0xFE) + [System.Text.Encoding]::Unicode.GetBytes($Text))
        $A = Get-E4B2OutputAnalysis -Bytes $Bytes -OemCodePage 852
        $A.Bom | Should -Be 'utf16le'
        $A.Encoding | Should -Be 'utf16le'
        $A.CrOnly | Should -Be 2
        $A.CrLf | Should -Be 5
        $A.LfOnly | Should -Be 0
        @($A.Percents) | Should -Be @('12', '45,5', '100')
        $A.PercentCount | Should -Be 3
        $A.DecimalSeparator | Should -Be ','
        $A.FirstChar | Should -Be 'U+000D'
        $A.FirstBytesHex | Should -Match '^FF FE 0D 00'
        $A.LastLines[-1] | Should -Be 'Ochrona zasobów nie znalazła naruszeń.'
        @($A.PercentContexts)[0] | Should -Match '\\r'
    }
    It 'Get-E4B2OutputAnalysis: UTF-16LE bez BOM rozpoznany po zerach; OEM 852 z polskimi znakami i procentem z kropką' {
        $Utf16 = [System.Text.Encoding]::Unicode.GetBytes("Verification 5% complete.`r")
        $U = Get-E4B2OutputAnalysis -Bytes $Utf16 -OemCodePage 852
        $U.Bom | Should -Be 'none'
        $U.Encoding | Should -Be 'utf16le'
        $Oem = [System.Text.Encoding]::GetEncoding(852).GetBytes("[==         18.4%         ]`r`nOperacja zakończona pomyślnie.`r`n")
        $A = Get-E4B2OutputAnalysis -Bytes $Oem -OemCodePage 852
        $A.Bom | Should -Be 'none'
        $A.Encoding | Should -Be 'oem852'
        @($A.Percents) | Should -Be @('18.4')
        $A.DecimalSeparator | Should -Be '.'
        $A.NonAsciiBytes | Should -BeGreaterThan 0
        $A.LastLines[-1] | Should -Be 'Operacja zakończona pomyślnie.'
        $A.Length | Should -Be $Oem.Length
    }
    It 'Get-E4B2OutputAnalysis: puste wyjście nie rzuca' {
        $A = Get-E4B2OutputAnalysis -Bytes ([byte[]]@()) -OemCodePage 852
        $A.Length | Should -Be 0
        @($A.Percents).Count | Should -Be 0
        $A.DecimalSeparator | Should -Be 'brak'
        $A.FirstChar | Should -BeNullOrEmpty
    }
    It 'Get-E4B2SrSummary: tylko linie [SR], liczniki znaczników i szablony bez cyfr' {
        $Text = @(
            '2026-10-22 10:00:01, Info                  CSI    00000001 [SR] Beginning Verify and Repair transaction'
            '2026-10-22 10:00:02, Info                  CSI    00000002 [SR] Verify complete'
            '2026-10-22 10:00:03, Info                  CBS    zwykła linia bez znacznika'
            '2026-10-22 10:00:04, Info                  CSI    00000003 [SR] Repairing 1 components'
            '2026-10-22 10:00:05, Info                  CSI    00000004 [SR] Cannot repair member file [l:8]"calc.exe" of Microsoft-Windows-Calc'
            '2026-10-22 10:00:06, Info                  CSI    00000005 [SR] Repair complete'
        ) -join "`r`n"
        $S = Get-E4B2SrSummary -Text $Text
        $S.SrLineCount | Should -Be 5
        $S.Counts['Verify complete'] | Should -Be 1
        $S.Counts['Repairing'] | Should -Be 1
        $S.Counts['Cannot repair member file'] | Should -Be 1
        $S.Counts['Repair complete'] | Should -Be 1
        $S.Counts['Repaired file'] | Should -Be 0
        @($S.Templates) | Should -Contain 'Repairing # components'
        (Get-E4B2SrSummary -Text '').SrLineCount | Should -Be 0
    }
    It 'Get-E4B2KeyLines: linie pasujące do wzorca, początek i koniec przy nadmiarze' {
        $Text = (1..10 | ForEach-Object { "linia $_ Error" }) + @('nic') -join "`n"
        @(Get-E4B2KeyLines -Text $Text -Pattern 'Error' -Head 2 -Tail 1) | Should -Be @('linia 1 Error', 'linia 2 Error', '…', 'linia 10 Error')
        @(Get-E4B2KeyLines -Text 'a Error' -Pattern 'Error' -Head 5 -Tail 5) | Should -Be @('a Error')
        @(Get-E4B2KeyLines -Text '' -Pattern 'Error').Count | Should -Be 0
    }
    It 'Get-E4B2PresenceSummary: liczba próbek, pierwsza i ostatnia sekunda dla każdej nazwy z listy' {
        $Samples = @(
            [pscustomobject]@{ T = 5; Present = @('TiWorker:10:0', 'TrustedInstaller:11:0') }
            [pscustomobject]@{ T = 10; Present = @('TiWorker:10:0') }
            [pscustomobject]@{ T = 15; Present = @() }
        )
        $P = Get-E4B2PresenceSummary -Samples $Samples -WatchNames @('TiWorker', 'TrustedInstaller', 'dism')
        $P['TiWorker'].Samples | Should -Be 2
        $P['TiWorker'].FirstSeconds | Should -Be 5
        $P['TiWorker'].LastSeconds | Should -Be 10
        $P['TrustedInstaller'].Samples | Should -Be 1
        $P['dism'].Samples | Should -Be 0
        $P['dism'].FirstSeconds | Should -BeNullOrEmpty
        $P.Total | Should -Be 3
    }
    It 'Get-E4B2PhaseOrder: stała kolejność, baseline zawsze, network tylko z siecią, final na końcu' {
        @(Get-E4B2PhaseOrder -Phase @('final', 'sfc')) | Should -Be @('baseline', 'sfc', 'final')
        @(Get-E4B2PhaseOrder -Phase @('network', 'dism') -Network $false) | Should -Be @('baseline', 'dism')
        @(Get-E4B2PhaseOrder -Phase @('final', 'network', 'windowsold', 'cleanup', 'sfc', 'dism', 'baseline')) |
            Should -Be @('baseline', 'dism', 'sfc', 'cleanup', 'windowsold', 'network', 'final')
        @(Get-E4B2NetworkPhases) | Should -Be @('network')
    }
    It 'Get-E4B2ToolPath: tylko pełna ścieżka System32 i tylko narzędzia z listy' {
        Get-E4B2ToolPath -Tool 'dism.exe' | Should -Be (Join-Path $env:SystemRoot 'System32\dism.exe')
        Get-E4B2ToolPath -Tool 'sfc.exe' | Should -Be (Join-Path $env:SystemRoot 'System32\sfc.exe')
        Get-E4B2ToolPath -Tool 'cleanmgr.exe' | Should -Be (Join-Path $env:SystemRoot 'System32\cleanmgr.exe')
        foreach ($Bad in 'cmd.exe', 'takeown.exe', 'icacls.exe', '..\dism.exe', 'C:\Windows\System32\dism.exe') { { Get-E4B2ToolPath -Tool $Bad } | Should -Throw }
    }
}

Describe 'Bezpieczniki (VM-only, mock-only)' {
    It 'Test-E4B2VmComputerName: tylko DSO-26H2-HOME/PRO' {
        Test-E4B2VmComputerName -ComputerName 'dso-26h2-pro' | Should -BeTrue
        Test-E4B2VmComputerName -ComputerName 'DSO-26H2-HOME' | Should -BeTrue
        Test-E4B2VmComputerName -ComputerName 'BRUTEFORCE_LTSC' | Should -BeFalse
        Test-E4B2VmComputerName -ComputerName '' | Should -BeFalse
    }
    It 'Assert-E4B2InsideVm rzuca w trybie mock-only, nawet z nazwą VM; poza trybem rzuca na hoście' {
        { Assert-E4B2InsideVm -ComputerName 'DSO-26H2-PRO' } | Should -Throw '*DSO_VM_TESTS_MOCK_ONLY*'
        $Saved = $env:DSO_VM_TESTS_MOCK_ONLY
        try {
            [Environment]::SetEnvironmentVariable('DSO_VM_TESTS_MOCK_ONLY', $null)
            { Assert-E4B2InsideVm -ComputerName 'HOST-PC' } | Should -Throw '*tylko w VM*'
            { Assert-E4B2InsideVm -ComputerName 'DSO-26H2-HOME' } | Should -Not -Throw
        }
        finally { $env:DSO_VM_TESTS_MOCK_ONLY = $Saved }
    }
    It '<Name> rzuca przed jakimkolwiek skutkiem (DISM, SFC, cleanmgr, pliki, usługi, rejestr)' -ForEach @(
        @{ Name = 'Start-E4B2Tool'; Code = { Start-E4B2Tool -Tool 'dism.exe' -Arguments '/Online /Cleanup-Image /ScanHealth' -OutFile 'C:\x.bin' -ErrFile 'C:\y.bin' } }
        @{ Name = 'Complete-E4B2Tool'; Code = { Complete-E4B2Tool -Handle ([pscustomobject]@{}) -Kill } }
        @{ Name = 'Invoke-E4B2Tool'; Code = { Invoke-E4B2Tool -Name 'x' -Tool 'sfc.exe' -Arguments '/scannow' -TimeoutSeconds 10 -WatchNames @('sfc') } }
        @{ Name = 'Save-E4B2Raw'; Code = { Save-E4B2Raw -Name 'x.bin' -Bytes ([byte[]]@(1)) } }
        @{ Name = 'Invoke-E4B2CleanmgrRun'; Code = { Invoke-E4B2CleanmgrRun -Name 'x' -Handler 'Temporary Files' -Id 7351 -VolumeCachesKey 'HKLM\X' -TimeoutSeconds 10 -WatchNames @('cleanmgr') } }
        @{ Name = 'Invoke-E4B2CleanmgrUserRun'; Code = { Invoke-E4B2CleanmgrUserRun -Name 'x' -Handler 'Previous Installations' -Id 7351 -VolumeCachesKey 'HKLM\X' -TimeoutSeconds 10 -WatchNames @('cleanmgr') } }
        @{ Name = 'Stop-E4B2ProcessById'; Code = { Stop-E4B2ProcessById -ProcessId 4 } }
        @{ Name = 'Set-E4B2SageFlag'; Code = { Set-E4B2SageFlag -VolumeCachesKey 'HKLM\X' -Handler 'Temporary Files' -Id 7351 } }
        @{ Name = 'Remove-E4B2SageFlag'; Code = { Remove-E4B2SageFlag -VolumeCachesKey 'HKLM\X' -Handler 'Temporary Files' -Id 7351 } }
        @{ Name = 'Remove-E4B2OldFiles'; Code = { Remove-E4B2OldFiles -Root 'C:\Windows\Temp' -CutoffUtc (Get-Date) } }
        @{ Name = 'New-E4B2TempSet'; Code = { New-E4B2TempSet -Root 'C:\Windows\Temp' -Label 'x' -GuardRoot 'C:\dso\g' -OldAgeDays 2 -VeryOldAgeDays 10 } }
        @{ Name = 'Close-E4B2TempSet'; Code = { Close-E4B2TempSet -Set ([pscustomobject]@{ Lock = $null }) } }
        @{ Name = 'New-E4B2WindowsOld'; Code = { New-E4B2WindowsOld -Path 'C:\Windows.old' -Megabytes 1 } }
        @{ Name = 'Stop-E4B2Service'; Code = { Stop-E4B2Service -Name 'wuauserv' } }
        @{ Name = 'Start-E4B2Service'; Code = { Start-E4B2Service -Name 'wuauserv' } }
        @{ Name = 'Remove-E4B2File'; Code = { Remove-E4B2File -Path 'C:\x.db' } }
        @{ Name = 'Invoke-E4B2DownloadProbe'; Code = { Invoke-E4B2DownloadProbe -TestMegabytes 1 } }
        @{ Name = 'Invoke-E4B2DeliveryOptimizationProbe'; Code = { Invoke-E4B2DeliveryOptimizationProbe } }
        @{ Name = 'Invoke-E4B2ThumbcacheProbe'; Code = { Invoke-E4B2ThumbcacheProbe -UserSid 'S-1-5-21-1' } }
        @{ Name = 'Invoke-E4B2DismProbe'; Code = { Invoke-E4B2DismProbe -Plan (Get-E4B2Plan) } }
        @{ Name = 'Invoke-E4B2SfcProbe'; Code = { Invoke-E4B2SfcProbe -Plan (Get-E4B2Plan) } }
        @{ Name = 'Invoke-E4B2CleanupProbe'; Code = { Invoke-E4B2CleanupProbe -Plan (Get-E4B2Plan) -UserSid 'S-1-5-21-1' } }
        @{ Name = 'Invoke-E4B2WindowsOldProbe'; Code = { Invoke-E4B2WindowsOldProbe -Plan (Get-E4B2Plan) } }
        @{ Name = 'Invoke-E4B2NetworkProbe'; Code = { Invoke-E4B2NetworkProbe -Plan (Get-E4B2Plan) } }
        @{ Name = 'Invoke-E4B2Collection'; Code = { Invoke-E4B2Collection -PlanJson '{}' -Phase 'baseline' -UserSid 'S-1-5-21-1' } }
    ) {
        Mock Invoke-ExploreNative { throw 'nie wolno' }
        if ($Name -ne 'Start-E4B2Tool') { Mock Start-E4B2Tool { throw 'nie wolno' } }
        Mock New-Item { throw 'nie wolno' }
        Mock Remove-Item { throw 'nie wolno' }
        Mock Stop-Service { throw 'nie wolno' }
        Mock Start-Service { throw 'nie wolno' }
        { & $Code } | Should -Throw '*DSO_VM_TESTS_MOCK_ONLY*'
        foreach ($Command in 'Invoke-ExploreNative', 'New-Item', 'Remove-Item', 'Stop-Service', 'Start-Service') { Should -Invoke $Command -Times 0 -Exactly }
        if ($Name -ne 'Start-E4B2Tool') { Should -Invoke Start-E4B2Tool -Times 0 -Exactly }
    }
    It 'każda funkcja wysyłana do VM ze skutkiem (wprost albo przez inną taką funkcję) zaczyna się od Assert-E4B2InsideVm' {
        $EffectCommands = @('Set-Content', 'New-Item', 'Remove-Item', 'Move-Item', 'Copy-Item', 'Invoke-ExploreNative', 'Stop-Process', 'Stop-Service', 'Start-Service', 'Delete-DeliveryOptimizationCache',
            'Register-ScheduledTask', 'Start-ScheduledTask', 'Stop-ScheduledTask', 'Unregister-ScheduledTask')
        $EffectMembers = @('Kill', 'Start', 'Delete', 'WriteAllBytes', 'WriteAllText', 'Create', 'CreateDirectory', 'SetValue', 'DeleteValue', 'SetLastWriteTimeUtc', 'SetLastAccessTimeUtc',
            'SetCreationTimeUtc', 'Write', 'CopyToAsync', 'Open')
        $Ast = [System.Management.Automation.Language.Parser]::ParseFile((Resolve-Path $script:E4B2ScriptPath), [ref]$null, [ref]$null)
        $Remote = @(Get-E4B2RemoteFunctionNames)
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
        # Wyjątki odczytowe: otwierają pliki tylko do odczytu albo czekają na proces (bez skutku w systemie).
        $ReadOnly = @('Read-E4B2FileFrom', 'Test-E4B2FileLocked', 'Wait-E4B2ToolExit')
        do {
            $Changed = $false
            foreach ($Name in $Functions.Keys) {
                if (-not $Effect[$Name] -and @($Calls[$Name] | Where-Object { $Effect[$_] -and $ReadOnly -notcontains $_ -and $_ -ne 'Assert-E4B2InsideVm' }).Count -gt 0) { $Effect[$Name] = $true; $Changed = $true }
            }
        } while ($Changed)
        $Guarded = @($Functions.Keys | Where-Object { $Effect[$_] -and $ReadOnly -notcontains $_ } | Sort-Object)
        foreach ($Name in 'Start-E4B2Tool', 'Complete-E4B2Tool', 'Invoke-E4B2Tool', 'Save-E4B2Raw', 'Invoke-E4B2CleanmgrRun', 'Invoke-E4B2CleanmgrUserRun', 'Stop-E4B2ProcessById', 'Set-E4B2SageFlag', 'Remove-E4B2SageFlag', 'Remove-E4B2OldFiles',
            'New-E4B2TempSet', 'New-E4B2WindowsOld', 'Stop-E4B2Service', 'Start-E4B2Service', 'Remove-E4B2File', 'Invoke-E4B2DownloadProbe', 'Invoke-E4B2DeliveryOptimizationProbe',
            'Invoke-E4B2ThumbcacheProbe', 'Invoke-E4B2DismProbe', 'Invoke-E4B2SfcProbe', 'Invoke-E4B2CleanupProbe', 'Invoke-E4B2WindowsOldProbe', 'Invoke-E4B2NetworkProbe', 'Invoke-E4B2Collection') {
            $Guarded | Should -Contain $Name
        }
        foreach ($Name in $Guarded) {
            $First = @($Functions[$Name].Body.EndBlock.Statements)[0]
            $First.Extent.Text | Should -Match '^Assert-E4B2InsideVm\b' -Because "$Name ma skutek, więc pierwsza instrukcja to bezpiecznik"
        }
    }
}

Describe 'Sondy w VM (mocki: nic nie jest zmieniane naprawdę)' {
    BeforeEach {
        Mock Assert-E4B2InsideVm { }
        Mock Start-Sleep { }
        Mock Get-E4B2FreeBytes { 1000 }
        Mock Get-E4B2PendingState { [pscustomobject]@{ RebootPending = $false } }
        Mock Get-E4B2FileLength { 100 }
        Mock Read-E4B2FileFrom { [pscustomobject]@{ Bytes = [byte[]]@(); Rotated = $false; Truncated = $false; Start = $Offset; LengthNow = 100 } }
        Mock Get-E4B2ProcessSample { @() }
        Mock Get-E4B2ChildProcesses { @() }
        Mock Save-E4B2Raw { $Name }
        $script:FakeSeconds = 0
    }
    It 'Invoke-E4B2Tool: narzędzie z System32, drugi proces raz po opóźnieniu, limit = zabicie tylko w VM, logi od offsetu, surowe bajty zapisane' {
        $script:Started = [System.Collections.Generic.List[string]]::new()
        $script:Killed = [System.Collections.Generic.List[string]]::new()
        Mock Start-E4B2Tool { $script:Started.Add("$Tool $Arguments"); [pscustomobject]@{ Tool = $Tool; Arguments = $Arguments; Watch = (New-FakeWatch); ProcessId = 1; OutFile = $OutFile; ErrFile = $ErrFile } }
        Mock Wait-E4B2ToolExit { $script:FakeSeconds += 5; $false }
        Mock Complete-E4B2Tool { if ($Kill) { $script:Killed.Add($Handle.Tool) }; [pscustomobject]@{ ExitCode = 0; TimedOut = [bool]$Kill; Killed = [bool]$Kill; OutBytes = [byte[]]@(); ErrBytes = [byte[]]@(); DurationSeconds = 30 } }
        $Out = Invoke-E4B2Tool -Name 'dism-restorehealth' -Tool 'dism.exe' -Arguments '/Online /Cleanup-Image /RestoreHealth' -TimeoutSeconds 30 -SampleSeconds 5 -WatchNames @('dism') `
            -Concurrent @(@{ Name = 'dism-concurrent'; Tool = 'dism.exe'; Arguments = '/Online /Cleanup-Image /CheckHealth'; DelaySeconds = 10; TimeoutSeconds = 20 }) -Logs
        @($script:Started) | Should -Be @('dism.exe /Online /Cleanup-Image /RestoreHealth', 'dism.exe /Online /Cleanup-Image /CheckHealth')
        $Out.TimedOut | Should -BeTrue
        $Out.Command | Should -Be "$(Join-Path $env:SystemRoot 'System32\dism.exe') /Online /Cleanup-Image /RestoreHealth"
        $script:Killed | Should -Contain 'dism.exe'
        Should -Invoke Read-E4B2FileFrom -Times 2 -Exactly -Scope It -ParameterFilter { $Offset -eq 100 }
        Should -Invoke Save-E4B2Raw -Scope It -ParameterFilter { $Name -eq 'dism-restorehealth.out.bin' }
        Should -Invoke Save-E4B2Raw -Scope It -ParameterFilter { $Name -eq 'dism-concurrent.out.bin' }
        Should -Invoke Save-E4B2Raw -Scope It -ParameterFilter { $Name -eq 'dism-restorehealth.cbs.log' }
        @($Out.Concurrent).Count | Should -Be 1
        $Out.Concurrent[0].StartedAfterSeconds | Should -BeGreaterOrEqual 10
    }
    It 'Invoke-E4B2Tool: z -WatchPath próbka ma istnienie ścieżki, wolne miejsce i procesy potomne' {
        Mock Start-E4B2Tool { [pscustomobject]@{ Tool = $Tool; Arguments = $Arguments; Watch = (New-FakeWatch); ProcessId = 77; OutFile = $OutFile; ErrFile = $ErrFile } }
        Mock Wait-E4B2ToolExit { $script:FakeSeconds += 5; $script:FakeSeconds -ge 10 }
        Mock Complete-E4B2Tool { [pscustomobject]@{ ExitCode = 0; TimedOut = $false; Killed = $false; OutBytes = [byte[]]@(); ErrBytes = [byte[]]@(); DurationSeconds = 10 } }
        Mock Test-Path { $true } -ParameterFilter { $LiteralPath -eq 'C:\Windows.old' }
        Mock Get-E4B2ChildProcesses { @('DismHost:12') } -ParameterFilter { $ParentId -eq 77 }
        $Out = Invoke-E4B2Tool -Name 'cleanmgr-previous' -Tool 'cleanmgr.exe' -Arguments '/sagerun:7351' -TimeoutSeconds 600 -SampleSeconds 5 -WatchNames @('cleanmgr') -WatchPath 'C:\Windows.old'
        $Out.Samples[0].PathExists | Should -BeTrue
        $Out.Samples[0].FreeBytes | Should -Be 1000
        @($Out.Samples[0].Children) | Should -Be @('DismHost:12')
        $Out.WatchPath | Should -Be 'C:\Windows.old'
    }
    It 'Invoke-E4B2Tool: zakończony przed limitem = bez zabijania, kod szesnastkowo' {
        Mock Start-E4B2Tool { [pscustomobject]@{ Tool = $Tool; Arguments = $Arguments; Watch = (New-FakeWatch); ProcessId = 1; OutFile = $OutFile; ErrFile = $ErrFile } }
        Mock Wait-E4B2ToolExit { $script:FakeSeconds += 5; $script:FakeSeconds -ge 10 }
        Mock Complete-E4B2Tool { [pscustomobject]@{ ExitCode = -2146498529; TimedOut = $false; Killed = [bool]$Kill; OutBytes = [byte[]]@(); ErrBytes = [byte[]]@(); DurationSeconds = 10 } }
        $Out = Invoke-E4B2Tool -Name 'dism-nosource' -Tool 'dism.exe' -Arguments '/x' -TimeoutSeconds 600 -SampleSeconds 5 -WatchNames @('dism')
        $Out.TimedOut | Should -BeFalse
        $Out.ExitCodeHex | Should -Be '0x800F081F'
        Should -Invoke Complete-E4B2Tool -Times 1 -Exactly -Scope It -ParameterFilter { -not $Kill }
        Should -Invoke Read-E4B2FileFrom -Times 0 -Exactly -Scope It
    }
    It 'Invoke-E4B2CleanmgrRun: obca flaga StateFlags<N> = nic nie uruchamia; własna flaga usunięta także po błędzie cleanmgr' {
        $script:Order = [System.Collections.Generic.List[string]]::new()
        Mock Test-E4B2HandlerKey { $true }
        Mock Get-E4B2SageFlags { @([pscustomobject]@{ Handler = 'Downloaded Program Files'; Name = 'StateFlags7351'; Value = 2 }) }
        Mock Set-E4B2SageFlag { $script:Order.Add("set $Handler") }
        Mock Remove-E4B2SageFlag { $script:Order.Add("remove $Handler") }
        Mock Invoke-E4B2Tool { $script:Order.Add("run $Arguments"); throw 'cleanmgr padł' }
        $Out = Invoke-E4B2CleanmgrRun -Name 'cleanmgr-temp' -Handler 'Temporary Files' -Id 7351 -VolumeCachesKey 'HKLM\X' -TimeoutSeconds 60 -WatchNames @('cleanmgr')
        $Out.Refused | Should -Match 'StateFlags7351'
        $script:Order.Count | Should -Be 0
        Mock Get-E4B2SageFlags { @() }
        $Out = Invoke-E4B2CleanmgrRun -Name 'cleanmgr-temp' -Handler 'Temporary Files' -Id 7351 -VolumeCachesKey 'HKLM\X' -TimeoutSeconds 60 -WatchNames @('cleanmgr')
        @($script:Order) | Should -Be @('set Temporary Files', 'run /sagerun:7351', 'remove Temporary Files')
        $Out.Error | Should -Match 'cleanmgr padł'
    }
    It 'Invoke-E4B2CleanmgrUserRun: obca flaga = nic nie rejestruje; własna flaga i zadanie usunięte także po błędzie' {
        $script:Order = [System.Collections.Generic.List[string]]::new()
        Mock Test-E4B2HandlerKey { $true }
        Mock Get-E4B2SageFlags { @([pscustomobject]@{ Handler = 'Temporary Files'; Name = 'StateFlags7351'; Value = 2 }) }
        Mock Set-E4B2SageFlag { $script:Order.Add("set $Handler") }
        Mock Remove-E4B2SageFlag { $script:Order.Add("remove $Handler") }
        Mock Register-ScheduledTask -RemoveParameterType 'Action', 'Principal', 'Settings' { $script:Order.Add("register $TaskName") }
        Mock Start-ScheduledTask { $script:Order.Add('start'); throw 'zadanie padło' }
        Mock Unregister-ScheduledTask { $script:Order.Add("unregister $TaskName") }
        Mock New-ScheduledTaskAction { [pscustomobject]@{ Execute = $Execute; Arguments = $Argument } }
        Mock New-ScheduledTaskPrincipal { [pscustomobject]@{ UserId = $UserId } }
        Mock New-ScheduledTaskSettingsSet { [pscustomobject]@{} }
        $Out = Invoke-E4B2CleanmgrUserRun -Name 'cleanmgr-previous-user' -Handler 'Previous Installations' -Id 7351 -VolumeCachesKey 'HKLM\X' -TimeoutSeconds 60 -WatchNames @('cleanmgr')
        $Out.Refused | Should -Match 'StateFlags7351'
        $script:Order.Count | Should -Be 0
        Mock Get-E4B2SageFlags { @() }
        $Out = Invoke-E4B2CleanmgrUserRun -Name 'cleanmgr-previous-user' -Handler 'Previous Installations' -Id 7351 -VolumeCachesKey 'HKLM\X' -TimeoutSeconds 60 -WatchNames @('cleanmgr')
        @($script:Order) | Should -Be @('set Previous Installations', 'register DSO-E4B2-cleanmgr-previous-user', 'start', 'unregister DSO-E4B2-cleanmgr-previous-user', 'remove Previous Installations')
        $Out.Error | Should -Match 'zadanie padło'
        Should -Invoke New-ScheduledTaskAction -Scope It -ParameterFilter { $Execute -eq (Join-Path $env:SystemRoot 'System32\cleanmgr.exe') -and $Argument -eq '/sagerun:7351' }
        Should -Invoke New-ScheduledTaskPrincipal -Scope It -ParameterFilter { $UserId -eq 'dso' -and $LogonType -eq 'Interactive' -and $RunLevel -eq 'Highest' }
    }
    It 'Invoke-E4B2CleanmgrRun: brak klucza programu obsługi = odmowa bez uruchamiania' {
        Mock Test-E4B2HandlerKey { $false }
        Mock Get-E4B2SageFlags { @() }
        Mock Set-E4B2SageFlag { throw 'nie wolno' }
        Mock Invoke-E4B2Tool { throw 'nie wolno' }
        $Out = Invoke-E4B2CleanmgrRun -Name 'cleanmgr-prev' -Handler 'Previous Installations' -Id 7351 -VolumeCachesKey 'HKLM\X' -TimeoutSeconds 60 -WatchNames @('cleanmgr')
        $Out.Refused | Should -Match 'Previous Installations'
        Should -Invoke Set-E4B2SageFlag -Times 0 -Exactly -Scope It
    }
    It 'Invoke-E4B2WindowsOldProbe: istniejący Windows.old = nic nie tworzy ani nie uruchamia' {
        Mock Test-Path { $true } -ParameterFilter { $LiteralPath -eq 'C:\Windows.old' }
        Mock New-E4B2WindowsOld { throw 'nie wolno' }
        Mock Invoke-E4B2CleanmgrRun { throw 'nie wolno' }
        Mock Get-E4B2SageInventory { [pscustomobject]@{ Handlers = @('Previous Installations'); StateFlags = @() } }
        $Out = Invoke-E4B2WindowsOldProbe -Plan (Get-E4B2Plan)
        $Out.Refused | Should -Match 'już istnieje'
        Should -Invoke New-E4B2WindowsOld -Times 0 -Exactly -Scope It
        Should -Invoke Invoke-E4B2CleanmgrRun -Times 0 -Exactly -Scope It
    }
    It 'Invoke-E4B2WindowsOldProbe: przygotowanie zwykłych plików -> cleanmgr (PS Direct) tylko z „Previous Installations” -> odczyt -> to samo w sesji dso' {
        $script:Order = [System.Collections.Generic.List[string]]::new()
        $script:Exists = $false
        Mock Test-Path { $script:Exists } -ParameterFilter { $LiteralPath -eq 'C:\Windows.old' }
        Mock Get-E4B2SageInventory { [pscustomobject]@{ Handlers = @('Previous Installations', 'Temporary Files'); StateFlags = @() } }
        Mock New-E4B2WindowsOld { $script:Order.Add("create $Path $Megabytes"); $script:Exists = $true; [pscustomobject]@{ Bytes = 200MB; Files = 10; Owner = 'BUILTIN\Administratorzy' } }
        Mock Get-E4B2FolderSize { [pscustomobject]@{ Bytes = 200MB; Files = 10 } }
        Mock Invoke-E4B2CleanmgrRun { $script:Order.Add("cleanmgr $Handler $Id $WatchPath"); $script:Exists = $false; [pscustomobject]@{ Name = $Name; Refused = $null; Error = $null } }
        Mock Invoke-E4B2CleanmgrUserRun { $script:Order.Add("cleanmgr-dso $Handler $Id $WatchPath"); $script:Exists = $false; [pscustomobject]@{ Name = $Name; Refused = $null; Error = $null } }
        $Out = Invoke-E4B2WindowsOldProbe -Plan (Get-E4B2Plan)
        $Id = (Get-E4B2Plan).SageRunId
        @($script:Order) | Should -Be @('create C:\Windows.old 200', "cleanmgr Previous Installations $Id C:\Windows.old", 'create C:\Windows.old 200', "cleanmgr-dso Previous Installations $Id C:\Windows.old")
        $Out.ExistsAfter | Should -BeFalse
        $Out.Recognized | Should -BeTrue
        $Out.User.ExistsAfter | Should -BeFalse
    }
    It 'Invoke-E4B2NetworkProbe: DISM ze źródłem WU, potem usuwanie pamięci DO na prawdziwych plikach, potem Download (bez plików testowych)' {
        $script:Order = [System.Collections.Generic.List[string]]::new()
        Mock Resolve-DnsName { [pscustomobject]@{ Type = 'A'; IPAddress = '1.2.3.4' } }
        Mock Invoke-E4B2Tool { $script:Order.Add("tool $Arguments"); [pscustomobject]@{ Name = $Name } }
        Mock Get-DeliveryOptimizationStatus { @() }
        Mock Invoke-E4B2DeliveryOptimizationProbe { $script:Order.Add('do'); [pscustomobject]@{} }
        Mock Invoke-E4B2DownloadProbe { $script:Order.Add("download $TestMegabytes"); [pscustomobject]@{} }
        $Out = Invoke-E4B2NetworkProbe -Plan (Get-E4B2Plan)
        @($script:Order) | Should -Be @('tool /Online /Cleanup-Image /RestoreHealth', 'do', 'download 0')
        @($Out.Errors).Count | Should -Be 0
    }
    It 'Remove-E4B2OldFiles: stare usunięte, młode i zablokowane zostają i są liczone, junction usunięty jako link bez wchodzenia, stary pusty folder usunięty' {
        $Root = Join-Path $TestDrive 'temp'
        $Guard = Join-Path $TestDrive 'guard'
        $Sub = Join-Path $Root 'sub'
        $null = [System.IO.Directory]::CreateDirectory($Sub)
        $null = [System.IO.Directory]::CreateDirectory($Guard)
        $Old = (Get-Date).ToUniversalTime().AddDays(-3)
        foreach ($Name in 'old.bin', 'young.bin', 'locked.bin') { [System.IO.File]::WriteAllBytes((Join-Path $Root $Name), [byte[]](1, 2, 3)) }
        [System.IO.File]::WriteAllBytes((Join-Path $Sub 'old.bin'), [byte[]](1, 2))
        [System.IO.File]::WriteAllBytes((Join-Path $Guard 'strażnik.txt'), [byte[]](1))
        foreach ($Path in (Join-Path $Root 'old.bin'), (Join-Path $Root 'locked.bin'), (Join-Path $Sub 'old.bin'), (Join-Path $Guard 'strażnik.txt')) { [System.IO.File]::SetLastWriteTimeUtc($Path, $Old) }
        [System.IO.Directory]::SetLastWriteTimeUtc($Sub, $Old)
        Microsoft.PowerShell.Management\New-Item -ItemType Junction -Path (Join-Path $Root 'link') -Value $Guard | Out-Null
        $Lock = [System.IO.File]::Open((Join-Path $Root 'locked.bin'), 'Open', 'ReadWrite', 'None')
        try { $Out = Remove-E4B2OldFiles -Root $Root -CutoffUtc (Get-Date).ToUniversalTime().AddDays(-1) }
        finally { $Lock.Dispose() }
        $Out.Deleted | Should -Be 2
        $Out.SkippedYoung | Should -Be 1
        $Out.SkippedLocked | Should -Be 1
        $Out.LinksRemoved | Should -Be 1
        $Out.DirsDeleted | Should -Be 1
        $Out.FreedBytes | Should -Be 5
        Test-Path -LiteralPath (Join-Path $Root 'old.bin') | Should -BeFalse
        Test-Path -LiteralPath (Join-Path $Root 'young.bin') | Should -BeTrue
        Test-Path -LiteralPath $Sub | Should -BeFalse
        Test-Path -LiteralPath (Join-Path $Guard 'strażnik.txt') | Should -BeTrue
        Test-Path -LiteralPath (Join-Path $Root 'link') | Should -BeFalse
        Test-Path -LiteralPath $Root | Should -BeTrue
    }
    It 'Remove-E4B2OldFiles: brak katalogu = zera' {
        $Out = Remove-E4B2OldFiles -Root (Join-Path $TestDrive 'brak') -CutoffUtc (Get-Date).ToUniversalTime()
        $Out.Deleted | Should -Be 0
        $Out.RootExists | Should -BeFalse
    }
    It 'New-E4B2TempSet + Get-E4B2TempSetState + Close-E4B2TempSet: pliki w wieku z planu, blokada, junction do strażnika' {
        $Root = Join-Path $TestDrive 'settemp'
        $null = [System.IO.Directory]::CreateDirectory($Root)
        $Set = New-E4B2TempSet -Root $Root -Label 'sys' -GuardRoot (Join-Path $TestDrive 'guards') -OldAgeDays 2 -VeryOldAgeDays 10 -FileBytes 1024
        try {
            $State = Get-E4B2TempSetState -Set $Set
            foreach ($Item in $State) { $Item.Exists | Should -BeTrue -Because $Item.Kind }
            @($State | ForEach-Object { $_.Kind }) | Should -Be @('old', 'veryold', 'fresh', 'locked', 'subold', 'link', 'sentinel')
            $OldItem = @($Set.Items | Where-Object { $_.Kind -eq 'old' })[0]
            ([System.IO.File]::GetLastWriteTimeUtc($OldItem.Path) -lt (Get-Date).ToUniversalTime().AddDays(-1.9)) | Should -BeTrue
            { [System.IO.File]::Open(@($Set.Items | Where-Object { $_.Kind -eq 'locked' })[0].Path, 'Open', 'Read', 'None').Dispose() } | Should -Throw
        }
        finally { Close-E4B2TempSet -Set $Set }
        $Lock = @($Set.Items | Where-Object { $_.Kind -eq 'locked' })[0].Path
        { [System.IO.File]::Open($Lock, 'Open', 'Read', 'None').Dispose() } | Should -Not -Throw
    }
    It 'New-E4B2WindowsOld: zwykłe pliki o zadanym rozmiarze, bez zmiany właściciela; istniejący folder = odmowa' {
        $Path = Join-Path $TestDrive 'Windows.old'
        $Out = New-E4B2WindowsOld -Path $Path -Megabytes 2
        $Out.Bytes | Should -BeGreaterOrEqual 2MB
        $Out.Files | Should -BeGreaterThan 1
        Test-Path -LiteralPath (Join-Path $Path 'Windows\System32') | Should -BeTrue
        $Out.Owner | Should -Be ((Get-Acl -LiteralPath $TestDrive).Owner)
        { New-E4B2WindowsOld -Path $Path -Megabytes 1 } | Should -Throw '*istnieje*'
    }
}

Describe 'Wysyłka funkcji do VM i treść skryptu' {
    It 'Get-E4B2RemoteScript: poprawny, definiuje wszystkie funkcje zdalne; nie woła niczego spoza listy; funkcje hosta nie są wysyłane' {
        $Text = Get-E4B2RemoteScript
        $Errors = $null
        $Ast = [System.Management.Automation.Language.Parser]::ParseInput($Text, [ref]$null, [ref]$Errors)
        @($Errors).Count | Should -Be 0
        $Sent = @(Get-ExploreCommonRemoteFunctionNames) + @(Get-E4B2RemoteFunctionNames)
        foreach ($Name in $Sent) { $Text | Should -Match "function $([regex]::Escape($Name)) \{" }
        $Called = $Ast.FindAll({ param($n) $n -is [System.Management.Automation.Language.CommandAst] }, $true) | ForEach-Object { $_.GetCommandName() } | Where-Object { $_ -like '*-E4B2*' -or $_ -like '*-Explore*' } | Sort-Object -Unique
        foreach ($Name in $Called) { $Sent | Should -Contain $Name }
        foreach ($HostOnly in (Get-E4B2HostOnlyFunctionNames)) { $Text | Should -Not -Match "function $([regex]::Escape($HostOnly)) \{" }
        $Text | Should -Not -Match 'Restore-VmCleanState|Wait-VmReady|Get-DsoVmCredential|Start-VM|Connect-VMNetworkAdapter'
    }
    It 'każda funkcja skryptu jest wysyłana albo na liście hosta; polecenia ze skutkiem tylko w swoich funkcjach; zakres R8 (bez ACL i właściciela)' {
        $Ast = [System.Management.Automation.Language.Parser]::ParseFile((Resolve-Path $script:E4B2ScriptPath), [ref]$null, [ref]$null)
        $Defined = @($Ast.FindAll({ param($n) $n -is [System.Management.Automation.Language.FunctionDefinitionAst] }, $true) | ForEach-Object { $_.Name })
        foreach ($Name in $Defined) { ((Get-E4B2RemoteFunctionNames) + (Get-E4B2HostOnlyFunctionNames)) | Should -Contain $Name }
        $Commands = @($Ast.FindAll({ param($n) $n -is [System.Management.Automation.Language.CommandAst] }, $true))
        $Allowed = @{
            'Delete-DeliveryOptimizationCache' = @('Invoke-E4B2DeliveryOptimizationProbe')
            'Stop-Service'                     = @('Stop-E4B2Service')
            'Start-Service'                    = @('Start-E4B2Service')
            'Start-VM'                         = @('Invoke-E4B2VmRun')
            'Restore-VmCleanState'             = @('Invoke-E4B2VmRun', 'Complete-E4B2VmCleanup')
            'Connect-VMNetworkAdapter'         = @('Invoke-E4B2VmRun')
            'Disconnect-VMNetworkAdapter'      = @('Invoke-E4B2VmRun', 'Complete-E4B2VmCleanup')
            'Start-E4B2Tool'                   = @('Invoke-E4B2Tool')
            'Register-ScheduledTask'           = @('Invoke-E4B2CleanmgrUserRun')
            'Unregister-ScheduledTask'         = @('Invoke-E4B2CleanmgrUserRun')
        }
        foreach ($Name in $Allowed.Keys) {
            foreach ($Command in ($Commands | Where-Object { $_.GetCommandName() -eq $Name })) { $Allowed[$Name] | Should -Contain (Get-CommandOwner $Command) -Because $Name }
        }
        foreach ($Command in ($Commands | Where-Object { $_.GetCommandName() -eq 'Connect-VMNetworkAdapter' })) { $Command.Extent.Text | Should -Match "-SwitchName 'Default Switch'" }
        $Names = @($Commands | ForEach-Object { $_.GetCommandName() } | Where-Object { $_ })
        foreach ($Forbidden in 'Set-ItemProperty', 'New-ItemProperty', 'Remove-ItemProperty', 'reg', 'reg.exe', 'dism', 'dism.exe', 'sfc', 'sfc.exe', 'cleanmgr', 'cleanmgr.exe', 'takeown', 'takeown.exe',
            'icacls', 'icacls.exe', 'Set-Acl', 'Stop-Computer', 'Restart-Computer', 'Invoke-Expression', 'iex', 'Invoke-RestMethod', 'Invoke-WebRequest', 'Stop-VM', 'Remove-VM', 'Restore-VMCheckpoint',
            'Set-VMNetworkAdapter', 'cmd', 'cmd.exe', 'Start-Process', 'Set-Service', 'Repair-WindowsImage', 'Add-Type') {
            $Names | Should -Not -Contain $Forbidden
        }
        $Text = [System.IO.File]::ReadAllText((Resolve-Path $script:E4B2ScriptPath), [System.Text.Encoding]::UTF8)
        $Text | Should -Not -Match 'HKCU:|HKEY_CURRENT_USER|EncodedCommand|/ResetBase|takeown|icacls|SetAccessControl|SetOwner|SetNamedSecurityInfo|SeTakeOwnershipPrivilege|SeRestorePrivilege|calc\.exe'
    }
    It 'plik ma BOM UTF-8 oraz sekcje Instrukcja uruchomienia i Opis działania; VmName tylko Home/Pro' {
        $Bytes = [System.IO.File]::ReadAllBytes((Resolve-Path $script:E4B2ScriptPath))
        ($Bytes[0..2] -join ',') | Should -Be '239,187,191'
        $Text = [System.IO.File]::ReadAllText((Resolve-Path $script:E4B2ScriptPath), [System.Text.Encoding]::UTF8)
        $Text.Substring(0, 400) | Should -Match 'Instrukcja uruchomienia'
        $Text | Should -Match 'Opis działania'
        foreach ($Command in (Get-Command $script:E4B2ScriptPath), (Get-Command Invoke-E4B2VmRun), (Get-Command Complete-E4B2VmCleanup)) {
            $Attr = $Command.Parameters['VmName'].Attributes | Where-Object { $_ -is [System.Management.Automation.ValidateSetAttribute] }
            $Attr.ValidValues | Should -Be @('DSO-26H2-Home', 'DSO-26H2-Pro')
        }
    }
    It 'plik testów też ma BOM UTF-8' {
        $Bytes = [System.IO.File]::ReadAllBytes((Join-Path $PSScriptRoot 'E4B2Exploration.Tests.ps1'))
        ($Bytes[0..2] -join ',') | Should -Be '239,187,191'
    }
}

Describe 'Argumenty przebiegu' {
    It 'Assert-E4B2OutDirectory: wymagany, bezwzględny, poza repo; w repo tylko ignorowane przez git' {
        $Repo = 'C:\Repo\DSOptimizer'
        { Assert-E4B2OutDirectory -Path '' -RepoRoot $Repo } | Should -Throw '*wymagany*'
        { Assert-E4B2OutDirectory -Path 'wyniki' -RepoRoot $Repo } | Should -Throw '*bezwzględn*'
        { Assert-E4B2OutDirectory -Path 'C:\Repo\DSOptimizer\tests\vm\out\x' -RepoRoot $Repo } | Should -Throw '*w repo*'
        { Assert-E4B2OutDirectory -Path 'C:\Repo\DSOptimizer\wyniki\pomiar\exploration' -RepoRoot $Repo } | Should -Throw '*w repo*'
        $Real = (Resolve-Path (Join-Path $PSScriptRoot '..\..\..')).ProviderPath   # prawdziwe repo: git check-ignore
        Assert-E4B2OutDirectory -Path "$Real\tests\vm\out\x" -RepoRoot $Real | Should -Be "$Real\tests\vm\out\x"   # ignorowany przez git
        { Assert-E4B2OutDirectory -Path "$Real\src\x" -RepoRoot $Real } | Should -Throw '*ignorowanym przez git*'   # nieignorowany
        Assert-E4B2OutDirectory -Path (Join-Path $env:TEMP 'dso-explore') -RepoRoot $Real | Should -Be (Join-Path $env:TEMP 'dso-explore')   # poza repo
    }
    It 'Get-E4B2RunArguments: VM, fazy w kolejności i plan' {
        $Arguments = Get-E4B2RunArguments -VmName 'DSO-26H2-Pro' -Phase @('network', 'sfc') -OutDirectory 'C:\w' -CheckpointName 'c' -ReadyTimeoutSeconds 60
        @($Arguments.VmName) | Should -Be @('DSO-26H2-Pro')
        @($Arguments.Phases) | Should -Be @('baseline', 'sfc', 'network')
        $Arguments.Plan.SageRunId | Should -Be (Get-E4B2Plan).SageRunId
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
        Mock Get-E4B2RemoteScript { 'function X {}' }
        Mock Invoke-E4B2VmPhase { $script:Calls.Add("phase $Phase"); [pscustomobject]@{ phase = $Phase } }
        Mock Copy-E4B2RawFiles -RemoveParameterValidation 'Session' { $script:Calls.Add('copy raw'); @() }
        Mock Connect-VMNetworkAdapter { $script:Calls.Add("connect $VMName $SwitchName") }
        Mock Disconnect-VMNetworkAdapter { $script:Calls.Add("disconnect $VMName") }
        Mock Remove-PSSession -RemoveParameterValidation 'Session' { }
        Mock Save-ExploreResult { $script:Calls.Add("save $FileName"); $script:SavedResult = $Result; "C:\w\$VmName\$FileName" }
        Mock Get-VM { [pscustomobject]@{ Name = $Name; State = 'Off' } }
        Mock Get-VMNetworkAdapter { [pscustomobject]@{ VMName = $VMName; Name = 'Network Adapter'; SwitchName = $null } }
        $script:SavedResult = $null
    }
    It 'przebieg: revert -> start -> gotowość -> fazy, sieć tylko na fazę network -> kopia surowych plików -> revert -> odłączenie -> zapis' {
        $Phases = Get-E4B2PhaseOrder -Phase @('dism', 'network', 'final')
        $Path = Invoke-E4B2VmRun -VmName 'DSO-26H2-Pro' -OutDirectory $TestDrive -Phases $Phases 6>$null
        $Path | Should -Be 'C:\w\DSO-26H2-Pro\e4b2.json'
        @($script:Calls) | Should -Be @('revert DSO-26H2-Pro', 'start DSO-26H2-Pro', 'ready', 'phase baseline', 'phase dism', 'connect DSO-26H2-Pro Default Switch', 'phase network',
            'disconnect DSO-26H2-Pro', 'phase final', 'copy raw', 'revert DSO-26H2-Pro', 'disconnect DSO-26H2-Pro', 'save e4b2.json')
        $script:SavedResult.cleanup.Ok | Should -BeTrue
        $script:SavedResult.userSid | Should -Be 'S-1-5-21-1-1001'
        $script:SavedResult.error | Should -BeNullOrEmpty
    }
    It 'błąd fazy network: karta odłączona od razu, dalsze fazy idą, wynik częściowy zapisany, wyjątek z nazwą VM' {
        Mock Invoke-E4B2VmPhase { $script:Calls.Add("phase $Phase"); if ($Phase -eq 'network') { throw 'DISM padł' }; [pscustomobject]@{ phase = $Phase } }
        { Invoke-E4B2VmRun -VmName 'DSO-26H2-Home' -OutDirectory $TestDrive -Phases @('baseline', 'network', 'final') 6>$null } | Should -Throw '*DSO-26H2-Home*DISM padł*'
        $script:Calls.IndexOf('disconnect DSO-26H2-Home') | Should -BeLessThan $script:Calls.IndexOf('phase final')
        $script:Calls[-1] | Should -Be 'save e4b2.json'
        $script:SavedResult.phases.baseline.phase | Should -Be 'baseline'
        $script:SavedResult.phases.final.phase | Should -Be 'final'
    }
    It 'stan końcowy niezgodny z kontraktem (VM nie Off, karta na przełączniku): zapisany i zgłoszony' {
        Mock Get-VM { [pscustomobject]@{ Name = $Name; State = 'Running' } }
        Mock Get-VMNetworkAdapter { [pscustomobject]@{ VMName = $VMName; Name = 'Network Adapter'; SwitchName = 'Default Switch' } }
        { Invoke-E4B2VmRun -VmName 'DSO-26H2-Home' -OutDirectory $TestDrive -Phases @('baseline') 3>$null 6>$null } | Should -Throw '*stan końcowy*'
        ($script:SavedResult.cleanup.Violations -join ' ') | Should -Match 'Running'
        ($script:SavedResult.cleanup.Violations -join ' ') | Should -Match 'Default Switch'
    }
    It 'błąd uwierzytelnienia: przekazany dalej, revert wykonany; Invoke-E4B2Exploration zatrzymuje całość' {
        Mock Wait-VmReady { $E = [System.InvalidOperationException]::new('błąd uwierzytelnienia PowerShell Direct'); $E.Data['VmAuthFailure'] = $true; throw $E }
        { Invoke-E4B2VmRun -VmName 'DSO-26H2-Pro' -OutDirectory $TestDrive -Phases @('baseline') } | Should -Throw '*uwierzytelnienia*'
        $script:Calls | Should -Contain 'revert DSO-26H2-Pro'
        $Arguments = Get-E4B2RunArguments -VmName @('DSO-26H2-Home', 'DSO-26H2-Pro') -Phase @('baseline') -OutDirectory 'C:\w' -CheckpointName 'c' -ReadyTimeoutSeconds 60
        Mock Invoke-E4B2VmRun { $E = [System.InvalidOperationException]::new('x'); $E.Data['VmAuthFailure'] = $true; throw $E }
        { Invoke-E4B2Exploration -Arguments $Arguments 6>$null } | Should -Throw
        Should -Invoke Invoke-E4B2VmRun -Times 1 -Exactly -Scope It
    }
}

Describe 'Blok główny SKRYPTU (-ShowPlan; bez VM)' {
    It '-ShowPlan: kod 0, plan i fazy; bez -OutDirectory kod 1' {
        $env:DSO_VM_TESTS_MOCK_ONLY | Should -Be '1'
        $Output = & $script:E4B2ScriptPath -ShowPlan -VmName DSO-26H2-Pro -Phase sfc -NoNetwork -OutDirectory (Join-Path $TestDrive 'w') 6>$null
        $LASTEXITCODE | Should -Be 0
        $Plan = ($Output | Out-String) | ConvertFrom-Json
        @($Plan.VmName) | Should -Be @('DSO-26H2-Pro')
        @($Plan.Phases) | Should -Be @('baseline', 'sfc')
        $Plan.Network | Should -BeFalse
        Test-Path -LiteralPath (Join-Path $TestDrive 'w') | Should -BeFalse
        & $script:E4B2ScriptPath -ShowPlan 6>$null | Out-Null
        $LASTEXITCODE | Should -Be 1
    }
}

AfterAll { Clear-NoLiveVm }
