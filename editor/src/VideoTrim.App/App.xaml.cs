using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI.Xaml;
using VideoTrim.App.Services;
using VideoTrim.Core.Abstractions;
using VideoTrim.Core.Encoding;
using VideoTrim.Core.Probing;
using VideoTrim.Core.Services;
using VideoTrim.Core.ViewModels;

namespace VideoTrim.App;

/// <summary>
/// Application entry point. Wires up dependency injection (§4.2): all services and view-models are
/// registered here so construction is explicit and everything is mockable in tests.
/// </summary>
public partial class App : Application
{
    private Window? _window;

    public App()
    {
        InitializeComponent();
        Services = ConfigureServices();
    }

    /// <summary>Global service provider (resolved by views' code-behind).</summary>
    public static IServiceProvider Services { get; private set; } = default!;

    /// <summary>The main window, exposed so services (e.g. file pickers) can get its HWND.</summary>
    public static Window? MainWindow { get; private set; }

    protected override void OnLaunched(LaunchActivatedEventArgs args)
    {
        // Fail loudly if the bundled binaries are missing — it's a packaging bug (§12).
        Services.GetRequiredService<IFfmpegLocator>().EnsureAvailable();

        _window = new MainWindow();
        MainWindow = _window;
        _window.Activate();
    }

    private static IServiceProvider ConfigureServices()
    {
        var services = new ServiceCollection();

        // FFmpeg binaries are bundled next to the executable under Assets/ffmpeg (§14).
        string ffmpegFolder = Path.Combine(AppContext.BaseDirectory, "Assets", "ffmpeg");

        services.AddSingleton<IFfmpegLocator>(_ => new FfmpegLocator(ffmpegFolder));
        services.AddSingleton<IProcessRunner, ProcessRunner>();
        services.AddSingleton<IFileSystem, SystemFileSystem>();
        services.AddSingleton<IFfmpegCommandBuilder, FfmpegCommandBuilder>();
        services.AddSingleton<ITargetSizeCalculator, TargetSizeCalculator>();

        services.AddSingleton<IMediaProbeService, MediaProbeService>();
        services.AddSingleton<IEncodingService, FfmpegEncodingService>();
        services.AddSingleton<IThumbnailService, ThumbnailService>();
        services.AddSingleton<IFilePickerService, FilePickerService>();

        services.AddTransient<ExportViewModel>();
        services.AddTransient<GifViewModel>();
        services.AddTransient<MainViewModel>();

        return services.BuildServiceProvider();
    }
}
