using System;

namespace Vidvix.Core.Models;

public enum AiInterpolationScaleFactor
{
    X2 = 2,
    X4 = 4
}

public enum AiInterpolationDevicePreference
{
    Automatic = 0,
    GpuPreferred = 1,
    Cpu = 2
}

public enum AiInterpolationExecutionDeviceKind
{
    Gpu = 0,
    Cpu = 1
}

public enum AiInterpolationFailureKind
{
    RuntimeMissing = 0,
    DeviceUnavailable = 1,
    InvalidInput = 2,
    ExecutionFailed = 3
}

public enum AiInterpolationProgressStage
{
    PreparingInput = 0,
    ExtractingFrames = 1,
    InterpolatingFrames = 2,
    EncodingOutput = 3,
    Completed = 4
}

public sealed class AiInterpolationRequest
{
    public AiInterpolationRequest(
        string inputPath,
        string outputFileNameWithoutExtension,
        OutputFormatOption outputFormat,
        string? outputDirectory,
        AiInterpolationScaleFactor scaleFactor,
        AiInterpolationDevicePreference devicePreference,
        bool enableUhdMode,
        IProgress<AiInterpolationProgress>? progress = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(inputPath);
        ArgumentException.ThrowIfNullOrWhiteSpace(outputFileNameWithoutExtension);
        ArgumentNullException.ThrowIfNull(outputFormat);

        InputPath = inputPath;
        OutputFileNameWithoutExtension = outputFileNameWithoutExtension;
        OutputFormat = outputFormat;
        OutputDirectory = outputDirectory;
        ScaleFactor = scaleFactor;
        DevicePreference = devicePreference;
        EnableUhdMode = enableUhdMode;
        Progress = progress;
    }

    public string InputPath { get; }

    public string OutputFileNameWithoutExtension { get; }

    public OutputFormatOption OutputFormat { get; }

    public string? OutputDirectory { get; }

    public AiInterpolationScaleFactor ScaleFactor { get; }

    public AiInterpolationDevicePreference DevicePreference { get; }

    public bool EnableUhdMode { get; }

    public IProgress<AiInterpolationProgress>? Progress { get; }
}

public sealed class AiInterpolationProgress
{
    public AiInterpolationProgress(
        AiInterpolationProgressStage stage,
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

    public AiInterpolationProgressStage Stage { get; }

    public string StageTitle { get; }

    public string DetailText { get; }

    public double? ProgressRatio { get; }

    public bool IsCompleted { get; }
}

public sealed class AiInterpolationResult
{
    public required string InputPath { get; init; }

    public required string OutputPath { get; init; }

    public required string OutputDirectory { get; init; }

    public required string OutputFileName { get; init; }

    public required OutputFormatOption OutputFormat { get; init; }

    public required AiInterpolationScaleFactor ScaleFactor { get; init; }

    public required AiInterpolationExecutionDeviceKind ExecutionDeviceKind { get; init; }

    public required string ExecutionDeviceDisplayName { get; init; }

    public required double SourceFrameRate { get; init; }

    public required double TargetFrameRate { get; init; }

    public required TimeSpan SourceDuration { get; init; }

    public required TimeSpan WorkflowDuration { get; init; }

    public required int ExtractedFrameCount { get; init; }

    public required int OutputFrameCount { get; init; }

    public required int InterpolationPassCount { get; init; }

    public bool UsedUhdMode { get; init; }

    public bool PreservedOriginalAudio { get; init; }

    public bool AudioWasTranscoded { get; init; }
}

public sealed class AiInterpolationWorkflowException : Exception
{
    public AiInterpolationWorkflowException(
        AiInterpolationFailureKind failureKind,
        string message,
        Exception? innerException = null)
        : base(message, innerException)
    {
        FailureKind = failureKind;
    }

    public AiInterpolationFailureKind FailureKind { get; }
}
