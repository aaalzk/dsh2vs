using Dsh2Vs.Core;

namespace Dsh2Vs.Debug;

public sealed class DebugOptions
{
    /// <summary>Explicit path to cdb.exe. When null the locator probes the Windows SDK folders.</summary>
    public string? CdbPath { get; set; }

    /// <summary>Kill a debug session that has not been used for this many minutes (0 disables).</summary>
    public int IdleTimeoutMinutes { get; set; } = 20;

    /// <summary>Allow launching executables outside the workspace.</summary>
    public bool AllowTargetsOutsideWorkspace { get; set; }

    /// <summary>Allow attaching to processes outside the workspace (system processes need admin anyway).</summary>
    public bool AllowSystemAttach { get; set; }

    /// <summary>Default timeout for a command that is expected to return a prompt (g / step).</summary>
    public int CommandTimeoutSeconds { get; set; } = 30;

    /// <summary>Optional symbol path; when null the debugger keeps its default.</summary>
    public string? SymbolPath { get; set; }
}

public sealed class CdbLocator
{
    private readonly DebugOptions _options;
    private string? _cached;

    public CdbLocator(DebugOptions options) => _options = options;

    public string Resolve()
    {
        if (_cached is not null && File.Exists(_cached))
        {
            return _cached;
        }

        if (!string.IsNullOrWhiteSpace(_options.CdbPath) && File.Exists(_options.CdbPath))
        {
            return _cached = _options.CdbPath!;
        }

        var fromEnv = Environment.GetEnvironmentVariable("DSH2VS_CDB");
        if (!string.IsNullOrWhiteSpace(fromEnv) && File.Exists(fromEnv))
        {
            return _cached = fromEnv;
        }

        foreach (var candidate in Candidates())
        {
            if (File.Exists(candidate))
            {
                return _cached = candidate;
            }
        }

        throw new InvalidOperationException(
            "cdb.exe not found. Install 'Debugging Tools for Windows' (Windows SDK), or set DSH2VS_CDB / Dsh2Vs:Debug:CdbPath.");
    }

    public bool TryResolve(out string path)
    {
        try
        {
            path = Resolve();
            return true;
        }
        catch
        {
            path = string.Empty;
            return false;
        }
    }

    private static IEnumerable<string> Candidates()
    {
        var kits = new[]
        {
            @"C:\Program Files (x86)\Windows Kits\10\Debuggers",
            @"C:\Program Files\Windows Kits\10\Debuggers",
            @"D:\Program Files (x86)\Windows Kits\10\Debuggers",
            @"D:\Program Files\Windows Kits\10\Debuggers",
        };

        foreach (var root in kits)
        {
            yield return Path.Combine(root, "x64", "cdb.exe");
        }

        // WinDbg (store app) ships a copy under WindowsApps; the SDK one above is preferred.
        var local = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        yield return Path.Combine(local, "Microsoft", "WindowsApps", "cdb.exe");
    }
}
