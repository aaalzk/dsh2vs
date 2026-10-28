using System.Runtime.InteropServices;

namespace Dsh2Vs.Debug;

/// <summary>
/// A few kernel32 calls the debugger needs but .NET does not expose:
/// interrupting a target parked in a modal loop, and killing a debuggee the engine refuses to release.
/// </summary>
internal static class Native
{
    private const uint ProcessAllAccess = 0x1F0FFF;
    private const uint ProcessTerminate = 0x0001;
    private const uint MinimalBreakRights = 0x0002 | 0x0008 | 0x0010 | 0x0020 | 0x0400 | 0x0800;

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern IntPtr OpenProcess(uint desiredAccess, bool inheritHandle, int processId);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool DebugBreakProcess(IntPtr process);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool TerminateProcess(IntPtr process, uint exitCode);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool CloseHandle(IntPtr handle);

    /// <summary>Injects a breakpoint from outside, for a target that never reads the debugger queue.</summary>
    public static bool BreakIntoProcess(int processId)
    {
        var handle = OpenProcess(ProcessAllAccess, false, processId);
        if (handle == IntPtr.Zero)
        {
            handle = OpenProcess(MinimalBreakRights, false, processId);
        }

        if (handle == IntPtr.Zero)
        {
            return false;
        }

        try
        {
            return DebugBreakProcess(handle);
        }
        finally
        {
            CloseHandle(handle);
        }
    }

    /// <summary>Last-resort kill for a debuggee the engine leaves running after its quit command.</summary>
    public static bool Kill(int processId)
    {
        var handle = OpenProcess(ProcessTerminate, false, processId);
        if (handle == IntPtr.Zero)
        {
            return false;
        }

        try
        {
            return TerminateProcess(handle, 1);
        }
        finally
        {
            CloseHandle(handle);
        }
    }
}
