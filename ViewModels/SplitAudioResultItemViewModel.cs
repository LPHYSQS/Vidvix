using System;
using System.Collections.Generic;
using System.IO;
using Vidvix.Core.Models;

namespace Vidvix.ViewModels;

public sealed class SplitAudioResultItemViewModel
{
    private readonly IReadOnlyDictionary<AudioStemKind, string> _stemPaths;

    public SplitAudioResultItemViewModel(AudioSeparationResult result)
    {
        ArgumentNullException.ThrowIfNull(result);

        InputPath = result.InputPath;
        SourceFileName = Path.GetFileName(result.InputPath);
        OutputDirectory = result.OutputDirectory;
        DurationText = FormatDuration(result.Duration);
        DurationMilliseconds = Math.Max(0d, result.Duration.TotalMilliseconds);
        CompletedAtText = DateTimeOffset.Now.ToLocalTime().ToString("HH:mm:ss");
        _stemPaths = CreateStemPathMap(result);
    }

    public string InputPath { get; }

    public string SourceFileName { get; }

    public string OutputDirectory { get; }

    public string DurationText { get; }

    public double DurationMilliseconds { get; }

    public string CompletedAtText { get; }

    public string VocalsPath => GetStemPath(AudioStemKind.Vocals);

    public string DrumsPath => GetStemPath(AudioStemKind.Drums);

    public string BassPath => GetStemPath(AudioStemKind.Bass);

    public string OtherPath => GetStemPath(AudioStemKind.Other);

    public string VocalsFileName => Path.GetFileName(VocalsPath);

    public string DrumsFileName => Path.GetFileName(DrumsPath);

    public string BassFileName => Path.GetFileName(BassPath);

    public string OtherFileName => Path.GetFileName(OtherPath);

    private string GetStemPath(AudioStemKind stemKind) =>
        _stemPaths.TryGetValue(stemKind, out var filePath) ? filePath : string.Empty;

    private static IReadOnlyDictionary<AudioStemKind, string> CreateStemPathMap(AudioSeparationResult result)
    {
        var stemPaths = new Dictionary<AudioStemKind, string>();
        foreach (var stemOutput in result.StemOutputs)
        {
            stemPaths[stemOutput.StemKind] = stemOutput.FilePath;
        }

        return stemPaths;
    }

    private static string FormatDuration(TimeSpan duration) =>
        duration.TotalHours >= 1d
            ? duration.ToString(@"hh\:mm\:ss")
            : duration.ToString(@"mm\:ss");
}
