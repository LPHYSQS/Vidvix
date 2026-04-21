using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Text;
using Microsoft.UI.Xaml;
using Vidvix.Core.Interfaces;
using Vidvix.Core.Models;
using Vidvix.Utils;

namespace Vidvix.ViewModels;

public sealed class MediaDetailPanelViewModel : ObservableObject
{
    private readonly ILocalizationService _localizationService;
    private string _headerTitle = string.Empty;
    private string _headerSubtitle = string.Empty;
    private string _currentInputPath = string.Empty;
    private string _errorMessage = string.Empty;
    private bool _isOpen;
    private bool _isLoading;
    private ProcessingWorkspaceKind _workspaceKind = ProcessingWorkspaceKind.Video;

    public MediaDetailPanelViewModel(ILocalizationService localizationService)
    {
        _localizationService = localizationService ?? throw new ArgumentNullException(nameof(localizationService));
        OverviewFields = new ObservableCollection<MediaDetailField>();
        VideoFields = new ObservableCollection<MediaDetailField>();
        AudioFields = new ObservableCollection<MediaDetailField>();
        AdvancedFields = new ObservableCollection<MediaDetailField>();
        ApplyDefaultHeaderText();
    }

    public ObservableCollection<MediaDetailField> OverviewFields { get; }

    public ObservableCollection<MediaDetailField> VideoFields { get; }

    public ObservableCollection<MediaDetailField> AudioFields { get; }

    public ObservableCollection<MediaDetailField> AdvancedFields { get; }

    public string HeaderTitle
    {
        get => _headerTitle;
        private set => SetProperty(ref _headerTitle, value);
    }

    public string HeaderSubtitle
    {
        get => _headerSubtitle;
        private set => SetProperty(ref _headerSubtitle, value);
    }

    public string CurrentInputPath
    {
        get => _currentInputPath;
        private set => SetProperty(ref _currentInputPath, value);
    }

    public string ErrorMessage
    {
        get => _errorMessage;
        private set
        {
            if (SetProperty(ref _errorMessage, value))
            {
                NotifyStateVisualsChanged();
            }
        }
    }

    public bool IsOpen
    {
        get => _isOpen;
        private set => SetProperty(ref _isOpen, value);
    }

    public bool IsLoading
    {
        get => _isLoading;
        private set
        {
            if (SetProperty(ref _isLoading, value))
            {
                NotifyStateVisualsChanged();
            }
        }
    }

    public bool HasError => !string.IsNullOrWhiteSpace(ErrorMessage);

    public bool HasContent =>
        !IsLoading &&
        !HasError &&
        (_workspaceKind == ProcessingWorkspaceKind.Video
            ? OverviewFields.Count > 0 || VideoFields.Count > 0 || AudioFields.Count > 0 || AdvancedFields.Count > 0
            : GetAudioOverviewFields().Count > 0 || AudioFields.Count > 0);

    public Visibility LoadingVisibility => IsLoading ? Visibility.Visible : Visibility.Collapsed;

    public Visibility ErrorVisibility => HasError ? Visibility.Visible : Visibility.Collapsed;

    public Visibility ContentVisibility => HasContent ? Visibility.Visible : Visibility.Collapsed;

    public Visibility VideoTemplateVisibility =>
        HasContent && _workspaceKind == ProcessingWorkspaceKind.Video ? Visibility.Visible : Visibility.Collapsed;

    public Visibility AudioTemplateVisibility =>
        HasContent && _workspaceKind == ProcessingWorkspaceKind.Audio ? Visibility.Visible : Visibility.Collapsed;

    public Visibility VideoOverviewVisibility => GetFieldsVisibility(OverviewFields);

    public Visibility VideoInfoVisibility => GetFieldsVisibility(VideoFields);

    public Visibility AudioInfoVisibility => GetFieldsVisibility(AudioFields);

    public Visibility AdvancedVisibility => GetFieldsVisibility(AdvancedFields);

    public Visibility AudioOverviewVisibility => GetFieldsVisibility(GetAudioOverviewFields());

    public string BackButtonText =>
        GetLocalizedText("mediaDetails.action.back", "返回");

    public string CopyAllButtonText =>
        GetLocalizedText("mediaDetails.action.copyAll", "复制全部");

    public string CopyButtonText =>
        GetLocalizedText("mediaDetails.action.copy", "复制");

    public string CopyToastFallbackText =>
        GetLocalizedText("mediaDetails.copy.feedback.default", "已复制");

