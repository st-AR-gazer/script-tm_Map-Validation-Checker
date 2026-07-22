using GBX.NET;
using GBX.NET.Engines.Game;

using MapValidationChecker.Cli.Infrastructure;
using MapValidationChecker.Core.Validation;

namespace MapValidationChecker.Cli.Evidence;

internal sealed class ReplayEvidenceIndex
{
    private readonly IReadOnlyDictionary<string, List<ReplayEntry>> entriesByMapUid;

    private ReplayEvidenceIndex(IReadOnlyDictionary<string, List<ReplayEntry>> entriesByMapUid)
    {
        this.entriesByMapUid = entriesByMapUid;
    }

    public static ReplayEvidenceIndex Empty { get; } = new(
        new Dictionary<string, List<ReplayEntry>>(StringComparer.Ordinal));

    public static ReplayEvidenceIndex Build(
        IReadOnlyList<string> files,
        Action<ReplayScanProgress>? reportProgress = null)
    {
        ArgumentNullException.ThrowIfNull(files);

        var entriesByMapUid = new Dictionary<string, List<ReplayEntry>>(StringComparer.Ordinal);
        var scanned = 0;
        var gbxCount = 0;
        var indexed = 0;

        foreach (var file in files)
        {
            scanned++;

            if (!GbxFile.HasMagic(file))
            {
                ReportProgress();
                continue;
            }

            try
            {
                gbxCount++;

                var replay = Gbx.ParseNode<CGameCtnReplayRecord>(file);
                var uid = replay.MapInfo?.Id;
                uid ??= replay.Challenge?.MapUid;
                uid ??= replay.Ghosts?.FirstOrDefault()?.Validate_ChallengeUid;

                if (string.IsNullOrWhiteSpace(uid))
                    continue;

                var times = new HashSet<int>();
                foreach (var ghost in replay.GetGhosts(alsoInClips: true))
                {
                    var time = GbxTime.ToMilliseconds(ghost.RaceTime);
                    if (time.HasValue)
                        times.Add(time.Value);
                }

                if (times.Count == 0)
                {
                    ReportProgress();
                    continue;
                }

                if (!entriesByMapUid.TryGetValue(uid, out var entries))
                {
                    entries = [];
                    entriesByMapUid[uid] = entries;
                }

                entries.Add(new ReplayEntry(file, times));
                indexed++;
            }
            catch
            {
            }

            ReportProgress();
        }

        return new ReplayEvidenceIndex(entriesByMapUid);

        void ReportProgress() => reportProgress?.Invoke(
            new ReplayScanProgress(scanned, files.Count, gbxCount, indexed));
    }

    public ReplayEvidence? FindMatch(string? mapUid, int authorTimeMs)
    {
        if (string.IsNullOrWhiteSpace(mapUid) ||
            !entriesByMapUid.TryGetValue(mapUid, out var entries))
        {
            return null;
        }

        var matchingEntry = entries.FirstOrDefault(entry => entry.GhostTimesMs.Contains(authorTimeMs));
        return matchingEntry is null
            ? null
            : new ReplayEvidence(matchingEntry.Path);
    }

    private sealed record ReplayEntry(string Path, HashSet<int> GhostTimesMs);
}

internal readonly record struct ReplayScanProgress(
    int Scanned,
    int Total,
    int GbxCount,
    int IndexedCount);
