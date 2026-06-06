namespace VideoTrim.Core.Models;

/// <summary>
/// Result of the target-size → video-bitrate calculation (§9.1, §6.5). Either a feasible video
/// bitrate to feed a two-pass encode, or an infeasible result so the UI can warn instead of
/// silently producing a worse-than-expected file.
/// </summary>
public sealed record TargetSizeResult
{
    private TargetSizeResult(bool feasible, int videoKbps, string? message)
    {
        Feasible = feasible;
        VideoKbps = videoKbps;
        Message = message;
    }

    public bool Feasible { get; }

    /// <summary>The video bitrate (kbps) to request from a two-pass encode. Valid only when feasible.</summary>
    public int VideoKbps { get; }

    /// <summary>User-facing suggestion when infeasible (null when feasible).</summary>
    public string? Message { get; }

    public static TargetSizeResult FromFeasible(int videoKbps) => new(true, videoKbps, null);

    public static TargetSizeResult Infeasible(string message) => new(false, 0, message);
}
