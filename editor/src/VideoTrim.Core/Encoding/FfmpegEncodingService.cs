using VideoTrim.Core.Abstractions;
using VideoTrim.Core.Models;
using VideoTrim.Core.Services;

namespace VideoTrim.Core.Encoding;

/// <summary>
/// FFmpeg-backed <see cref="IEncodingService"/>. Drives ffmpeg as a child process (via
/// <see cref="IProcessRunner"/>), implementing the single-pass CRF path (§10.1), the two-pass
/// bitrate/target-size path (§10.2, §9.1) and the GIF palettegen/paletteuse pipeline with the
/// bounded iterative target-size loop (§10.3, §9.2). All work is off the UI thread; progress is
/// surfaced via <see cref="IProgress{T}"/> and cancellation kills the process tree (§11).
/// </summary>
public sealed class FfmpegEncodingService : IEncodingService
{
    private readonly IProcessRunner _runner;
    private readonly IFfmpegLocator _locator;
    private readonly IFfmpegCommandBuilder _commandBuilder;
    private readonly ITargetSizeCalculator _sizeCalculator;
    private readonly IFileSystem _fileSystem;

    public FfmpegEncodingService(
        IProcessRunner runner,
        IFfmpegLocator locator,
        IFfmpegCommandBuilder commandBuilder,
        ITargetSizeCalculator sizeCalculator,
        IFileSystem fileSystem)
    {
        _runner = runner;
        _locator = locator;
        _commandBuilder = commandBuilder;
        _sizeCalculator = sizeCalculator;
        _fileSystem = fileSystem;
    }

    private static string NullSink => OperatingSystem.IsWindows() ? "NUL" : "/dev/null";

    public async Task<ExportResult> ExportVideoAsync(
        string inputPath, string outputPath,
        TrimRange range, ExportSettings settings,
        MediaInfo source,
        IProgress<EncodeProgress> progress, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        string ffmpeg = _locator.Resolve().FfmpegPath;
        TimeSpan total = range.Duration;
        string tempDir = _fileSystem.CreateTempSubdirectory("videotrim_");

        try
        {
            switch (settings.Mode)
            {
                case BitrateMode.Quality:
                    var crfArgs = _commandBuilder.BuildCrfExportArgs(inputPath, outputPath, range, settings, source);
                    await RunPhaseAsync(ffmpeg, crfArgs, tempDir, total, 0, 100, "Encoding", progress, ct)
                        .ConfigureAwait(false);
                    break;

                case BitrateMode.Bitrate:
                    int givenKbps = settings.VideoBitrateKbps
                        ?? throw new EncodingException("No target bitrate was provided.");
                    await RunTwoPassAsync(ffmpeg, inputPath, outputPath, range, settings, source,
                        givenKbps, tempDir, total, progress, ct).ConfigureAwait(false);
                    break;

                case BitrateMode.TargetSize:
                    long target = settings.TargetSizeBytes
                        ?? throw new EncodingException("No target size was provided.");
                    TargetSizeResult calc = _sizeCalculator.ComputeVideoBitrate(target, total, settings.Audio);
                    if (!calc.Feasible)
                        throw new EncodingException(calc.Message ?? "The target size is not achievable.");
                    await RunTwoPassAsync(ffmpeg, inputPath, outputPath, range, settings, source,
                        calc.VideoKbps, tempDir, total, progress, ct).ConfigureAwait(false);
                    break;

                default:
                    throw new EncodingException($"Unsupported bitrate mode: {settings.Mode}.");
            }

            long size = _fileSystem.FileExists(outputPath) ? _fileSystem.GetFileSize(outputPath) : 0;
            bool met = settings.Mode != BitrateMode.TargetSize
                       || settings.TargetSizeBytes is null
                       || size <= settings.TargetSizeBytes;

            progress.Report(new EncodeProgress(100, total, null, "Done"));
            return new ExportResult(outputPath, size, met);
        }
        catch
        {
            _fileSystem.DeleteIfExists(outputPath);
            throw;
        }
        finally
        {
            _fileSystem.DeleteDirectory(tempDir);
        }
    }

