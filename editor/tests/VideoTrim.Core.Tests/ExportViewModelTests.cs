using VideoTrim.Core.Models;
using VideoTrim.Core.Services;
using VideoTrim.Core.ViewModels;

namespace VideoTrim.Core.Tests;

public sealed class ExportViewModelTests
{
    private static ExportViewModel Create() => new(new TargetSizeCalculator());

    [Fact]
    public void DefaultMode_IsQuality()
    {
        var vm = Create();
        Assert.True(vm.IsQualityMode);
        Assert.False(vm.IsBitrateMode);
        Assert.False(vm.IsTargetSizeMode);
        Assert.Equal(BitrateMode.Quality, vm.Mode);
    }

    [Fact]
    public void SettingModeFlag_True_SelectsThatMode()
    {
        var vm = Create();

        vm.IsBitrateMode = true;
        Assert.Equal(BitrateMode.Bitrate, vm.Mode);
        Assert.False(vm.IsQualityMode);

        vm.IsTargetSizeMode = true;
        Assert.Equal(BitrateMode.TargetSize, vm.Mode);
        Assert.False(vm.IsBitrateMode);
    }

    [Fact]
    public void SettingModeFlag_False_IsIgnored()
    {
        var vm = Create();
        vm.IsBitrateMode = true;
        vm.IsBitrateMode = false; // mirrors the radio group pushing IsChecked=false back
        Assert.Equal(BitrateMode.Bitrate, vm.Mode);
    }

    [Fact]
    public void LoadingSource_PopulatesHeightAndFpsWithSourceValues()
    {
        var vm = Create();
        vm.SetSource(TestData.Media(fps: 60, height: 1080));

        Assert.Equal("1080", vm.HeightField.Text);
        Assert.Equal("60", vm.FpsField.Text);
        // Equal-to-source values mean "same as source" → omit scaling/fps filters.
        Assert.Null(vm.ToSettings().TargetHeight);
        Assert.Null(vm.ToSettings().TargetFps);
    }

    [Fact]
    public void PickingSameAsSource_RestoresSourceValueInTheBox()
    {
        var vm = Create();
        vm.SetSource(TestData.Media(fps: 30, height: 1080));
        vm.HeightField.Text = "720";
        Assert.Equal(720, vm.ToSettings().TargetHeight);

        vm.HeightField.Text = ExportOptionCatalog.SameAsSource; // user re-selects the sentinel
        Assert.Equal("1080", vm.HeightField.Text);              // box becomes numeric source value
        Assert.Null(vm.ToSettings().TargetHeight);
    }

    [Fact]
    public void TypedHeight_IsNormalizedEvenAndApplied()
    {
        var vm = Create();
        vm.SetSource(TestData.Media(height: 2160));
        vm.HeightField.Text = "721";          // odd → 720
        Assert.Equal(720, vm.ToSettings().TargetHeight);
    }

    [Fact]
    public void TypedBitrate_FlowsToSettings()
    {
        var vm = Create();
        vm.IsBitrateMode = true;
        vm.BitrateField.Text = "3500k";
        Assert.Equal(3500, vm.ToSettings().VideoBitrateKbps);
    }

    [Fact]
    public void TypedAudioBitrate_FlowsToSettings()
    {
        var vm = Create();
        vm.SetSource(TestData.Media(hasAudio: true));
        vm.IncludeAudio = true;
        vm.AudioBitrateField.Text = "192";
        Assert.Equal(192, vm.ToSettings().Audio.AudioBitrateKbps);
    }

    [Fact]
    public void TargetSize_WithUnitText_DrivesFeasibility()
    {
        var vm = Create();
        vm.SetSource(TestData.Media(fps: 30, durationSeconds: 60));
        vm.UpdateTrimDuration(TimeSpan.FromSeconds(60));
        vm.IsTargetSizeMode = true;

        vm.TargetSizeField.Text = "10 MB";   // feasible
        Assert.True(vm.IsTargetSizeFeasible);
        Assert.False(vm.BlocksExport);

        vm.TargetSizeField.Text = "50 KB";   // infeasible
        Assert.False(vm.IsTargetSizeFeasible);
        Assert.True(vm.BlocksExport);
    }
}
