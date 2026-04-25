using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Vidvix.Core.Interfaces;
using Vidvix.Core.Models;

namespace Vidvix.Services.AI;

internal sealed class AiRuntimeProbeExecutor
{
    private static readonly byte[] ProbeFrame0Bytes = Convert.FromBase64String(
        "iVBORw0KGgoAAAANSUhEUgAAAAQAAAAECAYAAACp8Z5+AAAAAXNSR0IArs4c6QAAAARnQU1BAACxjwv8YQUAAAAJcEhZcwAADsMAAA7DAcdvqGQAAAASSURBVBhXYxBQMPiPjBlIFwAA01oV8UBBjOIAAAAASUVORK5CYII=");

    private static readonly byte[] ProbeFrame1Bytes = Convert.FromBase64String(
        "iVBORw0KGgoAAAANSUhEUgAAAAQAAAAECAYAAACp8Z5+AAAAAXNSR0IArs4c6QAAAARnQU1BAACxjwv8YQUAAAAJcEhZcwAADsMAAA7DAcdvqGQAAAASSURBVBhXYzBwCPiPjBlIFwAAomkb8TO3RVgAAAAASUVORK5CYII=");

    private static readonly TimeSpan ProbeTimeout = TimeSpan.FromSeconds(20);
    private readonly ApplicationConfiguration _configuration;
    private readonly ILogger _logger;

