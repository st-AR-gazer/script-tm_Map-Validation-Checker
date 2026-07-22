using System.Collections;

using GBX.NET.Engines.Game;

using MapValidationChecker.Cli.Infrastructure;

using static MapValidationChecker.Cli.Infrastructure.GbxReflection;

namespace MapValidationChecker.Cli.Evidence;

internal static class GpsCandidateExtractor
{
    private const int CountdownOffsetMs = 3_000;

    public static List<GpsRecordDataEntryDump> CollectRecordDataEntries(CGameCtnChallenge map)
    {
        ArgumentNullException.ThrowIfNull(map);

        var entries = new List<GpsRecordDataEntryDump>();
        var clipGroup = map.ClipGroupInGame;
        if (clipGroup?.Clips is null || clipGroup.Clips.Count == 0)
            return entries;

        for (var triggerIndex = 0; triggerIndex < clipGroup.Clips.Count; triggerIndex++)
        {
            var trigger = clipGroup.Clips[triggerIndex];
            var clip = trigger.Clip;
            if (clip?.Tracks is null || clip.Tracks.Count == 0)
                continue;

            for (var trackIndex = 0; trackIndex < clip.Tracks.Count; trackIndex++)
            {
                var track = clip.Tracks[trackIndex];
                if (track?.Blocks is null || track.Blocks.Count == 0)
                    continue;

                for (var blockIndex = 0; blockIndex < track.Blocks.Count; blockIndex++)
                {
                    var block = track.Blocks[blockIndex];
                    if (block is null ||
                        !TryGetMemberValue(block, "RecordData", out var recordDataValue) ||
                        recordDataValue is null ||
                        !TryGetMemberValue(recordDataValue, "EntList", out var entListValue) ||
                        entListValue is null ||
                        entListValue is string ||
                        entListValue is not IEnumerable)
                    {
                        continue;
                    }

                    int? entListCount = TryGetCollectionCount(entListValue, out var countValue)
                        ? countValue
                        : null;

                    foreach (var entryItem in EnumerateCollectionItems(entListValue))
                    {
                        var basePath =
                            $"ClipGroupInGame.Clips[{triggerIndex}].Clip.Tracks[{trackIndex}].Blocks[{blockIndex}].RecordData.EntList[{entryItem.Index}]";
                        var entry = entryItem.Value;

                        if (entry is null)
                        {
                            entries.Add(new GpsRecordDataEntryDump(
                                basePath,
                                entListCount,
                                true,
                                null,
                                null,
                                null,
                                0,
                                null,
                                null,
                                0,
                                null,
                                null));
                            continue;
                        }

                        var samples = GetSampleCollectionSummary(entry, "Samples");
                        var samples2 = GetSampleCollectionSummary(entry, "Samples2");

                        entries.Add(new GpsRecordDataEntryDump(
                            basePath,
                            entListCount,
                            false,
                            TryGetIntMemberValue(entry, "U01"),
                            TryGetIntMemberValue(entry, "U02"),
                            TryGetIntMemberValue(entry, "U03"),
                            samples.Count,
                            samples.LastIndexWithTime,
                            samples.LastTimeMs,
                            samples2.Count,
                            samples2.LastIndexWithTime,
                            samples2.LastTimeMs));
                    }
                }
            }
        }

        return entries;
    }

    public static List<GpsCandidate> CollectMediaBlockGhostCandidates(
        CGameCtnChallenge map,
        int? maxDepth)
    {
        ArgumentNullException.ThrowIfNull(map);

        var candidates = new List<GpsCandidate>();
        if (map.ClipGroupInGame is null)
            return candidates;

        var index = 0;
        foreach (var block in TraverseForType<CGameCtnMediaBlockGhost>(
                     map.ClipGroupInGame,
                     maxDepth))
        {
            var ghost = block.GhostModel;
            if (ghost is null)
            {
                index++;
                continue;
            }

            var timeMs = GbxTime.ToMilliseconds(ghost.RaceTime);
            if (timeMs.HasValue)
            {
                candidates.Add(new GpsCandidate(
                    timeMs.Value,
                    $"CGameCtnMediaBlockGhost[{index}].GhostModel.RaceTime"));
            }

            index++;
        }

        return candidates;
    }

    public static IEnumerable<GpsCandidate> EnumerateMediaBlockEntityChunkCandidates(
        CGameCtnChallenge map)
    {
        ArgumentNullException.ThrowIfNull(map);

        var clipGroup = map.ClipGroupInGame;
        if (clipGroup?.Clips is null || clipGroup.Clips.Count == 0)
            yield break;

        for (var triggerIndex = 0; triggerIndex < clipGroup.Clips.Count; triggerIndex++)
        {
            var trigger = clipGroup.Clips[triggerIndex];
            var clip = trigger.Clip;
            if (clip?.Tracks is null || clip.Tracks.Count == 0)
                continue;

            for (var trackIndex = 0; trackIndex < clip.Tracks.Count; trackIndex++)
            {
                var track = clip.Tracks[trackIndex];
                if (track?.Blocks is null || track.Blocks.Count == 0)
                    continue;

                for (var blockIndex = 0; blockIndex < track.Blocks.Count; blockIndex++)
                {
                    var block = track.Blocks[blockIndex];
                    if (block is null ||
                        !TryGetMemberValue(block, "GhostName", out var ghostNameValue) ||
                        ghostNameValue is not string ||
                        !TryGetMemberValue(block, "Chunks", out var chunksValue) ||
                        chunksValue is null)
                    {
                        continue;
                    }

                    foreach (var chunkItem in EnumerateCollectionItems(chunksValue))
                    {
                        var chunk = chunkItem.Value;
                        if (chunk is null)
                            continue;

                        var timeMs = TryGetIntMemberValue(chunk, "U05");
                        if (!timeMs.HasValue)
                            continue;

                        yield return new GpsCandidate(
                            timeMs.Value,
                            $"ClipGroupInGame.Clips[{triggerIndex}].Clip.Tracks[{trackIndex}].Blocks[{blockIndex}].Chunks[{chunkItem.Index}].{chunk.GetType().Name}.U05");
                    }
                }
            }
        }
    }

