<#
Instrukcja uruchomienia:
    Sesja PowerShell uruchomiona jako administrator, na hoście z Hyper-V, z modułami testów VM (tests\vm\modules) i tests\vm\explore\ExploreCommon.psm1.
    VM utworzone wcześniej przez New-TestVm.ps1 (checkpoint 'czysta-26H2', poświadczenia w %LOCALAPPDATA%\DSOptimizer-vm, autologon 'dso').
    Obie VM (Home, potem Pro), bez sieci:
        .\tests\vm\explore\Invoke-E4A2Exploration.ps1 -OutDirectory C:\ścieżka\poza\repo\exploration
    Jedna VM, z siecią (Default Switch na czas przebiegu):
        .\tests\vm\explore\Invoke-E4A2Exploration.ps1 -VmName DSO-26H2-Pro -Network -OutDirectory C:\...\exploration
    -ShowPlan: wypisuje rozstrzygnięte argumenty i plan (JSON) i kończy, nic nie uruchamia.
    -OutDirectory jest wymagany: ścieżka bezwzględna poza repo; w repo tylko katalog ignorowany przez git.
    Czas: ok. 8-12 minut na VM.
Opis działania:
    Pomiar partii 2 Etapu 4a (strona „Aplikacje”): co jest zainstalowane na świeżym 26H2 i jak to usuwać z procesu administratora
    dla zalogowanego konta. Wszystko dzieje się WYŁĄCZNIE w VM DSO-26H2-Home/Pro; na hoście skrypt tylko (opcjonalnie) podłącza i odłącza
    kartę sieciową VM (Default Switch), robi zrzut ekranu VM (GetVirtualSystemThumbnailImage) i zapisuje wyniki.
    Przebieg jednej VM: revert do 'czysta-26H2' -> start -> gotowość (zalogowany dso) -> fazy -> kopia zrzutów -> ZAWSZE (finally) revert,
    odłączenie sieci i sprawdzenie stanu końcowego (VM Off, karta bez przełącznika) przed zapisem JSON. Błąd sprzątania trafia do wyniku.
    Konteksty w VM:
      K1 - PowerShell Direct jako wbudowany Administrator (jak proces aplikacji podniesionej na innym koncie niż zalogowane):
           Get-AppxPackage -User <SID dso> (czas: pierwszy i drugi odczyt), manifest pakietu (DisplayName, w tym ms-resource:) i jego rozwinięcie
           przez SHLoadIndirectString, WinRT PackageManager.FindPackagesForUser(<SID>) z Package.DisplayName, Get-AppxProvisionedPackage -Online,
           wpisy Uninstall (HKLM 64, WOW6432Node, HKU\<SID dso>: wszystkie wartości), stan ochrony systemu C: (rejestr SPP\Clients, WMI
           SystemRestore/SystemRestoreConfig, Win32_ShadowStorage, vssadmin list shadowstorage, usługi VSS/swprv), csc.exe (kompilacja
           i uruchomienie programu testowego) i COM WindowsInstaller.Installer (utworzenie pustej bazy MSI w C:\dso\e4a2).
      K2 - sesja interaktywna 'dso': zadanie Harmonogramu (principal dso / Interactive / Highest): Get-AppxPackage własnego konta,
           Get-StartApps, WinRT FindPackagesForUser(''), wyszukiwanie w Start (klawisz Win + fraza, zrzut Graphics.CopyFromScreen),
           usunięcie pakietu własnego konta (Remove-AppxPackage bez -User i z -User <SID dso>).
    Fazy: inventory (spis) -> search 'przed' (+ zrzut hosta) -> removal (Weather: WinRT RemovePackageAsync procesu, -User <SID>, 60 s odczytów
    i Start dso, gdy pakiet zostaje dla dso -> -AllUsers; News: z konta dso bez -User; Solitaire: z konta dso z -User <własny SID>;
    To Do: -User <SID>; Weather/News/Solitaire potem Remove-AppxProvisionedPackage -Online; stan dso / obraz / wszyscy po każdym kroku) ->
    search 'po' (+ zrzut hosta) -> protection (odczyt, Enable-ComputerRestore C:, odczyt, Disable-ComputerRestore C:, odczyt: sprawdzenie,
    czy odczyt rozróżnia oba stany) -> logoff (wylogowanie dso, czy „pending removal” To Do po -User <SID> dokańcza się); wszystko cofa revert.
    Ostatni bezpiecznik po stronie VM: Assert-E4A2InsideVm (nazwa komputera DSO-26H2-HOME/PRO i brak DSO_VM_TESTS_MOCK_ONLY) na początku
    każdej funkcji ze skutkiem. Pierwszy błąd uwierzytelnienia przerywa cały skrypt (blokada konta). Skrypt dotyka wyłącznie VM
    DSO-26H2-Home i DSO-26H2-Pro (ValidateSet), nigdy Bruteforce_LTSC; na hoście niczego nie usuwa.
    Wynik: <OutDirectory>\<VmName>\e4a2.json (UTF-8), zrzuty K2 <OutDirectory>\<VmName>\shots\*.png i zrzuty hosta host-start-*.png.
    Kod wyjścia: 0 gdy wszystkie wybrane VM zebrane bez błędu przebiegu, 1 w przeciwnym razie.
#>
[CmdletBinding()]
param(
    [ValidateSet('DSO-26H2-Home', 'DSO-26H2-Pro')][string[]]$VmName = @('DSO-26H2-Home', 'DSO-26H2-Pro'),
    [string]$OutDirectory,
    [string]$CheckpointName = 'czysta-26H2',
    [ValidateRange(60, 7200)][int]$ReadyTimeoutSeconds = 1200,
    [switch]$Network,
    [switch]$ShowPlan
)

Import-Module (Join-Path $PSScriptRoot 'ExploreCommon.psm1') -Force
Import-Module (Join-Path (Split-Path -Parent $PSScriptRoot) 'modules\VmSmoke.psm1')

# ---------------------------------------------------------------------------------------------------------------------
# Plan (dane z planu Tasku 2). {SID} w ścieżce = SID konta 'dso' (wstawiany w VM).
# Methods (po kolei): WinRtProcess = PackageManager.RemovePackageAsync(<pełna>) w procesie Administratora; UserSid = Remove-AppxPackage -Package
# <pełna> -User <SID> z procesu Administratora; OwnUser = Remove-AppxPackage bez -User z konta dso (K2); OwnUserSid = to samo z -User <SID dso> (K2).
# PollSeconds: gdy po metodach pakiet dalej jest dla dso, odczyt stanu co PollIntervalSeconds przez tyle sekund + Start dso (Get-StartApps).
# Fallback: metoda, gdy po odpytywaniu pakiet dalej jest dla dso; Deprovision: potem Remove-AppxProvisionedPackage -Online -PackageName.
# Search: cel wyszukiwany w Start przed i po; Phrase: fraza zapasowa (gdy nazwy z Get-StartApps nie da się wpisać przez SendKeys).
# LogoffTarget: pakiet usunięty przez -User <SID> bez Fallback; w fazie logoff wylogowanie dso i odczyt, czy usunięcie się dokończyło.
# ---------------------------------------------------------------------------------------------------------------------

function Get-E4A2Plan {
    [CmdletBinding()]
    param()
    return [ordered]@{
        Targets         = @(
            [ordered]@{ Name = 'Microsoft.BingWeather'; Methods = @('WinRtProcess', 'UserSid'); PollSeconds = 60; Fallback = 'AllUsers'; Deprovision = $true; Search = $true; Phrase = 'Pogoda' }
            [ordered]@{ Name = 'Microsoft.BingNews'; Methods = @('OwnUser'); PollSeconds = 0; Fallback = $null; Deprovision = $true; Search = $true; Phrase = 'News' }
            [ordered]@{ Name = 'Microsoft.MicrosoftSolitaireCollection'; Methods = @('OwnUserSid'); PollSeconds = 0; Fallback = $null; Deprovision = $true; Search = $false; Phrase = 'Solitaire' }
            [ordered]@{ Name = 'Microsoft.Todos'; Methods = @('UserSid'); PollSeconds = 0; Fallback = $null; Deprovision = $false; Search = $false; Phrase = 'To Do' }
        )
        LogoffTarget    = 'Microsoft.Todos'
        UninstallRoots  = @(
            'HKLM\SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall'
            'HKLM\SOFTWARE\WOW6432Node\Microsoft\Windows\CurrentVersion\Uninstall'
            'HKU\{SID}\Software\Microsoft\Windows\CurrentVersion\Uninstall'
        )
        CscRelativePath = 'Microsoft.NET\Framework64\v4.0.30319\csc.exe'
        InstallerProgId = 'WindowsInstaller.Installer'
    }
}

# ---------------------------------------------------------------------------------------------------------------------
# Funkcje wysyłane do VM (samowystarczalne: wołają siebie nawzajem, funkcje Explore z modułu i cmdlety systemowe).
# ---------------------------------------------------------------------------------------------------------------------

function Test-E4A2VmComputerName {
    # Nazwy komputerów VM nadawane przez New-TestVm.ps1 (VmBuild: nazwa VM wielkimi literami).
    [CmdletBinding()]
    [OutputType([bool])]
    param([AllowEmptyString()][string]$ComputerName)
    return (@('DSO-26H2-HOME', 'DSO-26H2-PRO') -contains ([string]$ComputerName).ToUpperInvariant())
}

function Assert-E4A2InsideVm {
    # Ostatni bezpiecznik: sondy E4A2 usuwają pakiety AppX, zmieniają ochronę systemu, kompilują programy i rejestrują zadania, więc działają
    # WYŁĄCZNIE w VM DSO-26H2-Home/Pro i nigdy w testach na mockach (DSO_VM_TESTS_MOCK_ONLY z tests\vm\tests\Support\NoLiveVm.ps1).
    [CmdletBinding()]
    param([string]$ComputerName = $env:COMPUTERNAME)
    if ($env:DSO_VM_TESTS_MOCK_ONLY) {
        throw 'Tryb DSO_VM_TESTS_MOCK_ONLY: sondy E4A2 nie mogą działać w testach na mockach (nic nie zostało uruchomione).'
    }
    if (-not (Test-E4A2VmComputerName -ComputerName $ComputerName)) {
        throw "Sondy E4A2 działają tylko w VM DSO-26H2-HOME/DSO-26H2-PRO (ten komputer: '$ComputerName'); nic nie zostało uruchomione."
    }
}

function Resolve-E4A2Path {
    # {SID} -> SID konta dso; tylko HKLM\ i HKU\ (jak Open-ExploreRegistryKey).
    [CmdletBinding()]
    [OutputType([string])]
    param(
        [Parameter(Mandatory)][string]$Path,
        [Parameter(Mandatory)][string]$UserSid
    )
    $Resolved = $Path.Replace('{SID}', $UserSid)
    if ($Resolved -notmatch '^(HKLM|HKU)\\') { throw "Resolve-E4A2Path: dozwolone tylko 'HKLM\' i 'HKU\' (podano '$Path')." }
    return $Resolved
}

