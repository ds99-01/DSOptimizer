<#
Instrukcja uruchomienia:
    Sesja PowerShell uruchomiona jako administrator, na hoście z Hyper-V, z modułami testów VM (tests\vm\modules) i tests\vm\explore\ExploreCommon.psm1.
    VM utworzone wcześniej przez New-TestVm.ps1 (checkpoint 'czysta-26H2', poświadczenia w %LOCALAPPDATA%\DSOptimizer-vm, autologon 'dso').
    Build DSOptimizera NIE jest potrzebny (pomiar woła wyłącznie narzędzia systemowe w VM).
    Przykład (jedna VM na proces; Home i Pro można uruchomić równolegle w dwóch sesjach, każda z własną VM):
        .\tests\vm\explore\Invoke-E4B2Exploration.ps1 -VmName DSO-26H2-Pro -OutDirectory $env:TEMP\dso-explore\exploration
    Bez -Phase: wszystkie fazy (baseline zawsze pierwsza, final zawsze ostatnia, jeśli wybrana).
    -NoNetwork: pomija fazę network; karta VM nie jest podłączana do 'Default Switch'.
    -ShowPlan: wypisuje rozstrzygnięte argumenty i plan (JSON) i kończy, nic nie uruchamia.
    -OutDirectory jest wymagany: ścieżka bezwzględna poza repo; w repo tylko katalog ignorowany przez git.
    Czas: ok. 1,5–2 godziny na VM (DISM trzy razy, SFC, StartComponentCleanup, cleanmgr dwa razy, DISM z siecią).
Opis działania:
    Pomiar partii 2 Etapu 4b (długie naprawy): DISM /RestoreHealth i /ScanHealth, SFC /scannow, wykrywanie trwającej operacji
    serwisowania, czyszczenie dysku (stare pliki tymczasowe, Optymalizacja dostarczania, SoftwareDistribution\Download,
    StartComponentCleanup, miniatury, porównanie z cleanmgr /sagerun) i Windows.old przez cleanmgr („Previous Installations”) – raz
    z PowerShell Direct (sesja 0) i raz w sesji dso (zadanie Harmonogramu Interactive + Highest, czyli jak DSOptimizer po UAC).
    Zakres zawężony decyzją Dawida (R8, 2026-10-22): żadnego uszkadzania plików systemowych, żadnej zmiany właściciela ani ACL;
    SFC i DISM tylko na zdrowej VM; Windows.old = zwykły folder z plikami testowymi utworzony kontem Administratora.
    Wszystkie zmiany WYŁĄCZNIE w VM DSO-26H2-Home/Pro; na hoście skrypt tylko podłącza/odłącza kartę sieciową VM (Default Switch,
    wyłącznie na fazę network), kopiuje surowe pliki wyników z VM i zapisuje JSON.
    Narzędzia startują z pełnej ścieżki System32 (Process, CreateNoWindow jak CREATE_NO_WINDOW w DSOptimizerze); stdout i stderr
    trafiają BAJTOWO do plików C:\dso\e4b2\raw\<nazwa>.out.bin/.err.bin (bez dekodowania), a segmenty CBS.log i dism.log z danego
    przebiegu (od offsetu sprzed startu) do <nazwa>.cbs.log/.dism.log. W trakcie co kilka sekund próbka procesów z listy
    serwisowania i długość pliku wyjścia (czy postęp płynie na bieżąco).
    Przebieg jednej VM: revert do 'czysta-26H2' -> start (bez sieci) -> gotowość (zalogowany dso) -> fazy: baseline (stan i bezczynna VM)
    -> dism -> sfc -> cleanup -> windowsold -> [sieć] network [koniec sieci] -> final (bezczynna VM po wszystkim) -> kopia surowych
    plików -> ZAWSZE (finally) revert, odłączenie karty i sprawdzenie stanu końcowego (VM Off, karta bez przełącznika) przed zapisem JSON.
    Ostatni bezpiecznik po stronie VM: Assert-E4B2InsideVm (nazwa komputera DSO-26H2-HOME/PRO i brak DSO_VM_TESTS_MOCK_ONLY) na
    początku każdej funkcji ze skutkiem. Pierwszy błąd uwierzytelnienia przerywa cały skrypt (blokada konta). Skrypt dotyka wyłącznie
    VM DSO-26H2-Home i DSO-26H2-Pro (ValidateSet), nigdy innej VM; na hoście niczego nie zmienia.
    Wynik: <OutDirectory>\<VmName>\e4b2.json (UTF-8) i <OutDirectory>\<VmName>\raw\*.
    Kod wyjścia: 0 gdy wszystkie wybrane VM zebrane bez błędu przebiegu, 1 w przeciwnym razie.
#>
[CmdletBinding()]
param(
    [ValidateSet('DSO-26H2-Home', 'DSO-26H2-Pro')][string[]]$VmName = @('DSO-26H2-Home', 'DSO-26H2-Pro'),
    [ValidateSet('baseline', 'dism', 'sfc', 'cleanup', 'windowsold', 'network', 'final')][string[]]$Phase = @('baseline', 'dism', 'sfc', 'cleanup', 'windowsold', 'network', 'final'),
    [string]$OutDirectory,
    [string]$CheckpointName = 'czysta-26H2',
    [ValidateRange(60, 7200)][int]$ReadyTimeoutSeconds = 1200,
    [switch]$NoNetwork,
    [switch]$ShowPlan
)

Import-Module (Join-Path $PSScriptRoot 'ExploreCommon.psm1') -Force

# ---------------------------------------------------------------------------------------------------------------------
# Plan (dane z planu Tasku 2 partii 2 Etapu 4b, zakres R8).
# ---------------------------------------------------------------------------------------------------------------------

function Get-E4B2Plan {
    [CmdletBinding()]
    param()
    return [ordered]@{
        # Lista z planu (pkt 3): kandydaci na IServicingActivity; pomiar ustala, które z nich nie dają fałszywych alarmów.
        WatchNames               = @('dism', 'DismHost', 'sfc', 'TiWorker', 'TrustedInstaller', 'cleanmgr')
        IdleSampleSeconds        = 180
        FinalIdleSeconds         = 120
        SampleSeconds            = 5
        ConcurrentDelaySeconds   = 30
        ConcurrentTimeoutSeconds = 3600
        DismTimeoutSeconds       = 3600
        SfcTimeoutSeconds        = 3600
        CleanupTimeoutSeconds    = 3600
        CleanmgrTimeoutSeconds   = 1200
        NoSourcePath             = 'C:\brak'
        ConcurrentVerifyFile     = 'C:\Windows\System32\kernel32.dll'
        # StateFlags<NNNN>: numer spoza zwyczajowych (0001, 0064, 65535); baseline sprawdza, że nikt go nie używa.
        SageRunId                = 7351
        VolumeCachesKey          = 'HKLM\SOFTWARE\Microsoft\Windows\CurrentVersion\Explorer\VolumeCaches'
        TempHandler              = 'Temporary Files'
        PreviousHandler          = 'Previous Installations'
        WindowsOldPath           = 'C:\Windows.old'
        WindowsOldMegabytes      = 200
        OldAgeDays               = 2
        VeryOldAgeDays           = 10
        TempFileBytes            = 1048576
        DownloadTestMegabytes    = 60
        GuardRoot                = 'C:\dso\e4b2\guard'
        RawDirectory             = 'C:\dso\e4b2\raw'
        NetworkWaitSeconds       = 120
        NetworkName              = 'www.microsoft.com'
        Services                 = @('wuauserv', 'bits', 'DoSvc', 'UsoSvc', 'TrustedInstaller', 'cryptsvc')
    }
}

# ---------------------------------------------------------------------------------------------------------------------
# Funkcje wysyłane do VM (samowystarczalne: wołają siebie nawzajem, funkcje Explore z modułu i cmdlety systemowe).
# ---------------------------------------------------------------------------------------------------------------------

function Test-E4B2VmComputerName {
    # Nazwy komputerów VM nadawane przez New-TestVm.ps1 (VmBuild: nazwa VM wielkimi literami).
    [CmdletBinding()]
    [OutputType([bool])]
    param([AllowEmptyString()][string]$ComputerName)
    return (@('DSO-26H2-HOME', 'DSO-26H2-PRO') -contains ([string]$ComputerName).ToUpperInvariant())
}

function Assert-E4B2InsideVm {
    # Ostatni bezpiecznik: sondy E4B2 uruchamiają DISM, SFC i cleanmgr, usuwają pliki, zatrzymują usługi i piszą do rejestru,
    # więc działają WYŁĄCZNIE w VM DSO-26H2-Home/Pro i nigdy w testach na mockach (DSO_VM_TESTS_MOCK_ONLY).
    [CmdletBinding()]
    param([string]$ComputerName = $env:COMPUTERNAME)
    if ($env:DSO_VM_TESTS_MOCK_ONLY) {
        throw 'Tryb DSO_VM_TESTS_MOCK_ONLY: sondy E4B2 nie mogą działać w testach na mockach (nic nie zostało uruchomione).'
    }
    if (-not (Test-E4B2VmComputerName -ComputerName $ComputerName)) {
        throw "Sondy E4B2 działają tylko w VM DSO-26H2-HOME/DSO-26H2-PRO (ten komputer: '$ComputerName'); nic nie zostało uruchomione."
    }
}

function Format-E4B2Error {
    # Wyjątek -> '0xHRESULT komunikat' (błąd bazowy).
    [CmdletBinding()]
    [OutputType([string])]
    param([Parameter(Mandatory)]$ErrorRecord)
    $Exception = if ($ErrorRecord -is [System.Management.Automation.ErrorRecord]) { $ErrorRecord.Exception } else { $ErrorRecord }
    $Base = $Exception.GetBaseException()
    return ('0x{0:X8} {1}' -f $Base.HResult, $Base.Message)
}

function Get-E4B2ToolPath {
    # Tylko narzędzia pomiaru, tylko z System32, tylko sama nazwa pliku (nigdy ścieżka, PATH ani katalog bieżący).
    [CmdletBinding()]
    [OutputType([string])]
    param([Parameter(Mandatory)][string]$Tool)
    if (@('dism.exe', 'sfc.exe', 'cleanmgr.exe') -notcontains $Tool.ToLowerInvariant()) { throw "Narzędzie '$Tool' nie jest dozwolone w pomiarze E4B2." }
    return (Join-Path $env:SystemRoot "System32\$Tool")
}

function Get-E4B2StateFlagsName {
    [CmdletBinding()]
    [OutputType([string])]
    param([Parameter(Mandatory)][int]$Id)
    return ('StateFlags{0:D4}' -f $Id)
}

function Get-E4B2OutputAnalysis {
    # Analiza surowych bajtów wyjścia narzędzia: BOM, kodowanie (UTF-16LE po BOM albo po zerach na nieparzystych pozycjach,
    # inaczej strona OEM), rodzaje końców linii, procenty postępu i separator dziesiętny, pierwsze/ostatnie linie, pierwszy znak.
    [CmdletBinding()]
    param(
        [Parameter(Mandatory)][AllowEmptyCollection()][byte[]]$Bytes,
        [Parameter(Mandatory)][int]$OemCodePage
    )
    $Length = $Bytes.Length
    $Bom = 'none'
    $Skip = 0
    if ($Length -ge 2 -and $Bytes[0] -eq 0xFF -and $Bytes[1] -eq 0xFE) { $Bom = 'utf16le'; $Skip = 2 }
    elseif ($Length -ge 3 -and $Bytes[0] -eq 0xEF -and $Bytes[1] -eq 0xBB -and $Bytes[2] -eq 0xBF) { $Bom = 'utf8'; $Skip = 3 }
    $OddZeros = 0
    $Pairs = 0
    for ($Index = 1; $Index -lt [math]::Min($Length, 4096); $Index += 2) { $Pairs++; if ($Bytes[$Index] -eq 0) { $OddZeros++ } }
    $Encoding = "oem$OemCodePage"
    if ($Bom -eq 'utf16le' -or ($Bom -eq 'none' -and $Pairs -gt 0 -and $OddZeros -ge [math]::Ceiling($Pairs * 0.4))) { $Encoding = 'utf16le' }
    elseif ($Bom -eq 'utf8') { $Encoding = 'utf8' }
    $Decoder = switch ($Encoding) { 'utf16le' { [System.Text.Encoding]::Unicode } 'utf8' { [System.Text.Encoding]::UTF8 } default { [System.Text.Encoding]::GetEncoding($OemCodePage) } }
    $Text = if ($Length -gt $Skip) { $Decoder.GetString($Bytes, $Skip, $Length - $Skip) } else { '' }
    $NonAscii = 0
    foreach ($Byte in $Bytes) { if ($Byte -ge 0x80) { $NonAscii++ } }
    $CrLf = ([regex]::Matches($Text, "`r`n")).Count
    $CrOnly = ([regex]::Matches($Text, "`r(?!`n)")).Count
    $LfOnly = ([regex]::Matches($Text, "(?<!`r)`n")).Count
    $PercentMatches = [regex]::Matches($Text, '(\d{1,3}(?:[.,]\d+)?)\s?%')
    $Percents = @($PercentMatches | Select-Object -First 300 | ForEach-Object { $_.Groups[1].Value })
    $Separators = @($Percents | ForEach-Object { if ($_ -match '\.') { '.' } elseif ($_ -match ',') { ',' } } | Sort-Object -Unique)
    $DecimalSeparator = if ($Separators.Count -eq 0) { 'brak' } elseif ($Separators.Count -eq 1) { $Separators[0] } else { 'mieszany' }
    $Contexts = @($PercentMatches | Select-Object -First 3 | ForEach-Object {
            $From = [math]::Max(0, $_.Index - 30)
            $To = [math]::Min($Text.Length, $_.Index + $_.Length + 30)
            $Text.Substring($From, $To - $From) -replace "`r", '\r' -replace "`n", '\n'
        })
    $Lines = @($Text -split "`r`n|`r|`n" | ForEach-Object { $_.TrimEnd() } | Where-Object { $_ -ne '' })
    $FirstHex = (@($Bytes | Select-Object -First 16 | ForEach-Object { '{0:X2}' -f $_ }) -join ' ')
    $FirstChar = if ($Text.Length -gt 0) { 'U+{0:X4}' -f [int]$Text[0] } else { $null }
    return [pscustomobject]@{
        Length = $Length; Bom = $Bom; Encoding = $Encoding; FirstBytesHex = $FirstHex; FirstChar = $FirstChar; NonAsciiBytes = $NonAscii
        CrLf = $CrLf; CrOnly = $CrOnly; LfOnly = $LfOnly; PercentCount = $PercentMatches.Count; Percents = $Percents; DecimalSeparator = $DecimalSeparator
        PercentContexts = $Contexts; FirstLines = @($Lines | Select-Object -First 12); LastLines = @($Lines | Select-Object -Last 15); LineCount = $Lines.Count
    }
}

