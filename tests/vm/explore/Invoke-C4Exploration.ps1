<#
Instrukcja uruchomienia:
    Sesja PowerShell uruchomiona jako administrator, na hoście z Hyper-V, z modułami testów VM (tests\vm\modules) i tests\vm\explore\ExploreCommon.psm1.
    VM utworzone wcześniej przez New-TestVm.ps1 (checkpoint 'czysta-26H2', poświadczenia w %LOCALAPPDATA%\DSOptimizer-vm).
    Obie VM, po kolei (Home, potem Pro):
        .\tests\vm\explore\Invoke-C4Exploration.ps1 -OutDirectory C:\ścieżka\do\wyników
    Jedna VM:
        .\tests\vm\explore\Invoke-C4Exploration.ps1 -VmName DSO-26H2-Pro -OutDirectory C:\ścieżka\do\wyników
    Bez -OutDirectory wynik trafia do tests\vm\out\c4-<data> (katalog out\ jest w .gitignore).
    Czas: kilka do kilkunastu minut na VM. Brak szyfrowania i odszyfrowania C: (decyzja Dawida): BitLocker tylko odczyt.
Opis działania:
    Skrypt zbiera dowody z prawdziwej VM 26H2 dla partii C4 (pozycje sec.*: BitLocker i menu "Przejmij na własność").
    Zapisuje FAKTY (surowe wartości), bez interpretacji. Wszystkie zmiany są WYŁĄCZNIE wewnątrz VM (na hoście nic się nie zmienia),
    a VM wraca do checkpointu 'czysta-26H2' (klucze menu, polityka i pliki testowe znikają razem z nim).
    Sondy (numery = "Otwarte pytania dla eksploracji VM" w docs\zrodla\2026-10-18-partia-c4-zrodla.md):
      bitlockerPolicy  (1)   - tylko odczyt: HKLM\SYSTEM\CurrentControlSet\Control\BitLocker (klucz, PreventDeviceEncryption).
      bitlockerState   (2)   - tylko odczyt (Home i Pro): Win32_EncryptableVolume (C:), GetConversionStatus, GetProtectionStatus,
                               Get-BitLockerVolume, obecność modułu BitLocker. Błąd lub pustka to dane, nie błąd przebiegu.
      shellClasses     (3)   - tylko odczyt: podklucze HKLM\SOFTWARE\Classes\{*,Directory,Drive,Folder,AllFileSystemObjects}\shell.
      guestPolicy      (7)   - polityka "Enable insecure guest logons" (MS Policy CSP LanmanWorkstation/EnableInsecureGuestLogons, ADMX:
                               HKLM\SOFTWARE\Policies\Microsoft\Windows\LanmanWorkstation, wartość AllowInsecureGuestAuth): odczyt
                               Get-SmbClientConfiguration (EnableInsecureGuestLogons) przed, zapis DWORD 1, odczyt po (i Kind odczytany z
                               rejestru, bo MS nie podaje typu), usunięcie wartości, odczyt po usunięciu.
      takeown          (5)   - takeown /f <folder> /a /r /d Y oraz icacls <folder> /setowner *S-1-5-32-544 /t /c /l na drzewach w
                               C:\DsoTest (z podfolderem bez List Folder dla bieżącego użytkownika); właściciel (SID) i SDDL przed i po.
                               Drzewa mają właściciela wyjściowego innego niż Administratorzy (konto dso albo SYSTEM), żeby zmiana była widoczna.
      menu             (4)   - wariantowe klucze menu w HKLM\SOFTWARE\Classes\...\shell\DSOptimizer.TakeOwnership (zapis tylko pod tę
                               ścieżkę, patrz Assert-C4WritePath): struktury Plain (kontrola), Subkey (podklucz ExtendedSubCommandsKey\Shell\runas,
                               jak w opisie MS), ValuePath (wartość ExtendedSubCommandsKey wskazująca ten podklucz); z Extended i bez; rejestracja
                               pod Directory/Folder/Drive/AllFileSystemObjects (pytanie: czy verb jest na C:\). (4a) lista verbów przez
                               Shell.Application NameSpace().ParseName().Verbs() dla pliku i folderu w C:\DsoTest, C:\, C:\Windows,
                               C:\Windows\System32 (każde wyliczenie w osobnym procesie powershell.exe po SHChangeNotify(SHCNE_ASSOCCHANGED)).
                               (4b) AppliesTo: warianty A, B, C ze źródeł MS (NOT / ~< na System.ItemPathDisplay lub System.ParsingPath)
                               oraz kontrola pozytywna System.ItemName. (4c) InvokeVerb na pliku i folderze testowym + właściciel po, token
                               sondy (poziom High => "nie rozstrzygnięte", bo PS Direct nie ma UAC).
      cmdline          (6)   - linia komendy z menu przez cmd.exe /c (%1 podstawione jak robi to powłoka) na plikach i folderach o nazwach ze
                               spacjami, polskimi znakami, %, &, ^, !: właściciel docelowego elementu i lista wszystkich zmian w C:\DsoTest.
    Metadane: CurrentBuild, UBR, EditionID, DisplayVersion, ProductName, Culture/UICulture, PSVersion; token sondy (SID, grupy, poziom integralności).
    Każdy krok zapisuje: komendę, kod wyjścia, surowe stdout/stderr, wartości rejestru jako {Exists, Kind, Value, Hex} i czas.
    Brak wartości (Exists=false) to nie to samo co wartość 0.
    Przebieg na każdej VM (moduł ExploreCommon): przywrócenie checkpointu -> start -> Wait-VmReady -> sesja PowerShell Direct ->
    zbieranie, a na końcu ZAWSZE (finally) zatrzymanie i powrót do checkpointu. Pierwszy błąd uwierzytelnienia przerywa cały skrypt
    (bez prób na drugiej VM, blokada konta). Inny błąd jednej VM jest zapisany i nie przerywa drugiej.
    Wynik: <OutDirectory>\<VmName>\c4-exploration.json (UTF-8).
    Wybór sond: -Probe menu,cmdline (nazwy: bitlockerPolicy, bitlockerState, shellClasses, guestPolicy, takeown, menu, cmdline); bez -Probe wszystkie.
    Ręczny test UAC (PS Direct ma pełny token Administratora bez UAC, więc G1 rozstrzyga człowiek w sesji użytkownika dso), jedna VM:
        .\tests\vm\explore\Invoke-C4Exploration.ps1 -PrepareManualUacTest -VmName DSO-26H2-Pro -Variant Subkey [-WithExtended] -OutDirectory <katalog>
          => revert, start, zapis wariantu menu, pliki testowe C:\DsoTest\manual-uac (właściciel: dso), VM ZOSTAJE URUCHOMIONA, skrypt drukuje instrukcję.
        .\tests\vm\explore\Invoke-C4Exploration.ps1 -FinishManualUacTest -VmName DSO-26H2-Pro -OutDirectory <katalog>
          => odczyt właściciela i ACL elementów testowych (c4-manual-uac-finish.json), zatrzymanie i revert do czysta-26H2 (zawsze, także po błędzie).
    Skrypt dotyka wyłącznie VM DSO-26H2-Home i DSO-26H2-Pro (ValidateSet), nigdy Bruteforce_LTSC.
    Kod wyjścia: 0 gdy wszystkie wybrane VM zebrane bez błędu, 1 w przeciwnym razie.
#>
[CmdletBinding()]
param(
    [ValidateSet('DSO-26H2-Home', 'DSO-26H2-Pro')][string[]]$VmName = @('DSO-26H2-Home', 'DSO-26H2-Pro'),
    [string]$OutDirectory,
    [string]$CheckpointName = 'czysta-26H2',
    [ValidateRange(60, 7200)][int]$ReadyTimeoutSeconds = 1200,
    [ValidateSet('bitlockerPolicy', 'bitlockerState', 'shellClasses', 'guestPolicy', 'takeown', 'menu', 'cmdline')][string[]]$Probe,
    [switch]$PrepareManualUacTest,
    [switch]$FinishManualUacTest,
    [ValidateSet('Plain', 'Subkey', 'ValuePath')][string]$Variant = 'Subkey',
    [switch]$WithExtended
)

# Wspólna orkiestracja i narzędzia odczytu (rejestr, program natywny, rekord kroku) są w ExploreCommon.psm1.
Import-Module (Join-Path $PSScriptRoot 'ExploreCommon.psm1') -Force

# ---------------------------------------------------------------------------------------------------------------------
# Funkcje wysyłane do VM (samowystarczalne: wołają tylko siebie nawzajem, funkcje Explore z modułu i cmdlety systemowe).
# Lista w Get-C4RemoteFunctionNames (część wspólna: Get-ExploreCommonRemoteFunctionNames).
# Uwaga: bloki -Action/-After kroków widzą zmienne funkcji sondy przez zasięg dynamiczny; nie nazywaj w nich zmiennych jak
# zmienne Invoke-ExploreStep (Name, Command, Record, Errors, Values, Keys, Output, Last, Spec).
# ---------------------------------------------------------------------------------------------------------------------

function Get-C4EnumPair {
    # Wartość enuma/liczby jako [string] i [int] (Int=$null, gdy nie da się rzutować). Nic nie jest interpretowane.
    [CmdletBinding()]
    param($Value)
    $Pair = [ordered]@{ String = $null; Int = $null }
    if ($null -eq $Value) { return [pscustomobject]$Pair }
    $Pair.String = [string]$Value
    try { $Pair.Int = [int]$Value } catch { $Pair.Int = $null }
    return [pscustomobject]$Pair
}

function Invoke-C4Read {
    # Odczyt przez cmdlet: wynik albo błąd jako DANE (Ok, Error, Count, Items). Pusty wynik to Ok=true, Count=0.
    [CmdletBinding()]
    param([Parameter(Mandatory)][scriptblock]$Script)
    $Result = [ordered]@{ Ok = $false; Error = $null; Count = 0; Items = @() }
    try {
        $Found = @(& $Script)
        $Result.Items = @($Found)
        $Result.Count = $Found.Count
        $Result.Ok = $true
    }
    catch { $Result.Error = $_.Exception.Message }
    return $Result
}

