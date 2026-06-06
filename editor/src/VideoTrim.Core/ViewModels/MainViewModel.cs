using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using VideoTrim.Core.Encoding;
using VideoTrim.Core.Models;
using VideoTrim.Core.Probing;
using VideoTrim.Core.Services;

namespace VideoTrim.Core.ViewModels;

/// <summary>
/// Orchestrating view-model for the main window (§4.1). Owns the authoritative trim range and
/// playhead, enforces the §7.2 invariants (clamping, min-gap, no crossing, frame snapping), and
/// drives the export pipeline with progress + cancellation. Holds no UI types so it is testable
/// without WinUI.
/// </summary>
public sealed partial class MainViewModel : ObservableObject
{
    private readonly IFilePickerService _filePicker;
    private readonly IMediaProbeService _probe;
    private readonly IEncodingService _encoder;

    private CancellationTokenSource? _exportCts;
    private TimeSpan _trimStart;
    private TimeSpan _trimEnd;
    private TimeSpan _playhead;
    private TimeSpan _duration;

    public MainViewModel(
        IFilePickerService filePicker,
        IMediaProbeService probe,
        IEncodingService encoder,
        ExportViewModel export,
        GifViewModel gif)
    {
        _filePicker = filePicker;
        _probe = probe;
        _encoder = encoder;
        Export = export;
        Gif = gif;
        Export.ExportConstraintsChanged += (_, _) => ExportCommand.NotifyCanExecuteChanged();
    }

    public ExportViewModel Export { get; }
    public GifViewModel Gif { get; }

    [ObservableProperty] private MediaInfo? _source;

    public string? FileName => Source is null ? null : Path.GetFileName(Source.FilePath);

    public string? SourceSummary => Source is null
        ? null
        : $"{Source.Width}×{Source.Height} · {Source.FrameRate:0.##} fps";

    public string PlayPauseLabel => IsPlaying ? "Pause" : "Play";

    partial void OnSourceChanged(MediaInfo? value)
    {
        OnPropertyChanged(nameof(FileName));
        OnPropertyChanged(nameof(SourceSummary));
    }

