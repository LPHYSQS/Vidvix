using System;
using System.ComponentModel;
using System.Threading.Tasks;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;
using Microsoft.UI.Xaml.Input;
using Vidvix.Core.Models;
using Vidvix.ViewModels;
using Windows.Foundation;
using VirtualKey = Windows.System.VirtualKey;

namespace Vidvix.Views.Controls;

public sealed partial class VideoTrimWorkspaceView : UserControl
{
    private static readonly TimeSpan ScrubPreviewInterval = TimeSpan.FromMilliseconds(90);

    private readonly DispatcherQueue _dispatcherQueue;
    private readonly DispatcherQueueTimer _positionTimer;
    private readonly DispatcherQueueTimer _scrubPreviewTimer;
    private readonly ToolTip _volumeToolTip;
    private bool _hasPendingScrubPreviewPosition;
    private bool _isControlLoaded;
    private bool _isPositionTimerRunning;
    private bool _isTimelineScrubbing;
    private bool _isUpdatingPlaybackState;
    private bool _isUpdatingTimeline;
    private bool _resumePlaybackAfterScrub;
    private int _sourceVersion;
    private TimeSpan _pendingScrubPreviewPosition;
    private VideoPreviewHostPlacement? _lastPreviewPlacement;

    public VideoTrimWorkspaceView()
    {
        InitializeComponent();
        PreviewPlayer.Visibility = Visibility.Collapsed;
        PreviewPlayer.IsHitTestVisible = false;
        RegisterTimelineInteractionHandlers();
        _volumeToolTip = new ToolTip();
        ToolTipService.SetToolTip(VolumeButton, _volumeToolTip);
        UpdateVolumeToolTip();
        _dispatcherQueue = DispatcherQueue.GetForCurrentThread()
            ?? throw new InvalidOperationException("未找到当前线程的调度队列。");

        _positionTimer = _dispatcherQueue.CreateTimer();
        _positionTimer.Interval = TimeSpan.FromMilliseconds(33);
        _positionTimer.IsRepeating = true;
        _positionTimer.Tick += OnPositionTimerTick;

        _scrubPreviewTimer = _dispatcherQueue.CreateTimer();
        _scrubPreviewTimer.Interval = ScrubPreviewInterval;
        _scrubPreviewTimer.IsRepeating = true;
        _scrubPreviewTimer.Tick += OnScrubPreviewTimerTick;

        LayoutUpdated += OnLayoutUpdated;
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
        }