function ConvertTo-E4A2ResourceReference {
    # DisplayName z manifestu w postaci ms-resource: -> kandydaci dla SHLoadIndirectString (@{<pełna nazwa>?ms-resource://...}).
    # Zwykły tekst (bez ms-resource:) = brak kandydatów (nazwa jest gotowa).
    [CmdletBinding()]
    [OutputType([string[]])]
    param(
        [AllowNull()][AllowEmptyString()][string]$Raw,
        [Parameter(Mandatory)][string]$PackageFullName,
        [Parameter(Mandatory)][string]$PackageName
    )
    if ([string]::IsNullOrEmpty($Raw) -or -not $Raw.StartsWith('ms-resource:', [System.StringComparison]::OrdinalIgnoreCase)) { return @() }
    $Rest = $Raw.Substring('ms-resource:'.Length)
    if ($Rest.StartsWith('//')) { return @("@{${PackageFullName}?ms-resource:$Rest}") }
    if ($Rest.StartsWith('/')) { return @("@{${PackageFullName}?ms-resource://$PackageName$Rest}") }
    return @("@{${PackageFullName}?ms-resource://$PackageName/Resources/$Rest}", "@{${PackageFullName}?ms-resource://$PackageName/$Rest}")
}

function Test-E4A2SppVolume {
    # Czy lista wpisów SPP\Clients (wartość {09F7EDC5-294E-4180-AF6A-FB0E6A0E9513}) zawiera GUID woluminu (DeviceID z Win32_Volume).
    [CmdletBinding()]
    [OutputType([bool])]
    param(
        [AllowNull()][AllowEmptyCollection()][string[]]$Entries,
        [AllowNull()][AllowEmptyString()][string]$VolumeId
    )
    if ([string]::IsNullOrEmpty($VolumeId)) { return $false }
    $Guid = [regex]::Match($VolumeId, '\{[0-9A-Fa-f-]{36}\}').Value
    if ([string]::IsNullOrEmpty($Guid)) { return $false }
    foreach ($Entry in @($Entries)) {
        if ($Entry -and $Entry.IndexOf($Guid, [System.StringComparison]::OrdinalIgnoreCase) -ge 0) { return $true }
    }
    return $false
}

function Select-E4A2Present {
    # Pierwszy pakiet o dokładnie tej nazwie albo $null.
    [CmdletBinding()]
    param(
        [AllowNull()][AllowEmptyCollection()][object[]]$Packages,
        [Parameter(Mandatory)][string]$Name
    )
    $Hit = @($Packages) | Where-Object { $null -ne $_ -and $_.Name -eq $Name } | Select-Object -First 1
    return $Hit
}

function Initialize-E4A2Native {
    # SHLoadIndirectString (shlwapi): rozwinięcie @{<pakiet>?ms-resource://...} w procesie Administratora; WTSLogoffSession (wtsapi32):
    # wylogowanie sesji dso także na Home (bez logoff.exe).
    [CmdletBinding()]
    param()
    # System.Runtime.WindowsRuntime: AsTask dla operacji WinRT (PackageManager.RemovePackageAsync).
    Add-Type -AssemblyName System.Runtime.WindowsRuntime
    if ('E4A2Native' -as [type]) { return }
    Add-Type -TypeDefinition @'
using System;
using System.Text;
using System.Runtime.InteropServices;
public static class E4A2Native {
    [DllImport("shlwapi.dll", CharSet = CharSet.Unicode)]
    public static extern int SHLoadIndirectString(string source, StringBuilder buffer, int size, IntPtr reserved);
    [DllImport("wtsapi32.dll", SetLastError = true)]
    public static extern bool WTSLogoffSession(IntPtr server, int sessionId, bool wait);
}
'@
}

function Resolve-E4A2DisplayName {
    # {Raw, Value, Via, Attempts}: tekst bez ms-resource: = Value od razu (Via=literal); inaczej kolejni kandydaci przez SHLoadIndirectString.
    [CmdletBinding()]
    param(
        [AllowNull()][AllowEmptyString()][string]$Raw,
        [Parameter(Mandatory)][string]$PackageFullName,
        [Parameter(Mandatory)][string]$PackageName
    )
    $Out = [ordered]@{ Raw = $Raw; Value = $null; Via = $null; Attempts = @() }
    if ([string]::IsNullOrEmpty($Raw)) { return [pscustomobject]$Out }
    $Candidates = @(ConvertTo-E4A2ResourceReference -Raw $Raw -PackageFullName $PackageFullName -PackageName $PackageName)
    if ($Candidates.Count -eq 0) {
        $Out.Value = $Raw
        $Out.Via = 'literal'
        return [pscustomobject]$Out
    }
    Initialize-E4A2Native
    foreach ($Candidate in $Candidates) {
        $Buffer = New-Object System.Text.StringBuilder 1024
        $Hr = [E4A2Native]::SHLoadIndirectString($Candidate, $Buffer, $Buffer.Capacity, [IntPtr]::Zero)
        if ($Hr -eq 0 -and $Buffer.Length -gt 0) {
            $Out.Value = $Buffer.ToString()
            $Out.Via = $Candidate
            break
        }
        $Out.Attempts += ('0x{0:X8} {1}' -f $Hr, $Candidate)
    }
    return [pscustomobject]$Out
}

function Get-E4A2ManifestNames {
    # Nazwy z AppxManifest.xml w katalogu pakietu (odczyt pliku jak w C#): Properties/DisplayName, PublisherDisplayName i aplikacje
    # (Application Id, VisualElements DisplayName, AppListEntry). Wartości surowe, także ms-resource:.
    [CmdletBinding()]
    param([AllowNull()][AllowEmptyString()][string]$InstallLocation)
    $Out = [ordered]@{ DisplayName = $null; PublisherDisplayName = $null; Applications = @(); Error = $null }
    try {
        if ([string]::IsNullOrEmpty($InstallLocation)) { throw 'brak InstallLocation' }
        $Path = Join-Path $InstallLocation 'AppxManifest.xml'
        if (-not (Test-Path -LiteralPath $Path)) { throw "brak pliku $Path" }
        $Xml = New-Object System.Xml.XmlDocument
        $Xml.Load($Path)
        $Properties = $Xml.SelectSingleNode("/*[local-name()='Package']/*[local-name()='Properties']")
        if ($null -ne $Properties) {
            $Node = $Properties.SelectSingleNode("*[local-name()='DisplayName']")
            if ($null -ne $Node) { $Out.DisplayName = $Node.InnerText }
            $Node = $Properties.SelectSingleNode("*[local-name()='PublisherDisplayName']")
            if ($null -ne $Node) { $Out.PublisherDisplayName = $Node.InnerText }
        }
        $Apps = @()
        foreach ($App in @($Xml.SelectNodes("/*[local-name()='Package']/*[local-name()='Applications']/*[local-name()='Application']"))) {
            $Visual = $App.SelectSingleNode("*[local-name()='VisualElements']")
            $Apps += [pscustomobject]@{
                Id           = $App.GetAttribute('Id')
                DisplayName  = if ($null -ne $Visual) { $Visual.GetAttribute('DisplayName') } else { $null }
                AppListEntry = if ($null -ne $Visual) { $Visual.GetAttribute('AppListEntry') } else { $null }
            }
        }
        $Out.Applications = $Apps
    }
    catch { $Out.Error = $_.Exception.Message }
    return [pscustomobject]$Out
}

function Get-E4A2WinRtPackages {
    # WinRT PackageManager.FindPackagesForUser(<SID>; '' = konto procesu): pełna nazwa, Package.DisplayName/PublisherDisplayName i flagi.
    [CmdletBinding()]
    param([AllowEmptyString()][string]$UserSid = '')
    $Out = [ordered]@{ UserSid = $UserSid; Ms = $null; Count = $null; Error = $null; Packages = @() }
    $Watch = [System.Diagnostics.Stopwatch]::StartNew()
    try {
        $null = [Windows.Management.Deployment.PackageManager, Windows.Management.Deployment, ContentType = WindowsRuntime]
        $Manager = New-Object -TypeName Windows.Management.Deployment.PackageManager
        $Found = @($Manager.FindPackagesForUser($UserSid))
        $Records = @()
        foreach ($Package in $Found) {
            $Record = [ordered]@{ FullName = $null; DisplayName = $null; PublisherDisplayName = $null; SignatureKind = $null; IsFramework = $null; IsResourcePackage = $null; IsBundle = $null; IsStub = $null; Errors = @() }
            try { $Record.FullName = [string]$Package.Id.FullName } catch { $Record.Errors += "FullName: $($_.Exception.Message)" }
            foreach ($Property in 'DisplayName', 'PublisherDisplayName', 'SignatureKind', 'IsFramework', 'IsResourcePackage', 'IsBundle', 'IsStub') {
                try { $Record[$Property] = [string]$Package.$Property } catch { $Record.Errors += "${Property}: $($_.Exception.Message)" }
            }
            $Records += [pscustomobject]$Record
        }
        $Out.Packages = $Records
        $Out.Count = $Records.Count
    }
    catch { $Out.Error = ('0x{0:X8} {1}' -f $_.Exception.HResult, $_.Exception.Message) }
    $Out.Ms = [int]$Watch.ElapsedMilliseconds
    return [pscustomobject]$Out
}

function Invoke-E4A2WinRtRemove {
    # PackageManager.RemovePackageAsync(<pełna nazwa>) w bieżącym procesie (konto procesu); zwraca tekst wyniku operacji. Wołane tylko
    # z Invoke-E4A2Removal (za bezpiecznikiem).
    [CmdletBinding()]
    [OutputType([string])]
    param([Parameter(Mandatory)][string]$PackageFullName)
    Assert-E4A2InsideVm
    Initialize-E4A2Native
    $null = [Windows.Management.Deployment.PackageManager, Windows.Management.Deployment, ContentType = WindowsRuntime]
    $Manager = New-Object -TypeName Windows.Management.Deployment.PackageManager
    $Operation = $Manager.RemovePackageAsync($PackageFullName)
    $AsTask = [System.WindowsRuntimeSystemExtensions].GetMethods() | Where-Object {
        $_.Name -eq 'AsTask' -and $_.GetParameters().Count -eq 1 -and $_.GetParameters()[0].ParameterType.Name -eq 'IAsyncOperationWithProgress`2'
    } | Select-Object -First 1
    $Task = $AsTask.MakeGenericMethod([Windows.Management.Deployment.DeploymentResult], [Windows.Management.Deployment.DeploymentProgress]).Invoke($null, @($Operation))
    if (-not $Task.Wait(120000)) { return 'brak wyniku w 120 s' }
    $Result = $Task.Result
    return ('ExtendedErrorCode=0x{0:X8} IsRegistered={1} ErrorText={2}' -f $Result.ExtendedErrorCode.HResult, $Result.IsRegistered, $Result.ErrorText)
}

