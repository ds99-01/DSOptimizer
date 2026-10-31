<#
Instrukcja uruchomienia:
    Sesja PowerShell uruchomiona jako administrator, na hoście z Hyper-V, z modułami testów VM (tests\vm\modules) i tests\vm\explore\ExploreCommon.psm1.
    VM utworzone wcześniej przez New-TestVm.ps1 (checkpoint 'czysta-26H2', poświadczenia w %LOCALAPPDATA%\DSOptimizer-vm, autologon 'dso').
    Obie VM (Home, potem Pro), bez sieci:
        .\tests\vm\explore\Invoke-E4A3Exploration.ps1 -OutDirectory C:\ścieżka\poza\repo\exploration
    Jedna VM:
        .\tests\vm\explore\Invoke-E4A3Exploration.ps1 -VmName DSO-26H2-Pro -OutDirectory C:\...\exploration
    -ShowPlan: wypisuje rozstrzygnięte argumenty i plan (JSON) i kończy, nic nie uruchamia.
    -OutDirectory jest wymagany: ścieżka bezwzględna poza repo; w repo tylko katalog ignorowany przez git.
    Czas: ok. 5-8 minut na VM.
Opis działania:
    Pomiar partii 3 Etapu 4a (strona „Przygotowanie”): odczyty CIM strony (grafika, urządzenia z problemem, aktywacja, produkt, BIOS,
    procesor) z czasami zapytań oraz otwieranie Ustawień (ms-settings:) z podniesionego procesu konta dso. Wszystko dzieje się WYŁĄCZNIE
    w VM DSO-26H2-Home/Pro; na hoście skrypt tylko zapisuje wyniki.
    Przebieg jednej VM: revert do 'czysta-26H2' -> start -> gotowość (zalogowany dso) -> fazy -> kopia zrzutów -> ZAWSZE (finally) revert
    i sprawdzenie stanu końcowego (VM Off, karta bez przełącznika) przed zapisem JSON. Błąd sprzątania trafia do wyniku.
    Konteksty w VM:
      K1 - PowerShell Direct jako wbudowany Administrator: zapytania CIM z planu (każde Repeat razy, czasy i mediana), wyłączenie
           nieistotnego urządzenia (kod 22) i próby wymuszenia „Podstawowej karty graficznej Microsoft” (wyłączenie karty Hyper-V,
           pnputil /remove-device i ponowne wykrycie), odczyt nazw z display.inf / displayoverride.inf i ich tłumaczeń pl-PL.
      K2 - zadanie Harmonogramu (principal dso / Interactive / Highest), czyli proces jak DSOptimizer po UAC: te same zapytania CIM
           z czasami oraz start %SystemRoot%\explorer.exe "ms-settings:..." przez CreateProcess (UseShellExecute=false), jak ShellUrlOpener:
           czas do procesu SystemSettings.exe i okna Ustawień, token SystemSettings.exe (podniesienie, poziom integralności), właściciel,
           nazwy elementów okna (UI Automation) i zrzut pulpitu dso.
    Fazy: inventory (K1 + K2: zapytania) -> settings (K2: oba adresy po kolei, między nimi zamknięcie Ustawień) -> devices (K1: kod 22,
    karta graficzna, INF); wszystko cofa revert. Ostatni bezpiecznik po stronie VM: Assert-E4A3InsideVm (nazwa komputera DSO-26H2-HOME/PRO
    i brak DSO_VM_TESTS_MOCK_ONLY) na początku każdej funkcji ze skutkiem. Pierwszy błąd uwierzytelnienia przerywa cały skrypt (blokada
    konta). Skrypt dotyka wyłącznie VM DSO-26H2-Home i DSO-26H2-Pro (ValidateSet), nigdy obcej VM; na hoście niczego nie zmienia.
    Wynik: <OutDirectory>\<VmName>\e4a3.json (UTF-8), zrzuty K2 <OutDirectory>\<VmName>\shots\*.png.
    Kod wyjścia: 0 gdy wszystkie wybrane VM zebrane bez błędu przebiegu, 1 w przeciwnym razie.
#>
[CmdletBinding()]
param(
    [ValidateSet('DSO-26H2-Home', 'DSO-26H2-Pro')][string[]]$VmName = @('DSO-26H2-Home', 'DSO-26H2-Pro'),
    [string]$OutDirectory,
    [string]$CheckpointName = 'czysta-26H2',
    [ValidateRange(60, 7200)][int]$ReadyTimeoutSeconds = 1200,
    [switch]$ShowPlan
)

Import-Module (Join-Path $PSScriptRoot 'ExploreCommon.psm1') -Force

# ---------------------------------------------------------------------------------------------------------------------
# Plan (dane z planu Tasku 2, dosłownie).
# Queries: zapytania strony „Przygotowanie” (klasa, filtr WQL, właściwości); Repeat: liczba powtórzeń każdego zapytania (mediana).
# SettingsUris: adresy otwierane przez explorer.exe; Markers: tekst, który musi być wśród nazw elementów okna Ustawień.
# DisableTargets: nieistotne urządzenia, pierwsze dające się wyłączyć jest wyłączane na chwilę (kod 22; Hyper-V Generation Counter
# zwraca „Not supported”). TimeoutThresholdMs: mediana powyżej = limit 20 s, inaczej 10 s.
# ---------------------------------------------------------------------------------------------------------------------

function Get-E4A3Plan {
    [CmdletBinding()]
    param()
    return [ordered]@{
        Queries            = @(
            [ordered]@{ Name = 'VideoController'; ClassName = 'Win32_VideoController'; Filter = ''; Properties = @('Name', 'PNPDeviceID', 'DriverVersion', 'DriverDate', 'InfFilename', 'AdapterCompatibility') }
            [ordered]@{ Name = 'SignedDriverDisplay'; ClassName = 'Win32_PnPSignedDriver'; Filter = "DeviceClass='DISPLAY'"; Properties = @('DeviceID', 'DriverProviderName', 'DriverDate', 'DriverVersion') }
            [ordered]@{ Name = 'ProblemDevices'; ClassName = 'Win32_PnPEntity'; Filter = 'ConfigManagerErrorCode <> 0'; Properties = @('Name', 'ConfigManagerErrorCode', 'HardwareID', 'PNPDeviceID') }
            [ordered]@{ Name = 'Activation'; ClassName = 'SoftwareLicensingProduct'; Filter = "ApplicationID='55c92734-d682-4d71-983e-d6ec3f16059f' AND PartialProductKey IS NOT NULL"; Properties = @('LicenseStatus', 'Name') }
            [ordered]@{ Name = 'ComputerSystemProduct'; ClassName = 'Win32_ComputerSystemProduct'; Filter = ''; Properties = @('Version', 'IdentifyingNumber') }
            [ordered]@{ Name = 'Bios'; ClassName = 'Win32_BIOS'; Filter = ''; Properties = @('SerialNumber') }
            [ordered]@{ Name = 'Processor'; ClassName = 'Win32_Processor'; Filter = ''; Properties = @('Manufacturer') }
            [ordered]@{ Name = 'ComputerSystem'; ClassName = 'Win32_ComputerSystem'; Filter = ''; Properties = @('Manufacturer', 'Model') }
        )
        Repeat             = 5
        SettingsUris       = @(
            [ordered]@{ Uri = 'ms-settings:windowsupdate'; Label = 'windowsupdate'; Markers = @('Windows Update', 'Historia aktualizacji', 'Opcje zaawansowane') }
            [ordered]@{ Uri = 'ms-settings:windowsupdate-optionalupdates'; Label = 'optionalupdates'; Markers = @('Aktualizacje opcjonalne') }
        )
        SettingsWaitSeconds = 30
        DisableTargets     = @('Composite Bus Enumerator', 'Microsoft Hyper-V Remote Desktop Control Channel', 'Microsoft Radio Device Enumeration Bus')
        TimeoutThresholdMs = 5000
    }
}

