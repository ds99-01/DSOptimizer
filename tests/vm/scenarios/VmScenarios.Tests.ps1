<#
Instrukcja uruchomienia:
    Nie uruchamiaj ręcznie. Plik wczytuje Invoke-DsoVmRun (tests\vm\modules\VmHost.psm1) przez
    New-PesterContainer -Data @{ Session; Cases; Layout; RecordDirectory; Reconnect } po przywróceniu VM i skopiowaniu builda.
Opis działania:
    Dla każdego przypadku z cases.psd1 wykonuje przebieg (Full: detect -> apply -> detect -> undo -> detect; ApplyOnly; DetectOnly; Execute: --execute + Check + Cleanup;
    Reboot: Full z prawdziwym restartem VM po apply i po undo; opcjonalnie CheckAfterApply/CheckAfterUndo, KeyAbsentAfterUndo i KeyPresentAfterUndo;
    AppRemove: --list-apps -> --remove-app <AppId> -> --list-apps + Check i Cleanup;
    AppInstall: --install-apps <AppIds> albo --list-install [--apps-catalog] + Check i Cleanup) przeciw VM
    (Invoke-VmCase), zapisuje rekord do <RecordDirectory>\<Name>.json i sprawdza wyniki. Każdy brak raportu CLI,
    niezaładowany hive użytkownika albo wyjątek przebiegu to czerwony test z czytelnym komunikatem - nigdy zielony "na pusto".
#>
param($Session, $Cases, $Layout, $RecordDirectory, $Reconnect)

BeforeDiscovery {
    # Pester 5 nie uruchamia BeforeAll bloku, w którym wszystkie testy są pominięte, więc rekord przypadku ze Skip
    # (Skipped, SkipReason) zapisujemy tutaj - dzięki temu pominięcie jest widoczne w raporcie, a nie ginie po cichu.
    $null = New-Item -ItemType Directory -Path $RecordDirectory -Force
    if (-not (Get-Command New-VmSkipRecord -ErrorAction SilentlyContinue)) { Import-Module (Join-Path $PSScriptRoot '..\modules\VmCommon.psm1') }
    foreach ($SkippedCase in @($Cases | Where-Object { $_.ContainsKey('Skip') })) {
        $SafeSkipName = $SkippedCase.Name -replace '[^A-Za-z0-9._-]', '_'
        $SkipRecord = New-VmSkipRecord -Case $SkippedCase
        [System.IO.File]::WriteAllText((Join-Path $RecordDirectory "$SafeSkipName.json"), ($SkipRecord | ConvertTo-Json -Depth 5), (New-Object System.Text.UTF8Encoding($true)))
    }
    $script:CaseData = @($Cases | ForEach-Object {
            $Item = @{} + $_
            $Item.Case = $_
            # Tytuł bloku: id tweaka albo aplikacji (Flow AppRemove ma AppId, AppInstall polecenie i AppIds zamiast TweakId).
            $Item.Id = if ($_.ContainsKey('AppId')) { [string]$_.AppId }
            elseif ($_.ContainsKey('Command')) { $Ids = if ($_.ContainsKey('AppIds')) { @($_.AppIds) -join ',' } else { '' }; ([string]$_.Command + ' ' + $Ids).Trim() }
            else { [string]$_.TweakId }
            $Item.IsSkipped = $_.ContainsKey('Skip')
            $Item.SkipReason = if ($_.ContainsKey('Skip')) { [string]$_.Skip } else { $null }
            $Item
        })
}

