namespace Dsh2Vs.Core;

public static class Severity
{
    public const string Error = "error";
    public const string Warning = "warning";
}

public sealed record BuildDiagnostic(
    string Severity,
    string Code,
    string File,
    int Line,
    int Column,
    string Message,
    string? Project = null);

public sealed record BuildResult(
    bool Success,
    int ExitCode,
    string Target,
    string Verb,
    string Configuration,
    string Platform,
    long DurationMs,
    IReadOnlyList<BuildDiagnostic> Diagnostics,
    string? LogPath,
    string? Summary)
{
    public int ErrorCount => Diagnostics.Count(d => d.Severity == Severity.Error);
    public int WarningCount => Diagnostics.Count(d => d.Severity == Severity.Warning);
}

public sealed record ProjectEntry(
    string Name,
    string FullPath,
    string RelativePath,
    string ProjectGuid,
    string TypeGuid);

public sealed record SolutionInfo(
    string FullPath,
    string Name,
    IReadOnlyList<ProjectEntry> Projects,
    IReadOnlyList<string> Configurations,
    IReadOnlyList<string> Platforms);

public sealed record ProjectInfo(
    string FullPath,
    string? TargetName,
    string? TargetPath,
    string? OutDir,
    string? IntDir,
    string? ConfigurationType,
    string Configuration,
    string Platform,
    IReadOnlyList<string> Configurations,
    IReadOnlyList<string> Platforms);

public sealed record ProcessResult(
    int ExitCode,
    string StandardOutput,
    string StandardError,
    long DurationMs,
    bool TimedOut);
