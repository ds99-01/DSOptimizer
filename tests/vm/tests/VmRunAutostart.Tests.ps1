BeforeAll {
    # Wyłącznik: żaden test na mockach nie może dotknąć prawdziwego Hyper-V (patrz Support\NoLiveVm.ps1).
    . (Join-Path $PSScriptRoot 'Support\NoLiveVm.ps1')

    Import-Module (Join-Path $PSScriptRoot '..\modules\VmCommon.psm1') -Force
    Import-Module (Join-Path $PSScriptRoot '..\modules\VmRun.psm1') -Force
    Import-Module (Join-Path $PSScriptRoot '..\modules\VmHost.psm1') -Force
    $script:VmRoot = Join-Path $PSScriptRoot '..'
    $script:CatalogPath = Join-Path $script:VmRoot 'catalog\vm-test-catalog.json'

    function Write-CasesFile {
        param([string]$Path, [string]$CaseBody)
        Set-Content -LiteralPath $Path -Value "@{ Cases = @( @{ $CaseBody } ) }" -Encoding UTF8
    }
    $script:ListCase = "Name = 'lista'; Flow = 'Autostart'; RunAs = 'dso'; Check = '`$true'; Expect = @{ Listed = @{ 'run:machine:SecurityHealth' = @{ State = 'Enabled'; CanToggle = `$false; Verdict = 'Protected' } } }"
    $script:SetItems = "Items = @(@{ Id = 'run:user:DsoRunUser'; Probe = 'DsoRunUser' }); Controls = @('DsoRunUserCtl')"
    $script:SetExpect = "SetExit = 0; SetOutcome = 'Applied'; RestoreExit = 0; RestoreOutcome = 'Undone'"
    $script:SetCase = "Name = 'wyl'; Flow = 'Autostart'; RunAs = 'dso'; $script:SetItems; Set = 'disabled'; Restore = 'undo'; Check = '`$true'; Expect = @{ $script:SetExpect }"

    function New-DsoAutostartCase {
        param([hashtable]$Extra = @{})
        $Case = @{
            Name = 'wyl'; Flow = 'Autostart'; RunAs = 'dso'; Check = '$true'
            Items = @(@{ Id = 'run:user:DsoRunUser'; Probe = 'DsoRunUser' }, @{ Id = 'appx:Microsoft.WindowsTerminal_8wekyb3d8bbwe!StartTerminalOnLoginTask'; Process = 'WindowsTerminal' })
            Controls = @('DsoRunUserCtl'); Set = 'disabled'; Restore = 'undo'
            Expect = @{ SetExit = 0; SetOutcome = 'Applied'; RestoreExit = 0; RestoreOutcome = 'Undone' }
        }
        foreach ($Key in $Extra.Keys) { $Case[$Key] = $Extra[$Key] }
        return $Case
    }
}

