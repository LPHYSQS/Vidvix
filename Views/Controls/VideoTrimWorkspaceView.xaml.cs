using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Threading;
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
using VirtualKey = Windows.System.VirtualKey;

namespace Vidvix.Views.Controls;

public sealed partial class VideoTrimWorkspaceView : UserControl
{
    private static readonly TimeSpan ScrubPreviewInterval = TimeSpan.FromMilliseconds(90);
    private static readonly TimeSpan ScrubPreviewCacheInterval = TimeSpan.FromMilliseconds(120);
    private static readonly TimeSpan SelectionBoundaryPreviewBackoff = TimeSpan.FromMilliseconds(80);
    private static readonly TimeSpan PausedPreviewRefreshDelay = TimeSpan.FromMilliseconds(80);
    private static readonly TimeSpan PausedPreviewWarmupLead = TimeSpan.FromMilliseconds(180);
    private const int PreviewSeekCacheCapacity = 18;

    private readonly DispatcherQueue _dispatcherQueue;
    private readonly MediaPlayer _mediaPlayer;
    private readonly DispatcherQueueTimer _positionTimer;
    private readonly HashSet<long> _previewSeekCacheBuckets = new();
    private readonly LinkedList<long> _previewSeekCacheOrder = new();
    private readonly object _previewSeekSyncRoot = new();
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
    private CancellationTokenSource? _previewSeekCancellationSource;
    private int _previewSeekCacheSourceVersion = -1;
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
        ResetPreviewSeekState();
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

    private void OnJumpToSelectionStartClick(object sender, RoutedEventArgs e)
    {
        if (ViewModel is null)
        {
            return;
        }

        JumpToSelectionBoundary(GetSelectionStart());
    }

    private void OnJumpToSelectionEndClick(object sender, RoutedEventArgs e)
    {
        if (ViewModel is null)
        {
            return;
        }

        JumpToSelectionBoundary(GetSelectionEnd());
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

    private void OnSelectionInputBeforeTextChanging(TextBox sender, TextBoxBeforeTextChangingEventArgs args)
    {
        if (ViewModel is null)
        {
            return;
        }

        if (!ViewModel.IsPotentialSelectionInputText(args.NewText))
        {
            args.Cancel = true;
        }
    }

    private void OnSelectionStartInputGotFocus(object sender, RoutedEventArgs e) =>
        ViewModel?.BeginSelectionStartInputEdit();

    private void OnSelectionEndInputGotFocus(object sender, RoutedEventArgs e) =>
        ViewModel?.BeginSelectionEndInputEdit();

    private void OnSelectionStartInputLostFocus(object sender, RoutedEventArgs e) =>
        ViewModel?.CommitSelectionStartInput();

    private void OnSelectionEndInputLostFocus(object sender, RoutedEventArgs e) =>
        ViewModel?.CommitSelectionEndInput();

    private void OnSelectionStartInputKeyDown(object sender, KeyRoutedEventArgs e)
    {
        if (e.Key != VirtualKey.Enter)
        {
            return;
        }

        ViewModel?.CommitSelectionStartInput();
        e.Handled = true;
    }

    private void OnSelectionEndInputKeyDown(object sender, KeyRoutedEventArgs e)
    {
        if (e.Key != VirtualKey.Enter)
        {
            return;
        }

        ViewModel?.CommitSelectionEndInput();
        e.Handled = true;
    }

    private void OnTimelineSliderPointerPressed(object sender, PointerRoutedEventArgs e)
    {
        _isTimelineScrubbing = true;
        _resumePlaybackAfterScrub = ViewModel?.IsPlaying == true;
        _timelineRefreshVersion++;
        _hasPendingTimelineRefresh = false;
        _hasPendingScrubPreviewPosition = false;
        CancelPendingPreviewSeek();
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
                StopPlaybackAtSelectionEnd();
            }
            else
            {
                SeekTo(selectionEnd);
            }

            return;
        }

