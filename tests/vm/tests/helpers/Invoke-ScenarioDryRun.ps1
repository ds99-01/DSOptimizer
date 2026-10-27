<#
Instrukcja uruchomienia:
    powershell.exe -NoProfile -File tests\vm\tests\helpers\Invoke-ScenarioDryRun.ps1 -Variant Good
    (wywoływane przez VmHost.Tests.ps1; nie dotyka żadnej VM ani Hyper-V)
Opis działania:
    Odtwarza kontekst produkcyjny: uruchamia Invoke-DsoVmRun (VmHost.psm1, StrictMode Latest, ErrorActionPreference Stop),
    który woła prawdziwy Invoke-Pester na prawdziwym pliku scenariuszy. Podmienione są tylko granice z VM i Hyper-V
    (Get-VM, Start-VM, Wait-VmReady, Copy-DsoBuildToVm, Invoke-Command, Invoke-VmCase). Sztuczne rekordy są generowane
    z oczekiwań przypadku (dla list bierze OSTATNIĄ wartość), a wariant mutuje je tak, by scenariusz miał zrobić się czerwony.
    Wypisuje linię 'DSORESULT:{json}' z passed, failed, messages, cases.
#>
param(
    [Parameter(Mandatory)][ValidateSet('Good', 'NoReport', 'HiveMissing', 'WrittenToHkcu', 'EmptyNotWrittenTo', 'StateDiff', 'RunThrows', 'WrongState', 'ArrayOk', 'ArrayBad', 'EmptyAllowed', 'EmptyActual', 'RealCases', 'Skipped', 'Execute', 'ExecuteCheckFails', 'ExecuteCleanupFails', 'ExecuteFailedMessage', 'ExecuteFailedMessageWrong', 'ExecuteRequiresReboot', 'ExecuteNoRequiresReboot', 'ExecuteNoRequiresRebootField', 'SkippedLiveIt', 'Reboot', 'RebootKeyPresent', 'RebootNoRestart', 'RebootNoKeyCheck', 'RebootWrongAfterReboot', 'ReconnectReal', 'FullChecks', 'FullCheckAfterApplyFails', 'FullCheckAfterUndoEmpty', 'RebootKeyPresentMissing', 'RebootMarkerNotGone', 'RebootNoRequiresReboot', 'NoSnapshot', 'AppRemove', 'AppRemoveStillListed', 'AppRemoveWrongOutcome', 'AppRemoveNoWarning', 'AppRemoveWrongRemovability', 'AppRemoveCheckFails', 'AppRemoveNoApps', 'AppRemoveNoRequiresReboot', 'AppInstall', 'AppInstallWrongOutcome', 'AppInstallMissingItem', 'AppInstallWrongInstalled', 'AppInstallNoInstallRows', 'AppInstallWrongWingetState', 'AppInstallNoMessage', 'AppInstallWrongExit', 'AppInstallCheckFails', 'AppInstallWrongWingetCode', 'AppInstallUnexpectedWingetCode')][string]$Variant
)
$ErrorActionPreference = 'Stop'
$VmRoot = Split-Path -Parent (Split-Path -Parent $PSScriptRoot)
Import-Module Pester -MinimumVersion 5.0
Import-Module (Join-Path $VmRoot 'modules\VmCommon.psm1') -Force
Import-Module (Join-Path $VmRoot 'modules\VmRun.psm1') -Force
Import-Module (Join-Path $VmRoot 'modules\VmHost.psm1') -Force

$global:DsoVariant = $Variant
$global:DsoSid = 'S-1-5-21-9-9-9-1000'
$global:DsoHiveLoaded = ($Variant -ne 'HiveMissing')

