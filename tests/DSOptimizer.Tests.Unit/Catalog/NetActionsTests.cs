using System;
using System.IO;
using System.Linq;
using System.Text;
using DSOptimizer.Catalog;
using DSOptimizer.Core.Catalog;
using DSOptimizer.Core.Model;
using DSOptimizer.Tests.Unit.Support;
using Xunit;

namespace DSOptimizer.Tests.Unit.Catalog;

/// <summary>
/// net.smb-harden (alias lan_panic) i net.network-private (partia C3, Task 5): akcje bez detekcji, journala i undo.
/// Jedyny dowód skutku to kod wyjścia i odczyt zwrotny, więc testy pilnują, że rozjazd odczytu daje kod ≠ 0 (Failed w silniku),
/// a nie zielone „Executed” bez efektu. Skrypty idą przez harness wyłącznie z mockami helperów
/// (Set-/Get-DsoSmbClientSetting, Get-DsoNetConnectionProfile, Set-DsoNetConnectionProfileCategory); nic nie dotyka sieci ani rejestru hosta.
/// </summary>
public class NetActionsTests
{
    private const string HardenId = "net.smb-harden";
    private const string PrivateId = "net.network-private";
    private const string NoProfileMessage = "brak aktywnego połączenia w profilu publicznym – nic nie zmieniono";

    private static TweakDefinition Get(string id)
        => CatalogLoader.Load(BuiltInCatalog.Sources).Definitions.Single(d => d.Id == id);

    private static string Doc(string id)
        => File.ReadAllText(Path.Combine(RepoRoot.Combine("docs", "tweaks"), id + ".md"), Encoding.UTF8);

    private static string AllText(TweakDefinition def) => def.Description + " " + string.Join(" ", def.Warnings);

    private static bool Has(string line, string text) => line.IndexOf(text, StringComparison.Ordinal) >= 0;

    private static string Apply(string id) => Assert.IsType<string>(Get(id).Script!.Apply);

    // ---- katalog ----

    [Fact]
    public void Entries_AreActionsInNetCategory_CautionRisk_AliasOnlyOnHarden()
    {
        var definitions = CatalogLoader.Load(BuiltInCatalog.Sources).Definitions;
        foreach (var id in new[] { HardenId, PrivateId })
        {
            var def = Get(id);
            Assert.Equal("net", def.Category);
            Assert.Equal(TweakKind.Action, def.Kind);
            Assert.Equal(RiskLevel.Caution, def.Risk);
            Assert.Equal(TweakScope.Machine, def.Scope);
            Assert.False(def.RequiresReboot);
            Assert.Empty(def.Registry);
            Assert.Empty(def.Services);
            Assert.Empty(def.Conflicts);
            var script = Assert.IsType<ScriptSet>(def.Script);
            Assert.NotNull(script.Apply);
            Assert.Null(script.Detect); // akcja nie ma detekcji ani undo: nie udajemy stanu, którego silnik nie śledzi
            Assert.Null(script.Undo);
            Assert.Null(script.Capture);
        }

        Assert.Equal(new[] { HardenId }, definitions.Where(d => d.Aliases.Contains("lan_panic")).Select(d => d.Id));
        Assert.Empty(Get(PrivateId).Aliases);
    }

