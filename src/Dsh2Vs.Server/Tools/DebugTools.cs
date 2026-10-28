using System.ComponentModel;
using System.Text;
using Dsh2Vs.Core;
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
    private readonly CrashAnalyzer _analyzer;
    private readonly CrashForensics _forensics;

    public DebugTools(
        VsState state,
        DebugSessionManager debug,
        DebugOptions options,
        CrashAnalyzer analyzer,
        CrashForensics forensics)
    {
        _state = state;
        _debug = debug;
        _options = options;
        _analyzer = analyzer;
        _forensics = forensics;
    }

    [McpServerTool(Name = "dump_save")]
    [Description(
        "Write a full user-mode minidump of the current debuggee ('.dump /ma'). Call it while stopped, e.g. right " +
        "after a crash, then hand the file to crash_analyze or debug_open_dump. " +
        "Default location: %LOCALAPPDATA%\\dsh2vs\\dumps.")]
    public async Task<string> DumpSave(
        [Description("Full output path for the .dmp. Optional.")] string? path = null,
        [Description("File name only, used when path is omitted (e.g. abort-01.dmp). Optional.")] string? fileName = null)
    {
        var session = _debug.Current;
        if (session is null)
        {
            return Error("No active debug session. Call debug_launch or debug_open_dump first.");
        }

        string full;
        if (!string.IsNullOrWhiteSpace(path))
        {
            full = Path.GetFullPath(path);
        }
        else
        {
            var dir = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "dsh2vs",
                "dumps");
            Directory.CreateDirectory(dir);

            var name = string.IsNullOrWhiteSpace(fileName)
                ? $"{SafeName(Path.GetFileNameWithoutExtension(session.Target))}-{DateTime.Now:yyyyMMdd-HHmmss}.dmp"
                : fileName.EndsWith(".dmp", StringComparison.OrdinalIgnoreCase) ? fileName : fileName + ".dmp";

            full = Path.Combine(dir, name);
        }

        var parent = Path.GetDirectoryName(full);
        if (!string.IsNullOrEmpty(parent))
        {
            Directory.CreateDirectory(parent);
        }

        var result = await session
            .ExecuteAsync($".dump /ma \"{full}\"", TimeSpan.FromMinutes(2), CancellationToken.None)
            .ConfigureAwait(false);

        var info = File.Exists(full) ? new FileInfo(full) : null;

        return Json.Serialize(new
        {
            ok = info is not null,
            path = full,
            exists = info is not null,
            sizeBytes = info?.Length,
            text = Trim(result.Text, 1500),
        });
    }

    [McpServerTool(Name = "crash_analyze")]
    [Description(
        "Run WinDbg's '!analyze -v' and return exception code, faulting module, process and failure bucket. " +
        "Pass dumpPath to analyse a .dmp file in a throwaway debugger (the live session is untouched); " +
        "omit it to analyse the currently stopped session, e.g. right after an access violation.")]
    public async Task<string> CrashAnalyze(
        [Description("Path to a .dmp file. Optional.")] string? dumpPath = null)
    {
        try
        {
            CrashReport report;
            if (string.IsNullOrWhiteSpace(dumpPath))
            {
                var session = _debug.Current
                              ?? throw new InvalidOperationException(
                                  "No active debug session and no dumpPath given.");

                report = await _analyzer.AnalyzeSessionAsync(session, CancellationToken.None).ConfigureAwait(false);
            }
            else
            {
                report = await _analyzer.AnalyzeDumpAsync(dumpPath, CancellationToken.None).ConfigureAwait(false);
            }

            return Json.Serialize(new
            {
                ok = true,
                exceptionCode = report.ExceptionCode,
                exceptionName = report.ExceptionName,
                module = report.ModuleName,
                image = report.ImageName,
                process = report.ProcessName,
                failureBucket = report.FailureBucket,
                faultingSymbol = report.FaultingSymbol,
                faultingSource = report.FaultingSource,
                raw = report.Raw,
            });
        }
        catch (Exception ex)
        {
            return Error(ex);
        }
    }

    [McpServerTool(Name = "debug_open_dump")]
    [Description(
        "Open a .dmp file as the current debug session, so stack_get / locals_get / threads_get / modules_get " +
        "work against the crash. Replaces any live session.")]
    public async Task<string> DebugOpenDump(
        [Description("Path to a .dmp file.")] string path)
    {
        try
        {
            var session = await _debug.OpenDumpAsync(path, CancellationToken.None).ConfigureAwait(false);
            var last = await session.ExecuteAsync(".lastevent", TimeSpan.FromSeconds(15), CancellationToken.None)
                .ConfigureAwait(false);

            return Json.Serialize(new
            {
                ok = true,
                dump = session.Target,
                state = session.State.ToString().ToLowerInvariant(),
                stop = Trim(last.Text, 1500),
                hint = "Use crash_analyze (no argument) for the verdict, or stack_get / locals_get for details.",
            });
        }
        catch (Exception ex)
        {
            return Error(ex);
        }
    }

    [McpServerTool(Name = "exception_set")]
    [Description(
        "Control how the debugger reacts to an exception event. mode=break stops on first chance (like VS " +
        "'break when thrown'), second stops only on second chance, notify logs the first chance and keeps running, " +
        "ignore disables it. event uses cdb names: av (access violation), eh (C++ exception), clr, int3, * (all). " +
        "Example: exception_set(event='av', mode='break') gets you the fault before Windows shows any dialog.")]
    public Task<string> ExceptionSet(
        [Description("Exception event, e.g. av, eh, clr, int3, or * for all.")] string @event,
        [Description("break | second | notify | ignore")] string mode = "break")
    {
        var command = mode.ToLowerInvariant() switch
        {
            "break" or "first" => "sxe",
            "second" => "sxd",
            "notify" => "sxn",
            "ignore" => "sxi",
            _ => throw new ArgumentException("mode must be break, second, notify or ignore"),
        };

        return Run($"{command} {@event}", 15000, "exception_set");
    }

    [McpServerTool(Name = "exception_list")]
    [Description("List the current first-chance / second-chance exception settings ('sx').")]
    public Task<string> ExceptionList() => Run("sx", 20000, "exception_list");

    [McpServerTool(Name = "debug_preflight")]
    [Description(
        "Check an executable's imports before running it: reads the PE import table and resolves every DLL " +
        "(image directory first, then the normal search path). Returns the missing ones. " +
        "This is the fast way to explain 'the program exits immediately with no output' - " +
        "STATUS_DLL_NOT_FOUND (0xC0000135) means a dependency was never found but nothing printed.")]
    public string DebugPreflight(
        [Description("Absolute or workspace-relative path to the .exe.")] string executable,
        [Description("Workspace name used to resolve a relative path. Optional.")] string? workspace = null)
    {
        try
        {
            var full = _state.ResolvePath(executable, workspace);
            var (resolved, missing) = DependencyResolver.Check(full);

            return Json.Serialize(new
            {
                ok = missing.Count == 0,
                executable = full,
                importedCount = resolved.Count + missing.Count,
                missingCount = missing.Count,
                missing,
                diagnosis = missing.Count == 0
                    ? "all imports resolve"
                    : $"missing dependency DLL(s): {string.Join(", ", missing)} - copy them next to the exe "
                      + "or add their directory to PATH; the program cannot start until then",
            });
        }
        catch (Exception ex)
        {
            return Error(ex);
        }
    }

    [McpServerTool(Name = "debug_launch")]
    [Description(
        "Start a C++ executable under the cdb debugger and stop at the initial breakpoint, so breakpoints can be set " +
        "before running. The target must live inside the workspace unless AllowTargetsOutsideWorkspace is configured. " +
        "Typical flow: debug_launch -> breakpoint_set -> debug_continue.")]
    public async Task<string> DebugLaunch(
        [Description("Absolute or workspace-relative path to the .exe.")] string executable,
        [Description("Command-line arguments for the target, as one string. Optional.")] string? arguments = null,
        [Description("Working directory for the target. Optional.")] string? workingDirectory = null,
        [Description("Workspace name (from workspace_list) used to resolve a relative path. Optional.")] string? workspace = null)
    {
        try
        {
            var target = _state.ResolvePath(executable, workspace);

            var session = await _debug.LaunchAsync(
                target,
                SplitArguments(arguments),
                workingDirectory,
                CancellationToken.None).ConfigureAwait(false);

            var stop = await session.ExecuteAsync(".lastevent", TimeSpan.FromSeconds(10), CancellationToken.None)
                .ConfigureAwait(false);

            // A program that fails to start (missing DLL) dies before any of our commands reach it.
            // Report that as a failure with the reason, never as "ok + exited".
            if (session.HasExited)
            {
                var code = session.ExitCode;
                return Json.Serialize(new
                {
                    ok = false,
                    target,
                    state = "exited",
                    exitCode = code,
                    exitCodeHex = code is int c ? $"0x{c:X8}" : null,
                    diagnosis = DescribeExit(code),
                    imports = Preflight(target),
                    output = Trim(stop.Text, 2000),
                    hint = code == unchecked((int)0xC0000135)
                        ? "A dependency DLL is missing. Use debug_preflight to list the exact names, then copy them "
                          + "next to the exe (or add their folder to PATH)."
                        : "The program exited before reaching the initial breakpoint; output above is what the debugger saw.",
                });
            }

            return Json.Serialize(new
            {
                ok = true,
                target,
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
            targetPid = session?.TargetPid,
            engine = _options.Engine,
            state = session?.State.ToString().ToLowerInvariant() ?? "idle",
            idleSeconds = session is null ? (int?)null : (int)(DateTimeOffset.UtcNow - session.LastUsedAt).TotalSeconds,
            idleTimeoutMinutes = _options.IdleTimeoutMinutes,
        });
    }

    [McpServerTool(Name = "debug_break")]
    [Description(
        "Interrupt a running target and stop it. Works even when the target is blocked in a modal dialog " +
        "(MessageBox / assert): if the queued .break is not processed, the bridge injects a break from outside. " +
        "It then selects the thread that is actually blocked (an injected break lands on a throwaway thread), " +
        "returns that thread's stack, and says what the target is waiting on.")]
    public async Task<string> DebugBreak()
    {
        var session = _debug.Current;
        if (session is null)
        {
            return Error("No active debug session. Call debug_launch or debug_attach first.");
        }

        var result = await session
            .InterruptAsync(TimeSpan.FromSeconds(10), CancellationToken.None)
            .ConfigureAwait(false);

        // All thread stacks: the injected break sits in ntdll!DbgUiRemoteBreakin, which says nothing.
        var all = await session.ExecuteAsync("~*k 12", TimeSpan.FromSeconds(20), CancellationToken.None)
            .ConfigureAwait(false);

        var threads = ThreadStackParser.Parse(all.Text);
        var picked = ThreadStackParser.PickInteresting(threads, result.State == DebuggerState.Stopped ? 0 : null);

        string stack = string.Empty;
        if (picked is int threadIndex)
        {
            var switched = await session
                .ExecuteAsync($"~{threadIndex}s; kb 12", TimeSpan.FromSeconds(15), CancellationToken.None)
                .ConfigureAwait(false);
            stack = switched.Text;
        }

        ModalFinding? diagnosis = null;
        if (session.TargetPid is int pid && result.State == DebuggerState.Stopped)
        {
            diagnosis = ModalDiagnosis.From(WindowProbe.Inspect(pid));
        }

        return Json.Serialize(new
        {
            ok = true,
            action = "break",
            state = result.State.ToString().ToLowerInvariant(),
            promptReturned = result.PromptReturned,
            targetPid = session.TargetPid,
            threadCount = threads.Count,
            selectedThread = picked,
            selectedThreadNote = "stack_get / locals_get now follow this thread; switch with debug_thread_switch",
            diagnosis = diagnosis is null
                ? null
                : new
                {
                    kind = diagnosis.Kind,
                    windowTitle = diagnosis.WindowTitle,
                    assertion = diagnosis.AssertionText,
                    summary = diagnosis.Summary,
                },
            stack = Trim(stack, 6000),
            threads = threads.Select(t => new
            {
                index = t.Index,
                id = t.SystemId,
                top = t.Symbols.FirstOrDefault(),
            }),
        });
    }

    [McpServerTool(Name = "debug_thread_switch")]
    [Description(
        "Select a thread ('~Ns') so that stack_get / locals_get / evaluate act on it. " +
        "Thread indices come from debug_break (or threads_get).")]
    public async Task<string> DebugThreadSwitch(
        [Description("Thread index as shown by threads_get / debug_break.")] int threadIndex)
    {
        var session = _debug.Current;
        if (session is null)
        {
            return Error("No active debug session. Call debug_launch or debug_attach first.");
        }

        var result = await session
            .ExecuteAsync($"~{threadIndex}s; .echo selected", TimeSpan.FromSeconds(10), CancellationToken.None)
            .ConfigureAwait(false);

        return Json.Serialize(new
        {
            ok = result.PromptReturned,
            selectedThread = threadIndex,
            state = result.State.ToString().ToLowerInvariant(),
            text = Trim(result.Text, 600),
        });
    }

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
    public async Task<string> StackGet(
        [Description("Maximum frames. Default 16.")] int frames = 16,
        [Description("Thread index to read (see threads_get / debug_break). Default: current thread.")]
        int? threadId = null)
    {
        var command = threadId is int t
            ? $"~{t}s; kb {Math.Clamp(frames, 1, 200)}"
            : $"kb {Math.Clamp(frames, 1, 200)}";

        var (ok, text, error) = await ExecuteAsync(command, 30000).ConfigureAwait(false);
        if (!ok)
        {
            return Error(error);
        }

        return Json.Serialize(new
        {
            ok = true,
            threadId = threadId,
            frames = CdbParsers.ParseStack(text),
            text = Trim(text, 6000),
        });
    }

    [McpServerTool(Name = "locals_get")]
    [Description("Show local variables of the current frame with their types ('dv /t /v').")]
    public Task<string> LocalsGet(
        [Description("Thread index to read. Default: current thread.")] int? threadId = null)
        => Run(threadId is int t ? $"~{t}s; dv /t /v" : "dv /t /v", 15000, "locals_get");

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

        var session = _debug.Current;
        var exited = session?.HasExited == true || state == "exited";
        var code = session?.ExitCode;

        // A crash should arrive with its evidence attached, not as a stack the caller has to interpret.
        object? forensics = null;
        if (_options.AutoForensics &&
            session is not null &&
            !exited &&
            state == "stopped" &&
            action is "continue" or "step" or "debug_execute" &&
            IsCrashStop(text))
        {
            var captured = await _forensics.CaptureAsync(session, text, CancellationToken.None).ConfigureAwait(false);
            if (captured is not null)
            {
                forensics = new
                {
                    exceptionCode = captured.Report.ExceptionCode,
                    exceptionName = captured.Report.ExceptionName,
                    faultingSymbol = captured.Report.FaultingSymbol,
                    module = captured.Report.ModuleName,
                    dumpPath = captured.DumpPath,
                    reportPath = captured.ReportPath,
                };
            }
        }

        return Json.Serialize(new
        {
            ok = true,
            action,
            state,
            promptReturned,
            exitCode = exited ? code : null,
            exitCodeHex = exited && code is int c ? $"0x{c:X8}" : null,
            diagnosis = exited ? DescribeExit(code) : DiagnoseStuck(state),
            forensics,
            text = Trim(text, MaxText),
        });
    }

    /// <summary>
    /// When a run command times out, the process is usually fine and simply busy - or it is sitting in a
    /// modal dialog nobody can answer. Say which, instead of leaving the caller to guess.
    /// </summary>
    private object? DiagnoseStuck(string state)
    {
        if (!string.Equals(state, "running", StringComparison.OrdinalIgnoreCase))
        {
            return null;
        }

        if (_debug.Current?.TargetPid is not int pid)
        {
            return null;
        }

        var diagnosis = ModalDiagnosis.From(WindowProbe.Inspect(pid));
        if (diagnosis is null)
        {
            return new
            {
                kind = "no-dialog",
                summary = "target is still running and no dialog window is visible: it may simply be busy or "
                          + "waiting on I/O. Use debug_break to see where it is.",
            };
        }

        return new
        {
            kind = diagnosis.Kind,
            windowTitle = diagnosis.WindowTitle,
            assertion = diagnosis.AssertionText,
            summary = diagnosis.Summary,
        };
    }

    /// <summary>
    /// True when the stop looks like an exception rather than a breakpoint. 0x80000003 is the
    /// loader breakpoint and explicitly not a crash.
    /// </summary>
    private static bool IsCrashStop(string text)
    {
        if (text.Contains("Breakpoint", StringComparison.OrdinalIgnoreCase) &&
            !text.Contains("second chance", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        if (text.Contains("(second chance)", StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        foreach (System.Text.RegularExpressions.Match match in System.Text.RegularExpressions.Regex.Matches(
                     text, @"code (?<code>[0-9a-fA-F]{8})"))
        {
            if (!match.Groups["code"].Value.Equals("80000003", StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }

        return text.Contains("Access violation", StringComparison.OrdinalIgnoreCase);
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

    /// <summary>Turns a debuggee exit code into something actionable instead of "it exited".</summary>
    private static string? DescribeExit(int? code) => code switch
    {
        null => null,
        0 => "the program ran and exited normally",
        unchecked((int)0xC0000135) => "STATUS_DLL_NOT_FOUND: a dependency DLL is missing, the program never started",
        unchecked((int)0xC0000142) => "STATUS_DLL_INIT_FAILED: a dependency DLL failed to initialize",
        unchecked((int)0xC0000409) => "STATUS_STACK_BUFFER_OVERRUN: /GS, abort() or __fastfail",
        unchecked((int)0xC0000005) => "STATUS_ACCESS_VIOLATION",
        _ => null,
    };

    /// <summary>Missing-import report, used to explain a 0xC0000135 start failure on the spot.</summary>
    private static object? Preflight(string executable)
    {
        try
        {
            var (resolved, missing) = DependencyResolver.Check(executable);
            return new
            {
                importedCount = resolved.Count + missing.Count,
                missingCount = missing.Count,
                missing,
            };
        }
        catch
        {
            return null;
        }
    }

    private static string SafeName(string name)
    {
        foreach (var c in Path.GetInvalidFileNameChars())
        {
            name = name.Replace(c, '_');
        }

        return string.IsNullOrWhiteSpace(name) ? "target" : name;
    }

    private static string Error(string? message) => Json.Serialize(new { ok = false, error = message });

    private static string Error(Exception ex) => Error(ex.Message);
}




