using Vidvix.Utils;

namespace Vidvix.ViewModels;

public enum AiWorkspaceMode
{
    Interpolation = 0,
    Enhancement = 1
}

public sealed class AiModeState : ObservableObject
{
    private AiWorkspaceMode _selectedMode = AiWorkspaceMode.Interpolation;

    public AiWorkspaceMode SelectedMode
    {
        get => _selectedMode;
        set
        {
            if (!SetProperty(ref _selectedMode, value))
            {
                return;
            }

            OnPropertyChanged(nameof(IsInterpolationSelected));
            OnPropertyChanged(nameof(IsEnhancementSelected));
            OnPropertyChanged(nameof(OutputFileNameSuffix));
        }
    }

    public bool IsInterpolationSelected
    {
        get => SelectedMode == AiWorkspaceMode.Interpolation;
        set
        {
            if (value)
            {
                SelectedMode = AiWorkspaceMode.Interpolation;
            }
        }
    }

    public bool IsEnhancementSelected
    {
        get => SelectedMode == AiWorkspaceMode.Enhancement;
        set
        {
            if (value)
            {
                SelectedMode = AiWorkspaceMode.Enhancement;
            }
        }
    }

    public string OutputFileNameSuffix =>
        SelectedMode == AiWorkspaceMode.Interpolation
            ? "_ai_interpolation"
            : "_ai_enhancement";
}