Describe 'Import-VmCases: Flow Autostart' {
    It 'przyjmuje przypadek z samą listą (bez Set)' {
        Write-CasesFile -Path "$TestDrive\l1.psd1" -CaseBody $script:ListCase
        $Cases = Import-VmCases -CasesPath "$TestDrive\l1.psd1" -CatalogPath $script:CatalogPath
        @($Cases).Count | Should -Be 1
        $Cases[0].Flow | Should -Be 'Autostart'
    }
    It 'przyjmuje przypadek z Set, Restore, Items, Controls, ListDoesNotWrite i MeasuredAfter' {
        Write-CasesFile -Path "$TestDrive\l2.psd1" -CaseBody ($script:SetCase.Replace('Expect = @{', "Initial = 'enabled'; Expect = @{ ListDoesNotWrite = @('HKU\{UserSid}\Software\X'); MeasuredAfter = @('run:user:DsoRunUserCtl'); "))
        { Import-VmCases -CasesPath "$TestDrive\l2.psd1" -CatalogPath $script:CatalogPath } | Should -Not -Throw
    }
    It 'przyjmuje CheckAfterUndo przy Set' {
        Write-CasesFile -Path "$TestDrive\l4.psd1" -CaseBody ($script:SetCase.Replace('Check = ', "CheckAfterUndo = '1'; Check = "))
        $Cases = Import-VmCases -CasesPath "$TestDrive\l4.psd1" -CatalogPath $script:CatalogPath
        $Cases[0].CheckAfterUndo | Should -Be '1'
    }
    It 'przyjmuje pozycję z Process zamiast Probe' {
        Write-CasesFile -Path "$TestDrive\l3.psd1" -CaseBody $script:SetCase.Replace("Probe = 'DsoRunUser'", "Process = 'WindowsTerminal'")
        { Import-VmCases -CasesPath "$TestDrive\l3.psd1" -CatalogPath $script:CatalogPath } | Should -Not -Throw
    }
    It 'odrzuca <Opis>' -ForEach @(
        @{ Opis = 'brak Check'; Body = "Name = 'x'; Flow = 'Autostart'; Expect = @{}"; Fragment = '*Check*' }
        @{ Opis = 'id ze spacją'; Body = "Name = 'x'; Flow = 'Autostart'; Items = @(@{ Id = 'run:user:Dso Run'; Probe = 'DsoRunUser' }); Controls = @('DsoRunUserCtl'); Set = 'disabled'; Restore = 'undo'; Check = '1'; Expect = @{ SetExit = 0; SetOutcome = 'Applied'; RestoreExit = 0; RestoreOutcome = 'Undone' }"; Fragment = '*spac*' }
        @{ Opis = 'Probe i Process naraz'; Body = "Name = 'x'; Flow = 'Autostart'; Items = @(@{ Id = 'run:user:DsoRunUser'; Probe = 'DsoRunUser'; Process = 'DsoRunUser' }); Controls = @('DsoRunUserCtl'); Set = 'disabled'; Restore = 'undo'; Check = '1'; Expect = @{ SetExit = 0; SetOutcome = 'Applied'; RestoreExit = 0; RestoreOutcome = 'Undone' }"; Fragment = '*Probe*Process*' }
        @{ Opis = 'pozycja bez Probe i Process'; Body = "Name = 'x'; Flow = 'Autostart'; Items = @(@{ Id = 'run:user:DsoRunUser' }); Controls = @('DsoRunUserCtl'); Set = 'disabled'; Restore = 'undo'; Check = '1'; Expect = @{ SetExit = 0; SetOutcome = 'Applied'; RestoreExit = 0; RestoreOutcome = 'Undone' }"; Fragment = '*Probe*Process*' }
        @{ Opis = 'Set bez Restore'; Body = "Name = 'x'; Flow = 'Autostart'; Items = @(@{ Id = 'run:user:DsoRunUser'; Probe = 'DsoRunUser' }); Controls = @('DsoRunUserCtl'); Set = 'disabled'; Check = '1'; Expect = @{ SetExit = 0; SetOutcome = 'Applied'; RestoreExit = 0; RestoreOutcome = 'Undone' }"; Fragment = '*Restore*' }
        @{ Opis = 'CheckAfterUndo bez Set'; Body = "Name = 'x'; Flow = 'Autostart'; CheckAfterUndo = '1'; Check = '1'; Expect = @{}"; Fragment = '*CheckAfterUndo*Set*' }
        @{ Opis = 'pusty CheckAfterUndo'; Body = "Name = 'x'; Flow = 'Autostart'; Items = @(@{ Id = 'run:user:DsoRunUser'; Probe = 'DsoRunUser' }); Controls = @('DsoRunUserCtl'); Set = 'disabled'; Restore = 'undo'; CheckAfterUndo = ' '; Check = '1'; Expect = @{ SetExit = 0; SetOutcome = 'Applied'; RestoreExit = 0; RestoreOutcome = 'Undone' }"; Fragment = '*CheckAfterUndo*' }
        @{ Opis = 'Restore bez Set'; Body = "Name = 'x'; Flow = 'Autostart'; Restore = 'undo'; Check = '1'; Expect = @{}"; Fragment = '*Set*' }
        @{ Opis = 'Set bez Items'; Body = "Name = 'x'; Flow = 'Autostart'; Controls = @('DsoRunUserCtl'); Set = 'disabled'; Restore = 'undo'; Check = '1'; Expect = @{ SetExit = 0; SetOutcome = 'Applied'; RestoreExit = 0; RestoreOutcome = 'Undone' }"; Fragment = '*Items*' }
        @{ Opis = 'Set bez Controls'; Body = "Name = 'x'; Flow = 'Autostart'; Items = @(@{ Id = 'run:user:DsoRunUser'; Probe = 'DsoRunUser' }); Set = 'disabled'; Restore = 'undo'; Check = '1'; Expect = @{ SetExit = 0; SetOutcome = 'Applied'; RestoreExit = 0; RestoreOutcome = 'Undone' }"; Fragment = '*Controls*' }
        @{ Opis = 'nieznany stan Set'; Body = "Name = 'x'; Flow = 'Autostart'; Items = @(@{ Id = 'run:user:DsoRunUser'; Probe = 'DsoRunUser' }); Controls = @('DsoRunUserCtl'); Set = 'off'; Restore = 'undo'; Check = '1'; Expect = @{ SetExit = 0; SetOutcome = 'Applied'; RestoreExit = 0; RestoreOutcome = 'Undone' }"; Fragment = '*Set*' }
        @{ Opis = 'nieznany klucz Expect'; Body = "Name = 'x'; Flow = 'Autostart'; Check = '1'; Expect = @{ ApplyExit = 0 }"; Fragment = '*ApplyExit*' }
        @{ Opis = 'klucz Expect wyniku bez Set'; Body = "Name = 'x'; Flow = 'Autostart'; Check = '1'; Expect = @{ SetExit = 0 }"; Fragment = '*SetExit*' }
        @{ Opis = 'Set bez wymaganych kluczy wyniku w Expect'; Body = "Name = 'x'; Flow = 'Autostart'; Items = @(@{ Id = 'run:user:DsoRunUser'; Probe = 'DsoRunUser' }); Controls = @('DsoRunUserCtl'); Set = 'disabled'; Restore = 'undo'; Check = '1'; Expect = @{ SetExit = 0 }"; Fragment = '*SetOutcome*' }
        @{ Opis = 'Listed bez CanToggle'; Body = "Name = 'x'; Flow = 'Autostart'; Check = '1'; Expect = @{ Listed = @{ 'run:user:DsoMissing' = @{ State = 'Enabled' } } }"; Fragment = '*CanToggle*' }
        @{ Opis = 'Listed z nieznanym polem'; Body = "Name = 'x'; Flow = 'Autostart'; Check = '1'; Expect = @{ Listed = @{ 'run:user:DsoMissing' = @{ State = 'Enabled'; CanToggle = `$true; Group = 'x' } } }"; Fragment = '*Group*' }
        @{ Opis = 'ListDoesNotWrite z HKCU'; Body = "Name = 'x'; Flow = 'Autostart'; Check = '1'; Expect = @{ ListDoesNotWrite = @('HKCU\Software\X') }"; Fragment = '*HKU*' }
        @{ Opis = 'klucz TweakId'; Body = "Name = 'x'; Flow = 'Autostart'; TweakId = 'vmtest.hklm-value'; Check = '1'; Expect = @{}"; Fragment = '*TweakId*' }
        @{ Opis = 'Set we Flow Full'; Body = "Name = 'x'; TweakId = 'vmtest.hklm-value'; Flow = 'Full'; RestorePoint = 'skip'; Set = 'disabled'; Expect = @{ Before = 'NotApplied'; ApplyExit = 0; ApplyOutcome = 'Applied'; AfterApply = 'Applied'; UndoExit = 0; UndoOutcome = 'Undone'; AfterUndo = 'NotApplied' }"; Fragment = '*Set*Autostart*' }
    ) {
        Write-CasesFile -Path "$TestDrive\bad.psd1" -CaseBody $Body
        { Import-VmCases -CasesPath "$TestDrive\bad.psd1" -CatalogPath $script:CatalogPath } | Should -Throw -ExpectedMessage $Fragment
    }
    It 'prawdziwy cases.psd1 ma pięć przypadków autostartu, każdy z RunAs dso poza autostart-enable-admin' {
        $Cases = Import-VmCases -CasesPath (Join-Path $script:VmRoot 'cases.psd1') -CatalogPath $script:CatalogPath
        $Auto = @($Cases | Where-Object { $_.Flow -eq 'Autostart' })
        @($Auto.Name | Sort-Object) | Should -Be @('autostart-enable-admin', 'autostart-five-sources-undo-dso', 'autostart-list-clean-dso', 'autostart-taskmgr-state-dso', 'autostart-terminal-dso')
        # Terminal: odczyt klucza zaraz po cofnięciu, zanim Windows po logowaniu odtworzyłby go sam z tymi samymi wartościami.
        @($Auto | Where-Object { $_.Name -eq 'autostart-terminal-dso' })[0].ContainsKey('CheckAfterUndo') | Should -BeTrue
        foreach ($Case in $Auto) {
            if ($Case.Name -eq 'autostart-enable-admin') { $Case.ContainsKey('RunAs') | Should -BeFalse }
            else { $Case.RunAs | Should -Be 'dso' }
        }
    }
}

