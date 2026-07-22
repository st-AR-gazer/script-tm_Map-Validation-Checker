namespace MapValidationChecker.Core.Validation;

/// <summary>
/// Is either a terminal outcome or a request for the caller to evaluate GPS evidence.
/// </summary>
public sealed class ValidationEvaluation
{
    private ValidationEvaluation(ValidationOutcome? outcome, bool requiresGpsEvidence)
    {
        Outcome = outcome;
        RequiresGpsEvidence = requiresGpsEvidence;
    }

    public ValidationOutcome? Outcome { get; }
    public bool RequiresGpsEvidence { get; }

    internal static ValidationEvaluation Completed(ValidationOutcome outcome)
    {
        ArgumentNullException.ThrowIfNull(outcome);
        return new ValidationEvaluation(outcome, requiresGpsEvidence: false);
    }

    internal static ValidationEvaluation NeedsGpsEvidence() =>
        new(outcome: null, requiresGpsEvidence: true);
}
