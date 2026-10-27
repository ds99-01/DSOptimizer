<#
Instrukcja uruchomienia:
    Sesja PowerShell uruchomiona jako administrator, na hoście z Hyper-V, z modułami testów VM (tests\vm\modules) i tests\vm\explore\ExploreCommon.psm1.
    VM utworzone wcześniej przez New-TestVm.ps1 (checkpoint 'czysta-26H2', poświadczenia w %LOCALAPPDATA%\DSOptimizer-vm, autologon 'dso').
    Obie VM, po kolei (Home, potem Pro):
        .\tests\vm\explore\Invoke-C5Exploration.ps1 -OutDirectory C:\ścieżka\poza\repo\exploration
    Jedna VM, wybrane sondy:
        .\tests\vm\explore\Invoke-C5Exploration.ps1 -VmName DSO-26H2-Pro -Probe panels,explorer -OutDirectory C:\ścieżka\poza\repo\exploration
    -OutDirectory jest wymagany: ścieżka bezwzględna (dysk albo UNC) poza repo; w repo tylko katalog ignorowany przez git.
    Czas: ok. 25-35 minut na VM (panele 2 x 30 wariantów po ok. 13 s, Store, Explorer, start i revert VM).
    Tylko sonda uzupełniająca paneli (3 warianty, K1 i K2): ok. 5 minut na VM.
    Bez trybu ręcznego (ruling R9): oba konteksty (K1, K2) automatycznie; potwierdzenie wzrokowe okien idzie do smoke z Dawidem.
Opis działania:
    Skrypt zbiera dowody z prawdziwej VM 26H2 dla partii C5 (panele systemowe, restart Explorera, ponowna rejestracja Store, launcher WinUtil).
    Zapisuje FAKTY (surowe wartości), bez interpretacji. Wszystko dzieje się WYŁĄCZNIE wewnątrz VM (na hoście nic się nie uruchamia ani nie zmienia),
    a VM wraca do checkpointu 'czysta-26H2' przed i po przebiegu (zatrzymany Explorer, rejestracja Appx i zadania Harmonogramu znikają razem z nim).
    Konteksty:
      K1 - PowerShell Direct jako wbudowany Administrator (tak działa runner testów VM).
      K2 - sesja interaktywna 'dso' (konto z unattend.xml, autologon): zadanie Harmonogramu New-ScheduledTaskPrincipal -UserId dso
           -LogonType Interactive -RunLevel Highest (MS: "User must already be logged on"), skrypt zadania w C:\dso\c5 zapisuje wynik JSON,
           K1 go czyta i ZAWSZE wyrejestrowuje zadanie. Okna najwyższego poziomu (EnumWindows, GetWindowText, GetClassName,
           GetWindowThreadProcessId przez Add-Type) tylko w K2, bo K1 nie widzi pulpitu dso. Gdy K2 nie ruszy (dso niezalogowany,
           brak wyniku w limicie), rekord ma Available=false i powód "K2 niedostępne: ...", a pozostałe sondy idą dalej.
    Sondy (numery = "Otwarte pytania dla eksploracji VM" w docs\zrodla\2026-10-19-partia-c5-zrodla.md), kolejność stała:
      panels   (1)-(4) - 14 pozycji WinUtil (config/feature.json @ 144d516) + warianty MS (control.exe /name, netconnections,
                         SystemPropertiesPerformance.exe) i v3.2 (sysdm.cpl,,3, control powercfg.cpl) oraz sonda uzupełniająca R20/R21 (CreateProcess jak
                         launcher DSOptimizer: control.exe wscui.cpl, control.exe sysdm.cpl po zamknięciu SystemPropertiesAdvanced.exe z tego przebiegu,
                         %SystemRoot%\explorer.exe shell:::{CLSID} z tokenami nowych explorer.exe i właścicieli okien), razem 30, w K1 i w K2:
                         uruchomienie (Start-Process = ShellExecute, plik z System32), odczyt po 3 s i po 10 s (nowe procesy: nazwa, PID,
                         CommandLine, SessionId, właściciel; w K2 nowe okna: tytuł, klasa, PID; SystemSettings/ApplicationFrameHost),
                         potem zamknięcie TYLKO nowych procesów. Stan ochrony systemu (punkty przywracania, klucz SystemRestore) tylko odczyt.
      winutil  (12)    - warunek: Get-NetConnectionProfile w VM bez żadnego profilu (inaczej "pominięte: VM ma sieć", nic nie startuje).
                         powershell.exe -NoExit -Command z poleceniem z README (dwa warianty cytowania), CommandLine z Win32_Process,
                         czy żyje po 10 s, okna w K2; zamknięcie tylko tego procesu i jego potomków.
      store    (8)-(11)- Get-AppxPackage (-AllUsers i bieżące konto, K1 i K2) | Where-Object Name -like '*Store*': nazwa, manifest,
                         Status, PackageUserInformation; Add-AppxPackage -Path <InstallLocation>\AppxManifest.xml -Register
                         -DisableDevelopmentMode (MS, Add-AppxPackage, przykład 3) w K1 i w K2 z odczytem przed/po; konto Administrator
                         w K1 jako naturalna ścieżka "brak pakietu"; WSReset.exe -i tylko kod wyjścia i komunikat (limit 60 s, sprzątanie).
      explorer (5)-(7) - AutoRestartShell (HKLM\...\Winlogon, tylko odczyt, MS-archiwum); token Explorera po logowaniu; Stop-Process -Force
                         i taskkill /f /pid na powłoce dso (K1), Stop-Process -Force z K2; po każdym: pętla 30 s co 0,5 s do nowego
                         explorer.exe w sesji dso (czas, PID, właściciel, SessionId, CommandLine, TokenElevation/TokenElevationType;
                         w K2 także czas do Shell_TrayWnd); wariant v3.2 (Start-Process explorer.exe z K1 i z K2) - tylko odczyt tokena
                         nowego procesu, potem zamknięcie TYLKO jego. Explorer jest ostatni, bo restartuje pulpit.
    Sprzątanie paneli (decyzja wykonawcy): Stop-Process tylko dla NOWYCH procesów, które są potomkami uruchomionego albo mają nazwę hosta panelu
    (control, rundll32, mmc, SystemPropertiesPerformance, rstrui, SystemSettings) w tej samej sesji; explorer.exe NIGDY. Okno panelu przekazane
    do Explorera (control.exe często tak robi) jest zamykane przez WM_CLOSE (PostMessage) tylko gdy jest NOWE, widoczne i ma klasę CabinetWClass;
    reszta zostaje i jest zapisana (LeftProcesses), bo VM i tak wraca do checkpointu. WM_CLOSE jest asynchroniczne: po nim pętla do 5 s co 250 ms,
    aż uchwyt zniknie (inaczej WindowsStillOpen), a każdy odczyt 3 s/10 s zapisuje też WSZYSTKIE widoczne okna CabinetWClass i
    ApplicationFrameWindow (ShellWindows), żeby ponowne użycie istniejącego okna było widać mimo braku nowego uchwytu.
    Ostatni bezpiecznik po stronie VM: Assert-C5InsideVm (nazwa komputera DSO-26H2-HOME/PRO z New-TestVm i brak DSO_VM_TESTS_MOCK_ONLY) na
    początku każdej sondy i każdej funkcji ze skutkiem - na hoście i w testach na mockach nic nie ruszy.
    Każda sonda i każda jej część w try/catch: błąd jednej trafia do wyniku, nie przerywa reszty. Pierwszy błąd uwierzytelnienia przerywa cały
    skrypt (bez prób na drugiej VM, blokada konta). Inny błąd jednej VM jest zapisany i nie przerywa drugiej.
    Wynik: <OutDirectory>\<VmName>\c5-exploration.json (UTF-8). Wybór sond: -Probe panels,winutil,store,explorer; bez -Probe wszystkie.
    -Variant: filtr wariantów sondy panels (np. -Probe panels -Variant security-maintenance-control,system-properties-control,devices-printers-explorer);
    wymaga sondy panels; bez -Variant wszystkie 30 wariantów.
    -ShowPlan: wypisuje rozstrzygnięte argumenty (VM, katalog, checkpoint, limit, sondy, warianty) jako JSON i kończy, nic nie uruchamia.
    Skrypt dotyka wyłącznie VM DSO-26H2-Home i DSO-26H2-Pro (ValidateSet), nigdy Bruteforce_LTSC.
    Kod wyjścia: 0 gdy wszystkie wybrane VM zebrane bez błędu, 1 w przeciwnym razie.
#>
[CmdletBinding()]
param(
    [ValidateSet('DSO-26H2-Home', 'DSO-26H2-Pro')][string[]]$VmName = @('DSO-26H2-Home', 'DSO-26H2-Pro'),
    [string]$OutDirectory,
    [string]$CheckpointName = 'czysta-26H2',
    [ValidateRange(60, 7200)][int]$ReadyTimeoutSeconds = 1200,
    [ValidateSet('panels', 'winutil', 'store', 'explorer')][string[]]$Probe,
    # Filtr wariantów sondy panels (nazwy = <pozycja bez "panels.">-<wariant>, lista w Get-C5PanelPlan); bez -Variant wszystkie.
    [ValidateSet('visual-effects-ms-exe', 'visual-effects-v32-sysdm-3', 'power-options-winutil', 'power-options-v32-control', 'power-options-ms-name',
        'network-connections-winutil', 'network-connections-ms-netconnections', 'computer-management-winutil', 'control-panel-winutil', 'mouse-winutil',
        'mouse-ms-name', 'devices-printers-winutil', 'devices-printers-ms-name', 'devices-printers-explorer', 'programs-features-winutil',
        'programs-features-ms-name', 'region-winutil', 'region-ms-name', 'security-maintenance-winutil', 'security-maintenance-control', 'sound-winutil',
        'sound-ms-name', 'system-properties-winutil', 'system-properties-ms-name', 'system-properties-control', 'date-time-winutil', 'date-time-ms-name',
        'firewall-winutil', 'firewall-ms-name', 'system-restore-winutil')][string[]]$Variant,
    # Tylko wypisuje (JSON) argumenty przebiegu po walidacji i kończy z kodem 0: bez VM, bez sprawdzania uprawnień (diagnostyka i testy).
    [switch]$ShowPlan
)

# Wspólna orkiestracja i narzędzia odczytu (rejestr, program natywny) są w ExploreCommon.psm1.
Import-Module (Join-Path $PSScriptRoot 'ExploreCommon.psm1') -Force

# ---------------------------------------------------------------------------------------------------------------------
# Funkcje wysyłane do VM (samowystarczalne: wołają tylko siebie nawzajem, funkcje Explore z modułu i cmdlety systemowe).
# Lista w Get-C5RemoteFunctionNames; podzbiór dla skryptu zadania K2 w Get-C5ChildFunctionNames.
# ---------------------------------------------------------------------------------------------------------------------

function Test-C5VmComputerName {
    # Nazwy komputerów VM nadawane przez New-TestVm.ps1 (VmBuild: nazwa VM wielkimi literami).
    [CmdletBinding()]
    [OutputType([bool])]
    param([AllowEmptyString()][string]$ComputerName)
    return (@('DSO-26H2-HOME', 'DSO-26H2-PRO') -contains ([string]$ComputerName).ToUpperInvariant())
}

function Assert-C5InsideVm {
    # Ostatni bezpiecznik: sondy C5 zatrzymują Explorera, rejestrują Appx, tworzą zadania i otwierają panele, więc działają WYŁĄCZNIE w VM
    # DSO-26H2-Home/Pro i nigdy w testach na mockach (zmienna DSO_VM_TESTS_MOCK_ONLY ustawiana przez tests\vm\tests\Support\NoLiveVm.ps1).
    [CmdletBinding()]
    param([string]$ComputerName = $env:COMPUTERNAME)
    if ($env:DSO_VM_TESTS_MOCK_ONLY) {
        throw 'Tryb DSO_VM_TESTS_MOCK_ONLY: sondy C5 nie mogą działać w testach na mockach (nic nie zostało uruchomione).'
    }
    if (-not (Test-C5VmComputerName -ComputerName $ComputerName)) {
        throw "Sondy C5 działają tylko w VM DSO-26H2-HOME/DSO-26H2-PRO (ten komputer: '$ComputerName'); nic nie zostało uruchomione."
    }
}

function Get-C5NativeSource {
    # P/Invoke (C# 5, kompilator Add-Type z PowerShell 5.1). Stałe i funkcje wg MS Learn (t3-ms-sources.md):
    # OpenProcess(PROCESS_QUERY_LIMITED_INFORMATION = 0x1000) -> OpenProcessToken(TOKEN_QUERY = 0x0008) -> GetTokenInformation
    # (TokenElevationType = 18, TokenElevation = 20); okna: EnumWindows, GetWindowText, GetClassName, GetWindowThreadProcessId,
    # IsWindowVisible; zamknięcie okna: PostMessage(WM_CLOSE = 0x0010).
    [CmdletBinding()]
    [OutputType([string])]
    param()
    return @'
using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Text;

namespace DsoC5
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
        [DllImport("user32.dll", SetLastError = true)]
        private static extern bool PostMessage(IntPtr hWnd, uint Msg, IntPtr wParam, IntPtr lParam);
        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern IntPtr OpenProcess(uint dwDesiredAccess, bool bInheritHandle, uint dwProcessId);
        [DllImport("advapi32.dll", SetLastError = true)]
        private static extern bool OpenProcessToken(IntPtr ProcessHandle, uint DesiredAccess, out IntPtr TokenHandle);
        [DllImport("advapi32.dll", SetLastError = true)]
        private static extern bool GetTokenInformation(IntPtr TokenHandle, int TokenInformationClass, out int TokenInformation, int TokenInformationLength, out int ReturnLength);
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

        public static bool CloseWindow(long handle)
        {
            return PostMessage(new IntPtr(handle), 0x0010, IntPtr.Zero, IntPtr.Zero);
        }

        // Wynik: [0] TokenIsElevated (0/1, -1 = brak odczytu), [1] TokenElevationType (-1 = brak odczytu), [2] ostatni błąd Win32 (0 = brak).
        public static int[] QueryElevation(uint processId)
        {
            int[] result = new int[] { -1, -1, 0 };
            IntPtr process = OpenProcess(0x1000, false, processId);
            if (process == IntPtr.Zero)
            {
                result[2] = Marshal.GetLastWin32Error();
                return result;
            }
            try
            {
                IntPtr token;
                if (!OpenProcessToken(process, 0x0008, out token))
                {
                    result[2] = Marshal.GetLastWin32Error();
                    return result;
                }
                try
                {
                    int value;
                    int length;
                    if (GetTokenInformation(token, 20, out value, 4, out length)) { result[0] = value; } else { result[2] = Marshal.GetLastWin32Error(); }
                    if (GetTokenInformation(token, 18, out value, 4, out length)) { result[1] = value; } else { result[2] = Marshal.GetLastWin32Error(); }
                }
                finally
                {
                    CloseHandle(token);
                }
            }
            finally
            {
                CloseHandle(process);
            }
            return result;
        }
    }
}
'@
}

