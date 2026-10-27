<#
Instrukcja uruchomienia:
    Moduł ładuje tests\vm\modules\VmSmoke.psm1 (smoke GUI, tests\vm\Invoke-GuiSmoke.ps1); nie uruchamia się go osobno.
    Testy (na mockach, bez VM): Invoke-Pester -Path tests/vm/tests/VmSmokeUi.Tests.ps1
Opis działania:
    Część smoke GUI, która dotyka okna (partia D Etapu 3): skrypt sondy UI uruchamiany W VM w sesji użytkownika 'dso'
    (UI Automation + user32, w kroku 5 mysz) oraz czysta agregacja jego wyniku na hoście. Sonda:
      1. szuka pozycji menu „Historia zmian” (UIA, po nazwie);
      2. stosuje jedną bezpieczną pozycję jak użytkownik: kategoria „Explorer i wygoda” → pole wyboru „Szybsze otwieranie podmenu”
         (ux.menu-delay, safe, bez restartu, bez okna potwierdzenia) → „Zastosuj (1)”, czeka na koniec operacji;
      3. otwiera „Historia zmian” i czyta nazwy wierszy listy (AutomationName wiersza = „Tytuł, rodzaj wpisu (kategoria), Stan[ – powód], data”; sonda szuka „Tytuł, *, Aktywna, *”);
      4. mierzy WM_NCHITTEST na pasku marki (44 DIP) w stanie normalnym i po maksymalizacji: punkt w środku paska, 2 px nad
         jego dolną krawędzią i 2 px pod nią (nad treścią), plus pionowy przekrój od 12 px nad paskiem do 12 px pod nim.
         Oczekiwane w obu stanach: HTCAPTION (2) w pasku, HTCLIENT (1) pod nim. Okno zostaje zmaksymalizowane (zrzut).
      5. klika PRAWDZIWĄ myszą (SetCursorPos + mouse_event, środek prostokąta z UIA) dwa filtry: „Ryzyko” (kategoria sondy)
         i „Filtr” („Historia zmian”): lista ma się rozwinąć (ExpandCollapseState = Expanded do 1 s), klik w drugą opcję zmienia wybraną
         wartość, klik w pierwszą wraca (FilterRiskOpened/Selected, FilterHistoryOpened/Selected, FilterError). Wzorce UIA same by tego
         nie wykryły: Invoke/Expand omijają hit-test, a błąd z v3.4 (nakładka ToggleButton 0x0) łapie tylko prawdziwy klik.
          6. wiersze listy prawdziwą myszą: tweak - klik w tytuł tylko wybiera wiersz (RowClickPreview), dwuklik przełącza pole wyboru, drugi
         wraca (RowDoubleClickToggles); panel „Zarządzanie komputerem” - klik w znacznik uruchomienia otwiera dokładnie jedno nowe okno
         mmc.exe (RunIconRuns), dwuklik w niego też dokładnie jedno (RunIconDoubleClickOnce); okna mmc.exe sonda zamyka.
      7. DOPIERO TERAZ wchodzi na „Prywatność i AI” (pierwsze wejście zaznacza sugestie do globalnej serii, więc wcześniej zepsułoby
         „Zastosuj (1)” z kroku 2) i czyta N z „Zastosuj (N)” przy bezczynnej aplikacji, po 3 s stałej wartości; nic nie stosuje.
         Host porównuje N z liczbą sugerowanych pozycji NotApplied/Partial z raportów CLI --detect tego przebiegu (PrivacySuggestedApply).
      8. Na samym końcu wchodzi na „Aplikacje” (pierwsze wejście czyta listę i zaznacza sugerowane aplikacje do osobnej serii „Usuń (N)”)
         i czyta N po zakończonym odczycie listy („Odśwież listę” aktywne, podtytuł bez „Odczyt listy w toku”), po 3 s stałej wartości;
         niczego nie klika. Host porównuje N z liczbą wierszy grupy Suggested z Removability=Removable (AppRow.CanRemove) z CLI
         --list-apps tego przebiegu uruchomionego jako to samo konto co okno (AppsSuggestedRemove).
      9. Po „Aplikacjach” wchodzi na „Przygotowanie” (Etap 4a partia 3) i czeka najwyżej 60 s, aż zniknie „Wczytywanie…” i będzie widać
         7 tytułów kroków; sprawdza przycisk „Otwórz Windows Update” (albo notatkę zastępczą, gdy PrepLinks.SettingsOpenable = false)
         i co najmniej jedną linię karty w kroku „Grafika” (PrepPage). Niczego nie klika – linki otwiera się ręcznie.
     10. Potem (Etap 4b partia 1) wchodzi na „Naprawy” i czyta teksty listy „Pozycje” (także po przewinięciu do końca);
         host sprawdza, że są wszystkie tytuły pozycji kategorii repair z katalogu (RepairPage, Get-GuiRepairTitle). Niczego nie
         zaznacza, nie klika ani nie uruchamia – żadna naprawa nie startuje.
     11. Na samym końcu (Etap 4c) wchodzi na „Instalacja”, czeka na koniec odczytu (najwyżej 180 s), wybiera po kolei każdą kategorię
         (nawigacja wzorcem SelectionItemPattern; kolejność: „Wszystkie”, „Zaznaczone”, kategorie z apps.json; na końcu wraca na „Wszystkie”)
         i czyta teksty listy „Aplikacje do instalacji” (z przewijaniem) oraz napis i stan
         przycisku „Zainstaluj (N)”; host sprawdza wszystkie kategorie i tytuły z apps.json i nieaktywne „Zainstaluj (0)” (InstallPage,
         Get-GuiInstallTitle). Bez zaznaczania, instalowania i wyszukiwania (VM bez sieci).
    Kroki 1-4 bez kliknięć myszą (wzorce UIA i SendMessageTimeout). Dotyka wyłącznie VM; na hoście nic nie zmienia.
#>
Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

# Pozycja stosowana przez sondę: ux.menu-delay (risk=safe, scope=user, bez restartu) – jedna bezpieczna pozycja bez potwierdzenia.
$script:UiProbeCategory = 'Explorer i wygoda'
$script:UiProbeTweakTitle = 'Szybsze otwieranie podmenu'
$script:HtClient = 1
$script:HtCaption = 2
# Strona „Przygotowanie” (krok 9 sondy): tytuły kroków = PrepText.*Title, SettingsOpenable = PrepLinks.SettingsOpenable (zgodność pilnują
# testy VmSmokeUi.Tests.ps1, czytając źródła Core). Linia z etykietą „Karty graficzne” (PrepText.GraphicsCardsLabel) to stan kroku 5
# (odczyt w toku, błąd odczytu, brak karty), nigdy linia karty.
$script:PrepStepTitles = @('Komputer', 'Windows Update', 'Opcjonalne aktualizacje sterowników', 'Sterowniki chipsetu', 'Grafika', 'Wsparcie producenta', 'Urządzenia bez sterownika')
$script:PrepSettingsOpenable = $true
$script:PrepCardsStateLabel = 'Karty graficzne'
# Strona „Instalacja” (krok 11 sondy): kategoria apps.json -> nazwa w UI (InstallText.CategoryName), w kolejności strony (InstallCategory);
# zgodność z Core pilnuje VmSmokeUi.Tests.ps1. Przycisk serii bez zaznaczenia (sonda niczego nie zaznacza).
$script:InstallCategoryNames = [ordered]@{
    'browsers' = 'Przeglądarki'; 'messengers' = 'Komunikatory'; 'multimedia' = 'Multimedia'; 'documents' = 'Dokumenty'
    'tools' = 'Narzędzia'; 'remote-help' = 'Zdalna pomoc'; 'games' = 'Gry'; 'runtimes' = 'Środowiska uruchomieniowe'
}
# Pierwsza pozycja listy kategorii (D1 planu RustDesk, InstallText.AllCategoriesName): cała lista z nagłówkami kategorii.
$script:InstallAllCategoriesName = 'Wszystkie'
# Druga pozycja listy kategorii (InstallText.SelectedGroupName): zaznaczone aplikacje; bez zaznaczania sonda widzi ja pusta.
$script:InstallSelectedCategoryName = 'Zaznaczone'
$script:InstallButtonIdle = 'Zainstaluj (0)'
# Podtytuł przy działającym winget i liście repo z RustDeskiem (InstallText.PageSummary z GitHubem, M6 review Tasku 5 planu RustDesk).
$script:InstallSourcePrefix = 'Źródła: winget (Microsoft) i GitHub (RustDesk) · '

function Get-GuiUiProbeTarget {
    # Kategoria i tytuł pozycji, którą stosuje sonda UI (jedno źródło dla sondy i sprawdzeń).
    [CmdletBinding()]
    [OutputType([hashtable])]
    param()
    return @{ Category = $script:UiProbeCategory; TweakTitle = $script:UiProbeTweakTitle }
}

function Get-GuiPrepExpected {
    # Oczekiwania sprawdzenia PrepPage: tytuły 7 kroków (w kolejności) i to, czy krok 2 ma przycisk (true) czy notatkę zastępczą (false).
    [CmdletBinding()]
    [OutputType([hashtable])]
    param()
    return @{ Titles = $script:PrepStepTitles; SettingsOpenable = $script:PrepSettingsOpenable }
}

