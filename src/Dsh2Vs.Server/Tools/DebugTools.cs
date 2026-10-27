using System.ComponentModel;
using System.Text;
using System.Text.Json;
using Dsh2Vs.Debug;
using ModelContextProtocol.Server;

namespace Dsh2Vs.Server.Tools;

[McpServerToolType]
public sealed class DebugTools
{
    private const int MaxText = 20000;

    private readonly VsState _state;
    private readonly DebugSessionManager _debug;
    private readonly DebugOptions _options;

    public DebugTools(VsState state, DebugSessionManager debug, DebugOptions options)
    {
        _state = state;
        _debug = debug;
        _options = options;
    }

    [McpServerTool(Name = "debug_launch")]
    [Description(
        "Start a C++ executable under the cdb debugger and stop at the initial breakpoint, so breakpoints can be set " +
        "before running. The target must live inside the workspace unless AllowTargetsOutsideWorkspace is configured. " +
        "Typical flow: debug_launch -> breakpoint_set -> debug_continue.")]
    public async Task<string> DebugLaunch(
        [Description("Absolute or workspace-relative path to the .exe.")] string executable,
        [Description("Command-line arguments for the target, as one string. Optional.")] string? arguments = null,
        [Description("Working directory for the target. Optional.")] string? workingDirectory = null)
    {
        try
        {
            var session = await _debug.LaunchAsync(
                ResolvePath(executable),
                SplitArguments(arguments),
                workingDirectory,
                CancellationToken.None).ConfigureAwait(false);

            var stop = await session.ExecuteAsync(".lastevent", TimeSpan.FromSeconds(10), CancellationToken.None)
                .ConfigureAwait(false);

            return Json.Serialize(new
            {
                ok = true,
                target = session.Target,
                state = session.State.ToString().ToLowerInvariant(),
                stop = Trim(stop.Text, 1200),
                hint = "Set breakpoints with breakpoint_set, then call debug_continue.",
            });
        }
        catch (Exception ex)
        {
            return Error(ex);
        }
    }

    [McpServerTool(Name = "debug_attach")]
    [Description(
        "Attach the debugger to a running process by PID. The process image must be inside the workspace unless " +
        "AllowTargetsOutsideWorkspace is configured; attaching to protected system processes also needs an elevated bridge.")]
    public async Task<string> DebugAttach([Description("Process id to attach to.")] int pid)
    {
        try
        {
            var session = await _debug.AttachAsync(pid, CancellationToken.None).ConfigureAwait(false);
            var stop = await session.ExecuteAsync(".lastevent", TimeSpan.FromSeconds(10), CancellationToken.None)
                .ConfigureAwait(false);

            return Json.Serialize(new
            {
                ok = true,
                target = session.Target,
                pid,
                state = session.State.ToString().ToLowerInvariant(),
                stop = Trim(stop.Text, 1200),
            });
        }
        catch (Exception ex)
        {
            return Error(ex);
        }
    }

    [McpServerTool(Name = "debug_stop")]
    [Description("Stop debugging: terminates the debuggee (when launched by the bridge) and closes the session.")]
    public async Task<string> DebugStop()
    {
        var stopped = await _debug.StopAsync().ConfigureAwait(false);
        return Json.Serialize(new { ok = true, stopped, state = "idle" });
    }

    [McpServerTool(Name = "debug_status")]
    [Description("Report the debug session: cdb path, target, state (stopped/running), idle time and idle timeout.")]
    public string DebugStatus()
    {
        var session = _debug.Current;

        return Json.Serialize(new
        {
            ok = true,
            cdb = _debug.CdbPath,
            active = session is not null,
            target = session?.Target,
            state = session?.State.ToString().ToLowerInvariant() ?? "idle",
            idleSeconds = session is null ? (int?)null : (int)(DateTimeOffset.UtcNow - session.LastUsedAt).TotalSeconds,
            idleTimeoutMinutes = _options.IdleTimeoutMinutes,
        });
    }

    [McpServerTool(Name = "debug_break")]
    [Description("Interrupt a running target and wait until it stops.")]
    public Task<string> DebugBreak() =>
        Run(".break", 15000, "break");

