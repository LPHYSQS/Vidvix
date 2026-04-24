using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.UI.Xaml;
using Vidvix.Core.Models;

namespace Vidvix.ViewModels;

public sealed partial class AiWorkspaceViewModel
{
    private readonly AiInterpolationExecutionCoordinator? _aiInterpolationExecutionCoordinator;
    private CancellationTokenSource? _processingCancellationSource;
    private AiInterpolationResult? _lastInterpolationResult;
    private AiInterpolationFailureKind? _lastInterpolationFailureKind;
    private Func<string>? _lastInterpolationFailureReasonResolver;
    private AiExecutionFeedbackKind _lastInterpolationFeedbackKind;

    public AiInterpolationSettingsState InterpolationSettings { get; }

    public AiInterpolationExecutionState InterpolationExecution { get; }

    public bool CanEditProcessingParameters => !IsProcessing;

    public Visibility InterpolationControlsVisibility =>
        ModeState.SelectedMode == AiWorkspaceMode.Interpolation
            ? Visibility.Visible
            : Visibility.Collapsed;

    public Visibility InterpolationProgressVisibility =>
        ModeState.SelectedMode == AiWorkspaceMode.Interpolation
            ? Visibility.Visible
            : Visibility.Collapsed;

    public string InterpolationSettingsTitleText =>
        GetLocalizedText("ai.interpolation.settings.title", "补帧参数");

    public string InterpolationSettingsDescriptionText =>
        GetLocalizedText(
            "ai.interpolation.settings.description",
            "R8 已接通 RIFE 首发补帧闭环。倍率先开放 2x / 4x，4x 通过两次 2x 逐级补帧完成。");

    public string InterpolationScaleTitleText =>
        GetLocalizedText("ai.interpolation.settings.scale.title", "补帧倍率");

    public string InterpolationScaleHintText =>
        FormatLocalizedText(
            "ai.interpolation.settings.scale.hint",
            "当前选择 {scale}。2x 单次补帧，4x 走两次 2x 逐级补帧。",
            ("scale", InterpolationSettings.SelectedScaleFactor.DisplayName));

    public string InterpolationDeviceTitleText =>
        GetLocalizedText("ai.interpolation.settings.device.title", "执行设备");

    public string InterpolationDeviceHintText =>
        InterpolationSettings.SelectedDevicePreference == AiInterpolationDevicePreference.Cpu
            ? GetLocalizedText(
                "ai.interpolation.settings.device.hint.cpu",
                "已锁定 CPU 模式；若当前机器 CPU fallback 不可用，任务会在启动前明确失败。")
            : GetLocalizedText(
                "ai.interpolation.settings.device.hint.auto",
                "自动 与 GPU优先 当前都会优先尝试 GPU，不可用时回退到 RIFE CPU fallback。");

    public string InterpolationUhdTitleText =>
        GetLocalizedText("ai.interpolation.settings.uhd.title", "UHD 模式");

    public string InterpolationUhdHintText =>
        GetLocalizedText(
            "ai.interpolation.settings.uhd.hint",
            "4K 或更高分辨率素材建议开启。该模式更稳，但显存占用和耗时都会明显上升。");

    public string ProcessingActionsTitleText =>
        GetLocalizedText("ai.page.processing.title", "执行控制");

    public string ProcessingActionsHintText =>
        ModeState.SelectedMode == AiWorkspaceMode.Interpolation
            ? GetLocalizedText(
                "ai.interpolation.action.hint",
                "执行时会自动抽帧、运行 RIFE、回填原音轨并生成输出视频；处理中会锁定导入、模式和输出设置。")
            : GetLocalizedText(
                "ai.enhancement.action.hint",
                "执行时会自动抽帧、运行 Real-ESRGAN、按规划组合放大或超采样回缩、回填原音轨并生成输出视频；处理中会锁定导入、模式和输出设置。");

    public string StartProcessingButtonText =>
        ModeState.SelectedMode == AiWorkspaceMode.Interpolation
            ? GetLocalizedText("ai.interpolation.action.start", "开始补帧")
            : GetLocalizedText("ai.enhancement.action.start", "开始增强");

    public string CancelProcessingButtonText =>
        GetLocalizedText("ai.interpolation.action.cancel", "取消任务");

    public string InterpolationProgressTitleText =>
        GetLocalizedText("ai.interpolation.progress.title", "补帧进度与最近结果");