function Get-GuiUiProbeScript {
    # Skrypt sondy UI uruchamiany W VM w sesji użytkownika 'dso' (tylko ASCII, PowerShell 5.1). Zapisuje JSON do -ResultFile.
    # Skrypt trzymamy w czystym ASCII (polskie znaki w nazwach UIA składane z kodów znaków), żeby nie zależeć od kodowania pliku
    # zapisanego w VM ani od strony kodowej, z którą PowerShell 5.1 go czyta.
    [CmdletBinding()]
    [OutputType([string])]
    param()
    return @'
param([Parameter(Mandatory)][string]$ResultFile, [Parameter(Mandatory)][string]$Category, [Parameter(Mandatory)][string]$TweakTitle)
$ErrorActionPreference = 'Stop'
$Out = [ordered]@{
    Ok = $false; Error = $null; Steps = @(); Dpi = $null
    HistoryMenuFound = $false; ApplyInvoked = $false; ApplyBusySeen = $false; ApplySeconds = $null; ApplyError = $null
    HistoryPageOpened = $false; EntryNames = @(); HistoryError = $null
    HitTest = @(); HitTestError = $null
    FilterRiskOpened = $false; FilterRiskSelected = $false; FilterHistoryOpened = $false; FilterHistorySelected = $false; FilterError = $null
    RowClickPreview = $false; RowDoubleClickToggles = $false; RunIconRuns = $false; RunIconDoubleClickOnce = $false; RowError = $null
    PrivacyButtonFound = $false; PrivacyApplyCount = $null; PrivacyApplyText = $null; PrivacyError = $null
    AppsListRead = $false; AppsButtonFound = $false; AppsRemoveCount = $null; AppsRemoveText = $null; AppsError = $null
    PrepMenuFound = $false; PrepLoaded = $false; PrepTitles = @(); PrepWindowsUpdateButton = $false; PrepWindowsUpdateNote = $false; PrepGraphicsLines = @(); PrepError = $null
    RepairMenuFound = $false; RepairListFound = $false; RepairTexts = @(); RepairError = $null
    InstallMenuFound = $false; InstallLoaded = $false; InstallSubtitle = $null; InstallCategories = @(); InstallTexts = @()
    InstallButtonText = $null; InstallButtonEnabled = $null; InstallError = $null
}
function Add-Step([string]$Text) { $script:Out.Steps += ('{0:HH:mm:ss.fff} {1}' -f (Get-Date), $Text) }
try {
    Add-Type -AssemblyName UIAutomationClient
    Add-Type -AssemblyName UIAutomationTypes
    Add-Type -TypeDefinition @"
using System;
using System.Runtime.InteropServices;
public static class DsoUser32 {
    [StructLayout(LayoutKind.Sequential)] public struct RECT { public int Left, Top, Right, Bottom; }
    [DllImport("user32.dll")] public static extern bool ShowWindow(IntPtr hWnd, int nCmdShow);
    [DllImport("user32.dll")] public static extern bool GetWindowRect(IntPtr hWnd, out RECT rect);
    [DllImport("user32.dll")] public static extern uint GetDpiForWindow(IntPtr hWnd);
    [DllImport("user32.dll", SetLastError = true)]
    public static extern IntPtr SendMessageTimeout(IntPtr hWnd, uint msg, IntPtr wParam, IntPtr lParam, uint flags, uint timeout, out IntPtr result);
    [DllImport("user32.dll")] public static extern bool SetCursorPos(int x, int y);
    [DllImport("user32.dll")] public static extern void mouse_event(uint flags, uint dx, uint dy, uint data, UIntPtr extra);
    [DllImport("user32.dll")] public static extern bool SetForegroundWindow(IntPtr hWnd);
    [DllImport("user32.dll")] public static extern void keybd_event(byte vk, byte scan, uint flags, UIntPtr extra);
    public static void PressKey(byte vk) {
        keybd_event(vk, 0, 0, UIntPtr.Zero); // key down
        System.Threading.Thread.Sleep(40);
        keybd_event(vk, 0, 0x0002, UIntPtr.Zero); // KEYEVENTF_KEYUP
    }
    public static void Click(int x, int y) {
        SetCursorPos(x, y);
        System.Threading.Thread.Sleep(100);
        mouse_event(0x0002, 0, 0, 0, UIntPtr.Zero); // MOUSEEVENTF_LEFTDOWN
        System.Threading.Thread.Sleep(60);
        mouse_event(0x0004, 0, 0, 0, UIntPtr.Zero); // MOUSEEVENTF_LEFTUP
    }
    public static void DoubleClick(int x, int y) {
        SetCursorPos(x, y);
        System.Threading.Thread.Sleep(150);
        for (int i = 0; i < 2; i++) {
            mouse_event(0x0002, 0, 0, 0, UIntPtr.Zero); // MOUSEEVENTF_LEFTDOWN
            System.Threading.Thread.Sleep(40);
            mouse_event(0x0004, 0, 0, 0, UIntPtr.Zero); // MOUSEEVENTF_LEFTUP
            System.Threading.Thread.Sleep(60);
        }
    }
    public static int NcHitTest(IntPtr hWnd, int x, int y) {
        IntPtr result;
        int packed = (y << 16) | (x & 0xFFFF);
        // WM_NCHITTEST = 0x0084, SMTO_ABORTIFHUNG = 0x0002
        if (SendMessageTimeout(hWnd, 0x0084, IntPtr.Zero, new IntPtr(packed), 0x0002, 2000, out result) == IntPtr.Zero) { return -1000; }
        return result.ToInt32();
    }
}
"@
    $AE = [System.Windows.Automation.AutomationElement]
    $Scope = [System.Windows.Automation.TreeScope]::Descendants
    function Find-Element($Parent, [string]$Name, $Type) {
        $ByName = New-Object System.Windows.Automation.PropertyCondition($AE::NameProperty, $Name)
        $ByType = New-Object System.Windows.Automation.PropertyCondition($AE::ControlTypeProperty, $Type)
        return $Parent.FindFirst($Scope, (New-Object System.Windows.Automation.AndCondition($ByName, $ByType)))
    }
    function Wait-Element($Parent, [string]$Name, $Type, [int]$Seconds) {
        $Limit = (Get-Date).AddSeconds($Seconds)
        do {
            $Found = Find-Element $Parent $Name $Type
            if ($null -ne $Found) { return $Found }
            Start-Sleep -Milliseconds 250
        } while ((Get-Date) -lt $Limit)
        return $null
    }
    $ListItem = [System.Windows.Automation.ControlType]::ListItem
    $Process = @(Get-Process -Name 'DSOptimizer' -ErrorAction Stop)[0]
    $Hwnd = $Process.MainWindowHandle
    if ($Hwnd -eq [IntPtr]::Zero) { throw 'DSOptimizer nie ma okna glownego.' }
    $Out.Dpi = [int][DsoUser32]::GetDpiForWindow($Hwnd)
    $Root = $AE::FromHandle($Hwnd)
    Add-Step "okno $Hwnd, DPI $($Out.Dpi)"

    # 1. Pozycja menu "Historia zmian"
    $HistoryItem = Find-Element $Root 'Historia zmian' $ListItem
    $Out.HistoryMenuFound = ($null -ne $HistoryItem)
    Add-Step "menu Historia zmian: $($Out.HistoryMenuFound)"

    # 2. Jedna bezpieczna pozycja: kategoria -> pole wyboru -> "Zastosuj (1)" -> koniec operacji
    try {
        $RefreshName = 'Od' + [char]0x015B + 'wie' + [char]0x017C + ' stan'
        $CategoryItem = Find-Element $Root $Category $ListItem
        if ($null -eq $CategoryItem) { throw "Brak pozycji menu '$Category'." }
        $CategoryItem.GetCurrentPattern([System.Windows.Automation.SelectionItemPattern]::Pattern).Select()
        $Box = Wait-Element $Root $TweakTitle ([System.Windows.Automation.ControlType]::CheckBox) 15
        if ($null -eq $Box) { throw "Brak pola wyboru '$TweakTitle'." }
        $Toggle = $Box.GetCurrentPattern([System.Windows.Automation.TogglePattern]::Pattern)
        if ($Toggle.Current.ToggleState -ne [System.Windows.Automation.ToggleState]::On) { $Toggle.Toggle() }
        $Apply = Wait-Element $Root 'Zastosuj (1)' ([System.Windows.Automation.ControlType]::Button) 10
        if ($null -eq $Apply) { throw 'Brak przycisku "Zastosuj (1)" (pozycja juz zastosowana albo nie trafila do serii).' }
        $Refresh = Find-Element $Root $RefreshName ([System.Windows.Automation.ControlType]::Button)
        if ($null -eq $Refresh) { throw 'Brak przycisku odswiezania stanu.' }
        $Started = Get-Date
        $Apply.GetCurrentPattern([System.Windows.Automation.InvokePattern]::Pattern).Invoke()
        $Out.ApplyInvoked = $true
        Add-Step 'Zastosuj (1): Invoke'
        $Limit = $Started.AddSeconds(10)
        while ((Get-Date) -lt $Limit -and $Refresh.Current.IsEnabled) { Start-Sleep -Milliseconds 50 }
        $Out.ApplyBusySeen = (-not $Refresh.Current.IsEnabled)
        $Limit = $Started.AddSeconds(240)
        while ((Get-Date) -lt $Limit -and -not $Refresh.Current.IsEnabled) { Start-Sleep -Milliseconds 250 }
        if (-not $Refresh.Current.IsEnabled) { throw 'Operacja nie skonczyla sie w 240 s.' }
        $Out.ApplySeconds = [math]::Round(((Get-Date) - $Started).TotalSeconds, 2)
        Add-Step "koniec operacji po $($Out.ApplySeconds) s (zajetosc widoczna: $($Out.ApplyBusySeen))"
    }
    catch { $Out.ApplyError = $_.Exception.Message; Add-Step "zastosowanie: BLAD $($Out.ApplyError)" }

    # 3. Strona "Historia zmian": lista (List o nazwie strony) i filtr; nazwy wierszy
    try {
        if ($null -eq $HistoryItem) { throw 'Brak pozycji menu Historia zmian.' }
        $HistoryItem.GetCurrentPattern([System.Windows.Automation.SelectionItemPattern]::Pattern).Select()
        $EntryList = Wait-Element $Root 'Historia zmian' ([System.Windows.Automation.ControlType]::List) 15
        $Filter = Wait-Element $Root 'Filtr' ([System.Windows.Automation.ControlType]::ComboBox) 5
        $Out.HistoryPageOpened = ($null -ne $EntryList -and $null -ne $Filter)
        Add-Step "strona historii: lista=$($null -ne $EntryList), filtr=$($null -ne $Filter)"
        if ($null -ne $EntryList) {
            $Limit = (Get-Date).AddSeconds(30)
            do {
                $Rows = $EntryList.FindAll([System.Windows.Automation.TreeScope]::Children,
                    (New-Object System.Windows.Automation.PropertyCondition($AE::ControlTypeProperty, $ListItem)))
                $Out.EntryNames = @($Rows | ForEach-Object { [string]$_.Current.Name })
                if (@($Out.EntryNames | Where-Object { $_ -like "$TweakTitle, *" }).Count -gt 0) { break }
                Start-Sleep -Milliseconds 500
            } while ((Get-Date) -lt $Limit)
            Add-Step "wiersze historii: $($Out.EntryNames.Count)"
        }
    }
    catch { $Out.HistoryError = $_.Exception.Message; Add-Step "historia: BLAD $($Out.HistoryError)" }

    # 4. WM_NCHITTEST na pasku marki: stan normalny, potem zmaksymalizowany (okno zostaje zmaksymalizowane)
    try {
        foreach ($State in @(@{ Name = 'Normal'; Show = 9 }, @{ Name = 'Maximized'; Show = 3 })) {
            [void][DsoUser32]::ShowWindow($Hwnd, $State.Show)
            Start-Sleep -Milliseconds 2000
            $Rect = New-Object DsoUser32+RECT
            [void][DsoUser32]::GetWindowRect($Hwnd, [ref]$Rect)
            $Minimize = Find-Element $Root 'Minimalizuj' ([System.Windows.Automation.ControlType]::Button)
            $Menu = Find-Element $Root 'Menu' ([System.Windows.Automation.ControlType]::List)
            if ($null -eq $Minimize -or $null -eq $Menu) { throw 'Brak przycisku Minimalizuj albo listy Menu (UIA).' }
            $Bar = $Minimize.Current.BoundingRectangle
            $BarTop = [int][math]::Round($Bar.Top)
            $BarBottom = [int][math]::Round($Bar.Bottom)
            $Scale = $Out.Dpi / 96.0
            $Left = [int][math]::Round($Menu.Current.BoundingRectangle.Left)
            $Points = @()
            # 8 i 80 DIP od lewej (znak i nazwa) oraz 'srodek' = polowa drogi od lewej krawedzi okna do przycisku Minimalizuj
            # (tekst komputera, najwieksza czesc paska; tam przeciaganie nie dzialalo, 2026-10-26).
            foreach ($Dip in @(8, 80, 'srodek')) {
                $X = if ($Dip -eq 'srodek') { [int][math]::Round(($Rect.Left + $Bar.Left) / 2) } else { $Left + [int][math]::Round($Dip * $Scale) }
                foreach ($Spot in @(@{ Name = 'mid'; Y = [int](($BarTop + $BarBottom) / 2) }, @{ Name = 'low'; Y = $BarBottom - 2 }, @{ Name = 'below'; Y = $BarBottom + 2 })) {
                    $Points += [ordered]@{ Name = $Spot.Name; XDip = $Dip; X = $X; Y = $Spot.Y; Ht = [DsoUser32]::NcHitTest($Hwnd, $X, $Spot.Y) }
                }
            }
            $X8 = $Left + [int][math]::Round(8 * $Scale)
            $Scan = @()
            for ($Y = $BarTop - 12; $Y -le $BarBottom + 12; $Y++) { $Scan += [ordered]@{ Y = $Y; Ht = [DsoUser32]::NcHitTest($Hwnd, $X8, $Y) } }
            $Out.HitTest += [ordered]@{
                State = $State.Name; WindowRect = @($Rect.Left, $Rect.Top, $Rect.Right, $Rect.Bottom)
                BarTop = $BarTop; BarBottom = $BarBottom; ContentLeft = $Left; Points = $Points; ScanX = $X8; Scan = $Scan
            }
            Add-Step "NCHITTEST $($State.Name): okno $($Rect.Top)..$($Rect.Bottom), pasek $BarTop..$BarBottom"
        }
    }
    catch { $Out.HitTestError = $_.Exception.Message; Add-Step "NCHITTEST: BLAD $($Out.HitTestError)" }

    # 5. Filtry klikane prawdziwa mysza: pole -> Expanded (do 1 s) -> klik w druga opcje -> zmiana wartosci -> klik w pierwsza -> powrot
    function Get-CenterPoint($Element) {
        $R = $Element.Current.BoundingRectangle
        return @([int][math]::Round($R.Left + $R.Width / 2), [int][math]::Round($R.Top + $R.Height / 2))
    }
    function Get-ComboValue($Combo) {
        $Name = $null
        try {
            $Sel = @($Combo.GetCurrentPattern([System.Windows.Automation.SelectionPattern]::Pattern).Current.GetSelection())
            if ($Sel.Count -gt 0) { $Name = [string]$Sel[0].Current.Name }
        } catch { }
        if ([string]::IsNullOrEmpty($Name)) {
            try { $Name = [string]$Combo.GetCurrentPattern([System.Windows.Automation.ValuePattern]::Pattern).Current.Value } catch { }
        }
        return $Name
    }
    function Wait-Expanded($Combo, [bool]$Expected) {
        $Ec = $Combo.GetCurrentPattern([System.Windows.Automation.ExpandCollapsePattern]::Pattern)
        $Target = if ($Expected) { [System.Windows.Automation.ExpandCollapseState]::Expanded } else { [System.Windows.Automation.ExpandCollapseState]::Collapsed }
        $Limit = (Get-Date).AddMilliseconds(1000)
        do {
            if ($Ec.Current.ExpandCollapseState -eq $Target) { return $true }
            Start-Sleep -Milliseconds 50
        } while ((Get-Date) -lt $Limit)
        return ($Ec.Current.ExpandCollapseState -eq $Target)
    }
    function Get-ComboOption($Combo, [int]$Index, [string[]]$ExpectedNames) {
        # Kandydat musi miec nazwe z oczekiwanej listy opcji pola (inaczej np. element menu mozna wziac za opcje filtra).
        $ByType = New-Object System.Windows.Automation.PropertyCondition($AE::ControlTypeProperty, $ListItem)
        $Options = @($Combo.FindAll($Scope, $ByType) | Where-Object { $ExpectedNames -contains [string]$_.Current.Name })
        if ($Options.Count -lt 2) {
            # lista rozwinieta bywa osobnym oknem procesu: szukamy elementow ListItem tego procesu pod polem
            $Field = $Combo.Current.BoundingRectangle
            $ByProcess = New-Object System.Windows.Automation.PropertyCondition($AE::ProcessIdProperty, [int]$Process.Id)
            $All = $AE::RootElement.FindAll($Scope, (New-Object System.Windows.Automation.AndCondition($ByProcess, $ByType)))
            $Options = @($All | Where-Object {
                    $R = $_.Current.BoundingRectangle
                    ($ExpectedNames -contains [string]$_.Current.Name) -and $R.Top -ge ($Field.Bottom - 2) -and $R.Left -ge ($Field.Left - 2) -and $R.Left -le $Field.Right
                })
        }
        $Sorted = @($Options | Sort-Object { $_.Current.BoundingRectangle.Top })
        if ($Sorted.Count -le $Index) { throw "Rozwinieta lista ma $($Sorted.Count) opcji o oczekiwanych nazwach (potrzeba > $Index)." }
        return $Sorted[$Index]
    }
    function Reset-FilterCombo($Combo, [string]$ComboName, $Before, [string[]]$ExpectedNames) {
        # Sprzatanie po polu (finally): otwarta lista -> Esc (potem awaryjnie Collapse, tylko sprzatanie, nie dowod), potem proba powrotu do pierwszej opcji.
        try {
            $Ec = $Combo.GetCurrentPattern([System.Windows.Automation.ExpandCollapsePattern]::Pattern)
            if ($Ec.Current.ExpandCollapseState -eq [System.Windows.Automation.ExpandCollapseState]::Expanded) {
                [DsoUser32]::PressKey(0x1B)
                if (-not (Wait-Expanded $Combo $false)) { $Ec.Collapse() }
                Add-Step "filtr '$ComboName': sprzatanie - lista byla otwarta, zamknieta"
            }
            if (-not [string]::IsNullOrEmpty($Before) -and (Get-ComboValue $Combo) -ne $Before) {
                $P = Get-CenterPoint $Combo
                [DsoUser32]::Click($P[0], $P[1])
                if (Wait-Expanded $Combo $true) {
                    $F = Get-CenterPoint (Get-ComboOption $Combo 0 $ExpectedNames)
                    [DsoUser32]::Click($F[0], $F[1])
                    [void](Wait-Expanded $Combo $false)
                }
                Add-Step "filtr '$ComboName': sprzatanie - proba powrotu do pierwszej opcji, wartosc '$(Get-ComboValue $Combo)'"
            }
        }
        catch { Add-Step "filtr '$ComboName': sprzatanie BLAD $($_.Exception.Message)" }
    }
    function Test-FilterCombo([string]$NavPrefix, [string]$ComboName, [string[]]$ExpectedNames, $Result) {
        # $Result (hashtable: Opened, Selected, Note) wypelniany na biezaco, wiec wyjatek w polowie nie gubi tego, co juz sprawdzono.
        $Combo = $null
        $Before = $null
        try {
            $Nav = $null
            $Items = $Root.FindAll($Scope, (New-Object System.Windows.Automation.PropertyCondition($AE::ControlTypeProperty, $ListItem)))
            foreach ($I in $Items) { if ($I.Current.Name -like "$NavPrefix*") { $Nav = $I; break } }
            if ($null -eq $Nav) { throw "Brak pozycji menu '$NavPrefix'." }
            $Nav.GetCurrentPattern([System.Windows.Automation.SelectionItemPattern]::Pattern).Select()
            $Combo = Wait-Element $Root $ComboName ([System.Windows.Automation.ControlType]::ComboBox) 15
            if ($null -eq $Combo) { throw "Brak ComboBox '$ComboName'." }
            Start-Sleep -Milliseconds 500
            [void][DsoUser32]::SetForegroundWindow($Hwnd)
            Start-Sleep -Milliseconds 300
            $Before = Get-ComboValue $Combo
            $P = Get-CenterPoint $Combo
            [DsoUser32]::Click($P[0], $P[1])
            $Result.Opened = (Wait-Expanded $Combo $true)
            Add-Step "filtr '$ComboName': klik $($P[0]),$($P[1]); rozwiniety=$($Result.Opened); wartosc przed '$Before'"
            if (-not $Result.Opened) { return }
            $Second = Get-ComboOption $Combo 1 $ExpectedNames
            $S = Get-CenterPoint $Second
            [DsoUser32]::Click($S[0], $S[1])
            $Closed = (Wait-Expanded $Combo $false)
            Start-Sleep -Milliseconds 300
            $After = Get-ComboValue $Combo
            $Changed = (-not [string]::IsNullOrEmpty($After)) -and ($After -ne $Before)
            Add-Step "filtr '$ComboName': klik w druga opcje '$($Second.Current.Name)'; lista zamknieta=$Closed; wartosc po '$After'; zmieniona=$Changed"
            if (-not $Closed) { $Result.Note = 'lista nie zamknela sie po wyborze opcji'; return }
            # powrot na pierwsza opcje
            $P = Get-CenterPoint $Combo
            [DsoUser32]::Click($P[0], $P[1])
            if (Wait-Expanded $Combo $true) {
                $F = Get-CenterPoint (Get-ComboOption $Combo 0 $ExpectedNames)
                [DsoUser32]::Click($F[0], $F[1])
                $ClosedBack = (Wait-Expanded $Combo $false)
                Start-Sleep -Milliseconds 300
                if (-not $ClosedBack) { $Result.Note = 'lista nie zamknela sie po powrocie do pierwszej opcji'; return }
            }
            $Back = Get-ComboValue $Combo
            Add-Step "filtr '$ComboName': powrot; wartosc '$Back' (poczatkowa '$Before')"
            $Result.Selected = $Changed -and ($Back -eq $Before)
        }
        finally {
            if ($null -ne $Combo) { Reset-FilterCombo $Combo $ComboName $Before $ExpectedNames }
        }
    }
    # Oczekiwane nazwy opcji (GuiText.RiskFilterName, HistoryText.FilterName); polskie znaki z kodow znakow (skrypt tylko ASCII).
    $RiskOptionNames = @('Wszystkie', 'Bez eksperymentalnych', 'Tylko bezpieczne')
    $HistoryOptionNames = @('Wszystkie wpisy', 'Aktywne', ('Czekaj' + [char]0x0105 + 'ce na restart'), ('Cofni' + [char]0x0119 + 'te i przerwane'))
    # Kazde pole osobno: blad jednego nie pomija drugiego, a opis bledu niesie nazwe pola.
    $FilterErrors = @()
    $R1 = @{ Opened = $false; Selected = $false; Note = $null }
    try { Test-FilterCombo $Category 'Ryzyko' $RiskOptionNames $R1 }
    catch { $FilterErrors += "Ryzyko: $($_.Exception.Message)"; Add-Step "filtr 'Ryzyko': BLAD $($_.Exception.Message)" }
    if ($R1.Note) { $FilterErrors += "Ryzyko: $($R1.Note)" }
    $Out.FilterRiskOpened = [bool]$R1.Opened
    $Out.FilterRiskSelected = [bool]$R1.Selected
    $R2 = @{ Opened = $false; Selected = $false; Note = $null }
    try { Test-FilterCombo 'Historia zmian' 'Filtr' $HistoryOptionNames $R2 }
    catch { $FilterErrors += "Filtr: $($_.Exception.Message)"; Add-Step "filtr 'Filtr': BLAD $($_.Exception.Message)" }
    if ($R2.Note) { $FilterErrors += "Filtr: $($R2.Note)" }
    $Out.FilterHistoryOpened = [bool]$R2.Opened
    $Out.FilterHistorySelected = [bool]$R2.Selected
    if ($FilterErrors.Count -gt 0) { $Out.FilterError = $FilterErrors -join '; ' }
    # 6. Wiersze listy klikane prawdziwa mysza (model klikniecia z Task 2): tweak - klik w tytul tylko pokazuje wiersz, dwuklik przelacza pole
    #    wyboru (drugi dwuklik wraca); panel - klik w znacznik uruchomienia (trojkat) uruchamia mmc.exe (nowe okno), dwuklik w niego dokladnie raz.
    function Get-ListRow([string]$NavName, [string]$Title) {
        $Nav = Find-Element $Root $NavName $ListItem
        if ($null -eq $Nav) { throw "Brak pozycji menu '$NavName'." }
        $Nav.GetCurrentPattern([System.Windows.Automation.SelectionItemPattern]::Pattern).Select()
        $List = Wait-Element $Root 'Pozycje' ([System.Windows.Automation.ControlType]::List) 15
        if ($null -eq $List) { throw 'Brak listy Pozycje.' }
        $Text = Wait-Element $List $Title ([System.Windows.Automation.ControlType]::Text) 15
        if ($null -eq $Text) { throw "Brak tytulu wiersza '$Title' na liscie." }
        $Walker = [System.Windows.Automation.TreeWalker]::ControlViewWalker
        $Row = $Walker.GetParent($Text)
        while ($null -ne $Row -and $Row.Current.ControlType.Id -ne $ListItem.Id) { $Row = $Walker.GetParent($Row) }
        if ($null -eq $Row) { throw "Brak kontenera wiersza '$Title' (ListItem)." }
        try { $Row.GetCurrentPattern([System.Windows.Automation.ScrollItemPattern]::Pattern).ScrollIntoView() } catch { }
        Start-Sleep -Milliseconds 400
        return @{ List = $List; Row = $Row; Text = $Text }
    }
    function Wait-AppIdle([int]$Seconds) {
        # Po uruchomieniu panelu aplikacja jest zajeta (IsBusy: "Odswiez stan" nieaktywne, ok. 8 s na VM) i ignoruje kolejny klik w znacznik
        # uruchomienia. Kolejny krok sondy czeka na koniec zajetosci - inaczej dwuklik trafialby w zajeta aplikacje i dawal 0 okien.
        $Name = 'Od' + [char]0x015B + 'wie' + [char]0x017C + ' stan'
        $Btn = Find-Element $Root $Name ([System.Windows.Automation.ControlType]::Button)
        if ($null -eq $Btn) { throw 'Brak przycisku odswiezania stanu.' }
        $T0 = Get-Date
        $Limit = $T0.AddSeconds($Seconds)
        while ((Get-Date) -lt $Limit -and -not $Btn.Current.IsEnabled) { Start-Sleep -Milliseconds 250 }
        if (-not $Btn.Current.IsEnabled) { throw "Aplikacja nadal zajeta po $Seconds s." }
        Add-Step ('aplikacja bezczynna po {0:0.0} s' -f ((Get-Date) - $T0).TotalSeconds)
        Start-Sleep -Milliseconds 500
    }
    function Get-MmcWindowId { return @(Get-Process -Name 'mmc' -ErrorAction SilentlyContinue | Where-Object { $_.MainWindowHandle -ne [IntPtr]::Zero } | ForEach-Object { [int]$_.Id }) }
    function Get-NewMmcId([int[]]$Base) { return @(Get-MmcWindowId | Where-Object { $Base -notcontains $_ }) }
    function Wait-NewMmc([int[]]$Base, [int]$Seconds) {
        $Limit = (Get-Date).AddSeconds($Seconds)
        do {
            if ((Get-NewMmcId $Base).Count -ge 1) { break }
            Start-Sleep -Milliseconds 300
        } while ((Get-Date) -lt $Limit)
        Start-Sleep -Seconds 3   # czas na ewentualne drugie (blednie uruchomione) okno
        return @(Get-NewMmcId $Base)
    }
    function Close-NewMmc([int[]]$Base) {
        foreach ($Id in (Get-NewMmcId $Base)) {
            $P = Get-Process -Id $Id -ErrorAction SilentlyContinue
            if ($null -eq $P) { continue }
            [void]$P.CloseMainWindow()
            if (-not $P.WaitForExit(5000)) { $P.Kill(); [void]$P.WaitForExit(5000) }
        }
        Start-Sleep -Milliseconds 800
    }
    $RowErrors = @()
    $TweakBox = $null
    $InitialToggle = $null
    try {
        $Tw = Get-ListRow $Category $TweakTitle
        $TweakBox = Find-Element $Tw.List $TweakTitle ([System.Windows.Automation.ControlType]::CheckBox)
        if ($null -eq $TweakBox) { throw "Brak pola wyboru '$TweakTitle' na liscie." }
        $Toggle = $TweakBox.GetCurrentPattern([System.Windows.Automation.TogglePattern]::Pattern)
        $InitialToggle = $Toggle.Current.ToggleState
        [void][DsoUser32]::SetForegroundWindow($Hwnd)
        Start-Sleep -Milliseconds 300
        $P = Get-CenterPoint $Tw.Text
        [DsoUser32]::Click($P[0], $P[1])
        Start-Sleep -Milliseconds 900
        $AfterSingle = $Toggle.Current.ToggleState
        $Selected = [bool]$Tw.Row.GetCurrentPattern([System.Windows.Automation.SelectionItemPattern]::Pattern).Current.IsSelected
        $Out.RowClickPreview = ($AfterSingle -eq $InitialToggle) -and $Selected
        Add-Step "wiersz tweaka: klik w tytul $($P[0]),$($P[1]); pole wyboru $InitialToggle -> $AfterSingle; wiersz wybrany=$Selected"
        $P = Get-CenterPoint $Tw.Text
        [DsoUser32]::DoubleClick($P[0], $P[1])
        Start-Sleep -Milliseconds 900
        $AfterDouble = $Toggle.Current.ToggleState
        $P = Get-CenterPoint $Tw.Text
        [DsoUser32]::DoubleClick($P[0], $P[1])
        Start-Sleep -Milliseconds 900
        $AfterSecond = $Toggle.Current.ToggleState
        $Out.RowDoubleClickToggles = ($AfterDouble -ne $InitialToggle) -and ($AfterSecond -eq $InitialToggle)
        Add-Step "wiersz tweaka: dwuklik -> $AfterDouble, drugi dwuklik -> $AfterSecond (poczatkowo $InitialToggle)"
    }
    catch { $RowErrors += "tweak: $($_.Exception.Message)"; Add-Step "wiersz tweaka: BLAD $($_.Exception.Message)" }
    finally {
        # sprzatanie: zaznaczenie do serii wraca do stanu poczatkowego (tylko zaznaczenie, nic nie jest stosowane)
        try {
            if ($null -ne $TweakBox -and $null -ne $InitialToggle) {
                $T = $TweakBox.GetCurrentPattern([System.Windows.Automation.TogglePattern]::Pattern)
                if ($T.Current.ToggleState -ne $InitialToggle) { $T.Toggle(); Add-Step 'wiersz tweaka: sprzatanie - przywrocono pole wyboru' }
            }
        } catch { Add-Step "wiersz tweaka: sprzatanie BLAD $($_.Exception.Message)" }
    }
    $PanelTitle = 'Zarz' + [char]0x0105 + 'dzanie komputerem'
    $BaseMmc = @(Get-MmcWindowId)
    try {
        $Pn = Get-ListRow 'Panele systemowe' $PanelTitle
        [void][DsoUser32]::SetForegroundWindow($Hwnd)
        Start-Sleep -Milliseconds 300
        $Scale = $Out.Dpi / 96.0
        # znacznik uruchomienia to kolumna 1 wiersza: 3 (krawedz serii) + 9 (margines) + 32 (pole wyboru) DIP od lewej krawedzi wiersza, szerokosc 22 DIP
        $RowRect = $Pn.Row.Current.BoundingRectangle
        $IconX = [int][math]::Round($RowRect.Left + (3 + 9 + 32 + 11) * $Scale)
        $IconY = [int][math]::Round($RowRect.Top + $RowRect.Height / 2)
        [DsoUser32]::Click($IconX, $IconY)
        $New = @(Wait-NewMmc $BaseMmc 20)
        $Out.RunIconRuns = ($New.Count -eq 1)
        Add-Step "panel: klik w znacznik uruchomienia ($IconX,$IconY); nowe okna mmc.exe: $($New.Count)"
        Close-NewMmc $BaseMmc
        if ((Get-NewMmcId $BaseMmc).Count -ne 0) { throw 'Nie udalo sie zamknac okna mmc.exe po pierwszym tescie.' }
        Wait-AppIdle 90
        [void][DsoUser32]::SetForegroundWindow($Hwnd)
        Start-Sleep -Milliseconds 300
        $RowRect = $Pn.Row.Current.BoundingRectangle
        $IconY = [int][math]::Round($RowRect.Top + $RowRect.Height / 2)
        [DsoUser32]::DoubleClick($IconX, $IconY)
        $New = @(Wait-NewMmc $BaseMmc 20)
        $Out.RunIconDoubleClickOnce = ($New.Count -eq 1)
        Add-Step "panel: dwuklik w znacznik uruchomienia; nowe okna mmc.exe: $($New.Count)"
    }
    catch { $RowErrors += "panel: $($_.Exception.Message)"; Add-Step "panel: BLAD $($_.Exception.Message)" }
    finally { try { Close-NewMmc $BaseMmc; Wait-AppIdle 90 } catch { Add-Step "panel: sprzatanie BLAD $($_.Exception.Message)" } }
    if ($RowErrors.Count -gt 0) { $Out.RowError = $RowErrors -join '; ' }
    # 7. Kategoria "Prywatnosc i AI" - DOPIERO po sciezce ux.menu-delay (krok 2) i po sprzataniu kroku 6: pierwsze wejscie na kategorie
    #    zaznacza jej sugerowane pozycje do globalnej serii, wiec wczesniejsze wejscie zepsuloby oczekiwanie kroku 2 (Review Focus 1).
    #    Nic nie jest stosowane ani przelaczane: sonda czyta tylko N z przycisku serii. Sugestie zaznaczaja sie raz, gdy wszystkie sugerowane
    #    pozycje maja znany stan, wiec N czytamy przy bezczynnej aplikacji i po 3 s tej samej wartosci (najdluzej 60 s).
    try {
        $PrivacyName = 'Prywatno' + [char]0x015B + [char]0x0107 + ' i AI'
        $PrivacyNav = Find-Element $Root $PrivacyName $ListItem
        if ($null -eq $PrivacyNav) { throw "Brak pozycji menu '$PrivacyName'." }
        $PrivacyNav.GetCurrentPattern([System.Windows.Automation.SelectionItemPattern]::Pattern).Select()
        Start-Sleep -Milliseconds 1000
        Wait-AppIdle 120
        $ApplyPattern = '^Zastosuj \((\d+)\)$'
        $ByButton = New-Object System.Windows.Automation.PropertyCondition($AE::ControlTypeProperty, [System.Windows.Automation.ControlType]::Button)
        $Last = $null
        $StableSince = $null
        $Limit = (Get-Date).AddSeconds(60)
        do {
            $Seen = $null
            foreach ($Button in $Root.FindAll($Scope, $ByButton)) {
                $Name = [string]$Button.Current.Name
                if ($Name -match $ApplyPattern) { $Seen = $Name; break }
            }
            if ($null -eq $StableSince -or $Seen -ne $Last) { $Last = $Seen; $StableSince = Get-Date }
            elseif (((Get-Date) - $StableSince).TotalSeconds -ge 3) { break }
            Start-Sleep -Milliseconds 250
        } while ((Get-Date) -lt $Limit)
        $Out.PrivacyButtonFound = ($null -ne $Last)
        $Out.PrivacyApplyText = $Last
        if ($null -ne $Last -and $Last -match $ApplyPattern) { $Out.PrivacyApplyCount = [int]$Matches[1] }
        Add-Step "prywatnosc: przycisk serii '$Last'"
    }
    catch { $Out.PrivacyError = $_.Exception.Message; Add-Step "prywatnosc: BLAD $($Out.PrivacyError)" }
    # 8. Strona "Aplikacje" - na samym koncu: pierwsze wejscie czyta liste i zaznacza sugerowane aplikacje do osobnej serii "Usun (N)"
    #    (nie miesza sie z "Zastosuj (N)"). Nic nie jest usuwane ani klikane: sonda czyta N z przycisku dopiero po zakonczonym odczycie
    #    listy ("Odswiez liste" aktywne, podtytul bez "Odczyt listy w toku" i "Lista nie zostala jeszcze odczytana") i po 3 s tej samej
    #    wartosci (najdluzej 180 s - odczyt AppX idzie przez PowerShell).
    try {
        $AppsNav = Find-Element $Root 'Aplikacje' $ListItem
        if ($null -eq $AppsNav) { throw "Brak pozycji menu 'Aplikacje'." }
        $AppsNav.GetCurrentPattern([System.Windows.Automation.SelectionItemPattern]::Pattern).Select()
        Start-Sleep -Milliseconds 1000
        $AppsRefreshName = 'Od' + [char]0x015B + 'wie' + [char]0x017C + ' list' + [char]0x0119
        $AppsRefresh = Wait-Element $Root $AppsRefreshName ([System.Windows.Automation.ControlType]::Button) 15
        if ($null -eq $AppsRefresh) { throw 'Brak przycisku odswiezania listy aplikacji.' }
        $TextType = [System.Windows.Automation.ControlType]::Text
        $ReadingName = 'Odczyt listy w toku'
        $NotReadName = 'Lista nie zosta' + [char]0x0142 + 'a jeszcze odczytana'
        $RemovePattern = '^Usu' + [char]0x0144 + ' \((\d+)\)$'
        $ByButton = New-Object System.Windows.Automation.PropertyCondition($AE::ControlTypeProperty, [System.Windows.Automation.ControlType]::Button)
        $Last = $null
        $StableSince = $null
        $Limit = (Get-Date).AddSeconds(180)
        do {
            $Read = $AppsRefresh.Current.IsEnabled -and $null -eq (Find-Element $Root $ReadingName $TextType) -and $null -eq (Find-Element $Root $NotReadName $TextType)
            $Seen = $null
            if ($Read) {
                foreach ($Button in $Root.FindAll($Scope, $ByButton)) {
                    $Name = [string]$Button.Current.Name
                    if ($Name -match $RemovePattern) { $Seen = $Name; break }
                }
            }
            if (-not $Read) { $Last = $null; $StableSince = $null }
            elseif ($null -eq $StableSince -or $Seen -ne $Last) { $Last = $Seen; $StableSince = Get-Date }
            elseif (((Get-Date) - $StableSince).TotalSeconds -ge 3) { $Out.AppsListRead = $true; break }
            Start-Sleep -Milliseconds 250
        } while ((Get-Date) -lt $Limit)
        if (-not $Out.AppsListRead) { throw 'Odczyt listy aplikacji nie skonczyl sie (albo N sie zmienialo) w 180 s.' }
        $Out.AppsButtonFound = ($null -ne $Last)
        $Out.AppsRemoveText = $Last
        if ($null -ne $Last -and $Last -match $RemovePattern) { $Out.AppsRemoveCount = [int]$Matches[1] }
        Add-Step "aplikacje: przycisk serii '$Last'"
    }
    catch { $Out.AppsError = $_.Exception.Message; Add-Step "aplikacje: BLAD $($Out.AppsError)" }
    # 9. Strona "Przygotowanie" - po kroku 8 (sama strona niczego nie zaznacza do serii, kolejnosc tylko dla porzadku). Nic nie jest klikane:
    #    wejscie wzorcem SelectionItemPattern, czekanie najwyzej 60 s, az znikna "Wczytywanie..." (PrepText.Loading) i pojawi sie 7 tytulow
    #    krokow; potem przycisk "Otworz Windows Update" albo notatka zastepcza (PrepLinks.SettingsOpenable) i teksty kroku "Grafika" do
    #    nastepnego tytulu (bez numerow krokow i napisow przyciskow linkow). Teksty w kolejnosci drzewa UIA (FindAll, Descendants).
    try {
        $PrepNav = Find-Element $Root 'Przygotowanie' $ListItem
        $Out.PrepMenuFound = ($null -ne $PrepNav)
        if ($null -eq $PrepNav) { throw "Brak pozycji menu 'Przygotowanie'." }
        $PrepNav.GetCurrentPattern([System.Windows.Automation.SelectionItemPattern]::Pattern).Select()
        Start-Sleep -Milliseconds 1000
        $PrepTitleNames = @('Komputer', 'Windows Update', ('Opcjonalne aktualizacje sterownik' + [char]0x00F3 + 'w'), 'Sterowniki chipsetu', 'Grafika',
            'Wsparcie producenta', ('Urz' + [char]0x0105 + 'dzenia bez sterownika'))
        $PrepLoadingName = 'Wczytywanie' + [char]0x2026
        $ByText = New-Object System.Windows.Automation.PropertyCondition($AE::ControlTypeProperty, [System.Windows.Automation.ControlType]::Text)
        $Walker = [System.Windows.Automation.TreeWalker]::ControlViewWalker
        $ButtonId = [System.Windows.Automation.ControlType]::Button.Id
        $PrepTexts = @()
        $Limit = (Get-Date).AddSeconds(60)
        do {
            # Tekst wewnatrz przycisku (napis linku) nie jest linia kroku.
            $PrepTexts = @($Root.FindAll($Scope, $ByText) | Where-Object {
                    $Parent = $Walker.GetParent($_)
                    $null -eq $Parent -or $Parent.Current.ControlType.Id -ne $ButtonId
                } | ForEach-Object { [string]$_.Current.Name })
            $Found = @($PrepTitleNames | Where-Object { $PrepTexts -contains $_ })
            $Loading = $PrepTexts -contains $PrepLoadingName
            if ($Found.Count -eq $PrepTitleNames.Count -and -not $Loading) { $Out.PrepLoaded = $true; break }
            Start-Sleep -Milliseconds 500
        } while ((Get-Date) -lt $Limit)
        $Out.PrepTitles = @($Found)
        $WuButtonName = 'Otw' + [char]0x00F3 + 'rz Windows Update'
        $WuNoteName = 'Otw' + [char]0x00F3 + 'rz Ustawienia ' + [char]0x203A + ' Windows Update.'
        $Out.PrepWindowsUpdateButton = ($null -ne (Find-Element $Root $WuButtonName ([System.Windows.Automation.ControlType]::Button)))
        $Out.PrepWindowsUpdateNote = ($PrepTexts -contains $WuNoteName)
        $GraphicsAt = [array]::IndexOf($PrepTexts, 'Grafika')
        $Lines = @()
        if ($GraphicsAt -ge 0) {
            for ($I = $GraphicsAt + 1; $I -lt $PrepTexts.Count; $I++) {
                $T = $PrepTexts[$I]
                if ($PrepTitleNames -contains $T) { break }
                if ([string]::IsNullOrWhiteSpace($T) -or $T -match '^\d+$') { continue }
                $Lines += $T
            }
        }
        $Out.PrepGraphicsLines = @($Lines)
        Add-Step "przygotowanie: tytuly $($Found.Count)/$($PrepTitleNames.Count), odczyt skonczony=$($Out.PrepLoaded), Windows Update: przycisk=$($Out.PrepWindowsUpdateButton) notatka=$($Out.PrepWindowsUpdateNote), grafika: $($Lines -join ' | ')"
    }
    catch { $Out.PrepError = $_.Exception.Message; Add-Step "przygotowanie: BLAD $($Out.PrepError)" }
    # 10. Strona "Naprawy" (Etap 4b partia 1) - przed "Instalacja". Nic nie jest zaznaczane, klikane ani uruchamiane: wejscie wzorcem
    #     SelectionItemPattern na pozycje menu, lista 'Pozycje' i nazwy jej tekstow (tytuly, opisy). Odczyt powtarzany co 0,5 s (najwyzej
    #     20 s), az dwa kolejne sa takie same i niepuste; potem lista przewijana wzorcem ScrollPattern do konca (wirtualizacja wierszy)
    #     i z powrotem. Host porownuje teksty z tytulami pozycji kategorii repair z katalogu.
    try {
        $RepairNav = Find-Element $Root 'Naprawy' $ListItem
        $Out.RepairMenuFound = ($null -ne $RepairNav)
        if ($null -eq $RepairNav) { throw "Brak pozycji menu 'Naprawy'." }
        $RepairNav.GetCurrentPattern([System.Windows.Automation.SelectionItemPattern]::Pattern).Select()
        Start-Sleep -Milliseconds 1000
        $RepairList = Wait-Element $Root 'Pozycje' ([System.Windows.Automation.ControlType]::List) 15
        $Out.RepairListFound = ($null -ne $RepairList)
        if ($null -eq $RepairList) { throw 'Brak listy Pozycje na stronie Naprawy.' }
        $RepairByText = New-Object System.Windows.Automation.PropertyCondition($AE::ControlTypeProperty, [System.Windows.Automation.ControlType]::Text)
        $RepairNames = New-Object System.Collections.Generic.List[string]
        $ReadRepairTexts = { @($RepairList.FindAll($Scope, $RepairByText) | ForEach-Object { [string]$_.Current.Name } | Where-Object { $_ }) }
        $Previous = $null
        $Limit = (Get-Date).AddSeconds(20)
        do {
            $Now = @(& $ReadRepairTexts)
            if ($Now.Count -gt 0 -and $null -ne $Previous -and (($Now -join '|') -eq ($Previous -join '|'))) { break }
            $Previous = $Now
            Start-Sleep -Milliseconds 500
        } while ((Get-Date) -lt $Limit)
        foreach ($N in $Now) { if (-not $RepairNames.Contains($N)) { $RepairNames.Add($N) } }
        try {
            $RepairScroll = $RepairList.GetCurrentPattern([System.Windows.Automation.ScrollPattern]::Pattern)
            if ($RepairScroll.Current.VerticallyScrollable) {
                $RepairScroll.SetScrollPercent([System.Windows.Automation.ScrollPattern]::NoScroll, 100)
                Start-Sleep -Milliseconds 600
                foreach ($N in @(& $ReadRepairTexts)) { if (-not $RepairNames.Contains($N)) { $RepairNames.Add($N) } }
                $RepairScroll.SetScrollPercent([System.Windows.Automation.ScrollPattern]::NoScroll, 0)
            }
        }
        catch { Add-Step "naprawy: przewijanie listy pominiete ($($_.Exception.Message))" }
        $Out.RepairTexts = @($RepairNames)
        Add-Step "naprawy: tekstow listy $($RepairNames.Count)"
    }
    catch { $Out.RepairError = $_.Exception.Message; Add-Step "naprawy: BLAD $($Out.RepairError)" }
    # 11. Strona "Instalacja" (Etap 4c) - na samym koncu. Nic nie jest zaznaczane, instalowane ani wyszukiwane (VM bez sieci): wejscie
    #     wzorcem SelectionItemPattern na pozycje menu, czekanie najwyzej 180 s na koniec odczytu ("Odswiez" strony aktywne, podtytul
    #     "Zrodlo: ..."/"Zrodla: ...", lista 'Kategorie' niepusta, przycisk serii staly przez 3 s), potem kazda kategoria wybierana
    #     wzorcem SelectionItemPattern (nawigacja, nie zaznaczenie do serii) i teksty listy 'Aplikacje do instalacji' czytane z przewijaniem
    #     wzorcem ScrollPattern. Na koniec napis i IsEnabled przycisku 'Zainstaluj (N)'. Host porownuje z apps.json repo.
    try {
        $InstallNav = Find-Element $Root 'Instalacja' $ListItem
        $Out.InstallMenuFound = ($null -ne $InstallNav)
        if ($null -eq $InstallNav) { throw "Brak pozycji menu 'Instalacja'." }
        $InstallNav.GetCurrentPattern([System.Windows.Automation.SelectionItemPattern]::Pattern).Select()
        Start-Sleep -Milliseconds 1000
        $InstallRefreshName = 'Od' + [char]0x015B + 'wie' + [char]0x017C
        # "Zrodlo: ..." albo "Zrodla: ..." (M6 planu RustDesk) - sonda czyta kazdy podtytul zrodla, dokladny tekst ocenia host.
        $SourcePrefix = [char]0x0179 + 'r' + [char]0x00F3 + 'd' + [char]0x0142
        $InstallPattern = '^Zainstaluj \((\d+)\)$'
        $TextType = [System.Windows.Automation.ControlType]::Text
        $ByInstallText = New-Object System.Windows.Automation.PropertyCondition($AE::ControlTypeProperty, $TextType)
        $ByInstallButton = New-Object System.Windows.Automation.PropertyCondition($AE::ControlTypeProperty, [System.Windows.Automation.ControlType]::Button)
        $ByInstallItem = New-Object System.Windows.Automation.PropertyCondition($AE::ControlTypeProperty, $ListItem)
        $CategoryList = Wait-Element $Root 'Kategorie' ([System.Windows.Automation.ControlType]::List) 30
        if ($null -eq $CategoryList) { throw 'Brak listy Kategorie na stronie Instalacja.' }
        $InstallButton = $null
        $Last = $null
        $StableSince = $null
        $Limit = (Get-Date).AddSeconds(180)
        do {
            $Refresh = Find-Element $Root $InstallRefreshName ([System.Windows.Automation.ControlType]::Button)
            $Subtitle = @($Root.FindAll($Scope, $ByInstallText) | ForEach-Object { [string]$_.Current.Name } | Where-Object { $_.StartsWith($SourcePrefix) }) | Select-Object -First 1
            $Items = @($CategoryList.FindAll($Scope, $ByInstallItem))
            $InstallButton = $null
            foreach ($Button in $Root.FindAll($Scope, $ByInstallButton)) { if ([string]$Button.Current.Name -match $InstallPattern) { $InstallButton = $Button; break } }
            $Ready = $null -ne $Refresh -and $Refresh.Current.IsEnabled -and $null -ne $Subtitle -and $Items.Count -gt 0 -and $null -ne $InstallButton
            $Seen = if ($Ready) { "$Subtitle|$($InstallButton.Current.Name)" } else { $null }
            if (-not $Ready) { $Last = $null; $StableSince = $null }
            elseif ($null -eq $StableSince -or $Seen -ne $Last) { $Last = $Seen; $StableSince = Get-Date }
            elseif (((Get-Date) - $StableSince).TotalSeconds -ge 3) { $Out.InstallLoaded = $true; break }
            Start-Sleep -Milliseconds 250
        } while ((Get-Date) -lt $Limit)
        $Out.InstallSubtitle = $Subtitle
        if (-not $Out.InstallLoaded) { throw 'Odczyt strony Instalacja nie skonczyl sie (albo stan sie zmienial) w 180 s.' }
        $CategoryNames = New-Object System.Collections.Generic.List[string]
        $InstallNames = New-Object System.Collections.Generic.List[string]
        foreach ($Item in @($CategoryList.FindAll($Scope, $ByInstallItem))) {
            $CategoryNames.Add([string]$Item.Current.Name)
            $Item.GetCurrentPattern([System.Windows.Automation.SelectionItemPattern]::Pattern).Select()
            Start-Sleep -Milliseconds 500
            $AppList = Wait-Element $Root 'Aplikacje do instalacji' ([System.Windows.Automation.ControlType]::List) 10
            if ($null -eq $AppList) { throw "Brak listy Aplikacje do instalacji na stronie Instalacja (kategoria '$($Item.Current.Name)')." }
            $ReadInstallTexts = { foreach ($N in @($AppList.FindAll($Scope, $ByInstallText) | ForEach-Object { [string]$_.Current.Name } | Where-Object { $_ })) { if (-not $InstallNames.Contains($N)) { $InstallNames.Add($N) } } }
            & $ReadInstallTexts
            try {
                $AppScroll = $AppList.GetCurrentPattern([System.Windows.Automation.ScrollPattern]::Pattern)
                if ($AppScroll.Current.VerticallyScrollable) {
                    foreach ($Percent in 25, 50, 75, 100) {
                        $AppScroll.SetScrollPercent([System.Windows.Automation.ScrollPattern]::NoScroll, $Percent)
                        Start-Sleep -Milliseconds 300
                        & $ReadInstallTexts
                    }
                    $AppScroll.SetScrollPercent([System.Windows.Automation.ScrollPattern]::NoScroll, 0)
                }
            }
            catch { Add-Step "instalacja: przewijanie listy pominiete ($($_.Exception.Message))" }
        }
        # Sonda konczy na widoku 'Wszystkie' (pierwsza pozycja), nie na pustym 'Zaznaczone'.
        $FirstItem = @($CategoryList.FindAll($Scope, $ByInstallItem)) | Select-Object -First 1
        if ($null -ne $FirstItem) { $FirstItem.GetCurrentPattern([System.Windows.Automation.SelectionItemPattern]::Pattern).Select(); Start-Sleep -Milliseconds 500 }
        $Out.InstallCategories = @($CategoryNames)
        $Out.InstallTexts = @($InstallNames)
        $Out.InstallButtonText = [string]$InstallButton.Current.Name
        $Out.InstallButtonEnabled = [bool]$InstallButton.Current.IsEnabled
        Add-Step "instalacja: kategorii $($CategoryNames.Count), tekstow listy $($InstallNames.Count), przycisk '$($Out.InstallButtonText)' aktywny=$($Out.InstallButtonEnabled)"
    }
    catch { $Out.InstallError = $_.Exception.Message; Add-Step "instalacja: BLAD $($Out.InstallError)" }
    $Out.Ok = $true
}
catch { $Out.Error = $_.Exception.Message }
$Json = $Out | ConvertTo-Json -Depth 8
Set-Content -LiteralPath $ResultFile -Value $Json -Encoding UTF8
'@
}