        if (ViewModel.IsPlaying && playbackPosition >= selectionEnd)
        {
            StopPlaybackAtSelectionEnd();
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

        ResetPreviewSeekState();
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

            ClearPreviewSeekCache();
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

            StopPlaybackAtSelectionEnd();
        });
    }

    private void OnMediaFailed(MediaPlayer sender, MediaPlayerFailedEventArgs args)
    {
        _dispatcherQueue.TryEnqueue(() =>
        {
            ResetPreviewSeekState();
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

            StopPlaybackAtSelectionEnd();
            return;
        }

        if (_isTimelineScrubbing)
        {
            return;
        }

        QueueTimelineRefresh(current);
    }

    private void JumpToSelectionBoundary(TimeSpan target)
    {
        if (ViewModel is null || _mediaPlayer.Source is null)
        {
            return;
        }

        StopScrubPreviewTimer();
        _resumePlaybackAfterScrub = false;
        InvalidatePendingTimelineRefresh();
        PausePlayback(syncTimelinePosition: false);
        QueuePreviewSeek(
            target,
            PreviewSeekMode.Exact,
            resumePlaybackAfterSeek: false);
    }

    private void SeekTo(TimeSpan position) => SeekTo(position, PreviewSeekMode.Exact, allowBoundaryBackoff: true);

    private void SeekTo(TimeSpan position, PreviewSeekMode mode, bool allowBoundaryBackoff)
    {
        if (_mediaPlayer.Source is null || ViewModel is null)
        {
            return;
        }

        var requestedTarget = Clamp(position, GetSelectionStart(), GetSelectionEnd());
        SetPreviewPosition(requestedTarget, mode, allowBoundaryBackoff);
        _isUpdatingTimeline = true;
        try
        {
            ViewModel.SyncCurrentPosition(requestedTarget);
        }
        finally
        {
            _isUpdatingTimeline = false;
        }
    }

    private void InvalidatePendingTimelineRefresh()
    {
        _timelineRefreshVersion++;
        _hasPendingTimelineRefresh = false;
    }

    private void CommitTimelinePosition()
    {
        if (ViewModel is null || _isUpdatingTimeline)
        {
            return;
        }

        StopScrubPreviewTimer();
        _hasPendingScrubPreviewPosition = false;
        var resumePlaybackAfterSeek = _resumePlaybackAfterScrub;
        _resumePlaybackAfterScrub = false;
        QueuePreviewSeek(
            TimeSpan.FromMilliseconds(TimelineSlider.Value),
            PreviewSeekMode.Exact,
            resumePlaybackAfterSeek,
            releasePlaybackStateSuppressionAfterSeek: true);
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
        return _mediaPlayer.PlaybackSession.Position;
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
        QueuePreviewSeek(
            _pendingScrubPreviewPosition,
            PreviewSeekMode.FastScrub,
            resumePlaybackAfterSeek: false);
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

    private void StopPlaybackAtSelectionEnd()
    {
        if (ViewModel is null || _mediaPlayer.Source is null)
        {
            return;
        }

        _resumePlaybackAfterScrub = false;
        StopScrubPreviewTimer();
        PausePlayback(syncTimelinePosition: false, seekPosition: GetSelectionEnd());
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

    private void QueuePreviewSeek(
        TimeSpan position,
        PreviewSeekMode mode,
        bool resumePlaybackAfterSeek,
        bool releasePlaybackStateSuppressionAfterSeek = false)
    {
        if (ViewModel is null || _mediaPlayer.Source is null)
        {
            if (releasePlaybackStateSuppressionAfterSeek)
            {
                _isSuppressingPlaybackStateSync = false;
            }

            return;
        }

        var request = new PreviewSeekRequest(
            Clamp(position, GetSelectionStart(), GetSelectionEnd()),
            mode,
            resumePlaybackAfterSeek,
            releasePlaybackStateSuppressionAfterSeek,
            _sourceVersion);

        var nextCancellationSource = new CancellationTokenSource();
        var previousCancellationSource = SwapPreviewSeekCancellationSource(nextCancellationSource);
        previousCancellationSource?.Cancel();
        previousCancellationSource?.Dispose();

        _ = Task.Run(
            () => ProcessPreviewSeekAsync(request, nextCancellationSource.Token),
            nextCancellationSource.Token);
    }

    private async Task ProcessPreviewSeekAsync(PreviewSeekRequest request, CancellationToken cancellationToken)
    {
        try
        {
            if (request.Mode == PreviewSeekMode.FastScrub &&
                IsPreviewSeekCached(request.SourceVersion, request.RequestedPosition))
            {
                return;
            }

            var dispatcherPriority = request.Mode == PreviewSeekMode.FastScrub
                ? DispatcherQueuePriority.Low
                : DispatcherQueuePriority.Normal;

            await EnqueueOnDispatcherAsync(() =>
            {
                if (!CanApplyPreviewSeek(request.SourceVersion))
                {
                    return;
                }

                if (_mediaPlayer.PlaybackSession.PlaybackState == MediaPlaybackState.Playing)
                {
                    PauseMediaPlayer();
                }

                StopPositionTimer();
                SetViewModelPlaying(false);
                SeekTo(
                    request.RequestedPosition,
                    request.Mode,
                    allowBoundaryBackoff: !request.ResumePlaybackAfterSeek);
            }, cancellationToken, dispatcherPriority).ConfigureAwait(false);

            if (request.ResumePlaybackAfterSeek)
            {
                await EnqueueOnDispatcherAsync(() =>
                {
                    if (!CanApplyPreviewSeek(request.SourceVersion))
                    {
                        return;
                    }

                    TrySetPlaybackRate(1d);
                    _mediaPlayer.Play();
                    StartPositionTimer();
                    SetViewModelPlaying(true);
                    if (request.ReleasePlaybackStateSuppressionAfterSeek)
                    {
                        _isSuppressingPlaybackStateSync = false;
                    }
                }, cancellationToken).ConfigureAwait(false);

                return;
            }

            if (request.Mode == PreviewSeekMode.Exact)
            {
                await ForcePausedPreviewRefreshAsync(
                    request.RequestedPosition,
                    request.SourceVersion,
                    request.ReleasePlaybackStateSuppressionAfterSeek,
                    cancellationToken).ConfigureAwait(false);
                return;
            }

            if (request.ReleasePlaybackStateSuppressionAfterSeek)
            {
                await EnqueueOnDispatcherAsync(
                    () => _isSuppressingPlaybackStateSync = false,
                    CancellationToken.None).ConfigureAwait(false);
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            if (request.ReleasePlaybackStateSuppressionAfterSeek)
            {
                await EnqueueOnDispatcherAsync(
                    () => _isSuppressingPlaybackStateSync = false,
                    CancellationToken.None).ConfigureAwait(false);
            }
        }
        catch (Exception exception)
        {
            Debug.WriteLine($"裁剪预览刷新失败：{exception}");
            if (request.ReleasePlaybackStateSuppressionAfterSeek)
            {
                await EnqueueOnDispatcherAsync(
                    () => _isSuppressingPlaybackStateSync = false,
                    CancellationToken.None).ConfigureAwait(false);
            }
        }
    }

    private async Task ForcePausedPreviewRefreshAsync(
        TimeSpan requestedPosition,
        int sourceVersion,
        bool releasePlaybackStateSuppressionAfterSeek,
        CancellationToken cancellationToken)
    {
        var warmupPosition = ResolvePausedPreviewWarmupPosition(requestedPosition);

        try
        {
            await EnqueueOnDispatcherAsync(() =>
            {
                if (!CanApplyPreviewSeek(sourceVersion))
                {
                    return;
                }

                _isSuppressingPlaybackStateSync = true;
                if (!AreClose(_mediaPlayer.PlaybackSession.Position, warmupPosition))
                {
                    SetPreviewPosition(warmupPosition, PreviewSeekMode.Exact, allowBoundaryBackoff: false);
                }

                TrySetPlaybackRate(1d);
                _mediaPlayer.Play();
            }, cancellationToken).ConfigureAwait(false);

            await Task.Delay(PausedPreviewRefreshDelay, cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
        }
        finally
        {
            await EnqueueOnDispatcherAsync(() =>
            {
                if (_mediaPlayer.Source is not null && CanApplyPreviewSeek(sourceVersion))
                {
                    PauseMediaPlayer();
                    StopPositionTimer();
                    SetViewModelPlaying(false);
                    SeekTo(requestedPosition, PreviewSeekMode.Exact, allowBoundaryBackoff: true);
                }

                _isSuppressingPlaybackStateSync = false;
                if (releasePlaybackStateSuppressionAfterSeek)
                {
                    _isSuppressingPlaybackStateSync = false;
                }
            }, CancellationToken.None).ConfigureAwait(false);
        }
    }

    private TimeSpan SetPreviewPosition(TimeSpan requestedPosition, PreviewSeekMode mode, bool allowBoundaryBackoff)
    {
        var previewTarget = ResolvePreviewTargetPosition(requestedPosition, mode, allowBoundaryBackoff);
        InvalidatePendingTimelineRefresh();
        _mediaPlayer.PlaybackSession.Position = previewTarget;
        if (mode == PreviewSeekMode.FastScrub)
        {
            RememberPreviewSeek(previewTarget);
        }

        return previewTarget;
    }

    private TimeSpan ResolvePreviewTargetPosition(TimeSpan requestedPosition, PreviewSeekMode mode, bool allowBoundaryBackoff)
    {
        var selectionStart = GetSelectionStart();
        var selectionEnd = GetSelectionEnd();
        var target = mode == PreviewSeekMode.FastScrub
            ? QuantizeScrubPreviewPosition(requestedPosition)
            : requestedPosition;

        if (allowBoundaryBackoff &&
            mode == PreviewSeekMode.Exact &&
            selectionEnd > selectionStart &&
            AreClose(requestedPosition, selectionEnd))
        {
            var availableBackoff = requestedPosition - selectionStart;
            if (availableBackoff > TimeSpan.Zero)
            {
                var backoffMilliseconds = Math.Min(
                    SelectionBoundaryPreviewBackoff.TotalMilliseconds,
                    Math.Max(1d, availableBackoff.TotalMilliseconds / 2d));
                target = requestedPosition - TimeSpan.FromMilliseconds(backoffMilliseconds);
            }
        }

        return Clamp(target, selectionStart, selectionEnd);
    }

    private TimeSpan ResolvePausedPreviewWarmupPosition(TimeSpan requestedPosition)
    {
        var previewTarget = ResolvePreviewTargetPosition(
            requestedPosition,
            PreviewSeekMode.Exact,
            allowBoundaryBackoff: true);
        var selectionStart = GetSelectionStart();
        var availableLead = previewTarget - selectionStart;
        if (availableLead <= TimeSpan.Zero)
        {
            return previewTarget;
        }

        // Paused seeks near trim boundaries need a short decoder run-up, otherwise the frame can stay stale.
        var leadMilliseconds = Math.Min(
            PausedPreviewWarmupLead.TotalMilliseconds,
            Math.Max(SelectionBoundaryPreviewBackoff.TotalMilliseconds, availableLead.TotalMilliseconds / 2d));
        return Clamp(
            previewTarget - TimeSpan.FromMilliseconds(leadMilliseconds),
            selectionStart,
            previewTarget);
    }

    private TimeSpan QuantizeScrubPreviewPosition(TimeSpan position)
    {
        var bucket = GetPreviewSeekBucket(position);
        return TimeSpan.FromMilliseconds(bucket * ScrubPreviewCacheInterval.TotalMilliseconds);
    }

    private bool CanApplyPreviewSeek(int sourceVersion) =>
        sourceVersion == _sourceVersion &&
        ViewModel is not null &&
        _mediaPlayer.Source is not null;

    private bool IsPreviewSeekCached(int sourceVersion, TimeSpan position)
    {
        if (_previewSeekCacheSourceVersion != sourceVersion)
        {
            return false;
        }

        return _previewSeekCacheBuckets.Contains(GetPreviewSeekBucket(position));
    }

    private void RememberPreviewSeek(TimeSpan position)
    {
        if (_previewSeekCacheSourceVersion != _sourceVersion)
        {
            ClearPreviewSeekCache();
            _previewSeekCacheSourceVersion = _sourceVersion;
        }

        var bucket = GetPreviewSeekBucket(position);
        if (_previewSeekCacheBuckets.Add(bucket))
        {
            _previewSeekCacheOrder.AddLast(bucket);
        }

        while (_previewSeekCacheOrder.Count > PreviewSeekCacheCapacity)
        {
            var oldestNode = _previewSeekCacheOrder.First;
            if (oldestNode is null)
            {
                break;
            }

            _previewSeekCacheOrder.RemoveFirst();
            _previewSeekCacheBuckets.Remove(oldestNode.Value);
        }
    }

    private void ResetPreviewSeekState()
    {
        CancelPendingPreviewSeek();
        ClearPreviewSeekCache();
        _hasPendingScrubPreviewPosition = false;
        _isSuppressingPlaybackStateSync = false;
    }

    private void CancelPendingPreviewSeek()
    {
        var cancellationSource = SwapPreviewSeekCancellationSource(null);
        cancellationSource?.Cancel();
        cancellationSource?.Dispose();
    }

    private CancellationTokenSource? SwapPreviewSeekCancellationSource(CancellationTokenSource? nextCancellationSource)
    {
        lock (_previewSeekSyncRoot)
        {
            var previousCancellationSource = _previewSeekCancellationSource;
            _previewSeekCancellationSource = nextCancellationSource;
            return previousCancellationSource;
        }
    }

    private void ClearPreviewSeekCache()
    {
        _previewSeekCacheBuckets.Clear();
        _previewSeekCacheOrder.Clear();
        _previewSeekCacheSourceVersion = -1;
    }

    private static long GetPreviewSeekBucket(TimeSpan position) =>
        (long)Math.Round(
            position.TotalMilliseconds / ScrubPreviewCacheInterval.TotalMilliseconds,
            MidpointRounding.AwayFromZero);

    private Task EnqueueOnDispatcherAsync(
        Action action,
        CancellationToken cancellationToken = default,
        DispatcherQueuePriority priority = DispatcherQueuePriority.Normal)
    {
        ArgumentNullException.ThrowIfNull(action);

        if (cancellationToken.IsCancellationRequested)
        {
            return Task.FromCanceled(cancellationToken);
        }

        if (_dispatcherQueue.HasThreadAccess)
        {
            action();
            return Task.CompletedTask;
        }

        var taskCompletionSource = new TaskCompletionSource<object?>(
            TaskCreationOptions.RunContinuationsAsynchronously);
        CancellationTokenRegistration cancellationRegistration = default;
        if (cancellationToken.CanBeCanceled)
        {
            cancellationRegistration = cancellationToken.Register(
                () => taskCompletionSource.TrySetCanceled(cancellationToken));
        }

        if (!_dispatcherQueue.TryEnqueue(priority, () =>
        {
            cancellationRegistration.Dispose();
            if (cancellationToken.IsCancellationRequested)
            {
                taskCompletionSource.TrySetCanceled(cancellationToken);
                return;
            }

            try
            {
                action();
                taskCompletionSource.TrySetResult(null);
            }
            catch (Exception exception)
            {
                taskCompletionSource.TrySetException(exception);
            }
        }))
        {
            cancellationRegistration.Dispose();
            taskCompletionSource.TrySetException(
                new InvalidOperationException("\u65e0\u6cd5\u5c06\u88c1\u526a\u9884\u89c8\u4efb\u52a1\u63d0\u4ea4\u5230 UI \u7ebf\u7a0b\u3002"));
        }

        return taskCompletionSource.Task;
    }

    private enum PreviewSeekMode
    {
        FastScrub,
        Exact
    }

    private readonly record struct PreviewSeekRequest(
        TimeSpan RequestedPosition,
        PreviewSeekMode Mode,
        bool ResumePlaybackAfterSeek,
        bool ReleasePlaybackStateSuppressionAfterSeek,
        int SourceVersion);
}
