using Dsh2Vs.Core;

namespace Dsh2Vs.Build;

public sealed class MsBuildRunner
{
    private readonly Dsh2VsOptions _options;
    private readonly MsBuildLocator _locator;

    public MsBuildRunner(Dsh2VsOptions options, MsBuildLocator locator)
    {
        _options = options;
        _locator = locator;
    }

    public string MsBuildPath => _locator.Resolve();

    public async Task<BuildResult> RunAsync(
        string targetPath,
        string verb,
        string configuration,
        string platform,
        CancellationToken cancellationToken)
    {
        if (!File.Exists(targetPath))
        {
            throw new FileNotFoundException("Build target not found", targetPath);
        }

        var msbuild = _locator.Resolve();
        var logDir = _options.ResolveLogDirectory();
        Directory.CreateDirectory(logDir);
        var logPath = Path.Combine(logDir, $"{DateTime.Now:yyyyMMdd-HHmmss}-{Guid.NewGuid():N}.log");

        var args = new List<string>
        {
            targetPath,
            "-nologo",
            "-v:minimal",
            "-p:Configuration=" + configuration,
            "-p:Platform=" + platform,
            "-flp:logfile=" + logPath + ";verbosity=normal;encoding=utf-8",
        };

        args.Add(_options.MaxCpuCount > 0 ? $"-m:{_options.MaxCpuCount}" : "-m");

        if (!string.Equals(verb, "Build", StringComparison.OrdinalIgnoreCase))
        {
            args.Add("-t:" + verb);
        }

        var process = await ProcessRunner.RunAsync(
            msbuild,
            args,
            Path.GetDirectoryName(targetPath),
            _options.BuildTimeoutSeconds,
            cancellationToken).ConfigureAwait(false);

        var lines = new List<string>();
        if (File.Exists(logPath))
        {
            try
            {
                lines.AddRange(File.ReadLines(logPath));
            }
            catch
            {
                // log may still be locked; stdout below still carries errors
            }
        }

        lines.AddRange(SplitLines(process.StandardOutput));
        lines.AddRange(SplitLines(process.StandardError));

        var diagnostics = MsBuildLogParser.Parse(lines);
        var summary = ExtractSummary(process.StandardOutput, process.TimedOut, process.ExitCode, diagnostics);

        return new BuildResult(
            process.ExitCode == 0 && !process.TimedOut,
            process.ExitCode,
            targetPath,
            verb,
            configuration,
            platform,
            process.DurationMs,
            diagnostics,
            File.Exists(logPath) ? logPath : null,
            summary);
    }

    private static IEnumerable<string> SplitLines(string text) =>
        string.IsNullOrEmpty(text)
            ? []
            : text.Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries);

    private static string ExtractSummary(
        string stdout,
        bool timedOut,
        int exitCode,
        IReadOnlyList<BuildDiagnostic> diagnostics)
    {
        if (timedOut)
        {
            return "build timed out";
        }

        var errors = diagnostics.Count(d => d.Severity == Severity.Error);
        var warnings = diagnostics.Count(d => d.Severity == Severity.Warning);
        var lines = SplitLines(stdout).Select(l => l.Trim()).Where(l => l.Length > 0).ToList();

        if (errors == 0 && exitCode == 0)
        {
            return $"build succeeded ({warnings} warning(s))";
        }

        foreach (var line in lines.AsEnumerable().Reverse())
        {
            if (line.Contains("Build succeeded", StringComparison.OrdinalIgnoreCase) ||
                line.Contains("Build FAILED", StringComparison.OrdinalIgnoreCase) ||
                line.Contains("生成成功", StringComparison.Ordinal) ||
                line.Contains("生成失败", StringComparison.Ordinal))
            {
                return $"{line} ({errors} error(s), {warnings} warning(s))";
            }
        }

        return $"{errors} error(s), {warnings} warning(s), exit code {exitCode}";
    }
}