Describe 'Split-VmCaseRuns: Flow Autostart' {
    It 'z Set = osobny przebieg autostart-<nazwa>, bez Set = main' {
        $Plan = Split-VmCaseRuns -Cases @(
            @{ Name = 'lista'; Flow = 'Autostart'; Check = '1'; Expect = @{} },
            (New-DsoAutostartCase -Extra @{ Name = 'wyl' })
        )
        @($Plan.Label) | Should -Be @('main', 'autostart-wyl')
        @($Plan[0].Cases).Count | Should -Be 1
        $Plan[0].Cases[0].Name | Should -Be 'lista'
    }
    It 'z Set i Skip = przebieg main (rekord pominięcia, bez restartu)' {
        $Plan = Split-VmCaseRuns -Cases @((New-DsoAutostartCase -Extra @{ Skip = 'powód' }))
        @($Plan.Label) | Should -Be @('skipped')
    }
}

Describe 'ConvertTo-DsoCliArguments: polecenia autostartu' {
    It 'set-autostart: id i stan, bez --catalog i --restore-point' {
        ConvertTo-DsoCliArguments -Command set-autostart -TweakId 'run:user:DsoRunUser' -AutostartState disabled -OutPath 'C:\dso\out\a.json' |
            Should -Be @('--set-autostart', 'run:user:DsoRunUser', 'disabled', '--out', 'C:\dso\out\a.json')
    }
    It 'list-autostart: bez id' {
        ConvertTo-DsoCliArguments -Command list-autostart -OutPath 'C:\dso\out\a.json' | Should -Be @('--list-autostart', '--out', 'C:\dso\out\a.json')
    }
    It 'undo-autostart: samo id' {
        ConvertTo-DsoCliArguments -Command undo-autostart -TweakId 'appx:Microsoft.WindowsTerminal_8wekyb3d8bbwe!StartTerminalOnLoginTask' -OutPath 'C:\dso\out\a.json' |
            Should -Be @('--undo-autostart', 'appx:Microsoft.WindowsTerminal_8wekyb3d8bbwe!StartTerminalOnLoginTask', '--out', 'C:\dso\out\a.json')
    }
    It 'odrzuca <Opis>' -ForEach @(
        @{ Opis = 'set-autostart bez stanu'; Params = @{ Command = 'set-autostart'; TweakId = 'run:user:DsoRunUser'; OutPath = 'C:\o.json' } }
        @{ Opis = 'set-autostart bez id'; Params = @{ Command = 'set-autostart'; AutostartState = 'enabled'; OutPath = 'C:\o.json' } }
        @{ Opis = 'id ze spacją'; Params = @{ Command = 'undo-autostart'; TweakId = 'run:user:Dso Run'; OutPath = 'C:\o.json' } }
        @{ Opis = 'katalog tweaków'; Params = @{ Command = 'list-autostart'; CatalogPath = 'C:\k.json'; OutPath = 'C:\o.json' } }
        @{ Opis = 'punkt przywracania'; Params = @{ Command = 'set-autostart'; TweakId = 'run:user:DsoRunUser'; AutostartState = 'enabled'; RestorePoint = 'skip'; OutPath = 'C:\o.json' } }
        @{ Opis = 'stan przy undo-autostart'; Params = @{ Command = 'undo-autostart'; TweakId = 'run:user:DsoRunUser'; AutostartState = 'enabled'; OutPath = 'C:\o.json' } }
        @{ Opis = 'stan przy apply'; Params = @{ Command = 'apply'; TweakId = 'vmtest.hklm-value'; AutostartState = 'enabled'; OutPath = 'C:\o.json' } }
        @{ Opis = 'id przy list-autostart'; Params = @{ Command = 'list-autostart'; TweakId = 'run:user:DsoRunUser'; OutPath = 'C:\o.json' } }
    ) {
        { ConvertTo-DsoCliArguments @Params } | Should -Throw
    }
}

Describe 'Get-VmAutostartExpectedStart' {
    It '<Opis>' -ForEach @(
        @{ Opis = 'Set disabled = brak startu po pierwszym restarcie'; Case = @{ Set = 'disabled'; Restore = 'undo' }; Phase = 'Set'; Want = $false }
        @{ Opis = 'Set enabled = start po pierwszym restarcie'; Case = @{ Set = 'enabled'; Restore = 'undo'; Initial = 'disabled' }; Phase = 'Set'; Want = $true }
        @{ Opis = 'undo bez Initial = stan domyślny enabled (start)'; Case = @{ Set = 'disabled'; Restore = 'undo' }; Phase = 'Restore'; Want = $true }
        @{ Opis = 'undo z Initial disabled = brak startu'; Case = @{ Set = 'enabled'; Restore = 'undo'; Initial = 'disabled' }; Phase = 'Restore'; Want = $false }
        @{ Opis = 'enable = start'; Case = @{ Set = 'disabled'; Restore = 'enable' }; Phase = 'Restore'; Want = $true }
        @{ Opis = 'disable = brak startu'; Case = @{ Set = 'enabled'; Restore = 'disable'; Initial = 'disabled' }; Phase = 'Restore'; Want = $false }
    ) {
        Get-VmAutostartExpectedStart -Case $Case -Phase $Phase | Should -Be $Want
    }
}

