using System;
using System.Globalization;
using DSOptimizer.Core.Localization;
using Xunit;

namespace DSOptimizer.Tests.Unit.Localization;

public class LanguageDetectorTests
{
    [Fact]
    public void Detect_PolishCulture_ReturnsPolish()
    {
        Assert.Equal(UiLanguage.Polish, LanguageDetector.Detect(new CultureInfo("pl-PL")));
    }

    [Theory]
    [InlineData("en-US")]
    [InlineData("en-GB")]
    [InlineData("de-DE")]
    [InlineData("uk-UA")]
    [InlineData("")]
    public void Detect_OtherCulture_ReturnsEnglish(string name)
    {
        var culture = name.Length == 0 ? CultureInfo.InvariantCulture : new CultureInfo(name);
        Assert.Equal(UiLanguage.English, LanguageDetector.Detect(culture));
    }

    [Fact]
    public void Detect_Null_Throws()
    {
        Assert.Throws<ArgumentNullException>(() => LanguageDetector.Detect(null!));
    }

    [Fact]
    public void ToCode_And_TryParse_RoundTrip()
    {
        Assert.Equal("pl", UiLanguageCodes.ToCode(UiLanguage.Polish));
        Assert.Equal("en", UiLanguageCodes.ToCode(UiLanguage.English));
        Assert.True(UiLanguageCodes.TryParse("pl", out var pl));
        Assert.Equal(UiLanguage.Polish, pl);
        Assert.True(UiLanguageCodes.TryParse("en", out var en));
        Assert.Equal(UiLanguage.English, en);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("EN")]
    [InlineData(" en")]
    [InlineData("en ")]
    [InlineData("de")]
    public void TryParse_Invalid_ReturnsFalse(string? code)
    {
        Assert.False(UiLanguageCodes.TryParse(code, out _));
    }
}
