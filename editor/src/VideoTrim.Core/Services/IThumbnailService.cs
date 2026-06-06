using VideoTrim.Core.Abstractions;

namespace VideoTrim.Core.Services;

/// <summary>One extracted timeline thumbnail (§6.3): its source timestamp and PNG file path.</summary>
public sealed record ThumbFrame(TimeSpan Position, string ImagePath);

/// <summary>
/// Optional filmstrip generator for the timeline background (§6.3, §7.2). Nice-to-have (M7); the
/// core scrub-to-preview behavior does not depend on it.
/// </summary>
public interface IThumbnailService
{
    Task<IReadOnlyList<ThumbFrame>> GenerateFilmstripAsync(
        string filePath, int count, int thumbHeight, CancellationToken ct);
}

/// <summary>
/// ffmpeg-backed <see cref="IThumbnailService"/>. Extracts <c>count</c> evenly-spaced frames into a
/// temp folder and returns their paths. Implemented simply via per-frame seeks for robustness.
/// </summary>
public sealed class ThumbnailService : IThumbnailService
{
    private readonly IProcessRunner _runner;
    private readonly IFfmpegLocator _locator;
    private readonly Probing.IMediaProbeService _probe;
    private readonly IFileSystem _fileSystem;

    public ThumbnailService(
        IProcessRunner runner,
        IFfmpegLocator locator,
        Probing.IMediaProbeService probe,
        IFileSystem fileSystem)
    {
        _runner = runner;
        _locator = locator;
        _probe = probe;
        _fileSystem = fileSystem;
    }

    public async Task<IReadOnlyList<ThumbFrame>> GenerateFilmstripAsync(
        string filePath, int count, int thumbHeight, CancellationToken ct)
    {
        if (count <= 0)
            return Array.Empty<ThumbFrame>();

        var info = await _probe.ProbeAsync(filePath, ct).ConfigureAwait(false);
        string ffmpeg = _locator.Resolve().FfmpegPath;
        string dir = _fileSystem.CreateTempSubdirectory("videotrimthumbs_");
        var frames = new List<ThumbFrame>(count);

        double totalSeconds = info.Duration.TotalSeconds;
        for (int i = 0; i < count; i++)
        {
            ct.ThrowIfCancellationRequested();
            double t = count == 1 ? totalSeconds / 2 : totalSeconds * i / (count - 1);
            // Nudge the last frame slightly inside the clip so the seek lands on a real frame.
            t = Math.Min(t, Math.Max(0, totalSeconds - 0.05));
            string outPath = Path.Combine(dir, $"thumb_{i:D3}.png");

            var args = new[]
            {
                "-y", "-hide_banner",
                "-ss", t.ToString("0.000", System.Globalization.CultureInfo.InvariantCulture),
                "-i", filePath,
                "-frames:v", "1",
                "-vf", $"scale=-2:{thumbHeight}",
                outPath,
            };

            var result = await _runner.RunAsync(ffmpeg, args, dir, null, null, ct).ConfigureAwait(false);
            if (result.ExitCode == 0 && _fileSystem.FileExists(outPath))
                frames.Add(new ThumbFrame(TimeSpan.FromSeconds(t), outPath));
        }

        return frames;
    }
}
