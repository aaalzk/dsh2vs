using System.Runtime.InteropServices;
using System.Runtime.InteropServices.Marshalling;
using ClrDebug;
using ClrDebug.DbgEng;

namespace Dsh2Vs.Debug;

/// <summary>
/// In-process dbgeng session (M5): same command surface as <see cref="CdbSession"/>, but the debugger
/// engine runs inside the bridge instead of in a child process.
///
/// Trade-off (why cdb stays the default): this path removes text-round-tripping and a process hop,
/// but an engine crash now takes the service down with it. cdb in a child process keeps the bridge
/// alive when a session dies.
/// </summary>
public sealed partial class DbgEngSession : IDebugSession
{
    private static readonly object LoadGate = new();
    private static bool _nativeLoaded;

    private readonly DebugClient _client;
    private readonly DebugControl _control;
    private readonly OutputCapture _capture;
    private string _lastOutput = string.Empty;

    private DbgEngSession(DebugClient client, DebugControl control, OutputCapture capture, string target, int? pid)
    {
        _client = client;
        _control = control;
        _capture = capture;
        Target = target;
        TargetPid = pid;
        LastUsedAt = DateTimeOffset.UtcNow;
    }

    public string Target { get; }

    public int? TargetPid { get; private set; }

    public DebuggerState State { get; private set; } = DebuggerState.Stopped;

    public DateTimeOffset LastUsedAt { get; private set; }

    public bool HasExited { get; private set; }

    /// <summary>Exit code reported by the engine once the debuggee is gone.</summary>
    public int? ExitCode { get; private set; }

    public static Task<DbgEngSession> StartProcessAsync(
        string executable,
        IReadOnlyList<string> arguments,
        string? workingDirectory,
        CancellationToken cancellationToken)
        => Task.Run(() =>
        {
            var session = Create(executable);
            var commandLine = arguments.Count == 0
                ? Quote(executable)
                : Quote(executable) + " " + string.Join(' ', arguments.Select(Quote));

            session._client.CreateProcess(0, commandLine, DEBUG_CREATE_PROCESS.DEBUG_ONLY_THIS_PROCESS);
            session.WaitInitial();

            if (!string.IsNullOrWhiteSpace(workingDirectory))
            {
                session.ExecuteAsync($".cd \"{workingDirectory}\"", TimeSpan.FromSeconds(5), cancellationToken)
                    .GetAwaiter().GetResult();
            }

            return session;
        }, cancellationToken);

    public static Task<DbgEngSession> AttachAsync(int pid, string target, CancellationToken cancellationToken)
        => Task.Run(() =>
        {
            var session = Create(target);
            session._client.AttachProcess(0, pid, DEBUG_ATTACH.DEFAULT);
            session.WaitInitial();
            return session;
        }, cancellationToken);

    public static Task<DbgEngSession> OpenDumpAsync(string dumpPath, CancellationToken cancellationToken)
        => Task.Run(() =>
        {
            var session = Create(dumpPath);
            session._client.OpenDumpFile(dumpPath);
            session._control.WaitForEvent((DEBUG_WAIT)0, 30000);
            return session;
        }, cancellationToken);

    private static DbgEngSession Create(string target)
    {
        EnsureNativeDbgEng();

        var create = GetDebugCreate();
        var iid = new Guid("27fe5639-8407-4f47-8364-ee118fb08ac8"); // IID_IDebugClient
        var hr = create(iid, out var ptr);
        if (hr != 0 || ptr == IntPtr.Zero)
        {
            throw new InvalidOperationException($"DebugCreate failed: {hr}");
        }

        unsafe
        {
            var raw = ComInterfaceMarshaller<IDebugClient>.ConvertToManaged((void*)ptr);
            var client = new DebugClient(raw);
            var control = client.Control;

            // cdb sets this for you; a raw client does not, and without it the target just runs away.
            control.AddEngineOptions(DEBUG_ENGOPT.INITIAL_BREAK);

            var capture = new OutputCapture();
            client.TrySetOutputCallbacks(capture);

            return new DbgEngSession(client, control, capture, target, null);
        }
    }

