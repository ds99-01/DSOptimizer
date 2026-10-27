using DSOptimizer.Core.Abstractions;

namespace DSOptimizer.Core.Install;

/// <summary>Usługi instalacji z GitHuba (RustDesk): HTTP, podpis, msiexec i folder pobrań; składane w <c>AppComposition</c>.</summary>
public sealed class GitHubInstallServices
{
    public required IWebDownloader Web { get; init; }

    public required ISignatureVerifier Signatures { get; init; }

    public required IMsiInstaller Msi { get; init; }

    public required IDownloadFiles Files { get; init; }
}