function global:New-DsoFakeStep {
    param([string]$Step, [int]$ExitCode = 0, $Report)
    [pscustomobject]@{ Step = $Step; ExitCode = $ExitCode; TimedOut = $false; Report = $Report; Json = $null; ReportError = $null }
}
function global:Get-DsoLast { param($Value) $Items = @($Value); return $Items[$Items.Count - 1] }
function global:New-DsoFakeEntries {
    param($Items, [string]$Kind)
    $Result = foreach ($Item in @($Items)) {
        $Entry = [ordered]@{}
        if ($Kind -eq 'Registry') { $Entry.Key = "$($Item.Path)\$($Item.Name)" } else { $Entry.Name = $Item.Name }
        foreach ($Key in $Item.Keys) { if ($Key -notin @('Path', 'Name')) { $Entry[$Key] = $Item[$Key] } }
        [pscustomobject]$Entry
    }
    return @($Result)
}
function global:New-DsoFakeRecord {
    param($Case)
    $E = $Case.Expect
    $Steps = [System.Collections.Generic.List[object]]::new()
    if ($Case.Flow -eq 'AppInstall') {
        $Report = [pscustomobject]@{
            exitCode = (Get-DsoLast $E.Exit)
            message  = $(if ($E.ContainsKey('MessageContains')) { "komunikat: $($E.MessageContains)." } else { $null })
            winget   = [pscustomobject]@{ state = $(if ($E.ContainsKey('WingetState')) { Get-DsoLast $E.WingetState } else { 'Ready' }); message = 'winget' }
        }
        if ($Case.Command -eq 'install-apps') {
            $Items = foreach ($Id in @($Case.AppIds)) {
                [pscustomobject]@{
                    id         = $Id
                    outcome    = $(if ($E.ContainsKey('Outcomes') -and $E.Outcomes.ContainsKey($Id)) { Get-DsoLast $E.Outcomes[$Id] } else { 'Installed' })
                    wingetCode = $(if ($E.ContainsKey('WingetCodes') -and $E.WingetCodes.ContainsKey($Id) -and $null -ne $E.WingetCodes[$Id]) { Get-DsoLast $E.WingetCodes[$Id] } elseif ($E.ContainsKey('WingetCodes') -and $E.WingetCodes.ContainsKey($Id)) { $null } else { '0x00000000' })
                    message    = ''
                }
            }
            $Report | Add-Member -NotePropertyName installItems -NotePropertyValue @($Items)
        }
        else {
            $Rows = @(if ($E.ContainsKey('Installed')) { foreach ($Id in $E.Installed.Keys) { [pscustomobject]@{ id = $Id; installed = [bool]$E.Installed[$Id] } } })
            $Report | Add-Member -NotePropertyName installRows -NotePropertyValue $Rows
        }
        $Steps.Add((New-DsoFakeStep $Case.Command (Get-DsoLast $E.Exit) $Report))
        return [pscustomobject]@{ Name = $Case.Name; Command = $Case.Command; Steps = $Steps.ToArray(); CheckResult = 'Passed'; CleanupError = $null }
    }
    if ($Case.Flow -eq 'AppRemove') {
        $Row = [pscustomobject]@{
            id = $Case.AppId; kind = 'Store'; name = 'Aplikacja'; publisher = $null
            group = $(if ($E.ContainsKey('Group')) { Get-DsoLast $E.Group } else { 'Other' })
            removability = $(if ($E.ContainsKey('Removability')) { Get-DsoLast $E.Removability } else { 'Removable' })
        }
        $Steps.Add((New-DsoFakeStep 'list-before' 0 ([pscustomobject]@{ exitCode = 0; apps = @(if ($E.ListedBefore) { $Row }) })))
        $Remove = [pscustomobject]@{
            outcome        = (Get-DsoLast $E.RemoveOutcome)
            requiresReboot = [bool]($E.ContainsKey('RequiresReboot') -and $E.RequiresReboot)
            aborted        = [bool]($E.ContainsKey('Aborted') -and $E.Aborted)
            message        = if ($E.ContainsKey('MessageContains')) { "komunikat: $($E.MessageContains)." } else { '' }
            warnings       = @(if ($E.ContainsKey('WarningContains')) { "ostrzeżenie: $($E.WarningContains)" })
            restorePoint   = if ($E.ContainsKey('RestorePointStatus')) { [pscustomobject]@{ status = (Get-DsoLast $E.RestorePointStatus) } } else { $null }
        }
        $Steps.Add((New-DsoFakeStep 'remove-app' (Get-DsoLast $E.RemoveExit) $Remove))
        $Steps.Add((New-DsoFakeStep 'list-after' 0 ([pscustomobject]@{ exitCode = 0; apps = @(if ($E.ListedAfter) { $Row }) })))
        return [pscustomobject]@{
            Name = $Case.Name; AppId = $Case.AppId; Steps = $Steps.ToArray()
            CheckResult = 'Passed'; CleanupError = $null
        }
    }
    if ($Case.Flow -eq 'Execute') {
        $Steps.Add((New-DsoFakeStep 'execute' (Get-DsoLast $E.ExecuteExit) ([pscustomobject]@{ outcome = (Get-DsoLast $E.ExecuteOutcome); requiresReboot = [bool]($E.ContainsKey('RequiresReboot') -and $E.RequiresReboot); message = $(if ($E.ContainsKey('MessageContains')) { "komunikat: $($E.MessageContains)." } else { '' }) })))
        return [pscustomobject]@{
            Name = $Case.Name; TweakId = $Case.TweakId; Steps = $Steps.ToArray(); SnapshotDiff = @()
            CheckResult = $(if ($Case.ContainsKey('Check')) { 'Passed' } else { $null }); CleanupError = $null
        }
    }
    if ($Case.Flow -eq 'DetectOnly') {
        $Steps.Add((New-DsoFakeStep 'detect-before' 0 ([pscustomobject]@{ state = (Get-DsoLast $E.Before) })))
        return [pscustomobject]@{ Name = $Case.Name; TweakId = $Case.TweakId; Steps = $Steps.ToArray(); SnapshotDiff = @() }
    }
    $Steps.Add((New-DsoFakeStep 'detect-before' 0 ([pscustomobject]@{ state = (Get-DsoLast $E.Before) })))
    $Apply = [pscustomobject]@{
        outcome      = (Get-DsoLast $E.ApplyOutcome)
        aborted      = [bool]($E.ContainsKey('Aborted') -and $E.Aborted)
        message      = if ($E.ContainsKey('MessageContains')) { "komunikat: $($E.MessageContains)." } else { '' }
        restorePoint = if ($E.ContainsKey('RestorePointStatus')) { [pscustomobject]@{ status = (Get-DsoLast $E.RestorePointStatus) } } else { $null }
        requiresReboot = ($Case.Flow -eq 'Reboot')
        user         = if ($E.ContainsKey('UserSid')) { [pscustomobject]@{ sid = $E.UserSid; isCertain = $(if ($E.ContainsKey('UserIsCertain')) { $E.UserIsCertain } else { $true }) } } else { $null }
    }
    $Steps.Add((New-DsoFakeStep 'apply' (Get-DsoLast $E.ApplyExit) $Apply))
    $Steps.Add((New-DsoFakeStep 'detect-after-apply' 0 ([pscustomobject]@{ state = (Get-DsoLast $E.AfterApply) })))
    $Reboots = @()
    if ($Case.Flow -eq 'Reboot') {
        $Steps.Add((New-DsoFakeStep 'detect-after-reboot' 0 ([pscustomobject]@{ state = (Get-DsoLast $E.AfterReboot) })))
        $Reboots = @([pscustomobject]@{ Step = 'reboot-after-apply'; BootBefore = '2026-10-17T10:00:00.0000000'; BootAfter = '2026-10-17T10:05:00.0000000'; Attempts = 1; MarkerGone = $true }, [pscustomobject]@{ Step = 'reboot-after-undo'; BootBefore = '2026-10-17T10:05:00.0000000'; BootAfter = '2026-10-17T10:10:00.0000000'; Attempts = 1; MarkerGone = $true })
    }
    if ($E.ContainsKey('UndoExit')) {
        $Steps.Add((New-DsoFakeStep 'undo' (Get-DsoLast $E.UndoExit) ([pscustomobject]@{ outcome = (Get-DsoLast $E.UndoOutcome) })))
        $Steps.Add((New-DsoFakeStep 'detect-after-undo' 0 ([pscustomobject]@{ state = (Get-DsoLast $E.AfterUndo) })))
    }
    $After = if ($E.ContainsKey('AfterApplySnapshot')) { $E.AfterApplySnapshot } else { @{} }
    $NotWritten = $null
    if ($E.ContainsKey('NotWrittenTo')) {
        $Rows = foreach ($Item in @($E.NotWrittenTo)) { [pscustomobject]@{ Key = "$($Item.Path)\$($Item.Name)"; Exists = $false } }
        $NotWritten = [pscustomobject]@{ Registry = @($Rows); Services = @() }
    }
    $CountBefore = $null; $CountAfter = $null
    if ($E.ContainsKey('RestorePointCreated')) { $CountBefore = 2; $CountAfter = 2 + [int][bool]$E.RestorePointCreated }
    return [pscustomobject]@{
        Name                    = $Case.Name
        TweakId                 = $Case.TweakId
        Steps                   = $Steps.ToArray()
        SnapshotAfterApply      = [pscustomobject]@{ Registry = (New-DsoFakeEntries -Items $After.Registry -Kind Registry); Services = (New-DsoFakeEntries -Items $After.Services -Kind Services) }
        SnapshotDiff            = @()
        Reboots                 = $Reboots
        KeysAfterUndo           = @(if ($E.ContainsKey('KeyAbsentAfterUndo')) { foreach ($K in @($E.KeyAbsentAfterUndo)) { [pscustomobject]@{ Key = $K; Exists = $false } } })
        KeysPresentAfterUndo    = @(if ($E.ContainsKey('KeyPresentAfterUndo')) { foreach ($K in @($E.KeyPresentAfterUndo)) { [pscustomobject]@{ Key = $K; Exists = $true } } })
        CheckAfterApplyResult   = $(if ($Case.ContainsKey('CheckAfterApply')) { 'Passed' } else { $null })
        CheckAfterUndoResult    = $(if ($Case.ContainsKey('CheckAfterUndo')) { 'Passed' } else { $null })
        NotWrittenTo            = $NotWritten
        NotWrittenToDiff        = @()
        RestorePointCountBefore = $CountBefore
        RestorePointCountAfter  = $CountAfter
    }
}
function global:Invoke-VmCase {
    param($Session, $Case, $Layout, [scriptblock]$Reconnect)
    if ($Case.ContainsKey('Skip')) { $global:DsoSkipInvoked = $true }
    if ($global:DsoVariant -eq 'RunThrows') { throw 'sztuczny wyjątek przebiegu' }
    if ($global:DsoVariant -eq 'ReconnectReal') {
        # PRAWDZIWY blok Reconnect z Invoke-DsoVmRun, wołany spod prawdziwego Invoke-Pester uruchomionego z wnętrza modułu.
        $global:DsoHb.Enqueue('LostCommunication')
        $NewSession = & $Reconnect 1
        $global:DsoReconnected = ($NewSession -is [System.Management.Automation.Runspaces.PSSession])
    }
    $R = New-DsoFakeRecord -Case $Case
    switch ($global:DsoVariant) {
        'NoReport' { $R.Steps[0] = New-DsoFakeStep 'detect-before' 740 $null }
        'WrongState' { $R.Steps[2].Report.state = 'NotApplied' }
        'WrittenToHkcu' { $R.NotWrittenToDiff = @("po apply: Rejestr 'x': Exists 'False' -> 'True'") }
        'EmptyNotWrittenTo' { $R.NotWrittenTo.Registry = @() }
        'StateDiff' { $R.SnapshotDiff = @("Rejestr 'x': Exists 'False' -> 'True'") }
        'EmptyActual' { $R.Steps[0].Report.state = $null }
        'ArrayBad' { $R.Steps[1].ExitCode = 5; $R.Steps[1].Report.outcome = 'Refused' }
        'ExecuteCheckFails' { $R.CheckResult = 'Check zwrócił: False' }
        'ExecuteCleanupFails' { $R.CleanupError = 'sprzątanie nie powiodło się' }
        'ExecuteFailedMessageWrong' { $R.Steps[0].Report.message = 'inny błąd' }
        'ExecuteNoRequiresReboot' { $R.Steps[0].Report.requiresReboot = $false }
        'ExecuteNoRequiresRebootField' { $R.Steps[0].Report.PSObject.Properties.Remove('requiresReboot') }
        'RebootKeyPresent' { $R.KeysAfterUndo[0].Exists = $true }
        'RebootKeyPresentMissing' { $R.KeysPresentAfterUndo[0].Exists = $false }
        'RebootMarkerNotGone' { $R.Reboots[1].MarkerGone = $false }
        'RebootNoRequiresReboot' { $R.Steps[1].Report.requiresReboot = $false }
        'FullCheckAfterApplyFails' { $R.CheckAfterApplyResult = 'Check zwrócił: False' }
        'FullCheckAfterUndoEmpty' { $R.CheckAfterUndoResult = $null }
        'RebootNoRestart' { $R.Reboots = @() }
        'RebootNoKeyCheck' { $R.KeysAfterUndo = @() }
        'RebootWrongAfterReboot' { $R.Steps[3].Report.state = 'NotApplied' }
        'AppRemoveStillListed' { $R.Steps[2].Report.apps = @([pscustomobject]@{ id = $Case.AppId; group = 'Other'; removability = 'Removable' }) }
        'AppRemoveWrongOutcome' { $R.Steps[1].Report.outcome = 'Failed' }
        'AppRemoveNoWarning' { $R.Steps[1].Report.warnings = @() }
        'AppRemoveWrongRemovability' { foreach ($App in @($R.Steps[0].Report.apps)) { $App.removability = 'Removable' } }
        'AppRemoveCheckFails' { $R.CheckResult = 'Check zwrócił: False' }
        'AppRemoveNoApps' { $R.Steps[0].Report = [pscustomobject]@{ exitCode = 0 } }
        'AppRemoveNoRequiresReboot' { $R.Steps[1].Report.PSObject.Properties.Remove('requiresReboot') }
        'AppInstallWrongOutcome' { if ($Case.Command -eq 'install-apps') { $R.Steps[0].Report.installItems[0].outcome = 'Installed' } }
        'AppInstallMissingItem' { if ($Case.Command -eq 'install-apps') { $R.Steps[0].Report.installItems = @($R.Steps[0].Report.installItems[0]) } }
        'AppInstallWrongInstalled' { if ($Case.Command -eq 'list-install') { $R.Steps[0].Report.installRows[0].installed = -not $R.Steps[0].Report.installRows[0].installed } }
        'AppInstallNoInstallRows' { if ($Case.Command -eq 'list-install') { $R.Steps[0].Report.PSObject.Properties.Remove('installRows') } }
        'AppInstallWrongWingetState' { $R.Steps[0].Report.winget.state = 'NotWorking' }
        'AppInstallNoMessage' { $R.Steps[0].Report.message = 'inny komunikat'; $R.Steps[0].Report.winget.message = 'winget' }
        'AppInstallWrongExit' { $R.Steps[0].ExitCode = 5 }
        'AppInstallCheckFails' { $R.CheckResult = 'Check zwrócił: False' }
        'AppInstallWrongWingetCode' { if ($Case.Command -eq 'install-apps') { $R.Steps[0].Report.installItems[0].wingetCode = '0x00000001' } }
        'AppInstallUnexpectedWingetCode' { if ($Case.Command -eq 'install-apps') { $R.Steps[0].Report.installItems[1].wingetCode = '0x8A150107' } }
    }
    return $R
}
# Granice z VM/Hyper-V: Invoke-Command (SID i hive), Copy-Item (logi) przekazują dalej wszystko inne.
function global:Invoke-Command {
    param($Session, $ScriptBlock, $ArgumentList)
    if (-not $PSBoundParameters.ContainsKey('Session')) {
        & (Get-Command Invoke-Command -CommandType Cmdlet) @PSBoundParameters
        return
    }
    if ($ScriptBlock.ToString() -match 'Get-LocalUser') { return $global:DsoSid }
    return $global:DsoHiveLoaded
}
function global:Copy-Item {
    param($Path, $Destination, $FromSession, [switch]$Recurse, [switch]$Force)
    if (-not $PSBoundParameters.ContainsKey('FromSession')) {
        & (Get-Command Copy-Item -CommandType Cmdlet) @PSBoundParameters
    }
}
$global:DsoHb = [System.Collections.Queue]::new()
$global:DsoReconnected = $false
function global:Get-VM {
    param($Name, $VMName, $ErrorAction)
    $Beat = if ($global:DsoHb.Count -gt 0) { $global:DsoHb.Dequeue() } else { 'OkApplicationsHealthy' }
    [pscustomobject]@{ Name = $Name; State = 'Off'; Heartbeat = $Beat }
}
function global:Start-Sleep { param($Seconds, $Milliseconds) }
function global:Remove-PSSession { param($Session, $ErrorAction) }
$global:DsoFakeSession = [System.Runtime.Serialization.FormatterServices]::GetUninitializedObject([System.Management.Automation.Runspaces.PSSession])
function global:New-PSSession { param($VMName, $Credential, $ErrorAction) $global:DsoFakeSession }
function global:Stop-VM { param($Name, [switch]$TurnOff, [switch]$Force) }
function global:Start-VM { param($Name) }
function global:Restore-VMCheckpoint { param($VMName, $Name, $Confirm) }
& (Get-Module VmHost) {
    if ($global:DsoVariant -ne 'ReconnectReal') { function script:Wait-VmReady { $null } }
    # Dry-run na atrapach: wyłącznik NoLiveVm (zmienna środowiskowa dziedziczona z Pestera) nie może blokować atrap.
    function script:Assert-DsoVmLiveAllowed { }
    function script:Get-VmBootObservation { , @() }
    function script:Copy-DsoBuildToVm { }
}

