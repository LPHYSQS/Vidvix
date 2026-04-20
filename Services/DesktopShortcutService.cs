using System;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using Vidvix.Core.Interfaces;
using Vidvix.Core.Models;
using Vidvix.Utils;

namespace Vidvix.Services;

public sealed class DesktopShortcutService : IDesktopShortcutService
{
    private readonly ApplicationConfiguration _configuration;
    private readonly ILogger _logger;
    private readonly string _iconPath;

    public DesktopShortcutService(ApplicationConfiguration configuration, ILogger logger)
    {
        _configuration = configuration ?? throw new ArgumentNullException(nameof(configuration));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _iconPath = Path.GetFullPath(Path.Combine(ApplicationPaths.ExecutableDirectoryPath, configuration.ApplicationIconRelativePath));
    }

    public DesktopShortcutOperationResult EnsureDesktopShortcut()
    {
        var shortcutPath = ResolveShortcutPath();
        if (File.Exists(shortcutPath))
        {
            _logger.Log(LogLevel.Info, $"桌面快捷方式已存在：{shortcutPath}");
            return new DesktopShortcutOperationResult(shortcutPath, createdNewShortcut: false);
        }

        var executablePath = ResolveExecutablePath();
        var workingDirectory = Path.GetDirectoryName(executablePath);
        if (string.IsNullOrWhiteSpace(workingDirectory))
        {
            workingDirectory = ApplicationPaths.ExecutableDirectoryPath;
        }

        CreateShortcut(
            shortcutPath,
            executablePath,
            workingDirectory,
            File.Exists(_iconPath) ? _iconPath : executablePath,
            $"{_configuration.ApplicationTitle} 桌面快捷方式");

        _logger.Log(LogLevel.Info, $"已创建桌面快捷方式：{shortcutPath}");
        return new DesktopShortcutOperationResult(shortcutPath, createdNewShortcut: true);
    }

    private string ResolveShortcutPath()
    {
        var desktopDirectory = Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory);
        if (string.IsNullOrWhiteSpace(desktopDirectory))
        {
            throw new InvalidOperationException("无法定位桌面目录。");
        }

        return Path.Combine(desktopDirectory, $"{_configuration.ApplicationTitle}.lnk");
    }

    private static string ResolveExecutablePath()
    {
        var executablePath = ApplicationPaths.ExecutablePath;
        if (File.Exists(executablePath))
        {
            return executablePath;
        }

        throw new FileNotFoundException("未找到应用程序可执行文件，无法创建桌面快捷方式。", executablePath);
    }

    private static void CreateShortcut(
        string shortcutPath,
        string targetPath,
        string workingDirectory,
        string iconPath,
        string description)
    {
        object? shellLinkObject = null;

        try
        {
            shellLinkObject = new ShellLinkComObject();
            var shellLink = (IShellLinkW)shellLinkObject;
            shellLink.SetPath(targetPath);
            shellLink.SetWorkingDirectory(workingDirectory);
            shellLink.SetDescription(description);
            shellLink.SetIconLocation(iconPath, 0);
            shellLink.SetShowCmd(1);

            var persistFile = (IPersistFile)shellLinkObject;
            persistFile.Save(shortcutPath, true);
        }
        finally
        {
            if (shellLinkObject is not null && Marshal.IsComObject(shellLinkObject))
            {
                Marshal.FinalReleaseComObject(shellLinkObject);
            }
        }
    }

    [ComImport]
    [Guid("00021401-0000-0000-C000-000000000046")]
    private sealed class ShellLinkComObject;

    [ComImport]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    [Guid("000214F9-0000-0000-C000-000000000046")]
    private interface IShellLinkW
    {
        void GetPath([Out] StringBuilder pszFile, int cchMaxPath, nint pfd, uint fFlags);

        void GetIDList(out nint ppidl);

        void SetIDList(nint pidl);

        void GetDescription([Out] StringBuilder pszName, int cchMaxName);

        void SetDescription([MarshalAs(UnmanagedType.LPWStr)] string pszName);

        void GetWorkingDirectory([Out] StringBuilder pszDir, int cchMaxPath);

        void SetWorkingDirectory([MarshalAs(UnmanagedType.LPWStr)] string pszDir);

        void GetArguments([Out] StringBuilder pszArgs, int cchMaxPath);

        void SetArguments([MarshalAs(UnmanagedType.LPWStr)] string pszArgs);

        void GetHotkey(out short pwHotkey);

        void SetHotkey(short wHotkey);

        void GetShowCmd(out int piShowCmd);

        void SetShowCmd(int iShowCmd);

        void GetIconLocation([Out] StringBuilder pszIconPath, int cchIconPath, out int piIcon);

        void SetIconLocation([MarshalAs(UnmanagedType.LPWStr)] string pszIconPath, int iIcon);

        void SetRelativePath([MarshalAs(UnmanagedType.LPWStr)] string pszPathRel, uint dwReserved);

        void Resolve(nint hwnd, uint fFlags);

        void SetPath([MarshalAs(UnmanagedType.LPWStr)] string pszFile);
    }

    [ComImport]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    [Guid("0000010B-0000-0000-C000-000000000046")]
    private interface IPersistFile
    {
        void GetClassID(out Guid pClassID);

        void IsDirty();

        void Load([MarshalAs(UnmanagedType.LPWStr)] string pszFileName, uint dwMode);

        void Save([MarshalAs(UnmanagedType.LPWStr)] string pszFileName, [MarshalAs(UnmanagedType.Bool)] bool fRemember);

        void SaveCompleted([MarshalAs(UnmanagedType.LPWStr)] string pszFileName);

        void GetCurFile([MarshalAs(UnmanagedType.LPWStr)] out string ppszFileName);
    }
}