function Initialize-C5Native {
    # Kompilacja typu DsoC5.Native raz na proces (tylko w VM).
    [CmdletBinding()]
    param()
    Assert-C5InsideVm
    if (-not ('DsoC5.Native' -as [type])) { Add-Type -TypeDefinition (Get-C5NativeSource) -Language CSharp }
}

function Invoke-C5ElevationQuery {
    # Natywny odczyt tokena procesu: (TokenIsElevated, TokenElevationType, błąd Win32).
    [CmdletBinding()]
    param([Parameter(Mandatory)][int]$ProcessId)
    Initialize-C5Native
    return , ([DsoC5.Native]::QueryElevation([uint32]$ProcessId))
}

function Get-C5ProcessTokenInfo {
    # Poziom podniesienia tokena procesu (MS: TOKEN_ELEVATION.TokenIsElevated, TOKEN_ELEVATION_TYPE Default=1, Full, Limited).
    [CmdletBinding()]
    param([Parameter(Mandatory)][int]$ProcessId)
    $Info = [ordered]@{ ProcessId = $ProcessId; Elevated = $null; ElevationType = $null; ElevationTypeName = $null; Win32Error = $null; Error = $null }
    try {
        # Bez @(): funkcja zwraca jedną tablicę int[] (return ,), a @() owinąłby ją w tablicę jednoelementową.
        $Raw = Invoke-C5ElevationQuery -ProcessId $ProcessId
        if ([int]$Raw[0] -ge 0) { $Info.Elevated = ([int]$Raw[0] -ne 0) }
        if ([int]$Raw[1] -ge 0) {
            $Info.ElevationType = [int]$Raw[1]
            $Info.ElevationTypeName = switch ([int]$Raw[1]) { 1 { 'Default' } 2 { 'Full' } 3 { 'Limited' } default { "nieznany ($($Raw[1]))" } }
        }
        if ([int]$Raw[2] -ne 0) { $Info.Win32Error = [int]$Raw[2] }
    }
    catch { $Info.Error = $_.Exception.Message }
    return [pscustomobject]$Info
}

function Get-C5CurrentSessionId {
    [CmdletBinding()]
    [OutputType([int])]
    param()
    return [int](Get-Process -Id $PID).SessionId
}

function Get-C5SelfTokenInfo {
    # Token procesu sondy (K1 albo zadanie K2): konto, SID, rola administratora, sesja i poziom podniesienia.
    [CmdletBinding()]
    param()
    $Info = [ordered]@{ ProcessId = $PID; UserName = $null; UserSid = $null; IsInRoleAdministrator = $null; SessionId = $null; Token = $null; Error = $null }
    try {
        $Identity = [System.Security.Principal.WindowsIdentity]::GetCurrent()
        $Principal = New-Object System.Security.Principal.WindowsPrincipal($Identity)
        $Info.UserName = [string]$Identity.Name
        $Info.UserSid = [string]$Identity.User.Value
        $Info.IsInRoleAdministrator = [bool]$Principal.IsInRole([System.Security.Principal.WindowsBuiltInRole]::Administrator)
        $Info.SessionId = Get-C5CurrentSessionId
        $Info.Token = Get-C5ProcessTokenInfo -ProcessId $PID
    }
    catch { $Info.Error = $_.Exception.Message }
    return [pscustomobject]$Info
}

# --- Procesy ---

function Get-C5ProcessSnapshot {
    # Migawka procesów (Win32_Process): klucz PID|CreationDate odróżnia ponownie użyty PID.
    [CmdletBinding()]
    param()
    foreach ($Item in @(Get-CimInstance -ClassName Win32_Process -ErrorAction Stop)) {
        $Created = $null
        if ($null -ne $Item.CreationDate) { $Created = ([datetime]$Item.CreationDate).ToString('o') }
        [pscustomobject]@{
            Key = "$($Item.ProcessId)|$Created"; Name = [string]$Item.Name; ProcessId = [int]$Item.ProcessId; ParentProcessId = [int]$Item.ParentProcessId
            SessionId = [int]$Item.SessionId; CommandLine = [string]$Item.CommandLine; CreationDate = $Created
        }
    }
}

function Get-C5NewProcesses {
    # Procesy z migawki "po", których nie ma w migawce "przed" (po kluczu PID|CreationDate).
    [CmdletBinding()]
    param(
        [AllowEmptyCollection()][object[]]$Before = @(),
        [AllowEmptyCollection()][object[]]$After = @()
    )
    $Known = New-Object System.Collections.Generic.HashSet[string]
    foreach ($Entry in $Before) { [void]$Known.Add([string]$Entry.Key) }
    return @($After | Where-Object { $null -ne $_ -and -not $Known.Contains([string]$_.Key) })
}

function Get-C5ProcessOwner {
    # Właściciel procesu przez Win32_Process.GetOwner (MS Learn); SID przez NTAccount.Translate. Błąd jako dana.
    [CmdletBinding()]
    param([Parameter(Mandatory)][int]$ProcessId)
    $Info = [ordered]@{ User = $null; Domain = $null; Owner = $null; OwnerSid = $null; Error = $null }
    try {
        $Cim = @(Get-CimInstance -ClassName Win32_Process -Filter "ProcessId=$ProcessId" -ErrorAction Stop) | Select-Object -First 1
        if ($null -eq $Cim) { $Info.Error = 'proces już nie istnieje' }
        else {
            $Owner = Invoke-CimMethod -InputObject $Cim -MethodName GetOwner -ErrorAction Stop
            if ([int]$Owner.ReturnValue -eq 0) {
                $Info.User = [string]$Owner.User
                $Info.Domain = [string]$Owner.Domain
                $Info.Owner = "$($Owner.Domain)\$($Owner.User)"
                try { $Info.OwnerSid = (New-Object System.Security.Principal.NTAccount($Owner.Domain, $Owner.User)).Translate([System.Security.Principal.SecurityIdentifier]).Value }
                catch { $Info.OwnerSid = $null }
            }
            else { $Info.Error = "GetOwner ReturnValue=$($Owner.ReturnValue)" }
        }
    }
    catch { $Info.Error = $_.Exception.Message }
    return [pscustomobject]$Info
}

function Get-C5ParentName {
    # Nazwa rodzica z TEJ SAMEJ migawki (pytanie 6: "przez winlogon"). Rodzic młodszy niż dziecko = ponownie użyty PID => $null; rodzic,
    # który już się zakończył (np. userinit), też daje $null.
    [CmdletBinding()]
    param(
        [Parameter(Mandatory)]$Entry,
        [AllowEmptyCollection()][object[]]$Snapshot = @()
    )
    $Parent = @($Snapshot | Where-Object { [int]$_.ProcessId -eq [int]$Entry.ParentProcessId }) | Select-Object -First 1
    if ($null -eq $Parent) { return $null }
    if ($Parent.CreationDate -and $Entry.CreationDate -and ([datetime]$Parent.CreationDate) -gt ([datetime]$Entry.CreationDate)) { return $null }
    return [string]$Parent.Name
}

function Get-C5ProcessDetail {
    # Wpis migawki + właściciel (bez tokena) + nazwa rodzica (z podanej migawki albo z pola ParentName wpisu).
    [CmdletBinding()]
    param(
        [Parameter(Mandatory)]$Entry,
        [AllowEmptyCollection()][object[]]$Snapshot
    )
    $Owner = Get-C5ProcessOwner -ProcessId ([int]$Entry.ProcessId)
    $ParentName = $null
    if ($PSBoundParameters.ContainsKey('Snapshot')) { $ParentName = Get-C5ParentName -Entry $Entry -Snapshot $Snapshot }
    elseif ($null -ne $Entry.PSObject.Properties['ParentName']) { $ParentName = $Entry.ParentName }
    return [pscustomobject]@{
        Name = $Entry.Name; ProcessId = $Entry.ProcessId; ParentProcessId = $Entry.ParentProcessId; ParentName = $ParentName; SessionId = $Entry.SessionId
        CommandLine = $Entry.CommandLine; CreationDate = $Entry.CreationDate; Owner = $Owner.Owner; OwnerUser = $Owner.User; OwnerSid = $Owner.OwnerSid; OwnerError = $Owner.Error
    }
}

function Get-C5ShellDetail {
    # Explorer: wpis + właściciel + rodzic + token (TokenElevation/TokenElevationType).
    [CmdletBinding()]
    param(
        [Parameter(Mandatory)]$Entry,
        [AllowEmptyCollection()][object[]]$Snapshot
    )
    $Detail = if ($PSBoundParameters.ContainsKey('Snapshot')) { Get-C5ProcessDetail -Entry $Entry -Snapshot $Snapshot } else { Get-C5ProcessDetail -Entry $Entry }
    $Detail | Add-Member -MemberType NoteProperty -Name Token -Value (Get-C5ProcessTokenInfo -ProcessId ([int]$Entry.ProcessId))
    return $Detail
}

function Get-C5SettingsHosts {
    # SystemSettings.exe / ApplicationFrameHost.exe obecne w migawce, z flagą IsNew (przekierowanie do Ustawień, decyzja D2 = tylko odnotować).
    [CmdletBinding()]
    param(
        [AllowEmptyCollection()][object[]]$After = @(),
        [AllowEmptyCollection()][object[]]$NewProcesses = @()
    )
    $NewKeys = @($NewProcesses | ForEach-Object { [string]$_.Key })
    return @($After | Where-Object { @('SystemSettings.exe', 'ApplicationFrameHost.exe') -contains $_.Name } | ForEach-Object {
            [pscustomobject]@{ Name = $_.Name; ProcessId = $_.ProcessId; SessionId = $_.SessionId; IsNew = ($NewKeys -contains [string]$_.Key) }
        })
}

function Select-C5CleanupTargets {
    # Cele sprzątania wyłącznie spośród NOWYCH procesów: potomkowie uruchomionego PID oraz procesy o nazwie z listy dozwolonych w tej samej sesji.
    # Nigdy explorer.exe (control.exe przekazuje okna do Explorera; zatrzymanie go to sonda Explorera, nie sprzątanie) ani procesy chronione.
    [CmdletBinding()]
    param(
        [AllowEmptyCollection()][object[]]$NewProcesses = @(),
        [int]$LaunchedPid = 0,
        [AllowEmptyCollection()][string[]]$AllowNames = @(),
        [Parameter(Mandatory)][int]$SessionId,
        [AllowEmptyCollection()][int[]]$ProtectedPids = @()
    )
    $Selected = @{}
    $Roots = New-Object System.Collections.Generic.HashSet[int]
    if ($LaunchedPid -gt 0) {
        $Changed = $true
        while ($Changed) {
            $Changed = $false
            foreach ($Process in $NewProcesses) {
                if ($Selected.ContainsKey([string]$Process.Key)) { continue }
                if ([int]$Process.ProcessId -eq $LaunchedPid -or $Roots.Contains([int]$Process.ParentProcessId)) {
                    $Selected[[string]$Process.Key] = $Process
                    [void]$Roots.Add([int]$Process.ProcessId)
                    $Changed = $true
                }
            }
        }
    }
    foreach ($Process in $NewProcesses) {
        if ($AllowNames -contains [string]$Process.Name -and [int]$Process.SessionId -eq $SessionId) { $Selected[[string]$Process.Key] = $Process }
    }
    return @($Selected.Values | Where-Object { [string]$_.Name -ne 'explorer.exe' -and $ProtectedPids -notcontains [int]$_.ProcessId } | Sort-Object -Property ProcessId)
}

function Select-C5ShellPid {
    # Powłoka spośród explorer.exe użytkownika: pierwsza bez "/factory" w linii poleceń (procesy /factory,{…} -Embedding to serwery COM,
    # a heurystyka "najstarszy" zawiodła w przebiegu 2026-10-19); bez takiej - pierwsza z listy; pusta lista => $null.
    [CmdletBinding()]
    param([AllowEmptyCollection()][object[]]$Shells = @())
    $Plain = @($Shells | Where-Object { [string]$_.CommandLine -notmatch '/factory' })
    if ($Plain.Count -gt 0) { return $Plain[0] }
    if (@($Shells).Count -gt 0) { return @($Shells)[0] }
    return $null
}

function Select-C5NewExplorerTargets {
    # Wariant v3.2: do zamknięcia tylko NOWE explorer.exe, które są uruchomionym procesem albo jego potomkami, ALBO leżą poza sesją powłoki
    # dso, ALBO mają innego właściciela. Nowy explorer.exe dso w sesji powłoki, który nie pochodzi od nas, może być nową powłoką (winlogon) -
    # nigdy nie jest celem; tak samo powłoka (ShellPid).
    [CmdletBinding()]
    param(
        [AllowEmptyCollection()][object[]]$NewProcesses = @(),
        [Parameter(Mandatory)][int]$ShellPid,
        [Parameter(Mandatory)][int]$ShellSessionId,
        [int]$LaunchedPid = 0,
        [hashtable]$OwnerUserByPid = @{},
        [string]$UserName = 'dso'
    )
    $Descendants = New-Object System.Collections.Generic.HashSet[int]
    if ($LaunchedPid -gt 0) {
        [void]$Descendants.Add($LaunchedPid)
        $Changed = $true
        while ($Changed) {
            $Changed = $false
            foreach ($Process in $NewProcesses) {
                if (-not $Descendants.Contains([int]$Process.ProcessId) -and $Descendants.Contains([int]$Process.ParentProcessId)) { [void]$Descendants.Add([int]$Process.ProcessId); $Changed = $true }
            }
        }
    }
    return @($NewProcesses | Where-Object {
            if ([string]$_.Name -ne 'explorer.exe' -or [int]$_.ProcessId -eq $ShellPid) { return $false }
            $Ours = $Descendants.Contains([int]$_.ProcessId)
            # Bez znanej powłoki dso (ShellSessionId < 0) celem są tylko nasze procesy: obcy nowy explorer.exe może być odradzaną powłoką.
            if ($ShellSessionId -lt 0) { return $Ours }
            $OtherSession = [int]$_.SessionId -ne $ShellSessionId
            $OtherOwner = $OwnerUserByPid.ContainsKey([int]$_.ProcessId) -and [string]$OwnerUserByPid[[int]$_.ProcessId] -ne $UserName
            $Ours -or $OtherSession -or $OtherOwner
        } | Sort-Object -Property ProcessId)
}

# --- Okna (tylko K2: sesja dso) ---

function Get-C5WindowList {
    # Wszystkie okna najwyższego poziomu bieżącego pulpitu.
    [CmdletBinding()]
    param()
    Initialize-C5Native
    return @([DsoC5.Native]::ListTopLevelWindows() | ForEach-Object {
            [pscustomobject]@{ Handle = [long]$_.Handle; Title = [string]$_.Title; ClassName = [string]$_.ClassName; ProcessId = [int]$_.ProcessId; Visible = [bool]$_.Visible }
        })
}

