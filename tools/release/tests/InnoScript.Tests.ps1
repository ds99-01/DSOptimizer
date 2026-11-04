#Requires -Version 5.1
<#
    Testy tools\release\DSOptimizer.iss (Pester 5+, Windows PowerShell 5.1).

    Instrukcja uruchomienia (z katalogu repo):
        Invoke-Pester -Path tools/release/tests/InnoScript.Tests.ps1                         # tekst skryptu i prawdziwa kompilacja
        Invoke-Pester -Path tools/release/tests/InnoScript.Tests.ps1 -ExcludeTagFilter Inno  # sam tekst skryptu

    Opis działania:
        Testy tekstowe czytają DSOptimizer.iss i pilnują reguł instalatora: stały AppId, instalacja tylko dla
        wszystkich użytkowników do Program Files (bez strony wyboru folderu), tylko x64, nazwa wpisu „DSOptimizer”
        równa nazwie z katalogu ochrony (apps-bloat.json, prot.dsoptimizer), mutex równy stałej programu
        (SingleInstanceGuard.cs), odmowa instalacji i deinstalacji przy działającym programie, żadnych zmian
        w danych programu (ProgramData), ikona na pulpicie tylko przy pierwszej instalacji, języki en i pl,
        grafika kreatora (każdy PNG z WizardImageFile i WizardSmallImageFile istnieje i ma wymiary z nazwy).
        Blok z tagiem Inno kompiluje skrypt lokalnym ISCC.exe (Inno Setup 6) z atrapą programu w TestDrive.
        Powstały instalator nie jest uruchamiany. Bez ISCC.exe blok jest pomijany.
#>

BeforeDiscovery {
    $script:IsccPath = Join-Path -Path $env:LOCALAPPDATA -ChildPath 'Programs\Inno Setup 6\ISCC.exe'
    $script:NoIscc = -not (Test-Path -LiteralPath $script:IsccPath -PathType Leaf)
}

BeforeAll {
    $script:RepoRoot = (Resolve-Path -Path (Join-Path -Path $PSScriptRoot -ChildPath '..\..\..')).Path
    $script:IssPath = Join-Path -Path $script:RepoRoot -ChildPath 'tools\release\DSOptimizer.iss'
    $script:IsccPath = Join-Path -Path $env:LOCALAPPDATA -ChildPath 'Programs\Inno Setup 6\ISCC.exe'
    $script:Iss = [System.IO.File]::ReadAllText($script:IssPath, [System.Text.Encoding]::UTF8)

    # Treść jednej sekcji skryptu ([Setup], [Code] …) do następnego nagłówka sekcji.
    function script:Get-IssSection {
        param([Parameter(Mandatory)][string]$Name)
        $Match = [regex]::Match($script:Iss, '(?ms)^\[' + [regex]::Escape($Name) + '\]\s*$(.*?)(?=^\[[A-Za-z]+\]\s*$|\z)')
        if (-not $Match.Success) { return '' }
        return $Match.Groups[1].Value
    }
}

