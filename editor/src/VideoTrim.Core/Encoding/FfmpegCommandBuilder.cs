using System.Globalization;
using VideoTrim.Core.Models;

namespace VideoTrim.Core.Encoding;

/// <summary>
/// Default <see cref="IFfmpegCommandBuilder"/>. Encodes every §10 command exactly, branching on
/// the container's codec (via <see cref="FormatCatalog"/>) and on whether audio is present/wanted.
/// </summary>
public sealed class FfmpegCommandBuilder : IFfmpegCommandBuilder
{
    public IReadOnlyList<string> BuildProbeArgs(string inputPath) => new[]
    {
        "-v", "quiet",
        "-print_format", "json",
        "-show_format",
        "-show_streams",
        inputPath,
    };

    public IReadOnlyList<string> BuildCrfExportArgs(
        string inputPath, string outputPath, TrimRange range, ExportSettings settings, MediaInfo source)
    {
        var spec = FormatCatalog.Get(settings.Format);
        var args = new List<string>();

        AddInput(args, inputPath, range);
        AddVideoFilter(args, settings.TargetHeight, settings.TargetFps);

        // Constant-quality video. VP9 needs -b:v 0 alongside -crf to enable true CQ mode.
        args.Add("-c:v");
        args.Add(spec.VideoCodec!);
        args.Add("-crf");
        args.Add(settings.QualityCrf.ToString(CultureInfo.InvariantCulture));
        if (spec.VideoCodec == "libvpx-vp9")
        {
            args.Add("-b:v");
            args.Add("0");
        }
        args.Add("-preset");
        args.Add("medium");
        args.Add("-pix_fmt");
        args.Add("yuv420p");

        AddAudioArgs(args, settings, source, spec);
        AddFastStart(args, settings.Format);
        AddProgress(args);
        args.Add(outputPath);
        return args;
    }

    public IReadOnlyList<string> BuildTwoPassArgs(
        int pass, string inputPath, string outputPath, TrimRange range, ExportSettings settings,
        MediaInfo source, int videoKbps, string passLogPrefix, string nullSink)
    {
        if (pass is not (1 or 2))
            throw new ArgumentOutOfRangeException(nameof(pass), pass, "Pass must be 1 or 2.");

        var spec = FormatCatalog.Get(settings.Format);
        var args = new List<string>();

        AddInput(args, inputPath, range);
        AddVideoFilter(args, settings.TargetHeight, settings.TargetFps);

        args.Add("-c:v");
        args.Add(spec.VideoCodec!);
        args.Add("-b:v");
        args.Add($"{videoKbps}k");
        args.Add("-pass");
        args.Add(pass.ToString(CultureInfo.InvariantCulture));
        args.Add("-passlogfile");
        args.Add(passLogPrefix);
        args.Add("-preset");
        args.Add("medium");
        args.Add("-pix_fmt");
        args.Add("yuv420p");

        if (pass == 1)
        {
            // Pass 1 only analyses the video; drop audio and write to the null sink.
            args.Add("-an");
            args.Add("-f");
            args.Add(spec.MuxFormat);
            AddProgress(args);
            args.Add(nullSink);
        }
        else
        {
            AddAudioArgs(args, settings, source, spec);
            AddFastStart(args, settings.Format);
            AddProgress(args);
            args.Add(outputPath);
        }

        return args;
    }

    public IReadOnlyList<string> BuildGifPaletteGenArgs(
        string inputPath, string palettePath, TrimRange range, GifSettings settings)
    {
        var args = new List<string>();
        AddInput(args, inputPath, range);
        string pre = GifPreFilter(settings);
        int colors = Math.Clamp(settings.MaxColors, 2, 256);
        args.Add("-vf");
        args.Add($"{pre},palettegen=max_colors={colors.ToString(CultureInfo.InvariantCulture)}");
        args.Add(palettePath);
        return args;
    }

    public IReadOnlyList<string> BuildGifPaletteUseArgs(
        string inputPath, string palettePath, string outputPath, TrimRange range, GifSettings settings)
    {
        var args = new List<string>();
        AddInput(args, inputPath, range);
        args.Add("-i");
        args.Add(palettePath);
        string pre = GifPreFilter(settings);
        args.Add("-lavfi");
        args.Add($"{pre}[x];[x][1:v]paletteuse=dither={DitherToken(settings.Dither)}");
        AddProgress(args);
        args.Add(outputPath);
        return args;
    }

    // ---- shared fragments --------------------------------------------------

    private static void AddInput(List<string> args, string inputPath, TrimRange range)
    {
        // -ss/-to before -i = accurate input seeking; re-encode trims to the exact start (§9.3).
        args.Add("-y");
        args.Add("-hide_banner");
        args.Add("-ss");
        args.Add(FormatSeconds(range.Start));
        args.Add("-to");
        args.Add(FormatSeconds(range.End));
        args.Add("-i");
        args.Add(inputPath);
    }

    private static void AddVideoFilter(List<string> args, int? targetHeight, double? targetFps)
    {
        string? filter = BuildScaleFpsFilter(targetHeight, targetFps);
        if (filter is null)
            return;
        args.Add("-vf");
        args.Add(filter);
    }

    /// <summary>scale=-2:{h} keeps aspect ratio and forces even width; omit parts that are null (§10.0).</summary>
    internal static string? BuildScaleFpsFilter(int? targetHeight, double? targetFps)
    {
        var parts = new List<string>();
        if (targetHeight.HasValue)
            parts.Add($"scale=-2:{targetHeight.Value.ToString(CultureInfo.InvariantCulture)}");
        if (targetFps.HasValue)
            parts.Add($"fps={FormatFps(targetFps.Value)}");
        return parts.Count == 0 ? null : string.Join(",", parts);
    }

    private static string GifPreFilter(GifSettings settings)
    {
        var parts = new List<string> { $"fps={FormatFps(settings.TargetFps)}" };
        if (settings.TargetHeight.HasValue)
            parts.Add($"scale=-2:{settings.TargetHeight.Value.ToString(CultureInfo.InvariantCulture)}:flags=lanczos");
        return string.Join(",", parts);
    }

    private static void AddAudioArgs(List<string> args, ExportSettings settings, MediaInfo source, FormatCatalog.FormatSpec spec)
    {
        bool include = settings.Audio.IncludeAudio && source.HasAudio && spec.SupportsAudio;
        if (!include)
        {
            args.Add("-an");
            return;
        }
        args.Add("-c:a");
        args.Add(spec.AudioCodec!);
        args.Add("-b:a");
        args.Add($"{settings.Audio.AudioBitrateKbps}k");
    }

    private static void AddFastStart(List<string> args, OutputFormat format)
    {
        if (FormatCatalog.SupportsFastStart(format))
        {
            args.Add("-movflags");
            args.Add("+faststart");
        }
    }

    private static void AddProgress(List<string> args)
    {
        args.Add("-progress");
        args.Add("pipe:1");
        args.Add("-nostats");
    }

    private static string DitherToken(DitherMode dither) => dither switch
    {
        DitherMode.None => "none",
        DitherMode.Bayer => "bayer:bayer_scale=3",
        DitherMode.FloydSteinberg => "floyd_steinberg",
        _ => "bayer:bayer_scale=3",
    };

    internal static string FormatSeconds(TimeSpan t) =>
        t.TotalSeconds.ToString("0.000", CultureInfo.InvariantCulture);

    internal static string FormatFps(double fps) =>
        fps.ToString("0.###", CultureInfo.InvariantCulture);
}