function Close-C5Window {
    # WM_CLOSE przez PostMessage (bez czekania): uprzejme zamknięcie okna panelu, które Explorer przejął od control.exe.
    [CmdletBinding()]
    param([Parameter(Mandatory)][long]$Handle)
    Assert-C5InsideVm
    Initialize-C5Native
    return [DsoC5.Native]::CloseWindow($Handle)
}

function Select-C5WindowsToClose {
    # Tylko NOWE, widoczne okna klasy CabinetWClass należące do explorer.exe (okna Eksploratora/Panelu sterowania); okna powłoki
    # (Shell_TrayWnd, Progman, WorkerW), ukryte okna pomocnicze i okna innych procesów zostają.
    [CmdletBinding()]
    param(
        [AllowEmptyCollection()][object[]]$Before = @(),
        [AllowEmptyCollection()][object[]]$Now = @(),
        [AllowEmptyCollection()][int[]]$ExplorerPids = @()
    )
    $Old = New-Object System.Collections.Generic.HashSet[long]
    foreach ($Window in $Before) { [void]$Old.Add([long]$Window.Handle) }
    return @($Now | Where-Object { -not $Old.Contains([long]$_.Handle) -and [bool]$_.Visible -and [string]$_.ClassName -eq 'CabinetWClass' -and $ExplorerPids -contains [int]$_.ProcessId })
}

function Wait-C5WindowsClosed {
    # WM_CLOSE jest asynchroniczne: pętla z limitem (domyślnie 5 s co 250 ms), aż uchwyty znikną z listy okien. Zwraca uchwyty nadal otwarte.
    [CmdletBinding()]
    param(
        [AllowEmptyCollection()][long[]]$Handles = @(),
        [int]$TimeoutMilliseconds = 5000,
        [int]$StepMilliseconds = 250
    )
    if (@($Handles).Count -eq 0) { return @() }
    $Remaining = @($Handles)
    $MaxPolls = [int][Math]::Ceiling($TimeoutMilliseconds / $StepMilliseconds)
    for ($Poll = 1; $Poll -le $MaxPolls; $Poll++) {
        $Present = New-Object System.Collections.Generic.HashSet[long]
        foreach ($Window in @(Get-C5WindowList)) { [void]$Present.Add([long]$Window.Handle) }
        $Remaining = @($Remaining | Where-Object { $Present.Contains([long]$_) })
        if ($Remaining.Count -eq 0) { break }
        if ($Poll -lt $MaxPolls) { Start-Sleep -Milliseconds $StepMilliseconds }
    }
    return @($Remaining)
}

function Select-C5ShellWindows {
    # WSZYSTKIE widoczne okna CabinetWClass (Eksplorator/Panel sterowania) i ApplicationFrameWindow (Ustawienia), także stare: ponowne
    # użycie istniejącego okna (ta sama lokalizacja, karta) nie daje nowego uchwytu, a tytuł pokazuje, co jest otwarte.
    [CmdletBinding()]
    param([AllowEmptyCollection()][object[]]$Now = @())
    return @($Now | Where-Object { [bool]$_.Visible -and @('CabinetWClass', 'ApplicationFrameWindow') -contains [string]$_.ClassName } | ForEach-Object {
            [pscustomobject]@{ Handle = $_.Handle; Title = $_.Title; ClassName = $_.ClassName; ProcessId = $_.ProcessId }
        })
}

function Get-C5NewWindows {
    # Nowe widoczne okna (z nazwą procesu z migawki) i liczba nowych ukrytych.
    [CmdletBinding()]
    param(
        [AllowEmptyCollection()][object[]]$Before = @(),
        [AllowEmptyCollection()][object[]]$Now = @(),
        [AllowEmptyCollection()][object[]]$Processes = @()
    )
    $Old = New-Object System.Collections.Generic.HashSet[long]
    foreach ($Window in $Before) { [void]$Old.Add([long]$Window.Handle) }
    $Names = @{}
    foreach ($Process in $Processes) { $Names[[int]$Process.ProcessId] = [string]$Process.Name }
    $Fresh = @($Now | Where-Object { -not $Old.Contains([long]$_.Handle) })
    $Visible = @($Fresh | Where-Object { [bool]$_.Visible } | ForEach-Object {
            [pscustomobject]@{ Handle = $_.Handle; Title = $_.Title; ClassName = $_.ClassName; ProcessId = $_.ProcessId; ProcessName = $Names[[int]$_.ProcessId] }
        })
    return [pscustomobject]@{ Visible = $Visible; HiddenCount = @($Fresh | Where-Object { -not [bool]$_.Visible }).Count }
}

# --- Uruchomienie, obserwacja, sprzątanie ---

function Resolve-C5SystemFile {
    # Nazwa pliku z System32 (jak launcher z Tasku 4: nigdy katalog roboczy ani PATH) albo shell:::{CLSID} bez zmian. Ścieżki i separatory odrzucane.
    [CmdletBinding()]
    [OutputType([string])]
    param(
        [Parameter(Mandatory)][string]$Name,
        # explorer.exe leży w %SystemRoot%, nie w System32 (t3-vm-results, "Mechanizm shell:::").
        [ValidateSet('System32', 'SystemRoot')][string]$Root = 'System32'
    )
    if ($Name -match '^shell:::\{[0-9A-Fa-f]{8}-[0-9A-Fa-f]{4}-[0-9A-Fa-f]{4}-[0-9A-Fa-f]{4}-[0-9A-Fa-f]{12}\}\z') { return $Name }
    if ($Name -match '^[A-Za-z0-9]+\.(exe|cpl|msc)\z') {
        if ($Root -eq 'SystemRoot') { return (Join-Path $env:SystemRoot $Name) }
        return (Join-Path $env:SystemRoot "System32\$Name")
    }
    throw "Resolve-C5SystemFile: dozwolona tylko nazwa pliku z System32 albo shell:::{CLSID} (podano '$Name')."
}

function Start-C5CreateProcess {
    # CreateProcess (Process.Start z UseShellExecute=$false): tak uruchamia launcher DSOptimizer (R16) - bez asocjacji powłoki.
    [CmdletBinding()]
    param(
        [Parameter(Mandatory)][string]$FilePath,
        [AllowEmptyString()][string]$Arguments = ''
    )
    Assert-C5InsideVm
    $StartInfo = New-Object System.Diagnostics.ProcessStartInfo
    $StartInfo.FileName = $FilePath
    $StartInfo.Arguments = $Arguments
    $StartInfo.UseShellExecute = $false
    return [System.Diagnostics.Process]::Start($StartInfo)
}

function Start-C5Process {
    # Domyślnie ShellExecute przez Start-Process (bez -Wait); z -UseCreateProcess przez Start-C5CreateProcess. Argumenty jednym łańcuchem
    # (Start-Process nie cytuje -ArgumentList, wartości bez spacji wewnątrz).
    [CmdletBinding()]
    param(
        [Parameter(Mandatory)][string]$FilePath,
        [AllowEmptyString()][string]$Arguments = '',
        [switch]$UseCreateProcess
    )
    Assert-C5InsideVm
    $Result = @{ Process = $null; ProcessId = $null; Error = $null; Method = $(if ($UseCreateProcess) { 'CreateProcess' } else { 'ShellExecute' }) }
    try {
        if ($UseCreateProcess) { $Started = Start-C5CreateProcess -FilePath $FilePath -Arguments $Arguments }
        elseif ([string]::IsNullOrEmpty($Arguments)) { $Started = Start-Process -FilePath $FilePath -PassThru -ErrorAction Stop }
        else { $Started = Start-Process -FilePath $FilePath -ArgumentList $Arguments -PassThru -ErrorAction Stop }
        $Result.Process = $Started
        if ($null -ne $Started) { $Result.ProcessId = [int]$Started.Id }
    }
    catch { $Result.Error = $_.Exception.Message }
    return $Result
}

function Get-C5Observation {
    # Odczyt w chwili t: nowe procesy (z właścicielem), Ustawienia, stan uruchomionego procesu, w K2 nowe okna.
    [CmdletBinding()]
    param(
        [AllowEmptyCollection()][object[]]$Before = @(),
        [AllowEmptyCollection()][object[]]$BeforeWindows = @(),
        $LaunchedProcess,
        [System.Diagnostics.Stopwatch]$LaunchWatch,
        [switch]$WithWindows,
        [switch]$ExplorerDetail
    )
    $Observation = [ordered]@{
        At = (Get-Date).ToString('o'); ElapsedSinceLaunchMs = $null; LaunchedHasExited = $null; LaunchedExitCode = $null; NewProcesses = @(); SettingsHosts = @()
        NewWindows = @(); NewHiddenWindowCount = $null; ShellWindows = @(); NewExplorers = @(); UserShellPid = $null; WindowOwners = @(); Error = $null
    }
    if ($null -ne $LaunchWatch) { $Observation.ElapsedSinceLaunchMs = [int]$LaunchWatch.ElapsedMilliseconds }
    if ($null -ne $LaunchedProcess) {
        try {
            $LaunchedProcess.Refresh()
            $Observation.LaunchedHasExited = [bool]$LaunchedProcess.HasExited
            if ($Observation.LaunchedHasExited) { $Observation.LaunchedExitCode = $LaunchedProcess.ExitCode }
        }
        catch { $Observation.Error = "stan uruchomionego procesu: $($_.Exception.Message)" }
    }
    $After = @(Get-C5ProcessSnapshot)
    $New = @(Get-C5NewProcesses -Before $Before -After $After)
    $Observation.NewProcesses = @($New | ForEach-Object { Get-C5ProcessDetail -Entry $_ -Snapshot $After })
    $Observation.SettingsHosts = @(Get-C5SettingsHosts -After $After -NewProcesses $New)
    if ($WithWindows) {
        try {
            $NowWindows = @(Get-C5WindowList)
            $Windows = Get-C5NewWindows -Before $BeforeWindows -Now $NowWindows -Processes $After
            $Observation.NewWindows = @($Windows.Visible)
            $Observation.NewHiddenWindowCount = $Windows.HiddenCount
            $Observation.ShellWindows = @(Select-C5ShellWindows -Now $NowWindows)
        }
        catch { $Observation.Error = "okna: $($_.Exception.Message)" }
    }
    if ($ExplorerDetail) {
        # Sonda shell::: przez explorer.exe: nowe explorer.exe (właściciel, sesja, token), powłoka dso i właściciele okien Eksploratora/Ustawień.
        try {
            $Observation.NewExplorers = @($New | Where-Object { $_.Name -eq 'explorer.exe' } | ForEach-Object { Get-C5ShellDetail -Entry $_ -Snapshot $After })
            $Shell = Select-C5ShellPid -Shells @(Get-C5ShellProcess -UserName 'dso')
            if ($null -ne $Shell) { $Observation.UserShellPid = [int]$Shell.ProcessId }
            $NewKeys = @($New | ForEach-Object { [string]$_.Key })
            $OwnerPids = @(@($Observation.ShellWindows) + @($Observation.NewWindows) | ForEach-Object { [int]$_.ProcessId } | Sort-Object -Unique)
            $Observation.WindowOwners = @(foreach ($OwnerPid in $OwnerPids) {
                    $Entry = @($After | Where-Object { [int]$_.ProcessId -eq $OwnerPid }) | Select-Object -First 1
                    if ($null -eq $Entry) { [pscustomobject]@{ ProcessId = $OwnerPid; Name = $null; Error = 'brak procesu w migawce' }; continue }
                    $Detail = Get-C5ShellDetail -Entry $Entry -Snapshot $After
                    $Detail | Add-Member -MemberType NoteProperty -Name IsUserShell -Value ($OwnerPid -eq $Observation.UserShellPid)
                    $Detail | Add-Member -MemberType NoteProperty -Name IsNew -Value ($NewKeys -contains [string]$Entry.Key)
                    $Detail
                })
        }
        catch { $Observation.Error = (@($Observation.Error, "explorer: $($_.Exception.Message)") | Where-Object { $_ }) -join ' | ' }
    }
    return [pscustomobject]$Observation
}

function Invoke-C5Cleanup {
    # Zamyka TYLKO nowe procesy (Select-C5CleanupTargets) i w K2 nowe okna Explorera (Select-C5WindowsToClose, WM_CLOSE); resztę zapisuje.
    [CmdletBinding()]
    param(
        [AllowEmptyCollection()][object[]]$Before = @(),
        [AllowEmptyCollection()][object[]]$BeforeWindows = @(),
        [int]$LaunchedPid = 0,
        [AllowEmptyCollection()][string[]]$AllowNames = @(),
        [switch]$WithWindows,
        # Sonda shell::: przez explorer.exe: także NOWE explorer.exe wg Select-C5NewExplorerTargets (uruchomiony/potomkowie, inna sesja, inny
        # właściciel); powłoka dso nigdy.
        [switch]$IncludeExplorer
    )
    Assert-C5InsideVm
    $Out = [ordered]@{ Stopped = @(); StopErrors = @(); ClosedWindows = @(); WindowsStillOpen = @(); LeftProcesses = @(); RemainingAfterStop = @(); ExplorerShellPid = $null; Error = $null }
    try {
        $After = @(Get-C5ProcessSnapshot)
        $New = @(Get-C5NewProcesses -Before $Before -After $After)
        $Targets = @(Select-C5CleanupTargets -NewProcesses $New -LaunchedPid $LaunchedPid -AllowNames $AllowNames -SessionId (Get-C5CurrentSessionId) -ProtectedPids @($PID))
        if ($IncludeExplorer) {
            $Shell = Select-C5ShellPid -Shells @(Get-C5ShellProcess -UserName 'dso')
            $ShellPid = 0
            $ShellSessionId = -1
            if ($null -ne $Shell) { $ShellPid = [int]$Shell.ProcessId; $ShellSessionId = [int]$Shell.SessionId }
            $Out.ExplorerShellPid = $ShellPid
            $OwnerUserByPid = @{}
            foreach ($Explorer in @($New | Where-Object { $_.Name -eq 'explorer.exe' })) { $OwnerUserByPid[[int]$Explorer.ProcessId] = [string](Get-C5ProcessOwner -ProcessId ([int]$Explorer.ProcessId)).User }
            $Known = @($Targets | ForEach-Object { [string]$_.Key })
            foreach ($Explorer in @(Select-C5NewExplorerTargets -NewProcesses $New -ShellPid $ShellPid -ShellSessionId $ShellSessionId -LaunchedPid $LaunchedPid -OwnerUserByPid $OwnerUserByPid)) {
                if ($Known -notcontains [string]$Explorer.Key) { $Targets += $Explorer }
            }
        }
        foreach ($Target in $Targets) {
            try {
                Stop-Process -Id $Target.ProcessId -Force -ErrorAction Stop
                $Out.Stopped += "$($Target.Name) ($($Target.ProcessId))"
            }
            catch { $Out.StopErrors += "$($Target.Name) ($($Target.ProcessId)): $($_.Exception.Message)" }
        }
        if ($WithWindows) {
            try {
                $ExplorerPids = @($After | Where-Object { $_.Name -eq 'explorer.exe' } | ForEach-Object { [int]$_.ProcessId })
                $ToClose = @(Select-C5WindowsToClose -Before $BeforeWindows -Now @(Get-C5WindowList) -ExplorerPids $ExplorerPids)
                foreach ($Window in $ToClose) {
                    $Posted = Close-C5Window -Handle ([long]$Window.Handle)
                    $Out.ClosedWindows += "$($Window.ClassName) '$($Window.Title)' (PID $($Window.ProcessId), uchwyt $($Window.Handle), PostMessage=$Posted)"
                }
                # Następny wariant robi migawkę okien od razu: czekamy (z limitem), aż zamknięte okna naprawdę znikną.
                $Out.WindowsStillOpen = @(Wait-C5WindowsClosed -Handles @($ToClose | ForEach-Object { [long]$_.Handle }) | ForEach-Object { "uchwyt $_" })
            }
            catch { $Out.Error = "okna: $($_.Exception.Message)" }
        }
        $TargetKeys = @($Targets | ForEach-Object { [string]$_.Key })
        $Out.LeftProcesses = @($New | Where-Object { $TargetKeys -notcontains [string]$_.Key } | ForEach-Object { "$($_.Name) ($($_.ProcessId))" })
        if ($Targets.Count -gt 0) {
            Start-Sleep -Milliseconds 1000
            $FinalKeys = @(Get-C5ProcessSnapshot | ForEach-Object { [string]$_.Key })
            $Out.RemainingAfterStop = @($Targets | Where-Object { $FinalKeys -contains [string]$_.Key } | ForEach-Object { "$($_.Name) ($($_.ProcessId))" })
        }
    }
    catch { $Out.Error = $_.Exception.Message }
    return [pscustomobject]$Out
}