function ConvertTo-DsoHitTestRange {
    # Przekrój pionowy [{Y, Ht}] -> zwarte zakresy "od..do: kod" (kolejne Y z tym samym kodem łączone).
    [CmdletBinding()]
    [OutputType([string[]])]
    param([AllowNull()][AllowEmptyCollection()][object[]]$Scan)
    $Ranges = New-Object System.Collections.Generic.List[string]
    $Start = $null; $Last = $null; $Code = $null
    foreach ($Point in @($Scan)) {
        if ($null -eq $Point) { continue }
        $Y = [int]$Point.Y; $Ht = [int]$Point.Ht
        if ($null -ne $Code -and $Ht -eq $Code -and $Y -eq $Last + 1) { $Last = $Y; continue }
        if ($null -ne $Code) { $Ranges.Add("$Start..${Last}: $Code") }
        $Start = $Y; $Last = $Y; $Code = $Ht
    }
    if ($null -ne $Code) { $Ranges.Add("$Start..${Last}: $Code") }
    return $Ranges.ToArray()
}

function Get-GuiPrivacySuggestedId {
    # Identyfikatory sugerowanych tweaków kategorii „Prywatność i AI” (category=privacy, kind=tweak, suggested=true) z plików katalogu
    # (wbudowany katalog repo: src\DSOptimizer.Catalog\catalog). Pusty wynik = błąd, bo sprawdzenie nie miałoby czego liczyć.
    # apps-bloat.json i apps.json leżą w tym samym katalogu, ale to klasyfikacja aplikacji i lista instalacji (obiekty entries),
    # nie pozycje katalogu: pomijane jawnie po nazwie, tak jak w BuiltInCatalog (osobne zasoby AppsBloatSource i AppsInstallSource, poza Sources).
    [CmdletBinding()]
    [OutputType([string[]])]
    param([Parameter(Mandatory)][string]$CatalogDirectory)
    $Ids = New-Object System.Collections.Generic.List[string]
    $TweakFiles = @(Get-ChildItem -LiteralPath $CatalogDirectory -Filter '*.json' -File | Where-Object { $_.Name -notin @('apps-bloat.json', 'apps.json') } | Sort-Object Name)
    foreach ($File in $TweakFiles) {
        $Items = [System.IO.File]::ReadAllText($File.FullName, [System.Text.Encoding]::UTF8) | ConvertFrom-Json
        foreach ($Item in @($Items)) {
            $Props = $Item.PSObject.Properties
            $Suggested = $null -ne $Props['suggested'] -and $Item.suggested -eq $true
            if ([string]$Item.category -eq 'privacy' -and [string]$Item.kind -eq 'tweak' -and $Suggested) { $Ids.Add([string]$Item.id) }
        }
    }
    if ($Ids.Count -eq 0) { throw "Brak sugerowanych tweaków kategorii privacy w '$CatalogDirectory'." }
    return $Ids.ToArray()
}

