using VideoTrim.Core.Encoding;
using VideoTrim.Core.Models;

namespace VideoTrim.Core.Tests;

public sealed class GifSizeReducerTests
{
    private static GifSettings Base() => new() { MaxColors = 256, TargetFps = 15, TargetHeight = 480 };

    [Fact]
    public void Colors_ReducedFirst_InSequence()
    {
        var s = Base();
        s = GifSizeReducer.Reduce(s, 1080)!;
        Assert.Equal(128, s.MaxColors);
        s = GifSizeReducer.Reduce(s, 1080)!;
        Assert.Equal(64, s.MaxColors);
        s = GifSizeReducer.Reduce(s, 1080)!;
        Assert.Equal(32, s.MaxColors);
        // colors unchanged afterward
        Assert.Equal(15, s.TargetFps);
    }

    [Fact]
    public void Fps_ReducedAfterColorsExhausted()
    {
        var s = new GifSettings { MaxColors = 32, TargetFps = 15, TargetHeight = 480 };
        s = GifSizeReducer.Reduce(s, 1080)!;
        Assert.Equal(32, s.MaxColors);
        Assert.True(s.TargetFps < 15);
        Assert.True(s.TargetFps >= GifSizeReducer.FpsFloor);
    }

    [Fact]
    public void Height_ReducedAfterFpsAtFloor()
    {
        var s = new GifSettings { MaxColors = 32, TargetFps = GifSizeReducer.FpsFloor, TargetHeight = 480 };
        s = GifSizeReducer.Reduce(s, 1080)!;
        Assert.NotNull(s.TargetHeight);
        Assert.True(s.TargetHeight < 480);
        Assert.Equal(0, s.TargetHeight!.Value % 2); // even width requirement preserved
    }

    [Fact]
    public void Height_UsesSourceWhenNull()
    {
        var s = new GifSettings { MaxColors = 32, TargetFps = GifSizeReducer.FpsFloor, TargetHeight = null };
        s = GifSizeReducer.Reduce(s, 720)!;
        Assert.NotNull(s.TargetHeight);
        Assert.True(s.TargetHeight < 720);
    }

    [Fact]
    public void Exhausted_ReturnsNull()
    {
        var s = new GifSettings
        {
            MaxColors = GifSizeReducer.ColorFloor,
            TargetFps = GifSizeReducer.FpsFloor,
            TargetHeight = GifSizeReducer.HeightFloor,
        };
        Assert.Null(GifSizeReducer.Reduce(s, 1080));
    }
}
