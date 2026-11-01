<#
Instrukcja uruchomienia (W VM, jako administrator):
    powershell -NoProfile -ExecutionPolicy Bypass -File C:\dso\net\Parse-NetTrace.ps1 -EtlPath C:\dso\net\s1.etl -SnapshotPath C:\dso\net\s1-procs-start.json -OutPath C:\dso\net\s1.json
Opis działania:
    Czyta plik ETL sesji logman z dostawcami Microsoft-Windows-Kernel-Network (PID właściciela połączenia w polu zdarzenia),
    Microsoft-Windows-Kernel-Process (start/stop procesów z PID rodzica) i Microsoft-Windows-DNS-Client (zapytania DNS z PID wołającego
    w nagłówku zdarzenia). Buduje instancje procesów (PID + czas startu), drzewo potomków DSOptimizer.exe i agreguje połączenia
    (proces, protokół, adres zdalny, port) oraz zapytania DNS. Wynik JSON: wszystkie procesy (do odróżnienia ruchu Windows)
    i osobno drzewo DSOptimizer.exe.
#>
param(
    [Parameter(Mandatory)][string]$EtlPath,
    [Parameter(Mandatory)][string]$SnapshotPath,
    [Parameter(Mandatory)][string]$OutPath,
    [string]$RootImage = 'DSOptimizer.exe'
)
$ErrorActionPreference = 'Stop'

function ConvertTo-IpText($Value) {
    if ($null -eq $Value) { return '' }
    if ($Value -is [byte[]]) { return ([System.Net.IPAddress]::new($Value)).ToString() }
    if ($Value -is [uint32]) { return ([System.Net.IPAddress]::new([BitConverter]::GetBytes($Value))).ToString() }
    return [string]$Value
}
function ConvertTo-Port($Value) { $V = [int]$Value; return (($V -band 0xFF) -shl 8) -bor (($V -shr 8) -band 0xFF) }

$LocalIps = @{}
foreach ($Ip in @(Get-NetIPAddress | ForEach-Object { ($_.IPAddress -split '%')[0] })) { $LocalIps[$Ip] = $true }
$LocalIps['127.0.0.1'] = $true; $LocalIps['::1'] = $true; $LocalIps['0.0.0.0'] = $true; $LocalIps['::'] = $true

# Procesy działające przed startem śladu (zrzut Win32_Process).
$SnapshotRaw = Get-Content -LiteralPath $SnapshotPath -Raw -Encoding UTF8 | ConvertFrom-Json
$Snapshot = @($SnapshotRaw | ForEach-Object { $_ })
$Instances = New-Object System.Collections.Generic.List[object]
$ByPid = @{}
function Add-Instance($Inst) {
    $Instances.Add($Inst)
    if (-not $ByPid.ContainsKey($Inst.Pid)) { $ByPid[$Inst.Pid] = New-Object System.Collections.Generic.List[object] }
    $ByPid[$Inst.Pid].Add($Inst)
}
foreach ($P in $Snapshot) {
    Add-Instance ([pscustomobject]@{ Pid = [int]$P.ProcessId; Ppid = [int]$P.ParentProcessId; Image = [string]$P.Name; Start = [datetime]::MinValue; Stop = $null; PreExisting = $true; Target = $false; ParentInst = $null; CommandLine = [string]$P.CommandLine })
}

