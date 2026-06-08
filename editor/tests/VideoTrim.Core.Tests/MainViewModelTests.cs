using VideoTrim.Core.Encoding;
using VideoTrim.Core.Models;
using VideoTrim.Core.Services;
using VideoTrim.Core.ViewModels;

namespace VideoTrim.Core.Tests;

public sealed class MainViewModelTests
{
    private static async Task<MainViewModel> CreateLoadedAsync(MediaInfo info)
    {
        var calc = new TargetSizeCalculator();
        var vm = new MainViewModel(
            new FakeFilePicker(),
            new FakeProbeService(info),
            new NoopEncoder(),
            new ExportViewModel(calc),
            new GifViewModel());
        await vm.LoadVideoAsync("C:\\videos\\sample.mp4");
        return vm;
    }

    [Fact]
    public async Task Load_InitializesFullRange()
    {
        var vm = await CreateLoadedAsync(TestData.Media(fps: 30, durationSeconds: 60));
        Assert.True(vm.IsMediaLoaded);
        Assert.Equal(TimeSpan.Zero, vm.TrimStart);
        Assert.Equal(TimeSpan.FromSeconds(60), vm.TrimEnd);
        Assert.True(vm.IsRangeValid);
    }

    [Fact]
    public async Task Load_Failure_ShowsErrorAndStaysUnloaded()
    {
        var calc = new TargetSizeCalculator();
        var vm = new MainViewModel(
            new FakeFilePicker(),
            new FakeProbeService(new EncodingException("Couldn't read this file.")),
            new NoopEncoder(),
            new ExportViewModel(calc),
            new GifViewModel());

        await vm.LoadVideoAsync("bad.mp4");

        Assert.False(vm.IsMediaLoaded);
        Assert.Equal(InfoSeverity.Error, vm.InfoSeverity);
    }

    [Fact]
    public async Task TrimStart_CannotCrossEnd_ClampsToEndMinusGap()
    {
        var vm = await CreateLoadedAsync(TestData.Media(fps: 30, durationSeconds: 10));
        vm.TrimStart = TimeSpan.FromSeconds(20); // beyond end
        Assert.True(vm.TrimStart <= vm.TrimEnd - vm.MinTrimGap + TimeSpan.FromMilliseconds(1));
        Assert.True(vm.TrimStart < vm.TrimEnd);
    }

    [Fact]
    public async Task TrimEnd_CannotCrossStart()
    {
        var vm = await CreateLoadedAsync(TestData.Media(fps: 30, durationSeconds: 10));
        vm.TrimStart = TimeSpan.FromSeconds(8);
        vm.TrimEnd = TimeSpan.FromSeconds(5); // before start
        Assert.True(vm.TrimEnd >= vm.TrimStart + vm.MinTrimGap - TimeSpan.FromMilliseconds(1));
    }

    [Fact]
    public async Task Playhead_ClampedToDuration()
    {
        var vm = await CreateLoadedAsync(TestData.Media(fps: 30, durationSeconds: 10));
        vm.PlayheadPosition = TimeSpan.FromSeconds(999);
        Assert.Equal(TimeSpan.FromSeconds(10), vm.PlayheadPosition);
        vm.PlayheadPosition = TimeSpan.FromSeconds(-5);
        Assert.Equal(TimeSpan.Zero, vm.PlayheadPosition);
    }

    [Fact]
    public async Task TrimStart_SnapsToFrame()
    {
        var vm = await CreateLoadedAsync(TestData.Media(fps: 10, durationSeconds: 10));
        vm.TrimStart = TimeSpan.FromSeconds(0.34); // round(3.4)=3 frames => 0.3s at 10fps
        Assert.Equal(TimeSpan.FromSeconds(0.3), vm.TrimStart);
    }

    [Fact]
    public async Task TargetSizeInfeasible_BlocksExport()
    {
        var vm = await CreateLoadedAsync(TestData.Media(fps: 30, durationSeconds: 60));
        vm.Export.Format = OutputFormat.Mp4;
        vm.Export.Mode = BitrateMode.TargetSize;
        vm.Export.TargetSizeField.Text = "50 KB"; // 50 KiB over 60s -> infeasible

        Assert.False(vm.Export.IsTargetSizeFeasible);
        Assert.False(vm.ExportCommand.CanExecute(null));
    }

    private sealed class NoopEncoder : IEncodingService
    {
        public Task<ExportResult> ExportVideoAsync(string i, string o, TrimRange r, ExportSettings s, MediaInfo m, IProgress<EncodeProgress> p, CancellationToken c)
            => Task.FromResult(new ExportResult(o, 1, true));

        public Task<ExportResult> ExportGifAsync(string i, string o, TrimRange r, GifSettings s, MediaInfo m, IProgress<EncodeProgress> p, CancellationToken c)
            => Task.FromResult(new ExportResult(o, 1, true));
    }
}
