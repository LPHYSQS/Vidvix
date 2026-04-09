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
    private string _headerTitle = "\u89c6\u9891\u8be6\u60c5";
    private string _headerSubtitle = "\u70b9\u51fb\u961f\u5217\u4e2d\u7684\u8be6\u60c5\u6309\u94ae\u67e5\u770b\u5a92\u4f53\u4fe1\u606f\u3002";
    private string _currentInputPath = string.Empty;
    private string _errorMessage = string.Empty;
    private bool _isOpen;
    private bool _isLoading;

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
        (OverviewFields.Count > 0 || VideoFields.Count > 0 || AudioFields.Count > 0 || AdvancedFields.Count > 0);

    public Visibility LoadingVisibility => IsLoading ? Visibility.Visible : Visibility.Collapsed;

    public Visibility ErrorVisibility => HasError ? Visibility.Visible : Visibility.Collapsed;

    public Visibility ContentVisibility => HasContent ? Visibility.Visible : Visibility.Collapsed;

    public string OverviewSummaryText => FormatFields(OverviewFields);

    public string VideoSummaryText => FormatFields(VideoFields);

    public string AudioSummaryText => FormatFields(AudioFields);

    public string AdvancedSummaryText => FormatFields(AdvancedFields);

    public void ShowLoading(string title, string inputPath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(title);
        ArgumentException.ThrowIfNullOrWhiteSpace(inputPath);

        HeaderTitle = title;
        HeaderSubtitle = inputPath;
        CurrentInputPath = inputPath;
        ErrorMessage = string.Empty;
        ClearAllFields();
        IsLoading = true;
        IsOpen = true;
    }

    public void ShowDetails(MediaDetailsSnapshot snapshot)
    {
        ArgumentNullException.ThrowIfNull(snapshot);

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

    public void ShowError(string title, string inputPath, string errorMessage)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(title);
        ArgumentException.ThrowIfNullOrWhiteSpace(inputPath);
        ArgumentException.ThrowIfNullOrWhiteSpace(errorMessage);

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
    }

    private void NotifyStateVisualsChanged()
    {
        OnPropertyChanged(nameof(HasError));
        OnPropertyChanged(nameof(HasContent));
        OnPropertyChanged(nameof(LoadingVisibility));
        OnPropertyChanged(nameof(ErrorVisibility));
        OnPropertyChanged(nameof(ContentVisibility));
    }
}
