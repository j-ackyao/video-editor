using System.Collections.ObjectModel;
using System.Globalization;
using System.Text.RegularExpressions;
using CommunityToolkit.Mvvm.ComponentModel;

namespace VideoTrim.Core.ViewModels;

/// <summary>
/// Backing view-model for an <b>editable</b> combo field: a single box that offers preset choices in
/// a dropdown but also accepts free-form typing (UX feedback — no spinner, presets + typing in one
/// control). When a "Same as source" sentinel is configured, picking it (or loading a video) fills
/// the box with the numeric source value, so the field is always numeric.
/// </summary>
public sealed partial class ComboFieldViewModel : ObservableObject
{
    private readonly Func<double, string> _formatSource;
    private double? _sourceValue;
    private bool _substituting;

    public ComboFieldViewModel(string? sameAsSourceLabel = null, Func<double, string>? formatSource = null)
    {
        SameAsSourceLabel = sameAsSourceLabel;
        _formatSource = formatSource ?? (v => v.ToString("0.###", CultureInfo.InvariantCulture));
    }

    /// <summary>Preset entries shown in the dropdown. The user may also type a value not in this list.</summary>
    public ObservableCollection<string> Options { get; } = new();

    /// <summary>The label that means "use the source value", or null if this field has no such option.</summary>
    public string? SameAsSourceLabel { get; }

    /// <summary>The formatted source value (e.g. "1080"), or null when no source has been applied.</summary>
    public string? SourceValueText => _sourceValue is { } v ? _formatSource(v) : null;

    [ObservableProperty] private string _text = string.Empty;

    /// <summary>Raised whenever the effective value changes (used to re-validate target size).</summary>
    public event EventHandler? ValueChanged;

    public void SetOptions(IEnumerable<string> options)
    {
        Options.Clear();
        foreach (var o in options)
            Options.Add(o);
    }

    /// <summary>Records the source value and shows it in the box (the numeric "same as source" default).</summary>
    public void ApplySourceValue(double value)
    {
        _sourceValue = value;
        Text = _formatSource(value);
    }

    partial void OnTextChanged(string value)
    {
        if (!_substituting
            && SameAsSourceLabel is not null
            && _sourceValue is { } sv
            && string.Equals(value?.Trim(), SameAsSourceLabel, StringComparison.OrdinalIgnoreCase))
        {
            // Replace the sentinel with the numeric source value so the field stays numeric.
            _substituting = true;
            Text = _formatSource(sv);
            _substituting = false;
            return;
        }

        ValueChanged?.Invoke(this, EventArgs.Empty);
    }
}

/// <summary>
/// Pure parsers that turn an editable-combo's text into a concrete numeric setting. Tolerant of
/// units and stray text ("2500k", "10 MB", "59.94", "Same as source"). Fully unit-testable.
/// </summary>
public static class FieldParsing
{
    private static readonly Regex LeadingNumber =
        new(@"[-+]?\d*\.?\d+", RegexOptions.Compiled | RegexOptions.CultureInvariant);

    /// <summary>Height in px, or null for "same as source" / empty / equal-to-source (omit scaling).</summary>
    public static int? ParseHeight(string? text, int? sourceHeight)
    {
        double? n = LeadingNumberOf(text);
        if (n is null)
            return null;
        int? h = ExportOptionCatalog.NormalizeHeight((int)Math.Round(n.Value));
        if (h is null)
            return null;
        if (sourceHeight is { } s && h.Value == s)
            return null; // unchanged → treat as "same as source"
        return h;
    }

    /// <summary>Fps, or null for "same as source" / empty / equal-to-source.</summary>
    public static double? ParseFps(string? text, double? sourceFps)
    {
        double? n = LeadingNumberOf(text);
        if (n is null || n.Value <= 0)
            return null;
        if (sourceFps is { } s && Math.Abs(n.Value - s) < 0.01)
            return null;
        return n;
    }

    /// <summary>Resolves a GIF fps: explicit value, else the source fps, else a 15 fps fallback.</summary>
    public static double ResolveGifFps(string? text, double? sourceFps)
        => ParseFps(text, sourceFps) ?? sourceFps ?? 15;

    /// <summary>Bitrate in kbps (accepts a trailing "k"/"kbps"), or null if not parseable/positive.</summary>
    public static int? ParseBitrateKbps(string? text)
    {
        double? n = LeadingNumberOf(text);
        if (n is null || n.Value <= 0)
            return null;
        return (int)Math.Round(n.Value);
    }

    /// <summary>
    /// Target size in bytes. Accepts an optional binary unit suffix (B/KB/KiB/MB/MiB/GB/GiB);
    /// a bare number defaults to MiB (matches the §9.1 binary convention). Null if not parseable.
    /// </summary>
    public static long? ParseSizeBytes(string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
            return null;

        Match m = LeadingNumber.Match(text);
        if (!m.Success || !double.TryParse(m.Value, NumberStyles.Float, CultureInfo.InvariantCulture, out double value) || value <= 0)
            return null;

        string unit = text[(m.Index + m.Length)..].Trim().ToLowerInvariant();
        double factor = unit switch
        {
            "" or "mb" or "mib" or "m" => 1024d * 1024d,
            "kb" or "kib" or "k" => 1024d,
            "gb" or "gib" or "g" => 1024d * 1024d * 1024d,
            "b" => 1d,
            _ => 1024d * 1024d, // unrecognized suffix → assume MiB
        };

        double bytes = value * factor;
        return bytes <= 0 ? null : (long)Math.Round(bytes);
    }

    private static double? LeadingNumberOf(string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
            return null;
        Match m = LeadingNumber.Match(text);
        if (!m.Success || !double.TryParse(m.Value, NumberStyles.Float, CultureInfo.InvariantCulture, out double n))
            return null;
        return n;
    }
}
