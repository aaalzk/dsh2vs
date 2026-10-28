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

    [McpServerTool(Name = "workspace_add")]
    [Description(
        "Register a directory as a workspace and persist it, so relative paths can resolve against it and the " +
        "safety guard accepts targets inside it. Takes effect immediately (no service restart). " +
        "Name defaults to the folder name.")]
    public string WorkspaceAdd(
        [Description("Directory to register, e.g. D:\\Projects\\MyRepo")] string path,
        [Description("Optional workspace name; defaults to the folder name.")] string? name = null)
    {
        var registry = _state.Workspaces;
        try
        {
            var info = registry.AddWorkspace(path, name);

            return Json.Serialize(new
            {
                ok = true,
                added = info.Name,
                path = info.Path,
                current = registry.CurrentName,
                workspacesFile = registry.WorkspacesFile,
                workspaces = registry.All.Select(w => new { w.Name, w.Path }),
            });
        }
        catch (Exception ex)
        {
            return Json.Serialize(new { ok = false, error = ex.Message });
        }
    }

    [McpServerTool(Name = "workspace_remove")]
    [Description("Remove a workspace by name and persist the change. The last remaining workspace cannot be removed.")]
    public string WorkspaceRemove([Description("Workspace name from workspace_list.")] string name)
    {
        var registry = _state.Workspaces;
        var removed = registry.RemoveWorkspace(name);

        return Json.Serialize(new
        {
            ok = removed,
            error = removed ? null : $"Unknown workspace '{name}', or it is the last one left.",
            current = registry.CurrentName,
            workspaces = registry.All.Select(w => new { w.Name, w.Path }),
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