    [Theory]
    [InlineData(HardenId, "Set-SmbClientConfiguration", "Get-SmbClientConfiguration")]
    [InlineData(PrivateId, "Set-NetConnectionProfile", "Get-NetConnectionProfile")]
    public void Script_CallsRealCmdletsOnlyInsideMockedHelperDefinitions_AndNeverTouchesRegistryOrHkcu(string id, string setter, string getter)
    {
        var apply = Apply(id);
        Assert.Equal(1, apply.Split(new[] { setter }, StringSplitOptions.None).Length - 1);
        Assert.Equal(1, apply.Split(new[] { getter }, StringSplitOptions.None).Length - 1);
        Assert.DoesNotContain("HKCU", apply, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("HKLM", apply, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("Format-List", apply, StringComparison.Ordinal);
        Assert.DoesNotContain("Stop-Service", apply, StringComparison.Ordinal);
        Assert.DoesNotContain("Set-Service", apply, StringComparison.Ordinal);
        Assert.DoesNotContain("Set-SmbServerConfiguration", apply, StringComparison.Ordinal);
    }

    [Fact]
    public void HardenWarnings_StateNoUndoHomeChangeAndOverrideOfJournalTweaks_VerbatimFromMicrosoft()
    {
        var def = Get(HardenId);
        var text = AllText(def);
        Assert.Contains("klient", text, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("serwer", text, StringComparison.OrdinalIgnoreCase);
        // Cytaty Microsoftu przeniesione z programu do dokumentu pozycji (spec opisów 2026-10-26, zasada 3).
        Assert.Contains("Windows disables insecure guest logons by default", Doc(HardenId), StringComparison.Ordinal);
        Assert.Contains("We recommend that you don't enable insecure guest logons.", Doc(HardenId), StringComparison.Ordinal);
        Assert.Contains("Bez cofania – ", def.Description, StringComparison.Ordinal); // jedno zdanie o cofaniu na końcu opisu, jak w naprawach
        Assert.Contains("Set-SmbClientConfiguration", text, StringComparison.Ordinal); // ręczne cofnięcie cmdletem MS
        // Nadpisanie tweaków SMB: nazwane tytułami (nie identyfikatorami), a ostrzeżenie o zdjęciu zabezpieczenia widoczne od razu.
        Assert.Contains(Get("net.smb-guest-access").Title, text, StringComparison.Ordinal);
        Assert.Contains(Get("net.smb-signing-client-off").Title, text, StringComparison.Ordinal);
        Assert.Contains(def.Warnings, w => w.Contains("cofnięcie", StringComparison.OrdinalIgnoreCase) && w.Contains("zdejmie", StringComparison.Ordinal));
        Assert.Contains("Home", text, StringComparison.Ordinal);
        Assert.Contains("STATUS_INVALID_SIGNATURE", text, StringComparison.Ordinal);
        Assert.DoesNotContain("zabetonowan", text, StringComparison.OrdinalIgnoreCase); // opis v3.2 nieprawdziwy względem tego, co robi akcja
        Assert.DoesNotContain("NetBIOS", def.Description, StringComparison.Ordinal);
    }

    [Fact]
    public void PrivateWarnings_AreVerbatimFromMicrosoft_AndNameTheFirewallSwitch()
    {
        var def = Get(PrivateId);
        var text = AllText(def);
        // Cytat Microsoftu przeniesiony do dokumentu pozycji; w programie uwaga po polsku o zaufanej sieci.
        Assert.Contains("networks at home or work, where you know and trust the people and devices on the network", Doc(PrivateId), StringComparison.Ordinal);
        Assert.Contains(def.Warnings, w => w.Contains("zaufanej sieci", StringComparison.Ordinal));
        Assert.Contains("discoverable", text, StringComparison.Ordinal);
        Assert.Contains("Private", text, StringComparison.Ordinal);
        Assert.Contains("zapor", text, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("DomainAuthenticated", text, StringComparison.Ordinal);
        Assert.Contains("-NetworkCategory Public", text, StringComparison.Ordinal); // ręczne cofnięcie
        Assert.Contains("Bez cofania – ", def.Description, StringComparison.Ordinal); // jedno zdanie o cofaniu na końcu opisu, jak w naprawach
        Assert.DoesNotContain("Komputery widoczne", text, StringComparison.OrdinalIgnoreCase);
    }

    [Theory]
    [InlineData(HardenId)]
    [InlineData(PrivateId)]
    public void Docs_StateNoUndoAndWhatWasNotTested(string id)
    {
        var doc = Doc(id);
        Assert.Contains("learn.microsoft.com", doc, StringComparison.Ordinal);
        Assert.Contains("brak cofnięcia", doc, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("odczyt zwrotny", doc, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("nie testowane na VM", doc, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("brak sieci", doc, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("Failed", doc, StringComparison.Ordinal);
    }

    [Fact]
    public void HardenDoc_ExplainsHomeVersusProAndTheTwoMicrosoftTextsAboutSigning()
    {
        var doc = Doc(HardenId);
        Assert.Contains("Home", doc, StringComparison.Ordinal);
        Assert.Contains("Pro", doc, StringComparison.Ordinal);
        Assert.Contains("EnableInsecureGuestLogons", doc, StringComparison.Ordinal);
        Assert.Contains("RequireSecuritySignature", doc, StringComparison.Ordinal);
        Assert.Contains("net.smb-guest-access", doc, StringComparison.Ordinal);
        Assert.Contains("net.smb-signing-client-off", doc, StringComparison.Ordinal);
        Assert.Contains("lan_panic", doc, StringComparison.Ordinal);
        Assert.Contains("smb-security-hardening", doc, StringComparison.Ordinal);
    }

    [Fact]
    public void PrivateDoc_ExplainsEnumComparisonNoNetworkOnVmAndDomainRule()
    {
        var doc = Doc(PrivateId);
        Assert.Contains("DomainAuthenticated", doc, StringComparison.Ordinal);
        Assert.Contains("Get-NetConnectionProfile", doc, StringComparison.Ordinal);
        Assert.Contains("Set-NetConnectionProfile", doc, StringComparison.Ordinal);
        Assert.Contains("NetworkCategory", doc, StringComparison.Ordinal);
        Assert.Contains(NoProfileMessage, doc, StringComparison.Ordinal);
        Assert.Contains("MSFT_NetConnectionProfile", doc, StringComparison.Ordinal);
    }

    // ---- net.smb-harden w harnessie (helpery zamockowane) ----

    /// <summary>Stateful model klienta SMB: Set zapisuje, Get czyta; <paramref name="setBody"/> pozwala zasymulować cmdlet, który nic nie zmienia.</summary>
    private static string SmbMocks(bool startGuest, bool startSigning, string? setBody = null, string? getBody = null)
        => "$script:smb = @{ EnableInsecureGuestLogons = $" + startGuest.ToString().ToLowerInvariant() + "; RequireSecuritySignature = $" + startSigning.ToString().ToLowerInvariant() + " }\r\n" +
           "function Set-DsoSmbClientSetting { param($AllowInsecureGuest, $RequireSigning) " +
           (setBody ?? "$script:smb.EnableInsecureGuestLogons = [bool]$AllowInsecureGuest; $script:smb.RequireSecuritySignature = [bool]$RequireSigning") +
           "; Write-Output ('SET guest=' + $AllowInsecureGuest + ' signing=' + $RequireSigning) }\r\n" +
           "function Get-DsoSmbClientSetting { param($Name) " + (getBody ?? "$script:smb[$Name]") + " }";

    [Theory]
    [InlineData(true)] // Home: gość domyślnie True
    [InlineData(false)] // Pro: gość domyślnie False
    public void Harden_SetsGuestOffAndSigningOn_ReadsBack_AndSucceeds(bool startGuest)
    {
        var run = PowerShellHarness.Run(Apply(HardenId), SmbMocks(startGuest, startSigning: false), string.Empty);

        Assert.Equal(0, run.ExitCode);
        Assert.Contains("SET guest=False signing=True", run.Output);
        Assert.Contains(run.Output, line => Has(line, "odczyt zwrotny zgodny"));
        Assert.Empty(run.Error);
    }

    [Fact]
    public void Harden_WhenGuestStaysOn_AfterSet_FailsWithReadBackMessage_NotSilentSuccess()
    {
        var run = PowerShellHarness.Run(Apply(HardenId), SmbMocks(true, true, setBody: "$script:smb.RequireSecuritySignature = $true"), string.Empty);

        Assert.NotEqual(0, run.ExitCode);
        Assert.Contains("odczyt zwrotny", run.Error, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("EnableInsecureGuestLogons", run.Error, StringComparison.Ordinal);
    }

    [Fact]
    public void Harden_WhenSigningStaysOff_AfterSet_FailsWithReadBackMessage()
    {
        var run = PowerShellHarness.Run(Apply(HardenId), SmbMocks(false, false, setBody: "$script:smb.EnableInsecureGuestLogons = $false"), string.Empty);

        Assert.NotEqual(0, run.ExitCode);
        Assert.Contains("odczyt zwrotny", run.Error, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("RequireSecuritySignature", run.Error, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("$null")]
    [InlineData("'False'")] // tekst nie jest wartością logiczną
    [InlineData("0")]
    [InlineData("throw 'Get-SmbClientConfiguration niedostępny'")]
    public void Harden_WhenReadBackIsNotBoolean_IsAFailure_NotAnAnswer(string getBody)
    {
        var run = PowerShellHarness.Run(Apply(HardenId), SmbMocks(true, false, getBody: getBody), string.Empty);

        Assert.NotEqual(0, run.ExitCode);
        Assert.NotEmpty(run.Error);
    }

    [Fact]
    public void Harden_WhenSetThrows_FailsAndReportsNoSuccess()
    {
        var run = PowerShellHarness.Run(Apply(HardenId), SmbMocks(true, true, setBody: "throw 'Set-SmbClientConfiguration: odmowa dostępu'"), string.Empty);

        Assert.NotEqual(0, run.ExitCode);
        Assert.Contains("odmowa dostępu", run.Error, StringComparison.Ordinal);
    }

    [Fact]
    public void Harden_WithoutMocks_HitsGuard_AndNeverTouchesHostSmbConfiguration()
    {
        var run = PowerShellHarness.Run(Apply(HardenId), mocks: string.Empty, snapshot: string.Empty);

        Assert.NotEqual(0, run.ExitCode);
        Assert.Contains("Bezpiecznik harnessu", run.Error, StringComparison.Ordinal);
    }

    // ---- net.network-private w harnessie (helpery zamockowane, NetworkCategory jako prawdziwy enum) ----

    private const string EnumDefinition =
        "Add-Type -TypeDefinition 'public enum DsoCategory { Public = 0, Private = 1, DomainAuthenticated = 2 } public enum DsoConnectivity { Disconnected = 0, NoTraffic = 1, Subnet = 2, LocalNetwork = 3, Internet = 4 }'\r\n";

    private static string Profile(string name, int index, string category, string ipv4, string ipv6 = "Disconnected")
        => $"[pscustomobject]@{{ Name = '{name}'; InterfaceAlias = 'Karta{index}'; InterfaceIndex = {index}; NetworkCategory = [DsoCategory]'{category}'; IPv4Connectivity = [DsoConnectivity]'{ipv4}'; IPv6Connectivity = [DsoConnectivity]'{ipv6}' }}";

    private static string ProfileMocks(string profiles, string? setBody = null, string? getBody = null)
        => EnumDefinition +
           "$script:profiles = @(" + profiles + ")\r\n" +
           "function Get-DsoNetConnectionProfile { " + (getBody ?? "$script:profiles") + " }\r\n" +
           "function Set-DsoNetConnectionProfileCategory { param($NetProfile, $Category) " +
           (setBody ?? "$target = $script:profiles | Where-Object { $_.InterfaceIndex -eq $NetProfile.InterfaceIndex }; $target.NetworkCategory = [DsoCategory]$Category") +
           "; Write-Output ('SET ' + $NetProfile.Name + ' -> ' + $Category) }";

    [Fact]
    public void Private_ChangesOnePublicActiveProfile_ToPrivate_PrintsNameAndPreviousCategory()
    {
        var run = PowerShellHarness.Run(Apply(PrivateId), ProfileMocks(Profile("Siec1", 5, "Public", "Internet")), string.Empty);

        Assert.Equal(0, run.ExitCode);
        Assert.Contains("SET Siec1 -> Private", run.Output);
        Assert.Contains(run.Output, line => Has(line, "'Siec1'") && Has(line, "Public -> Private") && !line.StartsWith("SET", StringComparison.Ordinal));
        Assert.Contains(run.Output, line => Has(line, "-NetworkCategory Public")); // wskazówka ręcznego cofnięcia
    }

    [Fact]
    public void Private_SkipsDomainAuthenticatedPrivateAndDisconnectedPublic_ChangesOnlyActivePublic()
    {
        var profiles = string.Join(
            ", ",
            Profile("Domena", 3, "DomainAuthenticated", "Internet"),
            Profile("Dom", 4, "Private", "Internet"),
            Profile("Odlaczona", 5, "Public", "Disconnected", "Disconnected"),
            Profile("Kawiarnia", 6, "Public", "Internet"));
        var run = PowerShellHarness.Run(Apply(PrivateId), ProfileMocks(profiles), string.Empty);

        Assert.Equal(0, run.ExitCode);
        var sets = run.Output.Where(l => l.StartsWith("SET ", StringComparison.Ordinal)).ToList();
        Assert.Equal(new[] { "SET Kawiarnia -> Private" }, sets);
        Assert.DoesNotContain(run.Output, l => l.StartsWith("SET Domena", StringComparison.Ordinal));
    }

    [Fact]
    public void Private_PublicProfileWithOnlyIpv6Connectivity_CountsAsActive()
    {
        var run = PowerShellHarness.Run(Apply(PrivateId), ProfileMocks(Profile("Tylko6", 7, "Public", "Disconnected", "Internet")), string.Empty);

        Assert.Equal(0, run.ExitCode);
        Assert.Contains("SET Tylko6 -> Private", run.Output);
    }

    [Fact]
    public void Private_ChangesEveryActivePublicProfile_Each_ToPrivateOnly()
    {
        var profiles = string.Join(", ", Profile("A", 1, "Public", "Internet"), Profile("B", 2, "Public", "LocalNetwork"));
        var run = PowerShellHarness.Run(Apply(PrivateId), ProfileMocks(profiles), string.Empty);

        Assert.Equal(0, run.ExitCode);
        var sets = run.Output.Where(l => l.StartsWith("SET ", StringComparison.Ordinal)).ToList();
        Assert.Equal(new[] { "SET A -> Private", "SET B -> Private" }, sets);
        Assert.All(sets, s => Assert.EndsWith("-> Private", s, StringComparison.Ordinal)); // nigdy nie ustawiamy Public ani Domain
    }

    [Theory]
    [InlineData("")] // brak profili (VM bez sieci: Get-NetConnectionProfile zwraca 0 obiektów)
    [InlineData("$null")]
    public void Private_WithNoProfiles_FailsWithMessage_AndChangesNothing(string profiles)
    {
        var run = PowerShellHarness.Run(Apply(PrivateId), ProfileMocks(profiles), string.Empty);

        Assert.NotEqual(0, run.ExitCode);
        Assert.Contains(NoProfileMessage, run.Error, StringComparison.Ordinal);
        Assert.DoesNotContain(run.Output, l => l.StartsWith("SET ", StringComparison.Ordinal));
    }

    [Fact]
    public void Private_WithOnlyPrivateDomainOrDisconnectedPublic_FailsWithMessage_AndChangesNothing()
    {
        var profiles = string.Join(
            ", ",
            Profile("Domena", 3, "DomainAuthenticated", "Internet"),
            Profile("Dom", 4, "Private", "Internet"),
            Profile("Odlaczona", 5, "Public", "Disconnected", "Disconnected"));
        var run = PowerShellHarness.Run(Apply(PrivateId), ProfileMocks(profiles), string.Empty);

        Assert.NotEqual(0, run.ExitCode);
        Assert.Contains(NoProfileMessage, run.Error, StringComparison.Ordinal);
        Assert.DoesNotContain(run.Output, l => l.StartsWith("SET ", StringComparison.Ordinal));
    }

    [Fact]
    public void Private_WhenReadBackStillPublic_Fails_NotSilentSuccess()
    {
        var run = PowerShellHarness.Run(Apply(PrivateId), ProfileMocks(Profile("Siec1", 5, "Public", "Internet"), setBody: "$null = 1"), string.Empty);

        Assert.NotEqual(0, run.ExitCode);
        Assert.Contains("odczyt zwrotny", run.Error, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("Siec1", run.Error, StringComparison.Ordinal);
    }

    [Fact]
    public void Private_WhenProfileVanishesAfterChange_IsAFailure()
    {
        var run = PowerShellHarness.Run(
            Apply(PrivateId),
            ProfileMocks(Profile("Siec1", 5, "Public", "Internet"), setBody: "$script:profiles = @()"),
            string.Empty);

        Assert.NotEqual(0, run.ExitCode);
        Assert.Contains("odczyt zwrotny", run.Error, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Private_WhenSetFailsOnSecondProfile_Fails_AndReportsWhatWasAlreadyChangedForManualRevert()
    {
        var profiles = string.Join(", ", Profile("Pierwsza", 1, "Public", "Internet"), Profile("Druga", 2, "Public", "Internet"));
        var setBody = "if ($NetProfile.Name -eq 'Druga') { throw 'Set-NetConnectionProfile: odmowa' }; $t = $script:profiles | Where-Object { $_.InterfaceIndex -eq $NetProfile.InterfaceIndex }; $t.NetworkCategory = [DsoCategory]$Category";
        var run = PowerShellHarness.Run(Apply(PrivateId), ProfileMocks(profiles, setBody), string.Empty);

        Assert.NotEqual(0, run.ExitCode);
        Assert.Contains("Druga", run.Error, StringComparison.Ordinal);
        Assert.Contains("odmowa", run.Error, StringComparison.Ordinal);
        Assert.Contains("'Pierwsza' (poprzednio Public, indeks 1)", run.Error, StringComparison.Ordinal); // już zmieniony: komunikat podaje go do ręcznego cofnięcia
    }

    [Fact]
    public void Private_WhenSecondReadThrowsAfterChange_ReportsAlreadyChangedProfiles()
    {
        var getBody = "$script:reads++; if ($script:reads -ge 2) { throw 'odczyt profili: awaria' }; $script:profiles";
        var run = PowerShellHarness.Run(
            Apply(PrivateId),
            "$script:reads = 0\r\n" + ProfileMocks(Profile("Siec1", 5, "Public", "Internet"), getBody: getBody),
            string.Empty);

        Assert.NotEqual(0, run.ExitCode);
        Assert.Contains("awaria", run.Error, StringComparison.Ordinal);
        Assert.Contains("'Siec1' (poprzednio Public, indeks 5)", run.Error, StringComparison.Ordinal);
    }

    [Fact]
    public void Private_ComparesCategoryByEnumName_NotByNumberOrLocalizedText()
    {
        // NetworkCategory jako liczba całkowita (0 = Public w enumie) nie jest nazwą: skrypt porównuje [string] z nazwą 'Public',
        // więc profil z surową liczbą nie zostanie ruszony (lepiej odmówić niż zgadywać numerację, której MS nie opublikowało).
        var raw = "[pscustomobject]@{ Name = 'Liczba'; InterfaceAlias = 'K'; InterfaceIndex = 9; NetworkCategory = 0; IPv4Connectivity = 'Internet'; IPv6Connectivity = 'Disconnected' }";
        var run = PowerShellHarness.Run(Apply(PrivateId), ProfileMocks(raw), string.Empty);

        Assert.NotEqual(0, run.ExitCode);
        Assert.Contains(NoProfileMessage, run.Error, StringComparison.Ordinal);
        Assert.DoesNotContain(run.Output, l => l.StartsWith("SET ", StringComparison.Ordinal));
    }

    [Fact]
    public void Private_WithoutMocks_HitsGuard_AndNeverTouchesHostNetworkProfiles()
    {
        var run = PowerShellHarness.Run(Apply(PrivateId), mocks: string.Empty, snapshot: string.Empty);

        Assert.NotEqual(0, run.ExitCode);
        Assert.Contains("Bezpiecznik harnessu", run.Error, StringComparison.Ordinal);
    }

    [Fact]
    public void Private_WithOnlyTheReadMocked_StillCannotChangeAnything_SetterGuardHolds()
    {
        // Mock odczytu bez mocka zapisu: próba zmiany profilu trafia w bezpiecznik helpera, nie w prawdziwy Set-NetConnectionProfile.
        var mocks = EnumDefinition + "function Get-DsoNetConnectionProfile { " + Profile("Siec1", 5, "Public", "Internet") + " }";
        var run = PowerShellHarness.Run(Apply(PrivateId), mocks, string.Empty);

        Assert.NotEqual(0, run.ExitCode);
        Assert.Contains("Bezpiecznik harnessu", run.Error, StringComparison.Ordinal);
    }
}
