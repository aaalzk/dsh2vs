using System.Text;
using System.Text.RegularExpressions;
using Dsh2Vs.Core;

namespace Dsh2Vs.Debug;

public sealed record CrashReport(
    string? ExceptionCode,
    string? ExceptionName,
    string? ModuleName,
    string? ImageName,
    string? ProcessName,
    string? FailureBucket,
    string? FaultingSymbol,
    string? FaultingSource,
    string Raw);

/// <summary>
/// Crash forensics for a live session or a dump.
///
/// The authoritative source is the exception record (".exr -1" live, ".ecxr" for a dump) plus the
/// stack: that needs no symbols at all. "!analyze -v" is only consulted when a symbol server is
/// configured, because without system symbols it happily reports WRONG_SYMBOLS nonsense
/// (and, worse, stalls for minutes trying to download them).
/// </summary>
public sealed class CrashAnalyzer
{
    private readonly DebugOptions _options;
    private readonly CdbLocator _locator;

    public CrashAnalyzer(DebugOptions options, CdbLocator locator)
    {
        _options = options;
        _locator = locator;
    }

    public async Task<CrashReport> AnalyzeSessionAsync(CdbSession session, CancellationToken cancellationToken)
    {
        var record = await session.ExecuteAsync(".exr -1", TimeSpan.FromSeconds(20), cancellationToken)
            .ConfigureAwait(false);
        var stack = await session.ExecuteAsync("kb 12", TimeSpan.FromSeconds(20), cancellationToken)
            .ConfigureAwait(false);

        var text = new StringBuilder();
        text.AppendLine("[exception record]").AppendLine(record.Text);
        text.AppendLine("[stack]").AppendLine(stack.Text);

        var code = ParseExceptionCode(record.Text);
        var faulting = FirstUserFrame(stack.Text) ?? ParseFaultingIp(record.Text);

        string? module = null, image = null, process = null, bucket = null;

        if (!string.IsNullOrWhiteSpace(_options.SymbolServer))
        {
            var symbols = _options.SymbolServer!;
            await session.ExecuteAsync($".sympath {symbols}", TimeSpan.FromSeconds(10), cancellationToken)
                .ConfigureAwait(false);

            var analyze = await session.ExecuteAsync("!analyze -v", TimeSpan.FromSeconds(90), cancellationToken)
                .ConfigureAwait(false);

            text.AppendLine("[!analyze -v]").AppendLine(analyze.Text);

            var parsed = Parse(analyze.Text);
            code ??= parsed.ExceptionCode;
            module = parsed.ModuleName;
            image = parsed.ImageName;
            process = parsed.ProcessName;
            bucket = parsed.FailureBucket;
            faulting ??= parsed.FaultingSymbol;
        }

        return new CrashReport(
            code,
            DescribeException(code) ?? ParseExceptionName(record.Text),
            module,
            image,
            process,
            bucket,
            faulting,
            ParseFaultingSource(record.Text),
            Truncate(text.ToString()));
    }

    /// <summary>Opens a dump in a throwaway cdb process; the live session is left untouched.</summary>
    public async Task<CrashReport> AnalyzeDumpAsync(string dumpPath, CancellationToken cancellationToken)
    {
        var full = Path.GetFullPath(dumpPath);
        if (!File.Exists(full))
        {
            throw new FileNotFoundException($"Dump not found: {full}");
        }

        var symbols = _options.SymbolServer ?? LocalSymbolPath(full);

        // .ecxr restores the faulting context, .exr -1 prints the exception record (code + address)
        // and needs no symbols at all. "!analyze" is only added when real symbols are configured.
        var commands = ".ecxr; .exr -1; kb 12;";

        // Only ask for the (slow, noisy) automatic analysis when real symbols are available.
        if (!string.IsNullOrWhiteSpace(_options.SymbolServer))
        {
            commands += " !analyze -v;";
        }

        commands += " q";

        var args = new List<string>();
        if (!string.IsNullOrWhiteSpace(symbols))
        {
            args.Add("-y");
            args.Add(symbols);
        }

        args.Add("-z");
        args.Add(full);
        args.Add("-c");
        args.Add(commands);

        var result = await ProcessRunner.RunAsync(
            _locator.Resolve(),
            args,
            Path.GetDirectoryName(full),
            240,
            cancellationToken).ConfigureAwait(false);

        var text = result.StandardOutput + "\n" + result.StandardError;
        var code = ParseExceptionCode(text);
        var analyzeReport = Parse(text);

        return new CrashReport(
            code ?? analyzeReport.ExceptionCode,
            DescribeException(code) ?? analyzeReport.ExceptionName,
            analyzeReport.ModuleName,
            analyzeReport.ImageName,
            analyzeReport.ProcessName,
            analyzeReport.FailureBucket,
            FirstUserFrame(text) ?? analyzeReport.FaultingSymbol,
            analyzeReport.FaultingSource,
            Truncate(text));
    }

    private static readonly Regex ExceptionCodePattern = new(
        @"ExceptionCode:\s*(?<code>[0-9a-fA-F]{8})",
        RegexOptions.Compiled);

    private static readonly Regex NtStatusCodePattern = new(
        @"EXCEPTION_CODE(?:_STR)?:\s*(?:\(NTSTATUS\)\s*)?(?<code>[0-9a-fA-Fx]+)",
        RegexOptions.Compiled);

    private static readonly Regex ExceptionNamePattern = new(
        @"ExceptionCode:\s*[0-9a-fA-F]{8}\s*\((?<name>[^)]+)\)",
        RegexOptions.Compiled);