function Get-E4B2SrSummary {
    # Linie [SR] z segmentu CBS.log: liczba, liczniki znanych znaczników (angielskie, z dokumentacji MS i CBS) i szablony (cyfry -> #).
    [CmdletBinding()]
    param([Parameter(Mandatory)][AllowEmptyString()][string]$Text)
    $Markers = @('Beginning Verify and Repair transaction', 'Verifying', 'Verify complete', 'Beginning Verify and Repair', 'Repairing', 'Repair complete', 'Repaired file',
        'Cannot repair member file', 'Could not reproject corrupted file', 'Committing', 'Unable to repair', 'repair pending', 'reboot')
    $Lines = @($Text -split "`r`n|`r|`n" | Where-Object { $_ -match '\[SR\]' })
    $Counts = [ordered]@{}
    foreach ($Marker in $Markers) { $Counts[$Marker] = @($Lines | Where-Object { $_.IndexOf($Marker, [System.StringComparison]::OrdinalIgnoreCase) -ge 0 }).Count }
    $Templates = @($Lines | ForEach-Object { (($_ -split '\[SR\]\s*', 2)[1] -replace '\d+', '#').Trim() } | Select-Object -Unique | Select-Object -First 60)
    return [pscustomobject]@{ SrLineCount = $Lines.Count; Counts = $Counts; Templates = $Templates; FirstLines = @($Lines | Select-Object -First 40); LastLines = @($Lines | Select-Object -Last 40) }
}

function Get-E4B2KeyLines {
    # Linie pasujące do wzorca: pierwsze Head i ostatnie Tail (z '…' pomiędzy, gdy coś pominięto).
    [CmdletBinding()]
    param(
        [Parameter(Mandatory)][AllowEmptyString()][string]$Text,
        [Parameter(Mandatory)][string]$Pattern,
        [int]$Head = 150,
        [int]$Tail = 100
    )
    $Lines = @($Text -split "`r`n|`r|`n" | Where-Object { $_ -ne '' -and $_ -match $Pattern })
    if ($Lines.Count -le ($Head + $Tail)) { return $Lines }
    return (@($Lines | Select-Object -First $Head) + @('…') + @($Lines | Select-Object -Last $Tail))
}

function Get-E4B2PresenceSummary {
    # Dla każdej nazwy z listy: w ilu próbkach był proces, pierwsza i ostatnia sekunda.
    [CmdletBinding()]
    param(
        [Parameter(Mandatory)][AllowEmptyCollection()][object[]]$Samples,
        [Parameter(Mandatory)][string[]]$WatchNames
    )
    $Out = [ordered]@{ Total = $Samples.Count }
    foreach ($Name in $WatchNames) {
        $Hits = @($Samples | Where-Object { @($_.Present | Where-Object { ([string]$_).Split(':')[0] -ieq $Name }).Count -gt 0 })
        $Out[$Name] = [pscustomobject]@{
            Samples      = $Hits.Count
            FirstSeconds = if ($Hits.Count -gt 0) { $Hits[0].T } else { $null }
            LastSeconds  = if ($Hits.Count -gt 0) { $Hits[-1].T } else { $null }
        }
    }
    return $Out
}

# --- odczyty (bez skutku) ---

function Get-E4B2ProcessSample {
    # Procesy z listy w tej chwili: 'nazwa:PID:sesja'.
    [CmdletBinding()]
    param([Parameter(Mandatory)][string[]]$Names)
    return @(Get-Process -Name $Names -ErrorAction SilentlyContinue | Sort-Object ProcessName, Id | ForEach-Object { '{0}:{1}:{2}' -f $_.ProcessName, $_.Id, $_.SessionId })
}

function Get-E4B2ChildProcesses {
    # Procesy potomne danego PID: 'nazwa:PID' (np. DismHost uruchomiony przez cleanmgr).
    [CmdletBinding()]
    param([Parameter(Mandatory)][int]$ParentId)
    return @(Get-CimInstance -ClassName Win32_Process -Filter "ParentProcessId=$ParentId" -ErrorAction SilentlyContinue | ForEach-Object { '{0}:{1}' -f ($_.Name -replace '\.exe$', ''), $_.ProcessId })
}

function Get-E4B2IdleTimeline {
    # Bezczynna VM: próbka procesów z listy co SampleSeconds przez Seconds.
    [CmdletBinding()]
    param(
        [Parameter(Mandatory)][string[]]$Names,
        [Parameter(Mandatory)][int]$Seconds,
        [Parameter(Mandatory)][int]$SampleSeconds
    )
    $Watch = [System.Diagnostics.Stopwatch]::StartNew()
    $Samples = New-Object System.Collections.Generic.List[object]
    do {
        $Samples.Add([pscustomobject]@{ T = [int]$Watch.Elapsed.TotalSeconds; Present = @(Get-E4B2ProcessSample -Names $Names) })
        Start-Sleep -Seconds $SampleSeconds
    } while ($Watch.Elapsed.TotalSeconds -lt $Seconds)
    $Services = @(Get-CimInstance -ClassName Win32_Service -Filter "Name='TrustedInstaller'" -ErrorAction SilentlyContinue | ForEach-Object { "$($_.Name)=$($_.State)/$($_.StartMode)" })
    return [pscustomobject]@{ Seconds = $Seconds; Samples = $Samples.ToArray(); Presence = (Get-E4B2PresenceSummary -Samples $Samples.ToArray() -WatchNames $Names); TrustedInstallerService = $Services }
}

function Get-E4B2FileLength {
    [CmdletBinding()]
    param([Parameter(Mandatory)][string]$Path)
    $Info = New-Object System.IO.FileInfo($Path)
    if (-not $Info.Exists) { return $null }
    return [long]$Info.Length
}

function Read-E4B2FileFrom {
    # Bajty pliku od offsetu (współdzielony odczyt: plik zostaje otwarty do zapisu przez TrustedInstaller/DISM).
    # Plik krótszy niż offset = rotacja (CBS.log -> CbsPersist): czytamy od początku. Najwyżej MaxBytes od końca.
    [CmdletBinding()]
    param(
        [Parameter(Mandatory)][string]$Path,
        [Parameter(Mandatory)][AllowNull()]$Offset,
        [long]$MaxBytes = 33554432
    )
    $Out = [ordered]@{ Bytes = [byte[]]@(); Rotated = $false; Truncated = $false; Start = 0; LengthNow = $null; Missing = $false }
    if (-not [System.IO.File]::Exists($Path)) { $Out.Missing = $true; return [pscustomobject]$Out }
    $Stream = New-Object System.IO.FileStream($Path, [System.IO.FileMode]::Open, [System.IO.FileAccess]::Read, ([System.IO.FileShare]::ReadWrite -bor [System.IO.FileShare]::Delete))
    try {
        $Length = $Stream.Length
        $Out.LengthNow = $Length
        $Start = if ($null -eq $Offset) { 0L } else { [long]$Offset }
        if ($Start -gt $Length) { $Out.Rotated = $true; $Start = 0L }
        if (($Length - $Start) -gt $MaxBytes) { $Start = $Length - $MaxBytes; $Out.Truncated = $true }
        $Out.Start = $Start
        $Count = [int]($Length - $Start)
        $Buffer = New-Object byte[] $Count
        $null = $Stream.Seek($Start, [System.IO.SeekOrigin]::Begin)
        $Read = 0
        while ($Read -lt $Count) {
            $Chunk = $Stream.Read($Buffer, $Read, $Count - $Read)
            if ($Chunk -le 0) { break }
            $Read += $Chunk
        }
        if ($Read -lt $Count) { $Buffer = $Buffer[0..($Read - 1)] }
        $Out.Bytes = $Buffer
    }
    finally { $Stream.Dispose() }
    return [pscustomobject]$Out
}

function Get-E4B2FreeBytes {
    [CmdletBinding()]
    param([string]$Drive = 'C')
    return [long](New-Object System.IO.DriveInfo($Drive)).AvailableFreeSpace
}

function Get-E4B2FolderSize {
    # Rozmiar i liczba plików folderu bez wchodzenia w junction/dowiązania.
    [CmdletBinding()]
    param([Parameter(Mandatory)][string]$Path)
    $Out = [ordered]@{ Path = $Path; Exists = $false; Bytes = 0L; Files = 0; Links = 0; Errors = 0 }
    if (-not [System.IO.Directory]::Exists($Path)) { return [pscustomobject]$Out }
    $Out.Exists = $true
    $Stack = New-Object System.Collections.Generic.Stack[string]
    $Stack.Push($Path)
    while ($Stack.Count -gt 0) {
        $Directory = $Stack.Pop()
        try { $Entries = @((New-Object System.IO.DirectoryInfo($Directory)).GetFileSystemInfos()) }
        catch { $Out.Errors++; continue }
        foreach ($Entry in $Entries) {
            if (($Entry.Attributes -band [System.IO.FileAttributes]::ReparsePoint) -ne 0) { $Out.Links++; continue }
            if ($Entry -is [System.IO.DirectoryInfo]) { $Stack.Push($Entry.FullName) }
            else { $Out.Files++; $Out.Bytes += $Entry.Length }
        }
    }
    return [pscustomobject]$Out
}

function Get-E4B2PendingState {
    # Oczekujący restart: CBS RebootPending/PackagesPending, WU RebootRequired, PendingFileRenameOperations.
    [CmdletBinding()]
    param()
    $Cbs = 'HKLM\SOFTWARE\Microsoft\Windows\CurrentVersion\Component Based Servicing'
    $RebootPending = Open-ExploreRegistryKey -Path "$Cbs\RebootPending"
    $PackagesPending = Open-ExploreRegistryKey -Path "$Cbs\PackagesPending"
    $WuReboot = Open-ExploreRegistryKey -Path 'HKLM\SOFTWARE\Microsoft\Windows\CurrentVersion\WindowsUpdate\Auto Update\RebootRequired'
    $Rename = Read-ExploreRegistryValue -Path 'HKLM\SYSTEM\CurrentControlSet\Control\Session Manager' -Name 'PendingFileRenameOperations'
    $Out = [pscustomobject]@{
        RebootPending       = ($null -ne $RebootPending)
        PackagesPending     = ($null -ne $PackagesPending)
        WuRebootRequired    = ($null -ne $WuReboot)
        PendingFileRenames  = if ($Rename.Exists) { @($Rename.Value).Count } else { 0 }
    }
    foreach ($Key in $RebootPending, $PackagesPending, $WuReboot) { if ($null -ne $Key) { $Key.Dispose() } }
    return $Out
}

function Test-E4B2HandlerKey {
    [CmdletBinding()]
    [OutputType([bool])]
    param(
        [Parameter(Mandatory)][string]$VolumeCachesKey,
        [Parameter(Mandatory)][string]$Handler
    )
    $Key = Open-ExploreRegistryKey -Path "$VolumeCachesKey\$Handler"
    if ($null -eq $Key) { return $false }
    $Key.Dispose()
    return $true
}

function Get-E4B2SageFlags {
    # Programy obsługi VolumeCaches, które mają już wartość StateFlags<Id> (cudze ustawienia cleanmgr /sageset).
    [CmdletBinding()]
    param(
        [Parameter(Mandatory)][string]$VolumeCachesKey,
        [Parameter(Mandatory)][int]$Id
    )
    $Name = Get-E4B2StateFlagsName -Id $Id
    $Root = Read-ExploreRegistryKey -Path $VolumeCachesKey
    foreach ($Handler in @($Root.SubKeys)) {
        $Value = Read-ExploreRegistryValue -Path "$VolumeCachesKey\$Handler" -Name $Name
        if ($Value.Exists) { [pscustomobject]@{ Handler = $Handler; Name = $Name; Value = $Value.Value } }
    }
}

function Get-E4B2SageInventory {
    # Wszystkie programy obsługi VolumeCaches i wszystkie nazwy StateFlags* w użyciu (stan świeżej VM).
    [CmdletBinding()]
    param([Parameter(Mandatory)][string]$VolumeCachesKey)
    $Root = Read-ExploreRegistryKey -Path $VolumeCachesKey
    $Flags = @()
    foreach ($Handler in @($Root.SubKeys)) {
        $Key = Read-ExploreRegistryKey -Path "$VolumeCachesKey\$Handler"
        foreach ($Value in @($Key.Values | Where-Object { $_.Name -like 'StateFlags*' })) { $Flags += "$Handler\$($Value.Name)=$($Value.Value)" }
    }
    return [pscustomobject]@{ Handlers = @($Root.SubKeys); StateFlags = $Flags }
}

function Get-E4B2ServiceState {
    [CmdletBinding()]
    param([Parameter(Mandatory)][string[]]$Names)
    foreach ($Name in $Names) {
        $Service = Get-CimInstance -ClassName Win32_Service -Filter "Name='$Name'" -ErrorAction SilentlyContinue
        if ($null -eq $Service) { [pscustomobject]@{ Name = $Name; Exists = $false }; continue }
        [pscustomobject]@{ Name = $Name; Exists = $true; State = [string]$Service.State; StartMode = [string]$Service.StartMode; ProcessId = [int]$Service.ProcessId }
    }
}