BeforeAll {
    # Moduły zwykle są już wczytane przez runner; import tylko, gdy brakuje poleceń (nie nadpisuje podmian w teście próbnym).
    if (-not (Get-Command Invoke-VmCase -ErrorAction SilentlyContinue)) { Import-Module (Join-Path $PSScriptRoot '..\modules\VmRun.psm1') }

    function Get-DsoUserSidsFromCase {
        param([hashtable]$Case)
        $Text = $Case | ConvertTo-Json -Depth 20 -Compress
        $Sids = foreach ($Match in [regex]::Matches($Text, '(?:HKU|HKEY_USERS)\\+(S-1-[0-9-]+)')) { $Match.Groups[1].Value }
        return @($Sids | Select-Object -Unique)
    }

    function Assert-DsoValueIn {
        # Porównanie z uwzględnieniem wielkości liter (nazwy stanów/wyników to nazwy enumów). Puste dane = porażka, nie zielony test.
        param($Actual, $Allowed, [string]$What)
        $List = @($Allowed | ForEach-Object { [string]$_ })
        if ($List.Count -eq 0 -or @($List | Where-Object { $_ -ne '' }).Count -eq 0) {
            throw "$What : Pusta lista dozwolonych wartości - oczekiwanie przypadku jest błędne, nic nie zostało sprawdzone."
        }
        if ($null -eq $Actual -or [string]$Actual -eq '') {
            throw "$What : pusta wartość w raporcie (dozwolone: $($List -join ', '))."
        }
        ($List -ccontains [string]$Actual) | Should -BeTrue -Because "$What : jest '$Actual', dozwolone: $($List -join ', ')"
    }
    function Get-DsoCaseRecord {
        if ($script:HiveProblem) { throw $script:HiveProblem }
        if ($script:RunProblem) { throw "Przebieg przypadku zakończył się wyjątkiem: $script:RunProblem" }
        if ($null -eq $script:Record) { throw 'Brak rekordu przebiegu przypadku.' }
        return $script:Record
    }

    function Get-DsoStepResult {
        # Zwraca krok o danej nazwie; brak kroku albo brak raportu CLI = porażka z kodem wyjścia i błędem odczytu.
        param([Parameter(Mandatory)][string]$StepName, [switch]$AllowNoReport)
        $Record = Get-DsoCaseRecord
        $Found = @($Record.Steps | Where-Object Step -eq $StepName)
        if ($Found.Count -ne 1) { throw "Brak kroku '$StepName' w rekordzie (znaleziono: $($Found.Count))." }
        $Step = $Found[0]
        if (-not $AllowNoReport) {
            $Raw = if ($Step.Json) { ([string]$Step.Json).Substring(0, [Math]::Min(300, ([string]$Step.Json).Length)) } else { '(brak pliku --out)' }
            $Step.Report | Should -Not -BeNullOrEmpty -Because "krok '$StepName' bez raportu CLI: exit $($Step.ExitCode), timeout $($Step.TimedOut), błąd odczytu '$($Step.ReportError)', surowy JSON: $Raw"
        }
        return $Step
    }

    function Get-DsoMainStepName {
        # Krok, którego raport niesie wynik operacji: execute (Execute), remove-app (AppRemove), apply (pozostałe).
        param([string]$Flow)
        switch ($Flow) {
            'Execute' { return 'execute' }
            'AppRemove' { return 'remove-app' }
            default { return 'apply' }
        }
    }

    function Get-DsoReportProperty {
        # Pole raportu CLI; brak pola to porażka (nie $null, który dałby zielone "false" na pusto).
        param($Step, [string]$Name)
        $Property = $Step.Report.PSObject.Properties[$Name]
        if ($null -eq $Property) { throw "Raport kroku '$($Step.Step)' nie ma pola '$Name' - nic nie zostało sprawdzone." }
        return $Property.Value
    }

    function Get-DsoListedApp {
        # Wiersze listy aplikacji (raport --list-apps) o danym id (bez rozróżniania wielkości liter, jak CLI).
        param($Step, [string]$AppId)
        $Apps = @(Get-DsoReportProperty -Step $Step -Name 'apps')
        return @($Apps | Where-Object { $null -ne $_ -and [string]$_.id -ieq $AppId })
    }
}

