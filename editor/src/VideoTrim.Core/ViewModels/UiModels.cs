using System.Globalization;
using VideoTrim.Core.Models;

namespace VideoTrim.Core.ViewModels;

/// <summary>UI-agnostic severity for the status/info banner (mapped to InfoBarSeverity in the view).</summary>
public enum InfoSeverity
{
    Informational,
    Success,
    Warning,
    Error
}

/// <summary>
/// Builds the editable-combo preset lists (§7.3, §7.6). Each list is a set of display strings the
/// user can pick or type over; "Same as source" is a sentinel handled by <see cref="ComboFieldViewModel"/>.
/// </summary>
public static class ExportOptionCatalog
{
    public const string SameAsSource = "Same as source";

    private static readonly int[] StandardHeights = { 2160, 1440, 1080, 720, 480, 360 };

    // Includes high-refresh options (144, 120) for high-fps sources, per UX feedback.
    private static readonly double[] StandardVideoFps = { 144, 120, 60, 30, 24, 15 };
    private static readonly double[] StandardGifFps = { 24, 20, 15, 12, 10, 8 };

    /// <summary>Heights ≤ source (so we don't upscale by default, §12), preceded by "Same as source".</summary>
    public static IReadOnlyList<string> HeightOptions(MediaInfo? source)
    {
        var list = new List<string> { SameAsSource };
        int srcHeight = source?.Height ?? int.MaxValue;
        foreach (int h in StandardHeights)
        {
            if (h <= srcHeight)
                list.Add(h.ToString(CultureInfo.InvariantCulture));
        }
        return list;
    }

    /// <summary>Frame rates ≤ source (don't fabricate frames by default, §12), preceded by "Same as source".</summary>
    public static IReadOnlyList<string> VideoFpsOptions(MediaInfo? source)
    {
        var list = new List<string> { SameAsSource };
        double srcFps = source?.FrameRate ?? double.MaxValue;
        foreach (double f in StandardVideoFps)
        {
            if (f <= srcFps + 0.01)
                list.Add(f.ToString("0.###", CultureInfo.InvariantCulture));
        }
        return list;
    }

    public static IReadOnlyList<string> GifFpsOptions(MediaInfo? source)
    {
        var list = new List<string> { SameAsSource };
        double srcFps = source?.FrameRate ?? double.MaxValue;
        foreach (double f in StandardGifFps)
        {
            if (f <= srcFps + 0.01)
                list.Add(f.ToString("0.###", CultureInfo.InvariantCulture));
        }
        return list;
    }

    /// <summary>Popular video bitrate presets (kbps).</summary>
    public static IReadOnlyList<string> VideoBitrateOptions() =>
        new[] { "8000", "5000", "2500", "1000", "500" };

    /// <summary>Popular target-size presets (with units).</summary>
    public static IReadOnlyList<string> TargetSizeOptions() =>
        new[] { "25 MB", "10 MB", "5 MB", "2 MB", "1 MB", "500 KB" };

    /// <summary>Audio bitrate presets (kbps).</summary>
    public static IReadOnlyList<string> AudioBitrateOptions() =>
        new[] { "320", "256", "192", "128", "96", "64" };

    /// <summary>Forces an even, positive height (yuv420p requires even dimensions).</summary>
    public static int? NormalizeHeight(int? height)
    {
        if (height is not { } h || h <= 0)
            return null;
        if (h % 2 != 0)
            h -= 1;
        return Math.Max(2, h);
    }
}
