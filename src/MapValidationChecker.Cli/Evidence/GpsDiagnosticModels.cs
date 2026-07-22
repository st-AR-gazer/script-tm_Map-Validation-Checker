namespace MapValidationChecker.Cli.Evidence;

internal sealed record GpsCandidate(int TimeMs, string Source);

internal sealed record GpsRecordDataEntryDump(
    string Path,
    int? EntListCount,
    bool IsNull,
    int? U01,
    int? U02,
    int? U03,
    int SamplesCount,
    int? LastSampleIndex,
    int? LastSampleTimeMs,
    int Samples2Count,
    int? LastSample2Index,
    int? LastSample2TimeMs);

internal sealed record EntListSummary(
    string Path,
    int? Count,
    List<int?>? SampledU03);

internal sealed record EntRecordElemDump(
    string Path,
    int? U03,
    int? U03MinusCountdownMs);
