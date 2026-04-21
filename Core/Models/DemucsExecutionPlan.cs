using System;

namespace Vidvix.Core.Models;

public sealed class DemucsExecutionPlan
{
    public required DemucsAccelerationMode RequestedAccelerationMode { get; init; }

    public required DemucsExecutionDeviceKind SelectedDeviceKind { get; init; }

    public required string DeviceDisplayName { get; init; }

    public required string DeviceArgument { get; init; }

    public required string LauncherScriptPath { get; init; }

    public required string ResolutionSummary { get; init; }

    public Func<string>? ResolutionSummaryResolver { get; init; }

    public required DemucsRuntimeResolution RuntimeResolution { get; init; }

    public bool UsesGpu =>
        SelectedDeviceKind is DemucsExecutionDeviceKind.DiscreteGpu
            or DemucsExecutionDeviceKind.IntegratedGpu
            or DemucsExecutionDeviceKind.UnknownGpu;

    public string ResolveResolutionSummary() => ResolutionSummaryResolver?.Invoke() ?? ResolutionSummary;
}