# ---------------------------------------------------------------------------------------------------------------------
# Funkcje wysyłane do VM (samowystarczalne: wołają siebie nawzajem, funkcje Explore z modułu i cmdlety systemowe).
# ---------------------------------------------------------------------------------------------------------------------

function Test-E4A3VmComputerName {
    # Nazwy komputerów VM nadawane przez New-TestVm.ps1 (VmBuild: nazwa VM wielkimi literami).
    [CmdletBinding()]
    [OutputType([bool])]
    param([AllowEmptyString()][string]$ComputerName)
    return (@('DSO-26H2-HOME', 'DSO-26H2-PRO') -contains ([string]$ComputerName).ToUpperInvariant())
}

function Assert-E4A3InsideVm {
    # Ostatni bezpiecznik: sondy E4A3 wyłączają urządzenia, usuwają kartę graficzną z menedżera urządzeń, rejestrują zadania, uruchamiają
    # Ustawienia i zamykają je, więc działają WYŁĄCZNIE w VM DSO-26H2-Home/Pro i nigdy w testach na mockach (DSO_VM_TESTS_MOCK_ONLY).
    [CmdletBinding()]
    param([string]$ComputerName = $env:COMPUTERNAME)
    if ($env:DSO_VM_TESTS_MOCK_ONLY) {
        throw 'Tryb DSO_VM_TESTS_MOCK_ONLY: sondy E4A3 nie mogą działać w testach na mockach (nic nie zostało uruchomione).'
    }
    if (-not (Test-E4A3VmComputerName -ComputerName $ComputerName)) {
        throw "Sondy E4A3 działają tylko w VM DSO-26H2-HOME/DSO-26H2-PRO (ten komputer: '$ComputerName'); nic nie zostało uruchomione."
    }
}

function Get-E4A3Median {
    # Mediana liczb (parzysta liczba = średnia dwóch środkowych); pusta lista = $null.
    [CmdletBinding()]
    param([AllowNull()][AllowEmptyCollection()][object[]]$Values)
    $Sorted = @(@($Values) | Where-Object { $null -ne $_ } | ForEach-Object { [double]$_ } | Sort-Object)
    if ($Sorted.Count -eq 0) { return $null }
    $Middle = [int][Math]::Floor($Sorted.Count / 2)
    if ($Sorted.Count % 2 -eq 1) { return $Sorted[$Middle] }
    return (($Sorted[$Middle - 1] + $Sorted[$Middle]) / 2)
}

function Get-E4A3IntegrityName {
    # RID poziomu integralności (MS: Mandatory Label SID S-1-16-<RID>) -> nazwa.
    [CmdletBinding()]
    [OutputType([string])]
    param([AllowNull()]$Rid)
    if ($null -eq $Rid -or [int]$Rid -lt 0) { return $null }
    switch ([int]$Rid) {
        0 { return 'Untrusted' }
        4096 { return 'Low' }
        8192 { return 'Medium' }
        8448 { return 'MediumPlus' }
        12288 { return 'High' }
        16384 { return 'System' }
        default { return "RID $Rid" }
    }
}

function ConvertTo-E4A3Row {
    # Instancja CIM -> obiekt z wybranymi właściwościami (tablice zostają tablicami, daty jako ISO 'o').
    [CmdletBinding()]
    param(
        [Parameter(Mandatory)]$Instance,
        [Parameter(Mandatory)][string[]]$Properties
    )
    $Row = [ordered]@{}
    foreach ($Name in $Properties) {
        $Value = $null
        $Property = $Instance.CimInstanceProperties[$Name]
        if ($null -ne $Property) { $Value = $Property.Value }
        if ($Value -is [datetime]) { $Value = $Value.ToString('o') }
        elseif ($Value -is [array]) { $Value = @($Value | ForEach-Object { [string]$_ }) }
        $Row[$Name] = $Value
    }
    return [pscustomobject]$Row
}

function Measure-E4A3Query {
    # Jedno zapytanie CIM Repeat razy: czasy (ms), mediana, pierwszy (zimny) odczyt i wiersze z ostatniego udanego odczytu. Błąd jest daną.
    [CmdletBinding()]
    param(
        [Parameter(Mandatory)]$Query,
        [ValidateRange(1, 20)][int]$Repeat = 5
    )
    $Out = [ordered]@{ Name = [string]$Query.Name; ClassName = [string]$Query.ClassName; Filter = [string]$Query.Filter; Ms = @(); FirstMs = $null; MedianMs = $null; Count = $null; Rows = @(); Errors = @() }
    $Times = @()
    for ($Run = 1; $Run -le $Repeat; $Run++) {
        $Watch = [System.Diagnostics.Stopwatch]::StartNew()
        try {
            $Arguments = @{ ClassName = [string]$Query.ClassName; Property = @($Query.Properties); OperationTimeoutSec = 120; ErrorAction = 'Stop' }
            if ($Query.Filter) { $Arguments.Filter = [string]$Query.Filter }
            $Instances = @(Get-CimInstance @Arguments)
            $Watch.Stop()
            $Times += [int]$Watch.ElapsedMilliseconds
            $Out.Count = $Instances.Count
            $Out.Rows = @($Instances | ForEach-Object { ConvertTo-E4A3Row -Instance $_ -Properties @($Query.Properties) })
        }
        catch {
            $Watch.Stop()
            $Out.Errors += ('{0} ms: 0x{1:X8} {2}' -f $Watch.ElapsedMilliseconds, $_.Exception.HResult, $_.Exception.Message)
        }
    }
    $Out.Ms = @($Times)
    if ($Times.Count -gt 0) { $Out.FirstMs = $Times[0] }
    $Out.MedianMs = Get-E4A3Median -Values $Times
    return [pscustomobject]$Out
}

