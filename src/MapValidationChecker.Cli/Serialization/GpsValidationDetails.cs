using MapValidationChecker.Core.Validation;

namespace MapValidationChecker.Cli.Serialization;

internal sealed class GpsValidationDetails
{
    public string? MatchType { get; set; }
    public string? Method { get; set; }
    public int AuthorTimeMs { get; set; }
    public int MatchedTimeMs { get; set; }
    public int DeltaMs { get; set; }
    public int? ThresholdMs { get; set; }
    public string? Source { get; set; }

    public static GpsValidationDetails FromEvidence(
        int authorTimeMs,
        int thresholdMs,
        GpsEvidence evidence)
    {
        ArgumentNullException.ThrowIfNull(evidence);

        var isExact = evidence.Kind == GpsMatchKind.ExactMatch;
        return new GpsValidationDetails
        {
            MatchType = isExact ? "exact_match" : "within_threshold",
            Method = evidence.Method switch
            {
                GpsMatchMethod.U05Exact => "u05_exact",
                GpsMatchMethod.U03Threshold => "u03_threshold",
                GpsMatchMethod.U03MinusCountdownThreshold => "u03_minus_countdown_threshold",
                _ => "u03_threshold"
            },
            AuthorTimeMs = authorTimeMs,
            MatchedTimeMs = evidence.GpsTimeMs,
            DeltaMs = evidence.DeltaMs,
            ThresholdMs = isExact ? null : thresholdMs,
            Source = evidence.Source
        };
    }
}
