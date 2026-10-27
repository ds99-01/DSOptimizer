using System;
using System.Collections.Generic;
using System.Threading;
using DSOptimizer.Core.Abstractions;
using DSOptimizer.Core.Apps;
using DSOptimizer.Windows.Apps;
using Newtonsoft.Json.Linq;
using Xunit;

namespace DSOptimizer.Tests.Windows.Apps;

/// <summary>Składanie żądań wykonawcy usuwania; skrypt i start deinstalatora są podmienione – nic nie jest usuwane.</summary>
[Trait("Category", "Windows")]
public class AppxRemoverTests
{
    private const string Sid = "S-1-5-21-0-0-0-1000";

    private static readonly AppxInfo News = new(
        "Microsoft.BingNews_4.55.0.0_x64__8wekyb3d8bbwe", "Microsoft.BingNews_8wekyb3d8bbwe", "Microsoft.BingNews", false, false, false, "Store", true);

    private sealed class FakeRunner : IScriptRunner
    {
        public List<ScriptRequest> Requests { get; } = new();

        public ScriptResult Run(ScriptRequest request, IProgress<string>? output, CancellationToken cancellationToken)
        {
            Requests.Add(request);
            return new ScriptResult { ExitCode = 0 };
        }
    }

    [Theory]
    [InlineData(StoreRemovalScope.CurrentUser, true, false)]
    [InlineData(StoreRemovalScope.AllUsers, true, true)]
    [InlineData(StoreRemovalScope.CurrentUser, false, false)]
    public void RemoveStoreApp_RunsRemovalScript_WithRequestInSnapshot(StoreRemovalScope scope, bool deprovision, bool allUsers)
    {
        var runner = new FakeRunner();
        var remover = new AppxRemover(runner, (_, _, _) => throw new InvalidOperationException("Bez deinstalatora w tym teście."));

        var result = remover.RemoveStoreApp(News, Sid, scope, deprovision, CancellationToken.None);

        Assert.Equal(0, result.ExitCode);
        var request = Assert.Single(runner.Requests);
        Assert.Equal(AppxRemovalScript.Body, request.Body);
        Assert.Equal(Sid, request.UserSid);
        Assert.Equal(AppRemovalTimeouts.Store, request.Timeout);
        var json = JObject.Parse(request.Snapshot);
        Assert.Equal("Microsoft.BingNews", (string?)json["Name"]);
        Assert.Equal(allUsers, (bool)json["AllUsers"]!);
        Assert.Equal(deprovision, (bool)json["Deprovision"]!);
    }

    [Fact]
    public void RunUninstaller_DelegatesWithPlanAndLimit()
    {
        var plan = new UninstallPlan(@"C:\Program Files\V\unins000.exe", "/SILENT", UninstallMode.Interactive);
        (UninstallPlan Plan, TimeSpan Timeout)? seen = null;
        var remover = new AppxRemover(new FakeRunner(), (p, t, _) =>
        {
            seen = (p, t);
            return new UninstallerRun(3010, false);
        });

        var run = remover.RunUninstaller(plan, AppRemovalTimeouts.Interactive, CancellationToken.None);

        Assert.Equal(new UninstallerRun(3010, false), run);
        Assert.Equal((plan, AppRemovalTimeouts.Interactive), seen);
    }

    [Fact]
    public void NullArguments_Throw()
    {
        var remover = new AppxRemover(new FakeRunner());

        Assert.Throws<ArgumentNullException>(() => remover.RemoveStoreApp(null!, Sid, StoreRemovalScope.CurrentUser, false, CancellationToken.None));
        Assert.Throws<ArgumentNullException>(() => remover.RemoveStoreApp(News, null!, StoreRemovalScope.CurrentUser, false, CancellationToken.None));
        Assert.Throws<ArgumentNullException>(() => remover.RunUninstaller(null!, TimeSpan.FromSeconds(1), CancellationToken.None));
        Assert.Throws<ArgumentNullException>(() => new AppxRemover(null!));
    }
}
