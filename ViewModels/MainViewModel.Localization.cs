using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Vidvix.Core.Models;

namespace Vidvix.ViewModels;

public sealed partial class MainViewModel
{
    private IReadOnlyList<ThemePreferenceOption> _themeOptions = Array.Empty<ThemePreferenceOption>();
    private IReadOnlyList<TranscodingModeOption> _transcodingOptions = Array.Empty<TranscodingModeOption>();
    private IReadOnlyList<ProcessingModeOption> _processingModes = Array.Empty<ProcessingModeOption>();
    private IReadOnlyList<LocalizationLanguageOption> _availableLanguages = Array.Empty<LocalizationLanguageOption>();
    private string _selectedLanguageCode = string.Empty;
    private DesktopShortcutNotificationState _desktopShortcutNotificationState;
    private bool _isSynchronizingLocalizationSelection;

    public event Action? LocalizationRefreshRequested;

    public IReadOnlyList<LocalizationLanguageOption> AvailableLanguages
    {
        get => _availableLanguages;
        private set => SetProperty(ref _availableLanguages, value);
    }

    public string SelectedLanguageCode
    {
        get => string.IsNullOrWhiteSpace(_selectedLanguageCode)
            ? ResolveLanguageOption(_localizationService.CurrentLanguage).Code
            : _selectedLanguageCode;
        set
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                return;
            }

            var resolvedCode = ResolveLanguageOption(value).Code;
            var currentCode = SelectedLanguageCode;
            if (!SetProperty(ref _selectedLanguageCode, resolvedCode))
            {
                return;
            }

            OnPropertyChanged(nameof(SelectedLanguageOption));
            OnPropertyChanged(nameof(CurrentLanguageDisplayText));

            if (_isSynchronizingLocalizationSelection ||
                string.Equals(currentCode, resolvedCode, StringComparison.OrdinalIgnoreCase))
            {
                return;
            }

