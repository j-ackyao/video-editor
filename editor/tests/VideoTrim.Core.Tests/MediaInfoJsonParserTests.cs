using VideoTrim.Core.Probing;

namespace VideoTrim.Core.Tests;

public sealed class MediaInfoJsonParserTests
{
    private const string WithAudio = """
    {
      "streams": [
        { "codec_type": "video", "codec_name": "h264", "width": 1920, "height": 1080, "r_frame_rate": "30000/1001" },
        { "codec_type": "audio", "codec_name": "aac" }
      ],
      "format": { "duration": "12.345000", "size": "5000000" }
    }
    """;

    private const string NoAudio = """
    {
      "streams": [
        { "codec_type": "video", "codec_name": "vp9", "width": 1280, "height": 720, "r_frame_rate": "24/1" }
      ],
      "format": { "duration": "8.000000", "size": "100" }
    }
    """;

    [Fact]
    public void Parse_MapsVideoAndAudio()
    {
        var info = MediaInfoJsonParser.Parse(WithAudio, "C:\\v\\a.mp4");
        Assert.Equal(1920, info.Width);
        Assert.Equal(1080, info.Height);
        Assert.Equal("h264", info.VideoCodec);
        Assert.True(info.HasAudio);
        Assert.Equal("aac", info.AudioCodec);
        Assert.Equal(29.97, info.FrameRate, 2);
        Assert.Equal(TimeSpan.FromSeconds(12.345), info.Duration);
        Assert.Equal(5_000_000, info.FileSizeBytes);
    }

    [Fact]
    public void Parse_NoAudioStream()
    {
        var info = MediaInfoJsonParser.Parse(NoAudio, "C:\\v\\b.webm");
        Assert.False(info.HasAudio);
        Assert.Null(info.AudioCodec);
        Assert.Equal(24, info.FrameRate, 3);
    }

    [Fact]
    public void Parse_FileSizeOverride_TakesPrecedence()
    {
        var info = MediaInfoJsonParser.Parse(WithAudio, "C:\\v\\a.mp4", fileSizeBytesOverride: 42);
        Assert.Equal(42, info.FileSizeBytes);
    }

    [Fact]
    public void Parse_NoVideoStream_Throws()
    {
        const string audioOnly = """{ "streams": [ { "codec_type": "audio", "codec_name": "mp3" } ], "format": {} }""";
        Assert.Throws<FormatException>(() => MediaInfoJsonParser.Parse(audioOnly, "x"));
    }

    [Theory]
    [InlineData("30000/1001", 29.97)]
    [InlineData("24/1", 24)]
    [InlineData("25", 25)]
    public void ParseRational_Valid(string value, double expected)
        => Assert.Equal(expected, MediaInfoJsonParser.ParseRational(value)!.Value, 2);

    [Theory]
    [InlineData("0/0")]
    [InlineData("")]
    [InlineData(null)]
    public void ParseRational_Invalid_ReturnsNull(string? value)
        => Assert.Null(MediaInfoJsonParser.ParseRational(value));
}
