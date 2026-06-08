using CommunityToolkit.Mvvm.ComponentModel;
using VideoTrim.Core.Models;
using VideoTrim.Core.Services;

namespace VideoTrim.Core.ViewModels;

/// <summary>
/// View-model for the video export panel (US3 §7.3, US4 §7.4, US5 §7.5). Each numeric input is an
/// editable combo field (presets + free typing); resolution/fps default to the source value and
/// expose a "Same as source" reset. Owns the live target-size feasibility check.
/// </summary>
public sealed partial class ExportViewModel : ObservableObject
{
    private readonly ITargetSizeCalculator _calculator;
    private MediaInfo? _source;

    public ExportViewModel(ITargetSizeCalculator calculator)
    {
        _calculator = calculator;

        HeightField = new ComboFieldViewModel(
            ExportOptionCatalog.SameAsSource,
            v => ((int)Math.Round(v)).ToString(System.Globalization.CultureInfo.InvariantCulture));
        FpsField = new ComboFieldViewModel(ExportOptionCatalog.SameAsSource);
        BitrateField = new ComboFieldViewModel();
        TargetSizeField = new ComboFieldViewModel();
        AudioBitrateField = new ComboFieldViewModel();

        HeightField.SetOptions(ExportOptionCatalog.HeightOptions(null));
        FpsField.SetOptions(ExportOptionCatalog.VideoFpsOptions(null));
        BitrateField.SetOptions(ExportOptionCatalog.VideoBitrateOptions());
        TargetSizeField.SetOptions(ExportOptionCatalog.TargetSizeOptions());
        AudioBitrateField.SetOptions(ExportOptionCatalog.AudioBitrateOptions());

        TargetSizeField.ValueChanged += (_, _) => RecomputeFeasibility();
        AudioBitrateField.ValueChanged += (_, _) => RecomputeFeasibility();
    }

    public ComboFieldViewModel HeightField { get; }
    public ComboFieldViewModel FpsField { get; }
    public ComboFieldViewModel BitrateField { get; }
    public ComboFieldViewModel TargetSizeField { get; }
    public ComboFieldViewModel AudioBitrateField { get; }

    public IReadOnlyList<OutputFormat> FormatOptions { get; } = new[]
    {
        OutputFormat.Mp4, OutputFormat.Mov, OutputFormat.Mkv, OutputFormat.Webm, OutputFormat.Gif,
    };

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsGif))]
    [NotifyPropertyChangedFor(nameof(IsVideo))]
    private OutputFormat _format = OutputFormat.Mp4;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsQualityMode))]
    [NotifyPropertyChangedFor(nameof(IsBitrateMode))]
    [NotifyPropertyChangedFor(nameof(IsTargetSizeMode))]
    private BitrateMode _mode = BitrateMode.Quality;

    [ObservableProperty] private int _qualityCrf = 23;

    /// <summary>
    /// Lowest CRF the UI allows. CRF 0 puts x264 into <b>lossless</b> mode, which it tags as the
    /// "High 4:4:4 Predictive" profile — Windows Media Foundation (the preview player) can't decode
    /// that and shows a black frame. CRF 1 is near-lossless and uses the normal High profile.
    /// </summary>
    public const int MinCrf = 1;
    public const int MaxCrf = 51;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanEditAudioBitrate))]
    private bool _includeAudio = true;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanEditAudioBitrate))]
    private bool _sourceHasAudio;

    [ObservableProperty] private bool _isTargetSizeFeasible = true;
    [ObservableProperty] private string? _feasibilityMessage;

    /// <summary>Latest trimmed duration, pushed in by the main view-model; drives feasibility.</summary>
    public TimeSpan TrimDuration { get; private set; }

    public bool IsGif => Format == OutputFormat.Gif;
    public bool IsVideo => Format != OutputFormat.Gif;

    // Settable so RadioButton.IsChecked can bind TwoWay directly (no enum converter needed).
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

        HeightField.SetOptions(ExportOptionCatalog.HeightOptions(source));
        FpsField.SetOptions(ExportOptionCatalog.VideoFpsOptions(source));

        if (source is not null)
        {
            // Populate the boxes with the source values (numeric "same as source" default).
            HeightField.ApplySourceValue(source.Height);
            FpsField.ApplySourceValue(source.FrameRate);
        }

        RecomputeFeasibility();
    }

    public void UpdateTrimDuration(TimeSpan duration)
    {
        TrimDuration = duration;
        RecomputeFeasibility();
    }

    public int AudioBitrateKbps => FieldParsing.ParseBitrateKbps(AudioBitrateField.Text) ?? 128;

    public long TargetSizeBytes => FieldParsing.ParseSizeBytes(TargetSizeField.Text) ?? 0;

    public ExportSettings ToSettings() => new()
    {
        Format = Format,
        TargetHeight = FieldParsing.ParseHeight(HeightField.Text, _source?.Height),
        TargetFps = FieldParsing.ParseFps(FpsField.Text, _source?.FrameRate),
        Mode = Mode,
        QualityCrf = Math.Clamp(QualityCrf, MinCrf, MaxCrf),
        VideoBitrateKbps = FieldParsing.ParseBitrateKbps(BitrateField.Text),
        TargetSizeBytes = Mode == BitrateMode.TargetSize ? TargetSizeBytes : null,
        Audio = new AudioSettings
        {
            IncludeAudio = IncludeAudio && SourceHasAudio,
            AudioBitrateKbps = AudioBitrateKbps,
        },
    };

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
    partial void OnIncludeAudioChanged(bool value) => RecomputeFeasibility();
    partial void OnFormatChanged(OutputFormat value) => ExportConstraintsChanged?.Invoke(this, EventArgs.Empty);
}
