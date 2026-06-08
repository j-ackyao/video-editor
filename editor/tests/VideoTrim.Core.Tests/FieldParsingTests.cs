using VideoTrim.Core.ViewModels;

namespace VideoTrim.Core.Tests;

public sealed class FieldParsingTests
{
    [Theory]
    [InlineData("720", 1080, 720)]
    [InlineData("721", 1080, 720)]      // odd → even
    [InlineData("1080", 1080, null)]    // equals source → same as source
    [InlineData("Same as source", 1080, null)]
    [InlineData("", 1080, null)]
    [InlineData("garbage", 1080, null)]
    public void ParseHeight(string text, int source, int? expected)
        => Assert.Equal(expected, FieldParsing.ParseHeight(text, source));

    [Theory]
    [InlineData("30", 60, 30.0)]
    [InlineData("59.94", 60, 59.94)]
    [InlineData("60", 60, null)]        // equals source
    [InlineData("Same as source", 60, null)]
    [InlineData("0", 60, null)]
    public void ParseFps(string text, double source, double? expected)
        => Assert.Equal(expected, FieldParsing.ParseFps(text, source));

    [Theory]
    [InlineData("2500", 2500)]
    [InlineData("2500k", 2500)]
    [InlineData("2500 kbps", 2500)]
    [InlineData("0", null)]
    [InlineData("", null)]
    public void ParseBitrateKbps(string text, int? expected)
        => Assert.Equal(expected, FieldParsing.ParseBitrateKbps(text));

    [Theory]
    [InlineData("10 MB", 10L * 1024 * 1024)]
    [InlineData("10mb", 10L * 1024 * 1024)]
    [InlineData("5 MiB", 5L * 1024 * 1024)]
    [InlineData("500 KB", 500L * 1024)]
    [InlineData("8", 8L * 1024 * 1024)]   // bare number defaults to MiB
    [InlineData("1 GB", 1024L * 1024 * 1024)]
    [InlineData("0 MB", null)]
    [InlineData("", null)]
    public void ParseSizeBytes(string text, long? expected)
        => Assert.Equal(expected, FieldParsing.ParseSizeBytes(text));

    [Theory]
    [InlineData("15", 60, 15.0)]
    [InlineData("Same as source", 60, 60.0)]  // GIF resolves "same as source" to the source fps
    [InlineData("", 48, 48.0)]
    public void ResolveGifFps(string text, double source, double expected)
        => Assert.Equal(expected, FieldParsing.ResolveGifFps(text, source));

    [Fact]
    public void ComboField_SameAsSource_SubstitutesNumericValue()
    {
        var field = new ComboFieldViewModel(ExportOptionCatalog.SameAsSource,
            v => ((int)v).ToString(System.Globalization.CultureInfo.InvariantCulture));
        field.ApplySourceValue(1080);
        Assert.Equal("1080", field.Text);

        field.Text = "720";
        Assert.Equal("720", field.Text);

        field.Text = ExportOptionCatalog.SameAsSource;
        Assert.Equal("1080", field.Text); // sentinel replaced with the source value
    }

    [Fact]
    public void ComboField_RaisesValueChanged_OnRealEdits()
    {
        var field = new ComboFieldViewModel();
        int count = 0;
        field.ValueChanged += (_, _) => count++;
        field.Text = "5000";
        field.Text = "1000";
        Assert.Equal(2, count);
    }

    [Fact]
    public void ComboField_SourceValueText_ReflectsAppliedSource()
    {
        var field = new ComboFieldViewModel(ExportOptionCatalog.SameAsSource,
            v => ((int)v).ToString(System.Globalization.CultureInfo.InvariantCulture));
        Assert.Null(field.SourceValueText);           // none applied yet
        field.ApplySourceValue(720);
        Assert.Equal("720", field.SourceValueText);   // used to display the numeric value on "Same as source"
    }
}
