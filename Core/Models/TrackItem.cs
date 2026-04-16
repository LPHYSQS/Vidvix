using System;
using Microsoft.UI.Xaml;
using Vidvix.Utils;

namespace Vidvix.Core.Models;

public sealed class TrackItem : ObservableObject
{
    private readonly Guid _trackId = Guid.NewGuid();
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
        bool hasEmbeddedAudioStream = false)
    {
        _sourceName = string.IsNullOrWhiteSpace(sourceName)
            ? throw new ArgumentException("轨道片段名称不能为空。", nameof(sourceName))
            : sourceName;
        _sourcePath = sourcePath ?? string.Empty;
        _durationText = string.IsNullOrWhiteSpace(durationText)
            ? throw new ArgumentException("轨道片段时长文本不能为空。", nameof(durationText))
            : durationText;
        _durationSeconds = durationSeconds >= 0
            ? durationSeconds
            : throw new ArgumentOutOfRangeException(nameof(durationSeconds));
        _resolutionText = string.IsNullOrWhiteSpace(resolutionText)
            ? "未知参数"
            : resolutionText;
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
        get => _durationText;
        set
        {
            if (SetProperty(ref _durationText, value))
            {
                OnPropertyChanged(nameof(TypeDisplayText));
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
        get => _resolutionText;
        set
        {
            var normalizedValue = string.IsNullOrWhiteSpace(value) ? "未知参数" : value;
            if (SetProperty(ref _resolutionText, normalizedValue))
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

    public string DisplayName => $"{SequenceNumberText} · {SourceName}";

    public string SequenceNumberText => SequenceNumber.ToString("00");

    public TimeSpan? KnownDuration => DurationSeconds > 0
        ? TimeSpan.FromSeconds(DurationSeconds)
        : null;

    public string TypeDisplayText => IsVideo ? "视频片段" : "音频片段";

    public string SummaryText => $"{TypeDisplayText} · {DurationText}";

    public string ResolutionDisplayText => IsVideo
        ? $"原始分辨率 · {ResolutionText}"
        : $"原始音频参数 · {ResolutionText}";

    public string ResolutionPresetLabelText => IsVideo ? "分辨率预设" : "参数预设";

    public Visibility ResolutionPresetBadgeVisibility =>
        IsResolutionPreset ? Visibility.Visible : Visibility.Collapsed;

    public bool CanSetAsResolutionPreset => IsSourceAvailable;

    public Visibility InvalidStateVisibility =>
        IsSourceAvailable ? Visibility.Collapsed : Visibility.Visible;

    public double NormalOutlineOpacity => IsSourceAvailable ? 1d : 0d;

    public string SourceAvailabilityStatusText => "素材已从列表移除，不参与当前合并";
}