Describe 'Wait-VmAutostartStart i Confirm-VmAutostartStart (mock odczytu VM)' {
    BeforeAll {
        # Odczyt VM: linie probe.log i - domyślnie - proces konta dso dla każdej sondy z linią po rozruchu (sonda śpi godzinę).
        function New-DsoObs {
            param([string[]]$Lines, [string[]]$NoProcessFor = @(), [object[]]$Extra = @(), [string]$Boot = '2026-11-02T10:00:00.0000000Z', $LogError = $null)
            $BootAt = [datetime]::Parse($Boot, [Globalization.CultureInfo]::InvariantCulture, [Globalization.DateTimeStyles]::RoundtripKind)
            $Processes = [System.Collections.Generic.List[object]]::new()
            foreach ($Line in $Lines) {
                $Parts = $Line.Split('|')
                $At = [datetime]::Parse($Parts[1], [Globalization.CultureInfo]::InvariantCulture, [Globalization.DateTimeStyles]::RoundtripKind)
                if ($At -gt $BootAt -and $NoProcessFor -notcontains $Parts[0]) { $Processes.Add([pscustomobject]@{ Name = $Parts[0]; UserName = 'DSO-26H2-PRO\dso'; StartUtc = $Parts[1]; Id = 100 + $Processes.Count }) }
            }
            foreach ($P in $Extra) { $Processes.Add($P) }
            return [pscustomobject]@{ BootUtc = $Boot; LogError = $LogError; Lines = $Lines; Processes = $Processes.ToArray() }
        }
    }
    BeforeEach {
        Mock -ModuleName VmRun Start-Sleep {}
        $global:DsoObs = [System.Collections.Generic.Queue[object]]::new()
        Mock -ModuleName VmRun Get-VmAutostartObservation { if ($global:DsoObs.Count -gt 1) { $global:DsoObs.Dequeue() } else { $global:DsoObs.Peek() } }
    }
    AfterEach { Remove-Variable -Name DsoObs -Scope Global -ErrorAction SilentlyContinue }

    It 'linie sprzed rozruchu się nie liczą; kontrola po rozruchu kończy czekanie, a pozycje są oceniane po zapasie' {
        $Old = @('DsoRunUserCtl|2026-11-02T09:55:00.0000000Z|dso|1', 'DsoRunUser|2026-11-02T09:55:01.0000000Z|dso|1')
        $global:DsoObs.Enqueue((New-DsoObs -Lines $Old))
        $global:DsoObs.Enqueue((New-DsoObs -Lines ($Old + 'DsoRunUserCtl|2026-11-02T10:00:12.0000000Z|dso|1')))
        $Result = Wait-VmAutostartStart -Session $null -Controls @('DsoRunUserCtl') -Items @(@{ Id = 'run:user:DsoRunUser'; Probe = 'DsoRunUser' })
        @($Result.ControlsStarted) | Should -Be @('DsoRunUserCtl')
        @($Result.Items).Count | Should -Be 1
        $Result.Items[0].Id | Should -Be 'run:user:DsoRunUser'
        $Result.Items[0].Started | Should -BeFalse -Because 'jedyna linia DsoRunUser jest sprzed rozruchu'
        Should -Invoke -ModuleName VmRun Start-Sleep -ParameterFilter { $Seconds -eq 20 } -Times 1 -Exactly
    }
    It 'pozycja z linią po rozruchu i procesem wystartowała; odczyt obejmuje procesy sond pozycji i kontroli' {
        $global:DsoObs.Enqueue((New-DsoObs -Lines @('DsoRunUserCtl|2026-11-02T10:00:12.0000000Z|dso|1', 'DsoRunUser|2026-11-02T10:00:13.0000000Z|dso|1')))
        $Result = Wait-VmAutostartStart -Session $null -Controls @('DsoRunUserCtl') -Items @(@{ Id = 'run:user:DsoRunUser'; Probe = 'DsoRunUser' })
        $Result.Items[0].Started | Should -BeTrue
        Should -Invoke -ModuleName VmRun Get-VmAutostartObservation -ParameterFilter { @($ProcessNames) -contains 'DsoRunUser' -and @($ProcessNames) -contains 'DsoRunUserCtl' }
    }
    It 'proces konta dso ze StartTime po rozruchu = start; proces innego konta = brak' {
        $Lines = @('DsoRunUserCtl|2026-11-02T10:00:12.0000000Z|dso|1')
        $global:DsoObs.Enqueue((New-DsoObs -Lines $Lines -Extra @([pscustomobject]@{ Name = 'WindowsTerminal'; UserName = 'DSO-26H2-PRO\Administrator'; StartUtc = '2026-11-02T10:00:20.0000000Z'; Id = 1 })))
        $Item = @{ Id = 'appx:Microsoft.WindowsTerminal_8wekyb3d8bbwe!StartTerminalOnLoginTask'; Process = 'WindowsTerminal' }
        (Wait-VmAutostartStart -Session $null -Controls @('DsoRunUserCtl') -Items @($Item)).Items[0].Started | Should -BeFalse
        $global:DsoObs.Clear()
        $global:DsoObs.Enqueue((New-DsoObs -Lines $Lines -Extra @([pscustomobject]@{ Name = 'WindowsTerminal'; UserName = 'DSO-26H2-PRO\dso'; StartUtc = '2026-11-02T10:00:20.0000000Z'; Id = 2 })))
        (Wait-VmAutostartStart -Session $null -Controls @('DsoRunUserCtl') -Items @($Item)).Items[0].Started | Should -BeTrue
    }
    It 'kontrola, która nie wystartowała po rozruchu, kończy się błędem po limicie (z nazwą sondy)' {
        $global:DsoObs.Enqueue((New-DsoObs -Lines @('DsoRunUserCtl|2026-11-02T09:55:00.0000000Z|dso|1', 'DsoRunWowCtl|2026-11-02T10:00:12.0000000Z|dso|1')))
        { Wait-VmAutostartStart -Session $null -Controls @('DsoRunUserCtl', 'DsoRunWowCtl') -Items @() -TimeoutSeconds 20 -PollSeconds 5 } | Should -Throw -ExpectedMessage '*DsoRunUserCtl*'
        Should -Invoke -ModuleName VmRun Get-VmAutostartObservation -Times 5 -Exactly
    }
    It 'proces sondy bez linii w probe.log (sonda nie dopisała linii) = błąd, nie „nie wystartowała”' {
        $Lines = @('DsoRunUserCtl|2026-11-02T10:00:12.0000000Z|dso|1')
        $global:DsoObs.Enqueue((New-DsoObs -Lines $Lines -Extra @([pscustomobject]@{ Name = 'DsoRunUser'; UserName = 'DSO-26H2-PRO\dso'; StartUtc = '2026-11-02T10:00:13.0000000Z'; Id = 7 })))
        { Wait-VmAutostartStart -Session $null -Controls @('DsoRunUserCtl') -Items @(@{ Id = 'run:user:DsoRunUser'; Probe = 'DsoRunUser' }) } | Should -Throw -ExpectedMessage "*rozbieżność*DsoRunUser*"
    }
    It 'linia sondy po rozruchu bez jej procesu = błąd (po jednym ponowieniu odczytu)' {
        $global:DsoObs.Enqueue((New-DsoObs -Lines @('DsoRunUserCtl|2026-11-02T10:00:12.0000000Z|dso|1', 'DsoRunUser|2026-11-02T10:00:13.0000000Z|dso|1') -NoProcessFor @('DsoRunUser')))
        { Wait-VmAutostartStart -Session $null -Controls @('DsoRunUserCtl') -Items @(@{ Id = 'run:user:DsoRunUser'; Probe = 'DsoRunUser' }) } | Should -Throw -ExpectedMessage "*rozbieżność*DsoRunUser*"
        # 1 odczyt w czekaniu + 2 przy ocenie (pierwszy i ponowiony).
        Should -Invoke -ModuleName VmRun Get-VmAutostartObservation -Times 3 -Exactly
    }
    It 'rozbieżność, która znika przy ponowionym odczycie, nie jest błędem' {
        $Ctl = 'DsoRunUserCtl|2026-11-02T10:00:12.0000000Z|dso|1'
        $global:DsoObs.Enqueue((New-DsoObs -Lines @($Ctl)))
        $global:DsoObs.Enqueue((New-DsoObs -Lines @($Ctl) -Extra @([pscustomobject]@{ Name = 'DsoRunUser'; UserName = 'DSO-26H2-PRO\dso'; StartUtc = '2026-11-02T10:00:31.0000000Z'; Id = 9 })))
        $global:DsoObs.Enqueue((New-DsoObs -Lines @($Ctl, 'DsoRunUser|2026-11-02T10:00:31.0000000Z|dso|1')))
        (Wait-VmAutostartStart -Session $null -Controls @('DsoRunUserCtl') -Items @(@{ Id = 'run:user:DsoRunUser'; Probe = 'DsoRunUser' })).Items[0].Started | Should -BeTrue
    }
    It 'nieudany odczyt probe.log po zapasie (także po ponowieniu) = błąd, nie „nie wystartowała”' {
        $global:DsoObs.Enqueue((New-DsoObs -Lines @('DsoRunUserCtl|2026-11-02T10:00:12.0000000Z|dso|1')))
        $global:DsoObs.Enqueue((New-DsoObs -Lines @() -LogError 'plik zajęty'))
        { Wait-VmAutostartStart -Session $null -Controls @('DsoRunUserCtl') -Items @(@{ Id = 'run:user:DsoRunUser'; Probe = 'DsoRunUser' }) } | Should -Throw -ExpectedMessage '*probe.log*plik zajęty*'
    }
    It 'Confirm: pozycja bez startu w pierwszym odczycie i ze startem w drugim = Late (Probe i Process)' {
        $Ctl = 'DsoRunUserCtl|2026-11-02T10:00:12.0000000Z|dso|1'
        $Items = @(@{ Id = 'run:user:DsoRunUser'; Probe = 'DsoRunUser' }, @{ Id = 'appx:Microsoft.WindowsTerminal_8wekyb3d8bbwe!StartTerminalOnLoginTask'; Process = 'WindowsTerminal' })
        $global:DsoObs.Enqueue((New-DsoObs -Lines @($Ctl)))
        $First = Wait-VmAutostartStart -Session $null -Controls @('DsoRunUserCtl') -Items $Items
        @($First.Items | Where-Object Started).Count | Should -Be 0
        $global:DsoObs.Clear()
        $global:DsoObs.Enqueue((New-DsoObs -Lines @($Ctl, 'DsoRunUser|2026-11-02T10:01:20.0000000Z|dso|1') -Extra @([pscustomobject]@{ Name = 'WindowsTerminal'; UserName = 'DSO-26H2-PRO\dso'; StartUtc = '2026-11-02T10:01:25.0000000Z'; Id = 3 })))
        $Again = Confirm-VmAutostartStart -Session $null -Observation $First -Controls @('DsoRunUserCtl') -Items $Items
        $Again.RecheckedAtUtc | Should -Not -BeNullOrEmpty
        foreach ($Row in @($Again.Items)) {
            $Row.Started | Should -BeTrue
            $Row.Late | Should -BeTrue -Because "pozycja '$($Row.Id)' wystartowała dopiero po pierwszym odczycie"
            $Row.StartedAtSettle | Should -BeFalse
        }
    }
    It 'Confirm: start w pierwszym odczycie = nie Late; brak startu w obu = Started false' {
        $Ctl = 'DsoRunUserCtl|2026-11-02T10:00:12.0000000Z|dso|1'
        $Items = @(@{ Id = 'run:user:DsoRunUser'; Probe = 'DsoRunUser' }, @{ Id = 'run32:DsoRunWow'; Probe = 'DsoRunWow' })
        $global:DsoObs.Enqueue((New-DsoObs -Lines @($Ctl, 'DsoRunUser|2026-11-02T10:00:14.0000000Z|dso|1')))
        $First = Wait-VmAutostartStart -Session $null -Controls @('DsoRunUserCtl') -Items $Items
        $Again = Confirm-VmAutostartStart -Session $null -Observation $First -Controls @('DsoRunUserCtl') -Items $Items
        $User = @($Again.Items | Where-Object Id -eq 'run:user:DsoRunUser')[0]
        $Wow = @($Again.Items | Where-Object Id -eq 'run32:DsoRunWow')[0]
        $User.Started | Should -BeTrue
        $User.Late | Should -BeFalse
        $Wow.Started | Should -BeFalse
        $Wow.Late | Should -BeFalse
    }
    It 'Confirm: odczyt z innym czasem rozruchu = błąd' {
        $Ctl = 'DsoRunUserCtl|2026-11-02T10:00:12.0000000Z|dso|1'
        $global:DsoObs.Enqueue((New-DsoObs -Lines @($Ctl)))
        $First = Wait-VmAutostartStart -Session $null -Controls @('DsoRunUserCtl') -Items @()
        $global:DsoObs.Clear()
        $global:DsoObs.Enqueue((New-DsoObs -Lines @('DsoRunUserCtl|2026-11-02T10:05:12.0000000Z|dso|1') -Boot '2026-11-02T10:05:00.0000000Z'))
        { Confirm-VmAutostartStart -Session $null -Observation $First -Controls @('DsoRunUserCtl') -Items @() } | Should -Throw -ExpectedMessage '*innego rozruchu*'
    }
}

