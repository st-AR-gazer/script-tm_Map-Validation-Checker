using MapValidationChecker.Cli.Evidence;

namespace MapValidationChecker.Cli.Diagnostics;

internal sealed class MapDataDump
{
    public int NbCheckpoints { get; set; }
    public bool IsLapRace { get; set; }
    public int NbLaps { get; set; }
    public int ExpectedWaypointCount { get; set; }
    public int? MapAuthorTimeMs { get; set; }
    public int? ChallengeParametersAuthorTimeMs { get; set; }
    public int? EffectiveAuthorTimeMs { get; set; }
    public int? ValidationGhostRaceTimeMs { get; set; }
    public List<string>? ScriptMetadataKeys { get; set; }
    public List<int>? WaypointTimesMs { get; set; }
    public List<int>? InvalidLinkedCheckpointOrders { get; set; }
    public ValidationTagDump? ValidationTag { get; set; }
    public List<GpsRecordDataEntryDump>? GpsRecordDataEntries { get; set; }
    public List<GpsCandidate>? GpsRecordDataCandidates { get; set; }
    public List<GpsCandidate>? MediaBlockEntityChunkCandidates { get; set; }
    public List<EntListSummary>? ClipGroupEntLists { get; set; }
    public List<GpsCandidate>? EntRecordElemCandidates { get; set; }
    public List<GpsCandidate>? MediaBlockGhostCandidates { get; set; }
}

internal sealed class ValidationTagDump
{
    public string? Key { get; set; }
    public string? Note { get; set; }
    public int? AuthorTimeMs { get; set; }
    public string? AuthorTimeSource { get; set; }
    public bool SignaturePresent { get; set; }
}
