using System;
using Vidvix.Utils;

namespace Vidvix.Core.Models;

public sealed class MediaItem : ObservableObject
{
    private string _fileName;
    private string _sourcePath;
    private string _durationText;
    private int _durationSeconds;
    private bool _isVideo;

    public MediaItem(string fileName, string durationText, int durationSeconds, bool isVideo, string? sourcePath = null)
    {
        _fileName = string.IsNullOrWhiteSpace(fileName)
            ? throw new ArgumentException("素材文件名不能为空。", nameof(fileName))
            : fileName;
        _sourcePath = sourcePath ?? string.Empty;
        _durationText = string.IsNullOrWhiteSpace(durationText)
            ? throw new ArgumentException("素材时长文本不能为空。", nameof(durationText))
            : durationText;
        _durationSeconds = durationSeconds >= 0
            ? durationSeconds
            : throw new ArgumentOutOfRangeException(nameof(durationSeconds));
        _isVideo = isVideo;
    }

    public string FileName
    {
        get => _fileName;
        set => SetProperty(ref _fileName, value);
    }

    public string SourcePath
    {
        get => _sourcePath;
        set => SetProperty(ref _sourcePath, value);
    }

    public string DurationText
    {
        get => _durationText;
        set
        {
            if (SetProperty(ref _durationText, value))
            {
                OnPropertyChanged(nameof(SummaryText));
                OnPropertyChanged(nameof(TypeDisplayText));
            }
        }
    }

    public int DurationSeconds
    {
        get => _durationSeconds;
        set => SetProperty(ref _durationSeconds, value);
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

    public string TypeDisplayText => IsVideo ? "视频" : "音频";

    public string SummaryText => $"{TypeDisplayText} · {DurationText}";
}