    public AiRuntimeProbeExecutor(ApplicationConfiguration configuration, ILogger logger)
    {
        _configuration = configuration ?? throw new ArgumentNullException(nameof(configuration));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    public Task<AiExecutionSupportStatus> ProbeRifeGpuAsync(
        AiRuntimeDescriptor descriptor,
        CancellationToken cancellationToken = default)
    {
        var model = descriptor.Models.FirstOrDefault();
        if (model is null)
        {
            return Task.FromResult(CreateProbeFailedStatus("RIFE model descriptor is missing."));
        }

        return ProbeRifeAsync(descriptor, model, useCpuFallback: false, cancellationToken);
    }

    public Task<AiExecutionSupportStatus> ProbeRifeCpuAsync(
        AiRuntimeDescriptor descriptor,
        CancellationToken cancellationToken = default)
    {
        var model = descriptor.Models.FirstOrDefault();
        if (model is null)
        {
            return Task.FromResult(CreateProbeFailedStatus("RIFE model descriptor is missing."));
        }

        return ProbeRifeAsync(descriptor, model, useCpuFallback: true, cancellationToken);
    }

    public Task<AiExecutionSupportStatus> ProbeRealEsrganGpuAsync(
        AiRuntimeDescriptor descriptor,
        CancellationToken cancellationToken = default)
    {
        var model = descriptor.Models.FirstOrDefault(item =>
            string.Equals(item.Id, "standard", StringComparison.OrdinalIgnoreCase));
        if (model is null)
        {
            return Task.FromResult(CreateProbeFailedStatus("Real-ESRGAN Standard model descriptor is missing."));
        }

        return ProbeRealEsrganAsync(descriptor, model, useCpuFallback: false, cancellationToken);
    }

    public Task<AiExecutionSupportStatus> ProbeRealEsrganCpuAsync(
        AiRuntimeDescriptor descriptor,
        CancellationToken cancellationToken = default)
    {
        var model = descriptor.Models.FirstOrDefault(item =>
            string.Equals(item.Id, "standard", StringComparison.OrdinalIgnoreCase));
        if (model is null)
        {
            return Task.FromResult(CreateProbeFailedStatus("Real-ESRGAN Standard model descriptor is missing."));
        }

        return ProbeRealEsrganAsync(descriptor, model, useCpuFallback: true, cancellationToken);
    }

    public async Task<IReadOnlyList<AiRuntimeGpuDeviceDescriptor>> ProbeRifeGpuDevicesAsync(
        AiRuntimeDescriptor descriptor,
        CancellationToken cancellationToken = default)
    {
        var model = descriptor.Models.FirstOrDefault();
        if (model is null || !descriptor.IsAvailable)
        {
            return Array.Empty<AiRuntimeGpuDeviceDescriptor>();
        }

        var probeRootPath = CreateProbeSessionRootPath(descriptor.Id);

        try
        {
            var input0Path = Path.Combine(probeRootPath, "frame0.png");
            var input1Path = Path.Combine(probeRootPath, "frame1.png");
            var discoveryOutputPath = Path.Combine(probeRootPath, "rife-gpu-discovery-output.png");

            await WriteProbeFramesAsync(input0Path, input1Path, cancellationToken).ConfigureAwait(false);
            var stagedModelPath = StageModelAssets(probeRootPath, model);

            var discoveryResult = await ExecuteProcessAsync(
                    descriptor.ExecutablePath,
                    new[]
                    {
                        "-v",
                        "-0",
                        input0Path,
                        "-1",
                        input1Path,
                        "-o",
                        discoveryOutputPath,
                        "-m",
                        stagedModelPath
                    },
                    cancellationToken)
                .ConfigureAwait(false);

            var discoveredDevices = ParseGpuDevices(discoveryResult.CombinedOutput);
            if (discoveredDevices.Count == 0)
            {
                return Array.Empty<AiRuntimeGpuDeviceDescriptor>();
            }

            var resolvedDevices = new List<AiRuntimeGpuDeviceDescriptor>(discoveredDevices.Count);
            foreach (var discoveredDevice in discoveredDevices)
            {
                var outputPath = Path.Combine(probeRootPath, $"rife-gpu-{discoveredDevice.Index}.png");
                var probeResult = await ExecuteProcessAsync(
                        descriptor.ExecutablePath,
                        new[]
                        {
                            "-0",
                            input0Path,
                            "-1",
                            input1Path,
                            "-o",
                            outputPath,
                            "-m",
                            stagedModelPath,
                            "-g",
                            discoveredDevice.Index.ToString()
                        },
                        cancellationToken)
                    .ConfigureAwait(false);

                var support = CreateGpuProbeStatus(probeResult, outputPath);
                resolvedDevices.Add(new AiRuntimeGpuDeviceDescriptor
                {
                    Index = discoveredDevice.Index,
                    Name = discoveredDevice.Name,
                    Kind = AiGpuDeviceClassifier.Classify(discoveredDevice.Name),
                    Support = support
                });
            }

            return resolvedDevices;
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception exception)
        {
            _logger.Log(LogLevel.Warning, $"RIFE GPU device probe failed for {descriptor.DisplayName}.", exception);
            return Array.Empty<AiRuntimeGpuDeviceDescriptor>();
        }
        finally
        {
            TryDeleteDirectory(probeRootPath);
        }
    }

    public async Task<IReadOnlyList<AiRuntimeGpuDeviceDescriptor>> ProbeRealEsrganGpuDevicesAsync(
        AiRuntimeDescriptor descriptor,
        CancellationToken cancellationToken = default)
    {
        var model = descriptor.Models.FirstOrDefault(item =>
            string.Equals(item.Id, "standard", StringComparison.OrdinalIgnoreCase));
        if (model is null || !descriptor.IsAvailable)
        {
            return Array.Empty<AiRuntimeGpuDeviceDescriptor>();
        }

        var probeRootPath = CreateProbeSessionRootPath(descriptor.Id);

        try
        {
            var inputPath = Path.Combine(probeRootPath, "frame0.png");
            var discoveryOutputPath = Path.Combine(probeRootPath, "realesrgan-gpu-discovery-output.png");
            await File.WriteAllBytesAsync(inputPath, ProbeFrame0Bytes, cancellationToken).ConfigureAwait(false);

            var stagedModelPath = StageModelAssets(probeRootPath, model);
            var discoveryResult = await ExecuteProcessAsync(
                    descriptor.ExecutablePath,
                    new[]
                    {
                        "-v",
                        "-i",
                        inputPath,
                        "-o",
                        discoveryOutputPath,
                        "-m",
                        stagedModelPath,
                        "-n",
                        model.RuntimeModelName,
                        "-s",
                        model.NativeScaleFactors.FirstOrDefault().ToString()
                    },
                    cancellationToken)
                .ConfigureAwait(false);

            var discoveredDevices = ParseGpuDevices(discoveryResult.CombinedOutput);
            if (discoveredDevices.Count == 0)
            {
                return Array.Empty<AiRuntimeGpuDeviceDescriptor>();
            }

            var resolvedDevices = new List<AiRuntimeGpuDeviceDescriptor>(discoveredDevices.Count);
            foreach (var discoveredDevice in discoveredDevices)
            {
                var outputPath = Path.Combine(probeRootPath, $"realesrgan-gpu-{discoveredDevice.Index}.png");
                var probeResult = await ExecuteProcessAsync(
                        descriptor.ExecutablePath,
                        new[]
                        {
                            "-i",
                            inputPath,
                            "-o",
                            outputPath,
                            "-m",
                            stagedModelPath,
                            "-n",
                            model.RuntimeModelName,
                            "-s",
                            model.NativeScaleFactors.FirstOrDefault().ToString(),
                            "-g",
                            discoveredDevice.Index.ToString()
                        },
                        cancellationToken)
                    .ConfigureAwait(false);

                var support = CreateGpuProbeStatus(probeResult, outputPath);
                resolvedDevices.Add(new AiRuntimeGpuDeviceDescriptor
                {
                    Index = discoveredDevice.Index,
                    Name = discoveredDevice.Name,
                    Kind = AiGpuDeviceClassifier.Classify(discoveredDevice.Name),
                    Support = support
                });
            }

            return resolvedDevices;
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception exception)
        {
            _logger.Log(LogLevel.Warning, $"Real-ESRGAN GPU device probe failed for {descriptor.DisplayName}.", exception);
            return Array.Empty<AiRuntimeGpuDeviceDescriptor>();
        }
        finally
        {
            TryDeleteDirectory(probeRootPath);
        }
    }

    private async Task<AiExecutionSupportStatus> ProbeRifeAsync(
        AiRuntimeDescriptor descriptor,
        AiRuntimeModelDescriptor model,
        bool useCpuFallback,
        CancellationToken cancellationToken)
    {
        if (!descriptor.IsAvailable)
        {
            return CreateMissingRuntimeStatus(descriptor);
        }

        var probeRootPath = CreateProbeSessionRootPath(descriptor.Id);

        try
        {
            var input0Path = Path.Combine(probeRootPath, "frame0.png");
            var input1Path = Path.Combine(probeRootPath, "frame1.png");
            var outputPath = Path.Combine(probeRootPath, useCpuFallback ? "rife-cpu-output.png" : "rife-gpu-output.png");

            await WriteProbeFramesAsync(input0Path, input1Path, cancellationToken).ConfigureAwait(false);

            var stagedModelPath = StageModelAssets(probeRootPath, model);
            var arguments = new[]
            {
                "-0",
                input0Path,
                "-1",
                input1Path,
                "-o",
                outputPath,
                "-m",
                stagedModelPath
            };

            var finalArguments = useCpuFallback
                ? arguments.Concat(new[] { "-g", "-1" }).ToArray()
                : arguments;
            var result = await ExecuteProcessAsync(descriptor.ExecutablePath, finalArguments, cancellationToken)
                .ConfigureAwait(false);

            if (result.WasSuccessful && File.Exists(outputPath))
            {
                return new AiExecutionSupportStatus
                {
                    State = AiExecutionSupportState.Available
                };
            }

            return new AiExecutionSupportStatus
            {
                State = AiExecutionSupportState.Unavailable,
                DiagnosticMessage = SummarizeDiagnostic(result)
            };
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception exception)
        {
            _logger.Log(
                LogLevel.Warning,
                $"AI runtime probe failed for {descriptor.DisplayName} ({(useCpuFallback ? "CPU" : "GPU")}).",
                exception);
            return CreateProbeFailedStatus(exception.Message);
        }
        finally
        {
            TryDeleteDirectory(probeRootPath);
        }
    }

    private async Task<AiExecutionSupportStatus> ProbeRealEsrganAsync(
        AiRuntimeDescriptor descriptor,
        AiRuntimeModelDescriptor model,
        bool useCpuFallback,
        CancellationToken cancellationToken)
    {
        if (!descriptor.IsAvailable)
        {
            return CreateMissingRuntimeStatus(descriptor);
        }

        var probeRootPath = CreateProbeSessionRootPath(descriptor.Id);

        try
        {
            var inputPath = Path.Combine(probeRootPath, "frame0.png");
            var outputPath = Path.Combine(probeRootPath, useCpuFallback ? "realesrgan-cpu-output.png" : "realesrgan-gpu-output.png");
            await File.WriteAllBytesAsync(inputPath, ProbeFrame0Bytes, cancellationToken).ConfigureAwait(false);

            var stagedModelPath = StageModelAssets(probeRootPath, model);
            var arguments = new[]
            {
                "-i",
                inputPath,
                "-o",
                outputPath,
                "-m",
                stagedModelPath,
                "-n",
                model.RuntimeModelName,
                "-s",
                model.NativeScaleFactors.FirstOrDefault().ToString()
            }.ToList();

            if (useCpuFallback)
            {
                arguments.Add("-g");
                arguments.Add("-1");
            }

            var result = await ExecuteProcessAsync(descriptor.ExecutablePath, arguments, cancellationToken)
                .ConfigureAwait(false);

            if (result.WasSuccessful && File.Exists(outputPath))
            {
                return new AiExecutionSupportStatus
                {
                    State = AiExecutionSupportState.Available
                };
            }

            if (useCpuFallback &&
                result.CombinedOutput.Contains("invalid gpu device", StringComparison.OrdinalIgnoreCase))
            {
                return new AiExecutionSupportStatus
                {
                    State = AiExecutionSupportState.Unsupported,
                    DiagnosticMessage = BuildUnsupportedRealEsrganCpuDiagnostic(result)
                };
            }

            return new AiExecutionSupportStatus
            {
                State = AiExecutionSupportState.Unavailable,
                DiagnosticMessage = SummarizeDiagnostic(result)
            };
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception exception)
        {
            _logger.Log(
                LogLevel.Warning,
                $"AI runtime probe failed for {descriptor.DisplayName} ({(useCpuFallback ? "CPU" : "GPU")}).",
                exception);
            return CreateProbeFailedStatus(exception.Message);
        }
        finally
        {
            TryDeleteDirectory(probeRootPath);
        }
    }

    private string CreateProbeSessionRootPath(string runtimeId)
    {
        var probeCacheRootPath = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            _configuration.LocalDataDirectoryName,
            _configuration.RuntimeDirectoryName,
            _configuration.AiRuntimeDirectoryName,
            _configuration.AiRuntimeProbeCacheDirectoryName,
            runtimeId);
        Directory.CreateDirectory(probeCacheRootPath);
        var sessionRootPath = Path.Combine(probeCacheRootPath, Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(sessionRootPath);
        return sessionRootPath;
    }

    private static async Task WriteProbeFramesAsync(
        string input0Path,
        string input1Path,
        CancellationToken cancellationToken)
    {
        await File.WriteAllBytesAsync(input0Path, ProbeFrame0Bytes, cancellationToken).ConfigureAwait(false);
        await File.WriteAllBytesAsync(input1Path, ProbeFrame1Bytes, cancellationToken).ConfigureAwait(false);
    }

    private static string StageModelAssets(string probeRootPath, AiRuntimeModelDescriptor model)
    {
        var targetDirectoryPath = Path.Combine(probeRootPath, model.PreparedDirectoryName);
        Directory.CreateDirectory(targetDirectoryPath);

        foreach (var asset in model.Assets)
        {
            var configTargetPath = Path.Combine(targetDirectoryPath, Path.GetFileName(asset.ConfigPath));
            var weightTargetPath = Path.Combine(targetDirectoryPath, Path.GetFileName(asset.WeightPath));
            File.Copy(asset.ConfigPath, configTargetPath, overwrite: true);
            File.Copy(asset.WeightPath, weightTargetPath, overwrite: true);
        }

        return targetDirectoryPath;
    }

    private static async Task<ProbeProcessResult> ExecuteProcessAsync(
        string executablePath,
        IReadOnlyCollection<string> arguments,
        CancellationToken cancellationToken)
    {
        var startInfo = new ProcessStartInfo(executablePath)
        {
            WorkingDirectory = Path.GetDirectoryName(executablePath) ?? AppContext.BaseDirectory,
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true
        };

        foreach (var argument in arguments)
        {
            startInfo.ArgumentList.Add(argument);
        }

        using var process = new Process
        {
            StartInfo = startInfo
        };

        if (!process.Start())
        {
            return new ProbeProcessResult(
                ExitCode: -1,
                TimedOut: false,
                StandardOutput: string.Empty,
                StandardError: "Process start returned false.");
        }

        var standardOutputTask = process.StandardOutput.ReadToEndAsync();
        var standardErrorTask = process.StandardError.ReadToEndAsync();
        var exitTask = process.WaitForExitAsync();
        var delayTask = Task.Delay(ProbeTimeout, cancellationToken);
        var completedTask = await Task.WhenAny(exitTask, delayTask).ConfigureAwait(false);

        if (completedTask != exitTask)
        {
            TryKill(process);
            if (cancellationToken.IsCancellationRequested)
            {
                cancellationToken.ThrowIfCancellationRequested();
            }

            await process.WaitForExitAsync().ConfigureAwait(false);
            return new ProbeProcessResult(
                ExitCode: process.ExitCode,
                TimedOut: true,
                StandardOutput: await standardOutputTask.ConfigureAwait(false),
                StandardError: await standardErrorTask.ConfigureAwait(false));
        }

        await exitTask.ConfigureAwait(false);

        return new ProbeProcessResult(
            ExitCode: process.ExitCode,
            TimedOut: false,
            StandardOutput: await standardOutputTask.ConfigureAwait(false),
            StandardError: await standardErrorTask.ConfigureAwait(false));
    }

    private static string SummarizeDiagnostic(ProbeProcessResult result)
    {
        if (result.TimedOut)
        {
            return "Probe timed out.";
        }

        var lines = result.CombinedOutput
            .Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries)
            .Select(line => line.Trim())
            .Where(line => !string.IsNullOrWhiteSpace(line))
            .ToArray();

        if (lines.Length == 0)
        {
            return $"Probe exited with code {result.ExitCode}.";
        }

        var priorityLines = lines
            .Where(IsPriorityDiagnosticLine)
            .Take(3)
            .ToArray();
        if (priorityLines.Length > 0)
        {
            return string.Join(" | ", priorityLines);
        }

        var summaryLines = lines
            .Take(3)
            .ToArray();

        return string.Join(" | ", summaryLines);
    }

