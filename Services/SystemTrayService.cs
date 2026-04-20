using System;
using System.Drawing;
using System.IO;
using Forms = System.Windows.Forms;
using Vidvix.Core.Interfaces;
using Vidvix.Core.Models;
using Vidvix.Utils;

namespace Vidvix.Services;

public sealed class SystemTrayService : ISystemTrayService
{
    private readonly IDispatcherService _dispatcherService;
    private readonly ILogger _logger;
    private readonly string _tooltipText;
    private readonly string _iconPath;
    private bool _isEnabled;
    private bool _isDisposed;
    private Icon? _trayIconImage;
    private Forms.NotifyIcon? _notifyIcon;
    private Forms.ContextMenuStrip? _contextMenu;
    private Action? _showWindowAction;
    private Action? _exitApplicationAction;

    public SystemTrayService(
        ApplicationConfiguration configuration,
        IDispatcherService dispatcherService,
        ILogger logger)
    {
        ArgumentNullException.ThrowIfNull(configuration);

        _dispatcherService = dispatcherService ?? throw new ArgumentNullException(nameof(dispatcherService));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _tooltipText = string.IsNullOrWhiteSpace(configuration.ApplicationTitle)
            ? "Vidvix"
            : configuration.ApplicationTitle;
        _iconPath = Path.GetFullPath(Path.Combine(ApplicationPaths.ExecutableDirectoryPath, configuration.ApplicationIconRelativePath));
    }

    public void Initialize(Action showWindowAction, Action exitApplicationAction)
    {
        ObjectDisposedException.ThrowIf(_isDisposed, this);
        _showWindowAction = showWindowAction ?? throw new ArgumentNullException(nameof(showWindowAction));
        _exitApplicationAction = exitApplicationAction ?? throw new ArgumentNullException(nameof(exitApplicationAction));
    }

    public void SetEnabled(bool enabled)
    {
        ObjectDisposedException.ThrowIf(_isDisposed, this);
        _isEnabled = enabled;

        if (!enabled)
        {
            if (_notifyIcon is not null)
            {
                _notifyIcon.Visible = false;
            }

            return;
        }

        EnsureNotifyIcon();
        if (_notifyIcon is not null)
        {
            _notifyIcon.Visible = true;
        }
    }

    public void Dispose()
    {
        if (_isDisposed)
        {
            return;
        }

        _isDisposed = true;

        if (_notifyIcon is not null)
        {
            _notifyIcon.Visible = false;
            _notifyIcon.DoubleClick -= OnNotifyIconDoubleClick;
            _notifyIcon.Dispose();
            _notifyIcon = null;
        }

        if (_contextMenu is not null)
        {
            _contextMenu.Dispose();
            _contextMenu = null;
        }

        _trayIconImage?.Dispose();
        _trayIconImage = null;
    }

    private void EnsureNotifyIcon()
    {
        if (_notifyIcon is not null)
        {
            return;
        }

        if (_showWindowAction is null || _exitApplicationAction is null)
        {
            throw new InvalidOperationException("系统托盘服务尚未完成初始化。");
        }

        _trayIconImage = ResolveTrayIcon();
        _contextMenu = new Forms.ContextMenuStrip();
        _contextMenu.Items.Add(CreateMenuItem("显示窗口", _showWindowAction));
        _contextMenu.Items.Add(CreateMenuItem("退出", _exitApplicationAction));

        _notifyIcon = new Forms.NotifyIcon
        {
            Icon = _trayIconImage,
            Text = TrimTooltipText(_tooltipText),
            Visible = _isEnabled,
            ContextMenuStrip = _contextMenu
        };
        _notifyIcon.DoubleClick += OnNotifyIconDoubleClick;
    }

    private Forms.ToolStripMenuItem CreateMenuItem(string text, Action action)
    {
        var menuItem = new Forms.ToolStripMenuItem(text);
        menuItem.Click += (_, _) => InvokeOnUi(action);
        return menuItem;
    }

    private void OnNotifyIconDoubleClick(object? sender, EventArgs e)
    {
        if (_showWindowAction is not null)
        {
            InvokeOnUi(_showWindowAction);
        }
    }

    private void InvokeOnUi(Action action)
    {
        if (_dispatcherService.HasThreadAccess)
        {
            action();
            return;
        }

        if (!_dispatcherService.TryEnqueue(action))
        {
            _logger.Log(LogLevel.Warning, "系统托盘操作派发到界面线程失败。");
        }
    }

    private Icon ResolveTrayIcon()
    {
        try
        {
            if (File.Exists(_iconPath))
            {
                return new Icon(_iconPath);
            }

            var executablePath = ApplicationPaths.ExecutablePath;
            if (File.Exists(executablePath))
            {
                using var associatedIcon = Icon.ExtractAssociatedIcon(executablePath);
                if (associatedIcon is not null)
                {
                    return (Icon)associatedIcon.Clone();
                }
            }
        }
        catch (Exception exception)
        {
            _logger.Log(LogLevel.Warning, "加载系统托盘图标失败，将回退为默认应用图标。", exception);
        }

        return (Icon)SystemIcons.Application.Clone();
    }

    private static string TrimTooltipText(string value) =>
        value.Length <= 63
            ? value
            : value[..63];
}
