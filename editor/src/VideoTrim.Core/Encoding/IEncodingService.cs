using VideoTrim.Core.Models;

namespace VideoTrim.Core.Encoding;

/// <summary>
/// The core export engine (§6.4). Translates settings + trim range into ffmpeg command line(s),
/// orchestrates multi-step (two-pass / GIF) encodes, reports progress, and honors cancellation.
/// </summary>
public interface IEncodingService
{
    Task<ExportResult> ExportVideoAsync(
        string inputPath, string outputPath,
        TrimRange range, ExportSettings settings,
        MediaInfo source,
        IProgress<EncodeProgress> progress, CancellationToken ct);

    Task<ExportResult> ExportGifAsync(
        string inputPath, string outputPath,
        TrimRange range, GifSettings settings,
        MediaInfo source,
        IProgress<EncodeProgress> progress, CancellationToken ct);
}
