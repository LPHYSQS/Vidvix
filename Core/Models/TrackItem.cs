using System;
using Microsoft.UI.Xaml;
using Vidvix.Utils;

namespace Vidvix.Core.Models;

public sealed class TrackItem : ObservableObject
{
    private string _sourceName;
    private string _durationText;
    private double _visualWidth;
    private bool _isVideo;
    private int _sequenceNumber;
    private bool _isResolutionPreset;

    public TrackItem(string sourceName, string durationText, double visualWidth, bool isVideo, int sequenceNumber)
    {
        _sourceName = string.IsNullOrWhiteSpace(sourceName)
            ? throw new ArgumentException("轨道片段名称不能为空。", nameof(sourceName))
            : sourceName;
        _durationText = string.IsNullOrWhiteSpace(durationText)
            ? throw new ArgumentException("轨道片段时长文本不能为空。", nameof(durationText))
            : durationText;
        _visualWidth = visualWidth > 0
            ? visualWidth
            : throw new ArgumentOutOfRangeException(nameof(visualWidth));
        _isVideo = isVideo;
        _sequenceNumber = sequenceNumber > 0
            ? sequenceNumber
            : throw new ArgumentOutOfRangeException(nameof(sequenceNumber));
    }

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
            }
        }
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

    public string TypeDisplayText => IsVideo ? "视频片段" : "音频片段";

    public string SummaryText => $"{TypeDisplayText} · {DurationText}";

    public string ResolutionPresetLabelText => "分辨率预设";

    public Visibility ResolutionPresetBadgeVisibility =>
        IsResolutionPreset ? Visibility.Visible : Visibility.Collapsed;
}
