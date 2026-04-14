using System;
using System.ComponentModel;
using System.Linq;
using System.Runtime.InteropServices;
using Microsoft.UI;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
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
    private readonly Storyboard _showCopyToastStoryboard;
    private readonly Storyboard _hideCopyToastStoryboard;
    private readonly DispatcherQueueTimer _copyToastTimer;
    private readonly WindowPlacementPreference? _pendingInitialWindowPlacement;
    private WindowPlacementPreference? _trackedWindowPlacement;
    private bool _hasAppliedInitialWindowPlacement;
    private bool _isDetailOverlayVisible;
    private bool _isCopyToastVisible;

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
        _showCopyToastStoryboard = CreateCopyToastStoryboard(isShowing: true);
        _hideCopyToastStoryboard = CreateCopyToastStoryboard(isShowing: false);
        var dispatcherQueue = DispatcherQueue.GetForCurrentThread()
            ?? throw new InvalidOperationException("未找到当前窗口线程的调度队列。");
        _copyToastTimer = dispatcherQueue.CreateTimer();
        _copyToastTimer.Interval = TimeSpan.FromSeconds(1.6);
        _copyToastTimer.IsRepeating = false;
        ConfigureWindowConstraints();
        _pendingInitialWindowPlacement = LoadPendingInitialWindowPlacement();
        _appWindow.Changed += OnAppWindowChanged;
        RootLayout.ActualThemeChanged += OnRootLayoutActualThemeChanged;
        ViewModel.PropertyChanged += OnViewModelPropertyChanged;
        ViewModel.DetailPanel.PropertyChanged += OnDetailPanelPropertyChanged;
        ViewModel.TransientNotificationRequested += OnTransientNotificationRequested;
        _hideDetailOverlayStoryboard.Completed += OnHideDetailOverlayCompleted;
        _hideCopyToastStoryboard.Completed += OnHideCopyToastCompleted;
        _copyToastTimer.Tick += OnCopyToastTimerTick;
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
        ViewModel.TransientNotificationRequested -= OnTransientNotificationRequested;
        _hideDetailOverlayStoryboard.Completed -= OnHideDetailOverlayCompleted;
        _hideCopyToastStoryboard.Completed -= OnHideCopyToastCompleted;
        _copyToastTimer.Tick -= OnCopyToastTimerTick;
        _copyToastTimer.Stop();
        Closed -= OnClosed;
        ViewModel.Dispose();
    }

    private void OnRootLayoutLoaded(object sender, RoutedEventArgs e)
    {
        UpdateTitleBarColors();
        UpdateWorkspaceToggleVisuals();
        ApplyDetailOverlayState();
    }

    private void OnRootLayoutActualThemeChanged(FrameworkElement sender, object args) =>
        UpdateWindowChrome();

    private void OnTransientNotificationRequested(string message)
    {
        if (string.IsNullOrWhiteSpace(message))
        {
            return;
        }

        ShowCopyToast(message);
    }

    private void OnViewModelPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(MainViewModel.RequestedTheme))
        {
            UpdateTitleBarColors();
            return;
        }

        if (e.PropertyName == nameof(MainViewModel.IsVideoWorkspaceSelected) ||
            e.PropertyName == nameof(MainViewModel.IsAudioWorkspaceSelected) ||
            e.PropertyName == nameof(MainViewModel.IsTrimWorkspaceSelected))
        {
            UpdateWorkspaceToggleVisuals();
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

    private void OnSettingsPaneClosed(object sender, object args)
    {
        ViewModel.HandleSettingsPaneClosed();
    }

    private AppWindow GetAppWindow()
    {
        var windowId = Win32Interop.GetWindowIdFromWindow(_windowHandle);
        return AppWindow.GetFromWindowId(windowId);
    }

}