    public string InterpolationProgressPlaceholderText =>
        GetLocalizedText(
            "ai.interpolation.progress.placeholder",
            "开始补帧后，这里会显示当前阶段、进度和最近一次输出结果。");

    public string LastInterpolationOutputLabelText =>
        GetLocalizedText("ai.interpolation.progress.lastOutput", "最近输出");

    public string InterpolationProgressStageTitleText =>
        string.IsNullOrWhiteSpace(InterpolationExecution.StageTitle)
            ? GetLocalizedText("ai.interpolation.progress.stage.idle", "待开始")
            : InterpolationExecution.StageTitle;

    public string InterpolationProgressDetailText =>
        string.IsNullOrWhiteSpace(InterpolationExecution.DetailText)
            ? InterpolationProgressPlaceholderText
            : InterpolationExecution.DetailText;

    public string InterpolationResultSummaryText =>
        string.IsNullOrWhiteSpace(InterpolationExecution.LastResultSummary)
            ? GetLocalizedText(
                "ai.interpolation.progress.result.empty",
                "最近还没有完成的补帧输出。")
            : InterpolationExecution.LastResultSummary;

    private Task StartCurrentModeProcessingAsync() =>
        ModeState.SelectedMode == AiWorkspaceMode.Interpolation
            ? StartInterpolationProcessingAsync()
            : StartEnhancementProcessingAsync();

    private async Task StartInterpolationProcessingAsync()
    {
        if (_aiInterpolationExecutionCoordinator is null)
        {
            SetStatusText(
                "ai.status.interpolationUnavailable",
                "当前运行环境未接入 AI补帧 工作流服务，暂时无法启动补帧。");
            return;
        }

        if (!InputState.HasCurrentMaterial)
        {
            SetStatusText("ai.status.ready", "先导入一个或多个视频，再从素材列表中锁定当前处理对象。");
            return;
        }

        var progress = new Progress<AiInterpolationProgress>(update =>
        {
            InterpolationExecution.ApplyProgress(update);
            OnPropertyChanged(nameof(InterpolationProgressStageTitleText));
            OnPropertyChanged(nameof(InterpolationProgressDetailText));
        });

        using var cancellationSource = new CancellationTokenSource();
        _processingCancellationSource = cancellationSource;
        ResetInterpolationOutcomeTracking();
        var request = new AiInterpolationRequest(
            InputState.CurrentInputPath,
            OutputSettings.EffectiveOutputFileName,
            OutputSettings.SelectedOutputFormat,
            OutputSettings.EffectiveOutputDirectory,
            InterpolationSettings.SelectedScaleFactorValue,
            InterpolationSettings.SelectedDevicePreference,
            InterpolationSettings.EnableUhdMode,
            progress);

        InterpolationExecution.ResetForExecution(
            GetLocalizedText("ai.interpolation.progress.stage.prepare", "准备补帧任务"),
            GetLocalizedText("ai.interpolation.progress.detail.prepare", "正在校验输入、运行时和输出路径…"));
        RefreshInterpolationExecutionDisplay();
        SetStatusText(() => CreateInterpolationStartedStatusMessage());

        try
        {
            IsProcessing = true;
            RefreshInterpolationModeProperties();

            var outcome = await _aiInterpolationExecutionCoordinator
                .ExecuteAsync(request, cancellationSource.Token)
                .ConfigureAwait(false);

            switch (outcome.Kind)
            {
                case AiInterpolationExecutionOutcomeKind.Succeeded:
                    ApplyInterpolationSuccessOutcome(outcome.Result!);
                    break;
                case AiInterpolationExecutionOutcomeKind.Cancelled:
                    ApplyInterpolationCancelledOutcome();
                    break;
                default:
                    ApplyInterpolationFailureOutcome(
                        outcome.FailureKind,
                        outcome.FailureReasonResolver ?? (() => GetInterpolationGenericFailureReason()));
                    break;
            }
        }
        finally
        {
            IsProcessing = false;
            _processingCancellationSource = null;
            RefreshInterpolationModeProperties();
            NotifyCommandStates();
        }
    }