function Get-C4TokenInfo {
    # Token procesu sondy: SID, grupy (SID), czy rola Administrator, czy jest SID poziomu integralności High (S-1-16-12288).
    [CmdletBinding()]
    param()
    $Info = [ordered]@{
        UserName = $null; UserSid = $null; IsInRoleAdministrator = $null; HasHighIntegritySid = $null
        IntegritySids = @(); GroupSids = @(); ImpersonationLevel = $null; Error = $null
    }
    try {
        $Identity = [System.Security.Principal.WindowsIdentity]::GetCurrent()
        $Principal = New-Object System.Security.Principal.WindowsPrincipal($Identity)
        $Groups = @($Identity.Groups | ForEach-Object { $_.Value })
        $Info.UserName = [string]$Identity.Name
        $Info.UserSid = [string]$Identity.User.Value
        $Info.IsInRoleAdministrator = [bool]$Principal.IsInRole([System.Security.Principal.WindowsBuiltInRole]::Administrator)
        $Info.GroupSids = @($Groups)
        $Info.IntegritySids = @($Groups | Where-Object { $_ -like 'S-1-16-*' })
        $Info.HasHighIntegritySid = [bool](@($Groups) -contains 'S-1-16-12288')
        $Info.ImpersonationLevel = [string]$Identity.ImpersonationLevel
    }
    catch { $Info.Error = $_.Exception.Message }
    return [pscustomobject]$Info
}

function Get-C4OwnerInfo {
    # Właściciel (SID i nazwa) i SDDL ścieżki; brak ścieżki = Exists=false; błąd jako dane.
    [CmdletBinding()]
    param(
        [Parameter(Mandatory)][string]$Path,
        [switch]$NoSddl
    )
    $Info = [ordered]@{ Path = $Path; Exists = $false; OwnerSid = $null; Owner = $null; Sddl = $null; Error = $null }
    try {
        if (-not (Test-Path -LiteralPath $Path)) { return [pscustomobject]$Info }
        $Info.Exists = $true
        $Acl = Get-Acl -LiteralPath $Path -ErrorAction Stop
        $Info.Owner = [string]$Acl.Owner
        $Info.OwnerSid = [string]($Acl.GetOwner([System.Security.Principal.SecurityIdentifier])).Value
        if (-not $NoSddl) { $Info.Sddl = [string]$Acl.Sddl }
    }
    catch { $Info.Error = $_.Exception.Message }
    return [pscustomobject]$Info
}

# --- Zapis do rejestru: JEDYNE miejsca z zapisem, każde zaczyna się od Assert-C4WritePath (test AST pilnuje kolejności) ---

function Assert-C4WritePath {
    # Dozwolone zapisy (HKLM, ścieżka względna): drzewo menu DSOptimizer.TakeOwnership pod pięcioma klasami powłoki albo klucz
    # polityki LanmanWorkstation (sonda 7). Cokolwiek innego (cudze verby, runas, BitLocker, reszta Windows) = wyjątek przed dotknięciem rejestru.
    [CmdletBinding()]
    param([Parameter(Mandatory)][AllowEmptyString()][string]$SubKeyPath)
    $MenuPattern = '^SOFTWARE\\Classes\\(\*|Directory|Drive|Folder|AllFileSystemObjects)\\shell\\DSOptimizer\.TakeOwnership(\\[^\\]+)*\z'
    $PolicyPattern = '^SOFTWARE\\Policies\\Microsoft\\Windows\\LanmanWorkstation\z'
    if ($SubKeyPath -notmatch $MenuPattern -and $SubKeyPath -notmatch $PolicyPattern) {
        throw "Zapis do rejestru poza dozwolonymi ścieżkami sondy (dozwolone tylko drzewo DSOptimizer.TakeOwnership i klucz polityki LanmanWorkstation): '$SubKeyPath'."
    }
}

function Set-C4RegistryValue {
    [CmdletBinding()]
    param(
        [Parameter(Mandatory)][string]$SubKeyPath,
        [Parameter(Mandatory)][AllowEmptyString()][string]$Name,
        [Parameter(Mandatory)][AllowEmptyString()]$Value,
        [Parameter(Mandatory)][ValidateSet('String', 'DWord')][string]$Kind
    )
    Assert-C4WritePath -SubKeyPath $SubKeyPath
    # CreateSubKey zamiast New-Item: nazwa klasy '*' byłaby wzorcem w ścieżce dostawcy rejestru.
    $Key = [Microsoft.Win32.Registry]::LocalMachine.CreateSubKey($SubKeyPath)
    try { $Key.SetValue($Name, $Value, [Microsoft.Win32.RegistryValueKind]$Kind) }
    finally { $Key.Dispose() }
}

function Remove-C4RegistryValue {
    [CmdletBinding()]
    param(
        [Parameter(Mandatory)][string]$SubKeyPath,
        [Parameter(Mandatory)][string]$Name
    )
    Assert-C4WritePath -SubKeyPath $SubKeyPath
    $Key = [Microsoft.Win32.Registry]::LocalMachine.OpenSubKey($SubKeyPath, $true)
    if ($null -ne $Key) {
        try { $Key.DeleteValue($Name, $false) }
        finally { $Key.Dispose() }
    }
}

function Remove-C4RegistryTree {
    [CmdletBinding()]
    param([Parameter(Mandatory)][string]$SubKeyPath)
    Assert-C4WritePath -SubKeyPath $SubKeyPath
    [Microsoft.Win32.Registry]::LocalMachine.DeleteSubKeyTree($SubKeyPath, $false)
}

# --- Menu: linie komend, warianty, rejestracja ---

function Get-C4MenuCommandLine {
    # Linia komendy verbu (hipoteza z research sekcja 3). Pliki: takeown /a + icacls /grant; foldery: rekurencja z /d Y i /t.
    # SID grupy Administratorzy (*S-1-5-32-544), nie nazwa (polski Windows). %1 podstawia powłoka.
    [CmdletBinding()]
    param([Parameter(Mandatory)][ValidateSet('File', 'Folder')][string]$Kind)
    $Arguments = if ($Kind -eq 'File') { '/c takeown /f "%1" /a && icacls "%1" /grant *S-1-5-32-544:F /c /l /q' }
    else { '/c takeown /f "%1" /a /r /d Y && icacls "%1" /grant *S-1-5-32-544:F /t /c /l /q' }
    return [pscustomobject]@{ Kind = $Kind; Exe = 'cmd.exe'; Arguments = $Arguments; Full = "cmd.exe $Arguments" }
}

function Set-C4MenuVariant {
    # Rejestruje JEDEN wariant menu pod jedną klasą powłoki (zapis przez Set-C4RegistryValue). Zwraca listę zapisanych wartości.
    #   Plain     - verb z MUIVerb i command (kontrola działania Verbs()/InvokeVerb, bez podmenu).
    #   Subkey    - MUIVerb + podklucz ExtendedSubCommandsKey\Shell\runas (MUIVerb, HasLUAShield, command), jak w opisie MS (research sekcja 3).
    #   ValuePath - jak Subkey, a dodatkowo wartość ExtendedSubCommandsKey (REG_SZ) ze ścieżką względem HKCR do tego podklucza.
    [CmdletBinding()]
    param(
        [Parameter(Mandatory)][ValidateSet('*', 'Directory', 'Drive', 'Folder', 'AllFileSystemObjects')][string]$Class,
        [Parameter(Mandatory)][ValidateSet('Plain', 'Subkey', 'ValuePath')][string]$Structure,
        [switch]$Extended,
        [string]$AppliesTo
    )
    $Root = "SOFTWARE\Classes\$Class\shell\DSOptimizer.TakeOwnership"
    $CommandKind = if ($Class -eq '*') { 'File' } else { 'Folder' }
    $CommandLine = (Get-C4MenuCommandLine -Kind $CommandKind).Full
    $Entries = New-Object System.Collections.Generic.List[object]
    $Entries.Add(@{ Path = $Root; Name = 'MUIVerb'; Value = 'DSO: Przejmij na własność' })
    if ($Extended) { $Entries.Add(@{ Path = $Root; Name = 'Extended'; Value = '' }) }
    if ($AppliesTo) { $Entries.Add(@{ Path = $Root; Name = 'AppliesTo'; Value = $AppliesTo }) }
    if ($Structure -eq 'Plain') {
        $Entries.Add(@{ Path = "$Root\command"; Name = ''; Value = $CommandLine })
    }
    else {
        if ($Structure -eq 'ValuePath') { $Entries.Add(@{ Path = $Root; Name = 'ExtendedSubCommandsKey'; Value = "$Class\shell\DSOptimizer.TakeOwnership\ExtendedSubCommandsKey" }) }
        $Sub = "$Root\ExtendedSubCommandsKey\Shell\runas"
        $Entries.Add(@{ Path = $Sub; Name = 'MUIVerb'; Value = 'DSO: Przejmij (administrator)' })
        $Entries.Add(@{ Path = $Sub; Name = 'HasLUAShield'; Value = '' })
        $Entries.Add(@{ Path = "$Sub\command"; Name = ''; Value = $CommandLine })
    }
    $Written = @()
    foreach ($Entry in $Entries) {
        Set-C4RegistryValue -SubKeyPath $Entry.Path -Name $Entry.Name -Value $Entry.Value -Kind 'String'
        $Written += [pscustomobject]@{ SubKeyPath = $Entry.Path; Name = $Entry.Name; Kind = 'String'; Value = $Entry.Value }
    }
    return $Written
}

function Remove-C4MenuVariant {
    [CmdletBinding()]
    param([Parameter(Mandatory)][ValidateSet('*', 'Directory', 'Drive', 'Folder', 'AllFileSystemObjects')][string]$Class)
    Remove-C4RegistryTree -SubKeyPath "SOFTWARE\Classes\$Class\shell\DSOptimizer.TakeOwnership"
}

function Get-C4MenuClasses {
    [CmdletBinding()]
    param()
    return @('*', 'Directory', 'Drive', 'Folder', 'AllFileSystemObjects')
}

function Get-C4MenuVariantPlan {
    # Warianty (4a, 4b). AppliesTo A/B/C: research-aqs-policy.md "Propozycja sond VM"; kontrola pozytywna: przykład MS System.ItemName.
    [CmdletBinding()]
    param()
    $AqsA = 'NOT System.ItemPathDisplay:="C:\Windows"'
    $AqsB = 'NOT (System.ItemPathDisplay:="C:\Windows" OR System.ItemPathDisplay:~<"C:\Windows\" OR System.ItemPathDisplay:="C:\")'
    $AqsC = 'NOT System.ParsingPath:="C:\Windows"'
    $AqsK = 'System.ItemName:="folder testowy"'
    $Plan = New-Object System.Collections.Generic.List[object]
    $Add = {
        param($Id, $Group, $Classes, $Structure, $ExtendedFlag, $Aqs)
        $Plan.Add([pscustomobject]@{ Id = $Id; Group = $Group; Classes = @($Classes); Structure = $Structure; Extended = [bool]$ExtendedFlag; AppliesTo = $Aqs })
    }
    & $Add 'p0-plain' 'kontrola' @('*', 'Directory') 'Plain' $false $null
    & $Add 'v1-subkey-extended' '4a' @('*', 'Directory') 'Subkey' $true $null
    & $Add 'v2-subkey-bez-extended' '4a' @('*', 'Directory') 'Subkey' $false $null
    & $Add 'v3-wartosc-extended' '4a' @('*', 'Directory') 'ValuePath' $true $null
    & $Add 'v4-wartosc-bez-extended' '4a' @('*', 'Directory') 'ValuePath' $false $null
    & $Add 'k1-folder-plain' 'klasy' @('Folder') 'Plain' $false $null
    & $Add 'k2-allfso-plain' 'klasy' @('AllFileSystemObjects') 'Plain' $false $null
    & $Add 'k3-drive-plain' 'klasy' @('Drive') 'Plain' $false $null
    & $Add 'aqs-kontrola-plain' '4b' @('Directory') 'Plain' $false $AqsK
    foreach ($Variant in @(@('a', $AqsA), @('b', $AqsB), @('c', $AqsC))) {
        & $Add "aqs-$($Variant[0])-plain" '4b' @('Directory') 'Plain' $false $Variant[1]
        & $Add "aqs-$($Variant[0])-subkey" '4b' @('Directory') 'Subkey' $false $Variant[1]
    }
    return $Plan.ToArray()
}

