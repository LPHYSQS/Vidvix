using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Vidvix.Core.Models;

namespace Vidvix.ViewModels;

public sealed partial class MainViewModel
{
    // 导入、队列与输入选择逻辑集中在这里，避免主文件继续膨胀。

    public async Task ImportPathsAsync(IEnumerable<string> inputPaths)
    {
        ArgumentNullException.ThrowIfNull(inputPaths);

        if (IsBusy)
        {
            StatusMessage = "当前正在处理任务，请等待完成或先取消。";
            return;
        }

        var normalizedPaths = inputPaths
            .Where(path => !string.IsNullOrWhiteSpace(path))
            .Select(Path.GetFullPath)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();

        if (normalizedPaths.Length == 0)
        {
            return;
        }

        StatusMessage = "正在整理导入内容...";

        var allowedInputFileTypes = GetCurrentSupportedInputFileTypes();
        var discovery = await Task.Run(() => _mediaImportDiscoveryService.Discover(normalizedPaths, allowedInputFileTypes));
        var knownPaths = new HashSet<string>(ImportItems.Select(item => item.InputPath), StringComparer.OrdinalIgnoreCase);
        var addedCount = 0;
        var duplicateCount = 0;

        foreach (var filePath in discovery.SupportedFiles)
        {
            if (!knownPaths.Add(filePath))
            {
                duplicateCount++;
                continue;
            }

            var item = new MediaJobViewModel(filePath);
            item.UpdatePlannedOutputPath(CreateOutputPath(filePath));
            ImportItems.Add(item);
            addedCount++;
        }

        StatusMessage = CreateImportStatusMessage(addedCount, duplicateCount, discovery);

        if (discovery.UnavailableDirectories > 0)
        {
            _logger.Log(LogLevel.Warning, $"有 {discovery.UnavailableDirectories} 个文件夹无法访问，已跳过。");
        }
    }

    private async Task SelectFilesAsync()
    {
        try
        {
            var selectedFiles = await _filePickerService.PickFilesAsync(
                new FilePickerRequest(GetCurrentSupportedInputFileTypes(), GetImportFilePickerCommitText()));

            if (selectedFiles.Count == 0)
            {
                StatusMessage = "已取消文件导入。";
                return;
            }

            await ImportPathsAsync(selectedFiles);
        }
        catch (OperationCanceledException)
        {
            StatusMessage = "已取消文件导入。";
        }
        catch (Exception exception)
        {
            StatusMessage = "导入文件失败。";
            _logger.Log(LogLevel.Error, "导入文件时发生异常。", exception);
        }
    }

    private async Task SelectFolderAsync()
    {
        try
        {
            var selectedFolder = await _filePickerService.PickFolderAsync(GetImportFolderPickerCommitText());

            if (string.IsNullOrWhiteSpace(selectedFolder))
            {
                StatusMessage = "已取消文件夹导入。";
                return;
            }

            await ImportPathsAsync(new[] { selectedFolder });
        }
        catch (OperationCanceledException)
        {
            StatusMessage = "已取消文件夹导入。";
        }
        catch (Exception exception)
        {
            StatusMessage = "导入文件夹失败。";
            _logger.Log(LogLevel.Error, "导入文件夹时发生异常。", exception);
        }
    }

    private async Task SelectOutputDirectoryAsync()
    {
        try
        {
            var selectedFolder = await _filePickerService.PickFolderAsync("选择输出目录");

            if (string.IsNullOrWhiteSpace(selectedFolder))
            {
                StatusMessage = "已取消选择输出目录。";
                return;
            }

            OutputDirectory = selectedFolder;
            StatusMessage = $"已将输出目录设置为：{OutputDirectory}";
        }
        catch (OperationCanceledException)
        {
            StatusMessage = "已取消选择输出目录。";
        }
        catch (Exception exception)
        {
            StatusMessage = "选择输出目录失败。";
            _logger.Log(LogLevel.Error, "选择输出目录时发生异常。", exception);
        }
    }

    private void ClearQueue()
    {
        if (ImportItems.Count == 0)
        {
            return;
        }

        CloseMediaDetails();
        ImportItems.Clear();
        StatusMessage = "已清空待处理列表。";
    }

    private void ClearOutputDirectory()
    {
        if (!HasCustomOutputDirectory)
        {
            return;
        }

        OutputDirectory = string.Empty;
        StatusMessage = "已清空输出目录，留空时将使用原文件夹输出。";
    }

    private void RemoveImportItem(object? parameter)
    {
        if (parameter is not MediaJobViewModel item || !ImportItems.Remove(item))
        {
            return;
        }

        CloseMediaDetailsIfShowing(item.InputPath);
        StatusMessage = $"已从待处理列表移除 {item.InputFileName}。";
    }

    private bool CanClearQueue() => !IsBusy && ImportItems.Count > 0;

    private bool CanClearOutputDirectory() => CanModifyInputs && HasCustomOutputDirectory;

    private bool CanRemoveImportItem(object? parameter) =>
        !IsBusy &&
        parameter is MediaJobViewModel item &&
        ImportItems.Contains(item);

    private string CreateImportStatusMessage(int addedCount, int duplicateCount, MediaImportDiscoveryResult discovery)
    {
        if (addedCount > 0)
        {
            return CreateImportedCountMessage(addedCount);
        }

        if (duplicateCount > 0)
        {
            return "导入内容已存在于列表中。";
        }

        return discovery.UnsupportedEntries > 0 || discovery.MissingEntries > 0
            ? CreateNoProcessableImportMessage()
            : "未发现新的可处理文件。";
    }
}
