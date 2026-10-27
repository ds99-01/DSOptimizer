<#
Instrukcja uruchomienia:
    Sesja PowerShell uruchomiona jako administrator, na hoście z Hyper-V, z modułami testów VM (tests\vm\modules) i tests\vm\explore\ExploreCommon.psm1.
    VM utworzone wcześniej przez New-TestVm.ps1 (checkpoint 'czysta-26H2', poświadczenia w %LOCALAPPDATA%\DSOptimizer-vm, autologon 'dso').
    Przed przebiegiem: dotnet build DSOptimizer.sln -c Release (faza protection tworzy punkt przywracania przez DSOptimizer.exe --apply).
    Zalecane dwa przebiegi na VM (każdy od czystego checkpointu; wyniki w osobnych katalogach):
      A) bez sieci - mechanizmy offline:
        .\tests\vm\explore\Invoke-E4B1Exploration.ps1 -VmName DSO-26H2-Pro -NoNetwork -OutDirectory C:\...\exploration\offline
      B) z siecią - historia WU przed resetem, reset, skan po resecie, winget ze źródłem, DNS z prawdziwymi nazwami:
        .\tests\vm\explore\Invoke-E4B1Exploration.ps1 -VmName DSO-26H2-Pro -Phase wuhistory, wureset, network -OutDirectory C:\...\exploration\network
    Bez -Phase: wszystkie fazy w jednym przebiegu (baseline zawsze pierwsza; netreset-after dołącza się sama po netreset).
    -NoNetwork: pomija fazy z siecią (wuhistory, network); karta VM nie jest podłączana do 'Default Switch'.
    -ShowPlan: wypisuje rozstrzygnięte argumenty i plan (JSON) i kończy, nic nie uruchamia.
    -OutDirectory jest wymagany: ścieżka bezwzględna poza repo; w repo tylko katalog ignorowany przez git.
    Czas: przebieg A ok. 15 minut na VM (restart po resecie sieci), przebieg B do ok. 45 minut (czekanie na pierwszy wpis historii WU).
Opis działania:
    Pomiar partii 1 Etapu 4b (kategoria „Naprawy”, krótkie naprawy): DNS, reset sieci, reset Windows Update (usługi, foldery, polityki),
    Sklep (R33), winget, ochrona systemu. Wszystkie zmiany WYŁĄCZNIE w VM DSO-26H2-Home/Pro; na hoście skrypt tylko podłącza/odłącza kartę
    sieciową VM (Default Switch, wyłącznie od pierwszej do ostatniej fazy z siecią: wuhistory, network) i zapisuje wyniki.
    Przebieg jednej VM: revert do 'czysta-26H2' -> start (bez sieci) -> gotowość (zalogowany dso) -> kopia buildu DSOptimizera do C:\dso ->
    fazy -> kopia zrzutów -> ZAWSZE (finally) revert, odłączenie karty i sprawdzenie stanu końcowego (VM Off, karta bez przełącznika) przed zapisem JSON.
    Konteksty w VM:
      K1 - PowerShell Direct jako wbudowany Administrator (odczyty, usługi, foldery, polityki, netsh, ochrona systemu).
      K2 - zadanie Harmonogramu (principal dso / Interactive / Highest), czyli proces jak DSOptimizer po UAC: winget, Remove-/Add-AppxPackage
           dla konta dso, start Sklepu, okna Ustawień i „Ochrony systemu” przez UI Automation, zrzuty pulpitu dso.
    Fazy: baseline (stan świeżej VM) -> dns -> policies -> store -> winget -> protection -> [sieć] wuhistory (skan WU i czekanie na wpis historii)
    -> wureset -> network (DNS z siecią, winget source list/search, skan WU po resecie, historia) [koniec sieci] -> netreset -> restart VM
    -> netreset-after (odczyt i przywrócenie DHCP).
    Ostatni bezpiecznik po stronie VM: Assert-E4B1InsideVm (nazwa komputera DSO-26H2-HOME/PRO i brak DSO_VM_TESTS_MOCK_ONLY) na początku każdej
    funkcji ze skutkiem. Pierwszy błąd uwierzytelnienia przerywa cały skrypt (blokada konta). Skrypt dotyka wyłącznie VM DSO-26H2-Home
    i DSO-26H2-Pro (ValidateSet), nigdy innej VM; na hoście niczego nie zmienia.
    Wynik: <OutDirectory>\<VmName>\e4b1.json (UTF-8), zrzuty K2 <OutDirectory>\<VmName>\shots\*.png.
    Kod wyjścia: 0 gdy wszystkie wybrane VM zebrane bez błędu przebiegu, 1 w przeciwnym razie.
#>
[CmdletBinding()]
param(
    [ValidateSet('DSO-26H2-Home', 'DSO-26H2-Pro')][string[]]$VmName = @('DSO-26H2-Home', 'DSO-26H2-Pro'),
    [ValidateSet('baseline', 'dns', 'policies', 'store', 'winget', 'protection', 'wuhistory', 'wureset', 'network', 'netreset')][string[]]$Phase = @('baseline', 'dns', 'policies', 'store', 'winget', 'protection', 'wuhistory', 'wureset', 'network', 'netreset'),
    [string]$OutDirectory,
    [string]$BuildDirectory,
    [string]$CheckpointName = 'czysta-26H2',
    [ValidateRange(60, 7200)][int]$ReadyTimeoutSeconds = 1200,
    [switch]$NoNetwork,
    [switch]$ShowPlan
)

Import-Module (Join-Path $PSScriptRoot 'ExploreCommon.psm1') -Force

# ---------------------------------------------------------------------------------------------------------------------
# Plan (dane z planu Tasku 2 partii 1 Etapu 4b).
# ---------------------------------------------------------------------------------------------------------------------

function Get-E4B1Plan {
    [CmdletBinding()]
    param()
    $PolicyKey = 'HKLM\SOFTWARE\Policies\Microsoft\Windows\WindowsUpdate'
    return [ordered]@{
        WuServices        = @('wuauserv', 'bits', 'cryptsvc', 'UsoSvc', 'msiserver', 'WaaSMedicSvc', 'DoSvc')
        WuStopBasic       = @('wuauserv', 'bits', 'cryptsvc')
        WuStopExtra       = @('UsoSvc', 'msiserver')
        WuFolders         = @('SoftwareDistribution', 'System32\catroot2')
        RecreateChecksSeconds = @(0, 30, 90)
        PolicyKey         = $PolicyKey
        Policies          = @(
            [ordered]@{ Key = $PolicyKey; Name = 'WUServer'; Kind = 'REG_SZ'; Value = 'http://dso-wsus.invalid:8530' }
            [ordered]@{ Key = $PolicyKey; Name = 'WUStatusServer'; Kind = 'REG_SZ'; Value = 'http://dso-wsus.invalid:8530' }
            [ordered]@{ Key = $PolicyKey; Name = 'DisableWindowsUpdateAccess'; Kind = 'REG_DWORD'; Value = '1' }
            [ordered]@{ Key = $PolicyKey; Name = 'SetDisableUXWUAccess'; Kind = 'REG_DWORD'; Value = '1' }
            [ordered]@{ Key = "$PolicyKey\AU"; Name = 'NoAutoUpdate'; Kind = 'REG_DWORD'; Value = '1' }
            [ordered]@{ Key = "$PolicyKey\AU"; Name = 'UseWUServer'; Kind = 'REG_DWORD'; Value = '1' }
        )
        DnsHosts          = [ordered]@{ Address = '10.123.45.67'; Name = 'dso-hosts-test.invalid' }
        DnsNegativeName   = 'dso-neg-test.invalid'
        DnsNetworkNames   = @('www.microsoft.com', 'learn.microsoft.com', 'www.bing.com')
        NetManual         = [ordered]@{ Address = '192.168.77.10'; PrefixLength = 24; Mask = '255.255.255.0'; Gateway = '192.168.77.1'; Dns = @('192.168.77.53', '192.168.77.54') }
        Store             = [ordered]@{ Name = 'Microsoft.WindowsStore'; Aumid = 'Microsoft.WindowsStore_8wekyb3d8bbwe!App'; Process = 'WinStore.App'; WindowTitle = 'Microsoft Store' }
        Winget            = [ordered]@{ Name = 'Microsoft.DesktopAppInstaller'; Family = 'Microsoft.DesktopAppInstaller_8wekyb3d8bbwe' }
        WuUri             = 'ms-settings:windowsupdate'
        WuHistoryUri      = 'ms-settings:windowsupdate-history'
        WuCheckButton     = '^(Sprawdź dostępność aktualizacji|Sprawdź aktualizacje|Check for updates|Spróbuj ponownie|Retry)'
        WuBusyPattern     = '^(Sprawdzanie dostępności|Sprawdzanie aktualizacji|Checking for updates)'
        ScanTimeoutSeconds = 600
        HistoryWaitSeconds = 1500
        RestorePointTweak = 'vmtest.hklm-value'
        RestorePointDescription = 'DSO E4B1 punkt po włączeniu z okna'
    }
}

# ---------------------------------------------------------------------------------------------------------------------
# Funkcje wysyłane do VM (samowystarczalne: wołają siebie nawzajem, funkcje Explore z modułu i cmdlety systemowe).
# ---------------------------------------------------------------------------------------------------------------------

function Test-E4B1VmComputerName {
    # Nazwy komputerów VM nadawane przez New-TestVm.ps1 (VmBuild: nazwa VM wielkimi literami).
    [CmdletBinding()]
    [OutputType([bool])]
    param([AllowEmptyString()][string]$ComputerName)
    return (@('DSO-26H2-HOME', 'DSO-26H2-PRO') -contains ([string]$ComputerName).ToUpperInvariant())
}

function Assert-E4B1InsideVm {
    # Ostatni bezpiecznik: sondy E4B1 resetują sieć i Windows Update, zmieniają usługi, polityki, pakiety AppX i ochronę systemu,
    # więc działają WYŁĄCZNIE w VM DSO-26H2-Home/Pro i nigdy w testach na mockach (DSO_VM_TESTS_MOCK_ONLY).
    [CmdletBinding()]
    param([string]$ComputerName = $env:COMPUTERNAME)
    if ($env:DSO_VM_TESTS_MOCK_ONLY) {
        throw 'Tryb DSO_VM_TESTS_MOCK_ONLY: sondy E4B1 nie mogą działać w testach na mockach (nic nie zostało uruchomione).'
    }
    if (-not (Test-E4B1VmComputerName -ComputerName $ComputerName)) {
        throw "Sondy E4B1 działają tylko w VM DSO-26H2-HOME/DSO-26H2-PRO (ten komputer: '$ComputerName'); nic nie zostało uruchomione."
    }
}

function Format-E4B1Error {
    # Wyjątek -> '0xHRESULT komunikat' (błąd bazowy).
    [CmdletBinding()]
    [OutputType([string])]
    param([Parameter(Mandatory)]$ErrorRecord)
    $Exception = if ($ErrorRecord -is [System.Management.Automation.ErrorRecord]) { $ErrorRecord.Exception } else { $ErrorRecord }
    $Base = $Exception.GetBaseException()
    return ('0x{0:X8} {1}' -f $Base.HResult, $Base.Message)
}

function Get-E4B1StartTypeName {
    # Wartość Start z rejestru usług + DelayedAutostart -> nazwa typu startu jak w DSOptimizer (ServiceStartType).
    [CmdletBinding()]
    [OutputType([string])]
    param([AllowNull()]$Start, [AllowNull()]$Delayed)
    if ($null -eq $Start) { return $null }
    switch ([int]$Start) {
        0 { return 'Boot' }
        1 { return 'System' }
        2 { if ($null -ne $Delayed -and [int]$Delayed -eq 1) { return 'AutomaticDelayed' } else { return 'Automatic' } }
        3 { return 'Manual' }
        4 { return 'Disabled' }
        default { return "nieznany ($Start)" }
    }
}

function Get-E4B1NewBackupName {
    # Nazwa kopii folderu WU jak w specu 4.3: <nazwa>.dso-<rrrrMMdd-HHmmss> (eksploracja dopisuje -e4b1, żeby kopie były rozpoznawalne).
    [CmdletBinding()]
    [OutputType([string])]
    param(
        [Parameter(Mandatory)][string]$FolderName,
        [Parameter(Mandatory)][datetime]$At
    )
    return ('{0}.dso-{1}-e4b1' -f $FolderName, $At.ToString('yyyyMMdd-HHmmss', [Globalization.CultureInfo]::InvariantCulture))
}

function Test-E4B1DnsOnlyHosts {
    # Kryterium „po czyszczeniu zostają tylko wpisy z hosts”: każdy wpis pamięci DNS ma nazwę z pliku hosts albo nazwę odwrotną (PTR, in-addr.arpa)
    # adresu z hosts (bez rozróżniania wielkości liter). HostsNames = wynik Get-E4B1HostsNames (nazwy + nazwy odwrotne). Pusta pamięć też spełnia kryterium.
    [CmdletBinding()]
    [OutputType([bool])]
    param(
        [AllowNull()][AllowEmptyCollection()][object[]]$Entries,
        [AllowEmptyCollection()][string[]]$HostsNames
    )
    $Allowed = @(@($HostsNames) | Where-Object { $_ } | ForEach-Object { $_.ToLowerInvariant() })
    foreach ($Entry in @($Entries)) {
        if ($null -eq $Entry) { continue }
        $Name = ([string]$Entry.Entry).ToLowerInvariant()
        if ($Allowed -notcontains $Name) { return $false }
    }
    return $true
}

function Get-E4B1HostsNames {
    # Nazwy z pliku hosts (linie bez komentarza: adres + nazwy) i nazwy odwrotne adresów IPv4.
    [CmdletBinding()]
    [OutputType([string[]])]
    param([AllowEmptyCollection()][string[]]$Lines)
    $Names = New-Object System.Collections.Generic.List[string]
    foreach ($Line in @($Lines)) {
        $Text = ([string]$Line -replace '#.*$', '').Trim()
        if (-not $Text) { continue }
        $Parts = @($Text -split '\s+')
        for ($Index = 1; $Index -lt $Parts.Count; $Index++) { if ($Parts[$Index]) { $Names.Add($Parts[$Index]) } }
        # Klient DNS wczytuje z hosts także wpis odwrotny (PTR) adresu IPv4, np. 67.45.123.10.in-addr.arpa (pomiar VM).
        $Octets = @($Parts[0] -split '\.')
        if ($Octets.Count -eq 4 -and @($Octets | Where-Object { $_ -notmatch '^\d{1,3}\z' }).Count -eq 0) { $Names.Add((($Octets[3..0]) -join '.') + '.in-addr.arpa') }
    }
    return @($Names)
}

