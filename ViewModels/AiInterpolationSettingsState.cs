using System;
using System.Collections.Generic;
using System.Linq;
using Vidvix.Core.Models;
using Vidvix.Utils;

namespace Vidvix.ViewModels;

public sealed class AiInterpolationSettingsState : ObservableObject
{
    private IReadOnlyList<AiInterpolationScaleFactorOption> _availableScaleFactors;
    private AiInterpolationScaleFactorOption _selectedScaleFactor;
    private IReadOnlyList<AiInterpolationDeviceOption> _availableDeviceOptions;
    private AiInterpolationDeviceOption _selectedDeviceOption;
    private bool _enableUhdMode;

    public AiInterpolationSettingsState(
        IReadOnlyList<AiInterpolationScaleFactorOption> availableScaleFactors,
        IReadOnlyList<AiInterpolationDeviceOption> availableDeviceOptions)
    {
        ArgumentNullException.ThrowIfNull(availableScaleFactors);
        ArgumentNullException.ThrowIfNull(availableDeviceOptions);
        if (availableScaleFactors.Count == 0)
        {
            throw new ArgumentException("At least one interpolation scale factor is required.", nameof(availableScaleFactors));
        }

        if (availableDeviceOptions.Count == 0)
        {
            throw new ArgumentException("At least one interpolation device option is required.", nameof(availableDeviceOptions));
        }

        _availableScaleFactors = availableScaleFactors.ToArray();
        _selectedScaleFactor = _availableScaleFactors[0];
        _availableDeviceOptions = availableDeviceOptions.ToArray();
        _selectedDeviceOption = _availableDeviceOptions[0];
    }

    public IReadOnlyList<AiInterpolationScaleFactorOption> AvailableScaleFactors
    {
        get => _availableScaleFactors;
        private set => SetProperty(ref _availableScaleFactors, value);
    }

    public AiInterpolationScaleFactorOption SelectedScaleFactor
    {
        get => _selectedScaleFactor;
        set
        {
            ArgumentNullException.ThrowIfNull(value);
            if (!SetProperty(ref _selectedScaleFactor, value))
            {
                return;
            }

            OnPropertyChanged(nameof(SelectedScaleFactorValue));
        }
    }

    public IReadOnlyList<AiInterpolationDeviceOption> AvailableDeviceOptions
    {
        get => _availableDeviceOptions;
        private set => SetProperty(ref _availableDeviceOptions, value);
    }

    public AiInterpolationDeviceOption SelectedDeviceOption
    {
        get => _selectedDeviceOption;
        set
        {
            ArgumentNullException.ThrowIfNull(value);
            if (!SetProperty(ref _selectedDeviceOption, value))
            {
                return;
            }

            OnPropertyChanged(nameof(SelectedDevicePreference));
        }
    }

    public bool EnableUhdMode
    {
        get => _enableUhdMode;
        set => SetProperty(ref _enableUhdMode, value);
    }

    public AiInterpolationScaleFactor SelectedScaleFactorValue => SelectedScaleFactor.ScaleFactor;

    public AiInterpolationDevicePreference SelectedDevicePreference => SelectedDeviceOption.DevicePreference;

    public void ReloadLocalizedOptions(
        IReadOnlyList<AiInterpolationScaleFactorOption> availableScaleFactors,
        IReadOnlyList<AiInterpolationDeviceOption> availableDeviceOptions)
    {
        ArgumentNullException.ThrowIfNull(availableScaleFactors);
        ArgumentNullException.ThrowIfNull(availableDeviceOptions);

        var selectedScaleFactor = SelectedScaleFactor.ScaleFactor;
        AvailableScaleFactors = availableScaleFactors.ToArray();
        _selectedScaleFactor = AvailableScaleFactors.FirstOrDefault(option => option.ScaleFactor == selectedScaleFactor)
            ?? AvailableScaleFactors[0];
        OnPropertyChanged(nameof(SelectedScaleFactor));
        OnPropertyChanged(nameof(SelectedScaleFactorValue));

        var selectedDevicePreference = SelectedDeviceOption.DevicePreference;
        AvailableDeviceOptions = availableDeviceOptions.ToArray();
        _selectedDeviceOption = AvailableDeviceOptions.FirstOrDefault(option => option.DevicePreference == selectedDevicePreference)
            ?? AvailableDeviceOptions[0];
        OnPropertyChanged(nameof(SelectedDeviceOption));
        OnPropertyChanged(nameof(SelectedDevicePreference));
    }
}

public sealed class AiInterpolationScaleFactorOption
{
    public AiInterpolationScaleFactorOption(
        AiInterpolationScaleFactor scaleFactor,
        string displayName,
        string description)
    {
        ScaleFactor = scaleFactor;
        DisplayName = displayName ?? string.Empty;
        Description = description ?? string.Empty;
    }

    public AiInterpolationScaleFactor ScaleFactor { get; }

    public string DisplayName { get; }

    public string Description { get; }
}

public sealed class AiInterpolationDeviceOption
{
    public AiInterpolationDeviceOption(
        AiInterpolationDevicePreference devicePreference,
        string displayName,
        string description)
    {
        DevicePreference = devicePreference;
        DisplayName = displayName ?? string.Empty;
        Description = description ?? string.Empty;
    }

    public AiInterpolationDevicePreference DevicePreference { get; }

    public string DisplayName { get; }

    public string Description { get; }
}
