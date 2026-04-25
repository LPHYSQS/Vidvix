using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.UI.Xaml;
using Vidvix.Core.Models;

namespace Vidvix.ViewModels;

public sealed partial class AiWorkspaceViewModel
{
    private const int EnhancementHighLoadWarningThreshold = 8;
    private readonly AiEnhancementExecutionCoordinator? _aiEnhancementExecutionCoordinator;
    private AiEnhancementResult? _lastEnhancementResult;
    private AiEnhancementFailureKind? _lastEnhancementFailureKind;
    private Func<string>? _lastEnhancementFailureReasonResolver;
    private AiExecutionFeedbackKind _lastEnhancementFeedbackKind;

    public AiEnhancementSettingsState EnhancementSettings { get; }

    public AiEnhancementExecutionState EnhancementExecution { get; }

    public Visibility EnhancementControlsVisibility =>
        ModeState.SelectedMode == AiWorkspaceMode.Enhancement
            ? Visibility.Visible
            : Visibility.Collapsed;

    public Visibility EnhancementHighLoadWarningVisibility =>
        ModeState.SelectedMode == AiWorkspaceMode.Enhancement &&
        EnhancementSettings.SelectedScaleFactorValue >= EnhancementHighLoadWarningThreshold
            ? Visibility.Visible
            : Visibility.Collapsed;

    public Visibility EnhancementProgressVisibility =>
        ModeState.SelectedMode == AiWorkspaceMode.Enhancement
            ? Visibility.Visible
            : Visibility.Collapsed;

    public string EnhancementSettingsTitleText =>
        GetLocalizedText("ai.enhancement.settings.title", "增强参数");

    public string EnhancementSettingsDescriptionText =>
        GetLocalizedText(
            "ai.enhancement.settings.description",
            "根据素材类型选择模型档位，并设置 2x 到 16x 的增强倍率。系统会按所选参数自动完成增强处理。");

    public string EnhancementModelTierTitleText =>
        GetLocalizedText("ai.enhancement.settings.modelTier.title", "模型档位");

    public string EnhancementModelTierHintText
    {
        get
        {
            var selectedOption = EnhancementSettings.SelectedModelTierOption;
            var nativeScales = string.Join(
                " / ",
                GetSelectedEnhancementNativeScales().Select(scale => $"{scale}x"));
            return FormatLocalizedText(
                "ai.enhancement.settings.modelTier.hint",
                $"{selectedOption.DisplayName} 档位：{selectedOption.Description} 当前底层原生倍率 {nativeScales}。",
                ("tier", selectedOption.DisplayName),
                ("description", selectedOption.Description),
                ("nativeScales", nativeScales));
        }
    }

    public string EnhancementScaleTitleText =>
        GetLocalizedText("ai.enhancement.settings.scale.title", "增强倍率");

    public string EnhancementScaleHintText =>
        FormatLocalizedText(
            "ai.enhancement.settings.scale.hint",
            "当前选择 {scale}，执行规划：{plan}。",
            ("scale", FormatEnhancementScale(EnhancementSettings.SelectedScaleFactorValue)),
            ("plan", BuildEnhancementScalePlanPreviewText()));

    public string EnhancementHighLoadTitleText =>
        GetLocalizedText("ai.enhancement.warning.highLoad.title", "高倍率高负载提醒");

    public string EnhancementHighLoadWarningText =>
        FormatLocalizedText(
            "ai.enhancement.warning.highLoad.body",
            "{scale} 已达到高负载阈值。机器负载、显存占用、耗时和失败风险都会明显上升，请优先使用更小样例先做验证。",
            ("scale", FormatEnhancementScale(EnhancementSettings.SelectedScaleFactorValue)));

    public string EnhancementProgressTitleText =>
        GetLocalizedText("ai.enhancement.progress.title", "增强进度与最近结果");

    public string EnhancementProgressPlaceholderText =>
        GetLocalizedText(
            "ai.enhancement.progress.placeholder",
            "开始增强后，这里会显示当前阶段、进度和最近一次输出结果。");

