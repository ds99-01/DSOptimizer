using DSOptimizer.Core.Abstractions;
using DSOptimizer.Windows.Native;

namespace DSOptimizer.Windows;

/// <summary>Wolne miejsce woluminu przez <c>GetDiskFreeSpaceExW</c> (całkowita liczba wolnych bajtów, bez limitów przydziału konta).</summary>
public sealed class WindowsDiskSpace : IDiskSpace
{
    public long? FreeBytes(string root)
    {
        if (string.IsNullOrEmpty(root))
        {
            return null;
        }

        return NativeMethods.GetDiskFreeSpaceEx(root, out _, out _, out var totalFree) && totalFree <= long.MaxValue
            ? (long)totalFree
            : null;
    }
}
