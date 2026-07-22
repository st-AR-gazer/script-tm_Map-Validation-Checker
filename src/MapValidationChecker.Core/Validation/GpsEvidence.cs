namespace MapValidationChecker.Core.Validation;

public enum GpsMatchKind
{
    ExactMatch,
    ThresholdMatch
}

public enum GpsMatchMethod
{
    U05Exact,
    U03Threshold,
    U03MinusCountdownThreshold
}

public sealed record GpsEvidence(
    int GpsTimeMs,
    int DeltaMs,
    string Source,
    GpsMatchMethod Method,
    GpsMatchKind Kind);

public enum GpsEvaluationState
{
    Disabled,
    NotEvaluated,
    NoMatch,
    Match
}

/// <summary>
/// Distinguishes deferred GPS work from an evaluated scan that found no match.
/// </summary>
public sealed record GpsEvaluation
{
    private GpsEvaluation(GpsEvaluationState state, GpsEvidence? evidence)
    {
        State = state;
        Evidence = evidence;
    }

    public GpsEvaluationState State { get; }
    public GpsEvidence? Evidence { get; }

    public static GpsEvaluation Disabled { get; } = new(GpsEvaluationState.Disabled, null);
    public static GpsEvaluation NotEvaluated { get; } = new(GpsEvaluationState.NotEvaluated, null);
    public static GpsEvaluation NoMatch { get; } = new(GpsEvaluationState.NoMatch, null);

    public static GpsEvaluation Matched(GpsEvidence evidence)
    {
        ArgumentNullException.ThrowIfNull(evidence);
        return new GpsEvaluation(GpsEvaluationState.Match, evidence);
    }
}
