using System;
using System.IO;
using System.Linq;
using System.Text;
using DSOptimizer.Catalog;
using DSOptimizer.Core.Catalog;
using DSOptimizer.Core.Localization;
using DSOptimizer.Core.Model;
using DSOptimizer.Tests.Unit.Support;
using Xunit;

namespace DSOptimizer.Tests.Unit.Catalog;

/// <summary>
/// sec.bitlocker-decrypt-c (partia C4, Task 4): akcja bez detekcji, journala i undo. Jedyny dowód skutku to odczyt zwrotny
/// <c>ConversionStatus</c> po <c>Decrypt</c>; rozjazd daje kod ≠ 0 (Failed w silniku), a nie zielone „Executed”.
/// Skrypty idą przez harness wyłącznie z mockami helperów <c>Get-DsoVolumeConversionStatus</c> i <c>Invoke-DsoVolumeDecrypt</c>;
/// nic nie dotyka WMI ani BitLockera hosta (bezpieczniki harnessu rzucają przy każdym niezamockowanym wywołaniu).
/// </summary>
public class SecDecryptActionTests
{
    private const string Id = "sec.bitlocker-decrypt-c";
    private const uint AutoUnlockEnabled = 0x80310029;
    private const uint LockedVolume = 0x80310000;
    private const string AlreadyDecryptedMessage = "C: nie jest zaszyfrowany – nic nie zmieniono";

    private static TweakDefinition Get()
        => CatalogLoader.Load(BuiltInCatalog.Sources).Definitions.Single(d => d.Id == Id);

    private static string Doc()
        => File.ReadAllText(Path.Combine(RepoRoot.Combine("docs", "tweaks"), Id + ".md"), Encoding.UTF8);

    private static string Apply() => Assert.IsType<string>(Get().Script!.Apply);

    private static string AllText(TweakDefinition def) => def.Description + " " + string.Join(" ", def.Warnings);

    private static bool Has(string line, string text) => line.IndexOf(text, StringComparison.Ordinal) >= 0;

    // ---- katalog ----

    [Fact]
    public void Entry_IsActionInSecCategory_CautionMachine_WithoutDetectUndoRegistryOrServices()
    {
        var def = Get();
        Assert.Equal("sec", def.Category);
        Assert.Equal(TweakKind.Action, def.Kind);
        Assert.Equal(RiskLevel.Caution, def.Risk);
        Assert.Equal(TweakScope.Machine, def.Scope);
        Assert.False(def.RequiresReboot);
        Assert.Empty(def.Aliases);
        Assert.Empty(def.Registry);
        Assert.Empty(def.Services);
        Assert.Empty(def.Conflicts);
        Assert.Equal(TweakDefinition.DefaultTimeout, def.Timeout); // akcja kończy się od razu (Decrypt wraca natychmiast); domyślny limit wystarcza
        var script = Assert.IsType<ScriptSet>(def.Script);
        Assert.NotNull(script.Apply);
        Assert.Null(script.Detect);
        Assert.Null(script.Undo);
        Assert.Null(script.Capture);
    }