    private static readonly Regex FieldPattern = new(
        @"^\s*(?<key>MODULE_NAME|IMAGE_NAME|PROCESS_NAME|FAILURE_BUCKET_ID|FAULTING_SOURCE_FILE|FAULTING_SOURCE_LINE):\s*(?<value>.+?)\s*$",
        RegexOptions.Compiled | RegexOptions.Multiline);

    private static readonly Regex FaultingIpPattern = new(
        @"FAULTING_IP:\s*\n?\s*(?<sym>\S+)",
        RegexOptions.Compiled);

    private static readonly Regex FrameSymbolPattern = new(
        @"(?<module>[A-Za-z_][A-Za-z0-9_.\-]*)!(?<body>[A-Za-z0-9_~`<>$@?+.\-]+)",
        RegexOptions.Compiled);

    private static readonly string[] SystemModules =
    [
        "ntdll", "KERNELBASE", "KERNEL32", "USER32", "win32u", "combase",
        "ucrtbase", "MSVCRT", "RPCRT4", "sechost", "ntdll_wrong_symbols",
    ];

    /// <summary>First stack frame outside Windows system DLLs — usually "your" code.</summary>
    internal static string? FirstUserFrame(string stackText)
    {
        foreach (Match match in FrameSymbolPattern.Matches(stackText))
        {
            var module = match.Groups["module"].Value;
            if (SystemModules.Any(s => module.Equals(s, StringComparison.OrdinalIgnoreCase)))
            {
                continue;
            }

            return $"{module}!{match.Groups["body"].Value}";
        }

        return null;
    }

    internal static string? ParseExceptionCode(string text)
    {
        var m = ExceptionCodePattern.Match(text);
        if (m.Success)
        {
            return "0x" + m.Groups["code"].Value.ToLowerInvariant();
        }

        m = NtStatusCodePattern.Match(text);
        if (!m.Success)
        {
            return null;
        }

        var code = m.Groups["code"].Value.Trim();
        return code.StartsWith("0x", StringComparison.OrdinalIgnoreCase) ? code : "0x" + code;
    }

    private static string? ParseExceptionName(string text)
    {
        var m = ExceptionNamePattern.Match(text);
        return m.Success ? m.Groups["name"].Value.Trim() : null;
    }

    private static string? ParseFaultingIp(string text)
    {
        var m = FaultingIpPattern.Match(text);
        return m.Success ? m.Groups["sym"].Value : null;
    }

    private static string? ParseFaultingSource(string text)
    {
        var file = FieldPattern.Matches(text)
            .Cast<Match>()
            .FirstOrDefault(m => m.Groups["key"].Value == "FAULTING_SOURCE_FILE")?.Groups["value"].Value;

        var line = FieldPattern.Matches(text)
            .Cast<Match>()
            .FirstOrDefault(m => m.Groups["key"].Value == "FAULTING_SOURCE_LINE")?.Groups["value"].Value;

        if (file is null)
        {
            return null;
        }

        return line is null ? file : $"{file}:{line}";
    }

    internal static CrashReport Parse(string text)
    {
        var fields = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (Match f in FieldPattern.Matches(text))
        {
            fields[f.Groups["key"].Value] = f.Groups["value"].Value.Trim();
        }

        var code = ParseExceptionCode(text);
        var module = fields.GetValueOrDefault("MODULE_NAME");
        var image = fields.GetValueOrDefault("IMAGE_NAME");

        // "!analyze" says this when it resolved the wrong binary entirely; never pass it on as fact.
        if (module is not null && module.Contains("wrong", StringComparison.OrdinalIgnoreCase))
        {
            module = null;
            image = null;
        }

        var ip = FaultingIpPattern.Match(text);

        return new CrashReport(
            code,
            DescribeException(code) ?? ParseExceptionName(text),
            module,
            image,
            fields.GetValueOrDefault("PROCESS_NAME"),
            fields.GetValueOrDefault("FAILURE_BUCKET_ID"),
            ip.Success ? ip.Groups["sym"].Value : null,
            ParseFaultingSource(text),
            Truncate(text));
    }

    /// <summary>Human readable name for the exception codes that show up in C++ crashes.</summary>
    internal static string? DescribeException(string? code) => code?.ToLowerInvariant() switch
    {
        "0xc0000005" => "access violation",
        "0xc0000409" => "stack buffer overrun / fast fail (often __fastfail, abort or /GS)",
        "0xc00000fd" => "stack overflow",
        "0xc0000374" => "heap corruption",
        "0xc0000602" => "fail fast exception",
        "0x80000003" => "breakpoint",
        "0xe0434352" => ".NET (CLR) unhandled exception",
        "0x40000015" => "fatal application exit",
        _ => null,
    };

    /// <summary>Directory holding the binary (and usually its PDB), plus the dump's own directory.</summary>
    private static string LocalSymbolPath(string target)
    {
        var dirs = new List<string>();

        var targetDir = File.Exists(target) ? Path.GetDirectoryName(target) : target;
        if (!string.IsNullOrWhiteSpace(targetDir) && Directory.Exists(targetDir))
        {
            dirs.Add(targetDir);
        }

        dirs.Add(Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "dsh2vs",
            "symbols"));

        return string.Join(';', dirs.Distinct(StringComparer.OrdinalIgnoreCase));
    }

    private static string Truncate(string text) =>
        text.Length <= 12000 ? text : text[..12000] + "\n... (truncated)";
}
