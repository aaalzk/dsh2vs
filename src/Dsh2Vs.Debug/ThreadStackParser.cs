using System.Text.RegularExpressions;

namespace Dsh2Vs.Debug;

public sealed record ThreadStack(int Index, string? SystemId, IReadOnlyList<string> Symbols)
{
    public string Joined => string.Join(" ", Symbols);
}

/// <summary>
/// Parses "~*k" (all thread stacks) and decides which thread is worth looking at.
///
/// An injected break lands on a throwaway thread (ntdll!DbgUiRemoteBreakin); the thread that
/// matters is the one sitting in MessageBoxW or a message-pump wait.
/// </summary>
public static partial class ThreadStackParser
{
    private static readonly Regex ThreadHeader = new(
        @"^\s*(?<index>\d+)\s+Id:\s*(?<pid>[0-9a-fA-F]+)\.(?<tid>[0-9a-fA-F]+)",
        RegexOptions.Compiled);

    private static readonly Regex FrameSymbol = new(
        @"(?<module>[A-Za-z_][A-Za-z0-9_.\-]*)!(?<body>[A-Za-z0-9_~`<>$@?+.\-]+)",
        RegexOptions.Compiled);

    private static readonly string[] SystemModules =
    [
        "ntdll", "KERNELBASE", "KERNEL32", "USER32", "win32u", "combase",
        "ucrtbase", "MSVCRT", "RPCRT4", "sechost",
    ];

    private static readonly string[] DialogMarkers =
    [
        "MessageBoxW", "MessageBoxA", "MessageBoxTimeout", "MessageBoxWorker",
        "DialogBox2", "SoftModalMessageBox", "NtUserWaitMessage",
    ];

    public static IReadOnlyList<ThreadStack> Parse(string dump)
    {
        var result = new List<ThreadStack>();
        if (string.IsNullOrWhiteSpace(dump))
        {
            return result;
        }

        var index = -1;
        var systemId = (string?)null;
        var symbols = new List<string>();

        void Flush()
        {
            if (index >= 0)
            {
                result.Add(new ThreadStack(index, systemId, symbols.ToArray()));
            }
        }

        foreach (var line in dump.Split('\n'))
        {
            var header = ThreadHeader.Match(line);
            if (header.Success)
            {
                Flush();
                index = int.Parse(header.Groups["index"].Value);
                systemId = header.Groups["pid"].Value + "." + header.Groups["tid"].Value;
                symbols = new List<string>();
                continue;
            }

            if (index < 0)
            {
                continue;
            }

            foreach (Match match in FrameSymbol.Matches(line))
            {
                symbols.Add($"{match.Groups["module"].Value}!{match.Groups["body"].Value}");
            }
        }

        Flush();
        return result;
    }

    /// <summary>
    /// Scores threads and returns the most informative one: dialog waiters first, then threads with
    /// real user-code frames. The injected break thread is never chosen unless it is all there is.
    /// </summary>
    public static int? PickInteresting(IReadOnlyList<ThreadStack> threads, int? current = null)
    {
        if (threads.Count == 0)
        {
            return current;
        }

        var best = (Index: (int?)null, Score: int.MinValue);

        foreach (var thread in threads)
        {
            var joined = thread.Joined;
            if (joined.Contains("DbgUiRemoteBreakin", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            var score = 0;

            foreach (var marker in DialogMarkers)
            {
                if (joined.Contains(marker, StringComparison.OrdinalIgnoreCase))
                {
                    score += 100;
                    break;
                }
            }

            foreach (var symbol in thread.Symbols)
            {
                var module = symbol[..symbol.IndexOf('!')];
                if (!SystemModules.Any(m => module.Equals(m, StringComparison.OrdinalIgnoreCase)))
                {
                    score += 10;
                }
            }

            if (score > best.Score)
            {
                best = (thread.Index, score);
            }
        }

        return best.Index ?? current ?? threads[0].Index;
    }
}
