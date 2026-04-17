using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Vidvix.Core.Interfaces;
using Vidvix.Core.Models;

namespace Vidvix.Services.FFmpeg;

public sealed class FFmpegTerminalService : IFFmpegTerminalService
{
    private static readonly IReadOnlyDictionary<string, string> AllowedExecutables =
        new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["ffmpeg"] = "ffmpeg",
            ["ffmpeg.exe"] = "ffmpeg",
            ["ffprobe"] = "ffprobe",
            ["ffprobe.exe"] = "ffprobe",
            ["ffplay"] = "ffplay",
            ["ffplay.exe"] = "ffplay"
        };

    private readonly ApplicationConfiguration _configuration;
    private readonly IFFmpegRuntimeService _runtimeService;
    private readonly ILogger _logger;

    public FFmpegTerminalService(
        ApplicationConfiguration configuration,
        IFFmpegRuntimeService runtimeService,
        ILogger logger)
    {
        _configuration = configuration;
        _runtimeService = runtimeService;
        _logger = logger;
    }

    public async Task<TerminalCommandExecutionResult> ExecuteAsync(
        string commandText,
        IProgress<string>? outputProgress = null,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(commandText))
        {
            return TerminalCommandExecutionResult.Failed(string.Empty, "请输入 FFmpeg、FFprobe 或 FFplay 命令。");
        }

        IReadOnlyList<string> tokens;

        try
        {
            tokens = SplitCommandLine(commandText);
        }
        catch (Exception exception)
        {
            _logger.Log(LogLevel.Warning, "解析终端命令失败。", exception);
            return TerminalCommandExecutionResult.Failed(commandText.Trim(), "命令格式无法解析，请检查引号和参数是否完整。");
        }

        if (tokens.Count == 0)
        {
            return TerminalCommandExecutionResult.Failed(commandText.Trim(), "请输入 FFmpeg、FFprobe 或 FFplay 命令。");
        }

        var requestedExecutable = tokens[0];
        if (!AllowedExecutables.TryGetValue(requestedExecutable, out var executableAlias))
        {
            return TerminalCommandExecutionResult.Failed(
                commandText.Trim(),
                "仅支持 ffmpeg、ffprobe、ffplay 三个内置命令，不能执行其他 CMD 或系统命令。");
        }

        var displayCommandText = BuildDisplayCommandText(executableAlias, tokens);

        try
        {
            var resolution = await _runtimeService.EnsureAvailableAsync(cancellationToken).ConfigureAwait(false);
            var executablePath = ResolveExecutablePath(executableAlias, resolution);

            if (string.IsNullOrWhiteSpace(executablePath) || !File.Exists(executablePath))
            {
                return TerminalCommandExecutionResult.Failed(
                    displayCommandText,
                    $"未找到内置 {executableAlias} 可执行文件。");
            }

            return await ExecuteProcessAsync(
                    executablePath,
                    displayCommandText,
                    tokens,
                    outputProgress,
                    cancellationToken)
                .ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            return TerminalCommandExecutionResult.Cancelled(displayCommandText);
        }
        catch (Exception exception)
        {
            _logger.Log(LogLevel.Error, $"执行终端命令失败：{displayCommandText}", exception);
            return TerminalCommandExecutionResult.Failed(displayCommandText, $"执行内置 {executableAlias} 时发生异常：{exception.Message}");
        }
    }

    private async Task<TerminalCommandExecutionResult> ExecuteProcessAsync(
        string executablePath,
        string displayCommandText,
        IReadOnlyList<string> tokens,
        IProgress<string>? outputProgress,
        CancellationToken cancellationToken)
    {
        using var process = new Process
        {
            StartInfo = CreateStartInfo(executablePath, tokens),
            EnableRaisingEvents = true
        };

        var standardOutputClosed = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var standardErrorClosed = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);

        process.OutputDataReceived += (_, eventArgs) =>
        {
            if (eventArgs.Data is null)
            {
                standardOutputClosed.TrySetResult();
                return;
            }

            outputProgress?.Report(eventArgs.Data);
        };

        process.ErrorDataReceived += (_, eventArgs) =>
        {
            if (eventArgs.Data is null)
            {
                standardErrorClosed.TrySetResult();
                return;
            }

            outputProgress?.Report(eventArgs.Data);
        };

        try
        {
            if (!process.Start())
            {
                return TerminalCommandExecutionResult.Failed(displayCommandText, "内置 FF 命令进程无法启动。");
            }

            process.BeginOutputReadLine();
            process.BeginErrorReadLine();

            using var cancellationRegistration = cancellationToken.Register(() => TryTerminateProcess(process));

            try
            {
                await process.WaitForExitAsync(cancellationToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                await AwaitOutputCompletionAsync(standardOutputClosed.Task, standardErrorClosed.Task).ConfigureAwait(false);
                return TerminalCommandExecutionResult.Cancelled(displayCommandText);
            }

            await AwaitOutputCompletionAsync(standardOutputClosed.Task, standardErrorClosed.Task).ConfigureAwait(false);

            return process.ExitCode == 0
                ? TerminalCommandExecutionResult.Success(displayCommandText, process.ExitCode)
                : TerminalCommandExecutionResult.Failed(
                    displayCommandText,
                    $"内置 FF 命令已退出，返回代码：{process.ExitCode}。",
                    process.ExitCode);
        }
        catch (Win32Exception exception)
        {
            _logger.Log(LogLevel.Error, $"启动终端命令失败：{displayCommandText}", exception);
            return TerminalCommandExecutionResult.Failed(displayCommandText, "内置 FF 命令不可用，请先确认软件运行时已准备完成。");
        }
    }

    private ProcessStartInfo CreateStartInfo(string executablePath, IReadOnlyList<string> tokens)
    {
        var startInfo = new ProcessStartInfo
        {
            FileName = executablePath,
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            StandardOutputEncoding = Encoding.UTF8,
            StandardErrorEncoding = Encoding.UTF8
        };

        for (var index = 1; index < tokens.Count; index++)
        {
            startInfo.ArgumentList.Add(tokens[index]);
        }

        return startInfo;
    }

    private string? ResolveExecutablePath(string executableAlias, FFmpegRuntimeResolution resolution)
    {
        var executableFileName = executableAlias switch
        {
            "ffprobe" => _configuration.FFprobeExecutableFileName,
            "ffplay" => _configuration.FFplayExecutableFileName,
            _ => _configuration.FFmpegExecutableFileName
        };

        if (string.Equals(executableAlias, "ffmpeg", StringComparison.OrdinalIgnoreCase) &&
            File.Exists(resolution.ExecutablePath))
        {
            return resolution.ExecutablePath;
        }

        foreach (var candidateRoot in EnumerateCandidateRoots(resolution))
        {
            var directPath = Path.Combine(candidateRoot, executableFileName);
            if (File.Exists(directPath))
            {
                return directPath;
            }

            if (!Directory.Exists(candidateRoot))
            {
                continue;
            }

            foreach (var filePath in Directory.EnumerateFiles(candidateRoot, executableFileName, SearchOption.AllDirectories))
            {
                return filePath;
            }
        }

        return null;
    }

    private IEnumerable<string> EnumerateCandidateRoots(FFmpegRuntimeResolution resolution)
    {
        var executableDirectory = Path.GetDirectoryName(resolution.ExecutablePath);
        if (!string.IsNullOrWhiteSpace(executableDirectory))
        {
            yield return executableDirectory;
        }

        yield return Path.Combine(AppContext.BaseDirectory, _configuration.RuntimeDirectoryName, _configuration.BundledRuntimeDirectoryName);
        yield return Path.Combine(
            AppContext.BaseDirectory,
            _configuration.RuntimeDirectoryName,
            _configuration.RuntimeVendorDirectoryName,
            _configuration.RuntimeCurrentVersionDirectoryName);
        yield return Path.Combine(
            resolution.StorageRootPath,
            _configuration.RuntimeCurrentVersionDirectoryName);
    }

    private static string BuildDisplayCommandText(string executableAlias, IReadOnlyList<string> tokens)
    {
        var segments = new List<string>(tokens.Count) { executableAlias };

        for (var index = 1; index < tokens.Count; index++)
        {
            segments.Add(QuoteArgument(tokens[index]));
        }

        return string.Join(" ", segments);
    }

    private static string QuoteArgument(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return "\"\"";
        }

        if (!value.Contains(' ') && !value.Contains('"'))
        {
            return value;
        }

        return $"\"{value.Replace("\"", "\\\"", StringComparison.Ordinal)}\"";
    }

    private static IReadOnlyList<string> SplitCommandLine(string commandText)
    {
        var argumentPointers = CommandLineToArgvW(commandText, out var argumentCount);
        if (argumentPointers == IntPtr.Zero)
        {
            throw new InvalidOperationException("无法解析当前命令。");
        }

        try
        {
            var arguments = new string[argumentCount];

            for (var index = 0; index < argumentCount; index++)
            {
                var argumentPointer = Marshal.ReadIntPtr(argumentPointers, index * IntPtr.Size);
                arguments[index] = Marshal.PtrToStringUni(argumentPointer) ?? string.Empty;
            }

            return arguments;
        }
        finally
        {
            _ = LocalFree(argumentPointers);
        }
    }

    private static async Task AwaitOutputCompletionAsync(Task standardOutputTask, Task standardErrorTask) =>
        await Task.WhenAll(standardOutputTask, standardErrorTask).ConfigureAwait(false);

    private static void TryTerminateProcess(Process process)
    {
        try
        {
            if (!process.HasExited)
            {
                process.Kill(entireProcessTree: true);
            }
        }
        catch
        {
        }
    }

    [DllImport("shell32.dll", SetLastError = true)]
    private static extern IntPtr CommandLineToArgvW(
        [MarshalAs(UnmanagedType.LPWStr)] string commandLine,
        out int argumentCount);

    [DllImport("kernel32.dll")]
    private static extern IntPtr LocalFree(IntPtr memory);
}
