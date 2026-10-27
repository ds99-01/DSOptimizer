using System;
using System.Linq;
using DSOptimizer.Core.Abstractions;
using DSOptimizer.Core.Catalog;
using DSOptimizer.Core.Engine;
using DSOptimizer.Core.Gui;
using DSOptimizer.Core.Model;
using DSOptimizer.Tests.Unit.Engine;
using DSOptimizer.Tests.Unit.Fakes;
using Xunit;

namespace DSOptimizer.Tests.Unit.Gui;

public class SeriesConfirmationTests
{
    private static GuiSession Session(EngineFixture f, params TweakDefinition[] definitions)
        => new(new LoadedCatalog(definitions, "sha256:test"), f.CreateServices());

    private static TweakDefinition Caution(string id = "test.caution")
        => TestTweaks.TwoValues(id) with { Risk = RiskLevel.Caution, Warnings = new[] { "Uwaga 1" }, Description = "Opis", Summary = "Podsumowanie" };

    private static TweakDefinition Safe(string id) => TestTweaks.TwoValues(id);

    [Fact]
    public void SeriesRequiresConfirmation_SingleSuggestedSafe_True()
    {
        Assert.True(GuiRules.SeriesRequiresConfirmation(new[] { Safe("a") with { Suggested = true } }));
        Assert.False(GuiRules.SeriesRequiresConfirmation(new[] { Safe("a") with { Suggested = false } }));
    }

    [Fact]
    public void ConfirmationForItem_SuggestedSafe_ReturnsWindow_NonSuggestedNull()
    {
        var session = Session(new EngineFixture(), Safe("a") with { Suggested = true }, Safe("b"));

        Assert.NotNull(session.ConfirmationForItem("a", GuiOperation.Apply, "jan"));
        Assert.Null(session.ConfirmationForItem("b", GuiOperation.Apply, "jan"));
    }

    [Fact]
    public void SeriesRequiresConfirmation_Table()
    {
        Assert.False(GuiRules.SeriesRequiresConfirmation(new[] { Safe("a") }));
        Assert.True(GuiRules.SeriesRequiresConfirmation(new[] { Caution() }));
        Assert.True(GuiRules.SeriesRequiresConfirmation(new[] { TestTweaks.Action() }));
        Assert.True(GuiRules.SeriesRequiresConfirmation(new[] { Safe("a"), Safe("b") }));
    }

    [Fact]
    public void ConfirmationForSeries_TwoSafe_ReturnsWindow()
    {
        var session = Session(new EngineFixture(), Safe("a"), Safe("b"));

        var window = session.ConfirmationForSeries(new[] { "a", "b" }, GuiOperation.Apply, "jan")!;

        Assert.NotNull(window);
        Assert.Equal("Zastosuj zaznaczone", window.Title);
        Assert.Equal("2 pozycje", window.Subtitle);
        Assert.Equal("Zastosuj (2)", window.PrimaryText);
        Assert.Equal(new[] { "a", "b" }, window.Items.Select(item => item.Id));
        Assert.Equal(GuiOperation.Apply, window.Operation);
    }

    [Fact]
    public void ConfirmationForSeries_ItemsFromTwoCategories_ListsBothByTitle()
    {
        var privacy = Safe("p.one") with { Category = "privacy", Title = "Pozycja prywatności" };
        var ux = Safe("u.one") with { Category = "ux", Title = "Pozycja Explorera" };
        var session = Session(new EngineFixture(), privacy, ux);

        var window = session.ConfirmationForSeries(new[] { "p.one", "u.one" }, GuiOperation.Apply, null)!;

        Assert.Equal(new[] { "Pozycja prywatności", "Pozycja Explorera" }, window.Items.Select(item => item.Title));
        Assert.Equal("Zastosuj (2)", window.PrimaryText);
    }

    [Fact]
    public void ConfirmationForSeries_OneSafe_ReturnsNull()
    {
        var session = Session(new EngineFixture(), Safe("a"));
        Assert.Null(session.ConfirmationForSeries(new[] { "a" }, GuiOperation.Apply, null));
    }

    [Fact]
    public void ConfirmationForSeries_OneCaution_ReturnsWindow()
    {
        var session = Session(new EngineFixture(), Caution());

        var window = session.ConfirmationForSeries(new[] { "test.caution" }, GuiOperation.Undo, null)!;

        Assert.Equal("Cofnij zaznaczone", window.Title);
        Assert.Equal("1 pozycja", window.Subtitle);
        Assert.Equal("Cofnij (1)", window.PrimaryText);
        var item = Assert.Single(window.Items);
        Assert.Equal(RiskLevel.Caution, item.Risk);
        Assert.Equal("Podsumowanie", item.Summary);
        Assert.Equal(new[] { "Uwaga 1" }, item.Warnings);
    }

