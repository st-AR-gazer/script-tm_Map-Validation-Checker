namespace MapValidationChecker.Core.Validation;

/// <summary>
/// Applies the validation rules in their compatibility-defined priority order.
/// </summary>
public sealed class ValidationEngine
{
    public ValidationEvaluation Evaluate(ValidationInput input)
    {
        ArgumentNullException.ThrowIfNull(input);
        ArgumentNullException.ThrowIfNull(input.Checkpoints);
        ArgumentNullException.ThrowIfNull(input.Gps);

        if (input.GpsThresholdMs < 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(ValidationInput.GpsThresholdMs),
                "GPS threshold cannot be negative.");
        }

        if (input.ManualOverride is not null)
        {
            return Complete(
                input.ManualOverride.Valid ? ValidationStatus.Yes : ValidationStatus.Maybe,
                ValidationType.Manual,
                note: input.ManualOverride.Note);
        }

        if (!input.AuthorTimeMs.HasValue)
        {
            return Complete(
                ValidationStatus.Unknown,
                ValidationType.Normal,
                note: "Map is missing author time; validation checks skipped.",
                error: "missing AuthorMedal time");
        }

        var authorTimeMs = input.AuthorTimeMs.Value;

        if (input.ValidationGhostTimeMs.HasValue)
        {
            if (input.ValidationGhostTimeMs.Value == authorTimeMs)
                return Complete(ValidationStatus.Yes, ValidationType.ValidationGhost);

            return Complete(
                ValidationStatus.Unknown,
                ValidationType.ValidationGhost,
                note: $"authorTimeMs={authorTimeMs}, validationGhostMs={input.ValidationGhostTimeMs.Value}",
                error: "validation ghost time mismatch");
        }

        if (input.ValidationTag is { } validationTag &&
            validationTag.MatchesAuthorTime(authorTimeMs))
        {
            return EvaluateMatchingValidationTag(validationTag);
        }

        if (input.MatchingReplay is not null)
        {
            return Complete(
                ValidationStatus.Yes,
                ValidationType.Replay,
                note: "Replay ghost time matched map author time.",
                replayPath: input.MatchingReplay.Path);
        }

        var waypointMetadata = input.WaypointMetadata;
        if (waypointMetadata is { WaypointCount: > 0 } &&
            waypointMetadata.FinishTimeMs == authorTimeMs)
        {
            if (!WaypointValidation.CountLooksPlausible(input.Checkpoints, waypointMetadata.WaypointCount))
            {
                var expectedWaypointCount = WaypointValidation.GetExpectedWaypointCount(input.Checkpoints);
                return Complete(
                    ValidationStatus.Maybe,
                    ValidationType.Plugin,
                    note:
                        $"Finish time matches, but waypoint count differs (mapNbCheckpoints={input.Checkpoints.NbCheckpoints}, mapIsLapRace={input.Checkpoints.IsLapRace}, mapNbLaps={input.Checkpoints.NbLaps}, mapExpectedWaypoints={expectedWaypointCount}, metadataWaypoints={waypointMetadata.WaypointCount}, invalidLinkedCheckpointGroups={input.Checkpoints.InvalidLinkedCheckpointGroupCount}).");
            }

            return Complete(ValidationStatus.Yes, ValidationType.Normal);
        }

        switch (input.Gps.State)
        {
            case GpsEvaluationState.NotEvaluated:
                return ValidationEvaluation.NeedsGpsEvidence();

            case GpsEvaluationState.Match:
                return EvaluateGpsMatch(input);

            case GpsEvaluationState.Disabled:
            case GpsEvaluationState.NoMatch:
                break;

            default:
                throw new ArgumentOutOfRangeException(
                    nameof(ValidationInput.Gps),
                    "Unknown GPS evaluation state.");
        }

