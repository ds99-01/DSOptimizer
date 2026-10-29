using System;
using System.Threading;
using DSOptimizer.Core.Errors;
using DSOptimizer.Core.Localization;
using DSOptimizer.Core.Model;
using DSOptimizer.Core.Operations;
using DSOptimizer.Tests.Unit.Catalog;
using DSOptimizer.Tests.Unit.Fakes;
using Xunit;

namespace DSOptimizer.Tests.Unit.Operations;

public class RegistryValueOperationTests
{
    private const string Path = @"HKLM\SOFTWARE\DSO\Test";
    private readonly FakeRegistry _registry = new();

    private RegistryValueOperation Operation(RegValue? value = null, RegValue? original = null, bool detectOnly = false)
        => new(new RegistryEntry { Path = Path, Name = "A", Value = value ?? RegValue.FromDWord(1), Original = original, DetectOnly = detectOnly }, _registry);

    [Fact]
    public void Detect_ComparesTypeAndValue()
    {
        Assert.Equal(OperationState.Mismatch, Operation().Detect(CancellationToken.None));
        _registry.Set(Path, "A", RegValue.FromString("1"));
        Assert.Equal(OperationState.Mismatch, Operation().Detect(CancellationToken.None));
        _registry.Set(Path, "A", RegValue.FromDWord(1));
        Assert.Equal(OperationState.Match, Operation().Detect(CancellationToken.None));
    }

    [Fact]
    public void Detect_ExpectedAbsentAndMissing_IsMatch()
        => Assert.Equal(OperationState.Match, Operation(RegValue.Absent).Detect(CancellationToken.None));

    [Fact]
    public void Detect_AccessDenied_IsUnknown()
    {
        _registry.DenyRead(Path, "A");
        Assert.Equal(OperationState.Unknown, Operation().Detect(CancellationToken.None));
    }

    [Fact]
    public void Capture_RecordsCurrentValueOrAbsent()
    {
        Assert.Equal(RegValue.Absent, Operation().Capture(CancellationToken.None).Value);
        _registry.Set(Path, "A", RegValue.FromString("x"));
        var snapshot = Operation().Capture(CancellationToken.None);
        Assert.Equal(OperationKind.Registry, snapshot.Op);
        Assert.Equal(Path, snapshot.Path);
        Assert.Equal("A", snapshot.Name);
        Assert.Equal(RegValue.FromString("x"), snapshot.Value);
    }

    [Fact]
    public void Capture_AccessDenied_Throws()
    {
        _registry.DenyRead(Path, "A");
        var ex = Assert.Throws<OperationException>(() => Operation().Capture(CancellationToken.None));
        Assert.Equal(OperationErrorKind.AccessDenied, ex.Kind);
    }

    [Fact]
    public void ApplyAndRestore_WriteValues()
    {
        var operation = Operation();
        var snapshot = operation.Capture(CancellationToken.None);
        operation.Apply(CancellationToken.None);
        Assert.Equal(RegValue.FromDWord(1), _registry.Get(Path, "A"));
        operation.Restore(snapshot, CancellationToken.None);
        Assert.Equal(RegValue.Absent, _registry.Get(Path, "A"));
    }

    [Fact]
    public void Restore_RestoresOriginalType()
    {
        _registry.Set(Path, "A", RegValue.FromString("1"));
        var operation = Operation();
        var snapshot = operation.Capture(CancellationToken.None);
        operation.Apply(CancellationToken.None);
        operation.Restore(snapshot, CancellationToken.None);
        Assert.Equal(RegValue.FromString("1"), _registry.Get(Path, "A"));
    }

    [Fact]
    public void Restore_ForeignSnapshot_Throws()
        => Assert.Throws<ArgumentException>(() => Operation().Restore(SnapshotRecord.ForRegistry(Path, "B", RegValue.Absent), CancellationToken.None));

    [Fact]
    public void DefaultsSnapshot_UsesOriginal()
    {
        Assert.Null(Operation().DefaultsSnapshot());
        Assert.Equal(RegValue.FromDWord(0), Operation(original: RegValue.FromDWord(0)).DefaultsSnapshot()!.Value);
    }

    [Fact]
    public void Owns_MatchesPathAndNameIgnoringCase()
    {
        Assert.True(Operation().Owns(SnapshotRecord.ForRegistry(Path.ToLowerInvariant(), "a", RegValue.Absent)));
        Assert.False(Operation().Owns(SnapshotRecord.ForScript(null)));
    }

    private RegistryValueOperation OperationAt(string path)
        => new(new RegistryEntry { Path = path, Name = "A", Value = RegValue.FromDWord(1) }, _registry);