function Invoke-C5LaunchProbe {
    # Wspólny przebieg panelu i launchera: migawka -> uruchomienie -> odczyt po FirstSeconds i SecondSeconds -> sprzątanie (zawsze).
    [CmdletBinding()]
    param(
        [Parameter(Mandatory)][string]$FilePath,
        [AllowEmptyString()][string]$Arguments = '',
        [AllowEmptyCollection()][string[]]$AllowNames = @(),
        [switch]$WithWindows,
        [int]$FirstSeconds = 3,
        [int]$SecondSeconds = 10,
        [switch]$UseCreateProcess,
        [switch]$ExplorerDetail
    )
    $Record = [ordered]@{ FilePath = $FilePath; Arguments = $Arguments; Launch = $null; At3s = $null; At10s = $null; Cleanup = $null; Error = $null }
    $Before = @()
    $BeforeWindows = @()
    $Started = @{ Process = $null; ProcessId = $null; Error = $null }
    try {
        $Before = @(Get-C5ProcessSnapshot)
        if ($WithWindows) { $BeforeWindows = @(Get-C5WindowList) }
        $LaunchedAt = (Get-Date).ToString('o')
        $LaunchWatch = [System.Diagnostics.Stopwatch]::StartNew()
        $Started = Start-C5Process -FilePath $FilePath -Arguments $Arguments -UseCreateProcess:$UseCreateProcess
        $Record.Launch = [pscustomobject]@{ ProcessId = $Started.ProcessId; Error = $Started.Error; Method = $Started.Method; LaunchedAt = $LaunchedAt; StartCallMs = [int]$LaunchWatch.ElapsedMilliseconds }
        if (-not $Started.Error) {
            Start-Sleep -Seconds $FirstSeconds
            $Record.At3s = Get-C5Observation -Before $Before -BeforeWindows $BeforeWindows -LaunchedProcess $Started.Process -LaunchWatch $LaunchWatch -WithWindows:$WithWindows -ExplorerDetail:$ExplorerDetail
            Start-Sleep -Seconds ($SecondSeconds - $FirstSeconds)
            $Record.At10s = Get-C5Observation -Before $Before -BeforeWindows $BeforeWindows -LaunchedProcess $Started.Process -LaunchWatch $LaunchWatch -WithWindows:$WithWindows -ExplorerDetail:$ExplorerDetail
        }
    }
    catch { $Record.Error = $_.Exception.Message }
    finally {
        try {
            $LaunchedPid = if ($null -ne $Started.ProcessId) { [int]$Started.ProcessId } else { 0 }
            $Record.Cleanup = Invoke-C5Cleanup -Before $Before -BeforeWindows $BeforeWindows -LaunchedPid $LaunchedPid -AllowNames $AllowNames -WithWindows:$WithWindows -IncludeExplorer:$ExplorerDetail
        }
        catch { $Record.Error = (@($Record.Error, "sprzątanie: $($_.Exception.Message)") | Where-Object { $_ }) -join ' | ' }
    }
    return [pscustomobject]$Record
}

# --- Panele (sondy 1-4) ---

function Get-C5PanelAllowNames {
    # Procesy, w których Windows otwiera panele (research, pytanie 2) + Ustawienia; explorer.exe celowo nie (patrz Select-C5CleanupTargets).
    [CmdletBinding()]
    [OutputType([string[]])]
    param()
    return @('control.exe', 'rundll32.exe', 'mmc.exe', 'SystemPropertiesPerformance.exe', 'rstrui.exe', 'SystemSettings.exe')
}

function Get-C5PanelPlan {
    # D1: wszystkie 14 pozycji WinUtil (config/feature.json @ 144d516, polecenie dosłownie w Literal) + warianty MS (Executing Control Panel Items,
    # Canonical Names) i v3.2 z research. File = nazwa z System32 (Resolve-C5SystemFile) albo shell:::{CLSID}; Arguments = jeden łańcuch.
    [CmdletBinding()]
    param()
    $Plan = New-Object System.Collections.Generic.List[object]
    # Launcher: ShellExecute (Start-Process) albo CreateProcess (jak launcher DSOptimizer, R16); Root: katalog pliku; PreCloseNames: procesy
    # z tego przebiegu zamykane PRZED wariantem (kontaminacja); ExplorerDetail: odczyt nowych explorer.exe, właścicieli okien i ich tokenów.
    $Add = {
        param($Id, $Variant, $Source, $WinUtilKey, $Literal, $File, $Arguments, $Launcher = 'ShellExecute', $Root = 'System32', $PreCloseNames = @(), $ExplorerDetail = $false)
        $Plan.Add([pscustomobject]@{
                Name = "$($Id -replace '^panels\.', '')-$Variant"; Id = $Id; Variant = $Variant; Source = $Source; WinUtilKey = $WinUtilKey; Literal = $Literal; File = $File
                Arguments = $Arguments; Launcher = $Launcher; Root = $Root; PreCloseNames = @($PreCloseNames); ExplorerDetail = [bool]$ExplorerDetail
            })
    }
    & $Add 'panels.visual-effects' 'ms-exe' 'MS' $null '%windir%\system32\SystemPropertiesPerformance.exe' 'SystemPropertiesPerformance.exe' ''
    & $Add 'panels.visual-effects' 'v32-sysdm-3' 'v3.2' $null "Start-Process 'control.exe' -ArgumentList 'sysdm.cpl,,3'" 'control.exe' 'sysdm.cpl,,3'
    & $Add 'panels.power-options' 'winutil' 'WinUtil' 'WPFPanelPower' 'powercfg.cpl' 'powercfg.cpl' ''
    & $Add 'panels.power-options' 'v32-control' 'v3.2' $null "Start-Process 'control' -ArgumentList 'powercfg.cpl'" 'control.exe' 'powercfg.cpl'
    & $Add 'panels.power-options' 'ms-name' 'MS' $null 'control.exe /name Microsoft.PowerOptions' 'control.exe' '/name Microsoft.PowerOptions'
    & $Add 'panels.network-connections' 'winutil' 'WinUtil' 'WPFPanelNetwork' 'ncpa.cpl' 'ncpa.cpl' ''
    & $Add 'panels.network-connections' 'ms-netconnections' 'MS' $null 'control.exe netconnections' 'control.exe' 'netconnections'
    & $Add 'panels.computer-management' 'winutil' 'WinUtil' 'WPFPanelComputer' 'compmgmt.msc' 'compmgmt.msc' ''
    & $Add 'panels.control-panel' 'winutil' 'WinUtil' 'WPFPanelControl' 'control' 'control.exe' ''
    & $Add 'panels.mouse' 'winutil' 'WinUtil' 'WPFPanelMouse' 'main.cpl' 'main.cpl' ''
    & $Add 'panels.mouse' 'ms-name' 'MS' $null 'control.exe /name Microsoft.Mouse' 'control.exe' '/name Microsoft.Mouse'
    & $Add 'panels.devices-printers' 'winutil' 'WinUtil' 'WPFPanelPrinter' "Start-Process 'shell:::{A8A91A66-3A7D-4424-8D24-04E180695C7A}'" 'shell:::{A8A91A66-3A7D-4424-8D24-04E180695C7A}' ''
    & $Add 'panels.devices-printers' 'ms-name' 'MS' $null 'control.exe /name Microsoft.DevicesAndPrinters' 'control.exe' '/name Microsoft.DevicesAndPrinters'
    # Sonda uzupełniająca (R20/R21, t3-vm-results "Mechanizm shell:::"): CreateProcess %SystemRoot%\explorer.exe z argumentem shell:::{CLSID}.
    & $Add 'panels.devices-printers' 'explorer' 'R16' $null 'explorer.exe shell:::{A8A91A66-3A7D-4424-8D24-04E180695C7A}' 'explorer.exe' 'shell:::{A8A91A66-3A7D-4424-8D24-04E180695C7A}' 'CreateProcess' 'SystemRoot' @() $true
    & $Add 'panels.programs-features' 'winutil' 'WinUtil' 'WPFPanelPrograms' 'appwiz.cpl' 'appwiz.cpl' ''
    & $Add 'panels.programs-features' 'ms-name' 'MS' $null 'control.exe /name Microsoft.ProgramsAndFeatures' 'control.exe' '/name Microsoft.ProgramsAndFeatures'
    & $Add 'panels.region' 'winutil' 'WinUtil' 'WPFPanelRegion' 'intl.cpl' 'intl.cpl' ''
    & $Add 'panels.region' 'ms-name' 'MS' $null 'control.exe /name Microsoft.RegionAndLanguage' 'control.exe' '/name Microsoft.RegionAndLanguage'
    & $Add 'panels.security-maintenance' 'winutil' 'WinUtil' 'WPFPanelSecurity' 'wscui.cpl' 'wscui.cpl' ''
    # Sonda uzupełniająca (R20/R21, "Panele warunkowe"): forma launchera control.exe wscui.cpl przez CreateProcess.
    & $Add 'panels.security-maintenance' 'control' 'R16' $null 'control.exe wscui.cpl' 'control.exe' 'wscui.cpl' 'CreateProcess'
    & $Add 'panels.sound' 'winutil' 'WinUtil' 'WPFPanelSound' 'mmsys.cpl' 'mmsys.cpl' ''
    & $Add 'panels.sound' 'ms-name' 'MS' $null 'control.exe /name Microsoft.Sound' 'control.exe' '/name Microsoft.Sound'
    & $Add 'panels.system-properties' 'winutil' 'WinUtil' 'WPFPanelSystem' 'sysdm.cpl' 'sysdm.cpl' ''
    & $Add 'panels.system-properties' 'ms-name' 'MS' $null 'control.exe /name Microsoft.System' 'control.exe' '/name Microsoft.System'
    # Sonda uzupełniająca (R20/R21): control.exe sysdm.cpl przez CreateProcess; przedtem zamknięcie SystemPropertiesAdvanced.exe z tego przebiegu.
    & $Add 'panels.system-properties' 'control' 'R16' $null 'control.exe sysdm.cpl' 'control.exe' 'sysdm.cpl' 'CreateProcess' 'System32' @('SystemPropertiesAdvanced.exe')
    & $Add 'panels.date-time' 'winutil' 'WinUtil' 'WPFPanelTimedate' 'timedate.cpl' 'timedate.cpl' ''
    & $Add 'panels.date-time' 'ms-name' 'MS' $null 'control.exe /name Microsoft.DateAndTime' 'control.exe' '/name Microsoft.DateAndTime'
    & $Add 'panels.firewall' 'winutil' 'WinUtil' 'WPFPanelFirewall' 'firewall.cpl' 'firewall.cpl' ''
    & $Add 'panels.firewall' 'ms-name' 'MS' $null 'control.exe /name Microsoft.WindowsFirewall' 'control.exe' '/name Microsoft.WindowsFirewall'
    & $Add 'panels.system-restore' 'winutil' 'WinUtil' 'WPFPanelRestore' 'rstrui.exe' 'rstrui.exe' ''
    return $Plan.ToArray()
}

function Get-C5PanelProperty {
    # Pole wariantu planu z wartością domyślną (warianty spoza Get-C5PanelPlan, np. w testach, mogą go nie mieć).
    [CmdletBinding()]
    param(
        [Parameter(Mandatory)]$Variant,
        [Parameter(Mandatory)][string]$Name,
        $Default
    )
    if ($null -ne $Variant.PSObject.Properties[$Name] -and $null -ne $Variant.$Name) { return $Variant.$Name }
    return $Default
}

function Invoke-C5PreClean {
    # Przed wariantem: procesy o podanych nazwach, które powstały W TYM PRZEBIEGU (po migawce startowej) w tej samej sesji, są zamykane;
    # starsze i z innych sesji tylko zapisane (kontaminacja z wcześniejszych wariantów, np. SystemPropertiesAdvanced.exe).
    [CmdletBinding()]
    param(
        [Parameter(Mandatory)][string[]]$Names,
        [AllowEmptyCollection()][object[]]$Baseline = @()
    )
    Assert-C5InsideVm
    $Out = [ordered]@{ Names = @($Names); Stopped = @(); StopErrors = @(); PreExisting = @(); OtherSession = @(); Error = $null }
    try {
        $Now = @(Get-C5ProcessSnapshot)
        $NewKeys = @(Get-C5NewProcesses -Before $Baseline -After $Now | ForEach-Object { [string]$_.Key })
        $SessionId = Get-C5CurrentSessionId
        foreach ($Process in @($Now | Where-Object { $Names -contains [string]$_.Name })) {
            $Label = "$($Process.Name) ($($Process.ProcessId)), sesja $($Process.SessionId)"
            if ($NewKeys -notcontains [string]$Process.Key) { $Out.PreExisting += $Label; continue }
            if ([int]$Process.SessionId -ne $SessionId) { $Out.OtherSession += $Label; continue }
            try {
                Stop-Process -Id $Process.ProcessId -Force -ErrorAction Stop
                $Out.Stopped += $Label
            }
            catch { $Out.StopErrors += "${Label}: $($_.Exception.Message)" }
        }
        if ($Out.Stopped.Count -gt 0) { Start-Sleep -Milliseconds 1000 }
    }
    catch { $Out.Error = $_.Exception.Message }
    return [pscustomobject]$Out
}

