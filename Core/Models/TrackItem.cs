using System;
using Vidvix.Utils;

namespace Vidvix.Core.Models;

public sealed class TrackItem : ObservableObject
{
    private string _displayName;
    private string _durationText;
    private double _visualWidth;
    private bool _isVideo;

    public TrackItem(string displayName, string durationText, double visualWidth, bool isVideo)
    {
        _displayName = string.IsNullOrWhiteSpace(displayName)
            ? throw new ArgumentException("轨道片段名称不能为空。", nameof(displayName))
            : displayName;
        _durationText = string.IsNullOrWhiteSpace(durationText)
            ? throw new ArgumentException("轨道片段时长文本不能为空。", nameof(durationText))
            : durationText;
        _visualWidth = visualWidth > 0
            ? visualWidth
            : throw new ArgumentOutOfRangeException(nameof(visualWidth));
        _isVideo = isVideo;
    }

    public string DisplayName
    {
        get => _displayName;
        set => SetProperty(ref _displayName, value);
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

    public bool IsAudio => !IsVideo;

    public string TypeDisplayText => IsVideo ? "视频片段" : "音频片段";

    public string SummaryText => $"{TypeDisplayText} · {DurationText}";
}
