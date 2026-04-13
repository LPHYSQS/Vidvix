using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Vidvix.Core.Interfaces;
using Vidvix.Core.Models;

namespace Vidvix.ViewModels;

public sealed partial class VideoTrimWorkspaceViewModel
{
    private readonly IDispatcherService _dispatcherService;
    private readonly IVideoPreviewService _videoPreviewService;
    private readonly SemaphoreSlim _previewInteractionSemaphore = new(1, 1);
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

    internal async Task ReloadPreviewAsync(CancellationToken cancellationToken = default)
    {
        if (!HasInput || string.IsNullOrWhiteSpace(_inputPath) || !File.Exists(_inputPath))
        {
            ResetPreviewInteractionState();
            await _videoPreviewService.UnloadAsync(cancellationToken).ConfigureAwait(false);
            return;
        }

        SetPreviewPreparing("正在准备视频预览...");
        try
        {
            await _videoPreviewService
                .LoadAsync(_inputPath, _volume, cancellationToken)
                .ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
        }
        catch (Exception exception)
        {
            _logger.Log(LogLevel.Error, "MPV 预览加载失败。", exception);
            RunPreviewOnUiThread(() =>
                SetPreviewFailed("当前视频无法预览，但仍可尝试直接导出。"));
        }
    }

    internal void UpdatePreviewHostPlacement(VideoPreviewHostPlacement placement) =>
        _videoPreviewService.UpdateHostPlacement(placement);

    internal bool HasLoadedPreview => _videoPreviewService.HasLoadedMedia;

    internal async Task TogglePreviewPlaybackAsync()
    {
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

            var actual = await SeekPreviewCoreAsync(target);
            SyncCurrentPosition(actual);
            await _videoPreviewService.PlayAsync();
            SetPlaying(true);
        }
        finally
        {
            _previewInteractionSemaphore.Release();
        }
    }

    internal async Task BeginTimelineDragAsync()
    {
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

            var actual = await SeekPreviewCoreAsync(target);
            SyncCurrentPosition(actual);

            if (shouldResumePlayback)
            {
                await _videoPreviewService.PlayAsync();
                SetPlaying(true);
            }
            else
            {
                SetPlaying(false);
            }
        }
        finally
        {
            _previewInteractionSemaphore.Release();
        }
    }

    internal async Task JumpToSelectionBoundaryAsync(TimeSpan position)
    {
        await _previewInteractionSemaphore.WaitAsync();
        try
        {
            if (!HasLoadedPreview)
            {
                return;
            }

            var actual = await SeekPreviewCoreAsync(position);
            SyncCurrentPosition(actual);
            SetPlaying(false);
        }
        finally
        {
            _previewInteractionSemaphore.Release();
        }
    }

    internal async Task HandleSelectionRangeChangedAsync()
    {
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
    }

    internal async Task StopPlaybackAtSelectionEndAsync()
    {
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

    private void UpdatePreviewVolume() => _ = _videoPreviewService.SetVolumeAsync(_volume);

    private void DisposePreview()
    {
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

            SetPreviewFailed(string.IsNullOrWhiteSpace(e.Message)
                ? "当前视频无法预览，但仍可尝试直接导出。"
                : e.Message);
        });
    }

    private void OnPreviewPositionChanged(object? sender, VideoPreviewPositionChangedEventArgs e)
    {
        RunPreviewOnUiThread(() =>
        {
            if (!HasInput || !IsPreviewReady || IsDragging || IsSeeking)
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
            if (!e.IsPlaying && !IsDragging && !IsSeeking)
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
