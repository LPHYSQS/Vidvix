using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Vidvix.Core.Interfaces;
using Vidvix.Core.Models;

namespace Vidvix.ViewModels;

public sealed partial class VideoTrimWorkspaceViewModel
{
    private static readonly TimeSpan SelectionBoundaryWarmupDebounce = TimeSpan.FromMilliseconds(180);
    private readonly IDispatcherService _dispatcherService;
    private readonly IVideoPreviewService _videoPreviewService;
    private readonly SemaphoreSlim _previewInteractionSemaphore = new(1, 1);
    private CancellationTokenSource? _selectionBoundaryWarmupCancellationSource;
    private bool _isBoundaryWarmupInProgress;
    private bool _isTimelineInteractionPending;
    private bool _isSeeking;
    private bool _isDragging;
    private bool _resumePlaybackAfterDragging;

    public bool IsSeeking
    {
        get => _isSeeking;
        private set
        {
            if (SetProperty(ref _isSeeking, value))
            {
                OnPropertyChanged(nameof(CanPlayPreview));
                OnPropertyChanged(nameof(CanJumpToSelectionBoundary));
            }
        }
    }

    public bool IsDragging
    {
        get => _isDragging;
        private set
        {
            if (SetProperty(ref _isDragging, value))
            {
                OnPropertyChanged(nameof(CanJumpToSelectionBoundary));
            }
        }
    }

    private void InitializePreview()
    {
        _videoPreviewService.MediaOpened += OnPreviewMediaOpened;
        _videoPreviewService.MediaFailed += OnPreviewMediaFailed;
        _videoPreviewService.PositionChanged += OnPreviewPositionChanged;
        _videoPreviewService.PlaybackStateChanged += OnPreviewPlaybackStateChanged;
        _videoPreviewService.MediaEnded += OnPreviewMediaEnded;
        _ = _videoPreviewService.SetVolumeAsync(_volume);
    }

    internal Task EnsurePreviewHostReadyAsync(CancellationToken cancellationToken = default) =>
        _videoPreviewService.InitializeAsync(cancellationToken);

    internal async Task ReloadPreviewAsync(CancellationToken cancellationToken = default)
    {
        if (!HasInput || string.IsNullOrWhiteSpace(_inputPath) || !File.Exists(_inputPath))
        {
            ResetPreviewInteractionState();
            await _videoPreviewService.UnloadAsync(cancellationToken).ConfigureAwait(false);
            return;
        }

        SetPreviewPreparing(GetDefaultPreviewPreparingMessage());
        try
        {
            await _videoPreviewService
                .InitializeAsync(cancellationToken)
                .ConfigureAwait(false);

            await _videoPreviewService
                .LoadAsync(
                    _inputPath,
                    _volume,
                    enableExternalSubtitleAutoLoad: !IsAudioTrim,
                    cancellationToken)
                .ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
        }
        catch (Exception exception)
        {
            _logger.Log(LogLevel.Error, GetLocalizedText("trim.log.previewLoadFailed", "MPV 预览加载失败。"), exception);
            RunPreviewOnUiThread(SetPreviewUnavailable);
        }
    }

    internal void UpdatePreviewHostPlacement(VideoPreviewHostPlacement placement)
    {
        if (IsAudioTrim && _mediaDetailsSnapshot?.HasEmbeddedArtwork != true)
        {
            placement = placement with { IsVisible = false };
        }

        _videoPreviewService.UpdateHostPlacement(placement);
    }

    internal Task RefreshPreviewRenderingAsync(CancellationToken cancellationToken = default) =>
        IsAudioTrim && _mediaDetailsSnapshot?.HasEmbeddedArtwork != true
            ? Task.CompletedTask
            : _videoPreviewService.RefreshAsync(cancellationToken);

    internal bool HasLoadedPreview => _videoPreviewService.HasLoadedMedia;

    internal void BeginTimelineInteractionPriority() => _isTimelineInteractionPending = true;

    internal void EndTimelineInteractionPriority() => _isTimelineInteractionPending = false;

