using VideoTrim.Core.Models;

namespace VideoTrim.Core.Encoding;

/// <summary>
/// Pure builder that translates settings + trim range into ffmpeg/ffprobe argument token lists
/// (§10). No process is launched here, so the §10 command mappings can be unit-tested by
/// comparing against golden expected argument lists (§13). Tokens are returned unquoted: callers
/// pass them straight to <c>ProcessStartInfo.ArgumentList</c>, which handles escaping.
/// </summary>
public interface IFfmpegCommandBuilder
{
    /// <summary>ffprobe args to read media metadata (§10.4). Run with the ffprobe binary.</summary>
    IReadOnlyList<string> BuildProbeArgs(string inputPath);

    /// <summary>Single-pass CRF / constant-quality export (§10.1) — the default path.</summary>
    IReadOnlyList<string> BuildCrfExportArgs(
        string inputPath, string outputPath, TrimRange range, ExportSettings settings, MediaInfo source);

    /// <summary>One pass (1 or 2) of a two-pass average-bitrate export (§10.2).</summary>
    IReadOnlyList<string> BuildTwoPassArgs(
        int pass, string inputPath, string outputPath, TrimRange range, ExportSettings settings,
        MediaInfo source, int videoKbps, string passLogPrefix, string nullSink);

    /// <summary>GIF step 1 — generate an optimized palette (§10.3).</summary>
    IReadOnlyList<string> BuildGifPaletteGenArgs(
        string inputPath, string palettePath, TrimRange range, GifSettings settings);

    /// <summary>GIF step 2 — apply the palette to produce the GIF (§10.3).</summary>
    IReadOnlyList<string> BuildGifPaletteUseArgs(
        string inputPath, string palettePath, string outputPath, TrimRange range, GifSettings settings);
}
