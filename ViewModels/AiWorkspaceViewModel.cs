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
        GetLocalizedText("ai.page.title", "AI 工作区");

    public string PageDescriptionText =>
        GetLocalizedText(
            "ai.page.description",
            "当前页面已对齐合并模块的三栏布局与模式切换结构，本轮只调整 AI 模块 UI，不接入任何 AI 或合并底层工作流。");

    public string MaterialsSectionTitleText =>
        GetLocalizedText("ai.page.materials.title", "素材列表");

    public string MaterialsSectionDescriptionText =>
        FormatLocalizedText(
            "ai.page.materials.description",
            $"参考合并模块改为纵向素材列表模板。首发仍冻结为视频-only 导入，支持格式：{BuildSupportedInputFormatsSummary()}；允许导入多个视频，但单次只处理一个当前视频。",
            ("formats", BuildSupportedInputFormatsSummary()));

    public string VideoOnlyImportBadgeText =>
        GetLocalizedText("ai.page.materials.badge.videoOnly", "视频-only 导入");

    public string SingleVideoExecutionBadgeText =>
        GetLocalizedText("ai.page.materials.badge.singleSelection", "单次单视频执行");

    public string MaterialsPlaceholderText =>
        GetLocalizedText(
            "ai.page.materials.placeholder",
            "这里将继续承接 AI 模块自己的素材导入、选中态和状态反馈，不复用合并模块的底层流程。");

    public string WorkspaceSectionTitleText =>
        GetLocalizedText("ai.page.workspace.title", "AI 工作区");

    public string WorkspaceSectionDescriptionText =>
        GetLocalizedText(
            "ai.page.workspace.description",
            "AI补帧 / AI增强 的工作区排版已对齐合并模块的模式切换结构，本轮只调整布局与控件摆位，不提前实现 runtime、参数区或推理执行。");

    public string OutputSectionTitleText =>
        GetLocalizedText("ai.page.output.title", "输出设置");

    public string OutputSectionDescriptionText =>
        GetLocalizedText(
            "ai.page.output.description",
            "输出设置先保留在右侧栏作为位置占位。首发范围仍冻结为 MP4 / MKV，默认保留原音轨；具体输出状态与约束后续再接 AI 模块自身逻辑。");

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
            "输出格式、目录与文件名区域当前只做 UI 承载，不接底层输出工作流。");

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
