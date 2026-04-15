// 功能：媒体输出路径工具（统一处理输出目录规范化、默认命名与重名避让）
// 模块：裁剪模块 / 视频转换模块 / 音频转换模块
// 说明：可复用，仅负责文件路径规划，不涉及 UI 或业务流程。
using System;
using System.Collections.Generic;
using System.IO;

namespace Vidvix.Utils;

public static class MediaPathResolver
{
    public static string CreateSiblingOutputPath(string inputFilePath, string outputExtension) =>
        CreateSiblingOutputPath(inputFilePath, outputExtension, string.Empty);

    public static string CreateSiblingOutputPath(string inputFilePath, string outputExtension, string suffix) =>
        CreateOutputPath(inputFilePath, outputExtension, outputDirectory: null, suffix);

    public static bool TryNormalizeOutputDirectory(string? outputDirectory, out string normalizedOutputDirectory)
    {
        normalizedOutputDirectory = string.Empty;
        if (string.IsNullOrWhiteSpace(outputDirectory))
        {
            return true;
        }

        try
        {
            normalizedOutputDirectory = Path.GetFullPath(outputDirectory.Trim());
            return true;
        }
        catch (Exception exception) when (
            exception is ArgumentException or
            NotSupportedException or
            PathTooLongException)
        {
            return false;
        }
    }

    public static string CreateOutputPath(
        string inputFilePath,
        string outputExtension,
        string? outputDirectory,
        string suffix)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(inputFilePath);
        ArgumentException.ThrowIfNullOrWhiteSpace(outputExtension);

        var normalizedExtension = outputExtension.StartsWith('.')
            ? outputExtension
            : $".{outputExtension}";
        var normalizedSuffix = suffix ?? string.Empty;

        var directory = string.IsNullOrWhiteSpace(outputDirectory)
            ? Path.GetDirectoryName(inputFilePath)
            : Path.GetFullPath(outputDirectory);

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

    public static string CreateUniqueOutputPath(string outputPath, ISet<string>? usedOutputPaths = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(outputPath);

        var directory = Path.GetDirectoryName(outputPath)
            ?? throw new InvalidOperationException("输出路径缺少有效目录。");
        var fileNameWithoutExtension = Path.GetFileNameWithoutExtension(outputPath);
        var extension = Path.GetExtension(outputPath);
        var candidatePath = outputPath;
        var suffixIndex = 2;

        while ((usedOutputPaths?.Contains(candidatePath) ?? false) ||
               File.Exists(candidatePath) ||
               Directory.Exists(candidatePath))
        {
            candidatePath = Path.Combine(directory, $"{fileNameWithoutExtension}_{suffixIndex}{extension}");
            suffixIndex++;
        }

        usedOutputPaths?.Add(candidatePath);
        return candidatePath;
    }

    public static string CreateVideoConversionOutputPath(
        string inputFilePath,
        string outputExtension,
        string? outputDirectory = null) =>
        CreateOutputPath(inputFilePath, outputExtension, outputDirectory, string.Empty);

    public static string CreateVideoTrackOutputPath(
        string inputFilePath,
        string outputExtension,
        string? outputDirectory = null) =>
        CreateOutputPath(inputFilePath, outputExtension, outputDirectory, "_video");

    public static string CreateAudioTrackOutputPath(
        string inputFilePath,
        string outputExtension,
        string? outputDirectory = null) =>
        CreateOutputPath(inputFilePath, outputExtension, outputDirectory, "_audio");

    public static string CreateSubtitleTrackOutputPath(
        string inputFilePath,
        string outputExtension,
        string? outputDirectory = null) =>
        CreateOutputPath(inputFilePath, outputExtension, outputDirectory, "_subtitle");

    public static string CreateAudioConversionOutputPath(
        string inputFilePath,
        string outputExtension,
        string? outputDirectory = null) =>
        CreateOutputPath(inputFilePath, outputExtension, outputDirectory, string.Empty);

    public static string CreateTrimOutputPath(
        string inputFilePath,
        string outputExtension,
        string? outputDirectory = null) =>
        CreateOutputPath(inputFilePath, outputExtension, outputDirectory, "_trim");

    public static string CreateMergeOutputPath(
        string inputFilePath,
        string outputExtension,
        string? outputDirectory = null,
        string? outputFileNameWithoutExtension = null) =>
        string.IsNullOrWhiteSpace(outputFileNameWithoutExtension)
            ? CreateOutputPath(inputFilePath, outputExtension, outputDirectory, "_merged")
            : CreateOutputPathWithFileName(inputFilePath, outputExtension, outputDirectory, outputFileNameWithoutExtension);

    public static string CreateOutputPathWithFileName(
        string inputFilePath,
        string outputExtension,
        string? outputDirectory,
        string outputFileNameWithoutExtension)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(inputFilePath);
        ArgumentException.ThrowIfNullOrWhiteSpace(outputExtension);
        ArgumentException.ThrowIfNullOrWhiteSpace(outputFileNameWithoutExtension);

        var normalizedExtension = outputExtension.StartsWith('.')
            ? outputExtension
            : $".{outputExtension}";
        var directory = string.IsNullOrWhiteSpace(outputDirectory)
            ? Path.GetDirectoryName(inputFilePath)
            : Path.GetFullPath(outputDirectory);

        if (string.IsNullOrWhiteSpace(directory))
        {
            throw new InvalidOperationException("输入文件路径缺少有效目录。");
        }

        var fileNameWithoutExtension = SanitizeOutputFileName(outputFileNameWithoutExtension);
        var outputPath = Path.Combine(directory, $"{fileNameWithoutExtension}{normalizedExtension}");

        if (string.Equals(outputPath, inputFilePath, StringComparison.OrdinalIgnoreCase))
        {
            outputPath = Path.Combine(directory, $"{fileNameWithoutExtension}_output{normalizedExtension}");
        }

        return outputPath;
    }

    public static string SanitizeOutputFileName(string outputFileNameWithoutExtension)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(outputFileNameWithoutExtension);

        var sanitized = Path.GetFileNameWithoutExtension(outputFileNameWithoutExtension.Trim());
        if (string.IsNullOrWhiteSpace(sanitized))
        {
            return string.Empty;
        }

        foreach (var invalidChar in Path.GetInvalidFileNameChars())
        {
            sanitized = sanitized.Replace(invalidChar, '_');
        }

        sanitized = sanitized.Trim().TrimEnd('.');
        return sanitized;
    }
}
