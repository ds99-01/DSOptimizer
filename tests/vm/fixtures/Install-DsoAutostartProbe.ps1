<#
Instrukcja uruchomienia:
    Tylko w VM DSO-26H2-Home/Pro, jako Administrator (Setup przypadków Flow='Autostart' w tests\vm\cases.psd1), np.:
        Set-ExecutionPolicy -Scope Process -ExecutionPolicy Bypass -Force
        & C:\dso\fixtures\Install-DsoAutostartProbe.ps1 -Name DsoRunUser -Source RunUser -UserSid S-1-5-21-...
        & C:\dso\fixtures\Install-DsoAutostartProbe.ps1 -Name DsoLnkCommon -Source FolderCommon
        & C:\dso\fixtures\Install-DsoAutostartProbe.ps1 -Name DsoMissing -Source RunUser -Missing
        & C:\dso\fixtures\Install-DsoAutostartProbe.ps1 -Name DsoLnkUser -Source FolderUser -StartupApproved disabled
Opis działania:
    Sonda autostartu: gdy brak C:\dsotest\DsoAutostartProbe.exe, kompiluje go raz z DsoAutostartProbe.cs przez csc.exe z .NET
    Framework 4 (bez sieci), kopiuje do C:\dsotest\<Name>.exe i zakłada pozycję autostartu:
      RunUser      = wartość <Name> w HKU\<SID>\Software\Microsoft\Windows\CurrentVersion\Run
      RunMachine   = wartość <Name> w HKLM\SOFTWARE\Microsoft\Windows\CurrentVersion\Run
      Run32        = wartość <Name> w HKLM\SOFTWARE\WOW6432Node\Microsoft\Windows\CurrentVersion\Run
      FolderUser   = skrót <Name>.lnk w folderze Autostart konta (profil z ProfileList)
      FolderCommon = skrót <Name>.lnk we wspólnym folderze Autostart (%ProgramData%)
    Wartość Run = "C:\dsotest\<Name>.exe" (w cudzysłowie), skrót przez COM WScript.Shell. -Missing = wpis na nieistniejący plik
    (exe nie powstaje, istniejący jest usuwany). -StartupApproved = wartość w StartupApproved jak po Menedżerze zadań: enabled =
    02 + 11 x 00, disabled = 03 00 00 00 + FILETIME UTC teraz; zapisane bajty trafiają też do C:\dsotest\<Name>.startupapproved.txt
    (format BitConverter, np. 03-00-00-00-...) dla Check przypadku. Bez -StartupApproved nic w StartupApproved nie powstaje.
    Pozycje konta bez -UserSid dostają SID konta 'dso'. Wypisuje jedną linię opisu. Odmawia poza VM (nazwa komputera).
#>
[CmdletBinding()]
param(
    [Parameter(Mandatory)][ValidatePattern('^Dso[A-Za-z0-9]+\z')][string]$Name,
    [Parameter(Mandatory)][ValidateSet('RunUser', 'RunMachine', 'Run32', 'FolderUser', 'FolderCommon')][string]$Source,
    [ValidatePattern('^S-1-5-21-[0-9-]+\z')][string]$UserSid,
    [switch]$Missing,
    [ValidateSet('enabled', 'disabled')][string]$StartupApproved,
    [string]$FixturesDirectory = 'C:\dso\fixtures'
)
Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

if (@('DSO-26H2-HOME', 'DSO-26H2-PRO') -notcontains ([string]$env:COMPUTERNAME).ToUpperInvariant()) {
    throw "Install-DsoAutostartProbe działa tylko w VM DSO-26H2-Home/Pro (komputer: '$env:COMPUTERNAME'); nic nie zostało zmienione."
}

$ProbeDirectory = 'C:\dsotest'
$null = New-Item -ItemType Directory -Path $ProbeDirectory -Force
$UserScoped = $Source -in @('RunUser', 'FolderUser')
if ($UserScoped -and -not $UserSid) { $UserSid = (Get-LocalUser -Name 'dso').SID.Value }

$Target = Join-Path $ProbeDirectory "$Name.exe"
if ($Missing) {
    if (Test-Path -LiteralPath $Target) { Remove-Item -LiteralPath $Target -Force }
}
else {
    $Built = Join-Path $ProbeDirectory 'DsoAutostartProbe.exe'
    if (-not (Test-Path -LiteralPath $Built -PathType Leaf)) {
        $Csc = Join-Path $env:WINDIR 'Microsoft.NET\Framework64\v4.0.30319\csc.exe'
        $Code = Join-Path $FixturesDirectory 'DsoAutostartProbe.cs'
        $Output = & $Csc /nologo /target:winexe "/out:$Built" $Code 2>&1
        if ($LASTEXITCODE -ne 0 -or -not (Test-Path -LiteralPath $Built -PathType Leaf)) {
            throw "csc.exe nie zbudował sondy autostartu (kod $LASTEXITCODE): $($Output -join ' ')"
        }
    }
    Copy-Item -LiteralPath $Built -Destination $Target -Force
}

