using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Vidvix.Core.Interfaces;
using Vidvix.Core.Models;

namespace Vidvix.ViewModels;

public sealed partial class AiWorkspaceViewModel
{
    private readonly IAiRuntimeCatalogService? _aiRuntimeCatalogService;
    private AiRuntimeCatalog? _runtimeCatalog;
    private bool _isRuntimeInspectionInProgress;
    private bool _hasRuntimeInspectionCompleted;
    private string _runtimeInspectionErrorMessage = string.Empty;

    public string CurrentModeRuntimeTitleText =>
        GetLocalizedText("ai.page.runtime.title", "运行时状态");

    public string CurrentModeRuntimeVersionText =>
        FormatLocalizedText(
            "ai.page.runtime.version",
            $"runtime 版本：{BuildCurrentModeRuntimeVersionValue()}",
            ("version", BuildCurrentModeRuntimeVersionValue()));

    public string CurrentModeGpuStatusText =>
        FormatLocalizedText(
            "ai.page.runtime.gpu",
            $"GPU 状态：{BuildCurrentModeGpuStatusValue()}",
            ("status", BuildCurrentModeGpuStatusValue()));

    public string CurrentModeCpuStatusText =>
        FormatLocalizedText(
            "ai.page.runtime.cpu",
            $"CPU fallback：{BuildCurrentModeCpuStatusValue()}",
            ("status", BuildCurrentModeCpuStatusValue()));

    public string CurrentModeModelSummaryText =>
        FormatLocalizedText(
            "ai.page.runtime.models",
            $"模型：{BuildCurrentModeModelSummaryValue()}",
            ("models", BuildCurrentModeModelSummaryValue()));

    public string CurrentModePackageSummaryText =>
        FormatLocalizedText(
            "ai.page.runtime.packageRoot",
            $"随包目录：{BuildCurrentModePackageRelativePath()}",
            ("path", BuildCurrentModePackageRelativePath()));

    public async Task InitializeRuntimeAsync(CancellationToken cancellationToken = default)
    {
        if (_aiRuntimeCatalogService is null ||
            _isRuntimeInspectionInProgress ||
            _hasRuntimeInspectionCompleted)
        {
            return;
        }

        _isRuntimeInspectionInProgress = true;
        _runtimeInspectionErrorMessage = string.Empty;
        OnRuntimeInspectionStateChanged();

        try
        {
            _runtimeCatalog = await _aiRuntimeCatalogService.GetCatalogAsync(cancellationToken);
            _hasRuntimeInspectionCompleted = true;
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception exception)
        {
            _runtimeInspectionErrorMessage = exception.Message;
            _logger?.Log(LogLevel.Warning, "加载 AI runtime 状态时发生异常。", exception);
        }
        finally
        {
            _isRuntimeInspectionInProgress = false;
            OnRuntimeInspectionStateChanged();
        }
    }

    private string BuildCurrentModeRuntimeSummaryText()
    {
        var engineName = CurrentModeEngineBadgeText;
        if (_isRuntimeInspectionInProgress)
        {
            return FormatLocalizedText(
                "ai.page.runtime.pending",
                $"正在检查 {engineName} runtime、模型和设备能力…",
                ("engine", engineName));
        }

        if (!string.IsNullOrWhiteSpace(_runtimeInspectionErrorMessage))
        {
            return FormatLocalizedText(
                "ai.page.runtime.failed",
                $"AI runtime 检查失败：{_runtimeInspectionErrorMessage}",
                ("message", NormalizeErrorMessage(
                    _runtimeInspectionErrorMessage,
                    "ai.page.runtime.error.generic",
                    "AI runtime 检查失败，请重试。")));
        }

        var descriptor = GetCurrentModeRuntimeDescriptor();
        if (descriptor is null)
        {
            return FormatLocalizedText(
                "ai.page.runtime.pending",
                $"正在检查 {engineName} runtime、模型和设备能力…",
                ("engine", engineName));
        }

        return descriptor.Availability switch
        {
            AiRuntimeAvailability.Available => FormatLocalizedText(
                "ai.page.runtime.ready",
                $"{engineName} runtime 已解析，后续 workflow 将直接复用当前随包目录、模型描述和设备探测结果。",
                ("engine", engineName)),
            AiRuntimeAvailability.Missing => FormatLocalizedText(
                "ai.page.runtime.missing",
                $"{engineName} runtime 缺失，请补齐离线资产。",
                ("engine", engineName)),
            _ => FormatLocalizedText(
                "ai.page.runtime.invalid",
                $"{engineName} runtime 已找到，但目录结构或模型文件不完整。",
                ("engine", engineName))
        };
    }

