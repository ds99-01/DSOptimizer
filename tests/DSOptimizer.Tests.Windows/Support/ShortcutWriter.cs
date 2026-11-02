using System.Runtime.InteropServices;
using System.Runtime.InteropServices.ComTypes;
using DSOptimizer.Windows.Native;

namespace DSOptimizer.Tests.Windows.Support;

/// <summary>
/// Zapis skrótu <c>.lnk</c> przez COM <c>ShellLink</c> w procesie testu (bez nowego procesu, bez powłoki), wyłącznie w katalogu testu
/// w <c>%TEMP%\dso-tests\&lt;guid&gt;</c>.
/// </summary>
public static class ShortcutWriter
{
    public static void Save(string linkPath, string target, string? arguments = null, string? darwinId = null)
    {
        var link = ShellLinkInterop.Create();
        try
        {
            link.SetPath(target);
            if (arguments is not null)
            {
                link.SetArguments(arguments);
            }

            if (darwinId is not null)
            {
                AddDarwinBlock((IShellLinkDataList)link, darwinId);
            }

            ((IPersistFile)link).Save(linkPath, true);
        }
        finally
        {
            Marshal.FinalReleaseComObject(link);
        }
    }

    /// <summary>Blok <c>EXP_DARWIN_LINK</c> (podpis 0xA0000006, ANSI i Unicode po 260 znaków) – skrót MSI „advertised”.</summary>
    private static void AddDarwinBlock(IShellLinkDataList data, string darwinId)
    {
        const int size = 8 + 260 + (260 * 2);
        var block = Marshal.AllocHGlobal(size);
        try
        {
            var bytes = new byte[size];
            System.BitConverter.GetBytes(size).CopyTo(bytes, 0);
            System.BitConverter.GetBytes(0xA0000006u).CopyTo(bytes, 4);
            System.Text.Encoding.ASCII.GetBytes(darwinId).CopyTo(bytes, 8);
            System.Text.Encoding.Unicode.GetBytes(darwinId).CopyTo(bytes, 8 + 260);
            Marshal.Copy(bytes, 0, block, size);
            data.AddDataBlock(block);
            data.GetFlags(out var flags);
            data.SetFlags(flags | ShellLinkInterop.SldfHasDarwinId);
        }
        finally
        {
            Marshal.FreeHGlobal(block);
        }
    }
}
