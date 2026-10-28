using System.Text;
using Dsh2Vs.Core;

namespace Dsh2Vs.Debug;

public sealed record ForensicsResult(string DumpPath, string ReportPath, CrashReport Report);

/// <summary>
/// "Crash → dump → verdict → report" in one step.
///
/// The point is that nobody has to be watching: when a target stops on an exception, the bridge
/// writes a minidump, analyses it on the spot (exception record + stack, no symbol download) and
/// drops a markdown report next to it. A failed capture never breaks the tool response.
/// </summary>
public sealed class CrashForensics
{
    private readonly CrashAnalyzer _analyzer;
    private readonly DebugOptions _options;

    public CrashForensics(CrashAnalyzer analyzer, DebugOptions options)
    {
        _analyzer = analyzer;
        _options = options;
    }

    public async Task<ForensicsResult?> CaptureAsync(
        IDebugSession session,
        string? stackText,
        CancellationToken cancellationToken)
    {
        try
        {
            var root = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
            var stamp = DateTime.Now.ToString("yyyyMMdd-HHmmss");
            var name = SafeName(Path.GetFileNameWithoutExtension(session.Target));

            var dumpDir = Path.Combine(root, "dsh2vs", "dumps");
            var reportDir = _options.ForensicsDirectory ?? Path.Combine(root, "dsh2vs", "reports");
            Directory.CreateDirectory(dumpDir);
            Directory.CreateDirectory(reportDir);

            var dumpPath = Path.Combine(dumpDir, $"{name}-{stamp}.dmp");
            await session.ExecuteAsync($".dump /ma \"{dumpPath}\"", TimeSpan.FromMinutes(2), cancellationToken)
                .ConfigureAwait(false);

            var report = await _analyzer.AnalyzeSessionAsync(session, cancellationToken).ConfigureAwait(false);

            var reportPath = Path.Combine(reportDir, $"crash-{name}-{stamp}.md");
            await File.WriteAllTextAsync(
                reportPath,
                BuildMarkdown(session, dumpPath, report, stackText, stamp),
                cancellationToken).ConfigureAwait(false);

            return new ForensicsResult(
                File.Exists(dumpPath) ? dumpPath : string.Empty,
                reportPath,
                report);
        }
        catch
        {
            // Forensics is best-effort: the debugging result itself must still reach the caller.
            return null;
        }
    }

    private static string BuildMarkdown(
        IDebugSession session,
        string dumpPath,
        CrashReport report,
        string? stackText,
        string stamp)
    {
        var sb = new StringBuilder();
        sb.AppendLine($"# 崩溃取证 {stamp}");
        sb.AppendLine();
        sb.AppendLine($"- 目标：`{session.Target}`");
        sb.AppendLine($"- 进程：{(report.ProcessName ?? session.TargetPid?.ToString() ?? "未知")}");
        sb.AppendLine($"- 异常码：`{report.ExceptionCode ?? "未知"}`{(report.ExceptionName is null ? string.Empty : $"（{report.ExceptionName}）")}");
        sb.AppendLine($"- 故障帧：`{report.FaultingSymbol ?? "未知"}`");
        sb.AppendLine($"- 模块：`{report.ModuleName ?? "未知"}`");
        sb.AppendLine($"- dump：`{dumpPath}`");
        sb.AppendLine();

        if (!string.IsNullOrWhiteSpace(stackText))
        {
            sb.AppendLine("## 调用栈");
            sb.AppendLine();
            sb.AppendLine("```");
            sb.AppendLine(stackText.Trim());
            sb.AppendLine("```");
            sb.AppendLine();
        }

        sb.AppendLine("## 调试器原始输出");
        sb.AppendLine();
        sb.AppendLine("```");
        sb.AppendLine(report.Raw.Trim());
        sb.AppendLine("```");

        return sb.ToString();
    }

    private static string SafeName(string name)
    {
        foreach (var c in Path.GetInvalidFileNameChars())
        {
            name = name.Replace(c, '_');
        }

        return string.IsNullOrWhiteSpace(name) ? "target" : name;
    }
}
