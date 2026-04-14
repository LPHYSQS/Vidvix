using System;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Media.Animation;

namespace Vidvix.Views;

public sealed partial class MainWindow
{
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

    private Storyboard CreateCopyToastStoryboard(bool isShowing)
    {
        var duration = new Duration(TimeSpan.FromMilliseconds(isShowing ? 220 : 200));
        var storyboard = new Storyboard();

        var translateAnimation = new DoubleAnimation
        {
            From = isShowing ? -18 : 0,
            To = isShowing ? 0 : -18,
            Duration = duration,
            EnableDependentAnimation = true,
            EasingFunction = new CubicEase
            {
                EasingMode = isShowing ? EasingMode.EaseOut : EasingMode.EaseIn
            }
        };

        Storyboard.SetTarget(translateAnimation, CopyToastTransform);
        Storyboard.SetTargetProperty(translateAnimation, "TranslateY");
        storyboard.Children.Add(translateAnimation);

        var opacityAnimation = new DoubleAnimation
        {
            From = isShowing ? 0 : 1,
            To = isShowing ? 1 : 0,
            Duration = duration
        };

        Storyboard.SetTarget(opacityAnimation, CopyToastPanel);
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

    private void ShowCopyToast(string message)
    {
        CopyToastText.Text = message;
        _copyToastTimer.Stop();
        _hideCopyToastStoryboard.Stop();
        CopyToastPanel.Visibility = Visibility.Visible;

        if (_isCopyToastVisible)
        {
            CopyToastPanel.Opacity = 1;
            CopyToastTransform.TranslateY = 0;
        }
        else
        {
            _isCopyToastVisible = true;
            CopyToastPanel.Opacity = 0;
            CopyToastTransform.TranslateY = -18;
            _showCopyToastStoryboard.Begin();
        }

        _copyToastTimer.Start();
    }

    private void HideCopyToast()
    {
        _showCopyToastStoryboard.Stop();

        if (!_isCopyToastVisible)
        {
            CopyToastPanel.Visibility = Visibility.Collapsed;
            CopyToastPanel.Opacity = 0;
            CopyToastTransform.TranslateY = -18;
            return;
        }

        _hideCopyToastStoryboard.Begin();
    }

    private void OnCopyToastTimerTick(DispatcherQueueTimer sender, object args)
    {
        sender.Stop();
        HideCopyToast();
    }

    private void OnHideCopyToastCompleted(object? sender, object e)
    {
        if (_copyToastTimer.IsRunning)
        {
            return;
        }

        _isCopyToastVisible = false;
        CopyToastPanel.Visibility = Visibility.Collapsed;
        CopyToastPanel.Opacity = 0;
        CopyToastTransform.TranslateY = -18;
    }
}
