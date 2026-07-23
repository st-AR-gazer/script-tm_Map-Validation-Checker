namespace MapValidationChecker.Cli.Application;

internal sealed record MapProcessingOptions(
    bool IncludePath,
    bool IncludeMapName,
    bool GpsEnabled,
    bool StrictGps,
    int GpsThresholdMs,
    bool DataDump,
    int? MaxDepth);
