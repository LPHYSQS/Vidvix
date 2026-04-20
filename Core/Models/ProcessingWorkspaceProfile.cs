using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using Vidvix.Core.Interfaces;

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
        string? headerDescription = null,
        string? localizationKeyPrefix = null,
        string? queueDragDropHintText = null,
        string? dragDropCaptionText = null,
        string? readyForImportMessage = null,
        string? emptyQueueProcessingMessage = null,
        string? importFilePickerCommitText = null,
        string? importFolderPickerCommitText = null,
        string? noProcessableImportMessage = null,
        string? supportedInputFormatsHint = null,
        string? importedCountMessageTemplate = null)
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
        LocalizationKeyPrefix = localizationKeyPrefix ?? string.Empty;
        QueueDragDropHintText = string.IsNullOrWhiteSpace(queueDragDropHintText)
            ? $"支持拖拽{MediaFileLabel}或文件夹到窗口任意位置"
            : queueDragDropHintText;
        DragDropCaptionText = string.IsNullOrWhiteSpace(dragDropCaptionText)
            ? $"导入{MediaFileLabel}或文件夹"
            : dragDropCaptionText;
        ReadyForImportMessage = string.IsNullOrWhiteSpace(readyForImportMessage)
            ? $"请导入{MediaFileLabel}或文件夹。"
            : readyForImportMessage;
        EmptyQueueProcessingMessage = string.IsNullOrWhiteSpace(emptyQueueProcessingMessage)
            ? $"请先导入至少一个{MediaFileLabel}。"
            : emptyQueueProcessingMessage;
        ImportFilePickerCommitText = string.IsNullOrWhiteSpace(importFilePickerCommitText)
            ? $"导入{MediaFileLabel}"
            : importFilePickerCommitText;
        ImportFolderPickerCommitText = string.IsNullOrWhiteSpace(importFolderPickerCommitText)
            ? $"导入{MediaLabel}文件夹"
            : importFolderPickerCommitText;
        NoProcessableImportMessage = string.IsNullOrWhiteSpace(noProcessableImportMessage)
            ? $"没有发现可处理的{MediaFileLabel}。"
            : noProcessableImportMessage;
        SupportedInputFormatsHint = string.IsNullOrWhiteSpace(supportedInputFormatsHint)
            ? BuildSupportedInputFormatsHint(supportedInputFileTypes, "、")
            : supportedInputFormatsHint;
        ImportedCountMessageTemplate = string.IsNullOrWhiteSpace(importedCountMessageTemplate)
            ? $"已导入 {{count}} 个{MediaFileLabel}。"
            : importedCountMessageTemplate;
    }

    public ProcessingWorkspaceKind Kind { get; }

    public string MediaLabel { get; }

    public string MediaFileLabel { get; }

    public IReadOnlyList<string> SupportedInputFileTypes { get; }

    public string FixedProcessingModeDisplayName { get; }

    public string FixedProcessingModeDescription { get; }

    public string HeaderTitle { get; }

    public string HeaderDescription { get; }

    public string LocalizationKeyPrefix { get; }

    public string QueueDragDropHintText { get; }

    public string DragDropCaptionText { get; }

    public string ReadyForImportMessage { get; }

    public string EmptyQueueProcessingMessage { get; }

    public string ImportFilePickerCommitText { get; }

    public string ImportFolderPickerCommitText { get; }

    public string NoProcessableImportMessage { get; }

    public string SupportedInputFormatsHint { get; }

    public string ImportedCountMessageTemplate { get; }

    public string CreateImportedCountMessage(int addedCount) =>
        ImportedCountMessageTemplate.Replace(
            "{count}",
            addedCount.ToString(CultureInfo.CurrentCulture),
            StringComparison.Ordinal);

    public ProcessingWorkspaceProfile Localize(ILocalizationService localizationService)
    {
        ArgumentNullException.ThrowIfNull(localizationService);

        if (string.IsNullOrWhiteSpace(LocalizationKeyPrefix))
        {
            return this;
        }

        var localizedMediaLabel = localizationService.GetString($"{LocalizationKeyPrefix}.mediaLabel", MediaLabel);
        var localizedMediaFileLabel = localizationService.GetString($"{LocalizationKeyPrefix}.mediaFileLabel", MediaFileLabel);
        var workspaceArguments = BuildWorkspaceArguments(localizedMediaLabel, localizedMediaFileLabel);
        var formatSeparator = ResolveSupportedInputFormatSeparator(localizationService.CurrentLanguage);
        var supportedFormatsValue = BuildSupportedInputFormatsHintValue(SupportedInputFileTypes, formatSeparator);

        return new ProcessingWorkspaceProfile(
            Kind,
            localizedMediaLabel,
            localizedMediaFileLabel,
            SupportedInputFileTypes,
            fixedProcessingModeDisplayName: LocalizeOptionalText(
                localizationService,
                $"{LocalizationKeyPrefix}.fixedProcessingModeDisplayName",
                FixedProcessingModeDisplayName),
            fixedProcessingModeDescription: LocalizeOptionalText(
                localizationService,
                $"{LocalizationKeyPrefix}.fixedProcessingModeDescription",
                FixedProcessingModeDescription),
            headerTitle: localizationService.GetString($"{LocalizationKeyPrefix}.headerTitle", HeaderTitle),
            headerDescription: localizationService.GetString($"{LocalizationKeyPrefix}.headerDescription", HeaderDescription),
            localizationKeyPrefix: LocalizationKeyPrefix,
            queueDragDropHintText: localizationService.Format(
                $"{LocalizationKeyPrefix}.queueDragDropHintText",
                workspaceArguments,
                QueueDragDropHintText),
            dragDropCaptionText: localizationService.Format(
                $"{LocalizationKeyPrefix}.dragDropCaptionText",
                workspaceArguments,
                DragDropCaptionText),
            readyForImportMessage: localizationService.Format(
                $"{LocalizationKeyPrefix}.readyForImportMessage",
                workspaceArguments,
                ReadyForImportMessage),
            emptyQueueProcessingMessage: localizationService.Format(
                $"{LocalizationKeyPrefix}.emptyQueueProcessingMessage",
                workspaceArguments,
                EmptyQueueProcessingMessage),
            importFilePickerCommitText: localizationService.Format(
                $"{LocalizationKeyPrefix}.importFilePickerCommitText",
                workspaceArguments,
                ImportFilePickerCommitText),
            importFolderPickerCommitText: localizationService.Format(
                $"{LocalizationKeyPrefix}.importFolderPickerCommitText",
                workspaceArguments,
                ImportFolderPickerCommitText),
            noProcessableImportMessage: localizationService.Format(
                $"{LocalizationKeyPrefix}.noProcessableImportMessage",
                workspaceArguments,
                NoProcessableImportMessage),
            supportedInputFormatsHint: localizationService.Format(
                $"{LocalizationKeyPrefix}.supportedInputFormatsHint",
                new Dictionary<string, object?>
                {
                    ["formats"] = supportedFormatsValue
                },
                SupportedInputFormatsHint),
            importedCountMessageTemplate: localizationService.GetString(
                $"{LocalizationKeyPrefix}.importedCountMessage",
                ImportedCountMessageTemplate));
    }

    private static string LocalizeOptionalText(
        ILocalizationService localizationService,
        string key,
        string fallback) =>
        string.IsNullOrWhiteSpace(fallback)
            ? string.Empty
            : localizationService.GetString(key, fallback);

    private static IReadOnlyDictionary<string, object?> BuildWorkspaceArguments(
        string mediaLabel,
        string mediaFileLabel) =>
        new Dictionary<string, object?>
        {
            ["mediaLabel"] = mediaLabel,
            ["mediaFileLabel"] = mediaFileLabel
        };

    private static string BuildSupportedInputFormatsHint(
        IReadOnlyList<string> supportedInputFileTypes,
        string separator) =>
        $"支持导入格式（{BuildSupportedInputFormatsHintValue(supportedInputFileTypes, separator)}）";

    private static string BuildSupportedInputFormatsHintValue(
        IReadOnlyList<string> supportedInputFileTypes,
        string separator) =>
        string.Join(
            separator,
            supportedInputFileTypes.Select(extension => extension.TrimStart('.').ToUpperInvariant()));

    private static string ResolveSupportedInputFormatSeparator(string languageCode) =>
        languageCode.StartsWith("zh", StringComparison.OrdinalIgnoreCase)
            ? "、"
            : ", ";
}
