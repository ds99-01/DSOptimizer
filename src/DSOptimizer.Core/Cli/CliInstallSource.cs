using DSOptimizer.Core.Install;

namespace DSOptimizer.Core.Cli;

/// <summary>Wartości pola <c>source</c> w raportach <c>--list-install</c> i <c>--install-apps</c> (decyzja planu 10).</summary>
public static class CliInstallSource
{
    public const string Winget = "winget";

    public const string GitHub = "github";

    public static string Of(InstallOrigin origin) => origin == InstallOrigin.GitHub ? GitHub : Winget;
}
