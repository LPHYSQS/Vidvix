using System;
using Vidvix.Core.Models;

namespace Vidvix.Services.AI;

internal static class AiGpuDeviceClassifier
{
    public static AiGpuDeviceKind Classify(string deviceName)
    {
        if (string.IsNullOrWhiteSpace(deviceName))
        {
            return AiGpuDeviceKind.UnknownGpu;
        }

        var upperName = deviceName.ToUpperInvariant();

        if (upperName.Contains("NVIDIA", StringComparison.Ordinal))
        {
            return AiGpuDeviceKind.DiscreteGpu;
        }

        if (upperName.Contains("INTEL ARC", StringComparison.Ordinal) ||
            upperName.Contains("INTEL(R) ARC", StringComparison.Ordinal))
        {
            return AiGpuDeviceKind.DiscreteGpu;
        }

        if (upperName.Contains("INTEL", StringComparison.Ordinal))
        {
            return AiGpuDeviceKind.IntegratedGpu;
        }

        if (upperName.Contains("ADRENO", StringComparison.Ordinal) ||
            upperName.Contains("QUALCOMM", StringComparison.Ordinal))
        {
            return AiGpuDeviceKind.IntegratedGpu;
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
                return AiGpuDeviceKind.DiscreteGpu;
            }

            if (upperName.Contains("RADEON(TM) GRAPHICS", StringComparison.Ordinal) ||
                upperName.Contains("RADEON GRAPHICS", StringComparison.Ordinal) ||
                upperName.Contains("VEGA", StringComparison.Ordinal) ||
                upperName.Contains("680M", StringComparison.Ordinal) ||
                upperName.Contains("760M", StringComparison.Ordinal) ||
                upperName.Contains("780M", StringComparison.Ordinal) ||
                upperName.Contains("880M", StringComparison.Ordinal))
            {
                return AiGpuDeviceKind.IntegratedGpu;
            }

            return AiGpuDeviceKind.UnknownGpu;
        }

        return AiGpuDeviceKind.UnknownGpu;
    }

    public static int GetPriority(AiGpuDeviceKind kind) =>
        kind switch
        {
            AiGpuDeviceKind.DiscreteGpu => 3,
            AiGpuDeviceKind.IntegratedGpu => 2,
            _ => 1
        };
}