Describe '<Name> (<Id>)' -ForEach $script:CaseData {
    BeforeAll {
        $script:Record = $null
        $script:RunProblem = $null
        $script:HiveProblem = $null
        # Jawna blokada: przypadek ze Skip nigdy nie rusza VM i nie nadpisuje rekordu pominięcia (zapisanego w BeforeDiscovery),
        # niezależnie od tego, czy któryś It tego bloku zostanie kiedyś dodany bez -Skip.
        if ($IsSkipped) { return }
        try {
            foreach ($Sid in (Get-DsoUserSidsFromCase -Case $Case)) {
                $Loaded = [bool](Invoke-Command -Session $Session -ScriptBlock { param($Sid) Test-Path -LiteralPath "Registry::HKEY_USERS\$Sid" } -ArgumentList $Sid)
                if (-not $Loaded) {
                    $script:HiveProblem = "Hive HKU\$Sid nie jest załadowany w VM - użytkownik 'dso' nie jest zalogowany, więc asercje HKU byłyby puste."
                }
            }
            if (-not $script:HiveProblem) {
                $CaseArgs = @{ Session = $Session; Case = $Case; Layout = $Layout }
                if ($Case.Flow -eq 'Reboot') { $CaseArgs.Reconnect = $Reconnect }
                $script:Record = Invoke-VmCase @CaseArgs
                $SafeName = $Case.Name -replace '[^A-Za-z0-9._-]', '_'
                $Json = $script:Record | ConvertTo-Json -Depth 20
                [System.IO.File]::WriteAllText((Join-Path $RecordDirectory "$SafeName.json"), $Json, (New-Object System.Text.UTF8Encoding($true)))
            }
        }
        catch {
            $script:RunProblem = $_.Exception.Message
        }
    }

    It 'przebieg przypadku i hive użytkownika (jeśli używany)' -Skip:$IsSkipped {
        $script:HiveProblem | Should -BeNullOrEmpty -Because "$script:HiveProblem"
        $script:RunProblem | Should -BeNullOrEmpty -Because "wyjątek przebiegu: $script:RunProblem"
        $script:Record | Should -Not -BeNullOrEmpty
    }

    if ($Expect.ContainsKey('ExecuteExit')) {
        It 'execute: kod wyjścia' -Skip:$IsSkipped {
            $Step = Get-DsoStepResult -StepName 'execute' -AllowNoReport
            (@($Expect.ExecuteExit) -contains $Step.ExitCode) | Should -BeTrue -Because "exit $($Step.ExitCode), dozwolone: $(@($Expect.ExecuteExit) -join ', '), ReportError '$($Step.ReportError)', timeout $($Step.TimedOut)"
        }
    }
    if ($Expect.ContainsKey('ExecuteOutcome')) {
        It 'execute: wynik' -Skip:$IsSkipped {
            $Step = Get-DsoStepResult -StepName 'execute'
            Assert-DsoValueIn -Actual $Step.Report.outcome -Allowed $Expect.ExecuteOutcome -What "wynik kroku 'execute' (exit $($Step.ExitCode))"
        }
    }
    if ($Expect.ContainsKey('RequiresReboot') -and $Case.Flow -eq 'Execute') {
        It 'execute: requiresReboot' -Skip:$IsSkipped {
            $Step = Get-DsoStepResult -StepName 'execute'
            $Actual = Get-DsoReportProperty -Step $Step -Name 'requiresReboot'
            ($Actual -is [bool] -and $Actual -eq [bool]$Expect.RequiresReboot) | Should -BeTrue -Because "krok 'execute': requiresReboot='$Actual', oczekiwano $($Expect.RequiresReboot)"
        }
    }
    if ($Case.ContainsKey('Check')) {
        It 'check: skrypt w VM zwrócił $true' -Skip:$IsSkipped {
            $Record = Get-DsoCaseRecord
            $Record.CheckResult | Should -Not -BeNullOrEmpty -Because 'brak wyniku Check w rekordzie - nic nie zostało sprawdzone'
            $Record.CheckResult | Should -BeExactly 'Passed' -Because "Check: $($Record.CheckResult)"
        }
    }
    if ($Case.ContainsKey('Cleanup')) {
        It 'cleanup: sprzątanie bez błędu' -Skip:$IsSkipped {
            $Record = Get-DsoCaseRecord
            $Record.CleanupError | Should -BeNullOrEmpty -Because "sprzątanie nie powiodło się: $($Record.CleanupError)"
        }
    }

    if ($Case.Flow -eq 'AppRemove') {
        It 'list-apps przed: obecność na liście, grupa i możliwość usunięcia' -Skip:$IsSkipped {
            $Step = Get-DsoStepResult -StepName 'list-before'
            $Step.ExitCode | Should -Be 0 -Because "krok 'list-before' (list-apps) zakończył się kodem $($Step.ExitCode)"
            $Rows = @(Get-DsoListedApp -Step $Step -AppId $Case.AppId)
            ($Rows.Count -eq 1) | Should -Be ([bool]$Expect.ListedBefore) -Because "krok 'list-before': '$($Case.AppId)' znaleziono $($Rows.Count) raz(y), oczekiwano ListedBefore=$($Expect.ListedBefore)"
            if ($Expect.ContainsKey('Group')) { Assert-DsoValueIn -Actual $Rows[0].group -Allowed $Expect.Group -What "grupa wiersza w kroku 'list-before'" }
            if ($Expect.ContainsKey('Removability')) { Assert-DsoValueIn -Actual $Rows[0].removability -Allowed $Expect.Removability -What "removability wiersza w kroku 'list-before'" }
        }
        It 'remove-app: kod wyjścia' -Skip:$IsSkipped {
            $Step = Get-DsoStepResult -StepName 'remove-app' -AllowNoReport
            (@($Expect.RemoveExit) -contains $Step.ExitCode) | Should -BeTrue -Because "krok 'remove-app': exit $($Step.ExitCode), dozwolone: $(@($Expect.RemoveExit) -join ', '), ReportError '$($Step.ReportError)', timeout $($Step.TimedOut)"
        }
        It 'remove-app: wynik' -Skip:$IsSkipped {
            $Step = Get-DsoStepResult -StepName 'remove-app'
            Assert-DsoValueIn -Actual $Step.Report.outcome -Allowed $Expect.RemoveOutcome -What "wynik kroku 'remove-app' (exit $($Step.ExitCode), komunikat '$($Step.Report.message)')"
        }
        if ($Expect.ContainsKey('RequiresReboot')) {
            It 'remove-app: requiresReboot' -Skip:$IsSkipped {
                $Step = Get-DsoStepResult -StepName 'remove-app'
                $Actual = Get-DsoReportProperty -Step $Step -Name 'requiresReboot'
                ($Actual -is [bool] -and $Actual -eq [bool]$Expect.RequiresReboot) | Should -BeTrue -Because "krok 'remove-app': requiresReboot='$Actual', oczekiwano $($Expect.RequiresReboot)"
            }
        }
        if ($Expect.ContainsKey('WarningContains')) {
            It 'remove-app: ostrzeżenie w raporcie' -Skip:$IsSkipped {
                $Step = Get-DsoStepResult -StepName 'remove-app'
                $Warnings = @(Get-DsoReportProperty -Step $Step -Name 'warnings')
                @($Warnings | Where-Object { [string]$_ -like "*$($Expect.WarningContains)*" }).Count | Should -BeGreaterThan 0 -Because "krok 'remove-app': brak ostrzeżenia '$($Expect.WarningContains)' (są: $($Warnings -join ' | '))"
            }
        }
        if ($Expect.ContainsKey('WarningAbsent')) {
            It 'remove-app: brak ostrzeżenia' -Skip:$IsSkipped {
                $Step = Get-DsoStepResult -StepName 'remove-app'
                $Warnings = @(Get-DsoReportProperty -Step $Step -Name 'warnings')
                @($Warnings | Where-Object { [string]$_ -like "*$($Expect.WarningAbsent)*" }).Count | Should -Be 0 -Because "krok 'remove-app': niepożądane ostrzeżenie '$($Expect.WarningAbsent)' (są: $($Warnings -join ' | '))"
            }
        }
        It 'list-apps po: obecność na liście' -Skip:$IsSkipped {
            $Step = Get-DsoStepResult -StepName 'list-after'
            $Step.ExitCode | Should -Be 0 -Because "krok 'list-after' (list-apps) zakończył się kodem $($Step.ExitCode)"
            $Rows = @(Get-DsoListedApp -Step $Step -AppId $Case.AppId)
            ($Rows.Count -eq 1) | Should -Be ([bool]$Expect.ListedAfter) -Because "krok 'list-after': '$($Case.AppId)' znaleziono $($Rows.Count) raz(y), oczekiwano ListedAfter=$($Expect.ListedAfter)"
        }
    }

    if ($Case.Flow -eq 'AppInstall') {
        It 'instalacja: kod wyjścia' -Skip:$IsSkipped {
            $Step = Get-DsoStepResult -StepName $Case.Command -AllowNoReport
            (@($Expect.Exit) -contains $Step.ExitCode) | Should -BeTrue -Because "krok '$($Case.Command)': exit $($Step.ExitCode), dozwolone: $(@($Expect.Exit) -join ', '), ReportError '$($Step.ReportError)', timeout $($Step.TimedOut)"
        }
        if ($Case.Command -eq 'install-apps') {
            It 'install-apps: każda pozycja z AppIds ma dokładnie jeden wynik' -Skip:$IsSkipped {
                $Step = Get-DsoStepResult -StepName 'install-apps'
                $Items = @(Get-DsoReportProperty -Step $Step -Name 'installItems' | Where-Object { $null -ne $_ })
                $Ids = @($Items | ForEach-Object { [string]$_.id })
                $Items.Count | Should -Be @($Case.AppIds).Count -Because "krok 'install-apps': $($Items.Count) wyników ($($Ids -join ', ')), a AppIds ma $(@($Case.AppIds).Count)"
                foreach ($Id in @($Case.AppIds)) {
                    @($Ids | Where-Object { $_ -ieq $Id }).Count | Should -Be 1 -Because "krok 'install-apps': brak wyniku pozycji '$Id' (są: $($Ids -join ', '))"
                }
            }
        }
        if ($Expect.ContainsKey('Outcomes')) {
            It 'install-apps: wynik pozycji' -Skip:$IsSkipped {
                $Step = Get-DsoStepResult -StepName 'install-apps'
                $Items = @(Get-DsoReportProperty -Step $Step -Name 'installItems' | Where-Object { $null -ne $_ })
                foreach ($Id in $Expect.Outcomes.Keys) {
                    $Found = @($Items | Where-Object { [string]$_.id -ieq $Id })
                    $Found.Count | Should -Be 1 -Because "krok 'install-apps': pozycja '$Id' znaleziona $($Found.Count) raz(y)"
                    Assert-DsoValueIn -Actual $Found[0].outcome -Allowed $Expect.Outcomes[$Id] -What "wynik pozycji '$Id' w kroku 'install-apps' (komunikat '$($Found[0].message)')"
                }
            }
        }
        if ($Expect.ContainsKey('WingetCodes')) {
            It 'install-apps: kod winget pozycji' -Skip:$IsSkipped {
                # M2: dozwolone wartości pola wingetCode; $null = pozycja bez kodu (winget dla niej nie startował, np. Skipped).
                $Step = Get-DsoStepResult -StepName 'install-apps'
                $Items = @(Get-DsoReportProperty -Step $Step -Name 'installItems' | Where-Object { $null -ne $_ })
                foreach ($Id in $Expect.WingetCodes.Keys) {
                    $Found = @($Items | Where-Object { [string]$_.id -ieq $Id })
                    $Found.Count | Should -Be 1 -Because "krok 'install-apps': pozycja '$Id' znaleziona $($Found.Count) raz(y)"
                    $Allowed = $Expect.WingetCodes[$Id]
                    if ($null -eq $Allowed) {
                        $Found[0].wingetCode | Should -BeNullOrEmpty -Because "krok 'install-apps': pozycja '$Id' nie powinna mieć kodu winget (jest '$($Found[0].wingetCode)', komunikat '$($Found[0].message)')"
                    }
                    else {
                        Assert-DsoValueIn -Actual $Found[0].wingetCode -Allowed $Allowed -What "kod winget pozycji '$Id' w kroku 'install-apps' (komunikat '$($Found[0].message)')"
                    }
                }
            }
        }
        if ($Expect.ContainsKey('Installed')) {
            It 'list-install: status „zainstalowana”' -Skip:$IsSkipped {
                $Step = Get-DsoStepResult -StepName 'list-install'
                $Rows = @(Get-DsoReportProperty -Step $Step -Name 'installRows' | Where-Object { $null -ne $_ })
                foreach ($Id in $Expect.Installed.Keys) {
                    $Found = @($Rows | Where-Object { [string]$_.id -ieq $Id })
                    $Found.Count | Should -Be 1 -Because "krok 'list-install': pozycja '$Id' znaleziona $($Found.Count) raz(y)"
                    $Actual = $Found[0].installed
                    ($Actual -is [bool] -and $Actual -eq [bool]$Expect.Installed[$Id]) | Should -BeTrue -Because "krok 'list-install': '$Id' installed='$Actual', oczekiwano $($Expect.Installed[$Id])"
                }
            }
        }
        if ($Expect.ContainsKey('WingetState')) {
            It 'instalacja: stan winget' -Skip:$IsSkipped {
                $Step = Get-DsoStepResult -StepName $Case.Command
                $Winget = Get-DsoReportProperty -Step $Step -Name 'winget'
                if ($null -eq $Winget) { throw "Raport kroku '$($Case.Command)' ma puste pole 'winget' - nic nie zostało sprawdzone." }
                Assert-DsoValueIn -Actual $Winget.state -Allowed $Expect.WingetState -What "stan winget w kroku '$($Case.Command)' (komunikat '$($Winget.message)')"
            }
        }
        if ($Expect.ContainsKey('MessageContains')) {
            It 'instalacja: komunikat' -Skip:$IsSkipped {
                # Komunikat raportu, stanu winget albo którejś pozycji (np. „brak połączenia” jest w wyniku pozycji, odmowa konta także w raporcie).
                $Step = Get-DsoStepResult -StepName $Case.Command
                $Texts = [System.Collections.Generic.List[string]]::new()
                $Texts.Add([string](Get-DsoReportProperty -Step $Step -Name 'message'))
                $Winget = $Step.Report.PSObject.Properties['winget']
                if ($null -ne $Winget -and $null -ne $Winget.Value) { $Texts.Add([string]$Winget.Value.message) }
                $Items = $Step.Report.PSObject.Properties['installItems']
                if ($null -ne $Items) { foreach ($Item in @($Items.Value | Where-Object { $null -ne $_ })) { $Texts.Add([string]$Item.message) } }
                @($Texts | Where-Object { $_ -like "*$($Expect.MessageContains)*" }).Count | Should -BeGreaterThan 0 -Because "krok '$($Case.Command)': brak komunikatu '$($Expect.MessageContains)' (są: $(@($Texts | Where-Object { $_ }) -join ' | '))"
            }
        }
    }

    if ($Expect.ContainsKey('Before')) {
        It 'detect przed zmianą' -Skip:$IsSkipped {
            $Step = Get-DsoStepResult -StepName 'detect-before'
            Assert-DsoValueIn -Actual $Step.Report.state -Allowed $Expect.Before -What "stan po kroku 'detect-before'"
        }
    }

    if ($Expect.ContainsKey('ApplyExit')) {
        It 'apply: kod wyjścia' -Skip:$IsSkipped {
            $Step = Get-DsoStepResult -StepName 'apply' -AllowNoReport
            (@($Expect.ApplyExit) -contains $Step.ExitCode) | Should -BeTrue -Because "exit $($Step.ExitCode), dozwolone: $(@($Expect.ApplyExit) -join ', '), ReportError '$($Step.ReportError)', timeout $($Step.TimedOut)"
        }
    }
    if ($Expect.ContainsKey('ApplyOutcome')) {
        It 'apply: wynik' -Skip:$IsSkipped {
            $Step = Get-DsoStepResult -StepName 'apply'
            Assert-DsoValueIn -Actual $Step.Report.outcome -Allowed $Expect.ApplyOutcome -What "wynik kroku 'apply' (exit $($Step.ExitCode))"
        }
        if ($Case.Flow -eq 'Reboot') {
            It 'apply: raport zgłasza requiresReboot=true (tweak wymaga restartu)' -Skip:$IsSkipped {
                $Step = Get-DsoStepResult -StepName 'apply'
                $Step.Report.requiresReboot | Should -BeTrue -Because "raport apply ma requiresReboot='$($Step.Report.requiresReboot)'; przypadek Reboot dotyczy tweaka z requiresReboot=true, więc silnik musi to zgłosić"
            }
        }
    }
    if ($Expect.ContainsKey('AfterApply')) {
        It 'detect po apply' -Skip:$IsSkipped {
            $Step = Get-DsoStepResult -StepName 'detect-after-apply'
            Assert-DsoValueIn -Actual $Step.Report.state -Allowed $Expect.AfterApply -What "stan po kroku 'detect-after-apply'"
        }
    }

    if ($Case.Flow -eq 'Reboot') {
        It 'restart po apply i po undo faktycznie nastąpił (późniejszy LastBootUpTime)' -Skip:$IsSkipped {
            $Record = Get-DsoCaseRecord
            $Reboots = @($Record.Reboots)
            $Reboots.Count | Should -Be 2 -Because 'brak dwóch potwierdzonych restartów w rekordzie - restart nie nastąpił albo nic nie zostało sprawdzone'
            @($Reboots.Step) | Should -Be @('reboot-after-apply', 'reboot-after-undo')
            foreach ($Reboot in $Reboots) {
                ([datetime]$Reboot.BootAfter) | Should -BeGreaterThan ([datetime]$Reboot.BootBefore) -Because "restart '$($Reboot.Step)' nie zmienił LastBootUpTime"
                $Reboot.MarkerGone | Should -BeTrue -Because "restart '$($Reboot.Step)': ulotny znacznik restartu nie zniknął albo nie został zweryfikowany (sam LastBootUpTime mógł się przesunąć przez zegar)"
            }
        }
    }
    if ($Expect.ContainsKey('AfterReboot')) {
        It 'detect po restarcie' -Skip:$IsSkipped {
            $Step = Get-DsoStepResult -StepName 'detect-after-reboot'
            Assert-DsoValueIn -Actual $Step.Report.state -Allowed $Expect.AfterReboot -What "stan po kroku 'detect-after-reboot'"
        }
    }

    if ($Case.ContainsKey('CheckAfterApply')) {
        It 'check po apply: skrypt w VM zwrócił $true' -Skip:$IsSkipped {
            $Record = Get-DsoCaseRecord
            $Record.CheckAfterApplyResult | Should -Not -BeNullOrEmpty -Because 'brak wyniku CheckAfterApply w rekordzie - nic nie zostało sprawdzone'
            ([string]$Record.CheckAfterApplyResult -ceq 'Passed') | Should -BeTrue -Because "CheckAfterApply: $($Record.CheckAfterApplyResult)"
        }
    }

    if ($Expect.ContainsKey('UndoExit')) {
        It 'undo: kod wyjścia' -Skip:$IsSkipped {
            $Step = Get-DsoStepResult -StepName 'undo' -AllowNoReport
            (@($Expect.UndoExit) -contains $Step.ExitCode) | Should -BeTrue -Because "exit $($Step.ExitCode), dozwolone: $(@($Expect.UndoExit) -join ', '), ReportError '$($Step.ReportError)', timeout $($Step.TimedOut)"
        }
    }
    if ($Expect.ContainsKey('UndoOutcome')) {
        It 'undo: wynik' -Skip:$IsSkipped {
            $Step = Get-DsoStepResult -StepName 'undo'
            Assert-DsoValueIn -Actual $Step.Report.outcome -Allowed $Expect.UndoOutcome -What "wynik kroku 'undo' (exit $($Step.ExitCode))"
        }
    }
    if ($Expect.ContainsKey('AfterUndo')) {
        It 'detect po undo' -Skip:$IsSkipped {
            $Step = Get-DsoStepResult -StepName 'detect-after-undo'
            Assert-DsoValueIn -Actual $Step.Report.state -Allowed $Expect.AfterUndo -What "stan po kroku 'detect-after-undo'"
        }
    }

    if ($Case.ContainsKey('CheckAfterUndo')) {
        It 'check po undo: skrypt w VM zwrócił $true' -Skip:$IsSkipped {
            $Record = Get-DsoCaseRecord
            $Record.CheckAfterUndoResult | Should -Not -BeNullOrEmpty -Because 'brak wyniku CheckAfterUndo w rekordzie - nic nie zostało sprawdzone'
            ([string]$Record.CheckAfterUndoResult -ceq 'Passed') | Should -BeTrue -Because "CheckAfterUndo: $($Record.CheckAfterUndoResult)"
        }
    }

    if ($Expect.ContainsKey('KeyPresentAfterUndo')) {
        It 'klucze istniejące przed apply przetrwały undo (undo ich nie skasowało)' -Skip:$IsSkipped {
            $Record = Get-DsoCaseRecord
            $Keys = @($Record.KeysPresentAfterUndo)
            $Keys.Count | Should -BeGreaterThan 0 -Because 'brak odczytu kluczy po undo - nic nie zostało sprawdzone'
            foreach ($Wanted in @($Expect.KeyPresentAfterUndo)) {
                $Found = @($Keys | Where-Object { $_.Key -ceq $Wanted })
                $Found.Count | Should -Be 1 -Because "brak odczytu klucza '$Wanted' po undo - nic nie zostało sprawdzone"
                $Found[0].Exists | Should -BeTrue -Because "klucz '$Wanted' (istniał przed apply) nie istnieje po undo - undo skasowało klucz sprzed apply"
            }
        }
    }

    if ($Expect.ContainsKey('KeyAbsentAfterUndo')) {
        It 'klucze utworzone przez apply nie istnieją po undo' -Skip:$IsSkipped {
            $Record = Get-DsoCaseRecord
            $Keys = @($Record.KeysAfterUndo)
            $Keys.Count | Should -BeGreaterThan 0 -Because 'brak odczytu kluczy po undo - nic nie zostało sprawdzone'
            foreach ($Wanted in @($Expect.KeyAbsentAfterUndo)) {
                $Found = @($Keys | Where-Object { $_.Key -ceq $Wanted })
                $Found.Count | Should -Be 1 -Because "brak odczytu klucza '$Wanted' po undo - nic nie zostało sprawdzone"
                $Found[0].Exists | Should -BeFalse -Because "klucz '$Wanted' nadal istnieje po undo (undo nie usunęło klucza utworzonego przez apply)"
            }
        }
    }

    if ($Expect.ContainsKey('AfterApplySnapshot')) {
        It 'stan po apply potwierdzony niezależnym odczytem' -Skip:$IsSkipped {
            $Record = Get-DsoCaseRecord
            $Problems = Test-VmSnapshotMatches -Snapshot $Record.SnapshotAfterApply -Expected $Expect.AfterApplySnapshot
            @($Problems).Count | Should -Be 0 -Because (@($Problems) -join '; ')
        }
    }

    # Bez niepustego Snapshot (żadnego wpisu rejestru ani usługi) nie ma czego porównywać - nie udajemy zielonego testu.
    $SnapshotEntries = 0
    if ($Case.ContainsKey('Snapshot')) {
        foreach ($Section in @('Registry', 'Services')) {
            if ($Case.Snapshot.ContainsKey($Section)) { $SnapshotEntries += @($Case.Snapshot[$Section]).Count }
        }
    }
    if ($SnapshotEntries -gt 0) {
        It 'stan końcowy = stan przed' -Skip:$IsSkipped {
            $Record = Get-DsoCaseRecord
            $Diff = $Record.SnapshotDiff
            @($Diff).Count | Should -Be 0 -Because (@($Diff) -join '; ')
        }
    }

    if ($Expect.ContainsKey('UserSid')) {
        It 'zmiana trafiła do HKU zalogowanego, nie do HKCU procesu' -Skip:$IsSkipped {
            $Record = Get-DsoCaseRecord
            $Step = Get-DsoStepResult -StepName 'apply'
            $Step.Report.user.sid | Should -BeExactly $Expect.UserSid
            if ($Expect.ContainsKey('UserIsCertain')) { $Step.Report.user.isCertain | Should -Be $Expect.UserIsCertain }
            $Registry = @($Record.NotWrittenTo.Registry)
            $Registry.Count | Should -BeGreaterThan 0 -Because 'brak odczytu NotWrittenTo - nic nie zostało sprawdzone'
            @($Record.NotWrittenToDiff).Count | Should -Be 0 -Because ("wpis w HKCU procesu (admina) zmienił się (powinna zmienić się tylko HKU użytkownika): " + (@($Record.NotWrittenToDiff) -join '; '))
        }
    }

    if ($Expect.ContainsKey('MessageContains') -and $Case.Flow -ne 'AppInstall') {
        It 'komunikat' -Skip:$IsSkipped {
            # Flow Execute: komunikat raportu kroku 'execute' (stderr akcji zakończonej Failed); AppRemove: krok 'remove-app'; pozostałe: 'apply'.
            $Step = Get-DsoStepResult -StepName (Get-DsoMainStepName -Flow $Case.Flow)
            $Step.Report.message | Should -BeLike "*$($Expect.MessageContains)*"
        }
    }

    if ($Expect.ContainsKey('Aborted')) {
        It 'seria przerwana' -Skip:$IsSkipped {
            $Step = Get-DsoStepResult -StepName (Get-DsoMainStepName -Flow $Case.Flow)
            $Step.Report.aborted | Should -Be $Expect.Aborted
        }
    }

    if ($Expect.ContainsKey('RestorePointStatus') -or $Expect.ContainsKey('RestorePointCreated')) {
        It 'restore point' -Skip:$IsSkipped {
            $Record = Get-DsoCaseRecord
            $Step = Get-DsoStepResult -StepName (Get-DsoMainStepName -Flow $Case.Flow)
            if ($Expect.ContainsKey('RestorePointStatus')) {
                $Step.Report.restorePoint | Should -Not -BeNullOrEmpty -Because "raport kroku '$($Step.Step)' bez sekcji restorePoint"
                Assert-DsoValueIn -Actual $Step.Report.restorePoint.status -Allowed $Expect.RestorePointStatus -What 'restorePoint.status'
            }
            if ($Expect.ContainsKey('RestorePointCreated')) {
                $Record.RestorePointCountBefore | Should -Not -BeNullOrEmpty -Because 'brak licznika punktów przed apply'
                $Delta = if ($Expect.RestorePointCreated) { 1 } else { 0 }
                $Record.RestorePointCountAfter | Should -Be ($Record.RestorePointCountBefore + $Delta)
            }
        }
    }
}
