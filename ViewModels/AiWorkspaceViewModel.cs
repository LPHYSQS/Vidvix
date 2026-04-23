using System;
using System.Collections.Generic;
using System.Linq;
using Vidvix.Core.Interfaces;
using Vidvix.Core.Models;
using Vidvix.Utils;

namespace Vidvix.ViewModels;

public sealed class AiWorkspaceViewModel : ObservableObject
{
    private static readonly IReadOnlyList<string> LaunchOutputFormats = new[] { "MP4", "MKV" };
    private readonly ApplicationConfiguration _configuration;
    private readonly ILocalizationService? _localizationService;

    public AiWorkspaceViewModel()
        : this(new ApplicationConfiguration(), localizationService: null)
    {
    }

    public AiWorkspaceViewModel(
        ApplicationConfiguration configuration,
        ILocalizationService? localizationService)
    {
        _configuration = configuration ?? throw new ArgumentNullException(nameof(configuration));
        _localizationService = localizationService;
    }

    public string SectionCaptionText =>
        GetLocalizedText("ai.page.caption", "AI 工作区");

    public string PageTitleText =>
        GetLocalizedText("ai.page.title", "AI 模块骨架已接入");

    public string PageDescriptionText =>
        GetLocalizedText(
            "ai.page.description",
            "当前轮次仅完成独立 AI 工作区、导航与页面骨架接线；后续轮次会继续补齐素材列表、模式状态、输出设置与离线执行链路。");

    public string MaterialsSectionTitleText =>
        GetLocalizedText("ai.page.materials.title", "素材列表");

    public string MaterialsSectionDescriptionText =>
        FormatLocalizedText(
            "ai.page.materials.description",
            $"后续轮次将在这里接入视频素材列表。首发冻结为视频-only 导入，支持格式：{BuildSupportedInputFormatsSummary()}；允许导入多个视频，但单次只处理一个当前视频。",
            ("formats", BuildSupportedInputFormatsSummary()));

    public string VideoOnlyImportBadgeText =>
        GetLocalizedText("ai.page.materials.badge.videoOnly", "视频-only 导入");

    public string SingleVideoExecutionBadgeText =>
        GetLocalizedText("ai.page.materials.badge.singleSelection", "单次单视频执行");

    public string MaterialsPlaceholderText =>
        GetLocalizedText(
            "ai.page.materials.placeholder",
            "R5 将在这里接入素材列表、选中态和导入反馈，本轮只先收口稳定壳层。");

    public string WorkspaceSectionTitleText =>
        GetLocalizedText("ai.page.workspace.title", "AI 工作区");

    public string WorkspaceSectionDescriptionText =>
        GetLocalizedText(
            "ai.page.workspace.description",
            "本轮只完成 AI补帧 / AI增强 的模式壳层和页面落点，不提前实现 runtime、参数区或推理执行。");

    public string OutputSectionTitleText =>
        GetLocalizedText("ai.page.output.title", "输出设置");

    public string OutputSectionDescriptionText =>
        GetLocalizedText(
            "ai.page.output.description",
            "本区已预留为 AI 输出设置。首发范围冻结为 MP4 / MKV，默认保留原音轨；具体输出状态与约束将在下一轮接入。");

    public string InterpolationCardTitleText =>
        GetLocalizedText("ai.interpolation.cardTitle", "AI补帧");

    public string InterpolationCardDescriptionText =>
        GetLocalizedText(
            "ai.interpolation.cardDescription",
            "首发路线冻结为 RIFE，后续将补齐素材输入、倍率参数、执行协调与取消控制。");

    public string InterpolationEngineBadgeText =>
        GetLocalizedText("ai.interpolation.engineBadge", "RIFE");

    public string InterpolationPlaceholderText =>
        GetLocalizedText(
            "ai.interpolation.placeholder",
            "R5 起再接入倍率、设备与执行协调，不在本轮提前打开推理链路。");

    public string EnhancementCardTitleText =>
        GetLocalizedText("ai.enhancement.cardTitle", "AI增强");

