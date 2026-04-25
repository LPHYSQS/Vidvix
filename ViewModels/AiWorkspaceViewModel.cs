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
    private readonly IMediaInfoService? _mediaInfoService;
    private readonly IFileRevealService? _fileRevealService;
    private readonly ILogger? _logger;
    private readonly AsyncRelayCommand _importFilesCommand;
    private readonly AsyncRelayCommand _selectOutputDirectoryCommand;
    private readonly RelayCommand _clearOutputDirectoryCommand;
    private readonly RelayCommand _clearCurrentInputCommand;
    private readonly RelayCommand _removeMaterialCommand;
    private readonly AsyncRelayCommand _startProcessingCommand;
    private readonly RelayCommand _cancelProcessingCommand;
    private readonly RelayCommand _revealLatestOutputCommand;
    private bool _isProcessing;
    private bool _suppressSelectionStatus;
    private string _statusText;
    private Func<string>? _statusTextResolver;

    public AiWorkspaceViewModel()
        : this(
            new ApplicationConfiguration(),
            localizationService: null,
            filePickerService: null,
            mediaImportDiscoveryService: null,
            mediaInfoService: null,
            aiRuntimeCatalogService: null,
            aiEnhancementWorkflowService: null,
            aiInterpolationWorkflowService: null,
            userPreferencesService: null,
            fileRevealService: null,
            logger: null)
    {
    }

    public AiWorkspaceViewModel(
        ApplicationConfiguration configuration,
        ILocalizationService? localizationService,
        IFilePickerService? filePickerService,
        IMediaImportDiscoveryService? mediaImportDiscoveryService,
        IMediaInfoService? mediaInfoService,
        IAiRuntimeCatalogService? aiRuntimeCatalogService,
        IAiEnhancementWorkflowService? aiEnhancementWorkflowService,
        IAiInterpolationWorkflowService? aiInterpolationWorkflowService,
        IUserPreferencesService? userPreferencesService,
        IFileRevealService? fileRevealService,
        ILogger? logger)
    {
        _configuration = configuration ?? throw new ArgumentNullException(nameof(configuration));
        _localizationService = localizationService;
        _filePickerService = filePickerService;
        _mediaImportDiscoveryService = mediaImportDiscoveryService;
        _mediaInfoService = mediaInfoService;
        _fileRevealService = fileRevealService;
        _aiRuntimeCatalogService = aiRuntimeCatalogService;
        _logger = logger;

        MaterialLibrary = new AiMaterialLibraryState(localizationService);
        InputState = new AiInputState();
        ModeState = new AiModeState();
        OutputSettings = new AiOutputSettingsState(BuildLaunchOutputFormats());
        EnhancementSettings = new AiEnhancementSettingsState(
            BuildEnhancementModelTierOptions(),
            BuildEnhancementScaleOptions());
        EnhancementExecution = new AiEnhancementExecutionState();
        InterpolationSettings = new AiInterpolationSettingsState(
            BuildInterpolationScaleFactorOptions(),
            BuildInterpolationDeviceOptions());
        InterpolationExecution = new AiInterpolationExecutionState();
        _statusText = string.Empty;
        _aiEnhancementExecutionCoordinator =
            aiEnhancementWorkflowService is not null &&
            localizationService is not null &&
            userPreferencesService is not null &&
            fileRevealService is not null &&
            logger is not null
                ? new AiEnhancementExecutionCoordinator(
                    aiEnhancementWorkflowService,
                    localizationService,
                    userPreferencesService,
                    fileRevealService,
                    logger)
                : null;
        _aiInterpolationExecutionCoordinator =
            aiInterpolationWorkflowService is not null &&
            localizationService is not null &&
            userPreferencesService is not null &&
            fileRevealService is not null &&
            logger is not null
                ? new AiInterpolationExecutionCoordinator(
                    aiInterpolationWorkflowService,
                    localizationService,
                    userPreferencesService,
                    fileRevealService,
                    logger)
                : null;

        _importFilesCommand = new AsyncRelayCommand(ImportFilesAsync, CanImportFilesInternal);
        _selectOutputDirectoryCommand = new AsyncRelayCommand(SelectOutputDirectoryAsync, CanSelectOutputDirectoryInternal);
        _clearOutputDirectoryCommand = new RelayCommand(ClearOutputDirectory, CanClearOutputDirectoryInternal);
        _clearCurrentInputCommand = new RelayCommand(ClearCurrentInput, CanClearCurrentInputInternal);
        _removeMaterialCommand = new RelayCommand(RemoveMaterial, CanRemoveMaterial);
        _startProcessingCommand = new AsyncRelayCommand(StartProcessingAsync, CanStartProcessingInternal);
        _cancelProcessingCommand = new RelayCommand(CancelProcessing, () => IsProcessing);
        _revealLatestOutputCommand = new RelayCommand(RevealLatestOutput, CanRevealLatestOutput);

        MaterialLibrary.PropertyChanged += OnMaterialLibraryPropertyChanged;
        ModeState.PropertyChanged += OnModeStatePropertyChanged;
        OutputSettings.PropertyChanged += OnOutputSettingsPropertyChanged;
        EnhancementSettings.PropertyChanged += OnEnhancementSettingsPropertyChanged;

        RefreshOutputContext();
        RefreshInterpolationLocalization();
        RefreshEnhancementLocalization();
        RefreshInterpolationModeProperties();
        RefreshEnhancementModeProperties();
        SetStatusText("ai.status.ready", "先导入一个或多个视频，再从素材列表中锁定当前处理对象。");
    }

    public AiMaterialLibraryState MaterialLibrary { get; }

    public AiInputState InputState { get; }

    public AiModeState ModeState { get; }

    public AiOutputSettingsState OutputSettings { get; }

    public ICommand ImportFilesCommand => _importFilesCommand;

    public ICommand SelectOutputDirectoryCommand => _selectOutputDirectoryCommand;

    public ICommand ClearOutputDirectoryCommand => _clearOutputDirectoryCommand;

    public ICommand ClearCurrentInputCommand => _clearCurrentInputCommand;

    public ICommand RemoveMaterialCommand => _removeMaterialCommand;

    public ICommand StartProcessingCommand => _startProcessingCommand;

    public ICommand CancelProcessingCommand => _cancelProcessingCommand;

    public ICommand RevealLatestOutputCommand => _revealLatestOutputCommand;

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
            OnPropertyChanged(nameof(CanEditProcessingParameters));
            OnPropertyChanged(nameof(CanModifyMaterials));
            NotifyCommandStates();
        }
    }

    public bool CanStartProcessing =>
        InputState.HasCurrentMaterial &&
        !IsProcessing;

    public bool CanModifyMaterials => !IsProcessing;

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
            "R9 已交付 AI补帧 与 AI增强 首发闭环：支持 RIFE 2x/4x 补帧，以及 Real-ESRGAN Standard / Anime、2x 到 16x 增强、超采样回缩、原音轨回填、进度反馈与取消清理。");

    public string MaterialsSectionTitleText =>
        GetLocalizedText("ai.page.materials.title", "素材列表");

    public string MaterialsSectionDescriptionText =>
        GetLocalizedText(
            "ai.page.materials.description",
            "导入要处理的视频，\n再从列表中选择当前素材。");

    public string MaterialsImportButtonText =>
        GetLocalizedText("ai.page.materials.importFiles", "导入视频");

    public string MaterialsPlaceholderText =>
        GetLocalizedText(
            "ai.page.materials.placeholder",
            "先导入一个或多个视频，再从素材列表中锁定当前处理对象。");

    public string RemoveMaterialButtonText =>
        GetLocalizedText("ai.page.materials.remove", "移除");

    public string RevealLatestOutputButtonText =>
        GetLocalizedText("ai.page.output.reveal", "定位文件");

    public string WorkspaceSectionTitleText =>
        GetLocalizedText("ai.page.workspace.title", "AI 工作区");

    public string WorkspaceSectionDescriptionText =>
        GetLocalizedText(
            "ai.page.workspace.description",
            "模式切换只影响 AI 参数壳层与后续执行规划，不会污染已导入素材和当前处理对象。");

    public string WorkspaceModeSwitchHintText =>
        GetLocalizedText(
            "ai.page.workspace.modeHint",
            "AI补帧 与 AI增强 共用同一份素材库与当前视频选择；两个模式都已接入独立 workflow，切换模式不会污染当前输入与输出状态。");

    public string InterpolationModeLabelText =>
        GetLocalizedText("ai.interpolation.modeLabel", "AI补帧");

    public string EnhancementModeLabelText =>
        GetLocalizedText("ai.enhancement.modeLabel", "AI增强");

    public string CurrentModeDescriptionText =>
        ModeState.SelectedMode == AiWorkspaceMode.Interpolation
            ? GetLocalizedText(
                "ai.interpolation.modeDescription",
                "首发路线固定为 RIFE，当前已支持 2x / 4x 补帧、设备策略、进度反馈、原音轨回填与取消清理。")
            : GetLocalizedText(
                "ai.enhancement.modeDescription",
                "首发路线固定为 Real-ESRGAN，当前已支持 Standard / Anime、2x 到 16x 倍率、组合放大、超采样回缩、原音轨回填、进度反馈与取消清理。");

    public string CurrentTrackTitleText =>
        GetLocalizedText("ai.page.workspace.inputTrackTitle", "当前输入轨道");

    public string ClearCurrentInputButtonText =>
        GetLocalizedText("ai.page.workspace.clearCurrentInput", "移除当前输入");

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
                "当前为 AI补帧 模式，单次只会将一个视频作为补帧输入；倍率、设备、进度和输出结果都挂在本模式下。")
            : GetLocalizedText(
                "ai.enhancement.trackHint",
                "当前为 AI增强 模式，单次只会将一个视频作为增强输入；模型档位、倍率链路、高倍率提醒与最近结果都挂在本模式下。");

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
            "输出设置已直接接入 AI补帧 与 AI增强 workflow：格式仍冻结为 MP4 / MKV，目录默认跟随当前素材，文件名留空时自动按模式生成。");

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
        ModeState.SelectedMode == AiWorkspaceMode.Interpolation
            ? FormatLocalizedText(
                "ai.page.output.summary",
                $"当前参数状态：模式 {GetCurrentModeDisplayName()}，输入 {GetCurrentInputSummaryText()}，输出 {OutputSettings.SelectedOutputFormat.DisplayName}，文件名 {OutputSettings.EffectiveOutputFileName}，原音轨默认跟随源文件。",
                ("mode", GetCurrentModeDisplayName()),
                ("input", GetCurrentInputSummaryText()),
                ("format", OutputSettings.SelectedOutputFormat.DisplayName),
                ("fileName", OutputSettings.EffectiveOutputFileName))
            : BuildEnhancementParameterSummaryText();

    public void RefreshLocalization()
    {
        OutputSettings.ReloadAvailableOutputFormats(BuildLaunchOutputFormats());
        MaterialLibrary.RefreshLocalization();

        OnPropertyChanged(nameof(SectionCaptionText));
        OnPropertyChanged(nameof(PageTitleText));
        OnPropertyChanged(nameof(PageDescriptionText));
        OnPropertyChanged(nameof(MaterialsSectionTitleText));
        OnPropertyChanged(nameof(MaterialsSectionDescriptionText));
        OnPropertyChanged(nameof(MaterialsImportButtonText));
        OnPropertyChanged(nameof(MaterialsPlaceholderText));
        OnPropertyChanged(nameof(RemoveMaterialButtonText));
        OnPropertyChanged(nameof(RevealLatestOutputButtonText));
        OnPropertyChanged(nameof(WorkspaceSectionTitleText));
        OnPropertyChanged(nameof(WorkspaceSectionDescriptionText));
        OnPropertyChanged(nameof(WorkspaceModeSwitchHintText));
        OnPropertyChanged(nameof(InterpolationModeLabelText));
        OnPropertyChanged(nameof(EnhancementModeLabelText));
        OnPropertyChanged(nameof(CurrentModeDescriptionText));
        OnPropertyChanged(nameof(CurrentTrackTitleText));
        OnPropertyChanged(nameof(ClearCurrentInputButtonText));
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
        RefreshInterpolationLocalization();
        RefreshEnhancementLocalization();
        RefreshLocalizedStatusText();
        RefreshInterpolationOutcomeLocalization();
        RefreshEnhancementOutcomeLocalization();
    }

    private async Task ImportFilesAsync()
    {
        if (TrySetProcessingLockedStatus("ai.operation.importMaterials", "导入素材"))
        {
            return;
        }

        if (_filePickerService is null || _mediaImportDiscoveryService is null)
        {
            SetStatusText("ai.status.importUnavailable", "当前运行环境未接入文件选择器，暂时无法导入 AI 视频素材。");
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
                SetStatusText("ai.status.importCancelled", "已取消视频导入。");
                return;
            }

            await ImportPathsAsync(selectedFiles);
        }
        catch (OperationCanceledException)
        {
            SetStatusText("ai.status.importCancelled", "已取消视频导入。");
        }
        catch (Exception exception)
        {
            SetStatusText("ai.status.importFailed", "导入视频素材失败，请重试。");
            _logger?.Log(LogLevel.Error, "导入 AI 视频素材时发生异常。", exception);
        }
    }

    private async Task ImportPathsAsync(IEnumerable<string> inputPaths)
    {
        if (TrySetProcessingLockedStatus("ai.operation.importMaterials", "导入素材"))
        {
            return;
        }

        if (_mediaImportDiscoveryService is null)
        {
            SetStatusText("ai.status.importUnavailable", "当前运行环境未接入文件选择器，暂时无法导入 AI 视频素材。");
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
            if (invalidEntryCount > 0)
            {
                SetStatusText(() => CreateRejectedOnlyStatusMessage(invalidEntryCount));
            }
            else
            {
                SetStatusText("ai.status.noProcessable", "没有发现可导入的视频文件。");
            }

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
            var importResult = await AddSupportedMaterialsAsync(discovery.SupportedFiles);
            SyncInputSelection(updateStatus: false);
            SetStatusText(() => CreateImportStatusMessage(importResult, rejectedCount));
        }
        finally
        {
            _suppressSelectionStatus = false;
        }

        NotifyCommandStates();
    }

    private async Task<AiMaterialImportResult> AddSupportedMaterialsAsync(IEnumerable<string> filePaths)
    {
        ArgumentNullException.ThrowIfNull(filePaths);

        var existingPaths = new HashSet<string>(
            MaterialLibrary.Materials.Select(item => item.InputPath),
            StringComparer.OrdinalIgnoreCase);

        var newMaterials = new List<AiMaterialItemViewModel>();
        var duplicateCount = 0;

        foreach (var filePath in filePaths)
        {
            if (string.IsNullOrWhiteSpace(filePath))
            {
                continue;
            }

            var normalizedPath = Path.GetFullPath(filePath);
            if (!existingPaths.Add(normalizedPath))
            {
                duplicateCount++;
                continue;
            }

            newMaterials.Add(await CreateMaterialItemAsync(normalizedPath));
        }

        var importResult = MaterialLibrary.AddMaterials(newMaterials);
        return new AiMaterialImportResult(importResult.AddedCount, duplicateCount + importResult.DuplicateCount);
    }

    private async Task<AiMaterialItemViewModel> CreateMaterialItemAsync(string filePath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(filePath);

        var durationText = string.Empty;

        if (_mediaInfoService is not null)
        {
            try
            {
                var loadResult = _mediaInfoService.TryGetCachedDetails(filePath, out var cachedSnapshot)
                    ? MediaDetailsLoadResult.Success(cachedSnapshot)
                    : await _mediaInfoService.GetMediaDetailsAsync(filePath).ConfigureAwait(false);

                if (loadResult.IsSuccess && loadResult.Snapshot?.MediaDuration is { } mediaDuration && mediaDuration > TimeSpan.Zero)
                {
                    durationText = FormatDuration(mediaDuration);
                }
                else if (!string.IsNullOrWhiteSpace(loadResult.ErrorMessage))
                {
                    _logger?.Log(LogLevel.Warning, $"读取 AI 素材信息失败：{Path.GetFileName(filePath)}，已按基础信息导入。");
                }
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception exception)
            {
                _logger?.Log(LogLevel.Warning, $"读取 AI 素材信息失败：{Path.GetFileName(filePath)}，已按基础信息导入。", exception);
            }
        }

        return new AiMaterialItemViewModel(filePath, durationText, _localizationService);
    }

    private async Task SelectOutputDirectoryAsync()
    {
        if (TrySetProcessingLockedStatus("ai.operation.selectOutputDirectory", "选择输出目录"))
        {
            return;
        }

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
                SetStatusText("ai.status.outputDirectorySelectionCancelled", "已取消选择输出目录。");
                return;
            }

            if (OutputSettings.TrySetCustomOutputDirectory(selectedFolder))
            {
                var outputDirectory = OutputSettings.CustomOutputDirectory;
                SetStatusText(() => CreateOutputDirectorySelectedStatusMessage(outputDirectory));
            }
        }
        catch (OperationCanceledException)
        {
            SetStatusText("ai.status.outputDirectorySelectionCancelled", "已取消选择输出目录。");
        }
        catch (Exception exception)
        {
            SetStatusText("ai.status.outputDirectorySelectionFailed", "选择输出目录失败，请重试。");
            _logger?.Log(LogLevel.Error, "选择 AI 输出目录时发生异常。", exception);
        }
    }

    private void ClearOutputDirectory()
    {
        if (TrySetProcessingLockedStatus("ai.operation.clearOutputDirectory", "清空输出目录"))
        {
            return;
        }

        if (!OutputSettings.ClearCustomOutputDirectory())
        {
            return;
        }

        SetStatusText("ai.status.outputDirectoryCleared", "已恢复为跟随当前素材目录输出。");
    }

    private void ClearCurrentInput()
    {
        if (TrySetProcessingLockedStatus("ai.operation.clearCurrentInput", "移除当前输入"))
        {
            return;
        }

        if (!InputState.HasCurrentMaterial)
        {
            return;
        }

        var fileName = InputState.CurrentInputFileName;

        _suppressSelectionStatus = true;
        try
        {
            MaterialLibrary.SelectedMaterial = null;
        }
        finally
        {
            _suppressSelectionStatus = false;
        }

        SetStatusText(() => CreateCurrentInputClearedStatusMessage(fileName));
        NotifyCommandStates();
    }

    private void RemoveMaterial(object? parameter)
    {
        if (TrySetProcessingLockedStatus("ai.operation.removeMaterial", "移除素材"))
        {
            return;
        }

        if (parameter is not AiMaterialItemViewModel material)
        {
            return;
        }

        _suppressSelectionStatus = true;
        try
        {
            if (!MaterialLibrary.RemoveMaterial(material))
            {
                return;
            }
        }
        finally
        {
            _suppressSelectionStatus = false;
        }

        if (MaterialLibrary.HasMaterials)
        {
            var fileName = material.InputFileName;
            SetStatusText(() => CreateRemovedMaterialStatusMessage(fileName));
        }
        else
        {
            SetStatusText("ai.status.libraryCleared", "素材列表已清空，当前没有可处理视频。");
        }

        NotifyCommandStates();
    }

    private Task StartProcessingAsync() => StartCurrentModeProcessingAsync();

    private void CancelProcessing() => CancelCurrentProcessing();

    private void OnMaterialLibraryPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(AiMaterialLibraryState.SelectedMaterial))
        {
            SyncInputSelection(updateStatus: !_suppressSelectionStatus);
        }

        if (e.PropertyName == nameof(AiMaterialLibraryState.MaterialCount) ||
            e.PropertyName == nameof(AiMaterialLibraryState.HasMaterials))
        {
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
        RefreshInterpolationModeProperties();
        RefreshInterpolationLocalization();
        RefreshEnhancementModeProperties();
        RefreshEnhancementLocalization();
        SetStatusText(() => CreateModeChangedStatusMessage(GetCurrentModeDisplayName()));
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
        if (IsProcessing &&
            !ReferenceEquals(InputState.CurrentMaterial, MaterialLibrary.SelectedMaterial))
        {
            _suppressSelectionStatus = true;
            try
            {
                MaterialLibrary.SelectedMaterial = InputState.CurrentMaterial;
            }
            finally
            {
                _suppressSelectionStatus = false;
            }

            SetStatusText(
                () => FormatLocalizedText(
                    "ai.status.processingLocked",
                    "当前 {mode} 任务正在执行，请先取消后再{operation}。",
                    ("mode", GetCurrentModeDisplayName()),
                    ("operation", GetLocalizedText("ai.operation.selectMaterial", "切换当前素材"))));
            return;
        }

        InputState.SetCurrentMaterial(MaterialLibrary.SelectedMaterial);
        RefreshOutputContext();
        RefreshInputDependentProperties();
        RefreshInterpolationModeProperties();

        if (updateStatus)
        {
            if (InputState.HasCurrentMaterial)
            {
                SetStatusText(() => CreateSelectedMaterialStatusMessage(InputState.CurrentInputFileName));
            }
            else
            {
                SetStatusText("ai.status.ready", "先导入一个或多个视频，再从素材列表中锁定当前处理对象。");
            }
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
        OnPropertyChanged(nameof(ProcessingActionsHintText));
        _clearCurrentInputCommand.NotifyCanExecuteChanged();
        _startProcessingCommand.NotifyCanExecuteChanged();
        RefreshEnhancementModeProperties();
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

    private static string FormatDuration(TimeSpan duration) => duration.ToString(@"hh\:mm\:ss");

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
            return InputState.HasCurrentMaterial
                ? CreateImportedKeepingSelectionStatusMessage(importResult.AddedCount, InputState.CurrentInputFileName)
                : CreateImportedPendingSelectionStatusMessage(importResult.AddedCount);
        }

        if (importResult.AddedCount > 0)
        {
            return InputState.HasCurrentMaterial
                ? FormatLocalizedText(
                    "ai.status.importSummary",
                    $"已导入 {importResult.AddedCount} 个视频，跳过 {importResult.DuplicateCount} 个重复项，拒绝 {rejectedCount} 个非视频或不可用条目。",
                    ("addedCount", importResult.AddedCount),
                    ("duplicateCount", importResult.DuplicateCount),
                    ("rejectedCount", rejectedCount))
                : CreateImportSummaryPendingSelectionStatusMessage(importResult, rejectedCount);
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

    private string CreateImportedPendingSelectionStatusMessage(int count) =>
        FormatLocalizedText(
            "ai.status.imported",
            $"已导入 {count} 个视频素材，请从素材列表中选择当前处理对象。",
            ("count", count));

    private string CreateImportedKeepingSelectionStatusMessage(int count, string fileName) =>
        FormatLocalizedText(
            "ai.status.importedKeepingSelection",
            $"已导入 {count} 个视频素材，当前处理对象保持为：{fileName}",
            ("count", count),
            ("fileName", fileName));

    private string CreateImportSummaryPendingSelectionStatusMessage(AiMaterialImportResult importResult, int rejectedCount) =>
        FormatLocalizedText(
            "ai.status.importSummaryPendingSelection",
            $"已导入 {importResult.AddedCount} 个视频，跳过 {importResult.DuplicateCount} 个重复项，拒绝 {rejectedCount} 个非视频或不可用条目。请从素材列表中选择当前处理对象。",
            ("addedCount", importResult.AddedCount),
            ("duplicateCount", importResult.DuplicateCount),
            ("rejectedCount", rejectedCount));

    private string CreateSelectedMaterialStatusMessage(string fileName) =>
        FormatLocalizedText(
            "ai.status.selectedMaterial",
            $"当前处理对象已切换为：{fileName}",
            ("fileName", fileName));

    private string CreateCurrentInputClearedStatusMessage(string fileName) =>
        FormatLocalizedText(
            "ai.status.currentInputCleared",
            $"已从当前输入轨道移除：{fileName}",
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

    private string GetImportDuplicateStatusMessage() =>
        GetLocalizedText(
            "ai.status.duplicateOnly",
            "所选视频已全部在素材列表中，未重复导入。");

    private string GetNoProcessableImportMessage() =>
        GetLocalizedText(
            "ai.status.noProcessable",
            "没有发现可导入的视频文件。");

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

    private string NormalizeErrorMessage(string? message, string fallbackKey, string fallback)
    {
        var normalized = string.IsNullOrWhiteSpace(message)
            ? GetLocalizedText(fallbackKey, fallback)
            : string.Join(
                " ",
                message
                    .Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries)
                    .Select(segment => segment.Trim())
                    .Where(segment => !string.IsNullOrWhiteSpace(segment)));

        return normalized.Length == 0
            ? GetLocalizedText(fallbackKey, fallback)
            : normalized;
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

    private bool CanClearCurrentInputInternal() =>
        !IsProcessing &&
        InputState.HasCurrentMaterial;

    private bool CanRemoveMaterial(object? parameter) =>
        !IsProcessing &&
        parameter is AiMaterialItemViewModel material &&
        MaterialLibrary.Materials.Contains(material);

    private bool CanStartProcessingInternal() => CanStartProcessing;

    private void RevealLatestOutput(object? parameter)
    {
        if (_fileRevealService is null ||
            parameter is not string outputPath ||
            string.IsNullOrWhiteSpace(outputPath))
        {
            return;
        }

        try
        {
            _fileRevealService.RevealFile(outputPath);
        }
        catch (Exception exception)
        {
            _logger?.Log(LogLevel.Warning, "定位 AI 输出文件失败。", exception);
            SetStatusText(
                "ai.status.revealFailed",
                "定位输出文件失败，请检查文件是否仍然存在。");
        }
    }

    private bool CanRevealLatestOutput(object? parameter) =>
        _fileRevealService is not null &&
        parameter is string outputPath &&
        !string.IsNullOrWhiteSpace(outputPath);

    private bool TrySetProcessingLockedStatus(string operationKey, string operationFallback)
    {
        if (!IsProcessing)
        {
            return false;
        }

        SetStatusText(
            () => FormatLocalizedText(
                "ai.status.processingLocked",
                "当前 {mode} 任务正在执行，请先取消后再{operation}。",
                ("mode", GetCurrentModeDisplayName()),
                ("operation", GetLocalizedText(operationKey, operationFallback))));
        return true;
    }

    private void SetStatusText(Func<string> resolver)
    {
        ArgumentNullException.ThrowIfNull(resolver);

        _statusTextResolver = resolver;
        StatusText = resolver();
    }

    private void SetStatusText(string key, string fallback) =>
        SetStatusText(() => GetLocalizedText(key, fallback));

    private void RefreshLocalizedStatusText()
    {
        if (_statusTextResolver is not null)
        {
            StatusText = _statusTextResolver();
            return;
        }

        StatusText = InputState.HasCurrentMaterial
            ? CreateSelectedMaterialStatusMessage(InputState.CurrentInputFileName)
            : GetReadyStatusMessage();
    }

    private void NotifyCommandStates()
    {
        _importFilesCommand.NotifyCanExecuteChanged();
        _selectOutputDirectoryCommand.NotifyCanExecuteChanged();
        _clearOutputDirectoryCommand.NotifyCanExecuteChanged();
        _clearCurrentInputCommand.NotifyCanExecuteChanged();
        _removeMaterialCommand.NotifyCanExecuteChanged();
        _startProcessingCommand.NotifyCanExecuteChanged();
        _cancelProcessingCommand.NotifyCanExecuteChanged();
        _revealLatestOutputCommand.NotifyCanExecuteChanged();
    }

    private enum AiExecutionFeedbackKind
    {
        None = 0,
        Succeeded = 1,
        Cancelled = 2,
        Failed = 3
    }
}