function Invoke-C5PanelVariant {
    [CmdletBinding()]
    param(
        [Parameter(Mandatory)]$Variant,
        [switch]$WithWindows,
        [AllowEmptyCollection()][object[]]$RunBaseline = @()
    )
    $Launcher = [string](Get-C5PanelProperty -Variant $Variant -Name 'Launcher' -Default 'ShellExecute')
    $Root = [string](Get-C5PanelProperty -Variant $Variant -Name 'Root' -Default 'System32')
    $PreCloseNames = @(Get-C5PanelProperty -Variant $Variant -Name 'PreCloseNames' -Default @())
    $ExplorerDetail = [bool](Get-C5PanelProperty -Variant $Variant -Name 'ExplorerDetail' -Default $false)
    $File = Resolve-C5SystemFile -Name ([string]$Variant.File) -Root $Root
    $Exists = $null
    if ($File -notlike 'shell:*') { $Exists = [bool](Test-Path -LiteralPath $File) }
    $PreClean = $null
    if ($PreCloseNames.Count -gt 0) { $PreClean = Invoke-C5PreClean -Names $PreCloseNames -Baseline $RunBaseline }
    $Probe = Invoke-C5LaunchProbe -FilePath $File -Arguments ([string]$Variant.Arguments) -AllowNames (Get-C5PanelAllowNames) -WithWindows:$WithWindows `
        -UseCreateProcess:($Launcher -eq 'CreateProcess') -ExplorerDetail:$ExplorerDetail
    return [pscustomobject]@{
        Name = (Get-C5PanelProperty -Variant $Variant -Name 'Name' -Default $null); Id = $Variant.Id; Variant = $Variant.Variant; Source = $Variant.Source
        WinUtilKey = $Variant.WinUtilKey; Literal = $Variant.Literal; Launcher = $Launcher; FilePath = $File; FileExists = $Exists; Arguments = $Variant.Arguments
        PreClean = $PreClean; Launch = $Probe.Launch; At3s = $Probe.At3s; At10s = $Probe.At10s; Cleanup = $Probe.Cleanup; Error = $Probe.Error
    }
}

function Select-C5PanelVariants {
    # Filtr -Variant: nazwy (także "a,b" w jednym łańcuchu z zadania K2) -> warianty w kolejności planu; pusto = wszystkie; nieznana nazwa = wyjątek.
    [CmdletBinding()]
    param([AllowNull()][AllowEmptyCollection()][string[]]$Names)
    $Plan = @(Get-C5PanelPlan)
    $Wanted = @($Names | ForEach-Object { [string]$_ -split ',' } | ForEach-Object { $_.Trim() } | Where-Object { $_ })
    if ($Wanted.Count -eq 0) { return $Plan }
    $Known = @($Plan | ForEach-Object { $_.Name })
    $Unknown = @($Wanted | Where-Object { $Known -notcontains $_ })
    if ($Unknown.Count -gt 0) { throw "Nieznane warianty paneli: $($Unknown -join ', ')." }
    return @($Plan | Where-Object { $Wanted -contains $_.Name })
}

function Invoke-C5PanelRun {
    # Wybrane (domyślnie wszystkie) warianty po kolei w jednym kontekście (okna tylko w K2); błąd jednego wariantu to dana.
    [CmdletBinding()]
    param(
        [Parameter(Mandatory)][ValidateSet('K1', 'K2')][string]$Context,
        [string[]]$Variant
    )
    Assert-C5InsideVm
    $Selected = @(Select-C5PanelVariants -Names $Variant)
    $RunBaseline = @(Get-C5ProcessSnapshot)
    foreach ($Entry in $Selected) {
        try { Invoke-C5PanelVariant -Variant $Entry -WithWindows:($Context -eq 'K2') -RunBaseline $RunBaseline }
        catch { [pscustomobject]@{ Name = $Entry.Name; Id = $Entry.Id; Variant = $Entry.Variant; Source = $Entry.Source; Literal = $Entry.Literal; Error = $_.Exception.Message } }
    }
}

function Get-C5SystemProtectionState {
    # Stan ochrony systemu przy rstrui.exe (tylko odczyt, bez włączania): liczba punktów przywracania i zrzut klucza SystemRestore.
    [CmdletBinding()]
    param()
    $State = [ordered]@{ RestorePointCount = $null; RestorePointError = $null; SystemRestoreKey = $null; Error = $null }
    try { $State.RestorePointCount = @(Get-ComputerRestorePoint -ErrorAction Stop).Count }
    catch { $State.RestorePointError = $_.Exception.Message }
    try { $State.SystemRestoreKey = Read-ExploreRegistryKey -Path 'HKLM\SOFTWARE\Microsoft\Windows NT\CurrentVersion\SystemRestore' }
    catch { $State.Error = $_.Exception.Message }
    return [pscustomobject]$State
}

# --- WinUtil (sonda 12) ---

function Get-C5WinUtilCommand {
    # README WinUtil, "Stable Branch (recommended)", dosłownie (research sekcja 6). Dana, nie kod WinUtil.
    [CmdletBinding()]
    [OutputType([string])]
    param()
    return 'irm https://christitus.com/win | iex'
}

function Get-C5WinUtilVariants {
    # Dwa warianty argumentów powershell.exe (pytanie 12: czy "|" przechodzi poprawnie): w cudzysłowie i bez.
    [CmdletBinding()]
    param()
    $Command = Get-C5WinUtilCommand
    return @(
        [pscustomobject]@{ Variant = 'cudzyslow'; Arguments = "-NoExit -Command `"$Command`"" }
        [pscustomobject]@{ Variant = 'bez-cudzyslowu'; Arguments = "-NoExit -Command $Command" }
    )
}

function Test-C5NoNetwork {
    # Warunek sondy WinUtil: zero profili Get-NetConnectionProfile. Profil albo błąd = "pominięte" (brak potwierdzenia braku sieci).
    [CmdletBinding()]
    param()
    $Result = [ordered]@{ NoNetwork = $false; Reason = $null; Profiles = @() }
    try {
        $Profiles = @(Get-NetConnectionProfile -ErrorAction Stop)
        $Result.Profiles = @($Profiles | ForEach-Object { "$($_.Name) | $($_.InterfaceAlias) | $($_.NetworkCategory) | IPv4: $($_.IPv4Connectivity) | IPv6: $($_.IPv6Connectivity)" })
        if ($Profiles.Count -eq 0) {
            $Result.NoNetwork = $true
            $Result.Reason = 'Get-NetConnectionProfile: 0 profili'
        }
        else { $Result.Reason = "pominięte: VM ma sieć (Get-NetConnectionProfile: $($Profiles.Count) profili)" }
    }
    catch { $Result.Reason = "pominięte: nie potwierdzono braku sieci (Get-NetConnectionProfile: $($_.Exception.Message))" }
    return [pscustomobject]$Result
}

function Invoke-C5WinUtilRun {
    # Sieć sprawdzana w tym samym kontekście tuż przed uruchomieniem; bez sieci: osobne okno powershell.exe, odczyt po 3 i 10 s, zamknięcie
    # tylko tego procesu i jego potomków (oraz hosta konsoli Windows Terminal, jeśli powstał nowy w tej sesji).
    [CmdletBinding()]
    param([Parameter(Mandatory)][ValidateSet('K1', 'K2')][string]$Context)
    Assert-C5InsideVm
    $Network = Test-C5NoNetwork
    if (-not $Network.NoNetwork) {
        return [pscustomobject]@{ Context = $Context; Variant = $null; Skipped = $Network.Reason; Network = $Network; Probe = $null }
    }
    $PowerShellPath = Join-Path $env:SystemRoot 'System32\WindowsPowerShell\v1.0\powershell.exe'
    foreach ($Variant in Get-C5WinUtilVariants) {
        try {
            $Probe = Invoke-C5LaunchProbe -FilePath $PowerShellPath -Arguments $Variant.Arguments -AllowNames @('WindowsTerminal.exe', 'OpenConsole.exe') -WithWindows:($Context -eq 'K2') -FirstSeconds 3 -SecondSeconds 10
            [pscustomobject]@{ Context = $Context; Variant = $Variant.Variant; Skipped = $null; Network = $Network; Probe = $Probe }
        }
        catch { [pscustomobject]@{ Context = $Context; Variant = $Variant.Variant; Skipped = $null; Network = $Network; Probe = $null; Error = $_.Exception.Message } }
    }
}

# --- Store (sondy 8-11) ---

function Get-C5StorePackageInfo {
    [CmdletBinding()]
    param([Parameter(Mandatory)]$Package)
    $Location = [string]$Package.InstallLocation
    $Manifest = $null
    $ManifestExists = $false
    $ManifestError = $null
    if ($Location) {
        $Manifest = Join-Path $Location 'AppxManifest.xml'
        $ManifestExists = [bool](Test-Path -LiteralPath $Manifest)
        # Test-Path daje $false także przy braku dostępu (ACL WindowsApps): powód z Get-Item jako dana.
        if (-not $ManifestExists) {
            try { $null = Get-Item -LiteralPath $Manifest -ErrorAction Stop; $ManifestError = 'Get-Item widzi plik, Test-Path nie' }
            catch { $ManifestError = $_.Exception.Message }
        }
    }
    $Users = @()
    if ($null -ne $Package.PSObject.Properties['PackageUserInformation']) { $Users = @($Package.PackageUserInformation | ForEach-Object { [string]$_ }) }
    return [pscustomobject]@{
        Name = [string]$Package.Name; PackageFullName = [string]$Package.PackageFullName; Version = [string]$Package.Version; Architecture = [string]$Package.Architecture
        InstallLocation = $Location; ManifestPath = $Manifest; ManifestExists = $ManifestExists; ManifestError = $ManifestError; Status = [string]$Package.Status; Users = $Users
    }
}

function Get-C5StorePackages {
    # Get-AppxPackage [-AllUsers] | Where-Object Name -like '*Store*' (pytanie 8). Błąd i pusty wynik jako dane.
    [CmdletBinding()]
    param([switch]$AllUsers)
    Assert-C5InsideVm
    $Result = [ordered]@{ AllUsers = [bool]$AllUsers; Ok = $false; Error = $null; Count = 0; Items = @() }
    try {
        $Found = if ($AllUsers) { @(Get-AppxPackage -AllUsers -ErrorAction Stop) } else { @(Get-AppxPackage -ErrorAction Stop) }
        $Items = @($Found | Where-Object { [string]$_.Name -like '*Store*' } | ForEach-Object { Get-C5StorePackageInfo -Package $_ })
        $Result.Items = $Items
        $Result.Count = $Items.Count
        $Result.Ok = $true
    }
    catch { $Result.Error = $_.Exception.Message }
    return [pscustomobject]$Result
}

function Register-C5StorePackage {
    # MS (Add-AppxPackage, przykład 3): ponowna rejestracja istniejącej instalacji z manifestu. W zadaniu w tle z limitem czasu,
    # żeby zawieszona rejestracja nie zatrzymała całego przebiegu.
    [CmdletBinding()]
    param(
        [Parameter(Mandatory)][string]$ManifestPath,
        [ValidateRange(10, 1800)][int]$TimeoutSeconds = 300
    )
    Assert-C5InsideVm
    $Result = [ordered]@{
        ManifestPath = $ManifestPath; Command = "Add-AppxPackage -Path '$ManifestPath' -Register -DisableDevelopmentMode"
        Ok = $false; Error = $null; TimedOut = $false; JobState = $null; DurationMs = $null
    }
    $Watch = [System.Diagnostics.Stopwatch]::StartNew()
    $Job = Start-Job -ScriptBlock {
        param([string]$Path)
        Add-AppxPackage -Path $Path -Register -DisableDevelopmentMode -ErrorAction Stop
        'zarejestrowano'
    } -ArgumentList $ManifestPath
    try {
        # Polecenia zadania przez -Id (liczba), nie przez obiekt zadania.
        $Done = Wait-Job -Id $Job.Id -Timeout $TimeoutSeconds
        if ($null -eq $Done) {
            $Result.TimedOut = $true
            $Result.Error = "przekroczono limit $TimeoutSeconds s"
            Stop-Job -Id $Job.Id
        }
        else {
            try {
                $null = Receive-Job -Id $Job.Id -ErrorAction Stop
                $Result.Ok = ([string]$Job.State -eq 'Completed')
            }
            catch { $Result.Error = $_.Exception.Message }
        }
        $Result.JobState = [string]$Job.State
    }
    finally {
        Remove-Job -Id $Job.Id -Force
        $Watch.Stop()
        $Result.DurationMs = [int]$Watch.ElapsedMilliseconds
    }
    return [pscustomobject]$Result
}

function Invoke-C5StoreRegistration {
    # Pytanie 9: rejestracja w danym kontekście z odczytem przed/po (bieżące konto) i po (-AllUsers, PackageUserInformation).
    [CmdletBinding()]
    param([Parameter(Mandatory)][ValidateSet('K1', 'K2')][string]$Context)
    Assert-C5InsideVm
    $Record = [ordered]@{ Context = $Context; NameMatchCount = 0; CandidateCount = 0; Target = $null; Before = $null; Register = $null; After = $null; AfterAllUsers = $null; Skipped = $null; Error = $null }
    $All = Get-C5StorePackages -AllUsers
    $ByName = @($All.Items | Where-Object { $_.Name -eq 'Microsoft.WindowsStore' })
    $Candidates = @($ByName | Where-Object { $_.ManifestExists })
    $Record.NameMatchCount = $ByName.Count
    $Record.CandidateCount = $Candidates.Count
    if ($Candidates.Count -eq 0) {
        if ($ByName.Count -eq 0) {
            $Record.Skipped = "brak pakietu Microsoft.WindowsStore na liście -AllUsers (Ok=$($All.Ok), błąd: $($All.Error)) – rejestracja pominięta"
        }
        else {
            $Reasons = (@($ByName | ForEach-Object { $_.ManifestError } | Where-Object { $_ } | Sort-Object -Unique) -join ' | ')
            $Record.Skipped = "pakiety Microsoft.WindowsStore po nazwie: $($ByName.Count), z czytelnym AppxManifest.xml: 0 (powód: $Reasons) – rejestracja pominięta"
        }
        return [pscustomobject]$Record
    }
    $Target = @($Candidates | Sort-Object -Property @{ Expression = { try { [version]$_.Version } catch { [version]'0.0' } } } -Descending)[0]
    $Record.Target = $Target
    $Record.Before = Get-C5StorePackages
    $Record.Register = Register-C5StorePackage -ManifestPath $Target.ManifestPath -TimeoutSeconds 300
    $Record.After = Get-C5StorePackages
    $Record.AfterAllUsers = Get-C5StorePackages -AllUsers
    return [pscustomobject]$Record
}

