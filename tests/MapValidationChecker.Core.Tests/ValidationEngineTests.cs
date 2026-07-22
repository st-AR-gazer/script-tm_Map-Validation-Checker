using MapValidationChecker.Core.Validation;

namespace MapValidationChecker.Core.Tests;

public sealed class ValidationEngineTests
{
    private readonly ValidationEngine engine = new();

    [Fact]
    public void Manual_override_is_terminal_even_without_an_author_time()
    {
        var outcome = EvaluateComplete(CreateInput() with
        {
            AuthorTimeMs = null,
            ManualOverride = new ManualOverrideEvidence(false, "Reviewed manually")
        });

        Assert.Equal(ValidationStatus.Maybe, outcome.Status);
        Assert.Equal(ValidationType.Manual, outcome.Type);
        Assert.Equal("Reviewed manually", outcome.Note);
        Assert.Null(outcome.Error);
    }

    [Fact]
    public void Missing_author_time_preserves_the_existing_error_contract()
    {
        var outcome = EvaluateComplete(CreateInput() with { AuthorTimeMs = null });

        Assert.Equal(ValidationStatus.Unknown, outcome.Status);
        Assert.Equal(ValidationType.Normal, outcome.Type);
        Assert.Equal("missing AuthorMedal time", outcome.Error);
        Assert.Equal("Map is missing author time; validation checks skipped.", outcome.Note);
    }

    [Fact]
    public void Matching_validation_ghost_is_validated()
    {
        var outcome = EvaluateComplete(CreateInput() with { ValidationGhostTimeMs = 10_000 });

        Assert.Equal(ValidationStatus.Yes, outcome.Status);
        Assert.Equal(ValidationType.ValidationGhost, outcome.Type);
        Assert.Null(outcome.Error);
    }

    [Fact]
    public void Mismatching_validation_ghost_is_a_terminal_error()
    {
        var outcome = EvaluateComplete(CreateInput() with { ValidationGhostTimeMs = 9_999 });

        Assert.Equal(ValidationStatus.Unknown, outcome.Status);
        Assert.Equal(ValidationType.ValidationGhost, outcome.Type);
        Assert.Equal("validation ghost time mismatch", outcome.Error);
        Assert.Equal("authorTimeMs=10000, validationGhostMs=9999", outcome.Note);
    }

    [Fact]
    public void Matching_validation_tag_preserves_its_diagnostic_note()
    {
        var outcome = EvaluateComplete(CreateInput() with
        {
            ValidationTag = new ValidationTagEvidence(
                "RemovalTag",
                "original note",
                10_000,
                "ChallengeParameters.AuthorTime",
                HasSignature: true)
        });

        Assert.Equal(ValidationStatus.Yes, outcome.Status);
        Assert.Equal(ValidationType.ValidationTag, outcome.Type);
        Assert.Equal(
            "Validation ghost removed (tag found in script metadata). tagKey=RemovalTag tagAuthorTimeSource=ChallengeParameters.AuthorTime removalSignature=present",
            outcome.Note);
    }

    [Fact]
    public void Mismatching_unsigned_validation_tag_is_maybe_with_both_warnings()
    {
        var outcome = EvaluateComplete(CreateInput() with
        {
            ValidationTag = new ValidationTagEvidence(
                "RemovalTag",
                null,
                9_000,
                "Note.AuthorTime (0:09.000)",
                HasSignature: false)
        });

        Assert.Equal(ValidationStatus.Maybe, outcome.Status);
        Assert.Equal(ValidationType.ValidationTag, outcome.Type);
        Assert.Equal(
            "Warning: validation tag author time mismatch; authorTimeMs=10000, tagAuthorTimeMs=9000. Warning: expected removal-tool signature is missing; metadata looks suspicious. tagKey=RemovalTag tagAuthorTimeSource=Note.AuthorTime (0:09.000) removalSignature=missing",
            outcome.Note);
    }

    [Fact]
    public void Validation_tag_without_a_time_is_maybe()
    {
        var outcome = EvaluateComplete(CreateInput() with
        {
            ValidationTag = new ValidationTagEvidence(null, null, null, null, HasSignature: true)
        });

        Assert.Equal(ValidationStatus.Maybe, outcome.Status);
        Assert.Equal(ValidationType.ValidationTag, outcome.Type);
        Assert.Equal(
            "Validation-removal tag found, but no tag author time was extracted. removalSignature=present",
            outcome.Note);
    }

    [Fact]
    public void Matching_replay_is_validated_and_carries_its_path()
    {
        var outcome = EvaluateComplete(CreateInput() with
        {
            MatchingReplay = new ReplayEvidence("C:/Replays/match.Replay.Gbx")
        });

        Assert.Equal(ValidationStatus.Yes, outcome.Status);
        Assert.Equal(ValidationType.Replay, outcome.Type);
        Assert.Equal("Replay ghost time matched map author time.", outcome.Note);
        Assert.Equal("C:/Replays/match.Replay.Gbx", outcome.ReplayPath);
    }

    [Fact]
    public void Matching_waypoint_finish_and_count_is_normal_validation()
    {
        var outcome = EvaluateComplete(CreateInput() with
        {
            WaypointMetadata = new WaypointMetadataEvidence(10_000, 3)
        });

        Assert.Equal(ValidationStatus.Yes, outcome.Status);
        Assert.Equal(ValidationType.Normal, outcome.Type);
        Assert.Null(outcome.Note);
    }