function Get-GuiRepairTitle {
    # Tytuły pozycji kategorii „Naprawy” (category=repair) z plików katalogu (wbudowany katalog repo: src\DSOptimizer.Catalog\catalog),
    # w kolejności plików i wpisów; apps-bloat.json i apps.json pomijane jawnie jak w Get-GuiPrivacySuggestedId. Pusty wynik = błąd.
    [CmdletBinding()]
    [OutputType([string[]])]
    param([Parameter(Mandatory)][string]$CatalogDirectory)
    $Titles = New-Object System.Collections.Generic.List[string]
    $Files = @(Get-ChildItem -LiteralPath $CatalogDirectory -Filter '*.json' -File | Where-Object { $_.Name -notin @('apps-bloat.json', 'apps.json') } | Sort-Object Name)
    foreach ($File in $Files) {
        $Items = [System.IO.File]::ReadAllText($File.FullName, [System.Text.Encoding]::UTF8) | ConvertFrom-Json
        foreach ($Item in @($Items)) {
            if ([string]$Item.category -eq 'repair') { $Titles.Add([string]$Item.title) }
        }
    }
    if ($Titles.Count -eq 0) { throw "Brak pozycji kategorii repair w '$CatalogDirectory'." }
    return $Titles.ToArray()
}

function Get-GuiInstallTitle {
    # Oczekiwania strony „Instalacja” (krok 11 sondy) z apps.json (wbudowana lista repo: src\DSOptimizer.Catalog\catalog\apps.json):
    # Titles = tytuły wpisów w kolejności pliku, Categories = „Wszystkie” (D1), „Zaznaczone”, potem nazwy kategorii w UI (InstallText.CategoryName)
    # w kolejności strony (InstallCategory), tylko te, które mają wpisy. Brak pliku, pusta lista albo nieznana kategoria = błąd.
    [CmdletBinding()]
    [OutputType([pscustomobject])]
    param([Parameter(Mandatory)][string]$CatalogDirectory)
    $Path = Join-Path $CatalogDirectory 'apps.json'
    if (-not (Test-Path -LiteralPath $Path -PathType Leaf)) { throw "Brak pliku apps.json w '$CatalogDirectory'." }
    $Entries = @(([System.IO.File]::ReadAllText($Path, [System.Text.Encoding]::UTF8) | ConvertFrom-Json).entries | Where-Object { $null -ne $_ })
    if ($Entries.Count -eq 0) { throw "Pusta lista wpisów w '$Path' (apps.json)." }
    $Titles = New-Object System.Collections.Generic.List[string]
    $Used = New-Object System.Collections.Generic.HashSet[string]
    foreach ($Entry in $Entries) {
        $Category = [string]$Entry.category
        if (-not $script:InstallCategoryNames.Contains($Category)) { throw "Nieznana kategoria '$Category' wpisu $($Entry.id) w apps.json." }
        $Titles.Add([string]$Entry.title)
        $null = $Used.Add($Category)
    }
    $Categories = @($script:InstallAllCategoriesName, $script:InstallSelectedCategoryName) + @($script:InstallCategoryNames.Keys | Where-Object { $Used.Contains($_) } | ForEach-Object { $script:InstallCategoryNames[$_] })
    return [pscustomobject]@{ Titles = $Titles.ToArray(); Categories = $Categories }
}

