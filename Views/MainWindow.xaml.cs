using System;
using System.Linq;
using Microsoft.UI;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Input;
using Vidvix.ViewModels;
using Windows.ApplicationModel.DataTransfer;
using Windows.UI;
using WinRT.Interop;

namespace Vidvix.Views;

public sealed partial class MainWindow : Window
{
    private const int MinimumWindowWidth = 1100;
    private const int MinimumWindowHeight = 720;
    private static readonly Color LightTitleBarBackgroundColor = ColorHelper.FromArgb(255, 243, 243, 243);
    private static readonly Color LightTitleBarForegroundColor = Colors.Black;
    private static readonly Color LightTitleBarInactiveForegroundColor = ColorHelper.FromArgb(255, 112, 112, 112);
    private static readonly Color LightTitleBarButtonHoverBackgroundColor = ColorHelper.FromArgb(28, 0, 0, 0);
    private static readonly Color LightTitleBarButtonPressedBackgroundColor = ColorHelper.FromArgb(54, 0, 0, 0);
    private static readonly Color DarkTitleBarBackgroundColor = ColorHelper.FromArgb(255, 31, 31, 31);
    private static readonly Color DarkTitleBarForegroundColor = Colors.White;
    private static readonly Color DarkTitleBarInactiveForegroundColor = ColorHelper.FromArgb(255, 176, 176, 176);
    private static readonly Color DarkTitleBarButtonHoverBackgroundColor = ColorHelper.FromArgb(34, 255, 255, 255);
    private static readonly Color DarkTitleBarButtonPressedBackgroundColor = ColorHelper.FromArgb(58, 255, 255, 255);

    private readonly AppWindow _appWindow;

    public MainWindow(MainViewModel viewModel)
    {
        ViewModel = viewModel;
        InitializeComponent();
        _appWindow = GetAppWindow();
        ConfigureWindowConstraints();
        RootLayout.ActualThemeChanged += OnRootLayoutActualThemeChanged;
        Closed += OnClosed;
    }

    public MainViewModel ViewModel { get; }

    private void OnClosed(object sender, WindowEventArgs args)
    {
        RootLayout.ActualThemeChanged -= OnRootLayoutActualThemeChanged;
        Closed -= OnClosed;
        ViewModel.Dispose();
    }

    private void OnRootLayoutLoaded(object sender, RoutedEventArgs e) =>
        UpdateTitleBarColors();

    private void OnRootLayoutActualThemeChanged(FrameworkElement sender, object args) =>
        UpdateTitleBarColors();

    private void UpdateTitleBarColors()
    {
        if (!AppWindowTitleBar.IsCustomizationSupported())
        {
            return;
        }

        var isDarkTheme = RootLayout.ActualTheme == ElementTheme.Dark;
        var backgroundColor = isDarkTheme ? DarkTitleBarBackgroundColor : LightTitleBarBackgroundColor;
        var foregroundColor = isDarkTheme ? DarkTitleBarForegroundColor : LightTitleBarForegroundColor;
        var inactiveForegroundColor = isDarkTheme ? DarkTitleBarInactiveForegroundColor : LightTitleBarInactiveForegroundColor;
        var hoverBackgroundColor = isDarkTheme ? DarkTitleBarButtonHoverBackgroundColor : LightTitleBarButtonHoverBackgroundColor;
        var pressedBackgroundColor = isDarkTheme ? DarkTitleBarButtonPressedBackgroundColor : LightTitleBarButtonPressedBackgroundColor;

        var titleBar = _appWindow.TitleBar;
        titleBar.BackgroundColor = backgroundColor;
        titleBar.ForegroundColor = foregroundColor;
        titleBar.InactiveBackgroundColor = backgroundColor;
        titleBar.InactiveForegroundColor = inactiveForegroundColor;
        titleBar.ButtonBackgroundColor = backgroundColor;
        titleBar.ButtonForegroundColor = foregroundColor;
        titleBar.ButtonInactiveBackgroundColor = backgroundColor;
        titleBar.ButtonInactiveForegroundColor = inactiveForegroundColor;
        titleBar.ButtonHoverBackgroundColor = hoverBackgroundColor;
        titleBar.ButtonHoverForegroundColor = foregroundColor;
        titleBar.ButtonPressedBackgroundColor = pressedBackgroundColor;
        titleBar.ButtonPressedForegroundColor = foregroundColor;
    }

    private void ConfigureWindowConstraints()
    {
        if (_appWindow.Presenter is OverlappedPresenter overlappedPresenter)
        {
            overlappedPresenter.PreferredMinimumWidth = MinimumWindowWidth;
            overlappedPresenter.PreferredMinimumHeight = MinimumWindowHeight;
        }
    }

    private AppWindow GetAppWindow()
    {
        var windowHandle = WindowNative.GetWindowHandle(this);
        var windowId = Win32Interop.GetWindowIdFromWindow(windowHandle);
        return AppWindow.GetFromWindowId(windowId);
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
        e.DragUIOverride.Caption = "导入视频文件或文件夹";
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