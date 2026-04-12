using System;
using System.ComponentModel;
using System.IO;
using System.Threading.Tasks;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;
using Microsoft.UI.Xaml.Input;
using Vidvix.ViewModels;
using Windows.Media.Core;
using Windows.Media.Playback;
using Windows.Storage;

namespace Vidvix.Views.Controls;

public sealed partial class VideoTrimWorkspaceView : UserControl
{
    private readonly DispatcherQueue _dispatcherQueue;
    private readonly MediaPlayer _mediaPlayer;
    private readonly DispatcherQueueTimer _positionTimer;
    private bool _hasPendingTimelineRefresh;
    private bool _isPositionTimerRunning;
    private bool _isTimelineDragActive;
    private bool _isUpdatingPlaybackState;
    private bool _isUpdatingTimeline;
    private TimeSpan _pendingTimelinePosition;
    private int _sourceVersion;
    private int _timelineRefreshVersion;

    public VideoTrimWorkspaceView()
    {
        InitializeComponent();
        _dispatcherQueue = DispatcherQueue.GetForCurrentThread()
            ?? throw new InvalidOperationException("\u672a\u627e\u5230\u5f53\u524d\u7ebf\u7a0b\u7684\u8c03\u5ea6\u961f\u5217\u3002");
        _mediaPlayer = new MediaPlayer
        {
            AutoPlay = false,
            RealTimePlayback = true
        };
        _mediaPlayer.MediaOpened += OnMediaOpened;
        _mediaPlayer.MediaEnded += OnMediaEnded;
        _mediaPlayer.MediaFailed += OnMediaFailed;
        _mediaPlayer.PlaybackSession.PlaybackStateChanged += OnPlaybackStateChanged;
        PreviewPlayer.SetMediaPlayer(_mediaPlayer);

        _positionTimer = _dispatcherQueue.CreateTimer();
        _positionTimer.Interval = TimeSpan.FromMilliseconds(33);
        _positionTimer.IsRepeating = true;
        _positionTimer.Tick += OnPositionTimerTick;

        Loaded += OnLoaded;
        Unloaded += OnUnloaded;
    }

    public VideoTrimWorkspaceViewModel? ViewModel
    {
        get => (VideoTrimWorkspaceViewModel?)GetValue(ViewModelProperty);
        set => SetValue(ViewModelProperty, value);
    }

    public static readonly DependencyProperty ViewModelProperty = DependencyProperty.Register(
        nameof(ViewModel),
        typeof(VideoTrimWorkspaceViewModel),
        typeof(VideoTrimWorkspaceView),
        new PropertyMetadata(null, OnViewModelChanged));

    private static void OnViewModelChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        var control = (VideoTrimWorkspaceView)d;
        if (e.OldValue is VideoTrimWorkspaceViewModel oldViewModel)
        {
            oldViewModel.PropertyChanged -= control.OnViewModelPropertyChanged;
        }

        if (e.NewValue is VideoTrimWorkspaceViewModel newViewModel)
        {
            newViewModel.PropertyChanged += control.OnViewModelPropertyChanged;
            control._mediaPlayer.Volume = newViewModel.VolumeLevel;
        }

