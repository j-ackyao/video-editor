using System.Diagnostics;

namespace VideoTrim.Core.Abstractions;

/// <summary>
/// Real <see cref="IProcessRunner"/> over <see cref="System.Diagnostics.Process"/>. Redirects
/// stdout (ffmpeg <c>-progress pipe:1</c>) and stderr, keeps a bounded stderr tail for diagnostics,
/// and kills the entire process tree on cancellation (§11).
/// </summary>
public sealed class ProcessRunner : IProcessRunner
{
    private const int StdErrTailLines = 60;

    public async Task<ProcessResult> RunAsync(
        string executable,
        IReadOnlyList<string> arguments,
        string? workingDirectory,
        Action<string>? onStdoutLine,
        Action<string>? onStderrLine,
        CancellationToken ct)
    {
        var psi = new ProcessStartInfo
        {
            FileName = executable,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
            WorkingDirectory = workingDirectory ?? Environment.CurrentDirectory,
        };
        foreach (var a in arguments)
            psi.ArgumentList.Add(a);

        using var process = new Process { StartInfo = psi, EnableRaisingEvents = true };

        var stderrTail = new Queue<string>();
        var stdoutClosed = new TaskCompletionSource();
        var stderrClosed = new TaskCompletionSource();

        process.OutputDataReceived += (_, e) =>
        {
            if (e.Data is null) { stdoutClosed.TrySetResult(); return; }
            onStdoutLine?.Invoke(e.Data);
        };
        process.ErrorDataReceived += (_, e) =>
        {
            if (e.Data is null) { stderrClosed.TrySetResult(); return; }
            lock (stderrTail)
            {
                stderrTail.Enqueue(e.Data);
                while (stderrTail.Count > StdErrTailLines)
                    stderrTail.Dequeue();
            }
            onStderrLine?.Invoke(e.Data);
        };

        try
        {
            if (!process.Start())
                throw new InvalidOperationException($"Failed to start process: {executable}");
        }
        catch (Exception ex) when (ex is not InvalidOperationException)
        {
            throw new InvalidOperationException(
                $"Failed to start process '{executable}'. Is FFmpeg installed/bundled?", ex);
        }

        process.BeginOutputReadLine();
        process.BeginErrorReadLine();

        try
        {
            await process.WaitForExitAsync(ct).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            TryKillTree(process);
            throw;
        }

        // Give the async readers a brief moment to drain after exit.
        await Task.WhenAny(
            Task.WhenAll(stdoutClosed.Task, stderrClosed.Task),
            Task.Delay(2000, CancellationToken.None)).ConfigureAwait(false);

        string tail;
        lock (stderrTail)
            tail = string.Join(Environment.NewLine, stderrTail);

        return new ProcessResult(process.ExitCode, tail);
    }

    private static void TryKillTree(Process process)
    {
        try
        {
            if (!process.HasExited)
                process.Kill(entireProcessTree: true);
        }
        catch
        {
            // Best effort: process may have already exited.
        }
    }
}
