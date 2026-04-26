using System;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Windows.ApplicationModel.DataTransfer;
using Vidvix.ViewModels;

namespace Vidvix.Views;

public sealed partial class AboutPage : Page
{
    private TextBlock? _selectableTextCopySource;

    public AboutPage()
    {
        InitializeComponent();
        Loaded += OnLoaded;
        ActualThemeChanged += OnActualThemeChanged;
    }

    public AboutWorkspaceViewModel ViewModel
    {
        get => (AboutWorkspaceViewModel)GetValue(ViewModelProperty);
        set => SetValue(ViewModelProperty, value);
    }

    public static readonly DependencyProperty ViewModelProperty = DependencyProperty.Register(
        nameof(ViewModel),
        typeof(AboutWorkspaceViewModel),
        typeof(AboutPage),
        new PropertyMetadata(new AboutWorkspaceViewModel()));

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        ApplySectionButtonStyle();
    }

    private void OnActualThemeChanged(FrameworkElement sender, object args)
    {
        ApplySectionButtonStyle();
    }

    private void ApplySectionButtonStyle()
    {
        if (Resources["DefaultAboutSectionRadioButtonStyle"] is not Style defaultStyle ||
            Resources["LightAboutSectionRadioButtonStyle"] is not Style lightStyle)
        {
            return;
        }

        var targetStyle = ActualTheme == ElementTheme.Light
            ? lightStyle
            : defaultStyle;

        AboutSectionRadioButton.Style = targetStyle;
        LicenseSectionRadioButton.Style = targetStyle;
        PrivacySectionRadioButton.Style = targetStyle;
    }

    private void OnSelectableTextContextRequested(UIElement sender, ContextRequestedEventArgs args)
    {
        _selectableTextCopySource = sender as TextBlock;
    }

    private void OnCopySelectableTextClick(object sender, RoutedEventArgs e)
    {
        if (_selectableTextCopySource is null)
        {
            return;
        }

        if (!string.IsNullOrWhiteSpace(_selectableTextCopySource.SelectedText))
        {
            _selectableTextCopySource.CopySelectionToClipboard();
            return;
        }

        if (_selectableTextCopySource.Tag is not string fullText ||
            string.IsNullOrWhiteSpace(fullText))
        {
            return;
        }

        var package = new DataPackage();
        package.SetText(fullText);
        Clipboard.SetContent(package);
    }
}
