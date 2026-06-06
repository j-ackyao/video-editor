namespace VideoTrim.Core.Abstractions;

/// <summary>
/// Minimal file-system surface the encoding service needs (temp dirs, sizes, deletes). Abstracted
/// so the GIF iterative target-size loop (§9.2) can be unit-tested with scripted sizes (§13).
/// </summary>
public interface IFileSystem
{
    long GetFileSize(string path);
    bool FileExists(string path);
    void DeleteIfExists(string path);
    string CreateTempSubdirectory(string prefix);
    void DeleteDirectory(string path);
}

/// <summary>Real <see cref="IFileSystem"/> over <see cref="System.IO"/>.</summary>
public sealed class SystemFileSystem : IFileSystem
{
    public long GetFileSize(string path) => new FileInfo(path).Length;

    public bool FileExists(string path) => File.Exists(path);

    public void DeleteIfExists(string path)
    {
        try
        {
            if (File.Exists(path))
                File.Delete(path);
        }
        catch
        {
            // Best effort cleanup.
        }
    }

    public string CreateTempSubdirectory(string prefix) =>
        Directory.CreateTempSubdirectory(prefix).FullName;

    public void DeleteDirectory(string path)
    {
        try
        {
            if (Directory.Exists(path))
                Directory.Delete(path, recursive: true);
        }
        catch
        {
            // Best effort cleanup.
        }
    }
}