function Test-E4B2FileLocked {
    # Czy plik jest otwarty przez kogoś innego (otwarcie do odczytu bez współdzielenia się nie udaje). Bez zmiany pliku.
    [CmdletBinding()]
    [OutputType([bool])]
    param([Parameter(Mandatory)][string]$Path)
    try { [System.IO.File]::Open($Path, [System.IO.FileMode]::Open, [System.IO.FileAccess]::Read, [System.IO.FileShare]::None).Dispose(); return $false }
    catch { return $true }
}

function Get-E4B2LockedCount {
    [CmdletBinding()]
    param(
        [Parameter(Mandatory)][string]$Root,
        [int]$Limit = 3000
    )
    if (-not [System.IO.Directory]::Exists($Root)) { return [pscustomobject]@{ Checked = 0; Locked = 0; Examples = @() } }
    $Files = @([System.IO.Directory]::EnumerateFiles($Root, '*', [System.IO.SearchOption]::AllDirectories) | Select-Object -First $Limit)
    $Locked = @($Files | Where-Object { Test-E4B2FileLocked -Path $_ })
    return [pscustomobject]@{ Checked = $Files.Count; Locked = $Locked.Count; Examples = @($Locked | Select-Object -First 10) }
}

function Get-E4B2TempSetState {
    [CmdletBinding()]
    param([Parameter(Mandatory)]$Set)
    foreach ($Item in @($Set.Items)) {
        [pscustomobject]@{ Kind = $Item.Kind; Path = $Item.Path; Exists = ([System.IO.File]::Exists($Item.Path) -or [System.IO.Directory]::Exists($Item.Path)) }
    }
}

function Get-E4B2UserProfilePath {
    [CmdletBinding()]
    param([Parameter(Mandatory)][string]$UserSid)
    $Value = Read-ExploreRegistryValue -Path "HKLM\SOFTWARE\Microsoft\Windows NT\CurrentVersion\ProfileList\$UserSid" -Name 'ProfileImagePath'
    if (-not $Value.Exists) { return $null }
    return [System.Environment]::ExpandEnvironmentVariables([string]$Value.Value)
}

function Wait-E4B2ToolExit {
    [CmdletBinding()]
    [OutputType([bool])]
    param(
        [Parameter(Mandatory)]$Handle,
        [Parameter(Mandatory)][int]$Seconds
    )
    return $Handle.Process.WaitForExit($Seconds * 1000)
}

# --- skutki (tylko w VM) ---

function Save-E4B2Raw {
    [CmdletBinding()]
    param(
        [Parameter(Mandatory)][ValidatePattern('^[A-Za-z0-9._-]+\z')][string]$Name,
        [Parameter(Mandatory)][AllowEmptyCollection()][byte[]]$Bytes,
        [string]$Directory = 'C:\dso\e4b2\raw'
    )
    Assert-E4B2InsideVm
    $null = [System.IO.Directory]::CreateDirectory($Directory)
    [System.IO.File]::WriteAllBytes((Join-Path $Directory $Name), $Bytes)
    return $Name
}

function Start-E4B2Tool {
    # Start narzędzia z System32 z przekierowaniem stdout/stderr BAJTOWO do plików (BaseStream.CopyToAsync, bez dekodowania).
    # FileStream z buforem 1 B: długość pliku w trakcie = to, co narzędzie już wypisało (czy postęp płynie na bieżąco).
    [CmdletBinding()]
    param(
        [Parameter(Mandatory)][string]$Tool,
        [Parameter(Mandatory)][AllowEmptyString()][string]$Arguments,
        [Parameter(Mandatory)][string]$OutFile,
        [Parameter(Mandatory)][string]$ErrFile
    )
    Assert-E4B2InsideVm
    $Path = Get-E4B2ToolPath -Tool $Tool
    $null = [System.IO.Directory]::CreateDirectory((Split-Path -Parent $OutFile))
    $Psi = New-Object System.Diagnostics.ProcessStartInfo
    $Psi.FileName = $Path
    $Psi.Arguments = $Arguments
    $Psi.WorkingDirectory = Join-Path $env:SystemRoot 'System32'
    $Psi.UseShellExecute = $false
    $Psi.RedirectStandardOutput = $true
    $Psi.RedirectStandardError = $true
    $Psi.CreateNoWindow = $true
    $OutStream = New-Object System.IO.FileStream($OutFile, [System.IO.FileMode]::Create, [System.IO.FileAccess]::Write, [System.IO.FileShare]::Read, 1)
    $ErrStream = New-Object System.IO.FileStream($ErrFile, [System.IO.FileMode]::Create, [System.IO.FileAccess]::Write, [System.IO.FileShare]::Read, 1)
    $Watch = [System.Diagnostics.Stopwatch]::StartNew()
    $Process = [System.Diagnostics.Process]::Start($Psi)
    $OutCopy = $Process.StandardOutput.BaseStream.CopyToAsync($OutStream, 4096)
    $ErrCopy = $Process.StandardError.BaseStream.CopyToAsync($ErrStream, 4096)
    return [pscustomobject]@{
        Tool = $Tool; Arguments = $Arguments; Command = "$Path $Arguments"; Process = $Process; ProcessId = $Process.Id; Watch = $Watch
        OutFile = $OutFile; ErrFile = $ErrFile; OutStream = $OutStream; ErrStream = $ErrStream; OutCopy = $OutCopy; ErrCopy = $ErrCopy; StartedAt = (Get-Date).ToString('o')
    }
}

function Complete-E4B2Tool {
    # Koniec przebiegu narzędzia: przy -Kill (limit pomiaru minął) zabicie procesu W VM; kod wyjścia, czas, bajty wyjścia.
    [CmdletBinding()]
    param(
        [Parameter(Mandatory)]$Handle,
        [switch]$Kill
    )
    Assert-E4B2InsideVm
    $Killed = $false
    if ($Kill -and -not $Handle.Process.HasExited) {
        try { $Handle.Process.Kill(); $Killed = $true } catch { $null = $_ }
    }
    $Handle.Process.WaitForExit()
    $Duration = $Handle.Watch.Elapsed.TotalSeconds
    try { $null = $Handle.OutCopy.Wait(15000) } catch { $null = $_ }
    try { $null = $Handle.ErrCopy.Wait(15000) } catch { $null = $_ }
    $Handle.OutStream.Dispose()
    $Handle.ErrStream.Dispose()
    $ExitCode = $Handle.Process.ExitCode
    $Handle.Process.Dispose()
    return [pscustomobject]@{
        ExitCode = $ExitCode; TimedOut = [bool]$Kill; Killed = $Killed; DurationSeconds = [math]::Round($Duration, 1)
        OutBytes = [System.IO.File]::ReadAllBytes($Handle.OutFile); ErrBytes = [System.IO.File]::ReadAllBytes($Handle.ErrFile)
    }
}

function Invoke-E4B2Tool {
    # Jeden przebieg narzędzia: offsety CBS.log/dism.log -> start -> próbki procesów i długości wyjścia co SampleSeconds
    # (opcjonalnie drugie narzędzie w trakcie) -> koniec (limit pomiaru = zabicie w VM) -> segmenty logów -> analiza bajtów.
    [CmdletBinding()]
    param(
        [Parameter(Mandatory)][ValidatePattern('^[A-Za-z0-9-]+\z')][string]$Name,
        [Parameter(Mandatory)][string]$Tool,
        [Parameter(Mandatory)][AllowEmptyString()][string]$Arguments,
        [Parameter(Mandatory)][int]$TimeoutSeconds,
        [Parameter(Mandatory)][string[]]$WatchNames,
        [int]$SampleSeconds = 5,
        [object[]]$Concurrent = @(),
        [switch]$Logs,
        [string]$RawDirectory = 'C:\dso\e4b2\raw',
        # Ścieżka, której istnienie trafia do każdej próbki (np. C:\Windows.old: kiedy zniknęła względem końca narzędzia).
        [string]$WatchPath
    )
    Assert-E4B2InsideVm
    $CbsPath = Join-Path $env:SystemRoot 'Logs\CBS\CBS.log'
    $DismPath = Join-Path $env:SystemRoot 'Logs\DISM\dism.log'
    $OemCodePage = [Globalization.CultureInfo]::CurrentCulture.TextInfo.OEMCodePage
    $CbsBefore = $null
    $DismBefore = $null
    if ($Logs) { $CbsBefore = Get-E4B2FileLength -Path $CbsPath; $DismBefore = Get-E4B2FileLength -Path $DismPath }
    $FreeBefore = Get-E4B2FreeBytes
    $PendingBefore = Get-E4B2PendingState
    $Handle = Start-E4B2Tool -Tool $Tool -Arguments $Arguments -OutFile (Join-Path $RawDirectory "$Name.out.bin") -ErrFile (Join-Path $RawDirectory "$Name.err.bin")
    $Samples = New-Object System.Collections.Generic.List[object]
    $Pending = @($Concurrent)
    $Running = New-Object System.Collections.Generic.List[object]
    $TimedOut = $false
    while (-not (Wait-E4B2ToolExit -Handle $Handle -Seconds $SampleSeconds)) {
        $Elapsed = [int]$Handle.Watch.Elapsed.TotalSeconds
        $Samples.Add([pscustomobject]@{
                T = $Elapsed; OutLength = (Get-E4B2FileLength -Path $Handle.OutFile); Present = @(Get-E4B2ProcessSample -Names $WatchNames); FreeBytes = (Get-E4B2FreeBytes)
                Children = @(Get-E4B2ChildProcesses -ParentId ([int]$Handle.ProcessId)); PathExists = $(if ($WatchPath) { [bool](Test-Path -LiteralPath $WatchPath) } else { $null })
            })
        foreach ($Spec in @($Pending)) {
            if ($Elapsed -ge [int]$Spec.DelaySeconds) {
                $Second = Start-E4B2Tool -Tool ([string]$Spec.Tool) -Arguments ([string]$Spec.Arguments) -OutFile (Join-Path $RawDirectory "$($Spec.Name).out.bin") -ErrFile (Join-Path $RawDirectory "$($Spec.Name).err.bin")
                $Running.Add([pscustomobject]@{ Spec = $Spec; Handle = $Second; StartedAfterSeconds = $Elapsed })
                $Pending = @($Pending | Where-Object { $_ -ne $Spec })
            }
        }
        if ($Elapsed -ge $TimeoutSeconds) { $TimedOut = $true; break }
    }
    $Main = Complete-E4B2Tool -Handle $Handle -Kill:$TimedOut
    $MainEnd = [int]$Handle.Watch.Elapsed.TotalSeconds
    $ConcurrentResults = @()
    foreach ($Entry in $Running) {
        $Limit = [int]$Entry.Spec.TimeoutSeconds
        $SecondTimedOut = $false
        while (-not (Wait-E4B2ToolExit -Handle $Entry.Handle -Seconds $SampleSeconds)) {
            if ($Entry.Handle.Watch.Elapsed.TotalSeconds -ge $Limit) { $SecondTimedOut = $true; break }
        }
        $Done = Complete-E4B2Tool -Handle $Entry.Handle -Kill:$SecondTimedOut
        $null = Save-E4B2Raw -Name "$($Entry.Spec.Name).out.bin" -Bytes $Done.OutBytes -Directory $RawDirectory
        $ConcurrentResults += [pscustomobject]@{
            Name = $Entry.Spec.Name; Command = "$(Get-E4B2ToolPath -Tool ([string]$Entry.Spec.Tool)) $($Entry.Spec.Arguments)"; StartedAfterSeconds = $Entry.StartedAfterSeconds; MainEndedAtSeconds = $MainEnd
            ExitCode = $Done.ExitCode; ExitCodeHex = ('0x{0:X8}' -f [int]$Done.ExitCode); TimedOut = $Done.TimedOut; Killed = $Done.Killed; DurationSeconds = $Done.DurationSeconds
            Output = (Get-E4B2OutputAnalysis -Bytes $Done.OutBytes -OemCodePage $OemCodePage); Stderr = (Get-E4B2OutputAnalysis -Bytes $Done.ErrBytes -OemCodePage $OemCodePage)
        }
    }
    $null = Save-E4B2Raw -Name "$Name.out.bin" -Bytes $Main.OutBytes -Directory $RawDirectory
    $Cbs = $null
    $Dism = $null
    if ($Logs) {
        $CbsRead = Read-E4B2FileFrom -Path $CbsPath -Offset $CbsBefore
        $CbsText = [System.Text.Encoding]::UTF8.GetString([byte[]]$CbsRead.Bytes)
        $null = Save-E4B2Raw -Name "$Name.cbs.log" -Bytes ([byte[]]$CbsRead.Bytes) -Directory $RawDirectory
        $Cbs = [pscustomobject]@{
            Offset = $CbsBefore; LengthNow = $CbsRead.LengthNow; Bytes = @($CbsRead.Bytes).Count; Rotated = $CbsRead.Rotated; Truncated = $CbsRead.Truncated; File = "$Name.cbs.log"
            Sr = (Get-E4B2SrSummary -Text $CbsText)
            KeyLines = @(Get-E4B2KeyLines -Text $CbsText -Pattern 'Corrupt|Summary|Operation result|Repair|reboot|Reboot|Exit|Error|0x8|Ending|Starting|Checking System Update Readiness|Total' -Head 120 -Tail 120)
        }
        $DismRead = Read-E4B2FileFrom -Path $DismPath -Offset $DismBefore
        $DismText = [System.Text.Encoding]::UTF8.GetString([byte[]]$DismRead.Bytes)
        $null = Save-E4B2Raw -Name "$Name.dism.log" -Bytes ([byte[]]$DismRead.Bytes) -Directory $RawDirectory
        $Dism = [pscustomobject]@{
            Offset = $DismBefore; LengthNow = $DismRead.LengthNow; Bytes = @($DismRead.Bytes).Count; Rotated = $DismRead.Rotated; Truncated = $DismRead.Truncated; File = "$Name.dism.log"
            KeyLines = @(Get-E4B2KeyLines -Text $DismText -Pattern 'Error|Warning|0x8|Exit|exit|Corrupt|corrupt|Repair|repair|Source|source|Health|Windows Update|WU|reboot|Reboot|Ending|Starting' -Head 120 -Tail 120)
        }
    }
    return [pscustomobject]@{
        Name = $Name; Command = "$(Get-E4B2ToolPath -Tool $Tool) $Arguments"; Arguments = $Arguments; StartedAt = $Handle.StartedAt; WatchPath = $WatchPath
        ExitCode = $Main.ExitCode; ExitCodeHex = ('0x{0:X8}' -f [int]$Main.ExitCode); TimedOut = $Main.TimedOut; Killed = $Main.Killed; DurationSeconds = $Main.DurationSeconds
        TimeoutSeconds = $TimeoutSeconds; FreeBefore = $FreeBefore; FreeAfter = (Get-E4B2FreeBytes); PendingBefore = $PendingBefore; PendingAfter = (Get-E4B2PendingState)
        OutFile = "$Name.out.bin"; ErrFile = "$Name.err.bin"; OemCodePage = $OemCodePage
        Output = (Get-E4B2OutputAnalysis -Bytes $Main.OutBytes -OemCodePage $OemCodePage); Stderr = (Get-E4B2OutputAnalysis -Bytes $Main.ErrBytes -OemCodePage $OemCodePage)
        Samples = $Samples.ToArray(); Presence = (Get-E4B2PresenceSummary -Samples $Samples.ToArray() -WatchNames $WatchNames)
        Concurrent = $ConcurrentResults; Cbs = $Cbs; Dism = $Dism
    }
}

