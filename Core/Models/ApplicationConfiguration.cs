using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace Vidvix.Core.Models;

public sealed class ApplicationConfiguration
{
    private static readonly IReadOnlyList<string> DefaultSupportedVideoInputFileTypes =
        new[] { ".mp4", ".mkv", ".mov", ".avi", ".wmv", ".m4v", ".flv", ".webm", ".ts", ".m2ts", ".mpeg", ".mpg" };

    private static readonly IReadOnlyList<string> DefaultSupportedAudioInputFileTypes =
        new[] { ".mp3", ".m4a", ".aac", ".wav", ".flac", ".wma", ".ogg", ".opus", ".aiff", ".aif", ".mka" };

    private static readonly IReadOnlyList<string> DefaultSupportedTrimInputFileTypes =
        DefaultSupportedVideoInputFileTypes
            .Concat(DefaultSupportedAudioInputFileTypes)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();

    private static readonly IReadOnlyList<string> DefaultSupportedSplitAudioInputFileTypes =
        DefaultSupportedTrimInputFileTypes;

    private static readonly IReadOnlyList<string> DefaultSupportedAiInputFileTypes =
        DefaultSupportedVideoInputFileTypes;

    public string ApplicationTitle { get; init; } = "Vidvix";

    public string ApplicationIconRelativePath { get; init; } = Path.Combine("Assets", "logo.ico");

    public string FFmpegExecutableFileName { get; init; } = "ffmpeg.exe";

    public string FFprobeExecutableFileName { get; init; } = "ffprobe.exe";

    public string FFplayExecutableFileName { get; init; } = "ffplay.exe";

    public string LocalDataDirectoryName { get; init; } = "Vidvix";

    public string UserPreferencesFileName { get; init; } = "user-preferences.json";

    public string DefaultUiLanguage { get; init; } = "zh-CN";

    public string FallbackUiLanguage { get; init; } = "zh-CN";

    public string SecondaryUiLanguage { get; init; } = "en-US";

    public string LocalizationResourceRelativePath { get; init; } = Path.Combine("Resources", "Localization");

    public string LocalizationManifestFileName { get; init; } = "manifest.json";

    public string RuntimeDirectoryName { get; init; } = "Tools";

    public string BundledRuntimeDirectoryName { get; init; } = "ffmpeg";

    public string MpvBundledRuntimeDirectoryName { get; init; } = "mpv";

    public string MpvLibraryFileName { get; init; } = "mpv-1.dll";

    public IReadOnlyList<string> MpvSupportDllFileNames { get; init; } =
        new[] { "d3dcompiler_43.dll" };

    public string ThumbnailCacheDirectoryName { get; init; } = "ThumbnailCache";

    public string AudioWaveformCacheDirectoryName { get; init; } = "AudioWaveformCache";

    public string RuntimeVendorDirectoryName { get; init; } = "MediaEngine";

    public string RuntimeCurrentVersionDirectoryName { get; init; } = "Current";

    public string RuntimeDownloadCacheDirectoryName { get; init; } = "Downloads";

    public string DemucsDirectoryName { get; init; } = "Demucs";

    public string AiRuntimeDirectoryName { get; init; } = "AI";

    public string AiLicensesDirectoryName { get; init; } = "Licenses";

    public string AiManifestsDirectoryName { get; init; } = "Manifests";

    public string AiRuntimeProbeCacheDirectoryName { get; init; } = "ProbeCache";

    public string RifeDirectoryName { get; init; } = "Rife";

    public string RifeExecutableFileName { get; init; } = "rife-ncnn-vulkan.exe";

    public IReadOnlyList<string> RifeSupportLibraryFileNames { get; init; } =
        new[] { "vcomp140.dll" };

    public string RifeManifestFileName { get; init; } = "rife.json";

    public IReadOnlyList<string> RifeLicenseFileNames { get; init; } =
        new[] { "rife-ncnn-vulkan-LICENSE.txt" };

    public string RifeModelDirectoryName { get; init; } = "rife-v4.6";

    public string RifeModelConfigFileName { get; init; } = "flownet.param";

    public string RifeModelWeightFileName { get; init; } = "flownet.bin";

    public string RealEsrganDirectoryName { get; init; } = "RealEsrgan";

    public string RealEsrganExecutableFileName { get; init; } = "realesrgan-ncnn-vulkan.exe";

