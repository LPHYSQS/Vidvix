using System;
using System.IO;
using Microsoft.UI.Xaml;
using Vidvix.Core.Interfaces;
using Vidvix.Core.Models;

namespace Vidvix.Services;

public sealed class WindowIconService : IWindowIconService
{
    private readonly string _iconPath;
    private readonly ILogger _logger;

    public WindowIconService(ApplicationConfiguration configuration, ILogger logger)
    {
        ArgumentNullException.ThrowIfNull(configuration);

        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _iconPath = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, configuration.ApplicationIconRelativePath));
    }

    public void ApplyIcon(Window window)
    {
        ArgumentNullException.ThrowIfNull(window);

        if (!File.Exists(_iconPath))
        {
            _logger.Log(LogLevel.Warning, $"未找到应用图标文件：{_iconPath}。");
            return;
        }

        try
        {
            window.AppWindow.SetIcon(_iconPath);
        }
        catch (Exception exception)
        {
            _logger.Log(LogLevel.Warning, "应用图标设置失败，将继续使用系统默认图标。", exception);
        }
    }
}
