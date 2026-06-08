using CommunityToolkit.Mvvm.ComponentModel;
using VideoTrim.Core.Models;

namespace VideoTrim.Core.ViewModels;

/// <summary>
/// View-model for the GIF export panel (US6 §7.6). Uses the same editable combo fields as video for
/// resolution/fps. GIF has no audio/bitrate; the "compression" knob is the palette color count
/// (§7.6). Target size is optional and drives the iterative loop (§9.2).
/// </summary>
public sealed partial class GifViewModel : ObservableObject
{
    public GifViewModel()
    {
        HeightField = new ComboFieldViewModel(
            ExportOptionCatalog.SameAsSource,
            v => ((int)Math.Round(v)).ToString(System.Globalization.CultureInfo.InvariantCulture));
        FpsField = new ComboFieldViewModel(ExportOptionCatalog.SameAsSource);
        TargetSizeField = new ComboFieldViewModel();

        HeightField.SetOptions(ExportOptionCatalog.HeightOptions(null));
        FpsField.SetOptions(ExportOptionCatalog.GifFpsOptions(null));
        TargetSizeField.SetOptions(ExportOptionCatalog.TargetSizeOptions());

        HeightField.Text = ExportOptionCatalog.SameAsSource;
        FpsField.Text = "15";              // GIF-appropriate default rather than source fps
        TargetSizeField.Text = "5 MB";
    }

    public ComboFieldViewModel HeightField { get; }
    public ComboFieldViewModel FpsField { get; }
    public ComboFieldViewModel TargetSizeField { get; }

    public IReadOnlyList<DitherMode> DitherOptions { get; } = new[]
    {
        DitherMode.None, DitherMode.Bayer, DitherMode.FloydSteinberg,
    };

    /// <summary>2..256 — primary compression knob (§7.6).</summary>
    [ObservableProperty] private int _maxColors = 256;

    [ObservableProperty] private DitherMode _dither = DitherMode.Bayer;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanEditTargetSize))]
    private bool _useTargetSize;

    public bool CanEditTargetSize => UseTargetSize;

    private MediaInfo? _source;

    public void SetSource(MediaInfo? source)
    {
        _source = source;
        HeightField.SetOptions(ExportOptionCatalog.HeightOptions(source));
        FpsField.SetOptions(ExportOptionCatalog.GifFpsOptions(source));
        if (source is not null)
            HeightField.ApplySourceValue(source.Height);
    }

    public long TargetSizeBytes => FieldParsing.ParseSizeBytes(TargetSizeField.Text) ?? 0;

    public GifSettings ToSettings() => new()
    {
        TargetHeight = FieldParsing.ParseHeight(HeightField.Text, _source?.Height),
        TargetFps = FieldParsing.ResolveGifFps(FpsField.Text, _source?.FrameRate),
        MaxColors = Math.Clamp(MaxColors, 2, 256),
        Dither = Dither,
        TargetSizeBytes = UseTargetSize ? TargetSizeBytes : null,
    };
}