    public IReadOnlyList<string> RealEsrganSupportLibraryFileNames { get; init; } =
        new[] { "vcomp140.dll" };

    public string RealEsrganManifestFileName { get; init; } = "realesrgan.json";

    public IReadOnlyList<string> RealEsrganLicenseFileNames { get; init; } =
        new[] { "realesrgan-ncnn-vulkan-LICENSE.txt", "Real-ESRGAN-LICENSE.txt" };

    public string RealEsrganStandardModelName { get; init; } = "realesrgan-x4plus";

    public string RealEsrganStandardModelConfigFileName { get; init; } = "realesrgan-x4plus.param";

    public string RealEsrganStandardModelWeightFileName { get; init; } = "realesrgan-x4plus.bin";

    public string RealEsrganAnimeModelName { get; init; } = "realesr-animevideov3";

    public string RealEsrganAnimeX2ModelConfigFileName { get; init; } = "realesr-animevideov3-x2.param";

    public string RealEsrganAnimeX2ModelWeightFileName { get; init; } = "realesr-animevideov3-x2.bin";

    public string RealEsrganAnimeX4ModelConfigFileName { get; init; } = "realesr-animevideov3-x4.param";

    public string RealEsrganAnimeX4ModelWeightFileName { get; init; } = "realesr-animevideov3-x4.bin";

    public string DemucsRuntimeDirectoryName { get; init; } = "Current";

    public string DemucsGpuRuntimeDirectoryName { get; init; } = "CurrentGpu";

    public string DemucsCudaRuntimeDirectoryName { get; init; } = "CurrentGpuCuda";

    public string DemucsPackagesDirectoryName { get; init; } = "Packages";

    public string DemucsModelsDirectoryName { get; init; } = "Models";

    public string DemucsRuntimeArchiveFileName { get; init; } = "demucs-runtime-win-x64-cpu.zip";

    public string DemucsGpuRuntimeArchiveFileName { get; init; } = "demucs-runtime-win-x64-gpu.zip";

    public string DemucsCudaRuntimeArchiveFileName { get; init; } = "demucs-runtime-win-x64-gpu-cuda.zip";

    public string DemucsModelArchiveFileName { get; init; } = "demucs-model-htdemucs_ft.zip";

    public string DemucsPythonExecutableFileName { get; init; } = "python.exe";

    public string DemucsModelName { get; init; } = "htdemucs_ft";

    public string DemucsScriptsDirectoryName { get; init; } = "Scripts";

    public string DemucsRunnerScriptFileName { get; init; } = "demucs_runner.py";

    public IReadOnlyList<string> DemucsRequiredModelFileNames { get; init; } =
        new[]
        {
            "htdemucs_ft.yaml",
            "f7e0c4bc-ba3fe64a.th",
            "d12395a8-e57c48e6.th",
            "92cfc3b6-ef3bcb9c.th",
            "04573f0d-f3cf25b2.th"
        };

    public IReadOnlyList<DemucsAccelerationModeOption> SupportedSplitAudioAccelerationModes { get; init; } =
        new[]
        {
            new DemucsAccelerationModeOption(
                DemucsAccelerationMode.Cpu,
                "CPU 兼容模式",
                "固定使用内置 CPU 运行时拆音，兼容性最高，适合所有机器。",
                displayNameKey: "common.splitAudio.acceleration.cpu.displayName",
                descriptionKey: "common.splitAudio.acceleration.cpu.description"),
            new DemucsAccelerationModeOption(
                DemucsAccelerationMode.GpuPreferred,
                "GPU 优先（独显 -> 核显 -> CPU）",
                "先尝试独立显卡，NVIDIA 独显优先走 CUDA，其它 GPU 再走 DirectML；如果 GPU 不可用，则自动回退到 CPU。",
                displayNameKey: "common.splitAudio.acceleration.gpuPreferred.displayName",
                descriptionKey: "common.splitAudio.acceleration.gpuPreferred.description")
        };

    public Uri FFmpegArchiveUri { get; init; } =
        new("https://github.com/BtbN/FFmpeg-Builds/releases/download/latest/ffmpeg-master-latest-win64-lgpl-shared.zip");

    public Uri FFmpegChecksumUri { get; init; } =
        new("https://github.com/BtbN/FFmpeg-Builds/releases/download/latest/checksums.sha256");

