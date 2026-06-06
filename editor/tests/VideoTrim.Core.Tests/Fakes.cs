using VideoTrim.Core.Abstractions;
using VideoTrim.Core.Models;
using VideoTrim.Core.Probing;
using VideoTrim.Core.Services;

namespace VideoTrim.Core.Tests;

/// <summary>Records every process invocation and returns scripted exit codes.</summary>
public sealed class FakeProcessRunner : IProcessRunner
{
    private readonly Queue<int> _exitCodes = new();
    public List<(string Exe, List<string> Args)> Invocations { get; } = new();
    public List<string> StdoutToEmit { get; } = new();

    public void EnqueueExit(int code) => _exitCodes.Enqueue(code);

    public Task<ProcessResult> RunAsync(
        string executable, IReadOnlyList<string> arguments, string? workingDirectory,
        Action<string>? onStdoutLine, Action<string>? onStderrLine, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        Invocations.Add((executable, new List<string>(arguments)));
        foreach (var line in StdoutToEmit)
            onStdoutLine?.Invoke(line);
        int code = _exitCodes.Count > 0 ? _exitCodes.Dequeue() : 0;
        return Task.FromResult(new ProcessResult(code, string.Empty));
    }
}

/// <summary>File system fake with scripted file sizes for the GIF loop tests.</summary>
public sealed class FakeFileSystem : IFileSystem
{
    private readonly Queue<long> _sizes = new();
    public List<string> Deleted { get; } = new();

    public void EnqueueSize(long size) => _sizes.Enqueue(size);

    public long GetFileSize(string path) => _sizes.Count > 0 ? _sizes.Dequeue() : 0;
    public bool FileExists(string path) => true;
    public void DeleteIfExists(string path) => Deleted.Add(path);
    public string CreateTempSubdirectory(string prefix) => Path.Combine(Path.GetTempPath(), prefix + "fake");
    public void DeleteDirectory(string path) { }
}

public sealed class FakeLocator : IFfmpegLocator
{
    public FfmpegBinaries Resolve() => new("ffmpeg", "ffprobe");
    public void EnsureAvailable() { }
}

public sealed class FakeFilePicker : IFilePickerService
{
    public string? OpenResult { get; set; }
    public string? SaveResult { get; set; }
    public Task<string?> PickOpenVideoAsync() => Task.FromResult(OpenResult);
    public Task<string?> PickSaveAsync(string suggestedFileName, string extension, string formatLabel)
        => Task.FromResult(SaveResult);
}

public sealed class FakeProbeService : IMediaProbeService
{
    private readonly MediaInfo? _info;
    private readonly Exception? _error;

    public FakeProbeService(MediaInfo info) => _info = info;
    public FakeProbeService(Exception error) => _error = error;

    public Task<MediaInfo> ProbeAsync(string filePath, CancellationToken ct)
        => _error is not null ? Task.FromException<MediaInfo>(_error) : Task.FromResult(_info!);
}

public static class TestData
{
    public static MediaInfo Media(double fps = 30, int height = 1080, int width = 1920,
        double durationSeconds = 60, bool hasAudio = true) =>
        new("C:\\videos\\sample.mp4", TimeSpan.FromSeconds(durationSeconds), width, height, fps,
            "h264", hasAudio, hasAudio ? "aac" : null, 50_000_000);
}