$RunSubKey = 'Software\Microsoft\Windows\CurrentVersion\Run'
$ApprovedSubKey = 'Software\Microsoft\Windows\CurrentVersion\Explorer\StartupApproved'
$Users = [Microsoft.Win32.RegistryHive]::Users
$Machine = [Microsoft.Win32.RegistryHive]::LocalMachine
$Entry = switch ($Source) {
    'RunUser' { @{ Hive = $Users; SubKey = "$UserSid\$RunSubKey"; ApprovedHive = $Users; ApprovedKey = "$UserSid\$ApprovedSubKey\Run"; ApprovedName = $Name } }
    'RunMachine' { @{ Hive = $Machine; SubKey = "SOFTWARE\Microsoft\Windows\CurrentVersion\Run"; ApprovedHive = $Machine; ApprovedKey = "$ApprovedSubKey\Run"; ApprovedName = $Name } }
    'Run32' { @{ Hive = $Machine; SubKey = 'SOFTWARE\WOW6432Node\Microsoft\Windows\CurrentVersion\Run'; ApprovedHive = $Machine; ApprovedKey = "$ApprovedSubKey\Run32"; ApprovedName = $Name } }
    'FolderUser' { @{ ApprovedHive = $Users; ApprovedKey = "$UserSid\$ApprovedSubKey\StartupFolder"; ApprovedName = "$Name.lnk" } }
    'FolderCommon' { @{ ApprovedHive = $Machine; ApprovedKey = "$ApprovedSubKey\StartupFolder"; ApprovedName = "$Name.lnk" } }
}

function Set-DsoRegistryValue {
    # Zapis przez .NET (widok 64-bit) jak w programie: brakujące klucze tworzy CreateSubKey.
    param([Microsoft.Win32.RegistryHive]$Hive, [string]$SubKey, [string]$ValueName, $Value, [Microsoft.Win32.RegistryValueKind]$Kind)
    $Base = [Microsoft.Win32.RegistryKey]::OpenBaseKey($Hive, [Microsoft.Win32.RegistryView]::Registry64)
    try {
        $Key = $Base.CreateSubKey($SubKey)
        try { $Key.SetValue($ValueName, $Value, $Kind) } finally { $Key.Dispose() }
    }
    finally { $Base.Dispose() }
}

$Location = $null
if ($Source -in @('FolderUser', 'FolderCommon')) {
    if ($Source -eq 'FolderUser') {
        $ProfilePath = [string](Get-ItemProperty -LiteralPath "Registry::HKEY_LOCAL_MACHINE\SOFTWARE\Microsoft\Windows NT\CurrentVersion\ProfileList\$UserSid").ProfileImagePath
        if ([string]::IsNullOrWhiteSpace($ProfilePath)) { throw "Brak profilu konta $UserSid w ProfileList." }
        $Folder = Join-Path $ProfilePath 'AppData\Roaming\Microsoft\Windows\Start Menu\Programs\Startup'
    }
    else {
        $Folder = Join-Path $env:ProgramData 'Microsoft\Windows\Start Menu\Programs\StartUp'
    }
    $null = New-Item -ItemType Directory -Path $Folder -Force
    $Location = Join-Path $Folder "$Name.lnk"
    $Shell = New-Object -ComObject WScript.Shell
    try {
        $Link = $Shell.CreateShortcut($Location)
        $Link.TargetPath = $Target
        $Link.Save()
    }
    finally { [void][System.Runtime.InteropServices.Marshal]::ReleaseComObject($Shell) }
}
else {
    Set-DsoRegistryValue -Hive $Entry.Hive -SubKey $Entry.SubKey -ValueName $Name -Value ('"{0}"' -f $Target) -Kind ([Microsoft.Win32.RegistryValueKind]::String)
    $Location = "$($Entry.Hive)\$($Entry.SubKey)\$Name"
}

$ApprovedText = 'bez wartości'
if ($StartupApproved) {
    $Bytes = New-Object byte[] 12
    if ($StartupApproved -eq 'enabled') {
        $Bytes[0] = 0x02
    }
    else {
        $Bytes[0] = 0x03
        [Array]::Copy([BitConverter]::GetBytes([DateTime]::UtcNow.ToFileTimeUtc()), 0, $Bytes, 4, 8)
    }
    Set-DsoRegistryValue -Hive $Entry.ApprovedHive -SubKey $Entry.ApprovedKey -ValueName $Entry.ApprovedName -Value $Bytes -Kind ([Microsoft.Win32.RegistryValueKind]::Binary)
    $ApprovedText = [BitConverter]::ToString($Bytes)
    [System.IO.File]::WriteAllText((Join-Path $ProbeDirectory "$Name.startupapproved.txt"), $ApprovedText, (New-Object System.Text.UTF8Encoding($false)))
}

$FileText = if ($Missing) { 'plik nie istnieje' } else { 'plik istnieje' }
Write-Output "$Name ($Source): $Location -> $Target ($FileText); StartupApproved: $ApprovedText"
