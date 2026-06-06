namespace VideoTrim.Core.Models;

/// <summary>
/// Immutable description of the loaded source video, produced by <c>ffprobe</c> (§5, §6.1).
/// Single source of truth for duration, dimensions, fps and whether an audio stream exists.
/// </summary>
public sealed record MediaInfo(
    string FilePath,
    TimeSpan Duration,
    int Width,
    int Height,
    double FrameRate,      // e.g. 29.97
    string VideoCodec,     // e.g. "h264"
    bool HasAudio,
    string? AudioCodec,
    long FileSizeBytes)
{
    /// <summary>Source aspect ratio (width / height); 0 when height is unknown.</summary>
    public double AspectRatio => Height > 0 ? (double)Width / Height : 0;
}
