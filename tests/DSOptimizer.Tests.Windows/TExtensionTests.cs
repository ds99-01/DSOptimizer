using DSOptimizer.App.Gui;
using DSOptimizer.Core.Localization;
using Xunit;

namespace DSOptimizer.Tests.Windows;

[Trait("Category", "Windows")]
public class TExtensionTests
{
    private static TranslationTable Table() =>
        TranslationTable.Load(new[]
        {
            new TranslationSource("t.json", "{\"language\":\"en\",\"entries\":[{\"pl\":\"Ustawienia\",\"en\":\"Settings\"}]}"),
        });

    [Fact]
    public void TExtension_ReturnsTranslationInEnglish()
    {
        using (L.UseTable(Table()))
        using (L.Use(UiLanguage.English))
        {
            Assert.Equal("Settings", new TExtension("Ustawienia").ProvideValue(null!));
        }
    }

    [Fact]
    public void TExtension_ReturnsPolishByDefault()
    {
        using (L.UseTable(Table()))
        {
            Assert.Equal("Ustawienia", new TExtension("Ustawienia").ProvideValue(null!));
        }
    }
}