    private void CancelCurrentProcessing()
    {
        if (!IsProcessing)
        {
            return;
        }

        _processingCancellationSource?.Cancel();
        if (ModeState.SelectedMode == AiWorkspaceMode.Interpolation)
        {
            InterpolationExecution.DetailText = GetInterpolationCancellingStatusMessage();
            RefreshInterpolationExecutionDisplay();
            SetStatusText("ai.status.interpolationCancelling", "正在取消 AI补帧 任务并清理临时目录…");
            return;
        }

        EnhancementExecution.DetailText = GetEnhancementCancellingStatusMessage();
        SetStatusText("ai.status.enhancementCancelling", "正在取消 AI增强 任务并清理临时目录…");
        OnPropertyChanged(nameof(EnhancementProgressDetailText));
    }

    private void RefreshInterpolationLocalization()
    {
        InterpolationSettings.ReloadLocalizedOptions(
            BuildInterpolationScaleFactorOptions(),
            BuildInterpolationDeviceOptions());

        OnPropertyChanged(nameof(CanEditProcessingParameters));
        OnPropertyChanged(nameof(InterpolationControlsVisibility));
        OnPropertyChanged(nameof(InterpolationProgressVisibility));
        OnPropertyChanged(nameof(InterpolationSettingsTitleText));
        OnPropertyChanged(nameof(InterpolationSettingsDescriptionText));
        OnPropertyChanged(nameof(InterpolationScaleTitleText));
        OnPropertyChanged(nameof(InterpolationScaleHintText));
        OnPropertyChanged(nameof(InterpolationDeviceTitleText));
        OnPropertyChanged(nameof(InterpolationDeviceHintText));
        OnPropertyChanged(nameof(InterpolationUhdTitleText));
        OnPropertyChanged(nameof(InterpolationUhdHintText));
        OnPropertyChanged(nameof(ProcessingActionsTitleText));
        OnPropertyChanged(nameof(ProcessingActionsHintText));
        OnPropertyChanged(nameof(StartProcessingButtonText));
        OnPropertyChanged(nameof(CancelProcessingButtonText));
        OnPropertyChanged(nameof(InterpolationProgressTitleText));
        OnPropertyChanged(nameof(InterpolationProgressPlaceholderText));
        OnPropertyChanged(nameof(LastInterpolationOutputLabelText));
        RefreshInterpolationExecutionDisplay();
    }

    private void RefreshInterpolationModeProperties()
    {
        OnPropertyChanged(nameof(CanStartProcessing));
        OnPropertyChanged(nameof(CanEditProcessingParameters));
        OnPropertyChanged(nameof(InterpolationControlsVisibility));
        OnPropertyChanged(nameof(InterpolationProgressVisibility));
        OnPropertyChanged(nameof(InterpolationScaleHintText));
        OnPropertyChanged(nameof(InterpolationDeviceHintText));
        OnPropertyChanged(nameof(ProcessingActionsHintText));
        OnPropertyChanged(nameof(StartProcessingButtonText));
        _startProcessingCommand.NotifyCanExecuteChanged();
        _cancelProcessingCommand.NotifyCanExecuteChanged();
    }

    private IReadOnlyList<AiInterpolationScaleFactorOption> BuildInterpolationScaleFactorOptions() =>
        new[]
        {
            new AiInterpolationScaleFactorOption(
                AiInterpolationScaleFactor.X2,
                GetLocalizedText("ai.interpolation.scaleOption.2x", "2x"),
                GetLocalizedText(
                    "ai.interpolation.scaleOption.2x.description",
                    "单次补帧，适合作为首发默认模式。")),
            new AiInterpolationScaleFactorOption(
                AiInterpolationScaleFactor.X4,
                GetLocalizedText("ai.interpolation.scaleOption.4x", "4x"),
                GetLocalizedText(
                    "ai.interpolation.scaleOption.4x.description",
                    "通过两次 2x 逐级补帧完成，耗时明显更长。"))
        };

    private IReadOnlyList<AiInterpolationDeviceOption> BuildInterpolationDeviceOptions() =>
        new[]
        {
            new AiInterpolationDeviceOption(
                AiInterpolationDevicePreference.Automatic,
                GetLocalizedText("ai.interpolation.deviceOption.automatic", "自动"),
                GetLocalizedText(
                    "ai.interpolation.deviceOption.automatic.description",
                    "优先尝试 GPU，不可用时回退到 RIFE CPU fallback。")),
            new AiInterpolationDeviceOption(
                AiInterpolationDevicePreference.GpuPreferred,
                GetLocalizedText("ai.interpolation.deviceOption.gpuPreferred", "GPU优先"),
                GetLocalizedText(
                    "ai.interpolation.deviceOption.gpuPreferred.description",
                    "和自动模式一样优先走 GPU，本轮先保持同一条稳定链路。")),
            new AiInterpolationDeviceOption(
                AiInterpolationDevicePreference.Cpu,
                GetLocalizedText("ai.interpolation.deviceOption.cpu", "CPU"),
                GetLocalizedText(
                    "ai.interpolation.deviceOption.cpu.description",
                    "强制使用 RIFE CPU fallback，兼容性更高但速度更慢。"))
        };

