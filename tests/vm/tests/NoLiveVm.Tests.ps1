BeforeDiscovery {
    $script:HasHyperV = [bool](Get-Module -ListAvailable -Name Hyper-V)
}

BeforeAll {
    # Wyłącznik: żaden test na mockach nie może dotknąć prawdziwego Hyper-V (patrz Support\NoLiveVm.ps1).
    . (Join-Path $PSScriptRoot 'Support\NoLiveVm.ps1')
    Import-Module (Join-Path $PSScriptRoot '..\modules\VmCommon.psm1') -Force
    Import-Module (Join-Path $PSScriptRoot '..\modules\VmRun.psm1') -Force
    Import-Module (Join-Path $PSScriptRoot '..\modules\VmHost.psm1') -Force
    Import-Module (Join-Path $PSScriptRoot '..\explore\ExploreCommon.psm1') -Force
    $script:Credential = [pscredential]::new('Administrator', (ConvertTo-SecureString 'Test-Haslo-1!' -AsPlainText -Force))
}

Describe 'Assert-DsoVmLiveAllowed (wyłącznik niezależny od mocków)' {
    It 'rzuca, gdy DSO_VM_TESTS_MOCK_ONLY jest ustawione (NoLiveVm.ps1 ustawia je w BeforeAll)' {
        $env:DSO_VM_TESTS_MOCK_ONLY | Should -Not -BeNullOrEmpty
        { Assert-DsoVmLiveAllowed } | Should -Throw '*DSO_VM_TESTS_MOCK_ONLY*'
    }
    It 'nie rzuca, gdy zmienna nie jest ustawiona' {
        $Saved = $env:DSO_VM_TESTS_MOCK_ONLY
        try {
            [Environment]::SetEnvironmentVariable('DSO_VM_TESTS_MOCK_ONLY', $null)
            { Assert-DsoVmLiveAllowed } | Should -Not -Throw
        }
        finally { $env:DSO_VM_TESTS_MOCK_ONLY = $Saved }
    }
}

