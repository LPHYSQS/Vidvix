// 功能：主工作区偏好与输出规划（集中管理格式偏好、输出目录和默认导出路径）
// 模块：视频转换模块 / 音频转换模块
// 说明：可复用，负责状态规划与持久化，不直接执行业务逻辑。
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Vidvix.Core.Models;
using Vidvix.Utils;

namespace Vidvix.ViewModels;

public sealed partial class MainViewModel
{
    // 偏好持久化和输出规划逻辑集中在这里，降低跨模块复制字段的风险。

    private void PersistUserPreferences()
    {
        _userPreferencesService.Update(existingPreferences => existingPreferences with
        {
            PreferredWorkspaceKind = _selectedWorkspaceKind,
            PreferredProcessingMode = _selectedProcessingMode?.Mode,
            PreferredOutputFormatExtension = _selectedOutputFormat?.Extension,
            PreferredVideoConvertOutputFormatExtension = GetRememberedOutputFormatExtension(ProcessingMode.VideoConvert),
            PreferredVideoTrackExtractOutputFormatExtension = GetRememberedOutputFormatExtension(ProcessingMode.VideoTrackExtract),
            PreferredAudioTrackExtractOutputFormatExtension = GetRememberedOutputFormatExtension(ProcessingMode.AudioTrackExtract),
            PreferredSubtitleTrackExtractOutputFormatExtension = GetRememberedOutputFormatExtension(ProcessingMode.SubtitleTrackExtract),
            PreferredOutputDirectory = HasCustomOutputDirectory ? OutputDirectory : null,
            ThemePreference = SelectedThemeOption.Preference,
            RevealOutputFileAfterProcessing = RevealOutputFileAfterProcessing,
            PreferredTranscodingMode = SelectedTranscodingModeOption.Mode,
            EnableGpuAccelerationForTranscoding = EnableGpuAccelerationForTranscoding
        });
    }

    private void ReloadOutputFormats(string? preferredOutputFormatExtension = null)
    {
        var preferenceMode = GetCurrentOutputFormatPreferenceMode();
        var formats = GetOutputFormatsForMode(preferenceMode);
        var desiredExtension = preferredOutputFormatExtension
            ?? GetRememberedOutputFormatExtension(preferenceMode)
            ?? _selectedOutputFormat?.Extension;

        AvailableOutputFormats = formats;

        var preferredFormat = desiredExtension is null
            ? null
            : formats.FirstOrDefault(format => string.Equals(format.Extension, desiredExtension, StringComparison.OrdinalIgnoreCase));
        var resolvedFormat = preferredFormat ?? formats.FirstOrDefault();

        if (resolvedFormat is not null)
        {
            RememberOutputFormatSelection(preferenceMode, resolvedFormat.Extension);
        }

        if (!ReferenceEquals(_selectedOutputFormat, resolvedFormat))
        {
            _selectedOutputFormat = resolvedFormat;
            OnPropertyChanged(nameof(SelectedOutputFormat));
        }

        OnPropertyChanged(nameof(SelectedOutputFormatDescription));
    }

    private ProcessingModeOption ResolveProcessingMode(ProcessingMode? preferredProcessingMode)
    {
        if (preferredProcessingMode is ProcessingMode processingMode)
        {
            var matchingMode = ProcessingModes.FirstOrDefault(option => option.Mode == processingMode);
            if (matchingMode is not null)
            {
                return matchingMode;
            }
        }

        return ProcessingModes.First();
    }

    private TranscodingModeOption ResolveTranscodingMode(TranscodingMode preferredTranscodingMode)
    {
        var matchingMode = TranscodingOptions.FirstOrDefault(option => option.Mode == preferredTranscodingMode);
        return matchingMode ?? TranscodingOptions[0];
    }

    private void InitializePreferredOutputFormatSelections(UserPreferences userPreferences)
    {
        RememberOutputFormatSelection(ProcessingMode.VideoConvert, userPreferences.PreferredVideoConvertOutputFormatExtension);
        RememberOutputFormatSelection(ProcessingMode.VideoTrackExtract, userPreferences.PreferredVideoTrackExtractOutputFormatExtension);
        RememberOutputFormatSelection(ProcessingMode.AudioTrackExtract, userPreferences.PreferredAudioTrackExtractOutputFormatExtension);
        RememberOutputFormatSelection(ProcessingMode.SubtitleTrackExtract, userPreferences.PreferredSubtitleTrackExtractOutputFormatExtension);

        if (userPreferences.PreferredProcessingMode is ProcessingMode preferredMode &&
            !string.IsNullOrWhiteSpace(userPreferences.PreferredOutputFormatExtension) &&
            string.IsNullOrWhiteSpace(GetRememberedOutputFormatExtension(preferredMode)))
        {
            RememberOutputFormatSelection(preferredMode, userPreferences.PreferredOutputFormatExtension);
        }
    }

