using System;

namespace Vidvix.Core.Models;

public sealed record AudioVideoComposeExportRequest(
    string VideoSourcePath,
    string VideoSourceName,
    TimeSpan VideoDuration,
    double VideoFrameRate,
    bool VideoHasAudio,
    string? VideoCodecName,
    string VideoContainerExtension,
    string AudioSourcePath,
    string AudioSourceName,
    TimeSpan AudioDuration,
    string? AudioCodecName,
    string AudioContainerExtension,
    string OutputPath,
    OutputFormatOption OutputFormat,
    TranscodingMode TranscodingMode,
    bool IsGpuAccelerationRequested,
    VideoAccelerationKind VideoAccelerationKind,
    AudioVideoComposeReferenceMode ReferenceMode,
    AudioVideoComposeVideoExtendMode VideoExtendMode,
    double ImportedAudioVolumeDecibels,
    bool MixOriginalVideoAudio,
    double OriginalVideoAudioVolumeDecibels,
    bool EnableImportedAudioFadeIn,
    TimeSpan ImportedAudioFadeInDuration,
    bool EnableImportedAudioFadeOut,
    TimeSpan ImportedAudioFadeOutDuration)
{
    public TimeSpan OutputDuration => ReferenceMode == AudioVideoComposeReferenceMode.Video
        ? VideoDuration
        : AudioDuration;

    public bool ShouldLoopImportedAudio =>
        ReferenceMode == AudioVideoComposeReferenceMode.Video &&
        AudioDuration > TimeSpan.Zero &&
        AudioDuration < VideoDuration;

    public bool ShouldLoopVideo =>
        ReferenceMode == AudioVideoComposeReferenceMode.Audio &&
        VideoDuration > TimeSpan.Zero &&
        VideoDuration < AudioDuration &&
        VideoExtendMode == AudioVideoComposeVideoExtendMode.Loop;

    public bool ShouldFreezeLastFrame =>
        ReferenceMode == AudioVideoComposeReferenceMode.Audio &&
        VideoDuration > TimeSpan.Zero &&
        VideoDuration < AudioDuration &&
        VideoExtendMode == AudioVideoComposeVideoExtendMode.FreezeLastFrame;

    public bool IncludeOriginalVideoAudio => MixOriginalVideoAudio && VideoHasAudio;
}
