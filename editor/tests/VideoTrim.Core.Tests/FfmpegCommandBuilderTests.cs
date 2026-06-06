using VideoTrim.Core.Encoding;
using VideoTrim.Core.Models;

namespace VideoTrim.Core.Tests;

public sealed class FfmpegCommandBuilderTests
{
    private readonly FfmpegCommandBuilder _b = new();
    private static TrimRange Range(double start, double end) =>
        new(TimeSpan.FromSeconds(start), TimeSpan.FromSeconds(end));

    [Fact]
    public void Probe_ProducesExpectedArgs()
    {
        var args = _b.BuildProbeArgs("in.mp4");
        Assert.Equal(new[]
        {
            "-v", "quiet", "-print_format", "json", "-show_format", "-show_streams", "in.mp4",
        }, args);
    }

    [Fact]
    public void Crf_Mp4_WithScaleFpsAndAudio_GoldenArgs()
    {
        var settings = new ExportSettings
        {
            Format = OutputFormat.Mp4,
            TargetHeight = 720,
            TargetFps = 30,
            Mode = BitrateMode.Quality,
            QualityCrf = 23,
            Audio = new AudioSettings { IncludeAudio = true, AudioBitrateKbps = 128 },
        };

        var args = _b.BuildCrfExportArgs("in.mp4", "out.mp4", Range(12.5, 20), settings, TestData.Media());

        Assert.Equal(new[]
        {
            "-y", "-hide_banner", "-ss", "12.500", "-to", "20.000", "-i", "in.mp4",
            "-vf", "scale=-2:720,fps=30",
            "-c:v", "libx264", "-crf", "23", "-preset", "medium", "-pix_fmt", "yuv420p",
            "-c:a", "aac", "-b:a", "128k",
            "-movflags", "+faststart",
            "-progress", "pipe:1", "-nostats", "out.mp4",
        }, args);
    }

    [Fact]
    public void Crf_SameAsSource_OmitsFilter()
    {
        var settings = new ExportSettings { Format = OutputFormat.Mp4, TargetHeight = null, TargetFps = null };
        var args = _b.BuildCrfExportArgs("in.mp4", "out.mp4", Range(0, 10), settings, TestData.Media());
        Assert.DoesNotContain("-vf", args);
    }

    [Fact]
    public void Crf_ExcludeAudio_AddsAn()
    {
        var settings = new ExportSettings
        {
            Format = OutputFormat.Mp4,
            Audio = new AudioSettings { IncludeAudio = false },
        };
        var args = _b.BuildCrfExportArgs("in.mp4", "out.mp4", Range(0, 10), settings, TestData.Media());
        Assert.Contains("-an", args);
        Assert.DoesNotContain("-c:a", args);
    }

    [Fact]
    public void Crf_SourceHasNoAudio_ForcesAnEvenIfIncludeRequested()
    {
        var settings = new ExportSettings
        {
            Format = OutputFormat.Mp4,
            Audio = new AudioSettings { IncludeAudio = true, AudioBitrateKbps = 192 },
        };
        var noAudioSource = TestData.Media(hasAudio: false);
        var args = _b.BuildCrfExportArgs("in.mp4", "out.mp4", Range(0, 10), settings, noAudioSource);
        Assert.Contains("-an", args);
        Assert.DoesNotContain("-c:a", args);
    }

    [Fact]
    public void Crf_Webm_UsesVp9ConstantQualityAndOpus_NoFastStart()
    {
        var settings = new ExportSettings
        {
            Format = OutputFormat.Webm,
            QualityCrf = 30,
            TargetHeight = null,
            TargetFps = null,
            Audio = new AudioSettings { IncludeAudio = true, AudioBitrateKbps = 96 },
        };
        var args = _b.BuildCrfExportArgs("in.mkv", "out.webm", Range(0, 10), settings, TestData.Media());

        Assert.Equal(new[]
        {
            "-y", "-hide_banner", "-ss", "0.000", "-to", "10.000", "-i", "in.mkv",
            "-c:v", "libvpx-vp9", "-crf", "30", "-b:v", "0", "-preset", "medium", "-pix_fmt", "yuv420p",
            "-c:a", "libopus", "-b:a", "96k",
            "-progress", "pipe:1", "-nostats", "out.webm",
        }, args);
    }