function Get-C4VerbTargets {
    [CmdletBinding()]
    param()
    return @(
        'C:\DsoTest\plik testowy.txt', 'C:\DsoTest\folder testowy', 'C:\', 'C:\Windows', 'C:\Windows\System32', 'C:\Windows\Temp', 'C:\Windows\win.ini'
    )
}

function Get-C4ListVerbsScript {
    # Tekst skryptu uruchamianego w OSOBNYM procesie powershell.exe (świeży proces = brak pamięci podręcznej skojarzeń z poprzedniego wariantu).
    [CmdletBinding()]
    [OutputType([string])]
    param()
    return @'
param(
    [Parameter(Mandatory)][string]$TargetsFile,
    [Parameter(Mandatory)][string]$ResultFile,
    [switch]$Notify,
    [switch]$DryRun
)
$ErrorActionPreference = 'Stop'
$Result = [ordered]@{ ProcessId = $PID; ChangeNotify = $null; ShellCreated = $false; Error = $null; Targets = @() }
try {
    if ($Notify) {
        Add-Type -Namespace DsoProbe -Name Native -MemberDefinition '[System.Runtime.InteropServices.DllImport("shell32.dll")] public static extern void SHChangeNotify(int wEventId, uint uFlags, System.IntPtr dwItem1, System.IntPtr dwItem2);'
        [DsoProbe.Native]::SHChangeNotify(0x08000000, 0, [System.IntPtr]::Zero, [System.IntPtr]::Zero)
        $Result.ChangeNotify = 'SHCNE_ASSOCCHANGED (0x08000000) wyslane'
        Start-Sleep -Seconds 2
    }
    $Shell = $null
    if (-not $DryRun) {
        $Shell = New-Object -ComObject Shell.Application
        $Result.ShellCreated = $true
    }
    # PS 5.1: ConvertFrom-Json zwraca tablicę jako JEDEN obiekt; bez rozwinięcia pętla widziałaby jeden "cel" (wszystkie ścieżki sklejone).
    $Parsed = Get-Content -LiteralPath $TargetsFile -Raw -Encoding UTF8 | ConvertFrom-Json
    $Targets = @($Parsed | ForEach-Object { $_ })
    foreach ($Target in $Targets) {
        $Entry = [ordered]@{ Path = [string]$Target; Found = $false; VerbCount = 0; VerbNames = @(); Error = $null }
        try {
            if ($DryRun) { $Result.Targets += [pscustomobject]$Entry; continue }
            $Parent = Split-Path -Parent $Target
            if ([string]::IsNullOrEmpty($Parent)) {
                $Item = $Shell.NameSpace(17).ParseName($Target)
            }
            else {
                $Item = $Shell.NameSpace($Parent).ParseName((Split-Path -Leaf $Target))
            }
            if ($null -ne $Item) {
                $Entry.Found = $true
                $VerbNames = @()
                foreach ($Verb in $Item.Verbs()) { $VerbNames += [string]$Verb.Name }
                $Entry.VerbNames = $VerbNames
                $Entry.VerbCount = $VerbNames.Count
            }
        }
        catch { $Entry.Error = $_.Exception.Message }
        $Result.Targets += [pscustomobject]$Entry
    }
}
catch { $Result.Error = $_.Exception.Message }
$Result | ConvertTo-Json -Depth 6 | Set-Content -LiteralPath $ResultFile -Encoding UTF8
'@
}

function Get-C4InvokeVerbScript {
    # Osobny proces: InvokeVerb(<nazwa>) na jednym elemencie. Wynik (Invoked, Error, lista verbów) do pliku JSON.
    [CmdletBinding()]
    [OutputType([string])]
    param()
    return @'
param(
    [Parameter(Mandatory)][string]$ItemPath,
    [Parameter(Mandatory)][string]$VerbName,
    [Parameter(Mandatory)][string]$ResultFile
)
$ErrorActionPreference = 'Stop'
$Result = [ordered]@{ ProcessId = $PID; ItemPath = $ItemPath; VerbName = $VerbName; Found = $false; VerbNames = @(); Invoked = $false; Error = $null }
try {
    $Shell = New-Object -ComObject Shell.Application
    $Item = $Shell.NameSpace((Split-Path -Parent $ItemPath)).ParseName((Split-Path -Leaf $ItemPath))
    if ($null -ne $Item) {
        $Result.Found = $true
        $VerbNames = @()
        foreach ($Verb in $Item.Verbs()) { $VerbNames += [string]$Verb.Name }
        $Result.VerbNames = $VerbNames
        $Item.InvokeVerb($VerbName)
        $Result.Invoked = $true
    }
}
catch { $Result.Error = $_.Exception.Message }
$Result | ConvertTo-Json -Depth 6 | Set-Content -LiteralPath $ResultFile -Encoding UTF8
'@
}

function Invoke-C4ChildScript {
    # Zapisuje skrypt do C:\DsoProbe (poza C:\DsoTest, żeby nie mieszać się ze zmianami właścicieli), uruchamia go w osobnym
    # powershell.exe (stdout/stderr/kod wyjścia w Native) i czyta plik wyniku JSON.
    [CmdletBinding()]
    param(
        [Parameter(Mandatory)][string]$ScriptText,
        [Parameter(Mandatory)][ValidatePattern('^[A-Za-z0-9-]+\.ps1\z')][string]$ScriptName,
        [Parameter(Mandatory)][ValidatePattern('^[A-Za-z0-9-]+\.json\z')][string]$ResultName,
        [string[]]$ScriptArguments = @(),
        [int]$TimeoutSeconds = 120
    )
    $ProbeDirectory = 'C:\DsoProbe'
    $ScriptPath = Join-Path $ProbeDirectory $ScriptName
    $ResultFile = Join-Path $ProbeDirectory $ResultName
    $null = New-Item -ItemType Directory -Path $ProbeDirectory -Force
    Set-Content -LiteralPath $ScriptPath -Value $ScriptText -Encoding UTF8
    if (Test-Path -LiteralPath $ResultFile) { Remove-Item -LiteralPath $ResultFile -Force }
    $ChildArguments = @('-NoProfile', '-NonInteractive', '-ExecutionPolicy', 'Bypass', '-File', $ScriptPath) + @($ScriptArguments) + @('-ResultFile', $ResultFile)
    $Native = Invoke-ExploreNative -FilePath 'powershell.exe' -Arguments $ChildArguments -TimeoutSeconds $TimeoutSeconds
    $Parsed = $null
    $ReadError = $null
    try {
        if (Test-Path -LiteralPath $ResultFile) { $Parsed = Get-Content -LiteralPath $ResultFile -Raw -Encoding UTF8 | ConvertFrom-Json }
        else { $ReadError = "brak pliku wyniku $ResultFile" }
    }
    catch { $ReadError = $_.Exception.Message }
    return [pscustomobject]@{ Native = $Native; Result = $Parsed; ReadError = $ReadError }
}

function Invoke-C4VerbListing {
    [CmdletBinding()]
    param(
        [Parameter(Mandatory)][string[]]$Targets,
        [switch]$Notify
    )
    $TargetsPath = 'C:\DsoProbe\targets.json'
    $null = New-Item -ItemType Directory -Path 'C:\DsoProbe' -Force
    Set-Content -LiteralPath $TargetsPath -Value (ConvertTo-Json -InputObject @($Targets)) -Encoding UTF8
    $ScriptArguments = @('-TargetsFile', $TargetsPath)
    if ($Notify) { $ScriptArguments += '-Notify' }
    return (Invoke-C4ChildScript -ScriptText (Get-C4ListVerbsScript) -ScriptName 'list-verbs.ps1' -ResultName 'list-verbs-result.json' -ScriptArguments $ScriptArguments)
}