    public string EnhancementCardDescriptionText =>
        GetLocalizedText(
            "ai.enhancement.cardDescription",
            "首发路线冻结为 Real-ESRGAN，倍率范围冻结为 2x 到 16x；参数与工作流将在后续轮次增量接入。");

    public string EnhancementEngineBadgeText =>
        GetLocalizedText("ai.enhancement.engineBadge", "Real-ESRGAN");

    public string EnhancementPlaceholderText =>
        GetLocalizedText(
            "ai.enhancement.placeholder",
            "倍率冻结为 2x 到 16x，默认 2x；参数区与高倍率提醒在后续轮次增量接入。");

    public string OutputFormatsBadgeText =>
        FormatLocalizedText(
            "ai.page.output.badge.formats",
            $"输出：{BuildLaunchOutputFormatsSummary()}",
            ("formats", BuildLaunchOutputFormatsSummary()));

    public string OutputAudioBadgeText =>
        GetLocalizedText("ai.page.output.badge.audio", "默认保留原音轨");

    public string OutputPlaceholderText =>
        GetLocalizedText(
            "ai.page.output.placeholder",
            "R5 将补齐输出格式、目录与文件名状态，本轮只保留可承载真实控件的稳定面板。");

    public void RefreshLocalization()
    {
        OnPropertyChanged(nameof(SectionCaptionText));
        OnPropertyChanged(nameof(PageTitleText));
        OnPropertyChanged(nameof(PageDescriptionText));
        OnPropertyChanged(nameof(MaterialsSectionTitleText));
        OnPropertyChanged(nameof(MaterialsSectionDescriptionText));
        OnPropertyChanged(nameof(VideoOnlyImportBadgeText));
        OnPropertyChanged(nameof(SingleVideoExecutionBadgeText));
        OnPropertyChanged(nameof(MaterialsPlaceholderText));
        OnPropertyChanged(nameof(WorkspaceSectionTitleText));
        OnPropertyChanged(nameof(WorkspaceSectionDescriptionText));
        OnPropertyChanged(nameof(OutputSectionTitleText));
        OnPropertyChanged(nameof(OutputSectionDescriptionText));
        OnPropertyChanged(nameof(InterpolationCardTitleText));
        OnPropertyChanged(nameof(InterpolationCardDescriptionText));
        OnPropertyChanged(nameof(InterpolationEngineBadgeText));
        OnPropertyChanged(nameof(InterpolationPlaceholderText));
        OnPropertyChanged(nameof(EnhancementCardTitleText));
        OnPropertyChanged(nameof(EnhancementCardDescriptionText));
        OnPropertyChanged(nameof(EnhancementEngineBadgeText));
        OnPropertyChanged(nameof(EnhancementPlaceholderText));
        OnPropertyChanged(nameof(OutputFormatsBadgeText));
        OnPropertyChanged(nameof(OutputAudioBadgeText));
        OnPropertyChanged(nameof(OutputPlaceholderText));
    }

    private string GetLocalizedText(string key, string fallback) =>
        _localizationService?.GetString(key, fallback) ?? fallback;

    private string FormatLocalizedText(string key, string fallback, params (string Name, object? Value)[] arguments)
    {
        if (_localizationService is null || arguments.Length == 0)
        {
            return fallback;
        }

        var localizedArguments = new Dictionary<string, object?>(arguments.Length, StringComparer.Ordinal);
        foreach (var argument in arguments)
        {
            localizedArguments[argument.Name] = argument.Value;
        }

        return _localizationService.Format(key, localizedArguments, fallback);
    }

    private string BuildSupportedInputFormatsSummary()
    {
        var separator = _localizationService?.CurrentLanguage.StartsWith("zh", StringComparison.OrdinalIgnoreCase) == true
            ? "、"
            : ", ";

        return string.Join(
            separator,
            _configuration.SupportedAiInputFileTypes.Select(extension => extension.TrimStart('.').ToUpperInvariant()));
    }

    private static string BuildLaunchOutputFormatsSummary() =>
        string.Join(" / ", LaunchOutputFormats);
}
