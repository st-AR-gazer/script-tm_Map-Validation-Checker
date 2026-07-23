using GBX.NET.Engines.Game;

using MapValidationChecker.Cli.Infrastructure;
using MapValidationChecker.Core.Validation;

namespace MapValidationChecker.Cli.Evidence;

internal static class NonGpsEvidenceReader
{
    public static NonGpsEvidence Read(
        CGameCtnChallenge map,
        ManualOverrideCatalog manualOverrides,
        ReplayEvidenceIndex replayIndex)
    {
        ArgumentNullException.ThrowIfNull(map);
        ArgumentNullException.ThrowIfNull(manualOverrides);
        ArgumentNullException.ThrowIfNull(replayIndex);

        var authorTimeMs = GbxTime.ToMilliseconds(
            map.AuthorTime ?? map.ChallengeParameters?.AuthorTime);
        var manualOverride = manualOverrides.Find(map.MapUid);
        int? validationGhostTimeMs = null;
        ValidationTagEvidence? validationTag = null;
        ReplayEvidence? matchingReplay = null;
        WaypointMetadataEvidence? waypointMetadata = null;
        var invalidLinkedCheckpointGroupCount = 0;

        if (manualOverride is null && authorTimeMs.HasValue)
        {
            validationGhostTimeMs = GbxTime.ToMilliseconds(
                map.ChallengeParameters?.RaceValidateGhost?.RaceTime);

            if (!validationGhostTimeMs.HasValue)
            {
                validationTag = ValidationTagReader.Read(map.ScriptMetadata);

                if (validationTag?.MatchesAuthorTime(authorTimeMs.Value) != true)
                {
                    matchingReplay = replayIndex.FindMatch(map.MapUid, authorTimeMs.Value);

                    if (matchingReplay is null)
                    {
                        var waypointEvidence = WaypointMetadataReader.Read(map);
                        waypointMetadata = waypointEvidence.Metadata;
                        invalidLinkedCheckpointGroupCount =
                            waypointEvidence.InvalidLinkedCheckpointGroupCount;
                    }
                }
            }
        }

        return new NonGpsEvidence(
            authorTimeMs,
            manualOverride,
            validationGhostTimeMs,
            validationTag,
            matchingReplay,
            waypointMetadata,
            new MapCheckpointFacts(
                map.NbCheckpoints,
                map.IsLapRace,
                map.NbLaps,
                invalidLinkedCheckpointGroupCount));
    }
}

internal sealed record NonGpsEvidence(
    int? AuthorTimeMs,
    ManualOverrideEvidence? ManualOverride,
    int? ValidationGhostTimeMs,
    ValidationTagEvidence? ValidationTag,
    ReplayEvidence? MatchingReplay,
    WaypointMetadataEvidence? WaypointMetadata,
    MapCheckpointFacts Checkpoints);