    [Fact]
    public void Capture_ValueExists_CreatedKeyRootNull()
    {
        _registry.Set(Path, "A", RegValue.FromDWord(7));
        Assert.Null(Operation().Capture(CancellationToken.None).CreatedKeyRoot);
    }

    [Fact]
    public void Capture_KeyExistsValueMissing_CreatedKeyRootNull()
    {
        _registry.CreateKey(Path);
        Assert.Null(Operation().Capture(CancellationToken.None).CreatedKeyRoot);
    }

    [Fact]
    public void Capture_TwoMissingLevels_ReturnsTopMissing()
    {
        _registry.CreateKey(@"HKLM\SOFTWARE\A");
        var snapshot = OperationAt(@"HKLM\SOFTWARE\A\B\C").Capture(CancellationToken.None);
        Assert.Equal(@"HKLM\SOFTWARE\A\B", snapshot.CreatedKeyRoot);
    }

    [Fact]
    public void Capture_HkuPath_NeverAboveSidChild()
    {
        var snapshot = OperationAt(@"HKU\S-1-5-21-1000\Software\X").Capture(CancellationToken.None);
        Assert.Equal(@"HKU\S-1-5-21-1000\Software\X", snapshot.CreatedKeyRoot);
    }

    [Fact]
    public void Capture_PathTooShallow_CreatedKeyRootNull()
        => Assert.Null(OperationAt(@"HKLM\X").Capture(CancellationToken.None).CreatedKeyRoot);

    [Fact]
    public void Capture_KeyAccessDenied_CreatedKeyRootNull()
    {
        _registry.DenyKey(Path);
        Assert.Null(Operation().Capture(CancellationToken.None).CreatedKeyRoot);
    }

    [Fact]
    public void Capture_ParentAccessDenied_CreatedKeyRootNull()
    {
        _registry.CreateKey(@"HKLM\SOFTWARE\A");
        _registry.DenyKey(@"HKLM\SOFTWARE\A\B");
        Assert.Null(OperationAt(@"HKLM\SOFTWARE\A\B\C").Capture(CancellationToken.None).CreatedKeyRoot);
    }

    [Fact]
    public void Restore_DeletesCreatedKeys()
    {
        _registry.CreateKey(@"HKLM\SOFTWARE\DSO");
        var operation = Operation();
        var snapshot = operation.Capture(CancellationToken.None);
        Assert.Equal(Path, snapshot.CreatedKeyRoot);
        operation.Apply(CancellationToken.None);
        operation.Restore(snapshot, CancellationToken.None);
        Assert.False(_registry.HasKey(Path));
        Assert.True(_registry.HasKey(@"HKLM\SOFTWARE\DSO"));
    }

    [Fact]
    public void Restore_DeletesChainUpToCreatedRoot()
    {
        _registry.CreateKey(@"HKLM\SOFTWARE\DSO");
        var operation = OperationAt(@"HKLM\SOFTWARE\DSO\N\K");
        var snapshot = operation.Capture(CancellationToken.None);
        Assert.Equal(@"HKLM\SOFTWARE\DSO\N", snapshot.CreatedKeyRoot);
        operation.Apply(CancellationToken.None);
        operation.Restore(snapshot, CancellationToken.None);
        Assert.False(_registry.HasKey(@"HKLM\SOFTWARE\DSO\N"));
        Assert.Equal(new[] { @"HKLM\SOFTWARE\DSO\N\K", @"HKLM\SOFTWARE\DSO\N" }, _registry.DeletedKeys);
    }

    [Fact]
    public void Restore_KeyExistedBefore_IsNotDeleted()
    {
        _registry.CreateKey(Path);
        var operation = Operation();
        var snapshot = operation.Capture(CancellationToken.None);
        operation.Apply(CancellationToken.None);
        operation.Restore(snapshot, CancellationToken.None);
        Assert.True(_registry.HasKey(Path));
        Assert.Empty(_registry.DeletedKeys);
    }

    [Fact]
    public void Restore_SiblingValueInKey_StopsWithoutError()
    {
        _registry.CreateKey(@"HKLM\SOFTWARE\DSO");
        var operation = Operation();
        var snapshot = operation.Capture(CancellationToken.None);
        operation.Apply(CancellationToken.None);
        _registry.Set(Path, "Other", RegValue.FromDWord(9));
        operation.Restore(snapshot, CancellationToken.None);
        Assert.True(_registry.HasKey(Path));
        Assert.Equal(RegValue.Absent, _registry.Get(Path, "A"));
    }

