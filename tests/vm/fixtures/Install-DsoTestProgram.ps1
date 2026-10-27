<#
Instrukcja uruchomienia:
    Tylko w VM DSO-26H2-Home/Pro, jako Administrator (Setup przypadków Flow='AppRemove' w tests\vm\cases.psd1), np.:
        & C:\dso\fixtures\Install-DsoTestProgram.ps1 -Mode quiet -KeyName DsoTestQuiet
        & C:\dso\fixtures\Install-DsoTestProgram.ps1 -Mode deferred -KeyName DsoTestDeferred -MarkerPath C:\dso\out\DsoTestDeferred.survived
        & C:\dso\fixtures\Install-DsoTestProgram.ps1 -Mode quiet -KeyName DsoTestUser -UserSid S-1-5-21-...
Opis działania:
    Program testowy Win32 do usuwania przez DSOptimizer. Gdy brak C:\dso\fixtures\DsoTestUninstaller.exe, kompiluje go z
    DsoTestUninstaller.cs przez csc.exe z .NET Framework 4 (bez sieci). Kopiuje exe do C:\DsoTest\<KeyName>\ (ścieżka bez spacji)
    i zakłada wpis Uninstall <KeyName> (HKLM widok 64-bit albo, z -UserSid, HKU\<SID> - program per użytkownik) z DisplayName,
    Publisher, DisplayVersion, QuietUninstallString i UninstallString = "<exe>" <Mode> <KeyName> [<MarkerPath>].
    Nazwa klucza bez spacji (Ruling R6), więc id aplikacji (win32:<ścieżka klucza>) przechodzi przez Start-Process w VM.
    Odmawia poza VM (nazwa komputera), bo zapisuje HKLM.
#>
[CmdletBinding()]
param(
    [Parameter(Mandatory)][ValidateSet('quiet', 'exit3010', 'deferred')][string]$Mode,
    [Parameter(Mandatory)][ValidatePattern('^DsoTest[A-Za-z0-9]+\z')][string]$KeyName,
    [ValidatePattern('^S-1-5-21-[0-9-]+\z')][string]$UserSid,
    [ValidatePattern('^[A-Za-z]:\\[^\s]+\z')][string]$MarkerPath,
    [string]$FixturesDirectory = 'C:\dso\fixtures'
)
Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

if (@('DSO-26H2-HOME', 'DSO-26H2-PRO') -notcontains ([string]$env:COMPUTERNAME).ToUpperInvariant()) {
    throw "Install-DsoTestProgram działa tylko w VM DSO-26H2-Home/Pro (komputer: '$env:COMPUTERNAME'); nic nie zostało zmienione."
}

$Exe = Join-Path $FixturesDirectory 'DsoTestUninstaller.exe'
if (-not (Test-Path -LiteralPath $Exe -PathType Leaf)) {
    $Csc = Join-Path $env:WINDIR 'Microsoft.NET\Framework64\v4.0.30319\csc.exe'
    $Source = Join-Path $FixturesDirectory 'DsoTestUninstaller.cs'
    $Output = & $Csc /nologo /target:exe "/out:$Exe" $Source 2>&1
    if ($LASTEXITCODE -ne 0 -or -not (Test-Path -LiteralPath $Exe -PathType Leaf)) {
        throw "csc.exe nie zbudował deinstalatora testowego (kod $LASTEXITCODE): $($Output -join ' ')"
    }
}

$InstallDirectory = Join-Path 'C:\DsoTest' $KeyName
$null = New-Item -ItemType Directory -Path $InstallDirectory -Force
$Target = Join-Path $InstallDirectory 'DsoTestUninstaller.exe'
Copy-Item -LiteralPath $Exe -Destination $Target -Force

$Command = '"{0}" {1} {2}' -f $Target, $Mode, $KeyName
if ($MarkerPath) { $Command += " $MarkerPath" }

$Hive = if ($UserSid) { [Microsoft.Win32.RegistryHive]::Users } else { [Microsoft.Win32.RegistryHive]::LocalMachine }
$SubKey = if ($UserSid) { "$UserSid\Software\Microsoft\Windows\CurrentVersion\Uninstall\$KeyName" } else { "SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall\$KeyName" }
$Base = [Microsoft.Win32.RegistryKey]::OpenBaseKey($Hive, [Microsoft.Win32.RegistryView]::Registry64)
try {
    $Key = $Base.CreateSubKey($SubKey)
    try {
        $Key.SetValue('DisplayName', "DSO Test $KeyName")
        $Key.SetValue('Publisher', 'DSOptimizer VM Tests')
        $Key.SetValue('DisplayVersion', '1.0.0')
        $Key.SetValue('InstallLocation', $InstallDirectory)
        $Key.SetValue('QuietUninstallString', $Command)
        $Key.SetValue('UninstallString', $Command)
    }
    finally { $Key.Dispose() }
}
finally { $Base.Dispose() }
