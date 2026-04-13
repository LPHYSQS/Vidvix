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
    private bool _isScrubPreviewUpdateInProgress;
    private bool _isStopAtSelectionEndInProgress;
    private bool _isTimelineCommitInProgress;
    private bool _isUpdatingTimeline;
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

    private async void OnPlayPauseClick(object sender, RoutedEventArgs e)
    {
        if (ViewModel is null || !ViewModel.CanPlayPreview)
        {
            return;
        }

        await ViewModel.TogglePreviewPlaybackAsync();
    }

    private async void OnJumpToSelectionStartClick(object sender, RoutedEventArgs e)
    {
        if (ViewModel is null)
        {
            return;
        }

        await ViewModel.JumpToSelectionBoundaryAsync(GetSelectionStart());
    }

    private async void OnJumpToSelectionEndClick(object sender, RoutedEventArgs e)
    {
        if (ViewModel is null)
        {
            return;
        }

        await ViewModel.JumpToSelectionBoundaryAsync(GetSelectionEnd());
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

    private async void OnTimelineSliderPointerPressed(object sender, PointerRoutedEventArgs e)
    {
        _hasPendingScrubPreviewPosition = false;
        StartScrubPreviewTimer();

        if (ViewModel is null)
        {
            return;
        }

        await ViewModel.BeginTimelineDragAsync();
        StopPositionTimer();
    }

    private void OnTimelineSliderPointerReleased(object sender, PointerRoutedEventArgs e)
    {
        _ = CommitTimelinePositionAsync();
    }

    private void OnTimelineSliderPointerCaptureLost(object sender, PointerRoutedEventArgs e)
    {
        _ = CommitTimelinePositionAsync();
    }

    private void OnTimelineSliderValueChanged(object sender, RangeBaseValueChangedEventArgs e)
    {
        if (ViewModel is null || _isUpdatingTimeline || !ViewModel.IsDragging)
        {
            return;
        }

        _isUpdatingTimeline = true;
        try
        {
            var target = TimeSpan.FromMilliseconds(e.NewValue);
            ViewModel.UpdateDraggingPosition(target);
            _pendingScrubPreviewPosition = Clamp(target, GetSelectionStart(), GetSelectionEnd());
            _hasPendingScrubPreviewPosition = true;
        }
        finally
        {
            _isUpdatingTimeline = false;
        }
    }

    private async void OnRangeSelectorSelectionChanged(object sender, EventArgs e)
    {
        if (ViewModel is null)
        {
            return;
        }

        await ViewModel.HandleSelectionRangeChangedAsync();
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

        if (e.PropertyName == nameof(VideoTrimWorkspaceViewModel.IsPlaying))
        {
            if (ViewModel.IsPlaying)
            {
                StartPositionTimer();
                return;
            }

            StopPositionTimer();
            UpdateTimelinePosition(TimeSpan.FromMilliseconds(ViewModel.CurrentPositionMilliseconds));
        }
    }

    private async Task ReloadSourceAsync()
    {
        _sourceVersion++;
        var version = _sourceVersion;
        StopScrubPreviewTimer();
        StopPositionTimer();

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

        var current = TimeSpan.FromMilliseconds(ViewModel.CurrentPositionMilliseconds);
        var selectionStart = GetSelectionStart();
        var selectionEnd = GetSelectionEnd();
        if (ViewModel.IsPlaying && selectionEnd > selectionStart && current >= selectionEnd)
        {
            StopPositionTimer();
            _ = StopPlaybackAtSelectionEndAsync();
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

    private async void OnScrubPreviewTimerTick(DispatcherQueueTimer sender, object args)
    {
        if (ViewModel is null ||
            !_hasPendingScrubPreviewPosition ||
            !ViewModel.HasLoadedPreview ||
            _isScrubPreviewUpdateInProgress)
        {
            return;
        }

        _isScrubPreviewUpdateInProgress = true;
        _hasPendingScrubPreviewPosition = false;
        try
        {
            await ViewModel.PreviewScrubAsync(_pendingScrubPreviewPosition);
        }
        finally
        {
            _isScrubPreviewUpdateInProgress = false;
        }
    }

    private async Task CommitTimelinePositionAsync()
    {
        StopScrubPreviewTimer();
        if (ViewModel is null || _isTimelineCommitInProgress)
        {
            return;
        }

        _isTimelineCommitInProgress = true;
        var target = Clamp(TimeSpan.FromMilliseconds(ViewModel.CurrentPositionMilliseconds), GetSelectionStart(), GetSelectionEnd());
        try
        {
            await ViewModel.CompleteTimelineDragAsync(target);
        }
        finally
        {
            _isTimelineCommitInProgress = false;
        }
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
        if (ViewModel is null || ViewModel.IsDragging)
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

    private async Task StopPlaybackAtSelectionEndAsync()
    {
        if (ViewModel is null || _isStopAtSelectionEndInProgress)
        {
            return;
        }

        _isStopAtSelectionEndInProgress = true;
        try
        {
            StopScrubPreviewTimer();
            await ViewModel.StopPlaybackAtSelectionEndAsync();
        }
        finally
        {
            _isStopAtSelectionEndInProgress = false;
        }
    }

    private void UpdateVolumeToolTip()
    {
        _volumeToolTip.Content = ViewModel?.VolumeToolTipText ?? "音量";
    }
}
