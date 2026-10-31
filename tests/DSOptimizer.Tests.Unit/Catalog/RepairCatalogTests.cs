using System;
using System.IO;
using System.Linq;
using System.Text;
using DSOptimizer.Catalog;
using DSOptimizer.Core.Catalog;
using DSOptimizer.Core.Engine;
using DSOptimizer.Core.Localization;
using DSOptimizer.Core.Model;
using DSOptimizer.Tests.Unit.Support;
using Xunit;

namespace DSOptimizer.Tests.Unit.Catalog;

/// <summary>
/// Kategoria repair (Etap 4b, partia 1): <c>repair.dns-flush</c> i <c>repair.network-reset</c> (Task 5). Wspólny plik testów dla Tasków 5-9.
/// Skrypty idą przez harness wyłącznie z mockami (Get-/Clear-DnsClientCache, Get-NetIPConfiguration, netsh, helper pliku hosts);
/// bezpieczniki harnessu rzucają przy każdym niezamockowanym wywołaniu. Nic nie dotyka hosta.
/// </summary>
public class RepairCatalogTests
{
    private const string DnsId = "repair.dns-flush";
    private const string NetResetId = "repair.network-reset";
    private const string WuResetId = "repair.wu-reset";
    private const string WuResetFullId = "repair.wu-reset-full";
    private const string RestorePointId = "repair.restore-point";
    private const string SystemProtectionId = "repair.system-protection";
    private const string WingetId = "repair.winget";

    private static TweakDefinition Get(string id)
        => CatalogLoader.Load(BuiltInCatalog.Sources).Definitions.Single(d => d.Id == id);

    private static string Apply(string id) => Assert.IsType<string>(Get(id).Script!.Apply);

    private static string Doc(string id)
        => File.ReadAllText(RepoRoot.Combine("docs", "tweaks", id + ".md"), Encoding.UTF8).Replace("\r\n", "\n");

    // ---- katalog ----

    [Fact]
    public void Catalog_LoadsWithoutValidationErrors()
    {
        var catalog = CatalogLoader.Load(BuiltInCatalog.Sources);
        Assert.Empty(CatalogValidator.Validate(catalog.Definitions));
    }

    [Theory]
    [InlineData(DnsId, RiskLevel.Safe, false)]
    [InlineData(NetResetId, RiskLevel.Caution, true)]
    public void Entry_IsScriptActionInRepairCategory_WithGlobalConstraintsRisk(string id, RiskLevel risk, bool reboot)
    {
        var def = Get(id);
        Assert.Equal("repair", def.Category);
        Assert.Equal(TweakKind.Action, def.Kind);
        Assert.Equal(risk, def.Risk);
        Assert.Equal(TweakScope.Machine, def.Scope);
        Assert.Equal(reboot, def.RequiresReboot);
        Assert.False(def.Suggested);
        Assert.Empty(def.Registry);
        Assert.Empty(def.Services);
        Assert.Null(def.Launch);
        var script = Assert.IsType<ScriptSet>(def.Script);
        Assert.NotNull(script.Apply);
        Assert.Null(script.Detect);
        Assert.Null(script.Undo);
        Assert.True(def.Timeout >= TimeSpan.FromSeconds(60), "timeoutSeconds: zmierzony czas x 3, min. 60");
        Assert.True(def.Summary.Length <= 160);
        Assert.True(def.Benefit!.Length <= 220);
    }

