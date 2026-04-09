using System;
using System.ComponentModel;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using Microsoft.UI;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media.Animation;
using Vidvix.Core.Interfaces;
using Vidvix.Core.Models;
using Vidvix.ViewModels;
using Windows.ApplicationModel.DataTransfer;
using Windows.Graphics;
using Windows.UI;
using WinRT.Interop;

namespace Vidvix.Views;

public sealed partial class MainWindow : Window
{
    private const int MinimumWindowWidth = 1100;
    private const int MinimumWindowHeight = 720;
    private static readonly Color LightTitleBarBackgroundColor = ColorHelper.FromArgb(255, 243, 243, 243);
    private static readonly Color LightTitleBarForegroundColor = Colors.Black;
    private static readonly Color LightTitleBarButtonForegroundColor = ColorHelper.FromArgb(255, 96, 96, 96);
    private static readonly Color LightTitleBarButtonHoverForegroundColor = Colors.White;
    private static readonly Color LightTitleBarButtonPressedForegroundColor = Colors.White;
    private static readonly Color LightTitleBarInactiveForegroundColor = ColorHelper.FromArgb(255, 112, 112, 112);
    private static readonly Color LightTitleBarButtonHoverBackgroundColor = ColorHelper.FromArgb(255, 122, 122, 122);
    private static readonly Color LightTitleBarButtonPressedBackgroundColor = ColorHelper.FromArgb(255, 102, 102, 102);
    private static readonly Color DarkTitleBarBackgroundColor = ColorHelper.FromArgb(255, 31, 31, 31);
    private static readonly Color DarkTitleBarForegroundColor = Colors.White;
    private static readonly Color DarkTitleBarButtonForegroundColor = ColorHelper.FromArgb(255, 214, 214, 214);
    private static readonly Color DarkTitleBarButtonHoverForegroundColor = ColorHelper.FromArgb(255, 28, 28, 28);
    private static readonly Color DarkTitleBarButtonPressedForegroundColor = ColorHelper.FromArgb(255, 18, 18, 18);
    private static readonly Color DarkTitleBarInactiveForegroundColor = ColorHelper.FromArgb(255, 176, 176, 176);
    private static readonly Color DarkTitleBarButtonHoverBackgroundColor = ColorHelper.FromArgb(255, 224, 224, 224);
    private static readonly Color DarkTitleBarButtonPressedBackgroundColor = ColorHelper.FromArgb(255, 206, 206, 206);

    private readonly AppWindow _appWindow;
    private readonly ILogger _logger;
    private readonly IUserPreferencesService _userPreferencesService;
    private readonly IntPtr _windowHandle;
    private readonly Storyboard _showDetailOverlayStoryboard;
    private readonly Storyboard _hideDetailOverlayStoryboard;
    private readonly WindowPlacementPreference? _pendingInitialWindowPlacement;
    private WindowPlacementPreference? _trackedWindowPlacement;
    private bool _hasAppliedInitialWindowPlacement;
    private bool _isDetailOverlayVisible;

    public MainWindow(MainViewModel viewModel, IUserPreferencesService userPreferencesService, ILogger logger)
    {
        ViewModel = viewModel;
        _userPreferencesService = userPreferencesService ?? throw new ArgumentNullException(nameof(userPreferencesService));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        InitializeComponent();
        _windowHandle = WindowNative.GetWindowHandle(this);
        _appWindow = GetAppWindow();
        _showDetailOverlayStoryboard = CreateDetailOverlayStoryboard(isShowing: true);
        _hideDetailOverlayStoryboard = CreateDetailOverlayStoryboard(isShowing: false);
        ConfigureWindowConstraints();
        _pendingInitialWindowPlacement = LoadPendingInitialWindowPlacement();
        _appWindow.Changed += OnAppWindowChanged;
        RootLayout.ActualThemeChanged += OnRootLayoutActualThemeChanged;
        ViewModel.PropertyChanged += OnViewModelPropertyChanged;
        ViewModel.DetailPanel.PropertyChanged += OnDetailPanelPropertyChanged;
        _hideDetailOverlayStoryboard.Completed += OnHideDetailOverlayCompleted;
        Closed += OnClosed;
    }

    public MainViewModel ViewModel { get; }

    private void OnClosed(object sender, WindowEventArgs args)
    {
        SaveWindowPlacement();
        _appWindow.Changed -= OnAppWindowChanged;
        RootLayout.ActualThemeChanged -= OnRootLayoutActualThemeChanged;
        ViewModel.PropertyChanged -= OnViewModelPropertyChanged;
        ViewModel.DetailPanel.PropertyChanged -= OnDetailPanelPropertyChanged;
        _hideDetailOverlayStoryboard.Completed -= OnHideDetailOverlayCompleted;
        Closed -= OnClosed;
        ViewModel.Dispose();
    }

    private void OnRootLayoutLoaded(object sender, RoutedEventArgs e)
    {
        UpdateTitleBarColors();
        ApplyDetailOverlayState();
    }

    private void OnRootLayoutActualThemeChanged(FrameworkElement sender, object args) =>
        UpdateTitleBarColors();