$Case = @{
    Name = 'dry'; TweakId = 'vmtest.hkcu-value'; Flow = 'Full'; RestorePoint = 'skip'
    Snapshot = @{ Registry = @(@{ Path = "HKU\$global:DsoSid\Software\X"; Name = 'V' }); Services = @() }
    Expect = @{
        Before = 'NotApplied'; ApplyExit = 0; ApplyOutcome = 'Applied'; AfterApply = 'Applied'
        UndoExit = 0; UndoOutcome = 'Undone'; AfterUndo = 'NotApplied'
        UserSid = $global:DsoSid; UserIsCertain = $true
        NotWrittenTo = @(@{ Path = 'HKCU\Software\X'; Name = 'V' })
        AfterApplySnapshot = @{ Registry = @(@{ Path = "HKU\$global:DsoSid\Software\X"; Name = 'V'; Exists = $true; Kind = 'String'; Data = 'v' }) }
    }
}
if ($Variant -in @('ArrayOk', 'ArrayBad')) {
    $Case.Expect.ApplyExit = @(0, 1)
    $Case.Expect.ApplyOutcome = @('Applied', 'FailedRolledBack')
}
if ($Variant -eq 'EmptyAllowed') { $Case.Expect.Before = '' }
if ($Variant -eq 'NoSnapshot') { $Case.Remove('Snapshot') }
$Cases = @($Case)
$global:DsoSkipInvoked = $false
if ($Variant -in @('ExecuteRequiresReboot', 'ExecuteNoRequiresReboot', 'ExecuteNoRequiresRebootField')) {
    $Cases = @(@{
            Name = 'execreboot'; TweakId = 'vmtest.action-marker'; Flow = 'Execute'; RestorePoint = 'skip'
            Check = '$true'
            Expect = @{ ExecuteExit = 0; ExecuteOutcome = 'Executed'; RequiresReboot = $true }
        })
}
if ($Variant -in @('Execute', 'ExecuteCheckFails', 'ExecuteCleanupFails')) {
    $Cases = @(@{
            Name = 'exec'; TweakId = 'vmtest.action-marker'; Flow = 'Execute'; RestorePoint = 'skip'
            Check = '$true'; Cleanup = 'Write-Output 1'
            Expect = @{ ExecuteExit = 0; ExecuteOutcome = 'Executed' }
        })
}
if ($Variant -in @('ExecuteFailedMessage', 'ExecuteFailedMessageWrong')) {
    $Cases = @(@{
            Name = 'execfail'; TweakId = 'vmtest.action-marker'; Flow = 'Execute'; RestorePoint = 'skip'
            Check = '$true'
            Expect = @{ ExecuteExit = 1; ExecuteOutcome = 'Failed'; MessageContains = 'brak aktywnego połączenia' }
        })
}
if ($Variant -in @('FullChecks', 'FullCheckAfterApplyFails', 'FullCheckAfterUndoEmpty')) {
    $Case.CheckAfterApply = '$true'
    $Case.CheckAfterUndo = '$true'
    $Case.Expect.KeyPresentAfterUndo = @('HKLM\SOFTWARE\DSOptimizer.VmTests')
    $Cases = @($Case)
}
if ($Variant -in @('Reboot', 'ReconnectReal', 'RebootKeyPresent', 'RebootNoRestart', 'RebootNoKeyCheck', 'RebootWrongAfterReboot', 'RebootKeyPresentMissing', 'RebootMarkerNotGone', 'RebootNoRequiresReboot')) {
    $Cases = @(@{
            Name = 'rb'; TweakId = 'vmtest.reboot-hklm'; Flow = 'Reboot'; RestorePoint = 'skip'
            CheckAfterApply = '$true'; CheckAfterUndo = '$true'
            Snapshot = @{ Registry = @(@{ Path = 'HKLM\SOFTWARE\DSOptimizer.VmTests\Reboot\Nowy'; Name = 'RebootDword' }); Services = @() }
            Expect = @{
                Before = 'NotApplied'; ApplyExit = 0; ApplyOutcome = @('Applied', 'PendingReboot'); AfterApply = @('Applied', 'NotApplied', 'Partial')
                AfterReboot = 'Applied'; UndoExit = 0; UndoOutcome = 'Undone'; AfterUndo = 'NotApplied'
                KeyAbsentAfterUndo = @('HKLM\SOFTWARE\DSOptimizer.VmTests\Reboot')
                KeyPresentAfterUndo = @('HKLM\SOFTWARE\DSOptimizer.VmTests')
            }
        })
}
if ($Variant -eq 'Skipped') {
    $Cases = @($Case, @{ Name = 'pominiety'; TweakId = 'vmtest.hklm-value'; Flow = 'DetectOnly'; RestorePoint = 'skip'; Skip = 'Przypadek nie do sprawdzenia w VM.'; Expect = @{ Before = 'NotApplied' } })
}
if ($Variant -like 'AppRemove*') {
    $Cases = @(
        @{
            Name = 'app-removed'; AppId = 'appx:Microsoft.BingNews_8wekyb3d8bbwe'; Flow = 'AppRemove'; RestorePoint = 'skip'
            Check = '$true'; Cleanup = 'Write-Output 1'
            Expect = @{
                ListedBefore = $true; Group = 'Suggested'; Removability = 'Removable'; RemoveExit = 0; RemoveOutcome = 'Removed'
                RequiresReboot = $true; ListedAfter = $false; WarningContains = 'Usunięta dla wszystkich kont'; WarningAbsent = 'nic takiego'
            }
        }
        @{
            Name = 'app-refused'; AppId = 'win32:HKU\{UserSid}\Software\Microsoft\Windows\CurrentVersion\Uninstall\DsoTestUser'; Flow = 'AppRemove'; RestorePoint = 'ifavailable'
            Check = '$true'
            Expect = @{
                ListedBefore = $true; Group = 'Other'; Removability = 'OtherAccount'; RemoveExit = 1; RemoveOutcome = 'Refused'
                ListedAfter = $true; MessageContains = 'nie wolno usunąć'; Aborted = $false; RestorePointStatus = 'ProtectionDisabled'
            }
        }
    )
}
if ($Variant -like 'AppInstall*') {
    $Cases = @(
        @{
            Name = 'inst-offline'; Flow = 'AppInstall'; Command = 'install-apps'; AppIds = @('install.7zip', 'install.vlc'); RunAs = 'dso'
            Check = '$true'; Cleanup = 'Write-Output 1'
            Expect = @{
                Exit = 1; Outcomes = @{ 'install.7zip' = 'Failed'; 'install.vlc' = @('Failed', 'Skipped') }; WingetState = 'Ready'; MessageContains = 'brak połączenia'
                WingetCodes = @{ 'install.7zip' = @('0x8A150107', '0x8A15000F'); 'install.vlc' = $null }
            }
        }
        @{
            Name = 'list-msi'; Flow = 'AppInstall'; Command = 'list-install'; AppsCatalog = 'C:\dso\fixtures\vm-test-apps.json'
            Check = '$true'
            Expect = @{ Exit = 0; WingetState = 'OtherAccount'; Installed = @{ 'install.dso-test-msi' = $true }; MessageContains = 'uruchom DSOptimizer na koncie' }
        }
    )
}
if ($Variant -eq 'RealCases') {
    $Cases = Import-VmCases -CasesPath (Join-Path $VmRoot 'cases.psd1') -CatalogPath (Join-Path $VmRoot 'catalog\vm-test-catalog.json')
}