    [Fact]
    public void TwoPass_Pass1_DiscardsAudioToNullSink()
    {
        var settings = new ExportSettings
        {
            Format = OutputFormat.Mp4,
            TargetHeight = 1080,
            TargetFps = 30,
            Mode = BitrateMode.Bitrate,
            Audio = new AudioSettings { IncludeAudio = true, AudioBitrateKbps = 128 },
        };
        var args = _b.BuildTwoPassArgs(1, "in.mp4", "out.mp4", Range(0, 60), settings,
            TestData.Media(), 1228, "C:\\tmp\\p", "NUL");

        Assert.Equal(new[]
        {
            "-y", "-hide_banner", "-ss", "0.000", "-to", "60.000", "-i", "in.mp4",
            "-vf", "scale=-2:1080,fps=30",
            "-c:v", "libx264", "-b:v", "1228k", "-pass", "1", "-passlogfile", "C:\\tmp\\p",
            "-preset", "medium", "-pix_fmt", "yuv420p",
            "-an", "-f", "mp4", "-progress", "pipe:1", "-nostats", "NUL",
        }, args);
    }

    [Fact]
    public void TwoPass_Pass2_WritesRealOutputWithAudio()
    {
        var settings = new ExportSettings
        {
            Format = OutputFormat.Mp4,
            TargetHeight = 1080,
            TargetFps = 30,
            Mode = BitrateMode.Bitrate,
            Audio = new AudioSettings { IncludeAudio = true, AudioBitrateKbps = 128 },
        };
        var args = _b.BuildTwoPassArgs(2, "in.mp4", "out.mp4", Range(0, 60), settings,
            TestData.Media(), 1228, "C:\\tmp\\p", "NUL");

        Assert.Equal(new[]
        {
            "-y", "-hide_banner", "-ss", "0.000", "-to", "60.000", "-i", "in.mp4",
            "-vf", "scale=-2:1080,fps=30",
            "-c:v", "libx264", "-b:v", "1228k", "-pass", "2", "-passlogfile", "C:\\tmp\\p",
            "-preset", "medium", "-pix_fmt", "yuv420p",
            "-c:a", "aac", "-b:a", "128k",
            "-movflags", "+faststart",
            "-progress", "pipe:1", "-nostats", "out.mp4",
        }, args);
    }

    [Fact]
    public void TwoPass_InvalidPass_Throws() =>
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            _b.BuildTwoPassArgs(3, "in.mp4", "out.mp4", Range(0, 1), new ExportSettings(),
                TestData.Media(), 1000, "p", "NUL"));

    [Fact]
    public void Gif_PaletteGen_WithScale_GoldenArgs()
    {
        var settings = new GifSettings { TargetHeight = 480, TargetFps = 15, MaxColors = 128, Dither = DitherMode.Bayer };
        var args = _b.BuildGifPaletteGenArgs("in.mp4", "pal.png", Range(0, 5), settings);

        Assert.Equal(new[]
        {
            "-y", "-hide_banner", "-ss", "0.000", "-to", "5.000", "-i", "in.mp4",
            "-vf", "fps=15,scale=-2:480:flags=lanczos,palettegen=max_colors=128",
            "pal.png",
        }, args);
    }

    [Fact]
    public void Gif_PaletteUse_GoldenArgs_AndDitherMapping()
    {
        var settings = new GifSettings { TargetHeight = 480, TargetFps = 15, Dither = DitherMode.FloydSteinberg };
        var args = _b.BuildGifPaletteUseArgs("in.mp4", "pal.png", "out.gif", Range(0, 5), settings);

        Assert.Equal(new[]
        {
            "-y", "-hide_banner", "-ss", "0.000", "-to", "5.000", "-i", "in.mp4", "-i", "pal.png",
            "-lavfi", "fps=15,scale=-2:480:flags=lanczos[x];[x][1:v]paletteuse=dither=floyd_steinberg",
            "-progress", "pipe:1", "-nostats", "out.gif",
        }, args);
    }

    [Fact]
    public void Gif_NoHeight_OmitsScaleInGraph()
    {
        var settings = new GifSettings { TargetHeight = null, TargetFps = 12, Dither = DitherMode.None };
        var gen = _b.BuildGifPaletteGenArgs("in.mp4", "pal.png", Range(0, 5), settings);
        var use = _b.BuildGifPaletteUseArgs("in.mp4", "pal.png", "out.gif", Range(0, 5), settings);

        Assert.Contains("fps=12,palettegen=max_colors=256", gen);
        Assert.Contains("fps=12[x];[x][1:v]paletteuse=dither=none", use);
    }

    [Fact]
    public void AccurateSeek_PlacesSsBeforeInput()
    {
        var args = _b.BuildCrfExportArgs("in.mp4", "out.mp4", Range(5, 9), new ExportSettings(), TestData.Media());
        int ssIndex = args.ToList().IndexOf("-ss");
        int iIndex = args.ToList().IndexOf("-i");
        Assert.True(ssIndex >= 0 && iIndex >= 0 && ssIndex < iIndex, "-ss must precede -i for accurate seeking (§9.3)");
    }
}
