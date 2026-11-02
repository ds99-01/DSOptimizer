using System;
using DSOptimizer.Core.Autostart;
using DSOptimizer.Core.Cli;
using Xunit;

namespace DSOptimizer.Tests.Unit.Cli;

/// <summary>Składnia <c>--list-autostart</c>/<c>--set-autostart</c>/<c>--undo-autostart</c>: każdy błąd = kod 2 bez odczytu i zapisu.</summary>
public class CliParserAutostartTests
{
    private const string Id = "run:user:OneDrive";

    private static CliParseResult Parse(params string[] args) => CliParser.Parse(args);

    private static void AssertUsageError(CliParseResult result, string fragment)
    {
        Assert.False(result.IsSuccess);
        Assert.Null(result.Options);
        Assert.Contains(fragment, result.Error, StringComparison.Ordinal);
        Assert.Contains("Składnia:", result.Error, StringComparison.Ordinal);
    }

    [Fact]
    public void ListAutostart_NoValue()
    {
        var result = Parse("--LIST-AUTOSTART", "--out", @"C:\dso\out\a.json");

        Assert.True(result.IsSuccess, result.Error);
        var options = result.Options!;
        Assert.Equal(CliCommand.ListAutostart, options.Command);
        Assert.Equal(string.Empty, options.TweakId);
        Assert.Null(options.AutostartTarget);
        Assert.Equal(@"C:\dso\out\a.json", options.OutPath);
        Assert.True(CliParser.IsAutostartCommand(options.Command));
        AssertUsageError(Parse("--list-autostart", "x", "--out", "o.json"), "Nieoczekiwany argument 'x'");
    }

    [Theory]
    [InlineData("disabled", AutostartState.Disabled)]
    [InlineData("enabled", AutostartState.Enabled)]
    [InlineData("Disabled", AutostartState.Disabled)]
    public void SetAutostart_IdAndState(string state, AutostartState expected)
    {
        var result = Parse("--set-autostart", Id, state, "--out", "o.json");

        Assert.True(result.IsSuccess, result.Error);
        var options = result.Options!;
        Assert.Equal(CliCommand.SetAutostart, options.Command);
        Assert.Equal(Id, options.TweakId);
        Assert.Equal(expected, options.AutostartTarget);
        Assert.True(CliParser.IsAutostartCommand(options.Command));
    }

    [Fact]
    public void SetAutostart_MissingState_Usage()
    {
        AssertUsageError(Parse("--set-autostart", Id, "--out", "o.json"), "Opcja --set-autostart wymaga stanu po id (disabled albo enabled).");
        AssertUsageError(Parse("--out", "o.json", "--set-autostart", Id), "Opcja --set-autostart wymaga stanu po id (disabled albo enabled).");
        AssertUsageError(Parse("--set-autostart", "--out", "o.json"), "Opcja --set-autostart wymaga wartości.");
    }

    [Fact]
    public void SetAutostart_StateOff_Usage()
    {
        var result = Parse("--set-autostart", Id, "off", "--out", "o.json");

        AssertUsageError(result, "Nieznany stan --set-autostart 'off' (dozwolone: disabled, enabled).");
        Assert.Equal("o.json", result.OutPath);
        AssertUsageError(Parse("--set-autostart", Id, "unknown", "--out", "o.json"), "Nieznany stan --set-autostart 'unknown'");
    }

    [Fact]
    public void UndoAutostart_Id()
    {
        var result = Parse("--undo-autostart", Id, "--out", "o.json");

        Assert.True(result.IsSuccess, result.Error);
        var options = result.Options!;
        Assert.Equal(CliCommand.UndoAutostart, options.Command);
        Assert.Equal(Id, options.TweakId);
        Assert.Null(options.AutostartTarget);
        Assert.True(CliParser.IsAutostartCommand(options.Command));
        AssertUsageError(Parse("--undo-autostart", "--out", "o.json"), "Opcja --undo-autostart wymaga wartości.");
    }

    [Theory]
    [InlineData("--catalog x.json")]
    [InlineData("--apps-catalog x.json")]
    [InlineData("--restore-point skip")]
    public void Autostart_ForeignOption_Usage(string option)
    {
        var extra = option.Split(' ');
        foreach (var command in new[] { new[] { "--list-autostart" }, new[] { "--set-autostart", Id, "disabled" }, new[] { "--undo-autostart", Id } })
        {
            var args = new string[command.Length + extra.Length + 2];
            command.CopyTo(args, 0);
            extra.CopyTo(args, command.Length);
            args[args.Length - 2] = "--out";
            args[args.Length - 1] = "o.json";

            AssertUsageError(Parse(args), extra[0]);
        }
    }

    [Fact]
    public void Autostart_TwoCommands_Usage()
        => AssertUsageError(
            Parse("--list-autostart", "--undo-autostart", Id, "--out", "o.json"),
            "Podaj dokładnie jedno z: --detect, --apply, --undo, --execute, --list-apps, --remove-app, --list-install, --install-apps, --search-apps, --list-autostart, --set-autostart, --undo-autostart.");

    [Fact]
    public void Autostart_ForeignCatalog_NamesAutostart()
        => AssertUsageError(Parse("--list-autostart", "--catalog", "x.json", "--out", "o.json"), "Opcja --catalog nie dotyczy poleceń autostartu.");

    [Fact]
    public void IsAutostartCommand_OtherCommands_False()
    {
        Assert.False(CliParser.IsAutostartCommand(CliCommand.ListApps));
        Assert.False(CliParser.IsAutostartCommand(CliCommand.Detect));
        Assert.Contains("--set-autostart <id> disabled|enabled", CliParser.Usage, StringComparison.Ordinal);
    }
}