function Invoke-E4A3Queries {
    # Wszystkie zapytania z planu po kolei (każde Repeat razy).
    [CmdletBinding()]
    param(
        [Parameter(Mandatory)][object[]]$Queries,
        [ValidateRange(1, 20)][int]$Repeat = 5
    )
    return @(foreach ($Query in $Queries) { Measure-E4A3Query -Query $Query -Repeat $Repeat })
}

function Get-E4A3InfEvidence {
    # Nazwy „Podstawowej karty graficznej Microsoft” z INF systemu (display.inf, displayoverride.inf) i ich tłumaczeń pl-PL (*.inf_loc); tylko odczyt.
    [CmdletBinding()]
    param()
    $Files = @('C:\Windows\INF\display.inf', 'C:\Windows\INF\displayoverride.inf',
        'C:\Windows\System32\DriverStore\pl-PL\display.inf_loc', 'C:\Windows\System32\DriverStore\pl-PL\displayoverride.inf_loc')
    return @(foreach ($File in $Files) {
            $Entry = [ordered]@{ Path = $File; Exists = (Test-Path -LiteralPath $File); Lines = @(); Error = $null }
            if ($Entry.Exists) {
                try { $Entry.Lines = @(Select-String -LiteralPath $File -Pattern 'MSBD|CC_030|DriverVer' -ErrorAction Stop | ForEach-Object { $_.Line.Trim() }) }
                catch { $Entry.Error = $_.Exception.Message }
            }
            [pscustomobject]$Entry
        })
}

function Get-E4A3DeviceReads {
    # Odczyt po kroku sondy urządzeń: karta graficzna, sterownik DISPLAY i urządzenia z problemem (po jednym zapytaniu).
    [CmdletBinding()]
    param([Parameter(Mandatory)][object[]]$Queries)
    $Wanted = @($Queries | Where-Object { @('VideoController', 'SignedDriverDisplay', 'ProblemDevices') -contains $_.Name })
    return @(Invoke-E4A3Queries -Queries $Wanted -Repeat 1)
}

function Invoke-E4A3DeviceProbe {
    # K1: kod 22 (Disable-PnpDevice na pierwszym dającym się wyłączyć nieistotnym urządzeniu z listy, odczyt, Enable-PnpDevice, odczyt) i próby
    # wymuszenia „Podstawowej karty graficznej Microsoft”: wyłączenie karty, odczyt, włączenie; pnputil /remove-device, odczyt, pnputil /scan-devices, odczyt.
    [CmdletBinding()]
    param(
        [Parameter(Mandatory)][object[]]$Queries,
        [Parameter(Mandatory)][string[]]$DisableTargets
    )
    Assert-E4A3InsideVm
    $Out = [ordered]@{ Code22 = $null; Graphics = $null; Inf = @() }
    $Code22 = [ordered]@{ Attempts = @(); Target = $null; InstanceId = $null; AfterDisable = @(); EnableError = $null; AfterEnable = @(); Error = $null }
    try {
        foreach ($Target in $DisableTargets) {
            $Attempt = [ordered]@{ Target = $Target; InstanceId = $null; DisableError = $null }
            $Device = Get-PnpDevice -FriendlyName $Target -PresentOnly -ErrorAction SilentlyContinue | Select-Object -First 1
            if ($null -eq $Device) { $Attempt.DisableError = 'brak urządzenia'; $Code22.Attempts += [pscustomobject]$Attempt; continue }
            $Attempt.InstanceId = $Device.InstanceId
            try { Disable-PnpDevice -InstanceId $Device.InstanceId -Confirm:$false -ErrorAction Stop } catch { $Attempt.DisableError = $_.Exception.Message }
            $Code22.Attempts += [pscustomobject]$Attempt
            if ($null -eq $Attempt.DisableError) { $Code22.Target = $Target; $Code22.InstanceId = $Device.InstanceId; break }
        }
        if ($null -ne $Code22.InstanceId) {
            Start-Sleep -Seconds 3
            $Code22.AfterDisable = @(Get-E4A3DeviceReads -Queries $Queries)
            try { Enable-PnpDevice -InstanceId $Code22.InstanceId -Confirm:$false -ErrorAction Stop } catch { $Code22.EnableError = $_.Exception.Message }
            Start-Sleep -Seconds 3
            $Code22.AfterEnable = @(Get-E4A3DeviceReads -Queries $Queries)
        }
    }
    catch { $Code22.Error = $_.Exception.Message }
    $Out.Code22 = [pscustomobject]$Code22
    $Graphics = [ordered]@{ InstanceId = $null; Before = @(); DisableError = $null; AfterDisable = @(); EnableError = $null; AfterEnable = @()
        Remove = $null; AfterRemove = @(); Scan = $null; AfterScan = @(); Error = $null }
    try {
        $Graphics.Before = @(Get-E4A3DeviceReads -Queries $Queries)
        $Video = Get-CimInstance -ClassName Win32_VideoController -ErrorAction Stop | Select-Object -First 1
        $Graphics.InstanceId = [string]$Video.PNPDeviceID
        try { Disable-PnpDevice -InstanceId $Graphics.InstanceId -Confirm:$false -ErrorAction Stop } catch { $Graphics.DisableError = $_.Exception.Message }
        Start-Sleep -Seconds 5
        $Graphics.AfterDisable = @(Get-E4A3DeviceReads -Queries $Queries)
        try { Enable-PnpDevice -InstanceId $Graphics.InstanceId -Confirm:$false -ErrorAction Stop } catch { $Graphics.EnableError = $_.Exception.Message }
        Start-Sleep -Seconds 5
        $Graphics.AfterEnable = @(Get-E4A3DeviceReads -Queries $Queries)
        $PnpUtil = Join-Path $env:SystemRoot 'System32\pnputil.exe'
        $Graphics.Remove = Invoke-ExploreNative -FilePath $PnpUtil -Arguments @('/remove-device', $Graphics.InstanceId) -TimeoutSeconds 60
        Start-Sleep -Seconds 5
        $Graphics.AfterRemove = @(Get-E4A3DeviceReads -Queries $Queries)
        $Graphics.Scan = Invoke-ExploreNative -FilePath $PnpUtil -Arguments @('/scan-devices') -TimeoutSeconds 120
        Start-Sleep -Seconds 8
        $Graphics.AfterScan = @(Get-E4A3DeviceReads -Queries $Queries)
    }
    catch { $Graphics.Error = $_.Exception.Message }
    $Out.Graphics = [pscustomobject]$Graphics
    $Out.Inf = @(Get-E4A3InfEvidence)
    return [pscustomobject]$Out
}

# --- K2: operacje w sesji dso (kopiowane do skryptu zadania; Add-Type tylko tam) ---