    internal async Task TogglePreviewPlaybackAsync()
    {
        CancelSelectionBoundaryWarmup();
        await _previewInteractionSemaphore.WaitAsync();
        try
        {
            if (!HasLoadedPreview || IsSeeking || IsDragging)
            {
                return;
            }

            if (IsPlaying)
            {
                await PausePreviewAsync();
                return;
            }

            var target = ClampToSelection(_currentPosition);
            if (target < _selectionStart || target >= _selectionEnd)
            {
                target = _selectionStart;
            }

            var previewPosition = ClampToSelection(_videoPreviewService.CurrentPosition);
            var actual = AreClose(previewPosition, target)
                ? previewPosition
                : await _videoPreviewService.SetPlaybackPositionAsync(target);
            SyncCurrentPosition(actual);
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
        CancelSelectionBoundaryWarmup();
        await _previewInteractionSemaphore.WaitAsync();
        try
        {
            _resumePlaybackAfterDragging = false;
            _isTimelineInteractionPending = false;
            IsDragging = false;

            if (!HasLoadedPreview)
            {
                SetPlaying(false);
                return;
            }

            if (IsPlaying || _videoPreviewService.IsPlaying)
            {
                await PausePreviewAsync();
                return;
            }

            SyncCurrentPosition(ClampToSelection(_videoPreviewService.CurrentPosition));
            SetPlaying(false);
        }
        finally
        {
            _previewInteractionSemaphore.Release();
        }
    }

    internal async Task BeginTimelineDragAsync()
    {
        CancelSelectionBoundaryWarmup();
        await _previewInteractionSemaphore.WaitAsync();
        try
        {
            if (IsDragging || IsSeeking)
            {
                return;
            }

            _resumePlaybackAfterDragging = IsPlaying;
            IsDragging = true;

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

        SyncCurrentPosition(ClampToSelection(position));
    }

    internal async Task PreviewScrubAsync(TimeSpan position)
    {
        if (!HasLoadedPreview || IsSeeking)
        {
            return;
        }

        var actual = await _videoPreviewService.SetPlaybackPositionAsync(ClampToSelection(position));
        SyncCurrentPosition(actual);
    }

    internal async Task CompleteTimelineDragAsync(TimeSpan position)
    {
        CancelSelectionBoundaryWarmup();
        await _previewInteractionSemaphore.WaitAsync();
        try
        {
            if (!IsDragging && !_resumePlaybackAfterDragging)
            {
                return;
            }

            var target = ClampToSelection(position);
            var shouldResumePlayback = _resumePlaybackAfterDragging;
            _resumePlaybackAfterDragging = false;
            IsDragging = false;

            if (!HasLoadedPreview)
            {
                SyncCurrentPosition(target);
                SetPlaying(false);
                return;
            }

            await SetPreviewPositionAsync(target, shouldResumePlayback);
        }
        finally
        {
            _previewInteractionSemaphore.Release();
        }

        QueueSelectionBoundaryWarmup();
    }

    internal async Task JumpTimelinePositionAsync(TimeSpan position)
    {
        CancelSelectionBoundaryWarmup();
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

    internal async Task JumpToSelectionBoundaryAsync(TimeSpan position)
    {
        CancelSelectionBoundaryWarmup();
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

    internal async Task HandleSelectionRangeChangedAsync()
    {
        CancelSelectionBoundaryWarmup();
        await _previewInteractionSemaphore.WaitAsync();
        try
        {
            var constrainedPosition = EnsureCurrentPositionWithinSelection();
            if (!HasLoadedPreview || IsSeeking || IsDragging)
            {
                SyncCurrentPosition(constrainedPosition);
                return;
            }

            var playbackPosition = _videoPreviewService.CurrentPosition;
            if (playbackPosition < _selectionStart)
            {
                SyncCurrentPosition(await SeekPreviewCoreAsync(_selectionStart));
                return;
            }

            if (playbackPosition > _selectionEnd)
            {
                if (IsPlaying)
                {
                    await PausePreviewAsync();
                }

                SyncCurrentPosition(await SeekPreviewCoreAsync(_selectionEnd));
                SetPlaying(false);
                return;
            }

            if (!AreClose(constrainedPosition, playbackPosition))
            {
                SyncCurrentPosition(await SeekPreviewCoreAsync(constrainedPosition));
                return;
            }

            SyncCurrentPosition(constrainedPosition);
        }
        finally
        {
            _previewInteractionSemaphore.Release();
        }

        QueueSelectionBoundaryWarmup();
    }

    internal async Task StopPlaybackAtSelectionEndAsync()
    {
        CancelSelectionBoundaryWarmup();
        await _previewInteractionSemaphore.WaitAsync();
        try
        {
            _resumePlaybackAfterDragging = false;
            IsDragging = false;

            if (!HasLoadedPreview)
            {
                SyncCurrentPosition(_selectionEnd);
                SetPlaying(false);
                return;
            }

            await PausePreviewAsync();
            SyncCurrentPosition(await SeekPreviewCoreAsync(_selectionEnd));
            SetPlaying(false);
        }
        finally
        {
            _previewInteractionSemaphore.Release();
        }
    }

    private async Task<TimeSpan> PausePreviewAsync()
    {
        var position = HasLoadedPreview
            ? await _videoPreviewService.PauseAsync()
            : ClampToSelection(_currentPosition);

        SyncCurrentPosition(position);
        SetPlaying(false);
        return position;
    }

    private async Task<TimeSpan> SeekPreviewCoreAsync(TimeSpan position)
    {
        var target = ClampToSelection(position);
        if (!HasLoadedPreview)
        {
            SyncCurrentPosition(target);
            return target;
        }

        IsSeeking = true;
        try
        {
            var actual = await _videoPreviewService.SeekAsync(target);
            SyncCurrentPosition(actual);
            return actual;
        }
        finally
        {
            IsSeeking = false;
        }
    }

    private async Task<TimeSpan> SetPreviewPositionAsync(TimeSpan position, bool shouldBePlayingAfterPositioning)
    {
        var target = ClampToSelection(position);
        SyncCurrentPosition(target);

        if (!HasLoadedPreview)
        {
            SetPlaying(false);
            return target;
        }

        var actual = target;
        IsSeeking = true;
        try
        {
            actual = await _videoPreviewService.SetPlaybackPositionAsync(target);
            SyncCurrentPosition(actual);

            if (shouldBePlayingAfterPositioning && !_videoPreviewService.IsPlaying)
            {
                await _videoPreviewService.PlayAsync();
            }
        }
        finally
        {
            IsSeeking = false;
        }

        SetPlaying(shouldBePlayingAfterPositioning);
        return actual;
    }

    private void UpdatePreviewVolume() => _ = _videoPreviewService.SetVolumeAsync(_volume);

    private void CancelSelectionBoundaryWarmup()
    {
        if (_selectionBoundaryWarmupCancellationSource is null)
        {
            return;
        }

        try
        {
            _selectionBoundaryWarmupCancellationSource.Cancel();
        }
        catch (ObjectDisposedException)
        {
        }
    }

    private void QueueSelectionBoundaryWarmup()
    {
        if (!HasLoadedPreview ||
            IsAudioTrim ||
            !IsPreviewReady ||
            IsPlaying ||
            IsSeeking ||
            IsDragging ||
            _mediaDuration <= TimeSpan.Zero)
        {
            return;
        }

        var previousCancellationSource = _selectionBoundaryWarmupCancellationSource;
        _selectionBoundaryWarmupCancellationSource = new CancellationTokenSource();
        previousCancellationSource?.Cancel();
        previousCancellationSource?.Dispose();

        var cancellationToken = _selectionBoundaryWarmupCancellationSource.Token;
        _ = Task.Run(() => WarmSelectionBoundaryFramesAsync(cancellationToken), cancellationToken);
    }

    private async Task WarmSelectionBoundaryFramesAsync(CancellationToken cancellationToken)
    {
        try
        {
            await Task.Delay(SelectionBoundaryWarmupDebounce, cancellationToken).ConfigureAwait(false);

            if (!HasLoadedPreview ||
                !IsPreviewReady ||
                IsPlaying ||
                IsSeeking ||
                IsDragging ||
                _mediaDuration <= TimeSpan.Zero)
            {
                return;
            }

            var restorePosition = ClampToSelection(_currentPosition);
            var priorityTargets = BuildBoundaryWarmupTargets(restorePosition);
            if (priorityTargets.Length == 0)
            {
                return;
            }

            _isBoundaryWarmupInProgress = true;
            try
            {
                foreach (var target in priorityTargets)
                {
                    cancellationToken.ThrowIfCancellationRequested();

                    if (IsPlaying || IsSeeking || IsDragging)
                    {
                        return;
                    }

                    await _videoPreviewService.SetPlaybackPositionAsync(target, cancellationToken).ConfigureAwait(false);
                }

                cancellationToken.ThrowIfCancellationRequested();

                if (IsPlaying || IsSeeking || IsDragging)
                {
                    return;
                }

                var restored = await _videoPreviewService
                    .SetPlaybackPositionAsync(restorePosition, cancellationToken)
                    .ConfigureAwait(false);
                SyncCurrentPosition(restored);
            }
            finally
            {
                _isBoundaryWarmupInProgress = false;
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
        }
        catch (Exception exception)
        {
            _logger.Log(LogLevel.Warning, GetLocalizedText("trim.log.boundaryWarmupFailed", "裁剪边界预热失败，已保留当前预览状态。"), exception);
        }
    }

    private TimeSpan[] BuildBoundaryWarmupTargets(TimeSpan restorePosition)
    {
        Span<TimeSpan> candidates =
        [
            ClampToSelection(_selectionStart),
            ClampToSelection(_selectionEnd),
            ClampToSelection(_selectionStart + TimeSpan.FromMilliseconds(1)),
            ClampToSelection(_selectionEnd - TimeSpan.FromMilliseconds(1))
        ];

        var result = new TimeSpan[candidates.Length];
        var count = 0;

        foreach (var candidate in candidates)
        {
            if (AreClose(candidate, restorePosition))
            {
                continue;
            }

            var isDuplicate = false;
            for (var index = 0; index < count; index++)
            {
                if (AreClose(result[index], candidate))
                {
                    isDuplicate = true;
                    break;
                }
            }

            if (isDuplicate)
            {
                continue;
            }

            result[count++] = candidate;
        }

        return result[..count];
    }

    private void DisposePreview()
    {
        CancelSelectionBoundaryWarmup();
        _selectionBoundaryWarmupCancellationSource?.Dispose();
        _selectionBoundaryWarmupCancellationSource = null;
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
            ResetPreviewInteractionState();
            SyncCurrentPosition(ClampToSelection(_videoPreviewService.CurrentPosition));
            SetPreviewReady();
            QueueSelectionBoundaryWarmup();
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

            if (string.IsNullOrWhiteSpace(e.Message))
            {
                SetPreviewUnavailable();
                return;
            }

            SetPreviewFailed(e.Message);
        });
    }

    private void OnPreviewPositionChanged(object? sender, VideoPreviewPositionChangedEventArgs e)
    {
        RunPreviewOnUiThread(() =>
        {
            if (!HasInput ||
                !IsPreviewReady ||
                IsDragging ||
                IsSeeking ||
                _isTimelineInteractionPending ||
                _isBoundaryWarmupInProgress)
            {
                return;
            }

            SyncCurrentPosition(ClampToSelection(e.Position));
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
            if (!e.IsPlaying &&
                !IsDragging &&
                !IsSeeking &&
                !_isTimelineInteractionPending &&
                !_isBoundaryWarmupInProgress)
            {
                SyncCurrentPosition(ClampToSelection(_videoPreviewService.CurrentPosition));
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
            SyncCurrentPosition(ClampToSelection(_videoPreviewService.CurrentPosition));
        });
    }

    private void ResetPreviewInteractionState()
    {
        _resumePlaybackAfterDragging = false;
        CancelSelectionBoundaryWarmup();
        _selectionBoundaryWarmupCancellationSource?.Dispose();
        _selectionBoundaryWarmupCancellationSource = null;
        _isBoundaryWarmupInProgress = false;
        _isTimelineInteractionPending = false;
        IsDragging = false;
        IsSeeking = false;
        SetPlaying(false);
    }

    private bool IsCurrentPreviewSource(string sourcePath) =>
        !string.IsNullOrWhiteSpace(sourcePath) &&
        string.Equals(_inputPath, sourcePath, StringComparison.OrdinalIgnoreCase);

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
}