if ($Variant -eq 'SkippedLiveIt') {
    # Kopia scenariusza bez -Skip na It-ach: blok ze Skip ma żywe testy, więc BeforeAll na pewno się wykona.
    $Dir = Join-Path ([System.IO.Path]::GetTempPath()) ('dso-live-' + [guid]::NewGuid().ToString('N'))
    $null = New-Item -ItemType Directory -Path $Dir -Force
    $Live = Join-Path $Dir 'Live.Tests.ps1'
    $Text = (Get-Content -LiteralPath (Join-Path $VmRoot 'scenarios\VmScenarios.Tests.ps1') -Raw -Encoding UTF8).Replace(' -Skip:$IsSkipped', '')
    [System.IO.File]::WriteAllText($Live, $Text, (New-Object System.Text.UTF8Encoding($true)))
    $SkipCase = @{ Name = 'pominiety'; TweakId = 'vmtest.hklm-value'; Flow = 'DetectOnly'; RestorePoint = 'skip'; Skip = 'Powod pominiecia.'; Expect = @{ Before = 'NotApplied' } }
    $Records = Join-Path $Dir 'records'
    $Container = New-PesterContainer -Path $Live -Data @{ Session = $null; Cases = @($SkipCase); Layout = (Get-DsoVmLayout); RecordDirectory = $Records }
    $Configuration = New-PesterConfiguration
    $Configuration.Run.Container = $Container
    $Configuration.Run.PassThru = $true
    $Configuration.Output.Verbosity = 'None'
    $null = Invoke-Pester -Configuration $Configuration
    $Saved = Get-Content -LiteralPath (Join-Path $Records 'pominiety.json') -Raw -Encoding UTF8 | ConvertFrom-Json
    Remove-Item -LiteralPath $Dir -Recurse -Force -ErrorAction SilentlyContinue
    Write-Output ('DSORESULT:' + (@{ skipInvoked = [bool]$global:DsoSkipInvoked; recordSkipped = [bool]$Saved.Skipped; recordReason = [string]$Saved.SkipReason } | ConvertTo-Json -Compress))
    return
}

