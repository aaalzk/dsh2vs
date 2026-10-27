using System.ComponentModel;
using Dsh2Vs.Build;
using Dsh2Vs.Core;
using ModelContextProtocol.Server;

namespace Dsh2Vs.Server.Tools;

[McpServerToolType]
public sealed class BuildTools
{
    private const int MaxDiagnostics = 200;

    private readonly VsState _state;
    private readonly MsBuildRunner _runner;

    public BuildTools(VsState state, MsBuildRunner runner)
    {
        _state = state;
        _runner = runner;
    }

    [McpServerTool(Name = "build")]
    [Description(
        "Build a Visual Studio solution (.sln) or C++ project (.vcxproj) with MSBuild. " +
        "Returns success, duration, log path and structured errors/warnings (file, line, column, code, message). " +
        "path defaults to the solution opened with project_open.")]
    public Task<string> Build(
        [Description("Absolute or workspace-relative path to the .sln/.vcxproj. Optional.")]
        string? path = null,
        [Description("Configuration name, e.g. Debug or Release. Defaults to the session/opened solution configuration.")]
        string? configuration = null,
        [Description("Platform name, e.g. x64 or Win32. Defaults to the session/opened solution platform.")]
        string? platform = null)
        => RunAsync("Build", path, configuration, platform, CancellationToken.None);

    [McpServerTool(Name = "rebuild")]
    [Description("Clean and rebuild a solution/project (MSBuild -t:Rebuild). Same parameters and result shape as build.")]
    public Task<string> Rebuild(
        [Description("Absolute or workspace-relative path to the .sln/.vcxproj. Optional.")]
        string? path = null,
        [Description("Configuration name, default from session.")] string? configuration = null,
        [Description("Platform name, default from session.")] string? platform = null)
        => RunAsync("Rebuild", path, configuration, platform, CancellationToken.None);

    [McpServerTool(Name = "clean")]
    [Description("Remove build outputs of a solution/project (MSBuild -t:Clean).")]
    public Task<string> Clean(
        [Description("Absolute or workspace-relative path to the .sln/.vcxproj. Optional.")]
        string? path = null,
        [Description("Configuration name, default from session.")] string? configuration = null,
        [Description("Platform name, default from session.")] string? platform = null)
        => RunAsync("Clean", path, configuration, platform, CancellationToken.None);

    [McpServerTool(Name = "build_errors")]
    [Description(
        "Return the diagnostics of the most recent build without rebuilding. " +
        "Use this to re-read compile errors after inspecting code.")]
    public string BuildErrors(
        [Description("Only return diagnostics of this severity: error, warning, or all (default).")]
        string? severity = null)
    {
        var last = _state.LastBuild;
        if (last is null)
        {
            return Json.Serialize(new { found = false, message = "No build has run in this session yet." });
        }

        var wanted = string.IsNullOrWhiteSpace(severity) ? "all" : severity.Trim().ToLowerInvariant();
        var diagnostics = wanted is "all" or ""
            ? last.Diagnostics
            : last.Diagnostics.Where(d => d.Severity == wanted).ToList();

        return Json.Serialize(new
        {
            found = true,
            success = last.Success,
            target = last.Target,
            verb = last.Verb,
            configuration = last.Configuration,
            platform = last.Platform,
            durationMs = last.DurationMs,
            errorCount = last.ErrorCount,
            warningCount = last.WarningCount,
            total = diagnostics.Count,
            truncated = diagnostics.Count > MaxDiagnostics,
            diagnostics = diagnostics.Take(MaxDiagnostics),
            logPath = last.LogPath,
        });
    }

    private async Task<string> RunAsync(
        string verb,
        string? path,
        string? configuration,
        string? platform,
        CancellationToken cancellationToken)
    {
        var target = _state.ResolvePath(path);
        var (cfg, plat) = _state.ResolveConfiguration(configuration, platform, target);

        await _state.BuildLock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            var result = await _runner.RunAsync(target, verb, cfg, plat, cancellationToken).ConfigureAwait(false);
            _state.LastBuild = result;
            _state.Configuration ??= cfg;
            _state.Platform ??= plat;

            return Json.Serialize(new
            {
                success = result.Success,
                exitCode = result.ExitCode,
                verb = result.Verb,
                target = result.Target,
                configuration = result.Configuration,
                platform = result.Platform,
                durationMs = result.DurationMs,
                errorCount = result.ErrorCount,
                warningCount = result.WarningCount,
                truncated = result.Diagnostics.Count > MaxDiagnostics,
                summary = result.Summary,
                diagnostics = result.Diagnostics.Take(MaxDiagnostics),
                logPath = result.LogPath,
            });
        }
        finally
        {
            _state.BuildLock.Release();
        }
    }
}
