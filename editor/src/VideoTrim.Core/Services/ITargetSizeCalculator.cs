using VideoTrim.Core.Models;

namespace VideoTrim.Core.Services;

/// <summary>
/// Pure bitrate math for "target output size" video exports (§6.5, §9.1). Has no FFmpeg
/// dependency and is the most important thing to unit-test.
/// </summary>
public interface ITargetSizeCalculator
{
    /// <summary>
    /// Returns the video bitrate (kbps) to request from a two-pass encode, or an infeasible
    /// result when the budget falls below the quality floor.
    /// </summary>
    TargetSizeResult ComputeVideoBitrate(long targetSizeBytes, TimeSpan duration, AudioSettings audio);
}
