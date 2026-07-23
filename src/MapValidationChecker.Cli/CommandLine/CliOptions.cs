namespace MapValidationChecker.Cli.CommandLine;

internal enum RunMode
{
    Single,
    Batch
}

internal sealed record CliOptions(
    RunMode Mode,
    string MapPath,
    string? ReplaysPath,
    string? ManualPath,
    bool Recursive,
    bool Pretty,
    bool IncludePath,
    bool IncludeMapName,
    bool Progress,
    double ProgressIntervalSeconds,
    string? OutputPath,
    bool GpsEnabled,
    bool StrictGps,
    int GpsThresholdMs,
    bool DataDump,
    int? MaxDepth);
