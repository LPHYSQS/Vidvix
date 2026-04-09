using System;
using System.Collections.Generic;
using System.IO;

namespace Vidvix.Core.Models;

public sealed class ApplicationConfiguration
{
    public string ApplicationTitle { get; init; } = "Vidvix";

    public string ApplicationIconRelativePath { get; init; } = Path.Combine("Assets", "logo.ico");

    public string FFmpegExecutableFileName { get; init; } = "ffmpeg.exe";

    public string FFprobeExecutableFileName { get; init; } = "ffprobe.exe";

    public string LocalDataDirectoryName { get; init; } = "Vidvix";

    public string UserPreferencesFileName { get; init; } = "user-preferences.json";

    public string RuntimeDirectoryName { get; init; } = "Tools";

    public string BundledRuntimeDirectoryName { get; init; } = "ffmpeg";

    public string RuntimeVendorDirectoryName { get; init; } = "MediaEngine";

    public string RuntimeCurrentVersionDirectoryName { get; init; } = "Current";

    public string RuntimeDownloadCacheDirectoryName { get; init; } = "Downloads";

    public Uri FFmpegArchiveUri { get; init; } =
        new("https://github.com/BtbN/FFmpeg-Builds/releases/download/latest/ffmpeg-master-latest-win64-lgpl-shared.zip");

    public Uri FFmpegChecksumUri { get; init; } =
        new("https://github.com/BtbN/FFmpeg-Builds/releases/download/latest/checksums.sha256");

    public string FFmpegArchiveFileName { get; init; } = "ffmpeg-master-latest-win64-lgpl-shared.zip";

    public bool OverwriteOutputFiles { get; init; } = true;

    public bool MirrorLogsToConsole { get; init; } = true;

    public TimeSpan? DefaultExecutionTimeout { get; init; }

    public IReadOnlyList<string> SupportedInputFileTypes { get; init; } =
        new[] { ".mp4", ".mkv", ".mov", ".avi", ".wmv", ".m4v", ".flv", ".webm", ".ts", ".m2ts", ".mpeg", ".mpg" };

    public IReadOnlyList<ProcessingModeOption> SupportedProcessingModes { get; init; } =
        new[]
        {
            new ProcessingModeOption(
                ProcessingMode.VideoConvert,
                "视频格式转换",
                "默认优先保留原始视频和音频流，遇到目标封装不兼容时会自动转码为兼容的编码。"),
            new ProcessingModeOption(
                ProcessingMode.VideoTrackExtract,
                "视频轨道提取",
                "仅输出视频轨道，不包含音频，对于特定封装会自动转换到兼容的视频编码。"),
            new ProcessingModeOption(
                ProcessingMode.AudioTrackExtract,
                "音频轨道提取",
                "默认提取第一条音频轨道，并按所选音频格式输出单独文件。")
        };

    public IReadOnlyList<OutputFormatOption> SupportedVideoOutputFormats { get; init; } =
        new[]
        {
            new OutputFormatOption("MP4", ".mp4", "兼容性最好，适合常见播放器和移动设备。"),
            new OutputFormatOption("MKV", ".mkv", "封装更宽松，更适合保留原始编码和长视频素材。"),
            new OutputFormatOption("MOV", ".mov", "适合部分剪辑软件和 Apple 工作流。"),
            new OutputFormatOption("AVI", ".avi", "传统视频容器，适合老牌软件或基础兼容场景。"),
            new OutputFormatOption("WMV", ".wmv", "适合 Windows 系统和部分老旧播放环境。"),
            new OutputFormatOption("M4V", ".m4v", "兼容 MP4 工作流，适合部分移动设备和 Apple 生态。"),
            new OutputFormatOption("FLV", ".flv", "适合老的流媒体或特定支持 FLV 的平台。"),
            new OutputFormatOption("WEBM", ".webm", "面向网页和浏览器场景，适合更重视开放媒体格式的分发。"),
            new OutputFormatOption("TS", ".ts", "适合封装流媒体或广播侧素材。"),
            new OutputFormatOption("M2TS", ".m2ts", "适合蓝光或高码率传输场景。"),
            new OutputFormatOption("MPEG", ".mpeg", "适合传统视频工作流和部分广播参考场景。"),
            new OutputFormatOption("MPG", ".mpg", "同为 MPEG 容器的常见扩展名，适合需要 .mpg 后缀的场景。")
        };

    public IReadOnlyList<OutputFormatOption> SupportedAudioOutputFormats { get; init; } =
        new[]
        {
            new OutputFormatOption("MP3", ".mp3", "通用音频格式，兼容性高。"),
            new OutputFormatOption("M4A", ".m4a", "AAC 封装，音质和体积较平衡。"),
            new OutputFormatOption("AAC", ".aac", "原始 AAC 音频流。"),
            new OutputFormatOption("WAV", ".wav", "无压缩音频，适合继续编辑。"),
            new OutputFormatOption("FLAC", ".flac", "无损压缩音频，适合存档。")
        };
}

