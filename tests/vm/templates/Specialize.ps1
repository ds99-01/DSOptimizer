<#
Instrukcja uruchomienia:
    Uruchamia go Windows Setup w fazie specialize (RunSynchronous z unattend.xml).
    Ręcznie nie uruchamiać. Szablon: token hasła jest podmieniany przez New-SpecializeScript.
Opis działania:
    Tworzy konto lokalne 'dso' (administrator przez SID grupy S-1-5-32-544, nie przez nazwę),
    ustawia to samo hasło wbudowanemu Administratorowi (SID kończący się na -500) i włącza go
    (PowerShell Direct loguje się jako Administrator). Wynik trafia do Specialize.log.
#>
$LogPath = 'C:\Windows\Setup\Scripts\Specialize.log'
try {
    $ErrorActionPreference = 'Stop'
    $Password = ConvertTo-SecureString -String '__PASSWORD__' -AsPlainText -Force
    New-LocalUser -Name 'dso' -Password $Password -PasswordNeverExpires -AccountNeverExpires | Out-Null
    Add-LocalGroupMember -SID 'S-1-5-32-544' -Member 'dso'
    $Admin = Get-LocalUser | Where-Object { $_.SID.Value -match '^S-1-5-21-.+-500$' }
    Set-LocalUser -SID $Admin.SID -Password $Password
    Enable-LocalUser -SID $Admin.SID
    'OK' | Out-File -FilePath $LogPath -Encoding utf8
}
catch {
    $_.Exception.Message | Out-File -FilePath $LogPath -Encoding utf8
    exit 1
}
