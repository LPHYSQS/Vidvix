using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.UI.Xaml.Controls;
using Vidvix.Core.Interfaces;
using Vidvix.Core.Models;
using Vidvix.Utils;

namespace Vidvix.ViewModels;

public sealed partial class SplitAudioWorkspaceViewModel
{
    private const double DefaultSplitAudioPreviewVolume = 0.8d;

    private readonly IVideoPreviewService _videoPreviewService;
    private readonly object _previewLoadSyncRoot = new();
    private readonly SemaphoreSlim _previewInteractionSemaphore = new(1, 1);
    private Task<bool>? _previewLoadTask;
    private TimeSpan _previewDuration;
    private TimeSpan _currentPreviewPosition;
    private bool _isPreviewReady;
    private bool _isPlaying;
    private bool _isSeeking;
    private bool _isDragging;
    private bool _resumePlaybackAfterDragging;
    private bool _isTimelineInteractionPending;

    public bool IsPreviewReady
    {
        get => _isPreviewReady;
        private set
        {
            if (SetProperty(ref _isPreviewReady, value))
            {
                OnPropertyChanged(nameof(CanPlayPreview));
            }
        }
    }

    public bool IsPlaying
    {
        get => _isPlaying;
        private set
        {
            if (SetProperty(ref _isPlaying, value))
            {
                OnPropertyChanged(nameof(PlayPauseButtonText));
                OnPropertyChanged(nameof(PlayPauseButtonSymbol));
            }
        }
    }

    public bool CanPlayPreview => HasInput && !IsBusy && !IsSeeking && _previewDuration > TimeSpan.Zero;

    public string PlayPauseButtonText => IsPlaying ? PausePreviewButtonText : PlayPreviewButtonText;

    public Symbol PlayPauseButtonSymbol => IsPlaying ? Symbol.Pause : Symbol.Play;

    public double TimelineMinimum => 0d;

    public double TimelineMaximum => Math.Max(1d, _previewDuration.TotalMilliseconds);

    public double CurrentPositionMilliseconds
    {
        get => _currentPreviewPosition.TotalMilliseconds;
        set => SyncCurrentPosition(TimeSpan.FromMilliseconds(value));
    }

    public string TimelinePositionText => $"{FormatCompactPreviewTime(_currentPreviewPosition)} / {FormatCompactPreviewTime(_previewDuration)}";

    public bool IsSeeking => _isSeeking;

    public bool IsDragging => _isDragging;

    internal bool HasLoadedPreview => _videoPreviewService.HasLoadedMedia;

    private void InitializePreview()
    {
        SplitAudioPlaybackCoordinator.Register(this);
        _videoPreviewService.MediaOpened += OnPreviewMediaOpened;
        _videoPreviewService.MediaFailed += OnPreviewMediaFailed;
        _videoPreviewService.PositionChanged += OnPreviewPositionChanged;
        _videoPreviewService.PlaybackStateChanged += OnPreviewPlaybackStateChanged;
        _videoPreviewService.MediaEnded += OnPreviewMediaEnded;
        _ = _videoPreviewService.SetVolumeAsync(DefaultSplitAudioPreviewVolume);
    }

    internal Task EnsurePreviewHostReadyAsync(CancellationToken cancellationToken = default) =>
        _videoPreviewService.InitializeAsync(cancellationToken);

    internal void UpdatePreviewHostPlacement(VideoPreviewHostPlacement placement) =>
        _videoPreviewService.UpdateHostPlacement(placement with { IsVisible = false });

    internal Task RefreshPreviewRenderingAsync(CancellationToken cancellationToken = default) =>
        _videoPreviewService.RefreshAsync(cancellationToken);

    internal Task ReloadPreviewAsync(CancellationToken cancellationToken = default) =>
        StartPreviewLoadAsync(forceReload: true, cancellationToken);

    private Task<bool> StartPreviewLoadAsync(bool forceReload, CancellationToken cancellationToken)
    {
        var inputPath = InputPath;
        if (!HasInput || string.IsNullOrWhiteSpace(inputPath) || !File.Exists(inputPath))
        {
            return ReloadPreviewCoreAsync(cancellationToken);
        }

        Task<bool> loadTask;
        lock (_previewLoadSyncRoot)
        {
            if (!forceReload && _previewLoadTask is not null)
            {
                loadTask = _previewLoadTask;
            }
            else
            {
                loadTask = ReloadPreviewCoreAsync(cancellationToken);
                _previewLoadTask = loadTask;
            }
        }

        return AwaitPreviewLoadAsync(loadTask);
    }

