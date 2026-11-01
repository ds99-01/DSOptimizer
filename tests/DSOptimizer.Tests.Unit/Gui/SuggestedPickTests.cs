using System.Linq;
using DSOptimizer.Core.Apps;
using DSOptimizer.Core.Gui;
using DSOptimizer.Core.Model;
using Xunit;

namespace DSOptimizer.Tests.Unit.Gui;

public class SuggestedPickTests
{
    private static readonly InstalledApp Program = new("win32:x", AppKind.Program, "X", null, null, AppScope.Machine, null, null,
        new Win32Info(@"HKLM\X", "\"C:\\x.exe\"", null, false, null, null, false));

    private static readonly UninstallPlan Plan = new(@"C:\x.exe", string.Empty, UninstallMode.Quiet);

    private static SuggestionCandidate C(string id, bool suggested, DetectionState? state) => new(id, suggested, state);

    private static AppRow Row(string id, AppGroup group, AppRemovability removability, UninstallPlan? plan)
        => new(Program with { Id = id }, group, null, null, null, null, removability, null, plan);

    [Fact]
    public void Settings_TakesOnlySuggestedNotAppliedOrPartial()
        => Assert.Equal(
            new[] { "n", "p" },
            SuggestedPick.Settings(new[]
            {
                C("n", true, DetectionState.NotApplied),
                C("p", true, DetectionState.Partial),
                C("a", true, DetectionState.Applied),
                C("u", true, DetectionState.Unknown),
                C("x", true, DetectionState.NotSupported),
                C("q", true, null),
                C("o", false, DetectionState.NotApplied),
            }));

    [Fact]
    public void Settings_EmptyInput_ReturnsEmpty()
        => Assert.Empty(SuggestedPick.Settings(System.Array.Empty<SuggestionCandidate>()));

    [Fact]
    public void Apps_TakesOnlySuggestedGroupThatCanBeRemoved()
    {
        var rows = new[]
        {
            Row("s", AppGroup.Suggested, AppRemovability.Removable, Plan),
            Row("other", AppGroup.Other, AppRemovability.Removable, Plan),
            Row("protected", AppGroup.Protected, AppRemovability.Removable, Plan),
            Row("account", AppGroup.Suggested, AppRemovability.OtherAccount, Plan),
            Row("uncertain", AppGroup.Suggested, AppRemovability.UserUncertain, Plan),
            Row("nocmd", AppGroup.Suggested, AppRemovability.NoSafeCommand, Plan),
            Row("noplan", AppGroup.Suggested, AppRemovability.Removable, null),
        };

        Assert.Equal(new[] { "s" }, SuggestedPick.Apps(rows));
    }

    [Fact]
    public void Apps_EmptyInput_ReturnsEmpty()
        => Assert.Empty(SuggestedPick.Apps(System.Array.Empty<AppRow>()));
}