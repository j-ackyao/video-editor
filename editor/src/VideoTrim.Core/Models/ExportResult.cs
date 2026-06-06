namespace VideoTrim.Core.Models;

/// <summary>
/// Outcome of an export. <see cref="MetTarget"/> is always true for non-target-size exports; for
/// target-size video/GIF it reports whether the final file landed at or under the requested size
/// (GIF may be a best-effort result after the bounded loop, §9.2).
/// </summary>
public sealed record ExportResult(string OutputPath, long FinalSizeBytes, bool MetTarget);
