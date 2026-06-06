using VideoTrim.Core.ViewModels;

namespace VideoTrim.Core.Tests;

public sealed class ExportOptionCatalogTests
{
    [Fact]
    public void VideoFps_HighRefresh_OfferedForHighFpsSource()
    {
        var opts = ExportOptionCatalog.VideoFpsFor(TestData.Media(fps: 144));
        var values = opts.Where(o => o.Fps.HasValue).Select(o => o.Fps!.Value).ToList();
        Assert.Contains(144d, values);
        Assert.Contains(120d, values);
    }

    [Fact]
    public void VideoFps_DoesNotOfferAboveSourceByDefault()
    {
        var opts = ExportOptionCatalog.VideoFpsFor(TestData.Media(fps: 30));
        var values = opts.Where(o => o.Fps.HasValue).Select(o => o.Fps!.Value).ToList();
        Assert.DoesNotContain(144d, values);
        Assert.DoesNotContain(60d, values);
        Assert.Contains(30d, values);
    }

    [Fact]
    public void EveryList_EndsWithCustomSentinel()
    {
        var src = TestData.Media(fps: 60, height: 1080);
        Assert.True(ExportOptionCatalog.ResolutionsFor(src).Last().IsCustom);
        Assert.True(ExportOptionCatalog.VideoFpsFor(src).Last().IsCustom);
        Assert.True(ExportOptionCatalog.GifFpsFor(src).Last().IsCustom);
    }

    [Fact]
    public void Resolutions_CapAtSourceHeight()
    {
        var opts = ExportOptionCatalog.ResolutionsFor(TestData.Media(height: 720));
        var heights = opts.Where(o => o.Height.HasValue).Select(o => o.Height!.Value).ToList();
        Assert.Contains(720, heights);
        Assert.DoesNotContain(1080, heights);
        Assert.DoesNotContain(2160, heights);
    }

    [Theory]
    [InlineData(1080, 1080)]
    [InlineData(999, 998)]
    [InlineData(1, 2)]
    [InlineData(0, null)]
    [InlineData(-10, null)]
    public void NormalizeHeight_ForcesEvenPositive(int input, int? expected)
        => Assert.Equal(expected, ExportOptionCatalog.NormalizeHeight(input));
}
