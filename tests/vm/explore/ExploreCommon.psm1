<#
Instrukcja uruchomienia:
    Moduł ładują skrypty eksploracji (Invoke-C1Exploration.ps1, Invoke-C2Exploration.ps1); nie uruchamia się go osobno.
    Testy: Invoke-Pester -Path tests/vm/tests/ExploreCommon.Tests.ps1
Opis działania:
    Wspólna część skryptów eksploracji VM (zbieranie dowodów z prawdziwej VM 26H2 oficjalnymi narzędziami).
    Strona VM (funkcje wysyłane do VM jako tekst, samowystarczalne, nazwy z prefiksem Explore, lista w
    Get-ExploreCommonRemoteFunctionNames): odczyt wartości i zrzut kluczy rejestru jako {Exists, Kind, Value, Hex},
    uruchomienie programu natywnego z przechwyceniem stdout/stderr (strona OEM) i kodem wyjścia, rekord kroku
    (komenda, kod wyjścia, stdout/stderr, rejestr, czas), porównanie odczytów rejestru, metadane systemu, kolekcja sond.
    Strona hosta: przebieg jednej VM (przywrócenie checkpointu -> start -> Wait-VmReady -> sesja -> zbieranie -> ZAWSZE
    zatrzymanie i powrót do checkpointu), zapis JSON (UTF-8) i pętla po VM, którą zatrzymuje pierwszy błąd uwierzytelnienia
    (rozpoznany przez Test-VmAuthFailure z VmHost, nie przez kopię tekstu). Dla skryptów z własną orkiestracją (od E5): reguła katalogu
    wyników (Assert-ExploreOutDirectory), kopia surowych plików z VM kawałkami Base64 (Copy-ExploreRawFiles) i kontrakt stanu końcowego
    VM (Complete-ExploreVmCleanup: revert, odłączenie dodanych dysków i karty, VM Off, karta bez przełącznika, dyski odłączone).
    Dotyka wyłącznie VM DSO-26H2-Home i DSO-26H2-Pro; na hoście niczego nie zmienia.
#>
Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

$script:ModulesDirectory = Join-Path (Split-Path -Parent $PSScriptRoot) 'modules'
# Bez -Force: gdy moduły są już załadowane (skrypt eksploracji, testy z mockami), nie przeładowujemy ich.
Import-Module (Join-Path $script:ModulesDirectory 'VmCommon.psm1')
Import-Module (Join-Path $script:ModulesDirectory 'VmHost.psm1')

# ---------------------------------------------------------------------------------------------------------------------
# Funkcje wysyłane do VM (samowystarczalne: wołają tylko siebie nawzajem i cmdlety systemowe).
# ---------------------------------------------------------------------------------------------------------------------

function Get-ExploreCommonRemoteFunctionNames {
    [CmdletBinding()]
    [OutputType([string[]])]
    param()
    return @(
        'Get-ExploreRegistryData', 'Open-ExploreRegistryKey', 'Read-ExploreRegistryValue', 'Read-ExploreRegistryKey', 'Read-ExploreRegistryTree',
        'Invoke-ExploreNative', 'Invoke-ExploreStep', 'Compare-ExploreRegistry', 'Get-ExploreMeta', 'Invoke-ExploreCollection'
    )
}

function Get-ExploreRegistryData {
    # Odczyt jednej wartości z otwartego klucza: Kind (RegistryValueKind), Value, Hex (dla DWORD/QWORD).
    [CmdletBinding()]
    param(
        [Parameter(Mandatory)]$Key,
        [Parameter(Mandatory)][AllowEmptyString()][string]$Name
    )
    $Raw = $Key.GetValue($Name, $null, 'DoNotExpandEnvironmentNames')
    if ($null -eq $Raw) { return $null }
    $Kind = [string]$Key.GetValueKind($Name)
    $Hex = $null
    $Value = $Raw
    if ($Raw -is [byte[]]) { $Value = [BitConverter]::ToString($Raw) }
    elseif ($Raw -is [string[]]) { $Value = @($Raw) }
    elseif ($Kind -eq 'DWord') { $Hex = ('0x{0:X8}' -f [int]$Raw) }
    elseif ($Kind -eq 'QWord') { $Hex = ('0x{0:X16}' -f [long]$Raw) }
    return [pscustomobject]@{ Kind = $Kind; Value = $Value; Hex = $Hex }
}