    [McpServerTool(Name = "debug_continue")]
    [Description(
        "Continue execution until the target stops (breakpoint, exception or exit) or the timeout elapses. " +
        "state=running means the target is still executing.")]
    public Task<string> DebugContinue(
        [Description("How long to wait for a stop event, in milliseconds. Default 15000.")] int timeoutMs = 15000)
        => Run("g", Math.Clamp(timeoutMs, 500, 600000), "continue");

    [McpServerTool(Name = "debug_step")]
    [Description("Step the stopped target: kind = into (t), over (p) or out (gu).")]
    public Task<string> DebugStep([Description("into | over | out")] string kind = "over")
    {
        var command = kind.ToLowerInvariant() switch
        {
            "into" => "t",
            "out" => "gu",
            "over" => "p",
            _ => throw new ArgumentException("kind must be into, over or out"),
        };

        return Run(command, _options.CommandTimeoutSeconds * 1000, "step");
    }

    [McpServerTool(Name = "breakpoint_set")]
    [Description(
        "Set a breakpoint. spec uses cdb syntax: 'file.cpp:42', 'module!function' or an address. " +
        "condition is an optional C++ expression that must be true to break. If the symbol is not loaded yet " +
        "the breakpoint is created as a deferred (bu) breakpoint, which resolves when the module loads.")]
    public async Task<string> BreakpointSet(
        [Description("Breakpoint location, for example loopy.cpp:14 or loopy!main")] string spec,
        [Description("Optional break condition, for example counter > 2")] string? condition = null)
    {
        var conditionSuffix = string.IsNullOrWhiteSpace(condition) ? string.Empty : $" \"{condition}\"";

        var direct = await Run($"bp {spec}{conditionSuffix}", _options.CommandTimeoutSeconds * 1000, "breakpoint_set")
            .ConfigureAwait(false);

        if (!direct.Contains("Couldn't resolve", StringComparison.OrdinalIgnoreCase))
        {
            return direct;
        }

        var deferred = await Run($"bu {spec}{conditionSuffix}", _options.CommandTimeoutSeconds * 1000, "breakpoint_set")
            .ConfigureAwait(false);

        return Json.Serialize(new
        {
            ok = true,
            action = "breakpoint_set",
            mode = "deferred",
            note = "Symbol was not loaded yet; created a deferred breakpoint that resolves when the module loads.",
            text = Trim(deferred, 4000),
        });
    }

    [McpServerTool(Name = "breakpoint_list")]
    [Description("List breakpoints with ids, state and hit counts ('bl').")]
    public Task<string> BreakpointList() =>
        Run("bl", 15000, "breakpoint_list");

    [McpServerTool(Name = "breakpoint_remove")]
    [Description("Remove a breakpoint by id, or pass 'all' to clear every breakpoint.")]
    public Task<string> BreakpointRemove([Description("Breakpoint id, or 'all'")] string id) =>
        Run($"bc {(id.Equals("all", StringComparison.OrdinalIgnoreCase) ? "*" : id)}", 15000, "breakpoint_remove");

    [McpServerTool(Name = "stack_get")]
    [Description("Return the call stack as structured frames: symbol, source file and line.")]
    public async Task<string> StackGet([Description("Maximum frames. Default 16.")] int frames = 16)
    {
        var (ok, text, error) = await ExecuteAsync($"kb {Math.Clamp(frames, 1, 200)}", 30000).ConfigureAwait(false);
        if (!ok)
        {
            return Error(error);
        }

        return Json.Serialize(new
        {
            ok = true,
            frames = CdbParsers.ParseStack(text),
            text = Trim(text, 6000),
        });
    }

    [McpServerTool(Name = "locals_get")]
    [Description("Show local variables of the current frame with their types ('dv /t /v').")]
    public Task<string> LocalsGet() =>
        Run("dv /t /v", 15000, "locals_get");

    [McpServerTool(Name = "evaluate")]
    [Description("Evaluate a C++ expression in the current frame ('??'). Returns the parsed value plus raw output.")]
    public async Task<string> Evaluate(
        [Description("C++ expression, for example counter + 1 or mathutil::add(1, 2)")] string expression)
    {
        var (ok, text, error) = await ExecuteAsync($"?? {expression}", 15000).ConfigureAwait(false);
        if (!ok)
        {
            return Error(error);
        }

        return Json.Serialize(new
        {
            ok = true,
            value = CdbParsers.ParseEvaluation(text),
            text = Trim(text, 2000),
        });
    }

