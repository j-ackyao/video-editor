using VideoTrim.Core.Encoding;

namespace VideoTrim.Core.Tests;

public sealed class FfmpegProgressParserTests
{
    [Fact]
    public void Feed_EmitsSnapshot_OnProgressLine()
    {
        var parser = new FfmpegProgressParser();
        Assert.Null(parser.Feed("frame=10"));
        Assert.Null(parser.Feed("out_time=00:00:12.500000"));
        Assert.Null(parser.Feed("speed=2.0x"));
        var snap = parser.Feed("progress=continue");

        Assert.NotNull(snap);
        Assert.Equal(TimeSpan.FromSeconds(12.5), snap!.Value.ProcessedTime);
        Assert.Equal(2.0, snap.Value.SpeedX);
    }

    [Fact]
    public void Feed_ParsesOutTimeMicroseconds()
    {
        var parser = new FfmpegProgressParser();
        parser.Feed("out_time_us=3000000");
        var snap = parser.Feed("progress=end");
        Assert.Equal(TimeSpan.FromSeconds(3), snap!.Value.ProcessedTime);
    }

    [Fact]
    public void Feed_HandlesNaSpeed()
    {
        var parser = new FfmpegProgressParser();
        parser.Feed("speed=N/A");
        var snap = parser.Feed("progress=continue");
        Assert.Null(snap!.Value.SpeedX);
    }

    [Fact]
    public void Feed_IgnoresNonKeyValueLines() => Assert.Null(new FfmpegProgressParser().Feed("garbage line"));
}
