using VideoTrim.Core.Encoding;
using VideoTrim.Core.Models;
using VideoTrim.Core.Services;

namespace VideoTrim.Core.Tests;

public sealed class FfmpegEncodingServiceTests
{
    private static (FfmpegEncodingService svc, FakeProcessRunner runner, FakeFileSystem fs) Create()
    {
        var runner = new FakeProcessRunner();
        var fs = new FakeFileSystem();
        var svc = new FfmpegEncodingService(
            runner, new FakeLocator(), new FfmpegCommandBuilder(), new TargetSizeCalculator(), fs);
        return (svc, runner, fs);
    }

    private static TrimRange Range(double s, double e) => new(TimeSpan.FromSeconds(s), TimeSpan.FromSeconds(e));
    private static readonly IProgress<EncodeProgress> Sink = new Progress<EncodeProgress>(_ => { });

    [Fact]
    public async Task ExportVideo_Quality_InvokesSinglePass()
    {
        var (svc, runner, fs) = Create();
        fs.EnqueueSize(123);
        var settings = new ExportSettings { Format = OutputFormat.Mp4, Mode = BitrateMode.Quality };

        var result = await svc.ExportVideoAsync("in.mp4", "out.mp4", Range(0, 5), settings,
            TestData.Media(), Sink, CancellationToken.None);

        Assert.Single(runner.Invocations);
        Assert.Contains("-crf", runner.Invocations[0].Args);
        Assert.Equal(123, result.FinalSizeBytes);
        Assert.True(result.MetTarget);
    }

    [Fact]
    public async Task ExportVideo_Bitrate_RunsTwoPasses()
    {
        var (svc, runner, fs) = Create();
        fs.EnqueueSize(999);
        var settings = new ExportSettings
        {
            Format = OutputFormat.Mp4,
            Mode = BitrateMode.Bitrate,
            VideoBitrateKbps = 2000,
        };

        await svc.ExportVideoAsync("in.mp4", "out.mp4", Range(0, 10), settings,
            TestData.Media(), Sink, CancellationToken.None);

        Assert.Equal(2, runner.Invocations.Count);
        Assert.Contains("1", PassValue(runner.Invocations[0].Args));
        Assert.Contains("2", PassValue(runner.Invocations[1].Args));
    }

    [Fact]
    public async Task ExportVideo_TargetSizeInfeasible_Throws()
    {
        var (svc, _, _) = Create();
        var settings = new ExportSettings
        {
            Format = OutputFormat.Mp4,
            Mode = BitrateMode.TargetSize,
            TargetSizeBytes = 50 * 1024, // 50 KiB
            Audio = new AudioSettings { IncludeAudio = true, AudioBitrateKbps = 128 },
        };

        await Assert.ThrowsAsync<EncodingException>(() =>
            svc.ExportVideoAsync("in.mp4", "out.mp4", Range(0, 60), settings,
                TestData.Media(), Sink, CancellationToken.None));
    }

    [Fact]
    public async Task ExportVideo_Failure_DeletesPartialOutput()
    {
        var (svc, runner, fs) = Create();
        runner.EnqueueExit(1); // ffmpeg fails
        var settings = new ExportSettings { Format = OutputFormat.Mp4, Mode = BitrateMode.Quality };

        await Assert.ThrowsAsync<EncodingException>(() =>
            svc.ExportVideoAsync("in.mp4", "out.mp4", Range(0, 5), settings,
                TestData.Media(), Sink, CancellationToken.None));

        Assert.Contains("out.mp4", fs.Deleted);
    }

    [Fact]
    public async Task ExportGif_NoTarget_EncodesOnce()
    {
        var (svc, runner, fs) = Create();
        fs.EnqueueSize(1000);
        var settings = new GifSettings { TargetSizeBytes = null, MaxColors = 256 };

        var result = await svc.ExportGifAsync("in.mp4", "out.gif", Range(0, 3), settings,
            TestData.Media(), Sink, CancellationToken.None);

        Assert.Equal(2, runner.Invocations.Count); // palettegen + paletteuse
        Assert.True(result.MetTarget);
    }

    [Fact]
    public async Task ExportGif_Target_IterativelyReducesUntilUnderTarget()
    {
        var (svc, runner, fs) = Create();
        // attempt 1 -> 5MB (over), attempt 2 -> 2MB (over), attempt 3 -> 0.8MB (under)
        fs.EnqueueSize(5_000_000);
        fs.EnqueueSize(2_000_000);
        fs.EnqueueSize(800_000);
        var settings = new GifSettings { TargetSizeBytes = 1_000_000, MaxColors = 256, TargetFps = 15, TargetHeight = 480 };

        var result = await svc.ExportGifAsync("in.mp4", "out.gif", Range(0, 3), settings,
            TestData.Media(height: 1080), Sink, CancellationToken.None);

        Assert.True(result.MetTarget);
        Assert.Equal(800_000, result.FinalSizeBytes);
        Assert.Equal(6, runner.Invocations.Count); // 3 attempts x 2 calls

        // Colors stepped 256 -> 128 -> 64 across the three palettegen calls.
        Assert.Contains("max_colors=256", PaletteGenFilter(runner.Invocations[0].Args));
        Assert.Contains("max_colors=128", PaletteGenFilter(runner.Invocations[2].Args));
        Assert.Contains("max_colors=64", PaletteGenFilter(runner.Invocations[4].Args));
    }

    [Fact]
    public async Task ExportGif_Target_NeverReached_ReturnsBestEffort()
    {
        var (svc, _, fs) = Create();
        for (int i = 0; i < GifSizeReducer.MaxIters; i++)
            fs.EnqueueSize(9_000_000); // always over

        var settings = new GifSettings { TargetSizeBytes = 1_000_000, MaxColors = 256, TargetFps = 15, TargetHeight = 480 };
        var result = await svc.ExportGifAsync("in.mp4", "out.gif", Range(0, 3), settings,
            TestData.Media(height: 1080), Sink, CancellationToken.None);

        Assert.False(result.MetTarget);
    }

    private static string PassValue(List<string> args)
    {
        int i = args.IndexOf("-pass");
        return i >= 0 && i + 1 < args.Count ? args[i + 1] : "";
    }

    private static string PaletteGenFilter(List<string> args)
    {
        int i = args.IndexOf("-vf");
        return i >= 0 && i + 1 < args.Count ? args[i + 1] : "";
    }
}