function Invoke-C5WsReset {
    # Pytanie 11: WSReset.exe -i tylko kod wyjścia i komunikat (limit 60 s), potem sprzątanie nowych procesów Store z tej sesji.
    [CmdletBinding()]
    param([Parameter(Mandatory)][ValidateSet('K1', 'K2')][string]$Context)
    Assert-C5InsideVm
    $Path = Join-Path $env:SystemRoot 'System32\WSReset.exe'
    $Record = [ordered]@{ Context = $Context; FilePath = $Path; FileExists = [bool](Test-Path -LiteralPath $Path); Native = $null; Cleanup = $null; Error = $null }
    $Before = @(Get-C5ProcessSnapshot)
    try { $Record.Native = Invoke-ExploreNative -FilePath $Path -Arguments @('-i') -TimeoutSeconds 60 }
    catch { $Record.Error = $_.Exception.Message }
    $Record.Cleanup = Invoke-C5Cleanup -Before $Before -LaunchedPid 0 -AllowNames @('WSReset.exe', 'WinStore.App.exe')
    return [pscustomobject]$Record
}

# --- Explorer (sondy 5-7) ---

function Get-C5ShellProcess {
    # explorer.exe należące do użytkownika (Win32_Process.GetOwner), opcjonalnie tylko z danej sesji; od najstarszego.
    [CmdletBinding()]
    param(
        [ValidatePattern('^[A-Za-z0-9_-]+\z')][string]$UserName = 'dso',
        [int]$SessionId = -1
    )
    $Snapshot = @(Get-C5ProcessSnapshot)
    $Shells = foreach ($Entry in @($Snapshot | Where-Object { $_.Name -eq 'explorer.exe' })) {
        if ($SessionId -ge 0 -and [int]$Entry.SessionId -ne $SessionId) { continue }
        $Owner = Get-C5ProcessOwner -ProcessId ([int]$Entry.ProcessId)
        if ([string]$Owner.User -ne $UserName) { continue }
        [pscustomobject]@{
            Key = $Entry.Key; Name = $Entry.Name; ProcessId = $Entry.ProcessId; ParentProcessId = $Entry.ParentProcessId; ParentName = (Get-C5ParentName -Entry $Entry -Snapshot $Snapshot); SessionId = $Entry.SessionId
            CommandLine = $Entry.CommandLine; CreationDate = $Entry.CreationDate; Owner = $Owner.Owner; OwnerSid = $Owner.OwnerSid
        }
    }
    return @($Shells | Sort-Object -Property CreationDate, ProcessId)
}

function Wait-C5NewShell {
    # Pętla z limitem (domyślnie 30 s co 0,5 s; twardy limit liczby prób) do pojawienia się NOWEGO explorer.exe użytkownika w tej samej sesji.
    [CmdletBinding()]
    param(
        [AllowEmptyCollection()][string[]]$ExcludeKeys = @(),
        [Parameter(Mandatory)][int]$SessionId,
        [string]$UserName = 'dso',
        [Parameter(Mandatory)][System.Diagnostics.Stopwatch]$Stopwatch,
        [int]$TimeoutSeconds = 30,
        [int]$StepMilliseconds = 500
    )
    $Result = [ordered]@{ Found = $false; NewPid = $null; ElapsedMs = $null; Polls = 0; Entry = $null }
    $MaxPolls = [int][Math]::Ceiling(($TimeoutSeconds * 1000) / $StepMilliseconds)
    for ($Poll = 1; $Poll -le $MaxPolls; $Poll++) {
        $Result.Polls = $Poll
        $Hit = @(Get-C5ShellProcess -UserName $UserName -SessionId $SessionId | Where-Object { $ExcludeKeys -notcontains [string]$_.Key }) | Select-Object -First 1
        if ($null -ne $Hit) {
            $Result.Found = $true
            $Result.NewPid = [int]$Hit.ProcessId
            $Result.ElapsedMs = [int]$Stopwatch.ElapsedMilliseconds
            $Result.Entry = $Hit
            break
        }
        if ($Stopwatch.Elapsed.TotalSeconds -ge $TimeoutSeconds) { break }
        if ($Poll -lt $MaxPolls) { Start-Sleep -Milliseconds $StepMilliseconds }
    }
    return [pscustomobject]$Result
}

function Invoke-C5ShellKill {
    # Pytania 6-7: zatrzymanie powłoki dso (Stop-Process -Force albo taskkill /f /pid, nigdy /im) i pomiar powrotu przez winlogon.
    # K2: powłoka z sesji zadania i dodatkowo czas do Shell_TrayWnd nowego procesu. Explorer nie jest tu nigdy uruchamiany.
    [CmdletBinding()]
    param(
        [Parameter(Mandatory)][ValidateSet('StopProcess', 'Taskkill')][string]$Method,
        [Parameter(Mandatory)][ValidateSet('K1', 'K2')][string]$Context,
        [ValidatePattern('^[A-Za-z0-9_-]+\z')][string]$UserName = 'dso'
    )
    Assert-C5InsideVm
    $Record = [ordered]@{
        Method = $Method; Context = $Context; Command = $null; Old = $null; KillResult = $null; KilledAt = $null; Found = $false; NewPid = $null
        ElapsedMs = $null; Polls = 0; New = $null; TaskbarFound = $null; TaskbarElapsedMs = $null; TaskbarPolls = $null; Skipped = $null; Error = $null
    }
    $SessionFilter = if ($Context -eq 'K2') { Get-C5CurrentSessionId } else { -1 }
    $Shells = @(Get-C5ShellProcess -UserName $UserName -SessionId $SessionFilter)
    if ($Shells.Count -eq 0) {
        $Record.Skipped = "brak explorer.exe użytkownika $UserName (sesja: $SessionFilter) – nic nie zatrzymano"
        return [pscustomobject]$Record
    }
    $Old = $Shells[0]
    $OldPid = [int]$Old.ProcessId
    $Record.Old = Get-C5ShellDetail -Entry $Old
    $ExcludeKeys = @($Shells | ForEach-Object { [string]$_.Key })
    $Watch = [System.Diagnostics.Stopwatch]::StartNew()
    $Record.KilledAt = (Get-Date).ToString('o')
    try {
        if ($Method -eq 'StopProcess') {
            $Record.Command = "Stop-Process -Id $OldPid -Force -PassThru"
            $Stopped = Stop-Process -Id $OldPid -Force -PassThru -ErrorAction Stop
            $Record.KillResult = "zatrzymano PID $(@($Stopped | ForEach-Object { $_.Id }) -join ',')"
        }
        else {
            $Taskkill = Join-Path $env:SystemRoot 'System32\taskkill.exe'
            $Record.Command = "$Taskkill /f /pid $OldPid"
            $Record.KillResult = Invoke-ExploreNative -FilePath $Taskkill -Arguments @('/f', '/pid', [string]$OldPid) -TimeoutSeconds 30
        }
    }
    catch {
        $Record.Error = "zatrzymanie: $($_.Exception.Message)"
        return [pscustomobject]$Record
    }
    $Wait = Wait-C5NewShell -ExcludeKeys $ExcludeKeys -SessionId ([int]$Old.SessionId) -UserName $UserName -Stopwatch $Watch -TimeoutSeconds 30 -StepMilliseconds 500
    $Record.Found = $Wait.Found
    $Record.NewPid = $Wait.NewPid
    $Record.ElapsedMs = $Wait.ElapsedMs
    $Record.Polls = $Wait.Polls
    if ($Wait.Found) {
        $Record.New = Get-C5ShellDetail -Entry $Wait.Entry
        if ($Context -eq 'K2') {
            $Record.TaskbarFound = $false
            for ($Poll = 1; $Poll -le 60; $Poll++) {
                $Record.TaskbarPolls = $Poll
                $Tray = @(Get-C5WindowList | Where-Object { $_.ClassName -eq 'Shell_TrayWnd' -and [int]$_.ProcessId -eq [int]$Wait.NewPid })
                if ($Tray.Count -gt 0) {
                    $Record.TaskbarFound = $true
                    $Record.TaskbarElapsedMs = [int]$Watch.ElapsedMilliseconds
                    break
                }
                if ($Watch.Elapsed.TotalSeconds -ge 60) { break }
                if ($Poll -lt 60) { Start-Sleep -Milliseconds 500 }
            }
        }
    }
    return [pscustomobject]$Record
}

function Invoke-C5ShellStartV32 {
    # Pytanie 7, wariant v3.2: Start-Process explorer.exe z procesu admina przy działającej powłoce; tylko odczyt tokena NOWYCH explorer.exe,
    # potem zamknięcie TYLKO ich (i w K2 nowych okien CabinetWClass przez WM_CLOSE). Bez działającej powłoki dso - pominięte.
    [CmdletBinding()]
    param(
        [Parameter(Mandatory)][ValidateSet('K1', 'K2')][string]$Context,
        [ValidatePattern('^[A-Za-z0-9_-]+\z')][string]$UserName = 'dso'
    )
    Assert-C5InsideVm
    $Record = [ordered]@{
        Context = $Context; Command = $null; ShellPid = $null; ShellSessionId = $null; LaunchedPid = $null; LaunchedAt = $null; LaunchError = $null
        LaunchedHasExited = $null; LaunchedExitCode = $null; ObservedAfterMs = $null; NewExplorers = @(); NewWindows = @(); ShellWindows = @()
        Stopped = @(); StopErrors = @(); ClosedWindows = @(); WindowsStillOpen = @(); ShellAliveAfter = $null; Skipped = $null; Error = $null
    }
    $Shells = @(Get-C5ShellProcess -UserName $UserName)
    if ($Shells.Count -eq 0) {
        $Record.Skipped = "brak explorer.exe użytkownika $UserName – wariant v3.2 pominięty (nie uruchamiamy powłoki od zera)"
        return [pscustomobject]$Record
    }
    $ShellPid = [int]$Shells[0].ProcessId
    $ShellSessionId = [int]$Shells[0].SessionId
    $Record.ShellPid = $ShellPid
    $Record.ShellSessionId = $ShellSessionId
    $WithWindows = ($Context -eq 'K2')
    $Before = @(Get-C5ProcessSnapshot)
    $BeforeWindows = @()
    if ($WithWindows) { $BeforeWindows = @(Get-C5WindowList) }
    $ExplorerPath = Join-Path $env:SystemRoot 'explorer.exe'
    $Record.Command = "Start-Process $ExplorerPath"
    $Record.LaunchedAt = (Get-Date).ToString('o')
    $LaunchWatch = [System.Diagnostics.Stopwatch]::StartNew()
    $Started = Start-C5Process -FilePath $ExplorerPath -Arguments ''
    $Record.LaunchedPid = $Started.ProcessId
    $Record.LaunchError = $Started.Error
    Start-Sleep -Seconds 5
    try {
        # Stan uruchomionego procesu: pusty NewExplorers + zakończony proces = przekazanie do istniejącej powłoki; bez procesu = nic nie wystartowało.
        if ($null -ne $Started.Process) {
            try {
                $Started.Process.Refresh()
                $Record.LaunchedHasExited = [bool]$Started.Process.HasExited
                if ($Record.LaunchedHasExited) { $Record.LaunchedExitCode = $Started.Process.ExitCode }
            }
            catch { $Record.Error = "stan uruchomionego procesu: $($_.Exception.Message)" }
        }
        $Record.ObservedAfterMs = [int]$LaunchWatch.ElapsedMilliseconds
        $After = @(Get-C5ProcessSnapshot)
        $New = @(Get-C5NewProcesses -Before $Before -After $After)
        $Record.NewExplorers = @($New | Where-Object { $_.Name -eq 'explorer.exe' } | ForEach-Object { Get-C5ShellDetail -Entry $_ -Snapshot $After })
        $NowWindows = @()
        if ($WithWindows) {
            $NowWindows = @(Get-C5WindowList)
            $Record.NewWindows = @((Get-C5NewWindows -Before $BeforeWindows -Now $NowWindows -Processes $After).Visible)
            $Record.ShellWindows = @(Select-C5ShellWindows -Now $NowWindows)
        }
        $OwnerUserByPid = @{}
        foreach ($Explorer in $Record.NewExplorers) { $OwnerUserByPid[[int]$Explorer.ProcessId] = [string]$Explorer.OwnerUser }
        $LaunchedPid = if ($null -ne $Started.ProcessId) { [int]$Started.ProcessId } else { 0 }
        foreach ($Target in @(Select-C5NewExplorerTargets -NewProcesses $New -ShellPid $ShellPid -ShellSessionId $ShellSessionId -LaunchedPid $LaunchedPid -OwnerUserByPid $OwnerUserByPid -UserName $UserName)) {
            try {
                Stop-Process -Id $Target.ProcessId -Force -ErrorAction Stop
                $Record.Stopped += "explorer.exe ($($Target.ProcessId))"
            }
            catch { $Record.StopErrors += "explorer.exe ($($Target.ProcessId)): $($_.Exception.Message)" }
        }
        if ($WithWindows) {
            $ToClose = @(Select-C5WindowsToClose -Before $BeforeWindows -Now $NowWindows -ExplorerPids @($ShellPid))
            foreach ($Window in $ToClose) {
                $Posted = Close-C5Window -Handle ([long]$Window.Handle)
                $Record.ClosedWindows += "$($Window.ClassName) '$($Window.Title)' (PID $($Window.ProcessId), PostMessage=$Posted)"
            }
            $Record.WindowsStillOpen = @(Wait-C5WindowsClosed -Handles @($ToClose | ForEach-Object { [long]$_.Handle }) | ForEach-Object { "uchwyt $_" })
        }
        $Record.ShellAliveAfter = [bool](@(Get-C5ProcessSnapshot | Where-Object { [int]$_.ProcessId -eq $ShellPid -and $_.Name -eq 'explorer.exe' }).Count)
    }
    catch { $Record.Error = $_.Exception.Message }
    return [pscustomobject]$Record
}

function Get-C5TaskbarInfo {
    # K2: powłoki dso z sesji zadania i ich okna Shell_TrayWnd (pasek zadań) - stan po sondach Explorera.
    [CmdletBinding()]
    param([ValidatePattern('^[A-Za-z0-9_-]+\z')][string]$UserName = 'dso')
    $Shells = @(Get-C5ShellProcess -UserName $UserName -SessionId (Get-C5CurrentSessionId))
    $ShellPids = @($Shells | ForEach-Object { [int]$_.ProcessId })
    $Tray = @(Get-C5WindowList | Where-Object { $_.ClassName -eq 'Shell_TrayWnd' } | ForEach-Object { "uchwyt $($_.Handle), PID $($_.ProcessId), widoczne=$($_.Visible)" })
    return [pscustomobject]@{ ShellPids = $ShellPids; TrayWindows = $Tray }
}

