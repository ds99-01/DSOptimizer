# Przypadki testów VM (Etap 1b + Etap 2a: Execute/DetectOnly/Skip/Catalog). Format: Import-VmCases w tests/vm/modules/VmRun.psm1.
# {UserSid} = SID konta interaktywnego 'dso' (podstawia runner). Kolejność ma znaczenie (RP-disabled na końcu).
@{
    Cases = @(
        @{
            Name = 'hklm-value'; TweakId = 'vmtest.hklm-value'; Flow = 'Full'; RestorePoint = 'skip'
            Setup = @("New-Item -Path 'Registry::HKEY_LOCAL_MACHINE\SOFTWARE\DSOptimizer.VmTests' -Force | Out-Null")
            Snapshot = @{ Registry = @(@{ Path = 'HKLM\SOFTWARE\DSOptimizer.VmTests'; Name = 'HklmDword' }, @{ Path = 'HKLM\SOFTWARE\DSOptimizer.VmTests'; Name = 'HklmString' }); Services = @() }
            Expect = @{
                Before = 'NotApplied'; ApplyExit = 0; ApplyOutcome = 'Applied'; AfterApply = 'Applied'
                UndoExit = 0; UndoOutcome = 'Undone'; AfterUndo = 'NotApplied'
                AfterApplySnapshot = @{ Registry = @(
                    @{ Path = 'HKLM\SOFTWARE\DSOptimizer.VmTests'; Name = 'HklmDword'; Exists = $true; Kind = 'DWord'; Data = '1' }
                    @{ Path = 'HKLM\SOFTWARE\DSOptimizer.VmTests'; Name = 'HklmString'; Exists = $true; Kind = 'String'; Data = 'zażółć' }) }
            }
        }
        @{
            Name = 'hkcu-value'; TweakId = 'vmtest.hkcu-value'; Flow = 'Full'; RestorePoint = 'skip'
            Setup = @("New-Item -Path 'Registry::HKEY_USERS\{UserSid}\Software\DSOptimizer.VmTests' -Force | Out-Null")
            Snapshot = @{ Registry = @(@{ Path = 'HKU\{UserSid}\Software\DSOptimizer.VmTests'; Name = 'UserString' }); Services = @() }
            Expect = @{
                Before = 'NotApplied'; ApplyExit = 0; ApplyOutcome = 'Applied'; AfterApply = 'Applied'
                UndoExit = 0; UndoOutcome = 'Undone'; AfterUndo = 'NotApplied'
                UserSid = '{UserSid}'; UserIsCertain = $true
                NotWrittenTo = @(@{ Path = 'HKCU\Software\DSOptimizer.VmTests'; Name = 'UserString' })
                AfterApplySnapshot = @{ Registry = @(@{ Path = 'HKU\{UserSid}\Software\DSOptimizer.VmTests'; Name = 'UserString'; Exists = $true; Kind = 'String'; Data = 'vm-1b' }) }
            }
        }
        @{
            Name = 'service-manual-to-disabled'; TweakId = 'vmtest.service-manual-to-disabled'; Flow = 'Full'; RestorePoint = 'skip'
            Setup = @(); Snapshot = @{ Registry = @(); Services = @('RetailDemo') }
            Expect = @{
                Before = 'NotApplied'; ApplyExit = 0; ApplyOutcome = 'Applied'; AfterApply = 'Applied'
                UndoExit = 0; UndoOutcome = 'Undone'; AfterUndo = 'NotApplied'
                AfterApplySnapshot = @{ Services = @(@{ Name = 'RetailDemo'; StartMode = 'Disabled' }) }
            }
            Hypothesis = 'RetailDemo: Manual i zatrzymana na czystej 26H2 (potwierdzone w spike, Task 13).'
        }
        @{
            Name = 'service-delayed-stop'; TweakId = 'vmtest.service-delayed-stop'; Flow = 'Full'; RestorePoint = 'skip'
            Setup = @("Start-Service -Name WSearch; (Get-Service -Name WSearch).WaitForStatus('Running', [TimeSpan]::FromSeconds(60))")
            Snapshot = @{ Registry = @(); Services = @('WSearch') }
            Expect = @{
                Before = 'NotApplied'; ApplyExit = 0; ApplyOutcome = 'Applied'; AfterApply = 'Applied'
                UndoExit = 0; UndoOutcome = 'Undone'; AfterUndo = 'NotApplied'
                AfterApplySnapshot = @{ Services = @(@{ Name = 'WSearch'; StartMode = 'Disabled'; State = 'Stopped' }) }
            }
        }
        @{
            Name = 'service-dependents-running'; TweakId = 'vmtest.service-dependents-running'; Flow = 'ApplyOnly'; RestorePoint = 'skip'
            Setup = @("Start-Service -Name Audiosrv; (Get-Service -Name Audiosrv).WaitForStatus('Running', [TimeSpan]::FromSeconds(60))")
            Snapshot = @{ Registry = @(); Services = @('AudioEndpointBuilder', 'Audiosrv') }
            Expect = @{
                Before = 'NotApplied'; ApplyExit = 1; ApplyOutcome = 'FailedRolledBack'; AfterApply = 'NotApplied'
                MessageContains = 'zależne'
                AfterApplySnapshot = @{ Services = @(@{ Name = 'AudioEndpointBuilder'; StartMode = 'Auto'; State = 'Running' }, @{ Name = 'Audiosrv'; State = 'Running' }) }
            }
        }
        @{
            Name = 'driver-start-type'; TweakId = 'vmtest.driver-start-type'; Flow = 'Full'; RestorePoint = 'skip'
            Setup = @(); Snapshot = @{ Registry = @(); Services = @('Serenum') }
            Expect = @{
                Before = 'NotApplied'; ApplyExit = @(0, 1); ApplyOutcome = @('Applied', 'FailedRolledBack'); AfterApply = @('Applied', 'NotApplied')
                UndoExit = @(0, 1); UndoOutcome = @('Undone', 'Refused', 'UndoneFromDefaults'); AfterUndo = 'NotApplied'
            }
            Hypothesis = 'Nieznane, czy ChangeServiceConfig2(DELAYED_AUTO_START) działa dla sterowników. Twarda asercja: stan po teście = stan przed (snapshot). Wynik zapisać w mapie drogowej autora.'
        }
        @{
            Name = 'reboot-hklm'; TweakId = 'vmtest.reboot-hklm'; Flow = 'Reboot'; RestorePoint = 'skip'
            Setup = @("New-Item -Path 'Registry::HKEY_LOCAL_MACHINE\SOFTWARE\DSOptimizer.VmTests' -Force | Out-Null")
            Snapshot = @{ Registry = @(@{ Path = 'HKLM\SOFTWARE\DSOptimizer.VmTests\Reboot\Nowy'; Name = 'RebootDword' }); Services = @() }
            Expect = @{
                Before = 'NotApplied'; ApplyExit = 0; ApplyOutcome = @('Applied', 'PendingReboot'); AfterApply = @('Applied', 'NotApplied', 'Partial')
                AfterReboot = 'Applied'
                UndoExit = 0; UndoOutcome = 'Undone'; AfterUndo = 'NotApplied'
                KeyAbsentAfterUndo = @('HKLM\SOFTWARE\DSOptimizer.VmTests\Reboot')
                KeyPresentAfterUndo = @('HKLM\SOFTWARE\DSOptimizer.VmTests')
            }
            Hypothesis = 'Klucz Reboot\Nowy nie istnieje przed apply; po undo silnik usuwa utworzone klucze (2+ segmenty pod hive), więc Reboot też znika, a klucz DSOptimizer.VmTests (istniał przed apply, Setup) przetrwa. Stan przed = po.'
        }
        @{
            Name = 'restore-point-enabled'; TweakId = 'vmtest.hklm-value'; Flow = 'Full'; RestorePoint = 'required'
            Setup = @("Enable-ComputerRestore -Drive 'C:\'", "New-Item -Path 'Registry::HKEY_LOCAL_MACHINE\SOFTWARE\DSOptimizer.VmTests' -Force | Out-Null")
            Snapshot = @{ Registry = @(@{ Path = 'HKLM\SOFTWARE\DSOptimizer.VmTests'; Name = 'HklmDword' }, @{ Path = 'HKLM\SOFTWARE\Microsoft\Windows NT\CurrentVersion\SystemRestore'; Name = 'SystemRestorePointCreationFrequency' }); Services = @() }
            Expect = @{
                Before = 'NotApplied'; ApplyExit = 0; ApplyOutcome = 'Applied'; AfterApply = 'Applied'
                UndoExit = 0; UndoOutcome = 'Undone'; AfterUndo = 'NotApplied'
                RestorePointStatus = 'Created'; RestorePointCreated = $true
            }
        }
        @{
            Name = 'action-marker'; TweakId = 'vmtest.action-marker'; Flow = 'Execute'; RestorePoint = 'skip'
            Check = "[System.IO.File]::ReadAllText('C:\dso\out\action-marker.txt', [System.Text.Encoding]::UTF8) -ceq 'zażółć'"
            Cleanup = "Remove-Item -LiteralPath 'C:\dso\out\action-marker.txt' -Force -ErrorAction SilentlyContinue"
            Expect = @{ ExecuteExit = 0; ExecuteOutcome = 'Executed' }
            Hypothesis = 'Akcja (--execute) zapisuje plik C:\dso\out\action-marker.txt (Layout.Out) z treścią zażółć; runner sprawdza treść i sprząta.'
        }
        @{
            Name = 'ux-menu-delay'; TweakId = 'ux.menu-delay'; Flow = 'Full'; Catalog = 'BuiltIn'; RestorePoint = 'skip'
            Setup = @("Set-ItemProperty -LiteralPath 'Registry::HKEY_USERS\{UserSid}\Control Panel\Desktop' -Name 'MenuShowDelay' -Value '250' -Type String")
            Snapshot = @{ Registry = @(@{ Path = 'HKU\{UserSid}\Control Panel\Desktop'; Name = 'MenuShowDelay' }); Services = @() }
            Expect = @{
                Before = 'NotApplied'; ApplyExit = 0; ApplyOutcome = 'Applied'; AfterApply = 'Applied'
                UndoExit = 0; UndoOutcome = 'Undone'; AfterUndo = 'NotApplied'
                UserSid = '{UserSid}'; UserIsCertain = $true
                NotWrittenTo = @(@{ Path = 'HKCU\Control Panel\Desktop'; Name = 'MenuShowDelay' })
                AfterApplySnapshot = @{ Registry = @(@{ Path = 'HKU\{UserSid}\Control Panel\Desktop'; Name = 'MenuShowDelay'; Exists = $true; Kind = 'String'; Data = '10' }) }
            }
            Hypothesis = 'Odczyt VM 26H2 (2026-10-18): domyślnie MenuShowDelay = REG_SZ "400" u użytkownika dso. Setup ustawia "250" (różne od original "400" z katalogu), więc stan końcowy = stan przed (SnapshotDiff) dowodzi undo ze snapshotu, a nie z fallbacku original. NotWrittenTo = HKCU Administratora bez zmian.'
        }
        @{
            Name = 'ux-game-dvr-policy'; TweakId = 'ux.game-dvr-policy'; Flow = 'Full'; Catalog = 'BuiltIn'; RestorePoint = 'skip'
            Setup = @(
                "if (-not (Test-Path -LiteralPath 'Registry::HKEY_LOCAL_MACHINE\SOFTWARE\Policies\Microsoft\Windows')) { New-Item -Path 'Registry::HKEY_LOCAL_MACHINE\SOFTWARE\Policies\Microsoft\Windows' | Out-Null }"
                "if (Test-Path -LiteralPath 'Registry::HKEY_LOCAL_MACHINE\SOFTWARE\Policies\Microsoft\Windows\GameDVR') { Remove-Item -LiteralPath 'Registry::HKEY_LOCAL_MACHINE\SOFTWARE\Policies\Microsoft\Windows\GameDVR' -Recurse }"
            )
            Snapshot = @{ Registry = @(@{ Path = 'HKLM\SOFTWARE\Policies\Microsoft\Windows\GameDVR'; Name = 'AllowGameDVR' }); Services = @() }
            Expect = @{
                Before = 'NotApplied'; ApplyExit = 0; ApplyOutcome = 'Applied'; AfterApply = 'Applied'
                UndoExit = 0; UndoOutcome = 'Undone'; AfterUndo = 'NotApplied'
                AfterApplySnapshot = @{ Registry = @(@{ Path = 'HKLM\SOFTWARE\Policies\Microsoft\Windows\GameDVR'; Name = 'AllowGameDVR'; Exists = $true; Kind = 'DWord'; Data = '0' }) }
                KeyAbsentAfterUndo = @('HKLM\SOFTWARE\Policies\Microsoft\Windows\GameDVR')
                KeyPresentAfterUndo = @('HKLM\SOFTWARE\Policies\Microsoft\Windows')
            }
            Hypothesis = 'Odczyt VM 26H2 (2026-10-18): klucz GameDVR i AllowGameDVR nie istnieją. Setup tworzy tylko brakujący klucz Windows (bez -Force, żeby nie skasować istniejących polityk) i zapewnia brak GameDVR. Undo usuwa wartość i pusty klucz GameDVR utworzony przez apply (A1), klucz Windows zostaje. Egzekwowanie polityki nie jest sprawdzane.'
        }
        @{
            Name = 'ux-game-dvr-policy-prev-value'; TweakId = 'ux.game-dvr-policy'; Flow = 'Full'; Catalog = 'BuiltIn'; RestorePoint = 'skip'
            Setup = @(
                "if (-not (Test-Path -LiteralPath 'Registry::HKEY_LOCAL_MACHINE\SOFTWARE\Policies\Microsoft\Windows')) { New-Item -Path 'Registry::HKEY_LOCAL_MACHINE\SOFTWARE\Policies\Microsoft\Windows' | Out-Null }"
                "if (-not (Test-Path -LiteralPath 'Registry::HKEY_LOCAL_MACHINE\SOFTWARE\Policies\Microsoft\Windows\GameDVR')) { New-Item -Path 'Registry::HKEY_LOCAL_MACHINE\SOFTWARE\Policies\Microsoft\Windows\GameDVR' | Out-Null }"
                "Set-ItemProperty -LiteralPath 'Registry::HKEY_LOCAL_MACHINE\SOFTWARE\Policies\Microsoft\Windows\GameDVR' -Name 'AllowGameDVR' -Value 1 -Type DWord"
            )
            Snapshot = @{ Registry = @(@{ Path = 'HKLM\SOFTWARE\Policies\Microsoft\Windows\GameDVR'; Name = 'AllowGameDVR' }); Services = @() }
            Expect = @{
                Before = 'NotApplied'; ApplyExit = 0; ApplyOutcome = 'Applied'; AfterApply = 'Applied'
                UndoExit = 0; UndoOutcome = 'Undone'; AfterUndo = 'NotApplied'
                AfterApplySnapshot = @{ Registry = @(@{ Path = 'HKLM\SOFTWARE\Policies\Microsoft\Windows\GameDVR'; Name = 'AllowGameDVR'; Exists = $true; Kind = 'DWord'; Data = '0' }) }
                KeyPresentAfterUndo = @('HKLM\SOFTWARE\Policies\Microsoft\Windows\GameDVR', 'HKLM\SOFTWARE\Policies\Microsoft\Windows')
            }
            Hypothesis = 'Wartość sprzed apply AllowGameDVR = 1 (różna od original <RemoveEntry>): undo ma przywrócić 1 ze snapshotu (SnapshotDiff pusty), a nie usunąć wartość, i zostawić klucz GameDVR, który istniał przed apply. Uruchamiany po przypadku bez wartości; stan GameDVR zostaje w VM do przywrócenia checkpointu (Cleanup działa tylko we Flow Execute).'
        }
        @{
            Name = 'privacy-widgets'; TweakId = 'privacy.widgets'; Flow = 'Full'; Catalog = 'BuiltIn'; RestorePoint = 'skip'
            Setup = @(
                "if (Test-Path -LiteralPath 'Registry::HKEY_LOCAL_MACHINE\SOFTWARE\Policies\Microsoft\Dsh') { Remove-Item -LiteralPath 'Registry::HKEY_LOCAL_MACHINE\SOFTWARE\Policies\Microsoft\Dsh' -Recurse }"
            )
            Snapshot = @{ Registry = @(@{ Path = 'HKLM\SOFTWARE\Policies\Microsoft\Dsh'; Name = 'AllowNewsAndInterests' }); Services = @() }
            Expect = @{
                Before = 'NotApplied'; ApplyExit = 0; ApplyOutcome = 'Applied'; AfterApply = 'Applied'
                UndoExit = 0; UndoOutcome = 'Undone'; AfterUndo = 'NotApplied'
                AfterApplySnapshot = @{ Registry = @(@{ Path = 'HKLM\SOFTWARE\Policies\Microsoft\Dsh'; Name = 'AllowNewsAndInterests'; Exists = $true; Kind = 'DWord'; Data = '0' }) }
                KeyAbsentAfterUndo = @('HKLM\SOFTWARE\Policies\Microsoft\Dsh')
            }
            Hypothesis = 'Odczyt VM 26H2 (2026-10-21): klucz Dsh i AllowNewsAndInterests nie istnieją na świeżej instalacji (Home i Pro). Setup zapewnia brak klucza Dsh. Undo ma usunąć wartość i klucz Dsh utworzony przez apply (Review Focus 4). Skutek (Win+W, przycisk) obserwowano w eksploracji, ten przypadek sprawdza zapis, detekcję i czysty undo.'
        }
        @{
            Name = 'privacy-search-web-results'; TweakId = 'privacy.search-web-results'; Flow = 'Full'; Catalog = 'BuiltIn'; RestorePoint = 'skip'
            Setup = @("Remove-ItemProperty -LiteralPath 'Registry::HKEY_USERS\{UserSid}\Software\Microsoft\Windows\CurrentVersion\SearchSettings' -Name 'IsGlobalWebSearchProviderToggleEnabled' -ErrorAction SilentlyContinue")
            Snapshot = @{ Registry = @(@{ Path = 'HKU\{UserSid}\Software\Microsoft\Windows\CurrentVersion\SearchSettings'; Name = 'IsGlobalWebSearchProviderToggleEnabled' }); Services = @() }
            Expect = @{
                Before = 'NotApplied'; ApplyExit = 0; ApplyOutcome = 'Applied'; AfterApply = 'Applied'
                UndoExit = 0; UndoOutcome = 'Undone'; AfterUndo = 'NotApplied'
                UserSid = '{UserSid}'; UserIsCertain = $true
                NotWrittenTo = @(@{ Path = 'HKCU\Software\Microsoft\Windows\CurrentVersion\SearchSettings'; Name = 'IsGlobalWebSearchProviderToggleEnabled' })
                AfterApplySnapshot = @{ Registry = @(@{ Path = 'HKU\{UserSid}\Software\Microsoft\Windows\CurrentVersion\SearchSettings'; Name = 'IsGlobalWebSearchProviderToggleEnabled'; Exists = $true; Kind = 'DWord'; Data = '0' }) }
                KeyPresentAfterUndo = @('HKU\{UserSid}\Software\Microsoft\Windows\CurrentVersion\SearchSettings')
            }
            Hypothesis = 'Odczyt VM 26H2 (2026-10-21): klucz SearchSettings istnieje, IsGlobalWebSearchProviderToggleEnabled brak (przełącznik włączony). Undo przywraca brak wartości ze snapshotu, a klucz SearchSettings (istniał przed apply) zostaje. NotWrittenTo = HKCU Administratora bez zmian. Skutek (wyniki Bing po restarcie Eksploratora) obserwowano w eksploracji.'
        }
        @{
            Name = 'privacy-settings-suggestions'; TweakId = 'privacy.settings-suggestions'; Flow = 'Full'; Catalog = 'BuiltIn'; RestorePoint = 'skip'
            Setup = @(
                "Remove-ItemProperty -LiteralPath 'Registry::HKEY_USERS\{UserSid}\Software\Microsoft\Windows\CurrentVersion\ContentDeliveryManager' -Name 'SubscribedContent-338393Enabled' -ErrorAction SilentlyContinue",
                "Remove-ItemProperty -LiteralPath 'Registry::HKEY_USERS\{UserSid}\Software\Microsoft\Windows\CurrentVersion\ContentDeliveryManager' -Name 'SubscribedContent-353694Enabled' -ErrorAction SilentlyContinue",
                "Remove-ItemProperty -LiteralPath 'Registry::HKEY_USERS\{UserSid}\Software\Microsoft\Windows\CurrentVersion\ContentDeliveryManager' -Name 'SubscribedContent-353696Enabled' -ErrorAction SilentlyContinue"
            )
            Snapshot = @{ Registry = @(
                @{ Path = 'HKU\{UserSid}\Software\Microsoft\Windows\CurrentVersion\ContentDeliveryManager'; Name = 'SubscribedContent-338393Enabled' }
                @{ Path = 'HKU\{UserSid}\Software\Microsoft\Windows\CurrentVersion\ContentDeliveryManager'; Name = 'SubscribedContent-353694Enabled' }
                @{ Path = 'HKU\{UserSid}\Software\Microsoft\Windows\CurrentVersion\ContentDeliveryManager'; Name = 'SubscribedContent-353696Enabled' }
            ); Services = @() }
            Expect = @{
                Before = 'NotApplied'; ApplyExit = 0; ApplyOutcome = 'Applied'; AfterApply = 'Applied'
                UndoExit = 0; UndoOutcome = 'Undone'; AfterUndo = 'NotApplied'
                UserSid = '{UserSid}'; UserIsCertain = $true
                NotWrittenTo = @(
                    @{ Path = 'HKCU\Software\Microsoft\Windows\CurrentVersion\ContentDeliveryManager'; Name = 'SubscribedContent-338393Enabled' }
                    @{ Path = 'HKCU\Software\Microsoft\Windows\CurrentVersion\ContentDeliveryManager'; Name = 'SubscribedContent-353694Enabled' }
                    @{ Path = 'HKCU\Software\Microsoft\Windows\CurrentVersion\ContentDeliveryManager'; Name = 'SubscribedContent-353696Enabled' }
                )
                AfterApplySnapshot = @{ Registry = @(
                    @{ Path = 'HKU\{UserSid}\Software\Microsoft\Windows\CurrentVersion\ContentDeliveryManager'; Name = 'SubscribedContent-338393Enabled'; Exists = $true; Kind = 'DWord'; Data = '0' }
                    @{ Path = 'HKU\{UserSid}\Software\Microsoft\Windows\CurrentVersion\ContentDeliveryManager'; Name = 'SubscribedContent-353694Enabled'; Exists = $true; Kind = 'DWord'; Data = '0' }
                    @{ Path = 'HKU\{UserSid}\Software\Microsoft\Windows\CurrentVersion\ContentDeliveryManager'; Name = 'SubscribedContent-353696Enabled'; Exists = $true; Kind = 'DWord'; Data = '0' }
                ) }
                KeyPresentAfterUndo = @('HKU\{UserSid}\Software\Microsoft\Windows\CurrentVersion\ContentDeliveryManager')
            }
            Hypothesis = 'Odczyt VM 26H2 (2026-10-21): klucz ContentDeliveryManager istnieje, trzy wartości SubscribedContent-338393/353694/353696Enabled brak (przełącznik włączony). Undo przywraca brak wartości ze snapshotu, klucz zostaje. Dowód skutku: stan przełącznika w Ustawieniach (eksploracja), treści nie obserwowano.'
        }
        @{
            Name = 'privacy-windows-tips'; TweakId = 'privacy.windows-tips'; Flow = 'Full'; Catalog = 'BuiltIn'; RestorePoint = 'skip'
            Setup = @("Set-ItemProperty -LiteralPath 'Registry::HKEY_USERS\{UserSid}\Software\Microsoft\Windows\CurrentVersion\ContentDeliveryManager' -Name 'SubscribedContent-338389Enabled' -Value 1 -Type DWord")
            Snapshot = @{ Registry = @(@{ Path = 'HKU\{UserSid}\Software\Microsoft\Windows\CurrentVersion\ContentDeliveryManager'; Name = 'SubscribedContent-338389Enabled' }); Services = @() }
            Expect = @{
                Before = 'NotApplied'; ApplyExit = 0; ApplyOutcome = 'Applied'; AfterApply = 'Applied'
                UndoExit = 0; UndoOutcome = 'Undone'; AfterUndo = 'NotApplied'
                UserSid = '{UserSid}'; UserIsCertain = $true
                NotWrittenTo = @(@{ Path = 'HKCU\Software\Microsoft\Windows\CurrentVersion\ContentDeliveryManager'; Name = 'SubscribedContent-338389Enabled' })
                AfterApplySnapshot = @{ Registry = @(@{ Path = 'HKU\{UserSid}\Software\Microsoft\Windows\CurrentVersion\ContentDeliveryManager'; Name = 'SubscribedContent-338389Enabled'; Exists = $true; Kind = 'DWord'; Data = '0' }) }
                KeyPresentAfterUndo = @('HKU\{UserSid}\Software\Microsoft\Windows\CurrentVersion\ContentDeliveryManager')
            }
            Hypothesis = 'Odczyt VM 26H2 (2026-10-21): SubscribedContent-338389Enabled = 1 (pole porad zaznaczone). Setup ustawia 1; undo przywraca 1 ze snapshotu, klucz ContentDeliveryManager zostaje. Dowód skutku: stan pola w Ustawieniach, treści nie obserwowano.'
        }
        @{
            Name = 'privacy-settings-account-notifications'; TweakId = 'privacy.settings-account-notifications'; Flow = 'Full'; Catalog = 'BuiltIn'; RestorePoint = 'skip'
            Setup = @("Remove-ItemProperty -LiteralPath 'Registry::HKEY_USERS\{UserSid}\Software\Microsoft\Windows\CurrentVersion\SystemSettings\AccountNotifications' -Name 'EnableAccountNotifications' -ErrorAction SilentlyContinue")
            Snapshot = @{ Registry = @(@{ Path = 'HKU\{UserSid}\Software\Microsoft\Windows\CurrentVersion\SystemSettings\AccountNotifications'; Name = 'EnableAccountNotifications' }); Services = @() }
            Expect = @{
                Before = 'NotApplied'; ApplyExit = 0; ApplyOutcome = 'Applied'; AfterApply = 'Applied'
                UndoExit = 0; UndoOutcome = 'Undone'; AfterUndo = 'NotApplied'
                UserSid = '{UserSid}'; UserIsCertain = $true
                NotWrittenTo = @(@{ Path = 'HKCU\Software\Microsoft\Windows\CurrentVersion\SystemSettings\AccountNotifications'; Name = 'EnableAccountNotifications' })
                AfterApplySnapshot = @{ Registry = @(@{ Path = 'HKU\{UserSid}\Software\Microsoft\Windows\CurrentVersion\SystemSettings\AccountNotifications'; Name = 'EnableAccountNotifications'; Exists = $true; Kind = 'DWord'; Data = '0' }) }
                KeyPresentAfterUndo = @('HKU\{UserSid}\Software\Microsoft\Windows\CurrentVersion\SystemSettings\AccountNotifications')
            }
            Hypothesis = 'Odczyt VM 26H2 (2026-10-21): klucz AccountNotifications istnieje, EnableAccountNotifications brak (przełącznik włączony). Undo przywraca brak wartości, klucz zostaje. Dowód skutku: stan przełącznika w Ustawieniach, treści nie obserwowano.'
        }
        @{
            Name = 'privacy-start-recommendations'; TweakId = 'privacy.start-recommendations'; Flow = 'Full'; Catalog = 'BuiltIn'; RestorePoint = 'skip'
            Setup = @("Remove-ItemProperty -LiteralPath 'Registry::HKEY_USERS\{UserSid}\Software\Microsoft\Windows\CurrentVersion\Explorer\Advanced' -Name 'Start_IrisRecommendations' -ErrorAction SilentlyContinue")
            Snapshot = @{ Registry = @(@{ Path = 'HKU\{UserSid}\Software\Microsoft\Windows\CurrentVersion\Explorer\Advanced'; Name = 'Start_IrisRecommendations' }); Services = @() }
            Expect = @{
                Before = 'NotApplied'; ApplyExit = 0; ApplyOutcome = 'Applied'; AfterApply = 'Applied'
                UndoExit = 0; UndoOutcome = 'Undone'; AfterUndo = 'NotApplied'
                UserSid = '{UserSid}'; UserIsCertain = $true
                NotWrittenTo = @(@{ Path = 'HKCU\Software\Microsoft\Windows\CurrentVersion\Explorer\Advanced'; Name = 'Start_IrisRecommendations' })
                AfterApplySnapshot = @{ Registry = @(@{ Path = 'HKU\{UserSid}\Software\Microsoft\Windows\CurrentVersion\Explorer\Advanced'; Name = 'Start_IrisRecommendations'; Exists = $true; Kind = 'DWord'; Data = '0' }) }
                KeyPresentAfterUndo = @('HKU\{UserSid}\Software\Microsoft\Windows\CurrentVersion\Explorer\Advanced')
            }
            Hypothesis = 'Odczyt VM 26H2 (2026-10-21): klucz Explorer\Advanced istnieje, Start_IrisRecommendations brak (przełącznik włączony). Undo przywraca brak wartości, klucz zostaje. Dowód skutku: stan przełącznika w Ustawieniach, w Start nie było takich treści. Restartu Eksploratora pozycja nie wykonuje (R15).'
        }
        @{
            Name = 'privacy-start-recommended-section'; TweakId = 'privacy.start-recommended-section'; Flow = 'Full'; Catalog = 'BuiltIn'; RestorePoint = 'skip'
            Setup = @(
                "if (Test-Path -LiteralPath 'Registry::HKEY_USERS\{UserSid}\Software\Policies\Microsoft\Windows\Explorer') { Remove-Item -LiteralPath 'Registry::HKEY_USERS\{UserSid}\Software\Policies\Microsoft\Windows\Explorer' -Recurse }"
            )
            Snapshot = @{ Registry = @(@{ Path = 'HKU\{UserSid}\Software\Policies\Microsoft\Windows\Explorer'; Name = 'HideRecommendedSection' }); Services = @() }
            Expect = @{
                Before = 'NotApplied'; ApplyExit = 0; ApplyOutcome = 'Applied'; AfterApply = 'Applied'
                UndoExit = 0; UndoOutcome = 'Undone'; AfterUndo = 'NotApplied'
                UserSid = '{UserSid}'; UserIsCertain = $true
                NotWrittenTo = @(@{ Path = 'HKCU\Software\Policies\Microsoft\Windows\Explorer'; Name = 'HideRecommendedSection' })
                AfterApplySnapshot = @{ Registry = @(@{ Path = 'HKU\{UserSid}\Software\Policies\Microsoft\Windows\Explorer'; Name = 'HideRecommendedSection'; Exists = $true; Kind = 'DWord'; Data = '1' }) }
                KeyAbsentAfterUndo = @('HKU\{UserSid}\Software\Policies\Microsoft\Windows\Explorer')
            }
            Hypothesis = 'Odczyt VM 26H2 (2026-10-21): wartość HideRecommendedSection i klucz Policies\Microsoft\Windows\Explorer użytkownika nie istnieją na świeżej instalacji (Home i Pro). Setup zapewnia brak klucza. Undo ma usunąć wartość i klucz utworzony przez apply (Review Focus 4). Skutek (sekcja Proponowane znika po restarcie Eksploratora) obserwowano w eksploracji. Restartu Eksploratora pozycja nie wykonuje (R15).'
        }
        @{
            Name = 'privacy-advertising-id'; TweakId = 'privacy.advertising-id'; Flow = 'Full'; Catalog = 'BuiltIn'; RestorePoint = 'skip'
            Setup = @(
                "Set-ItemProperty -LiteralPath 'Registry::HKEY_USERS\{UserSid}\Software\Microsoft\Windows\CurrentVersion\AdvertisingInfo' -Name 'Enabled' -Value 1 -Type DWord",
                "Set-ItemProperty -LiteralPath 'Registry::HKEY_USERS\{UserSid}\Software\Microsoft\Windows\CurrentVersion\AdvertisingInfo' -Name 'Id' -Value 'dso-test' -Type String",
                "Set-ItemProperty -LiteralPath 'Registry::HKEY_USERS\{UserSid}\Software\Microsoft\Windows\CurrentVersion\CPSS\Store\AdvertisingInfo' -Name 'Value' -Value 1 -Type DWord"
            )
            Snapshot = @{ Registry = @(
                @{ Path = 'HKU\{UserSid}\Software\Microsoft\Windows\CurrentVersion\AdvertisingInfo'; Name = 'Enabled' }
                @{ Path = 'HKU\{UserSid}\Software\Microsoft\Windows\CurrentVersion\AdvertisingInfo'; Name = 'Id' }
                @{ Path = 'HKU\{UserSid}\Software\Microsoft\Windows\CurrentVersion\CPSS\Store\AdvertisingInfo'; Name = 'Value' }
            ); Services = @() }
            Expect = @{
                Before = 'NotApplied'; ApplyExit = 0; ApplyOutcome = 'Applied'; AfterApply = 'Applied'
                UndoExit = 0; UndoOutcome = 'Undone'; AfterUndo = 'NotApplied'
                UserSid = '{UserSid}'; UserIsCertain = $true
                NotWrittenTo = @(
                    @{ Path = 'HKCU\Software\Microsoft\Windows\CurrentVersion\AdvertisingInfo'; Name = 'Enabled' }
                    @{ Path = 'HKCU\Software\Microsoft\Windows\CurrentVersion\CPSS\Store\AdvertisingInfo'; Name = 'Value' }
                )
                AfterApplySnapshot = @{ Registry = @(
                    @{ Path = 'HKU\{UserSid}\Software\Microsoft\Windows\CurrentVersion\AdvertisingInfo'; Name = 'Enabled'; Exists = $true; Kind = 'DWord'; Data = '0' }
                    @{ Path = 'HKU\{UserSid}\Software\Microsoft\Windows\CurrentVersion\AdvertisingInfo'; Name = 'Id'; Exists = $true; Kind = 'String'; Data = 'dso-test' }
                    @{ Path = 'HKU\{UserSid}\Software\Microsoft\Windows\CurrentVersion\CPSS\Store\AdvertisingInfo'; Name = 'Value'; Exists = $true; Kind = 'DWord'; Data = '0' }
                ) }
                KeyPresentAfterUndo = @('HKU\{UserSid}\Software\Microsoft\Windows\CurrentVersion\AdvertisingInfo', 'HKU\{UserSid}\Software\Microsoft\Windows\CurrentVersion\CPSS\Store\AdvertisingInfo')
            }
            Hypothesis = 'Odczyt VM 26H2 (2026-10-21): Enabled = 0 i Value = 0, brak wartości Id (wyłączony po OOBE), oba klucze istnieją. Setup ustawia 1/1 (przełącznik włączony) oraz tworzy testową wartość Id (String, dso-test), więc Before = NotApplied. R18: apply zapisuje tylko Enabled i Value i NIE usuwa Id; sprawdza to AfterApplySnapshot (Id nadal Exists = true, String, dso-test), a snapshot przed/po undo pilnuje, że Id przetrwało też undo. Undo przywraca 1/1 ze snapshotu. Stan przełącznika w Ustawieniach po apply (OS dodatkowo usuwa Id) potwierdza Task 9.'
        }
        @{
            Name = 'privacy-tailored-experiences'; TweakId = 'privacy.tailored-experiences'; Flow = 'Full'; Catalog = 'BuiltIn'; RestorePoint = 'skip'
            Setup = @("Remove-ItemProperty -LiteralPath 'Registry::HKEY_USERS\{UserSid}\Software\Microsoft\Windows\CurrentVersion\Privacy' -Name 'PersonalizedOffersEnabled' -ErrorAction SilentlyContinue")
            Snapshot = @{ Registry = @(@{ Path = 'HKU\{UserSid}\Software\Microsoft\Windows\CurrentVersion\Privacy'; Name = 'PersonalizedOffersEnabled' }); Services = @() }
            Expect = @{
                Before = 'NotApplied'; ApplyExit = 0; ApplyOutcome = 'Applied'; AfterApply = 'Applied'
                UndoExit = 0; UndoOutcome = 'Undone'; AfterUndo = 'NotApplied'
                UserSid = '{UserSid}'; UserIsCertain = $true
                NotWrittenTo = @(@{ Path = 'HKCU\Software\Microsoft\Windows\CurrentVersion\Privacy'; Name = 'PersonalizedOffersEnabled' })
                AfterApplySnapshot = @{ Registry = @(@{ Path = 'HKU\{UserSid}\Software\Microsoft\Windows\CurrentVersion\Privacy'; Name = 'PersonalizedOffersEnabled'; Exists = $true; Kind = 'DWord'; Data = '0' }) }
                KeyPresentAfterUndo = @('HKU\{UserSid}\Software\Microsoft\Windows\CurrentVersion\Privacy')
            }
            Hypothesis = 'Odczyt VM 26H2 (2026-10-21): klucz Privacy istnieje, PersonalizedOffersEnabled brak (przełącznik wyłączony, ale detekcja widzi NotApplied). Apply zapisuje jawne 0; undo przywraca brak wartości, klucz zostaje. Dowód skutku: stan przełącznika w Ustawieniach, treści nie obserwowano.'
        }
        @{
            Name = 'privacy-spotlight'; TweakId = 'privacy.spotlight'; Flow = 'Full'; Catalog = 'BuiltIn'; RestorePoint = 'skip'
            Setup = @(
                "if (Test-Path -LiteralPath 'Registry::HKEY_USERS\{UserSid}\Software\Policies\Microsoft\Windows\CloudContent') { Remove-Item -LiteralPath 'Registry::HKEY_USERS\{UserSid}\Software\Policies\Microsoft\Windows\CloudContent' -Recurse }"
            )
            Snapshot = @{ Registry = @(@{ Path = 'HKU\{UserSid}\Software\Policies\Microsoft\Windows\CloudContent'; Name = 'DisableWindowsSpotlightFeatures' }); Services = @() }
            Expect = @{
                Before = 'NotApplied'; ApplyExit = 0; ApplyOutcome = 'Applied'; AfterApply = 'Applied'
                UndoExit = 0; UndoOutcome = 'Undone'; AfterUndo = 'NotApplied'
                UserSid = '{UserSid}'; UserIsCertain = $true
                NotWrittenTo = @(@{ Path = 'HKCU\Software\Policies\Microsoft\Windows\CloudContent'; Name = 'DisableWindowsSpotlightFeatures' })
                AfterApplySnapshot = @{ Registry = @(@{ Path = 'HKU\{UserSid}\Software\Policies\Microsoft\Windows\CloudContent'; Name = 'DisableWindowsSpotlightFeatures'; Exists = $true; Kind = 'DWord'; Data = '1' }) }
                KeyAbsentAfterUndo = @('HKU\{UserSid}\Software\Policies\Microsoft\Windows\CloudContent')
            }
            Hypothesis = 'Odczyt VM 26H2 (2026-10-21): klucz Policies\Microsoft\Windows\CloudContent użytkownika i wartość DisableWindowsSpotlightFeatures nie istnieją na świeżej instalacji (Home i Pro). Setup zapewnia brak klucza. Undo ma usunąć wartość i klucz utworzony przez apply. Skutek (tło pulpitu przechodzi z „W centrum uwagi Windows” na „Obraz”, po cofnięciu zostaje „Obraz”) obserwowano w eksploracji; tego przypadek nie sprawdza.'
        }
        @{
            Name = 'privacy-diagnostic-data-required'; TweakId = 'privacy.diagnostic-data-required'; Flow = 'Full'; Catalog = 'BuiltIn'; RestorePoint = 'skip'
            Setup = @(
                "New-Item -Path 'Registry::HKEY_LOCAL_MACHINE\SOFTWARE\Policies\Microsoft\Windows\DataCollection' -Force | Out-Null",
                "Remove-ItemProperty -LiteralPath 'Registry::HKEY_LOCAL_MACHINE\SOFTWARE\Policies\Microsoft\Windows\DataCollection' -Name 'AllowTelemetry' -ErrorAction SilentlyContinue"
            )
            Snapshot = @{ Registry = @(@{ Path = 'HKLM\SOFTWARE\Policies\Microsoft\Windows\DataCollection'; Name = 'AllowTelemetry' }); Services = @() }
            Expect = @{
                Before = 'NotApplied'; ApplyExit = 0; ApplyOutcome = 'Applied'; AfterApply = 'Applied'
                UndoExit = 0; UndoOutcome = 'Undone'; AfterUndo = 'NotApplied'
                AfterApplySnapshot = @{ Registry = @(@{ Path = 'HKLM\SOFTWARE\Policies\Microsoft\Windows\DataCollection'; Name = 'AllowTelemetry'; Exists = $true; Kind = 'DWord'; Data = '1' }) }
                KeyPresentAfterUndo = @('HKLM\SOFTWARE\Policies\Microsoft\Windows\DataCollection')
            }
            Hypothesis = 'Odczyt VM 26H2 (2026-10-21): klucz Policies\Microsoft\Windows\DataCollection istnieje na świeżej instalacji (Home i Pro), AllowTelemetry nie ma. Setup zapewnia klucz i brak wartości. Po undo wartość znowu nie istnieje (snapshot przed = po), a klucz zostaje (KeyPresentAfterUndo, nie KeyAbsentAfterUndo). Skutek (baner, wyszarzony przełącznik opcjonalnych danych) obserwowano w eksploracji. AllowTelemetry=0 poza zakresem (brak edycji Enterprise/Education na VM).'
        }
        @{
            Name = 'privacy-feedback-notifications'; TweakId = 'privacy.feedback-notifications'; Flow = 'Full'; Catalog = 'BuiltIn'; RestorePoint = 'skip'
            Setup = @(
                "New-Item -Path 'Registry::HKEY_LOCAL_MACHINE\SOFTWARE\Policies\Microsoft\Windows\DataCollection' -Force | Out-Null",
                "Remove-ItemProperty -LiteralPath 'Registry::HKEY_LOCAL_MACHINE\SOFTWARE\Policies\Microsoft\Windows\DataCollection' -Name 'DoNotShowFeedbackNotifications' -ErrorAction SilentlyContinue"
            )
            Snapshot = @{ Registry = @(@{ Path = 'HKLM\SOFTWARE\Policies\Microsoft\Windows\DataCollection'; Name = 'DoNotShowFeedbackNotifications' }); Services = @() }
            Expect = @{
                Before = 'NotApplied'; ApplyExit = 0; ApplyOutcome = 'Applied'; AfterApply = 'Applied'
                UndoExit = 0; UndoOutcome = 'Undone'; AfterUndo = 'NotApplied'
                AfterApplySnapshot = @{ Registry = @(@{ Path = 'HKLM\SOFTWARE\Policies\Microsoft\Windows\DataCollection'; Name = 'DoNotShowFeedbackNotifications'; Exists = $true; Kind = 'DWord'; Data = '1' }) }
                KeyPresentAfterUndo = @('HKLM\SOFTWARE\Policies\Microsoft\Windows\DataCollection')
            }
            Hypothesis = 'Odczyt VM 26H2 (2026-10-21): klucz DataCollection istnieje na świeżej instalacji, DoNotShowFeedbackNotifications nie ma. Setup zapewnia klucz i brak wartości. Po undo wartość znowu nie istnieje, a klucz zostaje (KeyPresentAfterUndo). Dowód skutku: stan strony Diagnostyka i opinie w Ustawieniach (baner, lista Częstotliwość opinii znika), samych powiadomień nie obserwowano.'
        }
        @{
            Name = 'privacy-diagtrack'; TweakId = 'privacy.diagtrack'; Flow = 'Full'; Catalog = 'BuiltIn'; RestorePoint = 'skip'
            Setup = @(
                "Set-Service -Name 'DiagTrack' -StartupType Automatic",
                "Start-Service -Name 'DiagTrack'"
            )
            Snapshot = @{ Registry = @(@{ Path = 'HKLM\SYSTEM\CurrentControlSet\Services\DiagTrack'; Name = 'Start' }); Services = @('DiagTrack') }
            CheckAfterApply = '@(Get-CimInstance -ClassName Win32_Service -Filter "Name=''DiagTrack''" | Where-Object { $_.State -eq ''Stopped'' -and $_.StartMode -eq ''Disabled'' }).Count -eq 1'
            CheckAfterUndo = '@(Get-CimInstance -ClassName Win32_Service -Filter "Name=''DiagTrack''" | Where-Object { $_.State -eq ''Running'' -and $_.StartMode -eq ''Auto'' }).Count -eq 1'
            Expect = @{
                Before = 'NotApplied'; ApplyExit = 0; ApplyOutcome = 'Applied'; AfterApply = 'Applied'
                UndoExit = 0; UndoOutcome = 'Undone'; AfterUndo = 'NotApplied'
                AfterApplySnapshot = @{
                    Registry = @(@{ Path = 'HKLM\SYSTEM\CurrentControlSet\Services\DiagTrack'; Name = 'Start'; Exists = $true; Kind = 'DWord'; Data = '4' })
                    Services = @(@{ Name = 'DiagTrack'; StartMode = 'Disabled'; State = 'Stopped' })
                }
            }
            Hypothesis = 'Odczyt VM 26H2 (2026-10-21): DiagTrack = Automatic, Running (Home i Pro). Setup ustawia ten stan jawnie. Snapshot (Start i usługa: StartMode, State) sprzed apply musi być identyczny po undo, więc typ startu wraca ze snapshotu, a usługa, która działała, znowu działa. Detekcja opiera się na typie startu (Review Focus 5; przypadek StartPending pokrywa test jednostkowy na fake''ach). Stan po restarcie i wyszukiwanie Windows Update obserwowano w eksploracji, nie w tym przypadku.'
        }
        @{
            Name = 'ux-classic-context-menu'; TweakId = 'ux.classic-context-menu'; Flow = 'Full'; Catalog = 'BuiltIn'; RestorePoint = 'skip'
            Setup = @("if (Test-Path -LiteralPath 'Registry::HKEY_USERS\{UserSid}\Software\Classes\CLSID\{86ca1aa0-34aa-4e8b-a509-50c905bae2a2}') { Remove-Item -LiteralPath 'Registry::HKEY_USERS\{UserSid}\Software\Classes\CLSID\{86ca1aa0-34aa-4e8b-a509-50c905bae2a2}' -Recurse }")
            Snapshot = @{ Registry = @(@{ Path = 'HKU\{UserSid}\Software\Classes\CLSID\{86ca1aa0-34aa-4e8b-a509-50c905bae2a2}\InprocServer32'; Name = '' }); Services = @() }
            Expect = @{
                Before = 'NotApplied'; ApplyExit = 0; ApplyOutcome = 'Applied'; AfterApply = 'Applied'
                UndoExit = 0; UndoOutcome = 'Undone'; AfterUndo = 'NotApplied'
                UserSid = '{UserSid}'; UserIsCertain = $true
                NotWrittenTo = @(@{ Path = 'HKCU\Software\Classes\CLSID\{86ca1aa0-34aa-4e8b-a509-50c905bae2a2}\InprocServer32'; Name = '' })
                AfterApplySnapshot = @{ Registry = @(@{ Path = 'HKU\{UserSid}\Software\Classes\CLSID\{86ca1aa0-34aa-4e8b-a509-50c905bae2a2}\InprocServer32'; Name = ''; Exists = $true; Kind = 'String'; Data = '' }) }
                KeyAbsentAfterUndo = @(
                    'HKU\{UserSid}\Software\Classes\CLSID\{86ca1aa0-34aa-4e8b-a509-50c905bae2a2}\InprocServer32',
                    'HKU\{UserSid}\Software\Classes\CLSID\{86ca1aa0-34aa-4e8b-a509-50c905bae2a2}'
                )
            }
            Hypothesis = 'Odczyt VM 26H2 (2026-10-21): klucze {86ca...} i InprocServer32 nie istnieją na świeżej instalacji (Home i Pro); wartość domyślna to pusty REG_SZ (pusta nazwa wpisu). Setup zapewnia brak klucza. Silnik zapisuje w snapshocie najwyższy brakujący klucz ({86ca...}) i po undo kasuje oba. Task 4 obserwował usunięcie obu kluczy przy ręcznym cofnięciu; przez silnik potwierdzi to dopiero ten przypadek (Task 9). Skutek (okno #32768 po restarcie Eksploratora) obserwowano w eksploracji.'
        }
        @{
            Name = 'ux-taskbar-end-task'; TweakId = 'ux.taskbar-end-task'; Flow = 'Full'; Catalog = 'BuiltIn'; RestorePoint = 'skip'
            Setup = @("if (Test-Path -LiteralPath 'Registry::HKEY_USERS\{UserSid}\Software\Microsoft\Windows\CurrentVersion\Explorer\Advanced\TaskbarDeveloperSettings') { Remove-Item -LiteralPath 'Registry::HKEY_USERS\{UserSid}\Software\Microsoft\Windows\CurrentVersion\Explorer\Advanced\TaskbarDeveloperSettings' -Recurse }")
            Snapshot = @{ Registry = @(@{ Path = 'HKU\{UserSid}\Software\Microsoft\Windows\CurrentVersion\Explorer\Advanced\TaskbarDeveloperSettings'; Name = 'TaskbarEndTask' }); Services = @() }
            Expect = @{
                Before = 'NotApplied'; ApplyExit = 0; ApplyOutcome = 'Applied'; AfterApply = 'Applied'
                UndoExit = 0; UndoOutcome = 'Undone'; AfterUndo = 'NotApplied'
                UserSid = '{UserSid}'; UserIsCertain = $true
                NotWrittenTo = @(@{ Path = 'HKCU\Software\Microsoft\Windows\CurrentVersion\Explorer\Advanced\TaskbarDeveloperSettings'; Name = 'TaskbarEndTask' })
                AfterApplySnapshot = @{ Registry = @(@{ Path = 'HKU\{UserSid}\Software\Microsoft\Windows\CurrentVersion\Explorer\Advanced\TaskbarDeveloperSettings'; Name = 'TaskbarEndTask'; Exists = $true; Kind = 'DWord'; Data = '1' }) }
                KeyAbsentAfterUndo = @('HKU\{UserSid}\Software\Microsoft\Windows\CurrentVersion\Explorer\Advanced\TaskbarDeveloperSettings')
            }
            Hypothesis = 'Odczyt VM 26H2 (2026-10-21): wartość TaskbarEndTask i klucz TaskbarDeveloperSettings nie istnieją na świeżej instalacji (Home i Pro); klucz tworzy dopiero przełącznik Ustawień. Setup zapewnia brak klucza. Task 4 nie obserwował usunięcia samego klucza przy cofnięciu (klucz założył wcześniej przełącznik), więc dowodzi tego dopiero ten przypadek (Task 9). Skutek (pozycja Zakończ zadanie w menu przycisku po restarcie Eksploratora) obserwowano w eksploracji.'
        }
        @{
            Name = 'disk-hibernation-unsupported'; TweakId = 'disk.hibernation'; Flow = 'DetectOnly'; Catalog = 'BuiltIn'; RestorePoint = 'skip'
            Expect = @{ Before = 'NotSupported' }
            Hypothesis = 'Odczyt VM 26H2 (2026-10-18): firmware Hyper-V nie obsługuje hibernacji (powercfg /a), brak hiberfil.sys, brak Control\Power\HibernateEnabled. Detekcja przez CallNtPowerInformation bez parsowania tekstu ma dać NotSupported (SystemS4 = FALSE nie był odczytany osobno; potwierdzone pośrednio: przebieg 2026-10-18 dał NotSupported na Home i Pro). Prawdziwy test, bez apply.'
        }
        @{
            Name = 'disk-hibernation'; TweakId = 'disk.hibernation'; Flow = 'Full'; Catalog = 'BuiltIn'; RestorePoint = 'skip'
            Skip = 'VM Hyper-V bez obsługi S4 – apply/undo do weryfikacji na sprzęcie'
            Snapshot = @{ Registry = @(@{ Path = 'HKLM\SYSTEM\CurrentControlSet\Control\Power'; Name = 'HibernateEnabled' }); Services = @() }
            Expect = @{
                Before = 'NotApplied'; ApplyExit = 0; ApplyOutcome = 'Applied'; AfterApply = 'Applied'
                UndoExit = 0; UndoOutcome = 'Undone'; AfterUndo = 'NotApplied'
                AfterApplySnapshot = @{ Registry = @(@{ Path = 'HKLM\SYSTEM\CurrentControlSet\Control\Power'; Name = 'HibernateEnabled'; Exists = $true; Kind = 'DWord'; Data = '0' }) }
            }
            Hypothesis = 'Do uruchomienia na sprzęcie z obsługą hibernacji (Before = NotApplied wymaga SystemS4 = TRUE). Na VM Hyper-V powercfg /h on kończy się kodem 1, więc undo nie da się sprawdzić.'
        }
        @{
            Name = 'disk-ntfs-last-access'; TweakId = 'disk.ntfs-last-access'; Flow = 'Reboot'; Catalog = 'BuiltIn'; RestorePoint = 'skip'
            Setup = @("Set-ItemProperty -LiteralPath 'Registry::HKEY_LOCAL_MACHINE\SYSTEM\CurrentControlSet\Control\FileSystem' -Name 'NtfsDisableLastAccessUpdate' -Value 0x80000000 -Type DWord")
            Snapshot = @{ Registry = @(@{ Path = 'HKLM\SYSTEM\CurrentControlSet\Control\FileSystem'; Name = 'NtfsDisableLastAccessUpdate' }); Services = @() }
            Expect = @{
                Before = 'NotApplied'; ApplyExit = 0; ApplyOutcome = 'Applied'; AfterApply = 'Applied'
                AfterReboot = 'Applied'
                UndoExit = 0; UndoOutcome = 'Undone'; AfterUndo = 'NotApplied'
                AfterApplySnapshot = @{ Registry = @(@{ Path = 'HKLM\SYSTEM\CurrentControlSet\Control\FileSystem'; Name = 'NtfsDisableLastAccessUpdate'; Exists = $true; Kind = 'DWord'; Data = '-2147483647' }) }
                KeyPresentAfterUndo = @('HKLM\SYSTEM\CurrentControlSet\Control\FileSystem')
            }
            Hypothesis = 'Odczyt VM 26H2 (2026-10-18): domyślnie 0x80000002; fsutil set 1 zapisuje 0x80000001 (odczyt jako Int32 -2147483647). Setup ustawia 0x80000000 (różne od original 0x80000002), więc stan końcowy = stan przed (SnapshotDiff) dowodzi undo ze snapshotu, nie z fallbacku. Flow Reboot, bo MS wymaga restartu; skutek na plikach nie jest mierzony.'
        }
        @{
            Name = 'disk-ntfs-last-access-mode3'; TweakId = 'disk.ntfs-last-access'; Flow = 'DetectOnly'; Catalog = 'BuiltIn'; RestorePoint = 'skip'
            Setup = @("[Microsoft.Win32.Registry]::SetValue('HKEY_LOCAL_MACHINE\SYSTEM\CurrentControlSet\Control\FileSystem', 'NtfsDisableLastAccessUpdate', [BitConverter]::ToInt32([BitConverter]::GetBytes([uint32]2147483651), 0), [Microsoft.Win32.RegistryValueKind]::DWord)")
            Snapshot = @{ Registry = @(@{ Path = 'HKLM\SYSTEM\CurrentControlSet\Control\FileSystem'; Name = 'NtfsDisableLastAccessUpdate' }); Services = @() }
            Expect = @{ Before = 'Applied' }
            Hypothesis = 'Tryb 3 (0x80000003, system zarządza, aktualizacje wyłączone wg hintu Winaero; na 26H2 nieobserwowany, więc Setup zapisuje go przez API .NET) ma dać Applied: detekcja skryptem po masce dolnych 2 bitów, a nie porównanie z dokładną wartością 0x80000001. Tylko odczyt, bez apply; stan wartości zostaje w VM do przywrócenia checkpointu.'
        }
        @{
            Name = 'disk-ntfs-8dot3'; TweakId = 'disk.ntfs-8dot3'; Flow = 'Full'; Catalog = 'BuiltIn'; RestorePoint = 'skip'
            Setup = @("Set-ItemProperty -LiteralPath 'Registry::HKEY_LOCAL_MACHINE\SYSTEM\CurrentControlSet\Control\FileSystem' -Name 'NtfsDisable8dot3NameCreation' -Value 0 -Type DWord")
            Snapshot = @{ Registry = @(@{ Path = 'HKLM\SYSTEM\CurrentControlSet\Control\FileSystem'; Name = 'NtfsDisable8dot3NameCreation' }); Services = @() }
            Expect = @{
                Before = 'NotApplied'; ApplyExit = 0; ApplyOutcome = 'Applied'; AfterApply = 'Applied'
                UndoExit = 0; UndoOutcome = 'Undone'; AfterUndo = 'NotApplied'
                AfterApplySnapshot = @{ Registry = @(@{ Path = 'HKLM\SYSTEM\CurrentControlSet\Control\FileSystem'; Name = 'NtfsDisable8dot3NameCreation'; Exists = $true; Kind = 'DWord'; Data = '1' }) }
                KeyPresentAfterUndo = @('HKLM\SYSTEM\CurrentControlSet\Control\FileSystem')
            }
            Hypothesis = 'Odczyt VM 26H2 (2026-10-18): domyślnie rejestr 2 (per wolumin), C: ma 8dot3 już wyłączone. Setup ustawia 0 (różne od original 2), więc stan końcowy = stan przed dowodzi undo ze snapshotu. Sprawdzana jest tylko polityka globalna w rejestrze, nie skutek na plikach ani stan woluminu C:.'
        }
        @{
            Name = 'disk-reserved-storage'; TweakId = 'disk.reserved-storage'; Flow = 'Full'; Catalog = 'BuiltIn'; RestorePoint = 'skip'
            Snapshot = @{ Registry = @(@{ Path = 'HKLM\SOFTWARE\Microsoft\Windows\CurrentVersion\ReserveManager'; Name = 'PassedPolicy' }); Services = @() }
            CheckAfterApply = "[int](Get-ItemPropertyValue -LiteralPath 'Registry::HKEY_LOCAL_MACHINE\SOFTWARE\Microsoft\Windows\CurrentVersion\ReserveManager' -Name 'PassedPolicy') -eq 0"
            CheckAfterUndo = "[int](Get-ItemPropertyValue -LiteralPath 'Registry::HKEY_LOCAL_MACHINE\SOFTWARE\Microsoft\Windows\CurrentVersion\ReserveManager' -Name 'PassedPolicy') -eq 1"
            Expect = @{
                Before = 'NotApplied'; ApplyExit = 0; ApplyOutcome = 'Applied'; AfterApply = 'Applied'
                UndoExit = 0; UndoOutcome = 'Undone'; AfterUndo = 'NotApplied'
            }
            Hypothesis = 'Odczyt VM 26H2 (2026-10-18): ReservedStorageState = Enabled na starcie; po Disabled ReserveManager\PassedPolicy = 0, po Enabled = 1 (obserwacja, nie dokumentacja MS). Check czyta rejestr zamiast cmdletu detekcji, więc jest od niej niezależny. MiscPolicyInfo (nowa wartość po cyklu) celowo poza Snapshot i Check. Zachowanie przy oczekującej aktualizacji nie jest odtwarzane na VM (pokryte testem jednostkowym na mocku).'
        }
        @{
            Name = 'perf-sysmain'; TweakId = 'perf.sysmain'; Flow = 'Full'; Catalog = 'BuiltIn'; RestorePoint = 'skip'
            Snapshot = @{ Registry = @(@{ Path = 'HKLM\SYSTEM\CurrentControlSet\Services\SysMain'; Name = 'Start' }); Services = @('SysMain') }
            CheckAfterApply = '@(Get-CimInstance -ClassName Win32_Service -Filter "Name=''SysMain''" | Where-Object { $_.State -eq ''Stopped'' -and $_.StartMode -eq ''Disabled'' }).Count -eq 1'
            CheckAfterUndo = '@(Get-CimInstance -ClassName Win32_Service -Filter "Name=''SysMain''" | Where-Object { $_.State -eq ''Running'' -and $_.StartMode -eq ''Auto'' }).Count -eq 1'
            Expect = @{
                Before = 'NotApplied'; ApplyExit = 0; ApplyOutcome = 'Applied'; AfterApply = 'Applied'
                UndoExit = 0; UndoOutcome = 'Undone'; AfterUndo = 'NotApplied'
                AfterApplySnapshot = @{
                    Registry = @(@{ Path = 'HKLM\SYSTEM\CurrentControlSet\Services\SysMain'; Name = 'Start'; Exists = $true; Kind = 'DWord'; Data = '4' })
                    Services = @(@{ Name = 'SysMain'; StartMode = 'Disabled'; State = 'Stopped' })
                }
            }
            Hypothesis = 'Odczyt VM 26H2 (2026-10-18): SysMain = Auto, Running, DelayedAutoStart false, Start = DWORD 2, brak usług zależnych. Snapshot (Start i usługa: StartMode, DelayedAutoStart, State) sprzed apply musi być identyczny po undo, więc usługa, która działała, znowu działa. CheckAfterUndo dodatkowo wprost sprawdza Auto + Running (stan świeżego 26H2). Odmowa stopu przy działających zależnych nie jest odtwarzana na VM (brak usług zależnych), pokrywa ją test jednostkowy na fake''ach. Stan po restarcie nie jest badany.'
        }
        @{
            Name = 'perf-paging-executive'; TweakId = 'perf.paging-executive'; Flow = 'Reboot'; Catalog = 'BuiltIn'; RestorePoint = 'skip'
            Snapshot = @{ Registry = @(@{ Path = 'HKLM\SYSTEM\CurrentControlSet\Control\Session Manager\Memory Management'; Name = 'DisablePagingExecutive' }); Services = @() }
            Expect = @{
                Before = 'NotApplied'; ApplyExit = 0; ApplyOutcome = 'Applied'; AfterApply = 'Applied'
                AfterReboot = 'Applied'
                UndoExit = 0; UndoOutcome = 'Undone'; AfterUndo = 'NotApplied'
                AfterApplySnapshot = @{ Registry = @(@{ Path = 'HKLM\SYSTEM\CurrentControlSet\Control\Session Manager\Memory Management'; Name = 'DisablePagingExecutive'; Exists = $true; Kind = 'DWord'; Data = '1' }) }
                KeyPresentAfterUndo = @('HKLM\SYSTEM\CurrentControlSet\Control\Session Manager\Memory Management')
            }
            Hypothesis = 'Odczyt VM 26H2 (2026-10-18): DisablePagingExecutive istnieje i wynosi DWORD 0 (Home i Pro), więc stan końcowy po undo = DWORD 0 (SnapshotDiff pusty). Flow Reboot, bo wartość działa po restarcie (MS, archiwum); sprawdzane jest tylko, że 1 przetrwa restart i że undo przywraca 0. Skutek dla jądra nie jest mierzony. Przypadku z brakiem wartości na starcie nie ma, bo nie zachodzi na 26H2.'
        }
        @{
            Name = 'net-ipv6-prefer-ipv4'; TweakId = 'net.ipv6-prefer-ipv4'; Flow = 'Reboot'; Catalog = 'BuiltIn'; RestorePoint = 'skip'
            Snapshot = @{ Registry = @(@{ Path = 'HKLM\SYSTEM\CurrentControlSet\Services\Tcpip6\Parameters'; Name = 'DisabledComponents' }); Services = @() }
            CheckAfterApply = '$p = @(Get-NetPrefixPolicy | Where-Object { $_.Prefix -eq ''::ffff:0:0/96'' }); $d = @(Get-NetPrefixPolicy | Where-Object { $_.Prefix -eq ''::/0'' }); $p.Count -eq 1 -and $d.Count -eq 1 -and [int]$p[0].Precedence -gt [int]$d[0].Precedence'
            CheckAfterUndo = '$p = @(Get-NetPrefixPolicy | Where-Object { $_.Prefix -eq ''::ffff:0:0/96'' }); $d = @(Get-NetPrefixPolicy | Where-Object { $_.Prefix -eq ''::/0'' }); $p.Count -eq 1 -and $d.Count -eq 1 -and [int]$p[0].Precedence -lt [int]$d[0].Precedence'
            Expect = @{
                Before = 'NotApplied'; ApplyExit = 0; ApplyOutcome = 'Applied'; AfterApply = 'Applied'
                AfterReboot = 'Applied'
                UndoExit = 0; UndoOutcome = 'Undone'; AfterUndo = 'NotApplied'
                AfterApplySnapshot = @{ Registry = @(@{ Path = 'HKLM\SYSTEM\CurrentControlSet\Services\Tcpip6\Parameters'; Name = 'DisabledComponents'; Exists = $true; Kind = 'DWord'; Data = '32' }) }
                KeyPresentAfterUndo = @('HKLM\SYSTEM\CurrentControlSet\Services\Tcpip6\Parameters')
            }
            Hypothesis = 'Odczyt VM 26H2 (2026-10-18): klucz Tcpip6\Parameters istnieje, a DisabledComponents NIE istnieje (Home i Pro), więc stan końcowy po undo = brak wartości (SnapshotDiff pusty), a klucz zostaje (KeyPresentAfterUndo). Domyślne precedencje Get-NetPrefixPolicy: ::ffff:0:0/96 = 35, ::/0 = 40. CheckAfterApply (po restarcie, porównanie liczbowe, bez tekstu) sprawdza informacyjnie, że po 0x20 precedencja ::ffff:0:0/96 jest większa niż ::/0 (MS: Prefer IPv4 over IPv6); jeśli Windows tego nie zmieni, przypadek ma to ujawnić, nie ukryć. Preferencja IPv4 w ruchu nie jest testowana: VM bez sieci.'
        }
        @{
            Name = 'net-ipv6-disable'; TweakId = 'net.ipv6-disable'; Flow = 'Reboot'; Catalog = 'BuiltIn'; RestorePoint = 'skip'
            Snapshot = @{ Registry = @(@{ Path = 'HKLM\SYSTEM\CurrentControlSet\Services\Tcpip6\Parameters'; Name = 'DisabledComponents' }); Services = @() }
            Expect = @{
                Before = 'NotApplied'; ApplyExit = 0; ApplyOutcome = 'Applied'; AfterApply = 'Applied'
                AfterReboot = 'Applied'
                UndoExit = 0; UndoOutcome = 'Undone'; AfterUndo = 'NotApplied'
                AfterApplySnapshot = @{ Registry = @(@{ Path = 'HKLM\SYSTEM\CurrentControlSet\Services\Tcpip6\Parameters'; Name = 'DisabledComponents'; Exists = $true; Kind = 'DWord'; Data = '255' }) }
                KeyPresentAfterUndo = @('HKLM\SYSTEM\CurrentControlSet\Services\Tcpip6\Parameters')
            }
            Hypothesis = 'Odczyt VM 26H2 (2026-10-18): DisabledComponents nie istnieje (Home i Pro), więc stan końcowy po undo = brak wartości (SnapshotDiff pusty), klucz Parameters zostaje. Sprawdzane jest tylko, że 0xFF przetrwa restart i że undo przywraca brak. Bez CheckAfterApply: MS nie definiuje odczytu stanu stosu przy 0xFF (pole wyboru karty się nie zmienia). Skutek dla składników Windows i ruchu nie jest testowany: VM bez sieci.'
        }
        @{
            Name = 'perf-vbs-hvci'; TweakId = 'perf.vbs-hvci'; Flow = 'Reboot'; Catalog = 'BuiltIn'; RestorePoint = 'skip'
            Snapshot = @{ Registry = @(@{ Path = 'HKLM\SYSTEM\CurrentControlSet\Control\DeviceGuard\Scenarios\HypervisorEnforcedCodeIntegrity'; Name = 'Enabled' }); Services = @() }
            CheckAfterApply = '(Get-ItemPropertyValue -LiteralPath ''Registry::HKEY_LOCAL_MACHINE\SYSTEM\CurrentControlSet\Control\DeviceGuard\Scenarios\HypervisorEnforcedCodeIntegrity'' -Name ''Enabled'') -eq 0 -and $null -ne ($g = Get-CimInstance -Namespace ''root\Microsoft\Windows\DeviceGuard'' -ClassName Win32_DeviceGuard -ErrorAction Stop) -and @($g.SecurityServicesRunning) -notcontains 2'
            CheckAfterUndo = '-not (Test-Path -LiteralPath ''Registry::HKEY_LOCAL_MACHINE\SYSTEM\CurrentControlSet\Control\DeviceGuard\Scenarios\HypervisorEnforcedCodeIntegrity'') -and $null -ne ($g = Get-CimInstance -Namespace ''root\Microsoft\Windows\DeviceGuard'' -ClassName Win32_DeviceGuard -ErrorAction Stop) -and @($g.SecurityServicesRunning) -notcontains 2'
            Expect = @{
                Before = 'NotApplied'; ApplyExit = 0; ApplyOutcome = 'Applied'; AfterApply = 'Applied'
                AfterReboot = 'Applied'
                UndoExit = 0; UndoOutcome = 'Undone'; AfterUndo = 'NotApplied'
                AfterApplySnapshot = @{ Registry = @(@{ Path = 'HKLM\SYSTEM\CurrentControlSet\Control\DeviceGuard\Scenarios\HypervisorEnforcedCodeIntegrity'; Name = 'Enabled'; Exists = $true; Kind = 'DWord'; Data = '0' }) }
                KeyAbsentAfterUndo = @('HKLM\SYSTEM\CurrentControlSet\Control\DeviceGuard\Scenarios\HypervisorEnforcedCodeIntegrity')
            }
            Hypothesis = 'Odczyt VM 26H2 (2026-10-18): na świeżej VM Gen2 memory integrity NIE działa (VirtualizationBasedSecurityStatus = 0, SecurityServicesRunning = [0]), a klucz Scenarios\HypervisorEnforcedCodeIntegrity nie istnieje. Zmiana 2026-10-21 (E3-P2): bez wpisu Enabled = 0 skrypt detect zwraca NotApplied (nic nie zastosowano; wcześniej Partial = rejestr Mismatch + skrypt Match, co dawało w GUI "Cofnij" kończące się UndoIncomplete). Stąd Before i AfterUndo = NotApplied (konfiguracja nieustawiona), po apply i po restarcie Applied, a po undo klucz utworzony przez apply znika (reguła A1; snapshot początkowy i końcowy: brak wartości). Realny stan (memory integrity działa do restartu, wynik PendingReboot) NIE jest testowany na VM: na świeżej VM Gen2 memory integrity nie działa (VirtualizationBasedSecurityStatus = 0); ten scenariusz pokrywają testy jednostkowe na fake''ach. Check sprawdza tylko konfigurację i że Win32_DeviceGuard nie raportuje 2 w SecurityServicesRunning.'
        }
        @{
            Name = 'net-smb-guest-access'; TweakId = 'net.smb-guest-access'; Flow = 'Full'; Catalog = 'BuiltIn'; RestorePoint = 'skip'
            Snapshot = @{ Registry = @(@{ Path = 'HKLM\SYSTEM\CurrentControlSet\Services\LanmanWorkstation\Parameters'; Name = 'AllowInsecureGuestAuth' }); Services = @() }
            CheckAfterApply = '(Get-SmbClientConfiguration).EnableInsecureGuestLogons -eq $true'
            CheckAfterUndo = '$isHomeEdition = (Get-ItemPropertyValue -LiteralPath ''Registry::HKEY_LOCAL_MACHINE\SOFTWARE\Microsoft\Windows NT\CurrentVersion'' -Name ''EditionID'') -in @(''Core'', ''CoreN'', ''CoreSingleLanguage'', ''CoreCountrySpecific''); (Get-SmbClientConfiguration).EnableInsecureGuestLogons -eq $isHomeEdition'
            Expect = @{
                Before = @('NotApplied', 'Partial'); ApplyExit = 0; ApplyOutcome = 'Applied'; AfterApply = 'Applied'
                UndoExit = 0; UndoOutcome = 'Undone'; AfterUndo = @('NotApplied', 'Partial')
                AfterApplySnapshot = @{ Registry = @(@{ Path = 'HKLM\SYSTEM\CurrentControlSet\Services\LanmanWorkstation\Parameters'; Name = 'AllowInsecureGuestAuth'; Exists = $true; Kind = 'DWord'; Data = '1' }) }
                KeyPresentAfterUndo = @('HKLM\SYSTEM\CurrentControlSet\Services\LanmanWorkstation\Parameters')
            }
            Hypothesis = 'Odczyt VM 26H2 (2026-10-18): AllowInsecureGuestAuth nie istnieje (Home i Pro), a EnableInsecureGuestLogons = True na Home i False na Pro. Stąd Before i AfterUndo zależą od edycji (Pro NotApplied, Home Partial: rejestr bez wartości, a odczyt cmdletu już True), po apply Applied na obu. Stan końcowy po undo = brak wartości (SnapshotDiff pusty), a CheckAfterUndo porównuje odczyt cmdletu z domyślną edycji (EditionID Core* = True, Professional = False), czyli ma wykazać, że usunięcie wartości przywraca domyślną edycji (potwierdzone w przebiegu 20261018-195830: Passed na Home i Pro; rozstrzygający jest Pro, na Home odczyt jest True w obu stanach). Zapis przez API rejestru jest widoczny w cmdlecie bez restartu (CheckAfterApply Passed w przebiegu 20261018-195830). Skutek w LAN: osobny przypadek net-smb-lan-effect (Skip).'
        }
        @{
            Name = 'net-smb-signing-client-off'; TweakId = 'net.smb-signing-client-off'; Flow = 'Full'; Catalog = 'BuiltIn'; RestorePoint = 'skip'
            Snapshot = @{ Registry = @(@{ Path = 'HKLM\SYSTEM\CurrentControlSet\Services\LanmanWorkstation\Parameters'; Name = 'RequireSecuritySignature' }); Services = @() }
            CheckAfterApply = '(Get-SmbClientConfiguration).RequireSecuritySignature -eq $false'
            CheckAfterUndo = '(Get-SmbClientConfiguration).RequireSecuritySignature -eq $true'
            Expect = @{
                Before = 'NotApplied'; ApplyExit = 0; ApplyOutcome = 'Applied'; AfterApply = 'Applied'
                UndoExit = 0; UndoOutcome = 'Undone'; AfterUndo = 'NotApplied'
                AfterApplySnapshot = @{ Registry = @(@{ Path = 'HKLM\SYSTEM\CurrentControlSet\Services\LanmanWorkstation\Parameters'; Name = 'RequireSecuritySignature'; Exists = $true; Kind = 'DWord'; Data = '0' }) }
                KeyPresentAfterUndo = @('HKLM\SYSTEM\CurrentControlSet\Services\LanmanWorkstation\Parameters')
            }
            Hypothesis = 'Odczyt VM 26H2 (2026-10-18): RequireSecuritySignature nie istnieje w rejestrze, a odczyt cmdletu to True na Home i Pro (zdanie MS, że Home nie wymaga podpisywania, nie potwierdziło się). Stan końcowy po undo = brak wartości (SnapshotDiff pusty), CheckAfterUndo = True (potwierdzone w przebiegu 20261018-195830: Passed na Home i Pro, usunięcie wartości przywraca domyślną). Brak restartu potwierdzony: CheckAfterApply czyta cmdlet zaraz po apply przez API rejestru i przeszedł na obu VM. Połączenie z serwerem nie jest testowane: VM bez sieci (net-smb-lan-effect).'
        }
        @{
            Name = 'net-smb-lan-effect'; TweakId = 'net.smb-guest-access'; Flow = 'DetectOnly'; Catalog = 'BuiltIn'; RestorePoint = 'skip'
            Skip = 'brak sieci i serwera SMB na VM'
            Expect = @{ Before = @('NotApplied', 'Partial') }
            Hypothesis = 'Skutek w LAN (połączenie z gościem na serwerze SMB, wymagane podpisywanie po stronie serwera, widoczność komputerów) nie jest mierzony: VM bez sieci i bez serwera SMB. Rekord pominięcia ma to zostawić w raporcie zamiast udawać zaliczenie.'
        }
        @{
            Name = 'sec-bitlocker-prevent-auto'; TweakId = 'sec.bitlocker-prevent-auto'; Flow = 'Full'; Catalog = 'BuiltIn'; RestorePoint = 'skip'
            Snapshot = @{ Registry = @(@{ Path = 'HKLM\SYSTEM\CurrentControlSet\Control\BitLocker'; Name = 'PreventDeviceEncryption' }); Services = @() }
            Expect = @{
                Before = 'NotApplied'; ApplyExit = 0; ApplyOutcome = 'Applied'; AfterApply = 'Applied'
                UndoExit = 0; UndoOutcome = 'Undone'; AfterUndo = 'NotApplied'
                AfterApplySnapshot = @{ Registry = @(@{ Path = 'HKLM\SYSTEM\CurrentControlSet\Control\BitLocker'; Name = 'PreventDeviceEncryption'; Exists = $true; Kind = 'DWord'; Data = '1' }) }
                KeyPresentAfterUndo = @('HKLM\SYSTEM\CurrentControlSet\Control\BitLocker')
            }
            Hypothesis = 'Hipoteza z researchu partii C4 (MS: PreventDeviceEncryption = True (1), DWORD): na świeżym 26H2 Home i Pro wartość nie istnieje, a klucz BitLocker istnieje, więc stan końcowy po undo = brak wartości (SnapshotDiff pusty), a klucz zostaje (KeyPresentAfterUndo). Odczyt VM 26H2 (eksploracja C4, 2026-10-18) potwierdził: wartość nie istnieje (Home i Pro), klucz BitLocker istnieje na obu. Skutek (szyfrowanie nie startuje) osobny przypadek sec-bitlocker-prevent-auto-effect (Skip).'
        }
        @{
            Name = 'sec-bitlocker-prevent-auto-effect'; TweakId = 'sec.bitlocker-prevent-auto'; Flow = 'DetectOnly'; Catalog = 'BuiltIn'; RestorePoint = 'skip'
            Skip = 'wymaga nowego OOBE (skutek dotyczy startu szyfrowania w OOBE); checkpoint VM jest po OOBE'
            Expect = @{ Before = 'NotApplied' }
            Hypothesis = 'Skutek (po zapisie PreventDeviceEncryption = 1 automatyczne szyfrowanie urządzenia nie startuje w OOBE) wymaga nowego OOBE (MS: auto-szyfrowanie startuje w OOBE, a ochrona jest uzbrajana po zalogowaniu kontem Microsoft/Entra); checkpoint VM jest po OOBE. Odczyt VM Home (konto lokalne) pokazał ConversionStatus = 2 (78%), więc konto lokalne samo nie dowodzi, że szyfrowanie nie startuje. Skutek nie jest mierzony. Rekord pominięcia ma to zostawić w raporcie zamiast udawać zaliczenie.'
        }
        @{
            Name = 'sec-bitlocker-decrypt-c'; TweakId = 'sec.bitlocker-decrypt-c'; Flow = 'Execute'; Catalog = 'BuiltIn'; RestorePoint = 'skip'
            Check = '$volume = @(Get-CimInstance -Namespace ''root\CIMV2\Security\MicrosoftVolumeEncryption'' -ClassName Win32_EncryptableVolume -Filter "DriveLetter=''C:''"); if ($volume.Count -ne 1) { throw ''Brak woluminu C: w Win32_EncryptableVolume.'' }; $status = Invoke-CimMethod -InputObject $volume[0] -MethodName ''GetConversionStatus''; if ($status.ReturnValue -ne 0) { throw ''GetConversionStatus zwrocil kod rozny od 0.'' }; $status.ConversionStatus -in @(3, 0)'
            Expect = @{ ExecuteExit = 0; ExecuteOutcome = 'Executed' }
            Hypothesis = 'Obserwacje VM 26H2: po starcie checkpointu czysta-26H2 działa automatyczne szyfrowanie urządzenia na Home i Pro (konto lokalne), a postęp w chwili akcji jest niedeterministyczny. Eksploracja C4 2026-10-18: Home ConversionStatus = 2 (78%, 22:45), Pro ConversionStatus = 0 (0%, 22:51). Pełny przebieg vm-run-20261019-002807: Home 6% zaszyfrowane, Pro 78% zaszyfrowane. Własnego szyfrowania w VM nie robimy (decyzja Dawida). Jeden przypadek Execute dla obu edycji: akcja woła Decrypt i czyta zwrotnie status; Check wymaga GetConversionStatus ReturnValue = 0 oraz ConversionStatus 3 (odszyfrowywanie w toku) lub 0 (zakończone), bez podziału na EditionID. Ścieżki 1/4/3/5 i kod 0x80310029 nie są mierzone na VM (testy jednostkowe na mockach).'
        }
        @{
            Name = 'sec-take-ownership-menu'; TweakId = 'sec.take-ownership-menu'; Flow = 'Full'; Catalog = 'BuiltIn'; RestorePoint = 'skip'
            Setup = @(
                '$k = [Microsoft.Win32.Registry]::LocalMachine.CreateSubKey(''SOFTWARE\Classes\*\shell\runas''); $k.SetValue('''', ''DSO sentinel''); $k.Dispose()'
            )
            Snapshot = @{
                Registry = @(
                    @{ Path = 'HKLM\SOFTWARE\Classes\*\shell\DSOptimizer.TakeOwnership'; Name = 'MUIVerb' }
                    @{ Path = 'HKLM\SOFTWARE\Classes\*\shell\DSOptimizer.TakeOwnership'; Name = 'Extended' }
                    @{ Path = 'HKLM\SOFTWARE\Classes\*\shell\DSOptimizer.TakeOwnership'; Name = 'ExtendedSubCommandsKey' }
                    @{ Path = 'HKLM\SOFTWARE\Classes\*\shell\DSOptimizer.TakeOwnership\ExtendedSubCommandsKey\Shell\runas'; Name = 'MUIVerb' }
                    @{ Path = 'HKLM\SOFTWARE\Classes\*\shell\DSOptimizer.TakeOwnership\ExtendedSubCommandsKey\Shell\runas'; Name = 'HasLUAShield' }
                    @{ Path = 'HKLM\SOFTWARE\Classes\*\shell\DSOptimizer.TakeOwnership\ExtendedSubCommandsKey\Shell\runas\command'; Name = '' }
                    @{ Path = 'HKLM\SOFTWARE\Classes\Directory\shell\DSOptimizer.TakeOwnership'; Name = 'MUIVerb' }
                    @{ Path = 'HKLM\SOFTWARE\Classes\Directory\shell\DSOptimizer.TakeOwnership'; Name = 'Extended' }
                    @{ Path = 'HKLM\SOFTWARE\Classes\Directory\shell\DSOptimizer.TakeOwnership'; Name = 'AppliesTo' }
                    @{ Path = 'HKLM\SOFTWARE\Classes\Directory\shell\DSOptimizer.TakeOwnership'; Name = 'ExtendedSubCommandsKey' }
                    @{ Path = 'HKLM\SOFTWARE\Classes\Directory\shell\DSOptimizer.TakeOwnership\ExtendedSubCommandsKey\Shell\runas'; Name = 'MUIVerb' }
                    @{ Path = 'HKLM\SOFTWARE\Classes\Directory\shell\DSOptimizer.TakeOwnership\ExtendedSubCommandsKey\Shell\runas'; Name = 'HasLUAShield' }
                    @{ Path = 'HKLM\SOFTWARE\Classes\Directory\shell\DSOptimizer.TakeOwnership\ExtendedSubCommandsKey\Shell\runas\command'; Name = '' }
                    @{ Path = 'HKLM\SOFTWARE\Classes\*\shell\runas'; Name = '' }
                )
                Services = @()
            }
            CheckAfterApply = '$lm = [Microsoft.Win32.Registry]::LocalMachine; $s = $lm.OpenSubKey(''SOFTWARE\Classes\*\shell\runas''); $d = $lm.OpenSubKey(''SOFTWARE\Classes\Directory\shell\runas''); $ok = ($null -ne $s) -and ($null -eq $d) -and ($s.GetValue('''') -eq ''DSO sentinel'') -and ($s.SubKeyCount -eq 0) -and ($s.ValueCount -eq 1); if ($null -ne $s) { $s.Dispose() }; if ($null -ne $d) { $d.Dispose() }; $ok'
            CheckAfterUndo = '$lm = [Microsoft.Win32.Registry]::LocalMachine; $s = $lm.OpenSubKey(''SOFTWARE\Classes\*\shell\runas''); $ok = ($null -ne $s) -and ($s.GetValue('''') -eq ''DSO sentinel'') -and ($s.SubKeyCount -eq 0) -and ($s.ValueCount -eq 1); if ($null -ne $s) { $s.Dispose() }; $ok'
            Expect = @{
                Before = 'NotApplied'; ApplyExit = 0; ApplyOutcome = 'Applied'; AfterApply = 'Applied'
                UndoExit = 0; UndoOutcome = 'Undone'; AfterUndo = 'NotApplied'
                AfterApplySnapshot = @{
                    Registry = @(
                        @{ Path = 'HKLM\SOFTWARE\Classes\*\shell\DSOptimizer.TakeOwnership'; Name = 'MUIVerb'; Exists = $true; Kind = 'String'; Data = 'Przejmij na własność' }
                        @{ Path = 'HKLM\SOFTWARE\Classes\*\shell\DSOptimizer.TakeOwnership'; Name = 'Extended'; Exists = $true; Kind = 'String'; Data = '' }
                        @{ Path = 'HKLM\SOFTWARE\Classes\*\shell\DSOptimizer.TakeOwnership'; Name = 'ExtendedSubCommandsKey'; Exists = $true; Kind = 'String'; Data = '*\shell\DSOptimizer.TakeOwnership\ExtendedSubCommandsKey' }
                        @{ Path = 'HKLM\SOFTWARE\Classes\*\shell\DSOptimizer.TakeOwnership\ExtendedSubCommandsKey\Shell\runas'; Name = 'MUIVerb'; Exists = $true; Kind = 'String'; Data = 'Przejmij na własność (administrator)' }
                        @{ Path = 'HKLM\SOFTWARE\Classes\*\shell\DSOptimizer.TakeOwnership\ExtendedSubCommandsKey\Shell\runas'; Name = 'HasLUAShield'; Exists = $true; Kind = 'String'; Data = '' }
                        @{ Path = 'HKLM\SOFTWARE\Classes\*\shell\DSOptimizer.TakeOwnership\ExtendedSubCommandsKey\Shell\runas\command'; Name = ''; Exists = $true; Kind = 'String'; Data = 'cmd.exe /c takeown /f "%1" /a && icacls "%1" /grant *S-1-5-32-544:F /c /l /q' }
                        @{ Path = 'HKLM\SOFTWARE\Classes\Directory\shell\DSOptimizer.TakeOwnership'; Name = 'MUIVerb'; Exists = $true; Kind = 'String'; Data = 'Przejmij na własność' }
                        @{ Path = 'HKLM\SOFTWARE\Classes\Directory\shell\DSOptimizer.TakeOwnership'; Name = 'Extended'; Exists = $true; Kind = 'String'; Data = '' }
                        @{ Path = 'HKLM\SOFTWARE\Classes\Directory\shell\DSOptimizer.TakeOwnership'; Name = 'AppliesTo'; Exists = $true; Kind = 'String'; Data = 'NOT (System.ItemPathDisplay:="C:\Windows" OR System.ItemPathDisplay:~<"C:\Windows\" OR System.ItemPathDisplay:="C:\")' }
                        @{ Path = 'HKLM\SOFTWARE\Classes\Directory\shell\DSOptimizer.TakeOwnership'; Name = 'ExtendedSubCommandsKey'; Exists = $true; Kind = 'String'; Data = 'Directory\shell\DSOptimizer.TakeOwnership\ExtendedSubCommandsKey' }
                        @{ Path = 'HKLM\SOFTWARE\Classes\Directory\shell\DSOptimizer.TakeOwnership\ExtendedSubCommandsKey\Shell\runas'; Name = 'MUIVerb'; Exists = $true; Kind = 'String'; Data = 'Przejmij na własność (administrator)' }
                        @{ Path = 'HKLM\SOFTWARE\Classes\Directory\shell\DSOptimizer.TakeOwnership\ExtendedSubCommandsKey\Shell\runas'; Name = 'HasLUAShield'; Exists = $true; Kind = 'String'; Data = '' }
                        @{ Path = 'HKLM\SOFTWARE\Classes\Directory\shell\DSOptimizer.TakeOwnership\ExtendedSubCommandsKey\Shell\runas\command'; Name = ''; Exists = $true; Kind = 'String'; Data = 'cmd.exe /c takeown /f "%1" /a /r /d Y && icacls "%1" /grant *S-1-5-32-544:F /t /c /l /q' }
                    )
                }
                KeyAbsentAfterUndo = @('HKLM\SOFTWARE\Classes\*\shell\DSOptimizer.TakeOwnership', 'HKLM\SOFTWARE\Classes\Directory\shell\DSOptimizer.TakeOwnership')
                KeyPresentAfterUndo = @('HKLM\SOFTWARE\Classes\*\shell\runas', 'HKLM\SOFTWARE\Classes\*\shell', 'HKLM\SOFTWARE\Classes\Directory\shell')
            }
            Hypothesis = 'Struktura rejestru (nie klikanie): Extended ukrywa pozycję w Verbs(), a Verbs() nie rozwija podmenu (VM 26H2, przebieg 2, 2026-10-18), więc działanie menu potwierdził ręczny test Dawida z UAC (2026-10-19) i leży poza tym przypadkiem. Setup tworzy cudzy klucz-wartownik *\shell\runas (domyślna wartość „DSO sentinel”); apply nie może go dotknąć ani dodać Directory\shell\runas (CheckAfterApply), a undo ma usunąć oba nasze klucze DSOptimizer.TakeOwnership razem z zagnieżdżonymi podkluczami (A1) i zostawić wartownika oraz klucze shell (KeyPresentAfterUndo, CheckAfterUndo). Snapshot obejmuje wszystkie 13 wartości i wartownika: stan końcowy = stan przed. Odczyt VM Home i Pro (2026-10-18): żaden z kluczy DSOptimizer.TakeOwnership ani shell\runas nie istnieje na świeżym 26H2.'
        }
        @{
            Name = 'sec-take-ownership-menu-effect'; TweakId = 'sec.take-ownership-menu'; Flow = 'DetectOnly'; Catalog = 'BuiltIn'; RestorePoint = 'skip'
            Skip = 'wymaga kliknięcia w klasycznym menu Explorera (Shift) i zgody w UAC; PowerShell Direct działa jako Administrator z pełnym tokenem bez UAC, a Verbs() nie pokazuje pozycji z Extended ani podmenu'
            Expect = @{ Before = 'NotApplied' }
            Hypothesis = 'Skutek (menu z Shift, podmenu, UAC, właściciel S-1-5-32-544 i jawna pełna kontrola Administratorów na pliku, folderze, ich zawartości i podfolderze bez List Folder) potwierdził ręczny test Dawida na VM Pro (2026-10-18/19, wariant wartość + podklucz); wariant samego podklucza nie działa. Rekord pominięcia zostawia to w raporcie zamiast udawać zaliczenie.'
        }
        @{
            Name = 'net-smb-harden'; TweakId = 'net.smb-harden'; Flow = 'Execute'; Catalog = 'BuiltIn'; RestorePoint = 'skip'
            Snapshot = @{ Registry = @(@{ Path = 'HKLM\SYSTEM\CurrentControlSet\Services\LanmanWorkstation\Parameters'; Name = 'AllowInsecureGuestAuth' }, @{ Path = 'HKLM\SYSTEM\CurrentControlSet\Services\LanmanWorkstation\Parameters'; Name = 'RequireSecuritySignature' }); Services = @() }
            Check = '$parameters = ''Registry::HKEY_LOCAL_MACHINE\SYSTEM\CurrentControlSet\Services\LanmanWorkstation\Parameters''; $configuration = Get-SmbClientConfiguration; ($configuration.EnableInsecureGuestLogons -eq $false) -and ($configuration.RequireSecuritySignature -eq $true) -and ((Get-ItemPropertyValue -LiteralPath $parameters -Name ''AllowInsecureGuestAuth'') -eq 0) -and ((Get-ItemPropertyValue -LiteralPath $parameters -Name ''RequireSecuritySignature'') -eq 1)'
            Cleanup = '$isHomeEdition = (Get-ItemPropertyValue -LiteralPath ''Registry::HKEY_LOCAL_MACHINE\SOFTWARE\Microsoft\Windows NT\CurrentVersion'' -Name ''EditionID'') -in @(''Core'', ''CoreN'', ''CoreSingleLanguage'', ''CoreCountrySpecific''); Set-SmbClientConfiguration -EnableInsecureGuestLogons $isHomeEdition -RequireSecuritySignature $true -Force; $parameters = ''Registry::HKEY_LOCAL_MACHINE\SYSTEM\CurrentControlSet\Services\LanmanWorkstation\Parameters''; Remove-ItemProperty -LiteralPath $parameters -Name ''AllowInsecureGuestAuth'' -ErrorAction SilentlyContinue; Remove-ItemProperty -LiteralPath $parameters -Name ''RequireSecuritySignature'' -ErrorAction SilentlyContinue; $configuration = Get-SmbClientConfiguration; if ($configuration.EnableInsecureGuestLogons -ne $isHomeEdition -or $configuration.RequireSecuritySignature -ne $true) { throw ''Sprzątanie: odczyt klienta SMB po usunięciu wartości nie wrócił do domyślnej edycji.'' }'
            Expect = @{ ExecuteExit = 0; ExecuteOutcome = 'Executed'; MessageContains = 'odczyt zwrotny zgodny' }
            Hypothesis = 'Odczyt VM 26H2 (2026-10-18): domyślnie gość Home True / Pro False, podpisywanie True na obu, a wartości w rejestrze nie istnieją. Akcja ustawia gość False i podpisywanie True cmdletem (jawne DWORD 0 i 1) i sama sprawdza odczyt zwrotny. Check powtarza ten dowód niezależnie od skryptu akcji: odczyt cmdletu ORAZ DWORD-y w rejestrze. Cleanup przywraca wartości edycji cmdletem (Home: gość True, Pro: False; podpisywanie True), usuwa jawne DWORD-y i sprawdza odczyt, a Snapshot (brak wartości przed i po Cleanup) dowodzi braku śladów; Snapshot nie zastępuje Check. Po usunięciu DWORD-ów odczyt cmdletu wraca do domyślnej edycji (potwierdzone w przebiegu 20261018-195830: brak CleanupError na Home i Pro). Skutek w LAN: nie testowany (VM bez sieci).'
        }
        @{
            Name = 'net-network-private-no-network'; TweakId = 'net.network-private'; Flow = 'Execute'; Catalog = 'BuiltIn'; RestorePoint = 'skip'
            Check = '@(Get-NetConnectionProfile).Count -eq 0'
            Expect = @{ ExecuteExit = 1; ExecuteOutcome = 'Failed'; MessageContains = 'profilu publicznym' }
            Hypothesis = 'Odczyt VM 26H2 (2026-10-18): Get-NetConnectionProfile zwraca 0 obiektów bez błędu (karta Ethernet Disconnected). Akcja nie ma co zmienić, więc ma się skończyć Failed (kod 1) z komunikatem „brak aktywnego połączenia w profilu publicznym – nic nie zmieniono” (do raportu trafia stderr; asercja na fragmencie bez polskich znaków), a nie Executed bez efektu. Check dowodzi, że stan = baseline VM: nadal 0 profili, nic się nie pojawiło ani nie zmieniło (przypadek zakłada VM bez sieci).'
        }
        @{
            Name = 'net-network-private-change'; TweakId = 'net.network-private'; Flow = 'Execute'; Catalog = 'BuiltIn'; RestorePoint = 'skip'
            Skip = 'brak sieci na VM'
            Check = '@(Get-NetConnectionProfile | Where-Object { [string]$_.NetworkCategory -eq ''Public'' -and ([string]$_.IPv4Connectivity -ne ''Disconnected'' -or [string]$_.IPv6Connectivity -ne ''Disconnected'') }).Count -eq 0'
            Cleanup = 'Get-NetConnectionProfile | Where-Object { [string]$_.NetworkCategory -eq ''Private'' } | Set-NetConnectionProfile -NetworkCategory Public'
            Expect = @{ ExecuteExit = 0; ExecuteOutcome = 'Executed' }
            Hypothesis = 'Realna zmiana aktywnego profilu Public na Private wymaga sieci, a VM jej nie ma (karta Disconnected, 0 profili), więc przypadek jest pominięty z powodem i nie zalicza się jako zaliczony. Gdyby VM dostała sieć: Check = brak aktywnego profilu Public, Cleanup przywraca Public na profilach Private (zakłada, że wszystkie były Public przed akcją).'
        }
        @{
            Name = 'panels-visual-effects'; TweakId = 'panels.visual-effects'; Flow = 'Execute'; Catalog = 'BuiltIn'; RestorePoint = 'skip'
            Check = '$deadline = (Get-Date).AddSeconds(15); do { $found = @(Get-CimInstance -ClassName Win32_Process -Filter "Name=''SystemPropertiesPerformance.exe''").Count -gt 0; if (-not $found) { Start-Sleep -Milliseconds 500 } } while (-not $found -and (Get-Date) -lt $deadline); $found'
            Cleanup = 'Get-CimInstance -ClassName Win32_Process -Filter "Name=''SystemPropertiesPerformance.exe''" | ForEach-Object { Stop-Process -Id $_.ProcessId -Force -ErrorAction SilentlyContinue }'
            Expect = @{ ExecuteExit = 0; ExecuteOutcome = 'Executed' }
            Hypothesis = 'Odczyt VM 26H2 Home i Pro (eksploracja C5, 2026-10-19). SystemPropertiesPerformance.exe żyje po 10 s w K1 (okno „Opcje wydajności” w K2). Check odpytuje proces w pętli (15 s co 0,5 s), bo stan VM po starcie checkpointu bywa niedeterministyczny; sam start procesu nie dowodzi okna (okno ocenia smoke Tasku 9 w K2). Cleanup zamyka tylko procesy pasujące do Check.'
        }
        @{
            Name = 'panels-power-options'; TweakId = 'panels.power-options'; Flow = 'Execute'; Catalog = 'BuiltIn'; RestorePoint = 'skip'
            Check = '$deadline = (Get-Date).AddSeconds(15); do { $found = @(Get-CimInstance -ClassName Win32_Process -Filter "Name=''control.exe''" | Where-Object { $_.CommandLine -like ''*Microsoft.PowerOptions*'' }).Count -gt 0; if (-not $found) { Start-Sleep -Milliseconds 500 } } while (-not $found -and (Get-Date) -lt $deadline); $found'
            Cleanup = 'Get-CimInstance -ClassName Win32_Process -Filter "Name=''control.exe''" | Where-Object { $_.CommandLine -like ''*Microsoft.PowerOptions*'' } | ForEach-Object { Stop-Process -Id $_.ProcessId -Force -ErrorAction SilentlyContinue }'
            Expect = @{ ExecuteExit = 0; ExecuteOutcome = 'Executed' }
            Hypothesis = 'Odczyt VM 26H2 Home i Pro (eksploracja C5, 2026-10-19). W K1 żyje control.exe z linią /name Microsoft.PowerOptions (w K2 control.exe kończy się kodem 1, okno „Opcje zasilania” jest w explorer.exe). Check odpytuje proces w pętli (15 s co 0,5 s), bo stan VM po starcie checkpointu bywa niedeterministyczny; sam start procesu nie dowodzi okna (okno ocenia smoke Tasku 9 w K2). Cleanup zamyka tylko procesy pasujące do Check.'
        }
        @{
            Name = 'panels-network-connections'; TweakId = 'panels.network-connections'; Flow = 'Execute'; Catalog = 'BuiltIn'; RestorePoint = 'skip'
            Check = '$deadline = (Get-Date).AddSeconds(15); do { $found = @(Get-CimInstance -ClassName Win32_Process -Filter "Name=''control.exe''" | Where-Object { $_.CommandLine -like ''*netconnections*'' }).Count -gt 0; if (-not $found) { Start-Sleep -Milliseconds 500 } } while (-not $found -and (Get-Date) -lt $deadline); $found'
            Cleanup = 'Get-CimInstance -ClassName Win32_Process -Filter "Name=''control.exe''" | Where-Object { $_.CommandLine -like ''*netconnections*'' } | ForEach-Object { Stop-Process -Id $_.ProcessId -Force -ErrorAction SilentlyContinue }'
            Expect = @{ ExecuteExit = 0; ExecuteOutcome = 'Executed' }
            Hypothesis = 'Odczyt VM 26H2 Home i Pro (eksploracja C5, 2026-10-19). W K1 żyje control.exe z linią netconnections (okno „Połączenia sieciowe” w K2). Check odpytuje proces w pętli (15 s co 0,5 s), bo stan VM po starcie checkpointu bywa niedeterministyczny; sam start procesu nie dowodzi okna (okno ocenia smoke Tasku 9 w K2). Cleanup zamyka tylko procesy pasujące do Check.'
        }
        @{
            Name = 'panels-computer-management'; TweakId = 'panels.computer-management'; Flow = 'Execute'; Catalog = 'BuiltIn'; RestorePoint = 'skip'
            Check = '$deadline = (Get-Date).AddSeconds(15); do { $found = @(Get-CimInstance -ClassName Win32_Process -Filter "Name=''mmc.exe''" | Where-Object { $_.CommandLine -like ''*compmgmt.msc*'' }).Count -gt 0; if (-not $found) { Start-Sleep -Milliseconds 500 } } while (-not $found -and (Get-Date) -lt $deadline); $found'
            Cleanup = 'Get-CimInstance -ClassName Win32_Process -Filter "Name=''mmc.exe''" | Where-Object { $_.CommandLine -like ''*compmgmt.msc*'' } | ForEach-Object { Stop-Process -Id $_.ProcessId -Force -ErrorAction SilentlyContinue }'
            Expect = @{ ExecuteExit = 0; ExecuteOutcome = 'Executed' }
            Hypothesis = 'Odczyt VM 26H2 Home i Pro (eksploracja C5, 2026-10-19). W K1 żyje mmc.exe z compmgmt.msc (okno „Zarządzanie komputerem”, MMCMainFrame, w K2). Check odpytuje proces w pętli (15 s co 0,5 s), bo stan VM po starcie checkpointu bywa niedeterministyczny; sam start procesu nie dowodzi okna (okno ocenia smoke Tasku 9 w K2). Cleanup zamyka tylko procesy pasujące do Check.'
        }
        @{
            Name = 'panels-control-panel'; TweakId = 'panels.control-panel'; Flow = 'Execute'; Catalog = 'BuiltIn'; RestorePoint = 'skip'
            Check = '$deadline = (Get-Date).AddSeconds(15); do { $found = @(Get-CimInstance -ClassName Win32_Process -Filter "Name=''control.exe''" | Where-Object { $_.CommandLine -match ''(?i)control\.exe"?\s*$'' }).Count -gt 0; if (-not $found) { Start-Sleep -Milliseconds 500 } } while (-not $found -and (Get-Date) -lt $deadline); $found'
            Cleanup = 'Get-CimInstance -ClassName Win32_Process -Filter "Name=''control.exe''" | Where-Object { $_.CommandLine -match ''(?i)control\.exe"?\s*$'' } | ForEach-Object { Stop-Process -Id $_.ProcessId -Force -ErrorAction SilentlyContinue }'
            Expect = @{ ExecuteExit = 0; ExecuteOutcome = 'Executed' }
            Hypothesis = 'Odczyt VM 26H2 Home i Pro (eksploracja C5, 2026-10-19). W K1 żyje goły control.exe (bez argumentów); okno „Panel sterowania” w K2. Check odpytuje proces w pętli (15 s co 0,5 s), bo stan VM po starcie checkpointu bywa niedeterministyczny; sam start procesu nie dowodzi okna (okno ocenia smoke Tasku 9 w K2). Cleanup zamyka tylko procesy pasujące do Check.'
        }
        @{
            Name = 'panels-mouse'; TweakId = 'panels.mouse'; Flow = 'Execute'; Catalog = 'BuiltIn'; RestorePoint = 'skip'
            Check = '$deadline = (Get-Date).AddSeconds(15); do { $found = @(Get-CimInstance -ClassName Win32_Process -Filter "Name=''rundll32.exe''" | Where-Object { $_.CommandLine -like ''*main.cpl*'' }).Count -gt 0; if (-not $found) { Start-Sleep -Milliseconds 500 } } while (-not $found -and (Get-Date) -lt $deadline); $found'
            Cleanup = 'Get-CimInstance -ClassName Win32_Process -Filter "Name=''rundll32.exe''" | Where-Object { $_.CommandLine -like ''*main.cpl*'' } | ForEach-Object { Stop-Process -Id $_.ProcessId -Force -ErrorAction SilentlyContinue }'
            Expect = @{ ExecuteExit = 0; ExecuteOutcome = 'Executed' }
            Hypothesis = 'Odczyt VM 26H2 Home i Pro (eksploracja C5, 2026-10-19). W K1 żyje rundll32.exe z Control_RunDLL main.cpl (okno „Właściwości: Mysz” w K2). Check odpytuje proces w pętli (15 s co 0,5 s), bo stan VM po starcie checkpointu bywa niedeterministyczny; sam start procesu nie dowodzi okna (okno ocenia smoke Tasku 9 w K2). Cleanup zamyka tylko procesy pasujące do Check.'
        }
        @{
            Name = 'panels-programs-features'; TweakId = 'panels.programs-features'; Flow = 'Execute'; Catalog = 'BuiltIn'; RestorePoint = 'skip'
            Check = '$deadline = (Get-Date).AddSeconds(15); do { $found = @(Get-CimInstance -ClassName Win32_Process -Filter "Name=''control.exe''" | Where-Object { $_.CommandLine -like ''*Microsoft.ProgramsAndFeatures*'' }).Count -gt 0; if (-not $found) { Start-Sleep -Milliseconds 500 } } while (-not $found -and (Get-Date) -lt $deadline); $found'
            Cleanup = 'Get-CimInstance -ClassName Win32_Process -Filter "Name=''control.exe''" | Where-Object { $_.CommandLine -like ''*Microsoft.ProgramsAndFeatures*'' } | ForEach-Object { Stop-Process -Id $_.ProcessId -Force -ErrorAction SilentlyContinue }'
            Expect = @{ ExecuteExit = 0; ExecuteOutcome = 'Executed' }
            Hypothesis = 'Odczyt VM 26H2 Home i Pro (eksploracja C5, 2026-10-19). W K1 żyje control.exe z linią /name Microsoft.ProgramsAndFeatures (okno „Programy i funkcje” w K2). Check odpytuje proces w pętli (15 s co 0,5 s), bo stan VM po starcie checkpointu bywa niedeterministyczny; sam start procesu nie dowodzi okna (okno ocenia smoke Tasku 9 w K2). Cleanup zamyka tylko procesy pasujące do Check.'
        }
        @{
            Name = 'panels-region'; TweakId = 'panels.region'; Flow = 'Execute'; Catalog = 'BuiltIn'; RestorePoint = 'skip'
            Check = '$deadline = (Get-Date).AddSeconds(15); do { $found = @(Get-CimInstance -ClassName Win32_Process -Filter "Name=''rundll32.exe''" | Where-Object { $_.CommandLine -like ''*intl.cpl*'' }).Count -gt 0; if (-not $found) { Start-Sleep -Milliseconds 500 } } while (-not $found -and (Get-Date) -lt $deadline); $found'
            Cleanup = 'Get-CimInstance -ClassName Win32_Process -Filter "Name=''rundll32.exe''" | Where-Object { $_.CommandLine -like ''*intl.cpl*'' } | ForEach-Object { Stop-Process -Id $_.ProcessId -Force -ErrorAction SilentlyContinue }'
            Expect = @{ ExecuteExit = 0; ExecuteOutcome = 'Executed' }
            Hypothesis = 'Odczyt VM 26H2 Home i Pro (eksploracja C5, 2026-10-19). W K1 żyje rundll32.exe z Control_RunDLL intl.cpl (okno „Region” w K2). Check odpytuje proces w pętli (15 s co 0,5 s), bo stan VM po starcie checkpointu bywa niedeterministyczny; sam start procesu nie dowodzi okna (okno ocenia smoke Tasku 9 w K2). Cleanup zamyka tylko procesy pasujące do Check.'
        }
        @{
            Name = 'panels-security-maintenance'; TweakId = 'panels.security-maintenance'; Flow = 'Execute'; Catalog = 'BuiltIn'; RestorePoint = 'skip'
            Check = '$deadline = (Get-Date).AddSeconds(15); do { $found = @(Get-CimInstance -ClassName Win32_Process -Filter "Name=''rundll32.exe''" | Where-Object { $_.CommandLine -like ''*wscui.cpl*'' }).Count -gt 0; if (-not $found) { Start-Sleep -Milliseconds 500 } } while (-not $found -and (Get-Date) -lt $deadline); $found'
            Cleanup = 'Get-CimInstance -ClassName Win32_Process -Filter "Name=''rundll32.exe''" | Where-Object { $_.CommandLine -like ''*wscui.cpl*'' } | ForEach-Object { Stop-Process -Id $_.ProcessId -Force -ErrorAction SilentlyContinue }'
            Expect = @{ ExecuteExit = 0; ExecuteOutcome = 'Executed' }
            Hypothesis = 'Odczyt VM 26H2 Home i Pro (eksploracja C5, 2026-10-19). W K1 żyje rundll32.exe z Control_RunDLL wscui.cpl (okno „Zabezpieczenia i konserwacja” w K2). Check odpytuje proces w pętli (15 s co 0,5 s), bo stan VM po starcie checkpointu bywa niedeterministyczny; sam start procesu nie dowodzi okna (okno ocenia smoke Tasku 9 w K2). Cleanup zamyka tylko procesy pasujące do Check.'
        }
        @{
            Name = 'panels-sound'; TweakId = 'panels.sound'; Flow = 'Execute'; Catalog = 'BuiltIn'; RestorePoint = 'skip'
            Check = '$deadline = (Get-Date).AddSeconds(15); do { $found = @(Get-CimInstance -ClassName Win32_Process -Filter "Name=''rundll32.exe''" | Where-Object { $_.CommandLine -like ''*mmsys.cpl*'' }).Count -gt 0; if (-not $found) { Start-Sleep -Milliseconds 500 } } while (-not $found -and (Get-Date) -lt $deadline); $found'
            Cleanup = 'Get-CimInstance -ClassName Win32_Process -Filter "Name=''rundll32.exe''" | Where-Object { $_.CommandLine -like ''*mmsys.cpl*'' } | ForEach-Object { Stop-Process -Id $_.ProcessId -Force -ErrorAction SilentlyContinue }'
            Expect = @{ ExecuteExit = 0; ExecuteOutcome = 'Executed' }
            Hypothesis = 'Odczyt VM 26H2 Home i Pro (eksploracja C5, 2026-10-19). W K1 żyje rundll32.exe z Control_RunDLL mmsys.cpl (okno „Dźwięk” w K2; ręczne Win+R Dawida bez odpowiedzi - do potwierdzenia w smoke Tasku 9). Check odpytuje proces w pętli (15 s co 0,5 s), bo stan VM po starcie checkpointu bywa niedeterministyczny; sam start procesu nie dowodzi okna (okno ocenia smoke Tasku 9 w K2). Cleanup zamyka tylko procesy pasujące do Check.'
        }
        @{
            Name = 'panels-system-properties'; TweakId = 'panels.system-properties'; Flow = 'Execute'; Catalog = 'BuiltIn'; RestorePoint = 'skip'
            Check = '$deadline = (Get-Date).AddSeconds(15); do { $found = @(Get-CimInstance -ClassName Win32_Process -Filter "Name=''SystemPropertiesComputerName.exe''").Count -gt 0; if (-not $found) { Start-Sleep -Milliseconds 500 } } while (-not $found -and (Get-Date) -lt $deadline); $found'
            Setup = @('Get-CimInstance -ClassName Win32_Process -Filter "Name LIKE ''SystemProperties%''" | ForEach-Object { Stop-Process -Id $_.ProcessId -Force -ErrorAction SilentlyContinue }')
            Cleanup = 'Get-CimInstance -ClassName Win32_Process -Filter "Name=''SystemPropertiesComputerName.exe''" | ForEach-Object { Stop-Process -Id $_.ProcessId -Force -ErrorAction SilentlyContinue }'
            Expect = @{ ExecuteExit = 0; ExecuteOutcome = 'Executed' }
            Hypothesis = 'Odczyt VM 26H2 Home i Pro (eksploracja C5, 2026-10-19). W K1 żyje SystemPropertiesComputerName.exe (okno „Właściwości systemu” w K2). Okno jednoinstancyjne (hipoteza), więc Setup zamyka przed startem wszystkie SystemProperties*.exe (żeby stary proces nie zaliczył Check ani nie zablokował nowego okna), a Cleanup zamyka tylko SystemPropertiesComputerName.exe, czyli proces z Check. Check odpytuje proces w pętli (15 s co 0,5 s), bo stan VM po starcie checkpointu bywa niedeterministyczny; sam start procesu nie dowodzi okna (okno ocenia smoke Tasku 9 w K2). Cleanup zamyka tylko procesy pasujące do Check.'
        }
        @{
            Name = 'panels-date-time'; TweakId = 'panels.date-time'; Flow = 'Execute'; Catalog = 'BuiltIn'; RestorePoint = 'skip'
            Check = '$deadline = (Get-Date).AddSeconds(15); do { $found = @(Get-CimInstance -ClassName Win32_Process -Filter "Name=''rundll32.exe''" | Where-Object { $_.CommandLine -like ''*timedate.cpl*'' }).Count -gt 0; if (-not $found) { Start-Sleep -Milliseconds 500 } } while (-not $found -and (Get-Date) -lt $deadline); $found'
            Cleanup = 'Get-CimInstance -ClassName Win32_Process -Filter "Name=''rundll32.exe''" | Where-Object { $_.CommandLine -like ''*timedate.cpl*'' } | ForEach-Object { Stop-Process -Id $_.ProcessId -Force -ErrorAction SilentlyContinue }'
            Expect = @{ ExecuteExit = 0; ExecuteOutcome = 'Executed' }
            Hypothesis = 'Odczyt VM 26H2 Home i Pro (eksploracja C5, 2026-10-19). W K1 żyje rundll32.exe z Control_RunDLL timedate.cpl (okno „Data i godzina” w K2). Check odpytuje proces w pętli (15 s co 0,5 s), bo stan VM po starcie checkpointu bywa niedeterministyczny; sam start procesu nie dowodzi okna (okno ocenia smoke Tasku 9 w K2). Cleanup zamyka tylko procesy pasujące do Check.'
        }
        @{
            Name = 'panels-firewall'; TweakId = 'panels.firewall'; Flow = 'Execute'; Catalog = 'BuiltIn'; RestorePoint = 'skip'
            Check = '$deadline = (Get-Date).AddSeconds(15); do { $found = @(Get-CimInstance -ClassName Win32_Process -Filter "Name=''control.exe''" | Where-Object { $_.CommandLine -like ''*Microsoft.WindowsFirewall*'' }).Count -gt 0; if (-not $found) { Start-Sleep -Milliseconds 500 } } while (-not $found -and (Get-Date) -lt $deadline); $found'
            Cleanup = 'Get-CimInstance -ClassName Win32_Process -Filter "Name=''control.exe''" | Where-Object { $_.CommandLine -like ''*Microsoft.WindowsFirewall*'' } | ForEach-Object { Stop-Process -Id $_.ProcessId -Force -ErrorAction SilentlyContinue }'
            Expect = @{ ExecuteExit = 0; ExecuteOutcome = 'Executed' }
            Hypothesis = 'Odczyt VM 26H2 Home i Pro (eksploracja C5, 2026-10-19). W K1 żyje control.exe z linią /name Microsoft.WindowsFirewall (okno „Zapora Windows Defender” w K2). Check odpytuje proces w pętli (15 s co 0,5 s), bo stan VM po starcie checkpointu bywa niedeterministyczny; sam start procesu nie dowodzi okna (okno ocenia smoke Tasku 9 w K2). Cleanup zamyka tylko procesy pasujące do Check.'
        }
        @{
            Name = 'panels-system-restore'; TweakId = 'panels.system-restore'; Flow = 'Execute'; Catalog = 'BuiltIn'; RestorePoint = 'skip'
            Check = '$deadline = (Get-Date).AddSeconds(15); do { $found = @(Get-CimInstance -ClassName Win32_Process -Filter "Name=''rstrui.exe''").Count -gt 0; if (-not $found) { Start-Sleep -Milliseconds 500 } } while (-not $found -and (Get-Date) -lt $deadline); $found'
            Cleanup = 'Get-CimInstance -ClassName Win32_Process -Filter "Name=''rstrui.exe''" | ForEach-Object { Stop-Process -Id $_.ProcessId -Force -ErrorAction SilentlyContinue }'
            Expect = @{ ExecuteExit = 0; ExecuteOutcome = 'Executed' }
            Hypothesis = 'Odczyt VM 26H2 Home i Pro (eksploracja C5, 2026-10-19). W K1 żyje rstrui.exe (okno „Przywracanie systemu” w K2). Skutek uboczny zmierzony na VM: start usługi wbengine.exe (zostaje do revertu checkpointu). Check odpytuje proces w pętli (15 s co 0,5 s), bo stan VM po starcie checkpointu bywa niedeterministyczny; sam start procesu nie dowodzi okna (okno ocenia smoke Tasku 9 w K2). Cleanup zamyka tylko procesy pasujące do Check.'
        }
        @{
            Name = 'panels-devices-printers-no-shell'; TweakId = 'panels.devices-printers'; Flow = 'Execute'; Catalog = 'BuiltIn'; RestorePoint = 'skip'
            Check = 'Start-Sleep -Seconds 3; @(Get-CimInstance -ClassName Win32_Process -Filter "Name=''explorer.exe''" | Where-Object { $_.CommandLine -like ''*shell:::{A8A91A66*'' }).Count -eq 0'
            Cleanup = 'Get-CimInstance -ClassName Win32_Process -Filter "Name=''explorer.exe''" | Where-Object { $_.CommandLine -like ''*shell:::{A8A91A66*'' } | ForEach-Object { Stop-Process -Id $_.ProcessId -Force -ErrorAction SilentlyContinue }'
            Expect = @{ ExecuteExit = 1; ExecuteOutcome = 'Failed'; MessageContains = 'Brak Eksploratora tego konta' }
            Hypothesis = 'Eksploracja C5 (2026-10-19, sonda uzupełniająca): z PS Direct jako Administrator (sesja 0) nie ma Eksploratora tego konta, a explorer.exe uruchomiony bez strażnika zostaje osobnym, podniesionym procesem. Strażnik R23 ma odmówić (Failed, kod 1) i niczego nie uruchomić; Check czeka 3 s i wymaga braku explorer.exe z linią poleceń shell:::{A8A91A66...} (to dokładnie ten proces, który powstałby przy błędzie), Cleanup zamyka tylko taki proces i nigdy powłoki. Okno „Urządzenia i drukarki” w K2 sprawdza smoke Tasku 9.'
        }
        @{
            Name = 'tools-restart-explorer'; TweakId = 'tools.restart-explorer'; Flow = 'Execute'; Catalog = 'BuiltIn'; RestorePoint = 'skip'
            Setup = @('New-Item -ItemType Directory -Force -Path C:\dso | Out-Null; $shell = @(Get-CimInstance -ClassName Win32_Process -Filter "Name=''explorer.exe''" | Where-Object { $_.CommandLine -notlike ''*/factory*'' -and (Invoke-CimMethod -InputObject $_ -MethodName GetOwnerSid -ErrorAction SilentlyContinue).Sid -eq ''{UserSid}'' }); if ($shell.Count -ne 1) { throw "Oczekiwano jednej powłoki użytkownika dso, jest $($shell.Count)" }; Set-Content -Path C:\dso\restart-explorer-pid.txt -Value ("$($shell[0].ProcessId) $($shell[0].SessionId)") -Encoding ascii')
            Check = '$saved = (Get-Content -Path C:\dso\restart-explorer-pid.txt).Split('' ''); $oldPid = [int]$saved[0]; $oldSession = [int]$saved[1]; $deadline = (Get-Date).AddSeconds(15); do { $found = @(Get-CimInstance -ClassName Win32_Process -Filter "Name=''explorer.exe''" | Where-Object { $_.CommandLine -notlike ''*/factory*'' -and $_.ProcessId -ne $oldPid -and $_.SessionId -eq $oldSession -and (Invoke-CimMethod -InputObject $_ -MethodName GetOwnerSid -ErrorAction SilentlyContinue).Sid -eq ''{UserSid}'' }).Count -gt 0; if (-not $found) { Start-Sleep -Milliseconds 500 } } while (-not $found -and (Get-Date) -lt $deadline); $found'
            Cleanup = 'Remove-Item -Path C:\dso\restart-explorer-pid.txt -Force -ErrorAction SilentlyContinue'
            Expect = @{ ExecuteExit = 0; ExecuteOutcome = 'Executed' }
            Hypothesis = 'Odczyt VM 26H2 Home i Pro (eksploracja C5, 2026-10-19, ruling R18): Stop-Process -Force na powłoce dso -> nowa powłoka (rodzic userinit.exe, sesja 1, token Limited) w maks. 1,42 s; AutoRestartShell = 1. Setup zapisuje PID jedynej powłoki dso (bez /factory) do C:\dso\restart-explorer-pid.txt, Check w pętli (15 s co 0,5 s, bo stan VM po starcie checkpointu bywa niedeterministyczny) szuka powłoki dso o innym PID. K1 (PS Direct, Administrator) widzi powłokę dso przez Win32_Process i GetOwnerSid. Obserwacja wzrokowa paska zadań (K2) nie ma osobnego przypadku: runner nie widzi pulpitu, to smoke Tasku 9 (wynik trybu ręcznego w docs/tweaks/tools.restart-explorer.md). Cleanup usuwa tylko plik PID; powłoka zostaje wznowiona przez winlogon.'
        }
        @{
            Name = 'repair-store-other-account'; TweakId = 'repair.store'; Flow = 'Execute'; Catalog = 'BuiltIn'; RestorePoint = 'skip'
            Setup = @('New-Item -ItemType Directory -Force -Path C:\dso | Out-Null; $lines = @(Get-AppxPackage -AllUsers -Name Microsoft.WindowsStore | ForEach-Object { $n = $_.PackageFullName; $_.PackageUserInformation | ForEach-Object { "$n|$($_.UserSecurityId.Sid)|$($_.InstallState)" } }); if ($lines.Count -eq 0) { throw "Brak pakietu Microsoft.WindowsStore w odczycie -AllUsers" }; if (@($lines | Where-Object { $_ -like ''*|S-1-5-21-*'' }).Count -eq 0) { throw "Żadna linia odczytu -AllUsers nie ma kształtu |S-1-5-21-: nieoczekiwany kształt UserSecurityId.Sid" }; Set-Content -Path C:\dso\store-repair-before.txt -Value ($lines | Sort-Object) -Encoding ascii; $loc = (Get-AppxPackage -AllUsers -Name Microsoft.WindowsStore | Select-Object -First 1).InstallLocation; try { $first = @(Get-Content -LiteralPath (Join-Path $loc ''AppxManifest.xml'') -TotalCount 1 -ErrorAction Stop); $note = "OK: odczyt manifestu jako Administrator ($loc), linii: $($first.Count)" } catch { $note = "WYJĄTEK: $($_.Exception.GetType().FullName): $($_.Exception.Message)" }; Set-Content -Path C:\dso\store-repair-manifest-read.txt -Value $note -Encoding utf8')
            Check = '$saved = @(Get-Content -Path C:\dso\store-repair-before.txt); $now = @(Get-AppxPackage -AllUsers -Name Microsoft.WindowsStore | ForEach-Object { $n = $_.PackageFullName; $_.PackageUserInformation | ForEach-Object { "$n|$($_.UserSecurityId.Sid)|$($_.InstallState)" } }) | Sort-Object; $saved.Count -gt 0 -and (($saved -join '';'') -eq ($now -join '';''))'
            Cleanup = 'Remove-Item -Path C:\dso\store-repair-before.txt, C:\dso\store-repair-manifest-read.txt -Force -ErrorAction SilentlyContinue'
            Expect = @{ ExecuteExit = 1; ExecuteOutcome = 'Failed'; MessageContains = 'uruchom DSOptimizer na koncie' }
            Hypothesis = 'Odczyt VM 26H2 Home i Pro (eksploracja C5, 2026-10-19, ruling R19): rejestracja pakietu Microsoft.WindowsStore z PS Direct (K1, Administrator) trafia do konta Administratora, a wpis konta dso zostaje bez zmian. Runner działa jako Administrator, a $UserSid to konto dso, więc akcja ma odmówić (Failed, kod 1) BEZ rejestracji. Setup zapisuje odczyt Get-AppxPackage -AllUsers (PackageFullName, SID, InstallState) do C:\dso\store-repair-before.txt, Check wymaga identycznego odczytu po akcji (w szczególności brak nowego wpisu Administratora). Cleanup usuwa tylko pliki. Setup (R28) rzuca, gdy żadna zapisana linia nie ma kształtu |S-1-5-21- (weryfikacja kształtu UserSecurityId.Sid na prawdziwej VM), i zapisuje obok wynik tylko-odczytu Get-Content -TotalCount 1 manifestu Store jako Administrator (C:\dso\store-repair-manifest-read.txt: OK albo wyjątek) – dana do raportu, bez wpływu na wynik przypadku.'
        }
        @{
            Name = 'repair-store-register-dso'; TweakId = 'repair.store'; Flow = 'Execute'; Catalog = 'BuiltIn'; RestorePoint = 'skip'; RunAs = 'dso'
            Setup = @('$ex = @(Get-CimInstance -ClassName Win32_Process -Filter "Name=''explorer.exe''" | Sort-Object CreationDate | Select-Object -First 1); if ($ex.Count -eq 0) { throw "Brak explorer.exe: konto dso niezalogowane" }; $wait = 120 - ((Get-Date) - $ex[0].CreationDate).TotalSeconds; if ($wait -gt 0) { Start-Sleep -Seconds ([int][Math]::Ceiling($wait)) }')
            Check = '$sid = (New-Object System.Security.Principal.NTAccount(''dso'')).Translate([System.Security.Principal.SecurityIdentifier]).Value; @(Get-AppxPackage -AllUsers -Name Microsoft.WindowsStore | Where-Object { $_.Status -eq ''Ok'' -and @($_.PackageUserInformation | Where-Object { $_.UserSecurityId.Sid -eq $sid -and "$($_.InstallState)" -eq ''Installed'' }).Count -gt 0 }).Count -eq 1'
            Expect = @{ ExecuteExit = 0; ExecuteOutcome = 'Executed'; MessageContains = 'zarejestrowany ponownie' }
            Hypothesis = 'Poprzednio pominięty (tools-store-repair-register-dso: runner nie umiał RunAs, a Task 8 dodał RunAs=dso do Flow=Execute). Teraz aktywny: CLI jako dso (zadanie Harmonogramu Interactive + Highest, SID procesu = zalogowany) w stanie wyjściowym Installed rejestruje pakiet ponownie (ścieżka Installed, bez zmian względem C5; K2 2026-10-19: Ok w ok. 2 s). Komunikat „zarejestrowany ponownie” odróżnia ją od ścieżki R33 (repair-store-staged-dso). Task 10 (VM Home i Pro, 2026-10-22): zaraz po zalogowaniu dso Add-AppxPackage -Register zwraca 0x80073D02 („aplikacje trzeba zamknąć: Microsoft.WindowsStore”, AppXDeploymentServer 401/404/419) – Pro: porażka po 7, 33 i 60 s od startu explorer.exe, sukces po 80 s; Home: porażka po 12 s, sukces po 76 s; bez widocznego procesu z pakietu Sklepu. Setup czeka więc, aż od startu explorer.exe konta dso minie 120 s (stan systemu po logowaniu, nie błąd naprawy; komunikat naprawy przy 0x80073D02 każe zamknąć Sklep i spróbować ponownie). Check to niezależny odczyt Get-AppxPackage -AllUsers: dokładnie jeden pakiet Microsoft.WindowsStore ze Status Ok i InstallState Installed dla SID konta dso. Nie dowodzi działania Sklepu (VM bez sieci).'
        }
        @{
            Name = 'repair-store-staged-dso'; TweakId = 'repair.store'; Flow = 'Execute'; Catalog = 'BuiltIn'; RestorePoint = 'skip'; RunAs = 'dso'
            Setup = @(
                'New-Item -ItemType Directory -Force -Path C:\dso | Out-Null; Set-Content -LiteralPath C:\dso\store-remove.ps1 -Value ''$r = try { $p = Get-AppxPackage -Name Microsoft.WindowsStore | Select-Object -First 1; Remove-AppxPackage -Package $p.PackageFullName -ErrorAction Stop; "OK" } catch { "ERR " + $_.Exception.Message }; Set-Content -LiteralPath C:\dso\store-remove-result.txt -Value $r'' -Encoding ascii'
                '$ErrorActionPreference = ''Stop''; $n = ''DSO-StoreRemove''; Remove-Item -LiteralPath C:\dso\store-remove-result.txt -Force -ErrorAction SilentlyContinue; $a = New-ScheduledTaskAction -Execute ''powershell.exe'' -Argument ''-NoProfile -NonInteractive -ExecutionPolicy Bypass -File C:\dso\store-remove.ps1''; $p = New-ScheduledTaskPrincipal -UserId ''dso'' -LogonType Interactive -RunLevel Highest; try { Register-ScheduledTask -TaskName $n -Action $a -Principal $p -Force | Out-Null; Start-ScheduledTask -TaskName $n; $w = [System.Diagnostics.Stopwatch]::StartNew(); while ((Get-ScheduledTask -TaskName $n).State -eq ''Running'' -or (Get-ScheduledTaskInfo -TaskName $n).LastTaskResult -eq 267011) { if ($w.Elapsed.TotalSeconds -gt 60) { throw ''Remove-AppxPackage jako dso: przekroczono 60 s'' }; Start-Sleep -Milliseconds 500 } } finally { Unregister-ScheduledTask -TaskName $n -Confirm:$false -ErrorAction SilentlyContinue }; $res = (Get-Content -LiteralPath C:\dso\store-remove-result.txt -Raw).Trim(); if ($res -ne ''OK'') { throw "Remove-AppxPackage jako dso: $res" }'
                '$sid = (New-Object System.Security.Principal.NTAccount(''dso'')).Translate([System.Security.Principal.SecurityIdentifier]).Value; $pk = @(Get-AppxPackage -AllUsers -Name Microsoft.WindowsStore); if ($pk.Count -eq 0) { throw "Pakiet Microsoft.WindowsStore zniknął z dysku po Remove-AppxPackage: stan Staged nieosiągnięty" }; $inst = @($pk | ForEach-Object { $_.PackageUserInformation } | Where-Object { $_.UserSecurityId.Sid -eq $sid -and "$($_.InstallState)" -eq ''Installed'' }); if ($inst.Count -gt 0) { throw "Wpis dso nadal Installed po Remove-AppxPackage: stan wyjściowy nie jest Staged" }'
            )
            Check = '$sid = (New-Object System.Security.Principal.NTAccount(''dso'')).Translate([System.Security.Principal.SecurityIdentifier]).Value; @(Get-AppxPackage -AllUsers -Name Microsoft.WindowsStore | Where-Object { $_.Status -eq ''Ok'' -and @($_.PackageUserInformation | Where-Object { $_.UserSecurityId.Sid -eq $sid -and "$($_.InstallState)" -eq ''Installed'' }).Count -gt 0 }).Count -eq 1'
            Cleanup = 'Remove-Item -Path C:\dso\store-remove.ps1, C:\dso\store-remove-result.txt -Force -ErrorAction SilentlyContinue; Unregister-ScheduledTask -TaskName DSO-StoreRemove -Confirm:$false -ErrorAction SilentlyContinue'
            Expect = @{ ExecuteExit = 0; ExecuteOutcome = 'Executed'; MessageContains = 'był na dysku' }
            Hypothesis = 'R33, pomiar VM 26H2 Home i Pro (4b partia 1, 2026-10-22, sekcja 5): po Remove-AppxPackage -Package <PackageFullName Sklepu> w procesie dso (zadanie Harmonogramu Interactive + Highest) Get-AppxPackage -AllUsers pokazuje pakiet tylko dla S-1-5-18 = Staged, bez wpisu SID dso, a obraz nadal ma Sklep. Setup tak przygotowuje stan i rzuca, gdy pakiet zniknął z dysku albo wpis dso dalej jest Installed. CLI jako dso (SID procesu = zalogowany) rejestruje pakiet z manifestu (-DisableDevelopmentMode) i potwierdza odczytem zwrotnym; komunikat zawiera „był na dysku”. Check to niezależny odczyt: dokładnie jeden pakiet Microsoft.WindowsStore ze Status Ok i InstallState Installed dla SID dso. Cleanup usuwa pliki pomocnicze (stan Sklepu przywraca sama naprawa). Działania Sklepu nie sprawdza (VM bez sieci).'
        }
        @{
            Name = 'tools-winutil-offline'; TweakId = 'tools.winutil'; Flow = 'Execute'; Catalog = 'BuiltIn'; RestorePoint = 'skip'
            Setup = @('$ErrorActionPreference = ''Stop''; $profiles = @(Get-NetConnectionProfile -ErrorAction Stop); if ($profiles.Count -gt 0) { throw "VM ma profil sieci ($($profiles.Count)): handler pobrałby i uruchomił prawdziwy WinUtil z GitHuba; przypadek przerwany" }; $routes = @(Get-NetRoute -ErrorAction Stop | Where-Object { $_.DestinationPrefix -in ''0.0.0.0/0'', ''::/0'' }); if ($routes.Count -gt 0) { throw "VM ma trasę domyślną ($($routes.Count)): handler pobrałby i uruchomił prawdziwy WinUtil z GitHuba; przypadek przerwany" }; $old = @(Get-CimInstance -ClassName Win32_Process | Where-Object { $_.Name -eq ''powershell.exe'' -and $_.CommandLine -like ''*-File*C:\ProgramData\DSOptimizer\downloads\*.ps1*'' }); if ($old.Count -gt 0) { throw "Przed startem już działa powershell.exe ze skryptem z downloads ($($old.Count)); przypadek przerwany" }; $d = ''C:\ProgramData\DSOptimizer\downloads''; if ((Test-Path -LiteralPath $d) -and @(Get-ChildItem -LiteralPath $d -File -Filter ''*.ps1'' -Force).Count -gt 0) { throw "Przed startem w $d są już pliki .ps1: Check nie odróżniłby ich od nowych; przypadek przerwany" }')
            Check = '$p = @(Get-CimInstance -ClassName Win32_Process -Filter "Name=''powershell.exe''" | Where-Object { $_.CommandLine -like ''*-File*C:\ProgramData\DSOptimizer\downloads\*.ps1*'' }); $d = ''C:\ProgramData\DSOptimizer\downloads''; $files = if (Test-Path -LiteralPath $d) { @(Get-ChildItem -LiteralPath $d -File -Filter ''*.ps1'' -Force) } else { @() }; ($p.Count -eq 0) -and ($files.Count -eq 0)'
            Cleanup = '@(Get-CimInstance -ClassName Win32_Process -Filter "Name=''powershell.exe''" | Where-Object { $_.CommandLine -like ''*-File*C:\ProgramData\DSOptimizer\downloads\*.ps1*'' }) | ForEach-Object { Stop-Process -Id $_.ProcessId -Force -ErrorAction SilentlyContinue }'
            Expect = @{ ExecuteExit = 1; ExecuteOutcome = 'Failed'; MessageContains = 'brak połączenia z GitHubem' }
            Hypothesis = 'Etap 5, Task 4: tools.winutil to handler winutil (API releases/latest ChrisTitusTech/winutil → winutil.ps1 do C:\ProgramData\DSOptimizer\downloads → SHA256 = digest → powershell.exe -File). Na VM bez sieci GetText API kończy się Network/TimedOut, więc handler zwraca Failed „Nie wykonano – brak połączenia z GitHubem.” (kod 1) przed utworzeniem ścieżki pobrania: nic nie startuje i nic nie zostaje na dysku. Setup PRZERYWA (throw, fail-closed jak dawny tools-winutil-launch), gdy VM ma profil sieci albo trasę domyślną (inaczej handler pobrałby i uruchomił prawdziwy kod WinUtil jako admin), gdy już działa powershell.exe z -File na skrypcie z downloads albo gdy w downloads są już pliki .ps1. Check (niezależny odczyt w VM): brak procesu powershell.exe z -File wskazującym C:\ProgramData\DSOptimizer\downloads\*.ps1 i brak plików *.ps1 w downloads (folder nieobecny też przechodzi). Cleanup na wypadek regresji zamyka takie procesy; reszta wraca z checkpointem czysta-26H2.'
        }
        @{
            Name = 'tools-winutil-effect'; TweakId = 'tools.winutil'; Flow = 'Execute'; Catalog = 'BuiltIn'; RestorePoint = 'skip'
            Skip = 'skutek z siecią pokrywa faza av eksploracji E5 (Invoke-E5Exploration.ps1); zwykły przebieg VM jest bez sieci'
            Check = '$false'
            Expect = @{ ExecuteExit = 0; ExecuteOutcome = 'Executed' }
            Hypothesis = 'Skutek WinUtil z siecią (pobranie winutil.ps1 z wydania GitHuba, SHA256 = digest, okno WinUtil) wymaga sieci, a zwykły przebieg VM jej nie ma (tools-winutil-offline przerywa się przy profilu sieci). Okno i GUI WinUtil z siecią zmierzone w Task 1 Etapu 5 (wariant B, docs/zrodla/2026-10-26-etap5-pomiar-winutil.md), a --execute tools.winutil z siecią sprawdza faza av eksploracji E5. Rekord pominięcia ma to zostawić w raporcie zamiast udawać zaliczenie.'
        }
        @{
            Name = 'repair-dns-flush'; TweakId = 'repair.dns-flush'; Flow = 'Execute'; Catalog = 'BuiltIn'; RestorePoint = 'skip'
            Setup = @('$h = "$env:SystemRoot\System32\drivers\etc\hosts"; Add-Content -LiteralPath $h -Value @('''', ''10.123.45.67 dso-test-hosts.invalid'') -Encoding ascii; Resolve-DnsName -Name dso-test-hosts.invalid -ErrorAction Stop | Out-Null; Resolve-DnsName -Name dso-test-brak.invalid -ErrorAction SilentlyContinue | Out-Null; if (@(Get-DnsClientCache | Where-Object { ([string]$_.Entry) -like ''dso-test-hosts.invalid*'' }).Count -eq 0) { throw "Wpis z hosts nie trafił do pamięci DNS: pamięć przed akcją byłaby pusta" }')
            Check = '$names = @{}; foreach ($l in (Get-Content -LiteralPath "$env:SystemRoot\System32\drivers\etc\hosts")) { $t = ([string]$l -split ''#'', 2)[0].Trim(); if ($t) { $p = @($t -split ''\s+''); for ($i = 1; $i -lt $p.Count; $i++) { $names[$p[$i].TrimEnd(''.'').ToLowerInvariant()] = $true } } }; $left = @(Get-DnsClientCache | Where-Object { $n = ([string]$_.Entry).Trim().TrimEnd(''.'').ToLowerInvariant(); -not ($n -like ''*.in-addr.arpa'' -or $n -like ''*.ip6.arpa'' -or $names.ContainsKey($n)) }); $left.Count -eq 0'
            Cleanup = '$h = "$env:SystemRoot\System32\drivers\etc\hosts"; $kept = [string[]]@([System.IO.File]::ReadAllLines($h) | Where-Object { $_ -notmatch ''dso-test-hosts\.invalid'' }); $ok = $false; for ($i = 0; $i -lt 20 -and -not $ok; $i++) { try { [System.IO.File]::WriteAllLines($h, $kept, (New-Object System.Text.ASCIIEncoding)); $ok = $true } catch { Start-Sleep -Milliseconds 250 } }; if (-not $ok) { throw "Nie udało się usunąć wpisu testowego z hosts (plik zajęty)" }'
            Expect = @{ ExecuteExit = 0; ExecuteOutcome = 'Executed'; MessageContains = 'Wyczyszczono pamięć DNS' }
            Hypothesis = 'Odczyt VM 26H2 Home i Pro (pomiary 4b partia 1, 2026-10-22): Clear-DnsClientCache zostawia w Get-DnsClientCache tylko wpisy z pliku hosts (A, PTR, negatywny AAAA), pusta pamięć też jest dobra; spadek liczby wpisów nie jest kryterium. Bez sieci Windows nie trzyma w pamięci zwykłych odpowiedzi (Task 10: same zapytania localhost/.invalid dawały „przed: 0 wpisów”), więc Setup dopisuje do hosts wpis testowy 10.123.45.67 dso-test-hosts.invalid (jak w pomiarze), rozwiązuje go i rzuca, gdy wpis nie trafił do pamięci – akcja dostaje niepustą pamięć z wpisem, który ma zostać. Check to niezależny odczyt w VM: ten sam test co w skrypcie akcji (każdy wpis dotyczy nazwy z hosts albo *.in-addr.arpa/*.ip6.arpa), napisany od zera, nie kopia skryptu katalogu. Cleanup usuwa linię testową z hosts (do 20 prób co 250 ms: na Pro Set-Content trafił na plik czytany przez klienta DNS – „Nie można odczytać strumienia”); reszta wraca z checkpointem czysta-26H2.'
        }
        @{
            Name = 'repair-network-reset'; TweakId = 'repair.network-reset'; Flow = 'Execute'; Catalog = 'BuiltIn'; RestorePoint = 'skip'
            Check = '$o = @(netsh winsock show catalog); ($LASTEXITCODE -eq 0) -and ($o.Count -gt 0)'
            Expect = @{ ExecuteExit = 0; ExecuteOutcome = 'Executed'; RequiresReboot = $true }
            Hypothesis = 'Odczyt VM 26H2 Home i Pro (pomiary 4b partia 1, 2026-10-22): netsh winsock reset kod 0; netsh int ip reset kod 1 z jedną odmową dostępu (chroniony klucz Nsi) i resztą OK! – reguła oceny w skrypcie traktuje dokładnie ten wzorzec jako sukces z adnotacją, więc wynik to Executed z kodem 0 (nie Failed). requiresReboot=true z pola katalogu ma trafić do raportu kroku execute (Expect.RequiresReboot, Task 5 – silnik po Tasku 3 przenosi to do wyniku Executed). Skutku resetu nie da się odczytać przed restartem, więc Check dowodzi tylko, że stos nie jest zepsuty: netsh winsock show catalog działa (kod 0, niepusty wynik). Przypadek nie restartuje VM (Flow Execute); stan wraca do checkpointu czysta-26H2.'
        }
        @{
            Name = 'repair-wu-reset'; TweakId = 'repair.wu-reset'; Flow = 'Execute'; Catalog = 'BuiltIn'; RestorePoint = 'skip'
            Setup = @('Start-Service -Name wuauserv, bits, cryptsvc', 'New-Item -ItemType Directory -Path (Join-Path $env:SystemRoot ''SoftwareDistribution.dso-20000101-000000'') -Force | Out-Null', 'New-Item -ItemType Directory -Force -Path C:\dso | Out-Null; $a = (Get-CimInstance -ClassName Win32_Service -Filter "Name=''AppIDSvc''").State; $f = (Get-CimInstance -ClassName Win32_SystemDriver -Filter "Name=''applockerfltr''").State; if (-not $a -or -not $f) { throw "Brak odczytu stanu AppIDSvc/applockerfltr" }; Set-Content -LiteralPath C:\dso\wu-deps-before.txt -Value "$a|$f" -Encoding ascii')
            Check = '$deps = ((Get-CimInstance -ClassName Win32_Service -Filter "Name=''AppIDSvc''").State + ''|'' + (Get-CimInstance -ClassName Win32_SystemDriver -Filter "Name=''applockerfltr''").State) -eq (Get-Content -LiteralPath C:\dso\wu-deps-before.txt -Raw).Trim(); $running = $deps -and @(''wuauserv'', ''bits'', ''cryptsvc'' | Where-Object { (Get-Service -Name $_).Status -ne ''Running'' }).Count -eq 0; $sd = @(Get-ChildItem -LiteralPath $env:SystemRoot -Directory -Filter ''SoftwareDistribution.dso-*''); $cr = @(Get-ChildItem -LiteralPath (Join-Path $env:SystemRoot ''System32'') -Directory -Filter ''catroot2.dso-*''); $old = Test-Path -LiteralPath (Join-Path $env:SystemRoot ''SoftwareDistribution.dso-20000101-000000''); $new = (Test-Path -LiteralPath (Join-Path $env:SystemRoot ''SoftwareDistribution'')) -and (Test-Path -LiteralPath (Join-Path $env:SystemRoot ''System32\catroot2'')); $running -and ($sd.Count -eq 1) -and ($cr.Count -eq 1) -and (-not $old) -and $new'
            Cleanup = 'Remove-Item -LiteralPath C:\dso\wu-deps-before.txt -Force -ErrorAction SilentlyContinue; Get-ChildItem -LiteralPath $env:SystemRoot -Directory -Filter ''SoftwareDistribution.dso-*'' | Remove-Item -Recurse -Force -ErrorAction SilentlyContinue; Get-ChildItem -LiteralPath (Join-Path $env:SystemRoot ''System32'') -Directory -Filter ''catroot2.dso-*'' | Remove-Item -Recurse -Force -ErrorAction SilentlyContinue'
            Expect = @{ ExecuteExit = 0; ExecuteOutcome = 'Executed' }
            Hypothesis = 'Pomiar VM 26H2 Home i Pro (4b partia 1, 2026-10-22, sekcja 3): wuauserv i bits zatrzymują się przez ControlService, cryptsvc dopiero razem z zależnymi (AppIDSvc, sterownik applockerfltr) – wtedy catroot2 daje się przenieść; SoftwareDistribution na świeżym Home powstaje przy starcie wuauserv, nowe foldery Windows tworzy w sekundzie 0 po starcie usług. Setup uruchamia wuauserv, bits i cryptsvc (stan sprzed = Running, więc Check wymaga Running), tworzy starą kopię SoftwareDistribution.dso-20000101-000000 i zapisuje stan zależnych cryptsvc (AppIDSvc z Win32_Service, applockerfltr z Win32_SystemDriver) do C:\dso\wu-deps-before.txt; Check wymaga tego samego stanu po akcji (R8: zależne wracają do stanu sprzed, Task 10). Check (niezależny odczyt w VM): usługi działają, dokładnie jedna kopia SoftwareDistribution.dso-* i jedna catroot2.dso-* (stara usunięta po sukcesie), nowe foldery istnieją. Cleanup usuwa kopie; reszta wraca z checkpointem czysta-26H2.'
        }
        @{
            Name = 'repair-wu-reset-full'; TweakId = 'repair.wu-reset-full'; Flow = 'Execute'; Catalog = 'BuiltIn'; RestorePoint = 'skip'
            Setup = @(
                '$k = ''Registry::HKEY_LOCAL_MACHINE\SOFTWARE\Policies\Microsoft\Windows\WindowsUpdate''; New-Item -Path "$k\AU" -Force | Out-Null; New-ItemProperty -LiteralPath $k -Name WUServer -Value ''http://dso-wsus.invalid:8530'' -PropertyType String -Force | Out-Null; New-ItemProperty -LiteralPath $k -Name WUStatusServer -Value ''http://dso-wsus.invalid:8530'' -PropertyType String -Force | Out-Null; New-ItemProperty -LiteralPath $k -Name DisableWindowsUpdateAccess -Value 1 -PropertyType DWord -Force | Out-Null; New-ItemProperty -LiteralPath $k -Name SetDisableUXWUAccess -Value 1 -PropertyType DWord -Force | Out-Null; New-ItemProperty -LiteralPath "$k\AU" -Name NoAutoUpdate -Value 1 -PropertyType DWord -Force | Out-Null; New-ItemProperty -LiteralPath "$k\AU" -Name UseWUServer -Value 1 -PropertyType DWord -Force | Out-Null'
                'Set-Service -Name UsoSvc -StartupType Disabled'
                'Start-Service -Name wuauserv, bits, cryptsvc'
            )
            Check = '$k = ''Registry::HKEY_LOCAL_MACHINE\SOFTWARE\Policies\Microsoft\Windows\WindowsUpdate''; $noPolicies = -not (Test-Path -LiteralPath $k); $reg = @(Get-ChildItem -LiteralPath ''C:\ProgramData\DSOptimizer\backups'' -Filter ''wu-policies-*.reg'' -ErrorAction SilentlyContinue); $hasCopy = ($reg.Count -eq 1) -and ((Get-Content -LiteralPath $reg[0].FullName -Raw) -match ''WUServer''); $svc = @(Get-ChildItem -LiteralPath ''C:\ProgramData\DSOptimizer\backups'' -Filter ''wu-services-*.txt'' -ErrorAction SilentlyContinue).Count -eq 1; $type = { param($n) $p = Get-ItemProperty -LiteralPath "Registry::HKEY_LOCAL_MACHINE\SYSTEM\CurrentControlSet\Services\$n"; if ([int]$p.Start -eq 2) { if ([int]$p.DelayedAutostart -eq 1) { ''AutomaticDelayed'' } else { ''Automatic'' } } elseif ([int]$p.Start -eq 3) { ''Manual'' } else { [string]$p.Start } }; $types = ((& $type ''wuauserv'') -eq ''Manual'') -and ((& $type ''bits'') -eq ''AutomaticDelayed'') -and ((& $type ''cryptsvc'') -eq ''Automatic'') -and ((& $type ''UsoSvc'') -eq ''AutomaticDelayed''); $running = @(''wuauserv'', ''bits'', ''cryptsvc'' | Where-Object { (Get-Service -Name $_).Status -ne ''Running'' }).Count -eq 0; $noPolicies -and $hasCopy -and $svc -and $types -and $running'
            Cleanup = 'Remove-Item -LiteralPath ''Registry::HKEY_LOCAL_MACHINE\SOFTWARE\Policies\Microsoft\Windows\WindowsUpdate'' -Recurse -Force -ErrorAction SilentlyContinue; Remove-Item -LiteralPath ''C:\ProgramData\DSOptimizer\backups'' -Recurse -Force -ErrorAction SilentlyContinue; Get-ChildItem -LiteralPath $env:SystemRoot -Directory -Filter ''SoftwareDistribution.dso-*'' | Remove-Item -Recurse -Force -ErrorAction SilentlyContinue; Get-ChildItem -LiteralPath (Join-Path $env:SystemRoot ''System32'') -Directory -Filter ''catroot2.dso-*'' | Remove-Item -Recurse -Force -ErrorAction SilentlyContinue'
            Expect = @{ ExecuteExit = 0; ExecuteOutcome = 'Executed' }
            Hypothesis = 'Pomiar VM 26H2 Home i Pro (4b partia 1, 2026-10-22, sekcje 3 i 4): PartOfDomain = False (bez odmowy), reg export klucza WindowsUpdate zapisuje też AU w jednym pliku, delete usuwa całość; ChangeServiceConfig dla wuauserv, bits, cryptsvc, UsoSvc działa (msiserver, WaaSMedicSvc, DoSvc odmowa 5 – nie ruszane). Setup zapisuje 6 polityk testowych z pomiaru, wyłącza UsoSvc (typ do przywrócenia) i uruchamia wuauserv, bits, cryptsvc. Check (niezależny odczyt w VM): klucza polityk nie ma, w C:\ProgramData\DSOptimizer\backups jest jeden wu-policies-*.reg z WUServer i jeden wu-services-*.txt, typy startu = wartości świeżej 26H2 (wuauserv Manual, bits AutomaticDelayed, cryptsvc Automatic, UsoSvc AutomaticDelayed, odczyt Start/DelayedAutostart z rejestru), usługi działają. Cleanup usuwa klucz, kopie i katalog backups; reszta wraca z checkpointem czysta-26H2.'
        }
        @{
            Name = 'repair-system-protection'; TweakId = 'repair.system-protection'; Flow = 'Full'; Catalog = 'BuiltIn'; RestorePoint = 'skip'
            Setup = @("Disable-ComputerRestore -Drive 'C:\'")
            Snapshot = @{ Registry = @(@{ Path = 'HKLM\SOFTWARE\Microsoft\Windows NT\CurrentVersion\SPP\Clients'; Name = '{09F7EDC5-294E-4180-AF6A-FB0E6A0E9513}' }); Services = @() }
            CheckAfterApply = '$g = [regex]::Match([string](Get-CimInstance -ClassName Win32_Volume -Filter "DriveLetter = ''C:''").DeviceID, ''\{[^}]+\}'').Value; $v = @((Get-ItemProperty -LiteralPath ''Registry::HKEY_LOCAL_MACHINE\SOFTWARE\Microsoft\Windows NT\CurrentVersion\SPP\Clients'' -ErrorAction Stop).''{09F7EDC5-294E-4180-AF6A-FB0E6A0E9513}''); $g.Length -gt 0 -and @($v | Where-Object { $_ -like "*$g*" }).Count -gt 0'
            CheckAfterUndo = '$g = [regex]::Match([string](Get-CimInstance -ClassName Win32_Volume -Filter "DriveLetter = ''C:''").DeviceID, ''\{[^}]+\}'').Value; $p = Get-ItemProperty -LiteralPath ''Registry::HKEY_LOCAL_MACHINE\SOFTWARE\Microsoft\Windows NT\CurrentVersion\SPP\Clients'' -ErrorAction SilentlyContinue; $v = if ($null -ne $p) { @($p.''{09F7EDC5-294E-4180-AF6A-FB0E6A0E9513}'') } else { @() }; $g.Length -gt 0 -and @($v | Where-Object { $_ -and $_ -like "*$g*" }).Count -eq 0'
            Expect = @{
                Before = 'NotApplied'; ApplyExit = 0; ApplyOutcome = 'Applied'; AfterApply = 'Applied'
                UndoExit = 0; UndoOutcome = 'Undone'; AfterUndo = 'NotApplied'
            }
            Hypothesis = 'Pomiar VM 26H2 Home i Pro (4b partia 1, 2026-10-22, sekcja 7): świeża VM ma ochronę wyłączoną (SPP\Clients bez wpisu); Enable-ComputerRestore -Drive C:\ dodaje wpis woluminu C: (limit 3% ustawia VSS), Disable-ComputerRestore go usuwa, a punkty zostają. Setup wyłącza ochronę jawnie (stan wyjściowy niezależny od kolejności). Snapshot to wartość REG_MULTI_SZ klienta ochrony: stan końcowy = stan przed (brak wartości). CheckAfterApply/CheckAfterUndo to niezależny odczyt w VM (GUID woluminu C: z Win32_Volume i wpis w SPP\Clients), napisany od zera, nie kopia skryptu katalogu.'
        }
        @{
            Name = 'repair-restore-point-disabled'; TweakId = 'repair.restore-point'; Flow = 'Execute'; Catalog = 'BuiltIn'; RestorePoint = 'skip'
            Setup = @("Disable-ComputerRestore -Drive 'C:\'")
            Check = '@(Get-ComputerRestorePoint -ErrorAction SilentlyContinue | Where-Object { $_.Description -eq ''DSOptimizer – punkt ręczny'' }).Count -eq 0'
            Expect = @{ ExecuteExit = 1; ExecuteOutcome = 'Failed'; MessageContains = 'Ochrona systemu na dysku C: jest wyłączona' }
            Hypothesis = 'Potwierdzone na VM 26H2 (sekcja 7): przy wyłączonej ochronie CreateRestorePoint rzuca COMException 0x80070422, serwis zwraca ProtectionDisabled, a handler restore-point kończy akcję Failed z komunikatem odsyłającym do pozycji „Ochrona systemu”. Setup wyłącza ochronę jawnie. Check: punkt „DSOptimizer – punkt ręczny” nie powstał.'
        }
        @{
            Name = 'repair-restore-point-created'; TweakId = 'repair.restore-point'; Flow = 'Execute'; Catalog = 'BuiltIn'; RestorePoint = 'skip'
            Setup = @("Enable-ComputerRestore -Drive 'C:\'")
            Check = '@(Get-ComputerRestorePoint | Where-Object { $_.Description -eq ''DSOptimizer – punkt ręczny'' }).Count -ge 1'
            Cleanup = "Disable-ComputerRestore -Drive 'C:\'"
            Expect = @{ ExecuteExit = 0; ExecuteOutcome = 'Executed' }
            Hypothesis = 'Pomiar VM 26H2 (sekcja 7): po Enable-ComputerRestore -Drive C:\ (24-26 ms, ten sam stan co z okna) serwis tworzy punkt i potwierdza go na liście (Created), więc handler zwraca Executed. Check to niezależny odczyt Get-ComputerRestorePoint: punkt o opisie „DSOptimizer – punkt ręczny” jest na liście. Cleanup tylko wyłącza ochronę (punkt zostaje, bo Disable-ComputerRestore go nie usuwa); stan VM wraca z checkpointem czysta-26H2.'
        }
        @{
            Name = 'repair-system-files'; TweakId = 'repair.system-files'; Flow = 'Execute'; Catalog = 'BuiltIn'; RestorePoint = 'skip'; CliTimeoutSeconds = 1200
            Setup = @('New-Item -ItemType Directory -Force -Path C:\dso | Out-Null; $p = Join-Path $env:SystemRoot ''Logs\CBS\CBS.log''; $len = if (Test-Path -LiteralPath $p) { (Get-Item -LiteralPath $p).Length } else { 0 }; Set-Content -Path C:\dso\system-files-cbs-offset.txt -Value $len -Encoding ascii')
            Check = '$p = Join-Path $env:SystemRoot ''Logs\CBS\CBS.log''; $off = [int64](Get-Content -Path C:\dso\system-files-cbs-offset.txt); $fs = [IO.File]::Open($p, ''Open'', ''Read'', ''ReadWrite, Delete''); try { if ($off -gt $fs.Length) { $off = 0 }; $null = $fs.Seek($off, ''Begin''); $text = (New-Object IO.StreamReader($fs)).ReadToEnd() } finally { $fs.Dispose() }; $busy = @(Get-Process -Name dism, sfc -ErrorAction SilentlyContinue).Count; ($busy -eq 0) -and $text.Contains(''Checking System Update Readiness.'') -and $text.Contains(''[SR] Verify complete'') -and $text.Contains(''[SR] Repairing 0 components'')'
            Cleanup = 'Remove-Item -Path C:\dso\system-files-cbs-offset.txt -Force -ErrorAction SilentlyContinue'
            Expect = @{ ExecuteExit = 0; ExecuteOutcome = 'Executed'; RequiresReboot = $false; MessageContains = 'SFC: nie znaleziono naruszeń integralności plików systemowych.' }
            Hypothesis = 'Pomiar VM 26H2 (Task 2, pkt 1, 2, 6): na zdrowej VM bez sieci DISM /RestoreHealth kończy się kodem 0 po ok. 75 s z blokiem „Checking System Update Readiness.” (Total Detected Corruption 0) w CBS.log, a SFC /scannow kodem 0 po ok. 55 s z grupami [SR] Verify complete i [SR] Repairing 0 components, bez RebootPending – więc handler system-files zwraca Executed bez restartu, z linią SFC „nie znaleziono naruszeń”. Razem ok. 130 s, dlatego CliTimeoutSeconds = 1200 (domyślne 600 s z zapasem by wystarczyło, ale WU w tle wydłuża DISM do ok. 10 min). Setup zapisuje długość CBS.log sprzed akcji, Check to niezależny odczyt segmentu CBS.log od tego offsetu (blok podsumowania DISM i znaczniki [SR] tego przebiegu) i brak działającego dism.exe/sfc.exe po powrocie CLI. Naprawa uszkodzonego pliku nie jest testowana na VM (R8).'
        }
        @{
            Name = 'repair-disk-cleanup'; TweakId = 'repair.disk-cleanup'; Flow = 'Execute'; Catalog = 'BuiltIn'; RestorePoint = 'skip'; RunAs = 'dso'; CliTimeoutSeconds = 2700
            Setup = @(
                'New-Item -ItemType Directory -Force -Path C:\dso, C:\dso\guard | Out-Null; $old = (Get-Date).AddDays(-2); $u = ''C:\Users\dso\AppData\Local\Temp''; if (-not (Test-Path -LiteralPath $u)) { throw "Brak $u – konto dso nie ma profilu" }; foreach ($d in @((Join-Path $env:SystemRoot ''Temp''), $u)) { $o = Join-Path $d ''dso-dc-old.tmp''; Set-Content -LiteralPath $o -Value ''stary'' -Encoding ascii; $i = Get-Item -LiteralPath $o; $i.CreationTime = $old; $i.LastWriteTime = $old; $i.LastAccessTime = $old; Set-Content -LiteralPath (Join-Path $d ''dso-dc-fresh.tmp'') -Value ''swiezy'' -Encoding ascii; $sub = Join-Path $d ''dso-dc-dir''; New-Item -ItemType Directory -Force -Path $sub | Out-Null; $so = Join-Path $sub ''old.tmp''; Set-Content -LiteralPath $so -Value ''stary'' -Encoding ascii; (Get-Item -LiteralPath $so).LastWriteTime = $old; (Get-Item -LiteralPath $sub).LastWriteTime = $old }'
                '$g = ''C:\dso\guard\straznik.txt''; Set-Content -LiteralPath $g -Value ''nie ruszac'' -Encoding ascii; (Get-Item -LiteralPath $g).LastWriteTime = (Get-Date).AddDays(-10); $j = ''C:\Users\dso\AppData\Local\Temp\dso-dc-link''; New-Item -ItemType Junction -Path $j -Value C:\dso\guard | Out-Null; Add-Type -TypeDefinition "using System;using System.Runtime.InteropServices;public static class DsoLinkTime{[DllImport(""kernel32.dll"",SetLastError=true,CharSet=CharSet.Unicode)]static extern IntPtr CreateFileW(string n,uint a,uint s,IntPtr sa,uint c,uint f,IntPtr t);[DllImport(""kernel32.dll"",SetLastError=true)]static extern bool SetFileTime(IntPtr h,ref long c,ref long a,ref long w);[DllImport(""kernel32.dll"")]static extern bool CloseHandle(IntPtr h);public static void Set(string p,DateTime t){IntPtr h=CreateFileW(p,0x100,7,IntPtr.Zero,3,0x02200000,IntPtr.Zero);if(h==new IntPtr(-1))throw new System.ComponentModel.Win32Exception();try{long ft=t.ToFileTimeUtc();if(!SetFileTime(h,ref ft,ref ft,ref ft))throw new System.ComponentModel.Win32Exception();}finally{CloseHandle(h);}}}"; [DsoLinkTime]::Set($j, (Get-Date).AddDays(-2)); $li = Get-Item -LiteralPath $j -Force; if (-not ($li.Attributes -band [IO.FileAttributes]::ReparsePoint) -or $li.LastWriteTime -gt (Get-Date).AddDays(-1)) { throw ''Junction dso-dc-link nie jest starym linkiem'' }'
                '$dl = Join-Path $env:SystemRoot ''SoftwareDistribution\Download''; New-Item -ItemType Directory -Force -Path (Join-Path $dl ''dso-dc'') | Out-Null; Set-Content -LiteralPath (Join-Path $dl ''dso-dc-1.cab'') -Value (''x'' * 4096) -Encoding ascii; Set-Content -LiteralPath (Join-Path $dl ''dso-dc\dso-dc-2.msu'') -Value (''y'' * 4096) -Encoding ascii; $ex = ''C:\Users\dso\AppData\Local\Microsoft\Windows\Explorer''; New-Item -ItemType Directory -Force -Path $ex | Out-Null; Set-Content -LiteralPath (Join-Path $ex ''thumbcache_dsotest.db'') -Value ''miniatura'' -Encoding ascii; Set-Content -LiteralPath (Join-Path $ex ''iconcache_dsotest.db'') -Value ''ikona'' -Encoding ascii; Set-Content -LiteralPath C:\dso\dc-services.txt -Value (@(Get-Service -Name wuauserv, bits | ForEach-Object { $_.Name + ''='' + $_.Status }) | Sort-Object) -Encoding ascii'
            )
            Check = '$u = ''C:\Users\dso\AppData\Local\Temp''; $ex = ''C:\Users\dso\AppData\Local\Microsoft\Windows\Explorer''; $dl = Join-Path $env:SystemRoot ''SoftwareDistribution\Download''; $ok = $true; foreach ($d in @((Join-Path $env:SystemRoot ''Temp''), $u)) { $ok = $ok -and -not (Test-Path -LiteralPath (Join-Path $d ''dso-dc-old.tmp'')) -and -not (Test-Path -LiteralPath (Join-Path $d ''dso-dc-dir'')) -and (Test-Path -LiteralPath (Join-Path $d ''dso-dc-fresh.tmp'')) }; $ok = $ok -and -not (Test-Path -LiteralPath (Join-Path $u ''dso-dc-link'')) -and (Test-Path -LiteralPath C:\dso\guard\straznik.txt) -and ((Get-Content -LiteralPath C:\dso\guard\straznik.txt -Raw).Trim() -eq ''nie ruszac''); $ok = $ok -and (Test-Path -LiteralPath $dl) -and (@(Get-ChildItem -LiteralPath $dl -Force -Recurse | Where-Object { $_.Name -like ''dso-dc*'' }).Count -eq 0); $ok = $ok -and -not (Test-Path -LiteralPath (Join-Path $ex ''thumbcache_dsotest.db'')) -and (Test-Path -LiteralPath (Join-Path $ex ''iconcache_dsotest.db'')); $now = @(Get-Service -Name wuauserv, bits | ForEach-Object { $_.Name + ''='' + $_.Status }); foreach ($line in @(Get-Content -LiteralPath C:\dso\dc-services.txt)) { if ($line -like ''*=Running'' -and ($now -notcontains $line)) { $ok = $false } }; $ok -and (@(Get-Process -Name dism -ErrorAction SilentlyContinue).Count -eq 0)'
            Cleanup = '$j = ''C:\Users\dso\AppData\Local\Temp\dso-dc-link''; if (Test-Path -LiteralPath $j) { (Get-Item -LiteralPath $j -Force).Delete() }; foreach ($p in @((Join-Path $env:SystemRoot ''Temp\dso-dc-fresh.tmp''), ''C:\Users\dso\AppData\Local\Temp\dso-dc-fresh.tmp'', ''C:\Users\dso\AppData\Local\Microsoft\Windows\Explorer\iconcache_dsotest.db'', ''C:\dso\dc-services.txt'')) { Remove-Item -LiteralPath $p -Force -ErrorAction SilentlyContinue }; Remove-Item -LiteralPath C:\dso\guard -Recurse -Force -ErrorAction SilentlyContinue'
            Expect = @{ ExecuteExit = 0; ExecuteOutcome = 'Executed'; RequiresReboot = $false; MessageContains = 'Magazyn składników: wykonano (DISM /StartComponentCleanup, kod 0).' }
            Hypothesis = 'Pomiar VM 26H2 (Task 2, pkt 4a–4e, 6): pliki tymczasowe starsze niż 24 h znikają przy bezpośrednim usuwaniu przez uchwyt, świeże zostają, stary junction jest usuwany jako link, a plik-strażnik w celu (C:\dso\guard) zostaje; Delete-DeliveryOptimizationCache -Force bez sieci kończy się bez błędu (pamięć pusta); SoftwareDistribution\Download (na świeżej VM go nie ma – Setup tworzy go z plikami) jest czyszczony przy zatrzymanych wuauserv i bits, które wracają do stanu sprzed; DISM /StartComponentCleanup bez /ResetBase kończy się kodem 0 po ok. 12 s; thumbcache_*.db konta dso znika, iconcache_* zostaje. Razem poniżej 1 min; CliTimeoutSeconds = 2700 pokrywa limit DISM i skryptu DO (po 1200 s) z zapasem. Setup postarza junction przez SetFileTime na uchwycie z FILE_FLAG_OPEN_REPARSE_POINT (świeży link byłby tylko pominięty jako młody). Check to niezależny odczyt w VM: stare pliki i stary podfolder w C:\Windows\Temp i %TEMP% konta dso zniknęły, świeże zostały, junction zniknął, strażnik nietknięty, w Download brak plików testowych, usługi działające przed działają (zatrzymana przed może wystartować sama – nie sprawdzamy), dism.exe nie działa. RunAs=''dso'': CLI z zadania Harmonogramu konta dso (Interactive + Highest), więc IsCertain = true i SID procesu = zalogowany.'
        }
        @{
            Name = 'repair-disk-cleanup-admin'; TweakId = 'repair.disk-cleanup'; Flow = 'Execute'; Catalog = 'BuiltIn'; RestorePoint = 'skip'; CliTimeoutSeconds = 2700
            Setup = @(
                'New-Item -ItemType Directory -Force -Path C:\dso, C:\dso\guard | Out-Null; $old = (Get-Date).AddDays(-2); $u = ''C:\Users\dso\AppData\Local\Temp''; if (-not (Test-Path -LiteralPath $u)) { throw "Brak $u – konto dso nie ma profilu" }; foreach ($d in @((Join-Path $env:SystemRoot ''Temp''), $u)) { $o = Join-Path $d ''dso-dc-old.tmp''; Set-Content -LiteralPath $o -Value ''stary'' -Encoding ascii; $i = Get-Item -LiteralPath $o; $i.CreationTime = $old; $i.LastWriteTime = $old; $i.LastAccessTime = $old; Set-Content -LiteralPath (Join-Path $d ''dso-dc-fresh.tmp'') -Value ''swiezy'' -Encoding ascii; $sub = Join-Path $d ''dso-dc-dir''; New-Item -ItemType Directory -Force -Path $sub | Out-Null; $so = Join-Path $sub ''old.tmp''; Set-Content -LiteralPath $so -Value ''stary'' -Encoding ascii; (Get-Item -LiteralPath $so).LastWriteTime = $old; (Get-Item -LiteralPath $sub).LastWriteTime = $old }'
                '$g = ''C:\dso\guard\straznik.txt''; Set-Content -LiteralPath $g -Value ''nie ruszac'' -Encoding ascii; (Get-Item -LiteralPath $g).LastWriteTime = (Get-Date).AddDays(-10); $j = ''C:\Users\dso\AppData\Local\Temp\dso-dc-link''; New-Item -ItemType Junction -Path $j -Value C:\dso\guard | Out-Null; Add-Type -TypeDefinition "using System;using System.Runtime.InteropServices;public static class DsoLinkTime{[DllImport(""kernel32.dll"",SetLastError=true,CharSet=CharSet.Unicode)]static extern IntPtr CreateFileW(string n,uint a,uint s,IntPtr sa,uint c,uint f,IntPtr t);[DllImport(""kernel32.dll"",SetLastError=true)]static extern bool SetFileTime(IntPtr h,ref long c,ref long a,ref long w);[DllImport(""kernel32.dll"")]static extern bool CloseHandle(IntPtr h);public static void Set(string p,DateTime t){IntPtr h=CreateFileW(p,0x100,7,IntPtr.Zero,3,0x02200000,IntPtr.Zero);if(h==new IntPtr(-1))throw new System.ComponentModel.Win32Exception();try{long ft=t.ToFileTimeUtc();if(!SetFileTime(h,ref ft,ref ft,ref ft))throw new System.ComponentModel.Win32Exception();}finally{CloseHandle(h);}}}"; [DsoLinkTime]::Set($j, (Get-Date).AddDays(-2)); $li = Get-Item -LiteralPath $j -Force; if (-not ($li.Attributes -band [IO.FileAttributes]::ReparsePoint) -or $li.LastWriteTime -gt (Get-Date).AddDays(-1)) { throw ''Junction dso-dc-link nie jest starym linkiem'' }'
                '$dl = Join-Path $env:SystemRoot ''SoftwareDistribution\Download''; New-Item -ItemType Directory -Force -Path (Join-Path $dl ''dso-dc'') | Out-Null; Set-Content -LiteralPath (Join-Path $dl ''dso-dc-1.cab'') -Value (''x'' * 4096) -Encoding ascii; Set-Content -LiteralPath (Join-Path $dl ''dso-dc\dso-dc-2.msu'') -Value (''y'' * 4096) -Encoding ascii; $ex = ''C:\Users\dso\AppData\Local\Microsoft\Windows\Explorer''; New-Item -ItemType Directory -Force -Path $ex | Out-Null; Set-Content -LiteralPath (Join-Path $ex ''thumbcache_dsotest.db'') -Value ''miniatura'' -Encoding ascii; Set-Content -LiteralPath (Join-Path $ex ''iconcache_dsotest.db'') -Value ''ikona'' -Encoding ascii; Set-Content -LiteralPath C:\dso\dc-services.txt -Value (@(Get-Service -Name wuauserv, bits | ForEach-Object { $_.Name + ''='' + $_.Status }) | Sort-Object) -Encoding ascii'
            )
            Check = '$u = ''C:\Users\dso\AppData\Local\Temp''; $ex = ''C:\Users\dso\AppData\Local\Microsoft\Windows\Explorer''; $dl = Join-Path $env:SystemRoot ''SoftwareDistribution\Download''; $ok = $true; foreach ($d in @((Join-Path $env:SystemRoot ''Temp''), $u)) { $ok = $ok -and -not (Test-Path -LiteralPath (Join-Path $d ''dso-dc-old.tmp'')) -and -not (Test-Path -LiteralPath (Join-Path $d ''dso-dc-dir'')) -and (Test-Path -LiteralPath (Join-Path $d ''dso-dc-fresh.tmp'')) }; $ok = $ok -and -not (Test-Path -LiteralPath (Join-Path $u ''dso-dc-link'')) -and (Test-Path -LiteralPath C:\dso\guard\straznik.txt) -and ((Get-Content -LiteralPath C:\dso\guard\straznik.txt -Raw).Trim() -eq ''nie ruszac''); $ok = $ok -and (Test-Path -LiteralPath $dl) -and (@(Get-ChildItem -LiteralPath $dl -Force -Recurse | Where-Object { $_.Name -like ''dso-dc*'' }).Count -eq 0); $ok = $ok -and -not (Test-Path -LiteralPath (Join-Path $ex ''thumbcache_dsotest.db'')) -and (Test-Path -LiteralPath (Join-Path $ex ''iconcache_dsotest.db'')); $now = @(Get-Service -Name wuauserv, bits | ForEach-Object { $_.Name + ''='' + $_.Status }); foreach ($line in @(Get-Content -LiteralPath C:\dso\dc-services.txt)) { if ($line -like ''*=Running'' -and ($now -notcontains $line)) { $ok = $false } }; $ok -and (@(Get-Process -Name dism -ErrorAction SilentlyContinue).Count -eq 0)'
            Cleanup = '$j = ''C:\Users\dso\AppData\Local\Temp\dso-dc-link''; if (Test-Path -LiteralPath $j) { (Get-Item -LiteralPath $j -Force).Delete() }; foreach ($p in @((Join-Path $env:SystemRoot ''Temp\dso-dc-fresh.tmp''), ''C:\Users\dso\AppData\Local\Temp\dso-dc-fresh.tmp'', ''C:\Users\dso\AppData\Local\Microsoft\Windows\Explorer\iconcache_dsotest.db'', ''C:\dso\dc-services.txt'')) { Remove-Item -LiteralPath $p -Force -ErrorAction SilentlyContinue }; Remove-Item -LiteralPath C:\dso\guard -Recurse -Force -ErrorAction SilentlyContinue'
            Expect = @{ ExecuteExit = 0; ExecuteOutcome = 'Executed'; RequiresReboot = $false; MessageContains = 'Magazyn składników: wykonano (DISM /StartComponentCleanup, kod 0).' }
            Hypothesis = 'Pomiar VM 26H2 (Task 2, pkt 4a–4e, 6): pliki tymczasowe starsze niż 24 h znikają przy bezpośrednim usuwaniu przez uchwyt, świeże zostają, stary junction jest usuwany jako link, a plik-strażnik w celu (C:\dso\guard) zostaje; Delete-DeliveryOptimizationCache -Force bez sieci kończy się bez błędu (pamięć pusta); SoftwareDistribution\Download (na świeżej VM go nie ma – Setup tworzy go z plikami) jest czyszczony przy zatrzymanych wuauserv i bits, które wracają do stanu sprzed; DISM /StartComponentCleanup bez /ResetBase kończy się kodem 0 po ok. 12 s; thumbcache_*.db konta dso znika, iconcache_* zostaje. Razem poniżej 1 min; CliTimeoutSeconds = 2700 pokrywa limit DISM i skryptu DO (po 1200 s) z zapasem. Setup postarza junction przez SetFileTime na uchwycie z FILE_FLAG_OPEN_REPARSE_POINT (świeży link byłby tylko pominięty jako młody). Check to niezależny odczyt w VM: stare pliki i stary podfolder w C:\Windows\Temp i %TEMP% konta dso zniknęły, świeże zostały, junction zniknął, strażnik nietknięty, w Download brak plików testowych, usługi działające przed działają (zatrzymana przed może wystartować sama – nie sprawdzamy), dism.exe nie działa. Bez RunAs: CLI jako Administrator PS Direct (sesja 0, DISM tam działa – pomiar); kroki konta dotyczą zalogowanego dso po SID (ProfileList), nie profilu Administratora.'
        }
        @{
            Name = 'repair-windows-old-absent'; TweakId = 'repair.windows-old'; Flow = 'Execute'; Catalog = 'BuiltIn'; RestorePoint = 'skip'
            Check = '$k = ''Registry::HKEY_LOCAL_MACHINE\SOFTWARE\Microsoft\Windows\CurrentVersion\Explorer\VolumeCaches''; $all = @(Get-ChildItem -LiteralPath $k); $flags = @($all | Where-Object { $null -ne $_.GetValue(''StateFlags7351'') }).Count; $busy = @(Get-Process -Name cleanmgr -ErrorAction SilentlyContinue).Count; (-not (Test-Path -LiteralPath C:\Windows.old)) -and ($all.Count -gt 0) -and ($flags -eq 0) -and ($busy -eq 0)'
            Expect = @{ ExecuteExit = 1; ExecuteOutcome = 'NotSupported'; MessageContains = 'nie ma czego usuwać' }
            Hypothesis = 'Odczyt VM 26H2 (Task 2, pkt 5f): na świeżej VM nie ma C:\Windows.old, więc handler windows-old po strażniku serwisowania zwraca NotSupported „Nie ma poprzedniej instalacji Windows – nie ma czego usuwać.” bez żadnego kroku: nie czyta VolumeCaches, nie zapisuje StateFlags7351 i nie uruchamia cleanmgr. Check to niezależny odczyt w VM: folderu nie ma, pod żadnym z programów obsługi VolumeCaches nie ma wartości StateFlags7351, cleanmgr nie działa.'
        }
        @{
            Name = 'repair-windows-old'; TweakId = 'repair.windows-old'; Flow = 'Execute'; Catalog = 'BuiltIn'; RestorePoint = 'skip'; RunAs = 'dso'; CliTimeoutSeconds = 3000
            Setup = @('if (Test-Path -LiteralPath C:\Windows.old) { throw ''C:\Windows.old już istnieje – nic nie utworzono'' }; $chunk = New-Object byte[] 1048576; (New-Object System.Random 7).NextBytes($chunk); foreach ($d in @(''Windows\System32'', ''Windows\SysWOW64'', ''Users\Public\Documents'', ''Program Files\DsoTest'', ''ProgramData\DsoTest'')) { $f = Join-Path ''C:\Windows.old'' $d; $null = [IO.Directory]::CreateDirectory($f); foreach ($p in 1..2) { $s = New-Object IO.FileStream((Join-Path $f (''dso-wo-'' + $p + ''.bin'')), ''CreateNew'', ''Write''); try { for ($m = 0; $m -lt 20; $m++) { $s.Write($chunk, 0, $chunk.Length) } } finally { $s.Dispose() } }; [IO.File]::WriteAllText((Join-Path $f ''dso-wo.txt''), ''plik testowy Tasku 8'') }; if (@(Get-ChildItem -LiteralPath C:\Windows.old -Recurse -File).Count -ne 15) { throw ''Przygotowanie C:\Windows.old niepełne'' }')
            Check = '$k = ''Registry::HKEY_LOCAL_MACHINE\SOFTWARE\Microsoft\Windows\CurrentVersion\Explorer\VolumeCaches''; $all = @(Get-ChildItem -LiteralPath $k); $flags = @($all | Where-Object { $null -ne $_.GetValue(''StateFlags7351'') }).Count; $busy = @(Get-Process -Name cleanmgr -ErrorAction SilentlyContinue).Count; (-not (Test-Path -LiteralPath C:\Windows.old)) -and ($all.Count -gt 0) -and ($flags -eq 0) -and ($busy -eq 0)'
            Cleanup = 'Remove-Item -LiteralPath C:\Windows.old -Recurse -Force -ErrorAction SilentlyContinue'
            Expect = @{ ExecuteExit = 0; ExecuteOutcome = 'Executed'; RequiresReboot = $false; MessageContains = 'Usunięto poprzednią instalację Windows.' }
            Hypothesis = 'Pomiar VM 26H2 (Task 2, pkt 5, 5f, 6; Ruling R8/R18): zwykły folder C:\Windows.old (200 MB, 15 plików, właściciel Administratorzy, bez zmiany ACL) jest rozpoznawany przez program obsługi „Previous Installations” – po StateFlags7351 = 2 tylko pod tym kluczem cleanmgr /sagerun:7351 usuwa folder w ≤ 5 s, a uruchomiony z sesji konta dso (zadanie Harmonogramu, K2) kończy się sam z wynikiem 0x0 (pomiar Tasku 2: po ok. 11 min; przebieg handlera Tasku 8: kod 0 po 7 s), a z PS Direct (K1) nie kończy się w 1200 s – dlatego RunAs = dso. Handler czeka na koniec procesu, w finally usuwa StateFlags7351 i zwraca Executed po odczycie zwrotnym (folderu nie ma). CliTimeoutSeconds = 3000 > limit handlera 2700 s, żeby także wynik po limicie dotarł do raportu. Check to niezależny odczyt w VM: folderu nie ma, pod żadnym programem obsługi VolumeCaches nie ma StateFlags7351, cleanmgr nie działa.'
        }
        @{
            Name = 'repair-winget-other-account'; TweakId = 'repair.winget'; Flow = 'Execute'; Catalog = 'BuiltIn'; RestorePoint = 'skip'
            Setup = @('New-Item -ItemType Directory -Force -Path C:\dso | Out-Null; $lines = @(Get-AppxPackage -AllUsers -Name Microsoft.DesktopAppInstaller | ForEach-Object { $n = $_.PackageFullName; $_.PackageUserInformation | ForEach-Object { "$n|$($_.UserSecurityId.Sid)|$($_.InstallState)" } }); if ($lines.Count -eq 0) { throw "Brak pakietu Microsoft.DesktopAppInstaller w odczycie -AllUsers" }; Set-Content -Path C:\dso\winget-before.txt -Value ($lines | Sort-Object) -Encoding ascii')
            Check = '$saved = @(Get-Content -Path C:\dso\winget-before.txt); $now = @(Get-AppxPackage -AllUsers -Name Microsoft.DesktopAppInstaller | ForEach-Object { $n = $_.PackageFullName; $_.PackageUserInformation | ForEach-Object { "$n|$($_.UserSecurityId.Sid)|$($_.InstallState)" } }) | Sort-Object; $saved.Count -gt 0 -and (($saved -join '';'') -eq ($now -join '';''))'
            Cleanup = 'Remove-Item -Path C:\dso\winget-before.txt -Force -ErrorAction SilentlyContinue'
            Expect = @{ ExecuteExit = 1; ExecuteOutcome = 'Failed'; MessageContains = 'uruchom DSOptimizer na koncie' }
            Hypothesis = 'Runner PS Direct działa jako Administrator, a $UserSid to konto dso, więc naprawa winget ma odmówić (Failed, kod 1) BEZ rejestracji (wzorzec repair-store-other-account). Setup zapisuje odczyt Get-AppxPackage -AllUsers (PackageFullName, SID, InstallState) pakietu Microsoft.DesktopAppInstaller do C:\dso\winget-before.txt, Check wymaga identycznego odczytu po akcji (w szczególności brak nowego wpisu Administratora). Cleanup usuwa tylko plik.'
        }
        @{
            Name = 'repair-winget-dso'; TweakId = 'repair.winget'; Flow = 'Execute'; Catalog = 'BuiltIn'; RestorePoint = 'skip'; RunAs = 'dso'
            Setup = @(
                'New-Item -ItemType Directory -Force -Path C:\dso | Out-Null; $dir = ''C:\Users\dso\AppData\Local\Microsoft\WindowsApps''; Remove-Item -LiteralPath (Join-Path $dir ''winget.exe'') -Force; if (@(Get-ChildItem -LiteralPath $dir -Force -Filter ''winget.exe'' -ErrorAction SilentlyContinue).Count -ne 0) { throw "Alias winget.exe nadal istnieje w profilu dso: stan wyjściowy nie jest zepsuty" }'
                'Set-Content -LiteralPath C:\dso\winget-check.ps1 -Value @(''$o = (& winget --version 2>&1 | Out-String).Trim()'', ''Set-Content -LiteralPath C:\dso\winget-dso-version.txt -Value ("$LASTEXITCODE $o")'') -Encoding ascii'
            )
            Check = '$ErrorActionPreference = ''Stop''; $out = ''C:\dso\winget-dso-version.txt''; Remove-Item -LiteralPath $out -Force -ErrorAction SilentlyContinue; $n = ''DSO-WingetCheck''; $a = New-ScheduledTaskAction -Execute ''powershell.exe'' -Argument ''-NoProfile -NonInteractive -ExecutionPolicy Bypass -File C:\dso\winget-check.ps1''; $p = New-ScheduledTaskPrincipal -UserId ''dso'' -LogonType Interactive -RunLevel Highest; try { Register-ScheduledTask -TaskName $n -Action $a -Principal $p -Force | Out-Null; Start-ScheduledTask -TaskName $n; $w = [System.Diagnostics.Stopwatch]::StartNew(); while ((Get-ScheduledTask -TaskName $n).State -eq ''Running'' -or (Get-ScheduledTaskInfo -TaskName $n).LastTaskResult -eq 267011) { if ($w.Elapsed.TotalSeconds -gt 60) { throw ''winget --version jako dso: przekroczono 60 s'' }; Start-Sleep -Milliseconds 500 } } finally { Unregister-ScheduledTask -TaskName $n -Confirm:$false -ErrorAction SilentlyContinue }; $txt = (Get-Content -LiteralPath $out -Raw).Trim(); $txt -match ''^0 v\d+(\.\d+)+$'''
            Cleanup = 'Remove-Item -Path C:\dso\winget-check.ps1, C:\dso\winget-dso-version.txt -Force -ErrorAction SilentlyContinue'
            Expect = @{ ExecuteExit = 0; ExecuteOutcome = 'Executed' }
            Hypothesis = 'Pomiar VM 26H2 Home i Pro (4b partia 1, 2026-10-22, sekcja 6): App Installera nie da się usunąć dla konta (0x80073CFA), więc stan zepsuty to brak aliasu winget.exe w profilu dso (Setup usuwa go z poziomu Administratora i rzuca, gdy alias dalej istnieje); CLI jako dso (RunAs, zadanie Harmonogramu Interactive + Highest, SID procesu = zalogowany) rejestruje pakiet po nazwie rodziny i potwierdza winget --version. Check to niezależny odczyt: winget --version uruchomione jako dso tym samym sposobem (zadanie Harmonogramu, skrypt z Setup zapisuje kod i wyjście do pliku) musi dać kod 0 i numer wersji v<cyfry>. Cleanup usuwa tylko pliki pomocnicze.'
        }
        @{
            Name = 'restore-point-disabled'; TweakId = 'vmtest.hklm-value'; Flow = 'ApplyOnly'; RestorePoint = 'required'
            Setup = @("Disable-ComputerRestore -Drive 'C:\'")
            Snapshot = @{ Registry = @(@{ Path = 'HKLM\SOFTWARE\DSOptimizer.VmTests'; Name = 'HklmDword' }, @{ Path = 'HKLM\SOFTWARE\Microsoft\Windows NT\CurrentVersion\SystemRestore'; Name = 'SystemRestorePointCreationFrequency' }); Services = @() }
            Expect = @{
                Before = 'NotApplied'; ApplyExit = 1; ApplyOutcome = 'Skipped'; AfterApply = 'NotApplied'
                Aborted = $true; RestorePointStatus = 'ProtectionDisabled'
            }
            Hypothesis = 'Potwierdzone na VM 26H2: CreateRestorePoint przy wyłączonej ochronie rzuca COMException 0x80070422 -> ProtectionDisabled, kod 1058; seria przerwana (required).'
        }
        # Etap 4a partia 2 (Task 7): usuwanie aplikacji przez CLI (--list-apps, --remove-app <AppId>, --list-apps) + niezależny Check w VM.
        # Programy Win32 to pakiety testowe budowane w VM z tests\vm\fixtures (bez sieci); klucze Uninstall bez spacji (Ruling R6).
        # Ochrona systemu na VM jest wyłączona (pomiar T2), a Setup przypadków Win32 wyłącza ją jawnie (niezależnie od kolejności).
        @{
            Name = 'app-store-allusers'; AppId = 'appx:Microsoft.BingNews_8wekyb3d8bbwe'; Flow = 'AppRemove'; RestorePoint = 'skip'
            Expect = @{
                ListedBefore = $true; Group = 'Suggested'; Removability = 'Removable'
                RemoveExit = 0; RemoveOutcome = 'Removed'; RequiresReboot = $false; ListedAfter = $false
                WarningContains = 'Usunięta dla wszystkich kont na tym komputerze.'
            }
            Check = "-not (Get-AppxPackage -AllUsers -Name 'Microsoft.BingNews') -and -not (Get-AppxProvisionedPackage -Online | Where-Object DisplayName -eq 'Microsoft.BingNews')"
            Hypothesis = 'CLI jako wbudowany Administrator (PS Direct), zalogowany dso: SID procesu różny od zalogowanego, więc tryb AllUsers (Ruling R8): Remove-AppxPackage -AllUsers. Pakiet znika dla wszystkich kont i z obrazu; Get-AppxPackage -AllUsers nie widzi go też jako Staged (pomiar z review T5).'
        }
        @{
            Name = 'app-store-user'; AppId = 'appx:Microsoft.BingWeather_8wekyb3d8bbwe'; Flow = 'AppRemove'; RestorePoint = 'skip'; RunAs = 'dso'
            Expect = @{
                ListedBefore = $true; Group = 'Suggested'; Removability = 'Removable'
                RemoveExit = 0; RemoveOutcome = 'Removed'; RequiresReboot = $false; ListedAfter = $false
                WarningAbsent = 'Usunięta dla wszystkich kont'
            }
            Check = "-not (Get-AppxPackage -User '{UserSid}' -Name 'Microsoft.BingWeather') -and -not (Get-AppxProvisionedPackage -Online | Where-Object DisplayName -eq 'Microsoft.BingWeather')"
            Hypothesis = 'CLI jako dso z najwyższymi uprawnieniami (zadanie Harmonogramu Interactive/Highest, wzorzec K2): SID procesu = zalogowany, więc tryb CurrentUser (-User <SID dso>) + deprovisioning grupy microsoft; pakiet znika dla dso i z obrazu, bez ostrzeżenia o wszystkich kontach.'
        }
        @{
            Name = 'app-store-required-aborted'; AppId = 'appx:Microsoft.MicrosoftSolitaireCollection_8wekyb3d8bbwe'; Flow = 'AppRemove'; RestorePoint = 'required'
            Setup = @("Disable-ComputerRestore -Drive 'C:\'")
            Expect = @{
                ListedBefore = $true; Group = 'Suggested'; Removability = 'Removable'
                RemoveExit = 1; RemoveOutcome = 'Skipped'; Aborted = $true; RestorePointStatus = 'ProtectionDisabled'; ListedAfter = $true
                MessageContains = 'Seria przerwana przed pierwszym usunięciem'
            }
            Check = "[bool](Get-AppxPackage -User '{UserSid}' -Name 'Microsoft.MicrosoftSolitaireCollection') -and [bool](Get-AppxProvisionedPackage -Online | Where-Object DisplayName -eq 'Microsoft.MicrosoftSolitaireCollection')"
            Hypothesis = 'R20: domyślne --restore-point required przy serii samych AppX znaczy to samo co przy --apply. Ochrona systemu wyłączona -> punkt nie powstaje (ProtectionDisabled), seria przerwana przed pierwszym usunięciem (kod 1); pakiet zostaje dla dso i w obrazie.'
        }
        @{
            Name = 'app-win32-quiet'; AppId = 'win32:HKLM\SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall\DsoTestQuiet'; Flow = 'AppRemove'; RestorePoint = 'ifavailable'
            Setup = @("Disable-ComputerRestore -Drive 'C:\'", "Set-ExecutionPolicy -Scope Process -ExecutionPolicy Bypass -Force; & 'C:\dso\fixtures\Install-DsoTestProgram.ps1' -Mode quiet -KeyName DsoTestQuiet")
            Expect = @{
                ListedBefore = $true; Group = 'Other'; Removability = 'Removable'
                RemoveExit = 0; RemoveOutcome = 'Removed'; RequiresReboot = $false; ListedAfter = $false; RestorePointStatus = 'ProtectionDisabled'
            }
            Check = "-not (Test-Path -LiteralPath 'Registry::HKEY_LOCAL_MACHINE\SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall\DsoTestQuiet') -and (Test-Path -LiteralPath 'C:\DsoTest\DsoTestQuiet\ran.log')"
            Hypothesis = 'Cichy deinstalator z QuietUninstallString (CreateProcessW, pełna ścieżka): usuwa wpis i kończy się kodem 0. Punkt przywracania wymuszony dla programu, ochrona wyłączona -> ProtectionDisabled, ifavailable = kontynuacja (R15).'
        }
        @{
            Name = 'app-win32-required-aborted'; AppId = 'win32:HKLM\SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall\DsoTestRequired'; Flow = 'AppRemove'; RestorePoint = 'required'
            Setup = @("Disable-ComputerRestore -Drive 'C:\'", "Set-ExecutionPolicy -Scope Process -ExecutionPolicy Bypass -Force; & 'C:\dso\fixtures\Install-DsoTestProgram.ps1' -Mode quiet -KeyName DsoTestRequired")
            Expect = @{
                ListedBefore = $true; Group = 'Other'; Removability = 'Removable'
                RemoveExit = 1; RemoveOutcome = 'Skipped'; Aborted = $true; RestorePointStatus = 'ProtectionDisabled'; ListedAfter = $true
                MessageContains = 'Seria przerwana przed pierwszym usunięciem'
            }
            Check = "(Test-Path -LiteralPath 'Registry::HKEY_LOCAL_MACHINE\SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall\DsoTestRequired') -and -not (Test-Path -LiteralPath 'C:\DsoTest\DsoTestRequired\ran.log')"
            Hypothesis = 'Domyślne --restore-point required przy programie Win32: punkt nie powstał (ochrona wyłączona), odpowiedź na „Kontynuować bez punktu przywracania?” = Nie, seria przerwana przed pierwszym usunięciem; deinstalator nie wystartował (brak ran.log), wpis zostaje.'
        }
        @{
            Name = 'app-win32-deferred'; AppId = 'win32:HKLM\SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall\DsoTestDeferred'; Flow = 'AppRemove'; RestorePoint = 'ifavailable'
            Setup = @("Disable-ComputerRestore -Drive 'C:\'", "Set-ExecutionPolicy -Scope Process -ExecutionPolicy Bypass -Force; & 'C:\dso\fixtures\Install-DsoTestProgram.ps1' -Mode deferred -KeyName DsoTestDeferred -MarkerPath C:\dso\out\DsoTestDeferred.survived")
            Expect = @{
                ListedBefore = $true; Group = 'Other'; Removability = 'Removable'
                RemoveExit = 0; RemoveOutcome = 'Removed'; RequiresReboot = $false; ListedAfter = $false; RestorePointStatus = 'ProtectionDisabled'
            }
            Check = '$Marker = ''C:\dso\out\DsoTestDeferred.survived''; $Deadline = (Get-Date).AddSeconds(150); while (-not (Test-Path -LiteralPath $Marker) -and (Get-Date) -lt $Deadline) { Start-Sleep -Seconds 1 }; (-not (Test-Path -LiteralPath ''Registry::HKEY_LOCAL_MACHINE\SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall\DsoTestDeferred'')) -and (Test-Path -LiteralPath $Marker) -and ((Get-Content -LiteralPath $Marker -Raw) -like ''dsoptimizer-gone=True*'')'
            Hypothesis = 'Review Focus 2: deinstalator kopiuje się do %TEMP% i od razu kończy (kod 0), kopia usuwa wpis po 5 s; weryfikacja czeka na zniknięcie wpisu (do 60 s), więc Removed. Znacznik dsoptimizer-gone=True: kopia przeżyła zakończenie procesu DSOptimizer (deinstalator nie jest w jego Job Objecie).'
        }
        @{
            Name = 'app-win32-exit3010'; AppId = 'win32:HKLM\SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall\DsoTest3010'; Flow = 'AppRemove'; RestorePoint = 'ifavailable'
            Setup = @("Disable-ComputerRestore -Drive 'C:\'", "Set-ExecutionPolicy -Scope Process -ExecutionPolicy Bypass -Force; & 'C:\dso\fixtures\Install-DsoTestProgram.ps1' -Mode exit3010 -KeyName DsoTest3010")
            Expect = @{
                ListedBefore = $true; Group = 'Other'; Removability = 'Removable'
                RemoveExit = 0; RemoveOutcome = 'Removed'; RequiresReboot = $true; ListedAfter = $false; RestorePointStatus = 'ProtectionDisabled'
            }
            Check = "-not (Test-Path -LiteralPath 'Registry::HKEY_LOCAL_MACHINE\SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall\DsoTest3010')"
            Hypothesis = 'Deinstalator usuwa wpis i zwraca 3010: aplikacja zniknęła, więc Removed z RequiresReboot=true (restart trafia do banera przez journal PendingReboot).'
        }
        @{
            Name = 'app-win32-msi'; AppId = 'win32:HKLM\SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall\{4E3B2A10-7C5D-4F8E-9A1B-2C3D4E5F6A70}'; Flow = 'AppRemove'; RestorePoint = 'ifavailable'
            Setup = @(
                "Disable-ComputerRestore -Drive 'C:\'"
                "Set-ExecutionPolicy -Scope Process -ExecutionPolicy Bypass -Force; & 'C:\dso\fixtures\New-DsoTestMsi.ps1' -Path 'C:\dso\fixtures\DsoTestMsi.msi' | Out-Null"
                '$Process = Start-Process -FilePath (Join-Path $env:SystemRoot ''System32\msiexec.exe'') -ArgumentList ''/i'', ''C:\dso\fixtures\DsoTestMsi.msi'', ''/qn'', ''/norestart'', ''/l*v'', ''C:\dso\out\DsoTestMsi-install.log'' -Wait -PassThru; if ($Process.ExitCode -ne 0) { throw "msiexec /i zakończył się kodem $($Process.ExitCode)" }'
            )
            Expect = @{
                ListedBefore = $true; Group = 'Other'; Removability = 'Removable'
                RemoveExit = 0; RemoveOutcome = 'Removed'; RequiresReboot = $false; ListedAfter = $false; RestorePointStatus = 'ProtectionDisabled'
            }
            Check = '(-not (Test-Path -LiteralPath ''Registry::HKEY_LOCAL_MACHINE\SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall\{4E3B2A10-7C5D-4F8E-9A1B-2C3D4E5F6A70}'')) -and ($null -eq (Get-ItemProperty -LiteralPath ''Registry::HKEY_LOCAL_MACHINE\SOFTWARE\DSOptimizer.VmTests\Msi'' -Name ''Installed'' -ErrorAction SilentlyContinue))'
            Hypothesis = 'MSI bez plików (COM WindowsInstaller.Installer, x64, jedna wartość rejestru): wpis ARP {ProductCode} z WindowsInstaller=1; DSOptimizer usuwa przez msiexec.exe /x {kod} /qn /norestart z System32. Po usunięciu nie ma wpisu ARP ani wartości z tabeli Registry.'
        }
        @{
            Name = 'app-protected-calculator'; AppId = 'appx:Microsoft.WindowsCalculator_8wekyb3d8bbwe'; Flow = 'AppRemove'; RestorePoint = 'ifavailable'
            Expect = @{
                ListedBefore = $true; Group = 'Protected'; Removability = 'Protected'
                RemoveExit = 1; RemoveOutcome = 'Refused'; ListedAfter = $true; MessageContains = 'nie wolno usunąć'
            }
            Check = "[bool](Get-AppxPackage -User '{UserSid}' -Name 'Microsoft.WindowsCalculator') -and [bool](Get-AppxProvisionedPackage -Online | Where-Object DisplayName -eq 'Microsoft.WindowsCalculator')"
            Hypothesis = 'Review Focus 1: Kalkulator jest na liście chronionej (wygrywa z każdą regułą śmieci) -> grupa Protected, --remove-app = Refused z kodem 1 i nic się nie uruchamia; pakiet zostaje dla dso i w obrazie.'
        }
        @{
            Name = 'app-win32-other-account'; AppId = 'win32:HKU\{UserSid}\Software\Microsoft\Windows\CurrentVersion\Uninstall\DsoTestUser'; Flow = 'AppRemove'; RestorePoint = 'ifavailable'
            Setup = @("Set-ExecutionPolicy -Scope Process -ExecutionPolicy Bypass -Force; & 'C:\dso\fixtures\Install-DsoTestProgram.ps1' -Mode quiet -KeyName DsoTestUser -UserSid '{UserSid}'")
            Expect = @{
                ListedBefore = $true; Group = 'Other'; Removability = 'OtherAccount'
                RemoveExit = 1; RemoveOutcome = 'Refused'; ListedAfter = $true; MessageContains = 'Uruchom DSOptimizer na koncie dso'
            }
            Check = "(Test-Path -LiteralPath 'Registry::HKEY_USERS\{UserSid}\Software\Microsoft\Windows\CurrentVersion\Uninstall\DsoTestUser') -and -not (Test-Path -LiteralPath 'C:\DsoTest\DsoTestUser\ran.log')"
            Hypothesis = 'Review Focus 4: program per użytkownik w HKU\<SID dso>, CLI jako Administrator (SID procesu inny niż właściciel wpisu) -> OtherAccount z nazwą konta, --remove-app = Refused z kodem 1; deinstalator nie wystartował, wpis zostaje.'
        }
        @{
            Name = 'install-other-account'; Flow = 'AppInstall'; Command = 'install-apps'; AppIds = @('install.7zip', 'install.vlc')
            Setup = @("if (Test-Path -LiteralPath 'C:\ProgramData\DSOptimizer\logs\winget') { Remove-Item -LiteralPath 'C:\ProgramData\DSOptimizer\logs\winget' -Recurse -Force }")
            Expect = @{
                Exit = 1; Outcomes = @{ 'install.7zip' = 'Refused'; 'install.vlc' = 'Refused' }; WingetState = 'OtherAccount'
                MessageContains = 'uruchom DSOptimizer na koncie'
            }
            Check = "-not (Test-Path -LiteralPath 'C:\ProgramData\DSOptimizer\logs\winget') -or @(Get-ChildItem -LiteralPath 'C:\ProgramData\DSOptimizer\logs\winget' -Recurse -Force -File).Count -eq 0"
            Hypothesis = 'Review Focus 5: CLI jako Administrator PS Direct (SID procesu inny niż zalogowany dso) -> obie pozycje Refused z tekstem „uruchom DSOptimizer na koncie dso”, kod 1, winget nie startuje (także --version), więc nie powstaje żaden plik logu winget.'
        }
        @{
            Name = 'list-install-other-account'; Flow = 'AppInstall'; Command = 'list-install'
            Setup = @("if (Test-Path -LiteralPath 'C:\ProgramData\DSOptimizer\logs\winget') { Remove-Item -LiteralPath 'C:\ProgramData\DSOptimizer\logs\winget' -Recurse -Force }")
            Expect = @{ Exit = 0; WingetState = 'OtherAccount'; Installed = @{ 'install.7zip' = $false; 'install.vlc' = $false }; MessageContains = 'uruchom DSOptimizer na koncie' }
            Check = "-not (Test-Path -LiteralPath 'C:\ProgramData\DSOptimizer\logs\winget') -or @(Get-ChildItem -LiteralPath 'C:\ProgramData\DSOptimizer\logs\winget' -Recurse -Force -File).Count -eq 0"
            Hypothesis = 'Lista wbudowana jako Administrator PS Direct: odczyt się udaje (kod 0), stan winget = OtherAccount z wyjaśnieniem w raporcie, pozycje bez statusu „zainstalowana” na czystej VM; nic nie startuje winget.'
        }
        @{
            Name = 'install-offline-dso'; Flow = 'AppInstall'; Command = 'install-apps'; AppIds = @('install.7zip', 'install.vlc'); RunAs = 'dso'
            Expect = @{
                # Kolejność serii = kolejność apps.json (InstallOrder), nie AppIds: VLC (multimedia) stoi przed 7-Zip (narzędzia), więc
                # VLC startuje pierwszy i dostaje Failed z kodem sieci, a 7-Zip – Skipped bez kodu (spec 5.1: brak sieci na pierwszej
                # uruchomionej kończy serię). Zmiana kolejności w apps.json wymaga zmiany tego przypadku (M2 final review, R19).
                Exit = 1; Outcomes = @{ 'install.vlc' = 'Failed'; 'install.7zip' = 'Skipped' }; WingetState = 'Ready'
                WingetCodes = @{ 'install.vlc' = @('0x8A15000F', '0x8A150107', '0x8A150008', '0x80072EE7'); 'install.7zip' = $null }
                MessageContains = 'brak połączenia z internetem albo ze źródłem winget'
            }
            Check = '$Roots = @(''Registry::HKEY_LOCAL_MACHINE\SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall'', ''Registry::HKEY_LOCAL_MACHINE\SOFTWARE\WOW6432Node\Microsoft\Windows\CurrentVersion\Uninstall'', ''Registry::HKEY_USERS\{UserSid}\Software\Microsoft\Windows\CurrentVersion\Uninstall''); $Found = @(foreach ($Root in $Roots) { if (Test-Path -LiteralPath $Root) { Get-ChildItem -LiteralPath $Root | ForEach-Object { [string]$_.GetValue(''DisplayName'') } | Where-Object { $_ -like ''7-Zip*'' -or $_ -like ''VLC*'' } } }); $Found.Count -eq 0'
            Hypothesis = 'Spec 8 (dowód uczciwego wyniku): VM bez sieci, CLI jako dso (zadanie Harmonogramu) -> pierwsza pozycja z apps.json (VLC) Failed „brak połączenia” z kodem sieci winget (0x8A15000F/0x8A150107/0x8A150008/0x80072EE7), druga (7-Zip) Skipped bez kodu, kod 1; w Uninstall HKLM (64 i 32 bit) ani HKU\<SID dso> nie ma 7-Zip ani VLC.'
        }
        @{
            Name = 'list-install-msi-dso'; Flow = 'AppInstall'; Command = 'list-install'; AppsCatalog = 'C:\dso\fixtures\vm-test-apps.json'; RunAs = 'dso'
            Setup = @(
                "Set-ExecutionPolicy -Scope Process -ExecutionPolicy Bypass -Force; & 'C:\dso\fixtures\New-DsoTestMsi.ps1' -Path 'C:\dso\fixtures\DsoTestMsi.msi' | Out-Null"
                '$Process = Start-Process -FilePath (Join-Path $env:SystemRoot ''System32\msiexec.exe'') -ArgumentList ''/i'', ''C:\dso\fixtures\DsoTestMsi.msi'', ''/qn'', ''/norestart'', ''/l*v'', ''C:\dso\out\DsoTestMsi-install.log'' -Wait -PassThru; if ($Process.ExitCode -ne 0) { throw "msiexec /i zakończył się kodem $($Process.ExitCode)" }'
            )
            Expect = @{ Exit = 0; WingetState = 'Ready'; Installed = @{ 'install.dso-test-msi' = $true; 'install.dso-test-absent' = $false } }
            Check = "Test-Path -LiteralPath 'Registry::HKEY_LOCAL_MACHINE\SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall\{4E3B2A10-7C5D-4F8E-9A1B-2C3D4E5F6A70}'"
            Cleanup = '$Process = Start-Process -FilePath (Join-Path $env:SystemRoot ''System32\msiexec.exe'') -ArgumentList ''/x'', ''{4E3B2A10-7C5D-4F8E-9A1B-2C3D4E5F6A70}'', ''/qn'', ''/norestart'' -Wait -PassThru; if ($Process.ExitCode -notin @(0, 1605)) { throw "msiexec /x zakończył się kodem $($Process.ExitCode)" }'
            Hypothesis = 'Lista testowa przez --apps-catalog (vm-test-apps.json): program z MSI testowego (DisplayName „DSO Test MSI”, wpis ARP w HKLM) ma status „zainstalowana” z reguły match, pozycja bez programu nie; CLI jako dso, winget gotowy (tylko --version), kod 0, MSI zostaje zainstalowany do Cleanup.'
        }
        @{
            Name = 'install-rustdesk-offline-dso'; Flow = 'AppInstall'; Command = 'install-apps'; AppIds = @('install.rustdesk'); RunAs = 'dso'
            Expect = @{
                # Plan RustDesk (spec 3.7): bez sieci zapytanie do API GitHuba pada -> „Nie wykonano – brak połączenia z GitHubem”, bez kodu
                # winget (pozycja nie używa winget); winget --version i tak sprawdzany raz na serię (stan Ready, decyzja planu 3).
                Exit = 1; Outcomes = @{ 'install.rustdesk' = 'Failed' }; WingetCodes = @{ 'install.rustdesk' = $null }; WingetState = 'Ready'
                MessageContains = 'brak połączenia z GitHubem'
            }
            Check = '$Roots = @(''Registry::HKEY_LOCAL_MACHINE\SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall'', ''Registry::HKEY_LOCAL_MACHINE\SOFTWARE\WOW6432Node\Microsoft\Windows\CurrentVersion\Uninstall''); $Found = @(foreach ($Root in $Roots) { if (Test-Path -LiteralPath $Root) { Get-ChildItem -LiteralPath $Root | ForEach-Object { [string]$_.GetValue(''DisplayName'') } | Where-Object { $_ -eq ''RustDesk'' } } }); $Downloads = ''C:\ProgramData\DSOptimizer\downloads''; $NoFiles = (-not (Test-Path -LiteralPath $Downloads)) -or (@(Get-ChildItem -LiteralPath $Downloads -Recurse -Force -File).Count -eq 0); ($Found.Count -eq 0) -and ($null -eq (Get-Service -Name ''RustDesk'' -ErrorAction SilentlyContinue)) -and $NoFiles'
            Hypothesis = 'Plan RustDesk, Task 6 (spec 5, VM bez sieci): CLI jako dso -> install.rustdesk Failed „Nie wykonano – brak połączenia z GitHubem” bez kodu winget, kod 1, winget Ready; nic nie pobrane (C:\ProgramData\DSOptimizer\downloads nieobecny albo bez plików), brak wpisu Uninstall RustDesk w HKLM 64/32 i brak usługi RustDesk.'
        }
        @{
            Name = 'install-rustdesk-steam-offline-dso'; Flow = 'AppInstall'; Command = 'install-apps'; AppIds = @('install.rustdesk', 'install.steam'); RunAs = 'dso'
            Expect = @{
                # Kolejność serii = kolejność apps.json: RustDesk (Zdalna pomoc) przed Steam (Gry). Brak sieci na pierwszej uruchomionej pozycji
                # kończy serię (decyzja planu 4), więc Steam = Skipped bez kodu i winget install nie startuje.
                Exit = 1; Outcomes = @{ 'install.rustdesk' = 'Failed'; 'install.steam' = 'Skipped' }
                WingetCodes = @{ 'install.rustdesk' = $null; 'install.steam' = $null }; WingetState = 'Ready'
                MessageContains = 'brak połączenia z GitHubem'
            }
            Check = '$Roots = @(''Registry::HKEY_LOCAL_MACHINE\SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall'', ''Registry::HKEY_LOCAL_MACHINE\SOFTWARE\WOW6432Node\Microsoft\Windows\CurrentVersion\Uninstall''); $Found = @(foreach ($Root in $Roots) { if (Test-Path -LiteralPath $Root) { Get-ChildItem -LiteralPath $Root | ForEach-Object { [string]$_.GetValue(''DisplayName'') } | Where-Object { $_ -eq ''RustDesk'' } } }); $Downloads = ''C:\ProgramData\DSOptimizer\downloads''; $NoFiles = (-not (Test-Path -LiteralPath $Downloads)) -or (@(Get-ChildItem -LiteralPath $Downloads -Recurse -Force -File).Count -eq 0); $NoSteamLog = -not (Test-Path -Path ''C:\ProgramData\DSOptimizer\logs\winget\Valve.Steam-*.log''); ($Found.Count -eq 0) -and ($null -eq (Get-Service -Name ''RustDesk'' -ErrorAction SilentlyContinue)) -and $NoFiles -and $NoSteamLog'
            Hypothesis = 'Plan RustDesk, Task 6 (seria mieszana bez sieci): RustDesk pierwszy w kolejności apps.json -> Failed „brak połączenia z GitHubem”, Steam Skipped bez kodu (winget install nie wystartował: brak logu Valve.Steam-*.log), kod 1; nic nie pobrane, brak wpisu Uninstall i usługi RustDesk.'
        }
    )
}
