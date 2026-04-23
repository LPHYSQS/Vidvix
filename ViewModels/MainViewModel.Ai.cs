using System.Windows.Input;
using Vidvix.Utils;

namespace Vidvix.ViewModels;

public sealed partial class MainViewModel
{
    private readonly AsyncRelayCommand _switchToAiWorkspaceCommand;

    public AiWorkspaceViewModel AiWorkspace { get; }

    public ICommand SwitchToAiWorkspaceCommand => _switchToAiWorkspaceCommand;
}
