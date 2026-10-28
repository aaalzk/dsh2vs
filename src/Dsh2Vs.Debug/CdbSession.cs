using System.Diagnostics;
using System.Text;
using System.Text.RegularExpressions;

namespace Dsh2Vs.Debug;

public enum DebuggerState
{
    Stopped,
    Running,
    Exited,
}

public sealed record CdbResult(string Text, bool PromptReturned, DebuggerState State);

/// <summary>
/// A persistent cdb.exe driven over stdin/stdout.
///
/// cdb writes its prompt ("0:000>") without a trailing newline, so a line-based reader would block;
/// the pump appends raw characters and we synchronise on the last prompt position instead.
/// </summary>
public sealed class CdbSession : IDebugSession
{
    private static readonly Regex PromptPattern = new(@"\d+:\d+>", RegexOptions.Compiled);

    private readonly Process _process;
    private readonly StringBuilder _buffer = new();
    private readonly object _gate = new();
    private readonly SemaphoreSlim _io = new(1, 1);
    private readonly CancellationTokenSource _cts = new();

    private int _lastPromptEnd;
    private Task? _pump;
    private Task? _errPump;

    private CdbSession(Process process, string target)
    {
        _process = process;
        Target = target;
        LastUsedAt = DateTimeOffset.UtcNow;
    }

    public string Target { get; }

    /// <summary>Debuggee process id, parsed from cdb's first event line "(pid.thread)".</summary>
    public int? TargetPid { get; private set; }

    public DateTimeOffset LastUsedAt { get; private set; }

    public DebuggerState State { get; private set; } = DebuggerState.Running;

    public bool HasExited => _process.HasExited;

    /// <summary>Exit code of cdb itself, which mirrors the debuggee's when it fails to start.</summary>
    public int? ExitCode { get; private set; }

    private void CaptureExitCode()
    {
        if (ExitCode is not null || !_process.HasExited)
        {
            return;
        }

        try
        {
            ExitCode = _process.ExitCode;
        }
        catch
        {
            // exit code unavailable
        }
    }

    /// <summary>Last chunk of whatever the debugger printed, so a failed start is not a bare placeholder.</summary>
    private string LastOutputTail(int maxChars = 2000)
    {
        lock (_gate)
        {
            if (_buffer.Length == 0)
            {
                return string.Empty;
            }

            var start = Math.Max(0, _buffer.Length - maxChars);
            return _buffer.ToString(start, _buffer.Length - start);
        }
    }

    public static async Task<CdbSession> StartAsync(
        string cdbPath,
        IReadOnlyList<string> arguments,
        string target,
        int startupTimeoutSeconds,
        string? symbolPath,
        CancellationToken cancellationToken)
    {
        var psi = new ProcessStartInfo
        {
            FileName = cdbPath,
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardInput = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            StandardOutputEncoding = Encoding.UTF8,
            StandardErrorEncoding = Encoding.UTF8,
            WorkingDirectory = Path.GetDirectoryName(target) ?? Environment.CurrentDirectory,
        };

        foreach (var a in arguments)
        {
            psi.ArgumentList.Add(a);
        }

        if (!string.IsNullOrWhiteSpace(symbolPath))
        {
            psi.Environment["_NT_SYMBOL_PATH"] = symbolPath;
        }

        var process = new Process { StartInfo = psi, EnableRaisingEvents = true };
        if (!process.Start())
        {
            throw new InvalidOperationException($"Failed to start {cdbPath}");
        }

        var session = new CdbSession(process, target);
        session._pump = session.PumpAsync();
        session._errPump = session.PumpErrorAsync();

        var stopped = await session.WaitForPromptAsync(0, TimeSpan.FromSeconds(startupTimeoutSeconds), cancellationToken)
            .ConfigureAwait(false);

        session.State = stopped ? DebuggerState.Stopped : DebuggerState.Running;
        session.TargetPid = TryParsePid(session.Snapshot());
        return session;
    }

