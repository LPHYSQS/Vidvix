using System;
using Microsoft.UI.Xaml;
using Vidvix.Core.Interfaces;
using WinRT.Interop;

namespace Vidvix.Utils;

public sealed class WindowContext : IWindowContext
{
    private IntPtr _handle;

    public IntPtr Handle =>
        _handle != IntPtr.Zero
            ? _handle
            : throw new InvalidOperationException("The window handle has not been initialized.");

    public void SetWindow(Window window)
    {
        ArgumentNullException.ThrowIfNull(window);
        _handle = WindowNative.GetWindowHandle(window);
    }
}