    private IReadOnlyList<OutputFormatOption> GetOutputFormatsForMode(ProcessingMode processingMode) =>
        processingMode switch
        {
            ProcessingMode.AudioTrackExtract => _configuration.SupportedAudioOutputFormats,
            ProcessingMode.SubtitleTrackExtract => _configuration.SupportedSubtitleOutputFormats,
            _ => _configuration.SupportedVideoOutputFormats
        };

    private OutputFormatOption ResolvePreferredOutputFormat(ProcessingMode processingMode)
    {
        var formats = GetOutputFormatsForMode(processingMode);
        var rememberedExtension = GetRememberedOutputFormatExtension(processingMode);
        var preferredFormat = rememberedExtension is null
            ? null
            : formats.FirstOrDefault(format => string.Equals(format.Extension, rememberedExtension, StringComparison.OrdinalIgnoreCase));

        return preferredFormat ?? formats.First();
    }

    private string? GetRememberedOutputFormatExtension(ProcessingMode processingMode) =>
        _preferredOutputFormatExtensionsByMode.TryGetValue(processingMode, out var extension)
            ? extension
            : null;

    private void RememberOutputFormatSelection(ProcessingMode processingMode, string? extension)
    {
        if (string.IsNullOrWhiteSpace(extension))
        {
            _preferredOutputFormatExtensionsByMode.Remove(processingMode);
            return;
        }

        _preferredOutputFormatExtensionsByMode[processingMode] = extension.StartsWith(".", StringComparison.Ordinal)
            ? extension
            : $".{extension}";
    }

    private void RecalculatePlannedOutputs()
    {
        if (AvailableOutputFormats.Count == 0)
        {
            return;
        }

        var usedOutputPaths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var item in ImportItems)
        {
            var plannedOutputPath = MediaPathResolver.CreateUniqueOutputPath(CreateOutputPath(item.InputPath), usedOutputPaths);
            item.UpdatePlannedOutputPath(plannedOutputPath);
        }
    }

    private string CreateOutputPath(string inputPath) => SelectedProcessingMode.Mode switch
    {
        _ when IsAudioWorkspace => MediaPathResolver.CreateAudioConversionOutputPath(inputPath, SelectedOutputFormat.Extension, GetEffectiveOutputDirectory()),
        ProcessingMode.VideoConvert => MediaPathResolver.CreateVideoConversionOutputPath(inputPath, SelectedOutputFormat.Extension, GetEffectiveOutputDirectory()),
        ProcessingMode.VideoTrackExtract => MediaPathResolver.CreateVideoTrackOutputPath(inputPath, SelectedOutputFormat.Extension, GetEffectiveOutputDirectory()),
        ProcessingMode.AudioTrackExtract => MediaPathResolver.CreateAudioTrackOutputPath(inputPath, SelectedOutputFormat.Extension, GetEffectiveOutputDirectory()),
        ProcessingMode.SubtitleTrackExtract => MediaPathResolver.CreateSubtitleTrackOutputPath(inputPath, SelectedOutputFormat.Extension, GetEffectiveOutputDirectory()),
        _ => throw new InvalidOperationException("不支持的处理模式。")
    };

    private string? GetEffectiveOutputDirectory() =>
        HasCustomOutputDirectory
            ? OutputDirectory
            : null;

    private string NormalizeOutputDirectory(string? outputDirectory)
    {
        if (MediaPathResolver.TryNormalizeOutputDirectory(outputDirectory, out var normalizedDirectory))
        {
            return normalizedDirectory;
        }

        _logger.Log(LogLevel.Warning, "检测到无效的输出目录配置，已回退为原文件夹输出。");
        return string.Empty;
    }

    private void EnsureOutputDirectoryExists()
    {
        if (!HasCustomOutputDirectory)
        {
            return;
        }

        Directory.CreateDirectory(OutputDirectory);
    }
}