    private async Task<bool> AwaitPreviewLoadAsync(Task<bool> loadTask)
    {
        try
        {
            return await loadTask;
        }
        finally
        {
            lock (_previewLoadSyncRoot)
            {
                if (ReferenceEquals(_previewLoadTask, loadTask))
                {
                    _previewLoadTask = null;
                }
            }
        }
    }

    private async Task<bool> ReloadPreviewCoreAsync(CancellationToken cancellationToken)
    {
        var inputPath = InputPath;
        if (!HasInput || string.IsNullOrWhiteSpace(inputPath) || !File.Exists(inputPath))
        {
            ResetPreviewState();
            await _videoPreviewService.UnloadAsync(cancellationToken);
            return false;
        }

        ResetPreviewState(clearDuration: false);

        try
        {
            await _videoPreviewService.InitializeAsync(cancellationToken);
            await _videoPreviewService
                .LoadAsync(
                    inputPath,
                    DefaultSplitAudioPreviewVolume,
                    enableExternalSubtitleAutoLoad: false,
                    cancellationToken);
            return HasLoadedPreview || IsPreviewReady;
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            return false;
        }
        catch (Exception exception)
        {
            _logger.Log(LogLevel.Error, "Failed to load split-audio preview.", exception);
            RunPreviewOnUiThread(() =>
            {
                ResetPreviewState();
                if (string.IsNullOrWhiteSpace(InputFileName))
                {
                    SetStatusMessage(
                        "splitAudio.preview.unavailable.generic",
                        "\u5f53\u524d\u6587\u4ef6\u9884\u89c8\u4e0d\u53ef\u7528\uff0c\u4ecd\u53ef\u4ee5\u7ee7\u7eed\u62c6\u97f3\u3002");
                    return;
                }

                SetStatusMessage(
                    () => FormatLocalizedText(
                        "splitAudio.preview.unavailable.withFileName",
                        "\u5df2\u5bfc\u5165 {fileName}\uff0c\u4f46\u9884\u89c8\u6682\u4e0d\u53ef\u7528\uff0c\u4ecd\u53ef\u4ee5\u7ee7\u7eed\u62c6\u97f3\u3002",
                        ("fileName", InputFileName)));
            });
            return false;
        }
    }

    private async Task PrimePreviewTimelineAsync(string inputPath)
    {
        try
        {
            if (_mediaInfoService.TryGetCachedDetails(inputPath, out var cachedSnapshot))
            {
                RunPreviewOnUiThread(() => ApplyPlayableDuration(cachedSnapshot.MediaDuration ?? TimeSpan.Zero));
                return;
            }

            var detailsResult = await _mediaInfoService.GetMediaDetailsAsync(inputPath);
            if (detailsResult.IsSuccess)
            {
                var duration = detailsResult.Snapshot?.MediaDuration ?? TimeSpan.Zero;
                RunPreviewOnUiThread(() => ApplyPlayableDuration(duration));
            }
        }
        catch (Exception exception)
        {
            _logger.Log(LogLevel.Warning, "Failed to prime split-audio preview timeline.", exception);
        }
    }

    internal async Task TogglePreviewPlaybackAsync()
    {
        await _previewInteractionSemaphore.WaitAsync();
        try
        {
            if (IsSeeking || IsDragging)
            {
                return;
            }

            if (!await EnsurePreviewLoadedAsync())
            {
                return;
            }

            if (IsPlaying)
            {
                await PausePreviewAsync();
                return;
            }

            var target = ClampToDuration(_currentPreviewPosition);
            if (_previewDuration > TimeSpan.Zero && target >= _previewDuration)
            {
                target = TimeSpan.Zero;
            }

            var previewPosition = ClampToDuration(_videoPreviewService.CurrentPosition);
            var actual = AreClose(previewPosition, target)
                ? previewPosition
                : await _videoPreviewService.SetPlaybackPositionAsync(target);

            SyncCurrentPosition(actual);
            await SplitAudioPlaybackCoordinator.RequestPlaybackAsync(this);
            await _videoPreviewService.PlayAsync();
            SetPlaying(true);
        }
        finally
        {
            _previewInteractionSemaphore.Release();
        }
    }

