<#
Instrukcja uruchomienia:
    W VM (Setup przypadku app-win32-msi w tests\vm\cases.psd1), jako Administrator:
        & C:\dso\fixtures\New-DsoTestMsi.ps1 -Path C:\dso\fixtures\DsoTestMsi.msi
    Instalacja to osobny krok (też tylko w VM): msiexec.exe /i C:\dso\fixtures\DsoTestMsi.msi /qn /norestart
Opis działania:
    Buduje przez COM WindowsInstaller.Installer (bez sieci, bez WiX) pakiet MSI x64 bez plików: jedna funkcja, jeden komponent
    64-bitowy z jedną wartością rejestru HKLM\SOFTWARE\DSOptimizer.VmTests\Msi\Installed = 1 (ścieżka klucza komponentu).
    Stały ProductCode (= nazwa klucza Uninstall wpisu ARP, czyli id aplikacji
    win32:HKLM\SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall\{ProductCode}, bez spacji); nowy PackageCode przy każdym budowaniu.
    Skrypt tylko tworzy plik .msi - niczego nie instaluje.
#>
[CmdletBinding()]
param(
    [Parameter(Mandatory)][ValidatePattern('^[A-Za-z]:\\[^\s]+\.msi\z')][string]$Path
)
Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

$ProductCode = '{4E3B2A10-7C5D-4F8E-9A1B-2C3D4E5F6A70}'
$UpgradeCode = '{4E3B2A11-7C5D-4F8E-9A1B-2C3D4E5F6A71}'
$ComponentId = '{4E3B2A12-7C5D-4F8E-9A1B-2C3D4E5F6A72}'
$PackageCode = '{' + [guid]::NewGuid().ToString().ToUpperInvariant() + '}'

# Schemat standardowych tabel MSI (tylko potrzebne kolumny i typy z dokumentacji Windows Installer).
$Statements = @(
    'CREATE TABLE `Property` (`Property` CHAR(72) NOT NULL, `Value` LONGCHAR NOT NULL LOCALIZABLE PRIMARY KEY `Property`)'
    'CREATE TABLE `Directory` (`Directory` CHAR(72) NOT NULL, `Directory_Parent` CHAR(72), `DefaultDir` CHAR(255) NOT NULL LOCALIZABLE PRIMARY KEY `Directory`)'
    'CREATE TABLE `Component` (`Component` CHAR(72) NOT NULL, `ComponentId` CHAR(38), `Directory_` CHAR(72) NOT NULL, `Attributes` SHORT NOT NULL, `Condition` CHAR(255), `KeyPath` CHAR(72) PRIMARY KEY `Component`)'
    'CREATE TABLE `Feature` (`Feature` CHAR(38) NOT NULL, `Feature_Parent` CHAR(38), `Title` CHAR(64) LOCALIZABLE, `Description` CHAR(255) LOCALIZABLE, `Display` SHORT, `Level` SHORT NOT NULL, `Directory_` CHAR(72), `Attributes` SHORT NOT NULL PRIMARY KEY `Feature`)'
    'CREATE TABLE `FeatureComponents` (`Feature_` CHAR(38) NOT NULL, `Component_` CHAR(72) NOT NULL PRIMARY KEY `Feature_`, `Component_`)'
    'CREATE TABLE `Registry` (`Registry` CHAR(72) NOT NULL, `Root` SHORT NOT NULL, `Key` CHAR(255) NOT NULL LOCALIZABLE, `Name` CHAR(255) LOCALIZABLE, `Value` LONGCHAR LOCALIZABLE, `Component_` CHAR(72) NOT NULL PRIMARY KEY `Registry`)'
    'CREATE TABLE `InstallExecuteSequence` (`Action` CHAR(72) NOT NULL, `Condition` CHAR(255), `Sequence` SHORT PRIMARY KEY `Action`)'
    # RegisterProduct czyta tabelę Media także dla pakietu bez plików (bez niej msiexec /i = 1603, błąd 2228; pomiar VM).
    'CREATE TABLE `Media` (`DiskId` SHORT NOT NULL, `LastSequence` SHORT NOT NULL, `DiskPrompt` CHAR(64) LOCALIZABLE, `Cabinet` CHAR(255), `VolumeLabel` CHAR(32), `Source` CHAR(72) PRIMARY KEY `DiskId`)'
    "INSERT INTO ``Media`` (``DiskId``, ``LastSequence``) VALUES (1, 0)"
    "INSERT INTO ``Property`` (``Property``, ``Value``) VALUES ('ProductCode', '$ProductCode')"
    "INSERT INTO ``Property`` (``Property``, ``Value``) VALUES ('UpgradeCode', '$UpgradeCode')"
    "INSERT INTO ``Property`` (``Property``, ``Value``) VALUES ('ProductName', 'DSO Test MSI')"
    "INSERT INTO ``Property`` (``Property``, ``Value``) VALUES ('ProductVersion', '1.0.0')"
    "INSERT INTO ``Property`` (``Property``, ``Value``) VALUES ('ProductLanguage', '1033')"
    "INSERT INTO ``Property`` (``Property``, ``Value``) VALUES ('Manufacturer', 'DSOptimizer VM Tests')"
    "INSERT INTO ``Property`` (``Property``, ``Value``) VALUES ('ALLUSERS', '1')"
    "INSERT INTO ``Directory`` (``Directory``, ``DefaultDir``) VALUES ('TARGETDIR', 'SourceDir')"
    # 260 = msidbComponentAttributesRegistryKeyPath (4) + msidbComponentAttributes64bit (256).
    "INSERT INTO ``Component`` (``Component``, ``ComponentId``, ``Directory_``, ``Attributes``, ``KeyPath``) VALUES ('DsoTestMsiRegistry', '$ComponentId', 'TARGETDIR', 260, 'DsoTestMsiValue')"
    "INSERT INTO ``Feature`` (``Feature``, ``Title``, ``Display``, ``Level``, ``Attributes``) VALUES ('Main', 'DSO Test MSI', 0, 1, 0)"
    "INSERT INTO ``FeatureComponents`` (``Feature_``, ``Component_``) VALUES ('Main', 'DsoTestMsiRegistry')"
    # Root 2 = HKEY_LOCAL_MACHINE.
    "INSERT INTO ``Registry`` (``Registry``, ``Root``, ``Key``, ``Name``, ``Value``, ``Component_``) VALUES ('DsoTestMsiValue', 2, 'SOFTWARE\DSOptimizer.VmTests\Msi', 'Installed', '1', 'DsoTestMsiRegistry')"
)
$Sequence = [ordered]@{
    CostInitialize = 800; CostFinalize = 1000; InstallValidate = 1400; InstallInitialize = 1500; ProcessComponents = 1600
    UnpublishFeatures = 1800; RemoveRegistryValues = 2600; WriteRegistryValues = 5000; RegisterProduct = 6100
    PublishFeatures = 6300; PublishProduct = 6400; InstallFinalize = 6600
}
foreach ($Action in $Sequence.Keys) {
    $Statements += "INSERT INTO ``InstallExecuteSequence`` (``Action``, ``Sequence``) VALUES ('$Action', $($Sequence[$Action]))"
}

