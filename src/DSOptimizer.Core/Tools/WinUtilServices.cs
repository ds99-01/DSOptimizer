using DSOptimizer.Core.Abstractions;

namespace DSOptimizer.Core.Tools;

/// <summary>
/// Usługi handlera <c>winutil</c>: HTTP i folder pobrań wspólne z instalacją z GitHuba (RustDesk), launcher ten sam co dla pól
/// <c>launch</c> (CreateProcess poza Job Objectem); składane w <c>AppComposition</c>.
/// </summary>
public sealed class WinUtilServices
{
    public required IWebDownloader Web { get; init; }

    public required IDownloadFiles Files { get; init; }

    public required IProcessLauncher Launcher { get; init; }

    public required ILog Log { get; init; }
}
