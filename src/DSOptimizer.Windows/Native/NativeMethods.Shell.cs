using System;
using System.Runtime.InteropServices;

namespace DSOptimizer.Windows.Native;

/// <summary>shlwapi: rozwijanie napisów pośrednich (<c>@dll,-id</c>, <c>@{pakiet?ms-resource://…}</c>), tylko odczyt.</summary>
internal static partial class NativeMethods
{
    [DllImport("shlwapi.dll", CharSet = CharSet.Unicode, EntryPoint = "SHLoadIndirectString", ExactSpelling = true)]
    internal static extern int SHLoadIndirectString(string source, [Out] char[] outBuffer, uint outBufferChars, IntPtr reserved);
}