    public string FFmpegArchiveFileName { get; init; } = "ffmpeg-master-latest-win64-lgpl-shared.zip";

    public bool OverwriteOutputFiles { get; init; } = false;

    public bool MirrorLogsToConsole { get; init; } = true;

    public TimeSpan? DefaultExecutionTimeout { get; init; }

    public IReadOnlyList<string> SupportedVideoInputFileTypes { get; init; } = DefaultSupportedVideoInputFileTypes;

    public IReadOnlyList<string> SupportedAudioInputFileTypes { get; init; } = DefaultSupportedAudioInputFileTypes;

    public IReadOnlyList<string> SupportedTrimInputFileTypes { get; init; } = DefaultSupportedTrimInputFileTypes;

    public IReadOnlyList<string> SupportedSplitAudioInputFileTypes { get; init; } = DefaultSupportedSplitAudioInputFileTypes;

    public IReadOnlyList<string> SupportedAiInputFileTypes { get; init; } = DefaultSupportedAiInputFileTypes;

    public IReadOnlyDictionary<ProcessingWorkspaceKind, ProcessingWorkspaceProfile> WorkspaceProfiles { get; init; } =
        new Dictionary<ProcessingWorkspaceKind, ProcessingWorkspaceProfile>
        {
            [ProcessingWorkspaceKind.Video] = new(
                ProcessingWorkspaceKind.Video,
                "视频",
                "视频文件",
                DefaultSupportedVideoInputFileTypes,
                headerTitle: "视频处理",
                headerDescription: "批量处理视频，支持提取轨道。",
                localizationKeyPrefix: "common.workspace.video"),
            [ProcessingWorkspaceKind.Audio] = new(
                ProcessingWorkspaceKind.Audio,
                "音频",
                "音频文件",
                DefaultSupportedAudioInputFileTypes,
                fixedProcessingModeDisplayName: "音频格式转换",
                fixedProcessingModeDescription: "将音频文件转换为目标格式，支持多种音频格式之间互相转换。",
                headerTitle: "音频处理",
                headerDescription: "批量转换音频格式。",
                localizationKeyPrefix: "common.workspace.audio"),
            [ProcessingWorkspaceKind.Trim] = new(
                ProcessingWorkspaceKind.Trim,
                "裁剪",
                "音频或视频文件",
                DefaultSupportedTrimInputFileTypes,
                fixedProcessingModeDisplayName: "媒体裁剪",
                fixedProcessingModeDescription: "导入单个音频或视频文件后，按所选起止时间精确导出对应片段。",
                headerTitle: "媒体裁剪",
                headerDescription: "导入单个媒体后按时间裁剪。",
                localizationKeyPrefix: "common.workspace.trim"),
            [ProcessingWorkspaceKind.Merge] = new(
                ProcessingWorkspaceKind.Merge,
                "合并",
                "音频或视频文件",
                DefaultSupportedTrimInputFileTypes,
                fixedProcessingModeDisplayName: "素材合并",
                fixedProcessingModeDescription: "统一编排多段音视频素材，完成拼接、混音与音视频合成。",
                headerTitle: "媒体合并",
                headerDescription: "拼接音视频并完成合成。",
                localizationKeyPrefix: "common.workspace.merge"),
            [ProcessingWorkspaceKind.Ai] = new(
                ProcessingWorkspaceKind.Ai,
                "AI",
                "视频文件",
                DefaultSupportedAiInputFileTypes,
                headerTitle: "AI 工作区",
                headerDescription: "承载 AI补帧 与 AI增强 的独立工作区。",
                localizationKeyPrefix: "common.workspace.ai"),
            [ProcessingWorkspaceKind.SplitAudio] = new(
                ProcessingWorkspaceKind.SplitAudio,
                "拆音",
                "音频或视频文件",
                DefaultSupportedSplitAudioInputFileTypes,
                fixedProcessingModeDisplayName: "拆音分离",
                fixedProcessingModeDescription: "导入一个音频或视频文件，先标准化为临时 WAV，再由 Demucs 分离为 vocals、drums、bass、other 四轨。",
                headerTitle: "拆音",
                headerDescription: "导入单个媒体并输出 Demucs 四轨结果。",
                localizationKeyPrefix: "common.workspace.splitAudio"),
            [ProcessingWorkspaceKind.Terminal] = new(
                ProcessingWorkspaceKind.Terminal,
                "终端",
                "终端内容",
                Array.Empty<string>(),
                fixedProcessingModeDisplayName: "终端",
                fixedProcessingModeDescription: "集中输入 FFmpeg、FFprobe、FFplay 命令，并查看执行输出与状态。",
                headerTitle: "终端",
                headerDescription: "集中执行 FFmpeg 系列命令。",
                localizationKeyPrefix: "common.workspace.terminal")
        };

