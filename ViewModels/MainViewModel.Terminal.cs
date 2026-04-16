using System.Windows.Input;
using Vidvix.Utils;

namespace Vidvix.ViewModels;

public sealed partial class MainViewModel
{
    private readonly AsyncRelayCommand _switchToTerminalWorkspaceCommand;

    public ICommand SwitchToTerminalWorkspaceCommand => _switchToTerminalWorkspaceCommand;
}
