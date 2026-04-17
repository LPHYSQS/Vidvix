using System;
using System.Threading;
using System.Threading.Tasks;
using Vidvix.Core.Models;

namespace Vidvix.Core.Interfaces;

public interface IFFmpegTerminalService
{
    Task<TerminalCommandExecutionResult> ExecuteAsync(
        string commandText,
        IProgress<string>? outputProgress = null,
        CancellationToken cancellationToken = default);
}
