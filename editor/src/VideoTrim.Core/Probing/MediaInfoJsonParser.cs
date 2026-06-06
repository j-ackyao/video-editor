using System.Globalization;
using System.Text.Json;
using VideoTrim.Core.Models;

namespace VideoTrim.Core.Probing;

/// <summary>
/// Pure mapper from the <c>ffprobe -print_format json</c> document to <see cref="MediaInfo"/>
/// (§6.1). Kept separate from process execution so the JSON mapping is unit-testable (§13).
/// </summary>
public static class MediaInfoJsonParser
{
    public static MediaInfo Parse(string json, string filePath, long? fileSizeBytesOverride = null)
    {
        using var doc = JsonDocument.Parse(json);
        JsonElement root = doc.RootElement;

        if (!root.TryGetProperty("streams", out var streams) || streams.ValueKind != JsonValueKind.Array)
            throw new FormatException("ffprobe output contained no streams.");

        JsonElement? video = null;
        JsonElement? audio = null;
        foreach (var s in streams.EnumerateArray())
        {
            string? type = s.TryGetProperty("codec_type", out var t) ? t.GetString() : null;
            if (type == "video" && video is null)
                video = s;
            else if (type == "audio" && audio is null)
                audio = s;
        }

        if (video is null)
            throw new FormatException("No video stream found in the file.");

        var v = video.Value;
        int width = GetInt(v, "width") ?? 0;
        int height = GetInt(v, "height") ?? 0;
        string videoCodec = GetString(v, "codec_name") ?? "unknown";
        double frameRate = ParseRational(GetString(v, "r_frame_rate"))
            ?? ParseRational(GetString(v, "avg_frame_rate"))
            ?? 0;

        bool hasAudio = audio is not null;
        string? audioCodec = audio is null ? null : GetString(audio.Value, "codec_name");

        TimeSpan duration = TimeSpan.Zero;
        long fileSize = fileSizeBytesOverride ?? 0;
        if (root.TryGetProperty("format", out var format))
        {
            double? durSec = ParseDouble(GetString(format, "duration"));
            if (durSec.HasValue)
                duration = TimeSpan.FromSeconds(durSec.Value);

            if (fileSizeBytesOverride is null)
            {
                long? sz = ParseLong(GetString(format, "size"));
                if (sz.HasValue)
                    fileSize = sz.Value;
            }
        }

        // Fall back to a stream-level duration if the container omitted it.
        if (duration == TimeSpan.Zero)
        {
            double? streamDur = ParseDouble(GetString(v, "duration"));
            if (streamDur.HasValue)
                duration = TimeSpan.FromSeconds(streamDur.Value);
        }

        return new MediaInfo(
            FilePath: filePath,
            Duration: duration,
            Width: width,
            Height: height,
            FrameRate: frameRate,
            VideoCodec: videoCodec,
            HasAudio: hasAudio,
            AudioCodec: audioCodec,
            FileSizeBytes: fileSize);
    }

    /// <summary>Parses ffprobe's "num/den" rational (e.g. "30000/1001"); returns null if unusable.</summary>
    internal static double? ParseRational(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return null;
        int slash = value.IndexOf('/');
        if (slash < 0)
            return ParseDouble(value);

        if (long.TryParse(value[..slash], NumberStyles.Integer, CultureInfo.InvariantCulture, out long num) &&
            long.TryParse(value[(slash + 1)..], NumberStyles.Integer, CultureInfo.InvariantCulture, out long den) &&
            den != 0)
        {
            return (double)num / den;
        }
        return null;
    }

    private static int? GetInt(JsonElement e, string name) =>
        e.TryGetProperty(name, out var p) && p.ValueKind == JsonValueKind.Number ? p.GetInt32() : null;

    private static string? GetString(JsonElement e, string name) =>
        e.TryGetProperty(name, out var p) ? p.ToString() : null;

    private static double? ParseDouble(string? s) =>
        double.TryParse(s, NumberStyles.Float, CultureInfo.InvariantCulture, out double d) ? d : null;

    private static long? ParseLong(string? s) =>
        long.TryParse(s, NumberStyles.Integer, CultureInfo.InvariantCulture, out long l) ? l : null;
}