function Invoke-C4InvokeVerb {
    [CmdletBinding()]
    param(
        [Parameter(Mandatory)][string]$ItemPath,
        [Parameter(Mandatory)][string]$VerbName
    )
    return (Invoke-C4ChildScript -ScriptText (Get-C4InvokeVerbScript) -ScriptName 'invoke-verb.ps1' -ResultName 'invoke-verb-result.json' `
            -ScriptArguments @('-ItemPath', $ItemPath, '-VerbName', $VerbName) -TimeoutSeconds 60)
}

# --- Drzewa testowe, właściciele, migawki ---

function Get-C4SeedOwnerSid {
    # Właściciel WYJŚCIOWY elementów testowych: konto 'dso' (jeśli istnieje), inaczej SYSTEM (S-1-5-18). Musi różnić się od S-1-5-32-544,
    # bo takeown /a i icacls /setowner dają Administratorów, a zmiana ma być widoczna.
    [CmdletBinding()]
    param()
    try {
        $Sid = [string](Get-LocalUser -Name 'dso' -ErrorAction Stop).SID.Value
        if (-not [string]::IsNullOrWhiteSpace($Sid)) { return [pscustomobject]@{ Sid = $Sid; Source = 'konto dso' } }
    }
    catch { $null = $_ }
    return [pscustomobject]@{ Sid = 'S-1-5-18'; Source = 'SYSTEM (brak konta dso)' }
}

function Get-C4TreePaths {
    # Ścieżki drzewa testowego (bez dotykania dysku).
    [CmdletBinding()]
    param([Parameter(Mandatory)][string]$Root)
    return [ordered]@{
        Root = $Root; File = (Join-Path $Root 'plik testowy.txt'); Folder = (Join-Path $Root 'folder testowy')
        Child = (Join-Path $Root 'folder testowy\wewnatrz.txt'); DeniedFolder = (Join-Path $Root 'folder testowy\podfolder bez list')
        Hidden = (Join-Path $Root 'folder testowy\podfolder bez list\ukryty.txt')
    }
}

function New-C4TestTree {
    # Drzewo: plik testowy.txt; folder testowy\{wewnatrz.txt, podfolder bez list\ukryty.txt}. Właściciel wyjściowy ustawiony icacls /setowner,
    # potem na podfolderze Deny ListDirectory (List Folder) dla bieżącego użytkownika. Błąd zapisany w polu Error.
    [CmdletBinding()]
    param(
        [Parameter(Mandatory)][string]$Root,
        [Parameter(Mandatory)][string]$SeedOwnerSid,
        # SID, któremu odbieramy List Folder na podfolderze (domyślnie bieżące konto procesu; w teście ręcznym: dso).
        [string]$DenyUserSid
    )
    $Tree = Get-C4TreePaths -Root $Root
    $Tree.SeedOwnerSid = $SeedOwnerSid
    $Tree.Seed = $null
    $Tree.Deny = $null
    $Tree.Error = $null
    try {
        $null = New-Item -ItemType Directory -Path $Tree.DeniedFolder -Force
        foreach ($FilePath in @($Tree.File, $Tree.Child, $Tree.Hidden)) { Set-Content -LiteralPath $FilePath -Value 'dso test' -Encoding UTF8 }
        $Tree.Seed = Invoke-ExploreNative -FilePath 'icacls.exe' -Arguments @($Root, '/setowner', "*$SeedOwnerSid", '/t', '/c', '/l', '/q') -TimeoutSeconds 120
        $UserSid = if ($DenyUserSid) { New-Object System.Security.Principal.SecurityIdentifier($DenyUserSid) } else { [System.Security.Principal.WindowsIdentity]::GetCurrent().User }
        $Acl = Get-Acl -LiteralPath $Tree.DeniedFolder
        $Rule = New-Object System.Security.AccessControl.FileSystemAccessRule($UserSid, [System.Security.AccessControl.FileSystemRights]::ListDirectory, [System.Security.AccessControl.AccessControlType]::Deny)
        $Acl.AddAccessRule($Rule)
        Set-Acl -LiteralPath $Tree.DeniedFolder -AclObject $Acl
        $Tree.Deny = "Deny ListDirectory dla $($UserSid.Value) na $($Tree.DeniedFolder)"
    }
    catch { $Tree.Error = $_.Exception.Message }
    return [pscustomobject]$Tree
}

function Get-C4AclInfo {
    # Wpisy ACL ścieżki jako dane (SID, prawa, typ, dziedziczenie) i flagi: czy SID Administratorów (S-1-5-32-544) ma JAWNY (AdministratorsFullControl) albo dziedziczony Allow z pełną kontrolą (0x1F01FF).
    [CmdletBinding()]
    param([Parameter(Mandatory)][string]$Path)
    $Info = [ordered]@{ Path = $Path; Exists = $false; Aces = @(); AdministratorsFullControl = $false; AdministratorsFullControlInherited = $false; Error = $null }
    try {
        if (-not (Test-Path -LiteralPath $Path)) { return [pscustomobject]$Info }
        $Info.Exists = $true
        $Acl = Get-Acl -LiteralPath $Path -ErrorAction Stop
        $Aces = @()
        foreach ($Rule in @($Acl.Access)) {
            if ($null -eq $Rule) { continue }
            $RuleSid = try { $Rule.IdentityReference.Translate([System.Security.Principal.SecurityIdentifier]).Value } catch { [string]$Rule.IdentityReference }
            $Aces += [pscustomobject]@{ Sid = $RuleSid; Rights = [string]$Rule.FileSystemRights; RightsValue = [int]$Rule.FileSystemRights; Type = [string]$Rule.AccessControlType; IsInherited = [bool]$Rule.IsInherited }
        }
        $Info.Aces = @($Aces)
        # Tylko JAWNY wpis (IsInherited=false) dowodzi skutku icacls /grant; dziedziczony FullControl Administratorów jest zapisany osobno.
        $AdminFull = @($Aces | Where-Object { $_.Sid -eq 'S-1-5-32-544' -and $_.Type -eq 'Allow' -and (($_.RightsValue -band 0x1F01FF) -eq 0x1F01FF) })
        $Info.AdministratorsFullControl = [bool]@($AdminFull | Where-Object { -not $_.IsInherited }).Count
        $Info.AdministratorsFullControlInherited = [bool]@($AdminFull | Where-Object { $_.IsInherited }).Count
    }
    catch { $Info.Error = $_.Exception.Message }
    return [pscustomobject]$Info
}

function Get-C4OwnerList {
    [CmdletBinding()]
    param([Parameter(Mandatory)][string[]]$Path)
    return @($Path | ForEach-Object { Get-C4OwnerInfo -Path $_ })
}

function Get-C4TreeSnapshot {
    # Migawka właścicieli korzenia i wszystkiego pod nim (bez SDDL). Błędy wyliczania (np. folder bez List Folder) jako dane.
    [CmdletBinding()]
    param([string]$Root = 'C:\DsoTest')
    $Items = @(Get-ChildItem -LiteralPath $Root -Recurse -Force -ErrorAction SilentlyContinue -ErrorVariable EnumerationFailure)
    $Paths = @($Root) + @($Items | ForEach-Object { $_.FullName })
    $Entries = @($Paths | ForEach-Object { Get-C4OwnerInfo -Path $_ -NoSddl })
    return [pscustomobject]@{ Root = $Root; Count = $Entries.Count; EnumerationErrors = @($EnumerationFailure | ForEach-Object { [string]$_ }); Entries = $Entries }
}

function Compare-C4Snapshot {
    # Ścieżki, których właściciel różni się między migawkami (albo które pojawiły się/zniknęły).
    [CmdletBinding()]
    param(
        [Parameter(Mandatory)]$Before,
        [Parameter(Mandatory)]$After
    )
    $Old = @{}
    foreach ($Entry in @($Before.Entries)) { $Old[[string]$Entry.Path] = [string]$Entry.OwnerSid }
    $Changed = @()
    $Seen = @{}
    foreach ($Entry in @($After.Entries)) {
        $Seen[[string]$Entry.Path] = $true
        if (-not $Old.ContainsKey([string]$Entry.Path)) { $Changed += [string]$Entry.Path }
        elseif ($Old[[string]$Entry.Path] -ne [string]$Entry.OwnerSid) { $Changed += [string]$Entry.Path }
    }
    foreach ($OldPath in $Old.Keys) { if (-not $Seen.ContainsKey($OldPath)) { $Changed += $OldPath } }
    return @($Changed | Sort-Object -Unique)
}

function Get-C4ElevationVerdict {
    # Uczciwy werdykt (4c): bez faktów nie ma rozstrzygnięcia. Token z pełnym poziomem integralności (PS Direct jako Administrator, bez UAC)
    # uniemożliwia wnioskowanie o podniesieniu uprawnień z samej zmiany właściciela.
    [CmdletBinding()]
    param(
        [Parameter(Mandatory)][bool]$TokenFull,
        [Parameter(Mandatory)][bool]$InvokeAttempted,
        [Parameter(Mandatory)][bool]$OwnerChanged
    )
    if (-not $InvokeAttempted) {
        return [pscustomobject]@{ Verdict = 'nie rozstrzygnięte'; Reason = 'InvokeVerb nie został wykonany (element lub verb nieznaleziony, albo błąd).' }
    }
    if ($TokenFull) {
        return [pscustomobject]@{ Verdict = 'nie rozstrzygnięte'; Reason = 'Proces sondy ma pełny token (poziom integralności High, PS Direct jako Administrator, bez UAC): zmiana właściciela nie dowodzi, że verb runas podniósł uprawnienia, a monitu UAC nie da się tu wywołać.' }
    }
    if ($OwnerChanged) { return [pscustomobject]@{ Verdict = 'podniesienie potwierdzone'; Reason = 'Token bez poziomu High, a właściciel zmieniony na Administratorów.' } }
    return [pscustomobject]@{ Verdict = 'brak podniesienia'; Reason = 'Token bez poziomu High, właściciel bez zmiany.' }
}

# --- Sondy ---

function Invoke-C4BitLockerPolicyProbe {
    # (1) Tylko odczyt.
    [CmdletBinding()]
    param()
    $Key = 'HKLM\SYSTEM\CurrentControlSet\Control\BitLocker'
    $Regs = @(@{ Path = $Key; Name = 'PreventDeviceEncryption' })
    Invoke-ExploreStep -Name 'bitlocker-klucz' -Command "(odczyt rejestru: $Key)" -Registry $Regs -RegistryKeys @($Key)
}

function Get-C4ConversionSample {
    # Jedna próbka stanu woluminu C: z WMI (GetConversionStatus + GetProtectionStatus). Błąd lub brak klasy = Ok=false z komunikatem.
    # Tylko odczyt (obie metody WMI są odczytowe). Surowy kształt zwróconych obiektów (nazwy, typy, wartości właściwości) w ConversionRaw/ProtectionRaw.
    [CmdletBinding()]
    param()
    $Sample = [ordered]@{
        Ok = $false; Error = $null; Found = $false; ReturnValue = $null; ConversionStatus = $null; EncryptionPercentage = $null
        ProtectionReturnValue = $null; ProtectionStatus = $null; ConversionRaw = @(); ProtectionRaw = @(); At = (Get-Date).ToString('o')
    }
    try {
        $Volume = @(Get-CimInstance -Namespace 'root\CIMV2\Security\MicrosoftVolumeEncryption' -ClassName Win32_EncryptableVolume -Filter "DriveLetter='C:'" -ErrorAction Stop) | Select-Object -First 1
        if ($null -eq $Volume) { $Sample.Error = 'Win32_EncryptableVolume: brak woluminu C:' }
        else {
            $Sample.Found = $true
            $Conversion = Invoke-CimMethod -InputObject $Volume -MethodName GetConversionStatus -ErrorAction Stop
            $Sample.ConversionRaw = @($Conversion.PSObject.Properties | ForEach-Object { [pscustomobject]@{ Name = $_.Name; Type = $_.TypeNameOfValue; Value = [string]$_.Value } })
            $Sample.ReturnValue = $Conversion.ReturnValue
            $Sample.ConversionStatus = $Conversion.ConversionStatus
            $Sample.EncryptionPercentage = $Conversion.EncryptionPercentage
            $Protection = Invoke-CimMethod -InputObject $Volume -MethodName GetProtectionStatus -ErrorAction Stop
            $Sample.ProtectionRaw = @($Protection.PSObject.Properties | ForEach-Object { [pscustomobject]@{ Name = $_.Name; Type = $_.TypeNameOfValue; Value = [string]$_.Value } })
            $Sample.ProtectionReturnValue = $Protection.ReturnValue
            $Sample.ProtectionStatus = $Protection.ProtectionStatus
            $Sample.Ok = $true
        }
    }
    catch { $Sample.Error = $_.Exception.Message }
    return [pscustomobject]$Sample
}

function Invoke-C4BitLockerReadProbe {
    # (2) Tylko odczyt, Home i Pro: klasa WMI (błąd/pusto/obiekt), GetConversionStatus, ProtectionStatus, Get-BitLockerVolume, moduł BitLocker.
    [CmdletBinding()]
    param()
    Invoke-ExploreStep -Name "bitlocker-modul" -Command '(Test-Path ...\Modules\BitLocker)' -After {
        $ModulePath = Join-Path $env:SystemRoot 'System32\WindowsPowerShell\v1.0\Modules\BitLocker'
        @{ ModulePath = $ModulePath; ModuleDirectoryExists = [bool](Test-Path -LiteralPath $ModulePath) }
    }
    Invoke-ExploreStep -Name "wmi-win32-encryptablevolume" -Command "Get-CimInstance -Namespace root\CIMV2\Security\MicrosoftVolumeEncryption -ClassName Win32_EncryptableVolume -Filter ""DriveLetter='C:'""" -After {
        @{ Volume = (Invoke-C4Read -Script {
                    Get-CimInstance -Namespace 'root\CIMV2\Security\MicrosoftVolumeEncryption' -ClassName Win32_EncryptableVolume -Filter "DriveLetter='C:'" -ErrorAction Stop | ForEach-Object {
                        $Props = [ordered]@{}
                        foreach ($Property in $_.CimInstanceProperties) { $Props[$Property.Name] = if ($null -ne $Property.Value) { [string]$Property.Value } else { $null } }
                        [pscustomobject]$Props
                    }
                }) }
    }
    Invoke-ExploreStep -Name "wmi-getconversionstatus-protectionstatus" -Command 'Invoke-CimMethod GetConversionStatus, GetProtectionStatus (Win32_EncryptableVolume C:)' -After {
        @{ Sample = (Get-C4ConversionSample) }
    }
    Invoke-ExploreStep -Name "get-bitlockervolume" -Command 'Get-BitLockerVolume -MountPoint C:' -After {
        @{ BitLockerVolume = (Invoke-C4Read -Script {
                    Get-BitLockerVolume -MountPoint 'C:' -ErrorAction Stop | ForEach-Object {
                        [pscustomobject]@{
                            MountPoint = [string]$_.MountPoint; VolumeStatus = (Get-C4EnumPair $_.VolumeStatus); ProtectionStatus = (Get-C4EnumPair $_.ProtectionStatus)
                            EncryptionPercentage = $_.EncryptionPercentage; EncryptionMethod = [string]$_.EncryptionMethod; KeyProtectorCount = @($_.KeyProtector).Count
                        }
                    }
                }) }
    }
}

function Invoke-C4ShellClassesProbe {
    # (3) Tylko odczyt: podklucze shell pięciu klas (kolizje, runas).
    [CmdletBinding()]
    param()
    $Keys = @(Get-C4MenuClasses | ForEach-Object { "HKLM\SOFTWARE\Classes\$_\shell" })
    Invoke-ExploreStep -Name 'klasy-shell-podklucze' -Command ('(odczyt rejestru: ' + ($Keys -join '; ') + ')') -RegistryKeys $Keys
}

function Invoke-C4GuestPolicyProbe {
    # (7) CARRY C3. Polityka "Enable insecure guest logons" (MS Policy CSP / ADMX): HKLM\SOFTWARE\Policies\Microsoft\Windows\LanmanWorkstation,
    # wartość AllowInsecureGuestAuth; MS nie podaje typu, więc zapis DWORD 1 i odczyt Kind z rejestru. Tylko odczyt przez Get-SmbClientConfiguration.
    [CmdletBinding()]
    param()
    $PolicyKey = 'HKLM\SOFTWARE\Policies\Microsoft\Windows\LanmanWorkstation'
    $PolicySubKey = 'SOFTWARE\Policies\Microsoft\Windows\LanmanWorkstation'
    $Regs = @(
        @{ Path = $PolicyKey; Name = 'AllowInsecureGuestAuth' }
        @{ Path = 'HKLM\SYSTEM\CurrentControlSet\Services\LanmanWorkstation\Parameters'; Name = 'AllowInsecureGuestAuth' }
    )
    $ReadGuest = {
        $Result = [ordered]@{ Ok = $false; Error = $null; EnableInsecureGuestLogons = $null }
        try {
            $Config = Get-SmbClientConfiguration -ErrorAction Stop
            $Result.EnableInsecureGuestLogons = $Config.EnableInsecureGuestLogons
            $Result.Ok = $true
        }
        catch { $Result.Error = $_.Exception.Message }
        [pscustomobject]$Result
    }
    Invoke-ExploreStep -Name 'polityka-przed' -Command 'Get-SmbClientConfiguration (EnableInsecureGuestLogons); rejestr polityki i Parameters' -Registry $Regs -RegistryKeys @($PolicyKey) -After {
        @{ Smb = (& $ReadGuest) }
    }
    Invoke-ExploreStep -Name 'polityka-zapis-dword-1' -Command "zapis HKLM\$PolicySubKey AllowInsecureGuestAuth = 1 (DWORD)" -Registry $Regs -RegistryKeys @($PolicyKey) -After {
        @{ Smb = (& $ReadGuest) }
    } -Action {
        Set-C4RegistryValue -SubKeyPath $PolicySubKey -Name 'AllowInsecureGuestAuth' -Value 1 -Kind 'DWord'
        [pscustomobject]@{ ExitCode = $null; Stdout = 'zapisano AllowInsecureGuestAuth = 1 (DWORD)'; Stderr = $null; Error = $null }
    }
    Invoke-ExploreStep -Name 'polityka-po-usunieciu-wartosci' -Command "usunięcie wartości HKLM\$PolicySubKey AllowInsecureGuestAuth" -Registry $Regs -RegistryKeys @($PolicyKey) -After {
        @{ Smb = (& $ReadGuest) }
    } -Action {
        Remove-C4RegistryValue -SubKeyPath $PolicySubKey -Name 'AllowInsecureGuestAuth'
        [pscustomobject]@{ ExitCode = $null; Stdout = 'usunięto wartość AllowInsecureGuestAuth'; Stderr = $null; Error = $null }
    }
}

function Invoke-C4TakeownProbe {
    # (5) takeown /a /r /d Y i icacls /setowner *S-1-5-32-544 /t /c /l na osobnych drzewach (C:\DsoTest\p5-*), właściciel i SDDL przed i po.
    [CmdletBinding()]
    param()
    $Seed = Get-C4SeedOwnerSid
    $Cases = @(
        @{ Id = 'takeown'; Root = 'C:\DsoTest\p5-takeown'; FilePath = 'takeown.exe'; Label = 'takeown /f <folder> /a /r /d Y' }
        @{ Id = 'icacls-setowner'; Root = 'C:\DsoTest\p5-icacls'; FilePath = 'icacls.exe'; Label = 'icacls <folder> /setowner *S-1-5-32-544 /t /c /l' }
    )
    foreach ($Case in $Cases) {
        $Hold = @{ Tree = $null; Before = @() }
        Invoke-ExploreStep -Name "przygotowanie-$($Case.Id)" -Command "drzewo $($Case.Root), właściciel wyjściowy $($Seed.Sid) ($($Seed.Source)), Deny ListDirectory na podfolderze" -Action {
            $Hold.Tree = New-C4TestTree -Root $Case.Root -SeedOwnerSid $Seed.Sid
            if ($null -ne $Hold.Tree.Seed) { $Hold.Tree.Seed }
        } -After {
            $Hold.Before = @(Get-C4OwnerList -Path @($Hold.Tree.Folder, $Hold.Tree.Child, $Hold.Tree.DeniedFolder, $Hold.Tree.Hidden))
            @{ Tree = $Hold.Tree; SeedOwner = $Seed; OwnersBefore = $Hold.Before }
        }
        $Arguments = if ($Case.Id -eq 'takeown') { @('/f', $Hold.Tree.Folder, '/a', '/r', '/d', 'Y') } else { @($Hold.Tree.Folder, '/setowner', '*S-1-5-32-544', '/t', '/c', '/l') }
        Invoke-ExploreStep -Name $Case.Id -Command ("$($Case.FilePath) " + ($Arguments -join ' ')) -Action {
            Invoke-ExploreNative -FilePath $Case.FilePath -Arguments $Arguments -TimeoutSeconds 180
        } -After {
            @{ OwnersBefore = $Hold.Before; OwnersAfter = @(Get-C4OwnerList -Path @($Hold.Tree.Folder, $Hold.Tree.Child, $Hold.Tree.DeniedFolder, $Hold.Tree.Hidden)); ExpectedOwnerSid = 'S-1-5-32-544'; SeedOwnerSid = $Seed.Sid }
        }
    }
}

function Invoke-C4InvokeVerbProbe {
    # (4c) InvokeVerb (Shell.Application) na pliku i folderze testowym, właściciel po. Struktury: Plain (kontrola mechanizmu InvokeVerb),
    # Subkey i ValuePath (verb nadrzędny i 'runas'). Wniosek o podniesieniu uprawnień tylko przez Get-C4ElevationVerdict.
    [CmdletBinding()]
    param()
    $Seed = Get-C4SeedOwnerSid
    $Token = Get-C4TokenInfo
    $Attempts = @(
        @{ Structure = 'Plain'; Verb = 'DSOptimizer.TakeOwnership' }
        @{ Structure = 'Subkey'; Verb = 'DSOptimizer.TakeOwnership' }
        @{ Structure = 'Subkey'; Verb = 'runas' }
        @{ Structure = 'ValuePath'; Verb = 'DSOptimizer.TakeOwnership' }
        @{ Structure = 'ValuePath'; Verb = 'runas' }
    )
    $Verdicts = New-Object System.Collections.Generic.List[object]
    foreach ($Attempt in $Attempts) {
        $AttemptId = "$($Attempt.Structure)-$($Attempt.Verb -replace '[^A-Za-z0-9]', '')"
        $Hold = @{ Tree = $null; Before = @(); Files = $null; Folders = $null }
        Invoke-ExploreStep -Name "invokeverb-$AttemptId" -Command "InvokeVerb('$($Attempt.Verb)') na pliku i folderze (struktura $($Attempt.Structure)) w C:\DsoTest\p4c-$AttemptId" -Action {
            foreach ($Cls in (Get-C4MenuClasses)) { Remove-C4MenuVariant -Class $Cls }
            $Hold.Tree = New-C4TestTree -Root "C:\DsoTest\p4c-$AttemptId" -SeedOwnerSid $Seed.Sid
            foreach ($Cls in @('*', 'Directory')) { $null = Set-C4MenuVariant -Class $Cls -Structure $Attempt.Structure }
            $Hold.Before = @(Get-C4OwnerList -Path @($Hold.Tree.File, $Hold.Tree.Folder, $Hold.Tree.Child, $Hold.Tree.Hidden))
            $Hold.Files = Invoke-C4InvokeVerb -ItemPath $Hold.Tree.File -VerbName $Attempt.Verb
            $Hold.Folders = Invoke-C4InvokeVerb -ItemPath $Hold.Tree.Folder -VerbName $Attempt.Verb
            Start-Sleep -Seconds 3
            $Hold.Folders.Native
        } -After {
            $OwnersNow = @(Get-C4OwnerList -Path @($Hold.Tree.File, $Hold.Tree.Folder, $Hold.Tree.Child, $Hold.Tree.Hidden))
            $FileChanged = ($OwnersNow[0].OwnerSid -ne $Hold.Before[0].OwnerSid)
            $FolderChanged = ($OwnersNow[1].OwnerSid -ne $Hold.Before[1].OwnerSid)
            $Attempted = ([bool]$Hold.Files.Result.Invoked) -or ([bool]$Hold.Folders.Result.Invoked)
            $Verdict = Get-C4ElevationVerdict -TokenFull ([bool]$Token.HasHighIntegritySid) -InvokeAttempted $Attempted -OwnerChanged ($FileChanged -or $FolderChanged)
            $Verdicts.Add([pscustomobject]@{ Structure = $Attempt.Structure; Verb = $Attempt.Verb; FileOwnerChanged = $FileChanged; FolderOwnerChanged = $FolderChanged; Verdict = $Verdict.Verdict; Reason = $Verdict.Reason })
            @{ Tree = $Hold.Tree; OwnersBefore = $Hold.Before; OwnersAfter = $OwnersNow; FileOwnerChanged = $FileChanged; FolderOwnerChanged = $FolderChanged
                InvokeFile = $Hold.Files; InvokeFolder = $Hold.Folders; Verdict = $Verdict }
        }
    }
    Invoke-ExploreStep -Name 'invokeverb-werdykt' -Command '(zestawienie 4c: token sondy i werdykty prób)' -After {
        @{ Token = $Token; Verdicts = $Verdicts.ToArray() }
    }
}

function Invoke-C4ListingStep {
    # Jeden wariant menu: czysta karta (usunięcie naszych kluczy ze wszystkich klas), rejestracja, wyliczenie verbów w świeżym procesie.
    [CmdletBinding()]
    param(
        [Parameter(Mandatory)]$Plan,
        [Parameter(Mandatory)][string[]]$Targets
    )
    $Hold = @{ Child = $null; Written = @() }
    $Trees = @($Plan.Classes | ForEach-Object { "HKLM\SOFTWARE\Classes\$_\shell\DSOptimizer.TakeOwnership" })
    Invoke-ExploreStep -Name "lista-$($Plan.Id)" -Command "wariant $($Plan.Id): struktura $($Plan.Structure), Extended=$($Plan.Extended), AppliesTo=$($Plan.AppliesTo), klasy $($Plan.Classes -join ',')" -RegistryTrees $Trees -Action {
        foreach ($Cls in (Get-C4MenuClasses)) { Remove-C4MenuVariant -Class $Cls }
        $Written = @()
        foreach ($Cls in $Plan.Classes) { $Written += @(Set-C4MenuVariant -Class $Cls -Structure $Plan.Structure -Extended:([bool]$Plan.Extended) -AppliesTo $Plan.AppliesTo) }
        $Hold.Written = $Written
        $Hold.Child = Invoke-C4VerbListing -Targets $Targets -Notify
        $Hold.Child.Native
    } -After {
        @{ Variant = $Plan; Writes = @($Hold.Written); Listing = $Hold.Child.Result; ListingReadError = $Hold.Child.ReadError }
    }
}

function Invoke-C4MenuProbe {
    # (4) Menu: (4a)/(4b) listy verbów dla wariantów, (4c) InvokeVerb. Na końcu sprzątanie naszych kluczy (VM i tak wraca do checkpointu).
    [CmdletBinding()]
    param()
    $Seed = Get-C4SeedOwnerSid
    $Hold = @{ Tree = $null }
    Invoke-ExploreStep -Name 'przygotowanie-menu' -Command "token sondy; drzewo C:\DsoTest (właściciel wyjściowy $($Seed.Sid) - $($Seed.Source))" -Action {
        $Hold.Tree = New-C4TestTree -Root 'C:\DsoTest' -SeedOwnerSid $Seed.Sid
        if ($null -ne $Hold.Tree.Seed) { $Hold.Tree.Seed }
    } -After {
        @{ Token = (Get-C4TokenInfo); Tree = $Hold.Tree; SeedOwner = $Seed; Targets = @(Get-C4VerbTargets) }
    }
    $Targets = @(Get-C4VerbTargets)
    foreach ($Plan in @(Get-C4MenuVariantPlan)) { Invoke-C4ListingStep -Plan $Plan -Targets $Targets }
    $AllTrees = @(Get-C4MenuClasses | ForEach-Object { "HKLM\SOFTWARE\Classes\$_\shell\DSOptimizer.TakeOwnership" })
    Invoke-ExploreStep -Name 'sprzatanie-menu-po-listach' -Command 'usunięcie kluczy DSOptimizer.TakeOwnership ze wszystkich klas' -RegistryTrees $AllTrees -Action {
        foreach ($Cls in (Get-C4MenuClasses)) { Remove-C4MenuVariant -Class $Cls }
        [pscustomobject]@{ ExitCode = $null; Stdout = 'usunięto'; Stderr = $null; Error = $null }
    }
    Invoke-C4InvokeVerbProbe
    Invoke-ExploreStep -Name 'sprzatanie-menu-koncowe' -Command 'usunięcie kluczy DSOptimizer.TakeOwnership ze wszystkich klas' -RegistryTrees $AllTrees -Action {
        foreach ($Cls in (Get-C4MenuClasses)) { Remove-C4MenuVariant -Class $Cls }
        [pscustomobject]@{ ExitCode = $null; Stdout = 'usunięto'; Stderr = $null; Error = $null }
    }
}

function Get-C4CommandCases {
    # Przypadki sondy 6: pliki i foldery o nazwach z znakami specjalnymi (cel zawsze pod $Root). Foldery mają dziecko dziecko.txt.
    [CmdletBinding()]
    param([Parameter(Mandatory)][string]$Root)
    $BaseNames = [ordered]@{
        'spacje'     = 'spacja w nazwie'
        'polskie'    = 'zażółć gęślą jaźń'
        'procent'    = 'procent %PATH% 50%'
        'ampersand'  = 'amper & koniec'
        'daszek'     = 'daszek ^ test'
        'wykrzyknik' = 'wykrzyknik! test'
        'mieszane'   = 'mix %TEMP% & ^ ! zażółć'
    }
    $Cases = @()
    foreach ($Key in $BaseNames.Keys) {
        $FilePath = Join-Path $Root ($BaseNames[$Key] + '.txt')
        $FolderPath = Join-Path $Root ($BaseNames[$Key] + ' (folder)')
        $Cases += [pscustomobject]@{ Id = "plik-$Key"; Kind = 'File'; Path = $FilePath; IntendedPaths = @($FilePath) }
        $Cases += [pscustomobject]@{ Id = "folder-$Key"; Kind = 'Folder'; Path = $FolderPath; IntendedPaths = @($FolderPath, (Join-Path $FolderPath 'dziecko.txt')) }
    }
    return $Cases
}

function Invoke-C4CommandLineProbe {
    # (6) Linia komendy z menu przez cmd.exe /c, %1 podstawione dosłownie (jak powłoka). Sprawdzenie: właściciel celu i wszystkie zmiany w C:\DsoTest.
    [CmdletBinding()]
    param()
    $Seed = Get-C4SeedOwnerSid
    $Root = 'C:\DsoTest\p6'
    $Cases = @(Get-C4CommandCases -Root $Root)
    $Roll = @{ Snapshot = $null }
    Invoke-ExploreStep -Name 'przygotowanie-cmdline' -Command "pliki i foldery testowe w $Root, właściciel wyjściowy $($Seed.Sid) ($($Seed.Source))" -Action {
        $null = New-Item -ItemType Directory -Path $Root -Force
        foreach ($Item in $Cases) {
            if ($Item.Kind -eq 'File') { Set-Content -LiteralPath $Item.Path -Value 'dso test' -Encoding UTF8 }
            else {
                $null = New-Item -ItemType Directory -Path $Item.Path -Force
                Set-Content -LiteralPath (Join-Path $Item.Path 'dziecko.txt') -Value 'dso test' -Encoding UTF8
            }
        }
        Invoke-ExploreNative -FilePath 'icacls.exe' -Arguments @($Root, '/setowner', "*$($Seed.Sid)", '/t', '/c', '/l', '/q') -TimeoutSeconds 120
    } -After {
        $Roll.Snapshot = Get-C4TreeSnapshot -Root 'C:\DsoTest'
        @{ SeedOwner = $Seed; Cases = $Cases; SnapshotCount = $Roll.Snapshot.Count; SnapshotEnumerationErrors = @($Roll.Snapshot.EnumerationErrors); Snapshot = $Roll.Snapshot }
    }
    foreach ($Case in $Cases) {
        $Template = Get-C4MenuCommandLine -Kind $Case.Kind
        $RawArguments = $Template.Arguments.Replace('%1', $Case.Path)
        Invoke-ExploreStep -Name "cmdline-$($Case.Id)" -Command "$($Template.Exe) $RawArguments" -Action {
            Invoke-ExploreNative -FilePath $Template.Exe -RawArguments $RawArguments -TimeoutSeconds 120
        } -After {
            $Now = Get-C4TreeSnapshot -Root 'C:\DsoTest'
            $ChangedPaths = @(Compare-C4Snapshot -Before $Roll.Snapshot -After $Now)
            $Roll.Snapshot = $Now
            $Stray = @($ChangedPaths | Where-Object { $Case.IntendedPaths -notcontains $_ })
            $TargetEntry = @($Now.Entries | Where-Object { $_.Path -eq $Case.Path })[0]
            @{
                Case = $Case; RawArguments = $RawArguments; ChangedPaths = $ChangedPaths
                IntendedChanged = ($ChangedPaths -contains $Case.Path); StrayChangedPaths = $Stray; OnlyIntendedChanged = ($Stray.Count -eq 0 -and ($ChangedPaths -contains $Case.Path))
                TargetOwnerSidAfter = if ($null -ne $TargetEntry) { $TargetEntry.OwnerSid } else { $null }; SnapshotEnumerationErrors = @($Now.EnumerationErrors)
                TargetAclAfter = (Get-C4AclInfo -Path $Case.Path)
                IntendedAclsAfter = @($Case.IntendedPaths | ForEach-Object { Get-C4AclInfo -Path $_ })
            }
        }
    }
}

function Invoke-C4ManualUacCollection {
    # Ręczny test UAC (G1). Prepare: wariant menu pod * i Directory (ten sam zapis co sondy, Assert-C4WritePath), drzewo C:\DsoTest\manual-uac z właścicielem
    # dso (SID, ACL zapisane). Finish: odczyt właściciela i ACL tych samych elementów po ręcznym użyciu verbu. Zwraca JSON jak inne punkty wejścia.
    [CmdletBinding()]
    param(
        [Parameter(Mandatory)][ValidateSet('Prepare', 'Finish')][string]$Mode,
        [ValidateSet('Plain', 'Subkey', 'ValuePath')][string]$Structure = 'Subkey',
        [switch]$Extended
    )
    $Root = 'C:\DsoTest\manual-uac'
    $Paths = Get-C4TreePaths -Root $Root
    $ItemPaths = @($Paths.Root, $Paths.File, $Paths.Folder, $Paths.Child, $Paths.DeniedFolder, $Paths.Hidden)
    $Trees = @('*', 'Directory' | ForEach-Object { "HKLM\SOFTWARE\Classes\$_\shell\DSOptimizer.TakeOwnership" })
    $Hold = @{ Tree = $null; Writes = @() }
    $Probes = [ordered]@{}
    if ($Mode -eq 'Prepare') {
        $Probes.manualUac = {
            Invoke-ExploreStep -Name 'przygotowanie-reczny-uac' -Command "wariant $Structure (Extended=$([bool]$Extended)) pod * i Directory; drzewo $Root, właściciel dso, Deny List Folder dla dso" -RegistryTrees $Trees -Action {
                $DsoSid = [string](Get-LocalUser -Name 'dso' -ErrorAction Stop).SID.Value
                foreach ($Cls in (Get-C4MenuClasses)) { Remove-C4MenuVariant -Class $Cls }
                $Hold.Tree = New-C4TestTree -Root $Root -SeedOwnerSid $DsoSid -DenyUserSid $DsoSid
                $Written = @()
                foreach ($Cls in @('*', 'Directory')) { $Written += @(Set-C4MenuVariant -Class $Cls -Structure $Structure -Extended:$Extended) }
                $Hold.Writes = $Written
                if ($null -ne $Hold.Tree.Seed) { $Hold.Tree.Seed }
            } -After {
                @{ Structure = $Structure; Extended = [bool]$Extended; Writes = @($Hold.Writes); Tree = $Hold.Tree; Owners = @(Get-C4OwnerList -Path $ItemPaths); Acls = @($ItemPaths | ForEach-Object { Get-C4AclInfo -Path $_ }) }
            }
        }
    }
    else {
        $Probes.manualUac = {
            Invoke-ExploreStep -Name 'stan-po-tescie-recznym' -Command "odczyt właściciela i ACL elementów w $Root oraz kluczy menu" -RegistryTrees $Trees -After {
                @{ Owners = @(Get-C4OwnerList -Path $ItemPaths); Acls = @($ItemPaths | ForEach-Object { Get-C4AclInfo -Path $_ }) }
            }
        }
    }
    return (Invoke-ExploreCollection -Probe $Probes -Extra @{ token = (Get-C4TokenInfo); mode = $Mode })
}

function Invoke-C4Collection {
    # Punkt wejścia po stronie VM: zwraca JEDEN string JSON (meta + token + wybrane sondy; bez -Probe wszystkie siedem).
    # Kolejność: odczyty, polityka, drzewa takeown, menu, linia komendy.
    [CmdletBinding()]
    param([ValidateSet('bitlockerPolicy', 'bitlockerState', 'shellClasses', 'guestPolicy', 'takeown', 'menu', 'cmdline')][string[]]$Probe)
    $Probes = [ordered]@{
        bitlockerPolicy = { Invoke-C4BitLockerPolicyProbe }
        bitlockerState  = { Invoke-C4BitLockerReadProbe }
        shellClasses    = { Invoke-C4ShellClassesProbe }
        guestPolicy     = { Invoke-C4GuestPolicyProbe }
        takeown         = { Invoke-C4TakeownProbe }
        menu            = { Invoke-C4MenuProbe }
        cmdline         = { Invoke-C4CommandLineProbe }
    }
    if ($Probe) {
        # Wybór sond (kolejność zawsze ta sama co w pełnym przebiegu).
        $Selected = [ordered]@{}
        foreach ($Key in $Probes.Keys) { if ($Probe -contains $Key) { $Selected[$Key] = $Probes[$Key] } }
        $Probes = $Selected
    }
    return (Invoke-ExploreCollection -Probe $Probes -Extra @{ token = (Get-C4TokenInfo) })
}

# ---------------------------------------------------------------------------------------------------------------------
# Strona hosta (nic z tego nie jest wysyłane do VM).
# ---------------------------------------------------------------------------------------------------------------------

function Get-C4RemoteFunctionNames {
    return @(
        'Get-C4EnumPair', 'Invoke-C4Read', 'Get-C4TokenInfo', 'Get-C4OwnerInfo', 'Assert-C4WritePath', 'Set-C4RegistryValue', 'Remove-C4RegistryValue',
        'Remove-C4RegistryTree', 'Get-C4MenuCommandLine', 'Set-C4MenuVariant', 'Remove-C4MenuVariant', 'Get-C4MenuClasses', 'Get-C4MenuVariantPlan',
        'Get-C4VerbTargets', 'Get-C4ListVerbsScript', 'Get-C4InvokeVerbScript', 'Invoke-C4ChildScript', 'Invoke-C4VerbListing', 'Invoke-C4InvokeVerb',
        'Get-C4SeedOwnerSid', 'Get-C4TreePaths', 'Get-C4AclInfo', 'New-C4TestTree', 'Get-C4OwnerList', 'Get-C4TreeSnapshot', 'Compare-C4Snapshot', 'Get-C4ElevationVerdict',
        'Invoke-C4BitLockerPolicyProbe', 'Get-C4ConversionSample', 'Invoke-C4BitLockerReadProbe', 'Invoke-C4ShellClassesProbe',
        'Invoke-C4GuestPolicyProbe', 'Invoke-C4TakeownProbe', 'Invoke-C4InvokeVerbProbe',
        'Invoke-C4ListingStep', 'Invoke-C4MenuProbe', 'Get-C4CommandCases', 'Invoke-C4CommandLineProbe', 'Invoke-C4ManualUacCollection', 'Invoke-C4Collection'
    )
}

function Get-C4HostOnlyFunctionNames {
    return @('Get-C4RemoteFunctionNames', 'Get-C4HostOnlyFunctionNames', 'Get-C4RemoteScript', 'New-C4EntryPoint', 'Assert-C4ModeArguments', 'Invoke-C4Exploration', 'Invoke-C4ManualUacTest', 'Assert-C4PrepareResult', 'Get-C4ManualUacInstructions')
}

function Get-C4RemoteScript {
    # Tekst definicji funkcji zdalnych: część wspólna z modułu + funkcje C4 (FunctionInfo pobrane tu, w zasięgu skryptu).
    [CmdletBinding()]
    [OutputType([string])]
    param()
    $Names = @(Get-ExploreCommonRemoteFunctionNames) + @(Get-C4RemoteFunctionNames)
    return (ConvertTo-ExploreRemoteScript -Command @($Names | ForEach-Object { Get-Command -Name $_ -CommandType Function -ErrorAction Stop }))
}

function New-C4EntryPoint {
    # Tekst funkcji Invoke-C4EntryPoint dopisywanej do skryptu zdalnego (Invoke-ExploreVmRun woła punkt wejścia bez argumentów).
    # Body musi mieć dokładnie jeden z dwóch kształtów (wybór sond albo ręczny test UAC); wszystko inne (średniki, pipe, $(...), nawiasy) jest odrzucane.
    [CmdletBinding()]
    [OutputType([string])]
    param([Parameter(Mandatory)][ValidatePattern('^(Invoke-C4Collection( -Probe @\(''[A-Za-z]+''(,''[A-Za-z]+'')*\))?|Invoke-C4ManualUacCollection -Mode (Prepare|Finish) -Structure (Plain|Subkey|ValuePath)( -Extended)?)\z')][string]$Body)
    return "function Invoke-C4EntryPoint {`n$Body`n}"
}

function Assert-C4ModeArguments {
    # Tryby ręcznego testu UAC: wzajemnie wyłączne, dokładnie jedna VM podana jawnie, -Probe nie łączy się z trybami ręcznymi.
    [CmdletBinding()]
    param(
        [bool]$Prepare,
        [bool]$Finish,
        [string[]]$VmName,
        [bool]$VmNameExplicit,
        [string[]]$Probe
    )
    if (-not $Prepare -and -not $Finish) { return }
    if ($Prepare -and $Finish) { throw '-PrepareManualUacTest i -FinishManualUacTest wykluczają się.' }
    if (-not $VmNameExplicit -or @($VmName).Count -ne 1) { throw 'Tryby ręcznego testu UAC wymagają jawnie podanej JEDNEJ VM (-VmName DSO-26H2-Home albo DSO-26H2-Pro).' }
    if ($Probe) { throw '-Probe nie łączy się z trybami ręcznego testu UAC.' }
}

function Invoke-C4Exploration {
    # Wszystkie wybrane VM po kolei. Błąd uwierzytelnienia = stop całości; inny błąd = zapis i dalej (logika we wspólnym module).
    [CmdletBinding()]
    param(
        [Parameter(Mandatory)][ValidateSet('DSO-26H2-Home', 'DSO-26H2-Pro')][string[]]$VmName,
        [Parameter(Mandatory)][string]$OutDirectory,
        [string]$CheckpointName = 'czysta-26H2',
        [int]$ReadyTimeoutSeconds = 1200,
        [ValidateSet('bitlockerPolicy', 'bitlockerState', 'shellClasses', 'guestPolicy', 'takeown', 'menu', 'cmdline')][string[]]$Probe
    )
    $RemoteScript = Get-C4RemoteScript
    $Entry = 'Invoke-C4Collection'
    if ($Probe) {
        $RemoteScript = $RemoteScript + "`n`n" + (New-C4EntryPoint -Body ('Invoke-C4Collection -Probe @(' + (($Probe | ForEach-Object { "'$_'" }) -join ',') + ')'))
        $Entry = 'Invoke-C4EntryPoint'
    }
    return (Invoke-ExploreAll -VmName $VmName -OutDirectory $OutDirectory -FileName 'c4-exploration.json' -RemoteScript $RemoteScript `
            -EntryCommand $Entry -CheckpointName $CheckpointName -ReadyTimeoutSeconds $ReadyTimeoutSeconds)
}

