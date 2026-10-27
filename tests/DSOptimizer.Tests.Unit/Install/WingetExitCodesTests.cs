using System;
using DSOptimizer.Core.Install;
using Xunit;

namespace DSOptimizer.Tests.Unit.Install;

public class WingetExitCodesTests
{
    [Theory]
    [InlineData(0x8A150061u, WingetVerdict.AlreadyInstalled, "Już zainstalowane.")]
    [InlineData(0x8A15010Du, WingetVerdict.AlreadyInstalled, "Już zainstalowane.")]
    [InlineData(0x8A15002Bu, WingetVerdict.AlreadyInstalled, "Już zainstalowane.")]
    [InlineData(0x8A150109u, WingetVerdict.SuccessRebootRequired, "winget zgłosił sukces, potrzebny restart.")]
    [InlineData(0x8A15010Au, WingetVerdict.RebootBeforeInstall, "Nie wykonano – wymaga restartu przed instalacją.")]
    [InlineData(0x8A15000Fu, WingetVerdict.NoNetwork, "Nie wykonano – brak połączenia z internetem albo ze źródłem winget.")]
    [InlineData(0x8A150107u, WingetVerdict.NoNetwork, "Nie wykonano – brak połączenia z internetem albo ze źródłem winget.")]
    [InlineData(0x8A150008u, WingetVerdict.NoNetwork, "Nie wykonano – brak połączenia z internetem albo ze źródłem winget.")]
    [InlineData(0x8A150056u, WingetVerdict.AdminContextProhibited, "Nie wykonano – ten instalator nie działa z uprawnieniami administratora; zainstaluj aplikację ręcznie.")]
    [InlineData(0x8A150102u, WingetVerdict.InstallInProgress, "Nie wykonano – trwa inna instalacja, spróbuj za chwilę.")]
    [InlineData(0x8A150014u, WingetVerdict.NotFound, "Nie wykonano – pakietu nie ma w winget albo nie pasuje do tego komputera.")]
    [InlineData(0x8A150010u, WingetVerdict.NoApplicableInstaller, "Nie wykonano – pakietu nie ma w winget albo nie pasuje do tego komputera.")]
    [InlineData(0x8A150011u, WingetVerdict.HashMismatch, "Nie wykonano – plik instalatora nie zgadza się z manifestem, nie zainstalowano.")]
    [InlineData(0x8A150105u, WingetVerdict.DiskFull, "Nie wykonano – za mało miejsca na dysku.")]
    [InlineData(0x8A15003Au, WingetVerdict.BlockedByPolicy, "Nie wykonano – instalację zablokowały zasady tego komputera.")]
    [InlineData(0x8A150041u, WingetVerdict.AgreementsNotAccepted, "Nie wykonano – nie udało się zaakceptować umowy pakietu albo źródła.")]
    [InlineData(0x8A150046u, WingetVerdict.AgreementsNotAccepted, "Nie wykonano – nie udało się zaakceptować umowy pakietu albo źródła.")]
    public void Classify_SpecCode_VerdictAndText(uint code, WingetVerdict verdict, string text)
    {
        var exitCode = unchecked((int)code);

        Assert.Equal(verdict, WingetExitCodes.Classify(exitCode));
        Assert.Equal(text, WingetExitCodes.Describe(exitCode));
    }

    [Fact]
    public void Zero_Success()
    {
        Assert.Equal(WingetVerdict.Success, WingetExitCodes.Classify(0));
        Assert.Equal("winget zgłosił sukces.", WingetExitCodes.Describe(0));
        Assert.Equal("0x00000000", WingetExitCodes.Hex(0));
    }

    [Fact]
    public void UnknownCode_UnknownWithHex()
    {
        var code = unchecked((int)0x8A150099);

        Assert.Equal(WingetVerdict.Unknown, WingetExitCodes.Classify(code));
        Assert.Equal("0x8A150099", WingetExitCodes.Hex(code));
        Assert.Equal("Nie wykonano – winget zakończył się kodem 0x8A150099.", WingetExitCodes.Describe(code));
    }

    [Theory]
    [InlineData(1, "0x00000001")]
    [InlineData(-1, "0xFFFFFFFF")]
    [InlineData(1603, "0x00000643")]
    public void OtherNonZero_Unknown(int code, string hex)
    {
        Assert.Equal(WingetVerdict.Unknown, WingetExitCodes.Classify(code));
        Assert.Equal(hex, WingetExitCodes.Hex(code));
    }

    [Fact]
    public void Classify_0x80072EE7_NoNetwork()
    {
        var code = unchecked((int)0x80072EE7);
        Assert.Equal(WingetVerdict.NoNetwork, WingetExitCodes.Classify(code));
        Assert.Equal("Nie wykonano – brak połączenia z internetem albo ze źródłem winget.", WingetExitCodes.Describe(code));
    }

    [Fact]
    public void IsNetwork_0x80072EE7()
    {
        Assert.True(WingetExitCodes.IsNetwork(unchecked((int)0x80072EE7)));
    }

    [Theory]
    [InlineData(0x8A15000Fu, true)]
    [InlineData(0x8A150107u, true)]
    [InlineData(0x8A150008u, true)]
    [InlineData(0x8A150014u, false)]
    [InlineData(0x8A150056u, false)]
    [InlineData(0x8A150102u, false)]
    [InlineData(0x8A150099u, false)]
    [InlineData(0u, false)]
    public void IsNetwork_OnlyNetworkCodes(uint code, bool expected)
    {
        Assert.Equal(expected, WingetExitCodes.IsNetwork(unchecked((int)code)));
    }

    [Fact]
    public void Cancelled_0x800704C7_VerdictAndText()
    {
        var code = unchecked((int)0x800704C7);

        Assert.Equal(WingetVerdict.Cancelled, WingetExitCodes.Classify(code));
        Assert.Equal(
            "Nie wykonano – instalator został przerwany (kod 0x800704C7). Jeśli Windows pokazał komunikat o wirusie albo niechcianym oprogramowaniu, zablokował go antywirus.",
            WingetExitCodes.Describe(code));
        Assert.Equal(WingetExitCodes.Describe(code), InstallText.FailedWithCode(code, null));
    }

    [Theory]
    [InlineData(0x800700E1u)]
    [InlineData(0x800700E2u)]
    public void BlockedByAntivirus_VerdictAndText(uint raw)
    {
        var code = unchecked((int)raw);

        Assert.Equal(WingetVerdict.BlockedByAntivirus, WingetExitCodes.Classify(code));
        Assert.Equal("Nie wykonano – antywirus zablokował instalator jako zagrożenie.", WingetExitCodes.Describe(code));
        Assert.EndsWith(" Kod " + WingetExitCodes.Hex(code) + ".", InstallText.FailedWithCode(code, null), StringComparison.Ordinal);
    }

    [Fact]
    public void WithoutLog_StripsFirstMarker()
    {
        Assert.Equal("Tekst.", InstallText.WithoutLog(@"Tekst. Log winget: C:\a.log"));
        Assert.Equal("Tekst.", InstallText.WithoutLog(@"Tekst. Log MSI: C:\a.log"));
        Assert.Equal("Tekst.", InstallText.WithoutLog("Tekst."));
        Assert.Equal("A", InstallText.WithoutLog("A Log MSI: x Log winget: y"));
    }
}
