using System;
using System.Windows.Input;
using Microsoft.UI.Xaml;
using Vidvix.Core.Interfaces;
using Vidvix.Utils;

namespace Vidvix.ViewModels;

public sealed class AboutWorkspaceViewModel : ObservableObject
{
    private const string ApplicationName = "Vidvix";
    private const string ApplicationVersion = "1.2604.1.0";
    private const string ApplicationAuthor = "已逝情殇";
    private const string RepositoryUrl = "https://github.com/LPHYSQS/Vidvix";
    private const string AuthorEmail = "3261296352@qq.com";
    private const string WebsiteUrl = "https://lphysqs.github.io/VidvixWeb/";
    private static readonly Uri RepositoryUriValue = new(RepositoryUrl);
    private static readonly Uri AuthorEmailUriValue = new($"mailto:{AuthorEmail}");
    private static readonly Uri WebsiteUriValue = new(WebsiteUrl);
    private readonly ILocalizationService? _localizationService;
    private readonly RelayCommand _selectAboutSectionCommand;
    private readonly RelayCommand _selectLicenseSectionCommand;
    private readonly RelayCommand _selectPrivacySectionCommand;
    private AboutSectionKind _selectedSection = AboutSectionKind.About;

    public AboutWorkspaceViewModel()
        : this(null)
    {
    }

    public AboutWorkspaceViewModel(ILocalizationService? localizationService)
    {
        _localizationService = localizationService;
        _selectAboutSectionCommand = new RelayCommand(() => SelectSection(AboutSectionKind.About));
        _selectLicenseSectionCommand = new RelayCommand(() => SelectSection(AboutSectionKind.License));
        _selectPrivacySectionCommand = new RelayCommand(() => SelectSection(AboutSectionKind.Privacy));
    }

    public ICommand SelectAboutSectionCommand => _selectAboutSectionCommand;

    public ICommand SelectLicenseSectionCommand => _selectLicenseSectionCommand;

    public ICommand SelectPrivacySectionCommand => _selectPrivacySectionCommand;

    public bool IsAboutSectionSelected => _selectedSection == AboutSectionKind.About;

    public bool IsLicenseSectionSelected => _selectedSection == AboutSectionKind.License;

    public bool IsPrivacySectionSelected => _selectedSection == AboutSectionKind.Privacy;

    public Visibility AboutDetailsVisibility =>
        _selectedSection == AboutSectionKind.About ? Visibility.Visible : Visibility.Collapsed;

    public Visibility SectionPlaceholderVisibility =>
        _selectedSection == AboutSectionKind.About ? Visibility.Collapsed : Visibility.Visible;

    public string AboutSectionTabText =>
        GetLocalizedText("about.page.tab.about", "关于");

    public string LicenseSectionTabText =>
        GetLocalizedText("about.page.tab.license", "许可证");

    public string PrivacySectionTabText =>
        GetLocalizedText("about.page.tab.privacy", "隐私");

    public string SelectedSectionTitleText => _selectedSection switch
    {
        AboutSectionKind.License => GetLocalizedText("about.page.content.license.title", "许可证"),
        AboutSectionKind.Privacy => GetLocalizedText("about.page.content.privacy.title", "隐私"),
        _ => GetLocalizedText("about.page.content.about.title", "关于")
    };

    public string SelectedSectionDescriptionText => _selectedSection switch
    {
        AboutSectionKind.License => GetLocalizedText(
            "about.page.content.license.description",
            "这里将用于展示第三方组件及其许可证信息，后续内容会继续补充。"),
        AboutSectionKind.Privacy => GetLocalizedText(
            "about.page.content.privacy.description",
            "这里将用于展示隐私说明和数据使用注意事项，后续内容会继续补充。"),
        _ => GetLocalizedText(
            "about.page.content.about.description",
            "这里展示应用的基础信息与开源仓库地址。")
    };

    public string ApplicationNameLabelText =>
        GetLocalizedText("about.page.content.about.applicationName.label", "软件名称");

    public string ApplicationNameValueText => ApplicationName;

    public string ApplicationVersionLabelText =>
        GetLocalizedText("about.page.content.about.version.label", "版本号");

    public string ApplicationVersionValueText => ApplicationVersion;

    public string ApplicationAuthorLabelText =>
        GetLocalizedText("about.page.content.about.author.label", "作者");

    public string ApplicationAuthorValueText => ApplicationAuthor;

    public string RepositoryLabelText =>
        GetLocalizedText("about.page.content.about.repository.label", "开源地址");

    public string RepositoryUrlText => RepositoryUrl;

    public Uri RepositoryUri => RepositoryUriValue;

    public string ContactAuthorLabelText =>
        GetLocalizedText("about.page.content.about.contact.label", "联系作者与 Bug 反馈");

    public string AuthorEmailText => AuthorEmail;

    public Uri AuthorEmailUri => AuthorEmailUriValue;

    public string WebsiteLabelText =>
        GetLocalizedText("about.page.content.about.website.label", "软件官网");

    public string WebsiteUrlText => WebsiteUrl;

    public Uri WebsiteUri => WebsiteUriValue;

    public string CopyContextMenuText =>
        GetLocalizedText("about.page.action.copy", "复制");

    public void RefreshLocalization()
    {
        OnPropertyChanged(nameof(AboutSectionTabText));
        OnPropertyChanged(nameof(LicenseSectionTabText));
        OnPropertyChanged(nameof(PrivacySectionTabText));
        OnPropertyChanged(nameof(SelectedSectionTitleText));
        OnPropertyChanged(nameof(SelectedSectionDescriptionText));
        OnPropertyChanged(nameof(ApplicationNameLabelText));
        OnPropertyChanged(nameof(ApplicationVersionLabelText));
        OnPropertyChanged(nameof(ApplicationAuthorLabelText));
        OnPropertyChanged(nameof(RepositoryLabelText));
        OnPropertyChanged(nameof(ContactAuthorLabelText));
        OnPropertyChanged(nameof(WebsiteLabelText));
        OnPropertyChanged(nameof(CopyContextMenuText));
    }

    private void SelectSection(AboutSectionKind section)
    {
        if (_selectedSection == section)
        {
            return;
        }

        _selectedSection = section;
        OnPropertyChanged(nameof(IsAboutSectionSelected));
        OnPropertyChanged(nameof(IsLicenseSectionSelected));
        OnPropertyChanged(nameof(IsPrivacySectionSelected));
        OnPropertyChanged(nameof(AboutDetailsVisibility));
        OnPropertyChanged(nameof(SectionPlaceholderVisibility));
        OnPropertyChanged(nameof(SelectedSectionTitleText));
        OnPropertyChanged(nameof(SelectedSectionDescriptionText));
    }

    private string GetLocalizedText(string key, string fallback) =>
        _localizationService?.GetString(key, fallback) ?? fallback;

    private enum AboutSectionKind
    {
        About,
        License,
        Privacy
    }
}