function Get-GuiPrivacyExpectedCount {
    # Oczekiwane N w „Zastosuj (N)” po pierwszym wejściu na „Prywatność i AI”: sugerowane pozycje w stanie NotApplied albo Partial
    # (ta sama reguła co SuggestedSelection w Core) według raportów CLI --detect z tego przebiegu. Count = $null, gdy którejś pozycji brakuje
    # raportu albo stanu, stan jest spoza Applied/NotApplied/Partial, albo CLI nie było pewne użytkownika (HKCU czytane z innego konta).
    [CmdletBinding()]
    param(
        [Parameter(Mandatory)][string[]]$SuggestedId,
        [AllowNull()][AllowEmptyCollection()][object[]]$Detection
    )
    $Known = @('Applied', 'NotApplied', 'Partial')
    $Problems = @()
    $Parts = @()
    $Count = 0
    foreach ($Id in $SuggestedId) {
        $Entry = @($Detection) | Where-Object { $null -ne $_ -and [string]$_.Id -eq $Id } | Select-Object -First 1
        if ($null -eq $Entry) { $Problems += "$Id`: brak raportu detekcji"; continue }
        $State = [string]$Entry.State
        $Parts += "$Id=$(if ($State) { $State } else { 'brak' })"
        if (-not $State) { $Problems += "$Id`: brak stanu ($($Entry.Error))"; continue }
        if ($Known -notcontains $State) { $Problems += "$Id=$State`: stan spoza Applied/NotApplied/Partial"; continue }
        if ($Entry.IsCertain -ne $true) { $Problems += "$Id`: użytkownik niepewny (SID $($Entry.UserSid))"; continue }
        if ($State -eq 'NotApplied' -or $State -eq 'Partial') { $Count++ }
    }
    $Detail = "detekcja: $($Parts -join ', ')"
    if ($Problems.Count -gt 0) { return [pscustomobject]@{ Count = $null; Detail = "$Detail; problemy: $($Problems -join '; ')" } }
    return [pscustomobject]@{ Count = $Count; Detail = $Detail }
}

