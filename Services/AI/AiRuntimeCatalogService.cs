using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Vidvix.Core.Interfaces;
using Vidvix.Core.Models;
using Vidvix.Utils;

namespace Vidvix.Services.AI;

public sealed class AiRuntimeCatalogService : IAiRuntimeCatalogService
{
    private readonly ApplicationConfiguration _configuration;
    private readonly ILogger _logger;
    private readonly RifeRuntimeParser _rifeRuntimeParser;
    private readonly RealEsrganRuntimeParser _realEsrganRuntimeParser;
    private readonly AiRuntimeProbeExecutor _probeExecutor;
    private readonly SemaphoreSlim _syncLock = new(1, 1);
    private AiRuntimeCatalog? _cachedCatalog;
    private bool _hasRifeExecutionSupport;
    private bool _hasRealEsrganExecutionSupport;

    public AiRuntimeCatalogService(ApplicationConfiguration configuration, ILogger logger)
    {
        _configuration = configuration ?? throw new ArgumentNullException(nameof(configuration));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _rifeRuntimeParser = new RifeRuntimeParser(configuration);
        _realEsrganRuntimeParser = new RealEsrganRuntimeParser(configuration);
        _probeExecutor = new AiRuntimeProbeExecutor(configuration, logger);
    }

    public async Task<AiRuntimeCatalog> GetCatalogAsync(CancellationToken cancellationToken = default)
    {
        if (_cachedCatalog is not null)
        {
            return _cachedCatalog;
        }

        await _syncLock.WaitAsync(cancellationToken).ConfigureAwait(false);

        try
        {
            if (_cachedCatalog is not null)
            {
                return _cachedCatalog;
            }

            _cachedCatalog = BuildCatalog();
            _logger.Log(LogLevel.Info, "AI runtime catalog metadata prepared successfully.");
            return _cachedCatalog;
        }
        finally
        {
            _syncLock.Release();
        }
    }

    public async Task<AiRuntimeCatalog> EnsureExecutionSupportAsync(
        AiRuntimeKind runtimeKind,
        CancellationToken cancellationToken = default)
    {
        await _syncLock.WaitAsync(cancellationToken).ConfigureAwait(false);

        try
        {
            _cachedCatalog ??= BuildCatalog();

            switch (runtimeKind)
            {
                case AiRuntimeKind.Rife when !_hasRifeExecutionSupport:
                    _cachedCatalog = _cachedCatalog with
                    {
                        Rife = await InspectRifeAsync(_cachedCatalog.Rife, cancellationToken).ConfigureAwait(false)
                    };
                    _hasRifeExecutionSupport = true;
                    _logger.Log(LogLevel.Info, "RIFE execution support inspection completed.");
                    break;
                case AiRuntimeKind.RealEsrgan when !_hasRealEsrganExecutionSupport:
                    _cachedCatalog = _cachedCatalog with
                    {
                        RealEsrgan = await InspectRealEsrganAsync(_cachedCatalog.RealEsrgan, cancellationToken).ConfigureAwait(false)
                    };
                    _hasRealEsrganExecutionSupport = true;
                    _logger.Log(LogLevel.Info, "Real-ESRGAN execution support inspection completed.");
                    break;
            }

            return _cachedCatalog;
        }
        finally
        {
            _syncLock.Release();
        }
    }

    private AiRuntimeCatalog BuildCatalog()
    {
        var packageRootRelativePath = Path.Combine(
            _configuration.RuntimeDirectoryName,
            _configuration.AiRuntimeDirectoryName);
        var packageRootPath = ApplicationPaths.CombineFromExecutableDirectory(
            _configuration.RuntimeDirectoryName,
            _configuration.AiRuntimeDirectoryName);
        var licensesRootPath = Path.Combine(packageRootPath, _configuration.AiLicensesDirectoryName);
        var manifestRootPath = Path.Combine(packageRootPath, _configuration.AiManifestsDirectoryName);
        var rifeDescriptor = FinalizeParsedDescriptor(
            _rifeRuntimeParser.Parse(packageRootPath, licensesRootPath, manifestRootPath));
        var realEsrganDescriptor = FinalizeParsedDescriptor(
            _realEsrganRuntimeParser.Parse(packageRootPath, licensesRootPath, manifestRootPath));

        return new AiRuntimeCatalog
        {
            PackageRootRelativePath = packageRootRelativePath,
            PackageRootPath = packageRootPath,
            LicensesRootPath = licensesRootPath,
            ManifestRootPath = manifestRootPath,
            Rife = rifeDescriptor,
            RealEsrgan = realEsrganDescriptor
        };
    }