    private static IReadOnlyList<DiscoveredGpuDevice> ParseGpuDevices(string combinedOutput)
    {
        if (string.IsNullOrWhiteSpace(combinedOutput))
        {
            return Array.Empty<DiscoveredGpuDevice>();
        }

        var devices = new Dictionary<int, DiscoveredGpuDevice>();
        var lines = combinedOutput.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries);
        foreach (var rawLine in lines)
        {
            var line = rawLine.Trim();
            if (!TryParseGpuDeviceLine(line, out var index, out var name) || devices.ContainsKey(index))
            {
                continue;
            }

            devices[index] = new DiscoveredGpuDevice(index, name);
        }

        return devices.Values
            .OrderBy(device => device.Index)
            .ToArray();
    }

    private static bool TryParseGpuDeviceLine(
        string line,
        out int index,
        out string name)
    {
        index = -1;
        name = string.Empty;

        if (string.IsNullOrWhiteSpace(line) || line[0] != '[')
        {
            return false;
        }

        var closingBracketIndex = line.IndexOf(']');
        if (closingBracketIndex <= 2)
        {
            return false;
        }

        var header = line.Substring(1, closingBracketIndex - 1);
        var separatorIndex = header.IndexOf(' ');
        if (separatorIndex <= 0)
        {
            return false;
        }

        if (!int.TryParse(header.Substring(0, separatorIndex), out index))
        {
            return false;
        }

        name = header.Substring(separatorIndex + 1).Trim();
        return !string.IsNullOrWhiteSpace(name);
    }

    private static AiExecutionSupportStatus CreateGpuProbeStatus(
        ProbeProcessResult result,
        string outputPath)
    {
        if (result.WasSuccessful && File.Exists(outputPath))
        {
            return new AiExecutionSupportStatus
            {
                State = AiExecutionSupportState.Available
            };
        }

        return new AiExecutionSupportStatus
        {
            State = AiExecutionSupportState.Unavailable,
            DiagnosticMessage = SummarizeDiagnostic(result)
        };
    }

    private static string BuildUnsupportedRealEsrganCpuDiagnostic(ProbeProcessResult result)
    {
        const string fallbackMessage = "invalid gpu device";
        var invalidGpuLine = result.CombinedOutput
            .Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries)
            .Select(line => line.Trim())
            .FirstOrDefault(line => line.Contains(fallbackMessage, StringComparison.OrdinalIgnoreCase));

        if (!string.IsNullOrWhiteSpace(invalidGpuLine))
        {
            return invalidGpuLine;
        }

        return fallbackMessage;
    }

    private static bool IsPriorityDiagnosticLine(string line) =>
        line.Contains("invalid gpu device", StringComparison.OrdinalIgnoreCase) ||
        line.Contains("error", StringComparison.OrdinalIgnoreCase) ||
        line.Contains("failed", StringComparison.OrdinalIgnoreCase) ||
        line.Contains("unsupported", StringComparison.OrdinalIgnoreCase);

    private static AiExecutionSupportStatus CreateProbeFailedStatus(string diagnosticMessage) =>
        new()
        {
            State = AiExecutionSupportState.ProbeFailed,
            DiagnosticMessage = diagnosticMessage ?? string.Empty
        };

    private static AiExecutionSupportStatus CreateMissingRuntimeStatus(AiRuntimeDescriptor descriptor) =>
        new()
        {
            State = descriptor.Availability == AiRuntimeAvailability.Missing
                ? AiExecutionSupportState.MissingRuntime
                : AiExecutionSupportState.ProbeFailed,
            DiagnosticMessage = descriptor.AvailabilityReason
        };

    private static void TryDeleteDirectory(string directoryPath)
    {
        try
        {
            if (Directory.Exists(directoryPath))
            {
                Directory.Delete(directoryPath, recursive: true);
            }
        }
        catch
        {
        }
    }

    private static void TryKill(Process process)
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

    private sealed record ProbeProcessResult(
        int ExitCode,
        bool TimedOut,
        string StandardOutput,
        string StandardError)
    {
        public bool WasSuccessful => ExitCode == 0 && !TimedOut;

        public string CombinedOutput =>
            string.IsNullOrWhiteSpace(StandardOutput)
                ? StandardError
                : string.IsNullOrWhiteSpace(StandardError)
                    ? StandardOutput
                    : $"{StandardOutput}{Environment.NewLine}{StandardError}";
    }

    private sealed record DiscoveredGpuDevice(int Index, string Name);
}
