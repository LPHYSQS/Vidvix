using System;
using System.IO;

namespace Vidvix.Utils;

public static class VideoTrimPathResolver
{
    public static string CreateOutputPath(string inputPath, string outputExtension, string? outputDirectory = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(inputPath);
        ArgumentException.ThrowIfNullOrWhiteSpace(outputExtension);

        var sourcePath = Path.GetFullPath(inputPath);
        var extension = outputExtension.StartsWith(".", StringComparison.Ordinal)
            ? outputExtension
            : $".{outputExtension}";
        var targetDirectory = string.IsNullOrWhiteSpace(outputDirectory)
            ? Path.GetDirectoryName(sourcePath)
            : Path.GetFullPath(outputDirectory);

        if (string.IsNullOrWhiteSpace(targetDirectory))
        {
            throw new InvalidOperationException("无法确定裁剪输出目录。");
        }

        var fileName = Path.GetFileNameWithoutExtension(sourcePath);
        return Path.Combine(targetDirectory, $"{fileName}_trim{extension}");
    }
}