    private static AiRuntimeDescriptor FinalizeParsedDescriptor(AiRuntimeDescriptor descriptor)
    {
        if (descriptor.IsAvailable)
        {
            return descriptor;
        }

        var missingStatus = CreateMissingRuntimeStatus(descriptor);
        return descriptor with
        {
            GpuSupport = missingStatus,
            CpuSupport = missingStatus
        };
    }

    private async Task<AiRuntimeDescriptor> InspectRifeAsync(
        AiRuntimeDescriptor descriptor,
        CancellationToken cancellationToken)
    {
        if (!descriptor.IsAvailable)
        {
            return descriptor with
            {
                GpuSupport = CreateMissingRuntimeStatus(descriptor),
                CpuSupport = CreateMissingRuntimeStatus(descriptor),
                GpuDevices = Array.Empty<AiRuntimeGpuDeviceDescriptor>()
            };
        }

        var gpuDevices = await _probeExecutor.ProbeRifeGpuDevicesAsync(descriptor, cancellationToken).ConfigureAwait(false);
        var cpuSupport = await _probeExecutor.ProbeRifeCpuAsync(descriptor, cancellationToken).ConfigureAwait(false);
        var gpuSupport = await BuildGpuSupportStatusAsync(
            gpuDevices,
            fallbackSupportFactory: () => _probeExecutor.ProbeRifeGpuAsync(descriptor, cancellationToken)).ConfigureAwait(false);
        return descriptor with
        {
            GpuSupport = gpuSupport,
            CpuSupport = cpuSupport,
            GpuDevices = gpuDevices
        };
    }

    private async Task<AiRuntimeDescriptor> InspectRealEsrganAsync(
        AiRuntimeDescriptor descriptor,
        CancellationToken cancellationToken)
    {
        if (!descriptor.IsAvailable)
        {
            return descriptor with
            {
                GpuSupport = CreateMissingRuntimeStatus(descriptor),
                CpuSupport = CreateMissingRuntimeStatus(descriptor),
                GpuDevices = Array.Empty<AiRuntimeGpuDeviceDescriptor>()
            };
        }

        var gpuDevices = await _probeExecutor.ProbeRealEsrganGpuDevicesAsync(descriptor, cancellationToken).ConfigureAwait(false);
        var cpuSupport = await _probeExecutor.ProbeRealEsrganCpuAsync(descriptor, cancellationToken).ConfigureAwait(false);
        var gpuSupport = await BuildGpuSupportStatusAsync(
            gpuDevices,
            fallbackSupportFactory: () => _probeExecutor.ProbeRealEsrganGpuAsync(descriptor, cancellationToken)).ConfigureAwait(false);
        return descriptor with
        {
            GpuSupport = gpuSupport,
            CpuSupport = cpuSupport,
            GpuDevices = gpuDevices
        };
    }

    private static async Task<AiExecutionSupportStatus> BuildGpuSupportStatusAsync(
        IReadOnlyList<AiRuntimeGpuDeviceDescriptor> gpuDevices,
        Func<Task<AiExecutionSupportStatus>> fallbackSupportFactory)
    {
        ArgumentNullException.ThrowIfNull(gpuDevices);
        ArgumentNullException.ThrowIfNull(fallbackSupportFactory);

        if (gpuDevices.Any(device => device.IsAvailable))
        {
            return new AiExecutionSupportStatus
            {
                State = AiExecutionSupportState.Available
            };
        }

        var diagnostics = gpuDevices
            .Select(device => string.IsNullOrWhiteSpace(device.Support.DiagnosticMessage)
                ? string.Empty
                : $"{device.Name}: {device.Support.DiagnosticMessage}")
            .Where(message => !string.IsNullOrWhiteSpace(message))
            .Take(3)
            .ToArray();
        if (diagnostics.Length > 0)
        {
            return new AiExecutionSupportStatus
            {
                State = AiExecutionSupportState.Unavailable,
                DiagnosticMessage = string.Join(" | ", diagnostics)
            };
        }

        return await fallbackSupportFactory().ConfigureAwait(false);
    }

    private static AiExecutionSupportStatus CreateMissingRuntimeStatus(AiRuntimeDescriptor descriptor) =>
        new()
        {
            State = descriptor.Availability == AiRuntimeAvailability.Missing
                ? AiExecutionSupportState.MissingRuntime
                : AiExecutionSupportState.ProbeFailed,
            DiagnosticMessage = descriptor.AvailabilityReason
        };
}
