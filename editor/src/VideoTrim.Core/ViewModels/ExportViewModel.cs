using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using VideoTrim.Core.Models;
using VideoTrim.Core.Services;

namespace VideoTrim.Core.ViewModels;

/// <summary>
/// View-model for the video export panel (US3 §7.3, US4 §7.4, US5 §7.5). Owns the
/// <see cref="ExportSettings"/> the user is editing, the source-aware dropdown options, and the
/// live target-size feasibility check.
/// </summary>
public sealed partial class ExportViewModel : ObservableObject
{
    private readonly ITargetSizeCalculator _calculator;
    private MediaInfo? _source;

    public ExportViewModel(ITargetSizeCalculator calculator)
    {
        _calculator = calculator;
        ResolutionOptions = new ObservableCollection<ResolutionOption>(ExportOptionCatalog.ResolutionsFor(null));
        FpsOptions = new ObservableCollection<FpsOption>(ExportOptionCatalog.VideoFpsFor(null));
        SelectedResolution = ResolutionOptions[0];
        SelectedFps = FpsOptions[0];
    }

    public ObservableCollection<ResolutionOption> ResolutionOptions { get; }
    public ObservableCollection<FpsOption> FpsOptions { get; }

    public IReadOnlyList<OutputFormat> FormatOptions { get; } = new[]
    {
        OutputFormat.Mp4, OutputFormat.Mov, OutputFormat.Mkv, OutputFormat.Webm, OutputFormat.Gif,
    };

