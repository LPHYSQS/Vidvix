using System.IO;
using Vidvix.Core.Models;
using Vidvix.Utils;

namespace Vidvix.ViewModels;

public sealed class MediaJobViewModel : ObservableObject
{
    private MediaJobState _state = MediaJobState.Pending;
    private string _plannedOutputPath = string.Empty;
    private string _statusDetail = "等待开始";

    public MediaJobViewModel(string inputPath)
    {
        InputPath = Path.GetFullPath(inputPath);
        InputFileName = Path.GetFileName(InputPath);
        InputDirectory = Path.GetDirectoryName(InputPath) ?? string.Empty;
    }

    public string InputPath { get; }

    public string InputFileName { get; }

    public string InputDirectory { get; }

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
        MediaJobState.Running => "处理中",
        MediaJobState.Succeeded => "已完成",
        MediaJobState.Failed => "失败",
        MediaJobState.Cancelled => "已取消",
        _ => "待处理"
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

    public string StatusSummary => $"{StatusText} · {StatusDetail}";

    public void UpdatePlannedOutputPath(string outputPath) =>
        PlannedOutputPath = outputPath;

    public void ResetStatus() =>
        SetStatus(MediaJobState.Pending, "等待开始");

    public void MarkRunning() =>
        SetStatus(MediaJobState.Running, "正在处理");

    public void MarkSucceeded(string detail) =>
        SetStatus(MediaJobState.Succeeded, detail);

    public void MarkFailed(string detail) =>
        SetStatus(MediaJobState.Failed, detail);

    public void MarkCancelled() =>
        SetStatus(MediaJobState.Cancelled, "任务已取消");

    private void SetStatus(MediaJobState state, string statusDetail)
    {
        State = state;
        StatusDetail = statusDetail;
    }
}
