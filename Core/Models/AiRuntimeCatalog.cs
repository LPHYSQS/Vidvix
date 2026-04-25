using System;
using System.Collections.Generic;

namespace Vidvix.Core.Models;

public enum AiRuntimeAvailability
{
    Available,
    Missing,
    Invalid
}

public enum AiRuntimeKind
{
    Rife,
    RealEsrgan
}

public enum AiExecutionSupportState
{
    Pending,
    Available,
    Unavailable,
    Unsupported,
    MissingRuntime,
    ProbeFailed
}

public sealed record AiExecutionSupportStatus
{
    public AiExecutionSupportState State { get; init; } = AiExecutionSupportState.Pending;

    public string DiagnosticMessage { get; init; } = string.Empty;

    public bool IsAvailable => State == AiExecutionSupportState.Available;
}

public enum AiGpuDeviceKind
{
    DiscreteGpu,
    IntegratedGpu,
    UnknownGpu
}

public sealed record AiRuntimeGpuDeviceDescriptor
{
    public int Index { get; init; }

    public string Name { get; init; } = string.Empty;

    public AiGpuDeviceKind Kind { get; init; } = AiGpuDeviceKind.UnknownGpu;

    public AiExecutionSupportStatus Support { get; init; } = new();

    public bool IsAvailable => Support.IsAvailable;
}

public sealed record AiRuntimeModelAssetDescriptor
{
    public string FileStem { get; init; } = string.Empty;

    public string DisplayName { get; init; } = string.Empty;

    public int? NativeScale { get; init; }

    public string ConfigPath { get; init; } = string.Empty;

    public string WeightPath { get; init; } = string.Empty;
}

public sealed record AiRuntimeModelDescriptor
{
    public string Id { get; init; } = string.Empty;

    public string DisplayName { get; init; } = string.Empty;

    public string RuntimeModelName { get; init; } = string.Empty;

    public string Version { get; init; } = string.Empty;

    public string PreparedDirectoryName { get; init; } = string.Empty;

    public IReadOnlyList<int> NativeScaleFactors { get; init; } = Array.Empty<int>();

    public IReadOnlyList<AiRuntimeModelAssetDescriptor> Assets { get; init; } =
        Array.Empty<AiRuntimeModelAssetDescriptor>();
}

public sealed record AiRuntimeDescriptor
{
    public string Id { get; init; } = string.Empty;

    public string DisplayName { get; init; } = string.Empty;

    public string RuntimeVersion { get; init; } = string.Empty;

    public string ReleasePublishedAt { get; init; } = string.Empty;

    public string PackageRelativePath { get; init; } = string.Empty;

    public string RuntimeRootPath { get; init; } = string.Empty;

    public string ExecutablePath { get; init; } = string.Empty;

    public string ManifestPath { get; init; } = string.Empty;

    public IReadOnlyList<string> DependencyFilePaths { get; init; } = Array.Empty<string>();

    public IReadOnlyList<string> LicenseFilePaths { get; init; } = Array.Empty<string>();

    public IReadOnlyList<AiRuntimeModelDescriptor> Models { get; init; } = Array.Empty<AiRuntimeModelDescriptor>();

    public AiRuntimeAvailability Availability { get; init; } = AiRuntimeAvailability.Missing;

    public string AvailabilityReason { get; init; } = string.Empty;

    public AiExecutionSupportStatus GpuSupport { get; init; } = new();

    public AiExecutionSupportStatus CpuSupport { get; init; } = new();

    public IReadOnlyList<AiRuntimeGpuDeviceDescriptor> GpuDevices { get; init; } =
        Array.Empty<AiRuntimeGpuDeviceDescriptor>();

    public bool IsAvailable => Availability == AiRuntimeAvailability.Available;
}

public sealed record AiRuntimeCatalog
{
    public string PackageRootRelativePath { get; init; } = string.Empty;

    public string PackageRootPath { get; init; } = string.Empty;

    public string LicensesRootPath { get; init; } = string.Empty;

    public string ManifestRootPath { get; init; } = string.Empty;

    public AiRuntimeDescriptor Rife { get; init; } = new();

    public AiRuntimeDescriptor RealEsrgan { get; init; } = new();
}
