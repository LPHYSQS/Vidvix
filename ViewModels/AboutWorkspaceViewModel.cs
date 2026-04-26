using System;
using System.Collections.Generic;
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
    private const string FfmpegProjectUrl = "https://ffmpeg.org/";
    private const string MpvProjectUrl = "https://mpv.io/";
    private const string DemucsProjectUrl = "https://github.com/adefossez/demucs";
    private const string RifeProjectUrl = "https://github.com/nihui/rife-ncnn-vulkan";
    private const string RealEsrganRuntimeProjectUrl = "https://github.com/xinntao/Real-ESRGAN-ncnn-vulkan";
    private const string RealEsrganProjectUrl = "https://github.com/xinntao/Real-ESRGAN";

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

    public Visibility LicenseDetailsVisibility =>
        _selectedSection == AboutSectionKind.License ? Visibility.Visible : Visibility.Collapsed;

    public Visibility SectionPlaceholderVisibility =>
        _selectedSection == AboutSectionKind.Privacy ? Visibility.Visible : Visibility.Collapsed;

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
            "查看 Vidvix 本体、第三方组件与离线运行时资产的许可证信息。"),
        AboutSectionKind.Privacy => GetLocalizedText(
            "about.page.content.privacy.description",
            "这里将用于展示隐私说明和数据使用注意事项，后续内容会继续补充。"),
        _ => GetLocalizedText(
            "about.page.content.about.description",
            "在这里了解 Vidvix 的定位、核心能力，以及所致谢的开源项目。")
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

    public string ProductSummaryLabelText =>
        GetLocalizedText("about.page.content.about.summary.label", "产品概览");

    public string ProductSummaryTitleText =>
        GetLocalizedText("about.page.content.about.summary.title", "本地优先的离线媒体工作台");

    public string ProductSummaryDescriptionText =>
        GetLocalizedText(
            "about.page.content.about.summary.description",
            "Vidvix 将常用的本地媒体处理、AI 工作流与桌面端工具能力整合到同一套界面中，帮助你在尽量不依赖在线服务的前提下完成高频任务。");

    public string CapabilitySectionTitleText =>
        GetLocalizedText("about.page.content.about.capabilities.title", "核心能力");

    public string CapabilitySectionDescriptionText =>
        GetLocalizedText(
            "about.page.content.about.capabilities.description",
            "围绕高频媒体处理、AI 辅助与桌面效率体验，当前版本重点提供这些能力：");

    public IReadOnlyList<AboutSummaryItem> CapabilityItems => new[]
    {
        CreateSummaryItem(
            "about.page.content.about.capabilities.item.workspace.title",
            "多工作区协同",
            "about.page.content.about.capabilities.item.workspace.description",
            "视频、音频、裁剪、合并、AI、拆音与终端共享统一的输出规划、进度反馈和结果定位流程。"),
        CreateSummaryItem(
            "about.page.content.about.capabilities.item.localization.title",
            "双语与主题切换",
            "about.page.content.about.capabilities.item.localization.description",
            "界面支持简体中文与英语热切换，并提供跟随系统、浅色、深色三种外观偏好。"),
        CreateSummaryItem(
            "about.page.content.about.capabilities.item.offline.title",
            "离线优先",
            "about.page.content.about.capabilities.item.offline.description",
            "仓库内维护媒体与 AI 运行时资产，在条件满足时优先使用本地内置工具链，减少联网依赖。"),
        CreateSummaryItem(
            "about.page.content.about.capabilities.item.desktop.title",
            "桌面集成体验",
            "about.page.content.about.capabilities.item.desktop.description",
            "支持系统托盘、窗口状态记忆、快捷方式创建，以及媒体详情分区复制等桌面侧能力。")
    };

    public string OpenSourceSectionTitleText =>
        GetLocalizedText("about.page.content.about.openSource.title", "开源项目与工具鸣谢");

    public string OpenSourceSectionDescriptionText =>
        GetLocalizedText(
            "about.page.content.about.openSource.description",
            "Vidvix 的部分能力建立在这些优秀的开源项目之上，点击项目名称即可访问对应主页或仓库。");

    public IReadOnlyList<AboutOpenSourceItem> OpenSourceItems => new[]
    {
        CreateOpenSourceItem(
            "about.page.content.about.openSource.item.ffmpeg.title",
            "FFmpeg / FFprobe / FFplay",
            "about.page.content.about.openSource.item.ffmpeg.description",
            "负责媒体转换、信息探测、轨道提取，以及终端工作区中的受控命令执行。",
            FfmpegProjectUrl),
        CreateOpenSourceItem(
            "about.page.content.about.openSource.item.mpv.title",
            "mpv",
            "about.page.content.about.openSource.item.mpv.description",
            "用于媒体预览与嵌入式播放，为需要可视反馈的工作流提供支撑。",
            MpvProjectUrl),
        CreateOpenSourceItem(
            "about.page.content.about.openSource.item.demucs.title",
            "Demucs",
            "about.page.content.about.openSource.item.demucs.description",
            "为拆音工作区提供离线四轨音源分离能力。",
            DemucsProjectUrl),
        CreateOpenSourceItem(
            "about.page.content.about.openSource.item.rife.title",
            "RIFE NCNN Vulkan",
            "about.page.content.about.openSource.item.rife.description",
            "为 AI 补帧工作流提供离线插帧运行时支持。",
            RifeProjectUrl),
        CreateOpenSourceItem(
            "about.page.content.about.openSource.item.realesrgan.title",
            "Real-ESRGAN NCNN Vulkan",
            "about.page.content.about.openSource.item.realesrgan.description",
            "为 AI 增强工作流提供离线超分与增强运行时支持。",
            RealEsrganRuntimeProjectUrl)
    };

    public string LicenseSummaryLabelText =>
        GetLocalizedText("about.page.content.license.summary.label", "许可证概览");

    public string LicenseSummaryTitleText =>
        GetLocalizedText("about.page.content.license.summary.title", "软件本体与第三方组件许可证");

    public string LicenseSummaryDescriptionText =>
        GetLocalizedText(
            "about.page.content.license.summary.description",
            "本页汇总当前仓库快照中与 Vidvix 发行相关的许可证信息，重点覆盖软件本体、内置媒体与 AI 工具链，以及离线运行时包内保留的 notice 材料。");

    public string FirstPartyLicenseSectionTitleText =>
        GetLocalizedText("about.page.content.license.firstParty.title", "第一方组件");

    public string FirstPartyLicenseSectionDescriptionText =>
        GetLocalizedText(
            "about.page.content.license.firstParty.description",
            "以下内容适用于 Vidvix 项目自身源码与项目自有材料。");

    public IReadOnlyList<AboutLicenseItem> FirstPartyLicenseItems => new[]
    {
        CreateLicenseItem(
            "about.page.content.license.item.vidvix.title",
            "Vidvix 项目本体",
            "about.page.content.license.item.vidvix.version",
            "仓库源码 / 当前项目快照",
            "about.page.content.license.item.vidvix.license",
            "MIT",
            "about.page.content.license.item.vidvix.description",
            "适用于仓库中的原始源码与项目自有材料，不会替代或改写任何第三方运行时、模型或二进制的许可证。",
            "about.page.content.license.item.vidvix.notice",
            "仓库根目录 LICENSE 适用于 Vidvix 本体；第三方组件仍需分别遵守各自上游许可证。",
            RepositoryUrl)
    };

    public string ThirdPartyLicenseSectionTitleText =>
        GetLocalizedText("about.page.content.license.thirdParty.title", "第三方组件与运行时");

    public string ThirdPartyLicenseSectionDescriptionText =>
        GetLocalizedText(
            "about.page.content.license.thirdParty.description",
            "以下组件为当前仓库与离线分发流程中重点引用或随附的第三方项目。");

    public IReadOnlyList<AboutLicenseItem> ThirdPartyLicenseItems => new[]
    {
        CreateLicenseItem(
            "about.page.content.license.item.ffmpeg.title",
            "FFmpeg / FFprobe / FFplay",
            "about.page.content.license.item.ffmpeg.version",
            "2026-04-01-git-eedf8f0165-full_build-www.gyan.dev",
            "about.page.content.license.item.ffmpeg.license",
            "FFmpeg 上游项目为 LGPL 2.1+；若启用 GPL 组件则适用 GPL 2+",
            "about.page.content.license.item.ffmpeg.description",
            "负责媒体转换、媒体信息探测、轨道提取，以及终端工作区中的受控命令执行。",
            "about.page.content.license.item.ffmpeg.notice",
            "当前仓库快照保留 Tools/ffmpeg/LICENSE.txt（GNU LGPL v3 文本）；正式分发时应以实际随附 FFmpeg 构建的许可证材料为准。",
            FfmpegProjectUrl),
        CreateLicenseItem(
            "about.page.content.license.item.mpv.title",
            "mpv / libmpv",
            "about.page.content.license.item.mpv.version",
            "v0.41.0-459-gda4789c2d（mpv-1.dll 文件版本）",
            "about.page.content.license.item.mpv.license",
            "默认 GPL v2+；以非 GPL 配置构建时可为 LGPL 2.1+",
            "about.page.content.license.item.mpv.description",
            "用于预览与嵌入式播放，为裁剪与其他需要可视反馈的工作流提供底层播放能力。",
            "about.page.content.license.item.mpv.notice",
            "当前仓库快照包含 Tools/mpv/mpv-1.dll；正式发布时应保留与实际 shipped libmpv 构建对应的许可证文件。",
            MpvProjectUrl),
        CreateLicenseItem(
            "about.page.content.license.item.demucs.title",
            "Demucs",
            "about.page.content.license.item.demucs.version",
            "demucs 4.0.1；runtime lock 包含 Python 3.10.11、Torch 2.5.1、Torchaudio 2.5.1",
            "about.page.content.license.item.demucs.license",
            "MIT",
            "about.page.content.license.item.demucs.description",
            "为拆音工作区提供离线四轨音源分离能力，运行时通过仓库内的离线包与模型仓完成准备。",
            "about.page.content.license.item.demucs.notice",
            "Tools/Demucs/Packages/demucs-runtime-win-x64-*.zip 内含 Python、Torch、Torchaudio 等依赖的许可证与 notice 文件。",
            DemucsProjectUrl),
        CreateLicenseItem(
            "about.page.content.license.item.rife.title",
            "RIFE NCNN Vulkan",
            "about.page.content.license.item.rife.version",
            "20221029",
            "about.page.content.license.item.rife.license",
            "MIT",
            "about.page.content.license.item.rife.description",
            "为 AI 补帧工作流提供离线插帧运行时，当前仓库保留 `rife-v4.6` 首发模型与配置文件。",
            "about.page.content.license.item.rife.notice",
            "随附 notice 文件：Tools/AI/Licenses/rife-ncnn-vulkan-LICENSE.txt；来源与保留项记录于 Tools/AI/Manifests/rife.json。",
            RifeProjectUrl),
        CreateLicenseItem(
            "about.page.content.license.item.realesrganRuntime.title",
            "Real-ESRGAN NCNN Vulkan Runtime",
            "about.page.content.license.item.realesrganRuntime.version",
            "v0.2.5.0",
            "about.page.content.license.item.realesrganRuntime.license",
            "MIT",
            "about.page.content.license.item.realesrganRuntime.description",
            "为 AI 增强工作流提供离线超分与增强运行时，当前仓库保留 Standard 与 Anime 档位所需的执行文件与配置。",
            "about.page.content.license.item.realesrganRuntime.notice",
            "随附 notice 文件：Tools/AI/Licenses/realesrgan-ncnn-vulkan-LICENSE.txt；来源与保留项记录于 Tools/AI/Manifests/realesrgan.json。",
            RealEsrganRuntimeProjectUrl),
        CreateLicenseItem(
            "about.page.content.license.item.realesrganModel.title",
            "Real-ESRGAN Model Family",
            "about.page.content.license.item.realesrganModel.version",
            "realesrgan-x4plus / realesr-animevideov3-x2 / realesr-animevideov3-x4",
            "about.page.content.license.item.realesrganModel.license",
            "BSD-3-Clause",
            "about.page.content.license.item.realesrganModel.description",
            "对应 AI 增强工作流中当前首发保留的 Standard 与 Anime 模型集合。",
            "about.page.content.license.item.realesrganModel.notice",
            "随附 notice 文件：Tools/AI/Licenses/Real-ESRGAN-LICENSE.txt。",
            RealEsrganProjectUrl)
    };

    public string LicenseNotesSectionTitleText =>
        GetLocalizedText("about.page.content.license.notes.title", "分发与合规说明");

    public string LicenseNotesSectionDescriptionText =>
        GetLocalizedText(
            "about.page.content.license.notes.description",
            "除软件本体外，第三方组件仍以各自上游许可证为准；重新打包或替换运行时前应复核对应 notice 要求。");

    public IReadOnlyList<AboutSummaryItem> LicenseNoteItems => new[]
    {
        CreateSummaryItem(
            "about.page.content.license.notes.item.bundled.title",
            "仓库内随附的许可证材料",
            "about.page.content.license.notes.item.bundled.description",
            "当前仓库已保留根目录 LICENSE、Tools/ffmpeg/LICENSE.txt、Tools/AI/Licenses/*，Demucs runtime 包内也包含多项上游许可证文件。"),
        CreateSummaryItem(
            "about.page.content.license.notes.item.buildSpecific.title",
            "FFmpeg 与 mpv 的构建差异",
            "about.page.content.license.notes.item.buildSpecific.description",
            "这两类组件的最终义务可能随具体编译选项而变化；正式发布时应始终保留与实际 shipped binary 一致的许可证与 notice 文件。"),
        CreateSummaryItem(
            "about.page.content.license.notes.item.runtime.title",
            "离线运行时包",
            "about.page.content.license.notes.item.runtime.description",
            "Demucs 离线运行时包内含 Python、Torch、Torchaudio 等依赖；若你重新分发解压后的 runtime，请一并保留其附带许可证文件。")
    };

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
        OnPropertyChanged(nameof(ProductSummaryLabelText));
        OnPropertyChanged(nameof(ProductSummaryTitleText));
        OnPropertyChanged(nameof(ProductSummaryDescriptionText));
        OnPropertyChanged(nameof(CapabilitySectionTitleText));
        OnPropertyChanged(nameof(CapabilitySectionDescriptionText));
        OnPropertyChanged(nameof(CapabilityItems));
        OnPropertyChanged(nameof(OpenSourceSectionTitleText));
        OnPropertyChanged(nameof(OpenSourceSectionDescriptionText));
        OnPropertyChanged(nameof(OpenSourceItems));
        OnPropertyChanged(nameof(LicenseSummaryLabelText));
        OnPropertyChanged(nameof(LicenseSummaryTitleText));
        OnPropertyChanged(nameof(LicenseSummaryDescriptionText));
        OnPropertyChanged(nameof(FirstPartyLicenseSectionTitleText));
        OnPropertyChanged(nameof(FirstPartyLicenseSectionDescriptionText));
        OnPropertyChanged(nameof(FirstPartyLicenseItems));
        OnPropertyChanged(nameof(ThirdPartyLicenseSectionTitleText));
        OnPropertyChanged(nameof(ThirdPartyLicenseSectionDescriptionText));
        OnPropertyChanged(nameof(ThirdPartyLicenseItems));
        OnPropertyChanged(nameof(LicenseNotesSectionTitleText));
        OnPropertyChanged(nameof(LicenseNotesSectionDescriptionText));
        OnPropertyChanged(nameof(LicenseNoteItems));
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
        OnPropertyChanged(nameof(LicenseDetailsVisibility));
        OnPropertyChanged(nameof(SectionPlaceholderVisibility));
        OnPropertyChanged(nameof(SelectedSectionTitleText));
        OnPropertyChanged(nameof(SelectedSectionDescriptionText));
    }

    private string GetLocalizedText(string key, string fallback) =>
        _localizationService?.GetString(key, fallback) ?? fallback;

    private string FormatDetailLine(string labelKey, string labelFallback, string value) =>
        $"{GetLocalizedText(labelKey, labelFallback)}: {value}";

    private AboutSummaryItem CreateSummaryItem(
        string titleKey,
        string titleFallback,
        string descriptionKey,
        string descriptionFallback) =>
        new(
            GetLocalizedText(titleKey, titleFallback),
            GetLocalizedText(descriptionKey, descriptionFallback));

    private AboutOpenSourceItem CreateOpenSourceItem(
        string titleKey,
        string titleFallback,
        string descriptionKey,
        string descriptionFallback,
        string linkUrl) =>
        new(
            GetLocalizedText(titleKey, titleFallback),
            GetLocalizedText(descriptionKey, descriptionFallback),
            linkUrl);

    private AboutLicenseItem CreateLicenseItem(
        string titleKey,
        string titleFallback,
        string versionKey,
        string versionFallback,
        string licenseKey,
        string licenseFallback,
        string descriptionKey,
        string descriptionFallback,
        string noticeKey,
        string noticeFallback,
        string linkUrl) =>
        new(
            GetLocalizedText(titleKey, titleFallback),
            GetLocalizedText(descriptionKey, descriptionFallback),
            FormatDetailLine(
                "about.page.content.license.item.versionLabel",
                "版本 / 构建",
                GetLocalizedText(versionKey, versionFallback)),
            FormatDetailLine(
                "about.page.content.license.item.licenseLabel",
                "许可证",
                GetLocalizedText(licenseKey, licenseFallback)),
            FormatDetailLine(
                "about.page.content.license.item.noticeLabel",
                "随附 notice",
                GetLocalizedText(noticeKey, noticeFallback)),
            GetLocalizedText("about.page.content.license.item.sourceLabel", "项目地址"),
            linkUrl);

    private enum AboutSectionKind
    {
        About,
        License,
        Privacy
    }
}