function Get-E4B1NativeSource {
    # P/Invoke (C# 5, Add-Type z PowerShell 5.1), wg MS Learn: usługi (OpenSCManager/OpenService/ChangeServiceConfig/ChangeServiceConfig2/ControlService),
    # Restart Manager (RmStartSession/RmRegisterResources/RmGetList/RmEndSession: kto trzyma pliki), okna (EnumWindows i pokrewne, PostMessage BM_CLICK).
    [CmdletBinding()]
    [OutputType([string])]
    param()
    return @'
using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Text;

namespace DsoE4B1
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
        private const uint ServiceNoChange = 0xFFFFFFFF;

        [StructLayout(LayoutKind.Sequential)]
        private struct ServiceDelayedAutoStartInfo { public int fDelayedAutostart; }

        [StructLayout(LayoutKind.Sequential)]
        private struct ServiceStatus
        {
            public uint dwServiceType; public uint dwCurrentState; public uint dwControlsAccepted; public uint dwWin32ExitCode;
            public uint dwServiceSpecificExitCode; public uint dwCheckPoint; public uint dwWaitHint;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct RmUniqueProcess { public int dwProcessId; public System.Runtime.InteropServices.ComTypes.FILETIME ProcessStartTime; }

        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
        private struct RmProcessInfo
        {
            public RmUniqueProcess Process;
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 256)] public string strAppName;
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 64)] public string strServiceShortName;
            public int ApplicationType;
            public uint AppStatus;
            public uint TSSessionId;
            [MarshalAs(UnmanagedType.Bool)] public bool bRestartable;
        }

        [DllImport("advapi32.dll", SetLastError = true, CharSet = CharSet.Unicode, EntryPoint = "OpenSCManagerW")]
        private static extern IntPtr OpenSCManager(string machine, string database, uint access);
        [DllImport("advapi32.dll", SetLastError = true, CharSet = CharSet.Unicode, EntryPoint = "OpenServiceW")]
        private static extern IntPtr OpenService(IntPtr manager, string name, uint access);
        [DllImport("advapi32.dll", SetLastError = true, CharSet = CharSet.Unicode, EntryPoint = "ChangeServiceConfigW")]
        private static extern bool ChangeServiceConfig(IntPtr service, uint type, uint start, uint error, string binary, string group, IntPtr tag, string dependencies, string user, string password, string display);
        [DllImport("advapi32.dll", SetLastError = true, EntryPoint = "ChangeServiceConfig2W")]
        private static extern bool ChangeServiceConfig2(IntPtr service, uint level, ref ServiceDelayedAutoStartInfo info);
        [DllImport("advapi32.dll", SetLastError = true)]
        private static extern bool ControlService(IntPtr service, uint control, ref ServiceStatus status);
        [DllImport("advapi32.dll", SetLastError = true)]
        private static extern bool CloseServiceHandle(IntPtr handle);

        [DllImport("rstrtmgr.dll", CharSet = CharSet.Unicode)]
        private static extern int RmStartSession(out uint session, int flags, StringBuilder key);
        [DllImport("rstrtmgr.dll")]
        private static extern int RmEndSession(uint session);
        [DllImport("rstrtmgr.dll", CharSet = CharSet.Unicode)]
        private static extern int RmRegisterResources(uint session, uint fileCount, string[] files, uint appCount, IntPtr apps, uint serviceCount, string[] services);
        [DllImport("rstrtmgr.dll")]
        private static extern int RmGetList(uint session, out uint needed, ref uint count, [In, Out] RmProcessInfo[] info, ref uint rebootReasons);

        private delegate bool EnumWindowsProc(IntPtr hWnd, IntPtr lParam);
        [DllImport("user32.dll")]
        private static extern bool EnumWindows(EnumWindowsProc callback, IntPtr lParam);
        [DllImport("user32.dll", CharSet = CharSet.Unicode)]
        private static extern int GetWindowText(IntPtr hWnd, StringBuilder text, int max);
        [DllImport("user32.dll", CharSet = CharSet.Unicode)]
        private static extern int GetClassName(IntPtr hWnd, StringBuilder text, int max);
        [DllImport("user32.dll")]
        private static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint processId);
        [DllImport("user32.dll")]
        private static extern bool IsWindowVisible(IntPtr hWnd);
        [DllImport("user32.dll", SetLastError = true)]
        private static extern bool PostMessage(IntPtr hWnd, uint message, IntPtr wParam, IntPtr lParam);

        // Typ startu: start = 2/3/4, delayed = -1 (bez zmiany), 0 albo 1. Wynik: 0 = sukces, inaczej kod Win32 (z krokiem w opisie).
        public static string SetStartType(string name, uint start, int delayed)
        {
            IntPtr manager = OpenSCManager(null, null, 0x0001);
            if (manager == IntPtr.Zero) { return "OpenSCManager " + Marshal.GetLastWin32Error(); }
            try
            {
                IntPtr service = OpenService(manager, name, 0x0002 | 0x0001);
                if (service == IntPtr.Zero) { return "OpenService " + Marshal.GetLastWin32Error(); }
                try
                {
                    if (!ChangeServiceConfig(service, ServiceNoChange, start, ServiceNoChange, null, null, IntPtr.Zero, null, null, null, null))
                    {
                        return "ChangeServiceConfig " + Marshal.GetLastWin32Error();
                    }
                    if (start == 2 && delayed >= 0)
                    {
                        ServiceDelayedAutoStartInfo info = new ServiceDelayedAutoStartInfo();
                        info.fDelayedAutostart = delayed;
                        if (!ChangeServiceConfig2(service, 3, ref info)) { return "ChangeServiceConfig2 " + Marshal.GetLastWin32Error(); }
                    }
                    return "0";
                }
                finally { CloseServiceHandle(service); }
            }
            finally { CloseServiceHandle(manager); }
        }

        // ControlService(SERVICE_CONTROL_STOP) jak WindowsServiceManager.StopService (bez zatrzymywania zależnych). Wynik: 0 albo kod Win32.
        public static int StopService(string name)
        {
            IntPtr manager = OpenSCManager(null, null, 0x0001);
            if (manager == IntPtr.Zero) { return Marshal.GetLastWin32Error(); }
            try
            {
                IntPtr service = OpenService(manager, name, 0x0020 | 0x0004);
                if (service == IntPtr.Zero) { return Marshal.GetLastWin32Error(); }
                try
                {
                    ServiceStatus status = new ServiceStatus();
                    if (!ControlService(service, 0x00000001, ref status)) { return Marshal.GetLastWin32Error(); }
                    return 0;
                }
                finally { CloseServiceHandle(service); }
            }
            finally { CloseServiceHandle(manager); }
        }

        // Restart Manager: procesy trzymające podane pliki. Wiersz: pid|nazwa aplikacji|usługa|typ. Pierwszy wiersz przy błędzie: "ERROR <kod>".
        public static string[] GetLockers(string[] files)
        {
            List<string> result = new List<string>();
            uint session;
            StringBuilder key = new StringBuilder(64);
            int code = RmStartSession(out session, 0, key);
            if (code != 0) { result.Add("ERROR RmStartSession " + code); return result.ToArray(); }
            try
            {
                code = RmRegisterResources(session, (uint)files.Length, files, 0, IntPtr.Zero, 0, null);
                if (code != 0) { result.Add("ERROR RmRegisterResources " + code); return result.ToArray(); }
                uint needed;
                uint count = 0;
                uint reasons = 0;
                RmProcessInfo[] info = null;
                code = RmGetList(session, out needed, ref count, null, ref reasons);
                if (code == 234)
                {
                    info = new RmProcessInfo[needed];
                    count = needed;
                    code = RmGetList(session, out needed, ref count, info, ref reasons);
                }
                if (code != 0) { result.Add("ERROR RmGetList " + code); return result.ToArray(); }
                for (int i = 0; i < count; i++)
                {
                    result.Add(info[i].Process.dwProcessId + "|" + info[i].strAppName + "|" + info[i].strServiceShortName + "|" + info[i].ApplicationType);
                }
            }
            finally { RmEndSession(session); }
            return result.ToArray();
        }

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

        // BM_CLICK (0x00F5) przez PostMessage: przycisk otwierający okno modalne nie blokuje wołającego (InvokePattern by blokował).
        public static bool Click(long handle)
        {
            return PostMessage(new IntPtr(handle), 0x00F5, IntPtr.Zero, IntPtr.Zero);
        }

        [DllImport("user32.dll")]
        private static extern bool EnumChildWindows(IntPtr parent, EnumWindowsProc callback, IntPtr lParam);
        [DllImport("user32.dll")]
        private static extern IntPtr SendMessage(IntPtr hWnd, uint message, IntPtr wParam, IntPtr lParam);

        // Pierwsze okno potomne (rekurencyjnie) o danej klasie Win32 (np. SysListView32, msctls_trackbar32); 0 = brak.
        public static long FindChildByClass(long parent, string className)
        {
            long found = 0;
            EnumChildWindows(new IntPtr(parent), delegate (IntPtr hWnd, IntPtr lParam)
            {
                StringBuilder name = new StringBuilder(256);
                GetClassName(hWnd, name, name.Capacity);
                if (string.Equals(name.ToString(), className, StringComparison.OrdinalIgnoreCase)) { found = hWnd.ToInt64(); return false; }
                return true;
            }, IntPtr.Zero);
            return found;
        }

        // Suwak Win32 (trackbar): TBM_GETPOS 0x0400, TBM_GETRANGEMIN 0x0401, TBM_GETRANGEMAX 0x0402. Wynik: pos|min|max.
        public static string TrackbarState(long handle)
        {
            IntPtr h = new IntPtr(handle);
            return SendMessage(h, 0x0400, IntPtr.Zero, IntPtr.Zero).ToInt64() + "|" + SendMessage(h, 0x0401, IntPtr.Zero, IntPtr.Zero).ToInt64() + "|" + SendMessage(h, 0x0402, IntPtr.Zero, IntPtr.Zero).ToInt64();
        }

        // Klawisz (WM_KEYDOWN 0x0100 + WM_KEYUP 0x0101) wysłany do okna przez PostMessage, np. VK_HOME (0x24) na liście dysków.
        public static bool PostKey(long handle, int virtualKey)
        {
            IntPtr h = new IntPtr(handle);
            return PostMessage(h, 0x0100, new IntPtr(virtualKey), IntPtr.Zero) && PostMessage(h, 0x0101, new IntPtr(virtualKey), IntPtr.Zero);
        }
    }
}
'@
}

function Initialize-E4B1Native {
    # Typy P/Invoke i (opcjonalnie, tylko K2) UI Automation, System.Drawing, WinForms.
    [CmdletBinding()]
    param([switch]$Ui)
    if (-not ('DsoE4B1.Native' -as [type])) { Add-Type -TypeDefinition (Get-E4B1NativeSource) -Language CSharp }
    if ($Ui) {
        Add-Type -AssemblyName System.Drawing
        Add-Type -AssemblyName System.Windows.Forms
        Add-Type -AssemblyName UIAutomationClient
        Add-Type -AssemblyName UIAutomationTypes
        # Okna Win32 (#32770, SysListView32, przyciski) bez dostawców po stronie klienta widać tylko jako Pane bez dzieci (pomiar VM): rejestracja jak w MS Learn.
        try {
            Add-Type -AssemblyName UIAutomationClientsideProviders
            [System.Windows.Automation.ClientSettings]::RegisterClientSideProviderAssembly([UIAutomationClientsideProviders.UIAutomationClientSideProviders].Assembly.GetName())
        }
        catch { $null = $_ }
    }
}

function Get-E4B1UserProfilePath {
    # Ścieżka profilu konta po SID (ProfileList), np. C:\Users\dso.
    [CmdletBinding()]
    [OutputType([string])]
    param([Parameter(Mandatory)][string]$UserSid)
    $Entry = Read-ExploreRegistryValue -Path "HKLM\SOFTWARE\Microsoft\Windows NT\CurrentVersion\ProfileList\$UserSid" -Name 'ProfileImagePath'
    if (-not $Entry.Exists) { return $null }
    return [Environment]::ExpandEnvironmentVariables([string]$Entry.Value)
}

# --- odczyty K1 (bez skutku) ---

function Get-E4B1ServiceInfo {
    # Usługa: stan, typ startu (rejestr Start + DelayedAutostart, Win32_Service), proces, zależności w obie strony, wyzwalacze (sc qtriggerinfo).
    [CmdletBinding()]
    param(
        [Parameter(Mandatory)][string]$Name,
        [switch]$Triggers
    )
    $Out = [ordered]@{ Name = $Name; Exists = $false; Status = $null; StartRaw = $null; DelayedRaw = $null; StartType = $null; Win32StartMode = $null; Win32Delayed = $null
        ProcessId = $null; ImagePath = $null; DependentServices = @(); RunningDependents = @(); RequiredServices = @(); TriggerInfo = $null; Errors = @() }
    try {
        $Service = Get-Service -Name $Name -ErrorAction Stop
        $Out.Exists = $true
        $Out.Status = [string]$Service.Status
        $Out.DependentServices = @($Service.DependentServices | ForEach-Object { "$($_.Name)=$($_.Status)" })
        $Out.RunningDependents = @($Service.DependentServices | Where-Object { $_.Status -eq 'Running' } | ForEach-Object { $_.Name })
        $Out.RequiredServices = @($Service.ServicesDependedOn | ForEach-Object { $_.Name })
    }
    catch { $Out.Errors += "Get-Service: $(Format-E4B1Error $_)" }
    $Key = "HKLM\SYSTEM\CurrentControlSet\Services\$Name"
    $Start = Read-ExploreRegistryValue -Path $Key -Name 'Start'
    $Delayed = Read-ExploreRegistryValue -Path $Key -Name 'DelayedAutostart'
    $Image = Read-ExploreRegistryValue -Path $Key -Name 'ImagePath'
    if ($Start.Exists) { $Out.StartRaw = $Start.Value }
    if ($Delayed.Exists) { $Out.DelayedRaw = $Delayed.Value }
    if ($Image.Exists) { $Out.ImagePath = [string]$Image.Value }
    $Out.StartType = Get-E4B1StartTypeName -Start $Out.StartRaw -Delayed $Out.DelayedRaw
    try {
        $Win32 = Get-CimInstance -ClassName Win32_Service -Filter "Name='$Name'" -ErrorAction Stop | Select-Object -First 1
        if ($null -ne $Win32) {
            $Out.Win32StartMode = [string]$Win32.StartMode
            $Out.ProcessId = $Win32.ProcessId
            if ($Win32.CimInstanceProperties['DelayedAutoStart']) { $Out.Win32Delayed = $Win32.DelayedAutoStart }
        }
    }
    catch { $Out.Errors += "Win32_Service: $(Format-E4B1Error $_)" }
    if ($Triggers) {
        $Sc = Invoke-ExploreNative -FilePath (Join-Path $env:SystemRoot 'System32\sc.exe') -Arguments @('qtriggerinfo', $Name) -TimeoutSeconds 30
        $Out.TriggerInfo = [string]$Sc.Stdout
    }
    return [pscustomobject]$Out
}

function Get-E4B1FolderInfo {
    # Folder WU: istnienie, liczba elementów najwyższego poziomu, DataStore.edb (rozmiar), kopie <nazwa>.dso-* obok.
    [CmdletBinding()]
    param([Parameter(Mandatory)][string]$Path)
    $Out = [ordered]@{ Path = $Path; Exists = $false; TopItems = @(); TopCount = $null; FileCount = $null; DataStoreBytes = $null; Siblings = @(); Error = $null }
    try {
        $Out.Exists = Test-Path -LiteralPath $Path
        if ($Out.Exists) {
            $Top = @(Get-ChildItem -LiteralPath $Path -Force -ErrorAction Stop)
            $Out.TopItems = @($Top | Select-Object -First 40 | ForEach-Object { $_.Name })
            $Out.TopCount = $Top.Count
            $Out.FileCount = @(Get-ChildItem -LiteralPath $Path -Recurse -File -Force -ErrorAction SilentlyContinue).Count
            $Edb = Join-Path $Path 'DataStore\DataStore.edb'
            if (Test-Path -LiteralPath $Edb) { $Out.DataStoreBytes = (Get-Item -LiteralPath $Edb -Force).Length }
        }
        $Parent = Split-Path -Parent $Path
        $Leaf = Split-Path -Leaf $Path
        $Out.Siblings = @(Get-ChildItem -LiteralPath $Parent -Directory -Force -Filter "$Leaf.dso-*" -ErrorAction SilentlyContinue | ForEach-Object { $_.Name })
    }
    catch { $Out.Error = Format-E4B1Error $_ }
    return [pscustomobject]$Out
}

function Get-E4B1FolderLockers {
    # Restart Manager na plikach folderu (najwyżej 400): kto trzyma pliki. Bez skutku (tylko odczyt).
    [CmdletBinding()]
    param([Parameter(Mandatory)][string]$Path)
    $Out = [ordered]@{ Path = $Path; Files = 0; Lockers = @(); Error = $null }
    try {
        if (-not (Test-Path -LiteralPath $Path)) { $Out.Error = 'brak folderu'; return [pscustomobject]$Out }
        $Files = @(Get-ChildItem -LiteralPath $Path -Recurse -File -Force -ErrorAction SilentlyContinue | Select-Object -First 400 | ForEach-Object { $_.FullName })
        $Out.Files = $Files.Count
        if ($Files.Count -gt 0) {
            Initialize-E4B1Native
            $Out.Lockers = @([DsoE4B1.Native]::GetLockers([string[]]$Files))
        }
    }
    catch { $Out.Error = Format-E4B1Error $_ }
    return [pscustomobject]$Out
}

function Get-E4B1DnsCache {
    # Pamięć DNS klienta (Get-DnsClientCache): wpisy z nazwą, typem, stanem, sekcją, TTL i danymi.
    [CmdletBinding()]
    param()
    $Out = [ordered]@{ Count = $null; Entries = @(); Error = $null }
    try {
        $Entries = @(Get-DnsClientCache -ErrorAction Stop)
        $Out.Count = $Entries.Count
        $Out.Entries = @($Entries | ForEach-Object { [pscustomobject]@{ Entry = [string]$_.Entry; Name = [string]$_.Name; Type = [string]$_.Type; Status = [string]$_.Status; Section = [string]$_.Section; TimeToLive = $_.TimeToLive; Data = [string]$_.Data } })
    }
    catch { $Out.Error = Format-E4B1Error $_ }
    return [pscustomobject]$Out
}

function Get-E4B1IpConfig {
    # Konfiguracja IPv4 kart: DHCP, adresy, brama, DNS, klucz Tcpip\Parameters\Interfaces\{GUID}.
    [CmdletBinding()]
    param()
    $Out = @()
    try {
        foreach ($Adapter in @(Get-NetAdapter -ErrorAction Stop)) {
            $Item = [ordered]@{ Name = [string]$Adapter.Name; InterfaceIndex = $Adapter.ifIndex; Status = [string]$Adapter.Status; Guid = [string]$Adapter.InterfaceGuid
                Dhcp = $null; Addresses = @(); Gateways = @(); DnsServers = @(); TcpipKey = $null; Errors = @() }
            try { $Item.Dhcp = [string](Get-NetIPInterface -InterfaceIndex $Adapter.ifIndex -AddressFamily IPv4 -ErrorAction Stop).Dhcp } catch { $Item.Errors += "Dhcp: $(Format-E4B1Error $_)" }
            try { $Item.Addresses = @(Get-NetIPAddress -InterfaceIndex $Adapter.ifIndex -AddressFamily IPv4 -ErrorAction Stop | ForEach-Object { "$($_.IPAddress)/$($_.PrefixLength) $($_.PrefixOrigin) $($_.AddressState)" }) } catch { $Item.Errors += "IP: $(Format-E4B1Error $_)" }
            try { $Item.Gateways = @(Get-NetRoute -InterfaceIndex $Adapter.ifIndex -DestinationPrefix '0.0.0.0/0' -ErrorAction Stop | ForEach-Object { "$($_.NextHop) ($($_.Store))" }) } catch { $Item.Errors += "brama: $(Format-E4B1Error $_)" }
            try { $Item.DnsServers = @((Get-DnsClientServerAddress -InterfaceIndex $Adapter.ifIndex -AddressFamily IPv4 -ErrorAction Stop).ServerAddresses) } catch { $Item.Errors += "DNS: $(Format-E4B1Error $_)" }
            try { $Item.TcpipKey = Read-ExploreRegistryKey -Path "HKLM\SYSTEM\CurrentControlSet\Services\Tcpip\Parameters\Interfaces\$($Adapter.InterfaceGuid)" } catch { $Item.Errors += "rejestr: $(Format-E4B1Error $_)" }
            $Out += [pscustomobject]$Item
        }
    }
    catch { $Out += [pscustomobject]@{ Name = 'BLAD'; Errors = @(Format-E4B1Error $_) } }
    return $Out
}

function Get-E4B1PackageState {
    # Pakiet AppX: dla konta (-User <SID>), w obrazie (provisioned), u wszystkich kont (PackageUserInformation), wersja, lokalizacja, manifest na dysku.
    [CmdletBinding()]
    param(
        [Parameter(Mandatory)][string]$UserSid,
        [Parameter(Mandatory)][string]$Name
    )
    $Out = [ordered]@{ Name = $Name; ForUser = @(); Provisioned = @(); Packages = @(); UserInstallState = $null; Errors = @() }
    try { $Out.ForUser = @(Get-AppxPackage -User $UserSid -Name $Name -ErrorAction Stop | ForEach-Object { "$($_.PackageFullName) Status=$($_.Status)" }) } catch { $Out.Errors += "dso: $(Format-E4B1Error $_)" }
    try { $Out.Provisioned = @(Get-AppxProvisionedPackage -Online -ErrorAction Stop | Where-Object { $_.DisplayName -eq $Name } | ForEach-Object { "$($_.PackageName) $($_.Version)" }) } catch { $Out.Errors += "obraz: $(Format-E4B1Error $_)" }
    try {
        foreach ($Package in @(Get-AppxPackage -AllUsers -Name $Name -ErrorAction Stop)) {
            $Manifest = Join-Path ([string]$Package.InstallLocation) 'AppxManifest.xml'
            $Users = @($Package.PackageUserInformation | ForEach-Object { [pscustomobject]@{ Sid = [string]$_.UserSecurityId.Sid; User = [string]$_.UserSecurityId.Username; InstallState = [string]$_.InstallState } })
            $Own = @($Users | Where-Object { $_.Sid -eq $UserSid }) | Select-Object -First 1
            if ($null -ne $Own) { $Out.UserInstallState = $Own.InstallState }
            $Out.Packages += [pscustomobject]@{ PackageFullName = [string]$Package.PackageFullName; Version = [string]$Package.Version; Status = [string]$Package.Status
                InstallLocation = [string]$Package.InstallLocation; ManifestExists = (Test-Path -LiteralPath $Manifest); Users = $Users }
        }
    }
    catch { $Out.Errors += "wszyscy: $(Format-E4B1Error $_)" }
    return [pscustomobject]$Out
}