    internal async Task PausePreviewForDeactivationAsync()
    {
        await PausePreviewForPlaybackCoordinationAsync();
        await SplitAudioPlaybackCoordinator.PauseAllExceptAsync(this);
        SplitAudioPlaybackCoordinator.NotifyPaused(this);
    }

    private async Task PausePreviewForPlaybackCoordinationAsync()
    {
        await _previewInteractionSemaphore.WaitAsync();
        try
        {
            _resumePlaybackAfterDragging = false;
            _isTimelineInteractionPending = false;
            SetDragging(false);

            if (!HasLoadedPreview)
            {
                SetPlaying(false);
                SplitAudioPlaybackCoordinator.NotifyPaused(this);
                return;
            }

            if (IsPlaying || _videoPreviewService.IsPlaying)
            {
                await PausePreviewAsync();
                return;
            }

            SyncCurrentPosition(ClampToDuration(_videoPreviewService.CurrentPosition));
            SetPlaying(false);
            SplitAudioPlaybackCoordinator.NotifyPaused(this);
        }
        finally
        {
            _previewInteractionSemaphore.Release();
        }
    }

    internal void BeginTimelineInteractionPriority() => _isTimelineInteractionPending = true;

    internal void EndTimelineInteractionPriority() => _isTimelineInteractionPending = false;

    internal async Task BeginTimelineDragAsync()
    {
        await _previewInteractionSemaphore.WaitAsync();
        try
        {
            if (_isDragging || IsSeeking)
            {
                return;
            }

            _resumePlaybackAfterDragging = IsPlaying;
            SetDragging(true);

            if (_resumePlaybackAfterDragging && HasLoadedPreview)
            {
                await PausePreviewAsync();
            }
        }
        finally
        {
            _previewInteractionSemaphore.Release();
        }
    }

    internal void UpdateDraggingPosition(TimeSpan position)
    {
        if (!HasInput)
        {
            return;
        }

        SyncCurrentPosition(ClampToDuration(position));
    }

    internal async Task PreviewScrubAsync(TimeSpan position)
    {
        if (IsSeeking)
        {
            return;
        }

        if (!await EnsurePreviewLoadedAsync())
        {
            return;
        }

        var actual = await _videoPreviewService
            .SetPlaybackPositionAsync(ClampToDuration(position))
            ;
        SyncCurrentPosition(actual);
    }

    internal async Task CompleteTimelineDragAsync(TimeSpan position)
    {
        await _previewInteractionSemaphore.WaitAsync();
        try
        {
            if (!_isDragging && !_resumePlaybackAfterDragging)
            {
                return;
            }

            var target = ClampToDuration(position);
            var shouldResumePlayback = _resumePlaybackAfterDragging;
            _resumePlaybackAfterDragging = false;
            SetDragging(false);

            await SetPreviewPositionAsync(target, shouldResumePlayback);
        }
        finally
        {
            _previewInteractionSemaphore.Release();
        }
    }

    internal async Task JumpTimelinePositionAsync(TimeSpan position)
    {
        await _previewInteractionSemaphore.WaitAsync();
        try
        {
            await SetPreviewPositionAsync(position, IsPlaying);
        }
        finally
        {
            _previewInteractionSemaphore.Release();
        }
    }

    internal string FormatTimelineThumbToolTip(TimeSpan position) => FormatPreviewTime(position);

    private async Task<TimeSpan> PausePreviewAsync()
    {
        var position = HasLoadedPreview
            ? await _videoPreviewService.PauseAsync()
            : ClampToDuration(_currentPreviewPosition);

        SyncCurrentPosition(position);
        SetPlaying(false);
        SplitAudioPlaybackCoordinator.NotifyPaused(this);
        return position;
    }

