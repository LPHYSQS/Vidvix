using System;

namespace Vidvix.Core.Models;

public sealed class DesktopShortcutOperationResult
{
    public DesktopShortcutOperationResult(string shortcutPath, bool createdNewShortcut)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(shortcutPath);

        ShortcutPath = shortcutPath;
        CreatedNewShortcut = createdNewShortcut;
    }

    public string ShortcutPath { get; }

    public bool CreatedNewShortcut { get; }
}