function Get-E4B1WingetAlias {
    # Alias winget w profilu konta: %LOCALAPPDATA%\Microsoft\WindowsApps\winget.exe (istnienie, atrybuty, rozmiar).
    [CmdletBinding()]
    param([Parameter(Mandatory)][string]$UserSid)
    $Out = [ordered]@{ Path = $null; Exists = $false; Attributes = $null; Length = $null; Error = $null }
    try {
        $Profile = Get-E4B1UserProfilePath -UserSid $UserSid
        if (-not $Profile) { $Out.Error = 'brak profilu w ProfileList'; return [pscustomobject]$Out }
        $Out.Path = Join-Path $Profile 'AppData\Local\Microsoft\WindowsApps\winget.exe'
        $Item = Get-Item -LiteralPath $Out.Path -Force -ErrorAction SilentlyContinue
        if ($null -ne $Item) { $Out.Exists = $true; $Out.Attributes = [string]$Item.Attributes; $Out.Length = $Item.Length }
    }
    catch { $Out.Error = Format-E4B1Error $_ }
    return [pscustomobject]$Out
}

function Get-E4B1Protection {
    # Ochrona systemu C:: SPP\Clients (GUID woluminu C:), SystemRestoreConfig (DiskPercent), punkty (Get-ComputerRestorePoint i WMI),
    # Win32_ShadowStorage, vssadmin list shadowstorage. Bezpiecznik, bo uruchamia program (vssadmin).
    [CmdletBinding()]
    param()
    Assert-E4B1InsideVm
    $Out = [ordered]@{ VolumeId = $null; SppEntries = @(); SppProtected = $null; SrConfig = $null; Points = @(); PointsError = $null; ShadowStorage = @(); ShadowStorageError = $null
        Vssadmin = $null; ShadowCopies = @(); VolumeSizeBytes = $null; CreationFrequency = $null; Errors = @() }
    try {
        $Volume = Get-CimInstance -ClassName Win32_Volume -Filter "DriveLetter='C:'" -ErrorAction Stop | Select-Object -First 1
        $Out.VolumeId = [string]$Volume.DeviceID
        $Out.VolumeSizeBytes = $Volume.Capacity
    }
    catch { $Out.Errors += "Win32_Volume: $(Format-E4B1Error $_)" }
    try {
        $Spp = Read-ExploreRegistryValue -Path 'HKLM\SOFTWARE\Microsoft\Windows NT\CurrentVersion\SPP\Clients' -Name '{09F7EDC5-294E-4180-AF6A-FB0E6A0E9513}'
        if ($Spp.Exists) { $Out.SppEntries = @($Spp.Value) }
        $Guid = if ($Out.VolumeId) { ([regex]::Match($Out.VolumeId, '\{[0-9A-Fa-f-]+\}')).Value } else { '' }
        $Out.SppProtected = [bool]($Guid -and (@($Out.SppEntries | Where-Object { $_ -like "*$Guid*" }).Count -gt 0))
    }
    catch { $Out.Errors += "SPP: $(Format-E4B1Error $_)" }
    $Out.CreationFrequency = Read-ExploreRegistryValue -Path 'HKLM\SOFTWARE\Microsoft\Windows NT\CurrentVersion\SystemRestore' -Name 'SystemRestorePointCreationFrequency'
    try {
        $Config = Get-CimInstance -Namespace 'root/default' -ClassName SystemRestoreConfig -ErrorAction Stop | Select-Object -First 1
        if ($null -ne $Config) { $Out.SrConfig = [ordered]@{ RPSessionInterval = $Config.RPSessionInterval; DiskPercent = $Config.DiskPercent; RPGlobalInterval = $Config.RPGlobalInterval; RPLifeInterval = $Config.RPLifeInterval } }
    }
    catch { $Out.Errors += "SystemRestoreConfig: $(Format-E4B1Error $_)" }
    try { $Out.Points = @(Get-ComputerRestorePoint -ErrorAction Stop | ForEach-Object { "$($_.SequenceNumber) $($_.Description) $($_.CreationTime)" }) }
    catch { $Out.PointsError = Format-E4B1Error $_ }
    try {
        $Out.ShadowStorage = @(Get-CimInstance -ClassName Win32_ShadowStorage -ErrorAction Stop | ForEach-Object {
                [pscustomobject]@{ Volume = [string]$_.Volume.DeviceID; MaxSpace = $_.MaxSpace; UsedSpace = $_.UsedSpace; AllocatedSpace = $_.AllocatedSpace }
            })
    }
    catch { $Out.ShadowStorageError = Format-E4B1Error $_ }
    $Out.Vssadmin = Invoke-ExploreNative -FilePath (Join-Path $env:SystemRoot 'System32\vssadmin.exe') -Arguments @('list', 'shadowstorage') -TimeoutSeconds 60
    $Shadows = Invoke-ExploreNative -FilePath (Join-Path $env:SystemRoot 'System32\vssadmin.exe') -Arguments @('list', 'shadows') -TimeoutSeconds 60
    $Out.ShadowCopies = @(([string]$Shadows.Stdout -split "`r?`n") | Where-Object { $_ -match 'Shadow Copy ID|Identyfikator kopii|creation time|czas utworzenia|No items|Nie znaleziono' } | ForEach-Object { $_.Trim() })
    return [pscustomobject]$Out
}

function Get-E4B1WuHistoryCount {
    # Historia Windows Update przez COM Microsoft.Update.Session (IUpdateSearcher.GetTotalHistoryCount i ostatnie wpisy); tylko odczyt.
    [CmdletBinding()]
    param()
    $Out = [ordered]@{ Count = $null; Titles = @(); Error = $null }
    try {
        $Searcher = (New-Object -ComObject 'Microsoft.Update.Session').CreateUpdateSearcher()
        $Out.Count = [int]$Searcher.GetTotalHistoryCount()
        if ($Out.Count -gt 0) { $Out.Titles = @($Searcher.QueryHistory(0, [Math]::Min($Out.Count, 10)) | ForEach-Object { "$($_.Date.ToString('o')) rc=$($_.ResultCode) $($_.Title)" }) }
    }
    catch { $Out.Error = Format-E4B1Error $_ }
    return [pscustomobject]$Out
}

function Get-E4B1PolicyState {
    # Drzewo klucza polityk WU (wartości i podklucze).
    [CmdletBinding()]
    param([Parameter(Mandatory)][string]$Key)
    try { return @(Read-ExploreRegistryTree -Path $Key) } catch { return @([pscustomobject]@{ Path = $Key; Error = (Format-E4B1Error $_) }) }
}

# --- K2: operacje w sesji dso (kopiowane do skryptu zadania) ---

