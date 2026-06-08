using VideoTrim.Core.ViewModels;

namespace VideoTrim.Core.Tests;

public sealed class ExportOptionCatalogTests
{
    [Fact]
    public void VideoFps_HighRefresh_OfferedForHighFpsSource()
    {
        var opts = ExportOptionCatalog.VideoFpsOptions(TestData.Media(fps: 144));
        Assert.Contains("144", opts);
        Assert.Contains("120", opts);
    }

    [Fact]
    public void VideoFps_DoesNotOfferAboveSourceByDefault()
    {
        var opts = ExportOptionCatalog.VideoFpsOptions(TestData.Media(fps: 30));
        Assert.DoesNotContain("144", opts);
        Assert.DoesNotContain("60", opts);
        Assert.Contains("30", opts);
    }

    [Fact]
    public void HeightAndFps_StartWithSameAsSource()
    {
        var src = TestData.Media(fps: 60, height: 1080);
        Assert.Equal(ExportOptionCatalog.SameAsSource, ExportOptionCatalog.HeightOptions(src)[0]);
        Assert.Equal(ExportOptionCatalog.SameAsSource, ExportOptionCatalog.VideoFpsOptions(src)[0]);
        Assert.Equal(ExportOptionCatalog.SameAsSource, ExportOptionCatalog.GifFpsOptions(src)[0]);
    }

    [Fact]
    public void Resolutions_CapAtSourceHeight()
    {
        var opts = ExportOptionCatalog.HeightOptions(TestData.Media(height: 720));
        Assert.Contains("720", opts);
        Assert.DoesNotContain("1080", opts);
        Assert.DoesNotContain("2160", opts);
    }

    [Fact]
    public void BitrateAndSizePresets_AreOffered()
    {
        Assert.Contains("2500", ExportOptionCatalog.VideoBitrateOptions());
        Assert.Contains("10 MB", ExportOptionCatalog.TargetSizeOptions());
        Assert.Contains("5 MB", ExportOptionCatalog.TargetSizeOptions());
        Assert.Contains("128", ExportOptionCatalog.AudioBitrateOptions());
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
