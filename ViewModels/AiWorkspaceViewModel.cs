using System;
using System.Collections.Generic;
using System.Linq;
using Vidvix.Core.Interfaces;
using Vidvix.Core.Models;
using Vidvix.Utils;

namespace Vidvix.ViewModels;

public sealed class AiWorkspaceViewModel : ObservableObject
{
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

    public string EnhancementCardTitleText =>
        GetLocalizedText("ai.enhancement.cardTitle", "AI增强");

    public string EnhancementCardDescriptionText =>
        GetLocalizedText(
            "ai.enhancement.cardDescription",
            "首发路线冻结为 Real-ESRGAN，倍率范围冻结为 2x 到 16x；参数与工作流将在后续轮次增量接入。");

    public void RefreshLocalization()
    {
        OnPropertyChanged(nameof(SectionCaptionText));
        OnPropertyChanged(nameof(PageTitleText));
        OnPropertyChanged(nameof(PageDescriptionText));
        OnPropertyChanged(nameof(MaterialsSectionTitleText));
        OnPropertyChanged(nameof(MaterialsSectionDescriptionText));
        OnPropertyChanged(nameof(WorkspaceSectionTitleText));
        OnPropertyChanged(nameof(WorkspaceSectionDescriptionText));
        OnPropertyChanged(nameof(OutputSectionTitleText));
        OnPropertyChanged(nameof(OutputSectionDescriptionText));
        OnPropertyChanged(nameof(InterpolationCardTitleText));
        OnPropertyChanged(nameof(InterpolationCardDescriptionText));
        OnPropertyChanged(nameof(EnhancementCardTitleText));
        OnPropertyChanged(nameof(EnhancementCardDescriptionText));
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
}
