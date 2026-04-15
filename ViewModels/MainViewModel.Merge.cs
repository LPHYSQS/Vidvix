using System.Windows.Input;
using Vidvix.Utils;

namespace Vidvix.ViewModels;

public sealed partial class MainViewModel
{
    private readonly AsyncRelayCommand _switchToMergeWorkspaceCommand;

    public MergeViewModel MergeWorkspace { get; }

    public ICommand SwitchToMergeWorkspaceCommand => _switchToMergeWorkspaceCommand;
}
