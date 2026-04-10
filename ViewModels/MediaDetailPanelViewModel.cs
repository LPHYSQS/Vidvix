using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using Microsoft.UI.Xaml;
using Vidvix.Core.Models;
using Vidvix.Utils;

namespace Vidvix.ViewModels;

public sealed class MediaDetailPanelViewModel : ObservableObject
{
    private static readonly string[] AudioOverviewExcludedLabels = ["\u5206\u8fa8\u7387"];

    private string _headerTitle = "\u5a92\u4f53\u8be6\u60c5";
    private string _headerSubtitle = "\u70b9\u51fb\u961f\u5217\u4e2d\u7684\u8be6\u60c5\u6309\u94ae\u67e5\u770b\u5a92\u4f53\u4fe1\u606f\u3002";
    private string _currentInputPath = string.Empty;
    private string _errorMessage = string.Empty;
    private bool _isOpen;
    private bool _isLoading;
    private ProcessingWorkspaceKind _workspaceKind = ProcessingWorkspaceKind.Video;

    public MediaDetailPanelViewModel()
    {
        OverviewFields = new ObservableCollection<MediaDetailField>();
        VideoFields = new ObservableCollection<MediaDetailField>();
        AudioFields = new ObservableCollection<MediaDetailField>();
        AdvancedFields = new ObservableCollection<MediaDetailField>();
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

    public string OverviewSummaryText => FormatFields(OverviewFields);

    public string VideoSummaryText => FormatFields(VideoFields);

    public string AudioSummaryText => FormatFields(AudioFields);

    public string AdvancedSummaryText => FormatFields(AdvancedFields);

    public string AudioOverviewSummaryText => FormatFields(GetAudioOverviewFields());

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

    private IReadOnlyList<MediaDetailField> GetAudioOverviewFields() =>
        OverviewFields
            .Where(field => !AudioOverviewExcludedLabels.Contains(field.Label, StringComparer.Ordinal))
            .ToArray();

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
}