function Get-E4A3NativeSource {
    # P/Invoke (C# 5, Add-Type z PowerShell 5.1), wg MS Learn: EnumWindows/GetWindowText/GetClassName/GetWindowThreadProcessId/IsWindowVisible;
    # OpenProcess(PROCESS_QUERY_LIMITED_INFORMATION = 0x1000) -> OpenProcessToken(TOKEN_QUERY = 0x0008) -> GetTokenInformation
    # (TokenElevationType = 18, TokenElevation = 20, TokenIntegrityLevel = 25: TOKEN_MANDATORY_LABEL, RID = ostatnie podporządkowanie SID).
    [CmdletBinding()]
    [OutputType([string])]
    param()
    return @'
using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Text;

namespace DsoE4A3
{
    public class WindowInfo
    {
        public long Handle;
        public string Title;
        public string ClassName;
        public uint ProcessId;
        public bool Visible;
    }

    public static class Native
    {
        private delegate bool EnumWindowsProc(IntPtr hWnd, IntPtr lParam);

        [DllImport("user32.dll")]
        private static extern bool EnumWindows(EnumWindowsProc lpEnumFunc, IntPtr lParam);
        [DllImport("user32.dll", CharSet = CharSet.Unicode)]
        private static extern int GetWindowText(IntPtr hWnd, StringBuilder lpString, int nMaxCount);
        [DllImport("user32.dll", CharSet = CharSet.Unicode)]
        private static extern int GetClassName(IntPtr hWnd, StringBuilder lpClassName, int nMaxCount);
        [DllImport("user32.dll")]
        private static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint lpdwProcessId);
        [DllImport("user32.dll")]
        private static extern bool IsWindowVisible(IntPtr hWnd);
        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern IntPtr OpenProcess(uint dwDesiredAccess, bool bInheritHandle, uint dwProcessId);
        [DllImport("advapi32.dll", SetLastError = true)]
        private static extern bool OpenProcessToken(IntPtr ProcessHandle, uint DesiredAccess, out IntPtr TokenHandle);
        [DllImport("advapi32.dll", SetLastError = true)]
        private static extern bool GetTokenInformation(IntPtr TokenHandle, int TokenInformationClass, out int TokenInformation, int TokenInformationLength, out int ReturnLength);
        [DllImport("advapi32.dll", SetLastError = true)]
        private static extern bool GetTokenInformation(IntPtr TokenHandle, int TokenInformationClass, IntPtr TokenInformation, int TokenInformationLength, out int ReturnLength);
        [DllImport("advapi32.dll")]
        private static extern IntPtr GetSidSubAuthorityCount(IntPtr pSid);
        [DllImport("advapi32.dll")]
        private static extern IntPtr GetSidSubAuthority(IntPtr pSid, uint nSubAuthority);
        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern bool CloseHandle(IntPtr hObject);

        public static WindowInfo[] ListTopLevelWindows()
        {
            List<WindowInfo> list = new List<WindowInfo>();
            EnumWindows(delegate (IntPtr hWnd, IntPtr lParam)
            {
                StringBuilder title = new StringBuilder(512);
                StringBuilder className = new StringBuilder(256);
                GetWindowText(hWnd, title, title.Capacity);
                GetClassName(hWnd, className, className.Capacity);
                uint processId;
                GetWindowThreadProcessId(hWnd, out processId);
                WindowInfo info = new WindowInfo();
                info.Handle = hWnd.ToInt64();
                info.Title = title.ToString();
                info.ClassName = className.ToString();
                info.ProcessId = processId;
                info.Visible = IsWindowVisible(hWnd);
                list.Add(info);
                return true;
            }, IntPtr.Zero);
            return list.ToArray();
        }

        // Wynik: [0] TokenIsElevated (0/1, -1 = brak), [1] TokenElevationType (-1 = brak), [2] RID integralności (-1 = brak), [3] błąd Win32 (0 = brak).
        public static int[] QueryToken(uint processId)
        {
            int[] result = new int[] { -1, -1, -1, 0 };
            IntPtr process = OpenProcess(0x1000, false, processId);
            if (process == IntPtr.Zero)
            {
                result[3] = Marshal.GetLastWin32Error();
                return result;
            }
            try
            {
                IntPtr token;
                if (!OpenProcessToken(process, 0x0008, out token))
                {
                    result[3] = Marshal.GetLastWin32Error();
                    return result;
                }
                try
                {
                    int value;
                    int length;
                    if (GetTokenInformation(token, 20, out value, 4, out length)) { result[0] = value; } else { result[3] = Marshal.GetLastWin32Error(); }
                    if (GetTokenInformation(token, 18, out value, 4, out length)) { result[1] = value; } else { result[3] = Marshal.GetLastWin32Error(); }
                    int size;
                    GetTokenInformation(token, 25, IntPtr.Zero, 0, out size);
                    if (size > 0)
                    {
                        IntPtr buffer = Marshal.AllocHGlobal(size);
                        try
                        {
                            if (GetTokenInformation(token, 25, buffer, size, out size))
                            {
                                IntPtr sid = Marshal.ReadIntPtr(buffer);
                                int count = Marshal.ReadByte(GetSidSubAuthorityCount(sid));
                                result[2] = Marshal.ReadInt32(GetSidSubAuthority(sid, (uint)(count - 1)));
                            }
                            else { result[3] = Marshal.GetLastWin32Error(); }
                        }
                        finally { Marshal.FreeHGlobal(buffer); }
                    }
                }
                finally { CloseHandle(token); }
            }
            finally { CloseHandle(process); }
            return result;
        }
    }
}
'@
}

function Initialize-E4A3K2Native {
    [CmdletBinding()]
    param()
    if ('DsoE4A3.Native' -as [type]) { return }
    Add-Type -AssemblyName System.Drawing
    Add-Type -AssemblyName System.Windows.Forms
    Add-Type -AssemblyName UIAutomationClient
    Add-Type -AssemblyName UIAutomationTypes
    Add-Type -TypeDefinition (Get-E4A3NativeSource) -Language CSharp
}

