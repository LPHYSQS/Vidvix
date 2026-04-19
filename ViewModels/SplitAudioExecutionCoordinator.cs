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
    private readonly IUserPreferencesService _userPreferencesService;
    private readonly IFileRevealService _fileRevealService;
    private readonly ILogger _logger;

    public SplitAudioExecutionCoordinator(
        IAudioSeparationWorkflowService audioSeparationWorkflowService,
        IUserPreferencesService userPreferencesService,
        IFileRevealService fileRevealService,
        ILogger logger)
    {
        _audioSeparationWorkflowService = audioSeparationWorkflowService ?? throw new ArgumentNullException(nameof(audioSeparationWorkflowService));
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
            return SplitAudioExecutionOutcome.Succeeded(
                result,
                $"{result.ExecutionPlan.ResolutionSummary} 已生成 4 条 {outputFormat.DisplayName} 分轨文件。");
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            return SplitAudioExecutionOutcome.Cancelled("拆音任务已取消。");
        }
        catch (Exception exception)
        {
            _logger.Log(LogLevel.Error, "执行拆音任务时发生异常。", exception);
            return SplitAudioExecutionOutcome.Failed($"拆音失败：{exception.Message}");
        }
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
    private SplitAudioExecutionOutcome(AudioSeparationResult? result, string statusMessage, bool wasCancelled)
    {
        Result = result;
        StatusMessage = statusMessage;
        WasCancelled = wasCancelled;
    }

    public AudioSeparationResult? Result { get; }

    public string StatusMessage { get; }

    public bool WasCancelled { get; }

    public static SplitAudioExecutionOutcome Succeeded(AudioSeparationResult result, string statusMessage)
    {
        ArgumentNullException.ThrowIfNull(result);
        ArgumentException.ThrowIfNullOrWhiteSpace(statusMessage);
        return new SplitAudioExecutionOutcome(result, statusMessage, wasCancelled: false);
    }

    public static SplitAudioExecutionOutcome Cancelled(string statusMessage)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(statusMessage);
        return new SplitAudioExecutionOutcome(null, statusMessage, wasCancelled: true);
    }

    public static SplitAudioExecutionOutcome Failed(string statusMessage)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(statusMessage);
        return new SplitAudioExecutionOutcome(null, statusMessage, wasCancelled: false);
    }
}
