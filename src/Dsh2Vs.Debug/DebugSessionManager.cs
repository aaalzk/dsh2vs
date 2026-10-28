using System.Diagnostics;
using System.Text.RegularExpressions;
using Dsh2Vs.Core;

namespace Dsh2Vs.Debug;

public sealed class DebugSessionManager : IAsyncDisposable
{
    private readonly DebugOptions _debug;
    private readonly Dsh2VsOptions _options;
    private readonly WorkspaceRegistry _workspaces;
    private readonly CdbLocator _locator;
    private readonly SemaphoreSlim _gate = new(1, 1);
    private readonly Timer _idleTimer;

    private IDebugSession? _session;

    public DebugSessionManager(DebugOptions debug, Dsh2VsOptions options, WorkspaceRegistry workspaces, CdbLocator locator)
    {
        _debug = debug;
        _options = options;
        _workspaces = workspaces;
        _locator = locator;
        _idleTimer = new Timer(_ => _ = ReapIdleAsync(), null, TimeSpan.FromSeconds(30), TimeSpan.FromSeconds(30));
    }

    public IDebugSession? Current => _session;

    private bool UseDbgEng => string.Equals(_debug.Engine, "dbgeng", StringComparison.OrdinalIgnoreCase);

    public string CdbPath => _locator.Resolve();

