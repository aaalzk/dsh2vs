using System.ComponentModel;
using Dsh2Vs.Build;
using Dsh2Vs.Core;
using ModelContextProtocol.Server;

namespace Dsh2Vs.Server.Tools;

[McpServerToolType]
public sealed class ProjectTools
{
    private readonly VsState _state;
    private readonly SolutionParser _parser;
    private readonly ProjectInspector _inspector;

    public ProjectTools(VsState state, SolutionParser parser, ProjectInspector inspector)
    {
        _state = state;
        _parser = parser;
        _inspector = inspector;
    }

    [McpServerTool(Name = "project_open")]
    [Description(
        "Load a Visual Studio solution (.sln) or a single project (.vcxproj) without starting the IDE. " +
        "Returns its projects, configurations and platforms, and makes it the default target for build/debug. " +
        "This does not open Visual Studio; it only reads the project model.")]
    public string ProjectOpen(
        [Description("Absolute or workspace-relative path to a .sln or .vcxproj.")]
        string path,
        [Description("Workspace name (from workspace_list) used to resolve a relative path. Optional.")]
        string? workspace = null)
    {
        string full;
        try
        {
            full = _state.ResolvePath(path, workspace);
        }
        catch (Exception ex)
        {
            // Say where the relative path was resolved, otherwise "file not found" is unhelpful.
            var baseDir = _state.Workspaces.Find(workspace) ?? _state.Workspaces.Current;
            return Json.Serialize(new
            {
                ok = false,
                error = ex.Message,
                requested = path,
                workspace = baseDir?.Name,
                workspacePath = baseDir?.Path,
                resolved = Path.IsPathRooted(path)
                    ? path
                    : Path.Combine(baseDir?.Path ?? string.Empty, path),
                hint = "Check workspace_list for the available roots, or pass an absolute path.",
            });
        }

        var extension = Path.GetExtension(full);

        SolutionInfo info;
        if (extension.Equals(".sln", StringComparison.OrdinalIgnoreCase))
        {
            info = _parser.ParseSolution(full);
            _state.Configuration = null;
            _state.Platform = null;
        }
        else
        {
            var (configs, platforms) = _parser.ParseProjectConfigurations(full);
            var project = new ProjectEntry(
                Path.GetFileNameWithoutExtension(full),
                full,
                Path.GetFileName(full),
                string.Empty,
                string.Empty);

            info = new SolutionInfo(full, project.Name, [project], configs, platforms);
            _state.Configuration = null;
            _state.Platform = null;
        }

        _state.Solution = info;

        return Json.Serialize(new
        {
            opened = true,
            kind = extension.Equals(".sln", StringComparison.OrdinalIgnoreCase) ? "solution" : "project",
            solution = info.FullPath,
            name = info.Name,
            configurations = info.Configurations,
            platforms = info.Platforms,
            projectCount = info.Projects.Count,
            projects = info.Projects.Select(p => new { p.Name, p.FullPath }),
        });
    }

    [McpServerTool(Name = "project_info")]
    [Description(
        "Inspect one project with MSBuild evaluation: target name, output binary path, intermediate dir, " +
        "configuration type (Application/DynamicLibrary/StaticLibrary) and the configurations it declares. " +
        "projectPath defaults to the single project of the opened solution.")]
    public async Task<string> ProjectInfo(
        [Description("Absolute or workspace-relative path to a .vcxproj. Optional.")]
        string? projectPath = null,
        [Description("Configuration to evaluate, default from session.")]
        string? configuration = null,
        [Description("Platform to evaluate, default from session.")]
        string? platform = null)
    {
        var project = ResolveProject(projectPath);
        var (cfg, plat) = _state.ResolveConfiguration(configuration, platform, project);
        var info = await _inspector.InspectAsync(project, cfg, plat, CancellationToken.None).ConfigureAwait(false);

        return Json.Serialize(new
        {
            project = info.FullPath,
            configuration = info.Configuration,
            platform = info.Platform,
            targetName = info.TargetName,
            targetPath = info.TargetPath,
            outputDirectory = info.OutDir,
            intermediateDirectory = info.IntDir,
            configurationType = info.ConfigurationType,
            configurations = info.Configurations,
            platforms = info.Platforms,
        });
    }

    [McpServerTool(Name = "project_configs")]
    [Description("List the configurations/platforms of the currently opened solution or project.")]
    public string ProjectConfigs()
    {
        if (_state.Solution is null)
        {
            return Json.Serialize(new { opened = false, message = "No solution opened. Call project_open first." });
        }

        return Json.Serialize(new
        {
            opened = true,
            solution = _state.Solution.FullPath,
            configurations = _state.Solution.Configurations,
            platforms = _state.Solution.Platforms,
            active = new
            {
                configuration = _state.Configuration ?? _state.Solution.Configurations.FirstOrDefault(),
                platform = _state.Platform ?? _state.Solution.Platforms.FirstOrDefault(),
            },
        });
    }

    [McpServerTool(Name = "project_config_set")]
    [Description("Set the configuration/platform used by subsequent build/debug calls that do not pass one explicitly.")]
    public string ProjectConfigSet(
        [Description("Configuration name, e.g. Debug.")] string? configuration = null,
        [Description("Platform name, e.g. x64.")] string? platform = null)
    {
        if (!string.IsNullOrWhiteSpace(configuration))
        {
            _state.Configuration = configuration.Trim();
        }

        if (!string.IsNullOrWhiteSpace(platform))
        {
            _state.Platform = platform.Trim();
        }

        return Json.Serialize(new
        {
            configuration = _state.Configuration,
            platform = _state.Platform,
            note = "Applied to build/debug calls that omit explicit values.",
        });
    }

    private string ResolveProject(string? projectPath)
    {
        if (!string.IsNullOrWhiteSpace(projectPath))
        {
            return _state.ResolvePath(projectPath);
        }

        var solution = _state.Solution
            ?? throw new InvalidOperationException("No solution opened. Call project_open first.");

        var first = solution.Projects.FirstOrDefault()
            ?? throw new InvalidOperationException("The opened solution contains no projects.");

        return first.FullPath;
    }
}
