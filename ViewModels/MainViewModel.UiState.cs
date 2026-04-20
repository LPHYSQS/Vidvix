using System;
using System.Collections.Specialized;
using Microsoft.UI.Xaml;
using Vidvix.Core.Models;

namespace Vidvix.ViewModels;

public sealed partial class MainViewModel
{
    private void NotifyCommandStates()
    {
        _selectFilesCommand.NotifyCanExecuteChanged();
        _selectFolderCommand.NotifyCanExecuteChanged();
        _selectOutputDirectoryCommand.NotifyCanExecuteChanged();
        _clearQueueCommand.NotifyCanExecuteChanged();
        _clearOutputDirectoryCommand.NotifyCanExecuteChanged();
        _removeImportItemCommand.NotifyCanExecuteChanged();
        _showMediaDetailsCommand.NotifyCanExecuteChanged();
        _executeProcessingCommand.NotifyCanExecuteChanged();
        _cancelExecutionCommand.NotifyCanExecuteChanged();
        _closeMediaDetailsCommand.NotifyCanExecuteChanged();
        _copyAllMediaDetailsCommand.NotifyCanExecuteChanged();
        _copyMediaDetailSectionCommand.NotifyCanExecuteChanged();
        _switchToVideoWorkspaceCommand.NotifyCanExecuteChanged();
        _switchToAudioWorkspaceCommand.NotifyCanExecuteChanged();
        _switchToTrimWorkspaceCommand.NotifyCanExecuteChanged();
        _switchToMergeWorkspaceCommand.NotifyCanExecuteChanged();
        _switchToSplitAudioWorkspaceCommand.NotifyCanExecuteChanged();
        _switchToTerminalWorkspaceCommand.NotifyCanExecuteChanged();
    }

    private void SetReadyStatusMessage()
    {
        StatusMessage = ImportItems.Count == 0
            ? GetReadyForImportMessage()
            : GetReadyForProcessingMessage();
    }

    private void OnImportItemsChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        OnPropertyChanged(nameof(QueueSummaryText));
        RecalculatePlannedOutputs();
        NotifyCommandStates();
    }

    private void AddUiLog(LogLevel level, string message, bool clearExisting) =>
        AddUiLog(_selectedWorkspaceKind, level, message, clearExisting);

    private void AddUiLog(ProcessingWorkspaceKind workspaceKind, LogLevel level, string message, bool clearExisting)
    {
        var targetLogEntries = GetLogEntries(workspaceKind);

        void UpdateLogEntries()
        {
            if (clearExisting)
            {
                targetLogEntries.Clear();
            }

            targetLogEntries.Insert(0, new LogEntry(DateTimeOffset.Now, level, message));

            if (targetLogEntries.Count > 200)
            {
                targetLogEntries.RemoveAt(targetLogEntries.Count - 1);
            }
        }

        if (_dispatcherService.HasThreadAccess)
        {
            UpdateLogEntries();
            return;
        }

        _dispatcherService.TryEnqueue(UpdateLogEntries);
    }

    private void ClearUiLogs() => ClearUiLogs(_selectedWorkspaceKind);

    private void ClearUiLogs(ProcessingWorkspaceKind workspaceKind)
    {
        var targetLogEntries = GetLogEntries(workspaceKind);

        if (_dispatcherService.HasThreadAccess)
        {
            targetLogEntries.Clear();
            return;
        }

        _dispatcherService.TryEnqueue(() => targetLogEntries.Clear());
    }

    private static ElementTheme ConvertThemePreferenceToElementTheme(ThemePreference preference) => preference switch
    {
        ThemePreference.Light => ElementTheme.Light,
        ThemePreference.Dark => ElementTheme.Dark,
        _ => ElementTheme.Default
    };
}
