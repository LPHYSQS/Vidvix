using System;
using System.Collections.Generic;
using System.Linq;

namespace Vidvix.Core.Models;

/// <summary>
/// 集中定义单个工作区的展示文案和输入约束，避免这些规则散落在 ViewModel 各处。
/// </summary>
public sealed class ProcessingWorkspaceProfile
{
    public ProcessingWorkspaceProfile(
        ProcessingWorkspaceKind kind,
        string mediaLabel,
        string mediaFileLabel,
        IReadOnlyList<string> supportedInputFileTypes,
        string? fixedProcessingModeDisplayName = null,
        string? fixedProcessingModeDescription = null,
        string? headerTitle = null,
        string? headerDescription = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(mediaLabel);
        ArgumentException.ThrowIfNullOrWhiteSpace(mediaFileLabel);
        ArgumentNullException.ThrowIfNull(supportedInputFileTypes);

        Kind = kind;
        MediaLabel = mediaLabel;
        MediaFileLabel = mediaFileLabel;
        SupportedInputFileTypes = supportedInputFileTypes;
        FixedProcessingModeDisplayName = fixedProcessingModeDisplayName ?? string.Empty;
        FixedProcessingModeDescription = fixedProcessingModeDescription ?? string.Empty;
        HeaderTitle = string.IsNullOrWhiteSpace(headerTitle) ? $"{mediaLabel}处理" : headerTitle;
        HeaderDescription = string.IsNullOrWhiteSpace(headerDescription) ? $"管理{mediaFileLabel}导入与处理任务。" : headerDescription;
    }

    public ProcessingWorkspaceKind Kind { get; }

    public string MediaLabel { get; }

    public string MediaFileLabel { get; }

    public IReadOnlyList<string> SupportedInputFileTypes { get; }

    public string FixedProcessingModeDisplayName { get; }

    public string FixedProcessingModeDescription { get; }

    public string HeaderTitle { get; }

    public string HeaderDescription { get; }

    public string QueueDragDropHintText => $"支持拖拽{MediaFileLabel}或文件夹到窗口任意位置";

    public string DragDropCaptionText => $"导入{MediaFileLabel}或文件夹";

    public string ReadyForImportMessage => $"请导入{MediaFileLabel}或文件夹。";

    public string EmptyQueueProcessingMessage => $"请先导入至少一个{MediaFileLabel}。";

    public string ImportFilePickerCommitText => $"导入{MediaFileLabel}";

    public string ImportFolderPickerCommitText => $"导入{MediaLabel}文件夹";

    public string NoProcessableImportMessage => $"没有发现可处理的{MediaFileLabel}。";

    public string SupportedInputFormatsHint =>
        "支持导入格式（" +
        string.Join("、", SupportedInputFileTypes.Select(extension => extension.TrimStart('.').ToUpperInvariant())) +
        "）";

    public string CreateImportedCountMessage(int addedCount) => $"已导入 {addedCount} 个{MediaFileLabel}。";
}