Describe 'Wait-VmStartupInfo (mock odczytu VM)' {
    BeforeEach {
        Mock -ModuleName VmRun Start-Sleep {}
        $global:DsoInfo = [System.Collections.Generic.Queue[object]]::new()
        Mock -ModuleName VmRun Get-VmStartupInfoState { if ($global:DsoInfo.Count -gt 1) { $global:DsoInfo.Dequeue() } else { $global:DsoInfo.Peek() } }
    }
    AfterEach { Remove-Variable -Name DsoInfo -Scope Global -ErrorAction SilentlyContinue }

    It 'plik sprzed rozruchu się nie liczy; czeka na plik zapisany po rozruchu' {
        $Old = [pscustomobject]@{ Name = 'S-1-5-21-1_StartupInfo1.xml'; WriteUtc = '2026-11-02T09:58:00.0000000Z' }
        $New = [pscustomobject]@{ Name = 'S-1-5-21-1_StartupInfo2.xml'; WriteUtc = '2026-11-02T10:01:35.0000000Z' }
        $global:DsoInfo.Enqueue([pscustomobject]@{ Sid = 'S-1-5-21-1'; BootUtc = '2026-11-02T10:00:00.0000000Z'; Files = @($Old) })
        $global:DsoInfo.Enqueue([pscustomobject]@{ Sid = 'S-1-5-21-1'; BootUtc = '2026-11-02T10:00:00.0000000Z'; Files = @($Old, $New) })
        $Result = Wait-VmStartupInfo -Session $null
        $Result.File | Should -Be 'S-1-5-21-1_StartupInfo2.xml'
        Should -Invoke -ModuleName VmRun Get-VmStartupInfoState -Times 2 -Exactly
    }
    It 'brak pliku po rozruchu = błąd po limicie' {
        $global:DsoInfo.Enqueue([pscustomobject]@{ Sid = 'S-1-5-21-1'; BootUtc = '2026-11-02T10:00:00.0000000Z'; Files = @() })
        { Wait-VmStartupInfo -Session $null -TimeoutSeconds 10 -PollSeconds 5 } | Should -Throw -ExpectedMessage '*StartupInfo*'
    }
}

