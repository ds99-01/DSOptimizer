<#
Instrukcja uruchomienia:
    Uruchamia go Windows Setup przy pierwszym logowaniu (FirstLogonCommands z unattend.xml).
    Ręcznie nie uruchamiać.
Opis działania:
    Usuwa z dysku unattend.xml (zawiera hasło) i Specialize.ps1, po czym zapisuje znacznik
    gotowości C:\ProgramData\dso-vm-ready.txt, na który czeka host. Błąd trafia do
    C:\ProgramData\dso-vm-firstlogon-error.txt.
#>
try {
    $ErrorActionPreference = 'Stop'
    Remove-Item -LiteralPath 'C:\Windows\Panther\unattend.xml' -Force -ErrorAction SilentlyContinue
    Remove-Item -LiteralPath 'C:\Windows\Setup\Scripts\Specialize.ps1' -Force -ErrorAction SilentlyContinue
    $Leftover = @('C:\Windows\Panther\unattend.xml', 'C:\Windows\Setup\Scripts\Specialize.ps1') | Where-Object { Test-Path -LiteralPath $_ }
    if (@($Leftover).Count -gt 0) {
        throw ('Nie usunięto plików z hasłem: ' + (@($Leftover) -join ', '))
    }
    Get-Date -Format o | Out-File -FilePath 'C:\ProgramData\dso-vm-ready.txt' -Encoding utf8
}
catch {
    $_.Exception.Message | Out-File -FilePath 'C:\ProgramData\dso-vm-firstlogon-error.txt' -Encoding utf8
}