    public string LastEnhancementOutputLabelText =>
        GetLocalizedText("ai.enhancement.progress.lastOutput", "最近输出");

    public string EnhancementProgressStageTitleText =>
        string.IsNullOrWhiteSpace(EnhancementExecution.StageTitle)
            ? GetLocalizedText("ai.enhancement.progress.stage.idle", "待开始")
            : EnhancementExecution.StageTitle;

    public string EnhancementProgressDetailText =>
        string.IsNullOrWhiteSpace(EnhancementExecution.DetailText)
            ? EnhancementProgressPlaceholderText
            : EnhancementExecution.DetailText;

    public string EnhancementResultSummaryText =>
        string.IsNullOrWhiteSpace(EnhancementExecution.LastResultSummary)
            ? GetLocalizedText(
                "ai.enhancement.progress.result.empty",
                "最近还没有完成的增强输出。")
            : EnhancementExecution.LastResultSummary;

    private async Task StartEnhancementProcessingAsync()
    {
        if (_aiEnhancementExecutionCoordinator is null)
        {
            SetStatusText(
                "ai.status.enhancementUnavailable",
                "当前运行环境未接入 AI增强 工作流服务，暂时无法启动增强。");
            return;
        }

        if (!InputState.HasCurrentMaterial)
        {
            SetStatusText("ai.status.ready", "先导入一个或多个视频，再从素材列表中锁定当前处理对象。");
            return;
        }

        var progress = new Progress<AiEnhancementProgress>(update =>
        {
            RunOnUiThread(() =>
            {
                EnhancementExecution.ApplyProgress(update);
                RefreshEnhancementExecutionDisplay();
            });
        });

        using var cancellationSource = new CancellationTokenSource();
        _processingCancellationSource = cancellationSource;
        ResetEnhancementOutcomeTracking();
        var request = new AiEnhancementRequest(
            InputState.CurrentInputPath,
            OutputSettings.EffectiveOutputFileName,
            OutputSettings.SelectedOutputFormat,
            OutputSettings.EffectiveOutputDirectory,
            EnhancementSettings.SelectedModelTier,
            EnhancementSettings.SelectedScaleFactorValue,
            progress);

        EnhancementExecution.ResetForExecution(
            GetLocalizedText("ai.enhancement.progress.stage.prepare", "准备增强任务"),
            GetLocalizedText("ai.enhancement.progress.detail.prepare", "正在校验输入、运行时和输出路径…"));
        RefreshEnhancementExecutionDisplay();
        SetStatusText(() => CreateEnhancementStartedStatusMessage());

        try
        {
            IsProcessing = true;
            RefreshEnhancementModeProperties();

            var outcome = await _aiEnhancementExecutionCoordinator
                .ExecuteAsync(request, cancellationSource.Token)
                .ConfigureAwait(false);

            await RunOnUiThreadAsync(() =>
                {
                    switch (outcome.Kind)
                    {
                        case AiEnhancementExecutionOutcomeKind.Succeeded:
                            ApplyEnhancementSuccessOutcome(outcome.Result!);
                            break;
                        case AiEnhancementExecutionOutcomeKind.Cancelled:
                            ApplyEnhancementCancelledOutcome();
                            break;
                        default:
                            ApplyEnhancementFailureOutcome(
                                outcome.FailureKind,
                                outcome.FailureReasonResolver ?? (() => GetEnhancementGenericFailureReason()));
                            break;
                    }
                })
                .ConfigureAwait(false);
        }
        finally
        {
            await RunOnUiThreadAsync(() =>
                {
                    IsProcessing = false;
                    _processingCancellationSource = null;
                    RefreshEnhancementModeProperties();
                    NotifyCommandStates();
                })
                .ConfigureAwait(false);
        }
    }

    private void OnEnhancementSettingsPropertyChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        OnPropertyChanged(nameof(EnhancementModelTierHintText));
        OnPropertyChanged(nameof(EnhancementScaleHintText));
        OnPropertyChanged(nameof(EnhancementHighLoadWarningVisibility));
        OnPropertyChanged(nameof(EnhancementHighLoadWarningText));
        OnPropertyChanged(nameof(OutputParameterSummaryText));
    }

    private void RefreshEnhancementLocalization()
    {
        EnhancementSettings.ReloadLocalizedOptions(
            BuildEnhancementModelTierOptions(),
            BuildEnhancementScaleOptions());

        OnPropertyChanged(nameof(EnhancementControlsVisibility));
        OnPropertyChanged(nameof(EnhancementHighLoadWarningVisibility));
        OnPropertyChanged(nameof(EnhancementProgressVisibility));
        OnPropertyChanged(nameof(EnhancementSettingsTitleText));
        OnPropertyChanged(nameof(EnhancementSettingsDescriptionText));
        OnPropertyChanged(nameof(EnhancementModelTierTitleText));
        OnPropertyChanged(nameof(EnhancementModelTierHintText));
        OnPropertyChanged(nameof(EnhancementScaleTitleText));
        OnPropertyChanged(nameof(EnhancementScaleHintText));
        OnPropertyChanged(nameof(EnhancementHighLoadTitleText));
        OnPropertyChanged(nameof(EnhancementHighLoadWarningText));
        OnPropertyChanged(nameof(EnhancementProgressTitleText));
        OnPropertyChanged(nameof(EnhancementProgressPlaceholderText));
        OnPropertyChanged(nameof(LastEnhancementOutputLabelText));
        RefreshEnhancementExecutionDisplay();
    }

    private void RefreshEnhancementModeProperties()
    {
        OnPropertyChanged(nameof(CanStartProcessing));
        OnPropertyChanged(nameof(EnhancementControlsVisibility));
        OnPropertyChanged(nameof(EnhancementHighLoadWarningVisibility));
        OnPropertyChanged(nameof(EnhancementProgressVisibility));
        OnPropertyChanged(nameof(EnhancementModelTierHintText));
        OnPropertyChanged(nameof(EnhancementScaleHintText));
        OnPropertyChanged(nameof(EnhancementHighLoadWarningText));
        _startProcessingCommand.NotifyCanExecuteChanged();
        _cancelProcessingCommand.NotifyCanExecuteChanged();
    }

    private IReadOnlyList<AiEnhancementModelTierOption> BuildEnhancementModelTierOptions() =>
        new[]
        {
            new AiEnhancementModelTierOption(
                AiEnhancementModelTier.Standard,
                GetLocalizedText("ai.enhancement.modelTierOption.standard", "Standard"),
                GetLocalizedText(
                    "ai.enhancement.modelTierOption.standard.description",
                    "优先使用 realesrgan-x4plus，原生 4x。")),
            new AiEnhancementModelTierOption(
                AiEnhancementModelTier.Anime,
                GetLocalizedText("ai.enhancement.modelTierOption.anime", "Anime"),
                GetLocalizedText(
                    "ai.enhancement.modelTierOption.anime.description",
                    "优先使用 realesr-animevideov3，原生 2x / 4x。"))
        };

    private IReadOnlyList<AiEnhancementScaleOption> BuildEnhancementScaleOptions()
    {
        var options = new List<AiEnhancementScaleOption>(15);
        for (var scale = 2; scale <= 16; scale++)
        {
            var isHighLoad = scale >= EnhancementHighLoadWarningThreshold;
            options.Add(
                new AiEnhancementScaleOption(
                    scale,
                    $"{scale}x",
                    isHighLoad
                        ? GetLocalizedText(
                            "ai.enhancement.scaleOption.highLoad.description",
                            "高负载档位，建议先做小样例验证。")
                        : GetLocalizedText(
                            "ai.enhancement.scaleOption.standard.description",
                            "标准档位，可直接沿增强规划执行。")));
        }

        return options;
    }

    private string BuildEnhancementScalePlanPreviewText()
    {
        var plan = AiEnhancementScalePlanner.BuildPlan(
            GetSelectedEnhancementNativeScales(),
            EnhancementSettings.SelectedScaleFactorValue);
        var passSummary = string.Join(" -> ", plan.PassScales.Select(FormatEnhancementScale));
        if (plan.RequiresDownscale)
        {
            return FormatLocalizedText(
                "ai.enhancement.settings.scale.plan.overscale",
                "先按 {passes} 放大到 {upscale}，再回缩到 {target}",
                ("passes", passSummary),
                ("upscale", FormatEnhancementScale(plan.AchievedScale)),
                ("target", FormatEnhancementScale(plan.RequestedScale)));
        }

        return plan.PassCount == 1
            ? FormatLocalizedText(
                "ai.enhancement.settings.scale.plan.direct",
                "直接执行 {scale}",
                ("scale", passSummary))
            : FormatLocalizedText(
                "ai.enhancement.settings.scale.plan.multiPass",
                "按 {passes} 逐级组合放大",
                ("passes", passSummary));
    }

    private string BuildEnhancementParameterSummaryText()
    {
        var plan = AiEnhancementScalePlanner.BuildPlan(
            GetSelectedEnhancementNativeScales(),
            EnhancementSettings.SelectedScaleFactorValue);
        return FormatLocalizedText(
            "ai.page.output.summary.enhancement",
            "当前参数状态：模式 {mode}，输入 {input}，档位 {tier}，倍率 {scale}，规划 {plan}，输出 {format}，文件名 {fileName}，原音轨默认跟随源文件。",
            ("mode", EnhancementModeLabelText),
            ("input", GetCurrentInputSummaryText()),
            ("tier", EnhancementSettings.SelectedModelTierOption.DisplayName),
            ("scale", FormatEnhancementScale(EnhancementSettings.SelectedScaleFactorValue)),
            ("plan", BuildEnhancementScalePlanPreviewText()),
            ("format", OutputSettings.SelectedOutputFormat.DisplayName),
            ("fileName", OutputSettings.EffectiveOutputFileName));
    }

    private IReadOnlyList<int> GetSelectedEnhancementNativeScales()
    {
        var runtimeModel = GetSelectedEnhancementRuntimeModel();
        if (runtimeModel is not null && runtimeModel.NativeScaleFactors.Count > 0)
        {
            return runtimeModel.NativeScaleFactors;
        }

        return EnhancementSettings.SelectedModelTier == AiEnhancementModelTier.Standard
            ? new[] { 4 }
            : new[] { 2, 4 };
    }

    private AiRuntimeModelDescriptor? GetSelectedEnhancementRuntimeModel()
    {
        var descriptor = _runtimeCatalog?.RealEsrgan;
        if (descriptor is null)
        {
            return null;
        }

        var runtimeModelId = EnhancementSettings.SelectedModelTier == AiEnhancementModelTier.Standard
            ? "standard"
            : "anime";
        return descriptor.Models.FirstOrDefault(model =>
            string.Equals(model.Id, runtimeModelId, StringComparison.OrdinalIgnoreCase));
    }

    private string CreateEnhancementStartedStatusMessage() =>
        FormatLocalizedText(
            "ai.status.enhancementStarted",
            "已开始 AI增强：{fileName}，档位 {tier}，目标倍率 {scale}。",
            ("fileName", InputState.CurrentInputFileName),
            ("tier", EnhancementSettings.SelectedModelTierOption.DisplayName),
            ("scale", FormatEnhancementScale(EnhancementSettings.SelectedScaleFactorValue)));

    private string CreateEnhancementCompletedStatusMessage(AiEnhancementResult result) =>
        FormatLocalizedText(
            "ai.status.enhancementCompleted",
            "AI增强已完成：{fileName} -> {outputFileName}",
            ("fileName", Path.GetFileName(result.InputPath)),
            ("outputFileName", result.OutputFileName));

    private string CreateEnhancementSuccessSummary(AiEnhancementResult result)
    {
        var route = result.ScalePlan.RequiresDownscale
            ? FormatLocalizedText(
                "ai.enhancement.progress.result.route.overscale",
                "先到 {upscale} 再回缩到 {target}",
                ("upscale", FormatEnhancementScale(result.ScalePlan.AchievedScale)),
                ("target", FormatEnhancementScale(result.ScalePlan.RequestedScale)))
            : result.ScalePlan.PassCount == 1
                ? FormatLocalizedText(
                    "ai.enhancement.progress.result.route.direct",
                    "原生 {scale}",
                    ("scale", FormatEnhancementScale(result.ScalePlan.RequestedScale)))
                : FormatLocalizedText(
                    "ai.enhancement.progress.result.route.multiPass",
                    "组合 {passes}",
                    ("passes", string.Join(" -> ", result.ScalePlan.PassScales.Select(FormatEnhancementScale))));

        return FormatLocalizedText(
            "ai.enhancement.progress.result.success",
            "最近一次增强已完成：{tier}、{scale}、{route}、{device}，输出 {outputFileName}",
            ("tier", result.ModelDisplayName),
            ("scale", FormatEnhancementScale(result.ScalePlan.RequestedScale)),
            ("route", route),
            ("device", result.ExecutionDeviceDisplayName),
            ("outputFileName", result.OutputFileName));
    }

    private string CreateEnhancementFailedSummary(string failureReason, AiEnhancementFailureKind? failureKind)
    {
        var kindText = failureKind switch
        {
            AiEnhancementFailureKind.RuntimeMissing => GetLocalizedText("ai.enhancement.failureKind.runtimeMissing", "runtime 缺失"),
            AiEnhancementFailureKind.DeviceUnavailable => GetLocalizedText("ai.enhancement.failureKind.deviceUnavailable", "设备不可用"),
            AiEnhancementFailureKind.InvalidInput => GetLocalizedText("ai.enhancement.failureKind.invalidInput", "输入无效"),
            _ => GetLocalizedText("ai.enhancement.failureKind.executionFailed", "执行失败")
        };

        return FormatLocalizedText(
            "ai.status.enhancementFailed",
            "AI增强失败（{kind}）：{reason}",
            ("kind", kindText),
            ("reason", failureReason));
    }

    private string GetEnhancementCancelledStatusMessage() =>
        GetLocalizedText("ai.status.enhancementCancelled", "已取消当前 AI增强 任务，临时目录已开始清理。");

    private string GetEnhancementCancelledSummaryText() =>
        GetLocalizedText("ai.enhancement.progress.result.cancelled", "最近一次增强任务已取消。");

    private string GetEnhancementCancellingStatusMessage() =>
        GetLocalizedText("ai.status.enhancementCancelling", "正在取消 AI增强 任务并清理临时目录…");

    private string GetEnhancementUnavailableStatusMessage() =>
        GetLocalizedText(
            "ai.status.enhancementUnavailable",
            "当前运行环境未接入 AI增强 工作流服务，暂时无法启动增强。");

    private string GetEnhancementGenericFailureReason() =>
        GetLocalizedText("ai.enhancement.failure.unexpected", "增强执行失败，请重试。");

    private static string FormatEnhancementScale(int scaleFactor) =>
        scaleFactor.ToString(CultureInfo.InvariantCulture) + "x";

    private void ResetEnhancementOutcomeTracking()
    {
        _lastEnhancementFailureReasonResolver = null;
        _lastEnhancementFailureKind = null;
        _lastEnhancementFeedbackKind = AiExecutionFeedbackKind.None;
    }

    private void ApplyEnhancementSuccessOutcome(AiEnhancementResult result)
    {
        ArgumentNullException.ThrowIfNull(result);

        _lastEnhancementResult = result;
        _lastEnhancementFailureReasonResolver = null;
        _lastEnhancementFailureKind = null;
        _lastEnhancementFeedbackKind = AiExecutionFeedbackKind.Succeeded;

        EnhancementExecution.ApplySuccess(CreateEnhancementSuccessSummary(result), result.OutputPath);
        EnhancementExecution.StageTitle = GetLocalizedText("ai.enhancement.progress.stage.complete", "增强完成");
        EnhancementExecution.DetailText = FormatLocalizedText(
            "ai.enhancement.progress.detail.complete",
            "输出文件已生成：{fileName}",
            ("fileName", result.OutputFileName));

        SetStatusText(() => CreateEnhancementCompletedStatusMessage(result));
        RefreshEnhancementExecutionDisplay();
    }

    private void ApplyEnhancementCancelledOutcome()
    {
        _lastEnhancementFailureReasonResolver = null;
        _lastEnhancementFailureKind = null;
        _lastEnhancementFeedbackKind = AiExecutionFeedbackKind.Cancelled;

        var cancelledSummary = GetEnhancementCancelledSummaryText();
        EnhancementExecution.ApplyFailure(cancelledSummary);
        EnhancementExecution.StageTitle = GetLocalizedText("ai.enhancement.progress.stage.cancelled", "增强已取消");
        EnhancementExecution.DetailText = cancelledSummary;

        SetStatusText("ai.status.enhancementCancelled", "已取消当前 AI增强 任务，临时目录已开始清理。");
        RefreshEnhancementExecutionDisplay();
    }

    private void ApplyEnhancementFailureOutcome(
        AiEnhancementFailureKind? failureKind,
        Func<string> failureReasonResolver)
    {
        ArgumentNullException.ThrowIfNull(failureReasonResolver);

        _lastEnhancementFailureReasonResolver = failureReasonResolver;
        _lastEnhancementFailureKind = failureKind;
        _lastEnhancementFeedbackKind = AiExecutionFeedbackKind.Failed;

        var failureReason = GetCurrentEnhancementFailureReasonText();
        EnhancementExecution.ApplyFailure(CreateEnhancementFailedSummary(failureReason, failureKind));
        EnhancementExecution.StageTitle = GetLocalizedText("ai.enhancement.progress.stage.failed", "增强失败");
        EnhancementExecution.DetailText = failureReason;

        SetStatusText(() => CreateEnhancementFailedSummary(GetCurrentEnhancementFailureReasonText(), _lastEnhancementFailureKind));
        RefreshEnhancementExecutionDisplay();
    }

    private string GetCurrentEnhancementFailureReasonText() =>
        NormalizeErrorMessage(
            _lastEnhancementFailureReasonResolver?.Invoke(),
            "ai.enhancement.failure.unexpected",
            "增强执行失败，请重试。");

    private void RefreshEnhancementOutcomeLocalization()
    {
        if (_lastEnhancementFeedbackKind == AiExecutionFeedbackKind.None)
        {
            return;
        }

        switch (_lastEnhancementFeedbackKind)
        {
            case AiExecutionFeedbackKind.Succeeded when _lastEnhancementResult is not null:
                EnhancementExecution.StageTitle = GetLocalizedText("ai.enhancement.progress.stage.complete", "增强完成");
                EnhancementExecution.DetailText = FormatLocalizedText(
                    "ai.enhancement.progress.detail.complete",
                    "输出文件已生成：{fileName}",
                    ("fileName", _lastEnhancementResult.OutputFileName));
                EnhancementExecution.LastResultSummary = CreateEnhancementSuccessSummary(_lastEnhancementResult);
                break;
            case AiExecutionFeedbackKind.Cancelled:
                var cancelledSummary = GetEnhancementCancelledSummaryText();
                EnhancementExecution.StageTitle = GetLocalizedText("ai.enhancement.progress.stage.cancelled", "增强已取消");
                EnhancementExecution.DetailText = cancelledSummary;
                EnhancementExecution.LastResultSummary = cancelledSummary;
                break;
            case AiExecutionFeedbackKind.Failed:
                var failureReason = GetCurrentEnhancementFailureReasonText();
                EnhancementExecution.StageTitle = GetLocalizedText("ai.enhancement.progress.stage.failed", "增强失败");
                EnhancementExecution.DetailText = failureReason;
                EnhancementExecution.LastResultSummary = CreateEnhancementFailedSummary(failureReason, _lastEnhancementFailureKind);
                break;
        }

        RefreshEnhancementExecutionDisplay();
    }

    private void RefreshEnhancementExecutionDisplay()
    {
        OnPropertyChanged(nameof(EnhancementProgressStageTitleText));
        OnPropertyChanged(nameof(EnhancementProgressDetailText));
        OnPropertyChanged(nameof(EnhancementResultSummaryText));
    }
}
