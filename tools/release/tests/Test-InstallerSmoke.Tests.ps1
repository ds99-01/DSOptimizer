#Requires -Version 5.1
<#
    Testy tools\release\Test-InstallerSmoke.ps1 (Pester 5+, Windows PowerShell 5.1).

    Instrukcja uruchomienia (z katalogu repo):
        Invoke-Pester -Path tools/release/tests/Test-InstallerSmoke.Tests.ps1

    Opis działania:
        Testy niczego nie instalują i nie uruchamiają procesów (Start-Process jest atrapą, która rzuca wyjątek).
        Sprawdzają funkcje czyste (dopuszczenie maszyny, porównanie plików), odmowę startu na hoście (kod 2, bez
        żadnych zmian), zgodność stałych z DSOptimizer.iss i kodowanie pliku.
#>

BeforeAll {
    $script:ScriptPath = Join-Path -Path $PSScriptRoot -ChildPath '..\Test-InstallerSmoke.ps1'
    $script:IssPath = Join-Path -Path $PSScriptRoot -ChildPath '..\DSOptimizer.iss'
    . $script:ScriptPath -InstallerPath 'X:\brak\setup.exe' -ZipPath 'X:\brak\x.zip' -Version '1.2.3' -LogDirectory 'X:\brak\log'
}

