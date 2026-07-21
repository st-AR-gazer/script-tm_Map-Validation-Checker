namespace MapValidationChecker.Core.Validation;

/// <summary>
/// Identifies the evidence or fallback that produced a validation decision.
/// </summary>
public enum ValidationType
{
    Normal,
    Plugin,
    ValidationGhost,
    ValidationTag,
    Gps,
    Replay,
    Manual
}
