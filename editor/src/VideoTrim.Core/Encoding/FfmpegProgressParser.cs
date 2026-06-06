using System.Globalization;

namespace VideoTrim.Core.Encoding;

/// <summary>A parsed ffmpeg progress block (§6.4): how far the encode has progressed.</summary>
public readonly record struct FfmpegProgressSnapshot(TimeSpan ProcessedTime, double? SpeedX, string RawLine);

/// <summary>
/// Pure, stateful parser for ffmpeg's <c>-progress pipe:1</c> key=value stream (§6.4). Accumulates
/// fields until a <c>progress=continue|end</c> line closes a block, then yields a snapshot. No I/O,
/// so it is fully unit-testable.
/// </summary>
public sealed class FfmpegProgressParser
{
    private TimeSpan _outTime = TimeSpan.Zero;
    private double? _speed;
    private string _lastLine = string.Empty;

    /// <summary>
    /// Feeds one stdout line. Returns a snapshot when a progress block completes, otherwise null.
    /// </summary>
    public FfmpegProgressSnapshot? Feed(string line)
    {
        if (string.IsNullOrEmpty(line))
            return null;

        _lastLine = line;
        int eq = line.IndexOf('=');
        if (eq <= 0)
            return null;

        string key = line[..eq].Trim();
        string value = line[(eq + 1)..].Trim();

        switch (key)
        {
            case "out_time":
                if (TryParseOutTime(value, out var ts))
                    _outTime = ts;
                break;

            case "out_time_us":
                if (long.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out long us))
                    _outTime = TimeSpan.FromMilliseconds(us / 1000.0);
                break;

            case "speed":
                _speed = ParseSpeed(value);
                break;

            case "progress":
                // Block boundary ("continue" or "end") — emit the accumulated snapshot.
                return new FfmpegProgressSnapshot(_outTime, _speed, _lastLine);
        }

        return null;
    }

    private static bool TryParseOutTime(string value, out TimeSpan result)
    {
        result = TimeSpan.Zero;
        if (string.IsNullOrEmpty(value) || value == "N/A")
            return false;
        return TimeSpan.TryParse(value, CultureInfo.InvariantCulture, out result);
    }

    private static double? ParseSpeed(string value)
    {
        // Format is e.g. "3.2x"; trailing 'x' stripped. "N/A" early in the run → null.
        string trimmed = value.TrimEnd('x', 'X').Trim();
        if (double.TryParse(trimmed, NumberStyles.Float, CultureInfo.InvariantCulture, out double s))
            return s;
        return null;
    }
}