    [McpServerTool(Name = "threads_get")]
    [Description("List the target's threads ('~'); the current thread is marked with a dot.")]
    public Task<string> ThreadsGet() =>
        Run("~", 15000, "threads_get");

    [McpServerTool(Name = "modules_get")]
    [Description("List loaded modules, optionally filtered by name (e.g. 'loopy').")]
    public async Task<string> ModulesGet([Description("Optional module name filter.")] string? filter = null)
    {
        var command = string.IsNullOrWhiteSpace(filter) ? "lm" : $"lm m {filter}";
        var (ok, text, error) = await ExecuteAsync(command, 20000).ConfigureAwait(false);
        if (!ok)
        {
            return Error(error);
        }

        var modules = CdbParsers.ParseModules(text);
        return Json.Serialize(new { ok = true, count = modules.Count, modules });
    }

    [McpServerTool(Name = "memory_read")]
    [Description("Read memory at an address and return a hex dump ('db').")]
    public Task<string> MemoryRead(
        [Description("Address or symbol, for example 0x000000d12b9ff838")] string address,
        [Description("Bytes to dump. Default 64.")] int length = 64)
        => Run($"db {address} L?{Math.Clamp(length, 1, 4096)}", 15000, "memory_read");

    [McpServerTool(Name = "debug_execute")]
    [Description(
        "Escape hatch: run a raw cdb command and return its output. Use it for commands without a typed tool, " +
        "such as '!analyze -v', '.exr -1', 'k' or 'x loopy!*'.")]
    public Task<string> DebugExecute([Description("Raw cdb command line.")] string command) =>
        Run(command, _options.CommandTimeoutSeconds * 1000, "debug_execute");

    private async Task<string> Run(string command, int timeoutMs, string action)
    {
        var (ok, text, error, state, promptReturned) = await ExecuteDetailedAsync(command, timeoutMs).ConfigureAwait(false);
        if (!ok)
        {
            return Error(error);
        }

        return Json.Serialize(new
        {
            ok = true,
            action,
            state,
            promptReturned,
            text = Trim(text, MaxText),
        });
    }

    private async Task<(bool Ok, string Text, string? Error)> ExecuteAsync(string command, int timeoutMs)
    {
        var (ok, text, error, _, _) = await ExecuteDetailedAsync(command, timeoutMs).ConfigureAwait(false);
        return (ok, text, error);
    }

    private async Task<(bool Ok, string Text, string? Error, string State, bool PromptReturned)> ExecuteDetailedAsync(
        string command,
        int timeoutMs)
    {
        var session = _debug.Current;
        if (session is null)
        {
            return (false, string.Empty, "No active debug session. Call debug_launch or debug_attach first.", "idle", false);
        }

        var result = await session
            .ExecuteAsync(command, TimeSpan.FromMilliseconds(Math.Max(500, timeoutMs)), CancellationToken.None)
            .ConfigureAwait(false);

        return (true, result.Text, null, result.State.ToString().ToLowerInvariant(), result.PromptReturned);
    }

    private string ResolvePath(string path)
    {
        if (Path.IsPathRooted(path))
        {
            return Path.GetFullPath(path);
        }

        var root = _state.Options.WorkspaceRoot ?? Environment.CurrentDirectory;
        return Path.GetFullPath(Path.Combine(root, path));
    }

    private static IReadOnlyList<string> SplitArguments(string? arguments)
    {
        if (string.IsNullOrWhiteSpace(arguments))
        {
            return [];
        }

        var result = new List<string>();
        var current = new StringBuilder();
        var inQuotes = false;

        foreach (var ch in arguments)
        {
            if (ch == '"')
            {
                inQuotes = !inQuotes;
                continue;
            }

            if (ch == ' ' && !inQuotes)
            {
                if (current.Length > 0)
                {
                    result.Add(current.ToString());
                    current.Clear();
                }

                continue;
            }

            current.Append(ch);
        }

        if (current.Length > 0)
        {
            result.Add(current.ToString());
        }

        return result;
    }

    private static string Trim(string text, int max) =>
        text.Length <= max ? text : text[..max] + "\n... (truncated)";

    private static string Error(string? message) => Json.Serialize(new { ok = false, error = message });

    private static string Error(Exception ex) => Error(ex.Message);
}
