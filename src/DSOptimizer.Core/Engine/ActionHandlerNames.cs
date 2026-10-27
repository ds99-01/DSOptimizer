using System.Collections.Generic;

namespace DSOptimizer.Core.Engine;

/// <summary>Nazwy handlerów akcji C# dopuszczone w polu katalogu <c>handler</c>.</summary>
public static class ActionHandlerNames
{
    public const string WuReset = "wu-reset";

    public const string WuResetFull = "wu-reset-full";

    public const string RestorePoint = "restore-point";

    public const string SystemFiles = "system-files";

    public const string DiskCleanup = "disk-cleanup";

    public const string WindowsOld = "windows-old";

    /// <summary>WinUtil z wydania GitHuba ze sprawdzeniem SHA256 (Etap 5); krótka akcja, nie długa naprawa.</summary>
    public const string WinUtil = "winutil";

    public static IReadOnlyCollection<string> All { get; } = new[] { WuReset, WuResetFull, RestorePoint, SystemFiles, DiskCleanup, WindowsOld, WinUtil };

    /// <summary>Długie naprawy z oknem postępu (GUI po tej liście wybiera okno postępu).</summary>
    public static IReadOnlyCollection<string> LongRunning { get; } = new[] { SystemFiles, DiskCleanup, WindowsOld };
}
