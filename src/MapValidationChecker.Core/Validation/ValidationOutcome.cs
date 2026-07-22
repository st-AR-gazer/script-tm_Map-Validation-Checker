namespace MapValidationChecker.Core.Validation;

/// <summary>
/// Represents the terminal validation decision for a successfully parsed map.
/// </summary>
public sealed record ValidationOutcome(
    ValidationStatus Status,
    ValidationType Type,
    string? Note = null,
    string? Error = null,
    string? ReplayPath = null,
    GpsEvidence? GpsEvidence = null);
