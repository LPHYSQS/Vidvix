using System;
using System.Collections.Generic;
using System.Linq;
using Vidvix.Core.Interfaces;
using Vidvix.Core.Models;
using Vidvix.Utils;

namespace Vidvix.ViewModels;

internal sealed class SplitAudioWorkspacePreferencesState
{
    private readonly ApplicationConfiguration _configuration;
    private readonly ILocalizationService _localizationService;
    private readonly IUserPreferencesService _userPreferencesService;
    private readonly ILogger _logger;

    public SplitAudioWorkspacePreferencesState(
        ApplicationConfiguration configuration,
        ILocalizationService localizationService,
        IUserPreferencesService userPreferencesService,
        ILogger logger)
    {
        _configuration = configuration ?? throw new ArgumentNullException(nameof(configuration));
        _localizationService = localizationService ?? throw new ArgumentNullException(nameof(localizationService));
        _userPreferencesService = userPreferencesService ?? throw new ArgumentNullException(nameof(userPreferencesService));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));

        AvailableOutputFormats = Array.Empty<OutputFormatOption>();
        AvailableAccelerationModes = Array.Empty<DemucsAccelerationModeOption>();

        var preferences = _userPreferencesService.Load();
        ReloadLocalization(
            preferences.PreferredSplitAudioOutputFormatExtension,
            preferences.PreferredSplitAudioAccelerationMode);
        OutputDirectory = NormalizeOutputDirectory(preferences.PreferredSplitAudioOutputDirectory);
    }

    public IReadOnlyList<OutputFormatOption> AvailableOutputFormats { get; private set; }

    public IReadOnlyList<DemucsAccelerationModeOption> AvailableAccelerationModes { get; private set; }

    public OutputFormatOption SelectedOutputFormat { get; private set; } = null!;

    public DemucsAccelerationModeOption SelectedAccelerationMode { get; private set; } = null!;

    public string OutputDirectory { get; private set; } = string.Empty;

    public bool HasCustomOutputDirectory => !string.IsNullOrWhiteSpace(OutputDirectory);

    public void ReloadLocalization(
        string? preferredOutputFormatExtension = null,
        DemucsAccelerationMode? preferredAccelerationMode = null)
    {
        var currentOutputExtension = preferredOutputFormatExtension ?? SelectedOutputFormat?.Extension;
        var currentAccelerationMode = preferredAccelerationMode ?? SelectedAccelerationMode?.Mode;

        AvailableOutputFormats = _configuration.SupportedAudioOutputFormats
            .Select(option => option.Localize(_localizationService))
            .ToArray();
        AvailableAccelerationModes = _configuration.SupportedSplitAudioAccelerationModes
            .Select(option => option.Localize(_localizationService))
            .ToArray();

        SelectedOutputFormat = ResolvePreferredOutputFormat(currentOutputExtension);
        SelectedAccelerationMode = ResolvePreferredAccelerationMode(currentAccelerationMode);
    }

    public bool TrySetSelectedOutputFormat(OutputFormatOption value)
    {
        ArgumentNullException.ThrowIfNull(value);

        if (string.Equals(SelectedOutputFormat.Extension, value.Extension, StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        SelectedOutputFormat = value;
        PersistPreferences();
        return true;
    }

    public bool TrySetSelectedAccelerationMode(DemucsAccelerationModeOption value)
    {
        ArgumentNullException.ThrowIfNull(value);

        if (SelectedAccelerationMode.Mode == value.Mode)
        {
            return false;
        }

        SelectedAccelerationMode = value;
        PersistPreferences();
        return true;
    }

    public bool TrySetOutputDirectory(string? outputDirectory)
    {
        var normalizedDirectory = NormalizeOutputDirectory(outputDirectory);
        if (string.Equals(OutputDirectory, normalizedDirectory, StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        OutputDirectory = normalizedDirectory;
        PersistPreferences();
        return true;
    }

    public string GetAccelerationModeStatusMessage() =>
        SelectedAccelerationMode.Mode == DemucsAccelerationMode.GpuPreferred
            ? GetLocalizedText(
                "splitAudio.status.acceleration.gpuPreferred",
                "已切换为 GPU 优先模式，将按独显 -> 核显 -> CPU 的顺序尝试拆音。")
            : GetLocalizedText(
                "splitAudio.status.acceleration.cpu",
                "已切换为 CPU 兼容模式，本次拆音将固定使用 CPU。");

    private void PersistPreferences()
    {
        _userPreferencesService.Update(existingPreferences => existingPreferences with
        {
            PreferredSplitAudioOutputFormatExtension = SelectedOutputFormat.Extension,
            PreferredSplitAudioOutputDirectory = HasCustomOutputDirectory ? OutputDirectory : null,
            PreferredSplitAudioAccelerationMode = SelectedAccelerationMode.Mode
        });
    }

    private OutputFormatOption ResolvePreferredOutputFormat(string? preferredExtension)
    {
        if (!string.IsNullOrWhiteSpace(preferredExtension))
        {
            var preferredFormat = AvailableOutputFormats.FirstOrDefault(format =>
                string.Equals(format.Extension, preferredExtension, StringComparison.OrdinalIgnoreCase));
            if (preferredFormat is not null)
            {
                return preferredFormat;
            }
        }

        return AvailableOutputFormats.First();
    }

    private DemucsAccelerationModeOption ResolvePreferredAccelerationMode(DemucsAccelerationMode? preferredMode)
    {
        if (preferredMode is DemucsAccelerationMode accelerationMode)
        {
            var preferredOption = AvailableAccelerationModes.FirstOrDefault(option => option.Mode == accelerationMode);
            if (preferredOption is not null)
            {
                return preferredOption;
            }
        }

        return AvailableAccelerationModes.First();
    }

    private string NormalizeOutputDirectory(string? outputDirectory)
    {
        if (MediaPathResolver.TryNormalizeOutputDirectory(outputDirectory, out var normalizedDirectory))
        {
            return normalizedDirectory;
        }

        _logger.Log(LogLevel.Warning, "检测到无效的拆音输出目录配置，已回退为原文件夹输出。");
        return string.Empty;
    }

    private string GetLocalizedText(string key, string fallback) =>
        _localizationService.GetString(key, fallback);
}