    private async Task<TimeSpan> SetPreviewPositionAsync(TimeSpan position, bool shouldBePlayingAfterPositioning)
    {
        var target = ClampToDuration(position);
        SyncCurrentPosition(target);

        if (!await EnsurePreviewLoadedAsync())
        {
            SetPlaying(false);
            return target;
        }

        var actual = target;
        SetSeeking(true);
        try
        {
            actual = await _videoPreviewService.SetPlaybackPositionAsync(target);
            SyncCurrentPosition(actual);

            if (shouldBePlayingAfterPositioning && !_videoPreviewService.IsPlaying)
            {
                await SplitAudioPlaybackCoordinator.RequestPlaybackAsync(this);
                await _videoPreviewService.PlayAsync();
            }
        }
        finally
        {
            SetSeeking(false);
        }

        SetPlaying(shouldBePlayingAfterPositioning);
        return actual;
    }

    private async Task<bool> EnsurePreviewLoadedAsync(CancellationToken cancellationToken = default)
    {
        if (HasLoadedPreview)
        {
            return true;
        }

        var inputPath = InputPath;
        if (!HasInput || string.IsNullOrWhiteSpace(inputPath) || !File.Exists(inputPath))
        {
            return false;
        }

        return await StartPreviewLoadAsync(forceReload: false, cancellationToken);
    }

    private void DisposePreview()
    {
        SplitAudioPlaybackCoordinator.Unregister(this);
        _videoPreviewService.MediaOpened -= OnPreviewMediaOpened;
        _videoPreviewService.MediaFailed -= OnPreviewMediaFailed;
        _videoPreviewService.PositionChanged -= OnPreviewPositionChanged;
        _videoPreviewService.PlaybackStateChanged -= OnPreviewPlaybackStateChanged;
        _videoPreviewService.MediaEnded -= OnPreviewMediaEnded;
        _previewInteractionSemaphore.Dispose();
        _videoPreviewService.Dispose();
    }

    private void OnPreviewMediaOpened(object? sender, VideoPreviewMediaOpenedEventArgs e)
    {
        RunPreviewOnUiThread(() =>
        {
            if (!IsCurrentPreviewSource(e.SourcePath))
            {
                return;
            }

            ApplyPlayableDuration(e.Duration);
            SyncCurrentPosition(ClampToDuration(_videoPreviewService.CurrentPosition));
            IsPreviewReady = true;
            SetPlaying(false);
        });
    }

    private void OnPreviewMediaFailed(object? sender, VideoPreviewFailedEventArgs e)
    {
        RunPreviewOnUiThread(() =>
        {
            if (!IsCurrentPreviewSource(e.SourcePath))
            {
                return;
            }

            ResetPreviewState();
            if (string.IsNullOrWhiteSpace(e.Message))
            {
                SetStatusMessage(
                    "splitAudio.preview.unavailable.generic",
                    "\u5f53\u524d\u6587\u4ef6\u9884\u89c8\u4e0d\u53ef\u7528\uff0c\u4ecd\u53ef\u4ee5\u7ee7\u7eed\u62c6\u97f3\u3002");
            }
            else
            {
                SetStatusMessage(() => e.Message);
            }

            SplitAudioPlaybackCoordinator.NotifyPaused(this);
        });
    }

    private void OnPreviewPositionChanged(object? sender, VideoPreviewPositionChangedEventArgs e)
    {
        RunPreviewOnUiThread(() =>
        {
            if (!HasInput ||
                !IsPreviewReady ||
                _isDragging ||
                IsSeeking ||
                _isTimelineInteractionPending)
            {
                return;
            }

            SyncCurrentPosition(ClampToDuration(e.Position));
        });
    }

    private void OnPreviewPlaybackStateChanged(object? sender, VideoPreviewPlaybackStateChangedEventArgs e)
    {
        RunPreviewOnUiThread(() =>
        {
            if (!HasInput)
            {
                return;
            }

            SetPlaying(e.IsPlaying);
            if (!e.IsPlaying && !_isDragging && !IsSeeking && !_isTimelineInteractionPending)
            {
                SyncCurrentPosition(ClampToDuration(_videoPreviewService.CurrentPosition));
            }
        });
    }

