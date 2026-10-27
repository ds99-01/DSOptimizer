using System;
using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.InteropServices;

namespace DSOptimizer.Windows.Native;

/// <summary>Job Object z KILL_ON_JOB_CLOSE — zamknięcie/terminacja zabija proces i wszystkie jego dzieci.</summary>
internal sealed class JobObject : IDisposable
{
    private readonly SafeJobHandle _handle;

    public JobObject()
    {
        _handle = NativeMethods.CreateJobObject(IntPtr.Zero, null);
        if (_handle.IsInvalid)
        {
            throw new Win32Exception(Marshal.GetLastWin32Error());
        }

        var info = new NativeMethods.JobObjectExtendedLimitInformation();
        info.BasicLimitInformation.LimitFlags = NativeMethods.JobObjectLimitKillOnJobClose;
        var length = (uint)Marshal.SizeOf<NativeMethods.JobObjectExtendedLimitInformation>();
        if (!NativeMethods.SetInformationJobObject(_handle, NativeMethods.JobObjectInfoClassExtendedLimit, ref info, length))
        {
            var error = Marshal.GetLastWin32Error();
            _handle.Dispose();
            throw new Win32Exception(error);
        }
    }

    public void Assign(Process process)
    {
        if (process is null)
        {
            throw new ArgumentNullException(nameof(process));
        }

        if (!NativeMethods.AssignProcessToJobObject(_handle, process.Handle))
        {
            throw new Win32Exception(Marshal.GetLastWin32Error());
        }
    }

    /// <summary>Obejmuje proces po jego uchwycie (np. wstrzymany po <c>CREATE_SUSPENDED</c>).</summary>
    public void Assign(IntPtr processHandle)
    {
        if (!NativeMethods.AssignProcessToJobObject(_handle, processHandle))
        {
            throw new Win32Exception(Marshal.GetLastWin32Error());
        }
    }

    public void Terminate() => NativeMethods.TerminateJobObject(_handle, 1);

    public void Dispose() => _handle.Dispose();
}