function Get-GuiAppsExpectedCount {
    # Oczekiwane N w „Usuń (N)” po pierwszym wejściu na „Aplikacje”: wiersze grupy Suggested z Removability=Removable (ta sama reguła co
    # AppRow.CanRemove + AppSuggestionTracker w Core) z raportu CLI --list-apps tego przebiegu (wynik VmRun\Invoke-DsoCli).
    # Count = $null, gdy CLI nie zwróciło wyniku, przekroczyło limit czasu, skończyło się kodem różnym od 0 albo raport nie ma listy.
    [CmdletBinding()]
    param([AllowNull()]$Listing)
    if ($null -eq $Listing) { return [pscustomobject]@{ Count = $null; Detail = '--list-apps: brak wyniku' } }
    if ($Listing.TimedOut -eq $true) { return [pscustomobject]@{ Count = $null; Detail = '--list-apps: limit czasu' } }
    if ([int]$Listing.ExitCode -ne 0) { return [pscustomobject]@{ Count = $null; Detail = "--list-apps: kod $($Listing.ExitCode)" } }
    $Report = $Listing.Report
    if ($null -eq $Report) { return [pscustomobject]@{ Count = $null; Detail = "--list-apps: brak raportu ($($Listing.ReportError))" } }
    if ($null -eq $Report.PSObject.Properties['apps'] -or $null -eq $Report.apps) { return [pscustomobject]@{ Count = $null; Detail = '--list-apps: raport bez listy aplikacji' } }
    $All = @($Report.apps | Where-Object { $null -ne $_ })
    $Suggested = @($All | Where-Object { [string]$_.group -eq 'Suggested' -and [string]$_.removability -eq 'Removable' })
    $User = if ($null -ne $Report.PSObject.Properties['user'] -and $null -ne $Report.user) { "konto $($Report.user.sid), pewne: $($Report.user.isCertain)" } else { 'konto ?' }
    $Names = if ($Suggested.Count -gt 0) { ': ' + (($Suggested | ForEach-Object { [string]$_.name }) -join ', ') } else { '' }
    return [pscustomobject]@{ Count = $Suggested.Count; Detail = "--list-apps ($User): wierszy $($All.Count), sugerowanych do usunięcia $($Suggested.Count)$Names" }
}

function New-GuiUiCheck {
    [CmdletBinding()]
    param([string]$Name, [bool]$Passed, [string]$Detail)
    return [pscustomobject]@{ Name = $Name; Passed = $Passed; Detail = $Detail }
}

