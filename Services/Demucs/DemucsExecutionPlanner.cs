using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Vidvix.Core.Interfaces;
using Vidvix.Core.Models;
using Vidvix.Services;
using Vidvix.Utils;

namespace Vidvix.Services.Demucs;

public sealed class DemucsExecutionPlanner : IDemucsExecutionPlanner
{
    private static readonly JsonSerializerOptions SerializerOptions = new()
    {
        PropertyNameCaseInsensitive = true
    };

    private readonly ApplicationConfiguration _configuration;
    private readonly IDemucsRuntimeService _demucsRuntimeService;
    private readonly ILogger _logger;

    public DemucsExecutionPlanner(
        ApplicationConfiguration configuration,
        IDemucsRuntimeService demucsRuntimeService,
        ILogger logger)
    {
        _configuration = configuration ?? throw new ArgumentNullException(nameof(configuration));
        _demucsRuntimeService = demucsRuntimeService ?? throw new ArgumentNullException(nameof(demucsRuntimeService));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    public async Task<IReadOnlyList<DemucsExecutionPlan>> ResolveExecutionPlansAsync(
        DemucsAccelerationMode accelerationMode,
        CancellationToken cancellationToken = default)
    {
        var launcherScriptPath = ResolveLauncherScriptPath();

        if (accelerationMode == DemucsAccelerationMode.Cpu)
        {
            var cpuRuntime = await _demucsRuntimeService
                .EnsureAvailableAsync(cancellationToken, runtimeVariant: DemucsRuntimeVariant.Cpu)
                .ConfigureAwait(false);

            return new[]
            {
                CreateCpuPlan(
                    accelerationMode,
                    cpuRuntime,
                    launcherScriptPath,
                    "\u5f53\u524d\u4f7f\u7528 CPU \u6a21\u5f0f\u62c6\u97f3\uff0c\u517c\u5bb9\u6027\u6700\u9ad8\u3002")
            };
        }

        var executionPlans = new List<DemucsExecutionPlan>();
        var encounteredGpuProbeFailure = false;
        var discoveredCudaDiscreteGpu = false;

        try
        {
            var cudaRuntime = await _demucsRuntimeService
                .EnsureAvailableAsync(cancellationToken, runtimeVariant: DemucsRuntimeVariant.Cuda)
                .ConfigureAwait(false);

            var cudaProbe = await ProbeDevicesAsync(
                cudaRuntime,
                launcherScriptPath,
                "probe-cuda",
                cancellationToken).ConfigureAwait(false);

            var cudaDevices = SelectCudaDevices(cudaProbe);
            if (cudaDevices.Count > 0)
            {
                discoveredCudaDiscreteGpu = true;

                for (var index = 0; index < cudaDevices.Count; index++)
                {
                    var device = cudaDevices[index];
                    executionPlans.Add(CreateGpuPlan(
                        accelerationMode,
                        cudaRuntime,
                        launcherScriptPath,
                        device.Kind,
                        device.Name,
                        device.DeviceArgument,
                        BuildCudaAttemptSummary(device.Name, index)));
                }
            }
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            encounteredGpuProbeFailure = true;
            _logger.Log(LogLevel.Warning, "Demucs CUDA probe failed; will continue with fallback devices.", exception);
        }

        try
        {
            var directMlRuntime = await _demucsRuntimeService
                .EnsureAvailableAsync(cancellationToken, runtimeVariant: DemucsRuntimeVariant.DirectMl)
                .ConfigureAwait(false);

            var directMlProbe = await ProbeDevicesAsync(
                directMlRuntime,
                launcherScriptPath,
                "probe-directml",
                cancellationToken).ConfigureAwait(false);

            var directMlDevices = SelectPreferredDirectMlDevices(
                directMlProbe,
                includeDiscreteDevices: !discoveredCudaDiscreteGpu);

            for (var index = 0; index < directMlDevices.Count; index++)
            {
                var device = directMlDevices[index];
                executionPlans.Add(CreateGpuPlan(
                    accelerationMode,
                    directMlRuntime,
                    launcherScriptPath,
                    device.Kind,
                    device.Name,
                    device.DeviceArgument,
                    BuildDirectMlAttemptSummary(device.Kind, device.Name, index, discoveredCudaDiscreteGpu)));
            }
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            encounteredGpuProbeFailure = true;
            _logger.Log(LogLevel.Warning, "Demucs DirectML probe failed; will continue with CPU fallback.", exception);
        }

        var fallbackCpuRuntime = await _demucsRuntimeService
            .EnsureAvailableAsync(cancellationToken, runtimeVariant: DemucsRuntimeVariant.Cpu)
            .ConfigureAwait(false);

        if (executionPlans.Count == 0)
        {
            var fallbackSummary = encounteredGpuProbeFailure
                ? "\u5df2\u9009\u62e9 GPU \u4f18\u5148\u6a21\u5f0f\uff0cGPU \u8fd0\u884c\u65f6\u63a2\u6d4b\u5931\u8d25\uff0c\u5df2\u81ea\u52a8\u56de\u9000\u5230 CPU\u3002"
                : "\u5df2\u9009\u62e9 GPU \u4f18\u5148\u6a21\u5f0f\uff0c\u4f46\u672a\u68c0\u6d4b\u5230\u53ef\u7528\u72ec\u663e\u6216\u6838\u663e\uff0c\u5df2\u81ea\u52a8\u56de\u9000\u5230 CPU\u3002";

            return new[]
            {
                CreateCpuPlan(
                    accelerationMode,
                    fallbackCpuRuntime,
                    launcherScriptPath,
                    fallbackSummary)
            };
        }

        executionPlans.Add(CreateCpuPlan(
            accelerationMode,
            fallbackCpuRuntime,
            launcherScriptPath,
            "\u5df2\u9009\u62e9 GPU \u4f18\u5148\u6a21\u5f0f\uff0cGPU \u6267\u884c\u672a\u6210\u529f\uff0c\u5df2\u81ea\u52a8\u56de\u9000\u5230 CPU\u3002"));

        return executionPlans;
    }

    private string ResolveLauncherScriptPath()
    {
        var launcherScriptPath = Path.Combine(
            ApplicationPaths.ExecutableDirectoryPath,
            _configuration.RuntimeDirectoryName,
            _configuration.DemucsDirectoryName,
            _configuration.DemucsScriptsDirectoryName,
            _configuration.DemucsRunnerScriptFileName);

        if (!File.Exists(launcherScriptPath))
        {
            throw new InvalidOperationException(
                $"Demucs launcher script was not found: {Path.Combine("Tools", _configuration.DemucsDirectoryName, _configuration.DemucsScriptsDirectoryName, _configuration.DemucsRunnerScriptFileName)}");
        }

        return launcherScriptPath;
    }

    private async Task<GenericProbeResult> ProbeDevicesAsync(
        DemucsRuntimeResolution runtimeResolution,
        string launcherScriptPath,
        string probeCommand,
        CancellationToken cancellationToken)
    {
        using var process = new Process
        {
            StartInfo = new ProcessStartInfo
            {
                FileName = runtimeResolution.PythonExecutablePath,
                WorkingDirectory = runtimeResolution.RuntimeRootPath,
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                StandardOutputEncoding = Encoding.UTF8,
                StandardErrorEncoding = Encoding.UTF8
            }
        };
        var processId = 0;

        process.StartInfo.Environment["PYTHONUTF8"] = "1";
        process.StartInfo.ArgumentList.Add(launcherScriptPath);
        process.StartInfo.ArgumentList.Add(probeCommand);

        if (!process.Start())
        {
            throw new InvalidOperationException($"Demucs probe process could not be started: {probeCommand}");
        }

        processId = process.Id;
        using var cancellationRegistration = ExternalProcessTermination.RegisterTermination(process, cancellationToken);

        var standardOutputTask = process.StandardOutput.ReadToEndAsync();
        var standardErrorTask = process.StandardError.ReadToEndAsync();

        try
        {
            await process.WaitForExitAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            await ExternalProcessTermination.WaitForTerminationAsync(
                    process,
                    processId,
                    _logger,
                    $"Demucs 设备探测取消后，探测进程在宽限时间内仍未完全退出：{probeCommand}")
                .ConfigureAwait(false);
            await Task.WhenAll(standardOutputTask, standardErrorTask).ConfigureAwait(false);
            throw;
        }

        var standardOutput = await standardOutputTask.ConfigureAwait(false);
        var standardError = await standardErrorTask.ConfigureAwait(false);

        if (process.ExitCode != 0)
        {
            var failureDetail = !string.IsNullOrWhiteSpace(standardError)
                ? standardError.Trim()
                : standardOutput.Trim();

            throw new InvalidOperationException(
                string.IsNullOrWhiteSpace(failureDetail)
                    ? $"Demucs probe failed with exit code {process.ExitCode}: {probeCommand}"
                    : $"Demucs probe failed with exit code {process.ExitCode}: {probeCommand}. {failureDetail}");
        }

        var probeResult = JsonSerializer.Deserialize<GenericProbeResult>(standardOutput, SerializerOptions);
        if (probeResult is null)
        {
            throw new InvalidOperationException($"Demucs probe returned an empty payload: {probeCommand}");
        }

        return probeResult;
    }

    private static IReadOnlyList<ResolvedGpuDevice> SelectCudaDevices(GenericProbeResult probeResult)
    {
        if (!probeResult.IsAvailable || probeResult.DeviceCount <= 0 || probeResult.Devices.Count == 0)
        {
            return Array.Empty<ResolvedGpuDevice>();
        }

        return probeResult.Devices
            .Where(device => !string.IsNullOrWhiteSpace(device.Name))
            .Select(device => new ResolvedGpuDevice(
                device.Index,
                device.Name,
                string.IsNullOrWhiteSpace(device.Device) ? $"cuda:{device.Index}" : device.Device,
                DemucsExecutionDeviceKind.DiscreteGpu,
                probeResult.DefaultDevice is int defaultIndex && defaultIndex == device.Index))
            .OrderByDescending(device => device.IsDefaultDevice)
            .ThenBy(device => device.Index)
            .ToArray();
    }

    private static IReadOnlyList<ResolvedGpuDevice> SelectPreferredDirectMlDevices(
        GenericProbeResult probeResult,
        bool includeDiscreteDevices)
    {
        if (!probeResult.IsAvailable || probeResult.DeviceCount <= 0 || probeResult.Devices.Count == 0)
        {
            return Array.Empty<ResolvedGpuDevice>();
        }

        return probeResult.Devices
            .Where(device => !string.IsNullOrWhiteSpace(device.Name))
            .Where(device => !device.Name.Contains("Microsoft Basic Render Driver", StringComparison.OrdinalIgnoreCase))
            .Select(device => new ResolvedGpuDevice(
                device.Index,
                device.Name,
                string.IsNullOrWhiteSpace(device.Device) ? $"privateuseone:{device.Index}" : device.Device,
                ClassifyDeviceKind(device.Name),
                probeResult.DefaultDevice is int defaultIndex && defaultIndex == device.Index))
            .Where(device => includeDiscreteDevices || device.Kind != DemucsExecutionDeviceKind.DiscreteGpu)
            .OrderByDescending(device => GetKindPriority(device.Kind))
            .ThenByDescending(device => device.IsDefaultDevice)
            .ThenBy(device => device.Index)
            .ToArray();
    }

    private static DemucsExecutionDeviceKind ClassifyDeviceKind(string deviceName)
    {
        var upperName = deviceName.ToUpperInvariant();

        if (upperName.Contains("NVIDIA", StringComparison.Ordinal))
        {
            return DemucsExecutionDeviceKind.DiscreteGpu;
        }

        if (upperName.Contains("INTEL ARC", StringComparison.Ordinal) ||
            upperName.Contains("INTEL(R) ARC", StringComparison.Ordinal))
        {
            return DemucsExecutionDeviceKind.DiscreteGpu;
        }

        if (upperName.Contains("INTEL", StringComparison.Ordinal))
        {
            return DemucsExecutionDeviceKind.IntegratedGpu;
        }

        if (upperName.Contains("ADRENO", StringComparison.Ordinal) ||
            upperName.Contains("QUALCOMM", StringComparison.Ordinal))
        {
            return DemucsExecutionDeviceKind.IntegratedGpu;
        }

        if (upperName.Contains("AMD", StringComparison.Ordinal) ||
            upperName.Contains("RADEON", StringComparison.Ordinal) ||
            upperName.Contains("ATI", StringComparison.Ordinal))
        {
            if (upperName.Contains(" RX ", StringComparison.Ordinal) ||
                upperName.StartsWith("RX ", StringComparison.Ordinal) ||
                upperName.Contains("RADEON RX", StringComparison.Ordinal) ||
                upperName.Contains("RADEON PRO", StringComparison.Ordinal) ||
                upperName.Contains("FIREPRO", StringComparison.Ordinal))
            {
                return DemucsExecutionDeviceKind.DiscreteGpu;
            }

            if (upperName.Contains("RADEON(TM) GRAPHICS", StringComparison.Ordinal) ||
                upperName.Contains("RADEON GRAPHICS", StringComparison.Ordinal) ||
                upperName.Contains("VEGA", StringComparison.Ordinal) ||
                upperName.Contains("680M", StringComparison.Ordinal) ||
                upperName.Contains("760M", StringComparison.Ordinal) ||
                upperName.Contains("780M", StringComparison.Ordinal) ||
                upperName.Contains("880M", StringComparison.Ordinal))
            {
                return DemucsExecutionDeviceKind.IntegratedGpu;
            }

            return DemucsExecutionDeviceKind.UnknownGpu;
        }

        return DemucsExecutionDeviceKind.UnknownGpu;
    }

    private static int GetKindPriority(DemucsExecutionDeviceKind kind) =>
        kind switch
        {
            DemucsExecutionDeviceKind.DiscreteGpu => 3,
            DemucsExecutionDeviceKind.IntegratedGpu => 2,
            DemucsExecutionDeviceKind.UnknownGpu => 1,
            _ => 0
        };

    private static string BuildCudaAttemptSummary(string deviceName, int attemptIndex) =>
        attemptIndex == 0
            ? $"\u5df2\u68c0\u6d4b\u5230 NVIDIA \u72ec\u7acb\u663e\u5361\uff0c\u5f53\u524d\u5c06\u4f7f\u7528 CUDA \u62c6\u97f3\uff1a{deviceName}\u3002"
            : $"\u4e0a\u4e00\u5f20 NVIDIA \u72ec\u663e\u6267\u884c\u672a\u6210\u529f\uff0c\u5df2\u5207\u6362\u5230\u53e6\u4e00\u5f20\u72ec\u663e\u7ee7\u7eed\u62c6\u97f3\uff1a{deviceName}\u3002";

    private static string BuildDirectMlAttemptSummary(
        DemucsExecutionDeviceKind kind,
        string deviceName,
        int attemptIndex,
        bool discoveredCudaDiscreteGpu)
    {
        if (discoveredCudaDiscreteGpu)
        {
            return kind == DemucsExecutionDeviceKind.IntegratedGpu
                ? $"\u72ec\u663e CUDA \u6267\u884c\u672a\u6210\u529f\uff0c\u5df2\u56de\u9000\u5230\u6838\u663e\u7ee7\u7eed\u62c6\u97f3\uff1a{deviceName}\u3002"
                : $"\u72ec\u663e CUDA \u6267\u884c\u672a\u6210\u529f\uff0c\u5df2\u5c1d\u8bd5\u5176\u4ed6 DirectML \u8bbe\u5907\u7ee7\u7eed\u62c6\u97f3\uff1a{deviceName}\u3002";
        }

        return attemptIndex switch
        {
            0 when kind == DemucsExecutionDeviceKind.DiscreteGpu =>
                $"\u5df2\u68c0\u6d4b\u5230\u72ec\u7acb\u663e\u5361\uff0c\u5f53\u524d\u5c06\u4f7f\u7528 DirectML \u62c6\u97f3\uff1a{deviceName}\u3002",
            0 when kind == DemucsExecutionDeviceKind.IntegratedGpu =>
                $"\u672a\u68c0\u6d4b\u5230\u53ef\u7528\u72ec\u663e\uff0c\u5df2\u5207\u6362\u4e3a\u6838\u663e\u7ee7\u7eed\u62c6\u97f3\uff1a{deviceName}\u3002",
            0 =>
                $"\u5df2\u68c0\u6d4b\u5230\u53ef\u7528 GPU\uff0c\u5f53\u524d\u5c06\u4f7f\u7528 DirectML \u8bbe\u5907\u62c6\u97f3\uff1a{deviceName}\u3002",
            _ when kind == DemucsExecutionDeviceKind.DiscreteGpu =>
                $"\u4e0a\u4e00\u5f20 GPU \u6267\u884c\u672a\u6210\u529f\uff0c\u5df2\u5207\u6362\u5230\u53e6\u4e00\u5f20\u72ec\u663e\u7ee7\u7eed\u62c6\u97f3\uff1a{deviceName}\u3002",
            _ when kind == DemucsExecutionDeviceKind.IntegratedGpu =>
                $"\u4e0a\u4e00\u5f20 GPU \u6267\u884c\u672a\u6210\u529f\uff0c\u5df2\u56de\u9000\u5230\u6838\u663e\u7ee7\u7eed\u62c6\u97f3\uff1a{deviceName}\u3002",
            _ =>
                $"\u4e0a\u4e00\u5f20 GPU \u6267\u884c\u672a\u6210\u529f\uff0c\u5df2\u5c1d\u8bd5\u5176\u4ed6 GPU \u8bbe\u5907\u7ee7\u7eed\u62c6\u97f3\uff1a{deviceName}\u3002"
        };
    }

    private static DemucsExecutionPlan CreateCpuPlan(
        DemucsAccelerationMode requestedAccelerationMode,
        DemucsRuntimeResolution runtimeResolution,
        string launcherScriptPath,
        string resolutionSummary) =>
        new()
        {
            RequestedAccelerationMode = requestedAccelerationMode,
            SelectedDeviceKind = DemucsExecutionDeviceKind.Cpu,
            DeviceDisplayName = "CPU",
            DeviceArgument = "cpu",
            LauncherScriptPath = launcherScriptPath,
            ResolutionSummary = resolutionSummary,
            RuntimeResolution = runtimeResolution
        };

    private static DemucsExecutionPlan CreateGpuPlan(
        DemucsAccelerationMode requestedAccelerationMode,
        DemucsRuntimeResolution runtimeResolution,
        string launcherScriptPath,
        DemucsExecutionDeviceKind deviceKind,
        string deviceName,
        string deviceArgument,
        string resolutionSummary) =>
        new()
        {
            RequestedAccelerationMode = requestedAccelerationMode,
            SelectedDeviceKind = deviceKind,
            DeviceDisplayName = deviceName,
            DeviceArgument = deviceArgument,
            LauncherScriptPath = launcherScriptPath,
            ResolutionSummary = resolutionSummary,
            RuntimeResolution = runtimeResolution
        };

    private sealed class GenericProbeResult
    {
        public bool IsAvailable { get; init; }

        public int DeviceCount { get; init; }

        public int? DefaultDevice { get; init; }

        public IReadOnlyList<GenericProbeDevice> Devices { get; init; } = Array.Empty<GenericProbeDevice>();
    }

    private sealed class GenericProbeDevice
    {
        public int Index { get; init; }

        public string Name { get; init; } = string.Empty;

        public string Device { get; init; } = string.Empty;
    }

    private sealed record ResolvedGpuDevice(
        int Index,
        string Name,
        string DeviceArgument,
        DemucsExecutionDeviceKind Kind,
        bool IsDefaultDevice);
}
