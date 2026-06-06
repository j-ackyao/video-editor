using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using VideoTrim.Core.Models;

namespace VideoTrim.Core.ViewModels;

/// <summary>
/// View-model for the GIF export panel (US6 §7.6). GIF has no audio/bitrate, so the "compression"
/// knob is the palette color count (§7.6 assumption). Target size is optional and drives the
/// iterative loop (§9.2).
/// </summary>
public sealed partial class GifViewModel : ObservableObject
{
    public GifViewModel()
    {
        ResolutionOptions = new ObservableCollection<ResolutionOption>(ExportOptionCatalog.ResolutionsFor(null));
        FpsOptions = new ObservableCollection<FpsOption>(ExportOptionCatalog.GifFpsFor(null));
        SelectedResolution = ResolutionOptions[0];
        SelectedFps = FpsOptions.FirstOrDefault(f => f.Fps is >= 14 and <= 16) ?? FpsOptions[0];
    }

    public ObservableCollection<ResolutionOption> ResolutionOptions { get; }
    public ObservableCollection<FpsOption> FpsOptions { get; }

    public IReadOnlyList<DitherMode> DitherOptions { get; } = new[]
    {
        DitherMode.None, DitherMode.Bayer, DitherMode.FloydSteinberg,
    };

    public IReadOnlyList<SizeUnit> SizeUnitOptions { get; } = new[] { SizeUnit.MiB, SizeUnit.KiB };

    [ObservableProperty] private ResolutionOption? _selectedResolution;
    [ObservableProperty] private FpsOption? _selectedFps;

    /// <summary>2..256 — primary compression knob (§7.6).</summary>
    [ObservableProperty] private int _maxColors = 256;

    [ObservableProperty] private DitherMode _dither = DitherMode.Bayer;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanEditTargetSize))]
    private bool _useTargetSize;

    [ObservableProperty] private double _targetSizeValue = 5;
    [ObservableProperty] private SizeUnit _targetSizeUnit = SizeUnit.MiB;

    public bool CanEditTargetSize => UseTargetSize;

    public void SetSource(MediaInfo? source)
    {
        ReplaceOptions(ResolutionOptions, ExportOptionCatalog.ResolutionsFor(source));
        ReplaceOptions(FpsOptions, ExportOptionCatalog.GifFpsFor(source));
        SelectedResolution = ResolutionOptions[0];
        SelectedFps = FpsOptions.FirstOrDefault(f => f.Fps is >= 14 and <= 16) ?? FpsOptions[0];
    }

    public long TargetSizeBytes => SizeUnits.ToBytes(TargetSizeValue, TargetSizeUnit);

    public GifSettings ToSettings() => new()
    {
        TargetHeight = SelectedResolution?.Height,
        TargetFps = SelectedFps?.Fps ?? 15,
        MaxColors = Math.Clamp(MaxColors, 2, 256),
        Dither = Dither,
        TargetSizeBytes = UseTargetSize ? TargetSizeBytes : null,
    };

    private static void ReplaceOptions<T>(ObservableCollection<T> target, IReadOnlyList<T> items)
    {
        target.Clear();
        foreach (var item in items)
            target.Add(item);
    }
}
