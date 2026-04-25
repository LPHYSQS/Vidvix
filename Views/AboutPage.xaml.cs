using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Vidvix.ViewModels;

namespace Vidvix.Views;

public sealed partial class AboutPage : Page
{
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
}
