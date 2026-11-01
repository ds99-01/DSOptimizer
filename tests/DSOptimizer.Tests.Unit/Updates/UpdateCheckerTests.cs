using System;
using System.Linq;
using DSOptimizer.Core.Abstractions;
using DSOptimizer.Core.Install;
using DSOptimizer.Core.Updates;
using DSOptimizer.Tests.Unit.Fakes;
using Xunit;

namespace DSOptimizer.Tests.Unit.Updates;

public class UpdateCheckerTests
{
    private static string Json(string tag, bool pre = false, bool draft = false)
        => "{\"tag_name\":\"" + tag + "\",\"prerelease\":" + (pre ? "true" : "false") + ",\"draft\":" + (draft ? "true" : "false") + "}";

    private static (UpdateCheckResult Result, FakeWebDownloader Web, ListLog Log) Run(WebText response, string current = "4.0.9")
    {
        var web = new FakeWebDownloader().EnqueueText(response);
        var log = new ListLog();
        return (new UpdateChecker(web, current, log).Check(), web, log);
    }

    [Fact]
    public void Check_NewerNumericVersion_IsNewer()
    {
        var (result, _, _) = Run(FakeWebDownloader.Ok(Json("v4.0.10")));

        Assert.Equal(UpdateCheckOutcome.NewerAvailable, result.Outcome);
        Assert.Equal("4.0.10", result.LatestVersion);
    }

    [Theory]
    [InlineData("v4.0.9")]
    [InlineData("v4.0.8")]
    public void Check_SameOrOlder_IsUpToDate(string tag)
    {
        var (result, _, _) = Run(FakeWebDownloader.Ok(Json(tag)));

        Assert.Equal(UpdateCheckOutcome.UpToDate, result.Outcome);
    }

    [Fact]
    public void Check_Skipped_IsUpToDate()
    {
        var (result, _, _) = Run(FakeWebDownloader.Ok(Json("v9.0.0", pre: true)));

        Assert.Equal(UpdateCheckOutcome.UpToDate, result.Outcome);
        Assert.Null(result.LatestVersion);
    }

    [Fact]
    public void Check_UsesExactUrlAcceptAndTimeout()
    {
        var (_, web, _) = Run(FakeWebDownloader.Ok(Json("v4.0.9")));

        var call = Assert.Single(web.TextCalls);
        Assert.Equal((UpdateSource.ReleasesLatestUrl, RustDeskSource.ApiAccept, TimeSpan.FromSeconds(15)), call);
    }

    public static TheoryData<WebText> Failures => new()
    {
        FakeWebDownloader.Failure(WebFailure.Network),
        FakeWebDownloader.Failure(WebFailure.TimedOut),
        FakeWebDownloader.Failure(WebFailure.HttpStatus, 403),
        FakeWebDownloader.Failure(WebFailure.HttpStatus, 429),
        FakeWebDownloader.Failure(WebFailure.HttpStatus, 404),
        FakeWebDownloader.Failure(WebFailure.TooLarge),
    };

    [Theory]
    [MemberData(nameof(Failures))]
    public void Check_Network_Limit_Status_AreFailed(WebText response)
    {
        var (result, _, _) = Run(response);

        Assert.Equal(UpdateCheckOutcome.Failed, result.Outcome);
        Assert.Null(result.LatestVersion);
    }

    [Fact]
    public void Check_InvalidBody_IsFailed_AndNothingFromBodyIsLogged()
    {
        var (result, _, log) = Run(FakeWebDownloader.Ok("<html>tajny-tekst</html>"));

        Assert.Equal(UpdateCheckOutcome.Failed, result.Outcome);
        Assert.NotEmpty(log.Lines);
        Assert.DoesNotContain(log.Lines, l => l.Contains("tajny-tekst"));
    }

    [Fact]
    public void Check_BadTag_IsNotLogged()
    {
        var (result, _, log) = Run(FakeWebDownloader.Ok(Json("v1<script>")));

        Assert.Equal(UpdateCheckOutcome.Failed, result.Outcome);
        Assert.DoesNotContain(log.Lines, l => l.Contains("script"));
    }

    [Fact]
    public void Check_DownloaderThrows_IsFailed()
    {
        var web = new FakeWebDownloader { OnGetText = () => throw new InvalidOperationException("boom") };
        var log = new ListLog();

        var result = new UpdateChecker(web, "4.0.9", log).Check();

        Assert.Equal(UpdateCheckOutcome.Failed, result.Outcome);
    }

    [Fact]
    public void Check_Failure_LogsOnlyInfo()
    {
        var (_, _, log) = Run(FakeWebDownloader.Failure(WebFailure.HttpStatus, 403));

        Assert.NotEmpty(log.Lines);
        Assert.All(log.Lines, l => Assert.StartsWith("INFO", l));
        Assert.Contains(log.Lines, l => l.Contains("403"));
    }

    [Fact]
    public void Check_UnparsableCurrentVersion_IsFailed()
    {
        var (result, _, _) = Run(FakeWebDownloader.Ok(Json("v4.0.9")), current: "abc");

        Assert.Equal(UpdateCheckOutcome.Failed, result.Outcome);
    }
}