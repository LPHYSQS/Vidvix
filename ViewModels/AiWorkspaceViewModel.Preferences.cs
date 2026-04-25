using System;
using System.Linq;
using Vidvix.Core.Models;

namespace Vidvix.ViewModels;

public sealed partial class AiWorkspaceViewModel
{
    private void InitializePreferenceState(UserPreferences preferences)
    {
        ArgumentNullException.ThrowIfNull(preferences);

        RememberOutputFormatSelection(AiWorkspaceMode.Interpolation, preferences.PreferredAiInterpolationOutputFormatExtension);
        RememberOutputFormatSelection(AiWorkspaceMode.Enhancement, preferences.PreferredAiEnhancementOutputFormatExtension);
        ApplyPreferredEnhancementDevicePreference(preferences.PreferredAiEnhancementDevicePreference);
        ModeState.SelectedMode = ResolvePreferredAiWorkspaceMode(preferences.PreferredAiWorkspaceMode);
        ApplyCurrentModeOutputFormatPreference();
    }

    private void ApplyCurrentModeOutputFormatPreference()
    {
        OutputSettings.TrySelectOutputFormatByExtension(GetRememberedOutputFormatExtension(ModeState.SelectedMode));
    }

    private void RememberCurrentModeOutputFormatSelection() =>
        RememberOutputFormatSelection(ModeState.SelectedMode, OutputSettings.SelectedOutputFormat.Extension);

    private void PersistAiPreferences()
    {
        if (_userPreferencesService is null)
        {
            return;
        }

        _userPreferencesService.Update(existingPreferences => existingPreferences with
        {
            PreferredAiWorkspaceMode = ModeState.SelectedMode.ToString(),
            PreferredAiInterpolationOutputFormatExtension = GetRememberedOutputFormatExtension(AiWorkspaceMode.Interpolation),
            PreferredAiEnhancementOutputFormatExtension = GetRememberedOutputFormatExtension(AiWorkspaceMode.Enhancement),
            PreferredAiEnhancementDevicePreference = EnhancementSettings.SelectedDevicePreference.ToString()
        });
    }

    private static AiWorkspaceMode ResolvePreferredAiWorkspaceMode(string? preferredMode) =>
        Enum.TryParse<AiWorkspaceMode>(preferredMode, ignoreCase: true, out var resolvedMode)
            ? resolvedMode
            : AiWorkspaceMode.Interpolation;

    private static AiEnhancementDevicePreference ResolvePreferredEnhancementDevicePreference(string? preferredDevicePreference) =>
        Enum.TryParse<AiEnhancementDevicePreference>(preferredDevicePreference, ignoreCase: true, out var resolvedPreference)
            ? resolvedPreference
            : AiEnhancementDevicePreference.Automatic;

    private string? GetRememberedOutputFormatExtension(AiWorkspaceMode mode) =>
        _preferredOutputFormatExtensionsByMode.TryGetValue(mode, out var extension)
            ? extension
            : null;

    private void ApplyPreferredEnhancementDevicePreference(string? preferredDevicePreference)
    {
        var resolvedPreference = ResolvePreferredEnhancementDevicePreference(preferredDevicePreference);
        EnhancementSettings.SelectedDeviceOption =
            EnhancementSettings.AvailableDeviceOptions.FirstOrDefault(option => option.DevicePreference == resolvedPreference)
            ?? EnhancementSettings.AvailableDeviceOptions[0];
    }

    private void RememberOutputFormatSelection(AiWorkspaceMode mode, string? extension)
    {
        if (string.IsNullOrWhiteSpace(extension))
        {
            _preferredOutputFormatExtensionsByMode.Remove(mode);
            return;
        }

        _preferredOutputFormatExtensionsByMode[mode] = extension.StartsWith(".", StringComparison.Ordinal)
            ? extension
            : $".{extension}";
    }
}