        control.UpdateVolumeToolTip();
        control.UpdatePreviewHostPlacement(force: true);
        _ = control.ReloadSourceAsync();
    }

    private void RegisterTimelineInteractionHandlers()
    {
        TimelineSlider.AddHandler(PointerPressedEvent, new PointerEventHandler(OnTimelineSliderPointerPressed), true);
        TimelineSlider.AddHandler(PointerReleasedEvent, new PointerEventHandler(OnTimelineSliderPointerReleased), true);
        TimelineSlider.AddHandler(PointerCaptureLostEvent, new PointerEventHandler(OnTimelineSliderPointerCaptureLost), true);
    }

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        _isControlLoaded = true;
        UpdateVolumeToolTip();
        UpdatePreviewHostPlacement(force: true);
        _ = ReloadSourceAsync();
    }

    private void OnUnloaded(object sender, RoutedEventArgs e)
    {
        _isControlLoaded = false;
        StopScrubPreviewTimer();
        StopPositionTimer();
        _resumePlaybackAfterScrub = false;
        _isTimelineScrubbing = false;
        UpdatePreviewHostPlacement(force: true);
    }

    private void OnLayoutUpdated(object? sender, object e)
    {
        if (!_isControlLoaded)
        {
            return;
        }

        UpdatePreviewHostPlacement();
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

        ViewModel.SeekPreview(current);
        ViewModel.PlayPreview();
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
        _hasPendingScrubPreviewPosition = false;
        StartScrubPreviewTimer();

        if (!_resumePlaybackAfterScrub || ViewModel is null || !ViewModel.HasLoadedPreview)
        {
            return;
        }

        ViewModel.PausePreview();
        StopPositionTimer();
        SetViewModelPlaying(false);
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
        if (!ViewModel.HasLoadedPreview)
        {
            UpdateTimelinePosition(constrainedPosition);
            return;
        }

        var selectionStart = GetSelectionStart();
        var selectionEnd = GetSelectionEnd();
        var playbackPosition = ViewModel.GetPreviewPosition();

        if (playbackPosition < selectionStart)
        {
            ViewModel.SeekPreview(selectionStart);
            UpdateTimelinePosition(selectionStart);
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
                ViewModel.SeekPreview(selectionEnd);
                UpdateTimelinePosition(selectionEnd);
            }

            return;
        }

        if (!AreClose(constrainedPosition, playbackPosition))
        {
            ViewModel.SeekPreview(constrainedPosition);
            UpdateTimelinePosition(constrainedPosition);
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
            UpdatePreviewHostPlacement(force: true);
            _ = ReloadSourceAsync();
            return;
        }

        if (e.PropertyName == nameof(VideoTrimWorkspaceViewModel.IsPreviewReady) ||
            e.PropertyName == nameof(VideoTrimWorkspaceViewModel.EditorVisibility))
        {
            UpdatePreviewHostPlacement(force: true);
            return;
        }

        if (e.PropertyName == nameof(VideoTrimWorkspaceViewModel.VolumeToolTipText))
        {
            UpdateVolumeToolTip();
            return;
        }

        if (e.PropertyName == nameof(VideoTrimWorkspaceViewModel.IsPlaying) && !_isUpdatingPlaybackState)
        {
            if (ViewModel.IsPlaying)
            {
                StartPositionTimer();
                return;
            }

            StopPositionTimer();
            UpdateTimelinePosition(ViewModel.GetPreviewPosition());
        }
    }

    private async Task ReloadSourceAsync()
    {
        _sourceVersion++;
        var version = _sourceVersion;
        StopScrubPreviewTimer();
        StopPositionTimer();
        _resumePlaybackAfterScrub = false;
        _isTimelineScrubbing = false;

        if (ViewModel is null)
        {
            return;
        }

        try
        {
            await ViewModel.ReloadPreviewAsync().ConfigureAwait(true);
        }
        catch (Exception)
        {
            if (ViewModel is not null && version == _sourceVersion)
            {
                ViewModel.SetPreviewFailed("当前视频无法预览，但仍可尝试直接导出。");
            }
        }
        finally
        {
            if (version == _sourceVersion)
            {
                UpdatePreviewHostPlacement(force: true);
            }
        }
    }

    private void OnPositionTimerTick(DispatcherQueueTimer sender, object args)
    {
        if (ViewModel is null || !ViewModel.HasLoadedPreview)
        {
            return;
        }

        var current = ViewModel.GetPreviewPosition();
        var selectionStart = GetSelectionStart();
        var selectionEnd = GetSelectionEnd();
        if (ViewModel.IsPlaying && selectionEnd > selectionStart && current >= selectionEnd)
        {
            StopPlaybackAtSelectionEnd();
            return;
        }

        if (current < selectionStart)
        {
            current = selectionStart;
        }
        else if (current > selectionEnd)
        {
            current = selectionEnd;
        }

        UpdateTimelinePosition(current);
    }

    private void OnScrubPreviewTimerTick(DispatcherQueueTimer sender, object args)
    {
        if (ViewModel is null || !_hasPendingScrubPreviewPosition || !ViewModel.HasLoadedPreview)
        {
            return;
        }

        _hasPendingScrubPreviewPosition = false;
        ViewModel.SetPreviewPlaybackPosition(_pendingScrubPreviewPosition);
    }

    private void CommitTimelinePosition()
    {
        StopScrubPreviewTimer();
        if (ViewModel is null)
        {
            _resumePlaybackAfterScrub = false;
            return;
        }

        var target = Clamp(TimeSpan.FromMilliseconds(ViewModel.CurrentPositionMilliseconds), GetSelectionStart(), GetSelectionEnd());
        if (!ViewModel.HasLoadedPreview)
        {
            UpdateTimelinePosition(target);
            _resumePlaybackAfterScrub = false;
            return;
        }

        ViewModel.SeekPreview(target);
        UpdateTimelinePosition(target);

        if (_resumePlaybackAfterScrub)
        {
            ViewModel.PlayPreview();
            StartPositionTimer();
            SetViewModelPlaying(true);
        }
        else
        {
            ViewModel.PausePreview();
            SetViewModelPlaying(false);
        }

        _resumePlaybackAfterScrub = false;
    }

    private void PausePlayback(bool syncTimelinePosition, bool updateViewModelState = true, TimeSpan? seekPosition = null)
    {
        if (ViewModel is null)
        {
            return;
        }

        var pausedPosition = ViewModel.HasLoadedPreview
            ? ViewModel.PausePreview()
            : TimeSpan.FromMilliseconds(ViewModel.CurrentPositionMilliseconds);
        StopPositionTimer();

        if (seekPosition is { } target && ViewModel.HasLoadedPreview)
        {
            ViewModel.SeekPreview(target);
            pausedPosition = ViewModel.GetPreviewPosition();
        }
        else if (seekPosition is { } fallbackTarget)
        {
            pausedPosition = fallbackTarget;
        }

        if (syncTimelinePosition || seekPosition is not null)
        {
            UpdateTimelinePosition(pausedPosition);
        }

        if (updateViewModelState)
        {
            SetViewModelPlaying(false);
        }
    }

    private void JumpToSelectionBoundary(TimeSpan target)
    {
        if (ViewModel is null || !ViewModel.HasLoadedPreview)
        {
            return;
        }

        StopScrubPreviewTimer();
        PausePlayback(syncTimelinePosition: false, seekPosition: target);
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
        if (!_isPositionTimerRunning)
        {
            return;
        }

        _positionTimer.Stop();
        _isPositionTimerRunning = false;
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

    private void UpdatePreviewHostPlacement(bool force = false)
    {
        if (ViewModel is null)
        {
            return;
        }

        var placement = GetPreviewHostPlacement();
        if (!force && _lastPreviewPlacement == placement)
        {
            return;
        }

        _lastPreviewPlacement = placement;
        ViewModel.UpdatePreviewHostPlacement(placement);
    }

    private VideoPreviewHostPlacement GetPreviewHostPlacement()
    {
        if (!_isControlLoaded || ViewModel is null || PreviewViewport.XamlRoot is null)
        {
            return new VideoPreviewHostPlacement(0, 0, 0, 0, false);
        }

        var bounds = PreviewViewport.TransformToVisual(null).TransformBounds(
            new Rect(0, 0, PreviewViewport.ActualWidth, PreviewViewport.ActualHeight));
        var scale = PreviewViewport.XamlRoot.RasterizationScale;
        var width = Math.Max(0, (int)Math.Round(bounds.Width * scale));
        var height = Math.Max(0, (int)Math.Round(bounds.Height * scale));
        return new VideoPreviewHostPlacement(
            (int)Math.Round(bounds.X * scale),
            (int)Math.Round(bounds.Y * scale),
            width,
            height,
            ViewModel.HasInput && ViewModel.IsPreviewReady && width > 0 && height > 0);
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
    }

    private void StopPlaybackAtSelectionEnd()
    {
        if (ViewModel is null)
        {
            return;
        }

        _resumePlaybackAfterScrub = false;
        StopScrubPreviewTimer();
        PausePlayback(syncTimelinePosition: false, seekPosition: GetSelectionEnd());
    }

    private void UpdateVolumeToolTip()
    {
        _volumeToolTip.Content = ViewModel?.VolumeToolTipText ?? "音量";
    }
}
