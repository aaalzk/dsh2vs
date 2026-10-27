using System.Reflection;
using Dsh2Vs.Core;

namespace Dsh2Vs.Server;

/// <summary>
/// Session state shared by all MCP tool calls: the opened solution, the active configuration,
/// and the last build result.
/// </summary>
public sealed class VsState
{
    private readonly Dsh2VsOptions _options;

    public VsState(Dsh2VsOptions options)
    {
        _options = options;
        StartedAt = DateTimeOffset.Now;
    }

    public Dsh2VsOptions Options => _options;

    public SolutionInfo? Solution { get; set; }

    public string? Configuration { get; set; }

    public string? Platform { get; set; }

    public BuildResult? LastBuild { get; set; }

    /// <summary>Serializes builds: two concurrent MSBuild runs on one tree fight over the same obj files.</summary>
    public SemaphoreSlim BuildLock { get; } = new(1, 1);

    public DateTimeOffset StartedAt { get; }

    public static string Version =>
        Assembly.GetExecutingAssembly().GetName().Version?.ToString(3) ?? "0.1.0";

    public string ResolvePath(string? path)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            var fallback = Solution?.FullPath ?? _options.DefaultSolution;
            if (string.IsNullOrWhiteSpace(fallback))
            {
                throw new InvalidOperationException(
                    "No solution is open. Call project_open with a .sln/.vcxproj path first, or pass an explicit path.");
            }

            path = fallback;
        }

        var full = Path.IsPathRooted(path)
            ? path
            : Path.GetFullPath(Path.Combine(_options.WorkspaceRoot ?? Environment.CurrentDirectory, path));

        full = Path.GetFullPath(full);

        if (!File.Exists(full))
        {
            throw new FileNotFoundException($"Target not found: {full}");
        }

        return full;
    }

    public (string Configuration, string Platform) ResolveConfiguration(
        string? configuration,
        string? platform,
        string? targetPath)
    {
        var configs = Solution?.Configurations ?? [];
        var platforms = Solution?.Platforms ?? [];

        var cfg = FirstNonEmpty(
            configuration,
            Configuration,
            configs.FirstOrDefault(c => string.Equals(c, "Debug", StringComparison.OrdinalIgnoreCase)),
            configs.FirstOrDefault(),
            _options.DefaultConfiguration,
            "Debug");

        var plat = FirstNonEmpty(
            platform,
            Platform,
            platforms.FirstOrDefault(p =>
                string.Equals(p, "x64", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(p, "x86", StringComparison.OrdinalIgnoreCase)),
            platforms.FirstOrDefault(),
            _options.DefaultPlatform,
            "x64");

        return (cfg, plat);
    }

    public object Health()
    {
        string? msbuild = null;
        string? msbuildError = null;
        try
        {
            msbuild = new MsBuildLocator(_options).Resolve();
        }
        catch (Exception ex)
        {
            msbuildError = ex.Message;
        }

        return new
        {
            status = msbuild is null ? "degraded" : "ok",
            name = "dsh2vs",
            version = Version,
            startedAt = StartedAt,
            workspaceRoot = _options.WorkspaceRoot,
            msbuild,
            msbuildError,
            solution = Solution?.FullPath,
            configuration = Configuration,
            platform = Platform,
            lastBuild = LastBuild is null
                ? null
                : new
                {
                    target = LastBuild.Target,
                    verb = LastBuild.Verb,
                    success = LastBuild.Success,
                    durationMs = LastBuild.DurationMs,
                    errors = LastBuild.ErrorCount,
                    warnings = LastBuild.WarningCount,
                },
        };
    }

    private static string FirstNonEmpty(params string?[] values) =>
        values.FirstOrDefault(v => !string.IsNullOrWhiteSpace(v))!;
}
