using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Windows.Input;
using Microsoft.UI.Xaml;
using Vidvix.Core.Interfaces;
using Vidvix.Core.Models;
using Vidvix.Utils;

namespace Vidvix.ViewModels;

public sealed partial class AiWorkspaceViewModel : ObservableObject
{
    private static readonly HashSet<string> LaunchOutputFormatExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".mp4",
        ".mkv"
    };

    private readonly ApplicationConfiguration _configuration;
    private readonly ILocalizationService? _localizationService;
    private readonly IFilePickerService? _filePickerService;
    private readonly IMediaImportDiscoveryService? _mediaImportDiscoveryService;
    private readonly ILogger? _logger;
    private readonly AsyncRelayCommand _importFilesCommand;
    private readonly AsyncRelayCommand _selectOutputDirectoryCommand;
    private readonly RelayCommand _clearOutputDirectoryCommand;
    private readonly RelayCommand _removeMaterialCommand;
    private readonly AsyncRelayCommand _startProcessingCommand;
    private readonly RelayCommand _cancelProcessingCommand;
    private bool _isProcessing;
    private bool _suppressSelectionStatus;
    private string _statusText;

    public AiWorkspaceViewModel()
        : this(
            new ApplicationConfiguration(),
            localizationService: null,
            filePickerService: null,
            mediaImportDiscoveryService: null,
            aiRuntimeCatalogService: null,
            logger: null)
    {
    }

    public AiWorkspaceViewModel(
        ApplicationConfiguration configuration,
        ILocalizationService? localizationService,
        IFilePickerService? filePickerService,
        IMediaImportDiscoveryService? mediaImportDiscoveryService,
        IAiRuntimeCatalogService? aiRuntimeCatalogService,
        ILogger? logger)
    {
        _configuration = configuration ?? throw new ArgumentNullException(nameof(configuration));
        _localizationService = localizationService;
        _filePickerService = filePickerService;
        _mediaImportDiscoveryService = mediaImportDiscoveryService;
        _aiRuntimeCatalogService = aiRuntimeCatalogService;
        _logger = logger;

        MaterialLibrary = new AiMaterialLibraryState();
        InputState = new AiInputState();
        ModeState = new AiModeState();
        OutputSettings = new AiOutputSettingsState(BuildLaunchOutputFormats());
        _statusText = GetReadyStatusMessage();

        _importFilesCommand = new AsyncRelayCommand(ImportFilesAsync, CanImportFilesInternal);
        _selectOutputDirectoryCommand = new AsyncRelayCommand(SelectOutputDirectoryAsync, CanSelectOutputDirectoryInternal);
        _clearOutputDirectoryCommand = new RelayCommand(ClearOutputDirectory, CanClearOutputDirectoryInternal);
        _removeMaterialCommand = new RelayCommand(RemoveMaterial, CanRemoveMaterial);
        _startProcessingCommand = new AsyncRelayCommand(StartProcessingAsync, CanStartProcessingInternal);
        _cancelProcessingCommand = new RelayCommand(CancelProcessing, () => IsProcessing);

        MaterialLibrary.PropertyChanged += OnMaterialLibraryPropertyChanged;
        ModeState.PropertyChanged += OnModeStatePropertyChanged;
        OutputSettings.PropertyChanged += OnOutputSettingsPropertyChanged;

        RefreshOutputContext();
    }

    public AiMaterialLibraryState MaterialLibrary { get; }

    public AiInputState InputState { get; }

    public AiModeState ModeState { get; }

    public AiOutputSettingsState OutputSettings { get; }

    public ICommand ImportFilesCommand => _importFilesCommand;

    public ICommand SelectOutputDirectoryCommand => _selectOutputDirectoryCommand;

    public ICommand ClearOutputDirectoryCommand => _clearOutputDirectoryCommand;

    public ICommand RemoveMaterialCommand => _removeMaterialCommand;

    public ICommand StartProcessingCommand => _startProcessingCommand;

    public ICommand CancelProcessingCommand => _cancelProcessingCommand;

    public bool IsProcessing
    {
        get => _isProcessing;
        private set
        {
            if (!SetProperty(ref _isProcessing, value))
            {
                return;
            }

            OnPropertyChanged(nameof(CanStartProcessing));
            NotifyCommandStates();
        }
    }

    public bool CanStartProcessing => InputState.HasCurrentMaterial && !IsProcessing;

    public Visibility MaterialsEmptyVisibility =>
        MaterialLibrary.HasNoMaterials
            ? Visibility.Visible
            : Visibility.Collapsed;

    public Visibility CurrentTrackEmptyVisibility =>
        InputState.HasNoCurrentMaterial
            ? Visibility.Visible
            : Visibility.Collapsed;

    public Visibility CurrentTrackCardVisibility =>
        InputState.HasCurrentMaterial
            ? Visibility.Visible
            : Visibility.Collapsed;

    public string StatusText
    {
        get => _statusText;
        private set => SetProperty(ref _statusText, value);
    }

    public string SectionCaptionText =>
        GetLocalizedText("ai.page.caption", "AI 工作区");

    public string PageTitleText =>
        GetLocalizedText("ai.page.title", "AI 输入与输出状态");

    public string PageDescriptionText =>
        GetLocalizedText(
            "ai.page.description",
            "本轮已接入素材列表、单视频激活、模式切换壳层与基础输出设置状态；仍不启动实际 AI 推理。");

    public string MaterialsSectionTitleText =>
        GetLocalizedText("ai.page.materials.title", "素材列表");

    public string MaterialsSectionDescriptionText =>
        FormatLocalizedText(
            "ai.page.materials.description",
            $"仅接收视频素材，支持格式：{BuildSupportedInputFormatsSummary()}。允许导入多个视频，但单次只处理一个当前视频。",
            ("formats", BuildSupportedInputFormatsSummary()));

    public string MaterialsImportButtonText =>
        GetLocalizedText("ai.page.materials.importFiles", "导入视频");

    public string MaterialCountText =>
        FormatLocalizedText(
            "ai.page.materials.count",
            $"共 {MaterialLibrary.MaterialCount} 个素材",
            ("count", MaterialLibrary.MaterialCount));

    public string VideoOnlyImportBadgeText =>
        GetLocalizedText("ai.page.materials.badge.videoOnly", "视频-only 导入");

    public string SingleVideoExecutionBadgeText =>
        GetLocalizedText("ai.page.materials.badge.singleSelection", "单次单视频执行");

    public string MaterialsPlaceholderText =>
        GetLocalizedText(
            "ai.page.materials.placeholder",
            "先导入一个或多个视频，再从素材列表中锁定当前处理对象。");

    public string RemoveMaterialButtonText =>
        GetLocalizedText("ai.page.materials.remove", "移除");

    public string WorkspaceSectionTitleText =>
        GetLocalizedText("ai.page.workspace.title", "AI 工作区");

    public string WorkspaceSectionDescriptionText =>
        GetLocalizedText(
            "ai.page.workspace.description",
            "模式切换只影响 AI 参数壳层与后续执行规划，不会污染已导入素材和当前处理对象。");

    public string WorkspaceModeSwitchHintText =>
        GetLocalizedText(
            "ai.page.workspace.modeHint",
            "AI补帧 与 AI增强 共用同一份素材库与当前视频选择，后续只在各自模式下接入独立 workflow。");

    public string InterpolationModeLabelText =>
        GetLocalizedText("ai.interpolation.modeLabel", "AI补帧");

    public string EnhancementModeLabelText =>
        GetLocalizedText("ai.enhancement.modeLabel", "AI增强");

    public string CurrentModeDescriptionText =>
        ModeState.SelectedMode == AiWorkspaceMode.Interpolation
            ? GetLocalizedText(
                "ai.interpolation.modeDescription",
                "首发路线冻结为 RIFE，后续将接入倍率、设备策略、取消控制与进度反馈。")
            : GetLocalizedText(
                "ai.enhancement.modeDescription",
                "首发路线冻结为 Real-ESRGAN，倍率范围固定为 2x 到 16x，后续按增量方式接入模型与执行链路。");

    public string CurrentTrackTitleText =>
        GetLocalizedText("ai.page.workspace.inputTrackTitle", "当前输入轨道");

    public string CurrentInputStatusText =>
        InputState.HasCurrentMaterial
            ? FormatLocalizedText(
                "ai.page.workspace.currentSelection",
                $"当前处理对象：{InputState.CurrentInputFileName}",
                ("fileName", InputState.CurrentInputFileName))
            : GetLocalizedText(
                "ai.page.workspace.currentSelection.empty",
                "尚未选择当前处理对象");

    public string CurrentTrackHintText =>
        ModeState.SelectedMode == AiWorkspaceMode.Interpolation
            ? GetLocalizedText(
                "ai.interpolation.trackHint",
                "当前为 AI补帧 模式，单次只会将一个视频作为补帧输入；后续倍率、设备与执行结果都挂在本模式下。")
            : GetLocalizedText(
                "ai.enhancement.trackHint",
                "当前为 AI增强 模式，单次只会将一个视频作为增强输入；后续模型档位、倍率链路与高倍率提醒都挂在本模式下。");

    public string CurrentTrackEmptyText =>
        ModeState.SelectedMode == AiWorkspaceMode.Interpolation
            ? GetLocalizedText(
                "ai.interpolation.trackEmpty",
                "从素材列表中选择一个视频后，这里会锁定为当前补帧输入。")
            : GetLocalizedText(
                "ai.enhancement.trackEmpty",
                "从素材列表中选择一个视频后，这里会锁定为当前增强输入。");

    public string SourceFileLabelText =>
        GetLocalizedText("ai.page.workspace.sourceFile", "源文件");

    public string CurrentOutputDirectoryLabelText =>
        GetLocalizedText("ai.page.workspace.outputDirectory", "当前输出目录");

    public string CurrentOutputPreviewLabelText =>
        GetLocalizedText("ai.page.workspace.outputPreview", "当前输出文件名");

    public string CurrentModeRuntimeNoteText => BuildCurrentModeRuntimeSummaryText();

    public string CurrentModeEngineBadgeText =>
        ModeState.SelectedMode == AiWorkspaceMode.Interpolation
            ? GetLocalizedText("ai.interpolation.engineBadge", "RIFE")
            : GetLocalizedText("ai.enhancement.engineBadge", "Real-ESRGAN");

    public string OutputSectionTitleText =>
        GetLocalizedText("ai.page.output.title", "输出设置");

    public string OutputSectionDescriptionText =>
        GetLocalizedText(
            "ai.page.output.description",
            "输出设置已具备首发基础状态：格式仅保留 MP4 / MKV，目录默认跟随当前素材，文件名留空时自动按模式生成。");

    public string OutputFormatTitleText =>
        GetLocalizedText("ai.page.output.format.title", "输出格式");

    public string OutputFormatDescriptionText => OutputSettings.SelectedOutputFormatDescription;

    public string OutputFormatHintText =>
        GetLocalizedText(
            "ai.page.output.format.hint",
            "首发封装范围先冻结为 MP4 与 MKV，后续 runtime 接入时直接复用这里的输出格式状态。");

    public string OutputDirectoryTitleText =>
        GetLocalizedText("ai.page.output.directory.title", "输出目录");

    public string SelectOutputDirectoryButtonText =>
        GetLocalizedText("ai.page.output.directory.select", "选择目录");

    public string ClearOutputDirectoryButtonText =>
        GetLocalizedText("ai.page.output.directory.clear", "清空");

    public string OutputDirectoryHintText =>
        OutputSettings.HasCustomOutputDirectory
            ? FormatLocalizedText(
                "ai.page.output.directory.hint.custom",
                $"当前已固定输出到：{OutputSettings.EffectiveOutputDirectory}",
                ("path", OutputSettings.EffectiveOutputDirectory))
            : InputState.HasCurrentMaterial
                ? GetLocalizedText(
                    "ai.page.output.directory.hint.default",
                    "未手动指定目录时，默认跟随当前素材所在目录输出。")
                : GetLocalizedText(
                    "ai.page.output.directory.hint.empty",
                    "选择当前处理对象后，会自动带入默认输出目录。");

    public string OutputDirectoryPlaceholderText =>
        string.IsNullOrWhiteSpace(OutputSettings.OutputDirectoryDisplayText)
            ? GetLocalizedText(
                "ai.page.output.directory.placeholder.noInput",
                "选择当前处理对象后自动带入默认目录")
            : string.Empty;

    public string OutputFileNameTitleText =>
        GetLocalizedText("ai.page.output.fileName.title", "输出文件名");

    public string OutputFileNameHintText =>
        GetLocalizedText(
            "ai.page.output.fileName.hint",
            "留空时会按当前视频与所选模式自动生成建议文件名；后续运行时直接消费这里的文件名状态。");

    public string OutputFileNamePlaceholderText =>
        FormatLocalizedText(
            "ai.page.output.fileName.placeholder",
            $"例如：{OutputSettings.SuggestedOutputFileName}",
            ("name", OutputSettings.SuggestedOutputFileName));

    public string OutputFormatsBadgeText =>
        FormatLocalizedText(
            "ai.page.output.badge.formats",
            $"输出：{BuildLaunchOutputFormatsSummary()}",
            ("formats", BuildLaunchOutputFormatsSummary()));

    public string OutputAudioBadgeText =>
        GetLocalizedText("ai.page.output.badge.audio", "默认保留原音轨");

    public string OutputParameterSummaryText =>
        FormatLocalizedText(
            "ai.page.output.summary",
            $"当前参数状态：模式 {GetCurrentModeDisplayName()}，输入 {GetCurrentInputSummaryText()}，输出 {OutputSettings.SelectedOutputFormat.DisplayName}，文件名 {OutputSettings.EffectiveOutputFileName}，原音轨默认跟随源文件。",
            ("mode", GetCurrentModeDisplayName()),
            ("input", GetCurrentInputSummaryText()),
            ("format", OutputSettings.SelectedOutputFormat.DisplayName),
            ("fileName", OutputSettings.EffectiveOutputFileName));

    public void RefreshLocalization()
    {
        OutputSettings.ReloadAvailableOutputFormats(BuildLaunchOutputFormats());

        OnPropertyChanged(nameof(SectionCaptionText));
        OnPropertyChanged(nameof(PageTitleText));
        OnPropertyChanged(nameof(PageDescriptionText));
        OnPropertyChanged(nameof(MaterialsSectionTitleText));
        OnPropertyChanged(nameof(MaterialsSectionDescriptionText));
        OnPropertyChanged(nameof(MaterialsImportButtonText));
        OnPropertyChanged(nameof(MaterialCountText));
        OnPropertyChanged(nameof(VideoOnlyImportBadgeText));
        OnPropertyChanged(nameof(SingleVideoExecutionBadgeText));
        OnPropertyChanged(nameof(MaterialsPlaceholderText));
        OnPropertyChanged(nameof(RemoveMaterialButtonText));
        OnPropertyChanged(nameof(WorkspaceSectionTitleText));
        OnPropertyChanged(nameof(WorkspaceSectionDescriptionText));
        OnPropertyChanged(nameof(WorkspaceModeSwitchHintText));
        OnPropertyChanged(nameof(InterpolationModeLabelText));
        OnPropertyChanged(nameof(EnhancementModeLabelText));
        OnPropertyChanged(nameof(CurrentModeDescriptionText));
        OnPropertyChanged(nameof(CurrentTrackTitleText));
        OnPropertyChanged(nameof(CurrentInputStatusText));
        OnPropertyChanged(nameof(CurrentTrackHintText));
        OnPropertyChanged(nameof(CurrentTrackEmptyText));
        OnPropertyChanged(nameof(SourceFileLabelText));
        OnPropertyChanged(nameof(CurrentOutputDirectoryLabelText));
        OnPropertyChanged(nameof(CurrentOutputPreviewLabelText));
        OnPropertyChanged(nameof(CurrentModeRuntimeNoteText));
        OnPropertyChanged(nameof(CurrentModeEngineBadgeText));
        OnPropertyChanged(nameof(OutputSectionTitleText));
        OnPropertyChanged(nameof(OutputSectionDescriptionText));
        OnPropertyChanged(nameof(OutputFormatTitleText));
        OnPropertyChanged(nameof(OutputFormatDescriptionText));
        OnPropertyChanged(nameof(OutputFormatHintText));
        OnPropertyChanged(nameof(OutputDirectoryTitleText));
        OnPropertyChanged(nameof(SelectOutputDirectoryButtonText));
        OnPropertyChanged(nameof(ClearOutputDirectoryButtonText));
        OnPropertyChanged(nameof(OutputDirectoryHintText));
        OnPropertyChanged(nameof(OutputDirectoryPlaceholderText));
        OnPropertyChanged(nameof(OutputFileNameTitleText));
        OnPropertyChanged(nameof(OutputFileNameHintText));
        OnPropertyChanged(nameof(OutputFileNamePlaceholderText));
        OnPropertyChanged(nameof(OutputFormatsBadgeText));
        OnPropertyChanged(nameof(OutputAudioBadgeText));
        OnPropertyChanged(nameof(OutputParameterSummaryText));
        RefreshRuntimeLocalization();

        StatusText = InputState.HasCurrentMaterial
            ? CreateSelectedMaterialStatusMessage(InputState.CurrentInputFileName)
            : GetReadyStatusMessage();
    }

    private async Task ImportFilesAsync()
    {
        if (_filePickerService is null || _mediaImportDiscoveryService is null)
        {
            StatusText = GetImportUnavailableStatusMessage();
            return;
        }

        try
        {
            var selectedFiles = await _filePickerService.PickFilesAsync(
                new FilePickerRequest(
                    _configuration.SupportedAiInputFileTypes,
                    GetLocalizedText("ai.dialog.importFiles.commit", "导入视频")));

            if (selectedFiles.Count == 0)
            {
                StatusText = GetImportCancelledStatusMessage();
                return;
            }

            ImportPaths(selectedFiles);
        }
        catch (OperationCanceledException)
        {
            StatusText = GetImportCancelledStatusMessage();
        }
        catch (Exception exception)
        {
            StatusText = GetImportFailedStatusMessage();
            _logger?.Log(LogLevel.Error, "导入 AI 视频素材时发生异常。", exception);
        }
    }

    private void ImportPaths(IEnumerable<string> inputPaths)
    {
        if (_mediaImportDiscoveryService is null)
        {
            StatusText = GetImportUnavailableStatusMessage();
            return;
        }

        ArgumentNullException.ThrowIfNull(inputPaths);

        var normalizedPaths = new List<string>();
        var invalidEntryCount = 0;

        foreach (var inputPath in inputPaths)
        {
            if (string.IsNullOrWhiteSpace(inputPath))
            {
                continue;
            }

            try
            {
                normalizedPaths.Add(Path.GetFullPath(inputPath));
            }
            catch (Exception exception) when (
                exception is ArgumentException or
                NotSupportedException or
                PathTooLongException)
            {
                invalidEntryCount++;
            }
        }

        if (normalizedPaths.Count == 0)
        {
            StatusText = invalidEntryCount > 0
                ? CreateRejectedOnlyStatusMessage(invalidEntryCount)
                : GetNoProcessableImportMessage();
            return;
        }

        var discovery = _mediaImportDiscoveryService.Discover(
            normalizedPaths,
            _configuration.SupportedAiInputFileTypes);
        var rejectedCount =
            invalidEntryCount +
            discovery.UnsupportedEntries +
            discovery.MissingEntries +
            discovery.UnavailableDirectories;

        _suppressSelectionStatus = true;
        try
        {
            var importResult = MaterialLibrary.AddMaterials(discovery.SupportedFiles);
            SyncInputSelection(updateStatus: false);
            StatusText = CreateImportStatusMessage(importResult, rejectedCount);
        }
        finally
        {
            _suppressSelectionStatus = false;
        }

        NotifyCommandStates();
    }

    private async Task SelectOutputDirectoryAsync()
    {
        if (_filePickerService is null)
        {
            return;
        }

        try
        {
            var selectedFolder = await _filePickerService.PickFolderAsync(
                GetLocalizedText("ai.dialog.outputDirectory.commit", "选择输出目录"));

            if (string.IsNullOrWhiteSpace(selectedFolder))
            {
                StatusText = GetOutputDirectorySelectionCancelledStatusMessage();
                return;
            }

            if (OutputSettings.TrySetCustomOutputDirectory(selectedFolder))
            {
                StatusText = CreateOutputDirectorySelectedStatusMessage(OutputSettings.CustomOutputDirectory);
            }
        }
        catch (OperationCanceledException)
        {
            StatusText = GetOutputDirectorySelectionCancelledStatusMessage();
        }
        catch (Exception exception)
        {
            StatusText = GetOutputDirectorySelectionFailedStatusMessage();
            _logger?.Log(LogLevel.Error, "选择 AI 输出目录时发生异常。", exception);
        }
    }

    private void ClearOutputDirectory()
    {
        if (!OutputSettings.ClearCustomOutputDirectory())
        {
            return;
        }

        StatusText = GetOutputDirectoryClearedStatusMessage();
    }

    private void RemoveMaterial(object? parameter)
    {
        if (parameter is not AiMaterialItemViewModel material || !MaterialLibrary.RemoveMaterial(material))
        {
            return;
        }

        _suppressSelectionStatus = true;
        try
        {
            SyncInputSelection(updateStatus: false);
        }
        finally
        {
            _suppressSelectionStatus = false;
        }

        StatusText = MaterialLibrary.HasMaterials
            ? CreateRemovedMaterialStatusMessage(material.InputFileName)
            : GetLibraryClearedStatusMessage();

        NotifyCommandStates();
    }

    private Task StartProcessingAsync()
    {
        StatusText = GetStartDeferredStatusMessage();
        return Task.CompletedTask;
    }

    private void CancelProcessing()
    {
        if (!IsProcessing)
        {
            return;
        }

        IsProcessing = false;
    }

    private void OnMaterialLibraryPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(AiMaterialLibraryState.SelectedMaterial))
        {
            SyncInputSelection(updateStatus: !_suppressSelectionStatus);
        }

        if (e.PropertyName == nameof(AiMaterialLibraryState.MaterialCount) ||
            e.PropertyName == nameof(AiMaterialLibraryState.HasMaterials))
        {
            OnPropertyChanged(nameof(MaterialCountText));
            OnPropertyChanged(nameof(MaterialsEmptyVisibility));
            NotifyCommandStates();
        }
    }

    private void OnModeStatePropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName != nameof(AiModeState.SelectedMode))
        {
            return;
        }

        RefreshOutputContext();
        OnPropertyChanged(nameof(CurrentModeDescriptionText));
        OnPropertyChanged(nameof(CurrentTrackHintText));
        OnPropertyChanged(nameof(CurrentTrackEmptyText));
        OnPropertyChanged(nameof(CurrentModeRuntimeNoteText));
        OnPropertyChanged(nameof(CurrentModeEngineBadgeText));
        OnPropertyChanged(nameof(OutputFileNamePlaceholderText));
        OnPropertyChanged(nameof(OutputParameterSummaryText));
        RefreshRuntimeLocalization();
        StatusText = CreateModeChangedStatusMessage(GetCurrentModeDisplayName());
    }

    private void OnOutputSettingsPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        OnPropertyChanged(nameof(OutputFormatDescriptionText));
        OnPropertyChanged(nameof(OutputDirectoryHintText));
        OnPropertyChanged(nameof(OutputDirectoryPlaceholderText));
        OnPropertyChanged(nameof(OutputFileNamePlaceholderText));
        OnPropertyChanged(nameof(OutputFormatsBadgeText));
        OnPropertyChanged(nameof(OutputParameterSummaryText));
        _clearOutputDirectoryCommand.NotifyCanExecuteChanged();
    }

    private void SyncInputSelection(bool updateStatus)
    {
        InputState.SetCurrentMaterial(MaterialLibrary.SelectedMaterial);
        RefreshOutputContext();
        RefreshInputDependentProperties();

        if (updateStatus)
        {
            StatusText = InputState.HasCurrentMaterial
                ? CreateSelectedMaterialStatusMessage(InputState.CurrentInputFileName)
                : GetReadyStatusMessage();
        }
    }

    private void RefreshInputDependentProperties()
    {
        OnPropertyChanged(nameof(CanStartProcessing));
        OnPropertyChanged(nameof(CurrentInputStatusText));
        OnPropertyChanged(nameof(CurrentTrackEmptyVisibility));
        OnPropertyChanged(nameof(CurrentTrackCardVisibility));
        OnPropertyChanged(nameof(OutputDirectoryHintText));
        OnPropertyChanged(nameof(OutputDirectoryPlaceholderText));
        OnPropertyChanged(nameof(OutputFileNamePlaceholderText));
        OnPropertyChanged(nameof(OutputParameterSummaryText));
        _startProcessingCommand.NotifyCanExecuteChanged();
    }

    private void RefreshOutputContext()
    {
        OutputSettings.UpdateInputContext(
            InputState.CurrentInputDirectory,
            CreateSuggestedOutputFileName());
    }

    private string CreateSuggestedOutputFileName()
    {
        var baseName = InputState.HasCurrentMaterial
            ? InputState.CurrentInputFileNameWithoutExtension
            : "ai_output";
        if (string.IsNullOrWhiteSpace(baseName))
        {
            baseName = "ai_output";
        }

        return $"{baseName}{ModeState.OutputFileNameSuffix}";
    }

    private IReadOnlyList<OutputFormatOption> BuildLaunchOutputFormats()
    {
        var availableFormats = _configuration.SupportedVideoOutputFormats
            .Where(option => LaunchOutputFormatExtensions.Contains(option.Extension))
            .Select(LocalizeOutputFormatOption)
            .ToArray();

        if (availableFormats.Length > 0)
        {
            return availableFormats;
        }

        return new[]
        {
            new OutputFormatOption("MP4", ".mp4", "兼容性最好，适合常见播放器和移动设备。"),
            new OutputFormatOption("MKV", ".mkv", "封装更宽松，更适合保留原始编码和长视频素材。")
        };
    }

    private OutputFormatOption LocalizeOutputFormatOption(OutputFormatOption option) =>
        _localizationService is null
            ? option
            : option.Localize(_localizationService);

    private string BuildSupportedInputFormatsSummary()
    {
        var separator = _localizationService?.CurrentLanguage.StartsWith("zh", StringComparison.OrdinalIgnoreCase) == true
            ? "、"
            : ", ";

        return string.Join(
            separator,
            _configuration.SupportedAiInputFileTypes.Select(extension => extension.TrimStart('.').ToUpperInvariant()));
    }

    private string BuildLaunchOutputFormatsSummary() =>
        string.Join(" / ", OutputSettings.AvailableOutputFormats.Select(option => option.DisplayName));

    private string GetCurrentModeDisplayName() =>
        ModeState.SelectedMode == AiWorkspaceMode.Interpolation
            ? InterpolationModeLabelText
            : EnhancementModeLabelText;

    private string GetCurrentInputSummaryText() =>
        InputState.HasCurrentMaterial
            ? InputState.CurrentInputFileName
            : GetLocalizedText("ai.page.output.inputPending", "待选择视频");

    private string CreateImportStatusMessage(AiMaterialImportResult importResult, int rejectedCount)
    {
        if (importResult.AddedCount > 0 && rejectedCount == 0 && importResult.DuplicateCount == 0)
        {
            return FormatLocalizedText(
                "ai.status.imported",
                $"已导入 {importResult.AddedCount} 个视频素材，当前处理对象：{InputState.CurrentInputFileName}",
                ("count", importResult.AddedCount),
                ("fileName", InputState.CurrentInputFileName));
        }

        if (importResult.AddedCount > 0)
        {
            return FormatLocalizedText(
                "ai.status.importSummary",
                $"已导入 {importResult.AddedCount} 个视频，跳过 {importResult.DuplicateCount} 个重复项，拒绝 {rejectedCount} 个非视频或不可用条目。",
                ("addedCount", importResult.AddedCount),
                ("duplicateCount", importResult.DuplicateCount),
                ("rejectedCount", rejectedCount));
        }

        if (importResult.DuplicateCount > 0 && rejectedCount > 0)
        {
            return FormatLocalizedText(
                "ai.status.importDuplicateAndRejected",
                $"没有新增视频素材，跳过 {importResult.DuplicateCount} 个重复项，拒绝 {rejectedCount} 个非视频或不可用条目。",
                ("duplicateCount", importResult.DuplicateCount),
                ("rejectedCount", rejectedCount));
        }

        if (importResult.DuplicateCount > 0)
        {
            return GetImportDuplicateStatusMessage();
        }

        if (rejectedCount > 0)
        {
            return CreateRejectedOnlyStatusMessage(rejectedCount);
        }

        return GetNoProcessableImportMessage();
    }

    private string CreateRejectedOnlyStatusMessage(int rejectedCount) =>
        FormatLocalizedText(
            "ai.status.rejectedOnly",
            $"没有发现可导入的视频文件，已拒绝 {rejectedCount} 个非视频或不可用条目。",
            ("rejectedCount", rejectedCount));

    private string CreateSelectedMaterialStatusMessage(string fileName) =>
        FormatLocalizedText(
            "ai.status.selectedMaterial",
            $"当前处理对象已切换为：{fileName}",
            ("fileName", fileName));

    private string CreateRemovedMaterialStatusMessage(string fileName) =>
        FormatLocalizedText(
            "ai.status.removedMaterial",
            $"已从素材列表移除：{fileName}",
            ("fileName", fileName));

    private string CreateModeChangedStatusMessage(string modeName) =>
        FormatLocalizedText(
            "ai.status.modeChanged",
            $"已切换为 {modeName}，当前处理对象保持不变。",
            ("mode", modeName));

    private string CreateOutputDirectorySelectedStatusMessage(string path) =>
        FormatLocalizedText(
            "ai.status.outputDirectorySelected",
            $"已切换输出目录：{path}",
            ("path", path));

    private string GetReadyStatusMessage() =>
        GetLocalizedText(
            "ai.status.ready",
            "先导入一个或多个视频，再从素材列表中锁定当前处理对象。");

    private string GetImportUnavailableStatusMessage() =>
        GetLocalizedText(
            "ai.status.importUnavailable",
            "当前运行环境未接入文件选择器，暂时无法导入 AI 视频素材。");

    private string GetImportCancelledStatusMessage() =>
        GetLocalizedText(
            "ai.status.importCancelled",
            "已取消视频导入。");

    private string GetImportFailedStatusMessage() =>
        GetLocalizedText(
            "ai.status.importFailed",
            "导入视频素材失败，请重试。");

    private string GetImportDuplicateStatusMessage() =>
        GetLocalizedText(
            "ai.status.duplicateOnly",
            "所选视频已全部在素材列表中，未重复导入。");

    private string GetNoProcessableImportMessage() =>
        GetLocalizedText(
            "ai.status.noProcessable",
            "没有发现可导入的视频文件。");

    private string GetLibraryClearedStatusMessage() =>
        GetLocalizedText(
            "ai.status.libraryCleared",
            "素材列表已清空，当前没有可处理视频。");

    private string GetOutputDirectorySelectionCancelledStatusMessage() =>
        GetLocalizedText(
            "ai.status.outputDirectorySelectionCancelled",
            "已取消选择输出目录。");

    private string GetOutputDirectorySelectionFailedStatusMessage() =>
        GetLocalizedText(
            "ai.status.outputDirectorySelectionFailed",
            "选择输出目录失败，请重试。");

    private string GetOutputDirectoryClearedStatusMessage() =>
        GetLocalizedText(
            "ai.status.outputDirectoryCleared",
            "已恢复为跟随当前素材目录输出。");

    private string GetStartDeferredStatusMessage() =>
        GetLocalizedText(
            "ai.status.startDeferred",
            "R5 只完成输入与输出状态，不启动实际 AI 推理；下一轮开始继续接 runtime 与参数执行链路。");

    private string GetLocalizedText(string key, string fallback) =>
        _localizationService?.GetString(key, fallback) ?? fallback;

    private string FormatLocalizedText(string key, string fallback, params (string Name, object? Value)[] arguments)
    {
        if (_localizationService is null || arguments.Length == 0)
        {
            return fallback;
        }

        var localizedArguments = new Dictionary<string, object?>(arguments.Length, StringComparer.Ordinal);
        foreach (var argument in arguments)
        {
            localizedArguments[argument.Name] = argument.Value;
        }

        return _localizationService.Format(key, localizedArguments, fallback);
    }

    private bool CanImportFilesInternal() =>
        !IsProcessing &&
        _filePickerService is not null &&
        _mediaImportDiscoveryService is not null;

    private bool CanSelectOutputDirectoryInternal() =>
        !IsProcessing &&
        _filePickerService is not null;

    private bool CanClearOutputDirectoryInternal() =>
        !IsProcessing &&
        OutputSettings.HasCustomOutputDirectory;

    private bool CanRemoveMaterial(object? parameter) =>
        !IsProcessing &&
        parameter is AiMaterialItemViewModel material &&
        MaterialLibrary.Materials.Contains(material);

    private bool CanStartProcessingInternal() => CanStartProcessing;

    private void NotifyCommandStates()
    {
        _importFilesCommand.NotifyCanExecuteChanged();
        _selectOutputDirectoryCommand.NotifyCanExecuteChanged();
        _clearOutputDirectoryCommand.NotifyCanExecuteChanged();
        _removeMaterialCommand.NotifyCanExecuteChanged();
        _startProcessingCommand.NotifyCanExecuteChanged();
        _cancelProcessingCommand.NotifyCanExecuteChanged();
    }
}
