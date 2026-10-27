using System.Text.RegularExpressions;
using Dsh2Vs.Core;

namespace Dsh2Vs.Build;

/// <summary>
/// Extracts structured diagnostics from MSBuild console/file-log output.
/// Handles both "file(line,col): error CODE: text [proj]" and "file : error CODE: text" shapes.
/// </summary>
public static partial class MsBuildLogParser
{
    private static readonly Regex WithLocation = new(
        @"^(?<file>.+?)\((?<line>\d+)(?:,(?<col>\d+))?\)\s*:\s*(?<sev>error|warning)\s*(?:(?<code>[A-Za-z_][A-Za-z0-9_]*\d+)\s*)?:\s*(?<msg>.*?)(?:\s*\[(?<proj>[^\]]+)\])?\s*$",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);

    private static readonly Regex WithoutLocation = new(
        @"^(?<file>.+?)\s*:\s*(?<sev>error|warning)\s*(?:(?<code>[A-Za-z_][A-Za-z0-9_]*\d+)\s*)?:\s*(?<msg>.*?)(?:\s*\[(?<proj>[^\]]+)\])?\s*$",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);

    /// <summary>MSBuild console prefixes diagnostics with the parallel-build node id, e.g. "6>".</summary>
    private static readonly Regex NodePrefix = new(@"^\s*\d+>", RegexOptions.Compiled | RegexOptions.CultureInvariant);

    public static IReadOnlyList<BuildDiagnostic> Parse(IEnumerable<string> lines)
    {
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var result = new List<BuildDiagnostic>();

        foreach (var raw in lines)
        {
            var line = raw.TrimEnd('\r', '\n');

            // Console output from parallel builds is prefixed with the MSBuild node id: "6>C:\src\main.cpp(9,34): error ..."
            line = NodePrefix.Replace(line, string.Empty).Trim();

            if (line.Length == 0)
            {
                continue;
            }

            var m = WithLocation.Match(line);
            if (!m.Success)
            {
                m = WithoutLocation.Match(line);
            }

            if (!m.Success)
            {
                continue;
            }

            var severity = m.Groups["sev"].Value.ToLowerInvariant();
            var file = m.Groups["file"].Value.Trim();
            var code = m.Groups["code"].Success ? m.Groups["code"].Value : string.Empty;
            var message = m.Groups["msg"].Value.Trim();
            int.TryParse(m.Groups["line"].Value, out var lineNo);
            int.TryParse(m.Groups["col"].Value, out var colNo);
            var project = m.Groups["proj"].Success ? m.Groups["proj"].Value.Trim() : null;

            if (message.Length == 0 && code.Length == 0)
            {
                continue;
            }

            var key = $"{file}|{lineNo}|{colNo}|{code}|{message}";
            if (!seen.Add(key))
            {
                continue;
            }

            result.Add(new BuildDiagnostic(severity, code, file, lineNo, colNo, message, project));
        }

        return result;
    }
}
