using System;
using System.Collections.Generic;
using System.Linq;
using Vidvix.Core.Models;
using Vidvix.Utils;

namespace Vidvix.ViewModels;

public sealed class AiOutputSettingsState : ObservableObject
{
    private IReadOnlyList<OutputFormatOption> _availableOutputFormats;
    private OutputFormatOption _selectedOutputFormat;
    private string _customOutputDirectory = string.Empty;
    private string _outputFileName = string.Empty;
    private string _defaultOutputDirectory = string.Empty;
    private string _suggestedOutputFileName = "ai_output";

    public AiOutputSettingsState(IReadOnlyList<OutputFormatOption> availableOutputFormats)
    {
        ArgumentNullException.ThrowIfNull(availableOutputFormats);
        if (availableOutputFormats.Count == 0)
        {
            throw new ArgumentException("At least one output format option is required.", nameof(availableOutputFormats));
        }

        _availableOutputFormats = availableOutputFormats.ToArray();
        _selectedOutputFormat = _availableOutputFormats[0];
    }

    public IReadOnlyList<OutputFormatOption> AvailableOutputFormats
    {
        get => _availableOutputFormats;
        private set => SetProperty(ref _availableOutputFormats, value);
    }

    public OutputFormatOption SelectedOutputFormat
    {
        get => _selectedOutputFormat;
        set
        {
            ArgumentNullException.ThrowIfNull(value);

            if (!SetProperty(ref _selectedOutputFormat, value))
            {
                return;
            }

            OnPropertyChanged(nameof(SelectedOutputFormatDescription));
            OnPropertyChanged(nameof(EffectiveOutputFileNameWithExtension));
        }
    }

    public string SelectedOutputFormatDescription => SelectedOutputFormat.Description;

    public string CustomOutputDirectory => _customOutputDirectory;

    public bool HasCustomOutputDirectory => !string.IsNullOrWhiteSpace(CustomOutputDirectory);

    public string DefaultOutputDirectory => _defaultOutputDirectory;

    public string EffectiveOutputDirectory =>
        HasCustomOutputDirectory
            ? CustomOutputDirectory
            : DefaultOutputDirectory;

    public string OutputDirectoryDisplayText => EffectiveOutputDirectory;

    public string OutputFileName
    {
        get => _outputFileName;
        set
        {
            var normalized = NormalizeOutputFileName(value);
            if (!SetProperty(ref _outputFileName, normalized))
            {
                return;
            }

            OnPropertyChanged(nameof(HasCustomOutputFileName));
            OnPropertyChanged(nameof(EffectiveOutputFileName));
            OnPropertyChanged(nameof(EffectiveOutputFileNameWithExtension));
        }
    }

    public bool HasCustomOutputFileName => !string.IsNullOrWhiteSpace(OutputFileName);

    public string SuggestedOutputFileName => _suggestedOutputFileName;

    public string EffectiveOutputFileName =>
        HasCustomOutputFileName
            ? OutputFileName
            : SuggestedOutputFileName;

    public string EffectiveOutputFileNameWithExtension =>
        $"{EffectiveOutputFileName}{SelectedOutputFormat.Extension}";

    public void ReloadAvailableOutputFormats(IReadOnlyList<OutputFormatOption> availableOutputFormats)
    {
        ArgumentNullException.ThrowIfNull(availableOutputFormats);
        if (availableOutputFormats.Count == 0)
        {
            throw new ArgumentException("At least one output format option is required.", nameof(availableOutputFormats));
        }

        var preferredExtension = SelectedOutputFormat.Extension;
        AvailableOutputFormats = availableOutputFormats.ToArray();
        _selectedOutputFormat = AvailableOutputFormats.FirstOrDefault(option =>
                                   string.Equals(option.Extension, preferredExtension, StringComparison.OrdinalIgnoreCase))
                               ?? AvailableOutputFormats[0];

        OnPropertyChanged(nameof(SelectedOutputFormat));
        OnPropertyChanged(nameof(SelectedOutputFormatDescription));
        OnPropertyChanged(nameof(EffectiveOutputFileNameWithExtension));
    }

    public void UpdateInputContext(string? defaultOutputDirectory, string suggestedOutputFileName)
    {
        var normalizedDefaultDirectory = NormalizeOutputDirectory(defaultOutputDirectory);
        var normalizedSuggestedFileName = NormalizeOutputFileName(suggestedOutputFileName);
        if (string.IsNullOrWhiteSpace(normalizedSuggestedFileName))
        {
            normalizedSuggestedFileName = "ai_output";
        }

        var directoryChanged = !string.Equals(
            _defaultOutputDirectory,
            normalizedDefaultDirectory,
            StringComparison.OrdinalIgnoreCase);
        var fileNameChanged = !string.Equals(
            _suggestedOutputFileName,
            normalizedSuggestedFileName,
            StringComparison.Ordinal);

        if (!directoryChanged && !fileNameChanged)
        {
            return;
        }

        _defaultOutputDirectory = normalizedDefaultDirectory;
        _suggestedOutputFileName = normalizedSuggestedFileName;

        if (directoryChanged)
        {
            OnPropertyChanged(nameof(DefaultOutputDirectory));
            OnPropertyChanged(nameof(EffectiveOutputDirectory));
            OnPropertyChanged(nameof(OutputDirectoryDisplayText));
        }

        if (fileNameChanged)
        {
            OnPropertyChanged(nameof(SuggestedOutputFileName));
            OnPropertyChanged(nameof(EffectiveOutputFileName));
            OnPropertyChanged(nameof(EffectiveOutputFileNameWithExtension));
        }
    }

    public bool TrySetCustomOutputDirectory(string? outputDirectory)
    {
        var normalizedDirectory = NormalizeOutputDirectory(outputDirectory);
        if (string.Equals(_customOutputDirectory, normalizedDirectory, StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        _customOutputDirectory = normalizedDirectory;
        OnPropertyChanged(nameof(CustomOutputDirectory));
        OnPropertyChanged(nameof(HasCustomOutputDirectory));
        OnPropertyChanged(nameof(EffectiveOutputDirectory));
        OnPropertyChanged(nameof(OutputDirectoryDisplayText));
        return true;
    }

    public bool ClearCustomOutputDirectory() => TrySetCustomOutputDirectory(string.Empty);

    private static string NormalizeOutputDirectory(string? outputDirectory)
    {
        if (MediaPathResolver.TryNormalizeOutputDirectory(outputDirectory, out var normalizedDirectory))
        {
            return normalizedDirectory;
        }

        return string.Empty;
    }

    private static string NormalizeOutputFileName(string? outputFileName)
    {
        if (string.IsNullOrWhiteSpace(outputFileName))
        {
            return string.Empty;
        }

        return MediaPathResolver.SanitizeOutputFileName(outputFileName);
    }
}