    public async Task<IDebugSession> LaunchAsync(
        string executable,
        IReadOnlyList<string> targetArguments,
        string? workingDirectory,
        CancellationToken cancellationToken)
    {
        var full = Path.GetFullPath(executable);
        if (!File.Exists(full))
        {
            throw new FileNotFoundException($"Executable not found: {full}");
        }

        EnsureInsideWorkspace(full, "launch");

        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            await StopInternalAsync().ConfigureAwait(false);

            IDebugSession session;
            if (UseDbgEng)
            {
                session = await DbgEngSession
                    .StartProcessAsync(full, targetArguments, workingDirectory, cancellationToken)
                    .ConfigureAwait(false);
            }
            else
            {
                var cdbArgs = new List<string> { "-o", "-G", full };
                cdbArgs.AddRange(targetArguments);

                session = await CdbSession.StartAsync(
                    _locator.Resolve(),
                    cdbArgs,
                    full,
                    _debug.CommandTimeoutSeconds,
                    _debug.SymbolPath,
                    cancellationToken).ConfigureAwait(false);

                if (!string.IsNullOrWhiteSpace(workingDirectory))
                {
                    var cwd = Path.GetFullPath(workingDirectory);
                    await session.ExecuteAsync($".cd \"{cwd}\"", TimeSpan.FromSeconds(10), cancellationToken).ConfigureAwait(false);
                }
            }

            _session = session;
            return session;
        }
        finally
        {
            _gate.Release();
        }
    }

    /// <summary>Opens a dump file as the current session, so stack/locals/threads work on it.</summary>
    public async Task<IDebugSession> OpenDumpAsync(string dumpPath, CancellationToken cancellationToken)
    {
        var full = Path.GetFullPath(dumpPath);
        if (!File.Exists(full))
        {
            throw new FileNotFoundException($"Dump not found: {full}");
        }

        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            await StopInternalAsync().ConfigureAwait(false);

            IDebugSession session = UseDbgEng
                ? await DbgEngSession.OpenDumpAsync(full, cancellationToken).ConfigureAwait(false)
                : await CdbSession.StartAsync(
                    _locator.Resolve(),
                    ["-z", full],
                    full,
                    _debug.CommandTimeoutSeconds,
                    _debug.SymbolPath,
                    cancellationToken).ConfigureAwait(false);

            _session = session;
            return session;
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task<IDebugSession> AttachAsync(int pid, CancellationToken cancellationToken)
    {
        var image = ProcessImagePath(pid);
        if (image is null)
        {
            throw new InvalidOperationException(
                $"Cannot read the image path of process {pid}. It may be protected or already exited.");
        }

        EnsureInsideWorkspace(image, "attach");

        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            await StopInternalAsync().ConfigureAwait(false);

            IDebugSession session = UseDbgEng
                ? await DbgEngSession.AttachAsync(pid, image, cancellationToken).ConfigureAwait(false)
                : await CdbSession.StartAsync(
                    _locator.Resolve(),
                    ["-o", "-G", "-p", pid.ToString()],
                    image,
                    _debug.CommandTimeoutSeconds,
                    _debug.SymbolPath,
                    cancellationToken).ConfigureAwait(false);

            _session = session;
            return session;
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task<bool> StopAsync()
    {
        await _gate.WaitAsync().ConfigureAwait(false);
        try
        {
            return await StopInternalAsync().ConfigureAwait(false);
        }
        finally
        {
            _gate.Release();
        }
    }

    private async Task<bool> StopInternalAsync()
    {
        var session = _session;
        if (session is null)
        {
            return false;
        }

        _session = null;
        try
        {
            await session.DisposeAsync().ConfigureAwait(false);
        }
        catch
        {
            // the session is gone either way
        }

        return true;
    }

    private async Task ReapIdleAsync()
    {
        var timeout = TimeSpan.FromMinutes(Math.Max(1, _debug.IdleTimeoutMinutes));
        var session = _session;
        if (session is null)
        {
            return;
        }

        if (DateTimeOffset.UtcNow - session.LastUsedAt < timeout)
        {
            return;
        }

        try
        {
            await StopAsync().ConfigureAwait(false);
        }
        catch
        {
            // ignore
        }
    }

    private void EnsureInsideWorkspace(string path, string action)
    {
        if (_debug.AllowTargetsOutsideWorkspace)
        {
            return;
        }

        if (_workspaces.Contains(path))
        {
            return;
        }

        var known = string.Join(", ", _workspaces.All.Select(w => $"{w.Name}={w.Path}"));
        throw new InvalidOperationException(
            $"Refusing to {action} '{path}': it is outside every configured workspace ({known}). " +
            "Add it to the workspaces file, or set Dsh2Vs:Debug:AllowTargetsOutsideWorkspace=true.");
    }

    private static string? ProcessImagePath(int pid)
    {
        try
        {
            using var process = Process.GetProcessById(pid);
            return process.MainModule?.FileName;
        }
        catch
        {
            return null;
        }
    }

    public async ValueTask DisposeAsync()
    {
        await _idleTimer.DisposeAsync().ConfigureAwait(false);
        await _gate.WaitAsync().ConfigureAwait(false);
        try
        {
            await StopInternalAsync().ConfigureAwait(false);
        }
        finally
        {
            _gate.Release();
            _gate.Dispose();
        }
    }
}

/// <summary>Tolerant parsers for the handful of cdb outputs the tools expose as structured data.</summary>
public static partial class CdbParsers
{
    // cdb prints two shapes:
    //   full     "00 000000d1`2b9ff838 00007ff6`54061077 : args : sym [file @ line]"
    //   compact  "00007ff6`54061f89 : args : sym"
    // so the frame index and the Child-SP column are both optional.
    private static readonly Regex StackLine = new(
        @"^\s*(?:(?<idx>[0-9a-f]{2})\s+)?(?:(?<sp>[0-9a-f`]{8,})\s+)?(?<ret>[0-9a-f`]{8,})\s+:\s+.*?:\s+(?<sym>[^\[\s]+)(?:\s+\[(?<src>[^\]]+)\])?\s*$",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);

    private static readonly Regex SourceInfo = new(
        @"^(?<file>.+?)\s+@\s+(?<line>\d+)(?:\+(?<offset>\d+))?$",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);

    private static readonly Regex ModuleLine = new(
        @"^(?<start>[0-9a-f`]{8,})\s+(?<end>[0-9a-f`]{8,})\s+(?<name>\S+)",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);

    public static IReadOnlyList<object> ParseStack(string text)
    {
        var frames = new List<object>();
        foreach (var line in text.Split('\n'))
        {
            var m = StackLine.Match(line);
            if (!m.Success)
            {
                continue;
            }

            string? file = null;
            int? lineNo = null;
            if (m.Groups["src"].Success)
            {
                var s = SourceInfo.Match(m.Groups["src"].Value.Trim());
                if (s.Success)
                {
                    file = s.Groups["file"].Value.Trim();
                    if (int.TryParse(s.Groups["line"].Value, out var ln))
                    {
                        lineNo = ln;
                    }
                }
                else
                {
                    file = m.Groups["src"].Value.Trim();
                }
            }

            frames.Add(new
            {
                frame = m.Groups["idx"].Success ? Convert.ToInt32(m.Groups["idx"].Value, 16) : frames.Count,
                returnAddress = m.Groups["ret"].Value,
                symbol = m.Groups["sym"].Value,
                file,
                line = lineNo,
            });
        }

        return frames;
    }

    public static IReadOnlyList<object> ParseModules(string text)
    {
        var modules = new List<object>();
        foreach (var line in text.Split('\n'))
        {
            var m = ModuleLine.Match(line.TrimEnd());
            if (!m.Success)
            {
                continue;
            }

            modules.Add(new
            {
                name = m.Groups["name"].Value,
                start = m.Groups["start"].Value,
                end = m.Groups["end"].Value,
            });
        }

        return modules;
    }

    /// <summary>
    /// Extracts the value from cdb output. "? expr" prints "Evaluate expression: 42 = 00000000`0000002a";
    /// "?? expr" prints just the typed value, so fall back to the first non-empty line.
    /// </summary>
    public static string? ParseEvaluation(string text)
    {
        var idx = text.IndexOf("Evaluate expression:", StringComparison.Ordinal);
        if (idx >= 0)
        {
            var line = text[idx..].Split('\n')[0];
            var eq = line.IndexOf('=');
            return eq > 0 ? line[(eq + 1)..].Trim() : line.Trim();
        }

        var first = text.Split('\n')
            .Select(l => l.Trim())
            .FirstOrDefault(l => l.Length > 0 && !PromptOnly.IsMatch(l));
        return string.IsNullOrEmpty(first) ? null : first;
    }

    private static readonly Regex PromptOnly = new(@"^\d+:\d+>$", RegexOptions.Compiled);
}

