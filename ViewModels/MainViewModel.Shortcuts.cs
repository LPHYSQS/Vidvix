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
            DesktopShortcutNotificationMessage = result.CreatedNewShortcut
                ? "已在桌面创建应用快捷方式。"
                : "桌面快捷方式已存在。";
            IsDesktopShortcutNotificationOpen = true;

            StatusMessage = result.CreatedNewShortcut
                ? $"已创建桌面快捷方式：{result.ShortcutPath}"
                : "桌面快捷方式已存在，无需重复创建。";
        }
        catch (Exception exception)
        {
            _logger.Log(LogLevel.Warning, "创建桌面快捷方式失败。", exception);
            DesktopShortcutNotificationSeverity = InfoBarSeverity.Error;
            DesktopShortcutNotificationMessage = "创建桌面快捷方式失败，请稍后重试。";
            IsDesktopShortcutNotificationOpen = true;
            StatusMessage = "创建桌面快捷方式失败，请稍后重试。";
        }
    }
}
