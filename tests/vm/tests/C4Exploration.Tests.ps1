BeforeAll {
    # Wyłącznik: żaden test na mockach nie może dotknąć prawdziwego Hyper-V (patrz Support\NoLiveVm.ps1).
    . (Join-Path $PSScriptRoot 'Support\NoLiveVm.ps1')

    Import-Module (Join-Path $PSScriptRoot '..\modules\VmCommon.psm1') -Force
    Import-Module (Join-Path $PSScriptRoot '..\modules\VmRun.psm1') -Force
    Import-Module (Join-Path $PSScriptRoot '..\modules\VmHost.psm1') -Force
    # Skrypt jest dot-source'owany: przy '.' nie uruchamia przebiegu na VM, tylko definiuje funkcje.
    . (Join-Path $PSScriptRoot '..\explore\Invoke-C4Exploration.ps1')

    $script:C4ScriptPath = Join-Path $PSScriptRoot '..\explore\Invoke-C4Exploration.ps1'

    function script:Add-FakePath {
        # Rejestruje ścieżkę i wszystkich jej przodków (do korzenia dysku) w udawanym systemie plików; domyślny właściciel: Administratorzy.
        param([string]$Path)
        $Current = $Path
        while ($Current -and $Current -notmatch '^[A-Za-z]:\\?$') {
            if (-not $script:Fs.ContainsKey($Current)) { $script:Fs[$Current] = 'S-1-5-32-544' }
            $Current = Split-Path -Parent $Current
        }
    }

    function script:Set-FakeOwner {
        # Zmiana właściciela ścieżki i (opcjonalnie) wszystkiego pod nią.
        param([string]$Path, [string]$Sid, [bool]$Recurse)
        foreach ($Key in @($script:Fs.Keys)) {
            if ($Key -eq $Path -or ($Recurse -and $Key.StartsWith($Path + '\'))) { $script:Fs[$Key] = $Sid }
        }
    }

    function script:Get-GoodPrepareJson {
        '{"meta":{},"probes":{"manualUac":[{"Name":"przygotowanie-reczny-uac","Error":null,"Extra":{"Writes":[{"SubKeyPath":"SOFTWARE\\Classes\\*\\shell\\DSOptimizer.TakeOwnership","Name":"MUIVerb"},{"SubKeyPath":"SOFTWARE\\Classes\\*\\shell\\DSOptimizer.TakeOwnership\\command","Name":""},{"SubKeyPath":"SOFTWARE\\Classes\\Directory\\shell\\DSOptimizer.TakeOwnership","Name":"MUIVerb"},{"SubKeyPath":"SOFTWARE\\Classes\\Directory\\shell\\DSOptimizer.TakeOwnership\\command","Name":""}],"Tree":{"Error":null,"Deny":"d","Seed":{"ExitCode":0,"Error":null}}}}]}}'
    }

    function script:New-FakeCimVolume {
        # Prawdziwy obiekt CimInstance w pamięci (bez połączenia z WMI): mocki Invoke-CimMethod wymagają typu CimInstance w -InputObject.
        $Instance = New-Object Microsoft.Management.Infrastructure.CimInstance 'Win32_EncryptableVolume'
        $Instance.CimInstanceProperties.Add([Microsoft.Management.Infrastructure.CimProperty]::Create('DriveLetter', 'C:', [Microsoft.Management.Infrastructure.CimType]::String, [Microsoft.Management.Infrastructure.CimFlags]::None))
        $Instance
    }

}

Describe 'Sondy C4 (mocki; BitLocker, rejestr, uprawnienia i Hyper-V hosta nietknięte)' {
    BeforeEach {
        $script:Calls = [System.Collections.Generic.List[string]]::new()
        $script:Writes = [System.Collections.Generic.List[object]]::new()
        $script:Fs = @{}
        # Bezpieczniki: każdy cmdlet zmieniający stan lub czytający system rzuca, dopóki test nie nadpisze go zachowaniem.
        Mock Invoke-ExploreNative { throw 'HOST: Invoke-ExploreNative niedozwolone w testach' }
        Mock Get-CimInstance { throw "HOST: Get-CimInstance niedozwolone w testach ($ClassName)" }
        Mock Invoke-CimMethod { throw "HOST: Invoke-CimMethod niedozwolone w testach ($MethodName)" }
        Mock Get-BitLockerVolume { throw 'HOST: Get-BitLockerVolume niedozwolone w testach' }
        Mock Enable-BitLocker { throw 'HOST: Enable-BitLocker niedozwolone w testach' }
        Mock Disable-BitLocker { throw 'HOST: Disable-BitLocker niedozwolone w testach' }
        Mock Suspend-BitLocker { throw 'HOST: Suspend-BitLocker niedozwolone w testach' }
        Mock Get-SmbClientConfiguration { throw 'HOST: Get-SmbClientConfiguration niedozwolone w testach' }
        Mock Set-SmbClientConfiguration { throw 'HOST: Set-SmbClientConfiguration niedozwolone w testach' }
        Mock Get-Acl { throw 'HOST: Get-Acl niedozwolone w testach' }
        Mock Set-Acl { throw 'HOST: Set-Acl niedozwolone w testach' }
        Mock New-Item { throw 'HOST: New-Item niedozwolone w testach' }
        Mock Set-Content { throw 'HOST: Set-Content niedozwolone w testach' }
        Mock Get-Content { throw 'HOST: Get-Content niedozwolone w testach' }
        Mock Remove-Item { throw 'HOST: Remove-Item niedozwolone w testach' }
        Mock Test-Path { throw 'HOST: Test-Path niedozwolone w testach' }
        Mock Get-ChildItem { throw 'HOST: Get-ChildItem niedozwolone w testach' }
        Mock Get-LocalUser { throw 'HOST: Get-LocalUser niedozwolone w testach' }
        Mock Set-ItemProperty { throw 'HOST: Set-ItemProperty niedozwolone w testach' }
        Mock New-ItemProperty { throw 'HOST: New-ItemProperty niedozwolone w testach' }
        Mock Remove-ItemProperty { throw 'HOST: Remove-ItemProperty niedozwolone w testach' }
        Mock Restart-Computer { throw 'HOST: Restart-Computer niedozwolone w testach' }
        Mock Start-Process { throw 'HOST: Start-Process niedozwolone w testach' }
        Mock Start-Sleep { $script:Calls.Add("sleep $Seconds") }
        # Zapis do rejestru tylko przez trzy funkcje opakowujące; w testach rzucają, dopóki test ich nie zamockuje.
        Mock Set-C4RegistryValue { throw 'HOST: Set-C4RegistryValue niedozwolone w testach' }
        Mock Remove-C4RegistryValue { throw 'HOST: Remove-C4RegistryValue niedozwolone w testach' }
        Mock Remove-C4RegistryTree { throw 'HOST: Remove-C4RegistryTree niedozwolone w testach' }
        $script:RegBlock = {
            [pscustomobject]@{ Key = "$Path\$Name"; KeyExists = $true; Exists = $false; Kind = $null; Value = $null; Hex = $null }
        }
        Mock Read-ExploreRegistryValue $script:RegBlock
        Mock -ModuleName ExploreCommon Read-ExploreRegistryValue $script:RegBlock
        Mock Read-ExploreRegistryKey { [pscustomobject]@{ Path = $Path; KeyExists = $true; Values = @(); SubKeys = @() } }
        Mock -ModuleName ExploreCommon Read-ExploreRegistryKey { [pscustomobject]@{ Path = $Path; KeyExists = $true; Values = @(); SubKeys = @() } }
        Mock Read-ExploreRegistryTree { [pscustomobject]@{ Path = $Path; KeyExists = $true; Values = @(); SubKeys = @() } }
        Mock -ModuleName ExploreCommon Read-ExploreRegistryTree { [pscustomobject]@{ Path = $Path; KeyExists = $true; Values = @(); SubKeys = @() } }
    }

    Context 'Ścieżki zapisu do rejestru (Assert-C4WritePath)' {
        It 'przepuszcza tylko drzewo DSOptimizer.TakeOwnership pod pięcioma klasami i klucz polityki LanmanWorkstation' {
            foreach ($Class in '*', 'Directory', 'Drive', 'Folder', 'AllFileSystemObjects') {
                { Assert-C4WritePath -SubKeyPath "SOFTWARE\Classes\$Class\shell\DSOptimizer.TakeOwnership" } | Should -Not -Throw
                { Assert-C4WritePath -SubKeyPath "SOFTWARE\Classes\$Class\shell\DSOptimizer.TakeOwnership\ExtendedSubCommandsKey\Shell\runas\command" } | Should -Not -Throw
            }
            { Assert-C4WritePath -SubKeyPath 'SOFTWARE\Policies\Microsoft\Windows\LanmanWorkstation' } | Should -Not -Throw
        }
        It 'odrzuca cudzy runas, własny klucz o podobnej nazwie, BitLockera i resztę Windows' {
            foreach ($Bad in 'SOFTWARE\Classes\*\shell\runas', 'SOFTWARE\Classes\Directory\shell', 'SOFTWARE\Classes\*\shell\DSOptimizer.TakeOwnershipX',
                'SOFTWARE\Classes\*\shell\DSOptimizer.TakeOwnership.Evil\command', 'SOFTWARE\Classes\txtfile\shell\DSOptimizer.TakeOwnership',
                'SYSTEM\CurrentControlSet\Control\BitLocker', 'SOFTWARE\Microsoft\Windows', 'SOFTWARE\Policies\Microsoft\Windows\LanmanWorkstation\Extra', '') {
                { Assert-C4WritePath -SubKeyPath $Bad } | Should -Throw '*poza dozwolonymi*'
            }
        }
        It 'każda funkcja zapisująca zaczyna się od Assert-C4WritePath (AST), a [Microsoft.Win32.Registry] występuje tylko w nich' {
            $Ast = [System.Management.Automation.Language.Parser]::ParseFile((Resolve-Path $script:C4ScriptPath), [ref]$null, [ref]$null)
            $Functions = $Ast.FindAll({ param($n) $n -is [System.Management.Automation.Language.FunctionDefinitionAst] }, $true)
            foreach ($Name in 'Set-C4RegistryValue', 'Remove-C4RegistryValue', 'Remove-C4RegistryTree') {
                $Function = $Functions | Where-Object Name -eq $Name
                $FirstStatement = $Function.Body.EndBlock.Statements[0]
                $FirstStatement.Extent.Text | Should -Match '^Assert-C4WritePath -SubKeyPath \$SubKeyPath'
            }
            $Uses = $Ast.FindAll({ param($n) $n -is [System.Management.Automation.Language.TypeExpressionAst] -and $n.TypeName.FullName -eq 'Microsoft.Win32.Registry' }, $true)
            @($Uses).Count | Should -BeGreaterThan 0
            foreach ($Use in $Uses) {
                $Parent = $Use.Parent
                while ($null -ne $Parent -and $Parent -isnot [System.Management.Automation.Language.FunctionDefinitionAst]) { $Parent = $Parent.Parent }
                $Parent.Name | Should -BeIn @('Set-C4RegistryValue', 'Remove-C4RegistryValue', 'Remove-C4RegistryTree')
            }
        }
    }

    Context 'Token, właściciel, werdykt' {
        It 'Get-C4TokenInfo zwraca SID użytkownika, grupy i flagi (odczyt tokenu bieżącego procesu)' {
            $Token = Get-C4TokenInfo
            $Token.Error | Should -BeNullOrEmpty
            $Token.UserSid | Should -Match '^S-1-5-'
            @($Token.GroupSids).Count | Should -BeGreaterThan 0
            $Token.HasHighIntegritySid | Should -BeOfType [bool]
            $Token.IsInRoleAdministrator | Should -BeOfType [bool]
        }
        It 'Get-C4OwnerInfo: SID, nazwa i SDDL; brak ścieżki = Exists=false; błąd Get-Acl jako dane' {
            Mock Test-Path { $LiteralPath -ne 'C:\brak' }
            Mock Get-Acl {
                if ($LiteralPath -eq 'C:\zly') { throw 'odmowa dostępu' }
                $Acl = [pscustomobject]@{ Owner = 'BUILTIN\Administrators'; Sddl = 'O:BAG:SY' }
                $Acl | Add-Member -MemberType ScriptMethod -Name GetOwner -Value { param($Type) [pscustomobject]@{ Value = 'S-1-5-32-544' } }
                $Acl
            }
            $Ok = Get-C4OwnerInfo -Path 'C:\ok'
            $Ok.Exists | Should -BeTrue
            $Ok.OwnerSid | Should -Be 'S-1-5-32-544'
            $Ok.Sddl | Should -Be 'O:BAG:SY'
            (Get-C4OwnerInfo -Path 'C:\ok' -NoSddl).Sddl | Should -BeNullOrEmpty
            (Get-C4OwnerInfo -Path 'C:\brak').Exists | Should -BeFalse
            (Get-C4OwnerInfo -Path 'C:\zly').Error | Should -Match 'odmowa'
        }
        It 'werdykt 4c: pełny token = "nie rozstrzygnięte" z powodem; bez pełnego tokenu zmiana właściciela = potwierdzone, brak = brak podniesienia; bez wywołania = nie rozstrzygnięte' {
            $Full = Get-C4ElevationVerdict -TokenFull $true -InvokeAttempted $true -OwnerChanged $true
            $Full.Verdict | Should -Be 'nie rozstrzygnięte'
            $Full.Reason | Should -Match 'pełny token'
            (Get-C4ElevationVerdict -TokenFull $true -InvokeAttempted $true -OwnerChanged $false).Verdict | Should -Be 'nie rozstrzygnięte'
            (Get-C4ElevationVerdict -TokenFull $false -InvokeAttempted $true -OwnerChanged $true).Verdict | Should -Be 'podniesienie potwierdzone'
            (Get-C4ElevationVerdict -TokenFull $false -InvokeAttempted $true -OwnerChanged $false).Verdict | Should -Be 'brak podniesienia'
            (Get-C4ElevationVerdict -TokenFull $false -InvokeAttempted $false -OwnerChanged $false).Verdict | Should -Be 'nie rozstrzygnięte'
        }
    }

    Context 'Menu: linie komend, warianty, rejestracja' {
        It 'linia komendy używa SID *S-1-5-32-544 (nie nazwy grupy), %1 w cudzysłowie; foldery mają /r /d Y i /t' {
            $File = Get-C4MenuCommandLine -Kind File
            $Folder = Get-C4MenuCommandLine -Kind Folder
            $File.Exe | Should -Be 'cmd.exe'
            $File.Full | Should -Be ('cmd.exe ' + $File.Arguments)
            $File.Arguments | Should -Be '/c takeown /f "%1" /a && icacls "%1" /grant *S-1-5-32-544:F /c /l /q'
            $Folder.Arguments | Should -Be '/c takeown /f "%1" /a /r /d Y && icacls "%1" /grant *S-1-5-32-544:F /t /c /l /q'
            ($File.Arguments + $Folder.Arguments) | Should -Not -Match -RegularExpression '(?i)administrators|administratorzy'
        }
        It 'Subkey + Extended na klasie *: MUIVerb, Extended, podklucz ExtendedSubCommandsKey\Shell\runas (MUIVerb, HasLUAShield, command), komenda plikowa; wszystko String' {
            Mock Set-C4RegistryValue { $script:Writes.Add(@{ Path = $SubKeyPath; Name = $Name; Value = $Value; Kind = $Kind }) }
            $Written = @(Set-C4MenuVariant -Class '*' -Structure Subkey -Extended)
            $Root = 'SOFTWARE\Classes\*\shell\DSOptimizer.TakeOwnership'
            $Sub = "$Root\ExtendedSubCommandsKey\Shell\runas"
            $Pairs = @($Writes | ForEach-Object { "$($_.Path)|$($_.Name)" })
            $Pairs | Should -Be @("$Root|MUIVerb", "$Root|Extended", "$Sub|MUIVerb", "$Sub|HasLUAShield", "$Sub\command|")
            @($Writes | ForEach-Object { $_.Kind } | Sort-Object -Unique) | Should -Be @('String')
            ($Writes | Where-Object { $_.Path -eq "$Sub\command" }).Value | Should -Be (Get-C4MenuCommandLine -Kind File).Full
            ($Writes | Where-Object { $_.Name -eq 'Extended' }).Value | Should -Be ''
            @($Written).Count | Should -Be 5
        }
        It 'bez -Extended nie zapisuje Extended; AppliesTo trafia na klucz nadrzędny; Directory dostaje komendę folderową' {
            Mock Set-C4RegistryValue { $script:Writes.Add(@{ Path = $SubKeyPath; Name = $Name; Value = $Value; Kind = $Kind }) }
            $null = Set-C4MenuVariant -Class 'Directory' -Structure Subkey -AppliesTo 'NOT System.ItemPathDisplay:="C:\Windows"'
            $Root = 'SOFTWARE\Classes\Directory\shell\DSOptimizer.TakeOwnership'
            @($Writes | Where-Object { $_.Name -eq 'Extended' }).Count | Should -Be 0
            ($Writes | Where-Object { $_.Path -eq $Root -and $_.Name -eq 'AppliesTo' }).Value | Should -Be 'NOT System.ItemPathDisplay:="C:\Windows"'
            ($Writes | Where-Object { $_.Path -like '*\command' }).Value | Should -Be (Get-C4MenuCommandLine -Kind Folder).Full
        }
        It 'ValuePath dopisuje wartość ExtendedSubCommandsKey (ścieżka względem HKCR do własnego podklucza); Plain ma tylko MUIVerb i command, bez podmenu' {
            Mock Set-C4RegistryValue { $script:Writes.Add(@{ Path = $SubKeyPath; Name = $Name; Value = $Value; Kind = $Kind }) }
            $null = Set-C4MenuVariant -Class '*' -Structure ValuePath
            ($Writes | Where-Object { $_.Name -eq 'ExtendedSubCommandsKey' }).Value | Should -Be '*\shell\DSOptimizer.TakeOwnership\ExtendedSubCommandsKey'
            $Writes.Clear()
            $null = Set-C4MenuVariant -Class 'Drive' -Structure Plain
            @($Writes | ForEach-Object { "$($_.Path)|$($_.Name)" }) | Should -Be @('SOFTWARE\Classes\Drive\shell\DSOptimizer.TakeOwnership|MUIVerb', 'SOFTWARE\Classes\Drive\shell\DSOptimizer.TakeOwnership\command|')
        }
        It 'Remove-C4MenuVariant kasuje tylko nasze drzewo' {
            Mock Remove-C4RegistryTree { $script:Calls.Add($SubKeyPath) }
            Remove-C4MenuVariant -Class 'Directory'
            $Calls | Should -Be @('SOFTWARE\Classes\Directory\shell\DSOptimizer.TakeOwnership')
        }
        It 'plan wariantów: A, B, C z research-aqs-policy.md dosłownie, kontrola pozytywna System.ItemName, Extended z i bez dla obu struktur, klasy Folder/AllFileSystemObjects/Drive' {
            $Plan = @(Get-C4MenuVariantPlan)
            ($Plan | Where-Object Id -eq 'aqs-a-plain').AppliesTo | Should -Be 'NOT System.ItemPathDisplay:="C:\Windows"'
            ($Plan | Where-Object Id -eq 'aqs-b-plain').AppliesTo | Should -Be 'NOT (System.ItemPathDisplay:="C:\Windows" OR System.ItemPathDisplay:~<"C:\Windows\" OR System.ItemPathDisplay:="C:\")'
            ($Plan | Where-Object Id -eq 'aqs-c-subkey').AppliesTo | Should -Be 'NOT System.ParsingPath:="C:\Windows"'
            ($Plan | Where-Object Id -eq 'aqs-kontrola-plain').AppliesTo | Should -Match '^System\.ItemName:'
            foreach ($Structure in 'Subkey', 'ValuePath') {
                foreach ($Extended in $true, $false) {
                    @($Plan | Where-Object { $_.Group -eq '4a' -and $_.Structure -eq $Structure -and $_.Extended -eq $Extended }).Count | Should -Be 1
                }
            }
            foreach ($Class in 'Folder', 'AllFileSystemObjects', 'Drive') { @($Plan | Where-Object { $_.Classes -contains $Class }).Count | Should -BeGreaterThan 0 }
            @($Plan | Where-Object { $_.AppliesTo } | ForEach-Object { $_.Classes }) | Should -Not -Contain '*'
            @($Plan | ForEach-Object { $_.Id } | Sort-Object -Unique).Count | Should -Be $Plan.Count
        }
        It 'skrypt wyliczający verby: poprawna składnia, Shell.Application NameSpace/ParseName/Verbs, SHChangeNotify SHCNE_ASSOCCHANGED, korzeń dysku przez NameSpace(17)' {
            foreach ($Text in (Get-C4ListVerbsScript), (Get-C4InvokeVerbScript)) {
                $Errors = $null
                [void][System.Management.Automation.Language.Parser]::ParseInput($Text, [ref]$null, [ref]$Errors)
                @($Errors).Count | Should -Be 0
            }
            $List = Get-C4ListVerbsScript
            $List | Should -Match 'Shell\.Application'
            $List | Should -Match 'ParseName'
            $List | Should -Match '\.Verbs\(\)'
            $List | Should -Match 'SHChangeNotify\(0x08000000'
            $List | Should -Match 'NameSpace\(17\)'
            (Get-C4InvokeVerbScript) | Should -Match 'InvokeVerb\(\$VerbName\)'
        }
        It 'skrypt potomny z PRAWDZIWYM parsowaniem argumentów i JSON (powershell.exe -File ... -DryRun, bez COM): każdy cel osobno, surowe ścieżki ze spacjami i znakami specjalnymi' {
            $Dir = Join-Path $TestDrive 'child'
            $null = [System.IO.Directory]::CreateDirectory($Dir)
            $Script = Join-Path $Dir 'list-verbs.ps1'
            $TargetsFile = Join-Path $Dir 'targets.json'
            $ResultFile = Join-Path $Dir 'result.json'
            [System.IO.File]::WriteAllText($Script, (Get-C4ListVerbsScript), (New-Object System.Text.UTF8Encoding($true)))
            $Targets = @(Get-C4VerbTargets)
            [System.IO.File]::WriteAllText($TargetsFile, (ConvertTo-Json -InputObject @($Targets)), (New-Object System.Text.UTF8Encoding($true)))
            $Psi = New-Object System.Diagnostics.ProcessStartInfo
            $Psi.FileName = 'powershell.exe'
            $Psi.Arguments = "-NoProfile -NonInteractive -ExecutionPolicy Bypass -File `"$Script`" -TargetsFile `"$TargetsFile`" -ResultFile `"$ResultFile`" -DryRun"
            $Psi.UseShellExecute = $false
            $Psi.CreateNoWindow = $true
            $Process = [System.Diagnostics.Process]::Start($Psi)
            $null = $Process.WaitForExit(60000)
            $Process.ExitCode | Should -Be 0
            $Result = [System.IO.File]::ReadAllText($ResultFile, [System.Text.Encoding]::UTF8) | ConvertFrom-Json
            $Result.Error | Should -BeNullOrEmpty
            @($Result.Targets).Count | Should -Be 7
            @($Result.Targets | ForEach-Object { $_.Path }) | Should -Be $Targets
        }
        It 'Invoke-C4VerbListing: osobny powershell.exe z -File, -Notify tylko na żądanie, cele zapisane jako JSON, wynik odczytany z pliku' {
            Mock New-Item {}
            Mock Set-Content { $script:Calls.Add("set $LiteralPath") ; $script:LastContent = $Value }
            Mock Test-Path { $true }
            Mock Remove-Item { $script:Calls.Add("rm $LiteralPath") }
            Mock Get-Content { '{"ProcessId":7,"Targets":[{"Path":"C:\\","Found":true,"VerbCount":2,"VerbNames":["a","b"]}]}' }
            Mock Invoke-ExploreNative { $script:Native = @{ FilePath = $FilePath; Arguments = @($Arguments) }; [pscustomobject]@{ ExitCode = 0; Stdout = 'x'; Stderr = ''; Error = $null } }
            $Child = Invoke-C4VerbListing -Targets @('C:\', 'C:\Windows') -Notify
            $Native.FilePath | Should -Be 'powershell.exe'
            ($Native.Arguments -join ' ') | Should -Match '-File C:\\DsoProbe\\list-verbs\.ps1'
            $Native.Arguments | Should -Contain '-Notify'
            $Native.Arguments | Should -Contain '-ResultFile'
            $Child.Result.Targets[0].VerbCount | Should -Be 2
            $Child.Native.ExitCode | Should -Be 0
            $Calls | Should -Contain 'rm C:\DsoProbe\list-verbs-result.json'
            $null = Invoke-C4VerbListing -Targets @('C:\')
            $Native.Arguments | Should -Not -Contain '-Notify'
        }
        It 'Invoke-C4ChildScript: brak pliku wyniku to dane (ReadError), nie wyjątek' {
            Mock New-Item {}
            Mock Set-Content {}
            Mock Test-Path { $false }
            Mock Invoke-ExploreNative { [pscustomobject]@{ ExitCode = 1; Stdout = ''; Stderr = 'blad'; Error = $null } }
            $Child = Invoke-C4ChildScript -ScriptText 'x' -ScriptName 'a.ps1' -ResultName 'a.json'
            $Child.Result | Should -BeNullOrEmpty
            $Child.ReadError | Should -Match 'brak pliku wyniku'
            $Child.Native.ExitCode | Should -Be 1
        }
        It 'Invoke-C4ChildScript odrzuca nazwy ze ścieżką (nic poza C:\DsoProbe)' {
            { Invoke-C4ChildScript -ScriptText 'x' -ScriptName '..\a.ps1' -ResultName 'a.json' } | Should -Throw
            { Invoke-C4ChildScript -ScriptText 'x' -ScriptName 'a.ps1' -ResultName 'C:\a.json' } | Should -Throw
        }
    }

    Context 'Drzewo testowe i właściciel wyjściowy' {
        It 'Get-C4SeedOwnerSid: SID konta dso albo SYSTEM, gdy konta nie ma' {
            Mock Get-LocalUser { [pscustomobject]@{ SID = [pscustomobject]@{ Value = 'S-1-5-21-1-2-3-1001' } } }
            (Get-C4SeedOwnerSid).Sid | Should -Be 'S-1-5-21-1-2-3-1001'
            Mock Get-LocalUser { throw 'brak' }
            $Fallback = Get-C4SeedOwnerSid
            $Fallback.Sid | Should -Be 'S-1-5-18'
            $Fallback.Source | Should -Match 'SYSTEM'
        }
        It 'New-C4TestTree: pliki i podfolder, icacls /setowner PRZED Deny, Deny ListDirectory dla bieżącego użytkownika na podfolderze' {
            Mock New-Item { $script:Calls.Add("mkdir $Path") }
            Mock Set-Content { $script:Calls.Add("file $LiteralPath") }
            Mock Invoke-ExploreNative { $script:Calls.Add("native $FilePath $($Arguments -join ' ')"); [pscustomobject]@{ ExitCode = 0; Stdout = ''; Stderr = ''; Error = $null } }
            Mock Get-Acl { New-Object System.Security.AccessControl.DirectorySecurity }
            Mock Set-Acl { $script:SetAclPath = $LiteralPath; $script:SetAclObject = $AclObject; $script:Calls.Add("setacl $LiteralPath") }
            $Tree = New-C4TestTree -Root 'C:\DsoTest' -SeedOwnerSid 'S-1-5-18'
            $Tree.Error | Should -BeNullOrEmpty
            $Calls | Should -Contain 'mkdir C:\DsoTest\folder testowy\podfolder bez list'
            $Calls | Should -Contain 'file C:\DsoTest\plik testowy.txt'
            $Calls | Should -Contain 'file C:\DsoTest\folder testowy\podfolder bez list\ukryty.txt'
            $Calls | Should -Contain 'native icacls.exe C:\DsoTest /setowner *S-1-5-18 /t /c /l /q'
            ($Calls.IndexOf('native icacls.exe C:\DsoTest /setowner *S-1-5-18 /t /c /l /q')) | Should -BeLessThan ($Calls.IndexOf('setacl C:\DsoTest\folder testowy\podfolder bez list'))
            $script:SetAclPath | Should -Be 'C:\DsoTest\folder testowy\podfolder bez list'
            $Rules = @($script:SetAclObject.GetAccessRules($true, $false, [System.Security.Principal.SecurityIdentifier]))
            $Rules.Count | Should -Be 1
            $Rules[0].AccessControlType | Should -Be ([System.Security.AccessControl.AccessControlType]::Deny)
            ([int]$Rules[0].FileSystemRights -band 1) | Should -Be 1
            $Rules[0].IdentityReference.Value | Should -Be ([System.Security.Principal.WindowsIdentity]::GetCurrent().User.Value)
        }
        It 'New-C4TestTree: błąd jest zapisany w polu Error, nie rzucany' {
            Mock New-Item { throw 'dysk pełny' }
            (New-C4TestTree -Root 'C:\DsoTest' -SeedOwnerSid 'S-1-5-18').Error | Should -Match 'dysk pełny'
        }
        It 'Get-C4TreeSnapshot i Compare-C4Snapshot: różnice właściciela, nowe i zniknięte ścieżki; błędy wyliczania jako dane' {
            Mock Test-Path { $true }
            $script:Owners = @{ 'C:\R' = 'S-1'; 'C:\R\a' = 'S-1'; 'C:\R\b' = 'S-1' }
            Mock Get-ChildItem { @([pscustomobject]@{ FullName = 'C:\R\a' }, [pscustomobject]@{ FullName = 'C:\R\b' }) }
            Mock Get-Acl {
                $Owner = $script:Owners[[string]$LiteralPath]
                $Acl = [pscustomobject]@{ Owner = 'x'; Sddl = 's' ; Sid = $Owner }
                $Acl | Add-Member -MemberType ScriptMethod -Name GetOwner -Value { param($Type) [pscustomobject]@{ Value = $this.Sid } }
                $Acl
            }
            $Before = Get-C4TreeSnapshot -Root 'C:\R'
            $Before.Count | Should -Be 3
            $script:Owners['C:\R\b'] = 'S-1-5-32-544'
            $Owners['C:\R\c'] = 'S-1'
            Mock Get-ChildItem { @([pscustomobject]@{ FullName = 'C:\R\a' }, [pscustomobject]@{ FullName = 'C:\R\b' }, [pscustomobject]@{ FullName = 'C:\R\c' }) }
            $After = Get-C4TreeSnapshot -Root 'C:\R'
            @(Compare-C4Snapshot -Before $Before -After $After) | Should -Be @('C:\R\b', 'C:\R\c')
            @(Compare-C4Snapshot -Before $After -After $Before) | Should -Be @('C:\R\b', 'C:\R\c')
        }
    }

    Context 'Sondy BitLockera (odczyt) i bramka edycji' {
        It 'sonda 1: zrzut klucza BitLocker i jawnie PreventDeviceEncryption (Exists osobno od Value)' {
            $Steps = @(Invoke-C4BitLockerPolicyProbe)
            $Steps[0].RegistryKeys[0].Path | Should -Be 'HKLM\SYSTEM\CurrentControlSet\Control\BitLocker'
            $Steps[0].Registry[0].Key | Should -Be 'HKLM\SYSTEM\CurrentControlSet\Control\BitLocker\PreventDeviceEncryption'
            $Steps[0].Registry[0].Exists | Should -BeFalse
        }
        It 'sonda 2 na Home: brak klasy WMI i brak cmdletu to DANE (Ok=false, komunikat), wszystkie kroki zapisane, żadnego zapisu' {
            Mock Test-Path { $false }
            Mock Get-CimInstance { throw 'Invalid namespace' }
            Mock Get-BitLockerVolume { throw 'The term ''Get-BitLockerVolume'' is not recognized' }
            $Steps = @(Invoke-C4BitLockerReadProbe)
            @($Steps | ForEach-Object { $_.Name }) | Should -Be @('bitlocker-modul', 'wmi-win32-encryptablevolume', 'wmi-getconversionstatus-protectionstatus', 'get-bitlockervolume')
            ($Steps | Where-Object Name -eq 'bitlocker-modul').Extra.ModuleDirectoryExists | Should -BeFalse
            $Volume = ($Steps | Where-Object Name -eq 'wmi-win32-encryptablevolume').Extra.Volume
            $Volume.Ok | Should -BeFalse
            $Volume.Error | Should -Match 'Invalid namespace'
            ($Steps | Where-Object Name -eq 'wmi-getconversionstatus-protectionstatus').Extra.Sample.Ok | Should -BeFalse
            ($Steps | Where-Object Name -eq 'get-bitlockervolume').Extra.BitLockerVolume.Error | Should -Match 'not recognized'
            Should -Invoke Invoke-CimMethod -Times 0 -Exactly
        }
        It 'sonda 2 na Pro: obiekt WMI, GetConversionStatus i GetProtectionStatus (liczby), Get-BitLockerVolume z enumami jako [string] i [int]' {
            Mock Test-Path { $true }
            $Vol = New-FakeCimVolume
            Mock Get-CimInstance { $script:Calls.Add("cim $ClassName $Filter"); $Vol }
            Mock Invoke-CimMethod {
                $script:Calls.Add("method $MethodName")
                if ($MethodName -eq 'GetConversionStatus') { [pscustomobject]@{ ReturnValue = [uint32]0; ConversionStatus = [uint32]0; EncryptionPercentage = [uint32]0 } }
                elseif ($MethodName -eq 'GetProtectionStatus') { [pscustomobject]@{ ReturnValue = [uint32]0; ProtectionStatus = [uint32]0 } }
                else { throw "nieoczekiwana metoda $MethodName" }
            }
            Mock Get-BitLockerVolume { [pscustomobject]@{ MountPoint = 'C:'; VolumeStatus = 'FullyDecrypted'; ProtectionStatus = 'Off'; EncryptionPercentage = 0; EncryptionMethod = 'None'; KeyProtector = @() } }
            $Steps = @(Invoke-C4BitLockerReadProbe)
            $Calls | Should -Contain "cim Win32_EncryptableVolume DriveLetter='C:'"
            $Calls | Should -Contain 'method GetConversionStatus'
            $Calls | Should -Contain 'method GetProtectionStatus'
            $Props = ($Steps | Where-Object Name -eq 'wmi-win32-encryptablevolume').Extra.Volume.Items[0]
            $Props.DriveLetter | Should -Be 'C:'
            $Sample = ($Steps | Where-Object Name -eq 'wmi-getconversionstatus-protectionstatus').Extra.Sample
            $Sample.Ok | Should -BeTrue
            $Sample.ConversionStatus | Should -Be 0
            $Sample.ProtectionStatus | Should -Be 0
            @($Sample.ConversionRaw | ForEach-Object { $_.Name }) | Should -Contain 'ConversionStatus'
            @($Sample.ConversionRaw | ForEach-Object { $_.Name }) | Should -Contain 'EncryptionPercentage'
            ($Sample.ConversionRaw | Where-Object Name -eq 'ReturnValue').Type | Should -Match 'UInt32'
            $Bl = ($Steps | Where-Object Name -eq 'get-bitlockervolume').Extra.BitLockerVolume.Items[0]
            $Bl.VolumeStatus.String | Should -Be 'FullyDecrypted'
            $Bl.KeyProtectorCount | Should -Be 0
        }
        It 'sonda 3: zrzut shell pięciu klas' {
            $Steps = @(Invoke-C4ShellClassesProbe)
            @($Steps[0].RegistryKeys | ForEach-Object { $_.Path }) | Should -Be @(
                'HKLM\SOFTWARE\Classes\*\shell', 'HKLM\SOFTWARE\Classes\Directory\shell', 'HKLM\SOFTWARE\Classes\Drive\shell',
                'HKLM\SOFTWARE\Classes\Folder\shell', 'HKLM\SOFTWARE\Classes\AllFileSystemObjects\shell')
        }
    }

    Context 'Polityka guest logons (7)' {
        It 'odczyt przed, zapis DWORD 1 do dozwolonej ścieżki polityki, odczyt po, usunięcie wartości, odczyt po usunięciu; Set-SmbClientConfiguration nigdy' {
            $script:Guest = $false
            Mock Get-SmbClientConfiguration { $script:Calls.Add('get'); [pscustomobject]@{ EnableInsecureGuestLogons = $script:Guest } }
            Mock Set-C4RegistryValue { $script:Calls.Add("set $SubKeyPath $Name $Value $Kind"); $script:Guest = $true }
            Mock Remove-C4RegistryValue { $script:Calls.Add("rm $SubKeyPath $Name"); $script:Guest = $false }
            $Steps = @(Invoke-C4GuestPolicyProbe)
            @($Steps | ForEach-Object { $_.Name }) | Should -Be @('polityka-przed', 'polityka-zapis-dword-1', 'polityka-po-usunieciu-wartosci')
            $Calls | Should -Be @('get', 'set SOFTWARE\Policies\Microsoft\Windows\LanmanWorkstation AllowInsecureGuestAuth 1 DWord', 'get', 'rm SOFTWARE\Policies\Microsoft\Windows\LanmanWorkstation AllowInsecureGuestAuth', 'get')
            $Steps[0].Extra.Smb.EnableInsecureGuestLogons | Should -BeFalse
            $Steps[1].Extra.Smb.EnableInsecureGuestLogons | Should -BeTrue
            $Steps[2].Extra.Smb.EnableInsecureGuestLogons | Should -BeFalse
            foreach ($Step in $Steps) {
                $Step.RegistryKeys[0].Path | Should -Be 'HKLM\SOFTWARE\Policies\Microsoft\Windows\LanmanWorkstation'
                ($Step.Registry | ForEach-Object { $_.Key }) -join '|' | Should -Match 'Windows\\LanmanWorkstation\\AllowInsecureGuestAuth'
            }
            Should -Invoke Set-SmbClientConfiguration -Times 0 -Exactly
        }
        It 'błąd odczytu SMB jest daną (Ok=false), zapis polityki nadal się odbywa' {
            Mock Get-SmbClientConfiguration { throw 'brak usługi' }
            Mock Set-C4RegistryValue { $script:Calls.Add('set') }
            Mock Remove-C4RegistryValue { $script:Calls.Add('rm') }
            $Steps = @(Invoke-C4GuestPolicyProbe)
            $Steps[0].Extra.Smb.Ok | Should -BeFalse
            $Steps[0].Extra.Smb.Error | Should -Match 'brak usługi'
            $Calls | Should -Be @('set', 'rm')
        }
    }

    Context 'Udawany system plików: takeown, icacls, menu 4c, linia komendy 6' {
        BeforeEach {
            $script:AddedRules = [System.Collections.Generic.List[object]]::new()
            Mock New-Item { Add-FakePath -Path $Path }
            Mock Set-Content { Add-FakePath -Path ([string]$LiteralPath) }
            Mock Test-Path { $script:Fs.ContainsKey([string]$LiteralPath) }
            Mock Get-ChildItem {
                $Prefix = ([string]$LiteralPath) + '\'
                @($script:Fs.Keys | Where-Object { $_.StartsWith($Prefix) } | Sort-Object | ForEach-Object { [pscustomobject]@{ FullName = $_ } })
            }
            Mock Get-Acl {
                $Acl = [pscustomobject]@{ Owner = 'x'; Sddl = 'sddl'; Sid = $script:Fs[[string]$LiteralPath] }
                $Acl | Add-Member -MemberType ScriptMethod -Name GetOwner -Value { param($Type) [pscustomobject]@{ Value = $this.Sid } }
                $Acl | Add-Member -MemberType ScriptMethod -Name AddAccessRule -Value { param($Rule) $script:AddedRules.Add($Rule) }
                $Granted = @()
                if ($script:Fs[[string]$LiteralPath] -eq 'S-1-5-32-544') {
                    $Granted = @(New-Object System.Security.AccessControl.FileSystemAccessRule((New-Object System.Security.Principal.SecurityIdentifier('S-1-5-32-544')), [System.Security.AccessControl.FileSystemRights]::FullControl, [System.Security.AccessControl.AccessControlType]::Allow))
                }
                $Acl | Add-Member -MemberType NoteProperty -Name Access -Value $Granted
                $Acl
            }
            Mock Set-Acl {}
            Mock Get-LocalUser { [pscustomobject]@{ SID = [pscustomobject]@{ Value = 'S-1-5-21-1-1-1-1001' } } }
            Mock Invoke-ExploreNative {
                $Line = if ($PesterBoundParameters.ContainsKey('RawArguments')) { "$FilePath $RawArguments" } else { "$FilePath $($Arguments -join ' ')" }
                $script:Calls.Add($Line)
                $Code = 0
                if ($FilePath -eq 'icacls.exe' -and $Arguments[1] -eq '/setowner') {
                    Set-FakeOwner -Path $Arguments[0] -Sid $Arguments[2].TrimStart('*') -Recurse ($Arguments -contains '/t')
                }
                elseif ($FilePath -eq 'takeown.exe') {
                    Set-FakeOwner -Path $Arguments[1] -Sid 'S-1-5-32-544' -Recurse ($Arguments -contains '/r')
                }
                elseif ($FilePath -eq 'cmd.exe') {
                    if ($RawArguments -match '^/c takeown /f "([^"]+)" /a( /r /d Y)? ') {
                        $Target = $Matches[1]
                        if ($Target -match '%') { $Code = 1 }
                        else { Set-FakeOwner -Path $Target -Sid 'S-1-5-32-544' -Recurse ([bool]$Matches[2]) }
                    }
                    else { $Code = 1 }
                }
                [pscustomobject]@{ ExitCode = $Code; Stdout = ''; Stderr = ''; Error = $null }
            }
        }
        It 'sonda 5: takeown /f <folder> /a /r /d Y i icacls /setowner *S-1-5-32-544 /t /c /l na osobnych drzewach; właściciel przed (wyjściowy) i po (Administratorzy), także w podfolderze bez List Folder' {
            $Steps = @(Invoke-C4TakeownProbe)
            @($Steps | ForEach-Object { $_.Name }) | Should -Be @('przygotowanie-takeown', 'takeown', 'przygotowanie-icacls-setowner', 'icacls-setowner')
            $Calls | Should -Contain 'takeown.exe /f C:\DsoTest\p5-takeown\folder testowy /a /r /d Y'
            $Calls | Should -Contain 'icacls.exe C:\DsoTest\p5-icacls\folder testowy /setowner *S-1-5-32-544 /t /c /l'
            foreach ($Id in 'takeown', 'icacls-setowner') {
                $Step = $Steps | Where-Object Name -eq $Id
                @($Step.Extra.OwnersBefore | ForEach-Object { $_.OwnerSid } | Sort-Object -Unique) | Should -Be @('S-1-5-21-1-1-1-1001')
                @($Step.Extra.OwnersAfter | ForEach-Object { $_.OwnerSid } | Sort-Object -Unique) | Should -Be @('S-1-5-32-544')
                @($Step.Extra.OwnersAfter | Where-Object { $_.Path -like '*ukryty.txt' }).Count | Should -Be 1
                $Step.ExitCode | Should -Be 0
            }
            ($Steps[0].Extra.Tree.DeniedFolder) | Should -Be 'C:\DsoTest\p5-takeown\folder testowy\podfolder bez list'
        }
        It 'sonda 6: linia z menu przez cmd.exe /c z surową linią argumentów; %1 podstawione dosłownie; właściwy cel zmieniony, nic obcego; nazwa z % nie zmienia nic (to dana, nie błąd)' {
            $Steps = @(Invoke-C4CommandLineProbe)
            $Steps[0].Name | Should -Be 'przygotowanie-cmdline'
            @($Steps | Where-Object { $_.Name -like 'cmdline-*' }).Count | Should -Be 14
            $Space = $Steps | Where-Object Name -eq 'cmdline-plik-spacje'
            $Space.Command | Should -Be 'cmd.exe /c takeown /f "C:\DsoTest\p6\spacja w nazwie.txt" /a && icacls "C:\DsoTest\p6\spacja w nazwie.txt" /grant *S-1-5-32-544:F /c /l /q'
            $Calls | Should -Contain 'cmd.exe /c takeown /f "C:\DsoTest\p6\spacja w nazwie.txt" /a && icacls "C:\DsoTest\p6\spacja w nazwie.txt" /grant *S-1-5-32-544:F /c /l /q'
            $Space.Extra.IntendedChanged | Should -BeTrue
            $Space.Extra.OnlyIntendedChanged | Should -BeTrue
            @($Space.Extra.StrayChangedPaths).Count | Should -Be 0
            $Space.Extra.TargetOwnerSidAfter | Should -Be 'S-1-5-32-544'
            $Polish = $Steps | Where-Object Name -eq 'cmdline-plik-polskie'
            $Polish.Command | Should -Match 'zażółć gęślą jaźń\.txt'
            $Polish.Extra.OnlyIntendedChanged | Should -BeTrue
            $Folder = $Steps | Where-Object Name -eq 'cmdline-folder-daszek'
            $Folder.Command | Should -Match '/a /r /d Y && icacls'
            @($Folder.Extra.ChangedPaths) | Should -Be @('C:\DsoTest\p6\daszek ^ test (folder)', 'C:\DsoTest\p6\daszek ^ test (folder)\dziecko.txt')
            $Folder.Extra.OnlyIntendedChanged | Should -BeTrue
            $Percent = $Steps | Where-Object Name -eq 'cmdline-plik-procent'
            $Percent.ExitCode | Should -Be 1
            $Percent.Extra.IntendedChanged | Should -BeFalse
            @($Percent.Extra.ChangedPaths).Count | Should -Be 0
            foreach ($Id in 'ampersand', 'wykrzyknik') { ($Steps | Where-Object Name -eq "cmdline-plik-$Id").Extra.OnlyIntendedChanged | Should -BeTrue }
        }
        It 'sonda 6: stray zmiana innego pliku jest wykryta i oznaczona (OnlyIntendedChanged=false)' {
            Mock Invoke-ExploreNative {
                if ($FilePath -eq 'cmd.exe') {
                    $Matches = $null
                    $null = $RawArguments -match '^/c takeown /f "([^"]+)"'
                    Set-FakeOwner -Path $Matches[1] -Sid 'S-1-5-32-544' -Recurse $false
                    Set-FakeOwner -Path 'C:\DsoTest\p6\amper & koniec.txt' -Sid 'S-1-5-32-544' -Recurse $false
                }
                elseif ($FilePath -eq 'icacls.exe' -and $Arguments[1] -eq '/setowner') { Set-FakeOwner -Path $Arguments[0] -Sid $Arguments[2].TrimStart('*') -Recurse $true }
                [pscustomobject]@{ ExitCode = 0; Stdout = ''; Stderr = ''; Error = $null }
            }
            $Steps = @(Invoke-C4CommandLineProbe)
            $Step = $Steps | Where-Object Name -eq 'cmdline-plik-spacje'
            $Step.Extra.OnlyIntendedChanged | Should -BeFalse
            @($Step.Extra.StrayChangedPaths) | Should -Be @('C:\DsoTest\p6\amper & koniec.txt')
        }
        It 'nazwy przypadków obejmują spacje, polskie znaki, %, &, ^, ! (pliki i foldery)' {
            $Cases = @(Get-C4CommandCases -Root 'C:\DsoTest\p6')
            $Cases.Count | Should -Be 14
            $All = ($Cases | ForEach-Object { $_.Path }) -join '|'
            foreach ($Fragment in ' w ', 'zażółć', '%PATH%', ' & ', ' ^ ', 'wykrzyknik!') { $All | Should -Match ([regex]::Escape($Fragment)) }
            foreach ($Case in $Cases) { $Case.Path | Should -BeLike 'C:\DsoTest\p6\*' }
        }
        It 'menu 4: każdy wariant planu ma krok, każdy listing w osobnym powershell.exe z -Notify, klucze sprzątnięte, wynik 4c "nie rozstrzygnięte" przy pełnym tokenie' {
            Mock Set-C4RegistryValue { $script:Writes.Add(@{ Path = $SubKeyPath; Name = $Name; Value = $Value }) }
            Mock Remove-C4RegistryTree { $script:Calls.Add("rmtree $SubKeyPath") }
            Mock Get-Content { '{"ProcessId":1,"Invoked":true,"Targets":[{"Path":"C:\\","Found":true,"VerbCount":0,"VerbNames":[]}]}' }
            Mock Remove-Item {}
            Add-FakePath -Path 'C:\DsoProbe\list-verbs-result.json'
            Add-FakePath -Path 'C:\DsoProbe\invoke-verb-result.json'
            Mock Get-C4TokenInfo { [pscustomobject]@{ UserName = 'VM\Administrator'; UserSid = 'S-1-5-21-1-1-1-500'; IsInRoleAdministrator = $true; HasHighIntegritySid = $true; IntegritySids = @('S-1-16-12288'); GroupSids = @(); ImpersonationLevel = 'None'; Error = $null } }
            $Steps = @(Invoke-C4MenuProbe)
            $Plan = @(Get-C4MenuVariantPlan)
            foreach ($Variant in $Plan) { @($Steps | Where-Object Name -eq "lista-$($Variant.Id)").Count | Should -Be 1 }
            $Listing = @($Calls | Where-Object { $_ -like 'powershell.exe *list-verbs.ps1*' })
            $Listing.Count | Should -Be $Plan.Count
            foreach ($Line in $Listing) { $Line | Should -Match '-Notify' }
            $Step = $Steps | Where-Object Name -eq 'lista-aqs-b-subkey'
            $Step.Command | Should -Match 'ItemPathDisplay'
            $Step.Extra.Variant.AppliesTo | Should -Match '~<"C:\\Windows\\"'
            ($Step.Extra.Writes | Where-Object { $_.Name -eq 'AppliesTo' }).Value | Should -Match 'OR System.ItemPathDisplay:="C:\\"\)$'
            ($Step.Extra.Listing.Targets | Where-Object Path -eq 'C:\').Found | Should -BeTrue
            foreach ($Target in 'C:\DsoTest\plik testowy.txt', 'C:\DsoTest\folder testowy', 'C:\', 'C:\Windows', 'C:\Windows\System32') { Get-C4VerbTargets | Should -Contain $Target }
            ($Steps | Where-Object Name -eq 'sprzatanie-menu-koncowe') | Should -Not -BeNullOrEmpty
            @($Calls | Where-Object { $_ -like 'rmtree *' } | Sort-Object -Unique).Count | Should -Be 5
            $Verdict = $Steps | Where-Object Name -eq 'invokeverb-werdykt'
            @($Verdict.Extra.Verdicts).Count | Should -Be 5
            @($Verdict.Extra.Verdicts | ForEach-Object { $_.Verdict } | Sort-Object -Unique) | Should -Be @('nie rozstrzygnięte')
            ($Verdict.Extra.Token.HasHighIntegritySid) | Should -BeTrue
            @($Writes | Where-Object { $_.Path -notlike 'SOFTWARE\Classes\*\shell\DSOptimizer.TakeOwnership*' }).Count | Should -Be 0
            Should -Invoke Enable-BitLocker -Times 0 -Exactly
            Should -Invoke Set-SmbClientConfiguration -Times 0 -Exactly
        }

        It 'sonda 6: po komendzie zapisany jest ACL celu (czy SID Administratorów ma Allow FullControl po icacls /grant), obok właściciela' {
            $Steps = @(Invoke-C4CommandLineProbe)
            $Space = $Steps | Where-Object Name -eq 'cmdline-plik-spacje'
            $Space.Extra.TargetAclAfter.AdministratorsFullControl | Should -BeTrue
            @($Space.Extra.TargetAclAfter.Aces | Where-Object { $_.Sid -eq 'S-1-5-32-544' -and $_.Type -eq 'Allow' }).Count | Should -Be 1
            $Space.Extra.TargetAclAfter.Aces[0].RightsValue | Should -Be 2032127
            $Folder = $Steps | Where-Object Name -eq 'cmdline-folder-spacje'
            @($Folder.Extra.IntendedAclsAfter).Count | Should -Be 2
            $Percent = $Steps | Where-Object Name -eq 'cmdline-plik-procent'
            $Percent.Extra.TargetAclAfter.AdministratorsFullControl | Should -BeFalse
        }
        It 'komenda folderowa to takeown /f "%1" /a /r /d Y, potem grant (nie icacls /setowner)' {
            $Cmd = (Get-C4MenuCommandLine -Kind Folder).Arguments
            $Cmd | Should -Match '^/c takeown /f "%1" /a /r /d Y && icacls "%1" /grant \*S-1-5-32-544:F'
            $Cmd | Should -Not -Match 'setowner'
        }
        It 'ręczny test UAC, Prepare: wariant pod * i Directory (Assert-C4WritePath), drzewo w C:\DsoTest\manual-uac z właścicielem dso, właściciel i ACL zapisane; Extended tylko na żądanie' {
            Mock Get-LocalUser { [pscustomobject]@{ SID = [pscustomobject]@{ Value = 'S-1-5-21-9-9-9-1001' } } }
            Mock Remove-C4RegistryTree { $script:Calls.Add("rmtree $SubKeyPath") }
            Mock Set-C4RegistryValue { $script:Writes.Add(@{ Path = $SubKeyPath; Name = $Name; Value = $Value }) }
            Mock Get-C4TokenInfo { [pscustomobject]@{ UserSid = 'S-1-5-21-1-1-1-500'; HasHighIntegritySid = $true } }
            $Doc = Invoke-C4ManualUacCollection -Mode Prepare -Structure Subkey -Extended | ConvertFrom-Json
            $Doc.mode | Should -Be 'Prepare'
            $Step = $Doc.probes.manualUac[0]
            $Step.Name | Should -Be 'przygotowanie-reczny-uac'
            $Step.Error | Should -BeNullOrEmpty
            @($Writes | ForEach-Object { $_.Path } | Where-Object { $_ -like 'SOFTWARE\Classes\*\shell\DSOptimizer.TakeOwnership*' }).Count | Should -Be $Writes.Count
            foreach ($Class in '*', 'Directory') { @($Writes | Where-Object { $_.Path -eq "SOFTWARE\Classes\$Class\shell\DSOptimizer.TakeOwnership" -and $_.Name -eq 'Extended' }).Count | Should -Be 1 }
            @($Step.Extra.Owners | Where-Object { $_.Path -like 'C:\DsoTest\manual-uac\*' } | ForEach-Object { $_.OwnerSid } | Sort-Object -Unique) | Should -Be @('S-1-5-21-9-9-9-1001')
            @($Step.Extra.Acls).Count | Should -Be 6
            $Step.Extra.Tree.Folder | Should -Be 'C:\DsoTest\manual-uac\folder testowy'
            $Writes.Clear()
            $null = Invoke-C4ManualUacCollection -Mode Prepare -Structure Plain
            @($Writes | Where-Object { $_.Name -eq 'Extended' }).Count | Should -Be 0
        }

        It 'ręczny test UAC: Deny List Folder dotyczy konta dso, a nie konta procesu sondy' {
            Mock Get-LocalUser { [pscustomobject]@{ SID = [pscustomobject]@{ Value = 'S-1-5-21-9-9-9-1001' } } }
            Mock Remove-C4RegistryTree {}
            Mock Set-C4RegistryValue {}
            Mock Get-C4TokenInfo { [pscustomobject]@{ UserSid = 'S-1-5-21-1-1-1-500'; HasHighIntegritySid = $true } }
            $null = Invoke-C4ManualUacCollection -Mode Prepare -Structure Subkey
            $script:AddedRules.Count | Should -Be 1
            $script:AddedRules[0].IdentityReference.Value | Should -Be 'S-1-5-21-9-9-9-1001'
            $script:AddedRules[0].AccessControlType | Should -Be ([System.Security.AccessControl.AccessControlType]::Deny)
        }
        It 'New-C4TestTree bez -DenyUserSid odbiera List Folder bieżącemu kontem procesu (sondy 4c i 5 bez zmian)' {
            $null = New-C4TestTree -Root 'C:\DsoTest\x' -SeedOwnerSid 'S-1-5-18'
            $script:AddedRules[0].IdentityReference.Value | Should -Be ([System.Security.Principal.WindowsIdentity]::GetCurrent().User.Value)
        }
        It 'Get-C4AclInfo: AdministratorsFullControl liczy tylko wpisy JAWNE; dziedziczony FullControl jest zapisany osobno' {
            $Admins = New-Object System.Security.Principal.SecurityIdentifier('S-1-5-32-544')
            $Inherited = [pscustomobject]@{ IdentityReference = $Admins; FileSystemRights = [System.Security.AccessControl.FileSystemRights]::FullControl; AccessControlType = [System.Security.AccessControl.AccessControlType]::Allow; IsInherited = $true }
            $Explicit = [pscustomobject]@{ IdentityReference = $Admins; FileSystemRights = [System.Security.AccessControl.FileSystemRights]::FullControl; AccessControlType = [System.Security.AccessControl.AccessControlType]::Allow; IsInherited = $false }
            Add-FakePath -Path 'C:\DsoTest\a'
            $script:Rules = @($Inherited)
            Mock Get-Acl { [pscustomobject]@{ Owner = 'x'; Sddl = 's'; Access = $script:Rules } }
            $OnlyInherited = Get-C4AclInfo -Path 'C:\DsoTest\a'
            $OnlyInherited.AdministratorsFullControl | Should -BeFalse
            $OnlyInherited.AdministratorsFullControlInherited | Should -BeTrue
            $script:Rules = @($Inherited, $Explicit)
            $Both = Get-C4AclInfo -Path 'C:\DsoTest\a'
            $Both.AdministratorsFullControl | Should -BeTrue
            $Both.AdministratorsFullControlInherited | Should -BeTrue
            @($Both.Aces | Where-Object { -not $_.IsInherited }).Count | Should -Be 1
        }
        It 'ręczny test UAC, Prepare bez konta dso: błąd w kroku, zero zapisów do rejestru i zero drzewa' {
            Mock Get-LocalUser { throw 'brak konta' }
            Mock Remove-C4RegistryTree { $script:Calls.Add('rmtree') }
            $Doc = Invoke-C4ManualUacCollection -Mode Prepare | ConvertFrom-Json
            $Doc.probes.manualUac[0].Error | Should -Match 'brak konta'
            $Calls | Should -BeNullOrEmpty
            $script:Fs.Count | Should -Be 0
        }
        It 'ręczny test UAC, Finish: tylko odczyt (właściciel, ACL, klucze), zero zapisów do rejestru' {
            Add-FakePath -Path 'C:\DsoTest\manual-uac\plik testowy.txt'
            Add-FakePath -Path 'C:\DsoTest\manual-uac\folder testowy\wewnatrz.txt'
            Mock Get-C4TokenInfo { [pscustomobject]@{ UserSid = 'S-1'; HasHighIntegritySid = $true } }
            $Doc = Invoke-C4ManualUacCollection -Mode Finish | ConvertFrom-Json
            $Doc.mode | Should -Be 'Finish'
            $Step = $Doc.probes.manualUac[0]
            $Step.Name | Should -Be 'stan-po-tescie-recznym'
            (@($Step.Extra.Owners | Where-Object { $_.Path -like '*plik testowy.txt' })[0]).Exists | Should -BeTrue
            @($Step.RegistryKeys | ForEach-Object { $_.Path }) | Should -Contain 'HKLM\SOFTWARE\Classes\*\shell\DSOptimizer.TakeOwnership'
            Should -Invoke Set-C4RegistryValue -Times 0 -Exactly
            Should -Invoke Remove-C4RegistryTree -Times 0 -Exactly
        }
    }
}

Describe 'Wysyłka funkcji do VM (C4) i struktura skryptu' {
    It 'Get-C4RemoteScript jest poprawnym skryptem i definiuje wszystkie funkcje zdalne' {
        $Text = Get-C4RemoteScript
        $Errors = $null
        [void][System.Management.Automation.Language.Parser]::ParseInput($Text, [ref]$null, [ref]$Errors)
        @($Errors).Count | Should -Be 0
        foreach ($Name in ((Get-ExploreCommonRemoteFunctionNames) + (Get-C4RemoteFunctionNames))) { $Text | Should -Match "function $Name \{" }
        (Get-C4RemoteFunctionNames) | Should -Contain 'Invoke-C4Collection'
    }
    It 'funkcje zdalne nie wołają niczego spoza listy wysyłanej; funkcje hosta nie są wysyłane' {
        $Text = Get-C4RemoteScript
        $Ast = [System.Management.Automation.Language.Parser]::ParseInput($Text, [ref]$null, [ref]$null)
        $Called = $Ast.FindAll({ param($n) $n -is [System.Management.Automation.Language.CommandAst] }, $true) |
            ForEach-Object { $_.GetCommandName() } | Where-Object { $_ -like '*-C4*' -or $_ -like '*-Explore*' } | Sort-Object -Unique
        $Sent = (Get-ExploreCommonRemoteFunctionNames) + (Get-C4RemoteFunctionNames)
        foreach ($Name in $Called) { $Sent | Should -Contain $Name }
        foreach ($HostOnly in (Get-C4HostOnlyFunctionNames)) { $Text | Should -Not -Match "function $HostOnly \{" ; $Called | Should -Not -Contain $HostOnly }
        $Text | Should -Not -Match 'Restore-VmCleanState|Wait-VmReady|Get-DsoVmCredential|Test-VmAuthFailure|Start-VM|Invoke-ExploreVmRun|Invoke-ExploreAll'
    }
    It 'każda funkcja skryptu jest albo wysyłana do VM, albo na liście funkcji hosta (nic nie ginie)' {
        $Ast = [System.Management.Automation.Language.Parser]::ParseFile((Resolve-Path $script:C4ScriptPath), [ref]$null, [ref]$null)
        $Defined = @($Ast.FindAll({ param($n) $n -is [System.Management.Automation.Language.FunctionDefinitionAst] }, $true) | ForEach-Object { $_.Name })
        foreach ($Name in $Defined) { ((Get-C4RemoteFunctionNames) + (Get-C4HostOnlyFunctionNames)) | Should -Contain $Name }
        foreach ($Name in (Get-C4RemoteFunctionNames)) { $Defined | Should -Contain $Name }
    }
    It 'decyzja D1 (bez szyfrowania w VM): Enable-BitLocker i Decrypt nie występują wcale, Set-Acl tylko w New-C4TestTree; zakazane nie występują wcale' {
        $Ast = [System.Management.Automation.Language.Parser]::ParseFile((Resolve-Path $script:C4ScriptPath), [ref]$null, [ref]$null)
        $Commands = $Ast.FindAll({ param($n) $n -is [System.Management.Automation.Language.CommandAst] }, $true)
        $Owner = {
            param($Node)
            $Parent = $Node.Parent
            while ($null -ne $Parent -and $Parent -isnot [System.Management.Automation.Language.FunctionDefinitionAst]) { $Parent = $Parent.Parent }
            if ($null -ne $Parent) { $Parent.Name } else { '(poza funkcją)' }
        }
        foreach ($Command in ($Commands | Where-Object { $_.GetCommandName() -eq 'Set-Acl' })) { (& $Owner $Command) | Should -Be 'New-C4TestTree' }
        @($Commands | Where-Object { $_.GetCommandName() -eq 'Enable-BitLocker' }).Count | Should -Be 0
        foreach ($Command in ($Commands | Where-Object { $_.GetCommandName() -eq 'Invoke-CimMethod' })) { $Command.Extent.Text | Should -Match '-MethodName GetConversionStatus|-MethodName GetProtectionStatus' }
        $Names = @($Commands | ForEach-Object { $_.GetCommandName() } | Where-Object { $_ })
        foreach ($Forbidden in 'Disable-BitLocker', 'Suspend-BitLocker', 'Clear-BitLockerAutoUnlock', 'Set-SmbClientConfiguration', 'Set-SmbServerConfiguration', 'Restart-Computer', 'Stop-Computer',
            'Set-ItemProperty', 'New-ItemProperty', 'Remove-ItemProperty', 'reg.exe', 'reg', 'manage-bde', 'manage-bde.exe', 'Start-VM', 'Stop-VM', 'Restore-VMCheckpoint', 'Set-Service', 'Add-Type') {
            $Names | Should -Not -Contain $Forbidden
        }
        $Text = [System.IO.File]::ReadAllText($script:C4ScriptPath, [System.Text.Encoding]::UTF8)
        $Text | Should -Not -Match 'HKCU:|HKEY_CURRENT_USER|Bruteforce_LTSC\s*[''"]'
        $Text | Should -Not -Match '(?i)ClearAllAutoUnlockKeys|SkipHardwareTest|-MethodName Decrypt|Wait-C4Conversion|-Collector|Reconnect'
        # Katalog wyników na hoście tworzy dopiero Save-ExploreResult po sprawdzeniu dostawcy FileSystem: blok główny nie woła New-Item.
        $Main = $Ast.FindAll({ param($n) $n -is [System.Management.Automation.Language.IfStatementAst] -and $n.Extent.Text -match '^if \(\$MyInvocation\.InvocationName' }, $false)[0]
        @($Main.FindAll({ param($n) $n -is [System.Management.Automation.Language.CommandAst] -and $n.GetCommandName() -eq 'New-Item' }, $true)).Count | Should -Be 0
    }
    It 'plik ma BOM UTF-8 oraz sekcje Instrukcja uruchomienia i Opis działania' {
        $Bytes = [System.IO.File]::ReadAllBytes($script:C4ScriptPath)
        ($Bytes[0..2] -join ',') | Should -Be '239,187,191'
        $Text = [System.IO.File]::ReadAllText($script:C4ScriptPath, [System.Text.Encoding]::UTF8)
        $Text.Substring(0, 400) | Should -Match 'Instrukcja uruchomienia'
        $Text | Should -Match 'Opis działania'
    }
    It 'parametr VmName przyjmuje tylko DSO-26H2-Home i DSO-26H2-Pro (nigdy Bruteforce_LTSC), w skrypcie i w Invoke-C4Exploration' {
        $Attr = (Get-Command $script:C4ScriptPath).Parameters['VmName'].Attributes | Where-Object { $_ -is [System.Management.Automation.ValidateSetAttribute] }
        $Attr.ValidValues | Should -Be @('DSO-26H2-Home', 'DSO-26H2-Pro')
        $Attr2 = (Get-Command Invoke-C4Exploration).Parameters['VmName'].Attributes | Where-Object { $_ -is [System.Management.Automation.ValidateSetAttribute] }
        $Attr2.ValidValues | Should -Be @('DSO-26H2-Home', 'DSO-26H2-Pro')
        { Invoke-C4Exploration -VmName 'Bruteforce_LTSC' -OutDirectory $TestDrive } | Should -Throw
    }
    Context 'Wybór sond (-Probe)' {
        It 'bez -Probe: wszystkie siedem sond w ustalonej kolejności; z -Probe tylko wybrane (kolejność pełnego przebiegu), pozostałe nie są wołane' {
            Mock Invoke-C4BitLockerPolicyProbe { [pscustomobject]@{ Name = 'a' } }
            Mock Invoke-C4BitLockerReadProbe { [pscustomobject]@{ Name = 'b' } }
            Mock Invoke-C4ShellClassesProbe { [pscustomobject]@{ Name = 'c' } }
            Mock Invoke-C4GuestPolicyProbe { [pscustomobject]@{ Name = 'd' } }
            Mock Invoke-C4TakeownProbe { [pscustomobject]@{ Name = 'e' } }
            Mock Invoke-C4MenuProbe { [pscustomobject]@{ Name = 'f' } }
            Mock Invoke-C4CommandLineProbe { [pscustomobject]@{ Name = 'g' } }
            Mock Get-C4TokenInfo { [pscustomobject]@{ UserSid = 'S-1' } }
            Mock -ModuleName ExploreCommon Get-ExploreMeta { [ordered]@{ EditionID = 'Core' } }
            $All = Invoke-C4Collection | ConvertFrom-Json
            @($All.probes.PSObject.Properties.Name) | Should -Be @('bitlockerPolicy', 'bitlockerState', 'shellClasses', 'guestPolicy', 'takeown', 'menu', 'cmdline')
            $Some = Invoke-C4Collection -Probe 'cmdline', 'menu' | ConvertFrom-Json
            @($Some.probes.PSObject.Properties.Name) | Should -Be @('menu', 'cmdline')
            Should -Invoke Invoke-C4GuestPolicyProbe -Times 1 -Exactly
            Should -Invoke Invoke-C4MenuProbe -Times 2 -Exactly
            { Invoke-C4Collection -Probe 'nieznana' } | Should -Throw
        }
        It 'ValidateSet -Probe jest identyczny w skrypcie, Invoke-C4Collection i Invoke-C4Exploration (siedem nazw)' {
            $Expected = @('bitlockerPolicy', 'bitlockerState', 'shellClasses', 'guestPolicy', 'takeown', 'menu', 'cmdline')
            foreach ($Command in (Get-Command $script:C4ScriptPath), (Get-Command Invoke-C4Collection), (Get-Command Invoke-C4Exploration)) {
                ($Command.Parameters['Probe'].Attributes | Where-Object { $_ -is [System.Management.Automation.ValidateSetAttribute] }).ValidValues | Should -Be $Expected
            }
        }
        It 'Invoke-C4Exploration -Probe: do Invoke-ExploreAll idzie skrypt z Invoke-C4EntryPoint i wybranymi sondami; bez -Probe punkt wejścia Invoke-C4Collection' {
            $script:Seen = $null
            Mock Invoke-ExploreAll { $script:Seen = @{ Entry = $EntryCommand; Script = $RemoteScript }; [pscustomobject]@{ Paths = @(); Errors = @() } }
            $null = Invoke-C4Exploration -VmName 'DSO-26H2-Home' -OutDirectory $TestDrive -Probe 'menu', 'cmdline'
            $script:Seen.Entry | Should -Be 'Invoke-C4EntryPoint'
            $script:Seen.Script | Should -Match "function Invoke-C4EntryPoint \{\s+Invoke-C4Collection -Probe @\('menu','cmdline'\)\s+\}"
            $Errors = $null
            [void][System.Management.Automation.Language.Parser]::ParseInput($script:Seen.Script, [ref]$null, [ref]$Errors)
            @($Errors).Count | Should -Be 0
            $null = Invoke-C4Exploration -VmName 'DSO-26H2-Home' -OutDirectory $TestDrive
            $script:Seen.Entry | Should -Be 'Invoke-C4Collection'
            $script:Seen.Script | Should -Not -Match 'Invoke-C4EntryPoint'
        }
        It 'New-C4EntryPoint odrzuca wstrzyknięcie (średnik, pipe, podstawienie)' {
            { New-C4EntryPoint -Body 'Invoke-C4Collection; calc' } | Should -Throw
            { New-C4EntryPoint -Body 'Invoke-C4Collection | Out-Null' } | Should -Throw
            { New-C4EntryPoint -Body 'Invoke-C4Collection -Probe @(''menu'')' } | Should -Not -Throw
        }
    }

    Context 'Tryby ręcznego testu UAC i walidacja startu' {
        It 'Assert-C4ModeArguments: tryby wykluczają się, wymagają jawnie jednej VM, nie łączą się z -Probe; bez trybu nic nie sprawdza' {
            { Assert-C4ModeArguments -Prepare $true -Finish $true -VmName 'DSO-26H2-Pro' -VmNameExplicit $true } | Should -Throw '*wykluczają*'
            { Assert-C4ModeArguments -Prepare $true -Finish $false -VmName @('DSO-26H2-Home', 'DSO-26H2-Pro') -VmNameExplicit $true } | Should -Throw '*JEDNEJ*'
            { Assert-C4ModeArguments -Prepare $false -Finish $true -VmName @('DSO-26H2-Home', 'DSO-26H2-Pro') -VmNameExplicit $false } | Should -Throw '*JEDNEJ*'
            { Assert-C4ModeArguments -Prepare $true -Finish $false -VmName 'DSO-26H2-Pro' -VmNameExplicit $true -Probe 'menu' } | Should -Throw '*-Probe*'
            { Assert-C4ModeArguments -Prepare $true -Finish $false -VmName 'DSO-26H2-Pro' -VmNameExplicit $true } | Should -Not -Throw
            { Assert-C4ModeArguments -Prepare $false -Finish $false -VmName @('DSO-26H2-Home', 'DSO-26H2-Pro') -VmNameExplicit $false } | Should -Not -Throw
        }
        It 'parametry trybów: -Variant tylko Plain/Subkey/ValuePath, -VmName nadal tylko Home/Pro' {
            $Params = (Get-Command $script:C4ScriptPath).Parameters
            ($Params['Variant'].Attributes | Where-Object { $_ -is [System.Management.Automation.ValidateSetAttribute] }).ValidValues | Should -Be @('Plain', 'Subkey', 'ValuePath')
            $Params.ContainsKey('PrepareManualUacTest') | Should -BeTrue
            $Params.ContainsKey('FinishManualUacTest') | Should -BeTrue
            $Params.ContainsKey('WithExtended') | Should -BeTrue
            ($Params['VmName'].Attributes | Where-Object { $_ -is [System.Management.Automation.ValidateSetAttribute] }).ValidValues | Should -Be @('DSO-26H2-Home', 'DSO-26H2-Pro')
        }
        It 'Assert-C4PrepareResult: kompletny wynik przechodzi; błąd kroku, błąd drzewa, kod icacls <> 0, brak Deny, brak zapisów jednej klasy i brak kroku => wyjątek' {
            $Good = {
                $Writes = @()
                foreach ($Class in '*', 'Directory') {
                    $Root = "SOFTWARE\Classes\$Class\shell\DSOptimizer.TakeOwnership"
                    $Writes += [pscustomobject]@{ SubKeyPath = $Root; Name = 'MUIVerb'; Kind = 'String'; Value = 'x' }
                    $Writes += [pscustomobject]@{ SubKeyPath = "$Root\command"; Name = ''; Kind = 'String'; Value = 'cmd' }
                }
                [pscustomobject]@{ probes = [pscustomobject]@{ manualUac = @([pscustomobject]@{ Name = 'przygotowanie-reczny-uac'; Error = $null
                                Extra = [pscustomobject]@{ Writes = $Writes; Tree = [pscustomobject]@{ Error = $null; Deny = 'Deny'; Seed = [pscustomobject]@{ ExitCode = 0; Error = $null } } } }) } }
            }
            { Assert-C4PrepareResult -Document (& $Good) } | Should -Not -Throw
            $Doc = & $Good; $Doc.probes.manualUac[0].Error = 'brak konta'
            { Assert-C4PrepareResult -Document $Doc } | Should -Throw '*brak konta*'
            $Doc = & $Good; $Doc.probes.manualUac[0].Extra.Tree.Error = 'dostęp'
            { Assert-C4PrepareResult -Document $Doc } | Should -Throw '*drzewo: dostęp*'
            $Doc = & $Good; $Doc.probes.manualUac[0].Extra.Tree.Seed.ExitCode = 5
            { Assert-C4PrepareResult -Document $Doc } | Should -Throw '*icacls /setowner*5*'
            $Doc = & $Good; $Doc.probes.manualUac[0].Extra.Tree.Deny = $null
            { Assert-C4PrepareResult -Document $Doc } | Should -Throw '*Deny*'
            $Doc = & $Good; $Doc.probes.manualUac[0].Extra.Writes = @($Doc.probes.manualUac[0].Extra.Writes | Where-Object { $_.SubKeyPath -like '*Directory*' })
            { Assert-C4PrepareResult -Document $Doc } | Should -Throw '*klasą **'
            $Doc = [pscustomobject]@{ probes = [pscustomobject]@{ manualUac = @([pscustomobject]@{ Name = 'BLAD-SONDY'; Error = 'wyjątek sondy' }) } }
            { Assert-C4PrepareResult -Document $Doc } | Should -Throw '*wyjątek sondy*'
            { Assert-C4PrepareResult -Document ([pscustomobject]@{ meta = 1 }) } | Should -Throw '*brak kroku*'
        }
        It 'New-C4EntryPoint przepuszcza tylko dwa dokładne kształty (wybór sond, ręczny UAC); $(...), nawiasy, dodatkowe parametry odrzucone' {
            { New-C4EntryPoint -Body 'Invoke-C4Collection' } | Should -Not -Throw
            { New-C4EntryPoint -Body "Invoke-C4Collection -Probe @('menu','cmdline')" } | Should -Not -Throw
            { New-C4EntryPoint -Body 'Invoke-C4ManualUacCollection -Mode Finish -Structure Plain -Extended' } | Should -Not -Throw
            foreach ($Bad in 'Invoke-C4Collection $(calc)', 'Invoke-C4Collection (calc)', "Invoke-C4Collection -Probe @('menu') -Foo", 'Invoke-C4ManualUacCollection -Mode Prepare -Structure Subkey -Extended $true', 'calc', 'Invoke-C4ManualUacCollection -Mode Prepare -Structure X') {
                { New-C4EntryPoint -Body $Bad } | Should -Throw
            }
        }
        It 'instrukcja po Prepare: po polsku, vmconnect, użytkownik dso, Pokaż więcej opcji, UAC, komenda Finish; Shift tylko z Extended' {
            $With = (Get-C4ManualUacInstructions -VmName 'DSO-26H2-Pro' -Variant Subkey -WithExtended $true -OutDirectory 'C:\wyniki dir') -join "`n"
            $With | Should -Match 'vmconnect localhost DSO-26H2-Pro'
            $With | Should -Match "użytkownik 'dso'"
            $With | Should -Match 'Pokaż więcej opcji'
            $With | Should -Match 'UAC'
            $With | Should -Match 'Z KLAWISZEM SHIFT'
            $With | Should -Match 'DSO: Przejmij \(administrator\)'
            $With | Should -Match ([regex]::Escape('-FinishManualUacTest -VmName DSO-26H2-Pro -OutDirectory "C:\wyniki dir"'))
            $With | Should -Match 'autologon'
            $With | Should -Match 'klasyczne menu od razu'
            $With | Should -Match 'Nie wymaga sesji użytkownika dso'
            $Without = (Get-C4ManualUacInstructions -VmName 'DSO-26H2-Home' -Variant Plain -WithExtended $false -OutDirectory 'C:\w') -join "`n"
            $Without | Should -Not -Match 'Z KLAWISZEM SHIFT'
            $Without | Should -Not -Match 'Przejmij \(administrator\)'
        }
        It 'walidacja -OutDirectory (Resolve-ExploreOutDirectory) jest w bloku głównym PRZED jakąkolwiek pracą na VM (Invoke-C4Exploration/Invoke-C4ManualUacTest)' {
            $Text = [System.IO.File]::ReadAllText($script:C4ScriptPath, [System.Text.Encoding]::UTF8)
            $Main = $Text.Substring($Text.IndexOf('if ($MyInvocation.InvocationName'))
            $Resolve = $Main.IndexOf('Resolve-ExploreOutDirectory')
            $Resolve | Should -BeGreaterThan 0
            $Main | Should -Match '\$OutDirectory = Resolve-ExploreOutDirectory'
            $Resolve | Should -BeLessThan $Main.IndexOf('Invoke-C4ManualUacTest')
            $Resolve | Should -BeLessThan $Main.IndexOf('Invoke-C4Exploration -VmName')
            { Resolve-ExploreOutDirectory -Path 'Variable:\X' } | Should -Throw '*FileSystem*'
        }
    }
}

Describe 'Przebieg hosta C4 (orkiestracja na mockach; Hyper-V nietknięty)' {
    BeforeEach {
        # Bezpieczniki: domyślnie wszystko, co dotyka Hyper-V / PowerShell Direct, rzuca (w obu zasięgach).
        Set-NoLiveVmDefaultMock -ModuleName VmHost
        Set-NoLiveVmDefaultMock -ModuleName ExploreCommon
        Mock Set-SmbClientConfiguration { throw 'HOST: Set-SmbClientConfiguration niedozwolone w testach' }
        Mock Enable-BitLocker { throw 'HOST: Enable-BitLocker niedozwolone w testach' }
        Mock Invoke-CimMethod { throw 'HOST: Invoke-CimMethod niedozwolone w testach' }
        $script:Order = [System.Collections.Generic.List[string]]::new()
        Mock -ModuleName ExploreCommon Stop-VM { throw 'HOST: Stop-VM niedozwolone w testach' }
        Mock -ModuleName ExploreCommon Get-DsoVmCredential { [pscredential]::new('Administrator', (ConvertTo-SecureString 'x' -AsPlainText -Force)) }
        Mock -ModuleName ExploreCommon Restore-VmCleanState { $script:Order.Add("restore:$VmName") }
        Mock -ModuleName ExploreCommon Start-VM { $script:Order.Add("start:$Name") }
        Mock -ModuleName ExploreCommon Wait-VmReady { $script:Order.Add("ready:$VmName"); [System.Runtime.Serialization.FormatterServices]::GetUninitializedObject([System.Management.Automation.Runspaces.PSSession]) }
        Mock -ModuleName ExploreCommon Remove-PSSession { $script:Order.Add('removesession') }
        Mock -ModuleName ExploreCommon Invoke-Command { throw 'HOST: Invoke-Command niedozwolone w tym teście' }
    }
    It 'bez zmockowanego wyłącznika prawdziwa orkiestracja zatrzymuje się na DSO_VM_TESTS_MOCK_ONLY: błąd zapisany, zero Start-VM i revertu' {
        $Outcome = Invoke-C4Exploration -VmName @('DSO-26H2-Pro') -OutDirectory $TestDrive
        @($Outcome.Paths).Count | Should -Be 0
        $Outcome.Errors[0] | Should -Match 'DSO_VM_TESTS_MOCK_ONLY'
        Should -Invoke -ModuleName ExploreCommon Start-VM -Times 0 -Exactly
        Should -Invoke -ModuleName ExploreCommon Restore-VmCleanState -Times 0 -Exactly
    }
    Context 'z wyłącznikiem zmockowanym tylko w module (orkiestracja jak w C1-C3)' {
        BeforeEach {
            Mock -ModuleName ExploreCommon Assert-DsoVmLiveAllowed {}
            Mock -ModuleName ExploreCommon Invoke-Command { if ($ArgumentList) { $script:Order.Add("collect:$($ArgumentList[1])"); '{"meta":{"EditionID":"Professional"},"probes":{}}' } else { throw 'nieoczekiwane Invoke-Command' } }
        }
        It 'revert -> start -> ready -> zbieranie (Invoke-C4Collection jednym wywołaniem) -> usunięcie sesji -> revert; JSON c4-exploration.json' {
            $Outcome = Invoke-C4Exploration -VmName @('DSO-26H2-Pro') -OutDirectory $TestDrive
            ($Order -join ',') | Should -Be 'restore:DSO-26H2-Pro,start:DSO-26H2-Pro,ready:DSO-26H2-Pro,collect:Invoke-C4Collection,removesession,restore:DSO-26H2-Pro'
            $Outcome.Paths | Should -Be @((Join-Path $TestDrive 'DSO-26H2-Pro\c4-exploration.json'))
            @($Outcome.Errors).Count | Should -Be 0
            $Doc = Get-Content -LiteralPath $Outcome.Paths[0] -Raw -Encoding UTF8 | ConvertFrom-Json
            $Doc.vmName | Should -Be 'DSO-26H2-Pro'
            $Doc.checkpoint | Should -Be 'czysta-26H2'
        }
        It 'OutDirectory spoza FileSystem jest odrzucony przez Save-ExploreResult bez utworzenia czegokolwiek (Variable:)' {
            $Outcome = Invoke-C4Exploration -VmName @('DSO-26H2-Home') -OutDirectory 'Variable:\DsoNieFilesystemC4'
            @($Outcome.Paths).Count | Should -Be 0
            $Outcome.Errors[0] | Should -Match 'FileSystem'
            Test-Path -LiteralPath (Join-Path (Get-Location) 'DsoNieFilesystemC4') | Should -BeFalse
        }
        It 'dwie VM po kolei: Home, potem Pro, każda z własnym revertem' {
            $null = Invoke-C4Exploration -VmName @('DSO-26H2-Home', 'DSO-26H2-Pro') -OutDirectory $TestDrive
            @($Order | Where-Object { $_ -like 'restore:*' }) | Should -Be @('restore:DSO-26H2-Home', 'restore:DSO-26H2-Home', 'restore:DSO-26H2-Pro', 'restore:DSO-26H2-Pro')
        }
        It 'błąd zbierania: revert w finally nadal jest, błąd zapisany, druga VM zebrana' {
            $script:Fail = $true
            Mock -ModuleName ExploreCommon Invoke-Command { if ($script:Fail) { $script:Fail = $false; throw 'zerwana sesja' }; '{"meta":{},"probes":{}}' }
            $Outcome = Invoke-C4Exploration -VmName @('DSO-26H2-Home', 'DSO-26H2-Pro') -OutDirectory $TestDrive
            @($Outcome.Errors).Count | Should -Be 1
            $Outcome.Errors[0] | Should -Match 'zerwana sesja'
            @($Outcome.Paths).Count | Should -Be 1
            $Order | Should -Contain 'restore:DSO-26H2-Home'
        }
        It 'błąd uwierzytelnienia zatrzymuje całość: druga VM nietknięta, revert pierwszej jest' {
            Mock -ModuleName ExploreCommon Wait-VmReady {
                $F = [System.InvalidOperationException]::new('inny tekst niż znacznik')
                $F.Data['VmAuthFailure'] = $true
                throw $F
            }
            { Invoke-C4Exploration -VmName @('DSO-26H2-Home', 'DSO-26H2-Pro') -OutDirectory $TestDrive } | Should -Throw '*inny tekst*'
            ($Order | Where-Object { $_ -like '*Pro*' }) | Should -BeNullOrEmpty
            $Order[-1] | Should -Be 'restore:DSO-26H2-Home'
        }
        It 'ręczny test UAC Prepare: revert -> start -> ready -> zbieranie -> usunięcie sesji i BRAK końcowego revertu (VM zostaje uruchomiona); plik c4-manual-uac-prepare.json; wariant w punkcie wejścia' {
            $script:Defs = $null
            $script:GoodJson = Get-GoodPrepareJson
            Mock -ModuleName ExploreCommon Invoke-Command { if ($ArgumentList) { $script:Order.Add('collect'); $script:Defs = $ArgumentList[0]; $script:GoodJson } else { throw 'x' } }
            $Path = Invoke-C4ManualUacTest -Mode Prepare -VmName 'DSO-26H2-Pro' -OutDirectory $TestDrive -Variant ValuePath -WithExtended
            ($Order -join ',') | Should -Be 'restore:DSO-26H2-Pro,start:DSO-26H2-Pro,ready:DSO-26H2-Pro,collect,removesession'
            $Path | Should -Be (Join-Path $TestDrive 'DSO-26H2-Pro\c4-manual-uac-prepare.json')
            $script:Defs | Should -Match 'Invoke-C4ManualUacCollection -Mode Prepare -Structure ValuePath -Extended\s+\}'
            Should -Invoke -ModuleName ExploreCommon Stop-VM -Times 0 -Exactly
        }
        It 'ręczny test UAC Prepare: błąd zbierania => VM przywrócona (revert), wyjątek propagowany' {
            Mock -ModuleName ExploreCommon Invoke-Command { throw 'zerwana sesja' }
            { Invoke-C4ManualUacTest -Mode Prepare -VmName 'DSO-26H2-Home' -OutDirectory $TestDrive } | Should -Throw '*zerwana sesja*'
            $Order[-1] | Should -Be 'restore:DSO-26H2-Home'
        }
        It 'ręczny test UAC Prepare: błąd uwierzytelnienia => revert i wyjątek z flagą auth (stop)' {
            Mock -ModuleName ExploreCommon Wait-VmReady { $F = [System.InvalidOperationException]::new('odmowa'); $F.Data['VmAuthFailure'] = $true; throw $F }
            $Caught = $null
            try { Invoke-C4ManualUacTest -Mode Prepare -VmName 'DSO-26H2-Pro' -OutDirectory $TestDrive } catch { $Caught = $_.Exception }
            Test-VmAuthFailure -Exception $Caught | Should -BeTrue
            $Order[-1] | Should -Be 'restore:DSO-26H2-Pro'
        }
        It 'ręczny test UAC Finish: bez revertu i startu na początku, zbieranie, usunięcie sesji, ZAWSZE revert; plik c4-manual-uac-finish.json' {
            Mock -ModuleName ExploreCommon Invoke-Command { if ($ArgumentList) { $script:Order.Add('collect'); '{"meta":{},"probes":{}}' } else { throw 'x' } }
            $Path = Invoke-C4ManualUacTest -Mode Finish -VmName 'DSO-26H2-Pro' -OutDirectory $TestDrive
            ($Order -join ',') | Should -Be 'ready:DSO-26H2-Pro,collect,removesession,restore:DSO-26H2-Pro'
            $Path | Should -Be (Join-Path $TestDrive 'DSO-26H2-Pro\c4-manual-uac-finish.json')
        }
        It 'ręczny test UAC Finish: revert także po błędzie zbierania i po błędzie gotowości' {
            Mock -ModuleName ExploreCommon Invoke-Command { throw 'zerwana sesja' }
            { Invoke-C4ManualUacTest -Mode Finish -VmName 'DSO-26H2-Pro' -OutDirectory $TestDrive } | Should -Throw '*zerwana sesja*'
            $Order[-1] | Should -Be 'restore:DSO-26H2-Pro'
            $Order.Clear()
            Mock -ModuleName ExploreCommon Wait-VmReady { throw 'VM nie była gotowa' }
            { Invoke-C4ManualUacTest -Mode Finish -VmName 'DSO-26H2-Pro' -OutDirectory $TestDrive } | Should -Throw '*nie była gotowa*'
            $Order[-1] | Should -Be 'restore:DSO-26H2-Pro'
            ($Order | Where-Object { $_ -like 'start:*' }) | Should -BeNullOrEmpty
        }
        It 'ręczny test UAC Prepare: niekompletny wynik z VM => wyjątek, revert jako ostatnie wywołanie, plik JSON zostaje do diagnozy' {
            Mock -ModuleName ExploreCommon Invoke-Command { if ($ArgumentList) { $script:Order.Add('collect'); '{"meta":{},"probes":{"manualUac":[{"Name":"przygotowanie-reczny-uac","Error":"brak konta dso","Extra":null}]}}' } else { throw 'x' } }
            { Invoke-C4ManualUacTest -Mode Prepare -VmName 'DSO-26H2-Pro' -OutDirectory $TestDrive } | Should -Throw '*niekompletna*brak konta dso*'
            $Order[-1] | Should -Be 'restore:DSO-26H2-Pro'
            Test-Path -LiteralPath (Join-Path $TestDrive 'DSO-26H2-Pro\c4-manual-uac-prepare.json') | Should -BeTrue
        }
        It 'ręczny test UAC Prepare: kompletny wynik z VM => VM zostaje (brak końcowego revertu)' {
            $Good = Get-GoodPrepareJson
            $script:GoodJson = $Good
            Mock -ModuleName ExploreCommon Invoke-Command { if ($ArgumentList) { $script:Order.Add('collect'); $script:GoodJson } else { throw 'x' } }
            $null = Invoke-C4ManualUacTest -Mode Prepare -VmName 'DSO-26H2-Pro' -OutDirectory $TestDrive
            $Order[-1] | Should -Be 'removesession'
        }
        It 'ręczny test UAC Finish: Wait-VmReady bez wymagania sesji dso (-SkipInteractiveUserCheck), Prepare z wymaganiem' {
            Mock -ModuleName ExploreCommon Invoke-Command { if ($ArgumentList) { $script:Order.Add('collect'); '{"meta":{},"probes":{}}' } else { throw 'x' } }
            $null = Invoke-C4ManualUacTest -Mode Finish -VmName 'DSO-26H2-Pro' -OutDirectory $TestDrive
            Should -Invoke -ModuleName ExploreCommon Wait-VmReady -Times 1 -Exactly -ParameterFilter { $SkipInteractiveUserCheck.IsPresent }
        }
        It 'ręczny test UAC Finish: błąd odczytu => JSON z błędem zapisany PRZED revertem, potem revert; brak poświadczeń też kończy się revertem' {
            Mock -ModuleName ExploreCommon Invoke-Command { throw 'zerwana sesja' }
            { Invoke-C4ManualUacTest -Mode Finish -VmName 'DSO-26H2-Pro' -OutDirectory $TestDrive } | Should -Throw '*zerwana sesja*'
            $Order[-1] | Should -Be 'restore:DSO-26H2-Pro'
            $Saved = Get-Content -LiteralPath (Join-Path $TestDrive 'DSO-26H2-Pro\c4-manual-uac-finish.json') -Raw -Encoding UTF8 | ConvertFrom-Json
            $Saved.error | Should -Match 'zerwana sesja'
            $Saved.mode | Should -Be 'Finish'
            $Order.Clear()
            Mock -ModuleName ExploreCommon Get-DsoVmCredential { throw 'brak poświadczeń' }
            { Invoke-C4ManualUacTest -Mode Finish -VmName 'DSO-26H2-Home' -OutDirectory $TestDrive } | Should -Throw '*brak poświadczeń*'
            $Order | Should -Be @('restore:DSO-26H2-Home')
        }
        It 'ręczny test UAC: tylko VM z allowlisty' {
            { Invoke-C4ManualUacTest -Mode Prepare -VmName 'Bruteforce_LTSC' -OutDirectory $TestDrive } | Should -Throw
        }
        It 'checkpoint z parametru trafia do revertu; skrypt nie dubluje logiki Hyper-V (tylko Invoke-ExploreAll)' {
            $null = Invoke-C4Exploration -VmName @('DSO-26H2-Home') -OutDirectory $TestDrive -CheckpointName 'inny'
            Should -Invoke -ModuleName ExploreCommon Restore-VmCleanState -Times 2 -Exactly -ParameterFilter { $CheckpointName -eq 'inny' }
            $Text = [System.IO.File]::ReadAllText($script:C4ScriptPath, [System.Text.Encoding]::UTF8)
            $Text | Should -Match 'Invoke-ExploreAll'
            $Text | Should -Not -Match 'Restore-VMCheckpoint|Start-VM|New-PSSession|Stop-VM'
        }
    }
}

AfterAll { Clear-NoLiveVm }
