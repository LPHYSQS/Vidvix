using System;
using System.Diagnostics;
using System.Runtime.InteropServices;
using Microsoft.UI.Windowing;

namespace Vidvix.Views;

public sealed partial class MainWindow
{
    private void InitializeSystemTray()
    {
        _systemTrayService.Initialize(ShowWindowFromTray, ExitApplicationFromTray);
        UpdateSystemTrayState();
    }

    private void UpdateSystemTrayState()
    {
        _systemTrayService.SetEnabled(ShouldEnableSystemTray());
    }

    private void OnAppWindowClosing(AppWindow sender, AppWindowClosingEventArgs args)
    {
        if (!ShouldHideToSystemTrayOnClose())
        {
            return;
        }

        args.Cancel = true;
        HideWindowToTray();
    }

    private void HideWindowToTray()
    {
        SaveWindowPlacement();
        _appWindow.IsShownInSwitchers = false;
        TrayNativeMethods.ShowWindow(_windowHandle, TrayNativeMethods.SW_HIDE);
    }

    private void ShowWindowFromTray()
    {
        _appWindow.IsShownInSwitchers = true;
        TrayNativeMethods.ShowWindow(_windowHandle, TrayNativeMethods.SW_SHOW);
        TrayNativeMethods.ShowWindow(_windowHandle, TrayNativeMethods.SW_RESTORE);
        Activate();
        TrayNativeMethods.SetForegroundWindow(_windowHandle);
    }

    private void ExitApplicationFromTray()
    {
        _isExitRequested = true;
        _systemTrayService.SetEnabled(false);
        Close();
    }

    private bool ShouldHideToSystemTrayOnClose() =>
        !_isExitRequested &&
        ShouldEnableSystemTray();

    private static bool IsDebuggerSession() => Debugger.IsAttached;

    private bool ShouldEnableSystemTray() =>
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
    }
}
