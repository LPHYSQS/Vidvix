using System;
using Microsoft.UI.Xaml;

namespace Vidvix.Core.Interfaces;

public interface IWindowContext
{
    IntPtr Handle { get; }

    void SetWindow(Window window);
}