function Get-GuiUiCheck {
    # Czysta agregacja wyniku sondy UI -> { Name, Passed, Detail }: HistoryMenu, HistoryEntryActive, CaptionHitTest, FilterRiskOpened, FilterRiskSelected, FilterHistoryOpened, FilterHistorySelected, RowClickPreview, RowDoubleClickToggles, RunIconRuns, RunIconDoubleClickOnce, PrivacySuggestedApply, AppsSuggestedRemove, PrepPage, RepairPage, InstallPage. Sonda $null = niezaliczone.
    # -PrivacyExpected: wynik Get-GuiPrivacyExpectedCount ({ Count, Detail }); brak albo Count = $null = PrivacySuggestedApply niezaliczone.
    # -AppsExpected: wynik Get-GuiAppsExpectedCount ({ Count, Detail }); brak albo Count = $null = AppsSuggestedRemove niezaliczone.
    # -SettingsOpenable: PrepLinks.SettingsOpenable (domyślnie stała modułu); true = krok 2 strony „Przygotowanie” ma przycisk, false = notatkę.
    # -RepairTitles: wynik Get-GuiRepairTitle (tytuły kategorii repair z katalogu); brak albo pusta lista = RepairPage niezaliczone.
    # -InstallExpected: wynik Get-GuiInstallTitle ({ Titles, Categories } z apps.json); brak albo puste tytuły = InstallPage niezaliczone.
    [CmdletBinding()]
    param(
        [AllowNull()]$Probe, [AllowNull()]$PrivacyExpected, [AllowNull()]$AppsExpected, [bool]$SettingsOpenable = $script:PrepSettingsOpenable,
        [AllowNull()][AllowEmptyCollection()][string[]]$RepairTitles, [AllowNull()]$InstallExpected
    )
    $Names = 'HistoryMenu', 'HistoryEntryActive', 'CaptionHitTest', 'FilterRiskOpened', 'FilterRiskSelected', 'FilterHistoryOpened', 'FilterHistorySelected', 'RowClickPreview', 'RowDoubleClickToggles', 'RunIconRuns', 'RunIconDoubleClickOnce', 'PrivacySuggestedApply', 'AppsSuggestedRemove', 'PrepPage', 'RepairPage', 'InstallPage'
    if ($null -eq $Probe) { return , @($Names | ForEach-Object { New-GuiUiCheck -Name $_ -Passed $false -Detail 'brak odczytu sondy UI' }) }
    $Props = $Probe.PSObject.Properties
    $Get = { param($N, $Default) if ($null -ne $Props[$N] -and $null -ne $Props[$N].Value) { $Props[$N].Value } else { $Default } }
    $ProbeError = & $Get 'Error' $null
    $Checks = @()

    $MenuFound = [bool](& $Get 'HistoryMenuFound' $false)
    $Opened = [bool](& $Get 'HistoryPageOpened' $false)
    $MenuDetail = "pozycja menu: $MenuFound, strona (lista + filtr): $Opened"
    if ($ProbeError) { $MenuDetail += "; sonda: $ProbeError" }
    $Checks += New-GuiUiCheck -Name 'HistoryMenu' -Passed ($MenuFound -and $Opened) -Detail $MenuDetail

    $Title = $script:UiProbeTweakTitle
    $Rows = @(& $Get 'EntryNames' @())
    $Active = @($Rows | Where-Object { [string]$_ -like "$Title, *, Aktywna, *" })
    $ApplyError = & $Get 'ApplyError' $null
    $EntryDetail = if ($Active.Count -gt 0) { "wiersz: $($Active[0])" } else { "brak wiersza '$Title, <rodzaj wpisu>, Aktywna, …' (wiersze: $($Rows.Count)$(if ($Rows.Count) { ': ' + ($Rows -join ' | ') }))" }
    if ($ApplyError) { $EntryDetail += "; zastosowanie: $ApplyError" }
    $HistoryError = & $Get 'HistoryError' $null
    if ($HistoryError) { $EntryDetail += "; historia: $HistoryError" }
    $Checks += New-GuiUiCheck -Name 'HistoryEntryActive' -Passed ($Active.Count -gt 0) -Detail $EntryDetail

    # Oba stany okna: środek paska i 2 px nad jego dołem = HTCAPTION, 2 px pod paskiem = HTCLIENT.
    $HitError = & $Get 'HitTestError' $null
    $Measured = @(& $Get 'HitTest' @()) | Where-Object { $null -ne $_ }
    $Passed = $true
    $Parts = @()
    foreach ($State in 'Normal', 'Maximized') {
        $Entry = $Measured | Where-Object { [string]$_.State -eq $State } | Select-Object -First 1
        if ($null -eq $Entry) { $Passed = $false; $Parts += "$State`: brak pomiaru$(if ($HitError) { " ($HitError)" })"; continue }
        $Points = @($Entry.Points)
        $Bad = @($Points | Where-Object {
                $Expected = if ([string]$_.Name -eq 'below') { $script:HtClient } else { $script:HtCaption }
                [int]$_.Ht -ne $Expected
            })
        # Granica co do piksela z przekroju: BarBottom (BoundingRectangle.Bottom) jest krawędzią wyłączną, więc ostatni wiersz
        # paska to BarBottom−1 (HTCAPTION), a BarBottom to już treść (HTCLIENT).
        $Scan = @($Entry.Scan) | Where-Object { $null -ne $_ }
        $Bottom = [int]$Entry.BarBottom
        $LastRow = $Scan | Where-Object { [int]$_.Y -eq $Bottom - 1 } | Select-Object -First 1
        $FirstBelow = $Scan | Where-Object { [int]$_.Y -eq $Bottom } | Select-Object -First 1
        $EdgeOk = $null -ne $LastRow -and $null -ne $FirstBelow -and [int]$LastRow.Ht -eq $script:HtCaption -and [int]$FirstBelow.Ht -eq $script:HtClient
        # Środek paska (tekst komputera) musi być zmierzony: bez niego stara sonda przepuszczała martwy obszar przeciągania.
        $CenterCount = @($Points | Where-Object { [string]$_.XDip -eq 'srodek' -and [string]$_.Name -ne 'below' }).Count
        if ($Points.Count -eq 0 -or $Bad.Count -gt 0 -or -not $EdgeOk -or $CenterCount -lt 2) { $Passed = $false }
        $EdgeText = "y=$($Bottom - 1)=$(if ($LastRow) { $LastRow.Ht } else { 'brak' }), y=$Bottom=$(if ($FirstBelow) { $FirstBelow.Ht } else { 'brak' })"
        $Text = ($Points | ForEach-Object { "$($_.Name)@$($_.XDip)DIP(y=$($_.Y))=$($_.Ht)" }) -join ', '
        $Ranges = (ConvertTo-DsoHitTestRange -Scan $Scan) -join '; '
        $Parts += "$State`: pasek $($Entry.BarTop)..$($Bottom - 1); $Text; krawędź $EdgeText; przekrój x=$($Entry.ScanX): $Ranges"
    }
    $Checks += New-GuiUiCheck -Name 'CaptionHitTest' -Passed $Passed -Detail ($Parts -join ' | ')

    # Filtry klikane prawdziwą myszą: brak pola wyniku = niezaliczone (stara sonda nie klikała nic).
    $FilterError = & $Get 'FilterError' $null
    $FilterSuffix = if ($FilterError) { "; filtry: $FilterError" } else { '' }
    $FilterSpecs = @(
        @{ Name = 'FilterRiskOpened'; Text = "pole 'Ryzyko' rozwija się po kliknięciu myszą" }
        @{ Name = 'FilterRiskSelected'; Text = "klik w drugą opcję listy 'Ryzyko' zmienia wartość, klik w pierwszą ją przywraca" }
        @{ Name = 'FilterHistoryOpened'; Text = "pole 'Filtr' w Historii zmian rozwija się po kliknięciu myszą" }
        @{ Name = 'FilterHistorySelected'; Text = "klik w drugą opcję listy 'Filtr' zmienia wartość, klik w pierwszą ją przywraca" }
    )
    foreach ($Spec in $FilterSpecs) {
        $Present = $null -ne $Props[$Spec.Name]
        $Value = [bool](& $Get $Spec.Name $false)
        $Detail = if (-not $Present) { "brak pola $($Spec.Name) w wyniku sondy" } elseif ($Value) { $Spec.Text } else { "NIE: $($Spec.Text)" }
        $Checks += New-GuiUiCheck -Name $Spec.Name -Passed $Value -Detail ($Detail + $FilterSuffix)
    }

    # Wiersze listy klikane prawdziwą myszą (model kliknięcia): brak pola wyniku = niezaliczone.
    $RowError = & $Get 'RowError' $null
    $RowSuffix = if ($RowError) { "; wiersze: $RowError" } else { '' }
    $RowSpecs = @(
        @{ Name = 'RowClickPreview'; Text = 'pojedynczy klik w tytuł wiersza tweaka tylko go wybiera, pole wyboru bez zmiany' }
        @{ Name = 'RowDoubleClickToggles'; Text = 'dwuklik w wiersz tweaka zmienia pole wyboru, drugi dwuklik je przywraca' }
        @{ Name = 'RunIconRuns'; Text = 'klik w znacznik uruchomienia panelu otwiera dokładnie jedno nowe okno mmc.exe' }
        @{ Name = 'RunIconDoubleClickOnce'; Text = 'dwuklik w znacznik uruchomienia panelu otwiera dokładnie jedno nowe okno mmc.exe' }
    )
    foreach ($Spec in $RowSpecs) {
        $Present = $null -ne $Props[$Spec.Name]
        $Value = [bool](& $Get $Spec.Name $false)
        $Detail = if (-not $Present) { "brak pola $($Spec.Name) w wyniku sondy" } elseif ($Value) { $Spec.Text } else { "NIE: $($Spec.Text)" }
        $Checks += New-GuiUiCheck -Name $Spec.Name -Passed $Value -Detail ($Detail + $RowSuffix)
    }

    # „Prywatność i AI” (krok 7 sondy): N z przycisku serii = N z detekcji CLI. N = 0 przechodzi także bez przycisku (pasek serii ukryty), o ile sonda nie zgłosiła błędu.
    $ExpectedCount = $null
    $ExpectedDetail = 'brak oczekiwanego N (detekcja nie zwróciła wyniku)'
    if ($null -ne $PrivacyExpected) { $ExpectedCount = $PrivacyExpected.Count; $ExpectedDetail = [string]$PrivacyExpected.Detail }
    $HasField = $null -ne $Props['PrivacyButtonFound']
    $Found = [bool](& $Get 'PrivacyButtonFound' $false)
    $Shown = & $Get 'PrivacyApplyCount' $null
    $ShownText = & $Get 'PrivacyApplyText' $null
    $PrivacyError = & $Get 'PrivacyError' $null
    $PrivacyPassed = $false
    if ($HasField -and $null -ne $ExpectedCount) {
        if ($Found -and $null -ne $Shown) { $PrivacyPassed = ([int]$Shown -eq [int]$ExpectedCount) }
        elseif (-not $Found -and -not $PrivacyError) { $PrivacyPassed = ([int]$ExpectedCount -eq 0) }
    }
    $ShownDetail = if (-not $HasField) { 'brak pola PrivacyButtonFound w wyniku sondy' } elseif ($Found) { "przycisk '$ShownText'" } else { 'brak przycisku serii' }
    $PrivacyDetail = "$ShownDetail; oczekiwano $(if ($null -ne $ExpectedCount) { $ExpectedCount } else { '?' }) ($ExpectedDetail)"
    if ($PrivacyError) { $PrivacyDetail += "; sonda: $PrivacyError" }
    $Checks += New-GuiUiCheck -Name 'PrivacySuggestedApply' -Passed $PrivacyPassed -Detail $PrivacyDetail

    # „Aplikacje” (krok 8 sondy): N z „Usuń (N)” po zakończonym odczycie listy = N z --list-apps. Pasek serii Aplikacji jest zawsze
    # widoczny, więc brak przycisku nie przechodzi także przy N = 0.
    $AppsCount = $null
    $AppsExpectedDetail = 'brak oczekiwanego N (--list-apps nie zwróciło wyniku)'
    if ($null -ne $AppsExpected) { $AppsCount = $AppsExpected.Count; $AppsExpectedDetail = [string]$AppsExpected.Detail }
    $HasApps = $null -ne $Props['AppsButtonFound']
    $AppsRead = [bool](& $Get 'AppsListRead' $false)
    $AppsFound = [bool](& $Get 'AppsButtonFound' $false)
    $AppsShown = & $Get 'AppsRemoveCount' $null
    $AppsText = & $Get 'AppsRemoveText' $null
    $AppsError = & $Get 'AppsError' $null
    $AppsPassed = $HasApps -and $AppsRead -and $AppsFound -and $null -ne $AppsShown -and $null -ne $AppsCount -and ([int]$AppsShown -eq [int]$AppsCount)
    $AppsShownDetail = if (-not $HasApps) { 'brak pola AppsButtonFound w wyniku sondy' } elseif ($AppsFound) { "przycisk '$AppsText'" } else { 'brak przycisku serii' }
    if ($HasApps -and -not $AppsRead) { $AppsShownDetail += ' (lista nieodczytana do końca)' }
    $AppsDetail = "$AppsShownDetail; oczekiwano $(if ($null -ne $AppsCount) { $AppsCount } else { '?' }) ($AppsExpectedDetail)"
    if ($AppsError) { $AppsDetail += "; sonda: $AppsError" }
    $Checks += New-GuiUiCheck -Name 'AppsSuggestedRemove' -Passed $AppsPassed -Detail $AppsDetail

    # „Przygotowanie” (krok 9 sondy): odczyt skończony w 60 s, 7 tytułów kroków, przycisk „Otwórz Windows Update” (albo notatka zastępcza
    # przy SettingsOpenable = false) i co najmniej jedna linia karty w kroku „Grafika”. Brak pól kroku 9 (stara sonda) = niezaliczone.
    if ($null -eq $Props['PrepMenuFound']) {
        $Checks += New-GuiUiCheck -Name 'PrepPage' -Passed $false -Detail 'brak pola PrepMenuFound w wyniku sondy'
    }
    else {
        $Checks += Get-GuiPrepPageCheck -Get $Get -SettingsOpenable $SettingsOpenable
    }

    # „Naprawy” (krok 10 sondy): pozycja menu, lista „Pozycje” i każdy tytuł kategorii repair z katalogu wśród tekstów listy.
    # Brak pól kroku 10 (stara sonda) albo brak oczekiwanych tytułów = niezaliczone.
    if ($null -eq $Props['RepairMenuFound']) {
        $Checks += New-GuiUiCheck -Name 'RepairPage' -Passed $false -Detail 'brak pola RepairMenuFound w wyniku sondy'
    }
    else {
        $Checks += Get-GuiRepairPageCheck -Get $Get -RepairTitles $RepairTitles
    }

    # „Instalacja” (krok 11 sondy): pozycja menu, koniec odczytu z podtytułem źródła (winget działa), każda kategoria i każdy tytuł
    # z apps.json wśród tekstów listy, „Zainstaluj (0)” nieaktywne. Brak pól kroku 11 (stara sonda) albo oczekiwań = niezaliczone.
    if ($null -eq $Props['InstallMenuFound']) {
        $Checks += New-GuiUiCheck -Name 'InstallPage' -Passed $false -Detail 'brak pola InstallMenuFound w wyniku sondy'
    }
    else {
        $Checks += Get-GuiInstallPageCheck -Get $Get -InstallExpected $InstallExpected
    }
    return , $Checks
}