$OutDirectory = Join-Path ([System.IO.Path]::GetTempPath()) ('dso-dry-' + [guid]::NewGuid().ToString('N'))
$null = New-Item -ItemType Directory -Path $OutDirectory -Force
$Summary = @{ threw = $null; passed = 0; failed = 0; skipped = 0; skippedRecords = 0; skipInvoked = $false; reconnected = $false; messages = @(); cases = 0 }
try {
    $Credential = [pscredential]::new('Administrator', (ConvertTo-SecureString 'Test-Haslo-1!' -AsPlainText -Force))
    $Run = Invoke-DsoVmRun -VmName 'DSO-26H2-Test' -Credential $Credential -BuildDirectory $OutDirectory -CatalogPath (Join-Path $OutDirectory 'k.json') `
        -Cases $Cases -OutDirectory $OutDirectory -ReadyTimeoutSeconds 60
    $Report = Get-Content -LiteralPath $Run.ReportPath -Raw -Encoding UTF8 | ConvertFrom-Json
    $Summary.passed = $Run.Passed
    $Summary.failed = $Run.Failed
    $Summary.skipped = $Run.Skipped
    $Summary.skipInvoked = [bool]$global:DsoSkipInvoked
    $Summary.reconnected = [bool]$global:DsoReconnected
    $Summary.messages = @($Report.pester.failedTests | ForEach-Object { "$($_.name): $($_.error)" })
    $Summary.cases = @($Report.cases).Count
    $Summary.skippedRecords = @($Report.cases | Where-Object { $_.Skipped -eq $true -and $_.SkipReason }).Count
}
catch {
    $Summary.threw = $_.Exception.Message
}
finally {
    Remove-Item -LiteralPath $OutDirectory -Recurse -Force -ErrorAction SilentlyContinue
}
Write-Output ('DSORESULT:' + ($Summary | ConvertTo-Json -Depth 5 -Compress))
