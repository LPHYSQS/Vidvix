using System;
using System.Diagnostics;
using System.IO;
using Vidvix.Core.Interfaces;

namespace Vidvix.Services;

public sealed class FileRevealService : IFileRevealService
{
    public void RevealFile(string filePath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(filePath);

        var fullPath = Path.GetFullPath(filePath);

        if (File.Exists(fullPath))
        {
            StartExplorer($"/select,\"{fullPath}\"");
            return;
        }

        var directoryPath = Path.GetDirectoryName(fullPath);
        if (!string.IsNullOrWhiteSpace(directoryPath) && Directory.Exists(directoryPath))
        {
            StartExplorer($"\"{directoryPath}\"");
            return;
        }

        throw new FileNotFoundException("未找到可打开的输出文件或目录。", fullPath);
    }

    private static void StartExplorer(string arguments)
    {
        Process.Start(new ProcessStartInfo
        {
            FileName = "explorer.exe",
            Arguments = arguments,
            UseShellExecute = true
        });
    }
}