    [Fact]
    public void ConfirmationForSeries_InvalidInput_Throws()
    {
        var session = Session(new EngineFixture(), Safe("a"), TestTweaks.Action());
        Assert.Throws<ArgumentException>(() => session.ConfirmationForSeries(new[] { "a" }, GuiOperation.Execute, null));
        Assert.Throws<ArgumentException>(() => session.ConfirmationForSeries(new[] { "nie.ma" }, GuiOperation.Apply, null));
        Assert.Throws<ArgumentException>(() => session.ConfirmationForSeries(new[] { "test.action" }, GuiOperation.Apply, null));
    }

    [Theory]
    [InlineData(true, 0, "Program spróbuje go utworzyć przed pierwszą zmianą", "Niepotrzebny")]
    [InlineData(false, 1, "Nie – wyłączony w Ustawieniach", "Wymagany dla 1 pozycji")]
    [InlineData(true, 3, "Program spróbuje go utworzyć przed pierwszą zmianą", "Wymagany dla 3 pozycji")]
    public void Confirmation_Facts_Apply(bool restorePoint, int reboots, string restoreText, string rebootText)
    {
        var tweaks = Enumerable.Range(0, 3).Select(i => Safe("t" + i) with { RequiresReboot = i < reboots }).ToArray();
        var session = Session(new EngineFixture(), tweaks);
        session.CreateRestorePoint = restorePoint;

        var window = session.ConfirmationForSeries(new[] { "t0", "t1", "t2" }, GuiOperation.Apply, null)!;

        Assert.Equal(restorePoint, window.CreatesRestorePoint);
        Assert.Equal(
            new[]
            {
                new SeriesFact("Punkt przywracania", restoreText),
                new SeriesFact("Restart", rebootText),
                new SeriesFact("Cofanie", "Możliwe dla wszystkich, z historii zmian"),
            },
            window.Facts);
    }

    [Fact]
    public void Confirmation_Facts_Apply_IrreversibleListed()
    {
        var session = Session(
            new EngineFixture(),
            Safe("a") with { Title = "Pierwsza", Reversible = false },
            Safe("b"),
            Safe("c") with { Title = "Trzecia", Reversible = false });

        var window = session.ConfirmationForSeries(new[] { "a", "b", "c" }, GuiOperation.Apply, null)!;

        Assert.Equal(new SeriesFact("Cofanie", "Niemożliwe dla: Pierwsza; Trzecia"), window.Facts[^1]);
    }

    [Fact]
    public void Confirmation_Facts_Undo()
    {
        var session = Session(new EngineFixture(), Safe("a") with { RequiresReboot = true }, Safe("b"));

        var window = session.ConfirmationForSeries(new[] { "a", "b" }, GuiOperation.Undo, null)!;

        Assert.Equal(
            new[]
            {
                new SeriesFact("Punkt przywracania", "Nie – cofanie go nie tworzy"),
                new SeriesFact("Restart", "Wymagany dla 1 pozycji"),
            },
            window.Facts);
        Assert.False(window.CreatesRestorePoint);
    }

    [Fact]
    public void ConfirmationForItem_Action_FactsAndPrimary()
    {
        var session = Session(new EngineFixture(), TestTweaks.Action());

        var window = session.ConfirmationForItem("test.action", GuiOperation.Execute, null)!;

        Assert.Equal("Uruchom", window.Title);
        Assert.Equal("Akcja", window.Subtitle);
        Assert.Equal("Uruchom", window.PrimaryText);
        Assert.Equal(new[] { new SeriesFact("Cofanie", "Niemożliwe – akcji nie da się cofnąć") }, window.Facts);
        Assert.False(window.CreatesRestorePoint);
        var technical = Assert.Single(Assert.Single(window.Items).Technical);
        Assert.Equal(new SeriesFact("Polecenie", "Skrypt PowerShell"), technical);
    }

    [Fact]
    public void ConfirmationForItem_SafeTweak_IsNull()
    {
        var session = Session(new EngineFixture(), Safe("a"));
        Assert.Null(session.ConfirmationForItem("a", GuiOperation.Apply, null));
    }

    [Fact]
    public void ConfirmationForItem_Caution_UsesOperationNameAndTitle()
    {
        var session = Session(new EngineFixture(), Caution());

        var window = session.ConfirmationForItem("test.caution", GuiOperation.Apply, null)!;

        Assert.Equal("Zastosuj", window.Title);
        Assert.Equal("Dwie wartości", window.Subtitle);
        Assert.Equal("Zastosuj", window.PrimaryText);
    }

