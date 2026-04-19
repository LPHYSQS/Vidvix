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
using Vidvix.Views.Converters;
using Windows.Foundation;

namespace Vidvix.Views;

public sealed partial class SplitAudioPage : Page
{
    private static readonly TimeSpan ScrubPreviewInterval = TimeSpan.FromMilliseconds(90);
    private const double TimelineScrubActivationThreshold = 4d;

    private readonly DispatcherQueue _dispatcherQueue;
    private readonly DispatcherQueueTimer _scrubPreviewTimer;
    private readonly long _visibilityPropertyChangedCallbackToken;
    private bool _hasPendingScrubPreviewPosition;
    private bool _isControlLoaded;
    private bool _isTimelineDragActivationInProgress;
    private bool _isTimelineDragActivated;
    private bool _isScrubPreviewUpdateInProgress;
    private bool _isTimelinePointerPressed;
    private bool _isTimelineScrubPreviewEnabled;
    private bool _isTimelineCommitInProgress;
    private bool _isUpdatingTimeline;
    private int _sourceVersion;
    private VideoPreviewHostPlacement? _lastPreviewPlacement;
    private TimeSpan _pendingScrubPreviewPosition;
    private TimeSpan _timelineInteractionTarget;
    private Task? _timelineDragActivationTask;
    private Point _timelinePointerPressedPoint;

    public SplitAudioPage()
    {
        InitializeComponent();
        if (Resources.TryGetValue("TimelineMillisecondsToTimeConverter", out var converterResource) &&
            converterResource is TimelineMillisecondsToTimeConverter timelineConverter)
        {
            timelineConverter.Formatter = position => ViewModel?.FormatTimelineThumbToolTip(position) ??
                                                    TimelineMillisecondsToTimeConverter.FormatFullTime(position);
        }

        _dispatcherQueue = DispatcherQueue.GetForCurrentThread()
            ?? throw new InvalidOperationException("当前线程缺少可用的界面调度队列。");

        _scrubPreviewTimer = _dispatcherQueue.CreateTimer();
        _scrubPreviewTimer.Interval = ScrubPreviewInterval;
        _scrubPreviewTimer.IsRepeating = true;
        _scrubPreviewTimer.Tick += OnScrubPreviewTimerTick;

        RegisterTimelineInteractionHandlers();
        LayoutUpdated += OnLayoutUpdated;
        SizeChanged += OnPageSizeChanged;
        PreviewHostAnchor.SizeChanged += OnPreviewHostAnchorSizeChanged;
        Loaded += OnLoaded;
        Unloaded += OnUnloaded;
        _visibilityPropertyChangedCallbackToken = RegisterPropertyChangedCallback(VisibilityProperty, OnVisibilityPropertyChanged);
    }

    public SplitAudioWorkspaceViewModel ViewModel
    {
        get => (SplitAudioWorkspaceViewModel)GetValue(ViewModelProperty);
        set => SetValue(ViewModelProperty, value);
    }

    public static readonly DependencyProperty ViewModelProperty = DependencyProperty.Register(
        nameof(ViewModel),
        typeof(SplitAudioWorkspaceViewModel),
        typeof(SplitAudioPage),
        new PropertyMetadata(null, OnViewModelChanged));

    private static void OnViewModelChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        var page = (SplitAudioPage)d;
        if (e.OldValue is SplitAudioWorkspaceViewModel oldViewModel)
        {
            oldViewModel.PropertyChanged -= page.OnViewModelPropertyChanged;
        }

        if (e.NewValue is SplitAudioWorkspaceViewModel newViewModel)
        {
            newViewModel.PropertyChanged += page.OnViewModelPropertyChanged;
        }

        if (!page._isControlLoaded)
        {
            return;
        }