    [Fact]
    public void Warnings_StateUnprotectedData_NoUndoAndPolicyAndPreventAuto()
    {
        var def = Get();
        var text = AllText(def);
        // Spec opisów 2026-10-26: cytat Microsoftu tylko w dokumencie, w uwagach jego sens po polsku.
        Assert.Contains("Calling the Decrypt method leaves data unprotected.", Doc(), StringComparison.Ordinal);
        Assert.Contains(def.Warnings, w => w.Contains("nie są chronione", StringComparison.Ordinal) && w.Contains("kradzieży", StringComparison.Ordinal));
        // Brak cofania zmienia decyzję: uwaga widoczna od razu, z drogą powrotu (Ustawienia).
        Assert.Contains(def.Warnings, w => w.Contains("cofnąć", StringComparison.Ordinal) && w.Contains("Ustawieniach", StringComparison.Ordinal));
        Assert.EndsWith("Bez cofania – ponowne zaszyfrowanie dysku to osobna decyzja w Ustawieniach Windows.", def.Description, StringComparison.Ordinal); // zdanie o cofaniu na końcu opisu, jak w naprawach
        Assert.Contains("Ustawieniach", text, StringComparison.Ordinal); // ponowne szyfrowanie to osobna decyzja w Ustawieniach
        Assert.Contains(CatalogLoader.Load(BuiltInCatalog.Sources).Definitions.Single(d => d.Id == "sec.bitlocker-prevent-auto").Title, text, StringComparison.Ordinal); // nazwa pozycji, nie identyfikator
        // Ponowne szyfrowanie bez blokady: w programie tylko „może zaszyfrować”, niepewność (niezmierzone) w dokumencie – zasada 3 spec opisów.
        Assert.Contains("może zaszyfrować dysk ponownie", def.Description, StringComparison.Ordinal);
        Assert.Contains("niezmierzone; MS tego nie opisuje", Doc(), StringComparison.Ordinal);
        Assert.Contains("polityk", text, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("Clear-BitLockerAutoUnlock", text, StringComparison.Ordinal); // klucze automatycznego odblokowania: akcja ich nie usuwa
    }

    [Fact]
    public void Description_SaysDecryptReturnsImmediately_AndPromisesNoCompletion()
    {
        var description = Get().Description;
        Assert.Contains("Win32_EncryptableVolume", description, StringComparison.Ordinal);
        Assert.Contains("ConversionStatus", description, StringComparison.Ordinal);
        Assert.Contains("w tle", description, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("bezpiecznie", description, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Script_CallsWmiOnlyInsideHelperDefinitions_NeverTouchesRegistryHkcuOrOtherTools()
    {
        var apply = Apply();
        Assert.Equal(2, apply.Split(new[] { "Invoke-CimMethod" }, StringSplitOptions.None).Length - 1); // GetConversionStatus + Decrypt
        Assert.Equal(2, apply.Split(new[] { "Get-CimInstance" }, StringSplitOptions.None).Length - 1);
        Assert.Contains("-MethodName 'GetConversionStatus'", apply, StringComparison.Ordinal);
        Assert.Contains("-MethodName 'Decrypt'", apply, StringComparison.Ordinal);
        Assert.Contains("DriveLetter='C:'", apply, StringComparison.Ordinal);
        Assert.Contains("root\\CIMV2\\Security\\MicrosoftVolumeEncryption", apply, StringComparison.Ordinal);
        Assert.DoesNotContain("ClearAllAutoUnlockKeys", apply, StringComparison.Ordinal);
        Assert.DoesNotContain("HKCU", apply, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("HKLM", apply, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("manage-bde", apply, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("Disable-BitLocker", apply, StringComparison.Ordinal);
        Assert.DoesNotContain("Invoke-WmiMethod", apply, StringComparison.Ordinal);
        Assert.DoesNotContain("Get-WmiObject", apply, StringComparison.Ordinal);
    }

    // ---- mocki helperów ----

    private static string St(uint returnValue, uint status, uint percent)
        => $"[pscustomobject]@{{ ReturnValue = [uint32]{returnValue}; ConversionStatus = [uint32]{status}; EncryptionPercentage = [uint32]{percent} }}";

    /// <summary>Pierwszy odczyt statusu zwraca <paramref name="before"/>, kolejne <paramref name="after"/>; Decrypt zwraca <paramref name="decrypt"/>.</summary>
    private static string Mocks(string before, string after, string decrypt = "[uint32]0")
        => "$script:reads = 0\r\n" +
           "function Get-DsoVolumeConversionStatus { $script:reads++; if ($script:reads -eq 1) { " + before + " } else { " + after + " } }\r\n" +
           "function Invoke-DsoVolumeDecrypt { Write-Host 'DECRYPT-CALLED'; " + decrypt + " }\r\n" +
           "function Clear-BitLockerAutoUnlock { Write-Host 'CLEAR-CALLED' }";

    private static ScriptRun Run(string mocks) => PowerShellHarness.Run(Apply(), mocks, string.Empty);

    // ---- Review Focus 1: już odszyfrowany dysk ----

    [Fact]
    public void AlreadyDecrypted_IsExecuted_SaysNothingChanged_AndNeverCallsDecrypt()
    {
        var mocks = "function Get-DsoVolumeConversionStatus { " + St(0, 0, 0) + " }\r\n" +
                    "function Invoke-DsoVolumeDecrypt { throw 'Decrypt wywołane na odszyfrowanym dysku' }";
        var run = Run(mocks);

        Assert.Equal(0, run.ExitCode);
        Assert.Contains(AlreadyDecryptedMessage, run.Output);
        Assert.Empty(run.Error);
    }

    // ---- ścieżki 1/2/4: Decrypt, potem odczyt zwrotny ----

    [Theory]
    [InlineData(1)] // FullyEncrypted
    [InlineData(2)] // EncryptionInProgress (np. auto-szyfrowanie Home z checkpointu)
    [InlineData(4)] // EncryptionPaused
    public void Encrypted_CallsDecrypt_ReadsBackDecryptionInProgress_AndReportsPercent(uint before)
    {
        var run = Run(Mocks(St(0, before, 78), St(0, 3, 77)));

        Assert.Equal(0, run.ExitCode);
        Assert.Contains("DECRYPT-CALLED", run.Output);
        Assert.DoesNotContain("CLEAR-CALLED", run.Output);
        Assert.Contains(run.Output, line => Has(line, "Odszyfrowywanie C: rozpoczęte: 77% zaszyfrowane. Windows kontynuuje w tle, także po restarcie"));
        Assert.Empty(run.Error);
    }

    [Fact]
    public void SecBitlockerDecryptC_English_Message()
    {
        using (L.Use(UiLanguage.English))
        {
            var run = Run(Mocks(St(0, 1, 78), St(0, 3, 77)));

            Assert.Equal(0, run.ExitCode);
            Assert.Contains("Decryption of C: started: 77% encrypted. Windows continues in the background, even after a restart.", run.Output);
            Assert.Empty(run.Error);
        }
    }

    [Fact]
    public void Encrypted_WhenReadBackIsFullyDecrypted_IsExecutedAsFinished()
    {
        // Bardzo mały wolumin: Decrypt zdążył skończyć przed odczytem (MS: status po 3 lub 0).
        var run = Run(Mocks(St(0, 1, 100), St(0, 0, 0)));

        Assert.Equal(0, run.ExitCode);
        Assert.Contains("DECRYPT-CALLED", run.Output);
        Assert.Contains(run.Output, line => Has(line, "Odszyfrowywanie C: zakończone"));
    }

    // ---- ścieżka 3/5: wznowienie ----

    [Theory]
    [InlineData(3)] // DecryptionInProgress
    [InlineData(5)] // DecryptionPaused
    public void DecryptionAlreadyStarted_CallsDecryptAsResume_AndReportsInProgressPercent(uint before)
    {
        var run = Run(Mocks(St(0, before, 40), St(0, 3, 39)));

        Assert.Equal(0, run.ExitCode);
        Assert.Contains("DECRYPT-CALLED", run.Output);
        Assert.Contains(run.Output, line => Has(line, "Odszyfrowywanie C: w toku: 39% zaszyfrowane. Windows kontynuuje w tle, także po restarcie"));
    }

    // ---- Review Focus 2: kody błędów Decrypt ----

    [Fact]
    public void AutoUnlockKeys_FailsWithPolishMessageAndHexCode_WithoutClearingKeys()
    {
        var run = Run(Mocks(St(0, 1, 100), St(0, 3, 99), decrypt: $"[uint32]{AutoUnlockEnabled}"));

        Assert.NotEqual(0, run.ExitCode);
        Assert.Contains("0x80310029", run.Error, StringComparison.Ordinal);
        Assert.Contains("Najpierw usuń klucze automatycznego odblokowania innych dysków (Clear-BitLockerAutoUnlock) – akcja tego nie robi", run.Error, StringComparison.Ordinal);
        Assert.Contains("DECRYPT-CALLED", run.Output);
        Assert.DoesNotContain("CLEAR-CALLED", run.Output);
        Assert.DoesNotContain(run.Output, line => Has(line, "Odszyfrowywanie C:")); // żadnego komunikatu sukcesu
    }

    [Theory]
    [InlineData(LockedVolume, "0x80310000")]
    [InlineData(1u, "0x00000001")]
    [InlineData(0x8031001Eu, "0x8031001E")]
    public void OtherNonZeroDecryptCode_FailsWithHexCode_AndDoesNotReadBackAsSuccess(uint code, string hex)
    {
        var run = Run(Mocks(St(0, 1, 100), St(0, 3, 99), decrypt: $"[uint32]{code}"));

        Assert.NotEqual(0, run.ExitCode);
        Assert.Contains(hex, run.Error, StringComparison.Ordinal);
        Assert.Contains("Decrypt", run.Error, StringComparison.Ordinal);
        Assert.DoesNotContain("CLEAR-CALLED", run.Output);
        Assert.DoesNotContain(run.Output, line => Has(line, "Odszyfrowywanie C:"));
    }

    [Theory]
    [InlineData(1)] // nadal FullyEncrypted: Decrypt zwrócił 0, ale nic się nie zmieniło
    [InlineData(2)]
    [InlineData(4)]
    [InlineData(5)]
    [InlineData(9)] // poza enumem MS
    public void StatusAfterDecryptOutsideZeroAndThree_IsFailure_NotSuccess(uint after)
    {
        var run = Run(Mocks(St(0, 1, 100), St(0, after, 100)));

        Assert.NotEqual(0, run.ExitCode);
        Assert.Contains($"ConversionStatus = {after}", run.Error, StringComparison.Ordinal);
        Assert.Contains("odczyt zwrotny", run.Error, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain(run.Output, line => Has(line, "Odszyfrowywanie C:"));
    }

    [Fact]
    public void ReadBackReturnCodeNonZero_IsFailureWithHexCode()
    {
        var run = Run(Mocks(St(0, 1, 100), St(LockedVolume, 3, 99)));

        Assert.NotEqual(0, run.ExitCode);
        Assert.Contains("0x80310000", run.Error, StringComparison.Ordinal);
        Assert.DoesNotContain(run.Output, line => Has(line, "Odszyfrowywanie C:"));
    }

    [Fact]
    public void ReadBackReturnsNothing_IsFailure()
    {
        var run = Run(Mocks(St(0, 1, 100), "$null"));

        Assert.NotEqual(0, run.ExitCode);
        Assert.Contains("odczyt zwrotny", run.Error, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain(run.Output, line => Has(line, "Odszyfrowywanie C:"));
    }

    // ---- carry z Tasku 4 (m1): wynik helpera bez ReturnValue/ConversionStatus to błąd, nie „0” ----

    [Theory]
    [InlineData("[pscustomobject]@{ ConversionStatus = [uint32]0; EncryptionPercentage = [uint32]0 }")] // brak ReturnValue
    [InlineData("[pscustomobject]@{ ReturnValue = [uint32]0; EncryptionPercentage = [uint32]0 }")] // brak ConversionStatus
    public void ReadBeforeDecryptWithoutRequiredField_Fails_AndNeverCallsDecrypt(string malformed)
    {
        // `[uint32]$null` = 0 odczytałoby się jako FullyDecrypted i akcja udałaby „nic nie zmieniono” na niepewnym stanie.
        var mocks = "function Get-DsoVolumeConversionStatus { " + malformed + " }\r\n" +
                    "function Invoke-DsoVolumeDecrypt { throw 'Decrypt wywołane po niepełnym odczycie' }";
        var run = Run(mocks);

        Assert.NotEqual(0, run.ExitCode);
        Assert.Contains("ReturnValue", run.Error, StringComparison.Ordinal);
        Assert.DoesNotContain(run.Output, line => Has(line, "nic nie zmieniono"));
    }

    [Theory]
    [InlineData("[pscustomobject]@{ ConversionStatus = [uint32]3; EncryptionPercentage = [uint32]1 }")] // brak ReturnValue
    [InlineData("[pscustomobject]@{ ReturnValue = [uint32]0; EncryptionPercentage = [uint32]1 }")] // brak ConversionStatus
    public void ReadBackWithoutRequiredField_IsFailure(string malformed)
    {
        var run = Run(Mocks(St(0, 1, 100), malformed));

        Assert.NotEqual(0, run.ExitCode);
        Assert.Contains("odczyt zwrotny", run.Error, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain(run.Output, line => Has(line, "Odszyfrowywanie C:"));
    }

    [Fact]
    public void DecryptReturningNull_IsFailure_NotSuccessCodeZero()
    {
        // `@($null).Count` = 1, a `[uint32]$null` = 0: bez jawnego sprawdzenia null przeszedłby jako ReturnValue 0.
        var run = Run(Mocks(St(0, 1, 100), St(0, 3, 99), decrypt: "$null"));

        Assert.NotEqual(0, run.ExitCode);
        Assert.Contains("Decrypt nie zwrócił kodu", run.Error, StringComparison.Ordinal);
        Assert.DoesNotContain(run.Output, line => Has(line, "Odszyfrowywanie C:"));
    }

    // ---- odczyt przed Decrypt: nie wołamy Decrypt na niepewnym stanie ----

    [Fact]
    public void NoVolumeForC_FailsWithClearMessage_NeverExecuted_AndNeverCallsDecrypt()
    {
        var mocks = "function Get-DsoVolumeConversionStatus { $null }\r\n" +
                    "function Invoke-DsoVolumeDecrypt { throw 'Decrypt wywołane bez woluminu' }";
        var run = Run(mocks);

        Assert.NotEqual(0, run.ExitCode);
        Assert.Contains("Nie znaleziono woluminu C:", run.Error, StringComparison.Ordinal);
        Assert.DoesNotContain(run.Output, line => Has(line, "nic nie zmieniono"));
    }

    [Fact]
    public void ReadBeforeDecryptReturnsNonZeroCode_FailsWithHex_AndNeverCallsDecrypt()
    {
        var mocks = "function Get-DsoVolumeConversionStatus { " + St(LockedVolume, 0, 0) + " }\r\n" +
                    "function Invoke-DsoVolumeDecrypt { throw 'Decrypt wywołane po błędzie odczytu' }";
        var run = Run(mocks);

        Assert.NotEqual(0, run.ExitCode);
        Assert.Contains("0x80310000", run.Error, StringComparison.Ordinal);
        Assert.DoesNotContain(run.Output, line => Has(line, "nic nie zmieniono"));
    }

    [Fact]
    public void UnknownConversionStatusBeforeDecrypt_Fails_AndNeverCallsDecrypt()
    {
        var mocks = "function Get-DsoVolumeConversionStatus { " + St(0, 9, 0) + " }\r\n" +
                    "function Invoke-DsoVolumeDecrypt { throw 'Decrypt wywołane przy nieznanym statusie' }";
        var run = Run(mocks);

        Assert.NotEqual(0, run.ExitCode);
        Assert.Contains("ConversionStatus = 9", run.Error, StringComparison.Ordinal);
    }

    [Fact]
    public void PercentageAloneIsNeverUsedToDecide_ZeroPercentWithEncryptedStatusStillDecrypts()
    {
        // MS: „Always use ConversionStatus”. Status 2 przy 0% (start szyfrowania) nie jest „odszyfrowany”.
        var run = Run(Mocks(St(0, 2, 0), St(0, 3, 0)));

        Assert.Equal(0, run.ExitCode);
        Assert.Contains("DECRYPT-CALLED", run.Output);
    }

    // ---- błędy WMI nie są połykane ----

    [Fact]
    public void WmiErrorOnFirstRead_Fails_NeverCallsDecrypt()
    {
        var mocks = "function Get-DsoVolumeConversionStatus { throw 'Win32_EncryptableVolume: Brak dostępu' }\r\n" +
                    "function Invoke-DsoVolumeDecrypt { throw 'Decrypt wywołane po błędzie WMI' }";
        var run = Run(mocks);

        Assert.NotEqual(0, run.ExitCode);
        Assert.Contains("Brak dostępu", run.Error, StringComparison.Ordinal);
    }

    [Fact]
    public void WmiErrorInDecrypt_Fails_AndReportsNoSuccess()
    {
        var run = Run(Mocks(St(0, 1, 100), St(0, 3, 99), decrypt: "throw 'Decrypt: odmowa dostępu'"));

        Assert.NotEqual(0, run.ExitCode);
        Assert.Contains("odmowa dostępu", run.Error, StringComparison.Ordinal);
        Assert.DoesNotContain(run.Output, line => Has(line, "Odszyfrowywanie C:"));
    }

    [Fact]
    public void WmiErrorOnReadBack_Fails_AndReportsNoSuccess()
    {
        var run = Run(Mocks(St(0, 1, 100), "throw 'GetConversionStatus: błąd po Decrypt'"));

        Assert.NotEqual(0, run.ExitCode);
        Assert.Contains("błąd po Decrypt", run.Error, StringComparison.Ordinal);
        Assert.DoesNotContain(run.Output, line => Has(line, "Odszyfrowywanie C:"));
    }

    [Fact]
    public void WithoutMocks_HitsGuard_AndNeverTouchesHostBitLocker()
    {
        var run = PowerShellHarness.Run(Apply(), mocks: string.Empty, snapshot: string.Empty);

        Assert.NotEqual(0, run.ExitCode);
        Assert.Contains("Bezpiecznik harnessu", run.Error, StringComparison.Ordinal);
    }

    // ---- dokumentacja ----

    [Fact]
    public void Doc_HasSourcesPathsNoUndoAndTheVmLimitation()
    {
        var doc = Doc();
        Assert.Contains("learn.microsoft.com", doc, StringComparison.Ordinal);
        Assert.Contains("Calling the Decrypt method leaves data unprotected.", doc, StringComparison.Ordinal);
        Assert.Contains("Always use ConversionStatus", doc, StringComparison.Ordinal);
        Assert.Contains("0x80310029", doc, StringComparison.Ordinal);
        Assert.Contains("ClearAllAutoUnlockKeys", doc, StringComparison.Ordinal);
        Assert.Contains(AlreadyDecryptedMessage, doc, StringComparison.Ordinal);
        Assert.Contains("brak cofnięcia", doc, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("odczyt zwrotny", doc, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("Win32_EncryptableVolume", doc, StringComparison.Ordinal);
        Assert.Contains("Get-DsoVolumeConversionStatus", doc, StringComparison.Ordinal);
        Assert.Contains("Invoke-DsoVolumeDecrypt", doc, StringComparison.Ordinal);
        Assert.Contains("Ścieżka zaszyfrowanego dysku: prawdziwy `Decrypt` przećwiczony na VM na OBU edycjach (Home i Pro), bo po starcie checkpointu działa tam automatyczne szyfrowanie urządzenia; własnego szyfrowania w VM nie robimy (decyzja Dawida)", doc, StringComparison.Ordinal);
        Assert.DoesNotContain("Task 6", doc, StringComparison.Ordinal);
        Assert.Contains("vm-run-20261019-002807", doc, StringComparison.Ordinal);
        Assert.Contains("vm-run-decrypt-20261019-005135", doc, StringComparison.Ordinal);
    }
}
