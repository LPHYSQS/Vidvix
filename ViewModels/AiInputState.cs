using Vidvix.Utils;

namespace Vidvix.ViewModels;

public sealed class AiInputState : ObservableObject
{
    private AiMaterialItemViewModel? _currentMaterial;

    public AiMaterialItemViewModel? CurrentMaterial => _currentMaterial;

    public bool HasCurrentMaterial => _currentMaterial is not null;

    public bool HasNoCurrentMaterial => !HasCurrentMaterial;

    public string CurrentInputPath => _currentMaterial?.InputPath ?? string.Empty;

    public string CurrentInputFileName => _currentMaterial?.InputFileName ?? string.Empty;

    public string CurrentInputFileNameWithoutExtension => _currentMaterial?.InputFileNameWithoutExtension ?? string.Empty;

    public string CurrentInputDirectory => _currentMaterial?.InputDirectory ?? string.Empty;

    public void SetCurrentMaterial(AiMaterialItemViewModel? material)
    {
        if (ReferenceEquals(_currentMaterial, material))
        {
            return;
        }

        _currentMaterial = material;
        OnPropertyChanged(nameof(CurrentMaterial));
        OnPropertyChanged(nameof(HasCurrentMaterial));
        OnPropertyChanged(nameof(HasNoCurrentMaterial));
        OnPropertyChanged(nameof(CurrentInputPath));
        OnPropertyChanged(nameof(CurrentInputFileName));
        OnPropertyChanged(nameof(CurrentInputFileNameWithoutExtension));
        OnPropertyChanged(nameof(CurrentInputDirectory));
    }
}
