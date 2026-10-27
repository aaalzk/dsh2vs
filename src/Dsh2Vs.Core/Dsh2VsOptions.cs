namespace Dsh2Vs.Core;

/// <summary>
/// Runtime options for the dsh2vs bridge. Bound from the "Dsh2Vs" configuration section.
/// </summary>
public sealed class Dsh2VsOptions
{
    /// <summary>Explicit path to MSBuild.exe. When null the locator probes vswhere and known VS installs.</summary>
    public string? MsBuildPath { get; set; }

    /// <summary>Solution or project used when a tool is called without an explicit path.</summary>
    public string? DefaultSolution { get; set; }

    /// <summary>Configuration used when neither the call nor the session state provides one.</summary>
    public string? DefaultConfiguration { get; set; } = "Debug";

    /// <summary>Platform used when neither the call nor the session state provides one.</summary>
    public string? DefaultPlatform { get; set; }

    /// <summary>0 lets MSBuild decide (/m without a count).</summary>
    public int MaxCpuCount { get; set; }

    public int BuildTimeoutSeconds { get; set; } = 1800;

    /// <summary>Directory for build logs. Defaults to %TEMP%\dsh2vs\logs.</summary>
    public string? LogDirectory { get; set; }

    /// <summary>Base directory for relative paths passed to tools.</summary>
    public string? WorkspaceRoot { get; set; }

    public string ResolveLogDirectory() =>
        LogDirectory ?? Path.Combine(Path.GetTempPath(), "dsh2vs", "logs");
}