function Set-E4B2SageFlag {
    # StateFlags<Id> = 2 (zaznaczony) pod jednym programem obsługi VolumeCaches (rejestr 64-bit przez API .NET).
    [CmdletBinding()]
    param(
        [Parameter(Mandatory)][string]$VolumeCachesKey,
        [Parameter(Mandatory)][string]$Handler,
        [Parameter(Mandatory)][int]$Id
    )
    Assert-E4B2InsideVm
    $Sub = ($VolumeCachesKey -replace '^HKLM\\', '') + "\$Handler"
    $Base = [Microsoft.Win32.RegistryKey]::OpenBaseKey([Microsoft.Win32.RegistryHive]::LocalMachine, [Microsoft.Win32.RegistryView]::Registry64)
    try {
        $Key = $Base.OpenSubKey($Sub, $true)
        if ($null -eq $Key) { throw "Brak klucza HKLM\$Sub." }
        try { $Key.SetValue((Get-E4B2StateFlagsName -Id $Id), 2, [Microsoft.Win32.RegistryValueKind]::DWord) } finally { $Key.Dispose() }
    }
    finally { $Base.Dispose() }
}

function Remove-E4B2SageFlag {
    [CmdletBinding()]
    param(
        [Parameter(Mandatory)][string]$VolumeCachesKey,
        [Parameter(Mandatory)][string]$Handler,
        [Parameter(Mandatory)][int]$Id
    )
    Assert-E4B2InsideVm
    $Sub = ($VolumeCachesKey -replace '^HKLM\\', '') + "\$Handler"
    $Base = [Microsoft.Win32.RegistryKey]::OpenBaseKey([Microsoft.Win32.RegistryHive]::LocalMachine, [Microsoft.Win32.RegistryView]::Registry64)
    try {
        $Key = $Base.OpenSubKey($Sub, $true)
        if ($null -ne $Key) { try { $Key.DeleteValue((Get-E4B2StateFlagsName -Id $Id), $false) } finally { $Key.Dispose() } }
    }
    finally { $Base.Dispose() }
}

function Invoke-E4B2CleanmgrRun {
    # cleanmgr /sagerun:<Id> z flagą tylko pod jednym programem obsługi. Cudza StateFlags<Id> gdziekolwiek = nic nie uruchamiamy.
    # Własna flaga zawsze usuwana w finally (także po błędzie cleanmgr).
    [CmdletBinding()]
    param(
        [Parameter(Mandatory)][string]$Name,
        [Parameter(Mandatory)][string]$Handler,
        [Parameter(Mandatory)][int]$Id,
        [Parameter(Mandatory)][string]$VolumeCachesKey,
        [Parameter(Mandatory)][int]$TimeoutSeconds,
        [Parameter(Mandatory)][string[]]$WatchNames,
        [int]$SampleSeconds = 5,
        [string]$WatchPath
    )
    Assert-E4B2InsideVm
    $Out = [ordered]@{ Name = $Name; Handler = $Handler; Context = 'PowerShell Direct (Administrator, sesja 0)'; Refused = $null; Error = $null; Run = $null; FlagsAfter = @() }
    if (-not (Test-E4B2HandlerKey -VolumeCachesKey $VolumeCachesKey -Handler $Handler)) { $Out.Refused = "brak klucza programu obsługi '$Handler'"; return [pscustomobject]$Out }
    $Foreign = @(Get-E4B2SageFlags -VolumeCachesKey $VolumeCachesKey -Id $Id)
    if ($Foreign.Count -gt 0) { $Out.Refused = "cudze $(Get-E4B2StateFlagsName -Id $Id) pod: $(@($Foreign | ForEach-Object { $_.Handler }) -join ', ')"; return [pscustomobject]$Out }
    try {
        Set-E4B2SageFlag -VolumeCachesKey $VolumeCachesKey -Handler $Handler -Id $Id
        $Out.Run = Invoke-E4B2Tool -Name $Name -Tool 'cleanmgr.exe' -Arguments "/sagerun:$Id" -TimeoutSeconds $TimeoutSeconds -WatchNames $WatchNames -SampleSeconds $SampleSeconds -WatchPath $WatchPath
    }
    catch { $Out.Error = Format-E4B2Error $_ }
    finally {
        Remove-E4B2SageFlag -VolumeCachesKey $VolumeCachesKey -Handler $Handler -Id $Id
        $Out.FlagsAfter = @(Get-E4B2SageFlags -VolumeCachesKey $VolumeCachesKey -Id $Id)
    }
    return [pscustomobject]$Out
}

function Stop-E4B2ProcessById {
    # Zabicie procesu w VM (tylko gdy limit pomiaru minął: cleanmgr w sesji dso).
    [CmdletBinding()]
    param([Parameter(Mandatory)][int]$ProcessId)
    Assert-E4B2InsideVm
    try { [System.Diagnostics.Process]::GetProcessById($ProcessId).Kill(); return $true } catch { return $false }
}

function Invoke-E4B2CleanmgrUserRun {
    # cleanmgr /sagerun:<Id> w sesji konta dso (zadanie Harmonogramu Interactive + Highest, czyli jak DSOptimizer po UAC),
    # z flagą tylko pod jednym programem obsługi. Próbki: stan zadania, procesy z listy, istnienie WatchPath, wolne miejsce.
    # Limit pomiaru = zabicie cleanmgr w VM. Zadanie i flaga zawsze usuwane w finally.
    [CmdletBinding()]
    param(
        [Parameter(Mandatory)][ValidatePattern('^[a-z0-9-]+\z')][string]$Name,
        [Parameter(Mandatory)][string]$Handler,
        [Parameter(Mandatory)][int]$Id,
        [Parameter(Mandatory)][string]$VolumeCachesKey,
        [Parameter(Mandatory)][int]$TimeoutSeconds,
        [Parameter(Mandatory)][string[]]$WatchNames,
        [int]$SampleSeconds = 5,
        [string]$WatchPath,
        [ValidatePattern('^[A-Za-z0-9_-]+\z')][string]$UserName = 'dso'
    )
    Assert-E4B2InsideVm
    $TaskName = "DSO-E4B2-$Name"
    $Out = [ordered]@{ Name = $Name; Handler = $Handler; Context = "zadanie Harmonogramu $UserName (Interactive, Highest)"; TaskName = $TaskName; Refused = $null; Error = $null
        FreeBefore = $null; FreeAfter = $null; DurationSeconds = $null; TimedOut = $false; Killed = @(); CleanmgrSessions = @(); LastTaskResult = $null; Samples = @(); Presence = $null
        UnregisterError = $null; FlagsAfter = @() }
    if (-not (Test-E4B2HandlerKey -VolumeCachesKey $VolumeCachesKey -Handler $Handler)) { $Out.Refused = "brak klucza programu obsługi '$Handler'"; return [pscustomobject]$Out }
    $Foreign = @(Get-E4B2SageFlags -VolumeCachesKey $VolumeCachesKey -Id $Id)
    if ($Foreign.Count -gt 0) { $Out.Refused = "cudze $(Get-E4B2StateFlagsName -Id $Id) pod: $(@($Foreign | ForEach-Object { $_.Handler }) -join ', ')"; return [pscustomobject]$Out }
    $Registered = $false
    $Samples = New-Object System.Collections.Generic.List[object]
    try {
        Set-E4B2SageFlag -VolumeCachesKey $VolumeCachesKey -Handler $Handler -Id $Id
        $Action = New-ScheduledTaskAction -Execute (Get-E4B2ToolPath -Tool 'cleanmgr.exe') -Argument "/sagerun:$Id"
        $Principal = New-ScheduledTaskPrincipal -UserId $UserName -LogonType Interactive -RunLevel Highest
        $Settings = New-ScheduledTaskSettingsSet -AllowStartIfOnBatteries -DontStopIfGoingOnBatteries -ExecutionTimeLimit (New-TimeSpan -Seconds ($TimeoutSeconds + 120))
        $Registered = $true
        $null = Register-ScheduledTask -TaskName $TaskName -Action $Action -Principal $Principal -Settings $Settings -Force -ErrorAction Stop
        $Out.FreeBefore = Get-E4B2FreeBytes
        $Watch = [System.Diagnostics.Stopwatch]::StartNew()
        Start-ScheduledTask -TaskName $TaskName -ErrorAction Stop
        $Sessions = @()
        while ($true) {
            Start-Sleep -Seconds $SampleSeconds
            $Elapsed = [int]$Watch.Elapsed.TotalSeconds
            $State = [string](Get-ScheduledTask -TaskName $TaskName -ErrorAction Stop).State
            $Cleanmgr = @(Get-Process -Name 'cleanmgr' -ErrorAction SilentlyContinue)
            foreach ($Process in $Cleanmgr) { $Sessions += "$($Process.Id):$($Process.SessionId)" }
            $Samples.Add([pscustomobject]@{ T = $Elapsed; TaskState = $State; Present = @(Get-E4B2ProcessSample -Names $WatchNames); FreeBytes = (Get-E4B2FreeBytes)
                    PathExists = $(if ($WatchPath) { [bool](Test-Path -LiteralPath $WatchPath) } else { $null }) })
            if ($State -ne 'Running' -and $Cleanmgr.Count -eq 0 -and $Elapsed -ge 10) { break }
            if ($Elapsed -ge $TimeoutSeconds) {
                $Out.TimedOut = $true
                foreach ($Process in $Cleanmgr) { if (Stop-E4B2ProcessById -ProcessId $Process.Id) { $Out.Killed += "cleanmgr:$($Process.Id)" } }
                break
            }
        }
        $Out.DurationSeconds = [math]::Round($Watch.Elapsed.TotalSeconds, 1)
        $Out.CleanmgrSessions = @($Sessions | Select-Object -Unique)
        $Info = Get-ScheduledTaskInfo -TaskName $TaskName -ErrorAction SilentlyContinue
        if ($null -ne $Info) { $Out.LastTaskResult = ('0x{0:X8}' -f [int]$Info.LastTaskResult) }
    }
    catch { $Out.Error = Format-E4B2Error $_ }
    finally {
        if ($Registered) {
            try { Unregister-ScheduledTask -TaskName $TaskName -Confirm:$false -ErrorAction Stop } catch { $Out.UnregisterError = Format-E4B2Error $_ }
        }
        Remove-E4B2SageFlag -VolumeCachesKey $VolumeCachesKey -Handler $Handler -Id $Id
        $Out.FlagsAfter = @(Get-E4B2SageFlags -VolumeCachesKey $VolumeCachesKey -Id $Id)
        $Out.FreeAfter = Get-E4B2FreeBytes
        $Out.Samples = $Samples.ToArray()
        $Out.Presence = Get-E4B2PresenceSummary -Samples $Samples.ToArray() -WatchNames $WatchNames
    }
    return [pscustomobject]$Out
}

