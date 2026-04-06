using System;
using System.Collections.Generic;
using System.IO;

namespace Vidvix.Core.Models;

public sealed class ApplicationConfiguration
{
    public string ApplicationTitle { get; init; } = "Vidvix";

    public string ApplicationIconRelativePath { get; init; } = Path.Combine("Assets", "logo.ico");

    public string FFmpegExecutableFileName { get; init; } = "ffmpeg.exe";

    public string LocalDataDirectoryName { get; init; } = "Vidvix";

    public string RuntimeDirectoryName { get; init; } = "Tools";

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
                "视频无损转换",
                "保留视频和音频流，默认使用封装级无损复制，不提供额外编码参数。"),
            new ProcessingModeOption(
                ProcessingMode.VideoTrackExtract,
                "视频轨道提取",
                "只输出视频轨道，不包含音频，默认使用无损视频流复制。"),
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
            new OutputFormatOption("TS", ".ts", "适合封装流媒体或广播侧素材。")
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
