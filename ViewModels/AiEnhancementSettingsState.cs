using System;
using System.Collections.Generic;
using System.Linq;
using Vidvix.Core.Models;
using Vidvix.Utils;

namespace Vidvix.ViewModels;

public sealed class AiEnhancementSettingsState : ObservableObject
{
    private IReadOnlyList<AiEnhancementModelTierOption> _availableModelTierOptions;
    private AiEnhancementModelTierOption _selectedModelTierOption;
    private IReadOnlyList<AiEnhancementScaleOption> _availableScaleOptions;
    private AiEnhancementScaleOption _selectedScaleOption;

    public AiEnhancementSettingsState(
        IReadOnlyList<AiEnhancementModelTierOption> availableModelTierOptions,
        IReadOnlyList<AiEnhancementScaleOption> availableScaleOptions)
    {
        ArgumentNullException.ThrowIfNull(availableModelTierOptions);
        ArgumentNullException.ThrowIfNull(availableScaleOptions);
        if (availableModelTierOptions.Count == 0)
        {
            throw new ArgumentException("At least one enhancement model tier option is required.", nameof(availableModelTierOptions));
        }

        if (availableScaleOptions.Count == 0)
        {
            throw new ArgumentException("At least one enhancement scale option is required.", nameof(availableScaleOptions));
        }

        _availableModelTierOptions = availableModelTierOptions.ToArray();
        _selectedModelTierOption = _availableModelTierOptions[0];
        _availableScaleOptions = availableScaleOptions.ToArray();
        _selectedScaleOption = _availableScaleOptions[0];
    }

    public IReadOnlyList<AiEnhancementModelTierOption> AvailableModelTierOptions
    {
        get => _availableModelTierOptions;
        private set => SetProperty(ref _availableModelTierOptions, value);
    }

    public AiEnhancementModelTierOption SelectedModelTierOption
    {
        get => _selectedModelTierOption;
        set
        {
            ArgumentNullException.ThrowIfNull(value);
            if (!SetProperty(ref _selectedModelTierOption, value))
            {
                return;
            }

            OnPropertyChanged(nameof(SelectedModelTier));
        }
    }

    public IReadOnlyList<AiEnhancementScaleOption> AvailableScaleOptions
    {
        get => _availableScaleOptions;
        private set => SetProperty(ref _availableScaleOptions, value);
    }

    public AiEnhancementScaleOption SelectedScaleOption
    {
        get => _selectedScaleOption;
        set
        {
            ArgumentNullException.ThrowIfNull(value);
            if (!SetProperty(ref _selectedScaleOption, value))
            {
                return;
            }

            OnPropertyChanged(nameof(SelectedScaleFactorValue));
        }
    }

    public AiEnhancementModelTier SelectedModelTier => SelectedModelTierOption.ModelTier;

    public int SelectedScaleFactorValue => SelectedScaleOption.ScaleFactor;

    public void ReloadLocalizedOptions(
        IReadOnlyList<AiEnhancementModelTierOption> availableModelTierOptions,
        IReadOnlyList<AiEnhancementScaleOption> availableScaleOptions)
    {
        ArgumentNullException.ThrowIfNull(availableModelTierOptions);
        ArgumentNullException.ThrowIfNull(availableScaleOptions);

        var selectedTier = SelectedModelTier;
        AvailableModelTierOptions = availableModelTierOptions.ToArray();
        _selectedModelTierOption = AvailableModelTierOptions.FirstOrDefault(option => option.ModelTier == selectedTier)
            ?? AvailableModelTierOptions[0];
        OnPropertyChanged(nameof(SelectedModelTierOption));
        OnPropertyChanged(nameof(SelectedModelTier));

        var selectedScaleFactor = SelectedScaleFactorValue;
        AvailableScaleOptions = availableScaleOptions.ToArray();
        _selectedScaleOption = AvailableScaleOptions.FirstOrDefault(option => option.ScaleFactor == selectedScaleFactor)
            ?? AvailableScaleOptions[0];
        OnPropertyChanged(nameof(SelectedScaleOption));
        OnPropertyChanged(nameof(SelectedScaleFactorValue));
    }
}

public sealed class AiEnhancementModelTierOption
{
    public AiEnhancementModelTierOption(
        AiEnhancementModelTier modelTier,
        string displayName,
        string description)
    {
        ModelTier = modelTier;
        DisplayName = displayName ?? string.Empty;
        Description = description ?? string.Empty;
    }

    public AiEnhancementModelTier ModelTier { get; }

    public string DisplayName { get; }

    public string Description { get; }
}

public sealed class AiEnhancementScaleOption
{
    public AiEnhancementScaleOption(
        int scaleFactor,
        string displayName,
        string description)
    {
        if (scaleFactor < 2 || scaleFactor > 16)
        {
            throw new ArgumentOutOfRangeException(nameof(scaleFactor));
        }

        ScaleFactor = scaleFactor;
        DisplayName = displayName ?? string.Empty;
        Description = description ?? string.Empty;
    }

    public int ScaleFactor { get; }

    public string DisplayName { get; }

    public string Description { get; }
}