    public IReadOnlyList<int> AudioBitrateOptions { get; } = new[] { 320, 256, 192, 128, 96, 64 };
    public IReadOnlyList<SizeUnit> SizeUnitOptions { get; } = new[] { SizeUnit.MiB, SizeUnit.KiB };

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsGif))]
    [NotifyPropertyChangedFor(nameof(IsVideo))]
    private OutputFormat _format = OutputFormat.Mp4;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsCustomResolution))]
    private ResolutionOption? _selectedResolution;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsCustomFps))]
    private FpsOption? _selectedFps;

    /// <summary>Free-form height used when the "Custom…" resolution option is selected.</summary>
    [ObservableProperty] private int _customHeight = 1080;

    /// <summary>Free-form frame rate used when the "Custom…" fps option is selected.</summary>
    [ObservableProperty] private double _customFps = 60;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsQualityMode))]
    [NotifyPropertyChangedFor(nameof(IsBitrateMode))]
    [NotifyPropertyChangedFor(nameof(IsTargetSizeMode))]
    private BitrateMode _mode = BitrateMode.Quality;

    [ObservableProperty] private int _qualityCrf = 23;
    [ObservableProperty] private int? _videoBitrateKbps = 2500;
    [ObservableProperty] private double _targetSizeValue = 10;
    [ObservableProperty] private SizeUnit _targetSizeUnit = SizeUnit.MiB;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanEditAudioBitrate))]
    private bool _includeAudio = true;

    [ObservableProperty] private int _audioBitrateKbps = 128;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanEditAudioBitrate))]
    private bool _sourceHasAudio;

    [ObservableProperty] private bool _isTargetSizeFeasible = true;
    [ObservableProperty] private string? _feasibilityMessage;

    /// <summary>Latest trimmed duration, pushed in by the main view-model; drives feasibility.</summary>
    public TimeSpan TrimDuration { get; private set; }

    public bool IsGif => Format == OutputFormat.Gif;
    public bool IsVideo => Format != OutputFormat.Gif;

    /// <summary>True when the "Custom…" resolution option is selected (reveals the height input).</summary>
    public bool IsCustomResolution => SelectedResolution?.IsCustom ?? false;

    /// <summary>True when the "Custom…" fps option is selected (reveals the fps input).</summary>
    public bool IsCustomFps => SelectedFps?.IsCustom ?? false;

    // Settable so RadioButton.IsChecked can bind TwoWay directly (no enum converter needed).
    // Setting one to true selects that mode; the false push-back from the radio group is ignored.
    public bool IsQualityMode
    {
        get => Mode == BitrateMode.Quality;
        set { if (value) Mode = BitrateMode.Quality; }
    }

    public bool IsBitrateMode
    {
        get => Mode == BitrateMode.Bitrate;
        set { if (value) Mode = BitrateMode.Bitrate; }
    }

    public bool IsTargetSizeMode
    {
        get => Mode == BitrateMode.TargetSize;
        set { if (value) Mode = BitrateMode.TargetSize; }
    }

    public bool CanEditAudioBitrate => IncludeAudio && SourceHasAudio;

    /// <summary>Export should be blocked only when target-size mode is selected but infeasible (§7.4).</summary>
    public bool BlocksExport => IsVideo && IsTargetSizeMode && !IsTargetSizeFeasible;

    public event EventHandler? ExportConstraintsChanged;

    public void SetSource(MediaInfo? source)
    {
        _source = source;
        SourceHasAudio = source?.HasAudio ?? false;
        if (!SourceHasAudio)
            IncludeAudio = false;

        ReplaceOptions(ResolutionOptions, ExportOptionCatalog.ResolutionsFor(source));
        ReplaceOptions(FpsOptions, ExportOptionCatalog.VideoFpsFor(source));
        SelectedResolution = ResolutionOptions[0];
        SelectedFps = FpsOptions[0];
        RecomputeFeasibility();
    }

    public void UpdateTrimDuration(TimeSpan duration)
    {
        TrimDuration = duration;
        RecomputeFeasibility();
    }

    public long TargetSizeBytes => SizeUnits.ToBytes(TargetSizeValue, TargetSizeUnit);

    public ExportSettings ToSettings() => new()
    {
        Format = Format,
        TargetHeight = ResolveTargetHeight(),
        TargetFps = ResolveTargetFps(),
        Mode = Mode,
        QualityCrf = QualityCrf,
        VideoBitrateKbps = VideoBitrateKbps,
        TargetSizeBytes = Mode == BitrateMode.TargetSize ? TargetSizeBytes : null,
        Audio = new AudioSettings
        {
            IncludeAudio = IncludeAudio && SourceHasAudio,
            AudioBitrateKbps = AudioBitrateKbps,
        },
    };

    private int? ResolveTargetHeight() => SelectedResolution?.IsCustom == true
        ? ExportOptionCatalog.NormalizeHeight(CustomHeight)
        : SelectedResolution?.Height;

    private double? ResolveTargetFps()
    {
        if (SelectedFps?.IsCustom == true)
            return CustomFps > 0 ? CustomFps : null;
        return SelectedFps?.Fps;
    }

    public void RecomputeFeasibility()
    {
        if (Mode != BitrateMode.TargetSize || TrimDuration <= TimeSpan.Zero)
        {
            IsTargetSizeFeasible = true;
            FeasibilityMessage = null;
            ExportConstraintsChanged?.Invoke(this, EventArgs.Empty);
            return;
        }

        var audio = new AudioSettings { IncludeAudio = IncludeAudio && SourceHasAudio, AudioBitrateKbps = AudioBitrateKbps };
        TargetSizeResult result = _calculator.ComputeVideoBitrate(TargetSizeBytes, TrimDuration, audio);
        IsTargetSizeFeasible = result.Feasible;
        FeasibilityMessage = result.Feasible ? null : result.Message;
        ExportConstraintsChanged?.Invoke(this, EventArgs.Empty);
    }

    partial void OnModeChanged(BitrateMode value) => RecomputeFeasibility();
    partial void OnTargetSizeValueChanged(double value) => RecomputeFeasibility();
    partial void OnTargetSizeUnitChanged(SizeUnit value) => RecomputeFeasibility();
    partial void OnIncludeAudioChanged(bool value) => RecomputeFeasibility();
    partial void OnAudioBitrateKbpsChanged(int value) => RecomputeFeasibility();
    partial void OnFormatChanged(OutputFormat value) => ExportConstraintsChanged?.Invoke(this, EventArgs.Empty);

    private static void ReplaceOptions<T>(ObservableCollection<T> target, IReadOnlyList<T> items)
    {
        target.Clear();
        foreach (var item in items)
            target.Add(item);
    }
}
