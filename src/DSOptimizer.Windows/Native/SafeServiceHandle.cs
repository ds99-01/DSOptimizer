using Microsoft.Win32.SafeHandles;

namespace DSOptimizer.Windows.Native;

internal sealed class SafeServiceHandle : SafeHandleZeroOrMinusOneIsInvalid
{
    public SafeServiceHandle()
        : base(ownsHandle: true)
    {
    }

    protected override bool ReleaseHandle() => NativeMethods.CloseServiceHandle(handle);
}
