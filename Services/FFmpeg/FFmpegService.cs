using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.Globalization;
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
        var progressParser = executionOptions?.Progress is null
            ? null
            : new FFmpegProgressParser(executionOptions.InputDuration, executionOptions.Progress);

        using var process = new Process
        {
            StartInfo = CreateStartInfo(command, executionOptions),
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

            progressParser?.HandleStandardOutputLine(eventArgs.Data);
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
                    "FFmpeg \u8fdb\u7a0b\u65e0\u6cd5\u542f\u52a8\u3002");
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

            if (process.ExitCode == 0)
            {
                progressParser?.CompleteIfNeeded();
            }

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
                $"FFmpeg \u8fdb\u7a0b\u5df2\u9000\u51fa\uff0c\u8fd4\u56de\u4ee3\u7801\uff1a{process.ExitCode}\u3002");
        }
        catch (Win32Exception exception)
        {
            var duration = DateTimeOffset.UtcNow - startedAt;
            const string failureReason = "\u672c\u5730 FFmpeg \u4e0d\u53ef\u7528\uff0c\u8bf7\u5148\u5b8c\u6210\u8fd0\u884c\u65f6\u51c6\u5907\u3002";
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
            const string failureReason = "\u6267\u884c FFmpeg \u4efb\u52a1\u65f6\u53d1\u751f\u5f02\u5e38\u3002";
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

    private static ProcessStartInfo CreateStartInfo(FFmpegCommand command, FFmpegExecutionOptions? executionOptions)
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

        foreach (var argument in BuildArguments(command.Arguments, executionOptions?.Progress is not null))
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

    private static IReadOnlyList<string> BuildArguments(IReadOnlyList<string> arguments, bool enableProgressReporting)
    {
        if (!enableProgressReporting || ContainsArgument(arguments, "-progress"))
        {
            return arguments;
        }

        var containsNoStats = ContainsArgument(arguments, "-nostats");
        var preparedArguments = new List<string>(arguments.Count + (containsNoStats ? 2 : 3));
        var inserted = false;

        for (var index = 0; index < arguments.Count; index++)
        {
            if (!inserted && string.Equals(arguments[index], "-i", StringComparison.OrdinalIgnoreCase))
            {
                if (!containsNoStats)
                {
                    preparedArguments.Add("-nostats");
                }

                preparedArguments.Add("-progress");
                preparedArguments.Add("pipe:1");
                inserted = true;
            }

            preparedArguments.Add(arguments[index]);
        }

        if (inserted)
        {
            return preparedArguments;
        }

        if (!containsNoStats)
        {
            preparedArguments.Insert(0, "-nostats");
        }

        preparedArguments.Insert(containsNoStats ? 0 : 1, "-progress");
        preparedArguments.Insert(containsNoStats ? 1 : 2, "pipe:1");
        return preparedArguments;
    }

    private static bool ContainsArgument(IReadOnlyList<string> arguments, string targetArgument)
    {
        for (var index = 0; index < arguments.Count; index++)
        {
            if (string.Equals(arguments[index], targetArgument, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }

        return false;
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

    private sealed class FFmpegProgressParser
    {
        private readonly TimeSpan? _inputDuration;
        private readonly IProgress<FFmpegProgressUpdate> _progress;
        private TimeSpan? _processedDuration;
        private bool _completionReported;

        public FFmpegProgressParser(TimeSpan? inputDuration, IProgress<FFmpegProgressUpdate> progress)
        {
            _inputDuration = inputDuration;
            _progress = progress;
        }

        public void HandleStandardOutputLine(string line)
        {
            if (string.IsNullOrWhiteSpace(line))
            {
                return;
            }

            var separatorIndex = line.IndexOf('=');
            if (separatorIndex <= 0 || separatorIndex >= line.Length - 1)
            {
                return;
            }

            var key = line[..separatorIndex];
            var value = line[(separatorIndex + 1)..];

            switch (key)
            {
                case "out_time":
                    if (TimeSpan.TryParse(value, CultureInfo.InvariantCulture, out var parsedDuration))
                    {
                        _processedDuration = parsedDuration;
                    }

                    break;
                case "out_time_us":
                    if (long.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var microseconds) &&
                        microseconds >= 0)
                    {
                        _processedDuration = TimeSpan.FromTicks(microseconds * 10);
                    }

                    break;
                case "progress":
                    Report(isCompleted: string.Equals(value, "end", StringComparison.OrdinalIgnoreCase));
                    break;
            }
        }

        public void CompleteIfNeeded() => Report(isCompleted: true);

        private void Report(bool isCompleted)
        {
            if (isCompleted && _completionReported)
            {
                return;
            }

            var totalDuration = _inputDuration.HasValue && _inputDuration.Value > TimeSpan.Zero
                ? _inputDuration
                : null;
            var processedDuration = isCompleted
                ? totalDuration ?? _processedDuration
                : _processedDuration;

            double? progressRatio = null;
            if (processedDuration is { } processed && totalDuration is { } total && total > TimeSpan.Zero)
            {
                progressRatio = Math.Clamp(processed.TotalMilliseconds / total.TotalMilliseconds, 0d, 1d);
            }

            if (isCompleted)
            {
                progressRatio = 1d;
                _completionReported = true;
            }

            _progress.Report(new FFmpegProgressUpdate(processedDuration, totalDuration, progressRatio, isCompleted));
        }
    }
}
