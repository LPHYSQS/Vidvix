using System;

namespace Vidvix.Core.Models;

public sealed class AudioSeparationStemOutput
{
    public AudioSeparationStemOutput(AudioStemKind stemKind, string filePath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(filePath);

        StemKind = stemKind;
        FilePath = filePath;
    }

    public AudioStemKind StemKind { get; }

    public string FilePath { get; }
}
