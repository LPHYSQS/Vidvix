using System;
using System.ComponentModel;
using System.Threading.Tasks;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;
using Microsoft.UI.Xaml.Input;
using Vidvix.Views.Converters;
using Vidvix.Core.Models;
using Vidvix.ViewModels;
using Windows.Foundation;
using VirtualKey = Windows.System.VirtualKey;

namespace Vidvix.Views.Controls;

public sealed partial class VideoTrimWorkspaceView : UserControl
{
    private static readonly TimeSpan ScrubPreviewInterval = TimeSpan.FromMilliseconds(90);
    private const double TimelineScrubActivationThreshold = 4d;

    private readonly DispatcherQueue _dispatcherQueue;
    private readonly DispatcherQueueTimer _positionTimer;
    private readonly DispatcherQueueTimer _scrubPreviewTimer;
    private readonly ToolTip _volumeToolTip;
    private readonly long _visibilityPropertyChangedCallbackToken;
    private XamlRoot? _previewViewportXamlRoot;
    private bool _hasPendingScrubPreviewPosition;
    private bool _isControlLoaded;
    private bool _isTimelineDragActivationInProgress;
    private bool _isTimelineDragActivated;
    private bool _isPositionTimerRunning;
    private bool _isScrubPreviewUpdateInProgress;
    private bool _isStopAtSelectionEndInProgress;
    private bool _isTimelinePointerPressed;
    private bool _isTimelineScrubPreviewEnabled;
    private bool _isTimelineCommitInProgress;
    private bool _isUpdatingTimeline;
    private int _sourceVersion;
    private TimeSpan _pendingScrubPreviewPosition;
    private TimeSpan _timelineInteractionTarget;
    private Task? _timelineDragActivationTask;
    private Point _timelinePointerPressedPoint;
    private VideoPreviewHostPlacement? _lastPreviewPlacement;

    public VideoTrimWorkspaceView()
    {
        InitializeComponent();
        if (Resources.TryGetValue("TimelineMillisecondsToTimeConverter", out var converterResource) &&
            converterResource is TimelineMillisecondsToTimeConverter timelineConverter)
        {
            timelineConverter.Formatter = position => ViewModel?.FormatTimelineThumbToolTip(position) ??
                                                    TimelineMillisecondsToTimeConverter.FormatFullTime(position);
        }

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
        SizeChanged += OnControlSizeChanged;
        PreviewViewport.SizeChanged += OnPreviewViewportSizeChanged;
        Loaded += OnLoaded;
        Unloaded += OnUnloaded;
        _visibilityPropertyChangedCallbackToken = RegisterPropertyChangedCallback(VisibilityProperty, OnVisibilityPropertyChanged);
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
        if (!control._isControlLoaded)
        {
            return;
        }

        _ = control.ActivatePreviewSurfaceAsync(control.ViewModel?.HasInput == true);
    }