function Get-E4A2AppxInventory {
    # K1: Get-AppxPackage -User <SID> (czas pierwszego i drugiego odczytu), pola klasyfikacji, nazwy z manifestu i ich rozwinięcie;
    # dla porównania -AllUsers i -PackageTypeFilter ze wszystkimi typami (tylko liczba i czas).
    [CmdletBinding()]
    param([Parameter(Mandatory)][string]$UserSid)
    $Out = [ordered]@{ UserSid = $UserSid; ColdMs = $null; WarmMs = $null; Count = $null; Error = $null; ResolveMs = $null; Packages = @()
        AllUsersMs = $null; AllUsersCount = $null; AllUsersError = $null; AllTypesCount = $null; AllTypesError = $null }
    $Watch = [System.Diagnostics.Stopwatch]::StartNew()
    try {
        $Packages = @(Get-AppxPackage -User $UserSid -ErrorAction Stop)
        $Out.ColdMs = [int]$Watch.ElapsedMilliseconds
        $Watch.Restart()
        $null = @(Get-AppxPackage -User $UserSid -ErrorAction Stop)
        $Out.WarmMs = [int]$Watch.ElapsedMilliseconds
        $Out.Count = $Packages.Count
        $Watch.Restart()
        $Records = @()
        foreach ($Package in $Packages) {
            $Manifest = Get-E4A2ManifestNames -InstallLocation ([string]$Package.InstallLocation)
            $Display = Resolve-E4A2DisplayName -Raw $Manifest.DisplayName -PackageFullName $Package.PackageFullName -PackageName $Package.Name
            $Publisher = Resolve-E4A2DisplayName -Raw $Manifest.PublisherDisplayName -PackageFullName $Package.PackageFullName -PackageName $Package.Name
            $AppNames = @()
            foreach ($App in @($Manifest.Applications)) {
                $Resolved = Resolve-E4A2DisplayName -Raw $App.DisplayName -PackageFullName $Package.PackageFullName -PackageName $Package.Name
                $AppNames += [pscustomobject]@{ Id = $App.Id; Raw = $App.DisplayName; Value = $Resolved.Value; AppListEntry = $App.AppListEntry }
            }
            $Records += [pscustomobject][ordered]@{
                Name = $Package.Name; PackageFullName = $Package.PackageFullName; PackageFamilyName = $Package.PackageFamilyName; Publisher = $Package.Publisher
                Version = [string]$Package.Version; Architecture = [string]$Package.Architecture; IsFramework = [bool]$Package.IsFramework
                IsResourcePackage = [bool]$Package.IsResourcePackage; IsBundle = [bool]$Package.IsBundle; NonRemovable = [bool]$Package.NonRemovable
                SignatureKind = [string]$Package.SignatureKind; Status = [string]$Package.Status; InstallLocation = [string]$Package.InstallLocation
                ManifestDisplayName = $Display.Raw; DisplayName = $Display.Value; DisplayNameVia = $Display.Via; DisplayNameAttempts = @($Display.Attempts)
                ManifestPublisherDisplayName = $Publisher.Raw; PublisherDisplayName = $Publisher.Value; Applications = $AppNames; ManifestError = $Manifest.Error
            }
        }
        $Out.Packages = $Records
        $Out.ResolveMs = [int]$Watch.ElapsedMilliseconds
    }
    catch { $Out.Error = ('0x{0:X8} {1}' -f $_.Exception.HResult, $_.Exception.Message) }
    $Watch.Restart()
    try { $Out.AllUsersCount = @(Get-AppxPackage -AllUsers -ErrorAction Stop).Count; $Out.AllUsersMs = [int]$Watch.ElapsedMilliseconds }
    catch { $Out.AllUsersError = $_.Exception.Message }
    try { $Out.AllTypesCount = @(Get-AppxPackage -User $UserSid -PackageTypeFilter Main, Framework, Resource, Bundle, Optional -ErrorAction Stop).Count }
    catch { $Out.AllTypesError = $_.Exception.Message }
    return [pscustomobject]$Out
}

function Get-E4A2ProvisionedInventory {
    # Pakiety w obrazie systemu (provisioned), z czasem odczytu.
    [CmdletBinding()]
    param()
    $Out = [ordered]@{ Ms = $null; Count = $null; Error = $null; Packages = @() }
    $Watch = [System.Diagnostics.Stopwatch]::StartNew()
    try {
        $Packages = @(Get-AppxProvisionedPackage -Online -ErrorAction Stop)
        $Out.Packages = @($Packages | ForEach-Object {
                [pscustomobject]@{ DisplayName = $_.DisplayName; PackageName = $_.PackageName; Version = [string]$_.Version; PublisherId = $_.PublisherId; Architecture = [string]$_.Architecture }
            })
        $Out.Count = $Packages.Count
    }
    catch { $Out.Error = $_.Exception.Message }
    $Out.Ms = [int]$Watch.ElapsedMilliseconds
    return [pscustomobject]$Out
}

function Get-E4A2UninstallInventory {
    # Wpisy Uninstall: dla każdej gałęzi wszystkie podklucze ze wszystkimi wartościami (nazwa, typ, wartość).
    [CmdletBinding()]
    param(
        [Parameter(Mandatory)][string]$UserSid,
        [Parameter(Mandatory)][string[]]$Roots
    )
    $Out = @()
    foreach ($Root in $Roots) {
        $Record = [ordered]@{ Root = $Root; Path = $null; KeyExists = $false; Count = 0; Entries = @(); Error = $null; Ms = $null }
        $Watch = [System.Diagnostics.Stopwatch]::StartNew()
        try {
            $Path = Resolve-E4A2Path -Path $Root -UserSid $UserSid
            $Record.Path = $Path
            $Node = Read-ExploreRegistryKey -Path $Path
            $Record.KeyExists = [bool]$Node.KeyExists
            $Entries = @()
            foreach ($Sub in @($Node.SubKeys)) {
                $Entry = Read-ExploreRegistryKey -Path "$Path\$Sub"
                $Entries += [pscustomobject]@{ KeyName = $Sub; Values = @($Entry.Values); SubKeys = @($Entry.SubKeys) }
            }
            $Record.Entries = $Entries
            $Record.Count = $Entries.Count
        }
        catch { $Record.Error = $_.Exception.Message }
        $Record.Ms = [int]$Watch.ElapsedMilliseconds
        $Out += [pscustomobject]$Record
    }
    return $Out
}

function Get-E4A2SystemProtection {
    # Stan ochrony systemu C: WYŁĄCZNIE odczytem: rejestr SPP\Clients (GUID woluminu C:), klucz SystemRestore i polityka, WMI root\default
    # (SystemRestore = punkty, SystemRestoreConfig), Win32_ShadowStorage, vssadmin list shadowstorage, usługi VSS i swprv.
    # Bezpiecznik, bo uruchamia program (vssadmin).
    [CmdletBinding()]
    param()
    Assert-E4A2InsideVm
    $Out = [ordered]@{ VolumeId = $null; SppEntries = @(); SppProtected = $null; SppKey = $null; SystemRestoreKey = $null; PolicyKey = $null
        RestorePoints = $null; RestorePointsError = $null; SrConfig = $null; SrConfigError = $null; ShadowStorage = @(); ShadowStorageError = $null
        Vssadmin = $null; Services = @(); DurationMs = $null; Errors = @() }
    $Watch = [System.Diagnostics.Stopwatch]::StartNew()
    try { $Out.VolumeId = [string](Get-CimInstance -ClassName Win32_Volume -Filter "DriveLetter='C:'" -ErrorAction Stop | Select-Object -First 1).DeviceID }
    catch { $Out.Errors += "Win32_Volume: $($_.Exception.Message)" }
    try {
        $Spp = Read-ExploreRegistryKey -Path 'HKLM\SOFTWARE\Microsoft\Windows NT\CurrentVersion\SPP\Clients'
        $Out.SppKey = $Spp
        $Value = @($Spp.Values | Where-Object { $_.Name -eq '{09F7EDC5-294E-4180-AF6A-FB0E6A0E9513}' }) | Select-Object -First 1
        if ($null -ne $Value) { $Out.SppEntries = @($Value.Value) }
        $Out.SppProtected = Test-E4A2SppVolume -Entries @($Out.SppEntries) -VolumeId $Out.VolumeId
    }
    catch { $Out.Errors += "SPP: $($_.Exception.Message)" }
    try { $Out.SystemRestoreKey = Read-ExploreRegistryKey -Path 'HKLM\SOFTWARE\Microsoft\Windows NT\CurrentVersion\SystemRestore' } catch { $Out.Errors += "SystemRestore: $($_.Exception.Message)" }
    try { $Out.PolicyKey = Read-ExploreRegistryKey -Path 'HKLM\SOFTWARE\Policies\Microsoft\Windows NT\SystemRestore' } catch { $Out.Errors += "polityka: $($_.Exception.Message)" }
    try { $Out.RestorePoints = @(Get-CimInstance -Namespace 'root/default' -ClassName SystemRestore -ErrorAction Stop).Count }
    catch { $Out.RestorePointsError = ('0x{0:X8} {1}' -f $_.Exception.HResult, $_.Exception.Message) }
    try {
        $Config = Get-CimInstance -Namespace 'root/default' -ClassName SystemRestoreConfig -ErrorAction Stop | Select-Object -First 1
        if ($null -ne $Config) { $Out.SrConfig = [ordered]@{ RPSessionInterval = $Config.RPSessionInterval; DiskPercent = $Config.DiskPercent; RPGlobalInterval = $Config.RPGlobalInterval; RPLifeInterval = $Config.RPLifeInterval } }
    }
    catch { $Out.SrConfigError = ('0x{0:X8} {1}' -f $_.Exception.HResult, $_.Exception.Message) }
    try {
        $Out.ShadowStorage = @(Get-CimInstance -ClassName Win32_ShadowStorage -ErrorAction Stop | ForEach-Object {
                [pscustomobject]@{ Volume = [string]$_.Volume.DeviceID; DiffVolume = [string]$_.DiffVolume.DeviceID; MaxSpace = $_.MaxSpace; UsedSpace = $_.UsedSpace; AllocatedSpace = $_.AllocatedSpace }
            })
    }
    catch { $Out.ShadowStorageError = ('0x{0:X8} {1}' -f $_.Exception.HResult, $_.Exception.Message) }
    $Out.Vssadmin = Invoke-ExploreNative -FilePath (Join-Path $env:SystemRoot 'System32\vssadmin.exe') -Arguments @('list', 'shadowstorage') -TimeoutSeconds 60
    foreach ($Name in 'VSS', 'swprv') {
        try { $Service = Get-Service -Name $Name -ErrorAction Stop; $Out.Services += "$Name Status=$($Service.Status) StartType=$($Service.StartType)" }
        catch { $Out.Services += "${Name}: $($_.Exception.Message)" }
    }
    $Out.DurationMs = [int]$Watch.ElapsedMilliseconds
    return [pscustomobject]$Out
}

