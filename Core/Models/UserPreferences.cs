namespace Vidvix.Core.Models;

public sealed class UserPreferences
{
    public ThemePreference ThemePreference { get; init; } = ThemePreference.UseSystem;

    public bool RevealOutputFileAfterProcessing { get; init; } = true;
}