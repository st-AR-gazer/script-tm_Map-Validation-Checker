namespace MapValidationChecker.Core.Validation;

/// <summary>
/// Contains the already-extracted facts needed to classify one parsed map.
/// </summary>
public sealed record ValidationInput
{
    public int? AuthorTimeMs { get; init; }
    public ManualOverrideEvidence? ManualOverride { get; init; }
    public int? ValidationGhostTimeMs { get; init; }
    public ValidationTagEvidence? ValidationTag { get; init; }
    public ReplayEvidence? MatchingReplay { get; init; }
    public WaypointMetadataEvidence? WaypointMetadata { get; init; }
    public MapCheckpointFacts Checkpoints { get; init; } = MapCheckpointFacts.Empty;
    public GpsEvaluation Gps { get; init; } = GpsEvaluation.Disabled;
    public bool StrictGps { get; init; }
    public int GpsThresholdMs { get; init; } = 100;
}

public sealed record ManualOverrideEvidence(bool Valid, string? Note);

public sealed record ValidationTagEvidence(
    string? Key,
    string? Note,
    int? AuthorTimeMs,
    string? AuthorTimeSource,
    bool HasSignature);

public sealed record ReplayEvidence(string Path);

public sealed record WaypointMetadataEvidence(int FinishTimeMs, int WaypointCount);

public sealed record MapCheckpointFacts(
    int NbCheckpoints,
    bool IsLapRace,
    int NbLaps,
    int InvalidLinkedCheckpointGroupCount)
{
    public static MapCheckpointFacts Empty { get; } = new(0, false, 0, 0);
}
