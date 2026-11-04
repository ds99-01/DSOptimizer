; Instalator DSOptimizera (Inno Setup 6).
;
; Instrukcja uruchomienia (kompilacja, z katalogu repo):
;   ISCC.exe /DAppVersion=<X.Y.Z> /DProgramDir=<katalog buildu> /O<katalog wyjścia> /FDSOptimizer-<X.Y.Z>-setup /Q tools\release\DSOptimizer.iss
;
; Opis działania:
;   Instaluje pliki programu z katalogu buildu (bez .pdb) tylko dla wszystkich użytkowników, wyłącznie do
;   Program Files\DSOptimizer (folder zapisywalny tylko przez administratora; program zawsze działa jako administrator).
;   Inny folder (/DIR, winget --location) = odmowa. Działający DSOptimizer (zainstalowany albo przenośny, mutex
;   programu) = odmowa instalacji, aktualizacji i deinstalacji, zanim cokolwiek się zmieni.
;   Instalator i deinstalator nie tworzą, nie zmieniają i nie usuwają %ProgramData%\DSOptimizer: historia zmian
;   zostaje po odinstalowaniu. Ikona na pulpicie tylko przy pierwszej instalacji.

#ifndef AppVersion
#error Podaj /DAppVersion=X.Y.Z
#endif
#ifndef ProgramDir
#error Podaj /DProgramDir=<katalog buildu>
#endif
#define ProgramMutex "Global\DSOptimizer.SingleInstance"

[Setup]
AppId={{82DBAF33-36AA-4F12-A327-EEC64159D8E8}
AppName=DSOptimizer
AppVersion={#AppVersion}
AppPublisher=Dawid Świeboda
AppPublisherURL=https://dsoptimizer.com/
AppSupportURL=https://dsoptimizer.com/
AppUpdatesURL=https://github.com/ds99-01/DSOptimizer/releases
UninstallDisplayName=DSOptimizer
UninstallDisplayIcon={app}\DSOptimizer.exe
DefaultDirName={autopf}\DSOptimizer
DisableDirPage=yes
DisableProgramGroupPage=yes
DisableWelcomePage=no
PrivilegesRequired=admin
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
CloseApplications=yes
RestartApplications=no
ShowLanguageDialog=no
WizardStyle=modern
SetupLogging=yes
SetupIconFile=..\..\src\DSOptimizer.App\Assets\dsoptimizer.ico
VersionInfoVersion={#AppVersion}
VersionInfoProductName=DSOptimizer
VersionInfoCompany=Dawid Świeboda
OutputDir=..\..\wydania\inno
OutputBaseFilename=DSOptimizer-{#AppVersion}-setup

[Languages]
Name: "en"; MessagesFile: "compiler:Default.isl"
Name: "pl"; MessagesFile: "compiler:Languages\Polish.isl"

[Messages]
en.WelcomeLabel2=This will install [name/ver] in the Program Files folder, for all users of this computer.%n%nDSOptimizer is free software under the Mozilla Public License 2.0: https://github.com/ds99-01/DSOptimizer/blob/main/LICENSE%n%nUninstalling it keeps the change history in the ProgramData folder, so earlier changes can still be undone.
pl.WelcomeLabel2=Zostanie zainstalowany [name/ver] w folderze Program Files, dla wszystkich użytkowników tego komputera.%n%nDSOptimizer to darmowy program na licencji Mozilla Public License 2.0: https://github.com/ds99-01/DSOptimizer/blob/main/LICENSE%n%nOdinstalowanie zostawia historię zmian w folderze ProgramData, więc wcześniejsze zmiany nadal da się cofnąć.

[CustomMessages]
en.ProgramRunning=DSOptimizer is running (the installed or the portable version). Close every DSOptimizer window, wait until command-line commands finish, then run the installer again. Nothing was changed.
pl.ProgramRunning=DSOptimizer jest uruchomiony (wersja zainstalowana albo przenośna). Zamknij wszystkie okna DSOptimizera, poczekaj na koniec poleceń w trybie wiersza poleceń i uruchom instalator ponownie. Nic nie zostało zmienione.
en.ProgramRunningUninstall=DSOptimizer is running. Close every DSOptimizer window, wait until command-line commands finish, then uninstall it again. Nothing was removed.
pl.ProgramRunningUninstall=DSOptimizer jest uruchomiony. Zamknij wszystkie okna DSOptimizera, poczekaj na koniec poleceń w trybie wiersza poleceń i odinstaluj go ponownie. Nic nie zostało usunięte.
en.WrongFolder=DSOptimizer can be installed only in %1. Another folder (for example from /DIR or winget --location) is not supported. Nothing was changed.
pl.WrongFolder=DSOptimizera można zainstalować tylko w folderze %1. Inny folder (na przykład z /DIR albo winget --location) nie jest obsługiwany. Nic nie zostało zmienione.

[Tasks]
; Bez flag: zaznaczone także przy instalacji cichej. Przy aktualizacji zadania nie ma, więc usunięta ikona nie wraca.
Name: "desktopicon"; Description: "{cm:CreateDesktopIcon}"; GroupDescription: "{cm:AdditionalIcons}"; Check: IsFirstInstall

[Files]
Source: "{#ProgramDir}\*"; DestDir: "{app}"; Excludes: "*.pdb"; Flags: ignoreversion recursesubdirs createallsubdirs

[Icons]
Name: "{autoprograms}\DSOptimizer"; Filename: "{app}\DSOptimizer.exe"; WorkingDir: "{app}"
Name: "{autodesktop}\DSOptimizer"; Filename: "{app}\DSOptimizer.exe"; WorkingDir: "{app}"; Tasks: desktopicon

[Run]
; postinstall startuje na koncie, które uruchomiło instalator; shellexec, bo exe wymaga administratora (UAC).
Filename: "{app}\DSOptimizer.exe"; Description: "{cm:LaunchProgram,DSOptimizer}"; WorkingDir: "{app}"; Flags: postinstall skipifsilent nowait shellexec

[Code]
// Pierwsza instalacja = brak folderu poprzedniej instalacji o tym AppId.
function IsFirstInstall: Boolean;
begin
  Result := WizardForm.PrevAppDir = '';
end;

// Niepusty wynik zatrzymuje instalację przed jakąkolwiek zmianą (kod wyjścia 7).
function PrepareToInstall(var NeedsRestart: Boolean): String;
var
  ExpectedDir: String;
begin
  ExpectedDir := ExpandConstant('{autopf}\DSOptimizer');
  if CompareText(RemoveBackslashUnlessRoot(WizardDirValue), RemoveBackslashUnlessRoot(ExpectedDir)) <> 0 then
  begin
    Result := FmtMessage(CustomMessage('WrongFolder'), [ExpectedDir]);
    exit;
  end;
  if CheckForMutexes('{#ProgramMutex}') then
  begin
    Result := CustomMessage('ProgramRunning');
    exit;
  end;
  Result := '';
end;

function InitializeUninstall(): Boolean;
begin
  if CheckForMutexes('{#ProgramMutex}') then
  begin
    SuppressibleMsgBox(CustomMessage('ProgramRunningUninstall'), mbError, MB_OK, IDOK);
    Result := False;
    exit;
  end;
  Result := True;
end;
