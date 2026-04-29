using System;
using System.Diagnostics;
using System.Runtime.InteropServices;
using Microsoft.UI.Windowing;
using Vidvix.Core.Models;

namespace Vidvix.Views;

public sealed partial class MainWindow
{
    private const uint WmClose = 0x0010;
    private const uint WmSysCommand = 0x0112;
    private const nuint ScClose = 0xF060;

    private void InstallCloseMessageHook()
    {
        if (!TrayNativeMethods.SetWindowSubclass(_windowHandle, _windowCloseSubclassProc, 1, IntPtr.Zero))
        {
            _logger.Log(LogLevel.Warning, "安装窗口关闭消息钩子失败，将回退到 AppWindow.Closing 关闭拦截。");
        }
    }

    private void RemoveCloseMessageHook()
    {
        TrayNativeMethods.RemoveWindowSubclass(_windowHandle, _windowCloseSubclassProc, 1);
    }

    private IntPtr OnWindowCloseSubclassProc(
        IntPtr windowHandle,
        uint message,
        IntPtr wParam,
        IntPtr lParam,
        nuint subclassId,
        IntPtr referenceData)
    {
        if (windowHandle == _windowHandle && ShouldHandleTrayCloseMessage(message, wParam))
        {
            _dispatcherQueue.TryEnqueue(HideWindowToTray);
            return IntPtr.Zero;
        }

        return TrayNativeMethods.DefSubclassProc(windowHandle, message, wParam, lParam);
    }

    private void InitializeSystemTray()
    {
        if (!_systemTrayAvailable)
        {
            return;
        }

        try
        {
            _systemTrayService.Initialize(ShowWindowFromTray, ExitApplicationFromTray);
            UpdateSystemTrayState();
        }
        catch (Exception exception)
        {
            _systemTrayAvailable = false;
            _logger.Log(LogLevel.Warning, "系统托盘初始化失败，已自动禁用托盘能力。", exception);
        }
    }

    private void UpdateSystemTrayState()
    {
        if (!_systemTrayAvailable)
        {
            return;
        }

        try
        {
            _systemTrayService.SetEnabled(ShouldEnableSystemTray());
        }
        catch (Exception exception)
        {
            _systemTrayAvailable = false;
            _logger.Log(LogLevel.Warning, "系统托盘状态更新失败，已自动禁用托盘能力。", exception);

            try
            {
                _systemTrayService.SetEnabled(false);
            }
            catch
            {
            }
        }
    }

    private void OnAppWindowClosing(AppWindow sender, AppWindowClosingEventArgs args)
    {
        if (!ShouldHideToSystemTrayOnClose())
        {
            return;
        }

        args.Cancel = true;
        _dispatcherQueue.TryEnqueue(HideWindowToTray);
    }

    private void HideWindowToTray()
    {
        SaveWindowPlacement();
        _appWindow.IsShownInSwitchers = false;
        _appWindow.Hide();
    }

    public void RestoreAndActivate()
    {
        _appWindow.IsShownInSwitchers = true;
        _appWindow.Show();
        TrayNativeMethods.ShowWindow(_windowHandle, TrayNativeMethods.SW_RESTORE);
        Activate();
        TrayNativeMethods.SetForegroundWindow(_windowHandle);
    }

    private void ShowWindowFromTray() => RestoreAndActivate();

    private void ExitApplicationFromTray()
    {
        _isExitRequested = true;
        _systemTrayService.SetEnabled(false);
        Close();
    }

    private bool ShouldHideToSystemTrayOnClose() =>
        !_isExitRequested &&
        ShouldEnableSystemTray();

    private bool ShouldHandleTrayCloseMessage(uint message, IntPtr wParam)
    {
        if (!ShouldHideToSystemTrayOnClose())
        {
            return false;
        }

        if (message == WmClose)
        {
            return true;
        }

        return message == WmSysCommand &&
               (((nuint)wParam & 0xFFF0u) == ScClose);
    }

    private static bool IsDebuggerSession() => Debugger.IsAttached;

    private bool ShouldEnableSystemTray() =>
        _systemTrayAvailable &&
        ViewModel.EnableSystemTray &&
        !IsDebuggerSession();

    private static class TrayNativeMethods
    {
        public const int SW_HIDE = 0;
        public const int SW_SHOW = 5;
        public const int SW_RESTORE = 9;

        [DllImport("user32.dll")]
        public static extern bool ShowWindow(IntPtr handle, int command);

        [DllImport("user32.dll")]
        public static extern bool SetForegroundWindow(IntPtr handle);

        public delegate IntPtr SubclassProc(
            IntPtr hWnd,
            uint uMsg,
            IntPtr wParam,
            IntPtr lParam,
            nuint uIdSubclass,
            IntPtr dwRefData);

        [DllImport("comctl32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        public static extern bool SetWindowSubclass(
            IntPtr hWnd,
            SubclassProc pfnSubclass,
            nuint uIdSubclass,
            IntPtr dwRefData);

        [DllImport("comctl32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        public static extern bool RemoveWindowSubclass(
            IntPtr hWnd,
            SubclassProc pfnSubclass,
            nuint uIdSubclass);

        [DllImport("comctl32.dll")]
        public static extern IntPtr DefSubclassProc(
            IntPtr hWnd,
            uint uMsg,
            IntPtr wParam,
            IntPtr lParam);
    }
}