    private string CreateInterpolationStartedStatusMessage() =>
        FormatLocalizedText(
            "ai.status.interpolationStarted",
            "已开始 AI补帧：{fileName}，当前倍率 {scale}。",
            ("fileName", InputState.CurrentInputFileName),
            ("scale", InterpolationSettings.SelectedScaleFactor.DisplayName));

    private string CreateInterpolationCompletedStatusMessage(AiInterpolationResult result) =>
        FormatLocalizedText(
            "ai.status.interpolationCompleted",
            "AI补帧已完成：{fileName} -> {outputFileName}",
            ("fileName", Path.GetFileName(result.InputPath)),
            ("outputFileName", result.OutputFileName));

    private string CreateInterpolationSuccessSummary(AiInterpolationResult result) =>
        FormatLocalizedText(
            "ai.interpolation.progress.result.success",
            "最近一次补帧已完成：{scale}、{device}、目标帧率 {frameRate} fps，输出 {outputFileName}",
            ("scale", InterpolationSettings.SelectedScaleFactor.DisplayName),
            ("device", result.ExecutionDeviceDisplayName),
            ("frameRate", result.TargetFrameRate.ToString("0.###", CultureInfo.InvariantCulture)),
            ("outputFileName", result.OutputFileName));

    private string CreateInterpolationFailedSummary(string failureReason, AiInterpolationFailureKind? failureKind)
    {
        var kindText = failureKind switch
        {
            AiInterpolationFailureKind.RuntimeMissing => GetLocalizedText("ai.interpolation.failureKind.runtimeMissing", "runtime 缺失"),
            AiInterpolationFailureKind.DeviceUnavailable => GetLocalizedText("ai.interpolation.failureKind.deviceUnavailable", "设备不可用"),
            AiInterpolationFailureKind.InvalidInput => GetLocalizedText("ai.interpolation.failureKind.invalidInput", "输入无效"),
            _ => GetLocalizedText("ai.interpolation.failureKind.executionFailed", "执行失败")
        };

        return FormatLocalizedText(
            "ai.status.interpolationFailed",
            "AI补帧失败（{kind}）：{reason}",
            ("kind", kindText),
            ("reason", failureReason));
    }

    private string GetInterpolationCancelledStatusMessage() =>
        GetLocalizedText("ai.status.interpolationCancelled", "已取消当前 AI补帧 任务，临时目录已开始清理。");

    private string GetInterpolationCancelledSummaryText() =>
        GetLocalizedText("ai.interpolation.progress.result.cancelled", "最近一次补帧任务已取消。");

    private string GetInterpolationCancellingStatusMessage() =>
        GetLocalizedText("ai.status.interpolationCancelling", "正在取消 AI补帧 任务并清理临时目录…");

    private string GetInterpolationUnavailableStatusMessage() =>
        GetLocalizedText(
            "ai.status.interpolationUnavailable",
            "当前运行环境未接入 AI补帧 工作流服务，暂时无法启动补帧。");

    private string GetInterpolationGenericFailureReason() =>
        GetLocalizedText("ai.interpolation.failure.unexpected", "补帧执行失败，请重试。");

    private void ResetInterpolationOutcomeTracking()
    {
        _lastInterpolationFailureReasonResolver = null;
        _lastInterpolationFailureKind = null;
        _lastInterpolationFeedbackKind = AiExecutionFeedbackKind.None;
    }

    private void ApplyInterpolationSuccessOutcome(AiInterpolationResult result)
    {
        ArgumentNullException.ThrowIfNull(result);

        _lastInterpolationResult = result;
        _lastInterpolationFailureReasonResolver = null;
        _lastInterpolationFailureKind = null;
        _lastInterpolationFeedbackKind = AiExecutionFeedbackKind.Succeeded;

        InterpolationExecution.ApplySuccess(CreateInterpolationSuccessSummary(result), result.OutputPath);
        InterpolationExecution.StageTitle = GetLocalizedText("ai.interpolation.progress.stage.complete", "补帧完成");
        InterpolationExecution.DetailText = FormatLocalizedText(
            "ai.interpolation.progress.detail.complete",
            "输出文件已生成：{fileName}",
            ("fileName", result.OutputFileName));

        SetStatusText(() => CreateInterpolationCompletedStatusMessage(result));
        RefreshInterpolationExecutionDisplay();
    }

