using Vidvix.Core.Models;

namespace Vidvix.Core.Interfaces;

public interface IDesktopShortcutService
{
    DesktopShortcutOperationResult EnsureDesktopShortcut();
}
