using System.Windows.Input;
using Vidvix.Core.Interfaces;
using Vidvix.Utils;

namespace Vidvix.ViewModels;

public sealed class AboutWorkspaceViewModel : ObservableObject
{
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
            "这里将用于展示应用简介、版本信息和相关说明，后续内容会继续补充。")
    };

    public void RefreshLocalization()
    {
        OnPropertyChanged(nameof(AboutSectionTabText));
        OnPropertyChanged(nameof(LicenseSectionTabText));
        OnPropertyChanged(nameof(PrivacySectionTabText));
        OnPropertyChanged(nameof(SelectedSectionTitleText));
        OnPropertyChanged(nameof(SelectedSectionDescriptionText));
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
