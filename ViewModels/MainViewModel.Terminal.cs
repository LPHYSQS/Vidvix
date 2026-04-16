using System.Windows.Input;
using Vidvix.Utils;

namespace Vidvix.ViewModels;

public sealed partial class MainViewModel
{
    private readonly AsyncRelayCommand _switchToTerminalWorkspaceCommand;

    public TerminalWorkspaceViewModel TerminalWorkspace { get; }

    public ICommand SwitchToTerminalWorkspaceCommand => _switchToTerminalWorkspaceCommand;
}
