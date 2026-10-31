<#
Instrukcja uruchomienia:
    Sesja PowerShell uruchomiona jako administrator, na hoście z Hyper-V. VM utworzona przez New-TestVm.ps1 (checkpoint 'czysta-26H2').
    Przed przebiegiem zbuduj aplikację:
        dotnet build DSOptimizer.sln -c Release
    Domyślnie (DSO-26H2-Pro, na końcu revert i VM Off):
        .\tests\vm\Invoke-GuiSmoke.ps1 -OutDirectory $env:TEMP\dso-explore\smoke
    Home, albo Pro z oknem zostawionym do ręcznego przeklikania (VMConnect), bez revertu:
        .\tests\vm\Invoke-GuiSmoke.ps1 -VmName DSO-26H2-Home -OutDirectory $env:TEMP\dso-explore\smoke
        .\tests\vm\Invoke-GuiSmoke.ps1 -OutDirectory $env:TEMP\dso-explore\smoke -KeepRunning
    Wybór języka w pierwszym oknie (domyślnie polski; angielski np. do przebiegu Etapu 5):
        .\tests\vm\Invoke-GuiSmoke.ps1 -OutDirectory $env:TEMP\dso-explore\smoke -Language en
Opis działania:
    Revert do czystego checkpointu, start VM, kopia builda, start C:\dso\app\DSOptimizer.exe w sesji interaktywnej 'dso'
    (zadanie Harmonogramu), wybór języka w pierwszym oknie (-Language pl|en, sprawdzenie LanguageChoice), po -StartupSeconds (domyślnie 45 s, koniec detekcji startowej) sprawdza: proces żyje, Responding=True
    (z oknem głównym), brak zdarzeń .NET Runtime / Application Error / WER od startu, brak linii ERROR w
    %ProgramData%\DSOptimizer\logs\*.log. Sonda UI (UI Automation w sesji 'dso', bez myszy): pozycja menu „Historia zmian” otwiera
    stronę (HistoryMenu); po zastosowaniu „Szybsze otwieranie podmenu” (ux.menu-delay, safe, „Zastosuj (1)” bez potwierdzenia) wiersz
    historii ma stan „Aktywna” (HistoryEntryActive); w stanie normalnym i po maksymalizacji okna WM_NCHITTEST daje HTCAPTION w środku paska marki i 2 px nad
    jego dolną krawędzią, a HTCLIENT 2 px pod nim (CaptionHitTest); prawdziwa mysz rozwija filtry „Ryzyko” i „Filtr” i zmienia ich wartość
    (FilterRiskOpened/Selected, FilterHistoryOpened/Selected); klik w tytuł wiersza tweaka tylko go wybiera, dwuklik przełącza pole wyboru i drugi wraca,
    klik i dwuklik w znacznik uruchomienia panelu „Zarządzanie komputerem” otwierają dokładnie jedno okno mmc.exe (RowClickPreview, RowDoubleClickToggles,
    RunIconRuns, RunIconDoubleClickOnce). Na końcu sonda wchodzi na „Prywatność i AI” (dopiero po tamtych krokach, bo pierwsze wejście
    zaznacza sugestie do serii) i sprawdza „Zastosuj (N)”, gdzie N = sugerowane pozycje NotApplied/Partial z raportów CLI --detect tego
    przebiegu, uruchomionych przed sondą (PrivacySuggestedApply). Potem sonda wchodzi na „Aplikacje” i po zakończonym odczycie listy
    sprawdza „Usuń (N)”, gdzie N = wiersze grupy Suggested z Removability=Removable z CLI --list-apps tego przebiegu, uruchomionego przed sondą
    jako 'dso' - to samo konto co okno (AppsSuggestedRemove); niczego nie usuwa ani nie klika. Potem sonda wchodzi na „Przygotowanie”
    i po zakończonym odczycie (najwyżej 60 s, bez „Wczytywanie…”) sprawdza 7 tytułów kroków, przycisk „Otwórz Windows Update” (albo notatkę
    zastępczą, gdy PrepLinks.SettingsOpenable = false) i linię karty w kroku „Grafika” (PrepPage); żadnego linku nie klika. Jako ostatnie sonda wchodzi na „Instalacja” (kategorie i tytuły
    z apps.json, „Zainstaluj (0)” nieaktywne – InstallPage); niczego nie zaznacza ani nie instaluje.
    Zrzuty ekranu (Msvm_VirtualSystemManagementService.
    GetVirtualSystemThumbnailImage) po starcie i po sondzie UI oraz wynik (gui-smoke-<VM>.json) trafiają do -OutDirectory.
    Jedyna operacja to ta jedna bezpieczna pozycja (VM i tak wraca do checkpointu). Błąd uwierzytelnienia PowerShell Direct przerywa
    przebieg od razu (bez ponawiania). Bez -KeepRunning na końcu revert i VM Off. Kod wyjścia: 0 gdy wszystkie sprawdzenia
    przeszły, 1 w przeciwnym razie. Dotyka wyłącznie DSO-26H2-Home i DSO-26H2-Pro (inne nazwy są odrzucane).
