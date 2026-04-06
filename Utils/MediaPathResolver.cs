using System;
using System.IO;

namespace Vidvix.Utils;

public static class MediaPathResolver
{
    public static string CreateSiblingOutputPath(string inputFilePath, string outputExtension) =>
        CreateSiblingOutputPath(inputFilePath, outputExtension, string.Empty);

    public static string CreateSiblingOutputPath(string inputFilePath, string outputExtension, string suffix)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(inputFilePath);
        ArgumentException.ThrowIfNullOrWhiteSpace(outputExtension);

        var normalizedExtension = outputExtension.StartsWith('.')
            ? outputExtension
            : $".{outputExtension}";
        var normalizedSuffix = suffix ?? string.Empty;

        var directory = Path.GetDirectoryName(inputFilePath);

        if (string.IsNullOrWhiteSpace(directory))
        {
            throw new InvalidOperationException("输入文件路径缺少有效目录。");
        }

        var fileNameWithoutExtension = Path.GetFileNameWithoutExtension(inputFilePath);
        var outputPath = Path.Combine(directory, $"{fileNameWithoutExtension}{normalizedSuffix}{normalizedExtension}");

        if (string.Equals(outputPath, inputFilePath, StringComparison.OrdinalIgnoreCase))
        {
            var fallbackSuffix = string.IsNullOrWhiteSpace(normalizedSuffix)
                ? "_output"
                : $"{normalizedSuffix}_output";

            outputPath = Path.Combine(directory, $"{fileNameWithoutExtension}{fallbackSuffix}{normalizedExtension}");
        }

        return outputPath;
    }

    public static string CreateVideoConversionOutputPath(string inputFilePath, string outputExtension) =>
        CreateSiblingOutputPath(inputFilePath, outputExtension, string.Empty);

    public static string CreateVideoTrackOutputPath(string inputFilePath, string outputExtension) =>
        CreateSiblingOutputPath(inputFilePath, outputExtension, "_video");

    public static string CreateAudioTrackOutputPath(string inputFilePath, string outputExtension) =>
        CreateSiblingOutputPath(inputFilePath, outputExtension, "_audio");
}
