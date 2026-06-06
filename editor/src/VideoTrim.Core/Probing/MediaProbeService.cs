using System.Text;
using VideoTrim.Core.Abstractions;
using VideoTrim.Core.Encoding;
using VideoTrim.Core.Models;

namespace VideoTrim.Core.Probing;

/// <summary>
/// ffprobe-backed <see cref="IMediaProbeService"/>. Runs the §10.4 command, collects the JSON from
/// stdout, and maps it with <see cref="MediaInfoJsonParser"/>.
/// </summary>
public sealed class MediaProbeService : IMediaProbeService
{
    private readonly IProcessRunner _runner;
    private readonly IFfmpegLocator _locator;
    private readonly IFfmpegCommandBuilder _commandBuilder;
    private readonly IFileSystem _fileSystem;

    public MediaProbeService(
        IProcessRunner runner,
        IFfmpegLocator locator,
        IFfmpegCommandBuilder commandBuilder,
        IFileSystem fileSystem)
    {
        _runner = runner;
        _locator = locator;
        _commandBuilder = commandBuilder;
        _fileSystem = fileSystem;
    }

    public async Task<MediaInfo> ProbeAsync(string filePath, CancellationToken ct)
    {
        string ffprobe = _locator.Resolve().FfprobePath;
        var args = _commandBuilder.BuildProbeArgs(filePath);

        var stdout = new StringBuilder();
        ProcessResult result = await _runner.RunAsync(
            ffprobe, args, workingDirectory: null,
            onStdoutLine: line => stdout.AppendLine(line),
            onStderrLine: null,
            ct).ConfigureAwait(false);

        if (result.ExitCode != 0)
        {
            throw new EncodingException("Couldn't read this file.")
            {
                StdErrTail = result.StdErrTail,
            };
        }

        long? size = _fileSystem.FileExists(filePath) ? _fileSystem.GetFileSize(filePath) : null;

        try
        {
            return MediaInfoJsonParser.Parse(stdout.ToString(), filePath, size);
        }
        catch (Exception ex)
        {
            throw new EncodingException("Couldn't read this file.", ex);
        }
    }
}