Describe 'Invoke-VmCase: Flow Autostart (mocki VM)' {
    BeforeEach {
        $global:DsoOrder = [System.Collections.Generic.List[string]]::new()
        $global:DsoCliArgs = [System.Collections.Generic.List[object]]::new()
        Mock -ModuleName VmRun Copy-VmFixtures { $global:DsoOrder.Add('fixtures') }
        # Jak prawdziwe Invoke-VmSetup -PassThru: jedna tablica tekstów (return ,).
        Mock -ModuleName VmRun Invoke-VmSetup { $global:DsoOrder.Add('setup'); , [string[]]@('wynik setup', 'druga linia') }
        Mock -ModuleName VmRun Get-VmKeyExistence { $global:DsoOrder.Add('keys'); @($Keys | ForEach-Object { [pscustomobject]@{ Key = $_; Exists = $false } }) }
        Mock -ModuleName VmRun Invoke-DsoCli {
            $global:DsoOrder.Add(($Arguments[0] -replace '^--', '') + $(if ($Arguments.Count -gt 3 -and $Arguments[1] -ne '--out') { ' ' + $Arguments[1] } else { '' }))
            $global:DsoCliArgs.Add([pscustomobject]@{ Arguments = $Arguments; RunAsUser = $RunAsUser })
            [pscustomobject]@{ ExitCode = 0; TimedOut = $false; Report = [pscustomobject]@{ exitCode = 0; outcome = 'Applied' }; Json = '{}'; ReportError = $null }
        }
        Mock -ModuleName VmRun Restart-VmAndReconnect { $global:DsoOrder.Add("restart:$Step"); [pscustomobject]@{ Session = $null; Record = [pscustomobject]@{ Step = $Step; BootBefore = 'a'; BootAfter = 'b'; Attempts = 1; MarkerGone = $true } } }
        Mock -ModuleName VmRun Wait-VmAutostartStart { $global:DsoOrder.Add('wait-start'); [pscustomobject]@{ BootUtc = 'b'; ControlsStarted = $Controls; Items = @() } }
        Mock -ModuleName VmRun Confirm-VmAutostartStart { $global:DsoOrder.Add('confirm-start'); [pscustomobject]@{ BootUtc = 'b'; ControlsStarted = $Controls; Items = @(); RecheckedAtUtc = 'c' } }
        Mock -ModuleName VmRun Wait-VmStartupInfo { $global:DsoOrder.Add('wait-startupinfo'); [pscustomobject]@{ File = 'x.xml' } }
        Mock -ModuleName VmRun Invoke-VmCheck { if ($Script -eq 'po-cofnieciu') { $global:DsoOrder.Add('check-after-undo'); 'Check zwrócił: klucz zadania nie istnieje' } else { $global:DsoOrder.Add('check'); 'Passed' } }
        Mock -ModuleName VmRun Invoke-Command -RemoveParameterValidation 'Session' -MockWith { $global:DsoOrder.Add('cleanup') }
    }
    AfterEach { Remove-Variable -Name DsoOrder, DsoCliArgs -Scope Global -ErrorAction SilentlyContinue }

    It 'kolejność: fixtures, Setup, lista, set, restart, start + StartupInfo + ponowna ocena, przywrócenie, CheckAfterUndo, restart, start + StartupInfo + ponowna ocena, lista, Check, Cleanup' {
        $Case = New-DsoAutostartCase -Extra @{ Cleanup = 'Write-Output 1'; CheckAfterUndo = 'po-cofnieciu'; Expect = @{ SetExit = 0; SetOutcome = 'Applied'; RestoreExit = 0; RestoreOutcome = 'Undone'; ListDoesNotWrite = @('HKLM\SOFTWARE\X') } }
        $Record = Invoke-VmCase -Session $null -Case $Case -Layout (Get-DsoVmLayout) -Reconnect { $null }
        @($global:DsoOrder) | Should -Be @('fixtures', 'setup', 'keys', 'list-autostart', 'keys',
            'set-autostart run:user:DsoRunUser', 'set-autostart appx:Microsoft.WindowsTerminal_8wekyb3d8bbwe!StartTerminalOnLoginTask',
            'restart:reboot-after-set', 'wait-start', 'wait-startupinfo', 'confirm-start',
            'undo-autostart run:user:DsoRunUser', 'undo-autostart appx:Microsoft.WindowsTerminal_8wekyb3d8bbwe!StartTerminalOnLoginTask',
            'check-after-undo', 'restart:reboot-after-restore', 'wait-start', 'wait-startupinfo', 'confirm-start', 'list-autostart', 'check', 'cleanup')
        @($Record.Steps.Step) | Should -Be @('list-before', 'set-1', 'set-2', 'restore-1', 'restore-2', 'list-after')
        @($Record.Steps | Where-Object Step -like 'set-*' | ForEach-Object ItemId) | Should -Be @('run:user:DsoRunUser', 'appx:Microsoft.WindowsTerminal_8wekyb3d8bbwe!StartTerminalOnLoginTask')
        @($Record.Reboots.Step) | Should -Be @('reboot-after-set', 'reboot-after-restore')
        $Record.CheckResult | Should -Be 'Passed'
        $Record.CheckAfterUndoResult | Should -Be 'Check zwrócił: klucz zadania nie istnieje' -Because 'wynik CheckAfterUndo trafia do rekordu (scenariusz robi z niego czerwony test)'
        $Record.StartAfterSet.RecheckedAtUtc | Should -Be 'c'
        $Record.StartAfterRestore.RecheckedAtUtc | Should -Be 'c'
        @($Record.KeysBeforeList).Count | Should -Be 1
        @($Record.KeysAfterList).Count | Should -Be 1
        $Record.StartupInfo.File | Should -Be 'x.xml'
        $Record.StartupInfoAfterSet.File | Should -Be 'x.xml'
        # Konto przypadku (RunAs) przy każdym wywołaniu CLI, set z drugim argumentem = stan.
        @($global:DsoCliArgs | Where-Object { $_.RunAsUser -ne 'dso' }).Count | Should -Be 0
        $global:DsoCliArgs[1].Arguments[2] | Should -Be 'disabled'
    }
    It 'SetupOutput: płaska lista tekstów (bez tablicy w tablicy, w JSON bez „System.Object[]”)' {
        $Record = Invoke-VmCase -Session $null -Case @{ Name = 'lista'; Flow = 'Autostart'; RunAs = 'dso'; Check = '$true'; Expect = @{} } -Layout (Get-DsoVmLayout)
        @($Record.SetupOutput).Count | Should -Be 2
        foreach ($Line in @($Record.SetupOutput)) { $Line | Should -BeOfType [string] }
        $Json = $Record | ConvertTo-Json -Depth 20
        $Json | Should -Not -Match 'System\.Object\[\]'
        $Json | Should -Match '"wynik setup"'
    }
    It 'bez CheckAfterUndo: brak wyniku w rekordzie i brak dodatkowego Check' {
        $null = Invoke-VmCase -Session $null -Case (New-DsoAutostartCase) -Layout (Get-DsoVmLayout) -Reconnect { $null }
        @($global:DsoOrder | Where-Object { $_ -eq 'check-after-undo' }).Count | Should -Be 0
    }
    It 'Restore enable/disable = set-autostart z odpowiednim stanem' -ForEach @(
        @{ Restore = 'enable'; State = 'enabled' }
        @{ Restore = 'disable'; State = 'disabled' }
    ) {
        $Case = New-DsoAutostartCase -Extra @{ Restore = $Restore; Items = @(@{ Id = 'run32:DsoRunWow'; Probe = 'DsoRunWow' }); RunAs = $null }
        $Case.Remove('RunAs')
        $null = Invoke-VmCase -Session $null -Case $Case -Layout (Get-DsoVmLayout) -Reconnect { $null }
        $Restore = @($global:DsoCliArgs)[2]
        @($Restore.Arguments)[0..2] | Should -Be @('--set-autostart', 'run32:DsoRunWow', $State)
        $Restore.RunAsUser | Should -BeNullOrEmpty -Because 'przypadek bez RunAs = CLI jako Administrator sesji PS Direct'
    }
    It 'bez Set: tylko lista i Check, bez restartu' {
        $Case = @{ Name = 'lista'; Flow = 'Autostart'; RunAs = 'dso'; Check = '$true'; Expect = @{} }
        $Record = Invoke-VmCase -Session $null -Case $Case -Layout (Get-DsoVmLayout)
        @($global:DsoOrder) | Should -Be @('fixtures', 'setup', 'list-autostart', 'check')
        @($Record.Steps.Step) | Should -Be @('list-before')
        Should -Invoke -ModuleName VmRun Restart-VmAndReconnect -Times 0 -Exactly
    }
    It 'z Set bez Reconnect: wyjątek przed czymkolwiek' {
        { Invoke-VmCase -Session $null -Case (New-DsoAutostartCase) -Layout (Get-DsoVmLayout) } | Should -Throw -ExpectedMessage '*Reconnect*'
        @($global:DsoOrder).Count | Should -Be 0
    }
    It 'błąd czekania na start: Cleanup i tak się wykonuje, wyjątek wychodzi' {
        Mock -ModuleName VmRun Wait-VmAutostartStart { throw 'kontrola DsoRunUserCtl nie wystartowała' }
        $Case = New-DsoAutostartCase -Extra @{ Cleanup = 'Write-Output 1' }
        { Invoke-VmCase -Session $null -Case $Case -Layout (Get-DsoVmLayout) -Reconnect { $null } } | Should -Throw -ExpectedMessage '*DsoRunUserCtl*'
        $global:DsoOrder[$global:DsoOrder.Count - 1] | Should -Be 'cleanup'
    }
}

