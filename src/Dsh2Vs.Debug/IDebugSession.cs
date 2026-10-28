namespace Dsh2Vs.Debug;

/// <summary>
/// The surface the tools need from a debugger backend. Two implementations exist:
/// CdbSession (cdb.exe child process, fault-isolated) and DbgEngSession (in-process dbgeng).
/// </summary>
public interface IDebugSession : IAsyncDisposable
{
    string Target { get; }

    int? TargetPid { get; }

    DebuggerState State { get; }

    DateTimeOffset LastUsedAt { get; }

    bool HasExited { get; }

    /// <summary>
    /// Exit code of the debuggee, once known. 0xC0000135 (STATUS_DLL_NOT_FOUND) is the one that
    /// matters most: it means the program never really started.
    /// </summary>
    int? ExitCode { get; }

    /// <summary>Feeds one debugger command and returns everything it printed.</summary>
    Task<CdbResult> ExecuteAsync(string command, TimeSpan timeout, CancellationToken cancellationToken);

    /// <summary>Interrupts a running target and waits for it to stop.</summary>
    Task<CdbResult> InterruptAsync(TimeSpan timeout, CancellationToken cancellationToken);
}
