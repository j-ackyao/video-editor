namespace VideoTrim.Core.Abstractions;

/// <summary>Resolved absolute (or PATH-relative) paths to the ffmpeg/ffprobe executables.</summary>
public sealed record FfmpegBinaries(string FfmpegPath, string FfprobePath);

/// <summary>Locates the ffmpeg/ffprobe binaries the app runs (§14 — always bundled, never assumed).</summary>
public interface IFfmpegLocator
{
    FfmpegBinaries Resolve();

    /// <summary>
    /// Verifies the configured binaries exist. Called at startup so a packaging bug fails loudly
    /// rather than at first export (§12: "ffmpeg/ffprobe missing → hard error").
    /// </summary>
    void EnsureAvailable();
}

/// <summary>
/// Default locator. When a binary folder is supplied (the app points it at <c>Assets/ffmpeg</c>),
/// the executables are resolved there; otherwise the bare names are returned and the OS PATH is
/// used (handy for dev / tests).
/// </summary>
public sealed class FfmpegLocator : IFfmpegLocator
{
    private readonly string? _binaryFolder;

    public FfmpegLocator(string? binaryFolder = null) => _binaryFolder = binaryFolder;

    public FfmpegBinaries Resolve() =>
        new(ResolveTool("ffmpeg"), ResolveTool("ffprobe"));

    public void EnsureAvailable()
    {
        if (_binaryFolder is null)
            return; // PATH-based; cannot cheaply verify.

        var binaries = Resolve();
        if (!File.Exists(binaries.FfmpegPath) || !File.Exists(binaries.FfprobePath))
        {
            throw new FileNotFoundException(
                "Bundled ffmpeg/ffprobe not found. Expected them under: " + _binaryFolder);
        }
    }

    private string ResolveTool(string tool)
    {
        string exe = OperatingSystem.IsWindows() ? tool + ".exe" : tool;
        return _binaryFolder is null ? exe : Path.Combine(_binaryFolder, exe);
    }
}
