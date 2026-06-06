namespace VideoTrim.Core.Models;

/// <summary>Audio export settings (§5, US5 §7.5).</summary>
public sealed class AudioSettings
{
    public bool IncludeAudio { get; set; } = true;

    /// <summary>Audio bitrate in kbps; ignored when <see cref="IncludeAudio"/> is false.</summary>
    public int AudioBitrateKbps { get; set; } = 128;
}