    private void OnPreviewMediaEnded(object? sender, EventArgs e)
    {
        RunPreviewOnUiThread(() =>
        {
            if (!HasInput)
            {
                return;
            }

            SetPlaying(false);
            SyncCurrentPosition(ClampToDuration(_videoPreviewService.CurrentPosition));
            SplitAudioPlaybackCoordinator.NotifyPaused(this);
        });
    }

    private void ResetPreviewState(bool clearDuration = true)
    {
        _resumePlaybackAfterDragging = false;
        _isTimelineInteractionPending = false;
        SetSeeking(false);
        SetDragging(false);
        SetPlaying(false);
        IsPreviewReady = false;
        if (clearDuration)
        {
            _previewDuration = TimeSpan.Zero;
        }
        SplitAudioPlaybackCoordinator.NotifyPaused(this);
        OnPropertyChanged(nameof(CanPlayPreview));
        RaiseTimelineChanged();
        SyncCurrentPosition(TimeSpan.Zero);
    }

    private void ApplyPlayableDuration(TimeSpan duration)
    {
        _previewDuration = duration < TimeSpan.Zero ? TimeSpan.Zero : duration;
        OnPropertyChanged(nameof(CanPlayPreview));
        RaiseTimelineChanged();
    }

    private void SyncCurrentPosition(TimeSpan position)
    {
        var normalized = ClampToDuration(position);
        if (SetProperty(ref _currentPreviewPosition, normalized, nameof(CurrentPositionMilliseconds)))
        {
            OnPropertyChanged(nameof(TimelinePositionText));
        }
    }

    private void RaiseTimelineChanged()
    {
        OnPropertyChanged(nameof(TimelineMinimum));
        OnPropertyChanged(nameof(TimelineMaximum));
        OnPropertyChanged(nameof(CurrentPositionMilliseconds));
        OnPropertyChanged(nameof(TimelinePositionText));
    }

    private void SetPlaying(bool isPlaying) => IsPlaying = isPlaying;

    private void SetSeeking(bool isSeeking)
    {
        if (SetProperty(ref _isSeeking, isSeeking, nameof(IsSeeking)))
        {
            OnPropertyChanged(nameof(CanPlayPreview));
        }
    }

    private void SetDragging(bool isDragging)
    {
        SetProperty(ref _isDragging, isDragging, nameof(IsDragging));
    }

    private TimeSpan ClampToDuration(TimeSpan position)
    {
        if (position < TimeSpan.Zero)
        {
            return TimeSpan.Zero;
        }

        if (_previewDuration <= TimeSpan.Zero)
        {
            return position;
        }

        return position > _previewDuration ? _previewDuration : position;
    }

    private bool IsCurrentPreviewSource(string sourcePath) =>
        !string.IsNullOrWhiteSpace(sourcePath) &&
        string.Equals(InputPath, sourcePath, StringComparison.OrdinalIgnoreCase);

    private void RunPreviewOnUiThread(Action action)
    {
        ArgumentNullException.ThrowIfNull(action);

        if (_dispatcherService.HasThreadAccess)
        {
            action();
            return;
        }

        _dispatcherService.TryEnqueue(action);
    }

    private string FormatPreviewTime(TimeSpan duration)
    {
        if (duration < TimeSpan.Zero)
        {
            duration = TimeSpan.Zero;
        }

        return _previewDuration.TotalHours >= 1d
            ? $"{(int)duration.TotalHours:00}:{duration.Minutes:00}:{duration.Seconds:00}.{duration.Milliseconds:000}"
            : $"{(int)duration.TotalMinutes:00}:{duration.Seconds:00}.{duration.Milliseconds:000}";
    }

    private string FormatCompactPreviewTime(TimeSpan duration)
    {
        if (duration < TimeSpan.Zero)
        {
            duration = TimeSpan.Zero;
        }

        return _previewDuration.TotalHours >= 1d
            ? $"{(int)duration.TotalHours:00}:{duration.Minutes:00}:{duration.Seconds:00}"
            : $"{(int)duration.TotalMinutes:00}:{duration.Seconds:00}";
    }

    private static bool AreClose(TimeSpan left, TimeSpan right) =>
        Math.Abs((left - right).TotalMilliseconds) < 1d;

    async Task ISplitAudioPlaybackParticipant.PauseForPlaybackCoordinationAsync() =>
        await PausePreviewForPlaybackCoordinationAsync();
}