function Assert-C4PrepareResult {
    # Walidacja wyniku Prepare (ręczny test UAC): niekompletna konfiguracja VM = wyjątek, po którym Invoke-ExploreVmRun robi revert
    # (VM nie zostaje w połowie skonfigurowana). Sprawdza krok, drzewo (Error, kod icacls /setowner) i zapis kluczy menu pod OBIEMA klasami.
    [CmdletBinding()]
    param([Parameter(Mandatory)]$Document)
    $Steps = @()
    if ($null -ne $Document.PSObject.Properties['probes'] -and $null -ne $Document.probes.PSObject.Properties['manualUac']) { $Steps = @($Document.probes.manualUac) }
    $Step = @($Steps | Where-Object { $_.Name -eq 'przygotowanie-reczny-uac' })[0]
    if ($null -eq $Step) {
        $Reason = @($Steps | ForEach-Object { $_.Error } | Where-Object { $_ })[0]
        throw "Prepare: brak kroku 'przygotowanie-reczny-uac' w wyniku (sonda nie zadziałała: $Reason)."
    }
    $Problems = New-Object System.Collections.Generic.List[string]
    if ($Step.Error) { $Problems.Add("błąd kroku: $($Step.Error)") }
    $Extra = $Step.Extra
    if ($null -eq $Extra) { $Problems.Add('brak danych kroku (Extra)') }
    else {
        $Tree = $Extra.Tree
        if ($null -eq $Tree) { $Problems.Add('brak drzewa testowego') }
        else {
            if ($Tree.Error) { $Problems.Add("drzewo: $($Tree.Error)") }
            if ($null -eq $Tree.Seed) { $Problems.Add('brak wyniku icacls /setowner (Seed)') }
            elseif ($Tree.Seed.ExitCode -ne 0 -or $Tree.Seed.Error) { $Problems.Add("icacls /setowner: kod $($Tree.Seed.ExitCode) $($Tree.Seed.Error)") }
            if (-not $Tree.Deny) { $Problems.Add('brak Deny ListDirectory na podfolderze') }
        }
        $Writes = @($Extra.Writes)
        foreach ($Class in '*', 'Directory') {
            $Root = "SOFTWARE\Classes\$Class\shell\DSOptimizer.TakeOwnership"
            if (@($Writes | Where-Object { $_.SubKeyPath -eq $Root -and $_.Name -eq 'MUIVerb' }).Count -ne 1) { $Problems.Add("brak zapisu MUIVerb pod klasą $Class") }
            if (@($Writes | Where-Object { $_.SubKeyPath -like "$Root*\command" -and $_.Name -eq '' }).Count -lt 1) { $Problems.Add("brak zapisu command pod klasą $Class") }
        }
    }
    if ($Problems.Count -gt 0) { throw "Prepare: konfiguracja VM niekompletna, przywracam checkpoint: $($Problems -join '; ')" }
}