    private void OnViewModelPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(MainViewModel.RequestedTheme))
        {
            UpdateTitleBarColors();
        }
    }

    private void OnDetailPanelPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(MediaDetailPanelViewModel.IsOpen))
        {
            ApplyDetailOverlayState();
        }
    }

    private void ApplyDetailOverlayState()
    {
        if (ViewModel.DetailPanel.IsOpen)
        {
            ShowDetailOverlay();
            return;
        }

        HideDetailOverlay();
    }

    private Storyboard CreateDetailOverlayStoryboard(bool isShowing)
    {
        var duration = new Duration(TimeSpan.FromMilliseconds(isShowing ? 240 : 220));
        var storyboard = new Storyboard();

        var translateAnimation = new DoubleAnimation
        {
            From = isShowing ? 300 : 0,
            To = isShowing ? 0 : 300,
            Duration = duration,
            EnableDependentAnimation = true,
            EasingFunction = new CubicEase
            {
                EasingMode = isShowing ? EasingMode.EaseOut : EasingMode.EaseIn
            }
        };

        Storyboard.SetTarget(translateAnimation, DetailOverlayTransform);
        Storyboard.SetTargetProperty(translateAnimation, "TranslateX");
        storyboard.Children.Add(translateAnimation);

        var opacityAnimation = new DoubleAnimation
        {
            From = isShowing ? 0 : 1,
            To = isShowing ? 1 : 0,
            Duration = duration
        };

        Storyboard.SetTarget(opacityAnimation, DetailOverlayPanel);
        Storyboard.SetTargetProperty(opacityAnimation, "Opacity");
        storyboard.Children.Add(opacityAnimation);

        return storyboard;
    }

    private void ShowDetailOverlay()
    {
        _hideDetailOverlayStoryboard.Stop();
        DetailOverlayPanel.Visibility = Visibility.Visible;
        DetailOverlayPanel.IsHitTestVisible = true;

        if (_isDetailOverlayVisible)
        {
            DetailOverlayPanel.Opacity = 1;
            DetailOverlayTransform.TranslateX = 0;
            return;
        }

        _isDetailOverlayVisible = true;
        DetailOverlayPanel.Opacity = 0;
        DetailOverlayTransform.TranslateX = 300;
        _showDetailOverlayStoryboard.Begin();
    }

    private void HideDetailOverlay()
    {
        _showDetailOverlayStoryboard.Stop();
        DetailOverlayPanel.IsHitTestVisible = false;

        if (!_isDetailOverlayVisible)
        {
            DetailOverlayPanel.Visibility = Visibility.Collapsed;
            DetailOverlayPanel.Opacity = 0;
            DetailOverlayTransform.TranslateX = 300;
            return;
        }

        _hideDetailOverlayStoryboard.Begin();
    }

    private void OnHideDetailOverlayCompleted(object? sender, object e)
    {
        if (ViewModel.DetailPanel.IsOpen)
        {
            return;
        }

        _isDetailOverlayVisible = false;
        DetailOverlayPanel.Visibility = Visibility.Collapsed;
        DetailOverlayPanel.Opacity = 0;
        DetailOverlayTransform.TranslateX = 300;
    }

    private void UpdateTitleBarColors()
    {
        if (!AppWindowTitleBar.IsCustomizationSupported())
        {
            return;
        }

        var isDarkTheme = ResolveIsDarkTheme();
        var backgroundColor = isDarkTheme ? DarkTitleBarBackgroundColor : LightTitleBarBackgroundColor;
        var foregroundColor = isDarkTheme ? DarkTitleBarForegroundColor : LightTitleBarForegroundColor;
        var buttonForegroundColor = isDarkTheme ? DarkTitleBarButtonForegroundColor : LightTitleBarButtonForegroundColor;
        var inactiveForegroundColor = isDarkTheme ? DarkTitleBarInactiveForegroundColor : LightTitleBarInactiveForegroundColor;
        var hoverForegroundColor = isDarkTheme ? DarkTitleBarButtonHoverForegroundColor : LightTitleBarButtonHoverForegroundColor;
        var hoverBackgroundColor = isDarkTheme ? DarkTitleBarButtonHoverBackgroundColor : LightTitleBarButtonHoverBackgroundColor;
        var pressedForegroundColor = isDarkTheme ? DarkTitleBarButtonPressedForegroundColor : LightTitleBarButtonPressedForegroundColor;
        var pressedBackgroundColor = isDarkTheme ? DarkTitleBarButtonPressedBackgroundColor : LightTitleBarButtonPressedBackgroundColor;

        var titleBar = _appWindow.TitleBar;
        titleBar.PreferredTheme = isDarkTheme ? TitleBarTheme.Dark : TitleBarTheme.Legacy;
        titleBar.BackgroundColor = backgroundColor;
        titleBar.ForegroundColor = foregroundColor;
        titleBar.InactiveBackgroundColor = backgroundColor;
        titleBar.InactiveForegroundColor = inactiveForegroundColor;
        titleBar.ButtonBackgroundColor = backgroundColor;
        titleBar.ButtonForegroundColor = buttonForegroundColor;
        titleBar.ButtonInactiveBackgroundColor = backgroundColor;
        titleBar.ButtonInactiveForegroundColor = inactiveForegroundColor;
        titleBar.ButtonHoverBackgroundColor = hoverBackgroundColor;
        titleBar.ButtonHoverForegroundColor = hoverForegroundColor;
        titleBar.ButtonPressedBackgroundColor = pressedBackgroundColor;
        titleBar.ButtonPressedForegroundColor = pressedForegroundColor;
    }

    private void ConfigureWindowConstraints()
    {
        if (_appWindow.Presenter is OverlappedPresenter overlappedPresenter)
        {
            overlappedPresenter.PreferredMinimumWidth = MinimumWindowWidth;
            overlappedPresenter.PreferredMinimumHeight = MinimumWindowHeight;
        }
    }

    private bool ResolveIsDarkTheme() => ViewModel.RequestedTheme switch
    {
        ElementTheme.Dark => true,
        ElementTheme.Light => false,
        _ => RootLayout.ActualTheme == ElementTheme.Dark
    };

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

    private void OnSettingsPaneClosed(object sender, object args)
    {
        ViewModel.HandleSettingsPaneClosed();
    }

    private AppWindow GetAppWindow()
    {
        var windowId = Win32Interop.GetWindowIdFromWindow(_windowHandle);
        return AppWindow.GetFromWindowId(windowId);
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

    public async Task EnsureInitialWindowPlacementAsync()
    {
        if (_hasAppliedInitialWindowPlacement)
        {
            return;
        }

        if (_pendingInitialWindowPlacement is null)
        {
            _hasAppliedInitialWindowPlacement = true;
            _trackedWindowPlacement ??= CaptureCurrentWindowPlacement();
            return;
        }

        await ApplyInitialWindowPlacementAsync(_pendingInitialWindowPlacement);
        _hasAppliedInitialWindowPlacement = true;
    }

    private async Task ApplyInitialWindowPlacementAsync(WindowPlacementPreference savedPlacement)
    {
        for (var attempt = 0; attempt < 10; attempt++)
        {
            await Task.Delay(400);

            if (!TryCreateVisibleRect(savedPlacement, out var targetRect))
            {
                continue;
            }

            ApplyWindowBounds(targetRect);
            await Task.Delay(150);

            var placement = CaptureCurrentWindowPlacement();
            if (placement is null)
            {
                continue;
            }

            _trackedWindowPlacement = placement;
            if (IsPlacementMatch(placement, targetRect))
            {
                SaveWindowPlacement();
                return;
            }
        }

        _trackedWindowPlacement ??= CaptureCurrentWindowPlacement();
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

            var existingPreferences = _userPreferencesService.Load();
            _userPreferencesService.Save(new UserPreferences
            {
                PreferredProcessingMode = existingPreferences.PreferredProcessingMode,
                PreferredOutputFormatExtension = existingPreferences.PreferredOutputFormatExtension,
                PreferredOutputDirectory = existingPreferences.PreferredOutputDirectory,
                ThemePreference = existingPreferences.ThemePreference,
                RevealOutputFileAfterProcessing = existingPreferences.RevealOutputFileAfterProcessing,
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

    private static bool IsPlacementMatch(WindowPlacementPreference placement, RectInt32 targetRect)
    {
        const int tolerance = 2;

        return Math.Abs(placement.X - targetRect.X) <= tolerance &&
               Math.Abs(placement.Y - targetRect.Y) <= tolerance &&
               Math.Abs(placement.Width - targetRect.Width) <= tolerance &&
               Math.Abs(placement.Height - targetRect.Height) <= tolerance;
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

    private void OnRootDragOver(object sender, DragEventArgs e)
    {
        if (!ViewModel.CanModifyInputs)
        {
            e.AcceptedOperation = DataPackageOperation.None;
            return;
        }

        if (!e.DataView.Contains(StandardDataFormats.StorageItems))
        {
            e.AcceptedOperation = DataPackageOperation.None;
            return;
        }

        e.AcceptedOperation = DataPackageOperation.Copy;
        e.DragUIOverride.Caption = "\u5bfc\u5165\u89c6\u9891\u6587\u4ef6\u6216\u6587\u4ef6\u5939";
        e.DragUIOverride.IsCaptionVisible = true;
        e.DragUIOverride.IsContentVisible = true;
        e.Handled = true;
    }

    private async void OnRootDrop(object sender, DragEventArgs e)
    {
        if (!ViewModel.CanModifyInputs || !e.DataView.Contains(StandardDataFormats.StorageItems))
        {
            return;
        }

        var storageItems = await e.DataView.GetStorageItemsAsync();
        var paths = storageItems
            .Where(item => !string.IsNullOrWhiteSpace(item.Path))
            .Select(item => item.Path)
            .ToArray();

        if (paths.Length == 0)
        {
            return;
        }

        await ViewModel.ImportPathsAsync(paths);
    }
}
