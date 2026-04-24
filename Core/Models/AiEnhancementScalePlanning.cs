using System;
using System.Collections.Generic;
using System.Linq;

namespace Vidvix.Core.Models;

public sealed class AiEnhancementScalePlan
{
    public AiEnhancementScalePlan(
        IReadOnlyList<int> passScales,
        int requestedScale,
        int achievedScale)
    {
        ArgumentNullException.ThrowIfNull(passScales);
        if (passScales.Count == 0)
        {
            throw new ArgumentException("At least one enhancement pass is required.", nameof(passScales));
        }

        if (requestedScale < 2 || requestedScale > 16)
        {
            throw new ArgumentOutOfRangeException(nameof(requestedScale));
        }

        if (achievedScale < requestedScale)
        {
            throw new ArgumentOutOfRangeException(nameof(achievedScale));
        }

        PassScales = passScales.ToArray();
        RequestedScale = requestedScale;
        AchievedScale = achievedScale;
    }

    public IReadOnlyList<int> PassScales { get; }

    public int RequestedScale { get; }

    public int AchievedScale { get; }

    public int PassCount => PassScales.Count;

    public bool RequiresDownscale => AchievedScale != RequestedScale;
}

public static class AiEnhancementScalePlanner
{
    private const int MaximumTargetScale = 16;

    public static AiEnhancementScalePlan BuildPlan(
        IReadOnlyList<int> nativeScaleFactors,
        int requestedScale)
    {
        ArgumentNullException.ThrowIfNull(nativeScaleFactors);
        if (requestedScale < 2 || requestedScale > MaximumTargetScale)
        {
            throw new ArgumentOutOfRangeException(nameof(requestedScale));
        }

        var orderedNativeScales = nativeScaleFactors
            .Where(scale => scale >= 2)
            .Distinct()
            .OrderByDescending(scale => scale)
            .ToArray();
        if (orderedNativeScales.Length == 0)
        {
            throw new InvalidOperationException("No valid native enhancement scales are available.");
        }

        var candidates = new List<IReadOnlyList<int>>();
        BuildCandidates(
            product: 1,
            orderedNativeScales,
            new List<int>(),
            candidates);

        var exactCandidate = candidates
            .Where(candidate => Multiply(candidate) == requestedScale)
            .OrderBy(candidate => candidate.Count)
            .FirstOrDefault();
        if (exactCandidate is not null)
        {
            return new AiEnhancementScalePlan(exactCandidate, requestedScale, requestedScale);
        }

        var overscaleCandidate = candidates
            .Where(candidate => Multiply(candidate) >= requestedScale)
            .OrderBy(candidate => Multiply(candidate))
            .ThenBy(candidate => candidate.Count)
            .FirstOrDefault();
        if (overscaleCandidate is not null)
        {
            return new AiEnhancementScalePlan(
                overscaleCandidate,
                requestedScale,
                Multiply(overscaleCandidate));
        }

        throw new InvalidOperationException("Unable to build an enhancement scale plan for the requested target.");
    }

    private static void BuildCandidates(
        int product,
        IReadOnlyList<int> nativeScales,
        List<int> currentPasses,
        List<IReadOnlyList<int>> candidates)
    {
        if (currentPasses.Count > 0)
        {
            candidates.Add(currentPasses.ToArray());
        }

        if (product >= MaximumTargetScale || currentPasses.Count >= 4)
        {
            return;
        }

        foreach (var nativeScale in nativeScales)
        {
            var nextProduct = product * nativeScale;
            if (nextProduct > MaximumTargetScale)
            {
                continue;
            }

            currentPasses.Add(nativeScale);
            BuildCandidates(nextProduct, nativeScales, currentPasses, candidates);
            currentPasses.RemoveAt(currentPasses.Count - 1);
        }
    }

    private static int Multiply(IReadOnlyList<int> values)
    {
        var product = 1;
        for (var index = 0; index < values.Count; index++)
        {
            product *= values[index];
        }

        return product;
    }
}
