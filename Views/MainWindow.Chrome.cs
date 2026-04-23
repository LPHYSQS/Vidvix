using System;
using Microsoft.UI;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Media;
using Windows.UI;

namespace Vidvix.Views;

public sealed partial class MainWindow
{
    private static readonly Color LightWorkspaceToggleForegroundColor = ColorHelper.FromArgb(255, 42, 42, 42);
    private static readonly Color DarkWorkspaceToggleForegroundColor = ColorHelper.FromArgb(255, 242, 242, 242);

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

    private void UpdateWindowChrome()
    {
        UpdateTitleBarColors();
        UpdateWorkspaceToggleVisuals();
    }

    private void UpdateWorkspaceToggleVisuals()
    {
        var accentBrush = ResolveBrushResource("AccentFillColorDefaultBrush");
        var defaultBrush = new SolidColorBrush(ResolveIsDarkTheme()
            ? DarkWorkspaceToggleForegroundColor
            : LightWorkspaceToggleForegroundColor);

        VideoWorkspaceIcon.Foreground = ViewModel.IsVideoWorkspaceSelected ? accentBrush : defaultBrush;
        VideoWorkspaceText.Foreground = ViewModel.IsVideoWorkspaceSelected ? accentBrush : defaultBrush;
        AudioWorkspaceIcon.Foreground = ViewModel.IsAudioWorkspaceSelected ? accentBrush : defaultBrush;
        AudioWorkspaceText.Foreground = ViewModel.IsAudioWorkspaceSelected ? accentBrush : defaultBrush;
        TrimWorkspaceIcon.Foreground = ViewModel.IsTrimWorkspaceSelected ? accentBrush : defaultBrush;
        TrimWorkspaceText.Foreground = ViewModel.IsTrimWorkspaceSelected ? accentBrush : defaultBrush;
        MergeWorkspaceIcon.Stroke = ViewModel.IsMergeWorkspaceSelected ? accentBrush : defaultBrush;
        MergeWorkspaceText.Foreground = ViewModel.IsMergeWorkspaceSelected ? accentBrush : defaultBrush;
        AiWorkspaceIcon.Stroke = ViewModel.IsAiWorkspaceSelected ? accentBrush : defaultBrush;
        AiWorkspaceText.Foreground = ViewModel.IsAiWorkspaceSelected ? accentBrush : defaultBrush;
        SplitAudioWorkspaceIcon.Stroke = ViewModel.IsSplitAudioWorkspaceSelected ? accentBrush : defaultBrush;
        SplitAudioWorkspaceText.Foreground = ViewModel.IsSplitAudioWorkspaceSelected ? accentBrush : defaultBrush;
        TerminalWorkspaceIcon.Stroke = ViewModel.IsTerminalWorkspaceSelected ? accentBrush : defaultBrush;
        TerminalWorkspaceText.Foreground = ViewModel.IsTerminalWorkspaceSelected ? accentBrush : defaultBrush;
    }

    private static Brush ResolveBrushResource(string resourceKey) =>
        Application.Current.Resources.TryGetValue(resourceKey, out var resource) && resource is Brush brush
            ? brush
            : throw new InvalidOperationException($"未找到界面画笔资源：{resourceKey}。");

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
}
