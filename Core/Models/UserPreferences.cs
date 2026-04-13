namespace Vidvix.Core.Models;

public sealed record UserPreferences
{
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

    public double PreferredTrimPreviewVolumePercent { get; init; } = 80d;

    public ThemePreference ThemePreference { get; init; } = ThemePreference.UseSystem;

    public bool RevealOutputFileAfterProcessing { get; init; } = true;

    public TranscodingMode PreferredTranscodingMode { get; init; } = TranscodingMode.FastContainerConversion;

    public bool EnableGpuAccelerationForTranscoding { get; init; }

    public WindowPlacementPreference? MainWindowPlacement { get; init; }
}
