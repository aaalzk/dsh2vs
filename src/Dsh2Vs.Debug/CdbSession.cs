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
public sealed class CdbSession : IAsyncDisposable
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

    public DateTimeOffset LastUsedAt { get; private set; }

    public DebuggerState State { get; private set; } = DebuggerState.Running;

    public bool HasExited => _process.HasExited;

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
        return session;
    }

    public async Task<CdbResult> ExecuteAsync(string command, TimeSpan timeout, CancellationToken cancellationToken)
    {
        if (HasExited)
        {
            return new CdbResult("<debugger exited>", true, DebuggerState.Exited);
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
