using System;
using System.IO;
using System.Runtime.InteropServices;
using Microsoft.UI.Xaml;
using Vidvix.Core.Interfaces;
using Vidvix.Core.Models;
using Vidvix.Utils;
using WinRT.Interop;

namespace Vidvix.Services;

public sealed class WindowIconService : IWindowIconService
{
    private const uint WmSetIcon = 0x0080;
    private const nuint IconSmall = 0;
    private const nuint IconBig = 1;
    private const uint ImageIcon = 1;
    private const uint LoadFromFile = 0x0010;
    private const uint DefaultSize = 0x0040;

    private readonly string _iconPath;
    private readonly ILogger _logger;

    public WindowIconService(ApplicationConfiguration configuration, ILogger logger)
    {
        ArgumentNullException.ThrowIfNull(configuration);

        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _iconPath = Path.GetFullPath(Path.Combine(ApplicationPaths.ExecutableDirectoryPath, configuration.ApplicationIconRelativePath));
    }

    public void ApplyIcon(Window window)
    {
        ArgumentNullException.ThrowIfNull(window);

        if (!File.Exists(_iconPath))
        {
            _logger.Log(LogLevel.Warning, $"Application icon file was not found: {_iconPath}.");
            return;
        }

        try
        {
            window.AppWindow.SetIcon(_iconPath);
            ApplyWin32IconFallback(window);
        }
        catch (Exception exception)
        {
            _logger.Log(LogLevel.Warning, "Failed to apply the application icon. The system default icon will be used.", exception);
        }
    }

    private void ApplyWin32IconFallback(Window window)
    {
        var windowHandle = WindowNative.GetWindowHandle(window);
        if (windowHandle == IntPtr.Zero)
        {
            return;
        }

        var iconHandle = LoadImage(
            IntPtr.Zero,
            _iconPath,
            ImageIcon,
            cx: 0,
            cy: 0,
            LoadFromFile | DefaultSize);

        if (iconHandle == IntPtr.Zero)
        {
            return;
        }

        SendMessage(windowHandle, WmSetIcon, IconSmall, iconHandle);
        SendMessage(windowHandle, WmSetIcon, IconBig, iconHandle);
    }

    [DllImport("user32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    private static extern IntPtr LoadImage(
        IntPtr hInst,
        string lpszName,
        uint uType,
        int cx,
        int cy,
        uint fuLoad);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern IntPtr SendMessage(
        IntPtr hWnd,
        uint msg,
        nuint wParam,
        IntPtr lParam);
}
