#Requires -Version 5.1
<#
Instrukcja uruchomienia:
    Tylko na runnerze GitHub Actions albo w VM DSO-26H2-Home/Pro, w 64-bitowym Windows PowerShell 5.1 jako Administrator
    i na czystej maszynie (bez zainstalowanego DSOptimizera):
        .\tools\release\Test-InstallerSmoke.ps1 -InstallerPath <DSOptimizer-X.Y.Z-setup.exe> -ZipPath <DSOptimizer-X.Y.Z.zip> -Version X.Y.Z -LogDirectory <katalog>
    Kody wyjścia: 0 = wszystko zaliczone, 1 = coś niezaliczone, 2 = odmowa startu (zła albo nieczysta maszyna, nic nie zmieniono).

Opis działania:
    Naprawdę instaluje, aktualizuje, odmawia i odinstalowuje instalator Inno. Na hoście odmawia kodem 2, zanim cokolwiek
    zmieni. Kroki: dir-refusal (odmowa innego folderu), install (pliki równe zipowi, wpis w "Zainstalowanych aplikacjach",
    skróty, brak %ProgramData%\DSOptimizer), upgrade (ten sam instalator ponownie: ikona z pulpitu nie wraca, jeden
    deinstalator), running (działający program = mutex: odmowa instalatora i deinstalatora, nic się nie zmienia),
    uninstall (znika program, skróty i wpis, zostaje %ProgramData%\DSOptimizer). Wynik: <LogDirectory>\smoke-result.json
    (schema 1, version, passed, checks), logi Inno: <LogDirectory>\<krok>.log. Przy niezaliczonym kroku wypisuje
    ostatnie 40 linii jego logu.
    Przy dot-source (testy) tylko definicje funkcji i stałych.
#>
[CmdletBinding()]
param(
    [Parameter(Mandatory)][string]$InstallerPath,
    [Parameter(Mandatory)][string]$ZipPath,
    [Parameter(Mandatory)][ValidatePattern('^\d+\.\d+\.\d+\z')][string]$Version,
    [Parameter(Mandatory)][string]$LogDirectory
)

$SmokeAppId = '{82DBAF33-36AA-4F12-A327-EEC64159D8E8}'
$SmokeMutexName = 'Global\DSOptimizer.SingleInstance'
$SmokeUninstallSubKey = 'SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall\' + $SmokeAppId + '_is1'
$SmokeVmNames = @('DSO-26H2-HOME', 'DSO-26H2-PRO')

function Test-SmokeMachineAllowed {
    [CmdletBinding()]
    [OutputType([bool])]
    param(
        [AllowEmptyString()][AllowNull()][string]$GitHubActions,
        [AllowEmptyString()][AllowNull()][string]$ComputerName
    )
    if ($GitHubActions -ceq 'true') { return $true }
    return ($SmokeVmNames -contains ([string]$ComputerName).ToUpperInvariant())
}

function Compare-SmokeFiles {
    [CmdletBinding()]
    [OutputType([string[]])]
    param(
        [Parameter(Mandatory)][hashtable]$Expected,
        [Parameter(Mandatory)][hashtable]$Actual
    )
    $Differences = New-Object System.Collections.Generic.List[string]
    foreach ($Key in ($Expected.Keys | Sort-Object)) {
        if (-not $Actual.ContainsKey($Key)) {
            $Differences.Add("brak pliku: $Key")
        }
        elseif ([string]$Actual[$Key] -ne [string]$Expected[$Key]) {
            $Differences.Add("inna suma SHA256: $Key")
        }
    }
    foreach ($Key in ($Actual.Keys | Sort-Object)) {
        if (-not $Expected.ContainsKey($Key)) { $Differences.Add("nadmiarowy plik: $Key") }
    }
    return $Differences.ToArray()
}