    [Fact]
    public void ConfirmationForItem_InvalidInput_Throws()
    {
        var session = Session(new EngineFixture(), Caution(), TestTweaks.Action());
        Assert.Throws<ArgumentException>(() => session.ConfirmationForItem("nie.ma", GuiOperation.Apply, null));
        Assert.Throws<ArgumentException>(() => session.ConfirmationForItem("test.caution", GuiOperation.Execute, null));
        Assert.Throws<ArgumentException>(() => session.ConfirmationForItem("test.action", GuiOperation.Apply, null));
    }

    [Fact]
    public void Confirmation_UserScope_KnownName()
    {
        var session = Session(new EngineFixture(), TestTweaks.UserMenuDelay() with { RequiresReboot = true }, Safe("b"));

        var window = session.ConfirmationForSeries(new[] { "test.menu-delay", "b" }, GuiOperation.Apply, "jan")!;

        Assert.Equal(new[] { "Dotyczy konta jan.", "Zadziała po ponownym uruchomieniu." }, window.Items[0].Notes);
        Assert.Empty(window.Items[1].Notes);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("  ")]
    public void Confirmation_UserScope_UnknownName(string? userName)
    {
        var session = Session(new EngineFixture(), TestTweaks.UserMenuDelay(), Safe("b"));

        var window = session.ConfirmationForSeries(new[] { "test.menu-delay", "b" }, GuiOperation.Apply, userName)!;

        Assert.Equal(new[] { "Dotyczy bieżącego konta." }, window.Items[0].Notes);
    }

    [Fact]
    public void Confirmation_Technical_FromPreview()
    {
        var f = new EngineFixture();
        f.Registry.Set(TestTweaks.Path, "Delay", RegValue.FromString("400"));
        var tweak = TestTweaks.TwoValues("t.delay") with
        {
            Registry = new[]
            {
                new RegistryEntry { Path = TestTweaks.Path, Name = "Delay", Value = RegValue.FromString("10"), Original = RegValue.FromString("400") },
            },
        };
        var preview = f.CreateEngine().Preview(tweak).Single();
        var session = Session(f, tweak, Safe("b"));

        var apply = session.ConfirmationForSeries(new[] { "t.delay", "b" }, GuiOperation.Apply, null)!.Items[0].Technical;
        var undo = session.ConfirmationForSeries(new[] { "t.delay", "b" }, GuiOperation.Undo, null)!.Items[0].Technical;

        Assert.Equal(
            new[]
            {
                new SeriesFact("Gdzie", preview.Description),
                new SeriesFact("Zmiana", preview.Current + " na " + preview.Target),
            },
            apply);
        Assert.Contains("400", apply[1].Value, StringComparison.Ordinal);
        Assert.Contains("10", apply[1].Value, StringComparison.Ordinal);
        Assert.Equal(
            new[]
            {
                new SeriesFact("Gdzie", preview.Description),
                new SeriesFact("Zmiana", "przywrócenie stanu sprzed zmiany z historii"),
            },
            undo);
    }

    [Fact]
    public void Confirmation_PreviewThrows_ShowsUnavailable()
    {
        var f = new EngineFixture();
        var services = new EngineServices
        {
            Registry = new ThrowingRegistry(),
            Services = f.Services,
            Scripts = f.Scripts,
            Launcher = f.Launcher,
            Journal = f.Journal,
            RestorePoints = f.RestorePoints,
            SystemInfo = f.SystemInfo,
            User = f.User,
            Log = f.Log,
            Clock = f.Clock,
        };
        var session = new GuiSession(new LoadedCatalog(new[] { Safe("a"), Safe("b") }, "sha256:test"), services);

        var window = session.ConfirmationForSeries(new[] { "a", "b" }, GuiOperation.Apply, null)!;

        var technical = Assert.Single(window.Items[0].Technical);
        Assert.Equal(new SeriesFact("Szczegóły", "niedostępne: rejestr padł"), technical);
        Assert.Contains(f.Log.Lines, line => line.StartsWith("WARN [Gui]", StringComparison.Ordinal) && line.EndsWith("rejestr padł", StringComparison.Ordinal));
    }

    private sealed class ThrowingRegistry : IRegistry
    {
        public RegistryReadResult Read(string path, string name) => throw new InvalidOperationException("rejestr padł");

        public void Write(string path, string name, RegValue value) => throw new InvalidOperationException("rejestr padł");

        public ReadStatus KeyStatus(string path) => throw new InvalidOperationException("rejestr padł");

        public System.Collections.Generic.IReadOnlyList<string> SubKeyNames(string path) => throw new InvalidOperationException("rejestr padł");

        public bool DeleteKeyIfEmpty(string path) => throw new InvalidOperationException("rejestr padł");

        public void DeleteKeyTree(string path) => throw new InvalidOperationException("rejestr padł");
    }
}