function Open-ExploreRegistryKey {
    # JEDYNE miejsce otwierania klucza: zwraca otwarty klucz (wołający robi Dispose) albo $null, gdy klucza nie ma.
    # Inne błędy (np. brak uprawnień) są rzucane. Dozwolone tylko 'HKLM\' i 'HKU\'.
    [CmdletBinding()]
    param([Parameter(Mandatory)][string]$Path)
    if ($Path -notmatch '^(HKLM|HKU)\\') { throw "Open-ExploreRegistryKey: dozwolone tylko 'HKLM\' i 'HKU\' (podano '$Path')." }
    $Native = $Path -replace '^HKLM\\', 'Registry::HKEY_LOCAL_MACHINE\' -replace '^HKU\\', 'Registry::HKEY_USERS\'
    try { return (Get-Item -LiteralPath $Native -ErrorAction Stop) }
    catch {
        $NotFound = ($_.Exception -is [System.Management.Automation.ItemNotFoundException]) -or ($_.Exception -is [System.Management.Automation.DriveNotFoundException])
        if (-not $NotFound) { throw }
        return $null
    }
}

function Read-ExploreRegistryValue {
    # Zwraca {Key, KeyExists, Exists, Kind, Value, Hex}. Exists=false (brak wartości) != Value=0.
    [CmdletBinding()]
    param(
        [Parameter(Mandatory)][string]$Path,
        [Parameter(Mandatory)][string]$Name
    )
    $Result = [ordered]@{ Key = "$Path\$Name"; KeyExists = $false; Exists = $false; Kind = $null; Value = $null; Hex = $null }
    $Key = Open-ExploreRegistryKey -Path $Path
    if ($null -ne $Key) {
        try {
            $Result.KeyExists = $true
            $Data = Get-ExploreRegistryData -Key $Key -Name $Name
            if ($null -ne $Data) {
                $Result.Exists = $true
                $Result.Kind = $Data.Kind
                $Result.Value = $Data.Value
                $Result.Hex = $Data.Hex
            }
        }
        finally { $Key.Dispose() }
    }
    return [pscustomobject]$Result
}

function Read-ExploreRegistryKey {
    # Zrzut wszystkich wartości klucza (nazwa, typ, wartość) i nazw podkluczy.
    [CmdletBinding()]
    param([Parameter(Mandatory)][string]$Path)
    $Result = [ordered]@{ Path = $Path; KeyExists = $false; Values = @(); SubKeys = @() }
    $Key = Open-ExploreRegistryKey -Path $Path
    if ($null -ne $Key) {
        try {
            $Result.KeyExists = $true
            $Values = @()
            foreach ($ValueName in @($Key.GetValueNames())) {
                $Data = Get-ExploreRegistryData -Key $Key -Name $ValueName
                if ($null -eq $Data) { continue }
                $Label = if ($ValueName -eq '') { '(default)' } else { $ValueName }
                $Values += [pscustomobject]@{ Name = $Label; Kind = $Data.Kind; Value = $Data.Value; Hex = $Data.Hex }
            }
            $Result.Values = @($Values)
            $Result.SubKeys = @($Key.GetSubKeyNames())
        }
        finally { $Key.Dispose() }
    }
    return [pscustomobject]$Result
}

function Read-ExploreRegistryTree {
    # Zrzut klucza i wszystkich podkluczy (płaska lista wyników Read-ExploreRegistryKey, kolejność: rodzic przed dziećmi).
    [CmdletBinding()]
    param(
        [Parameter(Mandatory)][string]$Path,
        [int]$MaxDepth = 8
    )
    $Node = Read-ExploreRegistryKey -Path $Path
    $Node
    if ($MaxDepth -gt 0) {
        foreach ($Sub in @($Node.SubKeys)) { Read-ExploreRegistryTree -Path "$Path\$Sub" -MaxDepth ($MaxDepth - 1) }
    }
}