Describe 'DSOptimizer.iss (tekst)' {
    It 'ma BOM UTF-8' {
        $Bytes = [System.IO.File]::ReadAllBytes($script:IssPath)
        $Bytes.Length | Should -BeGreaterThan 3
        $Bytes[0] | Should -Be 0xEF
        $Bytes[1] | Should -Be 0xBB
        $Bytes[2] | Should -Be 0xBF
    }

    It 'ma końce linii CRLF' {
        $script:Iss | Should -Not -Match '(?<!\r)\n'
    }

    It 'AppId jest stały' {
        $script:Iss | Should -Match '(?m)^AppId=\{\{82DBAF33-36AA-4F12-A327-EEC64159D8E8\}\s*$'
    }

    It 'tylko per-machine' {
        $script:Iss | Should -Match '(?m)^PrivilegesRequired=admin\s*$'
        $script:Iss | Should -Not -Match 'PrivilegesRequiredOverridesAllowed'
    }

    It 'folder Program Files, bez strony folderu' {
        $script:Iss | Should -Match '(?m)^DefaultDirName=\{autopf\}\\DSOptimizer\s*$'
        $script:Iss | Should -Match '(?m)^DisableDirPage=yes\s*$'
    }

    It 'tylko x64' {
        $script:Iss | Should -Match '(?m)^ArchitecturesAllowed=x64compatible\s*$'
        $script:Iss | Should -Match '(?m)^ArchitecturesInstallIn64BitMode=x64compatible\s*$'
    }

    It 'wpis Uninstall: nazwa, ikona, wersja' {
        $script:Iss | Should -Match '(?m)^UninstallDisplayName=DSOptimizer\s*$'
        $script:Iss | Should -Match '(?m)^UninstallDisplayIcon=\{app\}\\DSOptimizer\.exe\s*$'
        $script:Iss | Should -Match '(?m)^AppVersion=\{#AppVersion\}\s*$'
        $script:Iss | Should -Match '(?m)^AppPublisher=Dawid Świeboda\s*$'
    }

    It 'bez wymuszania zamykania i bez podmiany przy restarcie' {
        $script:Iss | Should -Match '(?m)^CloseApplications=yes\s*$'
        $script:Iss | Should -Match '(?m)^RestartApplications=no\s*$'
        $script:Iss | Should -Not -Match '(?mi)^CloseApplications=\s*force'
        $script:Iss | Should -Not -Match '(?i)restartreplace'
    }

    It 'nie rusza danych programu' {
        $script:Iss | Should -Not -Match '(?i)\{commonappdata\}'
        $script:Iss | Should -Not -Match '(?mi)^\[UninstallDelete\]'
        $script:Iss | Should -Not -Match '(?mi)^\[InstallDelete\]'
        $script:Iss | Should -Not -Match '(?mi)^\[UninstallRun\]'
    }

    It 'odmowa przy działającym programie' {
        $Code = Get-IssSection -Name 'Code'
        $Prepare = [regex]::Match($Code, '(?s)function PrepareToInstall\(var NeedsRestart: Boolean\): String;.*?\r?\nend;')
        $Prepare.Success | Should -BeTrue
        $Prepare.Value | Should -Match "CheckForMutexes\('\{#ProgramMutex\}'\)"
        $Prepare.Value | Should -Match "CustomMessage\('ProgramRunning'\)"
        $Uninstall = [regex]::Match($Code, '(?s)function InitializeUninstall\(\): Boolean;.*?\r?\nend;')
        $Uninstall.Success | Should -BeTrue
        $Uninstall.Value | Should -Match "CheckForMutexes\('\{#ProgramMutex\}'\)"
        $Uninstall.Value | Should -Match "CustomMessage\('ProgramRunningUninstall'\)"
    }

    It 'odmowa przy innym folderze niż Program Files\DSOptimizer' {
        $Code = Get-IssSection -Name 'Code'
        $Code | Should -Match 'WizardDirValue'
        $Code | Should -Match "ExpandConstant\('\{autopf\}\\DSOptimizer'\)"
        $Code | Should -Match "CustomMessage\('WrongFolder'\)"
    }

    It 'mutex jak w programie' {
        $Guard = [System.IO.File]::ReadAllText((Join-Path -Path $script:RepoRoot -ChildPath 'src\DSOptimizer.Windows\SingleInstanceGuard.cs'), [System.Text.Encoding]::UTF8)
        $Expected = [regex]::Match($Guard, 'ProgramMutexName\s*=\s*@"([^"]+)"')
        $Expected.Success | Should -BeTrue
        $Actual = [regex]::Match($script:Iss, '(?m)^#define ProgramMutex "([^"]+)"\s*$')
        $Actual.Success | Should -BeTrue
        $Actual.Groups[1].Value | Should -BeExactly $Expected.Groups[1].Value
    }

    It 'nazwa wpisu jak w katalogu ochrony' {
        $CatalogPath = Join-Path -Path $script:RepoRoot -ChildPath 'src\DSOptimizer.Catalog\catalog\apps-bloat.json'
        $Catalog = [System.IO.File]::ReadAllText($CatalogPath, [System.Text.Encoding]::UTF8) | ConvertFrom-Json
        $Entry = @($Catalog.protected | Where-Object { $_.id -eq 'prot.dsoptimizer' })
        $Entry.Count | Should -Be 1
        $Actual = [regex]::Match($script:Iss, '(?m)^UninstallDisplayName=(.+?)\s*$')
        $Actual.Success | Should -BeTrue
        $Actual.Groups[1].Value | Should -BeExactly $Entry[0].match.displayName
    }

    It 'języki en (pierwszy) i pl' {
        $En = $script:Iss.IndexOf('Name: "en"; MessagesFile: "compiler:Default.isl"')
        $Pl = $script:Iss.IndexOf('Name: "pl"; MessagesFile: "compiler:Languages\Polish.isl"')
        $En | Should -BeGreaterThan -1
        $Pl | Should -BeGreaterThan $En
        $script:Iss | Should -Match '(?m)^ShowLanguageDialog=no\s*$'
    }

    It 'ikona na pulpicie tylko przy pierwszej instalacji' {
        $Tasks = Get-IssSection -Name 'Tasks'
        $Line = @($Tasks -split '\r?\n' | Where-Object { $_ -match '^Name: "desktopicon";' })
        $Line.Count | Should -Be 1
        $Line[0] | Should -Match 'Check: IsFirstInstall'
        $Line[0] | Should -Not -Match '(?i)unchecked'
        $script:Iss | Should -Match '(?m)^function IsFirstInstall: Boolean;'
    }

    It 'pliki z katalogu buildu bez .pdb' {
        $Files = Get-IssSection -Name 'Files'
        $Line = @($Files -split '\r?\n' | Where-Object { $_ -match '^Source: ' })
        $Line.Count | Should -Be 1
        $Line[0] | Should -Match ([regex]::Escape('Source: "{#ProgramDir}\*";'))
        $Line[0] | Should -Match ([regex]::Escape('Excludes: "*.pdb";'))
        $Line[0] | Should -Match 'Flags: [^;]*\brecursesubdirs\b'
    }

    It 'komunikaty en i pl dla odmów' {
        $Messages = Get-IssSection -Name 'CustomMessages'
        foreach ($Name in @('en.ProgramRunning', 'pl.ProgramRunning', 'en.ProgramRunningUninstall', 'pl.ProgramRunningUninstall', 'en.WrongFolder', 'pl.WrongFolder')) {
            $Messages | Should -Match ('(?m)^' + [regex]::Escape($Name) + '=\S')
        }
    }

    It 'grafika kreatora: <Directive> to pliki PNG o zadeklarowanych wymiarach' -ForEach @(
        @{ Directive = 'WizardImageFile'; Expected = @('202x386', '269x515', '336x643', '430x824', '534x1022') }
        @{ Directive = 'WizardSmallImageFile'; Expected = @('58x58', '77x77', '97x97', '124x124', '159x159') }
    ) {
        $Line = [regex]::Match($script:Iss, '(?m)^' + $Directive + '=(.+?)\s*$')
        $Line.Success | Should -BeTrue
        $Files = @($Line.Groups[1].Value -split ',' | ForEach-Object { $_.Trim() })
        $Sizes = foreach ($File in $Files) {
            $File | Should -Match '^wizard\\wizard-(image|small)-\d+x\d+\.png$'
            $Path = Join-Path -Path $script:RepoRoot -ChildPath ('tools\release\' + $File)
            Test-Path -LiteralPath $Path -PathType Leaf | Should -BeTrue -Because $File
            # Nagłówek PNG: sygnatura, potem IHDR z szerokością i wysokością (big-endian, bajty 16-23).
            $Bytes = [System.IO.File]::ReadAllBytes($Path)
            [System.BitConverter]::ToString($Bytes, 0, 8) | Should -Be '89-50-4E-47-0D-0A-1A-0A' -Because $File
            [System.Text.Encoding]::ASCII.GetString($Bytes, 12, 4) | Should -Be 'IHDR' -Because $File
            $Width = ([int]$Bytes[16] -shl 24) -bor ([int]$Bytes[17] -shl 16) -bor ([int]$Bytes[18] -shl 8) -bor [int]$Bytes[19]
            $Height = ([int]$Bytes[20] -shl 24) -bor ([int]$Bytes[21] -shl 16) -bor ([int]$Bytes[22] -shl 8) -bor [int]$Bytes[23]
            $Declared = [regex]::Match($File, '(\d+)x(\d+)\.png$')
            "${Width}x${Height}" | Should -Be ($Declared.Groups[1].Value + 'x' + $Declared.Groups[2].Value) -Because $File
            "${Width}x${Height}"
        }
        @($Sizes) | Should -Be $Expected
    }

    It 'wymaga AppVersion i ProgramDir (#error)' {
        $script:Iss | Should -Match '(?m)^#ifndef AppVersion\s*$\s*^#error [^\r\n]+\s*^#endif'
        $script:Iss | Should -Match '(?m)^#ifndef ProgramDir\s*$\s*^#error [^\r\n]+\s*^#endif'
    }
}

Describe 'Kompilacja (prawdziwy ISCC)' -Tag 'Inno' {
    BeforeAll {
        # Uruchamia ISCC.exe i zwraca kod wyjścia oraz połączone stdout i stderr. Powstałego instalatora nikt nie uruchamia.
        function script:Invoke-Iscc {
            param([Parameter(Mandatory)][string[]]$Arguments)
            $Quoted = foreach ($Argument in $Arguments) { '"' + $Argument + '"' }
            $Info = New-Object -TypeName System.Diagnostics.ProcessStartInfo
            $Info.FileName = $script:IsccPath
            $Info.Arguments = $Quoted -join ' '
            $Info.UseShellExecute = $false
            $Info.CreateNoWindow = $true
            $Info.RedirectStandardOutput = $true
            $Info.RedirectStandardError = $true
            $Process = [System.Diagnostics.Process]::Start($Info)
            $ErrTask = $Process.StandardError.ReadToEndAsync()
            $Out = $Process.StandardOutput.ReadToEnd()
            $Process.WaitForExit()
            return [pscustomobject]@{ ExitCode = $Process.ExitCode; Output = $Out + $ErrTask.Result }
        }

        $script:ProgramDir = Join-Path -Path $TestDrive -ChildPath 'program'
        $script:OutDir = Join-Path -Path $TestDrive -ChildPath 'out'
        $null = New-Item -ItemType Directory -Path $script:ProgramDir
        $null = New-Item -ItemType Directory -Path $script:OutDir
        [System.IO.File]::WriteAllBytes((Join-Path -Path $script:ProgramDir -ChildPath 'DSOptimizer.exe'), [byte[]](0x4D, 0x5A, 0, 0, 0, 0))
        [System.IO.File]::WriteAllText((Join-Path -Path $script:ProgramDir -ChildPath 'DSOptimizer.exe.config'), '<configuration />')
        [System.IO.File]::WriteAllText((Join-Path -Path $script:ProgramDir -ChildPath 'Wpf.Ui.dll'), 'atrapa')
        [System.IO.File]::WriteAllText((Join-Path -Path $script:ProgramDir -ChildPath 'DSOptimizer.pdb'), 'atrapa')
    }

    It 'kompiluje instalator z atrapy programu' -Skip:$script:NoIscc {
        $Result = Invoke-Iscc -Arguments @('/DAppVersion=4.0.0', "/DProgramDir=$script:ProgramDir", "/O$script:OutDir", '/FDSOptimizer-4.0.0-setup', '/Q', $script:IssPath)
        $Result.ExitCode | Should -Be 0 -Because $Result.Output
        $Setup = Join-Path -Path $script:OutDir -ChildPath 'DSOptimizer-4.0.0-setup.exe'
        Test-Path -LiteralPath $Setup -PathType Leaf | Should -BeTrue
        (Get-Item -LiteralPath $Setup).Length | Should -BeGreaterThan 0
    }

    It 'bez /DAppVersion kompilacja kończy się błędem #error' -Skip:$script:NoIscc {
        $Result = Invoke-Iscc -Arguments @("/DProgramDir=$script:ProgramDir", "/O$script:OutDir", '/FDSOptimizer-bez-wersji-setup', '/Q', $script:IssPath)
        $Result.ExitCode | Should -Not -Be 0
        # ISCC nie cytuje dyrektywy, tylko jej tekst: „Error on line N in …\DSOptimizer.iss: Podaj /DAppVersion=X.Y.Z.”
        $Result.Output | Should -Match 'Error on line \d+ in [^\r\n]*DSOptimizer\.iss: Podaj /DAppVersion=X\.Y\.Z'
        Test-Path -LiteralPath (Join-Path -Path $script:OutDir -ChildPath 'DSOptimizer-bez-wersji-setup.exe') | Should -BeFalse
    }
}
