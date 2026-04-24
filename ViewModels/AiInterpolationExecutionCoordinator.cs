using System;
using System.Threading;
using System.Threading.Tasks;
using Vidvix.Core.Interfaces;
using Vidvix.Core.Models;

namespace Vidvix.ViewModels;

internal sealed class AiInterpolationExecutionCoordinator
{
    private readonly IAiInterpolationWorkflowService _workflowService;
    private readonly ILocalizationService _localizationService;
    private readonly IUserPreferencesService _userPreferencesService;
    private readonly IFileRevealService _fileRevealService;
    private readonly ILogger _logger;

    public AiInterpolationExecutionCoordinator(
        IAiInterpolationWorkflowService workflowService,
        ILocalizationService localizationService,
        IUserPreferencesService userPreferencesService,
        IFileRevealService fileRevealService,
        ILogger logger)
    {
        _workflowService = workflowService ?? throw new ArgumentNullException(nameof(workflowService));
        _localizationService = localizationService ?? throw new ArgumentNullException(nameof(localizationService));
        _userPreferencesService = userPreferencesService ?? throw new ArgumentNullException(nameof(userPreferencesService));
        _fileRevealService = fileRevealService ?? throw new ArgumentNullException(nameof(fileRevealService));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    public async Task<AiInterpolationExecutionOutcome> ExecuteAsync(
        AiInterpolationRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        try
        {
            var result = await _workflowService.InterpolateAsync(request, cancellationToken).ConfigureAwait(false);
            TryRevealOutput(result.OutputPath);
            return AiInterpolationExecutionOutcome.Succeeded(result);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            return AiInterpolationExecutionOutcome.Cancelled();
        }
        catch (AiInterpolationWorkflowException exception)
        {
            _logger.Log(LogLevel.Warning, $"AI 补帧执行失败：{exception.FailureKind}", exception);
            return AiInterpolationExecutionOutcome.Failed(exception.FailureKind, () => exception.Message);
        }
        catch (Exception exception)
        {
            _logger.Log(LogLevel.Error, "AI 补帧协调器捕获到未处理异常。", exception);
            return AiInterpolationExecutionOutcome.Failed(
                AiInterpolationFailureKind.ExecutionFailed,
                () => _localizationService.GetString(
                    "ai.interpolation.failure.unexpected",
                    "补帧执行失败，请重试。"));
        }
    }

    private void TryRevealOutput(string outputPath)
    {
        try
        {
            if (!_userPreferencesService.Load().RevealOutputFileAfterProcessing || string.IsNullOrWhiteSpace(outputPath))
            {
                return;
            }

            _fileRevealService.RevealFile(outputPath);
        }
        catch (Exception exception)
        {
            _logger.Log(LogLevel.Warning, "AI 补帧完成后定位输出文件失败。", exception);
        }
    }
}

internal sealed class AiInterpolationExecutionOutcome
{
    private AiInterpolationExecutionOutcome(
        AiInterpolationExecutionOutcomeKind kind,
        AiInterpolationResult? result,
        AiInterpolationFailureKind? failureKind,
        Func<string>? failureReasonResolver)
    {
        Kind = kind;
        Result = result;
        FailureKind = failureKind;
        FailureReasonResolver = failureReasonResolver;
    }

    public AiInterpolationExecutionOutcomeKind Kind { get; }

    public AiInterpolationResult? Result { get; }

    public AiInterpolationFailureKind? FailureKind { get; }

    public Func<string>? FailureReasonResolver { get; }

    public string? FailureReason => FailureReasonResolver?.Invoke();

    public static AiInterpolationExecutionOutcome Succeeded(AiInterpolationResult result)
    {
        ArgumentNullException.ThrowIfNull(result);
        return new AiInterpolationExecutionOutcome(AiInterpolationExecutionOutcomeKind.Succeeded, result, null, null);
    }

    public static AiInterpolationExecutionOutcome Cancelled() =>
        new(AiInterpolationExecutionOutcomeKind.Cancelled, null, null, null);

    public static AiInterpolationExecutionOutcome Failed(
        AiInterpolationFailureKind failureKind,
        Func<string> failureReasonResolver)
    {
        ArgumentNullException.ThrowIfNull(failureReasonResolver);
        return new AiInterpolationExecutionOutcome(
            AiInterpolationExecutionOutcomeKind.Failed,
            null,
            failureKind,
            failureReasonResolver);
    }
}

internal enum AiInterpolationExecutionOutcomeKind
{
    Succeeded,
    Cancelled,
    Failed
}
