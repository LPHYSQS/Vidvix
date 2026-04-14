using System;
using System.Runtime.InteropServices;
using Microsoft.UI.Windowing;
using Vidvix.Core.Models;
using Windows.Graphics;

namespace Vidvix.Views;

public sealed partial class MainWindow
{
    private void OnAppWindowChanged(AppWindow sender, AppWindowChangedEventArgs args)
    {
        if (!_hasAppliedInitialWindowPlacement || (!args.DidPositionChange && !args.DidSizeChange))
        {
            return;
        }

        var placement = CaptureCurrentWindowPlacement();
        if (placement is not null)
        {
            _trackedWindowPlacement = placement;
            SaveWindowPlacement();
        }
    }

    private WindowPlacementPreference? LoadPendingInitialWindowPlacement()
    {
        try
        {
            return _userPreferencesService.Load().MainWindowPlacement;
        }
        catch (Exception exception)
        {
            _logger.Log(LogLevel.Warning, "恢复窗口位置和大小失败，已回退到默认窗口显示。", exception);
            return null;
        }
    }

    public void ApplyInitialWindowPlacementBeforeActivate()
    {
        if (_hasAppliedInitialWindowPlacement)
        {
            return;
        }

        if (_pendingInitialWindowPlacement is null)
        {
            _hasAppliedInitialWindowPlacement = true;
            return;
        }

        try
        {
            if (!TryCreateVisibleRect(_pendingInitialWindowPlacement, out var targetRect))
            {
                _hasAppliedInitialWindowPlacement = true;
                return;
            }

            ApplyWindowBounds(targetRect);
            _trackedWindowPlacement = new WindowPlacementPreference
            {
                X = targetRect.X,
                Y = targetRect.Y,
                Width = targetRect.Width,
                Height = targetRect.Height
            };
            _hasAppliedInitialWindowPlacement = true;
        }
        catch (Exception exception)
        {
            _logger.Log(LogLevel.Warning, "启动前恢复窗口位置和大小失败，已回退到默认窗口显示。", exception);
            _hasAppliedInitialWindowPlacement = true;
        }
    }

    private void SaveWindowPlacement()
    {
        try
        {
            var placement = _trackedWindowPlacement ?? CaptureCurrentWindowPlacement();
            if (placement is null)
            {
                return;
            }

            _userPreferencesService.Update(existingPreferences => existingPreferences with
            {
                MainWindowPlacement = placement
            });
        }
        catch (Exception exception)
        {
            _logger.Log(LogLevel.Warning, "保存窗口位置和大小失败。", exception);
        }
    }

    private WindowPlacementPreference? CaptureCurrentWindowPlacement()
    {
        if (_appWindow.Presenter is OverlappedPresenter overlappedPresenter &&
            overlappedPresenter.State != OverlappedPresenterState.Restored)
        {
            return null;
        }

        if (!NativeMethods.GetWindowRect(_windowHandle, out var rect))
        {
            return null;
        }

        var width = rect.Right - rect.Left;
        var height = rect.Bottom - rect.Top;
        if (width <= 0 || height <= 0)
        {
            return null;
        }

        return new WindowPlacementPreference
        {
            X = rect.Left,
            Y = rect.Top,
            Width = width,
            Height = height
        };
    }

    private void ApplyWindowBounds(RectInt32 rect)
    {
        if (!NativeMethods.MoveWindow(_windowHandle, rect.X, rect.Y, rect.Width, rect.Height, true))
        {
            _appWindow.MoveAndResize(rect);
        }
    }

    private static bool TryCreateVisibleRect(WindowPlacementPreference placement, out RectInt32 rect)
    {
        rect = default;

        if (placement.Width <= 0 || placement.Height <= 0)
        {
            return false;
        }

        var requestedRect = new RectInt32(
            placement.X,
            placement.Y,
            Math.Max(MinimumWindowWidth, placement.Width),
            Math.Max(MinimumWindowHeight, placement.Height));

        var requestedNativeRect = new NativeMethods.RECT
        {
            Left = requestedRect.X,
            Top = requestedRect.Y,
            Right = requestedRect.X + requestedRect.Width,
            Bottom = requestedRect.Y + requestedRect.Height
        };

        var monitorHandle = NativeMethods.MonitorFromRect(ref requestedNativeRect, NativeMethods.MONITOR_DEFAULTTONULL);
        if (monitorHandle == IntPtr.Zero)
        {
            return false;
        }

        var monitorInfo = new NativeMethods.MONITORINFO
        {
            cbSize = Marshal.SizeOf<NativeMethods.MONITORINFO>()
        };

        if (!NativeMethods.GetMonitorInfo(monitorHandle, ref monitorInfo))
        {
            return false;
        }

        var workArea = monitorInfo.rcWork;
        var displayBounds = new RectInt32(
            workArea.Left,
            workArea.Top,
            workArea.Right - workArea.Left,
            workArea.Bottom - workArea.Top);
        var width = requestedRect.Width;
        var height = requestedRect.Height;

        if (displayBounds.Width >= MinimumWindowWidth)
        {
            width = Math.Min(width, displayBounds.Width);
        }

        if (displayBounds.Height >= MinimumWindowHeight)
        {
            height = Math.Min(height, displayBounds.Height);
        }

        var maxX = displayBounds.X + Math.Max(0, displayBounds.Width - width);
        var maxY = displayBounds.Y + Math.Max(0, displayBounds.Height - height);
        var x = Math.Clamp(requestedRect.X, displayBounds.X, maxX);
        var y = Math.Clamp(requestedRect.Y, displayBounds.Y, maxY);

        rect = new RectInt32(x, y, width, height);
        return true;
    }

    private static class NativeMethods
    {
        public const uint MONITOR_DEFAULTTONULL = 0x00000000;

        [StructLayout(LayoutKind.Sequential)]
        public struct RECT
        {
            public int Left;
            public int Top;
            public int Right;
            public int Bottom;
        }

        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Auto)]
        public struct MONITORINFO
        {
            public int cbSize;
            public RECT rcMonitor;
            public RECT rcWork;
            public uint dwFlags;
        }

        [DllImport("user32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        public static extern bool GetWindowRect(IntPtr hWnd, out RECT lpRect);

        [DllImport("user32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        public static extern bool MoveWindow(IntPtr hWnd, int x, int y, int nWidth, int nHeight, bool bRepaint);

        [DllImport("user32.dll", SetLastError = true)]
        public static extern IntPtr MonitorFromRect(ref RECT lprc, uint dwFlags);

        [DllImport("user32.dll", CharSet = CharSet.Auto, SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        public static extern bool GetMonitorInfo(IntPtr hMonitor, ref MONITORINFO lpmi);
    }
}
