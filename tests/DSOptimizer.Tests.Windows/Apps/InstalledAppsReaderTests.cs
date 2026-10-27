using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using DSOptimizer.Core.Abstractions;
using DSOptimizer.Core.Apps;
using DSOptimizer.Core.Errors;
using DSOptimizer.Tests.Windows.Support;
using DSOptimizer.Windows.Apps;
using Microsoft.Win32;
using Xunit;

namespace DSOptimizer.Tests.Windows.Apps;

[Trait("Category", "Windows")]
public class InstalledAppsReaderTests
{
    private const string Sid = "S-1-5-21-0-0-0-1000";

    private const string NewsJson =
        """[{"Name":"Microsoft.BingNews","PackageFullName":"Microsoft.BingNews_4.55.0.0_x64__8wekyb3d8bbwe","PackageFamilyName":"Microsoft.BingNews_8wekyb3d8bbwe","Publisher":"CN=Microsoft Corporation","Version":"4.55.0.0","IsFramework":false,"IsResourcePackage":false,"NonRemovable":false,"SignatureKind":"Store","IsProvisioned":true,"DisplayName":"Wiadomości"}]""";

    private sealed class FakeRunner : IScriptRunner
    {
        public List<ScriptRequest> Requests { get; } = new();

        public Func<ScriptRequest, ScriptResult> Respond { get; set; } = _ => new ScriptResult { ExitCode = 0, Output = new[] { NewsJson } };

        public ScriptResult Run(ScriptRequest request, IProgress<string>? output, CancellationToken cancellationToken)
        {
            Requests.Add(request);
            return Respond(request);
        }
    }