$Events = Get-WinEvent -Path $EtlPath -Oldest -ErrorAction Stop
$ProviderCounts = @{}
$NetEvents = New-Object System.Collections.Generic.List[object]
$DnsEvents = New-Object System.Collections.Generic.List[object]
$First = $null; $Last = $null
foreach ($E in $Events) {
    $Key = "$($E.ProviderName)/$($E.Id)"
    if ($ProviderCounts.ContainsKey($Key)) { $ProviderCounts[$Key]++ } else { $ProviderCounts[$Key] = 1 }
    $T = $E.TimeCreated
    if ($null -eq $First) { $First = $T }
    $Last = $T
    switch ($E.ProviderName) {
        'Microsoft-Windows-Kernel-Process' {
            $Pr = $E.Properties
            if ($E.Id -eq 1) {
                $Image = [System.IO.Path]::GetFileName([string]$Pr[10].Value)
                Add-Instance ([pscustomobject]@{ Pid = [int]$Pr[0].Value; Ppid = [int]$Pr[3].Value; Image = $Image; Start = $T; Stop = $null; PreExisting = $false; Target = $false; ParentInst = $null; CommandLine = $null; ImagePath = [string]$Pr[10].Value })
            }
            elseif ($E.Id -eq 2) {
                $StopPid = [int]$Pr[0].Value
                $Inst = $null
                if ($ByPid.ContainsKey($StopPid)) { $Inst = $ByPid[$StopPid] | Where-Object { $null -eq $_.Stop -and $_.Start -le $T } | Select-Object -Last 1 }
                if ($null -ne $Inst) { $Inst.Stop = $T }
            }
        }
        'Microsoft-Windows-Kernel-Network' {
            $Pr = $E.Properties
            if ($Pr.Count -lt 6) { continue }
            $NetEvents.Add([pscustomobject]@{ Id = $E.Id; T = $T; Pid = [int]$Pr[0].Value; Size = [int64]$Pr[1].Value; D = (ConvertTo-IpText $Pr[2].Value); S = (ConvertTo-IpText $Pr[3].Value); DPort = (ConvertTo-Port $Pr[4].Value); SPort = (ConvertTo-Port $Pr[5].Value) })
        }
        'Microsoft-Windows-DNS-Client' {
            if ($E.Id -in 3006, 3008) {
                $Pr = $E.Properties
                $Results = if ($E.Id -eq 3008 -and $Pr.Count -gt 4) { [string]$Pr[4].Value } else { $null }
                $Status = if ($E.Id -eq 3008 -and $Pr.Count -gt 3) { [int64]$Pr[3].Value } else { $null }
                $DnsEvents.Add([pscustomobject]@{ Id = $E.Id; T = $T; Pid = [int]$E.ProcessId; Name = [string]$Pr[0].Value; Type = [int]$Pr[1].Value; Status = $Status; Results = $Results })
            }
        }
    }
}

function Find-Instance([int]$ProcId, [datetime]$At) {
    $Best = $null
    if (-not $ByPid.ContainsKey($ProcId)) { return $null }
    foreach ($I in $ByPid[$ProcId]) {
        if ($I.Start -gt $At.AddMilliseconds(5)) { continue }
        if ($null -ne $I.Stop -and $I.Stop -lt $At.AddSeconds(-2)) { continue }
        if ($null -eq $Best -or $I.Start -ge $Best.Start) { $Best = $I }
    }
    return $Best
}

# Rodzic każdej instancji i drzewo DSOptimizer.exe (kolejność startu = rodzic oceniony przed dzieckiem).
$Ordered = @($Instances | Sort-Object Start)
foreach ($I in $Ordered) {
    if (-not $I.PreExisting) { $I.ParentInst = Find-Instance -ProcId $I.Ppid -At $I.Start }
    if ($I.Image -ieq $RootImage) { $I.Target = $true }
    elseif ($null -ne $I.ParentInst -and $I.ParentInst.Target) { $I.Target = $true }
}

function Get-InstanceLabel($I) {
    if ($null -eq $I) { return $null }
    $Chain = @()
    $Cur = $I
    $Guard = 0
    while ($null -ne $Cur -and $Guard -lt 12) { $Chain += "$($Cur.Image)($($Cur.Pid))"; $Cur = $Cur.ParentInst; $Guard++ }
    return ($Chain -join ' <- ')
}

# Nazwy hostów z wyników DNS (wszystkie procesy): IP -> nazwy zapytań i CNAME.
$IpNames = @{}
foreach ($D in $DnsEvents) {
    if ($D.Id -ne 3008 -or [string]::IsNullOrEmpty($D.Results)) { continue }
    $Names = @($D.Name)
    foreach ($Part in ($D.Results -split ';')) {
        $Item = $Part.Trim()
        if (-not $Item) { continue }
        if ($Item -like 'type:*') { $Names += (($Item -split '\s+')[-1]); continue }
        $Ip = $Item -replace '^::ffff:', ''
        if (-not $IpNames.ContainsKey($Ip)) { $IpNames[$Ip] = New-Object System.Collections.Generic.HashSet[string] }
        foreach ($N in $Names) { [void]$IpNames[$Ip].Add($N) }
    }
}