    private void RegisterTimelineInteractionHandlers()
    {
        TimelineSlider.AddHandler(PointerPressedEvent, new PointerEventHandler(OnTimelineSliderPointerPressed), true);
        TimelineSlider.AddHandler(PointerMovedEvent, new PointerEventHandler(OnTimelineSliderPointerMoved), true);
        TimelineSlider.AddHandler(PointerReleasedEvent, new PointerEventHandler(OnTimelineSliderPointerReleased), true);
        TimelineSlider.AddHandler(PointerCaptureLostEvent, new PointerEventHandler(OnTimelineSliderPointerCaptureLost), true);
    }

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        _isControlLoaded = true;
        AttachPreviewViewportXamlRootChangedHandler();
        UpdateVolumeToolTip();
        _dispatcherQueue.TryEnqueue(() =>
        {
            if (!_isControlLoaded)
            {
                return;
            }

            _ = ActivatePreviewSurfaceAsync(ViewModel?.HasInput == true);
        });
    }

    private void OnUnloaded(object sender, RoutedEventArgs e)
    {
        _isControlLoaded = false;
        ViewModel?.EndTimelineInteractionPriority();
        ResetTimelinePointerInteraction();
        DetachPreviewViewportXamlRootChangedHandler();
        _lastPreviewPlacement = null;
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

        AttachPreviewViewportXamlRootChangedHandler();
        UpdatePreviewHostPlacement();
    }

    private void OnControlSizeChanged(object sender, SizeChangedEventArgs e)
    {
        RefreshPreviewHost(force: true);
    }

    private void OnPreviewViewportSizeChanged(object sender, SizeChangedEventArgs e)
    {
        RefreshPreviewHost(force: true);
    }

    private void OnVisibilityPropertyChanged(DependencyObject sender, DependencyProperty dp)
    {
        if (!_isControlLoaded || ViewModel is null)
        {
            return;
        }

        RefreshPreviewHost(force: true);
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

        ClearSelectionInputFocus(sender as Control);
        await ViewModel.JumpToSelectionBoundaryAsync(GetSelectionStart());
        ClearSelectionInputFocus(sender as Control);
    }

    private async void OnJumpToSelectionEndClick(object sender, RoutedEventArgs e)
    {
        if (ViewModel is null)
        {
            return;
        }

        ClearSelectionInputFocus(sender as Control);
        await ViewModel.JumpToSelectionBoundaryAsync(GetSelectionEnd());
        ClearSelectionInputFocus(sender as Control);
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
        StopPositionTimer();
        StopScrubPreviewTimer();
        _hasPendingScrubPreviewPosition = false;
        _isTimelinePointerPressed = true;
        _isTimelineDragActivated = false;
        _isTimelineScrubPreviewEnabled = false;
        _timelineDragActivationTask = null;
        _timelinePointerPressedPoint = e.GetCurrentPoint(TimelineSlider).Position;
        _timelineInteractionTarget = GetTimelineInteractionTarget();
        ViewModel?.BeginTimelineInteractionPriority();
    }

    private async void OnTimelineSliderPointerMoved(object sender, PointerRoutedEventArgs e)
    {
        if (!_isTimelinePointerPressed || _isTimelineScrubPreviewEnabled || ViewModel is null)
        {
            return;
        }

        var currentPoint = e.GetCurrentPoint(TimelineSlider).Position;
        var deltaX = currentPoint.X - _timelinePointerPressedPoint.X;
        var deltaY = currentPoint.Y - _timelinePointerPressedPoint.Y;
        if (Math.Abs(deltaX) < TimelineScrubActivationThreshold &&
            Math.Abs(deltaY) < TimelineScrubActivationThreshold)
        {
            return;
        }

        _timelineDragActivationTask ??= EnsureTimelineDragActivatedAsync();
        await _timelineDragActivationTask;
        if (!_isTimelineDragActivated || !ViewModel.IsDragging)
        {
            return;
        }

        _isTimelineScrubPreviewEnabled = true;
        StartScrubPreviewTimer();
    }

    private void OnTimelineSliderPointerReleased(object sender, PointerRoutedEventArgs e)
    {
        _ = FinalizeTimelineInteractionAsync();
    }

    private void OnTimelineSliderPointerCaptureLost(object sender, PointerRoutedEventArgs e)
    {
        _ = FinalizeTimelineInteractionAsync();
    }

    private void OnTimelineSliderValueChanged(object sender, RangeBaseValueChangedEventArgs e)
    {
        if (ViewModel is null || _isUpdatingTimeline || !_isTimelinePointerPressed)
        {
            return;
        }

        _isUpdatingTimeline = true;
        try
        {
            var target = TimeSpan.FromMilliseconds(e.NewValue);
            _timelineInteractionTarget = Clamp(target, GetSelectionStart(), GetSelectionEnd());

            if (!_isTimelineDragActivated || !ViewModel.IsDragging)
            {
                return;
            }

            ViewModel.UpdateDraggingPosition(target);
            _pendingScrubPreviewPosition = _timelineInteractionTarget;
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

        if (e.PropertyName == nameof(VideoTrimWorkspaceViewModel.VolumeToolTipText))
        {
            UpdateVolumeToolTip();
            return;
        }

        if (!_isControlLoaded)
        {
            return;
        }

        if (e.PropertyName == nameof(VideoTrimWorkspaceViewModel.InputPath))
        {
            _ = ActivatePreviewSurfaceAsync(reloadSource: true);
            return;
        }

        if (e.PropertyName == nameof(VideoTrimWorkspaceViewModel.IsPreviewReady) ||
            e.PropertyName == nameof(VideoTrimWorkspaceViewModel.EditorVisibility))
        {
            PreparePreviewViewportLayout();
            UpdatePreviewHostPlacement(force: true);
            _ = RefreshPreviewRenderingAsync();
            return;
        }

        if (e.PropertyName == nameof(VideoTrimWorkspaceViewModel.IsPlaying))
        {
            if (ViewModel.IsPlaying)
            {
                StartPositionTimer();
                _ = RefreshPreviewRenderingAsync();
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

        PreparePreviewViewportLayout();
        UpdatePreviewHostPlacement(force: true);

        try
        {
            await ViewModel.ReloadPreviewAsync().ConfigureAwait(true);
        }
        catch (Exception)
        {
            if (ViewModel is not null && version == _sourceVersion)
            {
                ViewModel.SetPreviewUnavailable();
            }
        }
        finally
        {
            if (version == _sourceVersion)
            {
                UpdatePreviewHostPlacement(force: true);
                await RefreshPreviewRenderingAsync().ConfigureAwait(true);
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

    private async Task EnsureTimelineDragActivatedAsync()
    {
        if (ViewModel is null || _isTimelineDragActivated || _isTimelineDragActivationInProgress)
        {
            return;
        }

        _isTimelineDragActivationInProgress = true;
        try
        {
            _timelineInteractionTarget = GetTimelineInteractionTarget();
            await ViewModel.BeginTimelineDragAsync();
            _isTimelineDragActivated = ViewModel.IsDragging;
            if (!_isTimelineDragActivated)
            {
                RestorePositionTimerIfNeeded();
                return;
            }

            ViewModel.UpdateDraggingPosition(_timelineInteractionTarget);
            _pendingScrubPreviewPosition = _timelineInteractionTarget;
            _hasPendingScrubPreviewPosition = true;
        }
        finally
        {
            _isTimelineDragActivationInProgress = false;
        }
    }

    private async Task FinalizeTimelineInteractionAsync()
    {
        if (!_isTimelinePointerPressed)
        {
            return;
        }

        var target = _timelineInteractionTarget;
        if (_timelineDragActivationTask is not null)
        {
            await _timelineDragActivationTask;
        }

        var shouldCommitDrag = _isTimelineDragActivated;
        ResetTimelinePointerInteraction();
        StopScrubPreviewTimer();

        try
        {
            if (ViewModel is null)
            {
                return;
            }

            if (shouldCommitDrag)
            {
                await CommitTimelinePositionAsync(target);
                return;
            }

            await ViewModel.JumpTimelinePositionAsync(target);
        }
        finally
        {
            ViewModel?.EndTimelineInteractionPriority();
            RestorePositionTimerIfNeeded();
        }
    }

    private async Task CommitTimelinePositionAsync(TimeSpan target)
    {
        StopScrubPreviewTimer();
        if (ViewModel is null || _isTimelineCommitInProgress)
        {
            return;
        }

        _isTimelineCommitInProgress = true;
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

    private void ResetTimelinePointerInteraction()
    {
        _isTimelinePointerPressed = false;
        _isTimelineDragActivated = false;
        _isTimelineScrubPreviewEnabled = false;
    }

    private void UpdateTimelinePosition(TimeSpan position)
    {
        if (ViewModel is null || ViewModel.IsDragging || _isTimelinePointerPressed)
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
        if (!_isControlLoaded ||
            ViewModel is null ||
            PreviewViewport.XamlRoot is null ||
            Visibility != Visibility.Visible ||
            PreviewViewport.Visibility != Visibility.Visible)
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

    private TimeSpan GetTimelineInteractionTarget() =>
        Clamp(TimeSpan.FromMilliseconds(TimelineSlider.Value), GetSelectionStart(), GetSelectionEnd());

    private void RestorePositionTimerIfNeeded()
    {
        if (ViewModel?.IsPlaying == true)
        {
            StartPositionTimer();
        }
    }

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
        _volumeToolTip.Content = ViewModel?.VolumeToolTipText ?? ViewModel?.VolumeTitleText ?? string.Empty;
    }

    private void AttachPreviewViewportXamlRootChangedHandler()
    {
        var currentXamlRoot = PreviewViewport.XamlRoot;
        if (ReferenceEquals(_previewViewportXamlRoot, currentXamlRoot))
        {
            return;
        }

        if (_previewViewportXamlRoot is not null)
        {
            _previewViewportXamlRoot.Changed -= OnPreviewViewportXamlRootChanged;
        }

        _previewViewportXamlRoot = currentXamlRoot;
        if (_previewViewportXamlRoot is not null)
        {
            _previewViewportXamlRoot.Changed += OnPreviewViewportXamlRootChanged;
        }
    }

    private void DetachPreviewViewportXamlRootChangedHandler()
    {
        if (_previewViewportXamlRoot is null)
        {
            return;
        }

        _previewViewportXamlRoot.Changed -= OnPreviewViewportXamlRootChanged;
        _previewViewportXamlRoot = null;
    }

    private void OnPreviewViewportXamlRootChanged(XamlRoot sender, XamlRootChangedEventArgs args)
    {
        RefreshPreviewHost(force: true);
    }

    private void ClearSelectionInputFocus(Control? focusTarget)
    {
        ViewModel?.CommitSelectionStartInput();
        ViewModel?.CommitSelectionEndInput();
        focusTarget?.Focus(FocusState.Programmatic);
    }

    private async Task ActivatePreviewSurfaceAsync(bool reloadSource)
    {
        if (!_isControlLoaded || ViewModel is null)
        {
            return;
        }

        PreparePreviewViewportLayout();
        UpdatePreviewHostPlacement(force: true);

        if (reloadSource)
        {
            await ReloadSourceAsync().ConfigureAwait(true);
            return;
        }

        try
        {
            await ViewModel.EnsurePreviewHostReadyAsync().ConfigureAwait(true);
            await RefreshPreviewRenderingAsync().ConfigureAwait(true);
        }
        catch
        {
        }
    }

    private void PreparePreviewViewportLayout()
    {
        if (!_isControlLoaded)
        {
            return;
        }

        UpdateLayout();
        PreviewViewport.UpdateLayout();
    }

    private void RefreshPreviewHost(bool force = false, bool requestRedraw = true)
    {
        if (!_isControlLoaded || ViewModel is null)
        {
            return;
        }

        UpdatePreviewHostPlacement(force);
        if (requestRedraw)
        {
            _ = RefreshPreviewRenderingAsync();
        }
    }

    private async Task RefreshPreviewRenderingAsync()
    {
        if (!_isControlLoaded || ViewModel is null)
        {
            return;
        }

        await ViewModel.RefreshPreviewRenderingAsync().ConfigureAwait(true);
    }
}