    partial void OnIsPlayingChanged(bool value) => OnPropertyChanged(nameof(PlayPauseLabel));

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(ExportCommand))]
    private bool _isMediaLoaded;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(ExportCommand))]
    [NotifyCanExecuteChangedFor(nameof(CancelExportCommand))]
    private bool _isExporting;

    [ObservableProperty] private double _exportProgress;
    [ObservableProperty] private string? _statusMessage;
    [ObservableProperty] private bool _isPlaying;
    [ObservableProperty] private string? _infoMessage;
    [ObservableProperty] private InfoSeverity _infoSeverity = InfoSeverity.Informational;
    [ObservableProperty] private bool _isInfoOpen;

    public TimeSpan Duration
    {
        get => _duration;
        private set
        {
            if (SetProperty(ref _duration, value))
                OnPropertyChanged(nameof(DurationText));
        }
    }

    /// <summary>Trim start handle (two-way bound). Snapped to frame and clamped (§7.2).</summary>
    public TimeSpan TrimStart
    {
        get => _trimStart;
        set => SetTrimStart(value);
    }

    /// <summary>Trim end handle (two-way bound). Snapped to frame and clamped (§7.2).</summary>
    public TimeSpan TrimEnd
    {
        get => _trimEnd;
        set => SetTrimEnd(value);
    }

    /// <summary>Playhead position (two-way bound). Clamped to [0, Duration] (§7.2).</summary>
    public TimeSpan PlayheadPosition
    {
        get => _playhead;
        set => SetPlayhead(value);
    }

    public TrimRange CurrentRange => new(_trimStart, _trimEnd);

    public string DurationText => FormatTime(_duration);
    public string PlayheadText => FormatTime(_playhead);
    public string SelectionText => $"{FormatTime(_trimStart)} – {FormatTime(_trimEnd)} ({FormatTime(CurrentRange.Duration)})";

    /// <summary>Minimum kept range: at least one frame and at least 0.1 s (§7.2).</summary>
    public TimeSpan MinTrimGap
    {
        get
        {
            double fps = Source?.FrameRate ?? 0;
            double oneFrame = fps > 0 ? 1.0 / fps : 0.1;
            return TimeSpan.FromSeconds(Math.Max(0.1, oneFrame));
        }
    }

    public bool IsRangeValid => IsMediaLoaded && (CurrentRange.Duration >= MinTrimGap);

    public async Task LoadVideoAsync(string path)
    {
        try
        {
            MediaInfo info = await _probe.ProbeAsync(path, CancellationToken.None).ConfigureAwait(true);
            Source = info;
            Duration = info.Duration;

            _trimStart = TimeSpan.Zero;
            _trimEnd = info.Duration;
            _playhead = TimeSpan.Zero;
            RaiseRangeChanged();

            IsMediaLoaded = true;
            Export.SetSource(info);
            Gif.SetSource(info);
            Export.UpdateTrimDuration(CurrentRange.Duration);
            ShowInfo($"Loaded {Path.GetFileName(path)} · {info.Width}×{info.Height} · {info.FrameRate:0.##} fps",
                InfoSeverity.Informational);
        }
        catch (Exception ex)
        {
            IsMediaLoaded = false;
            Source = null;
            ShowInfo(ex is EncodingException ? ex.Message : "Couldn't read this file.", InfoSeverity.Error);
        }
    }

    [RelayCommand]
    private async Task OpenVideoAsync()
    {
        string? path = await _filePicker.PickOpenVideoAsync().ConfigureAwait(true);
        if (path is not null)
            await LoadVideoAsync(path).ConfigureAwait(true);
    }

    [RelayCommand]
    private void TogglePlayPause() => IsPlaying = !IsPlaying;

    private bool CanExport() => IsMediaLoaded && !IsExporting && IsRangeValid && !Export.BlocksExport;

    [RelayCommand(CanExecute = nameof(CanExport))]
    private async Task ExportAsync()
    {
        if (Source is null)
            return;

        bool isGif = Export.Format == OutputFormat.Gif;
        string extension = FormatCatalog.Get(Export.Format).Extension;
        string suggested = Path.GetFileNameWithoutExtension(Source.FilePath) + "_trimmed";

        string? outputPath = await _filePicker
            .PickSaveAsync(suggested, extension, Export.Format.ToString())
            .ConfigureAwait(true);
        if (outputPath is null)
            return;

        _exportCts = new CancellationTokenSource();
        IsExporting = true;
        ExportProgress = 0;
        StatusMessage = "Starting…";

        var progress = new Progress<EncodeProgress>(p =>
        {
            ExportProgress = p.Percent;
            StatusMessage = p.SpeedX is { } sx
                ? $"{p.Percent:0}% · {sx:0.0}×"
                : $"{p.Percent:0}%";
        });

        try
        {
            TrimRange range = CurrentRange;
            ExportResult result = isGif
                ? await _encoder.ExportGifAsync(Source.FilePath, outputPath, range, Gif.ToSettings(), Source, progress, _exportCts.Token).ConfigureAwait(true)
                : await _encoder.ExportVideoAsync(Source.FilePath, outputPath, range, Export.ToSettings(), Source, progress, _exportCts.Token).ConfigureAwait(true);

            if (result.MetTarget)
                ShowInfo($"Exported {Path.GetFileName(result.OutputPath)} ({FormatBytes(result.FinalSizeBytes)}).", InfoSeverity.Success);
            else
                ShowInfo($"Exported, but couldn't get under the target size (final {FormatBytes(result.FinalSizeBytes)}).", InfoSeverity.Warning);
        }
        catch (OperationCanceledException)
        {
            ShowInfo("Export cancelled.", InfoSeverity.Informational);
        }
        catch (EncodingException ex)
        {
            ShowInfo(ex.Message, InfoSeverity.Error);
        }
        catch (Exception ex)
        {
            ShowInfo("Export failed: " + ex.Message, InfoSeverity.Error);
        }
        finally
        {
            IsExporting = false;
            ExportProgress = 0;
            StatusMessage = null;
            _exportCts?.Dispose();
            _exportCts = null;
        }
    }

    private bool CanCancelExport() => IsExporting;

    [RelayCommand(CanExecute = nameof(CanCancelExport))]
    private void CancelExport() => _exportCts?.Cancel();

    // ---- trim invariants (§7.2) -------------------------------------------

    private void SetTrimStart(TimeSpan value)
    {
        TimeSpan snapped = SnapToFrame(value);
        TimeSpan max = _trimEnd - MinTrimGap;
        if (max < TimeSpan.Zero)
            max = TimeSpan.Zero;
        TimeSpan clamped = Clamp(snapped, TimeSpan.Zero, max);
        if (SetProperty(ref _trimStart, clamped, nameof(TrimStart)))
            RaiseRangeChanged();
    }

    private void SetTrimEnd(TimeSpan value)
    {
        TimeSpan snapped = SnapToFrame(value);
        TimeSpan min = _trimStart + MinTrimGap;
        if (min > _duration)
            min = _duration;
        TimeSpan clamped = Clamp(snapped, min, _duration);
        if (SetProperty(ref _trimEnd, clamped, nameof(TrimEnd)))
            RaiseRangeChanged();
    }

    private void SetPlayhead(TimeSpan value)
    {
        TimeSpan clamped = Clamp(value, TimeSpan.Zero, _duration);
        if (SetProperty(ref _playhead, clamped, nameof(PlayheadPosition)))
            OnPropertyChanged(nameof(PlayheadText));
    }

    private void RaiseRangeChanged()
    {
        OnPropertyChanged(nameof(TrimStart));
        OnPropertyChanged(nameof(TrimEnd));
        OnPropertyChanged(nameof(PlayheadPosition));
        OnPropertyChanged(nameof(PlayheadText));
        OnPropertyChanged(nameof(CurrentRange));
        OnPropertyChanged(nameof(SelectionText));
        OnPropertyChanged(nameof(IsRangeValid));
        Export.UpdateTrimDuration(CurrentRange.Duration);
        ExportCommand.NotifyCanExecuteChanged();
    }

    /// <summary>Snaps a time to the nearest source frame; falls back to the raw value (§7.2).</summary>
    internal TimeSpan SnapToFrame(TimeSpan t)
    {
        double fps = Source?.FrameRate ?? 0;
        if (fps <= 0)
            return t;
        long frame = (long)Math.Round(t.TotalSeconds * fps, MidpointRounding.AwayFromZero);
        return TimeSpan.FromSeconds(frame / fps);
    }

    private static TimeSpan Clamp(TimeSpan value, TimeSpan min, TimeSpan max)
    {
        if (max < min)
            return min;
        if (value < min)
            return min;
        return value > max ? max : value;
    }

    private void ShowInfo(string message, InfoSeverity severity)
    {
        InfoMessage = message;
        InfoSeverity = severity;
        IsInfoOpen = true;
    }

    internal static string FormatTime(TimeSpan t)
    {
        if (t < TimeSpan.Zero)
            t = TimeSpan.Zero;
        return t.TotalHours >= 1
            ? t.ToString(@"h\:mm\:ss")
            : t.ToString(@"m\:ss");
    }

    internal static string FormatBytes(long bytes)
    {
        string[] units = { "B", "KB", "MB", "GB" };
        double size = bytes;
        int unit = 0;
        while (size >= 1024 && unit < units.Length - 1)
        {
            size /= 1024;
            unit++;
        }
        return $"{size:0.#} {units[unit]}";
    }
}