function Save-E4B1Shot {
    # Zrzut pulpitu sesji dso do C:\dso\e4b1\shots\<Label>.png; zwraca nazwę pliku.
    [CmdletBinding()]
    param([Parameter(Mandatory)][ValidatePattern('^[a-z0-9-]+\z')][string]$Label)
    Assert-E4B1InsideVm
    $Directory = 'C:\dso\e4b1\shots'
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

function Get-E4B1Elements {
    # Elementy okna (UI Automation, potomkowie): typ, nazwa, aktywność; unikalne pary typ+nazwa, najwyżej 400.
    [CmdletBinding()]
    param([Parameter(Mandatory)][long]$Handle)
    $Root = [System.Windows.Automation.AutomationElement]::FromHandle([IntPtr]$Handle)
    $All = $Root.FindAll([System.Windows.Automation.TreeScope]::Descendants, [System.Windows.Automation.Condition]::TrueCondition)
    $Seen = New-Object System.Collections.Generic.HashSet[string]
    $Out = @()
    foreach ($Element in $All) {
        $Current = $Element.Current
        $Name = [string]$Current.Name
        if (-not $Name) { continue }
        $Type = ([string]$Current.ControlType.ProgrammaticName) -replace '^ControlType\.', ''
        if (-not $Seen.Add("$Type|$Name")) { continue }
        $Out += [pscustomobject]@{ Type = $Type; Name = $Name; Enabled = [bool]$Current.IsEnabled }
        if ($Out.Count -ge 400) { break }
    }
    return $Out
}

function Find-E4B1Element {
    # Pierwszy potomek okna, którego nazwa pasuje do wzorca (regex, bez rozróżniania wielkości liter); z Type najpierw tylko danego typu,
    # a gdy takiego nie ma - pierwszy pasujący po nazwie dowolnego typu (okna Win32 bez dostawców UIA mają typ Pane).
    [CmdletBinding()]
    param(
        [Parameter(Mandatory)][long]$Handle,
        [Parameter(Mandatory)][string]$Pattern,
        [string]$Type
    )
    $Root = [System.Windows.Automation.AutomationElement]::FromHandle([IntPtr]$Handle)
    $All = $Root.FindAll([System.Windows.Automation.TreeScope]::Descendants, [System.Windows.Automation.Condition]::TrueCondition)
    $Loose = $null
    foreach ($Element in $All) {
        $Name = [string]$Element.Current.Name
        if (-not $Name -or $Name -notmatch $Pattern) { continue }
        if ($Type -and (([string]$Element.Current.ControlType.ProgrammaticName) -replace '^ControlType\.', '') -ne $Type) { if ($null -eq $Loose) { $Loose = $Element }; continue }
        return $Element
    }
    return $Loose
}

function Find-E4B1Window {
    # Widoczne okno najwyższego poziomu: klasa i tytuł (wildcard); opcjonalnie tylko procesu o podanym Id.
    [CmdletBinding()]
    param(
        [Parameter(Mandatory)][string]$ClassName,
        [Parameter(Mandatory)][string[]]$TitleLike,
        [int]$ProcessId = 0
    )
    foreach ($Window in @([DsoE4B1.Native]::ListTopLevelWindows())) {
        if (-not $Window.Visible -or $Window.ClassName -ne $ClassName) { continue }
        if ($ProcessId -gt 0 -and [int]$Window.ProcessId -ne $ProcessId) { continue }
        foreach ($Like in $TitleLike) { if ($Window.Title -like $Like) { return $Window } }
    }
    return $null
}

function Wait-E4B1Window {
    # Czeka (najwyżej Seconds) na okno z Find-E4B1Window.
    [CmdletBinding()]
    param(
        [Parameter(Mandatory)][string]$ClassName,
        [Parameter(Mandatory)][string[]]$TitleLike,
        [int]$ProcessId = 0,
        [ValidateRange(1, 300)][int]$Seconds = 30
    )
    $Watch = [System.Diagnostics.Stopwatch]::StartNew()
    while ($Watch.ElapsedMilliseconds -lt ($Seconds * 1000)) {
        $Window = Find-E4B1Window -ClassName $ClassName -TitleLike $TitleLike -ProcessId $ProcessId
        if ($null -ne $Window) { return $Window }
        Start-Sleep -Milliseconds 250
    }
    return $null
}

function Stop-E4B1ProcessByName {
    # Zamknięcie procesów o danej nazwie (Ustawienia, Sklep, okno Ochrony systemu) między pomiarami; czeka do 10 s.
    [CmdletBinding()]
    param([Parameter(Mandatory)][string[]]$Name)
    Assert-E4B1InsideVm
    $Processes = @(Get-Process -Name $Name -ErrorAction SilentlyContinue)
    foreach ($Process in $Processes) { try { $Process.Kill() } catch { $null = $_ } }
    foreach ($Process in $Processes) { try { $null = $Process.WaitForExit(10000) } catch { $null = $_ } }
    return $Processes.Count
}

function Start-E4B1Explorer {
    # %SystemRoot%\explorer.exe "<argument>" przez CreateProcess (UseShellExecute=false), jak ShellUrlOpener DSOptimizera.
    [CmdletBinding()]
    param([Parameter(Mandatory)][ValidatePattern('^(ms-settings:[a-z-]+|shell:AppsFolder\\[A-Za-z0-9._!-]+)\z')][string]$Argument)
    Assert-E4B1InsideVm
    $Psi = New-Object System.Diagnostics.ProcessStartInfo
    $Psi.FileName = Join-Path $env:SystemRoot 'explorer.exe'
    $Psi.Arguments = '"' + $Argument + '"'
    $Psi.UseShellExecute = $false
    return [System.Diagnostics.Process]::Start($Psi)
}

function Invoke-E4B1SettingsPage {
    # Ustawienia (konto dso): otwarcie adresu, okno, elementy (UI Automation), opcjonalnie klik przycisku (ClickPattern) i czekanie, aż zniknie
    # tekst „w toku” (BusyPattern) albo minie ScanSeconds; zrzuty; na końcu zamknięcie Ustawień.
    [CmdletBinding()]
    param(
        [Parameter(Mandatory)][ValidatePattern('^ms-settings:[a-z-]+\z')][string]$Uri,
        [Parameter(Mandatory)][ValidatePattern('^[a-z0-9-]+\z')][string]$Label,
        [string]$ClickPattern,
        [string]$BusyPattern,
        [ValidateRange(0, 1800)][int]$ScanSeconds = 0
    )
    Assert-E4B1InsideVm
    $Out = [ordered]@{ Op = 'Settings'; Uri = $Uri; Label = $Label; WindowMs = $null; Title = $null; Elements = @(); ManagedNotice = $false; CheckButton = $null
        Clicked = $false; ClickError = $null; Polls = @(); ScanMs = $null; ScanEndedByTimeout = $null; ElementsAfter = @(); Shot = $null; ShotAfter = $null; Error = $null }
    try {
        $null = Stop-E4B1ProcessByName -Name 'SystemSettings'
        Start-Sleep -Seconds 2
        $Watch = [System.Diagnostics.Stopwatch]::StartNew()
        $null = Start-E4B1Explorer -Argument $Uri
        $Window = Wait-E4B1Window -ClassName 'ApplicationFrameWindow' -TitleLike @('Ustawienia*', 'Settings*') -Seconds 40
        if ($null -eq $Window) { throw 'brak okna Ustawień po 40 s' }
        $Out.WindowMs = [int]$Watch.ElapsedMilliseconds
        $Out.Title = $Window.Title
        Start-Sleep -Seconds 6
        $Out.Elements = @(Get-E4B1Elements -Handle $Window.Handle)
        $Out.ManagedNotice = (@($Out.Elements | Where-Object { $_.Name -match 'organizac|organization' }).Count -gt 0)
        if ($ClickPattern) {
            $Button = @($Out.Elements | Where-Object { $_.Type -eq 'Button' -and $_.Name -match $ClickPattern }) | Select-Object -First 1
            if ($null -ne $Button) { $Out.CheckButton = [pscustomobject]@{ Name = $Button.Name; Enabled = $Button.Enabled } }
        }
        $Out.Shot = Save-E4B1Shot -Label "$Label"
        if ($ClickPattern -and $ScanSeconds -gt 0 -and $null -ne $Out.CheckButton -and $Out.CheckButton.Enabled) {
            try {
                $Element = Find-E4B1Element -Handle $Window.Handle -Pattern $ClickPattern -Type 'Button'
                $Invoke = $Element.GetCurrentPattern([System.Windows.Automation.InvokePattern]::Pattern)
                $Invoke.Invoke()
                $Out.Clicked = $true
            }
            catch { $Out.ClickError = Format-E4B1Error $_ }
            if ($Out.Clicked) {
                $Scan = [System.Diagnostics.Stopwatch]::StartNew()
                $Quiet = 0
                $Out.ScanEndedByTimeout = $true
                while ($Scan.ElapsedMilliseconds -lt ($ScanSeconds * 1000)) {
                    Start-Sleep -Seconds 5
                    $Names = @()
                    try { $Names = @(Get-E4B1Elements -Handle $Window.Handle | Where-Object { $_.Type -eq 'Text' } | ForEach-Object { $_.Name }) } catch { $Names = @("UIA: $(Format-E4B1Error $_)") }
                    $Busy = @($Names | Where-Object { $BusyPattern -and $_ -match $BusyPattern }).Count -gt 0
                    if (@($Out.Polls).Count -lt 60) { $Out.Polls += [pscustomobject]@{ Ms = [int]$Scan.ElapsedMilliseconds; Busy = $Busy; Texts = @($Names | Select-Object -First 12) } }
                    if ($Busy) { $Quiet = 0 } else { $Quiet++ }
                    if ($Scan.ElapsedMilliseconds -ge 20000 -and $Quiet -ge 2) { $Out.ScanEndedByTimeout = $false; break }
                }
                $Out.ScanMs = [int]$Scan.ElapsedMilliseconds
                $Out.ElementsAfter = @(Get-E4B1Elements -Handle $Window.Handle)
                $Out.ShotAfter = Save-E4B1Shot -Label "$Label-po"
            }
        }
    }
    catch { $Out.Error = Format-E4B1Error $_ }
    finally { try { $null = Stop-E4B1ProcessByName -Name 'SystemSettings' } catch { $null = $_ } }
    return [pscustomobject]$Out
}

function Invoke-E4B1StoreStart {
    # Start Sklepu jako dso: explorer.exe shell:AppsFolder\<AUMID>, czekanie na proces i okno, elementy, zrzut, zamknięcie.
    [CmdletBinding()]
    param(
        [Parameter(Mandatory)][string]$Aumid,
        [Parameter(Mandatory)][string]$ProcessName,
        [Parameter(Mandatory)][string]$WindowTitle,
        [Parameter(Mandatory)][ValidatePattern('^[a-z0-9-]+\z')][string]$Label
    )
    Assert-E4B1InsideVm
    $Out = [ordered]@{ Op = 'StoreStart'; Label = $Label; ProcessMs = $null; ProcessId = $null; WindowMs = $null; Window = $null; Elements = @(); Shot = $null; Closed = $null; Error = $null }
    try {
        $null = Stop-E4B1ProcessByName -Name $ProcessName
        Start-Sleep -Seconds 2
        $Watch = [System.Diagnostics.Stopwatch]::StartNew()
        $null = Start-E4B1Explorer -Argument "shell:AppsFolder\$Aumid"
        while ($Watch.ElapsedMilliseconds -lt 60000) {
            if ($null -eq $Out.ProcessId) {
                $Process = Get-Process -Name $ProcessName -ErrorAction SilentlyContinue | Select-Object -First 1
                if ($null -ne $Process) { $Out.ProcessId = $Process.Id; $Out.ProcessMs = [int]$Watch.ElapsedMilliseconds }
            }
            $Window = Find-E4B1Window -ClassName 'ApplicationFrameWindow' -TitleLike @("$WindowTitle*")
            if ($null -ne $Window -and $null -ne $Out.ProcessId) {
                $Out.WindowMs = [int]$Watch.ElapsedMilliseconds
                $Out.Window = [pscustomobject]@{ Handle = $Window.Handle; Title = $Window.Title; ProcessId = $Window.ProcessId }
                break
            }
            Start-Sleep -Milliseconds 300
        }
        Start-Sleep -Seconds 8
        if ($null -ne $Out.Window) { try { $Out.Elements = @(Get-E4B1Elements -Handle ([long]$Out.Window.Handle) | Select-Object -First 60) } catch { $Out.Error = "UIA: $(Format-E4B1Error $_)" } }
        $Out.Shot = Save-E4B1Shot -Label $Label
    }
    catch { $Out.Error = (@($Out.Error, (Format-E4B1Error $_)) | Where-Object { $_ }) -join ' | ' }
    finally { try { $Out.Closed = Stop-E4B1ProcessByName -Name $ProcessName } catch { $null = $_ } }
    return [pscustomobject]$Out
}

function Invoke-E4B1Appx {
    # Operacje AppX jako dso: Remove (Remove-AppxPackage -Package <pełna nazwa konta>), RegisterManifest (Add-AppxPackage -Register <manifest>
    # -DisableDevelopmentMode), RegisterFamily (Add-AppxPackage -RegisterByFamilyName -MainPackage <rodzina>), RemoveAlias (usunięcie aliasu
    # %LOCALAPPDATA%\Microsoft\WindowsApps\<Target> konta - stan „winget nie jest rozpoznawany”). Czas i błąd.
    [CmdletBinding()]
    param(
        [Parameter(Mandatory)][ValidateSet('Remove', 'RegisterManifest', 'RegisterFamily', 'RemoveAlias')][string]$Action,
        [Parameter(Mandatory)][string]$Target
    )
    Assert-E4B1InsideVm
    $Out = [ordered]@{ Op = 'Appx'; Action = $Action; Target = $Target; Command = $null; PackageFullName = $null; DurationMs = $null; Error = $null }
    $Watch = [System.Diagnostics.Stopwatch]::StartNew()
    try {
        switch ($Action) {
            'Remove' {
                $Package = @(Get-AppxPackage -Name $Target -ErrorAction Stop) | Select-Object -First 1
                if ($null -eq $Package) { throw "brak pakietu $Target dla konta procesu" }
                $Out.PackageFullName = [string]$Package.PackageFullName
                $Out.Command = "Remove-AppxPackage -Package $($Package.PackageFullName)"
                Remove-AppxPackage -Package $Package.PackageFullName -ErrorAction Stop
            }
            'RegisterManifest' {
                $Out.Command = "Add-AppxPackage -Register '$Target' -DisableDevelopmentMode"
                Add-AppxPackage -Register $Target -DisableDevelopmentMode -ErrorAction Stop
            }
            'RegisterFamily' {
                $Out.Command = "Add-AppxPackage -RegisterByFamilyName -MainPackage $Target"
                Add-AppxPackage -RegisterByFamilyName -MainPackage $Target -ErrorAction Stop
            }
            'RemoveAlias' {
                if ($Target -notmatch '^[a-z]+\.exe\z') { throw "nieprawidłowa nazwa aliasu '$Target'" }
                $Alias = Join-Path $env:LOCALAPPDATA "Microsoft\WindowsApps\$Target"
                $Out.Command = "Remove-Item -LiteralPath '$Alias' -Force"
                Remove-Item -LiteralPath $Alias -Force -ErrorAction Stop
            }
        }
    }
    catch { $Out.Error = Format-E4B1Error $_ }
    $Out.DurationMs = [int]$Watch.ElapsedMilliseconds
    return [pscustomobject]$Out
}

function Invoke-E4B1Winget {
    # winget z aliasu konta (%LOCALAPPDATA%\Microsoft\WindowsApps\winget.exe) w procesie podniesionym dso: kod wyjścia, wyjście, czas.
    [CmdletBinding()]
    param([Parameter(Mandatory)][ValidateSet('version', 'source-list', 'info', 'search')][string]$Command)
    Assert-E4B1InsideVm
    $Arguments = switch ($Command) {
        'version' { @('--version') }
        'source-list' { @('source', 'list') }
        'info' { @('--info') }
        # Połączenie ze źródłem winget (sieć): wyszukanie znanego pakietu bez pytań.
        'search' { @('search', '--id', 'Microsoft.PowerToys', '--source', 'winget', '--accept-source-agreements', '--disable-interactivity') }
    }
    $Path = Join-Path $env:LOCALAPPDATA 'Microsoft\WindowsApps\winget.exe'
    $Out = [ordered]@{ Op = 'Winget'; Command = $Command; Path = $Path; PathExists = (Test-Path -LiteralPath $Path); ExitCode = $null; Stdout = $null; Stderr = $null; Error = $null; TimedOut = $null; DurationMs = $null }
    $Watch = [System.Diagnostics.Stopwatch]::StartNew()
    $Result = Invoke-ExploreNative -FilePath $Path -Arguments $Arguments -TimeoutSeconds 120
    $Out.DurationMs = [int]$Watch.ElapsedMilliseconds
    $Out.ExitCode = $Result.ExitCode
    $Out.Stdout = $Result.Stdout
    $Out.Stderr = $Result.Stderr
    $Out.Error = $Result.Error
    $Out.TimedOut = $Result.TimedOut
    return [pscustomobject]$Out
}

function Invoke-E4B1ProtectionUi {
    # Okno „Ochrona systemu” (SystemPropertiesProtection.exe) jako dso, przez UI Automation: zaznaczenie dysku C:, „Konfiguruj…”, „Włącz ochronę systemu”,
    # odczyt suwaka i tekstów miejsca, OK w obu oknach (BM_CLICK przez PostMessage). Każdy krok z elementami okna; zrzuty.
    [CmdletBinding()]
    param()
    Assert-E4B1InsideVm
    $Out = [ordered]@{ Op = 'ProtectionUi'; Steps = @(); Slider = $null; DialogElements = @(); MainElements = @(); Shots = @(); Done = $false; Error = $null }
    $Process = $null
    try {
        $null = Stop-E4B1ProcessByName -Name 'SystemPropertiesProtection'
        $Process = [System.Diagnostics.Process]::Start((Join-Path $env:SystemRoot 'System32\SystemPropertiesProtection.exe'))
        $Main = Wait-E4B1Window -ClassName '#32770' -TitleLike @('Właściwości systemu*', 'System Properties*') -ProcessId $Process.Id -Seconds 30
        if ($null -eq $Main) { throw 'brak okna Właściwości systemu' }
        $Out.Steps += "okno główne: $($Main.Title)"
        Start-Sleep -Seconds 3
        $Out.MainElements = @(Get-E4B1Elements -Handle $Main.Handle)
        $Drive = Find-E4B1Element -Handle $Main.Handle -Pattern '\(C:\)' -Type 'ListItem'
        if ($null -ne $Drive) {
            $Out.Steps += "pozycja dysku: $($Drive.Current.Name) ($($Drive.Current.ControlType.ProgrammaticName))"
            $Drive.GetCurrentPattern([System.Windows.Automation.SelectionItemPattern]::Pattern).Select()
            $Out.Steps += "zaznaczono: $($Drive.Current.Name)"
        }
        else {
            # Pomiar Home: UIA widzi okno Win32 tylko jako Pane bez pozycji listy; lista SysListView32 dostaje VK_HOME (pierwszy dysk = C: na VM).
            $List = [DsoE4B1.Native]::FindChildByClass([long]$Main.Handle, 'SysListView32')
            if ($List -eq 0) { throw 'brak pozycji dysku (C:) na liście i brak SysListView32' }
            $Out.Steps += "lista Win32: VK_HOME=$([DsoE4B1.Native]::PostKey($List, 0x24))"
        }
        Start-Sleep -Seconds 1
        $Configure = Find-E4B1Element -Handle $Main.Handle -Pattern '^(Konfiguruj|Configure)' -Type 'Button'
        if ($null -eq $Configure) { throw 'brak przycisku Konfiguruj' }
        $null = [DsoE4B1.Native]::Click([long]$Configure.Current.NativeWindowHandle)
        $Out.Steps += "klik: $($Configure.Current.Name)"
        $Dialog = Wait-E4B1Window -ClassName '#32770' -TitleLike @('Ochrona systemu*', 'System Protection*') -ProcessId $Process.Id -Seconds 30
        if ($null -eq $Dialog) { throw 'brak okna konfiguracji ochrony' }
        $Out.Steps += "okno konfiguracji: $($Dialog.Title)"
        Start-Sleep -Seconds 2
        $Out.Shots += Save-E4B1Shot -Label 'ochrona-okno-przed'
        $Radio = Find-E4B1Element -Handle $Dialog.Handle -Pattern '^(Włącz ochronę systemu|Turn on system protection)' -Type 'RadioButton'
        if ($null -eq $Radio) { throw 'brak opcji Włącz ochronę systemu' }
        $null = [DsoE4B1.Native]::Click([long]$Radio.Current.NativeWindowHandle)
        $Out.Steps += "klik: $($Radio.Current.Name)"
        Start-Sleep -Seconds 2
        $Root = [System.Windows.Automation.AutomationElement]::FromHandle([IntPtr]$Dialog.Handle)
        $SliderCondition = New-Object System.Windows.Automation.PropertyCondition([System.Windows.Automation.AutomationElement]::ControlTypeProperty, [System.Windows.Automation.ControlType]::Slider)
        $Slider = $Root.FindFirst([System.Windows.Automation.TreeScope]::Descendants, $SliderCondition)
        if ($null -ne $Slider) {
            $Range = $Slider.GetCurrentPattern([System.Windows.Automation.RangeValuePattern]::Pattern)
            $Out.Slider = [pscustomobject]@{ Name = [string]$Slider.Current.Name; Value = $Range.Current.Value; Minimum = $Range.Current.Minimum; Maximum = $Range.Current.Maximum; Enabled = [bool]$Slider.Current.IsEnabled }
        }
        else {
            $Track = [DsoE4B1.Native]::FindChildByClass([long]$Dialog.Handle, 'msctls_trackbar32')
            if ($Track -ne 0) { $Out.Slider = "trackbar pos|min|max = $([DsoE4B1.Native]::TrackbarState($Track))" }
        }
        $Out.DialogElements = @(Get-E4B1Elements -Handle $Dialog.Handle)
        $Out.Shots += Save-E4B1Shot -Label 'ochrona-okno-wlaczona'
        $Ok = Find-E4B1Element -Handle $Dialog.Handle -Pattern '^OK$' -Type 'Button'
        if ($null -eq $Ok) { throw 'brak OK w oknie konfiguracji' }
        $null = [DsoE4B1.Native]::Click([long]$Ok.Current.NativeWindowHandle)
        $Out.Steps += 'klik: OK (konfiguracja)'
        Start-Sleep -Seconds 4
        $Confirm = Find-E4B1Window -ClassName '#32770' -TitleLike @('Ochrona systemu*', 'System Protection*') -ProcessId $Process.Id
        if ($null -ne $Confirm) { $Out.Steps += "okno konfiguracji nadal otwarte: $($Confirm.Title)" }
        $Out.Shots += Save-E4B1Shot -Label 'ochrona-okno-po'
        $MainOk = Find-E4B1Element -Handle $Main.Handle -Pattern '^OK$' -Type 'Button'
        if ($null -ne $MainOk) { $null = [DsoE4B1.Native]::Click([long]$MainOk.Current.NativeWindowHandle); $Out.Steps += 'klik: OK (okno główne)' }
        Start-Sleep -Seconds 3
        $Out.Done = $true
    }
    catch { $Out.Error = Format-E4B1Error $_ }
    finally { try { $null = Stop-E4B1ProcessByName -Name 'SystemPropertiesProtection' } catch { $null = $_ } }
    return [pscustomobject]$Out
}

function Invoke-E4B1K2Ops {
    # Dyspozytor operacji K2 (lista {Op, ...argumenty}); błąd operacji jest daną.
    [CmdletBinding()]
    param([Parameter(Mandatory)][AllowEmptyCollection()][object[]]$Ops)
    Assert-E4B1InsideVm
    Initialize-E4B1Native -Ui
    $Results = @()
    foreach ($Op in $Ops) {
        try {
            switch ([string]$Op.Op) {
                'Settings' {
                    $Scan = if ($Op.PSObject.Properties['ScanSeconds']) { [int]$Op.ScanSeconds } else { 0 }
                    $Click = if ($Op.PSObject.Properties['ClickPattern']) { [string]$Op.ClickPattern } else { '' }
                    $Busy = if ($Op.PSObject.Properties['BusyPattern']) { [string]$Op.BusyPattern } else { '' }
                    $Results += Invoke-E4B1SettingsPage -Uri ([string]$Op.Uri) -Label ([string]$Op.Label) -ClickPattern $Click -BusyPattern $Busy -ScanSeconds $Scan
                }
                'StoreStart' { $Results += Invoke-E4B1StoreStart -Aumid ([string]$Op.Aumid) -ProcessName ([string]$Op.ProcessName) -WindowTitle ([string]$Op.WindowTitle) -Label ([string]$Op.Label) }
                'Appx' { $Results += Invoke-E4B1Appx -Action ([string]$Op.Action) -Target ([string]$Op.Target) }
                'Winget' { $Results += Invoke-E4B1Winget -Command ([string]$Op.Command) }
                'ProtectionUi' { $Results += Invoke-E4B1ProtectionUi }
                'NetshIpReset' {
                    $Netsh = Invoke-ExploreNative -FilePath (Join-Path $env:SystemRoot 'System32\netsh.exe') -Arguments @('int', 'ip', 'reset') -TimeoutSeconds 120
                    $Results += [pscustomobject]@{ Op = 'NetshIpReset'; ExitCode = $Netsh.ExitCode; Stdout = $Netsh.Stdout; Stderr = $Netsh.Stderr; Error = $Netsh.Error }
                }
                default { throw "Nieznana operacja K2 '$($Op.Op)'." }
            }
        }
        catch { $Results += [pscustomobject]@{ Op = [string]$Op.Op; Error = (Format-E4B1Error $_) } }
    }
    return $Results
}

function Get-E4B1ChildFunctionNames {
    # Funkcje kopiowane do skryptu zadania K2 (domknięcie wywołań pilnuje test); z bezpiecznikiem, który dziecko woła przed operacjami.
    [CmdletBinding()]
    [OutputType([string[]])]
    param()
    return @('Test-E4B1VmComputerName', 'Assert-E4B1InsideVm', 'Format-E4B1Error', 'Get-E4B1NativeSource', 'Initialize-E4B1Native', 'Invoke-ExploreNative',
        'Save-E4B1Shot', 'Get-E4B1Elements', 'Find-E4B1Element', 'Find-E4B1Window', 'Wait-E4B1Window', 'Stop-E4B1ProcessByName', 'Start-E4B1Explorer',
        'Invoke-E4B1SettingsPage', 'Invoke-E4B1StoreStart', 'Invoke-E4B1Appx', 'Invoke-E4B1Winget', 'Invoke-E4B1ProtectionUi', 'Invoke-E4B1K2Ops')
}

function New-E4B1ChildScript {
    # Skrypt zadania K2: param(ResultFile, OpsFile), definicje funkcji, Invoke-E4B1K2Ops na liście z pliku JSON, wynik atomowo przez plik tymczasowy.
    [CmdletBinding()]
    [OutputType([string])]
    param()
    $Header = @'
param([Parameter(Mandatory)][string]$ResultFile, [Parameter(Mandatory)][string]$OpsFile)
$ErrorActionPreference = 'Stop'
'@
    $Definitions = foreach ($Name in Get-E4B1ChildFunctionNames) { "function $Name {`n$((Get-Command -Name $Name -CommandType Function -ErrorAction Stop).Definition)`n}" }
    $Main = @'
$Document = [ordered]@{ Ok = $false; Error = $null; ProcessId = $PID; User = [Security.Principal.WindowsIdentity]::GetCurrent().Name; Results = @() }
try {
    Assert-E4B1InsideVm
    $Parsed = Get-Content -LiteralPath $OpsFile -Raw -Encoding UTF8 | ConvertFrom-Json
    $Ops = @(foreach ($Item in $Parsed) { $Item })
    $Document.Results = @(Invoke-E4B1K2Ops -Ops $Ops)
    $Document.Ok = $true
}
catch { $Document.Error = $_.Exception.Message }
$Temporary = $ResultFile + '.tmp'
$Document | ConvertTo-Json -Depth 12 | Set-Content -LiteralPath $Temporary -Encoding UTF8
Move-Item -LiteralPath $Temporary -Destination $ResultFile -Force
'@
    return ($Header + "`n`n" + ($Definitions -join "`n`n") + "`n`n" + $Main)
}

function Invoke-E4B1UserTask {
    # K2: zadanie Harmonogramu (dso / Interactive / Highest) z listą operacji; wynik z pliku; zadanie ZAWSZE wyrejestrowane.
    # Niedostępność K2 to dana (Available=false, Reason), nie wyjątek.
    [CmdletBinding()]
    param(
        [Parameter(Mandatory)][ValidatePattern('^[a-z0-9-]+\z')][string]$Name,
        [Parameter(Mandatory)][AllowEmptyCollection()][object[]]$Ops,
        [ValidateRange(10, 3600)][int]$TimeoutSeconds = 300,
        [ValidatePattern('^[A-Za-z0-9_-]+\z')][string]$UserName = 'dso'
    )
    Assert-E4B1InsideVm
    $Directory = 'C:\dso\e4b1'
    $TaskName = "DSO-E4B1-$Name"
    $ScriptPath = "$Directory\k2.ps1"
    $OpsPath = "$Directory\$Name.ops.json"
    $ResultPath = "$Directory\$Name.json"
    $Out = [ordered]@{ Name = $Name; Available = $false; Reason = $null; WaitedMs = $null; ChildUser = $null; ChildError = $null; UnregisterError = $null; Results = @() }
    $Registered = $false
    $Watch = [System.Diagnostics.Stopwatch]::StartNew()
    try {
        $null = New-Item -ItemType Directory -Path $Directory -Force
        Set-Content -LiteralPath $ScriptPath -Value (New-E4B1ChildScript) -Encoding UTF8
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
            $Out.ChildUser = $Child.User
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

# --- K1: sondy ze skutkiem ---

function Invoke-E4B1Reg {
    # reg.exe z pełnej ścieżki System32 (add/export/delete/import) z przechwyceniem wyjścia.
    [CmdletBinding()]
    param([Parameter(Mandatory)][string[]]$Arguments)
    Assert-E4B1InsideVm
    $Result = Invoke-ExploreNative -FilePath (Join-Path $env:SystemRoot 'System32\reg.exe') -Arguments $Arguments -TimeoutSeconds 60
    return [pscustomobject]@{ Command = "reg $($Arguments -join ' ')"; ExitCode = $Result.ExitCode; Stdout = $Result.Stdout; Stderr = $Result.Stderr; Error = $Result.Error }
}

function Invoke-E4B1DnsProbe {
    # Pkt 1: pamięć DNS przed, po Resolve-DnsName localhost i nazwy z wpisu w hosts (+ nazwa nieistniejąca), po ipconfig /flushdns, ponownie wypełniona
    # i po Clear-DnsClientCache; na końcu przywrócenie pliku hosts i odczyt. Kryterium: po czyszczeniu tylko nazwy z hosts.
    [CmdletBinding()]
    param(
        [Parameter(Mandatory)]$HostsEntry,
        [Parameter(Mandatory)][string]$NegativeName,
        [AllowEmptyCollection()][string[]]$ExtraNames = @()
    )
    Assert-E4B1InsideVm
    $HostsPath = Join-Path $env:SystemRoot 'System32\drivers\etc\hosts'
    $Out = [ordered]@{ HostsOriginal = @(); HostsNamesWithEntry = @(); Before = $null; Resolves = @(); AfterFill = $null; Flush = $null; AfterFlush = $null; AfterFlushOnlyHosts = $null
        Refill = $null; ClearCmdlet = $null; AfterClear = $null; AfterClearOnlyHosts = $null; HostsRestored = $null; AfterRestore = $null; Error = $null }
    $Original = $null
    try {
        $Original = [System.IO.File]::ReadAllBytes($HostsPath)
        # Pliki czytane przez .NET, nie Get-Content: napisy z Get-Content niosą PSPath/PSProvider, a ConvertTo-Json -Depth 16 rozwija je w gigabajty.
        $Out.HostsOriginal = @([System.IO.File]::ReadAllLines($HostsPath) | Where-Object { $_ -and $_ -notmatch '^\s*#' })
        $Out.Before = Get-E4B1DnsCache
        $Line = "`r`n$($HostsEntry.Address) $($HostsEntry.Name)`r`n"
        [System.IO.File]::AppendAllText($HostsPath, $Line, [System.Text.Encoding]::ASCII)
        $Out.HostsNamesWithEntry = @(Get-E4B1HostsNames -Lines @([System.IO.File]::ReadAllLines($HostsPath)))
        Start-Sleep -Seconds 3
        $Names = @('localhost', [string]$HostsEntry.Name, $NegativeName) + @($ExtraNames)
        $Fill = {
            param([string[]]$List)
            foreach ($Name in $List) {
                $Watch = [System.Diagnostics.Stopwatch]::StartNew()
                $Item = [ordered]@{ Name = $Name; Answers = @(); Error = $null; Ms = $null }
                try { $Item.Answers = @(Resolve-DnsName -Name $Name -Type A -ErrorAction Stop | ForEach-Object { "$($_.Name) $($_.Type) $($_.IPAddress) $($_.Section)" }) } catch { $Item.Error = Format-E4B1Error $_ }
                $Item.Ms = [int]$Watch.ElapsedMilliseconds
                [pscustomobject]$Item
            }
        }
        $Out.Resolves = @(& $Fill $Names)
        $Out.AfterFill = Get-E4B1DnsCache
        $Out.Flush = Invoke-ExploreNative -FilePath (Join-Path $env:SystemRoot 'System32\ipconfig.exe') -Arguments @('/flushdns') -TimeoutSeconds 30
        Start-Sleep -Seconds 1
        $Out.AfterFlush = Get-E4B1DnsCache
        $Out.AfterFlushOnlyHosts = Test-E4B1DnsOnlyHosts -Entries @($Out.AfterFlush.Entries) -HostsNames @($Out.HostsNamesWithEntry)
        $Out.Refill = @(& $Fill $Names)
        $Out.ClearCmdlet = 'Clear-DnsClientCache'
        try { Clear-DnsClientCache -ErrorAction Stop } catch { $Out.ClearCmdlet = "Clear-DnsClientCache: $(Format-E4B1Error $_)" }
        Start-Sleep -Seconds 1
        $Out.AfterClear = Get-E4B1DnsCache
        $Out.AfterClearOnlyHosts = Test-E4B1DnsOnlyHosts -Entries @($Out.AfterClear.Entries) -HostsNames @($Out.HostsNamesWithEntry)
    }
    catch { $Out.Error = Format-E4B1Error $_ }
    finally {
        if ($null -ne $Original) {
            try { [System.IO.File]::WriteAllBytes($HostsPath, $Original); $Out.HostsRestored = $true } catch { $Out.HostsRestored = "błąd: $(Format-E4B1Error $_)" }
            Start-Sleep -Seconds 3
            $Out.AfterRestore = Get-E4B1DnsCache
        }
    }
    return [pscustomobject]$Out
}

function Invoke-E4B1PolicyProbe {
    # Pkt 4: utworzenie testowych polityk WU (reg add), widok Ustawień (K2), reg export klucza WindowsUpdate (z podkluczem AU, jednym plikiem),
    # reg delete, widok, reg import kopii, odczyt (= stan po utworzeniu), widok, na końcu reg delete (sprzątanie przed fazą wureset).
    [CmdletBinding()]
    param(
        [Parameter(Mandatory)][string]$PolicyKey,
        [Parameter(Mandatory)][object[]]$Policies,
        [Parameter(Mandatory)][string]$WuUri,
        [Parameter(Mandatory)][string]$CheckPattern
    )
    Assert-E4B1InsideVm
    $Directory = 'C:\dso\e4b1'
    $Export = "$Directory\wu-policies.reg"
    $Out = [ordered]@{ Before = @(); Adds = @(); AfterAdd = @(); ViewSet = $null; Export = $null; ExportText = @(); ExportHasAu = $null; Delete = $null; AfterDelete = @(); ViewDeleted = $null
        Import = $null; AfterImport = @(); ImportMatches = $null; ViewImported = $null; Single = @(); Cleanup = $null; AfterCleanup = @(); Error = $null }
    try {
        $null = New-Item -ItemType Directory -Path $Directory -Force
        $Out.Before = @(Get-E4B1PolicyState -Key $PolicyKey)
        foreach ($Policy in $Policies) {
            $Out.Adds += Invoke-E4B1Reg -Arguments @('add', [string]$Policy.Key, '/v', [string]$Policy.Name, '/t', [string]$Policy.Kind, '/d', [string]$Policy.Value, '/f')
        }
        $Out.AfterAdd = @(Get-E4B1PolicyState -Key $PolicyKey)
        $Ops = @(@{ Op = 'Settings'; Uri = $WuUri; Label = 'polityki-ustawione'; ClickPattern = $CheckPattern })
        $Out.ViewSet = Invoke-E4B1UserTask -Name 'policies-set' -Ops $Ops -TimeoutSeconds 180
        if (Test-Path -LiteralPath $Export) { Remove-Item -LiteralPath $Export -Force }
        $Out.Export = Invoke-E4B1Reg -Arguments @('export', $PolicyKey, $Export, '/y')
        if (Test-Path -LiteralPath $Export) { $Out.ExportText = @([System.IO.File]::ReadAllLines($Export)) }
        $Out.ExportHasAu = (@($Out.ExportText | Where-Object { $_ -match '\\WindowsUpdate\\AU\]$' }).Count -gt 0)
        $Out.Delete = Invoke-E4B1Reg -Arguments @('delete', $PolicyKey, '/f')
        $Out.AfterDelete = @(Get-E4B1PolicyState -Key $PolicyKey)
        $Ops[0].Label = 'polityki-usuniete'
        $Out.ViewDeleted = Invoke-E4B1UserTask -Name 'policies-deleted' -Ops $Ops -TimeoutSeconds 180
        $Out.Import = Invoke-E4B1Reg -Arguments @('import', $Export)
        $Out.AfterImport = @(Get-E4B1PolicyState -Key $PolicyKey)
        $Out.ImportMatches = (($Out.AfterImport | ConvertTo-Json -Depth 6 -Compress) -eq ($Out.AfterAdd | ConvertTo-Json -Depth 6 -Compress))
        $Ops[0].Label = 'polityki-przywrocone'
        $Out.ViewImported = Invoke-E4B1UserTask -Name 'policies-imported' -Ops $Ops -TimeoutSeconds 180
        # Każda wartość osobno: czy sama wywołuje „zarządza Twoja organizacja” i blokuje „Sprawdź aktualizacje”.
        foreach ($Policy in $Policies) {
            $null = Invoke-E4B1Reg -Arguments @('delete', $PolicyKey, '/f')
            $null = Invoke-E4B1Reg -Arguments @('add', [string]$Policy.Key, '/v', [string]$Policy.Name, '/t', [string]$Policy.Kind, '/d', [string]$Policy.Value, '/f')
            $Single = @(@{ Op = 'Settings'; Uri = $WuUri; Label = ('polityka-' + ([string]$Policy.Name).ToLowerInvariant()); ClickPattern = $CheckPattern })
            $View = Invoke-E4B1UserTask -Name ('policy-' + ([string]$Policy.Name).ToLowerInvariant()) -Ops $Single -TimeoutSeconds 180
            $First = @($View.Results) | Select-Object -First 1
            $Out.Single += [pscustomobject]@{ Name = [string]$Policy.Name; Key = [string]$Policy.Key; ManagedNotice = $(if ($null -ne $First) { $First.ManagedNotice } else { $null })
                CheckButton = $(if ($null -ne $First) { $First.CheckButton } else { $null }); Error = $(if ($null -ne $First) { $First.Error } else { $View.Reason }) }
        }
    }
    catch { $Out.Error = Format-E4B1Error $_ }
    finally {
        $Out.Cleanup = Invoke-E4B1Reg -Arguments @('delete', $PolicyKey, '/f')
        $Out.AfterCleanup = @(Get-E4B1PolicyState -Key $PolicyKey)
    }
    return [pscustomobject]$Out
}

function Invoke-E4B1StoreProbe {
    # Pkt 5 (R33): stan Sklepu -> start jako dso -> Remove-AppxPackage jako dso -> stan (PackageUserInformation) -> start (oczekiwany brak) ->
    # Add-AppxPackage -Register <InstallLocation>\AppxManifest.xml -DisableDevelopmentMode jako dso -> stan (InstallState) -> start Sklepu.
    [CmdletBinding()]
    param(
        [Parameter(Mandatory)][string]$UserSid,
        [Parameter(Mandatory)]$Store
    )
    Assert-E4B1InsideVm
    $Out = [ordered]@{ Before = $null; StartBefore = $null; Remove = $null; AfterRemove = $null; StartAfterRemove = $null; Manifest = $null; Register = $null; AfterRegister = $null; StartAfterRegister = $null; Error = $null }
    try {
        $Out.Before = Get-E4B1PackageState -UserSid $UserSid -Name ([string]$Store.Name)
        $Package = @($Out.Before.Packages) | Select-Object -First 1
        if ($null -eq $Package) { throw "brak pakietu $($Store.Name) na dysku" }
        $Out.Manifest = Join-Path ([string]$Package.InstallLocation) 'AppxManifest.xml'
        $StartOp = @{ Op = 'StoreStart'; Aumid = [string]$Store.Aumid; ProcessName = [string]$Store.Process; WindowTitle = [string]$Store.WindowTitle; Label = 'sklep-przed' }
        $Out.StartBefore = Invoke-E4B1UserTask -Name 'store-start-before' -Ops @($StartOp) -TimeoutSeconds 180
        $Out.Remove = Invoke-E4B1UserTask -Name 'store-remove' -Ops @(@{ Op = 'Appx'; Action = 'Remove'; Target = [string]$Store.Name }) -TimeoutSeconds 300
        Start-Sleep -Seconds 3
        $Out.AfterRemove = Get-E4B1PackageState -UserSid $UserSid -Name ([string]$Store.Name)
        $StartOp.Label = 'sklep-po-usunieciu'
        $Out.StartAfterRemove = Invoke-E4B1UserTask -Name 'store-start-removed' -Ops @($StartOp) -TimeoutSeconds 180
        $Out.Register = Invoke-E4B1UserTask -Name 'store-register' -Ops @(@{ Op = 'Appx'; Action = 'RegisterManifest'; Target = $Out.Manifest }) -TimeoutSeconds 300
        Start-Sleep -Seconds 3
        $Out.AfterRegister = Get-E4B1PackageState -UserSid $UserSid -Name ([string]$Store.Name)
        $StartOp.Label = 'sklep-po-rejestracji'
        $Out.StartAfterRegister = Invoke-E4B1UserTask -Name 'store-start-registered' -Ops @($StartOp) -TimeoutSeconds 180
    }
    catch { $Out.Error = Format-E4B1Error $_ }
    return [pscustomobject]$Out
}

function Invoke-E4B1WingetProbe {
    # Pkt 6: stan Instalatora aplikacji i alias konta -> winget --version jako dso -> Remove-AppxPackage jako dso i usunięcie aliasu (stan „zepsuty”) -> stan, alias,
    # winget --version -> Add-AppxPackage -RegisterByFamilyName -MainPackage <rodzina> jako dso -> stan, alias, winget --version i --info.
    [CmdletBinding()]
    param(
        [Parameter(Mandatory)][string]$UserSid,
        [Parameter(Mandatory)]$Winget
    )
    Assert-E4B1InsideVm
    $Out = [ordered]@{ Before = $null; AliasBefore = $null; RunBefore = $null; Remove = $null; AfterRemove = $null; AliasAfterRemove = $null; RunAfterRemove = $null
        Register = $null; AfterRegister = $null; AliasAfterRegister = $null; RunAfterRegister = $null; Error = $null }
    try {
        $Out.Before = Get-E4B1PackageState -UserSid $UserSid -Name ([string]$Winget.Name)
        $Out.AliasBefore = Get-E4B1WingetAlias -UserSid $UserSid
        $Out.RunBefore = Invoke-E4B1UserTask -Name 'winget-before' -Ops @(@{ Op = 'Winget'; Command = 'version' }, @{ Op = 'Winget'; Command = 'info' }) -TimeoutSeconds 300
        # Remove-AppxPackage dla konta odmawia (0x80073CFA „part of Windows”, pomiar Pro), więc stan „zepsuty” = brak aliasu winget.exe w profilu konta.
        $Out.Remove = Invoke-E4B1UserTask -Name 'winget-remove' -Ops @(@{ Op = 'Appx'; Action = 'Remove'; Target = [string]$Winget.Name }, @{ Op = 'Appx'; Action = 'RemoveAlias'; Target = 'winget.exe' }, @{ Op = 'Winget'; Command = 'version' }) -TimeoutSeconds 300
        Start-Sleep -Seconds 3
        $Out.AfterRemove = Get-E4B1PackageState -UserSid $UserSid -Name ([string]$Winget.Name)
        $Out.AliasAfterRemove = Get-E4B1WingetAlias -UserSid $UserSid
        $Out.RunAfterRemove = Invoke-E4B1UserTask -Name 'winget-removed' -Ops @(@{ Op = 'Winget'; Command = 'version' }) -TimeoutSeconds 300
        $Out.Register = Invoke-E4B1UserTask -Name 'winget-register' -Ops @(@{ Op = 'Appx'; Action = 'RegisterFamily'; Target = [string]$Winget.Family }) -TimeoutSeconds 300
        Start-Sleep -Seconds 3
        $Out.AfterRegister = Get-E4B1PackageState -UserSid $UserSid -Name ([string]$Winget.Name)
        $Out.AliasAfterRegister = Get-E4B1WingetAlias -UserSid $UserSid
        $Out.RunAfterRegister = Invoke-E4B1UserTask -Name 'winget-registered' -Ops @(@{ Op = 'Winget'; Command = 'version' }, @{ Op = 'Winget'; Command = 'info' }) -TimeoutSeconds 300
    }
    catch { $Out.Error = Format-E4B1Error $_ }
    return [pscustomobject]$Out
}

function Invoke-E4B1ProtectionProbe {
    # Pkt 7: stan -> włączenie z okna „Ochrona systemu” (K2) na świeżym systemie -> stan (limit z okna) -> Enable-ComputerRestore -Drive 'C:\' -> stan
    # -> punkt przez DSOptimizer.exe --apply <tweak vmtest> --restore-point required -> stan (limit w vssadmin) -> Disable-ComputerRestore -> stan po 3 s
    # i po 60 s (czy punkty znikają) -> punkt WMI (częstotliwość 0) przy wyłączonej ochronie -> stan -> ponowne Enable (czy stary punkt wraca) -> Disable.
    [CmdletBinding()]
    param(
        [Parameter(Mandatory)][string]$TweakId,
        [Parameter(Mandatory)][string]$Description
    )
    Assert-E4B1InsideVm
    $Out = [ordered]@{ Fresh = $null; Ui = $null; AfterUi = $null; Enable = $null; AfterEnable = $null; AppApply = $null; AppReport = $null; AfterPoint = $null; Disable = $null; AfterDisable = $null
        AfterDisable60s = $null; WmiPoint = $null; AfterWmiPoint = $null; ReEnable = $null; AfterReEnable = $null; Disable2 = $null; AfterDisable2 = $null; Error = $null }
    try {
        $Out.Fresh = Get-E4B1Protection
        # Najpierw okno „Ochrona systemu” na świeżym systemie (bez skojarzenia shadow storage): jaki limit ustawia Windows przy włączeniu z okna.
        $Out.Ui = Invoke-E4B1UserTask -Name 'protection-ui' -Ops @(@{ Op = 'ProtectionUi' }) -TimeoutSeconds 240
        Start-Sleep -Seconds 3
        $Out.AfterUi = Get-E4B1Protection
        $Watch = [System.Diagnostics.Stopwatch]::StartNew()
        try { Enable-ComputerRestore -Drive 'C:\' -ErrorAction Stop; $Out.Enable = "OK $([int]$Watch.ElapsedMilliseconds) ms" } catch { $Out.Enable = Format-E4B1Error $_ }
        $Out.AfterEnable = Get-E4B1Protection
        $null = Invoke-E4B1Reg -Arguments @('add', 'HKLM\SOFTWARE\DSOptimizer.VmTests', '/f')
        $ReportPath = 'C:\dso\out\e4b1-restore-point.json'
        $Out.AppApply = Invoke-ExploreNative -FilePath 'C:\dso\app\DSOptimizer.exe' -Arguments @('--apply', $TweakId, '--catalog', 'C:\dso\vm-test-catalog.json', '--out', $ReportPath, '--restore-point', 'required') -TimeoutSeconds 900
        if (Test-Path -LiteralPath $ReportPath) { $Out.AppReport = [System.IO.File]::ReadAllText($ReportPath, [System.Text.Encoding]::UTF8) }
        $Out.AfterPoint = Get-E4B1Protection
        $Watch.Restart()
        try { Disable-ComputerRestore -Drive 'C:\' -ErrorAction Stop; $Out.Disable = "OK $([int]$Watch.ElapsedMilliseconds) ms" } catch { $Out.Disable = Format-E4B1Error $_ }
        Start-Sleep -Seconds 3
        $Out.AfterDisable = Get-E4B1Protection
        # Czy punkty znikają z opóźnieniem (pomiar Pro: po 3 s punkt i shadow storage nadal są).
        Start-Sleep -Seconds 60
        $Out.AfterDisable60s = Get-E4B1Protection
        $null = Invoke-E4B1Reg -Arguments @('add', 'HKLM\SOFTWARE\Microsoft\Windows NT\CurrentVersion\SystemRestore', '/v', 'SystemRestorePointCreationFrequency', '/t', 'REG_DWORD', '/d', '0', '/f')
        $Watch.Restart()
        try {
            $Result = Invoke-CimMethod -Namespace 'root/default' -ClassName SystemRestore -MethodName CreateRestorePoint -Arguments @{ Description = $Description; RestorePointType = [uint32]12; EventType = [uint32]100 } -ErrorAction Stop
            $Out.WmiPoint = "ReturnValue=$($Result.ReturnValue) $([int]$Watch.ElapsedMilliseconds) ms"
        }
        catch { $Out.WmiPoint = Format-E4B1Error $_ }
        $Out.AfterWmiPoint = Get-E4B1Protection
        # Ponowne włączenie: czy stary punkt (shadow copy zostaje po Disable) wraca na listę; potem wyłączenie (odczyt po restarcie w netreset-after).
        try { Enable-ComputerRestore -Drive 'C:\' -ErrorAction Stop; $Out.ReEnable = 'OK' } catch { $Out.ReEnable = Format-E4B1Error $_ }
        Start-Sleep -Seconds 3
        $Out.AfterReEnable = Get-E4B1Protection
        try { Disable-ComputerRestore -Drive 'C:\' -ErrorAction Stop; $Out.Disable2 = 'OK' } catch { $Out.Disable2 = Format-E4B1Error $_ }
        Start-Sleep -Seconds 3
        $Out.AfterDisable2 = Get-E4B1Protection
    }
    catch { $Out.Error = Format-E4B1Error $_ }
    return [pscustomobject]$Out
}

function Get-E4B1ServiceStates {
    # Krótki stan usług: nazwa=Status.
    [CmdletBinding()]
    param([Parameter(Mandatory)][string[]]$Names)
    return @(foreach ($Name in $Names) { try { "$Name=$((Get-Service -Name $Name -ErrorAction Stop).Status)" } catch { "${Name}: $(Format-E4B1Error $_)" } })
}

function Stop-E4B1Service {
    # ControlService(STOP) jak DSOptimizer (bez zatrzymywania zależnych) i czekanie do 30 s na Stopped; czas i kod Win32.
    [CmdletBinding()]
    param([Parameter(Mandatory)][string]$Name)
    Assert-E4B1InsideVm
    $Out = [ordered]@{ Name = $Name; Before = $null; Code = $null; WaitError = $null; After = $null; Ms = $null }
    $Watch = [System.Diagnostics.Stopwatch]::StartNew()
    try {
        $Controller = New-Object System.ServiceProcess.ServiceController($Name)
        $Out.Before = [string]$Controller.Status
        $Out.Code = [DsoE4B1.Native]::StopService($Name)
        try { $Controller.WaitForStatus('Stopped', [TimeSpan]::FromSeconds(30)) } catch { $Out.WaitError = Format-E4B1Error $_ }
        $Controller.Refresh()
        $Out.After = [string]$Controller.Status
    }
    catch { $Out.WaitError = Format-E4B1Error $_ }
    $Out.Ms = [int]$Watch.ElapsedMilliseconds
    return [pscustomobject]$Out
}

function Move-E4B1Folder {
    # Zmiana nazwy folderu (Directory.Move) na <nazwa>.dso-<znacznik>-e4b1; błąd z HRESULT jest daną.
    [CmdletBinding()]
    param(
        [Parameter(Mandatory)][string]$Path,
        [Parameter(Mandatory)][string]$NewName
    )
    Assert-E4B1InsideVm
    $Target = Join-Path (Split-Path -Parent $Path) $NewName
    $Out = [ordered]@{ Path = $Path; Target = $Target; Moved = $false; Error = $null; Ms = $null }
    $Watch = [System.Diagnostics.Stopwatch]::StartNew()
    try { [System.IO.Directory]::Move($Path, $Target); $Out.Moved = $true } catch { $Out.Error = Format-E4B1Error $_ }
    $Out.Ms = [int]$Watch.ElapsedMilliseconds
    return [pscustomobject]$Out
}

function Invoke-E4B1ServiceConfigProbe {
    # Pkt 3: ChangeServiceConfig dla każdej usługi WU: zmiana na Disabled i powrót do typu sprzed (z DelayedAutostart), odczyt po każdej zmianie
    # i 10 s później (czy WaaSMedicSvc albo inny mechanizm przywraca/odmawia).
    [CmdletBinding()]
    param([Parameter(Mandatory)][string[]]$Names)
    Assert-E4B1InsideVm
    Initialize-E4B1Native
    $Records = @()
    foreach ($Name in $Names) {
        $Record = [ordered]@{ Name = $Name; Before = $null; ToDisabled = $null; AfterDisabled = $null; Restore = $null; AfterRestore = $null; After10s = $null; Error = $null }
        try {
            $Before = Get-E4B1ServiceInfo -Name $Name
            $Record.Before = $Before.StartType
            if ($null -eq $Before.StartRaw) { throw 'brak wartości Start' }
            $Record.ToDisabled = [DsoE4B1.Native]::SetStartType($Name, [uint32]4, -1)
            $Record.AfterDisabled = (Get-E4B1ServiceInfo -Name $Name).StartType
            $Delayed = if ($null -ne $Before.DelayedRaw) { [int]$Before.DelayedRaw } else { 0 }
            $Record.Restore = [DsoE4B1.Native]::SetStartType($Name, [uint32]$Before.StartRaw, $Delayed)
            $Record.AfterRestore = (Get-E4B1ServiceInfo -Name $Name).StartType
            Start-Sleep -Seconds 10
            $Record.After10s = (Get-E4B1ServiceInfo -Name $Name).StartType
        }
        catch { $Record.Error = Format-E4B1Error $_ }
        $Records += [pscustomobject]$Record
    }
    return $Records
}

function Invoke-E4B1CryptForceProbe {
    # Pkt 3: cryptsvc z działającymi usługami zależnymi (ControlService STOP = 1051): stan zależnych, ServiceController.Stop() (zatrzymuje też
    # zależne), kto trzyma catroot2, ponowna próba zmiany nazwy, przywrócenie cryptsvc i zależnych, które działały.
    [CmdletBinding()]
    param(
        [Parameter(Mandatory)][string[]]$Paths,
        [Parameter(Mandatory)][datetime]$At
    )
    Assert-E4B1InsideVm
    $Out = [ordered]@{ Dependents = @(); StopError = $null; StopMs = $null; StatesAfterStop = @(); Lockers = @(); Moves = @(); Restores = @(); StatesAfterRestore = @(); Error = $null }
    try {
        $Controller = New-Object System.ServiceProcess.ServiceController('cryptsvc')
        $Dependents = @($Controller.DependentServices)
        $Out.Dependents = @($Dependents | ForEach-Object { "$($_.ServiceName)=$($_.Status) type=$($_.ServiceType)" })
        $WasRunning = @($Dependents | Where-Object { $_.Status -eq 'Running' } | ForEach-Object { $_.ServiceName })
        $Watch = [System.Diagnostics.Stopwatch]::StartNew()
        try { $Controller.Stop(); $Controller.WaitForStatus('Stopped', [TimeSpan]::FromSeconds(60)) } catch { $Out.StopError = Format-E4B1Error $_ }
        $Out.StopMs = [int]$Watch.ElapsedMilliseconds
        $Out.StatesAfterStop = @(Get-E4B1ServiceStates -Names (@('cryptsvc') + @($Dependents | ForEach-Object { $_.ServiceName })))
        $Out.Lockers = @(foreach ($Path in $Paths) { Get-E4B1FolderLockers -Path $Path })
        foreach ($Path in $Paths) { $Out.Moves += Move-E4B1Folder -Path $Path -NewName (Get-E4B1NewBackupName -FolderName (Split-Path -Leaf $Path) -At $At) }
        foreach ($Name in @('cryptsvc') + $WasRunning) {
            $Restore = [ordered]@{ Name = $Name; Error = $null; After = $null }
            try {
                $Service = New-Object System.ServiceProcess.ServiceController($Name)
                if ($Service.Status -ne 'Running') { $Service.Start(); $Service.WaitForStatus('Running', [TimeSpan]::FromSeconds(60)) }
                $Service.Refresh()
                $Restore.After = [string]$Service.Status
            }
            catch { $Restore.Error = Format-E4B1Error $_ }
            $Out.Restores += [pscustomobject]$Restore
        }
        $Out.StatesAfterRestore = @(Get-E4B1ServiceStates -Names (@('cryptsvc') + @($Dependents | ForEach-Object { $_.ServiceName })))
    }
    catch { $Out.Error = Format-E4B1Error $_ }
    return [pscustomobject]$Out
}

function Invoke-E4B1WuResetProbe {
    # Pkt 3: kto trzyma pliki (Restart Manager) przy działających usługach, zatrzymanie wuauserv/bits/cryptsvc (kolejność, czas, stan po 0/5/15 s),
    # kto trzyma pliki po zatrzymaniu, zmiana nazwy obu folderów; przy porażce zatrzymanie UsoSvc/msiserver i ponowna próba; przywrócenie usług
    # do stanu sprzed; kiedy Windows tworzy foldery na nowo; historia aktualizacji w Ustawieniach (K2).
    [CmdletBinding()]
    param(
        [Parameter(Mandatory)][string[]]$AllServices,
        [Parameter(Mandatory)][string[]]$StopBasic,
        [Parameter(Mandatory)][string[]]$StopExtra,
        [Parameter(Mandatory)][string[]]$Folders,
        [Parameter(Mandatory)][int[]]$RecreateChecks,
        [Parameter(Mandatory)][string]$HistoryUri,
        [Parameter(Mandatory)][string]$WuUri
    )
    Assert-E4B1InsideVm
    Initialize-E4B1Native
    $Paths = @($Folders | ForEach-Object { Join-Path $env:SystemRoot $_ })
    $Stamp = Get-Date
    $Out = [ordered]@{ HistoryBefore = $null; ServicesBefore = @(); FoldersBefore = @(); LockersRunning = @(); Stops = @(); StatesAfterStop = @(); LockersStopped = @(); Moves = @(); ExtraStops = @()
        LockersBeforeRetry = @(); Retries = @(); CryptForce = $null; StatesAfterMoves = @(); Restores = @(); StatesAfterRestore = @(); Recreate = @(); HistoryAfter = $null; History = $null; Error = $null }
    try {
        $Out.HistoryBefore = Get-E4B1WuHistoryCount
        $Out.ServicesBefore = @(foreach ($Name in $AllServices) { Get-E4B1ServiceInfo -Name $Name })
        $Running = @($Out.ServicesBefore | Where-Object { $_.Status -eq 'Running' } | ForEach-Object { $_.Name })
        $Out.FoldersBefore = @(foreach ($Path in $Paths) { Get-E4B1FolderInfo -Path $Path })
        $Out.LockersRunning = @(foreach ($Path in $Paths) { Get-E4B1FolderLockers -Path $Path })
        foreach ($Name in $StopBasic) { $Out.Stops += Stop-E4B1Service -Name $Name }
        foreach ($Delay in 0, 5, 15) {
            if ($Delay -gt 0) { Start-Sleep -Seconds 5 }
            $Out.StatesAfterStop += [pscustomobject]@{ Second = $Delay; States = @(Get-E4B1ServiceStates -Names $AllServices) }
            if ($Delay -eq 0) { $Out.LockersStopped = @(foreach ($Path in $Paths) { Get-E4B1FolderLockers -Path $Path }) }
        }
        foreach ($Path in $Paths) { $Out.Moves += Move-E4B1Folder -Path $Path -NewName (Get-E4B1NewBackupName -FolderName (Split-Path -Leaf $Path) -At $Stamp) }
        $Failed = @($Out.Moves | Where-Object { -not $_.Moved } | ForEach-Object { $_.Path })
        if ($Failed.Count -gt 0) {
            foreach ($Name in $StopExtra) { $Out.ExtraStops += Stop-E4B1Service -Name $Name }
            $Out.LockersBeforeRetry = @(foreach ($Path in $Failed) { Get-E4B1FolderLockers -Path $Path })
            foreach ($Path in $Failed) { $Out.Retries += Move-E4B1Folder -Path $Path -NewName (Get-E4B1NewBackupName -FolderName (Split-Path -Leaf $Path) -At $Stamp.AddSeconds(1)) }
            $Still = @($Out.Retries | Where-Object { -not $_.Moved } | ForEach-Object { $_.Path })
            if ($Still.Count -gt 0) { $Out.CryptForce = Invoke-E4B1CryptForceProbe -Paths $Still -At $Stamp.AddSeconds(2) }
        }
        $Out.StatesAfterMoves = @(Get-E4B1ServiceStates -Names $AllServices)
        # Przywracanie w odwrotnej kolejności zatrzymania (cryptsvc, bits, wuauserv), potem dodatkowe; tylko usługi działające przed resetem.
        foreach ($Name in @($StopBasic[($StopBasic.Count - 1)..0]) + @($StopExtra)) {
            if ($Running -notcontains $Name) { continue }
            $Watch = [System.Diagnostics.Stopwatch]::StartNew()
            $Restore = [ordered]@{ Name = $Name; Error = $null; Ms = $null; After = $null }
            try {
                $Controller = New-Object System.ServiceProcess.ServiceController($Name)
                $Controller.Refresh()
                if ($Controller.Status -ne 'Running') { $Controller.Start(); $Controller.WaitForStatus('Running', [TimeSpan]::FromSeconds(60)) }
                $Controller.Refresh()
                $Restore.After = [string]$Controller.Status
            }
            catch { $Restore.Error = Format-E4B1Error $_ }
            $Restore.Ms = [int]$Watch.ElapsedMilliseconds
            $Out.Restores += [pscustomobject]$Restore
        }
        $Out.StatesAfterRestore = @(Get-E4B1ServiceStates -Names $AllServices)
        $Elapsed = 0
        foreach ($Second in $RecreateChecks) {
            if ($Second -gt $Elapsed) { Start-Sleep -Seconds ($Second - $Elapsed); $Elapsed = $Second }
            $Out.Recreate += [pscustomobject]@{ Second = $Second; Folders = @(foreach ($Path in $Paths) { Get-E4B1FolderInfo -Path $Path }); States = @(Get-E4B1ServiceStates -Names $AllServices) }
        }
        $Out.HistoryAfter = Get-E4B1WuHistoryCount
        $Out.History = Invoke-E4B1UserTask -Name 'wureset-view' -Ops @(@{ Op = 'Settings'; Uri = $HistoryUri; Label = 'historia-po-resecie' }, @{ Op = 'Settings'; Uri = $WuUri; Label = 'wu-po-resecie' }) -TimeoutSeconds 240
        $Out.Recreate += [pscustomobject]@{ Second = -1; Note = 'po otwarciu Ustawień (historia i strona WU)'; Folders = @(foreach ($Path in $Paths) { Get-E4B1FolderInfo -Path $Path }); States = @(Get-E4B1ServiceStates -Names $AllServices) }
    }
    catch { $Out.Error = Format-E4B1Error $_ }
    return [pscustomobject]$Out
}

function Select-E4B1Adapter {
    # Karta do resetu sieci: pierwsza karta Ethernet (Hyper-V ma jedną).
    [CmdletBinding()]
    param()
    return (@(Get-NetAdapter -ErrorAction Stop | Sort-Object -Property ifIndex) | Select-Object -First 1)
}

function Invoke-E4B1NetResetProbe {
    # Pkt 2 (przed restartem): ręczny IPv4 i DNS na karcie bez sieci, odczyt, netsh winsock reset, netsh int ip reset (kod, wyjście PL), odczyt.
    [CmdletBinding()]
    param([Parameter(Mandatory)]$Manual)
    Assert-E4B1InsideVm
    $Out = [ordered]@{ Adapter = $null; Before = @(); SetDhcp = $null; SetIp = $null; SetDns = $null; AfterManual = @(); Winsock = $null; IntIp = $null; IntIpLog = $null; IntIpAsUser = $null; AfterReset = @()
        LastBoot = $null; Error = $null }
    try {
        $Adapter = Select-E4B1Adapter
        $Out.Adapter = "$($Adapter.Name) ifIndex=$($Adapter.ifIndex) $($Adapter.Status) $($Adapter.InterfaceGuid)"
        $Out.Before = @(Get-E4B1IpConfig)
        $Netsh = Join-Path $env:SystemRoot 'System32\netsh.exe'
        # Ręczny adres jak z okna właściwości karty: netsh zapisuje go trwale także na karcie bez sieci (pomiar Home: Set-NetIPInterface -Dhcp Disabled
        # na rozłączonej karcie nie zmienia EnableDHCP, a New-NetIPAddress kończy się „Inconsistent parameters PolicyStore PersistentStore and Dhcp Enabled”).
        $Out.SetDhcp = 'netsh source=static (bez Set-NetIPInterface)'
        $Static = Invoke-ExploreNative -FilePath $Netsh -Arguments @('interface', 'ipv4', 'set', 'address', "name=$($Adapter.ifIndex)", 'source=static', "address=$($Manual.Address)", "mask=$($Manual.Mask)", "gateway=$($Manual.Gateway)") -TimeoutSeconds 60
        $Out.SetIp = "netsh kod $($Static.ExitCode): $(([string]$Static.Stdout).Trim())"
        try { Set-DnsClientServerAddress -InterfaceIndex $Adapter.ifIndex -ServerAddresses @($Manual.Dns) -ErrorAction Stop; $Out.SetDns = 'OK' } catch { $Out.SetDns = Format-E4B1Error $_ }
        Start-Sleep -Seconds 3
        $Out.AfterManual = @(Get-E4B1IpConfig)
        $Out.Winsock = Invoke-ExploreNative -FilePath $Netsh -Arguments @('winsock', 'reset') -TimeoutSeconds 120
        # Z plikiem logu: netsh zapisuje w nim, które klucze resetował (i który odmówił dostępu).
        $LogPath = Join-Path $env:TEMP 'dso-e4b1-netsh-int-ip-reset.log'
        $Out.IntIp = Invoke-ExploreNative -FilePath $Netsh -Arguments @('int', 'ip', 'reset', $LogPath) -TimeoutSeconds 120
        if (Test-Path -LiteralPath $LogPath) {
            $LogText = [System.IO.File]::ReadAllText($LogPath)
            $Out.IntIpLog = if ($LogText.Length -gt 30000) { $LogText.Substring(0, 30000) } else { $LogText }
        }
        # To samo z procesu podniesionego dso (jak DSOptimizer po UAC): czy kod wyjścia i „Odmowa dostępu” są takie same.
        $Out.IntIpAsUser = Invoke-E4B1UserTask -Name 'netsh-ip-reset' -Ops @(@{ Op = 'NetshIpReset' }) -TimeoutSeconds 180
        Start-Sleep -Seconds 3
        $Out.AfterReset = @(Get-E4B1IpConfig)
        $Out.LastBoot = (Get-CimInstance -ClassName Win32_OperatingSystem).LastBootUpTime.ToString('o')
    }
    catch { $Out.Error = Format-E4B1Error $_ }
    return [pscustomobject]$Out
}

function Invoke-E4B1NetResetAfter {
    # Pkt 2 (po restarcie): odczyt (czy ręczny IP/DNS przetrwał), potem przywrócenie DHCP i automatycznego DNS (przed fazą network) i odczyt.
    [CmdletBinding()]
    param([Parameter(Mandatory)]$Manual)
    Assert-E4B1InsideVm
    $Out = [ordered]@{ LastBoot = $null; AfterReboot = @(); Protection = $null; Restore = @(); AfterRestore = @(); Error = $null }
    try {
        $Out.LastBoot = (Get-CimInstance -ClassName Win32_OperatingSystem).LastBootUpTime.ToString('o')
        $Out.AfterReboot = @(Get-E4B1IpConfig)
        # Pkt 7: czy punkty przywracania (shadow copy) przetrwały restart przy wyłączonej ochronie (faza protection kończy się Disable).
        $Out.Protection = Get-E4B1Protection
        $Adapter = Select-E4B1Adapter
        $Static = Invoke-ExploreNative -FilePath (Join-Path $env:SystemRoot 'System32\netsh.exe') -Arguments @('interface', 'ipv4', 'set', 'address', "name=$($Adapter.ifIndex)", 'source=dhcp') -TimeoutSeconds 60
        $Out.Restore += "netsh source=dhcp kod $($Static.ExitCode)"
        try { Get-NetIPAddress -InterfaceIndex $Adapter.ifIndex -AddressFamily IPv4 -IPAddress ([string]$Manual.Address) -ErrorAction Stop | Remove-NetIPAddress -Confirm:$false -ErrorAction Stop; $Out.Restore += 'Remove-NetIPAddress OK' } catch { $Out.Restore += "Remove-NetIPAddress: $(Format-E4B1Error $_)" }
        try { Get-NetRoute -InterfaceIndex $Adapter.ifIndex -DestinationPrefix '0.0.0.0/0' -ErrorAction Stop | Remove-NetRoute -Confirm:$false -ErrorAction Stop; $Out.Restore += 'Remove-NetRoute OK' } catch { $Out.Restore += "Remove-NetRoute: $(Format-E4B1Error $_)" }
        try { Set-NetIPInterface -InterfaceIndex $Adapter.ifIndex -AddressFamily IPv4 -Dhcp Enabled -ErrorAction Stop; $Out.Restore += 'Dhcp Enabled OK' } catch { $Out.Restore += "Dhcp: $(Format-E4B1Error $_)" }
        try { Set-DnsClientServerAddress -InterfaceIndex $Adapter.ifIndex -ResetServerAddresses -ErrorAction Stop; $Out.Restore += 'DNS reset OK' } catch { $Out.Restore += "DNS: $(Format-E4B1Error $_)" }
        Start-Sleep -Seconds 3
        $Out.AfterRestore = @(Get-E4B1IpConfig)
    }
    catch { $Out.Error = Format-E4B1Error $_ }
    return [pscustomobject]$Out
}

function Wait-E4B1DhcpAddress {
    # Czeka (najwyżej Seconds) na adres IPv4 z DHCP; zwraca czas oczekiwania w ms (albo -1 po limicie).
    [CmdletBinding()]
    param([ValidateRange(5, 600)][int]$Seconds = 90)
    $Watch = [System.Diagnostics.Stopwatch]::StartNew()
    while ($Watch.ElapsedMilliseconds -lt ($Seconds * 1000)) {
        if (@(Get-NetIPAddress -AddressFamily IPv4 -PrefixOrigin Dhcp -ErrorAction SilentlyContinue).Count -gt 0) { return [int]$Watch.ElapsedMilliseconds }
        Start-Sleep -Seconds 2
    }
    return -1
}

function Invoke-E4B1WuHistoryProbe {
    # Faza z siecią przed resetem WU: skan z Ustawień (klik „Sprawdź aktualizacje” jako dso), potem czekanie (najwyżej WaitSeconds), aż historia WU
    # (COM, GetTotalHistoryCount) ma wpis; strona historii w Ustawieniach (zrzut). Daje historię, którą reset WU ma wyczyścić.
    [CmdletBinding()]
    param(
        [Parameter(Mandatory)][string]$WuUri,
        [Parameter(Mandatory)][string]$HistoryUri,
        [Parameter(Mandatory)][string]$CheckPattern,
        [Parameter(Mandatory)][string]$BusyPattern,
        [Parameter(Mandatory)][int]$ScanSeconds,
        [ValidateRange(60, 3600)][int]$WaitSeconds = 1500
    )
    Assert-E4B1InsideVm
    $Out = [ordered]@{ AddressWaitMs = $null; IpConfig = @(); HistoryStart = $null; Scan = $null; Polls = @(); HistoryWaitMs = $null; HistoryEnd = $null; View = $null; Error = $null }
    try {
        $Out.AddressWaitMs = Wait-E4B1DhcpAddress -Seconds 120
        $Out.IpConfig = @(Get-E4B1IpConfig)
        $Out.HistoryStart = Get-E4B1WuHistoryCount
        $Out.Scan = Invoke-E4B1UserTask -Name 'wuhistory-scan' -Ops @(@{ Op = 'Settings'; Uri = $WuUri; Label = 'skan-przed-resetem'; ClickPattern = $CheckPattern; BusyPattern = $BusyPattern; ScanSeconds = $ScanSeconds }) -TimeoutSeconds ($ScanSeconds + 300)
        $Watch = [System.Diagnostics.Stopwatch]::StartNew()
        while ($Watch.ElapsedMilliseconds -lt ($WaitSeconds * 1000)) {
            $Count = Get-E4B1WuHistoryCount
            $Out.Polls += [pscustomobject]@{ Ms = [int]$Watch.ElapsedMilliseconds; Count = $Count.Count; Error = $Count.Error }
            if ($null -ne $Count.Count -and $Count.Count -gt 0) { $Out.HistoryWaitMs = [int]$Watch.ElapsedMilliseconds; break }
            Start-Sleep -Seconds 30
        }
        $Out.HistoryEnd = Get-E4B1WuHistoryCount
        $Out.View = Invoke-E4B1UserTask -Name 'wuhistory-view' -Ops @(@{ Op = 'Settings'; Uri = $HistoryUri; Label = 'historia-przed-resetem' }, @{ Op = 'Settings'; Uri = $WuUri; Label = 'wu-przed-resetem' }) -TimeoutSeconds 240
    }
    catch { $Out.Error = Format-E4B1Error $_ }
    return [pscustomobject]$Out
}

function Invoke-E4B1NetworkProbe {
    # Faza z siecią: adres DHCP, DNS z prawdziwymi nazwami i ipconfig /flushdns, winget (source list, version) jako dso, skan WU w Ustawieniach
    # (klik „Sprawdź dostępność aktualizacji”, czekanie na koniec), historia aktualizacji, foldery WU po skanie.
    [CmdletBinding()]
    param(
        [Parameter(Mandatory)][string[]]$DnsNames,
        [Parameter(Mandatory)]$HostsEntry,
        [Parameter(Mandatory)][string]$NegativeName,
        [Parameter(Mandatory)][string]$WuUri,
        [Parameter(Mandatory)][string]$HistoryUri,
        [Parameter(Mandatory)][string]$CheckPattern,
        [Parameter(Mandatory)][string]$BusyPattern,
        [Parameter(Mandatory)][int]$ScanSeconds,
        [Parameter(Mandatory)][string[]]$Folders,
        [Parameter(Mandatory)][string[]]$Services
    )
    Assert-E4B1InsideVm
    $Out = [ordered]@{ AddressWaitMs = $null; IpConfig = @(); Dns = $null; K2 = $null; HistoryAfterScan = $null; FoldersAfter = @(); ServicesAfter = @(); Error = $null }
    try {
        $Out.AddressWaitMs = Wait-E4B1DhcpAddress -Seconds 120
        Start-Sleep -Seconds 5
        $Out.IpConfig = @(Get-E4B1IpConfig)
        $Out.Dns = Invoke-E4B1DnsProbe -HostsEntry $HostsEntry -NegativeName $NegativeName -ExtraNames $DnsNames
        $Ops = @(
            @{ Op = 'Winget'; Command = 'source-list' }
            @{ Op = 'Winget'; Command = 'search' }
            @{ Op = 'Winget'; Command = 'version' }
            @{ Op = 'Settings'; Uri = $WuUri; Label = 'skan-wu'; ClickPattern = $CheckPattern; BusyPattern = $BusyPattern; ScanSeconds = $ScanSeconds }
            @{ Op = 'Settings'; Uri = $HistoryUri; Label = 'historia-po-skanie' }
        )
        $Out.K2 = Invoke-E4B1UserTask -Name 'network' -Ops $Ops -TimeoutSeconds ($ScanSeconds + 600)
        $Out.HistoryAfterScan = Get-E4B1WuHistoryCount
        $Out.FoldersAfter = @(foreach ($Folder in $Folders) { Get-E4B1FolderInfo -Path (Join-Path $env:SystemRoot $Folder) })
        $Out.ServicesAfter = @(Get-E4B1ServiceStates -Names $Services)
    }
    catch { $Out.Error = Format-E4B1Error $_ }
    return [pscustomobject]$Out
}

function Invoke-E4B1Baseline {
    # Stan świeżej VM (tylko odczyt K1 + widoki K2): usługi WU (z wyzwalaczami), foldery, kto trzyma pliki, polityki, DNS, sieć, pakiety,
    # alias winget, ochrona systemu; K2: winget --version offline, strona WU i historia aktualizacji.
    [CmdletBinding()]
    param(
        [Parameter(Mandatory)][string]$UserSid,
        [Parameter(Mandatory)]$Plan
    )
    Assert-E4B1InsideVm
    $Out = [ordered]@{ Services = @(); Folders = @(); Lockers = @(); Policies = @(); Dns = $null; IpConfig = @(); Store = $null; Winget = $null; WingetAlias = $null; Protection = $null
        WuHistory = $null; PartOfDomain = $null; K2 = $null; Error = $null }
    try {
        $Out.Services = @(foreach ($Name in @($Plan.WuServices)) { Get-E4B1ServiceInfo -Name $Name -Triggers })
        $Out.Folders = @(foreach ($Folder in @($Plan.WuFolders)) { Get-E4B1FolderInfo -Path (Join-Path $env:SystemRoot $Folder) })
        $Out.Lockers = @(foreach ($Folder in @($Plan.WuFolders)) { Get-E4B1FolderLockers -Path (Join-Path $env:SystemRoot $Folder) })
        $Out.Policies = @(Get-E4B1PolicyState -Key ([string]$Plan.PolicyKey))
        $Out.Dns = Get-E4B1DnsCache
        $Out.IpConfig = @(Get-E4B1IpConfig)
        $Out.Store = Get-E4B1PackageState -UserSid $UserSid -Name ([string]$Plan.Store.Name)
        $Out.Winget = Get-E4B1PackageState -UserSid $UserSid -Name ([string]$Plan.Winget.Name)
        $Out.WingetAlias = Get-E4B1WingetAlias -UserSid $UserSid
        $Out.Protection = Get-E4B1Protection
        $Out.WuHistory = Get-E4B1WuHistoryCount
        try { $Out.PartOfDomain = (Get-CimInstance -ClassName Win32_ComputerSystem -ErrorAction Stop).PartOfDomain } catch { $Out.PartOfDomain = Format-E4B1Error $_ }
        $Ops = @(
            @{ Op = 'Winget'; Command = 'version' }
            @{ Op = 'Settings'; Uri = [string]$Plan.WuUri; Label = 'wu-swieza'; ClickPattern = [string]$Plan.WuCheckButton }
            @{ Op = 'Settings'; Uri = [string]$Plan.WuHistoryUri; Label = 'historia-swieza' }
        )
        $Out.K2 = Invoke-E4B1UserTask -Name 'baseline' -Ops $Ops -TimeoutSeconds 300
    }
    catch { $Out.Error = Format-E4B1Error $_ }
    return [pscustomobject]$Out
}

function Invoke-E4B1Collection {
    # Punkt wejścia po stronie VM (jedna faza): zwraca JEDEN string JSON.
    [CmdletBinding()]
    param(
        [Parameter(Mandatory)][string]$PlanJson,
        [Parameter(Mandatory)][ValidateSet('baseline', 'dns', 'policies', 'store', 'winget', 'protection', 'wuhistory', 'wureset', 'network', 'netreset', 'netreset-after')][string]$Phase,
        [Parameter(Mandatory)][string]$UserSid
    )
    Assert-E4B1InsideVm
    $Plan = $PlanJson | ConvertFrom-Json
    $Document = [ordered]@{ meta = $null; phase = $Phase; error = $null; data = $null }
    try { $Document.meta = Get-ExploreMeta } catch { $Document.meta = [ordered]@{ Error = $_.Exception.Message } }
    # Błąd fazy (także wiązania parametrów sondy) jest daną ze śladem stosu, żeby faza zawsze zwracała JSON.
    try {
    switch ($Phase) {
        'baseline' { $Document.data = Invoke-E4B1Baseline -UserSid $UserSid -Plan $Plan }
        'dns' { $Document.data = Invoke-E4B1DnsProbe -HostsEntry $Plan.DnsHosts -NegativeName ([string]$Plan.DnsNegativeName) }
        'policies' { $Document.data = Invoke-E4B1PolicyProbe -PolicyKey ([string]$Plan.PolicyKey) -Policies @($Plan.Policies) -WuUri ([string]$Plan.WuUri) -CheckPattern ([string]$Plan.WuCheckButton) }
        'store' { $Document.data = Invoke-E4B1StoreProbe -UserSid $UserSid -Store $Plan.Store }
        'winget' { $Document.data = Invoke-E4B1WingetProbe -UserSid $UserSid -Winget $Plan.Winget }
        'protection' { $Document.data = Invoke-E4B1ProtectionProbe -TweakId ([string]$Plan.RestorePointTweak) -Description ([string]$Plan.RestorePointDescription) }
        'wuhistory' {
            $Document.data = Invoke-E4B1WuHistoryProbe -WuUri ([string]$Plan.WuUri) -HistoryUri ([string]$Plan.WuHistoryUri) -CheckPattern ([string]$Plan.WuCheckButton) -BusyPattern ([string]$Plan.WuBusyPattern) `
                -ScanSeconds ([int]$Plan.ScanTimeoutSeconds) -WaitSeconds ([int]$Plan.HistoryWaitSeconds)
        }
        'wureset' {
            $Document.data = [ordered]@{
                ServiceConfig = @(Invoke-E4B1ServiceConfigProbe -Names @($Plan.WuServices))
                Reset         = Invoke-E4B1WuResetProbe -AllServices @($Plan.WuServices) -StopBasic @($Plan.WuStopBasic) -StopExtra @($Plan.WuStopExtra) -Folders @($Plan.WuFolders) `
                    -RecreateChecks @($Plan.RecreateChecksSeconds) -HistoryUri ([string]$Plan.WuHistoryUri) -WuUri ([string]$Plan.WuUri)
            }
        }
        'netreset' { $Document.data = Invoke-E4B1NetResetProbe -Manual $Plan.NetManual }
        'netreset-after' { $Document.data = Invoke-E4B1NetResetAfter -Manual $Plan.NetManual }
        'network' {
            $Document.data = Invoke-E4B1NetworkProbe -DnsNames @($Plan.DnsNetworkNames) -HostsEntry $Plan.DnsHosts -NegativeName ([string]$Plan.DnsNegativeName) -WuUri ([string]$Plan.WuUri) `
                -HistoryUri ([string]$Plan.WuHistoryUri) -CheckPattern ([string]$Plan.WuCheckButton) -BusyPattern ([string]$Plan.WuBusyPattern) -ScanSeconds ([int]$Plan.ScanTimeoutSeconds) `
                -Folders @($Plan.WuFolders) -Services @($Plan.WuServices)
        }
    }
    }
    catch { $Document.error = "$(Format-E4B1Error $_) | $($_.ScriptStackTrace)" }
    try { return ($Document | ConvertTo-Json -Depth 16) }
    catch { return ([ordered]@{ meta = $Document.meta; phase = $Phase; error = "ConvertTo-Json: $(Format-E4B1Error $_) | $($_.ScriptStackTrace)"; data = $null } | ConvertTo-Json -Depth 6) }
}

# ---------------------------------------------------------------------------------------------------------------------
# Strona hosta (nic z tego nie jest wysyłane do VM).
# ---------------------------------------------------------------------------------------------------------------------

function Get-E4B1RemoteFunctionNames {
    return @(
        'Test-E4B1VmComputerName', 'Assert-E4B1InsideVm', 'Format-E4B1Error', 'Get-E4B1StartTypeName', 'Get-E4B1NewBackupName', 'Test-E4B1DnsOnlyHosts', 'Get-E4B1HostsNames',
        'Get-E4B1NativeSource', 'Initialize-E4B1Native', 'Get-E4B1UserProfilePath', 'Get-E4B1ServiceInfo', 'Get-E4B1FolderInfo', 'Get-E4B1FolderLockers', 'Get-E4B1DnsCache',
        'Get-E4B1IpConfig', 'Get-E4B1PackageState', 'Get-E4B1WingetAlias', 'Get-E4B1Protection', 'Get-E4B1WuHistoryCount', 'Get-E4B1PolicyState',
        'Save-E4B1Shot', 'Get-E4B1Elements', 'Find-E4B1Element', 'Find-E4B1Window', 'Wait-E4B1Window', 'Stop-E4B1ProcessByName', 'Start-E4B1Explorer', 'Invoke-E4B1SettingsPage',
        'Invoke-E4B1StoreStart', 'Invoke-E4B1Appx', 'Invoke-E4B1Winget', 'Invoke-E4B1ProtectionUi', 'Invoke-E4B1K2Ops', 'Get-E4B1ChildFunctionNames', 'New-E4B1ChildScript',
        'Invoke-E4B1UserTask', 'Invoke-E4B1Reg', 'Invoke-E4B1DnsProbe', 'Invoke-E4B1PolicyProbe', 'Invoke-E4B1StoreProbe', 'Invoke-E4B1WingetProbe', 'Invoke-E4B1ProtectionProbe',
        'Get-E4B1ServiceStates', 'Stop-E4B1Service', 'Move-E4B1Folder', 'Invoke-E4B1ServiceConfigProbe', 'Invoke-E4B1CryptForceProbe', 'Invoke-E4B1WuResetProbe', 'Select-E4B1Adapter',
        'Invoke-E4B1NetResetProbe', 'Invoke-E4B1NetResetAfter', 'Wait-E4B1DhcpAddress', 'Invoke-E4B1WuHistoryProbe', 'Invoke-E4B1NetworkProbe', 'Invoke-E4B1Baseline', 'Invoke-E4B1Collection'
    )
}

function Get-E4B1HostOnlyFunctionNames {
    return @('Get-E4B1Plan', 'Get-E4B1RemoteFunctionNames', 'Get-E4B1HostOnlyFunctionNames', 'Get-E4B1RemoteScript', 'Assert-E4B1OutDirectory', 'Get-E4B1PhaseOrder', 'Get-E4B1NetworkPhases', 'Get-E4B1RunArguments',
        'Invoke-E4B1VmPhase', 'Restart-E4B1Vm', 'Complete-E4B1VmCleanup', 'Invoke-E4B1VmRun', 'Invoke-E4B1Exploration')
}

function Get-E4B1RemoteScript {
    # Tekst definicji funkcji zdalnych: część wspólna z modułu + funkcje E4B1 (FunctionInfo pobrane tu, w zasięgu skryptu).
    [CmdletBinding()]
    [OutputType([string])]
    param()
    $Names = @(Get-ExploreCommonRemoteFunctionNames) + @(Get-E4B1RemoteFunctionNames)
    return (ConvertTo-ExploreRemoteScript -Command @($Names | ForEach-Object { Get-Command -Name $_ -CommandType Function -ErrorAction Stop }))
}

function Assert-E4B1OutDirectory {
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

function Get-E4B1PhaseOrder {
    # Wybrane fazy w stałej kolejności przebiegu; baseline zawsze pierwsza; fazy z siecią (wuhistory, network) tylko z siecią i przed resetem
    # sieci; netreset-after zaraz po netreset (po restarcie).
    [CmdletBinding()]
    [OutputType([string[]])]
    param(
        [Parameter(Mandatory)][string[]]$Phase,
        [bool]$Network = $true
    )
    $Order = @('baseline', 'dns', 'policies', 'store', 'winget', 'protection', 'wuhistory', 'wureset', 'network', 'netreset', 'netreset-after')
    $Wanted = @($Phase) + @('baseline')
    if ($Wanted -contains 'netreset') { $Wanted += 'netreset-after' }
    if (-not $Network) { $Wanted = @($Wanted | Where-Object { @(Get-E4B1NetworkPhases) -notcontains $_ }) }
    return @($Order | Where-Object { $Wanted -contains $_ })
}

function Get-E4B1NetworkPhases {
    # Fazy oznaczone w planie „sieć”: karta VM podłączona do 'Default Switch' od pierwszej z nich do ostatniej (wureset pomiędzy nimi też ma sieć).
    [CmdletBinding()]
    [OutputType([string[]])]
    param()
    return @('wuhistory', 'network')
}

function Get-E4B1RunArguments {
    # Argumenty przebiegu po walidacji + plan (dla -ShowPlan).
    [CmdletBinding()]
    param(
        [Parameter(Mandatory)][string[]]$VmName,
        [Parameter(Mandatory)][string[]]$Phase,
        [Parameter(Mandatory)][string]$OutDirectory,
        [Parameter(Mandatory)][string]$BuildDirectory,
        [Parameter(Mandatory)][string]$CatalogPath,
        [Parameter(Mandatory)][string]$CheckpointName,
        [Parameter(Mandatory)][int]$ReadyTimeoutSeconds,
        [bool]$Network = $true
    )
    return [ordered]@{ VmName = @($VmName); Phases = @(Get-E4B1PhaseOrder -Phase $Phase -Network $Network); Network = $Network; OutDirectory = $OutDirectory; BuildDirectory = $BuildDirectory
        CatalogPath = $CatalogPath; CheckpointName = $CheckpointName; ReadyTimeoutSeconds = $ReadyTimeoutSeconds; Plan = (Get-E4B1Plan) }
}

function Invoke-E4B1VmPhase {
    # Jedna faza w VM przez otwartą sesję: definicje + Invoke-E4B1Collection; zwraca sparsowany dokument.
    [CmdletBinding()]
    param(
        [Parameter(Mandatory)][AllowNull()][System.Management.Automation.Runspaces.PSSession]$Session,
        [Parameter(Mandatory)][string]$RemoteScript,
        [Parameter(Mandatory)][string]$PlanJson,
        [Parameter(Mandatory)][string]$Phase,
        [Parameter(Mandatory)][string]$UserSid
    )
    Assert-DsoVmLiveAllowed
    $Remote = {
        param([string]$Definitions, [string]$Json, [string]$Which, [string]$Sid)
        . ([scriptblock]::Create($Definitions))
        Invoke-E4B1Collection -PlanJson $Json -Phase $Which -UserSid $Sid
    }
    $Raw = @(Invoke-Command -Session $Session -ScriptBlock $Remote -ArgumentList @($RemoteScript, $PlanJson, $Phase, $UserSid))
    $Text = [string]($Raw | Where-Object { $_ -is [string] } | Select-Object -Last 1)
    if ([string]::IsNullOrWhiteSpace($Text)) { throw "Faza '$Phase' nie zwróciła JSON." }
    return ($Text | ConvertFrom-Json)
}

function Restart-E4B1Vm {
    # Restart gościa po resecie sieci: Restart-Computer w VM, zamknięcie sesji, czekanie na zgaśnięcie heartbeatu, nowa sesja (zalogowany dso).
    [CmdletBinding()]
    param(
        [Parameter(Mandatory)][ValidateSet('DSO-26H2-Home', 'DSO-26H2-Pro')][string]$VmName,
        [Parameter(Mandatory)][AllowNull()][System.Management.Automation.Runspaces.PSSession]$Session,
        [Parameter(Mandatory)][pscredential]$Credential,
        [int]$ReadyTimeoutSeconds = 1200
    )
    Assert-DsoVmLiveAllowed
    # Zerwanie sesji przy restarcie bywa zgłaszane jako błąd Invoke-Command; restart potwierdza nowa sesja i LastBootUpTime w fazie netreset-after.
    try { Invoke-Command -Session $Session -ScriptBlock { Restart-Computer -Force } -ErrorAction Stop } catch { Write-Verbose "Restart-Computer: $($_.Exception.Message)" }
    Remove-PSSession -Session $Session -ErrorAction SilentlyContinue
    $null = Wait-VmHeartbeatDrop -VmName $VmName -TimeoutSeconds 180
    return (Wait-VmReady -VmName $VmName -Credential $Credential -TimeoutSeconds $ReadyTimeoutSeconds)
}

function Complete-E4B1VmCleanup {
    # Koniec przebiegu: revert do checkpointu, odłączenie karty i sprawdzenie kontraktu stanu końcowego: VM Off i karta bez przełącznika.
    [CmdletBinding()]
    param(
        [Parameter(Mandatory)][ValidateSet('DSO-26H2-Home', 'DSO-26H2-Pro')][string]$VmName,
        [Parameter(Mandatory)][string]$CheckpointName
    )
    $Out = [ordered]@{ Ok = $false; RevertError = $null; DisconnectError = $null; CheckError = $null; FinalState = $null; FinalSwitch = $null; Violations = @(); Problems = @() }
    try { Restore-VmCleanState -VmName $VmName -CheckpointName $CheckpointName }
    catch { $Out.RevertError = $_.Exception.Message; $Out.Problems += "revert do '$CheckpointName': $($_.Exception.Message)" }
    try { Disconnect-VMNetworkAdapter -VMName $VmName -ErrorAction Stop }
    catch { $Out.DisconnectError = $_.Exception.Message; $Out.Problems += "odłączenie sieci: $($_.Exception.Message)" }
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

function Invoke-E4B1VmRun {
    # Jedna VM: revert -> start (bez sieci) -> gotowość -> kopia buildu -> fazy (restart po netreset, sieć tylko na fazę network) -> kopia zrzutów
    # -> ZAWSZE revert, odłączenie karty i sprawdzenie stanu końcowego -> zapis JSON.
    [CmdletBinding()]
    [OutputType([string])]
    param(
        [Parameter(Mandatory)][ValidateSet('DSO-26H2-Home', 'DSO-26H2-Pro')][string]$VmName,
        [Parameter(Mandatory)][string]$OutDirectory,
        [Parameter(Mandatory)][string[]]$Phases,
        [Parameter(Mandatory)][string]$BuildDirectory,
        [Parameter(Mandatory)][string]$CatalogPath,
        [string]$CheckpointName = 'czysta-26H2',
        [int]$ReadyTimeoutSeconds = 1200
    )
    Assert-DsoVmLiveAllowed
    $Credential = Get-DsoVmCredential -VmName $VmName
    $Session = $null
    $StartedAt = Get-Date
    $Results = [ordered]@{}
    $Timeline = @()
    $UserSid = $null
    $PrimaryError = $null
    $VmDirectory = Join-Path $OutDirectory $VmName
    try {
        Restore-VmCleanState -VmName $VmName -CheckpointName $CheckpointName
        Start-VM -Name $VmName
        $Session = Wait-VmReady -VmName $VmName -Credential $Credential -TimeoutSeconds $ReadyTimeoutSeconds
        $UserSid = [string](Invoke-Command -Session $Session -ScriptBlock { (Get-LocalUser -Name 'dso').SID.Value })
        if ([string]::IsNullOrWhiteSpace($UserSid)) { throw "Nie udało się odczytać SID użytkownika 'dso' w VM '$VmName'." }
        # Chwila na dokończenie logowania dso (pasek zadań, Start, rejestracja pakietów) przed pierwszym odczytem.
        Start-Sleep -Seconds 45
        Copy-DsoBuildToVm -Session $Session -BuildDirectory $BuildDirectory -CatalogPath $CatalogPath -Layout (Get-DsoVmLayout)
        $RemoteScript = Get-E4B1RemoteScript
        $PlanJson = ConvertTo-Json -InputObject (Get-E4B1Plan) -Depth 6
        $NetworkPhases = @(Get-E4B1NetworkPhases)
        $LastNetwork = @($Phases | Where-Object { $NetworkPhases -contains $_ }) | Select-Object -Last 1
        $Connected = $false
        for ($Index = 0; $Index -lt $Phases.Count; $Index++) {
            $Phase = $Phases[$Index]
            if ($Phase -eq 'netreset-after') {
                $Session = Restart-E4B1Vm -VmName $VmName -Session $Session -Credential $Credential -ReadyTimeoutSeconds $ReadyTimeoutSeconds
                Start-Sleep -Seconds 45
            }
            if ($NetworkPhases -contains $Phase -and -not $Connected) {
                Connect-VMNetworkAdapter -VMName $VmName -SwitchName 'Default Switch' -ErrorAction Stop
                $Connected = $true
                $Timeline += "$(Get-Date -Format 'HH:mm:ss') sieć podłączona"
            }
            $Timeline += "$(Get-Date -Format 'HH:mm:ss') $Phase"
            Write-Host "$(Get-Date -Format 'HH:mm:ss') $VmName $Phase"
            try { $Results[$Phase] = Invoke-E4B1VmPhase -Session $Session -RemoteScript $RemoteScript -PlanJson $PlanJson -Phase $Phase -UserSid $UserSid }
            finally {
                if ($Connected -and ($Phase -eq $LastNetwork -or $Index -eq $Phases.Count - 1 -or $null -eq $LastNetwork)) {
                    try { Disconnect-VMNetworkAdapter -VMName $VmName -ErrorAction Stop; $Connected = $false; $Timeline += "$(Get-Date -Format 'HH:mm:ss') sieć odłączona" }
                    catch { $PrimaryError = (@($PrimaryError, "odłączenie sieci: $($_.Exception.Message)") | Where-Object { $_ }) -join ' | ' }
                }
            }
        }
    }
    catch {
        $PrimaryError = (@($PrimaryError, $_.Exception.Message) | Where-Object { $_ }) -join ' | '
        if (Test-VmAuthFailure -Exception $_.Exception) { throw }
    }
    finally {
        $Shots = Join-Path $VmDirectory 'shots'
        if ($null -ne $Session) {
            try {
                $null = New-Item -ItemType Directory -Path $Shots -Force
                # Copy-Item -FromSession zgłaszał OutOfMemoryException na PS Direct; pliki po kolei jako Base64 (zrzut ok. 100-500 KB).
                $Names = @(Invoke-Command -Session $Session -ScriptBlock { @(Get-ChildItem -LiteralPath 'C:\dso\e4b1\shots' -Filter '*.png' -ErrorAction SilentlyContinue | ForEach-Object { $_.Name }) })
                foreach ($ShotName in $Names) {
                    $Base64 = [string](Invoke-Command -Session $Session -ArgumentList @($ShotName) -ScriptBlock { param([string]$File) [Convert]::ToBase64String([System.IO.File]::ReadAllBytes("C:\dso\e4b1\shots\$File")) })
                    [System.IO.File]::WriteAllBytes((Join-Path $Shots $ShotName), [Convert]::FromBase64String($Base64))
                }
            }
            catch { $PrimaryError = (@($PrimaryError, "kopia zrzutów: $($_.Exception.Message)") | Where-Object { $_ }) -join ' | ' }
            Remove-PSSession -Session $Session -ErrorAction SilentlyContinue
        }
        # Sprzątanie PRZED zapisem, żeby jego wynik (także błędy) trafił do JSON; błąd sprzątania nigdy nie jest ukrywany.
        $Cleanup = Complete-E4B1VmCleanup -VmName $VmName -CheckpointName $CheckpointName
        if (-not $Cleanup.Ok) {
            $CleanupMessage = "sprzątanie VM: $(@($Cleanup.Problems) -join ' | ')"
            Write-Warning "VM '$VmName': $CleanupMessage"
            $PrimaryError = (@($PrimaryError, $CleanupMessage) | Where-Object { $_ }) -join ' | '
        }
        $Result = [ordered]@{ schema = 1; vmName = $VmName; checkpoint = $CheckpointName; userSid = $UserSid; startedAt = $StartedAt.ToString('o'); finishedAt = (Get-Date).ToString('o')
            error = $PrimaryError; cleanup = $Cleanup; timeline = $Timeline; shots = $Shots; phases = $Results }
        $Saved = Save-ExploreResult -Result $Result -OutDirectory $OutDirectory -VmName $VmName -FileName 'e4b1.json'
    }
    if ($PrimaryError) { throw "VM '$VmName': $PrimaryError (wynik częściowy: $Saved)" }
    return $Saved
}

function Invoke-E4B1Exploration {
    # Wszystkie wybrane VM po kolei. Błąd uwierzytelnienia = stop całości; inny błąd = zapis i dalej.
    [CmdletBinding()]
    param([Parameter(Mandatory)][System.Collections.IDictionary]$Arguments)
    $Paths = New-Object System.Collections.Generic.List[string]
    $Errors = New-Object System.Collections.Generic.List[string]
    foreach ($Vm in $Arguments.VmName) {
        try {
            Write-Host "$(Get-Date -Format 'HH:mm:ss') $Vm"
            $Paths.Add((Invoke-E4B1VmRun -VmName $Vm -OutDirectory $Arguments.OutDirectory -Phases $Arguments.Phases -BuildDirectory $Arguments.BuildDirectory -CatalogPath $Arguments.CatalogPath `
                        -CheckpointName $Arguments.CheckpointName -ReadyTimeoutSeconds $Arguments.ReadyTimeoutSeconds))
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
        $RepoRoot = Get-DsoRepoRoot
        $OutDirectory = Assert-E4B1OutDirectory -Path $OutDirectory -RepoRoot $RepoRoot
        $OutDirectory = Resolve-ExploreOutDirectory -Path $OutDirectory
        if (-not $BuildDirectory) { $BuildDirectory = Join-Path $RepoRoot 'src\DSOptimizer.App\bin\Release\net481' }
        $CatalogPath = Join-Path $VmRoot 'catalog\vm-test-catalog.json'
        $RunArguments = Get-E4B1RunArguments -VmName $VmName -Phase $Phase -OutDirectory $OutDirectory -BuildDirectory $BuildDirectory -CatalogPath $CatalogPath -CheckpointName $CheckpointName `
            -ReadyTimeoutSeconds $ReadyTimeoutSeconds -Network (-not $NoNetwork)
        if ($ShowPlan) {
            [pscustomobject]$RunArguments | ConvertTo-Json -Depth 6
            exit 0
        }
        if (-not (Test-Path -LiteralPath (Join-Path $BuildDirectory 'DSOptimizer.exe'))) { throw "Brak DSOptimizer.exe w '$BuildDirectory' (najpierw dotnet build DSOptimizer.sln -c Release)." }
        Import-Module (Join-Path $VmRoot 'modules\VmCommon.psm1') -Force
        Import-Module (Join-Path $VmRoot 'modules\VmHost.psm1') -Force
        # ExploreCommon (zaimportowany wyżej) trzyma referencję do starej instancji VmHost; ponowny import po nim wiąże go z bieżącą.
        Import-Module (Join-Path $PSScriptRoot 'ExploreCommon.psm1') -Force
        $Principal = New-Object System.Security.Principal.WindowsPrincipal([System.Security.Principal.WindowsIdentity]::GetCurrent())
        if (-not $Principal.IsInRole([System.Security.Principal.WindowsBuiltInRole]::Administrator)) {
            throw 'Uruchom skrypt w sesji PowerShell jako administrator (Hyper-V i PowerShell Direct tego wymagają).'
        }
        Write-Host "Wyniki: $OutDirectory"
        Write-Host "VM: $($RunArguments.VmName -join ', '); fazy: $($RunArguments.Phases -join ', ')"
        $Outcome = Invoke-E4B1Exploration -Arguments $RunArguments
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
