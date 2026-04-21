using System;

namespace Vidvix.Core.Models;

public sealed class TerminalCommandExecutionResult
{
    private TerminalCommandExecutionResult(
        string displayCommandText,
        int? exitCode,
        bool wasCancelled,
        bool wasRejected,
        string? failureReason,
        Func<string>? failureReasonResolver)
    {
        DisplayCommandText = displayCommandText;
        ExitCode = exitCode;
        WasCancelled = wasCancelled;
        WasRejected = wasRejected;
        FailureReason = failureReason;
        FailureReasonResolver = failureReasonResolver ?? (!string.IsNullOrWhiteSpace(failureReason) ? () => failureReason : null);
    }

    public string DisplayCommandText { get; }

    public int? ExitCode { get; }

    public bool WasCancelled { get; }

    public bool WasRejected { get; }

    public string? FailureReason { get; }

    public Func<string>? FailureReasonResolver { get; }

    public bool WasSuccessful =>
        !WasCancelled &&
        ExitCode == 0 &&
        string.IsNullOrWhiteSpace(FailureReason);

    public static TerminalCommandExecutionResult Success(string displayCommandText, int exitCode) =>
        new(displayCommandText, exitCode, false, false, null, null);

    public static TerminalCommandExecutionResult Failed(
        string displayCommandText,
        string failureReason,
        int? exitCode = null,
        bool wasRejected = false,
        Func<string>? failureReasonResolver = null) =>
        new(displayCommandText, exitCode, false, wasRejected, failureReason, failureReasonResolver);

    public static TerminalCommandExecutionResult Cancelled(
        string displayCommandText,
        string? failureReason = null,
        Func<string>? failureReasonResolver = null) =>
        new(displayCommandText, null, true, false, failureReason ?? "命令已取消。", failureReasonResolver);
}