function Remove-E4B2OldFiles {
    # Model algorytmu DeleteOldFiles z planu (Task 4): pliki starsze niż próg (LastWriteTimeUtc) usuwane, młodsze i w użyciu zostają
    # i są liczone, junction/dowiązanie usuwane jako link (bez wchodzenia), puste foldery starsze niż próg usuwane, sam Root zostaje.
    [CmdletBinding()]
    param(
        [Parameter(Mandatory)][string]$Root,
        [Parameter(Mandatory)][datetime]$CutoffUtc
    )
    Assert-E4B2InsideVm
    $Out = [ordered]@{ Root = $Root; RootExists = $false; Deleted = 0; SkippedYoung = 0; SkippedLocked = 0; SkippedDenied = 0; LinksRemoved = 0; DirsDeleted = 0; FreedBytes = 0L; Errors = @() }
    if (-not [System.IO.Directory]::Exists($Root)) { return [pscustomobject]$Out }
    $Out.RootExists = $true
    $Dirs = New-Object System.Collections.Generic.List[object]
    $Stack = New-Object System.Collections.Generic.Stack[string]
    $Stack.Push($Root)
    while ($Stack.Count -gt 0) {
        $Directory = $Stack.Pop()
        try { $Entries = @((New-Object System.IO.DirectoryInfo($Directory)).GetFileSystemInfos()) }
        catch { $Out.Errors += "$Directory -> $($_.Exception.Message)"; continue }
        foreach ($Entry in $Entries) {
            if (($Entry.Attributes -band [System.IO.FileAttributes]::ReparsePoint) -ne 0) {
                try {
                    if ($Entry -is [System.IO.DirectoryInfo]) { [System.IO.Directory]::Delete($Entry.FullName, $false) } else { [System.IO.File]::Delete($Entry.FullName) }
                    $Out.LinksRemoved++
                }
                catch { $Out.Errors += "$($Entry.FullName) -> $($_.Exception.Message)" }
                continue
            }
            if ($Entry -is [System.IO.DirectoryInfo]) {
                # Czas folderu przed usuwaniem jego plików (usunięcie pliku odświeża LastWriteTime folderu).
                $Dirs.Add([pscustomobject]@{ Path = $Entry.FullName; Old = ($Entry.LastWriteTimeUtc -lt $CutoffUtc) })
                $Stack.Push($Entry.FullName)
                continue
            }
            if ($Entry.LastWriteTimeUtc -ge $CutoffUtc) { $Out.SkippedYoung++; continue }
            $Size = $Entry.Length
            try { [System.IO.File]::Delete($Entry.FullName); $Out.Deleted++; $Out.FreedBytes += $Size }
            catch [System.UnauthorizedAccessException] { $Out.SkippedDenied++ }
            catch [System.IO.IOException] { $Out.SkippedLocked++ }
            catch { $Out.Errors += "$($Entry.FullName) -> $($_.Exception.Message)" }
        }
    }
    for ($Index = $Dirs.Count - 1; $Index -ge 0; $Index--) {
        $Dir = $Dirs[$Index]
        if (-not $Dir.Old) { continue }
        try {
            if (@([System.IO.Directory]::EnumerateFileSystemEntries($Dir.Path)).Count -eq 0) { [System.IO.Directory]::Delete($Dir.Path, $false); $Out.DirsDeleted++ }
        }
        catch { $Out.Errors += "$($Dir.Path) -> $($_.Exception.Message)" }
    }
    $Out.Errors = @($Out.Errors | Select-Object -First 20)
    return [pscustomobject]$Out
}

function New-E4B2TempSet {
    # Zestaw w katalogu tymczasowym: stary (−OldAgeDays), bardzo stary (−VeryOldAgeDays), świeży, stary otwarty bez współdzielenia,
    # stary plik w podfolderze, junction do folderu-strażnika z plikiem (stary). Uchwyt blokady zwalnia Close-E4B2TempSet.
    [CmdletBinding()]
    param(
        [Parameter(Mandatory)][string]$Root,
        [Parameter(Mandatory)][ValidatePattern('^[a-z0-9]+\z')][string]$Label,
        [Parameter(Mandatory)][string]$GuardRoot,
        [Parameter(Mandatory)][int]$OldAgeDays,
        [Parameter(Mandatory)][int]$VeryOldAgeDays,
        [int]$FileBytes = 1048576
    )
    Assert-E4B2InsideVm
    $Prefix = "dso-e4b2-$Label"
    $Now = (Get-Date).ToUniversalTime()
    $Old = $Now.AddDays(-$OldAgeDays)
    $VeryOld = $Now.AddDays(-$VeryOldAgeDays)
    $Data = New-Object byte[] $FileBytes
    (New-Object System.Random 42).NextBytes($Data)
    $Items = @(
        [pscustomobject]@{ Kind = 'old'; Path = (Join-Path $Root "$Prefix-old.bin"); Time = $Old }
        [pscustomobject]@{ Kind = 'veryold'; Path = (Join-Path $Root "$Prefix-veryold.bin"); Time = $VeryOld }
        [pscustomobject]@{ Kind = 'fresh'; Path = (Join-Path $Root "$Prefix-fresh.bin"); Time = $null }
        [pscustomobject]@{ Kind = 'locked'; Path = (Join-Path $Root "$Prefix-locked.bin"); Time = $Old }
        [pscustomobject]@{ Kind = 'subold'; Path = (Join-Path $Root "$Prefix-dir\old.bin"); Time = $Old }
        [pscustomobject]@{ Kind = 'link'; Path = (Join-Path $Root "$Prefix-link"); Time = $null }
        [pscustomobject]@{ Kind = 'sentinel'; Path = (Join-Path $GuardRoot "$Label\sentinel.bin"); Time = $VeryOld }
    )
    foreach ($Item in $Items) {
        if ($Item.Kind -eq 'link') { continue }
        $null = [System.IO.Directory]::CreateDirectory((Split-Path -Parent $Item.Path))
        [System.IO.File]::WriteAllBytes($Item.Path, $Data)
        if ($null -ne $Item.Time) {
            [System.IO.File]::SetCreationTimeUtc($Item.Path, $Item.Time)
            [System.IO.File]::SetLastWriteTimeUtc($Item.Path, $Item.Time)
            [System.IO.File]::SetLastAccessTimeUtc($Item.Path, $Item.Time)
        }
    }
    $SubDir = Join-Path $Root "$Prefix-dir"
    [System.IO.Directory]::SetCreationTimeUtc($SubDir, $Old)
    [System.IO.Directory]::SetLastWriteTimeUtc($SubDir, $Old)
    [System.IO.Directory]::SetLastAccessTimeUtc($SubDir, $Old)
    $LinkItem = @($Items | Where-Object { $_.Kind -eq 'link' })[0]
    $null = Microsoft.PowerShell.Management\New-Item -ItemType Junction -Path $LinkItem.Path -Value (Join-Path $GuardRoot $Label)
    $LockItem = @($Items | Where-Object { $_.Kind -eq 'locked' })[0]
    $Lock = [System.IO.File]::Open($LockItem.Path, [System.IO.FileMode]::Open, [System.IO.FileAccess]::ReadWrite, [System.IO.FileShare]::None)
    return [pscustomobject]@{ Root = $Root; Label = $Label; Items = @($Items | ForEach-Object { [pscustomobject]@{ Kind = $_.Kind; Path = $_.Path } }); Lock = $Lock; FileBytes = $FileBytes }
}

function Close-E4B2TempSet {
    [CmdletBinding()]
    param([Parameter(Mandatory)]$Set)
    Assert-E4B2InsideVm
    if ($null -ne $Set.Lock) { $Set.Lock.Dispose() }
}

function New-E4B2WindowsOld {
    # Zwykły folder z plikami testowymi (układ jak w Windows.old: Windows\System32, Users, Program Files), tworzony bieżącym kontem.
    # Bez zmiany właściciela i ACL (R8). Istniejący folder = odmowa (nigdy nie dopisujemy do prawdziwego Windows.old).
    [CmdletBinding()]
    param(
        [Parameter(Mandatory)][string]$Path,
        [Parameter(Mandatory)][ValidateRange(1, 1024)][int]$Megabytes
    )
    Assert-E4B2InsideVm
    if ([System.IO.Directory]::Exists($Path) -or [System.IO.File]::Exists($Path)) { throw "'$Path' już istnieje – nic nie zostało utworzone." }
    $Layout = @('Windows\System32', 'Windows\SysWOW64', 'Users\Public\Documents', 'Program Files\DsoTest', 'ProgramData\DsoTest')
    $Chunk = New-Object byte[] 1048576
    (New-Object System.Random 7).NextBytes($Chunk)
    $Files = 0
    $Bytes = 0L
    foreach ($Index in 0..($Layout.Count - 1)) {
        $Folder = Join-Path $Path $Layout[$Index]
        $null = [System.IO.Directory]::CreateDirectory($Folder)
        $Share = [int][math]::Floor($Megabytes / $Layout.Count)
        if ($Index -eq $Layout.Count - 1) { $Share = $Megabytes - ($Share * ($Layout.Count - 1)) }
        foreach ($Part in 1..2) {
            $Size = if ($Part -eq 1) { [int][math]::Ceiling($Share / 2) } else { $Share - [int][math]::Ceiling($Share / 2) }
            $File = Join-Path $Folder ("dso-e4b2-{0}.bin" -f $Part)
            $Stream = New-Object System.IO.FileStream($File, [System.IO.FileMode]::CreateNew, [System.IO.FileAccess]::Write)
            try { for ($Mb = 0; $Mb -lt $Size; $Mb++) { $Stream.Write($Chunk, 0, $Chunk.Length) } } finally { $Stream.Dispose() }
            $Files++
            $Bytes += [long]$Size * 1048576
        }
        [System.IO.File]::WriteAllText((Join-Path $Folder 'dso-e4b2.txt'), 'plik testowy pomiaru E4B2')
        $Files++
        $Bytes += 25
    }
    return [pscustomobject]@{ Path = $Path; Bytes = $Bytes; Files = $Files; Owner = (Get-Acl -LiteralPath $Path).Owner }
}

function Stop-E4B2Service {
    [CmdletBinding()]
    param([Parameter(Mandatory)][string]$Name)
    Assert-E4B2InsideVm
    $Watch = [System.Diagnostics.Stopwatch]::StartNew()
    $Error1 = $null
    try { Stop-Service -Name $Name -Force -ErrorAction Stop } catch { $Error1 = Format-E4B2Error $_ }
    return [pscustomobject]@{ Name = $Name; Action = 'stop'; Error = $Error1; Seconds = [math]::Round($Watch.Elapsed.TotalSeconds, 1); After = @(Get-E4B2ServiceState -Names @($Name))[0] }
}

function Start-E4B2Service {
    [CmdletBinding()]
    param([Parameter(Mandatory)][string]$Name)
    Assert-E4B2InsideVm
    $Watch = [System.Diagnostics.Stopwatch]::StartNew()
    $Error1 = $null
    try { Start-Service -Name $Name -ErrorAction Stop } catch { $Error1 = Format-E4B2Error $_ }
    return [pscustomobject]@{ Name = $Name; Action = 'start'; Error = $Error1; Seconds = [math]::Round($Watch.Elapsed.TotalSeconds, 1); After = @(Get-E4B2ServiceState -Names @($Name))[0] }
}

function Remove-E4B2File {
    [CmdletBinding()]
    param([Parameter(Mandatory)][string]$Path)
    Assert-E4B2InsideVm
    $Size = (New-Object System.IO.FileInfo($Path)).Length
    try { [System.IO.File]::Delete($Path); return [pscustomobject]@{ Path = $Path; Bytes = $Size; Deleted = $true; Error = $null } }
    catch { return [pscustomobject]@{ Path = $Path; Bytes = $Size; Deleted = $false; Error = (Format-E4B2Error $_) } }
}

# --- sondy ---

function Invoke-E4B2DownloadProbe {
    # SoftwareDistribution\Download: rozmiar, pliki w użyciu przy działających usługach, po zatrzymaniu wuauserv, po zatrzymaniu bits,
    # usunięcie zawartości (model DeleteOldFiles z progiem „wszystko”), przywrócenie usług do stanu sprzed.
    [CmdletBinding()]
    param([int]$TestMegabytes = 0)
    Assert-E4B2InsideVm
    $Dir = Join-Path $env:SystemRoot 'SoftwareDistribution\Download'
    $Out = [ordered]@{ Path = $Dir; TestMegabytes = $TestMegabytes; Before = $null; ServicesBefore = @(); LockedRunning = $null; StopWuauserv = $null; LockedNoWuauserv = $null
        StopBits = $null; LockedNoBits = $null; Delete = $null; After = $null; Restore = @(); ServicesAfter = @(); FreeBefore = (Get-E4B2FreeBytes); FreeAfter = $null; Error = $null }
    try {
        if ($TestMegabytes -gt 0) {
            $TestDir = Join-Path $Dir 'dso-e4b2-test'
            $null = [System.IO.Directory]::CreateDirectory($TestDir)
            $Chunk = New-Object byte[] 1048576
            foreach ($Part in 1..3) {
                $Stream = New-Object System.IO.FileStream((Join-Path $TestDir "part$Part.bin"), [System.IO.FileMode]::Create, [System.IO.FileAccess]::Write)
                try { for ($Mb = 0; $Mb -lt [math]::Ceiling($TestMegabytes / 3); $Mb++) { $Stream.Write($Chunk, 0, $Chunk.Length) } } finally { $Stream.Dispose() }
            }
        }
        $Out.Before = Get-E4B2FolderSize -Path $Dir
        $Out.ServicesBefore = @(Get-E4B2ServiceState -Names @('wuauserv', 'bits', 'DoSvc', 'UsoSvc'))
        $Out.LockedRunning = Get-E4B2LockedCount -Root $Dir
        $Out.StopWuauserv = Stop-E4B2Service -Name 'wuauserv'
        $Out.LockedNoWuauserv = Get-E4B2LockedCount -Root $Dir
        $Out.StopBits = Stop-E4B2Service -Name 'bits'
        $Out.LockedNoBits = Get-E4B2LockedCount -Root $Dir
        $Out.Delete = Remove-E4B2OldFiles -Root $Dir -CutoffUtc ([datetime]::MaxValue)
        $Out.After = Get-E4B2FolderSize -Path $Dir
    }
    catch { $Out.Error = Format-E4B2Error $_ }
    finally {
        foreach ($Name in 'bits', 'wuauserv') {
            $Was = @($Out.ServicesBefore | Where-Object { $_.Name -eq $Name })[0]
            if ($null -ne $Was -and $Was.State -eq 'Running') { $Out.Restore += Start-E4B2Service -Name $Name }
        }
        $Out.ServicesAfter = @(Get-E4B2ServiceState -Names @('wuauserv', 'bits', 'DoSvc', 'UsoSvc'))
        $Out.FreeAfter = Get-E4B2FreeBytes
    }
    return [pscustomobject]$Out
}

