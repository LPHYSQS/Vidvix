using System;
using System.Collections.Generic;
using Microsoft.UI.Xaml;
using Vidvix.Core.Interfaces;
using Vidvix.Utils;

namespace Vidvix.Core.Models;

public sealed class TrackItem : ObservableObject
{
    private readonly Guid _trackId = Guid.NewGuid();
    private readonly ILocalizationService? _localizationService;
    private string _sourceName;
    private string _sourcePath;
    private string _durationText;
    private int _durationSeconds;
    private string _resolutionText;
    private double _visualWidth;
    private bool _isVideo;
    private int _sequenceNumber;
    private bool _isResolutionPreset;
    private bool _isSourceAvailable;
    private bool _hasEmbeddedAudioStream;

    public TrackItem(
        string sourceName,
        string sourcePath,
        string durationText,
        int durationSeconds,
        string resolutionText,
        double visualWidth,
        bool isVideo,
        int sequenceNumber,
        bool isSourceAvailable = true,
        bool hasEmbeddedAudioStream = false,
        ILocalizationService? localizationService = null)
    {
        _localizationService = localizationService;
        _sourceName = string.IsNullOrWhiteSpace(sourceName)
            ? throw new ArgumentException("轨道片段名称不能为空。", nameof(sourceName))
            : sourceName;
        _sourcePath = sourcePath ?? string.Empty;
        _durationText = NormalizeDisplayValue(durationText);
        _durationSeconds = durationSeconds >= 0
            ? durationSeconds
            : throw new ArgumentOutOfRangeException(nameof(durationSeconds));
        _resolutionText = NormalizeDisplayValue(resolutionText);
        _visualWidth = visualWidth > 0
            ? visualWidth
            : throw new ArgumentOutOfRangeException(nameof(visualWidth));
        _isVideo = isVideo;
        _sequenceNumber = sequenceNumber > 0
            ? sequenceNumber
            : throw new ArgumentOutOfRangeException(nameof(sequenceNumber));
        _isSourceAvailable = isSourceAvailable;
        _hasEmbeddedAudioStream = hasEmbeddedAudioStream;
    }

    public Guid TrackId => _trackId;