Describe 'Set-NoLiveVmDefaultMock: rzucające mocki dysków hosta (VHDX nośnika E5)' -Skip:(-not $script:HasHyperV) {
    It 'bez -ModuleName (zasięg testu) i z -ModuleName: New-VHD, Mount-VHD, Dismount-VHD, Initialize-Disk, New-Partition, Format-Volume i Set-Disk rzucają bez jawnego mocka' {
        Set-NoLiveVmDefaultMock
        # Argumenty celowo wskazują nieistniejące obiekty (gdyby mock nie zadziałał, wywołanie i tak niczego by nie zmieniło).
        $Missing = Join-Path $TestDrive 'brak.vhdx'
        { New-VHD -Path $Missing -SizeBytes 1MB -Dynamic } | Should -Throw '*NoLiveVm*'
        { Mount-VHD -Path $Missing } | Should -Throw '*NoLiveVm*'
        { Dismount-VHD -Path $Missing } | Should -Throw '*NoLiveVm*'
        { Initialize-Disk -Number 99999 -PartitionStyle MBR } | Should -Throw '*NoLiveVm*'
        { New-Partition -DiskNumber 99999 -UseMaximumSize } | Should -Throw '*NoLiveVm*'
        { Format-Volume -Path '\\?\Volume{00000000-0000-0000-0000-000000000000}\' -FileSystem exFAT -Confirm:$false } | Should -Throw '*NoLiveVm*'
        { Set-Disk -Number 99999 -IsOffline $false } | Should -Throw '*NoLiveVm*'
        Test-Path -LiteralPath $Missing | Should -BeFalse
        foreach ($Command in 'New-VHD', 'Mount-VHD', 'Dismount-VHD', 'Initialize-Disk', 'New-Partition', 'Format-Volume', 'Set-Disk') { Should -Invoke $Command -Times 1 -Exactly }
    }
    It 'z -ModuleName ExploreCommon: mocki dysków w zasięgu modułu; jawny późniejszy Mock nadal wygrywa' {
        Set-NoLiveVmDefaultMock -ModuleName ExploreCommon
        { & (Get-Module ExploreCommon) { Initialize-Disk -Number 99999 -PartitionStyle MBR } } | Should -Throw '*NoLiveVm*'
        Mock -ModuleName ExploreCommon Initialize-Disk { 'jawny' }
        & (Get-Module ExploreCommon) { Initialize-Disk -Number 99999 -PartitionStyle MBR } | Should -Be 'jawny'
    }
}

Describe 'Wyłącznik blokuje funkcje dotykające Hyper-V zanim cokolwiek wywołają' -Skip:(-not $script:HasHyperV) {
    BeforeEach {
        Set-NoLiveVmDefaultMock -ModuleName VmHost
        Set-NoLiveVmDefaultMock -ModuleName ExploreCommon
    }
    It 'Restore-VmCleanState rzuca przed Get-VM / Stop-VM / Restore-VMCheckpoint' {
        { Restore-VmCleanState -VmName 'DSO-26H2-Test' -CheckpointName 'czysta-26H2' } | Should -Throw '*DSO_VM_TESTS_MOCK_ONLY*'
        Should -Invoke -ModuleName VmHost Get-VM -Times 0 -Exactly
        Should -Invoke -ModuleName VmHost Stop-VM -Times 0 -Exactly
        Should -Invoke -ModuleName VmHost Restore-VMCheckpoint -Times 0 -Exactly
    }
    It 'Wait-VmReady rzuca przed Get-VM / New-PSSession' {
        { Wait-VmReady -VmName 'DSO-26H2-Test' -Credential $script:Credential -TimeoutSeconds 1 } | Should -Throw '*DSO_VM_TESTS_MOCK_ONLY*'
        Should -Invoke -ModuleName VmHost Get-VM -Times 0 -Exactly
        Should -Invoke -ModuleName VmHost New-PSSession -Times 0 -Exactly
    }
    It 'Wait-VmHeartbeatDrop rzuca przed Get-VM' {
        { Wait-VmHeartbeatDrop -VmName 'DSO-26H2-Test' } | Should -Throw '*DSO_VM_TESTS_MOCK_ONLY*'
        Should -Invoke -ModuleName VmHost Get-VM -Times 0 -Exactly
    }
    It 'Copy-DsoBuildToVm rzuca przed Invoke-Command' {
        Mock -ModuleName VmHost Copy-Item -RemoveParameterValidation 'ToSession' -MockWith {}
        { Copy-DsoBuildToVm -Session $null -BuildDirectory 'C:\b' -CatalogPath 'C:\k.json' -Layout (Get-DsoVmLayout) } | Should -Throw '*DSO_VM_TESTS_MOCK_ONLY*'
        Should -Invoke -ModuleName VmHost Copy-Item -Times 0 -Exactly
    }
    It 'Invoke-ExploreVmRun rzuca przed Get-DsoVmCredential i przed jakąkolwiek operacją VM' {
        { Invoke-ExploreVmRun -VmName 'DSO-26H2-Pro' -OutDirectory $TestDrive -FileName 'a.json' -RemoteScript 'x' -EntryCommand 'E' } | Should -Throw '*DSO_VM_TESTS_MOCK_ONLY*'
        Should -Invoke -ModuleName ExploreCommon Get-DsoVmCredential -Times 0 -Exactly
        Should -Invoke -ModuleName ExploreCommon Start-VM -Times 0 -Exactly
        Should -Invoke -ModuleName VmHost Get-VM -Times 0 -Exactly
        Should -Invoke -ModuleName VmHost Restore-VMCheckpoint -Times 0 -Exactly
    }
}

Describe 'Domyślne rzucające mocki (Set-NoLiveVmDefaultMock)' -Skip:(-not $script:HasHyperV) {
    BeforeEach { Set-NoLiveVmDefaultMock -ModuleName VmHost }
    It '<Polecenie> rzuca, dopóki test go nie nadpisze' -ForEach @(
        @{ Polecenie = 'Get-VM'; Kod = 'Get-VM -Name ''NoSuchVm-NoLive''' }
        @{ Polecenie = 'Start-VM'; Kod = 'Start-VM -Name ''NoSuchVm-NoLive''' }
        @{ Polecenie = 'Stop-VM'; Kod = 'Stop-VM -Name ''NoSuchVm-NoLive''' }
        @{ Polecenie = 'Restore-VMCheckpoint'; Kod = 'Restore-VMCheckpoint -VMName ''NoSuchVm-NoLive'' -Name ''x'' -Confirm:$false' }
        @{ Polecenie = 'New-PSSession'; Kod = 'New-PSSession -VMName ''NoSuchVm-NoLive'' -Credential ([pscredential]::new(''a'', (ConvertTo-SecureString ''x'' -AsPlainText -Force)))' }
        @{ Polecenie = 'Get-DsoVmCredential'; Kod = 'Get-DsoVmCredential -VmName ''DSO-26H2-Test''' }
        @{ Polecenie = 'Invoke-Command -VMName'; Kod = 'Invoke-Command -VMName ''NoSuchVm-NoLive'' -Credential ([pscredential]::new(''a'', (ConvertTo-SecureString ''x'' -AsPlainText -Force))) -ScriptBlock { 1 }' }
    ) {
        # Kod wykonywany w zasięgu modułu VmHost, czyli tam, gdzie mocki -ModuleName VmHost działają.
        { InModuleScope VmHost -Parameters @{ Kod = $Kod } { & ([scriptblock]::Create($Kod)) } } | Should -Throw '*NoLiveVm*'
    }
}

AfterAll { Clear-NoLiveVm }