    private void WaitInitial()
    {
        _control.WaitForEvent((DEBUG_WAIT)0, 30000);
        var boot = _capture.Take();

        // dbgeng prints "(<pid>.<tid>): ..." on the first event; that is the only pid we get for free.
        var match = System.Text.RegularExpressions.Regex.Match(boot, @"\((?<pid>[0-9a-f]{1,8})\.[0-9a-f]{1,8}\)");
        if (match.Success &&
            int.TryParse(match.Groups["pid"].Value, System.Globalization.NumberStyles.HexNumber, null, out var pid))
        {
            TargetPid = pid;
        }
    }

    public Task<CdbResult> ExecuteAsync(string command, TimeSpan timeout, CancellationToken cancellationToken)
        => Task.Run(() =>
        {
            if (HasExited)
            {
                return new CdbResult(_lastOutput, true, DebuggerState.Exited);
            }

            LastUsedAt = DateTimeOffset.UtcNow;
            _capture.Take();

            var runs = IsRunCommand(command);
            _control.Execute(DEBUG_OUTCTL.THIS_CLIENT, command, DEBUG_EXECUTE.DEFAULT);

            if (!runs)
            {
                var output = _capture.Take();
                return new CdbResult(Clean(output), true, State);
            }

            bool stopped;
            try
            {
                _control.WaitForEvent((DEBUG_WAIT)0, (int)Math.Min(int.MaxValue, timeout.TotalMilliseconds));
                stopped = true;
            }
            catch (DbgEngInvalidStateException)
            {
                stopped = false;
            }

            State = stopped ? DebuggerState.Stopped : DebuggerState.Running;
            var text = _capture.Take();

            if (!string.IsNullOrWhiteSpace(text))
            {
                _lastOutput = Clean(text);
            }

            // "no debuggee" means the target is gone: harvest the exit code while it is available.
            DetectExit();
            if (HasExited)
            {
                State = DebuggerState.Exited;
            }

            // Never answer a failed start with a bare placeholder: hand back what the engine said.
            var payload = string.IsNullOrWhiteSpace(text)
                ? (HasExited && _lastOutput.Length > 0 ? _lastOutput : "<debugger exited>")
                : Clean(text);

            return new CdbResult(payload, stopped, State);
        }, cancellationToken);

    /// <summary>Marks the session exited and harvests the debuggee exit code (0xC0000135 etc.).</summary>
    private void DetectExit()
    {
        if (HasExited)
        {
            return;
        }

        try
        {
            if ((int)_control.TryGetExecutionStatus(out var status) != 0)
            {
                return;
            }

            if ((int)status != 7) // DEBUG_STATUS_NO_DEBUGGEE
            {
                return;
            }

            HasExited = true;
            if ((int)_client.TryGetExitCode(out var code) == 0)
            {
                ExitCode = code;
            }
        }
        catch
        {
            // leave it to Dispose
        }
    }

    public Task<CdbResult> InterruptAsync(TimeSpan timeout, CancellationToken cancellationToken)
        => Task.Run(() =>
        {
            _capture.Take();
            try
            {
                _control.SetInterrupt(DEBUG_INTERRUPT.ACTIVE);
                _control.WaitForEvent((DEBUG_WAIT)0, (int)timeout.TotalMilliseconds);
                State = DebuggerState.Stopped;
            }
            catch (Exception)
            {
                // fall through with whatever was captured
            }

            _control.Execute(DEBUG_OUTCTL.THIS_CLIENT, ".lastevent", DEBUG_EXECUTE.DEFAULT);
            var text = _capture.Take();
            return new CdbResult(Clean(text), State == DebuggerState.Stopped, State);
        }, cancellationToken);

