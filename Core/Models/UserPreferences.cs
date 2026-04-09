namespace Vidvix.Core.Models;

public sealed class UserPreferences
{
    public ProcessingMode? PreferredProcessingMode { get; init; }

    public string? PreferredOutputFormatExtension { get; init; }

    public string? PreferredOutputDirectory { get; init; }

    public ThemePreference ThemePreference { get; init; } = ThemePreference.UseSystem;

    public bool RevealOutputFileAfterProcessing { get; init; } = true;

    public WindowPlacementPreference? MainWindowPlacement { get; init; }
}
