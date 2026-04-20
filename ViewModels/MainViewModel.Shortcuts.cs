using System;
using System.Threading.Tasks;
using Microsoft.UI.Xaml.Controls;
using Vidvix.Core.Models;

namespace Vidvix.ViewModels;

public sealed partial class MainViewModel
{
    private async Task CreateDesktopShortcutAsync()
    {
        IsDesktopShortcutNotificationOpen = false;

        try
        {
            var result = await Task.Run(_desktopShortcutService.EnsureDesktopShortcut);

            DesktopShortcutNotificationSeverity = result.CreatedNewShortcut
                ? InfoBarSeverity.Success
                : InfoBarSeverity.Informational;
            SetDesktopShortcutNotificationState(
                result.CreatedNewShortcut
                    ? DesktopShortcutNotificationState.Created
                    : DesktopShortcutNotificationState.Exists);
            IsDesktopShortcutNotificationOpen = true;

            StatusMessage = result.CreatedNewShortcut
                ? FormatLocalizedText(
                    "settings.desktopShortcut.status.created",
                    $"已创建桌面快捷方式：{result.ShortcutPath}",
                    ("path", result.ShortcutPath))
                : GetLocalizedText(
                    "settings.desktopShortcut.status.exists",
                    "桌面快捷方式已存在，无需重复创建。");
        }
        catch (Exception exception)
        {
            _logger.Log(LogLevel.Warning, "创建桌面快捷方式失败。", exception);
            DesktopShortcutNotificationSeverity = InfoBarSeverity.Error;
            SetDesktopShortcutNotificationState(DesktopShortcutNotificationState.Failed);
            IsDesktopShortcutNotificationOpen = true;
            StatusMessage = GetLocalizedText(
                "settings.desktopShortcut.status.failed",
                "创建桌面快捷方式失败，请稍后重试。");
        }
    }
}