Describe 'Scenariusz VM: Flow Autostart (przez Invoke-DsoVmRun, kontekst produkcyjny)' {
    BeforeAll {
        function script:Invoke-DryRun {
            param([string]$Variant)
            $Script = Join-Path $PSScriptRoot 'helpers\Invoke-ScenarioDryRun.ps1'
            $Lines = @(& powershell.exe -NoProfile -File $Script -Variant $Variant 2>&1 | ForEach-Object { "$_" })
            $Line = $Lines | Where-Object { $_ -like 'DSORESULT:*' } | Select-Object -Last 1
            if (-not $Line) { throw "Brak wyniku dry-runu '$Variant': $($Lines -join ' | ')" }
            return ($Line.Substring(10) | ConvertFrom-Json)
        }
    }
    It 'Autostart: lista, brak zapisu odczytu, kody, wyniki, restarty, start i brak startu, pomiar, Check i Cleanup są zielone' {
        $Result = Invoke-DryRun -Variant 'Autostart'
        $Result.threw | Should -BeNullOrEmpty -Because "$($Result.threw)"
        $Result.failed | Should -Be 0 -Because (@($Result.messages) -join '; ')
        $Result.passed | Should -Be 16
        $Result.cases | Should -Be 2
    }
    It '<Variant>: czerwony test z komunikatem zawierającym "<Fragment>"' -ForEach @(
        @{ Variant = 'AutostartWrongListed'; Fragment = "'list-before'" }
        @{ Variant = 'AutostartListWrote'; Fragment = 'odczyt listy zapisał rejestr' }
        @{ Variant = 'AutostartWrongSetOutcome'; Fragment = "'set-1'" }
        @{ Variant = 'AutostartStartedWhenDisabled'; Fragment = 'oczekiwano False' }
        @{ Variant = 'AutostartNotStartedAfterUndo'; Fragment = 'oczekiwano True' }
        @{ Variant = 'AutostartControlMissing'; Fragment = 'DsoRunUserCtl' }
        @{ Variant = 'AutostartWrongRestoreOutcome'; Fragment = "'restore-1'" }
        @{ Variant = 'AutostartOneReboot'; Fragment = 'restart' }
        @{ Variant = 'AutostartNotMeasured'; Fragment = 'cpuMs' }
        @{ Variant = 'AutostartWrongStateAfter'; Fragment = "'list-after'" }
        @{ Variant = 'AutostartLateStart'; Fragment = 'późny start' }
        @{ Variant = 'AutostartCheckAfterUndoFails'; Fragment = 'klucz zadania nie istnieje' }
    ) {
        $Result = Invoke-DryRun -Variant $Variant
        $Result.threw | Should -BeNullOrEmpty -Because "$($Result.threw)"
        $Result.failed | Should -BeGreaterThan 0
        (@($Result.messages) -join ' | ') | Should -BeLike "*$Fragment*"
    }
}