    public IReadOnlyDictionary<MergeWorkspaceMode, MergeWorkspaceModeProfile> MergeModeProfiles { get; init; } =
        new Dictionary<MergeWorkspaceMode, MergeWorkspaceModeProfile>
        {
            [MergeWorkspaceMode.VideoJoin] = new(
                MergeWorkspaceMode.VideoJoin,
                displayName: "视频拼接",
                selectionMessage: "已切换到视频拼接模式。",
                timelineHintText: "当前为视频拼接模式，仅可将视频素材添加到视频轨道。",
                videoTrackEmptyText: "从素材列表单击视频文件，可将其添加到视频轨道。",
                audioTrackEmptyText: "当前模式聚焦视频拼接，音频轨道暂不参与编排。",
                supportsVideoTrackInput: true,
                supportsAudioTrackInput: false,
                replaceVideoTrackOnAdd: false,
                replaceAudioTrackOnAdd: false,
                showsVideoJoinTimeline: true,
                showsAudioJoinTimeline: false,
                showsStandardTimeline: false,
                rejectAudioInputMessage: "当前是视频拼接模式，请选择视频素材加入视频轨道。",
                localizationKeyPrefix: "merge.mode.videoJoin"),
            [MergeWorkspaceMode.AudioJoin] = new(
                MergeWorkspaceMode.AudioJoin,
                displayName: "音频拼接",
                selectionMessage: "已切换到音频拼接模式。",
                timelineHintText: "当前为音频拼接模式，仅可将音频素材添加到音频轨道。",
                videoTrackEmptyText: "当前模式聚焦音频拼接，视频轨道暂不参与编排。",
                audioTrackEmptyText: "从素材列表单击音频文件，可将其添加到音频轨道。",
                supportsVideoTrackInput: false,
                supportsAudioTrackInput: true,
                replaceVideoTrackOnAdd: false,
                replaceAudioTrackOnAdd: false,
                showsVideoJoinTimeline: false,
                showsAudioJoinTimeline: true,
                showsStandardTimeline: false,
                rejectVideoInputMessage: "当前是音频拼接模式，请选择音频素材加入音频轨道。",
                localizationKeyPrefix: "merge.mode.audioJoin"),
            [MergeWorkspaceMode.AudioVideoCompose] = new(
                MergeWorkspaceMode.AudioVideoCompose,
                displayName: "音视频合成",
                selectionMessage: "已切换到音视频合成模式。",
                timelineHintText: "当前为音视频合成模式，请分别添加 1 个视频和 1 个音频。",
                videoTrackEmptyText: "从素材列表单击一个视频文件，可将其放入视频轨道；再次添加会自动替换当前视频。",
                audioTrackEmptyText: "从素材列表单击一个音频文件，可将其放入音频轨道；再次添加会自动替换当前音频。",
                supportsVideoTrackInput: true,
                supportsAudioTrackInput: true,
                replaceVideoTrackOnAdd: true,
                replaceAudioTrackOnAdd: true,
                showsVideoJoinTimeline: false,
                showsAudioJoinTimeline: false,
                showsStandardTimeline: true,
                localizationKeyPrefix: "merge.mode.audioVideoCompose")
        };

    public IReadOnlyList<ProcessingModeOption> SupportedProcessingModes { get; init; } =
        new[]
        {
            new ProcessingModeOption(
                ProcessingMode.VideoConvert,
                "视频格式转换",
                "默认优先保留原始视频和音频流，遇到目标封装不兼容时会自动转码为兼容的编码。",
                displayNameKey: "common.processingMode.videoConvert.displayName",
                descriptionKey: "common.processingMode.videoConvert.description"),
            new ProcessingModeOption(
                ProcessingMode.VideoTrackExtract,
                "视频轨道提取",
                "仅输出视频轨道，不包含音频，对于特定封装会自动转换到兼容的视频编码。",
                displayNameKey: "common.processingMode.videoTrackExtract.displayName",
                descriptionKey: "common.processingMode.videoTrackExtract.description"),
            new ProcessingModeOption(
                ProcessingMode.AudioTrackExtract,
                "音频轨道提取",
                "默认提取第一条音频轨道，并按所选音频格式输出单独文件。",
                displayNameKey: "common.processingMode.audioTrackExtract.displayName",
                descriptionKey: "common.processingMode.audioTrackExtract.description"),
            new ProcessingModeOption(
                ProcessingMode.SubtitleTrackExtract,
                "字幕轨道提取",
                "默认提取第一条字幕轨道；文本字幕会按目标格式输出，图形字幕建议导出为 MKS 以保留原始字幕编码。",
                displayNameKey: "common.processingMode.subtitleTrackExtract.displayName",
                descriptionKey: "common.processingMode.subtitleTrackExtract.description")
        };

