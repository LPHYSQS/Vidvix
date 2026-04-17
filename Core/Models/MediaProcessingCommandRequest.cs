using System;

namespace Vidvix.Core.Models;

/// <summary>
/// 描述一次 FFmpeg 命令构建所需的完整输入。
/// </summary>
public sealed class MediaProcessingCommandRequest
{
    public MediaProcessingCommandRequest(
        string runtimeExecutablePath,
        string inputPath,
        string outputPath,
        MediaProcessingContext context,
        MediaDetailsSnapshot? inputSnapshot = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(runtimeExecutablePath);
        ArgumentException.ThrowIfNullOrWhiteSpace(inputPath);
        ArgumentException.ThrowIfNullOrWhiteSpace(outputPath);

        RuntimeExecutablePath = runtimeExecutablePath;
        InputPath = inputPath;
        OutputPath = outputPath;
        Context = context;
        InputSnapshot = inputSnapshot;
    }

    public string RuntimeExecutablePath { get; }

    public string InputPath { get; }

    public string OutputPath { get; }

    public MediaProcessingContext Context { get; }

    public MediaDetailsSnapshot? InputSnapshot { get; }
}
