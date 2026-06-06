using System.ComponentModel;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using VideoTrim.Core.ViewModels;
using Windows.ApplicationModel.DataTransfer;
using Windows.Media.Core;
using Windows.Media.Playback;
using Windows.Storage;

namespace VideoTrim.App.Views;

/// <summary>
/// Main page code-behind. Owns the <see cref="MediaPlayer"/> and the playback↔timeline sync
/// (§7.1): a ~30 Hz timer pushes the player position into the view-model while playing, and the
/// timeline's seek/scrub events drive the player position (scrub-to-preview, §7.2). All editing
/// state and validation live in <see cref="MainViewModel"/>.
/// </summary>
public sealed partial class MainPage : UserControl
{
    private readonly MainViewModel _vm;
    private readonly MediaPlayer _player = new();
    private readonly DispatcherQueueTimer _timer;
    private bool _isSeeking;

    public MainPage()
    {
        InitializeComponent();

        _vm = App.Services.GetRequiredService<MainViewModel>();
        DataContext = _vm;
        _vm.PropertyChanged += OnViewModelPropertyChanged;

        _player.AutoPlay = false;
        Player.SetMediaPlayer(_player);

        Timeline.SeekRequested += OnSeekRequested;

        _timer = DispatcherQueue.CreateTimer();
        _timer.Interval = TimeSpan.FromMilliseconds(33); // ~30 Hz
        _timer.Tick += OnTimerTick;
        _timer.Start();

        Unloaded += (_, _) =>
        {
            _timer.Stop();
            _player.Dispose();
        };
    }

    private void OnViewModelPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        switch (e.PropertyName)
        {
            case nameof(MainViewModel.Source):
                UpdatePlayerSource();
                break;
            case nameof(MainViewModel.IsPlaying):
                if (_vm.IsPlaying)
                    _player.Play();
                else
                    _player.Pause();
                break;
        }
    }

    private void UpdatePlayerSource()
    {
        if (_vm.Source is null)
        {
            _player.Source = null;
            return;
        }

        try
        {
            _player.Source = MediaSource.CreateFromUri(new Uri(_vm.Source.FilePath));
        }
        catch
        {
            _player.Source = null;
        }
    }

    private void OnTimerTick(DispatcherQueueTimer sender, object args)
    {
        if (_player.Source is null)
            return;

        MediaPlaybackSession? session = _player.PlaybackSession;
        if (session is null)
            return;

        if (_vm.IsPlaying && !_isSeeking)
            _vm.PlayheadPosition = session.Position;

        // Reflect natural end-of-playback back into the view-model.
        if (_vm.IsPlaying && session.PlaybackState == MediaPlaybackState.Paused &&
            _vm.Duration > TimeSpan.Zero && session.Position >= _vm.Duration - TimeSpan.FromMilliseconds(80))
        {
            _vm.IsPlaying = false;
        }
    }

    private void OnSeekRequested(object? sender, TimeSpan position)
    {
        if (_player.Source is null)
            return;

        _isSeeking = true;
        try
        {
            _player.PlaybackSession.Position = position;
        }
        catch
        {
            // Seeking can fail transiently while the media opens; ignore.
        }
        finally
        {
            _isSeeking = false;
        }
    }

    private void OnDragOver(object sender, DragEventArgs e)
    {
        if (e.DataView.Contains(StandardDataFormats.StorageItems))
        {
            e.AcceptedOperation = DataPackageOperation.Copy;
            e.DragUIOverride.Caption = "Open video";
        }
    }

    private async void OnDrop(object sender, DragEventArgs e)
    {
        if (!e.DataView.Contains(StandardDataFormats.StorageItems))
            return;

        var deferral = e.GetDeferral();
        try
        {
            var items = await e.DataView.GetStorageItemsAsync();
            if (items.OfType<StorageFile>().FirstOrDefault() is { } file)
                await _vm.LoadVideoAsync(file.Path);
        }
        finally
        {
            deferral.Complete();
        }
    }
}
