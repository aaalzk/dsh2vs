using System.Text.RegularExpressions;
using System.Xml.Linq;
using Dsh2Vs.Core;

namespace Dsh2Vs.Build;

/// <summary>
/// Reads .sln / .vcxproj structure without loading Visual Studio:
/// project list, solution configuration map, and per-project project configurations.
/// </summary>
public sealed class SolutionParser
{
    private static readonly Regex ProjectLine = new(
        @"^Project\(""(?<type>\{[0-9A-Fa-f\-]+\})""\)\s*=\s*""(?<name>[^""]+)"",\s*""(?<path>[^""]+)"",\s*""(?<guid>\{[0-9A-Fa-f\-]+\})""",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);

    private const string SolutionFolderType = "{2150E333-8FDC-42A3-9474-1A3956D46DE8}";

    public SolutionInfo ParseSolution(string solutionPath)
    {
        var fullPath = Path.GetFullPath(solutionPath);
        if (!File.Exists(fullPath))
        {
            throw new FileNotFoundException("Solution not found", fullPath);
        }

        var dir = Path.GetDirectoryName(fullPath)!;
        var projects = new List<ProjectEntry>();
        var configurations = new List<string>();
        var platforms = new List<string>();
        var inConfigSection = false;

        foreach (var line in File.ReadLines(fullPath))
        {
            var trimmed = line.Trim();

            if (trimmed.StartsWith("GlobalSection(SolutionConfigurationPlatforms)", StringComparison.OrdinalIgnoreCase))
            {
                inConfigSection = true;
                continue;
            }

            if (inConfigSection)
            {
                if (trimmed.StartsWith("EndGlobalSection", StringComparison.OrdinalIgnoreCase))
                {
                    inConfigSection = false;
                    continue;
                }

                var eq = trimmed.IndexOf('=');
                if (eq > 0)
                {
                    var key = trimmed[..eq].Trim();
                    var pipe = key.IndexOf('|');
                    if (pipe > 0)
                    {
                        var cfg = key[..pipe].Trim();
                        var plat = key[(pipe + 1)..].Trim();
                        if (!configurations.Contains(cfg, StringComparer.OrdinalIgnoreCase))
                        {
                            configurations.Add(cfg);
                        }

                        if (!platforms.Contains(plat, StringComparer.OrdinalIgnoreCase))
                        {
                            platforms.Add(plat);
                        }
                    }
                }

                continue;
            }

            var m = ProjectLine.Match(trimmed);
            if (!m.Success)
            {
                continue;
            }

            var typeGuid = m.Groups["type"].Value;
            if (string.Equals(typeGuid, SolutionFolderType, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            var relative = m.Groups["path"].Value.Replace('\\', Path.DirectorySeparatorChar);
            var absolute = Path.GetFullPath(Path.Combine(dir, relative));

            projects.Add(new ProjectEntry(
                m.Groups["name"].Value,
                absolute,
                relative,
                m.Groups["guid"].Value,
                typeGuid));
        }

        return new SolutionInfo(
            fullPath,
            Path.GetFileNameWithoutExtension(fullPath),
            projects,
            configurations.Count > 0 ? configurations : ["Debug", "Release"],
            platforms.Count > 0 ? platforms : ["x64"]);
    }

    /// <summary>Configurations declared inside a .vcxproj / .csproj (ProjectConfigurations item group).</summary>
    public (IReadOnlyList<string> Configurations, IReadOnlyList<string> Platforms) ParseProjectConfigurations(string projectPath)
    {
        var configs = new List<string>();
        var platforms = new List<string>();

        var doc = XDocument.Load(projectPath);
        foreach (var item in doc.Descendants()
                     .Where(e => e.Name.LocalName == "ProjectConfiguration"))
        {
            var include = item.Attribute("Include")?.Value;
            if (string.IsNullOrWhiteSpace(include))
            {
                continue;
            }

            var pipe = include.IndexOf('|');
            if (pipe <= 0)
            {
                continue;
            }

            var cfg = include[..pipe].Trim();
            var plat = include[(pipe + 1)..].Trim();

            if (!configs.Contains(cfg, StringComparer.OrdinalIgnoreCase))
            {
                configs.Add(cfg);
            }

            if (!platforms.Contains(plat, StringComparer.OrdinalIgnoreCase))
            {
                platforms.Add(plat);
            }
        }

        return (configs, platforms);
    }
}
