namespace VideoTrim.Core.Abstractions;

/// <summary>Outcome of a finished child process: exit code plus the tail of its stderr (§12 logging).</summary>
public sealed record ProcessResult(int ExitCode, string StdErrTail);

/// <summary>
/// Abstraction over launching a child process (ffmpeg/ffprobe). Exists so the encoding/probe
/// services can be unit-tested with a fake runner — no real ffmpeg required (§13).
/// </summary>
public interface IProcessRunner
{
    /// <summary>
    /// Runs <paramref name="executable"/> with <paramref name="arguments"/>, streaming each stdout
    /// and stderr line back via the callbacks. Honors cancellation by killing the whole process
    /// tree (§11). Returns the exit code and a stderr tail.
    /// </summary>
    Task<ProcessResult> RunAsync(
        string executable,
        IReadOnlyList<string> arguments,
        string? workingDirectory,
        Action<string>? onStdoutLine,
        Action<string>? onStderrLine,
        CancellationToken ct);
}
