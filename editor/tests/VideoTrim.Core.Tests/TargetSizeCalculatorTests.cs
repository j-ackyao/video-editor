using VideoTrim.Core.Models;
using VideoTrim.Core.Services;

namespace VideoTrim.Core.Tests;

public sealed class TargetSizeCalculatorTests
{
    private readonly TargetSizeCalculator _calc = new();

    [Fact]
    public void Feasible_WithAudio_ComputesExpectedBitrate()
    {
        // 10 MiB, 60 s, 128 kbps audio.
        long target = 10L * 1024 * 1024;
        var result = _calc.ComputeVideoBitrate(target, TimeSpan.FromSeconds(60),
            new AudioSettings { IncludeAudio = true, AudioBitrateKbps = 128 });

        // budgetBits = 10485760*8*0.97 = 81369497.6
        // audioBits  = 128*1000*60     = 7,680,000
        // videoKbps  = floor((81369497.6 - 7680000) / 60000) = floor(1228.16) = 1228
        Assert.True(result.Feasible);
        Assert.Equal(1228, result.VideoKbps);
    }

    [Fact]
    public void ExcludingAudio_FreesEntireBudget()
    {
        long target = 10L * 1024 * 1024;
        var withAudio = _calc.ComputeVideoBitrate(target, TimeSpan.FromSeconds(60),
            new AudioSettings { IncludeAudio = true, AudioBitrateKbps = 128 });
        var noAudio = _calc.ComputeVideoBitrate(target, TimeSpan.FromSeconds(60),
            new AudioSettings { IncludeAudio = false, AudioBitrateKbps = 128 });

        Assert.Equal(1356, noAudio.VideoKbps); // floor(81369497.6 / 60000)
        Assert.True(noAudio.VideoKbps > withAudio.VideoKbps);
    }

    [Fact]
    public void SafetyMargin_KeepsEstimateUnderTarget()
    {
        long target = 8L * 1024 * 1024; // 8 MiB
        var duration = TimeSpan.FromSeconds(30);
        var result = _calc.ComputeVideoBitrate(target, duration,
            new AudioSettings { IncludeAudio = false });

        // Reconstruct file size from the chosen bitrate; it must be <= target.
        long videoBytes = (long)(result.VideoKbps * 1000.0 * duration.TotalSeconds / 8);
        Assert.True(videoBytes <= target, $"{videoBytes} should be <= {target}");
    }

    [Fact]
    public void BelowFloor_IsInfeasible()
    {
        long target = 100L * 1024; // 100 KiB
        var result = _calc.ComputeVideoBitrate(target, TimeSpan.FromSeconds(60),
            new AudioSettings { IncludeAudio = true, AudioBitrateKbps = 128 });

        Assert.False(result.Feasible);
        Assert.False(string.IsNullOrWhiteSpace(result.Message));
    }

    [Fact]
    public void ZeroDuration_IsInfeasible()
    {
        var result = _calc.ComputeVideoBitrate(10L * 1024 * 1024, TimeSpan.Zero, new AudioSettings());
        Assert.False(result.Feasible);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-5)]
    public void NonPositiveTarget_IsInfeasible(long target)
    {
        var result = _calc.ComputeVideoBitrate(target, TimeSpan.FromSeconds(10), new AudioSettings());
        Assert.False(result.Feasible);
    }

    [Fact]
    public void JustAboveFloor_IsFeasible()
    {
        // Pick a target so the floor (100 kbps) is just cleared with no audio.
        // videoKbps = floor(target*8*0.97 / (1000*duration)); choose duration=10s.
        // Need >= 100: target*8*0.97/10000 >= 100  => target >= 128866 bytes.
        var result = _calc.ComputeVideoBitrate(130_000, TimeSpan.FromSeconds(10),
            new AudioSettings { IncludeAudio = false });
        Assert.True(result.Feasible);
        Assert.True(result.VideoKbps >= TargetSizeCalculator.MinVideoKbps);
    }
}
