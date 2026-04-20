using System;
using Vidvix.Core.Interfaces;

namespace Vidvix.Core.Models;

public sealed class OutputFormatOption
{
    public OutputFormatOption(
        string displayName,
        string extension,
        string description,
        string? displayNameKey = null,
        string? descriptionKey = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(displayName);
        ArgumentException.ThrowIfNullOrWhiteSpace(extension);
        ArgumentException.ThrowIfNullOrWhiteSpace(description);

        DisplayName = displayName;
        Extension = extension.StartsWith(".", StringComparison.Ordinal)
            ? extension
            : $".{extension}";
        Description = description;
        DisplayNameKey = displayNameKey ?? string.Empty;
        DescriptionKey = descriptionKey ?? string.Empty;
    }

    public string DisplayName { get; }

    public string Extension { get; }

    public string Description { get; }

    public string DisplayNameKey { get; }

    public string DescriptionKey { get; }

    public OutputFormatOption Localize(ILocalizationService localizationService)
    {
        ArgumentNullException.ThrowIfNull(localizationService);

        return new OutputFormatOption(
            LocalizeText(localizationService, DisplayNameKey, DisplayName),
            Extension,
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
