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
    private static readonly TimeSpan ScrubPreviewInterval = TimeSpan.FromMilliseconds(90);
    private readonly DispatcherQueue _dispatcherQueue;
    private readonly MediaPlayer _mediaPlayer;
    private readonly DispatcherQueueTimer _positionTimer;
    private readonly DispatcherQueueTimer _scrubPreviewTimer;
    private readonly ToolTip _volumeToolTip;
    private bool _hasPendingTimelineRefresh;
    private bool _hasPendingScrubPreviewPosition;
    private bool _isPositionTimerRunning;
    private bool _isSuppressingPlaybackStateSync;
    private bool _isTimelineScrubbing;
    private bool _resumePlaybackAfterScrub;
    private bool _isUpdatingPlaybackState;
    private bool _isUpdatingTimeline;
    private TimeSpan _pendingScrubPreviewPosition;
    private TimeSpan _pendingTimelinePosition;
    private int _sourceVersion;
    private int _timelineRefreshVersion;

    public VideoTrimWorkspaceView()
    {
        InitializeComponent();
        RegisterTimelineInteractionHandlers();
        _volumeToolTip = new ToolTip();
        ToolTipService.SetToolTip(VolumeButton, _volumeToolTip);
        UpdateVolumeToolTip();
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

        _scrubPreviewTimer = _dispatcherQueue.CreateTimer();
        _scrubPreviewTimer.Interval = ScrubPreviewInterval;
        _scrubPreviewTimer.IsRepeating = true;
        _scrubPreviewTimer.Tick += OnScrubPreviewTimerTick;

        Loaded += OnLoaded;
        Unloaded += OnUnloaded;
    }

    private void RegisterTimelineInteractionHandlers()
    {
        TimelineSlider.AddHandler(PointerPressedEvent, new PointerEventHandler(OnTimelineSliderPointerPressed), true);
        TimelineSlider.AddHandler(PointerReleasedEvent, new PointerEventHandler(OnTimelineSliderPointerReleased), true);
        TimelineSlider.AddHandler(PointerCaptureLostEvent, new PointerEventHandler(OnTimelineSliderPointerCaptureLost), true);
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

        control.UpdateVolumeToolTip();
        _ = control.ReloadSourceAsync();
    }

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        if (ViewModel is not null)
        {
            _mediaPlayer.Volume = ViewModel.VolumeLevel;
        }

        UpdateVolumeToolTip();
        _ = ReloadSourceAsync();
    }

    private void OnUnloaded(object sender, RoutedEventArgs e)
    {
        StopScrubPreviewTimer();
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
        var current = TimeSpan.FromMilliseconds(ViewModel.CurrentPositionMilliseconds);
        if (current < selectionStart || current >= selectionEnd)
        {
            current = selectionStart;
        }

        SeekTo(current);
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

    private void OnJumpToSelectionEndClick(object sender, RoutedEventArgs e)
    {
        if (ViewModel is null)
        {
            return;
        }

        PausePlayback(syncTimelinePosition: true);
        SeekTo(GetSelectionEnd());
    }

    private void OnVolumeButtonPointerWheelChanged(object sender, PointerRoutedEventArgs e)
    {
        if (ViewModel is null)
        {
            return;
        }

        var delta = e.GetCurrentPoint((UIElement)sender).Properties.MouseWheelDelta;
        if (delta == 0)
        {
            return;
        }

        AdjustVolume(Math.Sign(delta) * 5d);
        e.Handled = true;
    }

    private void OnTimelineSliderPointerPressed(object sender, PointerRoutedEventArgs e)
    {
        _isTimelineScrubbing = true;
        _resumePlaybackAfterScrub = ViewModel?.IsPlaying == true;
        _timelineRefreshVersion++;
        _hasPendingTimelineRefresh = false;
        _hasPendingScrubPreviewPosition = false;
        StartScrubPreviewTimer();

        if (!_resumePlaybackAfterScrub || _mediaPlayer.Source is null)
        {
            return;
        }

        _isSuppressingPlaybackStateSync = true;
        PauseMediaPlayer();
        StopPositionTimer();
    }

    private void OnTimelineSliderPointerReleased(object sender, PointerRoutedEventArgs e)
    {
        _isTimelineScrubbing = false;
        CommitTimelinePosition();
    }

    private void OnTimelineSliderPointerCaptureLost(object sender, PointerRoutedEventArgs e)
    {
        _isTimelineScrubbing = false;
        CommitTimelinePosition();
    }

    private void OnTimelineSliderValueChanged(object sender, RangeBaseValueChangedEventArgs e)
    {
        if (ViewModel is null || _isUpdatingTimeline || !_isTimelineScrubbing)
        {
            return;
        }

        _isUpdatingTimeline = true;
        try
        {
            var target = TimeSpan.FromMilliseconds(e.NewValue);
            ViewModel.SyncCurrentPosition(target);
            _pendingScrubPreviewPosition = Clamp(target, GetSelectionStart(), GetSelectionEnd());
            _hasPendingScrubPreviewPosition = true;
        }
        finally
        {
            _isUpdatingTimeline = false;
        }
    }

    private void OnRangeSelectorSelectionChanged(object sender, EventArgs e)
    {
        if (ViewModel is null)
        {
            return;
        }

        var constrainedPosition = ViewModel.EnsureCurrentPositionWithinSelection();
        if (_mediaPlayer.Source is null)
        {
            return;
        }

        var selectionStart = GetSelectionStart();
        var selectionEnd = GetSelectionEnd();
        var playbackPosition = _mediaPlayer.PlaybackSession.Position;

        if (playbackPosition < selectionStart)
        {
            SeekTo(selectionStart);
            return;
        }

        if (playbackPosition > selectionEnd)
        {
            if (ViewModel.IsPlaying)
            {
                LoopPlaybackToSelectionStart();
            }
            else
            {
                SeekTo(selectionEnd);
            }

            return;
        }

        if (ViewModel.IsPlaying && playbackPosition >= selectionEnd)
        {
            LoopPlaybackToSelectionStart();
            return;
        }

        if (!AreClose(constrainedPosition, playbackPosition))
        {
            SeekTo(constrainedPosition);
            return;
        }

        UpdateTimelinePosition(constrainedPosition);
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
            UpdateVolumeToolTip();
            return;
        }

        if (e.PropertyName == nameof(VideoTrimWorkspaceViewModel.VolumeToolTipText))
        {
            UpdateVolumeToolTip();
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
            if (ViewModel is null || _isTimelineScrubbing || _isSuppressingPlaybackStateSync)
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

            if (_isTimelineScrubbing)
            {
                StopScrubPreviewTimer();
                PauseMediaPlayer();
                StopPositionTimer();
                return;
            }

            LoopPlaybackToSelectionStart();
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
            if (ViewModel is null || _isTimelineScrubbing || _isSuppressingPlaybackStateSync)
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
            if (_isTimelineScrubbing)
            {
                StopScrubPreviewTimer();
                PauseMediaPlayer();
                StopPositionTimer();
                return;
            }

            LoopPlaybackToSelectionStart();
            return;
        }

        if (_isTimelineScrubbing)
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

        var target = Clamp(position, GetSelectionStart(), GetSelectionEnd());
        _mediaPlayer.PlaybackSession.Position = target;
        _isUpdatingTimeline = true;
        try
        {
            ViewModel.SyncCurrentPosition(target);
        }
        finally
        {
            _isUpdatingTimeline = false;
        }
    }

    private void CommitTimelinePosition()
    {
        if (ViewModel is null || _isUpdatingTimeline)
        {
            return;
        }

        StopScrubPreviewTimer();
        _hasPendingScrubPreviewPosition = false;
        SeekTo(TimeSpan.FromMilliseconds(TimelineSlider.Value));

        if (_resumePlaybackAfterScrub)
        {
            _resumePlaybackAfterScrub = false;
            TrySetPlaybackRate(1d);
            _mediaPlayer.Play();
            StartPositionTimer();
            SetViewModelPlaying(true);
        }

        _isSuppressingPlaybackStateSync = false;
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

    private void OnScrubPreviewTimerTick(DispatcherQueueTimer sender, object args)
    {
        if (!_isTimelineScrubbing || _mediaPlayer.Source is null)
        {
            return;
        }

        if (!_hasPendingScrubPreviewPosition)
        {
            return;
        }

        _hasPendingScrubPreviewPosition = false;
        SeekTo(_pendingScrubPreviewPosition);
    }

    private void StartScrubPreviewTimer()
    {
        if (_scrubPreviewTimer.IsRunning)
        {
            return;
        }

        _scrubPreviewTimer.Start();
    }

    private void StopScrubPreviewTimer()
    {
        _hasPendingScrubPreviewPosition = false;
        if (_scrubPreviewTimer.IsRunning)
        {
            _scrubPreviewTimer.Stop();
        }
    }

    private void UpdateTimelinePosition(TimeSpan position)
    {
        if (ViewModel is null || _isTimelineScrubbing)
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

    private static bool AreClose(TimeSpan left, TimeSpan right) =>
        Math.Abs((left - right).TotalMilliseconds) < 1d;

    private void AdjustVolume(double deltaPercent)
    {
        if (ViewModel is null)
        {
            return;
        }

        ViewModel.VolumePercent = Math.Clamp(ViewModel.VolumePercent + deltaPercent, 0d, 100d);
        _mediaPlayer.Volume = ViewModel.VolumeLevel;
    }

    private void LoopPlaybackToSelectionStart()
    {
        if (ViewModel is null || _mediaPlayer.Source is null)
        {
            return;
        }

        _isSuppressingPlaybackStateSync = true;
        SeekTo(GetSelectionStart());
        TrySetPlaybackRate(1d);

        if (_mediaPlayer.PlaybackSession.PlaybackState != MediaPlaybackState.Playing)
        {
            _mediaPlayer.Play();
        }

        StartPositionTimer();
        SetViewModelPlaying(true);
        _dispatcherQueue.TryEnqueue(DispatcherQueuePriority.Low, () => _isSuppressingPlaybackStateSync = false);
    }

    private void UpdateVolumeToolTip()
    {
        _volumeToolTip.Content = ViewModel?.VolumeToolTipText ?? "\u97f3\u91cf";
    }

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
