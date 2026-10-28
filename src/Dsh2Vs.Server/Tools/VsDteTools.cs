using System.ComponentModel;
using Dsh2Vs.Core;
using Dsh2Vs.Debug;
using Dsh2Vs.Server.VsDte;
using ModelContextProtocol.Server;

namespace Dsh2Vs.Server.Tools;

/// <summary>
/// M4 channel: talk to the user's own Visual Studio instance through DTE.
/// Everything here assumes VS is already running; when it is not, the tools say so plainly.
/// </summary>
[McpServerToolType]
public sealed class VsDteTools
{
    private const int MaxErrors = 200;

    private readonly VsState _state;
    private readonly VsDteClient _dte;
    private readonly DebugSessionManager _debug;

    public VsDteTools(VsState state, VsDteClient dte, DebugSessionManager debug)
    {
        _state = state;
        _dte = dte;
        _debug = debug;
    }

    [McpServerTool(Name = "vs_dte_status")]
    [Description(
        "Report the user's running Visual Studio instance via DTE: solution path, project count, " +
        "build state and debugger mode. Use this first to check that VS is running.")]
    public Task<string> Status() => Run("status");

    [McpServerTool(Name = "vs_dte_open")]
    [Description(
        "Open a solution in the user's Visual Studio window (so they can watch or edit). " +
        "This changes what VS currently has open.")]
    public async Task<string> Open(
        [Description("Absolute or workspace-relative path to a .sln.")] string path,
        [Description("Workspace name used to resolve a relative path. Optional.")] string? workspace = null)
    {
        try
        {
            var full = _state.ResolvePath(path, workspace);
            return await Run("open", full).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            return Error(ex.Message);
        }
    }

    [McpServerTool(Name = "vs_dte_build")]
    [Description(
        "Build the solution that is currently open in Visual Studio, using VS itself (DTE), " +
        "and return VS's own build result. For unattended work prefer the plain 'build' tool.")]
    public Task<string> Build(
        [Description("How long to wait for VS to finish building. Default 600 seconds.")] int timeoutSeconds = 600)
        => Run("build", timeoutSeconds: Math.Clamp(timeoutSeconds, 30, 3600));

    [McpServerTool(Name = "vs_dte_errors")]
    [Description("Read Visual Studio's Error List (warnings + errors) as structured items.")]
    public async Task<string> Errors()
    {
        var result = await _dte.RunAsync("errors").ConfigureAwait(false);
        return result.Json;
    }

    [McpServerTool(Name = "vs_dte_output")]
    [Description(
        "Read Visual Studio's Output window panes (Build / Debug / Tests ...). Returns the tail of each pane " +
        "so you can see what VS printed, including messages from libraries that do not go through the bridge.")]
    public async Task<string> Output(
        [Description("Only this pane (case-insensitive substring), e.g. 'Build'. Optional.")] string? pane = null,
        [Description("Max characters per pane. Default 6000.")] int maxChars = 6000)
    {
        try
        {
            var result = await _dte
                .RunAsync("output", pane: pane, maxChars: Math.Clamp(maxChars, 200, 100000))
                .ConfigureAwait(false);

            return result.Json;
        }
        catch (Exception ex)
        {
            return Error(ex.Message);
        }
    }

    [McpServerTool(Name = "vs_dte_attach")]
    [Description(
        "Hand a running process over to Visual Studio's debugger so the user can watch it. " +
        "Refuses while the bridge holds a cdb session on that process: one process cannot have two debuggers, " +
        "so call debug_stop first.")]
    public async Task<string> Attach(
        [Description("Process id to attach VS's debugger to.")] int processId)
    {
        var session = _debug.Current;
        if (session is not null)
        {
            return Json.Serialize(new
            {
                ok = false,
                error = "The bridge is currently debugging a process with cdb. " +
                        "Call debug_stop first, then attach VS (a process can only have one debugger).",
                cdbTarget = session.Target,
                cdbPid = session.TargetPid,
            });
        }

        return await Run("attach", processId: processId).ConfigureAwait(false);
    }

    private async Task<string> Run(string action, string? path = null, int? processId = null, int timeoutSeconds = 300)
    {
        try
        {
            var result = await _dte.RunAsync(action, path, processId, timeoutSeconds).ConfigureAwait(false);

            if (!result.Ok)
            {
                return Json.Serialize(new
                {
                    ok = false,
                    error = string.IsNullOrWhiteSpace(result.Json) ? result.Raw : result.Json,
                    raw = result.Raw.Length > 0 && result.Raw != result.Json ? result.Raw : null,
                });
            }

            return result.Json;
        }
        catch (Exception ex)
        {
            return Error(ex.Message);
        }
    }

    private static string Error(string message) => Json.Serialize(new { ok = false, error = message });
}