# --- K2: zadanie Harmonogramu w sesji dso ---

function Get-C5ChildFunctionNames {
    # Funkcje kopiowane do skryptu zadania K2 (domknięcie wywołań pilnuje test).
    [CmdletBinding()]
    [OutputType([string[]])]
    param()
    return @(
        'Get-ExploreRegistryData', 'Open-ExploreRegistryKey', 'Read-ExploreRegistryValue', 'Read-ExploreRegistryKey', 'Invoke-ExploreNative',
        'Test-C5VmComputerName', 'Assert-C5InsideVm', 'Get-C5NativeSource', 'Initialize-C5Native', 'Invoke-C5ElevationQuery', 'Get-C5ProcessTokenInfo',
        'Get-C5CurrentSessionId', 'Get-C5SelfTokenInfo', 'Get-C5ProcessSnapshot', 'Get-C5NewProcesses', 'Get-C5ProcessOwner', 'Get-C5ParentName', 'Get-C5ProcessDetail',
        'Get-C5ShellDetail', 'Get-C5SettingsHosts', 'Select-C5CleanupTargets', 'Select-C5ShellPid', 'Select-C5NewExplorerTargets', 'Get-C5WindowList', 'Close-C5Window',
        'Select-C5WindowsToClose', 'Wait-C5WindowsClosed', 'Select-C5ShellWindows', 'Get-C5NewWindows', 'Resolve-C5SystemFile', 'Start-C5CreateProcess', 'Start-C5Process', 'Get-C5Observation', 'Invoke-C5Cleanup',
        'Invoke-C5LaunchProbe', 'Get-C5PanelAllowNames', 'Get-C5PanelPlan', 'Get-C5PanelProperty', 'Invoke-C5PreClean', 'Invoke-C5PanelVariant', 'Select-C5PanelVariants', 'Invoke-C5PanelRun', 'Get-C5WinUtilCommand',
        'Get-C5WinUtilVariants', 'Test-C5NoNetwork', 'Invoke-C5WinUtilRun', 'Get-C5StorePackageInfo', 'Get-C5StorePackages', 'Register-C5StorePackage',
        'Invoke-C5StoreRegistration', 'Invoke-C5WsReset', 'Get-C5ShellProcess', 'Wait-C5NewShell', 'Invoke-C5ShellKill', 'Invoke-C5ShellStartV32',
        'Get-C5TaskbarInfo'
    )
}

function New-C5ChildScript {
    # Skrypt zadania K2: param(ResultFile), definicje funkcji dziecka, jedno wywołanie (walidowane), wynik JSON przez plik tymczasowy (atomowo).
    [CmdletBinding()]
    [OutputType([string])]
    param([Parameter(Mandatory)][ValidatePattern('^(Invoke|Get)-C5[A-Za-z0-9]+( -[A-Za-z]+ ''[A-Za-z0-9-]+(,[A-Za-z0-9-]+)*'')*\z')][string]$Body)
    $Header = @'
param([Parameter(Mandatory)][string]$ResultFile)
$ErrorActionPreference = 'Stop'
'@
    $Definitions = foreach ($Name in Get-C5ChildFunctionNames) { "function $Name {`n$((Get-Command -Name $Name -CommandType Function -ErrorAction Stop).Definition)`n}" }
    $Main = @'
$Document = [ordered]@{ Ok = $false; Error = $null; ProcessId = $PID; SessionId = $null; Token = $null; Data = @() }
try {
    $Document.Token = Get-C5SelfTokenInfo
    $Document.SessionId = $Document.Token.SessionId
    $Document.Data = @(__BODY__)
    $Document.Ok = $true
}
catch { $Document.Error = $_.Exception.Message }
$Temporary = $ResultFile + '.tmp'
$Document | ConvertTo-Json -Depth 10 | Set-Content -LiteralPath $Temporary -Encoding UTF8
Move-Item -LiteralPath $Temporary -Destination $ResultFile -Force
'@
    return ($Header + "`n`n" + ($Definitions -join "`n`n") + "`n`n" + $Main.Replace('__BODY__', $Body))
}

function Invoke-C5UserTask {
    # K2: zadanie Harmonogramu (principal dso / Interactive / Highest, MS Learn), skrypt -File z C:\dso\c5 (ścieżki bez spacji), wynik z pliku,
    # zadanie ZAWSZE wyrejestrowane. Niedostępność K2 to dana (Available=false, Reason "K2 niedostępne: ..."), nie wyjątek.
    [CmdletBinding()]
    param(
        [Parameter(Mandatory)][ValidatePattern('^[a-z0-9-]+\z')][string]$Name,
        [Parameter(Mandatory)][ValidatePattern('^(Invoke|Get)-C5[A-Za-z0-9]+( -[A-Za-z]+ ''[A-Za-z0-9-]+(,[A-Za-z0-9-]+)*'')*\z')][string]$Body,
        [ValidateRange(10, 3600)][int]$TimeoutSeconds = 300,
        [ValidatePattern('^[A-Za-z0-9_-]+\z')][string]$UserName = 'dso'
    )
    Assert-C5InsideVm
    $Directory = 'C:\dso\c5'
    $TaskName = "DSO-C5-$Name"
    $ScriptPath = "$Directory\$Name.ps1"
    $ResultPath = "$Directory\$Name.json"
    $Out = [ordered]@{
        Name = $Name; Context = 'K2'; Available = $false; Reason = $null; Body = $Body; TaskName = $TaskName; UserName = $UserName; UserShell = $null
        Started = $false; WaitedMs = $null; TaskState = $null; LastTaskResult = $null; ChildProcessId = $null; ChildSessionId = $null; ChildToken = $null
        ChildError = $null; UnregisterError = $null; Data = @()
    }
    try { $Out.UserShell = @(Get-C5ShellProcess -UserName $UserName | ForEach-Object { "explorer.exe PID $($_.ProcessId), sesja $($_.SessionId)" }) }
    catch { $Out.UserShell = "odczyt: $($_.Exception.Message)" }
    $RegisterAttempted = $false
    $Watch = [System.Diagnostics.Stopwatch]::StartNew()
    try {
        $null = New-Item -ItemType Directory -Path $Directory -Force
        Set-Content -LiteralPath $ScriptPath -Value (New-C5ChildScript -Body $Body) -Encoding UTF8
        if (Test-Path -LiteralPath $ResultPath) { Remove-Item -LiteralPath $ResultPath -Force }
        $PowerShellPath = Join-Path $env:SystemRoot 'System32\WindowsPowerShell\v1.0\powershell.exe'
        $Action = New-ScheduledTaskAction -Execute $PowerShellPath -Argument "-NoProfile -NonInteractive -ExecutionPolicy Bypass -WindowStyle Hidden -File $ScriptPath -ResultFile $ResultPath"
        $Principal = New-ScheduledTaskPrincipal -UserId $UserName -LogonType Interactive -RunLevel Highest
        $Settings = New-ScheduledTaskSettingsSet -AllowStartIfOnBatteries -DontStopIfGoingOnBatteries -ExecutionTimeLimit (New-TimeSpan -Seconds ($TimeoutSeconds + 60))
        $RegisterAttempted = $true
        $null = Register-ScheduledTask -TaskName $TaskName -Action $Action -Principal $Principal -Settings $Settings -Force -ErrorAction Stop
        Start-ScheduledTask -TaskName $TaskName -ErrorAction Stop
        $Out.Started = $true
        $Found = $false
        $EndedEarly = $false
        $MaxPolls = $TimeoutSeconds * 2
        for ($Poll = 1; $Poll -le $MaxPolls; $Poll++) {
            if (Test-Path -LiteralPath $ResultPath) { $Found = $true; break }
            # Co 5 s: zadanie już nie działa (Ready/Disabled), a wyniku brak => nie czekamy do końca limitu (K2 niedostępne).
            if ($Poll % 10 -eq 0) {
                $State = $null
                try { $State = [string](Get-ScheduledTask -TaskName $TaskName -ErrorAction Stop).State } catch { $State = $null }
                if ($State -eq 'Ready' -or $State -eq 'Disabled') {
                    if (Test-Path -LiteralPath $ResultPath) { $Found = $true } else { $EndedEarly = $true }
                    break
                }
            }
            Start-Sleep -Milliseconds 500
        }
        $Out.WaitedMs = [int]$Watch.ElapsedMilliseconds
        if ($Found) {
            $Child = Get-Content -LiteralPath $ResultPath -Raw -Encoding UTF8 | ConvertFrom-Json
            $Out.Available = $true
            $Out.ChildProcessId = $Child.ProcessId
            $Out.ChildSessionId = $Child.SessionId
            $Out.ChildToken = $Child.Token
            if (-not $Child.Ok) { $Out.ChildError = $Child.Error }
            $Out.Data = @($Child.Data)
        }
        else {
            try { $Out.TaskState = [string](Get-ScheduledTask -TaskName $TaskName -ErrorAction Stop).State } catch { $Out.TaskState = "odczyt: $($_.Exception.Message)" }
            try { $Out.LastTaskResult = [string](Get-ScheduledTaskInfo -TaskName $TaskName -ErrorAction Stop).LastTaskResult } catch { $Out.LastTaskResult = "odczyt: $($_.Exception.Message)" }
            $What = if ($EndedEarly) { "zadanie zakończyło się bez wyniku po $([int]($Out.WaitedMs / 1000)) s" } else { "zadanie nie zapisało wyniku w $TimeoutSeconds s" }
            $Out.Reason = "K2 niedostępne: $What (stan:$($Out.TaskState), LastTaskResult: $($Out.LastTaskResult); explorer ${UserName}: $(@($Out.UserShell) -join '; '))"
            try { Stop-ScheduledTask -TaskName $TaskName -ErrorAction Stop } catch { $Out.Reason += " | Stop-ScheduledTask: $($_.Exception.Message)" }
        }
    }
    catch { $Out.Reason = "K2 niedostępne: $($_.Exception.Message)" }
    finally {
        if ($RegisterAttempted) {
            try { Unregister-ScheduledTask -TaskName $TaskName -Confirm:$false -ErrorAction Stop }
            catch { $Out.UnregisterError = $_.Exception.Message }
        }
    }
    return [pscustomobject]$Out
}

# --- Sondy ---

function Invoke-C5Part {
    # Jedna część sondy jako rekord {Name, Context, Data, Error}: wyjątek części to dana, kolejne części idą dalej.
    [CmdletBinding()]
    param(
        [Parameter(Mandatory)][string]$Name,
        [Parameter(Mandatory)][string]$Context,
        [Parameter(Mandatory)][scriptblock]$Script
    )
    $Watch = [System.Diagnostics.Stopwatch]::StartNew()
    $Part = [ordered]@{ Name = $Name; Context = $Context; Skipped = $null; Error = $null; DurationMs = $null; Data = @() }
    try { $Part.Data = @(& $Script) }
    catch { $Part.Error = $_.Exception.Message }
    $Part.DurationMs = [int]$Watch.ElapsedMilliseconds
    return [pscustomobject]$Part
}

function Invoke-C5PanelsProbe {
    # (1)-(4): ochrona systemu (tylko odczyt), panele w K1, panele w K2 (zadanie; limit wg liczby wybranych wariantów).
    [CmdletBinding()]
    param([ValidatePattern('^[a-z0-9-]+\z')][string[]]$Variant)
    Assert-C5InsideVm
    $Names = @($Variant | Where-Object { $_ })
    Invoke-C5Part -Name 'ochrona-systemu' -Context 'K1' -Script { Get-C5SystemProtectionState }
    Invoke-C5Part -Name 'panele-k1' -Context 'K1' -Script { Invoke-C5PanelRun -Context 'K1' -Variant $Names }
    $Count = @(Select-C5PanelVariants -Names $Names).Count
    $Body = "Invoke-C5PanelRun -Context 'K2'"
    if ($Names.Count -gt 0) { $Body += " -Variant '$($Names -join ',')'" }
    Invoke-C5UserTask -Name 'panele-k2' -Body $Body -TimeoutSeconds ($Count * 25 + 60)
}

function Invoke-C5WinUtilProbe {
    # (12): najpierw sieć w K1; z siecią (albo bez potwierdzenia) nic nie startuje, także zadanie K2.
    [CmdletBinding()]
    param()
    Assert-C5InsideVm
    $Network = Test-C5NoNetwork
    [pscustomobject]@{ Name = 'siec'; Context = 'K1'; Skipped = $null; Error = $null; DurationMs = $null; Data = @($Network) }
    if (-not $Network.NoNetwork) {
        foreach ($Name in 'winutil-k1', 'winutil-k2') { [pscustomobject]@{ Name = $Name; Context = $Name.Substring($Name.Length - 2).ToUpperInvariant(); Skipped = $Network.Reason; Error = $null; DurationMs = $null; Data = @() } }
        return
    }
    Invoke-C5Part -Name 'winutil-k1' -Context 'K1' -Script { Invoke-C5WinUtilRun -Context 'K1' }
    Invoke-C5UserTask -Name 'winutil-k2' -Body "Invoke-C5WinUtilRun -Context 'K2'" -TimeoutSeconds 120
}

function Invoke-C5StoreProbe {
    # (8)-(11): odczyty (K1 -AllUsers, K1 bieżące = Administrator, K2 = dso), rejestracja K1 i K2, WSReset -i w K1 i K2.
    [CmdletBinding()]
    param()
    Assert-C5InsideVm
    Invoke-C5Part -Name 'store-pakiety-wszyscy-k1' -Context 'K1' -Script { Get-C5StorePackages -AllUsers }
    Invoke-C5Part -Name 'store-pakiety-biezace-k1' -Context 'K1' -Script { Get-C5StorePackages }
    Invoke-C5UserTask -Name 'store-pakiety-k2' -Body 'Get-C5StorePackages' -TimeoutSeconds 120
    Invoke-C5Part -Name 'store-rejestracja-k1' -Context 'K1' -Script { Invoke-C5StoreRegistration -Context 'K1' }
    Invoke-C5UserTask -Name 'store-rejestracja-k2' -Body "Invoke-C5StoreRegistration -Context 'K2'" -TimeoutSeconds 420
    Invoke-C5Part -Name 'wsreset-k1' -Context 'K1' -Script { Invoke-C5WsReset -Context 'K1' }
    Invoke-C5UserTask -Name 'wsreset-k2' -Body "Invoke-C5WsReset -Context 'K2'" -TimeoutSeconds 120
}