#>
[CmdletBinding()]
param(
    [ValidateSet('DSO-26H2-Home', 'DSO-26H2-Pro')][string]$VmName = 'DSO-26H2-Pro',
    [Parameter(Mandatory)][ValidateNotNullOrEmpty()][string]$OutDirectory,
    [switch]$KeepRunning,
    [string]$BuildDirectory,
    [string]$CatalogPath,
    [string]$CheckpointName = 'czysta-26H2',
    [ValidateRange(60, 7200)][int]$ReadyTimeoutSeconds = 1200,
    [ValidateRange(0, 600)][int]$StartupSeconds = 45,
    [ValidateSet('pl', 'en')][string]$Language = 'pl'
)
Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

$ExitCode = 1
try {
    $VmRoot = $PSScriptRoot
    Import-Module (Join-Path $VmRoot 'modules\VmCommon.psm1') -Force
    Import-Module (Join-Path $VmRoot 'modules\VmHost.psm1') -Force
    Import-Module (Join-Path $VmRoot 'modules\VmSmoke.psm1') -Force
    $RepoRoot = Get-DsoRepoRoot
    if (-not $BuildDirectory) { $BuildDirectory = Join-Path $RepoRoot 'src\DSOptimizer.App\bin\Release\net481' }
    if (-not $CatalogPath) { $CatalogPath = Join-Path $VmRoot 'catalog\vm-test-catalog.json' }

    $Principal = New-Object System.Security.Principal.WindowsPrincipal([System.Security.Principal.WindowsIdentity]::GetCurrent())
    if (-not $Principal.IsInRole([System.Security.Principal.WindowsBuiltInRole]::Administrator)) {
        throw 'Uruchom skrypt w sesji PowerShell jako administrator (Hyper-V i PowerShell Direct tego wymagają).'
    }
    if (-not (Test-Path -LiteralPath $CatalogPath -PathType Leaf)) { throw "Brak katalogu testowego '$CatalogPath'." }

    $Result = Invoke-DsoGuiSmoke -VmName $VmName -OutDirectory $OutDirectory -BuildDirectory $BuildDirectory -CatalogPath $CatalogPath `
        -KeepRunning:$KeepRunning -CheckpointName $CheckpointName -ReadyTimeoutSeconds $ReadyTimeoutSeconds -StartupSeconds $StartupSeconds -Language $Language
    foreach ($Check in $Result.checks) {
        $Mark = if ($Check.Passed) { 'OK  ' } else { 'FAIL' }
        $Color = if ($Check.Passed) { 'Green' } else { 'Red' }
        Write-Host "[$Mark] $($Check.Name): $($Check.Detail)" -ForegroundColor $Color
    }
    foreach ($Message in $Result.errors) { Write-Host "BŁĄD $Message" -ForegroundColor Red }
    Write-Host "Zrzut: $($Result.screenshotPath)"
    Write-Host "Zrzut po sondzie UI: $($Result.uiScreenshotPath)"
    Write-Host "Wynik: $($Result.resultPath)"
    $ExitCode = if ($Result.passed) { 0 } else { 1 }
}
catch {
    Write-Host "BŁĄD: $($_.Exception.Message)" -ForegroundColor Red
    Write-Host $_.ScriptStackTrace -ForegroundColor DarkGray
    $ExitCode = 1
}
exit $ExitCode