    public string LoadingText =>
        GetLocalizedText("mediaDetails.state.loading", "正在解析媒体信息...");

    public string ErrorTitleText =>
        GetLocalizedText("mediaDetails.state.error", "无法解析");

    public string OverviewSectionTitle =>
        GetLocalizedText("mediaDetails.section.overview", "概览");

    public string VideoInfoSectionTitle =>
        GetLocalizedText("mediaDetails.section.video", "视频信息");

    public string AudioInfoSectionTitle =>
        GetLocalizedText("mediaDetails.section.audio", "音频信息");

    public string AdvancedSectionTitle =>
        GetLocalizedText("mediaDetails.section.advanced", "高级信息");

    public string AudioOverviewSectionTitle =>
        GetLocalizedText("mediaDetails.section.audioOverview", "音频概览");

    public string OverviewSummaryText => FormatFields(OverviewFields);

    public string VideoSummaryText => FormatFields(VideoFields);

    public string AudioSummaryText => FormatFields(AudioFields);

    public string AdvancedSummaryText => FormatFields(AdvancedFields);

    public string AudioOverviewSummaryText => FormatFields(GetAudioOverviewFields());

    public bool CanCopySection(object? parameter)
    {
        if (!IsOpen || !HasContent)
        {
            return false;
        }

        return parameter switch
        {
            "Overview" => OverviewFields.Count > 0,
            "Video" => VideoFields.Count > 0,
            "Audio" => AudioFields.Count > 0,
            "Advanced" => AdvancedFields.Count > 0,
            "AudioOverview" => GetAudioOverviewFields().Count > 0,
            _ => false
        };
    }

    public bool TryBuildCopyText(object? parameter, out string text, out string feedbackMessage)
    {
        text = string.Empty;
        feedbackMessage = string.Empty;

        if (!IsOpen || !HasContent)
        {
            return false;
        }

        if (parameter is null or "All")
        {
            text = BuildAllCopyText();
            feedbackMessage = GetLocalizedText("mediaDetails.copy.feedback.all", "已复制全部详情");
            return !string.IsNullOrWhiteSpace(text);
        }

        if (!TryResolveSection(parameter, out var title, out var fields))
        {
            return false;
        }

        text = BuildSectionCopyText(title, fields);
        feedbackMessage = FormatLocalizedText(
            "mediaDetails.copy.feedback.section",
            $"已复制{title}",
            ("section", title));
        return !string.IsNullOrWhiteSpace(text);
    }

    public void ShowLoading(string title, string inputPath, ProcessingWorkspaceKind workspaceKind)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(title);
        ArgumentException.ThrowIfNullOrWhiteSpace(inputPath);

