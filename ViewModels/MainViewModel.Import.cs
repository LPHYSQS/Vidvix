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
            StatusMessage = GetImportBusyMessage();
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

        StatusMessage = GetImportOrganizingMessage();

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

            var item = new MediaJobViewModel(filePath, supportsThumbnail: !IsAudioWorkspace, _localizationService);
            item.UpdatePlannedOutputPath(CreateOutputPath(filePath));
            ImportItems.Add(item);
            _ = LoadQueueThumbnailAsync(item);
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
                StatusMessage = GetFileImportCancelledMessage();
                return;
            }

            await ImportPathsAsync(selectedFiles);
        }
        catch (OperationCanceledException)
        {
            StatusMessage = GetFileImportCancelledMessage();
        }
        catch (Exception exception)
        {
            StatusMessage = GetFileImportFailedMessage();
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
                StatusMessage = GetFolderImportCancelledMessage();
                return;
            }

            await ImportPathsAsync(new[] { selectedFolder });
        }
        catch (OperationCanceledException)
        {
            StatusMessage = GetFolderImportCancelledMessage();
        }
        catch (Exception exception)
        {
            StatusMessage = GetFolderImportFailedMessage();
            _logger.Log(LogLevel.Error, "导入文件夹时发生异常。", exception);
        }
    }

    private async Task SelectOutputDirectoryAsync()
    {
        try
        {
            var selectedFolder = await _filePickerService.PickFolderAsync(
                GetLocalizedText("mainWindow.settings.outputDirectory.dialogTitle", "选择输出目录"));

            if (string.IsNullOrWhiteSpace(selectedFolder))
            {
                StatusMessage = GetOutputDirectorySelectionCancelledMessage();
                return;
            }

            OutputDirectory = selectedFolder;
            StatusMessage = CreateOutputDirectorySelectedMessage(OutputDirectory);
        }
        catch (OperationCanceledException)
        {
            StatusMessage = GetOutputDirectorySelectionCancelledMessage();
        }
        catch (Exception exception)
        {
            StatusMessage = GetOutputDirectorySelectionFailedMessage();
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
        StatusMessage = GetQueueClearedMessage();
    }

    private void ClearOutputDirectory()
    {
        if (!HasCustomOutputDirectory)
        {
            return;
        }

        OutputDirectory = string.Empty;
        StatusMessage = GetOutputDirectoryClearedMessage();
    }

    private void RemoveImportItem(object? parameter)
    {
        if (parameter is not MediaJobViewModel item || !ImportItems.Remove(item))
        {
            return;
        }

        CloseMediaDetailsIfShowing(item.InputPath);
        StatusMessage = CreateQueueItemRemovedMessage(item.InputFileName);
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
            return GetImportDuplicateMessage();
        }

        return discovery.UnsupportedEntries > 0 || discovery.MissingEntries > 0
            ? CreateNoProcessableImportMessage()
            : GetImportNoNewFilesMessage();
    }
}