        if (waypointMetadata is null || waypointMetadata.WaypointCount <= 0)
        {
            return Complete(
                ValidationStatus.Unknown,
                ValidationType.Normal,
                note: "Missing author time or Race_AuthorRaceWaypointTimes metadata.");
        }

        var expectedWaypointCountFinal = WaypointValidation.GetExpectedWaypointCount(input.Checkpoints);
        return Complete(
            ValidationStatus.Maybe,
            ValidationType.Plugin,
            note:
                $"AuthorTime differs from metadata finish (authorTimeMs={authorTimeMs}, metadataFinishMs={waypointMetadata.FinishTimeMs}, mapNbCheckpoints={input.Checkpoints.NbCheckpoints}, mapIsLapRace={input.Checkpoints.IsLapRace}, mapNbLaps={input.Checkpoints.NbLaps}, mapExpectedWaypoints={expectedWaypointCountFinal}, metadataWaypoints={waypointMetadata.WaypointCount}).");
    }

    private static ValidationEvaluation EvaluateMatchingValidationTag(ValidationTagEvidence tag)
    {
        var signatureWarning = tag.HasSignature
            ? null
            : "Warning: expected removal-tool signature is missing; metadata looks suspicious.";

        return Complete(
            ValidationStatus.Yes,
            ValidationType.ValidationTag,
            note: BuildValidationTagNote(
                tag,
                JoinNonEmpty(
                    "Validation ghost removed (tag found in script metadata).",
                    signatureWarning)));
    }

    private static ValidationEvaluation EvaluateGpsMatch(ValidationInput input)
    {
        var gpsEvidence = input.Gps.Evidence ??
            throw new InvalidOperationException("Matched GPS evaluation is missing its evidence.");

        var methodNote = gpsEvidence.Method switch
        {
            GpsMatchMethod.U05Exact => "GPS validated via exact U05 match (delta 0 ms).",
            GpsMatchMethod.U03Threshold =>
                $"GPS validated via U03 within \u00b1{input.GpsThresholdMs} ms (delta {gpsEvidence.DeltaMs} ms; not exact).",
            GpsMatchMethod.U03MinusCountdownThreshold =>
                $"GPS validated via U03-3000 countdown normalization within \u00b1{input.GpsThresholdMs} ms (delta {gpsEvidence.DeltaMs} ms; not exact).",
            _ => $"GPS validation used fallback value within \u00b1{input.GpsThresholdMs} ms."
        };

        var note = input.StrictGps
            ? JoinNonEmpty(methodNote, "Strict mode => validated Yes.", "See gpsValidation for exact values.")
            : JoinNonEmpty(methodNote, "Still potentially invalid.", "See gpsValidation for exact values.");

        return Complete(
            input.StrictGps ? ValidationStatus.Yes : ValidationStatus.Maybe,
            ValidationType.Gps,
            note: note,
            gpsEvidence: gpsEvidence);
    }

    private static string BuildValidationTagNote(ValidationTagEvidence tag, string baseNote)
    {
        var keyPart = string.IsNullOrWhiteSpace(tag.Key) ? null : $"tagKey={tag.Key}";
        var sourcePart = string.IsNullOrWhiteSpace(tag.AuthorTimeSource)
            ? null
            : $"tagAuthorTimeSource={tag.AuthorTimeSource}";
        var signaturePart = tag.HasSignature
            ? "removalSignature=present"
            : "removalSignature=missing";

        return JoinNonEmpty(baseNote, keyPart, sourcePart, signaturePart);
    }

    private static ValidationEvaluation Complete(
        ValidationStatus status,
        ValidationType type,
        string? note = null,
        string? error = null,
        string? replayPath = null,
        GpsEvidence? gpsEvidence = null) =>
        ValidationEvaluation.Completed(
            new ValidationOutcome(status, type, note, error, replayPath, gpsEvidence));

    private static string JoinNonEmpty(params string?[] parts) =>
        string.Join(" ", parts.Where(part => !string.IsNullOrWhiteSpace(part)));
}
