using System;
using System.IO;

namespace Vidvix.Utils;

public static class MediaPathResolver
{
    public static string CreateSiblingOutputPath(string inputFilePath, string outputExtension)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(inputFilePath);
        ArgumentException.ThrowIfNullOrWhiteSpace(outputExtension);

        var normalizedExtension = outputExtension.StartsWith('.')
            ? outputExtension
            : $".{outputExtension}";

        var directory = Path.GetDirectoryName(inputFilePath);

        if (string.IsNullOrWhiteSpace(directory))
        {
            throw new InvalidOperationException("The input file path does not contain a valid directory.");
        }

        var fileNameWithoutExtension = Path.GetFileNameWithoutExtension(inputFilePath);
        var outputPath = Path.Combine(directory, $"{fileNameWithoutExtension}{normalizedExtension}");

        if (string.Equals(outputPath, inputFilePath, StringComparison.OrdinalIgnoreCase))
        {
            outputPath = Path.Combine(directory, $"{fileNameWithoutExtension}_audio{normalizedExtension}");
        }

        return outputPath;
    }
}