if (Test-Path -LiteralPath $Path) { Remove-Item -LiteralPath $Path -Force }
$Installer = New-Object -ComObject WindowsInstaller.Installer
$Database = $null
$View = $null
$Summary = $null
try {
    $Database = $Installer.OpenDatabase($Path, 3)  # 3 = msiOpenDatabaseModeCreate
    foreach ($Sql in $Statements) {
        $View = $Database.OpenView($Sql)
        $null = $View.Execute()
        $null = $View.Close()
        $null = [System.Runtime.InteropServices.Marshal]::ReleaseComObject($View)
        $View = $null
    }
    $Flags = [System.Reflection.BindingFlags]
    $Summary = $Database.GetType().InvokeMember('SummaryInformation', $Flags::GetProperty, $null, $Database, @(20))
    # PID: 2 Title, 3 Subject, 4 Author, 7 Template (x64 = pakiet 64-bit, wpis ARP w natywnym widoku), 9 PackageCode,
    # 14 schemat (x64 wymaga >= 200), 15 Word Count (2 = pliki skompresowane; pakiet nie ma plików).
    $Properties = @(@(2, 'DSO Test MSI'), @(3, 'DSOptimizer VM Tests'), @(4, 'DSOptimizer VM Tests'), @(7, 'x64;1033'), @(9, $PackageCode), @(14, [int]200), @(15, [int]2))
    foreach ($Pair in $Properties) {
        $null = $Summary.GetType().InvokeMember('Property', $Flags::SetProperty, $null, $Summary, @([int]$Pair[0], $Pair[1]))
    }
    $null = $Summary.GetType().InvokeMember('Persist', $Flags::InvokeMethod, $null, $Summary, $null)
    $null = $Database.Commit()
}
finally {
    # Obiekty COM trzymają uchwyt pliku MSI do GC: zwalniamy wszystkie także po błędzie, potem GC (plik da się od razu usunąć).
    foreach ($Com in @($View, $Summary, $Database)) {
        if ($null -ne $Com) { $null = [System.Runtime.InteropServices.Marshal]::ReleaseComObject($Com) }
    }
    $null = [System.Runtime.InteropServices.Marshal]::ReleaseComObject($Installer)
    [GC]::Collect()
    [GC]::WaitForPendingFinalizers()
}
[pscustomobject]@{ Path = $Path; ProductCode = $ProductCode }
