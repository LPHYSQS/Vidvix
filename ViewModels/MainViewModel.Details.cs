using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Vidvix.Core.Models;

namespace Vidvix.ViewModels;

public sealed partial class MainViewModel
{
    // 媒体详情面板的延迟打开、取消和缓存命中逻辑集中在这里。

    private void ToggleSettingsPane()
    {
        var shouldOpen = !IsSettingsPaneOpen;
        if (shouldOpen)
        {
            _pendingDetailItem = null;
            CloseMediaDetails();
        }

        IsSettingsPaneOpen = shouldOpen;
    }

    private void CloseSettingsPane()
    {
        _pendingDetailItem = null;
        IsSettingsPaneOpen = false;
    }

    private void OpenMediaDetails(object? parameter)
    {
        if (parameter is not MediaJobViewModel item || !ImportItems.Contains(item))
        {
            return;
        }

        if (IsSettingsPaneOpen)
        {
            _pendingDetailItem = item;
            IsSettingsPaneOpen = false;
            return;
        }

        _pendingDetailItem = null;
        _ = OpenMediaDetailsAsync(item);
    }

    private async Task OpenMediaDetailsAsync(MediaJobViewModel item)
    {
        var inputPath = item.InputPath;
        var title = item.InputFileName;

        CancelDetailLoad();
        var detailLoadVersion = Interlocked.Increment(ref _detailLoadVersion);
        IsSettingsPaneOpen = false;

        if (_mediaInfoService.TryGetCachedDetails(inputPath, out var cachedSnapshot))
        {
            if (!IsCurrentDetailLoadVersion(detailLoadVersion))
            {
                return;
            }

            DetailPanel.ShowDetails(cachedSnapshot, _selectedWorkspaceKind);
            StatusMessage = FormatLocalizedText(
                "mediaDetails.status.loadedFromCache",
                $"已从缓存载入 {item.InputFileName} 的详情。",
                ("fileName", item.InputFileName));
            NotifyCommandStates();
            return;
        }

        if (!IsCurrentDetailLoadVersion(detailLoadVersion))
        {
            return;
        }

        DetailPanel.ShowLoading(title, inputPath, _selectedWorkspaceKind);
        StatusMessage = FormatLocalizedText(
            "mediaDetails.status.loading",
            $"正在解析 {item.InputFileName} 的媒体详情...",
            ("fileName", item.InputFileName));
        NotifyCommandStates();

        var detailLoadCancellationSource = new CancellationTokenSource();
        _detailLoadCancellationSource = detailLoadCancellationSource;

        try
        {
            var result = await _mediaInfoService.GetMediaDetailsAsync(inputPath, detailLoadCancellationSource.Token).ConfigureAwait(false);
            if (!IsCurrentDetailLoadVersion(detailLoadVersion))
            {
                return;
            }

            _dispatcherService.TryEnqueue(() =>
            {
                if (!IsCurrentDetailLoadVersion(detailLoadVersion))
                {
                    return;
                }

                if (result.IsSuccess && result.Snapshot is not null)
                {
                    DetailPanel.ShowDetails(result.Snapshot, _selectedWorkspaceKind);
                    StatusMessage = FormatLocalizedText(
                        "mediaDetails.status.loaded",
                        $"媒体详情已加载：{item.InputFileName}",
                        ("fileName", item.InputFileName));
                    return;
                }

                var errorMessage = result.ErrorMessage ?? GetLocalizedText(
                    "mediaDetails.error.unavailable",
                    "无法解析该媒体文件。");
                DetailPanel.ShowError(title, inputPath, errorMessage, _selectedWorkspaceKind);
                StatusMessage = errorMessage;
            });
        }
        catch (OperationCanceledException)
        {
        }
        catch (Exception exception)
        {
            _logger.Log(LogLevel.Error, "读取媒体详情时发生异常。", exception);
            if (!IsCurrentDetailLoadVersion(detailLoadVersion))
            {
                return;
            }

            var errorMessage = ExtractFriendlyExceptionMessage(exception);
            _dispatcherService.TryEnqueue(() =>
            {
                if (!IsCurrentDetailLoadVersion(detailLoadVersion))
                {
                    return;
                }

                DetailPanel.ShowError(title, inputPath, errorMessage, _selectedWorkspaceKind);
                StatusMessage = errorMessage;
            });
        }
        finally
        {
            if (ReferenceEquals(_detailLoadCancellationSource, detailLoadCancellationSource))
            {
                _detailLoadCancellationSource = null;
            }

            detailLoadCancellationSource.Dispose();
            _dispatcherService.TryEnqueue(NotifyCommandStates);
        }
    }

    public void HandleSettingsPaneClosed()
    {
        if (_pendingDetailItem is not { } item)
        {
            return;
        }

        _pendingDetailItem = null;

        if (!ImportItems.Contains(item))
        {
            return;
        }

        _ = OpenMediaDetailsAsync(item);
    }

    private void CloseMediaDetails()
    {
        CancelDetailLoad();
        DetailPanel.Close();
        NotifyCommandStates();
    }

    private void CancelDetailLoad()
    {
        Interlocked.Increment(ref _detailLoadVersion);
        _detailLoadCancellationSource?.Cancel();
    }

    private void CloseMediaDetailsIfShowing(string inputPath)
    {
        if (!DetailPanel.IsOpen || string.IsNullOrWhiteSpace(inputPath))
        {
            return;
        }

        if (string.Equals(DetailPanel.CurrentInputPath, inputPath, StringComparison.OrdinalIgnoreCase))
        {
            CloseMediaDetails();
        }
    }

    private bool CanOpenMediaDetails(object? parameter) =>
        parameter is MediaJobViewModel item &&
        ImportItems.Contains(item);

    private bool CanCloseMediaDetails() => DetailPanel.IsOpen;

    private bool IsCurrentDetailLoadVersion(int detailLoadVersion) =>
        Volatile.Read(ref _detailLoadVersion) == detailLoadVersion;

    private void RefreshMediaDetailsLocalization()
    {
        if (!DetailPanel.IsOpen || string.IsNullOrWhiteSpace(DetailPanel.CurrentInputPath))
        {
            return;
        }

        var currentItem = ImportItems.FirstOrDefault(item =>
            string.Equals(item.InputPath, DetailPanel.CurrentInputPath, StringComparison.OrdinalIgnoreCase));
        if (currentItem is not null)
        {
            _ = OpenMediaDetailsAsync(currentItem);
            return;
        }

        if (_mediaInfoService.TryGetCachedDetails(DetailPanel.CurrentInputPath, out var cachedSnapshot))
        {
            DetailPanel.ShowDetails(cachedSnapshot, _selectedWorkspaceKind);
        }
    }
}
