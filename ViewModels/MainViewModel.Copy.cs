using System;
using Windows.ApplicationModel.DataTransfer;
using Vidvix.Core.Models;

namespace Vidvix.ViewModels;

public sealed partial class MainViewModel
{
    public event Action<string>? TransientNotificationRequested;

    private void CopyAllMediaDetails()
    {
        CopyMediaDetailsCore("All");
    }

    private void CopyMediaDetailSection(object? parameter)
    {
        CopyMediaDetailsCore(parameter);
    }

    private bool CanCopyAllMediaDetails() => DetailPanel.IsOpen && DetailPanel.HasContent;

    private bool CanCopyMediaDetailSection(object? parameter) => DetailPanel.IsOpen && DetailPanel.CanCopySection(parameter);

    private void CopyMediaDetailsCore(object? parameter)
    {
        if (!DetailPanel.TryBuildCopyText(parameter, out var text, out var feedbackMessage))
        {
            return;
        }

        try
        {
            var package = new DataPackage();
            package.SetText(text);
            Clipboard.SetContent(package);

            StatusMessage = feedbackMessage;
            TransientNotificationRequested?.Invoke(feedbackMessage);
        }
        catch (Exception exception)
        {
            _logger.Log(LogLevel.Warning, "\u590d\u5236\u5a92\u4f53\u8be6\u60c5\u5931\u8d25\u3002", exception);
            StatusMessage = "\u590d\u5236\u5931\u8d25\uff0c\u8bf7\u7a0d\u540e\u91cd\u8bd5\u3002";
            TransientNotificationRequested?.Invoke(StatusMessage);
        }
    }
}
