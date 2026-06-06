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

/// <summary>A resolution choice in the dropdowns. Null height = "Same as source" (§7.3).</summary>
public sealed record ResolutionOption(string Label, int? Height);

/// <summary>An fps choice in the dropdowns. Null fps = "Same as source" (§7.3).</summary>
public sealed record FpsOption(string Label, double? Fps);

/// <summary>Builds the standard, source-aware dropdown option lists (§7.3, §7.6).</summary>
public static class ExportOptionCatalog
{
    private static readonly (string Label, int Height)[] StandardHeights =
    {
        ("2160p", 2160), ("1440p", 1440), ("1080p", 1080),
        ("720p", 720), ("480p", 480), ("360p", 360),
    };

    private static readonly double[] StandardVideoFps = { 60, 30, 24, 15 };
    private static readonly double[] StandardGifFps = { 24, 20, 15, 12, 10, 8 };

    /// <summary>Resolutions ≤ source height plus "Same as source" (default options cap at source, §12).</summary>
    public static IReadOnlyList<ResolutionOption> ResolutionsFor(MediaInfo? source)
    {
        var list = new List<ResolutionOption> { new("Same as source", null) };
        int srcHeight = source?.Height ?? int.MaxValue;
        foreach (var (label, h) in StandardHeights)
        {
            if (h <= srcHeight)
                list.Add(new ResolutionOption(label, h));
        }
        return list;
    }

    /// <summary>Frame rates ≤ source fps plus "Same as source" (don't fabricate frames, §12).</summary>
    public static IReadOnlyList<FpsOption> VideoFpsFor(MediaInfo? source)
    {
        var list = new List<FpsOption> { new("Same as source", null) };
        double srcFps = source?.FrameRate ?? double.MaxValue;
        foreach (double f in StandardVideoFps)
        {
            if (f <= srcFps + 0.01)
                list.Add(new FpsOption($"{f:0.##} fps", f));
        }
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
        return list;
    }
}