public sealed class AboutSummaryItem
{
    public AboutSummaryItem(string title, string description)
    {
        Title = title;
        Description = description;
    }

    public string Title { get; }

    public string Description { get; }
}

public sealed class AboutOpenSourceItem
{
    public AboutOpenSourceItem(string title, string description, string linkUrl)
    {
        Title = title;
        Description = description;
        LinkUrl = linkUrl;
        LinkUri = new Uri(linkUrl);
    }

    public string Title { get; }

    public string Description { get; }

    public string LinkUrl { get; }

    public Uri LinkUri { get; }
}

public sealed class AboutLicenseItem
{
    public AboutLicenseItem(
        string title,
        string description,
        string versionDisplay,
        string licenseDisplay,
        string noticeDisplay,
        string sourceLabelText,
        string linkUrl)
    {
        Title = title;
        Description = description;
        VersionDisplay = versionDisplay;
        LicenseDisplay = licenseDisplay;
        NoticeDisplay = noticeDisplay;
        SourceLabelText = sourceLabelText;
        LinkUrl = linkUrl;
        LinkUri = new Uri(linkUrl);
    }

    public string Title { get; }

    public string Description { get; }

    public string VersionDisplay { get; }

    public string LicenseDisplay { get; }

    public string NoticeDisplay { get; }

    public string SourceLabelText { get; }

    public string LinkUrl { get; }

    public Uri LinkUri { get; }
}