            _ = ChangeLanguageAsync(resolvedCode);
        }
    }

    public LocalizationLanguageOption SelectedLanguageOption =>
        ResolveLanguageOption(SelectedLanguageCode);

    public string MainWindowTitleText =>
        GetLocalizedText("mainWindow.title.application", _configuration.ApplicationTitle);

    public string MainWindowSettingsButtonLabel =>
        GetLocalizedText("mainWindow.toolbar.applicationSettings", "应用设置");

    public string ExecutionProgressTitleText =>
        GetLocalizedText("mainWindow.progress.title", "处理进度");

    public string SettingsPaneTitleText =>
        GetLocalizedText("settings.pane.title", "应用设置");

    public string SettingsPaneCloseButtonText =>
        GetLocalizedText("common.action.close", "关闭");

    public string SettingsAppearanceTitleText =>
        GetLocalizedText("settings.appearance.title", "外观");

    public string SettingsThemeLabelText =>
        GetLocalizedText("settings.appearance.theme.label", "软件主题");

    public string SettingsLanguageSectionTitleText =>
        GetLocalizedText("settings.language.label", "界面语言");

    public string SettingsLanguageDescriptionText =>
        GetLocalizedText("settings.language.description", "切换后当前窗口会即时刷新，无需重新启动。");

    public string CurrentLanguageDisplayText =>
        FormatLocalizedText(
            "settings.language.currentValue",
            $"当前语言：{GetLanguageDisplayName(SelectedLanguageCode)}",
            ("language", GetLanguageDisplayName(SelectedLanguageCode)));

    public string CommonToggleOnText =>
        GetLocalizedText("common.toggle.on", "开启");

    public string CommonToggleOffText =>
        GetLocalizedText("common.toggle.off", "关闭");

    public string SettingsSystemTrayTitleText =>
        GetLocalizedText("settings.systemTray.title", "系统托盘");

    public string SettingsSystemTrayToggleHeaderText =>
        GetLocalizedText("settings.systemTray.toggleHeader", "关闭窗口时保留在系统托盘");

    public string SettingsSystemTrayDescriptionText =>
        GetLocalizedText(
            "settings.systemTray.description",
            "开启后，点击右上角关闭按钮不会退出应用，而是隐藏到系统托盘继续运行。右键托盘图标可选择“显示窗口”或“退出”。");

    public string SettingsDesktopShortcutTitleText =>
        GetLocalizedText("settings.desktopShortcut.title", "桌面快捷方式");

    public string SettingsDesktopShortcutDescriptionText =>
        GetLocalizedText(
            "settings.desktopShortcut.description",
            "检测桌面是否已存在当前应用快捷方式；如果不存在，会自动为你创建。");

    public string SettingsDesktopShortcutButtonText =>
        GetLocalizedText("settings.desktopShortcut.button", "生成桌面快捷方式");

    public string SettingsProcessingBehaviorTitleText =>
        GetLocalizedText("settings.processingBehavior.title", "处理完成行为");

    public string SettingsProcessingBehaviorToggleHeaderText =>
        GetLocalizedText("settings.processingBehavior.toggleHeader", "处理完成后定位输出文件");

    public string SettingsProcessingBehaviorDescriptionText =>
        GetLocalizedText(
            "settings.processingBehavior.description",
            "开启后，处理完成时会打开输出文件所在文件夹，并自动选中对应文件。批量任务会定位最后一个成功输出的文件。");

    public string SettingsTranscodingTitleText =>
        GetLocalizedText("settings.transcoding.title", "转码方式");

    public string SettingsTranscodingLabelText =>
        GetLocalizedText("settings.transcoding.label", "默认转码策略");

    public string SettingsGpuAccelerationTitleText =>
        GetLocalizedText("settings.transcoding.gpu.title", "GPU 加速");

    public string SettingsGpuAccelerationToggleHeaderText =>
        GetLocalizedText("settings.transcoding.gpu.toggleHeader", "是否开启 GPU 加速");

    private void InitializeLocalizationState(string? preferredLanguageCode)
    {
        ThemeOptions = BuildThemeOptions();
        TranscodingOptions = BuildTranscodingOptions();
        ProcessingModes = BuildProcessingModes();
        RebuildWorkspaceProfiles();
        AvailableLanguages = BuildLanguageOptions();
        SynchronizeLocalizationSelection(preferredLanguageCode);
    }

    private void SynchronizeLocalizationStateWithService() =>
        ApplyLocalizationState(_localizationService.CurrentLanguage);

    private void OnLocalizationLanguageChanged(object? sender, EventArgs args) =>
        _dispatcherService.TryEnqueue(() => ApplyLocalizationState(_localizationService.CurrentLanguage));

    private void ApplyLocalizationState(string? languageCode)
    {
        var themePreference = SelectedThemeOption.Preference;
        var transcodingMode = SelectedTranscodingModeOption.Mode;
        var processingMode = _selectedProcessingMode?.Mode;
        var outputFormatExtension = _selectedOutputFormat?.Extension;

        ThemeOptions = BuildThemeOptions();
        TranscodingOptions = BuildTranscodingOptions();
        ProcessingModes = BuildProcessingModes();
        RebuildWorkspaceProfiles();
        AvailableLanguages = BuildLanguageOptions();

        _selectedThemeOption = ResolveThemePreference(themePreference);
        OnPropertyChanged(nameof(SelectedThemeOption));

        _selectedTranscodingModeOption = ResolveTranscodingMode(transcodingMode);
        OnPropertyChanged(nameof(SelectedTranscodingModeOption));

        _selectedProcessingMode = ResolveProcessingMode(processingMode);
        OnPropertyChanged(nameof(SelectedProcessingMode));

        ReloadOutputFormats(outputFormatExtension);

        SynchronizeLocalizationSelection(languageCode);
        RefreshLocalizedTextProperties();
        RefreshDesktopShortcutNotificationText();
        RefreshWorkspaceLocalization();
        TrimWorkspace.RefreshLocalization();
        MergeWorkspace.RefreshLocalization();
        SplitAudioWorkspace.RefreshLocalization();
        LocalizationRefreshRequested?.Invoke();
    }

    private void SynchronizeLocalizationSelection(string? languageCode)
    {
        _isSynchronizingLocalizationSelection = true;

        try
        {
            SetProperty(ref _selectedLanguageCode, ResolveLanguageOption(languageCode).Code, nameof(SelectedLanguageCode));
            OnPropertyChanged(nameof(SelectedLanguageOption));
            OnPropertyChanged(nameof(CurrentLanguageDisplayText));
        }
        finally
        {
            _isSynchronizingLocalizationSelection = false;
        }
    }

    private async Task ChangeLanguageAsync(string languageCode)
    {
        try
        {
            await _localizationService.SetLanguageAsync(languageCode).ConfigureAwait(false);
        }
        catch (Exception exception)
        {
            _logger.Log(LogLevel.Warning, "切换界面语言时发生异常，已保留当前语言。", exception);
            _dispatcherService.TryEnqueue(SynchronizeLocalizationStateWithService);
        }
    }

    private IReadOnlyList<ThemePreferenceOption> BuildThemeOptions() =>
        new[]
        {
            new ThemePreferenceOption(
                ThemePreference.UseSystem,
                GetLocalizedText("settings.appearance.theme.option.system", "跟随系统"),
                GetLocalizedText(
                    "settings.appearance.theme.option.systemDescription",
                    "根据 Windows 当前主题自动切换明亮和暗黑外观。")),
            new ThemePreferenceOption(
                ThemePreference.Light,
                GetLocalizedText("settings.appearance.theme.option.light", "明亮主题"),
                GetLocalizedText(
                    "settings.appearance.theme.option.lightDescription",
                    "始终使用明亮外观，适合高亮环境。")),
            new ThemePreferenceOption(
                ThemePreference.Dark,
                GetLocalizedText("settings.appearance.theme.option.dark", "暗黑主题"),
                GetLocalizedText(
                    "settings.appearance.theme.option.darkDescription",
                    "始终使用暗黑外观，适合低亮环境。"))
        };

    private IReadOnlyList<TranscodingModeOption> BuildTranscodingOptions() =>
        new[]
        {
            new TranscodingModeOption(
                TranscodingMode.FastContainerConversion,
                GetLocalizedText("settings.transcoding.option.fast", "快速换封装（默认）"),
                GetLocalizedText(
                    "settings.transcoding.option.fastDescription",
                    "保持当前默认行为：优先直接复用原始流，必要时沿用现有的兼容编码策略，速度更快。")),
            new TranscodingModeOption(
                TranscodingMode.FullTranscode,
                GetLocalizedText("settings.transcoding.option.full", "真正转码（重新编码）"),
                GetLocalizedText(
                    "settings.transcoding.option.fullDescription",
                    "先解码再编码，重新生成音视频数据；更适合需要统一编码格式、兼容性或后续编辑的场景。"))
        };

    private IReadOnlyList<ProcessingModeOption> BuildProcessingModes() =>
        _configuration.SupportedProcessingModes
            .Select(option => option.Localize(_localizationService))
            .ToArray();

    private IReadOnlyList<LocalizationLanguageOption> BuildLanguageOptions() =>
        _localizationService.AvailableLanguages
            .Select(option => new LocalizationLanguageOption(
                option.Code,
                GetLanguageDisplayName(option.Code, option.NativeDisplayName, option.DisplayName),
                option.NativeDisplayName))
            .ToArray();

    private ThemePreferenceOption ResolveThemePreference(ThemePreference themePreference) =>
        ThemeOptions.FirstOrDefault(option => option.Preference == themePreference)
        ?? ThemeOptions.First();

    private LocalizationLanguageOption ResolveLanguageOption(string? languageCode)
    {
        if (AvailableLanguages.Count == 0)
        {
            var fallbackCode = languageCode ?? _localizationService.FallbackLanguage;
            return new LocalizationLanguageOption(fallbackCode, fallbackCode, fallbackCode);
        }

        return AvailableLanguages.FirstOrDefault(option =>
                   string.Equals(option.Code, languageCode, StringComparison.OrdinalIgnoreCase))
               ?? AvailableLanguages.FirstOrDefault(option =>
                   string.Equals(option.Code, _localizationService.FallbackLanguage, StringComparison.OrdinalIgnoreCase))
               ?? AvailableLanguages[0];
    }

    private string GetLocalizedText(string key, string fallback) =>
        _localizationService.GetString(key, fallback);

    private string FormatLocalizedText(string key, string fallback, params (string Name, object? Value)[] arguments)
    {
        Dictionary<string, object?>? localizedArguments = null;
        if (arguments.Length > 0)
        {
            localizedArguments = new Dictionary<string, object?>(arguments.Length, StringComparer.Ordinal);
            foreach (var argument in arguments)
            {
                localizedArguments[argument.Name] = argument.Value;
            }
        }

        return _localizationService.Format(key, localizedArguments, fallback);
    }

    private string GetLanguageDisplayName(
        string? languageCode,
        string? preferredFallback = null,
        string? secondaryFallback = null)
    {
        var keySegment = NormalizeLanguageKeySegment(languageCode);
        var fallback = !string.IsNullOrWhiteSpace(preferredFallback)
            ? preferredFallback
            : secondaryFallback ?? languageCode ?? _localizationService.FallbackLanguage;

        return GetLocalizedText($"common.language.option.{keySegment}", fallback);
    }

    private static string NormalizeLanguageKeySegment(string? languageCode) =>
        string.IsNullOrWhiteSpace(languageCode)
            ? "unknown"
            : languageCode.Trim().ToLowerInvariant().Replace('_', '-');

    private void RefreshLocalizedTextProperties()
    {
        OnPropertyChanged(nameof(MainWindowTitleText));
        OnPropertyChanged(nameof(MainWindowSettingsButtonLabel));
        OnPropertyChanged(nameof(ExecutionProgressTitleText));
        OnPropertyChanged(nameof(SettingsPaneTitleText));
        OnPropertyChanged(nameof(SettingsPaneCloseButtonText));
        OnPropertyChanged(nameof(SettingsPaneDescription));
        OnPropertyChanged(nameof(SettingsAppearanceTitleText));
        OnPropertyChanged(nameof(SettingsThemeLabelText));
        OnPropertyChanged(nameof(SettingsLanguageSectionTitleText));
        OnPropertyChanged(nameof(SettingsLanguageDescriptionText));
        OnPropertyChanged(nameof(CommonToggleOnText));
        OnPropertyChanged(nameof(CommonToggleOffText));
        OnPropertyChanged(nameof(SettingsSystemTrayTitleText));
        OnPropertyChanged(nameof(SettingsSystemTrayToggleHeaderText));
        OnPropertyChanged(nameof(SettingsSystemTrayDescriptionText));
        OnPropertyChanged(nameof(SettingsDesktopShortcutTitleText));
        OnPropertyChanged(nameof(SettingsDesktopShortcutDescriptionText));
        OnPropertyChanged(nameof(SettingsDesktopShortcutButtonText));
        OnPropertyChanged(nameof(SettingsProcessingBehaviorTitleText));
        OnPropertyChanged(nameof(SettingsPaneProcessingBehaviorInfoTooltip));
        OnPropertyChanged(nameof(SettingsProcessingBehaviorToggleHeaderText));
        OnPropertyChanged(nameof(SettingsProcessingBehaviorDescriptionText));
        OnPropertyChanged(nameof(SettingsTranscodingTitleText));
        OnPropertyChanged(nameof(SettingsPaneTranscodingInfoTooltip));
        OnPropertyChanged(nameof(SettingsTranscodingLabelText));
        OnPropertyChanged(nameof(SettingsGpuAccelerationTitleText));
        OnPropertyChanged(nameof(SettingsGpuAccelerationToggleHeaderText));
        OnPropertyChanged(nameof(GpuAccelerationDescription));
        OnPropertyChanged(nameof(CurrentLanguageDisplayText));
        OnPropertyChanged(nameof(WorkspaceHeaderCaption));
        OnPropertyChanged(nameof(WorkspaceHeaderTitle));
        OnPropertyChanged(nameof(WorkspaceHeaderDescription));
        OnPropertyChanged(nameof(QueueDragDropHintText));
        OnPropertyChanged(nameof(DragDropCaptionText));
        OnPropertyChanged(nameof(FixedProcessingModeDisplayName));
        OnPropertyChanged(nameof(FixedProcessingModeDescription));
        OnPropertyChanged(nameof(ProcessingModes));
        OnPropertyChanged(nameof(AvailableOutputFormats));
        OnPropertyChanged(nameof(SelectedOutputFormat));
        OnPropertyChanged(nameof(SelectedOutputFormatDescription));
        OnPropertyChanged(nameof(SupportedInputFormatsHint));
        RefreshExecutionProgressLocalization();
    }

    private void SetDesktopShortcutNotificationState(
        DesktopShortcutNotificationState notificationState)
    {
        _desktopShortcutNotificationState = notificationState;
        RefreshDesktopShortcutNotificationText();
    }

    private void RefreshDesktopShortcutNotificationText()
    {
        DesktopShortcutNotificationMessage = _desktopShortcutNotificationState switch
        {
            DesktopShortcutNotificationState.Created => GetLocalizedText(
                "settings.desktopShortcut.notification.created",
                "已在桌面创建应用快捷方式。"),
            DesktopShortcutNotificationState.Exists => GetLocalizedText(
                "settings.desktopShortcut.notification.exists",
                "桌面快捷方式已存在。"),
            DesktopShortcutNotificationState.Failed => GetLocalizedText(
                "settings.desktopShortcut.notification.failed",
                "创建桌面快捷方式失败，请稍后重试。"),
            _ => DesktopShortcutNotificationMessage
        };
    }

    private enum DesktopShortcutNotificationState
    {
        None,
        Created,
        Exists,
        Failed
    }
}