        _ = control.ReloadSourceAsync();
    }

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        if (ViewModel is not null)
        {
            _mediaPlayer.Volume = ViewModel.VolumeLevel;
        }

        _ = ReloadSourceAsync();
    }

    private void OnUnloaded(object sender, RoutedEventArgs e)
    {
        StopPositionTimer();
        PauseMediaPlayer();
    }

    private void OnPlayPauseClick(object sender, RoutedEventArgs e)
    {
        if (ViewModel is null || !ViewModel.CanPlayPreview)
        {
            return;
        }

        if (ViewModel.IsPlaying)
        {
            PausePlayback(syncTimelinePosition: true);
            return;
        }

        var selectionStart = GetSelectionStart();
        var selectionEnd = GetSelectionEnd();
        var current = _mediaPlayer.PlaybackSession.Position;
        if (current < selectionStart || current >= selectionEnd)
        {
            SeekTo(selectionStart);
        }

        TrySetPlaybackRate(1d);
        _mediaPlayer.Play();
        StartPositionTimer();
        SetViewModelPlaying(true);
    }

    private void OnResetPreviewClick(object sender, RoutedEventArgs e)
    {
        if (ViewModel is null)
        {
            return;
        }

        PausePlayback(syncTimelinePosition: true);
        SeekTo(GetSelectionStart());
    }

    private void OnTimelineSliderPointerPressed(object sender, PointerRoutedEventArgs e) => _isTimelineDragActive = true;

    private void OnTimelineSliderPointerReleased(object sender, PointerRoutedEventArgs e) => _isTimelineDragActive = false;

    private void OnTimelineSliderPointerCaptureLost(object sender, PointerRoutedEventArgs e) => _isTimelineDragActive = false;

    private void OnTimelineSliderValueChanged(object sender, RangeBaseValueChangedEventArgs e)
    {
        if (ViewModel is null || _isUpdatingTimeline || !_isTimelineDragActive)
        {
            return;
        }

        var target = TimeSpan.FromMilliseconds(e.NewValue);
        if (ViewModel.IsPlaying)
        {
            target = Clamp(target, GetSelectionStart(), GetSelectionEnd());
        }

        SeekTo(target);
    }

    private void OnRangeSelectorSelectionChanged(object sender, EventArgs e)
    {
        if (ViewModel is null)
        {
            return;
        }

        ViewModel.EnsureCurrentPositionWithinSelection();
        var current = TimeSpan.FromMilliseconds(ViewModel.CurrentPositionMilliseconds);
        if (current < GetSelectionStart() || current > GetSelectionEnd())
        {
            SeekTo(GetSelectionStart());
        }
    }

    private void OnViewModelPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (ViewModel is null)
        {
            return;
        }

        if (e.PropertyName == nameof(VideoTrimWorkspaceViewModel.InputPath))
        {
            _ = ReloadSourceAsync();
            return;
        }

        if (e.PropertyName == nameof(VideoTrimWorkspaceViewModel.VolumeLevel))
        {
            _mediaPlayer.Volume = ViewModel.VolumeLevel;
            return;
        }

        if (e.PropertyName == nameof(VideoTrimWorkspaceViewModel.IsPlaying) &&
            !_isUpdatingPlaybackState &&
            !ViewModel.IsPlaying)
        {
            PausePlayback(syncTimelinePosition: true, updateViewModelState: false);
        }
    }

    private async Task ReloadSourceAsync()
    {
        _sourceVersion++;
        var version = _sourceVersion;

        StopPositionTimer();
        PauseMediaPlayer();
        _mediaPlayer.Source = null;

        if (ViewModel is null || !ViewModel.HasInput || string.IsNullOrWhiteSpace(ViewModel.InputPath) || !File.Exists(ViewModel.InputPath))
        {
            return;
        }

        try
        {
            ViewModel.SetPreviewPreparing("\u6b63\u5728\u51c6\u5907\u89c6\u9891\u9884\u89c8...");
            var file = await StorageFile.GetFileFromPathAsync(ViewModel.InputPath);
            if (version != _sourceVersion)
            {
                return;
            }

            TrySetPlaybackRate(1d);
            _mediaPlayer.Source = MediaSource.CreateFromStorageFile(file);
            _mediaPlayer.Volume = ViewModel.VolumeLevel;
        }
        catch (Exception)
        {
            if (version != _sourceVersion || ViewModel is null)
            {
                return;
            }

            ViewModel.SetPreviewFailed("\u5f53\u524d\u89c6\u9891\u65e0\u6cd5\u9884\u89c8\uff0c\u4f46\u4ecd\u53ef\u5c1d\u8bd5\u76f4\u63a5\u5bfc\u51fa\u3002");
        }
    }

    private void OnMediaOpened(MediaPlayer sender, object args)
    {
        _dispatcherQueue.TryEnqueue(() =>
        {
            if (ViewModel is null)
            {
                return;
            }

            var duration = sender.PlaybackSession.NaturalDuration;
            if (duration > TimeSpan.Zero)
            {
                ViewModel.ApplyPlayableDuration(duration);
            }

            ViewModel.SetPreviewReady();
            SeekTo(GetSelectionStart());
        });
    }

    private void OnMediaEnded(MediaPlayer sender, object args)
    {
        _dispatcherQueue.TryEnqueue(() =>
        {
            if (ViewModel is null)
            {
                return;
            }

            PausePlayback(syncTimelinePosition: false, seekPosition: GetSelectionStart());
        });
    }

    private void OnMediaFailed(MediaPlayer sender, MediaPlayerFailedEventArgs args)
    {
        _dispatcherQueue.TryEnqueue(() =>
        {
            StopPositionTimer();
            ViewModel?.SetPreviewFailed("\u5f53\u524d\u89c6\u9891\u65e0\u6cd5\u9884\u89c8\uff0c\u4f46\u4ecd\u53ef\u5c1d\u8bd5\u76f4\u63a5\u5bfc\u51fa\u3002");
        });
    }

    private void OnPlaybackStateChanged(MediaPlaybackSession sender, object args)
    {
        _dispatcherQueue.TryEnqueue(() =>
        {
            if (ViewModel is null)
            {
                return;
            }

            switch (sender.PlaybackState)
            {
                case MediaPlaybackState.Playing:
                    StartPositionTimer();
                    SetViewModelPlaying(true);
                    break;

                case MediaPlaybackState.Paused:
                case MediaPlaybackState.None:
                    StopPositionTimer();
                    SetViewModelPlaying(false);
                    UpdateTimelinePosition(_mediaPlayer.PlaybackSession.Position);
                    break;
            }
        });
    }

    private void OnPositionTimerTick(DispatcherQueueTimer sender, object args)
    {
        if (ViewModel is null || _mediaPlayer.Source is null)
        {
            return;
        }

        var current = _mediaPlayer.PlaybackSession.Position;
        var selectionStart = GetSelectionStart();
        var selectionEnd = GetSelectionEnd();
        if (ViewModel.IsPlaying && selectionEnd > selectionStart && current >= selectionEnd)
        {
            PausePlayback(syncTimelinePosition: false, seekPosition: selectionStart);
            return;
        }

        if (_isTimelineDragActive)
        {
            return;
        }

        QueueTimelineRefresh(current);
    }

    private void SeekTo(TimeSpan position)
    {
        if (_mediaPlayer.Source is null || ViewModel is null)
        {
            return;
        }

        _mediaPlayer.PlaybackSession.Position = position;
        _isUpdatingTimeline = true;
        try
        {
            ViewModel.SyncCurrentPosition(position);
        }
        finally
        {
            _isUpdatingTimeline = false;
        }
    }

    private void PausePlayback(bool syncTimelinePosition, bool updateViewModelState = true, TimeSpan? seekPosition = null)
    {
        var pausedPosition = PauseMediaPlayer();
        StopPositionTimer();

        if (updateViewModelState)
        {
            SetViewModelPlaying(false);
        }

        if (seekPosition is { } target)
        {
            SeekTo(target);
            return;
        }

        if (syncTimelinePosition)
        {
            UpdateTimelinePosition(pausedPosition);
        }
    }

    private TimeSpan PauseMediaPlayer()
    {
        if (_mediaPlayer.Source is null)
        {
            return TimeSpan.Zero;
        }

        _mediaPlayer.Pause();
        var playbackSession = _mediaPlayer.PlaybackSession;
        var pausedPosition = playbackSession.Position;
        playbackSession.Position = pausedPosition;
        TrySetPlaybackRate(0d);
        return playbackSession.Position;
    }

    private void StartPositionTimer()
    {
        if (_isPositionTimerRunning)
        {
            return;
        }

        _positionTimer.Start();
        _isPositionTimerRunning = true;
    }

    private void StopPositionTimer()
    {
        _timelineRefreshVersion++;

        if (!_isPositionTimerRunning)
        {
            _hasPendingTimelineRefresh = false;
            return;
        }

        _positionTimer.Stop();
        _isPositionTimerRunning = false;
        _hasPendingTimelineRefresh = false;
    }

    private void QueueTimelineRefresh(TimeSpan position)
    {
        _pendingTimelinePosition = position;
        if (_hasPendingTimelineRefresh)
        {
            return;
        }

        _hasPendingTimelineRefresh = true;
        var refreshVersion = _timelineRefreshVersion;
        _dispatcherQueue.TryEnqueue(DispatcherQueuePriority.Low, () =>
        {
            _hasPendingTimelineRefresh = false;
            if (refreshVersion != _timelineRefreshVersion)
            {
                return;
            }

            UpdateTimelinePosition(_pendingTimelinePosition);
        });
    }

    private void UpdateTimelinePosition(TimeSpan position)
    {
        if (ViewModel is null || _isTimelineDragActive)
        {
            return;
        }

        _isUpdatingTimeline = true;
        try
        {
            ViewModel.SyncCurrentPosition(position);
        }
        finally
        {
            _isUpdatingTimeline = false;
        }
    }

    private void SetViewModelPlaying(bool isPlaying)
    {
        if (ViewModel is null)
        {
            return;
        }

        _isUpdatingPlaybackState = true;
        try
        {
            ViewModel.SetPlaying(isPlaying);
        }
        finally
        {
            _isUpdatingPlaybackState = false;
        }
    }

    private TimeSpan GetSelectionStart() =>
        ViewModel is null ? TimeSpan.Zero : TimeSpan.FromMilliseconds(ViewModel.SelectionStartMilliseconds);

    private TimeSpan GetSelectionEnd() =>
        ViewModel is null ? TimeSpan.Zero : TimeSpan.FromMilliseconds(ViewModel.SelectionEndMilliseconds);

    private static TimeSpan Clamp(TimeSpan value, TimeSpan minimum, TimeSpan maximum) =>
        value < minimum ? minimum : value > maximum ? maximum : value;

    private void TrySetPlaybackRate(double playbackRate)
    {
        try
        {
            _mediaPlayer.PlaybackSession.PlaybackRate = playbackRate;
        }
        catch (ArgumentOutOfRangeException)
        {
        }
        catch (NotSupportedException)
        {
        }
    }
}
