using System.Globalization;
using DSOptimizer.Core.Localization;

namespace DSOptimizer.Core.Install;

/// <summary>Werdykt kodu wyjścia <c>winget install</c> (spec 5.3).</summary>
public enum WingetVerdict
{
    Success,
    SuccessRebootRequired,
    AlreadyInstalled,
    RebootBeforeInstall,
    NoNetwork,
    AdminContextProhibited,
    InstallInProgress,
    NotFound,
    NoApplicableInstaller,
    HashMismatch,
    DiskFull,
    BlockedByPolicy,
    AgreementsNotAccepted,
    Cancelled,
    BlockedByAntivirus,
    Unknown,
}

/// <summary>
/// Kody wyjścia winget czytane jako <c>int</c> ze znakiem i porównywane z <c>unchecked((int)0x8A15xxxx)</c>. Mapowanie wyłącznie
/// według spec 5.3 (na podstawie <c>returnCodes.md</c> winget); każdy inny niezerowy kod = <see cref="WingetVerdict.Unknown"/>.
/// </summary>
public static class WingetExitCodes
{
    private const int PackageAlreadyInstalled = unchecked((int)0x8A150061);
    private const int InstallAlreadyInstalled = unchecked((int)0x8A15010D);
    private const int UpdateNotApplicable = unchecked((int)0x8A15002B);
    private const int RebootRequiredToFinish = unchecked((int)0x8A150109);
    private const int RebootRequiredForInstall = unchecked((int)0x8A15010A);
    private const int SourceDataMissing = unchecked((int)0x8A15000F);
    private const int InstallNoNetwork = unchecked((int)0x8A150107);
    private const int DownloadFailed = unchecked((int)0x8A150008);
    private const int InternetNameNotResolved = unchecked((int)0x80072EE7);
    private const int AdminContext = unchecked((int)0x8A150056);
    private const int InstallInProgress = unchecked((int)0x8A150102);
    private const int NoApplicationsFound = unchecked((int)0x8A150014);
    private const int NoApplicableInstaller = unchecked((int)0x8A150010);
    private const int InstallerHashMismatch = unchecked((int)0x8A150011);
    private const int DiskFull = unchecked((int)0x8A150105);
    private const int BlockedByPolicy = unchecked((int)0x8A15003A);
    private const int PackageAgreementsNotAccepted = unchecked((int)0x8A150041);
    private const int SourceAgreementsNotAccepted = unchecked((int)0x8A150046);
    private const int InstallCancelled = unchecked((int)0x800704C7);
    private const int VirusDetected = unchecked((int)0x800700E1);
    private const int VirusDetectedPotentiallyUnwanted = unchecked((int)0x800700E2);

    public static WingetVerdict Classify(int exitCode) => exitCode switch
    {
        0 => WingetVerdict.Success,
        RebootRequiredToFinish => WingetVerdict.SuccessRebootRequired,
        PackageAlreadyInstalled or InstallAlreadyInstalled or UpdateNotApplicable => WingetVerdict.AlreadyInstalled,
        RebootRequiredForInstall => WingetVerdict.RebootBeforeInstall,
        SourceDataMissing or InstallNoNetwork or DownloadFailed or InternetNameNotResolved => WingetVerdict.NoNetwork,
        AdminContext => WingetVerdict.AdminContextProhibited,
        InstallInProgress => WingetVerdict.InstallInProgress,
        NoApplicationsFound => WingetVerdict.NotFound,
        NoApplicableInstaller => WingetVerdict.NoApplicableInstaller,
        InstallerHashMismatch => WingetVerdict.HashMismatch,
        DiskFull => WingetVerdict.DiskFull,
        BlockedByPolicy => WingetVerdict.BlockedByPolicy,
        PackageAgreementsNotAccepted or SourceAgreementsNotAccepted => WingetVerdict.AgreementsNotAccepted,
        InstallCancelled => WingetVerdict.Cancelled,
        VirusDetected or VirusDetectedPotentiallyUnwanted => WingetVerdict.BlockedByAntivirus,
        _ => WingetVerdict.Unknown,
    };

    /// <summary>Brak sieci albo źródła: <c>0x8A15000F</c>, <c>0x8A150107</c>, <c>0x8A150008</c>, <c>0x80072EE7</c> (WININET_E_NAME_NOT_RESOLVED, Ruling R15; seria kończy się od razu).</summary>
    public static bool IsNetwork(int exitCode) => Classify(exitCode) == WingetVerdict.NoNetwork;

    /// <summary>Kod szesnastkowo: <c>0x</c> + 8 cyfr wielkimi literami (np. <c>0x8A15000F</c>).</summary>
    public static string Hex(int exitCode) => "0x" + exitCode.ToString("X8", CultureInfo.InvariantCulture);

    /// <summary>
    /// Opis kodu. Dla porażek to tekst wyniku pozycji „Nie wykonano – …” ze spec 5.3 (nieznany kod: sam kod, bez zgadywania
    /// przyczyny). Dla <see cref="WingetVerdict.Success"/>, <see cref="WingetVerdict.SuccessRebootRequired"/> i
    /// <see cref="WingetVerdict.AlreadyInstalled"/> to tylko tekst pomocniczy (np. do logu), <b>nie</b> wynik pozycji: sukces winget
    /// wymaga jeszcze odczytu zwrotnego (spec 5.3), dlatego tekst nie mówi „Zainstalowano”.
    /// </summary>
    public static string Describe(int exitCode) => Classify(exitCode) switch
    {
        WingetVerdict.Success => L.T("winget zgłosił sukces."),
        WingetVerdict.SuccessRebootRequired => L.T("winget zgłosił sukces, potrzebny restart."),
        WingetVerdict.AlreadyInstalled => L.T("Już zainstalowane."),
        WingetVerdict.RebootBeforeInstall => L.T("Nie wykonano – wymaga restartu przed instalacją."),
        WingetVerdict.NoNetwork => L.T("Nie wykonano – brak połączenia z internetem albo ze źródłem winget."),
        WingetVerdict.AdminContextProhibited
            => L.T("Nie wykonano – ten instalator nie działa z uprawnieniami administratora; zainstaluj aplikację ręcznie."),
        WingetVerdict.InstallInProgress => L.T("Nie wykonano – trwa inna instalacja, spróbuj za chwilę."),
        WingetVerdict.NotFound or WingetVerdict.NoApplicableInstaller
            => L.T("Nie wykonano – pakietu nie ma w winget albo nie pasuje do tego komputera."),
        WingetVerdict.HashMismatch => L.T("Nie wykonano – plik instalatora nie zgadza się z manifestem, nie zainstalowano."),
        WingetVerdict.DiskFull => L.T("Nie wykonano – za mało miejsca na dysku."),
        WingetVerdict.BlockedByPolicy => L.T("Nie wykonano – instalację zablokowały zasady tego komputera."),
        WingetVerdict.AgreementsNotAccepted => L.T("Nie wykonano – nie udało się zaakceptować umowy pakietu albo źródła."),
        WingetVerdict.Cancelled
            => L.F("Nie wykonano – instalator został przerwany (kod {0}). Jeśli Windows pokazał komunikat o wirusie albo niechcianym oprogramowaniu, zablokował go antywirus.",
                Hex(exitCode)),
        WingetVerdict.BlockedByAntivirus => L.T("Nie wykonano – antywirus zablokował instalator jako zagrożenie."),
        _ => L.F("Nie wykonano – winget zakończył się kodem {0}.", Hex(exitCode)),
    };
}