    public ValueTask DisposeAsync()
    {
        try
        {
            if (!HasExited)
            {
                _control.Execute(DEBUG_OUTCTL.THIS_CLIENT, "q", DEBUG_EXECUTE.DEFAULT);
            }
        }
        catch
        {
            // best effort
        }

        // The engine sometimes leaves the target behind (especially while it is still running),
        // and an orphaned debuggee is exactly what this bridge exists to avoid.
        if (TargetPid is int pid)
        {
            try
            {
                using var process = System.Diagnostics.Process.GetProcessById(pid);
                if (!process.HasExited)
                {
                    process.Kill(entireProcessTree: true);
                }
            }
            catch
            {
                // already gone
            }
        }

        HasExited = true;
        State = DebuggerState.Exited;
        _capture.Dispose();
        return ValueTask.CompletedTask;
    }

    private static bool IsRunCommand(string command)
    {
        var c = command.Trim().ToLowerInvariant();
        return c is "g" or "p" or "t" or "gu" or "g-" or "gc" or "gh"
               || c.StartsWith("g ", StringComparison.Ordinal)
               || c.StartsWith("p ", StringComparison.Ordinal)
               || c.StartsWith("t ", StringComparison.Ordinal);
    }

    private static string Quote(string value) =>
        value.Contains(' ') ? $"\"{value}\"" : value;

    private static string Clean(string text) => text.TrimEnd().Replace("\r\n", "\n");

    private static void EnsureNativeDbgEng()
    {
        lock (LoadGate)
        {
            if (_nativeLoaded)
            {
                return;
            }

            foreach (var candidate in Candidates())
            {
                if (File.Exists(candidate))
                {
                    NativeLibrary.Load(candidate);
                    _nativeLoaded = true;
                    return;
                }
            }

            throw new InvalidOperationException(
                "dbgeng.dll not found. Install the Windows SDK 'Debugging Tools for Windows', or set DSH2VS_DBGENG.");
        }
    }

    private static IEnumerable<string> Candidates()
    {
        var env = Environment.GetEnvironmentVariable("DSH2VS_DBGENG");
        if (!string.IsNullOrWhiteSpace(env))
        {
            yield return env;
        }

        foreach (var root in new[]
                 {
                     @"C:\Program Files (x86)\Windows Kits\10\Debuggers\x64",
                     @"C:\Program Files\Windows Kits\10\Debuggers\x64",
                     @"D:\Program Files (x86)\Windows Kits\10\Debuggers\x64",
                 })
        {
            yield return Path.Combine(root, "dbgeng.dll");
        }

        // Already resolvable through PATH / the app directory.
        yield return "dbgeng.dll";
    }

    private static DebugCreateDelegate GetDebugCreate()
    {
        foreach (var candidate in Candidates())
        {
            if (!File.Exists(candidate))
            {
                continue;
            }

            var handle = NativeLibrary.Load(candidate);
            if (NativeLibrary.TryGetExport(handle, "DebugCreate", out var fn))
            {
                return Marshal.GetDelegateForFunctionPointer<DebugCreateDelegate>(fn);
            }
        }

        throw new InvalidOperationException("DebugCreate not found in dbgeng.dll.");
    }

    /// <summary>Collects everything the engine prints, so one command's output can be returned as text.</summary>
    [GeneratedComClass]
    private sealed partial class OutputCapture : IDebugOutputCallbacks, IDisposable
    {
        private readonly List<string> _chunks = new();

        public HRESULT Output(DEBUG_OUTPUT mask, string text)
        {
            lock (_chunks)
            {
                _chunks.Add(text);
            }

            return HRESULT.S_OK;
        }

        public string Take()
        {
            lock (_chunks)
            {
                if (_chunks.Count == 0)
                {
                    return string.Empty;
                }

                var text = string.Concat(_chunks);
                _chunks.Clear();
                return text;
            }
        }

        public void Dispose()
        {
            lock (_chunks)
            {
                _chunks.Clear();
            }
        }
    }
}