function Set-E4A2SystemProtection {
    # ZMIANA (tylko w VM, cofana revertem): Enable-ComputerRestore / Disable-ComputerRestore na C:\, żeby sprawdzić, czy odczyt rozróżnia stany.
    [CmdletBinding()]
    param([Parameter(Mandatory)][bool]$Enable)
    Assert-E4A2InsideVm
    $Out = [ordered]@{ Action = if ($Enable) { 'Enable-ComputerRestore -Drive C:\' } else { 'Disable-ComputerRestore -Drive C:\' }; Error = $null; DurationMs = $null }
    $Watch = [System.Diagnostics.Stopwatch]::StartNew()
    try {
        if ($Enable) { Enable-ComputerRestore -Drive 'C:\' -ErrorAction Stop }
        else { Disable-ComputerRestore -Drive 'C:\' -ErrorAction Stop }
    }
    catch { $Out.Error = ('0x{0:X8} {1}' -f $_.Exception.HResult, $_.Exception.Message) }
    $Out.DurationMs = [int]$Watch.ElapsedMilliseconds
    return [pscustomobject]$Out
}

function Get-E4A2ToolAvailability {
    # csc.exe (kompilacja i uruchomienie programu testowego) oraz COM WindowsInstaller.Installer (Version, pusta baza MSI) w C:\dso\e4a2\tools.
    [CmdletBinding()]
    param(
        [Parameter(Mandatory)][string]$CscRelativePath,
        [Parameter(Mandatory)][string]$InstallerProgId
    )
    Assert-E4A2InsideVm
    $Directory = 'C:\dso\e4a2\tools'
    $null = New-Item -ItemType Directory -Path $Directory -Force
    $Out = [ordered]@{ CscPath = $null; CscExists = $false; CscVersion = $null; Compile = $null; Run = $null
        MsiexecExists = $false; ComCreated = $false; ComVersion = $null; ComVersionVia = $null; MsiCreated = $false; MsiVia = $null; Errors = @() }
    $Csc = Join-Path $env:WINDIR $CscRelativePath
    $Out.CscPath = $Csc
    $Out.CscExists = Test-Path -LiteralPath $Csc -PathType Leaf
    if ($Out.CscExists) {
        try {
            $Out.CscVersion = (Get-Item -LiteralPath $Csc).VersionInfo.FileVersion
            $Source = Join-Path $Directory 'hello.cs'
            $Exe = Join-Path $Directory 'hello.exe'
            Set-Content -LiteralPath $Source -Value 'class P { static int Main() { System.Console.WriteLine("dso-e4a2"); return 7; } }' -Encoding ASCII
            $Out.Compile = Invoke-ExploreNative -FilePath $Csc -Arguments @('/nologo', '/target:exe', "/out:$Exe", $Source) -TimeoutSeconds 120
            if (Test-Path -LiteralPath $Exe) { $Out.Run = Invoke-ExploreNative -FilePath $Exe -TimeoutSeconds 30 }
        }
        catch { $Out.Errors += "csc: $($_.Exception.Message)" }
    }
    $Out.MsiexecExists = Test-Path -LiteralPath (Join-Path $env:SystemRoot 'System32\msiexec.exe') -PathType Leaf
    try {
        $Installer = New-Object -ComObject $InstallerProgId
        $Out.ComCreated = $true
        try { $Out.ComVersion = [string]$Installer.Version; $Out.ComVersionVia = 'właściwość' } catch { $Out.Errors += "Version jako właściwość: $($_.Exception.Message)" }
        if ([string]::IsNullOrEmpty($Out.ComVersion)) {
            $Out.ComVersion = [string]$Installer.GetType().InvokeMember('Version', [System.Reflection.BindingFlags]::GetProperty, $null, $Installer, $null)
            $Out.ComVersionVia = 'InvokeMember'
        }
        $MsiPath = Join-Path $Directory 'empty.msi'
        if (Test-Path -LiteralPath $MsiPath) { Remove-Item -LiteralPath $MsiPath -Force }
        $Database = $null
        try { $Database = $Installer.OpenDatabase($MsiPath, 3); $Out.MsiVia = 'metoda' }
        catch { $Database = $Installer.GetType().InvokeMember('OpenDatabase', [System.Reflection.BindingFlags]::InvokeMethod, $null, $Installer, @($MsiPath, 3)); $Out.MsiVia = 'InvokeMember' }
        try { $null = $Database.Commit() } catch { $null = $Database.GetType().InvokeMember('Commit', [System.Reflection.BindingFlags]::InvokeMethod, $null, $Database, $null); $Out.MsiVia += ' + Commit przez InvokeMember' }
        $null = [System.Runtime.InteropServices.Marshal]::ReleaseComObject($Database)
        $Out.MsiCreated = Test-Path -LiteralPath $MsiPath -PathType Leaf
    }
    catch { $Out.Errors += "COM: $('0x{0:X8}' -f $_.Exception.HResult) $($_.Exception.Message)" }
    return [pscustomobject]$Out
}

function Get-E4A2PackageState {
    # Stan pakietu: dla konta dso (-User <SID>), w obrazie systemu (provisioned) i u wszystkich użytkowników (PackageUserInformation).
    [CmdletBinding()]
    param(
        [Parameter(Mandatory)][string]$UserSid,
        [Parameter(Mandatory)][string]$Name
    )
    $Out = [ordered]@{ Name = $Name; ForUser = $null; Provisioned = $null; Users = @(); Errors = @() }
    try { $Out.ForUser = (@(Get-AppxPackage -User $UserSid -Name $Name -ErrorAction Stop | ForEach-Object { $_.PackageFullName }) -join ', ') } catch { $Out.Errors += "dso: $($_.Exception.Message)" }
    if ($Out.ForUser -eq '') { $Out.ForUser = $null }
    try { $Out.Provisioned = (@(Get-AppxProvisionedPackage -Online -ErrorAction Stop | Where-Object { $_.DisplayName -eq $Name } | ForEach-Object { $_.PackageName }) -join ', ') } catch { $Out.Errors += "obraz: $($_.Exception.Message)" }
    if ($Out.Provisioned -eq '') { $Out.Provisioned = $null }
    try {
        foreach ($Package in @(Get-AppxPackage -AllUsers -Name $Name -ErrorAction Stop)) {
            foreach ($User in @($Package.PackageUserInformation)) {
                $Out.Users += "$($Package.PackageFullName) | $($User.UserSecurityId.Sid) $($User.UserSecurityId.Username) | $($User.InstallState)"
            }
        }
    }
    catch { $Out.Errors += "wszyscy: $($_.Exception.Message)" }
    return [pscustomobject]$Out
}

function Invoke-E4A2Removal {
    # ZMIANA (tylko w VM): jedna metoda usuwania z procesu Administratora, czas operacji i stan po (niezależny odczyt).
    # WinRtProcess: PackageManager.RemovePackageAsync(<pełna nazwa pakietu dso>) w procesie Administratora (wynik w Outcome);
    # UserSid: Remove-AppxPackage -Package <pełna> -User <SID>; AllUsers: Remove-AppxPackage -Package <pełna> -AllUsers;
    # Deprovision: Remove-AppxProvisionedPackage -Online -PackageName <pełna nazwa z obrazu>. Brak pakietu = NotPresent, nic nie jest usuwane.
    [CmdletBinding()]
    param(
        [Parameter(Mandatory)][string]$UserSid,
        [Parameter(Mandatory)][ValidatePattern('^[A-Za-z0-9.]+\z')][string]$Name,
        [Parameter(Mandatory)][ValidateSet('WinRtProcess', 'UserSid', 'AllUsers', 'Deprovision')][string]$Method
    )
    Assert-E4A2InsideVm
    $Out = [ordered]@{ Name = $Name; Method = $Method; PackageFullName = $null; Command = $null; NotPresent = $false; DurationMs = $null; Outcome = $null; Error = $null; After = $null }
    try {
        if ($Method -eq 'Deprovision') {
            $Provisioned = @(Get-AppxProvisionedPackage -Online -ErrorAction Stop) | Where-Object { $null -ne $_ -and $_.DisplayName -eq $Name } | Select-Object -First 1
            if ($null -eq $Provisioned) { $Out.NotPresent = $true }
            else {
                $Out.PackageFullName = $Provisioned.PackageName
                $Out.Command = "Remove-AppxProvisionedPackage -Online -PackageName $($Provisioned.PackageName)"
                $Watch = [System.Diagnostics.Stopwatch]::StartNew()
                try { $null = Remove-AppxProvisionedPackage -Online -PackageName $Provisioned.PackageName -ErrorAction Stop }
                finally { $Out.DurationMs = [int]$Watch.ElapsedMilliseconds }
            }
        }
        else {
            $Found = if ($Method -eq 'AllUsers') { @(Get-AppxPackage -AllUsers -Name $Name -ErrorAction Stop) } else { @(Get-AppxPackage -User $UserSid -Name $Name -ErrorAction Stop) }
            $Package = Select-E4A2Present -Packages $Found -Name $Name
            if ($null -eq $Package) { $Out.NotPresent = $true }
            else {
                $Out.PackageFullName = $Package.PackageFullName
                $Watch = [System.Diagnostics.Stopwatch]::StartNew()
                try {
                    if ($Method -eq 'WinRtProcess') {
                        $Out.Command = "PackageManager.RemovePackageAsync('$($Package.PackageFullName)') (konto procesu)"
                        $Out.Outcome = Invoke-E4A2WinRtRemove -PackageFullName $Package.PackageFullName
                    }
                    elseif ($Method -eq 'UserSid') {
                        $Out.Command = "Remove-AppxPackage -Package $($Package.PackageFullName) -User $UserSid"
                        Remove-AppxPackage -Package $Package.PackageFullName -User $UserSid -ErrorAction Stop
                    }
                    else {
                        $Out.Command = "Remove-AppxPackage -Package $($Package.PackageFullName) -AllUsers"
                        Remove-AppxPackage -Package $Package.PackageFullName -AllUsers -ErrorAction Stop
                    }
                }
                finally { $Out.DurationMs = [int]$Watch.ElapsedMilliseconds }
            }
        }
    }
    catch {
        # Błąd bazowy (WinRT przez AsTask opakowuje go w AggregateException).
        $Base = $_.Exception.GetBaseException()
        $Out.Error = ('0x{0:X8} {1}' -f $Base.HResult, $Base.Message)
    }
    $Out.After = Get-E4A2PackageState -UserSid $UserSid -Name $Name
    return [pscustomobject]$Out
}

function Invoke-E4A2RemovalPhase {
    # ZMIANA (tylko w VM): cele z planu po kolei: stan przed -> metody (K1 albo zadanie dso) -> gdy pakiet dalej jest dla dso: odpytywanie stanu
    # (PollSeconds) i Start dso w trakcie -> Fallback -> deprovisioning; stan po każdym kroku.
    [CmdletBinding()]
    param(
        [Parameter(Mandatory)][string]$UserSid,
        [Parameter(Mandatory)][object[]]$Targets,
        [ValidateRange(1, 60)][int]$PollIntervalSeconds = 5
    )
    Assert-E4A2InsideVm
    $Records = @()
    foreach ($Target in $Targets) {
        $Name = [string]$Target.Name
        $Record = [ordered]@{ Name = $Name; Methods = @($Target.Methods); Before = $null; Steps = @(); Tasks = @(); Poll = @(); StartDuringPoll = $null; Error = $null }
        try {
            $Record.Before = Get-E4A2PackageState -UserSid $UserSid -Name $Name
            $Last = $null
            foreach ($Method in @($Target.Methods)) {
                if ($Method -eq 'OwnUser' -or $Method -eq 'OwnUserSid') {
                    $TaskName = 'remove-' + $Name.ToLowerInvariant().Replace('.', '-')
                    $Record.Tasks += Invoke-E4A2UserTask -Name $TaskName -Ops @(@{ Op = 'Remove'; Name = $Name; WithSid = ($Method -eq 'OwnUserSid'); UserSid = $UserSid }) -TimeoutSeconds 300
                    $Last = Get-E4A2PackageState -UserSid $UserSid -Name $Name
                    $Record.Tasks += [pscustomobject]@{ Method = $Method; After = $Last }
                }
                else {
                    $Step = Invoke-E4A2Removal -UserSid $UserSid -Name $Name -Method $Method
                    $Record.Steps += $Step
                    $Last = $Step.After
                }
            }
            $StillThere = ($null -ne $Last -and $Last.ForUser)
            if ($StillThere -and [int]$Target.PollSeconds -gt 0) {
                for ($Second = $PollIntervalSeconds; $Second -le [int]$Target.PollSeconds; $Second += $PollIntervalSeconds) {
                    Start-Sleep -Seconds $PollIntervalSeconds
                    $State = Get-E4A2PackageState -UserSid $UserSid -Name $Name
                    $Record.Poll += [pscustomobject]@{ Second = $Second; ForUser = $State.ForUser; Users = @($State.Users) }
                    if (-not $State.ForUser) { break }
                }
                $Record.StartDuringPoll = Invoke-E4A2UserTask -Name ('start-' + $Name.ToLowerInvariant().Replace('.', '-')) -Ops @(@{ Op = 'Appx'; UserSid = $UserSid }) -TimeoutSeconds 300
                $Last = Get-E4A2PackageState -UserSid $UserSid -Name $Name
                $StillThere = [bool]$Last.ForUser
            }
            if ($Target.Fallback -and $StillThere) { $Record.Steps += Invoke-E4A2Removal -UserSid $UserSid -Name $Name -Method $Target.Fallback }
            if ($Target.Deprovision) { $Record.Steps += Invoke-E4A2Removal -UserSid $UserSid -Name $Name -Method Deprovision }
        }
        catch { $Record.Error = $_.Exception.Message }
        $Records += [pscustomobject]$Record
    }
    return $Records
}

function Invoke-E4A2LogoffProbe {
    # ZMIANA (tylko w VM): wylogowanie dso (WTSLogoffSession sesji powłoki dso; Home nie ma logoff.exe) i odczyt stanu pakietu przez 90 s: czy usunięcie
    # „pending removal” po Remove-AppxPackage -User <SID> dokańcza się przy wylogowaniu.
    [CmdletBinding()]
    param(
        [Parameter(Mandatory)][string]$UserSid,
        [Parameter(Mandatory)][ValidatePattern('^[A-Za-z0-9.]+\z')][string]$Name,
        [ValidateRange(1, 60)][int]$PollIntervalSeconds = 5,
        [ValidateRange(5, 600)][int]$PollSeconds = 90
    )
    Assert-E4A2InsideVm
    $Out = [ordered]@{ Name = $Name; Before = $null; SessionId = $null; Logoff = $null; ShellGoneAfterMs = $null; Poll = @(); Error = $null }
    try {
        $Out.Before = Get-E4A2PackageState -UserSid $UserSid -Name $Name
        foreach ($Process in @(Get-CimInstance -ClassName Win32_Process -Filter "Name='explorer.exe'" -ErrorAction Stop)) {
            $Owner = Invoke-CimMethod -InputObject $Process -MethodName GetOwner -ErrorAction SilentlyContinue
            if ($null -ne $Owner -and $Owner.User -eq 'dso') { $Out.SessionId = [int]$Process.SessionId }
        }
        if ($null -eq $Out.SessionId) { throw 'brak powłoki dso (explorer.exe) - nie ma kogo wylogować' }
        $Watch = [System.Diagnostics.Stopwatch]::StartNew()
        Initialize-E4A2Native
        $LoggedOff = [E4A2Native]::WTSLogoffSession([IntPtr]::Zero, [int]$Out.SessionId, $true)
        $Out.Logoff = "WTSLogoffSession=$LoggedOff LastError=$([System.Runtime.InteropServices.Marshal]::GetLastWin32Error())"
        for ($Poll = 0; $Poll -lt 120; $Poll++) {
            $Left = @(Get-CimInstance -ClassName Win32_Process -Filter "Name='explorer.exe' AND SessionId=$($Out.SessionId)" -ErrorAction SilentlyContinue)
            if ($Left.Count -eq 0) { $Out.ShellGoneAfterMs = [int]$Watch.ElapsedMilliseconds; break }
            Start-Sleep -Milliseconds 500
        }
        for ($Second = $PollIntervalSeconds; $Second -le $PollSeconds; $Second += $PollIntervalSeconds) {
            Start-Sleep -Seconds $PollIntervalSeconds
            $State = Get-E4A2PackageState -UserSid $UserSid -Name $Name
            $Out.Poll += [pscustomobject]@{ Second = $Second; ForUser = $State.ForUser; Users = @($State.Users); Errors = @($State.Errors) }
            if (-not $State.ForUser -and @($State.Errors).Count -eq 0) { break }
        }
    }
    catch { $Out.Error = $_.Exception.Message }
    return [pscustomobject]$Out
}

# --- K2: operacje w sesji dso (kopiowane do skryptu zadania; Add-Type tylko tam) ---

function Initialize-E4A2K2Native {
    [CmdletBinding()]
    param()
    if ('E4A2User32' -as [type]) { return }
    Add-Type -AssemblyName System.Drawing
    Add-Type -AssemblyName System.Windows.Forms
    Add-Type -TypeDefinition @'
using System;
using System.Runtime.InteropServices;
public static class E4A2User32 {
    [DllImport("user32.dll")] public static extern void keybd_event(byte vk, byte scan, uint flags, UIntPtr extra);
    public static void Press(byte vk) { keybd_event(vk, 0, 0, UIntPtr.Zero); System.Threading.Thread.Sleep(50); keybd_event(vk, 0, 2, UIntPtr.Zero); }
}
'@
}

function Save-E4A2K2Shot {
    # Zrzut pulpitu sesji dso do C:\dso\e4a2\shots\<Label>.png; zwraca nazwę pliku.
    [CmdletBinding()]
    param([Parameter(Mandatory)][ValidatePattern('^[a-z0-9-]+\z')][string]$Label)
    Assert-E4A2InsideVm
    $Directory = 'C:\dso\e4a2\shots'
    $null = New-Item -ItemType Directory -Path $Directory -Force
    $Bounds = [System.Windows.Forms.SystemInformation]::VirtualScreen
    $Bitmap = New-Object System.Drawing.Bitmap($Bounds.Width, $Bounds.Height)
    try {
        $Graphics = [System.Drawing.Graphics]::FromImage($Bitmap)
        try { $Graphics.CopyFromScreen($Bounds.Left, $Bounds.Top, 0, 0, $Bitmap.Size) } finally { $Graphics.Dispose() }
        $Bitmap.Save("$Directory\$Label.png", [System.Drawing.Imaging.ImageFormat]::Png)
    }
    finally { $Bitmap.Dispose() }
    return "$Label.png"
}

function ConvertTo-E4A2SearchPhrase {
    # Fraza do SendKeys: początek nazwy z liter ASCII, cyfr i spacji; krótsza niż 3 znaki = fraza zapasowa.
    [CmdletBinding()]
    [OutputType([string])]
    param(
        [AllowNull()][AllowEmptyString()][string]$Name,
        [Parameter(Mandatory)][string]$Fallback
    )
    $Phrase = [regex]::Match([string]$Name, '^[A-Za-z0-9 ]+').Value.Trim()
    if ($Phrase.Length -ge 3) { return $Phrase }
    return $Fallback
}

function Get-E4A2K2Appx {
    # Operacja Appx (konto dso, podniesione): Get-AppxPackage własnego konta i -User <SID> (czas), Get-StartApps, WinRT FindPackagesForUser('').
    [CmdletBinding()]
    param([Parameter(Mandatory)][string]$UserSid)
    $Out = [ordered]@{ Op = 'Appx'; OwnMs = $null; OwnCount = $null; OwnNames = @(); OwnError = $null; BySidMs = $null; BySidCount = $null; BySidError = $null
        StartApps = @(); StartAppsError = $null; WinRt = $null }
    $Watch = [System.Diagnostics.Stopwatch]::StartNew()
    try {
        $Own = @(Get-AppxPackage -ErrorAction Stop)
        $Out.OwnMs = [int]$Watch.ElapsedMilliseconds
        $Out.OwnCount = $Own.Count
        $Out.OwnNames = @($Own | ForEach-Object { $_.PackageFullName } | Sort-Object)
    }
    catch { $Out.OwnError = $_.Exception.Message }
    $Watch.Restart()
    try { $Out.BySidCount = @(Get-AppxPackage -User $UserSid -ErrorAction Stop).Count; $Out.BySidMs = [int]$Watch.ElapsedMilliseconds }
    catch { $Out.BySidError = $_.Exception.Message }
    try { $Out.StartApps = @(Get-StartApps -ErrorAction Stop | ForEach-Object { [pscustomobject]@{ Name = $_.Name; AppID = $_.AppID } }) }
    catch { $Out.StartAppsError = $_.Exception.Message }
    $Out.WinRt = Get-E4A2WinRtPackages -UserSid ''
    return [pscustomobject]$Out
}

function Get-E4A2K2Search {
    # Operacja Search: fraza (podana, z Get-StartApps po rodzinie pakietu albo zapasowa) -> Esc, Win, wpisanie, 6 s, zrzut; LeaveOpen = bez Esc
    # na końcu (zrzut hosta zobaczy otwarty Start).
    [CmdletBinding()]
    param(
        [Parameter(Mandatory)][string]$Name,
        [Parameter(Mandatory)][string]$Fallback,
        [AllowEmptyString()][string]$Phrase = '',
        [AllowEmptyString()][string]$Pfn = '',
        [Parameter(Mandatory)][ValidatePattern('^[a-z0-9-]+\z')][string]$Label,
        [bool]$LeaveOpen = $false
    )
    Assert-E4A2InsideVm
    $Out = [ordered]@{ Op = 'Search'; Name = $Name; Pfn = $Pfn; Phrase = $null; PhraseSource = $null; StartAppsMatch = @(); Shot = $null; Error = $null }
    try {
        if (-not $Pfn) {
            $Package = Get-AppxPackage -Name $Name -ErrorAction SilentlyContinue | Select-Object -First 1
            if ($null -ne $Package) { $Pfn = $Package.PackageFamilyName; $Out.Pfn = $Pfn }
        }
        $Hits = @()
        if ($Pfn) { $Hits = @(Get-StartApps | Where-Object { $_.AppID -like "$Pfn!*" }) }
        $Out.StartAppsMatch = @($Hits | ForEach-Object { "$($_.Name) | $($_.AppID)" })
        if ($Phrase) { $Out.PhraseSource = 'podana' }
        elseif ($Hits.Count -gt 0) { $Phrase = ConvertTo-E4A2SearchPhrase -Name $Hits[0].Name -Fallback $Fallback; $Out.PhraseSource = 'Get-StartApps' }
        else { $Phrase = $Fallback; $Out.PhraseSource = 'zapasowa' }
        $Out.Phrase = $Phrase
        [E4A2User32]::Press(0x1B); Start-Sleep -Seconds 1
        [E4A2User32]::Press(0x5B); Start-Sleep -Seconds 3
        [System.Windows.Forms.SendKeys]::SendWait($Phrase); Start-Sleep -Seconds 6
        $Out.Shot = Save-E4A2K2Shot -Label $Label
        if (-not $LeaveOpen) { [E4A2User32]::Press(0x1B); Start-Sleep -Seconds 1 }
    }
    catch { $Out.Error = $_.Exception.Message }
    return [pscustomobject]$Out
}

function Invoke-E4A2K2Remove {
    # Operacja Remove (konto dso, podniesione): Remove-AppxPackage -Package <pełna> bez -User albo (WithSid) z -User <SID dso>, czas i stan
    # własnego konta po.
    [CmdletBinding()]
    param(
        [Parameter(Mandatory)][ValidatePattern('^[A-Za-z0-9.]+\z')][string]$Name,
        [bool]$WithSid = $false,
        [AllowEmptyString()][string]$UserSid = ''
    )
    Assert-E4A2InsideVm
    $Out = [ordered]@{ Op = 'Remove'; Name = $Name; PackageFullName = $null; Command = $null; NotPresent = $false; DurationMs = $null; Error = $null; OwnAfter = @() }
    try {
        $Package = Get-AppxPackage -Name $Name -ErrorAction Stop | Select-Object -First 1
        if ($null -eq $Package) { $Out.NotPresent = $true }
        else {
            $Out.PackageFullName = $Package.PackageFullName
            $Watch = [System.Diagnostics.Stopwatch]::StartNew()
            try {
                if ($WithSid) {
                    $Out.Command = "Remove-AppxPackage -Package $($Package.PackageFullName) -User $UserSid"
                    Remove-AppxPackage -Package $Package.PackageFullName -User $UserSid -ErrorAction Stop
                }
                else {
                    $Out.Command = "Remove-AppxPackage -Package $($Package.PackageFullName)"
                    Remove-AppxPackage -Package $Package.PackageFullName -ErrorAction Stop
                }
            }
            finally { $Out.DurationMs = [int]$Watch.ElapsedMilliseconds }
        }
    }
    catch { $Out.Error = ('0x{0:X8} {1}' -f $_.Exception.HResult, $_.Exception.Message) }
    try { $Out.OwnAfter = @(Get-AppxPackage -Name $Name -ErrorAction Stop | ForEach-Object { $_.PackageFullName }) } catch { $Out.Error = (@($Out.Error, "odczyt po: $($_.Exception.Message)") | Where-Object { $_ }) -join ' | ' }
    return [pscustomobject]$Out
}

function Invoke-E4A2K2Ops {
    # Dyspozytor operacji K2 (lista {Op, ...argumenty}); błąd operacji jest daną.
    [CmdletBinding()]
    param([Parameter(Mandatory)][AllowEmptyCollection()][object[]]$Ops)
    Assert-E4A2InsideVm
    Initialize-E4A2K2Native
    $Results = @()
    foreach ($Op in $Ops) {
        try {
            switch ([string]$Op.Op) {
                'Appx' { $Results += Get-E4A2K2Appx -UserSid $Op.UserSid }
                'Search' { $Results += Get-E4A2K2Search -Name $Op.Name -Fallback $Op.Fallback -Phrase ([string]$Op.Phrase) -Pfn ([string]$Op.Pfn) -Label $Op.Label -LeaveOpen ([bool]$Op.LeaveOpen) }
                'Remove' { $Results += Invoke-E4A2K2Remove -Name $Op.Name -WithSid ([bool]$Op.WithSid) -UserSid ([string]$Op.UserSid) }
                default { throw "Nieznana operacja K2 '$($Op.Op)'." }
            }
        }
        catch { $Results += [pscustomobject]@{ Op = [string]$Op.Op; Error = $_.Exception.Message } }
    }
    return $Results
}

function Get-E4A2ChildFunctionNames {
    # Funkcje kopiowane do skryptu zadania K2 (domknięcie wywołań pilnuje test); z bezpiecznikiem, który dziecko woła przed operacjami.
    [CmdletBinding()]
    [OutputType([string[]])]
    param()
    return @('Test-E4A2VmComputerName', 'Assert-E4A2InsideVm', 'Initialize-E4A2K2Native', 'Save-E4A2K2Shot', 'ConvertTo-E4A2SearchPhrase', 'Get-E4A2WinRtPackages', 'Get-E4A2K2Appx', 'Get-E4A2K2Search', 'Invoke-E4A2K2Remove', 'Invoke-E4A2K2Ops')
}

function New-E4A2ChildScript {
    # Skrypt zadania K2: param(ResultFile, OpsFile), definicje funkcji, Invoke-E4A2K2Ops na liście z pliku JSON, wynik atomowo przez plik tymczasowy.
    [CmdletBinding()]
    [OutputType([string])]
    param()
    $Header = @'
param([Parameter(Mandatory)][string]$ResultFile, [Parameter(Mandatory)][string]$OpsFile)
$ErrorActionPreference = 'Stop'
'@
    $Definitions = foreach ($Name in Get-E4A2ChildFunctionNames) { "function $Name {`n$((Get-Command -Name $Name -CommandType Function -ErrorAction Stop).Definition)`n}" }
    $Main = @'
$Document = [ordered]@{ Ok = $false; Error = $null; ProcessId = $PID; Results = @() }
try {
    Assert-E4A2InsideVm
    $Parsed = Get-Content -LiteralPath $OpsFile -Raw -Encoding UTF8 | ConvertFrom-Json
    $Ops = @(foreach ($Item in $Parsed) { $Item })
    $Document.Results = @(Invoke-E4A2K2Ops -Ops $Ops)
    $Document.Ok = $true
}
catch { $Document.Error = $_.Exception.Message }
$Temporary = $ResultFile + '.tmp'
$Document | ConvertTo-Json -Depth 10 | Set-Content -LiteralPath $Temporary -Encoding UTF8
Move-Item -LiteralPath $Temporary -Destination $ResultFile -Force
'@
    return ($Header + "`n`n" + ($Definitions -join "`n`n") + "`n`n" + $Main)
}

function Invoke-E4A2UserTask {
    # K2: zadanie Harmonogramu (dso / Interactive / Highest) z listą operacji; wynik z pliku; zadanie ZAWSZE wyrejestrowane.
    # Niedostępność K2 to dana (Available=false, Reason), nie wyjątek.
    [CmdletBinding()]
    param(
        [Parameter(Mandatory)][ValidatePattern('^[a-z0-9-]+\z')][string]$Name,
        [Parameter(Mandatory)][AllowEmptyCollection()][object[]]$Ops,
        [ValidateRange(10, 1800)][int]$TimeoutSeconds = 240,
        [ValidatePattern('^[A-Za-z0-9_-]+\z')][string]$UserName = 'dso'
    )
    Assert-E4A2InsideVm
    $Directory = 'C:\dso\e4a2'
    $TaskName = "DSO-E4A2-$Name"
    $ScriptPath = "$Directory\k2.ps1"
    $OpsPath = "$Directory\$Name.ops.json"
    $ResultPath = "$Directory\$Name.json"
    $Out = [ordered]@{ Name = $Name; Available = $false; Reason = $null; WaitedMs = $null; ChildError = $null; UnregisterError = $null; Results = @() }
    $Registered = $false
    $Watch = [System.Diagnostics.Stopwatch]::StartNew()
    try {
        $null = New-Item -ItemType Directory -Path $Directory -Force
        Set-Content -LiteralPath $ScriptPath -Value (New-E4A2ChildScript) -Encoding UTF8
        Set-Content -LiteralPath $OpsPath -Value (ConvertTo-Json -InputObject @($Ops) -Depth 6) -Encoding UTF8
        if (Test-Path -LiteralPath $ResultPath) { Remove-Item -LiteralPath $ResultPath -Force }
        $PowerShellPath = Join-Path $env:SystemRoot 'System32\WindowsPowerShell\v1.0\powershell.exe'
        $Action = New-ScheduledTaskAction -Execute $PowerShellPath -Argument "-NoProfile -NonInteractive -ExecutionPolicy Bypass -WindowStyle Hidden -File $ScriptPath -ResultFile $ResultPath -OpsFile $OpsPath"
        $Principal = New-ScheduledTaskPrincipal -UserId $UserName -LogonType Interactive -RunLevel Highest
        $Settings = New-ScheduledTaskSettingsSet -AllowStartIfOnBatteries -DontStopIfGoingOnBatteries -ExecutionTimeLimit (New-TimeSpan -Seconds ($TimeoutSeconds + 60))
        $Registered = $true
        $null = Register-ScheduledTask -TaskName $TaskName -Action $Action -Principal $Principal -Settings $Settings -Force -ErrorAction Stop
        Start-ScheduledTask -TaskName $TaskName -ErrorAction Stop
        $Found = $false
        for ($Poll = 1; $Poll -le $TimeoutSeconds * 2; $Poll++) {
            if (Test-Path -LiteralPath $ResultPath) { $Found = $true; break }
            Start-Sleep -Milliseconds 500
        }
        $Out.WaitedMs = [int]$Watch.ElapsedMilliseconds
        if ($Found) {
            Start-Sleep -Milliseconds 300
            $Child = Get-Content -LiteralPath $ResultPath -Raw -Encoding UTF8 | ConvertFrom-Json
            $Out.Available = $true
            if (-not $Child.Ok) { $Out.ChildError = $Child.Error }
            $Out.Results = @($Child.Results)
        }
        else {
            $Out.Reason = "K2 niedostępne: brak wyniku w $TimeoutSeconds s"
            try { Stop-ScheduledTask -TaskName $TaskName -ErrorAction Stop } catch { $Out.Reason += " | Stop-ScheduledTask: $($_.Exception.Message)" }
        }
    }
    catch { $Out.Reason = "K2 niedostępne: $($_.Exception.Message)" }
    finally {
        if ($Registered) {
            try { Unregister-ScheduledTask -TaskName $TaskName -Confirm:$false -ErrorAction Stop } catch { $Out.UnregisterError = $_.Exception.Message }
        }
    }
    return [pscustomobject]$Out
}

function Invoke-E4A2Collection {
    # Punkt wejścia po stronie VM (jedna faza): zwraca JEDEN string JSON.
    # inventory: spis (AppX K1 i K2, WinRT, provisioned, Uninstall, ochrona systemu, csc/COM); search: wyszukiwanie celów w Start (zadanie dso);
    # removal: usuwanie celów i spis K2 po; protection: odczyt -> włączenie -> odczyt -> wyłączenie -> odczyt; logoff: wylogowanie dso
    # i odczyt stanu LogoffTarget (ostatnia faza: po niej nie ma już sesji dso).
    [CmdletBinding()]
    param(
        [Parameter(Mandatory)][string]$UserSid,
        [Parameter(Mandatory)][string]$PlanJson,
        [Parameter(Mandatory)][ValidateSet('inventory', 'search', 'removal', 'protection', 'logoff')][string]$Phase,
        [ValidatePattern('^[a-z]*\z')][string]$Label = ''
    )
    Assert-E4A2InsideVm
    $Plan = $PlanJson | ConvertFrom-Json
    $Document = [ordered]@{ meta = $null; userSid = $UserSid; phase = $Phase; label = $Label }
    try { $Document.meta = Get-ExploreMeta } catch { $Document.meta = [ordered]@{ Error = $_.Exception.Message } }
    switch ($Phase) {
        'inventory' {
            $Document.appx = Get-E4A2AppxInventory -UserSid $UserSid
            $Document.winrt = Get-E4A2WinRtPackages -UserSid $UserSid
            $Document.provisioned = Get-E4A2ProvisionedInventory
            $Document.uninstall = @(Get-E4A2UninstallInventory -UserSid $UserSid -Roots @($Plan.UninstallRoots))
            $Document.k2 = Invoke-E4A2UserTask -Name 'appx' -Ops @(@{ Op = 'Appx'; UserSid = $UserSid }) -TimeoutSeconds 300
            $Document.protection = Get-E4A2SystemProtection
            $Document.tools = Get-E4A2ToolAvailability -CscRelativePath $Plan.CscRelativePath -InstallerProgId $Plan.InstallerProgId
        }
        'search' {
            # Fraza i rodzina pakietu z pierwszego wyszukiwania ('przed') są zapamiętane w VM, żeby 'po' szukało tego samego.
            $StatePath = 'C:\dso\e4a2\search-state.json'
            $Saved = @{}
            if (Test-Path -LiteralPath $StatePath) {
                foreach ($Property in (Get-Content -LiteralPath $StatePath -Raw -Encoding UTF8 | ConvertFrom-Json).PSObject.Properties) { $Saved[$Property.Name] = $Property.Value }
            }
            $Targets = @($Plan.Targets | Where-Object { $_.Search })
            $Ops = @()
            for ($Index = 0; $Index -lt $Targets.Count; $Index++) {
                $Target = $Targets[$Index]
                $Remembered = $Saved[[string]$Target.Name]
                $Ops += @{
                    Op = 'Search'; Name = [string]$Target.Name; Fallback = [string]$Target.Phrase; Label = "start-$Label-$($Index + 1)"; LeaveOpen = ($Index -eq $Targets.Count - 1)
                    Phrase = if ($null -ne $Remembered) { [string]$Remembered.Phrase } else { '' }; Pfn = if ($null -ne $Remembered) { [string]$Remembered.Pfn } else { '' }
                }
            }
            $Document.search = Invoke-E4A2UserTask -Name "search-$Label" -Ops $Ops -TimeoutSeconds 180
            if (-not (Test-Path -LiteralPath $StatePath)) {
                $State = [ordered]@{}
                foreach ($Result in @($Document.search.Results | Where-Object { $_.Op -eq 'Search' })) { $State[[string]$Result.Name] = [ordered]@{ Phrase = $Result.Phrase; Pfn = $Result.Pfn } }
                Set-Content -LiteralPath $StatePath -Value ($State | ConvertTo-Json -Depth 4) -Encoding UTF8
            }
        }
        'removal' {
            $Document.removal = @(Invoke-E4A2RemovalPhase -UserSid $UserSid -Targets @($Plan.Targets))
            $Document.k2After = Invoke-E4A2UserTask -Name 'appx-after' -Ops @(@{ Op = 'Appx'; UserSid = $UserSid }) -TimeoutSeconds 300
        }
        'protection' {
            $Document.protection = [ordered]@{
                Before       = Get-E4A2SystemProtection
                Enable       = Set-E4A2SystemProtection -Enable $true
                AfterEnable  = Get-E4A2SystemProtection
                Disable      = Set-E4A2SystemProtection -Enable $false
                AfterDisable = Get-E4A2SystemProtection
            }
        }
        'logoff' { $Document.logoff = Invoke-E4A2LogoffProbe -UserSid $UserSid -Name ([string]$Plan.LogoffTarget) }
    }
    return ($Document | ConvertTo-Json -Depth 14)
}

# ---------------------------------------------------------------------------------------------------------------------
# Strona hosta (nic z tego nie jest wysyłane do VM).
# ---------------------------------------------------------------------------------------------------------------------

function Get-E4A2RemoteFunctionNames {
    return @(
        'Test-E4A2VmComputerName', 'Assert-E4A2InsideVm', 'Resolve-E4A2Path', 'ConvertTo-E4A2ResourceReference', 'Test-E4A2SppVolume', 'Select-E4A2Present',
        'Initialize-E4A2Native', 'Resolve-E4A2DisplayName', 'Get-E4A2ManifestNames', 'Get-E4A2WinRtPackages', 'Invoke-E4A2WinRtRemove', 'Get-E4A2AppxInventory', 'Get-E4A2ProvisionedInventory',
        'Get-E4A2UninstallInventory', 'Get-E4A2SystemProtection', 'Set-E4A2SystemProtection', 'Get-E4A2ToolAvailability', 'Get-E4A2PackageState', 'Invoke-E4A2Removal',
        'Invoke-E4A2RemovalPhase', 'Invoke-E4A2LogoffProbe', 'Initialize-E4A2K2Native', 'Save-E4A2K2Shot', 'ConvertTo-E4A2SearchPhrase', 'Get-E4A2K2Appx', 'Get-E4A2K2Search', 'Invoke-E4A2K2Remove',
        'Invoke-E4A2K2Ops', 'Get-E4A2ChildFunctionNames', 'New-E4A2ChildScript', 'Invoke-E4A2UserTask', 'Invoke-E4A2Collection'
    )
}

function Get-E4A2HostOnlyFunctionNames {
    return @('Get-E4A2Plan', 'Get-E4A2RemoteFunctionNames', 'Get-E4A2HostOnlyFunctionNames', 'Get-E4A2RemoteScript', 'Assert-E4A2OutDirectory', 'Get-E4A2RunArguments',
        'Invoke-E4A2VmPhase', 'Complete-E4A2VmCleanup', 'Invoke-E4A2VmRun', 'Invoke-E4A2Exploration')
}

function Get-E4A2RemoteScript {
    # Tekst definicji funkcji zdalnych: część wspólna z modułu + funkcje E4A2 (FunctionInfo pobrane tu, w zasięgu skryptu).
    [CmdletBinding()]
    [OutputType([string])]
    param()
    $Names = @(Get-ExploreCommonRemoteFunctionNames) + @(Get-E4A2RemoteFunctionNames)
    return (ConvertTo-ExploreRemoteScript -Command @($Names | ForEach-Object { Get-Command -Name $_ -CommandType Function -ErrorAction Stop }))
}

function Assert-E4A2OutDirectory {
    # -OutDirectory: wymagany, bezwzględny (C:\... albo \\serwer\udział\...), poza repo; w repo tylko katalog ignorowany przez git.
    [CmdletBinding()]
    [OutputType([string])]
    param(
        [AllowEmptyString()][string]$Path,
        [Parameter(Mandatory)][string]$RepoRoot
    )
    if ([string]::IsNullOrWhiteSpace($Path)) { throw '-OutDirectory jest wymagany (ścieżka bezwzględna poza repo, np. $env:TEMP\dso-explore\exploration).' }
    if ($Path -notmatch '^[A-Za-z]:\\' -and $Path -notmatch '^\\\\[^\\]+\\[^\\]+') { throw "-OutDirectory musi być ścieżką bezwzględną (C:\... albo \\serwer\udział\...): '$Path'." }
    $Full = [System.IO.Path]::GetFullPath($Path).TrimEnd('\')
    $Root = [System.IO.Path]::GetFullPath($RepoRoot).TrimEnd('\')
    $InRepo = ($Full -ieq $Root) -or $Full.StartsWith("$Root\", [System.StringComparison]::OrdinalIgnoreCase)
    if ($InRepo -and -not (Test-ExploreGitIgnored -Path $Full -RepoRoot $Root)) { throw "-OutDirectory '$Full' leży w repo ('$Root') poza katalogiem ignorowanym przez git; wyniki eksploracji trzymaj poza repo albo w katalogu ignorowanym przez git." }
    return $Full
}

function Get-E4A2RunArguments {
    # Argumenty przebiegu po walidacji (domyślnie bez sieci: spis i usuwanie jej nie potrzebują) + cele z planu (dla -ShowPlan).
    [CmdletBinding()]
    param(
        [Parameter(Mandatory)][string[]]$VmName,
        [Parameter(Mandatory)][string]$OutDirectory,
        [Parameter(Mandatory)][string]$CheckpointName,
        [Parameter(Mandatory)][int]$ReadyTimeoutSeconds,
        [bool]$Network = $false
    )
    return [ordered]@{ VmName = @($VmName); OutDirectory = $OutDirectory; CheckpointName = $CheckpointName; ReadyTimeoutSeconds = $ReadyTimeoutSeconds; Network = $Network
        Targets = @((Get-E4A2Plan).Targets) }
}

function Invoke-E4A2VmPhase {
    # Jedna faza w VM przez otwartą sesję: definicje + Invoke-E4A2Collection; zwraca sparsowany dokument.
    [CmdletBinding()]
    param(
        [Parameter(Mandatory)][AllowNull()][System.Management.Automation.Runspaces.PSSession]$Session,
        [Parameter(Mandatory)][string]$RemoteScript,
        [Parameter(Mandatory)][string]$UserSid,
        [Parameter(Mandatory)][string]$PlanJson,
        [Parameter(Mandatory)][ValidateSet('inventory', 'search', 'removal', 'protection', 'logoff')][string]$Phase,
        [ValidatePattern('^[a-z]*\z')][string]$Label = ''
    )
    Assert-DsoVmLiveAllowed
    $Remote = {
        param([string]$Definitions, [string]$Sid, [string]$Json, [string]$Which, [string]$Tag)
        . ([scriptblock]::Create($Definitions))
        Invoke-E4A2Collection -UserSid $Sid -PlanJson $Json -Phase $Which -Label $Tag
    }
    $Raw = @(Invoke-Command -Session $Session -ScriptBlock $Remote -ArgumentList @($RemoteScript, $UserSid, $PlanJson, $Phase, $Label))
    $Text = [string]($Raw | Where-Object { $_ -is [string] } | Select-Object -Last 1)
    if ([string]::IsNullOrWhiteSpace($Text)) { throw "Faza '$Phase' nie zwróciła JSON." }
    return ($Text | ConvertFrom-Json)
}

function Complete-E4A2VmCleanup {
    # Koniec przebiegu: revert do checkpointu, odłączenie karty (gdy przebieg ją podłączał) i sprawdzenie kontraktu stanu końcowego:
    # VM w stanie Off i karta bez przełącznika (zawsze, także bez -Network). Błędy i naruszenia wracają w Problems (Ok=false).
    [CmdletBinding()]
    param(
        [Parameter(Mandatory)][ValidateSet('DSO-26H2-Home', 'DSO-26H2-Pro')][string]$VmName,
        [Parameter(Mandatory)][string]$CheckpointName,
        [Parameter(Mandatory)][bool]$Network
    )
    $Out = [ordered]@{ Ok = $false; RevertError = $null; DisconnectError = $null; CheckError = $null; FinalState = $null; FinalSwitch = $null; Violations = @(); Problems = @() }
    try { Restore-VmCleanState -VmName $VmName -CheckpointName $CheckpointName }
    catch { $Out.RevertError = $_.Exception.Message; $Out.Problems += "revert do '$CheckpointName': $($_.Exception.Message)" }
    if ($Network) {
        try { Disconnect-VMNetworkAdapter -VMName $VmName -ErrorAction Stop }
        catch { $Out.DisconnectError = $_.Exception.Message; $Out.Problems += "odłączenie sieci: $($_.Exception.Message)" }
    }
    try {
        $Out.FinalState = [string](Get-VM -Name $VmName -ErrorAction Stop).State
        $Out.FinalSwitch = (@(Get-VMNetworkAdapter -VMName $VmName -ErrorAction Stop | ForEach-Object { $_.SwitchName } | Where-Object { $_ }) -join ', ')
        if ($Out.FinalState -ne 'Off') { $Out.Violations += "VM w stanie '$($Out.FinalState)' zamiast 'Off'" }
        if ($Out.FinalSwitch) { $Out.Violations += "karta podłączona do '$($Out.FinalSwitch)'" }
    }
    catch { $Out.CheckError = $_.Exception.Message; $Out.Problems += "odczyt stanu końcowego: $($_.Exception.Message)" }
    if (@($Out.Violations).Count -gt 0) { $Out.Problems += "stan końcowy: $(@($Out.Violations) -join '; ')" }
    $Out.Ok = (@($Out.Problems).Count -eq 0)
    return [pscustomobject]$Out
}

function Invoke-E4A2VmRun {
    # Jedna VM: revert -> (sieć) -> start -> gotowość -> inventory -> search przed (+zrzut hosta) -> removal -> search po (+zrzut hosta)
    # -> protection -> logoff -> kopia zrzutów -> ZAWSZE revert, odłączenie sieci, sprawdzenie stanu końcowego -> zapis JSON. Zwraca ścieżkę JSON.
    [CmdletBinding()]
    [OutputType([string])]
    param(
        [Parameter(Mandatory)][ValidateSet('DSO-26H2-Home', 'DSO-26H2-Pro')][string]$VmName,
        [Parameter(Mandatory)][string]$OutDirectory,
        [string]$CheckpointName = 'czysta-26H2',
        [int]$ReadyTimeoutSeconds = 1200,
        [bool]$Network = $false
    )
    Assert-DsoVmLiveAllowed
    $Credential = Get-DsoVmCredential -VmName $VmName
    $Session = $null
    $StartedAt = Get-Date
    $Phases = [ordered]@{}
    $Thumbnails = @()
    $UserSid = $null
    $PrimaryError = $null
    $VmDirectory = Join-Path $OutDirectory $VmName
    try {
        Restore-VmCleanState -VmName $VmName -CheckpointName $CheckpointName
        if ($Network) { Connect-VMNetworkAdapter -VMName $VmName -SwitchName 'Default Switch' -ErrorAction Stop }
        Start-VM -Name $VmName
        $Session = Wait-VmReady -VmName $VmName -Credential $Credential -TimeoutSeconds $ReadyTimeoutSeconds
        $UserSid = [string](Invoke-Command -Session $Session -ScriptBlock { (Get-LocalUser -Name 'dso').SID.Value })
        if ([string]::IsNullOrWhiteSpace($UserSid)) { throw "Nie udało się odczytać SID użytkownika 'dso' w VM '$VmName'." }
        # Chwila na dokończenie logowania dso (pasek zadań, Start, rejestracja pakietów) przed pierwszym odczytem.
        Start-Sleep -Seconds 45
        $RemoteScript = Get-E4A2RemoteScript
        $PlanJson = ConvertTo-Json -InputObject (Get-E4A2Plan) -Depth 6
        $Steps = @(
            @{ Key = 'inventory'; Phase = 'inventory'; Label = ''; Thumbnail = $null }
            @{ Key = 'search-przed'; Phase = 'search'; Label = 'przed'; Thumbnail = 'host-start-przed.png' }
            @{ Key = 'removal'; Phase = 'removal'; Label = ''; Thumbnail = $null }
            @{ Key = 'search-po'; Phase = 'search'; Label = 'po'; Thumbnail = 'host-start-po.png' }
            @{ Key = 'protection'; Phase = 'protection'; Label = ''; Thumbnail = $null }
            @{ Key = 'logoff'; Phase = 'logoff'; Label = ''; Thumbnail = $null }
        )
        foreach ($Step in $Steps) {
            $Phases[$Step.Key] = Invoke-E4A2VmPhase -Session $Session -RemoteScript $RemoteScript -UserSid $UserSid -PlanJson $PlanJson -Phase $Step.Phase -Label $Step.Label
            if ($Step.Thumbnail) {
                $Shot = [ordered]@{ Label = $Step.Key; Path = $null; Error = $null }
                try {
                    $null = New-Item -ItemType Directory -Path $VmDirectory -Force
                    $Shot.Path = Save-DsoVmThumbnail -VmName $VmName -Path (Join-Path $VmDirectory $Step.Thumbnail)
                }
                catch { $Shot.Error = $_.Exception.Message }
                $Thumbnails += [pscustomobject]$Shot
            }
        }
    }
    catch {
        $PrimaryError = $_.Exception.Message
        if (Test-VmAuthFailure -Exception $_.Exception) { throw }
    }
    finally {
        $Shots = Join-Path $VmDirectory 'shots'
        if ($null -ne $Session) {
            try {
                $null = New-Item -ItemType Directory -Path $Shots -Force
                Copy-Item -FromSession $Session -Path 'C:\dso\e4a2\shots\*.png' -Destination $Shots -ErrorAction Stop
            }
            catch { $PrimaryError = (@($PrimaryError, "kopia zrzutów: $($_.Exception.Message)") | Where-Object { $_ }) -join ' | ' }
            Remove-PSSession -Session $Session -ErrorAction SilentlyContinue
        }
        # Sprzątanie PRZED zapisem, żeby jego wynik (także błędy) trafił do JSON; błąd sprzątania nigdy nie jest ukrywany.
        $Cleanup = Complete-E4A2VmCleanup -VmName $VmName -CheckpointName $CheckpointName -Network $Network
        if (-not $Cleanup.Ok) {
            $CleanupMessage = "sprzątanie VM: $(@($Cleanup.Problems) -join ' | ')"
            Write-Warning "VM '$VmName': $CleanupMessage"
            $PrimaryError = (@($PrimaryError, $CleanupMessage) | Where-Object { $_ }) -join ' | '
        }
        $Result = [ordered]@{ schema = 1; vmName = $VmName; checkpoint = $CheckpointName; network = $Network; userSid = $UserSid; startedAt = $StartedAt.ToString('o')
            finishedAt = (Get-Date).ToString('o'); error = $PrimaryError; cleanup = $Cleanup; thumbnails = $Thumbnails; shots = $Shots; phases = $Phases }
        $Saved = Save-ExploreResult -Result $Result -OutDirectory $OutDirectory -VmName $VmName -FileName 'e4a2.json'
    }
    if ($PrimaryError) { throw "VM '$VmName': $PrimaryError (wynik częściowy: $Saved)" }
    return $Saved
}

function Invoke-E4A2Exploration {
    # Wszystkie wybrane VM po kolei. Błąd uwierzytelnienia = stop całości; inny błąd = zapis i dalej.
    [CmdletBinding()]
    param([Parameter(Mandatory)][System.Collections.IDictionary]$Arguments)
    $Paths = New-Object System.Collections.Generic.List[string]
    $Errors = New-Object System.Collections.Generic.List[string]
    foreach ($Vm in $Arguments.VmName) {
        try {
            Write-Host "$(Get-Date -Format 'HH:mm:ss') $Vm"
            $Paths.Add((Invoke-E4A2VmRun -VmName $Vm -OutDirectory $Arguments.OutDirectory -CheckpointName $Arguments.CheckpointName -ReadyTimeoutSeconds $Arguments.ReadyTimeoutSeconds -Network $Arguments.Network))
        }
        catch {
            if (Test-VmAuthFailure -Exception $_.Exception) { throw }
            $Errors.Add("VM '$Vm': $($_.Exception.Message)")
        }
    }
    return [pscustomobject]@{ Paths = $Paths.ToArray(); Errors = $Errors.ToArray() }
}

# Przy dot-sourcingu (testy) tylko definicje; uruchomienie przebiegu tylko przy zwykłym wywołaniu skryptu.
if ($MyInvocation.InvocationName -ne '.') {
    $ExitCode = 1
    try {
        Set-StrictMode -Version Latest
        $ErrorActionPreference = 'Stop'
        $VmRoot = Split-Path -Parent $PSScriptRoot
        Import-Module (Join-Path $VmRoot 'modules\VmCommon.psm1')
        $OutDirectory = Assert-E4A2OutDirectory -Path $OutDirectory -RepoRoot (Get-DsoRepoRoot)
        $OutDirectory = Resolve-ExploreOutDirectory -Path $OutDirectory
        $RunArguments = Get-E4A2RunArguments -VmName $VmName -OutDirectory $OutDirectory -CheckpointName $CheckpointName -ReadyTimeoutSeconds $ReadyTimeoutSeconds -Network ([bool]$Network)
        if ($ShowPlan) {
            [pscustomobject]$RunArguments | ConvertTo-Json -Depth 5
            exit 0
        }
        Import-Module (Join-Path $VmRoot 'modules\VmCommon.psm1') -Force
        Import-Module (Join-Path $VmRoot 'modules\VmRun.psm1') -Force
        Import-Module (Join-Path $VmRoot 'modules\VmHost.psm1') -Force
        # ExploreCommon i VmSmoke (zaimportowane wyżej) trzymają referencję do starej instancji VmHost; ponowny import po nich wiąże je z bieżącą.
        Import-Module (Join-Path $PSScriptRoot 'ExploreCommon.psm1') -Force
        Import-Module (Join-Path $VmRoot 'modules\VmSmoke.psm1') -Force
        $Principal = New-Object System.Security.Principal.WindowsPrincipal([System.Security.Principal.WindowsIdentity]::GetCurrent())
        if (-not $Principal.IsInRole([System.Security.Principal.WindowsBuiltInRole]::Administrator)) {
            throw 'Uruchom skrypt w sesji PowerShell jako administrator (Hyper-V i PowerShell Direct tego wymagają).'
        }
        Write-Host "Wyniki: $OutDirectory"
        Write-Host "VM: $($RunArguments.VmName -join ', '); sieć: $($RunArguments.Network)"
        $Outcome = Invoke-E4A2Exploration -Arguments $RunArguments
        foreach ($Path in $Outcome.Paths) { Write-Host "Zapisano: $Path" }
        foreach ($Message in $Outcome.Errors) { Write-Host "BŁĄD $Message" -ForegroundColor Red }
        $ExitCode = if (@($Outcome.Errors).Count -eq 0) { 0 } else { 1 }
    }
    catch {
        Write-Host "BŁĄD: $($_.Exception.Message)" -ForegroundColor Red
        Write-Host $_.ScriptStackTrace -ForegroundColor DarkGray
        $ExitCode = 1
    }
    exit $ExitCode
}