function Invoke-E4B2DeliveryOptimizationProbe {
    # Optymalizacja dostarczania: dostępność cmdletu, rozmiar folderów pamięci DO przed i po Delete-DeliveryOptimizationCache -Force.
    [CmdletBinding()]
    param()
    Assert-E4B2InsideVm
    $Folders = @(
        (Join-Path $env:SystemRoot 'SoftwareDistribution\DeliveryOptimization'),
        (Join-Path $env:SystemRoot 'ServiceProfiles\NetworkService\AppData\Local\Microsoft\Windows\DeliveryOptimization')
    )
    $Out = [ordered]@{ Command = $null; Module = $null; StatusCount = $null; StatusError = $null; Before = @(); After = @(); Service = $null; FreeBefore = (Get-E4B2FreeBytes); FreeAfter = $null
        Seconds = $null; Output = $null; Error = $null }
    $Command = Get-Command -Name 'Delete-DeliveryOptimizationCache' -ErrorAction SilentlyContinue
    $Out.Command = if ($null -ne $Command) { "$($Command.CommandType) $($Command.Name)" } else { $null }
    $Out.Module = if ($null -ne $Command) { "$($Command.ModuleName) $($Command.Version)" } else { $null }
    $Out.Service = @(Get-E4B2ServiceState -Names @('DoSvc'))[0]
    try { $Status = @(Get-DeliveryOptimizationStatus -ErrorAction Stop); $Out.StatusCount = $Status.Count; $Out.StatusStates = @($Status | ForEach-Object { [string]$_.Status } | Sort-Object -Unique) } catch { $Out.StatusError = Format-E4B2Error $_ }
    $Out.Before = @($Folders | ForEach-Object { Get-E4B2FolderSize -Path $_ })
    if ($null -ne $Command) {
        $Watch = [System.Diagnostics.Stopwatch]::StartNew()
        try { $Out.Output = @(Delete-DeliveryOptimizationCache -Force -ErrorAction Stop | ForEach-Object { [string]$_ }) } catch { $Out.Error = Format-E4B2Error $_ }
        $Out.Seconds = [math]::Round($Watch.Elapsed.TotalSeconds, 1)
    }
    try { $Out.StatusCountAfter = @(Get-DeliveryOptimizationStatus -ErrorAction Stop).Count } catch { $Out.StatusError = "$($Out.StatusError) | po: $(Format-E4B2Error $_)" }
    $Out.After = @($Folders | ForEach-Object { Get-E4B2FolderSize -Path $_ })
    $Out.FreeAfter = Get-E4B2FreeBytes
    return [pscustomobject]$Out
}

function Invoke-E4B2ThumbcacheProbe {
    # Miniatury konta: thumbcache_*.db przy działającym explorer.exe konta; które da się usunąć, a które są zablokowane.
    [CmdletBinding()]
    param([Parameter(Mandatory)][string]$UserSid)
    Assert-E4B2InsideVm
    $Out = [ordered]@{ Folder = $null; Explorer = @(); Files = @(); FreeBefore = (Get-E4B2FreeBytes); FreeAfter = $null; Error = $null }
    $Profile1 = Get-E4B2UserProfilePath -UserSid $UserSid
    if (-not $Profile1) { $Out.Error = 'brak ProfileImagePath'; return [pscustomobject]$Out }
    $Out.Folder = Join-Path $Profile1 'AppData\Local\Microsoft\Windows\Explorer'
    $Out.Explorer = @(Get-CimInstance -ClassName Win32_Process -Filter "Name='explorer.exe'" -ErrorAction SilentlyContinue | ForEach-Object {
            $Owner = Invoke-CimMethod -InputObject $_ -MethodName GetOwner -ErrorAction SilentlyContinue
            "$($_.ProcessId):$($Owner.User):$($_.SessionId)"
        })
    if ([System.IO.Directory]::Exists($Out.Folder)) {
        $Out.Files = @([System.IO.Directory]::GetFiles($Out.Folder, 'thumbcache_*.db') | ForEach-Object {
                $Locked = Test-E4B2FileLocked -Path $_
                $Result = Remove-E4B2File -Path $_
                [pscustomobject]@{ Name = (Split-Path -Leaf $_); Bytes = $Result.Bytes; LockedBefore = $Locked; Deleted = $Result.Deleted; Error = $Result.Error }
            })
    }
    $Out.FreeAfter = Get-E4B2FreeBytes
    return [pscustomobject]$Out
}

