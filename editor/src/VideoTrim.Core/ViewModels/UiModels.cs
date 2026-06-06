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

/// <summary>Binary size units for target-size inputs (§9.1 decision: binary MiB/KiB by default).</summary>
public enum SizeUnit
{
    KiB,
    MiB
}

public static class SizeUnits
{
    public static long ToBytes(double value, SizeUnit unit)
    {
        double factor = unit switch
        {
            SizeUnit.KiB => 1024d,
            SizeUnit.MiB => 1024d * 1024d,
            _ => 1d,
        };
        double bytes = value * factor;
        return bytes <= 0 ? 0 : (long)Math.Round(bytes);
    }
}

/// <summary>
/// A resolution choice in the dropdowns. <c>Height</c> null with <c>IsCustom</c> false = "Same as
/// source" (§7.3); <c>IsCustom</c> true reveals a numeric input for an arbitrary height.
/// </summary>
public sealed record ResolutionOption(string Label, int? Height, bool IsCustom = false);

/// <summary>
/// An fps choice in the dropdowns. <c>Fps</c> null with <c>IsCustom</c> false = "Same as source"
/// (§7.3); <c>IsCustom</c> true reveals a numeric input for an arbitrary frame rate.
/// </summary>
public sealed record FpsOption(string Label, double? Fps, bool IsCustom = false);

/// <summary>Builds the standard, source-aware dropdown option lists (§7.3, §7.6).</summary>
public static class ExportOptionCatalog
{
    /// <summary>Shared "Custom…" sentinels appended to the dropdowns to enable free-form entry.</summary>
    public static readonly ResolutionOption CustomResolution = new("Custom…", null, IsCustom: true);
    public static readonly FpsOption CustomFps = new("Custom…", null, IsCustom: true);

    private static readonly (string Label, int Height)[] StandardHeights =
    {
        ("2160p", 2160), ("1440p", 1440), ("1080p", 1080),
        ("720p", 720), ("480p", 480), ("360p", 360),
    };

    // Includes high-refresh options (144, 120) for high-fps sources, per UX feedback.
    private static readonly double[] StandardVideoFps = { 144, 120, 60, 30, 24, 15 };
    private static readonly double[] StandardGifFps = { 24, 20, 15, 12, 10, 8 };

    /// <summary>Resolutions ≤ source height plus "Same as source" and "Custom…" (§12).</summary>
    public static IReadOnlyList<ResolutionOption> ResolutionsFor(MediaInfo? source)
    {
        var list = new List<ResolutionOption> { new("Same as source", null) };
        int srcHeight = source?.Height ?? int.MaxValue;
        foreach (var (label, h) in StandardHeights)
        {
            if (h <= srcHeight)
                list.Add(new ResolutionOption(label, h));
        }
        list.Add(CustomResolution);
        return list;
    }

    /// <summary>Frame rates ≤ source fps plus "Same as source" and "Custom…" (don't fabricate by default, §12).</summary>
    public static IReadOnlyList<FpsOption> VideoFpsFor(MediaInfo? source)
    {
        var list = new List<FpsOption> { new("Same as source", null) };
        double srcFps = source?.FrameRate ?? double.MaxValue;
        foreach (double f in StandardVideoFps)
        {
            if (f <= srcFps + 0.01)
                list.Add(new FpsOption($"{f:0.##} fps", f));
        }
        list.Add(CustomFps);
        return list;
    }

    public static IReadOnlyList<FpsOption> GifFpsFor(MediaInfo? source)
    {
        var list = new List<FpsOption>();
        double srcFps = source?.FrameRate ?? double.MaxValue;
        foreach (double f in StandardGifFps)
        {
            if (f <= srcFps + 0.01)
                list.Add(new FpsOption($"{f:0.##} fps", f));
        }
        if (list.Count == 0)
            list.Add(new FpsOption($"{srcFps:0.##} fps", srcFps));
        list.Add(CustomFps);
        return list;
    }

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
