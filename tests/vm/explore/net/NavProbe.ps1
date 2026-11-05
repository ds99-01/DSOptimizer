<#
Instrukcja uruchomienia:
    Nie uruchamiać ręcznie. Invoke-NetExploration.ps1 wysyła ten skrypt do VM i startuje go przez Invoke-DsoUserSessionScript
    (zadanie Harmonogramu w sesji użytkownika dso, Interactive + Highest) z parametrami -ResultFile <plik json> i -TextsFile <plik json>.
Opis działania:
    Sonda nawigacji okna programu (UI Automation): czeka na okno DSOptimizer, potem zaznacza kolejno każdą pozycję list z pliku
    tekstów (lists: nazwy dwóch list w języku okna) przez SelectionItemPattern, bez klikania przycisków i odnośników. Plik
    tekstów (JSON UTF-8: lists, slowPages) składa host z tabel tłumaczeń. Dłuższe oczekiwanie na stronach, których nazwa zaczyna
    się od któregoś z slowPages (strony czytające system). Wynik JSON: Ok, Error, Steps, Pages.
    Plik wysyłany do PowerShella 5.1 w VM; kod bez znaków spoza ASCII.
#>
param([Parameter(Mandatory)][string]$ResultFile, [Parameter(Mandatory)][string]$TextsFile)
$ErrorActionPreference = 'Stop'
$Out = [ordered]@{ Ok = $false; Error = $null; Steps = @(); Pages = @() }
function Add-Step([string]$Text) { $script:Out.Steps += ('{0:HH:mm:ss.fff} {1}' -f (Get-Date), $Text) }
try {
    $Texts = [System.IO.File]::ReadAllText($TextsFile, [System.Text.Encoding]::UTF8).TrimStart([char]0xFEFF) | ConvertFrom-Json
    Add-Type -AssemblyName UIAutomationClient
    Add-Type -AssemblyName UIAutomationTypes
    $AE = [System.Windows.Automation.AutomationElement]
    $Limit = (Get-Date).AddSeconds(90)
    $Proc = $null
    do {
        $Proc = Get-Process -Name 'DSOptimizer' -ErrorAction SilentlyContinue | Where-Object { $_.MainWindowHandle -ne [IntPtr]::Zero } | Select-Object -First 1
        if ($Proc) { break }
        Start-Sleep -Milliseconds 500
    } while ((Get-Date) -lt $Limit)
    if (-not $Proc) { throw 'Brak okna DSOptimizer w 90 s.' }
    Add-Step "okno PID $($Proc.Id)"
    Start-Sleep -Seconds 10
    $Root = $AE::FromHandle($Proc.MainWindowHandle)
    $ListType = [System.Windows.Automation.ControlType]::List
    $ItemType = [System.Windows.Automation.ControlType]::ListItem
    foreach ($ListName in @($Texts.lists)) {
        $Cond = New-Object System.Windows.Automation.AndCondition(
            (New-Object System.Windows.Automation.PropertyCondition($AE::NameProperty, $ListName)),
            (New-Object System.Windows.Automation.PropertyCondition($AE::ControlTypeProperty, $ListType)))
        $List = $Root.FindFirst([System.Windows.Automation.TreeScope]::Descendants, $Cond)
        if (-not $List) { Add-Step "brak listy $ListName"; continue }
        $Items = @($List.FindAll([System.Windows.Automation.TreeScope]::Children,
                (New-Object System.Windows.Automation.PropertyCondition($AE::ControlTypeProperty, $ItemType))))
        Add-Step "lista ${ListName}: $($Items.Count) pozycji"
        foreach ($Item in $Items) {
            $Name = [string]$Item.Current.Name
            $Item.GetCurrentPattern([System.Windows.Automation.SelectionItemPattern]::Pattern).Select()
            $Wait = 8
            foreach ($Slow in @($Texts.slowPages)) {
                if ($Name.StartsWith([string]$Slow, [System.StringComparison]::Ordinal)) { $Wait = 45; break }
            }
            Start-Sleep -Seconds $Wait
            $Out.Pages += [ordered]@{ List = $ListName; Name = $Name; WaitSeconds = $Wait; At = (Get-Date).ToUniversalTime().ToString('o') }
            Add-Step "strona $Name ($Wait s)"
        }
    }
    $Out.Ok = $true
}
catch { $Out.Error = $_.Exception.Message; Add-Step "BLAD $($Out.Error)" }
$Out | ConvertTo-Json -Depth 5 | Set-Content -LiteralPath $ResultFile -Encoding UTF8
