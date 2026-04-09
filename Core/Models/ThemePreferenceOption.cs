namespace Vidvix.Core.Models;

public sealed class ThemePreferenceOption
{
    public ThemePreferenceOption(ThemePreference preference, string displayName, string description)
    {
        Preference = preference;
        DisplayName = displayName;
        Description = description;
    }

    public ThemePreference Preference { get; }

    public string DisplayName { get; }

    public string Description { get; }
}