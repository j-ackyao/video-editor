namespace VideoTrim.Core.Services;

/// <summary>
/// Thin, testable wrapper over the WinUI open/save pickers (§6.2). Lives in Core so view-models
/// stay UI-free; the WinUI implementation is in the app project.
/// </summary>
public interface IFilePickerService
{
    /// <summary>Shows an "open video" dialog. Returns the chosen path, or null if cancelled.</summary>
    Task<string?> PickOpenVideoAsync();

    /// <summary>
    /// Shows a "save as" dialog for the given format. <paramref name="extension"/> includes the
    /// leading dot (e.g. ".mp4"). Returns the chosen path, or null if cancelled.
    /// </summary>
    Task<string?> PickSaveAsync(string suggestedFileName, string extension, string formatLabel);
}
