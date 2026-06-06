using VideoTrim.Core.Models;

namespace VideoTrim.Core.Probing;

/// <summary>Reads a media file's metadata via ffprobe (§6.1). Source of truth for the loaded video.</summary>
public interface IMediaProbeService
{
    Task<MediaInfo> ProbeAsync(string filePath, CancellationToken ct);
}
