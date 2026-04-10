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
        _switchToVideoWorkspaceCommand.NotifyCanExecuteChanged();
        _switchToAudioWorkspaceCommand.NotifyCanExecuteChanged();
    }

    private void SetReadyStatusMessage()
    {
        StatusMessage = ImportItems.Count == 0
            ? GetReadyForImportMessage()
            : ReadyForProcessingMessage;
    }

    private void OnImportItemsChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        OnPropertyChanged(nameof(QueueSummaryText));
        RecalculatePlannedOutputs();
        NotifyCommandStates();
    }

    private void AddUiLog(LogLevel level, string message, bool clearExisting)
    {
        void UpdateLogEntries()
        {
            if (clearExisting)
            {
                LogEntries.Clear();
            }

            LogEntries.Insert(0, new LogEntry(DateTimeOffset.Now, level, message));

            if (LogEntries.Count > 200)
            {
                LogEntries.RemoveAt(LogEntries.Count - 1);
            }
        }

        if (_dispatcherService.HasThreadAccess)
        {
            UpdateLogEntries();
            return;
        }

        _dispatcherService.TryEnqueue(UpdateLogEntries);
    }

    private void ClearUiLogs()
    {
        if (_dispatcherService.HasThreadAccess)
        {
            LogEntries.Clear();
            return;
        }

        _dispatcherService.TryEnqueue(() => LogEntries.Clear());
    }

    private static ElementTheme ConvertThemePreferenceToElementTheme(ThemePreference preference) => preference switch
    {
        ThemePreference.Light => ElementTheme.Light,
        ThemePreference.Dark => ElementTheme.Dark,
        _ => ElementTheme.Default
    };
}