function Invoke-E4B2DismProbe {
    # DISM na zdrowej VM bez sieci: /ScanHealth, /RestoreHealth (w trakcie drugi DISM /CheckHealth i SFC /verifyfile – co robią,
    # gdy serwisowanie trwa), /RestoreHealth /Source:<brak> /LimitAccess.
    [CmdletBinding()]
    param([Parameter(Mandatory)]$Plan)
    Assert-E4B2InsideVm
    $Watch = @($Plan.WatchNames)
    $Sample = [int]$Plan.SampleSeconds
    $Out = [ordered]@{}
    $Steps = @(
        @{ Name = 'dism-scanhealth'; Arguments = '/Online /Cleanup-Image /ScanHealth'; Concurrent = @() }
        @{ Name = 'dism-restorehealth'; Arguments = '/Online /Cleanup-Image /RestoreHealth'; Concurrent = @(
                @{ Name = 'dism-concurrent-checkhealth'; Tool = 'dism.exe'; Arguments = '/Online /Cleanup-Image /CheckHealth'; DelaySeconds = [int]$Plan.ConcurrentDelaySeconds; TimeoutSeconds = [int]$Plan.ConcurrentTimeoutSeconds }
                @{ Name = 'sfc-concurrent-verifyfile'; Tool = 'sfc.exe'; Arguments = "/verifyfile=$($Plan.ConcurrentVerifyFile)"; DelaySeconds = ([int]$Plan.ConcurrentDelaySeconds + 15); TimeoutSeconds = [int]$Plan.ConcurrentTimeoutSeconds }
            )
        }
        @{ Name = 'dism-restorehealth-nosource'; Arguments = "/Online /Cleanup-Image /RestoreHealth /Source:$($Plan.NoSourcePath) /LimitAccess"; Concurrent = @() }
    )
    foreach ($Step in $Steps) {
        try {
            $Out[$Step.Name] = Invoke-E4B2Tool -Name $Step.Name -Tool 'dism.exe' -Arguments $Step.Arguments -TimeoutSeconds ([int]$Plan.DismTimeoutSeconds) -WatchNames $Watch -SampleSeconds $Sample `
                -Concurrent $Step.Concurrent -Logs -RawDirectory ([string]$Plan.RawDirectory)
        }
        catch { $Out[$Step.Name] = [pscustomobject]@{ Name = $Step.Name; Error = (Format-E4B2Error $_) } }
    }
    return [pscustomobject]$Out
}

function Invoke-E4B2SfcProbe {
    # SFC /scannow na zdrowej VM (w trakcie drugi SFC /verifyfile), segment [SR] CBS.log z tego przebiegu, oczekujący restart.
    [CmdletBinding()]
    param([Parameter(Mandatory)]$Plan)
    Assert-E4B2InsideVm
    $Concurrent = @(@{ Name = 'sfc-concurrent-verifyfile2'; Tool = 'sfc.exe'; Arguments = "/verifyfile=$($Plan.ConcurrentVerifyFile)"; DelaySeconds = [int]$Plan.ConcurrentDelaySeconds; TimeoutSeconds = [int]$Plan.ConcurrentTimeoutSeconds })
    $Out = [ordered]@{ Scannow = $null; Error = $null }
    try {
        $Out.Scannow = Invoke-E4B2Tool -Name 'sfc-scannow' -Tool 'sfc.exe' -Arguments '/scannow' -TimeoutSeconds ([int]$Plan.SfcTimeoutSeconds) -WatchNames @($Plan.WatchNames) `
            -SampleSeconds ([int]$Plan.SampleSeconds) -Concurrent $Concurrent -Logs -RawDirectory ([string]$Plan.RawDirectory)
    }
    catch { $Out.Error = Format-E4B2Error $_ }
    return [pscustomobject]$Out
}

function Invoke-E4B2CleanupProbe {
    # Czyszczenie: zestawy plików w %SystemRoot%\Temp, %TEMP% konta dso i %TEMP% Administratora -> cleanmgr „Temporary Files”
    # -> bezpośrednie usuwanie starych plików (model DeleteOldFiles) -> Optymalizacja dostarczania -> SoftwareDistribution\Download
    # -> AnalyzeComponentStore / StartComponentCleanup / AnalyzeComponentStore -> miniatury konta dso. Wolne miejsce przed i po każdym kroku.
    [CmdletBinding()]
    param(
        [Parameter(Mandatory)]$Plan,
        [Parameter(Mandatory)][string]$UserSid
    )
    Assert-E4B2InsideVm
    $Out = [ordered]@{ Steps = [ordered]@{}; Errors = @() }
    $Watch = @($Plan.WatchNames)
    $Sample = [int]$Plan.SampleSeconds
    $Profile1 = Get-E4B2UserProfilePath -UserSid $UserSid
    $Roots = [ordered]@{ system = (Join-Path $env:SystemRoot 'Temp'); user = $(if ($Profile1) { Join-Path $Profile1 'AppData\Local\Temp' } else { $null }); admin = [System.IO.Path]::GetTempPath().TrimEnd('\') }
    $Out.Roots = $Roots
    $Sets = [ordered]@{}
    try {
        $Free0 = Get-E4B2FreeBytes
        foreach ($Label in $Roots.Keys) {
            if (-not $Roots[$Label]) { continue }
            $Sets[$Label] = New-E4B2TempSet -Root $Roots[$Label] -Label $Label -GuardRoot ([string]$Plan.GuardRoot) -OldAgeDays ([int]$Plan.OldAgeDays) -VeryOldAgeDays ([int]$Plan.VeryOldAgeDays) -FileBytes ([int]$Plan.TempFileBytes)
        }
        $Out.Steps['prepare'] = [pscustomobject]@{ FreeBefore = $Free0; FreeAfter = (Get-E4B2FreeBytes); Sizes = @($Roots.Values | Where-Object { $_ } | ForEach-Object { Get-E4B2FolderSize -Path $_ }) }
        $Free1 = Get-E4B2FreeBytes
        $Cleanmgr = Invoke-E4B2CleanmgrRun -Name 'cleanmgr-temp' -Handler ([string]$Plan.TempHandler) -Id ([int]$Plan.SageRunId) -VolumeCachesKey ([string]$Plan.VolumeCachesKey) `
            -TimeoutSeconds ([int]$Plan.CleanmgrTimeoutSeconds) -WatchNames $Watch -SampleSeconds $Sample
        $Out.Steps['cleanmgr-temp'] = [pscustomobject]@{ FreeBefore = $Free1; FreeAfter = (Get-E4B2FreeBytes); Run = $Cleanmgr; Sets = [ordered]@{
                system = @(if ($Sets.Contains('system')) { Get-E4B2TempSetState -Set $Sets['system'] }); user = @(if ($Sets.Contains('user')) { Get-E4B2TempSetState -Set $Sets['user'] })
                admin = @(if ($Sets.Contains('admin')) { Get-E4B2TempSetState -Set $Sets['admin'] })
            }; Sizes = @($Roots.Values | Where-Object { $_ } | ForEach-Object { Get-E4B2FolderSize -Path $_ })
        }
        $Free2 = Get-E4B2FreeBytes
        $Cutoff = (Get-Date).ToUniversalTime().AddHours(-24)
        $Direct = [ordered]@{}
        foreach ($Label in 'system', 'user') { if ($Roots[$Label]) { $Direct[$Label] = Remove-E4B2OldFiles -Root $Roots[$Label] -CutoffUtc $Cutoff } }
        $Out.Steps['direct-temp'] = [pscustomobject]@{ FreeBefore = $Free2; FreeAfter = (Get-E4B2FreeBytes); Results = $Direct; Sets = [ordered]@{
                system = @(if ($Sets.Contains('system')) { Get-E4B2TempSetState -Set $Sets['system'] }); user = @(if ($Sets.Contains('user')) { Get-E4B2TempSetState -Set $Sets['user'] })
            }
        }
    }
    catch { $Out.Errors += "temp: $(Format-E4B2Error $_)" }
    finally { foreach ($Set in $Sets.Values) { Close-E4B2TempSet -Set $Set } }
    try { $Out.Steps['delivery-optimization'] = Invoke-E4B2DeliveryOptimizationProbe } catch { $Out.Errors += "DO: $(Format-E4B2Error $_)" }
    try { $Out.Steps['download'] = Invoke-E4B2DownloadProbe -TestMegabytes ([int]$Plan.DownloadTestMegabytes) } catch { $Out.Errors += "Download: $(Format-E4B2Error $_)" }
    foreach ($Step in @(
            @{ Name = 'dism-analyze-before'; Arguments = '/Online /Cleanup-Image /AnalyzeComponentStore' }
            @{ Name = 'dism-startcomponentcleanup'; Arguments = '/Online /Cleanup-Image /StartComponentCleanup' }
            @{ Name = 'dism-analyze-after'; Arguments = '/Online /Cleanup-Image /AnalyzeComponentStore' }
        )) {
        try {
            $Out.Steps[$Step.Name] = Invoke-E4B2Tool -Name $Step.Name -Tool 'dism.exe' -Arguments $Step.Arguments -TimeoutSeconds ([int]$Plan.CleanupTimeoutSeconds) -WatchNames $Watch -SampleSeconds $Sample `
                -Logs -RawDirectory ([string]$Plan.RawDirectory)
        }
        catch { $Out.Errors += "$($Step.Name): $(Format-E4B2Error $_)" }
    }
    try { $Out.Steps['thumbcache'] = Invoke-E4B2ThumbcacheProbe -UserSid $UserSid } catch { $Out.Errors += "thumbcache: $(Format-E4B2Error $_)" }
    return [pscustomobject]$Out
}

function Invoke-E4B2WindowsOldProbe {
    # Windows.old (mechanizm A): zwykły folder z plikami testowymi -> cleanmgr /sagerun z flagą tylko pod „Previous Installations”
    # -> czy folder zniknął (cleanmgr go rozpoznał), ile miejsca wróciło. Istniejący Windows.old = nic nie tworzymy ani nie uruchamiamy.
    [CmdletBinding()]
    param([Parameter(Mandatory)]$Plan)
    Assert-E4B2InsideVm
    $Path = [string]$Plan.WindowsOldPath
    $Out = [ordered]@{ Path = $Path; Refused = $null; Inventory = $null; Created = $null; FreeBefore = $null; Cleanmgr = $null; ExistsAfter = $null; SizeAfter = $null; FreeAfter = $null
        Recognized = $null; Error = $null }
    $Out.Inventory = Get-E4B2SageInventory -VolumeCachesKey ([string]$Plan.VolumeCachesKey)
    if (Test-Path -LiteralPath $Path) { $Out.Refused = "'$Path' już istnieje przed pomiarem – nic nie utworzono ani nie uruchomiono."; return [pscustomobject]$Out }
    try {
        $Out.FreeBefore = Get-E4B2FreeBytes
        $Out.Created = New-E4B2WindowsOld -Path $Path -Megabytes ([int]$Plan.WindowsOldMegabytes)
        $Out.FreeAfterCreate = Get-E4B2FreeBytes
        $Out.Cleanmgr = Invoke-E4B2CleanmgrRun -Name 'cleanmgr-previous' -Handler ([string]$Plan.PreviousHandler) -Id ([int]$Plan.SageRunId) -VolumeCachesKey ([string]$Plan.VolumeCachesKey) `
            -TimeoutSeconds ([int]$Plan.CleanmgrTimeoutSeconds) -WatchNames @($Plan.WatchNames) -SampleSeconds ([int]$Plan.SampleSeconds) -WatchPath $Path
        $Out.ExistsAfter = [bool](Test-Path -LiteralPath $Path)
        if ($Out.ExistsAfter) { $Out.SizeAfter = Get-E4B2FolderSize -Path $Path }
        $Out.FreeAfter = Get-E4B2FreeBytes
        $Out.Recognized = (-not $Out.ExistsAfter)
    }
    catch { $Out.Error = Format-E4B2Error $_ }
    # To samo w sesji dso (kontekst DSOptimizera po UAC): nowy folder, gdy poprzedni zniknął; inaczej ten, który został.
    $User = [ordered]@{ Created = $null; Cleanmgr = $null; ExistsAfter = $null; SizeAfter = $null; FreeBefore = $null; FreeAfter = $null; Recognized = $null; Error = $null }
    try {
        if (-not (Test-Path -LiteralPath $Path)) { $User.Created = New-E4B2WindowsOld -Path $Path -Megabytes ([int]$Plan.WindowsOldMegabytes) }
        $User.FreeBefore = Get-E4B2FreeBytes
        $User.Cleanmgr = Invoke-E4B2CleanmgrUserRun -Name 'cleanmgr-previous-user' -Handler ([string]$Plan.PreviousHandler) -Id ([int]$Plan.SageRunId) -VolumeCachesKey ([string]$Plan.VolumeCachesKey) `
            -TimeoutSeconds ([int]$Plan.CleanmgrTimeoutSeconds) -WatchNames @($Plan.WatchNames) -SampleSeconds ([int]$Plan.SampleSeconds) -WatchPath $Path
        $User.ExistsAfter = [bool](Test-Path -LiteralPath $Path)
        if ($User.ExistsAfter) { $User.SizeAfter = Get-E4B2FolderSize -Path $Path }
        $User.FreeAfter = Get-E4B2FreeBytes
        $User.Recognized = (-not $User.ExistsAfter)
    }
    catch { $User.Error = Format-E4B2Error $_ }
    $Out.User = [pscustomobject]$User
    return [pscustomobject]$Out
}

function Invoke-E4B2NetworkProbe {
    # Z siecią (Default Switch): łączność, DISM /RestoreHealth ze źródłem Windows Update, potem stan DO i SoftwareDistribution\Download
    # (prawdziwe pliki, jeśli WU coś pobrał): pliki w użyciu przy usługach działających / bez wuauserv / bez bits.
    [CmdletBinding()]
    param([Parameter(Mandatory)]$Plan)
    Assert-E4B2InsideVm
    $Out = [ordered]@{ Connectivity = $null; RestoreHealth = $null; DeliveryOptimization = $null; Download = $null; Errors = @() }
    $Watch = [System.Diagnostics.Stopwatch]::StartNew()
    $Resolved = $null
    do {
        try { $Resolved = @(Resolve-DnsName -Name ([string]$Plan.NetworkName) -ErrorAction Stop | Where-Object { $_.Type -eq 'A' } | ForEach-Object { $_.IPAddress }) } catch { $Resolved = $null }
        if (-not $Resolved) { Start-Sleep -Seconds 5 }
    } while (-not $Resolved -and $Watch.Elapsed.TotalSeconds -lt [int]$Plan.NetworkWaitSeconds)
    $Out.Connectivity = [pscustomobject]@{ Name = $Plan.NetworkName; Addresses = @($Resolved); Seconds = [math]::Round($Watch.Elapsed.TotalSeconds, 1) }
    try {
        $Out.RestoreHealth = Invoke-E4B2Tool -Name 'dism-restorehealth-network' -Tool 'dism.exe' -Arguments '/Online /Cleanup-Image /RestoreHealth' -TimeoutSeconds ([int]$Plan.DismTimeoutSeconds) `
            -WatchNames @($Plan.WatchNames) -SampleSeconds ([int]$Plan.SampleSeconds) -Logs -RawDirectory ([string]$Plan.RawDirectory)
    }
    catch { $Out.Errors += "RestoreHealth: $(Format-E4B2Error $_)" }
    try {
        $Out.DeliveryOptimization = [pscustomobject]@{
            Status = @(Get-DeliveryOptimizationStatus -ErrorAction Stop | ForEach-Object { "$($_.FileId) $($_.FileSize) $($_.Status)" } | Select-Object -First 20)
            Folders = @((Join-Path $env:SystemRoot 'SoftwareDistribution\DeliveryOptimization'), (Join-Path $env:SystemRoot 'ServiceProfiles\NetworkService\AppData\Local\Microsoft\Windows\DeliveryOptimization') | ForEach-Object { Get-E4B2FolderSize -Path $_ })
        }
    }
    catch { $Out.Errors += "DO: $(Format-E4B2Error $_)" }
    # Usuwanie pamięci DO na prawdziwych plikach pobranych przez WU (w fazie offline pamięć była pusta).
    try { $Out.DeliveryOptimizationDelete = Invoke-E4B2DeliveryOptimizationProbe } catch { $Out.Errors += "DO delete: $(Format-E4B2Error $_)" }
    try { $Out.Download = Invoke-E4B2DownloadProbe -TestMegabytes 0 } catch { $Out.Errors += "Download: $(Format-E4B2Error $_)" }
    return [pscustomobject]$Out
}

function Invoke-E4B2Baseline {
    # Stan świeżej VM (bez skutku): narzędzia, kodowanie, VolumeCaches i StateFlags w użyciu, usługi, rozmiary folderów, logi, bezczynna VM.
    [CmdletBinding()]
    param(
        [Parameter(Mandatory)]$Plan,
        [Parameter(Mandatory)][string]$UserSid
    )
    $Profile1 = Get-E4B2UserProfilePath -UserSid $UserSid
    return [ordered]@{
        Tools          = @('dism.exe', 'sfc.exe', 'cleanmgr.exe' | ForEach-Object { $P = Get-E4B2ToolPath -Tool $_; [pscustomobject]@{ Tool = $_; Path = $P; Exists = [System.IO.File]::Exists($P); Version = $(if ([System.IO.File]::Exists($P)) { (Get-Item -LiteralPath $P).VersionInfo.FileVersion }) } })
        OemCodePage    = [Globalization.CultureInfo]::CurrentCulture.TextInfo.OEMCodePage
        AnsiCodePage   = [Globalization.CultureInfo]::CurrentCulture.TextInfo.ANSICodePage
        Profile        = $Profile1
        Sage           = Get-E4B2SageInventory -VolumeCachesKey ([string]$Plan.VolumeCachesKey)
        OwnFlag        = @(Get-E4B2SageFlags -VolumeCachesKey ([string]$Plan.VolumeCachesKey) -Id ([int]$Plan.SageRunId))
        Services       = @(Get-E4B2ServiceState -Names @($Plan.Services))
        Pending        = Get-E4B2PendingState
        FreeBytes      = Get-E4B2FreeBytes
        WindowsOld     = [bool](Test-Path -LiteralPath ([string]$Plan.WindowsOldPath))
        DoCommand      = [bool](Get-Command -Name 'Delete-DeliveryOptimizationCache' -ErrorAction SilentlyContinue)
        Folders        = @(
            (Join-Path $env:SystemRoot 'Temp'), $(if ($Profile1) { Join-Path $Profile1 'AppData\Local\Temp' }), (Join-Path $env:SystemRoot 'SoftwareDistribution\Download'),
            (Join-Path $env:SystemRoot 'SoftwareDistribution\DeliveryOptimization'), (Join-Path $env:SystemRoot 'ServiceProfiles\NetworkService\AppData\Local\Microsoft\Windows\DeliveryOptimization'),
            $(if ($Profile1) { Join-Path $Profile1 'AppData\Local\Microsoft\Windows\Explorer' })
        ) | Where-Object { $_ } | ForEach-Object { Get-E4B2FolderSize -Path $_ }
        Logs           = [ordered]@{ Cbs = (Get-E4B2FileLength -Path (Join-Path $env:SystemRoot 'Logs\CBS\CBS.log')); Dism = (Get-E4B2FileLength -Path (Join-Path $env:SystemRoot 'Logs\DISM\dism.log')) }
        Idle           = Get-E4B2IdleTimeline -Names @($Plan.WatchNames) -Seconds ([int]$Plan.IdleSampleSeconds) -SampleSeconds ([int]$Plan.SampleSeconds)
    }
}

function Invoke-E4B2Final {
    # Po wszystkich sondach (bez skutku): bezczynna VM, oczekujący restart, usługi, Windows.old.
    [CmdletBinding()]
    param([Parameter(Mandatory)]$Plan)
    return [ordered]@{
        Pending    = Get-E4B2PendingState
        Services   = @(Get-E4B2ServiceState -Names @($Plan.Services))
        WindowsOld = [bool](Test-Path -LiteralPath ([string]$Plan.WindowsOldPath))
        FreeBytes  = Get-E4B2FreeBytes
        Idle       = Get-E4B2IdleTimeline -Names @($Plan.WatchNames) -Seconds ([int]$Plan.FinalIdleSeconds) -SampleSeconds ([int]$Plan.SampleSeconds)
    }
}

function Invoke-E4B2Collection {
    # Punkt wejścia jednej fazy w VM: zwraca JSON {meta, phase, error, data}.
    [CmdletBinding()]
    param(
        [Parameter(Mandatory)][string]$PlanJson,
        [Parameter(Mandatory)][string]$Phase,
        [Parameter(Mandatory)][string]$UserSid
    )
    Assert-E4B2InsideVm
    $Plan = $PlanJson | ConvertFrom-Json
    $Document = [ordered]@{ meta = $null; phase = $Phase; error = $null; data = $null }
    try { $Document.meta = Get-ExploreMeta } catch { $Document.meta = [ordered]@{ Error = $_.Exception.Message } }
    try {
        switch ($Phase) {
            'baseline' { $Document.data = Invoke-E4B2Baseline -Plan $Plan -UserSid $UserSid }
            'dism' { $Document.data = Invoke-E4B2DismProbe -Plan $Plan }
            'sfc' { $Document.data = Invoke-E4B2SfcProbe -Plan $Plan }
            'cleanup' { $Document.data = Invoke-E4B2CleanupProbe -Plan $Plan -UserSid $UserSid }
            'windowsold' { $Document.data = Invoke-E4B2WindowsOldProbe -Plan $Plan }
            'network' { $Document.data = Invoke-E4B2NetworkProbe -Plan $Plan }
            'final' { $Document.data = Invoke-E4B2Final -Plan $Plan }
            default { throw "Nieznana faza '$Phase'." }
        }
    }
    catch { $Document.error = "$(Format-E4B2Error $_) | $($_.ScriptStackTrace)" }
    try { return ($Document | ConvertTo-Json -Depth 16) }
    catch { return ([ordered]@{ meta = $Document.meta; phase = $Phase; error = "ConvertTo-Json: $(Format-E4B2Error $_)"; data = $null } | ConvertTo-Json -Depth 6) }
}

# ---------------------------------------------------------------------------------------------------------------------
# Strona hosta (nic z tego nie jest wysyłane do VM).
# ---------------------------------------------------------------------------------------------------------------------

function Get-E4B2RemoteFunctionNames {
    return @(
        'Test-E4B2VmComputerName', 'Assert-E4B2InsideVm', 'Format-E4B2Error', 'Get-E4B2ToolPath', 'Get-E4B2StateFlagsName', 'Get-E4B2OutputAnalysis', 'Get-E4B2SrSummary', 'Get-E4B2KeyLines',
        'Get-E4B2PresenceSummary', 'Get-E4B2ProcessSample', 'Get-E4B2IdleTimeline', 'Get-E4B2FileLength', 'Read-E4B2FileFrom', 'Get-E4B2FreeBytes', 'Get-E4B2FolderSize', 'Get-E4B2PendingState',
        'Test-E4B2HandlerKey', 'Get-E4B2SageFlags', 'Get-E4B2SageInventory', 'Get-E4B2ServiceState', 'Test-E4B2FileLocked', 'Get-E4B2LockedCount', 'Get-E4B2TempSetState', 'Get-E4B2UserProfilePath',
        'Wait-E4B2ToolExit', 'Save-E4B2Raw', 'Start-E4B2Tool', 'Complete-E4B2Tool', 'Invoke-E4B2Tool', 'Set-E4B2SageFlag', 'Remove-E4B2SageFlag', 'Invoke-E4B2CleanmgrRun', 'Get-E4B2ChildProcesses', 'Stop-E4B2ProcessById', 'Invoke-E4B2CleanmgrUserRun', 'Remove-E4B2OldFiles',
        'New-E4B2TempSet', 'Close-E4B2TempSet', 'New-E4B2WindowsOld', 'Stop-E4B2Service', 'Start-E4B2Service', 'Remove-E4B2File', 'Invoke-E4B2DownloadProbe', 'Invoke-E4B2DeliveryOptimizationProbe',
        'Invoke-E4B2ThumbcacheProbe', 'Invoke-E4B2DismProbe', 'Invoke-E4B2SfcProbe', 'Invoke-E4B2CleanupProbe', 'Invoke-E4B2WindowsOldProbe', 'Invoke-E4B2NetworkProbe', 'Invoke-E4B2Baseline',
        'Invoke-E4B2Final', 'Invoke-E4B2Collection'
    )
}

function Get-E4B2HostOnlyFunctionNames {
    return @('Get-E4B2Plan', 'Get-E4B2TimeoutSeconds', 'Get-E4B2RemoteFunctionNames', 'Get-E4B2HostOnlyFunctionNames', 'Get-E4B2RemoteScript', 'Assert-E4B2OutDirectory', 'Get-E4B2PhaseOrder',
        'Get-E4B2NetworkPhases', 'Get-E4B2RunArguments', 'Invoke-E4B2VmPhase', 'Copy-E4B2RawFiles', 'Complete-E4B2VmCleanup', 'Invoke-E4B2VmRun', 'Invoke-E4B2Exploration')
}

function Get-E4B2TimeoutSeconds {
    # Limit na jedno narzędzie z planu (pkt 6): max(4 × zmierzony czas, 45 min) dla DISM/SFC i max(4 × zmierzony czas, 20 min) dla czyszczenia.
    [CmdletBinding()]
    [OutputType([int])]
    param(
        [Parameter(Mandatory)][double]$MeasuredSeconds,
        [Parameter(Mandatory)][ValidateSet('Servicing', 'Cleanup')][string]$Kind
    )
    $Floor = if ($Kind -eq 'Servicing') { 2700 } else { 1200 }
    return [int][math]::Ceiling([math]::Max(4 * $MeasuredSeconds, $Floor))
}

function Get-E4B2RemoteScript {
    # Tekst definicji funkcji zdalnych: część wspólna z modułu + funkcje E4B2 (FunctionInfo pobrane tu, w zasięgu skryptu).
    [CmdletBinding()]
    [OutputType([string])]
    param()
    $Names = @(Get-ExploreCommonRemoteFunctionNames) + @(Get-E4B2RemoteFunctionNames)
    return (ConvertTo-ExploreRemoteScript -Command @($Names | ForEach-Object { Get-Command -Name $_ -CommandType Function -ErrorAction Stop }))
}

function Assert-E4B2OutDirectory {
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

function Get-E4B2PhaseOrder {
    # Wybrane fazy w stałej kolejności przebiegu; baseline zawsze pierwsza; network (sieć) tylko z siecią, po pomiarach offline.
    [CmdletBinding()]
    [OutputType([string[]])]
    param(
        [Parameter(Mandatory)][string[]]$Phase,
        [bool]$Network = $true
    )
    $Order = @('baseline', 'dism', 'sfc', 'cleanup', 'windowsold', 'network', 'final')
    $Wanted = @($Phase) + @('baseline')
    if (-not $Network) { $Wanted = @($Wanted | Where-Object { @(Get-E4B2NetworkPhases) -notcontains $_ }) }
    return @($Order | Where-Object { $Wanted -contains $_ })
}

function Get-E4B2NetworkPhases {
    # Fazy oznaczone w planie „sieć”: karta VM podłączona do 'Default Switch' tylko na czas tej fazy.
    [CmdletBinding()]
    [OutputType([string[]])]
    param()
    return @('network')
}

function Get-E4B2RunArguments {
    [CmdletBinding()]
    param(
        [Parameter(Mandatory)][string[]]$VmName,
        [Parameter(Mandatory)][string[]]$Phase,
        [Parameter(Mandatory)][string]$OutDirectory,
        [Parameter(Mandatory)][string]$CheckpointName,
        [Parameter(Mandatory)][int]$ReadyTimeoutSeconds,
        [bool]$Network = $true
    )
    return [ordered]@{ VmName = @($VmName); Phases = @(Get-E4B2PhaseOrder -Phase $Phase -Network $Network); Network = $Network; OutDirectory = $OutDirectory
        CheckpointName = $CheckpointName; ReadyTimeoutSeconds = $ReadyTimeoutSeconds; Plan = (Get-E4B2Plan) }
}

function Invoke-E4B2VmPhase {
    # Jedna faza w VM przez otwartą sesję: definicje + Invoke-E4B2Collection; zwraca sparsowany dokument.
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
        Invoke-E4B2Collection -PlanJson $Json -Phase $Which -UserSid $Sid
    }
    $Raw = @(Invoke-Command -Session $Session -ScriptBlock $Remote -ArgumentList @($RemoteScript, $PlanJson, $Phase, $UserSid))
    $Text = [string]($Raw | Where-Object { $_ -is [string] } | Select-Object -Last 1)
    if ([string]::IsNullOrWhiteSpace($Text)) { throw "Faza '$Phase' nie zwróciła JSON." }
    return ($Text | ConvertFrom-Json)
}

function Copy-E4B2RawFiles {
    # Surowe pliki z C:\dso\e4b2\raw w VM do <OutDirectory>\<VM>\raw, kawałkami po 4 MB jako Base64 (Copy-Item -FromSession zgłaszał OOM na PS Direct).
    [CmdletBinding()]
    param(
        [Parameter(Mandatory)][AllowNull()][System.Management.Automation.Runspaces.PSSession]$Session,
        [Parameter(Mandatory)][string]$Destination
    )
    Assert-DsoVmLiveAllowed
    $null = New-Item -ItemType Directory -Path $Destination -Force
    $Files = @(Invoke-Command -Session $Session -ScriptBlock { @(Get-ChildItem -LiteralPath 'C:\dso\e4b2\raw' -File -ErrorAction SilentlyContinue | ForEach-Object { [pscustomobject]@{ Name = $_.Name; Length = $_.Length } }) })
    $Copied = @()
    foreach ($File in $Files) {
        $Target = Join-Path $Destination $File.Name
        $Stream = [System.IO.File]::Create($Target)
        try {
            for ($Offset = 0L; $Offset -lt [long]$File.Length; $Offset += 4MB) {
                $Base64 = [string](Invoke-Command -Session $Session -ArgumentList @($File.Name, $Offset) -ScriptBlock {
                        param([string]$Name, [long]$At)
                        $S = [System.IO.File]::Open("C:\dso\e4b2\raw\$Name", 'Open', 'Read', 'ReadWrite')
                        try { $null = $S.Seek($At, 'Begin'); $B = New-Object byte[] ([math]::Min(4MB, $S.Length - $At)); $N = $S.Read($B, 0, $B.Length); [Convert]::ToBase64String($B, 0, $N) } finally { $S.Dispose() }
                    })
                $Bytes = [Convert]::FromBase64String($Base64)
                $Stream.Write($Bytes, 0, $Bytes.Length)
            }
        }
        finally { $Stream.Dispose() }
        $Copied += $Target
    }
    return $Copied
}

function Complete-E4B2VmCleanup {
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

function Invoke-E4B2VmRun {
    # Jedna VM: revert -> start (bez sieci) -> gotowość -> fazy (sieć tylko na fazę network) -> kopia surowych plików
    # -> ZAWSZE revert, odłączenie karty i sprawdzenie stanu końcowego -> zapis JSON.
    [CmdletBinding()]
    [OutputType([string])]
    param(
        [Parameter(Mandatory)][ValidateSet('DSO-26H2-Home', 'DSO-26H2-Pro')][string]$VmName,
        [Parameter(Mandatory)][string]$OutDirectory,
        [Parameter(Mandatory)][string[]]$Phases,
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
    $RawFiles = @()
    $Ready = $false
    $VmDirectory = Join-Path $OutDirectory $VmName
    try {
        Restore-VmCleanState -VmName $VmName -CheckpointName $CheckpointName
        Start-VM -Name $VmName
        $Session = Wait-VmReady -VmName $VmName -Credential $Credential -TimeoutSeconds $ReadyTimeoutSeconds
        $Ready = $true
        $UserSid = [string](Invoke-Command -Session $Session -ScriptBlock { (Get-LocalUser -Name 'dso').SID.Value })
        if ([string]::IsNullOrWhiteSpace($UserSid)) { throw "Nie udało się odczytać SID użytkownika 'dso' w VM '$VmName'." }
        # Chwila na dokończenie logowania dso (pasek zadań, Start, rejestracja pakietów) przed pierwszym odczytem.
        Start-Sleep -Seconds 45
        $RemoteScript = Get-E4B2RemoteScript
        $PlanJson = ConvertTo-Json -InputObject (Get-E4B2Plan) -Depth 6
        $NetworkPhases = @(Get-E4B2NetworkPhases)
        foreach ($Phase in $Phases) {
            $Network = ($NetworkPhases -contains $Phase)
            if ($Network) {
                Connect-VMNetworkAdapter -VMName $VmName -SwitchName 'Default Switch' -ErrorAction Stop
                $Timeline += "$(Get-Date -Format 'HH:mm:ss') sieć podłączona"
            }
            $Timeline += "$(Get-Date -Format 'HH:mm:ss') $Phase"
            Write-Host "$(Get-Date -Format 'HH:mm:ss') $VmName $Phase"
            try { $Results[$Phase] = Invoke-E4B2VmPhase -Session $Session -RemoteScript $RemoteScript -PlanJson $PlanJson -Phase $Phase -UserSid $UserSid }
            catch { $PrimaryError = (@($PrimaryError, "faza ${Phase}: $($_.Exception.Message)") | Where-Object { $_ }) -join ' | '; if (Test-VmAuthFailure -Exception $_.Exception) { throw } }
            finally {
                if ($Network) {
                    try { Disconnect-VMNetworkAdapter -VMName $VmName -ErrorAction Stop; $Timeline += "$(Get-Date -Format 'HH:mm:ss') sieć odłączona" }
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
        if ($Ready) {
            try { $RawFiles = @(Copy-E4B2RawFiles -Session $Session -Destination (Join-Path $VmDirectory 'raw')) }
            catch { $PrimaryError = (@($PrimaryError, "kopia surowych plików: $($_.Exception.Message)") | Where-Object { $_ }) -join ' | ' }
        }
        if ($null -ne $Session) { Remove-PSSession -Session $Session -ErrorAction SilentlyContinue }
        # Sprzątanie PRZED zapisem, żeby jego wynik (także błędy) trafił do JSON; błąd sprzątania nigdy nie jest ukrywany.
        $Cleanup = Complete-E4B2VmCleanup -VmName $VmName -CheckpointName $CheckpointName
        if (-not $Cleanup.Ok) {
            $CleanupMessage = "sprzątanie VM: $(@($Cleanup.Problems) -join ' | ')"
            Write-Warning "VM '$VmName': $CleanupMessage"
            $PrimaryError = (@($PrimaryError, $CleanupMessage) | Where-Object { $_ }) -join ' | '
        }
        $Result = [ordered]@{ schema = 1; vmName = $VmName; checkpoint = $CheckpointName; userSid = $UserSid; startedAt = $StartedAt.ToString('o'); finishedAt = (Get-Date).ToString('o')
            error = $PrimaryError; cleanup = $Cleanup; timeline = $Timeline; rawFiles = @($RawFiles | ForEach-Object { Split-Path -Leaf $_ }); phases = $Results }
        $Saved = Save-ExploreResult -Result $Result -OutDirectory $OutDirectory -VmName $VmName -FileName 'e4b2.json'
    }
    if ($PrimaryError) { throw "VM '$VmName': $PrimaryError (wynik częściowy: $Saved)" }
    return $Saved
}

function Invoke-E4B2Exploration {
    # Wszystkie wybrane VM po kolei. Błąd uwierzytelnienia = stop całości; inny błąd = zapis i dalej.
    [CmdletBinding()]
    param([Parameter(Mandatory)][System.Collections.IDictionary]$Arguments)
    $Paths = New-Object System.Collections.Generic.List[string]
    $Errors = New-Object System.Collections.Generic.List[string]
    foreach ($Vm in $Arguments.VmName) {
        try {
            Write-Host "$(Get-Date -Format 'HH:mm:ss') $Vm"
            $Paths.Add((Invoke-E4B2VmRun -VmName $Vm -OutDirectory $Arguments.OutDirectory -Phases $Arguments.Phases -CheckpointName $Arguments.CheckpointName -ReadyTimeoutSeconds $Arguments.ReadyTimeoutSeconds))
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
        $OutDirectory = Assert-E4B2OutDirectory -Path $OutDirectory -RepoRoot $RepoRoot
        $OutDirectory = Resolve-ExploreOutDirectory -Path $OutDirectory
        $RunArguments = Get-E4B2RunArguments -VmName $VmName -Phase $Phase -OutDirectory $OutDirectory -CheckpointName $CheckpointName -ReadyTimeoutSeconds $ReadyTimeoutSeconds -Network (-not $NoNetwork)
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
        Write-Host "VM: $($RunArguments.VmName -join ', '); fazy: $($RunArguments.Phases -join ', ')"
        $Outcome = Invoke-E4B2Exploration -Arguments $RunArguments
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
