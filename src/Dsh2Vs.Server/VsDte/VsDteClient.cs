using System.Globalization;
using Dsh2Vs.Core;

namespace Dsh2Vs.Server.VsDte;

public sealed record VsDteResult(bool Ok, string Json, string Raw);

/// <summary>
/// Drives the user's running Visual Studio through its DTE automation object.
///
/// DTE is COM and Marshal.GetActiveObject is gone in .NET Core, so instead of fighting interop
/// inside the bridge we run a small Windows PowerShell 5.1 script (scripts/vs-dte.ps1) as a child
/// process and take its JSON answer. It keeps the server simple and the script debuggable by hand.
/// </summary>
public sealed class VsDteClient
{
    private readonly Dsh2VsOptions _options;

    public VsDteClient(Dsh2VsOptions options) => _options = options;

    public string ScriptPath => Path.Combine(AppContext.BaseDirectory, "vs-dte.ps1");

    public async Task<VsDteResult> RunAsync(
        string action,
        string? path = null,
        int? processId = null,
        int timeoutSeconds = 300,
        string? pane = null,
        int? maxChars = null,
        CancellationToken cancellationToken = default)
    {
        if (!File.Exists(ScriptPath))
        {
            throw new FileNotFoundException(
                "vs-dte.ps1 is not deployed next to the server. Rebuild so the scripts folder is copied to the output.",
                ScriptPath);
        }

        var args = new List<string>
        {
            "-NoProfile",
            "-ExecutionPolicy",
            "Bypass",
            "-File",
            ScriptPath,
            "-Action",
            action,
        };

        if (!string.IsNullOrWhiteSpace(path))
        {
            args.Add("-Path");
            args.Add(path!);
        }

        if (processId is int pid)
        {
            args.Add("-ProcessId");
            args.Add(pid.ToString(CultureInfo.InvariantCulture));
        }

        if (!string.IsNullOrWhiteSpace(pane))
        {
            args.Add("-Pane");
            args.Add(pane!);
        }

        if (maxChars is int chars)
        {
            args.Add("-MaxChars");
            args.Add(chars.ToString(CultureInfo.InvariantCulture));
        }

        args.Add("-TimeoutSeconds");
        args.Add(timeoutSeconds.ToString(CultureInfo.InvariantCulture));

        var result = await ProcessRunner
            .RunAsync("powershell.exe", args, null, timeoutSeconds + 60, cancellationToken)
            .ConfigureAwait(false);

        var raw = (result.StandardOutput + "\n" + result.StandardError).Trim();

        // The script prints exactly one JSON line; PowerShell noise (if any) must not break parsing.
        var json = raw
            .Split('\n', StringSplitOptions.RemoveEmptyEntries)
            .Select(line => line.Trim())
            .LastOrDefault(line => line.StartsWith('{'));

        var ok = json is not null &&
                 json.Replace(" ", string.Empty, StringComparison.Ordinal)
                     .Contains("\"ok\":true", StringComparison.OrdinalIgnoreCase);

        return new VsDteResult(ok, json ?? "{}", raw);
    }
}