        SetWorkspaceKind(workspaceKind);
        HeaderTitle = title;
        HeaderSubtitle = inputPath;
        CurrentInputPath = inputPath;
        ErrorMessage = string.Empty;
        ClearAllFields();
        IsLoading = true;
        IsOpen = true;
    }

    public void ShowDetails(MediaDetailsSnapshot snapshot, ProcessingWorkspaceKind workspaceKind)
    {
        ArgumentNullException.ThrowIfNull(snapshot);

        SetWorkspaceKind(workspaceKind);
        HeaderTitle = snapshot.FileName;
        HeaderSubtitle = snapshot.InputPath;
        CurrentInputPath = snapshot.InputPath;
        ErrorMessage = string.Empty;
        ReplaceFields(OverviewFields, snapshot.OverviewFields);
        ReplaceFields(VideoFields, snapshot.VideoFields);
        ReplaceFields(AudioFields, snapshot.AudioFields);
        ReplaceFields(AdvancedFields, snapshot.AdvancedFields);
        NotifySummaryTextChanged();
        IsLoading = false;
        NotifyStateVisualsChanged();
        IsOpen = true;
    }

    public void ShowError(string title, string inputPath, string errorMessage, ProcessingWorkspaceKind workspaceKind)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(title);
        ArgumentException.ThrowIfNullOrWhiteSpace(inputPath);
        ArgumentException.ThrowIfNullOrWhiteSpace(errorMessage);

        SetWorkspaceKind(workspaceKind);
        HeaderTitle = title;
        HeaderSubtitle = inputPath;
        CurrentInputPath = inputPath;
        ClearAllFields();
        IsLoading = false;
        ErrorMessage = errorMessage;
        IsOpen = true;
    }

    public void Close()
    {
        IsLoading = false;
        IsOpen = false;
    }

    public void RefreshLocalization()
    {
        if (!IsOpen && string.IsNullOrWhiteSpace(CurrentInputPath))
        {
            ApplyDefaultHeaderText();
        }

        NotifyLocalizationChanged();
        NotifySummaryTextChanged();
        NotifyStateVisualsChanged();
    }

    private void ApplyDefaultHeaderText()
    {
        HeaderTitle = GetLocalizedText("mediaDetails.header.title", "媒体详情");
        HeaderSubtitle = GetLocalizedText("mediaDetails.header.subtitle", "点击队列中的详情按钮查看媒体信息。");
    }

    private void ClearAllFields()
    {
        OverviewFields.Clear();
        VideoFields.Clear();
        AudioFields.Clear();
        AdvancedFields.Clear();
        NotifySummaryTextChanged();
        NotifyStateVisualsChanged();
    }

    private static string FormatFields(IEnumerable<MediaDetailField> fields)
    {
        var lines = fields
            .Where(field => !string.IsNullOrWhiteSpace(field.Label) || !string.IsNullOrWhiteSpace(field.Value))
            .Select(field => $"{field.Label}: {field.Value}")
            .ToArray();

        return lines.Length == 0 ? string.Empty : string.Join(Environment.NewLine, lines);
    }

    private static string FormatFieldsForCopy(IEnumerable<MediaDetailField> fields)
    {
        var lines = fields
            .Where(field => !string.IsNullOrWhiteSpace(field.Label) || !string.IsNullOrWhiteSpace(field.Value))
            .Select(field => $"{field.Label}：{field.Value}")
            .ToArray();

        return lines.Length == 0 ? string.Empty : string.Join(Environment.NewLine, lines);
    }

    private string BuildAllCopyText()
    {
        var builder = new StringBuilder();

        AppendLineIfPresent(builder, $"{GetLocalizedText("mediaDetails.copy.field.fileName", "文件名")}：{HeaderTitle}");
        AppendLineIfPresent(builder, $"{GetLocalizedText("mediaDetails.copy.field.filePath", "文件路径")}：{HeaderSubtitle}");

        if (builder.Length > 0)
        {
            builder.AppendLine();
        }

        if (_workspaceKind == ProcessingWorkspaceKind.Video)
        {
            AppendSection(builder, OverviewSectionTitle, OverviewFields);
            AppendSection(builder, VideoInfoSectionTitle, VideoFields);
            AppendSection(builder, AudioInfoSectionTitle, AudioFields);
            AppendSection(builder, AdvancedSectionTitle, AdvancedFields);
        }
        else
        {
            AppendSection(builder, AudioOverviewSectionTitle, GetAudioOverviewFields());
            AppendSection(builder, AudioInfoSectionTitle, AudioFields);
        }

        return builder.ToString().Trim();
    }

    private static string BuildSectionCopyText(string title, IReadOnlyList<MediaDetailField> fields)
    {
        var summary = FormatFieldsForCopy(fields);
        return string.IsNullOrWhiteSpace(summary)
            ? string.Empty
            : $"{title}{Environment.NewLine}{summary}";
    }

    private static void AppendSection(StringBuilder builder, string title, IReadOnlyList<MediaDetailField> fields)
    {
        var summary = BuildSectionCopyText(title, fields);
        if (string.IsNullOrWhiteSpace(summary))
        {
            return;
        }

        if (builder.Length > 0)
        {
            builder.AppendLine();
        }

        builder.Append(summary);
    }

    private static void AppendLineIfPresent(StringBuilder builder, string text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return;
        }

        builder.AppendLine(text);
    }

    private bool TryResolveSection(object? parameter, out string title, out IReadOnlyList<MediaDetailField> fields)
    {
        switch (parameter)
        {
            case "Overview" when OverviewFields.Count > 0:
                title = OverviewSectionTitle;
                fields = OverviewFields.ToArray();
                return true;
            case "Video" when VideoFields.Count > 0:
                title = VideoInfoSectionTitle;
                fields = VideoFields.ToArray();
                return true;
            case "Audio" when AudioFields.Count > 0:
                title = AudioInfoSectionTitle;
                fields = AudioFields.ToArray();
                return true;
            case "Advanced" when AdvancedFields.Count > 0:
                title = AdvancedSectionTitle;
                fields = AdvancedFields.ToArray();
                return true;
            case "AudioOverview":
                var audioOverviewFields = GetAudioOverviewFields();
                if (audioOverviewFields.Count > 0)
                {
                    title = AudioOverviewSectionTitle;
                    fields = audioOverviewFields;
                    return true;
                }

                break;
        }

        title = string.Empty;
        fields = Array.Empty<MediaDetailField>();
        return false;
    }

    private void ReplaceFields(ObservableCollection<MediaDetailField> target, IReadOnlyList<MediaDetailField> source)
    {
        target.Clear();

        foreach (var field in source)
        {
            target.Add(field);
        }
    }

    private void NotifySummaryTextChanged()
    {
        OnPropertyChanged(nameof(OverviewSummaryText));
        OnPropertyChanged(nameof(VideoSummaryText));
        OnPropertyChanged(nameof(AudioSummaryText));
        OnPropertyChanged(nameof(AdvancedSummaryText));
        OnPropertyChanged(nameof(AudioOverviewSummaryText));
    }

    private void NotifyStateVisualsChanged()
    {
        OnPropertyChanged(nameof(HasError));
        OnPropertyChanged(nameof(HasContent));
        OnPropertyChanged(nameof(LoadingVisibility));
        OnPropertyChanged(nameof(ErrorVisibility));
        OnPropertyChanged(nameof(ContentVisibility));
        OnPropertyChanged(nameof(VideoTemplateVisibility));
        OnPropertyChanged(nameof(AudioTemplateVisibility));
        OnPropertyChanged(nameof(VideoOverviewVisibility));
        OnPropertyChanged(nameof(VideoInfoVisibility));
        OnPropertyChanged(nameof(AudioInfoVisibility));
        OnPropertyChanged(nameof(AdvancedVisibility));
        OnPropertyChanged(nameof(AudioOverviewVisibility));
    }

    private void NotifyLocalizationChanged()
    {
        OnPropertyChanged(nameof(BackButtonText));
        OnPropertyChanged(nameof(CopyAllButtonText));
        OnPropertyChanged(nameof(CopyButtonText));
        OnPropertyChanged(nameof(CopyToastFallbackText));
        OnPropertyChanged(nameof(LoadingText));
        OnPropertyChanged(nameof(ErrorTitleText));
        OnPropertyChanged(nameof(OverviewSectionTitle));
        OnPropertyChanged(nameof(VideoInfoSectionTitle));
        OnPropertyChanged(nameof(AudioInfoSectionTitle));
        OnPropertyChanged(nameof(AdvancedSectionTitle));
        OnPropertyChanged(nameof(AudioOverviewSectionTitle));
    }

    private IReadOnlyList<MediaDetailField> GetAudioOverviewFields()
    {
        var excludedLabels = GetAudioOverviewExcludedLabels();
        return OverviewFields
            .Where(field => !excludedLabels.Contains(field.Label))
            .ToArray();
    }

    private HashSet<string> GetAudioOverviewExcludedLabels() =>
        new(StringComparer.Ordinal)
        {
            GetLocalizedText("mediaDetails.field.resolution", "分辨率"),
            "分辨率",
            "Resolution"
        };

    private Visibility GetFieldsVisibility(IEnumerable<MediaDetailField> fields)
    {
        if (_workspaceKind == ProcessingWorkspaceKind.Audio &&
            (ReferenceEquals(fields, VideoFields) || ReferenceEquals(fields, AdvancedFields)))
        {
            return Visibility.Collapsed;
        }

        return fields.Any()
            ? Visibility.Visible
            : Visibility.Collapsed;
    }

    private void SetWorkspaceKind(ProcessingWorkspaceKind workspaceKind)
    {
        if (_workspaceKind == workspaceKind)
        {
            return;
        }

        _workspaceKind = workspaceKind;
        NotifyStateVisualsChanged();
        NotifySummaryTextChanged();
    }

    private string GetLocalizedText(string key, string fallback) =>
        _localizationService.GetString(key, fallback);

    private string FormatLocalizedText(
        string key,
        string fallback,
        params (string Name, object? Value)[] arguments)
    {
        if (arguments.Length == 0)
        {
            return GetLocalizedText(key, fallback);
        }

        var localizedArguments = arguments.ToDictionary(
            argument => argument.Name,
            argument => argument.Value,
            StringComparer.Ordinal);
        return _localizationService.Format(key, localizedArguments, fallback);
    }
}
