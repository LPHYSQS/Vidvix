namespace Vidvix.Core.Models;

public sealed record UserPreferences
{
    public const string SystemUiLanguagePreferenceCode = "system";

    public ProcessingWorkspaceKind PreferredWorkspaceKind { get; init; } = ProcessingWorkspaceKind.Video;

    public ProcessingMode? PreferredProcessingMode { get; init; }

    public string? PreferredOutputFormatExtension { get; init; }

    public string? PreferredVideoConvertOutputFormatExtension { get; init; }

    public string? PreferredVideoTrackExtractOutputFormatExtension { get; init; }

    public string? PreferredAudioTrackExtractOutputFormatExtension { get; init; }

    public string? PreferredSubtitleTrackExtractOutputFormatExtension { get; init; }

    public string? PreferredTrimOutputFormatExtension { get; init; }

    public string? PreferredOutputDirectory { get; init; }

    public string? PreferredTrimOutputDirectory { get; init; }

    public string PreferredAiWorkspaceMode { get; init; } = "Interpolation";

    public string? PreferredAiInterpolationOutputFormatExtension { get; init; }

    public string? PreferredAiEnhancementOutputFormatExtension { get; init; }

    public string? PreferredAiEnhancementDevicePreference { get; init; }

    public string? PreferredSplitAudioOutputFormatExtension { get; init; }

    public string? PreferredSplitAudioOutputDirectory { get; init; }

    public DemucsAccelerationMode PreferredSplitAudioAccelerationMode { get; init; } = DemucsAccelerationMode.Cpu;

    public TranscodingMode PreferredTrimTranscodingMode { get; init; } = TranscodingMode.FastContainerConversion;

    public double PreferredTrimPreviewVolumePercent { get; init; } = 80d;

    public MergeWorkspaceMode PreferredMergeWorkspaceMode { get; init; } = MergeWorkspaceMode.AudioVideoCompose;

    public string? PreferredMergeVideoJoinOutputFormatExtension { get; init; }

    public string? PreferredMergeVideoJoinOutputDirectory { get; init; }

    public string? PreferredMergeAudioJoinOutputFormatExtension { get; init; }

    public string? PreferredMergeAudioJoinOutputDirectory { get; init; }

    public AudioJoinParameterMode PreferredMergeAudioJoinParameterMode { get; init; } = AudioJoinParameterMode.Balanced;

    public string? PreferredMergeAudioVideoComposeOutputFormatExtension { get; init; }

    public string? PreferredMergeAudioVideoComposeOutputDirectory { get; init; }

    public AudioVideoComposeReferenceMode PreferredMergeAudioVideoComposeReferenceMode { get; init; } =
        AudioVideoComposeReferenceMode.Video;

    public AudioVideoComposeVideoExtendMode PreferredMergeAudioVideoComposeVideoExtendMode { get; init; } =
        AudioVideoComposeVideoExtendMode.Loop;

    public double PreferredMergeAudioVideoComposeImportedAudioVolumeDecibels { get; init; }

    public bool PreferredMergeAudioVideoComposeMixOriginalVideoAudio { get; init; }

    public double PreferredMergeAudioVideoComposeOriginalVideoVolumeDecibels { get; init; } = -8d;

    public bool PreferredMergeAudioVideoComposeEnableFadeIn { get; init; }

    public double PreferredMergeAudioVideoComposeFadeInSeconds { get; init; } = 2d;

    public bool PreferredMergeAudioVideoComposeEnableFadeOut { get; init; }

    public double PreferredMergeAudioVideoComposeFadeOutSeconds { get; init; } = 2d;

    public MergeSmallerResolutionStrategy PreferredMergeSmallerResolutionStrategy { get; init; } =
        MergeSmallerResolutionStrategy.PadWithBlackBars;

    public MergeLargerResolutionStrategy PreferredMergeLargerResolutionStrategy { get; init; } =
        MergeLargerResolutionStrategy.SqueezeToFit;

    public ThemePreference ThemePreference { get; init; } = ThemePreference.Dark;

    public string CurrentUiLanguage { get; init; } = SystemUiLanguagePreferenceCode;

    public bool RevealOutputFileAfterProcessing { get; init; } = true;

    public bool EnableSystemTray { get; init; }

    public TranscodingMode PreferredTranscodingMode { get; init; } = TranscodingMode.FastContainerConversion;

    public bool EnableGpuAccelerationForTranscoding { get; init; }

    public WindowPlacementPreference? MainWindowPlacement { get; init; }
}
