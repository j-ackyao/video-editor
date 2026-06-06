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
        Assert.True(vm.IsBitrateMode);
        Assert.False(vm.IsTargetSizeMode);

        vm.IsTargetSizeMode = true;
        Assert.Equal(BitrateMode.TargetSize, vm.Mode);
        Assert.True(vm.IsTargetSizeMode);
        Assert.False(vm.IsBitrateMode);
    }

    [Fact]
    public void SettingModeFlag_False_IsIgnored()
    {
        // Mirrors the RadioButton group pushing IsChecked=false back through the binding:
        // it must not clear the current selection.
        var vm = Create();
        vm.IsBitrateMode = true;

        vm.IsBitrateMode = false; // push-back from the group

        Assert.Equal(BitrateMode.Bitrate, vm.Mode);
        Assert.True(vm.IsBitrateMode);
    }

    [Fact]
    public void ModeFlags_RaisePropertyChanged()
    {
        var vm = Create();
        var changed = new List<string?>();
        vm.PropertyChanged += (_, e) => changed.Add(e.PropertyName);

        vm.IsTargetSizeMode = true;

        Assert.Contains(nameof(ExportViewModel.IsQualityMode), changed);
        Assert.Contains(nameof(ExportViewModel.IsBitrateMode), changed);
        Assert.Contains(nameof(ExportViewModel.IsTargetSizeMode), changed);
    }

    [Fact]
    public void TargetSizeMode_FeasibleProducesNoBlock()
    {
        var vm = Create();
        vm.UpdateTrimDuration(TimeSpan.FromSeconds(30));
        vm.IsTargetSizeMode = true;
        vm.TargetSizeUnit = SizeUnit.MiB;
        vm.TargetSizeValue = 10; // comfortably feasible at 30s

        Assert.True(vm.IsTargetSizeFeasible);
        Assert.False(vm.BlocksExport);
    }

    [Fact]
    public void CustomResolution_RevealsAndAppliesEvenHeight()
    {
        var vm = Create();
        var custom = vm.ResolutionOptions.Single(r => r.IsCustom);

        Assert.False(vm.IsCustomResolution);
        vm.SelectedResolution = custom;
        Assert.True(vm.IsCustomResolution);

        vm.CustomHeight = 999;                 // odd → normalized to even
        Assert.Equal(998, vm.ToSettings().TargetHeight);
    }

    [Fact]
    public void CustomFps_RevealsAndApplies()
    {
        var vm = Create();
        var custom = vm.FpsOptions.Single(f => f.IsCustom);

        Assert.False(vm.IsCustomFps);
        vm.SelectedFps = custom;
        Assert.True(vm.IsCustomFps);

        vm.CustomFps = 144;
        Assert.Equal(144, vm.ToSettings().TargetFps);
    }

    [Fact]
    public void SameAsSource_IsNotTreatedAsCustom()
    {
        var vm = Create();
        vm.SetSource(TestData.Media(fps: 240, height: 2160));
        vm.SelectedResolution = vm.ResolutionOptions[0]; // "Same as source"
        vm.SelectedFps = vm.FpsOptions[0];

        Assert.False(vm.IsCustomResolution);
        Assert.False(vm.IsCustomFps);
        Assert.Null(vm.ToSettings().TargetHeight);
        Assert.Null(vm.ToSettings().TargetFps);
    }
}