function Invoke-C4ManualUacTest {
    # Prepare: revert -> start -> zapis wariantu i drzewa -> walidacja (niekompletne = wyjątek i revert) -> VM zostaje uruchomiona.
    # Finish: odczyt (bez wymagania sesji dso) -> JSON zapisany PRZED revertem -> ZAWSZE zatrzymanie i revert.
    [CmdletBinding()]
    param(
        [Parameter(Mandatory)][ValidateSet('Prepare', 'Finish')][string]$Mode,
        [Parameter(Mandatory)][ValidateSet('DSO-26H2-Home', 'DSO-26H2-Pro')][string]$VmName,
        [Parameter(Mandatory)][string]$OutDirectory,
        [ValidateSet('Plain', 'Subkey', 'ValuePath')][string]$Variant = 'Subkey',
        [switch]$WithExtended,
        [string]$CheckpointName = 'czysta-26H2',
        [int]$ReadyTimeoutSeconds = 1200
    )
    # Wszystko, co może rzucić, jest zbudowane PRZED startem jakiejkolwiek pracy na VM.
    $Body = "Invoke-C4ManualUacCollection -Mode $Mode -Structure $Variant" + $(if ($WithExtended) { ' -Extended' } else { '' })
    $RemoteScript = (Get-C4RemoteScript) + "`n`n" + (New-C4EntryPoint -Body $Body)
    $Arguments = @{
        VmName = $VmName; OutDirectory = $OutDirectory; FileName = "c4-manual-uac-$($Mode.ToLowerInvariant()).json"; RemoteScript = $RemoteScript
        EntryCommand = 'Invoke-C4EntryPoint'; CheckpointName = $CheckpointName; ReadyTimeoutSeconds = $ReadyTimeoutSeconds; Mode = $Mode
    }
    if ($Mode -eq 'Prepare') { $Arguments.Validate = { param($Document) Assert-C4PrepareResult -Document $Document } }
    return (Invoke-ExploreVmRun @Arguments)
}

