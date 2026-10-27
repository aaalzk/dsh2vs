using System.Runtime.InteropServices;

namespace Dsh2Vs.Debug;

/// <summary>
/// Outside-the-box interrupt for debuggees that cannot process a queued cdb command
/// (a thread parked in a modal message loop, for example).
/// </summary>
internal static class Native
{
    private const uint ProcessAllAccess = 0x1F0FFF;
    private const uint MinimalBreakRights = 0x0002 /*VM_WRITE*/ | 0x0008 /*VM_OPERATION*/
                                             | 0x0010 /*CREATE_THREAD*/ | 0x0020 /*QUERY_INFO*/
                                             | 0x0400 /*QUERY_INFORMATION*/ | 0x0800 /*SUSPEND_RESUME*/;

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern IntPtr OpenProcess(uint desiredAccess, bool inheritHandle, int processId);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool DebugBreakProcess(IntPtr process);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool CloseHandle(IntPtr handle);

    public static bool DebugBreakProcess(int processId)
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
}