    [Theory]
    [InlineData(DnsId)]
    [InlineData(NetResetId)]
    public void Scripts_NeverUseHkcuNorForbiddenCommands(string id)
    {
        var apply = Apply(id);
        Assert.DoesNotContain("HKCU", apply, StringComparison.OrdinalIgnoreCase);
        Assert.False(IsForbiddenIpconfig(apply), "ipconfig /release|/renew|/flushdns jest zakazany");
        Assert.DoesNotContain("netcfg", apply, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("winhttp", apply, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("regsvr32", apply, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("Invoke-WebRequest", apply, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>ipconfig /release|/renew|/flushdns jest zakazany (Global Constraints); granica słowa, żeby Get-NetIPConfiguration nie liczył się jako ipconfig.</summary>
    private const string IpconfigPattern = @"(?<![A-Za-z])ipconfig(\.exe)?\b";

    private static bool IsForbiddenIpconfig(string script)
        => System.Text.RegularExpressions.Regex.IsMatch(script, IpconfigPattern, System.Text.RegularExpressions.RegexOptions.IgnoreCase);

    [Theory]
    [InlineData("ipconfig /release", true)]
    [InlineData("& ipconfig.exe /renew", true)]
    [InlineData("$x = IPCONFIG /flushdns", true)]
    [InlineData("ipconfig", true)]
    [InlineData("Get-NetIPConfiguration -All", false)]
    [InlineData("$ipconfiguration = 1", false)]
    [InlineData("Get-DsoIpconfigInfo", false)]
    public void IpconfigPattern_MatchesOnlyTheTool(string script, bool forbidden)
        => Assert.Equal(forbidden, IsForbiddenIpconfig(script));

    [Theory]
    [InlineData(DnsId)]
    [InlineData(NetResetId)]
    public void IpconfigGuard_CatchesInjectedCallInRealScript(string id)
    {
        Assert.False(IsForbiddenIpconfig(Apply(id)));
        Assert.True(IsForbiddenIpconfig(Apply(id) + "\r\nipconfig /release"), "wstrzyknięte ipconfig /release musi zostać wykryte");
    }

    [Fact]
    public void NetworkReset_Warnings_MatchMeasurement()
    {
        var def = Get(NetResetId);
        var text = string.Join(" ", def.Warnings);
        Assert.Contains("restart", text, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("adres IP", text, StringComparison.Ordinal);
        Assert.Contains("maska", text, StringComparison.Ordinal);
        Assert.Contains("brama", text, StringComparison.Ordinal);
        Assert.Contains("DHCP", text, StringComparison.Ordinal);
        Assert.Contains("DNS", text, StringComparison.Ordinal);
        Assert.Contains("zostają", text, StringComparison.Ordinal);
        Assert.Contains("VPN", text, StringComparison.Ordinal);
        Assert.Contains("w logu", text, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(WuResetId, RiskLevel.Caution, ActionHandlerNames.WuReset)]
    [InlineData(WuResetFullId, RiskLevel.Experimental, ActionHandlerNames.WuResetFull)]
    public void WuReset_IsHandlerActionInRepairCategory_WithGlobalConstraintsRisk(string id, RiskLevel risk, string handler)
    {
        var def = Get(id);
        Assert.Equal("repair", def.Category);
        Assert.Equal(TweakKind.Action, def.Kind);
        Assert.Equal(risk, def.Risk);
        Assert.Equal(TweakScope.Machine, def.Scope);
        Assert.Equal(handler, def.Handler);
        Assert.Null(def.Script);
        Assert.Null(def.Launch);
        Assert.False(def.RequiresReboot);
        Assert.False(def.Suggested);
        Assert.Empty(def.Registry);
        Assert.Empty(def.Services);
        Assert.True(def.Timeout >= TimeSpan.FromSeconds(60), "timeoutSeconds: zmierzony czas x 3, min. 60");
        Assert.True(def.Summary.Length <= 160);
        Assert.True(def.Benefit!.Length <= 220);
    }

    [Theory]
    [InlineData(WuResetId)]
    [InlineData(WuResetFullId)]
    public void WuReset_Warnings_MatchMeasurement(string id)
    {
        // Spec opisów 2026-10-26: w uwagach tylko to, co zmienia decyzję; szczegóły techniczne w description,
        // dopisek o wpisach definicji Defendera w docs/tweaks.
        var def = Get(id);
        Assert.Contains("Historia aktualizacji w Ustawieniach zostanie wyczyszczona, a aktualizacje pobiorą się od nowa.", string.Join(" ", def.Warnings), StringComparison.Ordinal);
        Assert.Contains("AppIDSvc", def.Description, StringComparison.Ordinal);
        Assert.Contains(".dso-", def.Description, StringComparison.Ordinal);
        Assert.Contains("zostać mogą tylko wpisy definicji Microsoft Defender", Doc(id), StringComparison.Ordinal);
    }

    [Fact]
    public void WuResetFull_Warnings_CoverPoliciesDomainMdmAndBackups()
    {
        var def = Get(WuResetFullId);
        Assert.Contains("świadomie", string.Join(" ", def.Warnings), StringComparison.Ordinal);
        Assert.Contains(@"%ProgramData%\DSOptimizer\backups", def.Description, StringComparison.Ordinal);
        Assert.Contains("domenie", def.Description, StringComparison.Ordinal);
        Assert.Contains("Intune/MDM", def.Description, StringComparison.Ordinal);
        Assert.Contains("typy uruchamiania", def.Description, StringComparison.Ordinal);
    }

    [Fact]
    public void WuReset_Basic_SaysPoliciesAreOnlyListed()
        => Assert.Contains("tylko je wypisuje", Get(WuResetId).Description, StringComparison.Ordinal);

    [Theory]
    [InlineData(DnsId)]
    [InlineData(NetResetId)]
    [InlineData(WuResetId)]
    [InlineData(WuResetFullId)]
    [InlineData(RestorePointId)]
    [InlineData(SystemProtectionId)]
    [InlineData(WingetId)]
    [InlineData("repair.system-files")]
    [InlineData("repair.disk-cleanup")]
    [InlineData("repair.windows-old")]
    public void Docs_ExistWithObservationSectionForHomeAndPro(string id)
    {
        var doc = Doc(id);
        Assert.Contains("## Obserwacja skutku", doc, StringComparison.Ordinal);
        Assert.Contains("**Home**", doc, StringComparison.Ordinal);
        Assert.Contains("**Pro**", doc, StringComparison.Ordinal);
        Assert.Contains("2026-10-22", doc, StringComparison.Ordinal);
    }

    // ---- repair.dns-flush w harnessie ----

    private static string DnsEntry(string entry, string type = "A")
        => $"[pscustomobject]@{{ Entry = '{entry}'; Name = '{entry}'; Type = {(type == "A" ? 1 : 12)}; Status = 0; TimeToLive = 100 }}";

    private static string DnsMocks(string before, string after, string hosts = "'127.0.0.1 localhost', '10.123.45.67 dso-host.lan dso-alias # komentarz', '# 1.1.1.1 zakomentowany'", string? clearBody = null, string? beforeBody = null, string? afterBody = null)
        => "$script:cleared = $false\r\n" +
           "function Get-DnsClientCache { if (-not $script:cleared) { Write-Host 'CALL read-before'; " + (beforeBody ?? "") + "; @(" + before + ") } else { Write-Host 'CALL read-after'; " + (afterBody ?? "") + "; @(" + after + ") } }\r\n" +
           "function Clear-DnsClientCache { Write-Host 'CALL clear'; " + (clearBody ?? "") + "; $script:cleared = $true }\r\n" +
           "function Get-DsoHostsText { @(" + hosts + ") }\r\n";

    private static ScriptRun RunDns(string mocks) => PowerShellHarness.Run(Apply(DnsId), mocks, string.Empty);

    private static string Join(params string[] items) => string.Join(", ", items);

    [Fact]
    public void DnsFlush_CallsReadBeforeClearAndReadAfter_InOrder_AndReportsCounts()
    {
        var run = RunDns(DnsMocks(
            Join(DnsEntry("www.microsoft.com"), DnsEntry("wpad"), DnsEntry("dso-host.lan"), DnsEntry("67.45.123.10.in-addr.arpa", "PTR")),
            Join(DnsEntry("dso-host.lan"), DnsEntry("67.45.123.10.in-addr.arpa", "PTR"))));

        Assert.Equal(0, run.ExitCode);
        Assert.Empty(run.Error);
        Assert.Equal(new[] { "CALL read-before", "CALL clear", "CALL read-after" }, run.Output.Where(l => l.StartsWith("CALL", StringComparison.Ordinal)).ToArray());
        Assert.Equal("Wyczyszczono pamięć DNS (przed: 4 wpisów, po: 2 – tylko wpisy z pliku hosts).", run.LastLine);
    }

    [Fact]
    public void DnsFlush_EmptyCacheAfter_IsSuccess()
    {
        var run = RunDns(DnsMocks(Join(DnsEntry("www.microsoft.com")), string.Empty));

        Assert.Equal(0, run.ExitCode);
        Assert.Equal("Wyczyszczono pamięć DNS (przed: 1 wpisów, po: 0).", run.LastLine);
    }

    [Fact]
    public void DnsFlush_HostsNamesAreMatchedCaseInsensitivelyAndWithTrailingDot_AnyTypeAndStatus()
    {
        var run = RunDns(DnsMocks(
            Join(DnsEntry("DSO-ALIAS.")),
            Join("[pscustomobject]@{ Entry = 'DSO-ALIAS.'; Name = 'DSO-ALIAS.'; Type = 28; Status = 9501; TimeToLive = 5 }", DnsEntry("1.0.0.0.0.0.0.0.0.0.0.0.0.0.0.0.0.0.0.0.0.0.0.0.0.0.0.0.0.0.0.1.ip6.arpa", "PTR"))));

        Assert.Equal(0, run.ExitCode);
        Assert.Equal("Wyczyszczono pamięć DNS (przed: 1 wpisów, po: 2 – tylko wpisy z pliku hosts).", run.LastLine);
    }

    [Fact]
    public void DnsFlush_CommentedHostsNameIsNotAnException()
    {
        // „zakomentowany" występuje w hosts tylko za znakiem #, więc wpis o tej nazwie po czyszczeniu to porażka.
        var run = RunDns(DnsMocks(Join(DnsEntry("zakomentowany")), Join(DnsEntry("zakomentowany"))));

        Assert.NotEqual(0, run.ExitCode);
        Assert.Contains("Pamięć DNS nie została wyczyszczona: zostało 1 wpisów.", run.Error, StringComparison.Ordinal);
    }

    [Fact]
    public void DnsFlush_NonHostsEntryLeft_Throws_WithCount()
    {
        var run = RunDns(DnsMocks(
            Join(DnsEntry("www.microsoft.com"), DnsEntry("dso-host.lan")),
            Join(DnsEntry("www.microsoft.com"), DnsEntry("example.org"), DnsEntry("dso-host.lan"))));

        Assert.NotEqual(0, run.ExitCode);
        Assert.Contains("Pamięć DNS nie została wyczyszczona: zostało 2 wpisów.", run.Error, StringComparison.Ordinal);
        Assert.DoesNotContain("Wyczyszczono", string.Join("\n", run.Output), StringComparison.Ordinal);
    }

    [Fact]
    public void DnsFlush_ReadBeforeFails_DoesNotClear()
    {
        var run = RunDns(DnsMocks(string.Empty, string.Empty, beforeBody: "throw 'DNS Client niedostępny'"));

        Assert.NotEqual(0, run.ExitCode);
        Assert.Contains("DNS Client niedostępny", run.Error, StringComparison.Ordinal);
        Assert.Contains("nic nie zmieniono", run.Error, StringComparison.Ordinal);
        Assert.DoesNotContain("CALL clear", run.Output);
    }

    [Fact]
    public void DnsFlush_ClearFails_ThrowsWithoutReadBack()
    {
        var run = RunDns(DnsMocks(Join(DnsEntry("a.example")), string.Empty, clearBody: "throw 'odmowa'"));

        Assert.NotEqual(0, run.ExitCode);
        Assert.Contains("Czyszczenie pamięci DNS nie powiodło się: odmowa", run.Error, StringComparison.Ordinal);
        Assert.DoesNotContain("CALL read-after", run.Output);
    }

    [Fact]
    public void DnsFlush_ReadBackFails_IsNotSuccess()
    {
        var run = RunDns(DnsMocks(Join(DnsEntry("a.example")), string.Empty, afterBody: "throw 'timeout'"));

        Assert.NotEqual(0, run.ExitCode);
        Assert.Contains("odczyt zwrotny", run.Error, StringComparison.Ordinal);
        Assert.Contains("skutku nie potwierdzono", run.Error, StringComparison.Ordinal);
        Assert.DoesNotContain("Wyczyszczono", string.Join("\n", run.Output), StringComparison.Ordinal);
    }

    [Fact]
    public void DnsFlush_HostsFileUnreadable_Throws_AfterClearing()
    {
        var run = RunDns(DnsMocks(Join(DnsEntry("a.example")), string.Empty).Replace("function Get-DsoHostsText { @(", "function Get-DsoHostsText { throw 'brak dostępu'; @("));

        Assert.NotEqual(0, run.ExitCode);
        Assert.Contains("hosts", run.Error, StringComparison.Ordinal);
        Assert.Contains("skutku nie potwierdzono", run.Error, StringComparison.Ordinal);
    }

    // ---- repair.network-reset w harnessie ----

    /// <summary>Wyjście <c>netsh int ip reset</c> z VM 26H2 Home (A2, 2026-10-22, strona kodowa OEM 852), dosłownie.</summary>
    private static readonly string[] RealIpResetOutput =
    {
        "Resetting Compartment Forwarding, OK!", "Resetting Compartment, OK!", "Resetting Control Protocol, OK!", "Resetting Echo Sequence Request, OK!",
        "Resetting Global, OK!", "Resetting Interface, OK!", "Resetting Anycast Address, OK!", "Resetting Multicast Address, OK!", "Resetting Unicast Address, OK!",
        "Resetting Neighbor, OK!", "Resetting Path, OK!", "Resetting Potential, OK!", "Resetting Prefix Policy, OK!", "Resetting Proxy Neighbor, OK!",
        "Resetting Route, OK!", "Resetting Site Prefix, OK!", "Resetting Subinterface, OK!", "Resetting Wakeup Pattern, OK!", "Resetting Resolve Neighbor, OK!",
        "Resetting , OK!", "Resetting , OK!", "Resetting , OK!", "Resetting , OK!",
        "Resetting , failed.", "Odmowa dost─Öpu.", string.Empty,
        "Resetting , OK!", "Resetting , OK!", "Resetting , OK!", "Resetting , OK!", "Resetting , OK!", "Resetting , OK!", "Resetting , OK!", "Resetting , OK!", "Resetting , OK!", "Resetting , OK!",
        "Restart the computer to complete this action.", string.Empty,
    };

    private static readonly string[] WinsockOk =
    {
        string.Empty, "Sucessfully reset the Winsock Catalog.", "You must restart the computer in order to complete the reset.", string.Empty,
    };

    private static string PsLines(params string[] lines) => "@(" + string.Join(", ", lines.Select(l => "'" + l.Replace("'", "''") + "'")) + ")";

    private const string TwoAdapters =
        "[pscustomobject]@{ InterfaceAlias = 'Ethernet'; IPv4Address = @([pscustomobject]@{ IPAddress = '192.168.1.20'; PrefixLength = 24 }); IPv4DefaultGateway = [pscustomobject]@{ NextHop = '192.168.1.1' }; DNSServer = @([pscustomobject]@{ AddressFamily = 2; ServerAddresses = @('1.1.1.1', '8.8.8.8') }); NetIPv4Interface = [pscustomobject]@{ Dhcp = 'Disabled' } }, " +
        "[pscustomobject]@{ InterfaceAlias = 'Wi-Fi'; IPv4Address = @([pscustomobject]@{ IPAddress = '10.0.0.7'; PrefixLength = 16 }); IPv4DefaultGateway = $null; DNSServer = @(); NetIPv4Interface = [pscustomobject]@{ Dhcp = 'Enabled' } }";

    private static string NetMocks(
        string winsockLines,
        int winsockCode,
        string ipResetLines,
        int ipResetCode,
        string? ipConfig = null,
        string? configBody = null,
        string? clearBody = null)
        => "function Get-NetIPConfiguration { Write-Host 'CALL get-ipconfig'; " + (configBody ?? "") + "; @(" + (ipConfig ?? TwoAdapters) + ") }\r\n" +
           "function netsh {\r\n" +
           "    $line = ($args -join ' ')\r\n" +
           "    Write-Host ('CALL netsh ' + $line)\r\n" +
           "    if ($line -eq 'winsock reset') { $global:LASTEXITCODE = " + winsockCode + "; " + winsockLines + " }\r\n" +
           "    elseif ($line -eq 'int ip reset') { $global:LASTEXITCODE = " + ipResetCode + "; " + ipResetLines + " }\r\n" +
           "    else { throw ('Nieoczekiwane wywołanie netsh: ' + $line) }\r\n" +
           "}\r\n" +
           "function Clear-DnsClientCache { Write-Host 'CALL clear-dns'; " + (clearBody ?? "") + " }\r\n";

    private static ScriptRun RunNet(string mocks) => PowerShellHarness.Run(Apply(NetResetId), mocks, string.Empty);

    private static string[] Calls(ScriptRun run) => run.Output.Where(l => l.StartsWith("CALL ", StringComparison.Ordinal)).ToArray();

    private const string FinalLine = "Polecenia zakończone powodzeniem. Skutek po restarcie komputera. Konfiguracja IP sprzed resetu jest wyżej i w pliku logu.";

    private const string Annotation = "Jedna pozycja chroniona przez system odmówiła dostępu – to zwykłe zachowanie Windows 11, reszta zresetowana.";

    [Fact]
    public void NetworkReset_RealMeasuredOutput_IsSuccessWithAnnotation_InOrder_AndExitsZero()
    {
        var run = RunNet(NetMocks(PsLines(WinsockOk), 0, PsLines(RealIpResetOutput), 1));

        Assert.Equal(0, run.ExitCode); // wrapper kończy exit $LASTEXITCODE: skrypt musi wyzerować kod 1 przyjęty z adnotacją
        Assert.Empty(run.Error);
        Assert.Equal(new[] { "CALL get-ipconfig", "CALL netsh winsock reset", "CALL netsh int ip reset", "CALL clear-dns" }, Calls(run));
        Assert.Contains(Annotation, run.Output);
        Assert.Equal(FinalLine, run.LastLine);
    }

    [Fact]
    public void NetworkReset_PrintsIpConfigurationOfEveryAdapterBeforeAnyChange()
    {
        var run = RunNet(NetMocks(PsLines(WinsockOk), 0, PsLines(RealIpResetOutput), 1));

        var first = run.Output.ToList().FindIndex(l => l.StartsWith("CALL netsh", StringComparison.Ordinal));
        var beforeChange = run.Output.Take(first).ToList();
        Assert.Contains(beforeChange, l => l.Contains("Ethernet", StringComparison.Ordinal) && l.Contains("192.168.1.20/24", StringComparison.Ordinal)
            && l.Contains("255.255.255.0", StringComparison.Ordinal) && l.Contains("192.168.1.1", StringComparison.Ordinal)
            && l.Contains("1.1.1.1", StringComparison.Ordinal) && l.Contains("8.8.8.8", StringComparison.Ordinal) && l.Contains("DHCP: nie", StringComparison.Ordinal));
        Assert.Contains(beforeChange, l => l.Contains("Wi-Fi", StringComparison.Ordinal) && l.Contains("10.0.0.7/16", StringComparison.Ordinal)
            && l.Contains("255.255.0.0", StringComparison.Ordinal) && l.Contains("brama: brak", StringComparison.Ordinal) && l.Contains("DNS: brak", StringComparison.Ordinal) && l.Contains("DHCP: tak", StringComparison.Ordinal));
    }

    [Fact]
    public void NetworkReset_IpResetExitZero_IsPlainSuccessWithoutAnnotation()
    {
        var run = RunNet(NetMocks(PsLines(WinsockOk), 0, PsLines("Resetting Global, OK!", "Restart the computer to complete this action."), 0));

        Assert.Equal(0, run.ExitCode);
        Assert.DoesNotContain(Annotation, run.Output);
        Assert.Equal(FinalLine, run.LastLine);
    }

    [Theory]
    [InlineData("Odmowa dostępu.")]
    [InlineData("Odmowa dost─Öpu.")]
    [InlineData("Odmowa dostĘpu.")]
    [InlineData("Access is denied.")]
    public void NetworkReset_AccessDeniedIsRecognizedInPolishEnglishAndGarbledEncodings(string denial)
    {
        var lines = new[] { "Resetting Global, OK!", "Resetting , failed.", denial, string.Empty, "Resetting , OK!", "Restart the computer to complete this action." };
        var run = RunNet(NetMocks(PsLines(WinsockOk), 0, PsLines(lines), 1));

        Assert.Equal(0, run.ExitCode);
        Assert.Contains(Annotation, run.Output);
    }

    [Fact]
    public void NetworkReset_WinsockNonZero_Throws_AndLaterCommandsDoNotStart()
    {
        var run = RunNet(NetMocks(PsLines("Błąd katalogu Winsock."), 1, PsLines(RealIpResetOutput), 1));

        Assert.NotEqual(0, run.ExitCode);
        Assert.Contains("netsh winsock reset", run.Error, StringComparison.Ordinal);
        Assert.Contains("kod 1", run.Error, StringComparison.Ordinal);
        Assert.Contains("Błąd katalogu Winsock.", run.Error, StringComparison.Ordinal);
        Assert.Equal(new[] { "CALL get-ipconfig", "CALL netsh winsock reset" }, Calls(run));
        Assert.DoesNotContain(FinalLine, run.Output);
    }

    public static readonly object[][] RejectedIpResets =
    {
        // Inny kod niż 0 i 1.
        new object[] { "kod 2", 2, new[] { "Resetting Global, OK!", "Resetting , failed.", "Odmowa dostępu." } },
        // Wzorzec odmowy, ale kod 5 (nie 1).
        new object[] { "kod 5", 5, new[] { "Resetting , failed.", "Odmowa dostępu." } },
        // Dwie pozycje failed.
        new object[] { "failed", 1, new[] { "Resetting , failed.", "Odmowa dostępu.", "Resetting , failed.", "Odmowa dostępu." } },
        // Jedno failed, ale inny komunikat zaraz po nim.
        new object[] { "failed", 1, new[] { "Resetting Global, OK!", "Resetting , failed.", "Nieoczekiwany błąd 87.", "Resetting , OK!" } },
        // Komunikat odmowy nie bezpośrednio po failed (pozycja OK pomiędzy).
        new object[] { "failed", 1, new[] { "Resetting , failed.", "Resetting , OK!", "Odmowa dostępu." } },
        // failed bez żadnej linii po nim.
        new object[] { "failed", 1, new[] { "Resetting Global, OK!", "Resetting , failed." } },
        // Pozycja Resetting nie kończąca się OK! (ani to failed).
        new object[] { "Resetting", 1, new[] { "Resetting Global, OK!", "Resetting Route, partially.", "Resetting , failed.", "Odmowa dostępu." } },
        // Kod 1 bez żadnego failed.
        new object[] { "kod 1", 1, new[] { "Resetting Global, OK!", "Restart the computer to complete this action." } },
        // Pusty wynik.
        new object[] { "kod 1", 1, Array.Empty<string>() },
    };

    [Theory]
    [MemberData(nameof(RejectedIpResets))]
    public void NetworkReset_IpResetOutsideTheMeasuredPattern_Throws_WithOutput_AndSkipsClear(string expectedFragment, int code, string[] lines)
    {
        var run = RunNet(NetMocks(PsLines(WinsockOk), 0, PsLines(lines), code));

        Assert.NotEqual(0, run.ExitCode);
        Assert.Contains("netsh int ip reset", run.Error, StringComparison.Ordinal);
        Assert.Contains(expectedFragment, run.Error, StringComparison.Ordinal);
        if (lines.Length > 0)
        {
            Assert.Contains(lines[0], run.Error, StringComparison.Ordinal);
        }

        Assert.DoesNotContain("CALL clear-dns", run.Output);
        Assert.DoesNotContain(FinalLine, run.Output);
        Assert.DoesNotContain(Annotation, run.Output);
        Assert.Contains(WinsockAlreadyReset, run.Error, StringComparison.Ordinal); // Winsock już zresetowany – technik musi to wiedzieć
    }

    private const string WinsockAlreadyReset = "Winsock został już zresetowany – uruchom ponownie komputer; konfiguracja IP sprzed resetu jest w pliku logu.";

    [Fact]
    public void NetworkReset_IpResetNetshThrows_SaysWinsockAlreadyReset()
    {
        var mocks = NetMocks(PsLines(WinsockOk), 0, "throw 'netsh nie odpowiada'", 1);

        var run = RunNet(mocks);

        Assert.NotEqual(0, run.ExitCode);
        Assert.Contains("netsh nie odpowiada", run.Error, StringComparison.Ordinal);
        Assert.Contains(WinsockAlreadyReset, run.Error, StringComparison.Ordinal);
        Assert.DoesNotContain("CALL clear-dns", run.Output);
    }

    [Fact]
    public void NetworkReset_WinsockFailure_DoesNotClaimWinsockWasReset()
    {
        var run = RunNet(NetMocks(PsLines("Błąd katalogu Winsock."), 1, PsLines(RealIpResetOutput), 1));

        Assert.DoesNotContain("Winsock został już zresetowany", run.Error, StringComparison.Ordinal);
    }

    [Fact]
    public void NetworkReset_IpConfigurationUnreadable_ChangesNothing()
    {
        var run = RunNet(NetMocks(PsLines(WinsockOk), 0, PsLines(RealIpResetOutput), 1, configBody: "throw 'brak modułu NetTCPIP'"));

        Assert.NotEqual(0, run.ExitCode);
        Assert.Contains("brak modułu NetTCPIP", run.Error, StringComparison.Ordinal);
        Assert.Contains("nic nie zmieniono", run.Error, StringComparison.Ordinal);
        Assert.Equal(new[] { "CALL get-ipconfig" }, Calls(run));
    }

    [Fact]
    public void NetworkReset_NoAdapters_StillResets_AndSaysSo()
    {
        var run = RunNet(NetMocks(PsLines(WinsockOk), 0, PsLines(RealIpResetOutput), 1, ipConfig: string.Empty));

        Assert.Equal(0, run.ExitCode);
        Assert.Contains(run.Output, l => l.Contains("brak adapterów", StringComparison.OrdinalIgnoreCase));
        Assert.Equal(FinalLine, run.LastLine);
    }

    [Fact]
    public void NetworkReset_ClearDnsFails_IsNotSuccess_AndSaysResetAlreadyDone()
    {
        var run = RunNet(NetMocks(PsLines(WinsockOk), 0, PsLines(RealIpResetOutput), 1, clearBody: "throw 'DNS Client zatrzymany'"));

        Assert.NotEqual(0, run.ExitCode);
        Assert.Contains("DNS Client zatrzymany", run.Error, StringComparison.Ordinal);
        Assert.Contains(WinsockAlreadyReset, run.Error, StringComparison.Ordinal);
        Assert.DoesNotContain(FinalLine, run.Output);
    }

    [Fact]
    public void NetworkReset_UsesOnlyTheTwoNetshCommands()
    {
        var apply = Apply(NetResetId);
        Assert.Contains("'winsock', 'reset'", apply, StringComparison.Ordinal);
        Assert.Contains("'int', 'ip', 'reset'", apply, StringComparison.Ordinal);
        Assert.DoesNotContain("'/release'", apply, StringComparison.Ordinal);
        Assert.DoesNotContain("'/renew'", apply, StringComparison.Ordinal);
    }

    // ---- repair.restore-point i repair.system-protection (Task 7) ----

    private const string R7Warning = "Wyłączenie ochrony systemu blokuje tworzenie nowych punktów przywracania i przywracanie systemu z istniejących. Istniejące punkty zostają na dysku (zajmują miejsce) i wrócą po ponownym włączeniu ochrony; usunąć je można w oknie »Ochrona systemu« (Konfiguruj → Usuń).";

    private const string VolumeGuid = "{7a1c2d3e-0000-4000-8000-0123456789ab}";

    [Fact]
    public void RestorePoint_IsSafeHandlerAction_InRepairCategory()
    {
        var def = Get(RestorePointId);
        Assert.Equal("repair", def.Category);
        Assert.Equal(TweakKind.Action, def.Kind);
        Assert.Equal(RiskLevel.Safe, def.Risk);
        Assert.Equal(TweakScope.Machine, def.Scope);
        Assert.Equal(ActionHandlerNames.RestorePoint, def.Handler);
        Assert.Null(def.Script);
        Assert.Null(def.Launch);
        Assert.False(def.RequiresReboot);
        Assert.False(def.Suggested);
        Assert.True(def.Timeout >= TimeSpan.FromSeconds(60));
        Assert.True(def.Summary.Length <= 160);
        Assert.True(def.Benefit!.Length <= 220);
    }

    [Fact]
    public void SystemProtection_IsReversibleSafeTweak_WithDetectApplyUndo()
    {
        var def = Get(SystemProtectionId);
        Assert.Equal("repair", def.Category);
        Assert.Equal(TweakKind.Tweak, def.Kind);
        Assert.Equal(RiskLevel.Safe, def.Risk);
        Assert.Equal(TweakScope.Machine, def.Scope);
        Assert.Null(def.Handler);
        Assert.False(def.Suggested);
        Assert.True(def.Reversible);
        Assert.False(def.RequiresReboot);
        var script = Assert.IsType<ScriptSet>(def.Script);
        Assert.NotNull(script.Detect);
        Assert.NotNull(script.Apply);
        Assert.NotNull(script.Undo);
        Assert.True(def.Timeout >= TimeSpan.FromSeconds(60));
        Assert.True(def.Summary.Length <= 160);
        Assert.True(def.Benefit!.Length <= 220);
    }

    [Fact]
    public void SystemProtection_Warnings_AreTheMeasuredR7Text_NotTheOldClaim()
    {
        // Spec opisów 2026-10-26: pełny tekst R7 w docs/tweaks, w programie skrót tej samej treści (≤ 140 znaków).
        var def = Get(SystemProtectionId);
        var text = string.Join(" ", def.Warnings);
        Assert.Contains(R7Warning, Doc(SystemProtectionId).Replace("\n", " "), StringComparison.Ordinal);
        Assert.Contains("Cofnięcie wyłącza ochronę", text, StringComparison.Ordinal);
        Assert.Contains("z istniejących nie przywrócisz systemu", text, StringComparison.Ordinal);
        Assert.Contains("zostają na dysku", text, StringComparison.Ordinal);
        Assert.DoesNotContain("usuwa wszystkie", text + " " + def.Description, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void SystemProtection_Scripts_UseOnlyEnableDisableComputerRestore_NoVssadminNoHkcu()
    {
        var def = Get(SystemProtectionId);
        foreach (var body in new[] { def.Script!.Detect!, Apply(SystemProtectionId), def.Script.Undo! })
        {
            Assert.DoesNotContain("vssadmin", body, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("HKCU", body, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("[Microsoft.Win32.Registry]::", body, StringComparison.Ordinal);
        }

        Assert.Contains("Enable-ComputerRestore -Drive 'C:\\'", Apply(SystemProtectionId), StringComparison.Ordinal);
        Assert.Contains("Disable-ComputerRestore -Drive 'C:\\'", def.Script!.Undo!, StringComparison.Ordinal);
    }

    private static string Detect(string id) => Get(id).Script!.Detect!;

    private static string Undo(string id) => Get(id).Script!.Undo!;

    private static string ProtectionMocks(string clients, string volumeId = @"'\\?\Volume" + VolumeGuid + @"\'", bool enableWorks = true, bool disableWorks = true, string? enableBody = null, string? disableBody = null, string? readBody = null)
        => "$script:on = " + (clients.Length > 0 ? "$true" : "$false") + "\r\n" +
           "function Get-DsoSystemVolumeId { " + volumeId + " }\r\n" +
           "function Get-DsoProtectionClients { Write-Host 'CALL read'; " + (readBody ?? "") + "; if ($script:on) { @(" + (clients.Length > 0 ? clients : "'" + VolumeGuid + ":(0)'") + ") } else { $null } }\r\n" +
           "function Enable-ComputerRestore { param([string]$Drive) Write-Host ('CALL enable ' + $Drive); " + (enableBody ?? "") + "; " + (enableWorks ? "$script:on = $true" : "") + " }\r\n" +
           "function Disable-ComputerRestore { param([string]$Drive) Write-Host ('CALL disable ' + $Drive); " + (disableBody ?? "") + "; " + (disableWorks ? "$script:on = $false" : "") + " }\r\n";

    private static string OwnClient => "'" + VolumeGuid + ":(0)'";

    [Fact]
    public void SystemProtection_Detect_NoEntry_IsNotApplied()
    {
        var run = PowerShellHarness.Run(Detect(SystemProtectionId), ProtectionMocks(string.Empty), string.Empty);

        Assert.Equal(0, run.ExitCode);
        Assert.Equal("NotApplied", run.LastLine);
    }

    [Fact]
    public void SystemProtection_Detect_EntryForSystemVolume_IsApplied_CaseInsensitive()
    {
        var run = PowerShellHarness.Run(Detect(SystemProtectionId), ProtectionMocks("'" + VolumeGuid.ToUpperInvariant() + ":(0)'"), string.Empty);

        Assert.Equal(0, run.ExitCode);
        Assert.Equal("Applied", run.LastLine);
    }

    [Fact]
    public void SystemProtection_Detect_EntryOnlyForOtherVolume_IsNotApplied()
    {
        var run = PowerShellHarness.Run(Detect(SystemProtectionId), ProtectionMocks("'{ffffffff-0000-4000-8000-000000000000}:(0)', 'inny wpis'"), string.Empty);

        Assert.Equal(0, run.ExitCode);
        Assert.Equal("NotApplied", run.LastLine);
    }

    [Fact]
    public void SystemProtection_Detect_VolumeIdWithoutGuid_IsUnknown_NotNotApplied()
    {
        var run = PowerShellHarness.Run(Detect(SystemProtectionId), ProtectionMocks(OwnClient, volumeId: "'brak guid'"), string.Empty);

        Assert.NotEqual(0, run.ExitCode);
        Assert.DoesNotContain("NotApplied", run.Output);
        Assert.DoesNotContain("Applied", run.Output);
    }

    [Fact]
    public void SystemProtection_Detect_ReadFails_IsUnknown()
    {
        var run = PowerShellHarness.Run(Detect(SystemProtectionId), ProtectionMocks(OwnClient, readBody: "throw 'brak dostępu do SPP'"), string.Empty);

        Assert.NotEqual(0, run.ExitCode);
    }

    [Fact]
    public void SystemProtection_Detect_WithoutMocks_HitsHarnessGuard()
    {
        var run = PowerShellHarness.Run(Detect(SystemProtectionId), string.Empty, string.Empty);

        Assert.NotEqual(0, run.ExitCode);
        Assert.Contains("Bezpiecznik harnessu", run.Error, StringComparison.Ordinal);
    }

    [Fact]
    public void SystemProtection_Apply_EnablesCThenReadsBack_AndReportsSuccess()
    {
        var run = PowerShellHarness.Run(Apply(SystemProtectionId), ProtectionMocks(string.Empty), string.Empty);

        Assert.Equal(0, run.ExitCode);
        Assert.Empty(run.Error);
        var calls = run.Output.Where(l => l.StartsWith("CALL", StringComparison.Ordinal)).ToArray();
        Assert.Contains("CALL enable C:\\", calls);
        Assert.Equal("CALL read", calls.Last());
        Assert.DoesNotContain(calls, c => c.Contains("disable", StringComparison.Ordinal));
        Assert.Equal("Włączono ochronę systemu na dysku C: (limit miejsca ustawia Windows, domyślnie 3% dysku).", run.LastLine);
    }

    [Fact]
    public void SystemProtection_Apply_ReadbackShowsNoEntry_IsFailureNotSuccess()
    {
        var run = PowerShellHarness.Run(Apply(SystemProtectionId), ProtectionMocks(string.Empty, enableWorks: false), string.Empty);

        Assert.NotEqual(0, run.ExitCode);
        Assert.Contains("Ochrona systemu nie została włączona", run.Error, StringComparison.Ordinal);
        Assert.DoesNotContain("Włączono ochronę systemu", string.Join("\n", run.Output), StringComparison.Ordinal);
    }

    [Fact]
    public void SystemProtection_Apply_ReadbackFails_IsFailureSayingEffectNotConfirmed()
    {
        // Odczyt zwrotny rzuca dopiero po Enable (flaga ustawiana przez mock włączenia).
        var run = PowerShellHarness.Run(Apply(SystemProtectionId), ProtectionMocks(string.Empty, readBody: "if ($script:on) { throw 'rejestr niedostępny' }"), string.Empty);

        Assert.NotEqual(0, run.ExitCode);
        Assert.Contains("skutku nie potwierdzono", run.Error, StringComparison.Ordinal);
        Assert.DoesNotContain("Włączono ochronę systemu", string.Join("\n", run.Output), StringComparison.Ordinal);
    }

    [Fact]
    public void SystemProtection_Apply_EnableThrows_IsFailureWithReason()
    {
        var run = PowerShellHarness.Run(Apply(SystemProtectionId), ProtectionMocks(string.Empty, enableBody: "throw 'Usługa VSS wyłączona'"), string.Empty);

        Assert.NotEqual(0, run.ExitCode);
        Assert.Contains("Włączenie ochrony systemu nie powiodło się: Usługa VSS wyłączona", run.Error, StringComparison.Ordinal);
    }

    [Fact]
    public void SystemProtection_Undo_DisablesCThenReadsBack_AndReportsSuccess()
    {
        var run = PowerShellHarness.Run(Undo(SystemProtectionId), ProtectionMocks(OwnClient), "NotApplied");

        Assert.Equal(0, run.ExitCode);
        Assert.Empty(run.Error);
        var calls = run.Output.Where(l => l.StartsWith("CALL", StringComparison.Ordinal)).ToArray();
        Assert.Contains("CALL disable C:\\", calls);
        Assert.Equal("CALL read", calls.Last());
        Assert.DoesNotContain(calls, c => c.Contains("enable", StringComparison.Ordinal));
        Assert.Equal("Wyłączono ochronę systemu na dysku C: (istniejące punkty przywracania zostają na dysku).", run.LastLine);
    }

    [Fact]
    public void SystemProtection_Undo_EntryStillThere_IsFailure()
    {
        var run = PowerShellHarness.Run(Undo(SystemProtectionId), ProtectionMocks(OwnClient, disableWorks: false), "NotApplied");

        Assert.NotEqual(0, run.ExitCode);
        Assert.Contains("Ochrona systemu nie została wyłączona", run.Error, StringComparison.Ordinal);
    }

    [Fact]
    public void SystemProtection_Undo_DisableThrows_IsFailureWithReason()
    {
        var run = PowerShellHarness.Run(Undo(SystemProtectionId), ProtectionMocks(OwnClient, disableBody: "throw 'odmowa'"), "NotApplied");

        Assert.NotEqual(0, run.ExitCode);
        Assert.Contains("Wyłączenie ochrony systemu nie powiodło się: odmowa", run.Error, StringComparison.Ordinal);
    }

    [Fact]
    public void SystemProtection_Undo_ReadbackFails_IsFailureSayingEffectNotConfirmed()
    {
        var run = PowerShellHarness.Run(Undo(SystemProtectionId), ProtectionMocks(OwnClient, readBody: "if (-not $script:on) { throw 'rejestr niedostępny' }"), "NotApplied");

        Assert.NotEqual(0, run.ExitCode);
        Assert.Contains("skutku nie potwierdzono", run.Error, StringComparison.Ordinal);
    }

    [Fact]
    public void SystemProtection_Capture_ReturnsStateBeforeChange()
    {
        var off = PowerShellHarness.Run(Get(SystemProtectionId).Script!.Capture!, ProtectionMocks(string.Empty), string.Empty);
        var on = PowerShellHarness.Run(Get(SystemProtectionId).Script!.Capture!, ProtectionMocks(OwnClient), string.Empty);

        Assert.Equal("NotApplied", off.LastLine);
        Assert.Equal("Applied", on.LastLine);
    }

    [Fact]
    public void SystemProtection_Undo_WasOnBeforeChange_StaysOn()
    {
        var run = PowerShellHarness.Run(Undo(SystemProtectionId), ProtectionMocks(OwnClient), "Applied");

        Assert.Equal(0, run.ExitCode);
        Assert.DoesNotContain(run.Output, line => line.Contains("CALL disable", StringComparison.Ordinal));
        Assert.Equal("Ochrona systemu była włączona przed zmianą – zostaje włączona.", run.LastLine);
    }

    [Theory]
    [InlineData("")]
    [InlineData("cokolwiek")]
    public void SystemProtection_Undo_NoKnownSnapshot_RefusesWithoutChange(string snapshot)
    {
        var run = PowerShellHarness.Run(Undo(SystemProtectionId), ProtectionMocks(OwnClient), snapshot);

        Assert.NotEqual(0, run.ExitCode);
        Assert.DoesNotContain(run.Output, line => line.Contains("CALL disable", StringComparison.Ordinal));
        Assert.Contains("nic nie zmieniono", run.Error, StringComparison.Ordinal);
    }

    [Fact]
    public void ProtectionHelpers_WithoutMocks_HitHarnessGuard()
    {
        foreach (var body in new[] { Apply(SystemProtectionId), Undo(SystemProtectionId) })
        {
            var run = PowerShellHarness.Run(body, string.Empty, "NotApplied");
            Assert.NotEqual(0, run.ExitCode);
            Assert.Contains("Bezpiecznik harnessu", run.Error, StringComparison.Ordinal);
        }
    }

    // ---- repair.winget (Task 8) ----

    private const string WingetOtherAccountText = "Naprawa winget działa tylko dla konta, na którym działa DSOptimizer: uruchom DSOptimizer na koncie TESTPC\\dso.";
    private const string StoreUrl = "ms-windows-store://pdp/?ProductId=9NBLGGH4NNS1";
    private const string UserSidText = "S-1-5-21-0-0-0-1000";

    [Fact]
    public void Winget_IsScriptActionInRepairCategory_WithSafeRisk()
    {
        var def = Get(WingetId);
        Assert.Equal("repair", def.Category);
        Assert.Equal(TweakKind.Action, def.Kind);
        Assert.Equal(RiskLevel.Safe, def.Risk);
        Assert.Equal(TweakScope.Machine, def.Scope);
        Assert.Null(def.Handler);
        Assert.Null(def.Launch);
        Assert.False(def.RequiresReboot);
        Assert.False(def.Suggested);
        Assert.Empty(def.Registry);
        Assert.Empty(def.Services);
        var script = Assert.IsType<ScriptSet>(def.Script);
        Assert.NotNull(script.Apply);
        Assert.Null(script.Detect);
        Assert.Null(script.Undo);
        Assert.True(def.Timeout >= TimeSpan.FromSeconds(60));
        Assert.True(def.Summary.Length <= 160);
        Assert.True(def.Benefit!.Length <= 220);
    }

    [Fact]
    public void Winget_Script_RegistersByFamilyName_NeverDownloads_NoHkcu()
    {
        var apply = Apply(WingetId);
        Assert.Contains("Add-AppxPackage -RegisterByFamilyName -MainPackage 'Microsoft.DesktopAppInstaller_8wekyb3d8bbwe'", apply, StringComparison.Ordinal);
        Assert.DoesNotContain("HKCU", apply, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("Invoke-WebRequest", apply, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("Invoke-RestMethod", apply, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("msixbundle", apply, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("Install-Module", apply, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("Add-AppxPackage -Path", apply, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("source", apply, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Winget_Script_StoreAddress_IsTheOneAllowedByExternalLinkPolicy()
    {
        Assert.Contains(StoreUrl, Apply(WingetId), StringComparison.Ordinal);
        Assert.Equal(StoreUrl, DSOptimizer.Core.Gui.ExternalLinkPolicy.AppInstallerStoreUrl);
        Assert.True(DSOptimizer.Core.Gui.ExternalLinkPolicy.IsAllowed(StoreUrl));
    }

    [Fact]
    public void Winget_Warnings_MatchMeasurement()
    {
        // Spec opisów 2026-10-26: uwagi o wersji i sieci przeniesione do description.
        var def = Get(WingetId);
        var text = def.Description + " " + string.Join(" ", def.Warnings);
        Assert.Contains("nie pobiera", text, StringComparison.Ordinal);
        Assert.Contains("wersji", text, StringComparison.Ordinal);
        Assert.Contains("sieci", text, StringComparison.Ordinal);
        Assert.Contains("konta", text, StringComparison.Ordinal);
    }

    private static string WingetPackage(string sid, string state = "Installed", string name = "Microsoft.DesktopAppInstaller")
        => "[pscustomobject]@{ Name = '" + name + "'; PackageFullName = '" + name + "_1.26.509.0_x64__8wekyb3d8bbwe'; Version = '1.26.509.0'; InstallLocation = 'C:\\x'; Status = 'Ok'; Users = @([pscustomobject]@{ Sid = '" + sid + "'; InstallState = '" + state + "' }) }";

    /// <summary>Mocki naprawy winget. Przed rejestracją: pakiet <paramref name="pkgBefore"/> i wersja (kod, wyjście); po rejestracji: <paramref name="pkgAfter"/> i <paramref name="afterCode"/>/<paramref name="afterOutput"/>.</summary>
    private static string WingetMocks(
        string processSid = UserSidText,
        string? pkgBefore = null,
        string? pkgAfter = null,
        int beforeCode = -1,
        string beforeOutput = "brak aliasu winget.exe",
        int afterCode = 0,
        string afterOutput = "v1.11.510",
        string? registerBody = null,
        string? readBody = null)
    {
        pkgBefore ??= WingetPackage(UserSidText);
        pkgAfter ??= WingetPackage(UserSidText);
        return "$script:registered = $false\r\n" +
               "function Get-DsoProcessSid { '" + processSid + "' }\r\n" +
               "function Get-DsoAccountName { param([string]$Sid) 'TESTPC\\dso' }\r\n" +
               "function Get-DsoAppInstallerPackage { Write-Host 'CALL read-package'; " + (readBody ?? "") + "; if ($script:registered) { @(" + pkgAfter + ") } else { @(" + pkgBefore + ") } }\r\n" +
               "function Invoke-DsoWingetVersion { Write-Host 'CALL winget-version'; if ($script:registered) { [pscustomobject]@{ ExitCode = " + afterCode + "; Output = '" + afterOutput + "' } } else { [pscustomobject]@{ ExitCode = " + beforeCode + "; Output = '" + beforeOutput + "' } } }\r\n" +
               "function Register-DsoAppInstaller { Write-Host 'CALL register'; " + (registerBody ?? "") + "; $script:registered = $true }\r\n";
    }

    private static ScriptRun RunWinget(string mocks) => PowerShellHarness.Run(Apply(WingetId), mocks, string.Empty);

    [Fact]
    public void Winget_AlreadyWorking_ReportsVersion_AndRegistersNothing()
    {
        var run = RunWinget(WingetMocks(beforeCode: 0, beforeOutput: "v1.11.510"));

        Assert.Equal(0, run.ExitCode);
        Assert.Empty(run.Error);
        Assert.Equal("winget działa (wersja v1.11.510), nic nie zmieniono.", run.LastLine);
        Assert.DoesNotContain("CALL register", run.Output);
    }

    [Fact]
    public void Winget_AliasMissing_RegistersThenConfirmsWithVersionReadback()
    {
        var run = RunWinget(WingetMocks());

        Assert.Equal(0, run.ExitCode);
        Assert.Empty(run.Error);
        var calls = Calls(run);
        Assert.Equal(1, calls.Count(c => c == "CALL register"));
        Assert.Equal("CALL winget-version", calls.Last());
        Assert.True(Array.IndexOf(calls, "CALL register") < Array.LastIndexOf(calls, "CALL winget-version"));
        Assert.Contains("Zarejestrowano ponownie", run.LastLine, StringComparison.Ordinal);
        Assert.Contains("v1.11.510", run.LastLine, StringComparison.Ordinal);
        Assert.DoesNotContain("nic nie zmieniono", run.LastLine, StringComparison.Ordinal);
    }

    [Fact]
    public void Winget_RegistrationDoesNotHelp_IsFailureWithStateAndStoreAddress()
    {
        var run = RunWinget(WingetMocks(afterCode: -1, afterOutput: "brak aliasu winget.exe"));

        Assert.NotEqual(0, run.ExitCode);
        Assert.Contains("Rejestracja nie pomogła:", run.Error, StringComparison.Ordinal);
        Assert.Contains("brak aliasu winget.exe", run.Error, StringComparison.Ordinal);
        Assert.Contains("Zaktualizuj „Instalator aplikacji” w Microsoft Store: " + StoreUrl + ".", run.Error, StringComparison.Ordinal);
        Assert.DoesNotContain("Zarejestrowano ponownie", string.Join("\n", run.Output), StringComparison.Ordinal);
        Assert.DoesNotContain(run.Output, l => l.StartsWith("winget działa", StringComparison.Ordinal));
    }

    [Fact]
    public void Winget_PackageMissingBeforeAndAfter_IsFailureNamingThePackage()
    {
        var run = RunWinget(WingetMocks(pkgBefore: WingetPackage("S-1-5-21-0-0-0-2000"), pkgAfter: WingetPackage("S-1-5-21-0-0-0-2000")));

        Assert.NotEqual(0, run.ExitCode);
        Assert.Contains("Rejestracja nie pomogła:", run.Error, StringComparison.Ordinal);
        Assert.Contains("Microsoft.DesktopAppInstaller", run.Error, StringComparison.Ordinal);
        Assert.Contains(StoreUrl, run.Error, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("Staged")]
    [InlineData("NotInstalled")]
    public void Winget_PackageNotInstalledForAccount_EvenWhenVersionWorks_StillRegistersAndVerifies(string state)
    {
        // Wersja działa, ale pakiet nie jest Installed dla konta: kryterium wymaga obu, więc rejestracja i ponowny odczyt.
        var run = RunWinget(WingetMocks(beforeCode: 0, beforeOutput: "v1.11.510", pkgBefore: WingetPackage(UserSidText, state)));

        Assert.Equal(0, run.ExitCode);
        Assert.Contains("CALL register", run.Output);
        Assert.Contains("Zarejestrowano ponownie", run.LastLine, StringComparison.Ordinal);
    }

    [Fact]
    public void Winget_OtherPackageNameOnly_IsNotCounted()
    {
        var other = WingetPackage(UserSidText, "Installed", "Microsoft.DesktopAppInstallerX");
        var run = RunWinget(WingetMocks(pkgBefore: other, pkgAfter: other));

        Assert.NotEqual(0, run.ExitCode);
        Assert.Contains("Rejestracja nie pomogła:", run.Error, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(0, "coś bez numeru wersji")]
    [InlineData(0, "")]
    [InlineData(1, "v1.11.510")]
    public void Winget_ExitCodeOrVersionOutputNotOk_IsNotWorking(int code, string output)
    {
        var run = RunWinget(WingetMocks(beforeCode: code, beforeOutput: output));

        Assert.Equal(0, run.ExitCode);
        Assert.Contains("CALL register", run.Output);
        Assert.DoesNotContain("nic nie zmieniono", run.LastLine, StringComparison.Ordinal);
        Assert.StartsWith("Zarejestrowano ponownie", run.LastLine, StringComparison.Ordinal);
    }

    [Fact]
    public void Winget_OtherAccount_Refuses_WithAccountName_AndTouchesNothing()
    {
        var run = RunWinget(WingetMocks(processSid: "S-1-5-21-0-0-0-500"));

        Assert.NotEqual(0, run.ExitCode);
        Assert.Contains(WingetOtherAccountText, run.Error, StringComparison.Ordinal);
        Assert.Empty(Calls(run));
    }

    [Fact]
    public void Winget_PackageReadFails_ChangesNothing()
    {
        var run = RunWinget(WingetMocks(readBody: "throw 'Appx niedostępny'"));

        Assert.NotEqual(0, run.ExitCode);
        Assert.Contains("Appx niedostępny", run.Error, StringComparison.Ordinal);
        Assert.Contains("nic nie zmieniono", run.Error, StringComparison.Ordinal);
        Assert.DoesNotContain("CALL register", run.Output);
    }

    [Fact]
    public void Winget_RegisterThrows_IsFailureWithReasonAndStoreAddress()
    {
        var run = RunWinget(WingetMocks(registerBody: "throw 'Odmowa 0x80073CFA'"));

        Assert.NotEqual(0, run.ExitCode);
        Assert.Contains("Rejestracja nie pomogła:", run.Error, StringComparison.Ordinal);
        Assert.Contains("Odmowa 0x80073CFA", run.Error, StringComparison.Ordinal);
        Assert.Contains(StoreUrl, run.Error, StringComparison.Ordinal);
    }

    [Fact]
    public void Winget_ReadbackThrowsAfterRegister_IsFailureNotSuccess()
    {
        var run = RunWinget(WingetMocks(readBody: "if ($script:registered) { throw 'odczyt padł' }"));

        Assert.NotEqual(0, run.ExitCode);
        Assert.Contains("Rejestracja nie pomogła:", run.Error, StringComparison.Ordinal);
        Assert.Contains("odczyt padł", run.Error, StringComparison.Ordinal);
        Assert.DoesNotContain("Zarejestrowano ponownie", string.Join("\n", run.Output), StringComparison.Ordinal);
    }

    [Fact]
    public void Winget_UnmockedPackageRead_HitsHarnessGuard_AndChangesNothing()
    {
        var run = RunWinget("function Get-DsoProcessSid { '" + UserSidText + "' }\r\n");

        Assert.NotEqual(0, run.ExitCode);
        Assert.Contains("Bezpiecznik harnessu: Get-DsoAppInstallerPackage", run.Error, StringComparison.Ordinal);
    }

    [Fact]
    public void Winget_VersionWritesToStderr_IsStillWorking_ProductionFunctionRunsRealProcess()
    {
        // Prawdziwa funkcja Invoke-DsoWingetVersion (bezpiecznik zdjęty) uruchamia własny exe z %LOCALAPPDATA% podmienionego na katalog testu:
        // kod 0, wersja na stdout i linia na stderr. W PS 5.1 przy $ErrorActionPreference='Stop' samo 2>&1 zamieniłoby stderr w wyjątek.
        var directory = Path.Combine(Path.GetTempPath(), "dso-tests-winget-" + Guid.NewGuid().ToString("N"));
        var aliasDirectory = Path.Combine(directory, "Microsoft", "WindowsApps");
        Directory.CreateDirectory(aliasDirectory);
        try
        {
            CompileFakeWinget(Path.Combine(aliasDirectory, "winget.exe"));
            var mocks = WingetMocks().Replace("function Invoke-DsoWingetVersion", "function Invoke-DsoWingetVersionMock") +
                        "Remove-Item -Path function:Invoke-DsoWingetVersion\r\n" +
                        "$env:LOCALAPPDATA = '" + directory.Replace("'", "''") + "'\r\n";

            var run = RunWinget(mocks);

            Assert.Equal(0, run.ExitCode);
            Assert.Empty(run.Error);
            Assert.Equal("winget działa (wersja v1.12.460), nic nie zmieniono.", run.LastLine);
            Assert.DoesNotContain("CALL register", run.Output);
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    private static void CompileFakeWinget(string exePath)
    {
        using var provider = new Microsoft.CSharp.CSharpCodeProvider();
        var parameters = new System.CodeDom.Compiler.CompilerParameters { GenerateExecutable = true, GenerateInMemory = false, OutputAssembly = exePath };
        const string Source = "public static class FakeWinget { public static int Main() { System.Console.Out.WriteLine(\"v1.12.460\"); System.Console.Error.WriteLine(\"Ostrzezenie testowe na stderr\"); return 0; } }";
        var result = provider.CompileAssemblyFromSource(parameters, Source);
        Assert.False(result.Errors.HasErrors, string.Join(" | ", result.Errors.Cast<System.CodeDom.Compiler.CompilerError>().Select(e => e.ErrorText)));
    }

    [Theory]
    [InlineData("Register-DsoAppInstaller")]
    [InlineData("Invoke-DsoWingetVersion")]
    public void Winget_UnmockedHelper_HitsHarnessGuard(string helper)
    {
        // Mock pod inną nazwą: skrypt nie pomija własnej definicji, ale bezpiecznik harnessu istnieje i rzuca.
        var mocks = WingetMocks().Replace("function " + helper, "function " + helper + "Mock");
        var run = RunWinget(mocks);

        Assert.NotEqual(0, run.ExitCode);
        Assert.Contains("Bezpiecznik harnessu: " + helper, run.Error, StringComparison.Ordinal);
    }

    // ---- repair.system-files (partia 2, Task 6) ----

    private const string SystemFilesId = "repair.system-files";

    [Fact]
    public void SystemFiles_IsSafeLongHandlerAction_WithMeasuredTimeout()
    {
        var def = Get(SystemFilesId);
        Assert.Equal("repair", def.Category);
        Assert.Equal(TweakKind.Action, def.Kind);
        Assert.Equal(RiskLevel.Safe, def.Risk);
        Assert.Equal(TweakScope.Machine, def.Scope);
        Assert.Equal(ActionHandlerNames.SystemFiles, def.Handler);
        Assert.Contains(def.Handler, ActionHandlerNames.LongRunning);
        Assert.Null(def.Script);
        Assert.Null(def.Launch);
        Assert.False(def.RequiresReboot);
        Assert.False(def.Suggested);
        Assert.Empty(def.Registry);
        Assert.Empty(def.Services);
        Assert.Equal(TimeSpan.FromSeconds(2700), def.Timeout);
        Assert.True(def.Summary.Length <= 160);
        Assert.True(def.Benefit!.Length <= 220);
        Assert.DoesNotContain("przyspiesz", def.Benefit, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("szybsz", def.Benefit, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void SystemFiles_Warnings_MatchMeasurement()
    {
        // Spec opisów 2026-10-26: czas i „Zatrzymaj” zmieniają decyzję (uwagi); limit 45 min i praca w tle – description.
        var def = Get(SystemFilesId);
        var text = string.Join(" ", def.Warnings);
        Assert.Contains("zwykle 2–3 min", text, StringComparison.Ordinal);
        Assert.Contains("ok. 10 min", text, StringComparison.Ordinal);
        Assert.Contains("Zatrzymaj", text, StringComparison.Ordinal);
        Assert.Contains("internet", text, StringComparison.Ordinal);
        Assert.Contains("45 min", def.Description, StringComparison.Ordinal);
        Assert.Contains("dalej w tle", def.Description, StringComparison.Ordinal);
    }

    [Fact]
    public void SystemFiles_Description_NamesMechanismAndLogs()
    {
        var text = Get(SystemFilesId).Description;
        Assert.Contains("/Online /Cleanup-Image /RestoreHealth", text, StringComparison.Ordinal);
        Assert.Contains("sfc /scannow", text, StringComparison.Ordinal);
        Assert.Contains("[SR]", text, StringComparison.Ordinal);
        Assert.Contains("CBS.log", text, StringComparison.Ordinal);
        Assert.Contains("0x800F081F", text, StringComparison.Ordinal);
        Assert.Contains("RebootPending", text, StringComparison.Ordinal);
    }

    [Fact]
    public void SystemFiles_Docs_MarkUnmeasuredVerdicts()
    {
        var doc = Doc(SystemFilesId);
        Assert.Contains("## Obserwacja skutku", doc, StringComparison.Ordinal);
        Assert.Contains("**Home**", doc, StringComparison.Ordinal);
        Assert.Contains("**Pro**", doc, StringComparison.Ordinal);
        Assert.Contains("2026-10-22", doc, StringComparison.Ordinal);
        Assert.Contains("niezmierzon", doc, StringComparison.Ordinal);
    }

    // ---- repair.disk-cleanup (partia 2, Task 7) ----

    private const string DiskCleanupId = "repair.disk-cleanup";

    [Fact]
    public void DiskCleanup_IsSafeLongHandlerAction_WithMeasuredTimeout()
    {
        var def = Get(DiskCleanupId);
        Assert.Equal("repair", def.Category);
        Assert.Equal(TweakKind.Action, def.Kind);
        Assert.Equal(RiskLevel.Safe, def.Risk);
        Assert.Equal(TweakScope.Machine, def.Scope);
        Assert.Equal(ActionHandlerNames.DiskCleanup, def.Handler);
        Assert.Contains(def.Handler, ActionHandlerNames.LongRunning);
        Assert.Null(def.Script);
        Assert.Null(def.Launch);
        Assert.False(def.RequiresReboot);
        Assert.False(def.Suggested);
        Assert.Empty(def.Registry);
        Assert.Empty(def.Services);
        Assert.Equal(TimeSpan.FromSeconds(1200), def.Timeout);
        Assert.True(def.Summary.Length <= 160);
        Assert.True(def.Benefit!.Length <= 220);
        Assert.DoesNotContain("przyspiesz", def.Benefit, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("szybsz", def.Benefit, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void DiskCleanup_Warnings_MatchMeasurementAndScope()
    {
        // Spec opisów 2026-10-26: w uwagach czas, „Zatrzymaj” i zakres; limity, praca w tle i /ResetBase – description.
        var def = Get(DiskCleanupId);
        var text = string.Join(" ", def.Warnings);
        Assert.Contains("zwykle poniżej 1 min", text, StringComparison.Ordinal);
        Assert.Contains("kilkadziesiąt minut", text, StringComparison.Ordinal); // zgodnie z opisem: w najgorszym razie ok. 40 min
        Assert.Contains("aktualizacja pobierana w tej chwili może pobrać się od nowa", def.Description, StringComparison.Ordinal);
        Assert.Contains("Zatrzymaj", text, StringComparison.Ordinal);
        Assert.Contains("24 h", text, StringComparison.Ordinal);
        Assert.Contains("Kosz", text, StringComparison.Ordinal);
        Assert.Contains("Pobrane", text, StringComparison.Ordinal);
        Assert.Contains("Windows.old", text, StringComparison.Ordinal);
        var details = def.Description;
        Assert.Contains("20 min", details, StringComparison.Ordinal);
        // Osobny limit 1200 s skryptu Optymalizacji dostarczania + 1200 s DISM = najgorzej ok. 40 min czekania.
        Assert.Contains("Optymalizacji dostarczania ma osobny limit 20 min", details, StringComparison.Ordinal);
        Assert.Contains("ok. 40 min", details, StringComparison.Ordinal);
        Assert.Contains("w tle", details, StringComparison.Ordinal);
        Assert.Contains("/ResetBase", details, StringComparison.Ordinal);
        Assert.Contains("odinstalow", details, StringComparison.Ordinal);
    }

    [Fact]
    public void DiskCleanup_Description_NamesMechanismsWithoutResetBaseOrCleanmgr()
    {
        var text = Get(DiskCleanupId).Description;
        Assert.Contains("/Online /Cleanup-Image /StartComponentCleanup", text, StringComparison.Ordinal);
        Assert.Contains("Delete-DeliveryOptimizationCache -Force", text, StringComparison.Ordinal);
        Assert.Contains(@"SoftwareDistribution\Download", text, StringComparison.Ordinal);
        Assert.Contains("thumbcache_*.db", text, StringComparison.Ordinal);
        Assert.Contains("ProfileImagePath", text, StringComparison.Ordinal);
        Assert.Contains("bez /ResetBase", text, StringComparison.Ordinal);
        Assert.Contains("cleanmgr", text, StringComparison.Ordinal);
    }

    [Fact]
    public void DiskCleanup_Docs_ObservationAndVerdicts()
    {
        var doc = Doc(DiskCleanupId);
        Assert.Contains("## Obserwacja skutku", doc, StringComparison.Ordinal);
        Assert.Contains("do potwierdzenia na sprzęcie", doc, StringComparison.Ordinal);
        Assert.Contains("Reclaimable Packages", doc, StringComparison.Ordinal);
        Assert.Contains("repair-disk-cleanup", doc, StringComparison.Ordinal);
    }

    // ---- repair.windows-old (partia 2, Task 8) ----

    private const string WindowsOldId = "repair.windows-old";

    [Fact]
    public void WindowsOld_IsCautionLongHandlerAction_WithMeasuredTimeout()
    {
        var def = Get(WindowsOldId);
        Assert.Equal("repair", def.Category);
        Assert.Equal(TweakKind.Action, def.Kind);
        Assert.Equal(RiskLevel.Caution, def.Risk);
        Assert.Equal(TweakScope.Machine, def.Scope);
        Assert.Equal(ActionHandlerNames.WindowsOld, def.Handler);
        Assert.Contains(def.Handler, ActionHandlerNames.LongRunning);
        Assert.Null(def.Script);
        Assert.Null(def.Launch);
        Assert.False(def.RequiresReboot);
        Assert.False(def.Suggested);
        Assert.Empty(def.Registry);
        Assert.Empty(def.Services);
        Assert.Equal(TimeSpan.FromSeconds(2700), def.Timeout);
        Assert.True(def.Summary.Length <= 160);
        Assert.True(def.Benefit!.Length <= 220);
        Assert.DoesNotContain("przyspiesz", def.Benefit, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("szybsz", def.Benefit, StringComparison.OrdinalIgnoreCase);
        // Pomiar VM: folder testowy 200 MB zniknął, wolne miejsce +158 MB (Home) i +193 MB (Pro) – nie „całe miejsce”.
        // Spec opisów 2026-10-26: wynik pomiaru tylko w docs/tweaks, w programie bez liczb z VM.
        Assert.DoesNotContain("całe miejsce", def.Benefit, StringComparison.Ordinal);
        Assert.DoesNotContain("MB", def.Benefit, StringComparison.Ordinal);
        var doc = Doc(WindowsOldId);
        Assert.Contains("przygotowany folder 200 MB", doc, StringComparison.Ordinal);
        Assert.Contains("Zwolniono: 158 MB.", doc, StringComparison.Ordinal);
        Assert.Contains("193 MB.", doc, StringComparison.Ordinal);
    }

    [Fact]
    public void WindowsOld_Warnings_NoWayBack_MeasuredTime_Background()
    {
        var def = Get(WindowsOldId);
        var text = string.Join(" ", def.Warnings);
        Assert.Contains("Po usunięciu nie da się wrócić do poprzedniej wersji Windows", text, StringComparison.Ordinal);
        // Oba pomiary: przebieg handlera na VM (Task 8: cleanmgr kończy się po 7 s) i pomiar Tasku 2 (ok. 11 min).
        // Spec opisów 2026-10-26: pomiary tylko w docs/tweaks, limit i praca w tle w description.
        var doc = Doc(WindowsOldId);
        Assert.Contains("po 7 s", doc, StringComparison.Ordinal);
        Assert.Contains("ok. 11 min", doc, StringComparison.Ordinal);
        Assert.Contains("niezmierzone", doc, StringComparison.Ordinal);
        Assert.Contains("45 min", def.Description, StringComparison.Ordinal);
        Assert.Contains("w tle", def.Description, StringComparison.Ordinal);
        // Ruling R30: po potwierdzeniu bez „Zatrzymaj” (nie ma już ścieżki „zatrzymaj przed cleanmgr”).
        Assert.Contains("Po potwierdzeniu nie da się przerwać", text, StringComparison.Ordinal);
        Assert.DoesNotContain("działa tylko przed", text + " " + def.Description, StringComparison.Ordinal);
    }

    [Fact]
    public void WindowsOld_Description_NamesCleanmgrMechanismAndStateFlags()
    {
        var text = Get(WindowsOldId).Description;
        Assert.Contains("cleanmgr.exe /sagerun:7351", text, StringComparison.Ordinal);
        Assert.Contains("StateFlags7351", text, StringComparison.Ordinal);
        Assert.Contains(@"VolumeCaches\Previous Installations", text, StringComparison.Ordinal);
        Assert.Contains("Windows.old", text, StringComparison.Ordinal);
        Assert.DoesNotContain("takeown", text, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("icacls", text, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void WindowsOld_Docs_ObservationAndHardwareVerdict()
    {
        var doc = Doc(WindowsOldId);
        Assert.Contains("## Obserwacja skutku", doc, StringComparison.Ordinal);
        Assert.Contains("do potwierdzenia na sprzęcie", doc, StringComparison.Ordinal);
        Assert.Contains("TrustedInstaller", doc, StringComparison.Ordinal);
        Assert.Contains("repair-windows-old", doc, StringComparison.Ordinal);
        Assert.Contains("repair-windows-old-absent", doc, StringComparison.Ordinal);
        Assert.Contains("niezmierzon", doc, StringComparison.Ordinal);
    }

    // ---- komunikaty skryptów po angielsku (Get-DsoText, tabela catalog-repair.json) ----

    [Fact]
    public void DnsFlush_English_Message()
    {
        ScriptRun run;
        using (L.Use(UiLanguage.English))
        {
            run = RunDns(DnsMocks(Join(DnsEntry("www.microsoft.com")), string.Empty));
        }

        Assert.Equal(0, run.ExitCode);
        Assert.Equal("Cleared the DNS cache (entries before: 1, after: 0).", run.LastLine);
    }

    [Fact]
    public void NetworkReset_English_Message()
    {
        ScriptRun run;
        using (L.Use(UiLanguage.English))
        {
            run = RunNet(NetMocks(PsLines(WinsockOk), 0, PsLines(RealIpResetOutput), 1));
        }

        Assert.Equal(0, run.ExitCode);
        Assert.Contains("IP configuration before the reset:", run.Output);
        Assert.Contains(run.Output, l => l.Contains("Ethernet: IPv4: 192.168.1.20/24 (mask 255.255.255.0), gateway: 192.168.1.1, DNS: 1.1.1.1, 8.8.8.8, DHCP: no", StringComparison.Ordinal));
        Assert.Contains(run.Output, l => l.Contains("Wi-Fi: IPv4: 10.0.0.7/16 (mask 255.255.0.0), gateway: none, DNS: none, DHCP: yes", StringComparison.Ordinal));
        Assert.Contains("One entry protected by the system denied access – this is normal on Windows 11, the rest was reset.", run.Output);
        Assert.Equal("The commands succeeded. Takes effect after the computer restarts. The IP configuration from before the reset is above and in the log file.", run.LastLine);
    }

    [Fact]
    public void SystemProtection_English_Message()
    {
        ScriptRun run;
        using (L.Use(UiLanguage.English))
        {
            run = PowerShellHarness.Run(Apply(SystemProtectionId), ProtectionMocks(string.Empty), string.Empty);
        }

        Assert.Equal(0, run.ExitCode);
        Assert.Equal("Turned on System Protection for the C: drive (Windows sets the space limit, 3% of the disk by default).", run.LastLine);
    }

    [Fact]
    public void Winget_English_Message()
    {
        ScriptRun run;
        using (L.Use(UiLanguage.English))
        {
            run = RunWinget(WingetMocks(processSid: "S-1-5-21-0-0-0-500"));
        }

        Assert.NotEqual(0, run.ExitCode);
        Assert.Contains("The winget repair works only for the account DSOptimizer runs on: run DSOptimizer on account TESTPC\\dso.", run.Error, StringComparison.Ordinal);
        Assert.DoesNotContain("CALL register", run.Output);
    }
}

/// <summary>
/// Kategoria repair, pozycja repair.store (dawniej tools.store-repair; partia C5 Task 7, ruling R19, decyzja D4; Etap 4b Task 9, R33): ponowna rejestracja pakietu Microsoft Store
/// z jego manifestu dla konta, które uruchomiło DSOptimizer. Skrypt idzie przez harness wyłącznie z mockami helperów
/// (Get-DsoProcessSid, Get-DsoStorePackage, Register-DsoStorePackage). Bezpieczniki harnessu (Get-/Add-/Remove-AppxPackage, wsreset)
/// rzucają przy każdym niezamockowanym wywołaniu. Żadna ścieżka nie może dać sukcesu bez odczytu zwrotnego pakietu.
/// </summary>
public class RepairStoreCatalogTests
{
    private const string Id = "repair.store";
    private const string UserSid = "S-1-5-21-0-0-0-1000"; // SID, który harness przekazuje skryptowi jako $UserSid
    private const string OtherSid = "S-1-5-21-0-0-0-500";
    private const string StoreName = "Microsoft.WindowsStore";
    private const string StoreFull = "Microsoft.WindowsStore_22506.1400.2.0_x64__8wekyb3d8bbwe";

    private static TweakDefinition Get()
        => CatalogLoader.Load(BuiltInCatalog.Sources).Definitions.Single(d => d.Id == Id);

    private static string Apply() => Assert.IsType<string>(Get().Script!.Apply);

    private static string AllText(TweakDefinition def) => def.Description + " " + string.Join(" ", def.Warnings);

    // ---- katalog ----

    [Fact]
    public void Entry_IsCautionActionInRepairCategory_WithLegacyAliases_AndNoUndo()
    {
        var def = Get();
        Assert.Equal("repair", def.Category);
        Assert.Equal(TweakKind.Action, def.Kind);
        Assert.Equal(RiskLevel.Caution, def.Risk);
        Assert.Equal(TweakScope.Machine, def.Scope);
        Assert.False(def.RequiresReboot);
        Assert.Equal(new[] { "tool_msstore", "tools.store-repair" }, def.Aliases);
        Assert.Empty(def.Registry);
        Assert.Empty(def.Services);
        Assert.Null(def.Launch);
        var script = Assert.IsType<ScriptSet>(def.Script);
        Assert.NotNull(script.Apply);
        Assert.Null(script.Detect);
        Assert.Null(script.Undo);
        Assert.Null(script.Capture);
        var owners = CatalogLoader.Load(BuiltInCatalog.Sources).Definitions.Where(d => d.Aliases.Contains("tool_msstore")).Select(d => d.Id);
        Assert.Equal(new[] { Id }, owners);
        var all = CatalogLoader.Load(BuiltInCatalog.Sources).Definitions;
        Assert.DoesNotContain(all, d => d.Id == "tools.store-repair");
        Assert.Equal(new[] { Id }, all.Where(d => d.Aliases.Contains("tools.store-repair")).Select(d => d.Id));
    }

    [Fact]
    public void Warnings_CoverNoUndoNoInstallLtscNetworkAndOwnAccountOnly()
    {
        var def = Get();
        // Spec opisów 2026-10-26: jednolite „Bez cofania – …” w uwagach wszystkich napraw.
        Assert.Contains(def.Warnings, w => w.StartsWith("Bez cofania", StringComparison.Ordinal));
        Assert.Contains(def.Warnings, w => w.Contains("nie instaluje", StringComparison.OrdinalIgnoreCase) && w.Contains("Microsoft Store", StringComparison.Ordinal));
        // Spec opisów 2026-10-26: cytat Microsoftu o LTSC tylko w docs/tweaks (pilnuje go Docs_ExistWithMeasuredFindingsAndSources),
        // uwagi o sieci i koncie w description.
        Assert.Contains(def.Warnings, w => w.Contains("LTSC", StringComparison.Ordinal));
        Assert.DoesNotContain("might be limited", AllText(def), StringComparison.Ordinal);
        Assert.Contains("sieci", def.Description, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("uruchomiło DSOptimizer", def.Description, StringComparison.Ordinal);
        Assert.DoesNotContain("Przywraca", AllText(def), StringComparison.Ordinal); // obietnica v3.2 „Przywraca Microsoft Store na LTSC”
        Assert.DoesNotContain("wsreset", AllText(def).Replace("wsreset -i", string.Empty), StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Script_UsesOnlyHelpers_ExactPackageName_NoWsresetNoRemoveNoWildcard()
    {
        var apply = Apply();
        Assert.DoesNotContain("HKCU", apply, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("wsreset", apply, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("Remove-AppxPackage", apply, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("*Store*", apply, StringComparison.Ordinal);
        Assert.DoesNotContain("*WindowsStore*", apply, StringComparison.Ordinal);
        Assert.DoesNotContain("Start-Process", apply, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("-EncodedCommand", apply, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("'" + StoreName + "'", apply, StringComparison.Ordinal);
        Assert.Equal(1, apply.Split(new[] { "Add-AppxPackage" }, StringSplitOptions.None).Length - 1); // tylko w helperze Register-DsoStorePackage
        Assert.Equal(1, apply.Split(new[] { "Get-AppxPackage" }, StringSplitOptions.None).Length - 1); // tylko w helperze Get-DsoStorePackage
        Assert.Contains("-AllUsers", apply, StringComparison.Ordinal);
        Assert.Contains("-DisableDevelopmentMode", apply, StringComparison.Ordinal);
    }

    [Fact]
    public void Docs_ExistWithMeasuredFindingsAndSources()
    {
        var text = File.ReadAllText(RepoRoot.Combine("docs", "tweaks", Id + ".md"), Encoding.UTF8).Replace("\r\n", "\n");
        Assert.Contains("## Werdykt 26H2\nPortować jako `kind=action`, `risk=caution`", text, StringComparison.Ordinal);
        Assert.Contains("22506.1400.2.0", text, StringComparison.Ordinal);
        Assert.Contains("0x80070520", text, StringComparison.Ordinal);
        Assert.Contains("0x80072ee7", text, StringComparison.Ordinal);
        Assert.Contains("StorePurchaseApp", text, StringComparison.Ordinal);
        Assert.Contains("add-appxpackage", text, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("might be limited", text, StringComparison.Ordinal);
        Assert.Contains("2026-10-19", text, StringComparison.Ordinal);
    }

    [Fact]
    public void Docs_CoverStagedRegistration_AndObservation()
    {
        var text = File.ReadAllText(RepoRoot.Combine("docs", "tweaks", Id + ".md"), Encoding.UTF8).Replace("\r\n", "\n");
        Assert.Contains("Staged", text, StringComparison.Ordinal);
        Assert.Contains("S-1-5-18", text, StringComparison.Ordinal);
        Assert.Contains("repair-store-staged-dso", text, StringComparison.Ordinal);
        Assert.Contains("## Obserwacja skutku", text, StringComparison.Ordinal);
        Assert.Contains("2026-10-22", text, StringComparison.Ordinal);
        Assert.False(File.Exists(RepoRoot.Combine("docs", "tweaks", "tools.store-repair.md")));
    }

    [Fact]
    public void Description_MentionsOnDiskRegistration_AndWarningsCoverIt()
    {
        var def = Get();
        Assert.Contains("Staged", def.Description, StringComparison.Ordinal);
        Assert.Contains(def.Warnings, w => w.Contains("na dysku", StringComparison.OrdinalIgnoreCase) && w.Contains("nie pobiera", StringComparison.OrdinalIgnoreCase));
    }

    // ---- skrypt w harnessie ----

    private static string User(string sid, string state = "Installed")
        => $"[pscustomobject]@{{ Sid = '{sid}'; InstallState = '{state}' }}";

    private static string Pkg(string name, string location, string status, string version, params string[] users)
        => $"[pscustomobject]@{{ Name = '{name}'; PackageFullName = '{name}_{version}_x64__8wekyb3d8bbwe'; Version = '{version}'; InstallLocation = '{location}'; Status = '{status}'; Users = @({string.Join(", ", users)}) }}";

    /// <summary>Mocki: lista pakietów przed rejestracją (<c>before</c>) i po niej (<c>after</c>), SID procesu, ciało rejestracji.</summary>
    private static string Mocks(string processSid, string before, string after, string? registerBody = null)
        => "function Get-DsoProcessSid { '" + processSid + "' }\r\n" +
           "$script:registered = 0\r\n" +
           "$script:before = @(" + before + ")\r\n" +
           "$script:after = @(" + after + ")\r\n" +
           "function Get-DsoStorePackage { if ($script:registered -eq 0) { $script:before } else { $script:after } }\r\n" +
           "function Register-DsoStorePackage { param($ManifestPath) " + (registerBody ?? "") + "; $script:registered = 1; Write-Output ('REGISTER ' + $ManifestPath) }";

    private static bool Registered(ScriptRun run) => run.Output.Any(l => l.StartsWith("REGISTER ", StringComparison.Ordinal));

    private sealed class StoreDir : IDisposable
    {
        public string Path { get; } = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "dso-tests", "store-" + Guid.NewGuid().ToString("N"));

        public StoreDir(bool withManifest)
        {
            Directory.CreateDirectory(Path);
            if (withManifest)
                File.WriteAllText(System.IO.Path.Combine(Path, "AppxManifest.xml"), "<Package/>");
        }

        public void Dispose() { try { Directory.Delete(Path, true); } catch (IOException) { } }
    }

    private static ScriptRun Run(string mocks) => PowerShellHarness.Run(Apply(), mocks, string.Empty);

    [Fact]
    public void ProcessSidDifferentFromUserSid_FailsWithoutRegistering()
    {
        using var dir = new StoreDir(true);
        var store = Pkg(StoreName, dir.Path, "Ok", "22506.1400.2.0", User(UserSid));
        var nameMock = "function Get-DsoAccountName { param([string]$Sid) if ($Sid -eq '" + UserSid + "') { 'TESTPC\\dso' } else { 'ZLE-KONTO' } }\r\n";
        var run = Run(nameMock + Mocks(OtherSid, store, store));

        Assert.NotEqual(0, run.ExitCode);
        // To samo zdanie co w repair.winget (spec 2): nazwa zalogowanego konta zamiast SID-ów.
        Assert.Contains("Naprawa Microsoft Store działa tylko dla konta, na którym działa DSOptimizer: uruchom DSOptimizer na koncie TESTPC\\dso. Nic nie zmieniono.", run.Error, StringComparison.Ordinal);
        Assert.DoesNotContain(OtherSid, run.Error, StringComparison.Ordinal);
        Assert.DoesNotContain("SID procesu", run.Error, StringComparison.Ordinal);
        Assert.False(Registered(run));
        Assert.DoesNotContain("Bezpiecznik harnessu", run.Error, StringComparison.Ordinal);
    }

    [Fact]
    public void ProcessSidDifferent_AccountNameUnresolvable_RefusesWithUserSid_BeforeReadingPackages()
    {
        // Bez mocka nazwy: prawdziwy helper nie przetłumaczy testowego SID-u i oddaje sam SID; pakietów nawet nie czyta.
        var mocks = "function Get-DsoProcessSid { '" + OtherSid + "' }\r\n";
        var run = Run(mocks);

        Assert.NotEqual(0, run.ExitCode);
        Assert.Contains("uruchom DSOptimizer na koncie " + UserSid + ". Nic nie zmieniono.", run.Error, StringComparison.Ordinal);
        Assert.DoesNotContain("Bezpiecznik harnessu", run.Error, StringComparison.Ordinal);
    }

    [Fact]
    public void ProcessSidMatchingIgnoresCase()
    {
        using var dir = new StoreDir(true);
        var before = Pkg(StoreName, dir.Path, "Ok", "22506.1400.2.0", User(UserSid));
        var run = Run(Mocks(UserSid.ToLowerInvariant(), before, before));

        Assert.Equal(0, run.ExitCode);
    }

    [Fact]
    public void NoPackageAtAll_FailsWithD4Message_WithoutRegistering()
    {
        var run = Run(Mocks(UserSid, string.Empty, string.Empty));

        Assert.NotEqual(0, run.ExitCode);
        Assert.Contains("Pakiet Microsoft Store nie jest zainstalowany dla tego konta; ta akcja go nie instaluje.", run.Error, StringComparison.Ordinal);
        Assert.False(Registered(run));
    }

    [Fact]
    public void OnlyStorePurchaseApp_IsNotTakenForStore_ExactNameFilter()
    {
        using var dir = new StoreDir(true);
        var purchase = Pkg("Microsoft.StorePurchaseApp", dir.Path, "Ok", "22509.1400.2.0", User(UserSid));
        var run = Run(Mocks(UserSid, purchase, purchase));

        Assert.NotEqual(0, run.ExitCode);
        Assert.Contains("nie jest zainstalowany dla tego konta", run.Error, StringComparison.Ordinal);
        Assert.False(Registered(run));
    }

    [Fact]
    public void PackageInstalledOnlyForOtherAccount_OnDisk_RegistersForThisAccount_R33()
    {
        // R33: pakiet jest na dysku (wpis innego konta), ale bez wpisu Installed dla SID konta: rejestracja z manifestu i odczyt zwrotny.
        using var dir = new StoreDir(true);
        var before = Pkg(StoreName, dir.Path, "Ok", "22506.1400.2.0", User(OtherSid));
        var after = Pkg(StoreName, dir.Path, "Ok", "22506.1400.2.0", User(OtherSid), User(UserSid));
        var run = Run(Mocks(UserSid, before, after));

        Assert.Equal(0, run.ExitCode);
        Assert.Equal(new[] { "REGISTER " + System.IO.Path.Combine(dir.Path, "AppxManifest.xml") }, run.Output.Where(l => l.StartsWith("REGISTER ", StringComparison.Ordinal)));
        Assert.Contains(run.Output, l => l.Contains("zarejestrowany", StringComparison.Ordinal) && l.Contains("na dysku", StringComparison.Ordinal));
        Assert.Empty(run.Error);
    }

    [Theory]
    [InlineData("Staged")]
    [InlineData("NotInstalled")]
    public void PackageNotInstalledStateForUser_OnDisk_RegistersAndVerifies_R33(string state)
    {
        using var dir = new StoreDir(true);
        var before = Pkg(StoreName, dir.Path, "Ok", "22506.1400.2.0", User(UserSid, state));
        var after = Pkg(StoreName, dir.Path, "Ok", "22506.1400.2.0", User(UserSid));
        var run = Run(Mocks(UserSid, before, after));

        Assert.Equal(0, run.ExitCode);
        Assert.True(Registered(run));
        Assert.Contains(run.Output, l => l.Contains("zarejestrowany", StringComparison.Ordinal));
    }

    [Fact]
    public void StagedForSystemAccountOnly_RegistersForUser_AndReportsVerifiedInstalled_R33()
    {
        // Stan zmierzony na VM po Remove-AppxPackage jako dso: tylko S-1-5-18 = Staged, bez wpisu SID konta.
        using var dir = new StoreDir(true);
        var before = Pkg(StoreName, dir.Path, "Ok", "22506.1400.2.0", User("S-1-5-18", "Staged"));
        var after = Pkg(StoreName, dir.Path, "Ok", "22506.1400.2.0", User("S-1-5-18", "Staged"), User(UserSid));
        var run = Run(Mocks(UserSid, before, after));

        Assert.Equal(0, run.ExitCode);
        Assert.Equal(1, run.Output.Count(l => l.StartsWith("REGISTER ", StringComparison.Ordinal)));
        Assert.Contains(run.Output, l => l.Contains("22506.1400.2.0", StringComparison.Ordinal) && l.Contains("odczyt zwrotny", StringComparison.Ordinal));
    }

    [Fact]
    public void PackageWithoutAnyUserEntry_OnDisk_Registers_R33()
    {
        using var dir = new StoreDir(true);
        var before = Pkg(StoreName, dir.Path, "Ok", "22506.1400.2.0");
        var after = Pkg(StoreName, dir.Path, "Ok", "22506.1400.2.0", User(UserSid));
        var run = Run(Mocks(UserSid, before, after));

        Assert.Equal(0, run.ExitCode);
        Assert.True(Registered(run));
    }

    [Fact]
    public void StagedRegistration_ReadBackStillWithoutInstalledEntry_FailsEvenWithoutError_R33()
    {
        using var dir = new StoreDir(true);
        var before = Pkg(StoreName, dir.Path, "Ok", "22506.1400.2.0", User("S-1-5-18", "Staged"));
        var run = Run(Mocks(UserSid, before, before));

        Assert.NotEqual(0, run.ExitCode);
        Assert.True(Registered(run));
        Assert.Contains("odczyt zwrotny", run.Error, StringComparison.Ordinal);
        Assert.DoesNotContain("zarejestrowany", string.Join("\n", run.Output), StringComparison.Ordinal);
    }

    [Fact]
    public void StagedRegistration_ReadBackInstalledButStatusNotOk_Fails_R33()
    {
        using var dir = new StoreDir(true);
        var before = Pkg(StoreName, dir.Path, "Ok", "22506.1400.2.0", User("S-1-5-18", "Staged"));
        var after = Pkg(StoreName, dir.Path, "NeedsRemediation", "22506.1400.2.0", User(UserSid));
        var run = Run(Mocks(UserSid, before, after));

        Assert.NotEqual(0, run.ExitCode);
        Assert.Contains("Status", run.Error, StringComparison.Ordinal);
    }

    [Fact]
    public void StagedRegistration_RegisterThrows_Fails_R33()
    {
        using var dir = new StoreDir(true);
        var before = Pkg(StoreName, dir.Path, "Ok", "22506.1400.2.0", User("S-1-5-18", "Staged"));
        var run = Run(Mocks(UserSid, before, before, registerBody: "throw 'Deployment failed 0x80073CF9'"));

        Assert.NotEqual(0, run.ExitCode);
        Assert.Contains("Deployment failed 0x80073CF9", run.Error, StringComparison.Ordinal);
    }

    [Fact]
    public void RegisterThrowsPackagesInUse_0x80073D02_SaysStoreIsInUse_CloseAndRetry()
    {
        // VM 26H2 Home i Pro (Task 10): tuż po zalogowaniu Add-AppxPackage -Register zwraca 0x80073D02 („aplikacje trzeba zamknąć:
        // Microsoft.WindowsStore”), a ta sama rejestracja kilkadziesiąt sekund później przechodzi. Komunikat ma powiedzieć, co zrobić.
        using var dir = new StoreDir(true);
        var store = Pkg(StoreName, dir.Path, "Ok", "22506.1400.2.0", User(UserSid));
        var run = Run(Mocks(UserSid, store, store, registerBody: "throw 'Deployment failed with HRESULT: 0x80073D02, Nie można zainstalować pakietu, ponieważ zasoby, które on modyfikuje, są obecnie w użyciu.'"));

        Assert.NotEqual(0, run.ExitCode);
        Assert.Contains("Microsoft Store jest teraz w użyciu", run.Error, StringComparison.Ordinal);
        Assert.Contains("zamknij Sklep", run.Error, StringComparison.Ordinal);
        Assert.Contains("uruchom naprawę ponownie", run.Error, StringComparison.Ordinal);
        Assert.Contains("0x80073D02", run.Error, StringComparison.Ordinal);
        Assert.DoesNotContain("zarejestrowany", string.Join("\n", run.Output), StringComparison.Ordinal);
    }

    [Fact]
    public void RegisterThrowsOtherError_KeepsGenericMessage_WithoutInUseHint()
    {
        using var dir = new StoreDir(true);
        var store = Pkg(StoreName, dir.Path, "Ok", "22506.1400.2.0", User(UserSid));
        var run = Run(Mocks(UserSid, store, store, registerBody: "throw 'Deployment failed 0x80073CF9'"));

        Assert.NotEqual(0, run.ExitCode);
        Assert.Contains("nie powiodła się: Deployment failed 0x80073CF9", run.Error, StringComparison.Ordinal);
        Assert.DoesNotContain("w użyciu", run.Error, StringComparison.Ordinal);
    }

    [Fact]
    public void StagedPackage_ManifestMissing_FailsWithoutRegistering_R33()
    {
        using var dir = new StoreDir(false);
        var before = Pkg(StoreName, dir.Path, "Ok", "22506.1400.2.0", User("S-1-5-18", "Staged"));
        var run = Run(Mocks(UserSid, before, before));

        Assert.NotEqual(0, run.ExitCode);
        Assert.Contains("nie można odczytać", run.Error, StringComparison.Ordinal);
        Assert.False(Registered(run));
    }

    [Fact]
    public void SeveralOnDiskStorePackages_WithoutInstalledEntry_FailAsAmbiguous_R33()
    {
        using var dir = new StoreDir(true);
        var a = Pkg(StoreName, dir.Path, "Ok", "22506.1400.2.0", User("S-1-5-18", "Staged"));
        var b = Pkg(StoreName, dir.Path, "Ok", "22507.1400.2.0", User("S-1-5-18", "Staged"));
        var run = Run(Mocks(UserSid, a + ", " + b, a + ", " + b));

        Assert.NotEqual(0, run.ExitCode);
        Assert.Contains("kilka pakietów", run.Error, StringComparison.Ordinal);
        Assert.False(Registered(run));
    }

    [Fact]
    public void InstalledForUser_TakesPrecedenceOverOtherOnDiskVersions_AndKeepsReRegistration()
    {
        // Wpis Installed dla konta wygrywa: dotychczasowe zachowanie (ponowna rejestracja tego pakietu), bez rejestrowania innej wersji ze Staged.
        using var dir = new StoreDir(true);
        using var otherDir = new StoreDir(true);
        var installed = Pkg(StoreName, dir.Path, "Ok", "22506.1400.2.0", User(UserSid));
        var staged = Pkg(StoreName, otherDir.Path, "Ok", "22400.1400.2.0", User("S-1-5-18", "Staged"));
        var run = Run(Mocks(UserSid, installed + ", " + staged, installed + ", " + staged));

        Assert.Equal(0, run.ExitCode);
        Assert.Equal(new[] { "REGISTER " + System.IO.Path.Combine(dir.Path, "AppxManifest.xml") }, run.Output.Where(l => l.StartsWith("REGISTER ", StringComparison.Ordinal)));
        Assert.DoesNotContain(run.Output, l => l.Contains("na dysku", StringComparison.Ordinal));
    }

    [Fact]
    public void ManifestMissing_FailsWithReason_WithoutRegistering()
    {
        using var dir = new StoreDir(false);
        var store = Pkg(StoreName, dir.Path, "Ok", "22506.1400.2.0", User(UserSid));
        var run = Run(Mocks(UserSid, store, store));

        Assert.NotEqual(0, run.ExitCode);
        Assert.Contains("AppxManifest.xml", run.Error, StringComparison.Ordinal);
        Assert.Contains("nie można odczytać", run.Error, StringComparison.Ordinal);
        Assert.False(Registered(run));
    }

    [Fact]
    public void EmptyInstallLocation_FailsWithReason_WithoutRegistering()
    {
        var store = Pkg(StoreName, string.Empty, "Ok", "22506.1400.2.0", User(UserSid));
        var run = Run(Mocks(UserSid, store, store));

        Assert.NotEqual(0, run.ExitCode);
        Assert.Contains("nie można odczytać", run.Error, StringComparison.Ordinal);
        Assert.False(Registered(run));
    }

    [Fact]
    public void SeveralStorePackagesForUser_FailsAsAmbiguous_WithoutRegistering()
    {
        using var dir = new StoreDir(true);
        var a = Pkg(StoreName, dir.Path, "Ok", "22506.1400.2.0", User(UserSid));
        var b = Pkg(StoreName, dir.Path, "Ok", "22507.1400.2.0", User(UserSid));
        var run = Run(Mocks(UserSid, a + ", " + b, a + ", " + b));

        Assert.NotEqual(0, run.ExitCode);
        Assert.Contains("kilka pakietów", run.Error, StringComparison.Ordinal);
        Assert.False(Registered(run));
    }

    [Fact]
    public void RegistrationError_FailsWithMessage_AndNeverReportsSuccess()
    {
        using var dir = new StoreDir(true);
        var store = Pkg(StoreName, dir.Path, "Ok", "22506.1400.2.0", User(UserSid));
        var run = Run(Mocks(UserSid, store, store, registerBody: "throw 'Deployment failed 0x80073CF9'"));

        Assert.NotEqual(0, run.ExitCode);
        Assert.Contains("Deployment failed 0x80073CF9", run.Error, StringComparison.Ordinal);
        Assert.DoesNotContain("zarejestrowany", string.Join("\n", run.Output), StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("NeedsRemediation")]
    [InlineData("Modified")]
    [InlineData("")]
    public void StatusAfterRegistrationNotOk_Fails(string status)
    {
        using var dir = new StoreDir(true);
        var before = Pkg(StoreName, dir.Path, "Ok", "22506.1400.2.0", User(UserSid));
        var after = Pkg(StoreName, dir.Path, status, "22506.1400.2.0", User(UserSid));
        var run = Run(Mocks(UserSid, before, after));

        Assert.NotEqual(0, run.ExitCode);
        Assert.Contains("Status", run.Error, StringComparison.Ordinal);
        Assert.True(Registered(run));
        Assert.DoesNotContain("zarejestrowany", string.Join("\n", run.Output), StringComparison.Ordinal);
    }

    [Fact]
    public void ReadBackWithoutPackageForUser_FailsEvenAfterRegistrationWithoutError()
    {
        using var dir = new StoreDir(true);
        var before = Pkg(StoreName, dir.Path, "Ok", "22506.1400.2.0", User(UserSid));
        var run = Run(Mocks(UserSid, before, string.Empty));

        Assert.NotEqual(0, run.ExitCode);
        Assert.Contains("odczyt zwrotny", run.Error, StringComparison.Ordinal);
        Assert.DoesNotContain("zarejestrowany", string.Join("\n", run.Output), StringComparison.Ordinal);
    }

    [Fact]
    public void ReadBackShowingPackageOnlyForOtherAccount_Fails()
    {
        using var dir = new StoreDir(true);
        var before = Pkg(StoreName, dir.Path, "Ok", "22506.1400.2.0", User(UserSid));
        var after = Pkg(StoreName, dir.Path, "Ok", "22506.1400.2.0", User(OtherSid));
        var run = Run(Mocks(UserSid, before, after));

        Assert.NotEqual(0, run.ExitCode);
        Assert.Contains("odczyt zwrotny", run.Error, StringComparison.Ordinal);
    }

    [Fact]
    public void Success_RegistersExactStoreManifestOnce_AndReportsVersionFromReadBack()
    {
        using var dir = new StoreDir(true);
        using var purchaseDir = new StoreDir(true);
        var purchase = Pkg("Microsoft.StorePurchaseApp", purchaseDir.Path, "Ok", "22509.1400.2.0", User(UserSid));
        var beforeStore = Pkg(StoreName, dir.Path, "Ok", "22506.1400.2.0", User(OtherSid), User(UserSid));
        var afterStore = Pkg(StoreName, dir.Path, "Ok", "22506.1400.3.0", User(OtherSid), User(UserSid));
        var run = Run(Mocks(UserSid, purchase + ", " + beforeStore, purchase + ", " + afterStore));

        Assert.Equal(0, run.ExitCode);
        Assert.Equal(new[] { "REGISTER " + System.IO.Path.Combine(dir.Path, "AppxManifest.xml") }, run.Output.Where(l => l.StartsWith("REGISTER ", StringComparison.Ordinal)));
        Assert.Contains(run.Output, l => l.Contains("zarejestrowany", StringComparison.Ordinal) && l.Contains("22506.1400.3.0", StringComparison.Ordinal));
        Assert.Empty(run.Error);
    }

    [Fact]
    public void Store_English_Message()
    {
        using var dir = new StoreDir(true);
        var before = Pkg(StoreName, dir.Path, "Ok", "22506.1400.2.0", User("S-1-5-18", "Staged"));
        var after = Pkg(StoreName, dir.Path, "Ok", "22506.1400.2.0", User(UserSid));
        ScriptRun run;
        using (L.Use(UiLanguage.English))
        {
            run = Run(Mocks(UserSid, before, after));
        }

        Assert.Equal(0, run.ExitCode);
        Assert.True(Registered(run));
        Assert.Equal(
            "Microsoft Store package registered for account " + UserSid + " (it was on the disk without an Installed entry for this account): version 22506.1400.2.0 (" + StoreFull + "), Status Ok (verified).",
            run.LastLine);
    }
}
