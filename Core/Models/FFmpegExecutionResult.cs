using System;

namespace Vidvix.Core.Models;

public sealed class FFmpegExecutionResult
{
    private FFmpegExecutionResult(
        FFmpegCommand command,
        int? exitCode,
        string standardOutput,
        string standardError,
        TimeSpan duration,
        bool wasCancelled,
        bool timedOut,
        string? failureReason)
    {
        Command = command;
        ExitCode = exitCode;
        StandardOutput = standardOutput;
        StandardError = standardError;
        Duration = duration;
        WasCancelled = wasCancelled;
        TimedOut = timedOut;
        FailureReason = failureReason;
    }

    public FFmpegCommand Command { get; }

    public int? ExitCode { get; }

    public string StandardOutput { get; }

    public string StandardError { get; }

    public TimeSpan Duration { get; }

    public bool WasCancelled { get; }

    public bool TimedOut { get; }

    public string? FailureReason { get; }

    public bool WasSuccessful =>
        !WasCancelled &&
        !TimedOut &&
        ExitCode == 0 &&
        string.IsNullOrWhiteSpace(FailureReason);

    public static FFmpegExecutionResult Success(
        FFmpegCommand command,
        int exitCode,
        string standardOutput,
        string standardError,
        TimeSpan duration) =>
        new(command, exitCode, standardOutput, standardError, duration, false, false, null);

    public static FFmpegExecutionResult Failed(
        FFmpegCommand command,
        int? exitCode,
        string standardOutput,
        string standardError,
        TimeSpan duration,
        string failureReason) =>
        new(command, exitCode, standardOutput, standardError, duration, false, false, failureReason);

    public static FFmpegExecutionResult Cancelled(
        FFmpegCommand command,
        string standardOutput,
        string standardError,
        TimeSpan duration) =>
        new(command, null, standardOutput, standardError, duration, true, false, "当前处理任务已取消。");

    public static FFmpegExecutionResult TimeoutFailure(
        FFmpegCommand command,
        string standardOutput,
        string standardError,
        TimeSpan duration) =>
        new(command, null, standardOutput, standardError, duration, false, true, "当前处理任务已超时。");
}
