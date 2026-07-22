using GBX.NET.Engines.Game;

using MapValidationChecker.Core.Validation;

namespace MapValidationChecker.Cli.Evidence;

internal static class GpsEvidenceReader
{
    public static GpsEvidence? FindMatch(
        CGameCtnChallenge map,
        int authorTimeMs,
        int? maxDepth,
        int thresholdMs)
    {
        ArgumentNullException.ThrowIfNull(map);

        // Kept in the contract for compatibility with the CLI option. The current
        // matching paths are direct GBX structures; reflected traversal is diagnostic-only.
        _ = maxDepth;

        if (map.ClipGroupInGame is null)
            return null;

        foreach (var candidate in GpsCandidateExtractor.EnumerateMediaBlockEntityChunkCandidates(map))
        {
            if (candidate.TimeMs == authorTimeMs)
            {
                return new GpsEvidence(
                    candidate.TimeMs,
                    0,
                    candidate.Source,
                    GpsMatchMethod.U05Exact,
                    GpsMatchKind.ExactMatch);
            }
        }

        var recordDataCandidates = GpsCandidateExtractor
            .EnumerateRecordDataCandidates(map)
            .ToList();
        var directMatch = FindBestThresholdMatch(
            recordDataCandidates,
            authorTimeMs,
            thresholdMs,
            static source => source.EndsWith(".U03", StringComparison.Ordinal));
        if (directMatch is not null)
            return directMatch;

        return FindBestThresholdMatch(
            recordDataCandidates,
            authorTimeMs,
            thresholdMs,
            static source => source.EndsWith(".U03MinusCountdown", StringComparison.Ordinal));
    }

    internal static GpsEvidence? FindBestThresholdMatch(
        IEnumerable<GpsCandidate> candidates,
        int authorTimeMs,
        int thresholdMs,
        Func<string, bool> sourceFilter)
    {
        ArgumentNullException.ThrowIfNull(candidates);
        ArgumentNullException.ThrowIfNull(sourceFilter);

        GpsCandidate? bestCandidate = null;
        var bestDelta = int.MaxValue;

        foreach (var candidate in candidates)
        {
            if (!sourceFilter(candidate.Source))
                continue;

            var delta = Math.Abs(candidate.TimeMs - authorTimeMs);
            if (delta > thresholdMs)
                continue;

            if (bestCandidate is null ||
                delta < bestDelta ||
                (delta == bestDelta &&
                 GetSourcePriority(candidate.Source) < GetSourcePriority(bestCandidate.Source)))
            {
                bestCandidate = candidate;
                bestDelta = delta;
            }
        }

        if (bestCandidate is null)
            return null;

        return new GpsEvidence(
            bestCandidate.TimeMs,
            bestDelta,
            bestCandidate.Source,
            GetThresholdMatchMethod(bestCandidate.Source),
            GpsMatchKind.ThresholdMatch);
    }

    private static int GetSourcePriority(string source)
    {
        if (source.EndsWith(".U03", StringComparison.Ordinal))
            return 0;
        if (source.EndsWith(".U03MinusCountdown", StringComparison.Ordinal))
            return 1;
        if (source.Contains(".Samples2[", StringComparison.Ordinal))
            return 2;
        if (source.Contains(".Samples[", StringComparison.Ordinal))
            return 3;
        if (source.EndsWith(".RaceTime", StringComparison.Ordinal))
            return 4;
        return 5;
    }

    private static GpsMatchMethod GetThresholdMatchMethod(string source) =>
        source.EndsWith(".U03MinusCountdown", StringComparison.Ordinal)
            ? GpsMatchMethod.U03MinusCountdownThreshold
            : GpsMatchMethod.U03Threshold;
}
