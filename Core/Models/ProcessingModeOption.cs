using System;
using Vidvix.Core.Interfaces;

namespace Vidvix.Core.Models;

public sealed class ProcessingModeOption
{
    public ProcessingModeOption(
        ProcessingMode mode,
        string displayName,
        string description,
        string? displayNameKey = null,
        string? descriptionKey = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(displayName);
        ArgumentException.ThrowIfNullOrWhiteSpace(description);

        Mode = mode;
        DisplayName = displayName;
        Description = description;
        DisplayNameKey = displayNameKey ?? string.Empty;
        DescriptionKey = descriptionKey ?? string.Empty;
    }

    public ProcessingMode Mode { get; }

    public string DisplayName { get; }

    public string Description { get; }

    public string DisplayNameKey { get; }

    public string DescriptionKey { get; }

    public ProcessingModeOption Localize(ILocalizationService localizationService)
    {
        ArgumentNullException.ThrowIfNull(localizationService);

        return new ProcessingModeOption(
            Mode,
            LocalizeText(localizationService, DisplayNameKey, DisplayName),
            LocalizeText(localizationService, DescriptionKey, Description),
            DisplayNameKey,
            DescriptionKey);
    }

    private static string LocalizeText(
        ILocalizationService localizationService,
        string key,
        string fallback) =>
        string.IsNullOrWhiteSpace(key)
            ? fallback
            : localizationService.GetString(key, fallback);
}