    public string SourceName
    {
        get => _sourceName;
        set
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(value);
            if (SetProperty(ref _sourceName, value))
            {
                OnPropertyChanged(nameof(DisplayName));
            }
        }
    }

    public string DurationText
    {
        get => string.IsNullOrWhiteSpace(_durationText)
            ? GetLocalizedText("merge.page.item.unknown.duration", "未知时长")
            : _durationText;
        set
        {
            if (SetProperty(ref _durationText, NormalizeDisplayValue(value)))
            {
                OnPropertyChanged(nameof(SummaryText));
            }
        }
    }

    public int DurationSeconds
    {
        get => _durationSeconds;
        set
        {
            ArgumentOutOfRangeException.ThrowIfNegative(value);
            if (SetProperty(ref _durationSeconds, value))
            {
                OnPropertyChanged(nameof(KnownDuration));
            }
        }
    }

    public string SourcePath
    {
        get => _sourcePath;
        set => SetProperty(ref _sourcePath, value ?? string.Empty);
    }

    public string ResolutionText
    {
        get => string.IsNullOrWhiteSpace(_resolutionText)
            ? GetLocalizedText(
                IsVideo ? "merge.page.item.unknown.resolution" : "merge.page.item.unknown.audioParameters",
                IsVideo ? "未知分辨率" : "未知音频参数")
            : _resolutionText;
        set
        {
            if (SetProperty(ref _resolutionText, NormalizeDisplayValue(value)))
            {
                OnPropertyChanged(nameof(ResolutionDisplayText));
            }
        }
    }

    public double VisualWidth
    {
        get => _visualWidth;
        set => SetProperty(ref _visualWidth, value);
    }

    public bool IsVideo
    {
        get => _isVideo;
        set
        {
            if (SetProperty(ref _isVideo, value))
            {
                OnPropertyChanged(nameof(TypeDisplayText));
                OnPropertyChanged(nameof(SummaryText));
                OnPropertyChanged(nameof(ResolutionDisplayText));
                OnPropertyChanged(nameof(ResolutionPresetLabelText));
                OnPropertyChanged(nameof(CanSetAsResolutionPreset));
            }
        }
    }

    public bool IsSourceAvailable
    {
        get => _isSourceAvailable;
        set
        {
            if (SetProperty(ref _isSourceAvailable, value))
            {
                OnPropertyChanged(nameof(CanSetAsResolutionPreset));
                OnPropertyChanged(nameof(InvalidStateVisibility));
                OnPropertyChanged(nameof(NormalOutlineOpacity));
                OnPropertyChanged(nameof(SourceAvailabilityStatusText));
            }
        }
    }

    public bool HasEmbeddedAudioStream
    {
        get => _hasEmbeddedAudioStream;
        set => SetProperty(ref _hasEmbeddedAudioStream, value);
    }

    public int SequenceNumber
    {
        get => _sequenceNumber;
        set
        {
            ArgumentOutOfRangeException.ThrowIfNegativeOrZero(value);
            if (SetProperty(ref _sequenceNumber, value))
            {
                OnPropertyChanged(nameof(DisplayName));
                OnPropertyChanged(nameof(SequenceNumberText));
            }
        }
    }

    public bool IsResolutionPreset
    {
        get => _isResolutionPreset;
        set
        {
            if (SetProperty(ref _isResolutionPreset, value))
            {
                OnPropertyChanged(nameof(ResolutionPresetBadgeVisibility));
                OnPropertyChanged(nameof(ResolutionPresetLabelText));
            }
        }
    }

    public bool IsAudio => !IsVideo;

    internal string RawDurationText => _durationText;

    internal string RawResolutionText => _resolutionText;

    public string DisplayName => $"{SequenceNumberText} · {SourceName}";

    public string SequenceNumberText => SequenceNumber.ToString("00");

    public TimeSpan? KnownDuration => DurationSeconds > 0
        ? TimeSpan.FromSeconds(DurationSeconds)
        : null;

    public string TypeDisplayText =>
        GetLocalizedText(
            IsVideo ? "merge.page.item.trackType.video" : "merge.page.item.trackType.audio",
            IsVideo ? "视频片段" : "音频片段");

    public string SummaryText =>
        FormatLocalizedText(
            "merge.page.item.summary",
            "{type} · {duration}",
            ("type", TypeDisplayText),
            ("duration", DurationText));

    public string ResolutionDisplayText => IsVideo
        ? FormatLocalizedText(
            "merge.page.item.resolution.video",
            "原始分辨率 · {value}",
            ("value", ResolutionText))
        : FormatLocalizedText(
            "merge.page.item.resolution.audio",
            "原始音频参数 · {value}",
            ("value", ResolutionText));

    public string ResolutionPresetLabelText =>
        GetLocalizedText(
            IsVideo ? "merge.page.item.presetLabel.video" : "merge.page.item.presetLabel.audio",
            IsVideo ? "分辨率预设" : "参数预设");

    public Visibility ResolutionPresetBadgeVisibility =>
        IsResolutionPreset ? Visibility.Visible : Visibility.Collapsed;

    public bool CanSetAsResolutionPreset => IsSourceAvailable;

    public Visibility InvalidStateVisibility =>
        IsSourceAvailable ? Visibility.Collapsed : Visibility.Visible;

    public double NormalOutlineOpacity => IsSourceAvailable ? 1d : 0d;

    public string SourceAvailabilityStatusText =>
        GetLocalizedText(
            "merge.page.item.sourceUnavailable",
            "素材已从列表移除，不参与当前合并");

    public void RefreshLocalization()
    {
        OnPropertyChanged(nameof(DurationText));
        OnPropertyChanged(nameof(ResolutionText));
        OnPropertyChanged(nameof(TypeDisplayText));
        OnPropertyChanged(nameof(SummaryText));
        OnPropertyChanged(nameof(ResolutionDisplayText));
        OnPropertyChanged(nameof(ResolutionPresetLabelText));
        OnPropertyChanged(nameof(SourceAvailabilityStatusText));
    }

    private string GetLocalizedText(string key, string fallback) =>
        _localizationService?.GetString(key, fallback) ?? fallback;

    private string FormatLocalizedText(string key, string fallback, params (string Name, object? Value)[] arguments)
    {
        if (_localizationService is null)
        {
            var formattedFallback = fallback;
            foreach (var argument in arguments)
            {
                formattedFallback = formattedFallback.Replace(
                    $"{{{argument.Name}}}",
                    argument.Value?.ToString() ?? string.Empty,
                    StringComparison.Ordinal);
            }

            return formattedFallback;
        }

        var localizedArguments = new Dictionary<string, object?>(arguments.Length, StringComparer.Ordinal);
        foreach (var argument in arguments)
        {
            localizedArguments[argument.Name] = argument.Value;
        }

        return _localizationService.Format(key, localizedArguments, fallback);
    }

    private static string NormalizeDisplayValue(string? value) =>
        string.IsNullOrWhiteSpace(value)
            ? string.Empty
            : value.Trim();
}
