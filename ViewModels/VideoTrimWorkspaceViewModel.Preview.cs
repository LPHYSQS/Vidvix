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

    private void InitializePreview()
    {
        _videoPreviewService.MediaOpened += OnPreviewMediaOpened;
        _videoPreviewService.MediaFailed += OnPreviewMediaFailed;
        _videoPreviewService.PlaybackStateChanged += OnPreviewPlaybackStateChanged;
        _videoPreviewService.MediaEnded += OnPreviewMediaEnded;
        _videoPreviewService.SetVolume(_volume);
    }

    internal async Task ReloadPreviewAsync(CancellationToken cancellationToken = default)
    {
        if (!HasInput || string.IsNullOrWhiteSpace(_inputPath) || !File.Exists(_inputPath))
        {
            _videoPreviewService.Unload();
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

    internal TimeSpan GetPreviewPosition() => _videoPreviewService.GetCurrentPosition();

    internal void PlayPreview() => _videoPreviewService.Play();

    internal TimeSpan PausePreview()
    {
        _videoPreviewService.Pause();
        return _videoPreviewService.GetCurrentPosition();
    }

    internal void SeekPreview(TimeSpan position) => _videoPreviewService.Seek(position);

    internal void SetPreviewPlaybackPosition(TimeSpan position) =>
        _videoPreviewService.SetPlaybackPosition(position);

    private void UpdatePreviewVolume() => _videoPreviewService.SetVolume(_volume);

    private void DisposePreview()
    {
        _videoPreviewService.MediaOpened -= OnPreviewMediaOpened;
        _videoPreviewService.MediaFailed -= OnPreviewMediaFailed;
        _videoPreviewService.PlaybackStateChanged -= OnPreviewPlaybackStateChanged;
        _videoPreviewService.MediaEnded -= OnPreviewMediaEnded;
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
            var selectionStart = ClampToSelection(_selectionStart);
            _videoPreviewService.Seek(selectionStart);
            SyncCurrentPosition(selectionStart);
            SetPlaying(false);
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

    private void OnPreviewPlaybackStateChanged(object? sender, VideoPreviewPlaybackStateChangedEventArgs e)
    {
        RunPreviewOnUiThread(() =>
        {
            if (!HasInput)
            {
                return;
            }

            SetPlaying(e.IsPlaying);
            if (!e.IsPlaying)
            {
                SyncCurrentPosition(_videoPreviewService.GetCurrentPosition());
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
            SyncCurrentPosition(_videoPreviewService.GetCurrentPosition());
        });
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