function Get-GuiInstallPageCheck {
    # Sprawdzenie InstallPage (krok 11 sondy) z odczytu pól sondy przez -Get (blok z Get-GuiUiCheck).
    [CmdletBinding()]
    param([Parameter(Mandatory)][scriptblock]$Get, [AllowNull()]$InstallExpected)
    $ExpectedTitles = @()
    $ExpectedCategories = @()
    if ($null -ne $InstallExpected) {
        $ExpectedTitles = @(@($InstallExpected.Titles) | Where-Object { -not [string]::IsNullOrWhiteSpace($_) })
        $ExpectedCategories = @(@($InstallExpected.Categories) | Where-Object { -not [string]::IsNullOrWhiteSpace($_) })
    }
    $Menu = [bool](& $Get 'InstallMenuFound' $false)
    $Loaded = [bool](& $Get 'InstallLoaded' $false)
    $Subtitle = [string](& $Get 'InstallSubtitle' '')
    $SubtitleOk = $Subtitle.StartsWith($script:InstallSourcePrefix, [System.StringComparison]::Ordinal) -and -not $Subtitle.EndsWith('niedostępne', [System.StringComparison]::Ordinal)
    $Categories = @(@(& $Get 'InstallCategories' @()) | Where-Object { $null -ne $_ } | ForEach-Object { [string]$_ })
    $Texts = @(@(& $Get 'InstallTexts' @()) | Where-Object { $null -ne $_ } | ForEach-Object { [string]$_ })
    $MissingCategories = @($ExpectedCategories | Where-Object { $Categories -notcontains $_ })
    $MissingTitles = @($ExpectedTitles | Where-Object { $Texts -notcontains $_ })
    $ButtonText = & $Get 'InstallButtonText' $null
    $ButtonEnabled = & $Get 'InstallButtonEnabled' $null
    $ButtonOk = [string]$ButtonText -eq $script:InstallButtonIdle -and $null -ne $ButtonEnabled -and -not [bool]$ButtonEnabled
    $InstallError = & $Get 'InstallError' $null
    $Parts = @()
    $Parts += if ($Menu) { 'pozycja menu' } else { 'NIE: pozycja menu' }
    $Parts += if ($Loaded) { 'odczyt skończony' } else { 'NIE: odczyt skończony w 180 s' }
    $Parts += if ($SubtitleOk) { "podtytuł '$Subtitle'" } else { "NIE: podtytuł źródła ('$Subtitle')" }
    if ($ExpectedTitles.Count -eq 0) { $Parts += 'brak oczekiwanych tytułów (apps.json)' }
    else {
        $CategoryText = "kategorie $($ExpectedCategories.Count - $MissingCategories.Count)/$($ExpectedCategories.Count)"
        if ($MissingCategories.Count -gt 0) { $CategoryText += " (brak: $($MissingCategories -join ', '))" }
        $TitleText = "tytuły $($ExpectedTitles.Count - $MissingTitles.Count)/$($ExpectedTitles.Count)"
        if ($MissingTitles.Count -gt 0) { $TitleText += " (brak: $($MissingTitles -join ', '))" }
        $Parts += $CategoryText
        $Parts += $TitleText
    }
    $Parts += if ($null -eq $ButtonText) { 'brak przycisku serii' } else { "przycisk '$ButtonText'" }
    # Cudzysłowy „ ” to w PowerShellu ograniczniki napisu – tylko w napisach w apostrofach.
    $ButtonIdleText = '„' + $script:InstallButtonIdle + '” nieaktywny'
    $Parts += if ($ButtonOk) { $ButtonIdleText } else { 'NIE: ' + $ButtonIdleText }
    $Detail = $Parts -join '; '
    if ($InstallError) { $Detail += "; sonda: $InstallError" }
    $Passed = $Menu -and $Loaded -and $SubtitleOk -and $ExpectedTitles.Count -gt 0 -and $MissingCategories.Count -eq 0 -and $MissingTitles.Count -eq 0 -and $ButtonOk -and -not $InstallError
    return (New-GuiUiCheck -Name 'InstallPage' -Passed $Passed -Detail $Detail)
}

function Get-GuiRepairPageCheck {
    # Sprawdzenie RepairPage (krok 10 sondy) z odczytu pól sondy przez -Get (blok z Get-GuiUiCheck). Bez zmian względem Etapu 4b.
    [CmdletBinding()]
    param([Parameter(Mandatory)][scriptblock]$Get, [AllowNull()][AllowEmptyCollection()][string[]]$RepairTitles)
    $Expected = @($RepairTitles | Where-Object { -not [string]::IsNullOrWhiteSpace($_) })
    $RepairMenu = [bool](& $Get 'RepairMenuFound' $false)
    $RepairList = [bool](& $Get 'RepairListFound' $false)
    $RepairTexts = @(@(& $Get 'RepairTexts' @()) | Where-Object { $null -ne $_ } | ForEach-Object { [string]$_ })
    $RepairMissing = @($Expected | Where-Object { $RepairTexts -notcontains $_ })
    $RepairError = & $Get 'RepairError' $null
    $RepairParts = @()
    $RepairParts += if ($RepairMenu) { 'pozycja menu' } else { 'NIE: pozycja menu' }
    $RepairParts += if ($RepairList) { 'lista' } else { 'NIE: lista' }
    if ($Expected.Count -eq 0) { $RepairParts += 'brak oczekiwanych tytułów (katalog)' }
    else {
        $RepairTitleText = "tytuły $($Expected.Count - $RepairMissing.Count)/$($Expected.Count)"
        if ($RepairMissing.Count -gt 0) { $RepairTitleText += " (brak: $($RepairMissing -join ', '))" }
        $RepairParts += $RepairTitleText
    }
    $RepairDetail = $RepairParts -join '; '
    if ($RepairError) { $RepairDetail += "; sonda: $RepairError" }
    $RepairPassed = $RepairMenu -and $RepairList -and $Expected.Count -gt 0 -and $RepairMissing.Count -eq 0 -and -not $RepairError
    return (New-GuiUiCheck -Name 'RepairPage' -Passed $RepairPassed -Detail $RepairDetail)
}

function Get-GuiPrepPageCheck {
    # Sprawdzenie PrepPage (krok 9 sondy) z odczytu pól sondy przez -Get (blok z Get-GuiUiCheck). Bez zmian względem partii 3 Etapu 4a.
    [CmdletBinding()]
    param([Parameter(Mandatory)][scriptblock]$Get, [bool]$SettingsOpenable)
    $PrepMenu = [bool](& $Get 'PrepMenuFound' $false)
    $PrepLoaded = [bool](& $Get 'PrepLoaded' $false)
    $PrepTitles = @(& $Get 'PrepTitles' @())
    $PrepMissing = @($script:PrepStepTitles | Where-Object { $PrepTitles -notcontains $_ })
    $WuButton = [bool](& $Get 'PrepWindowsUpdateButton' $false)
    $WuNote = [bool](& $Get 'PrepWindowsUpdateNote' $false)
    $WuOk = if ($SettingsOpenable) { $WuButton } else { $WuNote }
    $Graphics = @(@(& $Get 'PrepGraphicsLines' @()) | Where-Object { $null -ne $_ } | ForEach-Object { [string]$_ })
    $CardOk = $Graphics.Count -gt 0 -and $Graphics -notcontains $script:PrepCardsStateLabel
    $PrepError = & $Get 'PrepError' $null
    $PrepPassed = $PrepMenu -and $PrepLoaded -and $PrepMissing.Count -eq 0 -and $WuOk -and $CardOk -and -not $PrepError
    $PrepParts = @()
    $PrepParts += if ($PrepMenu) { 'pozycja menu' } else { 'NIE: pozycja menu' }
    $PrepParts += if ($PrepLoaded) { 'odczyt skończony' } else { 'NIE: odczyt skończony (nadal „Wczytywanie…” albo brak tytułów po 60 s)' }
    $TitleText = "tytuły $($script:PrepStepTitles.Count - $PrepMissing.Count)/$($script:PrepStepTitles.Count)"
    if ($PrepMissing.Count -gt 0) { $TitleText += " (brak: $($PrepMissing -join ', '))" }
    $PrepParts += $TitleText
    $WuText = if ($SettingsOpenable) { 'przycisk „Otwórz Windows Update”' } else { 'notatka „Otwórz Ustawienia › Windows Update.”' }
    $PrepParts += if ($WuOk) { $WuText } else { "NIE: $WuText" }
    $GraphicsText = if ($Graphics.Count -gt 0) { $Graphics -join ' | ' } else { 'pusto' }
    $PrepParts += if ($CardOk) { "Grafika: $GraphicsText" } else { "Grafika: brak linii karty ($GraphicsText)" }
    $PrepDetail = $PrepParts -join '; '
    if ($PrepError) { $PrepDetail += "; sonda: $PrepError" }
    return (New-GuiUiCheck -Name 'PrepPage' -Passed $PrepPassed -Detail $PrepDetail)
}

Export-ModuleMember -Function Get-GuiUiProbeTarget, Get-GuiPrepExpected, Get-GuiUiProbeScript, ConvertTo-DsoHitTestRange, Get-GuiPrivacySuggestedId, Get-GuiRepairTitle, Get-GuiInstallTitle, Get-GuiPrivacyExpectedCount, Get-GuiAppsExpectedCount, Get-GuiUiCheck