Describe 'Test-InstallerSmoke' {
    It 'na hoście (bez GITHUB_ACTIONS, inna nazwa komputera) kończy się kodem 2 i niczego nie uruchamia' {
        Mock Start-Process { throw 'HOST: Start-Process niedozwolone' }
        $OldGh = $env:GITHUB_ACTIONS
        $OldName = $env:COMPUTERNAME
        $LogDir = Join-Path -Path $TestDrive -ChildPath 'log-hosta'
        try {
            $env:GITHUB_ACTIONS = ''
            $env:COMPUTERNAME = 'DAWID-PC'
            $Global:LASTEXITCODE = $null
            & $script:ScriptPath -InstallerPath 'X:\brak\setup.exe' -ZipPath 'X:\brak\x.zip' -Version '1.2.3' -LogDirectory $LogDir *> $null
            $Code = $LASTEXITCODE
        }
        finally {
            $env:GITHUB_ACTIONS = $OldGh
            $env:COMPUTERNAME = $OldName
        }
        $Code | Should -Be 2
        Should -Invoke Start-Process -Times 0 -Exactly
        Test-Path -LiteralPath $LogDir | Should -BeFalse
    }

    It 'Test-SmokeMachineAllowed' {
        Test-SmokeMachineAllowed -GitHubActions 'true' -ComputerName 'X' | Should -BeTrue
        Test-SmokeMachineAllowed -GitHubActions '' -ComputerName 'DSO-EN-PRO' | Should -BeTrue
        Test-SmokeMachineAllowed -GitHubActions '' -ComputerName 'dso-pl-home' | Should -BeTrue
        Test-SmokeMachineAllowed -GitHubActions '' -ComputerName 'DSO-26H2-HOME' | Should -BeFalse
        Test-SmokeMachineAllowed -GitHubActions '' -ComputerName 'DAWID-PC' | Should -BeFalse
        Test-SmokeMachineAllowed -GitHubActions 'TRUE' -ComputerName 'X' | Should -BeFalse
    }

    It 'Compare-SmokeFiles: równe zbiory = brak różnic; brak pliku, nadmiar i inna suma = po jednej różnicy' {
        $Expected = @{ 'a.exe' = 'AA'; 'sub\b.dll' = 'BB' }
        @(Compare-SmokeFiles -Expected $Expected -Actual @{ 'a.exe' = 'AA'; 'sub\b.dll' = 'BB' }).Count | Should -Be 0
        @(Compare-SmokeFiles -Expected $Expected -Actual @{ 'a.exe' = 'AA' }).Count | Should -Be 1
        @(Compare-SmokeFiles -Expected $Expected -Actual @{ 'a.exe' = 'AA'; 'sub\b.dll' = 'BB'; 'c.txt' = 'CC' }).Count | Should -Be 1
        @(Compare-SmokeFiles -Expected $Expected -Actual @{ 'a.exe' = 'AA'; 'sub\b.dll' = 'ZZ' }).Count | Should -Be 1
    }

    It 'AppId i mutex jak w DSOptimizer.iss' {
        $Iss = [System.IO.File]::ReadAllText($script:IssPath, [System.Text.Encoding]::UTF8)
        $Iss -match 'AppId=\{(\{[0-9A-Fa-f-]+\})' | Should -BeTrue
        $SmokeAppId | Should -BeExactly $Matches[1]
        $Iss -match '#define ProgramMutex "([^"]+)"' | Should -BeTrue
        $SmokeMutexName | Should -BeExactly $Matches[1]
    }

    It 'plik ma BOM UTF-8' {
        $Bytes = [System.IO.File]::ReadAllBytes($script:ScriptPath)
        $Bytes[0] | Should -Be 0xEF
        $Bytes[1] | Should -Be 0xBB
        $Bytes[2] | Should -Be 0xBF
    }

    It 'strażnik (maszyna, 64-bit, czystość) stoi przed jakimkolwiek skutkiem ubocznym' {
        $Text = [System.IO.File]::ReadAllText($script:ScriptPath, [System.Text.Encoding]::UTF8)
        $Main = $Text.Substring($Text.IndexOf('if ($MyInvocation.InvocationName'))
        $Machine = $Main.IndexOf('Test-SmokeMachineAllowed')
        $Bits = $Main.IndexOf('Is64BitProcess')
        $Clean = $Main.IndexOf('Maszyna nie jest czysta')
        $Resolve = $Main.IndexOf('Resolve-SmokePath')
        $Run = $Main.IndexOf('Invoke-SmokeRun')
        $Machine | Should -BeGreaterThan -1
        $Bits | Should -BeGreaterThan $Machine
        $Clean | Should -BeGreaterThan $Bits
        $Resolve | Should -BeGreaterThan $Clean
        $Run | Should -BeGreaterThan $Resolve
    }

    It 'Resolve-SmokePath zamienia ścieżkę względną na bezwzględną względem $PWD' {
        Push-Location -LiteralPath $TestDrive
        try { $Full = Resolve-SmokePath -Path 'a\b.zip' } finally { Pop-Location }
        $Full | Should -Be ([System.IO.Path]::GetFullPath((Join-Path $TestDrive 'a\b.zip')))
        Resolve-SmokePath -Path 'C:\x\..\y.zip' | Should -Be 'C:\y.zip'
    }

    It 'wyjątek na początku kroku running nie przerywa przebiegu: krok uninstall działa, smoke-result.json powstaje' {
        Mock Start-Process { throw 'HOST: Start-Process niedozwolone' }
        Mock Invoke-SmokeProcess { 0 }
        Mock Get-SmokeZipHashes { @{} }
        Mock Get-SmokeUninstallValues { $null }
        Mock Get-SmokeDirectoryHashes { throw 'test: wyjątek w kroku' }
        Mock Remove-Item { }
        Mock Get-SmokeProgramDataPath { Join-Path -Path $TestDrive -ChildPath 'pd' }
        $OldPf = $env:ProgramFiles
        $LogDir = Join-Path -Path $TestDrive -ChildPath 'log-running'
        try {
            $env:ProgramFiles = Join-Path -Path $TestDrive -ChildPath 'pf'
            $Passed = Invoke-SmokeRun -InstallerPath 'X:\brak\setup.exe' -ZipPath 'X:\brak\x.zip' -Version '1.2.3' -LogDirectory $LogDir 6> $null
        }
        finally {
            $env:ProgramFiles = $OldPf
        }
        $Passed | Should -BeFalse
        Should -Invoke Start-Process -Times 0 -Exactly
        $Result = Get-Content -LiteralPath (Join-Path $LogDir 'smoke-result.json') -Raw -Encoding UTF8 | ConvertFrom-Json
        $Result.passed | Should -BeFalse
        @($Result.checks | Where-Object { $_.name -like 'uninstall:*' }).Count | Should -BeGreaterThan 0
        @($Result.checks | Where-Object { $_.name -eq 'running: wyjątek w kroku' }).Count | Should -Be 1
    }
}
