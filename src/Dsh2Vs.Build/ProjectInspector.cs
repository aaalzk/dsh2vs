using System.Text.Json;
using Dsh2Vs.Core;

namespace Dsh2Vs.Build;

/// <summary>
/// Queries project properties through MSBuild evaluation (-getProperty), so C++ projects are
/// resolved exactly the way Visual Studio would resolve them.
/// </summary>
public sealed class ProjectInspector
{
    private static readonly string[] PropertyNames =
    [
        "TargetName",
        "TargetPath",
        "OutDir",
        "IntDir",
        "ConfigurationType",
    ];

    private readonly Dsh2VsOptions _options;
    private readonly MsBuildLocator _locator;
    private readonly SolutionParser _parser;

    public ProjectInspector(Dsh2VsOptions options, MsBuildLocator locator, SolutionParser parser)
    {
        _options = options;
        _locator = locator;
        _parser = parser;
    }

    public async Task<ProjectInfo> InspectAsync(
        string projectPath,
        string configuration,
        string platform,
        CancellationToken cancellationToken)
    {
        var full = Path.GetFullPath(projectPath);
        var (configs, platforms) = _parser.ParseProjectConfigurations(full);
        var props = await GetPropertiesAsync(full, configuration, platform, cancellationToken).ConfigureAwait(false);

        return new ProjectInfo(
            full,
            props.GetValueOrDefault("TargetName"),
            props.GetValueOrDefault("TargetPath"),
            props.GetValueOrDefault("OutDir"),
            props.GetValueOrDefault("IntDir"),
            props.GetValueOrDefault("ConfigurationType"),
            configuration,
            platform,
            configs,
            platforms);
    }

    public async Task<IReadOnlyDictionary<string, string>> GetPropertiesAsync(
        string projectPath,
        string configuration,
        string platform,
        CancellationToken cancellationToken)
    {
        var args = new List<string>
        {
            projectPath,
            "-nologo",
            "-v:quiet",
            "-p:Configuration=" + configuration,
            "-p:Platform=" + platform,
            "-getProperty:" + string.Join(';', PropertyNames),
        };

        var result = await ProcessRunner.RunAsync(
            _locator.Resolve(),
            args,
            Path.GetDirectoryName(projectPath),
            180,
            cancellationToken).ConfigureAwait(false);

        return ParseProperties(result.StandardOutput);
    }

    internal static IReadOnlyDictionary<string, string> ParseProperties(string stdout)
    {
        var map = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        var start = stdout.IndexOf('{');
        if (start < 0)
        {
            return map;
        }

        try
        {
            using var doc = JsonDocument.Parse(stdout[start..]);
            if (!doc.RootElement.TryGetProperty("Properties", out var props))
            {
                return map;
            }

            foreach (var p in props.EnumerateObject())
            {
                map[p.Name] = p.Value.ValueKind == JsonValueKind.String
                    ? p.Value.GetString() ?? string.Empty
                    : p.Value.ToString();
            }
        }
        catch (JsonException)
        {
            // older MSBuild without -getProperty support, or warning noise
        }

        return map;
    }
}
