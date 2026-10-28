using System.Collections.Generic;
using DSOptimizer.Core.Abstractions;
using DSOptimizer.Core.Localization;
using DSOptimizer.Core.Settings;
using Xunit;

namespace DSOptimizer.Tests.Unit.Localization;

public class StartupLanguageTests
{
    private sealed class FakeStore : ISettingsStore
    {
        public List<AppSettings> Saved { get; } = new();

        public string? FailWith { get; init; }

        public SettingsLoadResult Load() => new(AppSettings.Default, null);

        public void Save(AppSettings settings)
        {
            if (FailWith is not null)
            {
                throw new SettingsStoreException(FailWith);
            }

            Saved.Add(settings);
        }
    }

    [Fact]
    public void Choose_SavedLanguage_DoesNotAskOrSave()
    {
        var store = new FakeStore();
        var asked = false;
        var loaded = AppSettings.Default with { Language = UiLanguage.English };

        var result = StartupLanguage.Choose(loaded, UiLanguage.Polish, _ => { asked = true; return UiLanguage.Polish; }, store);

        Assert.False(asked);
        Assert.Empty(store.Saved);
        Assert.Equal(UiLanguage.English, result.Language);
        Assert.Same(loaded, result.Settings);
        Assert.Null(result.Warning);
    }

    [Fact]
    public void Choose_NotChosen_AsksWithDetected()
    {
        UiLanguage? received = null;

        StartupLanguage.Choose(AppSettings.Default, UiLanguage.English, d => { received = d; return d; }, new FakeStore());

        Assert.Equal(UiLanguage.English, received);
    }

    [Fact]
    public void Choose_AnswerEnglish_SavesAndReturns()
    {
        var store = new FakeStore();

        var result = StartupLanguage.Choose(AppSettings.Default, UiLanguage.Polish, _ => UiLanguage.English, store);

        Assert.Equal(UiLanguage.English, result.Language);
        Assert.Equal(UiLanguage.English, result.Settings.Language);
        Assert.Equal(UiLanguage.English, Assert.Single(store.Saved).Language);
        Assert.Null(result.Warning);
    }

    [Fact]
    public void Choose_ClosedWithoutAnswer_UsesDetectedAndSaves()
    {
        var store = new FakeStore();

        var result = StartupLanguage.Choose(AppSettings.Default, UiLanguage.English, _ => null, store);

        Assert.Equal(UiLanguage.English, result.Language);
        Assert.Equal(UiLanguage.English, Assert.Single(store.Saved).Language);
    }

    [Fact]
    public void Choose_SaveFails_UsesAnswerAndWarns()
    {
        var store = new FakeStore { FailWith = "Brak dostępu." };

        var result = StartupLanguage.Choose(AppSettings.Default, UiLanguage.Polish, _ => UiLanguage.English, store);

        Assert.Equal(UiLanguage.English, result.Language);
        Assert.Equal(UiLanguage.English, result.Settings.Language);
        Assert.Equal(
            "Nie udało się zapisać wybranego języka: Brak dostępu. Okno wyboru pokaże się przy następnym uruchomieniu.",
            result.Warning);
    }

    [Fact]
    public void Choose_KeepsOtherSettings()
    {
        var store = new FakeStore();
        var loaded = AppSettings.Default with { CreateRestorePoint = false, Theme = ThemePreference.Dark };

        var result = StartupLanguage.Choose(loaded, UiLanguage.Polish, _ => UiLanguage.English, store);

        Assert.False(result.Settings.CreateRestorePoint);
        Assert.Equal(ThemePreference.Dark, result.Settings.Theme);
        Assert.False(Assert.Single(store.Saved).CreateRestorePoint);
    }
}