function Invoke-C5ExplorerProbe {
    # (5)-(7): AutoRestartShell (tylko odczyt), token po logowaniu, trzy zatrzymania z pomiarem powrotu, wariant v3.2 w K1 i K2, stan końcowy.
    [CmdletBinding()]
    param()
    Assert-C5InsideVm
    Invoke-C5Part -Name 'autorestartshell' -Context 'K1' -Script { Read-ExploreRegistryValue -Path 'HKLM\SOFTWARE\Microsoft\Windows NT\CurrentVersion\Winlogon' -Name 'AutoRestartShell' }
    Invoke-C5Part -Name 'explorer-po-logowaniu' -Context 'K1' -Script { Get-C5ShellProcess -UserName 'dso' | ForEach-Object { Get-C5ShellDetail -Entry $_ } }
    Invoke-C5Part -Name 'explorer-stop-process-k1' -Context 'K1' -Script { Invoke-C5ShellKill -Method 'StopProcess' -Context 'K1' }
    Invoke-C5Part -Name 'explorer-taskkill-k1' -Context 'K1' -Script { Invoke-C5ShellKill -Method 'Taskkill' -Context 'K1' }
    Invoke-C5UserTask -Name 'explorer-stop-process-k2' -Body "Invoke-C5ShellKill -Method 'StopProcess' -Context 'K2'" -TimeoutSeconds 180
    Invoke-C5Part -Name 'explorer-v32-k1' -Context 'K1' -Script { Invoke-C5ShellStartV32 -Context 'K1' }
    Invoke-C5UserTask -Name 'explorer-v32-k2' -Body "Invoke-C5ShellStartV32 -Context 'K2'" -TimeoutSeconds 120
    Invoke-C5UserTask -Name 'explorer-taskbar-k2' -Body 'Get-C5TaskbarInfo' -TimeoutSeconds 90
    Invoke-C5Part -Name 'explorer-koniec-k1' -Context 'K1' -Script { Get-C5ShellProcess -UserName 'dso' | ForEach-Object { Get-C5ShellDetail -Entry $_ } }
}

function Invoke-C5Collection {
    # Punkt wejścia po stronie VM: JEDEN string JSON (meta + userSid + token K1 + powłoka dso + wybrane sondy w stałej kolejności).
    [CmdletBinding()]
    param(
        [string]$UserSid,
        [ValidateSet('panels', 'winutil', 'store', 'explorer')][string[]]$Probe,
        [ValidatePattern('^[a-z0-9-]+\z')][string[]]$Variant
    )
    Assert-C5InsideVm
    $PanelVariants = @($Variant | Where-Object { $_ })
    $Probes = [ordered]@{
        panels   = { Invoke-C5PanelsProbe -Variant $PanelVariants }
        winutil  = { Invoke-C5WinUtilProbe }
        store    = { Invoke-C5StoreProbe }
        explorer = { Invoke-C5ExplorerProbe }
    }
    if ($Probe) {
        $Selected = [ordered]@{}
        foreach ($Key in $Probes.Keys) { if ($Probe -contains $Key) { $Selected[$Key] = $Probes[$Key] } }
        $Probes = $Selected
    }
    $Shell = $null
    try { $Shell = @(Get-C5ShellProcess -UserName 'dso' | ForEach-Object { "explorer.exe PID $($_.ProcessId), sesja $($_.SessionId), $($_.Owner)" }) }
    catch { $Shell = "odczyt: $($_.Exception.Message)" }
    $Extra = [ordered]@{ userSid = $UserSid; userName = 'dso'; panelVariants = @($PanelVariants); token = (Get-C5SelfTokenInfo); userShell = $Shell }
    return (Invoke-ExploreCollection -Probe $Probes -Extra $Extra)
}

# ---------------------------------------------------------------------------------------------------------------------
# Strona hosta (nic z tego nie jest wysyłane do VM).
# ---------------------------------------------------------------------------------------------------------------------

function Get-C5RemoteFunctionNames {
    return @(
        'Test-C5VmComputerName', 'Assert-C5InsideVm', 'Get-C5NativeSource', 'Initialize-C5Native', 'Invoke-C5ElevationQuery', 'Get-C5ProcessTokenInfo',
        'Get-C5CurrentSessionId', 'Get-C5SelfTokenInfo', 'Get-C5ProcessSnapshot', 'Get-C5NewProcesses', 'Get-C5ProcessOwner', 'Get-C5ParentName', 'Get-C5ProcessDetail',
        'Get-C5ShellDetail', 'Get-C5SettingsHosts', 'Select-C5CleanupTargets', 'Select-C5ShellPid', 'Select-C5NewExplorerTargets', 'Get-C5WindowList', 'Close-C5Window',
        'Select-C5WindowsToClose', 'Wait-C5WindowsClosed', 'Select-C5ShellWindows', 'Get-C5NewWindows', 'Resolve-C5SystemFile', 'Start-C5CreateProcess', 'Start-C5Process', 'Get-C5Observation', 'Invoke-C5Cleanup',
        'Invoke-C5LaunchProbe', 'Get-C5PanelAllowNames', 'Get-C5PanelPlan', 'Get-C5PanelProperty', 'Invoke-C5PreClean', 'Invoke-C5PanelVariant', 'Select-C5PanelVariants', 'Invoke-C5PanelRun', 'Get-C5SystemProtectionState',
        'Get-C5WinUtilCommand', 'Get-C5WinUtilVariants', 'Test-C5NoNetwork', 'Invoke-C5WinUtilRun', 'Get-C5StorePackageInfo', 'Get-C5StorePackages',
        'Register-C5StorePackage', 'Invoke-C5StoreRegistration', 'Invoke-C5WsReset', 'Get-C5ShellProcess', 'Wait-C5NewShell', 'Invoke-C5ShellKill',
        'Invoke-C5ShellStartV32', 'Get-C5TaskbarInfo', 'Get-C5ChildFunctionNames', 'New-C5ChildScript', 'Invoke-C5UserTask', 'Invoke-C5Part',
        'Invoke-C5PanelsProbe', 'Invoke-C5WinUtilProbe', 'Invoke-C5StoreProbe', 'Invoke-C5ExplorerProbe', 'Invoke-C5Collection'
    )
}

function Get-C5HostOnlyFunctionNames {
    return @('Get-C5RemoteFunctionNames', 'Get-C5HostOnlyFunctionNames', 'Get-C5RemoteScript', 'New-C5EntryPoint', 'Assert-C5OutDirectory', 'Get-C5RunArguments', 'Invoke-C5Exploration')
}

function Get-C5RunArguments {
    # Argumenty Invoke-C5Exploration z bloku głównego, bez pustych wartości: brak -Probe = pełna lista sond w kolejności przebiegu
    # (parametr z ValidateSet nie przyjmuje $null - błąd z przebiegu 2026-10-19).
    [CmdletBinding()]
    param(
        [Parameter(Mandatory)][string[]]$VmName,
        [Parameter(Mandatory)][string]$OutDirectory,
        [Parameter(Mandatory)][string]$CheckpointName,
        [Parameter(Mandatory)][int]$ReadyTimeoutSeconds,
        [AllowNull()][AllowEmptyCollection()][string[]]$Probe,
        [AllowNull()][AllowEmptyCollection()][string[]]$Variant
    )
    $All = @('panels', 'winutil', 'store', 'explorer')
    $Selected = if ($null -ne $Probe -and @($Probe | Where-Object { $_ }).Count -gt 0) { @($All | Where-Object { $Probe -contains $_ }) } else { $All }
    $Variants = @($Variant | Where-Object { $_ } | Select-Object -Unique)
    if ($Variants.Count -gt 0 -and $Selected -notcontains 'panels') { throw '-Variant dotyczy tylko sondy panels (dodaj panels do -Probe albo pomiń -Probe).' }
    return [ordered]@{ VmName = @($VmName); OutDirectory = $OutDirectory; CheckpointName = $CheckpointName; ReadyTimeoutSeconds = $ReadyTimeoutSeconds; Probe = @($Selected); Variant = @($Variants) }
}

function Get-C5RemoteScript {
    # Tekst definicji funkcji zdalnych: część wspólna z modułu + funkcje C5 (FunctionInfo pobrane tu, w zasięgu skryptu).
    [CmdletBinding()]
    [OutputType([string])]
    param()
    $Names = @(Get-ExploreCommonRemoteFunctionNames) + @(Get-C5RemoteFunctionNames)
    return (ConvertTo-ExploreRemoteScript -Command @($Names | ForEach-Object { Get-Command -Name $_ -CommandType Function -ErrorAction Stop }))
}

function New-C5EntryPoint {
    # Funkcja Invoke-C5EntryPoint dopisywana do skryptu zdalnego przy -Probe; przyjmuje -UserSid (Invoke-ExploreVmRun -PassUserSid).
    # Body: dokładnie "Invoke-C5Collection" z opcjonalnym -Probe @('a','b') i opcjonalnym -Variant @('x-y','z'); wszystko inne odrzucone.
    [CmdletBinding()]
    [OutputType([string])]
    param([Parameter(Mandatory)][ValidatePattern('^Invoke-C5Collection( -Probe @\(''[A-Za-z]+''(,''[A-Za-z]+'')*\))?( -Variant @\(''[a-z0-9-]+''(,''[a-z0-9-]+'')*\))?\z')][string]$Body)
    return "function Invoke-C5EntryPoint {`nparam([string]`$UserSid)`n$Body -UserSid `$UserSid`n}"
}

function Assert-C5OutDirectory {
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

function Invoke-C5Exploration {
    # Wszystkie wybrane VM po kolei. Błąd uwierzytelnienia = stop całości; inny błąd = zapis i dalej (logika we wspólnym module).
    [CmdletBinding()]
    param(
        [Parameter(Mandatory)][ValidateSet('DSO-26H2-Home', 'DSO-26H2-Pro')][string[]]$VmName,
        [Parameter(Mandatory)][string]$OutDirectory,
        [string]$CheckpointName = 'czysta-26H2',
        [int]$ReadyTimeoutSeconds = 1200,
        [ValidateSet('panels', 'winutil', 'store', 'explorer')][string[]]$Probe,
        [AllowEmptyCollection()][ValidateSet('visual-effects-ms-exe', 'visual-effects-v32-sysdm-3', 'power-options-winutil', 'power-options-v32-control', 'power-options-ms-name',
            'network-connections-winutil', 'network-connections-ms-netconnections', 'computer-management-winutil', 'control-panel-winutil', 'mouse-winutil',
            'mouse-ms-name', 'devices-printers-winutil', 'devices-printers-ms-name', 'devices-printers-explorer', 'programs-features-winutil',
            'programs-features-ms-name', 'region-winutil', 'region-ms-name', 'security-maintenance-winutil', 'security-maintenance-control', 'sound-winutil',
            'sound-ms-name', 'system-properties-winutil', 'system-properties-ms-name', 'system-properties-control', 'date-time-winutil', 'date-time-ms-name',
            'firewall-winutil', 'firewall-ms-name', 'system-restore-winutil')][string[]]$Variant = @()
    )
    if (@($Variant).Count -gt 0 -and $Probe -and $Probe -notcontains 'panels') { throw '-Variant dotyczy tylko sondy panels.' }
    $RemoteScript = Get-C5RemoteScript
    $Entry = 'Invoke-C5Collection'
    if ($Probe -or @($Variant).Count -gt 0) {
        $Ordered = @(@('panels', 'winutil', 'store', 'explorer') | Where-Object { -not $Probe -or $Probe -contains $_ })
        $Body = 'Invoke-C5Collection -Probe @(' + (($Ordered | ForEach-Object { "'$_'" }) -join ',') + ')'
        if (@($Variant).Count -gt 0) { $Body += ' -Variant @(' + ((@($Variant) | ForEach-Object { "'$_'" }) -join ',') + ')' }
        $RemoteScript = $RemoteScript + "`n`n" + (New-C5EntryPoint -Body $Body)
        $Entry = 'Invoke-C5EntryPoint'
    }
    return (Invoke-ExploreAll -VmName $VmName -OutDirectory $OutDirectory -FileName 'c5-exploration.json' -RemoteScript $RemoteScript -EntryCommand $Entry -PassUserSid -CheckpointName $CheckpointName -ReadyTimeoutSeconds $ReadyTimeoutSeconds)
}

# Przy dot-sourcingu (testy) tylko definicje; uruchomienie przebiegu tylko przy zwykłym wywołaniu skryptu.
if ($MyInvocation.InvocationName -ne '.') {
    $ExitCode = 1
    try {
        Set-StrictMode -Version Latest
        $ErrorActionPreference = 'Stop'
        $VmRoot = Split-Path -Parent $PSScriptRoot
        # Walidacja na starcie, przed jakąkolwiek pracą na VM; katalog tworzy dopiero Save-ExploreResult (bez New-Item na hoście).
        # Resolve-ExploreOutDirectory pochodzi z ExploreCommon (import na górze skryptu); Get-DsoRepoRoot z VmCommon, który ExploreCommon ładuje
        # tylko we własnym zakresie - w świeżej sesji trzeba go zaimportować tutaj (błąd wykryty uruchomieniem w czystym runspace).
        Import-Module (Join-Path $VmRoot 'modules\VmCommon.psm1')
        $OutDirectory = Assert-C5OutDirectory -Path $OutDirectory -RepoRoot (Get-DsoRepoRoot)
        $OutDirectory = Resolve-ExploreOutDirectory -Path $OutDirectory
        $RunArguments = Get-C5RunArguments -VmName $VmName -OutDirectory $OutDirectory -CheckpointName $CheckpointName -ReadyTimeoutSeconds $ReadyTimeoutSeconds -Probe $Probe -Variant $Variant
        if ($ShowPlan) {
            [pscustomobject]$RunArguments | ConvertTo-Json -Depth 3
            exit 0
        }
        Import-Module (Join-Path $VmRoot 'modules\VmCommon.psm1') -Force
        Import-Module (Join-Path $VmRoot 'modules\VmRun.psm1') -Force
        Import-Module (Join-Path $VmRoot 'modules\VmHost.psm1') -Force
        # ExploreCommon (zaimportowany wyżej, przed tymi modułami) trzyma referencję do starej instancji VmHost; ponowny import po nich wiąże go z bieżącą.
        Import-Module (Join-Path $PSScriptRoot 'ExploreCommon.psm1') -Force
        $Principal = New-Object System.Security.Principal.WindowsPrincipal([System.Security.Principal.WindowsIdentity]::GetCurrent())
        if (-not $Principal.IsInRole([System.Security.Principal.WindowsBuiltInRole]::Administrator)) {
            throw 'Uruchom skrypt w sesji PowerShell jako administrator (Hyper-V i PowerShell Direct tego wymagają).'
        }
        Write-Host "Wyniki: $OutDirectory"
        Write-Host "Sondy: $($RunArguments.Probe -join ', '); warianty paneli: $(if ($RunArguments.Variant.Count -gt 0) { $RunArguments.Variant -join ', ' } else { 'wszystkie' }); VM: $($RunArguments.VmName -join ', ')"
        $Outcome = Invoke-C5Exploration @RunArguments
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
