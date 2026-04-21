using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Vidvix.Core.Interfaces;
using Vidvix.Core.Models;

namespace Vidvix.ViewModels;

internal sealed class SplitAudioExecutionCoordinator
{
    private readonly IAudioSeparationWorkflowService _audioSeparationWorkflowService;
    private readonly ILocalizationService _localizationService;
    private readonly IUserPreferencesService _userPreferencesService;
    private readonly IFileRevealService _fileRevealService;
    private readonly ILogger _logger;

    public SplitAudioExecutionCoordinator(
        IAudioSeparationWorkflowService audioSeparationWorkflowService,
        ILocalizationService localizationService,
        IUserPreferencesService userPreferencesService,
        IFileRevealService fileRevealService,
        ILogger logger)
    {
        _audioSeparationWorkflowService = audioSeparationWorkflowService ?? throw new ArgumentNullException(nameof(audioSeparationWorkflowService));
        _localizationService = localizationService ?? throw new ArgumentNullException(nameof(localizationService));
        _userPreferencesService = userPreferencesService ?? throw new ArgumentNullException(nameof(userPreferencesService));
        _fileRevealService = fileRevealService ?? throw new ArgumentNullException(nameof(fileRevealService));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    public async Task<SplitAudioExecutionOutcome> ExecuteAsync(
        string inputPath,
        OutputFormatOption outputFormat,
        string? outputDirectory,
        DemucsAccelerationMode accelerationMode,
        IProgress<AudioSeparationProgress>? progress = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(inputPath);
        ArgumentNullException.ThrowIfNull(outputFormat);

        try
        {
            var result = await _audioSeparationWorkflowService.SeparateAsync(
                new AudioSeparationRequest(
                    inputPath,
                    outputFormat,
                    outputDirectory,
                    progress,
                    accelerationMode),
                cancellationToken);

            TryRevealOutput(result);
            return SplitAudioExecutionOutcome.Succeeded(result);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            return SplitAudioExecutionOutcome.Cancelled();
        }
        catch (Exception exception)
        {
            _logger.Log(LogLevel.Error, "执行拆音任务时发生异常。", exception);
            return SplitAudioExecutionOutcome.Failed(BuildFailureReasonResolver(exception));
        }
    }

    private Func<string> BuildFailureReasonResolver(Exception exception)
    {
        ArgumentNullException.ThrowIfNull(exception);

        return () => string.IsNullOrWhiteSpace(exception.Message)
            ? _localizationService.GetString("splitAudio.status.failedGenericReason", "未知错误")
            : exception.Message;
    }

    private void TryRevealOutput(AudioSeparationResult result)
    {
        try
        {
            if (!_userPreferencesService.Load().RevealOutputFileAfterProcessing)
            {
                return;
            }

            var preferredOutput = result.StemOutputs.FirstOrDefault()?.FilePath;
            if (!string.IsNullOrWhiteSpace(preferredOutput))
            {
                _fileRevealService.RevealFile(preferredOutput);
            }
        }
        catch (Exception exception)
        {
            _logger.Log(LogLevel.Warning, "拆音完成后定位输出文件失败。", exception);
        }
    }
}

internal sealed class SplitAudioExecutionOutcome
{
    private SplitAudioExecutionOutcome(
        AudioSeparationResult? result,
        SplitAudioExecutionOutcomeKind kind,
        Func<string>? failureReasonResolver)
    {
        Result = result;
        Kind = kind;
        FailureReasonResolver = failureReasonResolver;
    }

    public AudioSeparationResult? Result { get; }

    public SplitAudioExecutionOutcomeKind Kind { get; }

    public Func<string>? FailureReasonResolver { get; }

    public string? FailureReason => FailureReasonResolver?.Invoke();

    public static SplitAudioExecutionOutcome Succeeded(AudioSeparationResult result)
    {
        ArgumentNullException.ThrowIfNull(result);
        return new SplitAudioExecutionOutcome(result, SplitAudioExecutionOutcomeKind.Succeeded, null);
    }

    public static SplitAudioExecutionOutcome Cancelled() =>
        new(null, SplitAudioExecutionOutcomeKind.Cancelled, null);

    public static SplitAudioExecutionOutcome Failed(Func<string> failureReasonResolver)
    {
        ArgumentNullException.ThrowIfNull(failureReasonResolver);
        return new SplitAudioExecutionOutcome(null, SplitAudioExecutionOutcomeKind.Failed, failureReasonResolver);
    }
}

internal enum SplitAudioExecutionOutcomeKind
{
    Succeeded,
    Cancelled,
    Failed
}
