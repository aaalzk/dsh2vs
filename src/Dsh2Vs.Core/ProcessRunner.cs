using System.Diagnostics;
using System.Text;

namespace Dsh2Vs.Core;

public static class ProcessRunner
{
    public static async Task<ProcessResult> RunAsync(
        string fileName,
        IEnumerable<string> arguments,
        string? workingDirectory,
        int timeoutSeconds,
        CancellationToken cancellationToken)
    {
        var psi = new ProcessStartInfo
        {
            FileName = fileName,
            WorkingDirectory = workingDirectory ?? Environment.CurrentDirectory,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            RedirectStandardInput = false,
            UseShellExecute = false,
            CreateNoWindow = true,
            StandardOutputEncoding = Encoding.UTF8,
            StandardErrorEncoding = Encoding.UTF8,
        };

        foreach (var a in arguments)
        {
            psi.ArgumentList.Add(a);
        }

        var sw = Stopwatch.StartNew();
        using var process = new Process { StartInfo = psi };

        if (!process.Start())
        {
            return new ProcessResult(-1, string.Empty, "failed to start process", 0, false);
        }

        var stdoutTask = process.StandardOutput.ReadToEndAsync(cancellationToken);
        var stderrTask = process.StandardError.ReadToEndAsync(cancellationToken);

        using var timeoutCts = new CancellationTokenSource(TimeSpan.FromSeconds(Math.Max(1, timeoutSeconds)));
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(timeoutCts.Token, cancellationToken);

        var timedOut = false;
        try
        {
            await process.WaitForExitAsync(linked.Token).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            timedOut = timeoutCts.IsCancellationRequested && !cancellationToken.IsCancellationRequested;
            try
            {
                if (!process.HasExited)
                {
                    process.Kill(entireProcessTree: true);
                }
            }
            catch
            {
                // best effort
            }

            if (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
        }

        var stdout = await SafeAwait(stdoutTask).ConfigureAwait(false);
        var stderr = await SafeAwait(stderrTask).ConfigureAwait(false);
        sw.Stop();

        return new ProcessResult(
            timedOut ? -1 : process.ExitCode,
            stdout,
            stderr,
            sw.ElapsedMilliseconds,
            timedOut);
    }

    private static async Task<string> SafeAwait(Task<string> task)
    {
        try
        {
            return await task.ConfigureAwait(false);
        }
        catch
        {
            return string.Empty;
        }
    }
}