    [Fact]
    public void Matching_finish_with_implausible_waypoint_count_is_plugin_suspicion()
    {
        var outcome = EvaluateComplete(CreateInput() with
        {
            WaypointMetadata = new WaypointMetadataEvidence(10_000, 2)
        });

        Assert.Equal(ValidationStatus.Maybe, outcome.Status);
        Assert.Equal(ValidationType.Plugin, outcome.Type);
        Assert.Equal(
            "Finish time matches, but waypoint count differs (mapNbCheckpoints=3, mapIsLapRace=False, mapNbLaps=0, mapExpectedWaypoints=3, metadataWaypoints=2, invalidLinkedCheckpointGroups=0).",
            outcome.Note);
    }

    [Fact]
    public void Invalid_linked_checkpoint_shortfall_is_accepted_per_lap()
    {
        var outcome = EvaluateComplete(CreateInput() with
        {
            Checkpoints = new MapCheckpointFacts(4, IsLapRace: true, NbLaps: 2, InvalidLinkedCheckpointGroupCount: 1),
            WaypointMetadata = new WaypointMetadataEvidence(10_000, 6)
        });

        Assert.Equal(ValidationStatus.Yes, outcome.Status);
        Assert.Equal(ValidationType.Normal, outcome.Type);
    }

    [Fact]
    public void Unevaluated_gps_is_requested_only_after_stronger_rules_fail()
    {
        var evaluation = engine.Evaluate(CreateInput() with { Gps = GpsEvaluation.NotEvaluated });

        Assert.True(evaluation.RequiresGpsEvidence);
        Assert.Null(evaluation.Outcome);
    }

    [Fact]
    public void Exact_gps_match_is_maybe_by_default()
    {
        var evidence = new GpsEvidence(
            10_000,
            0,
            "ClipGroupInGame.Clips[0].U05",
            GpsMatchMethod.U05Exact,
            GpsMatchKind.ExactMatch);
        var outcome = EvaluateComplete(CreateInput() with
        {
            Gps = GpsEvaluation.Matched(evidence)
        });

        Assert.Equal(ValidationStatus.Maybe, outcome.Status);
        Assert.Equal(ValidationType.Gps, outcome.Type);
        Assert.Same(evidence, outcome.GpsEvidence);
        Assert.Equal(
            "GPS validated via exact U05 match (delta 0 ms). Still potentially invalid. See gpsValidation for exact values.",
            outcome.Note);
    }

    [Fact]
    public void Strict_gps_match_is_yes()
    {
        var outcome = EvaluateComplete(CreateInput() with
        {
            StrictGps = true,
            Gps = GpsEvaluation.Matched(new GpsEvidence(
                10_000,
                0,
                "source",
                GpsMatchMethod.U05Exact,
                GpsMatchKind.ExactMatch))
        });

        Assert.Equal(ValidationStatus.Yes, outcome.Status);
        Assert.Equal(ValidationType.Gps, outcome.Type);
        Assert.Equal(
            "GPS validated via exact U05 match (delta 0 ms). Strict mode => validated Yes. See gpsValidation for exact values.",
            outcome.Note);
    }

    [Theory]
    [InlineData(
        GpsMatchMethod.U03Threshold,
        "GPS validated via U03 within ±100 ms (delta 25 ms; not exact). Still potentially invalid. See gpsValidation for exact values.")]
    [InlineData(
        GpsMatchMethod.U03MinusCountdownThreshold,
        "GPS validated via U03-3000 countdown normalization within ±100 ms (delta 25 ms; not exact). Still potentially invalid. See gpsValidation for exact values.")]
    public void Threshold_gps_methods_preserve_their_notes(
        GpsMatchMethod method,
        string expectedNote)
    {
        var outcome = EvaluateComplete(CreateInput() with
        {
            Gps = GpsEvaluation.Matched(new GpsEvidence(
                9_975,
                25,
                "source",
                method,
                GpsMatchKind.ThresholdMatch))
        });

        Assert.Equal(expectedNote, outcome.Note);
    }

    [Fact]
    public void Missing_gps_and_waypoint_metadata_is_unknown()
    {
        var outcome = EvaluateComplete(CreateInput() with { Gps = GpsEvaluation.NoMatch });

        Assert.Equal(ValidationStatus.Unknown, outcome.Status);
        Assert.Equal(ValidationType.Normal, outcome.Type);
        Assert.Equal("Missing author time or Race_AuthorRaceWaypointTimes metadata.", outcome.Note);
    }

    [Fact]
    public void Mismatching_waypoint_finish_falls_back_to_plugin_suspicion()
    {
        var outcome = EvaluateComplete(CreateInput() with
        {
            WaypointMetadata = new WaypointMetadataEvidence(9_500, 3),
            Gps = GpsEvaluation.NoMatch
        });

        Assert.Equal(ValidationStatus.Maybe, outcome.Status);
        Assert.Equal(ValidationType.Plugin, outcome.Type);
        Assert.Equal(
            "AuthorTime differs from metadata finish (authorTimeMs=10000, metadataFinishMs=9500, mapNbCheckpoints=3, mapIsLapRace=False, mapNbLaps=0, mapExpectedWaypoints=3, metadataWaypoints=3).",
            outcome.Note);
    }

    private ValidationOutcome EvaluateComplete(ValidationInput input)
    {
        var evaluation = engine.Evaluate(input);
        Assert.False(evaluation.RequiresGpsEvidence);
        return Assert.IsType<ValidationOutcome>(evaluation.Outcome);
    }

    private static ValidationInput CreateInput() => new()
    {
        AuthorTimeMs = 10_000,
        Checkpoints = new MapCheckpointFacts(3, IsLapRace: false, NbLaps: 0, InvalidLinkedCheckpointGroupCount: 0),
        Gps = GpsEvaluation.Disabled
    };
}
