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
            _logger.Log(LogLevel.Info, eventArgs.Data);
        };

        process.ErrorDataReceived += (_, eventArgs) =>
        {
            if (eventArgs.Data is null)
            {
                standardErrorClosed.TrySetResult();
                return;
            }

            standardErrorBuilder.AppendLine(eventArgs.Data);
            _logger.Log(MapLogLevel(eventArgs.Data), eventArgs.Data);
        };

        try
        {
            _logger.Log(LogLevel.Info, $"Executing FFmpeg command: {command.DisplayCommand}");

            if (!process.Start())
            {
                return FFmpegExecutionResult.Failed(
                    command,
                    null,
                    standardOutputBuilder.ToString(),
                    standardErrorBuilder.ToString(),
                    DateTimeOffset.UtcNow - startedAt,
                    "The FFmpeg process could not be started.");
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

                var cancelledResult = FFmpegExecutionResult.Cancelled(
                    command,
                    standardOutputBuilder.ToString(),
                    standardErrorBuilder.ToString(),
                    DateTimeOffset.UtcNow - startedAt);

                _logger.Log(LogLevel.Warning, cancelledResult.FailureReason!);
                return cancelledResult;
            }
            catch (OperationCanceledException)
            {
                await AwaitOutputCompletionAsync(standardOutputClosed.Task, standardErrorClosed.Task).ConfigureAwait(false);

                var timedOutResult = FFmpegExecutionResult.TimeoutFailure(
                    command,
                    standardOutputBuilder.ToString(),
                    standardErrorBuilder.ToString(),
                    DateTimeOffset.UtcNow - startedAt);

                _logger.Log(LogLevel.Error, timedOutResult.FailureReason!);
                return timedOutResult;
            }

            await AwaitOutputCompletionAsync(standardOutputClosed.Task, standardErrorClosed.Task).ConfigureAwait(false);

            var standardOutput = standardOutputBuilder.ToString();
            var standardError = standardErrorBuilder.ToString();
            var duration = DateTimeOffset.UtcNow - startedAt;

            if (process.ExitCode == 0)
            {
                _logger.Log(LogLevel.Info, $"FFmpeg completed successfully in {duration.TotalSeconds:F2}s.");
                return FFmpegExecutionResult.Success(command, process.ExitCode, standardOutput, standardError, duration);
            }

            var failureReason = $"FFmpeg exited with code {process.ExitCode}.";
            _logger.Log(LogLevel.Error, failureReason);

            return FFmpegExecutionResult.Failed(
                command,
                process.ExitCode,
                standardOutput,
                standardError,
                duration,
                failureReason);
        }
        catch (Win32Exception exception)
        {
            var duration = DateTimeOffset.UtcNow - startedAt;
            const string failureReason = "FFmpeg executable was not found. Ensure FFmpeg is installed and available on PATH.";
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
            const string failureReason = "An unexpected error occurred while executing FFmpeg.";
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

    private static LogLevel MapLogLevel(string line)
    {
        if (line.Contains("warning", StringComparison.OrdinalIgnoreCase))
        {
            return LogLevel.Warning;
        }

        if (line.Contains("error", StringComparison.OrdinalIgnoreCase) ||
            line.Contains("failed", StringComparison.OrdinalIgnoreCase) ||
            line.Contains("invalid", StringComparison.OrdinalIgnoreCase))
        {
            return LogLevel.Error;
        }

        return LogLevel.Info;
    }
}


