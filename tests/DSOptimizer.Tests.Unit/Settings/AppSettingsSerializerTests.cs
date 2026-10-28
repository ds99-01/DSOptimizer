using DSOptimizer.Core.Localization;
using DSOptimizer.Core.Settings;
using Xunit;

namespace DSOptimizer.Tests.Unit.Settings;

public class AppSettingsSerializerTests
{
    [Fact]
    public void Serialize_Default_IsStableJson()
    {
        Assert.Equal(
            "{\"schemaVersion\":1,\"createRestorePoint\":true,\"theme\":\"system\"}",
            AppSettingsSerializer.Serialize(AppSettings.Default));
    }

    [Theory]
    [InlineData(true, ThemePreference.System)]
    [InlineData(true, ThemePreference.Light)]
    [InlineData(true, ThemePreference.Dark)]
    [InlineData(false, ThemePreference.System)]
    [InlineData(false, ThemePreference.Light)]
    [InlineData(false, ThemePreference.Dark)]
    public void RoundTrip_AllCombinations(bool createRestorePoint, ThemePreference theme)
    {
        var settings = new AppSettings { CreateRestorePoint = createRestorePoint, Theme = theme };

        var restored = AppSettingsSerializer.Deserialize(AppSettingsSerializer.Serialize(settings));

        Assert.Equal(settings, restored);
    }

    [Fact]
    public void Deserialize_MissingFields_UseDefaults()
    {
        Assert.Equal(AppSettings.Default, AppSettingsSerializer.Deserialize("{\"schemaVersion\":1}"));
        Assert.Equal(AppSettings.Default, AppSettingsSerializer.Deserialize("{\"schemaVersion\":1,\"nieznane\":{\"a\":1}}"));
    }

    [Fact]
    public void Deserialize_ThemeIsCaseInsensitive()
    {
        var settings = AppSettingsSerializer.Deserialize("{\"schemaVersion\":1,\"theme\":\"Dark\"}");

        Assert.Equal(ThemePreference.Dark, settings.Theme);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("null")]
    [InlineData("[]")]
    [InlineData("{")]
    [InlineData("﻿{\"schemaVersion\":1}")]
    [InlineData("{}")]
    [InlineData("{\"schemaVersion\":2}")]
    [InlineData("{\"schemaVersion\":\"1\"}")]
    [InlineData("{\"schemaVersion\":99999999999999999999}")]
    [InlineData("{\"schemaVersion\":-99999999999999999999}")]
    [InlineData("{\"schemaVersion\":1,\"createRestorePoint\":\"tak\"}")]
    [InlineData("{\"schemaVersion\":1,\"theme\":\"fioletowy\"}")]
    [InlineData("{\"schemaVersion\":1,\"theme\":5}")]
    public void Deserialize_Invalid_Throws(string json)
    {
        var ex = Assert.Throws<SettingsFormatException>(() => AppSettingsSerializer.Deserialize(json));

        Assert.False(string.IsNullOrWhiteSpace(ex.Message));
    }

    [Fact]
    public void Deserialize_NewerSchema_MentionsNewerVersion()
    {
        var ex = Assert.Throws<SettingsFormatException>(() => AppSettingsSerializer.Deserialize("{\"schemaVersion\":2}"));

        Assert.Contains("nowsza wersja", ex.Message);
    }

    [Fact]
    public void Serialize_NullLanguage_OmitsField()
    {
        var json = AppSettingsSerializer.Serialize(new AppSettings { Language = null });

        Assert.DoesNotContain("language", json, System.StringComparison.Ordinal);
        Assert.Equal("{\"schemaVersion\":1,\"createRestorePoint\":true,\"theme\":\"system\"}", json);
    }

    [Fact]
    public void Serialize_English_WritesEn()
    {
        var json = AppSettingsSerializer.Serialize(new AppSettings { Language = UiLanguage.English });

        Assert.Equal("{\"schemaVersion\":1,\"createRestorePoint\":true,\"theme\":\"system\",\"language\":\"en\"}", json);
    }

    [Fact]
    public void RoundTrip_Polish()
    {
        var settings = new AppSettings { Language = UiLanguage.Polish };

        Assert.Equal(settings, AppSettingsSerializer.Deserialize(AppSettingsSerializer.Serialize(settings)));
    }

    [Fact]
    public void Deserialize_MissingLanguage_IsNull()
    {
        var settings = AppSettingsSerializer.Deserialize("{\"schemaVersion\":1}");

        Assert.Null(settings.Language);
    }

    [Fact]
    public void Deserialize_UnknownLanguage_IsNotChosenAndKeepsOtherFields()
    {
        var settings = AppSettingsSerializer.Deserialize(
            "{\"schemaVersion\":1,\"createRestorePoint\":false,\"theme\":\"dark\",\"language\":\"de\"}");

        Assert.Null(settings.Language);
        Assert.False(settings.CreateRestorePoint);
        Assert.Equal(ThemePreference.Dark, settings.Theme);
    }

    [Fact]
    public void Deserialize_LanguageNumber_Throws()
    {
        Assert.Throws<SettingsFormatException>(() => AppSettingsSerializer.Deserialize("{\"schemaVersion\":1,\"language\":5}"));
    }

    [Fact]
    public void Deserialize_LanguageUpperCase_IsNotChosen()
    {
        Assert.Null(AppSettingsSerializer.Deserialize("{\"schemaVersion\":1,\"language\":\"EN\"}").Language);
    }
}