# Agregacja połączeń: instancja procesu + protokół + adres zdalny + port zdalny.
$Agg = @{}
foreach ($N in $NetEvents) {
    $Proto = if ($N.Id -lt 42) { 'TCP' } else { 'UDP' }
    if ($LocalIps.ContainsKey($N.D) -and -not $LocalIps.ContainsKey($N.S)) { $Remote = $N.S; $RPort = $N.SPort; $Local = $N.D; $LPort = $N.DPort }
    else { $Remote = $N.D; $RPort = $N.DPort; $Local = $N.S; $LPort = $N.SPort }
    $Inst = Find-Instance -ProcId $N.Pid -At $N.T
    $InstKey = if ($null -ne $Inst) { "$($Inst.Pid)@$($Inst.Start.Ticks)" } else { "$($N.Pid)@?" }
    $K = "$InstKey|$Proto|$Remote|$RPort"
    if (-not $Agg.ContainsKey($K)) {
        $Agg[$K] = [pscustomobject]@{ Pid = $N.Pid; Image = $(if ($Inst) { $Inst.Image } else { '?' }); Chain = (Get-InstanceLabel $Inst); Target = $(if ($Inst) { $Inst.Target } else { $false })
            Proto = $Proto; Remote = $Remote; RemotePort = $RPort; Hosts = $null; LocalPorts = New-Object System.Collections.Generic.HashSet[int]; Events = 0; Connects = 0; BytesOut = [int64]0; BytesIn = [int64]0
            First = $N.T; Last = $N.T; EventIds = New-Object System.Collections.Generic.HashSet[int] }
    }
    $A = $Agg[$K]
    $A.Events++; [void]$A.EventIds.Add($N.Id); [void]$A.LocalPorts.Add($LPort); $A.Last = $N.T
    if ($N.Id -in 12, 28) { $A.Connects++ }
    if ($N.Id -in 10, 26, 42, 58) { $A.BytesOut += $N.Size }
    if ($N.Id -in 11, 27, 43, 59) { $A.BytesIn += $N.Size }
}
$Connections = @(foreach ($A in $Agg.Values) {
        $HostList = if ($IpNames.ContainsKey($A.Remote)) { @($IpNames[$A.Remote]) } else { @() }
        [ordered]@{ pid = $A.Pid; image = $A.Image; chain = $A.Chain; dsoTree = [bool]$A.Target; proto = $A.Proto; remote = $A.Remote; remotePort = $A.RemotePort; hosts = $HostList
            localPorts = @($A.LocalPorts); events = $A.Events; connects = $A.Connects; bytesOut = $A.BytesOut; bytesIn = $A.BytesIn; first = $A.First.ToUniversalTime().ToString('o'); last = $A.Last.ToUniversalTime().ToString('o'); eventIds = @($A.EventIds | Sort-Object) }
    }) | Sort-Object { $_.first }

$DnsAgg = @{}
foreach ($D in $DnsEvents) {
    $Inst = Find-Instance -ProcId $D.Pid -At $D.T
    $K = "$($D.Pid)|$($D.Name)|$($D.Type)"
    if (-not $DnsAgg.ContainsKey($K)) {
        $DnsAgg[$K] = [ordered]@{ pid = $D.Pid; image = $(if ($Inst) { $Inst.Image } else { '?' }); chain = (Get-InstanceLabel $Inst); dsoTree = $(if ($Inst) { [bool]$Inst.Target } else { $false }); name = $D.Name; type = $D.Type
            queries = 0; completions = 0; statuses = @(); results = @(); first = $D.T.ToUniversalTime().ToString('o') }
    }
    $A = $DnsAgg[$K]
    if ($D.Id -eq 3006) { $A.queries++ } else { $A.completions++; if ($A.statuses -notcontains $D.Status) { $A.statuses += $D.Status }; if ($D.Results -and $A.results -notcontains $D.Results) { $A.results += $D.Results } }
}

$Procs = @(foreach ($I in $Ordered) {
        [ordered]@{ pid = $I.Pid; ppid = $I.Ppid; image = $I.Image; imagePath = $(if ($I.PSObject.Properties['ImagePath']) { $I.ImagePath } else { $null }); preExisting = $I.PreExisting; dsoTree = $I.Target
            start = $(if ($I.PreExisting) { $null } else { $I.Start.ToUniversalTime().ToString('o') }); stop = $(if ($I.Stop) { $I.Stop.ToUniversalTime().ToString('o') } else { $null }); chain = (Get-InstanceLabel $I); commandLine = $I.CommandLine }
    })

$Result = [ordered]@{
    etl = $EtlPath; eventCount = @($Events).Count; first = $(if ($First) { $First.ToUniversalTime().ToString('o') }); last = $(if ($Last) { $Last.ToUniversalTime().ToString('o') })
    providerCounts = $ProviderCounts; localIps = @($LocalIps.Keys)
    dsoTreeProcesses = @($Procs | Where-Object { $_.dsoTree })
    dsoTreeConnections = @($Connections | Where-Object { $_.dsoTree })
    dsoTreeDns = @($DnsAgg.Values | Where-Object { $_.dsoTree })
    processes = @($Procs | Where-Object { -not $_.preExisting })
    connections = $Connections
    dns = @($DnsAgg.Values)
}
$Result | ConvertTo-Json -Depth 8 | Set-Content -LiteralPath $OutPath -Encoding UTF8
"zapisano ${OutPath}: zdarzen $(@($Events).Count), polaczen $($Connections.Count), drzewo DSO: procesy $(@($Result.dsoTreeProcesses).Count), polaczenia $(@($Result.dsoTreeConnections).Count), DNS $(@($Result.dsoTreeDns).Count)"
