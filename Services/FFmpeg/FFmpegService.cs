using System;
using System.ComponentModel;
using System.Diagnostics;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Vidvix.Core.Interfaces;
using Vidvix.Core.Models;

namespace Vidvix.Services.FFmpeg;

public sealed class FFmpegService : IFFmpegService
{
    private readonly ILogger _logger;

    public FFmpegService(ILogger logger)
    {
        _logger = logger;
    }

    public async Task<FFmpegExecutionResult> ExecuteAsync(
        FFmpegCommand command,
        FFmpegExecutionOptions? executionOptions = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command);

        var standardOutputBuilder = new StringBuilder();
        var standardErrorBuilder = new StringBuilder();
        var startedAt = DateTimeOffset.UtcNow;

        using var process = new Process
        {
            StartInfo = CreateStartInfo(command),
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

            standardOutputBuilder.AppendLine(eventArgs.Data);
        };

        process.ErrorDataReceived += (_, eventArgs) =>
        {
            if (eventArgs.Data is null)
            {
                standardErrorClosed.TrySetResult();
                return;
            }

            standardErrorBuilder.AppendLine(eventArgs.Data);
        };

        try
        {
            if (!process.Start())
            {
                return FFmpegExecutionResult.Failed(
                    command,
                    null,
                    standardOutputBuilder.ToString(),
                    standardErrorBuilder.ToString(),
                    DateTimeOffset.UtcNow - startedAt,
                    "FFmpeg 进程无法启动。");
            }

            process.BeginOutputReadLine();
            process.BeginErrorReadLine();

            using var executionCancellationSource = CreateExecutionCancellationSource(executionOptions, cancellationToken);
            using var cancellationRegistration = executionCancellationSource.Token.Register(() => TryTerminateProcess(process));

            try
            {
                await process.WaitForExitAsync(executionCancellationSource.Token).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                await AwaitOutputCompletionAsync(standardOutputClosed.Task, standardErrorClosed.Task).ConfigureAwait(false);

                return FFmpegExecutionResult.Cancelled(
                    command,
                    standardOutputBuilder.ToString(),
                    standardErrorBuilder.ToString(),
                    DateTimeOffset.UtcNow - startedAt);
            }
            catch (OperationCanceledException)
            {
                await AwaitOutputCompletionAsync(standardOutputClosed.Task, standardErrorClosed.Task).ConfigureAwait(false);

                return FFmpegExecutionResult.TimeoutFailure(
                    command,
                    standardOutputBuilder.ToString(),
                    standardErrorBuilder.ToString(),
                    DateTimeOffset.UtcNow - startedAt);
            }

            await AwaitOutputCompletionAsync(standardOutputClosed.Task, standardErrorClosed.Task).ConfigureAwait(false);

            var standardOutput = standardOutputBuilder.ToString();
            var standardError = standardErrorBuilder.ToString();
            var duration = DateTimeOffset.UtcNow - startedAt;

            if (process.ExitCode == 0)
            {
                return FFmpegExecutionResult.Success(command, process.ExitCode, standardOutput, standardError, duration);
            }

            return FFmpegExecutionResult.Failed(
                command,
                process.ExitCode,
                standardOutput,
                standardError,
                duration,
                $"FFmpeg 进程已退出，返回代码：{process.ExitCode}。");
        }
        catch (Win32Exception exception)
        {
            var duration = DateTimeOffset.UtcNow - startedAt;
            const string failureReason = "本地 FFmpeg 不可用，请先完成运行时准备。";
            _logger.Log(LogLevel.Error, failureReason, exception);

            return FFmpegExecutionResult.Failed(
                command,
                null,
                standardOutputBuilder.ToString(),
                standardErrorBuilder.ToString(),
                duration,
                failureReason);
        }
        catch (Exception exception)
        {
            var duration = DateTimeOffset.UtcNow - startedAt;
            const string failureReason = "执行 FFmpeg 任务时发生异常。";
            _logger.Log(LogLevel.Error, failureReason, exception);

            return FFmpegExecutionResult.Failed(
                command,
                null,
                standardOutputBuilder.ToString(),
                standardErrorBuilder.ToString(),
                duration,
                failureReason);
        }
    }

    private static ProcessStartInfo CreateStartInfo(FFmpegCommand command)
    {
        var startInfo = new ProcessStartInfo
        {
            FileName = command.ExecutablePath,
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            StandardOutputEncoding = Encoding.UTF8,
            StandardErrorEncoding = Encoding.UTF8
        };

        foreach (var argument in command.Arguments)
        {
            startInfo.ArgumentList.Add(argument);
        }

        return startInfo;
    }

    private static CancellationTokenSource CreateExecutionCancellationSource(
        FFmpegExecutionOptions? executionOptions,
        CancellationToken cancellationToken)
    {
        var cancellationSource = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);

        if (executionOptions?.Timeout is { } timeout && timeout > TimeSpan.Zero)
        {
            cancellationSource.CancelAfter(timeout);
        }

        return cancellationSource;
    }

    private static async Task AwaitOutputCompletionAsync(Task standardOutputTask, Task standardErrorTask)
    {
        await Task.WhenAll(standardOutputTask, standardErrorTask).ConfigureAwait(false);
    }

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
}
