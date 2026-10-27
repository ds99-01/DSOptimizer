# Wspólny wyłącznik bezpieczeństwa testów Pester w tests/vm/tests (dot-source w BeforeAll KAŻDEGO pliku testów).
# Ustawia $env:DSO_VM_TESTS_MOCK_ONLY: od tej chwili Assert-DsoVmLiveAllowed (VmHost) rzuca, więc żaden test na mockach
# nie dotknie prawdziwego Hyper-V / PowerShell Direct, nawet gdy mock nie trafi (zły -ModuleName, przeładowanie modułu,
# nowy test bez mocka). Czyszczenie: Clear-NoLiveVm w AfterAll.
$script:NoLiveVmPrevious = [Environment]::GetEnvironmentVariable('DSO_VM_TESTS_MOCK_ONLY')
$env:DSO_VM_TESTS_MOCK_ONLY = '1'

function Clear-NoLiveVm {
    # Przywraca poprzednią wartość (np. '1' ustawione przez zewnętrzny proces uruchamiający Pester).
    [Environment]::SetEnvironmentVariable('DSO_VM_TESTS_MOCK_ONLY', $script:NoLiveVmPrevious)
}

function Set-NoLiveVmDefaultMock {
    # Domyślne RZUCAJĄCE mocki wszystkiego, co dotyka Hyper-V / PowerShell Direct i dysków hosta, w zasięgu danego modułu (bez
    # -ModuleName: zasięg testu – skrypty dot-source'owane, np. Invoke-E5Exploration.ps1). Wołać w BeforeEach Describe'ów
    # z orkiestracją; test nadpisuje tylko to, czego potrzebuje (późniejszy Mock tej samej komendy wygrywa).
    param([string]$ModuleName)
    $Scope = @{}
    if ($ModuleName) { $Scope.ModuleName = $ModuleName }
    # Dyski hosta (VHDX nośnika w E5): bez jawnego mocka żaden test nie utworzy, nie podłączy ani nie sformatuje dysku.
    # Typy CIM parametrów zdejmowane już tutaj: pierwszy Mock ustala sygnaturę, a jawne mocki testów podają obiekty zastępcze.
    $CimParameters = @{ 'Initialize-Disk' = @('InputObject'); 'New-Partition' = @('InputObject'); 'Format-Volume' = @('Partition', 'InputObject'); 'Set-Disk' = @('InputObject') }
    foreach ($DiskCommand in 'New-VHD', 'Mount-VHD', 'Dismount-VHD', 'Initialize-Disk', 'New-Partition', 'Format-Volume', 'Set-Disk') {
        if (-not (Get-Command -Name $DiskCommand -ErrorAction SilentlyContinue)) { continue }
        $Message = "NoLiveVm: nieoczekiwane wywołanie prawdziwego $DiskCommand w teście na mockach."
        $Extra = @{}
        if ($CimParameters.ContainsKey($DiskCommand)) { $Extra.RemoveParameterType = $CimParameters[$DiskCommand] }
        Mock @Scope @Extra -CommandName $DiskCommand -MockWith ([scriptblock]::Create("throw '$Message'"))
    }
    if (-not $ModuleName) { return }
    Mock -ModuleName $ModuleName Get-VM { throw 'NoLiveVm: nieoczekiwane wywołanie prawdziwego Get-VM w teście na mockach.' }
    Mock -ModuleName $ModuleName Start-VM { throw 'NoLiveVm: nieoczekiwane wywołanie prawdziwego Start-VM w teście na mockach.' }
    Mock -ModuleName $ModuleName Stop-VM { throw 'NoLiveVm: nieoczekiwane wywołanie prawdziwego Stop-VM w teście na mockach.' }
    Mock -ModuleName $ModuleName Restore-VMCheckpoint { throw 'NoLiveVm: nieoczekiwane wywołanie prawdziwego Restore-VMCheckpoint w teście na mockach.' }
    Mock -ModuleName $ModuleName New-PSSession { throw 'NoLiveVm: nieoczekiwane wywołanie prawdziwego New-PSSession w teście na mockach.' }
    Mock -ModuleName $ModuleName Get-DsoVmCredential { throw 'NoLiveVm: nieoczekiwane wywołanie prawdziwego Get-DsoVmCredential w teście na mockach.' }
    # Filtr po $PesterBoundParameters, a nie po $VMName: zmienna o tej nazwie bywa widoczna z zasięgu wołającego (parametr funkcji)
    # i dawała fałszywe trafienia. Wywołania bez -VMName obsługuje mock ustawiony przez test (Invoke-Command -Session).
    Mock -ModuleName $ModuleName Invoke-Command -RemoveParameterValidation 'Session' -ParameterFilter { $PesterBoundParameters.ContainsKey('VMName') } -MockWith {
        throw 'NoLiveVm: nieoczekiwane wywołanie Invoke-Command -VMName w teście na mockach.'
    }
}