function Invoke-ExploreNative {
    # Uruchamia program natywny z osobnym przechwyceniem stdout/stderr (nic nie trafia na konsolę) i kodem wyjścia.
    [CmdletBinding()]
    param(
        [Parameter(Mandatory)][string]$FilePath,
        [AllowEmptyCollection()][string[]]$Arguments = @(),
        [int]$TimeoutSeconds = 120,
        # Surowa linia argumentów (bez cytowania z Arguments): gdy podana, trafia do procesu dokładnie tak, jak jest. Dla linii komendy z menu powłoki.
        [string]$RawArguments
    )
    $Result = [ordered]@{ ExitCode = $null; Stdout = $null; Stderr = $null; Error = $null; TimedOut = $false; OemCodePage = $null }
    $Psi = New-Object System.Diagnostics.ProcessStartInfo
    $Psi.FileName = $FilePath
    # Narzędzia konsolowe (fsutil, powercfg, DISM, bcdedit) piszą do potoku w stronie kodowej OEM; bez tego polskie znaki są zniekształcone.
    try {
        $OemCodePage = [Globalization.CultureInfo]::CurrentCulture.TextInfo.OEMCodePage
        $OemEncoding = [Text.Encoding]::GetEncoding($OemCodePage)
        $Psi.StandardOutputEncoding = $OemEncoding
        $Psi.StandardErrorEncoding = $OemEncoding
        $Result.OemCodePage = $OemCodePage
    }
    catch { $Result.Error = "kodowanie OEM niedostępne, użyto domyślnego: $($_.Exception.Message)" }
    $Quoted = foreach ($Argument in $Arguments) { if ($Argument -match '[\s"]') { '"' + ($Argument -replace '"', '\"') + '"' } else { $Argument } }
    $Psi.Arguments = ($Quoted -join ' ')
    if ($PSBoundParameters.ContainsKey('RawArguments')) { $Psi.Arguments = $RawArguments }
    $Psi.UseShellExecute = $false
    $Psi.RedirectStandardOutput = $true
    $Psi.RedirectStandardError = $true
    $Psi.CreateNoWindow = $true
    $Process = $null
    try {
        $Process = [System.Diagnostics.Process]::Start($Psi)
        $OutTask = $Process.StandardOutput.ReadToEndAsync()
        $ErrTask = $Process.StandardError.ReadToEndAsync()
        if (-not $Process.WaitForExit($TimeoutSeconds * 1000)) {
            $Result.TimedOut = $true
            try { $Process.Kill() } catch { $null = $_ }
        }
        $Process.WaitForExit()
        $Result.ExitCode = $Process.ExitCode
        $Result.Stdout = $OutTask.Result
        $Result.Stderr = $ErrTask.Result
    }
    catch { $Result.Error = $_.Exception.Message }
    finally { if ($null -ne $Process) { $Process.Dispose() } }
    return [pscustomobject]$Result
}

function Invoke-ExploreStep {
    # Jeden krok sondy: akcja (zmiana oficjalnym narzędziem; może być pusta = sam odczyt), sekcja After (dodatkowe odczyty)
    # i odczyt wskazanych wartości/kluczy/drzew rejestru PO akcji. Błędy lądują w polu Error, nie przerywają zbierania.
    [CmdletBinding()]
    param(
        [Parameter(Mandatory)][string]$Name,
        [Parameter(Mandatory)][string]$Command,
        [scriptblock]$Action,
        [scriptblock]$After,
        [object[]]$Registry = @(),
        [string[]]$RegistryKeys = @(),
        [string[]]$RegistryTrees = @()
    )
    $Stopwatch = [System.Diagnostics.Stopwatch]::StartNew()
    $Record = [ordered]@{
        Name = $Name; Command = $Command; StartedAt = (Get-Date).ToString('o')
        ExitCode = $null; Stdout = $null; Stderr = $null; Error = $null; OemCodePage = $null
        Registry = @(); RegistryKeys = @(); Extra = $null; DurationMs = 0
    }
    $Errors = New-Object System.Collections.Generic.List[string]
    if ($null -ne $Action) {
        try {
            $Output = @(& $Action 6>$null)
            $Last = $Output | Where-Object { $null -ne $_ -and $null -ne $_.PSObject.Properties['ExitCode'] } | Select-Object -Last 1
            if ($null -ne $Last) {
                $Record.ExitCode = $Last.ExitCode
                $Record.Stdout = $Last.Stdout
                $Record.Stderr = $Last.Stderr
                if ($Last.PSObject.Properties['OemCodePage']) { $Record.OemCodePage = $Last.OemCodePage }
                if ($Last.PSObject.Properties['Error'] -and $Last.Error) { $Errors.Add([string]$Last.Error) }
            }
        }
        catch { $Errors.Add($_.Exception.Message) }
    }
    if ($null -ne $After) {
        try { $Record.Extra = (& $After 6>$null) }
        catch { $Errors.Add($_.Exception.Message) }
    }
    $Values = @()
    foreach ($Spec in $Registry) {
        try { $Values += Read-ExploreRegistryValue -Path ([string]$Spec.Path) -Name ([string]$Spec.Name) }
        catch { $Errors.Add("rejestr $($Spec.Path)\$($Spec.Name): $($_.Exception.Message)") }
    }
    $Record.Registry = @($Values)
    $Keys = @()
    foreach ($KeyPath in $RegistryKeys) {
        try { $Keys += Read-ExploreRegistryKey -Path $KeyPath }
        catch { $Errors.Add("klucz ${KeyPath}: $($_.Exception.Message)") }
    }
    foreach ($TreePath in $RegistryTrees) {
        try { $Keys += @(Read-ExploreRegistryTree -Path $TreePath) }
        catch { $Errors.Add("drzewo ${TreePath}: $($_.Exception.Message)") }
    }
    $Record.RegistryKeys = @($Keys)
    $Record.Error = if ($Errors.Count -gt 0) { $Errors -join ' | ' } else { $null }
    $Stopwatch.Stop()
    $Record.DurationMs = [int]$Stopwatch.ElapsedMilliseconds
    return [pscustomobject]$Record
}