    public IReadOnlyList<OutputFormatOption> SupportedVideoOutputFormats { get; init; } =
        new[]
        {
            new OutputFormatOption("MP4", ".mp4", "兼容性最好，适合常见播放器和移动设备。", descriptionKey: "common.outputFormat.video.mp4.description"),
            new OutputFormatOption("MKV", ".mkv", "封装更宽松，更适合保留原始编码和长视频素材。", descriptionKey: "common.outputFormat.video.mkv.description"),
            new OutputFormatOption("MOV", ".mov", "适合部分剪辑软件和 Apple 工作流。", descriptionKey: "common.outputFormat.video.mov.description"),
            new OutputFormatOption("AVI", ".avi", "传统视频容器，适合老牌软件或基础兼容场景。", descriptionKey: "common.outputFormat.video.avi.description"),
            new OutputFormatOption("WMV", ".wmv", "适合 Windows 系统和部分老旧播放环境。", descriptionKey: "common.outputFormat.video.wmv.description"),
            new OutputFormatOption("M4V", ".m4v", "兼容 MP4 工作流，适合部分移动设备和 Apple 生态。", descriptionKey: "common.outputFormat.video.m4v.description"),
            new OutputFormatOption("FLV", ".flv", "适合老的流媒体或特定支持 FLV 的平台。", descriptionKey: "common.outputFormat.video.flv.description"),
            new OutputFormatOption("WEBM", ".webm", "面向网页和浏览器场景，适合更重视开放媒体格式的分发。", descriptionKey: "common.outputFormat.video.webm.description"),
            new OutputFormatOption("TS", ".ts", "适合封装流媒体或广播侧素材。", descriptionKey: "common.outputFormat.video.ts.description"),
            new OutputFormatOption("M2TS", ".m2ts", "适合蓝光或高码率传输场景。", descriptionKey: "common.outputFormat.video.m2ts.description"),
            new OutputFormatOption("MPEG", ".mpeg", "适合传统视频工作流和部分广播参考场景。", descriptionKey: "common.outputFormat.video.mpeg.description"),
            new OutputFormatOption("MPG", ".mpg", "同为 MPEG 容器的常见扩展名，适合需要 .mpg 后缀的场景。", descriptionKey: "common.outputFormat.video.mpg.description")
        };

    public IReadOnlyList<OutputFormatOption> SupportedTrimOutputFormats { get; init; } =
        new[]
        {
            new OutputFormatOption("MP4", ".mp4", "兼容性最好，适合常见播放器和移动设备。", descriptionKey: "common.outputFormat.trim.mp4.description"),
            new OutputFormatOption("MKV", ".mkv", "封装更宽松，更适合保留高质量剪辑片段。", descriptionKey: "common.outputFormat.trim.mkv.description"),
            new OutputFormatOption("MOV", ".mov", "适合部分剪辑软件和 Apple 工作流。", descriptionKey: "common.outputFormat.trim.mov.description"),
            new OutputFormatOption("AVI", ".avi", "传统视频容器，适合老牌软件或基础兼容场景。", descriptionKey: "common.outputFormat.trim.avi.description"),
            new OutputFormatOption("WMV", ".wmv", "适合 Windows 系统和部分老旧播放环境。", descriptionKey: "common.outputFormat.trim.wmv.description"),
            new OutputFormatOption("M4V", ".m4v", "兼容 MP4 工作流，适合部分移动设备和 Apple 生态。", descriptionKey: "common.outputFormat.trim.m4v.description"),
            new OutputFormatOption("FLV", ".flv", "适合老的流媒体或特定支持 FLV 的平台。", descriptionKey: "common.outputFormat.trim.flv.description"),
            new OutputFormatOption("WEBM", ".webm", "面向网页和浏览器场景，适合开放媒体格式分发。", descriptionKey: "common.outputFormat.trim.webm.description"),
            new OutputFormatOption("TS", ".ts", "适合流媒体封装或广播侧片段输出。", descriptionKey: "common.outputFormat.trim.ts.description"),
            new OutputFormatOption("M2TS", ".m2ts", "适合蓝光或高码率传输片段场景。", descriptionKey: "common.outputFormat.trim.m2ts.description"),
            new OutputFormatOption("MPEG", ".mpeg", "适合传统视频工作流和部分广播参考场景。", descriptionKey: "common.outputFormat.trim.mpeg.description"),
            new OutputFormatOption("MPG", ".mpg", "同为 MPEG 容器的常见扩展名，适合需要 .mpg 后缀的场景。", descriptionKey: "common.outputFormat.trim.mpg.description")
        };