    private void ApplyInterpolationCancelledOutcome()
    {
        _lastInterpolationFailureReasonResolver = null;
        _lastInterpolationFailureKind = null;
        _lastInterpolationFeedbackKind = AiExecutionFeedbackKind.Cancelled;

        var cancelledSummary = GetInterpolationCancelledSummaryText();
        InterpolationExecution.ApplyFailure(cancelledSummary);
        InterpolationExecution.StageTitle = GetLocalizedText("ai.interpolation.progress.stage.cancelled", "补帧已取消");
        InterpolationExecution.DetailText = cancelledSummary;

        SetStatusText("ai.status.interpolationCancelled", "已取消当前 AI补帧 任务，临时目录已开始清理。");
        RefreshInterpolationExecutionDisplay();
    }

    private void ApplyInterpolationFailureOutcome(
        AiInterpolationFailureKind? failureKind,
        Func<string> failureReasonResolver)
    {
        ArgumentNullException.ThrowIfNull(failureReasonResolver);

        _lastInterpolationFailureReasonResolver = failureReasonResolver;
        _lastInterpolationFailureKind = failureKind;
        _lastInterpolationFeedbackKind = AiExecutionFeedbackKind.Failed;

        var failureReason = GetCurrentInterpolationFailureReasonText();
        InterpolationExecution.ApplyFailure(CreateInterpolationFailedSummary(failureReason, failureKind));
        InterpolationExecution.StageTitle = GetLocalizedText("ai.interpolation.progress.stage.failed", "补帧失败");
        InterpolationExecution.DetailText = failureReason;

        SetStatusText(() => CreateInterpolationFailedSummary(GetCurrentInterpolationFailureReasonText(), _lastInterpolationFailureKind));
        RefreshInterpolationExecutionDisplay();
    }

    private string GetCurrentInterpolationFailureReasonText() =>
        NormalizeErrorMessage(
            _lastInterpolationFailureReasonResolver?.Invoke(),
            "ai.interpolation.failure.unexpected",
            "补帧执行失败，请重试。");

    private void RefreshInterpolationOutcomeLocalization()
    {
        if (_lastInterpolationFeedbackKind == AiExecutionFeedbackKind.None)
        {
            return;
        }

        switch (_lastInterpolationFeedbackKind)
        {
            case AiExecutionFeedbackKind.Succeeded when _lastInterpolationResult is not null:
                InterpolationExecution.StageTitle = GetLocalizedText("ai.interpolation.progress.stage.complete", "补帧完成");
                InterpolationExecution.DetailText = FormatLocalizedText(
                    "ai.interpolation.progress.detail.complete",
                    "输出文件已生成：{fileName}",
                    ("fileName", _lastInterpolationResult.OutputFileName));
                InterpolationExecution.LastResultSummary = CreateInterpolationSuccessSummary(_lastInterpolationResult);
                break;
            case AiExecutionFeedbackKind.Cancelled:
                var cancelledSummary = GetInterpolationCancelledSummaryText();
                InterpolationExecution.StageTitle = GetLocalizedText("ai.interpolation.progress.stage.cancelled", "补帧已取消");
                InterpolationExecution.DetailText = cancelledSummary;
                InterpolationExecution.LastResultSummary = cancelledSummary;
                break;
            case AiExecutionFeedbackKind.Failed:
                var failureReason = GetCurrentInterpolationFailureReasonText();
                InterpolationExecution.StageTitle = GetLocalizedText("ai.interpolation.progress.stage.failed", "补帧失败");
                InterpolationExecution.DetailText = failureReason;
                InterpolationExecution.LastResultSummary = CreateInterpolationFailedSummary(failureReason, _lastInterpolationFailureKind);
                break;
        }

        RefreshInterpolationExecutionDisplay();
    }

    private void RefreshInterpolationExecutionDisplay()
    {
        OnPropertyChanged(nameof(InterpolationProgressStageTitleText));
        OnPropertyChanged(nameof(InterpolationProgressDetailText));
        OnPropertyChanged(nameof(InterpolationResultSummaryText));
    }
}
