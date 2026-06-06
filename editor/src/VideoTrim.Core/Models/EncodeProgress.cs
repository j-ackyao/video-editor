namespace VideoTrim.Core.Models;

/// <summary>
/// Progress snapshot streamed back to the UI during encoding (§5, §6.4). All fields are
/// best-effort, parsed from ffmpeg's <c>-progress pipe:1</c> output.
/// </summary>
public sealed record EncodeProgress(
    double Percent,           // 0..100, best-effort
    TimeSpan ProcessedTime,   // how much of the trimmed range is done
    double? SpeedX,           // encode speed multiple (e.g. 3.2x), if parseable
    string RawLine);          // last ffmpeg status line (for diagnostics/log)