    public static List<EntListSummary> CollectClipGroupEntListSummaries(
        CGameCtnChallenge map,
        int? maxDepth)
    {
        ArgumentNullException.ThrowIfNull(map);

        var summaries = new List<EntListSummary>();
        var root = map.ClipGroupInGame;
        if (root is null)
            return summaries;

        var visited = new HashSet<object>(ObjectReferenceEqualityComparer.Instance);
        var stack = new Stack<(object Value, string Path, int Depth)>();
        stack.Push((root, "ClipGroupInGame", 0));

        while (stack.Count > 0)
        {
            var (value, path, depth) = stack.Pop();
            if (value is string)
                continue;

            if (!visited.Add(value))
                continue;

            if (TryGetMemberValue(value, "EntList", out var entListValue) &&
                entListValue is not null &&
                entListValue is not string &&
                entListValue is IEnumerable)
            {
                int? entListCount = TryGetCollectionCount(entListValue, out var countValue)
                    ? countValue
                    : null;
                var sampledU03 = new List<int?>();
                foreach (var item in EnumerateCollectionItems(entListValue))
                {
                    sampledU03.Add(item.Value is null
                        ? null
                        : TryGetIntMemberValue(item.Value, "U03"));
                    if (sampledU03.Count >= 32)
                        break;
                }

                summaries.Add(new EntListSummary($"{path}.EntList", entListCount, sampledU03));
            }

            if (maxDepth.HasValue && depth >= maxDepth.Value)
                continue;

            foreach (var child in EnumerateObjectChildrenWithPaths(value, path))
                stack.Push((child.Obj, child.Path, depth + 1));
        }

        return summaries
            .OrderBy(summary => summary.Path, StringComparer.Ordinal)
            .ToList();
    }

    public static IEnumerable<GpsCandidate> EnumerateEntRecordElemCandidates(
        CGameCtnChallenge map,
        int? maxDepth)
    {
        foreach (var element in EnumerateEntRecordElems(map, maxDepth))
        {
            if (element.U03 is > 0)
                yield return new GpsCandidate(element.U03.Value, $"{element.Path}.U03");

            if (element.U03MinusCountdownMs.HasValue)
            {
                yield return new GpsCandidate(
                    element.U03MinusCountdownMs.Value,
                    $"{element.Path}.U03MinusCountdown");
            }
        }
    }

    public static IEnumerable<GpsCandidate> EnumerateRecordDataCandidates(CGameCtnChallenge map)
    {
        foreach (var candidate in EnumerateCandidatesFromEntries(CollectRecordDataEntries(map)))
            yield return candidate;
    }

    public static IEnumerable<GpsCandidate> EnumerateCandidatesFromEntries(
        IEnumerable<GpsRecordDataEntryDump> entries)
    {
        ArgumentNullException.ThrowIfNull(entries);

        foreach (var entry in entries)
        {
            if (entry.U03 is > 0)
                yield return new GpsCandidate(entry.U03.Value, $"{entry.Path}.U03");

            if (entry.U03 >= CountdownOffsetMs)
            {
                yield return new GpsCandidate(
                    entry.U03.Value - CountdownOffsetMs,
                    $"{entry.Path}.U03MinusCountdown");
            }

            if (entry.LastSampleTimeMs.HasValue && entry.LastSampleIndex.HasValue)
            {
                yield return new GpsCandidate(
                    entry.LastSampleTimeMs.Value,
                    $"{entry.Path}.Samples[{entry.LastSampleIndex.Value}].Time");
            }

            if (entry.LastSample2TimeMs.HasValue && entry.LastSample2Index.HasValue)
            {
                yield return new GpsCandidate(
                    entry.LastSample2TimeMs.Value,
                    $"{entry.Path}.Samples2[{entry.LastSample2Index.Value}].Time");
            }
        }
    }

    private static IEnumerable<EntRecordElemDump> EnumerateEntRecordElems(
        CGameCtnChallenge map,
        int? maxDepth)
    {
        var root = map.ClipGroupInGame;
        if (root is null)
            yield break;

        var visited = new HashSet<object>(ObjectReferenceEqualityComparer.Instance);
        var stack = new Stack<(object Value, string Path, int Depth)>();
        stack.Push((root, "ClipGroupInGame", 0));

        while (stack.Count > 0)
        {
            var (value, path, depth) = stack.Pop();
            if (value is string)
                continue;

            if (!visited.Add(value))
                continue;

            if (value.GetType().Name.EndsWith("EntRecordListElem", StringComparison.Ordinal))
            {
                var u03 = TryGetIntMemberValue(value, "U03");
                int? u03MinusCountdown = null;
                if (u03 >= CountdownOffsetMs)
                    u03MinusCountdown = u03.Value - CountdownOffsetMs;

                yield return new EntRecordElemDump(path, u03, u03MinusCountdown);
            }

            if (maxDepth.HasValue && depth >= maxDepth.Value)
                continue;

            foreach (var child in EnumerateObjectChildrenWithPaths(value, path))
                stack.Push((child.Obj, child.Path, depth + 1));
        }
    }
}
