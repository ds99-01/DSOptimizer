#Requires -Version 5.1
<#
    Testy tools\release\Build-Release.ps1 (Pester 5+, Windows PowerShell 5.1).

    Instrukcja uruchomienia (z katalogu repo):
        Invoke-Pester -Path tools/release/tests                         # wszystko, także prawdziwy build jednego exe (ok. 1 min)
        Invoke-Pester -Path tools/release/tests -ExcludeTagFilter Build # same testy na atrapie dotnet

    Opis działania:
        Testy skryptu kopiują Build-Release.ps1 do sztucznego repo w TestDrive (Directory.Build.props z Version 4.0.0)
        i podmieniają `dotnet` atrapą, która tworzy w katalogu `-o` udawane pliki buildu. Nic nie jest budowane naprawdę.
        Blok z tagiem Build uruchamia prawdziwy `dotnet build` aplikacji z /p:DsoSingleExe=true do katalogu tymczasowego
        i sprawdza, że Costura osadziła biblioteki w jednym exe: bez kompresji i bez symboli debugowania.
#>

BeforeAll {
    $script:SourceScript = (Resolve-Path -Path (Join-Path -Path $PSScriptRoot -ChildPath '..\Build-Release.ps1')).Path
    $script:RealRepoRoot = (Resolve-Path -Path (Join-Path -Path $PSScriptRoot -ChildPath '..\..\..')).Path

    function script:New-FakeRepo {
        param([string]$Version = '4.0.0')

        $Root = Join-Path -Path $TestDrive -ChildPath ([guid]::NewGuid().ToString('N'))
        $null = New-Item -ItemType Directory -Path (Join-Path -Path $Root -ChildPath 'tools\release')
        $null = New-Item -ItemType Directory -Path (Join-Path -Path $Root -ChildPath 'src\DSOptimizer.App')
        Copy-Item -LiteralPath $script:SourceScript -Destination (Join-Path -Path $Root -ChildPath 'tools\release\Build-Release.ps1')
        $Props = "<Project>`r`n  <PropertyGroup>`r`n    <TargetFramework>net481</TargetFramework>`r`n    <Version>$Version</Version>`r`n  </PropertyGroup>`r`n</Project>`r`n"
        [System.IO.File]::WriteAllText((Join-Path -Path $Root -ChildPath 'Directory.Build.props'), $Props, (New-Object -TypeName System.Text.UTF8Encoding -ArgumentList $true))
        [System.IO.File]::WriteAllText((Join-Path -Path $Root -ChildPath 'src\DSOptimizer.App\DSOptimizer.App.csproj'), '<Project Sdk="Microsoft.NET.Sdk" />')
        [System.IO.File]::WriteAllText((Join-Path -Path $Root -ChildPath 'tools\release\DSOptimizer.iss'), '; atrapa')
        return $Root
    }

    # Atrapa ISCC: skrypt .ps1 wykonywany w procesie Pestera (nie nowy proces). Zapisuje TestDrive\iscc-call.json
    # (argumenty i pliki z /DProgramDir bez .pdb z SHA256) i tworzy <O>\<F>.exe; z -Fail kończy się kodem 2 bez pliku.
    function script:New-FakeIscc {
        param([switch]$Fail)

        $Path = Join-Path -Path $TestDrive -ChildPath 'fake-iscc.ps1'
        $CallPath = Join-Path -Path $TestDrive -ChildPath 'iscc-call.json'
        if (Test-Path -LiteralPath $CallPath) {
            Remove-Item -LiteralPath $CallPath -Force
        }

        $Body = if ($Fail) {
            "exit 2`r`n"
        }
        else {
            @'
$OutDir = $null; $FileName = $null; $ProgramDir = $null
foreach ($Argument in $args) {
    $Text = [string]$Argument
    if ($Text.StartsWith('/DProgramDir=')) { $ProgramDir = $Text.Substring(13) }
    elseif ($Text.StartsWith('/O')) { $OutDir = $Text.Substring(2) }
    elseif ($Text.StartsWith('/F')) { $FileName = $Text.Substring(2) }
}
$Prefix = $ProgramDir.TrimEnd('\') + '\'
$Files = @(Get-ChildItem -LiteralPath $ProgramDir -Recurse -File | Where-Object { $_.Extension -ne '.pdb' } | ForEach-Object {
    [pscustomobject]@{ Name = $_.FullName.Substring($Prefix.Length).Replace('\', '/'); Sha256 = (Get-FileHash -LiteralPath $_.FullName -Algorithm SHA256).Hash.ToLowerInvariant() }
})
$Call = [pscustomobject]@{ Args = @($args | ForEach-Object { [string]$_ }); Files = $Files }
[System.IO.File]::WriteAllText((Join-Path -Path $PSScriptRoot -ChildPath 'iscc-call.json'), (ConvertTo-Json -InputObject $Call -Depth 5))
$null = New-Item -ItemType Directory -Path $OutDir -Force
[System.IO.File]::WriteAllBytes((Join-Path -Path $OutDir -ChildPath ($FileName + '.exe')), [byte[]](77, 90, 9, 9))
exit 0
'@
        }

        [System.IO.File]::WriteAllText($Path, $Body)
        return $Path
    }

    function script:Get-IsccCall {
        return (ConvertFrom-Json -InputObject ([System.IO.File]::ReadAllText((Join-Path -Path $TestDrive -ChildPath 'iscc-call.json'))))
    }

    function script:Get-ScriptPath {
        param([Parameter(Mandatory)][string]$Repo)
        return (Join-Path -Path $Repo -ChildPath 'tools\release\Build-Release.ps1')
    }

    # Atrapa wyjścia `dotnet build -o <katalog>`: udawane pliki folderu programu albo jednego exe.
    function script:Write-FakeBuildOutput {
        param(
            [Parameter(Mandatory)][object[]]$Arguments,
            [string[]]$ExtraFiles = @()
        )

        $Index = [array]::IndexOf([string[]]$Arguments, '-o')
        $OutDir = [string]$Arguments[$Index + 1]
        $null = New-Item -ItemType Directory -Path $OutDir -Force
        [System.IO.File]::WriteAllBytes((Join-Path -Path $OutDir -ChildPath 'DSOptimizer.exe'), [byte[]](77, 90, 1, 2, 3, 4))
        [System.IO.File]::WriteAllText((Join-Path -Path $OutDir -ChildPath 'DSOptimizer.exe.config'), '<configuration />')
        [System.IO.File]::WriteAllText((Join-Path -Path $OutDir -ChildPath 'DSOptimizer.pdb'), 'pdb')
        foreach ($Name in $ExtraFiles) {
            $Path = Join-Path -Path $OutDir -ChildPath $Name
            $null = New-Item -ItemType Directory -Path (Split-Path -Path $Path -Parent) -Force
            [System.IO.File]::WriteAllText($Path, $Name)
        }
    }

    function script:Get-Sha256Hex {
        param([Parameter(Mandatory)][string]$Path)
        $Sha = [System.Security.Cryptography.SHA256]::Create()
        try {
            $Bytes = $Sha.ComputeHash([System.IO.File]::ReadAllBytes($Path))
            return (-join ($Bytes | ForEach-Object { $_.ToString('x2') }))
        }
        finally {
            $Sha.Dispose()
        }
    }

    function script:Get-ZipEntryNames {
        param([Parameter(Mandatory)][string]$Path)
        Add-Type -AssemblyName System.IO.Compression
        Add-Type -AssemblyName System.IO.Compression.FileSystem
        $Zip = [System.IO.Compression.ZipFile]::OpenRead($Path)
        try {
            return @($Zip.Entries | ForEach-Object { $_.FullName })
        }
        finally {
            $Zip.Dispose()
        }
    }
}

Describe 'Build-Release.ps1 – sprawdzenie tagu i parametrów' {
    BeforeEach {
        $script:Repo = New-FakeRepo -Version '4.0.0'
        $script:OutRoot = Join-Path -Path $TestDrive -ChildPath ('out-' + [guid]::NewGuid().ToString('N'))
        Mock dotnet { Write-FakeBuildOutput -Arguments $args; $global:LASTEXITCODE = 0 }
    }

    It 'tag inny niż v + Version z Directory.Build.props = kod 1 i dotnet niewywołany' {
        $global:LASTEXITCODE = 0
        $Result = & (Get-ScriptPath -Repo $Repo) -Variant SingleExe -Tag 'v4.0.1' -OutRoot $OutRoot 2>$null

        $LASTEXITCODE | Should -Be 1
        $Result | Should -BeNullOrEmpty
        Should -Invoke dotnet -Times 0 -Exactly
        Test-Path -LiteralPath $OutRoot | Should -BeFalse
    }

    It 'tag zgodny z wersją buduje wydanie' {
        $Result = & (Get-ScriptPath -Repo $Repo) -Variant SingleExe -Tag 'v4.0.0' -OutRoot $OutRoot

        $LASTEXITCODE | Should -Be 0
        $Result | Should -Be (Join-Path -Path $OutRoot -ChildPath 'v4.0.0-SingleExe\DSOptimizer-4.0.0.exe')
        Should -Invoke dotnet -Times 1 -Exactly
    }

    It 'nieznany -Variant jest odrzucany przed budowaniem' {
        { & (Get-ScriptPath -Repo $Repo) -Variant 'Zip' -OutRoot $OutRoot } | Should -Throw
        Should -Invoke dotnet -Times 0 -Exactly
        Test-Path -LiteralPath $OutRoot | Should -BeFalse
    }

    It 'nieudany dotnet build = kod 1 bez pliku wydania' {
        Mock dotnet { $global:LASTEXITCODE = 1 }

        $Result = & (Get-ScriptPath -Repo $Repo) -Variant Folder -NoInstaller -OutRoot $OutRoot 2>$null

        $LASTEXITCODE | Should -Be 1
        $Result | Should -BeNullOrEmpty
        Test-Path -LiteralPath (Join-Path -Path $OutRoot -ChildPath 'v4.0.0-Folder\DSOptimizer-4.0.0.zip') | Should -BeFalse
    }

    It 'bez -OutRoot zapisuje do katalogu wydania w repo' {
        $Result = & (Get-ScriptPath -Repo $Repo) -Variant SingleExe

        $LASTEXITCODE | Should -Be 0
        $Result | Should -Be (Join-Path -Path $Repo -ChildPath 'wydania\v4.0.0-SingleExe\DSOptimizer-4.0.0.exe')
        Test-Path -LiteralPath $Result | Should -BeTrue
    }
}

Describe 'Build-Release.ps1 – wariant SingleExe' {
    BeforeEach {
        $script:Repo = New-FakeRepo -Version '4.0.0'
        $script:OutRoot = Join-Path -Path $TestDrive -ChildPath ('out-' + [guid]::NewGuid().ToString('N'))
        Mock dotnet { Write-FakeBuildOutput -Arguments $args; $global:LASTEXITCODE = 0 }
    }

    It 'buduje App w Release z /p:DsoSingleExe=true do katalogu pośredniego poza OutRoot' {
        $null = & (Get-ScriptPath -Repo $Repo) -Variant SingleExe -OutRoot $OutRoot

        $Csproj = Join-Path -Path $Repo -ChildPath 'src\DSOptimizer.App\DSOptimizer.App.csproj'
        Should -Invoke dotnet -Times 1 -Exactly -ParameterFilter {
            $args[0] -eq 'build' -and $args[1] -eq $Csproj -and ($args -join ' ') -match '(^| )-c Release( |$)' -and $args -contains '/p:DsoSingleExe=true' -and $args -contains '--no-incremental'
        }
        Should -Invoke dotnet -Times 1 -Exactly -ParameterFilter {
            $Index = [array]::IndexOf([string[]]$args, '-o')
            $Index -ge 0 -and -not ([string]$args[$Index + 1]).StartsWith($OutRoot, [System.StringComparison]::OrdinalIgnoreCase)
        }
    }

    It 'kopiuje exe jako DSOptimizer-4.0.0.exe i nic poza nim i SHA256SUMS.txt' {
        $Result = & (Get-ScriptPath -Repo $Repo) -Variant SingleExe -OutRoot $OutRoot

        $Dir = Join-Path -Path $OutRoot -ChildPath 'v4.0.0-SingleExe'
        $Result | Should -Be (Join-Path -Path $Dir -ChildPath 'DSOptimizer-4.0.0.exe')
        [System.IO.File]::ReadAllBytes($Result) | Should -Be ([byte[]](77, 90, 1, 2, 3, 4))
        @(Get-ChildItem -LiteralPath $Dir | ForEach-Object { $_.Name } | Sort-Object) | Should -Be @('DSOptimizer-4.0.0.exe', 'SHA256SUMS.txt')
    }

    It 'SHA256SUMS.txt ma format sha256sum: 64 znaki hex małymi literami, dwie spacje, nazwa' {
        $Result = & (Get-ScriptPath -Repo $Repo) -Variant SingleExe -OutRoot $OutRoot

        $SumsPath = Join-Path -Path (Split-Path -Path $Result -Parent) -ChildPath 'SHA256SUMS.txt'
        $Text = [System.IO.File]::ReadAllText($SumsPath)
        $Text | Should -MatchExactly '^[0-9a-f]{64}  DSOptimizer-4\.0\.0\.exe\n$'
        $Text.Substring(0, 64) | Should -BeExactly (Get-Sha256Hex -Path $Result)
        [System.IO.File]::ReadAllBytes($SumsPath)[0] | Should -Not -Be 0xEF   # bez BOM
    }

    It 'gdy w wyjściu jednego exe zostały biblioteki (Costura nie zadziałała) = kod 1 bez pliku wydania' {
        Mock dotnet { Write-FakeBuildOutput -Arguments $args -ExtraFiles @('Wpf.Ui.dll'); $global:LASTEXITCODE = 0 }

        $Result = & (Get-ScriptPath -Repo $Repo) -Variant SingleExe -OutRoot $OutRoot 2>$null

        $LASTEXITCODE | Should -Be 1
        $Result | Should -BeNullOrEmpty
        Test-Path -LiteralPath (Join-Path -Path $OutRoot -ChildPath 'v4.0.0-SingleExe\DSOptimizer-4.0.0.exe') | Should -BeFalse
    }
}

Describe 'Build-Release.ps1 – wariant Folder' {
    BeforeEach {
        $script:Repo = New-FakeRepo -Version '4.0.0'
        $script:OutRoot = Join-Path -Path $TestDrive -ChildPath ('out-' + [guid]::NewGuid().ToString('N'))
        Mock dotnet {
            Write-FakeBuildOutput -Arguments $args -ExtraFiles @('Wpf.Ui.dll', 'DSOptimizer.Core.dll', 'DSOptimizer.Core.pdb', 'pl\DSOptimizer.resources.dll')
            $global:LASTEXITCODE = 0
        }
    }

    It 'buduje App w Release z jawnym /p:DsoSingleExe=false' {
        $null = & (Get-ScriptPath -Repo $Repo) -Variant Folder -NoInstaller -OutRoot $OutRoot

        Should -Invoke dotnet -Times 1 -Exactly -ParameterFilter {
            $args[0] -eq 'build' -and $args -contains '/p:DsoSingleExe=false' -and $args -notcontains '/p:DsoSingleExe=true'
        }
    }

    It 'zip ma katalog DSOptimizer\ z całym folderem programu bez .pdb' {
        $Result = & (Get-ScriptPath -Repo $Repo) -Variant Folder -NoInstaller -OutRoot $OutRoot

        $Result | Should -Be (Join-Path -Path $OutRoot -ChildPath 'v4.0.0-Folder\DSOptimizer-4.0.0.zip')
        $Entries = Get-ZipEntryNames -Path $Result
        $Entries | Should -Not -BeNullOrEmpty
        $Entries | Where-Object { $_ -like '*.pdb' } | Should -BeNullOrEmpty
        $Entries | Where-Object { -not $_.StartsWith('DSOptimizer/', [System.StringComparison]::Ordinal) } | Should -BeNullOrEmpty
        @($Entries | Sort-Object) | Should -Be @(
            'DSOptimizer/DSOptimizer.Core.dll',
            'DSOptimizer/DSOptimizer.exe',
            'DSOptimizer/DSOptimizer.exe.config',
            'DSOptimizer/pl/DSOptimizer.resources.dll',
            'DSOptimizer/Wpf.Ui.dll'
        )
    }

    It 'SHA256SUMS.txt opisuje zip w formacie sha256sum' {
        $Result = & (Get-ScriptPath -Repo $Repo) -Variant Folder -NoInstaller -OutRoot $OutRoot

        $Text = [System.IO.File]::ReadAllText((Join-Path -Path (Split-Path -Path $Result -Parent) -ChildPath 'SHA256SUMS.txt'))
        $Text | Should -MatchExactly '^[0-9a-f]{64}  DSOptimizer-4\.0\.0\.zip\n$'
        $Text.Substring(0, 64) | Should -BeExactly (Get-Sha256Hex -Path $Result)
    }

    It 'ponowne uruchomienie nadpisuje zip zamiast błędu' {
        $null = & (Get-ScriptPath -Repo $Repo) -Variant Folder -NoInstaller -OutRoot $OutRoot
        $Result = & (Get-ScriptPath -Repo $Repo) -Variant Folder -NoInstaller -OutRoot $OutRoot

        $LASTEXITCODE | Should -Be 0
        (Get-ZipEntryNames -Path $Result).Count | Should -Be 5
    }
}

Describe 'Build-Release.ps1 – wariant Folder z instalatorem' {
    BeforeEach {
        $script:Repo = New-FakeRepo -Version '4.0.0'
        $script:OutRoot = Join-Path -Path $TestDrive -ChildPath ('out-' + [guid]::NewGuid().ToString('N'))
        $script:Dir = Join-Path -Path $script:OutRoot -ChildPath 'v4.0.0-Folder'
        $script:Iscc = New-FakeIscc
        Mock dotnet {
            Write-FakeBuildOutput -Arguments $args -ExtraFiles @('Wpf.Ui.dll', 'DSOptimizer.Core.dll', 'DSOptimizer.Core.pdb', 'pl\DSOptimizer.resources.dll')
            $global:LASTEXITCODE = 0
        }
    }

    It 'zip, instalator, SHA256SUMS.txt i nic więcej' {
        $null = & (Get-ScriptPath -Repo $Repo) -Variant Folder -IsccPath $Iscc -OutRoot $OutRoot

        $LASTEXITCODE | Should -Be 0
        $Names = @(Get-ChildItem -LiteralPath $Dir | ForEach-Object { $_.Name })
        $Names | Should -HaveCount 3
        $Names | Should -Contain 'DSOptimizer-4.0.0-setup.exe'
        $Names | Should -Contain 'DSOptimizer-4.0.0.zip'
        $Names | Should -Contain 'SHA256SUMS.txt'
    }

    It 'SHA256SUMS.txt ma dwie linie: zip, potem instalator' {
        $null = & (Get-ScriptPath -Repo $Repo) -Variant Folder -IsccPath $Iscc -OutRoot $OutRoot

        $SumsPath = Join-Path -Path $Dir -ChildPath 'SHA256SUMS.txt'
        $Text = [System.IO.File]::ReadAllText($SumsPath)
        $Text | Should -MatchExactly '^[0-9a-f]{64}  DSOptimizer-4\.0\.0\.zip\n[0-9a-f]{64}  DSOptimizer-4\.0\.0-setup\.exe\n$'
        $Lines = @($Text.TrimEnd("`n").Split("`n"))
        $Lines[0].Substring(0, 64) | Should -BeExactly (Get-Sha256Hex -Path (Join-Path -Path $Dir -ChildPath 'DSOptimizer-4.0.0.zip'))
        $Lines[1].Substring(0, 64) | Should -BeExactly (Get-Sha256Hex -Path (Join-Path -Path $Dir -ChildPath 'DSOptimizer-4.0.0-setup.exe'))
        [System.IO.File]::ReadAllBytes($SumsPath)[0] | Should -Not -Be 0xEF   # bez BOM
    }

    It 'instalator powstaje z tego samego katalogu co zip' {
        $Result = @(& (Get-ScriptPath -Repo $Repo) -Variant Folder -IsccPath $Iscc -OutRoot $OutRoot)

        $Call = Get-IsccCall
        $Names = @($Call.Files | ForEach-Object { $_.Name } | Sort-Object)
        $Names | Should -Be @('DSOptimizer.Core.dll', 'DSOptimizer.exe', 'DSOptimizer.exe.config', 'pl/DSOptimizer.resources.dll', 'Wpf.Ui.dll')
        Add-Type -AssemblyName System.IO.Compression
        Add-Type -AssemblyName System.IO.Compression.FileSystem
        $Zip = [System.IO.Compression.ZipFile]::OpenRead($Result[0])
        try {
            $InZip = @($Zip.Entries | ForEach-Object {
                $Stream = $_.Open()
                try {
                    $Sha = [System.Security.Cryptography.SHA256]::Create()
                    try { $Hex = -join ($Sha.ComputeHash($Stream) | ForEach-Object { $_.ToString('x2') }) } finally { $Sha.Dispose() }
                }
                finally { $Stream.Dispose() }
                [pscustomobject]@{ Name = $_.FullName.Substring('DSOptimizer/'.Length); Sha256 = $Hex }
            } | Sort-Object -Property Name)
        }
        finally {
            $Zip.Dispose()
        }

        @($InZip | ForEach-Object { $_.Name }) | Should -Be $Names
        foreach ($Entry in $InZip) {
            ($Call.Files | Where-Object { $_.Name -ceq $Entry.Name }).Sha256 | Should -BeExactly $Entry.Sha256
        }
    }

    It 'przekazuje wersję, katalog wyjścia i nazwę pliku' {
        $null = & (Get-ScriptPath -Repo $Repo) -Variant Folder -IsccPath $Iscc -OutRoot $OutRoot

        $Arguments = @((Get-IsccCall).Args)
        $Arguments | Should -Contain '/DAppVersion=4.0.0'
        $Arguments | Should -Contain ('/O' + $Dir)
        $Arguments | Should -Contain '/FDSOptimizer-4.0.0-setup'
        $Arguments | Where-Object { $_ -like '/DProgramDir=*' } | Should -Not -BeNullOrEmpty
        $Arguments[-1] | Should -BeLike '*tools\release\DSOptimizer.iss'
    }

    It 'wyjście: ścieżka zipa, potem instalatora' {
        $Result = @(& (Get-ScriptPath -Repo $Repo) -Variant Folder -IsccPath $Iscc -OutRoot $OutRoot)

        $Result | Should -Be @((Join-Path -Path $Dir -ChildPath 'DSOptimizer-4.0.0.zip'), (Join-Path -Path $Dir -ChildPath 'DSOptimizer-4.0.0-setup.exe'))
    }

    It 'brak podanego ISCC = kod 1 i dotnet niewywołany' {
        $Result = & (Get-ScriptPath -Repo $Repo) -Variant Folder -IsccPath 'C:\nie-ma\ISCC.exe' -OutRoot $OutRoot 2>$null

        $LASTEXITCODE | Should -Be 1
        $Result | Should -BeNullOrEmpty
        Should -Invoke dotnet -Times 0 -Exactly
    }

    It 'DSO_ISCC wskazuje ISCC, gdy nie podano -IsccPath' {
        $Previous = $env:DSO_ISCC
        try {
            $env:DSO_ISCC = $Iscc
            $null = & (Get-ScriptPath -Repo $Repo) -Variant Folder -OutRoot $OutRoot

            $LASTEXITCODE | Should -Be 0
            Test-Path -LiteralPath (Join-Path -Path $Dir -ChildPath 'DSOptimizer-4.0.0-setup.exe') | Should -BeTrue
        }
        finally {
            $env:DSO_ISCC = $Previous
        }
    }

    It 'DSO_ISCC bez pliku = kod 1, dotnet niewywołany i bez dalszego szukania' {
        # Typowe katalogi instalacji wskazują pusty katalog: prawdziwy ISCC z hosta nie może się tu uruchomić.
        $Empty = Join-Path -Path $TestDrive -ChildPath ('puste-' + [guid]::NewGuid().ToString('N'))
        $null = New-Item -ItemType Directory -Path $Empty
        $Previous = @{ DSO_ISCC = $env:DSO_ISCC; LOCALAPPDATA = $env:LOCALAPPDATA; ProgramFiles = $env:ProgramFiles; ProgramFilesX86 = ${env:ProgramFiles(x86)} }
        try {
            $env:DSO_ISCC = 'C:\nie-ma\ISCC.exe'
            $env:LOCALAPPDATA = $Empty
            $env:ProgramFiles = $Empty
            ${env:ProgramFiles(x86)} = $Empty

            $Result = @(& (Get-ScriptPath -Repo $Repo) -Variant Folder -OutRoot $OutRoot 2>&1)

            $LASTEXITCODE | Should -Be 1
            $Messages = @($Result | Where-Object { $_ -is [System.Management.Automation.ErrorRecord] } | ForEach-Object { $_.Exception.Message })
            ($Messages -join "`n") | Should -Match ([regex]::Escape('DSO_ISCC: C:\nie-ma\ISCC.exe'))
            Should -Invoke dotnet -Times 0 -Exactly
            Test-Path -LiteralPath $Dir | Should -BeFalse
        }
        finally {
            $env:DSO_ISCC = $Previous.DSO_ISCC
            $env:LOCALAPPDATA = $Previous.LOCALAPPDATA
            $env:ProgramFiles = $Previous.ProgramFiles
            ${env:ProgramFiles(x86)} = $Previous.ProgramFilesX86
        }
    }

    It 'nieudany ISCC = kod 1, bez zipa, instalatora i SHA256SUMS.txt w katalogu wydania' {
        $Failing = New-FakeIscc -Fail

        $Result = & (Get-ScriptPath -Repo $Repo) -Variant Folder -IsccPath $Failing -OutRoot $OutRoot 2>$null

        $LASTEXITCODE | Should -Be 1
        $Result | Should -BeNullOrEmpty
        foreach ($Name in 'DSOptimizer-4.0.0.zip', 'DSOptimizer-4.0.0-setup.exe', 'SHA256SUMS.txt') {
            Test-Path -LiteralPath (Join-Path -Path $Dir -ChildPath $Name) | Should -BeFalse
        }
    }

    It '-NoInstaller: jak dziś, jedna linia w SHA256SUMS.txt i bez wywołania ISCC' {
        $Result = & (Get-ScriptPath -Repo $Repo) -Variant Folder -NoInstaller -IsccPath $Iscc -OutRoot $OutRoot

        $Result | Should -Be (Join-Path -Path $Dir -ChildPath 'DSOptimizer-4.0.0.zip')
        @(Get-ChildItem -LiteralPath $Dir | ForEach-Object { $_.Name } | Sort-Object) | Should -Be @('DSOptimizer-4.0.0.zip', 'SHA256SUMS.txt')
        [System.IO.File]::ReadAllText((Join-Path -Path $Dir -ChildPath 'SHA256SUMS.txt')) | Should -MatchExactly '^[0-9a-f]{64}  DSOptimizer-4\.0\.0\.zip\n$'
        Test-Path -LiteralPath (Join-Path -Path $TestDrive -ChildPath 'iscc-call.json') | Should -BeFalse
    }

    It 'SingleExe nie woła ISCC' {
        Mock dotnet { Write-FakeBuildOutput -Arguments $args; $global:LASTEXITCODE = 0 }

        $null = & (Get-ScriptPath -Repo $Repo) -Variant SingleExe -IsccPath $Iscc -OutRoot $OutRoot

        $LASTEXITCODE | Should -Be 0
        Test-Path -LiteralPath (Join-Path -Path $TestDrive -ChildPath 'iscc-call.json') | Should -BeFalse
    }
}

Describe 'Build jednego exe (prawdziwy dotnet build)' -Tag 'Build' {
    BeforeAll {
        $script:BuildOut = Join-Path -Path ([System.IO.Path]::GetTempPath()) -ChildPath ('dso-release-test-' + [guid]::NewGuid().ToString('N'))
        $Csproj = Join-Path -Path $script:RealRepoRoot -ChildPath 'src\DSOptimizer.App\DSOptimizer.App.csproj'
        # --no-incremental jak w Build-Release.ps1: build przyrostowy po wcześniejszym buildzie zostawiał biblioteki w katalogu -o (2026-10-26).
        $script:BuildLog = & dotnet build $Csproj -c Release /p:DsoSingleExe=true -o $script:BuildOut --no-incremental --nologo 2>&1 | Out-String
        $script:BuildExit = $LASTEXITCODE
    }

    AfterAll {
        if (Test-Path -LiteralPath $script:BuildOut) {
            Remove-Item -LiteralPath $script:BuildOut -Recurse -Force
        }
    }

    It 'dotnet build kończy się kodem 0' {
        $script:BuildExit | Should -Be 0 -Because $script:BuildLog
    }

    It 'w katalogu wyjściowym jest tylko DSOptimizer.exe (+ DSOptimizer.exe.config), bez Wpf.Ui.dll' {
        $Names = @(Get-ChildItem -LiteralPath $script:BuildOut -Recurse -File | Where-Object { $_.Extension -ne '.pdb' } | ForEach-Object { $_.Name } | Sort-Object)
        $Names | Should -Be @('DSOptimizer.exe', 'DSOptimizer.exe.config')
        Test-Path -LiteralPath (Join-Path -Path $script:BuildOut -ChildPath 'Wpf.Ui.dll') | Should -BeFalse
    }

    It 'Costura osadza biblioteki bez kompresji i bez symboli debugowania' {
        $Exe = Join-Path -Path $script:BuildOut -ChildPath 'DSOptimizer.exe'
        # Osobny proces Windows PowerShell 5.1: ReflectionOnlyLoad z bajtów czyta metadane bez wykonywania kodu i bez blokady pliku.
        $Command = "[System.Reflection.Assembly]::ReflectionOnlyLoad([System.IO.File]::ReadAllBytes('$Exe')).GetManifestResourceNames()"
        $Resources = @(& "$env:SystemRoot\System32\WindowsPowerShell\v1.0\powershell.exe" -NoProfile -NonInteractive -Command $Command)
        $Costura = @($Resources | Where-Object { $_ -like 'costura.*' })

        $Costura | Should -Contain 'costura.wpf.ui.dll'
        $Costura | Should -Contain 'costura.newtonsoft.json.dll'
        $Costura | Should -Contain 'costura.dsoptimizer.core.dll'
        $Costura | Should -Not -Contain 'costura.costura.dll'
        $Costura | Where-Object { $_ -like '*.compressed' } | Should -BeNullOrEmpty
        $Costura | Where-Object { $_ -like '*.pdb' } | Should -BeNullOrEmpty
    }
}
