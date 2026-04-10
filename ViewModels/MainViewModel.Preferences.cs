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

        if (userPreferences.PreferredProcessingMode is ProcessingMode preferredMode &&
            !string.IsNullOrWhiteSpace(userPreferences.PreferredOutputFormatExtension) &&
            string.IsNullOrWhiteSpace(GetRememberedOutputFormatExtension(preferredMode)))
        {
            RememberOutputFormatSelection(preferredMode, userPreferences.PreferredOutputFormatExtension);
        }
    }

    private IReadOnlyList<OutputFormatOption> GetOutputFormatsForMode(ProcessingMode processingMode) =>
        processingMode == ProcessingMode.AudioTrackExtract
            ? _configuration.SupportedAudioOutputFormats
            : _configuration.SupportedVideoOutputFormats;

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
            var plannedOutputPath = CreateUniqueOutputPath(CreateOutputPath(item.InputPath), usedOutputPaths);
            item.UpdatePlannedOutputPath(plannedOutputPath);
        }
    }

    private string CreateOutputPath(string inputPath) => SelectedProcessingMode.Mode switch
    {
        _ when IsAudioWorkspace => MediaPathResolver.CreateAudioConversionOutputPath(inputPath, SelectedOutputFormat.Extension, GetEffectiveOutputDirectory()),
        ProcessingMode.VideoConvert => MediaPathResolver.CreateVideoConversionOutputPath(inputPath, SelectedOutputFormat.Extension, GetEffectiveOutputDirectory()),
        ProcessingMode.VideoTrackExtract => MediaPathResolver.CreateVideoTrackOutputPath(inputPath, SelectedOutputFormat.Extension, GetEffectiveOutputDirectory()),
        ProcessingMode.AudioTrackExtract => MediaPathResolver.CreateAudioTrackOutputPath(inputPath, SelectedOutputFormat.Extension, GetEffectiveOutputDirectory()),
        _ => throw new InvalidOperationException("不支持的处理模式。")
    };

    private string? GetEffectiveOutputDirectory() =>
        HasCustomOutputDirectory
            ? OutputDirectory
            : null;

    private string NormalizeOutputDirectory(string? outputDirectory)
    {
        if (string.IsNullOrWhiteSpace(outputDirectory))
        {
            return string.Empty;
        }

        try
        {
            return Path.GetFullPath(outputDirectory.Trim());
        }
        catch (Exception exception) when (
            exception is ArgumentException or
            NotSupportedException or
            PathTooLongException)
        {
            _logger.Log(LogLevel.Warning, "检测到无效的输出目录配置，已回退为原文件夹输出。", exception);
            return string.Empty;
        }
    }

    private void EnsureOutputDirectoryExists()
    {
        if (!HasCustomOutputDirectory)
        {
            return;
        }

        Directory.CreateDirectory(OutputDirectory);
    }

    private static string CreateUniqueOutputPath(string outputPath, ISet<string> usedOutputPaths)
    {
        var directory = Path.GetDirectoryName(outputPath)
            ?? throw new InvalidOperationException("输出路径缺少有效目录。");
        var fileNameWithoutExtension = Path.GetFileNameWithoutExtension(outputPath);
        var extension = Path.GetExtension(outputPath);
        var candidatePath = outputPath;
        var suffixIndex = 2;

        while (usedOutputPaths.Contains(candidatePath) || File.Exists(candidatePath) || Directory.Exists(candidatePath))
        {
            candidatePath = Path.Combine(directory, $"{fileNameWithoutExtension}_{suffixIndex}{extension}");
            suffixIndex++;
        }

        usedOutputPaths.Add(candidatePath);
        return candidatePath;
    }
}
