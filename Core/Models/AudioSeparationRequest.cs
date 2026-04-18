using System;

namespace Vidvix.Core.Models;

public sealed class AudioSeparationRequest
{
    public AudioSeparationRequest(
        string inputPath,
        OutputFormatOption outputFormat,
        string? outputDirectory = null,
        IProgress<AudioSeparationProgress>? progress = null,
        DemucsAccelerationMode accelerationMode = DemucsAccelerationMode.Cpu)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(inputPath);
        ArgumentNullException.ThrowIfNull(outputFormat);

        InputPath = inputPath;
        OutputFormat = outputFormat;
        OutputDirectory = outputDirectory;
        Progress = progress;
        AccelerationMode = accelerationMode;
    }

    public string InputPath { get; }

    public OutputFormatOption OutputFormat { get; }

    public string? OutputDirectory { get; }

    public IProgress<AudioSeparationProgress>? Progress { get; }

    public DemucsAccelerationMode AccelerationMode { get; }
}