    [Theory]
    [InlineData(@"HKLM\")]
    [InlineData(@"HKLM")]
    [InlineData(@"HKLM\SOFTWARE")]
    public void Restore_CreatedKeyRootAboveHiveRoot_StopsAtHiveRoot(string root)
    {
        _registry.CreateKey(Path);
        var snapshot = SnapshotRecord.ForRegistry(Path, "A", RegValue.Absent, root);
        Operation().Restore(snapshot, CancellationToken.None);
        Assert.Empty(_registry.DeletedKeys);
        Assert.True(_registry.HasKey(Path));
        Assert.True(_registry.HasKey(@"HKLM\SOFTWARE"));
    }

    [Fact]
    public void Restore_KeyDeleteDenied_DoesNotThrowAndValueIsRestored()
    {
        _registry.CreateKey(@"HKLM\SOFTWARE\DSO");
        var operation = Operation();
        var snapshot = operation.Capture(CancellationToken.None);
        operation.Apply(CancellationToken.None);
        _registry.DenyKey(Path);
        operation.Restore(snapshot, CancellationToken.None);
        Assert.Equal(RegValue.Absent, _registry.Get(Path, "A"));
        Assert.True(_registry.HasKey(Path));
    }

    [Fact]
    public void Restore_CreatedKeyRootNotAncestor_DeletesNothing()
    {
        _registry.CreateKey(Path);
        var snapshot = SnapshotRecord.ForRegistry(Path, "A", RegValue.Absent, @"HKLM\SOFTWARE\Inny\Klucz");
        Operation().Restore(snapshot, CancellationToken.None);
        Assert.Empty(_registry.DeletedKeys);
        Assert.True(_registry.HasKey(Path));
    }

    [Fact]
    public void Restore_CreatedKeyRootPrefixButNotSegmentAncestor_DeletesNothing()
    {
        _registry.CreateKey(Path);
        var snapshot = SnapshotRecord.ForRegistry(Path, "A", RegValue.Absent, Path.Substring(0, Path.Length - 2));
        Operation().Restore(snapshot, CancellationToken.None);
        Assert.Empty(_registry.DeletedKeys);
    }

    [Fact]
    public void Restore_ValueNotAbsent_DoesNotDeleteKeys()
    {
        _registry.CreateKey(Path);
        var snapshot = SnapshotRecord.ForRegistry(Path, "A", RegValue.FromDWord(3), Path);
        Operation().Restore(snapshot, CancellationToken.None);
        Assert.Empty(_registry.DeletedKeys);
    }

    [Fact]
    public void DefaultsSnapshot_HasNoCreatedKeyRoot()
        => Assert.Null(Operation(original: RegValue.Absent).DefaultsSnapshot()!.CreatedKeyRoot);

    [Fact]
    public void Plan_ShowsCurrentAndTarget()
    {
        var change = Operation().Plan(CancellationToken.None);
        Assert.Equal(Path + @"\A", change.Description);
        Assert.Equal("(brak)", change.Current);
        Assert.Equal("1 (0x00000001) [DWord]", change.Target);
    }

    [Fact]
    public void Flags_FollowEntry()
    {
        Assert.True(Operation(detectOnly: true).IsDetectOnly);
        Assert.True(Operation().ParticipatesInDetection);
        Assert.Equal(OperationKind.Registry, Operation().Kind);
    }

    // ---- wpis z "localized": true (tekst zapisywany w języku programu) ----

    private const string PolishLabel = "Przejmij na własność";
    private const string EnglishLabel = "Take ownership";

    private static IDisposable LabelTable() => L.UseTable(CatalogTranslationTests.DataTable((PolishLabel, EnglishLabel)));

    private RegistryValueOperation LocalizedOperation(bool localized = true)
        => new(new RegistryEntry { Path = Path, Name = "MUIVerb", Value = RegValue.FromString(PolishLabel), Original = RegValue.Absent, Localized = localized }, _registry);

    private OperationState DetectIn(UiLanguage language, bool localized = true)
    {
        using (LabelTable())
        using (L.Use(language))
        {
            return LocalizedOperation(localized).Detect(CancellationToken.None);
        }
    }

    [Theory]
    [InlineData(UiLanguage.English, EnglishLabel)]
    [InlineData(UiLanguage.Polish, PolishLabel)]
    public void Localized_Apply_WritesTextInCurrentLanguage(UiLanguage language, string expected)
    {
        using (LabelTable())
        using (L.Use(language))
        {
            LocalizedOperation().Apply(CancellationToken.None);
        }

        Assert.Equal(RegValue.FromString(expected), _registry.Get(Path, "MUIVerb"));
    }

    [Fact]
    public void Localized_Detect_MatchesPolishValueWhenProgramIsEnglish()
    {
        _registry.Set(Path, "MUIVerb", RegValue.FromString(PolishLabel));
        Assert.Equal(OperationState.Match, DetectIn(UiLanguage.English));
    }

    [Fact]
    public void Localized_Detect_MatchesEnglishValueWhenProgramIsPolish()
    {
        _registry.Set(Path, "MUIVerb", RegValue.FromString(EnglishLabel));
        Assert.Equal(OperationState.Match, DetectIn(UiLanguage.Polish));
    }

    [Theory]
    [InlineData("Take Ownership!")]
    [InlineData("take ownership")]
    [InlineData("")]
    public void Localized_Detect_OtherString_IsMismatch(string text)
    {
        _registry.Set(Path, "MUIVerb", RegValue.FromString(text));
        Assert.Equal(OperationState.Mismatch, DetectIn(UiLanguage.English));
        Assert.Equal(OperationState.Mismatch, DetectIn(UiLanguage.Polish));
    }

    [Fact]
    public void Localized_Detect_MissingValue_IsMismatch()
        => Assert.Equal(OperationState.Mismatch, DetectIn(UiLanguage.English));

    [Fact]
    public void Localized_Detect_DwordWithSameNumber_IsMismatch()
    {
        // Wartość innego typu niż String nie jest naszym wpisem, nawet z tą samą treścią (ExpandString) albo liczbą (DWORD).
        _registry.Set(Path, "MUIVerb", RegValue.FromExpandString(EnglishLabel));
        Assert.Equal(OperationState.Mismatch, DetectIn(UiLanguage.English));
        _registry.Set(Path, "MUIVerb", RegValue.FromDWord(1));
        Assert.Equal(OperationState.Mismatch, DetectIn(UiLanguage.English));
    }

    [Fact]
    public void Localized_Detect_AccessDenied_IsUnknown()
    {
        _registry.DenyRead(Path, "MUIVerb");
        Assert.Equal(OperationState.Unknown, DetectIn(UiLanguage.English));
    }

    [Theory]
    [InlineData(UiLanguage.English, EnglishLabel)]
    [InlineData(UiLanguage.Polish, PolishLabel)]
    public void Localized_Preview_TargetIsCurrentLanguage(UiLanguage language, string expected)
    {
        PlannedChange change;
        using (LabelTable())
        using (L.Use(language))
        {
            change = LocalizedOperation().Plan(CancellationToken.None);
        }

        Assert.Equal(RegValue.FromString(expected).ToDisplayString(), change.Target);
        Assert.Equal(Path + @"\MUIVerb", change.Description);
    }

    [Fact]
    public void Localized_Restore_UsesSnapshotNotLanguage()
    {
        SnapshotRecord snapshot;
        using (LabelTable())
        {
            using (L.Use(UiLanguage.Polish))
            {
                var operation = LocalizedOperation();
                snapshot = operation.Capture(CancellationToken.None);
                operation.Apply(CancellationToken.None);
            }

            Assert.Equal(RegValue.Absent, snapshot.Value);
            using (L.Use(UiLanguage.English))
            {
                Assert.Equal(OperationState.Match, LocalizedOperation().Detect(CancellationToken.None));
                LocalizedOperation().Restore(snapshot, CancellationToken.None);
            }
        }

        Assert.Equal(RegValue.Absent, _registry.Get(Path, "MUIVerb"));
    }

    [Fact]
    public void Localized_Restore_SnapshotWithForeignText_WritesItBack()
    {
        _registry.Set(Path, "MUIVerb", RegValue.FromString("Cudzy napis"));
        using (LabelTable())
        using (L.Use(UiLanguage.English))
        {
            var operation = LocalizedOperation();
            var snapshot = operation.Capture(CancellationToken.None);
            operation.Apply(CancellationToken.None);
            Assert.Equal(RegValue.FromString(EnglishLabel), _registry.Get(Path, "MUIVerb"));
            operation.Restore(snapshot, CancellationToken.None);
        }

        Assert.Equal(RegValue.FromString("Cudzy napis"), _registry.Get(Path, "MUIVerb"));
    }

    [Fact]
    public void NotLocalized_BehavesAsBefore()
    {
        // Bez "localized" tabela tłumaczeń nie ma wpływu: zapis i podgląd to dosłowna wartość z katalogu, detekcja tylko dokładna.
        using (LabelTable())
        using (L.Use(UiLanguage.English))
        {
            var operation = LocalizedOperation(localized: false);
            Assert.Equal(RegValue.FromString(PolishLabel).ToDisplayString(), operation.Plan(CancellationToken.None).Target);
            operation.Apply(CancellationToken.None);
        }

        Assert.Equal(RegValue.FromString(PolishLabel), _registry.Get(Path, "MUIVerb"));
        Assert.Equal(OperationState.Match, DetectIn(UiLanguage.English, localized: false));
        _registry.Set(Path, "MUIVerb", RegValue.FromString(EnglishLabel));
        Assert.Equal(OperationState.Mismatch, DetectIn(UiLanguage.English, localized: false));
        Assert.Equal(OperationState.Mismatch, DetectIn(UiLanguage.Polish, localized: false));
    }
}
