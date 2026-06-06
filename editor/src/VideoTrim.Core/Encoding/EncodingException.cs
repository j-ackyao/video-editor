namespace VideoTrim.Core.Encoding;

/// <summary>Raised when an ffmpeg/ffprobe process fails. Carries a user-friendly summary (§12).</summary>
public sealed class EncodingException : Exception
{
    public EncodingException(string message) : base(message) { }

    public EncodingException(string message, Exception inner) : base(message, inner) { }

    /// <summary>The tail of ffmpeg's stderr, for the diagnostics log (not necessarily shown raw).</summary>
    public string? StdErrTail { get; init; }
}