function Save-E4A3K2Shot {
    # Zrzut pulpitu sesji dso do C:\dso\e4a3\shots\<Label>.png; zwraca nazwę pliku.
    [CmdletBinding()]
    param([Parameter(Mandatory)][ValidatePattern('^[a-z0-9-]+\z')][string]$Label)
    Assert-E4A3InsideVm
    $Directory = 'C:\dso\e4a3\shots'
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

function Get-E4A3ProcessInfo {
    # Proces: token (podniesienie, typ, integralność) i właściciel (Win32_Process.GetOwner).
    [CmdletBinding()]
    param([Parameter(Mandatory)][int]$ProcessId)
    $Info = [ordered]@{ ProcessId = $ProcessId; Elevated = $null; ElevationType = $null; IntegrityRid = $null; Integrity = $null; Win32Error = $null; Owner = $null; Error = $null }
    try {
        $Raw = [DsoE4A3.Native]::QueryToken([uint32]$ProcessId)
        if ([int]$Raw[0] -ge 0) { $Info.Elevated = ([int]$Raw[0] -ne 0) }
        if ([int]$Raw[1] -ge 0) { $Info.ElevationType = switch ([int]$Raw[1]) { 1 { 'Default' } 2 { 'Full' } 3 { 'Limited' } default { "nieznany ($($Raw[1]))" } } }
        if ([int]$Raw[2] -ge 0) { $Info.IntegrityRid = [int]$Raw[2]; $Info.Integrity = Get-E4A3IntegrityName -Rid ([int]$Raw[2]) }
        if ([int]$Raw[3] -ne 0) { $Info.Win32Error = [int]$Raw[3] }
        $Process = Get-CimInstance -ClassName Win32_Process -Filter "ProcessId=$ProcessId" -ErrorAction Stop
        if ($null -ne $Process) {
            $Owner = Invoke-CimMethod -InputObject $Process -MethodName GetOwner -ErrorAction Stop
            $Info.Owner = "$($Owner.Domain)\$($Owner.User)"
        }
    }
    catch { $Info.Error = $_.Exception.Message }
    return [pscustomobject]$Info
}

function Find-E4A3SettingsWindow {
    # Widoczne okno najwyższego poziomu Ustawień: ramka aplikacji (ApplicationFrameWindow) z tytułem „Ustawienia”/„Settings”.
    [CmdletBinding()]
    param()
    $Windows = @([DsoE4A3.Native]::ListTopLevelWindows() | Where-Object { $_.Visible -and $_.ClassName -eq 'ApplicationFrameWindow' -and ($_.Title -like 'Ustawienia*' -or $_.Title -like 'Settings*') })
    return ($Windows | Select-Object -First 1)
}

function Get-E4A3WindowNames {
    # Nazwy elementów okna (UI Automation, potomkowie), unikalne, niepuste, najwyżej 400.
    [CmdletBinding()]
    param([Parameter(Mandatory)][long]$Handle)
    $Root = [System.Windows.Automation.AutomationElement]::FromHandle([IntPtr]$Handle)
    $All = $Root.FindAll([System.Windows.Automation.TreeScope]::Descendants, [System.Windows.Automation.Condition]::TrueCondition)
    $Names = New-Object System.Collections.Generic.List[string]
    foreach ($Element in $All) {
        $Name = [string]$Element.Current.Name
        if ($Name -and -not $Names.Contains($Name)) { $Names.Add($Name) }
        if ($Names.Count -ge 400) { break }
    }
    return @($Names)
}

function Test-E4A3Markers {
    # Czy każdy znacznik strony jest wśród nazw elementów okna (dokładnie albo jako początek nazwy), bez rozróżniania wielkości liter.
    [CmdletBinding()]
    [OutputType([bool])]
    param(
        [AllowNull()][AllowEmptyCollection()][string[]]$Names,
        [AllowEmptyCollection()][string[]]$Markers
    )
    if (@($Markers).Count -eq 0) { return $false }
    foreach ($Marker in $Markers) {
        $Hit = @(@($Names) | Where-Object { $_ -and $_.StartsWith($Marker, [System.StringComparison]::OrdinalIgnoreCase) })
        if ($Hit.Count -eq 0) { return $false }
    }
    return $true
}

function Stop-E4A3Settings {
    # Zamknięcie Ustawień między pomiarami (start na zimno przy następnym adresie); czeka do 10 s na koniec procesów.
    [CmdletBinding()]
    param()
    Assert-E4A3InsideVm
    $Processes = @(Get-Process -Name 'SystemSettings' -ErrorAction SilentlyContinue)
    foreach ($Process in $Processes) { try { $Process.Kill() } catch { $null = $_ } }
    foreach ($Process in $Processes) { try { $null = $Process.WaitForExit(10000) } catch { $null = $_ } }
    return $Processes.Count
}

function Invoke-E4A3OpenSettings {
    # Operacja Settings (konto dso, podniesione - jak DSOptimizer): CreateProcess %SystemRoot%\explorer.exe "<uri>" (UseShellExecute=false),
    # czas do procesu SystemSettings.exe i do okna Ustawień, token i właściciel SystemSettings.exe i explorer.exe, nazwy okna, zrzut; na końcu zamknięcie.
    [CmdletBinding()]
    param(
        [Parameter(Mandatory)][ValidatePattern('^ms-settings:[a-z-]+\z')][string]$Uri,
        [Parameter(Mandatory)][ValidatePattern('^[a-z0-9-]+\z')][string]$Label,
        [AllowEmptyCollection()][string[]]$Markers = @(),
        [ValidateRange(5, 120)][int]$WaitSeconds = 30
    )
    Assert-E4A3InsideVm
    $Out = [ordered]@{ Op = 'Settings'; Uri = $Uri; Label = $Label; Caller = $null; ClosedBefore = $null; PreexistingPids = @(); Explorer = $null; ExplorerExitCode = $null
        ExplorerExitedMs = $null; ProcessMs = $null; WindowMs = $null; SettingsProcess = $null; Window = $null; Names = @(); Markers = @($Markers); PageMatched = $false
        Shot = $null; ClosedAfter = $null; Error = $null }
    try {
        $Out.Caller = Get-E4A3ProcessInfo -ProcessId $PID
        $Out.ClosedBefore = Stop-E4A3Settings
        Start-Sleep -Seconds 2
        $Out.PreexistingPids = @(Get-Process -Name 'SystemSettings' -ErrorAction SilentlyContinue | ForEach-Object { $_.Id })
        $Psi = New-Object System.Diagnostics.ProcessStartInfo
        $Psi.FileName = Join-Path $env:SystemRoot 'explorer.exe'
        $Psi.Arguments = '"' + $Uri + '"'
        $Psi.UseShellExecute = $false
        $Watch = [System.Diagnostics.Stopwatch]::StartNew()
        $Explorer = [System.Diagnostics.Process]::Start($Psi)
        try { $Out.Explorer = Get-E4A3ProcessInfo -ProcessId $Explorer.Id } catch { $Out.Explorer = $_.Exception.Message }
        $SettingsPid = $null
        $Deadline = $WaitSeconds * 1000
        while ($Watch.ElapsedMilliseconds -lt $Deadline) {
            if ($null -eq $Out.ExplorerExitedMs -and $Explorer.HasExited) { $Out.ExplorerExitedMs = [int]$Watch.ElapsedMilliseconds; $Out.ExplorerExitCode = $Explorer.ExitCode }
            if ($null -eq $SettingsPid) {
                $Found = @(Get-Process -Name 'SystemSettings' -ErrorAction SilentlyContinue | Where-Object { $Out.PreexistingPids -notcontains $_.Id }) | Select-Object -First 1
                if ($null -ne $Found) { $SettingsPid = $Found.Id; $Out.ProcessMs = [int]$Watch.ElapsedMilliseconds }
            }
            $Window = Find-E4A3SettingsWindow
            if ($null -ne $Window -and $null -ne $SettingsPid) {
                $Out.WindowMs = [int]$Watch.ElapsedMilliseconds
                $Out.Window = [pscustomobject]@{ Handle = $Window.Handle; Title = $Window.Title; ClassName = $Window.ClassName; ProcessId = $Window.ProcessId }
                break
            }
            Start-Sleep -Milliseconds 200
        }
        if ($null -ne $SettingsPid) { $Out.SettingsProcess = Get-E4A3ProcessInfo -ProcessId $SettingsPid }
        if ($null -ne $Out.Window) {
            Start-Sleep -Seconds 4
            try { $Out.Names = @(Get-E4A3WindowNames -Handle ([long]$Out.Window.Handle)) } catch { $Out.Error = "UI Automation: $($_.Exception.Message)" }
            $Out.PageMatched = Test-E4A3Markers -Names $Out.Names -Markers $Markers
        }
        else { Start-Sleep -Seconds 2 }
        try { $Out.Shot = Save-E4A3K2Shot -Label "settings-$Label" } catch { $Out.Error = (@($Out.Error, "zrzut: $($_.Exception.Message)") | Where-Object { $_ }) -join ' | ' }
        if ($null -eq $Out.ExplorerExitedMs -and $Explorer.HasExited) { $Out.ExplorerExitCode = $Explorer.ExitCode }
    }
    catch { $Out.Error = (@($Out.Error, $_.Exception.Message) | Where-Object { $_ }) -join ' | ' }
    finally { try { $Out.ClosedAfter = Stop-E4A3Settings } catch { $null = $_ } }
    return [pscustomobject]$Out
}

function Invoke-E4A3K2Ops {
    # Dyspozytor operacji K2 (lista {Op, ...argumenty}); błąd operacji jest daną.
    [CmdletBinding()]
    param([Parameter(Mandatory)][AllowEmptyCollection()][object[]]$Ops)
    Assert-E4A3InsideVm
    Initialize-E4A3K2Native
    $Results = @()
    foreach ($Op in $Ops) {
        try {
            switch ([string]$Op.Op) {
                'Queries' { $Results += [pscustomobject]@{ Op = 'Queries'; Caller = (Get-E4A3ProcessInfo -ProcessId $PID); Queries = @(Invoke-E4A3Queries -Queries @($Op.Queries) -Repeat ([int]$Op.Repeat)) } }
                'Settings' { $Results += Invoke-E4A3OpenSettings -Uri ([string]$Op.Uri) -Label ([string]$Op.Label) -Markers @($Op.Markers) -WaitSeconds ([int]$Op.WaitSeconds) }
                default { throw "Nieznana operacja K2 '$($Op.Op)'." }
            }
        }
        catch { $Results += [pscustomobject]@{ Op = [string]$Op.Op; Error = $_.Exception.Message } }
    }
    return $Results
}

function Get-E4A3ChildFunctionNames {
    # Funkcje kopiowane do skryptu zadania K2 (domknięcie wywołań pilnuje test); z bezpiecznikiem, który dziecko woła przed operacjami.
    [CmdletBinding()]
    [OutputType([string[]])]
    param()
    return @('Test-E4A3VmComputerName', 'Assert-E4A3InsideVm', 'Get-E4A3Median', 'Get-E4A3IntegrityName', 'ConvertTo-E4A3Row', 'Measure-E4A3Query', 'Invoke-E4A3Queries',
        'Get-E4A3NativeSource', 'Initialize-E4A3K2Native', 'Save-E4A3K2Shot', 'Get-E4A3ProcessInfo', 'Find-E4A3SettingsWindow', 'Get-E4A3WindowNames', 'Test-E4A3Markers',
        'Stop-E4A3Settings', 'Invoke-E4A3OpenSettings', 'Invoke-E4A3K2Ops')
}

function New-E4A3ChildScript {
    # Skrypt zadania K2: param(ResultFile, OpsFile), definicje funkcji, Invoke-E4A3K2Ops na liście z pliku JSON, wynik atomowo przez plik tymczasowy.
    [CmdletBinding()]
    [OutputType([string])]
    param()
    $Header = @'
param([Parameter(Mandatory)][string]$ResultFile, [Parameter(Mandatory)][string]$OpsFile)
$ErrorActionPreference = 'Stop'
'@
    $Definitions = foreach ($Name in Get-E4A3ChildFunctionNames) { "function $Name {`n$((Get-Command -Name $Name -CommandType Function -ErrorAction Stop).Definition)`n}" }
    $Main = @'
$Document = [ordered]@{ Ok = $false; Error = $null; ProcessId = $PID; Results = @() }
try {
    Assert-E4A3InsideVm
    $Parsed = Get-Content -LiteralPath $OpsFile -Raw -Encoding UTF8 | ConvertFrom-Json
    $Ops = @(foreach ($Item in $Parsed) { $Item })
    $Document.Results = @(Invoke-E4A3K2Ops -Ops $Ops)
    $Document.Ok = $true
}
catch { $Document.Error = $_.Exception.Message }
$Temporary = $ResultFile + '.tmp'
$Document | ConvertTo-Json -Depth 12 | Set-Content -LiteralPath $Temporary -Encoding UTF8
Move-Item -LiteralPath $Temporary -Destination $ResultFile -Force
'@
    return ($Header + "`n`n" + ($Definitions -join "`n`n") + "`n`n" + $Main)
}

function Invoke-E4A3UserTask {
    # K2: zadanie Harmonogramu (dso / Interactive / Highest) z listą operacji; wynik z pliku; zadanie ZAWSZE wyrejestrowane.
    # Niedostępność K2 to dana (Available=false, Reason), nie wyjątek.
    [CmdletBinding()]
    param(
        [Parameter(Mandatory)][ValidatePattern('^[a-z0-9-]+\z')][string]$Name,
        [Parameter(Mandatory)][AllowEmptyCollection()][object[]]$Ops,
        [ValidateRange(10, 1800)][int]$TimeoutSeconds = 300,
        [ValidatePattern('^[A-Za-z0-9_-]+\z')][string]$UserName = 'dso'
    )
    Assert-E4A3InsideVm
    $Directory = 'C:\dso\e4a3'
    $TaskName = "DSO-E4A3-$Name"
    $ScriptPath = "$Directory\k2.ps1"
    $OpsPath = "$Directory\$Name.ops.json"
    $ResultPath = "$Directory\$Name.json"
    $Out = [ordered]@{ Name = $Name; Available = $false; Reason = $null; WaitedMs = $null; ChildError = $null; UnregisterError = $null; Results = @() }
    $Registered = $false
    $Watch = [System.Diagnostics.Stopwatch]::StartNew()
    try {
        $null = New-Item -ItemType Directory -Path $Directory -Force
        Set-Content -LiteralPath $ScriptPath -Value (New-E4A3ChildScript) -Encoding UTF8
        Set-Content -LiteralPath $OpsPath -Value (ConvertTo-Json -InputObject @($Ops) -Depth 8) -Encoding UTF8
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

function Invoke-E4A3Collection {
    # Punkt wejścia po stronie VM (jedna faza): zwraca JEDEN string JSON.
    # inventory: zapytania w K1 (Administrator) i K2 (dso podniesione); settings: oba adresy ms-settings w K2; devices: kod 22, karta graficzna, INF (K1).
    [CmdletBinding()]
    param(
        [Parameter(Mandatory)][string]$PlanJson,
        [Parameter(Mandatory)][ValidateSet('inventory', 'settings', 'devices')][string]$Phase
    )
    Assert-E4A3InsideVm
    $Plan = $PlanJson | ConvertFrom-Json
    $Document = [ordered]@{ meta = $null; phase = $Phase }
    try { $Document.meta = Get-ExploreMeta } catch { $Document.meta = [ordered]@{ Error = $_.Exception.Message } }
    switch ($Phase) {
        'inventory' {
            $Document.k1 = @(Invoke-E4A3Queries -Queries @($Plan.Queries) -Repeat ([int]$Plan.Repeat))
            $Document.k2 = Invoke-E4A3UserTask -Name 'queries' -Ops @(@{ Op = 'Queries'; Queries = @($Plan.Queries); Repeat = [int]$Plan.Repeat }) -TimeoutSeconds 600
        }
        'settings' {
            $Ops = @(foreach ($Item in @($Plan.SettingsUris)) { @{ Op = 'Settings'; Uri = [string]$Item.Uri; Label = [string]$Item.Label; Markers = @($Item.Markers); WaitSeconds = [int]$Plan.SettingsWaitSeconds } })
            $Document.settings = Invoke-E4A3UserTask -Name 'settings' -Ops $Ops -TimeoutSeconds 300
        }
        'devices' { $Document.devices = Invoke-E4A3DeviceProbe -Queries @($Plan.Queries) -DisableTargets @($Plan.DisableTargets) }
    }
    return ($Document | ConvertTo-Json -Depth 14)
}

# ---------------------------------------------------------------------------------------------------------------------
# Strona hosta (nic z tego nie jest wysyłane do VM).
# ---------------------------------------------------------------------------------------------------------------------

function Get-E4A3RemoteFunctionNames {
    return @(
        'Test-E4A3VmComputerName', 'Assert-E4A3InsideVm', 'Get-E4A3Median', 'Get-E4A3IntegrityName', 'ConvertTo-E4A3Row', 'Measure-E4A3Query', 'Invoke-E4A3Queries',
        'Get-E4A3InfEvidence', 'Get-E4A3DeviceReads', 'Invoke-E4A3DeviceProbe', 'Get-E4A3NativeSource', 'Initialize-E4A3K2Native', 'Save-E4A3K2Shot', 'Get-E4A3ProcessInfo',
        'Find-E4A3SettingsWindow', 'Get-E4A3WindowNames', 'Test-E4A3Markers', 'Stop-E4A3Settings', 'Invoke-E4A3OpenSettings', 'Invoke-E4A3K2Ops',
        'Get-E4A3ChildFunctionNames', 'New-E4A3ChildScript', 'Invoke-E4A3UserTask', 'Invoke-E4A3Collection'
    )
}

function Get-E4A3HostOnlyFunctionNames {
    return @('Get-E4A3Plan', 'Get-E4A3RemoteFunctionNames', 'Get-E4A3HostOnlyFunctionNames', 'Get-E4A3RemoteScript', 'Assert-E4A3OutDirectory', 'Get-E4A3RunArguments',
        'Get-E4A3TimeoutDecision', 'Invoke-E4A3VmPhase', 'Complete-E4A3VmCleanup', 'Invoke-E4A3VmRun', 'Invoke-E4A3Exploration')
}

function Get-E4A3RemoteScript {
    # Tekst definicji funkcji zdalnych: część wspólna z modułu + funkcje E4A3 (FunctionInfo pobrane tu, w zasięgu skryptu).
    [CmdletBinding()]
    [OutputType([string])]
    param()
    $Names = @(Get-ExploreCommonRemoteFunctionNames) + @(Get-E4A3RemoteFunctionNames)
    return (ConvertTo-ExploreRemoteScript -Command @($Names | ForEach-Object { Get-Command -Name $_ -CommandType Function -ErrorAction Stop }))
}

function Assert-E4A3OutDirectory {
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

function Get-E4A3RunArguments {
    # Argumenty przebiegu po walidacji + plan (dla -ShowPlan).
    [CmdletBinding()]
    param(
        [Parameter(Mandatory)][string[]]$VmName,
        [Parameter(Mandatory)][string]$OutDirectory,
        [Parameter(Mandatory)][string]$CheckpointName,
        [Parameter(Mandatory)][int]$ReadyTimeoutSeconds
    )
    return [ordered]@{ VmName = @($VmName); OutDirectory = $OutDirectory; CheckpointName = $CheckpointName; ReadyTimeoutSeconds = $ReadyTimeoutSeconds; Plan = (Get-E4A3Plan) }
}

function Get-E4A3TimeoutDecision {
    # Plan: jeśli mediana któregokolwiek zapytania przekracza próg (5 s), limit PrepLoader = 20 s, inaczej 10 s. Brak median = 20 s (ostrożnie).
    [CmdletBinding()]
    param(
        [AllowNull()][AllowEmptyCollection()][object[]]$MediansMs,
        [int]$ThresholdMs = 5000
    )
    $Known = @(@($MediansMs) | Where-Object { $null -ne $_ } | ForEach-Object { [double]$_ })
    if ($Known.Count -eq 0) { return [pscustomobject]@{ TimeoutSeconds = 20; MaxMedianMs = $null; Reason = 'brak zmierzonych median' } }
    $Max = ($Known | Measure-Object -Maximum).Maximum
    $Seconds = if ($Max -gt $ThresholdMs) { 20 } else { 10 }
    return [pscustomobject]@{ TimeoutSeconds = $Seconds; MaxMedianMs = $Max; Reason = "największa mediana $Max ms, próg $ThresholdMs ms" }
}

function Invoke-E4A3VmPhase {
    # Jedna faza w VM przez otwartą sesję: definicje + Invoke-E4A3Collection; zwraca sparsowany dokument.
    [CmdletBinding()]
    param(
        [Parameter(Mandatory)][AllowNull()][System.Management.Automation.Runspaces.PSSession]$Session,
        [Parameter(Mandatory)][string]$RemoteScript,
        [Parameter(Mandatory)][string]$PlanJson,
        [Parameter(Mandatory)][ValidateSet('inventory', 'settings', 'devices')][string]$Phase
    )
    Assert-DsoVmLiveAllowed
    $Remote = {
        param([string]$Definitions, [string]$Json, [string]$Which)
        . ([scriptblock]::Create($Definitions))
        Invoke-E4A3Collection -PlanJson $Json -Phase $Which
    }
    $Raw = @(Invoke-Command -Session $Session -ScriptBlock $Remote -ArgumentList @($RemoteScript, $PlanJson, $Phase))
    $Text = [string]($Raw | Where-Object { $_ -is [string] } | Select-Object -Last 1)
    if ([string]::IsNullOrWhiteSpace($Text)) { throw "Faza '$Phase' nie zwróciła JSON." }
    return ($Text | ConvertFrom-Json)
}

function Complete-E4A3VmCleanup {
    # Koniec przebiegu: revert do checkpointu i sprawdzenie kontraktu stanu końcowego: VM w stanie Off i karta bez przełącznika.
    [CmdletBinding()]
    param(
        [Parameter(Mandatory)][ValidateSet('DSO-26H2-Home', 'DSO-26H2-Pro')][string]$VmName,
        [Parameter(Mandatory)][string]$CheckpointName
    )
    $Out = [ordered]@{ Ok = $false; RevertError = $null; CheckError = $null; FinalState = $null; FinalSwitch = $null; Violations = @(); Problems = @() }
    try { Restore-VmCleanState -VmName $VmName -CheckpointName $CheckpointName }
    catch { $Out.RevertError = $_.Exception.Message; $Out.Problems += "revert do '$CheckpointName': $($_.Exception.Message)" }
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

function Invoke-E4A3VmRun {
    # Jedna VM: revert -> start -> gotowość -> inventory -> settings -> devices -> kopia zrzutów -> ZAWSZE revert i sprawdzenie stanu końcowego -> zapis JSON.
    [CmdletBinding()]
    [OutputType([string])]
    param(
        [Parameter(Mandatory)][ValidateSet('DSO-26H2-Home', 'DSO-26H2-Pro')][string]$VmName,
        [Parameter(Mandatory)][string]$OutDirectory,
        [string]$CheckpointName = 'czysta-26H2',
        [int]$ReadyTimeoutSeconds = 1200
    )
    Assert-DsoVmLiveAllowed
    $Credential = Get-DsoVmCredential -VmName $VmName
    $Session = $null
    $StartedAt = Get-Date
    $Phases = [ordered]@{}
    $PrimaryError = $null
    $VmDirectory = Join-Path $OutDirectory $VmName
    try {
        Restore-VmCleanState -VmName $VmName -CheckpointName $CheckpointName
        Start-VM -Name $VmName
        $Session = Wait-VmReady -VmName $VmName -Credential $Credential -TimeoutSeconds $ReadyTimeoutSeconds
        # Chwila na dokończenie logowania dso (pasek zadań, Start) przed pierwszym odczytem.
        Start-Sleep -Seconds 45
        $RemoteScript = Get-E4A3RemoteScript
        $PlanJson = ConvertTo-Json -InputObject (Get-E4A3Plan) -Depth 6
        foreach ($Phase in 'inventory', 'settings', 'devices') {
            $Phases[$Phase] = Invoke-E4A3VmPhase -Session $Session -RemoteScript $RemoteScript -PlanJson $PlanJson -Phase $Phase
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
                Copy-Item -FromSession $Session -Path 'C:\dso\e4a3\shots\*.png' -Destination $Shots -ErrorAction Stop
            }
            catch { $PrimaryError = (@($PrimaryError, "kopia zrzutów: $($_.Exception.Message)") | Where-Object { $_ }) -join ' | ' }
            Remove-PSSession -Session $Session -ErrorAction SilentlyContinue
        }
        # Sprzątanie PRZED zapisem, żeby jego wynik (także błędy) trafił do JSON; błąd sprzątania nigdy nie jest ukrywany.
        $Cleanup = Complete-E4A3VmCleanup -VmName $VmName -CheckpointName $CheckpointName
        if (-not $Cleanup.Ok) {
            $CleanupMessage = "sprzątanie VM: $(@($Cleanup.Problems) -join ' | ')"
            Write-Warning "VM '$VmName': $CleanupMessage"
            $PrimaryError = (@($PrimaryError, $CleanupMessage) | Where-Object { $_ }) -join ' | '
        }
        $Result = [ordered]@{ schema = 1; vmName = $VmName; checkpoint = $CheckpointName; startedAt = $StartedAt.ToString('o'); finishedAt = (Get-Date).ToString('o')
            error = $PrimaryError; cleanup = $Cleanup; shots = $Shots; phases = $Phases }
        $Saved = Save-ExploreResult -Result $Result -OutDirectory $OutDirectory -VmName $VmName -FileName 'e4a3.json'
    }
    if ($PrimaryError) { throw "VM '$VmName': $PrimaryError (wynik częściowy: $Saved)" }
    return $Saved
}

function Invoke-E4A3Exploration {
    # Wszystkie wybrane VM po kolei. Błąd uwierzytelnienia = stop całości; inny błąd = zapis i dalej.
    [CmdletBinding()]
    param([Parameter(Mandatory)][System.Collections.IDictionary]$Arguments)
    $Paths = New-Object System.Collections.Generic.List[string]
    $Errors = New-Object System.Collections.Generic.List[string]
    foreach ($Vm in $Arguments.VmName) {
        try {
            Write-Host "$(Get-Date -Format 'HH:mm:ss') $Vm"
            $Paths.Add((Invoke-E4A3VmRun -VmName $Vm -OutDirectory $Arguments.OutDirectory -CheckpointName $Arguments.CheckpointName -ReadyTimeoutSeconds $Arguments.ReadyTimeoutSeconds))
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
        $OutDirectory = Assert-E4A3OutDirectory -Path $OutDirectory -RepoRoot (Get-DsoRepoRoot)
        $OutDirectory = Resolve-ExploreOutDirectory -Path $OutDirectory
        $RunArguments = Get-E4A3RunArguments -VmName $VmName -OutDirectory $OutDirectory -CheckpointName $CheckpointName -ReadyTimeoutSeconds $ReadyTimeoutSeconds
        if ($ShowPlan) {
            [pscustomobject]$RunArguments | ConvertTo-Json -Depth 6
            exit 0
        }
        Import-Module (Join-Path $VmRoot 'modules\VmCommon.psm1') -Force
        Import-Module (Join-Path $VmRoot 'modules\VmHost.psm1') -Force
        # ExploreCommon (zaimportowany wyżej) trzyma referencję do starej instancji VmHost; ponowny import po nim wiąże go z bieżącą.
        Import-Module (Join-Path $PSScriptRoot 'ExploreCommon.psm1') -Force
        $Principal = New-Object System.Security.Principal.WindowsPrincipal([System.Security.Principal.WindowsIdentity]::GetCurrent())
        if (-not $Principal.IsInRole([System.Security.Principal.WindowsBuiltInRole]::Administrator)) {
            throw 'Uruchom skrypt w sesji PowerShell jako administrator (Hyper-V i PowerShell Direct tego wymagają).'
        }
        Write-Host "Wyniki: $OutDirectory"
        Write-Host "VM: $($RunArguments.VmName -join ', ')"
        $Outcome = Invoke-E4A3Exploration -Arguments $RunArguments
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
