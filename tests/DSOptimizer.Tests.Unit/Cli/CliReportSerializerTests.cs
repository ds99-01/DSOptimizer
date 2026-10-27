using System;
using System.Linq;
using System.Text.RegularExpressions;
using DSOptimizer.Core.Abstractions;
using DSOptimizer.Core.Apps;
using DSOptimizer.Core.Cli;
using DSOptimizer.Core.Engine;
using DSOptimizer.Core.Model;
using Newtonsoft.Json.Linq;
using Xunit;

namespace DSOptimizer.Tests.Unit.Cli;

public class CliReportSerializerTests
{
    [Fact]
    public void Serialize_UsesCamelCaseAndEnumNames()
    {
        var json = JObject.Parse(CliReportSerializer.Serialize(new CliReport
        {
            Command = CliCommand.Apply,
            TweakId = "vmtest.a",
            ExitCode = 1,
            Result = CliExitCode.OperationFailed,
            Outcome = TweakOutcome.FailedRolledBack,
            RestorePoint = new CliRestorePointInfo { Status = RestorePointStatus.Failed, Code = 1058u, Message = "x" },
            User = new CliUserInfo { Sid = "S-1-5-21-1", IsCertain = true },
            Warnings = new[] { "uwaga" },
        }));
        Assert.Equal(1, (int)json["schemaVersion"]!);
        Assert.Equal("Apply", (string)json["command"]!);
        Assert.Equal(1, (int)json["exitCode"]!);
        Assert.Equal("OperationFailed", (string)json["result"]!);
        Assert.Equal("FailedRolledBack", (string)json["outcome"]!);
        Assert.Equal(1058, (int)json["restorePoint"]!["code"]!);
        Assert.True((bool)json["user"]!["isCertain"]!);
        Assert.Equal("uwaga", (string)json["warnings"]![0]!);
        Assert.Equal(JTokenType.Null, json["state"]!.Type);
    }

    [Fact]
    public void Serialize_ProductContainsNameAndVersion()
        => Assert.StartsWith("DSOptimizer ", (string)JObject.Parse(CliReportSerializer.Serialize(new CliReport()))["product"]!, StringComparison.Ordinal);

    private static CliReport FullReport() => new()
    {
        Command = CliCommand.Apply,
        TweakId = "vmtest.a",
        ExitCode = 0,
        Result = CliExitCode.Success,
        State = DetectionState.Applied,
        Outcome = TweakOutcome.Applied,
        FinalState = DetectionState.Applied,
        Message = "ok",
        Warnings = new[] { "w" },
        NotRestored = new[] { "n" },
        Errors = new[] { "e" },
        RequiresReboot = true,
        Aborted = true,
        RestorePoint = new CliRestorePointInfo { Status = RestorePointStatus.Created, Code = 1u, Message = "m" },
        Catalog = new CliCatalogInfo { Source = "builtin", Hash = "sha256:a" },
        Machine = new CliMachineInfo { Build = 26300, Revision = 1, Edition = "Pro", MachineName = "PC" },
        User = new CliUserInfo { Sid = "S-1-5-21-1", IsCertain = true },
        StartedAt = new DateTimeOffset(2026, 10, 17, 12, 0, 0, TimeSpan.FromHours(2)),
        FinishedAt = new DateTimeOffset(2026, 10, 17, 12, 0, 1, TimeSpan.Zero),
    };

    private static string[] Keys(JToken token) => ((JObject)token).Properties().Select(p => p.Name).OrderBy(n => n, StringComparer.Ordinal).ToArray();

    private static string[] Sorted(params string[] names) => names.OrderBy(n => n, StringComparer.Ordinal).ToArray();

    [Fact]
    public void Serialize_FullReport_HasExactKeySet()
    {
        var json = JObject.Parse(CliReportSerializer.Serialize(FullReport()));
        Assert.Equal(
            Sorted("schemaVersion", "product", "command", "tweakId", "exitCode", "result", "state", "outcome", "finalState", "message",
                "warnings", "notRestored", "errors", "requiresReboot", "aborted", "restorePoint", "catalog", "machine", "user", "startedAt", "finishedAt"),
            Keys(json));
        Assert.Equal(Sorted("status", "code", "message"), Keys(json["restorePoint"]!));
        Assert.Equal(Sorted("source", "hash"), Keys(json["catalog"]!));
        Assert.Equal(Sorted("build", "revision", "edition", "machineName"), Keys(json["machine"]!));
        Assert.Equal(Sorted("sid", "isCertain"), Keys(json["user"]!));
    }

    [Fact]
    public void Serialize_ListAppsReport_HasAppsWithExactKeysAndEnumNames()
    {
        var json = JObject.Parse(CliReportSerializer.Serialize(new CliReport
        {
            Command = CliCommand.ListApps,
            Apps = new[]
            {
                new CliAppInfo
                {
                    Id = "appx:Microsoft.WindowsCalculator_8wekyb3d8bbwe",
                    Kind = AppKind.Store,
                    Name = "Kalkulator",
                    Publisher = null,
                    Group = AppGroup.Protected,
                    Removability = AppRemovability.Protected,
                },
            },
        }));
        Assert.Equal("ListApps", (string)json["command"]!);
        var app = json["apps"]![0]!;
        Assert.Equal(Sorted("id", "kind", "name", "publisher", "group", "removability"), Keys(app));
        Assert.Equal("Store", (string)app["kind"]!);
        Assert.Equal("Protected", (string)app["group"]!);
        Assert.Equal("Protected", (string)app["removability"]!);
        Assert.Equal(JTokenType.Null, app["publisher"]!.Type);
    }

    [Fact]
    public void Serialize_RemoveAppCommandName()
        => Assert.Equal("RemoveApp", (string)JObject.Parse(CliReportSerializer.Serialize(new CliReport { Command = CliCommand.RemoveApp }))["command"]!);

    [Fact]
    public void Serialize_ReportWithoutApps_OmitsAppsKey()
        => Assert.Null(JObject.Parse(CliReportSerializer.Serialize(FullReport()))["apps"]);

    [Fact]
    public void Serialize_Dates_IncludeOffset()
    {
        var pattern = new Regex(@"^\d{4}-\d{2}-\d{2}T\d{2}:\d{2}:\d{2}(\.\d+)?(Z|[+-]\d{2}:\d{2})$");
        // Surowy tekst JSON (JObject.Parse zamieniłby daty na DateTime).
        var raw = CliReportSerializer.Serialize(FullReport());
        var started = Regex.Match(raw, "\"startedAt\": \"([^\"]+)\"").Groups[1].Value;
        var finished = Regex.Match(raw, "\"finishedAt\": \"([^\"]+)\"").Groups[1].Value;
        Assert.Matches(pattern, started);
        Assert.Matches(pattern, finished);
    }
}
