namespace VideoTrim.Core.Models;

/// <summary>Dithering algorithm used by the GIF <c>paletteuse</c> step (§10.3).</summary>
public enum DitherMode
{
    None,
    Bayer,
    FloydSteinberg
}

/// <summary>
/// Settings for a GIF export (§5, US6 §7.6). GIF has no audio and no bitrate; the primary
/// "compression" knob is <see cref="MaxColors"/> (Assumption §7.6).
/// </summary>
public sealed class GifSettings
{
    /// <summary>null = keep source height.</summary>
    public int? TargetHeight { get; set; }

    /// <summary>GIFs are typically low-fps; default 15 (§7.6).</summary>
    public double TargetFps { get; set; } = 15;

    /// <summary>2..256 — primary compression knob; fewer colors = smaller file (§7.6).</summary>
    public int MaxColors { get; set; } = 256;

    public DitherMode Dither { get; set; } = DitherMode.Bayer;

    /// <summary>Optional; drives the bounded iterative loop in §9.2.</summary>
    public long? TargetSizeBytes { get; set; }
}
