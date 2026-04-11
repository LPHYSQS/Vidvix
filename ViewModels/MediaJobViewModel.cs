using System;
using System.IO;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media.Imaging;
using Vidvix.Core.Models;
using Vidvix.Utils;

namespace Vidvix.ViewModels;

public sealed class MediaJobViewModel : ObservableObject
{
    private MediaJobState _state = MediaJobState.Pending;
    private string _plannedOutputPath = string.Empty;
    private string _statusDetail = "\u7b49\u5f85\u5f00\u59cb";
    private BitmapImage? _thumbnailSource;
    private bool _isThumbnailLoading;

    public MediaJobViewModel(string inputPath, bool supportsThumbnail)
    {
        InputPath = Path.GetFullPath(inputPath);
        InputFileName = Path.GetFileName(InputPath);
        InputDirectory = Path.GetDirectoryName(InputPath) ?? string.Empty;
        SupportsThumbnail = supportsThumbnail;
        _isThumbnailLoading = supportsThumbnail;
    }

    public string InputPath { get; }

    public string InputFileName { get; }

    public string InputDirectory { get; }

    public bool SupportsThumbnail { get; }

    public MediaJobState State
    {
        get => _state;
        private set
        {
            if (SetProperty(ref _state, value))
            {
                OnPropertyChanged(nameof(IsPending));
                OnPropertyChanged(nameof(StatusText));
                OnPropertyChanged(nameof(StatusGlyph));
                OnPropertyChanged(nameof(StatusSummary));
            }
        }
    }

    public bool IsPending => State == MediaJobState.Pending;

    public string PlannedOutputPath
    {
        get => _plannedOutputPath;
        private set => SetProperty(ref _plannedOutputPath, value);
    }

    public string StatusText => State switch
    {
        MediaJobState.Running => "\u5904\u7406\u4e2d",
        MediaJobState.Succeeded => "\u5df2\u5b8c\u6210",
        MediaJobState.Failed => "\u5931\u8d25",
        MediaJobState.Cancelled => "\u5df2\u53d6\u6d88",
        _ => "\u5f85\u5904\u7406"
    };

    public string StatusDetail
    {
        get => _statusDetail;
        private set
        {
            if (SetProperty(ref _statusDetail, value))
            {
                OnPropertyChanged(nameof(StatusSummary));
            }
        }
    }

    public string StatusGlyph => State switch
    {
        MediaJobState.Running => "\uE895",
        MediaJobState.Succeeded => "\uE73E",
        MediaJobState.Failed => "\uEA39",
        MediaJobState.Cancelled => "\uE711",
        _ => "\uE768"
    };

    public string StatusSummary => $"{StatusText} \u00b7 {StatusDetail}";

    public BitmapImage? ThumbnailSource
    {
        get => _thumbnailSource;
        private set
        {
            if (SetProperty(ref _thumbnailSource, value))
            {
                OnPropertyChanged(nameof(HasThumbnail));
                OnPropertyChanged(nameof(ThumbnailVisibility));
                OnPropertyChanged(nameof(ThumbnailPlaceholderVisibility));
            }
        }
    }

    public bool HasThumbnail => ThumbnailSource is not null;

    public bool IsThumbnailLoading
    {
        get => _isThumbnailLoading;
        private set
        {
            if (SetProperty(ref _isThumbnailLoading, value))
            {
                OnPropertyChanged(nameof(ThumbnailProgressVisibility));
                OnPropertyChanged(nameof(ThumbnailPlaceholderIconVisibility));
                OnPropertyChanged(nameof(ThumbnailPlaceholderText));
            }
        }
    }

    public Visibility ThumbnailVisibility => HasThumbnail ? Visibility.Visible : Visibility.Collapsed;

    public Visibility ThumbnailPlaceholderVisibility => HasThumbnail ? Visibility.Collapsed : Visibility.Visible;

    public Visibility ThumbnailProgressVisibility =>
        SupportsThumbnail && IsThumbnailLoading ? Visibility.Visible : Visibility.Collapsed;

    public Visibility ThumbnailPlaceholderIconVisibility =>
        IsThumbnailLoading ? Visibility.Collapsed : Visibility.Visible;

    public Symbol ThumbnailPlaceholderSymbol => SupportsThumbnail ? Symbol.Video : Symbol.Audio;

    public string ThumbnailPlaceholderText => SupportsThumbnail
        ? (IsThumbnailLoading ? "\u6b63\u5728\u751f\u6210\u9884\u89c8\u56fe" : "\u6682\u65e0\u9884\u89c8\u56fe")
        : "\u97f3\u9891\u6587\u4ef6";

    public void UpdatePlannedOutputPath(string outputPath) =>
        PlannedOutputPath = outputPath;

    public void ResetStatus() =>
        SetStatus(MediaJobState.Pending, "\u7b49\u5f85\u5f00\u59cb");

    public void MarkRunning(string detail = "\u6b63\u5728\u5904\u7406") =>
        SetStatus(MediaJobState.Running, detail);

    public void UpdateRunningDetail(string detail)
    {
        if (State != MediaJobState.Running)
        {
            return;
        }

        StatusDetail = detail;
    }

    public void MarkSucceeded(string detail) =>
        SetStatus(MediaJobState.Succeeded, detail);

    public void MarkFailed(string detail) =>
        SetStatus(MediaJobState.Failed, detail);

    public void MarkCancelled() =>
        SetStatus(MediaJobState.Cancelled, "\u4efb\u52a1\u5df2\u53d6\u6d88");

    public void MarkThumbnailLoading()
    {
        if (!SupportsThumbnail)
        {
            return;
        }

        ThumbnailSource = null;
        IsThumbnailLoading = true;
    }

    public void SetThumbnail(Uri thumbnailUri)
    {
        ArgumentNullException.ThrowIfNull(thumbnailUri);

        var bitmapImage = new BitmapImage
        {
            DecodePixelWidth = 320,
            UriSource = thumbnailUri
        };

        ThumbnailSource = bitmapImage;
        IsThumbnailLoading = false;
    }

    public void MarkThumbnailUnavailable()
    {
        ThumbnailSource = null;
        IsThumbnailLoading = false;
    }

    private void SetStatus(MediaJobState state, string statusDetail)
    {
        State = state;
        StatusDetail = statusDetail;
    }
}
