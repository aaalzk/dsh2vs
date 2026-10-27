using System.ComponentModel;
using ModelContextProtocol.Server;

namespace Dsh2Vs.Server.Tools;

[McpServerToolType]
public sealed class StatusTools
{
    private readonly VsState _state;

    public StatusTools(VsState state) => _state = state;

    [McpServerTool(Name = "vs_status")]
    [Description(
        "Report the bridge state: version, resolved MSBuild.exe, workspace root, currently opened solution, " +
        "active configuration/platform, and the outcome of the last build.")]
    public string VsStatus() => Json.Serialize(_state.Health());
}
