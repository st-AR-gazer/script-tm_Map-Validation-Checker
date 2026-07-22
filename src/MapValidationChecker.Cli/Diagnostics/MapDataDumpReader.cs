using GBX.NET.Engines.Game;

using MapValidationChecker.Cli.Evidence;
using MapValidationChecker.Cli.Infrastructure;
using MapValidationChecker.Core.Validation;

namespace MapValidationChecker.Cli.Diagnostics;

internal static class MapDataDumpReader
{
    public static MapDataDump Read(
        CGameCtnChallenge map,
        int? effectiveAuthorTimeMs,
        int? maxDepth)
    {
        ArgumentNullException.ThrowIfNull(map);

        var challengeParameters = map.ChallengeParameters;
        var validationGhostTimeMs = GbxTime.ToMilliseconds(
            challengeParameters?.RaceValidateGhost?.RaceTime);
        var expectedWaypointCount = WaypointValidation.GetExpectedWaypointCount(
            new MapCheckpointFacts(map.NbCheckpoints, map.IsLapRace, map.NbLaps, 0));

        var metadataKeys = map.ScriptMetadata?.Traits?.Keys
            .OrderBy(key => key, StringComparer.Ordinal)
            .ToList();
        var waypointTimes = WaypointMetadataReader.ReadTimes(map.ScriptMetadata);

        ValidationTagDump? validationTag = null;
        var tagEvidence = ValidationTagReader.Read(map.ScriptMetadata);
        if (tagEvidence is not null)
        {
            validationTag = new ValidationTagDump
            {
                Key = tagEvidence.Key,
                Note = tagEvidence.Note,
                AuthorTimeMs = tagEvidence.AuthorTimeMs,
                AuthorTimeSource = tagEvidence.AuthorTimeSource,
                SignaturePresent = tagEvidence.HasSignature
            };
        }

        var gpsRecordDataEntries = GpsCandidateExtractor.CollectRecordDataEntries(map);
        var gpsRecordDataCandidates = GpsCandidateExtractor
            .EnumerateCandidatesFromEntries(gpsRecordDataEntries)
            .ToList();
        var mediaBlockEntityChunkCandidates = GpsCandidateExtractor
            .EnumerateMediaBlockEntityChunkCandidates(map)
            .ToList();
        var clipGroupEntLists = GpsCandidateExtractor.CollectClipGroupEntListSummaries(map, maxDepth);
        var entRecordElemCandidates = GpsCandidateExtractor
            .EnumerateEntRecordElemCandidates(map, maxDepth)
            .ToList();
        var invalidLinkedCheckpointOrders =
            WaypointMetadataReader.GetInvalidLinkedCheckpointOrders(map);
        var mediaBlockGhostCandidates =
            GpsCandidateExtractor.CollectMediaBlockGhostCandidates(map, maxDepth);

        return new MapDataDump
        {
            NbCheckpoints = map.NbCheckpoints,
            IsLapRace = map.IsLapRace,
            NbLaps = map.NbLaps,
            ExpectedWaypointCount = expectedWaypointCount,
            MapAuthorTimeMs = GbxTime.ToMilliseconds(map.AuthorTime),
            ChallengeParametersAuthorTimeMs = GbxTime.ToMilliseconds(challengeParameters?.AuthorTime),
            EffectiveAuthorTimeMs = effectiveAuthorTimeMs,
            ValidationGhostRaceTimeMs = validationGhostTimeMs,
            ScriptMetadataKeys = metadataKeys is { Count: > 0 } ? metadataKeys : null,
            WaypointTimesMs = waypointTimes is { Count: > 0 } ? waypointTimes : null,
            InvalidLinkedCheckpointOrders = invalidLinkedCheckpointOrders.Count > 0
                ? invalidLinkedCheckpointOrders
                : null,
            ValidationTag = validationTag,
            GpsRecordDataEntries = gpsRecordDataEntries.Count > 0 ? gpsRecordDataEntries : null,
            GpsRecordDataCandidates = gpsRecordDataCandidates.Count > 0
                ? gpsRecordDataCandidates
                : null,
            MediaBlockEntityChunkCandidates = mediaBlockEntityChunkCandidates.Count > 0
                ? mediaBlockEntityChunkCandidates
                : null,
            ClipGroupEntLists = clipGroupEntLists.Count > 0 ? clipGroupEntLists : null,
            EntRecordElemCandidates = entRecordElemCandidates.Count > 0
                ? entRecordElemCandidates
                : null,
            MediaBlockGhostCandidates = mediaBlockGhostCandidates.Count > 0
                ? mediaBlockGhostCandidates
                : null
        };
    }
}