    public async Task<ExportResult> ExportGifAsync(
        string inputPath, string outputPath,
        TrimRange range, GifSettings settings,
        MediaInfo source,
        IProgress<EncodeProgress> progress, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        string ffmpeg = _locator.Resolve().FfmpegPath;
        TimeSpan total = range.Duration;
        string tempDir = _fileSystem.CreateTempSubdirectory("videotrimgif_");
        string palettePath = Path.Combine(tempDir, "palette.png");

        try
        {
            if (settings.TargetSizeBytes is null)
            {
                await EncodeGifOnceAsync(ffmpeg, inputPath, outputPath, palettePath, range, settings,
                    total, 0, 100, "Creating GIF", progress, ct).ConfigureAwait(false);
                long sizeOnce = _fileSystem.FileExists(outputPath) ? _fileSystem.GetFileSize(outputPath) : 0;
                progress.Report(new EncodeProgress(100, total, null, "Done"));
                return new ExportResult(outputPath, sizeOnce, true);
            }

            long target = settings.TargetSizeBytes.Value;
            GifSettings current = settings;
            long lastSize = 0;

            for (int attempt = 0; attempt < GifSizeReducer.MaxIters; attempt++)
            {
                string label = $"GIF attempt {attempt + 1}";
                await EncodeGifOnceAsync(ffmpeg, inputPath, outputPath, palettePath, range, current,
                    total, 0, 100, label, progress, ct).ConfigureAwait(false);

                lastSize = _fileSystem.FileExists(outputPath) ? _fileSystem.GetFileSize(outputPath) : 0;
                if (lastSize <= target)
                {
                    progress.Report(new EncodeProgress(100, total, null, "Done"));
                    return new ExportResult(outputPath, lastSize, true);
                }

                if (attempt == GifSizeReducer.MaxIters - 1)
                    break;

                GifSettings? next = GifSizeReducer.Reduce(current, source.Height);
                if (next is null)
                    break; // Nothing left to reduce — keep the best-effort smallest result.
                current = next;
            }

            progress.Report(new EncodeProgress(100, total, null, "Done (best effort)"));
            return new ExportResult(outputPath, lastSize, lastSize <= target);
        }
        catch
        {
            _fileSystem.DeleteIfExists(outputPath);
            throw;
        }
        finally
        {
            _fileSystem.DeleteIfExists(palettePath);
            _fileSystem.DeleteDirectory(tempDir);
        }
    }

    private async Task RunTwoPassAsync(
        string ffmpeg, string inputPath, string outputPath, TrimRange range, ExportSettings settings,
        MediaInfo source, int videoKbps, string tempDir, TimeSpan total,
        IProgress<EncodeProgress> progress, CancellationToken ct)
    {
        // Both passes share the same stats log; pin it inside the per-export temp dir (§10.2).
        string passLogPrefix = Path.Combine(tempDir, "ffmpeg2pass");

        var pass1 = _commandBuilder.BuildTwoPassArgs(1, inputPath, outputPath, range, settings, source,
            videoKbps, passLogPrefix, NullSink);
        await RunPhaseAsync(ffmpeg, pass1, tempDir, total, 0, 50, "Analyzing (pass 1)", progress, ct)
            .ConfigureAwait(false);

        var pass2 = _commandBuilder.BuildTwoPassArgs(2, inputPath, outputPath, range, settings, source,
            videoKbps, passLogPrefix, NullSink);
        await RunPhaseAsync(ffmpeg, pass2, tempDir, total, 50, 100, "Encoding (pass 2)", progress, ct)
            .ConfigureAwait(false);
    }

    private async Task EncodeGifOnceAsync(
        string ffmpeg, string inputPath, string outputPath, string palettePath, TrimRange range,
        GifSettings settings, TimeSpan total, double startPct, double endPct, string label,
        IProgress<EncodeProgress> progress, CancellationToken ct)
    {
        double split = startPct + (endPct - startPct) * 0.2; // palettegen 0–20% of the slice (§6.4).

        var genArgs = _commandBuilder.BuildGifPaletteGenArgs(inputPath, palettePath, range, settings);
        await RunPhaseAsync(ffmpeg, genArgs, Path.GetDirectoryName(palettePath), total,
            startPct, split, $"{label} (palette)", progress, ct).ConfigureAwait(false);

        var useArgs = _commandBuilder.BuildGifPaletteUseArgs(inputPath, palettePath, outputPath, range, settings);
        await RunPhaseAsync(ffmpeg, useArgs, Path.GetDirectoryName(palettePath), total,
            split, endPct, label, progress, ct).ConfigureAwait(false);
    }

    private async Task RunPhaseAsync(
        string ffmpeg, IReadOnlyList<string> args, string? workingDir, TimeSpan total,
        double startPct, double endPct, string label,
        IProgress<EncodeProgress> progress, CancellationToken ct)
    {
        var parser = new FfmpegProgressParser();

        void OnStdout(string line)
        {
            FfmpegProgressSnapshot? snap = parser.Feed(line);
            if (snap is not { } s)
                return;

            double local = total.TotalSeconds > 0
                ? Math.Clamp(s.ProcessedTime.TotalSeconds / total.TotalSeconds, 0, 1)
                : 0;
            double percent = startPct + (endPct - startPct) * local;
            progress.Report(new EncodeProgress(percent, s.ProcessedTime, s.SpeedX, $"{label}: {s.RawLine}"));
        }

        ProcessResult result = await _runner
            .RunAsync(ffmpeg, args, workingDir, OnStdout, onStderrLine: null, ct)
            .ConfigureAwait(false);

        if (result.ExitCode != 0)
        {
            throw new EncodingException($"FFmpeg failed during {label.ToLowerInvariant()}.")
            {
                StdErrTail = result.StdErrTail,
            };
        }

        progress.Report(new EncodeProgress(endPct, total, null, $"{label}: done"));
    }
}
