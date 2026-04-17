using System.Windows.Input;
using Vidvix.Utils;

namespace Vidvix.ViewModels;

public sealed partial class MainViewModel
{
    private readonly AsyncRelayCommand _switchToSplitAudioWorkspaceCommand;

    public ICommand SwitchToSplitAudioWorkspaceCommand => _switchToSplitAudioWorkspaceCommand;
}