    /// <summary>Returns everything the debugger has printed so far (used for banner parsing).</summary>
    private string Snapshot()
    {
        lock (_gate)
        {
            return _buffer.ToString();
        }
    }

    private static readonly Regex EventPidPattern = new(@"\((?<pid>[0-9a-f]{1,8})\.[0-9a-f]{1,8}\)", RegexOptions.Compiled);

    internal static int? TryParsePid(string text)
    {
        var matches = EventPidPattern.Matches(text);
        if (matches.Count == 0)
        {
            return null;
        }

        var last = matches[^1].Groups["pid"].Value;
        return int.TryParse(last, System.Globalization.NumberStyles.HexNumber, null, out var pid) ? pid : null;
    }

    /// <summary>
    /// Interrupts a running target.
    ///
    /// cdb's ".break" only works when the target is in ordinary user code: a thread blocked in a
    /// modal message loop (MessageBox / assert dialog) never returns to a state where the queued
    /// command is processed. Falling back to DebugBreakProcess injects a break from outside, which
    /// always produces a debug event.
    /// </summary>
    public async Task<CdbResult> InterruptAsync(TimeSpan settleTimeout, CancellationToken cancellationToken)
    {
        int promptBefore;
        lock (_gate)
        {
            promptBefore = _lastPromptEnd;
        }

        var result = await ExecuteAsync(".break", TimeSpan.FromSeconds(3), cancellationToken).ConfigureAwait(false);
        if (result.PromptReturned)
        {
            return result;
        }

        if (TargetPid is null)
        {
            TargetPid = TryParsePid(Snapshot());
        }

        if (TargetPid is int pid && Native.BreakIntoProcess(pid))
        {
            var stopped = await WaitForPromptAsync(promptBefore, settleTimeout, cancellationToken).ConfigureAwait(false);
            if (stopped)
            {
                var stopEvent = await ExecuteAsync(".lastevent", TimeSpan.FromSeconds(5), cancellationToken)
                    .ConfigureAwait(false);

                // The injected break lands on the temporary remote-break thread (ntdll!DbgBreakPoint),
                // which says nothing about why the target was stuck. Dump every thread so the real
                // blocking frame (e.g. USER32!MessageBoxW) is visible immediately.
                var allThreads = await ExecuteAsync("~*k", TimeSpan.FromSeconds(15), cancellationToken)
                    .ConfigureAwait(false);

                var combined = stopEvent.Text +
                               "\n\n[all threads at the moment of the injected break]\n" +
                               allThreads.Text;

                return new CdbResult(combined, true, DebuggerState.Stopped);
            }
        }

        return result;
    }