    private string BuildCurrentModeRuntimeVersionValue()
    {
        var descriptor = GetCurrentModeRuntimeDescriptor();
        if (descriptor is null)
        {
            return GetRuntimePendingValueText();
        }

        if (!descriptor.IsAvailable)
        {
            return GetRuntimeUnavailableValueText();
        }

        return string.IsNullOrWhiteSpace(descriptor.RuntimeVersion)
            ? GetRuntimeUnavailableValueText()
            : descriptor.RuntimeVersion;
    }

    private string BuildCurrentModeGpuStatusValue() =>
        ResolveExecutionSupportText(GetCurrentModeRuntimeDescriptor()?.GpuSupport);

    private string BuildCurrentModeCpuStatusValue() =>
        ResolveExecutionSupportText(GetCurrentModeRuntimeDescriptor()?.CpuSupport);

    private string BuildCurrentModeModelSummaryValue()
    {
        var descriptor = GetCurrentModeRuntimeDescriptor();
        if (descriptor is null)
        {
            return GetRuntimePendingValueText();
        }

        if (!descriptor.IsAvailable || descriptor.Models.Count == 0)
        {
            return GetRuntimeUnavailableValueText();
        }

        return string.Join(
            " | ",
            descriptor.Models.Select(BuildRuntimeModelSummary));
    }

    private string BuildCurrentModePackageRelativePath() =>
        ModeState.SelectedMode == AiWorkspaceMode.Interpolation
            ? PathCombineRelative(
                _configuration.RuntimeDirectoryName,
                _configuration.AiRuntimeDirectoryName,
                _configuration.RifeDirectoryName)
            : PathCombineRelative(
                _configuration.RuntimeDirectoryName,
                _configuration.AiRuntimeDirectoryName,
                _configuration.RealEsrganDirectoryName);

    private string BuildRuntimeModelSummary(AiRuntimeModelDescriptor model)
    {
        var scaleSummary = model.NativeScaleFactors.Count == 0
            ? string.Empty
            : $" ({string.Join("/", model.NativeScaleFactors.Select(scale => $"{scale}x"))})";

        return $"{model.DisplayName}: {model.RuntimeModelName}{scaleSummary}";
    }

    private string ResolveExecutionSupportText(AiExecutionSupportStatus? status)
    {
        if (status is null)
        {
            return GetRuntimePendingValueText();
        }

        return status.State switch
        {
            AiExecutionSupportState.Available => GetLocalizedText("ai.page.runtime.device.available", "可用"),
            AiExecutionSupportState.Unavailable => GetLocalizedText("ai.page.runtime.device.unavailable", "不可用"),
            AiExecutionSupportState.Unsupported => GetLocalizedText("ai.page.runtime.device.unsupported", "当前 runtime 不支持"),
            AiExecutionSupportState.MissingRuntime => GetLocalizedText("ai.page.runtime.device.missingRuntime", "runtime 缺失"),
            AiExecutionSupportState.ProbeFailed => GetLocalizedText("ai.page.runtime.device.probeFailed", "探测失败"),
            _ => GetLocalizedText("ai.page.runtime.device.pending", "待探测")
        };
    }

    private string GetRuntimePendingValueText() =>
        GetLocalizedText("ai.page.runtime.value.pending", "待解析");

    private string GetRuntimeUnavailableValueText() =>
        GetLocalizedText("ai.page.runtime.value.unavailable", "未提供");

    private AiRuntimeDescriptor? GetCurrentModeRuntimeDescriptor() =>
        ModeState.SelectedMode == AiWorkspaceMode.Interpolation
            ? _runtimeCatalog?.Rife
            : _runtimeCatalog?.RealEsrgan;

    private void RefreshRuntimeLocalization() =>
        OnRuntimeInspectionStateChanged();

    private void OnRuntimeInspectionStateChanged()
    {
        OnPropertyChanged(nameof(CurrentModeRuntimeTitleText));
        OnPropertyChanged(nameof(CurrentModeRuntimeNoteText));
        OnPropertyChanged(nameof(CurrentModeRuntimeVersionText));
        OnPropertyChanged(nameof(CurrentModeGpuStatusText));
        OnPropertyChanged(nameof(CurrentModeCpuStatusText));
        OnPropertyChanged(nameof(CurrentModeModelSummaryText));
        OnPropertyChanged(nameof(CurrentModePackageSummaryText));
    }

    private static string PathCombineRelative(params string[] segments) =>
        string.Join("\\", segments.Where(segment => !string.IsNullOrWhiteSpace(segment)));
}