    private static UninstallRegistryReader SandboxReader(RegistrySandbox sandbox)
        => new(new[] { (sandbox.HkcuPath.Substring(@"HKCU\".Length), RegistryHive.CurrentUser, RegistryView.Registry64) });

    private static void AddProgram(RegistrySandbox sandbox, string keyName, string displayName)
    {
        using var root = sandbox.OpenOrCreate();
        using var key = root.CreateSubKey(keyName)!;
        key.SetValue("DisplayName", displayName);
    }

    private static InstalledAppsReader Reader(FakeRunner runner, RegistrySandbox sandbox)
        => new(new AppxReader(runner, _ => null), _ => SandboxReader(sandbox));

    [Fact]
    public void Read_MergesStoreAndPrograms_WithoutWarnings()
    {
        using var sandbox = new RegistrySandbox();
        AddProgram(sandbox, "Prog1", "Program Testowy");
        var runner = new FakeRunner();

        var reading = Reader(runner, sandbox).Read(Sid);

        Assert.Empty(reading.Warnings);
        Assert.Equal(new[] { "Wiadomości", "Program Testowy" }, reading.Apps.Select(a => a.Name));
        var request = Assert.Single(runner.Requests);
        Assert.Equal(Sid, request.UserSid);
        Assert.Equal(AppxScript.Body, request.Body);
        Assert.Equal(string.Empty, request.Snapshot);
        Assert.NotNull(request.Timeout);
    }

    [Theory]
    [InlineData("\"ms-resource:PublisherName\"", "Clipchamp Pty Ltd", "Clipchamp Pty Ltd")]
    [InlineData("\"Clipchamp\"", null, "Clipchamp")]
    [InlineData("\"ms-resource:PublisherName\"", null, "CN=33F0F141-36F3-4EC2-A77D-51B53D0BA0E4")]
    [InlineData("null", "nieużyte", "CN=33F0F141-36F3-4EC2-A77D-51B53D0BA0E4")]
    public void Read_StorePublisher_FromManifestPublisherDisplayName_ElseDistinguishedName(string rawJson, string? loaded, string expected)
    {
        using var sandbox = new RegistrySandbox();
        var json = NewsJson
            .Replace("\"Publisher\":\"CN=Microsoft Corporation\"", "\"Publisher\":\"CN=33F0F141-36F3-4EC2-A77D-51B53D0BA0E4\"")
            .Replace("\"DisplayName\":\"Wiadomości\"", "\"DisplayName\":\"Wiadomości\",\"PublisherDisplayName\":" + rawJson);
        var runner = new FakeRunner { Respond = _ => new ScriptResult { ExitCode = 0, Output = new[] { json } } };
        var reader = new InstalledAppsReader(
            new AppxReader(runner, source => source.Contains("PublisherName") ? loaded : null),
            _ => SandboxReader(sandbox));

        var app = Assert.Single(reader.Read(Sid).Apps);

        Assert.Equal(expected, app.Publisher);
        Assert.Equal("Wiadomości", app.Name);
    }

    [Fact]
    public void Read_AppxThrows_KeepsProgramsAndWarns()
    {
        using var sandbox = new RegistrySandbox();
        AddProgram(sandbox, "Prog1", "Program Testowy");
        var runner = new FakeRunner { Respond = _ => throw new OperationException(OperationErrorKind.Timeout, "Za długo.") };

        var reading = Reader(runner, sandbox).Read(Sid);

        Assert.Equal("Program Testowy", Assert.Single(reading.Apps).Name);
        Assert.Equal(AppsText.AppxReadFailed("Za długo."), Assert.Single(reading.Warnings));
    }

    [Fact]
    public void Read_AppxNonZeroExit_WarnsWithScriptError()
    {
        using var sandbox = new RegistrySandbox();
        AddProgram(sandbox, "Prog1", "Program Testowy");
        var runner = new FakeRunner { Respond = _ => new ScriptResult { ExitCode = 1, Errors = new[] { "Brak uprawnień." } } };

        var reading = Reader(runner, sandbox).Read(Sid);

        Assert.Single(reading.Apps);
        Assert.Equal(AppsText.AppxReadFailed("Brak uprawnień."), Assert.Single(reading.Warnings));
    }

    [Fact]
    public void Read_AppxGarbageOutput_WarnsAndKeepsPrograms()
    {
        using var sandbox = new RegistrySandbox();
        AddProgram(sandbox, "Prog1", "Program Testowy");
        var runner = new FakeRunner { Respond = _ => new ScriptResult { ExitCode = 0, Output = new[] { "to nie json" } } };

        var reading = Reader(runner, sandbox).Read(Sid);

        Assert.Single(reading.Apps);
        Assert.StartsWith(AppsText.AppxReadFailed(string.Empty), Assert.Single(reading.Warnings));
    }

    [Fact]
    public void Read_RegistryThrows_KeepsStoreAndWarns()
    {
        var reader = new InstalledAppsReader(new AppxReader(new FakeRunner(), _ => null), _ => throw new InvalidOperationException("Rejestr padł."));

        var reading = reader.Read(Sid);

        Assert.Equal("Wiadomości", Assert.Single(reading.Apps).Name);
        Assert.Equal(AppsText.RegistryReadFailed("Rejestr padł."), Assert.Single(reading.Warnings));
    }

    private static ScriptResult Presence(bool installed, bool provisioned)
        => new()
        {
            ExitCode = 0,
            Output = new[] { "{\"Installed\":" + (installed ? "true" : "false") + ",\"Provisioned\":" + (provisioned ? "true" : "false") + "}" },
        };

    [Theory]
    [InlineData(AppPresenceScope.User, false, false)]
    [InlineData(AppPresenceScope.UserAndImage, false, true)]
    [InlineData(AppPresenceScope.AllUsersAndImage, true, true)]
    public void ReadPresence_Store_AsksFreshWithPresenceScript_AndScopeInRequest(AppPresenceScope scope, bool allUsers, bool image)
    {
        using var sandbox = new RegistrySandbox();
        var runner = new FakeRunner();
        var reader = Reader(runner, sandbox);
        var app = reader.Read(Sid).Apps.Single();
        runner.Requests.Clear();

        runner.Respond = _ => Presence(installed: true, provisioned: false);
        Assert.Equal(AppPresence.Present, reader.ReadPresence(app, Sid, scope));

        runner.Respond = _ => Presence(installed: false, provisioned: true);
        Assert.Equal(AppPresence.ImageOnly, reader.ReadPresence(app, Sid, scope));

        runner.Respond = _ => Presence(installed: false, provisioned: false);
        Assert.Equal(AppPresence.Absent, reader.ReadPresence(app, Sid, scope));

        Assert.Equal(3, runner.Requests.Count);
        Assert.All(runner.Requests, r =>
        {
            Assert.Equal(AppxScript.PresenceBody, r.Body);
            Assert.Equal(Sid, r.UserSid);
            Assert.Equal(AppxScript.PresenceRequest("Microsoft.BingNews", scope), r.Snapshot);
            var request = Newtonsoft.Json.Linq.JObject.Parse(r.Snapshot);
            Assert.Equal(allUsers, (bool)request["AllUsers"]!);
            Assert.Equal(image, (bool)request["Image"]!);
        });
    }

    [Fact]
    public void ReadPresence_Store_ReadFails_IsUnknown_NeverAbsent()
    {
        using var sandbox = new RegistrySandbox();
        var runner = new FakeRunner();
        var reader = Reader(runner, sandbox);
        var app = reader.Read(Sid).Apps.Single();

        runner.Respond = _ => new ScriptResult { ExitCode = 1, Errors = new[] { "błąd" } };
        Assert.Equal(AppPresence.Unknown, reader.ReadPresence(app, Sid, AppPresenceScope.User));

        runner.Respond = _ => throw new OperationException(OperationErrorKind.Timeout, "Za długo.");
        Assert.Equal(AppPresence.Unknown, reader.ReadPresence(app, Sid, AppPresenceScope.User));

        runner.Respond = _ => new ScriptResult { ExitCode = 0, Output = new[] { "[]" } };
        Assert.Equal(AppPresence.Unknown, reader.ReadPresence(app, Sid, AppPresenceScope.AllUsersAndImage));
    }

    [Fact]
    public void ReadPresence_Program_FollowsRegistryKey()
    {
        var sandbox = new RegistrySandbox();
        AddProgram(sandbox, "Prog1", "Program Testowy");
        var runner = new FakeRunner { Respond = _ => new ScriptResult { ExitCode = 0, Output = new[] { "[]" } } };
        var reader = Reader(runner, sandbox);
        var app = reader.Read(Sid).Apps.Single();
        runner.Requests.Clear();

        Assert.Equal(AppPresence.Present, reader.ReadPresence(app, Sid, AppPresenceScope.User));

        sandbox.Dispose();
        Assert.Equal(AppPresence.Absent, reader.ReadPresence(app, Sid, AppPresenceScope.AllUsersAndImage));
        Assert.Empty(runner.Requests);
    }
}
