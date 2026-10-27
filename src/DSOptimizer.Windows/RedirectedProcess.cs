using System;
using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Text;
using DSOptimizer.Core.Abstractions;
using DSOptimizer.Core.Errors;
using DSOptimizer.Windows.Native;

namespace DSOptimizer.Windows;

/// <summary>
/// Wspólny start narzędzia z przechwyconym wyjściem (<see cref="System32ToolRunner"/>, <see cref="ServicingToolRunner"/>):
/// jeden potok na stdout i stderr, stdin = NUL, <c>bInheritHandles=TRUE</c> tylko z listą dziedziczonych uchwytów
/// (<c>PROC_THREAD_ATTRIBUTE_HANDLE_LIST</c>: koniec zapisu potoku i NUL), <c>CREATE_NO_WINDOW</c>.
/// </summary>
internal static class RedirectedProcess
{
    private const uint GenericRead = 0x80000000;
    private const uint FileShareReadWrite = 0x00000003;
    private const uint OpenExisting = 3;
    private const int ErrorFileNotFound = 2;
    private const int ErrorPathNotFound = 3;

    /// <summary>Potok wyjścia: koniec zapisu dziedziczony przez dziecko, koniec odczytu tylko w naszym procesie.</summary>
    public static (IntPtr Read, IntPtr Write) CreateOutputPipe()
    {
        var inheritable = Inheritable();
        if (!NativeMethods.CreatePipe(out var readPipe, out var writePipe, ref inheritable, 0))
        {
            throw new OperationException(OperationErrorKind.Unexpected, $"Nie można utworzyć potoku: {new Win32Exception(Marshal.GetLastWin32Error()).Message}");
        }

        if (!NativeMethods.SetHandleInformation(readPipe, NativeMethods.HandleFlagInherit, 0))
        {
            var error = Marshal.GetLastWin32Error();
            NativeMethods.CloseHandle(readPipe);
            NativeMethods.CloseHandle(writePipe);
            throw new OperationException(OperationErrorKind.Unexpected, $"Nie można zabezpieczyć potoku: {new Win32Exception(error).Message}");
        }

        return (readPipe, writePipe);
    }

    /// <summary>Dziedziczony uchwyt NUL na stdin (narzędzie czytające wejście kończy od razu, nie wisi na konsoli).</summary>
    public static IntPtr OpenNul()
    {
        var inheritable = Inheritable();
        var nul = NativeMethods.CreateFile("NUL", GenericRead, FileShareReadWrite, ref inheritable, OpenExisting, 0, IntPtr.Zero);
        if (nul == NativeMethods.InvalidHandleValue)
        {
            throw new OperationException(OperationErrorKind.Unexpected, $"Nie można otworzyć NUL: {new Win32Exception(Marshal.GetLastWin32Error()).Message}");
        }

        return nul;
    }

    /// <summary>
    /// <c>CreateProcessW</c> pełnej ścieżki <paramref name="toolPath"/> z dziedziczeniem wyłącznie <paramref name="stdIn"/> i
    /// <paramref name="output"/>. <paramref name="extraFlags"/> np. <c>CREATE_SUSPENDED</c>. Zwraca uchwyty procesu i wątku.
    /// </summary>
    public static (IntPtr Process, IntPtr Thread) Start(
        string toolPath,
        ToolRequest request,
        string workingDirectory,
        IntPtr stdIn,
        IntPtr output,
        uint extraFlags)
    {
        var handleList = Marshal.AllocHGlobal(IntPtr.Size * 2);
        var attributeList = IntPtr.Zero;
        var attributeListInitialized = false;
        try
        {
            Marshal.WriteIntPtr(handleList, 0, stdIn);
            Marshal.WriteIntPtr(handleList, IntPtr.Size, output);

            var size = IntPtr.Zero;
            NativeMethods.InitializeProcThreadAttributeList(IntPtr.Zero, 1, 0, ref size);
            attributeList = Marshal.AllocHGlobal(size);
            if (!NativeMethods.InitializeProcThreadAttributeList(attributeList, 1, 0, ref size))
            {
                throw Failure("przygotować atrybutów procesu", request, Marshal.GetLastWin32Error());
            }

            attributeListInitialized = true;
            if (!NativeMethods.UpdateProcThreadAttribute(
                    attributeList,
                    0,
                    NativeMethods.ProcThreadAttributeHandleList,
                    handleList,
                    (IntPtr)(IntPtr.Size * 2),
                    IntPtr.Zero,
                    IntPtr.Zero))
            {
                throw Failure("ograniczyć dziedziczonych uchwytów", request, Marshal.GetLastWin32Error());
            }

            var startupInfo = new NativeMethods.StartupInfoEx
            {
                StartupInfo = new NativeMethods.StartupInfo
                {
                    Size = (uint)Marshal.SizeOf<NativeMethods.StartupInfoEx>(),
                    Flags = NativeMethods.StartfUseStdHandles,
                    StdInput = stdIn,
                    StdOutput = output,
                    StdError = output,
                },
                AttributeList = attributeList,
            };

            var commandLine = (NativeProcessStarter.CommandLine(toolPath, request.Arguments) + "\0").ToCharArray();
            if (!NativeMethods.CreateProcessExtended(
                    toolPath,
                    commandLine,
                    IntPtr.Zero,
                    IntPtr.Zero,
                    inheritHandles: true,
                    extraFlags | NativeMethods.ExtendedStartupInfoPresent | NativeMethods.CreateNoWindow,
                    IntPtr.Zero,
                    workingDirectory,
                    ref startupInfo,
                    out var information))
            {
                throw Failure("uruchomić", request, Marshal.GetLastWin32Error());
            }

            return (information.Process, information.Thread);
        }
        finally
        {
            if (attributeListInitialized)
            {
                NativeMethods.DeleteProcThreadAttributeList(attributeList);
            }

            if (attributeList != IntPtr.Zero)
            {
                Marshal.FreeHGlobal(attributeList);
            }

            Marshal.FreeHGlobal(handleList);
        }
    }

    /// <summary>Strona kodowa OEM konsoli (<c>GetOEMCP</c>); nieobsługiwana = domyślne kodowanie systemu.</summary>
    public static Encoding OemEncoding()
    {
        try
        {
            return Encoding.GetEncoding((int)NativeMethods.GetOEMCP());
        }
        catch (Exception ex) when (ex is ArgumentException or NotSupportedException)
        {
            return Encoding.Default;
        }
    }

    public static void CloseIfSet(IntPtr handle)
    {
        if (handle != IntPtr.Zero)
        {
            NativeMethods.CloseHandle(handle);
        }
    }

    private static NativeMethods.SecurityAttributes Inheritable() => new()
    {
        Length = Marshal.SizeOf<NativeMethods.SecurityAttributes>(),
        InheritHandle = true,
    };

    private static OperationException Failure(string action, ToolRequest request, int error)
    {
        var kind = error is ErrorFileNotFound or ErrorPathNotFound ? OperationErrorKind.NotFound : OperationErrorKind.Unexpected;
        return new OperationException(kind, $"Nie można {action} {request.FileName}: {new Win32Exception(error).Message}");
    }
}