    public IReadOnlyList<OutputFormatOption> SupportedAudioOutputFormats { get; init; } =
        new[]
        {
            new OutputFormatOption("MP3", ".mp3", "通用音频格式，兼容性高。", descriptionKey: "common.outputFormat.audio.mp3.description"),
            new OutputFormatOption("M4A", ".m4a", "AAC 封装，音质和体积较平衡。", descriptionKey: "common.outputFormat.audio.m4a.description"),
            new OutputFormatOption("AAC", ".aac", "原始 AAC 音频流。", descriptionKey: "common.outputFormat.audio.aac.description"),
            new OutputFormatOption("WAV", ".wav", "无压缩音频，适合继续编辑。", descriptionKey: "common.outputFormat.audio.wav.description"),
            new OutputFormatOption("FLAC", ".flac", "无损压缩音频，适合存档。", descriptionKey: "common.outputFormat.audio.flac.description"),
            new OutputFormatOption("WMA", ".wma", "适合部分 Windows 播放环境和旧式工作流。", descriptionKey: "common.outputFormat.audio.wma.description"),
            new OutputFormatOption("OGG", ".ogg", "开放音频容器，适合 Vorbis 音频分发。", descriptionKey: "common.outputFormat.audio.ogg.description"),
            new OutputFormatOption("OPUS", ".opus", "现代低码率高音质格式，适合语音和流媒体。", descriptionKey: "common.outputFormat.audio.opus.description"),
            new OutputFormatOption("AIFF", ".aiff", "无压缩音频，常见于部分专业音频和 Apple 工作流。", descriptionKey: "common.outputFormat.audio.aiff.description"),
            new OutputFormatOption("AIF", ".aif", "AIFF 的常见扩展名，适合需要 .aif 后缀的场景。", descriptionKey: "common.outputFormat.audio.aif.description"),
            new OutputFormatOption("MKA", ".mka", "Matroska Audio 容器，适合保留原始音频编码输出。", descriptionKey: "common.outputFormat.audio.mka.description")
        };

    public IReadOnlyList<OutputFormatOption> SupportedSubtitleOutputFormats { get; init; } =
        new[]
        {
            new OutputFormatOption("SRT", ".srt", "通用文本字幕格式，兼容性最好，适合常见播放器和字幕平台。", descriptionKey: "common.outputFormat.subtitle.srt.description"),
            new OutputFormatOption("ASS", ".ass", "适合保留字幕样式、定位和特效信息，常用于进阶字幕工作流。", descriptionKey: "common.outputFormat.subtitle.ass.description"),
            new OutputFormatOption("SSA", ".ssa", "适合兼容部分旧式字幕编辑或播放工作流。", descriptionKey: "common.outputFormat.subtitle.ssa.description"),
            new OutputFormatOption("VTT", ".vtt", "WebVTT 字幕格式，适合网页和 HTML5 播放器。", descriptionKey: "common.outputFormat.subtitle.vtt.description"),
            new OutputFormatOption("TTML", ".ttml", "标准化文本字幕格式，适合部分平台交换和发布流程。", descriptionKey: "common.outputFormat.subtitle.ttml.description"),
            new OutputFormatOption("MKS", ".mks", "Matroska 字幕容器，适合尽量保留原始字幕编码，包括图形字幕。", descriptionKey: "common.outputFormat.subtitle.mks.description")
        };
}
