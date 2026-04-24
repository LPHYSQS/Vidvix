using System;
using System.Threading;
using System.Threading.Tasks;
using Vidvix.Core.Interfaces;
using Vidvix.Core.Models;

namespace Vidvix.ViewModels;

internal sealed class AiEnhancementExecutionCoordinator
{
    private readonly IAiEnhancementWorkflowService _workflowService;
    private readonly ILocalizationService _localizationService;
    private readonly IUserPreferencesService _userPreferencesService;
    private readonly IFileRevealService _fileRevealService;
    private readonly ILogger _logger;

    public AiEnhancementExecutionCoordinator(
        IAiEnhancementWorkflowService workflowService,
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

    public async Task<AiEnhancementExecutionOutcome> ExecuteAsync(
        AiEnhancementRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        try
        {
            var result = await _workflowService.EnhanceAsync(request, cancellationToken).ConfigureAwait(false);
            TryRevealOutput(result.OutputPath);
            return AiEnhancementExecutionOutcome.Succeeded(result);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            return AiEnhancementExecutionOutcome.Cancelled();
        }
        catch (AiEnhancementWorkflowException exception)
        {
            _logger.Log(LogLevel.Warning, $"AI 增强执行失败：{exception.FailureKind}", exception);
            return AiEnhancementExecutionOutcome.Failed(exception.FailureKind, () => exception.Message);
        }
        catch (Exception exception)
        {
            _logger.Log(LogLevel.Error, "AI 增强协调器捕获到未处理异常。", exception);
            return AiEnhancementExecutionOutcome.Failed(
                AiEnhancementFailureKind.ExecutionFailed,
                () => _localizationService.GetString(
                    "ai.enhancement.failure.unexpected",
                    "增强执行失败，请重试。"));
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
            _logger.Log(LogLevel.Warning, "AI 增强完成后定位输出文件失败。", exception);
        }
    }
}

internal sealed class AiEnhancementExecutionOutcome
{
    private AiEnhancementExecutionOutcome(
        AiEnhancementExecutionOutcomeKind kind,
        AiEnhancementResult? result,
        AiEnhancementFailureKind? failureKind,
        Func<string>? failureReasonResolver)
    {
        Kind = kind;
        Result = result;
        FailureKind = failureKind;
        FailureReasonResolver = failureReasonResolver;
    }

    public AiEnhancementExecutionOutcomeKind Kind { get; }

    public AiEnhancementResult? Result { get; }

    public AiEnhancementFailureKind? FailureKind { get; }

    public Func<string>? FailureReasonResolver { get; }

    public string? FailureReason => FailureReasonResolver?.Invoke();

    public static AiEnhancementExecutionOutcome Succeeded(AiEnhancementResult result)
    {
        ArgumentNullException.ThrowIfNull(result);
        return new AiEnhancementExecutionOutcome(AiEnhancementExecutionOutcomeKind.Succeeded, result, null, null);
    }

    public static AiEnhancementExecutionOutcome Cancelled() =>
        new(AiEnhancementExecutionOutcomeKind.Cancelled, null, null, null);

    public static AiEnhancementExecutionOutcome Failed(
        AiEnhancementFailureKind failureKind,
        Func<string> failureReasonResolver)
    {
        ArgumentNullException.ThrowIfNull(failureReasonResolver);
        return new AiEnhancementExecutionOutcome(
            AiEnhancementExecutionOutcomeKind.Failed,
            null,
            failureKind,
            failureReasonResolver);
    }
}

internal enum AiEnhancementExecutionOutcomeKind
{
    Succeeded,
    Cancelled,
    Failed
}
