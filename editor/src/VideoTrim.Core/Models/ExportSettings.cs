namespace VideoTrim.Core.Models;

/// <summary>The active bitrate strategy for a video export (§5). Exactly one is in effect.</summary>
public enum BitrateMode
{
    /// <summary>Constant Rate Factor — quality-targeted, single pass (§10.1).</summary>
    Quality,

    /// <summary>User-supplied average video bitrate — two pass (§10.2).</summary>
    Bitrate,

    /// <summary>Target output file size — bitrate computed (§9.1) then two pass (§10.2).</summary>
    TargetSize
}

/// <summary>
/// All settings for a (non-GIF) video export (§5). Spatial cropping is out of scope — only the
/// time range and these transcode options change; aspect ratio is preserved (width derived).
/// </summary>
public sealed class ExportSettings
{
    public OutputFormat Format { get; set; } = OutputFormat.Mp4;

    /// <summary>null = keep source height; width auto (even) to keep aspect ratio (§10.0).</summary>
    public int? TargetHeight { get; set; }

    /// <summary>null = keep source fps.</summary>
    public double? TargetFps { get; set; }

    public AudioSettings Audio { get; set; } = new();

    public BitrateMode Mode { get; set; } = BitrateMode.Quality;

    /// <summary>Used when <see cref="Mode"/> == <see cref="BitrateMode.Quality"/> (§7.3 default 23).</summary>
    public int QualityCrf { get; set; } = 23;

    /// <summary>Used when <see cref="Mode"/> == <see cref="BitrateMode.Bitrate"/>.</summary>
    public int? VideoBitrateKbps { get; set; }

    /// <summary>Used when <see cref="Mode"/> == <see cref="BitrateMode.TargetSize"/>.</summary>
    public long? TargetSizeBytes { get; set; }
}