function Compare-ExploreRegistry {
    # Różnice między dwiema listami wyników Read-ExploreRegistryValue (po kluczu): istnienie, typ, wartość.
    [CmdletBinding()]
    param(
        [Parameter(Mandatory)][AllowEmptyCollection()][object[]]$Baseline,
        [Parameter(Mandatory)][AllowEmptyCollection()][object[]]$Current
    )
    $Differences = @()
    foreach ($Before in $Baseline) {
        $Now = @($Current | Where-Object { $_.Key -eq $Before.Key })[0]
        if ($null -eq $Now) { $Differences += "$($Before.Key): brak w odczycie końcowym"; continue }
        if ($Before.Exists -ne $Now.Exists -or $Before.Kind -ne $Now.Kind -or [string]$Before.Value -ne [string]$Now.Value) {
            $Differences += "$($Before.Key): Exists=$($Before.Exists) Kind=$($Before.Kind) Value=$($Before.Value) -> Exists=$($Now.Exists) Kind=$($Now.Kind) Value=$($Now.Value)"
        }
    }
    return $Differences
}

function Get-ExploreMeta {
    [CmdletBinding()]
    param()
    $Version = 'HKLM\SOFTWARE\Microsoft\Windows NT\CurrentVersion'
    $Meta = [ordered]@{}
    foreach ($Name in @('CurrentBuild', 'UBR', 'EditionID', 'DisplayVersion', 'ProductName')) {
        $Entry = Read-ExploreRegistryValue -Path $Version -Name $Name
        $Meta[$Name] = $Entry.Value
    }
    $Meta.CollectedAt = (Get-Date).ToString('o')
    $Meta.Culture = (Get-Culture).Name
    $Meta.UICulture = (Get-UICulture).Name
    $Meta.PSVersion = [string]$PSVersionTable.PSVersion
    $Meta.Is64BitProcess = [Environment]::Is64BitProcess
    $Meta.ComputerName = $env:COMPUTERNAME
    return $Meta
}

function Invoke-ExploreCollection {
    # Punkt wejścia po stronie VM: zwraca JEDEN string JSON (meta + dodatkowe pola + wszystkie sondy).
    # Probe: słownik nazwa -> scriptblock sondy; Extra: dodatkowe pola dokumentu (np. userSid), wstawiane między meta i probes.
    [CmdletBinding()]
    param(
        [Parameter(Mandatory)][System.Collections.IDictionary]$Probe,
        [System.Collections.IDictionary]$Extra = @{}
    )
    $Probes = [ordered]@{}
    foreach ($ProbeName in $Probe.Keys) {
        try { $Probes[$ProbeName] = @(& $Probe[$ProbeName]) }
        catch { $Probes[$ProbeName] = @([pscustomobject]@{ Name = 'BLAD-SONDY'; Error = $_.Exception.Message }) }
    }
    $Meta = $null
    try { $Meta = Get-ExploreMeta } catch { $Meta = [ordered]@{ Error = $_.Exception.Message } }
    $Document = [ordered]@{ meta = $Meta }
    foreach ($Key in $Extra.Keys) { $Document[$Key] = $Extra[$Key] }
    $Document['probes'] = $Probes
    return ($Document | ConvertTo-Json -Depth 12)
}

# ---------------------------------------------------------------------------------------------------------------------
# Strona hosta.
# ---------------------------------------------------------------------------------------------------------------------

function ConvertTo-ExploreRemoteScript {
    # Tekst definicji funkcji zdalnych (z obiektów FunctionInfo); w VM ładowany przez dot-sourcing [scriptblock]::Create(...).
    # FunctionInfo pobiera wołający (skrypt widzi własne funkcje, moduł by ich nie zobaczył).
    [CmdletBinding()]
    [OutputType([string])]
    param([Parameter(Mandatory)][System.Management.Automation.FunctionInfo[]]$Command)
    $Parts = foreach ($Item in $Command) { "function $($Item.Name) {`n$($Item.Definition)`n}" }
    return ($Parts -join "`n`n")
}

function Resolve-ExploreOutDirectory {
    # Bezwzględna ścieżka katalogu wyników liczona od $PWD; rzuca, gdy dostawca != FileSystem (nic nie jest tworzone). Wołać na starcie, przed pracą na VM.
    [CmdletBinding()]
    [OutputType([string])]
    param([Parameter(Mandatory)][string]$Path)
    $Provider = $null
    $Drive = $null
    $Resolved = $ExecutionContext.SessionState.Path.GetUnresolvedProviderPathFromPSPath($Path, [ref]$Provider, [ref]$Drive)
    if ($null -eq $Provider -or $Provider.Name -ne 'FileSystem') {
        $ProviderName = if ($null -ne $Provider) { $Provider.Name } else { '(nieznany)' }
        throw "OutDirectory '$Path' nie leży w dostawcy FileSystem (dostawca: $ProviderName)."
    }
    return [System.IO.Path]::GetFullPath($Resolved)
}

