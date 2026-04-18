using System;
using System.Collections.Generic;

namespace Vidvix.Core.Models;

public sealed class AudioSeparationResult
{
    public AudioSeparationResult(
        string inputPath,
        string outputDirectory,
        IReadOnlyList<AudioSeparationStemOutput> stemOutputs,
        TimeSpan duration,
        DemucsExecutionPlan executionPlan)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(inputPath);
        ArgumentException.ThrowIfNullOrWhiteSpace(outputDirectory);
        ArgumentNullException.ThrowIfNull(stemOutputs);
        ArgumentNullException.ThrowIfNull(executionPlan);

        InputPath = inputPath;
        OutputDirectory = outputDirectory;
        StemOutputs = stemOutputs;
        Duration = duration;
        ExecutionPlan = executionPlan;
    }

    public string InputPath { get; }

    public string OutputDirectory { get; }

    public IReadOnlyList<AudioSeparationStemOutput> StemOutputs { get; }

    public TimeSpan Duration { get; }

    public DemucsExecutionPlan ExecutionPlan { get; }
}
