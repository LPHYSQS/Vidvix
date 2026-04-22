using System;
using System.Collections.Generic;
using Vidvix.Core.Interfaces;
using Vidvix.Utils;

namespace Vidvix.Core.Models;

public sealed class MediaItem : ObservableObject
{
    private readonly ILocalizationService? _localizationService;
    private string _fileName;
    private string _sourcePath;
    private string _durationText;
    private int _durationSeconds;
    private bool _isVideo;
    private string _resolutionText;

    public MediaItem(
        string fileName,
        string durationText,
        int durationSeconds,
        bool isVideo,
        string? sourcePath = null,
        string? resolutionText = null,
        ILocalizationService? localizationService = null)
    {
        _localizationService = localizationService;
        _fileName = string.IsNullOrWhiteSpace(fileName)
            ? throw new ArgumentException("素材文件名不能为空。", nameof(fileName))
            : fileName;
        _sourcePath = sourcePath ?? string.Empty;
        _durationText = NormalizeDisplayValue(durationText);
        _durationSeconds = durationSeconds >= 0
            ? durationSeconds
            : throw new ArgumentOutOfRangeException(nameof(durationSeconds));
        _isVideo = isVideo;
        _resolutionText = NormalizeDisplayValue(resolutionText);
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

    public string ResolutionText
    {
        get => string.IsNullOrWhiteSpace(_resolutionText)
            ? GetLocalizedText(
                IsVideo ? "merge.page.item.unknown.resolution" : "merge.page.item.unknown.audioParameters",
                IsVideo ? "未知分辨率" : "未知音频参数")
            : _resolutionText;
        set
        {
            SetProperty(ref _resolutionText, NormalizeDisplayValue(value));
        }
    }

    public bool IsAudio => !IsVideo;

    internal string RawDurationText => _durationText;

    internal string RawResolutionText => _resolutionText;

    public string TypeDisplayText =>
        GetLocalizedText(
            IsVideo ? "merge.page.item.mediaType.video" : "merge.page.item.mediaType.audio",
            IsVideo ? "视频" : "音频");

    public string SummaryText =>
        FormatLocalizedText(
            "merge.page.item.summary",
            "{type} · {duration}",
            ("type", TypeDisplayText),
            ("duration", DurationText));

    public void RefreshLocalization()
    {
        OnPropertyChanged(nameof(DurationText));
        OnPropertyChanged(nameof(ResolutionText));
        OnPropertyChanged(nameof(TypeDisplayText));
        OnPropertyChanged(nameof(SummaryText));
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
