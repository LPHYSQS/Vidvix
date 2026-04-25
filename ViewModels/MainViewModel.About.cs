using System.Windows.Input;
using Vidvix.Utils;

namespace Vidvix.ViewModels;

public sealed partial class MainViewModel
{
    private readonly AsyncRelayCommand _switchToAboutWorkspaceCommand;

    public ICommand SwitchToAboutWorkspaceCommand => _switchToAboutWorkspaceCommand;
}