        _ = page.ActivatePreviewAsync(reloadSource: true);
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
        _dispatcherQueue.TryEnqueue(() =>
        {
            if (!_isControlLoaded || ViewModel is null || !ViewModel.HasInput)
            {
                return;
            }

            _ = ActivatePreviewAsync(!ViewModel.HasLoadedPreview);
        });
    }

    private void OnUnloaded(object sender, RoutedEventArgs e)
    {
        _isControlLoaded = false;
        ViewModel?.EndTimelineInteractionPriority();
        ResetTimelinePointerInteraction();
        _lastPreviewPlacement = null;
        StopScrubPreviewTimer();
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

    private void OnPageSizeChanged(object sender, SizeChangedEventArgs e)
    {
        RefreshPreviewHost(force: true);
    }

    private void OnPreviewHostAnchorSizeChanged(object sender, SizeChangedEventArgs e)
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

    private void OnTimelineSliderPointerPressed(object sender, PointerRoutedEventArgs e)
    {
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
            _timelineInteractionTarget = Clamp(target, TimeSpan.Zero, GetTimelineMaximum());

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

    private void OnViewModelPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (!_isControlLoaded || ViewModel is null)
        {
            return;
        }

        if (e.PropertyName == nameof(SplitAudioWorkspaceViewModel.InputPath))
        {
            _ = ActivatePreviewAsync(reloadSource: true);
            return;
        }

        if (e.PropertyName == nameof(SplitAudioWorkspaceViewModel.IsPreviewReady))
        {
            PreparePreviewHostLayout();
            UpdatePreviewHostPlacement(force: true);
            _ = RefreshPreviewRenderingAsync();
            return;
        }

        if (e.PropertyName == nameof(SplitAudioWorkspaceViewModel.HasInput) &&
            !ViewModel.HasInput)
        {
            StopScrubPreviewTimer();
            ResetTimelinePointerInteraction();
        }
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
        }
    }

    private async Task CommitTimelinePositionAsync(TimeSpan target)
    {
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

    private TimeSpan GetTimelineInteractionTarget() =>
        ViewModel is null
            ? TimeSpan.Zero
            : Clamp(TimeSpan.FromMilliseconds(TimelineSlider.Value), TimeSpan.Zero, GetTimelineMaximum());

    private TimeSpan GetTimelineMaximum() =>
        ViewModel is null ? TimeSpan.Zero : TimeSpan.FromMilliseconds(ViewModel.TimelineMaximum);

    private static TimeSpan Clamp(TimeSpan value, TimeSpan minimum, TimeSpan maximum)
    {
        if (value < minimum)
        {
            return minimum;
        }

        return value > maximum ? maximum : value;
    }

    private async Task ActivatePreviewAsync(bool reloadSource)
    {
        if (!_isControlLoaded || ViewModel is null)
        {
            return;
        }

        PreparePreviewHostLayout();
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

    private async Task ReloadSourceAsync()
    {
        if (ViewModel is null)
        {
            return;
        }

        var version = ++_sourceVersion;
        UpdatePreviewHostPlacement(force: true);

        try
        {
            await ViewModel.ReloadPreviewAsync().ConfigureAwait(true);
        }
        catch
        {
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

    private void PreparePreviewHostLayout()
    {
        if (!_isControlLoaded)
        {
            return;
        }

        UpdateLayout();
        PreviewHostAnchor.UpdateLayout();
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
            PreviewHostAnchor.XamlRoot is null ||
            Visibility != Visibility.Visible ||
            PreviewHostAnchor.Visibility != Visibility.Visible)
        {
            return new VideoPreviewHostPlacement(0, 0, 0, 0, false);
        }

        var bounds = PreviewHostAnchor.TransformToVisual(null).TransformBounds(
            new Rect(0, 0, Math.Max(1d, PreviewHostAnchor.ActualWidth), Math.Max(1d, PreviewHostAnchor.ActualHeight)));
        var scale = PreviewHostAnchor.XamlRoot.RasterizationScale;
        var width = Math.Max(1, (int)Math.Round(bounds.Width * scale));
        var height = Math.Max(1, (int)Math.Round(bounds.Height * scale));
        return new VideoPreviewHostPlacement(
            (int)Math.Round(bounds.X * scale),
            (int)Math.Round(bounds.Y * scale),
            width,
            height,
            true);
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
