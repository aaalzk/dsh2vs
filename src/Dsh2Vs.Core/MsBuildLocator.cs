using System.Runtime.InteropServices;

namespace Dsh2Vs.Core;

/// <summary>
/// Finds the MSBuild.exe that ships with Visual Studio (C++ projects need the VS instance,
/// not the dotnet SDK one). Resolution order: explicit option, DSH2VS_MSBUILD, vswhere, known paths.
/// </summary>
public sealed class MsBuildLocator
{
    private readonly Dsh2VsOptions _options;
    private string? _cached;

    public MsBuildLocator(Dsh2VsOptions options) => _options = options;

    public string Resolve()
    {
        if (_cached is not null && File.Exists(_cached))
        {
            return _cached;
        }

        if (!string.IsNullOrWhiteSpace(_options.MsBuildPath) && File.Exists(_options.MsBuildPath))
        {
            return _cached = _options.MsBuildPath!;
        }

        var fromEnv = Environment.GetEnvironmentVariable("DSH2VS_MSBUILD");
        if (!string.IsNullOrWhiteSpace(fromEnv) && File.Exists(fromEnv))
        {
            return _cached = fromEnv;
        }

        var fromVsWhere = ProbeVsWhere();
        if (fromVsWhere is not null)
        {
            return _cached = fromVsWhere;
        }

        foreach (var candidate in KnownPaths())
        {
            if (File.Exists(candidate))
            {
                return _cached = candidate;
            }
        }

        throw new InvalidOperationException(
            "MSBuild.exe not found. Install Visual Studio with the C++ workload, or set DSH2VS_MSBUILD / Dsh2Vs:MsBuildPath.");
    }

    private static string? ProbeVsWhere()
    {
        string pf86 = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86);
        var vswhere = Path.Combine(pf86, "Microsoft Visual Studio", "Installer", "vswhere.exe");
        if (!File.Exists(vswhere))
        {
            return null;
        }

        try
        {
            var psi = new System.Diagnostics.ProcessStartInfo
            {
                FileName = vswhere,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true,
            };
            psi.ArgumentList.Add("-latest");
            psi.ArgumentList.Add("-prerelease");
            psi.ArgumentList.Add("-requires");
            psi.ArgumentList.Add("Microsoft.Component.MSBuild");
            psi.ArgumentList.Add("-find");
            psi.ArgumentList.Add(@"MSBuild\**\Bin\MSBuild.exe");

            using var p = System.Diagnostics.Process.Start(psi);
            if (p is null)
            {
                return null;
            }

            var stdout = p.StandardOutput.ReadToEnd();
            p.WaitForExit(15000);
            var first = stdout
                .Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                .FirstOrDefault(File.Exists);
            return first;
        }
        catch
        {
            return null;
        }
    }

    private static IEnumerable<string> KnownPaths()
    {
        var roots = new List<string>
        {
            Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles),
            Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86),
            @"D:\Program Files",
            @"D:\Program Files (x86)",
        };

        var years = new[] { "2022", "2026", "2019" };
        var editions = new[] { "Enterprise", "Professional", "Community", "BuildTools" };
        var driveRoots = new[] { "C:", "D:", "E:" };

        foreach (var drive in driveRoots)
        {
            foreach (var year in years)
            {
                foreach (var edition in editions)
                {
                    yield return $@"{drive}\Program Files\Microsoft Visual Studio\{year}\{edition}\MSBuild\Current\Bin\MSBuild.exe";
                }
            }
        }

        foreach (var root in roots)
        {
            foreach (var year in years)
            {
                foreach (var edition in editions)
                {
                    yield return Path.Combine(root, "Microsoft Visual Studio", year, edition, "MSBuild", "Current", "Bin", "MSBuild.exe");
                }
            }
        }
    }
}
