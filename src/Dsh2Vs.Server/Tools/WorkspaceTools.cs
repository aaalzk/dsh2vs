using System.ComponentModel;
using Dsh2Vs.Core;
using ModelContextProtocol.Server;

namespace Dsh2Vs.Server.Tools;

[McpServerToolType]
public sealed class WorkspaceTools
{
    private readonly VsState _state;

    public WorkspaceTools(VsState state) => _state = state;

    [McpServerTool(Name = "workspace_list")]
    [Description(
        "List the configured workspaces (named roots the bridge may touch) and which one is current. " +
        "Relative paths in other tools resolve against the current workspace; the safety guard accepts " +
        "any path inside any listed workspace.")]
    public string WorkspaceList()
    {
        var registry = _state.Workspaces;

        return Json.Serialize(new
        {
            ok = true,
            current = registry.CurrentName,
            workspacesFile = registry.WorkspacesFile,
            workspaces = registry.All.Select(w => new
            {
                name = w.Name,
                path = w.Path,
                isCurrent = w.Name.Equals(registry.CurrentName, StringComparison.OrdinalIgnoreCase),
            }),
            hint = "Call workspace_use to switch, or pass workspace=<name> to project_open / debug_launch.",
        });
    }

    [McpServerTool(Name = "workspace_use")]
    [Description("Make the named workspace the default for relative paths and for the safety boundary.")]
    public string WorkspaceUse([Description("Workspace name from workspace_list.")] string name)
    {
        var registry = _state.Workspaces;
        if (!registry.Use(name))
        {
            return Json.Serialize(new
            {
                ok = false,
                error = $"Unknown workspace '{name}'.",
                available = registry.All.Select(w => w.Name),
            });
        }

        return Json.Serialize(new
        {
            ok = true,
            current = registry.CurrentName,
            path = registry.Current?.Path,
        });
    }
}
