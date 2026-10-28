using DSOptimizer.App.ViewModels;
using DSOptimizer.Core.Localization;
using Xunit;

namespace DSOptimizer.Tests.Windows;

/// <summary>Okno wyboru języka przy pierwszym uruchomieniu: wykryty język jako propozycja, klik kafla = odpowiedź i zamknięcie.</summary>
[Trait("Category", "Windows")]
public class LanguageChoiceViewModelTests
{
    [Theory]
    [InlineData(UiLanguage.Polish, true, false)]
    [InlineData(UiLanguage.English, false, true)]
    public void Initial_DetectedIsPreselected(UiLanguage detected, bool polish, bool english)
    {
        var viewModel = new LanguageChoiceViewModel(detected);

        Assert.Equal(detected, viewModel.Detected);
        Assert.Equal(polish, viewModel.IsPolishDetected);
        Assert.Equal(english, viewModel.IsEnglishDetected);
        Assert.Null(viewModel.Answer);
    }

    [Fact]
    public void ChoosePolish_SetsAnswer()
    {
        var viewModel = new LanguageChoiceViewModel(UiLanguage.English);
        var closeRequests = 0;
        viewModel.CloseRequested += (_, _) => closeRequests++;

        viewModel.ChoosePolishCommand.Execute(null);

        Assert.Equal(UiLanguage.Polish, viewModel.Answer);
        Assert.Equal(1, closeRequests);
    }

    [Fact]
    public void ChooseEnglish_SetsAnswer()
    {
        var viewModel = new LanguageChoiceViewModel(UiLanguage.Polish);
        var closeRequests = 0;
        viewModel.CloseRequested += (_, _) => closeRequests++;

        viewModel.ChooseEnglishCommand.Execute(null);

        Assert.Equal(UiLanguage.English, viewModel.Answer);
        Assert.Equal(1, closeRequests);
    }

    [Fact]
    public void NoChoice_AnswerNull()
    {
        var viewModel = new LanguageChoiceViewModel(UiLanguage.English);

        Assert.True(viewModel.ChoosePolishCommand.CanExecute(null));
        Assert.True(viewModel.ChooseEnglishCommand.CanExecute(null));
        Assert.Null(viewModel.Answer);
    }
}
