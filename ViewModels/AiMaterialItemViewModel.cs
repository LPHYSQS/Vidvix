using System;
using System.IO;
using Vidvix.Core.Interfaces;
using Vidvix.Utils;

namespace Vidvix.ViewModels;

public sealed class AiMaterialItemViewModel : ObservableObject
{
    private readonly ILocalizationService? _localizationService;
    private bool _isActive;
    private string _durationText;
    private string _resolutionText;
    private string _frameRateText;

    public AiMaterialItemViewModel(
        string inputPath,
        string? durationText = null,
        string? resolutionText = null,
        string? frameRateText = null,
        ILocalizationService? localizationService = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(inputPath);

        _localizationService = localizationService;
        InputPath = Path.GetFullPath(inputPath);
        InputFileName = Path.GetFileName(InputPath);
        InputFileNameWithoutExtension = Path.GetFileNameWithoutExtension(InputPath);
        InputDirectory = Path.GetDirectoryName(InputPath) ?? string.Empty;
        FileExtensionText = Path.GetExtension(InputPath).TrimStart('.').ToUpperInvariant();
        _durationText = NormalizeDisplayValue(durationText);
        _resolutionText = NormalizeDisplayValue(resolutionText);
        _frameRateText = NormalizeDisplayValue(frameRateText);
    }

    public string InputPath { get; }

    public string InputFileName { get; }

    public string InputFileNameWithoutExtension { get; }

    public string InputDirectory { get; }

    public string FileExtensionText { get; }

    public string InputPathToolTipText => InputPath;

    public string MediaTypeText =>
        GetLocalizedText("common.workspace.video.mediaLabel", "视频");

    public string DurationText =>
        string.IsNullOrWhiteSpace(_durationText)
            ? GetLocalizedText("ai.page.materials.unknownDuration", "未知时长")
            : _durationText;

    public string ResolutionText =>
        string.IsNullOrWhiteSpace(_resolutionText)
            ? GetLocalizedText("ai.page.workspace.mediaMetrics.unknown", "未知")
            : _resolutionText;

    public string FrameRateText =>
        string.IsNullOrWhiteSpace(_frameRateText)
            ? GetLocalizedText("ai.page.workspace.mediaMetrics.unknown", "未知")
            : _frameRateText;

    public double SelectionOutlineOpacity => IsActive ? 1d : 0d;

    public bool IsActive
    {
        get => _isActive;
        set
        {
            if (SetProperty(ref _isActive, value))
            {
                OnPropertyChanged(nameof(SelectionOutlineOpacity));
            }
        }
    }

    public void RefreshLocalization()
    {
        OnPropertyChanged(nameof(MediaTypeText));
        OnPropertyChanged(nameof(DurationText));
        OnPropertyChanged(nameof(ResolutionText));
        OnPropertyChanged(nameof(FrameRateText));
    }

    private string GetLocalizedText(string key, string fallback) =>
        _localizationService?.GetString(key, fallback) ?? fallback;

    private static string NormalizeDisplayValue(string? value) =>
        string.IsNullOrWhiteSpace(value)
            ? string.Empty
            : value.Trim();
}
