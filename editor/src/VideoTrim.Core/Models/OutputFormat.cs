namespace VideoTrim.Core.Models;

/// <summary>Output container chosen by the user (§5). The app maps each to a default codec (§10.0).</summary>
public enum OutputFormat
{
    Mp4,
    Mov,
    Webm,
    Mkv,
    Gif
}

/// <summary>
/// Static format → container/extension/codec capability table (§10.0). Keeping the mapping in one
/// place means a container choice in the UI deterministically selects sane default codecs without
/// asking the user to pick a codec (Assumption: format → codec mapping, §7.3).
/// </summary>
public static class FormatCatalog
{
    public sealed record FormatSpec(
        OutputFormat Format,
        string Extension,        // includes leading dot, e.g. ".mp4"
        string? VideoCodec,      // ffmpeg encoder name, null for GIF (palette pipeline)
        string? AudioCodec,      // null when the container carries no audio (GIF)
        bool SupportsAudio,
        string MuxFormat);       // ffmpeg -f value used for the null sink in pass 1 / forced muxer

    private static readonly IReadOnlyDictionary<OutputFormat, FormatSpec> Specs =
        new Dictionary<OutputFormat, FormatSpec>
        {
            [OutputFormat.Mp4] = new(OutputFormat.Mp4, ".mp4", "libx264", "aac", true, "mp4"),
            [OutputFormat.Mov] = new(OutputFormat.Mov, ".mov", "libx264", "aac", true, "mov"),
            [OutputFormat.Mkv] = new(OutputFormat.Mkv, ".mkv", "libx264", "aac", true, "matroska"),
            [OutputFormat.Webm] = new(OutputFormat.Webm, ".webm", "libvpx-vp9", "libopus", true, "webm"),
            [OutputFormat.Gif] = new(OutputFormat.Gif, ".gif", null, null, false, "gif"),
        };

    public static FormatSpec Get(OutputFormat format) => Specs[format];

    /// <summary>True when the format wrapper supports an audio stream (everything except GIF).</summary>
    public static bool SupportsAudio(OutputFormat format) => Specs[format].SupportsAudio;

    /// <summary>True when MP4-style web fast-start (`-movflags +faststart`) applies (§10.1).</summary>
    public static bool SupportsFastStart(OutputFormat format) =>
        format is OutputFormat.Mp4 or OutputFormat.Mov;
}
