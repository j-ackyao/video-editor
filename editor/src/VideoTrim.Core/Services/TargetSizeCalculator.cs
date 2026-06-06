using VideoTrim.Core.Models;

namespace VideoTrim.Core.Services;

/// <summary>
/// Implements the target-size → video-bitrate budget math of §9.1.
/// Pure (no FFmpeg, no I/O), deterministic, and fully unit-tested.
/// </summary>
public sealed class TargetSizeCalculator : ITargetSizeCalculator
{
    /// <summary>3% headroom for container/muxing overhead + VBV slack (§9.1).</summary>
    public const double Safety = 0.97;

    /// <summary>Feasibility floor — below this, quality is pointless (§9.1).</summary>
    public const int MinVideoKbps = 100;

    public TargetSizeResult ComputeVideoBitrate(long targetSizeBytes, TimeSpan duration, AudioSettings audio)
    {
        double seconds = duration.TotalSeconds;

        // Guard the degenerate inputs the formula cannot divide by (decision: see decisions.md).
        if (seconds <= 0)
            return TargetSizeResult.Infeasible("The trim range is empty — choose a longer range.");

        if (targetSizeBytes <= 0)
            return TargetSizeResult.Infeasible("Enter a target size greater than zero.");

        int audioKbps = audio.IncludeAudio ? Math.Max(0, audio.AudioBitrateKbps) : 0;

        double budgetBits = targetSizeBytes * 8 * Safety;
        double audioBits = audioKbps * 1000.0 * seconds;
        double videoBits = budgetBits - audioBits;
        int videoKbps = (int)Math.Floor(videoBits / (1000.0 * seconds));

        if (videoKbps < MinVideoKbps)
        {
            return TargetSizeResult.Infeasible(
                "Can't reach this size at the chosen settings — try a lower resolution or fps, " +
                "raise the size, or exclude audio.");
        }

        return TargetSizeResult.FromFeasible(videoKbps);
    }
}