function Get-SmokeZipHashes {
    param([Parameter(Mandatory)][string]$Path)
    Add-Type -AssemblyName System.IO.Compression.FileSystem
    $Result = @{}
    $Zip = [System.IO.Compression.ZipFile]::OpenRead($Path)
    try {
        foreach ($Entry in $Zip.Entries) {
            if ($Entry.FullName.EndsWith('/')) { continue }
            $Relative = $Entry.FullName
            if ($Relative.StartsWith('DSOptimizer/')) { $Relative = $Relative.Substring('DSOptimizer/'.Length) }
            $Stream = $Entry.Open()
            try {
                $Sha = [System.Security.Cryptography.SHA256]::Create()
                try { $Hash = [System.BitConverter]::ToString($Sha.ComputeHash($Stream)).Replace('-', '') }
                finally { $Sha.Dispose() }
            }
            finally { $Stream.Dispose() }
            $Result[$Relative.Replace('/', '\')] = $Hash
        }
    }
    finally { $Zip.Dispose() }
    return $Result
}

function Get-SmokeDirectoryHashes {
    param([Parameter(Mandatory)][string]$Path)
    $Result = @{}
    if (-not (Test-Path -LiteralPath $Path -PathType Container)) { return $Result }
    $Prefix = (Resolve-Path -LiteralPath $Path).ProviderPath.TrimEnd('\') + '\'
    foreach ($File in Get-ChildItem -LiteralPath $Path -Recurse -Force -File) {
        $Result[$File.FullName.Substring($Prefix.Length)] = (Get-FileHash -LiteralPath $File.FullName -Algorithm SHA256).Hash
    }
    return $Result
}

function Get-SmokeUninstallValues {
    # Widok 64-bit rejestru (Inno w trybie 64-bit pisze tam); $null, gdy wpisu nie ma.
    $Base = [Microsoft.Win32.RegistryKey]::OpenBaseKey([Microsoft.Win32.RegistryHive]::LocalMachine, [Microsoft.Win32.RegistryView]::Registry64)
    try {
        $Key = $Base.OpenSubKey($SmokeUninstallSubKey)
        if ($null -eq $Key) { return $null }
        try {
            $Values = @{}
            foreach ($Name in $Key.GetValueNames()) { $Values[$Name] = $Key.GetValue($Name) }
            return $Values
        }
        finally { $Key.Dispose() }
    }
    finally { $Base.Dispose() }
}

function Invoke-SmokeProcess {
    param(
        [Parameter(Mandatory)][string]$FilePath,
        [Parameter(Mandatory)][string[]]$ArgumentList
    )
    $Process = Start-Process -FilePath $FilePath -ArgumentList $ArgumentList -Wait -PassThru
    return [int]$Process.ExitCode
}

function Invoke-SmokeRun {
    $Script:Checks = New-Object System.Collections.Generic.List[object]
    $AppDir = Join-Path -Path $env:ProgramFiles -ChildPath 'DSOptimizer'
    $DataDir = Join-Path -Path $env:ProgramData -ChildPath 'DSOptimizer'
    $StartMenuLink = Join-Path -Path ([Environment]::GetFolderPath('CommonPrograms')) -ChildPath 'DSOptimizer.lnk'
    $DesktopLink = Join-Path -Path ([Environment]::GetFolderPath('CommonDesktopDirectory')) -ChildPath 'DSOptimizer.lnk'
    $BadDir = Join-Path -Path $env:SystemDrive -ChildPath 'dso-zly-folder'
    $BaseArgs = @('/VERYSILENT', '/SUPPRESSMSGBOXES', '/NORESTART', '/SP-')

    function Add-Check {
        param([string]$Step, [string]$What, [bool]$Passed, [string]$Detail = '')
        $Script:Checks.Add([pscustomobject]@{ name = "${Step}: $What"; passed = $Passed; detail = $Detail })
        $Script:StepFailed = $Script:StepFailed -or (-not $Passed)
        $Mark = if ($Passed) { 'OK  ' } else { 'FAIL' }
        Write-Host "[$Mark] ${Step}: $What $Detail"
    }
    function Get-StepLog { param([string]$Step) Join-Path -Path $LogDirectory -ChildPath "$Step.log" }
    function Complete-Step {
        param([string]$Step)
        if ($Script:StepFailed) {
            $Log = Get-StepLog -Step $Step
            Write-Host "--- ostatnie 40 linii logu Inno kroku '$Step' ---"
            if (Test-Path -LiteralPath $Log) { Get-Content -LiteralPath $Log -Tail 40 | ForEach-Object { Write-Host $_ } }
            else { Write-Host '(brak logu)' }
        }
    }
    function Invoke-Step {
        param([string]$Step, [scriptblock]$Body)
        $Script:StepFailed = $false
        try { & $Body }
        catch { Add-Check -Step $Step -What 'wyjątek w kroku' -Passed $false -Detail $_.Exception.Message }
        Complete-Step -Step $Step
    }
    function Invoke-Setup {
        param([string]$Step, [string[]]$Extra = @())
        $All = $BaseArgs + @('/LOG="' + (Get-StepLog -Step $Step) + '"') + $Extra
        return Invoke-SmokeProcess -FilePath $InstallerPath -ArgumentList $All
    }
    function Invoke-Uninstaller {
        param([string]$Step)
        $Unins = Join-Path -Path $AppDir -ChildPath 'unins000.exe'
        return Invoke-SmokeProcess -FilePath $Unins -ArgumentList @('/VERYSILENT', '/SUPPRESSMSGBOXES', '/NORESTART', ('/LOG="' + (Get-StepLog -Step $Step) + '"'))
    }

    New-Item -ItemType Directory -Path $LogDirectory -Force | Out-Null
    $ExpectedFiles = Get-SmokeZipHashes -Path $ZipPath
    $ExpectedInstalled = @{}
    foreach ($Key in $ExpectedFiles.Keys) { $ExpectedInstalled[$Key] = $ExpectedFiles[$Key] }

    function Test-InstalledFiles {
        param([string]$Step)
        $Actual = Get-SmokeDirectoryHashes -Path $AppDir
        $Uninstall = @{}
        foreach ($Name in @('unins000.exe', 'unins000.dat')) {
            if ($Actual.ContainsKey($Name)) { $Uninstall[$Name] = $Actual[$Name]; $Actual.Remove($Name) }
        }
        $Differences = @(Compare-SmokeFiles -Expected $ExpectedInstalled -Actual $Actual)
        Add-Check -Step $Step -What 'pliki równe zipowi' -Passed ($Differences.Count -eq 0) -Detail ($Differences -join '; ')
        Add-Check -Step $Step -What 'unins000.exe i unins000.dat są' -Passed ($Uninstall.Count -eq 2) -Detail "znaleziono: $($Uninstall.Count)"
    }

    # 1. dir-refusal
    Invoke-Step -Step 'dir-refusal' -Body {
        $Code = Invoke-Setup -Step 'dir-refusal' -Extra @('/DIR="' + $BadDir + '"')
        Add-Check -Step 'dir-refusal' -What 'kod 7' -Passed ($Code -eq 7) -Detail "kod: $Code"
        Add-Check -Step 'dir-refusal' -What 'brak folderu zlego' -Passed (-not (Test-Path -LiteralPath $BadDir)) -Detail $BadDir
        Add-Check -Step 'dir-refusal' -What 'brak Program Files\DSOptimizer' -Passed (-not (Test-Path -LiteralPath $AppDir)) -Detail $AppDir
        Add-Check -Step 'dir-refusal' -What 'brak klucza Uninstall' -Passed ($null -eq (Get-SmokeUninstallValues))
    }

    # 2. install
    Invoke-Step -Step 'install' -Body {
        $DataExisted = Test-Path -LiteralPath $DataDir
        $Code = Invoke-Setup -Step 'install'
        Add-Check -Step 'install' -What 'kod 0' -Passed ($Code -eq 0) -Detail "kod: $Code"
        Test-InstalledFiles -Step 'install'
        $Values = Get-SmokeUninstallValues
        Add-Check -Step 'install' -What 'klucz Uninstall istnieje' -Passed ($null -ne $Values)
        if ($null -ne $Values) {
            Add-Check -Step 'install' -What 'DisplayName' -Passed ([string]$Values['DisplayName'] -ceq 'DSOptimizer') -Detail ([string]$Values['DisplayName'])
            Add-Check -Step 'install' -What 'DisplayVersion' -Passed ([string]$Values['DisplayVersion'] -ceq $Version) -Detail ([string]$Values['DisplayVersion'])
            Add-Check -Step 'install' -What 'Publisher' -Passed ([string]$Values['Publisher'] -ceq 'Dawid Świeboda') -Detail ([string]$Values['Publisher'])
            Add-Check -Step 'install' -What 'DisplayIcon' -Passed ([string]$Values['DisplayIcon'] -match '\\DSOptimizer\.exe(,0)?"?\z') -Detail ([string]$Values['DisplayIcon'])
        }
        Add-Check -Step 'install' -What 'skrót w Menu Start' -Passed (Test-Path -LiteralPath $StartMenuLink -PathType Leaf) -Detail $StartMenuLink
        Add-Check -Step 'install' -What 'skrót na pulpicie' -Passed (Test-Path -LiteralPath $DesktopLink -PathType Leaf) -Detail $DesktopLink
        if (-not $DataExisted) {
            Add-Check -Step 'install' -What 'instalator nie tworzy ProgramData\DSOptimizer' -Passed (-not (Test-Path -LiteralPath $DataDir)) -Detail $DataDir
        }
    }

    # 3. upgrade
    Invoke-Step -Step 'upgrade' -Body {
        Remove-Item -LiteralPath $DesktopLink -Force -ErrorAction SilentlyContinue
        $Code = Invoke-Setup -Step 'upgrade'
        Add-Check -Step 'upgrade' -What 'kod 0' -Passed ($Code -eq 0) -Detail "kod: $Code"
        Add-Check -Step 'upgrade' -What 'skrót na pulpicie nie wrócił' -Passed (-not (Test-Path -LiteralPath $DesktopLink)) -Detail $DesktopLink
        Add-Check -Step 'upgrade' -What 'skrót w Menu Start jest' -Passed (Test-Path -LiteralPath $StartMenuLink -PathType Leaf) -Detail $StartMenuLink
        Test-InstalledFiles -Step 'upgrade'
        Add-Check -Step 'upgrade' -What 'brak unins001.dat' -Passed (-not (Test-Path -LiteralPath (Join-Path -Path $AppDir -ChildPath 'unins001.dat')))
    }

    # 4. running
    try {
        Invoke-Step -Step 'running' -Body {
            $Before = Get-SmokeDirectoryHashes -Path $AppDir
            $VersionBefore = [string](Get-SmokeUninstallValues)['DisplayVersion']
            $Script:Held = New-Object System.Threading.Mutex($true, $SmokeMutexName)
            $Code = Invoke-Setup -Step 'running'
            Add-Check -Step 'running' -What 'instalator odmawia kodem 7' -Passed ($Code -eq 7) -Detail "kod: $Code"
            $VersionAfter = [string](Get-SmokeUninstallValues)['DisplayVersion']
            Add-Check -Step 'running' -What 'DisplayVersion bez zmian' -Passed ($VersionAfter -ceq $VersionBefore) -Detail "$VersionBefore -> $VersionAfter"
            $Diff = @(Compare-SmokeFiles -Expected $Before -Actual (Get-SmokeDirectoryHashes -Path $AppDir))
            Add-Check -Step 'running' -What 'sumy plików bez zmian po instalatorze' -Passed ($Diff.Count -eq 0) -Detail ($Diff -join '; ')
            $UninsCode = Invoke-Uninstaller -Step 'running-uninstall'
            Write-Host "running: kod deinstalatora przy działającym programie: $UninsCode"
            $Diff = @(Compare-SmokeFiles -Expected $Before -Actual (Get-SmokeDirectoryHashes -Path $AppDir))
            Add-Check -Step 'running' -What 'deinstalator nic nie usuwa (pliki)' -Passed ($Diff.Count -eq 0) -Detail ("kod deinstalatora: $UninsCode; " + ($Diff -join '; '))
            Add-Check -Step 'running' -What 'deinstalator nic nie usuwa (klucz)' -Passed ($null -ne (Get-SmokeUninstallValues)) -Detail "kod deinstalatora: $UninsCode"
        }
        if ($Script:StepFailed) {
            $Log = Get-StepLog -Step 'running-uninstall'
            if (Test-Path -LiteralPath $Log) {
                Write-Host "--- ostatnie 40 linii logu Inno 'running-uninstall' ---"
                Get-Content -LiteralPath $Log -Tail 40 | ForEach-Object { Write-Host $_ }
            }
        }
    }
    finally {
        if ($null -ne $Script:Held) {
            try { $Script:Held.ReleaseMutex() } catch { Write-Verbose $_.Exception.Message }
            $Script:Held.Dispose()
            $Script:Held = $null
        }
    }

    # 5. uninstall
    Invoke-Step -Step 'uninstall' -Body {
        if (-not (Test-Path -LiteralPath $DataDir)) {
            New-Item -ItemType Directory -Path $DataDir -Force | Out-Null
            Set-Content -LiteralPath (Join-Path -Path $DataDir -ChildPath 'smoke-marker.txt') -Value 'smoke' -Encoding ASCII
        }
        $DataBefore = Get-SmokeDirectoryHashes -Path $DataDir
        $Code = Invoke-Uninstaller -Step 'uninstall'
        Add-Check -Step 'uninstall' -What 'kod 0' -Passed ($Code -eq 0) -Detail "kod: $Code"
        $Deadline = (Get-Date).AddSeconds(60)
        while ((Test-Path -LiteralPath $AppDir) -and (Get-Date) -lt $Deadline) { Start-Sleep -Milliseconds 500 }
        Add-Check -Step 'uninstall' -What 'brak Program Files\DSOptimizer' -Passed (-not (Test-Path -LiteralPath $AppDir)) -Detail $AppDir
        Add-Check -Step 'uninstall' -What 'brak klucza Uninstall' -Passed ($null -eq (Get-SmokeUninstallValues))
        Add-Check -Step 'uninstall' -What 'brak skrótu w Menu Start' -Passed (-not (Test-Path -LiteralPath $StartMenuLink)) -Detail $StartMenuLink
        Add-Check -Step 'uninstall' -What 'brak skrótu na pulpicie' -Passed (-not (Test-Path -LiteralPath $DesktopLink)) -Detail $DesktopLink
        $DataAfter = Get-SmokeDirectoryHashes -Path $DataDir
        $Diff = @(Compare-SmokeFiles -Expected $DataBefore -Actual $DataAfter)
        Add-Check -Step 'uninstall' -What 'ProgramData\DSOptimizer z tymi samymi plikami zostaje' -Passed ((Test-Path -LiteralPath $DataDir) -and $Diff.Count -eq 0) -Detail ($Diff -join '; ')
    }

    $AllPassed = ($Script:Checks.Count -gt 0) -and (@($Script:Checks | Where-Object { -not $_.passed }).Count -eq 0)
    $Result = [ordered]@{
        schema  = 1
        version = $Version
        passed  = $AllPassed
        checks  = @($Script:Checks.ToArray())
    }
    $Json = $Result | ConvertTo-Json -Depth 4
    [System.IO.File]::WriteAllText((Join-Path -Path $LogDirectory -ChildPath 'smoke-result.json'), $Json, (New-Object System.Text.UTF8Encoding($false)))
    return $AllPassed
}

# Przy dot-sourcingu (testy) tylko definicje; przebieg tylko przy zwykłym wywołaniu skryptu.
if ($MyInvocation.InvocationName -ne '.') {
    Set-StrictMode -Version Latest
    $ErrorActionPreference = 'Stop'

    # Strażnik przed jakimkolwiek skutkiem ubocznym.
    if (-not (Test-SmokeMachineAllowed -GitHubActions $env:GITHUB_ACTIONS -ComputerName $env:COMPUTERNAME)) {
        Write-Host "Test-InstallerSmoke działa tylko na runnerze GitHub Actions i w VM DSO-26H2-Home/Pro (komputer: '$env:COMPUTERNAME'); nic nie zostało zmienione."
        exit 2
    }
    $AppDirectory = Join-Path -Path $env:ProgramFiles -ChildPath 'DSOptimizer'
    if ((Test-Path -LiteralPath $AppDirectory) -or ($null -ne (Get-SmokeUninstallValues))) {
        Write-Host 'Maszyna nie jest czysta (jest Program Files\DSOptimizer albo wpis Uninstall DSOptimizera); nic nie zostało zmienione.'
        exit 2
    }

    $ExitCode = 1
    try {
        foreach ($Required in @($InstallerPath, $ZipPath)) {
            if (-not (Test-Path -LiteralPath $Required -PathType Leaf)) { throw "Brak pliku: $Required" }
        }
        if (Invoke-SmokeRun) { $ExitCode = 0 }
    }
    catch {
        Write-Host "Smoke przerwany: $($_.Exception.Message)"
    }
    exit $ExitCode
}
