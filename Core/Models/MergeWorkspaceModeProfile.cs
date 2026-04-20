using System;
using Vidvix.Core.Interfaces;

namespace Vidvix.Core.Models;

/// <summary>
/// 集中定义单个合并模式的轨道能力与界面文案，避免这些规则散落在 ViewModel 中。
/// </summary>
public sealed class MergeWorkspaceModeProfile
{
    public MergeWorkspaceModeProfile(
        MergeWorkspaceMode mode,
        string displayName,
        string selectionMessage,
        string timelineHintText,
        string videoTrackEmptyText,
        string audioTrackEmptyText,
        bool supportsVideoTrackInput,
        bool supportsAudioTrackInput,
        bool replaceVideoTrackOnAdd,
        bool replaceAudioTrackOnAdd,
        bool showsVideoJoinTimeline,
        bool showsAudioJoinTimeline,
        bool showsStandardTimeline,
        string? rejectVideoInputMessage = null,
        string? rejectAudioInputMessage = null,
        string? localizationKeyPrefix = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(displayName);
        ArgumentException.ThrowIfNullOrWhiteSpace(selectionMessage);
        ArgumentException.ThrowIfNullOrWhiteSpace(timelineHintText);
        ArgumentException.ThrowIfNullOrWhiteSpace(videoTrackEmptyText);
        ArgumentException.ThrowIfNullOrWhiteSpace(audioTrackEmptyText);

        Mode = mode;
        DisplayName = displayName;
        SelectionMessage = selectionMessage;
        TimelineHintText = timelineHintText;
        VideoTrackEmptyText = videoTrackEmptyText;
        AudioTrackEmptyText = audioTrackEmptyText;
        SupportsVideoTrackInput = supportsVideoTrackInput;
        SupportsAudioTrackInput = supportsAudioTrackInput;
        ReplaceVideoTrackOnAdd = replaceVideoTrackOnAdd;
        ReplaceAudioTrackOnAdd = replaceAudioTrackOnAdd;
        ShowsVideoJoinTimeline = showsVideoJoinTimeline;
        ShowsAudioJoinTimeline = showsAudioJoinTimeline;
        ShowsStandardTimeline = showsStandardTimeline;
        RejectVideoInputMessage = rejectVideoInputMessage ?? string.Empty;
        RejectAudioInputMessage = rejectAudioInputMessage ?? string.Empty;
        LocalizationKeyPrefix = localizationKeyPrefix ?? string.Empty;
    }

    public MergeWorkspaceMode Mode { get; }

    public string DisplayName { get; }

    public string SelectionMessage { get; }

    public string TimelineHintText { get; }

    public string VideoTrackEmptyText { get; }

    public string AudioTrackEmptyText { get; }

    public bool SupportsVideoTrackInput { get; }

    public bool SupportsAudioTrackInput { get; }

    public bool ReplaceVideoTrackOnAdd { get; }

    public bool ReplaceAudioTrackOnAdd { get; }

    public bool ShowsVideoJoinTimeline { get; }

    public bool ShowsAudioJoinTimeline { get; }

    public bool ShowsStandardTimeline { get; }

    public string RejectVideoInputMessage { get; }

    public string RejectAudioInputMessage { get; }

    public string LocalizationKeyPrefix { get; }

    public MergeWorkspaceModeProfile Localize(ILocalizationService localizationService)
    {
        ArgumentNullException.ThrowIfNull(localizationService);

        if (string.IsNullOrWhiteSpace(LocalizationKeyPrefix))
        {
            return this;
        }

        return new MergeWorkspaceModeProfile(
            Mode,
            localizationService.GetString($"{LocalizationKeyPrefix}.displayName", DisplayName),
            localizationService.GetString($"{LocalizationKeyPrefix}.selectionMessage", SelectionMessage),
            localizationService.GetString($"{LocalizationKeyPrefix}.timelineHintText", TimelineHintText),
            localizationService.GetString($"{LocalizationKeyPrefix}.videoTrackEmptyText", VideoTrackEmptyText),
            localizationService.GetString($"{LocalizationKeyPrefix}.audioTrackEmptyText", AudioTrackEmptyText),
            SupportsVideoTrackInput,
            SupportsAudioTrackInput,
            ReplaceVideoTrackOnAdd,
            ReplaceAudioTrackOnAdd,
            ShowsVideoJoinTimeline,
            ShowsAudioJoinTimeline,
            ShowsStandardTimeline,
            LocalizeOptionalText(localizationService, $"{LocalizationKeyPrefix}.rejectVideoInputMessage", RejectVideoInputMessage),
            LocalizeOptionalText(localizationService, $"{LocalizationKeyPrefix}.rejectAudioInputMessage", RejectAudioInputMessage),
            LocalizationKeyPrefix);
    }

    private static string LocalizeOptionalText(
        ILocalizationService localizationService,
        string key,
        string fallback) =>
        string.IsNullOrWhiteSpace(fallback)
            ? string.Empty
            : localizationService.GetString(key, fallback);
}