    public async Task<CdbResult> ExecuteAsync(string command, TimeSpan timeout, CancellationToken cancellationToken)
    {
        if (HasExited)
        {
            CaptureExitCode();
            var tail = LastOutputTail();
            return new CdbResult(
                string.IsNullOrWhiteSpace(tail) ? "<debugger exited>" : tail,
                true,
                DebuggerState.Exited);
        }

        await _io.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            int start;
            lock (_gate)
            {
                start = _buffer.Length;
            }

            await _process.StandardInput.WriteAsync(command.AsMemory(), cancellationToken).ConfigureAwait(false);
            await _process.StandardInput.WriteAsync("\n".AsMemory(), cancellationToken).ConfigureAwait(false);
            await _process.StandardInput.FlushAsync(cancellationToken).ConfigureAwait(false);

            LastUsedAt = DateTimeOffset.UtcNow;

            var promptReturned = await WaitForPromptAsync(start, timeout, cancellationToken).ConfigureAwait(false);
            State = promptReturned ? DebuggerState.Stopped : DebuggerState.Running;

            string text;
            lock (_gate)
            {
                var end = promptReturned ? Math.Min(_lastPromptEnd, _buffer.Length) : _buffer.Length;
                var length = Math.Max(0, end - start);
                text = length > 0 ? _buffer.ToString(start, length) : string.Empty;
            }

            return new CdbResult(Clean(text), promptReturned, State);
        }
        finally
        {
            _io.Release();
        }
    }

    private async Task<bool> WaitForPromptAsync(int from, TimeSpan timeout, CancellationToken cancellationToken)
    {
        var deadline = DateTime.UtcNow + timeout;

        while (true)
        {
            lock (_gate)
            {
                if (_lastPromptEnd > from)
                {
                    return true;
                }
            }

            if (HasExited)
            {
                return false;
            }

            if (DateTime.UtcNow >= deadline)
            {
                return false;
            }

            try
            {
                await Task.Delay(20, cancellationToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                return false;
            }
        }
    }

    private async Task PumpAsync()
    {
        var buffer = new char[4096];
        try
        {
            while (!_cts.IsCancellationRequested)
            {
                var read = await _process.StandardOutput.ReadAsync(buffer.AsMemory(), _cts.Token).ConfigureAwait(false);
                if (read <= 0)
                {
                    break;
                }

                lock (_gate)
                {
                    var previous = _buffer.Length;
                    _buffer.Append(buffer, 0, read);

                    // Scan from a little before the new data so a prompt split across reads is still found.
                    var scanFrom = Math.Max(previous - 8, 0);
                    var slice = _buffer.ToString(scanFrom, _buffer.Length - scanFrom);
                    foreach (Match m in PromptPattern.Matches(slice))
                    {
                        var absolute = scanFrom + m.Index + m.Length;
                        if (absolute > _lastPromptEnd)
                        {
                            _lastPromptEnd = absolute;
                        }
                    }
                }
            }
        }
        catch (OperationCanceledException)
        {
            // shutting down
        }
        catch (ObjectDisposedException)
        {
            // shutting down
        }
        finally
        {
            State = DebuggerState.Exited;
            CaptureExitCode();
        }
    }

    /// <summary>
    /// cdb writes warnings and extension chatter to stderr. It must be drained, otherwise cdb blocks
    /// once the 4 KB pipe buffer fills and stops emitting prompts altogether.
    /// </summary>
    private async Task PumpErrorAsync()
    {
        var buffer = new char[2048];
        try
        {
            while (!_cts.IsCancellationRequested)
            {
                var read = await _process.StandardError.ReadAsync(buffer.AsMemory(), _cts.Token).ConfigureAwait(false);
                if (read <= 0)
                {
                    break;
                }

                lock (_gate)
                {
                    _buffer.Append(buffer, 0, read);
                }
            }
        }
        catch (OperationCanceledException)
        {
            // shutting down
        }
        catch (ObjectDisposedException)
        {
            // shutting down
        }
    }

    private static string Clean(string text)
    {
        var trimmed = text.TrimEnd();
        return trimmed.Replace("\r\n", "\n");
    }

    public async ValueTask DisposeAsync()
    {
        try
        {
            if (!HasExited)
            {
                await _io.WaitAsync(TimeSpan.FromSeconds(2)).ConfigureAwait(false);
                try
                {
                    // q kills the target; qd detaches. A bridge session owns the target, so kill it.
                    await _process.StandardInput.WriteAsync("q\n").ConfigureAwait(false);
                    await _process.StandardInput.FlushAsync().ConfigureAwait(false);
                }
                finally
                {
                    _io.Release();
                }

                using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(3));
                try
                {
                    await _process.WaitForExitAsync(timeout.Token).ConfigureAwait(false);
                }
                catch (OperationCanceledException)
                {
                    // fall through to kill
                }
            }
        }
        catch
        {
            // best effort
        }

        try
        {
            if (!_process.HasExited)
            {
                _process.Kill(entireProcessTree: true);
            }
        }
        catch
        {
            // best effort
        }

        _cts.Cancel();
        foreach (var pump in new[] { _pump, _errPump })
        {
            if (pump is null)
            {
                continue;
            }

            try
            {
                await pump.ConfigureAwait(false);
            }
            catch
            {
                // ignore
            }
        }

        _cts.Dispose();
        _io.Dispose();
        _process.Dispose();
    }
}