Describe 'Pliki sond autostartu' {
    It '<Plik>: UTF-8 z BOM' -ForEach @(
        @{ Plik = 'fixtures\Install-DsoAutostartProbe.ps1' }
        @{ Plik = 'modules\VmRun.psm1' }
        @{ Plik = 'cases.psd1' }
        @{ Plik = 'tests\VmRunAutostart.Tests.ps1' }
    ) {
        $Bytes = [System.IO.File]::ReadAllBytes((Join-Path $script:VmRoot $Plik))
        ($Bytes[0] -eq 0xEF -and $Bytes[1] -eq 0xBB -and $Bytes[2] -eq 0xBF) | Should -BeTrue
    }
    It 'Install-DsoAutostartProbe.ps1 odmawia poza VM DSO, zanim cokolwiek zapisze' {
        $Script = Join-Path $script:VmRoot 'fixtures\Install-DsoAutostartProbe.ps1'
        { & $Script -Name DsoHostCheck -Source RunUser -Missing } | Should -Throw -ExpectedMessage '*VM*'
        Test-Path -LiteralPath 'C:\dsotest\DsoHostCheck.exe' | Should -BeFalse
    }
    It 'DsoAutostartProbe.cs: składnia C# 5 (bez interpolacji i ciał wyrażeniowych), log probe.log i stały czas uśpienia' {
        $Text = Get-Content -LiteralPath (Join-Path $script:VmRoot 'fixtures\DsoAutostartProbe.cs') -Raw -Encoding UTF8
        $Text | Should -Not -Match '\$"'
        $Text | Should -Not -Match '\)\s*=>'
        $Text | Should -Match ([regex]::Escape('C:\dsotest\probe.log'))
        $Text | Should -Match 'Thread\.Sleep\(3600000\)'
    }
}

AfterAll { Clear-NoLiveVm }
