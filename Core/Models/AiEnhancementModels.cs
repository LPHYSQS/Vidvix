using System;

namespace Vidvix.Core.Models;

public enum AiEnhancementModelTier
{
    Standard = 0,
    Anime = 1
}

public enum AiEnhancementDevicePreference
{
    Automatic = 0,
    GpuPreferred = 1,
    Cpu = 2
}

public enum AiEnhancementExecutionDeviceKind
{
    Gpu = 0,
    Cpu = 1
}

public enum AiEnhancementFailureKind
{
    RuntimeMissing = 0,
    DeviceUnavailable = 1,
    InvalidInput = 2,
    ExecutionFailed = 3
}

public enum AiEnhancementProgressStage
{
    PreparingInput = 0,
    ExtractingFrames = 1,
    EnhancingFrames = 2,
    EncodingOutput = 3,
    Completed = 4
}

public sealed class AiEnhancementRequest
{
    public AiEnhancementRequest(
        string inputPath,
        string outputFileNameWithoutExtension,
        OutputFormatOption outputFormat,
        string? outputDirectory,
        AiEnhancementModelTier modelTier,
        int targetScaleFactor,
        AiEnhancementDevicePreference devicePreference,
        IProgress<AiEnhancementProgress>? progress = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(inputPath);
        ArgumentException.ThrowIfNullOrWhiteSpace(outputFileNameWithoutExtension);
        ArgumentNullException.ThrowIfNull(outputFormat);
        if (targetScaleFactor < 2 || targetScaleFactor > 16)
        {
            throw new ArgumentOutOfRangeException(nameof(targetScaleFactor));
        }

        InputPath = inputPath;
        OutputFileNameWithoutExtension = outputFileNameWithoutExtension;
        OutputFormat = outputFormat;
        OutputDirectory = outputDirectory;
        ModelTier = modelTier;
        TargetScaleFactor = targetScaleFactor;
        DevicePreference = devicePreference;
        Progress = progress;
    }

    public string InputPath { get; }

    public string OutputFileNameWithoutExtension { get; }

    public OutputFormatOption OutputFormat { get; }

    public string? OutputDirectory { get; }

    public AiEnhancementModelTier ModelTier { get; }

    public int TargetScaleFactor { get; }

    public AiEnhancementDevicePreference DevicePreference { get; }

    public IProgress<AiEnhancementProgress>? Progress { get; }
}

public sealed class AiEnhancementProgress
{
    public AiEnhancementProgress(
        AiEnhancementProgressStage stage,
        string stageTitle,
        string detailText,
        double? progressRatio,
        bool isCompleted = false)
    {
        Stage = stage;
        StageTitle = stageTitle ?? string.Empty;
        DetailText = detailText ?? string.Empty;
        ProgressRatio = progressRatio;
        IsCompleted = isCompleted;
    }

    public AiEnhancementProgressStage Stage { get; }

    public string StageTitle { get; }

    public string DetailText { get; }

    public double? ProgressRatio { get; }

    public bool IsCompleted { get; }
}

public sealed class AiEnhancementResult
{
    public required string InputPath { get; init; }

    public required string OutputPath { get; init; }

    public required string OutputDirectory { get; init; }

    public required string OutputFileName { get; init; }

    public required OutputFormatOption OutputFormat { get; init; }

    public required AiEnhancementModelTier ModelTier { get; init; }

    public required string ModelDisplayName { get; init; }

    public required AiEnhancementScalePlan ScalePlan { get; init; }

    public required AiEnhancementExecutionDeviceKind ExecutionDeviceKind { get; init; }

    public required string ExecutionDeviceDisplayName { get; init; }

    public required double SourceFrameRate { get; init; }

    public required TimeSpan SourceDuration { get; init; }

    public required TimeSpan WorkflowDuration { get; init; }

    public required int ExtractedFrameCount { get; init; }

    public required int OutputFrameCount { get; init; }

    public required bool PreservedOriginalAudio { get; init; }

    public bool AudioWasTranscoded { get; init; }
}

public sealed class AiEnhancementWorkflowException : Exception
{
    public AiEnhancementWorkflowException(
        AiEnhancementFailureKind failureKind,
        string message,
        Exception? innerException = null)
        : base(message, innerException)
    {
        FailureKind = failureKind;
    }

    public AiEnhancementFailureKind FailureKind { get; }
}
