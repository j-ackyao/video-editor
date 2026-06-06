using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;

namespace VideoTrim.App.Controls;

/// <summary>
/// The unified seek + trim timeline (US2 §7.2). Maps time ↔ x linearly, renders the kept range
/// highlight, and exposes two-way <see cref="TrimStart"/>/<see cref="TrimEnd"/>/<see cref="PlayheadPosition"/>
/// plus a <see cref="SeekRequested"/> event. The view-model owns the authoritative range and clamps;
/// this control only reports user intent.
/// </summary>
public sealed partial class TimelineControl : UserControl
{
    private enum DragTarget { None, Start, End }

    private DragTarget _drag = DragTarget.None;
    private const double HandleWidth = 14;

    public TimelineControl()
    {
        InitializeComponent();
    }

    /// <summary>Raised when the user seeks (track tapped) or scrubs a handle (scrub-to-preview).</summary>
    public event EventHandler<TimeSpan>? SeekRequested;

    public static readonly DependencyProperty DurationProperty = DependencyProperty.Register(
        nameof(Duration), typeof(TimeSpan), typeof(TimelineControl),
        new PropertyMetadata(TimeSpan.Zero, OnTimelineChanged));

    public static readonly DependencyProperty TrimStartProperty = DependencyProperty.Register(
        nameof(TrimStart), typeof(TimeSpan), typeof(TimelineControl),
        new PropertyMetadata(TimeSpan.Zero, OnTimelineChanged));

    public static readonly DependencyProperty TrimEndProperty = DependencyProperty.Register(
        nameof(TrimEnd), typeof(TimeSpan), typeof(TimelineControl),
        new PropertyMetadata(TimeSpan.Zero, OnTimelineChanged));

    public static readonly DependencyProperty PlayheadPositionProperty = DependencyProperty.Register(
        nameof(PlayheadPosition), typeof(TimeSpan), typeof(TimelineControl),
        new PropertyMetadata(TimeSpan.Zero, OnTimelineChanged));

    public TimeSpan Duration { get => (TimeSpan)GetValue(DurationProperty); set => SetValue(DurationProperty, value); }
    public TimeSpan TrimStart { get => (TimeSpan)GetValue(TrimStartProperty); set => SetValue(TrimStartProperty, value); }
    public TimeSpan TrimEnd { get => (TimeSpan)GetValue(TrimEndProperty); set => SetValue(TrimEndProperty, value); }
    public TimeSpan PlayheadPosition { get => (TimeSpan)GetValue(PlayheadPositionProperty); set => SetValue(PlayheadPositionProperty, value); }

    private static void OnTimelineChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
        => ((TimelineControl)d).UpdateVisuals();

    private double TrackWidth => TrackCanvas.ActualWidth;

    private void OnCanvasSizeChanged(object sender, SizeChangedEventArgs e) => UpdateVisuals();

    private void UpdateVisuals()
    {
        double width = TrackWidth;
        if (width <= 0 || Duration <= TimeSpan.Zero)
            return;

        double startX = TimeToX(TrimStart, width);
        double endX = TimeToX(TrimEnd, width);
        double playX = TimeToX(PlayheadPosition, width);

        TrackBackground.Width = width;
        TrackBackground.Height = 32;

        Canvas.SetLeft(DimLeft, 0);
        DimLeft.Width = Math.Max(0, startX);
        Canvas.SetLeft(DimRight, endX);
        DimRight.Width = Math.Max(0, width - endX);

        Canvas.SetLeft(KeptRange, startX);
        KeptRange.Width = Math.Max(0, endX - startX);

        Canvas.SetLeft(PlayheadLine, playX);
        Canvas.SetLeft(StartHandle, startX - HandleWidth / 2);
        Canvas.SetLeft(EndHandle, endX - HandleWidth / 2);
    }

    private double TimeToX(TimeSpan t, double width)
    {
        double frac = Duration.TotalSeconds > 0 ? t.TotalSeconds / Duration.TotalSeconds : 0;
        return Math.Clamp(frac, 0, 1) * width;
    }

    private TimeSpan XToTime(double x, double width)
    {
        double frac = width > 0 ? Math.Clamp(x / width, 0, 1) : 0;
        return TimeSpan.FromSeconds(frac * Duration.TotalSeconds);
    }

    private void OnHandlePointerPressed(object sender, PointerRoutedEventArgs e)
    {
        _drag = ReferenceEquals(sender, StartHandle) ? DragTarget.Start : DragTarget.End;
        TrackCanvas.CapturePointer(e.Pointer);
        e.Handled = true;
    }

    private void OnTrackPointerPressed(object sender, PointerRoutedEventArgs e)
    {
        if (_drag != DragTarget.None)
            return;

        // Tap on the track seeks the playhead.
        double x = e.GetCurrentPoint(TrackCanvas).Position.X;
        TimeSpan t = XToTime(x, TrackWidth);
        PlayheadPosition = t;
        SeekRequested?.Invoke(this, t);
    }

    private void OnCanvasPointerMoved(object sender, PointerRoutedEventArgs e)
    {
        if (_drag == DragTarget.None)
            return;

        double x = e.GetCurrentPoint(TrackCanvas).Position.X;
        TimeSpan t = XToTime(x, TrackWidth);

        if (_drag == DragTarget.Start)
            TrimStart = t;
        else
            TrimEnd = t;

        // Scrub-to-preview: show the frame under the handle being dragged (§7.2).
        SeekRequested?.Invoke(this, t);
    }

    private void OnCanvasPointerReleased(object sender, PointerRoutedEventArgs e)
    {
        if (_drag == DragTarget.None)
            return;
        _drag = DragTarget.None;
        TrackCanvas.ReleasePointerCapture(e.Pointer);
    }
}
