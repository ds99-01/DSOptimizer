using System;
using System.Threading;
using System.Threading.Tasks;
using DSOptimizer.Core.Gui;
using Xunit;

namespace DSOptimizer.Tests.Unit.Gui;

public class CrossThreadQuestionTests
{
    private static readonly TimeSpan Short = TimeSpan.FromMilliseconds(100);

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void Ask_PostedAndAnswered_ReturnsAnswer(bool answer)
        => Assert.Equal(answer, CrossThreadQuestion.Ask(work => work(), () => Task.FromResult(answer), TimeSpan.FromSeconds(5), CancellationToken.None));

    [Fact]
    public async Task Ask_AnswerLater_FromOtherThread()
    {
        var pending = new TaskCompletionSource<bool>();
        var asking = Task.Run(() => CrossThreadQuestion.Ask(work => work(), () => pending.Task, TimeSpan.FromSeconds(30), CancellationToken.None));
        pending.SetResult(true);

        Assert.True(await asking);
    }

    [Fact]
    public void Ask_PostNeverRuns_TimesOutWithNo()
        => Assert.False(CrossThreadQuestion.Ask(_ => { }, () => Task.FromResult(true), Short, CancellationToken.None));

    [Fact]
    public void Ask_NeverAnswered_TimesOutWithNo()
        => Assert.False(CrossThreadQuestion.Ask(work => work(), () => new TaskCompletionSource<bool>().Task, Short, CancellationToken.None));

    [Fact]
    public void Ask_Cancelled_ReturnsNo()
    {
        using var cancel = new CancellationTokenSource();
        cancel.Cancel();

        Assert.False(CrossThreadQuestion.Ask(_ => { }, () => Task.FromResult(true), TimeSpan.FromMinutes(10), cancel.Token));
    }

    [Fact]
    public void Ask_QuestionThrowsOrFaults_ReturnsNo()
    {
        Assert.False(CrossThreadQuestion.Ask(work => work(), () => throw new InvalidOperationException("okno"), TimeSpan.FromSeconds(5), CancellationToken.None));
        Assert.False(CrossThreadQuestion.Ask(work => work(), () => Task.FromException<bool>(new InvalidOperationException("okno")), TimeSpan.FromSeconds(5), CancellationToken.None));
    }

    [Fact]
    public void Ask_PostThrows_ReturnsNo()
        => Assert.False(CrossThreadQuestion.Ask(_ => throw new InvalidOperationException("dispatcher zamknięty"), () => Task.FromResult(true), TimeSpan.FromSeconds(5), CancellationToken.None));

    [Fact]
    public void DefaultTimeout_IsTenMinutes()
        => Assert.Equal(TimeSpan.FromMinutes(10), CrossThreadQuestion.DefaultTimeout);
}
