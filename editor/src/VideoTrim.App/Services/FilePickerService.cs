using VideoTrim.Core.Services;
using Windows.Storage.Pickers;

namespace VideoTrim.App.Services;

/// <summary>WinUI implementation of <see cref="IFilePickerService"/> (§6.2).</summary>
public sealed class FilePickerService : IFilePickerService
{
    private static readonly string[] VideoExtensions =
        { ".mp4", ".mov", ".mkv", ".webm", ".avi", ".m4v", ".wmv", ".flv", ".mpeg", ".mpg" };

    public async Task<string?> PickOpenVideoAsync()
    {
        var picker = new FileOpenPicker
        {
            ViewMode = PickerViewMode.List,
            SuggestedStartLocation = PickerLocationId.VideosLibrary,
        };
        InitializeWithWindow(picker);
        foreach (var ext in VideoExtensions)
            picker.FileTypeFilter.Add(ext);

        var file = await picker.PickSingleFileAsync();
        return file?.Path;
    }

    public async Task<string?> PickSaveAsync(string suggestedFileName, string extension, string formatLabel)
    {
        var picker = new FileSavePicker
        {
            SuggestedStartLocation = PickerLocationId.VideosLibrary,
            SuggestedFileName = suggestedFileName,
        };
        InitializeWithWindow(picker);
        picker.FileTypeChoices.Add(formatLabel, new List<string> { extension });

        var file = await picker.PickSaveFileAsync();
        return file?.Path;
    }

    private static void InitializeWithWindow(object picker)
    {
        var hwnd = WinRT.Interop.WindowNative.GetWindowHandle(App.MainWindow);
        WinRT.Interop.InitializeWithWindow.Initialize(picker, hwnd);
    }
}
