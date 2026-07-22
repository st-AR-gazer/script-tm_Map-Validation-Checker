using MapValidationChecker.Core.Validation;

namespace MapValidationChecker.Core.Tests;

public sealed class ValidationPriorityTests
{
    private readonly ValidationEngine engine = new();

    [Fact]
    public void Manual_override_beats_every_other_evidence()
    {
        var outcome = Evaluate(CreateAllEvidenceInput() with
        {
            ManualOverride = new ManualOverrideEvidence(true, null)
        });

        Assert.Equal(ValidationType.Manual, outcome.Type);
    }

    [Fact]
    public void Validation_ghost_beats_tag_replay_metadata_and_gps()
    {
        var outcome = Evaluate(CreateAllEvidenceInput());

        Assert.Equal(ValidationType.ValidationGhost, outcome.Type);
    }

    [Fact]
    public void Validation_tag_beats_replay_metadata_and_gps()
    {
        var outcome = Evaluate(CreateAllEvidenceInput() with { ValidationGhostTimeMs = null });

        Assert.Equal(ValidationType.ValidationTag, outcome.Type);
    }

    [Fact]
    public void Replay_beats_metadata_and_gps()
    {
        var outcome = Evaluate(CreateAllEvidenceInput() with
        {
            ValidationGhostTimeMs = null,
            ValidationTag = null
        });

        Assert.Equal(ValidationType.Replay, outcome.Type);
    }

    [Fact]
    public void Normal_metadata_beats_gps()
    {
        var outcome = Evaluate(CreateAllEvidenceInput() with
        {
            ValidationGhostTimeMs = null,
            ValidationTag = null,
            MatchingReplay = null
        });

        Assert.Equal(ValidationType.Normal, outcome.Type);
    }

    private ValidationOutcome Evaluate(ValidationInput input)
    {
        var evaluation = engine.Evaluate(input);
        Assert.False(evaluation.RequiresGpsEvidence);
        return Assert.IsType<ValidationOutcome>(evaluation.Outcome);
    }

    private static ValidationInput CreateAllEvidenceInput() => new()
    {
        AuthorTimeMs = 10_000,
        ValidationGhostTimeMs = 10_000,
        ValidationTag = new ValidationTagEvidence("tag", null, 10_000, "source", HasSignature: true),
        MatchingReplay = new ReplayEvidence("matching.Replay.Gbx"),
        WaypointMetadata = new WaypointMetadataEvidence(10_000, 3),
        Checkpoints = new MapCheckpointFacts(3, IsLapRace: false, NbLaps: 0, InvalidLinkedCheckpointGroupCount: 0),
        Gps = GpsEvaluation.NotEvaluated
    };
}
