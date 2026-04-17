namespace Vidvix.Core.Models;

public sealed class TerminalCommandExecutionResult
{
    private TerminalCommandExecutionResult(
        string displayCommandText,
        int? exitCode,
        bool wasCancelled,
        string? failureReason)
    {
        DisplayCommandText = displayCommandText;
        ExitCode = exitCode;
        WasCancelled = wasCancelled;
        FailureReason = failureReason;
    }

    public string DisplayCommandText { get; }

    public int? ExitCode { get; }

    public bool WasCancelled { get; }

    public string? FailureReason { get; }

    public bool WasSuccessful =>
        !WasCancelled &&
        ExitCode == 0 &&
        string.IsNullOrWhiteSpace(FailureReason);

    public static TerminalCommandExecutionResult Success(string displayCommandText, int exitCode) =>
        new(displayCommandText, exitCode, false, null);

    public static TerminalCommandExecutionResult Failed(
        string displayCommandText,
        string failureReason,
        int? exitCode = null) =>
        new(displayCommandText, exitCode, false, failureReason);

    public static TerminalCommandExecutionResult Cancelled(string displayCommandText) =>
        new(displayCommandText, null, true, "命令已取消。");
}