function Test-ExploreGitIgnored {
    # Czy ścieżka (może jeszcze nie istnieć) jest ignorowana przez git w repozytorium RepoRoot (git check-ignore -q: kod 0 = ignorowana).
    # Brak gita albo błąd (np. RepoRoot nie jest repozytorium) = $false.
    [CmdletBinding()]
    [OutputType([bool])]
    param(
        [Parameter(Mandatory)][string]$Path,
        [Parameter(Mandatory)][string]$RepoRoot
    )
    if (-not (Get-Command git.exe -CommandType Application -ErrorAction SilentlyContinue)) { return $false }
    if (-not (Test-Path -LiteralPath $RepoRoot -PathType Container)) { return $false }
    $Previous = $ErrorActionPreference
    $ErrorActionPreference = 'Continue'   # stderr gita (np. "fatal: not a git repository") nie może przerwać skryptu
    Try {
        & git.exe -C $RepoRoot check-ignore -q -- $Path 2>$null
        return ($LASTEXITCODE -eq 0)
    }
    Finally { $ErrorActionPreference = $Previous }
}

function Assert-ExploreOutDirectory {
    # -OutDirectory skryptu eksploracji: wymagany, bezwzględny (C:\... albo \\serwer\udział\...), poza repo; w repo tylko katalog ignorowany przez git
    # (git check-ignore). Zwraca pełną ścieżkę bez końcowego '\'. Example = przykład ścieżki w komunikacie o braku parametru.
    [CmdletBinding()]
    [OutputType([string])]
    param(
        [AllowEmptyString()][string]$Path,
        [Parameter(Mandatory)][string]$RepoRoot,
        [string]$Example = '...\dso-explore\<pomiar>\<vm>'
    )
    if ([string]::IsNullOrWhiteSpace($Path)) { throw "-OutDirectory jest wymagany (ścieżka bezwzględna poza repo, np. $Example)." }
    if ($Path -notmatch '^[A-Za-z]:\\' -and $Path -notmatch '^\\\\[^\\]+\\[^\\]+') { throw "-OutDirectory musi być ścieżką bezwzględną (C:\... albo \\serwer\udział\...): '$Path'." }
    $Full = [System.IO.Path]::GetFullPath($Path).TrimEnd('\')
    $Root = [System.IO.Path]::GetFullPath($RepoRoot).TrimEnd('\')
    $InRepo = ($Full -ieq $Root) -or $Full.StartsWith("$Root\", [System.StringComparison]::OrdinalIgnoreCase)
    if ($InRepo -and -not (Test-ExploreGitIgnored -Path $Full -RepoRoot $Root)) { throw "-OutDirectory '$Full' leży w repo ('$Root') poza katalogiem ignorowanym przez git; wyniki pomiaru trzymaj poza repo albo w katalogu ignorowanym przez git." }
    return $Full
}

function Copy-ExploreRawFiles {
    # Surowe pliki z katalogu VM (bez podkatalogów) do katalogu hosta, kawałkami po 4 MB jako Base64 (Copy-Item -FromSession zgłaszał OOM
    # na PowerShell Direct). Zwraca pełne ścieżki skopiowanych plików na hoście.
    [CmdletBinding()]
    param(
        [Parameter(Mandatory)][AllowNull()][System.Management.Automation.Runspaces.PSSession]$Session,
        [Parameter(Mandatory)][string]$Destination,
        [Parameter(Mandatory)][ValidatePattern('^[A-Za-z]:\\[^\s"]+\z')][string]$Source
    )
    Assert-DsoVmLiveAllowed
    $null = New-Item -ItemType Directory -Path $Destination -Force
    $Files = @(Invoke-Command -Session $Session -ArgumentList @($Source) -ScriptBlock { param([string]$From) @(Get-ChildItem -LiteralPath $From -File -ErrorAction SilentlyContinue | ForEach-Object { [pscustomobject]@{ Name = $_.Name; Length = $_.Length } }) })
    $Copied = @()
    foreach ($File in $Files) {
        $Target = Join-Path $Destination ([string]$File.Name)
        $Stream = [System.IO.File]::Create($Target)
        try {
            for ($Offset = 0L; $Offset -lt [long]$File.Length; $Offset += 4MB) {
                $Base64 = [string](Invoke-Command -Session $Session -ArgumentList @($Source, [string]$File.Name, $Offset) -ScriptBlock {
                        param([string]$From, [string]$Name, [long]$At)
                        $S = [System.IO.File]::Open((Join-Path $From $Name), 'Open', 'Read', 'ReadWrite')
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

function Complete-ExploreVmCleanup {
    # Koniec przebiegu skryptu eksploracji (kontrakt stanu końcowego): revert do checkpointu, odłączenie wskazanych dysków dodanych na czas
    # przebiegu (np. VHDX z wydaniem), odłączenie karty sieciowej i sprawdzenie: VM Off, karta bez przełącznika, żaden wskazany dysk
    # nie jest podłączony. Błąd jednego kroku nie przerywa kolejnych; wszystko trafia do Problems (Ok = brak problemów).
    [CmdletBinding()]
    param(
        [Parameter(Mandatory)][ValidateSet('DSO-26H2-Home', 'DSO-26H2-Pro')][string]$VmName,
        [Parameter(Mandatory)][string]$CheckpointName,
        [AllowEmptyCollection()][string[]]$DetachDiskPath = @()
    )
    $Paths = @($DetachDiskPath | Where-Object { -not [string]::IsNullOrWhiteSpace($_) })
    $Out = [ordered]@{ Ok = $false; RevertError = $null; DetachError = $null; DisconnectError = $null; CheckError = $null; FinalState = $null; FinalSwitch = $null
        AttachedDisks = @(); Violations = @(); Problems = @() }
    try { Restore-VmCleanState -VmName $VmName -CheckpointName $CheckpointName }
    catch { $Out.RevertError = $_.Exception.Message; $Out.Problems += "revert do '$CheckpointName': $($_.Exception.Message)" }
    if ($Paths.Count -gt 0) {
        $DetachErrors = @()
        try {
            foreach ($Drive in @(Get-VMHardDiskDrive -VMName $VmName -ErrorAction Stop | Where-Object { $null -ne $_ })) {
                if (@($Paths | Where-Object { $_ -ieq [string]$Drive.Path }).Count -eq 0) { continue }
                try { Remove-VMHardDiskDrive -VMHardDiskDrive $Drive -ErrorAction Stop }
                catch { $DetachErrors += "$($Drive.Path): $($_.Exception.Message)" }
            }
        }
        catch { $DetachErrors += "odczyt dysków: $($_.Exception.Message)" }
        if ($DetachErrors.Count -gt 0) { $Out.DetachError = $DetachErrors -join ' | '; $Out.Problems += "odłączenie dysku: $($Out.DetachError)" }
    }
    try { Disconnect-VMNetworkAdapter -VMName $VmName -ErrorAction Stop }
    catch { $Out.DisconnectError = $_.Exception.Message; $Out.Problems += "odłączenie sieci: $($_.Exception.Message)" }
    try {
        $Out.FinalState = [string](Get-VM -Name $VmName -ErrorAction Stop).State
        $Out.FinalSwitch = (@(Get-VMNetworkAdapter -VMName $VmName -ErrorAction Stop | ForEach-Object { $_.SwitchName } | Where-Object { $_ }) -join ', ')
        if ($Paths.Count -gt 0) {
            $Out.AttachedDisks = @(Get-VMHardDiskDrive -VMName $VmName -ErrorAction Stop | Where-Object { $null -ne $_ } | ForEach-Object { [string]$_.Path } |
                    Where-Object { $Current = $_; @($Paths | Where-Object { $_ -ieq $Current }).Count -gt 0 })
        }
        if ($Out.FinalState -ne 'Off') { $Out.Violations += "VM w stanie '$($Out.FinalState)' zamiast 'Off'" }
        if ($Out.FinalSwitch) { $Out.Violations += "karta podłączona do '$($Out.FinalSwitch)'" }
        foreach ($Disk in @($Out.AttachedDisks)) { $Out.Violations += "dysk '$Disk' nadal podłączony do VM" }
    }
    catch { $Out.CheckError = $_.Exception.Message; $Out.Problems += "odczyt stanu końcowego: $($_.Exception.Message)" }
    if (@($Out.Violations).Count -gt 0) { $Out.Problems += "stan końcowy: $(@($Out.Violations) -join '; ')" }
    $Out.Ok = (@($Out.Problems).Count -eq 0)
    return [pscustomobject]$Out
}

function Save-ExploreResult {
    [CmdletBinding()]
    [OutputType([string])]
    param(
        [Parameter(Mandatory)]$Result,
        [Parameter(Mandatory)][string]$OutDirectory,
        [Parameter(Mandatory)][ValidateSet('DSO-26H2-Home', 'DSO-26H2-Pro')][string]$VmName,
        [Parameter(Mandatory)][ValidatePattern('^[A-Za-z0-9-]+\.json\z')][string]$FileName
    )
    # Ścieżka względna liczona od bieżącej lokalizacji PowerShell ($PWD), bo katalog procesu .NET bywa inny; File.WriteAllText używa tego drugiego.
    $OutDirectory = Resolve-ExploreOutDirectory -Path $OutDirectory
    $Directory = Join-Path $OutDirectory $VmName
    $null = New-Item -ItemType Directory -Path $Directory -Force
    $Path = Join-Path $Directory $FileName
    $Json = $Result | ConvertTo-Json -Depth 30
    [System.IO.File]::WriteAllText($Path, $Json, (New-Object System.Text.UTF8Encoding($true)))
    return $Path
}

function Invoke-ExploreVmRun {
    # Jedna VM: revert -> start -> gotowość -> sesja -> zbieranie -> (zawsze) zatrzymanie i powrót do checkpointu. Zwraca ścieżkę JSON.
    # RemoteScript: definicje funkcji (ConvertTo-ExploreRemoteScript); EntryCommand: funkcja zbierająca zwracająca JSON;
    # PassUserSid: przekazuje jej -UserSid (SID konta 'dso' odczytany z VM).
    [CmdletBinding()]
    [OutputType([string])]
    param(
        [Parameter(Mandatory)][ValidateSet('DSO-26H2-Home', 'DSO-26H2-Pro')][string]$VmName,
        [Parameter(Mandatory)][string]$OutDirectory,
        [Parameter(Mandatory)][ValidatePattern('^[A-Za-z0-9-]+\.json\z')][string]$FileName,
        [Parameter(Mandatory)][string]$RemoteScript,
        [Parameter(Mandatory)][ValidatePattern('^[A-Za-z][A-Za-z0-9-]*\z')][string]$EntryCommand,
        [switch]$PassUserSid,
        [string]$CheckpointName = 'czysta-26H2',
        [int]$ReadyTimeoutSeconds = 1200,
        # Full: revert -> start -> zbieranie -> revert (domyślnie). Prepare: jak Full, ale VM ZOSTAJE URUCHOMIONA po sukcesie (revert tylko po błędzie).
        # Finish: bez revertu i startu na początku (VM już działa), zbieranie, ZAWSZE zatrzymanie i powrót do checkpointu.
        [ValidateSet('Full', 'Prepare', 'Finish')][string]$Mode = 'Full',
        # Opcjonalna walidacja wyniku (dostaje sparsowany dokument): rzuca, gdy wynik jest niekompletny. Wynik jest już zapisany; wyjątek uruchamia
        # revert (w Prepare VM nie zostaje w połowie skonfigurowana).
        [scriptblock]$Validate
    )
    Assert-DsoVmLiveAllowed
    # Finish: poświadczenia też w try, żeby brak poświadczeń nie zostawił VM uruchomionej bez revertu.
    $Credential = $null
    if ($Mode -ne 'Finish') { $Credential = Get-DsoVmCredential -VmName $VmName }
    $Succeeded = $false
    $Partial = $null
    $StartedAt = Get-Date
    $Session = $null
    $PrimaryError = $null
    $PrimaryException = $null
    try {
        if ($Mode -eq 'Finish') { $Credential = Get-DsoVmCredential -VmName $VmName }
        if ($Mode -ne 'Finish') {
            Restore-VmCleanState -VmName $VmName -CheckpointName $CheckpointName
            Start-VM -Name $VmName
        }
        # Finish nie wymaga interaktywnej sesji dso (użytkownik mógł się wylogować): wystarczy PowerShell Direct.
        $Session = Wait-VmReady -VmName $VmName -Credential $Credential -TimeoutSeconds $ReadyTimeoutSeconds -SkipInteractiveUserCheck:($Mode -eq 'Finish')
        $UserSid = ''
        if ($PassUserSid) {
            $UserSid = [string](Invoke-Command -Session $Session -ScriptBlock { (Get-LocalUser -Name 'dso').SID.Value })
            if ([string]::IsNullOrWhiteSpace($UserSid)) { throw "Nie udało się odczytać SID użytkownika 'dso' w VM '$VmName'." }
        }
        $Remote = {
            param([string]$Definitions, [string]$Entry, [string]$Sid)
            . ([scriptblock]::Create($Definitions))
            if ($Sid) { & $Entry -UserSid $Sid } else { & $Entry }
        }
        $Raw = @(Invoke-Command -Session $Session -ScriptBlock $Remote -ArgumentList @($RemoteScript, $EntryCommand, $UserSid))
        $JsonText = [string]($Raw | Where-Object { $_ -is [string] } | Select-Object -Last 1)
        if ([string]::IsNullOrWhiteSpace($JsonText)) { throw "VM '$VmName': zbieranie nie zwróciło JSON." }
        $Collected = $JsonText | ConvertFrom-Json
        $Partial = $Collected
        $Result = [ordered]@{
            schema     = 1
            vmName     = $VmName
            checkpoint = $CheckpointName
            startedAt  = $StartedAt.ToString('o')
            finishedAt = (Get-Date).ToString('o')
        }
        foreach ($Property in $Collected.PSObject.Properties) { $Result[$Property.Name] = $Property.Value }
        $SavedPath = Save-ExploreResult -Result $Result -OutDirectory $OutDirectory -VmName $VmName -FileName $FileName
        if ($null -ne $Validate) { & $Validate $Collected }
        $Succeeded = $true
        return $SavedPath
    }
    catch {
        $PrimaryError = $_.Exception.Message
        $PrimaryException = $_.Exception
        if ($Mode -eq 'Finish') {
            # Finish: przed revertem zapisz, co jest (błąd + ewentualnie częściowy wynik), żeby wynik nie przepadł.
            try {
                $Failure = [ordered]@{ schema = 1; vmName = $VmName; checkpoint = $CheckpointName; mode = 'Finish'; startedAt = $StartedAt.ToString('o'); finishedAt = (Get-Date).ToString('o'); error = $PrimaryError; partial = $Partial }
                $null = Save-ExploreResult -Result $Failure -OutDirectory $OutDirectory -VmName $VmName -FileName $FileName
            }
            catch { $null = $_ }
        }
        throw
    }
    finally {
        if ($null -ne $Session) { Remove-PSSession -Session $Session -ErrorAction SilentlyContinue }
        $KeepRunning = ($Mode -eq 'Prepare' -and $Succeeded)
        try { if (-not $KeepRunning) { Restore-VmCleanState -VmName $VmName -CheckpointName $CheckpointName } }
        catch {
            $RestoreMessage = "Przywracanie checkpointu '$CheckpointName' VM '$VmName' nie powiodło się: $($_.Exception.Message). VM może nie być w czystym stanie."
            if ($null -ne $PrimaryError) {
                $Combined = "$PrimaryError | $RestoreMessage"
                if (Test-VmAuthFailure -Exception $PrimaryException) {
                    # Spłaszczenie do tekstu gubiłoby flagę: znacznik w komunikacie + flaga, by Invoke-ExploreAll nadal zatrzymał całość.
                    $AuthFailure = [System.InvalidOperationException]::new("po wyczerpaniu prób logowania: $Combined")
                    $AuthFailure.Data['VmAuthFailure'] = $true
                    throw $AuthFailure
                }
                throw $Combined
            }
            throw $RestoreMessage
        }
    }
}

function Invoke-ExploreAll {
    # Wszystkie wybrane VM po kolei. Błąd uwierzytelnienia (Test-VmAuthFailure) = stop całości; inny błąd = zapis i dalej.
    [CmdletBinding()]
    param(
        [Parameter(Mandatory)][ValidateSet('DSO-26H2-Home', 'DSO-26H2-Pro')][string[]]$VmName,
        [Parameter(Mandatory)][string]$OutDirectory,
        [Parameter(Mandatory)][ValidatePattern('^[A-Za-z0-9-]+\.json\z')][string]$FileName,
        [Parameter(Mandatory)][string]$RemoteScript,
        [Parameter(Mandatory)][string]$EntryCommand,
        [switch]$PassUserSid,
        [string]$CheckpointName = 'czysta-26H2',
        [int]$ReadyTimeoutSeconds = 1200
    )
    $Paths = New-Object System.Collections.Generic.List[string]
    $Errors = New-Object System.Collections.Generic.List[string]
    foreach ($Name in $VmName) {
        try {
            $Paths.Add((Invoke-ExploreVmRun -VmName $Name -OutDirectory $OutDirectory -FileName $FileName -RemoteScript $RemoteScript -EntryCommand $EntryCommand `
                        -PassUserSid:$PassUserSid -CheckpointName $CheckpointName -ReadyTimeoutSeconds $ReadyTimeoutSeconds))
        }
        catch {
            if (Test-VmAuthFailure -Exception $_.Exception) { throw }
            $Errors.Add("VM '$Name': $($_.Exception.Message)")
        }
    }
    return [pscustomobject]@{ Paths = $Paths.ToArray(); Errors = $Errors.ToArray() }
}

Export-ModuleMember -Function Test-ExploreGitIgnored, Resolve-ExploreOutDirectory, Get-ExploreCommonRemoteFunctionNames, Get-ExploreRegistryData, Open-ExploreRegistryKey, Read-ExploreRegistryValue, Read-ExploreRegistryKey,
    Read-ExploreRegistryTree, Invoke-ExploreNative, Invoke-ExploreStep, Compare-ExploreRegistry, Get-ExploreMeta, Invoke-ExploreCollection,
    ConvertTo-ExploreRemoteScript, Save-ExploreResult, Invoke-ExploreVmRun, Invoke-ExploreAll, Assert-ExploreOutDirectory, Copy-ExploreRawFiles, Complete-ExploreVmCleanup