function Get-C4ManualUacInstructions {
    # Instrukcja ręczna po -PrepareManualUacTest (VM działa). OutDirectory ma być ścieżką bezwzględną (wchodzi w cudzysłów).
    [CmdletBinding()]
    [OutputType([string[]])]
    param(
        [Parameter(Mandatory)][string]$VmName,
        [Parameter(Mandatory)][string]$Variant,
        [bool]$WithExtended,
        [Parameter(Mandatory)][string]$OutDirectory
    )
    $Click = if ($WithExtended) { "Kliknij prawym przyciskiem Z KLAWISZEM SHIFT (pozycja ma 'Extended', bez Shift jej nie ma)" } else { 'Kliknij prawym przyciskiem' }
    $Menu = "Na Windows 11 zwykłe menu jest nowe, a pozycja jest w klasycznym: wybierz 'Pokaż więcej opcji'. Z Shift Windows 11 zwykle otwiera klasyczne menu od razu (wtedy ten krok pomiń)"
    $Entry = if ($Variant -eq 'Plain') { "pozycję 'DSO: Przejmij na własność'" } else { "pozycję 'DSO: Przejmij na własność', a w podmenu 'DSO: Przejmij (administrator)'" }
    return @(
        "VM '$VmName' działa z wariantem menu '$Variant' (Extended: $WithExtended). NIE zatrzymuj jej ani nie przywracaj ręcznie.",
        "1. Otwórz połączenie: vmconnect localhost $VmName. Użytkownik 'dso' powinien być zalogowany automatycznie (autologon po starcie); jeśli widzisz ekran logowania, zaloguj się jako 'dso' (hasło ustawione przy tworzeniu VM przez New-TestVm.ps1). To zwykła sesja z UAC.",
        '2. W Eksploratorze wejdź do C:\DsoTest\manual-uac.',
        "3. $Click na pliku 'plik testowy.txt'. $Menu. Wybierz $Entry.",
        '4. Zanotuj: czy pojawił się monit UAC (zgoda albo poświadczenia), co było w oknie, i potwierdź go. Zanotuj też, jeśli pozycji nie było w menu.',
        "5. To samo zrób dla folderu 'folder testowy' (rekurencja, podfolder 'podfolder bez list').",
        "6. Gdy skończysz, na hoście: .\tests\vm\explore\Invoke-C4Exploration.ps1 -FinishManualUacTest -VmName $VmName -OutDirectory `"$OutDirectory`"",
        '   Ten krok odczyta właściciela i ACL, zapisze JSON, zatrzyma VM i przywróci czysty checkpoint. Nie wymaga sesji użytkownika dso (możesz się wylogować), ale VM musi działać.'
    )
}

# Przy dot-sourcingu (testy) tylko definicje; uruchomienie przebiegu tylko przy zwykłym wywołaniu skryptu.
if ($MyInvocation.InvocationName -ne '.') {
    $ExitCode = 1
    try {
        Set-StrictMode -Version Latest
        $ErrorActionPreference = 'Stop'
        $VmRoot = Split-Path -Parent $PSScriptRoot
        Import-Module (Join-Path $VmRoot 'modules\VmCommon.psm1') -Force
        Import-Module (Join-Path $VmRoot 'modules\VmRun.psm1') -Force
        Import-Module (Join-Path $VmRoot 'modules\VmHost.psm1') -Force
        # ExploreCommon (zaimportowany wyżej, przed tymi modułami) trzyma referencję do starej instancji VmHost; ponowny import po nich wiąże go z bieżącą.
        Import-Module (Join-Path $PSScriptRoot 'ExploreCommon.psm1') -Force
        $Principal = New-Object System.Security.Principal.WindowsPrincipal([System.Security.Principal.WindowsIdentity]::GetCurrent())
        if (-not $Principal.IsInRole([System.Security.Principal.WindowsBuiltInRole]::Administrator)) {
            throw 'Uruchom skrypt w sesji PowerShell jako administrator (Hyper-V i PowerShell Direct tego wymagają).'
        }
        if (-not $OutDirectory) { $OutDirectory = Join-Path $VmRoot ('out\c4-' + (Get-Date -Format 'yyyyMMdd-HHmmss')) }
        # Walidacja na starcie, przed jakąkolwiek pracą na VM; katalog tworzy dopiero Save-ExploreResult (bez New-Item na hoście).
        $OutDirectory = Resolve-ExploreOutDirectory -Path $OutDirectory
        Assert-C4ModeArguments -Prepare ([bool]$PrepareManualUacTest) -Finish ([bool]$FinishManualUacTest) -VmName $VmName -VmNameExplicit ($PSBoundParameters.ContainsKey('VmName')) -Probe $Probe
        Write-Host "Wyniki: $OutDirectory"
        if ($PrepareManualUacTest -or $FinishManualUacTest) {
            $Mode = if ($PrepareManualUacTest) { 'Prepare' } else { 'Finish' }
            $SavedPath = Invoke-C4ManualUacTest -Mode $Mode -VmName $VmName[0] -OutDirectory $OutDirectory -Variant $Variant -WithExtended:$WithExtended -CheckpointName $CheckpointName -ReadyTimeoutSeconds $ReadyTimeoutSeconds
            Write-Host "Zapisano: $SavedPath"
            if ($Mode -eq 'Prepare') { foreach ($Line in (Get-C4ManualUacInstructions -VmName $VmName[0] -Variant $Variant -WithExtended ([bool]$WithExtended) -OutDirectory $OutDirectory)) { Write-Host $Line } }
            else { Write-Host "VM '$($VmName[0])' zatrzymana i przywrócona do '$CheckpointName'." }
            exit 0
        }
        $Outcome = Invoke-C4Exploration -VmName $VmName -OutDirectory $OutDirectory -CheckpointName $CheckpointName -ReadyTimeoutSeconds $ReadyTimeoutSeconds -Probe $Probe
        foreach ($Path in $Outcome.Paths) { Write-Host "Zapisano: $Path" }
        foreach ($Message in $Outcome.Errors) { Write-Host "BŁĄD $Message" -ForegroundColor Red }
        $ExitCode = if (@($Outcome.Errors).Count -eq 0 -and @($Outcome.Paths).Count -eq @($VmName).Count) { 0 } else { 1 }
    }
    catch {
        Write-Host "BŁĄD: $($_.Exception.Message)" -ForegroundColor Red
        Write-Host $_.ScriptStackTrace -ForegroundColor DarkGray
        $ExitCode = 1
    }
    exit $ExitCode
}
