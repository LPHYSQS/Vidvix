using System;

namespace Vidvix.Core.Interfaces;

public interface ISystemTrayService : IDisposable
{
    void Initialize(Action showWindowAction, Action exitApplicationAction);

    void SetEnabled(bool enabled);
}
