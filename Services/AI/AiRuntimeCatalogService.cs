using System;
using System.IO;
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

            var packageRootRelativePath = Path.Combine(
                _configuration.RuntimeDirectoryName,
                _configuration.AiRuntimeDirectoryName);
            var packageRootPath = ApplicationPaths.CombineFromExecutableDirectory(
                _configuration.RuntimeDirectoryName,
                _configuration.AiRuntimeDirectoryName);
            var licensesRootPath = Path.Combine(packageRootPath, _configuration.AiLicensesDirectoryName);
            var manifestRootPath = Path.Combine(packageRootPath, _configuration.AiManifestsDirectoryName);
            var rifeDescriptor = _rifeRuntimeParser.Parse(packageRootPath, licensesRootPath, manifestRootPath);
            var realEsrganDescriptor = _realEsrganRuntimeParser.Parse(packageRootPath, licensesRootPath, manifestRootPath);
            var inspectedRifeDescriptor = await InspectRifeAsync(rifeDescriptor, cancellationToken).ConfigureAwait(false);
            var inspectedRealEsrganDescriptor = await InspectRealEsrganAsync(realEsrganDescriptor, cancellationToken).ConfigureAwait(false);

            _cachedCatalog = new AiRuntimeCatalog
            {
                PackageRootRelativePath = packageRootRelativePath,
                PackageRootPath = packageRootPath,
                LicensesRootPath = licensesRootPath,
                ManifestRootPath = manifestRootPath,
                Rife = inspectedRifeDescriptor,
                RealEsrgan = inspectedRealEsrganDescriptor
            };

            _logger.Log(LogLevel.Info, "AI runtime catalog prepared successfully.");
            return _cachedCatalog;
        }
        finally
        {
            _syncLock.Release();
        }
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
                CpuSupport = CreateMissingRuntimeStatus(descriptor)
            };
        }

        var gpuSupport = await _probeExecutor.ProbeRifeGpuAsync(descriptor, cancellationToken).ConfigureAwait(false);
        var cpuSupport = await _probeExecutor.ProbeRifeCpuAsync(descriptor, cancellationToken).ConfigureAwait(false);
        return descriptor with
        {
            GpuSupport = gpuSupport,
            CpuSupport = cpuSupport
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
                CpuSupport = CreateMissingRuntimeStatus(descriptor)
            };
        }

        var gpuSupport = await _probeExecutor.ProbeRealEsrganGpuAsync(descriptor, cancellationToken).ConfigureAwait(false);
        var cpuSupport = await _probeExecutor.ProbeRealEsrganCpuAsync(descriptor, cancellationToken).ConfigureAwait(false);
        return descriptor with
        {
            GpuSupport = gpuSupport,
            CpuSupport = cpuSupport
        };
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
