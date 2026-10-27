using System;
using System.Runtime.InteropServices;

namespace DSOptimizer.Windows.Native;

/// <summary>kernel32: przerwanie blokującego <c>ReadFile</c> wątku czytającego potok (<c>PipeReader</c>).</summary>
internal static partial class NativeMethods
{
    /// <summary><c>THREAD_TERMINATE</c>: prawo wymagane przez <c>CancelSynchronousIo</c>.</summary>
    internal const uint ThreadTerminate = 0x0001;

    [DllImport("kernel32.dll")]
    internal static extern uint GetCurrentThreadId();

    [DllImport("kernel32.dll", SetLastError = true)]
    internal static extern IntPtr OpenThread(uint desiredAccess, [MarshalAs(UnmanagedType.Bool)] bool inheritHandle, uint threadId);

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool CancelSynchronousIo(IntPtr thread);
}
